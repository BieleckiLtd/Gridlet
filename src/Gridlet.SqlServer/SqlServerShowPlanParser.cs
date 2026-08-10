using System.Globalization;
using System.Xml.Linq;
using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>
/// Turns SQL Server's ShowPlan XML into Gridlet's plan tree. The XML is faithful but unreadable; the
/// tree keeps what somebody looking at a slow query actually reads - the operator, what it touches,
/// its share of the cost, how far the row estimate was off, and any warning the engine attached.
/// </summary>
public static class SqlServerShowPlanParser
{
    private const string ShowPlanNamespace = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    /// <summary>Parses one or more ShowPlan documents into a root per statement.</summary>
    /// <param name="xml">The plan XML, or several concatenated documents.</param>
    public static IReadOnlyList<QueryPlanNode> Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(WrapConcatenatedDocuments(xml));
        }
        catch (System.Xml.XmlException)
        {
            // A plan Gridlet cannot read is still worth showing as raw text, so this is not an error.
            return [];
        }

        XNamespace ns = ShowPlanNamespace;
        var statements = new List<QueryPlanNode>();
        foreach (var statement in document.Descendants(ns + "StmtSimple"))
        {
            var relOp = statement.Elements(ns + "QueryPlan").Elements(ns + "RelOp").FirstOrDefault();
            var children = relOp is null ? [] : new[] { ParseOperator(relOp, ns) };
            statements.Add(new QueryPlanNode(
                Operation: Attribute(statement, "StatementType") ?? "Statement",
                Detail: Collapse(Attribute(statement, "StatementText")),
                EstimatedRows: Number(statement, "StatementEstRows"),
                EstimatedCost: Number(statement, "StatementSubTreeCost"),
                Warnings: StatementWarnings(statement, ns),
                Children: children));
        }

        return statements;
    }

    /// <summary>
    /// SQL Server emits one document per statement, so a multi-statement batch produces several
    /// concatenated roots. They are wrapped into one document rather than parsed separately.
    /// </summary>
    private static string WrapConcatenatedDocuments(string xml)
    {
        var trimmed = xml.Trim();
        var firstRoot = trimmed.IndexOf("<ShowPlanXML", StringComparison.OrdinalIgnoreCase);
        var secondRoot = firstRoot < 0
            ? -1
            : trimmed.IndexOf("<ShowPlanXML", firstRoot + 1, StringComparison.OrdinalIgnoreCase);
        return secondRoot < 0 ? trimmed : $"<GridletPlans>{trimmed}</GridletPlans>";
    }

    private static QueryPlanNode ParseOperator(XElement relOp, XNamespace ns)
    {
        var children = relOp
            .Elements()
            .SelectMany(element => element.Elements(ns + "RelOp"))
            .Select(child => ParseOperator(child, ns))
            .ToArray();

        return new QueryPlanNode(
            Operation: Attribute(relOp, "PhysicalOp") ?? "Operator",
            Detail: DescribeTarget(relOp, ns),
            EstimatedRows: Number(relOp, "EstimateRows"),
            ActualRows: ActualRows(relOp, ns),
            EstimatedCost: Number(relOp, "EstimatedTotalSubtreeCost"),
            Warnings: OperatorWarnings(relOp, ns),
            Children: children);
    }

    /// <summary>Names what the operator reads, preferring the index over the bare table.</summary>
    private static string? DescribeTarget(XElement relOp, XNamespace ns)
    {
        var objectElement = OwnDescendants(relOp, ns).FirstOrDefault(
            element => element.Name == ns + "Object");
        if (objectElement is null)
        {
            var logical = Attribute(relOp, "LogicalOp");
            return string.Equals(logical, Attribute(relOp, "PhysicalOp"), StringComparison.Ordinal)
                ? null
                : logical;
        }

        var table = Trim(Attribute(objectElement, "Table"));
        var index = Trim(Attribute(objectElement, "Index"));
        return (table, index) switch
        {
            (null, null) => null,
            (not null, null) => table,
            (null, not null) => index,
            _ => $"{table}.{index}",
        };
    }

    /// <summary>
    /// The elements belonging to this operator, stopping at nested operators. Without the boundary
    /// an operator would describe itself with whatever its first child touches - a join would claim
    /// to read the table its left input reads.
    /// </summary>
    private static IEnumerable<XElement> OwnDescendants(XElement relOp, XNamespace ns)
    {
        foreach (var child in relOp.Elements())
        {
            if (child.Name == ns + "RelOp")
            {
                continue;
            }

            yield return child;
            foreach (var descendant in OwnDescendants(child, ns))
            {
                yield return descendant;
            }
        }
    }

    private static double? ActualRows(XElement relOp, XNamespace ns)
    {
        var counters = relOp
            .Elements(ns + "RunTimeInformation")
            .Elements(ns + "RunTimeCountersPerThread")
            .Select(counter => Number(counter, "ActualRows"))
            .Where(value => value is not null)
            .ToArray();
        return counters.Length == 0 ? null : counters.Sum(value => value!.Value);
    }

    private static IReadOnlyList<string>? OperatorWarnings(XElement relOp, XNamespace ns)
    {
        var warnings = relOp.Elements(ns + "Warnings").Elements()
            .Select(DescribeWarning)
            .Where(warning => warning is not null)
            .Select(warning => warning!)
            .ToArray();
        return warnings.Length == 0 ? null : warnings;
    }

    private static IReadOnlyList<string>? StatementWarnings(XElement statement, XNamespace ns)
    {
        var warnings = new List<string>();
        foreach (var missingIndex in statement.Descendants(ns + "MissingIndex"))
        {
            var table = Trim(Attribute(missingIndex, "Table"));
            var columns = missingIndex.Descendants(ns + "Column")
                .Select(column => Attribute(column, "Name"))
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => Trim(name)!)
                .ToArray();
            warnings.Add(columns.Length == 0
                ? $"Missing index on {table}"
                : $"Missing index on {table} ({string.Join(", ", columns)})");
        }

        return warnings.Count == 0 ? null : warnings;
    }

    private static string? DescribeWarning(XElement warning)
        => warning.Name.LocalName switch
        {
            "SpillToTempDb" => "Spilled to tempdb",
            "ColumnsWithNoStatistics" => "Columns with no statistics",
            "PlanAffectingConvert" => "Conversion affected the plan: "
                + (Attribute(warning, "Expression") ?? Attribute(warning, "ConvertIssue") ?? ""),
            "Warnings" => null,
            _ => Attribute(warning, "Message") ?? warning.Name.LocalName,
        };

    private static string? Attribute(XElement element, string name)
        => element.Attribute(name)?.Value;

    private static double? Number(XElement element, string name)
        => double.TryParse(
            element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim('[', ']');

    /// <summary>Statement text is multi-line SQL; the tree shows it as one line.</summary>
    private static string? Collapse(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

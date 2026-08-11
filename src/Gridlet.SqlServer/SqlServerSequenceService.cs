using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

internal static class SqlServerSequenceService
{
    private static readonly HashSet<string> Types =
        new(["tinyint", "smallint", "int", "bigint", "decimal", "numeric"], StringComparer.OrdinalIgnoreCase);

    public static async Task CreateAsync(
        GridletConnectionContext context, SequenceDesign design, CancellationToken cancellationToken)
        => await ExecuteAsync(context, BuildCreate(design), cancellationToken);

    internal static string BuildCreate(SequenceDesign design)
    {
        var type = NormalizeType(design.DataType);
        var start = Number(design.StartValue, "start value");
        var increment = Number(design.Increment, "increment");
        if (System.Numerics.BigInteger.Parse(increment, System.Globalization.CultureInfo.InvariantCulture).IsZero)
            throw new GridletValidationException("A sequence increment cannot be zero.");
        var minimum = string.IsNullOrWhiteSpace(design.MinimumValue)
            ? "NO MINVALUE" : $"MINVALUE {Number(design.MinimumValue, "minimum value")}";
        var maximum = string.IsNullOrWhiteSpace(design.MaximumValue)
            ? "NO MAXVALUE" : $"MAXVALUE {Number(design.MaximumValue, "maximum value")}";
        if (design.CacheSize is < 1) throw new GridletValidationException("Sequence cache size must be positive.");
        var cache = design.IsCached
            ? design.CacheSize is null ? "CACHE" : $"CACHE {design.CacheSize.Value}"
            : "NO CACHE";
        return $"CREATE SEQUENCE {SqlServerIdentifier.QuoteQualified(design.Schema, design.Name)} AS {type} " +
            $"START WITH {start} INCREMENT BY {increment} {minimum} {maximum} " +
            $"{(design.IsCycling ? "CYCLE" : "NO CYCLE")} {cache};";
    }

    public static Task RestartAsync(
        GridletConnectionContext context, string schema, string name, string value,
        CancellationToken cancellationToken)
        => ExecuteAsync(context, BuildRestart(schema, name, value), cancellationToken);

    internal static string BuildRestart(string schema, string name, string value)
        => $"ALTER SEQUENCE {SqlServerIdentifier.QuoteQualified(schema, name)} RESTART WITH {Number(value, "restart value")};";

    private static string NormalizeType(string value)
    {
        var type = value.Trim();
        var baseType = type.Split('(', 2)[0].Trim();
        var decimalMatch = System.Text.RegularExpressions.Regex.Match(type,
            @"^(?:decimal|numeric)(?:\(\s*(\d{1,2})\s*,\s*0\s*\))?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!Types.Contains(baseType) ||
            (baseType is "decimal" or "numeric" &&
             (!decimalMatch.Success || (decimalMatch.Groups[1].Success &&
                 int.Parse(decimalMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) is < 1 or > 38))) ||
            (baseType is not ("decimal" or "numeric") && !type.Equals(baseType, StringComparison.OrdinalIgnoreCase)))
            throw new GridletValidationException($"Sequence type '{value}' is not supported.");
        return type;
    }

    private static string Number(string? value, string label)
    {
        var trimmed = value?.Trim() ?? "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[+-]?\d+$"))
            throw new GridletValidationException($"Sequence {label} must be an integer.");
        return trimmed;
    }

    private static async Task ExecuteAsync(
        GridletConnectionContext context, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException ex) { throw new GridletQueryException(ex.Message, ex); }
    }
}

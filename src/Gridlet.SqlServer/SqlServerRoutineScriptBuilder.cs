using System.Globalization;
using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>
/// Builds the script that calls a stored procedure or function. Gridlet hands the script to the
/// query editor rather than executing it invisibly: the person sees exactly what will run, can edit
/// it before it does, and keeps it afterwards. Output parameters and the return value are declared
/// and selected at the end, so results that are not a result set are still on screen.
/// </summary>
public static class SqlServerRoutineScriptBuilder
{
    /// <summary>Builds the call script for <paramref name="routine"/>.</summary>
    public static string Build(
        RoutineDefinition routine,
        IReadOnlyDictionary<string, RoutineArgument> arguments)
    {
        var target = SqlServerIdentifier.QuoteQualified(routine.Object.Schema, routine.Object.Name);
        var parameters = routine.Parameters
            .Where(parameter => !parameter.IsReturnValue)
            .OrderBy(parameter => parameter.Ordinal)
            .ToArray();

        return routine.Object.Type switch
        {
            DbObjectType.StoredProcedure => BuildProcedure(routine, target, parameters, arguments),
            DbObjectType.ScalarFunction =>
                $"SELECT {target}({BuildFunctionArguments(parameters, arguments)}) AS [Result];",
            DbObjectType.TableValuedFunction =>
                $"SELECT * FROM {target}({BuildFunctionArguments(parameters, arguments)});",
            _ => throw new GridletValidationException(
                $"{target} is not a stored procedure or function."),
        };
    }

    private static string BuildProcedure(
        RoutineDefinition routine,
        string target,
        IReadOnlyList<RoutineParameterInfo> parameters,
        IReadOnlyDictionary<string, RoutineArgument> arguments)
    {
        var script = new List<string>();
        var outputs = parameters.Where(parameter => parameter.IsOutput).ToArray();
        var capturesReturnValue = routine.Parameters.Any(parameter => parameter.IsReturnValue);

        if (capturesReturnValue)
        {
            script.Add("DECLARE @ReturnValue int;");
        }

        foreach (var output in outputs)
        {
            var local = LocalName(output);
            script.Add(TryGetArgument(output, arguments, out var argument) && !argument.IsNull
                ? $"DECLARE {local} {output.DataType} = {Literal(output, argument)};"
                : $"DECLARE {local} {output.DataType};");
        }

        var call = new List<string>();
        foreach (var parameter in parameters)
        {
            if (parameter.IsOutput)
            {
                call.Add($"{parameter.Name} = {LocalName(parameter)} OUTPUT");
                continue;
            }

            // A parameter with no argument is left out of the call, so the routine's own default
            // applies. That is not the same as passing NULL, and the difference matters.
            if (TryGetArgument(parameter, arguments, out var argument))
            {
                call.Add($"{parameter.Name} = {Literal(parameter, argument)}");
            }
        }

        var arguments_ = call.Count == 0 ? "" : " " + string.Join(", ", call);
        script.Add(capturesReturnValue
            ? $"EXEC @ReturnValue = {target}{arguments_};"
            : $"EXEC {target}{arguments_};");

        var selected = new List<string>();
        if (capturesReturnValue)
        {
            selected.Add("@ReturnValue AS [Return value]");
        }

        selected.AddRange(outputs.Select(output =>
            $"{LocalName(output)} AS {SqlServerIdentifier.Quote(output.Name)}"));
        if (selected.Count > 0)
        {
            script.Add($"SELECT {string.Join(", ", selected)};");
        }

        return string.Join("\n", script);
    }

    private static string BuildFunctionArguments(
        IReadOnlyList<RoutineParameterInfo> parameters,
        IReadOnlyDictionary<string, RoutineArgument> arguments)
        // A function call is positional, so an omitted argument has to become DEFAULT rather than
        // disappear; skipping it would shift every argument after it onto the wrong parameter.
        => string.Join(", ", parameters.Select(parameter =>
            TryGetArgument(parameter, arguments, out var argument)
                ? Literal(parameter, argument)
                : "DEFAULT"));

    private static bool TryGetArgument(
        RoutineParameterInfo parameter,
        IReadOnlyDictionary<string, RoutineArgument> arguments,
        out RoutineArgument argument)
        => arguments.TryGetValue(parameter.Name, out argument!)
            || arguments.TryGetValue(parameter.Name.TrimStart('@'), out argument!);

    /// <summary>Renders one argument as a literal of the parameter's declared type.</summary>
    private static string Literal(RoutineParameterInfo parameter, RoutineArgument argument)
    {
        if (argument.IsNull || argument.Value is null)
        {
            return "NULL";
        }

        if (argument.IsRawSql)
        {
            return argument.Value;
        }

        var value = argument.Value;
        var baseType = parameter.DataType.Split('(')[0].Trim().ToLowerInvariant();
        return baseType switch
        {
            "bit" => value.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "yes" or "on" => "1",
                "0" or "false" or "no" or "off" => "0",
                _ => throw new GridletValidationException(
                    $"Parameter {parameter.Name} takes a bit; '{value}' is not 0 or 1."),
            },
            "tinyint" or "smallint" or "int" or "bigint" or "decimal" or "numeric" or "money"
                or "smallmoney" or "float" or "real" => Number(parameter, value),
            "binary" or "varbinary" or "image" or "rowversion" or "timestamp" => Binary(parameter, value),
            "uniqueidentifier" => Guid.TryParse(value.Trim(), out var guid)
                ? $"'{guid:D}'"
                : throw new GridletValidationException(
                    $"Parameter {parameter.Name} takes a uniqueidentifier; '{value}' is not one."),
            "char" or "varchar" or "text" => $"'{value.Replace("'", "''")}'",
            _ => $"N'{value.Replace("'", "''")}'",
        };
    }

    private static string Number(RoutineParameterInfo parameter, string value)
    {
        var trimmed = value.Trim();
        return decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? trimmed
            : throw new GridletValidationException(
                $"Parameter {parameter.Name} takes {parameter.DataType}; '{value}' is not a number.");
    }

    private static string Binary(RoutineParameterInfo parameter, string value)
    {
        var trimmed = value.Trim();
        var digits = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed[2..] : trimmed;
        return digits.Length > 0 && digits.Length % 2 == 0
            && digits.All(Uri.IsHexDigit)
                ? "0x" + digits
                : throw new GridletValidationException(
                    $"Parameter {parameter.Name} takes {parameter.DataType}; '{value}' is not hexadecimal.");
    }

    /// <summary>
    /// The local variable that receives an output parameter. It is named after the parameter so the
    /// script reads the way somebody would write it by hand.
    /// </summary>
    private static string LocalName(RoutineParameterInfo parameter)
        => "@out_" + parameter.Name.TrimStart('@');
}

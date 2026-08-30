using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

internal static class SqlServerTriggerService
{
    private sealed record TriggerAccumulator(
        string Name,
        string Scope,
        bool IsDisabled,
        string? Definition,
        string? Schema,
        string? ParentSchema,
        string? ParentName,
        List<string> Events);

    public static async Task<IReadOnlyList<TriggerInfo>> GetAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT trigger_object.name, trigger_schema.name, parent_schema.name, parent_object.name,
                   trg.is_disabled, mod.definition,
                   CONVERT(bit, OBJECTPROPERTYEX(trg.object_id, 'ExecIsInsertTrigger')),
                   CONVERT(bit, OBJECTPROPERTYEX(trg.object_id, 'ExecIsUpdateTrigger')),
                   CONVERT(bit, OBJECTPROPERTYEX(trg.object_id, 'ExecIsDeleteTrigger'))
            FROM sys.triggers trg
            JOIN sys.objects trigger_object ON trigger_object.object_id = trg.object_id
            JOIN sys.schemas trigger_schema ON trigger_schema.schema_id = trigger_object.schema_id
            JOIN sys.objects parent_object ON parent_object.object_id = trg.parent_id
            JOIN sys.schemas parent_schema ON parent_schema.schema_id = parent_object.schema_id
            LEFT JOIN sys.sql_modules mod ON mod.object_id = trg.object_id
            WHERE trg.parent_class = 1 AND trg.is_ms_shipped = 0
            ORDER BY trigger_schema.name, trigger_object.name;

            SELECT trg.name, trg.is_disabled, mod.definition, evt.type_desc
            FROM sys.triggers trg
            LEFT JOIN sys.sql_modules mod ON mod.object_id = trg.object_id
            LEFT JOIN sys.trigger_events evt ON evt.object_id = trg.object_id
            WHERE trg.parent_class = 0 AND trg.is_ms_shipped = 0
            ORDER BY trg.name, evt.type_desc;

            SELECT trg.name, trg.is_disabled, mod.definition, evt.type_desc
            FROM sys.server_triggers trg
            LEFT JOIN sys.server_sql_modules mod ON mod.object_id = trg.object_id
            LEFT JOIN sys.server_trigger_events evt ON evt.object_id = trg.object_id
            WHERE trg.is_ms_shipped = 0
            ORDER BY trg.name, evt.type_desc;
            """;

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        try
        {
            var triggers = new List<TriggerInfo>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var events = new List<string>();
                if (reader.GetBoolean(6)) events.Add("INSERT");
                if (reader.GetBoolean(7)) events.Add("UPDATE");
                if (reader.GetBoolean(8)) events.Add("DELETE");
                triggers.Add(new TriggerInfo(
                    reader.GetString(0), TriggerScopes.Object, reader.GetBoolean(4), events,
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }

            await reader.NextResultAsync(cancellationToken);
            triggers.AddRange(await ReadDdlTriggersAsync(reader, TriggerScopes.Database, cancellationToken));
            await reader.NextResultAsync(cancellationToken);
            triggers.AddRange(await ReadDdlTriggersAsync(reader, TriggerScopes.Server, cancellationToken));
            return triggers;
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task<IReadOnlyList<TriggerInfo>> ReadDdlTriggersAsync(
        SqlDataReader reader,
        string scope,
        CancellationToken cancellationToken)
    {
        var grouped = new Dictionary<string, TriggerAccumulator>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(0);
            if (!grouped.TryGetValue(name, out var trigger))
            {
                trigger = new TriggerAccumulator(name, scope, reader.GetBoolean(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), null, null, null, []);
                grouped.Add(name, trigger);
            }
            if (!reader.IsDBNull(3)) trigger.Events.Add(reader.GetString(3));
        }
        return grouped.Values.Select(trigger => new TriggerInfo(
            trigger.Name, trigger.Scope, trigger.IsDisabled, trigger.Events,
            trigger.Definition, trigger.Schema, trigger.ParentSchema, trigger.ParentName)).ToArray();
    }

    public static string BuildSetEnabled(TriggerStateDesign trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger.Name))
        {
            throw new GridletValidationException("A trigger name is required.");
        }

        var action = trigger.Enabled ? "ENABLE" : "DISABLE";
        var name = trigger.Scope == TriggerScopes.Object
            ? SqlServerIdentifier.QuoteQualified(
                Require(trigger.Schema, "An object trigger needs its schema."), trigger.Name)
            : SqlServerIdentifier.Quote(trigger.Name);
        var target = trigger.Scope switch
        {
            TriggerScopes.Object => SqlServerIdentifier.QuoteQualified(
                Require(trigger.ParentSchema, "An object trigger needs its parent schema."),
                Require(trigger.ParentName, "An object trigger needs its parent object.")),
            TriggerScopes.Database => "DATABASE",
            TriggerScopes.Server => "ALL SERVER",
            _ => throw new GridletValidationException($"Unknown trigger scope '{trigger.Scope}'."),
        };
        return $"{action} TRIGGER {name} ON {target};";
    }

    public static async Task SetEnabledAsync(
        GridletConnectionContext context,
        TriggerStateDesign trigger,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = BuildSetEnabled(trigger);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static string Require(string? value, string message)
        => string.IsNullOrWhiteSpace(value) ? throw new GridletValidationException(message) : value;
}

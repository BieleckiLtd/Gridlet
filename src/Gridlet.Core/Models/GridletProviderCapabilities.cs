namespace Gridlet.Models;

/// <summary>
/// Describes database-engine features that affect the provider-agnostic UI. Providers expose
/// these values so the browser does not need to infer SQL capabilities from a provider name.
/// </summary>
public sealed record GridletProviderCapabilities(
    string DefaultSchema,
    bool SupportsSchemas,
    bool SupportsViews,
    bool SupportsStoredProcedures,
    bool SupportsFunctions,
    bool SupportsTriggers,
    bool SupportsClusteredPrimaryKeys,
    IReadOnlyList<string> SuggestedDataTypes,
    string SelectExample,
    string CreateTriggerExample,
    string ObjectEditMode,
    bool SupportsCheckConstraints = false,
    bool SupportsUniqueConstraints = false,
    bool SupportsIndexes = false)
{
    /// <summary>Creates the legacy eleven-field capability shape without relying on optional-parameter ABI.</summary>
    public GridletProviderCapabilities(
        string defaultSchema,
        bool supportsSchemas,
        bool supportsViews,
        bool supportsStoredProcedures,
        bool supportsFunctions,
        bool supportsTriggers,
        bool supportsClusteredPrimaryKeys,
        IReadOnlyList<string> suggestedDataTypes,
        string selectExample,
        string createTriggerExample,
        string objectEditMode)
        : this(defaultSchema, supportsSchemas, supportsViews, supportsStoredProcedures,
            supportsFunctions, supportsTriggers, supportsClusteredPrimaryKeys, suggestedDataTypes,
            selectExample, createTriggerExample, objectEditMode, false, false, false)
    {
    }

    /// <summary>Deconstructs the legacy eleven-field capability shape.</summary>
    public void Deconstruct(
        out string defaultSchema,
        out bool supportsSchemas,
        out bool supportsViews,
        out bool supportsStoredProcedures,
        out bool supportsFunctions,
        out bool supportsTriggers,
        out bool supportsClusteredPrimaryKeys,
        out IReadOnlyList<string> suggestedDataTypes,
        out string selectExample,
        out string createTriggerExample,
        out string objectEditMode)
    {
        defaultSchema = DefaultSchema;
        supportsSchemas = SupportsSchemas;
        supportsViews = SupportsViews;
        supportsStoredProcedures = SupportsStoredProcedures;
        supportsFunctions = SupportsFunctions;
        supportsTriggers = SupportsTriggers;
        supportsClusteredPrimaryKeys = SupportsClusteredPrimaryKeys;
        suggestedDataTypes = SuggestedDataTypes;
        selectExample = SelectExample;
        createTriggerExample = CreateTriggerExample;
        objectEditMode = ObjectEditMode;
    }
}

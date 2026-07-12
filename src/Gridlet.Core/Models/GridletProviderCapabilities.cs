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
    string ObjectEditMode);

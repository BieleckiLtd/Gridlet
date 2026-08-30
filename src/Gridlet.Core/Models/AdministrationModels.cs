namespace Gridlet.Models;

/// <summary>A database user or role visible to the current identity.</summary>
public sealed record DatabasePrincipalInfo(
    string Name,
    string Type,
    string? AuthenticationType = null,
    string? DefaultSchema = null,
    bool IsFixedRole = false,
    bool IsSystem = false);

/// <summary>Membership of one database principal in a database role.</summary>
public sealed record DatabaseRoleMembershipInfo(string Role, string Member);

/// <summary>An explicit GRANT, DENY, or REVOKE-WITH-GRANT entry in the database catalog.</summary>
public sealed record DatabasePermissionInfo(
    string Grantee,
    string Grantor,
    string State,
    string Permission,
    string Scope,
    string? Securable = null);

/// <summary>A permission the current connection identity effectively has.</summary>
public sealed record EffectivePermissionInfo(string Scope, string Permission);

/// <summary>Database security metadata visible to the current connection identity.</summary>
public sealed record DatabaseSecurityOverview(
    string CurrentUser,
    string Login,
    string OriginalLogin,
    IReadOnlyList<DatabasePrincipalInfo> Principals,
    IReadOnlyList<DatabaseRoleMembershipInfo> RoleMemberships,
    IReadOnlyList<DatabasePermissionInfo> ExplicitPermissions,
    IReadOnlyList<EffectivePermissionInfo> EffectivePermissions);

/// <summary>The scope values used by <see cref="TriggerInfo"/>.</summary>
public static class TriggerScopes
{
    public const string Object = "object";
    public const string Database = "database";
    public const string Server = "server";
}

/// <summary>A DML, database DDL, or server DDL trigger.</summary>
public sealed record TriggerInfo(
    string Name,
    string Scope,
    bool IsDisabled,
    IReadOnlyList<string> Events,
    string? Definition = null,
    string? Schema = null,
    string? ParentSchema = null,
    string? ParentName = null);

/// <summary>Identifies a trigger and requests its enabled state.</summary>
public sealed record TriggerStateDesign(
    string Name,
    string Scope,
    bool Enabled,
    string? Schema = null,
    string? ParentSchema = null,
    string? ParentName = null);

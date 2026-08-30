using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

internal static class SqlServerSecurityService
{
    public static async Task<DatabaseSecurityOverview> GetOverviewAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT USER_NAME(), SUSER_SNAME(), ORIGINAL_LOGIN();

            SELECT p.name, p.type_desc, p.authentication_type_desc, p.default_schema_name,
                   p.is_fixed_role,
                   CONVERT(bit, CASE WHEN p.principal_id <= 4 OR p.name IN (N'INFORMATION_SCHEMA', N'sys') THEN 1 ELSE 0 END)
            FROM sys.database_principals p
            WHERE p.type IN ('S', 'U', 'G', 'E', 'X', 'R', 'C', 'K')
            ORDER BY p.is_fixed_role DESC, p.name;

            SELECT role.name, member.name
            FROM sys.database_role_members membership
            JOIN sys.database_principals role ON role.principal_id = membership.role_principal_id
            JOIN sys.database_principals member ON member.principal_id = membership.member_principal_id
            ORDER BY role.name, member.name;

            SELECT grantee.name, grantor.name, permission.state_desc, permission.permission_name,
                   permission.class_desc,
                   CASE permission.class
                     WHEN 0 THEN DB_NAME()
                     WHEN 1 THEN CONCAT(QUOTENAME(OBJECT_SCHEMA_NAME(permission.major_id)), N'.',
                                        QUOTENAME(OBJECT_NAME(permission.major_id)),
                                        CASE WHEN permission.minor_id > 0
                                             THEN CONCAT(N'.', QUOTENAME(COL_NAME(permission.major_id, permission.minor_id)))
                                             ELSE N'' END)
                     WHEN 3 THEN QUOTENAME(SCHEMA_NAME(permission.major_id))
                     WHEN 4 THEN QUOTENAME(USER_NAME(permission.major_id))
                     ELSE CONCAT(permission.class_desc, N':', permission.major_id)
                   END
            FROM sys.database_permissions permission
            JOIN sys.database_principals grantee ON grantee.principal_id = permission.grantee_principal_id
            JOIN sys.database_principals grantor ON grantor.principal_id = permission.grantor_principal_id
            ORDER BY grantee.name, permission.class_desc, permission.permission_name;

            SELECT N'DATABASE', permission_name
            FROM fn_my_permissions(NULL, N'DATABASE')
            ORDER BY permission_name;
            """;

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new GridletQueryException("SQL Server did not return the current security identity.");
            }

            var currentUser = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var login = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var originalLogin = reader.IsDBNull(2) ? "" : reader.GetString(2);

            var principals = new List<DatabasePrincipalInfo>();
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                principals.Add(new DatabasePrincipalInfo(
                    reader.GetString(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetBoolean(4), reader.GetBoolean(5)));
            }

            var memberships = new List<DatabaseRoleMembershipInfo>();
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                memberships.Add(new DatabaseRoleMembershipInfo(reader.GetString(0), reader.GetString(1)));
            }

            var explicitPermissions = new List<DatabasePermissionInfo>();
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                explicitPermissions.Add(new DatabasePermissionInfo(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
            }

            var effectivePermissions = new List<EffectivePermissionInfo>();
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                effectivePermissions.Add(new EffectivePermissionInfo(reader.GetString(0), reader.GetString(1)));
            }

            return new DatabaseSecurityOverview(currentUser, login, originalLogin, principals,
                memberships, explicitPermissions, effectivePermissions);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }
}

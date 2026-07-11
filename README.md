<p align="center">
  <img src="https://raw.githubusercontent.com/BieleckiLtd/Gridlet/main/assets/gridlet-icon.png" width="160" height="160" alt="Gridlet logo" />
</p>

<h1 align="center">Gridlet</h1>

Gridlet is an embeddable ASP.NET Core NuGet package that adds a configurable, web-based SQL Server
management interface to an existing application — browse schema, view and page through data, inspect
keys/indexes/relationships, and run queries, all from inside the host app using the host's own
authentication, authorization, routing, logging, and deployment model.

## Quick start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGridlet(options =>
    {
        options.AddConnection("Default", builder.Configuration.GetConnectionString("Default")!);
        options.Security.AuthorizationPolicy = "DbAdmins"; // or leave null for the default policy
        options.Limits.MaxQueryResultRows = 10_000; // server-enforced maximum for the UI row-cap control
    })
    .AddSqlServer();

var app = builder.Build();

app.MapGridlet("/gridlet"); // mount path is configurable

app.Run();
```

Browse to `/gridlet`. Every Gridlet endpoint (UI and API) requires authorization by default;
`options.Security.AllowAnonymous = true` exists for local development only.

## Developer configuration reference

`AddGridlet` registers Gridlet and accepts an optional `Action<GridletOptions>`. Register at least
one connection, then chain the provider package that matches its `ProviderName`:

```csharp
builder.Services
    .AddGridlet(options =>
    {
        options.AddConnection(
            name: "Reporting",
            connectionString: builder.Configuration.GetConnectionString("Reporting")!,
            providerName: GridletProviderNames.SqlServer,
            configure: connection =>
            {
                connection.AllowSqlExecution = true;
                connection.AllowWrites = false;
                connection.AllowDdl = false;
            });

        options.Limits.DefaultPageSize = 50;
        options.Limits.MaxPageSize = 500;
        options.Limits.MaxQueryResultRows = 10_000;
        options.Limits.CommandTimeoutSeconds = 30;

        options.Security.AllowAnonymous = false;
        options.Security.AuthorizationPolicy = "DbAdmins";

        options.Storage.FilePath = "App_Data/gridlet-store.json";
    })
    .AddSqlServer();
```

### `GridletOptions`

| Property | Default | Effect |
| --- | --- | --- |
| `Connections` | Empty | Explicit allow-list of connections exposed by Gridlet. Gridlet does not automatically expose the host application's other connection strings. Use `AddConnection(...)` to populate it. |
| `Limits` | New `GridletLimitsOptions` | Server-side paging, result-size, and timeout protections. |
| `Security` | New `GridletSecurityOptions` | Authentication and authorization applied to the entire Gridlet route group. |
| `Storage` | New `GridletStorageOptions` | Persistence settings for saved queries and published endpoint definitions. |

`AddConnection(name, connectionString, providerName, configure)` has the following arguments:

| Argument | Effect |
| --- | --- |
| `name` | Unique, case-insensitive name displayed in the UI and embedded in API routes. |
| `connectionString` | Provider-specific connection string used only on the server; it is never returned to the browser. Use a least-privileged database identity. |
| `providerName` | Provider implementation used for this connection. Defaults to `GridletProviderNames.SqlServer`; chain `.AddSqlServer()` to register it. |
| `configure` | Optional callback for the connection feature gates described below. |

### Per-connection options

| Property | Default | Effect |
| --- | --- | --- |
| `Name` | Empty | Display and route name. Normally set by `AddConnection`. Must be non-empty and unique. |
| `ConnectionString` | Empty | Secret server-side database connection string. Normally set by `AddConnection` and never exposed by Gridlet APIs. |
| `ProviderName` | `SqlServer` | Selects the registered `IGridletProvider`. |
| `AllowSqlExecution` | `true` | Shows and enables the ad-hoc SQL editor. This permits any statement allowed by the database login, including writes or DDL; it is independent of the two UI feature gates below. |
| `AllowWrites` | `true` | Enables Gridlet's explicit row insert/update/delete UI and endpoints. It does not prevent write statements submitted through the SQL editor. |
| `AllowDdl` | `true` | Enables Gridlet's schema/table designer UI and endpoints. It does not prevent DDL submitted through the SQL editor. |

### Limit options

| Property | Default | Effect |
| --- | --- | --- |
| `DefaultPageSize` | `50` | Default size for the paged table-data API retained for API consumers. The interactive UI uses streaming. Must be at least 1. |
| `MaxPageSize` | `500` | Server-enforced upper bound for paged browse requests and the batch size used by streamed table/view browsing. Must be at least `DefaultPageSize`. |
| `MaxQueryResultRows` | `10,000` | Server-enforced maximum rows retained per query result set or streamed table/view. The in-app **Row cap** can request a lower value and persists per browser, but can never exceed this value. Results stream progressively and virtualize above 1,000 rows; the cap still protects server and browser memory. |
| `CommandTimeoutSeconds` | `30` | Provider command timeout for query execution. The user can cancel sooner with the query toolbar's Cancel button. Must be at least 1. |

### Security options

| Property | Default | Effect |
| --- | --- | --- |
| `AllowAnonymous` | `false` | When false, `MapGridlet` applies ASP.NET Core authorization to every UI, API, asset, and published endpoint under the mount path. Set true only when anonymous database tooling is intentional, typically local development. |
| `AuthorizationPolicy` | `null` | Named ASP.NET Core authorization policy applied to the Gridlet route group. When null, the host's default policy is used. The policy must be registered by the host. Ignored when `AllowAnonymous` is true. |

Authentication itself remains the host application's responsibility. Configure ASP.NET Core
authentication and authorization before mapping Gridlet; Gridlet does not provide a separate login.

### Storage options

| Property | Default | Effect |
| --- | --- | --- |
| `FilePath` | `gridlet-store.json` | JSON file for saved queries and published endpoint definitions. Relative paths resolve from the host content root, and the process needs read/write access to the containing directory. It does not contain result data or connection strings. |

Replace `ISavedQueryStore` and/or `IPublishedEndpointStore` after `AddGridlet` to use a database or
another persistence mechanism. Gridlet uses `TryAdd`, so explicit host registrations take precedence.

### Mapping and operational services

`app.MapGridlet(pattern)` maps the UI and its APIs under `pattern`, which defaults to `/gridlet`.
The pattern may be changed, for example `app.MapGridlet("/internal/database")`. Configuration is
validated when the endpoints are mapped, so invalid connection names or limit combinations fail at
startup rather than on the first request.

Query execution, row writes, schema changes, and published endpoint invocations are sent to
`IGridletAuditSink`. The default sink writes structured events through `ILogger`; register your own
`IGridletAuditSink` before or after `AddGridlet` to persist them elsewhere (`TryAdd` preserves it).

## Packages

| Package | Purpose |
| --- | --- |
| `Gridlet.Core` | Provider-agnostic abstractions, domain model, options, auditing. |
| `Gridlet.AspNetCore` | `AddGridlet()` / `MapGridlet()`, JSON API, embedded web UI. |
| `Gridlet.SqlServer` | SQL Server provider (schema, data paging, query execution). |

The provider boundary (`IGridletProvider` → `ISchemaReader`, `ITableDataService`, `IQueryRunner`)
keeps the core and UI engine-neutral so `Gridlet.Postgres`, `Gridlet.MySql`, and `Gridlet.Sqlite`
can be added later without rewriting the product.

## Repository layout

```
src/
  Gridlet.Core/          core abstractions + domain model
  Gridlet.AspNetCore/    host integration, API endpoints, embedded UI
  Gridlet.SqlServer/     SQL Server provider
tests/
  Gridlet.Tests/         unit tests + in-memory endpoint/auth tests (no DB required)
samples/
  Gridlet.VisualTest/    startup project for visual testing against SQL Server LocalDB
```

## Visual testing

`samples/Gridlet.VisualTest` is the startup project. It connects to `(localdb)\MSSQLLocalDB`,
creates and seeds a `GridletSample` database on first run (customers/products/orders plus a view,
a stored procedure, and a function), and mounts Gridlet at `/gridlet` with anonymous access.
It also registers an `OddSecond` ASP.NET Core authorization policy and includes a published endpoint
definition in the sample `gridlet-store.json` for `GET /gridlet/pub/samples/odd-second`. The endpoint
returns query results during odd-numbered UTC
seconds and returns `403 Forbidden` during even-numbered UTC seconds, demonstrating how a published
endpoint can require a host-defined policy while the rest of the sample remains anonymous.

```
dotnet run --project samples/Gridlet.VisualTest
# → http://localhost:5088/gridlet
# retry this URL on consecutive seconds to see alternating 200/403 responses:
# → http://localhost:5088/gridlet/pub/samples/odd-second
```

## Security model

- **AuthN/AuthZ** — Gridlet maps all endpoints inside one route group and applies
  `RequireAuthorization()` (or the policy named in `Security.AuthorizationPolicy`). It never invents
  its own login; it reuses whatever the host has configured.
- **Identifiers** — every schema/table/column name that reaches dynamic SQL is validated against
  live metadata and bracket-quoted; values always travel as parameters.
- **Limits** — page size, query row caps, and command timeouts are enforced from `GridletOptions.Limits`.
- **SQL editor** — can be disabled per connection (`AllowSqlExecution = false`). Statement-level
  write protection is intentionally delegated to the SQL login's own permissions: point Gridlet at
  a login that has exactly the rights its users should have.
- **Feature gates** — row editing (`AllowWrites`) and the table designer (`AllowDdl`) can each be
  switched off per connection; the UI hides the controls and the endpoints return 403.
- **Designer safety** — designer data types are validated against a whitelist, every identifier is
  bracket-quoted, and row values always travel as SQL parameters.
- **Audit** — queries, row writes, schema changes, and published-API invocations flow through
  `IGridletAuditSink` (default: structured logging); replace the sink to persist audit events.

## API publishing

Any query can be published as an HTTP endpoint from the query editor (`Publish…`), or via
`POST {mount}/api/published`. Published endpoints:

- live at `{mount}/pub/{route}` (GET with query-string parameters, or POST with a JSON body),
- bind `@parameters` in the SQL to request values (missing optional parameters become `NULL`),
- let the publisher declare each value parameter as `auto`, `string`, `integer`, `number`, or
  `boolean`; Gridlet performs no implicit filtering, ordering, or pagination,
- inherit Gridlet's authorization and can additionally require a named policy,
- are stored (together with saved queries) in a JSON file — `options.Storage.FilePath`,
  default `gridlet-store.json` under the content root; swap `ISavedQueryStore` /
  `IPublishedEndpointStore` to persist elsewhere.

Pagination is deliberately query-authored. For example, publish `page` and `page_size` as
required integer parameters and use them directly in SQL Server:

```sql
SELECT *
FROM dbo.Customers
ORDER BY CustomerId
OFFSET ((@page - 1) * @page_size) ROWS
FETCH NEXT @page_size ROWS ONLY;
```

## V1 scope status

- [x] Explicitly configured SQL Server connections
- [x] Browse databases, tables, views, stored procedures, functions
- [x] Paged, sortable data grid for tables and views
- [x] Inspect columns, keys, indexes, constraints, relationships
- [x] View source of views/procedures/functions
- [x] Ad-hoc query editor with multiple result sets, messages, timing
- [x] Safety limits, query timeouts, audit logging
- [x] Configurable mount path, host auth reuse
- [x] Create tables visually; add/edit/remove columns (drop table/column included)
- [x] Edit table rows where permitted (insert/update/delete with NULL support)
- [x] Saved queries
- [x] Export results and table data (CSV/JSON)
- [x] Publish queries/operations as protected API endpoints
- [x] Resizable grid columns (data grids and query results)
- [ ] Create/edit views and stored procedures from the UI
- [ ] Index/foreign-key designer
- [ ] Server-side full-table export (current export covers the loaded rows)

## Development

```
dotnet build
dotnet test
```

Tests run against an in-memory fake provider and the real endpoint pipeline — no SQL Server needed,
so they also run in CI (`.github/workflows/ci.yml`).

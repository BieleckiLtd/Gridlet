# Gridlet

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
    })
    .AddSqlServer();

var app = builder.Build();

app.MapGridlet("/gridlet"); // mount path is configurable

app.Run();
```

Browse to `/gridlet`. Every Gridlet endpoint (UI and API) requires authorization by default;
`options.Security.AllowAnonymous = true` exists for local development only.

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

```
dotnet run --project samples/Gridlet.VisualTest
# → http://localhost:5088/gridlet
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
- **Audit** — query executions flow through `IGridletAuditSink` (default: structured logging);
  replace the sink to persist audit events.

## V1 scope status

- [x] Explicitly configured SQL Server connections
- [x] Browse databases, tables, views, stored procedures, functions
- [x] Paged, sortable data grid for tables and views
- [x] Inspect columns, keys, indexes, constraints, relationships
- [x] View source of views/procedures/functions
- [x] Ad-hoc query editor with multiple result sets, messages, timing
- [x] Safety limits, query timeouts, audit logging
- [x] Configurable mount path, host auth reuse
- [ ] Create tables visually; add/edit/remove columns
- [ ] Edit table rows where permitted
- [ ] Create/edit views and stored procedures from the UI
- [ ] Saved queries
- [ ] Export results (CSV/JSON)
- [ ] Publish queries/operations as protected API endpoints

## Development

```
dotnet build
dotnet test
```

Tests run against an in-memory fake provider and the real endpoint pipeline — no SQL Server needed,
so they also run in CI (`.github/workflows/ci.yml`).

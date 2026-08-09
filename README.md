<p align="center">
  <img src="assets/gridlet-logo.png" width="160" height="160" alt="Gridlet logo" />
</p>

<h1 align="center">Gridlet</h1>

Gridlet is an embeddable ASP.NET Core NuGet package that adds a configurable, web-based database
management interface to an existing application. Browse schemas and data, inspect keys, indexes and
relationships, and run queries, all from within the host application using its existing
authentication, authorisation, routing, logging and deployment model. Gridlet also includes an
integrated AI agent, allowing you to ask questions about the database, explore its structure,
generate queries, and talk directly with your data using natural language.

<p align="center">
  <img src="assets/screenshot-1.png" width="100%" alt="Gridlet table browser showing customer data" />
</p>

## Multiple providers

Register SQL Server and SQLite connections side by side, then switch between providers and their
databases from the Gridlet header. Gridlet adapts its object browser and tools to the selected
provider, surfacing the database objects and operations it supports, such as stored procedures and
functions in SQL Server, while omitting concepts that do not apply to that provider.

```csharp
.AddSqlServer(sqlServerConnectionString)
.AddSqlite(sqliteConnectionString);
```

<p align="center">
  <img src="assets/screenshot-4.png" width="720" alt="Gridlet connection selector showing SQLite and SQL Server connections in one app" />
</p>

## Query editor

Write and run SQL inside Gridlet, inspect results as they stream in, save useful queries, and export
the current result set as CSV or JSON.

<p align="center">
  <img src="assets/screenshot-3.png" width="100%" alt="Gridlet query editor showing order summary results" />
</p>

## Talk with your database

The optional `Gridlet.AgentFramework` package adds an **Ask** workspace powered by
[Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/).

**Nothing is shared with a model unless somebody opts in.** A **Share** menu next to the composer
controls what one conversation may reach. The scopes are independent rather than a mode switch:

- **Schema** — object names, columns, keys, indexes, relationships, and definitions. On by
  default, because reasoning about a database without its shape is mostly guesswork.
- **Data** — row values, read through one bounded, read-only query at a time. Off by default.
- **Published API** — this Gridlet's own published endpoints: reading their definitions and
  permission to invoke GET endpoints. Off by default. This does not grant direct database-query
  access or automatically share rows. If the agent invokes an endpoint, that response is shared
  and may contain data; it is bounded by the endpoint's SQL and the identity it runs under.

Any scope can be cleared, including while an answer is streaming; the next tool call sees the
change. A scope that is off makes its tools refuse without touching the database, and no scope can
exceed what the connection's `AllowAgentSchemaAccess`, `AllowAgentDataAccess`, and
`AllowAgentApiAccess` settings permit. The agent is read-only in every configuration: it can
propose DDL and SQL, but it can never apply it.

### Asking for access mid-answer

When the agent needs a scope nobody shared, it does not fail and it does not silently do without.
It asks, and an **Allow / Deny** card appears in the transcript with the reason it gave. The turn
stays open while the card waits, so allowing lets the same answer continue straight into the query
it wanted — no repeating the question, no starting a new conversation. Allowing shares that scope
for the rest of the conversation and ticks the matching checkbox; denying, or leaving the card
unanswered until it expires, is final for that turn. Every decision is audited.

Answering a card goes through its own endpoint guarded by the same authorization policy as the chat
route that would have granted the scope up front, so an access prompt can never widen what a person
was already allowed to reach.

### The agent knows Gridlet, not just your database

The agent has no filesystem, repository, or web access, so it is given Gridlet's own documentation
as a tool. It can explain how to publish a query as an HTTP API endpoint, how to declare and bind
parameters, how to page and index an endpoint so it scales, what the response shape is, and what the
interface, designer, and security model do — written for people who have never built an HTTP
endpoint before. A separate tool reports what *this* installation actually permits, so it does not
describe features a host switched off.

Gridlet supplies the selected database technology and live engine version in the agent's system
instructions, so responses use the correct SQL dialect and version-supported features instead of
inferring the engine from schema details. Built-in SQL Server and SQLite providers report this
metadata; third-party providers can implement `IGridletDatabaseSystemInfoProvider` to do the same.

<video src="https://github.com/user-attachments/assets/f62b6df5-9681-4a37-b133-3141b31f568d" controls></video>

<p align="center"><em>Gridlet Ask workspace demonstration.</em></p>

Profiles can use a ChatGPT subscription through the local Codex runtime, a Claude subscription
through the local Claude Code CLI, a GitHub Copilot subscription through the local Copilot CLI,
the OpenAI API, Anthropic's API, an OpenAI-compatible endpoint, or local Ollama. Provider URLs and
models are allow-listed by the host. An API server key
may come from configuration, User Secrets, or a vault; alternatively, authenticated users can enter
their own key. User keys are held only in server memory behind an expiring, user-bound handle and
are never written to Gridlet storage or browser storage.

```csharp
builder.Services
    .AddGridlet()
    .AddSqlServer(managementConnectionString, connection =>
    {
        connection.AllowAgentSchemaAccess = true;
        connection.AllowAgentDataAccess = true;
        connection.AgentDataConnectionString = readOnlyConnectionString;
    })
    .AddAgentFramework(agents =>
    {
        // Uses the ChatGPT account owned by the local Codex installation; no API key.
        agents.AddCodex("codex-subscription", "gpt-5.4")
            .WithReasoningEffort(GridletCodexReasoningEffort.High);

        // Uses the account owned by the local Claude Code CLI; no API key.
        agents.AddClaudeCode("claude-subscription", "sonnet")
            .WithReasoningEffort(GridletClaudeCodeEffort.High)
            .AllowReasoningEffortSelection();

        // Uses the account owned by the local GitHub Copilot CLI; no API key.
        agents.AddGitHubCopilot("github-copilot", "gpt-5")
            .WithReasoningEffort(GridletCopilotReasoningEffort.Medium);

        agents.AddOpenAI("openai", "gpt-5-mini")
            .WithServerApiKey(builder.Configuration["AI:OpenAI:ApiKey"])
            .AllowUserApiKeys();

        agents.AddAnthropic("claude", "claude-sonnet-4-5")
            .AllowUserApiKeys();

        // Optional: the fallback window for when the model is not resident in Ollama.
        agents.AddOllama(
                "local", new Uri("http://127.0.0.1:11434"), "qwen3:4b")
            .WithContextWindow(32_768);
    });
```

`AddCodex` launches `codex app-server` over stdio/JSON-RPC and uses the ChatGPT login stored by that
local Codex installation. Install the Codex CLI and run `codex login` as the same operating-system
user that runs the .NET application. Gridlet never receives the ChatGPT tokens. This is a host-level
identity, not a separate identity for each web user: use it only where application users are meant
to share the host's Codex entitlement, and protect the agent endpoints with authorization policies.
Each Ask tab owns an ephemeral Codex thread and reuses its app-server process across turns. Closing
the tab releases the process; abandoned sessions are evicted after `ConversationIdleTimeout`.
The app-server custom-tool bridge is currently an experimental Codex protocol surface.
Set per-profile reasoning with `WithReasoningEffort`; omit it to retain the Codex/model default.
Supported levels depend on the selected model, and `ExtraHigh` maps to app-server's `xhigh` value.
The expanded Thinking panel shows every reasoning surface app-server supplies without requesting
more than Codex's concise summary: summary sections, optional model-supported raw reasoning, tool
activity, and authoritative completed-item corrections. Raw reasoning is not available from every
model or turn.
For schema-heavy conversations, increase `MaxToolIterations` up to its validated maximum of `100`,
or set it to `null` for no Gridlet-imposed ceiling. Providers can still impose their own limits.
When Gridlet's configured limit is reached, it asks Codex to finish using the information already
collected and streams that tool feedback to the client.

`AddClaudeCode` launches the native Claude Code executable in bidirectional `stream-json` mode and
uses the login stored by that installation. Install Claude Code and run `claude auth login` as the
same operating-system user that runs the .NET application. Each Ask tab keeps one Claude Code
process across turns and releases it under the same close, failure, idle-expiry, and process-cap
rules as Codex; CLI session persistence is disabled. Gridlet disables Claude Code's built-in tools;
the only model-callable tools are Gridlet's bounded database functions, exposed through Claude
Code's SDK MCP bridge. On Windows, use the native `claude.exe`: Gridlet rejects npm `.cmd`/`.bat`
shims because Windows re-parses their arguments through `cmd.exe`.
`AllowReasoningEffortSelection` exposes the provider-supported effort levels in the Ask header.
Changing effort applies to the next turn without clearing the conversation or replacing the
tab-scoped Codex/Claude CLI session. For Claude, Gridlet invokes only the built-in `/effort` command
and wraps user messages beginning with `/` as literal content so they cannot invoke CLI commands.
The `WithReasoningEffort` value is the initial selection.

`AddGitHubCopilot` launches the installed GitHub Copilot CLI over stdio using GitHub's Copilot SDK
and Microsoft Agent Framework adapter. Install the CLI and run `copilot login` as the same
operating-system user that runs the .NET application. Gridlet never receives the stored GitHub
credentials. As with Codex, this is a shared host-level identity rather than a separate identity for
each web user. Gridlet disables Copilot configuration discovery and allow-lists only the custom
schema/read-only database tools for the session; Copilot's shell, filesystem, MCP, and other built-in
tools are unavailable. `WithReasoningEffort` supports `Low`, `Medium`, `High`, and `ExtraHigh` when
the selected Copilot model advertises that capability. Gridlet requests Copilot's concise reasoning
summary and shows every reasoning event supplied by the adapter in the expanded Thinking panel.
The optional `MaxToolIterations` ceiling is enforced through a pre-tool hook; when reached, the
denial includes feedback directing the model to finish with the data already collected.

See GitHub's documentation for [local CLI authentication](https://docs.github.com/en/copilot/how-tos/copilot-sdk/setup/local-cli)
and the [Microsoft Agent Framework integration](https://docs.github.com/en/copilot/how-tos/copilot-sdk/integrations/microsoft-agent-framework).

`Gridlet.AgentFramework` is published as a prerelease package because some Microsoft Agent
Framework provider adapters remain preview dependencies.

## Published APIs

Turn a query into an HTTP endpoint, then test it with the built-in request preview and inspect the
status, timing, size, and formatted JSON response. Published endpoints can be protected by
authorization policies already defined by the host ASP.NET Core app.

<p align="center">
  <img src="assets/screenshot-2.png" width="620" alt="Gridlet published API preview showing a formatted JSON response" />
</p>

## Quick start

In an app that already configures ASP.NET Core authentication and authorization:

```csharp
using Gridlet;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGridlet()
    .AddSqlServer(builder.Configuration.GetConnectionString("Default"));

var app = builder.Build();

app.MapGridlet();

app.Run();
```

Browse to `/gridlet`. Gridlet uses the host's default authorization policy. Add more connections by
chaining `.AddSqlServer(connectionString)` or `.AddSqlite(connectionString)`.

## Developer configuration reference

`AddGridlet` registers Gridlet and accepts an optional `Action<GridletOptions>`. Chain
`AddSqlServer(...)` or `AddSqlite(...)` once for each connection. Pass a connection string directly,
or pass `IConfiguration` and its key in the standard `ConnectionStrings` section. Each method
registers its provider, derives the connection label from the server or SQLite filename, and selects
the connection string's initial database by default:

```csharp
builder.Services
    .AddGridlet(options =>
    {
        options.Limits.DefaultPageSize = 50;
        options.Limits.MaxPageSize = 500;
        options.Limits.MaxQueryResultRows = 10_000;
        options.Limits.CommandTimeoutSeconds = 30;

        options.Security.AllowAnonymous = false;
        options.Security.AuthorizationPolicy = "GridletAccess";
        options.Security.AgentDataAuthorizationPolicy = "GridletDataAgent";
        options.Security.AgentSchemaAuthorizationPolicy = "GridletSchemaAgent";
        options.Security.AgentCredentialAuthorizationPolicy = "GridletAgentCredentials";

        options.Storage.FilePath = "App_Data/gridlet-store.json";
    })
    .AddSqlServer(builder.Configuration, "Reporting", connection =>
    {
        connection.AllowSqlExecution = true;
        connection.AllowWrites = false;
        connection.AllowDdl = false;
        connection.AllowAgentSchemaAccess = true;
        connection.AllowAgentDataAccess = true;
        connection.AllowAgentApiAccess = true;
        connection.AgentDataConnectionString = readOnlyReportingConnectionString;
    });
```

To bind the same options from an `IConfigurationSection`, use the explicit configuration entry
point and then register every provider named by the configured connections:

```csharp
builder.Services
    .AddGridletFromConfiguration(builder.Configuration.GetSection("Gridlet"))
    .AddSqlServer()
    .AddSqlite();
```

The bound connections, limits, security, storage, and audit settings go through the same startup
validation as callback-based configuration. Provider registration remains explicit; omit a provider
method only when no configured connection uses it.

### `GridletOptions`

| Property | Default | Effect |
| --- | --- | --- |
| `Connections` | Empty | Explicit allow-list of connections exposed by Gridlet. Calls to `AddSqlServer(connectionString)` and `AddSqlite(connectionString)` populate it; Gridlet does not automatically expose the host application's other connection strings. |
| `Limits` | New `GridletLimitsOptions` | Server-side paging, result-size, and timeout protections. |
| `Security` | New `GridletSecurityOptions` | Authentication and authorization applied to the entire Gridlet route group. |
| `Storage` | New `GridletStorageOptions` | Persistence settings for saved queries and published endpoint definitions. |
| `Audit` | New `GridletAuditOptions` | Privacy controls for SQL and error details written by the default audit logger. |
| `PublishedApiRoutePrefix` | `"pub"` | The single route segment, directly under the mount, that published endpoints answer on — so `/gridlet/pub/customers` becomes `/gridlet/endpoints/customers` when set to `"endpoints"`. Surrounding slashes are optional. Must be one segment of ASCII letters, digits, `.`, `-`, or `_`, and cannot be `api` (Gridlet's own management API is mounted there). Changing it moves every published endpoint at once, so existing callers have to be updated. |

The lower-level, provider-agnostic
`AddConnection(name, connectionString, providerName, configure)` API remains available for custom
registration:

| Argument | Effect |
| --- | --- |
| `name` | Unique, case-insensitive name displayed in the UI and embedded in API routes. |
| `connectionString` | Provider-specific connection string used only on the server; it is never returned to the browser. Use a least-privileged database identity. |
| `providerName` | Required `GridletProviderNames` enum value selecting the provider implementation. Chain `.AddSqlServer()` or `.AddSqlite()` to register the selected provider. |
| `configure` | Optional callback for the connection feature gates described below. |

`AddConnection(configuration, connectionName, providerName, configure)` resolves a value from the
standard `ConnectionStrings` configuration section. Provider-specific registration is simpler for
the built-in SQL Server and SQLite providers because it does not require a name or provider enum.

### Per-connection options

| Property | Default | Effect |
| --- | --- | --- |
| `Name` | Empty | Internal display/route label. Provider-specific registration derives it automatically; lower-level `AddConnection` calls must supply a non-empty, unique value. |
| `ConnectionString` | Empty | Secret server-side database connection string. Normally set by `AddConnection` and never exposed by Gridlet APIs. |
| `ProviderName` | `GridletProviderNames.Unspecified` | Strongly typed provider selection. `Unspecified` is rejected during validation, and `AddConnection` requires a concrete value explicitly. |
| `DefaultDatabase` | `null` | Database selected when the UI first opens the connection. Provider-specific registration derives it from the connection string when available. |
| `AllowSqlExecution` | `true` | Shows and enables the ad-hoc SQL editor. This permits any statement allowed by the database login, including writes or DDL; it is independent of the two UI feature gates below. |
| `AllowWrites` | `true` | Enables Gridlet's explicit row insert/update/delete UI and endpoints. It does not prevent write statements submitted through the SQL editor. |
| `AllowDdl` | `true` | Enables Gridlet's schema-changing UI and endpoints: creating, altering, and dropping schemas, tables, columns, keys, indexes, views, routines, and triggers where supported. It does not prevent DDL submitted through the SQL editor. |
| `AllowAgentSchemaAccess` | `false` | Permits a person to share schema metadata and object definitions with a configured model. Sharing remains their per-conversation choice; the agent cannot apply DDL. |
| `AllowAgentDataAccess` | `false` | Permits a person to share row data, read through bounded read-only queries. Off by default in every conversation even when this is enabled. |
| `AllowAgentApiAccess` | `false` | Permits a person to share this Gridlet's published API definitions and let the agent invoke GET endpoints. It grants no direct database-query access and is independent of `AllowAgentDataAccess`. If an endpoint is invoked, its response is shared and may contain data. Off by default in every conversation even when this is enabled. |
| `AgentDataConnectionString` | `null` | Separate server-side connection string used whenever the agent reads data. Use a SELECT-only identity. Never returned by Gridlet. |
| `AllowAgentDataWithPrimaryConnection` | `false` | Explicitly opts into using the primary Gridlet identity when no agent-specific connection is configured. Avoid this when the primary identity can write or run DDL. |

### Limit options

| Property | Default | Effect |
| --- | --- | --- |
| `DefaultPageSize` | `50` | Default size for the paged table-data API retained for API consumers. The interactive UI uses streaming. Must be at least 1. |
| `MaxPageSize` | `500` | Server-enforced upper bound for paged browse requests and the batch size used by streamed table/view browsing. Must be at least `DefaultPageSize`. |
| `MaxQueryResultRows` | `10,000` | Maximum rows retained per query result set or streamed table/view for the interactive UI and ad-hoc query editor. This is a hard cap there: the **Row cap** control can request a lower value (persisted per browser) but can never exceed it. It does **not** apply to published API endpoints, which are uncapped by default and set any cap per endpoint (see [API publishing](#api-publishing)). Results stream progressively and virtualize above 1,000 rows; the cap still protects server and browser memory. |
| `CommandTimeoutSeconds` | `30` | Provider command timeout for query execution. The user can cancel sooner with the query toolbar's Cancel button. Must be at least 1. |

Paged table browsing uses the table's full primary key as its default order when one is available.
When a user selects another sort column, remaining primary-key columns are appended as tie-breakers,
so rows with equal sort values do not move unpredictably between pages. Without a usable primary
key, otherwise-equal rows retain the database engine's ordering.

### Security options

| Property | Default | Effect |
| --- | --- | --- |
| `AllowAnonymous` | `false` | When false, `MapGridlet` applies ASP.NET Core authorization to every UI, API, asset, and published endpoint under the mount path. Set true only when anonymous database tooling is intentional, typically local development. A named `AuthorizationPolicy` takes precedence. |
| `AuthorizationPolicy` | `null` | Named ASP.NET Core authorization policy applied to the Gridlet route group. When null, the host's default policy is used unless `AllowAnonymous` is true. The policy must be registered by the host. When set, it always applies. |
| `AgentDataAuthorizationPolicy` | `null` | Optional additional policy for conversations sharing data, and for answering an agent's request to share it. |
| `AgentSchemaAuthorizationPolicy` | `null` | Optional additional policy for conversations sharing schema, and for answering an agent's request to share it. |
| `AgentCredentialAuthorizationPolicy` | `null` | Optional additional policy for creating and removing ephemeral user-key handles. |
| `AllowAnonymousAgentCredentials` | `false` | Allows anonymous BYOK handles only when explicitly enabled. Authenticated, user-bound keys are the default. |

### Agent Framework options

`AddAgentFramework` is optional and lives in the `Gridlet.AgentFramework` package. It accepts named,
host-controlled profiles through `AddCodex`, `AddClaudeCode`, `AddGitHubCopilot`, `AddOpenAI`, `AddAnthropic`,
`AddOpenAICompatible`, and `AddOllama`. API-backed profile builders support `WithServerApiKey` and
`AllowUserApiKeys`; subscription-backed Codex, Claude Code, and GitHub Copilot profiles reject both because
authentication belongs exclusively to the local CLI runtime. `AsLocal` controls the safe locality
metadata exposed for OpenAI-compatible profiles.

`AccessPromptTimeout` (default five minutes, between ten seconds and thirty minutes) bounds how long
an agent's request to share schema or data waits for an answer. The turn stays on the wire for that
long, so an unanswered prompt expires as a denial rather than holding a response open indefinitely.

### Context-window gauge

When a provider reports token usage, the Ask composer draws a ring around the send button showing how
much of the model's context window the conversation occupies, with the token breakdown in a tooltip on
hover. Providers differ in what they report:

| Provider | Tokens used | Context-window size | Arrives |
| --- | --- | --- | --- |
| Codex | `thread/tokenUsage/updated`, the most recent request's usage | `modelContextWindow` | While streaming |
| Claude Code | Usage forwarded from the model's own streaming events | `modelUsage.contextWindow` | While streaming; the window from the first completed turn onward |
| GitHub Copilot | Session metadata `contextInfo.totalTokens` | The model's `max_prompt_tokens` | End of turn |
| Ollama | `prompt_eval_count` / `eval_count` | The window the server actually loaded the model with, read from `/api/ps` | End of response |
| OpenAI, Anthropic, OpenAI-compatible | Endpoint-reported usage, when returned | Not reported; declare it | End of response |

Ollama never reports a window with a response, and the effective window is the runtime `num_ctx` rather
than the model's trained maximum, so Gridlet asks the running server which window the model is loaded
with. If the model is not resident, the declared value is used instead.

Copilot pushes `session.usage_info` only to the owner of the live session, which the Agent Framework
adapter keeps to itself, so Gridlet reads the same numbers from the session's metadata API once the turn
ends. The denominator is the model's prompt budget rather than its full window, because the remainder is
reserved for the model's own output.

`WithContextWindow` lets the host declare a window for providers that report usage without one. Any
window discovered from the provider wins over the declaration. With usage but no window, the ring stays
neutral and the tooltip reports the token count alone; with no usage at all the ring is not drawn. The
gauge reflects the current conversation only and resets when the conversation or model changes.

Reading usage never fails a turn: if a probe cannot reach Ollama, or Copilot cannot supply metadata, the
answer is unaffected and the gauge simply stays as it was.

The agent's `list_database_objects` catalog tool accepts optional schema, case-insensitive name-text,
and object-type filters. This lets the model narrow large catalogs before requesting table details
or object definitions.

| Property | Default | Effect |
| --- | --- | --- |
| `CredentialLifetime` | 30 minutes | Lifetime of an ephemeral user-key handle; constrained to at most one day. |
| `MaxEphemeralCredentials` | `512` | Maximum active browser-supplied API-key handles retained by one application instance. New handles are rejected at the limit rather than evicting active credentials. |
| `MaxEphemeralCredentialsPerOwner` | `8` | Maximum active browser-supplied API-key handles for one authenticated or explicitly anonymous owner. Must not exceed `MaxEphemeralCredentials`. |
| `ConversationIdleTimeout` | 30 minutes | Inactivity timeout for a subscription-backed CLI session owned by an Ask tab; constrained to one minute through one day. Explicitly closing the tab releases it immediately. |
| `MaxActiveConversations` | `32` | Maximum retained subscription-backed CLI sessions per application instance, preventing abandoned tabs from creating an unbounded process pool. |
| `MaxHistoryMessages` / `MaxHistoryCharacters` | `50` / `200,000` | Per-turn conversation-history limits. Conversations remain browser-held and are not persisted. |
| `MaxMessageCharacters` | `20,000` | Maximum current user message length. |
| `MaxToolResultCharacters` | `32,000` | Maximum serialized schema or query result sent back to a model tool call. |
| `MaxQueryCharacters` / `MaxQueryRows` | `20,000` / `100` | Data-agent SQL and per-result-set row caps. |
| `QueryTimeoutSeconds` | `120` | Timeout for the data agent's read-only query tool. |
| `MaxToolIterations` / `MaxOutputTokens` | `8` / `4,096` | Optional tool-call limit (`null` disables Gridlet's ceiling) and API-model output-token request. Subscription-backed CLI providers do not expose an equivalent stable output-token field. |
| `CodexExecutablePath` | `codex` | Command or absolute path used to launch `codex app-server` for subscription-backed profiles. |
| `ClaudeExecutablePath` | `claude` | Command or absolute path used to launch the native Claude Code CLI for subscription-backed profiles. |
| `CopilotExecutablePath` | `copilot` | Command or absolute path used to launch GitHub Copilot CLI for subscription-backed profiles. |

Authentication itself remains the host application's responsibility. Configure ASP.NET Core
authentication and authorization before mapping Gridlet; Gridlet does not provide a separate login.

### Storage options

| Property | Default | Effect |
| --- | --- | --- |
| `FilePath` | `gridlet-store.json` | JSON file for saved queries and published endpoint definitions. Relative paths resolve from the host content root, and the process needs read/write access to the containing directory. It does not contain result data or connection strings. |

Replace `ISavedQueryStore` and/or `IPublishedEndpointStore` after `AddGridlet` to use a database or
another persistence mechanism. Gridlet uses `TryAdd`, so explicit host registrations take precedence.

### Audit options

| Property | Default | Effect |
| --- | --- | --- |
| `IncludeSqlText` | `true` | Includes user-authored SQL in the default structured audit log. Set to `false` when SQL literals may contain private data. |
| `IncludeErrorDetails` | `true` | Includes database and exception details in the default structured audit log. Set to `false` to redact them. |

Both defaults preserve the existing audit output. These settings affect only the built-in logging
sink; a replacement `IGridletAuditSink` controls its own handling of audit event fields.

### Mapping and operational services

`app.MapGridlet(pattern)` maps the UI and its APIs under `pattern`, which defaults to `/gridlet`.
The pattern may be changed, for example `app.MapGridlet("/internal/database")`. Configuration is
validated when the endpoints are mapped, so invalid connection names or limit combinations fail at
startup rather than on the first request.

Hosts that do not need the embedded UI can map narrower surfaces under the same configurable prefix:

```csharp
app.MapGridletApi();       // management API plus published endpoints; no UI or assets
// or
app.MapGridletPublished(); // published endpoints only
```

`MapGridletApi` and `MapGridletPublished` use the same startup validation and security options as
`MapGridlet`. They require authorization by default; `AuthorizationPolicy` takes precedence, and
anonymous access is enabled only by explicitly setting `Security.AllowAnonymous`.

Query execution, row writes, schema changes, and published endpoint invocations are sent to
`IGridletAuditSink`. The default sink writes structured events through `ILogger`; register your own
`IGridletAuditSink` before or after `AddGridlet` to persist them elsewhere (`TryAdd` preserves it).

## Packages

| Package | Purpose |
| --- | --- |
| `Gridlet.Core` | Provider-agnostic abstractions, domain model, options, auditing. |
| `Gridlet.AspNetCore` | `AddGridlet()` / `MapGridlet()`, JSON API, embedded web UI. |
| `Gridlet.AgentFramework` | Optional Microsoft Agent Framework integration with subscription-backed Codex, OpenAI, Anthropic, OpenAI-compatible, and Ollama profiles. |
| `Gridlet.SqlServer` | SQL Server provider (schema, data paging, query execution). |
| `Gridlet.Sqlite` | SQLite provider (schema, data paging, query execution, writes, and DDL). |

The provider boundary (`IGridletProvider` → `ISchemaReader`, `ITableDataService`, `IQueryRunner`)
keeps the core and UI engine-neutral so providers such as `Gridlet.Postgres` and `Gridlet.MySql`
can be added later without rewriting the product.

## Repository layout

```
src/
  Gridlet.Core/          core abstractions + domain model
  Gridlet.AgentFramework/ optional Microsoft Agent Framework integration
    Prompts/             every instruction given to a model, as editable Markdown
  Gridlet.AspNetCore/    host integration, API endpoints, embedded UI
  Gridlet.SqlServer/     SQL Server provider
  Gridlet.Sqlite/        SQLite provider
tests/
  Gridlet.Tests/         unit tests + in-memory endpoint/auth tests (no DB required)
  Gridlet.BrowserTests/  end-to-end browser tests for the embedded UI
  Gridlet.ConsumerCompileTest/  compile-time checks for the public consumer API
samples/
  Gridlet.Demo/          runnable demo against SQLite and SQL Server LocalDB
```

## Demo

`samples/Gridlet.Demo` is the runnable sample project. It creates and seeds a local
`BytePizza.db` SQLite database on first run. The Byte Pizza dataset covers pizzas, toppings,
customers, orders, promotions, and deliveries, along with views, triggers, FTS5 search, JSON,
generated columns, and representative SQLite indexes. On Windows, the demo also creates a
`GridletLocalDbSample` database in the `MSSQLLocalDB` SQL
Server LocalDB instance, including multiple schemas, a view, trigger, stored procedure, and function.
If LocalDB is not installed, the demo logs a warning and continues with SQLite only. It mounts
Gridlet at `/gridlet` with anonymous access.
The sample store includes published APIs for typed query-string parameters, JSON request bodies,
FTS5 search, views, joins, JSON extraction, result caps, and endpoint-specific authorization.
Byte Pizza delivers from 11:00 until 22:00 in the host's local time zone. During those hours the
delivery menu is authorized; outside them, a complementary policy exposes a collection-only
Margherita menu. The inactive endpoint returns `403 Forbidden`, demonstrating that a host policy can
control one published API while the rest of Gridlet remains anonymous.

```
dotnet run --project samples/Gridlet.Demo
# → http://localhost:5088/gridlet
# typed GET parameter and FTS5 search:
# → http://localhost:5088/gridlet/pub/menu/vegetarian?max_calories=900
# → http://localhost:5088/gridlet/pub/menu/search?term=cold
# one of these two policy-controlled menus returns 200 and the other 403:
# → http://localhost:5088/gridlet/pub/menu/delivery
# → http://localhost:5088/gridlet/pub/menu/after-hours
# typed JSON-body parameters:
curl -X POST http://localhost:5088/gridlet/pub/orders/estimate \
  -H "Content-Type: application/json" \
  -d '{"pizza_id":9,"size":"Large","quantity":1}'
```

## Security model

- **AuthN/AuthZ:** Gridlet maps all endpoints inside one route group and applies
  `RequireAuthorization()` (or the policy named in `Security.AuthorizationPolicy`). It never invents
  its own login; it reuses whatever the host has configured.
- **Identifiers:** every schema/table/column name that reaches dynamic SQL is validated against
  live metadata and bracket-quoted; values always travel as parameters.
- **Limits:** page size, query row caps, and command timeouts are enforced from `GridletOptions.Limits`.
- **SQL editor:** can be disabled per connection (`AllowSqlExecution = false`). Statement-level
  write protection is intentionally delegated to the SQL login's own permissions: point Gridlet at
  a login that has exactly the rights its users should have.
- **Feature gates:** row editing (`AllowWrites`) and the table designer (`AllowDdl`) can each be
  switched off per connection; the UI hides the controls and the endpoints return 403.
- **Designer safety:** designer data types are validated against a whitelist, every identifier is
  bracket-quoted, and row values always travel as SQL parameters.
- **Audit:** queries, row writes, schema changes, and published-API invocations flow through
  `IGridletAuditSink` (default: structured logging); replace the sink to persist audit events.
- **Agents:** all three scopes are default-off per connection, and a person then opts into each one
  separately per conversation, with data and API off even when the connection permits them. The
  API scope rides the same route, and therefore the same host authorization policy, as data because
  an invoked endpoint may return row values. The API grant remains independent: it does not enable
  direct database queries or turn on the Data scope. Every database tool re-checks the live grant
  at the moment it runs, so clearing a scope or denying a request closes it immediately rather than
  at the next turn. Reading data should use
  `AgentDataConnectionString` with a SELECT-only database principal. The agent never receives a
  mutation or DDL tool in any configuration. Tool results, schema definitions, saved SQL, cell
  values, and an agent's own stated reason for requesting access are treated as untrusted model
  input; row, character, iteration, token, prompt-timeout, and query-timeout caps are enforced.
- **Subscription CLI isolation:** Codex, Claude Code, and GitHub Copilot each treat their working
  directory as a project. Launched in the host application's directory they will, without any tool
  call, read `AGENTS.md` and `CLAUDE.md` up the tree and obey them as instructions, report the
  absolute path, git root, and a directory listing to the model, and load the operating-system
  user's own agent memory. Gridlet launches all three in a private empty directory and additionally
  disables Codex's project-doc and environment-context injection and Copilot's custom instructions,
  so none of that reaches a model through the Ask workspace.
- **Agent keys:** server keys remain in host configuration. User keys require authentication by
  default, live only in process memory, are zeroed when removed/expired, and are referenced by
  opaque handles sent in request bodies rather than URLs.

An explicitly configured `AuthorizationPolicy` takes precedence over `AllowAnonymous`. This makes a
named policy fail closed even if a development configuration layer also sets `AllowAnonymous` to
`true`. Anonymous access is enabled only when `AllowAnonymous` is `true` and no named policy is set.

### Separate database identity for published APIs

You can configure a second named connection for published endpoints so their SQL runs as a
least-privileged database user:

```csharp
options.AddConnection("Management", adminConnectionString, GridletProviderNames.SqlServer);

options.AddConnection(
    "PublishedApi",
    restrictedApiConnectionString,
    GridletProviderNames.SqlServer,
    configure: connection =>
{
    // Hide interactive mutation tools for this connection. These are Gridlet feature gates;
    // the restricted database user's GRANT/DENY permissions remain the security boundary.
    connection.AllowSqlExecution = false;
    connection.AllowWrites = false;
    connection.AllowDdl = false;
});
```

Select `PublishedApi` as the connection when publishing the endpoint. Gridlet stores that connection
name with the endpoint and uses its connection string on invocation. This separation is currently
selectable rather than mandatory: a publisher can still select `Management`, so the host must limit
publishing to trusted administrators and review stored endpoint definitions. Gridlet does not yet
have a dedicated execution connection that automatically overrides every published endpoint.

## API publishing

Any query can be published as an HTTP endpoint from the query editor (`Publish…`), or via
`POST {mount}/api/published`. Published endpoints:

- live at `{mount}/{PublishedApiRoutePrefix}/{route}`, which is `{mount}/pub/{route}` unless the
  host changed the prefix (GET with query-string parameters, or POST, PUT, PATCH, and DELETE with a JSON body),
- bind `@parameters` in the SQL to request values (missing optional parameters become `NULL`),
- let the publisher declare each value parameter as `auto`, `string`, `integer`, `number`, or
  `boolean`; Gridlet performs no implicit filtering, ordering, or pagination,
- inherit Gridlet's authorization and can additionally require a named policy,
- are stored (together with saved queries) in a JSON file at `options.Storage.FilePath`,
  default `gridlet-store.json` under the content root; swap `ISavedQueryStore` /
  `IPublishedEndpointStore` to persist elsewhere.

### Response shape

Invocations **stream** their first result set as JSON, so server memory stays bounded no matter how
large the result is (only one batch of rows is held at a time). The response body is:

```json
{ "rows": [ { "col": "value" }, ... ], "rowCount": 123 }
```

`rows` streams first; `rowCount` is only known once every row has been sent, so it **trails** the
array. A statement with no result set returns `{ "recordsAffected": N }` instead. There is no
`truncated` field: published endpoints are uncapped by default (see below), so there is normally
nothing to truncate.

Clients that prefer line-delimited streaming can send `Accept: application/x-ndjson`. Gridlet then
emits one event per line: a `row` event for each record followed by exactly one terminal event on a
completed request:

```json
{"type":"row","row":{"col":"value"}}
{"type":"completed","rowCount":123,"recordsAffected":-1}
```

An error before streaming returns the appropriate `4xx`/`5xx` status and one terminal `error` event
with `rowCount: 0`. If rows have already been sent, the status cannot change from `200`; the final
line is instead an `error` event containing the emitted `rowCount`. The existing JSON response
remains the default when the NDJSON media type is not requested.

Because the `200 OK` status and the first rows are already on the wire, a failure that occurs
**after** streaming has begun cannot change the status code. Such a failure closes the JSON with an
`"error"` field (`{ "rows": [ ... ], "rowCount": N, "error": "message" }`), which consumers should
check for before trusting a partial result. Failures that occur **before** the first byte (routing,
authorization, parameter binding, connection resolution, or an immediate query error) still return a
clean `4xx`/`5xx` status with `{ "error": "message" }`. The `api.invoke` audit event is written when
the stream finishes, so a mid-stream failure is recorded as `succeeded: false`.

### Row cap

Published endpoints are **uncapped by default**. They stream every row, independent of the global
`MaxQueryResultRows` limit (which continues to govern the UI and ad-hoc query editor). An endpoint can
opt into a cap via the optional `maxRows` field on `POST {mount}/api/published`:

- omitted / `null`: uncapped (stream every row),
- `0` or less: uncapped,
- a positive number: cap at that many rows.

Because the default is uncapped, pagination is deliberately query-authored. For example, publish
`page` and `page_size` as
required integer parameters and use them directly in SQL Server:

```sql
SELECT *
FROM dbo.Customers
ORDER BY CustomerId
OFFSET ((@page - 1) * @page_size) ROWS
FETCH NEXT @page_size ROWS ONLY;
```

## Feature status

- [x] Explicitly configured SQL Server connections
- [x] Browse databases, tables, views, stored procedures, functions
- [x] Streaming, sortable data grid for tables and views
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
- [x] Create/edit views, stored procedures, and functions from the UI
- [x] Discover, create, edit, and delete database triggers
- [x] Create/edit indexes and primary/foreign keys
- [ ] Server-side full-table export (current export covers the loaded rows)

## Provider status

- [x] SQL Server support through the `Gridlet.SqlServer` provider
- [x] SQLite support through the `Gridlet.Sqlite` provider, with provider-specific schema,
  query, write, trigger, and DDL coverage

SQLite exposes its primary database and schema as `main`. It supports tables, views, indexes,
foreign keys, generated columns, row editing, and table-designer DDL; stored procedures, functions,
and user-created schemas are not SQLite features and are omitted from the UI.

## Development

```
dotnet build
pwsh tests/Gridlet.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium # first run only
dotnet test
```

Tests run against an in-memory fake provider, temporary SQLite databases, and the real endpoint
pipeline. No SQL Server is needed, so they also run in CI (`.github/workflows/ci.yml`). Browser tests start Gridlet on an ephemeral
loopback port and use headless Chromium; install its pinned Playwright browser once after cloning or
after updating the Playwright package.

## Third-party software

Gridlet's browser UI is implemented in plain HTML, CSS, and JavaScript; it does not bundle a
third-party front-end framework, editor, icon set, or web font.

The distributable packages use the following third-party projects at runtime:

| Dependency | Used by |
| --- | --- |
| [`Microsoft.Data.SqlClient`](https://github.com/dotnet/SqlClient) | SQL Server connectivity |
| [`Microsoft.Data.Sqlite`](https://learn.microsoft.com/dotnet/standard/data/sqlite/) | SQLite ADO.NET connectivity (MIT). |
| [`SQLitePCLRaw`](https://github.com/ericsink/SQLitePCL.raw) and SQLite | Patched native SQLite bundle used by `Gridlet.Sqlite` (Apache-2.0 / public domain). |
| [`Microsoft.Extensions.DependencyInjection.Abstractions`](https://github.com/dotnet/runtime), [`Microsoft.Extensions.Logging.Abstractions`](https://github.com/dotnet/runtime), and [`Microsoft.Extensions.Options`](https://github.com/dotnet/runtime) | Core hosting abstractions |
| [`Microsoft.Extensions.FileProviders.Embedded`](https://github.com/dotnet/aspnetcore) and the ASP.NET Core shared framework | Embedded UI and ASP.NET Core integration |

The test project additionally uses [xUnit.net](https://github.com/xunit/xunit) and its Visual Studio
runner under the Apache License 2.0, plus Microsoft's MIT-licensed ASP.NET Core TestHost and .NET test
SDK. These development dependencies are not bundled into Gridlet's distributable packages.

Copyrights remain with their respective owners. The in-app **About → Licences** tab provides the
runtime notices to Gridlet users; complete license texts and notices are available from the linked
projects.

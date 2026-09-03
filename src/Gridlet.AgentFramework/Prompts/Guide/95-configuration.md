Configuration is written by a developer in the host application's startup code, not in the
Gridlet UI. The shape is:

```csharp
builder.Services
    .AddGridlet(options => { /* limits, security, storage, audit */ })
    .AddSqlServer(connectionString)
    .AddSqlite(otherConnectionString);

app.MapGridlet();
```

Per-connection settings gate SQL execution, writes, DDL, agent schema access, agent data
access, agent published-API access, and the separate published-API connection string.
`options.Limits` bounds query result rows and timeouts, and caps how many pinned query sessions may
be open (`MaxQuerySessions`) and how long one may sit idle before it is rolled back and closed
(`QuerySessionIdleTimeoutMinutes`). Background query jobs are bounded separately by
`MaxQueryJobs`, `MaxQueryJobsPerOwner`, `MaxQueryJobEvents`, and `MaxQueryJobRetainedBytes`, then
removed after `QueryJobRetentionMinutes`. `options.Security` attaches host
authorization policies, `options.Storage` chooses where saved queries and published endpoints
live, `options.Audit` controls what the audit log records, and
`options.PublishedApiRoutePrefix` chooses the route segment published endpoints answer on
(`pub` by default). The mount itself is the argument to `app.MapGridlet()`.

The optional `Gridlet.AgentFramework` package adds this Ask workspace and configures which
model profiles are offered, including local Ollama endpoints, hosted OpenAI or Anthropic
APIs, and subscription-backed local CLIs.

If someone asks how to change a setting, tell them it is a code change in the host
application and describe the option - do not imply it can be toggled from this page.

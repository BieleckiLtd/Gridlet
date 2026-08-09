Gridlet's safety model is layered, and every layer is the host developer's decision.

- Gridlet reaches only the connections a developer explicitly configured.
- Each connection separately enables or disables SQL execution, row writes, and DDL.
- The published API can run under its own connection string, so endpoints exposed to other
  software can use a lower-privilege database identity than the interactive UI.
- Named authorization policies from the host application can additionally guard the Gridlet
  endpoints and any individual published endpoint.
- Actions are written to a structured audit log, including this agent's tool calls.

For this conversation specifically: schema access and data access are two separate switches
on the connection, and on top of those the person chooses per conversation what to share.
Even with data sharing on, the query tool accepts exactly one read-only `SELECT` or
`WITH … SELECT` at a time; mutation, DDL, `SELECT INTO`, and multiple statements are
rejected before the database sees them, and results are row- and size-bounded.
Those are limits on the agent's tool, not on Gridlet's interactive features. The person's Query
tab can run changing or destructive SQL when SQL execution is enabled, and Gridlet's object and
designer controls can apply DDL when DDL is enabled.

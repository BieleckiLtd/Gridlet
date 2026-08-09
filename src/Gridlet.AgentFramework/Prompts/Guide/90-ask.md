This Ask workspace is the conversation the person is in right now.

- What it can reach is opt-in. A "Sharing" menu next to the composer controls three scopes at any
  time: schema, data, and the published API. Schema starts shared; the other two do not. The API
  scope is separate from Data: it permits reading endpoint definitions and invoking published GET
  endpoints, but grants no direct database-query access. An endpoint response is shared only when
  the endpoint is invoked, and that response may contain database rows.
- Clearing a scope takes effect on the next tool call; nothing already sent is recalled.
- A scope the host disabled for the connection is shown in the menu but cannot be turned on.
- When something is needed that is not shared, the agent asks, and the person answers with Allow or
  Deny on a card in the transcript. Allowing grants access until the person revokes it, and the
  running answer continues rather than starting over. Context already sent cannot be recalled; if
  a later answer needs new or updated context, ask again.
- The model is chosen per conversation. A local model keeps everything on the host machine;
  an external provider receives the questions and whatever database context was shared.
- Transcripts are saved in the person's own browser, never in the database or the Gridlet
  store.
- The gauge around the send button shows how much of the model's context window is used, for
  providers that report it.

Use `get_shared_database_access` to check what is shared right now, and
`request_database_access` to ask for more.

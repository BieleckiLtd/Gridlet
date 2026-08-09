The query editor runs SQL against the selected connection and database, but only when that
connection has SQL execution enabled.

- Results stream in as they arrive and the row count updates while they do. When the safety
  cap is reached the editor says so rather than silently truncating.
- A result set can be exported as CSV or JSON.
- A useful query can be saved by name and reopened later. Saved queries are stored by the
  host, not in the browser, so they are shared by everyone who can reach that Gridlet.
- A saved query is also the starting point for publishing an HTTP endpoint.

- **Sessions and transactions.** Each execution normally gets its own connection, so a `BEGIN
  TRAN` is discarded the moment the statement ends. The `Session` button in the query toolbar pins
  one connection to that tab: `BEGIN`, the statements after it, and the final `COMMIT` or
  `ROLLBACK` are then one unit of work, and the toolbar says whether a transaction is open. This is
  the way to preview a change before keeping it — run the `UPDATE`, look at the rows, then commit or
  roll back. Buttons for begin, commit and rollback sit beside the toggle, and typing the statements
  works the same way. A session ends when the person closes it or the tab, or after it has been
  idle for the configured timeout; ending it always rolls back rather than commits.

Write queries that are bounded by construction: select the columns actually needed, filter in
SQL rather than in the grid, and prefer aggregates when the question is about totals rather
than individual rows.

The editor's safety cap protects the browser and the server, not the database. A query that scans
a hundred million rows still scans them before the cap discards what will not fit, so the cap is
not a substitute for a `TOP` or `LIMIT` in the query you hand somebody. Put the limit in the SQL.

The editor will run whatever it is given, including statements that change or destroy data, on any
connection where SQL execution is enabled. That is the person's decision to make, and it is a
decision they should be able to make knowingly: before a change runs, they should know what it
targets, roughly how many rows it will touch, and whether it can be undone. Show them the `SELECT`
that identifies those rows first — it costs one extra step and it is the difference between a
change and an accident.

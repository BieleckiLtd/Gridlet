The Gridlet page has four regions.

- The header picks the connection and then the database within it. Every tab remembers the
  connection and database it was opened against, so switching the header does not silently
  repoint an open tab.
- The left sidebar is the object browser: tables, views, and, on providers that support
  them, stored procedures, functions, and triggers. Gridlet hides object types the selected
  provider does not have.
- The middle is a tab strip. Opening a table gives a streaming, sortable data grid plus a
  structure view with columns, primary key, indexes, and foreign keys, and a definition view
  for views, procedures, functions, and triggers. The grid is not read-only: rows are added,
  edited, and deleted in it directly, which the `editing-data` topic covers.
- Tabs also host the query editor, the table designer, published API previews, and Ask
  conversations like this one.

Rows stream in progressively rather than loading all at once, and the grid is capped by the
host's `MaxQueryResultRows` limit so a careless `SELECT *` cannot exhaust the browser or the
server.

The grid's `⧩ Filter` button adds conditions on a column — equals, contains, starts with, a
comparison, or is null — and they run in the database, over every row of the table, not over the
rows already fetched. The row count reflects the filter. Several conditions combine with AND, and
each one can be removed on its own. This is the answer to "find the row where…" without leaving the
grid for the query editor.

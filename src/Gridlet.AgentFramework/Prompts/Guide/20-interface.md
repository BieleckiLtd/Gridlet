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

Each column header has a filter button that works like a spreadsheet's AutoFilter: sort commands,
a searchable checklist of the column's values with (Blanks), and conditions chosen by the column's
type. Text Filters offer equals, begins with, contains and their opposites, with `*` and `?`
wildcards. Number Filters add comparisons, Between, Top 10 and Above or Below Average. Date Filters
group the checklist by year, month and day, and add Before, After, Between, relative periods such
as This Week or Last Month, and All Dates in the Period. A text column that holds ISO dates filters
as dates. Filters run in the database, over every row of the table, not over the rows already
fetched, and the row count reflects them. Each column holds one filter, the columns combine with
AND, and each filter's chip above the grid removes it. This is the answer to "find the row where…"
without leaving the grid for the query editor.

Right-clicking a foreign-key value offers to follow it: the referenced table opens in a tab,
filtered to the row that key points at.

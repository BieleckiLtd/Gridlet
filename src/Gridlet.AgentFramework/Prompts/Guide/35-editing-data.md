Gridlet edits rows directly in the data grid, so changing or deleting a row is usually a click and
a keystroke rather than a SQL statement. When somebody asks how to change or remove data, this is
the answer to lead with.

- **Delete rows.** Select one or more rows by clicking the numbered gutter on the left of the grid,
  then press `Delete`. A confirmation dialog names how many rows will go and says it cannot be
  undone; nothing is deleted until they confirm. Gridlet deletes each row by the key that identifies
  it.
- **Edit a row.** Click any cell in it. The row opens in an editor where the values can be changed
  and saved. `Tab` at the end moves to the next row.
- **Choose a foreign-key value.** Foreign-key columns can optionally show the stored key followed
  by a dimmed, human-readable value from any column of the referenced table. This is off by default;
  enable it for a foreign key in the table's **Structure** view with **Show value…**, then choose the
  referenced column to display. Selecting an enabled cell opens a searchable, virtualized list.
  A person can choose a suggestion or type an exact key directly; only the key is written, never the
  displayed referenced value. Clearing a nullable foreign key writes `NULL`. An italic `#REF!`
  means the stored key no longer resolves to a referenced row.
- **Add a row.** The `＋ Row` button above the grid opens the same editor with an empty row.
- **Copy rows.** Select rows and press `Ctrl+C` (`Cmd+C` on a Mac) to copy them as tab-separated
  text, ready to paste into a spreadsheet.

Three conditions have to hold for any of this to appear, and if somebody says they cannot see it,
these are the things to check in order:

1. The object is a **table**. Views, procedures, and functions are read-only in the grid.
2. The connection has **`AllowWrites` enabled**. It is on by default, but a developer may have
   turned it off for this connection — `describe_gridlet_deployment` reports which.
3. Gridlet can **identify a single row**. It uses the primary key when there is one. Without one it
   falls back to a unique key over non-nullable columns on SQL Server, and to the `rowid` on SQLite,
   so heaps and rowid tables are editable too. A table where none of those exist — a SQL Server heap
   with no unique key, or a WITHOUT ROWID table with no usable primary key — gets a read-only grid
   even when writes are allowed. Adding a primary key is a designer or DDL change.

The SQL editor is the other route, and the one to use when the change is not a handful of rows a
person can point at — a conditional update across many rows, a delete driven by a subquery, or
anything they want to keep and re-run. It runs whatever it is given on any connection where SQL
execution is enabled, including statements that change or destroy data, so a statement offered for
that editor carries the responsibilities described in your instructions: a `WHERE` clause, a plain
statement of what it affects, and the `SELECT` that shows those rows first.

Row edits, inserts, and deletes made through the grid are written to the audit log, the same as any
other change.

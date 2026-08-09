Published endpoints are uncapped by default: they stream every row the query returns,
independent of the `MaxQueryResultRows` limit that governs the UI. That is deliberate, and it
makes paging the query author's job.

Page in SQL, using required parameters:

```sql
SELECT CustomerId, Name
FROM dbo.Customers
ORDER BY CustomerId
OFFSET ((@page - 1) * @page_size) ROWS
FETCH NEXT @page_size ROWS ONLY;
```

An endpoint can also opt into a hard cap with `maxRows`: omitted, `null`, `0`, or negative
means uncapped; a positive number caps at that many rows.

What makes a published endpoint hold up under real traffic:

- Always `ORDER BY` a unique, stable key when paging. Without it the database may return the
  same row on two pages and skip another.
- Support the query with an index that matches its filter and sort. An endpoint called
  thousands of times a day runs its query thousands of times a day.
- Select named columns, never `SELECT *`. It keeps the payload small and stops the response
  shape from changing when someone adds a column.
- Prefer keyset paging (`WHERE Id > @after ORDER BY Id`) over large `OFFSET` values; `OFFSET`
  cost grows with the offset, keyset cost does not.
- Return aggregates when the caller wants a total. Sending a million rows so the client can
  count them wastes both ends.
- Cap anything a caller controls. A `@page_size` with no upper bound is a request for
  trouble; clamp it in SQL with `LEAST`/`TOP` or validate it before publishing.
- Give each endpoint a route that names the resource (`customers/by-city`), and use `GET`
  for reads so caches and browsers behave predictably.
- Attach an authorization policy to anything not meant to be public, and consider publishing
  under a separate, lower-privilege database identity.

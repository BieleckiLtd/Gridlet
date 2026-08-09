Parameters are how one published endpoint answers many questions instead of being hard-coded
to one.

- Write `@name` placeholders in the SQL, then declare each one as a parameter of the
  endpoint. A caller supplies values by query string on `GET` or in the JSON body otherwise.
- Each parameter is declared `auto`, `string`, `integer`, `number`, or `boolean`. `auto`
  keeps whatever a JSON client sent and treats query-string values as text.
- A parameter is required or optional. A missing optional parameter binds as `NULL`, which
  is what makes "filter only when a value was supplied" possible:

```sql
SELECT CustomerId, Name, City
FROM dbo.Customers
WHERE (@city IS NULL OR City = @city)
ORDER BY CustomerId;
```

Always bind values as parameters. Never build the SQL by pasting a caller's value into the
query text — that is how SQL injection happens, and a parameter is both safer and faster
because the database can reuse the execution plan.

Declare the narrowest type that fits. An `integer` parameter rejects `'12 OR 1=1'` before it
ever reaches the database, whereas `auto` would pass the text through to be compared.

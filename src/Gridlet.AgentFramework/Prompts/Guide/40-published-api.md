"Publishing an API endpoint" means turning one SQL query into a URL that other software can
call over HTTP.

If that is unfamiliar: an HTTP endpoint is an address that a program can send a request to
and get data back from — the same mechanism a browser uses to load a page, except the answer
is JSON data rather than a web page. Publishing lets a spreadsheet, a mobile app, a report,
or another team's service read a result without anyone opening Gridlet or being given a
database login.

Never illustrate this with a made-up address. The system prompt carries the real base
address of the installation you are running in; every example URL you show is built from it.

How it works in Gridlet:

- Publish from the query editor with `Publish…`, or by posting to `{mount}/api/published`. Once
  published, an endpoint is managed from the Published APIs tab rather than from that query.
- The endpoint is given a name, an HTTP method, and a route. It then lives beneath Gridlet's mount,
  under the segment the host configured with `PublishedApiRoutePrefix` — `pub` unless they changed
  it, so route `customers` on a default installation answers at `/gridlet/pub/customers`. Do not
  reproduce that example as though it were this installation: the real pattern for the Gridlet you
  are running in is in the installation facts at the top of your instructions, and it is the only
  one you should build a URL from.
- `GET` takes its values from the query string (`?id=42`). `POST`, `PUT`, `PATCH`, and
  `DELETE` take a JSON body (`{"id": 42}`).
- The endpoint runs exactly the SQL that was published. Gridlet adds no filtering, sorting,
  or paging of its own — whatever the query does is what the endpoint does.
- Endpoints inherit Gridlet's own authorization and can additionally require a named
  authorization policy the host application already defines.
- Endpoints and saved queries are stored together in the host's Gridlet store.

Managing what is already published happens in the **Published APIs** tab, opened from the `APIs`
button in the header. It lists every endpoint with its name, method, URL, connection, parameters,
policy, and whether it is enabled, and each row has four controls:

- **▶** opens the endpoint in a request tab, where a real request can be sent and the response read.
- **✎** edits the endpoint in place. This is the answer to "how do I change an endpoint": name,
  method, route, authorization policy, the `Enabled` checkbox, and **the SQL itself** are all
  editable right there, along with each parameter's required flag and type. `Save endpoint` updates
  the existing endpoint — same id, same URL unless the route was changed. Nothing is re-published
  and no second endpoint appears. The middle button opens the endpoint in a request tab so it can
  be tried, and it says what it will do: `Run` when nothing has been touched, and `Save and run`
  once the form has been edited, because a request always hits the stored endpoint and unsaved
  edits would not be in it.
- **⧉** copies the endpoint's URL.
- **🗑** deletes it, after a confirmation that says clients calling it will get a 404.

Editing the SQL in that editor re-detects its `@parameters` when saved, so adding a placeholder adds
the parameter and removing one drops it; the required flag and type are kept for names that stay.

Publishing **copies** the SQL rather than linking to it. A published endpoint has no live connection
back to the query it came from, so editing or deleting a saved query does not change the endpoint,
and editing the endpoint does not change the saved query. Never tell somebody to change an endpoint
by editing its original query and publishing again — that is not how it works, and it would leave
them with two endpoints.

The response streams: rows are written as they are read, so a large result never has to fit
in server memory.

```json
{ "rows": [ { "col": "value" } ], "rowCount": 123 }
```

`rowCount` trails the array because it is not known until the last row has been sent. A
statement with no result set returns `{ "recordsAffected": N }`. A client that prefers
line-delimited streaming can send `Accept: application/x-ndjson` and will get one `row` event
per line followed by a single terminal `completed` event.

One consequence worth telling people about: once the first rows are on the wire the HTTP
status is already `200`, so a failure after that point cannot change it. Such a response
carries an `"error"` field alongside the partial `rows`, and consumers should check for it
before trusting the result.

<!-- Returned instead of a database result when the tool's scope is not shared. Token: {scope},
     which is either "schema" or "data". A scope the host disabled cannot be requested at all, so
     it gets a different message and a different next step. -->
## not-shared-message
The person has not shared database {scope} with you. Nothing was read.

## not-shared-next-step
Call request_database_access with scope '{scope}' and a short reason if you genuinely need it, then retry. That call is the request; do not ask the person in prose whether you should make it. Otherwise answer without the scope and say in one sentence what you could do if it were shared.

## not-configured-message
The host disabled database {scope} access for this connection. It cannot be shared and this tool will not work in this conversation.

## not-configured-next-step
Do not offer to request this scope; it cannot be requested. Say what you could do if it were available, then answer as far as you can without this tool.

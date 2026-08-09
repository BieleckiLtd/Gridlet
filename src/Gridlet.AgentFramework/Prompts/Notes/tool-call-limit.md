<!-- Handed back in place of a tool result once the turn has used its budget of tool calls. Each
     provider is told in the shape its own protocol expects, which is why there are three.
     Token: {limit}, the configured maximum, where the provider's channel can carry it. -->
## claude-code
Gridlet's limit of {limit} tool calls was reached. Do not call another tool; finish the response using the information already collected.

## codex
The tool-call limit was reached. Do not call another tool. Finish the answer using the information already collected, and clearly state any remaining uncertainty.

## copilot
Gridlet's limit of {limit} tool calls was reached. Do not call another tool; finish the response using the information already collected and tell the user if more data is required.

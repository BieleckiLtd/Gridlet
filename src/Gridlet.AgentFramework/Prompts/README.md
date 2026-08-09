# Gridlet agent prompts

Every instruction Gridlet gives a model lives in this folder as Markdown. Nothing here is C#, so
the wording can be changed by editing a file. The files are embedded into the
`Gridlet.AgentFramework` assembly at build time, so a change takes effect on the next build.

## Layout

| Folder | What it holds |
| --- | --- |
| `Instructions/` | The system prompt, assembled in this order: `base`, `product-briefing`, `access`, `access-state`, `database-environment`, `installation`. |
| `Instructions/cli-*.md` | Extra text appended for a subscription-backed CLI provider, which has its own tools to be told about. |
| `Guide/` | The product documentation the `get_gridlet_guide` tool serves, one topic per file. |
| `Tools/` | One file per tool: what it does and what each parameter means. |
| `Notes/` | Agent-facing prose returned inside a tool result rather than in the system prompt. |

## File format

A file is plain Markdown. Text before the first `## ` heading is the file's main text; each
`## name` heading starts a named section. Which sections a file must have depends on where it is
used, and the tests in `GridletPromptTests` fail if a required one is missing.

A `{token}` is replaced at runtime with a value from the running installation — for example
`{database_technology}`. Tokens are listed in an HTML comment at the top of each file that uses
them. Leaving a token out of the text is allowed; inventing a new one is not, because nothing
supplies it.

## Adding a guide topic

Drop a file in `Guide/`. The number prefix orders the topic list the agent sees; the rest of the
file name is the topic name it passes to `get_gridlet_guide`. No code change is needed.

## Adding a tool

A new tool needs a matching `Tools/<tool_name>.md`, and every `## ` section in that file must name
a real parameter of the method. Both are checked when the tool list is built, so a typo fails fast
rather than silently dropping a description.

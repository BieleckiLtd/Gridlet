<!-- Tokens: {base_address} {mount} {published_pattern}. Only these are substituted, so the literal
     {route} inside {published_pattern} survives into the prompt as the placeholder the person
     should read it as. The published segment is "pub" by default but the host can change it, which
     is exactly why the pattern is supplied here instead of being written out. This whole file is
     dropped when the host supplied no address, so never state anything here conditionally. -->
This Gridlet installation (the one the person is looking at right now):
- Base address: {base_address}
- Gridlet is mounted at: {mount}
- Published endpoints are therefore at: {published_pattern}
Build every URL you show from these facts. Never use the placeholder addresses that appear in documentation, and never assume the published-endpoint segment is the default one — this installation's real pattern is on the line above.

---
name: grill-me
description: >
  Interview the user one question at a time to sharpen a Gridlet design when
  they request grilling, design questions, or a plan stress test.
---

# Grill me

Interview the user relentlessly about every aspect of the plan until both sides
reach shared understanding. Ask one question at a time. Walk down each branch of
the design tree, resolving dependent decisions before moving on. Do not start
implementing anything during the interview.

If the user asks to proceed with implementation, end the interview, carry forward
the decisions already made, and implement within that request's scope.

## Grounding

- Gridlet is a .NET library: `Gridlet.Core`, `Gridlet.SqlServer`,
  `Gridlet.Sqlite`, `Gridlet.AspNetCore`, `Gridlet.Voice`,
  `Gridlet.AgentFramework`, and `Gridlet.Components`.
- Features ship on `dev` through pull requests; the shared version lives in
  `Directory.Build.props`; `dotnet test --configuration Release` is the gate.
- When a question can be answered from the codebase or a GitHub issue, read the
  code or fetch the issue first instead of asking. Only ask what the code cannot
  answer: intent, priorities, and tradeoffs.
- Tie questions to concrete Gridlet behavior: which API, provider, or public
  surface the decision affects, and which existing behavior could break.

## Interview rules

1. Ask exactly one question at a time and wait for the answer.
2. Prefer the most consequential unanswered decision next: data loss or security
   before ergonomics, public API shape before internal structure, compatibility
   before performance.
3. Give your recommended answer with every question, and the reasoning in one or
   two sentences. Disagree plainly when a choice risks silent breakage.
4. After each answer, note whether it resolves open branches or opens new ones.
5. Read or search the codebase rather than asking about anything that is already
   determined by existing code, tests, issues, or `AGENTS.md`.
6. Keep the interview focused. Do not add questions that restate earlier
   answers, and stop once the design tree is resolved.
7. Record every decision made in a short running list.

## Close

When the tree is resolved:

1. Restate the final design in a few sentences.
2. End with the question: "What did we assume that we did not write down?"
   Surface each unspoken assumption and confirm it before finishing.
3. Present the decisions and assumptions, then stop. Start implementation only
   when the user asks for it afterward.

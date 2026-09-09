# AstraDocs: a gentle onboarding to Agora

Welcome. This folder explains this repository in small steps, with several passes over the same ideas. You can read a concept, see it drawn, follow its code, try it, and explain it back. Take as many passes as you need. The page numbers are a route, not a deadline.

You do not need to memorize the repository. Your first goal is to answer three questions: **What is this request asking? Where is it handled? What changes, if anything?**

## Start with one small session

**Building all the stories:** follow the [implementation bootcamp](bootcamp/README.md), including its live tracker, journal, worked lessons, and exercises. The story files below preserve the original specifications; the bootcamp records what is actually implemented and verified.

Read [the big picture](01-big-picture.md). Stop after its three questions. On your next pass, use [the folder map](03-find-your-way.md) to locate the files it names. Running the application can wait until [the hands-on lab](09-hands-on-lab.md).

If you already feel overloaded, read only this:

> Agora is the backend of a practice online shop. A client sends a request. C# code decides what to do. Some requests read or write a SQLite database. The API sends a response.

That is enough to begin.

## Choose another explanation when one does not click

| I would like... | Open |
| --- | --- |
| The system explained without much code | [01: Big picture and shop analogy](01-big-picture.md) |
| Unfamiliar words translated | [02: Words and symbols](02-words-and-symbols.md) |
| To know which file to open | [03: Find your way](03-find-your-way.md) |
| A story of one request | [04: A browsing request](04-browsing-story.md) |
| Small pieces of actual code explained | [05: Read the code slowly](05-read-code-slowly.md) |
| To understand changing data | [06: Add an item, three ways](06-adding-an-item.md) |
| A picture of checkout and stock | [07: Checkout storyboard](07-checkout-storyboard.md) |
| Help separating objects, rows, and JSON | [08: Follow the data](08-follow-the-data.md) |
| Commands to try, with expected observations | [09: Hands-on lab](09-hands-on-lab.md) |
| Help when something goes wrong | [10: Debugging without guessing](10-debugging.md) |
| An introduction to the tests | [11: Tests as examples](11-tests-as-examples.md) |
| A small first contribution | [12: First change](12-first-change.md) |
| Repetition, flashcards, and a blank worksheet | [13: Revisit and recall](13-revisit-and-recall.md) |
| Answers with explanations | [14: Answer key](14-answer-key.md) |
| A patient onboarding plan for a teammate or mentor | [15: Mentor guide](15-mentor-guide.md) |
| A small feature to build with detailed steps | [16: 25 junior user stories and implementation plans](16-junior-user-stories.md) |
| A larger feature to own across data, rules, and tests | [17: 30 mid-level feature stories and guided plans](17-midlevel-user-stories.md) |
| Practice designing features with concurrency, access, and rollout concerns | [18: 20 mid/senior feature stories and guided plans](18-mid-senior-user-stories.md) |

## How the repetition works

We reuse a shopper buying a tee. First you see the shop. Then you follow browsing. Then you add a tee to a cart. Later you revisit the same events through objects, database writes, and tests. When a page repeats something, look for the new detail it adds.

The stock examples use small invented quantities. The live lab reads your actual local stock. Example labels such as `P1` and `V1` are drawing labels, not real IDs you can paste into requests.

Each numbered lesson has a small outcome, a stopping point, and a link onward. Questions labeled Q1–Q18 have a matching [answer key](14-answer-key.md). Try a prediction first, then check it; a wrong prediction is useful information about what to revisit.

## Relationship to the other documentation

This folder is the slower onboarding route. [docs/learning](../docs/learning/README.md) continues into independent feature work and advanced engineering judgment. [The API reference](../docs/api-reference.md) is for looking up contracts; it is not required reading on day one.

The code is the authority for current behavior. Lessons link to actual files and name methods to find. Existing limitations, including order access and payment recovery, are recorded in [the review findings](../docs/learning/review-findings.md). Advanced topics are clearly marked so you can return to them later.

**Begin:** [01: The big picture](01-big-picture.md).

## For documentation contributors

Run `./scripts/verify.ps1 -Suite Docs` from PowerShell to check local file links
in the root README, `docs`, and `astradocs`, without requiring the .NET SDK.
If Windows blocks scripts, use
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/verify.ps1 -Suite Docs`.
This process-only setting does not change the saved machine policy.
The checker verifies file targets, not section anchors or external URLs.

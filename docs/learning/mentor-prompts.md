# Ask for help that builds independence

Use these prompts with a teammate or coding assistant. Attach your hypothesis and attempted experiment so help starts at the right level.

## When starting

> I am working on Agora lesson __. My current understanding is __. Ask me three prediction questions about the relevant code, then give me one bounded exercise. Let me attempt it before showing a solution.

## When stuck

> For input __ I expected __ but observed __. I inspected __ and tried __. Help me distinguish my hypothesis __ from another likely cause. Give one hint and one experiment first.

## Before implementing

> Here is my contract and proposed test matrix. Find ambiguous behavior or a missing counterexample. Ask me to resolve it before generating implementation code.

## During review

> Review this diff in order: requirement correctness, authorization, state and failure handling, data/query cost, test strength, then readability. Identify concrete risks with file references. Ask me to explain one tradeoff.

## Practicing senior-level judgment

> Challenge this design as a maintainer and operator. Choose a process-interruption point, a concurrency case, and a rollout compatibility issue. Ask how my design recovers and what evidence would prove it.

## Checking understanding

> Ask me to explain this feature to a new teammate without reading the code. After my explanation, identify missing causal steps and suggest one transfer exercise in another endpoint.

You can ask for a full worked solution when needed. Then close it and rebuild the reasoning yourself on a different example. The goal is being able to make and defend the next change independently.

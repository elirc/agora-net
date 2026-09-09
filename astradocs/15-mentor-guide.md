# 15: Onboard a teammate with patience and concrete checks

[Home](README.md) · Previous: [Answer key](14-answer-key.md)

This guide is for a teammate, mentor, or coding assistant helping someone use AstraDocs. Let the learner choose the pace. Offer different representations of a concept without assigning them a fixed "learning style" or interpreting repetition as a lack of ability.

## A session with one outcome

Agree on a small outcome such as "explain why adding a cart item does not reserve stock." Ask what they currently expect. Read one short section together. Draw its table or flow. Let them point to the code. Run one observation if useful. Ask them to explain it back using different numbers.

End with the learner's own sentence and one next question. It is fine to finish after one concept. Time boxes such as ten or twenty minutes are options, not passing criteria.

## Repeat by changing the representation

| If this explanation did not land... | Try this next | Check understanding with... |
| --- | --- | --- |
| The folder diagram | Follow one endpoint through four actual files | "Which file would you open for this rejected page size?" |
| The word "persistence" | Show the memory/database table | "What survives a new HTTP request, and why?" |
| The stock formula | Draw five boxes and mark two reserved | "What changes when those two are committed?" |
| The LINQ expression | Test each variant against each bound on paper | "Can any one row satisfy both conditions?" |
| An integration-test explanation | Compare it side by side with `CartTests` | "Which one reaches SQLite?" |
| A correct memorized answer | Change the quantities or use another endpoint | "Does the explanation still predict the result?" |

Use the same words for the same concepts. Do not rename the controller three times inside one explanation. Introduce an analogy, connect it to the actual file, and state where the analogy stops being accurate.

## Questions that reveal reasoning

- "What do you expect before running this?"
- "Which exact input made you expect that?"
- "Where does this value come from?"
- "Has the database changed yet, or only an object?"
- "What would this test miss?"
- "What could we check next to tell those two explanations apart?"

Avoid relying only on "Does that make sense?" A learner can agree while still missing a causal step. Ask for an example, a drawing, or a prediction instead.

## When the learner is stuck

Reduce the problem to one request and one value. Offer one hint, then give them time to use it. If they need the answer, explain it clearly and revisit with a new example later. Do not make withholding the answer into a test of persistence.

Separate tooling problems from code understanding. A missing SDK, package-source issue, or occupied port can block a good explanation. Help resolve the environment, then return to the concept.

## Suggested onboarding sequence

First, build the request/response picture and locate the files. Next, browse products and follow query execution. Then add an item and distinguish object changes from saves. Revisit those same events through data shapes and tests. Finish this onboarding with the exact-limit cart test and a short review conversation. Introduce checkout failure recovery after the ordinary read/write path is comfortable.

## Handoff checklist

A learner is ready for a small independent ticket when they can trace one request, find its input rules, distinguish reads from saves, choose a relevant test, and ask for help with expected versus actual behavior. They can keep notes and look up names. Speed and unaided recall of every file are not the target.

Use [the broader learning roadmap](../docs/learning/roadmap.md) when they want the next stage. Keep known implementation gaps, such as payment recovery and order authorization, visible without presenting them as prerequisites for understanding their first controller.

## A reusable assistant prompt

> Help me with AstraDocs page __. My current understanding is __. Teach one concept at a time using the code, then a diagram or concrete example. Ask me to predict one result and wait for my attempt. If I am stuck, give one hint and then explain fully if needed. Repeat the idea later with different numbers. Do not move to the next topic until we have checked the current explanation together.

# Progress journal template

Copy this into your personal notes for each session. Keep real tokens, addresses, and passwords out of examples.

## Session

- Date and lesson/ticket:
- Smallest outcome I intend to demonstrate:
- My prediction before running code:
- Request/test command and relevant result:
- Expected behavior versus observed behavior:
- Root cause, with a file or method reference:
- What I changed and why this boundary owns the change:
- Regression that would fail if the bug returned:
- Full verification result, including anything I could not run:
- Compatibility or operational limit:
- What I can now explain without looking:
- What still confuses me:
- Next smallest experiment:

## Weekly reflection

Pick one artifact to demonstrate: a test, PR, query plan, design note, or incident analysis. Explain what was difficult, what evidence changed your mind, and what you would simplify next time. Update one rubric score with a link to that artifact.

## Example of useful evidence

"I predicted the product with variants 10 and 100 would be excluded by 20-40. Separate `Any` expressions admitted it because each had a different witness. I can explain why the single-variant predicate fixes it. My integration test also includes an in-range product so an always-empty implementation fails."

That is more informative than "finished LINQ chapter." Record the causal explanation, not just task completion.

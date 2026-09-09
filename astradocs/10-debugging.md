# 10: Debugging without guessing

[Home](README.md) · Previous: [Hands-on lab](09-hands-on-lab.md) · Next: [Tests](11-tests-as-examples.md)

**Small outcome:** turn "it does not work" into a specific question you can investigate.

## A repeatable sentence

> For input __, I expected __, but observed __. The earliest point where my expectation may be wrong is __.

For example: "For `pageSize=101`, I expected a product list, but observed 400. I need to check whether 101 is an allowed page size." You can answer that by opening `ProductSearchRequest.PageSize`; no database investigation is necessary yet.

## A decision path

```text
Did the command start?
  No -> check SDK, command, current directory, and restore error.
  Yes -> did the request reach a listening API?
    No -> check terminal A, address, port, and connection error.
    Yes -> inspect the HTTP status and body.
      400 -> check input shape, types, and validation rules.
      404 -> check route and actual resource identifier.
      409 -> inspect the stock/state/concurrency conflict.
      500 -> inspect the server exception and its first relevant code frame.
      200 with surprising data -> compare filters, fixture data, and query logic.
```

This is a starting path, not an exhaustive status-code reference. Use the actual response details and [API reference](../docs/api-reference.md) to narrow it further. Authentication failures also have their own 401/403 paths.

## Watch one value with a debugger

Start the API under your editor's debugger and set a breakpoint inside `ProductsController.List`. Send `pageSize=2`. Inspect `request.PageSize`, then step to the query construction and page load.

Send `pageSize=101`. The action-body breakpoint should not be reached for that invalid model. Binding/validation rejects the request earlier. A breakpoint that is not hit can tell you where the behavior happens; it does not automatically mean your debugger is broken. First confirm the valid request hits it.

Without a debugger, send the requests with `curl.exe -i`, inspect their responses, and compare the validation attributes to the requested values.

## Repeat the cart lesson as a debugging case

Symptom: "I added two units but available stock did not decrease."

Before fixing anything, ask whether that is the intended behavior. Find `CartsController.AddItem`. It checks available stock but does not call `Reserve`. Find `CheckoutService.CheckoutAsync`, which does reserve stock. The observation is consistent with the current design.

A useful investigation can end with "the system is behaving as designed; my expectation needed correction."

## Ask for help with evidence

Send the request shape, expected result, actual status/body, relevant method, and what you already checked. Redact tokens and personal data. A minimal input that reproduces the issue helps more than a large unfiltered log.

**Q15:** A valid request reaches `List`, but `pageSize=101` returns 400 before its breakpoint. Which code should you inspect first?

**Stop:** write a debugging sentence for one observation from the lab. Include one next check, not a list of ten unrelated guesses. [Answer](14-answer-key.md).

# Treat HTTP as a contract

**Outcome:** define caller-visible behavior before implementing a feature.

For a query endpoint, specify parameter names, defaults, accepted values, empty-result behavior, ordering, pagination, and errors. For a mutation, also specify who can act, what persists, whether a repeat is safe, and where the caller finds the created resource.

Agora uses request DTOs so the public input is separate from storage entities. `[ApiController]` validates bound input and returns HTTP 400 for invalid model state. `IValidatableObject` handles rules involving multiple properties, such as minimum not exceeding maximum. See [ASP.NET Core validation](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-10.0).

## Status codes tell callers what happened

| Agora response | Example | Caller interpretation |
| --- | --- | --- |
| 200 with empty `items` | A valid search has no matches | Search completed successfully |
| 201 | A product was created | A new resource exists; inspect its location |
| 400 | Invalid query bounds or malformed input | Correct the request |
| 401 / 403 | No accepted identity / identity lacks permission | Authentication and authorization are distinct |
| 404 | Requested resource absent | Resource cannot be found through this route |
| 409 | Stock, state, or concurrency conflict | State has changed or forbids the action |
| 422 | Referenced shipping method cannot be used | Syntactically accepted request has a semantic rejection |

These are the repository's conventions, not a claim that every API must make identical choices. Read `DomainExceptionFilter` to see which exceptions map to which responses. A returned 409 alone does not prove that no earlier external side effect occurred.

## Compatibility is observable behavior

Changing an unknown sort from newest to 400 can break existing callers even though the new rule looks tidier. Returning only matching variants can break a client that uses the full list for selection. The worked example preserves those existing behaviors and explicitly documents the new rejection of invalid ranges and literal wildcard search.

**Exercise:** draft the contract for a `sku` catalog filter. Should it be exact or substring? Case-sensitive? Can it combine with currency and stock through different variants? What happens when it is blank? Write five request/response examples before touching code.

**Checkpoint:** hand the contract to someone else and ask them to predict an edge-case response. If they cannot, identify the missing rule. **Stretch:** explain how you would introduce a breaking change while supporting existing clients.

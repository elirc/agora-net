# C# concepts you can point to in this repository

**Outcome:** read the important syntax and connect it to runtime behavior. Use tiny experiments when the words feel abstract.

## Types carry decisions

`Guid? CategoryId` means the caller may omit the filter. `bool? InStock` has three states: omitted, true, and false. A plain `bool` would lose the distinction between "do not filter" and "find unavailable variants." Exercise: predict how `/api/products`, `?inStock=true`, and `?inStock=false` differ for a product with one available and one unavailable variant.

`request.CategoryId is { } categoryId` matches a non-null value and gives it a local name. `product.Category!` suppresses a compiler nullability warning; it does not create an object or validate it. In an EF expression, navigation access is translated into SQL; in normal in-memory C#, dereferencing an actual null fails. Always know which context you are reading.

`ProductSearchRequest` uses `init` properties so callers can initialize the contract without casually mutating it afterward. `PagedResult<T>` is a generic record: the same response envelope can contain products or categories. Read its `TotalPages` expression and evaluate it for 21 items with page size 20.

## State versus value

An `InventoryItem` has identity and changing state. A `Money` represents an amount and currency with value semantics. Read [Money.cs](../../src/Agora.Domain/Common/Money.cs), especially rounding and currency checks. Try these in a scratch test: 1.005 USD; 1 USD plus 1 EUR; subtracting more than the balance. Predict the result first. The current Money subtraction clamps at zero: discuss where that is useful and where it might hide an upstream error.

Private setters on inventory force normal callers through guarded methods. They do not replace database constraints or concurrency control. Ask what happens if two valid objects were loaded at different times.

## Deferred queries versus in-memory loops

`IQueryable<Product>` describes work a provider can translate. Adding `Where` builds an expression; it does not retrieve the catalog. `ToListAsync` executes the query and materializes results. After materialization, ordinary LINQ operates on objects in memory. Moving `ToListAsync` before filtering changes both memory usage and which system evaluates your conditions.

Write two pseudocode sequences: filter -> page -> load, and load -> filter -> page. For one million rows with twenty matches, explain the data transferred in each. Do not claim a timing difference without measuring.

## Async and lifetimes

`await` lets the request yield while asynchronous I/O is pending. It does not make each statement run on a separate thread. `CancellationToken` carries a request to stop cooperating work. Cancellation after a payment was accepted cannot undo the payment.

The application's `DbContext` is scoped. Do not run simultaneous queries on the same context with `Task.WhenAll`, and do not inject a scoped context directly into a singleton worker. A future worker needs its own scope per unit of work. Locate the singleton fake gateway and scoped checkout service registrations in `Program` and explain why their lifetimes differ.

**Checkpoint:** explain nullable booleans, deferred execution, and cancellation in your own words, using one concrete line of Agora code for each. **Stretch:** find a `!` suppression and state the invariant it assumes.

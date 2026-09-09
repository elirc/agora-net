# 03: Find your way through the folders

[Home](README.md) · Previous: [Words](02-words-and-symbols.md) · Next: [Browsing story](04-browsing-story.md)

**Small outcome:** find the file responsible for a behavior without opening everything.

## Map view

```text
agora-net/
  Agora.slnx                 groups the .NET projects into a solution
  src/
    Agora.Api/               HTTP host, controllers, input/output contracts
    Agora.Domain/            business objects and guarded methods
    Agora.Infrastructure/    persistence and multi-step services
  tests/Agora.Tests/          executable examples and regressions
  docs/                      reference docs and broader learning curriculum
  astradocs/                 this gradual onboarding route
  scripts/verify.ps1         documentation-link and test checks
```

The source project references go `Api -> Infrastructure -> Domain`. They are built and run together as this application. Domain does not reference the API. The tests reference the API and can use the other projects through it.

## Task view: "I want to find..."

| Question | Open this file | Find this name |
| --- | --- | --- |
| Where does the application start? | [Program.cs](../src/Agora.Api/Program.cs) | `WebApplication.CreateBuilder` |
| What handles product browsing? | [ProductsController.cs](../src/Agora.Api/Controllers/ProductsController.cs) | `List` |
| Why is a page size rejected? | [ProductSearchRequest.cs](../src/Agora.Api/Contracts/ProductSearchRequest.cs) | `PageSize` |
| How does stock filtering work? | [ProductCatalogQuery.cs](../src/Agora.Api/Queries/ProductCatalogQuery.cs) | `inStock` |
| What happens when I add to a cart? | [CartsController.cs](../src/Agora.Api/Controllers/CartsController.cs) | `AddItem` |
| Why does adding the same variant merge? | [Cart.cs](../src/Agora.Domain/Entities/Cart.cs) | `AddItem` |
| How are stock rules enforced? | [InventoryItem.cs](../src/Agora.Domain/Entities/InventoryItem.cs) | `Reserve` |
| How is checkout coordinated? | [CheckoutService.cs](../src/Agora.Infrastructure/Services/CheckoutService.cs) | `CheckoutAsync` |
| How are entities mapped to SQLite? | [AgoraDbContext.cs](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs) | `OnModelCreating` |
| How does a domain error become HTTP? | [DomainExceptionFilter.cs](../src/Agora.Api/Filters/DomainExceptionFilter.cs) | `OnException` |

## Walk view: the first four files

Open `Program.cs` and locate `AddControllers`, `AddDbContext`, and `MapControllers`. They configure controller support, register database access, and map controller endpoints. You do not need to understand JWT configuration yet.

Next open `ProductsController.List`. Find its request type. Open that type. Then find `ProductCatalogQuery.Apply` and open the helper. You have now followed one behavior across four files.

Repeat the route aloud: **startup configures the host; the controller receives input; the input contract validates options; the helper composes a query.** The controller then executes the query and returns a response.

## Search view

From the repository root, if `rg` is installed:

```powershell
rg -n 'class ProductsController|class ProductSearchRequest' src
rg -n 'AddItem|MaxQuantityPerLine' src/Agora.Domain/Entities/Cart.cs
rg -n 'SaveChangesAsync' src/Agora.Api/Controllers/CartsController.cs
```

Your editor's "Find in Files" can answer the same questions. Search for a route or method name before browsing folders at random. Read the whole surrounding method once you find a match.

**Stop:** locate the cart quantity limit and its existing tests. You can stop when you can name both files; changing the limit is not part of this lesson.

# Your first hour

**Outcome:** send a request, run a test, and locate the code responsible for each. Prerequisite: a terminal and a .NET 10 SDK. You do not need to know every C# keyword yet.

## 1. Establish a baseline

Open a terminal in the directory containing `Agora.slnx`:

```powershell
dotnet --info
dotnet restore Agora.slnx
dotnet test Agora.slnx --no-restore
```

`restore` downloads dependencies declared in project files. `test` builds the projects and executes tests. A failing restore is a tooling problem; no application test has run yet. Keep the first useful error, not just the last line.

On Windows, if `dotnet` is not on PATH but exists in your user install, use this for the current terminal only:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --info
```

If restore reports that no packages can be resolved, inspect `dotnet nuget list source`. For this public-dependency repository you can try `dotnet restore Agora.slnx --source https://api.nuget.org/v3/index.json`. Do not change package versions to disguise a source configuration failure. SDK installation instructions: [Microsoft's Windows guide](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

## 2. Observe a running system

In terminal A:

```powershell
dotnet run --project src/Agora.Api
```

In terminal B:

```powershell
$baseUrl = 'http://localhost:5077'
Invoke-RestMethod "$baseUrl/health"
$catalog = Invoke-RestMethod "$baseUrl/api/products?search=tee&pageSize=2"
$catalog | ConvertTo-Json -Depth 8
Invoke-RestMethod "$baseUrl/api/products?maxPrice=30&inStock=true&currency=USD"
```

The development launch profile creates and seeds a local SQLite database. No separate database server is needed. `ConvertTo-Json -Depth 8` exposes nested variants instead of displaying only their type names. You can also send the requests in [Agora.Api.http](../../src/Agora.Api/Agora.Api.http) from an editor with HTTP-file support.

Before running the second request, predict whether `pageSize=2` changes `totalCount`. It should limit `items`, while `totalCount` describes all matches.

## 3. Follow one request

Open [ProductsController](../../src/Agora.Api/Controllers/ProductsController.cs), find `List`, then open [ProductSearchRequest](../../src/Agora.Api/Contracts/ProductSearchRequest.cs) and [ProductCatalogQuery](../../src/Agora.Api/Queries/ProductCatalogQuery.cs).

Set a breakpoint inside `List` if your editor supports debugging. Request `pageSize=2` and inspect `request.PageSize`. Request `pageSize=101`: validation rejects it before the action body. Without a debugger, compare the status and response using `curl.exe -i "http://localhost:5077/api/products?pageSize=101"` on Windows (`curl -i` in Bash).

## 4. Run one relevant test

```powershell
dotnet test --filter FullyQualifiedName~CatalogSearchApiTests.PriceRange
```

Read the fixture, request, and assertion. Draw the two variants with prices 10 and 100. Explain why a requested range 20 through 40 must exclude that product.

**Checkpoint:** show a successful request, a rejected request, and one test result. Explain why a running server is unnecessary for integration tests using `WebApplicationFactory`.

**Next experiment:** request a page beyond the result set. Predict status, item count, and total count, then compare. Record the result in your journal.

## Repeatable checks

From PowerShell, `./scripts/verify.ps1 -Suite Catalog` checks local Markdown file links
and runs the catalog learning tests. Use `-Suite Domain` for selected money, stock,
and order-state tests, or omit `-Suite` for the full suite. The script detects a
user-local Windows SDK if it is absent from PATH. It reports failures rather than
silently marking a lesson complete; link anchors and external URLs are not checked.

If Windows blocks `.ps1` execution, run the checked-in script in a single process:
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/verify.ps1 -Suite Catalog`.
This does not change the machine's saved execution policy. The direct `dotnet test`
commands above also work without a PowerShell script.

For documentation edits alone, `./scripts/verify.ps1 -Suite Docs` checks file links
across both `docs` and the gradual onboarding in [AstraDocs](../../astradocs/README.md)
without building or running tests.

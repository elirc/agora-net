# 09: Try it in small, observable steps

[Home](README.md) · Previous: [Data shapes](08-follow-the-data.md) · Next: [Debugging](10-debugging.md)

**Small outcome:** browse, create your own practice cart, and observe a saved change. Commands below use **PowerShell**, from the repository root. Run one block at a time. You can stop after any checkpoint.

## A. Find the tools and start the application

```powershell
dotnet --info
```

If PowerShell cannot find `dotnet` but your SDK is installed under your user profile:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --info
```

Look for a .NET 10 SDK. If it is missing or restore fails, use [the setup guide](../docs/learning/01-first-hour.md) before continuing. A tooling failure does not mean you misunderstood the application.

In terminal A:

```powershell
dotnet restore Agora.slnx
dotnet run --project src/Agora.Api
```

Expected observation: the process stays running and announces its listening address. The development profile uses `http://localhost:5077`; it applies migrations and seeds demo data when needed. Repeated runs preserve your local data, so later stock and counts may differ from a fresh setup.

Keep terminal A open. Put the remaining commands in **terminal B**. If the printed port differs, change `$baseUrl` accordingly.

## B. Read without changing anything

```powershell
$baseUrl = 'http://localhost:5077'
Invoke-RestMethod "$baseUrl/health"
$page = Invoke-RestMethod "$baseUrl/api/products?pageSize=2&sort=name"
$page | ConvertTo-Json -Depth 8
```

**Predict first:** does page size change the total number of matching products? Expected: `items` contains at most two products; `totalCount` counts all matches. On the seeded catalog there are enough products for two items.

Find the tee variant and inspect its stock:

```powershell
$product = Invoke-RestMethod "$baseUrl/api/products/by-slug/classic-cotton-tee"
$variant = $product.variants | Where-Object { $_.sku -eq 'TEE-BLK-M' }
if ($null -eq $variant) { throw 'Demo variant is missing; inspect your local catalog before continuing.' }
$before = Invoke-RestMethod "$baseUrl/api/inventory/$($variant.sku)"
$before | ConvertTo-Json
if ($before.quantityAvailable -lt 2) { throw 'This example needs two available units. Stop here or use the existing cart integration tests instead.' }
```

This reads your current stock rather than assuming it is still the seed quantity. Do not reset your database just to match a screenshot. **Checkpoint:** point to on-hand, reserved, and available fields and explain their relationship.

## C. Create a cart and save a line

These requests create a practice cart in your local database. They do not check out or charge a payment. Keep the returned cart token private; it grants access to that cart.

```powershell
$cart = Invoke-RestMethod -Method Post "$baseUrl/api/carts"
$body = @{ productVariantId = $variant.id; quantity = 2 } | ConvertTo-Json
$updated = Invoke-RestMethod -Method Post `
    -Uri "$baseUrl/api/carts/$($cart.token)/items" `
    -ContentType 'application/json' -Body $body
$updated.items | Format-Table sku, quantity
$updated.subtotal | ConvertTo-Json
```

Expected: one active line with quantity two. On unchanged seed pricing, the subtotal is 39.98 USD. If local pricing differs, calculate two times the price returned in `$variant.price.amount`.

Now read the cart in a separate HTTP request:

```powershell
$reloaded = Invoke-RestMethod "$baseUrl/api/carts/$($cart.token)"
$reloaded.items | Format-Table sku, quantity
$after = Invoke-RestMethod "$baseUrl/api/inventory/$($variant.sku)"
$after | ConvertTo-Json
```

The reloaded cart demonstrates the saved state. In a local session with no other stock-changing activity, inventory should match `$before`: adding the cart line does not reserve stock. If the values differ, investigate concurrent activity rather than assuming this request reserved it.

**Checkpoint:** connect your observation to `cart.AddItem`, `SaveChangesAsync`, and `CartResponse.From` in [the write walkthrough](06-adding-an-item.md).

## D. Observe a rejected request

```powershell
curl.exe -i "http://localhost:5077/api/products?pageSize=101"
```

This Windows command prints headers and body even for an HTTP error. Adjust its port if you changed `$baseUrl`. Expected: HTTP 400 and a problem response describing invalid input. This is an intentional observation, not a setup failure.

## E. Clear only the practice cart you created

```powershell
Invoke-RestMethod -Method Delete "$baseUrl/api/carts/$($cart.token)"
$cleared = Invoke-RestMethod "$baseUrl/api/carts/$($cart.token)"
$cleared.items.Count
```

Expected count: zero. This route clears the cart's lines; the cart record and token still exist. Do this in the same terminal session so `$cart` still refers to your practice cart. Stop terminal A with Ctrl+C when finished.

## Write four sentences

"I requested __. The response showed __. The database change was __. Stock reservations did/did not change because __."

If you can complete those sentences, you have followed a real read and write through the application. For an alternative without a manually running server, try `dotnet test --filter FullyQualifiedName~CartsApiTests` and continue with [the testing guide](11-tests-as-examples.md).

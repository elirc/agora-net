# agora-net

An e-commerce platform backend built with C# / .NET 10 and ASP.NET Core Web API.

## Overview

Agora is a clean-layered e-commerce backend covering:

- **Catalog** — categories, products with variants (SKU, price, options) and image URLs
- **Search** — product search, filtering, and pagination
- **Inventory** — stock levels with reservation during checkout
- **Carts** — guest carts via token, line items, quantity rules
- **Checkout** — address capture, tax + shipping calculators, discount codes
- **Orders** — lifecycle: pending → paid → fulfilled → cancelled/refunded
- **Payments** — fake payment gateway behind an abstraction

## Solution layout

| Project | Purpose |
| --- | --- |
| `Agora.Api` | ASP.NET Core Web API host, controllers, HTTP concerns |
| `Agora.Domain` | Entities, value objects, domain services, abstractions |
| `Agora.Infrastructure` | EF Core (SQLite), migrations, gateway implementations |
| `Agora.Tests` | xUnit unit + WebApplicationFactory integration tests |

## Getting started

```bash
dotnet build
dotnet test
dotnet run --project src/Agora.Api
```

The API listens on the standard ASP.NET Core dev ports; a seeded SQLite database (`agora.db`) is created on first run.

## Tech notes

- **Money** is stored as `decimal` amount + ISO currency code.
- **SQLite + DateTimeOffset**: SQLite cannot order/compare `DateTimeOffset` natively, so the DbContext converts all `DateTimeOffset` properties to UTC ticks (`long`) via a value converter.

# 02: Words and symbols, in small groups

[Home](README.md) · Previous: [Big picture](01-big-picture.md) · Next: [Folder map](03-find-your-way.md)

**Small outcome:** recognize enough vocabulary to read one controller. This is a lookup page; you do not need to memorize it.

## Group A: a conversation over HTTP

| Term | Say it in everyday language | Here it looks like |
| --- | --- | --- |
| API | A defined way for programs to talk | Agora's HTTP endpoints |
| Endpoint | One supported operation at an address | `GET /api/products` |
| Method | The kind of HTTP operation | GET to read, POST for actions such as creating a cart |
| Route | The address pattern code listens for | `api/carts/{token}` |
| Query parameter | A named option in the URL | `pageSize=2` |
| Request body | Structured input sent with a request | `{ "quantity": 2, "productVariantId": "..." }` |
| JSON | A text format with names and values | `{ "page": 1, "items": [] }` |
| Status code | A concise HTTP outcome | 200 success, 400 invalid input, 404 not found |

Repeat in a sentence: **an endpoint combines an HTTP method and route; input can arrive in the route, query, or body.** Those inputs do not all live in the same place.

## Group B: the code doing the work

| Term | Everyday translation | Find it here |
| --- | --- | --- |
| Class | A definition of a kind of object | `Cart` |
| Object | One instance made from a class | `new Cart()` |
| Property | A named value on an object | `cart.Token` |
| Method | A named operation | `cart.AddItem(...)` |
| Entity | An object identified across changes | A particular cart or order |
| DTO | A shape used to send or receive data | `CartResponse` |
| Service | A collaborator organizing related work | `CheckoutService` |
| Dependency injection | The host supplies a needed collaborator | The controller receives `AgoraDbContext db` |

An entity and a DTO may contain similar fields but serve different jobs. The entity participates in application state and rules; the response DTO states what the caller receives. [Follow the data](08-follow-the-data.md) repeats this distinction with a table.

## Group C: storage

**Database:** durable organized data. **EF Core:** the library this repository uses to connect C# objects and queries to database operations. **DbContext:** the EF object used for a unit of database work. **Migration:** a versioned database-schema change. **Seeder:** code adding example data.

A migration defines structure; a seeder supplies example contents. Think of creating a blank order form versus filling out a sample order. The analogy is about their jobs, not their exact implementation.

## Symbols you will actually see

| C# | Read it as | Important detail |
| --- | --- | --- |
| `Guid?` | A GUID or no value | GUIDs are identifiers; `?` allows absence |
| `bool?` | True, false, or absent | Omitted `inStock` differs from false |
| `var cart = new Cart();` | Create a cart object and name it `cart` | C# still knows its type |
| `await db.SaveChangesAsync(ct);` | Wait for tracked changes to be saved | Calling `AddItem` alone is not this save |
| `x => x.Id` | Given `x`, use its ID | A small function/expression |
| `p.Variants.Any(...)` | At least one variant must satisfy this condition | The same variant must satisfy conditions inside that `Any` |
| `something!` | Tell the compiler to assume it is not null | Does not make a null value safe at runtime |
| `??` | Use the right side if the left is null | Missing inventory can be treated as zero available stock |

**Stop:** point to `pageSize=2` in a URL and say which group it belongs to. Then explain `bool?` using the three possible stock-filter inputs. Those two explanations are enough for now.

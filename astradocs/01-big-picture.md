# 01: The big picture, three ways

[Home](README.md) · Next: [Words and symbols](02-words-and-symbols.md)

**Small outcome:** explain what Agora does in two sentences. No commands required.

## First pass: everyday language

Agora is software for the behind-the-scenes work of an online shop. It knows about products, shopping carts, stock, orders, and more. This repository contains the backend API, its data storage code, and tests. It does not contain a customer storefront website.

A browser, terminal command, or another program can ask the API for information. The API can return a list of products as JSON: structured text made of fields and values. A frontend could use that information to draw a shop page.

Say it another way: **the backend supplies information and behavior; a customer-facing screen would decide how to display them.**

## Second pass: a shop analogy

Imagine a shop with a service counter, shop rules, and record-keeping equipment.

| Shop idea | Codebase counterpart | What it does |
| --- | --- | --- |
| Customer asks the counter | HTTP request to the API | Carries the requested action and input |
| Counter receives the request | Controller | Coordinates handling and returns an HTTP result |
| Shop rules | Domain objects and methods | Guard rules such as allowed cart quantities |
| Record-keeping equipment | Infrastructure and EF Core | Load and save data |
| Stored records | SQLite database | Keep data beyond the lifetime of a request |
| A reply or receipt | Response DTO serialized as JSON | Gives the caller a defined response shape |

The analogy has limits. There are no actual people passing papers between projects. Some controllers directly query the database through EF Core. More involved workflows use service classes. The folder boundaries organize code; they are not separate servers.

## Third pass: the picture

```text
Client                         Agora API                  SQLite
  |                                |                        |
  |---- "Show me products" ------->|                        |
  |                                |---- read products ---->|
  |                                |<--- product data ------|
  |<--- JSON product response -----|                        |
```

Reading products does not sell them. A request to add an item to a cart changes the cart. Checkout is a later request that involves stock and payment.

## Three words to keep

**Request:** what the caller asks. **Behavior:** what the code does with that request. **Response:** what the caller receives.

You will meet these same three words in the browsing story, the live lab, and the tests.

## Stop and say it back

- **Q1:** Does this repository contain the shopper's website, or the backend a website could call?
- **Q2:** Does asking for a product list reduce inventory?
- **Q3:** Which is the persistent store: a controller variable or SQLite?

Use [the answer key](14-answer-key.md) after your attempt. If one answer is uncertain, reread only the matching paragraph. You can stop here for this session.

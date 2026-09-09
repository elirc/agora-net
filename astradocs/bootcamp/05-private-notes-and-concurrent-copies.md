# Module 5: Private notes, list membership, and competing edits

[Bootcamp home](README.md) | [Tracker](story-tracker.md) | [Journal](journal.md)

Stories: MS-14 and MS-15. Implementation and verification are tracked separately. Read the tracker before treating a planned test or migration as verified.

## Start with the user's information

A wishlist item says “I am interested in this variant.” A private note adds “because it is a gift for Sam.” A copy operation says “put these choices on another one of my lists too.” These requests sound small, but they touch different kinds of information.

| Information | Where it belongs | Who may read it | What changes its revision |
| --- | --- | --- | --- |
| Variant price/name | Product variant | Public catalog readers | The catalog's editing rules |
| Private note | Wishlist item | That wishlist's owner | An accepted note edit, including clear |
| Which variants are on a list | Wishlist and its items | That wishlist's owner | Adding, removing, clearing, moving, copying new choices, or product deletion |
| Previously observed stock shortage | Wishlist item | That wishlist's owner | A stock observation; it does not advance the note or membership revision |

Read [Wishlist and WishlistItem](../../src/Agora.Domain/Entities/Wishlist.cs), [the API contracts](../../src/Agora.Api/Contracts/WishlistContracts.cs), and [WishlistsController](../../src/Agora.Api/Controllers/WishlistsController.cs). Locate `NoteVersion` and `MembershipVersion`. They protect different edits. A note should not make a previously read set of list members stale; adding a member should not make a note edit stale.

## Trace a private note through four boundaries

Suppose you own wishlist W containing item I at note version zero. Send PUT `/api/me/wishlists/W/items/I/note` with `{"note":"  gift for Sam  ","expectedVersion":0}` after substituting real IDs.

1. Authentication provides the customer ID. The caller does not choose it in the JSON body.
2. The database query requires the item ID, wishlist ID, and wishlist owner to match. Any missing or foreign combination returns 404.
3. The action compares the expected version with the loaded item. A mismatch returns 409. The domain method trims the note, converts blank/null to null, checks the 500-character limit after trimming, and advances the note version.
4. SaveChanges sends a conditional database update. EF checks the original version again. This catches a competing edit that committed after the action's first comparison.

The response includes the normalized note and its new version. A missing expected version is invalid. Clearing a note is an edit, so it also requires and advances the version. HTML-looking text remains ordinary text; no server-side rendering or interpretation occurs. Consumers that render text must use their normal escaping rules.

In plain language, the revision says: “apply this edit only if the note is still the one I read.” In a code trace, it is a request comparison followed by an EF concurrency predicate. In a test, it is two contexts reading zero, the first saving one, and the second failing to save its stale zero.

## Why comparing versions in C# is insufficient

Consider this sequence:

| Moment | Client A | Client B |
| --- | --- | --- |
| 1 | Reads version 0 | |
| 2 | | Reads version 0 |
| 3 | Checks expected 0 against loaded 0 | |
| 4 | | Saves a new note at version 1 |
| 5 | Tries to save its old view | |

At moment 3, A's comparison was true. That truth expired before moment 5. The database therefore checks the original version again when it saves:

```mermaid
sequenceDiagram
    participant A as Editor A
    participant B as Editor B
    participant DB as SQLite
    A->>DB: Read note at version 0
    B->>DB: Read note at version 0
    B->>DB: Save only if version is 0
    DB-->>B: Saved at version 1
    A->>DB: Save only if version is 0
    DB-->>A: No matching row; conflict
```

Now express the last arrow as an update condition:

```sql
UPDATE WishlistItems
SET Note = @newNote, NoteVersion = @nextVersion
WHERE Id = @itemId AND NoteVersion = @originalVersion;
```

This is explanatory SQL, not a command to execute manually. EF generates the actual statement. If no row matches the old revision, EF raises `DbUpdateConcurrencyException`. The existing exception filter maps it to 409. A useful client response is to reload and show the current note before asking the user to edit again; blindly resending the old value could erase the intent of the newer edit.

## Reads that write need special attention

Existing wishlist detail reads can record that an item was observed out of stock. Adding a note token changes the concurrency behavior of those writes too.

Two orderings matter. If the stock observation saves first, a later note edit updates only the note fields; it should preserve the already-saved observation. If a note edit saves first, a stale observation update carries the old note token and fails with the documented 409. This implementation chooses that explicit conflict instead of silently retrying a potentially stale observation.

The tests cover both orderings using separate SQLite connections. This is deliberate: the usual API fixture shares one in-memory connection, which is excellent for ordinary HTTP tests but does not represent independent database connections. These tests arrange the competing reads and saves sequentially to make the stale-write condition deterministic. They prove stale-write detection and rollback, not a load benchmark or every possible scheduling interleaving.

Some existing mutation routes save their change and then build a response through the observation helper. A later observation conflict does not undo a previously committed operation. On a 409, reload the list before deciding what remains to do. The journal records this distinction because a status code alone does not explain transaction boundaries.

## Copying as a set operation

Work this example on paper:

- Source variants: A, B, C.
- Target variants: B, D.
- Selected source items: the entries for A, then B.

The result adds A, skips B, and leaves every source row in place. The target now contains B, D, A conceptually; its read ordering follows the existing item timestamps. The response's added/skipped arrays follow the caller's selection order. A second request with the current target revision successfully skips both. Reusing the old revision returns 409.

The endpoint validates both owned lists before exposing relationship details. It validates the entire selected item set before adding any row. A list of item IDs is not a list of variant IDs: the source-item lookup proves those choices actually belonged to the selected owned source.

New target rows receive new identities and timestamps. Notes are not copied: the target list might have a different recipient or purpose. Stock observations are fresh. A source item that was once out of stock may now be available; its copied row does not inherit an old back-in-stock history. Unavailable variants remain valid wishlist choices because a wishlist is not a stock reservation.

## One save protects several rows

`Wishlist.AddItem` advances the parent membership version. Copying several new choices can advance it several times before one save; treat the returned version as an opaque concurrency token, not as a count of HTTP calls. Existing membership paths participate too: remove, clear, move-to-cart, and product deletion's cascade. An empty clear and a copy containing only existing variants do not advance membership.

SaveChanges wraps the new child rows and the parent's conditional update in one transaction. If the parent update loses a race, inserted children must roll back too. The unique `(WishlistId, ProductVariantId)` index provides a second boundary against duplicate children. A scoped unique-constraint conflict in the copy action becomes 409.

Even an all-skipped copy performs a conditional parent check. Otherwise another client could alter membership after the initial comparison and the server could report success using an obsolete revision. The conditional check writes the same revision value and does not increment it.

Open [WishlistConcurrencyTests](../../tests/Agora.Tests/Integration/WishlistConcurrencyTests.cs). Find the stale-parent test and predict the final rows before reading the assertions. It verifies that a losing insert disappears and a stale parent deletion fails. The repeated-variant test checks that only one child persists and the losing revision does not advance.

## A migration changes existing databases

Adding C# properties changes the EF model. It does not add columns to an existing database by itself. The migration must add nullable Note plus zero-initialized note and membership revisions while preserving item identities, membership, and observation flags.

Inspect [the generated migration](../../src/Agora.Infrastructure/Migrations/20260908190324_WishlistNotesAndMembership.cs). Its three additions and their defaults are small enough to read completely. Compare them with the token configuration in [AgoraDbContext](../../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs): schema shape and EF's concurrency behavior are related, but a numeric column alone does not make EF use it in an update condition.

[WishlistMigrationTests](../../tests/Agora.Tests/Integration/WishlistMigrationTests.cs) constructs a database through actual migrations, prepares rows, moves to the preceding physical schema, confirms the new columns are absent, then upgrades it. This creates a reproducible old-schema fixture. It verifies preserved rows and zero/null defaults. This is different evidence from EnsureCreated, which creates only the current schema from scratch.

The migration workflow should be reviewed in this order: generated Up/Down operations, model snapshot changes, upgrade result, then normal API regressions. A migration file existing on disk is not proof that an upgrade works. Do not run a downgrade against valuable user data just because the test uses a disposable database.

## Practice, then compare your explanation

Run the focused suite after the migration has been generated:

```powershell
dotnet test Agora.slnx --filter "FullyQualifiedName~WishlistEditingApiTests|FullyQualifiedName~WishlistConcurrencyTests|FullyQualifiedName~WishlistMigrationTests|FullyQualifiedName~WishlistsApiTests"
```

Read [WishlistEditingApiTests](../../tests/Agora.Tests/Integration/WishlistEditingApiTests.cs) and answer these before reading the explanations:

1. You know another customer's item GUID and submit the correct note revision. Why must the edit still fail?
2. Does copying a row mean retaining its database ID?
3. Source A has a private gift note. Should copying A to a new list also copy the note?
4. Two requests pass the C# version check. Can both still be safely accepted?
5. Why does a product-delete route need to know about wishlist membership revisions?
6. A copy inserts a child but loses the parent version check. What must a fresh context observe?

<details>
<summary>Answers and explanations</summary>

1. A revision detects stale data; ownership authorizes access. They solve separate problems. The owner predicate fails, producing 404.
2. No. The copied target item is a new row with its own ID and timestamp, referencing the same variant.
3. No. This feature explicitly keeps notes tied to the source item's context.
4. Only if the database still accepts their conditional updates under the contract. For two competing membership changes from the same revision, one must fail; the early C# comparison cannot decide the race.
5. The database cascade removes variant-backed wishlist items. Without a parent revision change, a previously read membership token would incorrectly appear current.
6. The previously committed target contents, without the losing request's new child. The save transaction must roll back all its changes.

</details>

Repeat the lesson in three sentences: a private field belongs to an owned item; a revision protects an edit from stale state; a transaction keeps a multi-row operation whole. Then point to the predicate, token mapping, and rollback assertion that make those sentences true.

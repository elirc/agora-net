# Catalog synchronization API reference

All routes require an admin bearer token and return `Cache-Control: private, no-store`. Version 1 mirrors product fields, variants, and images. It excludes inventory, reviews, taxonomy labels, tags, collections, and quantity-price policies.

## Bootstrap

`GET /api/admin/catalog-sync/bootstrap`

The response contains `watermark` and `products`. Replace the consumer's entire product dictionary with this response. Do not merge bootstrap into an old dictionary: deleted products from the old dictionary would survive.

The read is one database snapshot, so the product array and watermark describe the same committed point. It includes active and inactive products. Old products have revision zero.

Limits are 1,000 products, 256 KiB for each complete product snapshot, and 5 MiB for the complete JSON response wrapper. The server returns 422 rather than truncating. A larger catalog needs a future export workflow.

## Poll changes

`GET /api/admin/catalog-sync/changes?after=40&limit=100`

`after` is the last sequence the consumer durably applied. It must be zero or positive and no greater than the current high watermark. `limit` is 1–100.

The response includes:

- `after`: requested cursor;
- `lastDeliveredSequence`: checkpoint after applying this page;
- `highWatermark`: greatest committed event when the page was read;
- `retentionFloor`: greatest purged sequence;
- `changes`: increasing sequence prefix.

An Upsert contains a complete version-1 product snapshot. Replace `mirror[productId]`; do not patch selected fields. A Delete contains product ID/revision and a null product; remove that key even when it is already absent.

The full page is at most 1 MiB. The server stops before the next row would cross the budget and never skips that row to return later changes. Keep polling after `lastDeliveredSequence` until it equals `highWatermark`.

Re-reading an earlier page is safe. Complete Upserts and Delete tombstones are idempotent when applied by sequence. Save the local mirror changes and new checkpoint atomically when the consumer database supports it. If processing fails halfway, retain the earlier checkpoint and repeat the page.

`after` below `retentionFloor` returns 410. Discard the local mirror and bootstrap again. A future cursor returns 400 because it cannot describe committed server history.

Example loop:

```text
bootstrap -> replace mirror -> checkpoint = watermark
repeat:
  page = changes(after = checkpoint)
  apply every row in sequence order
  checkpoint = page.lastDeliveredSequence
until checkpoint == page.highWatermark
```

## Purge retained changes

`POST /api/admin/catalog-sync/purge`

Each call examines an oldest contiguous prefix, deletes at most 1,000 events older than 30 days, and advances the retention floor atomically. It returns `purgedCount`, `retentionFloor`, and `highWatermark`.

A recent event is a barrier. If sequences 80/81 are old, 82 is recent, and 83 is old, the call deletes 80/81 only. It does not jump across 82. Deleting old rows never permits sequence reuse because the SQLite key uses AUTOINCREMENT.

## Consumer checklist

1. Treat sequence as an opaque increasing checkpoint; gaps are valid.
2. Deduplicate by applying complete state at the received sequence.
3. Persist a checkpoint only after its rows are applied.
4. Replace the whole mirror after bootstrap or 410.
5. Do not infer inventory from this feed.
6. Alert on 422 bootstrap size failures; do not silently accept a partial mirror.

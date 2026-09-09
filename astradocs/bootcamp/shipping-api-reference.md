# Shipping policy and delivery-calendar API reference

This focused reference covers SS05 and SS06. All examples use JSON and UTC. Authentication follows the API's existing bearer-token rules.

## Public shipping eligibility preview

`POST /api/shipping-methods/eligibility`

```json
{
  "country": "us",
  "weightGrams": 2000
}
```

The country must be exactly two ASCII letters and is normalized to uppercase. Weight must be nonnegative. The response contains only active, eligible methods in deterministic code/ID order:

```json
[
  {
    "id": "00000000-0000-0000-0000-000000000001",
    "code": "light",
    "name": "Light",
    "minDays": 1,
    "maxDays": 3
  }
]
```

This endpoint is informational. Checkout does not accept its answer as authorization. Checkout recalculates weight from the current active cart and evaluates the current policy again.

## Admin shipping eligibility policy

`GET /api/admin/shipping-methods/{id}/eligibility`

Admin authentication is required. A method with no policy returns an empty country list, null maximum, and null revision. That means unrestricted eligibility.

`PUT /api/admin/shipping-methods/{id}/eligibility`

```json
{
  "countries": ["US", "CA"],
  "maximumWeightGrams": 2000,
  "expectedRevision": null
}
```

Limits and meanings:

- At most 50 unique countries after trim and uppercase normalization.
- Every country is exactly two ASCII letters.
- Empty countries means any syntactically valid country.
- Maximum weight is null or 0..1,000,000 grams.
- The maximum is inclusive: 2,000 succeeds and 2,001 fails for a 2,000-gram cap.
- Null revision creates only when no policy exists. Replacements require the exact revision; stale input returns 409.
- Unknown method IDs return 404. Invalid policy input returns 400 for DTO shape errors or 422 for domain-rule errors.

Admin GET/PUT responses use `private, no-store`.

## Checkout behavior

Quote and checkout resolve an explicit method when supplied; otherwise they resolve the active default. An ineligible explicit or default method returns 422 with stable reasons such as `CountryNotServed` or `WeightExceeded`. The server never silently changes the method.

The active cart weight is calculated with checked wide integers. Negative legacy variant weights are rejected. Eligibility failure happens before inventory reservation, discount use, gift-card redemption, payment, order creation, and webhooks.

## Admin delivery calendar

`GET /api/admin/delivery-calendar`

```json
{
  "enabled": false,
  "cutoffUtc": "14:00",
  "closureDates": [],
  "revision": 0
}
```

The singleton is seeded disabled at revision 0 for upgraded databases.

`PUT /api/admin/delivery-calendar`

```json
{
  "enabled": true,
  "cutoffUtc": "14:00",
  "closureDates": ["2026-09-14", "2026-12-25"],
  "expectedRevision": 0
}
```

Limits and meanings:

- Cutoff is `HH:mm` in UTC with minute precision; seconds and offsets are rejected.
- There may be at most 366 unique ISO dates.
- Business dates are Monday-Friday excluding closures.
- Dispatch is today only when today is a business date and the captured time is strictly before cutoff.
- Otherwise dispatch is the next business date.
- Shipping method day 0 means dispatch date. Day 1 means the next business date.
- Calendar searches stop after 730 calendar days and return a clear domain error instead of looping indefinitely.
- Replacement requires the exact revision; stale input returns 409.

Admin responses use `private, no-store`.

## Date interpretation in quote and order responses

Checkout quotes include `estimatedDeliveryFrom` and `estimatedDeliveryTo`. A quote recalculates these fields using the current calendar and current time.

When the calendar is enabled, estimates are business dates serialized at `00:00:00Z`. Midnight is a canonical representation of a date, not a promised arrival time.

When disabled, the application preserves its earlier elapsed-day behavior: `calculatedAt + minDays/maxDays`, including time of day.

Checkout stores the exact calculated pair on the order. Later calendar changes affect new quotes and checkouts only; old order responses retain the dates promised when they were created.

## Worked cutoff example

With Friday 11 September 2026, cutoff 14:00 UTC, and Monday 14 September closed:

| Captured time | Dispatch/day 0 | Day 1 |
|---|---|---|
| Friday 13:59 UTC | Friday 11th | Tuesday 15th |
| Friday 14:00 UTC | Tuesday 15th | Wednesday 16th |

The second row demonstrates the strict cutoff boundary.

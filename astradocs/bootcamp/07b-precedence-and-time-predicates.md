# Workshop 7b: saved defaults and scheduled discounts

Stories: MS-19 and MS-28. [Shared pricing](07a-quotes-and-shared-pricing.md) | [Tracker](story-tracker.md) | [Journal](journal.md)

Both features let someone configure a decision in advance. Neither configuration removes the need to validate when the decision is used. A saved shipping method can become inactive; a discount's start instant can arrive while its usage limit is already exhausted.

## Defaults are inputs with lower priority

Saved preferences contain an optional address-book ID, an optional shipping-method code, and an edit revision. They belong to an account. Checkout and quote only use them when `useSavedPreferences` is true, which requires authentication. Existing clients omit the flag and retain existing behavior.

Resolve address and shipping separately. A shopper can override the address while keeping the preferred method, or override the method while keeping the preferred address.

| Explicit input | Saved value | Result when opted in |
| --- | --- | --- |
| Valid address | Any address | Explicit address |
| Missing address | Owned existing address | Saved address snapshot |
| Missing address | Cleared or absent | Existing required-address failure |
| Foreign explicit address ID | Valid saved address | 404; no fallback |
| Invalid inline address fields | Valid saved address | Model validation failure; no fallback |
| Valid method | Any method | Explicit method |
| Missing method | Active saved method | Saved method |
| Missing method | No saved method | Active default method |
| Missing method | Saved missing/inactive method | 422 |
| Invalid explicit method | Active saved method | 422; no fallback |
| Explicit blank method | Active saved method | 422 when opted in |

The old checkout gives a supplied saved-address ID priority over an inline address when both are supplied. This extraction preserves that behavior. The preference address is only considered when neither explicit form is supplied.

Repeat the rule without technical terms: use what the shopper deliberately chose; if they chose nothing, try their saved preference; if there is no preference, use the old default or ask for the missing information. An invalid deliberate choice is a problem to fix, not permission to guess something else.

## References expire in meaning even if their IDs remain

Read [CheckoutPreferencesController](../../src/Agora.Api/Controllers/CheckoutPreferencesController.cs). Saving an address requires current ownership; saving a method requires current activity. Then read the shared resolution in [CheckoutPricingService](../../src/Agora.Infrastructure/Services/CheckoutPricingService.cs). Using the preference rechecks those conditions.

The nullable address FK uses SET NULL on deletion. Removing an address clears the preference's reference, so normal address fallback rules apply. Shipping code is retained when its method disappears: an opted-in use that needs it returns 422, making the stale choice visible. The user can select another method explicitly or clear/update their preference.

A foreign key proves that a referenced address exists. It does not prove that its owner matches the preference's owner. The use-time query includes both address ID and customer ID. The test deliberately constructs a cross-owner reference that satisfies the FK and demonstrates why the ownership predicate is still essential.

## Revisions distinguish missing rows from existing empty preferences

GET `/api/me/checkout-preferences` returns null address, null method, and null version when no row exists. PUT requires the JSON property expectedVersion:

```json
{"shippingAddressId":null,"shippingMethodCode":"standard","expectedVersion":null}
```

Null means “create only if no preference row exists.” A successful creation starts at version zero. To replace it, send zero; replacement writes version one. Null fields clear the selections. Omitting expectedVersion is a bad request, while repeating null after creation is 409.

An existing row with both selections null is still a real row with a revision. Do not confuse “no preferences selected” with “no preference record exists.” The count of rows is unnecessary because CustomerId is the primary key.

The PUT transaction protects load/validate/save locally. [CheckoutPreferencePersistenceTests](../../tests/Agora.Tests/Integration/CheckoutPreferencePersistenceTests.cs) coordinates two create-only writers on separate connections, then verifies a stale tracked replacement cannot overwrite the winner. Its upgrade test also checks address SET NULL and account cascade deletion.

## Scheduling is a time predicate, not a timer

Read [DiscountCode.IsRedeemable](../../src/Agora.Domain/Entities/DiscountCode.cs). In words, redeemability requires:

1. The discount is active.
2. Start is absent or the supplied instant is at/after start.
3. Expiry is absent or the supplied instant is before expiry.
4. Usage limit is absent or usage remains below it.

All four must hold. Adding a start does not bypass the other three.

For a start at 12:00 and expiry at 13:00 UTC, the valid time interval is `[12:00, 13:00)`. The square bracket includes start; the parenthesis excludes expiry.

| Evaluation instant | Time rule |
| --- | --- |
| 11:59:59.9999999 | Too early |
| 12:00:00 | Allowed |
| 12:59:59.9999999 | Allowed |
| 13:00:00 | Expired |

No worker needs to flip IsActive at noon. Calling the same predicate with a later instant changes its result. IsActive remains a separate administrator control. This avoids relying on a background job to run at exactly the right moment.

## Offsets, one clock, and replacement semantics

`2030-01-01T12:00:00Z` and `2030-01-01T05:00:00-07:00` denote the same instant. Discount input timestamps require an explicit offset or Z and normalize to UTC. A timestamp with no zone is rejected because its meaning would otherwise depend on the server's local timezone. See [OffsetTimestampJsonConverter](../../src/Agora.Api/Contracts/OffsetTimestampJsonConverter.cs).

The API validates the final start/expiry pair: when both exist, start must strictly precede expiry. Both create and update accept nullable StartsAt. Existing update contracts use replacement semantics, so omitted/null StartsAt clears the schedule. A client wishing to retain it must send it.

The calculator captures TimeProvider once per operation. Domain methods accept that instant rather than reading a real clock internally. Tests can move from one tick before start to exact start without sleeping. Quote and checkout each capture their own instant; they do not share a frozen eligibility promise across requests.

## Walk the tests as worked examples

[CheckoutPreferencesApiTests](../../tests/Agora.Tests/Integration/CheckoutPreferencesApiTests.cs) uses a saved German address with no matching seeded tax zone and an explicit US address with eight-percent tax. The different tax amounts expose which address was chosen. Distinct shipping charges expose which method was chosen. This verifies behavior more clearly than inspecting a private helper.

[DiscountSchedulesApiTests](../../tests/Agora.Tests/Integration/DiscountSchedulesApiTests.cs) tests one tick early, exact start, exact expiry, offset equivalence, invalid pairs, and clearing through an omitted property. It checks early failures leave stock, order count, usage, cart revision, and gateway count untouched. [DiscountScheduleTests](../../tests/Agora.Tests/Unit/DiscountScheduleTests.cs) covers the pure conjunction with disabled and exhausted codes.

The migration adds a nullable start column, so old discounts gain null and keep their previous time behavior. Preferences are a new empty table; existing customers are not automatically opted in.

## Practice, then teach it twice

**Exercise:** a customer supplies a valid inline address, omits shipping, and has an inactive preferred method. Predict the result. **Answer:** 422 for the method. An explicit address only overrides the address dimension.

**Exercise:** their preferred address was deleted, but they supply a valid inline address and valid method. **Answer:** checkout can proceed through those explicit selections; the cleared preference is not needed.

**Exercise:** a code starts now but has already reached its usage limit. **Answer:** it is not redeemable. Start eligibility is one part of an AND expression.

**Exercise:** a PUT omits StartsAt while retaining a future ExpiresAt. **Answer:** it clears the start. This follows the documented replacement contract, not patch semantics.

First explain these rules to a shopper using no class names. Then explain them to a teammate by pointing to the DTO default, ownership query, database relationship, supplied clock, and boundary assertions. Record any place where your first explanation was incomplete and rewrite it using a counterexample.

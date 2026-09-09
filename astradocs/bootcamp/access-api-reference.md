# Access API reference — sessions and guest orders

This reference accompanies lessons 09a and 09b. It records the exact wire contract after the authentication cutover.

## Login and registration

`POST /api/auth/register` accepts the existing email, password, and full-name fields plus optional `deviceLabel` (trimmed, maximum 80 characters).

`POST /api/auth/login` accepts email, password, and the same optional `deviceLabel`.

Both success responses contain:

```json
{
  "token": "signed-jwt",
  "expiresAt": "2026-09-08T13:00:00Z",
  "sessionId": "6cc62070-7560-4377-84c9-0a73fe07500d",
  "customer": { "id": "...", "email": "...", "fullName": "...", "role": "Customer", "createdAt": "..." }
}
```

The JWT contains `sub`, `email`, `role`, and `sid`. A token without `sid` is rejected after deployment. Clients must log in again at cutover; old tokens cannot be backfilled honestly.

## Session routes

All routes require a currently valid bearer token.

| Method and route | Result |
| --- | --- |
| `GET /api/me/sessions?page=1&pageSize=20` | active sessions owned by caller; page size 1..100 |
| `DELETE /api/me/sessions/{id}` | 204; repeated owned revocation is a no-op; foreign ID is 404 |
| `POST /api/me/sessions/revoke-all` | revokes every currently saved unrevoked session, including current |

List item:

```json
{
  "id": "6cc62070-7560-4377-84c9-0a73fe07500d",
  "deviceLabel": "Laptop",
  "issuedAt": "2026-09-08T12:00:00Z",
  "expiresAt": "2026-09-08T13:00:00Z",
  "revokedAt": null,
  "isCurrent": true
}
```

Private responses use `Cache-Control: private, no-store`. Revocation affects later authorization checks. It does not cancel controller work already authorized and running.

## Guest checkout receipt

Guest checkout adds these nullable top-level fields to the existing checkout JSON shape:

```json
{
  "number": "ORD-20260908-1234ABCD",
  "status": "Paid",
  "guestOrderAccessToken": "32hexchars.43base64urlchars",
  "guestOrderAccessExpiresAt": "2026-10-08T12:00:00Z"
}
```

An account-owned checkout returns both guest fields as null. Quote never issues a credential. The plaintext token appears only in successful guest checkout and admin rotation responses. Checkout marks its response `private, no-store`.

Although the flattened checkout shape retains the historical `giftCardCode` and `paymentTransactionId` field names for client parsing compatibility, their values are now null. Provider and gift-card bearer references remain server-side.

## Presenting guest access

Send the credential only as a request header:

```http
X-Agora-Order-Access: 32hexchars.43base64urlchars
```

Do not put it in a route, query string, cookie, or request body. The credential is bound to exactly one guest order and expires 30 days after issue.

## Order and return access matrix

| Route/action | Owner | Admin | Guest token | Anonymous/email only |
| --- | ---: | ---: | ---: | ---: |
| `GET /api/orders/{number}` | yes | yes | yes, bound guest order | 404 |
| `GET /api/orders/{number}/fulfillments` | yes | yes | yes, bound guest order | 404 |
| `POST /api/orders/{number}/cancel` | yes | yes | no | 401/404 |
| `POST /api/orders/{number}/refund` | no | yes | no | 401/403 |
| `POST /api/orders/{number}/returns` | yes | yes | yes | 404 |
| `GET /api/returns/{number}` | yes | yes | yes via return's order | 404 |
| `POST /api/returns/{number}/cancel` | yes | yes | yes | 404 |
| approve/reject return | no | yes | no | 401/403 |

The optional legacy `email` fields on return create/cancel requests are ignored for authorization. An account order always requires its authenticated owner or Admin, even when a caller knows the matching email or holds a valid credential for another order.

Ordinary customer order projections omit email, gift-card code, payment transaction ID, internal notes, and unrelated account data. Ordinary customer return projections omit refund transaction ID.

## Admin rotation

`POST /api/admin/orders/{number}/guest-access/rotate` requires Admin. It works for both current and legacy guest orders, rejects account-owned orders, atomically revokes the old active credential, and returns:

```json
{
  "credentialId": "68255bf8-27c8-4169-a2f1-a798354e202a",
  "guestOrderAccessToken": "replacement-shown-once",
  "expiresAt": "2026-10-08T12:00:00Z"
}
```

The previous token fails immediately after commit. Normal reads cannot redisclose the replacement because only its SHA-256 digest is stored.

## Status behavior

| Status | Meaning |
| --- | --- |
| 400 | malformed paging, validation, or token-independent input |
| 401 | missing/invalid bearer authentication on an authenticated-only action |
| 403 | authenticated role lacks an admin permission |
| 404 | resource absent or caller lacks owner/capability access |
| 409 | state conflict such as invalid rotation target or lifecycle transition where mapped |
| 422 | well-formed business request cannot be used |

Wrong, malformed, expired, revoked, and foreign guest credentials use the same private-resource failure behavior. Clients must not use status differences to probe resource existence.

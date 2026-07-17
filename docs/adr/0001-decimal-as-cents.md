# 1. Store decimals as integer cents, rates as millionths

Status: Accepted

## Context

The store runs on SQLite, which has no `decimal` affinity and no
`DateTimeOffset` type. EF Core's default mapping stores both as TEXT. TEXT
money is a trap that pays out late:

- `ORDER BY Total` sorts lexicographically — `"9.99"` sorts after `"100.00"`.
- `WHERE PaidAt >= @from` compares strings, so range filters silently mis-scope
  reports.
- Arithmetic pushed into SQL rounds through a REAL, which is binary floating
  point — exactly what money must never touch.

We wanted this closed once, globally, rather than remembered per property.

## Decision

Convert at the persistence boundary, via conventions in
`AgoraDbContext.ConfigureConventions`:

- every `DateTimeOffset` → `long` UTC ticks
  (`DateTimeOffsetToUtcTicksConverter`);
- every `decimal` → `long` integer cents (`DecimalToCentsConverter`,
  `× 100`, away-from-zero).

Both are ordered-comparable integers, so ordering and range filters translate
to real integer comparisons.

Then the exception that makes the rule work: **tax rates are not amounts**. A
rate of `0.095` through a cents converter rounds to `0.10` — a 9.5% rate
silently becomes 10%, and every order in that zone is wrong by half a percent.
`TaxZone.DefaultRate` and `TaxZoneRate.Rate` therefore override the convention
with `DecimalRateToMillionthsConverter` (`× 1 000 000`, 6 dp).

## Consequences

- Money arithmetic is exact and money columns sort correctly; reports can
  range-filter `PaidAt` in SQL.
- One global rule plus one explicit, documented opt-out for the handful of
  columns that are *rates*. The opt-out is applied per property in
  `OnModelCreating`, next to a comment saying why.
- Any new fractional non-money `decimal` (a commission rate, a weight factor)
  **must** opt out too, or it inherits cent precision and silently rounds. This
  is the sharp edge of a global convention and the reason `PersistenceTests`
  pins converter round-trips.
- Raw SQL against the database sees integers, not decimals: `Total` reads
  `4485`, not `44.85`.
- Ticks are UTC-normalized, so the original offset of a `DateTimeOffset` is not
  preserved — everything comes back as `+00:00`. The API deals only in UTC, so
  this is a non-issue in practice.

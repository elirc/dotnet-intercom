# Module 03: Architecture and Patterns

Where the senior questions live. The first four pages map the architecture by concern; the last three sharpen judgment.

| Page | Concern |
| --- | --- |
| [01-boundaries-and-layers.md](01-boundaries-and-layers.md) | What each layer owns and must not; where boundaries leak |
| [02-data-model-and-persistence.md](02-data-model-and-persistence.md) | Entities, relationships, indexes, transactions, safe schema change |
| [03-validation-auth-and-permissions.md](03-validation-auth-and-permissions.md) | Authn/authz layers, policies, tenant/resource isolation, IDOR |
| [04-side-effects-async-and-reliability.md](04-side-effects-async-and-reliability.md) | Background work, external calls, idempotency, retries, failure visibility |
| [05-pattern-catalog.md](05-pattern-catalog.md) | 14 patterns this repo uses, as cards |
| [06-architecture-critique.md](06-architecture-critique.md) | Strongest choices, risks, prioritized improvements, "if I owned this" |
| [07-comparative-implementations.md](07-comparative-implementations.md) | 5 decision points, both sides, with a migration drill |

Suggested order: 01 → 02 → 03 → 04 to build the map, then 05 pattern catalog, then 06 critique and 07 comparisons together (they cross-reference). The critique and comparisons are where you practice thinking like the person who owns the consequences.

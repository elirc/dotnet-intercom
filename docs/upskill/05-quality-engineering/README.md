# Module 05: Quality Engineering

How this codebase stays correct — and where it doesn't yet.

| Page | Use it to |
| --- | --- |
| [01-testing-strategy.md](01-testing-strategy.md) | Know what belongs in unit vs integration vs E2E, and what not to test |
| [02-writing-tests-here.md](02-writing-tests-here.md) | 8 concrete recipes using the repo's existing helpers + exact commands |
| [03-systematic-debugging.md](03-systematic-debugging.md) | A repeatable debugging loop + 5 repo-specific scenarios |
| [04-performance-thinking.md](04-performance-thinking.md) | Measure-first discipline + the real hotspots here |
| [05-security-checklist.md](05-security-checklist.md) | A pre-merge checklist tied to real code and the risk register |
| [06-observability-and-operations.md](06-observability-and-operations.md) | "How would I know this broke?" per flow |

The test suites are green and verified (27/27 unit); the gaps are in *what isn't tested* (routing round-robin, the breach job, presence, refresh rotation). This module teaches you to see both.

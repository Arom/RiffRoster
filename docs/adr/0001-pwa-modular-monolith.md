# ADR 0001: PWA first, with a modular monolith

Date: 2026-09-07. Status: accepted implementation direction from FIRST_PUBLIC_VERSION_PLAN.md; architecture migration has not yet occurred.

## Context and decision

The private beta is one .NET 10 application with a JSON store, plain browser JavaScript, browser-only Tesseract and a service worker. Invitations must remain easy to open from group chats. Native distribution adds work without resolving the immediate identity, data integrity and offline gaps.

Keep one deployable ASP.NET Core application, serve an installable PWA and introduce PostgreSQL before scaling beyond one replica. Migrate in independently deployable slices to Domain, Application, Infrastructure and API projects. Dependencies point inward; the domain cannot reference HTTP, filesystem, EF, email, push or payment providers. Use React/TypeScript for the client in Phase 3. Do not add microservices or a mediator framework for ceremony.

Performance identifiers survive editing and migration. Store UTC instants with an event IANA timezone. Keep poster bytes in the browser; APIs receive reviewed structured rows only. PostgreSQL transactions will cover publication, selection replacement, account deletion and billing idempotency. Reminders become durable database jobs.

## Consequences and release gates

Phase 0 introduces tests and recovery verification without changing runtime policy. Phase 1 requires dry-run JSON mapping, counts and reference checks, immutable source backup and staging rollback before production cutover. No native binaries, new prices or privacy promises are introduced by this ADR. The full first-public-version checklist remains binding; a passing baseline is not public-launch approval.

The current app's private authentication, name-based foreign keys, browser-local time conversion, plain push-secret storage and JSON recovery behaviour remain known migration targets. Characterization tests record these, not endorse them as final design.

## Alternatives and review

Native-first was rejected by the product plan because link sharing and a single codebase are more valuable for the initial pilot. A whole-system rewrite would obscure regressions and compromise rollback. Continuing to scale JSON would keep file-lock and transaction limitations.

Revisit native work only at the plan's usage/season gate and measured installation, reminder or retention failures. Revisit deployment topology only with operational evidence.

## Reference checks

- ASP.NET Core's official integration-test guidance supports WebApplicationFactory with a public test entry point: https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0
- Lighthouse's PWA audits are deprecated, so record available categories and validate installation separately: https://developer.chrome.com/docs/lighthouse/pwa/installable-manifest

Payment/platform pricing is not encoded or changed in Phase 0; recheck those facts in the applicable implementation phase.

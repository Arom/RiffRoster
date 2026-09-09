# Phase 0 baseline evidence

Captured 7 September 2026 against the existing seeded schedule and a synthetic attendee. No production account or data was used. Reproduce with `pnpm baseline` after building the app. A new isolated Development server and browser profile are created for each run.

| Measurement | Result |
|---|---:|
| Mobile performance | 100 |
| Accessibility | 96 |
| Best practices | 96 |
| SEO | 90 |

Version, browser, timestamp and warnings are in `scores.json`. Full evidence is in `lighthouse-mobile.json` and `lighthouse-mobile.html`. This is one mobile-simulated run on localhost with an authenticated browser profile and a previously opened shell. It is not a cold first-visit/production-load claim and does not certify WCAG compliance. Lighthouse PWA audits are deprecated; installation and device offline/push checks remain separate (https://developer.chrome.com/docs/lighthouse/pwa/installable-manifest).

Screenshots were visually inspected:

- `schedule-390x844.png`: phone layout retains horizontal stage lanes, but day labels are crowded and header controls push most performance content below the initial viewport.
- `schedule-768x1024.png`: tablet layout shows the picked act and both populated stage lanes; large header area remains.
- `schedule-1440x900.png`: desktop layout has ample lane space and a visible selected act. A transient pick-success toast is visible in the tablet/desktop baseline.

Lighthouse reports contrast and visible-label/accessibility-name mismatches, small fonts, missing meta description, unminified/unused assets, missing text compression and cache-lifetime opportunities. These are carried into later UI/deployment phases; no baseline screenshot was adjusted to hide them.

The five Chromium journeys cover current login/personal mode, retained picks, event creation/switching, stable edit/delete, group creation/join/comparison, OCR review and contextual reminder setup. OCR and push browser services are mocked. Parser and service-worker helpers are tested from the unchanged source file; server authorisation and persistence have separate xUnit coverage.

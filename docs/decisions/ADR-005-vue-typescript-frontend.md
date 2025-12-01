# ADR-005: Vue 3 + TypeScript for Frontend

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Choose the frontend framework and language for the management dashboard and landing pages of the link management platform.

## Context

The platform needs a browser-based UI for:
- Workspace and domain management (onboarding, DNS status, settings)
- Link management (create, edit, search, bulk import/export)
- Analytics dashboard (click charts, geo/device breakdowns, exports)
- Billing and account management
- Admin and abuse review tools

The frontend is a single-page application (SPA) backed by REST APIs. It does not need SSR/SSG (the marketing site can be static). The UI should be responsive, accessible, and maintainable by one person.

Candidate frameworks evaluated:

| Framework | Strengths | Weaknesses |
|---|---|---|
| **Vue 3** | Gentle learning curve, Composition API, excellent TypeScript support, Vuetify component library, smaller bundle than React | Smaller ecosystem than React, fewer third-party component libraries |
| **React** | Largest ecosystem, most tutorials/examples, widest hiring pool | Heavier bundle, more boilerplate for common patterns, hooks foot-guns, ecosystem churn |
| **Svelte** | Smallest bundle, least boilerplate, compile-time reactivity | Smallest ecosystem, fewer component libraries, less AI training data (harder for Claude Code) |

## Approach

Use **Vue 3 with Composition API and TypeScript**. For UI components, use **Vuetify 3** (Material Design). For server state, use **TanStack Query** (formerly Vue Query) for caching, background refetching, and optimistic updates. Use **Pinia** only for client-only state (UI preferences, current selections, auth state).

API client code and types are auto-generated from the backend OpenAPI spec, ensuring the frontend stays in sync with the API without manual type maintenance.

## Constraints

- Vue 3 requires ES2015+ browsers. This is acceptable — the target audience (developers, creators, agencies) uses modern browsers.
- Auto-generated API types depend on the backend OpenAPI spec being kept up to date. This is enforced by generating the spec from the Minimal API endpoint definitions (using `Microsoft.AspNetCore.OpenApi`).
- Vuetify 3 enforces Material Design patterns. Custom design requirements that conflict with Material Design would require component overrides or a different component library.

## Decisions

1. **Vue 3 over React** — Vue's Composition API is cleaner than React hooks for a solo developer. Fewer foot-guns (no stale closures, no dependency array mistakes). Smaller bundle by default. `.vue` single-file components keep template, logic, and styles co-located.
2. **TypeScript over JavaScript** — Type safety catches API contract mismatches and refactoring errors at compile time. Auto-generated types from OpenAPI make TypeScript low-overhead.
3. **TanStack Query over manual fetch** — Eliminates a whole class of bugs (stale data, missed refetches, loading state inconsistencies) that are common with hand-rolled `fetch` + `useEffect` patterns. Declarative cache keys, automatic background refetching, and optimistic mutations reduce the amount of state management code.
4. **Pinia over Vuex** — Pinia is the officially recommended state management for Vue 3. Vuex is in maintenance mode. Pinia's modular store design aligns with service-scoped frontend code.
5. **Vuetify over custom CSS** — Provides a consistent, professional-looking UI out of the box without design effort. Material Design is familiar and well-documented.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Ecosystem breadth** | React has more third-party libraries and examples. In practice, the core needs (routing, state, data fetching, UI components, charts) are all well-served in Vue. Gaps are unlikely for a management dashboard. |
| **Vuetify lock-in** | Using Vuetify means the UI looks like Material Design. If a custom brand identity requires different visual patterns, migrating off Vuetify would be significant work. This is acceptable for an MVP — the component library is a productivity trade, not a permanent commitment. |
| **AI training data** | React has more representation in AI training corpora than Vue 3 Composition API. Claude Code handles Vue well (the `.claude/guidelines/frontend.md` file provides explicit patterns), but some edge cases may require more explicit prompting. |

## Evolution

- If SSR or static generation becomes needed (e.g., for a public marketing site or link preview pages), add **Nuxt 3** (which builds on Vue 3).
- If Vuetify becomes a constraint for custom branding, evaluate **Tailwind CSS + Headless UI** or **PrimeVue** as alternatives. The TanStack Query + Pinia layer would remain unchanged.
- If the frontend grows to the point where a separate SPA is needed for the admin/abuse tools, extract it into `app/AdminPortal/` following the service-scoped structure.

## Related ADRs

- [ADR-004: C# for Backend Services](ADR-004-csharp-backend.md) — TypeScript on the frontend, C# on the backend, OpenAPI contract between them.
- [ADR-003: Serverless-First Architecture](ADR-003-serverless-first-architecture.md) — The frontend is a static SPA served from S3 + CloudFront, aligning with serverless principles.

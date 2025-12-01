# Control Plane — Frontend

Vue 3 + TypeScript single-page application for the Short.io Control Plane.

## Stack

| Technology | Version | Purpose |
|---|---|---|
| Vue 3 | 3.5+ | UI framework (Composition API, `<script setup lang="ts">`) |
| TypeScript | 5.7+ | Strict type checking |
| Vuetify 3 | 3.7+ | Material Design component library |
| Pinia | 2.x | Client state (UI preferences, selections) |
| TanStack Query | 5.x | Server state (queries, mutations, caching) |
| Vue Router | 4.x | SPA navigation |
| openapi-fetch | 0.x | Typed HTTP client generated from OpenAPI spec |
| Vite | 5.x | Build tool and dev server |
| Vitest | 2.x | Test runner |

## Setup

```bash
# Install dependencies
npm install

# Copy environment file and adjust for your setup
cp .env.example .env

# Start dev server (http://localhost:5173)
npm run dev
```

## Environment Variables

Vite supports per-mode environment files. Copy the relevant `.example` to activate it:

| File | Mode | Purpose |
|---|---|---|
| `.env.example` | all | Baseline template — copy to `.env` |
| `.env.development.example` | `npm run dev` | Local dev overrides — copy to `.env.development` |
| `.env.production.example` | `npm run build` | Production values — copy to `.env.production` |

| Variable | Description | Default |
|---|---|---|
| `VITE_API_URL` | Backend API base URL (proxied at `/api`) | `http://localhost:5000` |

The Vite dev server proxies all `/api/*` requests to `VITE_API_URL`. In production, configure your reverse proxy to forward `/api/*` to the backend.

## Available Scripts

| Command | Description |
|---|---|
| `npm run dev` | Start dev server with HMR at `http://localhost:5173` |
| `npm run build` | Type-check and produce an optimized production bundle in `dist/` |
| `npm run preview` | Serve the production bundle locally |
| `npm run test` | Run Vitest once (exits 0 with no test files) |
| `npm run test:watch` | Run Vitest in watch mode |
| `npm run type-check` | Run `vue-tsc --noEmit` without building |
| `npm run lint` | Run ESLint over `src/` |
| `npm run format` | Auto-format `src/` with Prettier |
| `npm run generate:api` | Regenerate TypeScript types from the OpenAPI spec |

## API Types

Types are auto-generated from the backend's OpenAPI spec. Regenerate after backend changes:

```bash
# 1. Build the backend to emit the OpenAPI spec
dotnet build ../../src/ControlPlane/ControlPlane.Api.sln

# 2. Regenerate frontend types
npm run generate:api
```

The generated `src/services/generated/api.d.ts` is gitignored — do not edit it manually.

## Architecture

```
Component → Composable → Service → apiClient (openapi-fetch)
                ↓
        TanStack Query
        (caching, refetch, invalidation)
```

- **Components** (`src/components/`) — Reusable Vuetify-first UI components
- **Composables** (`src/composables/`) — `use*()` functions wrapping TanStack Query
- **Views** (`src/views/`) — Page-level components mounted by Vue Router
- **Stores** (`src/stores/`) — Pinia stores for client-only state (UI prefs, form drafts)
- **Services** (`src/services/`) — Typed API client wrappers around `openapi-fetch`
- **Types** (`src/types/`) — Shared TypeScript interfaces and type utilities

## Path Aliases

`@` maps to `src/`. Use it everywhere:

```typescript
import { useLinks } from '@/composables/useLinks'
import type { Link } from '@/types'
```

# Applications

Frontend applications. Vue 3 + TypeScript with Composition API.

## Directory Structure

```
app/
└── {ServiceName}/                  # PascalCase, one subdirectory per app
    ├── src/
    │   ├── components/             # Reusable Vue components
    │   ├── composables/            # Composition API logic
    │   ├── views/                  # Page components
    │   ├── stores/                 # Pinia stores (client state only)
    │   ├── router/                 # Vue Router configuration
    │   ├── services/               # API clients (auto-generated types)
    │   └── types/                  # TypeScript type definitions
    ├── tests/
    └── vite.config.ts
```

## Conventions

- **Composition API** — `<script setup lang="ts">`. Never Options API.
- **TanStack Query for server state** — queries, mutations, caching
- **Pinia for client state only** — UI preferences, form drafts
- **Auto-generated API types** — `npm run generate:api` from OpenAPI spec
- **Vuetify components first** — prefer over custom components
- **Explicit types** — no `any`

## Placement Rule

No source files directly in `app/`. Every file must live within a service-scoped subdirectory.

## References

- [Frontend guidelines](../.claude/guidelines/frontend.md)

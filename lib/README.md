# Shared Libraries

Code shared across 2+ services. Libraries here must be truly generic — no service-specific logic.

## Directory Structure

```
lib/
└── {LibraryName}/                  # PascalCase dot-separated, e.g., Common.Logging/
    ├── src/
    └── tests/
```

## When to Create a Shared Library

- Code is used by **2+ services** (actual, not hypothetical)
- Logic is **truly generic** (not service-specific)
- Abstraction is **proven** (not premature)

## When NOT to Create

- Only one service uses it — keep it in `src/{ServiceName}/`
- Logic contains service-specific assumptions
- Not proven to be reusable yet

**Locality beats DRY until proven otherwise.**

## Dependency Direction

```
Services (src/, app/)
    ↓ can depend on
Shared Libraries (lib/)
    ↓ can depend on
External Packages (NuGet, npm)
```

Shared libraries must not depend on `src/` or `app/` code.

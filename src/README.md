# Source

Backend services, APIs, and business logic. C# / .NET 8 with the Minimal API pattern.

## Directory Structure

```
src/
└── {ServiceName}/                  # PascalCase, one subdirectory per service
    ├── {ServiceName}.Api/          # Minimal API endpoints, Program.cs
    ├── {ServiceName}.Domain/       # Domain models, value objects, interfaces
    └── {ServiceName}.Infrastructure/  # Data access, external services
```

## Conventions

- **One endpoint per file** with static nested classes (`Registration`, `Handler`, `Request`, `Response`)
- **Records for DTOs** — `required` + `init` properties, immutability by default
- **Result&lt;T&gt; pattern** for expected failures, exceptions for programming errors only
- **Interface-based DI** — composition over inheritance, constructor injection
- **File-scoped namespaces** and **primary constructors** (C# 12)

## Placement Rule

No source files directly in `src/`. Every file must live within a service-scoped subdirectory.

## References

- [C# basics](../.claude/csharp/basics.md)
- [Minimal API pattern](../.claude/csharp/minimal-api.md)
- [Domain modeling](../.claude/csharp/domain-modeling.md)
- [Error handling](../.claude/csharp/error-modeling.md)

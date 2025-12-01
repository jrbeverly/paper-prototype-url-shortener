# Tests

Test suites mirroring the source directory structure.

## Directory Structure

```
test/
└── {ServiceName}/                  # PascalCase, mirrors source
    ├── {ServiceName}.Tests/        # Unit + integration tests
    └── {ServiceName}.E2E/          # End-to-end tests (when applicable)
```

## Test Pyramid

- **60% Unit** — Fast, deterministic, no external dependencies
- **30% Integration** — Data access, API contracts
- **10% E2E** — Critical user flows only

## C# Testing Stack

- **xUnit** — Test framework
- **FluentAssertions** — Readable assertions
- **Moq** — Mocking
- **Testcontainers** — Integration tests

## Frontend Testing Stack

- **Vitest** — Test runner
- **@vue/test-utils** — Component testing
- **Playwright** — E2E

## Principles

- Business logic → 100% coverage
- API endpoints → Critical paths
- Data access → Happy path + errors
- Prefer `[Theory]` with data-driven tests over repetitive `[Fact]` methods

## References

- [C# testing](../.claude/csharp/testing.md)
- [Frontend testing](../.claude/guidelines/frontend.md#testing)

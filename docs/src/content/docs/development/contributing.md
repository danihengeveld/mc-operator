---
title: Contributing
description: How to contribute to mc-operator — code, docs, issues, and pull requests.
sidebar:
  order: 2
---

## Ways to contribute

- **Bug reports**: Open an issue on [GitHub](https://github.com/danihengeveld/mc-operator/issues) with as much detail as possible (operator version, Kubernetes version, steps to reproduce, expected vs actual behaviour).
- **Feature requests**: Open an issue describing the use case and motivation. Feature requests are more likely to be accepted when they include a clear description of the problem being solved.
- **Pull requests**: For bug fixes and small improvements, open a PR directly. For larger changes, open an issue first to discuss direction.
- **Documentation**: Improvements to the docs are always welcome — fix typos, clarify confusing sections, or add examples.

## Pull request process

1. Fork the repository and create a branch from `main`.
2. Make your changes with clear, focused commits.
3. Ensure tests pass: `dotnet run --project src/McOperator.Tests/`
4. Ensure the Helm chart lints: `helm lint charts/mc-operator --strict`
5. Update documentation if your change affects user-facing behaviour.
6. Open a pull request against `main`. Describe what changed and why.

## Commit style

Use short, imperative commit messages:

```
fix: reject empty server version in validation webhook
feat: add levelType validation to admission webhook
docs: clarify storage immutability in CRD reference
chore: update KubeOps to 10.3.3
```

Prefixes: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `ci`.

## Code standards

- All new public APIs should have XML doc comments
- New behaviour should come with unit tests
- Avoid unnecessary abstractions — the codebase intentionally stays simple
- Follow existing patterns (builder pattern for resource construction, static methods on builder classes)

## Testing philosophy

Tests are unit tests that validate the output of builder methods and webhook logic. They do not spin up Kubernetes or Docker. This keeps the test suite fast and portable.

When adding a test:
- Mirror the pattern of existing test classes (TUnit `[Test]` / `[Arguments]`, async `Assert.That`)
- Test one thing per test method
- Use a builder helper (`BuildServer(...)`) to create test fixtures

## Security issues

For security vulnerabilities, do **not** open a public issue. Email [dani@hengeveld.dev](mailto:dani@hengeveld.dev) directly.

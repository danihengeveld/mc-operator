# Contributing to mc-operator

Thank you for your interest in contributing. This document explains how the project works, how to set up your development environment, and the conventions we follow.

## Code of conduct

Be respectful and constructive. This project follows standard open-source conduct norms — harassment of any kind is not tolerated.

## Ways to contribute

- **Bug reports** — Open an [issue](https://github.com/danihengeveld/mc-operator/issues) with the operator version, Kubernetes version, steps to reproduce, and expected vs. actual behaviour.
- **Feature requests** — Open an issue describing the use case and motivation. Larger features should be discussed before implementation.
- **Pull requests** — Small bug fixes and documentation improvements are welcome directly. For non-trivial changes, open an issue first.
- **Documentation** — Improvements to `docs/` are always welcome.

## Development setup

### Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0+ |
| Docker | Recent |
| kubectl | Cluster-matching |
| Helm | 3.14+ |
| Bun | 1.3+ (docs only) |
| A Kubernetes cluster | 1.25+ |

### Clone and restore

```bash
git clone https://github.com/danihengeveld/mc-operator.git
cd mc-operator

# Restore .NET tools (KubeOps CLI)
dotnet tool restore

# Restore NuGet packages (uses locked mode)
dotnet restore McOperator.slnx
```

### Run tests

```bash
dotnet run --project src/McOperator.Tests/
```

### Build the Docker image

```bash
docker build -t ghcr.io/danihengeveld/mc-operator:dev .
```

### Docs site

```bash
cd docs
bun install
bun run dev     # local dev server at http://localhost:4321
bun run build   # production build
```

### Lint the Helm chart

```bash
helm lint charts/mc-operator --strict
```

## Project layout

```
mc-operator/
├── .github/
│   ├── dependabot.yml
│   └── workflows/          # CI, release-image, release-chart
├── charts/mc-operator/     # Helm chart
├── docs/                   # Astro Starlight site (Bun)
├── examples/               # Example MinecraftServer manifests
├── manifests/              # CRDs, RBAC, Kustomize operator manifests
└── src/
    ├── McOperator/         # Operator application (.NET 10)
    └── McOperator.Tests/   # TUnit unit tests
```

## Coding conventions

- **C#**: nullable enabled, implicit usings, warnings-as-errors. See `.editorconfig`.
- **Tests**: TUnit, async `Assert.That(...)`. Mirror existing test patterns.
- **NuGet versions**: All centralized in `Directory.Packages.props`. Do not set `Version=` on individual `PackageReference` items.
- **Commits**: Imperative, lower-case prefix: `fix:`, `feat:`, `docs:`, `chore:`, `refactor:`, `test:`, `ci:`.

## Pull request process

1. Fork and create a branch from `main`.
2. Make focused, well-described commits.
3. Ensure tests pass: `dotnet run --project src/McOperator.Tests/`
4. Ensure the Helm chart lints: `helm lint charts/mc-operator --strict`
5. Update `docs/` and `CHANGELOG.md` if behaviour changes.
6. Open the PR against `main`. Describe _what_ changed and _why_.

## Changelog

All user-visible changes must be recorded in `CHANGELOG.md` under `## [Unreleased]`. See the existing entries for the format. Include entries under the relevant section (`### Added`, `### Changed`, `### Fixed`, `### Removed`).

## Security issues

Do **not** open a public issue for security vulnerabilities. Email [dani@hengeveld.dev](mailto:dani@hengeveld.dev) directly.

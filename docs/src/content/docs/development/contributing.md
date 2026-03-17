---
title: Contributing
description: How to contribute to mc-operator — code, docs, issues, and pull requests.
sidebar:
  order: 2
---

import { Aside } from '@astrojs/starlight/components';

Full contribution guidelines are in [CONTRIBUTING.md](https://github.com/danihengeveld/mc-operator/blob/main/CONTRIBUTING.md) in the repository root. This page summarises the key points.

## Ways to contribute

- **Bug reports**: Open an [issue](https://github.com/danihengeveld/mc-operator/issues) with operator version, Kubernetes version, steps to reproduce, and expected vs. actual behaviour.
- **Feature requests**: Open an issue describing the use case. Larger features should be discussed before implementation.
- **Pull requests**: Small bug fixes and documentation improvements are welcome directly. For non-trivial changes, open an issue first.
- **Documentation**: Improvements to the docs site are always welcome.

## Development setup

```bash
git clone https://github.com/danihengeveld/mc-operator.git
cd mc-operator
dotnet tool restore
dotnet restore McOperator.slnx
```

Run tests:

```bash
dotnet run --project src/McOperator.Tests/
```

Docs site (uses [Bun](https://bun.sh)):

```bash
cd docs
bun install
bun run dev
```

Helm chart:

```bash
helm lint charts/mc-operator --strict
```

## Pull request process

1. Fork and create a branch from `main`.
2. Make focused commits with descriptive messages.
3. Tests must pass: `dotnet run --project src/McOperator.Tests/`
4. Chart must lint: `helm lint charts/mc-operator --strict`
5. Update `CHANGELOG.md` and docs if behaviour changes.
6. Open a PR against `main`.

## Commit style

```
fix: reject empty server version in validation webhook
feat: add custom JVM argument validation
docs: clarify storage immutability in CRD reference
chore: update KubeOps to 10.3.4
ci: add docs build step to CI workflow
```

Prefixes: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `ci`.

## Security issues

Do **not** open a public issue for security vulnerabilities. Email [dani@hengeveld.dev](mailto:dani@hengeveld.dev) directly.


# Contributing to Mem0Sharp

Thank you for helping improve Mem0Sharp. Contributions can include bug fixes, tests, documentation, provider implementations, and design discussions.

## Before you start

- Check the [open issues](https://github.com/jihadkhawaja/mem0sharp/issues) and existing pull requests before starting work.
- For a new feature, public API change, or architectural change, open an issue first so the scope and design can be discussed.
- For a small documentation correction or a focused bug fix, you can usually start with a pull request.
- Do not include credentials, connection strings containing secrets, generated build output, or changes to licensing and attribution files unless the change has been discussed first.

For security vulnerabilities, follow the private reporting process in the repository's [security policy](https://github.com/jihadkhawaja/mem0sharp/security/policy) rather than opening a public issue.

## Development prerequisites

- .NET 10 SDK
- .NET 8 and .NET 9 runtimes to execute the complete target-framework test matrix
- Git

The default in-memory service and unit tests do not require an external vector database or model provider.

## Set up the repository

```powershell
git clone https://github.com/jihadkhawaja/mem0sharp.git
cd mem0sharp
dotnet restore .\Mem0Sharp.slnx
```

Create a branch for each change:

```powershell
git switch -c fix/short-description
```

Use a descriptive branch name such as `fix/expiration-filter` or `docs/getting-started`.

## Build and test

Run the same checks used by the package publishing workflow:

```powershell
dotnet build .\Mem0Sharp.slnx
dotnet test .\tests\Mem0Sharp.Tests\Mem0Sharp.Tests.csproj
dotnet test .\tests\Mem0Sharp.NetStandard.Tests\Mem0Sharp.NetStandard.Tests.csproj
```

For a release-style local check, use the `Release` configuration:

```powershell
dotnet build .\Mem0Sharp.slnx --configuration Release
dotnet test .\tests\Mem0Sharp.Tests\Mem0Sharp.Tests.csproj --configuration Release
dotnet test .\tests\Mem0Sharp.NetStandard.Tests\Mem0Sharp.NetStandard.Tests.csproj --configuration Release
```

When adding or changing behavior, add or update an xUnit test in `tests/Mem0Sharp.Tests`. Prefer tests that exercise the public service or contract involved in the change. Keep tests deterministic and avoid requiring network access or external services unless the scenario specifically covers an integration boundary.

The tag-triggered publishing workflow packs `Mem0Sharp`
from the release tag and publishes it with the tag version.

## Code and architecture guidelines

- Keep public types in the `Mem0Sharp` namespace; folder names describe architecture rather than namespace segments.
- Put provider-neutral models in `src/Mem0Sharp/Domain` and provider-neutral interfaces in `src/Mem0Sharp/Contracts`.
- Keep use-case orchestration in `src/Mem0Sharp/Application`.
- Put HTTP and vendor-specific code under the `src/Mem0Sharp/Infrastructure` folders.
- Preserve dependency direction toward contracts and domain models. Do not make contracts depend on application services or concrete adapters.
- Preserve existing public APIs and behavior unless a breaking change has been discussed and documented.
- Keep changes focused. Avoid unrelated refactoring, formatting churn, or new dependencies when an existing .NET API or abstraction is sufficient.

Read [Architecture](docs/architecture.md) for the source layout and extension rules, and [API reference](docs/api-reference.md) before changing public contracts.

## Documentation changes

Update the relevant documentation when behavior, public APIs, configuration, or supported providers change. The main documentation entry points are:

- [README](README.md) for the project overview and first-run path.
- [Getting started](docs/getting-started.md) for setup and core usage.
- [Providers and persistence](docs/providers-and-persistence.md) for model and database configuration.
- [API reference](docs/api-reference.md) for public types and extension points.
- [Python feature parity](docs/mem0-python-parity.md) when a capability changes relative to the reference Mem0 project.

Keep examples copyable, use placeholders for secrets, and verify relative links before submitting the pull request.

## Pull requests

1. Keep the pull request focused on one problem or closely related change.
2. Explain the problem, the approach, and any compatibility or migration impact.
3. Link the relevant issue when one exists.
4. Include tests for bug fixes and new behavior, or explain why a test is not practical.
5. Include documentation updates for user-visible changes.
6. Confirm that the build and tests pass locally.
7. Call out any changes that require PostgreSQL, pgvector, an external model provider, or other environment-specific setup.

Maintainers may ask for changes to the design, tests, documentation, or scope before merging. Please update the branch in response to review feedback and keep discussions tied to the pull request's goal.

## Licensing and attribution

Mem0Sharp is licensed under the Apache License 2.0. Contributions must be compatible with that license. Preserve the existing attribution and trademark language in [NOTICE](NOTICE), [LICENSE](LICENSE), and [README](README.md). Do not copy code or documentation from another project without confirming its license and attribution requirements.

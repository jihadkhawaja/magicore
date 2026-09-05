# Security Policy

MagiCore takes the security of the library and its users seriously. Thank you for
reporting vulnerabilities responsibly.

## Reporting a vulnerability

Please do not report security vulnerabilities through public GitHub issues, pull
requests, or discussions.

Report vulnerabilities privately through [GitHub Private Vulnerability
Reporting](https://github.com/jihadkhawaja/magicore/security/advisories/new).

Please include as much of the following information as possible:

- The affected package, component, or integration.
- The affected version, tag, or commit.
- Clear steps to reproduce the issue.
- The security impact and a proof of concept, if available.
- Any suggested mitigation or fix.
- Whether the issue depends on an external service such as PostgreSQL, pgvector,
  or an OpenAI-compatible model provider.

The maintainers will use the private advisory to investigate the report and
coordinate any fix or disclosure with the reporter.

## Scope

This policy covers security vulnerabilities in the MagiCore source code,
NuGet package, built-in storage and provider integrations, and repository
configuration.

MagiCore does not require a hosted memory service at runtime. Vulnerabilities
in external services or deployments should also be
reported to their respective maintainers, while including any MagiCore-specific
impact in the private report.

## Protect credentials and memory data

Do not include real API keys, passwords, connection strings, personal data, or
production memory records in a report. Replace sensitive values with clearly
identified placeholders and provide a minimal reproduction where possible.

## Supported versions

Please reproduce reports against the latest published version when possible and
include the exact version or commit used. Security fixes are evaluated against
the currently maintained codebase; upgrade guidance will be included with any
released fix when applicable.

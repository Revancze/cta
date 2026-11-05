# CTA

[![CI](https://github.com/Revancze/cta/actions/workflows/ci.yml/badge.svg)](https://github.com/Revancze/cta/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**CTA** (working name) keeps a long-term history of xUnit and NUnit test results and answers
the questions the last green build cannot:

- When did this test start failing, and which commit broke it?
- Which tests are flaky?
- What slows the test suite down?
- How does quality evolve over commits?

A lean, offline alternative to Azure DevOps Test Analytics.

> **Status:** early development, phase F0 (foundation). Nothing to install yet.

## Principles

- **Offline, no server.** The network is only needed to download results from GitHub Actions, on demand.
- **One database on your disk.** A single SQLite file, kept outside git.
- **No clutter.** XML reports are temporary and deleted after a successful import.
- **On demand only.** Nothing runs in the background.
- **One binary.** Native AOT, no runtime to install.
- **Idempotent.** Importing the same run twice changes nothing.
- **Answers "why".** For every failure: the first failing commit, what changed, and the error.

## Planned usage

```bash
cta init              # choose where the database lives
cta run               # run dotnet test and import the results
cta pull              # download new results from GitHub Actions
cta show report 14    # summary of the last 14 days
cta show why <test>   # why and since when a test fails
```

## Development

Requirements: .NET SDK 10.0.402 or newer. Python 3.14 is needed only for the requirements documentation.

```bash
dotnet tool restore
dotnet build
dotnet test
```

Requirements and the traceability matrix ([StrictDoc](https://github.com/strictdoc-project/strictdoc)):

```bash
py -3.14 -m venv .venv
.venv/Scripts/python -m pip install -r tools/requirements-docs.txt
.venv/Scripts/strictdoc export . --formats html --output-dir output
```

## Documentation

- [Roadmap](docs/roadmap.md) (Czech)
- [Architecture decision records](docs/adr/README.md) (Czech)
- [Requirements](docs/requirements/)

## License

[MIT](LICENSE)

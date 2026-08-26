# Contributing to Icod.Host

Thank you for contributing to `Icod.Host`. The library provides neutral,
cross-platform host identity and processor-resource observations. Changes should
preserve factual semantics, explicit platform limitations, and provenance.

## Supported toolchain

- Target framework: `net10.0`.
- Language version: C# 13.
- Nullable reference types and implicit global usings remain enabled.
- Supported CI runners are `windows-latest`, `ubuntu-latest`, and
  `macos-latest`.
- Repository text files use UTF-8 with LF line endings.
- Public, protected, and internal types and members should have substantive XML
  documentation; use `<inheritdoc/>` where appropriate.

Do not change the target framework, language version, configuration policy, or
repository line-ending convention as part of an unrelated contribution.

## Architecture

`Icod.Host` owns neutral factual observations such as:

- normalized host identity;
- processor counts;
- process affinity or processor-set selection;
- hard CPU quotas;
- processor and NUMA topology;
- observation availability and provenance; and
- narrow platform adapters needed to obtain those facts.

Do not add GNU `hostid`/`nproc` command policy, process control, ProcPs-specific
metrics or `/proc` models, command-line parsing, or command-hosting
infrastructure.

`ObservationFidelity` is also outside this package. Semantic-fidelity policy
belongs to consumers that map factual host observations into higher-level
cross-platform models.

The package should remain free of dependencies on `Icod.CommandFramework`,
`Icod.CoreUtils`, and `Icod.ProcPs`.

## C# style

Follow `.editorconfig` and the surrounding source. In particular:

- use tabs for C# indentation;
- use 1TBS braces and always brace conditional and loop bodies;
- use PascalCase for types and members and camelCase for locals and parameters;
- validate public, protected, and internal method parameters at entry;
- keep nullable flow explicit rather than suppressing warnings casually;
- propagate `CancellationToken` through asynchronous work; and
- avoid unrelated formatting churn.

Unsupported platform behavior must remain explicit. Do not fabricate Unix
capabilities or substitute unrelated measurements merely to return a value.

## Tests

Add or update tests for changed behavior. Important cases include:

- deterministic host-ID normalization;
- availability/provenance behavior;
- Linux CPU-list parsing;
- cgroup v1 and v2 quota parsing;
- affinity descriptor validation;
- processor quota validation;
- provider injection and cancellation; and
- controlled system-provider behavior on Windows, Linux, and macOS.

Tests must not write to standard output or standard error unless explicitly
communicating with another process. Keep any temporary resources uniquely named
and delete only resources owned by the test.

## Build and validation

From the repository root:

```text
dotnet clean Icod.Host.sln -c Debug
dotnet restore Icod.Host.sln
dotnet build Icod.Host.sln -c Debug --no-restore
dotnet test Icod.Host.sln -c Debug --no-build
```

Before merge or release, also validate Release:

```text
dotnet clean Icod.Host.sln -c Release
dotnet restore Icod.Host.sln
dotnet build Icod.Host.sln -c Release --no-restore
dotnet test Icod.Host.sln -c Release --no-build
```

`build.cmd` and `build.sh` may be used for the standard local sequence. Pull
requests run Staging across all three CI operating systems; pushes to `main`
run Release and publish only after the complete Release matrix succeeds.

## Pull requests and commits

Keep changes focused. A pull request should identify:

- the factual host-resource contract being changed;
- important platform-specific behavior;
- added or changed tests;
- build/test commands and platforms used; and
- intentionally unsupported or deferred behavior.

Use concise imperative commit subjects. Discuss cross-package ownership changes
before introducing a new shared abstraction.

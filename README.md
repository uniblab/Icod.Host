# Icod.Host

[![PR Staging build](https://github.com/uniblab/Icod.Host/actions/workflows/pull-request.yaml/badge.svg)](https://github.com/uniblab/Icod.Host/actions/workflows/pull-request.yaml)
[![Main Release validation](https://github.com/uniblab/Icod.Host/actions/workflows/main.yaml/badge.svg?branch=main)](https://github.com/uniblab/Icod.Host/actions/workflows/main.yaml)

`Icod.Host` is a cross-platform .NET library for factual host identity and
processor-resource observation. It provides neutral system facts without tying
callers to a command framework or to a command suite such as CoreUtils or
ProcPs.

The library is the standalone successor to the factual provider layer that was
originally incubated under `Icod.CommandFramework.Host`.

## Features

- normalized 32-bit host identifiers with source descriptions;
- explicit resource availability and provenance;
- configured, installed/present, online, and process-available processor counts;
- current-process affinity or processor-set observations;
- container, cgroup, and Windows job-object hard CPU quota observations;
- processor package, physical-core, logical-processor, and NUMA topology;
- capability reports derived from the individual observations;
- deterministic Linux CPU-list and cgroup-quota parsers; and
- injectable host and processor-resource provider contracts.

`Icod.Host` deliberately reports `Unavailable`, `Unsupported`, and
`NotApplicable` distinctly. Consumers should not replace an unsupported
topology, affinity, or quota observation with a plausible-looking zero.

## Requirements

The initial `1.0.0` release targets .NET 10.0 and uses C# 13.

The package has no runtime package dependencies.

## Installation

```text
Install-Package Icod.Host -Version 1.0.0
```

or:

```text
dotnet add package Icod.Host --version 1.0.0
```

## Example

```csharp
using Icod.Host;

HostResourceSnapshot snapshot =
	await SystemHostResourceProvider.Instance.ObserveAsync();

if ( snapshot.HostIdentifier.IsAvailable ) {
	Console.WriteLine(
		snapshot.HostIdentifier.GetRequiredValue().Hexadecimal
	);
}

Console.WriteLine(
	snapshot.Processors.ProcessAvailableProcessorCount.GetRequiredValue()
);
```

A larger runnable example is available under `samples/Icod.Host.Sample`.

## Platform profile

| Fact | Windows | Linux | macOS | Other/BSD fallback |
| --- | --- | --- | --- | --- |
| Host identifier | Stable MachineGuid folded to 32 bits | Native `gethostid`, then machine-id/host-name fallback | Native `gethostid`, then host-name fallback | Native `gethostid` where available, then stable-text fallback |
| Configured processors | Maximum processor-group capacity | `sysconf(_SC_NPROCESSORS_CONF)` | `hw.logicalcpu_max` / `hw.ncpu` | Unsupported |
| Installed processors | Active processors across groups | sysfs `present`, then configured count | Configured logical processors | Unsupported |
| Online processors | Active processors across groups | sysfs `online`, then `sysconf` | `hw.logicalcpu` / `hw.ncpu` | Unsupported |
| Process-available processors | `Environment.ProcessorCount` | `Environment.ProcessorCount` | `Environment.ProcessorCount` | `Environment.ProcessorCount` |
| Affinity / processor set | Default CPU sets, then process-group mask | `sched_getaffinity` | Unsupported | Unsupported |
| Hard CPU quota | Job-object hard or maximum rate | cgroup v2 `cpu.max` or cgroup v1 CFS quota | Unsupported | Unsupported |
| Topology / NUMA | Group-aware logical processor information | sysfs package/core/node directories | package/core/logical sysctls; NUMA unsupported | Process-available logical count only |

Windows CPU-set values are labeled as opaque CPU-set identifiers rather than
logical processor indices. A legacy affinity mask that covers only the current
Windows processor group is marked incomplete. Relative Windows job weights are
not misrepresented as hard quotas.

On Linux, the affinity observation reflects the effective scheduler mask,
including cpuset restrictions. Cgroup membership paths are rooted and checked
for containment before controller files are read.

## Host identifier normalization

Native signed host identifiers are normalized to their low unsigned 32 bits.

Stable textual machine identifiers are trimmed and, when hexadecimal, decoded
to bytes before deterministic FNV-1a folding. Other text is normalized to
lowercase invariant UTF-8 before folding. Raw Windows MachineGuid and Linux
machine-id values are not exposed by the public snapshot.

## Design boundary

`Icod.Host` owns factual host identity and processor-resource observations.

It does not own:

- GNU `hostid` or `nproc` command policy;
- process enumeration or process control;
- ProcPs `/proc` models, process metrics, memory maps, slab data, or command
  presentation;
- command-line parsing or diagnostics; or
- wall-clock/date parsing and formatting.

`ObservationFidelity` is intentionally not part of `Icod.Host`. Semantic
fidelity describes how a consumer maps platform-specific observations onto a
higher-level model; it is separate from this package's factual availability and
provenance contracts.

## Migrating from Icod.CommandFramework.Host

Consumers of the factual Host layer can replace:

```csharp
using Icod.CommandFramework.Host;
```

with:

```csharp
using Icod.Host;
```

and reference:

```xml
<PackageReference Include="Icod.Host" Version="1.0.0" />
```

`ObservationFidelity` is not migrated by this package.

## Building

On Windows:

```text
build.cmd
```

On Unix-like hosts:

```text
./build.sh
```

Both scripts support `clean`, `restore`, `build`, `test`, `pack`, and `validate`.
With no argument they run:

```text
clean -> restore -> build -> test -> pack -> validate
```

Local builds always use `Debug`.

The repository lifecycle is:

```text
local build.*       -> Debug
pull request        -> Staging
push to main        -> Release validation
v<semver> tag       -> Release publication
```

Pull requests build/test on Windows, Linux, and macOS; Linux additionally packs
and verifies the exact Staging `.nupkg` / `.snupkg` artifacts. Pushes to `main`
run validation-only Release builds on Windows x64/ARM64, Linux x64/ARM64, and
macOS x64/ARM64. Only a `v*` tag whose commit is contained in `main` and whose
version matches `PackageVersion` may publish to NuGet.org and GitHub Packages.

See [`packaging/README.md`](packaging/README.md) for the current build and
distribution contract.

## Author

Timothy J. Bruce <uniblab@hotmail.com>

Copyright (c) 2026 Timothy J. Bruce.

## License

Licensed under the GNU Lesser General Public License v3.0 or later
(`LGPL-3.0-or-later`). See `LICENSE` for the complete license text.

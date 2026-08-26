# Icod.Host source

The `Icod.Host` source directory contains the neutral factual host-resource
layer extracted from `Icod.CommandFramework.Host`.

## Responsibilities

The production surface covers:

- `HostIdentifier` and deterministic host-identifier normalization;
- `HostResourceValue<T>`, availability, provenance, and capability reporting;
- `IHostIdentifierProvider`, `IProcessorResourceProvider`, and
  `IHostResourceProvider`;
- processor count, affinity, quota, topology, and NUMA models;
- deterministic Linux CPU-list and cgroup quota parsers; and
- `SystemHostResourceProvider`, which selects narrow native/BCL observations for
  Windows, Linux, macOS, and portable fallback hosts.

The system provider reports facts only. Command policy belongs to consumers.

## Availability and provenance

Every optional fact uses `HostResourceValue<T>` so consumers can distinguish an
available value from a temporarily unavailable value, a platform-unsupported
concept, or a concept that does not apply to the current process.

`HostResourceProvenance` identifies where a value came from. Unsupported facts
must remain explicit rather than being replaced by zero or another unrelated
measurement.

## Boundary

`ObservationFidelity` is intentionally excluded. Availability/provenance are
properties of factual observations; semantic fidelity belongs to consumers that
map those observations into suite-specific cross-platform models.

Likewise, process control, ProcPs-specific observations, GNU command policy, and
command-hosting infrastructure do not belong in this library.

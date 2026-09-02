# Icod.Host build and distribution tooling

This directory adapts the canonical Icod C#/.NET build-cycle contract to `Icod.Host`.

## Lifecycle

| Lifecycle | Configuration | Entry point |
| --- | --- | --- |
| local `build.cmd` / `build.sh` | `Debug` | `packaging/Invoke-Build.ps1` |
| pull request | `Staging` | `.github/workflows/pull-request.yaml` |
| push to `main` | `Release` | `.github/workflows/main.yaml` |
| manual diagnostic | selected | `.github/workflows/distribution-validation.yaml` |
| `v*` tag contained in `main` | `Release` | `.github/workflows/release.yaml` |

`main` is validation-only. Package publication occurs only from a valid release tag.

## Package contract

`Icod.Host` is one `net10.0` library package plus its matching symbol package.

`VerifyPackageArtifact.ps1` checks the exact generated package set:

- exactly one `Icod.Host` `.nupkg`;
- one matching `.snupkg`;
- package ID/version metadata;
- `README.md`, `LICENSE`, and `icon.png`;
- `lib/net10.0/Icod.Host.dll`;
- `lib/net10.0/Icod.Host.xml`; and
- `Icod.Host.pdb` in the symbol package.

The package that passes this verification is the package later published.

## CI/CD dependency model

Pull requests build and test Staging on Windows, Linux, and macOS. Linux produces and verifies the canonical Staging package artifact.

`main` builds and tests Release on six OS/architecture runners. Linux x64 reuses its validated build to pack and verify the platform-neutral package; the other runners do not rebuild the package.

Tagged releases rebuild/test the exact tagged commit, pack and verify the exact Release package, then publish NuGet.org and GitHub Packages in parallel. GitHub Release creation remains the final rendezvous and contains the `.nupkg`, `.snupkg`, and checksum manifest.

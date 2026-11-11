# Third-party notices

RayBork is licensed under the [MIT License](LICENSE). Copyright (c) 2026 Norne
<norne19@outlook.com>. The MIT text in `LICENSE` is the license for RayBork itself; it is not a
substitute for the licenses of the dependencies below.

This file is the repository's notice policy and dependency inventory. It is part of the owning
documentation: a dependency change is not complete until this file is updated in the same pull
request.

## Notice policy

- **Runtime dependencies** are pinned centrally in `Directory.Packages.props`, license-reviewed, and
  must pass the Native AOT publish gate for `win-x64` and `linux-x64` before they enter
  `RayBork.Engine`. The milestone that first uses a package's features owns its feature-level AOT
  validation; the status column records that boundary honestly.
- **Test and tooling packages** are JIT-only. They are not part of the runtime publish graph and are
  not covered by the runtime AOT gate, but they are still license-reviewed before adoption.
- **Every dependency row records** the exact pinned version, license, purpose, and runtime, test, or
  tooling classification. Floating versions are not allowed; transitive dependencies are governed
  by the pinned graph of their direct package.
- **A dependency change updates this file in the same pull request** as the version or package
  change. Review covers license, version, purpose, classification, and AOT status.
- **Full license texts ship with the packages.** NuGet packages carry their own license metadata
  and license files, and in-box .NET APIs are covered by the .NET runtime notices and SDK. This file
  summarizes the inventory and points at those texts; it does not replace them.
- **Incompatible licenses are rejected.** A dependency whose license cannot be distributed with an
  MIT-licensed project is not adopted. Do not vendor dependency source without a notice update and
  review.
- **Versions move deliberately.** A version bump is a dependency change and follows the rules above.

M0 status: only SDL3 is exercised end to end by the Native AOT `samples/HelloInterop` host. The
other runtime packages are pinned and remain in the AOT publish graph without AOT/trim warnings,
but their feature-level gates land in M1 (logging), M2 (Vulkan), and M3 (PolyType and
Nerdbank.MessagePack). The architecture dependency matrix in
[docs/architecture.md](docs/architecture.md#dependency-matrix) records the same status.

## Runtime dependencies

These packages are referenced by `RayBork.Engine` or deployed with it.

| Package | Version | License | Purpose | AOT status |
| --- | --- | --- | --- | --- |
| `SDL3-CS` | 3.4.20 | Zlib | Managed SDL3 bindings for windowing, input, events, and future audio | Exercised end to end by the M0 `samples/HelloInterop` host on `win-x64` and `linux-x64`. |
| `SDL3-CS.Native` | 3.4.2 | Zlib | Per-RID native SDL3 binaries deployed alongside the engine | Loaded and run by the same M0 host on both RIDs. |
| SDL3 (native library) | bundled by `SDL3-CS.Native` 3.4.2 | Zlib | The native windowing, input, and audio library itself | Proven by the M0 AOT host; upgraded only with the `SDL3-CS.Native` pin. |
| `Silk.NET.Vulkan` | 2.23.0 | MIT | Vulkan 1.2 bindings for the M2 renderer backend | Pinned and in the AOT publish graph; feature-level AOT validation lands with the M2 backend. |
| `PolyType` | 1.4.1 | MIT | Compiled shapes for custom TOML data and AOT-safe serialization | Pinned and in the AOT publish graph; feature-level AOT validation lands in M3. |
| `Nerdbank.MessagePack` | 1.4.104 | MIT | MessagePack serialization for custom data and pack entries | Pinned and in the AOT publish graph; feature-level AOT validation lands in M3. |
| `Microsoft.Extensions.Logging` | 11.0.0 | MIT | Logging abstraction exposed as `EngineContext.LoggerFactory` | Pinned and in the AOT publish graph; feature-level AOT validation lands in M1. |
| `ZLogger` | 2.6.1 | MIT | Default console logging provider | Pinned and in the AOT publish graph; feature-level AOT validation lands in M1. |

## Platform and in-box APIs

These are part of the target framework rather than NuGet packages.

| Component | Version | License | Purpose | Classification |
| --- | --- | --- | --- | --- |
| .NET 11 runtime and SDK | `net11.0` (SDK `11.0.100`, see `global.json`) | MIT, .NET Foundation | Target framework, runtime, and Native AOT toolchain | Runtime |
| `System.IO.Compression.Zstandard*` | in-box in .NET 11 | MIT for the .NET managed API; the native Zstandard library is BSD-3-Clause (Meta Platforms, Inc.) | Zstandard pack payload compression; there is no separate compression package | Runtime |

`.NET` is copyright (c) .NET Foundation and Contributors and is licensed under the MIT License.
RayBork does not redistribute a standalone Zstandard package: the in-box
`System.IO.Compression.Zstandard*` APIs and the native library they use ship with the .NET runtime,
and their notices are included with that runtime. The Zstandard format and reference
implementation are by Meta Platforms, Inc. and affiliates and are available under the BSD-3-Clause
License; the .NET runtime's third-party notices carry the full zstd text.

## Test and tooling dependencies

These packages are JIT-only and are not part of the runtime publish graph.

| Package | Version | License | Classification | Purpose | AOT status |
| --- | --- | --- | --- | --- | --- |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | 5.6.0 | MIT | Tooling | Public API tracking for `RayBork.Engine` only | Compile-time analyzer; no runtime or publish-graph impact. |
| `xunit.v3` | 3.2.2 | Apache-2.0 | Test | Test framework for `RayBork.Tests` | JIT only; not in the runtime publish graph. |
| `xunit.runner.visualstudio` | 3.1.5 | Apache-2.0 | Test | VSTest adapter for xUnit v3 | JIT only; not in the runtime publish graph. |
| `Microsoft.NET.Test.Sdk` | 18.11.1 | MIT | Test | Test host and MSBuild integration | JIT only; not in the runtime publish graph. |
| `Moq` | 4.21.0 | BSD-3-Clause | Test | Mocking for unit tests | JIT only; not in the runtime publish graph. |

## Maintaining this file

- Add or remove a row when a package is added or removed; group the rows by classification and keep
  the runtime, platform, and test/tooling tables intact.
- Keep versions identical to `Directory.Packages.props`; a review that finds a drift blocks merge.
- Keep the license value to the SPDX identifier used by the package metadata.
- Update the M0 status paragraph when a milestone turns a deferred AOT status into an exercised
  gate.
- If a package's license text is not included in its distribution, add the license text here or
  reject the package.

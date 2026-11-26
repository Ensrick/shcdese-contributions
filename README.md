# Script Extender contribution checks

Selected production sources and isolated checks for proposed
[Stronghold Crusader DE Script Extender](https://gitlab.com/rawra-stronghold-crusader/shcde-script-extender)
changes. The library license is retained in [LICENSE.txt](LICENSE.txt), with the
incorporated GPL text in [COPYING.GPLv3.txt](COPYING.GPLv3.txt).

`INPUTS.json` binds the production and fixture files, the public RedBird source
revision and the exact released Core dependency. This repository contains no game
assembly, installed assets or complete upstream repository history.

## Cost ownership checks

The internal cost owner coordinates temporary physical recruitment prices with
supported persistent setters. It retains explicit restore intent, including
same-value writes, and preserves uncertain failures instead of restoring stale
values. The selected admission source also checks scope validity before entry and
at both final observations.

`CostOwnerChecks.csproj` executes fourteen groups over owned pinned buffers and
the actual RedBird storage classes. The source mode compiles three unchanged
public Core sources; the binary mode references the hash-pinned released Core DLL.
Controlled storage failures are fixture injections. Both modes run the same cases.
CI also compiles and executes the released-binary lane on Windows .NET Framework
4.8.1, matching the SDK target in addition to the .NET 10 checks. That host remains
different from Unity's Mono runtime.

Use .NET 10 and explicitly disable parent build customization on both commands:

```powershell
dotnet restore CostOwnerChecks.csproj --configfile NuGet.Remote.Config -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:CoreReferenceMode=Source -p:RedBirdSourceRoot=<public-redbird-checkout>
dotnet build CostOwnerChecks.csproj --no-restore -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:CoreReferenceMode=Source -p:RedBirdSourceRoot=<public-redbird-checkout>
```

For the binary lane, select `CoreReferenceMode=Binary` and supply
`RedBirdCorePath=<verified-public-Core-DLL>` instead of `RedBirdSourceRoot`.
The project refuses a different binary hash. CI records exact inputs and per-group
results, then retains logs and JSON only.
The fixture reports its actual CLR, pointer width and loaded storage-provider
identity; binary runs verify both the copied file and the loaded assembly checksum.

## Full SDK cost-owner adapter port

The portable [adapter patch](ports/GameUnitManagerAPI.cost-owner.patch) connects
the existing cost owner to the real `GameUnitManagerAPI`. Its exact base, target,
kernel and LF/CRLF result hashes are recorded in `INPUTS.json.costOwnerSdkPort`.
It targets the complete official v2.14.4 source at
`7c3a252794974ee7af5cca02f446ba994fb9d22d`. Its existing five patch hunks are
unchanged; the official projectile-death predicates and Interop source remain
intact. Do not apply it to an arbitrary release or rebase it by inference.

Before changing a full SDK checkout, require that exact base and the recorded
complete-file hash (normalizing only CRLF to LF, preserving the UTF-8 BOM).
Preserve unrelated work. Run `git apply --check` on the patch, then apply it and
verify the recorded result hash. Copy the existing
[`candidate/NativeRecruitmentCostOwner.cs`](candidate/NativeRecruitmentCostOwner.cs)
to the recorded kernel target; refuse a differing existing destination. The
kernel is reused byte-for-byte rather than embedded again in the patch. Retain
the repository license files and build the real full SDK project with its real
dependencies. Use global `SkipPostBuild=true` for a compile-only check and route
output, intermediates and temporary/cache data to owned locations.

This port briefly serializes supported gold/goods setters and scratch restore
intent. Public signatures and getter behavior remain unchanged; existing
cross-thread write interleavings now serialize. The short cost gate contains no
logging, callbacks, native entry or engine-monitor acquisition. It does not make
getters generally thread-safe or govern arbitrary pointer/native writes.

A separate private full-project compilation of this exact official-source
two-file port returned compiler exit 0, with 162 warnings and zero errors. The
original owner run remains RED: its resource-argument checker resolved a relative
compiler path against the wrong directory. Independent actual source, reference,
output and embedded-resource review qualified the compilation without a rebuild.
These private compiler results are separate from the isolated fixtures.

The subsequent offline ABI check passed all 34 installed managed consumers with
zero broken targets or binding errors and the same 14 existing shared-native-site
warnings. Baseline and candidate scans each matched all 370 SDK byte patterns;
all 11 SDK declaration contracts and both installed plugin-graph checks passed.
This is Cecil and native PE-byte metadata coverage. It does not execute target
code, verify minimum loader dependency versions or a candidate-overlay graph,
or establish native behavior, installation, startup, gameplay or performance.
No version stamp was introduced. Internal `Begin`
still has no qualified native caller: current-world/frame/entry ownership,
material settlement and complete-operation/reset/save integration remain
unavailable. No new recruitment admission feature or public provider is enabled. The separate
guarded-frame prototypes and proposed RedBird factory are not installed or
activated by this cost-only patch. Installed-consumer ABI and later runtime
qualification remain separate gates.

## Scope and guarded-frame checks

`GuardedFrameChecks.csproj` runs 44 groups using five exact public dependency DLLs.
The fourteen frame groups cover generated code, actual RedBird contexts on owned
pages and validation, including legacy replay, register/flag/SIMD restoration, abort routing,
nested owned calls and the default result when a guarded callback throws before
dispatch. The source retains a 15-byte instruction test prefix; no game image,
dump or executable is an input. CLR-owned callback thunks remain pinned until the
fixture releases them. CI supplies the finite process deadline.

Six groups cover a separate expanded-frame prototype: it validates ten complete
instructions across a 63-byte displacement and records entry/completion around
five scheduling stores. The owned cell requires exclusive single-writer use and
terminal comparison with the caller's private expected token; a nonzero token
alone is not caller identity. No managed callback occurs between trace markers.
Dormant and unowned routes do not write that trace. These tests use assembled
instructions and owned record buffers, not a game image or code dump.

Activating the expanded route still requires actual whole-range ownership,
ingress, current-world/reset and terminal binding. Relocated memory operations
also change fault PCs; buffer tests do not establish fault/unwind equivalence.
The previously qualified 15-byte generator remains unchanged.

Run this suite through `tools/Invoke-QualificationChecks.ps1 -Suite GuardedFrame`
with `Mode=Binary` and the verified `PublicExtenderDir`. Input and loaded-provider
identities, exact case names and actual runtime information are retained. These
fixtures do not establish the native game's entry/epilogue, AVX-upper, unwind,
live entity, material-payment, lifecycle or installation contracts.

## Verification limits

The complete SDK and its `GameUnitManagerAPI` adapter are not compiled by these
fixtures. Native reader binding, the actual game's machine frame, live purchase,
material settlement, save/load integration and installation require separate verification.
No game is launched. This selected source package is a development contribution,
and its checks do not establish gameplay acceptance or performance.

RedBird remains a separately licensed dependency from
[its upstream project](https://gitlab.com/Rawra/redbird). Source-mode inputs are
fetched from a pinned public contribution mirror with the upstream license retained.

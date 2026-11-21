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

## Verification limits

The complete SDK and its `GameUnitManagerAPI` adapter are not compiled by this
fixture. Native reader binding, the machine-code frame, live purchase, material
settlement, save/load integration and installation require separate verification.
No game is launched. This selected source package is a development contribution,
and its checks do not establish gameplay acceptance or performance.

RedBird remains a separately licensed dependency from
[its upstream project](https://gitlab.com/Rawra/redbird). Source-mode inputs are
fetched from a pinned public contribution mirror with the upstream license retained.

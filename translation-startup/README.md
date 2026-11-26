# Deferred translation startup: selected source and CPU checks

This focused proposal targets official Script Extender main
`81a7a945c47c2c5dcc347cd1ad3661a54be02429` (2.14.6).
Read the [contract and material public Apply timing change](TRANSLATION_STARTUP.md)
before using or integrating these five production files. Successful Apply means
accepted; the three translation detours activate at the qualifying MasterController
PRE boundary. The adjacent early-locale and original-text risks remain open.

This is a standalone selected-source fixture, not a complete SDK or installable
package. Explicit MonoMod/Noesis/game/registry/log leaves run no executable hook,
game assembly or native instruction. No game binary, private native IL data,
private SDK version/identity or workspace evidence is included. The repository
[LGPL notice](../LICENSE.txt) and [GPL text](../COPYING.GPLv3.txt) remain applicable.

The project compiles the genuine full manager and other four production files,
plus two fixture sources. It has no extracted Apply/Unload. Unrelated void leaves
are not game ABI tests; legacy omissions in unrelated hook cleanup are not fixed.
Candidate runs all fourteen groups. Baseline replaces only whole Manager.cs with
its exact official preimage and runs OtherComponent; this mixed wiring control
must actually fail with the specific eager-preparation CAUSAL_REGRESSION/raw1.
No compile/setup/other failure counts as RED. The caught failure cannot display
an unhandled-exception dialog. Candidate must raw0/all14.

Local Windows x64 SDK10.0.300 / .NET10.0.8 qualification reproduced that baseline
failure and passed the candidate14. Both DLL/PDB identities and seven source
checksums matched. These are local controlled observations, not hosted CI or
full SDK/net481/provider-link/UnityMono/game/UI/startup/performance/install proof.
The public project adds only a defaulted ManagerSource selector; all seven C#
bodies are byte-identical to the executed images. Hosted CI validates packaging.

## Run the standalone checks

Use SDK10.0.300 and runtime/reference pack10.0.8 (fixed global.json/runtimeconfig).
There are no package references; NuGet.Config clears feeds. Run from this folder.
Set child-only DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false and
DOTNET_GENERATE_ASPNET_CERTIFICATE=false. Put DOTNET_CLI_HOME, TEMP/TMP, NuGet
caches, logs and build outputs in an owned work directory; preserve parent PATH.
Run `python CheckFixture.py inputs` before/after the builds and runs.

Restore with `dotnet restore TranslationStartupChecks.csproj --configfile NuGet.Config`
and build with `dotnet build TranslationStartupChecks.csproj --configuration Release --no-restore`.
For BOTH commands explicitly use ImportDirectoryBuildProps=false,
ImportDirectoryBuildTargets=false, NuGetAudit=false, UseSharedCompilation=false,
and separate BaseIntermediateOutputPath/MSBuildProjectExtensionsPath/BaseOutputPath.
Select `-p:ManagerSource=baseline/ManagedHookManager.cs` only for the baseline;
candidate uses the default src/ManagedHookManager.cs.

Run the baseline DLL with `--baseline-apply` and retain its actual raw1/log;
run candidate without arguments and retain raw0/log. Use
`python CheckFixture.py log baseline <log> <actual-exit>` or the candidate form
for the distinct oracles. [The workflow](../.github/workflows/translation-startup-checks.yml)
contains complete fixed commands. The Python checker hashes/parses data only;
it never launches builds or fixtures. A separate development-SDK build hold
is neither exercised nor released by these checks.

# Loader lifecycle control-flow checks

These checks compile the complete actual `BulkMapLoaderDetours.cs` file and the
six original event/base source files. They call its static wrapper methods with
managed Original delegates and explicitly injected publisher/logger failures.
No wrapper is copied or reimplemented in the fixture.

The 50 named groups cover at-most-once dispatch, first-result retention after a
Post fault, unchanged Pre-fault forwarding, full-width return values, read-only
raw input forwarding, existing ignored Skip behavior, the LoadSave/Post-return
asymmetry, unguarded error-logger faults, and nested/failing start suppression.
Two controls retain the existing Save wrapper's single-forward behavior.

The hook, scanner, context, archive and publisher classes in
`FixtureSdkDoubles.cs` are compile/control-flow doubles. Installation/scanning
and archive-building paths throw if accidentally reached. The actual production
constructor is never called. Its static handle initialization uses these doubles.
Strings read by the production unsafe methods are small fixture-owned unmanaged
UTF-16 allocations and are released after each case.

This does **not** qualify the full SDK dependency graph, actual RedBird handles,
R3 observer policy, native ABI or Original functions, hooks, threading/lifecycle
ownership, native load/reset, game behavior or installation. No private production
field name, reflection seam or game input is used. Suppression is observed through
the actual unload wrapper's notifications. A successful campaign wrapper with
empty publishers resets the known old leaked flag before each baseline case.

## Build inputs

The default production root is `../../src/SHCDESE.BepInEx` from this directory.
`LoaderSourcePath` may select a separately frozen whole original or corrected
loader file; `PayloadSourceRoot` selects the original event/base sources.
Never replace them with simplified wrapper/event copies.

Use .NET 10 or `-p:TargetFramework=net481`, x64. The Framework lane also requires
`-p:SystemMemoryPath=<admitted-System.Memory.dll>` because the real production
constructor signature contains `ReadOnlySpan<byte>`. The file is hash-checked
against the actual System.Memory 4.6.3 `lib/net462` provider:
`d5e8e4866f9cfa66f7765660f84b210198893e55335487afe5ebda342c0e913d`.
The Framework executable requires
`--system-memory-sha d5e8e4866f9cfa66f7765660f84b210198893e55335487afe5ebda342c0e913d`;
it verifies the actually loaded provider checksum and reports its MVID. No Span
operation is executed. The net10 executable takes no arguments.

This project has no PackageReferences. The provided runner supplies
`NuGet.Loader.Config` to clear package sources. A compatible installed targeting
pack is required. For both restore and build, pass
`-p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false` globally.
Build settings must remain child-process-local; isolated CLI homes must explicitly
set `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` and
`DOTNET_GENERATE_ASPNET_CERTIFICATE=false` in every child environment.

The runner must require exact unique PASS labels, no FAIL labels, exit zero and
the expected group manifest. A compilation/setup failure is not an executed test.
For the unmodified pinned original, a deliberate RED run must match the 15 named
defects in the manifest, rather than merely returning nonzero. All other groups
must pass. SDK/game qualification remains separate even after these checks pass.

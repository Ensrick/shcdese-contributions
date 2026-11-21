# Native loader invocation checks

Selected-source contribution for the
[Stronghold Crusader DE Script Extender](https://gitlab.com/rawra-stronghold-crusader/shcde-script-extender).
The upstream LGPL license and incorporated GPL text are retained.

Six load, unload and start wrappers currently catch native or Post failures and
can invoke Original again. The candidate tracks whether the native call began
and returned, preventing that retry. Normal start calls also restore their prior
unload-notification suppression in `finally`, including nested starts and faults.
Existing Pre-failure forwarding, ignored Skip flags and return-value differences
remain explicit; see the [fixture contract](tests/LoaderLifecycleChecks/README.md).

The fixture compiles the complete production class and six original event/base
files, with explicit SDK control-flow doubles. It checks fifty groups in both
.NET 10 and Windows Framework-targeted modes. The exact original public source
is a negative control: only the fifteen named known defects may fail. Compilation
errors, missing groups, unexpected failures or different raw exits are failures,
not regression evidence. No native Original, hook, scanner or game is executed.

On Windows x64, run `tools/Invoke-LoaderChecks.ps1` with a fresh result directory and an explicit
`Original` or `Candidate` variant and `net10.0` or `net481` target. The Framework
target also requires the real System.Memory reference prepared by
`tools/Prepare-LoaderReference.ps1`. Child SDK settings are isolated; parent build
customization is disabled. Persistent User/Machine PATH types and hashes are
checked before and after the child processes, without writing those values.
Only JSON and text logs are retained.

System.Memory 4.6.3 is a separately licensed Microsoft .NET component, under the
[MIT license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT). Its exact
[official NuGet package](https://www.nuget.org/packages/System.Memory/4.6.3) and
selected declaration DLL are hash-pinned. This fixture does not exercise Span
operations or qualify the complete SDK dependency graph. Dependencies are never
uploaded as result artifacts.

This is a source proposal and controlled regression check, not an installed SDK
or native-load qualification. Full SDK build/linkage, actual R3 observer behavior,
native ABI, world/reset ownership and in-game acceptance remain separate checks.

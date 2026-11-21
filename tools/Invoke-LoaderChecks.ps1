#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Original','Candidate')][string]$Variant,
    [Parameter(Mandatory)][ValidateSet('net10.0','net481')][string]$TargetFramework,
    [Parameter(Mandatory)][string]$ResultDirectory,
    [string]$SystemMemoryPath
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$results = [IO.Path]::GetFullPath($ResultDirectory)
if (Test-Path -LiteralPath $results) { throw 'Result directory must be fresh.' }
[void][IO.Directory]::CreateDirectory($results)
$inputs = Get-Content -LiteralPath (Join-Path $repo 'LOADER_INPUTS.json') -Raw | ConvertFrom-Json
$groups = Get-Content -LiteralPath (Join-Path $repo 'tests/LoaderLifecycleChecks/EXPECTED_GROUPS.json') -Raw | ConvertFrom-Json
$pins = [Collections.Generic.List[object]]::new()
foreach ($pin in $inputs.sourceFiles) {
    $data = [IO.File]::ReadAllBytes((Join-Path $repo $pin.path))
    $raw = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data)).ToLowerInvariant()
    $lfData = [Text.Encoding]::UTF8.GetBytes([Text.Encoding]::UTF8.GetString($data).Replace("`r`n", "`n"))
    $lf = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($lfData)).ToLowerInvariant()
    if ($lf -cne $pin.sha256LF -or ($raw -cne $pin.sha256LF -and $raw -cne $pin.sha256CRLF)) { throw "Source pin mismatch: $($pin.path)" }
    $pins.Add([ordered]@{path=$pin.path;bytes=$data.Length;sha256=$raw;sha256LF=$lf})
}
$loader = Join-Path $repo 'src/SHCDESE.BepInEx/Detours/BulkMapLoaderDetours.cs'
if ($Variant -eq 'Original') {
    # The exact public source is the negative-control input, never a game binary.
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    $client.MaxResponseContentBufferSize = 65536
    try {
        $reply = $client.GetByteArrayAsync([string]$inputs.originalSource.url).GetAwaiter().GetResult()
        $source = [Text.Encoding]::UTF8.GetString($reply) | ConvertFrom-Json
        if ($source.commit_id -cne $inputs.originalSource.commit -or $source.blob_id -cne $inputs.originalSource.blob -or $source.encoding -cne 'base64') { throw 'Original source identity mismatch.' }
        $data = [Convert]::FromBase64String($source.content)
    } finally { $client.Dispose() }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data)).ToLowerInvariant()
    if ($data.Length -ne $inputs.originalSource.bytes -or $hash -cne $inputs.originalSource.sha256) { throw 'Original source content mismatch.' }
    $loader = Join-Path $results 'original-BulkMapLoaderDetours.cs'
    [IO.File]::WriteAllBytes($loader,$data)
    $pins.Add([ordered]@{path='original-BulkMapLoaderDetours.cs';bytes=$data.Length;sha256=$hash})
}
if ($TargetFramework -eq 'net481') {
    if ((Get-FileHash -LiteralPath $SystemMemoryPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inputs.systemMemory.entrySha256) { throw 'System.Memory input differs.' }
    $pins.Add([ordered]@{path='System.Memory.dll';bytes=(Get-Item -LiteralPath $SystemMemoryPath).Length;sha256=$inputs.systemMemory.entrySha256})
}
[ordered]@{variant=$Variant;targetFramework=$TargetFramework;commit=$env:GITHUB_SHA;inputs=$pins.ToArray();scope='Actual linked source with explicit SDK control-flow doubles; no full SDK or game.'} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $results 'inputs.json') -Encoding utf8

function Invoke-OwnedProcess([string]$Executable, [string[]]$Arguments, [string]$LogName) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    # A copied child environment keeps SDK first use out of persistent user settings.
    $start.Environment['DOTNET_ADD_GLOBAL_TOOLS_TO_PATH'] = 'false'
    $start.Environment['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    $start.Environment['DOTNET_CLI_HOME'] = Join-Path $results 'dotnet-home'
    $start.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $start.Environment['MSBUILDDISABLENODEREUSE'] = '1'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw 'The owned child did not start.' }
        $out = $process.StandardOutput.ReadToEndAsync()
        $err = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) { $process.Kill($true); $process.WaitForExit(); throw 'Owned source-check child exceeded 120 seconds.' }
        $text = $out.GetAwaiter().GetResult() + $err.GetAwaiter().GetResult()
        if ([Text.Encoding]::UTF8.GetByteCount($text) -gt 2MB) { throw 'Source-check output exceeded its evidence budget.' }
        [IO.File]::WriteAllText((Join-Path $results $LogName),$text,[Text.UTF8Encoding]::new($false))
        Write-Host $text
        return [pscustomobject]@{exitCode=$process.ExitCode;text=$text}
    } finally { $process.Dispose() }
}

function Get-PathEnvironmentStamp {
    $records = foreach ($entry in @(
        @{scope='User';hive=[Microsoft.Win32.Registry]::CurrentUser;key='Environment'},
        @{scope='Machine';hive=[Microsoft.Win32.Registry]::LocalMachine;key='SYSTEM\CurrentControlSet\Control\Session Manager\Environment'}
    )) {
        $key = $entry.hive.OpenSubKey($entry.key,$false)
        try {
            $present = $null -ne $key -and 'Path' -in $key.GetValueNames()
            $value = if ($present) { [string]$key.GetValue('Path',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { '' }
            [ordered]@{scope=$entry.scope;present=$present;kind=$(if ($present) {$key.GetValueKind('Path').ToString()} else {$null});sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::Unicode.GetBytes($value))).ToLowerInvariant()}
        } finally { if ($null -ne $key) { $key.Dispose() } }
    }
    return @($records)
}

if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) { throw 'The qualified runner requires Windows.' }
$pathBefore = Get-PathEnvironmentStamp
try {
$dotnet = (Get-Command dotnet -CommandType Application).Source
$sdk = Invoke-OwnedProcess $dotnet @('--version') 'sdk.log'
if ($sdk.exitCode -ne 0) { throw 'SDK identity command failed.' }
$framework = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
[ordered]@{os=[Environment]::OSVersion.VersionString;processBits=([IntPtr]::Size*8);sdk=$sdk.text.Trim();frameworkRelease=$framework.Release;frameworkVersion=$framework.Version;imageVersion=$env:ImageVersion} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'host.json') -Encoding utf8
$project = Join-Path $repo 'tests/LoaderLifecycleChecks/LoaderLifecycleChecks.csproj'
$output = Join-Path $results 'build'
[string[]]$properties = @('-p:ImportDirectoryBuildProps=false','-p:ImportDirectoryBuildTargets=false',"-p:TargetFramework=$TargetFramework",'-p:PlatformTarget=x64','-p:Prefer32Bit=false',"-p:LoaderSourcePath=$loader","-p:BaseIntermediateOutputPath=$results/obj/")
if ($TargetFramework -eq 'net481') { $properties += "-p:SystemMemoryPath=$([IO.Path]::GetFullPath($SystemMemoryPath))" }
$restore = Invoke-OwnedProcess $dotnet (@('restore',$project,'--configfile',(Join-Path $repo 'NuGet.Loader.Config'),'--disable-parallel') + $properties) 'restore.log'
if ($restore.exitCode -ne 0) { throw 'Actual-source fixture restore failed; no regression credit.' }
$build = Invoke-OwnedProcess $dotnet (@('build',$project,'-c','Release','--no-restore','--disable-build-servers','--maxcpucount:2','-p:UseSharedCompilation=false','--verbosity:normal','--output',$output) + $properties) 'build.log'
if ($build.exitCode -ne 0) { throw 'Actual-source fixture compilation failed; no regression credit.' }
if ($TargetFramework -eq 'net481') {
    $copied = Join-Path $output 'System.Memory.dll'
    if ((Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inputs.systemMemory.entrySha256) { throw 'Copied provider differs from the admitted reference.' }
    $execution = Invoke-OwnedProcess (Join-Path $output 'LoaderLifecycleChecks.exe') @('--system-memory-sha',$inputs.systemMemory.entrySha256) 'execution.log'
} else {
    $execution = Invoke-OwnedProcess $dotnet @((Join-Path $output 'LoaderLifecycleChecks.dll')) 'execution.log'
}
$passed = @([regex]::Matches($execution.text,'(?m)^PASS ([A-Za-z0-9.]+)\r?$') | ForEach-Object {$_.Groups[1].Value})
$failed = @([regex]::Matches($execution.text,'(?m)^FAIL ([A-Za-z0-9.]+) ::') | ForEach-Object {$_.Groups[1].Value})
$all = @($passed) + @($failed)
$expected = @($groups.expectedGroups)
$expectedFailures = if ($Variant -eq 'Original') { @($groups.originalRedFailureGroups) } else { @() }
$missing = @($expected | Where-Object {$_ -cnotin $all})
$unexpected = @($all | Where-Object {$_ -cnotin $expected})
$wrongFailure = @($failed | Where-Object {$_ -cnotin $expectedFailures})
$missingFailure = @($expectedFailures | Where-Object {$_ -cnotin $failed})
$summaryMatches = [regex]::Matches($execution.text,'(?m)^SUMMARY (\{[^\r\n]+\})\r?$')
$summary = if ($summaryMatches.Count -eq 1) { $summaryMatches[0].Groups[1].Value | ConvertFrom-Json } else { $null }
$expectedExit = if ($Variant -eq 'Original') { 1 } else { 0 }
$valid = $execution.exitCode -eq $expectedExit -and $all.Count -eq $groups.expectedCount -and
    @($all | Select-Object -Unique).Count -eq $all.Count -and $missing.Count -eq 0 -and $unexpected.Count -eq 0 -and
    $wrongFailure.Count -eq 0 -and $missingFailure.Count -eq 0 -and $null -ne $summary -and
    $summary.totalGroups -eq $groups.expectedCount -and $summary.rawExit -eq $execution.exitCode -and
    @($summary.failedNames).Count -eq $failed.Count -and @($summary.failedNames | Select-Object -Unique).Count -eq $failed.Count -and
    @($summary.failedNames | Where-Object {$_ -cnotin $failed}).Count -eq 0
$status = if (-not $valid) {'QUALIFICATION_FAILED'} elseif ($Variant -eq 'Original') {'EXPECTED_ORIGINAL_DEFECTS_REPRODUCED'} else {'CANDIDATE_GROUPS_PASSED'}
[ordered]@{status=$status;variant=$Variant;targetFramework=$TargetFramework;commit=$env:GITHUB_SHA;rawExit=$execution.exitCode;passed=$passed;failed=$failed;expectedFailures=$expectedFailures;missing=$missing;unexpected=$unexpected;scope='Actual source control flow only; SDK doubles, no native target/game/installed-hook/full-SDK proof.'} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $results 'result.json') -Encoding utf8
if (-not $valid) { throw 'Exact original/candidate group qualification failed.' }
Write-Host $status
} finally {
    $pathAfter = Get-PathEnvironmentStamp
    $unchanged = ($pathBefore | ConvertTo-Json -Compress) -ceq ($pathAfter | ConvertTo-Json -Compress)
    [ordered]@{before=$pathBefore;after=$pathAfter;unchanged=$unchanged;scope='Persistent User/Machine PATH value types and hashes only; values are never written.'} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $results 'environment.json') -Encoding utf8
    if (-not $unchanged) { throw 'Persistent PATH changed during the owned launcher interval; no restoration attempted.' }
}

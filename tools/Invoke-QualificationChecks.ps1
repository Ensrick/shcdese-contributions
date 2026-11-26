[CmdletBinding()]
param(
    [ValidateSet('CostOwner','GuardedFrame')][string]$Suite = 'CostOwner',
    [Parameter(Mandatory=$true)][ValidateSet('Source','Binary')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$ResultDirectory,
    [ValidateSet('net10.0','net481')][string]$TargetFramework = 'net10.0',
    [string]$RedBirdSourceRoot,
    [string]$RedBirdCorePath,
    [string]$PublicExtenderDir
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$results = [IO.Path]::GetFullPath($ResultDirectory)
if (Test-Path -LiteralPath $results) { throw 'Result directory must be fresh.' }
[void][IO.Directory]::CreateDirectory($results)
$inputs = Get-Content -LiteralPath (Join-Path $repo 'INPUTS.json') -Raw | ConvertFrom-Json
$isFrame = $Suite -eq 'GuardedFrame'
if ($isFrame -and ($Mode -ne 'Binary' -or $TargetFramework -ne 'net10.0')) { throw 'The guarded frame requires the released binary lane on Windows x64 .NET 10.' }
$project = if ($isFrame) { 'GuardedFrameChecks.csproj' } else { 'CostOwnerChecks.csproj' }
$assembly = if ($isFrame) { 'GuardedFrameChecks' } else { 'CostOwnerChecks' }
$records = [Collections.Generic.List[object]]::new()
function Assert-SourcePin([string]$Path, $Pin) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $raw = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    $normalized = [Text.Encoding]::UTF8.GetString($bytes).Replace("`r`n", "`n")
    $lf = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($normalized))).ToLowerInvariant()
    if ($lf -cne $Pin.sha256LF -or ($raw -cne $Pin.sha256LF -and $raw -cne $Pin.sha256CRLF)) { throw "Source pin mismatch: $($Pin.path)" }
    $records.Add([ordered]@{ path=$Pin.path; bytes=$bytes.Length; sha256=$raw; normalizedLfSha256=$lf })
}
foreach ($pin in $inputs.productionFiles) { Assert-SourcePin (Join-Path $repo $pin.path) $pin }
$properties = @('-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false', "-p:CoreReferenceMode=$Mode", "-p:TargetFramework=$TargetFramework", '-p:PlatformTarget=x64', '-p:Prefer32Bit=false')
if ($isFrame) {
    foreach ($pin in $inputs.publicFrameReferences) {
        $path = Join-Path $PublicExtenderDir $pin.name
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $pin.sha256) { throw 'Public frame provider identity mismatch.' }
        $records.Add([ordered]@{ path=$pin.name; sha256=$actual; bytes=(Get-Item -LiteralPath $path).Length })
    }
    $properties += "-p:PublicExtenderDir=$([IO.Path]::GetFullPath($PublicExtenderDir))"
} elseif ($Mode -eq 'Source') {
    foreach ($pin in $inputs.publicSource.files) { Assert-SourcePin (Join-Path $RedBirdSourceRoot $pin.path) $pin }
    $properties += "-p:RedBirdSourceRoot=$([IO.Path]::GetFullPath($RedBirdSourceRoot))"
} else {
    $actual = (Get-FileHash -LiteralPath $RedBirdCorePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $inputs.publicCore.sha256) { throw 'Released Core identity mismatch.' }
    $records.Add([ordered]@{ path='RedBird.Core.dll'; sha256=$actual; bytes=(Get-Item -LiteralPath $RedBirdCorePath).Length })
    $properties += "-p:RedBirdCorePath=$([IO.Path]::GetFullPath($RedBirdCorePath))"
}
$output = Join-Path $results 'build'
$properties += "-p:BaseIntermediateOutputPath=$results/obj/"
$admission = [ordered]@{ suite=$Suite; mode=$Mode; targetFramework=$TargetFramework; commit=$env:GITHUB_SHA; inputs=$records.ToArray(); localGameInputs=$false }
$admission | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $results 'inputs.json') -Encoding utf8
$framework = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
[ordered]@{ os=[Environment]::OSVersion.VersionString; processBits=([IntPtr]::Size*8); frameworkRelease=$framework.Release; frameworkVersion=$framework.Version; imageVersion=$env:ImageVersion } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'host.json') -Encoding utf8
Push-Location $repo
try {
    & dotnet restore $project --configfile NuGet.Remote.Config --disable-parallel @properties
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $LASTEXITCODE" }
    & dotnet build $project -c Release --no-restore --disable-build-servers --maxcpucount:2 -p:UseSharedCompilation=false --output $output @properties
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    $runtimeIdentity = 'self'
    if ($Mode -eq 'Binary') {
        $runtimePins = if ($isFrame) { @($inputs.publicFrameReferences) } else { @($inputs.publicCore) }
        $runtimeRecords = foreach ($pin in $runtimePins) {
            $copied = Join-Path $output $pin.name
            $copiedHash = (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($copiedHash -cne $pin.sha256) { throw 'Copied runtime provider differs from the admitted binary.' }
            [ordered]@{ path=('build/' + $pin.name); sha256=$copiedHash; bytes=(Get-Item -LiteralPath $copied).Length }
        }
        @($runtimeRecords) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'runtime-provider.json') -Encoding utf8
        $runtimeIdentity = $inputs.publicCore.sha256
    }
    # Keep a single path as an array: scalar string splatting passes its characters.
    [string[]]$runtimeArguments = if ($isFrame) { @(Join-Path $repo 'INPUTS.json') } else { @($Mode,$runtimeIdentity) }
    if ($TargetFramework -eq 'net481') {
        & (Join-Path $output ($assembly + '.exe')) @runtimeArguments 2>&1 | Tee-Object -FilePath (Join-Path $results 'execution.log')
    } else {
        & dotnet (Join-Path $output ($assembly + '.dll')) @runtimeArguments 2>&1 | Tee-Object -FilePath (Join-Path $results 'execution.log')
    }
    $executionExit = $LASTEXITCODE
    $lines = Get-Content -LiteralPath (Join-Path $results 'execution.log')
    $passed = @($lines | Where-Object { $_ -cmatch '^PASS [A-Za-z0-9.]+$' } | ForEach-Object { $_.Substring(5) })
    $failed = @($lines | Where-Object { $_ -cmatch '^FAIL ' })
    $expected = if ($isFrame) { @($inputs.expectedGuardedGroupNames) } else { @($inputs.expectedGroupNames) }
    $expectedCount = if ($isFrame) { $inputs.expectedGuardedGroups } else { $inputs.expectedCostOwnerGroups }
    $missing = @($expected | Where-Object { $_ -cnotin $passed })
    $unexpected = @($passed | Where-Object { $_ -cnotin $expected })
    $result = [ordered]@{ suite=$Suite; mode=$Mode; targetFramework=$TargetFramework; commit=$env:GITHUB_SHA; exitCode=$executionExit; passedGroups=$passed; failures=$failed; missing=$missing; unexpected=$unexpected; nativeGameExecuted=$false }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $results 'result.json') -Encoding utf8
    if ($executionExit -ne 0 -or $failed.Count -ne 0 -or $missing.Count -ne 0 -or $unexpected.Count -ne 0 -or
        $passed.Count -ne $expectedCount -or @($passed | Select-Object -Unique).Count -ne $passed.Count) {
        throw 'Selected group qualification failed.'
    }
} finally { Pop-Location }

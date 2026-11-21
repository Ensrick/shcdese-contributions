[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Source','Binary')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$ResultDirectory,
    [ValidateSet('net10.0','net481')][string]$TargetFramework = 'net10.0',
    [string]$RedBirdSourceRoot,
    [string]$RedBirdCorePath
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$results = [IO.Path]::GetFullPath($ResultDirectory)
if (Test-Path -LiteralPath $results) { throw 'Result directory must be fresh.' }
[void][IO.Directory]::CreateDirectory($results)
$inputs = Get-Content -LiteralPath (Join-Path $repo 'INPUTS.json') -Raw | ConvertFrom-Json
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
if ($Mode -eq 'Source') {
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
$admission = [ordered]@{ mode=$Mode; targetFramework=$TargetFramework; commit=$env:GITHUB_SHA; inputs=$records.ToArray(); localGameInputs=$false }
$admission | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $results 'inputs.json') -Encoding utf8
Push-Location $repo
try {
    & dotnet restore CostOwnerChecks.csproj --configfile NuGet.Remote.Config --disable-parallel @properties
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $LASTEXITCODE" }
    & dotnet build CostOwnerChecks.csproj -c Release --no-restore --disable-build-servers --maxcpucount:2 -p:UseSharedCompilation=false --output $output @properties
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    $runtimeIdentity = 'self'
    if ($Mode -eq 'Binary') {
        $copied = Join-Path $output 'RedBird.Core.dll'
        $copiedHash = (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($copiedHash -cne $inputs.publicCore.sha256) { throw 'Copied Core runtime provider differs from the admitted binary.' }
        [ordered]@{ path='build/RedBird.Core.dll'; sha256=$copiedHash; bytes=(Get-Item -LiteralPath $copied).Length } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'runtime-provider.json') -Encoding utf8
        $runtimeIdentity = $copiedHash
    }
    if ($TargetFramework -eq 'net481') {
        & (Join-Path $output 'CostOwnerChecks.exe') $Mode $runtimeIdentity 2>&1 | Tee-Object -FilePath (Join-Path $results 'execution.log')
    } else {
        & dotnet (Join-Path $output 'CostOwnerChecks.dll') $Mode $runtimeIdentity 2>&1 | Tee-Object -FilePath (Join-Path $results 'execution.log')
    }
    $executionExit = $LASTEXITCODE
    $lines = Get-Content -LiteralPath (Join-Path $results 'execution.log')
    $passed = @($lines | Where-Object { $_ -cmatch '^PASS [A-Za-z0-9]+$' } | ForEach-Object { $_.Substring(5) })
    $failed = @($lines | Where-Object { $_ -cmatch '^FAIL ' })
    $expected = @($inputs.expectedGroupNames)
    $missing = @($expected | Where-Object { $_ -cnotin $passed })
    $unexpected = @($passed | Where-Object { $_ -cnotin $expected })
    $result = [ordered]@{ mode=$Mode; targetFramework=$TargetFramework; commit=$env:GITHUB_SHA; exitCode=$executionExit; passedGroups=$passed; failures=$failed; missing=$missing; unexpected=$unexpected; nativeGameExecuted=$false }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $results 'result.json') -Encoding utf8
    if ($executionExit -ne 0 -or $failed.Count -ne 0 -or $missing.Count -ne 0 -or $unexpected.Count -ne 0 -or
        $passed.Count -ne $inputs.expectedCostOwnerGroups -or @($passed | Select-Object -Unique).Count -ne $passed.Count) {
        throw 'Cost-owner group qualification failed.'
    }
} finally { Pop-Location }

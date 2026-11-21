#requires -Version 7.4
<#
.SYNOPSIS
Prepares one hash-pinned System.Memory reference for the isolated net481 loader fixture.
.DESCRIPTION
Downloads only the fixed official NuGet package into bounded memory, verifies its
identity, then reads only lib/net462/System.Memory.dll. A fresh output directory
receives that verified reference and a small provenance JSON. No package restore,
global configuration, environment change or assembly load occurs. The fixture
uses the declared type reference; this does not qualify Span operations or the SDK.
The package is MIT licensed; retain its attribution in the qualification README.
Do not upload the dependency DLL as a qualification artifact.
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$OutputDirectory)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$packageUrl = 'https://api.nuget.org/v3-flatcontainer/system.memory/4.6.3/system.memory.4.6.3.nupkg'
$packageBytes = 192935
$packageSha256 = '26078aeb758c9ae985e8bf851f973026061da6a5eb4837204d0c2d2204c72955'
$entryName = 'lib/net462/System.Memory.dll'
$referenceBytes = 145200
$referenceSha256 = 'd5e8e4866f9cfa66f7765660f84b210198893e55335487afe5ebda342c0e913d'
$root = [IO.Path]::GetFullPath($OutputDirectory)
$parent = [IO.Path]::GetDirectoryName($root.TrimEnd([IO.Path]::DirectorySeparatorChar))
if (Test-Path -LiteralPath $root) { throw 'Loader reference directory must be fresh.' }
if ([string]::IsNullOrEmpty($parent) -or -not [IO.Directory]::Exists($parent) -or
    (([IO.File]::GetAttributes($parent) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
    throw 'Supply a fresh child of an existing plain output directory.'
}

function Read-ExactBytes([IO.Stream]$Stream, [int]$ExpectedBytes,
    [Threading.CancellationToken]$CancellationToken) {
    if ($ExpectedBytes -le 0 -or $ExpectedBytes -gt 192935) { throw 'Invalid fixed input bound.' }
    $memory = [IO.MemoryStream]::new($ExpectedBytes)
    try {
        $buffer = [byte[]]::new(16384)
        [int]$total = 0
        while ($true) {
            # One extra byte detects an overrun without retaining an oversized body.
            $count = [Math]::Min($buffer.Length, $ExpectedBytes - $total + 1)
            $read = $Stream.ReadAsync($buffer, 0, $count, $CancellationToken).GetAwaiter().GetResult()
            if ($read -eq 0) { break }
            $total += $read
            if ($total -gt $ExpectedBytes) { throw 'Fixed dependency exceeded its exact byte bound.' }
            $memory.Write($buffer, 0, $read)
        }
        if ($total -ne $ExpectedBytes) { throw 'Fixed dependency length differs from its pin.' }
        return ,$memory.ToArray()
    } finally { $memory.Dispose() }
}

function Get-BytesSha256([byte[]]$Bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(30)
$client.MaxResponseContentBufferSize = $packageBytes
$timeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(30))
$response = $null
$stream = $null
try {
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('SHCDESE-LoaderReferenceQualification/1')
    $response = $client.GetAsync([uri]$packageUrl, [Net.Http.HttpCompletionOption]::ResponseHeadersRead,
        $timeout.Token).GetAwaiter().GetResult()
    if ([int]$response.StatusCode -ne 200) { throw "Official NuGet package returned HTTP $([int]$response.StatusCode)." }
    $length = $response.Content.Headers.ContentLength
    if ($null -ne $length -and $length -ne $packageBytes) { throw 'HTTP package length differs from its pin.' }
    $stream = $response.Content.ReadAsStreamAsync($timeout.Token).GetAwaiter().GetResult()
    [byte[]]$package = Read-ExactBytes $stream $packageBytes $timeout.Token
} finally {
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $response) { $response.Dispose() }
    $timeout.Dispose()
    $client.Dispose()
    $handler.Dispose()
}
if ((Get-BytesSha256 $package) -cne $packageSha256) { throw 'Official NuGet package checksum mismatch.' }

$packageStream = [IO.MemoryStream]::new($package, $false)
$zip = [IO.Compression.ZipArchive]::new($packageStream, [IO.Compression.ZipArchiveMode]::Read)
try {
    if ($zip.Entries.Count -gt 256) { throw 'Official NuGet package exceeds the fixed entry bound.' }
    $entries = @($zip.Entries | Where-Object FullName -CEQ $entryName)
    if ($entries.Count -ne 1 -or $entries[0].Length -ne $referenceBytes) {
        throw 'Expected exactly one pinned net462 System.Memory reference.'
    }
    $entryStream = $entries[0].Open()
    try { [byte[]]$reference = Read-ExactBytes $entryStream $referenceBytes ([Threading.CancellationToken]::None) }
    finally { $entryStream.Dispose() }
} finally { $zip.Dispose(); $packageStream.Dispose() }
if ((Get-BytesSha256 $reference) -cne $referenceSha256) { throw 'System.Memory reference checksum mismatch.' }

# Verify all input bytes before creating any output. Never reuse or clean an existing directory.
if (Test-Path -LiteralPath $root) { throw 'Loader reference directory appeared during preparation.' }
[void](New-Item -ItemType Directory -Path $root -ErrorAction Stop)
$target = Join-Path $root 'System.Memory.dll'
$output = [IO.FileStream]::new($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $output.Write($reference, 0, $reference.Length); $output.Flush($true) }
finally { $output.Dispose() }
if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $referenceSha256) {
    throw 'Written System.Memory reference differs from the admitted bytes.'
}
$receipt = [ordered]@{
    schema = 'selected-loader-reference/1'
    package = [ordered]@{ url=$packageUrl; bytes=$packageBytes; sha256=$packageSha256; license='MIT' }
    entry = [ordered]@{ name=$entryName; bytes=$referenceBytes; sha256=$referenceSha256 }
    referencePath = $target
    scope = 'Declared net462 type reference for isolated net481 loader fixture; no Span operation or full SDK qualification.'
    uploadDependency = $false
}
[byte[]]$json = [Text.Encoding]::UTF8.GetBytes(($receipt | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
if ($json.Length -gt 4096) { throw 'Loader reference provenance exceeds its 4 KiB bound.' }
$record = [IO.FileStream]::new((Join-Path $root 'loader-reference.json'), [IO.FileMode]::CreateNew,
    [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $record.Write($json, 0, $json.Length); $record.Flush($true) }
finally { $record.Dispose() }
$receipt

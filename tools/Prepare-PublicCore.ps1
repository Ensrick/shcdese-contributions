[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory, [switch]$IncludeFrameReferences)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) { throw 'Public dependency directory must be fresh.' }
[void][IO.Directory]::CreateDirectory($root)
$archive = Join-Path $root 'public-extender.zip'
$inputPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'INPUTS.json'
$inputs = Get-Content -LiteralPath $inputPath -Raw | ConvertFrom-Json
$selected = if ($IncludeFrameReferences) { @($inputs.publicFrameReferences) } else { @($inputs.publicCore) }
try {
    Invoke-WebRequest -Uri $inputs.publicCoreArchive.url -OutFile $archive -TimeoutSec 60
    if ((Get-Item -LiteralPath $archive).Length -ne $inputs.publicCoreArchive.bytes -or
        (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inputs.publicCoreArchive.sha256) {
        throw 'Official public archive identity mismatch.'
    }
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::OpenRead($archive)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
    try {
        foreach ($expected in $selected) {
            if ($expected.name -cnotin @('Iced.dll','RedBird.Abstractions.dll','RedBird.Core.dll','RedBird.X64.dll','Microsoft.Extensions.Logging.Abstractions.dll')) { throw 'Unexpected public provider.' }
            $entries = @($zip.Entries | Where-Object FullName -CEQ $expected.entry)
            if ($entries.Count -ne 1 -or $entries[0].Length -ne $expected.bytes) { throw 'Public provider entry mismatch.' }
            $target = Join-Path $root $expected.name
            $source = $entries[0].Open()
            $destination = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $source.CopyTo($destination) } finally { $destination.Dispose(); $source.Dispose() }
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected.sha256) {
                throw 'Public provider checksum mismatch.'
            }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
} finally {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
}

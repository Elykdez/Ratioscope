<#
.SYNOPSIS
    Pack a macOS Ratioscope.app bundle into a release zip from Windows.

.DESCRIPTION
    A plain Windows zip stores no POSIX mode bits, so an extracted .app has a
    non-executable Contents/MacOS binary and macOS reports the app as damaged.
    This packer writes the mode bits itself: every entry carries an explicit
    external-attributes value, and the central directory is stamped with host
    OS 3 (Unix) so macOS honours those bits on extraction.

    Mach-O files get 0755, every other file 0644, directories 0755.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\pack-macos.ps1 -Tag v1.0.1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\pack-macos.ps1 `
        -AppPath D:\Builds\Ratioscope.app -OutputPath D:\out\Ratioscope-macOS-Universal-v1.0.1.zip -Force
#>
[CmdletBinding()]
param(
    [string]$Tag,
    [string]$AppPath,
    [string]$OutputPath,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildDir = if ($env:RELEASE_BUILD_DIR) { $env:RELEASE_BUILD_DIR } else { Join-Path $repoRoot 'Builds\Release' }

if (-not $AppPath) { $AppPath = Join-Path $buildDir 'Ratioscope.app' }
if (-not $OutputPath) {
    if (-not $Tag) { throw 'Supply -Tag (for example v1.0.1) or an explicit -OutputPath.' }
    if ($Tag -notmatch '^v[0-9][0-9A-Za-z.-]*$') { throw "Invalid tag '$Tag'. Use a tag such as v1.0.0." }
    $OutputPath = Join-Path $buildDir "Ratioscope-macOS-Universal-$Tag.zip"
}

$AppPath = [IO.Path]::GetFullPath($AppPath)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)

if (-not (Test-Path -LiteralPath $AppPath -PathType Container)) {
    throw "App bundle not found: $AppPath"
}
$bundleName = Split-Path -Leaf $AppPath
if ([IO.Path]::GetExtension($bundleName) -ne '.app') {
    throw "Expected a .app bundle, got: $bundleName"
}
$mainExecutable = "$bundleName/Contents/MacOS/"
if (-not (Test-Path -LiteralPath (Join-Path $AppPath 'Contents\MacOS') -PathType Container)) {
    throw "Bundle has no Contents/MacOS directory: $AppPath"
}
if (Test-Path -LiteralPath $OutputPath) {
    if (-not $Force) { throw "Output already exists: $OutputPath (pass -Force to overwrite)." }
    Remove-Item -LiteralPath $OutputPath -Force
}

$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and -not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Mach-O magics as they appear on disk: thin 32/64 bit, and fat/universal.
$machOMagics = @(
    @(0xCE, 0xFA, 0xED, 0xFE), # MH_MAGIC     little endian
    @(0xCF, 0xFA, 0xED, 0xFE), # MH_MAGIC_64  little endian
    @(0xFE, 0xED, 0xFA, 0xCE), # MH_MAGIC     big endian
    @(0xFE, 0xED, 0xFA, 0xCF), # MH_MAGIC_64  big endian
    @(0xCA, 0xFE, 0xBA, 0xBE), # FAT_MAGIC
    @(0xCA, 0xFE, 0xBA, 0xBF)  # FAT_MAGIC_64
)

function Test-MachO {
    param([string]$Path)

    $head = New-Object byte[] 4
    $stream = [IO.File]::OpenRead($Path)
    try {
        if ($stream.Read($head, 0, 4) -lt 4) { return $false }
    } finally {
        $stream.Dispose()
    }

    foreach ($magic in $machOMagics) {
        if ($head[0] -eq $magic[0] -and $head[1] -eq $magic[1] -and
            $head[2] -eq $magic[2] -and $head[3] -eq $magic[3]) { return $true }
    }
    return $false
}

function Set-ZipUnixHost {
    <#
        .SYNOPSIS
            Rewrite every central-directory record's "version made by" host byte to
            3 (Unix). Extractors ignore the POSIX mode bits in the external
            attributes unless the host byte says the archive came from Unix.
    #>
    param([string]$Path)

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        $length = $stream.Length
        $window = [int][Math]::Min(65557, $length)
        $stream.Position = $length - $window
        $tail = $reader.ReadBytes($window)

        $eocd = -1
        for ($i = $window - 22; $i -ge 0; $i--) {
            if ($tail[$i] -eq 0x50 -and $tail[$i + 1] -eq 0x4B -and
                $tail[$i + 2] -eq 0x05 -and $tail[$i + 3] -eq 0x06) { $eocd = $i; break }
        }
        if ($eocd -lt 0) { throw "No end-of-central-directory record found in $Path." }

        $entryCount = [BitConverter]::ToUInt16($tail, $eocd + 10)
        $cdSize = [BitConverter]::ToUInt32($tail, $eocd + 12)
        $cdOffset = [BitConverter]::ToUInt32($tail, $eocd + 16)
        if ($entryCount -eq 0xFFFF -or $cdSize -eq 0xFFFFFFFF -or $cdOffset -eq 0xFFFFFFFF) {
            throw 'ZIP64 central directories are not supported by this packer.'
        }

        $stream.Position = $cdOffset
        $centralDirectory = $reader.ReadBytes([int]$cdSize)
        if ($centralDirectory.Length -ne [int]$cdSize) { throw 'Truncated central directory.' }

        $cursor = 0
        for ($n = 0; $n -lt $entryCount; $n++) {
            if ([BitConverter]::ToUInt32($centralDirectory, $cursor) -ne 0x02014B50) {
                throw "Malformed central-directory record $n in $Path."
            }
            $centralDirectory[$cursor + 5] = 3
            $nameLength = [BitConverter]::ToUInt16($centralDirectory, $cursor + 28)
            $extraLength = [BitConverter]::ToUInt16($centralDirectory, $cursor + 30)
            $commentLength = [BitConverter]::ToUInt16($centralDirectory, $cursor + 32)
            $cursor += 46 + $nameLength + $extraLength + $commentLength
        }

        $stream.Position = $cdOffset
        $stream.Write($centralDirectory, 0, $centralDirectory.Length)
        $stream.Flush()
    } finally {
        $stream.Dispose()
    }
}

Write-Host "Packing $AppPath"
Write-Host "     -> $OutputPath"

$prefixLength = $AppPath.Length
$executableCount = 0
$fileCount = 0

$archive = $null
$zipStream = $null
try {
    $zipStream = [IO.File]::Open($OutputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite)
    $archive = New-Object IO.Compression.ZipArchive($zipStream, [IO.Compression.ZipArchiveMode]::Create, $true)

    foreach ($directory in @(Get-Item -LiteralPath $AppPath) + @(Get-ChildItem -LiteralPath $AppPath -Recurse -Force -Directory)) {
        $relative = $bundleName + $directory.FullName.Substring($prefixLength).Replace('\', '/')
        $entry = $archive.CreateEntry("$relative/", [IO.Compression.CompressionLevel]::NoCompression)
        $entry.LastWriteTime = [DateTimeOffset]$directory.LastWriteTime
        # 0040755: directory, rwxr-xr-x
        $entry.ExternalAttributes = 0x41ED -shl 16
    }

    foreach ($file in Get-ChildItem -LiteralPath $AppPath -Recurse -Force -File) {
        $relative = $bundleName + $file.FullName.Substring($prefixLength).Replace('\', '/')

        # Anything in Contents/MacOS is launched directly; other Mach-O files are
        # marked executable too so nested helper bundles keep working.
        $isExecutable = $relative.StartsWith($mainExecutable, [StringComparison]::Ordinal) -or (Test-MachO -Path $file.FullName)

        $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]$file.LastWriteTime
        # 0100755 (rwxr-xr-x) for executables, 0100644 (rw-r--r--) otherwise.
        $entry.ExternalAttributes = $(if ($isExecutable) { 0x81ED } else { 0x81A4 }) -shl 16

        $source = [IO.File]::OpenRead($file.FullName)
        try {
            $target = $entry.Open()
            try { $source.CopyTo($target, 1MB) } finally { $target.Dispose() }
        } finally {
            $source.Dispose()
        }

        $fileCount++
        if ($isExecutable) { $executableCount++ }
    }
} finally {
    if ($archive) { $archive.Dispose() }
    if ($zipStream) { $zipStream.Dispose() }
}

Set-ZipUnixHost -Path $OutputPath

$packed = Get-Item -LiteralPath $OutputPath
Write-Host ''
Write-Host ("Files:      {0} ({1} executable)" -f $fileCount, $executableCount)
Write-Host ("Archive:    {0:N2} MB" -f ($packed.Length / 1MB))
Write-Host ("SHA-256:    {0}" -f (Get-FileHash -Algorithm SHA256 -LiteralPath $OutputPath).Hash.ToLowerInvariant())
Write-Host ''
Write-Host $OutputPath

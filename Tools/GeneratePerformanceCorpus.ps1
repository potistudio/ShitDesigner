[CmdletBinding()]
param(
    [string]$OutputRoot = 'TestResults/PerformanceCorpus/fhd60-v1',
    [double]$DurationSeconds = 1.0,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Resolve-OutputPath {
    param([string]$Path)
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Assert-OutputPathIsExternalCorpus {
    param([string]$Path)
    $full = Resolve-OutputPath $Path
    $streaming = [IO.Path]::GetFullPath((Join-Path $repoRoot 'Assets/StreamingAssets')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($full.StartsWith($streaming, [StringComparison]::OrdinalIgnoreCase) -or
        [String]::Equals($full, $streaming.TrimEnd([IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Performance corpus output must stay outside Assets/StreamingAssets: $full"
    }
    return $full
}

function Find-Tool {
    param([Parameter(Mandatory = $true)][string]$Name)
    $configured = [Environment]::GetEnvironmentVariable($Name.ToUpperInvariant())
    if (-not [String]::IsNullOrWhiteSpace($configured)) {
        if (-not (Test-Path -LiteralPath $configured -PathType Leaf)) { throw "$Name points at a missing executable: $configured" }
        return (Resolve-Path -LiteralPath $configured).Path
    }
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) { throw "$Name is required. Install the pinned local FFmpeg tool or set $($Name.ToUpperInvariant())." }
    return $command.Source
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$File,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    $output = & $File @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')`n$($output -join "`n")" }
    return $output
}

function Get-HashProject {
    $project = Join-Path $repoRoot 'Tools/VideoFixtures/ProbeSmoke.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Missing source-direct XXH3 helper: $project" }
    return $project
}

function Get-Xxh3-128 {
    param([Parameter(Mandatory = $true)][string]$Path)
    $project = Get-HashProject
    $output = & dotnet run --project $project --no-restore -- --hash $Path 2>&1
    if ($LASTEXITCODE -ne 0) { throw "XXH3-128 helper failed for ${Path}: $($output -join "`n")" }
    $hash = ($output | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ -match '^[0-9a-fA-F]{32}$' } | Select-Object -Last 1)
    if ([String]::IsNullOrWhiteSpace($hash)) { throw "XXH3-128 helper returned no 32-digit digest for $Path." }
    return $hash.ToLowerInvariant()
}

function Read-VideoProbe {
    param(
        [Parameter(Mandatory = $true)][string]$Ffprobe,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $json = Invoke-External $Ffprobe @(
        '-v', 'error', '-select_streams', 'v:0',
        '-show_entries', 'stream=codec_name,width,height,r_frame_rate,avg_frame_rate,nb_frames',
        '-of', 'json', $Path
    ) | Out-String
    try { $parsed = $json | ConvertFrom-Json } catch { throw "ffprobe returned invalid JSON for $Path`: $json" }
    $stream = @($parsed.streams) | Select-Object -First 1
    if ($null -eq $stream) { throw "ffprobe found no video stream: $Path" }
    return $stream
}

function Convert-RationalToDouble {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value -match '^([0-9]+)(?:/([0-9]+))?$') {
        $numerator = [double]$Matches[1]
        $denominator = if ($Matches[2]) { [double]$Matches[2] } else { 1.0 }
        if ($denominator -le 0) { throw "Invalid ffprobe frame-rate denominator: $Value" }
        return $numerator / $denominator
    }
    throw "Invalid ffprobe rational value: $Value"
}

function Assert-VideoEntry {
    param(
        [Parameter(Mandatory = $true)]$Entry,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Ffprobe
    )
    if ($null -eq $Entry -or [String]::IsNullOrWhiteSpace([string]$Entry.file)) { throw 'Performance corpus manifest contains an entry without a file.' }
    if ([int]$Entry.width -ne 1920 -or [int]$Entry.height -ne 1080 -or [int]$Entry.fps -ne 60) {
        throw "Performance corpus entry is not FHD 60fps: $($Entry.file)"
    }
    if ([String]$Entry.xxh3_128 -notmatch '^[0-9a-fA-F]{32}$') { throw "Performance corpus entry has an invalid XXH3-128 digest: $($Entry.file)" }
    $path = [IO.Path]::GetFullPath((Join-Path $Root ([string]$Entry.file)))
    $rootWithSeparator = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $path.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) { throw "Performance corpus entry escapes its output root: $($Entry.file)" }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Performance corpus file is missing: $path" }
    $info = Get-Item -LiteralPath $path
    if ([int64]$Entry.bytes -ne $info.Length) { throw "Performance corpus byte count differs: $path" }
    $actualHash = Get-Xxh3-128 $path
    if (-not [String]::Equals($actualHash, [string]$Entry.xxh3_128, [StringComparison]::OrdinalIgnoreCase)) { throw "Performance corpus XXH3-128 differs: $path" }
    $probe = Read-VideoProbe $Ffprobe $path
    if ([string]$probe.codec_name -ne [string]$Entry.codecName) { throw "Codec mismatch for $path`: manifest=$($Entry.codecName), ffprobe=$($probe.codec_name)" }
    if ([int]$probe.width -ne 1920 -or [int]$probe.height -ne 1080) { throw "Resolution mismatch for $path`: $($probe.width)x$($probe.height)" }
    $probeFps = Convert-RationalToDouble ([string]$probe.r_frame_rate)
    if ([Math]::Abs($probeFps - 60.0) -gt 0.0001) { throw "Frame-rate mismatch for $path`: $probeFps" }
    if ($Entry.frameCount -and [string]$probe.nb_frames -match '^[0-9]+$' -and [int64]$probe.nb_frames -ne [int64]$Entry.frameCount) {
        throw "Frame-count mismatch for $path`: manifest=$($Entry.frameCount), ffprobe=$($probe.nb_frames)"
    }
    return $probe
}

$outputPath = Assert-OutputPathIsExternalCorpus $OutputRoot
$ffprobe = Find-Tool 'ffprobe'
$manifestPath = Join-Path $outputPath 'manifest.json'

if ($VerifyOnly) {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Performance corpus manifest is missing: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string]$manifest.version -ne 'fhd60-v1') { throw "Unexpected performance corpus version: $($manifest.version)" }
    $entries = @($manifest.entries)
    foreach ($codec in @('H264', 'Hap')) {
        $entry = $entries | Where-Object { [string]$_.codec -eq $codec } | Select-Object -First 1
        if ($null -eq $entry) { throw "Performance corpus entry is missing for codec $codec." }
        Assert-VideoEntry $entry $outputPath $ffprobe | Out-Null
    }
    Write-Output "Performance corpus verified: $outputPath"
    return
}

if ($DurationSeconds -lt 1.0 -or $DurationSeconds -gt 10.0) { throw 'DurationSeconds must be between 1 and 10 seconds for the short loop corpus.' }
$ffmpeg = Find-Tool 'ffmpeg'
$frameCount = [int][Math]::Round($DurationSeconds * 60.0, [MidpointRounding]::AwayFromZero)
if ($frameCount -lt 60) { throw 'The generated corpus must contain at least one second of 60fps frames.' }

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('ShitDesignerPerformanceCorpus-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null
$h264Temporary = Join-Path $temporaryRoot 'h264_fhd60.mp4'
$hapTemporary = Join-Path $temporaryRoot 'hap_fhd60.mov'
$durationText = $DurationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)
$pattern = "testsrc2=size=1920x1080:rate=60:duration=$durationText"

try {
    $common = @('-hide_banner', '-loglevel', 'error', '-y', '-f', 'lavfi', '-i', $pattern, '-frames:v', $frameCount,
        '-an', '-map_metadata', '-1', '-fflags', '+bitexact', '-threads', '1')
    Invoke-External $ffmpeg ($common + @('-c:v', 'libx264', '-preset', 'medium', '-pix_fmt', 'yuv420p', '-g', '60', '-keyint_min', '60', '-sc_threshold', '0', '-movflags', '+faststart', $h264Temporary)) | Out-Null
    Invoke-External $ffmpeg ($common + @('-vf', 'format=rgba', '-c:v', 'hap', '-format', 'hap', '-chunks', '4', '-compressor', 'snappy', $hapTemporary)) | Out-Null

    $h264Probe = Assert-VideoEntry ([pscustomobject]@{ name = 'FHD60 H.264 performance corpus'; codec = 'H264'; codecName = 'h264'; file = 'h264_fhd60.mp4'; xxh3_128 = (Get-Xxh3-128 $h264Temporary); bytes = (Get-Item $h264Temporary).Length; width = 1920; height = 1080; fps = 60; frameCount = $frameCount }) $temporaryRoot $ffprobe
    $hapProbe = Assert-VideoEntry ([pscustomobject]@{ name = 'FHD60 Hap performance corpus'; codec = 'Hap'; codecName = 'hap'; file = 'hap_fhd60.mov'; xxh3_128 = (Get-Xxh3-128 $hapTemporary); bytes = (Get-Item $hapTemporary).Length; width = 1920; height = 1080; fps = 60; frameCount = $frameCount }) $temporaryRoot $ffprobe
    Copy-Item -LiteralPath $h264Temporary -Destination (Join-Path $outputPath 'h264_fhd60.mp4') -Force
    Copy-Item -LiteralPath $hapTemporary -Destination (Join-Path $outputPath 'hap_fhd60.mov') -Force

    $manifest = [ordered]@{
        version = 'fhd60-v1'
        generator = 'Tools/GeneratePerformanceCorpus.ps1'
        source = 'ffmpeg lavfi testsrc2; self-authored deterministic moving test pattern'
        durationSeconds = $DurationSeconds
        entries = @(
            [ordered]@{ name = 'FHD60 H.264 performance corpus'; codec = 'H264'; codecName = 'h264'; file = 'h264_fhd60.mp4'; xxh3_128 = (Get-Xxh3-128 (Join-Path $outputPath 'h264_fhd60.mp4')); bytes = (Get-Item (Join-Path $outputPath 'h264_fhd60.mp4')).Length; width = 1920; height = 1080; fps = 60; frameCount = $frameCount },
            [ordered]@{ name = 'FHD60 Hap performance corpus'; codec = 'Hap'; codecName = 'hap'; file = 'hap_fhd60.mov'; xxh3_128 = (Get-Xxh3-128 (Join-Path $outputPath 'hap_fhd60.mov')); bytes = (Get-Item (Join-Path $outputPath 'hap_fhd60.mov')).Length; width = 1920; height = 1080; fps = 60; frameCount = $frameCount }
        )
    }
    [IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    & $PSCommandPath -OutputRoot $outputPath -VerifyOnly
    if ($LASTEXITCODE -ne 0) { throw 'Generated corpus failed its VerifyOnly pass.' }
    Write-Output "Generated performance corpus: $outputPath"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}

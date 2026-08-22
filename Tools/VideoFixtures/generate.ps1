[CmdletBinding()]
param(
    [string]$Output = "Assets/ShitDesigner/Tests/Media/Fixtures",
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

function Find-Tool([string]$name) {
    $configured = [Environment]::GetEnvironmentVariable($name.ToUpperInvariant())
    if (-not [string]::IsNullOrWhiteSpace($configured)) {
        if (-not (Test-Path -LiteralPath $configured -PathType Leaf)) {
            throw "$name points at a missing executable: $configured"
        }
        return (Resolve-Path -LiteralPath $configured).Path
    }
    $command = Get-Command $name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "$name is required to generate the video fixtures. Install a pinned, trusted ffmpeg build or set $($name.ToUpperInvariant())."
    }
    return $command.Source
}

function Invoke-Ffmpeg([string]$ffmpeg, [string[]]$arguments) {
    & $ffmpeg @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed with exit code ${LASTEXITCODE}: $($arguments -join ' ')"
    }
}

function Xxh3-128([string]$path) {
    # System.IO.Hashing.XxHash128 is the XXH3-128 algorithm used by the
    # project integrity contract. Keep the calculation in a tiny source-direct
    # helper so PowerShell itself never supplies a different hash algorithm.
    $hashProject = Join-Path $PSScriptRoot "ProbeSmoke.csproj"
    if (-not (Test-Path -LiteralPath $hashProject)) {
        throw "Missing source-direct hash helper: $hashProject"
    }
    $result = & dotnet run --project $hashProject --no-restore -- --hash $path
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($result)) {
        throw "XXH3-128 helper failed for $path"
    }
    return ($result | Select-Object -Last 1).Trim().ToLowerInvariant()
}

function First-FrameRgba([string]$ffmpeg, [string]$path) {
    $raw = "$path.first-frame.rgba"
    try {
        Invoke-Ffmpeg $ffmpeg @(
            "-hide_banner", "-loglevel", "error", "-i", $path,
            "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgba", $raw
        )
        $bytes = [IO.File]::ReadAllBytes($raw)
        if ($bytes.Length -lt 4) { throw "decoded frame is shorter than one RGBA pixel" }
        return ([BitConverter]::ToString($bytes[0..3])).Replace("-", "").ToLowerInvariant()
    }
    finally {
        if (Test-Path -LiteralPath $raw) { Remove-Item -LiteralPath $raw -Force }
    }
}

function Assert-Vp8AlphaBlock([string]$ffprobe, [string]$path) {
    $probe = & $ffprobe -v error -select_streams v:0 -show_packets -show_entries packet=side_data_list -of json $path 2>&1
    if ($LASTEXITCODE -ne 0 -or -not ($probe -join "`n").Contains("Matroska BlockAdditional")) {
        throw "The VP8 alpha fixture does not contain a Matroska BlockAdditional alpha payload: $path"
    }
}

$outputPath = [IO.Path]::GetFullPath($Output)
$ffmpeg = Find-Tool "ffmpeg"
$ffprobe = Find-Tool "ffprobe"
$hashProject = Join-Path $PSScriptRoot "ProbeSmoke.csproj"

if ($VerifyOnly) {
    & dotnet run --project $hashProject --no-restore --verify $outputPath
    if ($LASTEXITCODE -ne 0) { throw "Fixture hash verification failed." }
    Write-Host "Video fixture manifest verified: $outputPath"
    exit 0
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$tempPath = Join-Path ([IO.Path]::GetTempPath()) ("ShitDesignerVideoFixtures-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempPath | Out-Null

try {
    # Every command is explicit and metadata-free. The clips stay short and
    # deterministic, but use 64x64 video: Windows Media Foundation rejects
    # H.264 streams smaller than 48x48 even though the codec itself is valid.
    # Keeping the functional fixture above that platform floor exercises the
    # real Unity VideoPlayer rather than a backend-specific special case.
    $commonVideo = @("-hide_banner", "-loglevel", "error", "-frames:v", "2", "-an", "-map_metadata", "-1", "-fflags", "+bitexact")
    Invoke-Ffmpeg $ffmpeg (@("-y", "-f", "lavfi", "-i", "color=c=red:s=64x64:r=2:d=1") + $commonVideo + @("-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-pix_fmt", "yuv420p", "-g", "1", "-keyint_min", "1", "-sc_threshold", "0", "-movflags", "+faststart", (Join-Path $tempPath "h264.mp4")))
    Invoke-Ffmpeg $ffmpeg (@("-y", "-f", "lavfi", "-i", "color=c=red@0.5:s=64x64:r=2:d=1,format=rgba,format=yuva420p") + $commonVideo + @("-c:v", "libvpx", "-pix_fmt", "yuva420p", "-auto-alt-ref", "0", "-metadata:s:v:0", "alpha_mode=1", (Join-Path $tempPath "vp8-alpha.webm")))
    Assert-Vp8AlphaBlock $ffprobe (Join-Path $tempPath "vp8-alpha.webm")
    Invoke-Ffmpeg $ffmpeg @(
        "-y", "-f", "lavfi", "-i", "color=c=blue:s=64x64:r=2:d=1",
        "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=1",
        "-frames:v", "2", "-map", "0:v:0", "-map", "1:a:0", "-c:v", "libx264",
        "-preset", "ultrafast", "-tune", "zerolatency", "-pix_fmt", "yuv420p",
        "-g", "1", "-keyint_min", "1", "-sc_threshold", "0", "-c:a", "aac",
        "-b:a", "32k", "-ar", "48000", "-movflags", "+faststart", "-map_metadata", "-1",
        "-fflags", "+bitexact", "-shortest", (Join-Path $tempPath "h264-audio.mp4")
    )
    Invoke-Ffmpeg $ffmpeg (@("-y", "-f", "lavfi", "-i", "color=c=green:s=64x64:r=2:d=1") + $commonVideo + @("-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "30", "-pix_fmt", "yuv420p", (Join-Path $tempPath "unsupported-vp9.webm")))

    $h264Bytes = [IO.File]::ReadAllBytes((Join-Path $tempPath "h264.mp4"))
    [IO.File]::WriteAllBytes((Join-Path $tempPath "malformed-h264-truncated.mp4"), $h264Bytes[0..([Math]::Min(127, $h264Bytes.Length - 1))])

    foreach ($name in @("h264.mp4", "vp8-alpha.webm", "h264-audio.mp4", "unsupported-vp9.webm", "malformed-h264-truncated.mp4")) {
        Copy-Item -LiteralPath (Join-Path $tempPath $name) -Destination (Join-Path $outputPath $name) -Force
    }

    $ffmpegVersion = (& $ffmpeg -version | Select-Object -First 1).Trim()
    # Keep the checked-in native Hap corpus in this manifest as well. The
    # Hap generator and this script are intentionally independent, so running
    # either generator must not erase the other backend's integrity records.
    $hapEntries = @(
        [ordered]@{ file = "hap1.mov"; container = "Mov"; codec = "Hap1"; width = 4; height = 4; fps = 60; durationSeconds = 0.033333; frameCount = 2; hasAlpha = $false; hasAudio = $false; expectedFirstFrameRgba8 = "ff0000ff"; xxh3_128 = "d09529d3a8c9545140885f8e935c911a"; bytes = 354; probe = "Supported" }
        [ordered]@{ file = "hap5.mov"; container = "Mov"; codec = "Hap5"; width = 4; height = 4; fps = 60; durationSeconds = 0.033333; frameCount = 2; hasAlpha = $true; hasAudio = $false; expectedFirstFrameRgba8 = "ff0000ff"; xxh3_128 = "3fccea6f67d908c0ce918bbcc7c17567"; bytes = 370; probe = "Supported" }
        [ordered]@{ file = "hapy.mov"; container = "Mov"; codec = "HapY"; width = 4; height = 4; fps = 60; durationSeconds = 0.033333; frameCount = 2; hasAlpha = $false; hasAudio = $false; expectedFirstFrameRgba8 = "ff46ffff"; xxh3_128 = "99f543017ce7b28f17873ba805aa54d7"; bytes = 370; probe = "Supported" }
        [ordered]@{ file = "hapm.mov"; container = "Mov"; codec = "HapM"; width = 4; height = 4; fps = 60; durationSeconds = 0.033333; frameCount = 2; hasAlpha = $true; hasAudio = $false; expectedFirstFrameRgba8 = "ff46ffff"; xxh3_128 = "23514452f8a28a2659adc5413c124b30"; bytes = 402; probe = "Supported" }
        [ordered]@{ file = "hap1-snappy-multichunk.mov"; container = "Mov"; codec = "Hap1"; width = 4; height = 4; fps = 60; durationSeconds = 0.033333; frameCount = 2; hasAlpha = $false; hasAudio = $false; expectedFirstFrameRgba8 = "ff0000ff"; xxh3_128 = "73dfc5781c3c39f611541fd1eff6bc1b"; bytes = 406; probe = "Supported" }
    )
    $entries = @($hapEntries) + @(
        [ordered]@{ file = "h264.mp4"; container = "Mp4"; codec = "H264"; width = 64; height = 64; fps = 2; durationSeconds = 1.0; frameCount = 2; hasAlpha = $false; hasAudio = $false; expectedFirstFrameRgba8 = (First-FrameRgba $ffmpeg (Join-Path $outputPath "h264.mp4")); xxh3_128 = (Xxh3-128 (Join-Path $outputPath "h264.mp4")); bytes = (Get-Item (Join-Path $outputPath "h264.mp4")).Length; probe = "Supported" }
        [ordered]@{ file = "vp8-alpha.webm"; container = "WebM"; codec = "VP8"; width = 64; height = 64; fps = 2; durationSeconds = 1.0; frameCount = 2; hasAlpha = $true; alphaEvidence = "Matroska BlockAdditional id 1"; hasAudio = $false; expectedFirstFrameRgba8 = (First-FrameRgba $ffmpeg (Join-Path $outputPath "vp8-alpha.webm")); xxh3_128 = (Xxh3-128 (Join-Path $outputPath "vp8-alpha.webm")); bytes = (Get-Item (Join-Path $outputPath "vp8-alpha.webm")).Length; probe = "Supported" }
        [ordered]@{ file = "h264-audio.mp4"; container = "Mp4"; codec = "H264"; width = 64; height = 64; fps = 2; durationSeconds = 1.0; frameCount = 2; hasAlpha = $false; hasAudio = $true; expectedFirstFrameRgba8 = (First-FrameRgba $ffmpeg (Join-Path $outputPath "h264-audio.mp4")); xxh3_128 = (Xxh3-128 (Join-Path $outputPath "h264-audio.mp4")); bytes = (Get-Item (Join-Path $outputPath "h264-audio.mp4")).Length; probe = "Supported"; audioPolicy = "IgnoredByUnityVideoBackend" }
        [ordered]@{ file = "unsupported-vp9.webm"; container = "WebM"; codec = "VP9"; width = 64; height = 64; fps = 2; durationSeconds = 1.0; frameCount = 2; hasAlpha = $false; hasAudio = $false; expectedFirstFrameRgba8 = (First-FrameRgba $ffmpeg (Join-Path $outputPath "unsupported-vp9.webm")); xxh3_128 = (Xxh3-128 (Join-Path $outputPath "unsupported-vp9.webm")); bytes = (Get-Item (Join-Path $outputPath "unsupported-vp9.webm")).Length; probe = "Unsupported" }
        [ordered]@{ file = "malformed-h264-truncated.mp4"; container = "Mp4"; codec = "H264"; width = 4; height = 4; fps = 2; durationSeconds = 1.0; frameCount = 2; hasAlpha = $false; hasAudio = $false; expectedFirstFrameRgba8 = $null; xxh3_128 = (Xxh3-128 (Join-Path $outputPath "malformed-h264-truncated.mp4")); bytes = (Get-Item (Join-Path $outputPath "malformed-h264-truncated.mp4")).Length; probe = "Malformed" }
    )
    foreach ($entry in $entries) { $entry.expectedFrame = $entry.expectedFirstFrameRgba8 }

    $invalidHash = [ordered]@{ generator = "Tools/VideoFixtures/generate.ps1"; reason = "negative test: expected hash deliberately differs from h264.mp4"; fixture = "h264.mp4"; xxh3_128 = "00000000000000000000000000000000"; bytes = (Get-Item (Join-Path $outputPath "h264.mp4")).Length }
    $manifest = [ordered]@{
        version = 1
        generator = "Tools/VideoFixtures/generate.ps1"
        license = "self-authored deterministic 4x4 test patterns; ffmpeg is a build-time tool dependency, no external media is redistributed"
        ffmpeg = $ffmpegVersion
        commands = [ordered]@{ h264 = "lavfi color red -> libx264 yuv420p"; vp8Alpha = "lavfi RGBA red@0.5 -> libvpx yuva420p alpha_mode=1"; audio = "lavfi color blue + sine 440Hz -> H.264 + AAC (audio ignored by runtime)"; unsupported = "lavfi color green -> libvpx-vp9" }
        fixtures = $entries
        negativeManifests = @("manifest-invalid-hash.json")
    }
    [IO.File]::WriteAllText((Join-Path $outputPath "manifest.json"), (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine))
    [IO.File]::WriteAllText((Join-Path $outputPath "manifest-invalid-hash.json"), (($invalidHash | ConvertTo-Json -Depth 8) + [Environment]::NewLine))
    Write-Host "Generated deterministic video fixtures in $outputPath"
}
finally {
    if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Recurse -Force }
}

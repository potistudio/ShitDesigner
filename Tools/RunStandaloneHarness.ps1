param(
    [ValidateSet('h264', 'hap')]
    [string]$Codec = 'h264',
    [ValidateSet('d3d12', 'vulkan', 'metal')]
    [string]$GraphicsApi = 'd3d12',
    [string]$Platform = '',
    [string]$BuildPath = '',
    [string]$CorpusRoot = '',
    [string]$ArtifactDirectory = '',
    [string]$UnityPath = '',
    [switch]$FixtureMode,
    [double]$WarmupSeconds = -1,
    [double]$MeasureSeconds = -1,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'StandaloneHarnessProcess.ps1')
. (Join-Path $PSScriptRoot 'UnityProcessEnvironment.ps1')
Ensure-UnityProcessEnvironment

function Resolve-ProjectPath {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ([IO.Path]::IsPathRooted($Value)) {
        return [IO.Path]::GetFullPath($Value)
    }
    return [IO.Path]::GetFullPath((Join-Path $project $Value))
}

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Invoke-Unity {
    param(
        [Parameter(Mandatory = $true)][string[]]$UnityArguments,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    Ensure-Directory (Split-Path -Parent $LogPath)
    $arguments = @(
        '-batchmode',
        '-nographics',
        '-quit',
        '-projectPath', $project,
        '-buildTarget', $buildTarget
    ) + $UnityArguments + @('-logFile', $LogPath)
    return Invoke-RootProcess -FilePath $unity -Arguments $arguments
}

function Get-PlayerGraphicsArguments {
    if ($Platform -eq 'windows') {
        if ($GraphicsApi -eq 'd3d12') { return @('-force-d3d12') }
        if ($GraphicsApi -eq 'vulkan') { return @('-force-vulkan') }
    }
    elseif ($Platform -eq 'macos' -and $GraphicsApi -eq 'metal') {
        return @('-force-metal')
    }

    throw "Graphics API '$GraphicsApi' is not valid for platform '$Platform'."
}

function Get-PlayerExecutablePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ($Platform -eq 'macos' -and $Path.EndsWith('.app', [StringComparison]::OrdinalIgnoreCase)) {
        $appName = [IO.Path]::GetFileNameWithoutExtension($Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
        return Join-Path (Join-Path $Path 'Contents/MacOS') $appName
    }
    return $Path
}

. (Join-Path $PSScriptRoot 'StandaloneHarnessArtifactValidation.ps1')

$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if ([string]::IsNullOrWhiteSpace($Platform)) {
    $Platform = if ($env:OS -eq 'Windows_NT') { 'windows' } else { 'macos' }
}
if ($Platform -notin @('windows', 'macos')) {
    throw "Unsupported platform '$Platform'. Use windows or macos."
}
if ($Platform -eq 'windows' -and $GraphicsApi -eq 'metal') {
    throw 'Metal is only valid for macos.'
}
if ($Platform -eq 'macos' -and $GraphicsApi -ne 'metal') {
    throw "macos requires GraphicsApi metal, not '$GraphicsApi'."
}

$buildTarget = if ($Platform -eq 'windows') { 'StandaloneWindows64' } else { 'StandaloneOSX' }
if ([string]::IsNullOrWhiteSpace($BuildPath)) {
    $BuildPath = if ($Platform -eq 'windows') {
        Join-Path $project 'Builds/ShitDesignerHarness/StandaloneWindows64/ShitDesignerHarness.exe'
    }
    else {
        Join-Path $project 'Builds/ShitDesignerHarness/StandaloneOSX/ShitDesignerHarness.app'
    }
}
else {
    $BuildPath = Resolve-ProjectPath $BuildPath
}
if ($CorpusRoot) { $CorpusRoot = Resolve-ProjectPath $CorpusRoot }
if ($ArtifactDirectory) { $ArtifactDirectory = Resolve-ProjectPath $ArtifactDirectory }
$hasWarmupOverride = $PSBoundParameters.ContainsKey('WarmupSeconds')
$hasMeasureOverride = $PSBoundParameters.ContainsKey('MeasureSeconds')
if (($hasWarmupOverride -or $hasMeasureOverride) -and -not $FixtureMode) {
    throw 'WarmupSeconds and MeasureSeconds are fixture-only overrides. Production Performance runs are always warmup=30 and measure=600.'
}
if ($hasWarmupOverride -and ($WarmupSeconds -lt 0 -or [double]::IsNaN($WarmupSeconds) -or [double]::IsInfinity($WarmupSeconds))) { throw 'WarmupSeconds must be finite and non-negative.' }
if ($hasMeasureOverride -and ($MeasureSeconds -lt 0 -or [double]::IsNaN($MeasureSeconds) -or [double]::IsInfinity($MeasureSeconds))) { throw 'MeasureSeconds must be finite and non-negative.' }
if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $UnityPath = if ($Platform -eq 'macos') {
        '/Applications/Unity/Hub/Editor/6000.5.9f1/Unity.app/Contents/MacOS/Unity'
    }
    else {
        'C:/Program Files/Unity Editor/6000.5.9f1/Editor/Unity.exe'
    }
}
$unity = $UnityPath

$testResults = Join-Path $project 'TestResults'
$snapshotPath = $null
$snapshotReady = $false
$exitCode = 1
$failureMessage = $null
$cleanupFailures = @()
$harnessDefineNeedsCleanup = $false

try {
    # Keep snapshot creation inside the same cleanup scope as the build. A
    # partially created temp file is removed even when the copy itself fails.
    $projectSettingsPath = Join-Path $project 'ProjectSettings/ProjectSettings.asset'
    $snapshotPath = [IO.Path]::GetTempFileName()
    [IO.File]::Copy($projectSettingsPath, $snapshotPath, $true)
    $snapshotReady = $true
    Ensure-Directory $testResults

    if ($Build) {
        # The Windows artifact is built once with D3D12 first and Vulkan second.
        # GraphicsApi selects the runtime validation mode, never the build path.
        $harnessDefineNeedsCleanup = $true
        $enableCode = Invoke-Unity -UnityArguments @(
            '-executeMethod', 'ShitDesigner.Editor.StandaloneHarnessBuild.EnableHarnessDefine'
        ) -LogPath (Join-Path $testResults 'harness-enable.log')
        if ($enableCode -ne 0) { throw "EnableHarnessDefine failed with exit code $enableCode." }

        $buildCode = Invoke-Unity -UnityArguments @(
            '-executeMethod', 'ShitDesigner.Editor.StandaloneHarnessBuild.BuildStandalonePerformanceHarness',
            '-sdHarnessBuildTarget', $buildTarget,
            '-sdHarnessBuildOutput', $BuildPath
        ) -LogPath (Join-Path $testResults ("harness-build-{0}.log" -f $Platform))
        if ($buildCode -ne 0) { throw "BuildStandaloneHarness failed with exit code $buildCode." }

        $disableCode = Invoke-Unity -UnityArguments @(
            '-executeMethod', 'ShitDesigner.Editor.StandaloneHarnessBuild.DisableHarnessDefine'
        ) -LogPath (Join-Path $testResults 'harness-disable.log')
        if ($disableCode -ne 0) { throw "DisableHarnessDefine failed with exit code $disableCode." }
        $harnessDefineNeedsCleanup = $false
    }

    if (-not (Test-Path -LiteralPath $BuildPath)) {
        throw "Harness Player not found: $BuildPath"
    }
    $playerPath = Get-PlayerExecutablePath $BuildPath
    if (-not (Test-Path -LiteralPath $playerPath)) {
        throw "Harness Player executable not found: $playerPath"
    }

    $artifactDirectoryWasExplicit = -not [string]::IsNullOrWhiteSpace($ArtifactDirectory)
    if (-not $artifactDirectoryWasExplicit) {
        $runDirectoryId = 'run-' + [Guid]::NewGuid().ToString('N')
        $ArtifactDirectory = Join-Path $project (Join-Path 'TestResults/StandaloneHarness' $runDirectoryId)
    }
    Ensure-Directory $ArtifactDirectory
    $runStartedUtc = [DateTime]::UtcNow
    $playerLogPath = Join-Path $ArtifactDirectory 'player.log'

    # Performance acceptance needs a real Player presentation boundary. Unity
    # documents -batchmode as headless/no-display, so it cannot supply the
    # CPU/GPU FrameTiming samples used by this non-Profiler measurement. On
    # Windows, start this Player normally on the visible desktop: Program
    # Presented Frame timing is a displayed-Player measurement, not a hidden
    # console or Editor batch-process measurement.
    $playerArguments = @() + (Get-PlayerGraphicsArguments) + @('-sdHarnessCodec', $Codec, '-sdHarnessArtifactDir', $ArtifactDirectory, '-logFile', $playerLogPath)
    if ($CorpusRoot) { $playerArguments += @('-sdHarnessCorpusRoot', $CorpusRoot) }
    if ($FixtureMode) {
        # Fixture mode is an integration diagnostic, not a shorter production
        # claim. Explicitly retain the production 30 second readiness window
        # while keeping its measured interval short.
        $fixtureWarmupSeconds = if ($hasWarmupOverride) { $WarmupSeconds } else { 30d }
        $fixtureMeasureSeconds = if ($hasMeasureOverride) { $MeasureSeconds } else { 2d }
        $playerArguments += @('-sdHarnessFixtureMode', '-sdHarnessWarmupSeconds', $fixtureWarmupSeconds.ToString([Globalization.CultureInfo]::InvariantCulture), '-sdHarnessMeasureSeconds', $fixtureMeasureSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
    }

    $playerExitCode = Invoke-RootProcess -FilePath $playerPath -Arguments $playerArguments -WindowStyle Normal
    $validationArguments = @{
        Directory = $ArtifactDirectory
        RunStartedUtc = $runStartedUtc
        PlayerExitCode = $playerExitCode
        ExpectedPlatform = $Platform
        ExpectedGraphicsApi = $GraphicsApi
        ExpectedCodec = $Codec
        ExpectedMode = 'performance'
        ExpectedFixtureMode = [bool]$FixtureMode
        ApplicationLogPath = $playerLogPath
    }
    if (-not $FixtureMode) {
        $artifactValidation = Validate-HarnessArtifacts @validationArguments -RequireProductionRun
    }
    else {
        $artifactValidation = Validate-HarnessArtifacts @validationArguments
    }
    if (-not $artifactValidation.IsValid) {
        $exitCode = 1
        $failureMessage = $artifactValidation.Error
    }
    else {
        $exitCode = $playerExitCode
    }
}
catch {
    $exitCode = 1
    $failureMessage = $_.Exception.Message
}
finally {
    if ($harnessDefineNeedsCleanup) {
        try {
            $recoveryCode = Invoke-Unity -UnityArguments @(
                '-executeMethod', 'ShitDesigner.Editor.StandaloneHarnessBuild.DisableHarnessDefine'
            ) -LogPath (Join-Path $testResults 'harness-disable-recovery.log')
            if ($recoveryCode -ne 0) {
                $cleanupFailures += "DisableHarnessDefine recovery failed with exit code $recoveryCode."
            }
        }
        catch {
            $cleanupFailures += "DisableHarnessDefine recovery failed: $($_.Exception.Message)"
        }
    }

    if ($snapshotReady) {
        try {
            # Restore the exact ProjectSettings bytes even when Unity compilation
            # or the build itself failed. This also removes graphics settings.
            [IO.File]::Copy($snapshotPath, $projectSettingsPath, $true)
        }
        catch {
            $cleanupFailures += "ProjectSettings restore failed: $($_.Exception.Message)"
        }
    }
    if ($snapshotPath) {
        try {
            if (Test-Path -LiteralPath $snapshotPath) { Remove-Item -LiteralPath $snapshotPath -Force }
        }
        catch {
            $cleanupFailures += "Snapshot cleanup failed: $($_.Exception.Message)"
        }
    }
}

if ($cleanupFailures.Count -ne 0) {
    $cleanupMessage = 'Cleanup failures: ' + ($cleanupFailures -join ' | ')
    if ($failureMessage) { $failureMessage = "$failureMessage $cleanupMessage" } else { $failureMessage = $cleanupMessage }
    # A cleanup failure invalidates an otherwise successful or environment run.
    $exitCode = 1
}
if ($failureMessage) { [Console]::Error.WriteLine($failureMessage) }
exit $exitCode

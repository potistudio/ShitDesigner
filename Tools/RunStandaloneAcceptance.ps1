param(
    [ValidateSet('d3d12', 'vulkan', 'metal')]
    [string]$GraphicsApi = 'd3d12',
    [ValidateSet('windows', 'macos')]
    [string]$Platform = 'windows',
    [Parameter(Mandatory = $true)][string]$FixtureRoot,
    [string]$BuildPath = '',
    [string]$ArtifactRoot = '',
    [string]$UnityPath = '',
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'StandaloneHarnessProcess.ps1')
. (Join-Path $PSScriptRoot 'StandaloneHarnessArtifactValidation.ps1')
. (Join-Path $PSScriptRoot 'UnityProcessEnvironment.ps1')
Ensure-UnityProcessEnvironment

$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
function Resolve-ProjectPath {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path $project $Value))
}
function Test-ContainedPath {
    param([Parameter(Mandatory = $true)][string]$Parent, [Parameter(Mandatory = $true)][string]$Child)
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $childFull = [IO.Path]::GetFullPath($Child).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $childFull.StartsWith($parentFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
function Ensure-Directory { param([Parameter(Mandatory = $true)][string]$Path) if (-not (Test-Path -LiteralPath $Path -PathType Container)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null } }
function Invoke-EditorUnity {
    param([Parameter(Mandatory = $true)][string[]]$Arguments, [Parameter(Mandatory = $true)][string]$LogPath)
    Ensure-Directory (Split-Path -Parent $LogPath)
    $all = @('-batchmode', '-nographics', '-quit', '-projectPath', $project, '-buildTarget', $buildTarget) + $Arguments + @('-logFile', $LogPath)
    return Invoke-RootProcess -FilePath $unity -Arguments $all
}
function Get-PlayerPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ($Platform -eq 'macos' -and $Path.EndsWith('.app', [StringComparison]::OrdinalIgnoreCase)) {
        $name = [IO.Path]::GetFileNameWithoutExtension($Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
        return Join-Path (Join-Path $Path 'Contents/MacOS') $name
    }
    return $Path
}
function Get-ApiArgs {
    if ($Platform -eq 'windows' -and $GraphicsApi -eq 'd3d12') { return @('-force-d3d12') }
    if ($Platform -eq 'windows' -and $GraphicsApi -eq 'vulkan') { return @('-force-vulkan') }
    if ($Platform -eq 'macos' -and $GraphicsApi -eq 'metal') { return @('-force-metal') }
    throw "Graphics API '$GraphicsApi' is not valid for platform '$Platform'."
}
function Invoke-PlayerStage {
    param([Parameter(Mandatory = $true)][string]$Stage, [Parameter(Mandatory = $true)][string]$StageDirectory, [string]$ProjectRoot = '', [string]$Fingerprint = '', [string]$BackupFingerprint = '')
    Ensure-Directory $StageDirectory
    $playerLog = Join-Path $StageDirectory 'Player.log'
    $args = @('-batchmode') + (Get-ApiArgs) + @('-logFile', $playerLog, '-sdHarnessMode', 'acceptance', '-sdHarnessStage', $Stage, '-sdHarnessFixtureRoot', $FixtureRoot, '-sdHarnessArtifactDir', $StageDirectory)
    if ($ProjectRoot) { $args += @('-sdHarnessProjectRoot', $ProjectRoot) }
    if ($Fingerprint) { $args += @('-sdHarnessExpectedFingerprint', $Fingerprint) }
    if ($BackupFingerprint) { $args += @('-sdHarnessExpectedBackupFingerprint', $BackupFingerprint) }
    $started = [DateTime]::UtcNow
    $code = Invoke-RootProcess -FilePath $player -Arguments $args
    $script:RunStandaloneAcceptanceLastPlayerExitCode = $code
    $validation = Validate-HarnessArtifacts -Directory $StageDirectory -RunStartedUtc $started -PlayerExitCode $code -ApplicationLogPath $playerLog
    if (-not $validation.IsValid) { throw "Acceptance $Stage artifact validation failed: $($validation.Error)" }
    $jsonFile = @(Get-ChildItem -LiteralPath $StageDirectory -Filter '*.json' -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1)
    if ($jsonFile.Count -ne 1) { throw "Acceptance $Stage artifact JSON was not found." }
    $json = ([IO.File]::ReadAllText($jsonFile[0].FullName) | ConvertFrom-Json)
    $expectedPlatform = if ($Platform -eq 'windows') { 'WindowsPlayer' } else { 'OSXPlayer' }
    $expectedGraphicsApi = if ($GraphicsApi -eq 'd3d12') { 'Direct3D12' } elseif ($GraphicsApi -eq 'vulkan') { 'Vulkan' } else { 'Metal' }
    if ([string]$json.mode -ne 'acceptance' -or [string]$json.stage -ne $Stage -or [string]$json.platform -ne $expectedPlatform -or [string]$json.graphicsApi -ne $expectedGraphicsApi -or [string]$json.acceptance.stage -ne $Stage -or [string]$json.acceptance.graphicsApi -ne $expectedGraphicsApi -or [bool]$json.developmentBuild -or [string]$json.buildOptions -ne 'None') {
        throw "Acceptance $Stage artifact does not match requested mode/stage/platform/graphics API: mode=$($json.mode), stage=$($json.stage), platform=$($json.platform), graphicsApi=$($json.graphicsApi)."
    }
    return [pscustomobject]@{ ExitCode = $code; Json = $json }
}

$FixtureRoot = Resolve-ProjectPath $FixtureRoot
$sourceFixtureRoot = $FixtureRoot
$buildTarget = if ($Platform -eq 'windows') { 'StandaloneWindows64' } else { 'StandaloneOSX' }
if (-not $BuildPath) { $BuildPath = if ($Platform -eq 'windows') { Join-Path $project 'Builds/ShitDesignerAcceptance/StandaloneWindows64/ShitDesignerAcceptance.exe' } else { Join-Path $project 'Builds/ShitDesignerAcceptance/StandaloneOSX/ShitDesignerAcceptance.app' } } else { $BuildPath = Resolve-ProjectPath $BuildPath }
if (-not $ArtifactRoot) { $ArtifactRoot = Join-Path $project ('TestResults/StandaloneAcceptance/run-' + [Guid]::NewGuid().ToString('N')) } else { $ArtifactRoot = Resolve-ProjectPath $ArtifactRoot }
Ensure-Directory $ArtifactRoot
if (-not $UnityPath) { $UnityPath = if ($Platform -eq 'windows') { 'C:/Program Files/Unity Editor/6000.5.9f1/Editor/Unity.exe' } else { '/Applications/Unity/Hub/Editor/6000.5.9f1/Unity.app/Contents/MacOS/Unity' } }
$unity = $UnityPath
$player = Get-PlayerPath $BuildPath
$settings = Join-Path $project 'ProjectSettings/ProjectSettings.asset'
$snapshot = $null
$snapshotReady = $false
$defineNeedsCleanup = $false
$exitCode = 1
$failureMessage = $null
$cleanupFailures = @()
$runFixtureRoot = $null
$script:RunStandaloneAcceptanceLastPlayerExitCode = $null
try {
    $snapshot = [IO.Path]::GetTempFileName()
    [IO.File]::Copy($settings, $snapshot, $true)
    $snapshotReady = $true
    if ($Build) {
        $defineNeedsCleanup = $true
        $enabled = Invoke-EditorUnity -Arguments @('-executeMethod', 'ShitDesigner.Editor.StandaloneHarnessBuild.EnableHarnessDefine') -LogPath (Join-Path $ArtifactRoot 'enable.log')
        if ($enabled -ne 0) { throw "EnableHarnessDefine failed with exit code $enabled." }
        $built = Invoke-EditorUnity -Arguments @('-executeMethod', 'ShitDesigner.Editor.StandaloneHarnessBuild.BuildStandaloneAcceptanceHarness', '-sdHarnessBuildTarget', $buildTarget, '-sdHarnessBuildOutput', $BuildPath) -LogPath (Join-Path $ArtifactRoot 'build.log')
        if ($built -ne 0) { throw "BuildStandaloneAcceptanceHarness failed with exit code $built." }
        $disabled = Invoke-EditorUnity -Arguments @('-executeMethod', 'ShitDesigner.Editor.StandaloneHarnessBuild.DisableHarnessDefine') -LogPath (Join-Path $ArtifactRoot 'disable.log')
        if ($disabled -ne 0) { throw "DisableHarnessDefine failed with exit code $disabled." }
        $defineNeedsCleanup = $false
    }
    if (-not (Test-Path -LiteralPath $player -PathType Leaf)) { throw "Acceptance Player executable is missing: $player" }

    # The Player must never import from the workspace fixture directory.  A
    # run-local copy makes the initial import self-contained, and removing it
    # before Reopen proves the saved project owns its media files.
    $runFixtureRoot = Join-Path $ArtifactRoot 'source-fixtures'
    if (-not (Test-ContainedPath ([IO.Path]::GetFullPath($ArtifactRoot)) ([IO.Path]::GetFullPath($runFixtureRoot)))) { throw "Run fixture copy escaped the artifact root: $runFixtureRoot" }
    if (Test-Path -LiteralPath $runFixtureRoot) { throw "Run fixture copy already exists: $runFixtureRoot" }
    Copy-Item -LiteralPath $sourceFixtureRoot -Destination $runFixtureRoot -Recurse -Force
    if (-not (Test-Path -LiteralPath $runFixtureRoot -PathType Container)) { throw "Run fixture copy was not created: $runFixtureRoot" }
    $FixtureRoot = [IO.Path]::GetFullPath($runFixtureRoot)
    $initial = Invoke-PlayerStage 'Initial' (Join-Path $ArtifactRoot 'initial')
    if ($initial.ExitCode -ne 0) { $exitCode = $initial.ExitCode; throw "Acceptance initial stage returned $($initial.ExitCode)." }
    $projectRoot = [string]$initial.Json.acceptance.persistence.projectRoot
    $fingerprint = [string]$initial.Json.acceptance.persistence.fingerprint
    $backupFingerprint = [string]$initial.Json.acceptance.persistence.backupFingerprint
    if (-not $projectRoot -or -not $fingerprint -or -not $backupFingerprint) { throw 'Initial acceptance artifact did not provide projectRoot, fingerprint, and backupFingerprint.' }
    $portableProjectRoot = Join-Path $ArtifactRoot 'portable-project'
    $artifactFull = [IO.Path]::GetFullPath($ArtifactRoot)
    $projectFull = [IO.Path]::GetFullPath($projectRoot)
    $portableProjectFull = [IO.Path]::GetFullPath($portableProjectRoot)
    if (-not (Test-ContainedPath $artifactFull $portableProjectFull)) { throw "Portable project target is outside the artifact root: $portableProjectFull" }
    if (-not (Test-Path -LiteralPath $projectFull -PathType Container)) { throw "Acceptance project root is not a directory: $projectFull" }
    if (Test-Path -LiteralPath $portableProjectFull) { throw "Portable project target already exists: $portableProjectFull" }
    Copy-Item -LiteralPath $projectFull -Destination $portableProjectFull -Recurse -Force
    if (-not (Test-Path -LiteralPath $portableProjectFull -PathType Container)) { throw "Portable project copy was not created: $portableProjectFull" }

    # Keep Recovery's input separate before Reopen saves the portable copy.
    # Initial deliberately seeds a distinct one-generation .bak; corrupting
    # this independent copy later proves that Recovery loads that exact file.
    $recoveryRoot = Join-Path $ArtifactRoot 'recovery-project'
    $recoveryFull = [IO.Path]::GetFullPath($recoveryRoot)
    if (-not (Test-ContainedPath $artifactFull $recoveryFull)) { throw "Recovery target is outside the run artifact root: $recoveryFull" }
    if (Test-Path -LiteralPath $recoveryFull) { throw "Recovery target already exists: $recoveryFull" }
    Copy-Item -LiteralPath $portableProjectFull -Destination $recoveryFull -Recurse -Force
    $recoveryMain = Join-Path $recoveryFull 'project.json'
    $recoveryBackup = Join-Path $recoveryFull 'project.json.bak'
    if (-not (Test-Path -LiteralPath $recoveryMain -PathType Leaf) -or -not (Test-Path -LiteralPath $recoveryBackup -PathType Leaf)) { throw 'Recovery copy does not contain exact project.json and project.json.bak targets.' }

    Remove-Item -LiteralPath $runFixtureRoot -Recurse -Force
    if (Test-Path -LiteralPath $runFixtureRoot) { throw "Run fixture copy could not be removed before Reopen: $runFixtureRoot" }
    $reopen = Invoke-PlayerStage 'Reopen' (Join-Path $ArtifactRoot 'reopen') $portableProjectFull $fingerprint
    if ($reopen.ExitCode -ne 0) { $exitCode = $reopen.ExitCode; throw "Acceptance reopen stage returned $($reopen.ExitCode)." }
    [IO.File]::WriteAllText($recoveryMain, '{"broken":true}', [System.Text.UTF8Encoding]::new($false))
    $recovery = Invoke-PlayerStage 'Recovery' (Join-Path $ArtifactRoot 'recovery') $recoveryFull $fingerprint $backupFingerprint
    $exitCode = $recovery.ExitCode
    if ($exitCode -eq 0) { $exitCode = 0 }
}
catch {
    $failureMessage = $_.Exception.Message
    if ($script:RunStandaloneAcceptanceLastPlayerExitCode -eq 2) { $exitCode = 2 }
    if ($exitCode -eq 1 -and $_.Exception.Message -match 'environment|missing|fixture|native') { $exitCode = 2 }
}
finally {
    if ($runFixtureRoot -and (Test-Path -LiteralPath $runFixtureRoot)) {
        try { Remove-Item -LiteralPath $runFixtureRoot -Recurse -Force } catch { $cleanupFailures += "Run fixture copy cleanup failed: $($_.Exception.Message)" }
    }
    if ($defineNeedsCleanup) {
        try {
            $disabledRecovery = Invoke-EditorUnity -Arguments @('-executeMethod', 'ShitDesigner.Editor.StandaloneHarnessBuild.DisableHarnessDefine') -LogPath (Join-Path $ArtifactRoot 'disable-recovery.log')
            if ($disabledRecovery -ne 0) { $cleanupFailures += "DisableHarnessDefine recovery failed with exit code $disabledRecovery." }
        } catch { $cleanupFailures += "DisableHarnessDefine recovery failed: $($_.Exception.Message)" }
    }
    if ($snapshotReady) { try { [IO.File]::Copy($snapshot, $settings, $true) } catch { $cleanupFailures += "ProjectSettings restore failed: $($_.Exception.Message)" } }
    if ($snapshot) { try { if (Test-Path -LiteralPath $snapshot) { Remove-Item -LiteralPath $snapshot -Force } } catch { $cleanupFailures += "ProjectSettings snapshot cleanup failed: $($_.Exception.Message)" } }
}
if ($failureMessage) { [Console]::Error.WriteLine($failureMessage) }
if ($cleanupFailures.Count -gt 0) {
    foreach ($cleanupFailure in $cleanupFailures) { [Console]::Error.WriteLine($cleanupFailure) }
    if ($exitCode -eq 0) { $exitCode = 1 }
}
exit $exitCode

param([string]$TestResults = "TestResults/harness-contract.xml")

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'UnityProcessEnvironment.ps1')
Ensure-UnityProcessEnvironment
$unity = [IO.Path]::GetFullPath("C:/Program Files/Unity Editor/6000.5.9f1/Editor/Unity.exe")
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not (Test-Path -LiteralPath $unity -PathType Leaf)) {
  throw "Unity executable was not found: $unity"
}

$resultPath = if ([IO.Path]::IsPathRooted($TestResults)) {
  [IO.Path]::GetFullPath($TestResults)
} else {
  [IO.Path]::GetFullPath((Join-Path $project $TestResults))
}
$resultDirectory = Split-Path -Parent $resultPath
New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
$enableLog = Join-Path $resultDirectory 'harness-enable.log'
$testLog = Join-Path $resultDirectory 'harness-contract.log'
$disableLog = Join-Path $resultDirectory 'harness-disable.log'

function ConvertTo-UnityArgument([string]$Value) {
  if ($null -eq $Value) { return '""' }
  # Start-Process joins ArgumentList into one Windows command line. Unity
  # paths may contain spaces, so quote every value and escape embedded quotes
  # using the Windows command-line convention.
  return '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-Unity([string[]]$Arguments) {
  $quotedArguments = @($Arguments | ForEach-Object { ConvertTo-UnityArgument $_ })
  # Avoid the Start-Process wait switch here.  On this Unity installation it
  # can follow persistent VBCSCompiler descendants after the Unity root
  # process has finished.  The runner contract is based on the root exit code.
  $process = Start-Process -FilePath $unity -ArgumentList $quotedArguments -PassThru -WindowStyle Hidden
  try {
    $process.WaitForExit()
    return [int]$process.ExitCode
  }
  finally {
    $process.Dispose()
  }
}

# Snapshot the complete ProjectSettings file before Unity gets a chance to
# rewrite define symbols.  The execute-method cleanup is best effort, but the
# byte-for-byte snapshot is the fallback when a custom-define compile failure
# prevents DisableHarnessDefine from being loaded.
$projectSettingsPath = [IO.Path]::GetFullPath((Join-Path $project 'ProjectSettings/ProjectSettings.asset'))
if (-not (Test-Path -LiteralPath $projectSettingsPath -PathType Leaf)) {
  throw "Project settings file was not found: $projectSettingsPath"
}
$snapshotPath = $null
$snapshotReady = $false

# A failed enable invocation must still reach DisableHarnessDefine.  Start in
# the failed state so an exception cannot leave the caller reporting success.
$testExit = 1
$failureMessage = $null
try {
  $snapshotPath = [IO.Path]::GetTempFileName()
  [IO.File]::Copy($projectSettingsPath, $snapshotPath, $true)
  $snapshotReady = $true

  $enableExit = Invoke-Unity @(
    '-batchmode', '-nographics', '-quit', '-projectPath', $project,
    '-buildTarget', 'StandaloneWindows64', '-executeMethod',
    'ShitDesigner.Editor.StandaloneHarnessBuild.EnableHarnessDefine', '-logFile', $enableLog
  )
  if ($enableExit -ne 0) { throw "Harness define enable failed with exit code $enableExit." }

  # Do not pass -quit here: Unity's -runTests controller exits after the test
  # run, and -quit can terminate it before the XML result is flushed.
  $testExit = Invoke-Unity @(
    '-batchmode', '-nographics', '-projectPath', $project,
    '-buildTarget', 'StandaloneWindows64', '-runTests', '-testPlatform', 'editmode',
    '-assemblyNames', 'ShitDesigner.TestHarness.Tests.EditMode',
    '-testResults', $resultPath, '-logFile', $testLog
  )
}
catch {
  $failureMessage = $_.Exception.Message
  if ($testExit -eq 0) { $testExit = 1 }
}
finally {
  if ($snapshotReady) {
    try {
      $disableExit = Invoke-Unity @(
        '-batchmode', '-nographics', '-quit', '-projectPath', $project,
        '-buildTarget', 'StandaloneWindows64', '-executeMethod',
        'ShitDesigner.Editor.StandaloneHarnessBuild.DisableHarnessDefine', '-logFile', $disableLog
      )
      if ($disableExit -ne 0) {
        if ($testExit -eq 0) { $testExit = $disableExit }
        if ([string]::IsNullOrWhiteSpace($failureMessage)) { $failureMessage = "Harness define disable failed with exit code $disableExit." }
      }
    }
    catch {
      if ($testExit -eq 0) { $testExit = 1 }
      if ([string]::IsNullOrWhiteSpace($failureMessage)) { $failureMessage = $_.Exception.Message }
    }

    try {
      # Always restore the exact original bytes.  This removes only the
      # runner's temporary state and preserves symbol order and all unrelated
      # ProjectSettings fields, even if DisableHarnessDefine partially ran.
      [IO.File]::Copy($snapshotPath, $projectSettingsPath, $true)
    }
    catch {
      if ($testExit -eq 0) { $testExit = 1 }
      if ([string]::IsNullOrWhiteSpace($failureMessage)) { $failureMessage = $_.Exception.Message }
    }
  }

  if (-not [string]::IsNullOrWhiteSpace($snapshotPath)) {
    try {
      Remove-Item -LiteralPath $snapshotPath -Force -ErrorAction Stop
    }
    catch {
      if ($testExit -eq 0) { $testExit = 1 }
      if ([string]::IsNullOrWhiteSpace($failureMessage)) { $failureMessage = $_.Exception.Message }
    }
  }
}

if (-not [string]::IsNullOrWhiteSpace($failureMessage)) {
  [Console]::Error.WriteLine($failureMessage)
}
exit $testExit

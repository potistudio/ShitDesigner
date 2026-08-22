$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Assert-Contract {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) { throw "Standalone harness tooling contract failed: $Message" }
}

function Read-Source {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    return [IO.File]::ReadAllText((Join-Path $root $RelativePath))
}

$runnerPath = Join-Path $root 'Tools/RunStandaloneHarness.ps1'
$acceptanceRunnerPath = Join-Path $root 'Tools/RunStandaloneAcceptance.ps1'
$contractRunnerPath = Join-Path $root 'Tools/RunHarnessContractTests.ps1'
$artifactValidatorPath = Join-Path $root 'Tools/StandaloneHarnessArtifactValidation.ps1'
$processPath = Join-Path $root 'Tools/StandaloneHarnessProcess.ps1'
$environmentPath = Join-Path $root 'Tools/UnityProcessEnvironment.ps1'
$runnerTokens = $null
$runnerErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($runnerPath, [ref]$runnerTokens, [ref]$runnerErrors) | Out-Null
Assert-Contract ($null -eq $runnerErrors -or $runnerErrors.Count -eq 0) 'RunStandaloneHarness.ps1 must parse without PowerShell errors.'
$acceptanceRunnerTokens = $null
$acceptanceRunnerErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($acceptanceRunnerPath, [ref]$acceptanceRunnerTokens, [ref]$acceptanceRunnerErrors) | Out-Null
Assert-Contract ($null -eq $acceptanceRunnerErrors -or $acceptanceRunnerErrors.Count -eq 0) 'RunStandaloneAcceptance.ps1 must parse without PowerShell errors.'
$contractRunnerTokens = $null
$contractRunnerErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($contractRunnerPath, [ref]$contractRunnerTokens, [ref]$contractRunnerErrors) | Out-Null
Assert-Contract ($null -eq $contractRunnerErrors -or $contractRunnerErrors.Count -eq 0) 'RunHarnessContractTests.ps1 must parse without PowerShell errors.'
$validatorTokens = $null
$validatorErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($artifactValidatorPath, [ref]$validatorTokens, [ref]$validatorErrors) | Out-Null
Assert-Contract ($null -eq $validatorErrors -or $validatorErrors.Count -eq 0) 'StandaloneHarnessArtifactValidation.ps1 must parse without PowerShell errors.'
$processTokens = $null
$processErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile($processPath, [ref]$processTokens, [ref]$processErrors) | Out-Null
Assert-Contract ($null -eq $processErrors -or $processErrors.Count -eq 0) 'StandaloneHarnessProcess.ps1 must parse without PowerShell errors.'

$runner = Read-Source 'Tools/RunStandaloneHarness.ps1'
$acceptanceRunner = Read-Source 'Tools/RunStandaloneAcceptance.ps1'
$contractRunner = Read-Source 'Tools/RunHarnessContractTests.ps1'
$artifactValidator = Read-Source 'Tools/StandaloneHarnessArtifactValidation.ps1'
$process = Read-Source 'Tools/StandaloneHarnessProcess.ps1'
$environment = Read-Source 'Tools/UnityProcessEnvironment.ps1'
$cmd = Read-Source 'Tools/RunStandaloneHarness.cmd'
$build = Read-Source 'Assets/ShitDesigner/Editor/StandaloneHarnessBuild.cs'
$bootstrapAuthoring = Read-Source 'Assets/ShitDesigner/Editor/BootstrapSceneAuthoring.cs'
$presentationTheme = Read-Source 'Assets/ShitDesigner/Presentation/Resources/PresentationTheme.uss'
$projectSettings = Read-Source 'ProjectSettings/ProjectSettings.asset'
$harnessAsmDef = (Read-Source 'Assets/ShitDesigner/TestHarness/ShitDesigner.TestHarness.asmdef' | ConvertFrom-Json)
$harnessTestsAsmDef = (Read-Source 'Assets/ShitDesigner/TestHarness/Tests/ShitDesigner.TestHarness.Tests.EditMode.asmdef' | ConvertFrom-Json)
$harnessContractSource = Read-Source 'Assets/ShitDesigner/TestHarness/Tests/HarnessContractTests.cs'
$performanceContractSource = Read-Source 'Assets/ShitDesigner/TestHarness/Tests/PerformanceHarnessContractTests.cs'
$acceptanceContractSource = Read-Source 'Assets/ShitDesigner/TestHarness/Tests/StandaloneAcceptanceContractTests.cs'
$acceptanceHarnessSource = Read-Source 'Assets/ShitDesigner/TestHarness/StandaloneAcceptanceHarness.cs'
$acceptanceContractsSource = Read-Source 'Assets/ShitDesigner/TestHarness/StandaloneAcceptanceContracts.cs'
$harnessArtifactSource = Read-Source 'Assets/ShitDesigner/TestHarness/HarnessContracts.cs'
$performanceHarnessSource = Read-Source 'Assets/ShitDesigner/TestHarness/StandalonePerformanceHarness.cs'
$productionCompositionSource = Read-Source 'Assets/ShitDesigner/Bootstrap/ProductionCompositionRoot.cs'
$persistenceSource = Read-Source 'Assets/ShitDesigner/Persistence/ProjectPersistence.cs'

Assert-Contract ($process -match '\.WaitForExit\(\)') 'Runner must wait for the root Unity/Player process.'
Assert-Contract ($runner -notmatch '(?im)^\s*Start-Process[^\r\n]*-Wait') 'Runner must not delegate waiting to Start-Process -Wait.'
Assert-Contract ($process -notmatch '(?im)^\s*Start-Process[^\r\n]*-Wait') 'Process helper must not delegate waiting to Start-Process -Wait.'
Assert-Contract ($process -match "WindowStyle\s*=\s*'Hidden'" -and $process -match "ValidateSet\('Hidden', 'Normal'\)") 'Root process helper must preserve Hidden as the default and allow only the explicit visible Performance override.'
Assert-Contract ($runner -match '\$projectSettingsPath' -and $runner -match '\[IO\.File\]::Copy\(\$snapshotPath,\s*\$projectSettingsPath') 'Runner must snapshot and restore ProjectSettings bytes.'
Assert-Contract ($runner -match '(?is)finally\s*\{.*?\[IO\.File\]::Copy\(\$snapshotPath') 'ProjectSettings restore must be in finally.'
Assert-Contract ($runner -match "'?-batchmode") 'Unity build invocations must run in batch mode.'
Assert-Contract ($runner -match '-force-d3d12' -and $runner -match '-force-vulkan' -and $runner -match '-force-metal') 'Runtime API selection must cover D3D12, Vulkan, and Metal.'
Assert-Contract ($runner -match 'StandaloneWindows64' -and $runner -match 'StandaloneOSX') 'Runner must select both supported Standalone targets.'
Assert-Contract ($runner -match 'StandaloneWindows64.*ShitDesignerHarness\.exe' -and $runner -match 'StandaloneOSX.*ShitDesignerHarness\.app') 'Default build paths must be platform-specific.'
Assert-Contract ($runner -notmatch 'harness-build-\$GraphicsApi') 'GraphicsApi must not create an API-specific Windows build.'
Assert-Contract ($runner -match 'harnessDefineNeedsCleanup' -and $runner -match 'DisableHarnessDefine') 'Define cleanup must cover build failures.'
Assert-Contract ($runner -match "StandaloneHarnessArtifactValidation\.ps1" -and $runner -match 'Validate-HarnessArtifacts') 'Runner must validate the generated artifact pair.'
Assert-Contract ($runner -match '\$playerArguments\s*=\s*@\(\)\s*\+' -and $runner -notmatch '\$playerArguments\s*=\s*@\(''\-batchmode''\)') 'Performance Player must not use Unity headless batch mode; FrameTiming requires a real presentation boundary.'
Assert-Contract ($runner -match 'Invoke-RootProcess\s+-FilePath\s+\$playerPath\s+-Arguments\s+\$playerArguments\s+-WindowStyle\s+Normal' -and $runner -match 'visible desktop' -and $runner -match 'Program' -and $runner -match 'Presented Frame') 'Windows Performance Player must be explicitly Normal on the visible desktop for Program Presented Frame timing.'
Assert-Contract ($runner -match 'WarmupSeconds and MeasureSeconds are fixture-only overrides' -and $runner -match '\-sdHarnessFixtureMode' -and $runner -match '\-sdHarnessWarmupSeconds' -and $runner -match '\-sdHarnessMeasureSeconds' -and $runner -match 'else\s*\{\s*30d\s*\}' -and $runner -match 'else\s*\{\s*2d\s*\}') 'Fixture runs must explicitly pass a 30 second readiness window and a short measured interval without changing the production 30/600 contract.'
Assert-Contract ($runner -match 'runStartedUtc' -and $runner -match 'runDirectoryId') 'Runner must use run-scoped/fresh artifact selection.'
Assert-Contract ($runner -match 'cleanupFailures\s*\+=') 'Runner must aggregate cleanup errors without overwriting the original failure.'
Assert-Contract ($runner -match 'snapshotReady' -and $runner -match '\$snapshotPath\s*=\s*\[IO\.Path\]::GetTempFileName') 'Snapshot creation and partial snapshot cleanup must be guarded.'
Assert-Contract ($runner -match 'StandaloneHarnessProcess\.ps1' -and $process -match 'CommandLineToArgvW' -and $process -match 'backslashes') 'Runner must quote trailing backslashes and embedded quotes safely.'
Assert-Contract ($runner -match '\$unity\s*=\s*\$UnityPath') 'Runner must use the configured Unity executable path.'
Assert-Contract ($environment -match 'function\s+Ensure-UnityProcessEnvironment') 'Unity process environment fallback must be defined in one shared helper.'
Assert-Contract ($environment -match '\$env:ALLUSERSPROFILE\s*=\s*\$env:ProgramData') 'Unity process environment fallback must set ALLUSERSPROFILE from ProgramData.'
Assert-Contract ($environment -match 'Test-Path\s+-LiteralPath\s+\$env:ProgramData\s+-PathType\s+Container') 'Unity process environment fallback must validate ProgramData before assignment.'
Assert-Contract ($environment -notmatch "(?i)SetEnvironmentVariable\([^\r\n]*(Machine|User)") 'Unity process environment fallback must not persist Machine or User environment changes.'
Assert-Contract ($runner -match 'UnityProcessEnvironment\.ps1' -and $runner -match 'Ensure-UnityProcessEnvironment') 'Standalone runner must apply the shared Unity process environment fallback before launching Unity.'
Assert-Contract ($acceptanceRunner -match 'UnityProcessEnvironment\.ps1' -and $acceptanceRunner -match 'Ensure-UnityProcessEnvironment') 'Acceptance runner must apply the shared Unity process environment fallback before launching Unity.'
Assert-Contract ($contractRunner -match 'UnityProcessEnvironment\.ps1' -and $contractRunner -match 'Ensure-UnityProcessEnvironment') 'Harness contract runner must apply the shared Unity process environment fallback before launching Unity.'
Assert-Contract ($runner -match '6000\.5\.9f1' -and $acceptanceRunner -match '6000\.5\.9f1' -and $contractRunner -match '6000\.5\.9f1') 'All harness runners must default to Unity 6000.5.9f1.'
Assert-Contract ($artifactValidator -match 'Player exit code must be 0, 1, or 2' -and $artifactValidator -match 'artifactWriteError') 'Artifact validator must enforce exit/status and write-error contracts.'
Assert-Contract ($artifactValidator -match 'LastWriteTimeUtc' -and $artifactValidator -match 'runId') 'Artifact validator must enforce freshness and run identity.'
Assert-Contract ($artifactValidator -match 'ExpectedPlatform' -and $artifactValidator -match 'ExpectedGraphicsApi' -and $artifactValidator -match 'ExpectedCodec' -and $artifactValidator -match 'ExpectedFixtureMode') 'Performance artifact validation must compare the requested platform, graphics API, codec, and fixture mode.'
Assert-Contract ($artifactValidator -match 'schemaVersion' -and $artifactValidator -match 'warmupSeconds' -and $artifactValidator -match 'measureSeconds' -and $artifactValidator -match 'presentedFrames') 'Performance artifact validation must enforce schema, duration, and timing contracts.'
Assert-Contract ($artifactValidator -match 'poolBudgetBytes' -and $artifactValidator -match 'endNativeContextCount' -and $artifactValidator -match 'faultedFrames' -and $artifactValidator -match 'expectedLogicalControlUpdates') 'Performance artifact validation must enforce resources, diagnostics, and interaction contracts.'
Assert-Contract ($artifactValidator -match 'projectRoot' -and $artifactValidator -match 'nativePluginProbe' -and $artifactValidator -match 'codecProbe' -and $artifactValidator -match 'HapVideoBackend' -and $artifactValidator -match 'UnityVideoBackend') 'Performance artifact validation must enforce project and codec/native probe contracts.'
Assert-Contract ($runner -match '\$playerLogPath' -and $runner -match '-logFile.*playerLogPath' -and $runner -match 'ApplicationLogPath') 'Player application logs must be written into and validated from the run directory.'
Assert-Contract ($build -match 'BootstrapSceneAuthoring\.Ensure\(\)' -and $bootstrapAuthoring -match 'EnsurePresentationFonts' -and $bootstrapAuthoring -match 'NotoSansJP\.ttf' -and $bootstrapAuthoring -match 'fallbacks\s*==\s*null' -and $bootstrapAuthoring -match 'fallbackFontAssetTable\s*=\s*fallbacks' -and $bootstrapAuthoring -match 'new\s+List<FontAsset>' -and $bootstrapAuthoring -match 'PersistFontSubassets' -and $bootstrapAuthoring -match 'AssetDatabase\.AddObjectToAsset\(texture,\s*asset\)' -and $bootstrapAuthoring -match 'AssetDatabase\.AddObjectToAsset\(asset\.material,\s*asset\)') 'Harness builds must generate the bundled Noto TextCore FontAssets, including initialized fallback tables and persisted atlas/material subassets, before copying the production scene.'
Assert-Contract ($presentationTheme -match 'resource\("NotoSans"\)' -and $presentationTheme -match 'resource\("NotoSansMono"\)') 'The live Resources theme must use the bundled Noto Sans and Noto Sans Mono TextCore FontAssets.'
Assert-Contract ((Test-Path -LiteralPath (Join-Path $root 'Assets/ShitDesigner/Presentation/Resources/Fonts/NotoSans.ttf')) -and (Test-Path -LiteralPath (Join-Path $root 'Assets/ShitDesigner/Presentation/Resources/Fonts/NotoSansMono.ttf')) -and (Test-Path -LiteralPath (Join-Path $root 'Assets/ShitDesigner/Presentation/Resources/Fonts/NotoSansJP.ttf'))) 'The required Noto source fonts must be bundled in Resources.'
Assert-Contract (Test-Path -LiteralPath (Join-Path $root 'ThirdParty/NotoFonts-OFL-1.1.txt')) 'The bundled Noto source fonts must retain their SIL Open Font License 1.1 text.'

Assert-Contract ($acceptanceRunner -match 'Invoke-PlayerStage' -and $acceptanceRunner -match "'Initial'" -and $acceptanceRunner -match "'Reopen'" -and $acceptanceRunner -match "'Recovery'") 'Acceptance runner must execute all three stages.'
Assert-Contract ($acceptanceRunner -match '\$player\s*=\s*Get-PlayerPath' -and $acceptanceRunner -match 'Invoke-RootProcess\s+-FilePath\s+\$player') 'Acceptance stages must use one selected Player build.'
Assert-Contract ($acceptanceRunner -match '\$args\s*=\s*@\(''\-batchmode''\)' -and $acceptanceRunner -match 'Invoke-RootProcess\s+-FilePath\s+\$player\s+-Arguments\s+\$args(?!\s+-WindowStyle)' -and $acceptanceRunner -notmatch 'WindowStyle\s+Normal') 'Acceptance Player must remain batch-mode and use the helper Hidden default.'
Assert-Contract ($acceptanceRunner -match '-force-d3d12' -and $acceptanceRunner -match '-force-vulkan' -and $acceptanceRunner -match '-force-metal') 'Acceptance runner must expose the supported graphics API flags.'
Assert-Contract ($acceptanceRunner -match 'sdHarnessMode.*acceptance' -and $acceptanceRunner -match 'sdHarnessExpectedBackupFingerprint') 'Acceptance runner must pass acceptance mode and the known backup fingerprint.'
Assert-Contract ($acceptanceRunner -match '\$playerLog\s*=\s*Join-Path\s+\$StageDirectory\s+''Player\.log''' -and $acceptanceRunner -match '''-logFile'',\s*\$playerLog' -and $acceptanceRunner -match 'Validate-HarnessArtifacts.*ApplicationLogPath\s+\$playerLog' -and $acceptanceRunner -match "Invoke-PlayerStage 'Initial'" -and $acceptanceRunner -match "Invoke-PlayerStage 'Reopen'" -and $acceptanceRunner -match "Invoke-PlayerStage 'Recovery'") 'Each Acceptance Player stage must write and validate a unique Player.log inside its stage artifact directory.'
Assert-Contract ($acceptanceRunner -match '(?is)try\s*\{.*?\$snapshot\s*=\s*\[IO\.Path\]::GetTempFileName') 'Acceptance snapshot creation must be inside the protected try block.'
Assert-Contract ($acceptanceRunner -match '(?is)finally\s*\{.*?ProjectSettings restore failed' -and $acceptanceRunner -match 'cleanupFailures\s*\+=') 'Acceptance runner must restore settings and aggregate cleanup errors.'
Assert-Contract ($acceptanceRunner -match 'Test-ContainedPath' -and $acceptanceRunner -match 'recovery-project' -and $acceptanceRunner -match 'Copy-Item\s+-LiteralPath\s+\$projectFull') 'Recovery copy must be an exact, prevalidated child of the run artifact root.'
Assert-Contract ($acceptanceRunner -match 'source-fixtures' -and $acceptanceRunner -match '\$sourceFixtureRoot' -and $acceptanceRunner -match 'Copy-Item\s+-LiteralPath\s+\$sourceFixtureRoot' -and $acceptanceRunner -match 'Remove-Item\s+-LiteralPath\s+\$runFixtureRoot\s+-Recurse') 'Acceptance Initial must use a run-local fixture copy and remove it before Reopen.'
Assert-Contract ($acceptanceRunner -match 'portable-project' -and $acceptanceRunner -match '\$portableProjectFull' -and $acceptanceRunner -match 'Invoke-PlayerStage' -and $acceptanceRunner -match 'portableProjectFull') 'Acceptance Reopen must use a copied project root after the fixture source is removed.'
Assert-Contract ($acceptanceRunner -match 'backupFingerprint' -and $acceptanceRunner -match 'project\.json\.bak') 'Acceptance runner must validate and propagate the durable backup target.'
Assert-Contract ($acceptanceRunner -match '(?s)Copy-Item\s+-LiteralPath\s+\$portableProjectFull\s+-Destination\s+\$recoveryFull.*?\$reopen\s*=\s*Invoke-PlayerStage\s+''Reopen''' -and $acceptanceRunner -match 'Invoke-PlayerStage ''Recovery''.*\$backupFingerprint') 'Recovery must use an independent pre-Reopen copy and the Initial seeded backup fingerprint.'
Assert-Contract ($acceptanceRunner -match 'Validate-HarnessArtifacts') 'Acceptance runner must validate each stage artifact pair.'
Assert-Contract ($contractRunner -match "'-assemblyNames',\s*'ShitDesigner\.TestHarness\.Tests\.EditMode'" -and $contractRunner -notmatch "'-testFilter',\s*'ShitDesigner\.TestHarness\.Tests\.HarnessContractTests'") 'Harness contract runner must execute the complete EditMode test assembly rather than one legacy class.'
Assert-Contract ($contractRunner -match "'-assemblyNames',\s*'ShitDesigner\.TestHarness\.Tests\.EditMode'" -and $harnessContractSource -match 'class\s+HarnessContractTests' -and $performanceContractSource -match 'class\s+PerformanceHarnessContractTests' -and $acceptanceContractSource -match 'class\s+StandaloneAcceptanceContractTests') 'Harness contract runner must target the EditMode assembly containing all three contract test classes.'
Assert-Contract ($acceptanceRunner -match 'LastPlayerExitCode' -and $acceptanceRunner -match 'ExitCode\s*=\s*2') 'Acceptance runner must preserve an environment-failed Player exit code through artifact errors.'
Assert-Contract ($acceptanceRunner -match 'expectedPlatform' -and $acceptanceRunner -match 'expectedGraphicsApi' -and $acceptanceRunner -match 'json\.mode' -and $acceptanceRunner -match 'json\.stage') 'Acceptance runner must reject an artifact from the wrong platform, graphics API, mode, or stage.'
Assert-Contract ($acceptanceRunner -notmatch "manifest\.json'\)\s*-PathType\s+Leaf\)\)\s*\{") 'Missing fixtures must be reported by the Player acceptance artifact rather than rejected before the Player can write EnvironmentFailed.'
Assert-Contract ($acceptanceRunner -match '(?m)^\s*\$args\s*=\s*@\(' -and $acceptanceRunner -notmatch '(?m)^\s*\$args\s*=\s*@\([^\r\n]*-quit') 'Acceptance Player invocation must not add -quit before the Player owns its exit.'
$acceptanceCmd = Read-Source 'Tools/RunStandaloneAcceptance.cmd'
Assert-Contract ($acceptanceCmd -match '(?i)RunStandaloneAcceptance\.ps1' -and $acceptanceCmd -match '(?i)ERRORLEVEL') 'Acceptance CMD must delegate and preserve the PowerShell exit code.'

Assert-Contract ($cmd -match '(?i)RunStandaloneHarness\.ps1') 'CMD must delegate to the PowerShell runner.'
Assert-Contract ($cmd -match '(?i)ERRORLEVEL') 'CMD must return the delegated runner exit code.'
Assert-Contract ($cmd -notmatch '(?i)Unity\.exe') 'CMD must not duplicate Unity build orchestration.'

$d3d12Index = $build.IndexOf('GraphicsDeviceType.Direct3D12', [StringComparison]::Ordinal)
$vulkanIndex = $build.IndexOf('GraphicsDeviceType.Vulkan', [StringComparison]::Ordinal)
Assert-Contract ($d3d12Index -ge 0 -and $vulkanIndex -gt $d3d12Index) 'Windows graphics API order must be D3D12 then Vulkan.'
Assert-Contract ($build -match 'GraphicsDeviceType\.Metal') 'macOS build must select Metal.'
Assert-Contract ($build -match 'OSArchitecture\.ARM64') 'macOS build must select arm64.'
Assert-Contract ($build -match 'enableFrameTimingStats\s*=\s*true' -and $build -match 'oldFrameTimingStats') 'Frame timing stats must be enabled for the build and restored.'
Assert-Contract ($projectSettings -match '(?m)^\s*enableFrameTimingStats:\s*1\s*$') 'Product Player must permanently enable frame timing statistics.'
Assert-Contract ($projectSettings -match '(?ms)^\s*scriptingBackend:\s*\r?\n\s*Standalone:\s*1\s*$') 'Product Standalone Player must permanently use the IL2CPP scripting backend.'
Assert-Contract ($build -match 'SetScriptingBackend\(NamedBuildTarget\.Standalone,\s*ScriptingImplementation\.IL2CPP\)' -and $build -match 'oldScriptingBackend' -and $build -match 'Standalone scripting backend') 'Harness builds must explicitly select IL2CPP and restore the previous Standalone scripting backend.'
Assert-Contract ($build -match 'sdHarnessBuildTarget') 'Build entry must accept an explicit platform target.'
Assert-Contract ($runner -match 'BuildStandalonePerformanceHarness' -and $build -match '(?s)BuildStandalonePerformanceHarness\(\).*?BuildOptions\.Development' -and $build -match '(?s)BuildStandaloneAcceptanceHarness\(\).*?BuildOptions\.None') 'Performance builds must be Development for all-thread GC allocation bytes while Acceptance remains a production Player build.'
Assert-Contract ($build -notmatch 'ConnectWithProfiler') 'Performance Development builds must not auto-connect a Profiler.'
Assert-Contract ($artifactValidator -match 'developmentBuild' -and $artifactValidator -match "buildOptions -ne 'Development'") 'Performance artifact validation must retain the Development build provenance required for GC allocation measurement.'
Assert-Contract ($acceptanceRunner -match 'BuildStandaloneAcceptanceHarness' -and $acceptanceRunner -match 'developmentBuild' -and $acceptanceRunner -match "buildOptions -ne 'None'") 'Acceptance runner must build and validate a non-Development production Player.'
Assert-Contract ($harnessAsmDef.references -contains 'Unity.RenderPipelines.Core.Runtime') 'Harness runtime must reference Unity RenderPipelines core for GraphicsSettings.'
Assert-Contract ($harnessTestsAsmDef.references -contains 'ShitDesigner.Application' -and $harnessTestsAsmDef.references -contains 'ShitDesigner.Graph' -and $harnessTestsAsmDef.references -contains 'ShitDesigner.Media' -and $harnessTestsAsmDef.references -contains 'ShitDesigner.Nodes' -and $harnessTestsAsmDef.references -contains 'ShitDesigner.Persistence') 'Harness contract tests must reference the production assemblies used by their public read-model contracts.'
Assert-Contract ($harnessArtifactSource -match 'ProfilerCategory\.Memory' -and $harnessArtifactSource -match 'GC Allocated In Frame' -and $harnessArtifactSource -match 'ProfilerMarkerDataUnit\.Bytes' -and $harnessArtifactSource -match 'SumAllSamplesInFrame' -and $harnessArtifactSource -match 'WrapAroundWhenCapacityReached' -and $harnessArtifactSource -notmatch 'MarkerOptions\s*=\s*[^;\r\n]*CollectOnlyOnCurrentThread') 'Performance GC allocation measurement must use the all-thread Memory GC Allocated In Frame bytes counter with a wrapping latest-frame sample, never a current-thread count or duration.'
Assert-Contract ($performanceHarnessSource -match 'HarnessGcAllocationContract\.CounterCategory' -and $performanceHarnessSource -match 'HarnessGcAllocationContract\.CounterName' -and $performanceHarnessSource -match 'HarnessGcAllocationContract\.SampleCapacity' -and $performanceHarnessSource -match 'UnitType' -and $performanceHarnessSource -match 'DescribeAvailableMemoryCounters' -and $performanceHarnessSource -match 'became unavailable during the measured interval') 'Performance harness must use the wrapping latest-frame GC byte counter, and report counter availability rather than emit gcAllocatedBytes when the Player recorder is unavailable.'
Assert-Contract ($performanceHarnessSource -notmatch 'Completed Unity FrameTiming was invalid' -and $performanceHarnessSource -match 'MarkUnresolvedTimingUnavailable' -and $harnessArtifactSource -match 'timingAvailableFrames' -and $harnessArtifactSource -match 'timingUnavailableFrames') 'Unavailable FrameTiming must remain an explicit same-frame NaN quality sample, reported in the artifact and judged only by the documented 99 percent ratio.'
Assert-Contract ($performanceHarnessSource -match 'HarnessTimingCompletionTracker' -and $performanceHarnessSource -match 'HarnessFrameTimingReadinessContract\.IsReady' -and $performanceHarnessSource -match 'FrameTimingWarmupTimeoutSeconds' -and $performanceHarnessSource -match 'HarnessFrameTimingSourceArtifact' -and $performanceHarnessSource -match 'BeginTimingDrain' -and $performanceHarnessSource -match 'FrameTimingDrainPresentationCount\s*=\s*FrameTimingCompletionCorrelation\.MaximumPendingFrames\s*\+\s*1' -and $performanceHarnessSource -match 'DrainUncompleted' -and $performanceHarnessSource -match 'HarnessMeasurementBoundaryContract' -and $productionCompositionSource -match 'ProductionFrameTimingDiagnostic' -and $productionCompositionSource -match 'RawInvalid' -and $productionCompositionSource -match 'ApiException' -and $productionCompositionSource -match 'TryReadCompleted' -and $productionCompositionSource -match 'IUnityFrameTimingHistoryReader' -and $productionCompositionSource -match 'CaptureAndRead\(_history\)' -and $productionCompositionSource -match 'FrameTimingCompletionCorrelation' -and $productionCompositionSource -match 'CompletionDelayFrames\s*=\s*4' -and $productionCompositionSource -match 'MaximumPendingFrames\s*=\s*CompletionDelayFrames' -and $productionCompositionSource -match 'GetLatestTimings\(\(uint\)Math\.Min\(destination\.Length, _timings\.Length\), _timings\)' -and $productionCompositionSource -match 'OldestFirstIndex\(\(int\)count, ordinal\)' -and $productionCompositionSource -notmatch 'Queue<RuntimeFrameTimingSample>' -and $productionCompositionSource -match 'RecordPresentation\(presentedFrameNumber, now\)' -and $productionCompositionSource -match 'TryExpire\(out sample\)' -and $productionCompositionSource -match 'completionIdentity\s*<=\s*0d' -and $productionCompositionSource -match '(?s)catch\s*\{.*?TryExpire\(out sample\)' -and $productionCompositionSource -notmatch 'RuntimeFrameTimingSample\.Unavailable\(presentedFrameNumber\)') 'FrameTiming completion must fence the first public valid completion before measurement, publish raw terminal outcome evidence, consume one oldest unseen result from Unity newest-first multi-frame history per scalar public poll, tolerate only a finite jitter window, preserve one extra public projection Tick, and expire missing or invalid completions without an unbounded queue.'
Assert-Contract ($acceptanceHarnessSource -match 'NavigationSubmitEvent\.GetPooled\(\)' -and $acceptanceHarnessSource -match 'FocusAndSubmitPickVerifiedAcceptanceSave' -and $acceptanceHarnessSource -match 'focusController.*focusedElement') 'Acceptance Save must verify focus and dispatch public NavigationSubmit after its live-panel Pick proof.'
Assert-Contract ($acceptanceHarnessSource -notmatch 'SimulateSingleClick' -and $acceptanceHarnessSource -notmatch 'System\.Reflection' -and $acceptanceHarnessSource -notmatch 'ClickEvent\.GetPooled\(') 'Acceptance Save must not depend on internal Clickable reflection or direct ClickEvent dispatch in the Player.'
Assert-Contract ($acceptanceHarnessSource -match 'SaveTaskPublished' -and $acceptanceHarnessSource -match 'DescribeSaveTaskFailure' -and $acceptanceContractsSource -match 'SaveTaskPublished' -and $acceptanceContractsSource -match 'SaveTaskFailed') 'Acceptance Save must recognize a newly published failed Save task and report its public diagnostic.'
Assert-Contract ($harnessArtifactSource -match 'taskAfterStage' -and $harnessArtifactSource -match 'taskAfterPath' -and $harnessArtifactSource -match 'taskAfterDiagnosticCode' -and $harnessArtifactSource -match 'taskAfterExceptionMessage') 'Acceptance artifact must retain failed Save stage, path, diagnostic, and exception evidence.'
Assert-Contract ($acceptanceContractsSource -match 'ComputePersistedPreviewComponent' -and $acceptanceContractsSource -notmatch 'workspace-panel:' -and $acceptanceContractsSource -match 'preview\.FitMode' -and $acceptanceContractsSource -match 'preview\.BackgroundMode' -and $acceptanceContractsSource -match 'tab order is Project UI State') 'Acceptance fingerprint component diagnostics must retain Preview tab ordering while excluding Workspace session state.'
Assert-Contract ($acceptanceHarnessSource -match 'CaptureCanonicalProjectFingerprint' -and $acceptanceHarnessSource -match 'TryCaptureCanonicalProjectFingerprint' -and $acceptanceContractsSource -match 'Runtime quality and demand negotiation' -and $acceptanceContractsSource -match 'DescribeDifference' -and $acceptanceHarnessSource -match 'fingerprintComponents' -and $harnessArtifactSource -match 'acceptanceFingerprintComponents') 'Acceptance persistence equality must use the exact canonical Project query while retaining component diagnostics without hashing runtime output negotiation.'
Assert-Contract ($acceptanceHarnessSource -match 'ObserveAcceptanceOutputEvidence\(model, videoId, fixture\)' -and $acceptanceHarnessSource -match 'FixtureFrameEvidenceObserved\(fixture\.ownershipFramesObserved, fixture\.outputReadyObserved, fixture\.realFrameObserved\)' -and $acceptanceHarnessSource -match 'return _acceptanceOutputsObserved && _acceptanceRealFrameObserved' -and $acceptanceContractsSource -match 'ObserveOutputsReadyAfterVideoBinding' -and $harnessArtifactSource -match 'acceptanceLastOutput') 'Acceptance fixture frame evidence must retain separate in-fixture ownership and bound public real-frame observations, and retain the last public output diagnostic.'
Assert-Contract ($acceptanceContractsSource -match '!x\.ownershipFramesObserved' -and $acceptanceContractsSource -match '!x\.outputReadyObserved' -and $acceptanceContractsSource -match '!x\.realFrameObserved') 'Initial acceptance artifact validation must reject a fixture missing any ownership, bound-output, or real-frame evidence.'
Assert-Contract ($persistenceSource -match 'PreviewNodeIds = x\?\.PreviewNodeIds\?\.ToList\(\)' -and $persistenceSource -notmatch 'PreviewNodeIds = x\?\.PreviewNodeIds\?\.OrderBy') 'Canonical persistence must preserve Project-owned Preview tab assignment order.'
Assert-Contract ($build -match 'TryDeleteTemporaryScene' -and $build -match 'AssetDatabase\.DeleteAsset returned false' -and $build -match 'TryRestoreActiveScene') 'Build entry must verify scene cleanup and restore the active scene.'
Assert-Contract ($build -match 'CombineFailures' -and $build -match 'TryRestore\(' -and $build -match 'oldApis \?\? Array\.Empty') 'Build entry must aggregate failures and restore empty graphics API lists.'
Assert-Contract ($build -match 'using UnityScene = UnityEngine\.SceneManagement\.Scene;' -and $build -match 'RestoreActiveSceneOrThrow\(UnityScene originalScene\)' -and $build -match 'TryRestoreActiveScene\(UnityScene originalScene') 'Build entry must qualify Unity Scene to avoid the ShitDesigner.Scene namespace collision.'
Assert-Contract ($build -match 'const string temporaryPath = "Assets/Scenes/ShitDesignerHarnessBuildTemp\.unity";' -and $build -notmatch 'Assets/Scenes/\.ShitDesignerHarnessBuild\.unity') 'Temporary Harness scene must use a Unity-valid non-dot asset name.'

. (Join-Path $root 'Tools/StandaloneHarnessArtifactValidation.ps1')
. (Join-Path $root 'Tools/StandaloneHarnessProcess.ps1')
. $environmentPath
$originalAllUsersProfile = [Environment]::GetEnvironmentVariable('ALLUSERSPROFILE', 'Process')
$originalProgramData = [Environment]::GetEnvironmentVariable('ProgramData', 'Process')
$machineAllUsersProfile = [Environment]::GetEnvironmentVariable('ALLUSERSPROFILE', 'Machine')
$userAllUsersProfile = [Environment]::GetEnvironmentVariable('ALLUSERSPROFILE', 'User')
try {
    $validProgramData = [IO.Path]::GetTempPath()
    [Environment]::SetEnvironmentVariable('ProgramData', $validProgramData, 'Process')
    [Environment]::SetEnvironmentVariable('ALLUSERSPROFILE', $null, 'Process')
    Ensure-UnityProcessEnvironment
    Assert-Contract ([Environment]::GetEnvironmentVariable('ALLUSERSPROFILE', 'Process') -eq $validProgramData) 'Unity process environment fallback must populate only the current process when ALLUSERSPROFILE is absent.'
    Assert-Contract ([Environment]::GetEnvironmentVariable('ALLUSERSPROFILE', 'Machine') -eq $machineAllUsersProfile -and [Environment]::GetEnvironmentVariable('ALLUSERSPROFILE', 'User') -eq $userAllUsersProfile) 'Unity process environment fallback must not persist ALLUSERSPROFILE outside the current process.'

    [Environment]::SetEnvironmentVariable('ALLUSERSPROFILE', $null, 'Process')
    [Environment]::SetEnvironmentVariable('ProgramData', (Join-Path $validProgramData ('missing-programdata-' + [Guid]::NewGuid().ToString('N'))), 'Process')
    $invalidProgramDataThrew = $false
    try { Ensure-UnityProcessEnvironment } catch { $invalidProgramDataThrew = $true }
    Assert-Contract $invalidProgramDataThrew 'Unity process environment fallback must fail fast when ProgramData is not a valid directory.'
}
finally {
    [Environment]::SetEnvironmentVariable('ALLUSERSPROFILE', $originalAllUsersProfile, 'Process')
    [Environment]::SetEnvironmentVariable('ProgramData', $originalProgramData, 'Process')
}
$quoteCases = @(
    @{ Input = ''; Expected = '""' },
    @{ Input = 'plain'; Expected = 'plain' },
    @{ Input = 'C:\path with space\'; Expected = '"C:\path with space\\"' },
    @{ Input = 'C:\path "quoted"\'; Expected = '"C:\path \"quoted\"\\"' }
)
foreach ($quoteCase in $quoteCases) {
    Assert-Contract ((ConvertTo-ProcessArgument $quoteCase.Input) -ceq $quoteCase.Expected) "Command-line quoting mismatch for '$($quoteCase.Input)'."
}

$artifactTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('ShitDesignerHarnessArtifactContract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $artifactTestRoot -Force | Out-Null
try {
    $runId = 'harness-contract-run'
    $runStartedUtc = [DateTime]::UtcNow.AddSeconds(-1)
    function Write-ArtifactContractPair {
        param([string]$Status, [string]$WriteError = '', [string]$LineEnding = "`n")
        $json = [pscustomobject]@{ runId = $runId; status = $Status; artifactWriteError = $WriteError } | ConvertTo-Json
        [IO.File]::WriteAllText((Join-Path $artifactTestRoot ($runId + '.json')), $json)
        [IO.File]::WriteAllText((Join-Path $artifactTestRoot ($runId + '.txt')), "status=$Status$LineEnding")
    }

    Write-ArtifactContractPair 'Passed'
    $result = Validate-HarnessArtifacts -Directory $artifactTestRoot -RunStartedUtc $runStartedUtc -PlayerExitCode 0
    Assert-Contract $result.IsValid 'Valid Passed artifact pair must be accepted for exit code 0.'

    Write-ArtifactContractPair 'EnvironmentFailed'
    $result = Validate-HarnessArtifacts -Directory $artifactTestRoot -RunStartedUtc $runStartedUtc -PlayerExitCode 2
    Assert-Contract ($result.IsValid -and $result.Status -eq 'EnvironmentFailed') 'EnvironmentFailed artifact must remain exit code 2.'

    Write-ArtifactContractPair 'Failed' -LineEnding "`r`n"
    $result = Validate-HarnessArtifacts -Directory $artifactTestRoot -RunStartedUtc $runStartedUtc -PlayerExitCode 1
    Assert-Contract ($result.IsValid -and $result.Status -eq 'Failed') 'CRLF Failed artifact must remain exit code 1.'

    Write-ArtifactContractPair 'Passed'
    $result = Validate-HarnessArtifacts -Directory $artifactTestRoot -RunStartedUtc $runStartedUtc -PlayerExitCode 1
    Assert-Contract (-not $result.IsValid) 'Status/exit mismatch must fail validation.'

    Write-ArtifactContractPair 'Passed' 'write failed'
    $result = Validate-HarnessArtifacts -Directory $artifactTestRoot -RunStartedUtc $runStartedUtc -PlayerExitCode 0
    Assert-Contract (-not $result.IsValid) 'artifactWriteError must fail validation.'

    [IO.File]::WriteAllText((Join-Path $artifactTestRoot ($runId + '.json')), '{not-json')
    $result = Validate-HarnessArtifacts -Directory $artifactTestRoot -RunStartedUtc $runStartedUtc -PlayerExitCode 0
    Assert-Contract (-not $result.IsValid) 'Malformed JSON must fail validation.'

    Remove-Item -LiteralPath (Join-Path $artifactTestRoot ($runId + '.txt')) -Force
    $result = Validate-HarnessArtifacts -Directory $artifactTestRoot -RunStartedUtc $runStartedUtc -PlayerExitCode 0
    Assert-Contract (-not $result.IsValid) 'Missing TXT artifact must fail validation.'
}
finally {
    if (Test-Path -LiteralPath $artifactTestRoot) { Remove-Item -LiteralPath $artifactTestRoot -Recurse -Force }
}

$strictArtifactRoot = Join-Path ([IO.Path]::GetTempPath()) ('ShitDesignerStrictHarnessArtifactContract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $strictArtifactRoot -Force | Out-Null
try {
    $strictRunId = 'harness-strict-contract-run'
    $strictRunStartedUtc = [DateTime]::UtcNow.AddSeconds(-1)
    $strictLogPath = Join-Path $strictArtifactRoot 'player.log'

    function New-StrictPreview {
        param([string]$Id = 'preview-1', [int]$Stage = 0)
        $widths = @(640, 480, 320, 160, 160)
        $heights = @(360, 270, 180, 90, 90)
        $fps = @(30, 30, 20, 10, 5)
        return [pscustomobject]@{
            id = $Id; width = $widths[$Stage]; height = $heights[$Stage]; format = 'R8G8B8A8_UNorm'
            targetFramesPerSecond = $fps[$Stage]; frameNumber = 12; quality = "Stage$Stage"; qualityStage = $Stage
        }
    }

    function New-StrictPerformanceArtifact {
        param([string]$Status = 'Passed', [string]$Codec = 'H264')
        $preview1 = New-StrictPreview 'preview-1' 0
        $preview2 = New-StrictPreview 'preview-2' 0
        $sample = [pscustomobject]@{ sampleSeconds = 600; programFrameNumber = 12; previews = @($preview1, $preview2) }
        $codecProbe = if ($Codec -eq 'Hap') {
            [pscustomobject]@{ path = 'ExtensionVideoCapabilityProbe(FileVideoMetadataProbe)'; passed = $true; supported = $true; backend = 'HapVideoBackend'; container = 'Mov'; codec = 'HapY'; hasAlpha = $false; hasAudio = $false; durationSeconds = 1; diagnostic = '' }
        }
        else {
            [pscustomobject]@{ path = 'ExtensionVideoCapabilityProbe(FileVideoMetadataProbe)'; passed = $true; supported = $true; backend = 'UnityVideoBackend'; container = 'Mp4'; codec = 'H264'; hasAlpha = $false; hasAudio = $false; durationSeconds = 1; diagnostic = '' }
        }
        $nativeProbe = if ($Codec -eq 'Hap') {
            [pscustomobject]@{ path = 'PInvokeHapNativeApi.ProbeInstalledBinary'; supportedPlatform = $true; passed = $true; abiVersion = 1; capabilities = 15; diagnosticCode = ''; diagnostic = '' }
        }
        else { $null }
        return [pscustomobject]@{
            schemaVersion = '2'; mode = 'performance'; stage = ''; runId = $strictRunId; status = $Status
            failure = $(if ($Status -eq 'Passed') { '' } else { 'strict contract failure' })
            scenario = '3D Generator + 2D Generator + Shader Effect + VideoPlayer + 2-input Blend + Feedback + ProgramOutput'
            codec = $Codec; corpusVersion = 'v1'; corpusFile = $(if ($Codec -eq 'Hap') { 'hap_fhd60.mov' } else { 'h264_fhd60.mp4' }); platform = 'WindowsPlayer'
            operatingSystem = 'Windows 10'; graphicsApi = 'Direct3D12'; graphicsDeviceName = 'NVIDIA RTX 3060'
        graphicsDeviceVersion = 'driver'; unityVersion = '6000.5.9f1'; packageVersion = '6000.5.9f1'
            buildId = 'build-id'; developmentBuild = $true; buildOptions = 'Development'; projectRoot = $strictArtifactRoot; projectRevision = '42'; seed = $strictRunId; fixtureMode = $false
            productionCompositionUsed = $true; productionCatalogUsed = $true
            renderPipeline = 'UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset'; warmupSeconds = 30; measureSeconds = 600
            timing = [pscustomobject]@{
                updateSamples = 2; measuredFrames = 2; presentedFrames = 2; timingAvailableFrames = 2; timingUnavailableFrames = 0; goodFrameRatio = 1
                averageCpuMilliseconds = 8; averageGpuMilliseconds = 7; maxCpuMilliseconds = 8; maxGpuMilliseconds = 7
                minimumProgramCadenceFps = 60; maxConsecutiveProgramMissing = 0; gcAllocatedBytes = 0
                gcCollectionCount0 = 0; gcCollectionCount1 = 0; gcCollectionCount2 = 0
                diagnosticIntervals = @(); previewQualitySamples = @($sample)
            }
            interactions = [pscustomobject]@{
                logicalControlUpdatesPerSecond = 120; presetTriggerIntervalSeconds = 10; measurementSeconds = 600
                logicalControlUpdates = 72000; expectedLogicalControlUpdates = 72000
                presetTriggerFires = 60; expectedPresetTriggerFires = 60
            }
            output = [pscustomobject]@{
                programWidth = 1920; programHeight = 1080; programFormat = 'R16G16B16A16_SFloat'; programTargetFps = 60; programState = 'Available'
                previewCount = 2; previewWidth = 640; previewHeight = 360; previewTargetFps = 30
                previews = @($preview1, $preview2); previewQualities = @('Stage0', 'Stage0')
            }
            resources = [pscustomobject]@{
                poolBudgetBytes = 1000; poolCurrentBytes = 100; poolLeasedBytes = 40; poolFreeBytes = 60; poolHighWaterBytes = 100
                sceneCount = 1; layerCount = 1; backendCount = 1; nativeContextCount = 1; activeOutputLeases = 3; poolEntryCount = 3
                endLeases = 0; endPoolEntryCount = 0; endActiveOutputLeases = 0; endSceneCount = 0; endLayerCount = 0; endBackendCount = 0; endNativeContextCount = 0
            }
            ownership = [pscustomobject]@{
                available = $true; runtimeDisposed = $false; sceneCount = 1; layerCount = 1; backendCount = 1; nativeContextCount = 1; activeOutputLeaseCount = 3
                program = [pscustomobject]@{ id = 'program'; targetKind = 'Program'; width = 1920; height = 1080; graphicsFormat = 'R16G16B16A16_SFloat'; targetFramesPerSecond = 60; frameNumber = 12 }
                previews = @(
                    [pscustomobject]@{ id = 'preview-1'; targetKind = 'Preview'; width = 640; height = 360; graphicsFormat = 'R8G8B8A8_UNorm'; targetFramesPerSecond = 30; frameNumber = 12 },
                    [pscustomobject]@{ id = 'preview-2'; targetKind = 'Preview'; width = 640; height = 360; graphicsFormat = 'R8G8B8A8_UNorm'; targetFramesPerSecond = 30; frameNumber = 12 }
                )
                texturePool = [pscustomobject]@{ budgetBytes = 1000; leasedBytes = 40; freeBytes = 60; highWaterBytes = 100; budgetWarningActive = $false; usageRatio = 0.1; entries = @() }
            }
            diagnostics = [pscustomobject]@{ faultedFrames = 0; fatalFrames = 0; holdingLastFrameFrames = 0; currentCodes = @(); historyCodes = @(); intervals = @() }
            nativePluginProbe = $nativeProbe; codecProbe = $codecProbe
            artifactWriteError = ''
        }
    }

    function Write-StrictArtifact {
        param([Parameter(Mandatory = $true)][object]$Artifact, [bool]$WriteLog = $true)
        [IO.File]::WriteAllText((Join-Path $strictArtifactRoot ($strictRunId + '.json')), ($Artifact | ConvertTo-Json -Depth 30))
        [IO.File]::WriteAllText((Join-Path $strictArtifactRoot ($strictRunId + '.txt')), "status=$($Artifact.status)`n")
        if ($WriteLog) { [IO.File]::WriteAllText($strictLogPath, "Player log for $strictRunId`n") }
        elseif (Test-Path -LiteralPath $strictLogPath) { Remove-Item -LiteralPath $strictLogPath -Force }
    }

    function Invoke-StrictValidation {
        param([int]$PlayerExitCode = 0, [bool]$FixtureMode = $false, [string]$Codec = 'h264', [switch]$RequireProductionRun)
        $arguments = @{
            Directory = $strictArtifactRoot; RunStartedUtc = $strictRunStartedUtc; PlayerExitCode = $PlayerExitCode
            ExpectedPlatform = 'windows'; ExpectedGraphicsApi = 'd3d12'; ExpectedCodec = $Codec; ExpectedMode = 'performance'
            ExpectedFixtureMode = $FixtureMode; ApplicationLogPath = $strictLogPath
        }
        if ($RequireProductionRun) { return Validate-HarnessArtifacts @arguments -RequireProductionRun }
        return Validate-HarnessArtifacts @arguments
    }

    $validArtifact = New-StrictPerformanceArtifact
    Write-StrictArtifact $validArtifact
    $result = Invoke-StrictValidation -RequireProductionRun
    Assert-Contract ($result.IsValid -and $result.Status -eq 'Passed') 'A complete production performance artifact must pass strict validation.'

    function Assert-StrictReject {
        param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][scriptblock]$Mutation)
        $candidate = New-StrictPerformanceArtifact
        & $Mutation $candidate
        Write-StrictArtifact $candidate
        $candidateResult = Invoke-StrictValidation -RequireProductionRun
        Assert-Contract (-not $candidateResult.IsValid) "$Name must be rejected by strict validation."
    }

    Assert-StrictReject 'schemaVersion mismatch' { param($x) $x.schemaVersion = '1' }
    Assert-StrictReject 'mode mismatch' { param($x) $x.mode = 'acceptance' }
    Assert-StrictReject 'platform mismatch' { param($x) $x.platform = 'OSXPlayer' }
    Assert-StrictReject 'graphics API mismatch' { param($x) $x.graphicsApi = 'Vulkan' }
    Assert-StrictReject 'codec mismatch' { param($x) $x.codec = 'Hap' }
    Assert-StrictReject 'fixture mode mismatch' { param($x) $x.fixtureMode = $true }
    Assert-StrictReject 'performance release build' { param($x) $x.developmentBuild = $false }
    Assert-StrictReject 'performance build-options mismatch' { param($x) $x.buildOptions = 'None' }
    Assert-StrictReject 'warm-up mismatch' { param($x) $x.warmupSeconds = 29 }
    Assert-StrictReject 'measurement duration mismatch' { param($x) $x.measureSeconds = 599 }
    Assert-StrictReject 'package metadata missing' { param($x) $x.packageVersion = '' }
    Assert-StrictReject 'project root missing' { param($x) $x.projectRoot = '' }
    Assert-StrictReject 'project root does not exist' { param($x) $x.projectRoot = (Join-Path $strictArtifactRoot 'does-not-exist') }
    Assert-StrictReject 'codec probe missing' { param($x) $x.codecProbe = $null }
    Assert-StrictReject 'codec probe backend mismatch' { param($x) $x.codecProbe.backend = 'HapVideoBackend' }
    Assert-StrictReject 'codec probe failed' { param($x) $x.codecProbe.passed = $false }
    Assert-StrictReject 'Presented timing count mismatch' { param($x) $x.timing.presentedFrames = 0; $x.timing.measuredFrames = 0 }
    Assert-StrictReject 'timing availability count mismatch' { param($x) $x.timing.timingUnavailableFrames = 1 }
    Assert-StrictReject 'no finite timing sample in passed artifact' { param($x) $x.timing.timingAvailableFrames = 0; $x.timing.timingUnavailableFrames = 2 }
    Assert-StrictReject 'timing unavailable' { param($x) $x.timing.averageCpuMilliseconds = 'unavailable' }
    Assert-StrictReject 'zero Presented CPU timing' { param($x) $x.timing.averageCpuMilliseconds = 0 }
    Assert-StrictReject 'zero Presented GPU timing' { param($x) $x.timing.averageGpuMilliseconds = 0 }
    Assert-StrictReject 'negative Presented CPU timing' { param($x) $x.timing.maxCpuMilliseconds = -0.001 }
    $slowCadenceArtifact = New-StrictPerformanceArtifact
    $slowCadenceArtifact.timing.minimumProgramCadenceFps = 58.999
    Write-StrictArtifact $slowCadenceArtifact
    $result = Invoke-StrictValidation -RequireProductionRun
    Assert-Contract ($result.IsValid -and $result.Status -eq 'Passed') 'A below-59 minimum cadence diagnostic must not be an artifact pass/fail gate.'
    Assert-StrictReject 'good frame ratio below threshold' { param($x) $x.timing.goodFrameRatio = 0.98 }
    Assert-StrictReject 'consecutive missing threshold' { param($x) $x.timing.maxConsecutiveProgramMissing = 3 }
    Assert-StrictReject 'Program FHD60 mismatch' { param($x) $x.output.programWidth = 1280 }
    Assert-StrictReject 'Program HDR format mismatch' { param($x) $x.output.programFormat = 'R8G8B8A8_UNorm' }
    Assert-StrictReject 'Ownership HDR format mismatch' { param($x) $x.ownership.program.graphicsFormat = 'R8G8B8A8_UNorm' }
    Assert-StrictReject 'required scenario mismatch' { param($x) $x.scenario = 'wrong scenario' }
    Assert-StrictReject 'short Preview quality coverage' { param($x) $x.timing.previewQualitySamples[0].sampleSeconds = 599.999 }
    Assert-StrictReject 'Preview count mismatch' { param($x) $x.output.previewCount = 1; $x.output.previews = @($x.output.previews[0]) }
    Assert-StrictReject 'Pool budget mismatch' { param($x) $x.resources.endLeases = 1 }
    Assert-StrictReject 'Fault diagnostic' { param($x) $x.diagnostics.faultedFrames = 1 }
    Assert-StrictReject 'Interaction count mismatch' { param($x) $x.interactions.logicalControlUpdates = 71999 }
    Assert-StrictReject 'Ownership snapshot missing' { param($x) $x.ownership.available = $false }

    $hapArtifact = New-StrictPerformanceArtifact 'Passed' 'Hap'
    Write-StrictArtifact $hapArtifact
    $result = Invoke-StrictValidation -Codec 'hap' -RequireProductionRun
    Assert-Contract ($result.IsValid -and $result.Status -eq 'Passed') 'A complete Hap performance artifact must pass strict validation.'
    $hapArtifact.nativePluginProbe = $null
    Write-StrictArtifact $hapArtifact
    $result = Invoke-StrictValidation -Codec 'hap' -RequireProductionRun
    Assert-Contract (-not $result.IsValid) 'Hap Passed artifacts must preserve the native plugin probe.'
    $hapArtifact = New-StrictPerformanceArtifact 'Passed' 'Hap'
    $hapArtifact.nativePluginProbe.passed = $false
    Write-StrictArtifact $hapArtifact
    $result = Invoke-StrictValidation -Codec 'hap' -RequireProductionRun
    Assert-Contract (-not $result.IsValid) 'Hap Passed artifacts must reject a failed native plugin probe.'

    $candidate = New-StrictPerformanceArtifact
    Write-StrictArtifact $candidate $false
    $result = Invoke-StrictValidation -RequireProductionRun
    Assert-Contract (-not $result.IsValid -and $result.Error -match 'application log') 'Missing Player application log must fail strict validation.'

    $candidate = New-StrictPerformanceArtifact
    Write-StrictArtifact $candidate
    [IO.File]::WriteAllText($strictLogPath, '')
    $result = Invoke-StrictValidation -RequireProductionRun
    Assert-Contract (-not $result.IsValid -and $result.Error -match 'application log') 'Empty Player application log must fail strict validation.'

    $candidate = New-StrictPerformanceArtifact
    Write-StrictArtifact $candidate
    [IO.File]::WriteAllText($strictLogPath, 'Font not found for path: NotoSans' + [Environment]::NewLine)
    $result = Invoke-StrictValidation -RequireProductionRun
    Assert-Contract (-not $result.IsValid -and $result.Error -match 'NotoSans') 'A Player run missing the required bundled NotoSans FontAsset must fail strict validation.'

    $failedArtifact = New-StrictPerformanceArtifact 'Failed'
    $failedArtifact.timing = $null; $failedArtifact.output = $null; $failedArtifact.resources = $null; $failedArtifact.interactions = $null
    Write-StrictArtifact $failedArtifact
    $result = Invoke-StrictValidation -PlayerExitCode 1
    Assert-Contract ($result.IsValid -and $result.Status -eq 'Failed') 'Failed artifacts must not be rejected for missing success-only metrics.'

    $failedArtifact = New-StrictPerformanceArtifact 'Failed'
    $failedArtifact.ownership = $null
    Write-StrictArtifact $failedArtifact
    $result = Invoke-StrictValidation -PlayerExitCode 1
    Assert-Contract (-not $result.IsValid) 'Runtime failures must preserve an ownership snapshot.'

    $failedArtifact = New-StrictPerformanceArtifact 'Failed'
    $failedArtifact.diagnostics = $null
    Write-StrictArtifact $failedArtifact
    $result = Invoke-StrictValidation -PlayerExitCode 1
    Assert-Contract (-not $result.IsValid) 'Runtime failures must preserve diagnostics.'

    $failedArtifact = New-StrictPerformanceArtifact 'Failed'
    $failedArtifact.codecProbe.path = ''; $failedArtifact.codecProbe.diagnostic = ''
    Write-StrictArtifact $failedArtifact
    $result = Invoke-StrictValidation -PlayerExitCode 1
    Assert-Contract (-not $result.IsValid) 'Failure artifacts must preserve codec probe path or diagnostic.'

    $failedHapArtifact = New-StrictPerformanceArtifact 'Failed' 'Hap'
    $failedHapArtifact.nativePluginProbe.path = ''; $failedHapArtifact.nativePluginProbe.diagnostic = ''
    Write-StrictArtifact $failedHapArtifact
    $result = Invoke-StrictValidation -PlayerExitCode 1 -Codec 'hap'
    Assert-Contract (-not $result.IsValid) 'Hap failure artifacts must preserve native probe path or diagnostic.'

    $environmentArtifact = New-StrictPerformanceArtifact 'EnvironmentFailed'
    $environmentArtifact.failure = 'ENVIRONMENT: corpus file is missing'
    $environmentArtifact.productionCompositionUsed = $false
    $environmentArtifact.productionCatalogUsed = $false
    $environmentArtifact.corpusVersion = ''
    $environmentArtifact.corpusFile = ''
    $environmentArtifact.ownership = [pscustomobject]@{ available = $false }
    $environmentArtifact.diagnostics = $null
    Write-StrictArtifact $environmentArtifact
    $result = Invoke-StrictValidation -PlayerExitCode 2
    Assert-Contract ($result.IsValid -and $result.Status -eq 'EnvironmentFailed') 'Valid EnvironmentFailed artifacts must preserve exit code 2.'
}
finally {
    if (Test-Path -LiteralPath $strictArtifactRoot) { Remove-Item -LiteralPath $strictArtifactRoot -Recurse -Force }
}

Write-Output 'Standalone harness tooling contract passed.'

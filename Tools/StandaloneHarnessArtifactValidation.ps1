function New-ArtifactValidationResult {
    param(
        [Parameter(Mandatory = $true)][bool]$IsValid,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Error,
        [string]$Status = ''
    )
    return [pscustomobject]@{ IsValid = $IsValid; Error = $Error; Status = $Status }
}

function Test-ArtifactNonEmpty {
    param([AllowNull()][object]$Value)
    return $null -ne $Value -and -not [string]::IsNullOrWhiteSpace([string]$Value)
}

function Test-ArtifactFiniteNumber {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return $false }
    try {
        $number = [double]$Value
        return -not [double]::IsNaN($number) -and -not [double]::IsInfinity($number)
    }
    catch { return $false }
}

function Test-ArtifactNonNegativeNumber {
    param([AllowNull()][object]$Value)
    if (-not (Test-ArtifactFiniteNumber $Value)) { return $false }
    return [double]$Value -ge 0d
}

function Test-ArtifactPositiveNumber {
    param([AllowNull()][object]$Value)
    if (-not (Test-ArtifactFiniteNumber $Value)) { return $false }
    return [double]$Value -gt 0d
}

function Test-ArtifactPathExists {
    param([AllowNull()][object]$Value)
    if (-not (Test-ArtifactNonEmpty $Value)) { return $false }
    try { return Test-Path -LiteralPath ([IO.Path]::GetFullPath([string]$Value)) -PathType Container }
    catch { return $false }
}

function Get-ArtifactArray {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return @() }
    return @($Value)
}

function Get-ExpectedPlatformValue {
    param([AllowNull()][string]$Platform)
    $normalized = if ($null -eq $Platform) { '' } else { $Platform.ToLowerInvariant() }
    switch ($normalized) {
        'windows' { return 'WindowsPlayer' }
        'macos' { return 'OSXPlayer' }
        default { return $Platform }
    }
}

function Get-ExpectedGraphicsApiValue {
    param([AllowNull()][string]$GraphicsApi)
    $normalized = if ($null -eq $GraphicsApi) { '' } else { $GraphicsApi.ToLowerInvariant() }
    switch ($normalized) {
        'd3d12' { return 'Direct3D12' }
        'vulkan' { return 'Vulkan' }
        'metal' { return 'Metal' }
        default { return $GraphicsApi }
    }
}

function Get-ExpectedCodecValue {
    param([AllowNull()][string]$Codec)
    $normalized = if ($null -eq $Codec) { '' } else { $Codec.ToLowerInvariant() }
    switch ($normalized) {
        'h264' { return 'H264' }
        'hap' { return 'Hap' }
        default { return $Codec }
    }
}

function Test-ExpectedString {
    param(
        [AllowNull()][object]$Actual,
        [AllowNull()][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Name
    )
    if ([string]::IsNullOrWhiteSpace($Expected)) { return '' }
    if ([string]$Actual -ne $Expected) { return "$Name mismatch: expected '$Expected', actual '$Actual'." }
    return ''
}

function Test-PreviewDescriptor {
    param([AllowNull()][object]$Preview)
    if ($null -eq $Preview -or -not (Test-ArtifactNonEmpty $Preview.id) -or -not (Test-ArtifactNonEmpty $Preview.format)) { return $false }
    try { $stage = [int]$Preview.qualityStage } catch { return $false }
    $widths = @(640, 480, 320, 160, 160)
    $heights = @(360, 270, 180, 90, 90)
    $fps = @(30, 30, 20, 10, 5)
    if ($stage -lt 0 -or $stage -ge $widths.Count) { return $false }
    return [string]$Preview.quality -eq "Stage$stage" -and
        [int]$Preview.width -eq $widths[$stage] -and
        [int]$Preview.height -eq $heights[$stage] -and
        [int]$Preview.targetFramesPerSecond -eq $fps[$stage]
}

function Test-PreviewCollection {
    param([AllowNull()][object]$Previews)
    $items = Get-ArtifactArray $Previews
    if ($items.Count -ne 2) { return 'Preview descriptor count must be exactly 2.' }
    if ($items | Where-Object { -not (Test-PreviewDescriptor $_) }) { return 'A Preview descriptor is missing or has an unknown quality stage.' }
    $ids = @($items | ForEach-Object { [string]$_.id } | Select-Object -Unique)
    if ($ids.Count -ne 2) { return 'Preview descriptor IDs must be distinct.' }
    return ''
}

function Test-OwnershipPreviewCollection {
    param([AllowNull()][object]$Previews)
    $items = Get-ArtifactArray $Previews
    if ($items.Count -ne 2) { return 'Ownership Preview count must be exactly 2.' }
    $widths = @(640, 480, 320, 160, 160)
    $heights = @(360, 270, 180, 90, 90)
    $fps = @(30, 30, 20, 10, 5)
    foreach ($item in $items) {
        if ($null -eq $item -or -not (Test-ArtifactNonEmpty $item.id) -or -not (Test-ArtifactNonEmpty $item.graphicsFormat)) { return 'Ownership Preview descriptor is incomplete.' }
        $valid = $false
        for ($index = 0; $index -lt $widths.Count; $index++) {
            if ([int]$item.width -eq $widths[$index] -and [int]$item.height -eq $heights[$index] -and [int]$item.targetFramesPerSecond -eq $fps[$index]) { $valid = $true; break }
        }
        if (-not $valid) { return 'Ownership Preview descriptor has an invalid quality stage.' }
    }
    $ids = @($items | ForEach-Object { [string]$_.id } | Select-Object -Unique)
    if ($ids.Count -ne 2) { return 'Ownership Preview IDs must be distinct.' }
    return ''
}

function Test-PerformanceArtifact {
    param(
        [Parameter(Mandatory = $true)][object]$Json,
        [Parameter(Mandatory = $true)][bool]$FixtureMode,
        [Parameter(Mandatory = $true)][bool]$RequireProductionRun,
        [Parameter(Mandatory = $true)][string]$ExpectedCodec
    )

    if ([string]$Json.schemaVersion -ne '2') { return "schemaVersion must be '2'." }
    if ([string]$Json.mode -ne 'performance') { return "mode must be 'performance'." }
    if ($Json.fixtureMode -ne $FixtureMode) { return "fixtureMode mismatch: expected '$FixtureMode', actual '$($Json.fixtureMode)'." }

    $expectedScenario = '3D Generator + 2D Generator + Shader Effect + VideoPlayer + 2-input Blend + Feedback + ProgramOutput'
    if ([string]$Json.scenario -ne $expectedScenario) { return 'Performance artifact scenario does not match the required Program path.' }

    $requiredMetadata = @('packageVersion', 'buildId', 'unityVersion', 'operatingSystem', 'graphicsDeviceName', 'graphicsDeviceVersion', 'platform', 'graphicsApi', 'codec', 'scenario', 'seed', 'buildOptions')
    foreach ($name in $requiredMetadata) {
        if (-not (Test-ArtifactNonEmpty $Json.$name)) { return "Required artifact metadata '$name' is missing." }
    }
    if (-not [bool]$Json.developmentBuild -or [string]$Json.buildOptions -ne 'Development') {
        return 'Performance artifacts must come from the Development Player used for all-thread GC allocation measurement.'
    }
    if ($RequireProductionRun) {
        if ([double]$Json.warmupSeconds -ne 30d -or [double]$Json.measureSeconds -ne 600d) {
            return 'Production performance artifacts must report warmupSeconds=30 and measureSeconds=600.'
        }
        if ($Json.fixtureMode) { return 'A production performance artifact cannot be fixtureMode=true.' }
    }

    if ($Json.status -eq 'Passed') {
        if (Test-ArtifactNonEmpty $Json.failure) { return 'Passed artifact must not preserve a failure reason.' }
        if (-not [bool]$Json.productionCompositionUsed -or -not [bool]$Json.productionCatalogUsed) {
            return 'Passed artifact must identify the production composition and catalog.'
        }
        foreach ($name in @('projectRevision', 'renderPipeline', 'corpusVersion', 'corpusFile')) {
            if (-not (Test-ArtifactNonEmpty $Json.$name)) { return "Passed artifact metadata '$name' is missing." }
        }
        if (-not (Test-ArtifactPathExists $Json.projectRoot)) { return 'Passed artifact projectRoot is missing or does not exist.' }

        $codecProbe = $Json.codecProbe
        if ($null -eq $codecProbe -or -not (Test-ArtifactNonEmpty $codecProbe.path) -or -not [bool]$codecProbe.passed -or -not [bool]$codecProbe.supported) {
            return 'Passed artifact codecProbe is missing, unsupported, or failed.'
        }
        if (-not (Test-ArtifactNonEmpty $codecProbe.backend) -or -not (Test-ArtifactNonEmpty $codecProbe.codec) -or -not (Test-ArtifactFiniteNumber $codecProbe.durationSeconds)) {
            return 'Passed artifact codecProbe metadata is incomplete.'
        }
        if ([string]$ExpectedCodec -eq 'H264') {
            if ([string]$codecProbe.backend -ne 'UnityVideoBackend' -or [string]$codecProbe.codec -ne 'H264') { return 'H264 codecProbe must use UnityVideoBackend and report H264.' }
        }
        elseif ([string]$ExpectedCodec -eq 'Hap') {
            if ([string]$codecProbe.backend -ne 'HapVideoBackend' -or [string]$codecProbe.codec -notin @('Hap1', 'Hap5', 'HapY', 'HapM')) { return 'Hap codecProbe must use HapVideoBackend and report a guaranteed Hap codec.' }
            $nativeProbe = $Json.nativePluginProbe
            if ($null -eq $nativeProbe -or -not (Test-ArtifactNonEmpty $nativeProbe.path) -or -not [bool]$nativeProbe.supportedPlatform -or -not [bool]$nativeProbe.passed -or [uint32]$nativeProbe.abiVersion -le 0 -or [uint32]$nativeProbe.capabilities -eq 0) {
                return 'Passed Hap artifact nativePluginProbe is missing, unsupported, or incomplete.'
            }
        }
        else { return "Unsupported expected performance codec '$ExpectedCodec'." }

        $timing = $Json.timing
        if ($null -eq $timing) { return 'Passed artifact timing is missing.' }
        foreach ($name in @('goodFrameRatio', 'minimumProgramCadenceFps')) {
            if (-not (Test-ArtifactNonNegativeNumber $timing.$name)) { return "Timing field '$name' is unavailable or negative." }
        }
        foreach ($name in @('averageCpuMilliseconds', 'averageGpuMilliseconds', 'maxCpuMilliseconds', 'maxGpuMilliseconds')) {
            if (-not (Test-ArtifactPositiveNumber $timing.$name)) { return "Timing field '$name' is unavailable or not positive." }
        }
        if ([double]$timing.goodFrameRatio -lt 0.99d -or [double]$timing.goodFrameRatio -gt 1d) { return 'goodFrameRatio must be at least 0.99 and no greater than 1.' }
        if ([int]$timing.presentedFrames -le 0 -or [int]$timing.measuredFrames -le 0) { return 'Presented timing frame count must be greater than zero.' }
        if ([int]$timing.presentedFrames -ne [int]$timing.measuredFrames) { return 'measuredFrames and presentedFrames must agree.' }
        if ([int]$timing.timingAvailableFrames -lt 0 -or [int]$timing.timingUnavailableFrames -lt 0) { return 'Timing availability counts cannot be negative.' }
        if ([int]$timing.timingAvailableFrames + [int]$timing.timingUnavailableFrames -ne [int]$timing.presentedFrames) {
            return 'Timing availability counts must account for every Presented Program frame.'
        }
        if ([int]$timing.timingAvailableFrames -le 0) { return 'Passed artifact must expose at least one finite CPU/GPU timing sample.' }
        if ([int]$timing.updateSamples -lt [int]$timing.presentedFrames) { return 'updateSamples cannot be less than presentedFrames.' }
        if ([int]$timing.maxConsecutiveProgramMissing -lt 0 -or [int]$timing.maxConsecutiveProgramMissing -ge 3) { return 'maxConsecutiveProgramMissing must be less than 3.' }

        $output = $Json.output
        if ($null -eq $output) { return 'Passed artifact output is missing.' }
        if ([int]$output.programWidth -ne 1920 -or [int]$output.programHeight -ne 1080 -or [int]$output.programTargetFps -ne 60) { return 'Passed Program output must remain 1920x1080 at 60fps.' }
        if ([string]$output.programFormat -ne 'R16G16B16A16_SFloat') { return 'Passed Program GraphicsFormat must be R16G16B16A16_SFloat for the HDR performance harness.' }
        if ([int]$output.previewCount -ne 2) { return 'Passed artifact must contain exactly two Preview outputs.' }
        $previewError = Test-PreviewCollection $output.previews
        if ($previewError) { return $previewError }
        $outputPreviews = Get-ArtifactArray $output.previews
        if ([int]$output.previewWidth -ne [int]$outputPreviews[0].width -or [int]$output.previewHeight -ne [int]$outputPreviews[0].height -or [int]$output.previewTargetFps -ne [int]$outputPreviews[0].targetFramesPerSecond) {
            return 'Output Preview summary does not match its descriptors.'
        }
        if ((Get-ArtifactArray $output.previewQualities).Count -ne 2) { return 'Passed artifact must record two Preview quality values.' }
        $qualitySamples = Get-ArtifactArray $timing.previewQualitySamples
        if ($qualitySamples.Count -eq 0) { return 'Passed artifact has no Preview quality samples.' }
        $previousSampleSeconds = -1d
        foreach ($sample in $qualitySamples) {
            if ($null -eq $sample -or -not (Test-ArtifactNonNegativeNumber $sample.sampleSeconds)) { return 'Preview quality sampleSeconds must be finite and non-negative.' }
            if ([double]$sample.sampleSeconds -lt $previousSampleSeconds) { return 'Preview quality sampleSeconds must be monotonic.' }
            $sampleError = Test-PreviewCollection $sample.previews
            if ($sampleError) { return "Preview quality sample is invalid: $sampleError" }
            $previousSampleSeconds = [double]$sample.sampleSeconds
        }
        if (-not (Test-ArtifactNonNegativeNumber $Json.measureSeconds)) { return 'measureSeconds must be finite and non-negative.' }
        if ($previousSampleSeconds -lt [double]$Json.measureSeconds) {
            return "Preview quality samples do not cover the complete measurement interval: last=$previousSampleSeconds, required=$($Json.measureSeconds)."
        }

        $resources = $Json.resources
        if ($null -eq $resources) { return 'Passed artifact resources are missing.' }
        foreach ($name in @('poolBudgetBytes', 'poolCurrentBytes', 'poolLeasedBytes', 'poolFreeBytes', 'poolHighWaterBytes')) {
            if (-not (Test-ArtifactNonNegativeNumber $resources.$name)) { return "Resource field '$name' is unavailable or negative." }
        }
        if ([int64]$resources.poolBudgetBytes -le 0 -or [int64]$resources.poolCurrentBytes -gt [int64]$resources.poolBudgetBytes -or [int64]$resources.poolLeasedBytes + [int64]$resources.poolFreeBytes -ne [int64]$resources.poolCurrentBytes -or [int64]$resources.poolHighWaterBytes -gt [int64]$resources.poolBudgetBytes) {
            return 'Texture pool usage is inconsistent with its budget.'
        }
        foreach ($name in @('endLeases', 'endPoolEntryCount', 'endActiveOutputLeases', 'endSceneCount', 'endLayerCount', 'endBackendCount', 'endNativeContextCount')) {
            if ([int64]$resources.$name -ne 0) { return "Teardown resource '$name' was not zero." }
        }

        $ownership = $Json.ownership
        if ($null -eq $ownership -or -not [bool]$ownership.available) { return 'Passed artifact ownership snapshot is unavailable.' }
        if ($null -eq $ownership.program -or [string]$ownership.program.graphicsFormat -ne 'R16G16B16A16_SFloat' -or [int]$ownership.program.width -ne 1920 -or [int]$ownership.program.height -ne 1080 -or [int]$ownership.program.targetFramesPerSecond -ne 60) { return 'Ownership Program descriptor is invalid.' }
        $ownershipPreviewError = Test-OwnershipPreviewCollection $ownership.previews
        if ($ownershipPreviewError) { return "Ownership Preview snapshot is invalid: $ownershipPreviewError" }
        if ($null -eq $ownership.texturePool -or [bool]$ownership.texturePool.budgetWarningActive) { return 'Ownership texture pool snapshot is missing or warned.' }
        if ([int64]$ownership.texturePool.budgetBytes -ne [int64]$resources.poolBudgetBytes -or [int64]$ownership.texturePool.leasedBytes + [int64]$ownership.texturePool.freeBytes -ne [int64]$resources.poolCurrentBytes -or [int64]$ownership.texturePool.highWaterBytes -gt [int64]$resources.poolBudgetBytes) { return 'Ownership and resource pool usage disagree.' }

        $diagnostics = $Json.diagnostics
        if ($null -eq $diagnostics -or [int]$diagnostics.faultedFrames -ne 0 -or [int]$diagnostics.fatalFrames -ne 0) { return 'Passed artifact contains Faulted or Fatal diagnostics.' }
        $interactions = $Json.interactions
        if ($null -eq $interactions -or [double]$interactions.logicalControlUpdatesPerSecond -ne 120d -or [double]$interactions.presetTriggerIntervalSeconds -ne 10d -or [double]$interactions.measurementSeconds -ne [double]$Json.measureSeconds) {
            return 'Interaction schedule metadata is invalid.'
        }
        if ([int]$interactions.expectedLogicalControlUpdates -ne [math]::Floor([double]$interactions.measurementSeconds * 120d) -or [int]$interactions.expectedPresetTriggerFires -ne [math]::Floor([double]$interactions.measurementSeconds / 10d) -or [int]$interactions.logicalControlUpdates -lt [int]$interactions.expectedLogicalControlUpdates -or [int]$interactions.presetTriggerFires -lt [int]$interactions.expectedPresetTriggerFires) {
            return 'Interaction counts are below their expected 120Hz/10-second schedule.'
        }
    }
    else {
        if (-not (Test-ArtifactNonEmpty $Json.failure)) { return 'Failed artifact must preserve a failure reason.' }
        if ($null -eq $Json.ownership) { return 'Failure artifact must preserve an ownership snapshot, even when it is unavailable.' }
        if ($Json.productionCompositionUsed -and $null -eq $Json.diagnostics) { return 'A runtime failure must preserve diagnostics.' }
        if ($null -eq $Json.codecProbe -or (-not (Test-ArtifactNonEmpty $Json.codecProbe.path) -and -not (Test-ArtifactNonEmpty $Json.codecProbe.diagnostic))) {
            return 'Failure artifact must preserve codec probe path or diagnostic.'
        }
        if ([string]$ExpectedCodec -eq 'Hap' -and ($null -eq $Json.nativePluginProbe -or (-not (Test-ArtifactNonEmpty $Json.nativePluginProbe.path) -and -not (Test-ArtifactNonEmpty $Json.nativePluginProbe.diagnostic)))) {
            return 'Hap failure artifact must preserve native plugin probe path or diagnostic.'
        }
    }
    return ''
}

function Validate-HarnessArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][DateTime]$RunStartedUtc,
        [Parameter(Mandatory = $true)][int]$PlayerExitCode,
        [string]$ExpectedPlatform = '',
        [string]$ExpectedGraphicsApi = '',
        [string]$ExpectedCodec = '',
        [string]$ExpectedMode = '',
        [Nullable[bool]]$ExpectedFixtureMode = $null,
        [switch]$RequireProductionRun,
        [string]$ApplicationLogPath = ''
    )

    $expectedStatus = switch ($PlayerExitCode) {
        0 { 'Passed'; break }
        1 { 'Failed'; break }
        2 { 'EnvironmentFailed'; break }
        default { return New-ArtifactValidationResult $false "Player exit code must be 0, 1, or 2, not $PlayerExitCode." }
    }
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        return New-ArtifactValidationResult $false "Artifact directory does not exist: $Directory"
    }

    $freshFiles = @(Get-ChildItem -LiteralPath $Directory -File -ErrorAction Stop | Where-Object { $_.LastWriteTimeUtc -ge $RunStartedUtc })
    $jsonFiles = @($freshFiles | Where-Object { $_.Extension -ieq '.json' })
    $textFiles = @($freshFiles | Where-Object { $_.Extension -ieq '.txt' })
    if ($jsonFiles.Count -ne 1 -or $textFiles.Count -ne 1) {
        return New-ArtifactValidationResult $false "Expected exactly one fresh JSON and TXT artifact, found $($jsonFiles.Count) JSON and $($textFiles.Count) TXT."
    }

    $jsonFile = $jsonFiles[0]
    $textFile = $textFiles[0]
    try {
        $json = [IO.File]::ReadAllText($jsonFile.FullName) | ConvertFrom-Json
        $text = [IO.File]::ReadAllText($textFile.FullName)
    }
    catch {
        return New-ArtifactValidationResult $false "Could not parse the fresh Harness artifact pair: $($_.Exception.Message)"
    }
    if ($null -eq $json) { return New-ArtifactValidationResult $false 'Harness JSON artifact parsed to null.' }

    $jsonStatus = [string]$json.status
    # HarnessArtifactWriter uses Environment.NewLine.  Accept a complete
    # LF, CRLF, or EOF-terminated line while keeping status parsing strict.
    $statusMatch = [regex]::Match($text, '(?m)^status=(?<status>[^\r\n]*)(?:\r?\n|\z)')
    if (-not $statusMatch.Success) { return New-ArtifactValidationResult $false 'Harness TXT artifact has no parseable status line.' }
    $textStatus = [string]$statusMatch.Groups['status'].Value
    if ([string]::IsNullOrWhiteSpace($jsonStatus) -or $jsonStatus -ne $textStatus) {
        return New-ArtifactValidationResult $false "JSON/TXT artifact status mismatch: JSON='$jsonStatus', TXT='$textStatus'."
    }
    if ($jsonStatus -notin @('Passed', 'Failed', 'EnvironmentFailed')) {
        return New-ArtifactValidationResult $false "Unknown Harness artifact status '$jsonStatus'."
    }
    if ($jsonStatus -ne $expectedStatus) {
        return New-ArtifactValidationResult $false "Player exit code $PlayerExitCode requires status '$expectedStatus', but artifact status is '$jsonStatus'."
    }

    $runId = [string]$json.runId
    $jsonBase = [IO.Path]::GetFileNameWithoutExtension($jsonFile.Name)
    $textBase = [IO.Path]::GetFileNameWithoutExtension($textFile.Name)
    if ([string]::IsNullOrWhiteSpace($runId) -or $runId -ne $jsonBase -or $runId -ne $textBase) {
        return New-ArtifactValidationResult $false 'Artifact runId does not match the JSON/TXT filenames.'
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$json.artifactWriteError)) {
        return New-ArtifactValidationResult $false "Harness artifactWriteError is present: $([string]$json.artifactWriteError)"
    }

    if (-not [string]::IsNullOrWhiteSpace($ApplicationLogPath)) {
        $logPath = [IO.Path]::GetFullPath($ApplicationLogPath)
        if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) { return New-ArtifactValidationResult $false "Player application log is missing: $logPath" }
        $logInfo = Get-Item -LiteralPath $logPath -ErrorAction Stop
        if ($logInfo.LastWriteTimeUtc -lt $RunStartedUtc) { return New-ArtifactValidationResult $false "Player application log is stale: $logPath" }
        if ($logInfo.Length -le 0) { return New-ArtifactValidationResult $false "Player application log is empty: $logPath" }
        if (Select-String -LiteralPath $logPath -SimpleMatch 'Font not found for path: NotoSans' -Quiet) {
            return New-ArtifactValidationResult $false "Player application log reports that the required bundled NotoSans FontAsset could not be resolved: $logPath"
        }
    }

    $strictPerformance = [string]$ExpectedMode -eq 'performance'
    if ($strictPerformance) {
        $expectedPlatformValue = Get-ExpectedPlatformValue $ExpectedPlatform
        $expectedGraphicsApiValue = Get-ExpectedGraphicsApiValue $ExpectedGraphicsApi
        $expectedCodecValue = Get-ExpectedCodecValue $ExpectedCodec
        foreach ($pair in @(
            @{ Actual = $json.platform; Expected = $expectedPlatformValue; Name = 'platform' },
            @{ Actual = $json.graphicsApi; Expected = $expectedGraphicsApiValue; Name = 'graphicsApi' },
            @{ Actual = $json.codec; Expected = $expectedCodecValue; Name = 'codec' }
        )) {
            $mismatch = Test-ExpectedString $pair.Actual $pair.Expected $pair.Name
            if ($mismatch) { return New-ArtifactValidationResult $false $mismatch }
        }
        if ($null -eq $ExpectedFixtureMode) { return New-ArtifactValidationResult $false 'ExpectedFixtureMode is required for performance artifact validation.' }
        try { $performanceFailure = Test-PerformanceArtifact $json ([bool]$ExpectedFixtureMode) ([bool]$RequireProductionRun) $expectedCodecValue }
        catch { return New-ArtifactValidationResult $false "Performance artifact fields are invalid: $($_.Exception.Message)" $jsonStatus }
        if ($performanceFailure) { return New-ArtifactValidationResult $false $performanceFailure $jsonStatus }
    }
    return New-ArtifactValidationResult $true '' $jsonStatus
}

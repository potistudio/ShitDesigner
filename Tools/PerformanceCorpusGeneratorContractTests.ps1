[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$generatorPath = Join-Path $root 'Tools/GeneratePerformanceCorpus.ps1'

function Assert-Contract {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (-not $Condition) { throw "Performance corpus generator contract failed: $Message" }
}

Assert-Contract (Test-Path -LiteralPath $generatorPath -PathType Leaf) 'Generator script must exist.'
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile($generatorPath, [ref]$tokens, [ref]$errors) | Out-Null
Assert-Contract ($null -eq $errors -or $errors.Count -eq 0) 'Generator script must parse without PowerShell errors.'

$source = [IO.File]::ReadAllText($generatorPath)
foreach ($required in @(
    '-VerifyOnly',
    'ffmpeg',
    'ffprobe',
    'ProbeSmoke.csproj',
    '--hash',
    '1920x1080',
    'rate=60',
    'h264_fhd60.mp4',
    'hap_fhd60.mov',
    'codec = ''H264''',
    'codec = ''Hap''',
    'Assets/StreamingAssets',
    'TestResults/PerformanceCorpus'
)) {
    Assert-Contract ($source.IndexOf($required, [StringComparison]::OrdinalIgnoreCase) -ge 0) "Generator must retain required contract token: $required"
}

$outside = Join-Path ([IO.Path]::GetTempPath()) ('ShitDesignerCorpusContract-' + [Guid]::NewGuid().ToString('N'))
$inside = Join-Path $root 'Assets/StreamingAssets/PerformanceCorpus-contract-probe'
try {
    # VerifyOnly is intentionally exercised against an empty external root:
    # a missing corpus is an explicit verification failure, never a skip.
    New-Item -ItemType Directory -Force -Path $outside | Out-Null
    $verifyFailed = $false
    try {
        $output = @(& $generatorPath -OutputRoot $outside -VerifyOnly 2>&1)
    }
    catch {
        $verifyFailed = $true
        $output = @($_)
    }
    Assert-Contract $verifyFailed 'VerifyOnly must fail when the manifest is absent.'
    Assert-Contract (($output -join "`n") -match '(?i)manifest is missing') 'VerifyOnly failure must identify the missing manifest.'

    $blocked = $false
    try {
        & $generatorPath -OutputRoot $inside -VerifyOnly 2>&1 | Out-Null
    }
    catch {
        $blocked = $true
    }
    Assert-Contract $blocked 'Generator must reject output beneath Assets/StreamingAssets.'
}
finally {
    if (Test-Path -LiteralPath $outside) { Remove-Item -LiteralPath $outside -Recurse -Force }
}

Write-Output 'Performance corpus generator contract passed.'

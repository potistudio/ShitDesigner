function ConvertTo-ProcessArgument {
    param([AllowNull()][object]$Value)

    $text = if ($null -eq $Value) { '' } else { [string]$Value }
    if ($text.Length -eq 0) { return '""' }
    if ($text -notmatch '[\s"]') { return $text }

    # Start-Process joins ArgumentList values into one Windows command line.
    # Apply the CommandLineToArgvW quoting rule, including doubled trailing
    # backslashes and escaped embedded quotes.
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($character in $text.ToCharArray()) {
        if ($character -eq [char]'\') {
            $backslashes++
            continue
        }
        if ($character -eq [char]'"') {
            for ($i = 0; $i -lt ($backslashes * 2 + 1); $i++) { [void]$builder.Append('\') }
            [void]$builder.Append('"')
            $backslashes = 0
            continue
        }
        for ($i = 0; $i -lt $backslashes; $i++) { [void]$builder.Append('\') }
        $backslashes = 0
        [void]$builder.Append($character)
    }
    for ($i = 0; $i -lt ($backslashes * 2); $i++) { [void]$builder.Append('\') }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-RootProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [ValidateSet('Hidden', 'Normal')][string]$WindowStyle = 'Hidden'
    )

    $quotedArguments = @($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ })
    $startParameters = @{
        FilePath     = $FilePath
        ArgumentList = $quotedArguments
        PassThru     = $true
    }
    if ($env:OS -eq 'Windows_NT') {
        $startParameters.WindowStyle = $WindowStyle
    }

    $process = Start-Process @startParameters
    try {
        # Waiting on the returned root process makes Application.Quit(exitCode)
        # observable without using the Start-Process wait switch.
        $process.WaitForExit()
        return [int]$process.ExitCode
    }
    finally {
        $process.Dispose()
    }
}

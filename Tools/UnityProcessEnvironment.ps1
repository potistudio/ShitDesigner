function Ensure-UnityProcessEnvironment {
    # Unity Package Manager v9.26.1 reads ALLUSERSPROFILE while resolving its
    # deprecated global config root on Windows. Codex-launched processes can
    # omit it even when ProgramData is available. Set the process environment
    # only so child Unity processes inherit the fallback without changing User
    # or Machine environment variables.
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ALLUSERSPROFILE)) {
        return
    }

    if ([string]::IsNullOrWhiteSpace($env:ProgramData) -or -not (Test-Path -LiteralPath $env:ProgramData -PathType Container)) {
        throw 'Cannot launch Unity: ALLUSERSPROFILE is empty and ProgramData is not a valid directory.'
    }

    $env:ALLUSERSPROFILE = $env:ProgramData
}

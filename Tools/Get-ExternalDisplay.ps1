<#
.SYNOPSIS
Lists monitors detected by Windows and classifies internal and external displays.

.DESCRIPTION
Reads the WmiMonitorID and WmiMonitorConnectionParams classes. The script does
not enable, disable, or reconfigure any display. Its object output is suitable
for filtering and automation; -AsJson provides a stable JSON array.

.EXAMPLE
.\Get-ExternalDisplay.ps1

.EXAMPLE
.\Get-ExternalDisplay.ps1 -ExternalOnly -AsJson

.EXAMPLE
.\Get-ExternalDisplay.ps1 -ExternalOnly -FailWhenMissing
#>
[CmdletBinding()]
param(
    [switch]$ExternalOnly,
    [switch]$AsJson,
    [switch]$FailWhenMissing
)

$ErrorActionPreference = 'Stop'

function ConvertFrom-WmiCharArray {
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) { return $null }
    $characters = foreach ($code in $Value) {
        if ([int]$code -ne 0) { [char][int]$code }
    }
    $text = -join $characters
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text.Trim()
}

function Get-ConnectionInfo {
    param([int]$Technology)

    # WmiMonitorConnectionParams.VideoOutputTechnology follows the
    # D3DKMDT_VIDEO_OUTPUT_TECHNOLOGY values used by Windows display drivers.
    $connections = @{
        -2 = @{ Name = 'Uninitialized'; Kind = 'Unknown' }
        -1 = @{ Name = 'Other'; Kind = 'External' }
         0 = @{ Name = 'VGA'; Kind = 'External' }
         1 = @{ Name = 'S-Video'; Kind = 'External' }
         2 = @{ Name = 'Composite Video'; Kind = 'External' }
         3 = @{ Name = 'Component Video'; Kind = 'External' }
         4 = @{ Name = 'DVI'; Kind = 'External' }
         5 = @{ Name = 'HDMI'; Kind = 'External' }
         6 = @{ Name = 'LVDS'; Kind = 'Internal' }
         8 = @{ Name = 'D-JPN'; Kind = 'External' }
         9 = @{ Name = 'SDI'; Kind = 'External' }
        10 = @{ Name = 'DisplayPort'; Kind = 'External' }
        11 = @{ Name = 'Embedded DisplayPort'; Kind = 'Internal' }
        12 = @{ Name = 'UDI'; Kind = 'External' }
        13 = @{ Name = 'Embedded UDI'; Kind = 'Internal' }
        14 = @{ Name = 'SDTV Dongle'; Kind = 'External' }
        15 = @{ Name = 'Miracast'; Kind = 'External' }
        16 = @{ Name = 'Indirect Wired'; Kind = 'External' }
        -2147483648 = @{ Name = 'Internal'; Kind = 'Internal' }
    }

    if ($connections.ContainsKey($Technology)) { return $connections[$Technology] }
    return @{ Name = "Unknown ($Technology)"; Kind = 'Unknown' }
}

function Get-ExternalDisplay {
    [CmdletBinding()]
    param([switch]$OnlyExternal)

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'Get-ExternalDisplay supports Windows only.'
    }

    $monitorIds = @(Get-CimInstance -Namespace 'root/wmi' -ClassName 'WmiMonitorID')
    $connectionByInstance = @{}
    foreach ($connection in @(Get-CimInstance -Namespace 'root/wmi' -ClassName 'WmiMonitorConnectionParams')) {
        $connectionByInstance[[string]$connection.InstanceName] = $connection
    }

    $results = foreach ($monitor in $monitorIds) {
        $instanceName = [string]$monitor.InstanceName
        $connection = $connectionByInstance[$instanceName]
        $technology = if ($null -eq $connection) {
            -2
        }
        else {
            $rawTechnology = [uint32]$connection.VideoOutputTechnology
            [BitConverter]::ToInt32([BitConverter]::GetBytes($rawTechnology), 0)
        }
        $connectionInfo = Get-ConnectionInfo -Technology $technology
        $kind = [string]$connectionInfo.Kind

        [pscustomobject]@{
            FriendlyName         = ConvertFrom-WmiCharArray $monitor.UserFriendlyName
            Manufacturer         = ConvertFrom-WmiCharArray $monitor.ManufacturerName
            SerialNumber         = ConvertFrom-WmiCharArray $monitor.SerialNumberID
            Active               = [bool]$monitor.Active
            Connection           = [string]$connectionInfo.Name
            ConnectionTechnology = $technology
            Kind                 = $kind
            IsExternal           = $kind -eq 'External'
            InstanceName         = $instanceName
        }
    }

    if ($OnlyExternal) { return @($results | Where-Object IsExternal) }
    return @($results)
}

$displays = @(Get-ExternalDisplay -OnlyExternal:$ExternalOnly)

if ($AsJson) {
    ConvertTo-Json -InputObject @($displays) -Depth 3
}
else {
    $displays
}

if ($FailWhenMissing -and -not ($displays | Where-Object IsExternal)) {
    Write-Error 'No external display was detected.' -ErrorAction Continue
    exit 2
}

<#
.SYNOPSIS
    Stops the HELIOS development stack. Pass -Volumes to also drop MySQL and Redis data.
#>
[CmdletBinding()]
param([switch]$Volumes)

$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..\deploy\compose')
try {
    if ($Volumes) { docker compose down -v } else { docker compose down }
}
finally {
    Pop-Location
}

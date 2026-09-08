<#
.SYNOPSIS
    Restores, builds and tests the solution the same way CI does.
#>
[CmdletBinding()]
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'

dotnet restore (Join-Path $root 'Helios.sln')
dotnet build   (Join-Path $root 'Helios.sln') -c Release --no-restore

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'Helios.sln') -c Release --no-build
}

<#
.SYNOPSIS
    Brings up the HELIOS development stack (MySQL, Redis, Ollama, API, Worker, Web).
#>
[CmdletBinding()]
param(
    [switch]$Gpu,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$composeDir = Join-Path $PSScriptRoot '..\deploy\compose'

if (-not (Test-Path (Join-Path $composeDir '.env'))) {
    Write-Host 'No deploy/compose/.env found. Copy .env.example and set the passwords first.' -ForegroundColor Yellow
    exit 1
}

$composeArgs = @('compose')
if ($Gpu) { $composeArgs += @('--profile', 'gpu') }
$composeArgs += 'up'
if ($Build) { $composeArgs += '--build' }
$composeArgs += '-d'

Push-Location $composeDir
try {
    & docker @composeArgs
    Write-Host ''
    Write-Host 'API  http://localhost:5080/health' -ForegroundColor Green
    Write-Host 'Web  http://localhost:5173'        -ForegroundColor Green
}
finally {
    Pop-Location
}

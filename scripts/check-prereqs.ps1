<#
.SYNOPSIS
    Checks everything HELIOS needs to build and run locally, and reports what is missing.
.DESCRIPTION
    Read-only. Installs nothing, changes nothing. Run it before starting a work session.
.EXAMPLE
    ./scripts/check-prereqs.ps1
#>
[CmdletBinding()]
param()

$script:missing = 0

function Test-Item {
    param(
        [string]$Name,
        [scriptblock]$Check,
        [string]$Fix,
        [switch]$Optional
    )

    try { $result = & $Check } catch { $result = $null }

    if ($result) {
        Write-Host ("  [ok]      {0,-22} {1}" -f $Name, $result) -ForegroundColor Green
    }
    elseif ($Optional) {
        Write-Host ("  [--]      {0,-22} not installed (optional)" -f $Name) -ForegroundColor DarkGray
        if ($Fix) { Write-Host ("            -> {0}" -f $Fix) -ForegroundColor DarkGray }
    }
    else {
        $script:missing++
        Write-Host ("  [MISSING] {0,-22} required" -f $Name) -ForegroundColor Red
        if ($Fix) { Write-Host ("            -> {0}" -f $Fix) -ForegroundColor Yellow }
    }
}

Write-Host ''
Write-Host 'HELIOS prerequisites' -ForegroundColor Cyan
Write-Host '===================='
Write-Host ''
Write-Host 'Toolchain'

Test-Item 'dotnet SDK' {
    $v = (& dotnet --version 2>$null)
    if ($v -and [version]($v -split '-')[0] -ge [version]'10.0.0') { $v } else { $null }
} 'Install the .NET 10 SDK from https://dotnet.microsoft.com/download'

Test-Item 'git' { (& git --version 2>$null) } 'https://git-scm.com/download/win'

Test-Item 'node' {
    $v = (& node --version 2>$null)
    if ($v -and [int](($v -replace '^v','') -split '\.')[0] -ge 20) { $v } else { $null }
} 'Install Node 22 LTS from https://nodejs.org - required for frontend/helios-web'

Test-Item 'docker' {
    $v = (& docker --version 2>$null)
    if ($v) { $v } else { $null }
} 'Install Docker Desktop (WSL2 backend). CAP WSL2 FIRST - see below'

Test-Item 'ollama' {
    $v = (& ollama --version 2>$null)
    if ($v) { ($v | Select-Object -First 1) } else { $null }
} 'Install Ollama for Windows (native, NOT the container) from https://ollama.com/download'

Write-Host ''
Write-Host 'Machine'

Test-Item 'NVIDIA GPU' {
    if (Get-Command nvidia-smi -ErrorAction SilentlyContinue) {
        (& nvidia-smi --query-gpu=name,memory.total --format=csv,noheader | Select-Object -First 1)
    } else { $null }
} 'No GPU detected - local inference will run on CPU and be very slow' -Optional

Test-Item 'System RAM' {
    $gb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 0)
    if ($gb -ge 16) { "$gb GB" } else { $null }
} 'Under 16 GB - the hybrid dev stack will be very tight'

$wslConfig = Join-Path $env:USERPROFILE '.wslconfig'
Test-Item 'WSL2 memory cap' {
    if ((Test-Path $wslConfig) -and (Select-String -Path $wslConfig -Pattern '^\s*memory\s*=' -Quiet)) {
        (Select-String -Path $wslConfig -Pattern '^\s*memory\s*=').Line.Trim()
    } else { $null }
} "Copy scripts/wslconfig.template to $wslConfig, then run: wsl --shutdown"

Write-Host ''
Write-Host 'Services'

Test-Item 'MySQL on :3306' {
    if ((Test-NetConnection -ComputerName localhost -Port 3306 -WarningAction SilentlyContinue).TcpTestSucceeded) { 'reachable' } else { $null }
} 'docker compose -f deploy/compose/docker-compose.data.yml up -d' -Optional

Test-Item 'Redis on :6379' {
    if ((Test-NetConnection -ComputerName localhost -Port 6379 -WarningAction SilentlyContinue).TcpTestSucceeded) { 'reachable' } else { $null }
} 'docker compose -f deploy/compose/docker-compose.data.yml up -d' -Optional

Test-Item 'Ollama on :11434' {
    if ((Test-NetConnection -ComputerName localhost -Port 11434 -WarningAction SilentlyContinue).TcpTestSucceeded) { 'reachable' } else { $null }
} 'Start Ollama, then: ollama pull qwen2.5-coder:14b' -Optional

Write-Host ''
Write-Host 'Configuration'

$composeEnv = Join-Path $PSScriptRoot '..\deploy\compose\.env'
Test-Item 'compose .env' {
    if (Test-Path $composeEnv) { 'present' } else { $null }
} 'Copy-Item deploy/compose/.env.example deploy/compose/.env, then set the passwords'

Write-Host ''
if ($script:missing -eq 0) {
    Write-Host 'All required prerequisites present.' -ForegroundColor Green
} else {
    Write-Host ("{0} required item(s) missing - see the arrows above." -f $script:missing) -ForegroundColor Yellow
}
Write-Host ''

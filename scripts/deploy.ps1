#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Full deploy: stop services, publish, register (first run only), start.

.DESCRIPTION
    Run this from the repo root to go from source to running Windows Services
    in one step. On the first run it registers each instance with sc.exe.
    On subsequent runs it just stops, republishes, and restarts.

.PARAMETER DeployPath
    Folder the binary and services.json are published into and run from.
    Defaults to C:\Services\WindowsProxyService.

.PARAMETER ProjectPath
    Path to the .csproj file, relative to where this script is called from.
    Defaults to src\WindowsProxyService\WindowsProxyService.csproj.

.EXAMPLE
    # From the repo root, first-time or any subsequent deploy:
    .\scripts\deploy.ps1

    # Custom deploy path:
    .\scripts\deploy.ps1 -DeployPath "D:\MyServices\WindowsProxyService"
#>
param(
    [string]$DeployPath  = "C:\Services\WindowsProxyService",
    [string]$ProjectPath = "src\WindowsProxyService\WindowsProxyService.csproj"
)

$ErrorActionPreference = "Stop"
$servicesJson = Join-Path $DeployPath "services.json"

function Get-ServiceName([string]$instanceName) {
    return "WindowsProxyService.$instanceName"
}

# ---------------------------------------------------------------------------
# Step 1: Stop any running instances
# Must happen before publish -- Windows locks the .exe while the service runs.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Stopping running instances..."

# Prefer the already-deployed services.json; fall back to source tree on first run.
$sourceServicesJson = Join-Path (Split-Path $PSScriptRoot) "src\WindowsProxyService\services.json"
$jsonPath = if (Test-Path $servicesJson) { $servicesJson } else { $sourceServicesJson }

if (Test-Path $jsonPath) {
    $instances = Get-Content $jsonPath -Raw | ConvertFrom-Json
    foreach ($instance in $instances) {
        $svcName = Get-ServiceName $instance.InstanceName
        $svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
        if ($svc -and $svc.Status -eq "Running") {
            Write-Host "    Stopping $svcName..."
            Stop-Service -Name $svcName -Force
        }
    }
} else {
    Write-Host "    No services.json found yet -- skipping stop step."
}

# ---------------------------------------------------------------------------
# Step 2: Publish (Release, win-x64, self-contained)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Publishing to $DeployPath..."

dotnet publish $ProjectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained `
    --output $DeployPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed (exit code $LASTEXITCODE). Aborting."
}

Write-Host "    Publish succeeded."

# ---------------------------------------------------------------------------
# Step 3: Register services -- first run only, skipped if already registered.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Registering services (skipped for any already registered)..."

$exePath  = Join-Path $DeployPath "WindowsProxyService.exe"
$instances = Get-Content $servicesJson -Raw | ConvertFrom-Json

foreach ($instance in $instances) {
    $instanceName = $instance.InstanceName
    $svcName      = Get-ServiceName $instanceName
    $description  = $instance.ServiceDescription
    $binPath      = "`"$exePath`" --name $instanceName"

    $existing = Get-Service -Name $svcName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "    $svcName already registered -- skipping."
    } else {
        Write-Host "    Registering $svcName..."
        sc.exe create $svcName binPath= $binPath start= auto DisplayName= $svcName | Out-Null
        sc.exe description $svcName $description | Out-Null
        Write-Host "    -> Registered."
    }
}

# ---------------------------------------------------------------------------
# Step 4: Start all instances
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Starting services..."

foreach ($instance in $instances) {
    $svcName = Get-ServiceName $instance.InstanceName
    Write-Host "    Starting $svcName..."
    Start-Service -Name $svcName
}

Write-Host ""
Write-Host "==> Deploy complete."
Write-Host ""
Write-Host "Service status:"
Get-Service -Name "WindowsProxyService.*" | Format-Table Name, Status -AutoSize

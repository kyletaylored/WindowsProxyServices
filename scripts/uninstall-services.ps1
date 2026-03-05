#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops and removes each Windows Service instance defined in services.json.

.PARAMETER PublishPath
    Path to the folder containing services.json.
    Defaults to C:\Services\WindowsProxyService.

.EXAMPLE
    .\uninstall-services.ps1
    .\uninstall-services.ps1 -PublishPath "D:\MyServices\WindowsProxyService"
#>
param(
    [string]$PublishPath = "C:\Services\WindowsProxyService"
)

$ErrorActionPreference = "Stop"

$servicesJson = Join-Path $PublishPath "services.json"

if (-not (Test-Path $servicesJson)) {
    Write-Error "services.json not found: $servicesJson"
}

$instances = Get-Content $servicesJson -Raw | ConvertFrom-Json

foreach ($instance in $instances) {
    $serviceName = "WindowsProxyService.$($instance.InstanceName)"

    $existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if (-not $existing) {
        Write-Warning "Service '$serviceName' not found -- skipping."
        continue
    }

    Write-Host "Stopping service: $serviceName"
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue

    Write-Host "Deleting service: $serviceName"
    sc.exe delete $serviceName | Out-Null
    Write-Host "  -> Removed."
}

Write-Host ""
Write-Host "Done."

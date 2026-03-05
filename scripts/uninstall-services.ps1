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
    $name = $instance.InstanceName

    $existing = Get-Service -Name $name -ErrorAction SilentlyContinue
    if (-not $existing) {
        Write-Warning "Service '$name' not found — skipping."
        continue
    }

    Write-Host "Stopping service: $name"
    Stop-Service -Name $name -Force -ErrorAction SilentlyContinue

    Write-Host "Deleting service: $name"
    sc.exe delete $name | Out-Null
    Write-Host "  -> Removed."
}

Write-Host ""
Write-Host "Done."

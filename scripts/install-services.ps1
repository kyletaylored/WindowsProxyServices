#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Registers each instance defined in services.json as a Windows Service.

.PARAMETER PublishPath
    Path to the folder containing WindowsProxyService.exe and services.json.
    Defaults to C:\Services\WindowsProxyService.

.EXAMPLE
    .\install-services.ps1
    .\install-services.ps1 -PublishPath "D:\MyServices\WindowsProxyService"
#>
param(
    [string]$PublishPath = "C:\Services\WindowsProxyService"
)

$ErrorActionPreference = "Stop"

$exePath      = Join-Path $PublishPath "WindowsProxyService.exe"
$servicesJson = Join-Path $PublishPath "services.json"

if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found: $exePath`nRun 'dotnet publish' first and check -PublishPath."
}

if (-not (Test-Path $servicesJson)) {
    Write-Error "services.json not found: $servicesJson"
}

$instances = Get-Content $servicesJson -Raw | ConvertFrom-Json

foreach ($instance in $instances) {
    $name        = $instance.InstanceName
    $description = $instance.ServiceDescription
    $binPath     = "`"$exePath`" --name $name"

    $existing = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Warning "Service '$name' already exists — skipping. Run uninstall-services.ps1 first to reinstall."
        continue
    }

    Write-Host "Creating service: $name  ($description)"
    sc.exe create $name binPath= $binPath start= auto DisplayName= $name | Out-Null
    sc.exe description $name $description | Out-Null
    Write-Host "  -> Created."
}

Write-Host ""
Write-Host "Done. Start all instances with:"
Write-Host "  Get-Service Proxy-* | Start-Service"

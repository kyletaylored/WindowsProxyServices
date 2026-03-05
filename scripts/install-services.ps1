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
    $instanceName = $instance.InstanceName
    $serviceName  = "WindowsProxyService.$instanceName"
    $description  = $instance.ServiceDescription

    # --name receives the bare InstanceName; the Windows service name gets the prefix
    $binPath = "`"$exePath`" --name $instanceName"

    $existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Warning "Service '$serviceName' already exists -- skipping. Run uninstall-services.ps1 first to reinstall."
        continue
    }

    Write-Host "Creating service: $serviceName  ($description)"
    sc.exe create $serviceName binPath= $binPath start= auto DisplayName= $serviceName | Out-Null
    sc.exe description $serviceName $description | Out-Null
    Write-Host "  -> Created."
}

Write-Host ""
Write-Host "Done. Start all instances with:"
Write-Host "  Get-Service WindowsProxyService.* | Start-Service"

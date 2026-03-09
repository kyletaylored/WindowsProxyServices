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

# Resolve DD_VERSION from the latest git tag, falling back to the short commit hash.
# $ErrorActionPreference is temporarily set to "Continue" so that git writing to
# stderr does not trigger a terminating error under the "Stop" preference above.
$ddVersion = $null
$ErrorActionPreference = "Continue"
try {
    $_result = & git describe --tags --abbrev=0 2>&1
    if ($LASTEXITCODE -eq 0) { $ddVersion = "$_result".Trim() }

    if (-not $ddVersion) {
        $_result = & git rev-parse --short HEAD 2>&1
        if ($LASTEXITCODE -eq 0) { $ddVersion = "$_result".Trim() }
    }
} finally {
    $ErrorActionPreference = "Stop"
}
if (-not $ddVersion) { $ddVersion = "0.0.0-unknown" }

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

    $ddService = "windowsproxyservice-$($instanceName.ToLower())"
    $envVars   = @(
        "DD_SERVICE=$ddService",
        "DD_VERSION=$ddVersion"
    )
    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName" `
        -Name Environment -Value $envVars -Type MultiString
    Write-Host "  -> Created.  DD_SERVICE=$ddService  DD_VERSION=$ddVersion"
}

Write-Host ""
Write-Host "Done. Start all instances with:"
Write-Host "  Get-Service WindowsProxyService.* | Start-Service"

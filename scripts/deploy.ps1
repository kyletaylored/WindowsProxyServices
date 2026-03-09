#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Full deploy: stop services, publish, register (first run only), compile rules, start.

.DESCRIPTION
    Run this from the repo root to go from source to running Windows Services
    in one step. On the first run it registers each instance with sc.exe.
    On subsequent runs it just stops, republishes, and restarts.

    Also ensures the Datadog rule compiler is present and (re)compiles rules.toml
    into the Datadog managed policy directory on every deploy.

.PARAMETER DeployPath
    Folder the binary and services.json are published into and run from.
    Defaults to C:\Services\WindowsProxyService.

.PARAMETER ProjectPath
    Path to the .csproj file, relative to where this script is called from.
    Defaults to src\WindowsProxyService\WindowsProxyService.csproj.

.PARAMETER RulesToolPath
    Folder where dd-rules-converter.exe is installed.
    Defaults to C:\tools.

.PARAMETER RulesToolVersion
    Version of dd-rules-converter to download if not already present.
    Defaults to v0.1.1.

.EXAMPLE
    # From repo root, run as Administrator:
    .\scripts\deploy.ps1

    # Custom deploy path:
    .\scripts\deploy.ps1 -DeployPath "D:\MyServices\WindowsProxyService"
#>
param(
    [string]$DeployPath       = "C:\Services\WindowsProxyService",
    [string]$ProjectPath      = "src\WindowsProxyService\WindowsProxyService.csproj",
    [string]$RulesToolPath    = "C:\tools",
    [string]$RulesToolVersion = "v0.1.1"
)

$ErrorActionPreference = "Stop"
$repoRoot       = Split-Path $PSScriptRoot
$servicesJson   = Join-Path $DeployPath "services.json"
$rulesConverter = Join-Path $RulesToolPath "dd-rules-converter.exe"
$rulesFile      = Join-Path $repoRoot "rules.toml"
$policyOutput   = "C:\ProgramData\Datadog\managed\rc-orgwide-wls-policy.bin"

function Get-ServiceName([string]$instanceName) {
    return "WindowsProxyService.$instanceName"
}

# Resolve DD_VERSION from the latest git tag, falling back to the short commit hash.
# 2>&1 merges stderr into the captured output so nothing leaks to the terminal;
# $LASTEXITCODE is the authority on whether the command succeeded.
$ddVersion = $null
$_result = & git -C $repoRoot describe --tags --abbrev=0 2>&1
if ($LASTEXITCODE -eq 0) { $ddVersion = "$_result".Trim() }

if (-not $ddVersion) {
    $_result = & git -C $repoRoot rev-parse --short HEAD 2>&1
    if ($LASTEXITCODE -eq 0) { $ddVersion = "$_result".Trim() }
}

if (-not $ddVersion) { $ddVersion = "0.0.0-unknown" }
Write-Host ""
Write-Host "==> DD_VERSION resolved to: $ddVersion"

# ---------------------------------------------------------------------------
# Step 1: Stop any running instances
# Must happen before publish -- Windows locks the .exe while the service runs.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Stopping running instances..."

# Prefer the already-deployed services.json; fall back to source tree on first run.
$sourceServicesJson = Join-Path $repoRoot "src\WindowsProxyService\services.json"
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
# Step 4: Set Datadog environment variables on each service.
# Runs every deploy so DD_VERSION always reflects the current build.
# Written to HKLM:\SYSTEM\CurrentControlSet\Services\<Name>\Environment
# as REG_MULTI_SZ -- read by the tracer at process startup.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Setting Datadog environment variables (DD_SERVICE, DD_VERSION)..."

foreach ($instance in $instances) {
    $svcName  = Get-ServiceName $instance.InstanceName
    $ddService = "windowsproxyservice-$($instance.InstanceName.ToLower())"
    $regPath   = "HKLM:\SYSTEM\CurrentControlSet\Services\$svcName"

    if (-not (Test-Path $regPath)) {
        Write-Warning "    Registry key not found for $svcName -- skipping (service may not be registered yet)."
        continue
    }

    $envVars = @(
        "DD_SERVICE=$ddService",
        "DD_VERSION=$ddVersion"
    )

    Set-ItemProperty -Path $regPath -Name Environment -Value $envVars -Type MultiString
    Write-Host "    $svcName -> DD_SERVICE=$ddService  DD_VERSION=$ddVersion"
}

# ---------------------------------------------------------------------------
# Step 5: Ensure the Datadog rule compiler is present, download if missing.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Checking for Datadog rule compiler..."

if (Test-Path $rulesConverter) {
    Write-Host "    Found: $rulesConverter"
} else {
    Write-Host "    Not found -- downloading dd-rules-converter $RulesToolVersion..."

    $zipUrl  = "https://github.com/DataDog/dd-policy-engine/releases/download/$RulesToolVersion/dd-rules-converter-win-x64.zip"
    $zipFile = Join-Path $env:TEMP "dd-rules-converter.zip"

    Invoke-WebRequest -Uri $zipUrl -OutFile $zipFile
    New-Item -ItemType Directory -Path $RulesToolPath -Force | Out-Null
    Expand-Archive -Path $zipFile -DestinationPath $RulesToolPath -Force
    Remove-Item $zipFile

    if (-not (Test-Path $rulesConverter)) {
        Write-Error "Download succeeded but $rulesConverter was not found after extraction. Check the archive layout."
    }

    Write-Host "    -> Installed to $rulesConverter"
}

# ---------------------------------------------------------------------------
# Step 6: Compile rules.toml into the Datadog managed policy directory.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Compiling rules.toml..."

if (-not (Test-Path $rulesFile)) {
    Write-Error "rules.toml not found at $rulesFile. Aborting."
}

$policyDir = Split-Path $policyOutput
New-Item -ItemType Directory -Path $policyDir -Force | Out-Null

& $rulesConverter -rules $rulesFile -output $policyOutput

if ($LASTEXITCODE -ne 0) {
    Write-Error "Rule compilation failed (exit code $LASTEXITCODE). Aborting."
}

Write-Host "    -> Policy written to $policyOutput"

# ---------------------------------------------------------------------------
# Step 6: Start all instances
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

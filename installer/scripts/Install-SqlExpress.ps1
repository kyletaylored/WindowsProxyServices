<#
  Install-SqlExpress.ps1 — run by the MSI's InstallSqlExpressCA (deferred, as SYSTEM).

  Installs SQL Server Express as the SQLEXPRESS named instance when it is not
  already present.  NT AUTHORITY\SYSTEM is both the service account and a
  sysadmin so WindowsSqlService (running as LocalSystem) can connect straight
  away; BUILTIN\Administrators is also sysadmin so GrantSqlSystemAccessCA can
  connect as the installing user.

  Steps:
    1. Download the SQL Express bootstrapper (SSEI).
    2. Use it to download the full Express media (/ACTION=Download).
    3. Extract the media and run setup.exe silently.

  Progress and errors go to C:\Windows\Temp\wps-install.log.  The custom action
  is Return="ignore", so a non-zero exit here never fails the MSI.
#>
$ErrorActionPreference = 'Stop'
$log = 'C:\Windows\Temp\wps-install.log'
function Write-Log([string]$Message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [SqlInstall] $Message" | Add-Content -Path $log -ErrorAction Ignore
}

Write-Log 'CA started'

if (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -Name SQLEXPRESS -ErrorAction Ignore) {
    Write-Log 'SQLEXPRESS already installed - skipping download'
    exit 0
}

Write-Log 'SQLEXPRESS not found - downloading SQL Express'
$work = Join-Path $env:TEMP "wps-sqlexpress-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work -Force | Out-Null

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $bootstrapper = Join-Path $work 'SQL2022-SSEI-Expr.exe'
    (New-Object Net.WebClient).DownloadFile('https://go.microsoft.com/fwlink/p/?LinkID=2216019', $bootstrapper)
    Write-Log 'Bootstrapper downloaded - fetching installation media'

    $media = Join-Path $work 'media'
    $p = Start-Process $bootstrapper -ArgumentList "/ACTION=Download /MEDIAPATH=`"$media`" /MEDIATYPE=Core /QUIET" -Wait -PassThru
    $package = Get-ChildItem $media -Filter 'SQLEXPR*.exe' -Recurse -ErrorAction Ignore | Select-Object -First 1
    if (-not $package) { throw "media download failed (bootstrapper exit code $($p.ExitCode))" }
    Write-Log "Media downloaded ($($package.Name)) - extracting"

    $extract = Join-Path $work 'extract'
    $p = Start-Process $package.FullName -ArgumentList "/Q /X:`"$extract`"" -Wait -PassThru
    $setup = Join-Path $extract 'setup.exe'
    if (-not (Test-Path $setup)) { throw "extraction failed (exit code $($p.ExitCode))" }
    Write-Log 'Extraction complete - starting silent install (this may take several minutes)'

    $setupArgs = @(
        '/Q', '/ACTION=Install', '/FEATURES=SQLEngine', '/INSTANCENAME=SQLEXPRESS',
        '/SQLSVCACCOUNT="NT AUTHORITY\SYSTEM"',
        '/SQLSYSADMINACCOUNTS="BUILTIN\Administrators" "NT AUTHORITY\SYSTEM"',
        '/AGTSVCSTARTUPTYPE=Disabled', '/BROWSERSVCSTARTUPTYPE=Automatic',
        '/UPDATEENABLED=False', '/IACCEPTSQLSERVERLICENSETERMS'
    ) -join ' '
    $p = Start-Process $setup -ArgumentList $setupArgs -Wait -PassThru
    Write-Log "SQL Express installer exited with code $($p.ExitCode)"
    if ($p.ExitCode -notin 0, 3010) { exit $p.ExitCode }
}
catch {
    Write-Log "ERROR during download/install: $_"
    exit 1
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction Ignore
}

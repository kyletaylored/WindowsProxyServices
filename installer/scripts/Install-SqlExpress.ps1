<#
  Install-SqlExpress.ps1 — installs SQL Server Express as the SQLEXPRESS named
  instance when it is not already present.

  Runs in two modes:

  -Schedule   Run by the MSI's InstallSqlExpressCA (deferred, as SYSTEM).
              Registers a one-off SYSTEM scheduled task that re-runs this
              script without -Schedule, starts it and returns immediately.
              SQL Server setup cannot run inside the MSI: msiexec's custom
              action token lacks the backup/security/debug privileges setup's
              rules check requires, and setup's own MSIs would block on the
              Windows Installer mutex our install holds.

  (default)   Run by the scheduled task with a full SYSTEM token.
                1. Download the SQL Express bootstrapper (SSEI).
                2. Use it to download the full Express media (/ACTION=Download).
                3. Wait for our MSI to release the Windows Installer mutex.
                4. Extract the media and run setup.exe silently.
                5. Run Grant-SqlSystemAccess.ps1 to confirm SYSTEM is sysadmin.
                6. Delete the scheduled task.

  NT AUTHORITY\SYSTEM is both the service account and a sysadmin so
  WindowsSqlService (running as LocalSystem) can connect; BUILTIN\Administrators
  is also sysadmin.  WindowsSqlService retries its database connection every
  30 s, so it picks up the instance once setup completes.

  Progress and errors go to C:\Windows\Temp\wps-install.log.  The custom action
  is Return="ignore", so a non-zero exit here never fails the MSI.
#>
param([switch]$Schedule)

$ErrorActionPreference = 'Stop'
$log      = 'C:\Windows\Temp\wps-install.log'
$taskName = 'WindowsProxyService-InstallSqlExpress'
function Write-Log([string]$Message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [SqlInstall] $Message" | Add-Content -Path $log -ErrorAction Ignore
}

if (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -Name SQLEXPRESS -ErrorAction Ignore) {
    Write-Log 'SQLEXPRESS already installed - skipping download'
    exit 0
}

if ($Schedule) {
    Write-Log 'CA started - scheduling SQL Express install task'
    try {
        $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $action   = New-ScheduledTaskAction -Execute $powershell `
                        -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                        -ExecutionTimeLimit (New-TimeSpan -Hours 2)
        Register-ScheduledTask -TaskName $taskName -Action $action -Settings $settings `
            -User 'NT AUTHORITY\SYSTEM' -RunLevel Highest -Force | Out-Null
        Start-ScheduledTask -TaskName $taskName
        Write-Log "Scheduled task '$taskName' started - SQL Express installs after the MSI completes"
    }
    catch {
        Write-Log "ERROR scheduling SQL Express install: $_"
        exit 1
    }
    exit 0
}

Write-Log 'SQLEXPRESS not found - downloading SQL Express'
$work = Join-Path $env:TEMP "wps-sqlexpress-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work -Force | Out-Null
$exitCode = 0

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

    # Global\_MSIExecute exists while any Windows Installer transaction is
    # running — including the WindowsProxyServices MSI that scheduled us.
    $deadline = (Get-Date).AddMinutes(30)
    $mutex = $null
    while ([Threading.Mutex]::TryOpenExisting('Global\_MSIExecute', [ref]$mutex)) {
        $mutex.Dispose()
        if ((Get-Date) -gt $deadline) { throw 'timed out waiting for Windows Installer to become idle' }
        Start-Sleep -Seconds 5
    }
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
    if ($p.ExitCode -in 0, 3010) {
        & (Join-Path $PSScriptRoot 'Grant-SqlSystemAccess.ps1')
        $exitCode = $LASTEXITCODE
    }
    else {
        $exitCode = $p.ExitCode
    }
}
catch {
    Write-Log "ERROR during download/install: $_"
    $exitCode = 1
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction Ignore
}

Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Ignore
exit $exitCode

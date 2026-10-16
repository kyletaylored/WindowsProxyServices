<#
  Grant-SqlSystemAccess.ps1 — run by the MSI's GrantSqlSystemAccessCA
  (deferred, impersonating the elevated installing user).

  Runs on every install, whether SQL Express was pre-existing or was just
  installed by Install-SqlExpress.ps1:
    1. Wait up to 120 s for MSSQL$SQLEXPRESS to reach Running.
    2. Connect as the installing user (a sysadmin via BUILTIN\Administrators).
    3. Create the NT AUTHORITY\SYSTEM login if absent and add it to sysadmin.

  Uses System.Data.SqlClient (Windows PowerShell 5.1 / .NET Framework) rather
  than sqlcmd, which is not present on Express installs without client tools.
  The custom action is Return="ignore"; WindowsSqlService retries its database
  connection at startup, so it recovers once access is in place.
#>
$ErrorActionPreference = 'Stop'
$log = 'C:\Windows\Temp\wps-install.log'
function Write-Log([string]$Message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [SqlGrant] $Message" | Add-Content -Path $log -ErrorAction Ignore
}

Write-Log 'CA started - waiting for MSSQL$SQLEXPRESS'

if (-not (Get-Service 'MSSQL$SQLEXPRESS' -ErrorAction Ignore)) {
    Write-Log 'ERROR: MSSQL$SQLEXPRESS service not found - SQL Express may not have installed correctly'
    exit 1
}

$deadline = (Get-Date).AddSeconds(120)
do {
    $svc = Get-Service 'MSSQL$SQLEXPRESS' -ErrorAction Ignore
    if ($svc.Status -eq 'Running') { Write-Log 'MSSQL$SQLEXPRESS is Running'; break }
    Write-Log "Waiting for service (status: $($svc.Status))"
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline)

try {
    Add-Type -AssemblyName System.Data
    $conn = New-Object System.Data.SqlClient.SqlConnection('Server=localhost\SQLEXPRESS;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=15')
    $conn.Open()
    Write-Log 'Connected to SQLEXPRESS as installer user'

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'NT AUTHORITY\SYSTEM') CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS"
    $cmd.ExecuteNonQuery() | Out-Null
    $cmd.CommandText = "IF IS_SRVROLEMEMBER(N'sysadmin', N'NT AUTHORITY\SYSTEM') != 1 EXEC sp_addsrvrolemember N'NT AUTHORITY\SYSTEM', N'sysadmin'"
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Dispose()

    Write-Log 'NT AUTHORITY\SYSTEM granted sysadmin successfully'
}
catch {
    Write-Log "ERROR granting access: $_"
    exit 1
}

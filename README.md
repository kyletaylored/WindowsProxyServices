# WindowsProxyServices

A .NET 8 Worker Service that acts as a configurable HTTP reverse proxy using [YARP](https://microsoft.github.io/reverse-proxy/). A single compiled binary can be deployed as multiple named Windows Service instances, each forwarding traffic to a different upstream URL.

## Architecture

- **Runtime:** .NET 8, Worker Service host
- **Proxying:** YARP (Yet Another Reverse Proxy) — high-performance, no manual request plumbing
- **Logging:** JSON structured logging via `AddJsonConsole`, with `InstanceName` scoped on every request
- **Configuration:** `services.json` — a shared array of instance definitions
- **Deployment:** `sc.exe` via PowerShell automation

## Project Structure

```
src/WindowsProxyService/   Source project
scripts/                   PowerShell install/uninstall scripts
_reference/old_plan/       Legacy .NET Framework implementation (reference only)
```

## Configuration — services.json

```json
[
  {
    "InstanceName": "Proxy-1",
    "ServiceDescription": "Forwards to Open-Meteo API",
    "Host": "+",
    "Port": 5052,
    "ProxyUrl": "https://api.open-meteo.com"
  },
  {
    "InstanceName": "Proxy-2",
    "ServiceDescription": "Forwards to Cat Facts API",
    "Host": "+",
    "Port": 5053,
    "ProxyUrl": "https://catfact.ninja"
  }
]
```

- **InstanceName** — matches the `--name` argument and becomes the Windows Service name.
- **Host** — `+` means listen on all network interfaces (Kestrel wildcard).
- **Port** — the local port this instance listens on.
- **ProxyUrl** — the upstream base URL. All incoming paths and query strings are appended.

## Build & Publish

```powershell
dotnet publish src/WindowsProxyService/WindowsProxyService.csproj `
  -c Release -r win-x64 --self-contained `
  -o C:\Services\WindowsProxyService
```

The `services.json` is copied to the output folder automatically.

## Install as Windows Services

```powershell
# Run as Administrator
.\scripts\install-services.ps1 -PublishPath "C:\Services\WindowsProxyService"
```

Start all instances:

```powershell
Get-Service Proxy-* | Start-Service
```

## Uninstall

```powershell
.\scripts\uninstall-services.ps1 -PublishPath "C:\Services\WindowsProxyService"
```

## Running Locally (Console Mode)

```powershell
dotnet run --project src/WindowsProxyService -- --name Proxy-1
```

## Validate

```powershell
# Check status endpoint
Invoke-RestMethod http://localhost:5052/api/status

# Hit the proxy (Open-Meteo forecast)
Invoke-RestMethod "http://localhost:5052/v1/forecast?latitude=32.78&longitude=-96.80&current_weather=true"
```

## Log Format

Each log entry is a single-line JSON object:

```json
{"Timestamp":"2026-03-05 10:15:00","Level":"Information","Message":"Proxy 'Proxy-1' starting on +:5052, forwarding to https://api.open-meteo.com","Category":"WindowsProxyService.Program","Scopes":[{"Instance":"Proxy-1"}]}
```

# WindowsProxyServices

A test harness for validating **Datadog SSI (Single Step Installation) auto-instrumentation** on Windows. It provisions multiple named .NET Windows Services — each a lightweight HTTP reverse proxy — giving you a realistic set of live .NET processes to target with Workload Selection rules.

A single compiled binary (`WindowsProxyService.exe`) is deployed as multiple Windows Service instances, each forwarding traffic to a different upstream URL and listening on its own port. This lets you verify that SSI rules correctly instrument (or skip) specific processes based on executable name, DLL name, or naming patterns.

## Architecture

- **Runtime:** .NET 8, Worker Service host
- **Proxying:** YARP (Yet Another Reverse Proxy) — no manual request plumbing
- **Logging:** JSON structured logging via `AddJsonConsole`, with `InstanceName` scoped on every request
- **Configuration:** `services.json` — array of instance definitions; `rules.toml` — Workload Selection rules
- **Deployment:** `sc.exe` via PowerShell (`deploy.ps1`)

## Project Structure

```
src/WindowsProxyService/   Source project
scripts/                   PowerShell deploy/install/uninstall scripts
rules.toml                 Datadog Workload Selection rules (compile before use)
```

## Configuration — services.json

```json
[
  {
    "InstanceName": "OpenMeteo",
    "ServiceDescription": "Proxies requests to the Open-Meteo weather forecast API (no auth required)",
    "Host": "+",
    "Port": 5052,
    "ProxyUrl": "https://api.open-meteo.com"
  },
  {
    "InstanceName": "CatFacts",
    "ServiceDescription": "Proxies requests to the Cat Facts API (no auth required)",
    "Host": "+",
    "Port": 5053,
    "ProxyUrl": "https://catfact.ninja"
  },
  {
    "InstanceName": "JsonPlaceholder",
    "ServiceDescription": "Proxies requests to JSONPlaceholder -- fake REST API for testing (no auth required)",
    "Host": "+",
    "Port": 5054,
    "ProxyUrl": "https://jsonplaceholder.typicode.com"
  },
  {
    "InstanceName": "DogCeo",
    "ServiceDescription": "Proxies requests to the Dog CEO random dog image API (no auth required)",
    "Host": "+",
    "Port": 5055,
    "ProxyUrl": "https://dog.ceo"
  },
  {
    "InstanceName": "ChuckNorris",
    "ServiceDescription": "Proxies requests to the Chuck Norris jokes API (no auth required)",
    "Host": "+",
    "Port": 5056,
    "ProxyUrl": "https://api.chucknorris.io"
  }
]
```

- **InstanceName** — matches the `--name` argument. The registered Windows Service name is `WindowsProxyService.<InstanceName>`.
- **Host** — `+` means listen on all network interfaces (Kestrel wildcard).
- **Port** — the local port this instance listens on.
- **ProxyUrl** — the upstream base URL. All incoming paths and query strings are appended.

## Deploy (recommended)

Run `deploy.ps1` from an elevated terminal at the repo root. It handles everything in the correct order:

1. Stop running instances (so the `.exe` is unlocked)
2. `dotnet publish` (Release, win-x64, self-contained) directly into the deploy folder
3. Register services with `sc.exe` — first run only, skipped on subsequent deploys
4. Download `dd-rules-converter.exe` if not already present
5. Compile `rules.toml` into `C:\ProgramData\Datadog\managed\rc-orgwide-wls-policy.bin`
6. Start all instances

```powershell
# From repo root, run as Administrator:
.\scripts\deploy.ps1

# Custom deploy path or tool location:
.\scripts\deploy.ps1 -DeployPath "D:\MyServices\WindowsProxyService" -RulesToolPath "D:\tools"
```

That's it — the same command works for first-time setup and every update after. Rules are recompiled on every deploy so changes to `rules.toml` are always picked up.

## Manual Steps (advanced)

These scripts are used internally by `deploy.ps1` but can be run individually if needed.

**Publish only:**

```powershell
dotnet publish src/WindowsProxyService/WindowsProxyService.csproj `
  -c Release -r win-x64 --self-contained `
  -o C:\Services\WindowsProxyService
```

**Register services (after a manual publish):**

```powershell
.\scripts\install-services.ps1 -PublishPath "C:\Services\WindowsProxyService"
Get-Service WindowsProxyService.* | Start-Service
```

**Uninstall all instances:**

```powershell
.\scripts\uninstall-services.ps1 -PublishPath "C:\Services\WindowsProxyService"
```

## Running Locally (Console Mode)

```powershell
dotnet run --project src/WindowsProxyService -- --name OpenMeteo
```

## Datadog SSI — Workload Selection

This project exists to test Datadog's **host-level SSI auto-instrumentation** for .NET on Windows. With SSI enabled, the Datadog tracer automatically attaches to .NET processes on startup. **Workload Selection** rules let you control exactly which processes are instrumented.

### Prerequisites

1. Install the Datadog Agent with SSI enabled:

```powershell
$p = Start-Process -Wait -PassThru msiexec -ArgumentList '/qn /i "https://windows-agent.datadoghq.com/datadog-agent-7-latest.amd64.msi" /log C:\Windows\SystemTemp\install-datadog.log APIKEY="<YOUR_API_KEY>" SITE="datadoghq.com" DD_APM_INSTRUMENTATION_ENABLED="host" DD_APM_INSTRUMENTATION_LIBRARIES="dotnet:3"'
```

2. Install the rule compiler:

```powershell
Invoke-WebRequest -Uri "https://github.com/DataDog/dd-policy-engine/releases/download/v0.1.1/dd-rules-converter-win-x64.zip" -OutFile "dd-rules-converter.zip"
Expand-Archive -Path "dd-rules-converter.zip" -DestinationPath "C:\tools"
```

### Compile and apply rules.toml

Edit `rules.toml` in the repo root to define which processes to instrument, then compile it:

```powershell
C:\tools\dd-rules-converter.exe -rules rules.toml -output "C:\ProgramData\Datadog\managed\rc-orgwide-wls-policy.bin"
```

The tracer loads the compiled policy automatically the next time a .NET process starts — no agent restart needed. Restart the proxy services to pick up the new rules:

```powershell
Get-Service WindowsProxyService.* | Restart-Service
```

### How the proxy services map to rule selectors

All instances share one binary, so `process.executable` matches all of them at once. Use `dotnet.dll` or a naming convention if you need per-instance targeting.

| Selector | Value | Matches |
| --- | --- | --- |
| `process.executable` | `WindowsProxyService.exe` | All instances |
| `process.executable` | `*ProxyService.exe` | All instances (wildcard suffix) |
| `dotnet.dll` | `WindowsProxyService.dll` | All instances (by entry-point DLL) |

### Example rules (see rules.toml for full file)

```toml
[instrument-all-proxy-instances]
description = "Instrument all WindowsProxyService instances"
instrument  = true
expression  = "process.executable:WindowsProxyService.exe runtime.language:dotnet"

[instrument-by-name-suffix]
description = "Instrument any .NET process whose executable ends with ProxyService.exe"
instrument  = true
expression  = "process.executable:*ProxyService.exe runtime.language:dotnet"
```

### Troubleshooting

Enable debug logging to inspect rule evaluation:

```powershell
$env:DD_TRACE_DEBUG = "true"
$env:DD_TRACE_LOG_DIRECTORY = "C:\logs\datadog"
```

Confirm the compiled policy file exists and is readable:

```
C:\ProgramData\Datadog\managed\rc-orgwide-wls-policy.bin
```

## Validate

### Status endpoint (all instances)

Each instance exposes `/api/status` locally — it does not forward to the upstream.

```powershell
Invoke-RestMethod http://localhost:5052/api/status
Invoke-RestMethod http://localhost:5053/api/status
Invoke-RestMethod http://localhost:5054/api/status
Invoke-RestMethod http://localhost:5055/api/status
Invoke-RestMethod http://localhost:5056/api/status
```

### WindowsProxyService.OpenMeteo — port 5052

Upstream: `https://api.open-meteo.com`

```powershell
# Current weather for Dallas, TX
Invoke-RestMethod "http://localhost:5052/v1/forecast?latitude=32.78&longitude=-96.80&current_weather=true"

# Hourly temperature + wind speed for the next 3 days
Invoke-RestMethod "http://localhost:5052/v1/forecast?latitude=32.78&longitude=-96.80&hourly=temperature_2m,windspeed_10m&forecast_days=3"

# Historical weather for a specific date range
Invoke-RestMethod "http://localhost:5052/v1/archive?latitude=32.78&longitude=-96.80&start_date=2025-01-01&end_date=2025-01-07&daily=temperature_2m_max"
```

### WindowsProxyService.CatFacts — port 5053

Upstream: `https://catfact.ninja`

```powershell
# Random cat fact
Invoke-RestMethod "http://localhost:5053/fact"

# Random cat fact with a max length
Invoke-RestMethod "http://localhost:5053/fact?max_length=100"

# Paginated list of facts
Invoke-RestMethod "http://localhost:5053/facts?page=1&limit=5"
```

### WindowsProxyService.JsonPlaceholder — port 5054

Upstream: `https://jsonplaceholder.typicode.com`

```powershell
# Get a single post
Invoke-RestMethod "http://localhost:5054/posts/1"

# List all comments on a post
Invoke-RestMethod "http://localhost:5054/posts/1/comments"

# Create a new post (simulated -- returns the created resource)
Invoke-RestMethod "http://localhost:5054/posts" -Method Post `
  -ContentType "application/json" `
  -Body '{"title":"Test","body":"Hello proxy","userId":1}'

# Get a user
Invoke-RestMethod "http://localhost:5054/users/1"
```

### WindowsProxyService.DogCeo — port 5055

Upstream: `https://dog.ceo`

```powershell
# Random dog image (any breed)
Invoke-RestMethod "http://localhost:5055/api/breeds/image/random"

# List all breeds
Invoke-RestMethod "http://localhost:5055/api/breeds/list/all"

# Random image for a specific breed
Invoke-RestMethod "http://localhost:5055/api/breed/labrador/images/random"

# Multiple random images
Invoke-RestMethod "http://localhost:5055/api/breeds/image/random/3"
```

### WindowsProxyService.ChuckNorris — port 5056

Upstream: `https://api.chucknorris.io`

```powershell
# Random joke
Invoke-RestMethod "http://localhost:5056/jokes/random"

# Random joke from a specific category
Invoke-RestMethod "http://localhost:5056/jokes/random?category=science"

# List all categories
Invoke-RestMethod "http://localhost:5056/jokes/categories"

# Search jokes by keyword
Invoke-RestMethod "http://localhost:5056/jokes/search?query=computer"
```

## Windows Service Names

Each instance is registered under `WindowsProxyService.<InstanceName>`:

| Service Name                          | Port | Upstream                             |
| ------------------------------------- | ---- | ------------------------------------ |
| `WindowsProxyService.OpenMeteo`       | 5052 | https://api.open-meteo.com           |
| `WindowsProxyService.CatFacts`        | 5053 | https://catfact.ninja                |
| `WindowsProxyService.JsonPlaceholder` | 5054 | https://jsonplaceholder.typicode.com |
| `WindowsProxyService.DogCeo`          | 5055 | https://dog.ceo                      |
| `WindowsProxyService.ChuckNorris`     | 5056 | https://api.chucknorris.io           |

## Log Format

Each log entry is a single-line JSON object:

```json
{
  "Timestamp": "2026-03-05 10:15:00",
  "Level": "Information",
  "Message": "Proxy 'OpenMeteo' starting on +:5052, forwarding to https://api.open-meteo.com",
  "Category": "WindowsProxyService.Program",
  "Scopes": [{ "Instance": "OpenMeteo" }]
}
```

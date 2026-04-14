# WindowsProxyServices

A test harness for validating **Datadog SSI (Single Step Installation) auto-instrumentation** on Windows. It provisions multiple named .NET Windows Services — each a lightweight HTTP reverse proxy — giving you a realistic set of live .NET processes to target with Workload Selection rules.

A single compiled binary (`WindowsProxyService.exe`) is deployed as multiple Windows Service instances, each forwarding traffic to a different upstream URL and listening on its own port. A **web dashboard** and **system tray app** are included to make it easy to start/stop services, fire test requests, and inspect responses without touching the terminal.

## What's Included

| Component | Type | Port | Purpose |
|-----------|------|------|---------|
| `WindowsProxyService` | Windows Service (×5) | 5052–5056 | YARP reverse proxies — one per upstream API |
| `WindowsDashboardService` | Windows Service | 5051 | Bootstrap 5 web UI — status, test requests, start/stop |
| `WindowsTrayApp` | Desktop App | — | System tray icon — opens dashboard, start/stop shortcuts |

## Project Structure

```
src/
  WindowsProxyService/         Core proxy service (one binary, five instances)
  WindowsDashboardService/     Web dashboard + REST control API
    wwwroot/index.html         Bootstrap 5 dark-theme dashboard UI
  WindowsTrayApp/              WinForms system tray application
installer/
  Product.wxs                  WiX v4 MSI package definition
  Services.wxs                 Windows service + shortcut registrations
  License.rtf                  Installer license/disclaimer
scripts/
  deploy.ps1                   Full deploy: publish → register → rules → start
  install-services.ps1         Register proxy + dashboard services (after manual publish)
  uninstall-services.ps1       Stop and remove all services
rules.toml                     Datadog Workload Selection rules (compile before use)
```

## Quick Start

### Option A — MSI Installer (recommended)

Download the latest `WindowsProxyServices-<version>.msi` from the [Releases](https://github.com/kyletaylored/WindowsProxyServices/releases) page and run it. The installer:

- Copies all binaries to `C:\Services\WindowsProxyService\` (configurable in the GUI)
- Registers and auto-starts all five proxy services plus the dashboard service
- Creates a **Start Menu shortcut** for the tray app

Silent install:

```powershell
msiexec /i WindowsProxyServices-1.0.0.msi /quiet
# Custom path:
msiexec /i WindowsProxyServices-1.0.0.msi /quiet INSTALLFOLDER="D:\Custom\"
```

Uninstall:

```powershell
msiexec /x WindowsProxyServices-1.0.0.msi /quiet
# or: Apps & Features → Windows Proxy Services → Uninstall
```

### Option B — Deploy Script

Run `deploy.ps1` from an elevated terminal at the repo root for a manual one-shot deploy of all services. See [Scripts](#scripts) below for full details.

```powershell
# From repo root, run as Administrator:
.\scripts\deploy.ps1
```

### Option C — Visual Studio (local dev)

See [Local Development](#local-development) below.

---

## Local Development

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (any edition) **or** VS Code with the C# extension
- Windows (required — services and tray app are Windows-only)

### Running from the terminal

The proxy service supports starting one or multiple instances in a single process:

```powershell
# Terminal 1 — dashboard (http://localhost:5051)
dotnet run --project src/WindowsDashboardService

# Terminal 2 — all five proxy instances in one process
dotnet run --project src/WindowsProxyService -- --all

# Optional — tray app (separate window, needs dashboard running)
dotnet run --project src/WindowsTrayApp
```

Or start a subset of services:

```powershell
dotnet run --project src/WindowsProxyService -- --name OpenMeteo CatFacts
```

Open [http://localhost:5051](http://localhost:5051) to access the dashboard.

> **Tip:** When running in console mode the proxies log structured JSON to stdout. The dashboard uses HTTP port probing as a fallback when Windows services aren't registered, so status dots still reflect whether each proxy is actually up.

**`--name` argument forms:**

| Form | Behaviour |
|------|-----------|
| `--name OpenMeteo` | Start one instance |
| `--name OpenMeteo CatFacts` | Start two instances in one process |
| `--name OpenMeteo --name CatFacts` | Same, flags repeated |
| `--name *` or `--all` | Start every service defined in `services.json` |

### Running from Visual Studio

**Quickest setup — all services in two processes:**

1. Open `WindowsProxyServices.sln`.
2. Right-click `WindowsProxyService` → **Properties** → **Debug** → **General** → **Open debug launch profiles UI**.
3. Set **Command line arguments** to `--all`.
4. Right-click the **Solution** → **Properties** → **Common Properties** → **Startup Project** → **Multiple Startup Projects**.
5. Set `WindowsDashboardService` and `WindowsProxyService` to **Start**.
6. Press **F5** — the dashboard starts on port 5051 and all five proxies start on ports 5052–5056.

**Start a single proxy instance (e.g. for focused debugging):**

Set the command line arguments to `--name OpenMeteo` (or whichever service you want) instead of `--all`. To run additional instances alongside it, open terminals and `dotnet run` with the remaining names.

**Run the tray app:**

Add `WindowsTrayApp` to the Multiple Startup Projects list, or start it separately from a terminal.

---

## Scripts

All scripts require an **Administrator** PowerShell session. Run them from the repo root.

### `deploy.ps1` — Full deploy (all services)

Handles everything in order: stop → publish → register → set DD vars → compile rules → start.
The same command works for first-time setup and every subsequent update.  Deploys all three components: the five proxy services, the dashboard service, and the tray app executable.

```powershell
# Default deploy path (C:\Services\WindowsProxyService):
.\scripts\deploy.ps1

# Custom deploy or tool paths:
.\scripts\deploy.ps1 -DeployPath "D:\MyServices" -RulesToolPath "D:\tools"
```

**Parameters:**

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-DeployPath` | `C:\Services\WindowsProxyService` | Where binaries are published and services run from |
| `-ProjectPath` | `src\WindowsProxyService\WindowsProxyService.csproj` | Proxy service project to publish |
| `-DashboardProjectPath` | `src\WindowsDashboardService\WindowsDashboardService.csproj` | Dashboard service project to publish |
| `-TrayAppProjectPath` | `src\WindowsTrayApp\WindowsTrayApp.csproj` | Tray app project to publish |
| `-RulesToolPath` | `C:\tools` | Where `dd-rules-converter.exe` is installed (downloaded automatically if missing) |
| `-RulesToolVersion` | `v0.1.1` | Version to download if the tool is absent |

**What it does:**

1. Stops any running proxy service instances and `WindowsDashboardService` (so the `.exe` files are unlocked for publish)
2. `dotnet publish` — Release, win-x64, self-contained — all three projects into `-DeployPath`
3. Registers each proxy instance and `WindowsDashboardService` with `sc.exe` (first run only; skipped if already registered). `WindowsTrayApp` is not a service and is not registered.
4. Writes `DD_SERVICE` and `DD_VERSION` to each proxy service's registry environment block
5. Downloads `dd-rules-converter.exe` if not present in `-RulesToolPath`
6. Compiles `rules.toml` → `C:\ProgramData\Datadog\managed\rc-orgwide-wls-policy.bin`
7. Starts all proxy service instances and `WindowsDashboardService`

### `install-services.ps1` — Register services after a manual publish

Use this when you've already run `dotnet publish` manually and just need to register the Windows services.

```powershell
# After publishing to the default path:
.\scripts\install-services.ps1

# Custom publish output path:
.\scripts\install-services.ps1 -PublishPath "D:\MyServices\WindowsProxyService"
```

Reads `services.json` from `-PublishPath`, creates each `WindowsProxyService.<InstanceName>` service and `WindowsDashboardService` with `sc.exe`, and writes `DD_SERVICE`/`DD_VERSION` to each proxy service's registry. Skips any service that is already registered.

After running, start the services:

```powershell
Get-Service WindowsProxyService.*, WindowsDashboardService | Start-Service
```

### `uninstall-services.ps1` — Stop and remove all services

```powershell
.\scripts\uninstall-services.ps1

# Custom path:
.\scripts\uninstall-services.ps1 -PublishPath "D:\MyServices\WindowsProxyService"
```

Reads `services.json`, stops and deletes each proxy service instance, then stops and deletes `WindowsDashboardService`. Does not touch the deployed files.

---

## Dashboard

The **Windows Dashboard Service** runs at [http://localhost:5051](http://localhost:5051) and provides:

- **Live status cards** for each proxy service, auto-refreshing every 5 seconds
- **Test button** — fires a GET request through the local proxy and shows the formatted JSON response inline
- **Start / Stop buttons** — controls each Windows service directly (the dashboard runs as LocalSystem so no elevation prompt is needed)

The dashboard is itself a Windows service (`WindowsDashboardService`) so it starts automatically with Windows after an MSI install.

**REST API (used by the dashboard UI and tray app):**

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/api/services` | List all services with status, port, upstream URL |
| `POST` | `/api/services/{name}/test` | Fire a test GET request through the proxy; returns status code + body |
| `POST` | `/api/services/{name}/start` | Start the named Windows service |
| `POST` | `/api/services/{name}/stop` | Stop the named Windows service |

Example:

```powershell
# List all services and their status
Invoke-RestMethod http://localhost:5051/api/services

# Test the ChuckNorris proxy
Invoke-RestMethod -Method Post http://localhost:5051/api/services/ChuckNorris/test

# Stop the DogCeo proxy
Invoke-RestMethod -Method Post http://localhost:5051/api/services/DogCeo/stop
```

## Tray App

`WindowsTrayApp.exe` sits in the system tray and gives quick access to everything without opening a browser or terminal.

- **Double-click** the icon → opens the dashboard in your default browser
- **Right-click** → context menu:
  - **Open Dashboard** — opens [http://localhost:5051](http://localhost:5051)
  - **Start All / Stop All** — controls all proxy services at once
  - Per-service **Start / Stop** sub-menus (status dot `●`/`○` updates when you open the menu)
  - **Exit**

The tray app delegates start/stop to the dashboard API, so it needs `WindowsDashboardService` to be running. If the dashboard is unreachable it shows a warning message.

After an MSI install, launch it from **Start Menu → Windows Proxy Services Tray** or directly from `C:\Services\WindowsProxyService\WindowsTrayApp.exe`.

---

## Configuration — services.json

Defines all proxy instances. Located at `src/WindowsProxyService/services.json` (copied alongside the exe at publish time).

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
    "ServiceDescription": "Proxies requests to JSONPlaceholder — fake REST API for testing (no auth required)",
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

| Field | Description |
|-------|-------------|
| `InstanceName` | Matches the `--name` value. Windows Service name is `WindowsProxyService.<InstanceName>`. Pass multiple names or `--all` to start more than one instance per process. |
| `Host` | `+` listens on all interfaces (Kestrel wildcard). |
| `Port` | Port this instance binds to. |
| `ProxyUrl` | Upstream base URL. All incoming paths and query strings are forwarded. |

---

## Datadog SSI — Workload Selection

This project exists to test Datadog's **host-level SSI auto-instrumentation** for .NET on Windows. With SSI enabled, the Datadog tracer automatically attaches to .NET processes on startup. **Workload Selection** rules let you control exactly which processes are instrumented.

### Prerequisites

1. Install the Datadog Agent with SSI enabled:

```powershell
$p = Start-Process -Wait -PassThru msiexec -ArgumentList '/qn /i "https://windows-agent.datadoghq.com/datadog-agent-7-latest.amd64.msi" /log C:\Windows\SystemTemp\install-datadog.log APIKEY="<YOUR_API_KEY>" SITE="datadoghq.com" DD_APM_INSTRUMENTATION_ENABLED="host" DD_APM_INSTRUMENTATION_LIBRARIES="dotnet:3"'
```

2. Install the rule compiler (or let `deploy.ps1` download it automatically):

```powershell
Invoke-WebRequest -Uri "https://github.com/DataDog/dd-policy-engine/releases/download/v0.1.1/dd-rules-converter-win-x64.zip" -OutFile "dd-rules-converter.zip"
Expand-Archive -Path "dd-rules-converter.zip" -DestinationPath "C:\tools"
```

### Compile and apply rules.toml

Edit `rules.toml` in the repo root, then compile it:

```powershell
C:\tools\dd-rules-converter.exe -rules rules.toml -output "C:\ProgramData\Datadog\managed\rc-orgwide-wls-policy.bin"
```

The tracer loads the compiled policy automatically the next time a .NET process starts — no agent restart needed. Restart the proxy services to pick up new rules:

```powershell
Get-Service WindowsProxyService.* | Restart-Service
```

### How the proxy services map to rule selectors

All proxy instances share one binary, so `process.executable` matches all of them at once. Use `dotnet.dll` or a naming convention for per-instance targeting.

| Selector | Value | Matches |
|----------|-------|---------|
| `process.executable` | `WindowsProxyService.exe` | All proxy instances |
| `process.executable` | `*ProxyService.exe` | All proxy instances (wildcard) |
| `dotnet.dll` | `WindowsProxyService.dll` | All proxy instances (by DLL) |
| `process.executable` | `WindowsDashboardService.exe` | Dashboard service only |

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

Confirm the compiled policy file exists:

```
C:\ProgramData\Datadog\managed\rc-orgwide-wls-policy.bin
```

---

## Validate

### Using the Dashboard

Open [http://localhost:5051](http://localhost:5051) — click **Test** on any card to fire a request through that proxy and see the live response. Status badges refresh automatically every 5 seconds.

### Using PowerShell

**Status endpoint (each proxy instance):**

```powershell
Invoke-RestMethod http://localhost:5052/api/status
Invoke-RestMethod http://localhost:5053/api/status
Invoke-RestMethod http://localhost:5054/api/status
Invoke-RestMethod http://localhost:5055/api/status
Invoke-RestMethod http://localhost:5056/api/status
```

**WindowsProxyService.OpenMeteo — port 5052**

```powershell
# Current weather for Dallas, TX
Invoke-RestMethod "http://localhost:5052/v1/forecast?latitude=32.78&longitude=-96.80&current_weather=true"

# Hourly temperature for the next 3 days
Invoke-RestMethod "http://localhost:5052/v1/forecast?latitude=32.78&longitude=-96.80&hourly=temperature_2m,windspeed_10m&forecast_days=3"
```

**WindowsProxyService.CatFacts — port 5053**

```powershell
Invoke-RestMethod "http://localhost:5053/fact"
Invoke-RestMethod "http://localhost:5053/facts?page=1&limit=5"
```

**WindowsProxyService.JsonPlaceholder — port 5054**

```powershell
Invoke-RestMethod "http://localhost:5054/posts/1"
Invoke-RestMethod "http://localhost:5054/posts/1/comments"

# Simulated POST (returns the created resource)
Invoke-RestMethod "http://localhost:5054/posts" -Method Post `
  -ContentType "application/json" `
  -Body '{"title":"Test","body":"Hello proxy","userId":1}'
```

**WindowsProxyService.DogCeo — port 5055**

```powershell
Invoke-RestMethod "http://localhost:5055/api/breeds/image/random"
Invoke-RestMethod "http://localhost:5055/api/breeds/list/all"
```

**WindowsProxyService.ChuckNorris — port 5056**

```powershell
Invoke-RestMethod "http://localhost:5056/jokes/random"
Invoke-RestMethod "http://localhost:5056/jokes/random?category=science"
Invoke-RestMethod "http://localhost:5056/jokes/categories"
```

---

## Windows Service Names

| Service Name | Port | Upstream |
|---|---|---|
| `WindowsProxyService.OpenMeteo` | 5052 | https://api.open-meteo.com |
| `WindowsProxyService.CatFacts` | 5053 | https://catfact.ninja |
| `WindowsProxyService.JsonPlaceholder` | 5054 | https://jsonplaceholder.typicode.com |
| `WindowsProxyService.DogCeo` | 5055 | https://dog.ceo |
| `WindowsProxyService.ChuckNorris` | 5056 | https://api.chucknorris.io |
| `WindowsDashboardService` | 5051 | *(serves the local dashboard UI)* |

## Log Format

Each proxy service emits single-line JSON:

```json
{
  "Timestamp": "2026-03-05 10:15:00",
  "Level": "Information",
  "Message": "Proxy 'OpenMeteo' starting on +:5052, forwarding to https://api.open-meteo.com",
  "Category": "WindowsProxyService.Program",
  "Scopes": [{ "Instance": "OpenMeteo" }]
}
```

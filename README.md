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

- **InstanceName** — matches the `--name` argument. The registered Windows Service name is `WindowsProxyService.<InstanceName>`.
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
Get-Service WindowsProxyService.* | Start-Service
```

## Uninstall

```powershell
.\scripts\uninstall-services.ps1 -PublishPath "C:\Services\WindowsProxyService"
```

## Running Locally (Console Mode)

```powershell
dotnet run --project src/WindowsProxyService -- --name OpenMeteo
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

# Create a new post (simulated — returns the created resource)
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

| Service Name | Port | Upstream |
|---|---|---|
| `WindowsProxyService.OpenMeteo` | 5052 | https://api.open-meteo.com |
| `WindowsProxyService.CatFacts` | 5053 | https://catfact.ninja |
| `WindowsProxyService.JsonPlaceholder` | 5054 | https://jsonplaceholder.typicode.com |
| `WindowsProxyService.DogCeo` | 5055 | https://dog.ceo |
| `WindowsProxyService.ChuckNorris` | 5056 | https://api.chucknorris.io |

## Log Format

Each log entry is a single-line JSON object:

```json
{"Timestamp":"2026-03-05 10:15:00","Level":"Information","Message":"Proxy 'OpenMeteo' starting on +:5052, forwarding to https://api.open-meteo.com","Category":"WindowsProxyService.Program","Scopes":[{"Instance":"OpenMeteo"}]}
```

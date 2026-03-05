## Requirements: WindowsProxyService

### 1. Executive Summary

The **WindowsProxyService** is a .NET 8/9 worker service acting as a configurable HTTP proxy. It is designed to be deployed as multiple Windows Service instances from a single codebase to test multi-hop networking and log aggregation.

### 2. Core Features (Updated)

- **Instance-Specific Logging:** Every log entry will include the `InstanceName` (e.g., "Proxy-1") to allow for easy filtering when logs from multiple services are aggregated.
- **JSON Structured Logging:** All console/system logs will be output in a single-line JSON format.
- **Headless Proxying:** Uses `YARP` (Yet Another Reverse Proxy) for high-performance request forwarding.
- **Windows Service Lifecycle:** Managed via `sc.exe`, supporting Start, Stop, and Restart commands.

### 3. Technical Requirements

- **Runtime:** .NET 8.0 or later.
- **Configuration:** A shared `services.json` file.
- **Logging Format:**

```json
{
  "Timestamp": "2026-03-05T10:15:00Z",
  "Level": "Information",
  "Message": "Request proxied to https://api.open-meteo.com",
  "Instance": "Proxy-1",
  "TraceId": "..."
}
```

---

## Execution Plan

### Phase 1: Development

1. **Project Setup:** Create a .NET Worker Service (including scaffolding as a github ready project)
2. **Add YARP:** Install the `Yarp.ReverseProxy` NuGet package.
3. **JSON Logging Config:** Update `Program.cs` to use the JSON console formatter:

```csharp
builder.Logging.AddJsonConsole(options => {
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});

```

4. **Argument Parsing:** Implement logic to read `--name [InstanceName]` from the command line at startup to find the correct config in `services.json`.

### Phase 2: Build & Deployment

1. **Compile:** Generate a single `publish` folder containing the `.exe` and the `services.json`.
2. **Automation Script:** Use PowerShell to loop through the JSON file and register each service.

```powershell
# Example installation snippet
$name = "Proxy-1"
$binPath = "C:\Services\WindowsProxyService.exe --name $name"
sc.exe create $name binPath= $binPath start= auto

```

### Phase 3: Validation

1. Start all services via PowerShell: `Get-Service Proxy-* | Start-Service`.
2. Verify logs: Check Event Viewer or redirected console output to ensure logs are valid JSON strings.
3. Test Proxy: Hit `http://localhost:5052` and verify the redirect to Open-Meteo works.

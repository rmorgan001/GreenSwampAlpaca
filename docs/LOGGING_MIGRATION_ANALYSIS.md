# GreenSwamp Alpaca — ASCOM Logging Migration Analysis
**Report Date:** 2026-05-03 08:29
**Author:** GitHub Copilot analysis for Andy
**Branch:** master
**Scope:** Replace `ASCOM.Tools` logger infrastructure with ASP.NET `Microsoft.Extensions.Logging` equivalents, while preserving the `MonitorLog` / `MonitorQueue` pipeline entirely.

---

## 1. Executive Summary

The solution currently uses two distinct logging systems that must be treated differently:

| System | Purpose | Action |
|--------|---------|--------|
| **ASCOM `ILogger` / `ConsoleLogger` / `TraceLogger`** | Bootstrap and framework-level startup logging in `Program.cs` and the `ASCOM.Alpaca.Razor` library | **Replace** with `Microsoft.Extensions.Logging.ILogger<T>` |
| **`MonitorLog` / `MonitorQueue` pipeline** | Domain-level structured logging — device, category, type filtering, UI monitor window, file persistence | **Preserve unchanged** |

The two systems are completely independent. The `MonitorLog` pipeline never calls the ASCOM `ILogger`; it uses its own `BlockingCollection` queue. The ASCOM `ILogger` is only used for server startup diagnostics, Swagger setup warnings, discovery errors, and validation messages.

---

## 2. Current ASCOM Logging Architecture

### 2.1 ASCOM Logger Types in Use

| Type | Location | Description |
|------|----------|-------------|
| `ASCOM.Common.Interfaces.ILogger` | `Program.cs` line 32 | Static field `Program.Logger` — the ASCOM logger interface |
| `ASCOM.Tools.ConsoleLogger` | `Program.cs` line 46 | Concrete implementation, writes to `Console` |
| `ASCOM.Tools.TraceLogger` (commented out) | `Program.cs` line 47 | Alternative file-based ASCOM logger |
| `ASCOM.Tools.ConsoleLogger` | `ASCOM.Alpaca.Razor/Logging.cs` line 18 | Default logger used by the Razor library |

### 2.2 Static `ASCOM.Alpaca.Logging` Wrapper Class

**File:** `ASCOM.Alpaca.Razor/Logging.cs`

This class is the central hub the Razor library uses to emit log messages. It holds a static `ILogger` instance (`ASCOM.Common.Interfaces.ILogger`) and exposes:

```
Logging.LogError(string)
Logging.LogWarning(string)
Logging.LogVerbose(string)
Logging.LogAPICall(IPAddress, string, uint, uint, uint)   // internal
Logging.AttachLogger(ILogger)                             // called from Program.cs
```

**Method signature mapping to Microsoft.Extensions.Logging:**

| ASCOM Logging method | MEL equivalent |
|----------------------|----------------|
| `Logging.LogError(msg)` | `logger.LogError(msg)` |
| `Logging.LogWarning(msg)` | `logger.LogWarning(msg)` |
| `Logging.LogVerbose(msg)` | `logger.LogDebug(msg)` |
| `Logging.LogAPICall(...)` | `logger.LogDebug(...)` |

### 2.3 Call Sites for `ASCOM.Alpaca.Logging` (Static)

| File | Call count | Methods called |
|------|-----------|----------------|
| `ASCOM.Alpaca.Razor/StartupHelpers.cs` | 8 | `LogWarning`, `LogError` |
| `ASCOM.Alpaca.Razor/Controllers/BaseController.cs` | ~3 | `LogError` |
| `GreenSwamp.Alpaca.Settings/Services/VersionedSettingsService.cs` | 3 | `LogError`, `LogWarning`, `LogVerbose` |
| `GreenSwamp.Alpaca.Settings/Services/SettingsTemplateService.cs` | 5 | `LogError`, `LogWarning`, `LogVerbose` |

### 2.4 `Program.Logger` static field usage

`Program.Logger` (type `ASCOM.Common.Interfaces.ILogger?`) is used extensively **within `Program.cs` only**. It is assigned `new ASCOM.Tools.ConsoleLogger()` before the DI container is built, making it a pre-DI bootstrap logger. It is also passed to the Razor library via `ASCOM.Alpaca.Logging.AttachLogger(Logger)`.

Total call sites in `Program.cs`: approximately **45 `Logger.LogInformation/Warning/Error` calls** covering:
- Port collision detection
- Settings validation
- Device registration
- Lifecycle events (startup/shutdown)

### 2.5 `Microsoft.Extensions.Logging.ILogger<T>` already in use

Several **GreenSwamp.Alpaca.Server** services already correctly use DI-injected `ILogger<T>`:

| File | Logger type |
|------|------------|
| `Controllers/SetupDevicesController.cs` | `ILogger<SetupDevicesController>` (DI injected) |
| `Services/DeviceManagementService.cs` | `ILogger<DeviceManagementService>` (DI injected) |

These files are **already correct** and require no changes.

---

## 3. MonitorLog / MonitorQueue Pipeline (MUST NOT CHANGE)

### 3.1 Architecture

```
Call site (any project)
  └─ MonitorLog.LogToMonitor(MonitorEntry)
	   └─ MonitorQueue.AddEntry(entry)          [BlockingCollection]
			└─ ProcessEntryQueueItem(entry)
				 ├─ WriteOutSession()            → GSSessionLog*.txt
				 ├─ WriteOutErrors()             → GSErrorLog*.txt
				 ├─ WriteOutMonitor()            → GSMonitorLog*.txt
				 ├─ MonitorEntry property set    → UI PropertyChanged event
				 └─ ProcessChartItems()          → CmdjSentEntry / Cmdj2SentEntry
```

### 3.2 MonitorEntry structure

```csharp
MonitorEntry {
	DateTime Datetime
	int      Index
	MonitorDevice   Device     // Server | Telescope | Focuser | Ui
	MonitorCategory Category   // Other | Driver | Interface | Server | Mount | Notes | Alignment
	MonitorType     Type       // Information | Data | Warning | Error | Debug
	string   Method
	int      Thread
	string   Message
}
```

### 3.3 Usage statistics — MonitorLog call sites

| File | Call count |
|------|-----------|
| `Server/TelescopeDriver/Telescope.cs` | 239 |
| `Mount.SkyWatcher/Commands.cs` | 84 |
| `MountControl/Mount.Serial.cs` | 49 |
| `MountControl/Axes.cs` | 32 |
| `Mount.SkyWatcher/SkyWatcher.cs` | 32 |
| `MountControl/Mount.cs` | 26 |
| `MountControl/Mount.Operations.cs` | 14 |
| `MountControl/Mount.Tracking.cs` | 14 |
| `MountControl/SlewController.cs` | 11 |
| `Simulator/Actions.cs` | 11 |
| `MountControl/SkyPredictor.cs` | 10 |
| `MountControl/Mount.Tasks.cs` | 10 |
| `MountControl/CommandQueue.cs` | 10 |
| `MountControl/Mount.Motion.cs` | 8 |
| `Shared/Transport/SerialOverUdpPort.cs` | 8 |
| `MountControl/AutoHome/AutohomeSky.cs` | 8 |
| `MountControl/Transforms.cs` | 6 |
| `MountControl/AutoHome/AutohomeSim.cs` | 4 |
| `Mount.SkyWatcher/SkyQueue.cs` | 4 |
| `Mount.Commands/CommandQueueBase.cs` | 4 |
| `MountControl/SkySettings.cs` | 4 |
| `MountControl/Mount.Lifecycle.cs` | 3 |
| `MountControl/Mount.Position.cs` | 3 |
| `Simulator/IOSerial.cs` | 2 |
| `Shared/Settings.cs` | 2 |
| `MountControl/Mount.Pec.cs` | 2 |
| `MountControl/Pulses/HCPulses.cs` | 2 |
| `MountControl/Mount.Init.cs` | 1 |
| **Total** | **~673** |

> ⚠️ **None of these call sites should be modified.** The MonitorLog pipeline is the application's internal structured logging system and is completely independent of the ASCOM `ILogger`.

### 3.4 User-configurable monitor settings (preserved)

The `MonitorSettings` section in `appsettings.json` controls all filter dimensions. These settings are loaded by `Settings.Load()` and applied by `MonitorLog.Load_Settings()`:

```json
"MonitorSettings": {
  "ServerDevice": true,   // Device filter
  "Telescope": true,
  "Ui": false,
  "Other": false,         // Category filter
  "Driver": true,
  "Interface": true,
  "Server": true,
  "Mount": true,
  "Alignment": false,
  "Information": true,    // Type/level filter
  "Data": false,
  "Warning": true,
  "Error": true,
  "Debug": false,
  "LogMonitor": false,    // Output to file
  "LogSession": true,     // Session log file
  "LogCharting": false,   // Charting log file
  "StartMonitor": true    // Enable monitor window
}
```

These filters are entirely within the `GreenSwamp.Alpaca.Shared` / `GreenSwamp.Alpaca.Settings` domain and are **not affected by this migration**.

---

## 4. NuGet Package Dependencies

### 4.1 ASCOM packages currently referenced for logging

| Project | Package | Used for logging? |
|---------|---------|-------------------|
| `GreenSwamp.Alpaca.Server` | `ASCOM.Tools 3.1.0` | ✅ Yes — `ConsoleLogger` in `Program.cs` |
| `GreenSwamp.Alpaca.Server` | `ASCOM.AstrometryTools 3.1.0` | ❌ No — astrometry calculations |
| `ASCOM.Alpaca.Razor` | `ASCOM.Tools 3.1.0` | ✅ Yes — `ConsoleLogger` in `Logging.cs` |
| `ASCOM.Alpaca.Razor` | `ASCOM.Common.Components 3.1.0` | ⚠️ Partial — provides `ILogger` interface, also used for device types |
| `ASCOM.Alpaca.Razor` | `ASCOM.Alpaca.Device 3.1.0` | ❌ No — device protocol |
| `GreenSwamp.Alpaca.MountControl` | `ASCOM.Tools 3.1.0` | ❌ No — astrometry/coord transforms |
| `GreenSwamp.Alpaca.Principles` | `ASCOM.Tools 3.1.0` | ❌ No — time/math utilities |

> ⚠️ `ASCOM.Tools` **cannot be fully removed** from `ASCOM.Alpaca.Razor`, `GreenSwamp.Alpaca.MountControl`, or `GreenSwamp.Alpaca.Principles` because they depend on it for non-logging functionality (astrometry, coordinate transforms, device protocol). However, `ASCOM.Tools` can be removed from `GreenSwamp.Alpaca.Server.csproj` once `ConsoleLogger` is eliminated.

### 4.2 Packages already present for MEL

`GreenSwamp.Alpaca.Settings` already references:
- `Microsoft.Extensions.Logging.Abstractions 10.0.7`

The ASP.NET framework reference in `ASCOM.Alpaca.Razor` (`Microsoft.AspNetCore.App`) already provides `Microsoft.Extensions.Logging`.

---

## 5. Proposed Migration Plan

### 5.1 Phase 1 — Bridge `ASCOM.Alpaca.Logging` to MEL (Low Risk)

**Target file:** `ASCOM.Alpaca.Razor/Logging.cs`

Replace the static `ASCOM.Common.Interfaces.ILogger` backing field with `Microsoft.Extensions.Logging.ILogger`. Keep the same public API (`LogError`, `LogWarning`, `LogVerbose`, `AttachLogger`) so all 16+ call sites in the Razor library continue to compile unchanged.

```csharp
// Before
static internal ASCOM.Common.Interfaces.ILogger Log { get; } = new ASCOM.Tools.ConsoleLogger();
public static void AttachLogger(ASCOM.Common.Interfaces.ILogger log) { Log = log; }

// After
private static Microsoft.Extensions.Logging.ILogger? _log;
public static void AttachLogger(Microsoft.Extensions.Logging.ILogger log) { _log = log; }
public static void LogError(string message)   => _log?.LogError(message);
public static void LogWarning(string message) => _log?.LogWarning(message);
public static void LogVerbose(string message) => _log?.LogDebug(message);
```

**Impact:** All existing `Logging.LogXxx(...)` call sites in `StartupHelpers.cs`, `BaseController.cs`, `VersionedSettingsService.cs`, and `SettingsTemplateService.cs` remain unchanged syntactically.

### 5.2 Phase 2 — Replace `Program.Logger` with MEL (Medium Risk)

**Target file:** `GreenSwamp.Alpaca.Server/Program.cs`

The challenge is that `Program.Logger` is used **before the DI container is built** (lines 46–100+). ASP.NET Core's `WebApplication.CreateBuilder` provides a `LoggerFactory` that can be used for pre-build logging.

**Approach:**
1. Replace `internal static ASCOM.Common.Interfaces.ILogger? Logger` with `internal static ILogger<Program>? Logger`.
2. Use `LoggerFactory.Create(...)` for bootstrap logging before `builder.Build()`.
3. After `builder.Build()`, obtain the DI-resolved `ILogger<Program>` and replace the bootstrap instance.
4. Pass the MEL logger to `ASCOM.Alpaca.Logging.AttachLogger(...)` (now accepting `ILogger`).

```csharp
// Bootstrap (pre-DI)
using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
Logger = loggerFactory.CreateLogger<Program>();

// Post-build (production DI logger)
Logger = app.Services.GetRequiredService<ILogger<Program>>();
ASCOM.Alpaca.Logging.AttachLogger(Logger);
```

**Logging level configuration** (replaces ASCOM `ConsoleLogger` / `TraceLogger` choice):

```json
// appsettings.json
"Logging": {
  "LogLevel": {
	"Default": "Information",
	"Microsoft.AspNetCore": "Warning"
  }
}
```

For file logging add `Microsoft.Extensions.Logging.File` (community) or Serilog/NLog via a sink — **not required in the first pass**. Console logging is provided by `AddConsole()` (from `Microsoft.Extensions.Logging.Console`).

**User configurable logging level:** The existing `"Logging"` section in `appsettings.json` is already correctly structured for MEL. Users can set `"Default": "Debug"` to get verbose output.

### 5.3 Phase 3 — Wire File Logging (Optional, Replaces `TraceLogger`)

`ASCOM.Tools.TraceLogger` writes to a dated `.txt` file. To replicate this with MEL, add Serilog with a rolling file sink or use `Microsoft.Extensions.Logging.File`:

```csharp
builder.Logging.AddFile("Logs/greenswamp-{Date}.txt");
```

Or with Serilog:
```csharp
builder.Host.UseSerilog((ctx, cfg) =>
	cfg.ReadFrom.Configuration(ctx.Configuration)
	   .WriteTo.Console()
	   .WriteTo.File("Logs/greenswamp-.txt", rollingInterval: RollingInterval.Day));
```

Configuration stays in `appsettings.json` — fully user-accessible. This is the equivalent of switching from `ConsoleLogger` to `TraceLogger` in the old code.

### 5.4 Phase 4 — Remove `ASCOM.Tools` from `Server.csproj` (Low Risk)

Once `ConsoleLogger` usage is gone from `Program.cs`, the `ASCOM.Tools` `PackageReference` in `GreenSwamp.Alpaca.Server.csproj` can be removed. The `ASCOM.AstrometryTools` reference must stay (JPLEPH).

---

## 6. Files Requiring Changes

### 6.1 Required changes

| File | Change |
|------|--------|
| `ASCOM.Alpaca.Razor/Logging.cs` | Replace backing field from `ASCOM.Common.Interfaces.ILogger` to `Microsoft.Extensions.Logging.ILogger`; keep public API surface |
| `GreenSwamp.Alpaca.Server/Program.cs` | Replace `ASCOM.Common.Interfaces.ILogger?` field and `ASCOM.Tools.ConsoleLogger` instantiation with MEL bootstrap + DI |
| `GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj` | Remove `ASCOM.Tools` package reference |

### 6.2 Files requiring no changes (already correct)

| File | Reason |
|------|--------|
| `GreenSwamp.Alpaca.Shared/Monitor.cs` | MonitorLog pipeline — untouched |
| `GreenSwamp.Alpaca.Shared/MonitorQueue.cs` | MonitorLog pipeline — untouched |
| `GreenSwamp.Alpaca.Shared/Settings.cs` | MonitorLog settings — untouched |
| `ASCOM.Alpaca.Razor/StartupHelpers.cs` | Calls `Logging.LogXxx()` — no change needed (Phase 1 keeps API) |
| `ASCOM.Alpaca.Razor/Controllers/BaseController.cs` | Calls `Logging.LogXxx()` — no change needed |
| `GreenSwamp.Alpaca.Settings/Services/VersionedSettingsService.cs` | Calls `ASCOM.Alpaca.Logging.LogXxx()` — no change needed |
| `GreenSwamp.Alpaca.Settings/Services/SettingsTemplateService.cs` | Calls `ASCOM.Alpaca.Logging.LogXxx()` — no change needed |
| `Server/Controllers/SetupDevicesController.cs` | Already uses `ILogger<T>` correctly |
| `Server/Services/DeviceManagementService.cs` | Already uses `ILogger<T>` correctly |
| All 28 `MonitorLog.LogToMonitor` call-site files (~673 calls) | MonitorLog pipeline — untouched |

---

## 7. Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|-----------|
| Pre-DI bootstrap logging gap | Medium | Use `LoggerFactory.Create()` for the period before `builder.Build()` |
| `ASCOM.Common.Interfaces.ILogger` referenced in `Settings` project via `ASCOM.Alpaca.Razor` | Low | `GreenSwamp.Alpaca.Settings` already has `Microsoft.Extensions.Logging.Abstractions`; no new packages needed |
| `ASCOM.Tools` still needed by `ASCOM.Alpaca.Razor` for non-logging | Low | Only remove from `Server.csproj`; keep in `ASCOM.Alpaca.Razor.csproj` |
| `TraceLogger` file logging lost | Low | Covered by Phase 3 (Serilog or `AddFile`) |
| Console output format changes | Very Low | MEL default console format is adequate; structured JSON format available |
| Thread safety of `Logging._log` static field | Low | Assign once at startup; no concurrent writes to field |

---

## 8. Affected NuGet Packages After Migration

### To add
| Package | Project | Purpose |
|---------|---------|---------|
| `Microsoft.Extensions.Logging.Console` | `GreenSwamp.Alpaca.Server` | Console output for MEL (already transitively present via ASP.NET, but explicit is cleaner) |

### Optionally add (Phase 3)
| Package | Project | Purpose |
|---------|---------|---------|
| `Serilog.AspNetCore` | `GreenSwamp.Alpaca.Server` | Rich file + console sinks |
| `Serilog.Sinks.File` | `GreenSwamp.Alpaca.Server` | Rolling file output |

### To remove
| Package | Project | Reason |
|---------|---------|--------|
| `ASCOM.Tools` | `GreenSwamp.Alpaca.Server` | No longer needed after `ConsoleLogger` removed |

---

## 9. Migration Steps (Ordered Implementation Sequence)

1. **Edit `ASCOM.Alpaca.Razor/Logging.cs`** — swap internal field from `ASCOM.Common.Interfaces.ILogger` to `Microsoft.Extensions.Logging.ILogger?`; update `AttachLogger` signature; redirect `LogError/Warning/Verbose` to MEL calls. Keep `LogVerbose` → `LogDebug`.

2. **Edit `GreenSwamp.Alpaca.Server/Program.cs`**:
   a. Change field declaration to `internal static ILogger<Program>? Logger;`
   b. Replace `Logger = new ASCOM.Tools.ConsoleLogger()` with `LoggerFactory.Create(...)` bootstrap
   c. After `app = builder.Build()`, replace with `app.Services.GetRequiredService<ILogger<Program>>()`
   d. Update `ASCOM.Alpaca.Logging.AttachLogger(Logger)` — signature now matches MEL `ILogger`

3. **Edit `GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj`** — remove `<PackageReference Include="ASCOM.Tools" />`

4. **Build and verify** — confirm 0 new errors; confirm startup log messages appear on console as before

5. *(Optional Phase 3)* Add Serilog or file logging provider for rolling-file output equivalent to `TraceLogger`

---

## 10. `appsettings.json` Logging Configuration (Post-Migration)

```json
"Logging": {
  "LogLevel": {
	"Default": "Information",
	"Microsoft.AspNetCore": "Warning",
	"GreenSwamp": "Information"
  }
}
```

For verbose/debug output (equivalent to old `TraceLogger`):
```json
"Logging": {
  "LogLevel": {
	"Default": "Debug"
  }
}
```

For file output with Serilog (Phase 3):
```json
"Serilog": {
  "MinimumLevel": "Information",
  "WriteTo": [
	{ "Name": "Console" },
	{
	  "Name": "File",
	  "Args": {
		"path": "Logs/greenswamp-.txt",
		"rollingInterval": "Day",
		"retainedFileCountLimit": 7
	  }
	}
  ]
}
```

---

## 11. Summary

The migration is **surgical and low-risk**. The key insight is that the ASCOM logging system (`ILogger`, `ConsoleLogger`, `Logging.cs`) touches only **3 files that need editing** and **2 files that incidentally call `Logging.LogXxx()`** — but those 2 need **no changes** because the Phase 1 shim preserves the public API. The `MonitorLog` pipeline spanning ~673 call sites across 28 files is completely isolated and untouched.

The user-configurable logging levels and the choice of console vs file output are preserved and improved: they migrate from commented-out code (`// Logger = new ASCOM.Tools.TraceLogger(...)`) to first-class `appsettings.json` configuration that the user can change without recompiling.

---

*Report generated: 2026-05-03 08:29*
*Workspace: T:\source\repos\GreenSwampAlpaca*

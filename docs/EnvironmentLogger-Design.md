# Environment Logger – Design Document

**Author:** Andy  
**Created:** 2026-05-30 14:12  
**Last Updated:** 2026-05-30 14:23  
**Status:** Decisions recorded – ready for implementation

---

## 1. Purpose

At startup, GreenSwamp Alpaca Server writes a single, human-readable environment log file that captures the runtime conditions of the host machine. This file is the first thing a support engineer or developer examines when a bug report arrives. The log must be generated on **both Windows and Linux** without any platform-specific dependencies such as WMI or WPF.

---

## 2. Reference: GSServer WPF Implementation

The existing WPF application (`GS.Shared\Environment`) was reviewed. The following table summarises each source file and its role:

| File | Role | Cross-platform? |
|---|---|---|
| `EnvironmentHelper.cs` | Convenience façade; resolves the log path and calls `EnvironmentLogger` | ✅ Yes (once `GsFile` replaced) |
| `EnvironmentLogger.cs` | Orchestrator; wraps the sync work in a `CancellationTokenSource` with timeout; handles cleanup of old logs | ✅ Yes |
| `EnvironmentInfo.cs` | Collects core info via `System.Environment`, `RuntimeInformation`, `Process`, `DriveInfo`, `CultureInfo` | ⚠️ Partially – contains `WindowsIdentity` (admin check) and `System.Windows` (WPF screen info) |
| `WmiEnvironmentInfo.cs` | Collects richer hardware details via `System.Management` (WMI) | ❌ Windows-only |

### Key design patterns worth keeping

* **Timeout-protected logging** – the entire write is wrapped in a `CancellationTokenSource` so a slow or frozen WMI/hardware query never blocks the application startup path.
* **Cancellation checkpoints** – each section checks `cancellationToken.IsCancellationRequested` before proceeding.
* **Privacy by default** – `ObscureText()` masks all but the first and last characters of machine name, username, and user domain. `ObscurePath()` specifically handles `\Users\<name>` path segments.
* **Graceful degradation** – every section is wrapped in its own `try/catch`; a failure in one section does not suppress subsequent sections.
* **Rolling log retention** – `CleanupOldLogs()` keeps only the *N* most recent files.
* **Async first, sync fallback** – `LogEnvironmentAsync` is the primary entry point; `LogEnvironmentSync` is available for constrained call sites.

---

## 3. Goals for the New Implementation

1. Run identically on **Windows 10/11** and **Ubuntu / Debian Linux** (arm64 and x64).
2. No `System.Management` (WMI) – replaced with cross-platform alternatives.
3. No `System.Windows` – WPF screen/DPI info replaced with Blazor-server equivalents or omitted.
4. Integrate with the existing `SettingsPathResolver` for consistent log placement (service mode vs. interactive mode).
5. Use `Microsoft.Extensions.Logging` (`ILogger`) for any internal diagnostic output rather than bare `Console.WriteLine`.
6. Expose the log path through the existing `IVersionedSettingsService` contract so the Blazor UI can surface a "Download Environment Log" link.
7. Keep the **timeout + cancellation** pattern from the reference implementation.
8. Keep the **privacy masking** logic from the reference implementation.

---

## 4. Information Sections

The sections below define what to capture and how to do it cross-platform. Sections marked **Windows extra** are only attempted when `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` is true.

### 4.1 Application

| Field | Source |
|---|---|
| Assembly name, version, file version, product version | `Assembly.GetEntryAssembly()` |
| Copyright | `FileVersionInfo.GetVersionInfo` |
| Build date | `File.GetLastWriteTime(assembly.Location)` |
| Assembly location (path obscured) | `assembly.Location` |

### 4.2 Operating System

| Field | Source |
|---|---|
| OS description | `RuntimeInformation.OSDescription` |
| OS architecture | `RuntimeInformation.OSArchitecture` |
| OS version | `Environment.OSVersion` |
| 64-bit OS | `Environment.Is64BitOperatingSystem` |
| Machine name (masked) | `Environment.MachineName` |
| User name (masked) | `Environment.UserName` |
| User domain (masked, Windows only) | `Environment.UserDomainName` |
| System directory | `Environment.SystemDirectory` (Windows) / `/etc` sentinel (Linux) |
| Uptime | `Environment.TickCount64` cast to `TimeSpan` |

**Linux extra** – read `/proc/version` and `/etc/os-release` for a richer OS description when running on Linux.

### 4.3 Runtime

| Field | Source |
|---|---|
| CLR version | `Environment.Version` |
| Framework description | `RuntimeInformation.FrameworkDescription` |
| 64-bit process | `Environment.Is64BitProcess` |
| Logical processor count | `Environment.ProcessorCount` |
| System page size | `Environment.SystemPageSize` |
| Running as admin / root | **Windows:** `WindowsPrincipal.IsInRole(Administrator)` · **Linux:** `Environment.UserName == "root"` or `geteuid() == 0` via P/Invoke |

### 4.4 Process

| Field | Source |
|---|---|
| PID, name, start time | `Process.GetCurrentProcess()` |
| Thread count, handle count | `Process.Threads.Count`, `Process.HandleCount` |
| Working set, private memory, virtual memory, peak working set | `Process.*` properties |
| GC memory, max generation | `GC.GetTotalMemory(false)`, `GC.MaxGeneration` |
| Command-line arguments (first 10) | `Environment.GetCommandLineArgs()` |

### 4.5 Hardware – CPU

| Field | Source |
|---|---|
| Logical processor count | `Environment.ProcessorCount` |
| CPU name | **Windows extra:** `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0\ProcessorNameString` via registry · **Linux:** parse `/proc/cpuinfo` for `model name` |
| Physical core count | **Windows extra:** registry / `GetLogicalProcessorInformation` P/Invoke · **Linux:** count unique `core id` entries in `/proc/cpuinfo` |
| Max clock speed (MHz) | **Windows extra:** registry `~MHz` value · **Linux:** parse `/proc/cpuinfo` `cpu MHz` |

### 4.6 Hardware – Memory

| Field | Source |
|---|---|
| Total physical memory | **Windows:** `GlobalMemoryStatusEx` P/Invoke (via helper or `PerformanceCounter`) · **Linux:** parse `/proc/meminfo` `MemTotal` |
| Available physical memory | Same sources for `MemAvailable` / `AvailablePhysicalMemory` |
| GC reported memory | `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` (.NET 5+) |

### 4.7 Culture & Locale

| Field | Source |
|---|---|
| Current culture / UI culture | `CultureInfo.CurrentCulture`, `CultureInfo.CurrentUICulture` |
| Installed UI culture | `CultureInfo.InstalledUICulture` |
| Time zone, UTC offset, DST | `TimeZoneInfo.Local` |

### 4.8 Paths

All paths that may contain a username are passed through `ObscurePath()`.

| Field | Source |
|---|---|
| Current directory | `Environment.CurrentDirectory` |
| Base directory | `AppDomain.CurrentDomain.BaseDirectory` |
| Temp path | `Path.GetTempPath()` |
| AppData (Roaming / Local) | `Environment.GetFolderPath(...)` |
| Home / Documents | `Environment.GetFolderPath(MyDocuments)` / `$HOME` |
| Settings root | `SettingsPathResolver.GetSettingsRoot()` |
| Log directory | `SettingsPathResolver.GetLogPath()` |

### 4.9 Drives / File Systems

| Field | Source |
|---|---|
| Drive/mount name, type, available, total | `DriveInfo.GetDrives()` – cross-platform |

### 4.10 Network

| Field | Source |
|---|---|
| Host name (masked) | `System.Net.Dns.GetHostName()` |
| Network interfaces (name, type, MAC last-4-visible, IPv4, IPv6) | `System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()` |

**MAC address masking rule:** replace all but the last two octets with `**`. Example: `A4:C3:F0:85:AC:D1` → `**:**:**:**:AC:D1`.

### 4.11 Blazor / ASP.NET Core Server

Replaces the WPF screen/DPI section. This section is populated from information available server-side and does not require a browser round-trip.

| Field | Source |
|---|---|
| Kestrel listen addresses | `IServer` / `IServerAddressesFeature` |
| Application lifetime start time | `IHostApplicationLifetime` or `Process.StartTime` |
| Environment name | `IHostEnvironment.EnvironmentName` |
| Content root | `IHostEnvironment.ContentRootPath` |
| Web root | `IWebHostEnvironment.WebRootPath` |

---

## 5. Privacy Masking Rules

The masking logic from GSServer is carried forward unchanged:

```csharp
// ObscureText: keeps first and last chars, replaces middle with '*'
// e.g. "DESKTOP-ABC123" → "D**********3"
static string ObscureText(string text)

// ObscurePath: locates \Users\<name>\ or /home/<name>/ and masks <name>
// Windows: T:\Users\A***y\AppData → T:\Users\A***y\AppData
// Linux:   /home/a***y/.config    → /home/a***y/.config
static string ObscurePath(string path)
```

Linux home paths use `/home/<name>/` as the pattern instead of `\Users\`.

---

## 6. Architecture

### 6.1 Proposed File Layout

```
GreenSwamp.Alpaca.Shared\
  Environment\
	EnvironmentLogger.cs          # Orchestrator (async, timeout, cleanup)
	EnvironmentInfo.cs            # Cross-platform core sections
	PlatformEnvironmentInfo.cs    # Windows & Linux hardware helpers
	EnvironmentHelper.cs          # Convenience façade (startup call)
```

`GreenSwamp.Alpaca.Shared` already targets both `net8.0` and `net10.0` and has no platform-specific packages, making it the correct home for this code.

### 6.2 Class Responsibilities

#### `EnvironmentLogger` (public static)

Mirrors the GSServer design:

```
LogEnvironmentAsync(logFilePath, timeoutSeconds, ct) → Task
LogEnvironmentSync(logFilePath, timeoutSeconds)
CleanupOldLogs(directory, pattern, keepCount)
```

Timeout is enforced with a linked `CancellationTokenSource`. On cancellation or exception, a `[TIMEOUT]` / `[ERROR]` trailer is appended.

#### `EnvironmentInfo` (internal static)

Collects all sections that work on every OS without P/Invoke:

* `LogApplicationInfo(writer)`
* `LogOperatingSystemInfo(writer)`
* `LogRuntimeInfo(writer)`
* `LogProcessInfo(writer)`
* `LogCultureInfo(writer)`
* `LogPathInfo(writer)`
* `LogDriveInfo(writer)`
* `LogNetworkInfo(writer)`

Privacy helpers (`ObscureText`, `ObscurePath`) live here as private static methods.

#### `PlatformEnvironmentInfo` (internal static)

Houses the conditionally compiled hardware queries:

```csharp
static void LogCpuInfo(StreamWriter writer)       // Windows registry vs /proc/cpuinfo
static void LogMemoryInfo(StreamWriter writer)     // GlobalMemoryStatusEx vs /proc/meminfo
static void LogAdminInfo(StreamWriter writer)      // WindowsPrincipal vs geteuid
```

Each method uses `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` guards rather than `#if` preprocessor directives, keeping a single binary with runtime dispatch.

#### `EnvironmentHelper` (public static)

Convenience façade matching the GSServer pattern:

```csharp
Task<string?> LogToDefaultLocationAsync()
string? LogToDefaultLocation()
string GetDefaultLogPath()
string GetLogDirectory()
```

`GetDefaultLogPath()` delegates to `SettingsPathResolver` rather than to the WPF-specific `GsFile.GetLogPath()`.

### 6.3 Sequence Diagram

```
Program.cs / Startup
  └─► EnvironmentHelper.LogToDefaultLocationAsync()
		├─ SettingsPathResolver.GetLogPath()  → resolve path
		├─ EnvironmentLogger.LogEnvironmentAsync(path, 10s, ct)
		│     ├─ EnvironmentInfo.LogApplicationInfo()
		│     ├─ EnvironmentInfo.LogOperatingSystemInfo()
		│     ├─ EnvironmentInfo.LogRuntimeInfo()
		│     ├─ EnvironmentInfo.LogProcessInfo()
		│     ├─ PlatformEnvironmentInfo.LogCpuInfo()     ← Windows registry OR /proc/cpuinfo
		│     ├─ PlatformEnvironmentInfo.LogMemoryInfo()  ← GlobalMemoryStatusEx OR /proc/meminfo
		│     ├─ PlatformEnvironmentInfo.LogAdminInfo()   ← WindowsPrincipal OR geteuid
		│     ├─ EnvironmentInfo.LogCultureInfo()
		│     ├─ EnvironmentInfo.LogPathInfo()
		│     ├─ EnvironmentInfo.LogDriveInfo()
		│     └─ EnvironmentInfo.LogNetworkInfo()
		└─ EnvironmentLogger.CleanupOldLogs(dir, "GreenSwampEnv*.log", keep=3)
```

---

## 7. Integration Points

### 7.1 Application Startup

**Decision: startup only** – the log is triggered once at startup; there is no on-demand UI trigger.

Call `EnvironmentHelper.LogToDefaultLocationAsync()` in `Program.cs` immediately after the DI container is built but before the server starts accepting requests. Fire-and-forget so the log write never delays server startup:

```csharp
// Program.cs – fire and forget, do not block server startup
_ = EnvironmentHelper.LogToDefaultLocationAsync()
		.ContinueWith(t =>
		{
			if (t.Exception is not null)
				logger.LogWarning(t.Exception, "Environment log failed");
		});
```

### 7.2 Log File Path

Path is resolved by `SettingsPathResolver`:

| Context | Example path |
|---|---|
| Windows interactive | `%AppData%\GreenSwampAlpaca\<version>\Logs\GreenSwampEnv_2026-05-30_141200.log` |
| Windows service | `C:\Users\Public\Documents\GreenSwampServer\<version>\Logs\GreenSwampEnv_*.log` |
| Linux interactive | `~/.config/GreenSwampAlpaca/<version>/Logs/GreenSwampEnv_*.log` |
| Linux systemd | `~/GreenSwampServer/<version>/Logs/GreenSwampEnv_*.log` |

### 7.3 Blazor UI

**Decision: no UI download button.** The log is written to disk at startup only. Support engineers access it directly from the log directory resolved by `SettingsPathResolver`. No Blazor component or download endpoint is required.

---

## 8. Platform-Specific Implementation Notes

### 8.1 CPU Name – Windows

```csharp
using var key = Registry.LocalMachine.OpenSubKey(
	@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
writer.WriteLine($"CPU: {key?.GetValue("ProcessorNameString")}");
writer.WriteLine($"Max Speed: {key?.GetValue("~MHz")} MHz");
```

Guard with `OperatingSystem.IsWindows()` so the compiler suppresses the CA1416 warning.

### 8.2 CPU Name – Linux

```csharp
var lines = File.ReadAllLines("/proc/cpuinfo");
var model = lines.FirstOrDefault(l => l.StartsWith("model name"))?.Split(':')[1].Trim();
writer.WriteLine($"CPU: {model}");
```

### 8.3 Total Physical Memory – Windows

Use `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` as a first approximation (available in .NET 5+), then optionally P/Invoke `GlobalMemoryStatusEx` for full accuracy:

```csharp
[DllImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
```

### 8.4 Total Physical Memory – Linux

```csharp
var lines = File.ReadAllLines("/proc/meminfo");
var totalKb = long.Parse(lines[0].Split(':')[1].Trim().Split(' ')[0]);
writer.WriteLine($"Total RAM: {totalKb / (1024.0 * 1024):N2} GB");
```

### 8.5 Admin / Root Detection

```csharp
if (OperatingSystem.IsWindows())
{
	var identity = WindowsIdentity.GetCurrent();
	var principal = new WindowsPrincipal(identity);
	isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
}
else
{
	// geteuid returns 0 for root
	isAdmin = Environment.UserName == "root";
}
```

---

## 9. Decisions

All questions resolved by Andy on 2026-05-30.

| # | Question | **Decision** |
|---|---|---|
| 1 | Startup trigger vs on-demand UI | **Startup only** – log written once at application startup; no on-demand trigger |
| 2 | Code location | **`GreenSwamp.Alpaca.Shared`** project – minimal disruption, already multi-targets `net8.0`/`net10.0` |
| 3 | MAC address masking | **Last 4 hex characters (2 octets) visible** – e.g. `**:**:**:**:AC:D1` |
| 4 | macOS `/proc/cpuinfo` parsing | **No** – Windows and Linux only |
| 5 | Blazor UI download button | **No** – log accessed directly from disk by support engineers |

---

## 10. Out of Scope

* Live hardware monitoring / metrics streaming (separate feature).
* Sending the log automatically to a remote endpoint or issue tracker.
* ASCOM-specific device enumeration (handled by ASCOM Platform separately).

---

*End of document*

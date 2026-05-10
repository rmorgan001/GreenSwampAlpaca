# Fast Monitor Buffer — Feature Design Document

**Document status:** Final
**Author:** Andy / GitHub Copilot
**Date:** 2026-05-10 10:24
**Target solution:** GreenSwamp Alpaca

---

## 1. Overview

This document describes the design for a **Fast Monitor Buffer** — an optional, high-performance
in-memory rotating buffer that replaces the existing async file-per-entry monitor writer
(`WriteOutMonitor` / `FileWriteAsync`) when enabled via a server configuration setting.

All existing call-site code (`MonitorLog.LogToMonitor(...)`, `MonitorQueue.AddEntry(...)`)
continues to work unchanged.

---

## 2. Goals

| # | Goal |
|---|------|
| G1 | Zero changes to any existing call-site code |
| G2 | Replace the per-entry async file write for monitor entries with an in-memory rotating ring buffer |
| G3 | Buffer capacity: **200 records** (circular, oldest entry discarded when full) |
| G4 | Buffer is flushed to a new **datetime-stamped file** on an explicit `WriteBuffer()` call |
| G5 | Feature is opt-in via a new `bool FastMonitor` server configuration setting |
| G6 | When `FastMonitor = false` (default), existing `WriteOutMonitor` behaviour is preserved exactly |

---

## 3. Existing Architecture (as-is)

### 3.1 Data flow

```
Caller
  └─ MonitorLog.LogToMonitor(MonitorEntry)   [Monitor.cs – static]
	   └─ MonitorQueue.AddEntry(entry)        [MonitorQueue.cs – static]
			└─ BlockingCollection<MonitorEntry>  (background Task consumer)
				 └─ ProcessEntryQueueItem(entry)
					  ├─ WriteOutErrors(entry)   → FileWriteAsync → GSErrorLog*.txt
					  ├─ WriteOutSession(entry)  → FileWriteAsync → GSSessionLog*.txt
					  └─ WriteOutMonitor(entry)  → FileWriteAsync → GSMonitorLog*.txt
													(only when Settings.StartMonitor
													 && Settings.LogMonitor are true)
```

### 3.2 Key files

| File | Role |
|------|------|
| `GreenSwamp.Alpaca.Shared/Monitor.cs` | Static `MonitorLog` facade, filter lists, `LogToMonitor()` entry point |
| `GreenSwamp.Alpaca.Shared/MonitorQueue.cs` | Static queue, background consumer, all `WriteOut*` methods, `FileWriteAsync` |
| `GreenSwamp.Alpaca.Shared/Settings.cs` | Static settings bridge — properties read by `MonitorQueue` (e.g. `Settings.LogMonitor`, `Settings.StartMonitor`) |
| `GreenSwamp.Alpaca.Settings/Models/MonitorSettings.cs` | JSON-serialisable settings model |
| `GreenSwamp.Alpaca.Server/Pages/MonitorSettings.razor` | Blazor UI for monitor settings |

### 3.3 `MonitorEntry` fields

```
DateTime        Datetime
int             Index
MonitorDevice   Device
MonitorCategory Category
MonitorType     Type
string          Method
int             Thread
string          Message
```

### 3.4 `WriteOutMonitor` — the target method to replace/wrap

Located in `MonitorQueue.cs` (~line 390):

```csharp
private static void WriteOutMonitor(MonitorEntry entry)
{
	if (!Settings.LogMonitor) return;
	FileWriteAsync(
		Path.Combine(GsFile.GetLogPath(), "GSMonitorLog") + FileName,
		$"{entry.Datetime...}|...");
}
```

The async `FileWriteAsync` acquires a `SemaphoreSlim(1)` for every single entry, which is the
bottleneck this feature eliminates.

---

## 4. Proposed Architecture (to-be)

### 4.1 New setting: `FastMonitor`

Add `bool FastMonitor { get; set; } = false;` to:

1. `GreenSwamp.Alpaca.Settings/Models/MonitorSettings.cs` — JSON model property
2. `GreenSwamp.Alpaca.Shared/Settings.cs` — static bridge property (following the existing pattern)

When `FastMonitor = true`:
- `WriteOutMonitor` **skips** `FileWriteAsync` and instead calls `FastMonitorBuffer.Add(entry)`.
- The session and error writers are **not affected** — they always write to file as before.

When `FastMonitor = false` (default):
- Existing behaviour is fully preserved.

### 4.2 New class: `FastMonitorBuffer` (in `GreenSwamp.Alpaca.Shared`)

New file:

```
GreenSwamp.Alpaca.Shared/
  FastMonitorBuffer.cs      ← NEW
```

Responsibilities:
- Maintain a **thread-safe rotating ring buffer** of 200 `MonitorEntry` records.
- Expose `Add(MonitorEntry entry)` — called from `WriteOutMonitor` when `FastMonitor` is true.
- Expose `WriteBuffer()` — writes the current buffer contents to a new datetime-stamped file
  and does **not** clear the buffer (ring continues rolling).

#### 4.2.1 Ring buffer implementation

Use a fixed-size array with a lock-protected write index that wraps at capacity.

```
Capacity    = 200
_buffer     = MonitorEntry[200]
_writeIndex = int (0-based, wraps with % Capacity)
_count      = int (capped at Capacity, tracks how many valid entries exist)
_lock       = object (standard monitor lock)
```

The ring overwrites the oldest entry when full — standard circular buffer semantics.

#### 4.2.2 `WriteBuffer()` method

- Snapshots the current buffer contents **in insertion order** (oldest to newest).
- Writes each record as a pipe-delimited line matching the existing `GSMonitorLog` format:
  `yyyy-MM-dd HH:mm:ss.fff|{index:0000#}|{Device}|{Category}|{Type}|{Thread}|{Method}|{Message}`
- Output filename: `GSFastMonitorLog{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt` in the standard
  `GsFile.GetLogPath()` directory.
- File write uses `StreamWriter` with `using` — **synchronous**, since `WriteBuffer()` is an
  explicit user action, not a hot-path operation.
- Concurrent `WriteBuffer()` calls are serialised via a dedicated `SemaphoreSlim(1, 1)` owned
  by `FastMonitorBuffer`.

### 4.3 Changes to `MonitorQueue.WriteOutMonitor`

The only change to `MonitorQueue.cs` in the hot path is one extra branch:

```csharp
private static void WriteOutMonitor(MonitorEntry entry)
{
	if (!Settings.LogMonitor) return;

	if (Settings.FastMonitor)
	{
		FastMonitorBuffer.Add(entry);   // ← new branch
		return;
	}

	// Original path — unchanged
	FileWriteAsync(
		Path.Combine(GsFile.GetLogPath(), "GSMonitorLog") + FileName,
		$"...");
}
```

### 4.4 New public API: `MonitorQueue.WriteBuffer()`

A new public static method is added to `MonitorQueue` so callers use a consistent, familiar
entry point and `FastMonitorBuffer` is not accessed directly from outside `Shared`:

```csharp
/// <summary>
/// Flushes the fast monitor ring buffer to a datetime-stamped file.
/// Only has effect when Settings.FastMonitor is true.
/// </summary>
public static void WriteBuffer()
{
	if (!Settings.FastMonitor) return;
	FastMonitorBuffer.WriteBuffer();
}
```

---

## 5. Settings Changes Detail

### 5.1 `MonitorSettings.cs` (model)

Add inside the `#region Logging Options` block:

```csharp
/// <summary>
/// When true, monitor entries are written to an in-memory rotating buffer (200 records)
/// instead of being written to file on each entry. Call MonitorQueue.WriteBuffer() to persist.
/// Default: false (standard async file logging).
/// </summary>
public bool FastMonitor { get; set; } = false;
```

### 5.2 `Settings.cs` (bridge)

Add following the `LogMonitor` property pattern:

```csharp
private static bool _fastMonitor;
public static bool FastMonitor
{
	get => _fastMonitor;
	set
	{
		if (_fastMonitor == value) return;
		_fastMonitor = value;
		LogSetting(MethodBase.GetCurrentMethod()?.Name, $"{value}");
		OnStaticPropertyChanged();
	}
}
```

Also add one line to the `LoadFromService(MonitorSettings settings)` method:

```csharp
FastMonitor = settings.FastMonitor;
```

### 5.3 `MonitorSettings.razor`

The two checkboxes `LogMonitor` and `FastMonitor` are **mutually exclusive**. Each uses
`ValueChanged` to turn the other off when it is turned on. A **Write Buffer** button is added
below, enabled only when `FastMonitor` is on.

```razor
@* Log Monitor — turns off FastMonitor when enabled *@
<MudCheckBox Value="_settings.LogMonitor"
			 ValueChanged="@((bool v) => { _settings.LogMonitor = v; if (v) _settings.FastMonitor = false; })"
			 Label="Log Monitor" Color="Color.Primary" Class="mt-2" />
<MudText Typo="Typo.caption" Color="Color.Secondary">GSMonitorLog*.txt</MudText>

@* Fast Monitor — turns off LogMonitor when enabled *@
<MudCheckBox Value="_settings.FastMonitor"
			 ValueChanged="@((bool v) => { _settings.FastMonitor = v; if (v) _settings.LogMonitor = false; })"
			 Label="Fast Monitor (Buffer)" Color="Color.Info" Class="mt-2" />
<MudText Typo="Typo.caption" Color="Color.Secondary">
	In-memory 1000-record ring buffer. Mutually exclusive with Log Monitor.
</MudText>

@* Write Buffer action button — visible and enabled only when FastMonitor is active *@
@if (_settings.FastMonitor)
{
	<MudButton Variant="Variant.Outlined" Color="Color.Info" Size="Size.Small"
			   StartIcon="@Icons.Material.Filled.Save" Class="mt-2"
			   OnClick="WriteBufferAsync" Disabled="_writingBuffer">
		@if (_writingBuffer)
		{
			<MudProgressCircular Size="Size.Small" Indeterminate="true" Class="me-1" />
		}
		Write Buffer to File
	</MudButton>
}
```

A `_writingBuffer` bool field and `WriteBufferAsync` method are added to the `@code` block:

```csharp
private bool _writingBuffer;

private async Task WriteBufferAsync()
{
	_writingBuffer = true;
	try
	{
		await Task.Run(() => GreenSwamp.Alpaca.Shared.MonitorQueue.WriteBuffer());
		_message = $"Fast monitor buffer written to file at {DateTime.Now:HH:mm:ss}";
		_isError = false;
	}
	catch (Exception ex)
	{
		_message = $"Error writing buffer: {ex.Message}";
		_isError = true;
	}
	finally
	{
		_writingBuffer = false;
	}
}
```

---

## 6. `FastMonitorBuffer` — Detailed Specification

### 6.1 File location

`GreenSwamp.Alpaca.Shared/FastMonitorBuffer.cs`

### 6.2 Class skeleton

```csharp
namespace GreenSwamp.Alpaca.Shared
{
	/// <summary>
	/// Thread-safe rotating ring buffer for fast monitor logging.
	/// Holds up to <see cref="Capacity"/> MonitorEntry records.
	/// Oldest records are silently overwritten when the buffer is full.
	/// Call <see cref="WriteBuffer"/> to persist a snapshot to disk.
	/// </summary>
	public static class FastMonitorBuffer
	{
		public  const  int            Capacity = 200;
		private static MonitorEntry[] _buffer  = new MonitorEntry[Capacity];
		private static int            _writeIndex;   // next write slot (0-based, wraps)
		private static int            _count;        // valid entry count (capped at Capacity)
		private static readonly object        _lock     = new object();
		private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);
		private const  string                 Fmt       = "0000#";

		public static void Add(MonitorEntry entry) { ... }
		public static void WriteBuffer()           { ... }
		internal static MonitorEntry[] GetSnapshot() { ... }
	}
}
```

### 6.3 `Add` algorithm

```csharp
lock (_lock)
{
	_buffer[_writeIndex] = entry;
	_writeIndex = (_writeIndex + 1) % Capacity;
	if (_count < Capacity) _count++;
}
```

### 6.4 `GetSnapshot` algorithm

Returns entries in insertion order (oldest first):

```csharp
lock (_lock)
{
	var snapshot = new MonitorEntry[_count];
	int startIndex = (_count < Capacity) ? 0 : _writeIndex;  // oldest slot
	for (int i = 0; i < _count; i++)
		snapshot[i] = _buffer[(startIndex + i) % Capacity];
	return snapshot;
}
```

### 6.5 `WriteBuffer` algorithm

```csharp
public static void WriteBuffer()
{
	var snapshot = GetSnapshot();
	if (snapshot.Length == 0) return;

	_fileLock.Wait();
	try
	{
		var filePath = Path.Combine(
			GsFile.GetLogPath(),
			$"GSFastMonitorLog{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt");

		Directory.CreateDirectory(
			Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException());

		using var sw = new StreamWriter(filePath, append: false, System.Text.Encoding.UTF8);
		int idx = 0;
		foreach (var entry in snapshot)
		{
			idx++;
			sw.WriteLine(
				$"{entry.Datetime.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}" +
				$"|{idx.ToString(Fmt)}" +
				$"|{entry.Device}|{entry.Category}|{entry.Type}" +
				$"|{entry.Thread}|{entry.Method}|{entry.Message}");
		}
	}
	finally
	{
		_fileLock.Release();
	}
}
```

> **Note on async:** `WriteBuffer()` is declared synchronous because it is an explicit user
> action (not on the hot logging path) and synchronous `StreamWriter` is sufficient here.
> If the caller is a Blazor event handler, it should invoke `Task.Run(() => MonitorQueue.WriteBuffer())`
> to avoid blocking the render thread.

---

## 7. File Naming Convention

| File type | Pattern | Example |
|-----------|---------|---------|
| Existing monitor log | `GSMonitorLog{yyyy-MM-dd-HH}.txt` | `GSMonitorLog2026-05-10-10.txt` |
| Fast monitor buffer flush | `GSFastMonitorLog{yyyy-MM-dd-HH-mm-ss}.txt` | `GSFastMonitorLog2026-05-10-10-16-42.txt` |

Second-precision suffix guarantees uniqueness across multiple `WriteBuffer()` calls within
the same session.

Old `GSFastMonitorLog` files are cleaned up automatically. A new `DeleteFiles` call is added
to the `MonitorQueue` static constructor alongside the existing four calls:

```csharp
DeleteFiles("GSFastMonitorLog", 7, GsFile.GetLogPath());
```

---

## 8. Thread Safety Summary

| Concern | Mechanism |
|---------|-----------|
| Concurrent `Add()` calls | `lock (_lock)` on array write |
| Concurrent `WriteBuffer()` calls | `SemaphoreSlim(1, 1)` on file write |
| Snapshot isolation | Snapshot taken inside the same `lock` as `Add` writes |
| No data loss on flush | Buffer is **not** cleared after `WriteBuffer()`; ring continues rolling |

---

## 9. Affected Files Summary

| File | Change type | Notes |
|------|------------|-------|
| `GreenSwamp.Alpaca.Shared/FastMonitorBuffer.cs` | **NEW** | Core ring buffer implementation |
| `GreenSwamp.Alpaca.Shared/MonitorQueue.cs` | **MODIFY** | One branch in `WriteOutMonitor`; new `WriteBuffer()` public method; one extra `DeleteFiles` call in static ctor |
| `GreenSwamp.Alpaca.Shared/Settings.cs` | **MODIFY** | New `FastMonitor` bridge property + one line in `LoadFromService` |
| `GreenSwamp.Alpaca.Settings/Models/MonitorSettings.cs` | **MODIFY** | New `FastMonitor` JSON property in Logging Options region |
| `GreenSwamp.Alpaca.Server/Pages/MonitorSettings.razor` | **MODIFY** | New checkbox in Logging Options card |

**Files with zero changes (all call-site callers):** Every file calling `MonitorLog.LogToMonitor()`
or `MonitorQueue.AddEntry()` — none require any modification.

---

## 10. Out of Scope

- Changes to session log (`WriteOutSession`) or error log (`WriteOutErrors`) — untouched.
- Pulse log path — untouched.
- Blazor real-time UI display of buffer contents — potential future enhancement.
- Automatic periodic `WriteBuffer()` scheduling — not required by the current specification.

---

## 11. Design Decisions (Andy, 2026-05-10)

All open questions from Section 11 have been resolved:

| # | Question | Decision |
|---|----------|----------|
| OQ1 | Expose `WriteBuffer()` as a Blazor button? | **Yes** — button added to the Logging Options card on `MonitorSettings.razor`, enabled only when `FastMonitor` is active |
| OQ2 | `FastMonitor` and `LogMonitor` mutually exclusive? | **Yes** — toggling one on turns the other off via `ValueChanged` callbacks in the UI |
| OQ3 | Reset buffer after `WriteBuffer()`? | **No** — ring continues rolling; flush is non-destructive |
| OQ4 | Filename precision? | **Second** precision is sufficient (`yyyy-MM-dd-HH-mm-ss`) |

---

*End of document — 2026-05-10 10:24*



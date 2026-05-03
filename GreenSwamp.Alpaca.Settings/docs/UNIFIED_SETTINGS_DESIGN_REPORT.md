# Unified Settings Design Report

## GreenSwamp Alpaca — Settings Architecture Evaluation



**Author:** GitHub Copilot (for Andy)

**Date:** 2026-05-02 19:08

**Branch:** master

**Scope:** Evaluate options to replace the ASCOM-platform settings mechanism (`ASCOM.Tools.XMLProfile`) with a unified, ASCOM-platform-independent design.



---



## 1. Executive Summary



The GreenSwamp Alpaca server currently uses **two parallel, incompatible settings systems**:



| System | Owner | Storage | Mechanism |

|--------|-------|---------|-----------|

| **GreenSwamp Settings** (`GreenSwamp.Alpaca.Settings`) | Device/mount/monitor config | JSON files in `%AppData%\GreenSwampAlpaca\{version}\` | `IVersionedSettingsService` |

| **ASCOM Server Settings** (`ServerSettings.cs`) | Alpaca server/network config | ASCOM XML profile store (`%AppData%\ASCOM\...`) | `ASCOM.Tools.XMLProfile` |



The goal is to **eliminate the ASCOM platform dependency** from the server settings layer and deliver a single, unified, JSON-backed settings system for all configuration.



---



## 2. Current Architecture — Detailed Analysis



### 2.1 GreenSwamp.Alpaca.Settings (the modern system)



This system is already well-designed and ASCOM-platform-free:



- **`IVersionedSettingsService` / `VersionedSettingsService`** — Singleton DI service; async, thread-safe (per-device `SemaphoreSlim` locks), atomic writes (temp-file rename). Stores files in `%AppData%\GreenSwampAlpaca\{version}\`.

- **File layout:**

  - `appsettings.user.json` — Monitor/logging filter settings (`MonitorSettings`)

  - `appsettings.alpaca.user.json` — Alpaca discovery metadata (`AlpacaDevice[]`)

  - `device-nn.settings.json` — Per-device mount settings (`SkySettings`)

  - `observatory.settings.json` — Observatory physical properties (`ObservatorySettings`)

- **Template system** — `common.json`, `germanpolar-overrides.json`, etc. provide factory defaults; `[CommonSetting]` / `[UniqueSetting]` attributes control what is shared vs. per-mode.

- **Validation** — `ValidationResult` with errors/warnings; health-check UI at `/settings-health`.

- **Events** — `DeviceSettingsChanged`, `MonitorSettingsChanged` for reactive UI.

- **DI Registration** — `AddVersionedSettings()` extension; properly scoped as singleton.



**Assessment: This system is production-ready and should be the target design for all settings.**



---



### 2.2 ASCOM Server Settings (the legacy system)



**`ServerSettings.cs`** (`GreenSwamp.Alpaca.Server`) is a `static` class wrapping `ASCOM.Tools.XMLProfile`:



```csharp

internal static ASCOM.Tools.XMLProfile Profile = 

	new ASCOM.Tools.XMLProfile(Program.DriverID, "Server");

```



**Settings stored in this system:**



| Property | Type | Default |

|----------|------|---------|

| `Location` | string | "Unknown" |

| `AutoStartBrowser` | bool | true |

| `ServerPort` | ushort | 31426 |

| `AllowRemoteAccess` | bool | true |

| `AllowDiscovery` | bool | true |

| `LocalRespondOnlyToLocalHost` | bool | true |

| `PreventRemoteDisconnects` | bool | false |

| `RunSwagger` | bool | true |

| `AllowImageBytesDownload` | bool | true |

| `RunInStrictAlpacaMode` | bool | true |

| `UseAuth` | bool | false |

| `UserName` | string | "User" |

| `Password` | string | (hashed) |

| `LoggingLevel` | enum | Information |

| `GetDeviceUniqueId()` | GUID per device | (generated) |



**Problems with this system:**

1. **ASCOM platform dependency** — requires `ASCOM.Tools` NuGet package; ties the server to ASCOM infrastructure.

2. **XML storage** — stored in ASCOM's Windows registry/XML profile path, not portable, not inspectable as plain text.

3. **`static` class** — not injectable, not testable; tightly coupled to `Program`.

4. **No async support** — all reads/writes are synchronous.

5. **Split from device settings** — server config is in a completely different location from device config, creating a split-brain problem.

6. **`AlpacaConfiguration` wrapper** — `AlpacaConfiguration.cs` is just a pass-through adapter from `ServerSettings` → `IAlpacaConfiguration`; it exists only to bridge this structural gap.

7. **`GetDeviceUniqueId()`** — device UUIDs are stored in the ASCOM profile, not co-located with device definitions in `appsettings.alpaca.user.json`. The `AlpacaDevice.UniqueId` already exists in the GreenSwamp system but the ASCOM version shadows it.

8. **`--reset` CLI arg** calls `ServerSettings.Reset()` which clears the ASCOM profile, not the JSON files.



---



### 2.3 ASCOM Alpaca Razor (infrastructure dependency)



The `ASCOM.Alpaca.Razor` project provides:

- `IAlpacaConfiguration` interface (server identity/network configuration contract)

- `DeviceManager` (telescope device registry, routing ASCOM API calls)

- `StartupHelpers` (Swagger, discovery, authentication pipeline)

- HTTP API controllers (Alpaca protocol)



This project **cannot** be removed — it implements the Alpaca wire protocol. However, **its dependency on `ASCOM.Tools.XMLProfile` through `ServerSettings` can and should be severed**. The `IAlpacaConfiguration` interface it defines is a clean boundary — the implementation (`AlpacaConfiguration`) is entirely in the server project and can be changed freely.



---



### 2.4 Dependency Map



```

GreenSwamp.Alpaca.Server

  ├── ServerSettings.cs ──────────────────► ASCOM.Tools.XMLProfile  [REMOVE]

  ├── AlpacaConfiguration.cs ─────────────► ServerSettings          [REPLACE]

  ├── Program.cs ─────────────────────────► ServerSettings          [REPLACE]

  └── IVersionedSettingsService (DI) ─────► appsettings.user.json  [EXTEND]



GreenSwamp.Alpaca.Settings

  └── IVersionedSettingsService ──────────► JSON files              [TARGET]

```



---



## 3. Options Analysis



### Option A — Extend IVersionedSettingsService to absorb ServerSettings (Recommended)



Add a new `ServerConfig` settings model and integrate it fully into the existing `IVersionedSettingsService` pattern.



**Design:**

- Add `ServerConfig` model class (mirrors all 14+ `ServerSettings` properties).

- Store in `appsettings.server.user.json` (same `%AppData%\GreenSwampAlpaca\{version}\` root).

- Add `GetServerConfig()` / `SaveServerConfigAsync()` to `IVersionedSettingsService`.

- Replace `AlpacaConfiguration.cs` to read from `IVersionedSettingsService` (injected, not static).

- Delete `ServerSettings.cs`.

- Move `GetDeviceUniqueId()` logic to use `AlpacaDevice.UniqueId` in `appsettings.alpaca.user.json` (already exists).

- Add factory defaults in `appsettings.json` (existing config builder pipeline already loads this).



**File layout after migration:**

```

%AppData%\GreenSwampAlpaca\{version}\

  appsettings.user.json         (MonitorSettings — unchanged)

  appsettings.alpaca.user.json  (AlpacaDevice[] — unchanged)

  appsettings.server.user.json  (ServerConfig — NEW)

  observatory.settings.json     (ObservatorySettings — unchanged)

  device-nn.settings.json       (SkySettings — unchanged)

```



**Pros:**

- ✅ Single settings system, single storage root

- ✅ Zero ASCOM platform dependency in settings layer

- ✅ Consistent JSON format and tooling

- ✅ Async, thread-safe (consistent with existing service)

- ✅ Injectable and testable (`IVersionedSettingsService` already registered in DI)

- ✅ Leverages existing validation infrastructure

- ✅ Can fire `ServerConfigChanged` event for reactive Blazor UI updates

- ✅ Minimal scope — only `ServerSettings.cs` and `AlpacaConfiguration.cs` change in the server project

- ✅ `appsettings.json` factory defaults already used by `VersionedSettingsService`

- ✅ `GetDeviceUniqueId()` duplication eliminated — UUIDs consolidate into `AlpacaDevice.UniqueId`

- ✅ `--reset` CLI arg can clear the JSON file instead of the ASCOM profile



**Cons:**

- ⚠️ `ServerSettings` is accessed before DI container is built (port binding, startup URL args). Requires a small bootstrap read of `appsettings.server.user.json` before `builder.Build()`.

- ⚠️ Existing ASCOM profile data (stored in `%AppData%\ASCOM\`) is not automatically migrated. A one-time migration step is needed on first run.



**Effort estimate:** Medium — ~3–5 days including tests and migration path.



---



### Option B — New Standalone `ServerConfigService` (Clean Separation)



Create a dedicated `IServerConfigService` / `ServerConfigService` that is independent of `IVersionedSettingsService`.



**Design:**

- New `IServerConfigService` interface in `GreenSwamp.Alpaca.Settings`.

- Backed by `appsettings.server.user.json` (same root as above).

- Register separately in DI: `services.AddSingleton<IServerConfigService, ServerConfigService>()`.

- `AlpacaConfiguration` implements `IAlpacaConfiguration` by reading from `IServerConfigService`.



**Pros:**

- ✅ Clean single-responsibility separation (server config vs. device/monitor config)

- ✅ Easier to extend independently

- ✅ No risk of touching `IVersionedSettingsService` interface



**Cons:**

- ⚠️ Two services to manage, register, and inject instead of one

- ⚠️ Creates another interface/abstraction where one already exists

- ⚠️ Violates the existing "one service, one settings root" design pattern already in the codebase

- ⚠️ Does not eliminate the `ASCOM.Tools` dependency faster than Option A

- ⚠️ Bootstrap-before-DI problem is the same as Option A



**Effort estimate:** Medium — similar to Option A but more boilerplate.



---



### Option C — Microsoft.Extensions.Options + `appsettings.json` sections



Use the built-in `IOptions<T>` / `IOptionsMonitor<T>` pattern directly for server configuration.



**Design:**

- Add a `[ServerConfig]` section to `appsettings.json` (defaults) and `appsettings.user.json` (user overrides).

- Bind with `services.Configure<ServerConfig>(configuration.GetSection("ServerConfig"))`.

- Saves via `IWritableOptions<T>` pattern (a thin wrapper that reads the JSON file, patches the section, and rewrites it).



**Pros:**

- ✅ Uses only .NET BCL — zero additional dependencies

- ✅ `IOptionsMonitor<T>` provides hot-reload / change notification natively

- ✅ Very lightweight; no custom service needed

- ✅ Well-understood pattern in .NET ecosystem



**Cons:**

- ⚠️ `IWritableOptions<T>` is not part of the BCL — you must implement the writable wrapper yourself, or add a library like `Microsoft.Extensions.Configuration.Binder`

- ⚠️ Saving requires reading and rewriting the full JSON file without the atomic temp-rename safety that `VersionedSettingsService` provides

- ⚠️ `appsettings.user.json` is already used for `MonitorSettings` by `IVersionedSettingsService` — mixing two patterns in one file creates ambiguity

- ⚠️ Hot-reload is complex with file-based configs on Windows

- ⚠️ Bootstrap-before-DI problem remains (need port before builder is built)

- ⚠️ Diverges from the existing design pattern, creating a mixed codebase that is harder to maintain



**Effort estimate:** Low-Medium initially, but higher maintenance cost long-term due to inconsistency.



---



### Option D — Keep ASCOM.Tools but wrap behind an interface



Wrap `ASCOM.Tools.XMLProfile` behind a new `IProfileStore` interface so it can be swapped later without touching `ServerSettings`.



**Design:**

- `IProfileStore` with `GetValue`, `WriteValue`, `Clear`, `ContainsKey`.

- `XmlProfileStore : IProfileStore` wraps `ASCOM.Tools.XMLProfile`.

- `ServerSettings` becomes an instance class injected with `IProfileStore`.



**Pros:**

- ✅ Minimal code change — existing property logic is preserved verbatim

- ✅ Easy to unit test by swapping with a `DictionaryProfileStore`

- ✅ Low risk



**Cons:**

- ❌ Does NOT eliminate the `ASCOM.Tools` NuGet dependency — you still ship it

- ❌ Does NOT unify storage — ASCOM profile path stays in `%AppData%\ASCOM\`

- ❌ Adds an interface wrapping an implementation that will eventually be deleted anyway

- ❌ Creates technical debt: two storage roots still exist

- ❌ Does not address any of the structural problems with the static class design



**Assessment: This is a dead-end transitional step. Not recommended as a final solution.**



---



## 4. Recommendation



**Adopt Option A: Extend `IVersionedSettingsService` to absorb `ServerSettings`.**



This is the least-disruption, highest-value path. The key reasons:



1. `IVersionedSettingsService` is the established, well-tested, DI-registered settings contract.

2. The file storage pattern is consistent and already used across all other settings domains.

3. The `AlpacaConfiguration` adapter is a thin shim — replacing it to read from DI instead of a static class is a small, bounded change.

4. It eliminates the ASCOM platform dependency from the settings layer completely.

5. `AlpacaDevice.UniqueId` already exists in `appsettings.alpaca.user.json` — the `GetDeviceUniqueId()` duplication is already partially resolved.



---



## 5. Proposed Migration Plan (Option A)



### Phase 1 — Add `ServerConfig` model and service contract



1. Create `GreenSwamp.Alpaca.Settings\Models\ServerConfig.cs` with all 14 server properties.

2. Add factory defaults to `GreenSwamp.Alpaca.Server\appsettings.json` under a `"ServerConfig"` key.

3. Add `GetServerConfig()` / `SaveServerConfigAsync()` / `ServerConfigChanged` event to `IVersionedSettingsService`.

4. Implement in `VersionedSettingsService` (store as `appsettings.server.user.json`).



### Phase 2 — Bootstrap read for startup binding



5. Add a static helper `ServerConfig.LoadBootstrap()` that reads `appsettings.server.user.json` directly (before DI) using `System.Text.Json`  — used only for port and remote-access binding at startup in `Program.cs`.



### Phase 3 — Replace server-side consumers



6. Replace `AlpacaConfiguration.cs` to accept `IVersionedSettingsService` via constructor injection.

7. Update `Program.cs`:

   - Replace all `ServerSettings.XYZ` references with `serverConfig.XYZ` from the bootstrap read.

   - Register `AlpacaConfiguration` in DI (it is currently `new`'d manually).

8. Replace `Data\UserService.cs` auth reads from `ServerSettings` with `IVersionedSettingsService`.

9. Update the Setup Blazor page to save via `IVersionedSettingsService`.



### Phase 4 — Migration from ASCOM profile on first run



10. Add a one-time migration in `VersionedSettingsService` constructor: if `appsettings.server.user.json` does not exist **and** the ASCOM profile key exists, read the old values and write the new JSON file. Log a migration message.

11. Add a `--migrate-settings` CLI argument for explicit migration.



### Phase 5 — Remove ASCOM.Tools dependency from Server project



12. Delete `ServerSettings.cs`.

13. Remove `<PackageReference Include="ASCOM.Tools" ...>` from `GreenSwamp.Alpaca.Server.csproj` (verify no other usages remain using `Select-String`).

14. Verify `ASCOM.Tools` is still needed in `GreenSwamp.Alpaca.MountControl.csproj` and `GreenSwamp.Alpaca.Principles.csproj` (it is — for ASCOM type usage; leave those references intact).



### Phase 6 — `IAlpacaConfiguration` decoupling (optional, future)



15. Consider whether `IAlpacaConfiguration` (defined in `ASCOM.Alpaca.Razor`) can be replaced with a GreenSwamp-owned interface to further reduce the ASCOM surface in the server project. This is out of scope for Phase 1–5 and requires careful evaluation of the Alpaca controller pipeline.



---



## 6. Key Risk: Startup Ordering



`ServerSettings.ServerPort` is read in `Program.cs` **before** `WebApplicationBuilder` is created (line ~63), to check if the port is already in use. Similarly, `AllowRemoteAccess` is used to construct the `--urls` binding argument before `builder.Build()`.



This means the new `ServerConfig` model must support a **pre-DI bootstrap read**. The recommended approach:



```csharp

// In Program.cs — before builder is created:

var bootstrapConfig = ServerConfig.LoadBootstrap(

	Path.Combine(

		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),

		"GreenSwampAlpaca",

		GetCurrentVersion(),

		"appsettings.server.user.json"

	),

	fallbackPort: Program.DefaultPort

);

```



This is a simple `File.Exists` → `JsonSerializer.Deserialize<ServerConfig>` call, with fallback to defaults if the file is absent. It mirrors the pattern already used internally by `VersionedSettingsService`.



---



## 7. `IAlpacaConfiguration` Audit



After migration, `AlpacaConfiguration.cs` will implement `IAlpacaConfiguration` by reading from `IVersionedSettingsService`. The interface members map as follows:



| `IAlpacaConfiguration` member | Source after migration |

|-------------------------------|----------------------|

| `RunInStrictAlpacaMode` | `ServerConfig.RunInStrictAlpacaMode` |

| `PreventRemoteDisconnects` | `ServerConfig.PreventRemoteDisconnects` |

| `ServerName` | `Program.ServerName` (const — unchanged) |

| `Manufacturer` | `Program.Manufacturer` (const — unchanged) |

| `ServerVersion` | `Program.ServerVersion` (const — unchanged) |

| `Location` | `ServerConfig.Location` |

| `AllowImageBytesDownload` | `ServerConfig.AllowImageBytesDownload` |

| `AllowDiscovery` | `ServerConfig.AllowDiscovery` |

| `ServerPort` | `ServerConfig.ServerPort` |

| `AllowRemoteAccess` | `ServerConfig.AllowRemoteAccess` |

| `LocalRespondOnlyToLocalHost` | `ServerConfig.LocalRespondOnlyToLocalHost` |

| `RunSwagger` | `ServerConfig.RunSwagger` |



Auth settings (`UseAuth`, `UserName`, `Password`, `LoggingLevel`) are not exposed through `IAlpacaConfiguration`; they remain in `ServerConfig` but are consumed via `IVersionedSettingsService` in `Data\UserService.cs` and the logging setup.



---



## 8. Impact on `ASCOM.Tools` NuGet References



After completing Phase 5, the `ASCOM.Tools` dependency in `GreenSwamp.Alpaca.Server.csproj` can be removed. The dependency remains in:



| Project | Reason to keep |

|---------|----------------|

| `ASCOM.Alpaca.Razor` | Core Alpaca protocol types; cannot remove |

| `GreenSwamp.Alpaca.MountControl` | ASCOM device type enums, coordinate types |

| `GreenSwamp.Alpaca.Principles` | ASCOM astrometry tools |



The `GreenSwamp.Alpaca.Settings.csproj` currently has a `ProjectReference` to `ASCOM.Alpaca.Razor`. This is unusual and should be reviewed — the settings project should ideally not depend on the Alpaca wire-protocol project. The reference appears to be a legacy artefact and may be removable as part of Option A work.



---



## 9. Summary Table



| Criterion | Option A (Recommended) | Option B | Option C | Option D |

|-----------|----------------------|----------|----------|----------|

| Removes ASCOM platform dependency | ✅ | ✅ | ✅ | ❌ |

| Unified storage root | ✅ | ✅ | ⚠️ Partial | ❌ |

| Consistent with existing patterns | ✅ | ⚠️ | ❌ | ⚠️ |

| Testable / Injectable | ✅ | ✅ | ✅ | ✅ |

| Async + thread-safe writes | ✅ | ✅ | ⚠️ | ❌ |

| Atomic file writes | ✅ | ✅ | ❌ | ❌ |

| Change event support | ✅ | ✅ | ✅ (hot-reload) | ❌ |

| Effort (story points) | Medium (8–13) | Medium (8–13) | Low-Med (5–8) | Low (3–5) |

| Long-term maintenance | Low | Low-Med | Med | High |

| Bootstrap-before-DI | Solvable | Solvable | Solvable | N/A |



---



*Report saved to `GreenSwamp.Alpaca.Settings\docs\UNIFIED_SETTINGS_DESIGN_REPORT.md`*

*Last updated: 2026-05-02 19:08*


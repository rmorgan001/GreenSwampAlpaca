# Configuration File Management REST API — Requirements

**Document:** CONFIG-API-REQUIREMENTS.md
**Status:** Approved — ready for implementation planning
**Updated:** 2026-05-03 10:34
**Author:** Andy

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-03 10:19 | Initial draft |
| 2026-05-03 10:34 | File rename decisions applied; all five open questions resolved and closed |

---

## 1. Background and Motivation

The GreenSwamp Alpaca server persists all of its runtime state across five families of
JSON configuration files, all stored under
`%AppData%\GreenSwampAlpaca\{version}\`.

### 1.1 File naming — pre-requisite rename

Before the REST API is implemented, two existing files will be renamed to make the
naming scheme self-consistent across all configuration families.  Any related schema
or template files will be renamed to match.

| Current name | Renamed to | Reason |
|--------------|-----------|--------|
| `appsettings.user.json` | `monitor.settings.user.json` | Consistent with per-resource naming pattern |
| `appsettings.alpaca.user.json` | `devices.alpaca.user.json` | Consistent with per-resource naming pattern |

All references in `IVersionedSettingsService`, `VersionedSettingsService`,
`ConfigurationBuilderExtensions`, and any schema files must be updated as part of
this pre-requisite step before API work begins.

### 1.2 Full file inventory (post-rename)

| File | Model class | Purpose |
|------|-------------|---------|
| `monitor.settings.user.json` | `MonitorSettings` | Server-wide logging / monitor filter switches |
| `devices.alpaca.user.json` | `List<AlpacaDevice>` | Alpaca discovery metadata (one entry per device) |
| `device-nn.settings.json` (0–99) | `SkySettings` | Per-device mount settings |
| `appsettings.server.user.json` | `ServerConfig` | Network, security, and identity settings |
| `observatory.settings.json` | `ObservatorySettings` | Physical observatory properties (lat/lon/elevation) |

Template files in `{version}\templates\`:

| Template file | Purpose |
|---------------|---------|
| `common.json` | Common defaults applied to all alignment modes |
| `germanpolar-overrides.json` | GermanPolar-specific property overrides |
| `polar-overrides.json` | Polar-specific property overrides |
| `altaz-overrides.json` | AltAz-specific property overrides |

Today, all file I/O is performed internally through `IVersionedSettingsService`
(888-line implementation) and `ISettingsTemplateService`. There is **no HTTP
surface** that exposes these files to external tools, automation scripts, or
backup/restore pipelines.

This document defines a REST API that provides full CRUD, upload, and download
access to all configuration files with appropriate validation guards.

---

## 2. Goals

1. CRUD operations for every configuration file family.
2. Download of any current configuration file as a JSON attachment.
3. Upload of a replacement configuration file with pre-acceptance validation.
4. Validation reuses the domain logic already present in
   `IVersionedSettingsService.ValidateDeviceSettings()`,
   `IVersionedSettingsService.ValidateAlpacaDevices()`, and
   `System.ComponentModel.DataAnnotations` on the model classes.
5. The API must fit into the existing controller pattern used by
   `SetupDevicesController` (ASP.NET Core MVC, attribute routing under `/setup`).
6. API responses must be consistent with existing `ErrorResponse` and
   `ValidationResult` model shapes.

---

## 3. Out of Scope (v1)

- Propagating `ObservatorySettings` changes to existing `device-nn.settings.json`
  files (already marked TODO in `VersionedSettingsService`).
- Authentication / authorisation changes — the existing `AuthorizationFilter`
  service-filter applies to all setup controllers and is sufficient for v1.
- Non-JSON configuration assets (e.g., `JPLEPH` ephemeris binary).
- Template file editing — templates are read-only factory defaults.
- Multi-version migration via the API.
- Automatic server restart on configuration changes (changes requiring a restart
  are saved and take effect on the next manual restart).

---

## 4. Proposed API — Overview

All routes are prefixed with `/setup/config` to sit alongside the existing
`/setup/devices` routes in `SetupDevicesController` (or a new, dedicated
`ConfigController`).

### 4.1 Resource model

```
/setup/config/monitor                      MonitorSettings
/setup/config/server                       ServerConfig
/setup/config/observatory                  ObservatorySettings
/setup/config/alpaca-devices               List<AlpacaDevice>
/setup/config/devices/{deviceNumber}       SkySettings  (deviceNumber 0–99)
/setup/config/templates                    read-only index
/setup/config/templates/{name}             read-only template download
```

---

## 5. Detailed Endpoint Specifications

### 5.1 Monitor Settings — `MonitorSettings`

Backed by `monitor.settings.user.json` (renamed from `appsettings.user.json`).

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/setup/config/monitor` | Returns the current `MonitorSettings` object |
| `PUT` | `/setup/config/monitor` | Replaces all monitor settings |
| `GET` | `/setup/config/monitor/download` | Downloads `monitor.settings.user.json` as a file attachment |
| `POST` | `/setup/config/monitor/upload` | Uploads a replacement file (validated before acceptance) |

**GET `/setup/config/monitor`**
- Returns: `200 OK` with `MonitorSettings` body.

**PUT `/setup/config/monitor`**
- Request body: `MonitorSettings` JSON.
- Validates the body via `DataAnnotations`.
- On success: calls `IVersionedSettingsService.SaveMonitorSettingsAsync()`, returns `200 OK`.
- On validation failure: `400 Bad Request` with `ValidationResult` body.

**GET `/setup/config/monitor/download`**
- Returns: `200 OK`, `Content-Type: application/json`,
  `Content-Disposition: attachment; filename="monitor.settings.user.json"`.
- Streams the raw file from `IVersionedSettingsService.UserSettingsPath`
  (property must be updated to reflect the new filename).
- If the file does not yet exist: `404 Not Found`.

**POST `/setup/config/monitor/upload`**
- Request: `multipart/form-data`, single field `file` (`.json`).
- Pre-acceptance checks: see §6.
- On all checks pass: calls `SaveMonitorSettingsAsync()`, returns `200 OK`.
- On any check failure: `400 Bad Request` with `ValidationResult` body.
- The live file is **never overwritten** until all checks pass.

---

### 5.2 Server Configuration — `ServerConfig`

Backed by `appsettings.server.user.json` (name unchanged).

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/setup/config/server` | Returns current `ServerConfig` |
| `PUT` | `/setup/config/server` | Replaces server config |
| `GET` | `/setup/config/server/download` | Downloads `appsettings.server.user.json` as attachment |
| `POST` | `/setup/config/server/upload` | Uploads replacement (validated before acceptance) |

**PUT `/setup/config/server`**
- Validation:
  1. `DataAnnotations` on `ServerConfig`.
  2. `ServerPort` must be in range 1024–65535.
  3. If `ServerPort` differs from the running port, the response body **must**
	 include a `warning` field:
	 `"Server restart required for port change to take effect"`.
- On success: calls `IVersionedSettingsService.SaveServerConfigAsync()`, fires
  `ServerConfigChanged` event, returns `200 OK`.
- **A server restart is never triggered automatically** — the caller is
  responsible for restarting when ready (Q1 decision).

> **Note:** Port changes saved via this API only take effect after a manual server
> restart. This must be clearly documented in the Swagger description for every
> PUT and POST endpoint on this resource.

**POST `/setup/config/server/upload`** — full 7-check pipeline (see §6).

---

### 5.3 Observatory Settings — `ObservatorySettings`

Backed by `observatory.settings.json` (name unchanged).

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/setup/config/observatory` | Returns current `ObservatorySettings` |
| `PUT` | `/setup/config/observatory` | Replaces observatory settings |
| `GET` | `/setup/config/observatory/download` | Downloads `observatory.settings.json` as attachment |
| `POST` | `/setup/config/observatory/upload` | Uploads replacement (validated before acceptance) |

**PUT `/setup/config/observatory`**
- Validation:
  1. `DataAnnotations` (`Latitude` −90 to 90, `Longitude` −180 to 180,
	 `Elevation` −500 to 9000).
- On success: calls `IVersionedSettingsService.SaveObservatorySettingsAsync()`,
  returns `200 OK`.
- Response body **must** include an `info` field:
  `"Changes will apply to newly created devices only. Existing device-nn.settings.json files are not updated."`.

**POST `/setup/config/observatory/upload`** — full 7-check pipeline (see §6).

---

### 5.4 Alpaca Device Discovery — `List<AlpacaDevice>`

Backed by `devices.alpaca.user.json` (renamed from `appsettings.alpaca.user.json`).

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/setup/config/alpaca-devices` | Returns current `List<AlpacaDevice>` |
| `PUT` | `/setup/config/alpaca-devices` | Replaces the full AlpacaDevices list |
| `GET` | `/setup/config/alpaca-devices/download` | Downloads `devices.alpaca.user.json` as attachment |
| `POST` | `/setup/config/alpaca-devices/upload` | Uploads replacement (validated before acceptance) |

**PUT `/setup/config/alpaca-devices`**
- Validation (delegates to `IVersionedSettingsService.ValidateAlpacaDevices()`):
  1. List must not exceed 100 entries.
  2. `DeviceNumber` values must be unique within the list.
  3. `UniqueId` must be a non-empty GUID string.
  4. `DeviceType` must be a known ASCOM device type string.
- On success: calls `SaveAlpacaDevicesAsync()`, returns `200 OK`.

**POST `/setup/config/alpaca-devices/upload`** — full 7-check pipeline; list
validation in step 7 is the same as PUT above.

---

### 5.5 Per-Device Settings — `SkySettings`

Each device is backed by `device-nn.settings.json` (name unchanged).

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/setup/config/devices` | Summary list of all devices (deviceNumber + deviceName + alignmentMode) |
| `GET` | `/setup/config/devices/{deviceNumber}` | Full `SkySettings` for one device |
| `PUT` | `/setup/config/devices/{deviceNumber}` | Replaces settings for one device |
| `DELETE` | `/setup/config/devices/{deviceNumber}` | Deletes the device settings file and its Alpaca discovery entry |
| `GET` | `/setup/config/devices/{deviceNumber}/download` | Downloads `device-nn.settings.json` as attachment |
| `POST` | `/setup/config/devices/{deviceNumber}/upload` | Uploads replacement settings for one device (validated) |

**GET `/setup/config/devices/{deviceNumber}`**
- `deviceNumber` must be 0–99; otherwise `400 Bad Request`.
- If file does not exist: `404 Not Found`.
- Returns: `200 OK` with `SkySettings` body.

**PUT `/setup/config/devices/{deviceNumber}`**
- Validation:
  1. `DataAnnotations` on `SkySettings`.
  2. `DeviceNumber` in the body must match the route `{deviceNumber}`; mismatch
	 returns `400 Bad Request`.
  3. Delegates to `IVersionedSettingsService.ValidateDeviceSettings()`.
- On success: calls `SaveDeviceSettingsAsync()`, fires `DeviceSettingsChanged`
  event, returns `200 OK`.

**DELETE `/setup/config/devices/{deviceNumber}`**

Deletion must maintain consistency across the two files that describe a device
(Q2 decision — consistency is enforced):

1. Check that the device is not currently connected; if connected return
   `409 Conflict`.
2. Call `IVersionedSettingsService.DeleteDeviceSettingsAsync(deviceNumber)` to
   remove `device-nn.settings.json`.
3. Call `IVersionedSettingsService.RemoveAlpacaDeviceAsync(deviceNumber)` to
   remove the corresponding entry from `devices.alpaca.user.json`.
4. Both operations must succeed; if step 3 fails after step 2 succeeds, log the
   inconsistency as an error and return `500 Internal Server Error` with an
   explanatory message so the caller can take corrective action.
5. Returns `204 No Content` on full success.
6. Returns `404 Not Found` if `device-nn.settings.json` does not exist.

**POST `/setup/config/devices/{deviceNumber}/upload`** — full 7-check pipeline
plus the validation in PUT steps 1–3.

---

### 5.6 Templates — Read-Only

Templates are served from the AppData versioned copy written by
`SettingsTemplateService.InitializeTemplates()` (Q5 decision).

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/setup/config/templates` | Lists available template names and their alignment mode associations |
| `GET` | `/setup/config/templates/{name}` | Downloads a named template JSON file |

Valid `{name}` values: `common`, `germanpolar-overrides`, `polar-overrides`,
`altaz-overrides`.

- Returns: `200 OK` with file content, `Content-Type: application/json`.
- No POST / PUT / DELETE — templates are immutable factory defaults.
- If `{name}` is not in the valid set: `404 Not Found`.
- The download path is constructed via `ISettingsTemplateService` (which already
  knows the versioned AppData templates folder).

---

## 6. Upload Validation Pipeline (Common)

All `POST …/upload` endpoints **must** apply the following checks in order
before the live file is touched:

| # | Check | Failure response |
|---|-------|-----------------|
| 1 | File present in the multipart form | `400` — "No file provided" |
| 2 | File size ≤ 1 MB (Q3: confirmed sufficient) | `413 Payload Too Large` |
| 3 | Extension is `.json` OR MIME is `application/json` | `415 Unsupported Media Type` |
| 4 | Content is parseable as UTF-8 JSON (no syntax errors) | `400` — "Invalid JSON: {parse error message}" |
| 5 | Content deserialises to the target model type | `400` — "Schema mismatch: {property} …" |
| 6 | Model-level `DataAnnotations` validation passes | `400` with `ValidationResult` body |
| 7 | Service-level validation passes (where available) | `400` with `ValidationResult` body |

Only if all 7 checks pass is the live file replaced (atomically via the temp-file
rename already used by `VersionedSettingsService`).

The response body for any upload failure must conform to `ValidationResult`:

```json
{
  "isValid": false,
  "errors": [
	{ "propertyName": "Latitude", "errorMessage": "Must be between -90 and 90" }
  ],
  "warnings": []
}
```

---

## 7. Download Behaviour

- All download endpoints stream the file directly from disk — they do **not**
  round-trip through deserialise/serialise (to preserve original formatting and
  avoid precision loss on `double` properties).
- Template downloads are served from the versioned AppData copy (Q5 decision).
- Response headers:
  - `Content-Type: application/json; charset=utf-8`
  - `Content-Disposition: attachment; filename="{actual-filename}"`
  - `Cache-Control: no-store`
- If the file does not yet exist: `404 Not Found` with an `ErrorResponse` body;
  the caller should use the PUT endpoint to create it first.

---

## 8. Access Control

The API is **not** restricted to localhost callers (Q4 decision). Access is
governed by the existing `AuthorizationFilter` service-filter that is applied to
all setup controllers. Remote access is therefore subject to the same
authentication requirements as all other setup endpoints, controlled by
`ServerConfig.AllowRemoteAccess`.

---

## 9. HTTP Response Code Summary

| Code | Meaning in this API |
|------|---------------------|
| `200 OK` | Successful GET / PUT / POST |
| `204 No Content` | Successful DELETE |
| `400 Bad Request` | Validation failure, range error, or body mismatch |
| `404 Not Found` | File / device not found |
| `409 Conflict` | DELETE rejected because device is currently connected |
| `413 Payload Too Large` | Uploaded file exceeds 1 MB |
| `415 Unsupported Media Type` | Upload is not a JSON file |
| `500 Internal Server Error` | Partial DELETE failure (settings deleted, Alpaca entry could not be removed) |

---

## 10. Implementation Guidance

### 10.1 Controller placement

Create a new `ConfigController` in `GreenSwamp.Alpaca.Server\Controllers\` to
keep this API separate from the existing `SetupDevicesController`. Apply:

```csharp
[ServiceFilter(typeof(AuthorizationFilter))]
[ApiExplorerSettings(GroupName = "AlpacaSetup")]
[ApiController]
[Route("setup/config")]
[Produces(MediaTypeNames.Application.Json)]
```

### 10.2 Service dependencies

Inject `IVersionedSettingsService` (already registered in DI). For template
access inject `ISettingsTemplateService` (already registered). No new services
are required for v1.

### 10.3 Upload helper

Extract the 7-check pipeline into a private generic helper:

```csharp
private async Task<(T? model, ValidationResult result)>
	ValidateUploadAsync<T>(IFormFile file, Func<T, ValidationResult>? domainValidate = null)
```

This keeps individual action methods thin and ensures the pipeline is applied
consistently across all resource types.

### 10.4 Swagger / OpenAPI

- Each endpoint must carry XML `<summary>` and `<response>` tags so that the
  existing Swagger UI (already configured via `StartupHelpers`) renders useful
  documentation.
- File-download endpoints should use `[Produces("application/json")]` with
  `[ProducesResponseType(typeof(FileStreamResult), 200)]`.
- File-upload endpoints should declare `[Consumes("multipart/form-data")]`.
- The `ServerConfig` PUT and POST Swagger summaries must explicitly state that
  port changes require a manual server restart.

### 10.5 Atomic writes

Reuse the temp-file-rename pattern already present in `VersionedSettingsService`
— never write directly to the live path.

### 10.6 Concurrency

The per-file `SemaphoreSlim` locks in `VersionedSettingsService` already
serialise writes. Upload endpoints must call the existing `Save*Async` methods
rather than writing files directly.

---

## 11. Test Requirements

A new test class `ConfigControllerTests` (project TBD — see implementation plan)
must cover at minimum:

| Test | Scenario |
|------|----------|
| `GetMonitorSettings_ReturnsOk` | Happy path GET |
| `PutMonitorSettings_ValidBody_ReturnsOk` | Happy path PUT |
| `PutMonitorSettings_DataAnnotationsFailure_ReturnsBadRequest` | Annotations guard |
| `UploadMonitorSettings_ValidFile_ReturnsOk` | Upload happy path |
| `UploadMonitorSettings_OversizedFile_Returns413` | Size guard (check 2) |
| `UploadMonitorSettings_NotJson_Returns415` | MIME guard (check 3) |
| `UploadMonitorSettings_MalformedJson_ReturnsBadRequest` | Parse guard (check 4) |
| `DownloadMonitorSettings_FileExists_ReturnsAttachment` | Download happy path |
| `DownloadMonitorSettings_FileNotFound_Returns404` | Missing file |
| `GetDeviceSettings_UnknownDevice_Returns404` | Missing device file |
| `PutDeviceSettings_DeviceNumberMismatch_Returns400` | Body/route mismatch |
| `DeleteDeviceSettings_ConnectedDevice_Returns409` | Conflict guard |
| `DeleteDeviceSettings_RemovesAlpacaEntryToo` | Consistency enforcement (Q2) |
| `PutServerConfig_PortChange_ReturnsWarning` | Restart warning |
| `GetTemplates_ReturnsAllFourNames` | Template index |
| `GetTemplate_UnknownName_Returns404` | Template not found |
| `GetTemplate_KnownName_ReturnsFileFromAppData` | AppData copy served (Q5) |

---

## 12. Resolved Decisions

| # | Question | Decision |
|---|----------|----------|
| Q1 | Should upload of a new `ServerConfig` automatically trigger a server restart? | **No.** Changes are saved and take effect on the next manual restart. A `warning` field in the response informs the caller. |
| Q2 | Should DELETE of `device-nn.settings.json` also remove the entry from `devices.alpaca.user.json`? | **Yes — always.** Both operations are executed atomically. Partial failure is logged and returns `500`. |
| Q3 | Is the 1 MB file size cap sufficient? | **Yes.** Confirmed acceptable for all current and anticipated file sizes. |
| Q4 | Should the API be restricted to localhost callers only? | **No.** Access is governed by the existing `AuthorizationFilter` and `ServerConfig.AllowRemoteAccess`, consistent with all other setup endpoints. |
| Q5 | Should template downloads serve the AppData copy or the embedded assembly resource? | **AppData copy.** Served from the versioned folder written by `SettingsTemplateService.InitializeTemplates()`. |

---

*End of document — 2026-05-03 10:34*

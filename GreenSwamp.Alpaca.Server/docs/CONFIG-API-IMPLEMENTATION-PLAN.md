# Configuration File Management REST API — Phased Implementation Plan

**Document:** CONFIG-API-IMPLEMENTATION-PLAN.md
**Status:** Ready for review — no code changes made
**Updated:** 2026-05-03 10:34
**Author:** Andy
**Requirements ref:** CONFIG-API-REQUIREMENTS.md

---

## Overview

This plan is split into five phases. Each phase is independently buildable and
deployable; later phases add capability without breaking earlier ones. The file
rename pre-requisite (Phase 0) is intentionally isolated so it can be verified
in isolation before any API code is written.

```
Phase 0 — File rename pre-requisite          (no API code)
Phase 1 — ConfigController skeleton + monitor resource
Phase 2 — Server, observatory and alpaca-devices resources
Phase 3 — Per-device CRUD and upload/download
Phase 4 — Templates resource
Phase 5 — Tests
```

Estimated scope per phase is noted alongside each step to help with scheduling.

---

## Phase 0 — File Rename Pre-Requisite

**Goal:** Rename `appsettings.user.json` → `monitor.settings.user.json` and
`appsettings.alpaca.user.json` → `devices.alpaca.user.json` throughout the
codebase, updating all path constants, schema `$id` references, and any related
template or schema files. No new API surface is introduced.

**Build gate:** Solution must compile with zero new errors after each step.
Existing tests must remain green throughout.

---

### Step P0-1 — Update `IVersionedSettingsService` path properties

**File:** `GreenSwamp.Alpaca.Settings\Services\IVersionedSettingsService.cs`

- Rename the `UserSettingsPath` property to `MonitorSettingsPath` and update its
  XML summary to reference `monitor.settings.user.json`.
- Rename the `AlpacaSettingsPath` property to `AlpacaDevicesSettingsPath` and
  update its XML summary to reference `devices.alpaca.user.json`.
- No implementation changes in this step — interface only.

---

### Step P0-2 — Update `VersionedSettingsService` path expressions

**File:** `GreenSwamp.Alpaca.Settings\Services\VersionedSettingsService.cs`

- Update the `UserSettingsPath` expression-body property to return
  `Path.Combine(_currentVersionPath, "monitor.settings.user.json")`.
- Update the `AlpacaSettingsPath` expression-body property to return
  `Path.Combine(_currentVersionPath, "devices.alpaca.user.json")`.
- Rename both properties to match the interface changes made in P0-1.
- The `_monitorFileLock` and `_alpacaFileLock` fields are named correctly
  already — no rename required there.

---

### Step P0-3 — Update all call sites of the renamed properties

**Files to search:** All `.cs` files in the solution that reference
`UserSettingsPath` or `AlpacaSettingsPath`.

Expected locations (verify with symbol search before editing):
- `GreenSwamp.Alpaca.Server\AlpacaConfiguration.cs`
- `GreenSwamp.Alpaca.Server\Program.cs` (bootstrap path derivation)
- `GreenSwamp.Alpaca.Settings\Extensions\ConfigurationBuilderExtensions.cs`
- Any Blazor pages or components that display the path in the UI

Replace each reference with the renamed property:
- `UserSettingsPath` → `MonitorSettingsPath`
- `AlpacaSettingsPath` → `AlpacaDevicesSettingsPath`

---

### Step P0-4 — Rename schema and template files on disk

**Files:**
- Rename `GreenSwamp.Alpaca.Settings\Templates\appsettings.schema.json` to
  `monitor.settings.schema.json` (if this schema is specifically for
  `MonitorSettings`; confirm by inspecting the `title` field).
- Update the `$id` field inside the renamed schema file to reflect the new name.
- Update any `$schema` references in `monitor.settings.user.json` that point to
  the old schema filename.

> If `appsettings.schema.json` covers the full `appsettings.json` structure
> (not just `MonitorSettings`) it should **not** be renamed — confirm before
> acting.

---

### Step P0-5 — Update `SettingsTemplateService` and `ConfigurationBuilderExtensions`

**Files:**
- `GreenSwamp.Alpaca.Settings\Services\SettingsTemplateService.cs` — update any
  hardcoded filename strings.
- `GreenSwamp.Alpaca.Settings\Extensions\ConfigurationBuilderExtensions.cs` —
  update the optional-JSON source that loads the user settings file by name.

---

### Step P0-6 — Migrate existing user files on startup

**File:** `GreenSwamp.Alpaca.Settings\Services\VersionedSettingsService.cs`

Add a one-time migration guard in the constructor (or in a dedicated
`MigrateFileNamesIfRequired()` method called from the constructor):

```
if old name exists AND new name does not exist → rename (File.Move)
if both exist → log a warning, prefer the new name, leave the old file in place
if only new name exists → no action
```

This ensures existing installations are not broken on first run after the update.
The migration must run before any file read operations in the constructor body.

---

### Step P0-7 — Build and verify

- Run `dotnet build` — zero errors required.
- Run all existing tests — all must pass.
- Manually confirm the renamed properties appear correctly in Swagger UI.

---

## Phase 1 — `ConfigController` Skeleton + Monitor Resource

**Goal:** Stand up the new controller with the `/setup/config/monitor` resource
fully implemented (GET, PUT, download, upload). This gives an early end-to-end
smoke test of the upload pipeline before the other resources are added.

---

### Step 1-1 — Create `ConfigController` skeleton

**File (new):** `GreenSwamp.Alpaca.Server\Controllers\ConfigController.cs`

- Class decorated with `[ServiceFilter(typeof(AuthorizationFilter))]`,
  `[ApiExplorerSettings(GroupName = "AlpacaSetup")]`, `[ApiController]`,
  `[Route("setup/config")]`, `[Produces(MediaTypeNames.Application.Json)]`.
- Constructor injecting `IVersionedSettingsService`,
  `ISettingsTemplateService`, and `ILogger<ConfigController>`.
- Stub `ValidateUploadAsync<T>` private helper (signature only, throws
  `NotImplementedException`).
- No action methods yet.

---

### Step 1-2 — Implement `ValidateUploadAsync<T>` helper

**File:** `ConfigController.cs`

Implement the 7-check pipeline described in §6 of the requirements:

1. Null / empty file check → `ValidationResult.Failure("No file provided")`.
2. Size ≤ 1 MB check → return a special `413`-flagged result (or throw a custom
   `FileTooLargeException` caught in the action method).
3. Extension / MIME check.
4. UTF-8 JSON parse using `JsonDocument.ParseAsync`.
5. Deserialise to `T` using `JsonSerializer.DeserializeAsync<T>`.
6. `DataAnnotations` validation via `Validator.TryValidateObject`.
7. Invoke the optional `domainValidate` delegate if provided.

Return `(T? model, ValidationResult result)` — caller checks `result.IsValid`
before proceeding.

---

### Step 1-3 — Add monitor GET and PUT action methods

**File:** `ConfigController.cs`

Implement:
- `GET /setup/config/monitor` → `GetMonitorSettings()`
- `PUT /setup/config/monitor` → `PutMonitorSettings([FromBody] MonitorSettings settings)`

Full XML doc, `[ProducesResponseType]` attributes, and DataAnnotations model
state check on PUT.

---

### Step 1-4 — Add monitor download action method

**File:** `ConfigController.cs`

Implement:
- `GET /setup/config/monitor/download` → `DownloadMonitorSettings()`

Stream `IVersionedSettingsService.MonitorSettingsPath` using
`PhysicalFileResult` or `FileStreamResult`. Return `404` if file absent.
Set `Content-Disposition: attachment; filename="monitor.settings.user.json"` and
`Cache-Control: no-store`.

---

### Step 1-5 — Add monitor upload action method

**File:** `ConfigController.cs`

Implement:
- `POST /setup/config/monitor/upload` → `UploadMonitorSettings(IFormFile file)`

Call `ValidateUploadAsync<MonitorSettings>(file)`. On success call
`SaveMonitorSettingsAsync`. On failure map the result to the correct HTTP status
code (400 / 413 / 415).

---

### Step 1-6 — Build and smoke test

- `dotnet build` — zero errors.
- Swagger UI shows the four monitor endpoints.
- Manual GET round-trip confirms the renamed file path is used.

---

## Phase 2 — Server, Observatory, and Alpaca-Devices Resources

**Goal:** Add the remaining three single-resource endpoints using the same
patterns established in Phase 1.

---

### Step 2-1 — Add server config GET, PUT, download, upload

**File:** `ConfigController.cs`

- `GET /setup/config/server` → `GetServerConfig()`
- `PUT /setup/config/server` → `PutServerConfig([FromBody] ServerConfig config)`
  - After DataAnnotations, validate `ServerPort` range (1024–65535).
  - If port differs from `BootstrapConfig.ServerPort`, add a `warning` key to a
	wrapper response object before returning `200 OK`.
- `GET /setup/config/server/download` → `DownloadServerConfig()`
- `POST /setup/config/server/upload` → `UploadServerConfig(IFormFile file)`

Swagger summaries for PUT and POST must state that port changes require a manual
restart.

---

### Step 2-2 — Add observatory settings GET, PUT, download, upload

**File:** `ConfigController.cs`

- `GET /setup/config/observatory` → `GetObservatorySettings()`
- `PUT /setup/config/observatory` → `PutObservatorySettings([FromBody] ObservatorySettings settings)`
  - On success include `info` message in response body.
- `GET /setup/config/observatory/download` → `DownloadObservatorySettings()`
- `POST /setup/config/observatory/upload` → `UploadObservatorySettings(IFormFile file)`

---

### Step 2-3 — Add alpaca-devices GET, PUT, download, upload

**File:** `ConfigController.cs`

- `GET /setup/config/alpaca-devices` → `GetAlpacaDevices()`
- `PUT /setup/config/alpaca-devices` → `PutAlpacaDevices([FromBody] List<AlpacaDevice> devices)`
  - Delegate to `IVersionedSettingsService.ValidateAlpacaDevices()` after a
	transient in-memory dry-run (do not write until valid).
- `GET /setup/config/alpaca-devices/download` → `DownloadAlpacaDevices()`
  - `Content-Disposition: attachment; filename="devices.alpaca.user.json"`.
- `POST /setup/config/alpaca-devices/upload` → `UploadAlpacaDevices(IFormFile file)`

---

### Step 2-4 — Build and smoke test

- `dotnet build` — zero errors.
- Swagger UI shows all resources from Phase 1 and Phase 2.

---

## Phase 3 — Per-Device CRUD, Upload, and Download

**Goal:** Implement all six endpoints on the `/setup/config/devices` resource,
including the consistency-enforcing DELETE.

---

### Step 3-1 — Add device summary list endpoint

**File:** `ConfigController.cs`

- `GET /setup/config/devices` → `GetAllDevicesSummary()`
- Returns a lightweight DTO list (`deviceNumber`, `deviceName`,
  `alignmentMode`) built from `IVersionedSettingsService.GetAllDeviceSettings()`.
- Define an internal `DeviceSettingsSummary` record in the same file (or in
  `GreenSwamp.Alpaca.Server\Models\`) to represent the response shape.

---

### Step 3-2 — Add device GET by number

**File:** `ConfigController.cs`

- `GET /setup/config/devices/{deviceNumber}` → `GetDeviceSettings(int deviceNumber)`
- Range guard: 0–99, else `400`.
- `404` if file absent.

---

### Step 3-3 — Add device PUT

**File:** `ConfigController.cs`

- `PUT /setup/config/devices/{deviceNumber}` → `PutDeviceSettings(int deviceNumber, [FromBody] SkySettings settings)`
- Validate: body `DeviceNumber` must equal route `{deviceNumber}`.
- DataAnnotations, then `IVersionedSettingsService.ValidateDeviceSettings()`.
- On success: `SaveDeviceSettingsAsync()` (fires `DeviceSettingsChanged` event
  automatically).

---

### Step 3-4 — Add device DELETE (with consistency enforcement)

**File:** `ConfigController.cs`

- `DELETE /setup/config/devices/{deviceNumber}` → `DeleteDeviceSettings(int deviceNumber)`
- Check connected state via `MountRegistry.GetInstance(deviceNumber)?.IsConnected`
  — return `409` if true.
- Call `DeleteDeviceSettingsAsync(deviceNumber)`.
- Call `RemoveAlpacaDeviceAsync(deviceNumber)`.
- If `RemoveAlpacaDeviceAsync` throws after `DeleteDeviceSettingsAsync` succeeds,
  log error and return `500` with descriptive message.
- On full success: `204 No Content`.

---

### Step 3-5 — Add device download

**File:** `ConfigController.cs`

- `GET /setup/config/devices/{deviceNumber}/download` → `DownloadDeviceSettings(int deviceNumber)`
- Stream `IVersionedSettingsService.GetDeviceSettingsPath(deviceNumber)`.
- `Content-Disposition: attachment; filename="device-{nn:D2}.settings.json"`.

---

### Step 3-6 — Add device upload

**File:** `ConfigController.cs`

- `POST /setup/config/devices/{deviceNumber}/upload` → `UploadDeviceSettings(int deviceNumber, IFormFile file)`
- Full 7-check pipeline; domain validation step passes
  `settings => _settingsService.ValidateDeviceSettings(deviceNumber)` (write to
  temp path, validate, then commit).

---

### Step 3-7 — Build and smoke test

- `dotnet build` — zero errors.
- Manually test DELETE to confirm both `device-nn.settings.json` and the
  corresponding `devices.alpaca.user.json` entry are removed.

---

## Phase 4 — Templates Resource

**Goal:** Add the read-only template index and download endpoints, serving files
from the versioned AppData copy.

---

### Step 4-1 — Add template list endpoint

**File:** `ConfigController.cs`

- `GET /setup/config/templates` → `GetTemplates()`
- Return a hardcoded list of the four known template descriptors:

```json
[
  { "name": "common",                 "alignmentMode": null,          "description": "Common defaults for all alignment modes" },
  { "name": "germanpolar-overrides",  "alignmentMode": "GermanPolar", "description": "GermanPolar-specific overrides" },
  { "name": "polar-overrides",        "alignmentMode": "Polar",       "description": "Polar-specific overrides" },
  { "name": "altaz-overrides",        "alignmentMode": "AltAz",       "description": "AltAz-specific overrides" }
]
```

Define an internal `TemplateDescriptor` record for the response shape.

---

### Step 4-2 — Add template download endpoint

**File:** `ConfigController.cs`

- `GET /setup/config/templates/{name}` → `DownloadTemplate(string name)`
- Validate `name` against the four allowed values; return `404` otherwise
  (do not expose directory traversal via the `name` parameter).
- Resolve the file path by calling `ISettingsTemplateService` or by constructing
  the path directly from the known AppData templates folder.
- Stream the file with `Content-Type: application/json` and
  `Content-Disposition: attachment; filename="{name}.json"`.

---

### Step 4-3 — Build and smoke test

- `dotnet build` — zero errors.
- Swagger UI shows complete API surface.

---

## Phase 5 — Tests

**Goal:** Add automated test coverage for all endpoints and all pipeline failure
modes. Tests should use `WebApplicationFactory<Program>` or a mock-based
approach consistent with the existing test project style.

**Project:** Use or extend `GreenSwamp.Alpaca.MountControl.Tests`, or create a
new `GreenSwamp.Alpaca.Server.Tests` project if integration-style tests are
preferred. Confirm with the existing test setup before creating a new project.

---

### Step 5-1 — Determine test project placement and setup

- Inspect `GreenSwamp.Alpaca.MountControl.Tests` to identify the testing
  framework in use (xUnit / NUnit / MSTest) and the mocking library.
- Decide whether to add to the existing project or create
  `GreenSwamp.Alpaca.Server.Tests`.
- Set up `WebApplicationFactory<Program>` or controller-unit-test harness
  with mocked `IVersionedSettingsService` and `ISettingsTemplateService`.

---

### Step 5-2 — Monitor resource tests

Implement all monitor-related cases from §11 of the requirements:
- `GetMonitorSettings_ReturnsOk`
- `PutMonitorSettings_ValidBody_ReturnsOk`
- `PutMonitorSettings_DataAnnotationsFailure_ReturnsBadRequest`
- `UploadMonitorSettings_ValidFile_ReturnsOk`
- `UploadMonitorSettings_OversizedFile_Returns413`
- `UploadMonitorSettings_NotJson_Returns415`
- `UploadMonitorSettings_MalformedJson_ReturnsBadRequest`
- `DownloadMonitorSettings_FileExists_ReturnsAttachment`
- `DownloadMonitorSettings_FileNotFound_Returns404`

---

### Step 5-3 — Server and observatory resource tests

- `GetServerConfig_ReturnsOk`
- `PutServerConfig_PortChange_ReturnsWarningInBody`
- `PutServerConfig_InvalidPort_ReturnsBadRequest`
- `GetObservatorySettings_ReturnsOk`
- `PutObservatorySettings_InvalidLatitude_ReturnsBadRequest`
- `PutObservatorySettings_ValidBody_ReturnsInfoMessage`

---

### Step 5-4 — Alpaca-devices resource tests

- `GetAlpacaDevices_ReturnsOk`
- `PutAlpacaDevices_DuplicateDeviceNumbers_ReturnsBadRequest`
- `PutAlpacaDevices_ExceedsLimit_ReturnsBadRequest`
- `UploadAlpacaDevices_ValidFile_ReturnsOk`

---

### Step 5-5 — Per-device resource tests

- `GetDeviceSettings_UnknownDevice_Returns404`
- `PutDeviceSettings_DeviceNumberMismatch_Returns400`
- `DeleteDeviceSettings_ConnectedDevice_Returns409`
- `DeleteDeviceSettings_RemovesAlpacaEntryToo` *(verifies Q2 consistency)*
- `DeleteDeviceSettings_AlpacaRemovalFails_Returns500`
- `UploadDeviceSettings_ValidFile_ReturnsOk`
- `DownloadDeviceSettings_FileExists_ReturnsCorrectFilename`

---

### Step 5-6 — Template resource tests

- `GetTemplates_ReturnsAllFourNames`
- `GetTemplate_UnknownName_Returns404`
- `GetTemplate_KnownName_ReturnsFileFromAppData` *(verifies Q5 decision)*

---

### Step 5-7 — Upload pipeline edge-case tests (shared across resources)

These tests verify the `ValidateUploadAsync<T>` helper in isolation or via one
representative endpoint:
- `Upload_NoFileProvided_Returns400`
- `Upload_FileSizeExactly1MB_Accepted`
- `Upload_FileSizeOver1MB_Returns413`
- `Upload_WrongExtensionWrongMime_Returns415`
- `Upload_WrongExtensionCorrectMime_Accepted`
- `Upload_CorrectExtensionWrongMime_Accepted`
- `Upload_JsonSyntaxError_Returns400WithParseMessage`
- `Upload_UnknownJsonProperties_SchemaDetails_Returns400`

---

### Step 5-8 — Run full test suite and confirm green

- All new and pre-existing tests must pass.
- Code coverage report to be generated with `dotnet-coverage` as per workspace
  instructions.

---

## Dependency Summary

```
Phase 0  ──┐
		   ├──► Phase 1  ──┐
						   ├──► Phase 2  ──┐
										   ├──► Phase 3  ──┐
														   ├──► Phase 4  ──┐
																		   └──► Phase 5
```

Each phase depends on the previous. Phases 1–4 each produce a working, buildable
state. Phase 5 tests the entire surface end-to-end and should be run in full
before marking the implementation complete.

---

## Files to be Created

| File | Phase |
|------|-------|
| `GreenSwamp.Alpaca.Server\Controllers\ConfigController.cs` | Phase 1 |
| `GreenSwamp.Alpaca.Server\Models\DeviceSettingsSummary.cs` | Phase 3 |
| `GreenSwamp.Alpaca.Server\Models\TemplateDescriptor.cs` | Phase 4 |
| Test project file (TBD in Step 5-1) | Phase 5 |

---

## Files to be Modified

| File | Phase | Nature of change |
|------|-------|-----------------|
| `GreenSwamp.Alpaca.Settings\Services\IVersionedSettingsService.cs` | P0-1 | Rename two path properties |
| `GreenSwamp.Alpaca.Settings\Services\VersionedSettingsService.cs` | P0-2, P0-6 | Update path strings; add migration guard |
| `GreenSwamp.Alpaca.Settings\Extensions\ConfigurationBuilderExtensions.cs` | P0-3, P0-5 | Update filename references |
| `GreenSwamp.Alpaca.Server\AlpacaConfiguration.cs` | P0-3 | Update property references |
| `GreenSwamp.Alpaca.Server\Program.cs` | P0-3 | Update path property references |
| `GreenSwamp.Alpaca.Settings\Services\SettingsTemplateService.cs` | P0-5 | Update filename strings |
| Any Blazor pages displaying settings paths | P0-3 | Update property references |
| Schema files under `Templates\` | P0-4 | Rename + update `$id` fields |

---

*End of document — 2026-05-03 10:34*

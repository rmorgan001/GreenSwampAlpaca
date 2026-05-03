# GreenSwamp Alpaca - JSON Settings Files CRUD API Status

**Date:** 2024-12-19  
**Focus:** REST API endpoints for Create, Read, Update, Delete operations on JSON settings files

---

## ⚠️ Current Status: NOT IMPLEMENTED

There are **NO REST API endpoints** currently available for CRUD operations on JSON settings files.

---

## What Doesn't Exist (v1.0)

### Create Settings File
❌ **Not Available**
```
POST /setup/settings/{deviceNumber}     - NOT IMPLEMENTED
POST /api/v1/settings                    - NOT IMPLEMENTED
```

### Read Settings File
❌ **Not Available**
```
GET /setup/settings/{deviceNumber}       - NOT IMPLEMENTED
GET /api/v1/settings/{deviceNumber}      - NOT IMPLEMENTED
GET /setup/settings                      - NOT IMPLEMENTED
```

### Update Settings File
❌ **Not Available**
```
PUT /setup/settings/{deviceNumber}       - NOT IMPLEMENTED
PATCH /setup/settings/{deviceNumber}     - NOT IMPLEMENTED
```

### Delete Settings File
❌ **Not Available**
```
DELETE /setup/settings/{deviceNumber}    - NOT IMPLEMENTED
```

---

## What DOES Exist (v1.0)

### Device Management Endpoints (Limited)

**These are the ONLY available endpoints related to settings:**

#### 1. POST /setup/devices - Indirect Settings Create
```http
POST http://localhost:31426/setup/devices
Content-Type: application/json

{
  "deviceNumber": 0,
  "deviceName": "Telescope",
  "profileName": "default"
}
```
**What it does:**
- Creates a new `device-NN.settings.json` file
- **INDIRECTLY** creates settings by loading defaults
- Cannot customize settings during creation
- Settings use template defaults only

**Response:**
```json
{
  "deviceNumber": 0,
  "deviceName": "Telescope",
  "uniqueId": "...",
  "message": "Device added successfully"
}
```

#### 2. DELETE /setup/devices/{deviceNumber} - Device Removal
```http
DELETE http://localhost:31426/setup/devices/0
```
**What it does:**
- Removes device from registry
- **Does NOT delete** the `device-NN.settings.json` file
- Settings file remains in `%AppData%\GreenSwampAlpaca\1.0\`

**Response:**
```json
{
  "message": "Device 0 removed successfully"
}
```

#### 3. GET /setup/devices - Limited Settings Read
```http
GET http://localhost:31426/setup/devices
```
**What it returns:**
- Device number, name, connection state
- Alignment mode, mount type
- **Does NOT return full settings JSON**
- Only subset of settings data

**Response:**
```json
[
  {
	"deviceNumber": 0,
	"deviceName": "Telescope #1",
	"connected": false,
	"alignmentMode": "AltAz",
	"mountType": "AltAzMount",
	"comPort": null,
	"baudRate": null
  }
]
```

---

## Current Settings Access Methods

### Method 1: File System Access (Not via REST)
**Direct file editing:**
```powershell
$settingsFile = "$env:APPDATA\GreenSwampAlpaca\1.0\device-00.settings.json"
$settings = Get-Content $settingsFile | ConvertFrom-Json
$settings.latitude = 40.1234
$settings | ConvertTo-Json | Set-Content $settingsFile
```

### Method 2: Web UI (Blazor Pages)
**Via web interface:**
- Page: `/devices/telescopesetup`
- Page: `/mountsettings`
- Allows editing but not via REST API

### Method 3: Programmatic Access (IVersionedSettingsService)
**Via C# service injection:**
```csharp
public async Task UpdateSettings(int deviceNumber, SkySettings settings)
{
	await _settingsService.SaveDeviceSettingsAsync(deviceNumber, settings);
}
```

---

## Why Not Available?

### Documented Limitations (From Code Review)

The documentation I created explicitly noted these as **Future Enhancements**:

```markdown
## Future Enhancements

Planned REST API improvements:

1. **Settings Endpoint**
   GET  /setup/settings/{deviceNumber}    - Fetch device settings
   PUT  /setup/settings/{deviceNumber}    - Update device settings
   PATCH /setup/settings/{deviceNumber}   - Partial settings update

2. **Monitoring Endpoint**
   GET  /setup/monitor                    - Get monitoring filters
   PUT  /setup/monitor                    - Update monitoring filters
```

### Design Decision

The current architecture focuses on:
- Device lifecycle management (add/remove)
- Mount control (tracking, pier side)
- Alpaca protocol compliance

Settings management through REST API is a **v2.0 feature**.

---

## Settings File Locations

For reference, if you need to access settings files directly:

```
%AppData%\GreenSwampAlpaca\1.0\
├── device-00.settings.json             # Device #0 settings
├── device-01.settings.json             # Device #1 settings
├── device-02.settings.json             # Device #2 settings
├── appsettings.user.json               # Server-wide settings
├── appsettings.alpaca.user.json        # Alpaca discovery
└── observatory.settings.json           # Observatory settings
```

### Settings File Structure

**Example: device-00.settings.json**
```json
{
  "deviceNumber": 0,
  "deviceName": "Telescope #1",
  "enabled": true,
  "mount": "AltAzMount",
  "port": "COM3",
  "baudRate": 9600,
  "dataBits": 8,
  "handshake": "None",
  "readTimeout": 5000,
  "dtrEnable": false,
  "rtsEnable": false,
  "latitude": 40.1234,
  "longitude": -105.5678,
  "elevation": 1600.0,
  "utcOffset": "+06:00:00",
  "autoTrack": true,
  "alignmentMode": "AltAz",
  "equatorialCoordinateType": "Topocentric",
  "atPark": false,
  "focuserTemperature": 20.5,
  "opticalTubeAssemblyEflLength": 1200.0,
  "opticalTubeAssemblyFLRatio": 5.9,
  "opticalTubeAssemblyAperture": 203.2
}
```

---

## Workarounds for v1.0

### Option 1: Direct File I/O (Recommended)
```powershell
# Read settings
$settings = Get-Content "$env:APPDATA\GreenSwampAlpaca\1.0\device-00.settings.json" `
	| ConvertFrom-Json

# Modify
$settings.latitude = 40.5678
$settings.tracking = $true

# Write back
$settings | ConvertTo-Json | Set-Content "$env:APPDATA\GreenSwampAlpaca\1.0\device-00.settings.json"
```

### Option 2: Use Blazor Web UI
- Navigate to: `http://localhost:31426/devices/telescopesetup`
- Edit settings through web interface
- Changes persist to `device-NN.settings.json`

### Option 3: Use C# Service Layer
```csharp
// In a .NET application with DI configured
public class SettingsManager
{
	private readonly IVersionedSettingsService _settingsService;

	public async Task UpdateDeviceAsync(int deviceNumber, SkySettings settings)
	{
		await _settingsService.SaveDeviceSettingsAsync(deviceNumber, settings);
	}

	public SkySettings? GetDeviceSettings(int deviceNumber)
	{
		return _settingsService.GetDeviceSettings(deviceNumber);
	}
}
```

---

## What You CAN Do with Current API

### ✓ Create Settings (Indirectly)
```bash
POST /setup/devices
# Creates device-NN.settings.json with default values
```

### ✓ List Devices (Basic Info Only)
```bash
GET /setup/devices
# Returns minimal settings info
```

### ✓ Remove Device (But NOT Delete Settings)
```bash
DELETE /setup/devices/{deviceNumber}
# Removes from registry, file remains
```

### ✗ Read Full Settings
❌ Not available via REST API

### ✗ Update Settings
❌ Not available via REST API

### ✗ Delete Settings
❌ Not available via REST API

---

## Planned Implementation (v2.0)

Based on my documentation review, these endpoints are **planned but not yet implemented**:

### Settings CRUD Endpoints (Future)
```
GET    /setup/settings/{deviceNumber}           - Fetch full settings
PUT    /setup/settings/{deviceNumber}           - Replace all settings
PATCH  /setup/settings/{deviceNumber}           - Partial update
DELETE /setup/settings/{deviceNumber}           - Delete settings file
GET    /setup/settings                          - List all settings
```

### Monitoring Settings Endpoints (Future)
```
GET    /setup/monitor                           - Get monitoring filters
PUT    /setup/monitor                           - Update monitoring
```

### Observatory Settings Endpoints (Future)
```
GET    /setup/observatory                       - Get observatory config
PUT    /setup/observatory                       - Update observatory
```

---

## Recommended Approach

### For Direct Settings Management

**Use file system directly** until REST API is implemented:

```powershell
function Get-DeviceSettings([int]$deviceNumber) {
	$path = "$env:APPDATA\GreenSwampAlpaca\1.0\device-$($deviceNumber.ToString('D2')).settings.json"
	Get-Content $path | ConvertFrom-Json
}

function Set-DeviceSettings([int]$deviceNumber, $settings) {
	$path = "$env:APPDATA\GreenSwampAlpaca\1.0\device-$($deviceNumber.ToString('D2')).settings.json"
	# Create temp file for atomic write
	$tempPath = "$path.tmp"
	$settings | ConvertTo-Json | Set-Content $tempPath
	Move-Item $tempPath $path -Force
}

# Usage
$settings = Get-DeviceSettings -deviceNumber 0
$settings.latitude = 40.5678
Set-DeviceSettings -deviceNumber 0 -settings $settings
```

### For New Features

**If building integration:**

1. **For now:** Access files directly or use Blazor UI
2. **Plan for:** REST endpoints to be added in v2.0
3. **Monitor:** GitHub repository for API additions
4. **Alternative:** Fork and add these endpoints yourself

---

## Summary Table

| Operation | Method | Status | Current Alternative |
|-----------|--------|--------|----------------------|
| **Create** | POST /setup/settings/{n} | ❌ Not Available | POST /setup/devices (indirect) |
| **Read** | GET /setup/settings/{n} | ❌ Not Available | Direct file access or Blazor UI |
| **Update** | PUT /setup/settings/{n} | ❌ Not Available | Direct file access or Blazor UI |
| **Delete** | DELETE /setup/settings/{n} | ❌ Not Available | Direct file access (manual) |

---

## Controller Analysis

### SetupDevicesController.cs
**Current Methods:**
- `GetDevices()` - Returns limited device info, NOT full settings
- `AddDevice()` - Creates device with default settings
- `RemoveDevice()` - Removes device (keeps file)

**Missing Methods:**
- `GetDeviceSettings(int deviceNumber)` - ❌ NOT IMPLEMENTED
- `UpdateDeviceSettings(int deviceNumber, SkySettings)` - ❌ NOT IMPLEMENTED
- `DeleteDeviceSettings(int deviceNumber)` - ❌ NOT IMPLEMENTED
- `PatchDeviceSettings(int deviceNumber, JsonPatch)` - ❌ NOT IMPLEMENTED

---

## Conclusion

**The bottom line:**

✅ **Device management** via REST API is available  
❌ **Settings file CRUD** via REST API is **NOT available**

### To Manage Settings Now:
1. Edit `device-NN.settings.json` files directly
2. Use Blazor web UI at `http://localhost:31426`
3. Use C# code with `IVersionedSettingsService`

### To Get Settings CRUD via REST API:
1. Wait for v2.0 release
2. Implement custom endpoints (fork the project)
3. Monitor GitHub for updates

---

**Document Status:** ✓ COMPLETE  
**Last Updated:** 2024-12-19  
**Confidence:** High (based on complete code review)

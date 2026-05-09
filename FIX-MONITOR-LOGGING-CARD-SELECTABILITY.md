# Fix: Monitor / Logging Section Card Now Selectable

**Timestamp:** 2026-05-09 17:34  
**Commit:** `22d36b7`  
**Branch:** master

---

## Problem

The Monitor / Logging settings card was not appearing when clicking on the "Monitor / Logging" tree node in Settings Explorer.

## Root Cause

In `SettingsExplorer.razor.cs`, the `OnTreeNodeSelected` method had a selectability check that only allowed:
1. `Group` level nodes
2. `DeviceManagerNode` (Telescope Devices section)

The Monitor / Logging section is a `Section` level node, so it was being rejected as non-selectable.

## Solution

Updated the selectability condition in `SettingsExplorer.razor.cs` (line 318):

**Before:**
```csharp
var selectable = node.Level == SettingsNodeLevel.Group || IsDeviceManagerNode(node);
```

**After:**
```csharp
var selectable = node.Level == SettingsNodeLevel.Group || IsDeviceManagerNode(node) || IsMonitorLoggingSectionNode(node);
```

This adds the Monitor / Logging section to the list of selectable section-level nodes.

## Result

✅ Now when you click "Monitor / Logging" in the tree, the unified card appears with:
- Configuration Presets (Development, Production, Troubleshooting, Profile Debug)
- Quick Actions (All Devices, All Categories, All Types, Clear All Filters)

## Files Modified

- `GreenSwamp.Alpaca.Server/Pages/SettingsExplorer.razor.cs` (1 line changed)

## Build Status

✅ **Build:** Success (0 errors)

---

## Testing

1. Open Settings Explorer (`/settings-explorer`)
2. Click on "Monitor / Logging" in the left tree
3. Confirm the unified card appears on the right with both presets and quick actions
4. Click each preset button → card should show in both pages
5. Click each quick action button → filters should update in the working copy

/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using ASCOM.Alpaca;
using GreenSwamp.Alpaca.MountControl;
using GreenSwamp.Alpaca.Server.TelescopeDriver;
using GreenSwamp.Alpaca.Settings.Services;
using Microsoft.Extensions.Logging;
using MountSkySettings = GreenSwamp.Alpaca.MountControl.SkySettings;
using ModelSkySettings = GreenSwamp.Alpaca.Settings.Models.SkySettings;

namespace GreenSwamp.Alpaca.Server.Services
{
    /// <summary>Encapsulates the outcome of a hot-reload operation.</summary>
    public record DeviceReloadResult(bool Success, int ReloadedCount, string? ErrorMessage = null);

    /// <summary>
    /// Unified device registry that manages both ASCOM DeviceManager
    /// and MountRegistry with synchronized operations.
    /// Register as a singleton in DI.
    /// </summary>
    public class UnifiedDeviceRegistry
    {
        private readonly IVersionedSettingsService _settingsService;
        private readonly ILogger<UnifiedDeviceRegistry> _logger;

        public UnifiedDeviceRegistry(
            IVersionedSettingsService settingsService,
            ILogger<UnifiedDeviceRegistry> logger)
        {
            ArgumentNullException.ThrowIfNull(settingsService);
            ArgumentNullException.ThrowIfNull(logger);
            _settingsService = settingsService;
            _logger = logger;
            _settingsService.DeviceSettingsChanged += OnDeviceSettingsChanged;
        }


        /// <summary>
        /// Registers a device with both ASCOM DeviceManager and MountRegistry atomically.
        /// This ensures synchronized state across both registries.
        /// </summary>
        /// <param name="deviceNumber">Device number (0-based)</param>
        /// <param name="deviceName">Display name for the device</param>
        /// <param name="uniqueId">ASCOM unique identifier (GUID)</param>
        /// <param name="settings">Settings instance for this device</param>
        public void RegisterDevice(
            int deviceNumber,
            string deviceName,
            string uniqueId,
            MountSkySettings settings)
        {
            // 1. Register with MountRegistry (internal control).
            MountRegistry.CreateInstance(deviceNumber, settings, deviceName);

            // 2. Create Telescope driver now that Mount is registered.
            var mount = MountRegistry.GetInstance(deviceNumber);
            var telescope = new Telescope(deviceNumber, mount);

            // 3. Register with DeviceManager (ASCOM routing)
            DeviceManager.LoadTelescope(
                deviceNumber,
                telescope,
                deviceName,
                uniqueId
            );
        }

        /// <summary>
        /// Validates that a device number is available across BOTH registries.
        /// Checks MountRegistry and DeviceManager for conflicts.
        /// </summary>
        public bool IsDeviceNumberAvailable(int deviceNumber)
        {
            // Check MountRegistry
            if (MountRegistry.GetInstance(deviceNumber) != null)
                return false;

            // Check DeviceManager
            if (DeviceManager.Telescopes.ContainsKey(deviceNumber))
                return false;

            return true;
        }

        /// <summary>
        /// Gets the next available device number starting from slot 0.
        /// Returns the lowest available slot number.
        /// </summary>
        public int GetNextAvailableDeviceNumber()
        {
            for (int i = 0; i < int.MaxValue; i++)
            {
                if (IsDeviceNumberAvailable(i))
                    return i;
            }

            throw new InvalidOperationException("No available device numbers");
        }

        /// <summary>
        /// Removes a device from the registry. Any device number may be removed.
        /// </summary>
        /// <param name="deviceNumber">Device number to remove</param>
        /// <returns>True if removed, false if not found</returns>
        public bool RemoveDevice(int deviceNumber)
        {
            // Disconnect and remove from MountRegistry (graceful disconnect if connected)
            bool removed = MountRegistry.RemoveInstance(deviceNumber);

            // Remove from DeviceManager so it is no longer advertised via Alpaca discovery
            DeviceManager.UnloadTelescope(deviceNumber);

            return removed;
        }

        /// <summary>
        /// Gets all devices from the registry.
        /// </summary>
        public IReadOnlyDictionary<int, Alpaca.MountControl.Mount> GetAllDevices()
        {
            return MountRegistry.GetAllInstances();
        }

        /// <summary>
        /// Tears down all currently registered devices and rebuilds the runtime registries
        /// from the current settings files — equivalent to a device-scoped partial restart.
        /// </summary>
        public async Task<DeviceReloadResult> ReloadAllDevicesAsync()
        {
            try
            {
                // 1. Snapshot current device numbers before clearing
                var currentNumbers = MountRegistry.GetAllInstances().Keys.ToList();
                _logger.LogInformation("Hot reload: tearing down {Count} device(s)", currentNumbers.Count);

                // 2. Disconnect and remove every live device from both registries
                foreach (var num in currentNumbers)
                {
                    MountRegistry.RemoveInstance(num); // disconnects if connected
                    DeviceManager.UnloadTelescope(num);
                }

                // 3. Reload enabled devices from current settings files
                var allDevices = _settingsService.GetAllDeviceSettings();
                var enabledDevices = allDevices.Where(d => d.Enabled).ToList();
                var alpacaDevices = _settingsService.GetAlpacaDevices();
                var alpacaMap = alpacaDevices.ToDictionary(d => d.DeviceNumber);

                _logger.LogInformation("Hot reload: registering {Count} enabled device(s)", enabledDevices.Count);

                int registered = 0;
                foreach (var device in enabledDevices)
                {
                    try
                    {
                        var deviceSettings = new GreenSwamp.Alpaca.MountControl.SkySettings(
                            device,
                            _settingsService);

                        string uniqueId = alpacaMap.TryGetValue(device.DeviceNumber, out var alpacaDevice)
                            ? alpacaDevice.UniqueId
                            : Guid.NewGuid().ToString();

                        RegisterDevice(device.DeviceNumber, device.DeviceName, uniqueId, deviceSettings);
                        registered++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Hot reload: failed to register device {Number}: {Name}",
                            device.DeviceNumber, device.DeviceName);
                    }
                }

                // 4. Wire settings event listeners for each newly registered device
                foreach (var kvp in MountRegistry.GetAllInstances())
                    kvp.Value.InitializeSettings();

                _logger.LogInformation("Hot reload complete — {Registered} device(s) active", registered);
                return new DeviceReloadResult(true, registered);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hot reload failed");
                return new DeviceReloadResult(false, 0, ex.Message);
            }
        }

        /// <summary>
        /// Returns the number of ASCOM/Alpaca clients currently connected to the given device,
        /// or 0 if the device is not registered.
        /// </summary>
        public int GetConnectedClientCount(int deviceNumber)
        {
            var mount = MountRegistry.GetInstance(deviceNumber);
            return mount?.ConnectedClientCount ?? 0;
        }

        /// <summary>
        /// Returns true if the mount hardware for the given device is currently running
        /// (queues started, serial port open). False if never started or stopped.
        /// </summary>
        public bool IsMountRunning(int deviceNumber)
        {
            var mount = MountRegistry.GetInstance(deviceNumber);
            return mount?.IsMountRunning ?? false;
        }

        /// <summary>
        /// Performs a full instance replacement of a single device after a breaking settings change.
        /// Clears all ASCOM client connections, tears down the existing mount instance, then
        /// creates a fresh instance from the settings now on disk. ASCOM clients must reconnect.
        /// </summary>
        /// <param name="deviceNumber">Device to replace.</param>
        public Task ReplaceDeviceAsync(int deviceNumber)
        {
            var existingMount = MountRegistry.GetInstance(deviceNumber);
            if (existingMount is null)
            {
                _logger.LogWarning("ReplaceDevice: device {Number} not found in registry", deviceNumber);
                return Task.CompletedTask;
            }

            _logger.LogInformation("ReplaceDevice: tearing down device {Number} for instance recreation", deviceNumber);

            // Capture the unique ID before teardown.
            var alpacaDevices = _settingsService.GetAlpacaDevices();
            string uniqueId = alpacaDevices.FirstOrDefault(d => d.DeviceNumber == deviceNumber)?.UniqueId
                              ?? Guid.NewGuid().ToString();

            // Tear down: clear clients, stop hardware, remove from both registries.
            existingMount.ClearAllConnections();
            MountRegistry.RemoveInstance(deviceNumber);
            DeviceManager.UnloadTelescope(deviceNumber);

            // Reload settings from disk so the fresh instance starts with the saved values.
            var allDevices = _settingsService.GetAllDeviceSettings();
            var deviceModel = allDevices.FirstOrDefault(d => d.DeviceNumber == deviceNumber);
            if (deviceModel is null)
            {
                _logger.LogError("ReplaceDevice: no settings found on disk for device {Number}", deviceNumber);
                return Task.CompletedTask;
            }

            // Recreate from disk.
            var freshSettings = new GreenSwamp.Alpaca.MountControl.SkySettings(deviceModel, _settingsService);
            RegisterDevice(deviceNumber, deviceModel.DeviceName ?? $"device-{deviceNumber}", uniqueId, freshSettings);
            MountRegistry.GetInstance(deviceNumber)?.InitializeSettings();

            _logger.LogInformation("ReplaceDevice: device {Number} recreated from disk", deviceNumber);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Handles DeviceSettingsChanged from the settings service.
        /// Applies the updated settings to the live mount instance.
        /// </summary>
        private void OnDeviceSettingsChanged(object? sender, ModelSkySettings incoming)
        {
            var mount = MountRegistry.GetInstance(incoming.DeviceNumber);
            if (mount is null)
                return;

            mount.Settings.ApplySettings(incoming);
            _logger.LogDebug("Applied settings to device {Number}", incoming.DeviceNumber);
        }
    }
}

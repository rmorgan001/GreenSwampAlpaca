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

using GreenSwamp.Alpaca.Settings.Models;

namespace GreenSwamp.Alpaca.Shared
{
    /// <summary>
    /// Factory class providing preconfigured monitor settings presets.
    /// Each preset combines specific device, category, and message type filters with appropriate logging options.
    /// </summary>
    public static class MonitorPresets
    {
        /// <summary>
        /// Development preset: Maximum logging with all devices, categories, and message types enabled.
        /// Ideal for development and debugging, captures everything to understand application behavior.
        /// </summary>
        /// <returns>A MonitorSettings object configured for development logging</returns>
        public static MonitorSettings GetDevelopmentPreset()
        {
            return new MonitorSettings
            {
                // Device filters - all enabled
                ServerDevice = true,
                Telescope = true,
                Ui = true,

                // Category filters - all enabled
                Other = true,
                Driver = true,
                Interface = true,
                Server = true,
                Mount = true,
                Alignment = true,

                // Type filters - all enabled
                Information = true,
                Data = true,
                Warning = true,
                Error = true,
                Debug = true,

                // Logging options - maximum logging
                LogMonitor = true,
                LogSession = true,
                LogCharting = false,
                StartMonitor = true,

                // Miscellaneous
                Language = "en-US",
                LogPath = "",
                Version = "0"
            };
        }

        /// <summary>
        /// Production preset: Minimal logging focused on critical issues.
        /// Recommended for operational environments where performance and disk space are concerns.
        /// Only essential server and mount events are logged.
        /// </summary>
        /// <returns>A MonitorSettings object configured for production logging</returns>
        public static MonitorSettings GetProductionPreset()
        {
            return new MonitorSettings
            {
                // Device filters - only server and telescope
                ServerDevice = true,
                Telescope = true,
                Ui = false,

                // Category filters - minimal
                Other = false,
                Driver = false,
                Interface = false,
                Server = true,
                Mount = true,
                Alignment = false,

                // Type filters - warnings and errors only
                Information = true,
                Data = false,
                Warning = true,
                Error = true,
                Debug = false,

                // Logging options - minimal logging
                LogMonitor = false,
                LogSession = true,
                LogCharting = false,
                StartMonitor = true,

                // Miscellaneous
                Language = "en-US",
                LogPath = "",
                Version = "0"
            };
        }

        /// <summary>
        /// Troubleshooting preset: Focused on mount operations and detailed data collection.
        /// Useful when diagnosing mount movement, slewing, or alignment issues.
        /// Includes driver data and full mount operation details.
        /// </summary>
        /// <returns>A MonitorSettings object configured for troubleshooting logging</returns>
        public static MonitorSettings GetTroubleshootingPreset()
        {
            return new MonitorSettings
            {
                // Device filters - server and telescope
                ServerDevice = true,
                Telescope = true,
                Ui = false,

                // Category filters - mount and driver focused
                Other = false,
                Driver = true,
                Interface = true,
                Server = true,
                Mount = true,
                Alignment = false,

                // Type filters - all events
                Information = true,
                Data = true,
                Warning = true,
                Error = true,
                Debug = false,

                // Logging options - full logging
                LogMonitor = true,
                LogSession = true,
                LogCharting = false,
                StartMonitor = true,

                // Miscellaneous
                Language = "en-US",
                LogPath = "",
                Version = "0"
            };
        }

        /// <summary>
        /// Profile Debug preset: Focused on server-side settings loading and configuration issues.
        /// Used when diagnosing application startup, configuration, or settings-related problems.
        /// Captures detailed server initialization and settings application events.
        /// </summary>
        /// <returns>A MonitorSettings object configured for profile debugging</returns>
        public static MonitorSettings GetProfileDebugPreset()
        {
            return new MonitorSettings
            {
                // Device filters - server only
                ServerDevice = true,
                Telescope = false,
                Ui = false,

                // Category filters - server focused
                Other = false,
                Driver = false,
                Interface = false,
                Server = true,
                Mount = true,
                Alignment = false,

                // Type filters - information and errors
                Information = true,
                Data = false,
                Warning = true,
                Error = true,
                Debug = false,

                // Logging options - full logging
                LogMonitor = true,
                LogSession = true,
                LogCharting = false,
                StartMonitor = true,

                // Miscellaneous
                Language = "en-US",
                LogPath = "",
                Version = "0"
            };
        }
    }
}

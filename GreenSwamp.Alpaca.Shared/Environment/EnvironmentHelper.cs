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

using GreenSwamp.Alpaca.Settings.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GreenSwamp.Alpaca.Shared.EnvironmentLog
{
    /// <summary>
    /// Convenience façade for environment logging.
    /// Resolves the log file path via <see cref="SettingsPathResolver"/> and
    /// delegates to <see cref="EnvironmentLogger"/>.
    /// </summary>
    public static class EnvironmentHelper
    {
        private const string LogPattern = "GreenSwampEnv*.log";
        private const int KeepCount = 3;
        private const int TimeoutSeconds = 10;

        /// <summary>
        /// Write the environment log to the standard log location asynchronously.
        /// Old logs beyond the keep count are pruned after writing.
        /// </summary>
        /// <returns>
        /// The path of the created log file, or <c>null</c> if logging failed.
        /// </returns>
        public static async Task<string?> LogToDefaultLocationAsync()
        {
            try
            {
                var logPath = GetDefaultLogPath();
                await EnvironmentLogger.LogEnvironmentAsync(logPath, TimeoutSeconds).ConfigureAwait(false);
                CleanupLogs(logPath);
                return logPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the full path for the next environment log file.
        /// Format: <c>&lt;LogsRoot&gt;\GreenSwampEnv_yyyy-MM-dd_HHmmss.log</c>
        /// </summary>
        public static string GetDefaultLogPath()
        {
            var logsRoot = SettingsPathResolver.GetLogsRoot();
            var fileName = $"GreenSwampEnv_{DateTime.Now:yyyy-MM-dd_HHmmss}.log";
            return Path.Combine(logsRoot, fileName);
        }

        /// <summary>
        /// Returns the directory where environment logs are stored.
        /// </summary>
        public static string GetLogDirectory() => SettingsPathResolver.GetLogsRoot();

        // ── Private ──────────────────────────────────────────────────────────

        private static void CleanupLogs(string logPath)
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir))
                EnvironmentLogger.CleanupOldLogs(dir, LogPattern, KeepCount);
        }
    }
}

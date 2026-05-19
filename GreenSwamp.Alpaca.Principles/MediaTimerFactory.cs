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

using System.Runtime.InteropServices;

namespace GreenSwamp.Alpaca.Principles
{
    /// <summary>
    /// Creates the appropriate <see cref="IMediaTimer"/> implementation for the current OS.
    /// Use this instead of instantiating <see cref="MediaTimer"/> directly so that the code
    /// remains portable across Windows, Linux, and Raspberry Pi.
    /// </summary>
    public static class MediaTimerFactory
    {
        /// <summary>
        /// Returns a new <see cref="IMediaTimer"/> instance.
        /// On Windows this is a <see cref="MediaTimer"/> (winmm multimedia timer).
        /// On all other platforms this is a <see cref="LinuxMediaTimer"/> (thread-based timer).
        /// </summary>
        public static IMediaTimer Create()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new MediaTimer();
            return new LinuxMediaTimer();
        }
    }
}

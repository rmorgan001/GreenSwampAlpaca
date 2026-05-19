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

using System;

namespace GreenSwamp.Alpaca.Principles
{
    /// <summary>
    /// Platform-agnostic periodic/one-shot timer abstraction.
    /// Use <see cref="MediaTimerFactory.Create"/> to obtain the correct implementation
    /// for the current operating system.
    /// </summary>
    public interface IMediaTimer : IDisposable
    {
        /// <summary>Period between Tick events in milliseconds.</summary>
        int Period { get; set; }

        /// <summary>Timer resolution hint in milliseconds (best-effort on non-Windows platforms).</summary>
        int Resolution { get; set; }

        /// <summary>Periodic or one-shot firing mode.</summary>
        TimerMode Mode { get; set; }

        /// <summary>True while the timer is running.</summary>
        bool IsRunning { get; }

        /// <summary>Raised on each timer interval.</summary>
        event EventHandler Tick;

        /// <summary>Raised when the timer starts.</summary>
        event EventHandler Started;

        /// <summary>Raised when the timer stops.</summary>
        event EventHandler Stopped;

        /// <summary>Starts the timer.</summary>
        void Start();

        /// <summary>Stops the timer.</summary>
        void Stop();
    }
}

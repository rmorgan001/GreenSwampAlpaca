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

using System.ComponentModel.DataAnnotations;

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// A single named observatory site with its physical properties.
    /// </summary>
    public class ObservatoryInfo
    {
        /// <summary>Stable GUID used as a dictionary key and tree-node identity.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>User-defined display name shown in the Settings Explorer tree.</summary>
        [Required, MaxLength(100)]
        public string Name { get; set; } = "Default Observatory";

        [Range(-90, 90)]
        public double Latitude { get; set; } = 51.476852;

        [Range(-180, 180)]
        public double Longitude { get; set; } = 0.0;

        [Range(-500, 9000)]
        public double Elevation { get; set; } = 10.0;

        public TimeSpan UTCOffset { get; set; } = TimeSpan.Zero;
    }

    /// <summary>
    /// Collection of observatory sites — content of observatory.settings.json.
    /// Replaces the previous single-observatory model (no backwards compatibility).
    /// </summary>
    public class ObservatorySettings
    {
        public List<ObservatoryInfo> Observatories { get; set; } = [];
    }
}

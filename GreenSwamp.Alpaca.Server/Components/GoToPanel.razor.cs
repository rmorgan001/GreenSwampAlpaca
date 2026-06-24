using GreenSwamp.Alpaca.MountControl;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace GreenSwamp.Alpaca.Server.Components
{
    public partial class GoToPanel
    {
        [Parameter] public int DeviceNumber { get; set; }
        [Parameter] public bool IsEnabled { get; set; }

        private enum CoordMode { RaDec, AltAz }
        private enum EntryMode { HmsDms, Float }

        private GreenSwamp.Alpaca.MountControl.Mount? _mount;
        private bool _isMountRunning;
        private double _horizonLimit;
        private int _altDMin;
        private bool _canSync;

        private CoordMode _coordMode = CoordMode.RaDec;
        private EntryMode _entryMode = EntryMode.HmsDms;

        // DMS / HMS backing fields
        private int _raH, _raM;
        private double _raS;
        private int _decD, _decM;
        private double _decS;
        private int _azD, _azM;
        private double _azS;
        private int _altD, _altM;
        private double _altS;

        // Float backing fields
        private double _raFloat;
        private double _decFloat;
        private double _azFloat;
        private double _altFloat;

        protected override void OnInitialized()
        {
            _mount = MountRegistry.GetInstance(DeviceNumber);
            _isMountRunning = _mount?.IsMountRunning ?? false;
            _horizonLimit = _mount?.Settings.AxisLowerLimitY ?? 0.0;
            _altDMin = (int)Math.Floor(_horizonLimit);
            _canSync = _mount?.Settings.CanSync ?? false;
        }

        /// <summary>Converts between HMS/DMS and float representations when the entry mode changes.</summary>
        private void OnEntryModeChanged(EntryMode newMode)
        {
            if (_entryMode == newMode) return;

            if (newMode == EntryMode.Float)
            {
                _raFloat = HmsToHours(_raH, _raM, _raS);
                _decFloat = DmsToDegs(_decD, _decM, _decS);
                _azFloat = DmsToDegs(_azD, _azM, _azS);
                _altFloat = DmsToDegs(_altD, _altM, _altS);
            }
            else
            {
                (_raH, _raM, _raS) = HoursToHms(_raFloat);
                (_decD, _decM, _decS) = DegsToDegs(_decFloat);
                (_azD, _azM, _azS) = DegsToDegs(_azFloat);
                (_altD, _altM, _altS) = DegsToDegs(_altFloat);
            }

            _entryMode = newMode;
        }

        /// <summary>Commands the mount to slew to the entered coordinates.</summary>
        private async Task OnGoTo()
        {
            _isMountRunning = _mount?.IsMountRunning ?? false;
            if (_mount == null || !_isMountRunning)
            {
                Snackbar.Add("Mount is not running.", Severity.Error);
                return;
            }

            try
            {
                SlewResult result;
                if (_coordMode == CoordMode.RaDec)
                {
                    var ra = _entryMode == EntryMode.Float ? _raFloat : HmsToHours(_raH, _raM, _raS);
                    var dec = _entryMode == EntryMode.Float ? _decFloat : DmsToDegs(_decD, _decM, _decS);
                    result = await _mount.SlewRaDecAsync(ra, dec, tracking: true);
                }
                else
                {
                    var az = _entryMode == EntryMode.Float ? _azFloat : DmsToDegs(_azD, _azM, _azS);
                    var alt = _entryMode == EntryMode.Float ? _altFloat : DmsToDegs(_altD, _altM, _altS);
                    result = await _mount.SlewAltAzAsync(alt, az);
                }

                if (result.CanProceed)
                    Snackbar.Add("GoTo in progress\u2026", Severity.Info);
                else
                    Snackbar.Add($"GoTo rejected: {result.ErrorMessage}", Severity.Warning);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"GoTo failed: {ex.Message}", Severity.Error);
            }
        }

        /// <summary>Copies the current live mount position into all entry fields.</summary>
        private void OnCopy()
        {
            var state = StateService.GetCurrentState(DeviceNumber);

            (_raH, _raM, _raS) = HoursToHms(state.RightAscension);
            (_decD, _decM, _decS) = DegsToDegs(state.Declination);
            (_azD, _azM, _azS) = DegsToDegs(state.Azimuth);
            (_altD, _altM, _altS) = DegsToDegs(state.Altitude);

            _raFloat = state.RightAscension;
            _decFloat = state.Declination;
            _azFloat = state.Azimuth;
            _altFloat = state.Altitude;
        }

        /// <summary>Syncs the mount to the entered RA/Dec coordinates.</summary>
        private async Task OnSync()
        {
            _isMountRunning = _mount?.IsMountRunning ?? false;
            if (_mount == null || !_isMountRunning)
            {
                Snackbar.Add("Mount is not running.", Severity.Error);
                return;
            }

            try
            {
                var ra = _entryMode == EntryMode.Float ? _raFloat : HmsToHours(_raH, _raM, _raS);
                var dec = _entryMode == EntryMode.Float ? _decFloat : DmsToDegs(_decD, _decM, _decS);

                _mount.TargetRa = ra;
                _mount.TargetDec = dec;
                await Task.Run(() => _mount.SyncToTargetRaDec());
                Snackbar.Add("Sync complete.", Severity.Success);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Sync failed: {ex.Message}", Severity.Error);
            }
        }

        // ── Coordinate conversion helpers ───────────────────────────────────────

        private static double HmsToHours(int h, int m, double s) =>
            h + m / 60.0 + s / 3600.0;

        private static (int h, int m, double s) HoursToHms(double hours)
        {
            hours = Math.Max(0.0, hours);
            var h = (int)hours;
            var rem = (hours - h) * 60.0;
            var m = (int)rem;
            var s = (rem - m) * 60.0;
            return (h, m, s);
        }

        /// <summary>
        /// Converts DMS to decimal degrees. The sign is carried in <paramref name="d"/>;
        /// minutes and seconds are always positive. E.g. d=-45, m=30, s=0 → -45.5°.
        /// </summary>
        private static double DmsToDegs(int d, int m, double s)
        {
            var neg = d < 0;
            var total = Math.Abs(d) + m / 60.0 + s / 3600.0;
            return neg ? -total : total;
        }

        private static (int d, int m, double s) DegsToDegs(double degrees)
        {
            var neg = degrees < 0;
            degrees = Math.Abs(degrees);
            var d = (int)degrees;
            var rem = (degrees - d) * 60.0;
            var m = (int)rem;
            var s = (rem - m) * 60.0;
            return (neg ? -d : d, m, s);
        }
    }
}

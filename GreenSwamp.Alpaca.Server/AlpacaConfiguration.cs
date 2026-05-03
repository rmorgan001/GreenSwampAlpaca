using ASCOM.Alpaca;
using GreenSwamp.Alpaca.Settings.Models;

namespace GreenSwamp.Alpaca.Server
{
    /// <summary>
    /// Implements IAlpacaConfiguration using the unified ServerConfig model
    /// from GreenSwamp.Alpaca.Settings, replacing the former ASCOM XMLProfile-backed
    /// ServerSettings static class.
    /// </summary>
    internal class AlpacaConfiguration : IAlpacaConfiguration
    {
        private readonly ServerConfig _config;

        internal AlpacaConfiguration(ServerConfig config)
        {
            _config = config;
        }

        public bool RunInStrictAlpacaMode => _config.RunInStrictAlpacaMode;

        public bool PreventRemoteDisconnects => _config.PreventRemoteDisconnects;

        public string ServerName => Program.ServerName;

        public string Manufacturer => Program.Manufacturer;

        public string ServerVersion => Program.ServerVersion;

        public string Location => _config.Location;

        public bool AllowImageBytesDownload => _config.AllowImageBytesDownload;

        public bool AllowDiscovery => _config.AllowDiscovery;

        public int ServerPort => _config.ServerPort;

        public bool AllowRemoteAccess => _config.AllowRemoteAccess;

        public bool LocalRespondOnlyToLocalHost => _config.LocalRespondOnlyToLocalHost;

        public bool RunSwagger => _config.RunSwagger;
    }
}

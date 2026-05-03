using ASCOM.Alpaca;
using GreenSwamp.Alpaca.Settings.Services;

namespace GreenSwamp.Alpaca.Server.Data
{
	internal class UserService : IUserService
	{
		private readonly IVersionedSettingsService _settings;

		public UserService(IVersionedSettingsService settings)
		{
			_settings = settings;
		}

		public async Task<bool> Authenticate(string username, string password)
		{
			return await Task.Run(() =>
			{
				try
				{
					var config = _settings.GetServerConfig();
					return username == config.UserName && Hash.Validate(config.Password, password);
				}
				catch
				{
					return false;
				}
			});
		}

		public bool UseAuth => _settings.GetServerConfig().UseAuth;
	}
}

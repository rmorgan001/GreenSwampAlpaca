using Microsoft.Extensions.Logging;
using System.Net;

namespace ASCOM.Alpaca
{
    public class Logging
    {
        private static ILogger? _log;
        private static AscomLoggerAdapter? _adapter;

        /// <summary>
        /// Returns an ASCOM ILogger adapter wrapping the current MEL logger.
        /// Used by ASCOM discovery components that require ASCOM.Common.Interfaces.ILogger.
        /// </summary>
        internal static ASCOM.Common.Interfaces.ILogger? Log => _adapter;

        public static void AttachLogger(ILogger log)
        {
            _log = log;
            _adapter = new AscomLoggerAdapter(log);
        }

        public static void LogError(string message)
        {
            try
            {
                if (_log != null)
                    _log.LogError(message);
                else
                    Console.Error.WriteLine($"[ERROR] {message}");
            }
            catch
            {
                //Log should never throw.
            }
        }

        public static void LogVerbose(string message)
        {
            try
            {
                if (_log != null)
                    _log.LogDebug(message);
                else
                    Console.WriteLine($"[DEBUG] {message}");
            }
            catch
            {
                //Log should never throw.
            }
        }

        public static void LogWarning(string message)
        {
            try
            {
                if (_log != null)
                    _log.LogWarning(message);
                else
                    Console.WriteLine($"[WARN] {message}");
            }
            catch
            {
                //Log should never throw.
            }
        }

        internal static void LogAPICall(IPAddress remoteIpAddress, string request, uint clientID, uint clientTransactionID, uint transactionID)
        {
            LogVerbose($"Transaction: {transactionID} - {remoteIpAddress} ({clientID}, {clientTransactionID}) requested {request}");
        }
    }
}

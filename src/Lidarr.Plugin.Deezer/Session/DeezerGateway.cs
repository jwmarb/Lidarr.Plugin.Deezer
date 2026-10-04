using System.Collections.Generic;
using System.Net;
using System.Text;

namespace NzbDrone.Plugin.Deezer
{
    /// <summary>
    /// Builds gw-light.php URLs. The one place query strings are assembled; the
    /// previous implementation had this loop written out twice, once for a method
    /// that had no callers.
    /// </summary>
    public static class DeezerGateway
    {
        private const string GatewayBase = "https://www.deezer.com/ajax/gw-light.php";

        public static string Url(IReadOnlyDictionary<string, string> parameters)
        {
            var builder = new StringBuilder(GatewayBase);
            var first = true;

            foreach (var pair in parameters)
            {
                builder.Append(first ? '?' : '&');
                builder.Append(WebUtility.UrlEncode(pair.Key));
                builder.Append('=');
                builder.Append(WebUtility.UrlEncode(pair.Value));
                first = false;
            }

            return builder.ToString();
        }
    }
}

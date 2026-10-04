namespace NzbDrone.Core.Plugins
{
    public class DeezerPlugin : Plugin
    {
        // Name and Owner must match the repo name and owner in GithubUrl.
        // Lidarr parses those from the URL the user pastes (PluginService.ParseUrl)
        // and matches installed plugins on Owner + Name, so a mismatch breaks
        // update checks and uninstall detection.
        public override string Name => "Lidarr.Plugin.Deezer";
        public override string Owner => "jwmarb";
        public override string GithubUrl => "https://github.com/jwmarb/Lidarr.Plugin.Deezer";
    }
}

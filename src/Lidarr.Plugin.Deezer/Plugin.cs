namespace NzbDrone.Core.Plugins
{
    public class DeezerPlugin : Plugin
    {
        // Name is the plugin's display name in Lidarr's System -> Plugins table
        // (PluginResource.Name -> PluginRow). It is deliberately the short product
        // name, not the repo name: Lidarr's own integration test asserts the
        // Deemix plugin reports "Deemix" while living in "Lidarr.Plugin.Deemix".
        //
        // Owner + GithubUrl are the identity that actually matters and must keep
        // matching the GitHub repo: install/uninstall resolve the plugin folder as
        // <plugins>/<owner>/<repo> by parsing GithubUrl (PluginService.ParseUrl),
        // and update checks hit the releases API for that same URL.
        //
        // The assembly name must stay "Lidarr.Plugin.Deezer" regardless of this
        // value -- plugin discovery globs "Lidarr.Plugin.*.dll" by file name.
        public override string Name => "Deezer";
        public override string Owner => "jwmarb";
        public override string GithubUrl => "https://github.com/jwmarb/Lidarr.Plugin.Deezer";
    }
}

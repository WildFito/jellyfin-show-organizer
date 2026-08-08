using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ShowOrganizer.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string TmdbApiKey { get; set; } = string.Empty;
        public bool HideMissingCastMembers { get; set; } = false;
        public int MaxCastMembers { get; set; } = 20;
        public bool HideMissingCrewMembers { get; set; } = false;
        public int MaxCrewMembers { get; set; } = 10;
    }
}

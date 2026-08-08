using System;
using Jellyfin.Plugin.ShowOrganizer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ShowOrganizer
{
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name => "ShowOrganizer";

        public override Guid Id => Guid.Parse("f98bb2d0-ea65-4f36-be5d-ff63d7d7b1d1");

        public static Plugin? Instance { get; internal set; }
    }
}

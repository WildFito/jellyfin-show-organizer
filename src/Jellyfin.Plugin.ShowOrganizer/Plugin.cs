using System;
using Jellyfin.Plugin.ShowOrganizer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShowOrganizer
{
    public class Plugin : BasePlugin<PluginConfiguration>, IDisposable
    {
        private readonly ILogger<Plugin>? _logger;

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin>? logger = null)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _logger = logger;

            var asm = GetType().Assembly;
            var ver = Version?.ToString() ?? asm.GetName().Version?.ToString() ?? "0.0.0.0";
            var location = string.IsNullOrEmpty(asm.Location) ? "unknown" : asm.Location;
            var pid = Environment.ProcessId;
            var alcName = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(asm)?.Name ?? "Default";

            _logger?.LogInformation("ShowOrganizer: Plugin initialized. Version={Version} Assembly={Location} PID={PID}", ver, location, pid);
            _logger?.LogDebug("ShowOrganizer: Assembly diagnostics. FullName={FullName} ALC={ALC}", asm.FullName, alcName);
        }

        public override string Name => "ShowOrganizer";

        public override Guid Id => Guid.Parse("f98bb2d0-ea65-4f36-be5d-ff63d7d7b1d1");

        public static Plugin? Instance { get; internal set; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                var asm = GetType().Assembly;
                var ver = Version?.ToString() ?? asm.GetName().Version?.ToString() ?? "0.0.0.0";
                var location = string.IsNullOrEmpty(asm.Location) ? "unknown" : asm.Location;
                var pid = Environment.ProcessId;

                _logger?.LogInformation("ShowOrganizer: Dispose started. Version={Version} Assembly={Location} PID={PID}", ver, location, pid);

                if (Instance == this)
                {
                    Instance = null;
                    _logger?.LogInformation("ShowOrganizer: Plugin.Instance cleared.");
                }

                Providers.Tmdb.ShowOrganizerEpisodeProvider.ResetState(_logger);

                _logger?.LogInformation("ShowOrganizer: Dispose completed.");
            }
        }
    }
}

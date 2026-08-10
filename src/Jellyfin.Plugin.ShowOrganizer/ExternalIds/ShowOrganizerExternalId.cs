using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.ShowOrganizer.ExternalIds
{
    public class ShowOrganizerExternalId : IExternalId
    {
        public string ProviderName => "TheMovieDb Show Group";

        public string Key => "ShowOrganizer";

        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

        public bool Supports(IHasProviderIds item)
        {
            return item is Series;
        }
    }
}

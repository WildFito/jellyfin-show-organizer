using Jellyfin.Plugin.ShowOrganizer.Models;

namespace Jellyfin.Plugin.ShowOrganizer.Services
{
    public enum ShowOrganizerEligibilityState
    {
        Inactive,
        InvalidMissingTmdbId,
        InvalidReference,
        UnsupportedProvider,
        Eligible
    }

    public class ShowOrganizerEligibilityResult
    {
        public ShowOrganizerEligibilityState State { get; }
        public ShowOrderReference? OrderReference { get; }
        public int SeriesTmdbId { get; }
        public string Fingerprint { get; }

        public ShowOrganizerEligibilityResult(
            ShowOrganizerEligibilityState state,
            ShowOrderReference? orderReference,
            int seriesTmdbId,
            string fingerprint)
        {
            State = state;
            OrderReference = orderReference;
            SeriesTmdbId = seriesTmdbId;
            Fingerprint = fingerprint;
        }
    }
}

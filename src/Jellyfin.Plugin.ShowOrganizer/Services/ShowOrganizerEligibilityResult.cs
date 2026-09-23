using Jellyfin.Plugin.ShowOrganizer.Models;

namespace Jellyfin.Plugin.ShowOrganizer.Services;

public enum ShowOrganizerEligibilityState
{
    Inactive,
    InvalidMissingTmdbId,
    InvalidReference,
    UnsupportedProvider,
    Eligible
}

public class ShowOrganizerEligibilityResult(
    ShowOrganizerEligibilityState state,
    ShowOrderReference? orderReference,
    int seriesTmdbId,
    string fingerprint)
{
    public ShowOrganizerEligibilityState State { get; } = state;

    public ShowOrderReference? OrderReference { get; } = orderReference;

    public int SeriesTmdbId { get; } = seriesTmdbId;

    public string Fingerprint { get; } = fingerprint;
}

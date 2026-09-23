using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.ShowOrganizer.Models;

public class ShowOrderReference(string provider, string orderId)
{
    public string Provider { get; } = provider;

    public string OrderId { get; } = orderId;

    public static bool TryParse(string? value, [NotNullWhen(true)] out ShowOrderReference? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cleanValue = value.Trim();

        if (cleanValue.Contains(':', StringComparison.Ordinal))
        {
            var parts = cleanValue.Split(':', 2);
            var provider = parts[0].Trim().ToLowerInvariant();
            var orderId = parts[1].Trim();

            if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(orderId))
            {
                return false;
            }

            result = new ShowOrderReference(provider, orderId);
            return true;
        }

        result = new ShowOrderReference("tmdb", cleanValue);
        return true;
    }

    public override string ToString() => $"{Provider}:{OrderId}";
}

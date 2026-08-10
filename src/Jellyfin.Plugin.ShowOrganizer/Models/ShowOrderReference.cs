using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.ShowOrganizer.Models
{
    public class ShowOrderReference
    {
        public ShowOrderReference(string provider, string orderId)
        {
            Provider = provider;
            OrderId = orderId;
        }

        public string Provider { get; }
        public string OrderId { get; }

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
}

using System.Security.Cryptography;
using System.Text;
using AbaddonRGB.Core.Engines;

namespace AbaddonRGB.Core;

/// <summary>
/// Identidade persistente, independente da posição temporária do dispositivo no motor.
/// Prioriza número de série; depois localização; por último nome e formato das zonas.
/// A última alternativa é menos confiável para peças fisicamente idênticas.
/// </summary>
public static class DeviceIdentity
{
    public static string Create(string name, string? vendor = null, string? serial = null,
        string? location = null, int ledCount = 0, IReadOnlyList<LightZone>? zones = null)
    {
        var manufacturer = Normalize(vendor);
        var model = Normalize(name);
        string fingerprint;

        if (!string.IsNullOrWhiteSpace(serial))
            fingerprint = $"serial|{manufacturer}|{model}|{Normalize(serial)}";
        else if (!string.IsNullOrWhiteSpace(location))
            fingerprint = $"location|{manufacturer}|{model}|{Normalize(location)}";
        else
            fingerprint = $"shape|{manufacturer}|{model}|{ledCount}|" +
                string.Join("|", (zones ?? Array.Empty<LightZone>()).Select(z => $"{Normalize(z.Name)}:{z.LedCount}"));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        return "abaddon:" + Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    private static string Normalize(string? text) =>
        string.Join(" ", (text ?? "").Trim().ToUpperInvariant().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

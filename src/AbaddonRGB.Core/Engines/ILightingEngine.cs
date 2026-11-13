namespace AbaddonRGB.Core.Engines;

/// <summary>Uma zona de um dispositivo (ex.: "IO Cover", "Addressable Header").</summary>
public sealed record LightZone(string Name, int LedCount);

/// <summary>Uma peça com LEDs, do jeito que o Abaddon RGB enxerga, não importa o motor.</summary>
/// <param name="Location">Onde a peça está ligada, como o motor informa (ex.: endereço no barramento da placa).</param>
public sealed record LightDevice(string EngineId, int Index, string Name, IReadOnlyList<LightZone> Zones, int LedCount,
    string StableId = "", string Location = "")
{
    /// <summary>
    /// Identificador do aparelho USB ("usb:046D:C33C"), tirado da localização, ou "" se não houver.
    /// Serve para reconhecer a mesma peça vista por dois motores (OpenRGB e nativo).
    /// </summary>
    public string HardwareKey
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Location)) return "";
            var match = System.Text.RegularExpressions.Regex.Match(Location,
                @"vid_([0-9a-f]{4}).*?pid_([0-9a-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success
                ? $"usb:{match.Groups[1].Value.ToUpperInvariant()}:{match.Groups[2].Value.ToUpperInvariant()}"
                : "";
        }
    }

    /// <summary>Versão curta da localização: o endereço "0x.." quando houver.</summary>
    public string ShortLocation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Location)) return "";
            var match = System.Text.RegularExpressions.Regex.Match(Location, @"0x[0-9A-Fa-f]+");
            return match.Success ? match.Value.ToUpperInvariant().Replace("0X", "0x") : Location.Trim();
        }
    }
}

/// <summary>
/// Um "motor" é quem realmente conversa com o hardware.
/// Hoje existe o motor OpenRGB; os drivers nativos entram pelo <see cref="NativeEngine"/>.
/// </summary>
public interface ILightingEngine : IDisposable
{
    string Id { get; }
    string DisplayName { get; }
    bool IsConnected { get; }

    bool TryConnect(out string? error);

    /// <summary>Somente leitura: descobre as peças sem enviar nada para elas.</summary>
    IReadOnlyList<LightDevice> GetDevices();

    /// <summary>Envia uma cor para cada LED da peça.</summary>
    void SetColors(LightDevice device, IReadOnlyList<RgbColor> colors);
}

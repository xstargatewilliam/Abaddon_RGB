using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbaddonRGB.Core;

/// <summary>
/// Correção de ordem de cores para uma zona específica de um dispositivo.
/// </summary>
public sealed class ZoneCorrection
{
    /// <summary>Trecho do nome do dispositivo (sem diferenciar maiúsculas). Ex.: "ASRock".</summary>
    public string Device { get; set; } = "";

    /// <summary>Trecho do nome da zona. Ex.: "Addressable Header".</summary>
    public string Zone { get; set; } = "";

    /// <summary>Ordem em que os LEDs da zona leem as cores. Ex.: "GRB".</summary>
    public string Order { get; set; } = ColorOrder.Default;

    public bool Matches(string deviceName, string zoneName) =>
        !string.IsNullOrWhiteSpace(Device) &&
        !string.IsNullOrWhiteSpace(Zone) &&
        deviceName.Contains(Device, StringComparison.OrdinalIgnoreCase) &&
        zoneName.Contains(Zone, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Configurações do Abaddon RGB, salvas em %APPDATA%\AbaddonRGB\config.json.
/// </summary>
public sealed class AppConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6742;

    /// <summary>Última cor aplicada, em hexadecimal (#RRGGBB).</summary>
    public string Color { get; set; } = "#3A6EA5";

    /// <summary>Brilho de 0 a 100.</summary>
    public int Brightness { get; set; } = 100;

    /// <summary>Reaplica a última cor assim que o programa conecta ao OpenRGB.</summary>
    public bool ApplyOnStart { get; set; } = true;

    /// <summary>Procura e instala novas versões do GitHub sozinho.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Versão do formato do arquivo, para migrar configurações antigas.</summary>
    public int ConfigVersion { get; set; } = CurrentConfigVersion;

    private const int CurrentConfigVersion = 2;

    /// <summary>
    /// Correções de ordem de cor por zona. Começa vazia: no modo usado pelo Abaddon RGB
    /// (cor por LED), o cooler Rise Mode da ASRock já recebe as cores na ordem certa.
    /// Exemplo: { "Device": "ASRock", "Zone": "Addressable", "Order": "GRB" }.
    /// </summary>
    public List<ZoneCorrection> Corrections { get; set; } = new();

    [JsonIgnore]
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AbaddonRGB");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Folder, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config is not null)
                {
                    // Arquivos antigos não têm "ConfigVersion": nesse caso ele vale 0.
                    using (var doc = JsonDocument.Parse(json))
                    {
                        if (!doc.RootElement.TryGetProperty(nameof(ConfigVersion), out _))
                            config.ConfigVersion = 0;
                    }

                    config.Normalize();
                    if (config.Migrate())
                        try { config.Save(); } catch { }
                    return config;
                }
            }
        }
        catch
        {
            // Arquivo corrompido: guarda uma cópia e recomeça com o padrão.
            try { File.Copy(FilePath, FilePath + ".bak", overwrite: true); } catch { }
        }

        var fresh = new AppConfig();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private void Normalize()
    {
        Brightness = Math.Clamp(Brightness, 0, 100);
        if (Port is < 1 or > 65535) Port = 6742;
        if (string.IsNullOrWhiteSpace(Host)) Host = "127.0.0.1";
        if (!TryParseHex(Color, out _, out _, out _)) Color = "#3A6EA5";
        Corrections ??= new List<ZoneCorrection>();
    }

    /// <summary>Ajusta configurações criadas por versões antigas. Retorna true se mudou algo.</summary>
    private bool Migrate()
    {
        if (ConfigVersion >= CurrentConfigVersion) return false;

        // v0.1 vinha com a correção GRB do cooler ASRock, que se mostrou desnecessária.
        Corrections?.RemoveAll(c =>
            string.Equals(c.Device, "ASRock", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Zone, "Addressable", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Order, "GRB", StringComparison.OrdinalIgnoreCase));

        ConfigVersion = CurrentConfigVersion;
        return true;
    }

    public static bool TryParseHex(string? hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var value = hex.Trim().TrimStart('#');
        if (value.Length != 6) return false;

        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return false;

        r = (byte)((rgb >> 16) & 0xFF);
        g = (byte)((rgb >> 8) & 0xFF);
        b = (byte)(rgb & 0xFF);
        return true;
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}

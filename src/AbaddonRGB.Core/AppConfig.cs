using System.Text.Json;
using System.Text.Json.Serialization;

namespace AbaddonRGB.Core;

/// <summary>Correção de ordem de cores para uma zona específica de um dispositivo.</summary>
public sealed class ZoneCorrection
{
    /// <summary>Trecho do nome do dispositivo (sem diferenciar maiúsculas). Ex.: "ASRock".</summary>
    public string Device { get; set; } = "";
    public string DeviceId { get; set; } = "";

    /// <summary>Trecho do nome da zona. Ex.: "Addressable Header".</summary>
    public string Zone { get; set; } = "";

    /// <summary>Ordem em que os LEDs da zona leem as cores. Ex.: "GRB".</summary>
    public string Order { get; set; } = ColorOrder.Default;

    public bool Matches(string deviceName, string zoneName, string stableId = "") =>
        !string.IsNullOrWhiteSpace(Zone) &&
        (string.IsNullOrWhiteSpace(DeviceId)
            ? !string.IsNullOrWhiteSpace(Device) && deviceName.Contains(Device, StringComparison.OrdinalIgnoreCase)
            : string.Equals(DeviceId, stableId, StringComparison.OrdinalIgnoreCase)) &&
        zoneName.Contains(Zone, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Cor própria de uma peça inteira (Zone vazia) ou de uma zona dela.</summary>
public sealed class TargetColor
{
    public string Device { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";

    [JsonIgnore]
    public bool IsWholeDevice => string.IsNullOrWhiteSpace(Zone);

    public bool Matches(string deviceName, string zoneName, string stableId = "") =>
        (string.IsNullOrWhiteSpace(DeviceId)
            ? string.Equals(Device, deviceName, StringComparison.OrdinalIgnoreCase)
            : string.Equals(DeviceId, stableId, StringComparison.OrdinalIgnoreCase)) &&
        (IsWholeDevice || string.Equals(Zone, zoneName, StringComparison.OrdinalIgnoreCase));

    public bool SameTarget(string device, string zone, string stableId = "") =>
        (string.IsNullOrWhiteSpace(DeviceId) || string.IsNullOrWhiteSpace(stableId)
            ? string.Equals(Device, device, StringComparison.OrdinalIgnoreCase)
            : string.Equals(DeviceId, stableId, StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(Zone ?? "", zone ?? "", StringComparison.OrdinalIgnoreCase);

    public TargetColor Clone() => new() { Device = Device, DeviceId = DeviceId, Zone = Zone, Color = Color };
}

/// <summary>Nome escolhido pela pessoa para uma peça (Zone vazia) ou uma zona. Ex.: "Cooler e fans".</summary>
public sealed class Nickname
{
    public string Device { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Name { get; set; } = "";

    public bool Matches(string device, string zone, string stableId) =>
        (string.IsNullOrWhiteSpace(DeviceId) || string.IsNullOrWhiteSpace(stableId)
            ? string.Equals(Device, device, StringComparison.OrdinalIgnoreCase)
            : string.Equals(DeviceId, stableId, StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(Zone ?? "", zone ?? "", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Um conjunto de cores salvo com nome (ex.: "Dia", "Noite").</summary>
public sealed class Profile
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3A6EA5";
    public int Brightness { get; set; } = 100;
    public LightEffect Effect { get; set; } = LightEffect.Static;
    public int EffectSpeed { get; set; } = 5;
    public List<TargetColor> Overrides { get; set; } = new();
}

public sealed class ScheduleRule
{
    /// <summary>Horário no formato 24h, "HH:mm".</summary>
    public string Time { get; set; } = "22:00";
    public string Profile { get; set; } = "";
}

public sealed class ScheduleSettings
{
    public bool Enabled { get; set; }
    public List<ScheduleRule> Rules { get; set; } = new();
}

public sealed class TemperatureSettings
{
    public bool Enabled { get; set; }

    /// <summary>Identificador do sensor escolhido. Vazio = o primeiro sensor de CPU (ou o primeiro que existir).</summary>
    public string SensorId { get; set; } = "";

    public int ColdTemp { get; set; } = 40;
    public int HotTemp { get; set; } = 80;
    public string ColdColor { get; set; } = "#0040FF";
    public string HotColor { get; set; } = "#FF0000";

    /// <summary>Peça que mostra a temperatura. Vazio = todas.</summary>
    public string TargetDevice { get; set; } = "";
    public string TargetDeviceId { get; set; } = "";

    /// <summary>Zona da peça. Vazio = a peça inteira.</summary>
    public string TargetZone { get; set; } = "";
}

/// <summary>Configurações do Abaddon RGB, salvas em %APPDATA%\AbaddonRGB\config.json.</summary>
public sealed class AppConfig
{
    private const int CurrentConfigVersion = 3;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6742;

    /// <summary>Cor geral atual, em hexadecimal (#RRGGBB).</summary>
    public string Color { get; set; } = "#3A6EA5";

    /// <summary>Brilho de 0 a 100.</summary>
    public int Brightness { get; set; } = 100;

    /// <summary>Cores próprias de peças ou zonas, por cima da cor geral.</summary>
    public List<TargetColor> Overrides { get; set; } = new();

    /// <summary>Peças que o Abaddon RGB não deve tocar.</summary>
    public List<string> DisabledDevices { get; set; } = new();

    /// <summary>IDs persistentes das peças que não devem receber comandos.</summary>
    public List<string> DisabledDeviceIds { get; set; } = new();

    /// <summary>Nomes dados às peças e zonas (só para exibição).</summary>
    public List<Nickname> Nicknames { get; set; } = new();

    /// <summary>Usa os drivers próprios (sem o OpenRGB) nas peças que já têm driver. Começa desligado.</summary>
    public bool UseNativeDrivers { get; set; }

    /// <summary>Efeito atual (Estático = cor parada).</summary>
    public LightEffect Effect { get; set; } = LightEffect.Static;

    /// <summary>Velocidade do efeito, de 1 (lento) a 10 (rápido).</summary>
    public int EffectSpeed { get; set; } = 5;

    /// <summary>
    /// ID de cada aparelho USB ("usb:046D:C33C") como o OpenRGB o via: quando o motor nativo
    /// assume a peça, ela herda as cores próprias e os nomes já configurados.
    /// </summary>
    public Dictionary<string, string> HardwareIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>O nome que aparece na tela: o apelido, se houver, ou o nome original.</summary>
    public string DisplayName(string device, string zone, string stableId)
    {
        var nickname = Nicknames.FirstOrDefault(n => n.Matches(device, zone, stableId));
        return nickname is not null && !string.IsNullOrWhiteSpace(nickname.Name)
            ? nickname.Name
            : zone.Length > 0 ? zone : device;
    }

    /// <summary>Reaplica as cores assim que o programa conecta.</summary>
    public bool ApplyOnStart { get; set; } = true;

    /// <summary>Procura e instala novas versões do GitHub sozinho.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Modo de segurança: detecta as peças, mas não envia nada para elas.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Corrige a mistura das cores para o jeito que o olho enxerga os LEDs (gama 2,2).</summary>
    public bool GammaCorrection { get; set; } = true;

    public List<Profile> Profiles { get; set; } = new()
    {
        new Profile { Name = "Dia", Color = "#3A6EA5", Brightness = 100 },
        new Profile { Name = "Noite", Color = "#FF6A00", Brightness = 25 },
        new Profile { Name = "Apagado", Color = "#000000", Brightness = 0 },
    };

    public ScheduleSettings Schedule { get; set; } = new();
    public TemperatureSettings Temperature { get; set; } = new();

    /// <summary>
    /// Correções de ordem de cor por zona. Começa vazia.
    /// Exemplo: { "Device": "ASRock", "Zone": "Addressable", "Order": "GRB" }.
    /// </summary>
    public List<ZoneCorrection> Corrections { get; set; } = new();

    /// <summary>Versão do formato do arquivo, para migrar configurações antigas.</summary>
    public int ConfigVersion { get; set; } = CurrentConfigVersion;

    /// <summary>Só para testes: troca a pasta das configurações.</summary>
    [JsonIgnore]
    public static string? FolderOverride { get; set; }

    [JsonIgnore]
    public static string Folder => FolderOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AbaddonRGB");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Folder, "config.json");

    [JsonIgnore]
    public static string LogFolder => Path.Combine(Folder, "logs");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Trava das configurações. Quem altera listas (cores próprias, correções, peças desativadas...)
    /// ou lê várias opções que precisam combinar entre si segura esta trava.
    /// Regra: ela é a última trava. Com ela segura, nunca se pede outra (do controlador ou de um motor),
    /// então pode ser pega a partir de qualquer lugar sem risco de travamento cruzado.
    /// </summary>
    [JsonIgnore]
    public object SyncRoot { get; } = new();

    /// <summary>Cópia do último config.json que abriu sem erro.</summary>
    [JsonIgnore]
    public static string BackupPath => FilePath + ".bak";

    /// <summary>Cópia anterior à <see cref="BackupPath"/> (duas versões recuperáveis).</summary>
    [JsonIgnore]
    public static string OlderBackupPath => FilePath + ".bak2";

    /// <summary>Mensagem para mostrar na tela se o arquivo estava estragado ao abrir (ou null).</summary>
    [JsonIgnore]
    public string? LoadWarning { get; private set; }

    /// <summary>Erro do último salvamento (null se deu certo).</summary>
    [JsonIgnore]
    public string? LastSaveError { get; private set; }

    public static AppConfig Load()
    {
        if (TryRead(FilePath, out var config))
        {
            // Abriu certo: esta passa a ser a cópia de segurança.
            try { File.Copy(FilePath, BackupPath, overwrite: true); } catch { }
            return config!;
        }

        string? warning = null;
        if (File.Exists(FilePath))
        {
            // Arquivo estragado: guarda para análise, sem sobrescrever a cópia boa.
            try { File.Copy(FilePath, FilePath + ".corrompido", overwrite: true); } catch { }

            foreach (var path in new[] { BackupPath, OlderBackupPath })
            {
                if (!TryRead(path, out var backup)) continue;
                backup!.LoadWarning = "As configurações estavam estragadas. Recuperei a última cópia boa.";
                try { backup.Save(); } catch { }
                return backup;
            }

            warning = "As configurações estavam estragadas e não havia cópia boa. Comecei do padrão " +
                      "(o arquivo antigo ficou como config.json.corrompido).";
        }

        var fresh = new AppConfig { LoadWarning = warning };
        try { fresh.Save(); } catch { }
        return fresh;
    }

    private static bool TryRead(string path, out AppConfig? result)
    {
        result = null;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
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
                    result = config;
                    return true;
                }
            }
        }
        catch
        {
            // Arquivo ilegível: quem chamou decide o que fazer.
        }

        return false;
    }

    /// <summary>
    /// Depois de cada gravação: a cópia boa anterior vira .bak2 e o arquivo recém-gravado vira .bak.
    /// Assim a recuperação devolve o último salvamento, não o estado de quando o programa abriu.
    /// </summary>
    private static void RotateBackups()
    {
        try
        {
            if (File.Exists(BackupPath)) File.Copy(BackupPath, OlderBackupPath, overwrite: true);
            File.Copy(FilePath, BackupPath, overwrite: true);
        }
        catch
        {
            // Cópia de segurança é bônus: não falha o salvamento por causa dela.
        }
    }

    /// <summary>Salva sem lançar erro; devolve false (e guarda o motivo em LastSaveError) se falhou.</summary>
    public bool TrySave()
    {
        try
        {
            Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Save()
    {
        lock (SyncRoot)
        {
            Normalize();
            Directory.CreateDirectory(Folder);
            // Nunca deixe um config.json parcialmente gravado após falha ou desligamento.
            var temp = FilePath + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(temp, FilePath, overwrite: true);
                LastSaveError = null;
                RotateBackups();
            }
            catch (Exception ex)
            {
                LastSaveError = ex.Message;
                throw;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }

    public Profile? FindProfile(string? name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool IsDisabled(string deviceName, string stableId = "") =>
        (!string.IsNullOrWhiteSpace(stableId) &&
         DisabledDeviceIds.Any(id => string.Equals(id, stableId, StringComparison.OrdinalIgnoreCase))) ||
        DisabledDevices.Any(d => string.Equals(d, deviceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Associa configurações antigas (baseadas no nome) à identidade física.
    /// Só deve ser chamada quando há exatamente uma peça com esse nome.
    /// </summary>
    public bool BindDeviceIdentity(string name, string stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId)) return false;
        var changed = false;

        foreach (var target in Overrides.Concat(Profiles.SelectMany(p => p.Overrides)))
        {
            if (!string.IsNullOrWhiteSpace(target.DeviceId) ||
                !string.Equals(target.Device, name, StringComparison.OrdinalIgnoreCase)) continue;
            target.DeviceId = stableId;
            changed = true;
        }

        foreach (var correction in Corrections)
        {
            // Correções com nome parcial são regras intencionais para vários dispositivos.
            if (!string.IsNullOrWhiteSpace(correction.DeviceId) ||
                !string.Equals(correction.Device, name, StringComparison.OrdinalIgnoreCase)) continue;
            correction.DeviceId = stableId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(Temperature.TargetDeviceId) &&
            string.Equals(Temperature.TargetDevice, name, StringComparison.OrdinalIgnoreCase))
        {
            Temperature.TargetDeviceId = stableId;
            changed = true;
        }

        if (DisabledDevices.RemoveAll(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            if (!DisabledDeviceIds.Contains(stableId, StringComparer.OrdinalIgnoreCase))
                DisabledDeviceIds.Add(stableId);
            changed = true;
        }

        return changed;
    }

    private void Normalize()
    {
        Brightness = Math.Clamp(Brightness, 0, 100);
        if (Port is < 1 or > 65535) Port = 6742;
        if (string.IsNullOrWhiteSpace(Host)) Host = "127.0.0.1";
        if (!RgbColor.TryParse(Color, out _)) Color = "#3A6EA5";

        Corrections ??= new List<ZoneCorrection>();
        Overrides ??= new List<TargetColor>();
        DisabledDevices ??= new List<string>();
        DisabledDeviceIds ??= new List<string>();
        Nicknames ??= new List<Nickname>();
        EffectSpeed = Math.Clamp(EffectSpeed, Effects.MinSpeed, Effects.MaxSpeed);
        if (!Enum.IsDefined(Effect)) Effect = LightEffect.Static;
        HardwareIds = HardwareIds is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(HardwareIds, StringComparer.OrdinalIgnoreCase);
        Nicknames.RemoveAll(n => n is null || string.IsNullOrWhiteSpace(n.Name) ||
                                 (string.IsNullOrWhiteSpace(n.Device) && string.IsNullOrWhiteSpace(n.DeviceId)));
        Profiles ??= new List<Profile>();
        Schedule ??= new ScheduleSettings();
        Schedule.Rules ??= new List<ScheduleRule>();
        Temperature ??= new TemperatureSettings();

        Overrides.RemoveAll(o => o is null || string.IsNullOrWhiteSpace(o.Device) || !RgbColor.TryParse(o.Color, out _));
        Profiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Name));
        foreach (var profile in Profiles)
        {
            profile.EffectSpeed = Math.Clamp(profile.EffectSpeed, Effects.MinSpeed, Effects.MaxSpeed);
            if (!Enum.IsDefined(profile.Effect)) profile.Effect = LightEffect.Static;
            profile.Brightness = Math.Clamp(profile.Brightness, 0, 100);
            if (!RgbColor.TryParse(profile.Color, out _)) profile.Color = "#3A6EA5";
            profile.Overrides ??= new List<TargetColor>();
        }

        Temperature.ColdTemp = Math.Clamp(Temperature.ColdTemp, 0, 119);
        Temperature.HotTemp = Math.Clamp(Temperature.HotTemp, Temperature.ColdTemp + 1, 120);
        Temperature.SensorId ??= "";
        Temperature.TargetDevice ??= "";
        Temperature.TargetDeviceId ??= "";
        Temperature.TargetZone ??= "";
    }

    /// <summary>Ajusta configurações criadas por versões antigas. Retorna true se mudou algo.</summary>
    private bool Migrate()
    {
        if (ConfigVersion >= CurrentConfigVersion) return false;

        if (ConfigVersion < 2)
        {
            // v0.1 vinha com a correção GRB do cooler ASRock, que se mostrou desnecessária.
            Corrections.RemoveAll(c =>
                string.Equals(c.Device, "ASRock", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Zone, "Addressable", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Order, "GRB", StringComparison.OrdinalIgnoreCase));
        }

        ConfigVersion = CurrentConfigVersion;
        return true;
    }

    public static bool TryParseHex(string? hex, out byte r, out byte g, out byte b)
    {
        var ok = RgbColor.TryParse(hex, out var color);
        (r, g, b) = (color.R, color.G, color.B);
        return ok;
    }

    public static string ToHex(byte r, byte g, byte b) => new RgbColor(r, g, b).ToHex();
}

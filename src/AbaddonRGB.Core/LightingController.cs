using AbaddonRGB.Core.Engines;

namespace AbaddonRGB.Core;

/// <summary>Resultado da aplicação de cor em uma peça.</summary>
public sealed record DeviceResult(string Name, string Engine, bool Success, string? Note = null, bool Skipped = false);

/// <summary>Situação de um motor, para a tela de diagnóstico.</summary>
public sealed record EngineStatus(string Id, string Name, bool Connected, int Devices, string? Error);

/// <summary>
/// O "maestro": junta os motores, as configurações e as automações,
/// e decide que cor cada LED recebe.
/// </summary>
public sealed class LightingController : IDisposable
{
    private readonly object _lock = new();
    private readonly List<ILightingEngine> _engines;
    private readonly Dictionary<string, string?> _errors = new();
    private List<LightDevice> _devices = new();

    public LightingController(AppConfig config, IEnumerable<ILightingEngine> engines, AppLog log)
    {
        Config = config;
        Log = log;
        _engines = engines.ToList();
    }

    public AppConfig Config { get; }
    public AppLog Log { get; }

    public bool IsConnected
    {
        get { lock (_lock) return _engines.Any(e => e.IsConnected); }
    }

    /// <summary>As peças vistas na última detecção.</summary>
    public IReadOnlyList<LightDevice> Devices
    {
        get { lock (_lock) return _devices.ToArray(); }
    }

    /// <summary>Tenta conectar todos os motores. Retorna true se pelo menos um conectou.</summary>
    public bool Connect(out string? error)
    {
        lock (_lock)
        {
            error = null;
            foreach (var engine in _engines)
            {
                if (engine.IsConnected)
                {
                    _errors[engine.Id] = null;
                    continue;
                }

                bool ok;
                string? engineError;
                try { ok = engine.TryConnect(out engineError); }
                catch (Exception ex) { ok = false; engineError = ex.Message; }
                var changed = !_errors.TryGetValue(engine.Id, out var previous) || previous != engineError;
                _errors[engine.Id] = ok ? null : engineError;

                if (ok) Log.Info($"Motor {engine.DisplayName}: conectado.");
                else if (changed && engine.Id == OpenRgbEngine.EngineId)
                    Log.Warn($"Motor {engine.DisplayName}: {engineError}");

                if (!ok && engine.Id == OpenRgbEngine.EngineId) error = engineError;
            }

            var any = _engines.Any(e => e.IsConnected);
            if (any) RefreshDevicesLocked();
            return any;
        }
    }

    /// <summary>Somente leitura: pergunta de novo aos motores quais peças existem.</summary>
    public IReadOnlyList<LightDevice> RefreshDevices()
    {
        lock (_lock)
        {
            RefreshDevicesLocked();
            return _devices.ToArray();
        }
    }

    private void RefreshDevicesLocked()
    {
        var found = new List<LightDevice>();
        foreach (var engine in _engines.Where(e => e.IsConnected))
        {
            try
            {
                found.AddRange(engine.GetDevices());
            }
            catch (Exception ex)
            {
                _errors[engine.Id] = ex.Message;
                Log.Error($"Motor {engine.DisplayName}: falha ao listar as peças ({ex.Message}).");
            }
        }

        // Um único motor pode escrever em cada dispositivo físico. Quando ambos
        // reconhecem o mesmo ID, o motor nativo tem prioridade.
        var unique = found.GroupBy(d => string.IsNullOrWhiteSpace(d.StableId)
                ? $"{d.EngineId}:{d.Index}" : d.StableId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(d => d.EngineId == NativeEngine.EngineId ? 0 : 1).First())
            .ToList();

        if (unique.Count != found.Count)
            Log.Info($"{found.Count - unique.Count} detecção(ões) duplicada(s): controle atribuído a um único motor.");

        // Converte entradas antigas por nome sem arriscar associar duas peças homônimas.
        var migrated = false;
        foreach (var group in unique.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            if (group.Count() == 1)
                migrated |= Config.BindDeviceIdentity(group.Key, group.First().StableId);
        if (migrated)
        {
            try { Config.Save(); }
            catch (Exception ex) { Log.Warn("Não foi possível salvar a migração dos IDs: " + ex.Message); }
        }

        static string Signature(LightDevice d) =>
            $"{d.EngineId}|{d.Index}|{d.StableId}|{d.Name}|{d.LedCount}|{d.Zones.Count}";
        if (!unique.Select(Signature).SequenceEqual(_devices.Select(Signature)))
            Log.Info("Peças detectadas: " + (unique.Count == 0 ? "nenhuma" :
                string.Join("; ", unique.Select(d => $"{d.Name} [{d.LedCount} LEDs, {d.StableId}]"))));

        _devices = unique;
    }

    public IReadOnlyList<EngineStatus> GetEngineStatus()
    {
        lock (_lock)
        {
            return _engines.Select(e => new EngineStatus(
                e.Id,
                e.DisplayName,
                e.IsConnected,
                _devices.Count(d => d.EngineId == e.Id),
                _errors.TryGetValue(e.Id, out var error) ? error : null)).ToArray();
        }
    }

    /// <summary>
    /// Aplica as cores atuais (cor geral + cores próprias + correções).
    /// </summary>
    /// <param name="temperatureColor">Cor vinda da automação de temperatura, se estiver ativa.</param>
    /// <param name="onlyTemperatureTarget">Envia só para a peça que mostra a temperatura (economiza comandos).</param>
    public IReadOnlyList<DeviceResult> Apply(RgbColor? temperatureColor = null, bool onlyTemperatureTarget = false)
    {
        lock (_lock)
        {
            if (!_engines.Any(e => e.IsConnected))
                throw new InvalidOperationException("Nenhum motor conectado.");

            if (_devices.Count == 0) RefreshDevicesLocked();

            var baseColor = RgbColor.ParseOr(Config.Color, new RgbColor(58, 110, 165));
            IReadOnlyList<TargetColor> overrides = Config.Overrides.ToArray();
            TargetColor? top = null;

            var targetDevice = Config.Temperature.TargetDevice;
            var targetDeviceId = Config.Temperature.TargetDeviceId;
            var temperatureEverywhere = string.IsNullOrWhiteSpace(targetDevice) &&
                                        string.IsNullOrWhiteSpace(targetDeviceId);
            if (temperatureColor is not null)
            {
                if (temperatureEverywhere)
                {
                    baseColor = temperatureColor.Value;
                    overrides = Array.Empty<TargetColor>();
                }
                else
                {
                    top = new TargetColor
                    {
                        Device = targetDevice,
                        DeviceId = targetDeviceId,
                        Zone = Config.Temperature.TargetZone,
                        Color = temperatureColor.Value.ToHex(),
                    };
                }
            }

            var corrections = Config.Corrections.ToArray();
            var results = new List<DeviceResult>();

            foreach (var device in _devices.ToArray())
            {
                var engine = _engines.FirstOrDefault(e => e.Id == device.EngineId);
                if (engine is null) continue;

                if (onlyTemperatureTarget && !temperatureEverywhere &&
                    !(string.IsNullOrWhiteSpace(targetDeviceId)
                        ? string.Equals(device.Name, targetDevice, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(device.StableId, targetDeviceId, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (Config.IsDisabled(device.Name, device.StableId))
                {
                    results.Add(new DeviceResult(device.Name, engine.DisplayName, false, "não controlada", Skipped: true));
                    continue;
                }

                var colors = ColorComposer.Build(device, baseColor, Config.Brightness, overrides, corrections, top);
                if (colors.Length == 0)
                {
                    results.Add(new DeviceResult(device.Name, engine.DisplayName, false, "sem LEDs", Skipped: true));
                    continue;
                }

                if (Config.ReadOnly)
                {
                    results.Add(new DeviceResult(device.Name, engine.DisplayName, false, "somente leitura", Skipped: true));
                    continue;
                }

                try
                {
                    engine.SetColors(device, colors);
                    results.Add(new DeviceResult(device.Name, engine.DisplayName, true));
                }
                catch (Exception ex)
                {
                    results.Add(new DeviceResult(device.Name, engine.DisplayName, false, ex.Message));
                    Log.Error($"{device.Name}: {ex.Message}");
                    if (!engine.IsConnected)
                    {
                        // O motor caiu: esquece as peças dele para redescobrir na próxima conexão.
                        _devices = _devices.Where(d => d.EngineId != engine.Id).ToList();
                        Log.Warn($"Motor {engine.DisplayName}: conexão perdida.");
                    }
                }
            }

            return results;
        }
    }

    /// <summary>Copia um perfil salvo para as cores atuais. Não envia nada: chame Apply depois.</summary>
    public bool LoadProfile(string? name)
    {
        lock (_lock)
        {
            var profile = Config.FindProfile(name);
            if (profile is null) return false;

            Config.Color = profile.Color;
            Config.Brightness = profile.Brightness;
            Config.Overrides = profile.Overrides.Select(o => o.Clone()).ToList();
            try { Config.Save(); } catch { }
            Log.Info($"Perfil \"{profile.Name}\" carregado ({profile.Color}, brilho {profile.Brightness}%).");
            return true;
        }
    }

    /// <summary>Salva as cores atuais como um perfil (cria ou substitui).</summary>
    public Profile SaveProfile(string name)
    {
        lock (_lock)
        {
            name = name.Trim();
            var profile = Config.FindProfile(name);
            if (profile is null)
            {
                profile = new Profile { Name = name };
                Config.Profiles.Add(profile);
            }

            profile.Color = Config.Color;
            profile.Brightness = Config.Brightness;
            profile.Overrides = Config.Overrides.Select(o => o.Clone()).ToList();
            try { Config.Save(); } catch { }
            Log.Info($"Perfil \"{profile.Name}\" salvo.");
            return profile;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var engine in _engines)
                try { engine.Dispose(); } catch { }
        }
    }
}

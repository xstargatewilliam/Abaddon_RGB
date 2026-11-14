using AbaddonRGB.Core.Engines;

namespace AbaddonRGB.Core;

/// <summary>Em que pé está o envio de cor a uma peça.</summary>
public enum SendState
{
    /// <summary>A peça recebeu o comando.</summary>
    Sent,

    /// <summary>A peça já mostrava esta cor: nada a enviar.</summary>
    Unchanged,

    /// <summary>Ainda não enviado (limite de envios por segundo); vai na próxima rodada.</summary>
    Pending,

    /// <summary>De propósito ficou de fora (somente leitura, peça desativada, sem LEDs).</summary>
    Skipped,

    /// <summary>O envio falhou.</summary>
    Failed,
}

/// <summary>Resultado da aplicação de cor em uma peça.</summary>
public sealed record DeviceResult(string Name, string Engine, bool Success, string? Note = null, bool Skipped = false,
    SendState State = SendState.Sent);

/// <summary>Cores calculadas para uma peça (Colors) ou o motivo de ela ficar de fora (Skip).</summary>
public sealed record RenderedDevice(LightDevice Device, string EngineName, RgbColor[]? Colors, DeviceResult? Skip);

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
    private bool _nativeTakeoverLogged;
    private readonly System.Diagnostics.Stopwatch _effectClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Dictionary<string, RgbColor[]> _lastSent = new();
    private readonly HashSet<string> _failing = new();
    private readonly Dictionary<string, object> _engineLocks = new();
    private int _devicesVersion;

    /// <summary>
    /// Muda quando as peças mudam ou um motor reconecta (as peças podem ter perdido as cores).
    /// Quem guarda o que já foi enviado deve reenviar tudo.
    /// </summary>
    public int DevicesVersion => Volatile.Read(ref _devicesVersion);

    private object EngineLock(string engineId)
    {
        lock (_engineLocks)
        {
            if (!_engineLocks.TryGetValue(engineId, out var gate))
                _engineLocks[engineId] = gate = new object();
            return gate;
        }
    }

    /// <summary>Motores que existem (conectados ou não), para quem envia quadros.</summary>
    public IReadOnlyList<string> EngineIds
    {
        get { lock (_lock) return _engines.Select(e => e.Id).ToArray(); }
    }

    public bool IsEngineConnected(string engineId)
    {
        lock (_lock) return _engines.Any(e => e.Id == engineId && e.IsConnected);
    }

    /// <summary>Cor do efeito no início do PC agora (para a prévia na tela).</summary>
    public RgbColor EffectPreviewColor()
    {
        var baseColor = RgbColor.ParseOr(Config.Color, new RgbColor(58, 110, 165));
        if (!Effects.IsAnimated(Config.Effect)) return baseColor;
        return Effects.ColorAt(Config.Effect, Config.EffectSpeed, _effectClock.Elapsed.TotalSeconds, baseColor, 0.05, 0);
    }

    public LightingController(AppConfig config, IEnumerable<ILightingEngine> engines, AppLog log)
    {
        Config = config;
        Log = log;
        _engines = engines.ToList();
    }

    public AppConfig Config { get; }
    public AppLog Log { get; }

    /// <summary>
    /// Algum motor conectado e útil. O motor nativo ligado sem nenhum aparelho não conta:
    /// sem isso, com o OpenRGB fora do ar o programa achava que estava tudo conectado.
    /// </summary>
    public bool IsConnected
    {
        get { lock (_lock) return _engines.Any(Useful); }
    }

    private static bool Useful(ILightingEngine engine) =>
        engine.IsConnected && (engine is not NativeEngine native || native.HasDevices);

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
                if (engine.Id == NativeEngine.EngineId && !Config.UseNativeDrivers) continue;

                if (engine.IsConnected)
                {
                    _errors[engine.Id] = null;
                    continue;
                }

                bool ok;
                string? engineError;
                try
                {
                    lock (EngineLock(engine.Id)) ok = engine.TryConnect(out engineError);
                }
                catch (Exception ex) { ok = false; engineError = ex.Message; }
                var changed = !_errors.TryGetValue(engine.Id, out var previous) || previous != engineError;
                _errors[engine.Id] = ok ? null : engineError;

                if (ok)
                {
                    Log.Info($"Motor {engine.DisplayName}: conectado.");
                    Interlocked.Increment(ref _devicesVersion);
                }
                else if (changed && engine.Id == OpenRgbEngine.EngineId)
                    Log.Warn($"Motor {engine.DisplayName}: {engineError}");

                if (!ok && engine.Id == OpenRgbEngine.EngineId) error = engineError;
            }

            if (_engines.Any(e => e.IsConnected)) RefreshDevicesLocked();
            return _engines.Any(Useful);
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
                // A detecção pode abrir e fechar aparelhos: mesma trava do envio.
                lock (EngineLock(engine.Id)) found.AddRange(engine.GetDevices());
            }
            catch (Exception ex)
            {
                _errors[engine.Id] = ex.Message;
                Log.Error($"Motor {engine.DisplayName}: falha ao listar as peças ({ex.Message}).");
            }
        }

        // Lembra o ID que o OpenRGB dá a cada aparelho físico USB, para o motor nativo herdar.
        var idsChanged = false;
        foreach (var device in found.Where(d => d.EngineId != NativeEngine.EngineId && d.PhysicalKey.Length > 0))
        {
            if (Config.HardwareIds.TryGetValue(device.PhysicalKey, out var known) && known == device.StableId) continue;
            Config.HardwareIds[device.PhysicalKey] = device.StableId;
            idsChanged = true;
        }

        // A mesma peça física vista pelo motor nativo e pelo OpenRGB: fica só a do nativo, com o ID antigo.
        // Compara pela peça física (interface + instância do Windows), não pelo modelo:
        // com dois teclados iguais, só sai do OpenRGB aquele que o nativo realmente abriu.
        var nativeKeys = found.Where(d => d.EngineId == NativeEngine.EngineId && d.PhysicalKey.Length > 0)
            .Select(d => d.PhysicalKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (nativeKeys.Count > 0)
        {
            var before = found.Count;
            found = found
                .Where(d => d.EngineId == NativeEngine.EngineId || !nativeKeys.Contains(d.PhysicalKey))
                .Select(d => d.EngineId == NativeEngine.EngineId && InheritedId(d) is { } inherited
                    ? d with { StableId = inherited }
                    : d)
                .ToList();
            if (found.Count != before && !_nativeTakeoverLogged)
            {
                _nativeTakeoverLogged = true;
                Log.Info($"Motor nativo assumiu {before - found.Count} peça(s) que o OpenRGB também via.");
            }
        }

        if (idsChanged)
        {
            SaveConfig();
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
                string.Join("; ", unique.Select(d => $"{d.Name} [{d.LedCount} LEDs" +
                    (string.IsNullOrWhiteSpace(d.Location) ? "" : $", {d.Location}") + $", {d.StableId}]"))));

        // Só quando a lista mudou: as peças novas (ou reconectadas) precisam receber tudo de novo.
        static string ListSignature(IEnumerable<LightDevice> list) =>
            string.Join("|", list.Select(d => $"{d.EngineId}:{d.Index}:{d.StableId}:{d.LedCount}"));
        if (ListSignature(unique) != ListSignature(_devices))
        {
            foreach (var gone in _devices.Where(d => !unique.Any(u => u.StableId == d.StableId)))
                Log.Warn($"{gone.Name} não foi mais encontrada. Se for a placa-mãe, desligue o PC da tomada por 30 s para reiniciar o controlador de LED.");
            _lastSent.Clear();
            Interlocked.Increment(ref _devicesVersion);
        }

        _devices = unique;
    }

    /// <summary>
    /// ID que o OpenRGB dava a esta peça. Versões antigas guardavam só o modelo ("usb:046D:C33C");
    /// esse vale apenas se não houver registro de outra peça do mesmo modelo.
    /// </summary>
    private string? InheritedId(LightDevice device)
    {
        if (device.PhysicalKey.Length > 0 && Config.HardwareIds.TryGetValue(device.PhysicalKey, out var exact))
            return exact;
        if (device.HardwareKey.Length > 0 && Config.HardwareIds.TryGetValue(device.HardwareKey, out var byModel) &&
            Config.HardwareIds.Keys.Count(k => k.StartsWith(device.HardwareKey + ":", StringComparison.OrdinalIgnoreCase)) <= 1)
            return byModel;
        return null;
    }

    /// <summary>Liga ou desliga o motor nativo e redetecta as peças.</summary>
    public void SetNativeEnabled(bool enabled)
    {
        lock (_lock)
        {
            Config.UseNativeDrivers = enabled;
            _nativeTakeoverLogged = false;
            var native = _engines.OfType<NativeEngine>().FirstOrDefault();
            if (native is null) return;

            if (enabled)
            {
                try
                {
                    bool connected;
                string? error;
                lock (EngineLock(native.Id)) connected = native.TryConnect(out error);
                if (connected) Log.Info("Motor nativo: ligado.");
                    else Log.Warn("Motor nativo: " + error);
                }
                catch (Exception ex)
                {
                    Log.Error("Motor nativo: " + ex.Message);
                }
            }
            else
            {
                // Mesma trava do envio: nenhum quadro é escrito num teclado sendo fechado.
                lock (EngineLock(native.Id)) native.Disconnect();
                Log.Info("Motor nativo: desligado. As peças voltam para o OpenRGB.");
            }

            RefreshDevicesLocked();
        }
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
    /// <param name="effectFrame">
    /// Quadro de efeito animado: só envia às peças cuja cor mudou desde o último envio,
    /// e não repete no registro o mesmo erro a cada quadro.
    /// </param>
    public IReadOnlyList<DeviceResult> Apply(RgbColor? temperatureColor = null, bool onlyTemperatureTarget = false,
        bool effectFrame = false)
    {
        lock (_lock)
        {
            if (!_engines.Any(e => e.IsConnected))
                throw new InvalidOperationException("Nenhum motor conectado.");

            if (_devices.Count == 0) RefreshDevicesLocked();

            var results = new List<DeviceResult>();
            foreach (var item in RenderLocked(temperatureColor, onlyTemperatureTarget, engineId: null))
            {
                if (item.Skip is not null)
                {
                    results.Add(item.Skip);
                    continue;
                }

                var device = item.Device;
                var colors = item.Colors!;
                var cacheKey = CacheKey(device);
                if (effectFrame && _lastSent.TryGetValue(cacheKey, out var previous) && previous.AsSpan().SequenceEqual(colors))
                {
                    results.Add(new DeviceResult(device.Name, item.EngineName, true, State: SendState.Unchanged));
                    continue;
                }

                results.Add(SendLocked(device, colors, quietRepeats: effectFrame));
            }

            return results;
        }
    }

    /// <summary>
    /// Calcula a cor de cada LED (sem enviar nada). Usado por quem envia os quadros:
    /// cada motor envia no seu ritmo, sem esperar os outros.
    /// </summary>
    /// <param name="engineId">Só as peças deste motor (null = todas).</param>
    public IReadOnlyList<RenderedDevice> Render(string? engineId, RgbColor? temperatureColor)
    {
        lock (_lock)
        {
            return RenderLocked(temperatureColor, onlyTemperatureTarget: false, engineId);
        }
    }

    /// <summary>Envia as cores a uma peça (pode ser chamado de vários processos ao mesmo tempo).</summary>
    public DeviceResult Send(LightDevice device, RgbColor[] colors)
    {
        ILightingEngine? engine;
        lock (_lock) engine = _engines.FirstOrDefault(e => e.Id == device.EngineId);
        if (engine is null) return new DeviceResult(device.Name, device.EngineId, false, "motor desconhecido");

        try
        {
            lock (EngineLock(engine.Id))
            {
                // Confere de novo logo antes de escrever: o quadro foi calculado antes, e nesse meio-tempo
                // a pessoa pode ter ligado o somente leitura, desativado a peça ou a peça pode ter sumido.
                var refusal = WriteRefusal(device);
                if (refusal is not null)
                    return new DeviceResult(device.Name, engine.DisplayName, false, refusal, Skipped: true, State: SendState.Skipped);
                engine.SetColors(device, colors);
            }
            lock (_lock)
            {
                _lastSent[CacheKey(device)] = colors;
                if (_failing.Remove(CacheKey(device))) Log.Info($"{device.Name}: voltou a responder.");
            }

            return new DeviceResult(device.Name, engine.DisplayName, true);
        }
        catch (Exception ex)
        {
            lock (_lock) return Failed(engine, device, ex, quietRepeats: true);
        }
    }

    /// <summary>Salva a configuração; se falhar, registra o motivo em vez de engolir o erro.</summary>
    public bool SaveConfig()
    {
        if (Config.TrySave()) return true;
        Log.Warn("Não foi possível salvar as configurações: " + Config.LastSaveError);
        return false;
    }

    private string? WriteRefusal(LightDevice device)
    {
        lock (_lock)
        {
            if (Config.ReadOnly) return "somente leitura";
            if (Config.IsDisabled(device.Name, device.StableId)) return "não controlada";
            if (!_devices.Any(d => d.EngineId == device.EngineId && d.Index == device.Index && d.StableId == device.StableId))
                return "peça não está mais conectada";
            return null;
        }
    }

    private static string CacheKey(LightDevice device) => device.EngineId + "|" + device.StableId + "|" + device.Index;

    private DeviceResult SendLocked(LightDevice device, RgbColor[] colors, bool quietRepeats)
    {
        var engine = _engines.First(e => e.Id == device.EngineId);
        try
        {
            lock (EngineLock(engine.Id)) engine.SetColors(device, colors);
            _lastSent[CacheKey(device)] = colors;
            if (_failing.Remove(CacheKey(device))) Log.Info($"{device.Name}: voltou a responder.");
            return new DeviceResult(device.Name, engine.DisplayName, true);
        }
        catch (Exception ex)
        {
            return Failed(engine, device, ex, quietRepeats);
        }
    }

    private DeviceResult Failed(ILightingEngine engine, LightDevice device, Exception ex, bool quietRepeats)
    {
        var key = CacheKey(device);
        _lastSent.Remove(key);
        if (_failing.Add(key) || !quietRepeats) Log.Error($"{device.Name}: {ex.Message}");
        if (!engine.IsConnected && _devices.Any(d => d.EngineId == engine.Id))
        {
            // O motor caiu: esquece as peças dele para redescobrir na próxima conexão.
            _devices = _devices.Where(d => d.EngineId != engine.Id).ToList();
            Interlocked.Increment(ref _devicesVersion);
            Log.Warn($"Motor {engine.DisplayName}: conexão perdida.");
        }

        return new DeviceResult(device.Name, engine.DisplayName, false, ex.Message, State: SendState.Failed);
    }

    private List<RenderedDevice> RenderLocked(RgbColor? temperatureColor, bool onlyTemperatureTarget, string? engineId)
    {
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
        var output = new List<RenderedDevice>();

        // Efeito animado: a cor geral passa a variar LED a LED. Temperatura em tudo tem prioridade.
        var effect = Config.Effect;
        var animate = Effects.IsAnimated(effect) && !(temperatureColor is not null && temperatureEverywhere);
        var seconds = _effectClock.Elapsed.TotalSeconds;
        var devices = _devices.ToArray();
        var totalLeds = Math.Max(devices.Sum(d => Math.Max(d.LedCount, 1)), 1);
        var ledsBefore = 0;

        for (var deviceSlot = 0; deviceSlot < devices.Length; deviceSlot++)
        {
            var device = devices[deviceSlot];
            var firstLed = ledsBefore;
            ledsBefore += Math.Max(device.LedCount, 1);

            var engine = _engines.FirstOrDefault(e => e.Id == device.EngineId);
            if (engine is null) continue;
            if (engineId is not null && engine.Id != engineId) continue;

            if (onlyTemperatureTarget && !temperatureEverywhere &&
                !(string.IsNullOrWhiteSpace(targetDeviceId)
                    ? string.Equals(device.Name, targetDevice, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(device.StableId, targetDeviceId, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (Config.IsDisabled(device.Name, device.StableId))
            {
                output.Add(new RenderedDevice(device, engine.DisplayName, null,
                    new DeviceResult(device.Name, engine.DisplayName, false, "não controlada", Skipped: true, State: SendState.Skipped)));
                continue;
            }

            Func<int, RgbColor>? baseForLed = null;
            if (animate)
            {
                var speed = Config.EffectSpeed;
                var fixedBase = baseColor;
                var slot = deviceSlot;
                // Posição ao longo do PC: as peças em ordem, LED a LED (para a onda atravessar tudo).
                baseForLed = led => Effects.ColorAt(effect, speed, seconds, fixedBase,
                    (firstLed + led + 0.5) / totalLeds, slot * 4096 + led);
            }

            var colors = ColorComposer.Build(device, baseColor, Config.Brightness, overrides, corrections, top,
                gamma: Config.GammaCorrection, baseForLed: baseForLed);
            if (colors.Length == 0)
            {
                output.Add(new RenderedDevice(device, engine.DisplayName, null,
                    new DeviceResult(device.Name, engine.DisplayName, false, "sem LEDs", Skipped: true, State: SendState.Skipped)));
                continue;
            }

            if (Config.ReadOnly)
            {
                output.Add(new RenderedDevice(device, engine.DisplayName, null,
                    new DeviceResult(device.Name, engine.DisplayName, false, "somente leitura", Skipped: true, State: SendState.Skipped)));
                continue;
            }

            output.Add(new RenderedDevice(device, engine.DisplayName, colors, null));
        }

        return output;
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
            Config.Effect = profile.Effect;
            Config.EffectSpeed = profile.EffectSpeed;
            Config.Overrides = profile.Overrides.Select(o => o.Clone()).ToList();
            SaveConfig();
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
            profile.Effect = Config.Effect;
            profile.EffectSpeed = Config.EffectSpeed;
            profile.Overrides = Config.Overrides.Select(o => o.Clone()).ToList();
            SaveConfig();
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

namespace AbaddonRGB.Core.Engines;

/// <summary>
/// Um driver nativo fala direto com uma família de peças, sem o OpenRGB.
/// A Fase 0 só define o contrato; os primeiros drivers (Logitech) entram na Fase 1.
/// </summary>
public interface INativeDriver : IDisposable
{
    string Name { get; }

    /// <summary>Somente leitura: procura as peças que este driver conhece.</summary>
    IReadOnlyList<LightDevice> Detect(string engineId, int firstIndex);

    void SetColors(LightDevice device, IReadOnlyList<RgbColor> colors);
}

/// <summary>Motor dos drivers próprios do Abaddon RGB (falam direto com o hardware, sem o OpenRGB).</summary>
public sealed class NativeEngine : ILightingEngine
{
    public const string EngineId = "native";

    private readonly List<INativeDriver> _drivers;
    private readonly Dictionary<int, INativeDriver> _owners = new();
    private bool _connected;

    public NativeEngine(IEnumerable<INativeDriver>? drivers = null) =>
        _drivers = drivers?.ToList() ?? new List<INativeDriver>();

    public string Id => EngineId;
    public string DisplayName => "Nativo";
    public int DriverCount => _drivers.Count;
    public bool IsConnected => _connected && _drivers.Count > 0;

    public bool TryConnect(out string? error)
    {
        error = _drivers.Count == 0 ? "nenhum driver nativo instalado ainda" : null;
        _connected = _drivers.Count > 0;
        return _connected;
    }

    public IReadOnlyList<LightDevice> GetDevices()
    {
        _owners.Clear();
        var devices = new List<LightDevice>();
        foreach (var driver in _drivers)
        {
            foreach (var detected in driver.Detect(EngineId, devices.Count))
            {
                // Drivers novos devem fornecer um ID baseado em metadados físicos.
                // Mantém um fallback determinístico para drivers que ainda não o fazem.
                var device = string.IsNullOrWhiteSpace(detected.StableId)
                    ? detected with { StableId = DeviceIdentity.Create(detected.Name,
                        ledCount: detected.LedCount, zones: detected.Zones) }
                    : detected;
                _owners[device.Index] = driver;
                devices.Add(device);
            }
        }

        return devices;
    }

    public void SetColors(LightDevice device, IReadOnlyList<RgbColor> colors)
    {
        if (!_owners.TryGetValue(device.Index, out var driver))
            throw new InvalidOperationException("Peça desconhecida pelo motor nativo.");
        driver.SetColors(device, colors);
    }

    /// <summary>Fecha todos os aparelhos abertos. Um novo TryConnect abre de novo.</summary>
    public void Disconnect()
    {
        foreach (var driver in _drivers)
            try { driver.Dispose(); } catch { }
        _owners.Clear();
        _connected = false;
    }

    public void Dispose()
    {
        foreach (var driver in _drivers)
            try { driver.Dispose(); } catch { }
        _connected = false;
    }
}

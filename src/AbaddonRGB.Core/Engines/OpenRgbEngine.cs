using OpenRGB.NET;

namespace AbaddonRGB.Core.Engines;

/// <summary>Motor que usa o servidor SDK do OpenRGB para falar com o hardware.</summary>
public sealed class OpenRgbEngine : ILightingEngine
{
    public const string EngineId = "openrgb";

    private readonly Func<(string Host, int Port)> _endpoint;
    private readonly HashSet<int> _directModeSet = new();
    private OpenRgbClient? _client;

    public OpenRgbEngine(Func<(string Host, int Port)> endpoint) => _endpoint = endpoint;

    public string Id => EngineId;
    public string DisplayName => "OpenRGB";
    public bool IsConnected => _client?.Connected == true;

    public bool TryConnect(out string? error)
    {
        error = null;
        if (IsConnected) return true;

        Disconnect();
        try
        {
            var (host, port) = _endpoint();
            _client = new OpenRgbClient(host, port, name: "Abaddon RGB", autoConnect: true, timeoutMs: 2000);
            return _client.Connected;
        }
        catch (Exception ex)
        {
            Disconnect();
            error = ex.Message;
            return false;
        }
    }

    public IReadOnlyList<LightDevice> GetDevices()
    {
        if (_client is null) return Array.Empty<LightDevice>();
        try
        {
            return _client.GetAllControllerData()
                .Select(d => new LightDevice(
                    EngineId,
                    d.Index,
                    d.Name,
                    d.Zones.Select(z => new LightZone(z.Name, (int)z.LedCount)).ToArray(),
                    d.Leds.Length))
                .ToArray();
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    public void SetColors(LightDevice device, IReadOnlyList<RgbColor> colors)
    {
        if (_client is null || !_client.Connected)
            throw new InvalidOperationException("Não há conexão com o OpenRGB.");
        if (colors.Count == 0) return;

        try
        {
            // Coloca a peça no modo que aceita cor por LED. Só precisa uma vez por conexão.
            if (_directModeSet.Add(device.Index))
                _client.SetCustomMode(device.Index);

            var buffer = new Color[colors.Count];
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = new Color(colors[i].R, colors[i].G, colors[i].B);

            _client.UpdateLeds(device.Index, buffer);
        }
        catch
        {
            if (_client?.Connected != true) Disconnect();
            throw;
        }
    }

    private void Disconnect()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
        _directModeSet.Clear();
    }

    public void Dispose() => Disconnect();
}

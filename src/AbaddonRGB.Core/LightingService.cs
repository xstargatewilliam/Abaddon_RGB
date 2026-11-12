using OpenRGB.NET;

namespace AbaddonRGB.Core;

/// <summary>Resultado da aplicação de cor em um dispositivo.</summary>
public sealed record DeviceResult(string Name, bool Success, string? Error = null, int CorrectedZones = 0);

/// <summary>
/// Conversa com o servidor SDK do OpenRGB e aplica as cores nos dispositivos,
/// já corrigindo a ordem de cores das zonas configuradas.
/// </summary>
public sealed class LightingService : IDisposable
{
    private readonly object _lock = new();
    private OpenRgbClient? _client;

    public bool IsConnected
    {
        get { lock (_lock) return _client?.Connected == true; }
    }

    /// <summary>Tenta conectar ao OpenRGB. Retorna false se o servidor não estiver disponível.</summary>
    public bool TryConnect(string host, int port, out string? error)
    {
        lock (_lock)
        {
            error = null;
            if (_client?.Connected == true)
                return true;

            DisposeClient();
            try
            {
                _client = new OpenRgbClient(host, port, name: "Abaddon RGB", autoConnect: true, timeoutMs: 2000);
                return _client.Connected;
            }
            catch (Exception ex)
            {
                DisposeClient();
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>Lista os nomes dos dispositivos reconhecidos pelo OpenRGB.</summary>
    public IReadOnlyList<string> GetDeviceNames()
    {
        lock (_lock)
        {
            if (_client is null) return Array.Empty<string>();
            try
            {
                return _client.GetAllControllerData().Select(d => d.Name).ToArray();
            }
            catch
            {
                DisposeClient();
                throw;
            }
        }
    }

    /// <summary>
    /// Pinta todos os dispositivos com uma única cor, aplicando brilho e correções de ordem.
    /// </summary>
    public IReadOnlyList<DeviceResult> ApplySolidColor(byte r, byte g, byte b, int brightness,
        IReadOnlyList<ZoneCorrection> corrections)
    {
        var factor = Math.Clamp(brightness, 0, 100) / 100.0;
        var sr = (byte)Math.Round(r * factor);
        var sg = (byte)Math.Round(g * factor);
        var sb = (byte)Math.Round(b * factor);

        lock (_lock)
        {
            if (_client is null || !_client.Connected)
                throw new InvalidOperationException("Não há conexão com o OpenRGB.");

            Device[] devices;
            try
            {
                devices = _client.GetAllControllerData();
            }
            catch
            {
                DisposeClient();
                throw;
            }

            var results = new List<DeviceResult>(devices.Length);
            foreach (var device in devices)
            {
                try
                {
                    var corrected = 0;
                    var colors = BuildColors(device, sr, sg, sb, corrections, ref corrected);
                    if (colors.Length == 0)
                    {
                        results.Add(new DeviceResult(device.Name, false, "dispositivo sem LEDs"));
                        continue;
                    }

                    // Coloca o dispositivo no modo que aceita cor por LED (Direct/Custom/Static).
                    _client.SetCustomMode(device.Index);
                    _client.UpdateLeds(device.Index, colors);
                    results.Add(new DeviceResult(device.Name, true, CorrectedZones: corrected));
                }
                catch (Exception ex)
                {
                    results.Add(new DeviceResult(device.Name, false, ex.Message));
                    if (!_client.Connected)
                    {
                        DisposeClient();
                        break;
                    }
                }
            }

            return results;
        }
    }

    /// <summary>
    /// Monta o vetor de cores do dispositivo inteiro, zona por zona,
    /// trocando a ordem dos canais nas zonas que precisam de correção.
    /// </summary>
    private static Color[] BuildColors(Device device, byte r, byte g, byte b,
        IReadOnlyList<ZoneCorrection> corrections, ref int correctedZones)
    {
        var total = device.Leds.Length;
        var colors = new Color[total];
        var normal = new Color(r, g, b);
        Array.Fill(colors, normal);

        var offset = 0;
        foreach (var zone in device.Zones)
        {
            var count = (int)zone.LedCount;
            var correction = corrections.FirstOrDefault(c => c.Matches(device.Name, zone.Name));
            if (correction is not null && count > 0)
            {
                var (cr, cg, cb) = ColorOrder.Apply(r, g, b, correction.Order);
                var fixedColor = new Color(cr, cg, cb);
                for (var i = offset; i < offset + count && i < total; i++)
                    colors[i] = fixedColor;
                correctedZones++;
            }

            offset += count;
        }

        return colors;
    }

    private void DisposeClient()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
    }

    public void Dispose()
    {
        lock (_lock) DisposeClient();
    }
}

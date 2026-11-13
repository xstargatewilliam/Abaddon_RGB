/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Lista de modelos e teclas visíveis portada do OpenRGB (RGBController_LogitechHIDPP20,
 * tabela known_kb_8080_strips), Copyright (C) OpenRGB contributors, GPL-2.0-or-later.
 */
namespace AbaddonRGB.Core.Engines.Native;

/// <summary>Driver nativo dos teclados Logitech com cor por tecla (HID++ 2.0, recurso 0x8080).</summary>
public sealed class LogitechKeyboardDriver : INativeDriver
{
    public const int LogitechVendor = 0x046D;

    /// <summary>
    /// Modelos com fio conhecidos e as teclas extras que existem de verdade em cada um
    /// (o firmware anuncia IDs a mais, que não acendem nada).
    /// Chave: tipo de tecla; valor: IDs aceitos (null = todos).
    /// </summary>
    private static readonly Dictionary<int, (string Model, Dictionary<ushort, byte[]?> Extras)> Models = new()
    {
        [0xC33C] = ("G512", new() { [0x0040] = new byte[] { 0x02, 0x03 } }),
        [0xC342] = ("G512", new() { [0x0040] = new byte[] { 0x02, 0x03 } }),
        [0xC331] = ("G810", G810Extras()),
        [0xC337] = ("G810", G810Extras()),
        [0xC333] = ("G610", G810Extras()),
        [0xC338] = ("G610", G810Extras()),
        [0xC32B] = ("G910", new() { [0x0004] = null, [0x0010] = new byte[] { 0x01, 0x02 } }),
        [0xC335] = ("G910", new() { [0x0004] = null, [0x0010] = new byte[] { 0x01, 0x02 } }),
        [0xC339] = ("G Pro", new() { [0x0010] = new byte[] { 0x01 }, [0x0040] = new byte[] { 0x01, 0x02, 0x03, 0x04 } }),
    };

    private static Dictionary<ushort, byte[]?> G810Extras() => new()
    {
        [0x0010] = new byte[] { 0x01 },
        [0x0040] = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 },
        [0x0002] = new byte[] { 0xE2, 0xCD, 0xB7, 0xB6, 0xB5 },
    };

    private static readonly Dictionary<ushort, string> ZoneNames = new()
    {
        [0x0001] = "Teclas",
        [0x0002] = "Mídia",
        [0x0004] = "Teclas G",
        [0x0008] = "Botões",
        [0x0010] = "Logo",
        [0x0040] = "Indicadores",
    };

    private sealed class OpenKeyboard
    {
        public required string InterfaceKey;
        public required IHidLink Link;
        public required LogitechPerKeyKeyboard Keyboard;
        public required LightDevice Device;
        public required List<(ushort KeyType, byte KeyId)> Leds;
    }

    private readonly IHidBackend _backend;
    private readonly Action<string>? _log;
    private readonly object _lock = new();
    private readonly Dictionary<string, OpenKeyboard> _open = new(StringComparer.OrdinalIgnoreCase);

    public LogitechKeyboardDriver(IHidBackend backend, Action<string>? log = null)
    {
        _backend = backend;
        _log = log;
    }

    public string Name => "Logitech (teclados com cor por tecla)";

    public IReadOnlyList<LightDevice> Detect(string engineId, int firstIndex)
    {
        lock (_lock)
        {
            var collections = _backend.Find(LogitechVendor, Models.Keys.ToArray());
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<LightDevice>();

            foreach (var group in collections.GroupBy(c => c.InterfaceKey, StringComparer.OrdinalIgnoreCase))
            {
                // Coleção de 20 bytes (relatório 0x11) e de 64 bytes (0x12) da mesma interface.
                var longCollection = group.FirstOrDefault(c => c.OutputLength == HidPpChannel.LongLength);
                var veryLong = group.FirstOrDefault(c => c.OutputLength == HidPpChannel.VeryLongLength);
                if (longCollection is null || veryLong is null) continue;

                seen.Add(group.Key);
                var index = firstIndex + result.Count;

                if (_open.TryGetValue(group.Key, out var existing) && existing.Link.IsOpen)
                {
                    existing.Device = existing.Device with { Index = index };
                    result.Add(existing.Device);
                    continue;
                }

                if (existing is not null) Close(existing);

                var opened = TryOpen(engineId, index, longCollection, veryLong);
                if (opened is null) continue;
                _open[group.Key] = opened;
                result.Add(opened.Device);
            }

            // Teclados que sumiram (desconectados): fecha.
            foreach (var key in _open.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                Close(_open[key]);
                _open.Remove(key);
            }

            return result;
        }
    }

    private OpenKeyboard? TryOpen(string engineId, int index, HidCollectionInfo longCollection, HidCollectionInfo veryLong)
    {
        IHidLink? link = null;
        try
        {
            link = _backend.Open(longCollection, veryLong);
            var keyboard = new LogitechPerKeyKeyboard(new HidPpChannel(link));
            var error = keyboard.Discover();
            if (error is not null)
            {
                _log?.Invoke($"Teclado Logitech {longCollection.ProductId:X4}: {error}.");
                link.Dispose();
                return null;
            }

            var (model, extras) = Models.TryGetValue(longCollection.ProductId, out var known)
                ? known
                : ("Logitech", new Dictionary<ushort, byte[]?>());

            var zones = new List<LightZone>();
            var leds = new List<(ushort, byte)>();
            foreach (var type in keyboard.KeyTypes)
            {
                IReadOnlyList<byte> ids = type.KeyIds;
                if (type.KeyType != 0x0001)
                {
                    // Extras só entram se o modelo de fato tem essas luzes.
                    if (!extras.TryGetValue(type.KeyType, out var allowed)) continue;
                    if (allowed is not null) ids = ids.Where(allowed.Contains).ToList();
                }

                if (ids.Count == 0) continue;
                zones.Add(new LightZone(ZoneNames.TryGetValue(type.KeyType, out var zoneName) ? zoneName : $"Tipo 0x{type.KeyType:X2}", ids.Count));
                leds.AddRange(ids.Select(id => (type.KeyType, id)));
            }

            var name = string.IsNullOrWhiteSpace(longCollection.ProductName)
                ? $"Logitech {model}"
                : longCollection.ProductName.Trim();
            var location = "HID: " + longCollection.Path;
            var id = DeviceIdentity.Create(name, "Logitech", longCollection.Serial, location, leds.Count, zones);

            _log?.Invoke($"{name}: {leds.Count} LEDs pelo motor nativo (recurso 0x8080 no índice 0x{keyboard.FeatureIndex:X2}).");
            return new OpenKeyboard
            {
                InterfaceKey = longCollection.InterfaceKey,
                Link = link,
                Keyboard = keyboard,
                Leds = leds,
                Device = new LightDevice(engineId, index, name, zones, leds.Count, id, location),
            };
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Teclado Logitech {longCollection.ProductId:X4}: não consegui abrir ({ex.Message}).");
            try { link?.Dispose(); } catch { }
            return null;
        }
    }

    public void SetColors(LightDevice device, IReadOnlyList<RgbColor> colors)
    {
        lock (_lock)
        {
            // O ID pode ter sido trocado pelo controlador (para herdar as configurações da peça);
            // a localização HID é que identifica o teclado aberto.
            var keyboard = _open.Values.FirstOrDefault(k =>
                string.Equals(k.Device.Location, device.Location, StringComparison.OrdinalIgnoreCase));
            if (keyboard is null) throw new InvalidOperationException("Teclado não está mais conectado.");

            var byType = new List<(ushort, IReadOnlyList<(byte, RgbColor)>)>();
            for (var i = 0; i < keyboard.Leds.Count && i < colors.Count; i++)
            {
                var (keyType, keyId) = keyboard.Leds[i];
                if (byType.Count == 0 || byType[^1].Item1 != keyType)
                    byType.Add((keyType, new List<(byte, RgbColor)>()));
                ((List<(byte, RgbColor)>)byType[^1].Item2).Add((keyId, colors[i]));
            }

            string? error;
            try
            {
                error = keyboard.Keyboard.SetColors(byType);
            }
            catch (Exception ex)
            {
                // Erro de escrita = teclado desconectado: será reaberto na próxima detecção.
                Close(keyboard);
                _open.Remove(keyboard.InterfaceKey);
                throw new IOException("o teclado foi desconectado (" + ex.Message + ")", ex);
            }

            if (error is not null) throw new IOException(error);
        }
    }

    private static void Close(OpenKeyboard keyboard)
    {
        try { keyboard.Link.Dispose(); } catch { }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var keyboard in _open.Values) Close(keyboard);
            _open.Clear();
        }
    }
}

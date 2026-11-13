/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Portado do OpenRGB (Controllers/LogitechController/LogitechHIDPP20Controller e
 * RGBController_LogitechHIDPP20), Copyright (C) OpenRGB contributors, GPL-2.0-or-later:
 * recurso HID++ 2.0 0x8080 "Per Key Lighting" usado pela família G512/G610/G810/G910/G Pro.
 *   fn0 GetInfo, fn1 GetKeyTypeInfo, fn2 GetKeyColors (lista as teclas), fn3 SetKeyColors
 *   (até 14 teclas por relatório 0x12), fn5 FlushLeds (aplica o quadro).
 */
namespace AbaddonRGB.Core.Engines.Native;

/// <summary>Um tipo de tecla do 0x8080 e os IDs de tecla que o aparelho informou.</summary>
public sealed record PerKeyType(ushort KeyType, IReadOnlyList<byte> KeyIds);

/// <summary>Teclado Logitech com iluminação por tecla (recurso 0x8080).</summary>
public sealed class LogitechPerKeyKeyboard
{
    public const ushort FeaturePerKey = 0x8080;

    private const byte FnGetInfo = 0x00;
    private const byte FnGetKeyTypeInfo = 0x10;
    private const byte FnGetKeyColors = 0x20;
    private const byte FnSetKeyColors = 0x30;
    private const byte FnFlushLeds = 0x50;

    public const int KeysPerFrame = 14;

    /// <summary>Quantos quadros podem ficar sem confirmação ao mesmo tempo.</summary>
    private const int Window = 4;
    private const int AckWaitMs = 60;
    private const int TailMs = 250;
    private const int FlushTimeoutMs = 250;
    private const int BusyPollMs = 20;

    /// <summary>Tipos de tecla conhecidos (bits do typeFlags): teclado, mídia, G, botões, logo, indicadores.</summary>
    private static readonly ushort[] KnownKeyTypes = { 0x0001, 0x0002, 0x0004, 0x0008, 0x0010, 0x0040 };

    private readonly HidPpChannel _channel;
    private byte _index;

    public LogitechPerKeyKeyboard(HidPpChannel channel) => _channel = channel;

    public byte FeatureIndex => _index;
    public IReadOnlyList<PerKeyType> KeyTypes { get; private set; } = Array.Empty<PerKeyType>();

    /// <summary>Somente leitura: confirma o recurso 0x8080 e lista as teclas de cada tipo.</summary>
    /// <returns>Mensagem de erro, ou null se deu certo.</returns>
    public string? Discover()
    {
        var index = _channel.GetFeatureIndex(FeaturePerKey);
        if (index is null) return "o teclado não respondeu";
        if (index == 0) return "o teclado não tem o recurso de cor por tecla (0x8080)";
        _index = index.Value;

        var info = _channel.Request(_index, FnGetInfo, ReadOnlySpan<byte>.Empty);
        if (info is null || !info.Ok || info.Data.Length < 7) return "não consegui ler as informações de iluminação";
        var typeFlags = (ushort)((info.Data[0] << 8) | info.Data[1]);
        var maxKeys = (info.Data[5] << 8) | info.Data[6];

        var types = new List<PerKeyType>();
        foreach (var keyType in KnownKeyTypes)
        {
            if ((typeFlags & keyType) == 0) continue;

            var typeInfo = _channel.Request(_index, FnGetKeyTypeInfo,
                new[] { (byte)(keyType >> 8), (byte)(keyType & 0xFF) });
            var typeOk = typeInfo is { Ok: true } && typeInfo.Data.Length >= 2;
            var keyCount = typeOk ? (typeInfo!.Data[0] << 8) | typeInfo.Data[1] : 0;
            if (typeOk && keyCount == 0) continue;

            var ids = ReadKeyIds(keyType, keyCount, maxKeys);
            if (ids.Count > 0) types.Add(new PerKeyType(keyType, ids));
        }

        KeyTypes = types;
        return types.Count == 0 ? "o teclado não informou nenhuma tecla" : null;
    }

    /// <summary>fn2 GetKeyColors, paginado de 14 em 14: só os IDs interessam.</summary>
    private List<byte> ReadKeyIds(ushort keyType, int expectedCount, int maxKeys)
    {
        var pages = expectedCount > 0
            ? (expectedCount + KeysPerFrame - 1) / KeysPerFrame
            : Math.Max(maxKeys / KeysPerFrame + 4, 16);

        var ids = new List<byte>();
        var start = 0;
        for (var page = 0; page < pages; page++)
        {
            var response = _channel.Request(_index, FnGetKeyColors, new byte[]
            {
                (byte)(keyType >> 8), (byte)(keyType & 0xFF),
                (byte)(start >> 8), (byte)(start & 0xFF),
                0x00,
            });
            if (response is null || !response.Ok || response.Data.Length <= 4) break;

            // Data: [keyType(2), início(2), depois (id, R, G, B) * n]; id 0 = vazio.
            var found = 0;
            for (var pos = 4; pos + 3 < response.Data.Length; pos += 4)
            {
                var id = response.Data[pos];
                if (id == 0) continue;
                ids.Add(id);
                found++;
            }

            if (found == 0) break;
            start += KeysPerFrame;
        }

        return ids;
    }

    /// <summary>Monta os relatórios fn3 (até 14 teclas cada) de uma lista de teclas do mesmo tipo.</summary>
    public static List<byte[]> BuildSetKeyPayloads(ushort keyType, IReadOnlyList<(byte KeyId, RgbColor Color)> keys)
    {
        var payloads = new List<byte[]>();
        for (var offset = 0; offset < keys.Count; offset += KeysPerFrame)
        {
            var count = Math.Min(KeysPerFrame, keys.Count - offset);
            var payload = new byte[4 + count * 4];
            payload[0] = (byte)(keyType >> 8);
            payload[1] = (byte)(keyType & 0xFF);
            payload[2] = (byte)(count >> 8);
            payload[3] = (byte)(count & 0xFF);
            for (var k = 0; k < count; k++)
            {
                var (id, color) = keys[offset + k];
                payload[4 + k * 4] = id;
                payload[5 + k * 4] = color.R;
                payload[6 + k * 4] = color.G;
                payload[7 + k * 4] = color.B;
            }

            payloads.Add(payload);
        }

        return payloads;
    }

    /// <summary>
    /// Pinta as teclas e aplica (fn5). As confirmações dos quadros são aguardadas numa janela
    /// de até 4, como no OpenRGB; uma confirmação perdida não interrompe o quadro.
    /// </summary>
    /// <returns>Mensagem de erro, ou null se o teclado confirmou a aplicação.</returns>
    public string? SetColors(IReadOnlyList<(ushort KeyType, IReadOnlyList<(byte KeyId, RgbColor Color)> Keys)> byType)
    {
        if (_index == 0) return "teclado não inicializado";

        var link = _channel.Link;
        link.Drain();

        var sent = 0;
        var answered = 0;
        var rejected = 0;

        foreach (var (keyType, keys) in byType)
        {
            foreach (var payload in BuildSetKeyPayloads(keyType, keys))
            {
                while (sent - answered >= Window)
                {
                    if (!ReadOne(AckWaitMs, ref answered, ref rejected)) { answered = sent; break; }
                }

                _channel.SendVeryLong(_index, FnSetKeyColors, payload);
                sent++;
            }
        }

        var tailDeadline = Environment.TickCount64 + TailMs;
        while (answered < sent)
        {
            var remaining = (int)Math.Min(AckWaitMs, tailDeadline - Environment.TickCount64);
            if (remaining <= 0 || !ReadOne(remaining, ref answered, ref rejected)) break;
        }

        // fn5 FlushLeds: o teclado só mostra as cores depois disto. "Ocupado" (0x08) = tentar de novo.
        var flushDeadline = Environment.TickCount64 + FlushTimeoutMs * 4;
        _channel.SendLong(_index, FnFlushLeds, ReadOnlySpan<byte>.Empty);
        while (true)
        {
            var remaining = (int)Math.Min(FlushTimeoutMs, flushDeadline - Environment.TickCount64);
            if (remaining <= 0) return "o teclado não confirmou a aplicação das cores";

            var report = link.Read(remaining);
            if (report is null) return "o teclado não confirmou a aplicação das cores";

            var flush = _channel.Match(report, _index, FnFlushLeds);
            if (flush is null) continue;
            if (flush.Ok) return rejected > 0 ? $"o teclado recusou {rejected} pacote(s) de cor" : null;
            if (flush.Error != HidPpChannel.ErrorBusy) return $"o teclado recusou a aplicação (erro 0x{flush.Error:X2})";

            Thread.Sleep(BusyPollMs);
            _channel.SendLong(_index, FnFlushLeds, ReadOnlySpan<byte>.Empty);
        }
    }

    private bool ReadOne(int timeoutMs, ref int answered, ref int rejected)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var remaining = (int)(deadline - Environment.TickCount64);
            if (remaining <= 0) return false;
            var report = _channel.Link.Read(remaining);
            if (report is null) return false;

            var ack = _channel.Match(report, _index, FnSetKeyColors);
            if (ack is null) continue;
            answered++;
            if (!ack.Ok) rejected++;
            return true;
        }
    }
}

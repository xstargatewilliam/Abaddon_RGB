/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * Partes portadas do OpenRGB (Controllers/LogitechController/LogitechHIDPP20Controller),
 * Copyright (C) OpenRGB contributors, GPL-2.0-or-later: formato das mensagens HID++ 2.0,
 * consulta de índice de recurso (IRoot) e leitura das respostas.
 */
namespace AbaddonRGB.Core.Engines.Native;

/// <summary>Resposta de um pedido HID++ 2.0.</summary>
/// <param name="Data">Bytes depois do cabeçalho de 4 bytes.</param>
/// <param name="Error">Código de erro do aparelho (0 = deu certo).</param>
public sealed record HidPpResponse(byte[] Data, byte Error)
{
    public bool Ok => Error == 0;
}

/// <summary>Conversa HID++ 2.0 com um aparelho Logitech com fio (índice 0xFF).</summary>
public sealed class HidPpChannel
{
    public const byte ShortReport = 0x10;
    public const byte LongReport = 0x11;
    public const byte VeryLongReport = 0x12;
    public const int LongLength = 20;
    public const int VeryLongLength = 64;
    public const byte WiredDeviceIndex = 0xFF;

    /// <summary>Recurso de erro: a resposta vem com o índice 0xFF.</summary>
    public const byte ErrorFeature = 0xFF;

    /// <summary>Código de erro "ocupado, tente de novo".</summary>
    public const byte ErrorBusy = 0x08;

    /// <summary>
    /// Identificador desta aplicação nos pedidos (4 bits). O OpenRGB usa 0x07 e o G HUB 0x01;
    /// um valor próprio evita confundir respostas se outro programa falar com o aparelho ao mesmo tempo.
    /// </summary>
    public const byte SoftwareId = 0x0B;

    private readonly IHidLink _link;
    private readonly byte _deviceIndex;
    private readonly Dictionary<ushort, byte> _featureIndex = new();

    public HidPpChannel(IHidLink link, byte deviceIndex = WiredDeviceIndex)
    {
        _link = link;
        _deviceIndex = deviceIndex;
    }

    public IHidLink Link => _link;

    public static byte[] BuildLong(byte deviceIndex, byte feature, byte function, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[LongLength];
        buffer[0] = LongReport;
        buffer[1] = deviceIndex;
        buffer[2] = feature;
        buffer[3] = (byte)((function & 0xF0) | SoftwareId);
        payload[..Math.Min(payload.Length, LongLength - 4)].CopyTo(buffer.AsSpan(4));
        return buffer;
    }

    public static byte[] BuildVeryLong(byte deviceIndex, byte feature, byte function, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[VeryLongLength];
        buffer[0] = VeryLongReport;
        buffer[1] = deviceIndex;
        buffer[2] = feature;
        buffer[3] = (byte)((function & 0xF0) | SoftwareId);
        payload[..Math.Min(payload.Length, VeryLongLength - 4)].CopyTo(buffer.AsSpan(4));
        return buffer;
    }

    public void SendLong(byte feature, byte function, ReadOnlySpan<byte> payload) =>
        _link.WriteLong(BuildLong(_deviceIndex, feature, function, payload));

    public void SendVeryLong(byte feature, byte function, ReadOnlySpan<byte> payload) =>
        _link.WriteVeryLong(BuildVeryLong(_deviceIndex, feature, function, payload));

    /// <summary>Classifica um relatório recebido em relação a um pedido (recurso + função).</summary>
    /// <returns>Resposta, ou null se o relatório for de outra coisa.</returns>
    public HidPpResponse? Match(byte[] report, byte feature, byte function)
    {
        if (report.Length < 5) return null;
        if (report[0] is not (ShortReport or LongReport or VeryLongReport)) return null;
        if (report[1] != _deviceIndex) return null;

        // Erro: [id, dev, 0xFF, recurso original, função|swid, código]
        if (report[2] == ErrorFeature)
        {
            if (report.Length >= 6 && report[3] == feature &&
                (report[4] & 0xF0) == (function & 0xF0) && (report[4] & 0x0F) == SoftwareId)
                return new HidPpResponse(Array.Empty<byte>(), report[5] == 0 ? (byte)0xFF : report[5]);
            return null;
        }

        if (report[2] != feature) return null;
        if ((report[3] & 0xF0) != (function & 0xF0) || (report[3] & 0x0F) != SoftwareId) return null;
        return new HidPpResponse(report[4..], 0);
    }

    /// <summary>Envia um pedido (relatório 0x11) e espera a resposta.</summary>
    public HidPpResponse? Request(byte feature, byte function, ReadOnlySpan<byte> payload,
        int timeoutMs = 300, int attempts = 2)
    {
        var data = payload.ToArray();
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            _link.Drain();
            SendLong(feature, function, data);
            var response = WaitFor(feature, function, timeoutMs);
            if (response is null) continue;
            if (response.Error == ErrorBusy)
            {
                Thread.Sleep(20);
                continue;
            }

            return response;
        }

        return null;
    }

    /// <summary>Espera a resposta de um pedido já enviado.</summary>
    public HidPpResponse? WaitFor(byte feature, byte function, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var remaining = (int)(deadline - Environment.TickCount64);
            if (remaining <= 0) return null;
            var report = _link.Read(remaining);
            if (report is null) return null;
            var match = Match(report, feature, function);
            if (match is not null) return match;
        }
    }

    /// <summary>
    /// Descobre em que índice o aparelho expõe um recurso (IRoot.GetFeature, recurso 0x0000 função 0).
    /// Retorna 0 se o aparelho não tem o recurso, ou null se não respondeu.
    /// </summary>
    public byte? GetFeatureIndex(ushort featurePage)
    {
        if (_featureIndex.TryGetValue(featurePage, out var cached)) return cached;

        var response = Request(0x00, 0x00, new[] { (byte)(featurePage >> 8), (byte)(featurePage & 0xFF) });
        if (response is null) return null;
        var index = response.Ok && response.Data.Length > 0 ? response.Data[0] : (byte)0;
        _featureIndex[featurePage] = index;
        return index;
    }
}

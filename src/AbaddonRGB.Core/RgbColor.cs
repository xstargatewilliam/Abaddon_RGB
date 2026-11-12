using System.Globalization;

namespace AbaddonRGB.Core;

/// <summary>Uma cor RGB simples, independente de qualquer motor ou biblioteca.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Black => new(0, 0, 0);

    public static bool TryParse(string? hex, out RgbColor color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var value = hex.Trim().TrimStart('#');
        if (value.Length != 6) return false;
        if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;

        color = new RgbColor((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        return true;
    }

    public static RgbColor ParseOr(string? hex, RgbColor fallback) => TryParse(hex, out var c) ? c : fallback;

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Aplica o brilho (0 a 100%).</summary>
    public RgbColor Scale(int brightnessPercent)
    {
        var factor = Math.Clamp(brightnessPercent, 0, 100) / 100.0;
        return new RgbColor((byte)Math.Round(R * factor), (byte)Math.Round(G * factor), (byte)Math.Round(B * factor));
    }

    /// <summary>Mistura duas cores: t = 0 devolve <paramref name="a"/>, t = 1 devolve <paramref name="b"/>.</summary>
    public static RgbColor Lerp(RgbColor a, RgbColor b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        static byte Mix(byte x, byte y, double t) => (byte)Math.Round(x + (y - x) * t);
        return new RgbColor(Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t));
    }

    /// <summary>Maior diferença entre os canais das duas cores.</summary>
    public int Distance(RgbColor other) =>
        Math.Max(Math.Abs(R - other.R), Math.Max(Math.Abs(G - other.G), Math.Abs(B - other.B)));
}

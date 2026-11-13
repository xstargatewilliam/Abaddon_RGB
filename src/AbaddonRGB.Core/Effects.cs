/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
using System.Text.Json.Serialization;

namespace AbaddonRGB.Core;

/// <summary>Efeitos de luz calculados pelo próprio programa (funcionam em qualquer peça e motor).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LightEffect
{
    Static,
    Breathing,
    Spectrum,
    Wave,
    Stars,
}

public static class Effects
{
    public const int MinSpeed = 1;
    public const int MaxSpeed = 10;

    public static readonly LightEffect[] All =
        { LightEffect.Static, LightEffect.Breathing, LightEffect.Spectrum, LightEffect.Wave, LightEffect.Stars };

    public static string DisplayName(LightEffect effect) => effect switch
    {
        LightEffect.Breathing => "Respiração",
        LightEffect.Spectrum => "Ciclo de cores",
        LightEffect.Wave => "Onda arco-íris",
        LightEffect.Stars => "Estrelas",
        _ => "Estático",
    };

    public static bool IsAnimated(LightEffect effect) => effect != LightEffect.Static;

    /// <summary>Se o efeito usa a cor geral escolhida (os de arco-íris não usam).</summary>
    public static bool UsesBaseColor(LightEffect effect) =>
        effect is LightEffect.Static or LightEffect.Breathing or LightEffect.Stars;

    /// <summary>Duração de um ciclo, em segundos: velocidade 1 = lento, 10 = rápido.</summary>
    private static double Period(LightEffect effect, int speed)
    {
        var s = Math.Clamp(speed, MinSpeed, MaxSpeed);
        return effect switch
        {
            LightEffect.Breathing => 11.0 / s + 0.9,
            LightEffect.Spectrum => 40.0 / s,
            LightEffect.Wave => 16.0 / s,
            LightEffect.Stars => 9.0 / s + 0.6,
            _ => 1,
        };
    }

    /// <summary>
    /// Cor de um LED num instante.
    /// </summary>
    /// <param name="seconds">Tempo desde o início do efeito.</param>
    /// <param name="position">Posição do LED de 0 a 1, percorrendo as peças em ordem.</param>
    /// <param name="ledKey">Número fixo do LED (para as estrelas piscarem em momentos diferentes).</param>
    public static RgbColor ColorAt(LightEffect effect, int speed, double seconds, RgbColor baseColor,
        double position, int ledKey)
    {
        var period = Period(effect, speed);
        switch (effect)
        {
            case LightEffect.Breathing:
            {
                // Sobe e desce suave; nunca apaga de todo para não parecer que desligou.
                var phase = seconds / period;
                var level = 0.06 + 0.94 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
                return Multiply(baseColor, level);
            }

            case LightEffect.Spectrum:
                return FromHue(Fraction(seconds / period));

            case LightEffect.Wave:
                return FromHue(Fraction(position - seconds / period));

            case LightEffect.Stars:
            {
                // Fundo na cor escolhida, fraco; cada LED tem seu momento de brilhar em branco.
                var dim = Multiply(baseColor, 0.22);
                var offset = Hash(ledKey);
                var phase = Fraction(seconds / period + offset);
                const double flash = 0.18;
                if (phase >= flash) return dim;
                var intensity = Math.Sin(Math.PI * phase / flash);
                return RgbColor.Lerp(dim, new RgbColor(255, 255, 255), intensity);
            }

            default:
                return baseColor;
        }
    }

    private static double Fraction(double value) => value - Math.Floor(value);

    private static RgbColor Multiply(RgbColor color, double factor) =>
        new((byte)Math.Round(color.R * factor), (byte)Math.Round(color.G * factor), (byte)Math.Round(color.B * factor));

    /// <summary>Número entre 0 e 1 que parece aleatório, mas é sempre o mesmo para o mesmo LED.</summary>
    private static double Hash(int key)
    {
        unchecked
        {
            var x = (uint)key * 2654435761u;
            x ^= x >> 15;
            x *= 2246822519u;
            x ^= x >> 13;
            return (x & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>Cor de saturação e brilho máximos para um tom (0 a 1).</summary>
    public static RgbColor FromHue(double hue)
    {
        var h = Fraction(hue) * 6;
        var x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = (int)h switch
        {
            0 => (1.0, x, 0.0),
            1 => (x, 1.0, 0.0),
            2 => (0.0, 1.0, x),
            3 => (0.0, x, 1.0),
            4 => (x, 0.0, 1.0),
            _ => (1.0, 0.0, x),
        };
        return new RgbColor((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}

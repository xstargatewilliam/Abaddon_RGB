using AbaddonRGB.Core.Engines;

namespace AbaddonRGB.Core;

/// <summary>Monta a cor de cada LED de uma peça a partir da cor geral, das cores próprias e das correções.</summary>
public static class ColorComposer
{
    /// <param name="top">Cor que vence todas as outras (usada pela automação de temperatura).</param>
    public static RgbColor[] Build(LightDevice device, RgbColor baseColor, int brightness,
        IReadOnlyList<TargetColor> overrides, IReadOnlyList<ZoneCorrection> corrections, TargetColor? top = null,
        bool gamma = false, Func<int, RgbColor>? baseForLed = null)
    {
        // baseForLed: cor geral LED a LED (efeitos). As cores próprias e a de temperatura continuam fixas.
        RgbColor Base(int led) => baseForLed is null ? baseColor : baseForLed(led);

        var colors = new RgbColor[Math.Max(device.LedCount, 0)];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = (gamma ? Gamma(Base(i)) : Base(i)).Scale(brightness);

        var offset = 0;
        foreach (var zone in device.Zones)
        {
            var color = baseColor;
            var own = false;

            // Primeiro a cor própria da peça inteira, depois a da zona (a mais específica vence).
            foreach (var o in overrides)
                if (o.IsWholeDevice && o.Matches(device.Name, zone.Name, device.StableId) && RgbColor.TryParse(o.Color, out var c))
                {
                    color = c;
                    own = true;
                }
            foreach (var o in overrides)
                if (!o.IsWholeDevice && o.Matches(device.Name, zone.Name, device.StableId) && RgbColor.TryParse(o.Color, out var c))
                {
                    color = c;
                    own = true;
                }

            if (top is not null && top.Matches(device.Name, zone.Name, device.StableId) && RgbColor.TryParse(top.Color, out var t))
            {
                color = t;
                own = true;
            }

            var correction = corrections.FirstOrDefault(c => c.Matches(device.Name, zone.Name, device.StableId));
            RgbColor Finish(RgbColor value)
            {
                if (gamma) value = Gamma(value);
                value = value.Scale(brightness);
                if (correction is null) return value;
                var (r, g, b) = ColorOrder.Apply(value.R, value.G, value.B, correction.Order);
                return new RgbColor(r, g, b);
            }

            var fixedColor = Finish(color);
            for (var i = offset; i < offset + zone.LedCount && i < colors.Length; i++)
                colors[i] = own || baseForLed is null ? fixedColor : Finish(Base(i));

            offset += zone.LedCount;
        }

        return colors;
    }

    private const double GammaExponent = 2.2;
    private static readonly byte[] GammaTable = Enumerable.Range(0, 256)
        .Select(v => (byte)Math.Round(255 * Math.Pow(v / 255.0, GammaExponent)))
        .ToArray();

    /// <summary>
    /// Os LEDs acendem de forma linear, mas o olho não enxerga assim: 25% de verde
    /// parece quase metade, e um azul com um pouco de verde vira ciano.
    /// A correção de gama ajusta a mistura. Cores puras, branco e preto não mudam.
    /// </summary>
    public static RgbColor Gamma(RgbColor color) =>
        new(GammaTable[color.R], GammaTable[color.G], GammaTable[color.B]);
}

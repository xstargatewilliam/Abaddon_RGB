using AbaddonRGB.Core.Engines;

namespace AbaddonRGB.Core;

/// <summary>Monta a cor de cada LED de uma peça a partir da cor geral, das cores próprias e das correções.</summary>
public static class ColorComposer
{
    /// <param name="top">Cor que vence todas as outras (usada pela automação de temperatura).</param>
    public static RgbColor[] Build(LightDevice device, RgbColor baseColor, int brightness,
        IReadOnlyList<TargetColor> overrides, IReadOnlyList<ZoneCorrection> corrections, TargetColor? top = null)
    {
        var colors = new RgbColor[Math.Max(device.LedCount, 0)];
        Array.Fill(colors, baseColor.Scale(brightness));

        var offset = 0;
        foreach (var zone in device.Zones)
        {
            var color = baseColor;

            // Primeiro a cor própria da peça inteira, depois a da zona (a mais específica vence).
            foreach (var o in overrides)
                if (o.IsWholeDevice && o.Matches(device.Name, zone.Name, device.StableId) && RgbColor.TryParse(o.Color, out var c))
                    color = c;
            foreach (var o in overrides)
                if (!o.IsWholeDevice && o.Matches(device.Name, zone.Name, device.StableId) && RgbColor.TryParse(o.Color, out var c))
                    color = c;

            if (top is not null && top.Matches(device.Name, zone.Name, device.StableId) && RgbColor.TryParse(top.Color, out var t))
                color = t;

            color = color.Scale(brightness);

            var correction = corrections.FirstOrDefault(c => c.Matches(device.Name, zone.Name, device.StableId));
            if (correction is not null)
            {
                var (r, g, b) = ColorOrder.Apply(color.R, color.G, color.B, correction.Order);
                color = new RgbColor(r, g, b);
            }

            for (var i = offset; i < offset + zone.LedCount && i < colors.Length; i++)
                colors[i] = color;

            offset += zone.LedCount;
        }

        return colors;
    }
}

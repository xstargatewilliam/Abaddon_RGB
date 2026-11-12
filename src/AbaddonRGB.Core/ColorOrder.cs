namespace AbaddonRGB.Core;

/// <summary>
/// Corrige a ordem dos canais de cor para LEDs que leem os bytes numa ordem
/// diferente da que o controlador envia (ex.: cooler ARGB que lê G-R-B).
/// </summary>
public static class ColorOrder
{
    public const string Default = "RGB";

    /// <summary>
    /// Verifica se a ordem é uma permutação válida de R, G e B (ex.: "GRB", "BGR").
    /// </summary>
    public static bool IsValid(string? order)
    {
        if (string.IsNullOrWhiteSpace(order) || order.Length != 3)
            return false;

        var upper = order.ToUpperInvariant();
        return upper.Contains('R') && upper.Contains('G') && upper.Contains('B');
    }

    /// <summary>
    /// Recebe a cor desejada e devolve os bytes que precisam ser enviados
    /// para que um LED com a ordem <paramref name="ledOrder"/> mostre essa cor.
    /// O byte na posição i é lido pelo LED como o canal ledOrder[i].
    /// </summary>
    public static (byte R, byte G, byte B) Apply(byte r, byte g, byte b, string? ledOrder)
    {
        if (!IsValid(ledOrder))
            return (r, g, b);

        var order = ledOrder!.ToUpperInvariant();

        byte Channel(char c) => c switch
        {
            'R' => r,
            'G' => g,
            _ => b,
        };

        return (Channel(order[0]), Channel(order[1]), Channel(order[2]));
    }
}

/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
using AbaddonRGB.Core.Engines;

namespace AbaddonRGB.Core;

/// <summary>
/// Quantos envios por segundo cada peça aguenta.
/// A regra é conservadora: toda peça começa no limite mais seguro (4 por segundo)
/// e só ganha mais quando é de um tipo conhecido e testado. Assim uma peça nova,
/// com nome ou localização diferente do esperado, nunca recebe comandos demais.
/// </summary>
public static class DeviceSafety
{
    /// <summary>Limite padrão: o que o controlador da placa ASRock aguentou sem travar.</summary>
    public const int ConservativeIntervalMs = 250;

    public sealed record Rule(string Description, Func<LightDevice, bool> Matches, int IntervalMs);

    /// <summary>Exceções ao limite padrão, da mais específica para a mais geral.</summary>
    public static readonly IReadOnlyList<Rule> Rules = new[]
    {
        // Placas ASRock (Polychrome) ficam sempre no limite seguro, mesmo se outra regra combinar.
        new Rule("Placa ASRock (Polychrome)", d => Contains(d.Name, "ASRock") || Contains(d.Name, "Polychrome"),
            ConservativeIntervalMs),

        // Memórias com chip ENE: mais rápidas que a placa, mas ainda no barramento lento.
        new Rule("Memória ENE (SMBus)", d => Contains(d.Name, "ENE") && OnSmBus(d), 90),

        // Motor nativo: drivers próprios, por USB.
        new Rule("Motor nativo (USB)", d => d.EngineId == NativeEngine.EngineId && IsUsb(d), 0),

        // Teclados, mouses e outros aparelhos USB pelo OpenRGB.
        new Rule("Aparelho USB", d => IsUsb(d) && !OnSmBus(d), 0),
    };

    /// <summary>Intervalo mínimo entre dois envios para esta peça.</summary>
    public static int MinIntervalMs(LightDevice device)
    {
        foreach (var rule in Rules)
            if (rule.Matches(device)) return rule.IntervalMs;
        return ConservativeIntervalMs;
    }

    /// <summary>Qual regra vale para a peça (para o registro).</summary>
    public static string Describe(LightDevice device)
    {
        foreach (var rule in Rules)
            if (rule.Matches(device)) return rule.Description;
        return "Desconhecida (limite seguro)";
    }

    private static bool Contains(string? text, string value) =>
        text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

    private static bool OnSmBus(LightDevice d) =>
        Contains(d.Location, "I2C") || Contains(d.Location, "SMBus");

    private static bool IsUsb(LightDevice d) =>
        d.Location.StartsWith("HID", StringComparison.OrdinalIgnoreCase) || d.PhysicalKey.Length > 0;
}

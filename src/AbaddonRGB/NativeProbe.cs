using HidSharp;

namespace AbaddonRGB;

/// <summary>
/// Primeiro passo do motor nativo: procurar as peças direto no Windows, sem o OpenRGB.
/// É somente leitura: lista os dispositivos USB (HID), não abre nem escreve em nenhum.
/// </summary>
internal static class NativeProbe
{
    private const int LogitechVendor = 0x046D;

    private static readonly (int Vendor, int Product, string Name)[] Known =
    {
        (LogitechVendor, 0xC33C, "Teclado Logitech G512"),
        (LogitechVendor, 0xC53F, "Receptor Logitech Lightspeed (mouse G305)"),
    };

    public static IReadOnlyList<string> Run()
    {
        var lines = new List<string>();
        var devices = DeviceList.Local.GetHidDevices().ToList();
        lines.Add($"{devices.Count} interfaces USB (HID) encontradas no Windows.");

        foreach (var (vendor, product, name) in Known)
        {
            var interfaces = devices.Count(d => d.VendorID == vendor && d.ProductID == product);
            lines.Add(interfaces > 0
                ? $"✔ {name}: encontrado ({vendor:X4}:{product:X4}, {interfaces} interfaces). Driver nativo previsto para a Fase 1."
                : $"✖ {name}: não encontrado ({vendor:X4}:{product:X4}).");
        }

        var others = devices
            .Where(d => d.VendorID == LogitechVendor && !Known.Any(k => k.Product == d.ProductID))
            .Select(d => d.ProductID)
            .Distinct()
            .Select(p => $"{LogitechVendor:X4}:{p:X4}")
            .ToList();
        if (others.Count > 0)
            lines.Add("Outros dispositivos Logitech: " + string.Join(", ", others) + ".");

        lines.Add("Placa-mãe, cooler e memórias ficam no SMBus: a detecção nativa delas entra na Fase 2 (PawnIO + administrador).");
        return lines;
    }
}

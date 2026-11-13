/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
namespace AbaddonRGB.Core.Engines.Native;

/// <summary>Uma "coleção" HID do Windows (cada interface de um aparelho pode ter várias).</summary>
/// <param name="Path">Caminho do Windows para abrir a coleção.</param>
/// <param name="OutputLength">Tamanho do relatório de saída (com o byte do ID).</param>
public sealed record HidCollectionInfo(
    string Path,
    int VendorId,
    int ProductId,
    int OutputLength,
    int InputLength,
    string ProductName,
    string Serial)
{
    /// <summary>
    /// O mesmo caminho sem o trecho "&amp;colNN" e sem o número da instância da coleção:
    /// coleções da mesma interface física têm a mesma chave.
    /// (Portado de LogitechDevicePathKey, OpenRGB, GPL-2.0-or-later.)
    /// </summary>
    public string InterfaceKey
    {
        get
        {
            var key = Path ?? "";
            var collection = false;
            for (var pos = 0; pos + 4 <= key.Length; pos++)
            {
                if (key[pos] == '&' && (key[pos + 1] is 'c' or 'C') && (key[pos + 2] is 'o' or 'O') && (key[pos + 3] is 'l' or 'L'))
                {
                    var end = pos + 4;
                    while (end < key.Length && Uri.IsHexDigit(key[end])) end++;
                    key = key.Remove(pos, end - pos);
                    collection = true;
                    break;
                }
            }

            if (collection)
            {
                var guid = key.LastIndexOf('#');
                var last = guid < 0 ? -1 : key.LastIndexOf('&', guid);
                if (last >= 0) key = key.Remove(last, guid - last);
            }

            return key.ToLowerInvariant();
        }
    }
}

/// <summary>Canal aberto com um aparelho HID: escreve relatórios e entrega os que chegam.</summary>
public interface IHidLink : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Envia um relatório 0x11 (20 bytes).</summary>
    void WriteLong(byte[] report);

    /// <summary>Envia um relatório 0x12 (64 bytes).</summary>
    void WriteVeryLong(byte[] report);

    /// <summary>Próximo relatório recebido (de qualquer coleção), ou null se o tempo acabar.</summary>
    byte[]? Read(int timeoutMs);

    /// <summary>Descarta relatórios antigos que ficaram na fila.</summary>
    void Drain();
}

/// <summary>Quem lista e abre aparelhos HID (no programa real, a biblioteca HidSharp).</summary>
public interface IHidBackend
{
    IReadOnlyList<HidCollectionInfo> Find(int vendorId, IReadOnlyCollection<int> productIds);

    /// <summary>Abre as coleções "longa" (0x11) e "muito longa" (0x12) da mesma interface.</summary>
    IHidLink Open(HidCollectionInfo longCollection, HidCollectionInfo veryLongCollection);
}

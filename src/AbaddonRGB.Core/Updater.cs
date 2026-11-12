using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AbaddonRGB.Core;

/// <summary>Uma versão publicada nas releases do GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, string ChecksumUrl = "");

/// <summary>
/// Atualização automática: procura a release mais recente no GitHub, baixa o novo
/// AbaddonRGB.exe e troca o arquivo atual por ele.
/// </summary>
public static class Updater
{
    public const string Owner = "xstargatewilliam";
    public const string Repo = "Abaddon_RGB";
    public const string AssetName = "AbaddonRGB.exe";
    public const string ChecksumAssetName = "AbaddonRGB.exe.sha256";

    private const string OldSuffix = ".old";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AbaddonRGB", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Versão do programa que está rodando (vem do &lt;Version&gt; do .csproj).</summary>
    public static Version CurrentVersion(System.Reflection.Assembly assembly) =>
        Normalize(assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Retorna a atualização disponível, ou null se já estiver na versão mais nova.</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        using var response = await Http.GetAsync(url, cancellationToken);

        // 404 = ainda não existe nenhuma release publicada.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var latest = ParseLatestRelease(json);
        return latest is not null && IsNewer(latest.Version, current) ? latest : null;
    }

    /// <summary>Lê a resposta da API do GitHub e encontra o AbaddonRGB.exe da release.</summary>
    public static UpdateInfo? ParseLatestRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
        if (!root.TryGetProperty("tag_name", out var tagElement)) return null;

        var tag = tagElement.GetString() ?? "";
        if (!TryParseTag(tag, out var version)) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        string? exe = null;
        string? checksum = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var download = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
            if (string.IsNullOrWhiteSpace(download)) continue;
            if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)) exe = download;
            if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase)) checksum = download;
        }

        // Releases antigas sem hash verificável não são instaladas automaticamente.
        return exe is not null && checksum is not null
            ? new UpdateInfo(version, tag, exe, checksum) : null;
    }

    /// <summary>Aceita "v0.2.0", "V0.2", "0.2.1" etc.</summary>
    public static bool TryParseTag(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) text = text[..dash];
        if (!Version.TryParse(text.Contains('.') ? text : text + ".0", out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    public static bool IsNewer(Version remote, Version current) => Normalize(remote) > Normalize(current);

    private static Version Normalize(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    /// <summary>Baixa o novo .exe para a pasta temporária e confere se é um executável válido.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(Path.GetTempPath(), "AbaddonRGB_update");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"AbaddonRGB_{update.Version}.exe");

        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(target);
            await input.CopyToAsync(output, cancellationToken);
        }

        try
        {
            ValidateExecutable(target);
            if (string.IsNullOrWhiteSpace(update.ChecksumUrl))
                throw new InvalidDataException("A release não tem checksum SHA-256.");

            var expectedText = (await Http.GetStringAsync(update.ChecksumUrl, cancellationToken)).Trim();
            var expectedHash = expectedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Checksum SHA-256 inválido na release.");

            await using var stream = File.OpenRead(target);
            var actualHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 do executável não confere com a release.");

            return target;
        }
        catch
        {
            try { File.Delete(target); } catch { }
            throw;
        }
    }

    /// <summary>Um .exe do Windows começa com "MZ" e o nosso tem vários megabytes.</summary>
    public static void ValidateExecutable(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1_000_000)
            throw new InvalidDataException("O arquivo baixado está incompleto.");

        using var stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("O arquivo baixado não é um programa válido.");
    }

    /// <summary>
    /// Troca o .exe atual pelo novo e abre a versão nova. O Windows não deixa
    /// sobrescrever um programa em uso, mas deixa renomear: o atual vira ".old"
    /// e é apagado quando a versão nova abrir.
    /// </summary>
    public static void InstallAndRestart(string downloadedExe, string currentExe, string arguments)
    {
        var oldPath = currentExe + OldSuffix;
        if (File.Exists(oldPath)) File.Delete(oldPath);

        File.Move(currentExe, oldPath);
        try
        {
            File.Move(downloadedExe, currentExe);
        }
        catch
        {
            // Deu errado: devolve o programa antigo para o lugar.
            File.Move(oldPath, currentExe);
            throw;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(currentExe) ?? "",
            });
            if (process is null) throw new InvalidOperationException("Falha ao iniciar a nova versão.");
        }
        catch
        {
            // Se a nova versão nem iniciar, devolve o executável anterior.
            try
            {
                File.Delete(currentExe);
                File.Move(oldPath, currentExe);
            }
            catch { }
            throw;
        }
    }

    /// <summary>Apaga a versão antiga que sobrou da última atualização.</summary>
    public static void CleanupOldVersion(string currentExe)
    {
        var oldPath = currentExe + OldSuffix;
        for (var attempt = 0; attempt < 10 && File.Exists(oldPath); attempt++)
        {
            try
            {
                File.Delete(oldPath);
            }
            catch
            {
                // A versão antiga pode estar terminando de fechar.
                Thread.Sleep(500);
            }
        }
    }
}

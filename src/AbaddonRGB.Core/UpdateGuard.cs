/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
using System.Diagnostics;
using System.Text.Json;

namespace AbaddonRGB.Core;

/// <summary>
/// Atualização em duas etapas: a versão nova só é dada como boa depois de ficar aberta
/// um tempo e controlando peças. Até lá a versão anterior fica guardada (".old").
/// Só conta como falha uma abertura que terminou sem fechar normalmente (travou ou caiu):
/// fechar o programa pelo menu não conta. Com duas falhas, a versão anterior volta sozinha.
/// </summary>
public static class UpdateGuard
{
    /// <summary>Quanto tempo a versão nova precisa ficar aberta para ser confirmada.</summary>
    public static readonly TimeSpan ConfirmAfter = TimeSpan.FromSeconds(45);

    /// <summary>Quantas aberturas que caíram antes de voltar à versão anterior.</summary>
    public const int MaxAttempts = 2;

    private sealed class Pending
    {
        public string Exe { get; set; } = "";
        public string FromVersion { get; set; } = "";
        public string ToVersion { get; set; } = "";
        /// <summary>Aberturas da versão nova que terminaram sem fechar normalmente.</summary>
        public int Failures { get; set; }

        /// <summary>True enquanto a versão nova está aberta (fica true se ela cair).</summary>
        public bool Running { get; set; }
    }

    public static string MarkerPath(string folder) => Path.Combine(folder, "update-pending.json");

    /// <summary>Chamado pela versão antiga, logo antes de abrir a nova.</summary>
    public static void BeginUpdate(string folder, string exe, string fromVersion, string toVersion)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(MarkerPath(folder), JsonSerializer.Serialize(new Pending
        {
            Exe = exe,
            FromVersion = fromVersion,
            ToVersion = toVersion,
        }));
    }

    public enum StartDecision
    {
        /// <summary>Nada pendente: abrir normalmente.</summary>
        Normal,

        /// <summary>Versão nova em teste: abrir e confirmar depois de <see cref="ConfirmAfter"/>.</summary>
        Trial,

        /// <summary>A versão nova falhou demais: a anterior foi restaurada e deve ser aberta.</summary>
        RolledBack,
    }

    /// <summary>
    /// Chamado no início do programa. Se a abertura anterior da versão nova não fechou
    /// normalmente, conta uma falha; com <see cref="MaxAttempts"/> falhas, devolve a versão anterior.
    /// </summary>
    public static StartDecision OnStartup(string folder, string exe, out string message)
    {
        message = "";
        var marker = MarkerPath(folder);
        if (!File.Exists(marker)) return StartDecision.Normal;

        Pending? pending;
        try { pending = JsonSerializer.Deserialize<Pending>(File.ReadAllText(marker)); }
        catch { pending = null; }

        if (pending is null || !string.Equals(Path.GetFullPath(pending.Exe), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
        {
            // Marcador estragado ou de outro .exe: descarta.
            TryDelete(marker);
            return StartDecision.Normal;
        }

        // A abertura anterior não chegou a fechar normalmente: foi uma falha.
        if (pending.Running) pending.Failures++;

        var old = exe + Updater.OldSuffix;
        if (pending.Failures >= MaxAttempts && File.Exists(old))
        {
            if (TryRestore(exe, old, out var error))
            {
                TryDelete(marker);
                message = $"A versão {pending.ToVersion} caiu {pending.Failures} vezes ao abrir. Voltei para a {pending.FromVersion}.";
                return StartDecision.RolledBack;
            }

            // Não deu para trocar os arquivos: segue com a versão nova, que está no lugar.
            message = $"Não consegui voltar para a {pending.FromVersion}: {error}";
        }

        pending.Running = true;
        File.WriteAllText(marker, JsonSerializer.Serialize(pending));
        return StartDecision.Trial;
    }

    /// <summary>Chamado quando o programa fecha normalmente: essa abertura não conta como falha.</summary>
    public static void MarkCleanExit(string folder)
    {
        var marker = MarkerPath(folder);
        try
        {
            if (!File.Exists(marker)) return;
            var pending = JsonSerializer.Deserialize<Pending>(File.ReadAllText(marker));
            if (pending is null) return;
            pending.Running = false;
            File.WriteAllText(marker, JsonSerializer.Serialize(pending));
        }
        catch { }
    }

    /// <summary>
    /// Troca a versão nova pela anterior em etapas, desfazendo cada uma se a seguinte falhar:
    /// nunca deixa o caminho do programa sem um .exe.
    /// </summary>
    public static bool TryRestore(string exe, string old, out string error)
    {
        error = "";
        var failed = exe + ".failed";
        try
        {
            TryDelete(failed);
            File.Move(exe, failed);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false; // nada mudou
        }

        try
        {
            File.Move(old, exe);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { File.Move(failed, exe); } // devolve a versão nova para o lugar
            catch (Exception back) { error += " / " + back.Message; }
            return false;
        }
    }

    /// <summary>A versão nova ficou aberta o bastante: apaga a anterior e o marcador.</summary>
    public static void Confirm(string folder, string exe)
    {
        TryDelete(MarkerPath(folder));
        Updater.CleanupOldVersion(exe);
        TryDelete(exe + ".failed");
    }

    /// <summary>Abre o .exe restaurado (depois de um RolledBack).</summary>
    public static void Relaunch(string exe, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        });
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}

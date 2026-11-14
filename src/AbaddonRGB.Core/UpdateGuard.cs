/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
using System.Diagnostics;
using System.Text.Json;

namespace AbaddonRGB.Core;

/// <summary>
/// Atualização em duas etapas: a versão nova só é dada como boa depois de ficar aberta
/// alguns segundos. Até lá a versão anterior fica guardada (".old"). Se a versão nova
/// fechar com erro logo ao abrir, na terceira tentativa a anterior volta sozinha.
/// </summary>
public static class UpdateGuard
{
    /// <summary>Quanto tempo a versão nova precisa ficar aberta para ser confirmada.</summary>
    public static readonly TimeSpan ConfirmAfter = TimeSpan.FromSeconds(45);

    /// <summary>Quantas aberturas sem confirmação antes de voltar à versão anterior.</summary>
    public const int MaxAttempts = 2;

    private sealed class Pending
    {
        public string Exe { get; set; } = "";
        public string FromVersion { get; set; } = "";
        public string ToVersion { get; set; } = "";
        public int Attempts { get; set; }
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
            Attempts = 0,
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
    /// Chamado no início do programa. Conta a tentativa e, se a versão nova já falhou
    /// <see cref="MaxAttempts"/> vezes, devolve a versão anterior para o lugar.
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

        var old = exe + Updater.OldSuffix;
        if (pending.Attempts >= MaxAttempts && File.Exists(old))
        {
            // A versão nova abriu e caiu repetidas vezes: volta para a anterior.
            var failed = exe + ".failed";
            TryDelete(failed);
            File.Move(exe, failed);
            File.Move(old, exe);
            TryDelete(marker);
            message = $"A versão {pending.ToVersion} não abriu direito. Voltei para a {pending.FromVersion}.";
            return StartDecision.RolledBack;
        }

        pending.Attempts++;
        File.WriteAllText(marker, JsonSerializer.Serialize(pending));
        return StartDecision.Trial;
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

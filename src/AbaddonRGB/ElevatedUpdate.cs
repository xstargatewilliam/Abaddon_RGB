/*
 * Abaddon RGB
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
using System.ComponentModel;
using System.Diagnostics;
using AbaddonRGB.Core;
using Microsoft.Win32;

namespace AbaddonRGB;

/// <summary>
/// Atualização quando o programa está instalado em C:\Program Files (só administradores escrevem lá).
/// O programa aberto normalmente não consegue trocar o próprio arquivo; então ele se abre de novo
/// como administrador (o Windows pede a confirmação) só para baixar e instalar, e fecha.
/// O download vai para a própria pasta protegida, como em <see cref="Updater.DownloadAsync"/>.
/// </summary>
internal static class ElevatedUpdate
{
    public const string Argument = "--install-update";

    /// <summary>Id do instalador (mesmo AppId do installer\AbaddonRGB.iss).</summary>
    private const string UninstallKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{2EBE7F89-F592-473F-8037-612B97C13BBE}_is1";

    /// <summary>True se este programa consegue gravar na pasta do próprio .exe.</summary>
    public static bool CanWriteNextTo(string exe)
    {
        var probe = Path.Combine(Path.GetDirectoryName(exe) ?? ".", ".abaddon-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Abre uma cópia como administrador para instalar a atualização.
    /// False se a pessoa recusou o aviso do Windows.
    /// </summary>
    public static bool Start(string exe, bool tray)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = Argument + (tray ? " " + StartupManager.TrayArgument : ""),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // "Não" no aviso do Windows
        }
    }

    /// <summary>Executado pela cópia de administrador: procura, baixa, instala e abre a versão nova.</summary>
    public static void Run(string exe, bool tray)
    {
        var restart = Program.UpdatedArgument + (tray ? " " + StartupManager.TrayArgument : "");
        try
        {
            var current = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var update = Updater.CheckAsync(current).GetAwaiter().GetResult();
            if (update is null)
            {
                // Nada novo (ou já instalado por outra cópia): só abre o programa de novo.
                UpdateGuard.Relaunch(exe, restart);
                return;
            }

            var from = "v" + current.ToString(3);
            var to = "v" + update.Version.ToString(3);
            var downloaded = Updater.DownloadAsync(update, exe).GetAwaiter().GetResult();

            UpdateGuard.BeginUpdate(AppConfig.Folder, exe, from, to);
            try
            {
                Updater.InstallAndRestart(downloaded, exe, restart);
            }
            catch
            {
                try { File.Delete(UpdateGuard.MarkerPath(AppConfig.Folder)); } catch { }
                throw;
            }

            UpdateInstalledVersion(update.Version);
        }
        catch (Exception ex)
        {
            MessageBox.Show("A atualização falhou: " + ex.Message + "\n\nO Abaddon RGB vai abrir na versão atual.",
                "Abaddon RGB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            try { UpdateGuard.Relaunch(exe, restart); } catch { }
        }
    }

    /// <summary>Mantém a versão certa em Configurações → Aplicativos instalados.</summary>
    private static void UpdateInstalledVersion(Version version)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallKey, writable: true);
            if (key is null) return; // não foi instalado pelo instalador
            key.SetValue("DisplayVersion", version.ToString(3));
            key.SetValue("DisplayName", "Abaddon RGB");
        }
        catch
        {
        }
    }
}

using System.Diagnostics;
using System.Security.Principal;

namespace AbaddonRGB;

/// <summary>
/// Permissão de administrador. A temperatura da CPU (Ryzen) só pode ser lida assim:
/// o PawnIO só libera o acesso ao processador para programas abertos como administrador.
/// </summary>
internal static class Elevation
{
    public const string ElevatedArgument = "--elevated";

    public static bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Abre uma cópia do programa como administrador (o Windows pergunta antes).
    /// Retorna false se a pessoa recusou no aviso do Windows.
    /// </summary>
    public static bool RelaunchAsAdministrator(bool startInTray)
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = ElevatedArgument + (startInTray ? " " + StartupManager.TrayArgument : ""),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // A pessoa clicou em "Não" no aviso do Windows.
            return false;
        }
    }
}

using System.Diagnostics;
using Microsoft.Win32;

namespace AbaddonRGB;

/// <summary>
/// Liga e desliga a inicialização automática com o Windows, de dois jeitos:
/// - normal: pela chave "Run" do registro do usuário (sem administrador);
/// - como administrador: por uma tarefa agendada com privilégio mais alto, que abre
///   o programa no login sem o aviso do Windows (necessário para ler a CPU).
/// </summary>
internal static class StartupManager
{
    public const string TrayArgument = "--tray";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AbaddonRGB";
    private const string TaskName = "AbaddonRGB";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
    private static string Command => $"\"{ExePath}\" {TrayArgument}";

    public static bool IsEnabled => RunKeyExists || TaskExists;

    /// <summary>True se a inicialização está configurada para abrir como administrador.</summary>
    public static bool StartsElevated => TaskExists;

    private static bool RunKeyExists
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    private static bool TaskExists => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>
    /// Liga ou desliga a inicialização. Com <paramref name="elevated"/>, usa a tarefa agendada
    /// (só funciona se o programa já estiver aberto como administrador).
    /// </summary>
    public static void SetEnabled(bool enabled, bool elevated)
    {
        if (!enabled)
        {
            SetRunKey(false);
            DeleteTask();
            return;
        }

        if (elevated)
        {
            if (!Elevation.IsAdministrator)
                throw new InvalidOperationException("Para iniciar como administrador, o Abaddon RGB precisa estar aberto como administrador.");
            CreateTask();
            SetRunKey(false);
        }
        else
        {
            SetRunKey(true);
            DeleteTask();
        }
    }

    /// <summary>Se o .exe mudou de pasta, atualiza o caminho salvo.</summary>
    public static void RefreshPathIfEnabled()
    {
        if (RunKeyExists) SetRunKey(true);
        if (TaskExists && Elevation.IsAdministrator) CreateTask();
    }

    private static void SetRunKey(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName);
    }

    private static void CreateTask()
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        // /IT: só roda com o usuário logado (não pede senha). /DELAY: dá tempo ao OpenRGB subir.
        var code = RunSchtasks(
            $"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RU \"{user}\" /IT /RL HIGHEST /DELAY 0000:15 " +
            $"/TR \"\\\"{ExePath}\\\" {TrayArgument}\"");
        if (code != 0)
            throw new InvalidOperationException($"O Windows não aceitou criar a tarefa de inicialização (código {code}).");
    }

    private static void DeleteTask()
    {
        if (TaskExists) RunSchtasks($"/Delete /F /TN \"{TaskName}\"");
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null) return -1;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(10000) ? process.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }
}

using Microsoft.Win32;

namespace AbaddonRGB;

/// <summary>Liga e desliga a inicialização automática com o Windows (só para o usuário atual).</summary>
internal static class StartupManager
{
    public const string TrayArgument = "--tray";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AbaddonRGB";

    private static string Command => $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\" {TrayArgument}";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName);
    }

    /// <summary>Se o .exe mudou de pasta, atualiza o caminho salvo no registro.</summary>
    public static void RefreshPathIfEnabled()
    {
        if (IsEnabled) SetEnabled(true);
    }
}

using AbaddonRGB.Core;

namespace AbaddonRGB;

internal static class Program
{
    public const string UpdatedArgument = "--updated";

    [STAThread]
    private static void Main(string[] args)
    {
        var startInTray = HasArg(args, StartupManager.TrayArgument);
        var justUpdated = HasArg(args, UpdatedArgument);
        var justElevated = HasArg(args, Elevation.ElevatedArgument);

        // Só uma cópia do programa aberta por vez. Logo depois de uma atualização,
        // a versão antiga ainda pode estar fechando: espera um pouco por ela.
        using var mutex = new Mutex(initiallyOwned: false, "AbaddonRGB_SingleInstance");
        // O mesmo vale para a cópia aberta como administrador: a anterior está fechando.
        if (!TryAcquire(mutex, justUpdated || justElevated ? TimeSpan.FromSeconds(20) : TimeSpan.Zero))
        {
            if (!justUpdated && !justElevated)
            {
                MessageBox.Show("O Abaddon RGB já está aberto. Procure o ícone perto do relógio.",
                    "Abaddon RGB", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        try
        {
            // A versão anterior (".old") só é apagada depois que esta abrir bem (MainForm confirma).
            // Se esta versão já caiu duas vezes logo ao abrir, a anterior volta para o lugar.
            var exe = Environment.ProcessPath;
            if (exe is not null)
            {
                string message = "";
                UpdateGuard.StartDecision decision;
                try { decision = UpdateGuard.OnStartup(AppConfig.Folder, exe, out message); }
                catch { decision = UpdateGuard.StartDecision.Normal; }

                if (decision == UpdateGuard.StartDecision.RolledBack)
                {
                    MessageBox.Show(message, "Abaddon RGB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    // "--updated" faz a cópia restaurada esperar esta fechar e soltar o mutex.
                    try { UpdateGuard.Relaunch(exe, UpdatedArgument + (startInTray ? " " + StartupManager.TrayArgument : "")); }
                    catch { }
                    return;
                }

                if (message.Length > 0)
                    MessageBox.Show(message, "Abaddon RGB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(startInTray, justUpdated,
                sensorFactory: () => new WindowsTemperatureProvider(),
                nativeProbe: NativeProbe.Run));

            // Chegou aqui = fechou normalmente (não caiu): não conta como falha da atualização.
            if (exe is not null) UpdateGuard.MarkCleanExit(AppConfig.Folder, exe);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool HasArg(string[] args, string name) =>
        args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool TryAcquire(Mutex mutex, TimeSpan wait)
    {
        try
        {
            return mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // A cópia anterior fechou sem liberar: agora o mutex é nosso.
            return true;
        }
    }
}

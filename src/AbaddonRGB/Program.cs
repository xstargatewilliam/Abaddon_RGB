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

        // Só uma cópia do programa aberta por vez. Logo depois de uma atualização,
        // a versão antiga ainda pode estar fechando: espera um pouco por ela.
        using var mutex = new Mutex(initiallyOwned: false, "AbaddonRGB_SingleInstance");
        if (!TryAcquire(mutex, justUpdated ? TimeSpan.FromSeconds(20) : TimeSpan.Zero))
        {
            if (!justUpdated)
            {
                MessageBox.Show("O Abaddon RGB já está aberto. Procure o ícone perto do relógio.",
                    "Abaddon RGB", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null) Updater.CleanupOldVersion(exe);

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(startInTray, justUpdated,
                sensorFactory: () => new WindowsTemperatureProvider(),
                nativeProbe: NativeProbe.Run));
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

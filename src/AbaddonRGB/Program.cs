namespace AbaddonRGB;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Só uma cópia do programa aberta por vez.
        using var mutex = new Mutex(initiallyOwned: true, "AbaddonRGB_SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("O Abaddon RGB já está aberto. Procure o ícone perto do relógio.",
                "Abaddon RGB", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var startInTray = args.Any(a => a.Equals(StartupManager.TrayArgument, StringComparison.OrdinalIgnoreCase));

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startInTray));
    }
}

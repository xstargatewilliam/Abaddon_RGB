using System.Runtime.InteropServices;
using AbaddonRGB.Core;
using AbaddonRGB.Core.Automation;
using AbaddonRGB.Core.Engines;
using Microsoft.Win32;

namespace AbaddonRGB;

/// <summary>
/// Janela principal do Abaddon RGB. Este arquivo cuida do ciclo de vida:
/// conexão, aplicação das cores, bandeja e atualização.
/// As telas ficam em MainForm.Pages.cs e as automações em MainForm.Automation.cs.
/// </summary>
internal sealed partial class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly AppLog _log;
    private readonly LightingController _controller;
    private readonly Func<ITemperatureProvider> _sensorFactory;
    private readonly Func<IReadOnlyList<string>> _nativeProbe;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly Version _version = Updater.CurrentVersion(typeof(MainForm).Assembly);

    private readonly NotifyIcon _tray = new();
    private readonly ToolTip _tips = new();
    private readonly Label _status = new();
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private readonly System.Windows.Forms.Timer _healthTimer = new();
    private bool _healthChecking;
    private bool _connecting;

    private bool _allowVisible;
    private bool _exiting;
    private bool _trayHintShown;
    private bool _updating;
    private bool _syncing;
    private Icon? _trayIcon;

    public MainForm(bool startInTray, bool justUpdated,
        Func<ITemperatureProvider> sensorFactory, Func<IReadOnlyList<string>> nativeProbe)
    {
        _allowVisible = !startInTray;
        _sensorFactory = sensorFactory;
        _nativeProbe = nativeProbe;

        _config = AppConfig.Load();
        _log = new AppLog(AppConfig.LogFolder);
        _controller = new LightingController(_config, new ILightingEngine[]
        {
            new OpenRgbEngine(() => (_config.Host, _config.Port)),
            new NativeEngine(),
        }, _log);

        _log.Info($"Abaddon RGB {VersionText} iniciado" + (startInTray ? " (na bandeja)." : "."));

        BuildUi();
        BuildTray();
        SyncColorUi();
        ShowPage(0);

        try { StartupManager.RefreshPathIfEnabled(); } catch { }

        // Garante que a janela tenha "handle" mesmo começando escondida,
        // para podermos atualizar a tela a partir das tarefas em segundo plano.
        _ = Handle;
        Ui.UseDarkTitleBar(Handle);
        _log.LineAdded += OnLogLine;
        foreach (var line in _log.Recent) AppendLog(line);

        StartAutomations();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _ = ConnectAndApplyAsync(applyAfterConnect: _config.ApplyOnStart);

        // Detecta conexões perdidas e dispositivos que voltaram após suspensão
        // ou reinício do servidor, sem depender de mudanças manuais de cor.
        _healthTimer.Interval = 15000;
        _healthTimer.Tick += async (_, _) => await CheckConnectionHealthAsync();
        _healthTimer.Start();

        if (justUpdated)
        {
            _tray.ShowBalloonTip(4000, "Abaddon RGB atualizado",
                $"Agora você está na versão {VersionText}.", ToolTipIcon.Info);
        }

        // Procura atualização 20 segundos depois de abrir e, depois, a cada 6 horas.
        _updateTimer.Interval = (int)TimeSpan.FromHours(6).TotalMilliseconds;
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(manual: false);
        _updateTimer.Start();
        _ = CheckForUpdatesLaterAsync();
    }

    private string VersionText => "v" + _version.ToString(3);

    private void SetStatus(string text)
    {
        _status.Text = text;
        _tips.SetToolTip(_status, text);
    }

    // ---------------------------------------------------------------- conexão e aplicação

    private async Task ConnectAndApplyAsync(bool applyAfterConnect)
    {
        if (_connecting) return;
        _connecting = true;
        try
        {
        // Na inicialização do Windows o OpenRGB pode demorar para subir: tenta por até ~3 minutos.
        const int attempts = 60;
        string? lastError = null;

        for (var attempt = 1; attempt <= attempts && !_exiting; attempt++)
        {
            SetStatus(attempt == 1
                ? "Conectando..."
                : $"Esperando o OpenRGB ({_config.Host}:{_config.Port})... tentativa {attempt} de {attempts}");

            var connected = false;
            try
            {
                connected = await Task.Run(() =>
                {
                    var ok = _controller.Connect(out var error);
                    lastError = error;
                    return ok;
                });
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            if (connected)
            {
                RefreshDeviceViews();
                ApplyScheduleNow(announce: false);
                if (applyAfterConnect)
                    await ApplyAsync();
                else
                    SetStatus($"Conectado: {_controller.Devices.Count} peças.");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        if (_exiting) return;
        RefreshDeviceViews();
        SetStatus("Não encontrei o OpenRGB. Confira se ele está aberto (ou instalado como serviço) " +
                  "com o servidor SDK ligado na porta " + _config.Port + ", e clique em Reconectar na aba Sistema." +
                  (lastError is null ? "" : $" Detalhe: {lastError}"));
        }
        finally { _connecting = false; }
    }

    /// <summary>
    /// Verificação leve em segundo plano: redescobre dispositivos e recupera
    /// motores ausentes. Nunca roda junto a uma aplicação de cor.
    /// </summary>
    private async Task CheckConnectionHealthAsync()
    {
        if (_exiting || _connecting || _healthChecking) return;
        _healthChecking = true;
        var acquired = false;
        try
        {
            acquired = await _busy.WaitAsync(0);
            if (!acquired) return;

            var before = _controller.Devices.Select(d =>
                $"{d.EngineId}:{d.Index}:{d.StableId}:{d.LedCount}").ToArray();
            var wasConnected = _controller.IsConnected;

            var connected = await Task.Run(() => _controller.Connect(out _));
            if (_exiting) return;

            var after = _controller.Devices.Select(d =>
                $"{d.EngineId}:{d.Index}:{d.StableId}:{d.LedCount}").ToArray();
            var changed = !before.SequenceEqual(after) || wasConnected != connected;
            if (!changed) return;

            RefreshDeviceViews();
            _log.Info("Conexão/dispositivos alterados. Sincronizando estado RGB.");
            if (connected && _config.ApplyOnStart)
            {
                // Solta o semáforo antes de chamar ApplyAsync.
                _busy.Release();
                acquired = false;
                ApplyScheduleNow(announce: false);
                await ApplyAsync();
            }
            else if (!connected)
                SetStatus("Nenhum motor de iluminação conectado; reconexão automática ativa.");
        }
        catch (Exception ex) { _log.Warn("Verificação de conexão: " + ex.Message); }
        finally
        {
            if (acquired) try { _busy.Release(); } catch (ObjectDisposedException) { }
            _healthChecking = false;
        }
    }

    /// <summary>Envia as cores atuais para as peças.</summary>
    /// <param name="quiet">Não mexe na barra de status (usado pela automação de temperatura).</param>
    /// <param name="onlyTemperatureTarget">Envia só para a peça que mostra a temperatura.</param>
    private async Task ApplyAsync(bool quiet = false, bool onlyTemperatureTarget = false)
    {
        bool entered;
        try { entered = await _busy.WaitAsync(quiet ? 0 : 3000); }
        catch (ObjectDisposedException) { return; }
        if (!entered) return;

        try
        {
            if (!quiet)
            {
                try { _config.Save(); } catch { }
            }

            if (!_controller.IsConnected)
            {
                var ok = await Task.Run(() => _controller.Connect(out _));
                if (!ok)
                {
                    if (!quiet) SetStatus("Sem conexão com o OpenRGB. Abra a aba Sistema e clique em Reconectar.");
                    return;
                }

                RefreshDeviceViews();
            }

            var temperature = _config.Temperature.Enabled ? _temperatureColor : null;
            var results = await Task.Run(() => _controller.Apply(temperature, onlyTemperatureTarget && temperature is not null));

            if (quiet) return;

            RefreshDeviceViews();
            var okCount = results.Count(r => r.Success);
            var failed = results.Where(r => !r.Success && !r.Skipped).ToList();
            var skipped = results.Count(r => r.Skipped);

            var text = _config.ReadOnly
                ? "Modo somente leitura: nada foi enviado para as peças."
                : $"{_config.Color} aplicada em {okCount} de {results.Count - skipped} peças, brilho {_config.Brightness}%.";
            if (failed.Count > 0)
                text += " Falhou: " + string.Join("; ", failed.Select(f => f.Name + " (" + f.Note + ")")) + ".";
            if (temperature is not null)
                text += " Temperatura ativa.";

            SetStatus(text);
            _log.Info(text);
            _tray.Text = Truncate($"Abaddon RGB — {_config.Color}", 63);
        }
        catch (Exception ex)
        {
            if (!quiet) SetStatus("Erro ao aplicar a cor: " + ex.Message);
            _log.Error("Erro ao aplicar: " + ex.Message);
        }
        finally
        {
            try { _busy.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max);

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume || _exiting || !IsHandleCreated) return;

        // Ao voltar da suspensão, as memórias perdem a cor: reaplica depois de alguns segundos.
        try
        {
            BeginInvoke(new Action(async () =>
            {
                _log.Info("O PC voltou da suspensão: reaplicando as cores.");
                await Task.Delay(TimeSpan.FromSeconds(6));
                if (_exiting) return;
                _temperature.Reset();
                ApplyScheduleNow(announce: false);
                await ApplyAsync();
            }));
        }
        catch { }
    }

    // ---------------------------------------------------------------- log

    private void OnLogLine(string line)
    {
        if (_exiting || !IsHandleCreated) return;
        try
        {
            if (InvokeRequired) BeginInvoke(new Action<string>(AppendLog), line);
            else AppendLog(line);
        }
        catch { }
    }

    private void AppendLog(string line)
    {
        if (_logBox.IsDisposed) return;
        if (_logBox.TextLength > 60000) _logBox.Clear();
        _logBox.AppendText(line + Environment.NewLine);
    }

    // ---------------------------------------------------------------- atualização

    private async Task CheckForUpdatesLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(20));
        if (!_exiting) await CheckForUpdatesAsync(manual: false);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updating || _exiting) return;
        if (!manual && !_config.AutoUpdate) return;

        try
        {
            if (manual) SetStatus("Procurando atualização...");
            var update = await Updater.CheckAsync(_version);

            if (update is null)
            {
                if (manual) SetStatus($"Você já está na versão mais nova ({VersionText}).");
                return;
            }

            var newVersion = "v" + update.Version.ToString(3);
            if (manual)
            {
                var answer = MessageBox.Show(this,
                    $"A versão {newVersion} está disponível (você tem a {VersionText}).\n\n" +
                    "Instalar agora? O programa fecha e abre de novo sozinho, em poucos segundos.",
                    "Abaddon RGB", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    SetStatus($"Atualização {newVersion} disponível. Clique em Procurar atualização quando quiser instalar.");
                    return;
                }
            }

            await InstallUpdateAsync(update);
        }
        catch (Exception ex)
        {
            // Sem internet ou GitHub fora do ar: na checagem automática, fica quieto.
            if (manual) SetStatus("Não consegui procurar atualização: " + ex.Message);
        }
    }

    private async Task InstallUpdateAsync(UpdateInfo update)
    {
        var exe = Environment.ProcessPath;
        if (exe is null || Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Atualização encontrada, mas o programa está rodando em modo de desenvolvimento.");
            return;
        }

        _updating = true;
        try
        {
            var newVersion = "v" + update.Version.ToString(3);
            SetStatus($"Baixando {newVersion}...");
            _log.Info($"Atualizando para {newVersion}.");

            var downloaded = await Updater.DownloadAsync(update);

            var arguments = Program.UpdatedArgument + (Visible ? "" : " " + StartupManager.TrayArgument);
            Updater.InstallAndRestart(downloaded, exe, arguments);
            ExitApp();
        }
        catch (UnauthorizedAccessException)
        {
            SetStatus("Não tenho permissão para trocar o arquivo nesta pasta. Mova o AbaddonRGB.exe " +
                      "para outra pasta (por exemplo, Documentos) e tente de novo.");
        }
        catch (Exception ex)
        {
            SetStatus("A atualização falhou: " + ex.Message);
            _log.Error("Atualização falhou: " + ex.Message);
        }
        finally
        {
            _updating = false;
        }
    }

    // ---------------------------------------------------------------- janela e bandeja

    private void BuildTray()
    {
        _tray.Text = "Abaddon RGB";
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => RebuildTrayMenu();
        _tray.DoubleClick += (_, _) => ShowWindow();
        RebuildTrayMenu();
        _tray.Visible = true;
    }

    private void RebuildTrayMenu()
    {
        var menu = _tray.ContextMenuStrip;
        if (menu is null) return;

        menu.Items.Clear();
        menu.Items.Add("Abrir Abaddon RGB", null, (_, _) => ShowWindow());
        menu.Items.Add("Reaplicar cores", null, async (_, _) => await ApplyAsync());

        if (_config.Profiles.Count > 0)
        {
            var profiles = new ToolStripMenuItem("Perfis");
            foreach (var profile in _config.Profiles.ToArray())
            {
                var name = profile.Name;
                profiles.DropDownItems.Add(name, null, async (_, _) => await ApplyProfileAsync(name));
            }

            menu.Items.Add(profiles);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Procurar atualização", null, async (_, _) => await CheckForUpdatesAsync(manual: true));
        menu.Items.Add("Sair", null, (_, _) => ExitApp());
    }

    private void UpdateTrayIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var fill = new SolidBrush(color);
            using var border = new Pen(Color.FromArgb(20, 20, 24), 3f);
            graphics.FillEllipse(fill, 3, 3, 26, 26);
            graphics.DrawEllipse(border, 3, 3, 26, 26);
        }

        var newIcon = Icon.FromHandle(bitmap.GetHicon());
        var oldIcon = _trayIcon;

        _tray.Icon = newIcon;
        _trayIcon = newIcon;

        if (oldIcon is not null)
        {
            DestroyIcon(oldIcon.Handle);
            oldIcon.Dispose();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) => BackdropPanel.Draw(this, e.Graphics);

    protected override void SetVisibleCore(bool value)
    {
        // Quando inicia com o Windows, a janela começa escondida (só o ícone da bandeja).
        if (!_allowVisible)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }

        base.SetVisibleCore(value);
    }

    private void ShowWindow()
    {
        _allowVisible = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // O "X" só esconde a janela; para fechar de verdade, use "Sair" no ícone da bandeja.
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(3000, "Abaddon RGB",
                    "Continuo aqui perto do relógio. Para fechar de vez, clique com o botão direito e escolha Sair.",
                    ToolTipIcon.Info);
            }

            return;
        }

        base.OnFormClosing(e);
    }

    private void ExitApp()
    {
        _exiting = true;
        _tray.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _exiting = true;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _log.LineAdded -= OnLogLine;
            StopAutomations();
            _updateTimer.Dispose();
            _healthTimer.Dispose();
            _tray.Dispose();
            _tips.Dispose();
            _controller.Dispose();
            _busy.Dispose();
            if (_trayIcon is not null)
            {
                DestroyIcon(_trayIcon.Handle);
                _trayIcon.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}

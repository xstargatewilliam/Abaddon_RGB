using System.Runtime.InteropServices;
using AbaddonRGB.Core;

namespace AbaddonRGB;

/// <summary>
/// Janela principal do Abaddon RGB: escolhe uma cor, aplica em tudo e fica na bandeja do Windows.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly System.Drawing.Color Background = System.Drawing.Color.FromArgb(30, 30, 36);
    private static readonly System.Drawing.Color Surface = System.Drawing.Color.FromArgb(42, 42, 50);
    private static readonly System.Drawing.Color TextColor = System.Drawing.Color.FromArgb(235, 235, 240);
    private static readonly System.Drawing.Color MutedText = System.Drawing.Color.FromArgb(160, 160, 175);

    private static readonly (string Name, string Hex)[] Presets =
    {
        ("Vermelho", "#FF0000"), ("Laranja", "#FF6A00"), ("Amarelo", "#FFD000"),
        ("Verde", "#00FF40"), ("Ciano", "#00E5FF"), ("Azul", "#0040FF"),
        ("Azul Abaddon", "#3A6EA5"), ("Roxo", "#8A2BE2"), ("Rosa", "#FF2E9A"),
        ("Branco", "#FFFFFF"),
    };

    private readonly AppConfig _config;
    private readonly LightingService _service = new();
    private readonly SemaphoreSlim _busy = new(1, 1);

    private readonly Panel _preview = new();
    private readonly TextBox _hexBox = new();
    private readonly TrackBar _brightness = new();
    private readonly Label _brightnessLabel = new();
    private readonly Button _applyButton = new();
    private readonly CheckBox _startupCheck = new();
    private readonly ListBox _devices = new();
    private readonly Label _status = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolTip _tips = new();
    private readonly CheckBox _autoUpdateCheck = new();
    private readonly System.Windows.Forms.Timer _updateTimer = new();
    private readonly Version _version = Updater.CurrentVersion(typeof(MainForm).Assembly);
    private bool _updating;

    private System.Drawing.Color _current;
    private bool _allowVisible;
    private bool _exiting;
    private bool _trayHintShown;
    private Icon? _trayIcon;

    public MainForm(bool startInTray, bool justUpdated = false)
    {
        _allowVisible = !startInTray;
        _config = AppConfig.Load();

        AppConfig.TryParseHex(_config.Color, out var r, out var g, out var b);
        _current = System.Drawing.Color.FromArgb(r, g, b);

        BuildUi();
        BuildTray();
        UpdateColorUi();

        try { StartupManager.RefreshPathIfEnabled(); } catch { }

        // Garante que a janela tenha "handle" mesmo começando escondida,
        // para podermos atualizar a tela a partir das tarefas em segundo plano.
        _ = Handle;
        _ = ConnectAndApplyAsync(applyAfterConnect: _config.ApplyOnStart);

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

    // ---------------------------------------------------------------- interface

    private void BuildUi()
    {
        Text = "Abaddon RGB";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Background;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var title = new Label
        {
            Text = "Abaddon RGB  " + VersionText,
            Font = new Font("Segoe UI Semibold", 16f),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
        };
        var subtitle = new Label
        {
            Text = "Uma cor para o PC inteiro: placa-mãe, cooler, memórias, teclado e mouse.",
            ForeColor = MutedText,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        // Linha da cor: amostra grande + hexadecimal + botão de escolher.
        var colorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };

        _preview.Size = new Size(96, 96);
        _preview.Cursor = Cursors.Hand;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.Margin = new Padding(0, 0, 14, 0);
        _preview.Click += (_, _) => PickColor();
        _tips.SetToolTip(_preview, "Clique para escolher qualquer cor");

        var colorSide = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
        };
        colorSide.Controls.Add(new Label { Text = "Cor (hexadecimal)", ForeColor = MutedText, AutoSize = true });

        _hexBox.Width = 130;
        _hexBox.CharacterCasing = CharacterCasing.Upper;
        _hexBox.MaxLength = 7;
        _hexBox.BackColor = Surface;
        _hexBox.ForeColor = TextColor;
        _hexBox.BorderStyle = BorderStyle.FixedSingle;
        _hexBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            CommitHex(apply: true);
        };
        _hexBox.Leave += (_, _) => CommitHex(apply: false);
        colorSide.Controls.Add(_hexBox);

        var pickButton = MakeButton("Escolher cor...");
        pickButton.Margin = new Padding(0, 8, 0, 0);
        pickButton.Click += (_, _) => PickColor();
        colorSide.Controls.Add(pickButton);

        colorRow.Controls.Add(_preview);
        colorRow.Controls.Add(colorSide);

        // Cores rápidas.
        var presetsLabel = new Label { Text = "Cores rápidas", ForeColor = MutedText, AutoSize = true };
        var presetsRow = new FlowLayoutPanel
        {
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 2, 0, 10),
        };
        foreach (var (name, hex) in Presets)
        {
            AppConfig.TryParseHex(hex, out var pr, out var pg, out var pb);
            var swatchColor = System.Drawing.Color.FromArgb(pr, pg, pb);
            var swatch = new Button
            {
                Size = new Size(36, 36),
                BackColor = swatchColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 6, 6),
            };
            swatch.FlatAppearance.BorderColor = Surface;
            swatch.FlatAppearance.BorderSize = 2;
            _tips.SetToolTip(swatch, name);
            swatch.Click += async (_, _) =>
            {
                _current = swatchColor;
                UpdateColorUi();
                await ApplyAsync();
            };
            presetsRow.Controls.Add(swatch);
        }

        // Brilho.
        _brightnessLabel.AutoSize = true;
        _brightnessLabel.ForeColor = MutedText;
        _brightness.Minimum = 0;
        _brightness.Maximum = 100;
        _brightness.TickFrequency = 10;
        _brightness.SmallChange = 5;
        _brightness.LargeChange = 10;
        _brightness.Width = 420;
        _brightness.Value = Math.Clamp(_config.Brightness, 0, 100);
        _brightness.ValueChanged += (_, _) => UpdateBrightnessLabel();
        // Só aplica quando a pessoa solta o controle, para não mandar dezenas de comandos.
        _brightness.MouseUp += async (_, _) => await ApplyAsync();
        _brightness.KeyUp += async (_, _) => await ApplyAsync();
        UpdateBrightnessLabel();

        // Botões principais.
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 8) };
        _applyButton.Text = "Aplicar em tudo";
        StyleButton(_applyButton);
        _applyButton.Font = new Font("Segoe UI Semibold", 10f);
        _applyButton.Click += async (_, _) => await ApplyAsync();

        var reconnectButton = MakeButton("Reconectar");
        reconnectButton.Click += async (_, _) => await ConnectAndApplyAsync(applyAfterConnect: false);

        var configButton = MakeButton("Abrir configurações");
        configButton.Click += (_, _) => OpenConfigFolder();
        _tips.SetToolTip(configButton, "Abre a pasta do config.json (correções de cor, porta do OpenRGB etc.)");

        actions.Controls.Add(_applyButton);
        actions.Controls.Add(reconnectButton);
        actions.Controls.Add(configButton);

        _startupCheck.Text = "Iniciar com o Windows (abre escondido e reaplica a última cor)";
        _startupCheck.AutoSize = true;
        _startupCheck.ForeColor = TextColor;
        _startupCheck.Margin = new Padding(0, 0, 0, 10);
        try { _startupCheck.Checked = StartupManager.IsEnabled; } catch { }
        _startupCheck.CheckedChanged += (_, _) =>
        {
            try
            {
                StartupManager.SetEnabled(_startupCheck.Checked);
            }
            catch (Exception ex)
            {
                SetStatus("Não consegui alterar a inicialização automática: " + ex.Message);
            }
        };

        // Atualizações.
        var updateRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        var updateButton = MakeButton("Procurar atualização");
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);
        _autoUpdateCheck.Text = "Atualizar automaticamente";
        _autoUpdateCheck.AutoSize = true;
        _autoUpdateCheck.ForeColor = TextColor;
        _autoUpdateCheck.Margin = new Padding(4, 6, 0, 0);
        _autoUpdateCheck.Checked = _config.AutoUpdate;
        _autoUpdateCheck.CheckedChanged += (_, _) =>
        {
            _config.AutoUpdate = _autoUpdateCheck.Checked;
            try { _config.Save(); } catch { }
        };
        _tips.SetToolTip(_autoUpdateCheck, "Quando sair uma versão nova no GitHub, o programa baixa, instala e reabre sozinho.");
        updateRow.Controls.Add(updateButton);
        updateRow.Controls.Add(_autoUpdateCheck);

        // Lista de dispositivos.
        var devicesLabel = new Label { Text = "Dispositivos", ForeColor = MutedText, AutoSize = true };
        _devices.Width = 440;
        _devices.Height = 150;
        _devices.BackColor = Surface;
        _devices.ForeColor = TextColor;
        _devices.BorderStyle = BorderStyle.None;
        _devices.IntegralHeight = false;
        _devices.Margin = new Padding(0, 2, 0, 8);

        _status.AutoSize = true;
        _status.MaximumSize = new Size(440, 0);
        _status.ForeColor = MutedText;

        root.Controls.Add(title);
        root.Controls.Add(subtitle);
        root.Controls.Add(colorRow);
        root.Controls.Add(presetsLabel);
        root.Controls.Add(presetsRow);
        root.Controls.Add(_brightnessLabel);
        root.Controls.Add(_brightness);
        root.Controls.Add(actions);
        root.Controls.Add(_startupCheck);
        root.Controls.Add(updateRow);
        root.Controls.Add(devicesLabel);
        root.Controls.Add(_devices);
        root.Controls.Add(_status);

        Controls.Add(root);
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir Abaddon RGB", null, (_, _) => ShowWindow());
        menu.Items.Add("Reaplicar cor", null, async (_, _) => await ApplyAsync());
        menu.Items.Add("Procurar atualização", null, async (_, _) => await CheckForUpdatesAsync(manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitApp());

        _tray.Text = "Abaddon RGB";
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.Visible = true;
    }

    private Button MakeButton(string text)
    {
        var button = new Button { Text = text };
        StyleButton(button);
        return button;
    }

    private static void StyleButton(Button button)
    {
        button.AutoSize = true;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Surface;
        button.ForeColor = TextColor;
        button.Padding = new Padding(8, 4, 8, 4);
        button.Margin = new Padding(0, 0, 8, 0);
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(70, 70, 82);
    }

    private void UpdateBrightnessLabel() => _brightnessLabel.Text = $"Brilho: {_brightness.Value}%";

    private void UpdateColorUi()
    {
        _preview.BackColor = _current;
        _hexBox.Text = AppConfig.ToHex(_current.R, _current.G, _current.B);
        UpdateTrayIcon();
    }

    private void UpdateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var fill = new SolidBrush(_current);
            using var border = new Pen(System.Drawing.Color.FromArgb(20, 20, 24), 3f);
            graphics.FillEllipse(fill, 3, 3, 26, 26);
            graphics.DrawEllipse(border, 3, 3, 26, 26);
        }

        var handle = bitmap.GetHicon();
        var newIcon = Icon.FromHandle(handle);
        var oldIcon = _trayIcon;

        _tray.Icon = newIcon;
        Icon = newIcon;
        _trayIcon = newIcon;

        if (oldIcon is not null)
        {
            DestroyIcon(oldIcon.Handle);
            oldIcon.Dispose();
        }
    }

    private void SetStatus(string text) => _status.Text = text;

    // ---------------------------------------------------------------- ações

    private void PickColor()
    {
        using var dialog = new ColorDialog { FullOpen = true, AnyColor = true, Color = _current };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _current = dialog.Color;
        UpdateColorUi();
        _ = ApplyAsync();
    }

    private void CommitHex(bool apply)
    {
        if (AppConfig.TryParseHex(_hexBox.Text, out var r, out var g, out var b))
        {
            _current = System.Drawing.Color.FromArgb(r, g, b);
            UpdateColorUi();
            if (apply) _ = ApplyAsync();
        }
        else
        {
            SetStatus("Cor inválida. Use o formato #RRGGBB, por exemplo #3A6EA5.");
            UpdateColorUi();
        }
    }

    private async Task ConnectAndApplyAsync(bool applyAfterConnect)
    {
        // Na inicialização do Windows o OpenRGB pode demorar para subir: tenta por até ~3 minutos.
        const int attempts = 60;
        string? lastError = null;

        for (var attempt = 1; attempt <= attempts && !_exiting; attempt++)
        {
            SetStatus(attempt == 1
                ? $"Conectando ao OpenRGB ({_config.Host}:{_config.Port})..."
                : $"Esperando o OpenRGB ({_config.Host}:{_config.Port})... tentativa {attempt} de {attempts}");

            var host = _config.Host;
            var port = _config.Port;
            var connected = await Task.Run(() =>
            {
                var ok = _service.TryConnect(host, port, out var error);
                lastError = error;
                return ok;
            });

            if (connected)
            {
                await RefreshDevicesAsync();
                if (applyAfterConnect)
                    await ApplyAsync();
                else
                    SetStatus("Conectado ao OpenRGB.");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        if (_exiting) return;
        SetStatus("Não encontrei o OpenRGB. Confira se ele está aberto (ou instalado como serviço) " +
                  "com o servidor SDK ligado na porta " + _config.Port + ", e clique em Reconectar." +
                  (lastError is null ? "" : $" Detalhe: {lastError}"));
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            var names = await Task.Run(() => _service.GetDeviceNames());
            _devices.Items.Clear();
            foreach (var name in names)
                _devices.Items.Add("•  " + name);
        }
        catch (Exception ex)
        {
            SetStatus("Conectei, mas não consegui listar os dispositivos: " + ex.Message);
        }
    }

    private async Task ApplyAsync()
    {
        if (!await _busy.WaitAsync(0))
            return; // já tem uma aplicação em andamento

        try
        {
            _applyButton.Enabled = false;

            var r = _current.R;
            var g = _current.G;
            var b = _current.B;
            var brightness = _brightness.Value;

            _config.Color = AppConfig.ToHex(r, g, b);
            _config.Brightness = brightness;
            try { _config.Save(); } catch { }

            if (!_service.IsConnected)
            {
                var host = _config.Host;
                var port = _config.Port;
                var ok = await Task.Run(() => _service.TryConnect(host, port, out _));
                if (!ok)
                {
                    SetStatus("Sem conexão com o OpenRGB. Abra o OpenRGB e clique em Reconectar.");
                    return;
                }
            }

            SetStatus("Aplicando cor...");
            var corrections = _config.Corrections.ToList();
            var results = await Task.Run(() => _service.ApplySolidColor(r, g, b, brightness, corrections));

            _devices.Items.Clear();
            foreach (var result in results)
            {
                var line = result.Success ? "✔  " + result.Name : "✖  " + result.Name;
                if (result.CorrectedZones > 0) line += "  (ordem de cor corrigida)";
                if (!result.Success && result.Error is not null) line += "  — " + result.Error;
                _devices.Items.Add(line);
            }

            var okCount = results.Count(x => x.Success);
            SetStatus($"{_config.Color} aplicada em {okCount} de {results.Count} dispositivos, brilho {brightness}%.");
            _tray.Text = $"Abaddon RGB — {_config.Color}";
        }
        catch (Exception ex)
        {
            SetStatus("Erro ao aplicar a cor: " + ex.Message);
        }
        finally
        {
            _applyButton.Enabled = true;
            _busy.Release();
        }
    }

    private void OpenConfigFolder()
    {
        try
        {
            _config.Save();
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{AppConfig.FilePath}\"");
        }
        catch (Exception ex)
        {
            SetStatus("Não consegui abrir a pasta: " + ex.Message);
        }
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
            _tray.Text = $"Abaddon RGB — baixando {newVersion}";

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
        }
        finally
        {
            _updating = false;
        }
    }

    // ---------------------------------------------------------------- janela e bandeja

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
            _updateTimer.Dispose();
            _tray.Dispose();
            _tips.Dispose();
            _service.Dispose();
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

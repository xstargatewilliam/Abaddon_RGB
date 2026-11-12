using AbaddonRGB.Core;
using AbaddonRGB.Core.Engines;

namespace AbaddonRGB;

/// <summary>As telas: Cores, Perfis, Automações e Sistema.</summary>
internal sealed partial class MainForm
{
    private static readonly (string Name, string Hex)[] Presets =
    {
        ("Vermelho", "#FF0000"), ("Laranja", "#FF6A00"), ("Amarelo", "#FFD000"),
        ("Verde", "#00FF40"), ("Ciano", "#00E5FF"), ("Azul", "#0040FF"),
        ("Azul Abaddon", "#3A6EA5"), ("Roxo", "#8A2BE2"), ("Rosa", "#FF2E9A"),
        ("Branco", "#FFFFFF"),
    };

    private static readonly string[] PageNames = { "Cores", "Perfis", "Automações", "Sistema" };

    /// <summary>O que um item da árvore de peças representa.</summary>
    private sealed class NodeTag
    {
        public string Device = "";
        public string Zone = "";
    }

    private readonly List<Button> _navButtons = new();
    private readonly List<FlowLayoutPanel> _pages = new();
    private int _currentPage;

    // Cores
    private readonly Panel _preview = new();
    private readonly TextBox _hexBox = new();
    private readonly TrackBar _brightness = new();
    private readonly Label _brightnessLabel = new();
    private readonly TreeView _tree = new();
    private Button _toggleControlButton = null!;

    // Perfis
    private ListBox _profileList = null!;
    private readonly TextBox _profileName = new();

    // Sistema
    private readonly Label _engineLabel = new();
    private readonly TextBox _logBox = new();
    private readonly CheckBox _startupCheck = Ui.Check("Iniciar com o Windows (abre escondido e reaplica as cores)");
    private readonly CheckBox _autoUpdateCheck = Ui.Check("Atualizar automaticamente");
    private readonly CheckBox _readOnlyCheck = Ui.Check("Modo somente leitura (detecta as peças, mas não envia nada)");

    private void BuildUi()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Abaddon RGB";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Ui.Background;
        ForeColor = Ui.Text;
        Font = new Font("Segoe UI", 9.75f);
        Padding = new Padding(16, 12, 16, 12);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var title = new Label
        {
            Text = "Abaddon RGB  " + VersionText,
            Font = new Font("Segoe UI Semibold", 15f),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        var nav = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        for (var i = 0; i < PageNames.Length; i++)
        {
            var index = i;
            var button = Ui.Button(PageNames[i]);
            button.Margin = new Padding(0, 0, 6, 0);
            button.Click += (_, _) => ShowPage(index);
            _navButtons.Add(button);
            nav.Controls.Add(button);
        }

        var host = new Panel
        {
            Size = new Size(Ui.ContentWidth + 34, 520),
            Margin = new Padding(0, 0, 0, 8),
        };
        _pages.Add(BuildColorsPage());
        _pages.Add(BuildProfilesPage());
        _pages.Add(BuildAutomationPage());
        _pages.Add(BuildSystemPage());
        foreach (var page in _pages) host.Controls.Add(page);

        _status.AutoSize = false;
        _status.Size = new Size(Ui.ContentWidth + 34, 54);
        _status.ForeColor = Ui.Muted;

        root.Controls.Add(title);
        root.Controls.Add(nav);
        root.Controls.Add(host);
        root.Controls.Add(_status);
        Controls.Add(root);

        ResumeLayout(true);
    }

    private void ShowPage(int index)
    {
        _currentPage = index;
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
            _navButtons[i].BackColor = i == index ? Ui.Accent : Ui.Surface;
        }

        if (index == 1) RefreshProfileList();
        if (index == 2) OnAutomationPageShown();
        if (index == 3) RefreshEngineLabel();
    }

    // ---------------------------------------------------------------- Cores

    private FlowLayoutPanel BuildColorsPage()
    {
        var page = Ui.Page();

        _preview.Size = new Size(76, 76);
        _preview.Cursor = Cursors.Hand;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.Margin = new Padding(0, 0, 14, 0);
        _preview.Click += (_, _) => PickMainColor();
        _tips.SetToolTip(_preview, "Clique para escolher qualquer cor");

        _hexBox.Width = 110;
        _hexBox.CharacterCasing = CharacterCasing.Upper;
        _hexBox.MaxLength = 7;
        _hexBox.BackColor = Ui.Surface;
        _hexBox.ForeColor = Ui.Text;
        _hexBox.BorderStyle = BorderStyle.FixedSingle;
        _hexBox.Margin = new Padding(0, 2, 0, 8);
        _hexBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            CommitHex(apply: true);
        };
        _hexBox.Leave += (_, _) => CommitHex(apply: false);

        var pickButton = Ui.Button("Escolher cor...");
        pickButton.Click += (_, _) => PickMainColor();

        var colorSide = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        colorSide.Controls.Add(Ui.Label("Cor geral (hexadecimal)", muted: true));
        colorSide.Controls.Add(_hexBox);
        colorSide.Controls.Add(pickButton);
        page.Controls.Add(Ui.Row(_preview, colorSide));

        var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        foreach (var (name, hex) in Presets)
        {
            var color = Ui.ToDrawing(RgbColor.ParseOr(hex, RgbColor.Black));
            var swatch = Ui.Swatch(color, 34);
            _tips.SetToolTip(swatch, name);
            swatch.Click += async (_, _) =>
            {
                SetMainColor(color);
                await ApplyAsync();
            };
            presets.Controls.Add(swatch);
        }

        page.Controls.Add(Ui.Label("Cores rápidas", muted: true));
        page.Controls.Add(presets);

        _brightnessLabel.AutoSize = true;
        _brightnessLabel.ForeColor = Ui.Muted;
        _brightness.Minimum = 0;
        _brightness.Maximum = 100;
        _brightness.TickFrequency = 10;
        _brightness.SmallChange = 5;
        _brightness.LargeChange = 10;
        _brightness.Width = Ui.ContentWidth - 10;
        _brightness.Margin = new Padding(0);
        _brightness.ValueChanged += (_, _) =>
        {
            _brightnessLabel.Text = $"Brilho: {_brightness.Value}%";
            if (!_syncing) _config.Brightness = _brightness.Value;
        };
        // Só aplica quando a pessoa solta o controle, para não mandar dezenas de comandos.
        _brightness.MouseUp += async (_, _) => await ApplyAsync();
        _brightness.KeyUp += async (_, _) => await ApplyAsync();
        page.Controls.Add(_brightnessLabel);
        page.Controls.Add(_brightness);

        var applyButton = Ui.Button("Aplicar em tudo");
        applyButton.Font = new Font("Segoe UI Semibold", 9.75f);
        applyButton.BackColor = Ui.Accent;
        applyButton.Click += async (_, _) => await ApplyAsync();
        page.Controls.Add(Ui.Row(applyButton));

        page.Controls.Add(Ui.Label("Peças — selecione uma peça ou zona para dar uma cor só dela", muted: true));

        _tree.Size = new Size(Ui.ContentWidth - 10, 172);
        _tree.BackColor = Ui.Surface;
        _tree.ForeColor = Ui.Text;
        _tree.LineColor = Ui.Border;
        _tree.BorderStyle = BorderStyle.None;
        _tree.HideSelection = false;
        _tree.ShowNodeToolTips = true;
        _tree.Margin = new Padding(0, 2, 0, 8);
        _tree.AfterSelect += (_, _) => UpdateToggleControlText();
        page.Controls.Add(_tree);

        var ownColor = Ui.Button("Cor própria...");
        ownColor.Click += async (_, _) => await SetOwnColorAsync();
        var followMain = Ui.Button("Seguir a cor geral");
        followMain.Click += async (_, _) => await ClearOwnColorAsync();
        _toggleControlButton = Ui.Button("Não controlar esta peça");
        _toggleControlButton.Click += async (_, _) => await ToggleControlAsync();
        _tips.SetToolTip(_toggleControlButton, "Se uma peça der problema, o Abaddon RGB para de enviar comandos para ela.");
        page.Controls.Add(Ui.Row(ownColor, followMain, _toggleControlButton));

        return page;
    }

    private void SetMainColor(Color color)
    {
        _config.Color = Ui.FromDrawing(color).ToHex();
        SyncColorUi();
    }

    /// <summary>Atualiza a tela a partir das configurações (cor, brilho, ícone).</summary>
    private void SyncColorUi()
    {
        _syncing = true;
        try
        {
            var color = Ui.ToDrawing(RgbColor.ParseOr(_config.Color, new RgbColor(58, 110, 165)));
            _preview.BackColor = color;
            _hexBox.Text = _config.Color;
            _brightness.Value = Ui.Clamp(_config.Brightness, 0, 100);
            _brightnessLabel.Text = $"Brilho: {_brightness.Value}%";
            UpdateTrayIcon(color);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void PickMainColor()
    {
        using var dialog = new ColorDialog { FullOpen = true, AnyColor = true, Color = _preview.BackColor };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetMainColor(dialog.Color);
        _ = ApplyAsync();
    }

    private void CommitHex(bool apply)
    {
        if (_syncing) return;
        if (RgbColor.TryParse(_hexBox.Text, out var color))
        {
            var changed = color.ToHex() != _config.Color;
            SetMainColor(Ui.ToDrawing(color));
            if (apply || changed) _ = ApplyAsync();
        }
        else
        {
            SetStatus("Cor inválida. Use o formato #RRGGBB, por exemplo #3A6EA5.");
            SyncColorUi();
        }
    }

    /// <summary>Redesenha tudo que lista as peças: árvore, alvos da temperatura e status dos motores.</summary>
    private void RefreshDeviceViews()
    {
        RefreshTree();
        RefreshTemperatureTargets();
        RefreshEngineLabel();
    }

    private void RefreshTree()
    {
        var selected = _tree.SelectedNode?.Tag as NodeTag;
        var devices = _controller.Devices;

        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        TreeNode? toSelect = null;

        foreach (var device in devices)
        {
            var node = new TreeNode(DescribeTarget(device.Name, "", device.Name))
            {
                Tag = new NodeTag { Device = device.Name },
                ToolTipText = $"{device.LedCount} LEDs, motor {EngineName(device.EngineId)}",
            };
            if (_config.IsDisabled(device.Name)) node.ForeColor = Ui.Muted;

            if (device.Zones.Count > 1)
            {
                foreach (var zone in device.Zones)
                {
                    var child = new TreeNode(DescribeTarget(device.Name, zone.Name, zone.Name))
                    {
                        Tag = new NodeTag { Device = device.Name, Zone = zone.Name },
                        ToolTipText = $"{zone.LedCount} LEDs",
                    };
                    node.Nodes.Add(child);
                    if (IsSame(selected, device.Name, zone.Name)) toSelect = child;
                }
            }

            _tree.Nodes.Add(node);
            if (IsSame(selected, device.Name, "")) toSelect = node;
        }

        if (devices.Count == 0)
            _tree.Nodes.Add(new TreeNode("Nenhuma peça detectada ainda.") { ForeColor = Ui.Muted });

        _tree.ExpandAll();
        if (toSelect is not null) _tree.SelectedNode = toSelect;
        else if (_tree.Nodes.Count > 0) _tree.Nodes[0].EnsureVisible();
        _tree.EndUpdate();
        UpdateToggleControlText();
    }

    private static bool IsSame(NodeTag? tag, string device, string zone) =>
        tag is not null &&
        string.Equals(tag.Device, device, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(tag.Zone, zone, StringComparison.OrdinalIgnoreCase);

    private string DescribeTarget(string device, string zone, string label)
    {
        var text = label;
        var own = _config.Overrides.FirstOrDefault(o => o.SameTarget(device, zone));
        if (own is not null) text += "  —  cor própria " + own.Color;
        if (zone.Length == 0 && _config.IsDisabled(device)) text += "  —  não controlada";
        return text;
    }

    private string EngineName(string engineId) =>
        engineId == OpenRgbEngine.EngineId ? "OpenRGB" : engineId == NativeEngine.EngineId ? "nativo" : engineId;

    private NodeTag? SelectedTarget()
    {
        if (_tree.SelectedNode?.Tag is NodeTag tag) return tag;
        SetStatus("Primeiro selecione uma peça ou zona na lista.");
        return null;
    }

    private void UpdateToggleControlText()
    {
        var tag = _tree.SelectedNode?.Tag as NodeTag;
        _toggleControlButton.Text = tag is not null && _config.IsDisabled(tag.Device)
            ? "Voltar a controlar"
            : "Não controlar esta peça";
    }

    private async Task SetOwnColorAsync()
    {
        var tag = SelectedTarget();
        if (tag is null) return;

        var current = _config.Overrides.FirstOrDefault(o => o.SameTarget(tag.Device, tag.Zone));
        var start = current is not null ? Ui.ToDrawing(RgbColor.ParseOr(current.Color, RgbColor.Black)) : _preview.BackColor;

        using var dialog = new ColorDialog { FullOpen = true, AnyColor = true, Color = start };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _config.Overrides.RemoveAll(o => o.SameTarget(tag.Device, tag.Zone));
        _config.Overrides.Add(new TargetColor
        {
            Device = tag.Device,
            Zone = tag.Zone,
            Color = Ui.FromDrawing(dialog.Color).ToHex(),
        });
        await ApplyAsync();
    }

    private async Task ClearOwnColorAsync()
    {
        var tag = SelectedTarget();
        if (tag is null) return;

        // Na peça inteira, limpa também as cores próprias das zonas dela.
        if (tag.Zone.Length == 0)
            _config.Overrides.RemoveAll(o => string.Equals(o.Device, tag.Device, StringComparison.OrdinalIgnoreCase));
        else
            _config.Overrides.RemoveAll(o => o.SameTarget(tag.Device, tag.Zone));
        await ApplyAsync();
    }

    private async Task ToggleControlAsync()
    {
        var tag = SelectedTarget();
        if (tag is null) return;

        if (_config.IsDisabled(tag.Device))
        {
            _config.DisabledDevices.RemoveAll(d => string.Equals(d, tag.Device, StringComparison.OrdinalIgnoreCase));
            _log.Info($"{tag.Device}: voltou a ser controlada.");
        }
        else
        {
            _config.DisabledDevices.Add(tag.Device);
            _log.Info($"{tag.Device}: não será mais controlada.");
        }

        await ApplyAsync();
    }

    // ---------------------------------------------------------------- Perfis

    private FlowLayoutPanel BuildProfilesPage()
    {
        var page = Ui.Page();
        page.Controls.Add(Ui.Label("Um perfil guarda a cor geral, o brilho e as cores próprias de cada peça. " +
                                   "Os perfis também aparecem no menu do ícone perto do relógio.", muted: true));

        _profileList = Ui.List(230);
        _profileList.Margin = new Padding(0, 8, 0, 8);
        _profileList.DoubleClick += async (_, _) => await ApplySelectedProfileAsync();
        page.Controls.Add(_profileList);

        var apply = Ui.Button("Aplicar");
        apply.BackColor = Ui.Accent;
        apply.Click += async (_, _) => await ApplySelectedProfileAsync();
        var overwrite = Ui.Button("Atualizar com as cores atuais");
        overwrite.Click += (_, _) =>
        {
            var name = SelectedProfileName();
            if (name is null) return;
            _controller.SaveProfile(name);
            RefreshProfileList();
            SetStatus($"Perfil \"{name}\" atualizado com as cores atuais.");
        };
        var delete = Ui.Button("Excluir");
        delete.Click += (_, _) => DeleteSelectedProfile();
        page.Controls.Add(Ui.Row(apply, overwrite, delete));

        page.Controls.Add(Ui.Section("Novo perfil"));
        _profileName.Width = 200;
        _profileName.MaxLength = 30;
        _profileName.BackColor = Ui.Surface;
        _profileName.ForeColor = Ui.Text;
        _profileName.BorderStyle = BorderStyle.FixedSingle;
        _profileName.Margin = new Padding(0, 4, 8, 0);
        var save = Ui.Button("Salvar as cores atuais");
        save.Click += (_, _) => SaveNewProfile();
        page.Controls.Add(Ui.Row(Ui.InlineLabel("Nome"), _profileName, save));

        return page;
    }

    private void RefreshProfileList()
    {
        var selected = SelectedProfileName(quiet: true);
        _profileList.BeginUpdate();
        _profileList.Items.Clear();
        foreach (var profile in _config.Profiles)
        {
            var extra = profile.Overrides.Count > 0 ? $", {profile.Overrides.Count} cor(es) própria(s)" : "";
            _profileList.Items.Add($"{profile.Name}   —   {profile.Color}, brilho {profile.Brightness}%{extra}");
        }

        _profileList.EndUpdate();
        var index = _config.Profiles.FindIndex(p => p.Name == selected);
        if (index >= 0) _profileList.SelectedIndex = index;
        RefreshRuleProfiles();
    }

    private string? SelectedProfileName(bool quiet = false)
    {
        var index = _profileList.SelectedIndex;
        if (index >= 0 && index < _config.Profiles.Count) return _config.Profiles[index].Name;
        if (!quiet) SetStatus("Primeiro selecione um perfil na lista.");
        return null;
    }

    private async Task ApplySelectedProfileAsync()
    {
        var name = SelectedProfileName();
        if (name is not null) await ApplyProfileAsync(name);
    }

    private async Task ApplyProfileAsync(string name)
    {
        if (!_controller.LoadProfile(name))
        {
            SetStatus($"Não encontrei o perfil \"{name}\".");
            return;
        }

        SyncColorUi();
        await ApplyAsync();
        SetStatus($"Perfil \"{name}\" aplicado. " + _status.Text);
    }

    private void SaveNewProfile()
    {
        var name = _profileName.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus("Dê um nome ao perfil antes de salvar.");
            return;
        }

        _controller.SaveProfile(name);
        _profileName.Clear();
        RefreshProfileList();
        SetStatus($"Perfil \"{name}\" salvo com as cores atuais.");
    }

    private void DeleteSelectedProfile()
    {
        var name = SelectedProfileName();
        if (name is null) return;

        var used = _config.Schedule.Rules.Count(r => string.Equals(r.Profile, name, StringComparison.OrdinalIgnoreCase));
        var question = $"Excluir o perfil \"{name}\"?" +
                       (used > 0 ? $"\n\nEle é usado em {used} regra(s) de horário, que também serão removidas." : "");
        if (MessageBox.Show(this, question, "Abaddon RGB", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _config.Profiles.RemoveAll(p => p.Name == name);
        _config.Schedule.Rules.RemoveAll(r => string.Equals(r.Profile, name, StringComparison.OrdinalIgnoreCase));
        try { _config.Save(); } catch { }
        _log.Info($"Perfil \"{name}\" excluído.");
        RefreshProfileList();
        RefreshRuleList();
        SetStatus($"Perfil \"{name}\" excluído.");
    }

    // ---------------------------------------------------------------- Sistema

    private FlowLayoutPanel BuildSystemPage()
    {
        var page = Ui.Page();

        page.Controls.Add(Ui.Section("Motores"));
        _engineLabel.AutoSize = true;
        _engineLabel.MaximumSize = new Size(Ui.ContentWidth - 10, 0);
        _engineLabel.ForeColor = Ui.Text;
        _engineLabel.Margin = new Padding(0, 0, 0, 8);
        page.Controls.Add(_engineLabel);

        var reconnect = Ui.Button("Reconectar");
        reconnect.Click += async (_, _) => await ConnectAndApplyAsync(applyAfterConnect: false);
        var redetect = Ui.Button("Detectar peças de novo");
        redetect.Click += async (_, _) =>
        {
            try
            {
                await Task.Run(() => _controller.RefreshDevices());
                RefreshDeviceViews();
                SetStatus($"{_controller.Devices.Count} peças detectadas.");
            }
            catch (Exception ex)
            {
                SetStatus("Não consegui detectar as peças: " + ex.Message);
            }
        };
        var probe = Ui.Button("Detecção nativa (teste)");
        probe.Click += async (_, _) => await RunNativeProbeAsync();
        _tips.SetToolTip(probe, "Procura as peças direto no Windows, sem o OpenRGB. Só lê, não envia nada.");
        page.Controls.Add(Ui.Row(reconnect, redetect, probe));

        _readOnlyCheck.Checked = _config.ReadOnly;
        _readOnlyCheck.CheckedChanged += (_, _) =>
        {
            _config.ReadOnly = _readOnlyCheck.Checked;
            try { _config.Save(); } catch { }
            _log.Info("Modo somente leitura " + (_config.ReadOnly ? "ligado." : "desligado."));
            if (!_config.ReadOnly) _ = ApplyAsync();
        };
        page.Controls.Add(_readOnlyCheck);

        try { _startupCheck.Checked = StartupManager.IsEnabled; } catch { }
        _startupCheck.CheckedChanged += (_, _) =>
        {
            try { StartupManager.SetEnabled(_startupCheck.Checked); }
            catch (Exception ex) { SetStatus("Não consegui alterar a inicialização automática: " + ex.Message); }
        };
        page.Controls.Add(_startupCheck);

        var updateButton = Ui.Button("Procurar atualização");
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);
        _autoUpdateCheck.Checked = _config.AutoUpdate;
        _autoUpdateCheck.Margin = new Padding(4, 6, 0, 0);
        _autoUpdateCheck.CheckedChanged += (_, _) =>
        {
            _config.AutoUpdate = _autoUpdateCheck.Checked;
            try { _config.Save(); } catch { }
        };
        page.Controls.Add(Ui.Row(updateButton, _autoUpdateCheck));

        page.Controls.Add(Ui.Section("Registro"));
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.WordWrap = false;
        _logBox.Size = new Size(Ui.ContentWidth - 10, 170);
        _logBox.BackColor = Ui.Surface;
        _logBox.ForeColor = Ui.Text;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.Font = new Font("Consolas", 8.5f);
        _logBox.Margin = new Padding(0, 2, 0, 8);
        page.Controls.Add(_logBox);

        var openConfig = Ui.Button("Abrir configurações");
        openConfig.Click += (_, _) => OpenInExplorer(AppConfig.FilePath, select: true);
        var openLogs = Ui.Button("Abrir pasta de registros");
        openLogs.Click += (_, _) => OpenInExplorer(AppConfig.LogFolder, select: false);
        page.Controls.Add(Ui.Row(openConfig, openLogs));

        return page;
    }

    private void RefreshEngineLabel()
    {
        var lines = new List<string>();
        foreach (var engine in _controller.GetEngineStatus())
        {
            string text;
            if (engine.Id == NativeEngine.EngineId)
                text = "Nativo: ainda sem drivers próprios. Os primeiros (teclado e mouse Logitech) entram na Fase 1.";
            else if (engine.Connected)
                text = $"{engine.Name}: conectado, {engine.Devices} peças.";
            else
                text = $"{engine.Name}: desconectado" + (engine.Error is null ? "." : $" ({engine.Error}).");
            lines.Add(text);
        }

        _engineLabel.Text = string.Join(Environment.NewLine, lines);
    }

    private async Task RunNativeProbeAsync()
    {
        SetStatus("Procurando peças direto no Windows (somente leitura)...");
        try
        {
            var lines = await Task.Run(() => _nativeProbe());
            _log.Info("Detecção nativa (somente leitura):");
            foreach (var line in lines) _log.Info("   " + line);
            SetStatus($"Detecção nativa concluída: {lines.Count} linha(s) no registro abaixo.");
        }
        catch (Exception ex)
        {
            _log.Error("Detecção nativa falhou: " + ex.Message);
            SetStatus("A detecção nativa falhou: " + ex.Message);
        }
    }

    private void OpenInExplorer(string path, bool select)
    {
        try
        {
            if (select) _config.Save();
            else Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start("explorer.exe", select ? $"/select,\"{path}\"" : $"\"{path}\"");
        }
        catch (Exception ex)
        {
            SetStatus("Não consegui abrir a pasta: " + ex.Message);
        }
    }
}

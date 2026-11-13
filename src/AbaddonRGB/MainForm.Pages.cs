using AbaddonRGB.Core;
using AbaddonRGB.Core.Engines;

namespace AbaddonRGB;

/// <summary>As telas: Início (cores), Perfis, Automações e Sistema.</summary>
internal sealed partial class MainForm
{
    private static readonly (string Name, string Hex)[] Presets =
    {
        ("Vermelho", "#FF0000"), ("Laranja", "#FF6A00"), ("Amarelo", "#FFD000"),
        ("Verde", "#00FF40"), ("Ciano", "#00E5FF"), ("Azul", "#0040FF"),
        ("Roxo Abaddon", "#A020F0"), ("Magenta", "#FF2DAA"), ("Azul Abaddon", "#3A6EA5"),
        ("Branco", "#FFFFFF"),
    };

    private static readonly (string Name, string Icon)[] PageInfo =
    {
        ("Início", "home"), ("Perfis", "folder"), ("Automações", "clock"), ("Sistema", "gear"),
    };

    // Medidas da janela (em 96 dpi; o Windows ajusta para outras escalas).
    private const int SideWidth = 196;
    private const int ContentLeft = 216;
    private const int ContentWidth = 848;
    private const int HostTop = 60;
    private const int HostHeight = 580;

    /// <summary>O que um item da árvore de peças representa.</summary>
    private sealed class NodeTag
    {
        public string Device = "";
        public string DeviceId = "";
        public string Zone = "";
    }

    private readonly List<NavButton> _navButtons = new();
    private readonly List<Panel> _pages = new();
    private readonly HeaderPanel _header = new();
    private readonly StatusPill _pill = new();
    private readonly Label _cpuLabel = new();
    private readonly Label _gpuLabel = new();
    private int _currentPage;

    // Início
    private readonly ColorWheel _wheel = new();
    private readonly Panel _preview = new();
    private readonly TextBox _hexBox = Ui.TextBox(96);
    private readonly NeonSlider _brightness = new();
    private readonly Label _brightnessLabel = new();
    private readonly PcPreview _pcPreview = new();
    private readonly TreeView _tree = new();
    private NeonButton _toggleControlButton = null!;

    // Perfis
    private ListBox _profileList = null!;
    private readonly TextBox _profileName = Ui.TextBox(210);

    // Sistema
    private readonly Label _engineLabel = new();
    private readonly TextBox _logBox = new();
    private readonly CheckBox _startupCheck = Ui.Check("Iniciar com o Windows (escondido, reaplicando as cores)");
    private readonly CheckBox _autoUpdateCheck = Ui.Check("Atualizar automaticamente");
    private readonly CheckBox _gammaCheck = Ui.Check("Corrigir a mistura das cores nos LEDs");
    private readonly CheckBox _readOnlyCheck = Ui.Check("Modo somente leitura (não envia nada às peças)");

    private void BuildUi()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Abaddon RGB";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(ContentLeft + ContentWidth + 16, 700);
        BackColor = Ui.Background;
        ForeColor = Ui.Text;
        Font = Ui.Body();
        DoubleBuffered = true;

        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("AbaddonRGB.Assets.app.ico");
            if (stream is not null) Icon = new Icon(stream);
        }
        catch { }

        Controls.Add(BuildSidebar());

        _header.Bounds = new Rectangle(ContentLeft, 10, ContentWidth, 44);
        _header.Subtitle = VersionText;
        Controls.Add(_header);

        var host = new BackdropPanel { Bounds = new Rectangle(ContentLeft, HostTop, ContentWidth, HostHeight) };
        _pages.Add(BuildColorsPage());
        _pages.Add(BuildProfilesPage());
        _pages.Add(BuildAutomationPage());
        _pages.Add(BuildSystemPage());
        foreach (var page in _pages) host.Controls.Add(page);
        Controls.Add(host);

        Controls.Add(BuildFooter());
        ResumeLayout(true);
    }

    private static BackdropPanel NewPage() => new() { Dock = DockStyle.Fill, Visible = false };

    private Panel BuildSidebar()
    {
        var side = new Panel { Bounds = new Rectangle(0, 0, SideWidth, 700), BackColor = Ui.Sidebar };
        side.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var pen = new Pen(Ui.Border))
                g.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height);

            // "ABADDON" em branco e "RGB" em magenta, centralizados.
            using var font = Ui.Heading(14f, bold: true);
            using var white = new SolidBrush(Ui.Text);
            using var pink = new SolidBrush(Ui.Accent2);
            var first = g.MeasureString("ABADDON", font);
            var second = g.MeasureString("RGB", font);
            var x = (side.Width - (first.Width + second.Width - 6)) / 2f;
            g.DrawString("ABADDON", font, white, x, 142);
            g.DrawString("RGB", font, pink, x + first.Width - 6, 142);
        };

        var logo = new PictureBox
        {
            Bounds = new Rectangle((SideWidth - 116) / 2, 20, 116, 116),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Ui.Asset("logo.png"),
            BackColor = Ui.Sidebar,
        };
        side.Controls.Add(logo);

        for (var i = 0; i < PageInfo.Length; i++)
        {
            var index = i;
            var button = new NavButton(PageInfo[i].Name, Ui.Icon(PageInfo[i].Icon))
            {
                Location = new Point(0, 190 + i * 48),
                Size = new Size(SideWidth - 1, 46),
            };
            button.Click += (_, _) => ShowPage(index);
            _navButtons.Add(button);
            side.Controls.Add(button);
        }

        _pill.Location = new Point(12, 700 - 46 - 14);
        _pill.Set(false, "procurando as peças...");
        side.Controls.Add(_pill);
        return side;
    }

    private Panel BuildFooter()
    {
        var footer = new Panel
        {
            Bounds = new Rectangle(ContentLeft, HostTop + HostHeight + 10, ContentWidth, 40),
            BackColor = Ui.Panel,
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Ui.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, footer.Width - 1, footer.Height - 1);
            var icon = Ui.Icon("thermo");
            if (icon is not null)
            {
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(icon, footer.Width - 214, 11, 18, 18);
            }
        };

        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Bounds = new Rectangle(12, 11, ContentWidth - 240, 20);
        _status.ForeColor = Ui.Muted;
        _status.BackColor = Ui.Panel;
        footer.Controls.Add(_status);

        foreach (var (label, x, text) in new[] { (_cpuLabel, ContentWidth - 192, "CPU --"), (_gpuLabel, ContentWidth - 98, "GPU --") })
        {
            label.AutoSize = false;
            label.Bounds = new Rectangle(x, 11, 90, 20);
            label.ForeColor = Ui.Text;
            label.BackColor = Ui.Panel;
            label.Text = text;
            footer.Controls.Add(label);
        }

        return footer;
    }

    private void ShowPage(int index)
    {
        _currentPage = index;
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
            _navButtons[i].Active = i == index;
        }

        _header.Title = PageInfo[index].Name;
        if (index == 1) RefreshProfileList();
        if (index == 2) OnAutomationPageShown();
        if (index == 3) RefreshEngineLabel();
    }

    // ---------------------------------------------------------------- Início

    private Panel BuildColorsPage()
    {
        var page = NewPage();

        // ---- Cor geral
        var colorCard = new Card("Cor geral", 0, 0, 468, 330);

        _wheel.Bounds = new Rectangle(16, 44, 208, 208);
        _wheel.ColorChanging += (_, _) => SetMainColor(_wheel.SelectedColor, fromWheel: true);
        _wheel.ColorCommitted += async (_, _) => await ApplyAsync();
        colorCard.Controls.Add(_wheel);

        var hexLabel = Ui.Label("HEXADECIMAL", muted: true);
        hexLabel.Font = Ui.Body(8f, semibold: true);
        hexLabel.Location = new Point(244, 46);
        colorCard.Controls.Add(hexLabel);

        _preview.Bounds = new Rectangle(244, 68, 28, 28);
        _preview.Cursor = Cursors.Hand;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.Click += (_, _) => PickMainColor();
        _tips.SetToolTip(_preview, "Clique para abrir o seletor completo de cores");
        colorCard.Controls.Add(_preview);

        _hexBox.Location = new Point(280, 70);
        _hexBox.Margin = new Padding(0);
        _hexBox.CharacterCasing = CharacterCasing.Upper;
        _hexBox.MaxLength = 7;
        _hexBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            CommitHex(apply: true);
        };
        _hexBox.Leave += (_, _) => CommitHex(apply: false);
        colorCard.Controls.Add(_hexBox);

        var pickButton = Ui.Button("Mais cores...");
        pickButton.Location = new Point(244, 108);
        pickButton.Click += (_, _) => PickMainColor();
        _tips.SetToolTip(pickButton, "A roda escolhe cores vivas. Para tons escuros ou exatos, use este seletor ou o hexadecimal.");
        colorCard.Controls.Add(pickButton);

        _brightnessLabel.AutoSize = true;
        _brightnessLabel.ForeColor = Ui.Muted;
        _brightnessLabel.Font = Ui.Body(8f, semibold: true);
        _brightnessLabel.Location = new Point(244, 160);
        colorCard.Controls.Add(_brightnessLabel);

        _brightness.Bounds = new Rectangle(238, 180, 214, 26);
        _brightness.ValueChanged += (_, _) =>
        {
            _brightnessLabel.Text = $"BRILHO  {_brightness.Value}%";
            if (_syncing) return;
            _config.Brightness = _brightness.Value;
            _pcPreview.SetLight(_preview.BackColor, _config.Brightness);
        };
        // Só aplica quando a pessoa solta o controle, para não mandar dezenas de comandos.
        _brightness.Committed += async (_, _) => await ApplyAsync();
        colorCard.Controls.Add(_brightness);

        var applyButton = Ui.Button("Aplicar em tudo", primary: true);
        applyButton.Location = new Point(244, 218);
        applyButton.Click += async (_, _) => await ApplyAsync();
        colorCard.Controls.Add(applyButton);

        var x = 18;
        foreach (var (name, hex) in Presets)
        {
            var color = Ui.ToDrawing(RgbColor.ParseOr(hex, RgbColor.Black));
            var swatch = Ui.Swatch(color, 34);
            swatch.Location = new Point(x, 272);
            _tips.SetToolTip(swatch, name);
            swatch.Click += async (_, _) =>
            {
                SetMainColor(color);
                await ApplyAsync();
            };
            colorCard.Controls.Add(swatch);
            x += 43;
        }

        page.Controls.Add(colorCard);

        // ---- Prévia do PC
        var pcCard = new Card("Seu PC", 480, 0, 368, 330);
        _pcPreview.Bounds = new Rectangle(34, 30, 300, 292);
        pcCard.Controls.Add(_pcPreview);
        page.Controls.Add(pcCard);

        // ---- Peças
        var partsCard = new Card("Peças", 0, 342, 848, 238);
        var body = partsCard.Body;
        body.Controls.Add(Ui.Label("Selecione uma peça ou zona para dar uma cor só dela.", muted: true));

        var icons = new ImageList { ImageSize = new Size(18, 18), ColorDepth = ColorDepth.Depth32Bit };
        foreach (var key in new[] { "board", "ram", "keyboard", "mouse", "fan", "bolt" })
        {
            var icon = Ui.Icon(key);
            if (icon is not null) icons.Images.Add(key, icon);
        }

        _tree.Size = new Size(partsCard.InnerWidth, 112);
        _tree.ImageList = icons;
        _tree.BackColor = Ui.Field;
        _tree.ForeColor = Ui.Text;
        _tree.LineColor = Ui.Border;
        _tree.BorderStyle = BorderStyle.None;
        _tree.HideSelection = false;
        _tree.ShowNodeToolTips = true;
        _tree.ItemHeight = 22;
        _tree.Margin = new Padding(0, 2, 0, 10);
        Ui.DarkScrollbars(_tree);
        _tree.AfterSelect += (_, _) => UpdateToggleControlText();
        body.Controls.Add(_tree);

        var ownColor = Ui.Button("Cor própria...");
        ownColor.Click += async (_, _) => await SetOwnColorAsync();
        var followMain = Ui.Button("Seguir a cor geral");
        followMain.Click += async (_, _) => await ClearOwnColorAsync();
        _toggleControlButton = Ui.Button("Não controlar esta peça");
        _toggleControlButton.Click += async (_, _) => await ToggleControlAsync();
        _tips.SetToolTip(_toggleControlButton, "Se uma peça der problema, o Abaddon RGB para de enviar comandos para ela.");
        var swapButton = Ui.Button("Inverter vermelho/verde");
        swapButton.Click += async (_, _) => await ToggleRedGreenSwapAsync();
        _tips.SetToolTip(swapButton, "Use quando a peça mostra verde no lugar de vermelho (e rosa no lugar de verde). Clique de novo para desfazer.");
        var identify = Ui.Button("Identificar");
        identify.Click += async (_, _) => await IdentifyAsync();
        _tips.SetToolTip(identify, "Faz a peça ou zona selecionada piscar, para você ver qual luz do PC ela controla.");
        body.Controls.Add(Ui.Row(identify, ownColor, followMain, swapButton, _toggleControlButton));

        page.Controls.Add(partsCard);
        return page;
    }

    private static string IconFor(string deviceName)
    {
        var name = deviceName.ToUpperInvariant();
        if (name.IndexOf("DRAM", StringComparison.Ordinal) >= 0 || name.IndexOf("RAM", StringComparison.Ordinal) >= 0) return "ram";
        if (name.IndexOf("KEYBOARD", StringComparison.Ordinal) >= 0) return "keyboard";
        if (name.IndexOf("MOUSE", StringComparison.Ordinal) >= 0) return "mouse";
        if (name.IndexOf("ASROCK", StringComparison.Ordinal) >= 0 || name.IndexOf("B450", StringComparison.Ordinal) >= 0) return "board";
        return "bolt";
    }

    private void SetMainColor(Color color, bool fromWheel = false)
    {
        _config.Color = Ui.FromDrawing(color).ToHex();
        SyncColorUi(moveWheel: !fromWheel);
    }

    /// <summary>Atualiza a tela a partir das configurações (cor, brilho, ícone).</summary>
    private void SyncColorUi(bool moveWheel = true)
    {
        _syncing = true;
        try
        {
            var color = Ui.ToDrawing(RgbColor.ParseOr(_config.Color, new RgbColor(58, 110, 165)));
            _preview.BackColor = color;
            _hexBox.Text = _config.Color;
            _brightness.Value = Ui.Clamp(_config.Brightness, 0, 100);
            _brightnessLabel.Text = $"BRILHO  {_brightness.Value}%";
            if (moveWheel) _wheel.ShowColor(color);
            _pcPreview.SetLight(color, _config.Brightness);
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
            var node = new TreeNode(DescribeTarget(device.Name, "", device.Name, device.StableId))
            {
                Tag = new NodeTag { Device = device.Name, DeviceId = device.StableId },
                ToolTipText = $"{device.LedCount} LEDs, motor {EngineName(device.EngineId)}",
                ImageKey = IconFor(device.Name),
                SelectedImageKey = IconFor(device.Name),
            };
            if (_config.IsDisabled(device.Name, device.StableId)) node.ForeColor = Ui.Muted;

            if (device.Zones.Count > 1)
            {
                foreach (var zone in device.Zones)
                {
                    var child = new TreeNode(DescribeTarget(device.Name, zone.Name, zone.Name, device.StableId))
                    {
                        Tag = new NodeTag { Device = device.Name, DeviceId = device.StableId, Zone = zone.Name },
                        ToolTipText = $"{zone.LedCount} LEDs",
                        ImageKey = "bolt",
                        SelectedImageKey = "bolt",
                    };
                    node.Nodes.Add(child);
                    if (IsSame(selected, device.Name, zone.Name)) toSelect = child;
                }
            }

            _tree.Nodes.Add(node);
            if (IsSame(selected, device.Name, "")) toSelect = node;
        }

        if (devices.Count == 0)
            _tree.Nodes.Add(new TreeNode("Nenhuma peça detectada ainda.") { ForeColor = Ui.Muted, ImageKey = "bolt", SelectedImageKey = "bolt" });

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

    private string DescribeTarget(string device, string zone, string label, string stableId)
    {
        var text = label;
        var own = _config.Overrides.FirstOrDefault(o => o.SameTarget(device, zone, stableId));
        if (own is not null) text += "  —  cor própria " + own.Color;
        if (zone.Length > 0 && IsSwapped(device, zone, stableId)) text += "  —  vermelho/verde invertidos";
        if (zone.Length == 0 && _config.IsDisabled(device, stableId)) text += "  —  não controlada";
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
        _toggleControlButton.Text = tag is not null && _config.IsDisabled(tag.Device, tag.DeviceId)
            ? "Voltar a controlar"
            : "Não controlar esta peça";
    }

    private async Task SetOwnColorAsync()
    {
        var tag = SelectedTarget();
        if (tag is null) return;

        var current = _config.Overrides.FirstOrDefault(o => o.SameTarget(tag.Device, tag.Zone, tag.DeviceId));
        var start = current is not null ? Ui.ToDrawing(RgbColor.ParseOr(current.Color, RgbColor.Black)) : _preview.BackColor;

        using var dialog = new ColorDialog { FullOpen = true, AnyColor = true, Color = start };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _config.Overrides.RemoveAll(o => o.SameTarget(tag.Device, tag.Zone, tag.DeviceId));
        _config.Overrides.Add(new TargetColor
        {
            Device = tag.Device,
            DeviceId = tag.DeviceId,
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
            _config.Overrides.RemoveAll(o =>
                string.IsNullOrWhiteSpace(o.DeviceId)
                    ? string.Equals(o.Device, tag.Device, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(o.DeviceId, tag.DeviceId, StringComparison.OrdinalIgnoreCase));
        else
            _config.Overrides.RemoveAll(o => o.SameTarget(tag.Device, tag.Zone, tag.DeviceId));
        await ApplyAsync();
    }

    private bool _identifying;

    /// <summary>Pisca a peça ou zona selecionada (branco e apagado) e depois restaura as cores.</summary>
    private async Task IdentifyAsync()
    {
        var tag = SelectedTarget();
        if (tag is null || _identifying) return;
        if (_config.ReadOnly)
        {
            SetStatus("O modo somente leitura está ligado: desligue na aba Sistema para identificar as peças.");
            return;
        }

        _identifying = true;
        var where = tag.Zone.Length > 0 ? $"{tag.Device} › {tag.Zone}" : tag.Device;
        SetStatus($"Piscando: {where}. Veja qual luz do PC está piscando.");

        // Cor temporária: não é salva no config.json.
        var flash = new TargetColor { Device = tag.Device, DeviceId = tag.DeviceId, Zone = tag.Zone, Color = "#FFFFFF" };
        try
        {
            _config.Overrides.Add(flash);
            for (var i = 0; i < 4 && !_exiting; i++)
            {
                flash.Color = "#FFFFFF";
                await ApplyAsync(quiet: true);
                await Task.Delay(600);
                flash.Color = "#000000";
                await ApplyAsync(quiet: true);
                await Task.Delay(450);
            }
        }
        finally
        {
            _config.Overrides.Remove(flash);
            _identifying = false;
        }

        await ApplyAsync();
        SetStatus($"Pronto. A zona que piscou foi: {where}.");
    }

    private bool IsSwapped(string device, string zone, string stableId) =>
        _config.Corrections.Any(c => c.Matches(device, zone, stableId) && !string.Equals(c.Order, ColorOrder.Default, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Liga ou desliga a troca de vermelho com verde na zona selecionada
    /// (ou em todas as zonas, se a peça inteira estiver selecionada).
    /// </summary>
    private async Task ToggleRedGreenSwapAsync()
    {
        var tag = SelectedTarget();
        if (tag is null) return;

        var device = _controller.Devices.FirstOrDefault(d => tag.DeviceId.Length > 0
            ? string.Equals(d.StableId, tag.DeviceId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(d.Name, tag.Device, StringComparison.OrdinalIgnoreCase));
        if (device is null) return;

        var zones = tag.Zone.Length > 0
            ? new List<string> { tag.Zone }
            : device.Zones.Select(z => z.Name).ToList();
        if (zones.Count == 0) return;

        // Se alguma zona escolhida já está invertida, o clique desfaz; senão, inverte todas.
        var undo = zones.Any(z => IsSwapped(tag.Device, z, tag.DeviceId));
        foreach (var zone in zones)
        {
            // Só remove correções desta peça e desta zona exata (as regras gerais por nome parcial ficam).
            _config.Corrections.RemoveAll(c => c.Matches(tag.Device, zone, tag.DeviceId) &&
                                               string.Equals(c.Zone, zone, StringComparison.OrdinalIgnoreCase));
            if (!undo)
                _config.Corrections.Add(new ZoneCorrection { Device = tag.Device, DeviceId = tag.DeviceId, Zone = zone, Order = "GRB" });
        }

        var where = tag.Zone.Length > 0 ? $"{tag.Device} › {tag.Zone}" : tag.Device;
        _log.Info($"{where}: vermelho/verde " + (undo ? "voltaram ao normal." : "invertidos."));
        await ApplyAsync();
    }

    private async Task ToggleControlAsync()
    {
        var tag = SelectedTarget();
        if (tag is null) return;

        if (_config.IsDisabled(tag.Device, tag.DeviceId))
        {
            _config.DisabledDevices.RemoveAll(d => string.Equals(d, tag.Device, StringComparison.OrdinalIgnoreCase));
            _config.DisabledDeviceIds.RemoveAll(id => string.Equals(id, tag.DeviceId, StringComparison.OrdinalIgnoreCase));
            _log.Info($"{tag.Device}: voltou a ser controlada.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(tag.DeviceId))
                _config.DisabledDeviceIds.Add(tag.DeviceId);
            else
                _config.DisabledDevices.Add(tag.Device);
            _log.Info($"{tag.Device}: não será mais controlada.");
        }

        await ApplyAsync();
    }

    // ---------------------------------------------------------------- Perfis

    private Panel BuildProfilesPage()
    {
        var page = NewPage();

        var listCard = new Card("Perfis salvos", 0, 0, 520, 580);
        var body = listCard.Body;
        body.Controls.Add(Ui.Label("Um perfil guarda a cor geral, o brilho e as cores próprias de cada peça.",
            muted: true, maxWidth: listCard.InnerWidth));

        _profileList = Ui.List(listCard.InnerWidth, 410);
        _profileList.Margin = new Padding(0, 6, 0, 12);
        _profileList.DoubleClick += async (_, _) => await ApplySelectedProfileAsync();
        body.Controls.Add(_profileList);

        var apply = Ui.Button("Aplicar", primary: true);
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
        body.Controls.Add(Ui.Row(apply, overwrite, delete));
        page.Controls.Add(listCard);

        var newCard = new Card("Novo perfil", 532, 0, 316, 170);
        _profileName.MaxLength = 30;
        var save = Ui.Button("Salvar as cores atuais", primary: true);
        save.Click += (_, _) => SaveNewProfile();
        newCard.Body.Controls.Add(Ui.Row(Ui.InlineLabel("Nome"), _profileName));
        newCard.Body.Controls.Add(save);
        page.Controls.Add(newCard);

        var tipCard = new Card("Dica", 532, 182, 316, 200);
        tipCard.Body.Controls.Add(Ui.Label(
            "Os perfis também aparecem no menu do ícone perto do relógio: clique com o botão direito nele para trocar sem abrir a janela.\n\n" +
            "Na aba Automações, um perfil pode entrar sozinho num horário.",
            muted: true, maxWidth: tipCard.InnerWidth));
        page.Controls.Add(tipCard);

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

    private Panel BuildSystemPage()
    {
        var page = NewPage();

        var enginesCard = new Card("Motores", 0, 0, 418, 232);
        _engineLabel.AutoSize = true;
        _engineLabel.MaximumSize = new Size(enginesCard.InnerWidth, 0);
        _engineLabel.ForeColor = Ui.Text;
        _engineLabel.Margin = new Padding(0, 0, 0, 12);
        enginesCard.Body.Controls.Add(_engineLabel);

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
        enginesCard.Body.Controls.Add(Ui.Row(reconnect, redetect));
        enginesCard.Body.Controls.Add(Ui.Row(probe));
        page.Controls.Add(enginesCard);

        var optionsCard = new Card("Opções", 430, 0, 418, 232);
        _gammaCheck.Checked = _config.GammaCorrection;
        _gammaCheck.CheckedChanged += async (_, _) =>
        {
            _config.GammaCorrection = _gammaCheck.Checked;
            _log.Info("Correção de mistura de cores " + (_config.GammaCorrection ? "ligada." : "desligada."));
            await ApplyAsync();
        };
        _tips.SetToolTip(_gammaCheck, "Deixa as cores misturadas (azul com um pouco de verde, laranja, lilás) mais parecidas com a tela.");

        _readOnlyCheck.Checked = _config.ReadOnly;
        _readOnlyCheck.CheckedChanged += (_, _) =>
        {
            _config.ReadOnly = _readOnlyCheck.Checked;
            try { _config.Save(); } catch { }
            _log.Info("Modo somente leitura " + (_config.ReadOnly ? "ligado." : "desligado."));
            if (!_config.ReadOnly) _ = ApplyAsync();
        };

        try { _startupCheck.Checked = StartupManager.IsEnabled; } catch { }
        _startupCheck.CheckedChanged += (_, _) =>
        {
            try { StartupManager.SetEnabled(_startupCheck.Checked); }
            catch (Exception ex) { SetStatus("Não consegui alterar a inicialização automática: " + ex.Message); }
        };

        _autoUpdateCheck.Checked = _config.AutoUpdate;
        _autoUpdateCheck.CheckedChanged += (_, _) =>
        {
            _config.AutoUpdate = _autoUpdateCheck.Checked;
            try { _config.Save(); } catch { }
        };

        var updateButton = Ui.Button("Procurar atualização");
        updateButton.Margin = new Padding(0, 8, 0, 0);
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);

        optionsCard.Body.Controls.Add(_startupCheck);
        optionsCard.Body.Controls.Add(_autoUpdateCheck);
        optionsCard.Body.Controls.Add(_gammaCheck);
        optionsCard.Body.Controls.Add(_readOnlyCheck);
        optionsCard.Body.Controls.Add(updateButton);
        page.Controls.Add(optionsCard);

        var logCard = new Card("Registro", 0, 244, 848, 336);
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.WordWrap = false;
        _logBox.Size = new Size(logCard.InnerWidth, 230);
        _logBox.BackColor = Ui.Field;
        _logBox.ForeColor = Ui.Text;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.Font = new Font("Consolas", 8.75f);
        _logBox.Margin = new Padding(0, 2, 0, 10);
        Ui.DarkScrollbars(_logBox);
        logCard.Body.Controls.Add(_logBox);

        var openConfig = Ui.Button("Abrir configurações");
        openConfig.Click += (_, _) => OpenInExplorer(AppConfig.FilePath, select: true);
        var openLogs = Ui.Button("Abrir pasta de registros");
        openLogs.Click += (_, _) => OpenInExplorer(AppConfig.LogFolder, select: false);
        logCard.Body.Controls.Add(Ui.Row(openConfig, openLogs));
        page.Controls.Add(logCard);

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

        _engineLabel.Text = string.Join(Environment.NewLine + Environment.NewLine, lines);

        var count = _controller.Devices.Count;
        _pill.Set(_controller.IsConnected, _controller.IsConnected ? $"{count} peças online" : "abra a aba Sistema");
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

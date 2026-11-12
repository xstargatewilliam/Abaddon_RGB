using AbaddonRGB.Core;
using AbaddonRGB.Core.Automation;

namespace AbaddonRGB;

/// <summary>Automações: troca de perfil por horário e cor conforme a temperatura.</summary>
internal sealed partial class MainForm
{
    private sealed class SensorItem
    {
        public string Id = "";
        public string Text = "";
        public override string ToString() => Text;
    }

    private sealed class TargetItem
    {
        public string Device = "";
        public string DeviceId = "";
        public string Zone = "";
        public string Text = "";
        public override string ToString() => Text;
    }

    private readonly TemperatureAutomation _temperature = new();
    private readonly System.Windows.Forms.Timer _scheduleTimer = new();
    private readonly System.Windows.Forms.Timer _temperatureTimer = new();

    private ITemperatureProvider? _sensors;
    private bool _sensorsFailed;
    private bool _readingSensors;
    private bool _sensorListFilled;
    private IReadOnlyList<TemperatureSensor> _lastSensors = Array.Empty<TemperatureSensor>();
    private RgbColor? _temperatureColor;
    private DateTime _lastScheduleCheck = DateTime.Now;

    // Horário
    private readonly CheckBox _scheduleCheck = Ui.Check("Trocar de perfil por horário");
    private ListBox _ruleList = null!;
    private NumericUpDown _ruleHour = null!;
    private NumericUpDown _ruleMinute = null!;
    private ComboBox _ruleProfile = null!;

    // Temperatura
    private readonly CheckBox _temperatureCheck = Ui.Check("Mudar a cor conforme a temperatura");
    private ComboBox _sensorCombo = null!;
    private ComboBox _targetCombo = null!;
    private NumericUpDown _coldTemp = null!;
    private NumericUpDown _hotTemp = null!;
    private Button _coldColor = null!;
    private Button _hotColor = null!;
    private readonly Label _temperatureLive = new();

    private Panel BuildAutomationPage()
    {
        var page = NewPage();
        var scheduleCard = new Card("Por horário", 0, 0, 848, 250);
        var temperatureCard = new Card("Por temperatura", 0, 262, 848, 318);
        var target = scheduleCard.Body;

        // ---- Por horário
        _scheduleCheck.Checked = _config.Schedule.Enabled;
        _scheduleCheck.CheckedChanged += async (_, _) =>
        {
            if (_syncing) return;
            _config.Schedule.Enabled = _scheduleCheck.Checked;
            SaveConfigQuietly();
            _log.Info("Automação por horário " + (_config.Schedule.Enabled ? "ligada." : "desligada."));
            if (_config.Schedule.Enabled && ApplyScheduleNow(announce: true))
                await ApplyAsync();
        };
        target.Controls.Add(_scheduleCheck);

        _ruleList = Ui.List(scheduleCard.InnerWidth, 100);
        target.Controls.Add(_ruleList);

        _ruleHour = Ui.Number(0, 23, 22, 46);
        _ruleMinute = Ui.Number(0, 59, 0, 46);
        _ruleProfile = Ui.Combo(170);
        var addRule = Ui.Button("Adicionar");
        addRule.Click += async (_, _) => await AddRuleAsync();
        var removeRule = Ui.Button("Remover");
        removeRule.Click += (_, _) => RemoveSelectedRule();
        target.Controls.Add(Ui.Row(Ui.InlineLabel("Às"), _ruleHour, Ui.InlineLabel(":"), _ruleMinute,
            Ui.InlineLabel("aplicar"), _ruleProfile, addRule, removeRule));

        // ---- Por temperatura
        target = temperatureCard.Body;
        _temperatureCheck.Checked = _config.Temperature.Enabled;
        _temperatureCheck.CheckedChanged += async (_, _) =>
        {
            if (_syncing) return;
            _config.Temperature.Enabled = _temperatureCheck.Checked;
            SaveConfigQuietly();
            _log.Info("Automação por temperatura " + (_config.Temperature.Enabled ? "ligada." : "desligada."));
            await RestartTemperatureAsync();
        };
        target.Controls.Add(_temperatureCheck);

        _sensorCombo = Ui.Combo(440);
        _sensorCombo.SelectedIndexChanged += async (_, _) =>
        {
            if (_syncing || !(_sensorCombo.SelectedItem is SensorItem item)) return;
            _config.Temperature.SensorId = item.Id;
            SaveConfigQuietly();
            await RestartTemperatureAsync();
        };
        var refreshSensors = Ui.Button("Atualizar");
        refreshSensors.Click += (_, _) => FillSensorCombo();
        target.Controls.Add(Ui.Row(Ui.InlineLabel("Sensor"), _sensorCombo, refreshSensors));

        var settings = _config.Temperature;
        _coldTemp = Ui.Number(0, 119, settings.ColdTemp);
        _hotTemp = Ui.Number(1, 120, settings.HotTemp);
        _coldTemp.ValueChanged += async (_, _) => await OnTemperatureRangeChangedAsync();
        _hotTemp.ValueChanged += async (_, _) => await OnTemperatureRangeChangedAsync();
        _coldColor = Ui.Swatch(Ui.ToDrawing(RgbColor.ParseOr(settings.ColdColor, new RgbColor(0, 64, 255))), 28);
        _hotColor = Ui.Swatch(Ui.ToDrawing(RgbColor.ParseOr(settings.HotColor, new RgbColor(255, 0, 0))), 28);
        _coldColor.Margin = new Padding(0, 3, 18, 0);
        _hotColor.Margin = new Padding(0, 3, 0, 0);
        _coldColor.Click += async (_, _) => await PickTemperatureColorAsync(cold: true);
        _hotColor.Click += async (_, _) => await PickTemperatureColorAsync(cold: false);
        _tips.SetToolTip(_coldColor, "Cor quando está frio");
        _tips.SetToolTip(_hotColor, "Cor quando está quente");
        target.Controls.Add(Ui.Row(Ui.InlineLabel("Frio até"), _coldTemp, Ui.InlineLabel("°C"), _coldColor,
            Ui.InlineLabel("Quente a partir de"), _hotTemp, Ui.InlineLabel("°C"), _hotColor));

        _targetCombo = Ui.Combo(440);
        _targetCombo.SelectedIndexChanged += async (_, _) =>
        {
            if (_syncing || !(_targetCombo.SelectedItem is TargetItem item)) return;
            _config.Temperature.TargetDevice = item.Device;
            _config.Temperature.TargetDeviceId = item.DeviceId;
            _config.Temperature.TargetZone = item.Zone;
            SaveConfigQuietly();
            await RestartTemperatureAsync();
        };
        target.Controls.Add(Ui.Row(Ui.InlineLabel("Mostrar em"), _targetCombo));

        _temperatureLive.AutoSize = true;
        _temperatureLive.MaximumSize = new Size(temperatureCard.InnerWidth, 0);
        _temperatureLive.Margin = new Padding(0, 6, 0, 0);
        _temperatureLive.ForeColor = Ui.Muted;
        _temperatureLive.Text = "Lendo sensores...";
        target.Controls.Add(_temperatureLive);

        RefreshRuleProfiles();
        RefreshRuleList();
        RefreshTemperatureTargets();
        page.Controls.Add(scheduleCard);
        page.Controls.Add(temperatureCard);
        return page;
    }

    private void StartAutomations()
    {
        _scheduleTimer.Interval = 20000;
        _scheduleTimer.Tick += async (_, _) => await ScheduleTickAsync();
        _scheduleTimer.Start();

        _temperatureTimer.Interval = 2500;
        _temperatureTimer.Tick += async (_, _) => await TemperatureTickAsync();
        _temperatureTimer.Start();
    }

    private void StopAutomations()
    {
        _scheduleTimer.Dispose();
        _temperatureTimer.Dispose();
        try { _sensors?.Dispose(); } catch { }
    }

    private void SaveConfigQuietly()
    {
        try { _config.Save(); } catch { }
    }

    private void OnAutomationPageShown()
    {
        RefreshRuleProfiles();
        RefreshRuleList();
        if (!_sensorListFilled) FillSensorCombo();
    }

    // ---------------------------------------------------------------- horário

    private void RefreshRuleProfiles()
    {
        if (_ruleProfile is null) return;
        var selected = _ruleProfile.SelectedItem as string;
        _ruleProfile.Items.Clear();
        foreach (var profile in _config.Profiles) _ruleProfile.Items.Add(profile.Name);
        if (selected is not null && _ruleProfile.Items.Contains(selected)) _ruleProfile.SelectedItem = selected;
        else if (_ruleProfile.Items.Count > 0) _ruleProfile.SelectedIndex = 0;
    }

    private List<ScheduleRule> SortedRules() =>
        _config.Schedule.Rules
            .OrderBy(r => Scheduler.TryParseTime(r.Time, out var t) ? t : TimeSpan.MaxValue)
            .ToList();

    private void RefreshRuleList()
    {
        if (_ruleList is null) return;
        _config.Schedule.Rules = SortedRules();
        _ruleList.Items.Clear();
        foreach (var rule in _config.Schedule.Rules)
            _ruleList.Items.Add($"{rule.Time}   →   {rule.Profile}");
        if (_config.Schedule.Rules.Count == 0)
            _ruleList.Items.Add("Nenhuma regra. Exemplo: às 22:00 aplicar Noite, às 07:00 aplicar Dia.");
    }

    private async Task AddRuleAsync()
    {
        if (!(_ruleProfile.SelectedItem is string profile))
        {
            SetStatus("Crie um perfil primeiro, na aba Perfis.");
            return;
        }

        var time = $"{(int)_ruleHour.Value:00}:{(int)_ruleMinute.Value:00}";
        _config.Schedule.Rules.RemoveAll(r => r.Time == time);
        _config.Schedule.Rules.Add(new ScheduleRule { Time = time, Profile = profile });
        SaveConfigQuietly();
        RefreshRuleList();
        _log.Info($"Regra de horário: às {time} aplicar \"{profile}\".");

        if (!_config.Schedule.Enabled)
        {
            SetStatus($"Regra adicionada. Marque \"Trocar de perfil por horário\" para ela valer.");
            return;
        }

        SetStatus($"Regra adicionada: às {time} aplicar \"{profile}\".");
        if (ApplyScheduleNow(announce: true)) await ApplyAsync();
    }

    private void RemoveSelectedRule()
    {
        var index = _ruleList.SelectedIndex;
        if (index < 0 || index >= _config.Schedule.Rules.Count)
        {
            SetStatus("Primeiro selecione uma regra na lista.");
            return;
        }

        _config.Schedule.Rules.RemoveAt(index);
        SaveConfigQuietly();
        RefreshRuleList();
    }

    /// <summary>
    /// Carrega o perfil que deveria estar valendo agora, segundo as regras.
    /// Retorna true se carregou (quem chamou decide quando aplicar).
    /// </summary>
    private bool ApplyScheduleNow(bool announce)
    {
        _lastScheduleCheck = DateTime.Now;
        if (!_config.Schedule.Enabled) return false;

        var rule = Scheduler.ActiveAt(_config.Schedule.Rules, DateTime.Now.TimeOfDay);
        if (rule is null || !_controller.LoadProfile(rule.Profile)) return false;

        SyncColorUi();
        if (announce) SetStatus($"Pelo horário, o perfil de agora é \"{rule.Profile}\".");
        return true;
    }

    private async Task ScheduleTickAsync()
    {
        if (_exiting) return;
        var now = DateTime.Now;
        var last = _lastScheduleCheck;
        _lastScheduleCheck = now;
        if (!_config.Schedule.Enabled) return;

        var due = Scheduler.DueBetween(_config.Schedule.Rules, last, now);
        if (due is null) return;

        _log.Info($"Horário {due.Time}: aplicando o perfil \"{due.Profile}\".");
        if (!_controller.LoadProfile(due.Profile))
        {
            _log.Warn($"O perfil \"{due.Profile}\" da regra das {due.Time} não existe mais.");
            return;
        }

        SyncColorUi();
        await ApplyAsync();
    }

    // ---------------------------------------------------------------- temperatura

    private void RefreshTemperatureTargets()
    {
        if (_targetCombo is null) return;

        var settings = _config.Temperature;
        var items = new List<TargetItem> { new TargetItem { Text = "Todas as peças" } };
        var seen = new List<string>();
        foreach (var device in _controller.Devices)
        {
            if (seen.Contains(device.StableId)) continue;
            seen.Add(device.StableId);
            items.Add(new TargetItem { Device = device.Name, DeviceId = device.StableId, Text = device.Name + " (inteira)" });
            if (device.Zones.Count > 1)
                foreach (var zone in device.Zones)
                    items.Add(new TargetItem { Device = device.Name, DeviceId = device.StableId, Zone = zone.Name, Text = device.Name + "  ›  " + zone.Name });
        }

        // Mantém o alvo salvo mesmo que a peça não esteja conectada agora.
        var current = items.FirstOrDefault(i =>
            (string.IsNullOrWhiteSpace(settings.TargetDeviceId)
                ? string.Equals(i.Device, settings.TargetDevice, StringComparison.OrdinalIgnoreCase)
                : string.Equals(i.DeviceId, settings.TargetDeviceId, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(i.Zone, settings.TargetZone, StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            current = new TargetItem
            {
                Device = settings.TargetDevice,
                DeviceId = settings.TargetDeviceId,
                Zone = settings.TargetZone,
                Text = settings.TargetDevice + (settings.TargetZone.Length > 0 ? "  ›  " + settings.TargetZone : "") + " (não detectada)",
            };
            items.Add(current);
        }

        _syncing = true;
        try
        {
            _targetCombo.Items.Clear();
            foreach (var item in items) _targetCombo.Items.Add(item);
            _targetCombo.SelectedItem = current;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void FillSensorCombo()
    {
        var settings = _config.Temperature;
        _syncing = true;
        try
        {
            _sensorCombo.Items.Clear();
            var auto = new SensorItem { Id = "", Text = "Automático (CPU, se existir)" };
            _sensorCombo.Items.Add(auto);
            object selected = auto;
            foreach (var sensor in _lastSensors)
            {
                var value = sensor.Value is null ? "sem leitura" : $"{sensor.Value:0} °C";
                var item = new SensorItem { Id = sensor.Id, Text = $"{sensor.Name}  —  {value}" };
                _sensorCombo.Items.Add(item);
                if (sensor.Id == settings.SensorId) selected = item;
            }

            if (settings.SensorId.Length > 0 && ReferenceEquals(selected, auto))
            {
                var missing = new SensorItem { Id = settings.SensorId, Text = "Sensor salvo (não encontrado agora)" };
                _sensorCombo.Items.Add(missing);
                selected = missing;
            }

            _sensorCombo.SelectedItem = selected;
            _sensorListFilled = _lastSensors.Count > 0;
        }
        finally
        {
            _syncing = false;
        }
    }

    private async Task OnTemperatureRangeChangedAsync()
    {
        if (_syncing) return;

        // O "quente" precisa ficar acima do "frio".
        if (_hotTemp.Value <= _coldTemp.Value)
        {
            _syncing = true;
            try { _hotTemp.Value = Math.Min(_hotTemp.Maximum, _coldTemp.Value + 1); }
            finally { _syncing = false; }
        }

        _config.Temperature.ColdTemp = (int)_coldTemp.Value;
        _config.Temperature.HotTemp = (int)_hotTemp.Value;
        SaveConfigQuietly();
        await RestartTemperatureAsync();
    }

    private async Task PickTemperatureColorAsync(bool cold)
    {
        var button = cold ? _coldColor : _hotColor;
        using var dialog = new ColorDialog { FullOpen = true, AnyColor = true, Color = button.BackColor };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        button.BackColor = dialog.Color;
        var hex = Ui.FromDrawing(dialog.Color).ToHex();
        if (cold) _config.Temperature.ColdColor = hex;
        else _config.Temperature.HotColor = hex;
        SaveConfigQuietly();
        await RestartTemperatureAsync();
    }

    /// <summary>Depois de mudar qualquer ajuste: recalcula a cor e repinta todas as peças.</summary>
    private async Task RestartTemperatureAsync()
    {
        _temperature.Reset();
        _temperatureColor = null;

        if (_config.Temperature.Enabled)
        {
            await TemperatureTickAsync();
            if (_temperatureColor is not null) return; // o tick já pintou
        }

        await ApplyAsync();
    }

    private void UpdateFooterTemperatures(IReadOnlyList<TemperatureSensor> sensors)
    {
        string Show(string prefix)
        {
            var sensor = sensors.FirstOrDefault(s => s.Value is not null && s.Name.StartsWith(prefix, StringComparison.Ordinal));
            return sensor is null ? prefix + " --" : $"{prefix} {sensor.Value:0} °C";
        }

        _cpuLabel.Text = Show("CPU");
        _gpuLabel.Text = Show("GPU");
    }

    private TemperatureSensor? PickSensor(IReadOnlyList<TemperatureSensor> sensors)
    {
        var id = _config.Temperature.SensorId;
        if (id.Length > 0)
            return sensors.FirstOrDefault(s => s.Id == id);

        var withValue = sensors.Where(s => s.Value is not null).ToList();
        return withValue.FirstOrDefault(s => s.Name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0)
               ?? withValue.FirstOrDefault();
    }

    private async Task TemperatureTickAsync()
    {
        if (_exiting || _readingSensors) return;

        var enabled = _config.Temperature.Enabled;
        var watching = Visible && _currentPage == 2;
        if (!enabled && !Visible) return;

        _readingSensors = true;
        try
        {
            var sensors = await Task.Run(() =>
            {
                if (_sensors is null && !_sensorsFailed)
                {
                    try { _sensors = _sensorFactory(); }
                    catch (Exception ex)
                    {
                        _sensorsFailed = true;
                        _log.Error("Não consegui iniciar a leitura de sensores: " + ex.Message);
                    }
                }

                try { return _sensors?.Read() ?? Array.Empty<TemperatureSensor>(); }
                catch (Exception) { return Array.Empty<TemperatureSensor>(); }
            });

            if (_exiting) return;
            UpdateFooterTemperatures(sensors);
            var firstRead = _lastSensors.Count == 0 && sensors.Count > 0;
            _lastSensors = sensors;
            if (firstRead)
            {
                _log.Info("Sensores de temperatura: " + string.Join("; ",
                    sensors.Select(s => s.Name + (s.Value is null ? " (sem leitura)" : $" {s.Value:0} °C"))));
                if (watching) FillSensorCombo();
            }

            var sensor = PickSensor(sensors);
            if (sensor?.Value is null)
            {
                _temperatureLive.Text = sensors.Count == 0
                    ? "Nenhum sensor de temperatura encontrado. Para ler a CPU, abra o Abaddon RGB como administrador (com o PawnIO instalado)."
                    : "O sensor escolhido está sem leitura. Escolha outro, ou abra o Abaddon RGB como administrador para ler a CPU.";
                return;
            }

            var value = sensor.Value.Value;
            if (!enabled)
            {
                var preview = TemperatureAutomation.ColorFor(value, _config.Temperature);
                _temperatureLive.Text = $"Agora: {sensor.Name} em {value:0} °C. Se ligada, a cor seria {preview.ToHex()}.";
                return;
            }

            var color = _temperature.Update(value, _config.Temperature);
            if (color is not null)
            {
                var first = _temperatureColor is null;
                _temperatureColor = color;
                // Na primeira vez pinta tudo; depois, só a peça que mostra a temperatura.
                await ApplyAsync(quiet: !first, onlyTemperatureTarget: !first);
            }

            _temperatureLive.Text =
                $"Agora: {sensor.Name} em {value:0} °C  →  cor {(_temperatureColor ?? RgbColor.Black).ToHex()}";
        }
        catch (Exception ex)
        {
            _temperatureLive.Text = "Erro ao ler a temperatura: " + ex.Message;
        }
        finally
        {
            _readingSensors = false;
        }
    }
}

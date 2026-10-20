namespace HeadphoneAutoSwitch;

public sealed class MainForm : Form
{
    private Settings _settings;
    private readonly RazerHeadset _detector = new();
    private SwitchDecision _decision;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pause;
    private readonly CheckBox _startup = new() { Text = UiText.StartWithWindows, AutoSize = true };
    private bool _refreshingStartup;
    private bool _startInTray;
    private readonly ComboBox _headphones = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _speakers = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox _enabled = new() { Text = UiText.AutoSwitch, AutoSize = true };
    private readonly CheckBox _communications = new() { Text = UiText.Communications, AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(580, 0), Text = UiText.Waiting };
    private readonly Label _current = new() { AutoSize = true, MaximumSize = new Size(580, 0) };
    private readonly Label _action = new() { AutoSize = true, MaximumSize = new Size(580, 0) };
    private bool _busy, _exiting;
    private int _generation;
    private DateTimeOffset _previousPoll, _retryAfter;
    private string? _lastReading;

    public MainForm(bool startInTray = false)
    {
        _startInTray = startInTray;
        try { _settings = Storage.Load(); }
        catch (Exception ex)
        {
            _settings = new();
            Storage.Log(UiText.LoadError + " " + ex.Message);
            MessageBox.Show(UiText.LoadError + "\n" + ex.Message,
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _decision = new(_settings.OfflineSamples);
        Text = AppInfo.Name;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(640, 550);
        MinimumSize = new Size(650, 580);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppInfo.AppIcon;
        BuildLayout();
        _enabled.Checked = _settings.AutoSwitch;
        _communications.Checked = _settings.SwitchCommunications;

        var menu = new ContextMenuStrip();
        menu.Items.Add(UiText.Settings, null, (_, _) => ShowSettings());
        _pause = new(UiText.Pause, null, (_, _) => TogglePause());
        menu.Items.Add(_pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiText.Exit, null, (_, _) => ExitApplication());
        _tray = new NotifyIcon { Icon = AppInfo.AppIcon, Text = AppInfo.Name, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowSettings();
        _startup.CheckedChanged += (_, _) => ToggleStartup();
        Activated += (_, _) => RefreshStartupState();
        UpdatePauseText();
        RefreshStartupState();
        RefreshOutputs();
        _timer.Interval = _settings.PollMilliseconds;
        _timer.Tick += async (_, _) => await PollAsync();
        // A login launch may never show the form, so polling must not depend on Shown.
        _timer.Start();
        FormClosing += OnClosing;
        Storage.Log($"Start {AppInfo.Version} (UI: {UiText.LanguageFor(System.Globalization.CultureInfo.CurrentUICulture)}, tray: {startInTray})");
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startInTray && value)
        {
            _startInTray = false;
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    private void BuildLayout()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 0,
            AutoScroll = true
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control, int bottom = 10)
        {
            control.Margin = new Padding(0, 0, 0, bottom);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(control, 0, panel.RowCount++);
        }
        Add(new Label { Text = UiText.Heading, Font = new Font(Font, FontStyle.Bold), AutoSize = true }, 6);
        Add(new Label { Text = UiText.Device, AutoSize = true }, 22);
        Add(new Label { Text = UiText.WhenOnline, AutoSize = true }, 5);
        Add(_headphones, 14);
        Add(new Label { Text = UiText.WhenOffline, AutoSize = true }, 5);
        Add(_speakers, 14);
        Add(_enabled, 6);
        Add(_communications, 6);
        Add(_startup, 12);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.Add(Button(UiText.Save, SaveSettings));
        buttons.Controls.Add(Button(UiText.Refresh, RefreshOutputs));
        Add(buttons, 18);
        Add(_status, 6);
        Add(_current, 6);
        Add(_action, 14);
        Add(new Label
        {
            Text = UiText.Help,
            AutoSize = true, MaximumSize = new Size(580, 0), ForeColor = SystemColors.GrayText
        }, 0);
        Controls.Add(panel);
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(7, 3, 7, 3) };
        button.Click += (_, _) => action();
        return button;
    }

    private void RefreshOutputs()
    {
        try
        {
            var endpoints = WindowsAudio.ListOutputs();
            var headphonesId = (_headphones.SelectedItem as AudioEndpoint)?.Id ?? _settings.HeadphonesId;
            var speakersId = (_speakers.SelectedItem as AudioEndpoint)?.Id ?? _settings.SpeakersId;
            if (string.IsNullOrEmpty(headphonesId))
                headphonesId = endpoints.FirstOrDefault(x => x.Name.Contains("BlackShark", StringComparison.OrdinalIgnoreCase))?.Id ?? "";
            if (string.IsNullOrEmpty(speakersId))
            {
                var currentId = WindowsAudio.DefaultId();
                if (currentId != headphonesId) speakersId = currentId ?? "";
            }
            Populate(_headphones, endpoints, headphonesId);
            Populate(_speakers, endpoints, speakersId);
            UpdateCurrentOutput();
        }
        catch (Exception ex) { ShowError(UiText.GetDevicesError, ex); }
    }

    private static void Populate(ComboBox box, List<AudioEndpoint> endpoints, string selectedId)
    {
        box.Items.Clear();
        box.Items.AddRange(endpoints.Cast<object>().ToArray());
        if (!string.IsNullOrEmpty(selectedId) && !endpoints.Any(x => x.Id == selectedId))
            box.Items.Add(new AudioEndpoint(selectedId, UiText.MissingDevice));
        box.SelectedItem = box.Items.Cast<AudioEndpoint>().FirstOrDefault(x => x.Id == selectedId);
    }

    private void SaveSettings()
    {
        var updated = new Settings
        {
            HeadphonesId = (_headphones.SelectedItem as AudioEndpoint)?.Id ?? "",
            SpeakersId = (_speakers.SelectedItem as AudioEndpoint)?.Id ?? "",
            AutoSwitch = _enabled.Checked,
            SwitchCommunications = _communications.Checked,
            PollMilliseconds = _settings.PollMilliseconds,
            OfflineSamples = _settings.OfflineSamples
        };
        if (!updated.IsConfigured)
        {
            MessageBox.Show(UiText.ChooseDevices, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            Storage.Save(updated);
            _settings = updated;
            _generation++;
            _decision = new(updated.OfflineSamples);
            _retryAfter = DateTimeOffset.MinValue;
            UpdatePauseText();
            _action.Text = updated.AutoSwitch ? UiText.SavedEnabled : UiText.SavedDisabled;
        }
        catch (Exception ex) { ShowError(UiText.SaveError, ex); }
    }

    private async Task PollAsync()
    {
        if (_busy || _exiting) return;
        _busy = true;
        var generation = _generation;
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_previousPoll != default && now - _previousPoll > TimeSpan.FromSeconds(15))
            {
                // A resume must gather fresh observations, not complete a pre-sleep debounce.
                _decision.Reset();
                _retryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
            }
            _previousPoll = now;
            var reading = await _detector.PollAsync(_shutdown.Token);
            if (_exiting || generation != _generation) return;
            var label = reading.State switch
            {
                LinkState.Online => UiText.Online,
                LinkState.Offline => UiText.Offline,
                _ => UiText.Unknown
            };
            _status.Text = label + "\n" + reading.Detail;
            _tray.Text = AppInfo.Name + " · " + label;
            var signature = reading.State + ": " + reading.Detail;
            if (_lastReading != signature) { Storage.Log(signature); _lastReading = signature; }
            if (!_settings.AutoSwitch || !_settings.IsConfigured) return;
            var desired = _decision.Observe(reading.State);
            if (desired == null || DateTimeOffset.UtcNow < _retryAfter) return;
            var endpointId = desired == LinkState.Online ? _settings.HeadphonesId : _settings.SpeakersId;
            try
            {
                WindowsAudio.SetDefault(endpointId, _settings.SwitchCommunications);
                _decision.Acknowledge(desired.Value);
                _action.Text = desired == LinkState.Online ? UiText.SwitchedHeadphones : UiText.SwitchedSpeakers;
                Storage.Log(_action.Text);
                UpdateCurrentOutput();
            }
            catch (Exception ex)
            {
                _retryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
                _action.Text = UiText.SwitchError + ex.Message;
                Storage.Log(_action.Text);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _decision.Observe(LinkState.Unknown);
            _status.Text = UiText.PollError + ex.Message;
            Storage.Log(_status.Text);
        }
        finally { _busy = false; }
    }

    private void UpdateCurrentOutput()
    {
        var id = WindowsAudio.DefaultId();
        var name = WindowsAudio.ListOutputs().FirstOrDefault(x => x.Id == id)?.Name ?? UiText.NotSelected;
        _current.Text = UiText.CurrentOutput + name;
    }

    private void TogglePause()
    {
        if (!_settings.IsConfigured) { ShowSettings(); return; }
        var previous = _settings.AutoSwitch;
        _settings.AutoSwitch = !previous;
        try { Storage.Save(_settings); }
        catch (Exception ex) { _settings.AutoSwitch = previous; ShowError(UiText.SaveError, ex); return; }
        _enabled.Checked = _settings.AutoSwitch;
        _generation++;
        _decision.Reset();
        _retryAfter = DateTimeOffset.MinValue;
        UpdatePauseText();
        _action.Text = _settings.AutoSwitch ? UiText.Enabled : UiText.Paused;
    }

    private void RefreshStartupState()
    {
        _refreshingStartup = true;
        try { _startup.Checked = WindowsStartup.IsEnabled(); _startup.Enabled = true; }
        catch (Exception ex)
        {
            _startup.Checked = false;
            _startup.Enabled = false;
            Storage.Log(UiText.StartupReadError + ": " + ex.Message);
        }
        finally { _refreshingStartup = false; }
    }

    private void ToggleStartup()
    {
        if (_refreshingStartup) return;
        try
        {
            WindowsStartup.SetEnabled(_startup.Checked);
            RefreshStartupState();
            _action.Text = _startup.Checked ? UiText.StartupEnabled : UiText.StartupDisabled;
            Storage.Log(_action.Text);
        }
        catch (Exception ex)
        {
            RefreshStartupState();
            ShowError(UiText.StartupWriteError, ex);
        }
    }

    private void UpdatePauseText() => _pause.Text = _settings.AutoSwitch ? UiText.Pause : UiText.Resume;
    private void ShowSettings() { Show(); WindowState = FormWindowState.Normal; Activate(); RefreshOutputs(); }
    private void ExitApplication() { _exiting = true; Close(); }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        _exiting = true;
        _timer.Stop();
        _shutdown.Cancel();
        if (_busy)
        {
            // Keep the message loop alive until the cancelled HID poll has sent remote-off.
            e.Cancel = true;
            while (_busy) await Task.Delay(25);
            Close();
            return;
        }
        _tray.Visible = false;
        _tray.Dispose();
        _timer.Dispose();
    }
    private void ShowError(string title, Exception ex)
    {
        Storage.Log(title + ": " + ex);
        MessageBox.Show(title + ".\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

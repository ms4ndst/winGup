using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace WinGup;

/// <summary>
/// Settings window for configuring Winget Updater.
/// </summary>
public class SettingsWindow : Form
{
    private const string StartupTaskId = "WinGupStartupTask";
    private const string StartupRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "winGup";

    private readonly IIpcClient _ipcClient;
    private readonly ILogger _logger;

    private DateTimePicker _morningTimePicker = null!;
    private DateTimePicker _afternoonTimePicker = null!;
    private CheckBox _notifyCheckBox = null!;
    private CheckBox _autoCheckCheckBox = null!;
    private CheckBox _pinnedCheckBox = null!;
    private CheckBox _unknownVersionsCheckBox = null!;
    private CheckBox _startupCheckBox = null!;
    private Button _saveButton = null!;
    private Button _cancelButton = null!;

    /// <summary>
    /// Creates a new SettingsWindow.
    /// </summary>
    /// <param name="ipcClient">IPC client for service communication</param>
    /// <param name="logger">Logger instance</param>
    public SettingsWindow(IIpcClient ipcClient, ILogger logger)
    {
        _ipcClient = ipcClient ?? throw new ArgumentNullException(nameof(ipcClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        InitializeComponent();
        _ = LoadSettingsAsync();
    }

    private void InitializeComponent()
    {
        Text = "Winget Updater Settings";
        Size = new System.Drawing.Size(400, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var y = 20;
        const int labelWidth = 150;
        const int controlX = 165;

        var morningLabel = new Label
        {
            Text = "Morning Check Time:",
            Location = new System.Drawing.Point(20, y + 3),
            Size = new System.Drawing.Size(labelWidth, 20)
        };
        _morningTimePicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Time,
            ShowUpDown = true,
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(100, 20)
        };
        Controls.Add(morningLabel);
        Controls.Add(_morningTimePicker);
        y += 32;

        var afternoonLabel = new Label
        {
            Text = "Afternoon Check Time:",
            Location = new System.Drawing.Point(20, y + 3),
            Size = new System.Drawing.Size(labelWidth, 20)
        };
        _afternoonTimePicker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Time,
            ShowUpDown = true,
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(100, 20)
        };
        Controls.Add(afternoonLabel);
        Controls.Add(_afternoonTimePicker);
        y += 32;

        _notifyCheckBox = new CheckBox
        {
            Text = "Notify on Updates",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(180, 20)
        };
        Controls.Add(_notifyCheckBox);
        y += 26;

        _autoCheckCheckBox = new CheckBox
        {
            Text = "Auto Check for Updates",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(180, 20)
        };
        Controls.Add(_autoCheckCheckBox);
        y += 26;

        _pinnedCheckBox = new CheckBox
        {
            Text = "Include Pinned Updates",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(180, 20)
        };
        Controls.Add(_pinnedCheckBox);
        y += 26;

        _unknownVersionsCheckBox = new CheckBox
        {
            Text = "Include Unknown Versions",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(180, 20)
        };
        Controls.Add(_unknownVersionsCheckBox);
        y += 26;

        _startupCheckBox = new CheckBox
        {
            Text = "Run on Windows Startup",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(180, 20)
        };
        Controls.Add(_startupCheckBox);
        y += 36;

        _saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(75, 23),
            DialogResult = DialogResult.OK
        };
        _saveButton.Click += SaveButton_Click;

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(controlX + 85, y),
            Size = new System.Drawing.Size(75, 23),
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(_saveButton);
        Controls.Add(_cancelButton);

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;

        // Apply Catppuccin Mocha theme
        CatppuccinTheme.ApplyToForm(this);
        CatppuccinTheme.StyleButton(_saveButton, isPrimary: true);
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var startupEnabled = await GetStartupEnabledAsync().ConfigureAwait(false);
            var response = await _ipcClient.SendMessageAsync("get_settings").ConfigureAwait(false);

            InvokeOnUiThread(() =>
            {
                _startupCheckBox.Checked = startupEnabled;

                if (string.IsNullOrEmpty(response)) return;

                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("morning_check", out var mc) && mc.GetString() is { } mt)
                    if (TimeOnly.TryParse(mt, out var t))
                        _morningTimePicker.Value = DateTime.Today.Add(t.ToTimeSpan());

                if (root.TryGetProperty("afternoon_check", out var ac) && ac.GetString() is { } at)
                    if (TimeOnly.TryParse(at, out var t))
                        _afternoonTimePicker.Value = DateTime.Today.Add(t.ToTimeSpan());

                if (root.TryGetProperty("notify_on_updates", out var n))
                    _notifyCheckBox.Checked = n.GetBoolean();

                if (root.TryGetProperty("auto_check", out var a))
                    _autoCheckCheckBox.Checked = a.GetBoolean();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
        }
    }

    private async void SaveButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await SetStartupEnabledAsync(_startupCheckBox.Checked);

            var config = new
            {
                morning_check = _morningTimePicker.Value.ToString("HH:mm"),
                afternoon_check = _afternoonTimePicker.Value.ToString("HH:mm"),
                notify_on_updates = _notifyCheckBox.Checked,
                auto_check = _autoCheckCheckBox.Checked,
                include_pinned_updates = _pinnedCheckBox.Checked,
                include_unknown_versions = _unknownVersionsCheckBox.Checked
            };

            var json = System.Text.Json.JsonSerializer.Serialize(config);
            await _ipcClient.SendMessageAsync("save_settings", json).ConfigureAwait(false);
            _logger.LogInformation("Settings saved");

            InvokeOnUiThread(Close);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            MessageBox.Show("Failed to save settings: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool IsPackaged()
    {
        try { _ = Windows.ApplicationModel.Package.Current.Id; return true; }
        catch { return false; }
    }

    private static async Task<bool> GetStartupEnabledAsync()
    {
        if (IsPackaged())
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
                return task.State is Windows.ApplicationModel.StartupTaskState.Enabled
                    or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            catch { return false; }
        }

        using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, writable: false);
        return key?.GetValue(StartupValueName) is not null;
    }

    private static async Task SetStartupEnabledAsync(bool enabled)
    {
        if (IsPackaged())
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
                if (enabled)
                    await task.RequestEnableAsync();
                else
                    task.Disable();
                return;
            }
            catch { }
        }

        using var key = Registry.CurrentUser.OpenSubKey(StartupRegKey, writable: true);
        if (key is null) return;
        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(StartupValueName, $"\"{exePath}\" standalone");
        }
        else
        {
            key.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }
    }

    private void InvokeOnUiThread(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
            Invoke(action);
        else
            action();
    }
}

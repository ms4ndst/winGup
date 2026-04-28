using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using WinGup.Models;

namespace WinGup;

/// <summary>
/// Settings window ported from Python ui_component.py SettingsDialog
/// Uses WinForms for Windows-native settings UI
/// </summary>
public class SettingsWindow : Form
{
    private readonly IIpcClient _ipcClient;
    private readonly ILogger _logger;

    // Controls
    private DateTimePicker _morningTimePicker = null!;
    private DateTimePicker _afternoonTimePicker = null!;
    private CheckBox _notifyCheckBox = null!;
    private CheckBox _autoCheckCheckBox = null!;
    private CheckBox _pinnedCheckBox = null!;
    private CheckBox _unknownVersionsCheckBox = null!;
    private Button _saveButton = null!;
    private Button _cancelButton = null!;

    /// <summary>
    /// Creates a new SettingsWindow
    /// </summary>
    /// <param name="ipcClient">IPC client for service communication</param>
    /// <param name="logger">Logger instance</param>
    public SettingsWindow(IIpcClient ipcClient, ILogger logger)
    {
        _ipcClient = ipcClient ?? throw new ArgumentNullException(nameof(ipcClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "Winget Updater Settings";
        Size = new System.Drawing.Size(400, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var y = 20;
        const int labelWidth = 150;
        const int controlX = 160;

        // Morning check time
        var morningLabel = new Label
        {
            Text = "Morning Check Time:",
            Location = new System.Drawing.Point(20, y),
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
        y += 30;

        // Afternoon check time
        var afternoonLabel = new Label
        {
            Text = "Afternoon Check Time:",
            Location = new System.Drawing.Point(20, y),
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
        y += 30;

        // Notify on updates
        _notifyCheckBox = new CheckBox
        {
            Text = "Notify on Updates",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(150, 20)
        };
        Controls.Add(_notifyCheckBox);
        y += 25;

        // Auto check
        _autoCheckCheckBox = new CheckBox
        {
            Text = "Auto Check for Updates",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(150, 20)
        };
        Controls.Add(_autoCheckCheckBox);
        y += 25;

        // Include pinned
        _pinnedCheckBox = new CheckBox
        {
            Text = "Include Pinned Updates",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(150, 20)
        };
        Controls.Add(_pinnedCheckBox);
        y += 25;

        // Include unknown versions
        _unknownVersionsCheckBox = new CheckBox
        {
            Text = "Include Unknown Versions",
            Location = new System.Drawing.Point(controlX, y),
            Size = new System.Drawing.Size(150, 20)
        };
        Controls.Add(_unknownVersionsCheckBox);
        y += 40;

        // Buttons
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
    }

    private void LoadSettings()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var response = await _ipcClient.SendMessageAsync("get_config");
                if (response is not null)
                {
                    InvokeOnUiThread(() =>
                    {
                        // Parse config from response and set controls
                        // Simplified - full implementation would parse JSON
                        _morningTimePicker.Value = DateTime.Today.AddHours(8);
                        _afternoonTimePicker.Value = DateTime.Today.AddHours(16);
                        _notifyCheckBox.Checked = true;
                        _autoCheckCheckBox.Checked = true;
                        _pinnedCheckBox.Checked = false;
                        _unknownVersionsCheckBox.Checked = false;
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
            }
        });
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var config = new
                {
                    morning_check_time = _morningTimePicker.Value.ToString("HH:mm"),
                    afternoon_check_time = _afternoonTimePicker.Value.ToString("HH:mm"),
                    notify_on_updates = _notifyCheckBox.Checked,
                    auto_check = _autoCheckCheckBox.Checked,
                    include_pinned_updates = _pinnedCheckBox.Checked,
                    include_unknown_versions = _unknownVersionsCheckBox.Checked
                };

                var json = System.Text.Json.JsonSerializer.Serialize(config);
                await _ipcClient.SendMessageAsync("save_settings", json);
                _logger.LogInformation("Settings saved");

                // Close the window on the UI thread
                InvokeOnUiThread(() => Close());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                MessageBox.Show("Failed to save settings: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
    }

    internal static void InvokeOnUiThread(Action action)
    {
        if (Application.OpenForms.Count > 0)
        {
            var form = Application.OpenForms[0]!;
            form.Invoke(action);
        }
        else
        {
            action();
        }
    }
}

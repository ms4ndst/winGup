using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using WinGup.Models;

namespace WinGup;

/// <summary>
/// Update list window ported from Python ui_component.py UpdateListDialog
/// Displays available updates in a DataGridView
/// </summary>
public class UpdateListWindow : Form
{
    private DataGridView _grid = null!;
    private Button _refreshButton = null!;
    private Button _updateButton = null!;
    private Button _updateAllButton = null!;
    private Button _pinButton = null!;
    private Button _closeButton = null!;
    private Label _statusLabel = null!;
    private readonly IIpcClient? _ipcClient;
    private List<UpdateInfo> _updates = new();
    private bool _loadTriggered;

    /// <summary>Raised when the displayed update count changes.</summary>
    public event EventHandler<UpdateCountChangedEventArgs>? UpdateCountChanged;

    /// <summary>
    /// Creates a new UpdateListWindow with predefined updates
    /// </summary>
    /// <param name="updates">List of available updates to display</param>
    public UpdateListWindow(IEnumerable<UpdateInfo> updates)
    {
        _updates = updates.ToList();
        InitializeComponent();
        PopulateGrid();
    }

    /// <summary>
    /// Creates a new UpdateListWindow that loads updates from the service
    /// </summary>
    /// <param name="ipcClient">IPC client for loading updates</param>
    public UpdateListWindow(IIpcClient ipcClient)
    {
        _ipcClient = ipcClient ?? throw new ArgumentNullException(nameof(ipcClient));
        InitializeComponent();
        _ = LoadUpdatesAsync();
    }

    private void InitializeComponent()
    {
        Text = "Available Updates";
        Size = new System.Drawing.Size(800, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;

        // Grid
        _grid = new DataGridView
        {
            Location = new System.Drawing.Point(10, 10),
            Size = new System.Drawing.Size(760, 380),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };

        _grid.Columns.Add("PackageId", "Package ID");
        _grid.Columns.Add("Name", "Name");
        _grid.Columns.Add("CurrentVersion", "Current Version");
        _grid.Columns.Add("AvailableVersion", "Available Version");
        _grid.Columns.Add("Source", "Source");
        _grid.Columns.Add("IsPinned", "Pinned");

        // Add context menu for pin/unpin
        var contextMenu = new ContextMenuStrip();
        var pinItem = new ToolStripMenuItem("Pin Update", null, (_, _) => TogglePin());
        var unpinItem = new ToolStripMenuItem("Unpin Update", null, (_, _) => TogglePin());
        contextMenu.Items.Add(pinItem);
        contextMenu.Items.Add(unpinItem);
        _grid.ContextMenuStrip = contextMenu;

        Controls.Add(_grid);

        // Buttons
        var buttonY = 400;

        _refreshButton = new Button
        {
            Text = "Refresh",
            Location = new System.Drawing.Point(10, buttonY),
            Size = new System.Drawing.Size(75, 23)
        };
        _refreshButton.Click += RefreshButton_Click;

        _updateButton = new Button
        {
            Text = "Update Selected",
            Location = new System.Drawing.Point(95, buttonY),
            Size = new System.Drawing.Size(110, 23)
        };
        _updateButton.Click += UpdateButton_Click;

        _updateAllButton = new Button
        {
            Text = "Update All",
            Location = new System.Drawing.Point(215, buttonY),
            Size = new System.Drawing.Size(85, 23)
        };
        _updateAllButton.Click += UpdateAllButton_Click;

        _pinButton = new Button
        {
            Text = "Toggle Pin",
            Location = new System.Drawing.Point(310, buttonY),
            Size = new System.Drawing.Size(100, 23)
        };
        _pinButton.Click += (_, _) => TogglePin();

        _closeButton = new Button
        {
            Text = "Close",
            Location = new System.Drawing.Point(695, buttonY),
            Size = new System.Drawing.Size(75, 23),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _closeButton.Click += (_, _) => Close();

        _statusLabel = new Label
        {
            Text = "",
            Location = new System.Drawing.Point(420, buttonY + 3),
            Size = new System.Drawing.Size(265, 18),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = System.Drawing.Color.Gray
        };

        Controls.Add(_refreshButton);
        Controls.Add(_updateButton);
        Controls.Add(_updateAllButton);
        Controls.Add(_pinButton);
        Controls.Add(_statusLabel);
        Controls.Add(_closeButton);
    }

    private void PopulateGrid()
    {
        if (IsDisposed || _grid.IsDisposed) return;
        _grid.Rows.Clear();

        foreach (var update in _updates)
        {
            _grid.Rows.Add(
                update.Id,
                update.Name,
                update.CurrentVersion,
                update.AvailableVersion,
                update.Source,
                update.IsPinned ? "Yes" : "No"
            );
        }

        _updateButton.Enabled = _updates.Count > 0;
        _updateAllButton.Enabled = _updates.Count > 0;
        UpdateCountChanged?.Invoke(this, new UpdateCountChangedEventArgs(_updates.Count));
    }

    private static List<UpdateInfo> ParseUpdates(string json)
    {
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return System.Text.Json.JsonSerializer.Deserialize<List<UpdateInfo>>(json, options) ?? new List<UpdateInfo>();
    }

    /// <summary>Reloads updates from the service.</summary>
    public void Reload()
    {
        if (_ipcClient is not null)
            _ = LoadUpdatesAsync();
    }

    private async Task LoadUpdatesAsync()
    {
        if (_ipcClient is null) return;

        InvokeOnUiThread(() => _statusLabel.Text = "Loading...");

        try
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var response = await _ipcClient.SendMessageAsync("get_updates");
            var updates = string.IsNullOrEmpty(response)
                ? new List<UpdateInfo>()
                : System.Text.Json.JsonSerializer.Deserialize<List<UpdateInfo>>(response, options) ?? new List<UpdateInfo>();

            // Cache empty and no prior check triggered — service is still running winget
            if (updates.Count == 0 && !_loadTriggered)
            {
                _loadTriggered = true;
                InvokeOnUiThread(() => _statusLabel.Text = "Checking for updates...");
                await _ipcClient.SendMessageAsync("check_updates");
                await Task.Delay(TimeSpan.FromSeconds(25));
                response = await _ipcClient.SendMessageAsync("get_updates");
                updates = string.IsNullOrEmpty(response)
                    ? new List<UpdateInfo>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<UpdateInfo>>(response, options) ?? new List<UpdateInfo>();
            }

            _updates = updates;
            InvokeOnUiThread(() =>
            {
                PopulateGrid();
                _statusLabel.Text = _updates.Count == 0 ? "No updates available." : "";
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to load updates: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void TogglePin()
    {
        if (_grid.SelectedRows.Count == 0)
        {
            MessageBox.Show("Please select an update to pin/unpin.", "No Selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedIds = new List<string>();
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            selectedIds.Add(row.Cells[0].Value?.ToString() ?? "");
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (_ipcClient is null) return;

                var data = System.Text.Json.JsonSerializer.Serialize(selectedIds);
                var response = await _ipcClient.SendMessageAsync("toggle_pin", data);

                if (!string.IsNullOrEmpty(response))
                {
                    var updates = ParseUpdates(response);
                    InvokeOnUiThread(() =>
                    {
                        _updates = updates;
                        PopulateGrid();
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to toggle pin: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
    }

    private void RefreshButton_Click(object? sender, EventArgs e)
    {
        if (_ipcClient is not null)
        {
            _ = LoadUpdatesAsync();
        }
    }

    private void UpdateAllButton_Click(object? sender, EventArgs e)
    {
        var unpinnedIds = _updates
            .Where(u => !u.IsPinned)
            .Select(u => u.Id)
            .ToList();

        if (unpinnedIds.Count == 0)
        {
            MessageBox.Show("No unpinned updates available.", "Nothing to Update",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Update all {unpinnedIds.Count} unpinned package(s)?",
            "Confirm Update All",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        _updateAllButton.Enabled = false;
        _updateButton.Enabled = false;
        InvokeOnUiThread(() => _statusLabel.Text = "Installing...");

        _ = Task.Run(async () =>
        {
            try
            {
                if (_ipcClient is null) return;

                var data = System.Text.Json.JsonSerializer.Serialize(unpinnedIds);
                var response = await _ipcClient.SendMessageAsync("update_packages", data);

                if (!string.IsNullOrEmpty(response))
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    using var doc = System.Text.Json.JsonDocument.Parse(response);
                    var root = doc.RootElement;

                    var failed = new List<string>();
                    if (root.TryGetProperty("failed", out var failedEl))
                        foreach (var f in failedEl.EnumerateArray())
                            failed.Add(f.GetString() ?? "");

                    if (root.TryGetProperty("updates", out var updatesEl))
                    {
                        var updates = System.Text.Json.JsonSerializer.Deserialize<List<UpdateInfo>>(
                            updatesEl.GetRawText(), options) ?? new();
                        InvokeOnUiThread(() =>
                        {
                            _updates = updates;
                            PopulateGrid();
                            _statusLabel.Text = failed.Count > 0
                                ? $"{failed.Count} package(s) failed."
                                : "All updates installed.";
                        });
                    }

                    if (failed.Count > 0)
                        MessageBox.Show($"Failed to install: {string.Join(", ", failed)}", "Install Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to update packages: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                InvokeOnUiThread(() =>
                {
                    _updateAllButton.Enabled = _updates.Count > 0;
                    _updateButton.Enabled = _updates.Count > 0;
                });
            }
        });
    }

    private void UpdateButton_Click(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0)
        {
            MessageBox.Show("Please select updates to install.", "No Selection",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedIds = new List<string>();
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            selectedIds.Add(row.Cells[0].Value?.ToString() ?? "");
        }

        var result = MessageBox.Show(
            $"Update {selectedIds.Count} package(s)?",
            "Confirm Update",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _updateButton.Enabled = false;
            InvokeOnUiThread(() => _statusLabel.Text = "Installing...");

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_ipcClient is null) return;

                    var data = System.Text.Json.JsonSerializer.Serialize(selectedIds);
                    var response = await _ipcClient.SendMessageAsync("update_packages", data);

                    if (!string.IsNullOrEmpty(response))
                    {
                        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        using var doc = System.Text.Json.JsonDocument.Parse(response);
                        var root = doc.RootElement;

                        var failed = new List<string>();
                        if (root.TryGetProperty("failed", out var failedEl))
                            foreach (var f in failedEl.EnumerateArray())
                                failed.Add(f.GetString() ?? "");

                        if (root.TryGetProperty("updates", out var updatesEl))
                        {
                            var updates = System.Text.Json.JsonSerializer.Deserialize<List<UpdateInfo>>(
                                updatesEl.GetRawText(), options) ?? new();
                            InvokeOnUiThread(() =>
                            {
                                _updates = updates;
                                PopulateGrid();
                                _statusLabel.Text = failed.Count > 0
                                    ? $"{failed.Count} package(s) failed to install."
                                    : "Installation complete.";
                            });
                        }

                        if (failed.Count > 0)
                            MessageBox.Show($"Failed to install: {string.Join(", ", failed)}", "Install Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to update packages: " + ex.Message, "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    InvokeOnUiThread(() => _updateButton.Enabled = _updates.Count > 0);
                }
            });
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

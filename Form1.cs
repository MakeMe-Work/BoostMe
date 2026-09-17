using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BoostMe;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly ContextMenuStrip trayMenu = new();
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly BoostSettings settings;
    private readonly MMDeviceEnumerator deviceEnumerator = new();

    public TrayApplicationContext()
    {
        settings = BoostSettings.Load();
        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BoostMe",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        trayIcon.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
                BuildMenu();
        };

        refreshTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        refreshTimer.Tick += (_, _) => ApplySavedVolumes();
        refreshTimer.Start();
        trayIcon.DoubleClick += (_, _) => BuildMenu();
        BuildMenu();
        if (settings.GlobalBoostPercent > 100)
        {
            settings.GlobalBoostPercent = 100;
            settings.Save();
        }
    }

    private void BuildMenu()
    {
        trayMenu.Items.Clear();
        trayMenu.Items.Add(new ToolStripLabel("BOOSTME  /  ACTIVE AUDIO") { Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        trayMenu.Items.Add(new ToolStripSeparator());

        var sessions = GetAudioSessions();
        if (sessions.Count == 0)
        {
            trayMenu.Items.Add(new ToolStripLabel("No active audio apps"));
        }
        else
        {
            foreach (var session in sessions)
            {
                var processKey = session.ProcessName.ToLowerInvariant();
                var appItem = new ToolStripMenuItem(session.DisplayName)
                {
                    ToolTipText = session.ProcessName,
                    Image = SystemIcons.Application.ToBitmap()
                };

                var current = settings.Get(processKey);
                foreach (var level in new[] { 25, 50, 75, 100 })
                {
                    var levelItem = new ToolStripMenuItem($"{level}%") { Checked = current == level };
                    levelItem.Click += (_, _) =>
                    {
                        settings.Set(processKey, level);
                        settings.Save();
                        ApplySavedVolumes();
                        BuildMenu();
                    };
                    appItem.DropDownItems.Add(levelItem);
                }

                var resetItem = new ToolStripMenuItem("Reset to Windows volume") { Checked = current is null };
                resetItem.Click += (_, _) =>
                {
                    settings.Remove(processKey);
                    settings.Save();
                    BuildMenu();
                };
                appItem.DropDownItems.Add(new ToolStripSeparator());
                appItem.DropDownItems.Add(resetItem);
                trayMenu.Items.Add(appItem);
            }
        }

        trayMenu.Items.Add(new ToolStripSeparator());
        var boostMenu = new ToolStripMenuItem("Global audio boost");
        foreach (var level in new[] { 100, 125, 150, 200, 300 })
        {
            var levelItem = new ToolStripMenuItem(level == 100 ? "Off (100%)" : $"{level}%")
            {
                Checked = settings.GlobalBoostPercent == level
            };
            levelItem.Click += (_, _) =>
            {
                if (level > 100)
                {
                    MessageBox.Show("Boost above 100% requires a virtual audio device. The current playback endpoint cannot safely capture and replay its own audio.", "Global boost unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    settings.GlobalBoostPercent = 100;
                }
                else
                {
                    settings.GlobalBoostPercent = level;
                }
                settings.Save();
                BuildMenu();
            };
            boostMenu.DropDownItems.Add(levelItem);
        }
        trayMenu.Items.Add(boostMenu);
        trayMenu.Items.Add(new ToolStripSeparator());
        var locationItem = new ToolStripMenuItem("Settings file") { ToolTipText = BoostSettings.FilePath };
        locationItem.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{BoostSettings.FilePath}\"") { UseShellExecute = true });
        trayMenu.Items.Add(locationItem);
        var exitItem = new ToolStripMenuItem("Exit BoostMe");
        exitItem.Click += (_, _) => ExitThread();
        trayMenu.Items.Add(exitItem);
    }

    private void ApplySavedVolumes()
    {
        foreach (var session in GetAudioSessions())
        {
            var savedPercent = settings.Get(session.ProcessName.ToLowerInvariant());
            if (savedPercent is null)
                continue;

            try
            {
                session.SessionVolume.Volume = savedPercent.Value / 100f;
            }
            catch (COMException)
            {
                // A session can disappear between enumeration and update.
            }
        }
    }

    private List<AudioSessionInfo> GetAudioSessions()
    {
        var sessions = new Dictionary<uint, AudioSessionInfo>();
        try
        {
            using var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionCollection = device.AudioSessionManager.Sessions;
            for (var index = 0; index < sessionCollection.Count; index++)
            {
                var session = sessionCollection[index];
                if (session.State != AudioSessionState.AudioSessionStateActive || session.GetProcessID == 0)
                    continue;

                try
                {
                    using var process = Process.GetProcessById((int)session.GetProcessID);
                    var processName = $"{process.ProcessName}.exe";
                    var displayName = string.IsNullOrWhiteSpace(session.DisplayName) ? process.ProcessName : session.DisplayName;
                    sessions[session.GetProcessID] = new AudioSessionInfo(processName, displayName, session.SimpleAudioVolume);
                }
                catch (ArgumentException)
                {
                    // The process ended during enumeration.
                }
            }
        }
        catch (COMException)
        {
            // No render endpoint is available.
        }

        return sessions.Values.OrderBy(item => item.DisplayName).ToList();
    }

    protected override void ExitThreadCore()
    {
        refreshTimer.Stop();
        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayMenu.Dispose();
        deviceEnumerator.Dispose();
        base.ExitThreadCore();
    }

    private sealed record AudioSessionInfo(string ProcessName, string DisplayName, SimpleAudioVolume SessionVolume);
}

public sealed class BoostSettings
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Program Files", "BoostMe", "settings.json");
    public Dictionary<string, int> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int GlobalBoostPercent { get; set; } = 100;

    public int? Get(string processName) => Apps.TryGetValue(processName, out var value) ? value : null;
    public void Set(string processName, int value) => Apps[processName] = value;
    public void Remove(string processName) => Apps.Remove(processName);

    public static BoostSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<BoostSettings>(File.ReadAllText(FilePath)) ?? new BoostSettings();
        }
        catch (JsonException) { }
        return new BoostSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}

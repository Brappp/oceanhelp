using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace SamplePlugin.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private readonly Configuration _configuration;
        private readonly Plugin _plugin;
        private string _logStatus = "Monitoring inactive";
        private const float MinWindowWidth = 500;

        // Professional color scheme
        private readonly Vector4 _activeColor = new Vector4(0.129f, 0.682f, 0.259f, 1.0f);      // Professional green
        private readonly Vector4 _inactiveColor = new Vector4(0.839f, 0.153f, 0.157f, 1.0f);    // Professional red
        private readonly Vector4 _timerColor = new Vector4(0.204f, 0.478f, 0.761f, 1.0f);       // Professional blue
        private readonly Vector4 _pendingColor = new Vector4(0.945f, 0.647f, 0.157f, 1.0f);     // Professional orange
        private readonly Vector4 _headerColor = new Vector4(0.267f, 0.267f, 0.267f, 1.0f);      // Dark gray for headers
        private readonly Vector4 _textColor = new Vector4(0.8f, 0.8f, 0.8f, 1.0f);              // Light gray for text

        private Vector4 _statusColor;

        public MainWindow(Plugin plugin) : base(
            "Ocean Log Monitor###OceanConfig",
            ImGuiWindowFlags.None)
        {
            _plugin = plugin;
            _configuration = plugin.Configuration;

            // Set initial size 
            SizeCondition = ImGuiCond.FirstUseEver;
            Size = new Vector2(MinWindowWidth, 400);

            // Modify size constraints to allow much more flexibility
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(300, 300),  // Allow much smaller width
                MaximumSize = new Vector2(2000, 1500) // Greatly expanded maximum size
            };

            UpdateStatus();
        }

        private void DrawSectionHeader(string text)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, _headerColor);
            ImGui.TextUnformatted(text.ToUpperInvariant());
            ImGui.PopStyleColor();
            ImGui.Separator();
            ImGui.Spacing();
        }

        public override void Draw()
        {
            // Apply global styling
            ImGui.PushStyleColor(ImGuiCol.Text, _textColor);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));

            // Use full available width with a small margin
            float contentWidth = ImGui.GetWindowWidth() - 20;

            // Status Section
            DrawSectionHeader("Monitor Status");

            // Current Status
            ImGui.PushStyleColor(ImGuiCol.Text, _statusColor);
            ImGui.TextUnformatted(_logStatus);
            ImGui.PopStyleColor();

            ImGui.Spacing();

            // Next Check Timer
            var timeUntilCheck = _plugin.TimeUntilNextCheck;
            ImGui.PushStyleColor(ImGuiCol.Text, _timerColor);
            ImGui.TextUnformatted($"Next Check: {FormatTimeSpan(timeUntilCheck)}");
            ImGui.PopStyleColor();

            // Command Execution Timer
            if (_plugin.HasPendingExecution && _plugin.TimeUntilCommand.HasValue)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, _pendingColor);
                ImGui.TextUnformatted($"Command Execution: {FormatTimeSpan(_plugin.TimeUntilCommand.Value)}");
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();

            // Last Check Information
            ImGui.TextUnformatted("Last Check:");
            ImGui.SameLine();
            ImGui.TextColored(_timerColor, _configuration.LastProcessedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never");

            if (!string.IsNullOrEmpty(_configuration.LastFoundEntry))
            {
                ImGui.TextUnformatted("Last Entry:");
                ImGui.PushTextWrapPos(ImGui.GetWindowWidth() - 20);
                ImGui.TextColored(_timerColor, _configuration.LastFoundEntry);
                ImGui.PopTextWrapPos();
            }

            // Configuration Section
            DrawSectionHeader("Configuration");

            // Monitor Toggle
            bool monitorLogs = _configuration.MonitorEnabled;
            if (ImGui.Checkbox("Enable Monitoring", ref monitorLogs))
            {
                _configuration.MonitorEnabled = monitorLogs;
                _plugin.SaveConfiguration();
                UpdateStatus();
            }

            ImGui.Spacing();

            // Check Interval
            ImGui.Spacing();
            ImGui.TextUnformatted("Check Interval (Minutes)");
            ImGui.SetNextItemWidth(contentWidth);
            int interval = _configuration.CheckIntervalMinutes;
            if (ImGui.SliderInt("##CheckIntervalSlider", ref interval, 1, 60))
            {
                _configuration.CheckIntervalMinutes = interval;
                _plugin.SaveConfiguration();
            }

            ImGui.Spacing();

            // Chat Command
            ImGui.TextUnformatted("Chat Command:");
            ImGui.SetNextItemWidth(contentWidth);
            string command = _configuration.ChatCommand;
            if (ImGui.InputText("##ChatCommand", ref command, 256,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
            {
                _configuration.ChatCommand = command;
                _plugin.SaveConfiguration();
            }

            ImGui.Spacing();

            // Log Directory
            ImGui.TextUnformatted("Log Directory:");
            ImGui.SetNextItemWidth(contentWidth);
            string logDir = _plugin.LogDirectory;
            if (ImGui.InputText("##LogDir", ref logDir, 512,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
            {
                _plugin.LogDirectory = logDir;
                _configuration.LogDirectory = logDir;
                _plugin.SaveConfiguration();
            }

            // Actions Section
            DrawSectionHeader("Actions");

            // Calculate button sizes - make them smaller
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 6)); // Reduce horizontal spacing between buttons

            float buttonWidth = contentWidth / 2.5f; // Reduce width more aggressively

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.204f, 0.478f, 0.761f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.204f, 0.478f, 0.761f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.204f, 0.478f, 0.761f, 0.7f));
            if (ImGui.Button("Force Check", new Vector2(buttonWidth, 25)))
            {
                _plugin.CheckLogs(true);
                UpdateStatus();
            }
            ImGui.PopStyleColor(3);

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.839f, 0.153f, 0.157f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.839f, 0.153f, 0.157f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.839f, 0.153f, 0.157f, 0.7f));
            if (ImGui.Button("Reset Data", new Vector2(buttonWidth, 25)))
            {
                _configuration.LastProcessedTime = null;
                _configuration.LastFoundEntry = null;
                _plugin.SaveConfiguration();
                UpdateStatus();
            }
            ImGui.PopStyleColor(3);

            ImGui.PopStyleVar(2); // Pop frame rounding and item spacing

            // Pop global styling
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor();
        }

        private string FormatTimeSpan(TimeSpan time)
        {
            if (time.TotalSeconds <= 0)
                return "Due Now";

            if (time.TotalHours >= 1)
                return $"{(int)time.TotalHours}h {time.Minutes:D2}m {time.Seconds:D2}s";

            if (time.TotalMinutes >= 1)
                return $"{(int)time.TotalMinutes}m {time.Seconds:D2}s";

            return $"{time.Seconds}s";
        }

        public void UpdateStatus()
        {
            if (_configuration.MonitorEnabled)
            {
                _logStatus = "MONITORING ACTIVE";
                _statusColor = _activeColor;
            }
            else
            {
                _logStatus = "MONITORING DISABLED";
                _statusColor = _inactiveColor;
            }
        }

        public void SetLastFoundEntry(string entry)
        {
            _configuration.LastFoundEntry = entry;
            _plugin.SaveConfiguration();
        }

        public void Dispose() { }
    }
}

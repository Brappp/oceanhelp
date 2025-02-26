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
        private string _logStatus = "Inactive";

        // Refined Color Palette
        private readonly Vector4 _activeColor = new Vector4(0.2f, 0.85f, 0.3f, 1f);    // Soft Green
        private readonly Vector4 _inactiveColor = new Vector4(0.85f, 0.2f, 0.2f, 1f);  // Soft Red
        private readonly Vector4 _timerColor = new Vector4(0.6f, 0.75f, 1f, 1f);      // Gentle Blue
        private readonly Vector4 _pendingColor = new Vector4(0.95f, 0.75f, 0.3f, 1f); // Soft Orange
        private readonly Vector4 _separatorColor = new Vector4(0.5f, 0.5f, 0.5f, 1f); // Dark Gray

        private Vector4 _statusColor;

        public MainWindow(Plugin plugin) : base("Log Monitor###LogMonitor", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar)
        {
            _plugin = plugin;
            _configuration = plugin.Configuration;
            Size = new Vector2(420, 270);
            SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(400, 250), MaximumSize = new Vector2(500, 320) };
            UpdateStatus();
        }

        public override void Draw()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 5));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 3));

            float contentWidth = ImGui.GetWindowWidth() - 20;

            // === Status Section ===
            ImGui.TextColored(_statusColor, _logStatus);
            ImGui.SameLine();
            ImGui.TextDisabled($"Next: {FormatTimeSpan(_plugin.TimeUntilNextCheck)}");

            if (_plugin.HasPendingExecution && _plugin.TimeUntilCommand.HasValue)
            {
                ImGui.SameLine();
                ImGui.TextColored(_pendingColor, $"Exec: {FormatTimeSpan(_plugin.TimeUntilCommand.Value)}");
            }

            DrawSeparator();

            // Last Log Info (Aligned)
            ImGui.TextDisabled($"Last: {_configuration.LastProcessedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");

            if (!string.IsNullOrEmpty(_configuration.LastFoundEntry))
            {
                ImGui.TextDisabled("Entry:");
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 0.4f)); // Subtle background
                ImGui.PushTextWrapPos(ImGui.GetWindowWidth() - 20);
                ImGui.TextColored(_timerColor, _configuration.LastFoundEntry);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }

            DrawSeparator();

            // === Controls ===
            // Monitor Toggle & Interval Adjustment
            bool monitorLogs = _configuration.MonitorEnabled;
            if (ImGui.Checkbox("Enable", ref monitorLogs))
            {
                _configuration.MonitorEnabled = monitorLogs;
                _plugin.SaveConfiguration();
                UpdateStatus();
            }

            ImGui.SameLine();
            ImGui.TextDisabled("Interval:");
            ImGui.SameLine();

            int interval = _configuration.CheckIntervalMinutes;

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
            if (ImGui.Button(" - ", new Vector2(25, 22)))
            {
                interval = Math.Max(1, interval - 1);
                _configuration.CheckIntervalMinutes = interval;
                _plugin.SaveConfiguration();
            }
            ImGui.SameLine();
            ImGui.Text($"{interval} min");
            ImGui.SameLine();
            if (ImGui.Button(" + ", new Vector2(25, 22)))
            {
                interval = Math.Min(60, interval + 1);
                _configuration.CheckIntervalMinutes = interval;
                _plugin.SaveConfiguration();
            }
            ImGui.PopStyleVar();

            DrawSeparator();

            // Chat Command Input
            ImGui.TextDisabled("Cmd:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(contentWidth - 60);
            string command = _configuration.ChatCommand;
            if (ImGui.InputText("##Cmd", ref command, 256, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _configuration.ChatCommand = command;
                _plugin.SaveConfiguration();
            }

            // Log Directory Input
            ImGui.TextDisabled("Dir:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(contentWidth - 60);
            string logDir = _plugin.LogDirectory;
            if (ImGui.InputText("##Dir", ref logDir, 512, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _plugin.LogDirectory = logDir;
                _configuration.LogDirectory = logDir;
                _plugin.SaveConfiguration();
            }

            DrawSeparator();

            // === Buttons (Force Check & Reset) ===
            float buttonWidth = (contentWidth / 2) - 5;

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f); // Rounded buttons for a polished look
            if (ImGui.Button("🔍 Check", new Vector2(buttonWidth, 24)))
            {
                _plugin.CheckLogs(true);
                UpdateStatus();
            }
            ImGui.SameLine();
            if (ImGui.Button("🔄 Reset", new Vector2(buttonWidth, 24)))
            {
                _configuration.LastProcessedTime = null;
                _configuration.LastFoundEntry = null;
                _plugin.SaveConfiguration();
                UpdateStatus();
            }
            ImGui.PopStyleVar();

            ImGui.PopStyleVar(2);
        }

        private void DrawSeparator()
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Separator, _separatorColor);
            ImGui.Separator();
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        private string FormatTimeSpan(TimeSpan time)
        {
            if (time.TotalSeconds <= 0) return "Now";
            return $"{(int)time.TotalMinutes}m {time.Seconds}s";
        }

        public void UpdateStatus()
        {
            _logStatus = _configuration.MonitorEnabled ? "ACTIVE" : "DISABLED";
            _statusColor = _configuration.MonitorEnabled ? _activeColor : _inactiveColor;
        }

        public void Dispose() { }
    }
}

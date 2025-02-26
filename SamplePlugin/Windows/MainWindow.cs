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

        // Professional color palette
        private readonly Vector4 _activeColor = new Vector4(0.2f, 0.85f, 0.3f, 1f);      // Active green
        private readonly Vector4 _inactiveColor = new Vector4(0.85f, 0.2f, 0.2f, 1f);    // Inactive red
        private readonly Vector4 _timerColor = new Vector4(0.75f, 0.85f, 1f, 1f);        // Light blue for time
        private readonly Vector4 _pendingColor = new Vector4(0.95f, 0.75f, 0.3f, 1f);    // Amber for pending
        private readonly Vector4 _borderColor = new Vector4(0.3f, 0.3f, 0.35f, 0.5f);    // Subtle border
        private readonly Vector4 _labelColor = new Vector4(0.7f, 0.7f, 0.7f, 1f);        // Label text

        private Vector4 _statusColor;

        public MainWindow(Plugin plugin)
            : base("Log Monitor###LogMonitor",
                  ImGuiWindowFlags.NoResize |
                  ImGuiWindowFlags.NoScrollbar |
                  ImGuiWindowFlags.NoScrollWithMouse)
        {
            _plugin = plugin;
            _configuration = plugin.Configuration;
            Size = new Vector2(340, 178); // Even more compact height
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(320, 170),
                MaximumSize = new Vector2(380, 190)
            };
            UpdateStatus();
        }

        public override void Draw()
        {
            // Ultra-compact styling
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(7, 5));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3, 2));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2);

            ImGui.PushStyleColor(ImGuiCol.Border, _borderColor);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.14f, 0.14f, 0.16f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.2f, 0.24f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.12f, 0.12f, 0.15f, 0.8f));
            ImGui.PushStyleColor(ImGuiCol.CheckMark, _activeColor);

            float contentWidth = ImGui.GetContentRegionAvail().X;

            // === STATUS BAR - ultra-compact three-column layout ===
            ImGui.TextColored(_statusColor, _logStatus);
            ImGui.SameLine(contentWidth * 0.34f); // Position for "Next" 
            ImGui.TextColored(_timerColor, $"Next: {FormatTimeSpan(_plugin.TimeUntilNextCheck)}");

            string execText = _plugin.TimeUntilCommand.HasValue ? FormatTimeSpan(_plugin.TimeUntilCommand.Value) : "Waiting";
            ImGui.SameLine(contentWidth * 0.68f); // Position for "Exec"
            ImGui.TextColored(_pendingColor, $"Exec: {execText}");

            // === LAST PROCESSED TIME on same row as log entry label ===
            ImGui.TextColored(_labelColor, $"Last: {_configuration.LastProcessedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");

            // === LOG ENTRY - with text wrapping and dynamic height ===
            if (!string.IsNullOrEmpty(_configuration.LastFoundEntry))
            {
                // Calculate appropriate height based on content
                float textWidth = ImGui.CalcTextSize(_configuration.LastFoundEntry).X;
                float availableWidth = contentWidth - 16; // Account for padding
                float textHeight = ImGui.GetTextLineHeight();

                // Determine if text will wrap and calculate needed height
                int lines = 1;
                if (textWidth > availableWidth)
                {
                    // Estimate number of lines needed (approximate)
                    lines = Math.Min(3, (int)Math.Ceiling(textWidth / availableWidth));
                }

                float boxHeight = (textHeight * lines) + 6; // Add padding

                // Display with wrapped text
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.10f, 0.10f, 0.12f, 0.8f));
                ImGui.BeginChild("##Entry", new Vector2(contentWidth, boxHeight), true,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

                // Push width constraint for wrapping and render text
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + availableWidth);
                ImGui.TextColored(_timerColor, _configuration.LastFoundEntry);
                ImGui.PopTextWrapPos();

                ImGui.EndChild();
                ImGui.PopStyleColor();
            }

            // === ALL CONTROLS ON ONE ROW - With more space ===
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 2));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, 2));

            // Enable checkbox
            bool monitorLogs = _configuration.MonitorEnabled;
            if (ImGui.Checkbox("##EnableBox", ref monitorLogs))
            {
                _configuration.MonitorEnabled = monitorLogs;
                _plugin.SaveConfiguration();
                UpdateStatus();
            }

            ImGui.SameLine(0, 1);
            ImGui.Text("Enable");

            // More space between controls
            ImGui.SameLine(85);
            ImGui.Text("Int:");
            ImGui.SameLine(0, 5);

            int interval = _configuration.CheckIntervalMinutes;

            if (ImGui.Button("-", new Vector2(16, 20)))
            {
                interval = Math.Max(1, interval - 1);
                _configuration.CheckIntervalMinutes = interval;
                _plugin.SaveConfiguration();
            }

            ImGui.SameLine(0, 3);
            ImGui.Text($"{interval}");

            ImGui.SameLine(0, 3);
            if (ImGui.Button("+", new Vector2(16, 20)))
            {
                interval = Math.Min(60, interval + 1);
                _configuration.CheckIntervalMinutes = interval;
                _plugin.SaveConfiguration();
            }

            // Check and Reset buttons on same row - much smaller fixed width
            float buttonWidth = 55; // Fixed small width for each button

            ImGui.SameLine(contentWidth - (buttonWidth * 2) - 2);
            if (ImGui.Button("Check", new Vector2(buttonWidth, 20)))
            {
                _plugin.CheckLogs(true);
                UpdateStatus();
            }

            ImGui.SameLine(0, 2);
            if (ImGui.Button("Reset", new Vector2(buttonWidth, 20)))
            {
                _configuration.LastProcessedTime = null;
                _configuration.LastFoundEntry = null;
                _plugin.SaveConfiguration();
                UpdateStatus();
            }

            ImGui.PopStyleVar(2);

            // Cmd + Dir inputs
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));

            ImGui.Text("Cmd:");
            ImGui.SameLine(40);
            ImGui.SetNextItemWidth(contentWidth - 43);
            string command = _configuration.ChatCommand;
            if (ImGui.InputText("##Cmd", ref command, 256))
            {
                _configuration.ChatCommand = command;
                _plugin.SaveConfiguration();
            }

            ImGui.Text("Dir:");
            ImGui.SameLine(40);
            ImGui.SetNextItemWidth(contentWidth - 43);
            string logDir = _plugin.LogDirectory;
            if (ImGui.InputText("##Dir", ref logDir, 512))
            {
                _plugin.LogDirectory = logDir;
                _configuration.LogDirectory = logDir;
                _plugin.SaveConfiguration();
            }

            ImGui.PopStyleVar();

            // Restore styling
            ImGui.PopStyleColor(5);
            ImGui.PopStyleVar(4);
        }

        /// <summary>
        /// Formats a TimeSpan into a compact string representation.
        /// </summary>
        private string FormatTimeSpan(TimeSpan time)
        {
            if (time.TotalSeconds <= 0)
            {
                return "Now";
            }
            return $"{(int)time.TotalMinutes}m {time.Seconds}s";
        }

        /// <summary>
        /// Updates the status text and color based on the monitor state.
        /// </summary>
        public void UpdateStatus()
        {
            _logStatus = _configuration.MonitorEnabled ? "ACTIVE" : "DISABLED";
            _statusColor = _configuration.MonitorEnabled ? _activeColor : _inactiveColor;
        }

        public void Dispose()
        {
            // Cleanup resources if necessary.
        }
    }
}

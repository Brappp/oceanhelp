using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using SamplePlugin;

namespace SamplePlugin.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private readonly Configuration _configuration;
        private readonly Plugin _plugin;
        private string _logStatus = "Inactive";

        // Color palette for UI elements
        private readonly Vector4 _activeColor = new Vector4(0.2f, 0.85f, 0.3f, 1f);
        private readonly Vector4 _inactiveColor = new Vector4(0.85f, 0.2f, 0.2f, 1f);
        private readonly Vector4 _timerColor = new Vector4(0.75f, 0.85f, 1f, 1f);
        private readonly Vector4 _pendingColor = new Vector4(0.95f, 0.75f, 0.3f, 1f);
        private readonly Vector4 _borderColor = new Vector4(0.3f, 0.3f, 0.35f, 0.5f);
        private readonly Vector4 _labelColor = new Vector4(0.7f, 0.7f, 0.7f, 1f);
        private readonly Vector4 _boatColor = new Vector4(0.3f, 0.8f, 0.9f, 1f);

        private Vector4 _statusColor;

        public MainWindow(Plugin plugin)
            : base("Log Monitor###LogMonitor", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            _plugin = plugin;
            _configuration = plugin.Configuration;
            // Increase the window size to accommodate boat time display and pre-arrival command
            Size = new Vector2(400, 240);
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(380, 240),
                MaximumSize = new Vector2(500, 330)
            };

            UpdateStatus();
        }

        public override void Draw()
        {
            // Push compact styling
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

            // --- Row 1: Status Bar ---
            ImGui.TextColored(_statusColor, _logStatus);
            ImGui.SameLine(contentWidth * 0.34f);
            ImGui.TextColored(_timerColor, $"Next: {FormatTimeSpan(_plugin.TimeUntilNextCheck)}");
            ImGui.SameLine(contentWidth * 0.68f);
            string execText = _plugin.TimeUntilCommand.HasValue ? FormatTimeSpan(_plugin.TimeUntilCommand.Value) : "Waiting";
            ImGui.TextColored(_pendingColor, $"Exec: {execText}");

            // --- Row 2: Last Processed Time ---
            ImGui.TextColored(_labelColor, $"Last: {_configuration.LastProcessedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");

            // --- Row 2.5: Next Boat Time (Eastern Time with proper DST handling) ---
            if (_plugin.NextBoatTimeUtc.HasValue)
            {
                // Convert UTC boat time to Eastern time with DST support
                DateTime estBoatTime = ConvertUtcToEst(_plugin.NextBoatTimeUtc.Value);
                TimeSpan? timeUntilBoat = _plugin.TimeUntilNextBoat;

                // Display the next boat time with correct timezone label in 12-hour format
                string timeAbbr = TimeZoneHelper.GetEasternTimeAbbreviation(estBoatTime);
                ImGui.TextColored(_boatColor, $"Boat: {estBoatTime:hh:mm:ss tt (MM/dd)} {timeAbbr}");
                ImGui.SameLine(contentWidth * 0.70f);

                // Display countdown with color changes based on time remaining
                if (timeUntilBoat.HasValue)
                {
                    string countdownText = FormatLongTimeSpan(timeUntilBoat.Value);

                    // Determine color based on time remaining
                    Vector4 countdownColor = _boatColor;

                    // Change color to yellow when 10 minutes or less remain
                    if (timeUntilBoat.Value.TotalMinutes <= 10)
                    {
                        countdownColor = new Vector4(0.95f, 0.85f, 0.1f, 1f); // Yellow
                    }

                    // Change color to red when 3 minutes or less remain
                    if (timeUntilBoat.Value.TotalMinutes <= 3)
                    {
                        countdownColor = new Vector4(0.95f, 0.2f, 0.2f, 1f); // Red
                    }

                    // Make text bigger by pushing font scale
                    float originalScale = ImGui.GetFont().Scale;
                    ImGui.GetFont().Scale *= 1.2f; // Increase size by 20%
                    ImGui.PushFont(ImGui.GetFont());

                    ImGui.TextColored(countdownColor, countdownText);

                    // Restore original font size
                    ImGui.GetFont().Scale = originalScale;
                    ImGui.PopFont();
                }
                else
                {
                    // Make text bigger by pushing font scale
                    float originalScale = ImGui.GetFont().Scale;
                    ImGui.GetFont().Scale *= 1.2f; // Increase size by 20%
                    ImGui.PushFont(ImGui.GetFont());

                    ImGui.TextColored(new Vector4(0.95f, 0.2f, 0.2f, 1f), "Arriving soon!");

                    // Restore original font size
                    ImGui.GetFont().Scale = originalScale;
                    ImGui.PopFont();
                }
            }
            else
            {
                ImGui.TextColored(_labelColor, "Next boat: Unknown");
            }

            // --- Row 3: Log Entry Display (fixed height) ---
            float childHeight = 24;
            if (!string.IsNullOrEmpty(_configuration.LastFoundEntry))
            {
                ImGui.BeginChild("LogEntry", new Vector2(0, childHeight), true, ImGuiWindowFlags.NoScrollbar);
                ImGui.TextWrapped(_configuration.LastFoundEntry);
                ImGui.EndChild();
            }
            else
            {
                ImGui.Dummy(new Vector2(0, childHeight));
            }

            // --- Row 4: Controls (Enable, Interval, Delete) in a table ---
            if (ImGui.BeginTable("ControlsTable", 3, ImGuiTableFlags.RowBg))
            {
                // Column 1: Enable checkbox
                ImGui.TableNextColumn();
                bool monitorLogs = _configuration.MonitorEnabled;
                if (ImGui.Checkbox("Enable", ref monitorLogs))
                {
                    _configuration.MonitorEnabled = monitorLogs;
                    _plugin.SaveConfiguration();
                    UpdateStatus();
                }

                // Column 2: Interval controls (-, value, +)
                ImGui.TableNextColumn();
                ImGui.Text("Int:");
                ImGui.SameLine();
                if (ImGui.Button("-", new Vector2(16, 20)))
                {
                    _configuration.CheckIntervalMinutes = Math.Max(1, _configuration.CheckIntervalMinutes - 1);
                    _plugin.SaveConfiguration();
                }
                ImGui.SameLine();
                ImGui.Text($"{_configuration.CheckIntervalMinutes}");
                ImGui.SameLine();
                if (ImGui.Button("+", new Vector2(16, 20)))
                {
                    _configuration.CheckIntervalMinutes = Math.Min(60, _configuration.CheckIntervalMinutes + 1);
                    _plugin.SaveConfiguration();
                }

                // Column 3: Delete old log files checkbox
                ImGui.TableNextColumn();
                bool deleteOldFiles = _configuration.DeleteOldFiles;
                if (ImGui.Checkbox("Delete", ref deleteOldFiles))
                {
                    _configuration.DeleteOldFiles = deleteOldFiles;
                    _plugin.SaveConfiguration();
                }
                ImGui.EndTable();
            }

            // --- Row 5: Buttons (Check, Reset) ---
            if (ImGui.BeginTable("ButtonsTable", 2, ImGuiTableFlags.None))
            {
                ImGui.TableNextColumn();
                if (ImGui.Button("Check", new Vector2(85, 20)))
                {
                    _plugin.CheckLogs(true);
                    UpdateStatus();
                }
                ImGui.TableNextColumn();
                if (ImGui.Button("Reset", new Vector2(85, 20)))
                {
                    _configuration.LastProcessedTime = null;
                    _configuration.LastFoundEntry = null;
                    _configuration.LastProcessedFileName = null;
                    _configuration.NextBoatTimeUtc = null;
                    _configuration.NextBoatMinutes = null;
                    _plugin.SaveConfiguration();
                    UpdateStatus();
                }
                ImGui.EndTable();
            }

            // --- Row 6: Command and Directory Inputs ---
            ImGui.Text("Cmd:");
            ImGui.SameLine(40);
            ImGui.SetNextItemWidth(contentWidth - 43);
            string command = _configuration.ChatCommand;
            if (ImGui.InputText("##Cmd", ref command, 256))
            {
                _configuration.ChatCommand = command;
                _plugin.SaveConfiguration();
            }

            // --- Row 7: Pre-Arrival Command Input ---
            ImGui.Text("Pre:");
            ImGui.SameLine(40);
            ImGui.SetNextItemWidth(contentWidth - 43);
            string preArrivalCmd = _configuration.PreArrivalCommand;
            if (ImGui.InputText("##PreCmd", ref preArrivalCmd, 256))
            {
                _configuration.PreArrivalCommand = preArrivalCmd;
                _plugin.SaveConfiguration();
            }

            // --- Row 8: Directory Input ---
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

            // Restore styling
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(5);
        }

        private string FormatTimeSpan(TimeSpan time)
        {
            if (time.TotalSeconds <= 0)
                return "Now";
            return $"{(int)time.TotalMinutes}m {time.Seconds}s";
        }

        private string FormatLongTimeSpan(TimeSpan time)
        {
            if (time.TotalSeconds <= 0)
                return "Now!";

            if (time.TotalHours >= 1)
            {
                return $"{(int)time.TotalHours}h {time.Minutes}m {time.Seconds}s";
            }

            return $"{time.Minutes}m {time.Seconds}s";
        }

        public void UpdateStatus()
        {
            _logStatus = _configuration.MonitorEnabled ? "ACTIVE" : "DISABLED";
            _statusColor = _configuration.MonitorEnabled ? _activeColor : _inactiveColor;
        }

        // Helper method to convert UTC to Eastern time with DST support
        private DateTime ConvertUtcToEst(DateTime utcTime)
        {
            return TimeZoneHelper.ConvertUtcToEastern(utcTime);
        }

        public void Dispose()
        {
            // Cleanup resources if necessary.
        }
    }
}

using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using SamplePlugin;

namespace SamplePlugin.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Plugin _plugin;
        private readonly Configuration _configuration;

        public ConfigWindow(Plugin plugin)
            : base("Plugin Configuration###ConfigWindow", ImGuiWindowFlags.AlwaysAutoResize)
        {
            _plugin = plugin;
            _configuration = plugin.Configuration;
            IsOpen = false; // Start closed
        }

        public override void Draw()
        {
            if (!IsOpen) return;
            ImGui.Text("Plugin Configuration");
            ImGui.Separator();

            // Monitor Enabled
            bool monitorEnabled = _configuration.MonitorEnabled;
            if (ImGui.Checkbox("Monitor Enabled", ref monitorEnabled))
            {
                _configuration.MonitorEnabled = monitorEnabled;
                _plugin.SaveConfiguration();
            }

            // Check Interval
            int interval = _configuration.CheckIntervalMinutes;
            if (ImGui.InputInt("Check Interval (minutes)", ref interval))
            {
                if (interval < 1) interval = 1;
                if (interval > 60) interval = 60;
                _configuration.CheckIntervalMinutes = interval;
                _plugin.SaveConfiguration();
            }

            // Chat Command
            string chatCommand = _configuration.ChatCommand;
            if (ImGui.InputText("Chat Command", ref chatCommand, 256))
            {
                _configuration.ChatCommand = chatCommand;
                _plugin.SaveConfiguration();
            }

            // Log Directory
            string logDir = _configuration.LogDirectory;
            if (ImGui.InputText("Log Directory", ref logDir, 512))
            {
                _configuration.LogDirectory = logDir;
                _plugin.LogDirectory = logDir;
                _plugin.SaveConfiguration();
            }

            // Delete Old Files Option
            bool deleteOldFiles = _configuration.DeleteOldFiles;
            if (ImGui.Checkbox("Delete Old Log Files", ref deleteOldFiles))
            {
                _configuration.DeleteOldFiles = deleteOldFiles;
                _plugin.SaveConfiguration();
            }

            if (ImGui.Button("Close"))
            {
                IsOpen = false;
            }
        }

        public void Dispose()
        {
            // Cleanup resources if necessary.
        }
    }
}

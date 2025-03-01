using Dalamud.Configuration;
using System;

namespace SamplePlugin
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        // Core settings
        public bool MonitorEnabled { get; set; } = true;
        public int CheckIntervalMinutes { get; set; } = 5;
        public string ChatCommand { get; set; } = "/echo Ocean trip found!";

        // Pre-arrival command (1 minute before boat arrives)
        public string PreArrivalCommand { get; set; } = "/echo Boat arriving in 1 minute!";

        // Path settings
        public string LogDirectory { get; set; } = @"D:\Rebornbuddy64 1.0.679.0\Logs";

        // Tracking info
        public DateTime? LastProcessedTime { get; set; } = null;
        public string LastFoundEntry { get; set; } = null;
        public string LastProcessedFileName { get; set; } = null;

        // Next boat time tracking
        public DateTime? NextBoatTimeUtc { get; set; } = null;
        public int? NextBoatMinutes { get; set; } = null;

        // Option to delete old log files after processing
        public bool DeleteOldFiles { get; set; } = false;
    }
}

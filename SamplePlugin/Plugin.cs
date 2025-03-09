using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using SamplePlugin.Windows;
using Dalamud.Plugin.Services;
using System.Linq;
using Dalamud.IoC;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SamplePlugin
{
    public class Plugin : IDalamudPlugin
    {
        public string Name => "Ocean Log Monitor";
        public Configuration Configuration { get; private set; }
        private const string CommandName = "/logmonitor";

        // Public property for MainWindow access
        public string LogDirectory { get; set; }

        // Timer tracking fields
        private DateTime _lastCheckTime = DateTime.Now;
        private DateTime? _nextCommandExecutionTime;
        private bool _pendingExecution;

        // Next boat time tracking (new)
        public DateTime? NextBoatTimeUtc { get; private set; }
        public int? NextBoatMinutes { get; private set; }
        private CancellationTokenSource _countdownTokenSource;
        private Task _countdownTask;

        // Timer properties for UI
        public TimeSpan TimeUntilNextCheck =>
            _lastCheckTime.AddMinutes(Configuration.CheckIntervalMinutes) - DateTime.Now;

        public TimeSpan? TimeUntilCommand =>
            _nextCommandExecutionTime.HasValue ?
            _nextCommandExecutionTime.Value - DateTime.Now :
            null;

        // Next boat time properties (new)
        public TimeSpan? TimeUntilNextBoat =>
            NextBoatTimeUtc.HasValue ?
            NextBoatTimeUtc.Value - DateTime.UtcNow :
            null;

        // Flag to track if pre-arrival command has been executed
        private bool _preArrivalCommandExecuted = false;

        // Convert UTC time to EST time
        public DateTime? NextBoatTimeEst =>
            NextBoatTimeUtc.HasValue ?
            ConvertUtcToEst(NextBoatTimeUtc.Value) :
            null;

        private CancellationTokenSource _cancellationTokenSource;
        private readonly WindowSystem _windowSystem;
        private readonly MainWindow _mainWindow;
        private Task _monitoringTask;

        [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] public static IChatGui ChatGui { get; private set; } = null!;

        public Plugin()
        {
            // Initialize configuration with defaults
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

            // Ensure log directory is set
            LogDirectory = Configuration.LogDirectory ?? @"D:\Rebornbuddy64 1.0.679.0\Logs";

            // Validate and set configuration defaults
            EnsureConfigurationDefaults();

            // Save initial configuration
            SaveConfiguration();

            // Register chat command
            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Commands: /logmonitor forcecheck, /logmonitor status (includes pre-arrival command info)"
            });

            // Setup window system and UI
            _windowSystem = new WindowSystem(Name);
            _mainWindow = new MainWindow(this);
            _windowSystem.AddWindow(_mainWindow);

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;

            // Restore next boat time if available with proper Kind setting
            if (Configuration.NextBoatTimeUtc.HasValue)
            {
                // Ensure the restored time is properly marked as UTC
                NextBoatTimeUtc = DateTime.SpecifyKind(Configuration.NextBoatTimeUtc.Value, DateTimeKind.Utc);
                NextBoatMinutes = Configuration.NextBoatMinutes;

                // Important: Reset the pre-arrival command execution flag on startup
                _preArrivalCommandExecuted = false;

                // Start countdown timer for commands
                StartCountdownTimer();

                // Log that we restored boat time
                PluginLog.Information($"Restored next boat time: {NextBoatTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");
                if (TimeUntilNextBoat.HasValue)
                {
                    PluginLog.Information($"Time until next boat: {TimeUntilNextBoat.Value.TotalMinutes:F1} minutes");
                }
            }

            // Start monitoring logs
            StartMonitoring();
            PluginLog.Information($"{Name} initialized with log directory: {LogDirectory}");
        }

        // Helper method to convert UTC to Eastern time with DST support
        private DateTime ConvertUtcToEst(DateTime utcTime)
        {
            return TimeZoneHelper.ConvertUtcToEastern(utcTime);
        }

        private void EnsureConfigurationDefaults()
        {
            // Use existing values or defaults
            Configuration.MonitorEnabled = Configuration.MonitorEnabled;
            Configuration.CheckIntervalMinutes = Configuration.CheckIntervalMinutes > 0
                ? Configuration.CheckIntervalMinutes
                : 5;
            Configuration.ChatCommand ??= "/echo Ocean trip found!";
            // Default: do not delete old files unless explicitly enabled
            Configuration.DeleteOldFiles = Configuration.DeleteOldFiles;
        }

        public void Dispose()
        {
            StopMonitoring();
            StopCountdownTimer();
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
            CommandManager.RemoveHandler(CommandName);
            _windowSystem.RemoveAllWindows();
        }

        private void OnCommand(string command, string args)
        {
            switch (args.ToLower())
            {
                case "forcecheck":
                    PluginLog.Information("Manual log check triggered");
                    CheckLogs(true);
                    break;
                case "status":
                    var status = Configuration.MonitorEnabled ? "enabled" : "disabled";
                    var pendingStatus = _pendingExecution ? " (Command execution pending)" : "";
                    ChatGui.Print($"Ocean Log Monitor is {status}{pendingStatus}. Checking every {Configuration.CheckIntervalMinutes} minutes.");
                    if (Configuration.LastProcessedTime.HasValue)
                    {
                        ChatGui.Print($"Last check: {Configuration.LastProcessedTime.Value:yyyy-MM-dd HH:mm:ss.fff}");
                    }
                    if (NextBoatTimeUtc.HasValue && NextBoatTimeEst.HasValue)
                    {
                        // Update to show Eastern Time with proper DST handling in 12-hour format
                        string timeAbbr = TimeZoneHelper.GetEasternTimeAbbreviation(NextBoatTimeEst.Value);
                        ChatGui.Print($"Next boat arrival: {NextBoatTimeEst:yyyy-MM-dd hh:mm:ss tt} {timeAbbr} time");

                        TimeSpan? timeUntilBoat = TimeUntilNextBoat;
                        if (timeUntilBoat.HasValue)
                        {
                            ChatGui.Print($"Time until next boat: {timeUntilBoat.Value.ToString(@"hh\:mm\:ss")}");
                            if (timeUntilBoat.Value.TotalMinutes > 1)
                            {
                                ChatGui.Print($"Pre-arrival command will execute in {(timeUntilBoat.Value.TotalMinutes - 1):F1} minutes");
                            }

                            // If less than 1 minute left and command not yet executed, show countdown
                            if (timeUntilBoat.Value.TotalMinutes <= 1 && !_preArrivalCommandExecuted)
                            {
                                ChatGui.Print("Pre-arrival command will execute very soon!");
                            }
                            else if (_preArrivalCommandExecuted)
                            {
                                ChatGui.Print("Pre-arrival command has already been executed.");
                            }
                        }
                    }
                    ChatGui.Print($"Main command: {Configuration.ChatCommand}");
                    ChatGui.Print($"Pre-arrival command: {Configuration.PreArrivalCommand}");
                    break;
                default:
                    ChatGui.Print("Available commands: /logmonitor forcecheck, /logmonitor status");
                    break;
            }
        }

        private void StartMonitoring()
        {
            StopMonitoring();
            _cancellationTokenSource = new CancellationTokenSource();
            _monitoringTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (Configuration.MonitorEnabled && !_pendingExecution)
                    {
                        CheckLogs(false);
                    }
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(Configuration.CheckIntervalMinutes), _cancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        private void StartCountdownTimer()
        {
            StopCountdownTimer();

            if (!NextBoatTimeUtc.HasValue)
                return;

            // Reset pre-arrival command execution flag
            _preArrivalCommandExecuted = false;
            PluginLog.Information($"Starting countdown timer, reset pre-arrival flag to false");

            _countdownTokenSource = new CancellationTokenSource();
            _countdownTask = Task.Run(async () =>
            {
                while (!_countdownTokenSource.Token.IsCancellationRequested)
                {
                    // Check if boat time has passed
                    if (NextBoatTimeUtc.HasValue && DateTime.UtcNow > NextBoatTimeUtc.Value)
                    {
                        PluginLog.Information("Boat arrival time passed. Clearing next boat time.");
                        NextBoatTimeUtc = null;
                        NextBoatMinutes = null;
                        Configuration.NextBoatTimeUtc = null;
                        Configuration.NextBoatMinutes = null;
                        _preArrivalCommandExecuted = false;
                        SaveConfiguration();
                        _mainWindow.UpdateStatus();
                        break;
                    }

                    // Check if we should execute the pre-arrival command
                    // Changed condition from 5 minutes to 1 minute to match original code
                    if (NextBoatTimeUtc.HasValue && !_preArrivalCommandExecuted &&
                        TimeUntilNextBoat.HasValue && TimeUntilNextBoat.Value.TotalMinutes <= 1.0 &&
                        TimeUntilNextBoat.Value.TotalSeconds > 0)
                    {
                        PluginLog.Information($"Pre-arrival check: Minutes={TimeUntilNextBoat.Value.TotalMinutes:F2}, Seconds={TimeUntilNextBoat.Value.TotalSeconds:F1}");
                        PluginLog.Information("Executing pre-arrival command (1 minute before boat arrives).");

                        _preArrivalCommandExecuted = true;

                        // Execute command if it's set
                        if (!string.IsNullOrWhiteSpace(Configuration.PreArrivalCommand))
                        {
                            try
                            {
                                PluginLog.Information($"Executing pre-arrival command: {Configuration.PreArrivalCommand}");
                                ExecuteChatCommand(Configuration.PreArrivalCommand);
                                ChatGui.Print("Pre-arrival command executed!");
                            }
                            catch (Exception ex)
                            {
                                PluginLog.Error($"Error executing pre-arrival command: {ex.Message}");
                                ChatGui.PrintError($"Error executing pre-arrival command: {ex.Message}");
                            }
                        }
                        else
                        {
                            PluginLog.Information("No pre-arrival command set, skipping execution");
                        }
                    }

                    // Periodic logging of time remaining
                    if (NextBoatTimeUtc.HasValue && TimeUntilNextBoat.HasValue &&
                        (int)TimeUntilNextBoat.Value.TotalMinutes % 5 == 0 &&
                        TimeUntilNextBoat.Value.Seconds == 0)
                    {
                        int minutes = (int)TimeUntilNextBoat.Value.TotalMinutes;
                        PluginLog.Information($"Time until boat: {minutes} minutes");
                    }

                    // Update UI
                    _mainWindow.UpdateStatus();

                    try
                    {
                        // Update every second
                        await Task.Delay(TimeSpan.FromSeconds(1), _countdownTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, _countdownTokenSource.Token);
        }

        private void StopCountdownTimer()
        {
            if (_countdownTokenSource != null)
            {
                _countdownTokenSource.Cancel();
                _countdownTask?.Wait();
                _countdownTokenSource.Dispose();
                _countdownTokenSource = null;
            }
        }

        private void StopMonitoring()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _monitoringTask?.Wait();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public void CheckLogs(bool isForced)
        {
            // If a command is pending and not forced, skip the check
            if (_pendingExecution && !isForced)
            {
                PluginLog.Information("Skipping check due to pending command execution");
                return;
            }

            try
            {
                _lastCheckTime = DateTime.Now;
                DateTime currentTime = DateTime.Now;

                // Ensure the log directory exists
                if (!Directory.Exists(LogDirectory))
                {
                    ChatGui.PrintError($"Log directory not found: {LogDirectory}");
                    throw new DirectoryNotFoundException($"Log directory not found: {LogDirectory}");
                }

                // Retrieve all log files sorted by last write time (most recent first)
                var logFiles = Directory.GetFiles(LogDirectory, "*.txt")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToArray();

                if (logFiles.Length == 0)
                {
                    ChatGui.PrintError($"No log files found in directory: {LogDirectory}");
                    throw new FileNotFoundException($"No log files found in directory: {LogDirectory}");
                }

                ChatGui.Print($"Checking {logFiles.Length} log files");
                PluginLog.Information($"Checking {logFiles.Length} log files from {LogDirectory}");

                // Use the stored last processed time and file name for comparison
                DateTime lastProcessedTime = Configuration.LastProcessedTime ?? DateTime.MinValue;
                string storedFile = Configuration.LastProcessedFileName;

                PluginLog.Information($"Last processed time: {lastProcessedTime:yyyy-MM-dd HH:mm:ss.fff}");

                // Local candidate variables for the most recent matching entry
                DateTime? candidateEntryTime = null;
                string candidateEntryLine = null;
                string candidateFile = null;
                int? candidateMinutes = null;
                const string targetPhrase = "Next boat is in";

                // Process every log file
                foreach (var file in logFiles)
                {
                    FileInfo fileInfo = new FileInfo(file);
                    // Get the file's last write time for context
                    DateTime fileLastWriteTime = fileInfo.LastWriteTime;
                    PluginLog.Information($"Examining log file: {file} from {fileLastWriteTime:yyyy-MM-dd HH:mm:ss}");

                    try
                    {
                        using (StreamReader reader = new StreamReader(file))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (line.Contains("[Ocean Trip]") && line.Contains(targetPhrase))
                                {
                                    // Extract the timestamp more precisely
                                    var timestampMatch = Regex.Match(line, @"\[([\d:\.]+)\s+\w\]");
                                    if (timestampMatch.Success)
                                    {
                                        string timeStr = timestampMatch.Groups[1].Value;
                                        PluginLog.Information($"Found matching line: {line}");
                                        PluginLog.Information($"Time string extracted: {timeStr}");

                                        // Extract minutes until next boat
                                        int minutes = ExtractNextBoatMinutes(line);
                                        PluginLog.Information($"Extracted next boat time: {minutes} minutes");

                                        // Parse the time component from the log
                                        if (TimeSpan.TryParse(timeStr, out TimeSpan logTimeOfDay))
                                        {
                                            // First, determine which day this entry belongs to based on timestamps and current time
                                            DateTime entryDate = DetermineEntryDate(fileLastWriteTime, logTimeOfDay, currentTime);
                                            DateTime entryTime = entryDate.Date + logTimeOfDay;

                                            PluginLog.Information($"Constructed entry time: {entryTime:yyyy-MM-dd HH:mm:ss.fff}");

                                            // Check if this entry is newer than our current candidate
                                            if (!candidateEntryTime.HasValue || IsNewer(entryTime, candidateEntryTime.Value, currentTime))
                                            {
                                                candidateEntryTime = entryTime;
                                                candidateEntryLine = line;
                                                candidateFile = file;
                                                candidateMinutes = minutes;
                                                PluginLog.Information($"New candidate entry found: {entryTime:yyyy-MM-dd HH:mm:ss.fff}");
                                            }
                                            // For entries with same timestamp but from different files
                                            else if (entryTime == candidateEntryTime.Value &&
                                                    (storedFile == null || !string.Equals(file, storedFile, StringComparison.OrdinalIgnoreCase)))
                                            {
                                                candidateEntryTime = entryTime;
                                                candidateEntryLine = line;
                                                candidateFile = file;
                                                candidateMinutes = minutes;
                                                PluginLog.Information($"Same time but new file: {entryTime:yyyy-MM-dd HH:mm:ss.fff}");
                                            }
                                        }
                                        else
                                        {
                                            PluginLog.Error($"Failed to parse time from: {timeStr}");
                                        }
                                    }
                                    else
                                    {
                                        PluginLog.Warning($"Could not extract timestamp from line: {line}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error reading log file {file}: {ex.Message}");
                        ChatGui.PrintError($"Error reading log file {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                if (candidateEntryTime.HasValue && candidateMinutes.HasValue)
                {
                    // Calculate next boat time in UTC (since log times are UTC)
                    // Ensure the calculated time is properly marked as UTC
                    DateTime nextBoatTime = DateTime.SpecifyKind(
                        candidateEntryTime.Value.AddMinutes(candidateMinutes.Value),
                        DateTimeKind.Utc);

                    // Check if this is a new/different boat time
                    bool isNewBoatTime = !NextBoatTimeUtc.HasValue ||
                                         Math.Abs((nextBoatTime - NextBoatTimeUtc.Value).TotalMinutes) > 1;

                    if (isNewBoatTime)
                    {
                        // Get Eastern Time for logging purposes with proper DST handling
                        DateTime estBoatTime = ConvertUtcToEst(nextBoatTime);
                        string timeAbbr = TimeZoneHelper.GetEasternTimeAbbreviation(estBoatTime);

                        PluginLog.Information($"New boat time calculated: {nextBoatTime:yyyy-MM-dd HH:mm:ss} UTC, {estBoatTime:yyyy-MM-dd HH:mm:ss} {timeAbbr}");
                        NextBoatTimeUtc = nextBoatTime;
                        NextBoatMinutes = candidateMinutes.Value;

                        // Save to configuration
                        Configuration.NextBoatTimeUtc = nextBoatTime;
                        Configuration.NextBoatMinutes = candidateMinutes.Value;
                        SaveConfiguration();

                        // Reset pre-arrival execution flag for new boat time
                        _preArrivalCommandExecuted = false;

                        // Start countdown timer
                        StartCountdownTimer();
                    }
                }

                if (isForced)
                {
                    if (candidateEntryTime.HasValue)
                    {
                        ChatGui.Print($"Latest ocean trip found: {candidateEntryLine}");
                        ChatGui.Print($"Entry time: {candidateEntryTime.Value:yyyy-MM-dd HH:mm:ss}, Last processed: {lastProcessedTime:yyyy-MM-dd HH:mm:ss}");

                        bool isNew = IsNewer(candidateEntryTime.Value, lastProcessedTime, currentTime) ||
                                     (candidateEntryTime.Value == lastProcessedTime &&
                                      (storedFile == null || !string.Equals(candidateFile, storedFile, StringComparison.OrdinalIgnoreCase)));

                        ChatGui.Print($"Is new: {isNew}");

                        if (NextBoatTimeUtc.HasValue && NextBoatTimeEst.HasValue)
                        {
                            // Update to show Eastern Time with proper DST handling
                            string timeAbbr = TimeZoneHelper.GetEasternTimeAbbreviation(NextBoatTimeEst.Value);
                            ChatGui.Print($"Next boat arrival: {NextBoatTimeEst:yyyy-MM-dd HH:mm:ss} {timeAbbr} time");

                            TimeSpan? timeUntilBoat = TimeUntilNextBoat;
                            if (timeUntilBoat.HasValue)
                            {
                                ChatGui.Print($"Time until next boat: {timeUntilBoat.Value.ToString(@"hh\:mm\:ss")}");
                            }
                        }

                        ChatGui.Print("Forcing update of LastProcessedTime since this is a manual check.");
                        Configuration.LastProcessedTime = candidateEntryTime.Value;
                        Configuration.LastFoundEntry = candidateEntryLine;
                        Configuration.LastProcessedFileName = candidateFile;
                        SaveConfiguration();
                    }
                    else
                    {
                        ChatGui.Print("No ocean trip entries found in logs.");
                    }
                }
                else if (candidateEntryTime.HasValue)
                {
                    bool isNew = IsNewer(candidateEntryTime.Value, lastProcessedTime, currentTime) ||
                                 (candidateEntryTime.Value == lastProcessedTime &&
                                  (storedFile == null || !string.Equals(candidateFile, storedFile, StringComparison.OrdinalIgnoreCase)));

                    PluginLog.Information($"Candidate time: {candidateEntryTime.Value:yyyy-MM-dd HH:mm:ss.fff}, Last processed: {lastProcessedTime:yyyy-MM-dd HH:mm:ss.fff}, Is new: {isNew}");

                    if (isNew)
                    {
                        ChatGui.Print("New ocean trip detected, command will be executed in 1 minute.");
                        PluginLog.Information($"New ocean trip detected: {candidateEntryLine}");

                        // Critical: Set these flags for command execution
                        _pendingExecution = true;
                        _nextCommandExecutionTime = DateTime.Now.AddMinutes(1);

                        // Update configuration
                        Configuration.LastProcessedTime = candidateEntryTime.Value;
                        Configuration.LastFoundEntry = candidateEntryLine;
                        Configuration.LastProcessedFileName = candidateFile;
                        SaveConfiguration();

                        // Execute the command after delay
                        Task.Run(async () =>
                        {
                            try
                            {
                                PluginLog.Information($"Waiting 1 minute before executing command: {Configuration.ChatCommand}");
                                await Task.Delay(TimeSpan.FromMinutes(1));

                                PluginLog.Information($"Executing command: {Configuration.ChatCommand}");
                                ExecuteChatCommand(Configuration.ChatCommand);
                                ChatGui.Print("Command executed!");
                            }
                            catch (Exception ex)
                            {
                                PluginLog.Error($"Error during command execution: {ex.Message}");
                                ChatGui.PrintError($"Error executing command: {ex.Message}");
                            }
                            finally
                            {
                                _pendingExecution = false;
                                _nextCommandExecutionTime = null;
                                PluginLog.Information("Command execution completed, resuming monitoring");
                            }
                        });
                    }
                    else
                    {
                        PluginLog.Information("No new ocean trip entries found");
                    }
                }

                // Delete old files if enabled
                if (Configuration.DeleteOldFiles && !string.IsNullOrEmpty(Configuration.LastProcessedFileName))
                {
                    string candidateFullPath = Path.GetFullPath(Configuration.LastProcessedFileName);
                    foreach (var file in logFiles)
                    {
                        string fileFullPath = Path.GetFullPath(file);
                        if (!string.Equals(fileFullPath, candidateFullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                File.Delete(file);
                                PluginLog.Information($"Deleted old log file: {file}");
                            }
                            catch (Exception ex)
                            {
                                PluginLog.Error($"Failed to delete file {file}: {ex.Message}");
                            }
                        }
                    }
                }

                _mainWindow.UpdateStatus();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error processing logs: {ex.Message}\n{ex.StackTrace}");
                ChatGui.PrintError($"Error checking logs: {ex.Message}");
            }
        }

        // Helper method to extract minutes from the log line
        private int ExtractNextBoatMinutes(string line)
        {
            try
            {
                // Pattern to match: "Next boat is in XX minutes"
                var minutesMatch = Regex.Match(line, @"Next boat is in (\d+) minutes");
                if (minutesMatch.Success && minutesMatch.Groups.Count > 1)
                {
                    if (int.TryParse(minutesMatch.Groups[1].Value, out int minutes))
                    {
                        return minutes;
                    }
                }
                // Default if no match or failed to parse
                return 60;
            }
            catch
            {
                // Default in case of errors
                return 60;
            }
        }

        // Helper method to determine which day a log entry belongs to
        private DateTime DetermineEntryDate(DateTime fileLastWriteTime, TimeSpan logTimeOfDay, DateTime currentTime)
        {
            DateTime fileDate = fileLastWriteTime.Date;
            DateTime now = currentTime.Date;

            // Early morning hours (after midnight)
            bool isAfterMidnight = logTimeOfDay.Hours < 6; // Consider times before 6 AM as potentially "after midnight"
            bool isCurrentHourAfterMidnight = currentTime.Hour < 6;

            // Late night hours (before midnight)
            bool isLateNight = logTimeOfDay.Hours >= 22; // Consider times after 10 PM as "late night"

            // Case 1: Log timestamp is from early morning (after midnight) and current time is also early morning
            if (isAfterMidnight && isCurrentHourAfterMidnight)
            {
                PluginLog.Information($"Case 1: Entry from early morning today: {now:yyyy-MM-dd}");
                return now; // Today
            }

            // Case 2: Log timestamp is from early morning (after midnight) but current time is not early morning
            // This likely means the entry is from tomorrow morning (after today's midnight)
            if (isAfterMidnight && !isCurrentHourAfterMidnight && fileLastWriteTime.Date == now)
            {
                PluginLog.Information($"Case 2: Entry from tomorrow morning: {now.AddDays(1):yyyy-MM-dd}");
                return now.AddDays(1); // Tomorrow
            }

            // Case 3: Log timestamp is late night and current time is early morning
            // This likely means the entry is from yesterday late night
            if (isLateNight && isCurrentHourAfterMidnight && fileLastWriteTime.Date <= now)
            {
                PluginLog.Information($"Case 3: Entry from yesterday late night: {now.AddDays(-1):yyyy-MM-dd}");
                return now.AddDays(-1); // Yesterday
            }

            // Default case: use the file's date
            PluginLog.Information($"Default case: Using file date: {fileDate:yyyy-MM-dd}");
            return fileDate;
        }

        // Helper method to compare timestamps, specially handling day transitions
        private bool IsNewer(DateTime time1, DateTime time2, DateTime currentTime)
        {
            // Simple case: if dates are different by more than 1 day
            if (Math.Abs((time1.Date - time2.Date).TotalDays) > 1)
            {
                return time1 > time2;
            }

            // Special case for day transition (midnight)
            bool isTime1AfterMidnight = time1.Hour < 6 && time1.Date == currentTime.Date;
            bool isTime2BeforeMidnight = time2.Hour >= 20 && time2.Date == currentTime.Date.AddDays(-1);

            // If time1 is after midnight today and time2 is before midnight yesterday
            if (isTime1AfterMidnight && isTime2BeforeMidnight)
            {
                PluginLog.Information($"Special case: {time1:HH:mm} today is newer than {time2:HH:mm} yesterday");
                return true;
            }

            // Normal comparison
            return time1 > time2;
        }

        private void ExecuteChatCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                PluginLog.Error("Cannot execute empty chat command");
                return;
            }

            PluginLog.Information($"Executing command: {command}");

            if (command.StartsWith("/echo", StringComparison.OrdinalIgnoreCase))
            {
                string message = command.Substring(5).Trim();
                ChatGui.Print(message);
                PluginLog.Information($"Printed echo message: {message}");
            }
            else
            {
                try
                {
                    PluginLog.Information($"Processing game command: {command}");
                    CommandManager.ProcessCommand(command);
                    PluginLog.Information($"Game command executed successfully: {command}");
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Failed to execute chat command: {command}\nError: {ex.Message}");
                    ChatGui.PrintError($"Failed to execute command: {command}");
                }
            }
        }

        public void SaveConfiguration()
        {
            Configuration.LogDirectory = LogDirectory;
            PluginInterface.SavePluginConfig(Configuration);
            PluginLog.Information("Configuration saved successfully.");
        }

        private void DrawUI()
        {
            _windowSystem.Draw();
        }

        private void OpenConfigUi()
        {
            _mainWindow.IsOpen = true;
        }

        private void OpenMainUi()
        {
            _mainWindow.IsOpen = true;
        }
    }
}

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using SamplePlugin.Windows;
using Dalamud.Plugin.Services;
using System.Linq;
using Dalamud.IoC;

namespace SamplePlugin
{
    public class Plugin : IDalamudPlugin
    {
        public string Name => "Ocean Log Monitor";
        public Configuration Configuration { get; private set; }
        private const string CommandName = "/logmonitor";

        // Make these public so they can be accessed/modified from MainWindow
        public string LogDirectory { get; set; }

        // Timer tracking fields
        private DateTime _lastCheckTime = DateTime.Now;
        private DateTime? _nextCommandExecutionTime;
        private bool _pendingExecution;

        // Timer properties that MainWindow uses
        public TimeSpan TimeUntilNextCheck =>
            _lastCheckTime.AddMinutes(Configuration.CheckIntervalMinutes) - DateTime.Now;

        public TimeSpan? TimeUntilCommand =>
            _nextCommandExecutionTime.HasValue ?
            _nextCommandExecutionTime.Value - DateTime.Now :
            null;

        public bool HasPendingExecution => _pendingExecution;

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
                HelpMessage = "Commands: /logmonitor forcecheck, /logmonitor status"
            });

            // Setup window system
            _windowSystem = new WindowSystem(Name);
            _mainWindow = new MainWindow(this);
            _windowSystem.AddWindow(_mainWindow);

            // Register UI events
            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;

            // Start monitoring
            StartMonitoring();
            PluginLog.Information($"{Name} initialized with log directory: {LogDirectory}");
        }

        private void EnsureConfigurationDefaults()
        {
            // Ensure sensible defaults
            Configuration.MonitorEnabled = Configuration.MonitorEnabled; // Keeps existing value or defaults to true
            Configuration.CheckIntervalMinutes = Configuration.CheckIntervalMinutes > 0
                ? Configuration.CheckIntervalMinutes
                : 5; // Default to 5 minutes if not set or invalid
            Configuration.ChatCommand ??= "/echo Ocean trip found!";
        }

        public void Dispose()
        {
            // Cleanup method
            StopMonitoring();
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
                    break;
                default:
                    ChatGui.Print("Available commands: /logmonitor forcecheck, /logmonitor status");
                    break;
            }
        }

        private void StartMonitoring()
        {
            // Stop any existing monitoring to prevent duplicate threads
            StopMonitoring();

            // Create a new cancellation token source
            _cancellationTokenSource = new CancellationTokenSource();

            // Start a new monitoring task
            _monitoringTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // Only check if monitoring is enabled and no command is pending execution
                    if (Configuration.MonitorEnabled && !_pendingExecution)
                    {
                        CheckLogs(false);
                    }

                    try
                    {
                        // Wait for the configured interval before next check
                        await Task.Delay(
                            TimeSpan.FromMinutes(Configuration.CheckIntervalMinutes),
                            _cancellationTokenSource.Token
                        );
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        private void StopMonitoring()
        {
            // Safely stop the monitoring task
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
            // Don't perform check if there's a pending execution, unless it's a forced check
            if (_pendingExecution && !isForced)
            {
                PluginLog.Information("Skipping check due to pending command execution");
                return;
            }

            try
            {
                _lastCheckTime = DateTime.Now;

                // Validate log directory exists
                if (!Directory.Exists(LogDirectory))
                {
                    ChatGui.PrintError($"Log directory not found: {LogDirectory}");
                    throw new DirectoryNotFoundException($"Log directory not found: {LogDirectory}");
                }

                // Get log files sorted by most recent first
                var logFiles = Directory.GetFiles(LogDirectory, "*.txt")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToArray();

                if (logFiles.Length == 0)
                {
                    ChatGui.PrintError($"No log files found in directory: {LogDirectory}");
                    throw new FileNotFoundException($"No log files found in directory: {LogDirectory}");
                }

                // Debug output
                ChatGui.Print($"Checking {logFiles.Length} log files");
                PluginLog.Information($"Checking {logFiles.Length} log files from {LogDirectory}");

                // Set up initial tracking variables
                var lastProcessedTime = Configuration.LastProcessedTime ?? DateTime.MinValue;
                DateTime? latestEntryTime = null;
                bool foundNewEntry = false;
                string latestEntry = null;

                // Regex to match ocean trip log entries - ensuring it's case-insensitive
                var regex = new Regex(@"\[(\d{2}:\d{2}:\d{2}\.\d{3}) N\] \[Ocean Trip\] Next boat is in (\d+) minutes\. Passing the time until then\.",
                    RegexOptions.IgnoreCase);

                // Process log files
                foreach (var file in logFiles)
                {
                    var fileInfo = new FileInfo(file);
                    var fileDate = fileInfo.LastWriteTime.Date;

                    PluginLog.Information($"Examining log file: {file} from {fileDate:yyyy-MM-dd}");

                    try
                    {
                        var fileContent = File.ReadAllText(file);
                        var lines = fileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        PluginLog.Information($"Found {lines.Length} lines in file");

                        // Check each line for matches
                        foreach (var line in lines.Reverse())
                        {
                            var match = regex.Match(line);
                            if (match.Success)
                            {
                                var timeStr = match.Groups[1].Value;
                                PluginLog.Information($"Found matching entry: {line} with time {timeStr}");

                                if (DateTime.TryParse($"{fileDate:yyyy-MM-dd} {timeStr}",
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None,
                                    out DateTime entryTime))
                                {
                                    // Adjust for potential day rollover
                                    if (entryTime.TimeOfDay > fileDate.TimeOfDay.Add(TimeSpan.FromHours(12)))
                                    {
                                        entryTime = entryTime.AddDays(-1);
                                        PluginLog.Information($"Adjusted entry time for day rollover: {entryTime}");
                                    }

                                    // Debug output the time comparison
                                    PluginLog.Information($"Entry time: {entryTime}, Last processed: {lastProcessedTime}, Compare: {entryTime > lastProcessedTime}");

                                    // Track the latest entry time
                                    if (!latestEntryTime.HasValue || entryTime > latestEntryTime.Value)
                                    {
                                        latestEntryTime = entryTime;
                                        latestEntry = line;
                                        PluginLog.Information($"New latest entry found: {latestEntry} at {latestEntryTime}");
                                    }

                                    // Check if this is a new entry since last processed
                                    if (entryTime > lastProcessedTime)
                                    {
                                        foundNewEntry = true;
                                        PluginLog.Information($"New entry detected: {entryTime} > {lastProcessedTime}");
                                    }
                                }
                                else
                                {
                                    PluginLog.Error($"Failed to parse date/time from: {fileDate:yyyy-MM-dd} {timeStr}");
                                }
                            }
                        }

                        // If we found the latest entry time, debug output its value
                        if (latestEntryTime.HasValue)
                        {
                            PluginLog.Information($"Latest entry time: {latestEntryTime.Value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error($"Error reading log file {file}: {ex.Message}");
                        // Continue to the next file even if there's an error with this one
                    }
                }

                // Force check and no log files found
                if (logFiles.Length == 0 && isForced)
                {
                    ChatGui.Print("No log files found to check.");
                    return;
                }

                // Process the new entry if found
                if (foundNewEntry && !_pendingExecution)
                {
                    // Ensure we don't process the same entry again
                    if (latestEntryTime.HasValue && latestEntryTime.Value > lastProcessedTime)
                    {
                        ChatGui.Print("New ocean trip detected, command will be executed in 1 minute.");
                        PluginLog.Information($"New ocean trip detected: {latestEntry}");

                        // Mark as pending and set execution time
                        _pendingExecution = true;
                        _nextCommandExecutionTime = DateTime.Now.AddMinutes(1);

                        // Update configuration with latest processed entry
                        Configuration.LastProcessedTime = latestEntryTime.Value;
                        Configuration.LastFoundEntry = latestEntry;
                        SaveConfiguration();

                        // Execute command after delay
                        Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(TimeSpan.FromMinutes(1));
                                ExecuteChatCommand(Configuration.ChatCommand);
                            }
                            catch (Exception ex)
                            {
                                PluginLog.Error($"Error during command execution: {ex.Message}");
                                ChatGui.PrintError($"Error executing command: {ex.Message}");
                            }
                            finally
                            {
                                // Reset execution state
                                _pendingExecution = false;
                                _nextCommandExecutionTime = null;
                                PluginLog.Information("Command execution completed, resuming monitoring");
                            }
                        });
                    }
                    else
                    {
                        PluginLog.Information($"Found entry time ({latestEntryTime}) not newer than last processed time ({lastProcessedTime})");
                    }
                }
                else if (isForced)
                {
                    // For forced checks, always provide feedback
                    if (latestEntryTime.HasValue)
                    {
                        ChatGui.Print($"Latest ocean trip found: {latestEntry}");
                        ChatGui.Print($"Entry time: {latestEntryTime.Value}, Last processed: {lastProcessedTime}");
                        ChatGui.Print($"Is new: {latestEntryTime.Value > lastProcessedTime}");
                    }
                    else
                    {
                        ChatGui.Print("No ocean trip entries found in logs.");
                    }
                }

                // Update main window status
                _mainWindow.UpdateStatus();
            }
            catch (Exception ex)
            {
                // Log and display any errors
                PluginLog.Error($"Error processing logs: {ex.Message}\n{ex.StackTrace}");
                ChatGui.PrintError($"Error checking logs: {ex.Message}");
            }
        }

        private void ExecuteChatCommand(string command)
        {
            // Validate command
            if (string.IsNullOrEmpty(command))
            {
                PluginLog.Error("Cannot execute empty chat command");
                return;
            }

            // Special handling for echo commands
            if (command.StartsWith("/echo", StringComparison.OrdinalIgnoreCase))
            {
                string message = command.Substring(5).Trim();
                ChatGui.Print(message);
                PluginLog.Information($"Printed debug message: {message}");
            }
            else
            {
                // Execute other chat commands
                try
                {
                    CommandManager.ProcessCommand(command);
                    PluginLog.Information($"Executed chat command: {command}");
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
            // Update and save configuration
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

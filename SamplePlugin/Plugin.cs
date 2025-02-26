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

        // Timer properties for UI
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

            // Setup window system and UI
            _windowSystem = new WindowSystem(Name);
            _mainWindow = new MainWindow(this);
            _windowSystem.AddWindow(_mainWindow);

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
            PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;

            // Start monitoring logs
            StartMonitoring();
            PluginLog.Information($"{Name} initialized with log directory: {LogDirectory}");
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

                // Local candidate variables for the most recent matching entry
                DateTime? candidateEntryTime = null;
                string candidateEntryLine = null;
                string candidateFile = null;
                const string targetPhrase = "Next boat is in";

                // Process every log file
                foreach (var file in logFiles)
                {
                    FileInfo fileInfo = new FileInfo(file);
                    // Use the full LastWriteTime
                    DateTime fileDate = fileInfo.LastWriteTime;
                    PluginLog.Information($"Examining log file: {file} from {fileDate:yyyy-MM-dd}");

                    try
                    {
                        using (StreamReader reader = new StreamReader(file))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (line.Contains("[Ocean Trip]") && line.Contains(targetPhrase))
                                {
                                    int closingBracket = line.IndexOf(']');
                                    if (closingBracket > 0)
                                    {
                                        string timeStr = line.Substring(1, closingBracket - 1).Split(' ')[0];
                                        PluginLog.Information($"Found matching line: {line}");
                                        PluginLog.Information($"Time string extracted: {timeStr}");
                                        string dateTimeStr = $"{fileDate:yyyy-MM-dd} {timeStr}";
                                        if (DateTime.TryParseExact(dateTimeStr, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime entryTime))
                                        {
                                            PluginLog.Information($"Parsed entry time: {entryTime:yyyy-MM-dd HH:mm:ss.fff}");
                                            if (!candidateEntryTime.HasValue || entryTime > candidateEntryTime.Value)
                                            {
                                                candidateEntryTime = entryTime;
                                                candidateEntryLine = line;
                                                candidateFile = file;
                                            }
                                            else if (entryTime == candidateEntryTime.Value &&
                                                     (storedFile == null || !string.Equals(file, storedFile, StringComparison.OrdinalIgnoreCase)))
                                            {
                                                candidateEntryTime = entryTime;
                                                candidateEntryLine = line;
                                                candidateFile = file;
                                            }
                                        }
                                        else
                                        {
                                            PluginLog.Error($"Failed to parse date/time from: {dateTimeStr}");
                                        }
                                    }
                                    else
                                    {
                                        PluginLog.Warning($"Line format unexpected: {line}");
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

                if (isForced)
                {
                    if (candidateEntryTime.HasValue)
                    {
                        ChatGui.Print($"Latest ocean trip found: {candidateEntryLine}");
                        ChatGui.Print($"Entry time: {candidateEntryTime.Value:yyyy-MM-dd HH:mm:ss}, Last processed: {lastProcessedTime:yyyy-MM-dd HH:mm:ss}");
                        bool isNew = candidateEntryTime.Value > lastProcessedTime ||
                                     (candidateEntryTime.Value == lastProcessedTime &&
                                      (storedFile == null || !string.Equals(candidateFile, storedFile, StringComparison.OrdinalIgnoreCase)));
                        ChatGui.Print($"Is new: {isNew}");
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
                else if (candidateEntryTime.HasValue &&
                    (candidateEntryTime.Value > lastProcessedTime ||
                     (candidateEntryTime.Value == lastProcessedTime &&
                      (storedFile == null || !string.Equals(candidateFile, storedFile, StringComparison.OrdinalIgnoreCase)))))
                {
                    ChatGui.Print("New ocean trip detected, command will be executed in 1 minute.");
                    PluginLog.Information($"New ocean trip detected: {candidateEntryLine}");
                    _pendingExecution = true;
                    _nextCommandExecutionTime = DateTime.Now.AddMinutes(1);
                    Configuration.LastProcessedTime = candidateEntryTime.Value;
                    Configuration.LastFoundEntry = candidateEntryLine;
                    Configuration.LastProcessedFileName = candidateFile;
                    SaveConfiguration();
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
                            _pendingExecution = false;
                            _nextCommandExecutionTime = null;
                            PluginLog.Information("Command execution completed, resuming monitoring");
                        }
                    });
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

        private void ExecuteChatCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                PluginLog.Error("Cannot execute empty chat command");
                return;
            }
            if (command.StartsWith("/echo", StringComparison.OrdinalIgnoreCase))
            {
                string message = command.Substring(5).Trim();
                ChatGui.Print(message);
                PluginLog.Information($"Printed debug message: {message}");
            }
            else
            {
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

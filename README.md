# RVAScheduler
Schedule Jobs and Monitor Directories - Coming Soon (< 1 month, currently in testing)

Visual Studio 2022 Project C# .NET Core windows service/Console application that wraps quartz.net and FileSystemWatcher.

  - version 1.0
    - Define quartz jobs using simple json
    - Monitor directory for file and trigger jobs
    - Restart service automatically reloads jobs
    - Loaded job json is checksummed, changes are autoloaded every minute
    - JobConsole job integreated to call batch files (passes params)
    - Individual job log files
    - Main application log file
    
  - next version
    - Azure key vault integration (read)
    - Azure storage integration (read/write)
    - Azure msg queue integration (read/write)

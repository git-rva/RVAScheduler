# RVAScheduler
Schedule Jobs and Monitor Directories (currently in testing)

Visual Studio 2022 Project C# .NET Core windows service/Console application that wraps quartz.net and FileSystemWatcher.

version 1.0 (pick master branch)

  - Features
    - Define quartz jobs using simple json
    - Monitor directory for files and trigger jobs
    - Restart service automatically reloads jobs
    - Loaded job json is checksummed, changes are autoloaded every minute from jobDir
    - JobConsole job integreated to call batch files (passes params)
    - Individual job log files
    - Job Start/Stop notifications -> CSV file
    - Main application log file
  - Testing
    - Laptop (passes)
      - Sleep mode (works: jobs run when os runs, jobs missed when in sleep mode)
      - Scheduled jobs (works: submitConsoleJob,submitFileSystemTriggerConsoleJob,submitDailyJob,submitDaysofWeekJob,submitMonthlyJob,submitWeeklyJob)
      - FileSystemDirMonitor jobs (works: Paste 5 at a time whith all being processed to archive using batch files)
    - Server/Workstation (in-progress)
  - Build: Install
    - ASP.NET and web development workload (Visual Studio setup)
    - Add new Project RVASchedulerService: search for "Worker Service", and select Worker Service template.
    - Manage NuGet Packages... dialog. Search for "Microsoft.Extensions.Hosting.WindowsServices", and install it.
    - see https://docs.microsoft.com/en-us/dotnet/core/extensions/windows-service
    - Visual Studio 2022 personal edition
      - Build solution
      - Publish to directory
        - Run as service on same machine from the publish directory
        - or
        - Install: Create RVAScheduler dir and sub-dirs on target machine
          - RVAScheduler\bin
          - RVAScheduler\config
          - RVAScheduler\log
          - RVAScheduler\data
          - RVAScheduler\archive
          - RVAScheduler\incoming
          - RVAScheduler\job
          - RVAScheduler\batch
          - copy publish dir to RVAScheduler\bin
          - PowerShell: sc.exe create "RVASchedulerService" binpath="RVAScheduler\bin\RVASchedulerService.exe"
          - Start service


next version

  Azure key vault integration (read)
  Azure storage integration (read/write)
  Azure msg queue integration (read/write)

  - FileProcessor Features
    - Execute process using Shell execute 
    - Azure key vault read secret
    - Place file into Azure storage
    - Read file from Azure storge
    - Job Start/Stop notifications -> EventGrid/Azure Queue
    - Listen for Azure events -> Trigger Job

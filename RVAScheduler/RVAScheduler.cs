
/*
RVAScheduler - https://github.com/git-rva/RVAScheduler

Schedule Jobs and Monitor Directories - Coming Soon (< 1 month, currently in testing)

Visual Studio 2022 Project C# .NET Core windows service/Console application that wraps quartz.net and FileSystemWatcher.

version 1.0

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
    - Server/Workstation
      - 
  - Build: Install
    - ASP.NET and web development workload (Visual Studio setup)
    - Add new Project RVASchedulerService: search for "Worker Service", and select Worker Service template.
    - Manage NuGet Packages... dialog. Search for "Microsoft.Extensions.Hosting.WindowsServices", and install it.
    - see https://docs.microsoft.com/en-us/dotnet/core/extensions/windows-service
    - Visual Studio 2022 personal edition
      - Build solution
      - Publish to directory
        - Run as service on same machine from the publish directory
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


   Copyright 2022 Rod VanAmburgh

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using static System.Net.Mime.MediaTypeNames;
using System;
using Quartz.Impl;
using Quartz;
using Quartz.Impl.Matchers;
using static Quartz.Logging.OperationName;
using Quartz.Impl.Calendar;
using static Quartz.MisfireInstruction;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Runtime.Intrinsics.Arm;
using System.Diagnostics;
using System.Text;

namespace com.rodv.job
{
    public class RVAScheduler : IJobListener
    {
        public static String name { get { return "RVAScheduler"; } }
        public static String version { get { return "1.0"; } }
        // defaults
        public static String logDir = @"C:\code\cs\Rod\RVAScheduler\log";
        public static String incomingDir = @"C:\code\cs\Rod\RVAScheduler\incoming";
        public static String dataDir = @"C:\code\cs\Rod\RVAScheduler\data";
        public static String archiveDir = @"C:\code\cs\Rod\RVAScheduler\archive";
        public static String configDir = @"C:\code\cs\Rod\RVAScheduler\config";
        public static String batchDir = @"C:\code\cs\Rod\RVAScheduler\batch";
        public static String binDir = @"C:\code\cs\Rod\RVAScheduler\bin";
        public static String jobDir = @"C:\code\cs\Rod\RVAScheduler\job";
        public static bool logJobActivity = false;

        Logger logger = null;
        protected Logger Logger { get { return this.logger != null ? this.logger : throw new Exception("ERROR: RVAScheduler.Logger is not set"); } }

        DateTime startDt = DateTime.Now;
#pragma warning disable CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate (possibly because of nullability attributes).
        Thread thread = new Thread(Run);
        public Thread Thread { get { return thread; } }
#pragma warning restore CS8622 // Nullability of reference types in type of parameter doesn't match the target delegate (possibly because of nullability attributes).
        public bool Running { get; set; }

        public string Name { get { return name; } }

        IScheduler? Scheduler = null;
        /// <summary>
        /// job.key to TriggerFileSystem
        /// </summary>
        Dictionary<JobKey, TriggerFileSystem> triggerFileSystems = new Dictionary<JobKey, TriggerFileSystem>();
        /// <summary>
        /// jobFiles[filePath] = sha512String
        /// </summary>
        Dictionary<string, string> jobFiles = new Dictionary<string, string>();
        static JsonDocument? config = null;
        StreamWriter jobActivityLogFile = null;
        private readonly object gateKeeperJobActivityLogFile = new object();

        public RVAScheduler()
        {
            string baseDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            //this.logger = new Logger(string.Format(@"{0}\{1}-{2}.log", baseDir, name, startDt.ToString("yyyy-MM-dd_HH_mm_ss_fff")));
            this.logger = new Logger(string.Format(@"{0}\{1}.log", baseDir, name));
            Logger.Log(string.Format("{0} Starting {1}", name, startDt.ToString("yyyy-MM-dd HH:mm:ss.fff")));

            // when run in the IDE - RVAScheduler\RVAScheduler\bin\Debug\net6.0\config\config.json
            if (baseDir.Contains(@"\RVAScheduler\bin\x64\"))
            {
                baseDir = baseDir.Substring(0, baseDir.IndexOf(@"\RVAScheduler\bin\x64\"));
            }
            else if (baseDir.Contains(@"\bin\"))
            {
                baseDir = baseDir.Substring(0, baseDir.IndexOf(@"\bin\"));
            }
            else
            {
                baseDir = Directory.GetParent(baseDir).FullName;             
            }
            Logger.Log(string.Format("baseDir: {0}", baseDir));
            LoadConfig(string.Format(@"{0}\config\config.json", baseDir));
            this.logger.CloseLogFile();
            this.logger = null;

            // Log file based on config value
            this.logger = new Logger(string.Format(@"{0}\{1}.log", logDir, name));
            Logger.Log(string.Format("{0} Starting {1}", name, startDt.ToString("yyyy-MM-dd HH:mm:ss.fff"))); 
            Logger.Log(string.Format("logDir = {0}", logDir));
            Logger.Log(string.Format("incomingDir = {0}", incomingDir));
            Logger.Log(string.Format("dataDir = {0}", dataDir));
            Logger.Log(string.Format("archiveDir = {0}", archiveDir));
            Logger.Log(string.Format("configDir = {0}", configDir));
            Logger.Log(string.Format("batchDir = {0}", batchDir));
            Logger.Log(string.Format("binDir = {0}", binDir));
            Logger.Log(string.Format("jobDir = {0}", jobDir));
            Logger.Log(string.Format("logJobActivity = {0}", logJobActivity));

            StdSchedulerFactory factory = new StdSchedulerFactory();
            Scheduler = (IScheduler)factory.GetScheduler().Result;    
            thread.Name = string.Format("{0}_thread", name);
            this.Running = true;
            thread.Start(this);
        }

        public void Cleanup(string calledByName)
        {
            // Cleanup() could be called multiple times
            lock (name)
            {
                try
                {                   
                    Logger.Log(string.Format("{0} cleanup({1}) {2}", name, calledByName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
                    triggerFileSystems.Clear();
                    if (Scheduler != null)
                    {
                        Scheduler.Shutdown();
                    }
                }
                catch(Exception e)
                {
                    Logger.LogException(e);
                }
                finally
                {
                    Scheduler = null;
                    Running = false;
                }
            }
        }

        CancellationToken cancellationToken = CancellationToken.None;
        public void SetCancellationToken(CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
        }

        static void Run(object data)
        {
            RVAScheduler r = (RVAScheduler)data;
            long totalJobsLoaded = 0l;
            try
            {
                //Logger.Log("Thread has started");
                r.Scheduler.ListenerManager.AddJobListener(r, GroupMatcher<JobKey>.AnyGroup());
                r.Scheduler.Start();

                // loader thread - load jobs from disk
                while (r.Running)
                {                 
                    int numJobsLoaded = r.LoadJobs();
                    totalJobsLoaded += numJobsLoaded;
                    GroupMatcher<JobKey> gMatcher = GroupMatcher<JobKey>.AnyGroup();
                    IReadOnlyCollection<JobKey> jobKeys = r.Scheduler.GetJobKeys(gMatcher).Result;
                    r.Logger.Log(string.Format("Run() {0} numJobsLoaded: {1} totalJobsLoaded: {2} scheduledJobs: {3} triggerFileSystems: {4}",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), numJobsLoaded, totalJobsLoaded, jobKeys.Count, r.triggerFileSystems.Count));
                    try
                    {
                        if (r.cancellationToken.Equals(CancellationToken.None))
                        {
                            Task.Delay(1000 * 60).Wait();
                        }
                        else
                        {                           
                            Task.Delay(1000 * 60).Wait(r.cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        r.Logger.LogException(ex);
                    }

                    if (r.cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                r.Logger.LogException(e);
            }
            finally
            {
                r.Cleanup("RVAScheduler.Run()");
            }
        }

        //
        // IJobListener
        //

        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            LogJobActivity("JOB_START", context, null);
            return Task.CompletedTask;
        }

        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            LogJobActivity("JOB_STOP", context, jobException);
            return Task.CompletedTask;
        }

        public void LogJobActivity(string operation, IJobExecutionContext context, JobExecutionException? jobException)
        {
            lock (gateKeeperJobActivityLogFile)
            {
                try
                {
                    if (logJobActivity && jobActivityLogFile != null)
                    {
                        StringBuilder sb = new StringBuilder(512);
                        sb.Append("{");
                        if (context.MergedJobDataMap != null && context.MergedJobDataMap.Count > 0)
                        {
                            foreach (string key in context.MergedJobDataMap.Keys)
                            {
                                sb.AppendFormat("\"{0}\": \"{1}\", ", key, context.MergedJobDataMap[key]);
                            }
                            sb.Remove(sb.Length - 2, 2);
                        }
                        sb.Append("}");
                        sb.Replace("\"", "\"\"");
                        jobActivityLogFile.WriteLine(string.Format("{0},{1},{2},{3},\"{4}\"",
                                operation,
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                                context.JobDetail.Key.ToString(),
                                //context.JobDetail.GetType().FullName,
                                context.JobDetail.Description,
                                //context.JobDetail.ToString()));
                                //context.MergedJobDataMap));
                                sb.ToString()));
                        jobActivityLogFile.Flush();
                    }
                }
                catch (Exception e)
                {
                    Logger.LogException(e);
                }
            }
        }

        string getConfig(string name)
        {
            try
            {
                if (config != null)
                {
                    string cfg = config.RootElement.GetProperty(name).GetString();
                    Logger.Log(string.Format("loaded config -> {0} = {1}", name, cfg));
                    return cfg;
                }
            }
            catch(Exception e)
            {
                Logger.LogException(e);
            }

            return "";
        }

        void LoadConfig(string filePath)
        {
            try
            {
                if(!File.Exists(filePath))
                {
                    Logger.Log(string.Format("using default config values, config file not found: {0}", filePath));
                    return;
                }

                // Load config data from json file
                config = JsonDocument.Parse(File.ReadAllText(filePath));
                logDir = getConfig("logDir");
                incomingDir = getConfig("incomingDir");
                dataDir = getConfig("dataDir");
                archiveDir = getConfig("archiveDir");
                configDir = getConfig("configDir");
                batchDir = getConfig("batchDir");
                binDir = getConfig("binDir");
                jobDir = getConfig("jobDir");
                logJobActivity = Boolean.Parse(getConfig("logJobActivity"));
                if (logJobActivity)
                {
                    jobActivityLogFile = File.CreateText(String.Format(@"{0}\{1}_{2}.csv",
                        logDir, name, DateTime.Now.ToString("yyyy-MM-dd_HH_mm_ss")));
                    jobActivityLogFile.WriteLine(string.Format("{0},{1},{2},{3},\"{4}\"",
                        "OPERATION",
                        "REC_CREATE",
                        "JOB_KEY",
                        "JOB_DESC",
                        "JOB_DETAIL"));
                    jobActivityLogFile.Flush();
                }
            }
            catch(Exception e)
            {
                Logger.LogException(e);
            }
        }

        // Load jobs from a directory of json files. The job files are checksummed when loaded and any changes to a job file will
        // result in it being reloaded (could take up to 60 seonds)
        int LoadJobs()
        {
            //Logger.Log(string.Format("LoadJobs() {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
            int numJobsLoaded = 0;
            var hashAlg = SHA512.Create();
            foreach (string filePath in Directory.GetFiles(jobDir, "*.json"))
            {
                if(!jobFiles.ContainsKey(filePath))
                {
                    jobFiles[filePath] = null;
                }

                try
                {
                    // calculate a file checksum
                    byte[] sha512 = hashAlg.ComputeHash(File.ReadAllBytes(filePath));
                    string sha512String = "";
                    foreach (byte x in sha512)
                    {
                        sha512String += String.Format("{0:x2}", x);
                    }
                    
                    // does new file checksum match last load checksum
                    if (sha512String.Equals(jobFiles[filePath]))
                    {
                        continue; // skip loading file as there are no changes to it
                    }

                    // Load json file and create job according to submitMethodName
                    JsonDocument jd = JsonDocument.Parse(File.ReadAllText(filePath));
                    bool enabled = jd.RootElement.GetProperty("enabled").GetBoolean();
                    if(!enabled)
                    {
                        // remove job if already loaded
                        JobKey jobKey = new JobKey(
                            jd.RootElement.GetProperty("identityName").GetString(), 
                            jd.RootElement.GetProperty("identityGroup").GetString());
                        if(Scheduler.CheckExists(jobKey).Result)
                        {
                            Scheduler.DeleteJob(jobKey);
                        }
                        continue; // skip loading since the job is not enabled
                    }
                    bool pause = jd.RootElement.GetProperty("pause").GetBoolean();
                    if (pause)
                    {
                        // pause job if already loaded
                        JobKey jobKey = new JobKey(
                            jd.RootElement.GetProperty("identityName").GetString(),
                            jd.RootElement.GetProperty("identityGroup").GetString());
                        if (Scheduler.CheckExists(jobKey).Result)
                        {
                            Scheduler.PauseJob(jobKey);
                        }
                        continue; // skip loading since the job is paused
                    }

                    string submitMethodName = jd.RootElement.GetProperty("SubmitMethodName").GetString();
                    Logger.Log(string.Format("  LoadJobs() {0} {1} LoadMethodName: {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), 
                        filePath, submitMethodName));

                    if (submitMethodName.Equals("submitFileSystemTriggerConsoleJob"))
                    {
                        string dir = jd.RootElement.GetProperty("directory").GetString();
                        if(dir.Equals("RVAScheduler.incomingDir"))
                        {
                            dir = incomingDir;
                        }

                        submitFileSystemTriggerConsoleJob(
                            jd.RootElement.GetProperty("identityName").GetString(),
                            jd.RootElement.GetProperty("identityGroup").GetString(),
                            jd.RootElement.GetProperty("workingDir").GetString(),
                            jd.RootElement.GetProperty("FileName").GetString(),
                            jd.RootElement.GetProperty("ArgumentsString").GetString(),
                            dir,
                            jd.RootElement.GetProperty("mask").GetString());
                    }
                    else if (submitMethodName.Equals("submitDailyJob"))
                    {
                        submitDailyJob(
                            jd.RootElement.GetProperty("identityName").GetString(),
                            jd.RootElement.GetProperty("identityGroup").GetString(),
                            jd.RootElement.GetProperty("workingDir").GetString(),
                            jd.RootElement.GetProperty("FileName").GetString(),
                            jd.RootElement.GetProperty("ArgumentsString").GetString(),
                            jd.RootElement.GetProperty("hour").GetInt32(),
                            jd.RootElement.GetProperty("minute").GetInt32());
                    }
                    else if (submitMethodName.Equals("submitWeeklyJob"))
                    {
                        DayOfWeek dow = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), jd.RootElement.GetProperty("dayOfWeek").GetString());
                        submitWeeklyJob(
                            jd.RootElement.GetProperty("identityName").GetString(),
                            jd.RootElement.GetProperty("identityGroup").GetString(),
                            jd.RootElement.GetProperty("workingDir").GetString(),
                            jd.RootElement.GetProperty("FileName").GetString(),
                            jd.RootElement.GetProperty("ArgumentsString").GetString(),
                            dow,
                            jd.RootElement.GetProperty("hour").GetInt32(),
                            jd.RootElement.GetProperty("minute").GetInt32());
                    }
                    else if (submitMethodName.Equals("submitMonthlyJob"))
                    {
                        submitMonthlyJob(
                            jd.RootElement.GetProperty("identityName").GetString(),
                            jd.RootElement.GetProperty("identityGroup").GetString(),
                            jd.RootElement.GetProperty("workingDir").GetString(),
                            jd.RootElement.GetProperty("FileName").GetString(),
                            jd.RootElement.GetProperty("ArgumentsString").GetString(),
                            jd.RootElement.GetProperty("dayOfMonth").GetInt32(),
                            jd.RootElement.GetProperty("hour").GetInt32(),
                            jd.RootElement.GetProperty("minute").GetInt32());
                    }
                    else if (submitMethodName.Equals("submitDaysofWeekJob"))
                    {
                        // Monday,Wednesday,Saturday
                        string[] sa = jd.RootElement.GetProperty("daysOfWeek").GetString().Split(",");
                        DayOfWeek[] dowa = new DayOfWeek[sa.Length];
                        for (int i = 0; i < sa.Length; i++)
                        {
                            dowa[i] = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), sa[i]);
                        }
                        
                        submitDaysofWeekJob(
                            jd.RootElement.GetProperty("identityName").GetString(),
                            jd.RootElement.GetProperty("identityGroup").GetString(),
                            jd.RootElement.GetProperty("workingDir").GetString(),
                            jd.RootElement.GetProperty("FileName").GetString(),
                            jd.RootElement.GetProperty("ArgumentsString").GetString(),
                            dowa,
                            jd.RootElement.GetProperty("hour").GetInt32(),
                            jd.RootElement.GetProperty("minute").GetInt32());
                    }
                    else if (submitMethodName.Equals("submitConsoleJob"))
                    {
                        string ArgumentsString = jd.RootElement.GetProperty("ArgumentsString").GetString();
                        if (ArgumentsString.Contains("RVAScheduler.archiveDir") && File.Exists(RVAScheduler.archiveDir))
                        {
                            ArgumentsString = ArgumentsString.Replace("RVAScheduler.archiveDir", RVAScheduler.archiveDir);
                        }

                        submitConsoleJob(
                            jd.RootElement.GetProperty("identityName").GetString(),
                            jd.RootElement.GetProperty("identityGroup").GetString(),
                            jd.RootElement.GetProperty("workingDir").GetString(),
                            jd.RootElement.GetProperty("FileName").GetString(),
                            ArgumentsString,
                            jd.RootElement.GetProperty("intervalInSeconds").GetInt32());
                    }
                    else
                    {
                        throw new Exception(String.Format(
                            "ERROR: unknown submitMethodName: {0} (permitted values are submitFileSystemTriggerConsoleJob and submitConsoleJob)", 
                            submitMethodName));
                    }

                    //Logger.Log(LoadMethodName);
                    jobFiles[filePath] = sha512String;
                    numJobsLoaded++;
                }
                catch(Exception e)
                {
                    Logger.LogException(e);
                }
            }

            if (numJobsLoaded > 0)
            {
                Logger.Log(string.Format("LoadJobs() {0} numJobsLoaded: {1} jobFiles.Count: {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), 
                    numJobsLoaded, jobFiles.Count));
            }
            return numJobsLoaded;
        }

        /// <summary>
        /// Sumbit the job and Tigger to quartz
        /// </summary>
        /// <param name="j"></param>
        /// <param name="t"></param>
        public void submitJob(IJobDetail j, ITrigger t)
        {
            if (Scheduler.CheckExists(j.Key).Result)
            {   
                Scheduler.DeleteJob(j.Key);
            }

            Scheduler.ScheduleJob(j, t);
        }

        // this method is used for testing
        void submitDirJob(string identityName, string identityGroup, string pathMask, int intervalInSeconds)
        {
            if(pathMask.Contains(';') || pathMask.Contains('\n'))
            {
                throw new Exception("ERROR: submitDirJob() pathMask contains illegal characters (; or \\n are not permitted)");
            }

            if (intervalInSeconds < 0)
            {
                throw new Exception("ERROR: submitDirJob() intervalInSeconds must be 1 or greater");
            }

            IJobDetail job = JobBuilder.Create<JobConsole>()
                .WithIdentity(identityName, identityGroup)
                .WithDescription(string.Format("{0}", "JobConsole"))
                .UsingJobData("WorkingDirectory", ".")
                .UsingJobData("FileName", "cmd.exe")
                .UsingJobData("Arguments", string.Format(@"/c dir {0}", pathMask))
                .Build();

            // Trigger the job to run now, and then repeat every intervalInSeconds seconds
            // see https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/more-about-triggers.html#common-trigger-attributes
            // see https://quartznet.sourceforge.io/apidoc/3.0/html/
            ITrigger trigger1 = TriggerBuilder.Create()
                .WithIdentity(string.Format("trigger_{0}", identityName), string.Format("trigger_{0}", identityGroup))
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInSeconds(intervalInSeconds)
                    .RepeatForever())
                .Build();

            //HolidayCalendar cal = new HolidayCalendar();
            //cal.AddExcludedDate(someDate);
            //ITrigger trigger2 = TriggerBuilder.Create()
            //    .WithIdentity(string.Format("trigger_{0}", identityName), string.Format("trigger_{0}", identityGroup))
            //    .StartNow()
            //    .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(9, 30)) // execute job daily at 9:30onds(intervalInSeconds)
                //.WithSchedule(CronScheduleBuilder
                //.WeeklyOnDayAndHourAndMinute(DayOfWeek.Wednesday, 10, 42)
                //    .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time")))
                //.ModifiedByCalendar("myHolidays") 
            //    .Build();

            Logger.Log(string.Format("{0} submitDirJob(\"{1}\", {2})", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), pathMask, intervalInSeconds));
            submitJob(job, trigger1);
        }

        /// <summary>
        /// Create a scheduled job repeating every intervalInSeconds using the JobConsole class. When run UseShellExecute = false
        /// </summary>
        /// <param name="identityName"></param>
        /// <param name="identityGroup"></param>
        /// <param name="workingDir"></param>
        /// <param name="FileName"></param>
        /// <param name="ArgumentsString"></param>
        /// <param name="intervalInSeconds"></param>
        public void submitConsoleJob(string identityName, string identityGroup, string workingDir, string FileName, string ArgumentsString, 
            int intervalInSeconds)
        {
            // need to use fully resolved paths!!!
            /*if (!File.Exists(workingDir))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() workingDir not found: {0}", workingDir));
            }

            if (!File.Exists(FileName))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() FileName not found: {0}", FileName));
            }*/

            IJobDetail job = JobBuilder.Create<JobConsole>()
                .WithIdentity(identityName, identityGroup)
                .WithDescription(string.Format("{0} {1} intervalInSeconds: {2}", "submitConsoleJob", "JobConsole", intervalInSeconds))
                .UsingJobData("WorkingDirectory", workingDir)
                .UsingJobData("FileName", FileName)
                .UsingJobData("Arguments", ArgumentsString)
                .Build();

            // Trigger the job to run now, and then repeat every intervalInSeconds seconds
            // see https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/more-about-triggers.html#common-trigger-attributes
            // see https://quartznet.sourceforge.io/apidoc/3.0/html/
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(string.Format("trigger_{0}", identityName), string.Format("trigger_{0}", identityGroup))
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInSeconds(intervalInSeconds)
                    .RepeatForever())
                .Build();

            Logger.Log(string.Format("    {0} submitConsoleJob(\"{1}\", \"{2}\", \"{3}\")", 
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), workingDir, FileName, ArgumentsString));
            Logger.Log(string.Format("    {0}  intervalInSeconds: {1}",
                "WithIntervalInSeconds", intervalInSeconds));
            submitJob(job, trigger);
        }

        public void submitDailyJob(string identityName, string identityGroup, string workingDir, string FileName, string ArgumentsString, 
            int hour, int minute)
        {
            // need to use fully resolved paths!!!
            /*if (!File.Exists(workingDir))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() workingDir not found: {0}", workingDir));
            }

            if (!File.Exists(FileName))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() FileName not found: {0}", FileName));
            }*/

            IJobDetail job = JobBuilder.Create<JobConsole>()
                .WithIdentity(identityName, identityGroup)
                .WithDescription(string.Format("{0} {1} hour: {2} minute: {3}", "submitDailyJob", "JobConsole", hour, minute))
                .UsingJobData("WorkingDirectory", workingDir)
                .UsingJobData("FileName", FileName)
                .UsingJobData("Arguments", ArgumentsString)
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(string.Format("trigger_{0}", identityName), string.Format("trigger_{0}", identityGroup))
//.ForJob("myJob")
                .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(hour, minute)) // execute job daily
                .Build();

            Logger.Log(string.Format("    {0} submitDailyJob(\"{1}\", \"{2}\", \"{3}\")",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), workingDir, FileName, ArgumentsString));
            Logger.Log(string.Format("    {0}  hour: {1}, minute: {2}",
                "DailyAtHourAndMinute", hour, minute));
            submitJob(job, trigger);
        }

        public void submitWeeklyJob(string identityName, string identityGroup, string workingDir, string FileName, string ArgumentsString, 
            DayOfWeek dayOfWeek, int hour, int minute)
        {
            // need to use fully resolved paths!!!
            /*if (!File.Exists(workingDir))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() workingDir not found: {0}", workingDir));
            }

            if (!File.Exists(FileName))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() FileName not found: {0}", FileName));
            }*/

            IJobDetail job = JobBuilder.Create<JobConsole>()
                .WithIdentity(identityName, identityGroup)
                .WithDescription(string.Format("{0} {1} dayOfWeek: {2} hour: {3} minute: {4}",
                    "submitWeeklyJob", "JobConsole", dayOfWeek, hour, minute))
                .UsingJobData("WorkingDirectory", workingDir)
                .UsingJobData("FileName", FileName)
                .UsingJobData("Arguments", ArgumentsString)
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(string.Format("trigger_{0}", identityName), string.Format("trigger_{0}", identityGroup))
                //.ForJob("myJob")
                .WithSchedule(CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(dayOfWeek, hour, minute)) // execute job weekly
                .Build();

            Logger.Log(string.Format("    {0} submitWeeklyJob(\"{1}\", \"{2}\", \"{3}\")",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), workingDir, FileName, ArgumentsString));
            Logger.Log(string.Format("    {0}  hour: {1}, minute: {2}, dayOfWeek: {3}",
                "WeeklyOnDayAndHourAndMinute", hour, minute, dayOfWeek));
            submitJob(job, trigger);
        }

        public void submitMonthlyJob(string identityName, string identityGroup, string workingDir, string FileName, string ArgumentsString, 
            int dayOfMonth, int hour, int minute)
        {
            // need to use fully resolved paths!!!
            /*if (!File.Exists(workingDir))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() workingDir not found: {0}", workingDir));
            }

            if (!File.Exists(FileName))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() FileName not found: {0}", FileName));
            }*/

            IJobDetail job = JobBuilder.Create<JobConsole>()
                .WithIdentity(identityName, identityGroup)
                .WithDescription(string.Format("{0} {1} dayOfMonth: {2} hour: {3} minute: {4}",
                    "submitMonthlyJob", "JobConsole", dayOfMonth, hour, minute))
                .UsingJobData("WorkingDirectory", workingDir)
                .UsingJobData("FileName", FileName)
                .UsingJobData("Arguments", ArgumentsString)
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(string.Format("trigger_{0}", identityName), string.Format("trigger_{0}", identityGroup))
                //.ForJob("myJob")
                .WithSchedule(CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(dayOfMonth, hour, minute)) // execute job weekly
                .Build();

            Logger.Log(string.Format("    {0} submitMonthlyJob(\"{1}\", \"{2}\", \"{3}\")",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), workingDir, FileName, ArgumentsString));
            Logger.Log(string.Format("    {0}  hour: {1}, minute: {2}, dayOfMonth: {3}",
                "MonthlyOnDayAndHourAndMinute", hour, minute, dayOfMonth));
            submitJob(job, trigger);
        }

        public void submitDaysofWeekJob(string identityName, string identityGroup, string workingDir, string FileName, string ArgumentsString,
            DayOfWeek[] daysOfWeek, int hour, int minute)
        {
            // need to use fully resolved paths!!!
            /*if (!File.Exists(workingDir))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() workingDir not found: {0}", workingDir));
            }

            if (!File.Exists(FileName))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() FileName not found: {0}", FileName));
            }*/

            IJobDetail job = JobBuilder.Create<JobConsole>()
                .WithIdentity(identityName, identityGroup)
                .WithDescription(string.Format("{0} {1} daysOfWeek: {2} hour: {3} minute: {4}", 
                    "submitDaysofWeekJob", "JobConsole", string.Join('/', daysOfWeek), hour, minute))
                .UsingJobData("WorkingDirectory", workingDir)
                .UsingJobData("FileName", FileName)
                .UsingJobData("Arguments", ArgumentsString)
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(string.Format("trigger_{0}", identityName), string.Format("trigger_{0}", identityGroup))
                //.ForJob("myJob")
                .WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(hour, minute, daysOfWeek)) // execute job weekly
                .Build();

            Logger.Log(string.Format("    {0} submitDaysofWeekJob(\"{1}\", \"{2}\", \"{3}\")",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), workingDir, FileName, ArgumentsString));
            Logger.Log(string.Format("    {0}  hour: {1}, minute: {2}, daysOfWeek: {3}",
                "AtHourAndMinuteOnGivenDaysOfWeek", hour, minute, string.Join('/', daysOfWeek)));
            submitJob(job, trigger);
        }

        /// <summary>
        /// Create a FileSystemTrigger job repeating every intervalInSeconds using the TriggerFileSystem class. When run UseShellExecute = false
        /// TriggerFileSystem uses a FileSystemWatcher to fire exactly 1 event for each file found. When the event fires and before the Job is run,
        /// inside the provided Callback, filePath and batchPath (if exists) are added to the MergedJobDataMap. 
        /// filePath is the full path of the file just placed into the directory.
        /// batchPath is defined as string.Format(@"{0}\{1}.bat", RVAScheduler.batchDir, Path.GetFileNameWithoutExtension(filePath)) and the filename 
        /// is used as a switch: if the file exists batchPath is added to the MergedJobDataMap.
        /// </summary>
        /// <param name="identityName"></param>
        /// <param name="identityGroup"></param>
        /// <param name="workingDir"></param>
        /// <param name="FileName"></param>
        /// <param name="ArgumentsString"></param>
        /// <param name="directory"></param>
        /// <param name="mask"></param>
        public void submitFileSystemTriggerConsoleJob(string identityName, string identityGroup, string workingDir, string FileName, string ArgumentsString, string directory, string mask)
        {
            // need to use fully resolved paths!!!
            /*if (!File.Exists(workingDir))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() workingDir not found: {0}", workingDir));
            }

            if (!File.Exists(FileName))
            {
                throw new Exception(String.Format("ERROR: submitConsoleJob() FileName not found: {0}", FileName));
            }*/

            IJobDetail job = JobBuilder.Create<JobConsole>()
                .WithIdentity(identityName, identityGroup)
                .WithDescription(string.Format("{0} {1} directory: {2} mask: {3}", 
                    "submitFileSystemTriggerConsoleJob", "JobConsole", directory, mask))
                .UsingJobData("WorkingDirectory", workingDir)
                .UsingJobData("FileName", FileName)
                .UsingJobData("Arguments", ArgumentsString)
                .StoreDurably(true) // required if no Trigger is being provided
                .Build();

            Logger.Log(string.Format("    {0} submitFileSystemTriggerConsoleJob(\"{1}\", \"{2}\", \"{3}\", \"{4}\", \"{5}\")",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), workingDir, FileName, ArgumentsString, directory, mask));

            if (Scheduler.CheckExists(job.Key).Result)
            {
                Scheduler.DeleteJob(job.Key);
            }

            Scheduler.AddJob(job, true); // no Trigger is defined!

            if (triggerFileSystems.ContainsKey(job.Key))
            {
                triggerFileSystems[job.Key].Cleanup();
                triggerFileSystems.Remove(job.Key);
            }
            // The job.Key is used to run the job when the Trigger fires 
            TriggerFileSystem tfs = new TriggerFileSystem(directory, mask, Scheduler, job.Key, this.Logger);        
            triggerFileSystems[job.Key] = tfs;
        }

        public static void Main(string[] args)
        {
            RVAScheduler? rs = null;
            try
            {
                rs = new RVAScheduler();
                long count = 0;
                while(rs.Running)
                {
                    count++;

                    //Logger.Log(string.Format("Main() {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
                    // print list of running jobs
                    //var executingJobs = rs.Scheduler.GetCurrentlyExecutingJobs().Result;
                    //foreach (var job in executingJobs)
                    //{
                    //    Logger.Log(job.JobDetail.ToString());
                    //}

                    if (count == 2)
                    {
                        // print list of scheduled jobs after 30 seconds
                        rs.Logger.Log("Scheduled Jobs:");
                        foreach (JobKey jobKey in rs.Scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()).Result)
                        {
                            rs.Logger.Log(string.Format("  Job -> {0} Triggers -> {1}", 
                                rs.Scheduler.GetJobDetail(jobKey).Result.ToString(), 
                                string.Join(",", rs.Scheduler.GetTriggersOfJob(jobKey).Result) ));
                        }
                    }

                    // submit a job via API
                    if (count == 1)
                    {
                        //rs.submitDirJob(@"C:\code\*.*", 10);
                        //rs.submitConsoleJob(".", "cmd.exe", @"/c dir C:\code\*.*", 15);
                        //rs.submitFileSystemTriggerConsoleJob(".", "cmd.exe", @"/c dir C:\code\*.*", RVAScheduler.incomingDir, @"*.*");
                        //rs.submitFileSystemTriggerConsoleJob(".", "cmd.exe", @"/c dir ~filePath~", RVAScheduler.incomingDir, @"*.*");
                    }
                    rs.thread.Join(1000 * 30);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(string.Format("{0} {1}", e.Message, e.StackTrace));
                System.Diagnostics.Debug.WriteLine(string.Format("{0} {1}", e.Message, e.StackTrace));
            }
            finally
            {
                if (rs != null)
                {
                    try
                    {
                        rs.Cleanup("RVAScheduler.Main()");
                    }
                    catch (Exception ee)
                    {
                        Console.WriteLine(string.Format("{0} {1}", ee.Message, ee.StackTrace));
                        System.Diagnostics.Debug.WriteLine(string.Format("{0} {1}", ee.Message, ee.StackTrace));
                    }
                }
            }
            Console.WriteLine("Exiting");
            System.Diagnostics.Debug.WriteLine("Exiting");
        }

        
    }

}


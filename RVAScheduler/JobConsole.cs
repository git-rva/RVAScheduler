/*
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
using Quartz;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace com.rodv.job
{
    /// <summary>
    /// This is a Console Job that uses the Process class to execute a command line (program and arguments)
    /// </summary>
    internal class JobConsole : Job
    {
        IJobExecutionContext? context = null;
        JobKey? key = null;
        Process? process = null;
        string WorkingDirectory = "."; // default
        String FileName = "cmd.exe"; // default
        string Arguments = @"/c dir *.*"; // default
        string filePath = null; // set by TriggerFileSystem
        string batchPath = null; // set by TriggerFileSystem

        public JobConsole()
        {
            ;
        }

        /// <summary>
        /// When the job has been started by quartz or Triggered Execute() is called (see quartz IJob).
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public override Task Execute(IJobExecutionContext context)
        {
            this.context = context;
            this.key = this.context.JobDetail.Key;
            JobDataMap dataMap = this.context.MergedJobDataMap;
            this.WorkingDirectory = dataMap.GetString("WorkingDirectory");
            this.FileName = dataMap.GetString("FileName");
            this.Arguments = dataMap.GetString("Arguments");

            // if defined, create logfile using the name of the incoming filePath
            if (dataMap.Contains("filePath"))
            {
                string logFilePath = string.Format(@"{0}\{1}-{2}-{3}.log",
                    RVAScheduler.logDir,
                    this.key.Name,
                    this.key.Group,
                    Path.GetFileName(dataMap.GetString("filePath")));
                CreateLogger(logFilePath);
            }
            else
            {
                string logFilePath = string.Format(@"{0}\{1}-{2}.log",
                    RVAScheduler.logDir,
                    this.key.Name, 
                    this.key.Group);
                CreateLogger(logFilePath);
            }

            // be nice and print the dataMap to the log file
            Logger.Log("  dataMap:");
            foreach (string key in dataMap.Keys)
            {
                Logger.Log(string.Format("    {0}: {1}", key, dataMap.GetString(key)));
            }

            // if defined and batchPath exists, save batchPath and replace the ~batchPath~ symbol in the command line (noop if there is no ~batchPath~ symbol)
            if (dataMap.Contains("batchPath"))
            {
                this.batchPath = dataMap.GetString("batchPath");
                if (File.Exists(this.batchPath) && this.Arguments.Contains("~batchPath~"))
                {
                    this.Arguments = this.Arguments.Replace("~batchPath~", string.Format("\"{0}\"", this.batchPath));
                }
            }

            // if defined and filePath exists, save filePath and replace the ~filePath~ symbol in the command line (noop if there is no ~filePath~ symbol)
            // also replace the ~fileName~ symbol in the command line
            if (dataMap.Contains("filePath"))
            {
                this.filePath = dataMap.GetString("filePath");
                if (File.Exists(this.filePath) && this.Arguments.Contains("~filePath~"))
                {
                    this.Arguments = this.Arguments.Replace("~filePath~", string.Format("\"{0}\"", this.filePath));
                }

                if (File.Exists(this.filePath) && this.Arguments.Contains("~fileName~"))
                {
                    this.Arguments = this.Arguments.Replace("~fileName~", string.Format("\"{0}\"", Path.GetFileName(this.filePath)));
                }
            }

            // RVAScheduler.archiveDir exists, replace the RVAScheduler.archiveDir symbol in the command line (noop if there is no RVAScheduler.archiveDir symbol)
            if (Directory.Exists(RVAScheduler.archiveDir) && this.Arguments.Contains("RVAScheduler.archiveDir"))
            {
                this.Arguments = this.Arguments.Replace("RVAScheduler.archiveDir", RVAScheduler.archiveDir);
            }

            // RVAScheduler.dataDir exists, replace the RVAScheduler.dataDir symbol in the command line (noop if there is no RVAScheduler.dataDir symbol)
            if (Directory.Exists(RVAScheduler.dataDir) && this.Arguments.Contains("RVAScheduler.dataDir"))
            {
                this.Arguments = this.Arguments.Replace("RVAScheduler.dataDir", RVAScheduler.dataDir);
            }

            return Task.Run(Run);
        }

        /// <summary>
        /// Use the Process class to run the job, capture stdout and stderror and return the return code
        /// </summary>
        /// <returns></returns>
        public override int Run()
        {
            try
            {
                Logger.Log(string.Format("  JobConsole.Run() {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
                Logger.Log(string.Format("    WorkingDirectory: {0}", this.WorkingDirectory));
                Logger.Log(string.Format("    {0} {1}", this.FileName, this.Arguments));

                process = new Process();
                process.StartInfo.WorkingDirectory = this.WorkingDirectory;
                process.StartInfo.FileName = this.FileName;
                process.StartInfo.Arguments = this.Arguments;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.OutputDataReceived += (sender, data) =>
                {
                    lock (gateKeeper)
                    {
                        Logger.Log(data.Data);
                    }
                        
                };

                process.StartInfo.RedirectStandardError = true;
                process.ErrorDataReceived += (sender, data) =>
                {
                    lock (gateKeeper)
                    {
                        Logger.Log(data.Data);
                    }
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                Logger.Log(String.Format("ExitCode: {0}", process.ExitCode));
                if(process.ExitCode != 0)
                {
                    throw new Exception(String.Format("ERROR: JobConsole failed with ExitCode: {0}", process.ExitCode));
                }
                return process.ExitCode;
            }
            catch (Exception e)
            {
                Logger.LogException(e);
                return 1;
            }
            finally
            {
                Logger.CloseLogFile();
            }
        }
    }
}

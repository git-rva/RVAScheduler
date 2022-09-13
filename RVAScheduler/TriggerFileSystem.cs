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
using Quartz.Impl.Triggers;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace com.rodv.job
{
    internal class TriggerFileSystem
    {
        FileSystemDirMonitor fsm = null;
        IScheduler Scheduler = null;
        string dirOrFileToMonitor = null;
        string mask = null;
        JobKey jobKey = null;
        public CancellationToken CancellationToken = new CancellationToken();

        public TriggerFileSystem(string dirOrFileToMonitor, string mask, IScheduler Scheduler, JobKey jobKey, Logger logger)
        {
            this.logger = logger;

            this.dirOrFileToMonitor = dirOrFileToMonitor;
            this.mask = mask;
            this.Scheduler = Scheduler;
            this.jobKey = jobKey;
            this.fsm = new FileSystemDirMonitor(dirOrFileToMonitor, mask, logger);
            this.fsm.Run = new FileSystemDirMonitor.Callback(this.Run); // assign callback
            this.fsm.EnableRaisingEvents = true;
        }

        public void Cleanup()
        {
            this.fsm.EnableRaisingEvents = false;
            this.fsm.Cleanup();
        }

        private Logger logger = null;
        public void setLogger(Logger logger)
        {
            this.logger = logger;
            this.fsm.setLogger(logger);
        }
        protected Logger Logger { get { return this.logger != null ? this.logger : throw new Exception("ERROR: TriggerFileSystem.Logger is not set"); } }

        /// <summary>
        /// Callback
        /// </summary>
        /// <param name="filePath"></param>
        public void Run(string filePath)
        {
            Logger.Log(string.Format("TriggerFileSystem.Run() {0} {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), filePath));
            IJobDetail jobDetail = Scheduler.GetJobDetail(this.jobKey).Result;
            // add data to MergedJobDataMap
            JobDataMap jobDataMap = new JobDataMap();
            jobDataMap["filePath"] = filePath;
            string batchPath = string.Format(@"{0}\{1}.bat", RVAScheduler.batchDir, Path.GetFileNameWithoutExtension(filePath));
            if (File.Exists(batchPath))
            {
                jobDataMap["batchPath"] = batchPath;
            }
            // assigns jobData to MergedJobDataMap and runs Job now (there is no Trigger in this case)
            Scheduler.TriggerJob(this.jobKey, jobDataMap, CancellationToken);
        }

        public override string ToString()
        {
            return string.Format("TriggerFileSystem -> dirOrFileToMonitor: {0} mask: {1} jobKey: {2}",
                dirOrFileToMonitor, mask, jobKey);
        }

    }
}

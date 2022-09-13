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
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace com.rodv.job
{
    internal class FileSystemDirMonitor
    {
        // see https://docs.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher?view=net-6.0
        FileSystemWatcher fileWatcher = new FileSystemWatcher();
        /// <summary>
        /// track file system events by path key
        /// </summary>
        Dictionary<string, FileSystemEventInfo> filePaths = new Dictionary<string, FileSystemEventInfo>();
        /// <summary>
        /// User defined callback Run(string filePath) { ; }
        /// </summary>
        /// <param name="filePath"></param>
        public delegate void Callback(string filePath);
        public Callback Run { get; set; }

        public bool EnableRaisingEvents { 
            get { return this.fileWatcher.EnableRaisingEvents; } 
            set { this.fileWatcher.EnableRaisingEvents = value; } 
        }

        private Logger logger = null;
        public void setLogger(Logger logger)
        {
            this.logger = logger;
        }
        protected Logger Logger { get { return this.logger != null ? this.logger : throw new Exception("ERROR: FileSystemDirMonitor.Logger is not set"); } }

        public FileSystemDirMonitor(string directory, string mask, Logger logger)
        {
            this.logger = logger;

            this.fileWatcher.Path = directory;
            this.fileWatcher.NotifyFilter = NotifyFilters.LastWrite;
            this.fileWatcher.Filter = mask;
            //this.fileWatcher.Created += FileWatcher_Created;
            //this.fileWatcher.Renamed += FileWatcher_Renamed;
            this.fileWatcher.Changed += FileWatcher_Changed;
            Logger.Log(string.Format("    FileSystemDirMonitor(\"{0}\", \"{1}\")", directory, mask));
        }

        public void Cleanup()
        {
            this.fileWatcher.Changed -= FileWatcher_Changed;
            this.fileWatcher.Dispose();
        }

        private void FileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            // maintain in case only one (or more than 2) events were generated!
            ClearCache();

            if (!filePaths.ContainsKey(e.FullPath))
            {
                //Log(string.Format("CHANGE: {0}", e.FullPath));
                FireTrigger(e);
                filePaths.Add(e.FullPath, new FileSystemEventInfo(e.FullPath));
            }
            else
            {
                filePaths[e.FullPath].IncrementEventCount();
            }
        }

        /*private void FileWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            Logger.Log(string.Format("RENAME: {0}", e.FullPath));
        }

        private void FileWatcher_Created(object sender, FileSystemEventArgs e)
        {
            Logger.Log(string.Format("CREATE: {0}", e.FullPath));
        }*/

        private void FireTrigger(FileSystemEventArgs e)
        {
            try
            {
                //Logger.Log(string.Format("FireTrigger(): {0} {1}", e.FullPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
                if(Run != null)
                {
                    Run(e.FullPath);
                }
            }
            catch(Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        private void ClearCache()
        {
            try
            {
                foreach (String key in filePaths.Keys)
                {
                    //Log(string.Format("FileSystemEventInfo: {0}", filePaths[key].ToString()));
                    if (filePaths[key].IsStale())
                    {
                        //Logger.Log(string.Format("  Remove stale FileSystemEventInfo: {0}", filePaths[key].ToString()));
                        filePaths.Remove(key);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        public static void zMain(string[] args)
        {          
            FileSystemDirMonitor fsm = new FileSystemDirMonitor(RVAScheduler.incomingDir, @"*.*", new Logger());
            fsm.setLogger(new Logger());
            fsm.Logger.Log("Starting (press q to quit) ...");
            fsm.EnableRaisingEvents = true;
            while(fsm.EnableRaisingEvents && Console.ReadKey().Key != ConsoleKey.Q)
            {
                Task.Delay(1000).Wait();
            }
            fsm.Logger.Log("");
            fsm.ClearCache();
            fsm.Logger.Log("Exiting");
        }
    }
}

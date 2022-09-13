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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace com.rodv.job
{
    public class Logger
    {
        private readonly object gateKeeper = new object();
        enum LogModes { LOG_MODE_CONSOLE, LOG_MODE_LOGFILE };
        LogModes logMode = LogModes.LOG_MODE_CONSOLE;
        string logFilePath = null;
        StreamWriter logFile = null;

        public Logger()
        {
            logMode = LogModes.LOG_MODE_CONSOLE;
        }

        public Logger(string logFilePath)
        {
            this.logFilePath = logFilePath;
            logMode = LogModes.LOG_MODE_LOGFILE;
            logFile = new StreamWriter(this.logFilePath);
        }

        public string CloseLogFile()
        {
            lock (gateKeeper)
            {
                if (logFile != null)
                {
                    logFile.Flush();
                    logFile.Close();
                    logFile = null;
                }
            }

            return this.logFilePath;
        }

        public void Log(string text)
        {
            lock (gateKeeper)
            {
                if (logMode == LogModes.LOG_MODE_CONSOLE)
                {
                    Console.WriteLine(text);
                    System.Diagnostics.Debug.WriteLine(text);
                }
                else if(logMode == LogModes.LOG_MODE_LOGFILE && logFile != null)
                {
                    logFile.WriteLine(text);
                    logFile.Flush();
                }
            }
        }

        public void LogException(Exception e)
        {
            Log(string.Format("{0} {1}", e.Message, e.StackTrace));
        }

    }
}

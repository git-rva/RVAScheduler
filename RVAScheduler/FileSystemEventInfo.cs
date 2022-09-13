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
    internal class FileSystemEventInfo
    {
        string Path { get; set; }
        DateTime EventFired { get; set; }
        int EventCount { get; set; }
        int MinutesUntilStale { get; set; }

        public FileSystemEventInfo(string path)
        {
            Path = path;
            EventFired = DateTime.Now;
            EventCount = 1;
            MinutesUntilStale = 1;
        }

        public void IncrementEventCount()
        {
            EventCount++;
        }

        public bool IsStale()
        {
            return EventFired.AddMinutes(MinutesUntilStale) <= DateTime.Now;
        }

        public override string ToString()
        {
            return string.Format("FileSystemEventInfo -> Path: {0} EventFired: {1} EventCount: {2}", 
                Path, EventFired.ToString("yyyy-MM-dd HH:mm:ss.fff"), EventCount);
        }
    }
}

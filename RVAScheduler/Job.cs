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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace com.rodv.job
{
    //
    // Custom job base class
    //
    internal class Job : IJob
    {
        protected readonly object gateKeeper = new object();

        private Logger logger = null;
        public void setLogger(Logger logger)
        {
            this.logger = logger;
        }
        public void CreateLogger(string logFilePath)
        {
            lock (gateKeeper)
            {
                this.logger = new Logger(logFilePath);
            }
        }
        protected Logger Logger { get { return this.logger != null ? this.logger : throw new Exception("ERROR: Job.Logger is not set"); } }

        public Job()
        {
            ;
        }

        public virtual Task Execute(IJobExecutionContext context)
        {
            throw new NotImplementedException("you need to override by adding this method to your class: public Task Execute(IJobExecutionContext context)");
        }

        public virtual int Run()
        {
            throw new NotImplementedException("you need to override by adding this method to your class: public Task Run()");
        }
    }
}

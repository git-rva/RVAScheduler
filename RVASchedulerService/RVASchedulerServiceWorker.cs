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
namespace RVASchedulerService
{
    /// <summary>
    /// To create the .NET Worker Service app as a Windows Service, it's recommended that you publish 
    /// the app as a single file executable. It's less error-prone to have a self-contained executable, 
    /// as there aren't any dependent files lying around the file system. 
    /// 
    /// PowerShell: sc.exe create "RVASchedulerService" binpath="C:\Path\To\RVASchedulerService.exe"
    /// </summary>
    public class RVASchedulerServiceWorker : BackgroundService
    {
        /// <summary>
        /// the logger is implemented by the Windows Event Log - Microsoft.Extensions.Logging.EventLog.EventLogLogger.
        /// Logs are written to, and available for viewing in the Event Viewer.
        /// </summary>
        private readonly ILogger<RVASchedulerServiceWorker> _logger;

        /// <summary>
        /// RVASchedulerServiceWorker is injected along with an ILogger
        /// </summary>
        /// <param name="logger"></param>
        public RVASchedulerServiceWorker(ILogger<RVASchedulerServiceWorker> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// This method is called when the IHostedService starts. The implementation should return a 
        /// task that represents the lifetime of the long running operation(s) being performed.
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int _retCode = -1; // default is error
            com.rodv.job.RVAScheduler? _RVAScheduler = null;
            try
            {
                _RVAScheduler = new com.rodv.job.RVAScheduler();
                _RVAScheduler.SetCancellationToken(stoppingToken);
                while (_RVAScheduler.Running && !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation(String.Format("RVASchedulerServiceWorker running at: {0}", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
                    // number of milliseconds to wait before completing the returned task, or -1 to wait indefinitely
                    // cancellation token to observe while waiting for the task to complete
                    try
                    {
                        await Task.Delay(-1, stoppingToken);
                    }
                    catch (Exception exx)
                    {
                        _logger.LogError(exx, "{Message}", exx.Message);
                    }
                }

                while (_RVAScheduler.Running)
                {
                    try
                    {
                        Task.Delay(500).Wait();
                    }
                    catch (Exception exx)
                    {
                        _logger.LogError(exx, "{Message}", exx.Message);
                    }
                }

                Task.Delay(1000 * 1).Wait();

                _retCode = 0; // no error
            }
            catch (Exception ex)
            {
                _retCode = -1; // error
                _logger.LogError(ex, "{Message}", ex.Message);              
            }
            finally
            {
                try
                {
                    if (_RVAScheduler != null)
                    {
                        _RVAScheduler.Cleanup("RVASchedulerServiceWorker.ExecuteAsync()");
                    }
                }
                catch (Exception exx)
                {
                    _logger.LogError(exx, "{Message}", exx.Message);
                }

                _logger.LogInformation(String.Format("RVASchedulerServiceWorker exiting at: {0} retCode: {1}", 
                    DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), _retCode));

                // Terminates this process and returns an exit code to the operating system.
                // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
                // performs one of two scenarios:
                // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
                // 2. When set to "StopHost": will cleanly stop the host, and log errors.
                //
                // In order for the Windows Service Management system to leverage configured
                // recovery options, we need to terminate the process with a non-zero exit code.
                //Environment.Exit(_retCode);
            }
        }
    }
}
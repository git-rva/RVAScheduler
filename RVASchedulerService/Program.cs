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
using RVASchedulerService;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "RVASchedulerService";
    })
    // Adds services to the container. This can be called multiple times and the results will be additive.
    .ConfigureServices(services =>
    {
        // the logger is implemented by the Windows Event Log - Microsoft.Extensions.Logging.EventLog.EventLogLogger.
        // Logs are written to, and available for viewing in the Event Viewer.
        //     settings loadded from appsettings.json
        LoggerProviderOptions.RegisterProviderOptions<
            EventLogSettings, EventLogLoggerProvider>(services);

        services.AddSingleton<com.rodv.job.RVAScheduler>();
        services.AddHostedService<RVASchedulerServiceWorker>();
    })
    // Adds a delegate for configuring the provided ILoggingBuilder. This may be called multiple times.
    .ConfigureLogging((context, logging) =>
    {
        // See: https://github.com/dotnet/runtime/issues/47303
        logging.AddConfiguration(
            context.Configuration.GetSection("Logging"));
        //logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

await host.RunAsync();

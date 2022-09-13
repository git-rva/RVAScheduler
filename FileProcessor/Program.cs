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
using com.rodv.job;
using System.Diagnostics;

namespace com.rodv.FileProcessor
{
    /// <summary>
    /// This class is the start of the Azure integration code
    /// </summary>
    internal class FileProcessor
    {
        static readonly object gateKeeper = new object();
        static Logger Logger = new Logger();

        public static void Main(string[] args)
        {
            Console.WriteLine("FileProcessor:");
            Console.WriteLine(String.Format("  {0}", string.Join(',', args)));

            if(args.Length > 2 && args[0].Equals("ShellExecute"))
            {
                // ShellExecute
                Run("", string.Format(@"{0}\{1}", args[1], args[2].Replace("\"", "")), "", true);
            }
            else if (args.Length > 2 && args[0].Equals("Execute"))
            {
                // Execute
                Run("", string.Format(@"{0}\{1}", args[1], args[2].Replace("\"", "")), "", false);
            }
        }

        static int Run(string WorkingDirectory, string FileName, string Arguments, bool UseShellExecute)
        {
            try
            {
                Logger.Log(string.Format("  FileProcessor.Run() {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
                Logger.Log(string.Format("    WorkingDirectory: {0}", WorkingDirectory));
                Logger.Log(string.Format("    {0} {1}", FileName, Arguments));

                Process process = new Process();
                process.StartInfo.WorkingDirectory = WorkingDirectory;
                process.StartInfo.FileName = FileName;
                process.StartInfo.Arguments = Arguments;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.UseShellExecute = UseShellExecute;
                if (!UseShellExecute)
                {
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
                }
                process.Start();
                if (!UseShellExecute)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    Logger.Log(String.Format("ExitCode: {0}", process.ExitCode));
                    if (process.ExitCode != 0)
                    {
                        throw new Exception(String.Format("ERROR: JobConsole failed with ExitCode: {0}", process.ExitCode));
                    }
                    return process.ExitCode;
                }

                return 0;
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

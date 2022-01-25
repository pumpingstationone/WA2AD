using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;

namespace WA2AD
{
    //
    // This is a singleton so use it like:
    //
    //      Log.Write(Log.Level.Informational, "Heeeyyyy there!");
    //
    // If you want to write to the event log, include this in your
    // program:
    //
    //       EventLogTraceListener myTraceListener = new EventLogTraceListener("WA2AD");
    //       Trace.Listeners.Add(myTraceListener);
    //
    // Note that you may need to register the application name beforehand, so run this
    // in an admin-level powershell:
    //
    //      New-EventLog -LogName Application -Source "WA2AD"
    //
    public sealed class Log
    {
        public enum Level
        {
            Informational,
            Warning,
            Error
        }

        private static string logFilename = "wa2ad.log";
        private static Object logFileLock = new Object();

        private static readonly Lazy<Log> log = new Lazy<Log>(() => new Log());

        public static Log Instance { get { return log.Value; } }

        private static String GetTimestamp(DateTime value)
        {
            return value.ToString("MM/dd/yyyy HH:mm:ss.ffff");
        }

        public static void Write(Level level, string logEntry)
        {
            Console.ResetColor();

            string ts = GetTimestamp(DateTime.Now);
            string logLine = ts + " - " + logEntry;

            switch (level)
            {
                case Level.Informational:
                    Trace.TraceInformation(logEntry);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(logLine);
                    break;
                case Level.Warning:
                    Trace.TraceWarning(logEntry);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(logLine);
                    break;
                case Level.Error:
                    Trace.TraceError(logEntry);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(logLine);
                    break;
            }

            Console.ResetColor();

            lock (logFileLock)
            {
                // And let's write to a file
                using (StreamWriter sw = File.AppendText(logFilename))
                {
                    sw.WriteLine(logLine);
                }
            }
        }

        private Log()
        {
        }
    }
}

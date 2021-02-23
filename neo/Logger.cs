using System;
using System.IO;

namespace Neo
{
    public static class Logger
    {
        private static readonly string log_dictionary = Path.Combine(AppContext.BaseDirectory, "TestLogs");

        public static void Write(string source, string message)
        {
            DateTime now = DateTime.Now;
            string line = $"[{now.TimeOfDay:hh\\:mm\\:ss\\.fff}]<{source}> {message}";
            Console.WriteLine(line);
            if (string.IsNullOrEmpty(log_dictionary)) return;
            lock (log_dictionary)
            {
                Directory.CreateDirectory(log_dictionary);
                string path = Path.Combine(log_dictionary, $"{now:yyyy-MM-dd}.log");
                File.AppendAllLines(path, new[] { line });
            }
        }
    }
}

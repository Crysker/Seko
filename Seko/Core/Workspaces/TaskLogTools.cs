using System;
using System.IO;
using System.Threading.Tasks;

namespace Seko.Core.Workspaces
{
    public static class TaskLogTools
    {
        public static async Task<string> ReadTaskLogAsync()
        {
            string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seko", "Logs", "Tasks");
            string[] logFiles = Directory.GetFiles(logDirectory, "*.md");

            if (logFiles.Length == 0)
            {
                throw new FileNotFoundException("No task log files found in the specified directory.");
            }

            string newestLog = logFiles[0];
            foreach (string logFile in logFiles)
            {
                FileInfo fileInfo = new FileInfo(logFile);
                if (fileInfo.LastWriteTime > FileInfo.GetLastWriteTime(newestLog))
                {
                    newestLog = logFile;
                }
            }

            return await File.ReadAllTextAsync(newestLog);
        }
    }
}

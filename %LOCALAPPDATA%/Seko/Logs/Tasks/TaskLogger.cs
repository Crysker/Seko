---
# Seko Task Logger

## Task Logger Implementation

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Seko.Logging
{
    public static class TaskLogger
    {
        private static int _taskId = 0;
        private static readonly string _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seko", "Logs", "Tasks");
        private static readonly string _logFilePath = Path.Combine(_logPath, "tasks.log.md");
        
        public static void StartTask(string workspaceName, string modelName, string userRequest)
        {
            _taskId++;
            var startTime = DateTime.Now;
            
            // Create directory if it doesn't exist
            Directory.CreateDirectory(_logPath);
            
            // Write task start
            WriteLogEntry(_taskId, startTime, "Running", workspaceName, modelName, userRequest, "Running");
        }
        
        public static void EndTask(string status, string response)
        {
            var endTime = DateTime.Now;
            var duration = endTime - startTime;
            
            // Write task end
            WriteLogEntry(_taskId, startTime, endTime, duration, workspaceName, modelName, userRequest, status, response);
        }
        
        private static void WriteLogEntry(int taskId, DateTime startTime, string status, string workspaceName, string modelName, string userRequest, string finalStatus)
        {
            var endTime = DateTime.Now;
            var duration = endTime - startTime;
            
            var logEntry = $"| {taskId} | {startTime:yyyy-MM-dd HH:mm:ss} | {endTime:yyyy-MM-dd HH:mm:ss} | {duration:mm\:ss\.fff} | {workspaceName} | {modelName} | {userRequest} | {finalStatus} | {response} |";
            
            File.AppendAllText(_logFilePath, logEntry + "\n");
        }
        
        private static void WriteLogEntry(int taskId, DateTime startTime, DateTime endTime, TimeSpan duration, string workspaceName, string modelName, string userRequest, string status, string response)
        {
            var logEntry = $"| {taskId} | {startTime:yyyy-MM-dd HH:mm:ss} | {endTime:yyyy-MM-dd HH:mm:ss} | {duration:mm\:ss\.fff} | {workspaceName} | {modelName} | {userRequest} | {status} | {response} |";
            
            File.AppendAllText(_logFilePath, logEntry + "\n");
        }
    }
}
```
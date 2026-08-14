// Task focus logic to ensure the latest user request is always the current task.
// Previous conversation is used as context, but older requests are not treated as unfinished work.
// During tool tasks, the original current request is fixed and unrelated work is not switched to.

using System;

namespace Seko
{
    public static class TaskFocus
    {
        private static string _currentTask;
        private static bool _isToolTaskActive;

        public static void SetCurrentTask(string task)
        {
            _currentTask = task;
            _isToolTaskActive = true;
        }

        public static string GetCurrentTask()
        {
            return _currentTask;
        }

        public static bool IsToolTaskActive()
        {
            return _isToolTaskActive;
        }

        public static void EndToolTask()
        {
            _isToolTaskActive = false;
        }
    }
}
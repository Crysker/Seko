using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Seko.Core
{
    public static class SearchWorkspace
    {
        public static List<SearchResult> Search(string query, int maxResults = 20)
        {
            var results = new List<SearchResult>();
            var ignoredDirectories = new HashSet<string> { ".git", "bin", "obj", ".vs", "node_modules" };
            var blockedFiles = new HashSet<string> { "*.sln", "*.csproj" };

            // Search files
            var files = Directory.GetFiles(".", "*.*", SearchOption.AllDirectories)
                .Where(f => !ignoredDirectories.Any(d => f.Contains(d)) && !blockedFiles.Any(b => f.EndsWith(b)));

            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                var lineNumber = 0;
                var found = false;

                for (int i = 0; i < lines.Length && results.Count < maxResults; i++)
                {
                    lineNumber++;
                    if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new SearchResult
                        {
                            FilePath = file,
                            LineNumber = lineNumber,
                            Context = GetContext(lines, i, 3)
                        });
                        found = true;
                    }
                }

                if (found && results.Count < maxResults)
                {
                    results.Add(new SearchResult
                    {
                        FilePath = file,
                        LineNumber = -1,
                        Context = Path.GetFileName(file)
                    });
                }
            }

            return results;
        }

        private static string GetContext(string[] lines, int index, int contextLines)
        {
            int start = Math.Max(0, index - contextLines);
            int end = Math.Min(lines.Length, index + contextLines + 1);
            return string.Join("\n", lines.Skip(start).Take(end - start));
        }
    }

    public class SearchResult
    {
        public string FilePath { get; set; }
        public int LineNumber { get; set; }
        public string Context { get; set; }
    }
}
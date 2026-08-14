using System.Text.Json;
using Seko.Core.Workspaces;

namespace Seko.Infrastructure.Workspaces;

public sealed class JsonWorkspaceStore : IWorkspaceStore
{
    private readonly string _filePath;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonWorkspaceStore()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        var configDirectory = Path.Combine(
            localAppData,
            "Seko",
            "Config");

        Directory.CreateDirectory(configDirectory);

        _filePath = Path.Combine(
            configDirectory,
            "workspaces.json");
    }

    public WorkspaceState Load()
    {
        if (!File.Exists(_filePath))
        {
            return new WorkspaceState();
        }

        try
        {
            var json = File.ReadAllText(_filePath);

            var state = JsonSerializer.Deserialize<WorkspaceState>(
                json,
                _jsonOptions);

            return state ?? new WorkspaceState();
        }
        catch
        {
            var backupPath =
                _filePath +
                ".bak";

            if (!File.Exists(
                    backupPath))
            {
                return new WorkspaceState();
            }

            try
            {
                var backupJson =
                    File.ReadAllText(
                        backupPath);

                return
                    JsonSerializer.Deserialize<WorkspaceState>(
                        backupJson,
                        _jsonOptions)
                    ?? new WorkspaceState();
            }
            catch
            {
                return new WorkspaceState();
            }
        }
    }

    public void Save(WorkspaceState state)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            state,
            _jsonOptions);

        var temporaryPath =
            _filePath +
            ".tmp";

        var backupPath =
            _filePath +
            ".bak";

        File.WriteAllText(
            temporaryPath,
            json);

        try
        {
            if (File.Exists(
                    _filePath))
            {
                File.Replace(
                    temporaryPath,
                    _filePath,
                    backupPath,
                    true);
            }
            else
            {
                File.Move(
                    temporaryPath,
                    _filePath);
            }
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
    }
}
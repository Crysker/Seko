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
            return new WorkspaceState();
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

        File.WriteAllText(
            _filePath,
            json);
    }
}
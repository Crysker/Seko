using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Seko.Core.Agent;
using Seko.Core.Chat;
using Seko.Core.Workspaces;
using Seko.Desktop.Services;
using Seko.Infrastructure.Agent;
using Seko.Infrastructure.Workspaces;

namespace Seko.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ChatMessage> _conversation =
        new();

    private readonly ObservableCollection<Workspace> _workspaces =
        new();

    private readonly IWorkspaceStore _workspaceStore;

    private IAgent _agent;
    private Workspace _activeWorkspace;

    private bool _isSending;
    private bool _restartScheduled;

    public MainWindow()
    {
        InitializeComponent();

        ApplySekoWindowIcon();

        _workspaceStore =
            new JsonWorkspaceStore();

        var state =
            LoadWorkspaceState();

        foreach (var workspace in state.Workspaces)
        {
            _workspaces.Add(
                workspace);
        }

        _activeWorkspace =
            _workspaces.FirstOrDefault(
                workspace =>
                    workspace.Id == state.ActiveWorkspaceId)
            ?? _workspaces.First();

        WorkspaceList.ItemsSource =
            _workspaces;

        ConversationList.ItemsSource =
            _conversation;

        _agent =
            CreateAgentForWorkspace(
                _activeWorkspace);

        AddConversationMessage(
            new ChatMessage
            {
                Role =
                    MessageRole.Assistant,

                Content =
                    "I'm online locally.\n\n" +
                    $"Active workspace: {_activeWorkspace.Name}\n" +
                    "Model: qwen3:8b via Ollama\n\n" +
                    "What are we working on?"
            });

        UpdateWorkspaceUi();

        Loaded += (_, _) =>
        {
            MessageInput.Focus();

            ScrollConversationToBottom();
        };

        Closing += (_, _) =>
        {
            SaveWorkspaceState();
        };
    }

    private async void SendButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void MessageInput_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(
                ModifierKeys.Shift))
        {
            return;
        }

        e.Handled =
            true;

        await SendCurrentMessageAsync();
    }

    private void NewWorkspaceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new OpenFolderDialog
            {
                Title =
                    "Choose a folder for the new Seko workspace",

                Multiselect =
                    false
            };

        var result =
            dialog.ShowDialog(
                this);

        if (result != true)
        {
            return;
        }

        var selectedPath =
            Path.GetFullPath(
                dialog.FolderName);

        var existingWorkspace =
            _workspaces.FirstOrDefault(
                workspace =>
                    string.Equals(
                        Path.GetFullPath(
                            workspace.RootPath),

                        selectedPath,

                        StringComparison.OrdinalIgnoreCase));

        if (existingWorkspace is not null)
        {
            ActivateWorkspace(
                existingWorkspace);

            return;
        }

        var directoryInfo =
            new DirectoryInfo(
                selectedPath);

        var workspaceName =
            directoryInfo.Name;

        if (string.IsNullOrWhiteSpace(
                workspaceName))
        {
            workspaceName =
                selectedPath;
        }

        var workspace =
            new Workspace
            {
                Id =
                    Guid.NewGuid(),

                Name =
                    workspaceName,

                RootPath =
                    selectedPath
            };

        _workspaces.Add(
            workspace);

        ActivateWorkspace(
            workspace);

        SaveWorkspaceState();
    }

    private void WorkspaceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender
            is not Button button)
        {
            return;
        }

        if (button.Tag
            is not Workspace workspace)
        {
            return;
        }

        ActivateWorkspace(
            workspace);
    }

    private void ActivateWorkspace(
        Workspace workspace)
    {
        _activeWorkspace =
            workspace;

        _agent =
            CreateAgentForWorkspace(
                workspace);

        UpdateWorkspaceUi();

        SaveWorkspaceState();

        _conversation.Clear();

        AddConversationMessage(
            new ChatMessage
            {
                Role =
                    MessageRole.Assistant,

                Content =
                    $"Switched to {workspace.Name}.\n\n" +
                    "What are we working on?"
            });

        MessageInput.Focus();
    }

    private static IAgent CreateAgentForWorkspace(
        Workspace workspace)
    {
        return
            new OllamaAgent(
                workspace);
    }

    private WorkspaceState LoadWorkspaceState()
    {
        var state =
            _workspaceStore.Load();

        var validWorkspaces =
            state.Workspaces
                .Where(
                    workspace =>
                        !string.IsNullOrWhiteSpace(
                            workspace.RootPath))
                .Where(
                    workspace =>
                        Directory.Exists(
                            workspace.RootPath))
                .ToList();

        if (validWorkspaces.Count == 0)
        {
            var generalWorkspace =
                CreateDefaultWorkspace();

            validWorkspaces.Add(
                generalWorkspace);

            state.ActiveWorkspaceId =
                generalWorkspace.Id;
        }

        return
            new WorkspaceState
            {
                Workspaces =
                    validWorkspaces,

                ActiveWorkspaceId =
                    state.ActiveWorkspaceId
            };
    }

    private void SaveWorkspaceState()
    {
        var state =
            new WorkspaceState
            {
                Workspaces =
                    _workspaces.ToList(),

                ActiveWorkspaceId =
                    _activeWorkspace.Id
            };

        _workspaceStore.Save(
            state);
    }

    private void UpdateWorkspaceUi()
    {
        WorkspaceTitle.Text =
            _activeWorkspace.Name;

        WorkspacePathText.Text =
            _activeWorkspace.RootPath;
    }

    private async Task SendCurrentMessageAsync()
    {
        if (_isSending
            || _restartScheduled)
        {
            return;
        }

        var text =
            MessageInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                text))
        {
            return;
        }

        _isSending =
            true;

        MessageInput.IsEnabled =
            false;

        try
        {
            var userMessage =
                new ChatMessage
                {
                    Role =
                        MessageRole.User,

                    Content =
                        text
                };

            AddConversationMessage(
                userMessage);

            MessageInput.Clear();

            var response =
                await _agent.SendAsync(
                    _conversation.ToList());

            AddConversationMessage(
                response);

            if (_agent
                is IRestartAwareAgent restartAwareAgent
                && restartAwareAgent.RestartRequested)
            {
                await RestartAfterSelfUpdateAsync();
            }
        }
        catch (Exception exception)
        {
            AddConversationMessage(
                new ChatMessage
                {
                    Role =
                        MessageRole.Assistant,

                    Content =
                        "Something went wrong:\n\n" +
                        exception.Message
                });
        }
        finally
        {
            _isSending =
                false;

            if (!_restartScheduled)
            {
                MessageInput.IsEnabled =
                    true;

                MessageInput.Focus();
            }

            ScrollConversationToBottom();
        }
    }

    private async Task RestartAfterSelfUpdateAsync()
    {
        if (_restartScheduled)
        {
            return;
        }

        _restartScheduled =
            true;

        MessageInput.IsEnabled =
            false;

        var scheduled =
            SekoRestartService.TryScheduleRestart(
                _activeWorkspace.RootPath,
                Environment.ProcessId,
                out var error);

        if (!scheduled)
        {
            _restartScheduled =
                false;

            MessageInput.IsEnabled =
                true;

            AddConversationMessage(
                new ChatMessage
                {
                    Role =
                        MessageRole.Assistant,

                    Content =
                        "The update is committed, but I couldn't schedule my restart.\n\n" +
                        error
                });

            return;
        }

        AddConversationMessage(
            new ChatMessage
            {
                Role =
                    MessageRole.Assistant,

                Content =
                    "Update complete. Restarting into the new build..."
            });

        await Task.Delay(
            1200);

        Application.Current.Shutdown();
    }

    private void AddConversationMessage(
        ChatMessage message)
    {
        _conversation.Add(
            message);

        ScrollConversationToBottom();
    }

    private void ScrollConversationToBottom()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(
                () =>
                {
                    ConversationScrollViewer.ScrollToEnd();
                }));
    }

    private void ApplySekoWindowIcon()
    {
        try
        {
            if (Application.Current.Resources[
                    "SekoAppIcon"]
                is ImageSource icon)
            {
                Icon =
                    icon;
            }
        }
        catch
        {
            // The icon is cosmetic.
            // A missing resource should never prevent Seko from starting.
        }
    }

    private static Workspace CreateDefaultWorkspace()
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        var rootPath =
            Path.Combine(
                localAppData,
                "Seko",
                "Workspaces",
                "General");

        Directory.CreateDirectory(
            rootPath);

        return
            new Workspace
            {
                Id =
                    Guid.NewGuid(),

                Name =
                    "General",

                RootPath =
                    rootPath
            };
    }
}
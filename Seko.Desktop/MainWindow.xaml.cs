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
using Seko.Infrastructure.Diagnostics;
using Seko.Infrastructure.Workspaces;

namespace Seko.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ChatMessage> _conversation =
        new();

    private readonly ObservableCollection<Workspace> _workspaces =
        new();

    private readonly ObservableCollection<string> _activityEntries =
        new();

    private readonly ObservableCollection<SekoTaskLogSummary> _activityHistoryEntries =
        new();

    private readonly SekoTaskLogArchive _taskLogArchive =
        new();

    private readonly IWorkspaceStore _workspaceStore;

    private IAgent _agent = null!;
    private Workspace _activeWorkspace;

    private CancellationTokenSource? _requestCancellationSource;

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
                    workspace.Id
                    == state.ActiveWorkspaceId)
            ?? _workspaces.First();

        WorkspaceList.ItemsSource =
            _workspaces;

        ConversationList.ItemsSource =
            _conversation;

        AgentActivityList.ItemsSource =
            _activityEntries;

        ActivityHistoryList.ItemsSource =
            _activityHistoryEntries;

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

        SetAgentStateReady();

        Loaded += (_, _) =>
        {
            MessageInput.Focus();

            ScrollConversationToBottom();
        };

        Closing += (_, _) =>
        {
            _requestCancellationSource?.Cancel();

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

    private void StopAgentButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_restartScheduled)
        {
            return;
        }

        if (_requestCancellationSource is null
            || _requestCancellationSource.IsCancellationRequested)
        {
            return;
        }

        StopAgentButton.IsEnabled =
            false;

        StopAgentButton.Content =
            "Stopping…";

        ActivityHeaderText.Text =
            "Stopping";

        AgentStateText.Text =
            "Stopping";

        AddActivityLine(
            "Stopping current task…");

        _requestCancellationSource.Cancel();
    }

    private void NewWorkspaceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSending
            || _restartScheduled)
        {
            AddActivityLine(
                "Stop the current task before switching workspaces.");

            return;
        }

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
        if (_isSending
            || _restartScheduled)
        {
            AddActivityLine(
                "Stop the current task before switching workspaces.");

            return;
        }

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

    private void ActivitySidebarButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowActivityHistory();
    }

    private void RefreshActivityHistoryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshActivityHistory();
    }

    private void BackToChatButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowChatView();
    }

    private void ActivityHistoryList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        ShowSelectedActivityLog();
    }

    private void ShowActivityHistory()
    {
        ChatView.Visibility =
            Visibility.Collapsed;

        ActivityHistoryView.Visibility =
            Visibility.Visible;

        ActivitySidebarButton.Background =
            FindResource(
                "SurfaceHoverBrush")
            as Brush;

        RefreshActivityHistory();
    }

    private void ShowChatView()
    {
        ActivityHistoryView.Visibility =
            Visibility.Collapsed;

        ChatView.Visibility =
            Visibility.Visible;

        ActivitySidebarButton.Background =
            Brushes.Transparent;

        MessageInput.Focus();
    }

    private void RefreshActivityHistory()
    {
        var selectedPath =
            (ActivityHistoryList.SelectedItem
                as SekoTaskLogSummary)
            ?.FilePath;

        var summaries =
            _taskLogArchive.LoadRecent(
                100);

        _activityHistoryEntries.Clear();

        foreach (var summary
                 in summaries)
        {
            _activityHistoryEntries.Add(
                summary);
        }

        ActivityHistorySubtitle.Text =
            summaries.Count == 0
                ? "No local task logs yet"
                : summaries.Count == 1
                    ? "1 recent local task"
                    : $"{summaries.Count} recent local tasks";

        var selection =
            summaries.FirstOrDefault(
                summary =>
                    string.Equals(
                        summary.FilePath,
                        selectedPath,
                        StringComparison.OrdinalIgnoreCase))
            ?? summaries.FirstOrDefault();

        ActivityHistoryList.SelectedItem =
            selection;

        if (selection is null)
        {
            ActivityHistoryDetails.MarkdownText =
                string.Empty;

            ActivityHistoryScrollViewer.Visibility =
                Visibility.Collapsed;

            ActivityHistoryEmptyText.Visibility =
                Visibility.Visible;
        }
    }

    private void ShowSelectedActivityLog()
    {
        if (ActivityHistoryList.SelectedItem
            is not SekoTaskLogSummary summary)
        {
            ActivityHistoryDetails.MarkdownText =
                string.Empty;

            ActivityHistoryScrollViewer.Visibility =
                Visibility.Collapsed;

            ActivityHistoryEmptyText.Visibility =
                Visibility.Visible;

            return;
        }

        if (!_taskLogArchive.TryReadLog(
                summary,
                out var content))
        {
            content =
                "# Activity log unavailable\n\n"
                + "Seko could not read this local diagnostic log.";
        }

        ActivityHistoryDetails.MarkdownText =
            content;

        ActivityHistoryEmptyText.Visibility =
            Visibility.Collapsed;

        ActivityHistoryScrollViewer.Visibility =
            Visibility.Visible;

        ActivityHistoryScrollViewer.ScrollToTop();
    }

    private void ActivateWorkspace(
        Workspace workspace)
    {
        DetachAgentEvents(
            _agent);

        _activeWorkspace =
            workspace;

        _agent =
            CreateAgentForWorkspace(
                workspace);

        UpdateWorkspaceUi();

        SaveWorkspaceState();

        ShowChatView();

        _conversation.Clear();

        _activityEntries.Clear();

        ActivityPanel.Visibility =
            Visibility.Collapsed;

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

    private IAgent CreateAgentForWorkspace(
        Workspace workspace)
    {
        var agent =
            new SekoSelfUpdatingAgent(
                workspace);

        if (agent
            is IAgentActivitySource activitySource)
        {
            activitySource.ActivityChanged +=
                Agent_ActivityChanged;
        }

        return agent;
    }

    private void DetachAgentEvents(
        IAgent? agent)
    {
        if (agent
            is IAgentActivitySource activitySource)
        {
            activitySource.ActivityChanged -=
                Agent_ActivityChanged;
        }
    }

    private void Agent_ActivityChanged(
        AgentActivity activity)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(
                () =>
                {
                    ActivityPanel.Visibility =
                        Visibility.Visible;

                    AddActivityLine(
                        activity.Message);

                    switch (activity.Kind)
                    {
                        case AgentActivityKind.Completed:
                            ActivityHeaderText.Text =
                                "Task complete";
                            break;

                        case AgentActivityKind.Error:
                            ActivityHeaderText.Text =
                                "Needs attention";
                            break;

                        default:
                            ActivityHeaderText.Text =
                                "Working";
                            break;
                    }
                }));
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

        var cancellationSource =
            new CancellationTokenSource();

        _requestCancellationSource =
            cancellationSource;

        BeginAgentRun();

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

            var activeAgent =
                _agent;

            var response =
                await activeAgent.SendAsync(
                    _conversation.ToList(),
                    cancellationSource.Token);

            AddConversationMessage(
                response);

            if (activeAgent
                is IRestartAwareAgent restartAwareAgent
                && restartAwareAgent.RestartRequested)
            {
                await RestartAfterSelfUpdateAsync();
            }
        }
        catch (OperationCanceledException)
        {
            AddActivityLine(
                "Task stopped by you.");

            ActivityHeaderText.Text =
                "Stopped";

            AddConversationMessage(
                new ChatMessage
                {
                    Role =
                        MessageRole.Assistant,

                    Content =
                        "Stopped."
                });
        }
        catch (Exception exception)
        {
            AddActivityLine(
                "Task failed.");

            ActivityHeaderText.Text =
                "Needs attention";

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
            if (ReferenceEquals(
                    _requestCancellationSource,
                    cancellationSource))
            {
                _requestCancellationSource =
                    null;
            }

            cancellationSource.Dispose();

            _isSending =
                false;

            if (!_restartScheduled)
            {
                MessageInput.IsEnabled =
                    true;

                StopAgentButton.IsEnabled =
                    true;

                StopAgentButton.Content =
                    "Stop";

                StopAgentButton.Visibility =
                    Visibility.Collapsed;

                SetAgentStateReady();

                MessageInput.Focus();
            }

            ScrollConversationToBottom();

            if (ActivityHistoryView.Visibility
                == Visibility.Visible)
            {
                RefreshActivityHistory();
            }
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

        StopAgentButton.IsEnabled =
            false;

        StopAgentButton.Visibility =
            Visibility.Collapsed;

        ActivityPanel.Visibility =
            Visibility.Visible;

        ActivityHeaderText.Text =
            "Restarting";

        AgentStateText.Text =
            "Restarting";

        AgentStateDot.Fill =
            FindResource(
                "AccentStrongBrush")
            as Brush;

        AddActivityLine(
            "Preparing restart helper…");

        var scheduled =
            SekoRestartService.TryScheduleRestart(
                _activeWorkspace.RootPath,
                Environment.ProcessId,
                out var error);

        if (!scheduled)
        {
            _restartScheduled =
                false;

            AddActivityLine(
                "Restart could not be scheduled.");

            ActivityHeaderText.Text =
                "Needs attention";

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

        AddActivityLine(
            "Restart helper ready.");

        AddActivityLine(
            "Closing current Seko…");

        AddConversationMessage(
            new ChatMessage
            {
                Role =
                    MessageRole.Assistant,

                Content =
                    "Update complete. Restarting into the new build…"
            });

        ScrollConversationToBottom();

        await Task.Delay(
            1500);

        Application.Current.Shutdown();
    }

    private void BeginAgentRun()
    {
        _activityEntries.Clear();

        ActivityPanel.Visibility =
            Visibility.Visible;

        ActivityHeaderText.Text =
            "Working";

        StopAgentButton.Content =
            "Stop";

        StopAgentButton.IsEnabled =
            true;

        StopAgentButton.Visibility =
            Visibility.Visible;

        AgentStateText.Text =
            "Working";

        AgentStateDot.Fill =
            FindResource(
                "AccentStrongBrush")
            as Brush;

        AddActivityLine(
            "Starting task…");
    }

    private void SetAgentStateReady()
    {
        AgentStateText.Text =
            "Ready";

        AgentStateDot.Fill =
            FindResource(
                "SuccessBrush")
            as Brush;
    }

    private void AddActivityLine(
        string message)
    {
        if (string.IsNullOrWhiteSpace(
                message))
        {
            return;
        }

        if (_activityEntries.Count > 0
            && string.Equals(
                _activityEntries[^1],
                message,
                StringComparison.Ordinal))
        {
            return;
        }

        _activityEntries.Add(
            message);

        while (_activityEntries.Count > 7)
        {
            _activityEntries.RemoveAt(
                0);
        }
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
            // Cosmetic only.
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
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
using Seko.Infrastructure.Attachments;
using Seko.Infrastructure.Diagnostics;
using Seko.Infrastructure.Workspaces;

namespace Seko.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ChatMessage> _conversation =
        new();

    private readonly List<ChatMessage> _agentConversation =
        new();

    private readonly ObservableCollection<Workspace> _workspaces =
        new();

    private readonly ObservableCollection<string> _activityEntries =
        new();

    private readonly ObservableCollection<SekoTaskLogSummary> _activityHistoryEntries =
        new();

    private readonly ObservableCollection<SekoAttachment> _pendingAttachments =
        new();

    private readonly SekoTaskLogArchive _taskLogArchive =
        new();

    private readonly SekoAttachmentAnalyzer _attachmentAnalyzer =
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

        AttachmentList.ItemsSource =
            _pendingAttachments;

        RefreshAttachmentTray();

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
        if (e.Key == Key.V
            && Keyboard.Modifiers.HasFlag(
                ModifierKeys.Control))
        {
            if (TryPasteClipboardImage())
            {
                e.Handled =
                    true;
            }

            return;
        }

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

    private void AttachmentMenuButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSending
            || _restartScheduled)
        {
            return;
        }

        if (AttachmentMenuButton.ContextMenu
            is not ContextMenu menu)
        {
            return;
        }

        menu.PlacementTarget =
            AttachmentMenuButton;

        menu.Placement =
            System.Windows.Controls.Primitives.PlacementMode.Top;

        menu.IsOpen =
            true;
    }

    private void AttachFileMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSending
            || _restartScheduled)
        {
            return;
        }

        var dialog =
            new OpenFileDialog
            {
                Title =
                    "Attach local context to Seko",

                Multiselect =
                    true,

                CheckFileExists =
                    true,

                Filter =
                    "Supported files|*.txt;*.md;*.log;*.csv;*.json;*.jsonc;*.xml;*.yml;*.yaml;*.toml;*.ini;*.config;*.cs;*.xaml;*.csproj;*.sln;*.props;*.targets;*.html;*.css;*.js;*.ts;*.ps1;*.py;*.sql;*.png;*.jpg;*.jpeg;*.webp|All files|*.*"
            };

        var result =
            dialog.ShowDialog(
                this);

        if (result != true)
        {
            return;
        }

        foreach (var fileName
                 in dialog.FileNames)
        {
            if (_pendingAttachments.Count
                >= SekoAttachmentAnalyzer.MaximumAttachments)
            {
                ShowAttachmentNotice(
                    $"Seko accepts up to {SekoAttachmentAnalyzer.MaximumAttachments} attachments per message.");

                break;
            }

            TryAddAttachment(
                fileName);
        }

        RefreshAttachmentTray();

        MessageInput.Focus();
    }

    private async void CaptureScreenMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSending
            || _restartScheduled)
        {
            return;
        }

        if (_pendingAttachments.Count
            >= SekoAttachmentAnalyzer.MaximumAttachments)
        {
            ShowAttachmentNotice(
                $"Seko accepts up to {SekoAttachmentAnalyzer.MaximumAttachments} attachments per message.");

            return;
        }

        var hiddenForCapture =
            false;

        try
        {
            Hide();

            hiddenForCapture =
                true;

            await Task.Delay(
                250);

            var path =
                SekoScreenCaptureService.CapturePrimaryScreen();

            Show();

            hiddenForCapture =
                false;

            Activate();

            TryAddAttachment(
                path);

            RefreshAttachmentTray();

            MessageInput.Focus();
        }
        catch (Exception exception)
        {
            if (hiddenForCapture)
            {
                Show();

                Activate();
            }

            ShowAttachmentNotice(
                "Seko could not capture the primary screen.\n\n"
                + exception.Message);
        }
    }

    private void MessageInput_Pasting(
        object sender,
        DataObjectPastingEventArgs e)
    {
        if (TryPasteClipboardImage())
        {
            e.CancelCommand();
        }
    }

    private bool TryPasteClipboardImage()
    {
        if (_isSending
            || _restartScheduled)
        {
            return false;
        }

        string? path;

        try
        {
            if (!SekoScreenCaptureService.TrySaveClipboardImage(
                    out path))
            {
                return false;
            }
        }
        catch (Exception exception)
        {
            ShowAttachmentNotice(
                "Seko could not paste the image from the Windows clipboard.\n\n"
                + exception.Message);

            return true;
        }

        if (string.IsNullOrWhiteSpace(
                path))
        {
            return false;
        }

        if (_pendingAttachments.Count
            >= SekoAttachmentAnalyzer.MaximumAttachments)
        {
            SekoScreenCaptureService.TryDeleteOwnedCapture(
                path);

            ShowAttachmentNotice(
                $"Seko accepts up to {SekoAttachmentAnalyzer.MaximumAttachments} attachments per message.");

            return true;
        }

        TryAddAttachment(
            path);

        RefreshAttachmentTray();

        MessageInput.Focus();

        return true;
    }

    private void RemoveAttachmentButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSending)
        {
            return;
        }

        if (sender
            is not Button button
            || button.Tag
                is not SekoAttachment attachment)
        {
            return;
        }

        _pendingAttachments.Remove(
            attachment);

        SekoScreenCaptureService.TryDeleteOwnedCapture(
            attachment.FilePath);

        RefreshAttachmentTray();

        MessageInput.Focus();
    }

    private void TryAddAttachment(
        string filePath)
    {
        try
        {
            var attachment =
                _attachmentAnalyzer.CreateAttachment(
                    filePath);

            var alreadyAdded =
                _pendingAttachments.Any(
                    current =>
                        string.Equals(
                            current.FilePath,
                            attachment.FilePath,
                            StringComparison.OrdinalIgnoreCase));

            if (alreadyAdded)
            {
                return;
            }

            _pendingAttachments.Add(
                attachment);
        }
        catch (Exception exception)
        {
            ShowAttachmentNotice(
                exception.Message);
        }
    }

    private void RefreshAttachmentTray()
    {
        AttachmentTray.Visibility =
            _pendingAttachments.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void ShowAttachmentNotice(
        string message)
    {
        MessageBox.Show(
            this,
            message,
            "Seko attachment",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static string BuildDisplayUserText(
        string request,
        IReadOnlyList<SekoAttachment> attachments)
    {
        var displayRequest =
            string.IsNullOrWhiteSpace(
                request)
                ? "Please inspect the attached context."
                : request.Trim();

        if (attachments.Count == 0)
        {
            return
                displayRequest;
        }

        return
            displayRequest
            + "\n\nAttached: "
            + string.Join(
                ", ",
                attachments.Select(
                    attachment =>
                        attachment.DisplayName));
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

        _agentConversation.Clear();

        foreach (var attachment
                 in _pendingAttachments.ToArray())
        {
            SekoScreenCaptureService.TryDeleteOwnedCapture(
                attachment.FilePath);
        }

        _pendingAttachments.Clear();

        RefreshAttachmentTray();

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
                text)
            && _pendingAttachments.Count == 0)
        {
            return;
        }

        var attachments =
            _pendingAttachments.ToArray();

        var requestText =
            string.IsNullOrWhiteSpace(
                text)
                ? "Please inspect the attached context."
                : text;

        _isSending =
            true;

        var cancellationSource =
            new CancellationTokenSource();

        _requestCancellationSource =
            cancellationSource;

        BeginAgentRun();

        MessageInput.IsEnabled =
            false;

        AttachmentMenuButton.IsEnabled =
            false;

        AttachmentList.IsEnabled =
            false;

        try
        {
            string attachmentContext =
                string.Empty;

            if (attachments.Length > 0)
            {
                AddActivityLine(
                    attachments.Any(
                        attachment =>
                            attachment.Kind
                            == SekoAttachmentKind.Image)
                        ? "Inspecting local attachments and screenshot..."
                        : "Reading local attachments...");

                attachmentContext =
                    await _attachmentAnalyzer.BuildContextAsync(
                        attachments,
                        cancellationSource.Token);
            }

            var displayUserMessage =
                new ChatMessage
                {
                    Role =
                        MessageRole.User,

                    Content =
                        BuildDisplayUserText(
                            requestText,
                            attachments)
                };

            var agentUserMessage =
                new ChatMessage
                {
                    Id =
                        displayUserMessage.Id,

                    Role =
                        MessageRole.User,

                    Content =
                        SekoAttachmentContext.Compose(
                            requestText,
                            attachmentContext),

                    CreatedAt =
                        displayUserMessage.CreatedAt
                };

            AddConversationMessage(
                displayUserMessage,
                agentUserMessage);

            MessageInput.Clear();

            _pendingAttachments.Clear();

            RefreshAttachmentTray();

            var activeAgent =
                _agent;

            var response =
                await activeAgent.SendAsync(
                    _agentConversation.ToList(),
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
                        "Something went wrong:\n\n"
                        + exception.Message
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

                AttachmentMenuButton.IsEnabled =
                    true;

                AttachmentList.IsEnabled =
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
        ChatMessage message,
        ChatMessage? agentMessage = null)
    {
        _conversation.Add(
            message);

        _agentConversation.Add(
            agentMessage
            ?? message);

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
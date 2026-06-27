using CodexAppServerWinForms.Mcp;

namespace CodexAppServerWinForms;

public sealed class MainForm : Form
{
    private readonly TextBox _codexExeTextBox;
    private readonly TextBox _repoRootTextBox;
    private readonly TextBox _filePathTextBox;
    private readonly TextBox _modelTextBox;
    private readonly ComboBox _approvalComboBox;
    private readonly ComboBox _sandboxComboBox;
    private readonly TextBox _promptTextBox;
    private readonly TextBox _assistantTextBox;
    private readonly TextBox _telemetryTextBox;
    private readonly TextBox _toolTextBox;
    private readonly TextBox _statusTextBox;
    private readonly TextBox _rawTextBox;
    private readonly Label _turnStateLabel;
    private readonly Label _tokenStateLabel;
    private readonly Button _startServerButton;
    private readonly Button _startThreadButton;
    private readonly Button _sendTurnButton;
    private readonly Button _clearTurnButton;
    private readonly Button _browseRepoButton;
    private readonly Button _browseFileButton;

    private readonly SelectedFileState _selectedFileState;

    private CodexAppServerClient? _client;
    private string _lastFinalAssistantText = string.Empty;

    public MainForm(SelectedFileState selectedFileState)
    {
        _selectedFileState = selectedFileState;

        Text = "Codex App Server WinForms Sample - MCP file boundary";
        Width = 1250;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;

        var y = 12;

        Controls.Add(new Label { Text = "Codex exe", Left = 12, Top = y + 4, Width = 90 });
        _codexExeTextBox = new TextBox { Left = 110, Top = y, Width = 180, Text = "codex" };
        _startServerButton = new Button { Left = 305, Top = y - 1, Width = 130, Text = "Start Server" };
        _startServerButton.Click += async (_, _) => await StartServerAsync();
        Controls.Add(_codexExeTextBox);
        Controls.Add(_startServerButton);

        _turnStateLabel = new Label { Left = 455, Top = y + 4, Width = 340, Text = "Server not started." };
        _tokenStateLabel = new Label { Left = 805, Top = y + 4, Width = 400, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = "Tokens: n/a" };
        Controls.Add(_turnStateLabel);
        Controls.Add(_tokenStateLabel);

        y += 38;
        Controls.Add(new Label { Text = "Repo root", Left = 12, Top = y + 4, Width = 90 });
        _repoRootTextBox = new TextBox { Left = 110, Top = y, Width = 970, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _browseRepoButton = new Button { Left = 1095, Top = y - 1, Width = 130, Text = "Browse Repo", Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _browseRepoButton.Click += (_, _) => BrowseRepo();
        Controls.Add(_repoRootTextBox);
        Controls.Add(_browseRepoButton);

        y += 38;
        Controls.Add(new Label { Text = "File", Left = 12, Top = y + 4, Width = 90 });
        _filePathTextBox = new TextBox { Left = 110, Top = y, Width = 970, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _browseFileButton = new Button { Left = 1095, Top = y - 1, Width = 130, Text = "Browse File", Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _browseFileButton.Click += (_, _) => BrowseFile();
        Controls.Add(_filePathTextBox);
        Controls.Add(_browseFileButton);

        y += 38;
        Controls.Add(new Label { Text = "Model", Left = 12, Top = y + 4, Width = 90 });
        _modelTextBox = new TextBox { Left = 110, Top = y, Width = 180, Text = "gpt-5.4" };
        Controls.Add(_modelTextBox);

        Controls.Add(new Label { Text = "Approval", Left = 305, Top = y + 4, Width = 70 });
        _approvalComboBox = new ComboBox { Left = 380, Top = y, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _approvalComboBox.Items.AddRange(["never", "on-request", "on-failure", "untrusted"]);
        _approvalComboBox.SelectedItem = "never";
        Controls.Add(_approvalComboBox);

        Controls.Add(new Label { Text = "Sandbox", Left = 545, Top = y + 4, Width = 70 });
        _sandboxComboBox = new ComboBox { Left = 620, Top = y, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
        _sandboxComboBox.Items.AddRange(["read-only", "workspace-write", "danger-full-access"]);
        _sandboxComboBox.SelectedItem = "read-only";
        Controls.Add(_sandboxComboBox);

        _startThreadButton = new Button { Left = 805, Top = y - 1, Width = 130, Text = "Start Thread" };
        _startThreadButton.Click += async (_, _) => await StartThreadAsync();
        Controls.Add(_startThreadButton);

        _clearTurnButton = new Button { Left = 950, Top = y - 1, Width = 130, Text = "Clear Output" };
        _clearTurnButton.Click += (_, _) => ClearTurnViews();
        Controls.Add(_clearTurnButton);

        y += 44;
        Controls.Add(new Label { Text = "Prompt", Left = 12, Top = y + 4, Width = 90 });
        _promptTextBox = new TextBox
        {
            Left = 110,
            Top = y,
            Width = 970,
            Height = 95,
            Multiline = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = "Analyze the selected file. Fetch it through the harness MCP tool GetSelectedFile. Be specific. Do not edit files unless explicitly asked."
        };
        _sendTurnButton = new Button { Left = 1095, Top = y, Width = 130, Height = 40, Text = "Send Turn", Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _sendTurnButton.Click += async (_, _) => await SendTurnAsync();
        Controls.Add(_promptTextBox);
        Controls.Add(_sendTurnButton);

        y += 110;
        var tabs = new TabControl
        {
            Left = 12,
            Top = y,
            Width = 1213,
            Height = 640,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _assistantTextBox = CreateOutputTextBox(wordWrap: true);
        _telemetryTextBox = CreateOutputTextBox(wordWrap: false);
        _toolTextBox = CreateOutputTextBox(wordWrap: false);
        _statusTextBox = CreateOutputTextBox(wordWrap: false);
        _rawTextBox = CreateOutputTextBox(wordWrap: false);

        tabs.TabPages.Add(MakeTab("Assistant Response", _assistantTextBox));
        tabs.TabPages.Add(MakeTab("Telemetry", _telemetryTextBox));
        tabs.TabPages.Add(MakeTab("Tool / Command Events", _toolTextBox));
        tabs.TabPages.Add(MakeTab("Status / Lifecycle", _statusTextBox));
        tabs.TabPages.Add(MakeTab("Raw Protocol", _rawTextBox));
        Controls.Add(tabs);

        FormClosing += async (_, _) =>
        {
            if (_client is not null)
                await _client.DisposeAsync();
        };
    }

    private static TextBox CreateOutputTextBox(bool wordWrap)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = wordWrap,
            ReadOnly = true,
            Font = new Font("Consolas", 10)
        };
    }

    private static TabPage MakeTab(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    private async Task StartServerAsync()
    {
        try
        {
            _startServerButton.Enabled = false;
            _client = new CodexAppServerClient();

            _client.RawProtocol += line => AppendLine(_rawTextBox, line);
            _client.LogLine += line => AppendLine(_statusTextBox, line);
            _client.AssistantText += OnAssistantText;
            _client.Telemetry += OnTelemetry;
            _client.ToolActivity += OnToolActivity;
            _client.Status += OnStatus;
            _client.Exited += code => OnStatus(new StatusEvent("server", $"Codex app-server exited with code {code}."));

            await _client.StartAsync(_codexExeTextBox.Text.Trim());
            _turnStateLabel.Text = "Server started.";
        }
        catch (Exception ex)
        {
            AppendLine(_statusTextBox, "ERROR: " + ex);
            MessageBox.Show(this, ex.Message, "Failed to start Codex app-server");
            _startServerButton.Enabled = true;
            _turnStateLabel.Text = "Server failed.";
        }
    }

    private async Task StartThreadAsync()
    {
        try
        {
            if (_client is null || !_client.IsStarted)
            {
                MessageBox.Show(this, "Start Codex app-server first.");
                return;
            }

            var repoRoot = _repoRootTextBox.Text.Trim();
            if (!Directory.Exists(repoRoot))
            {
                MessageBox.Show(this, "Select a valid repo root.");
                return;
            }

            _selectedFileState.SetRepoRoot(repoRoot);

            _turnStateLabel.Text = "Starting thread...";

            await _client.StartThreadAsync(
                repoRoot: repoRoot,
                model: _modelTextBox.Text.Trim(),
                approvalPolicy: _approvalComboBox.SelectedItem?.ToString() ?? "never",
                sandbox: _sandboxComboBox.SelectedItem?.ToString() ?? "read-only");

            _turnStateLabel.Text = "Thread ready.";
        }
        catch (Exception ex)
        {
            AppendLine(_statusTextBox, "ERROR: " + ex);
            MessageBox.Show(this, ex.Message, "Failed to start thread");
            _turnStateLabel.Text = "Thread failed.";
        }
    }

    private async Task SendTurnAsync()
    {
        try
        {
            if (_client is null || !_client.IsStarted)
            {
                MessageBox.Show(this, "Start Codex app-server first.");
                return;
            }

            var repoRoot = _repoRootTextBox.Text.Trim();
            var filePath = _filePathTextBox.Text.Trim();

            if (!Directory.Exists(repoRoot))
            {
                MessageBox.Show(this, "Select a valid repo root.");
                return;
            }

            if (!File.Exists(filePath))
            {
                MessageBox.Show(this, "Select a valid file.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_client.ThreadId))
                await StartThreadAsync();

            if (string.IsNullOrWhiteSpace(_client.ThreadId))
                return;

            ClearTurnViews(keepStatus: true, keepRaw: true);
            _turnStateLabel.Text = "Sending turn...";

            _selectedFileState.SetSelection(repoRoot, filePath);

            var relativePath = Path.GetRelativePath(repoRoot, filePath);
            var userPrompt = _promptTextBox.Text.Trim();
            var prompt = $$"""
            {{userPrompt}}

            Harness-controlled source access is available through the local MCP server.

            MCP server:
            {{McpHostFactory.LocalMcpUrl}}

            Required tool boundary:
            - Call GetSelectedFile from the harness MCP server to fetch the selected file.
            - Do not rely on file content being in this prompt.
            - Do not bypass the harness by directly reading or searching files unless the user explicitly asks for broader repo inspection.

            Repository root / Codex cwd:
            {{repoRoot}}

            Selected file path, metadata only:
            {{relativePath}}
            """;

            await _client.StartTurnAsync(prompt);
            _turnStateLabel.Text = "Turn running / streaming.";
        }
        catch (Exception ex)
        {
            AppendLine(_statusTextBox, "ERROR: " + ex);
            MessageBox.Show(this, ex.Message, "Failed to send turn");
            _turnStateLabel.Text = "Turn failed.";
        }
    }

    private void BrowseRepo()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select repository root / Codex cwd"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _repoRootTextBox.Text = dialog.SelectedPath;

            try
            {
                _selectedFileState.SetRepoRoot(dialog.SelectedPath);

                var filePath = _filePathTextBox.Text.Trim();
                if (File.Exists(filePath))
                    _selectedFileState.SetSelection(dialog.SelectedPath, filePath);
            }
            catch (Exception ex)
            {
                AppendLine(_statusTextBox, "Repo selection error: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Failed to select repo root");
            }
        }
    }

    private void BrowseFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select a file to expose through the harness MCP tool",
            Filter = "All files (*.*)|*.*|C# files (*.cs)|*.cs|Text files (*.txt)|*.txt",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _filePathTextBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(_repoRootTextBox.Text))
            {
                var dir = Path.GetDirectoryName(dialog.FileName);
                if (dir is not null)
                    _repoRootTextBox.Text = dir;
            }

            try
            {
                var repoRoot = _repoRootTextBox.Text.Trim();
                if (Directory.Exists(repoRoot))
                    _selectedFileState.SetSelection(repoRoot, dialog.FileName);
                else
                    _selectedFileState.SetSelectedFile(dialog.FileName);

                AppendLine(_statusTextBox, $"Selected MCP file: {dialog.FileName}");
            }
            catch (Exception ex)
            {
                AppendLine(_statusTextBox, "Selection error: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Failed to select MCP file");
            }
        }
    }

    private void OnAssistantText(AssistantTextEvent e)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(() => OnAssistantText(e));
            return;
        }

        if (e.IsFinal)
        {
            // Codex sends token deltas while streaming and later sends the completed message.
            // Replace with the completed text so the response tab is clean and not duplicated.
            _lastFinalAssistantText = e.Text;
            _assistantTextBox.Text = e.Text;
            _turnStateLabel.Text = "Assistant response completed.";
        }
        else if (string.IsNullOrEmpty(_lastFinalAssistantText))
        {
            _assistantTextBox.AppendText(e.Text);
        }
    }

    private void OnTelemetry(TelemetryEvent e)
    {
        AppendLine(_telemetryTextBox, e.Summary);

        if (e.InputTokens.HasValue || e.OutputTokens.HasValue)
        {
            SetLabelSafe(_tokenStateLabel,
                $"Tokens: in {e.InputTokens:N0}, cached {e.CachedInputTokens:N0}, out {e.OutputTokens:N0}, window {e.ModelContextWindow:N0}");
        }
        else if (e.PrimaryUsedPercent.HasValue || e.SecondaryUsedPercent.HasValue)
        {
            AppendLine(_telemetryTextBox,
                $"Usage: primary {e.PrimaryUsedPercent}%, secondary {e.SecondaryUsedPercent}%, plan {e.PlanType}");
        }
    }

    private void OnToolActivity(ToolEvent e)
    {
        var detail = string.IsNullOrWhiteSpace(e.Detail) ? string.Empty : " | " + e.Detail;
        AppendLine(_toolTextBox, $"{e.EventType}: {e.Name} [{e.Status}]{detail}");
    }

    private void OnStatus(StatusEvent e)
    {
        AppendLine(_statusTextBox, $"{e.EventType}: {e.Summary}");

        if (e.EventType.Contains("turn", StringComparison.OrdinalIgnoreCase) ||
            e.EventType.Contains("assistant", StringComparison.OrdinalIgnoreCase) ||
            e.EventType.Contains("thread", StringComparison.OrdinalIgnoreCase))
        {
            SetLabelSafe(_turnStateLabel, e.Summary.Length > 80 ? e.Summary[..80] + "..." : e.Summary);
        }
    }

    private void ClearTurnViews(bool keepStatus = false, bool keepRaw = false)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ClearTurnViews(keepStatus, keepRaw));
            return;
        }

        _lastFinalAssistantText = string.Empty;
        _assistantTextBox.Clear();
        _telemetryTextBox.Clear();
        _toolTextBox.Clear();
        if (!keepStatus)
            _statusTextBox.Clear();
        if (!keepRaw)
            _rawTextBox.Clear();
        _tokenStateLabel.Text = "Tokens: n/a";
    }

    private void AppendLine(TextBox textBox, string text)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLine(textBox, text));
            return;
        }

        textBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }

    private void SetLabelSafe(Label label, string text)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(() => SetLabelSafe(label, text));
            return;
        }

        label.Text = text;
    }
}

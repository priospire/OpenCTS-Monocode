using OpenCTS.Core;

namespace OpenCTS.App;

public sealed class MainForm : Form
{
    private readonly ScratchProjectConverter _converter = new();
    private readonly TextBox _inputPathTextBox = new();
    private readonly TextBox _outputPathTextBox = new();
    private readonly RichTextBox _sourceEditor = new();
    private readonly RichTextBox _statusTextBox = new();
    private readonly Button _convertButton = new();
    private readonly Button _repairButton = new();
    private readonly Button _saveSourceButton = new();
    private readonly CheckBox _attemptRepairCheckBox = new();
    private readonly CheckBox _darkModeCheckBox = new();
    private readonly System.Windows.Forms.Timer _diagnosticsTimer = new() { Interval = 350 };
    private readonly List<DiagnosticDisplaySpan> _displayedDiagnostics = [];

    private UiTheme _theme = UiTheme.Dark;
    private ScratchProjectEditSession? _editSession;
    private string? _editSessionPath;
    private bool _isApplyingHighlight;
    private bool _isBusy;
    private bool _diagnosticsPending;
    private int _diagnosticsVersion;

    public MainForm()
    {
        Text = "ScratchASM IDE";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        Size = new Size(1180, 760);
        Font = new Font("Segoe UI", 9F);

        _diagnosticsTimer.Tick += DiagnosticsTimer_Tick;
        Controls.Add(BuildLayout());
        ApplyTheme();
        SetStatus("Ready.", _theme.Muted);
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreatePathsPanel(), 0, 1);
        root.Controls.Add(CreateEditorPanel(), 0, 2);
        root.Controls.Add(CreateStatusPanel(), 0, 3);
        return root;
    }

    private Control CreateHeader()
    {
        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label title = new()
        {
            Text = "ScratchASM IDE",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold),
            Margin = new Padding(0, 8, 0, 0)
        };
        header.Controls.Add(title, 0, 0);

        FlowLayoutPanel actions = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false
        };

        _convertButton.Text = "Compile";
        _convertButton.MinimumSize = new Size(104, 34);
        _convertButton.Click += ConvertButton_Click;
        actions.Controls.Add(_convertButton);

        _repairButton.Text = "Repair";
        _repairButton.MinimumSize = new Size(92, 34);
        _repairButton.Click += RepairButton_Click;
        actions.Controls.Add(_repairButton);

        _saveSourceButton.Text = "Save Source";
        _saveSourceButton.MinimumSize = new Size(108, 34);
        _saveSourceButton.Click += SaveSourceButton_Click;
        actions.Controls.Add(_saveSourceButton);

        _darkModeCheckBox.Text = "Dark";
        _darkModeCheckBox.Checked = true;
        _darkModeCheckBox.AutoSize = true;
        _darkModeCheckBox.Margin = new Padding(12, 9, 8, 0);
        _darkModeCheckBox.CheckedChanged += DarkModeCheckBox_CheckedChanged;
        actions.Controls.Add(_darkModeCheckBox);

        header.Controls.Add(actions, 1, 0);
        return header;
    }

    private Control CreatePathsPanel()
    {
        Panel shell = CreateSurfacePanel(new Padding(12));
        TableLayoutPanel paths = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        paths.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        paths.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        paths.Controls.Add(CreateInputRow(), 0, 0);
        paths.Controls.Add(CreateOutputRow(), 0, 1);
        shell.Controls.Add(paths);
        return shell;
    }

    private Control CreateInputRow()
    {
        TableLayoutPanel row = CreatePathRow("Input");
        _inputPathTextBox.PlaceholderText = ".sasm, .mono, .sb3, project.json, or project folder";
        _inputPathTextBox.Leave += InputPathTextBox_Leave;
        row.Controls.Add(_inputPathTextBox, 1, 0);
        row.Controls.Add(CreateButton("File", BrowseInputFile), 2, 0);
        row.Controls.Add(CreateButton("Folder", BrowseInputFolder), 3, 0);
        return row;
    }

    private Control CreateOutputRow()
    {
        TableLayoutPanel row = CreatePathRow("Output");
        _outputPathTextBox.PlaceholderText = "Output Scratch project path";
        row.Controls.Add(_outputPathTextBox, 1, 0);
        row.Controls.Add(CreateButton("Save As", BrowseOutputFile), 2, 0);
        _attemptRepairCheckBox.Text = "Repair";
        _attemptRepairCheckBox.AutoSize = true;
        _attemptRepairCheckBox.Margin = new Padding(10, 8, 0, 0);
        row.Controls.Add(_attemptRepairCheckBox, 3, 0);
        return row;
    }

    private Control CreateEditorPanel()
    {
        Panel shell = CreateSurfacePanel(new Padding(0));
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(CreatePanelHeader("ScratchASM Source"), 0, 0);

        _sourceEditor.Dock = DockStyle.Fill;
        _sourceEditor.BorderStyle = BorderStyle.None;
        _sourceEditor.AcceptsTab = true;
        _sourceEditor.WordWrap = false;
        _sourceEditor.DetectUrls = false;
        _sourceEditor.Font = CreateMonoFont(10F);
        _sourceEditor.TextChanged += SourceEditor_TextChanged;
        panel.Controls.Add(_sourceEditor, 0, 1);
        shell.Controls.Add(panel);
        return shell;
    }

    private Control CreateStatusPanel()
    {
        Panel shell = CreateSurfacePanel(new Padding(0));
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(CreatePanelHeader("Diagnostics"), 0, 0);

        _statusTextBox.Dock = DockStyle.Fill;
        _statusTextBox.BorderStyle = BorderStyle.None;
        _statusTextBox.Multiline = true;
        _statusTextBox.ReadOnly = true;
        _statusTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        _statusTextBox.Font = CreateMonoFont(9F);
        _statusTextBox.WordWrap = false;
        _statusTextBox.MouseDoubleClick += StatusTextBox_MouseDoubleClick;
        panel.Controls.Add(_statusTextBox, 0, 1);
        shell.Controls.Add(panel);
        return shell;
    }

    private static TableLayoutPanel CreatePathRow(string labelText)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 4)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label label = new()
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
        };
        row.Controls.Add(label, 0, 0);
        return row;
    }

    private Panel CreateSurfacePanel(Padding padding)
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            Padding = padding,
            Margin = new Padding(0, 0, 0, 10),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private Label CreatePanelHeader(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
        };
    }

    private static Button CreateButton(string text, EventHandler clickHandler)
    {
        Button button = new()
        {
            Text = text,
            MinimumSize = new Size(84, 30),
            Margin = new Padding(8, 0, 0, 0)
        };
        button.Click += clickHandler;
        return button;
    }

    private void BrowseInputFile(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Select ScratchASM or Scratch input",
            Filter = "ScratchASM and Scratch inputs (*.sasm;*.mono;*.sb3;*.json)|*.sasm;*.mono;*.sb3;*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _inputPathTextBox.Text = dialog.FileName;
            SetDefaultOutputPath(dialog.FileName);
            LoadInputPreview(dialog.FileName);
        }
    }

    private void BrowseInputFolder(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Select a folder containing project.json"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _inputPathTextBox.Text = dialog.SelectedPath;
            SetDefaultOutputPath(dialog.SelectedPath);
            _editSession = null;
            _editSessionPath = null;
            SetStatus("Folder input selected.", _theme.Muted);
        }
    }

    private void BrowseOutputFile(object? sender, EventArgs e)
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Save Scratch project",
            Filter = "Scratch 3 project (*.sb3)|*.sb3|All files (*.*)|*.*",
            DefaultExt = "sb3",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (!string.IsNullOrWhiteSpace(_outputPathTextBox.Text))
        {
            dialog.FileName = TrimPath(_outputPathTextBox.Text);
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputPathTextBox.Text = dialog.FileName;
        }
    }

    private async void ConvertButton_Click(object? sender, EventArgs e)
    {
        await RunConversionAsync(forceRepair: false);
    }

    private async void RepairButton_Click(object? sender, EventArgs e)
    {
        await RunConversionAsync(forceRepair: true);
    }

    private void SaveSourceButton_Click(object? sender, EventArgs e)
    {
        string inputPath = TrimPath(_inputPathTextBox.Text);
        if (IsScratchAsmPath(inputPath))
        {
            try
            {
                File.WriteAllText(inputPath, _sourceEditor.Text);
                SetStatus($"Saved {inputPath}", _theme.Success);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                SetStatus(ex.Message, _theme.Error);
            }

            return;
        }

        using SaveFileDialog dialog = new()
        {
            Title = "Save ScratchASM source",
            Filter = "ScratchASM source (*.sasm)|*.sasm|All files (*.*)|*.*",
            DefaultExt = "sasm",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, _sourceEditor.Text);
            _inputPathTextBox.Text = dialog.FileName;
            SetDefaultOutputPath(dialog.FileName);
            SetStatus($"Saved {dialog.FileName}", _theme.Success);
        }
    }

    private async Task RunConversionAsync(bool forceRepair)
    {
        string inputPath = TrimPath(_inputPathTextBox.Text);
        string outputPath = TrimPath(_outputPathTextBox.Text);
        bool attemptRepair = forceRepair || _attemptRepairCheckBox.Checked;

        SetBusy(true);
        _diagnosticsPending = false;
        _diagnosticsTimer.Stop();
        _diagnosticsVersion++;
        SetStatus(forceRepair ? "Repairing..." : "Compiling...", _theme.Muted);

        try
        {
            List<ValidationIssue> prefixIssues = [];
            ConversionResult result;
            if (CanMergeEditedSb3(inputPath))
            {
                string source = _sourceEditor.Text;
                if (attemptRepair)
                {
                    ScratchAsmSourceRepairResult repair = ScratchAsmSourceRepairer.Repair(source);
                    source = repair.SourceText;
                    prefixIssues.AddRange(repair.Issues);
                    ReplaceEditorText(source);
                }

                ScratchProjectEditSession session = _editSession!;
                result = await Task.Run(() => session.WriteEdited(source, outputPath));
            }
            else
            {
                string? sourceOverride = IsScratchAsmPath(inputPath) && _sourceEditor.TextLength > 0
                    ? _sourceEditor.Text
                    : null;
                if (attemptRepair && sourceOverride is not null)
                {
                    ScratchAsmSourceRepairResult repair = ScratchAsmSourceRepairer.Repair(sourceOverride);
                    sourceOverride = repair.SourceText;
                    prefixIssues.AddRange(repair.Issues);
                    ReplaceEditorText(sourceOverride);
                }

                result = await Task.Run(() => _converter.ConvertToSb3(inputPath, outputPath, new ConversionOptions
                {
                    AttemptSafeRepair = attemptRepair,
                    ScratchAsmSourceText = sourceOverride
                }));
            }

            ShowConversionResult(result, prefixIssues);
        }
        finally
        {
            SetBusy(false);
            if (_diagnosticsPending && _sourceEditor.TextLength > 0)
            {
                _diagnosticsTimer.Start();
            }
        }
    }

    private bool CanMergeEditedSb3(string inputPath)
    {
        return _editSession is not null &&
            _editSessionPath is not null &&
            string.Equals(Path.GetFullPath(inputPath), _editSessionPath, StringComparison.OrdinalIgnoreCase) &&
            _sourceEditor.TextLength > 0;
    }

    private void ShowConversionResult(ConversionResult result, IReadOnlyList<ValidationIssue> prefixIssues)
    {
        List<ValidationIssue> issues = [.. prefixIssues, .. result.Issues];
        if (result.Success)
        {
            string text = $"Wrote {result.OutputPath}";
            if (issues.Count == 0)
            {
                SetStatus(text, _theme.Success);
                return;
            }

            ShowIssues(issues, text, _theme.Success);
            return;
        }

        ShowIssues(issues, "Build failed.", _theme.Error);
    }

    private void SetDefaultOutputPath(string inputPath)
    {
        if (!string.IsNullOrWhiteSpace(_outputPathTextBox.Text))
        {
            return;
        }

        try
        {
            string fullInputPath = Path.GetFullPath(inputPath);
            string outputDirectory;
            string outputName;

            if (Directory.Exists(fullInputPath))
            {
                outputDirectory = Directory.GetParent(fullInputPath)?.FullName ?? fullInputPath;
                outputName = Path.GetFileName(fullInputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            else
            {
                outputDirectory = Path.GetDirectoryName(fullInputPath) ?? Directory.GetCurrentDirectory();
                outputName = string.Equals(Path.GetFileName(fullInputPath), "project.json", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(outputDirectory)
                    : Path.GetFileNameWithoutExtension(fullInputPath);
            }

            if (string.IsNullOrWhiteSpace(outputName))
            {
                outputName = "project";
            }

            _outputPathTextBox.Text = Path.Combine(outputDirectory, outputName + ".sb3");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SetStatus(ex.Message, _theme.Error);
        }
    }

    private void InputPathTextBox_Leave(object? sender, EventArgs e)
    {
        string inputPath = TrimPath(_inputPathTextBox.Text);
        if (!string.IsNullOrWhiteSpace(inputPath))
        {
            SetDefaultOutputPath(inputPath);
            LoadInputPreview(inputPath);
        }
    }

    private void LoadInputPreview(string inputPath)
    {
        _editSession = null;
        _editSessionPath = null;
        if (IsScratchAsmPath(inputPath))
        {
            LoadScratchAsmSource(inputPath);
            return;
        }

        if (!string.Equals(Path.GetExtension(inputPath), ".sb3", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(inputPath))
        {
            return;
        }

        try
        {
            ScratchProjectEditSession session = ScratchProjectEditSession.Open(inputPath);
            _editSession = session;
            _editSessionPath = Path.GetFullPath(inputPath);
            ReplaceEditorText(session.SourceText);
            if (session.Issues.Count == 0)
            {
                SetStatus("Loaded .sb3 as editable ScratchASM source.", _theme.Success);
            }
            else
            {
                ShowIssues(session.Issues, "Loaded .sb3 with diagnostics.", _theme.Warning);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus($"Could not decompile .sb3: {ex.Message}", _theme.Error);
        }
    }

    private void LoadScratchAsmSource(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            return;
        }

        try
        {
            ReplaceEditorText(File.ReadAllText(inputPath));
            SetStatus(ScratchAsmLanguage.IsCompatibilitySourceName(inputPath)
                ? "Loaded legacy .mono source. Save new files as .sasm."
                : "Loaded ScratchASM source.",
                ScratchAsmLanguage.IsCompatibilitySourceName(inputPath) ? _theme.Warning : _theme.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus(ex.Message, _theme.Error);
        }
    }

    private void ReplaceEditorText(string text)
    {
        _isApplyingHighlight = true;
        try
        {
            _sourceEditor.Text = text;
        }
        finally
        {
            _isApplyingHighlight = false;
        }

        ApplyScratchAsmHighlighting();
        ScheduleDiagnostics();
    }

    private void SourceEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_isApplyingHighlight)
        {
            return;
        }

        ApplyScratchAsmHighlighting();
        ScheduleDiagnostics();
    }

    private void ScheduleDiagnostics()
    {
        _diagnosticsTimer.Stop();
        _diagnosticsPending = true;
        _diagnosticsVersion++;

        if (_sourceEditor.TextLength == 0)
        {
            _diagnosticsPending = false;
            if (!_isBusy)
            {
                SetStatus("Ready.", _theme.Muted);
            }

            return;
        }

        if (!_isBusy)
        {
            _diagnosticsTimer.Start();
        }
    }

    private async void DiagnosticsTimer_Tick(object? sender, EventArgs e)
    {
        _diagnosticsTimer.Stop();
        if (_isBusy || _sourceEditor.TextLength == 0)
        {
            return;
        }

        _diagnosticsPending = false;
        int version = _diagnosticsVersion;
        string sourceText = _sourceEditor.Text;
        string inputPath = TrimPath(_inputPathTextBox.Text);
        string sourceName = IsScratchAsmPath(inputPath) ? inputPath : "editor.sasm";
        IReadOnlyList<CtsDiagnostic> diagnostics = await Task.Run(
            () => CtsCompiler.Compile(sourceText, sourceName).Diagnostics);

        if (version != _diagnosticsVersion || _isBusy || IsDisposed)
        {
            return;
        }

        ShowDiagnostics(diagnostics);
    }

    private void ShowDiagnostics(IReadOnlyList<CtsDiagnostic> diagnostics)
    {
        _displayedDiagnostics.Clear();
        _statusTextBox.Clear();
        if (diagnostics.Count == 0)
        {
            AppendStatusLine("No ScratchASM diagnostics.", _theme.Success, null);
            return;
        }

        foreach (CtsDiagnostic diagnostic in diagnostics)
        {
            Color color = diagnostic.Severity == DiagnosticSeverity.Error ? _theme.Error : _theme.Warning;
            AppendStatusLine(FormatDiagnostic(diagnostic), color, diagnostic.Span);
        }

        _statusTextBox.Select(0, 0);
    }

    private void ShowIssues(IReadOnlyList<ValidationIssue> issues, string header, Color headerColor)
    {
        _displayedDiagnostics.Clear();
        _statusTextBox.Clear();
        AppendStatusLine(header, headerColor, null);
        foreach (ValidationIssue issue in issues)
        {
            Color color = issue.Severity == DiagnosticSeverity.Error ? _theme.Error : _theme.Warning;
            AppendStatusLine(Program.FormatIssue(issue), color, issue.Span);
        }

        _statusTextBox.Select(0, 0);
    }

    private void SetStatus(string text, Color color)
    {
        _displayedDiagnostics.Clear();
        _statusTextBox.Clear();
        AppendStatusLine(text, color, null);
    }

    private void AppendStatusLine(string text, Color color, SourceSpan? sourceSpan)
    {
        int start = _statusTextBox.TextLength;
        _statusTextBox.SelectionColor = color;
        _statusTextBox.AppendText(text);
        if (sourceSpan is not null)
        {
            _displayedDiagnostics.Add(new DiagnosticDisplaySpan(start, text.Length, sourceSpan));
        }

        _statusTextBox.SelectionColor = _theme.Text;
        _statusTextBox.AppendText(Environment.NewLine);
    }

    private static string FormatDiagnostic(CtsDiagnostic diagnostic)
    {
        return $"{diagnostic.Severity} {diagnostic.Code} line {diagnostic.Span.Start.Line}, column {diagnostic.Span.Start.Column}: {diagnostic.Message}";
    }

    private void StatusTextBox_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        int characterIndex = _statusTextBox.GetCharIndexFromPosition(e.Location);
        DiagnosticDisplaySpan? display = _displayedDiagnostics.FirstOrDefault(candidate =>
            characterIndex >= candidate.Start && characterIndex < candidate.Start + candidate.Length);
        if (display is null)
        {
            return;
        }

        int start = CtsSourcePosition.GetOffset(_sourceEditor.Text, display.Span.Start);
        int end = CtsSourcePosition.GetOffset(_sourceEditor.Text, display.Span.End);
        _sourceEditor.Select(start, Math.Max(0, end - start));
        _sourceEditor.ScrollToCaret();
        _sourceEditor.Focus();
    }

    private void ApplyScratchAsmHighlighting()
    {
        if (_sourceEditor.TextLength == 0)
        {
            return;
        }

        _isApplyingHighlight = true;
        int selectionStart = _sourceEditor.SelectionStart;
        int selectionLength = _sourceEditor.SelectionLength;

        try
        {
            _sourceEditor.SuspendLayout();
            _sourceEditor.SelectAll();
            _sourceEditor.SelectionColor = _theme.EditorText;

            foreach (CtsColorSpan span in CtsSyntaxClassifier.Classify(_sourceEditor.Text))
            {
                if (span.Start < 0 || span.Start + span.Length > _sourceEditor.TextLength)
                {
                    continue;
                }

                _sourceEditor.Select(span.Start, span.Length);
                _sourceEditor.SelectionColor = AdjustSyntaxColor(span);
            }

            _sourceEditor.Select(
                Math.Min(selectionStart, _sourceEditor.TextLength),
                Math.Min(selectionLength, Math.Max(0, _sourceEditor.TextLength - selectionStart)));
        }
        finally
        {
            _sourceEditor.ResumeLayout();
            _isApplyingHighlight = false;
        }
    }

    private Color AdjustSyntaxColor(CtsColorSpan span)
    {
        Color color = ColorTranslator.FromHtml(span.Color);
        if (_theme.IsDark && span.Kind == "Comment")
        {
            return _theme.Muted;
        }

        return color;
    }

    private void DarkModeCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        _theme = _darkModeCheckBox.Checked ? UiTheme.Dark : UiTheme.Light;
        ApplyTheme();
        ApplyScratchAsmHighlighting();
    }

    private void ApplyTheme()
    {
        BackColor = _theme.Background;
        ForeColor = _theme.Text;
        ApplyThemeToControl(this);
        _sourceEditor.BackColor = _theme.EditorBackground;
        _sourceEditor.ForeColor = _theme.EditorText;
        _statusTextBox.BackColor = _theme.StatusBackground;
        _statusTextBox.ForeColor = _theme.Text;
    }

    private void ApplyThemeToControl(Control control)
    {
        foreach (Control child in control.Controls)
        {
            child.ForeColor = _theme.Text;
            switch (child)
            {
                case TableLayoutPanel table:
                    table.BackColor = _theme.Background;
                    break;
                case Panel panel:
                    panel.BackColor = _theme.Surface;
                    break;
                case Label label:
                    label.BackColor = Color.Transparent;
                    break;
                case Button button:
                    button.BackColor = _theme.Accent;
                    button.ForeColor = Color.White;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = _theme.AccentDark;
                    button.FlatAppearance.BorderSize = 1;
                    button.Cursor = Cursors.Hand;
                    break;
                case CheckBox checkBox:
                    checkBox.BackColor = Color.Transparent;
                    checkBox.ForeColor = _theme.Text;
                    break;
                case TextBox textBox:
                    textBox.BackColor = _theme.InputBackground;
                    textBox.ForeColor = _theme.Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
            }

            ApplyThemeToControl(child);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        _convertButton.Enabled = !busy;
        _repairButton.Enabled = !busy;
        _saveSourceButton.Enabled = !busy;
        _attemptRepairCheckBox.Enabled = !busy;
    }

    private static string TrimPath(string path)
    {
        return path.Trim().Trim('"');
    }

    private static bool IsScratchAsmPath(string path)
    {
        return ScratchAsmLanguage.IsSupportedSourceName(path);
    }

    private static Font CreateMonoFont(float size)
    {
        try
        {
            return new Font("Cascadia Mono", size);
        }
        catch (ArgumentException)
        {
            return new Font(FontFamily.GenericMonospace, size);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _diagnosticsTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed record DiagnosticDisplaySpan(int Start, int Length, SourceSpan Span);

    private sealed record UiTheme(
        bool IsDark,
        Color Background,
        Color Surface,
        Color InputBackground,
        Color EditorBackground,
        Color StatusBackground,
        Color Text,
        Color EditorText,
        Color Muted,
        Color Accent,
        Color AccentDark,
        Color Success,
        Color Warning,
        Color Error)
    {
        public static UiTheme Dark { get; } = new(
            true,
            Color.FromArgb(16, 20, 27),
            Color.FromArgb(25, 31, 42),
            Color.FromArgb(14, 18, 25),
            Color.FromArgb(11, 15, 22),
            Color.FromArgb(14, 18, 25),
            Color.FromArgb(229, 234, 242),
            Color.FromArgb(229, 234, 242),
            Color.FromArgb(149, 160, 177),
            Color.FromArgb(37, 99, 235),
            Color.FromArgb(29, 78, 216),
            Color.FromArgb(34, 197, 94),
            Color.FromArgb(245, 158, 11),
            Color.FromArgb(239, 68, 68));

        public static UiTheme Light { get; } = new(
            false,
            Color.FromArgb(244, 247, 251),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(250, 252, 255),
            Color.FromArgb(24, 31, 42),
            Color.FromArgb(24, 31, 42),
            Color.FromArgb(96, 108, 124),
            Color.FromArgb(37, 99, 235),
            Color.FromArgb(29, 78, 216),
            Color.FromArgb(22, 163, 74),
            Color.FromArgb(202, 138, 4),
            Color.FromArgb(220, 38, 38));
    }
}

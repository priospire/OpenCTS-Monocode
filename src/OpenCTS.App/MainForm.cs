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
    private readonly CheckBox _attemptRepairCheckBox = new();
    private readonly System.Windows.Forms.Timer _diagnosticsTimer = new() { Interval = 350 };
    private readonly List<DiagnosticDisplaySpan> _displayedDiagnostics = [];
    private bool _isApplyingHighlight;
    private bool _isConverting;
    private bool _diagnosticsPending;
    private int _diagnosticsVersion;

    public MainForm()
    {
        Text = "OpenCTS Monocode Scratch SB3 Converter";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 460);
        Size = new Size(900, 560);

        _diagnosticsTimer.Tick += DiagnosticsTimer_Tick;
        Controls.Add(BuildLayout());
        SetStatus("Ready.");
    }

    private Control BuildLayout()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateInputGroup(), 0, 0);
        layout.Controls.Add(CreateSourceGroup(), 0, 1);
        layout.Controls.Add(CreateOutputGroup(), 0, 2);
        layout.Controls.Add(CreateActionRow(), 0, 3);
        layout.Controls.Add(CreateStatusGroup(), 0, 4);
        return layout;
    }

    private Control CreateInputGroup()
    {
        GroupBox group = new()
        {
            Text = "Input project",
            Dock = DockStyle.Top,
            Padding = new Padding(10),
            AutoSize = true
        };

        TableLayoutPanel row = CreatePathRow();
        _inputPathTextBox.PlaceholderText = "Folder, project.json, .sb3, or .mono";
        _inputPathTextBox.Leave += InputPathTextBox_Leave;
        row.Controls.Add(_inputPathTextBox, 0, 0);
        row.Controls.Add(CreateButton("Browse File", BrowseInputFile), 1, 0);
        row.Controls.Add(CreateButton("Browse Folder", BrowseInputFolder), 2, 0);
        group.Controls.Add(row);
        return group;
    }

    private Control CreateSourceGroup()
    {
        GroupBox group = new()
        {
            Text = "Monocode source",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        _sourceEditor.Dock = DockStyle.Fill;
        _sourceEditor.AcceptsTab = true;
        _sourceEditor.WordWrap = false;
        _sourceEditor.Font = new Font(FontFamily.GenericMonospace, 9F);
        _sourceEditor.TextChanged += SourceEditor_TextChanged;
        group.Controls.Add(_sourceEditor);
        return group;
    }

    private Control CreateOutputGroup()
    {
        GroupBox group = new()
        {
            Text = "Output .sb3",
            Dock = DockStyle.Top,
            Padding = new Padding(10),
            AutoSize = true
        };

        TableLayoutPanel row = CreatePathRow();
        _outputPathTextBox.PlaceholderText = "Output Scratch project path";
        row.Controls.Add(_outputPathTextBox, 0, 0);
        row.Controls.Add(CreateButton("Save As", BrowseOutputFile), 1, 0);
        group.Controls.Add(row);
        return group;
    }

    private Control CreateActionRow()
    {
        FlowLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 8),
            AutoSize = true
        };

        _convertButton.Text = "Convert";
        _convertButton.AutoSize = true;
        _convertButton.MinimumSize = new Size(120, 34);
        _convertButton.Click += ConvertButton_Click;
        row.Controls.Add(_convertButton);

        _attemptRepairCheckBox.Text = "Attempt safe repair";
        _attemptRepairCheckBox.AutoSize = true;
        _attemptRepairCheckBox.Margin = new Padding(0, 8, 12, 0);
        row.Controls.Add(_attemptRepairCheckBox);
        return row;
    }

    private Control CreateStatusGroup()
    {
        GroupBox group = new()
        {
            Text = "Status and diagnostics",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        _statusTextBox.Dock = DockStyle.Fill;
        _statusTextBox.Multiline = true;
        _statusTextBox.ReadOnly = true;
        _statusTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        _statusTextBox.Font = new Font(FontFamily.GenericMonospace, 9F);
        _statusTextBox.WordWrap = false;
        _statusTextBox.MouseDoubleClick += StatusTextBox_MouseDoubleClick;
        group.Controls.Add(_statusTextBox);
        return group;
    }

    private static TableLayoutPanel CreatePathRow()
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return row;
    }

    private static Button CreateButton(string text, EventHandler clickHandler)
    {
        Button button = new()
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(108, 30),
            Margin = new Padding(8, 0, 0, 0)
        };
        button.Click += clickHandler;
        return button;
    }

    private void BrowseInputFile(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Select Scratch input",
            Filter = "Scratch inputs (*.mono;*.sb3;*.json)|*.mono;*.sb3;*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _inputPathTextBox.Text = dialog.FileName;
            LoadMonocodeSourceIfAvailable(dialog.FileName);
            SetDefaultOutputPath(dialog.FileName);
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
        string inputPath = TrimPath(_inputPathTextBox.Text);
        string outputPath = TrimPath(_outputPathTextBox.Text);
        ConversionOptions options = new()
        {
            AttemptSafeRepair = _attemptRepairCheckBox.Checked,
            MonocodeSourceText = IsMonocodePath(inputPath) && _sourceEditor.TextLength > 0
                ? _sourceEditor.Text
                : null
        };

        _convertButton.Enabled = false;
        _attemptRepairCheckBox.Enabled = false;
        _isConverting = true;
        _diagnosticsPending = false;
        _diagnosticsTimer.Stop();
        _diagnosticsVersion++;
        SetStatus("Converting...");

        try
        {
            ConversionResult result = await Task.Run(() => _converter.ConvertToSb3(inputPath, outputPath, options));
            if (result.Success)
            {
                string warningText = result.Issues.Count == 0
                    ? string.Empty
                    : Environment.NewLine + string.Join(Environment.NewLine, result.Issues.Select(Program.FormatIssue));
                SetStatus($"Wrote {result.OutputPath}{warningText}");
                return;
            }

            SetStatus(string.Join(Environment.NewLine, result.Issues.Select(Program.FormatIssue)));
        }
        finally
        {
            _convertButton.Enabled = true;
            _attemptRepairCheckBox.Enabled = true;
            _isConverting = false;
            if (_diagnosticsPending && _sourceEditor.TextLength > 0)
            {
                _diagnosticsTimer.Start();
            }
        }
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
            SetStatus(ex.Message);
        }
    }

    private void SetStatus(string text)
    {
        _displayedDiagnostics.Clear();
        _statusTextBox.Clear();
        _statusTextBox.SelectionColor = Color.Black;
        _statusTextBox.Text = text;
    }

    private void ShowDiagnostics(IReadOnlyList<CtsDiagnostic> diagnostics)
    {
        _displayedDiagnostics.Clear();
        _statusTextBox.Clear();
        if (diagnostics.Count == 0)
        {
            _statusTextBox.SelectionColor = Color.DarkGreen;
            _statusTextBox.AppendText("No Monocode diagnostics.");
            return;
        }

        foreach (CtsDiagnostic diagnostic in diagnostics)
        {
            int start = _statusTextBox.TextLength;
            string text = FormatDiagnostic(diagnostic);
            _statusTextBox.SelectionColor = diagnostic.Severity == DiagnosticSeverity.Error
                ? Color.Firebrick
                : Color.DarkGoldenrod;
            _statusTextBox.AppendText(text);
            _displayedDiagnostics.Add(new DiagnosticDisplaySpan(start, text.Length, diagnostic));
            _statusTextBox.AppendText(Environment.NewLine);
        }

        _statusTextBox.Select(0, 0);
    }

    private void InputPathTextBox_Leave(object? sender, EventArgs e)
    {
        LoadMonocodeSourceIfAvailable(TrimPath(_inputPathTextBox.Text));
    }

    private void SourceEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_isApplyingHighlight)
        {
            return;
        }

        ApplyMonocodeHighlighting();
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
            if (!_isConverting)
            {
                SetStatus("Ready.");
            }

            return;
        }

        if (!_isConverting)
        {
            _diagnosticsTimer.Start();
        }
    }

    private async void DiagnosticsTimer_Tick(object? sender, EventArgs e)
    {
        _diagnosticsTimer.Stop();
        if (_isConverting || _sourceEditor.TextLength == 0)
        {
            return;
        }

        _diagnosticsPending = false;
        int version = _diagnosticsVersion;
        string sourceText = _sourceEditor.Text;
        string inputPath = TrimPath(_inputPathTextBox.Text);
        string sourceName = IsMonocodePath(inputPath) ? inputPath : "editor.mono";
        IReadOnlyList<CtsDiagnostic> diagnostics = await Task.Run(
            () => CtsCompiler.Compile(sourceText, sourceName).Diagnostics);

        if (version != _diagnosticsVersion || _isConverting || IsDisposed)
        {
            return;
        }

        ShowDiagnostics(diagnostics);
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

        CtsDiagnostic diagnostic = display.Diagnostic;
        int start = CtsSourcePosition.GetOffset(_sourceEditor.Text, diagnostic.Span.Start);
        int end = CtsSourcePosition.GetOffset(_sourceEditor.Text, diagnostic.Span.End);
        _sourceEditor.Select(start, Math.Max(0, end - start));
        _sourceEditor.ScrollToCaret();
        _sourceEditor.Focus();
    }

    private void LoadMonocodeSourceIfAvailable(string inputPath)
    {
        if (!IsMonocodePath(inputPath) || !File.Exists(inputPath))
        {
            return;
        }

        try
        {
            _sourceEditor.Text = File.ReadAllText(inputPath);
            ApplyMonocodeHighlighting();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatus(ex.Message);
        }
    }

    private void ApplyMonocodeHighlighting()
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
            _sourceEditor.SelectionColor = Color.Black;

            foreach (CtsColorSpan span in CtsSyntaxClassifier.Classify(_sourceEditor.Text))
            {
                if (span.Start < 0 || span.Start + span.Length > _sourceEditor.TextLength)
                {
                    continue;
                }

                _sourceEditor.Select(span.Start, span.Length);
                _sourceEditor.SelectionColor = ColorTranslator.FromHtml(span.Color);
            }

            _sourceEditor.Select(Math.Min(selectionStart, _sourceEditor.TextLength), Math.Min(selectionLength, Math.Max(0, _sourceEditor.TextLength - selectionStart)));
        }
        finally
        {
            _sourceEditor.ResumeLayout();
            _isApplyingHighlight = false;
        }
    }

    private static string TrimPath(string path)
    {
        return path.Trim().Trim('"');
    }

    private static bool IsMonocodePath(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".mono", StringComparison.OrdinalIgnoreCase);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _diagnosticsTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed record DiagnosticDisplaySpan(int Start, int Length, CtsDiagnostic Diagnostic);
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LetterboxImageCropper;

public sealed class MainForm : Form
{
    private static readonly int WorkerCount = Math.Clamp(Environment.ProcessorCount, 1, 8);
    private static readonly DetectionSettings Defaults = new();

    // ---- Controls -------------------------------------------------------------------------
    private readonly TextBox _folderBox = new() { ReadOnly = true, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 3) };
    private readonly Button _browseButton = new() { Text = "Browse…", AutoSize = true };

    private readonly Label _summaryLabel = new() { AutoSize = true, Text = "No folder analyzed yet." };
    private readonly Label _warningLabel = new() { AutoSize = true, ForeColor = Color.Firebrick, Visible = false };

    private readonly ComboBox _groupCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly NumericUpDown _topNum = CropNum();
    private readonly NumericUpDown _bottomNum = CropNum();
    private readonly NumericUpDown _leftNum = CropNum();
    private readonly NumericUpDown _rightNum = CropNum();
    private readonly Label _frameLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(3, 8, 3, 3) };

    private readonly NumericUpDown _thresholdNum = Num(0, 254, Defaults.BlackThreshold);
    private readonly NumericUpDown _noiseNum = Num(0, 20, (decimal)(Defaults.NoiseAllowance * 100), decimals: 1, increment: 0.1m);
    private readonly CheckBox _symmetricCheck = new() { Text = "Force symmetric bars (top = bottom, left = right)", AutoSize = true, Checked = true };
    private readonly ComboBox _searchCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly NumericUpDown _trimNum = Num(0, 50, 0);

    private readonly ComboBox _formatCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly NumericUpDown _qualityNum = Num(1, 100, 95);
    private readonly CheckBox _chromaCheck = new() { Text = "4:4:4 chroma (no colour subsampling)", AutoSize = true };
    private readonly Label _outputLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private readonly Button _recalcButton = new() { Text = "Recalculate", AutoSize = true, MinimumSize = new Size(130, 34) };
    private readonly Button _confirmButton = new() { Text = "Confirm && Export", AutoSize = true, MinimumSize = new Size(150, 34), Enabled = false };

    private readonly PreviewPanel _preview = new() { Dock = DockStyle.Fill };
    private readonly Button _prevButton = new() { Text = "◀", Width = 40 };
    private readonly Button _nextButton = new() { Text = "▶", Width = 40 };
    private readonly CheckBox _croppedCheck = new() { Text = "Show cropped", AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
    private readonly Label _imageLabel = new() { AutoSize = true, Margin = new Padding(12, 7, 3, 3) };

    private readonly CheckBox _deviationsCheck = new() { Text = "Show deviations only", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(4, 4, 0, 2) };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
        HideSelection = false, GridLines = true,
    };

    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Ready." };
    private readonly ToolStripProgressBar _progressBar = new() { Visible = false, Width = 220 };
    private readonly ToolStripButton _cancelButton = new() { Text = "Cancel", Visible = false };

    private readonly System.Windows.Forms.Timer _progressTimer = new() { Interval = 100 };
    private readonly Control[] _settingsControls;

    // ---- State ----------------------------------------------------------------------------
    private readonly string? _initialFolder;
    private string? _folder;
    private List<ImageDetection>? _detections;
    private DetectionSettings? _detectedWith;
    private AnalysisResult? _analysis;
    private SizeGroup? _group;
    private List<ImageDetection> _listItems = new();
    private CancellationTokenSource? _cts;
    private int _progressCount;
    private string _progressText = "";
    private bool _busy;
    private bool _updating;
    private int _previewVersion;

    public MainForm(string? initialFolder = null)
    {
        _initialFolder = initialFolder;
        _settingsControls = new Control[]
        {
            _browseButton, _groupCombo, _topNum, _bottomNum, _leftNum, _rightNum, _thresholdNum, _noiseNum,
            _symmetricCheck, _searchCombo, _trimNum, _formatCombo, _qualityNum, _chromaCheck, _recalcButton,
        };
        BuildUi();
        WireEvents();
        UpdateFormatControls();
        UpdateActionState();
    }

    // =======================================================================================
    // UI construction
    // =======================================================================================

    private static NumericUpDown Num(decimal min, decimal max, decimal value, int decimals = 0, decimal increment = 1) => new()
    {
        Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), DecimalPlaces = decimals,
        Increment = increment, Width = 72, TextAlign = HorizontalAlignment.Right,
    };

    private static NumericUpDown CropNum() => Num(0, 100000, 0);

    private static TableLayoutPanel NewTable(int columns)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = columns, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, Margin = new Padding(0), GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        for (int i = 0; i < columns; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return table;
    }

    private static GroupBox NewGroup(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 6), Margin = new Padding(0, 0, 0, 8),
        };
        group.Controls.Add(content);
        return group;
    }

    private static Label NewLabel(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) };

    private static void AddSpanning(TableLayoutPanel table, Control control)
    {
        table.Controls.Add(control);
        table.SetColumnSpan(control, table.ColumnCount);
    }

    private void BuildUi()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        Text = AppInfo.Title;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1320, 840);
        MinimumSize = new Size(960, 640);
        AllowDrop = true;

        // ---- Folder row (top) ----
        var folderRow = NewTable(3);
        folderRow.Dock = DockStyle.Top;
        folderRow.Padding = new Padding(8, 8, 8, 4);
        folderRow.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100);
        folderRow.Controls.Add(NewLabel("Folder:"));
        folderRow.Controls.Add(_folderBox);
        folderRow.Controls.Add(_browseButton);

        // ---- Left settings column ----
        _summaryLabel.MaximumSize = new Size(330, 0);
        _warningLabel.MaximumSize = new Size(330, 0);
        var summary = NewTable(1);
        summary.Controls.Add(_summaryLabel);
        summary.Controls.Add(_warningLabel);

        var crop = NewTable(4);
        crop.Controls.Add(NewLabel("Resolution:"));
        crop.Controls.Add(_groupCombo);
        crop.SetColumnSpan(_groupCombo, 3);
        crop.Controls.Add(NewLabel("Top:"));
        crop.Controls.Add(_topNum);
        crop.Controls.Add(NewLabel("Bottom:"));
        crop.Controls.Add(_bottomNum);
        crop.Controls.Add(NewLabel("Left:"));
        crop.Controls.Add(_leftNum);
        crop.Controls.Add(NewLabel("Right:"));
        crop.Controls.Add(_rightNum);
        AddSpanning(crop, _frameLabel);

        var detection = NewTable(2);
        detection.Controls.Add(NewLabel("Black threshold (0–254):"));
        detection.Controls.Add(_thresholdNum);
        detection.Controls.Add(NewLabel("Noise allowance per line (%):"));
        detection.Controls.Add(_noiseNum);
        detection.Controls.Add(NewLabel("Extra trim per bar (px):"));
        detection.Controls.Add(_trimNum);
        AddSpanning(detection, NewLabel("Bars to detect:"));
        _searchCombo.Items.AddRange(new object[]
        {
            "Top/bottom or left/right (automatic)",
            "Top/bottom only (letterbox)",
            "Left/right only (pillarbox)",
        });
        _searchCombo.SelectedIndex = 0;
        AddSpanning(detection, _searchCombo);
        AddSpanning(detection, _symmetricCheck);

        _formatCombo.Items.AddRange(new object[] { "Same as source (recommended)", "JPEG (custom quality)", "PNG (lossless)" });
        _formatCombo.SelectedIndex = 0;
        var output = NewTable(2);
        output.Controls.Add(NewLabel("Format:"));
        output.Controls.Add(_formatCombo);
        output.Controls.Add(NewLabel("JPEG quality:"));
        output.Controls.Add(_qualityNum);
        AddSpanning(output, _chromaCheck);
        _outputLabel.MaximumSize = new Size(330, 0);
        AddSpanning(output, _outputLabel);

        // Action buttons stay pinned below the (scrollable) settings, so they are always visible.
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, WrapContents = false, Padding = new Padding(8, 6, 8, 8) };
        _confirmButton.Font = new Font(Font, FontStyle.Bold);
        buttons.Controls.Add(_recalcButton);
        buttons.Controls.Add(_confirmButton);

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1,
            // Right padding leaves room for the vertical scrollbar on small screens.
            Padding = new Padding(8, 4, 8 + SystemInformation.VerticalScrollBarWidth, 8), GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Vertical scrolling only (the standard WinForms trick to suppress the horizontal scrollbar).
        left.HorizontalScroll.Maximum = 0;
        left.HorizontalScroll.Visible = false;
        left.VerticalScroll.Visible = true;
        left.AutoScroll = true;
        left.Controls.Add(NewGroup("Analysis", summary));
        left.Controls.Add(NewGroup("Crop (pixels removed)", crop));
        left.Controls.Add(NewGroup("Detection settings", detection));
        left.Controls.Add(NewGroup("Output", output));

        var leftColumn = new Panel { Dock = DockStyle.Left, Width = 400 };
        leftColumn.Controls.Add(left);    // Fill - must be added first
        leftColumn.Controls.Add(buttons); // Bottom

        // ---- Right: preview + image list ----
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(4, 4, 4, 0) };
        nav.Controls.Add(_prevButton);
        nav.Controls.Add(_nextButton);
        nav.Controls.Add(_croppedCheck);
        nav.Controls.Add(_imageLabel);

        _list.Columns.Add("File", 260);
        _list.Columns.Add("Top", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Bottom", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Left", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Right", 60, HorizontalAlignment.Right);
        _list.Columns.Add("Status", 480);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
        split.Panel1.Controls.Add(_preview);
        split.Panel1.Controls.Add(nav);
        split.Panel2.Controls.Add(_list);
        split.Panel2.Controls.Add(_deviationsCheck);

        // ---- Status bar ----
        var status = new StatusStrip();
        status.Items.AddRange(new ToolStripItem[] { _statusLabel, _progressBar, _cancelButton });

        // Dock order: the Fill control must be added first.
        Controls.Add(split);
        Controls.Add(leftColumn);
        Controls.Add(folderRow);
        Controls.Add(status);

        ResumeLayout(false);
        PerformLayout();

        Shown += (_, _) =>
        {
            split.SplitterDistance = Math.Max(200, split.Height - LogicalToDeviceUnits(230));
            if (!string.IsNullOrEmpty(_initialFolder) && Directory.Exists(_initialFolder))
                _ = SetFolderAsync(_initialFolder);
        };
    }

    private void WireEvents()
    {
        _browseButton.Click += (_, _) => BrowseFolder();
        _recalcButton.Click += async (_, _) => await RunAnalysisAsync();
        _confirmButton.Click += async (_, _) => await RunExportAsync();
        _cancelButton.Click += (_, _) => _cts?.Cancel();

        _groupCombo.SelectedIndexChanged += (_, _) => { if (!_updating) OnGroupChanged(); };
        foreach (var num in new[] { _topNum, _bottomNum, _leftNum, _rightNum })
            num.ValueChanged += (_, _) => OnCropEdited();

        _symmetricCheck.CheckedChanged += (_, _) => Reaggregate();
        _searchCombo.SelectedIndexChanged += (_, _) => Reaggregate();
        _trimNum.ValueChanged += (_, _) => Reaggregate();
        _thresholdNum.ValueChanged += (_, _) => UpdateSummary();
        _noiseNum.ValueChanged += (_, _) => UpdateSummary();

        _formatCombo.SelectedIndexChanged += (_, _) => UpdateFormatControls();

        _prevButton.Click += (_, _) => Step(-1);
        _nextButton.Click += (_, _) => Step(+1);
        _croppedCheck.CheckedChanged += (_, _) => _preview.ShowCropped = _croppedCheck.Checked;
        _deviationsCheck.CheckedChanged += (_, _) => RefreshList();
        _list.SelectedIndexChanged += (_, _) => OnListSelectionChanged();

        _progressTimer.Tick += (_, _) => UpdateProgress();

        DragEnter += (_, e) =>
        {
            if (!_busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
            {
                string path = paths[0];
                string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (folder != null) await SetFolderAsync(folder);
            }
        };

        FormClosing += (_, _) => _cts?.Cancel();
    }

    // =======================================================================================
    // Folder selection and analysis
    // =======================================================================================

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder with the letterboxed screenshots",
            UseDescriptionForTitle = true,
            InitialDirectory = _folder ?? "",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _ = SetFolderAsync(dialog.SelectedPath);
    }

    private async Task SetFolderAsync(string folder)
    {
        if (_busy) return;
        folder = Path.GetFullPath(folder);
        _folder = folder;
        _folderBox.Text = folder;
        _outputLabel.Text = $"Saved to: {Path.Combine(folder, ImageFiles.OutputFolderName)}";
        await RunAnalysisAsync();
    }

    private DetectionSettings CurrentDetectionSettings() => new()
    {
        BlackThreshold = (int)_thresholdNum.Value,
        NoiseAllowance = (double)_noiseNum.Value / 100.0,
    };

    private AnalysisOptions CurrentAnalysisOptions() => new()
    {
        Detection = _detectedWith ?? CurrentDetectionSettings(),
        ForceSymmetric = _symmetricCheck.Checked,
        Search = _searchCombo.SelectedIndex switch { 1 => BarSearch.TopBottomOnly, 2 => BarSearch.LeftRightOnly, _ => BarSearch.Auto },
        ExtraTrim = (int)_trimNum.Value,
    };

    private async Task RunAnalysisAsync()
    {
        if (_busy || _folder is null) return;

        List<string> files;
        try
        {
            files = ImageFiles.Enumerate(_folder);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot read the folder:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (files.Count == 0)
        {
            ClearResults("No supported images (jpg, jpeg, png) found in this folder.");
            return;
        }

        DetectionSettings settings = CurrentDetectionSettings();
        var results = new ImageDetection[files.Count];
        CancellationToken token = BeginBusy($"Analyzing {files.Count} images", files.Count);
        var timer = Stopwatch.StartNew();
        try
        {
            await Task.Run(() => Parallel.For(0, files.Count,
                new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = WorkerCount },
                i =>
                {
                    results[i] = BarDetector.Detect(files[i], settings);
                    Interlocked.Increment(ref _progressCount);
                }), token);
        }
        catch (OperationCanceledException)
        {
            EndBusy("Analysis cancelled - previous results (if any) are still shown.");
            return;
        }

        EndBusy($"Analyzed {files.Count} images in {timer.Elapsed.TotalSeconds:0.0} s.");

        _detections = results.ToList();
        _detectedWith = settings;
        _group = null;

        // Pre-fill the custom JPEG quality with the most common quality found in the sources.
        int? quality = _detections.Where(d => d.Source.Jpeg?.Quality is not null)
            .GroupBy(d => d.Source.Jpeg!.Quality!.Value).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault();
        if (quality.HasValue) _qualityNum.Value = Math.Clamp(quality.Value, 1, 100);

        Reaggregate();
    }

    /// <summary>Re-runs the voting with the current options (cheap - no image decoding).</summary>
    private void Reaggregate()
    {
        if (_detections is null || _busy) return;

        (int, int)? previousSize = _group is null ? null : (_group.Width, _group.Height);
        _analysis = CropAnalyzer.Analyze(_detections, CurrentAnalysisOptions());

        _updating = true;
        _groupCombo.Items.Clear();
        foreach (SizeGroup g in _analysis.Groups)
            _groupCombo.Items.Add($"{g.SizeText}   ({g.Images.Count} image{(g.Images.Count == 1 ? "" : "s")})");
        int index = previousSize is null ? 0 : Math.Max(0, _analysis.Groups.ToList().FindIndex(g => (g.Width, g.Height) == previousSize));
        if (_analysis.Groups.Count > 0) _groupCombo.SelectedIndex = index;
        _updating = false;

        OnGroupChanged();
    }

    private void ClearResults(string message)
    {
        _detections = null;
        _analysis = null;
        _group = null;
        _groupCombo.Items.Clear();
        _list.Items.Clear();
        _listItems.Clear();
        _summaryLabel.Text = message;
        _warningLabel.Visible = false;
        _frameLabel.Text = "";
        _imageLabel.Text = "";
        _preview.SetImage(null, message);
        UpdateActionState();
    }

    // =======================================================================================
    // Group / crop handling
    // =======================================================================================

    private void OnGroupChanged()
    {
        _group = _analysis is { Groups.Count: > 0 } && _groupCombo.SelectedIndex >= 0
            ? _analysis.Groups[_groupCombo.SelectedIndex]
            : null;

        LoadCropControls();
        UpdateSummary();
        RefreshList();
        UpdateActionState();
    }

    private void LoadCropControls()
    {
        _updating = true;
        Bars crop = _group?.Crop ?? default;
        int h = _group?.Height ?? 0, w = _group?.Width ?? 0;
        SetNum(_topNum, crop.Top, Math.Max(0, h - 1));
        SetNum(_bottomNum, crop.Bottom, Math.Max(0, h - 1));
        SetNum(_leftNum, crop.Left, Math.Max(0, w - 1));
        SetNum(_rightNum, crop.Right, Math.Max(0, w - 1));
        _updating = false;
        UpdateFrameLabel();
        _preview.SetCrop(crop);

        static void SetNum(NumericUpDown num, int value, int max)
        {
            num.Maximum = Math.Max(max, value);
            num.Value = value;
        }
    }

    private void OnCropEdited()
    {
        if (_updating || _group is null) return;
        _group.Crop = new Bars((int)_topNum.Value, (int)_bottomNum.Value, (int)_leftNum.Value, (int)_rightNum.Value);
        UpdateFrameLabel();
        _preview.SetCrop(_group.Crop);
        // Statuses (and therefore eligibility) depend on the crop.
        UpdateSummary();
        UpdateListStatuses();
        UpdateActionState();
    }

    /// <summary>Re-evaluates statuses after a crop edit without reloading the preview image.</summary>
    private void UpdateListStatuses()
    {
        if (_deviationsCheck.Checked)
        {
            RefreshList(); // the set of listed images may change
            return;
        }

        _list.BeginUpdate();
        for (int i = 0; i < _listItems.Count && i < _list.Items.Count; i++)
        {
            ImageStatus status = StatusOf(_listItems[i]);
            _list.Items[i].SubItems[5].Text = StatusText(status, _listItems[i]);
            _list.Items[i].BackColor = StatusColor(status);
        }
        _list.EndUpdate();

        if (_list.SelectedIndices.Count > 0)
        {
            int index = _list.SelectedIndices[0];
            ImageDetection d = _listItems[index];
            _imageLabel.Text = $"{index + 1} / {_listItems.Count}    {d.FileName}    -    {StatusText(StatusOf(d), d)}";
        }
    }

    private void UpdateFrameLabel()
    {
        if (_group is null)
        {
            _frameLabel.Text = "";
            return;
        }

        Bars c = _group.Crop;
        if (!c.IsValidFor(_group.Width, _group.Height))
        {
            _frameLabel.ForeColor = Color.Firebrick;
            _frameLabel.Text = "Invalid crop - nothing would remain.";
            return;
        }

        int fw = c.FrameWidth(_group.Width), fh = c.FrameHeight(_group.Height);
        bool edited = c != _group.Suggested;
        _frameLabel.ForeColor = edited ? Color.DarkOrange : SystemColors.ControlText;
        _frameLabel.Text = $"Frame: {fw} × {fh}  ({fw / (double)fh:0.00}:1){(edited ? "  - edited" : "")}";
    }

    private void UpdateSummary()
    {
        if (_analysis is null)
        {
            _warningLabel.Visible = false;
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Images: {_analysis.TotalImages}    Resolutions: {_analysis.Groups.Count}" +
                      (_analysis.Unreadable.Count > 0 ? $"    Unreadable: {_analysis.Unreadable.Count}" : ""));

        var warnings = new List<string>();
        if (_group is not null)
        {
            SizeGroup g = _group;
            var formats = g.Images.GroupBy(i => i.Source.Describe()).OrderByDescending(x => x.Count()).ToList();
            string source = formats[0].Key + (formats.Count > 1 ? $" (+{formats.Count - 1} other encodings)" : "");
            Bars v = g.Voted;
            string Side(int value) => value > 0 ? $"{value} px" : "none";

            sb.AppendLine();
            sb.AppendLine($"Resolution {g.SizeText}: {g.Images.Count} images");
            sb.AppendLine($"Source: {source}");
            var parts = new List<string>();
            if (g.TopBottomConsidered) parts.Add($"top {Side(v.Top)}, bottom {Side(v.Bottom)}");
            if (g.LeftRightConsidered) parts.Add($"left {Side(v.Left)}, right {Side(v.Right)}");
            sb.AppendLine($"Detected: {g.BoxingKind}");
            sb.AppendLine($"Bars: {string.Join(", ", parts)}");
            int fw = g.Suggested.FrameWidth(g.Width), fh = g.Suggested.FrameHeight(g.Height);
            sb.AppendLine($"Resulting frame: {fw} × {fh}  ({fw / (double)fh:0.00}:1)");
            sb.AppendLine($"Images matching exactly: {g.MatchingImages} of {g.Images.Count}");
            sb.AppendLine($"Images with a clearly lit edge: {g.ConfidentImages}");

            var statuses = g.Images.Select(g.StatusOf).ToList();
            int eligible = statuses.Count(s => s.IsEligible());
            int content = statuses.Count(s => s == ImageStatus.ContentInBar);
            sb.AppendLine($"Eligible for export: {eligible} of {g.Images.Count}");
            var skipped = statuses.Where(s => !s.IsEligible()).GroupBy(s => s).OrderBy(x => x.Key)
                .Select(x => $"{x.Count()} {StatusShort(x.Key)}").ToList();
            sb.Append(skipped.Count > 0 ? $"Skipped: {string.Join("; ", skipped)}" : "Skipped: none");
            if (_analysis.Groups.Count > 1)
                sb.Append($"{Environment.NewLine}Eligible in all resolutions: {_analysis.EligibleCount} of {_analysis.TotalImages}");

            warnings.AddRange(g.Warnings);
            if (g.Crop == default)
                warnings.Add("No black bars detected with the current mode - nothing to crop for this resolution.");
            if (content > 0)
                warnings.Add($"{content} image(s) have picture or subtitles inside the area that would be cut - they will be skipped.");
        }

        if (_detectedWith is not null && _detectedWith != CurrentDetectionSettings())
            warnings.Add("Detection settings changed - press Recalculate to apply them.");

        _summaryLabel.Text = sb.ToString();
        _warningLabel.Text = string.Join(Environment.NewLine, warnings.Select(w => "⚠ " + w));
        _warningLabel.Visible = warnings.Count > 0;
        _recalcButton.Font = new Font(Font, _detectedWith is not null && _detectedWith != CurrentDetectionSettings() ? FontStyle.Bold : FontStyle.Regular);
    }

    // =======================================================================================
    // Image list and preview
    // =======================================================================================

    private static string StatusShort(ImageStatus status) => status switch
    {
        ImageStatus.Ok => "OK",
        ImageStatus.DarkScene => "OK - dark scene (bar looked thicker, cut area is black)",
        ImageStatus.NothingToCrop => "no black bars detected (nothing to crop)",
        ImageStatus.ContentInBar => "picture or subtitles inside the cut area",
        ImageStatus.AllDark => "entirely dark",
        ImageStatus.InvalidCrop => "invalid crop (nothing would remain)",
        ImageStatus.Unreadable => "unreadable",
        _ => status.ToString(),
    };

    private static string StatusText(ImageStatus status, ImageDetection d) => status switch
    {
        _ when status.IsEligible() => StatusShort(status),
        ImageStatus.Unreadable => "Skipped - unreadable: " + d.Error,
        _ => "Skipped - " + StatusShort(status),
    };

    private static Color StatusColor(ImageStatus status) => status switch
    {
        ImageStatus.Ok => SystemColors.Window,
        ImageStatus.DarkScene => Color.FromArgb(255, 248, 215),
        ImageStatus.ContentInBar or ImageStatus.InvalidCrop => Color.FromArgb(255, 220, 215),
        ImageStatus.Unreadable => Color.FromArgb(255, 190, 190),
        _ => Color.FromArgb(232, 232, 232), // NothingToCrop, AllDark
    };

    private ImageStatus StatusOf(ImageDetection d) =>
        !d.IsReadable ? ImageStatus.Unreadable : _group?.StatusOf(d) ?? ImageStatus.Ok;

    private void RefreshList()
    {
        string? selectedPath = _list.SelectedIndices.Count > 0 ? _listItems[_list.SelectedIndices[0]].Path : null;

        IEnumerable<ImageDetection> items = _group?.Images ?? Enumerable.Empty<ImageDetection>();
        if (_analysis is not null) items = items.Concat(_analysis.Unreadable);
        if (_deviationsCheck.Checked) items = items.Where(d => StatusOf(d) != ImageStatus.Ok);
        _listItems = items.ToList();

        bool tb = _group?.TopBottomConsidered ?? true, lr = _group?.LeftRightConsidered ?? true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (ImageDetection d in _listItems)
        {
            ImageStatus status = StatusOf(d);
            string Edge(EdgeInfo e, int dim) => !d.IsReadable ? "" : e.Size >= dim ? "all" : e.Size.ToString();
            var item = new ListViewItem(new[]
            {
                d.FileName,
                tb ? Edge(d.Top, d.Height) : "-", tb ? Edge(d.Bottom, d.Height) : "-",
                lr ? Edge(d.Left, d.Width) : "-", lr ? Edge(d.Right, d.Width) : "-",
                StatusText(status, d),
            })
            { BackColor = StatusColor(status) };
            _list.Items.Add(item);
        }
        _list.EndUpdate();

        int index = selectedPath is null ? -1 : _listItems.FindIndex(d => d.Path == selectedPath);
        if (index < 0 && _listItems.Count > 0) index = 0;
        if (index >= 0)
        {
            Select(index);
            ShowImage(index); // also refreshes the preview when the selection itself did not change
        }
        else
        {
            _imageLabel.Text = "";
            _preview.SetImage(null, _deviationsCheck.Checked ? "No deviating images." : "No images.");
        }
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _list.Items.Count) return;
        _updating = true;
        _list.Items[index].Selected = true;
        _list.Items[index].Focused = true;
        _list.EnsureVisible(index);
        _updating = false;
    }

    private void Step(int delta)
    {
        if (_listItems.Count == 0) return;
        int current = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
        int next = Math.Clamp(current + delta, 0, _listItems.Count - 1);
        Select(next);
        ShowImage(next);
    }

    private void OnListSelectionChanged()
    {
        if (_updating || _list.SelectedIndices.Count == 0) return;
        ShowImage(_list.SelectedIndices[0]);
    }

    private async void ShowImage(int index)
    {
        if (index < 0 || index >= _listItems.Count) return;
        ImageDetection d = _listItems[index];
        int version = ++_previewVersion;

        ImageStatus status = StatusOf(d);
        _imageLabel.Text = $"{index + 1} / {_listItems.Count}    {d.FileName}    -    {StatusText(status, d)}";
        _prevButton.Enabled = index > 0;
        _nextButton.Enabled = index < _listItems.Count - 1;

        if (!d.IsReadable)
        {
            _preview.SetImage(null, $"Cannot read {d.FileName}:\n{d.Error}");
            return;
        }

        PreviewImage? image = null;
        string? error = null;
        try
        {
            image = await Task.Run(() => PreviewImage.Load(d.Path));
        }
        catch (Exception ex)
        {
            error = $"Cannot display {d.FileName}:\n{ex.Message}";
        }

        if (version != _previewVersion || IsDisposed)
        {
            image?.Dispose();
            return;
        }

        _preview.SetCrop(_group?.Crop ?? default);
        _preview.SetImage(image, error);
    }

    // =======================================================================================
    // Export
    // =======================================================================================

    private ExportOptions CurrentExportOptions() => new(
        _formatCombo.SelectedIndex switch { 1 => OutputFormat.Jpeg, 2 => OutputFormat.Png, _ => OutputFormat.SameAsSource },
        (int)_qualityNum.Value,
        _chromaCheck.Checked);

    private void UpdateFormatControls()
    {
        bool customJpeg = _formatCombo.SelectedIndex == 1;
        _qualityNum.Enabled = customJpeg && !_busy;
        _chromaCheck.Enabled = customJpeg && !_busy;
    }

    private async Task RunExportAsync()
    {
        if (_busy || _analysis is null || _folder is null || _analysis.Groups.Count == 0) return;

        // Only eligible images (status OK / OK - dark scene) are processed.
        int eligibleTotal = _analysis.EligibleCount;
        if (eligibleTotal == 0)
        {
            MessageBox.Show(this, "No image is eligible for cropping - there is nothing to export.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var exportGroups = _analysis.Groups.Where(g => g.EligibleCount > 0).ToList();
        if (_analysis.Groups.Count > 1)
        {
            string lines = string.Join(Environment.NewLine, _analysis.Groups.Select(g => g.EligibleCount > 0
                ? $"  {g.SizeText}: {g.EligibleCount} of {g.Images.Count} → {g.Crop.FrameWidth(g.Width)} × {g.Crop.FrameHeight(g.Height)}"
                : $"  {g.SizeText}: 0 of {g.Images.Count} (nothing eligible - skipped)"));
            if (MessageBox.Show(this, $"The folder contains images of {_analysis.Groups.Count} sizes. Each size uses its own crop:\n\n{lines}\n\nContinue?",
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
        }

        ExportOptions options = CurrentExportOptions();
        string outDir = Path.Combine(_folder, ImageFiles.OutputFolderName);

        // Not-eligible images, by status, for the final report.
        var notEligible = _analysis.Groups.SelectMany(g => g.Images.Select(g.StatusOf))
            .Concat(_analysis.Unreadable.Select(_ => ImageStatus.Unreadable))
            .Where(s => !s.IsEligible())
            .GroupBy(s => s)
            .ToDictionary(x => x.Key, x => x.Count());

        // Build the job list; make target names unique (e.g. a.jpg + a.png both converted to PNG).
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobs = new List<(string Source, string Target, Bars Crop)>();
        foreach (SizeGroup g in exportGroups)
        {
            foreach (ImageDetection d in g.Images.Where(i => g.StatusOf(i).IsEligible()))
            {
                string name = ImageExporter.TargetFileName(d.Path, options);
                string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
                for (int k = 2; !usedNames.Add(name); k++) name = $"{stem}_{k}{ext}";
                jobs.Add((d.Path, Path.Combine(outDir, name), g.Crop));
            }
        }

        try
        {
            Directory.CreateDirectory(outDir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cannot create the output folder:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        bool overwrite = true;
        int existing = jobs.Count(j => File.Exists(j.Target));
        if (existing > 0)
        {
            var answer = MessageBox.Show(this,
                $"{existing} of {jobs.Count} files already exist in \"{ImageFiles.OutputFolderName}\".\n\n" +
                "Yes - overwrite them\nNo - skip existing files\nCancel - do nothing",
                Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            overwrite = answer == DialogResult.Yes;
        }

        int saved = 0, skipped = 0;
        var errors = new ConcurrentQueue<string>();
        bool cancelled = false;
        CancellationToken token = BeginBusy($"Exporting {jobs.Count} images", jobs.Count);
        var timer = Stopwatch.StartNew();
        try
        {
            await Task.Run(() => Parallel.ForEach(jobs,
                new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = WorkerCount },
                job =>
                {
                    try
                    {
                        if (!overwrite && File.Exists(job.Target))
                        {
                            Interlocked.Increment(ref skipped);
                        }
                        else
                        {
                            ImageExporter.Export(job.Source, job.Target, job.Crop, options);
                            Interlocked.Increment(ref saved);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue($"{Path.GetFileName(job.Source)}: {Codecs.ErrorMessage(ex)}");
                    }
                    Interlocked.Increment(ref _progressCount);
                }), token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        EndBusy(cancelled ? "Export cancelled." : $"Exported {saved} images in {timer.Elapsed.TotalSeconds:0.0} s.");

        var report = new StringBuilder();
        if (cancelled) report.AppendLine("Export was cancelled.\n");
        report.AppendLine($"Saved: {saved}");
        if (skipped > 0) report.AppendLine($"Skipped (already existed): {skipped}");
        if (!errors.IsEmpty) report.AppendLine($"Failed: {errors.Count}");
        if (notEligible.Count > 0)
        {
            report.AppendLine($"Not processed (not eligible): {notEligible.Values.Sum()}");
            foreach (var (status, count) in notEligible.OrderBy(x => x.Key))
                report.AppendLine($"   {count} × {StatusShort(status)}");
        }
        if (!errors.IsEmpty)
        {
            report.AppendLine();
            foreach (string e in errors.Take(10)) report.AppendLine(e);
            if (errors.Count > 10) report.AppendLine($"… and {errors.Count - 10} more");
        }
        report.AppendLine();
        report.Append($"Open \"{outDir}\"?");

        if (MessageBox.Show(this, report.ToString(), Text, MessageBoxButtons.YesNo,
                errors.IsEmpty ? MessageBoxIcon.Information : MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            Process.Start(new ProcessStartInfo(outDir) { UseShellExecute = true });
        }
    }

    // =======================================================================================
    // Busy state / progress
    // =======================================================================================

    private CancellationToken BeginBusy(string text, int total)
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        _progressCount = 0;
        _progressText = text;
        _progressBar.Maximum = Math.Max(1, total);
        _progressBar.Value = 0;
        _progressBar.Visible = true;
        _cancelButton.Visible = true;
        _statusLabel.Text = text + "…";
        _progressTimer.Start();
        UpdateActionState();
        return _cts.Token;
    }

    private void EndBusy(string text)
    {
        _progressTimer.Stop();
        _busy = false;
        _cts?.Dispose();
        _cts = null;
        _progressBar.Visible = false;
        _cancelButton.Visible = false;
        _statusLabel.Text = text;
        UpdateActionState();
    }

    private void UpdateProgress()
    {
        int done = Math.Min(Volatile.Read(ref _progressCount), _progressBar.Maximum);
        _progressBar.Value = done;
        _statusLabel.Text = $"{_progressText}… {done} / {_progressBar.Maximum}";
    }

    private void UpdateActionState()
    {
        foreach (Control c in _settingsControls) c.Enabled = !_busy;
        _recalcButton.Enabled = !_busy && _folder is not null;
        _groupCombo.Enabled = !_busy && _groupCombo.Items.Count > 1;
        foreach (var num in new[] { _topNum, _bottomNum, _leftNum, _rightNum })
            num.Enabled = !_busy && _group is not null;
        // Export is possible only when at least one image is eligible for cropping.
        int eligible = _analysis?.EligibleCount ?? 0;
        _confirmButton.Enabled = !_busy && eligible > 0;
        _confirmButton.Text = _analysis is null ? "Confirm && Export" : $"Confirm && Export ({eligible})";
        UpdateFormatControls();
    }
}

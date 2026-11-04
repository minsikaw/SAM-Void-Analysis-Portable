using System.Drawing;
using System.Windows.Forms;
using RFGAN.Batch;

namespace RFGAN.Desktop;

public sealed class MainForm : Form
{
    private readonly BatchImporter _importer = new();
    private readonly BatchProcessor _processor = new();
    private readonly List<BatchSample> _samples = new();
    private BatchSettings _settings = new();
    private CancellationTokenSource? _cts;
    private bool _configured;
    private bool _busy;

    private readonly DataGridView _grid = new();
    private readonly PictureBox _preview = new();
    private readonly Label _details = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly FlowLayoutPanel _toolbar = new();
    private readonly ComboBox _maskMode = new();
    private readonly NumericUpDown _threshold = new();
    private readonly CheckBox _bright = new();
    private readonly CheckBox _showMask = new();
    private readonly Button _stop = new();
    private readonly Button _run = new();

    private static readonly Color Navy = Color.FromArgb(19, 30, 45);
    private static readonly Color Teal = Color.FromArgb(11, 134, 139);
    private static readonly Color Background = Color.FromArgb(244, 247, 250);

    public MainForm()
    {
        Text = "RF GaN · SAM Batch Quantifier v1";
        MinimumSize = new Size(1080, 650);
        Size = new Size(1470, 880);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Background;
        AutoScaleMode = AutoScaleMode.Dpi;
        BuildUi();
        FormClosing += (_, e) =>
        {
            if (_busy) { e.Cancel = true; MessageBox.Show(this, "Cancel the batch before closing."); }
            else _preview.Image?.Dispose();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(0) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 83));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Navy };
        header.Controls.Add(new Label
        {
            Text = "RF GaN  /  SAM Batch Quantifier", Dock = DockStyle.Top,
            Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold),
            ForeColor = Color.White, Height = 45, Padding = new Padding(22, 7, 0, 0)
        });
        header.Controls.Add(new Label
        {
            Text = "Import → verify sample identity and mask → configure detector → quantify → review → export",
            Dock = DockStyle.Bottom, ForeColor = Color.FromArgb(173, 204, 216), Height = 31,
            Padding = new Padding(24, 0, 0, 9)
        });
        root.Controls.Add(header, 0, 0);

        var commandPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12, 4, 12, 1) };
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        commandPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        _toolbar.Dock = DockStyle.Fill; _toolbar.WrapContents = false; _toolbar.AutoScroll = true;
        commandPanel.Controls.Add(_toolbar, 0, 0);
        AddButton("Import images", (Func<Task>)(async () => await ImportImagesAsync()), 120);
        AddButton("Import ZGT", (Func<Task>)(async () => await ImportZgtAsync()), 115);
        AddButton("Assign mask", AssignMask, 110);
        AddButton("Confirm IDs", ConfirmIdentities, 111);
        AddButton("Detector parameters", () => { ConfigureDetector(); }, 168);
        AddButton("Preview ROI", (Func<Task>)(async () => await PreviewDetectionAsync()), 115);
        _run.Text = "Run batch"; _run.Width = 118; _run.Height = 32;
        _run.BackColor = Teal; _run.ForeColor = Color.White; _run.FlatStyle = FlatStyle.Flat;
        _run.Click += async (_, _) => await RunBatchAsync();
        _toolbar.Controls.Add(_run);
        AddButton("Review selected", (Func<Task>)(async () => await ReviewSelectedAsync()), 130);
        AddButton("Export results", ExportResults, 125);
        _stop.Text = "Cancel"; _stop.Width = 78; _stop.Height = 32; _stop.Enabled = false;
        _stop.Click += (_, _) => _cts?.Cancel();
        _toolbar.Controls.Add(_stop);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(3, 4, 0, 2) };
        controls.Controls.Add(NewLabel("Defect mask mode"));
        _maskMode.Width = 225; _maskMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _maskMode.Items.AddRange(new object[] { MaskMode.ExternalBinaryMask, MaskMode.ExperimentalImageThreshold });
        _maskMode.SelectedItem = _settings.MaskMode;
        controls.Controls.Add(_maskMode);
        controls.Controls.Add(NewLabel("Threshold"));
        _threshold.Minimum = 0; _threshold.Maximum = 255; _threshold.Value = _settings.ExploratoryThreshold;
        _threshold.Width = 75; controls.Controls.Add(_threshold);
        _bright.Text = "Bright = defect"; _bright.Checked = true; _bright.Width = 138;
        _bright.Padding = new Padding(4, 4, 0, 0); controls.Controls.Add(_bright);
        _showMask.Text = "Show mask overlay"; _showMask.Checked = true; _showMask.Width = 168;
        _showMask.Padding = new Padding(4, 4, 0, 0); controls.Controls.Add(_showMask);
        commandPanel.Controls.Add(controls, 0, 1);
        root.Controls.Add(commandPanel, 0, 1);
        _maskMode.SelectedIndexChanged += (_, _) =>
        {
            _settings.MaskMode = (MaskMode)_maskMode.SelectedItem!;
            UpdateMaskMode(); InvalidateMeasurements();
        };
        _threshold.ValueChanged += (_, _) => { _settings.ExploratoryThreshold = (byte)_threshold.Value; InvalidateMeasurements(); };
        _bright.CheckedChanged += (_, _) => { _settings.BrightPixelsAreDelamination = _bright.Checked; InvalidateMeasurements(); };
        _showMask.CheckedChanged += (_, _) => RefreshPreview();
        UpdateMaskMode();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            SplitterWidth = 7, BackColor = Background
        };
        Shown += (_, _) =>
        {
            split.Panel1MinSize = 430;
            split.Panel2MinSize = 330;
            split.SplitterDistance = Math.Clamp((int)(split.Width * 0.55), 430, Math.Max(430, split.Width - 340));
        };
        root.Controls.Add(split, 0, 2);
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 5, 8) };
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 4, 12, 8), RowCount = 3 };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(right);
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Color.White; _grid.BorderStyle = BorderStyle.None;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false; _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowTemplate.Height = 34; _grid.ColumnHeadersHeight = 42;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Navy;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.EnableHeadersVisualStyles = false;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(200, 231, 232);
        _grid.DefaultCellStyle.SelectionForeColor = Navy;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 249, 251);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SampleID", HeaderText = "Sample ID (editable)", Width = 145 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Image", HeaderText = "Image", Width = 175, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mask", HeaderText = "Mask", Width = 98, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Confirmed", HeaderText = "ID verified", Width = 90, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 112, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Delam", HeaderText = "Delam %", Width = 92, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Details", HeaderText = "Issues / notes", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 190, ReadOnly = true });
        left.Controls.Add(_grid);
        _grid.SelectionChanged += (_, _) => RefreshPreview();
        _grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var sample = (BatchSample)_grid.Rows[e.RowIndex].Tag!;
            sample.Identity = Convert.ToString(_grid.Rows[e.RowIndex].Cells[0].Value)?.Trim() ?? "";
            sample.IdentityConfirmed = false;
            InvalidateSample(sample); BatchInputValidator.ValidateImport(sample, _settings); RefreshRows();
        };
        right.Controls.Add(new Label
        {
            Text = "SAMPLE / ROI / DELAMINATION OVERLAY", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold), ForeColor = Navy
        }, 0, 0);
        _preview.Dock = DockStyle.Fill; _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BackColor = Color.FromArgb(27, 38, 52);
        right.Controls.Add(_preview, 0, 1);
        _details.Dock = DockStyle.Fill; _details.Padding = new Padding(7);
        _details.BackColor = Color.White; _details.ForeColor = Color.FromArgb(49, 66, 77);
        _details.Text = "Import SAM images or ZGT files to begin.\nBlue outline = detected die; green outline = TSV; red = defect mask inside sample ROI.";
        right.Controls.Add(_details, 0, 2);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(17, 6, 17, 7) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 77));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        _status.Text = "Ready · No SAM samples imported";
        _status.ForeColor = Color.FromArgb(69, 87, 101); _status.Dock = DockStyle.Fill;
        footer.Controls.Add(_status, 0, 0);
        _progress.Dock = DockStyle.Fill; _progress.Style = ProgressBarStyle.Continuous;
        footer.Controls.Add(_progress, 1, 0);
        root.Controls.Add(footer, 0, 3);
    }

    private static Label NewLabel(string text) => new()
    {
        Text = text, AutoSize = true, Padding = new Padding(12, 6, 4, 0), ForeColor = Navy
    };

    private void AddButton(string text, Action action, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 32, FlatStyle = FlatStyle.Standard, Margin = new Padding(2, 2, 5, 2) };
        button.Click += (_, _) => action();
        _toolbar.Controls.Add(button);
    }

    private void AddButton(string text, Func<Task> action, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 32, FlatStyle = FlatStyle.Standard, Margin = new Padding(2, 2, 5, 2) };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (OperationCanceledException) { SetStatus("Cancelled"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        _toolbar.Controls.Add(button);
    }

    private BatchSample? Selected()
        => _grid.CurrentRow?.Tag as BatchSample;

    private void SetStatus(string status) => _status.Text = status;

    private void BeginBusy()
    {
        _busy = true;
        _cts = new CancellationTokenSource();
        foreach (Control c in _toolbar.Controls) if (c != _stop) c.Enabled = false;
        _stop.Enabled = true; _grid.ReadOnly = true;
    }
    private void EndBusy()
    {
        _busy = false;
        _cts?.Dispose(); _cts = null;
        foreach (Control c in _toolbar.Controls) c.Enabled = true;
        _stop.Enabled = false; _grid.ReadOnly = false;
        foreach (DataGridViewColumn col in _grid.Columns) col.ReadOnly = col.Index != 0;
    }

    private async Task ImportImagesAsync()
    {
        using var chooser = new FolderBrowserDialog { Description = "Choose a folder containing C-scan images and optional delamination masks" };
        if (chooser.ShowDialog(this) != DialogResult.OK) return;
        BeginBusy();
        try
        {
            SetStatus("Inspecting images and matching sample identities...");
            var incoming = await Task.Run(() => _importer.ImportImages(chooser.SelectedPath, _cts!.Token));
            _samples.Clear(); _samples.AddRange(incoming);
            foreach (var sample in _samples) BatchInputValidator.ValidateImport(sample, _settings);
            RefreshRows();
            SetStatus($"Imported {incoming.Count} images · {incoming.Count(s => s.MaskPresent)} mask matches · Confirm IDs before verified export");
        }
        finally { EndBusy(); }
    }

    private async Task ImportZgtAsync()
    {
        using var chooser = new FolderBrowserDialog { Description = "Choose the folder containing .zgt files" };
        if (chooser.ShowDialog(this) != DialogResult.OK) return;
        using var options = new ZgtOptionsForm(_settings);
        if (options.ShowDialog(this) != DialogResult.OK) return;
        _settings.ZgtMode = options.Mode; _settings.GateStart = options.GateStart; _settings.GateEnd = options.GateEnd;
        string workspace = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RFGAN.BatchQuantifier", "Workspaces", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6]);
        BeginBusy();
        try
        {
            var progress = new Progress<(int current, int total, string name)>(p =>
            {
                _progress.Maximum = Math.Max(1, p.total); _progress.Value = Math.Min(p.current, _progress.Maximum);
                SetStatus($"Importing ZGT {p.current}/{p.total}: {p.name}");
            });
            var incoming = await Task.Run(() => _importer.ImportZgt(chooser.SelectedPath, workspace, _settings, progress, _cts!.Token));
            _samples.Clear(); _samples.AddRange(incoming);
            foreach (var sample in _samples) BatchInputValidator.ValidateImport(sample, _settings);
            RefreshRows();
            SetStatus($"Imported {incoming.Count} ZGT scans · validity masks generated · delamination masks are NOT generated from ZGT");
            MessageBox.Show(this,
                "ZGT conversion produced C-scan preview images and separate acquisition-validity masks.\n\nNo delamination labels were inferred from raw signals. Assign external defect masks, or select the explicitly EXPERIMENTAL intensity threshold mode for a demonstration.",
                "ZGT import completed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { EndBusy(); }
    }

    private void AssignMask()
    {
        var sample = Selected(); if (sample == null) return;
        using var dialog = new OpenFileDialog
        {
            Title = $"Assign binary delamination mask for {sample.Identity}",
            Filter = "Images|*.png;*.tif;*.tiff;*.bmp;*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        sample.MaskPath = dialog.FileName;
        sample.ImportError = null;
        InvalidateSample(sample); BatchInputValidator.ValidateImport(sample, _settings); RefreshRows();
        SetStatus($"Assigned mask to {sample.Identity}. Re-run to verify dimensions and binary values.");
    }

    private void ConfirmIdentities()
    {
        if (_samples.Count == 0) return;
        var ids = _samples.Select(s => s.Identity).ToList();
        var duplicates = ids.GroupBy(s => s, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicates.Length > 0 || ids.Any(s => string.IsNullOrWhiteSpace(s) || s == "UNIDENTIFIED"))
        {
            MessageBox.Show(this, "Missing or duplicate IDs: " + string.Join(", ", duplicates) + "\nEdit Sample ID cells first. No duplicate sample IDs are accepted in v1.", "Resolve sample IDs");
            return;
        }
        if (MessageBox.Show(this,
                $"Confirm that all {_samples.Count} displayed sample identities correctly correspond to their source files?\nYou are responsible for checking the filenames/positions.",
                "Confirm sample identities", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (var sample in _samples)
        {
            sample.IdentityConfirmed = true;
            if (sample.Result == null) BatchInputValidator.ValidateImport(sample, _settings);
        }
        RefreshRows();
        SetStatus($"Confirmed {_samples.Count} unique sample identities");
    }

    private bool ConfigureDetector()
    {
        using var dialog = new DetectorSettingsForm(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        _settings = dialog.UpdatedSettings;
        _configured = true;
        InvalidateMeasurements();
        SetStatus("Detector parameters updated · results invalidated · preview or run batch next");
        return true;
    }

    private bool EnsureDetectorConfigured() => _configured || ConfigureDetector();

    private async Task PreviewDetectionAsync()
    {
        var sample = Selected();
        if (sample == null || !File.Exists(sample.ImagePath)) return;
        if (!EnsureDetectorConfigured()) return;
        if (sample.ManualRoi.HasValue && MessageBox.Show(this,
            "Previewing automatic detection discards the existing manual ROI. Continue?",
            "Discard manual ROI?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        BeginBusy();
        try
        {
            SetStatus($"Detecting sample ROI for {sample.Identity}...");
            var result = await Task.Run(() => _processor.DetectOnly(sample, _settings, _cts!.Token));
            sample.Detection = result;
            sample.RoiApproved = false;
            sample.ManualRoi = null;
            sample.Result = null;
            sample.Message = result.Success
                ? $"ROI preview confidence {result.Confidence:P0}; review before trusting uncertain regions"
                : "Detection failed: " + result.FailureReason;
            sample.State = SampleState.NeedsReview;
            RefreshRows(); RefreshPreview();
            SetStatus($"{sample.Identity}: {(result.Success ? $"detected ROI ({result.Confidence:P0})" : "ROI detection failed")}");
        }
        finally { EndBusy(); }
    }

    private async Task RunBatchAsync()
    {
        if (_samples.Count == 0) { MessageBox.Show(this, "Import samples first."); return; }
        if (!EnsureDetectorConfigured()) return;
        var duplicateIds = _samples.GroupBy(s => s.Identity, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (duplicateIds.Count > 0)
        { MessageBox.Show(this, "Duplicate sample IDs: " + string.Join(", ", duplicateIds.Select(g => g.Key)) + ". Correct before batch processing."); return; }
        var frozenSettings = _settings.Clone();
        BeginBusy();
        _progress.Maximum = Math.Max(1, _samples.Count); _progress.Value = 0;
        try
        {
            for (int i = 0; i < _samples.Count; i++)
            {
                _cts!.Token.ThrowIfCancellationRequested();
                var sample = _samples[i];
                SetStatus($"Analysing {i + 1}/{_samples.Count}: {sample.Identity}");
                await Task.Run(() => _processor.ProcessOne(sample, frozenSettings, _cts.Token));
                _progress.Value = i + 1;
                RefreshRows();
            }
            SetStatus($"Batch complete · {_samples.Count(s => s.State == SampleState.Completed)} completed · {_samples.Count(s => s.State == SampleState.NeedsReview)} review · {_samples.Count(s => s.State is SampleState.Blocked or SampleState.Error)} blocked/error");
        }
        catch (OperationCanceledException) { SetStatus("Batch cancelled · completed rows retained, remaining rows unchanged"); }
        finally { EndBusy(); RefreshRows(); }
    }

    private async Task ReviewSelectedAsync()
    {
        BatchSample? sample = Selected(); if (sample == null) return;
        if (!File.Exists(sample.ImagePath)) { MessageBox.Show(this, "Image unavailable."); return; }
        if (sample.Detection == null && !sample.ManualRoi.HasValue)
        {
            if (!EnsureDetectorConfigured()) return;
            sample.Detection = await Task.Run(() => _processor.DetectOnly(sample, _settings));
        }
        using var dialog = new ReviewForm(sample, _settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        sample.ManualRoi = dialog.ApprovedManualRoi;
        sample.RoiApproved = true;
        sample.ApprovedCscanHash = FileIdentity.Sha256File(sample.ImagePath);
        sample.ApprovedRecipeHash = DetectionRecipeFactory.Create(_settings).RecipeHashSha256;
        BeginBusy();
        try
        {
            await Task.Run(() => _processor.ProcessOne(sample, _settings.Clone(), _cts!.Token));
            RefreshRows();
            SetStatus($"Review completed: {sample.Identity} → {sample.State} · {sample.Message}");
        }
        finally { EndBusy(); }
    }

    private void ExportResults()
    {
        if (_samples.Count == 0) return;
        int unresolved = _samples.Count(s => s.State != SampleState.Completed);
        if (unresolved > 0 && MessageBox.Show(this,
                $"{unresolved} samples are not completed. They will be exported with explicit Blocked/NeedsReview/Error statuses and no fabricated measurements. Continue?",
                "Incomplete batch", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        using var chooser = new FolderBrowserDialog { Description = "Select destination for results.csv and results.json" };
        if (chooser.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            BatchExporter.Export(chooser.SelectedPath, _samples, _settings);
            SetStatus("Exported CSV and JSON results to " + chooser.SelectedPath);
            MessageBox.Show(this, "Exported results.csv and results.json with measurement provenance and explicit sample statuses.", "Export finished");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void UpdateMaskMode()
    {
        bool enabled = _settings.MaskMode == MaskMode.ExperimentalImageThreshold;
        _threshold.Enabled = enabled; _bright.Enabled = enabled;
    }

    private void InvalidateSample(BatchSample sample)
    {
        sample.Result = null; sample.State = SampleState.Imported;
        sample.RoiApproved = false; sample.Message = "Settings/input changed; re-run required";
    }

    private void InvalidateMeasurements()
    {
        if (_busy) return;
        foreach (var sample in _samples)
        {
            sample.Result = null; sample.State = SampleState.Imported;
            sample.RoiApproved = false; sample.Detection = null;
            sample.Message = "Settings changed; re-run required";
            BatchInputValidator.ValidateImport(sample, _settings);
        }
        RefreshRows();
    }

    private void RefreshRows()
    {
        BatchSample? previous = Selected();
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var sample in _samples)
            {
                string maskStatus = sample.MaskPresent ? "Present" : "Missing";
                int index = _grid.Rows.Add(sample.Identity, Path.GetFileName(sample.ImagePath), maskStatus,
                    sample.IdentityConfirmed ? "Yes" : "No", sample.State.ToString(),
                    sample.Result?.DelaminationPercent.ToString("F2") ?? "—", sample.Message);
                var row = _grid.Rows[index]; row.Tag = sample;
                if (sample.State is SampleState.Blocked or SampleState.Error)
                    row.Cells[4].Style.ForeColor = Color.DarkRed;
                else if (sample.State == SampleState.NeedsReview)
                    row.Cells[4].Style.ForeColor = Color.DarkOrange;
                else if (sample.State == SampleState.Completed)
                    row.Cells[4].Style.ForeColor = Color.DarkGreen;
            }
            int selected = previous == null ? 0 : _samples.IndexOf(previous);
            if (_grid.Rows.Count > 0)
            {
                selected = Math.Clamp(selected, 0, _grid.Rows.Count - 1);
                _grid.ClearSelection(); _grid.Rows[selected].Selected = true;
                _grid.CurrentCell = _grid.Rows[selected].Cells[0];
            }
        }
        finally { _grid.ResumeLayout(); }
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_busy) return;
        BatchSample? sample = Selected();
        var old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        if (sample == null || !File.Exists(sample.ImagePath))
        { _details.Text = "No valid image selected."; return; }
        try
        {
            bool hasRoi = sample.ManualRoi.HasValue || sample.Detection?.Success == true;
            bool showMask = _showMask.Checked && hasRoi &&
                (_settings.MaskMode == MaskMode.ExperimentalImageThreshold || sample.MaskPresent);
            _preview.Image = PreviewRenderer.Render(sample, _settings, showMask);
            _details.Text = $"{sample.Identity} | {sample.State} | {sample.SourceFormat}\n" +
                $"ROI confidence: {(sample.Detection?.Confidence.ToString("P0") ?? (sample.ManualRoi.HasValue ? "Manual" : "Not detected"))}  ·  Mask: {(sample.MaskPresent ? "Present" : "Missing")}\n" +
                $"Coverage: {(sample.Result?.CoveragePercent.ToString("F1") ?? "—")}%  ·  Delamination: {(sample.Result?.DelaminationPercent.ToString("F2") ?? "—")}%\n" +
                (sample.Result?.Exploratory == true ? "EXPERIMENTAL threshold segmentation — not a calibrated defect measurement." : sample.Message);
        }
        catch (Exception ex)
        {
            _details.Text = "Preview unavailable: " + ex.Message;
            try { _preview.Image = PreviewRenderer.Render(sample, _settings, false); }
            catch { /* Import validation reports unavailable sources. */ }
        }
    }
}

using System.Drawing;
using System.Windows.Forms;
using RFGAN.Batch;
using RFGAN.SampleMapper.Services;

namespace RFGAN.Desktop;

public sealed class DetectorSettingsForm : Form
{
    private readonly BatchSettings _draft;
    private readonly PropertyGrid _detectorGrid = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
    private readonly PropertyGrid _geometryGrid = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly NumericUpDown _confidence = new() { Minimum = 0, Maximum = 100, DecimalPlaces = 0, Width = 75 };
    private readonly NumericUpDown _coverage = new() { Minimum = 0, Maximum = 100, DecimalPlaces = 0, Width = 75 };
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    public BatchSettings UpdatedSettings => _draft;

    public DetectorSettingsForm(BatchSettings active)
    {
        _draft = active.Clone();
        Text = "Detection parameters · Phase 4.3 original engine";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(650, 540);
        Size = new Size(785, 690);
        Font = new Font("Segoe UI", 9.5f);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Original Phase 4.3 classical detector parameters. Changes are applied as a snapshot to the next run.\nGeometry dimensions are fractions of the image and are optional; leave empty if uncalibrated.",
            ForeColor = Color.FromArgb(65, 74, 83), AutoSize = false
        }, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var detectorTab = new TabPage("Image processing + scoring");
        var geometryTab = new TabPage("Learned die / TSV geometry");
        detectorTab.Controls.Add(_detectorGrid);
        geometryTab.Controls.Add(_geometryGrid);
        tabs.TabPages.AddRange(new[] { detectorTab, geometryTab });
        _detectorGrid.SelectedObject = _draft.Detector;
        _geometryGrid.SelectedObject = _draft.Geometry;
        layout.Controls.Add(tabs, 0, 1);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        _type.Items.AddRange(Enum.GetValues<SampleType>().Cast<object>().ToArray());
        _type.SelectedItem = _draft.SampleType;
        _confidence.Value = (decimal)(100 * _draft.MinimumConfidence);
        _coverage.Value = (decimal)(100 * _draft.MinimumValidCoverage);
        controls.Controls.AddRange(new Control[]
        {
            new Label { Text = "Sample type", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _type,
            new Label { Text = "Min ROI confidence %", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, _confidence,
            new Label { Text = "Min acquisition coverage %", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, _coverage
        });
        layout.Controls.Add(controls, 0, 2);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "Apply settings", Width = 140, Height = 35, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", Width = 100, Height = 35, DialogResult = DialogResult.Cancel };
        var reset = new Button { Text = "Reset CV defaults", Width = 145, Height = 35 };
        reset.Click += (_, _) => { _draft.Detector = new SamAdvancedDetectionParams(); _draft.Geometry = new SamAdvancedLearnedGeometry(); _detectorGrid.SelectedObject = _draft.Detector; _geometryGrid.SelectedObject = _draft.Geometry; };
        ok.Click += (_, _) =>
        {
            _detectorGrid.Refresh();
            _draft.SampleType = (SampleType)_type.SelectedItem!;
            _draft.MinimumConfidence = (double)_confidence.Value / 100;
            _draft.MinimumValidCoverage = (double)_coverage.Value / 100;
            if (_draft.Detector.ClaheClipLimit <= 0 || _draft.Detector.ClaheTileSize < 1 ||
                _draft.Detector.BlurDivisor <= 0 || _draft.Detector.MorphDivisor <= 0 ||
                _draft.Detector.BrightMaskPercentile < 0 || _draft.Detector.BrightMaskPercentile > 100 ||
                _draft.Detector.ShowTopCandidates < 1)
            {
                MessageBox.Show(this, "Invalid detector parameters. CLAHE, blur and morphology values must be positive; brightness percentile must be 0–100.", "Check parameters");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        bar.Controls.AddRange(new Control[] { ok, cancel, reset });
        layout.Controls.Add(bar, 0, 3);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}

using System.Drawing;
using System.Windows.Forms;
using RFGAN.Batch;
using CvRect = OpenCvSharp.Rect;

namespace RFGAN.Desktop;

public sealed class ReviewForm : Form
{
    private readonly BatchSample _sample;
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(22, 29, 38) };
    private readonly Label _info = new() { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.FromArgb(60, 71, 84) };
    private System.Drawing.Point? _mouseStart;
    private System.Drawing.Point? _mouseEnd;
    private CvRect? _selected;
    public CvRect? ApprovedManualRoi { get; private set; }

    public ReviewForm(BatchSample sample, BatchSettings settings)
    {
        _sample = sample;
        _selected = sample.ManualRoi;
        Text = $"Review ROI and mask — {sample.Identity}";
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(680, 590);
        Size = new Size(990, 730);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        Controls.Add(layout);
        _info.Text = $"{sample.Identity} | {sample.State}\n{sample.Message}\nDrag a rectangle to override the sample ROI. Blue/cyan = automatic sample; yellow = manual selection; red = defect pixels within ROI. Only the chosen ROI is quantified.";
        layout.Controls.Add(_info, 0, 0);
        layout.Controls.Add(_picture, 0, 1);
        try
        {
            _picture.Image = PreviewRenderer.Render(sample, settings,
                sample.Detection?.Success == true || sample.ManualRoi.HasValue);
        }
        catch
        {
            try { _picture.Image = PreviewRenderer.Render(sample, settings, false); }
            catch (Exception ex) { _info.Text += "\nPreview error: " + ex.Message; }
        }
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var approve = new Button { Text = "Approve and reanalyse", Width = 180, Height = 36 };
        var cancel = new Button { Text = "Cancel", Width = 100, Height = 36, DialogResult = DialogResult.Cancel };
        var clear = new Button { Text = "Use detected ROI", Width = 165, Height = 36 };
        clear.Click += (_, _) => { _selected = null; _mouseStart = null; _mouseEnd = null; _picture.Invalidate(); };
        approve.Click += (_, _) =>
        {
            if (!_selected.HasValue && sample.Detection?.Success != true)
            { MessageBox.Show(this, "No valid automatic ROI. Draw a manual rectangle on the image.", "ROI required"); return; }
            ApprovedManualRoi = _selected;
            DialogResult = DialogResult.OK; Close();
        };
        footer.Controls.AddRange(new Control[] { approve, cancel, clear });
        layout.Controls.Add(footer, 0, 2);
        AcceptButton = approve; CancelButton = cancel;

        _picture.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && TryMap(e.Location, out var p))
            { _mouseStart = p; _mouseEnd = p; _picture.Invalidate(); }
        };
        _picture.MouseMove += (_, e) =>
        {
            if (_mouseStart.HasValue && (e.Button & MouseButtons.Left) != 0 && TryMap(e.Location, out var p))
            { _mouseEnd = p; _picture.Invalidate(); }
        };
        _picture.MouseUp += (_, e) =>
        {
            if (!_mouseStart.HasValue || !TryMap(e.Location, out var p)) return;
            _mouseEnd = p;
            int x1 = Math.Min(_mouseStart.Value.X, p.X), y1 = Math.Min(_mouseStart.Value.Y, p.Y);
            int w = Math.Abs(_mouseStart.Value.X - p.X), h = Math.Abs(_mouseStart.Value.Y - p.Y);
            if (w >= 3 && h >= 3) _selected = new CvRect(x1, y1, w, h);
            _mouseStart = null; _mouseEnd = null; _picture.Invalidate();
        };
        _picture.Paint += (_, e) =>
        {
            if (_picture.Image == null) return;
            var roi = _selected;
            if (_mouseStart.HasValue && _mouseEnd.HasValue)
            {
                var a = _mouseStart.Value; var b = _mouseEnd.Value;
                roi = new CvRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            }
            if (roi.HasValue && roi.Value.Width > 0 && roi.Value.Height > 0)
            {
                var v = roi.Value;
                var p0 = ToScreen(v.X, v.Y); var p1 = ToScreen(v.Right, v.Bottom);
                using var pen = new Pen(Color.Yellow, 2.5f);
                e.Graphics.DrawRectangle(pen, p0.X, p0.Y, p1.X - p0.X, p1.Y - p0.Y);
            }
        };
    }

    private float GetScale()
        => _picture.Image == null ? 1f : Math.Min(
            _picture.ClientSize.Width / (float)_picture.Image.Width,
            _picture.ClientSize.Height / (float)_picture.Image.Height);

    private System.Drawing.PointF ToScreen(int x, int y)
    {
        if (_picture.Image == null) return new System.Drawing.PointF();
        float scale = GetScale();
        float offsetX = (_picture.Width - _picture.Image.Width * scale) / 2;
        float offsetY = (_picture.Height - _picture.Image.Height * scale) / 2;
        return new System.Drawing.PointF(offsetX + x * scale, offsetY + y * scale);
    }

    private bool TryMap(System.Drawing.Point screen, out System.Drawing.Point image)
    {
        image = default;
        if (_picture.Image == null) return false;
        float scale = GetScale();
        var origin = ToScreen(0, 0);
        float ix = (screen.X - origin.X) / scale;
        float iy = (screen.Y - origin.Y) / scale;
        if (ix < 0 || iy < 0 || ix >= _picture.Image.Width || iy >= _picture.Image.Height) return false;
        image = new System.Drawing.Point((int)ix, (int)iy);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _picture.Image?.Dispose();
        base.Dispose(disposing);
    }
}

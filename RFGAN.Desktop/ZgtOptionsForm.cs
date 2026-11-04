using System.Drawing;
using System.Windows.Forms;
using RFGAN.Batch;
using RFGAN.SampleMapper.Services;

namespace RFGAN.Desktop;

public sealed class ZgtOptionsForm : Form
{
    private readonly ComboBox _metric = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly NumericUpDown _start = new() { Minimum = 0, Maximum = 10000000, Width = 160 };
    private readonly NumericUpDown _end = new() { Minimum = 1, Maximum = 10000000, Width = 160 };
    public ZgtCscanMode Mode => (ZgtCscanMode)_metric.SelectedItem!;
    public int GateStart => (int)_start.Value;
    public int GateEnd => (int)_end.Value;

    public ZgtOptionsForm(BatchSettings settings)
    {
        Text = "ZGT C-scan conversion";
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(470, 280);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 5 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int h in new[] { 60, 40, 40, 40, 60 }) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        Controls.Add(panel);
        var info = new Label { Text = "ZGT contains A-scan waveforms. Choose a gate and C-scan metric; the displayed PNG is normalized per scan for visualization.", Dock = DockStyle.Fill };
        panel.Controls.Add(info, 0, 0); panel.SetColumnSpan(info, 2);
        _metric.Items.AddRange(Enum.GetValues<ZgtCscanMode>().Cast<object>().ToArray());
        _metric.SelectedItem = settings.ZgtMode;
        _start.Value = settings.GateStart;
        _end.Value = Math.Max(settings.GateEnd, settings.GateStart + 1);
        AddRow("C-scan metric", _metric, 1);
        AddRow("Gate start (sample)", _start, 2);
        AddRow("Gate end (exclusive)", _end, 3);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var ok = new Button { Text = "Import ZGT batch", Width = 135, Height = 32 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 95, Height = 32 };
        ok.Click += (_, _) =>
        {
            if (_start.Value >= _end.Value && Mode != ZgtCscanMode.RuntimeMap)
            { MessageBox.Show(this, "Gate end must be greater than gate start."); return; }
            DialogResult = DialogResult.OK; Close();
        };
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        panel.Controls.Add(buttons, 0, 4); panel.SetColumnSpan(buttons, 2);
        AcceptButton = ok; CancelButton = cancel;

        void AddRow(string label, Control control, int row)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, 0, row);
            panel.Controls.Add(control, 1, row);
        }
    }
}

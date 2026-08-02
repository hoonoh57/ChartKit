namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private void ApplyToolbarStyle()
    {
        _toolbar.BackColor = Color.FromArgb(239, 243, 236);
        _toolbar.ForeColor = Color.FromArgb(30, 35, 40);
        _toolbar.RenderMode = ToolStripRenderMode.Professional;
        _toolbar.Renderer = new ChartToolbarRenderer();
        _toolbar.Padding = new Padding(6, 4, 6, 4);
        _toolbar.Height = 38;
        _toolbar.Font = new Font("Segoe UI", 9f, FontStyle.Regular);

        StyleEditor(_dataSymbolEditor, 112, HorizontalAlignment.Left);
        StyleEditor(_historyCountEditor, 64, HorizontalAlignment.Right);
        StyleEditor(_displayCountEditor, 58, HorizontalAlignment.Right);

        _symbolHistoryButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _symbolHistoryButton.AutoSize = false;
        _symbolHistoryButton.Width = 26;
        _symbolHistoryButton.Margin = new Padding(0, 1, 5, 1);

        _dataTimeframeEditor.AutoSize = false;
        _dataTimeframeEditor.Width = 64;
        _dataTimeframeEditor.Margin = new Padding(3, 1, 8, 1);
        _dataTimeframeEditor.TextAlign = ContentAlignment.MiddleCenter;

        _reloadDataButton.AutoSize = false;
        _reloadDataButton.Width = 52;
        _reloadDataButton.Margin = new Padding(3, 1, 8, 1);
        _reloadDataButton.Font = new Font(_toolbar.Font, FontStyle.Bold);
        _reloadDataButton.ToolTipText = "입력한 종목·주기·총 봉수로 다시 조회";

        _symbolNameLabel.Width = 130;
        _symbolNameLabel.Margin = new Padding(3, 1, 8, 1);
        _countLabel.Width = 132;
        _countLabel.Margin = new Padding(4, 1, 8, 1);

        _dateButton.Margin = new Padding(2, 1, 2, 1);
        _infoButton.Margin = new Padding(2, 1, 2, 1);
        _toolsButton.Margin = new Padding(2, 1, 2, 1);
    }

    private static void StyleEditor(
        ToolStripTextBox editor,
        int width,
        HorizontalAlignment alignment)
    {
        editor.AutoSize = false;
        editor.Width = width;
        editor.Margin = new Padding(3, 1, 3, 1);
        editor.TextBox.BorderStyle = BorderStyle.FixedSingle;
        editor.TextBox.TextAlign = alignment;
        editor.TextBox.BackColor = Color.White;
        editor.TextBox.ForeColor = Color.FromArgb(20, 25, 30);
    }

    private sealed class ChartToolbarRenderer : ToolStripProfessionalRenderer
    {
        public ChartToolbarRenderer()
            : base(new ChartToolbarColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(
            ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(190, 198, 190));
            e.Graphics.DrawLine(
                pen,
                e.AffectedBounds.Left,
                e.AffectedBounds.Bottom - 1,
                e.AffectedBounds.Right,
                e.AffectedBounds.Bottom - 1);
        }
    }

    private sealed class ChartToolbarColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Color.FromArgb(239, 243, 236);
        public override Color ToolStripGradientMiddle => Color.FromArgb(239, 243, 236);
        public override Color ToolStripGradientEnd => Color.FromArgb(239, 243, 236);
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(220, 232, 218);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(210, 224, 208);
        public override Color ButtonPressedGradientBegin => Color.FromArgb(197, 217, 194);
        public override Color ButtonPressedGradientEnd => Color.FromArgb(184, 207, 181);
        public override Color MenuItemSelected => Color.FromArgb(220, 232, 218);
        public override Color MenuItemBorder => Color.FromArgb(150, 170, 148);
        public override Color SeparatorDark => Color.FromArgb(185, 193, 185);
        public override Color SeparatorLight => Color.White;
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Transkript;

public sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _notify;

    public event Action? ExitRequested;
    public event Action? SettingsRequested;
    public event Action? HistoryRequested;
    public event Action? UpdateRequested;

    public TrayManager()
    {
        var menu = new ContextMenuStrip
        {
            Renderer        = new CleanMenuRenderer(),
            ShowImageMargin = false,
            Font            = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Padding         = new Padding(0, 4, 0, 4),
            BackColor       = Color.White,
        };

        // Header (app name — disabled)
        var header = new ToolStripLabel("Transkript")
        {
            Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(13, 13, 13),
            Enabled   = false,
            Margin    = new Padding(8, 2, 0, 2),
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(MakeItem("Paramètres",  () => SettingsRequested?.Invoke()));
        menu.Items.Add(MakeItem("Historique",  () => HistoryRequested?.Invoke()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MakeItem("Quitter",     () => ExitRequested?.Invoke(),  danger: true));

        _notify = new NotifyIcon
        {
            Text             = "Transkript",
            Icon             = LoadIcon(),
            Visible          = true,
            ContextMenuStrip = menu,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ToolStripMenuItem MakeItem(string text, Action action, bool danger = false)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = danger
                ? Color.FromArgb(200, 50, 50)
                : Color.FromArgb(13, 13, 13),
            Margin = new Padding(4, 1, 4, 1),
        };
        item.Click += (_, _) => action();
        return item;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetStatus(string status)
    {
        string text = $"Transkript — {status}";
        _notify.Text = text.Length > 127 ? text[..127] : text;
    }

    public void ShowBalloon(string title, string message)
        => _notify.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);

    /// <summary>
    /// Insère un item "Mise à jour disponible" en haut du menu et affiche une balloon.
    /// </summary>
    public void ShowUpdateAvailable(string version)
    {
        // Balloon cliquable
        _notify.ShowBalloonTip(
            6000,
            "Mise à jour disponible",
            $"Transkript {version} est disponible. Cliquez pour installer.",
            ToolTipIcon.Info);

        _notify.BalloonTipClicked += OnBalloonClicked;

        // Item en haut du menu
        var item = new ToolStripMenuItem($"⬆  Transkript {version} disponible")
        {
            ForeColor = Color.FromArgb(13, 100, 200),
            Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Margin    = new Padding(4, 1, 4, 1),
        };
        item.Click += (_, _) => UpdateRequested?.Invoke();

        _notify.ContextMenuStrip!.Items.Insert(0, item);
        _notify.ContextMenuStrip!.Items.Insert(1, new ToolStripSeparator());
    }

    private void OnBalloonClicked(object? sender, EventArgs e)
    {
        _notify.BalloonTipClicked -= OnBalloonClicked;
        UpdateRequested?.Invoke();
    }

    // ── Icon ──────────────────────────────────────────────────────────────────

    private static Icon LoadIcon()
    {
        string icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(icoPath))
            return new Icon(icoPath, 32, 32);

        using var bmp = new Bitmap(32, 32);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(13, 13, 13));
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
    }

    // ── Custom renderer ───────────────────────────────────────────────────────

    private sealed class CleanMenuRenderer : ToolStripProfessionalRenderer
    {
        public CleanMenuRenderer() : base(new CleanColorTable()) { }

        // Background de chaque item au survol
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;

            using var brush = new SolidBrush(Color.FromArgb(243, 243, 243));
            var rect = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(rect, 6);
            e.Graphics.FillPath(brush, path);
        }

        // Séparateur épuré
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(Color.FromArgb(235, 235, 235));
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        // Pas d'icône margin (trait vertical gris par défaut)
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        // Pas de coche / flèche par défaut
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(r.Right - radius * 2, r.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(r.X, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class CleanColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground  => Color.White;
        public override Color MenuBorder                   => Color.FromArgb(225, 225, 225);
        public override Color MenuItemBorder               => Color.Transparent;
        public override Color MenuItemSelected             => Color.Transparent;
        public override Color MenuItemSelectedGradientBegin => Color.Transparent;
        public override Color MenuItemSelectedGradientEnd   => Color.Transparent;
        public override Color MenuItemPressedGradientBegin  => Color.FromArgb(235, 235, 235);
        public override Color MenuItemPressedGradientEnd    => Color.FromArgb(235, 235, 235);
        public override Color ImageMarginGradientBegin      => Color.White;
        public override Color ImageMarginGradientMiddle     => Color.White;
        public override Color ImageMarginGradientEnd        => Color.White;
        public override Color SeparatorDark                 => Color.FromArgb(235, 235, 235);
        public override Color SeparatorLight                => Color.Transparent;
    }
}

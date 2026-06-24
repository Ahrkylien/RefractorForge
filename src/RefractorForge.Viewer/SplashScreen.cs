using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RefractorForge.Viewer;

/// <summary>
/// Splash + startup screen, both running in the same borderless WinForms window on a dedicated STA thread.
/// Phase 1 (splash): shows the branding image while the GL window loads in the background.
/// Phase 2 (startup): transitions in-place to the project picker. The splash image persists as the form
/// BackgroundImage so it is visible through semi-transparent panels throughout.
/// The GL window stays hidden until the user confirms a project choice.
/// </summary>
internal static class SplashScreen
{
    // ── Public result types ────────────────────────────────────────────
    public enum StartupAction { OpenProject, NewMap, OpenLevelRfa, OpenLevelFolder }

    public sealed class StartupResult
    {
        public required StartupAction Action;
        public string? ProjectPath;        // .rfproj path; set for OpenProject (recent click or file-picker)
        public string[]? LevelArchives;    // set for OpenLevelRfa
        public string? LevelFolder;        // set for OpenLevelFolder
    }

    // ── Private helpers ────────────────────────────────────────────────

    /// <summary>
    /// Form subclass that composites the entire window in one double-buffered pass.
    /// WS_EX_COMPOSITED eliminates the cascade-repaint flicker that transparent child controls cause.
    /// </summary>
    private sealed class SplashForm : Form
    {
        public SplashForm()
        {
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint,
                true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }
    }

    /// <summary>
    /// Panel that supports true transparent backgrounds: propagates the parent BackgroundImage
    /// through the WinForms transparency chain without triggering cascade repaints.
    /// </summary>
    private sealed class GlassPanel : Panel
    {
        public GlassPanel() => SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.OptimizedDoubleBuffer        |
            ControlStyles.AllPaintingInWmPaint,
            true);
    }

    // ── Private state ──────────────────────────────────────────────────
    private static Form?   _form;
    private static Thread? _thread;
    private static int     _shownTick;
    private static volatile bool _startupPhase;
    private static System.Windows.Forms.Timer? _safetyTimer;

    private static volatile StartupResult? _result;
    public  static StartupResult? PollResult() => _result;

    public static bool IsShowing => _form is not null && !_form.IsDisposed;

    // ── Phase 1: splash ────────────────────────────────────────────────
    public static void Show()
    {
        try
        {
            string img = System.IO.Path.Combine(AppContext.BaseDirectory, "refractorforgesplash.png");
            if (!System.IO.File.Exists(img)) return;
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                try
                {
                    _form = Build(img);
                    _form.Shown += (_, _) => { _shownTick = Environment.TickCount; ready.Set(); };
                    Application.Run(_form);
                }
                catch { ready.Set(); }
            }) { IsBackground = true, Name = "splash" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait(2000);
        }
        catch { }
    }

    /// <summary>Block until the splash has been on screen for at least <paramref name="ms"/> ms.</summary>
    public static void WaitVisibleFor(int ms)
    {
        if (_form is null || _shownTick == 0) return;
        int elapsed = Environment.TickCount - _shownTick;
        if (elapsed < ms) Thread.Sleep(ms - elapsed);
    }

    /// <summary>Close the window immediately (used when the GL editor is ready without a startup screen).</summary>
    public static void Close()
    {
        try
        {
            var f = _form;
            if (f is not null && !f.IsDisposed)
                f.BeginInvoke(new Action(() => { try { f.Close(); } catch { } }));
        }
        catch { }
    }

    // ── Phase 2: project picker ────────────────────────────────────────
    /// <summary>
    /// Transition the splash window in-place to the project picker.
    /// Called from the GL thread after OnLoad completes; executes the UI work on the STA thread.
    /// </summary>
    public static void SwitchToStartup(string[] recentProjPaths)
    {
        var items = recentProjPaths
            .Where(System.IO.File.Exists)
            .Select(p =>
            {
                var proj = ProjectFile.Load(p);
                return (
                    name: proj?.Name ?? System.IO.Path.GetFileNameWithoutExtension(p),
                    game: (proj?.Game ?? "").Contains("1942", StringComparison.OrdinalIgnoreCase) ? "BF1942" : "BFVietnam",
                    path: p
                );
            })
            .ToArray();

        var f = _form;
        if (f is null || f.IsDisposed) return;
        f.BeginInvoke(new Action(() => BuildStartupUI(f, items)));
    }

    // ── Shared top-area branding ──────────────────────────────────────
    // Called from form.Paint (phase 1) and hdr.Paint (phase 2) at the same local
    // coordinates, so neither text moves between phases.
    private static void PaintTopBranding(Graphics g, int formWidth)
    {
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        void Shadow(string s, Font f, Brush b, float x, float y)
        {
            using var sh = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            g.DrawString(s, f, sh, x + 1.5f, y + 1.5f);
            g.DrawString(s, f, b,  x,        y);
        }

        using var titleFont    = new Font("Segoe UI", 20f, FontStyle.Bold,    GraphicsUnit.Point);
        using var subtitleFont = new Font("Segoe UI",  9f, FontStyle.Regular, GraphicsUnit.Point);
        using var creditFont   = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);

        var amber      = Color.FromArgb(0xFF, 0xCA, 0x3A);
        var slate      = Color.FromArgb(0xE8, 0xEE, 0xF4);
        var creditCol  = Color.FromArgb(255, 240, 244, 250);

        // Centre the left-side block (title + subtitle) vertically in a 76 px header.
        var titleSz    = g.MeasureString("RefractorForge", titleFont);
        var subtitleSz = g.MeasureString("BF1942 / BFVietnam Map Editor", subtitleFont);
        const float gap     = 4f;
        const float leftX   = 22f;
        const float hdrH    = 76f;
        float blockH  = titleSz.Height + gap + subtitleSz.Height;
        float titleY  = (hdrH - blockH) / 2f;
        float subY    = titleY + titleSz.Height + gap;

        Shadow("RefractorForge",               titleFont,    new SolidBrush(amber),     leftX,      titleY);
        Shadow("BF1942 / BFVietnam Map Editor", subtitleFont, new SolidBrush(slate),     leftX + 3f, subY);

        // "developed by" right-aligned and vertically centred to the title line only.
        const string credit = "developed by Lucas Ludwiczak";
        var creditSz  = g.MeasureString(credit, creditFont);
        float creditY = titleY + (titleSz.Height - creditSz.Height) / 2f;
        Shadow(credit, creditFont, new SolidBrush(creditCol), formWidth - creditSz.Width - 14f, creditY);
    }

    // ── UI building ────────────────────────────────────────────────────
    private static Form Build(string imgPath)
    {
        var src = Image.FromFile(imgPath);
        int w = Math.Min(820, src.Width);
        int h = (int)(src.Height * (w / (float)src.Width));

        var form = new SplashForm
        {
            FormBorderStyle       = FormBorderStyle.None,
            StartPosition         = FormStartPosition.CenterScreen,
            ShowInTaskbar         = true,
            Width                 = w,
            Height                = h,
            BackColor             = Color.Black,
            BackgroundImage       = src,
            BackgroundImageLayout = ImageLayout.Stretch,
        };
        try { var ico = System.IO.Path.Combine(AppContext.BaseDirectory, "RefractorForge.ico"); if (System.IO.File.Exists(ico)) form.Icon = new Icon(ico); } catch { }

        form.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            if (!_startupPhase)
            {
                // Phase 1: bottom gradient + border.
                int band = Math.Max(96, h / 4);
                using (var grad = new LinearGradientBrush(
                           new Rectangle(0, h - band - 1, w, band + 2),
                           Color.FromArgb(0,   0, 0, 0),
                           Color.FromArgb(205, 0, 0, 0),
                           LinearGradientMode.Vertical))
                {
                    grad.WrapMode = WrapMode.TileFlipXY;
                    g.FillRectangle(grad, new Rectangle(0, h - band, w, band));
                }
                using var border = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
                g.DrawRectangle(border, 0, 0, w - 1, h - 1);

                // Phase 1: branding at the top — same coordinates the header uses in phase 2.
                PaintTopBranding(g, w);
            }
            // Phase 2: branding is painted by the header panel AFTER its own overlay.
        };

        _safetyTimer = new System.Windows.Forms.Timer { Interval = 12000 };
        _safetyTimer.Tick += (_, _) => { _safetyTimer.Stop(); try { form.Close(); } catch { } };
        _safetyTimer.Start();
        form.FormClosed += (_, _) =>
        {
            try { _safetyTimer?.Dispose(); _safetyTimer = null; } catch { }
        };
        return form;
    }

    private static void BuildStartupUI(Form form, (string name, string game, string path)[] items)
    {
        _startupPhase = true;
        try { _safetyTimer?.Stop(); } catch { }

        var bgNav   = Color.FromArgb(0x1E, 0x2A, 0x38);
        var bgCard  = Color.FromArgb(0x26, 0x33, 0x44);
        var amber   = Color.FromArgb(0xFF, 0xCA, 0x3A);
        var slate   = Color.FromArgb(0xAD, 0xBB, 0xCC);
        var divider = Color.FromArgb(0x3A, 0x4E, 0x62);
        var dimText = Color.FromArgb(0x55, 0x65, 0x75);

        int w = form.ClientSize.Width;
        int h = form.ClientSize.Height;

        form.Controls.Clear();
        form.BackColor = bgNav;   // fallback colour if the BackgroundImage is missing

        const int hdrH  = 76;
        const int footH = 62;
        int       listH = h - hdrH - footH;

        // ── Header — transparent so the splash image shows through ───────
        var hdr = new GlassPanel { Location = new Point(0, 0), Size = new Size(w, hdrH), BackColor = Color.Transparent };
        hdr.Paint += (_, e) =>
        {
            var g = e.Graphics;
            // Overlay gradient first, then branding on top so it is never dimmed.
            using (var grad = new LinearGradientBrush(new Rectangle(0, 0, w, hdrH),
                       Color.FromArgb(150, 0x08, 0x10, 0x18),
                       Color.FromArgb(80,  0x08, 0x10, 0x18),
                       LinearGradientMode.Vertical))
                g.FillRectangle(grad, 0, 0, w, hdrH);

            // All three branding strings — same positions as phase 1.
            PaintTopBranding(g, w);

            using var pen = new Pen(divider, 1f);
            g.DrawLine(pen, 0, hdrH - 1, w, hdrH - 1);
        };
        form.Controls.Add(hdr);

        // ── List body — semi-transparent card tint over the splash image ──
        var listPanel = new GlassPanel
        {
            Location   = new Point(0, hdrH),
            Size       = new Size(w, listH),
            BackColor  = Color.Transparent,
            AutoScroll = false,
        };
        listPanel.Paint += (_, e) =>
        {
            using var b = new SolidBrush(Color.FromArgb(195, bgCard));
            e.Graphics.FillRectangle(b, e.ClipRectangle);
        };

        listPanel.Controls.Add(new Label
        {
            Text      = "Recent Projects",
            ForeColor = slate,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(24, 14),
            Font      = new Font("Segoe UI", 9f),
        });
        listPanel.Controls.Add(new Panel
        {
            Location  = new Point(24, 36),
            Size      = new Size(w - 48, 1),
            BackColor = divider,
        });

        if (items.Length == 0)
        {
            listPanel.Controls.Add(new Label
            {
                Text      = "(no recent projects)",
                ForeColor = dimText,
                BackColor = Color.Transparent,
                AutoSize  = true,
                Location  = new Point(28, 52),
                Font      = new Font("Segoe UI", 9f),
            });
        }
        else
        {
            int rowY = 44;
            foreach (var (name, game, path) in items)
            {
                var row = MakeRow(form, w, name, game, path, bgCard, divider);
                row.Location = new Point(0, rowY);
                listPanel.Controls.Add(row);
                rowY += row.Height + 1;
            }
        }
        form.Controls.Add(listPanel);

        // ── Footer — semi-transparent navy tint ──────────────────────────
        var footer = new GlassPanel
        {
            Location  = new Point(0, hdrH + listH),
            Size      = new Size(w, footH),
            BackColor = Color.Transparent,
        };
        footer.Paint += (_, e) =>
        {
            using var b = new SolidBrush(Color.FromArgb(205, bgNav));
            e.Graphics.FillRectangle(b, e.ClipRectangle);
            using var pen = new Pen(divider, 1f);
            e.Graphics.DrawLine(pen, 0, 0, w, 0);
        };

        // Equal margins on all sides and between buttons.
        const int btnMargin = 14;
        int btnW = (w - btnMargin * 5) / 4;
        int btnY = (footH - 36) / 2;

        var openBtn = MakeButton("Open Project  (.rfproj)",
            new Point(btnMargin, btnY), new Size(btnW, 36),
            Color.FromArgb(0x1D, 0x6F, 0xE8), Color.FromArgb(0x2E, 0x84, 0xFF));
        openBtn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Open Project",
                Filter = "RefractorForge Project|*.rfproj|All files|*.*",
            };
            if (dlg.ShowDialog(form) == DialogResult.OK)
            {
                _result = new StartupResult { Action = StartupAction.OpenProject, ProjectPath = dlg.FileName };
                form.Close();
            }
        };

        var rfaBtn = MakeButton("Open Level RFA",
            new Point(btnMargin * 2 + btnW, btnY), new Size(btnW, 36),
            Color.FromArgb(0xB4, 0x53, 0x09), Color.FromArgb(0xD4, 0x66, 0x0A));
        rfaBtn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title       = "Select Level Archive(s)",
                Filter      = "Level Archives|*.rfa|All files|*.*",
                Multiselect = true,
            };
            if (dlg.ShowDialog(form) == DialogResult.OK && dlg.FileNames.Length > 0)
            {
                _result = new StartupResult { Action = StartupAction.OpenLevelRfa, LevelArchives = dlg.FileNames };
                form.Close();
            }
        };

        var folderBtn = MakeButton("Open Level Folder",
            new Point(btnMargin * 3 + btnW * 2, btnY), new Size(btnW, 36),
            Color.FromArgb(0x07, 0x76, 0x6E), Color.FromArgb(0x09, 0x92, 0x89));
        folderBtn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description            = "Select extracted level folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton    = false,
            };
            if (dlg.ShowDialog(form) == DialogResult.OK)
            {
                _result = new StartupResult { Action = StartupAction.OpenLevelFolder, LevelFolder = dlg.SelectedPath };
                form.Close();
            }
        };

        var newBtn = MakeButton("New Map",
            new Point(btnMargin * 4 + btnW * 3, btnY), new Size(btnW, 36),
            Color.FromArgb(0x15, 0x80, 0x3D), Color.FromArgb(0x1A, 0x9E, 0x4C));
        newBtn.Click += (_, _) =>
        {
            _result = new StartupResult { Action = StartupAction.NewMap };
            form.Close();
        };

        footer.Controls.AddRange(new Control[] { openBtn, rfaBtn, folderBtn, newBtn });
        form.Controls.Add(footer);
        form.Invalidate(true);
    }

    private static GlassPanel MakeRow(Form form, int w, string name, string game, string path,
                                      Color baseBg, Color divider)
    {
        var hoverBg   = Color.FromArgb(0x30, 0x40, 0x55);
        var isHovered = false;
        var row = new GlassPanel { Size = new Size(w, 48), BackColor = Color.Transparent, Cursor = Cursors.Hand };

        // Semi-transparent row fill painted after the parent BackgroundImage shows through.
        row.Paint += (_, e) =>
        {
            using var b = new SolidBrush(Color.FromArgb(190, isHovered ? hoverBg : baseBg));
            e.Graphics.FillRectangle(b, e.ClipRectangle);
        };

        var nameLbl = new Label
        {
            Text      = name,
            ForeColor = Color.FromArgb(0xEA, 0xEA, 0xEA),
            BackColor = Color.Transparent,
            AutoSize  = false,
            Size      = new Size(w - 130, 24),
            Location  = new Point(24, 4),
            Font      = new Font("Segoe UI", 10f, FontStyle.Regular),
        };
        var gameLbl = new Label
        {
            Text      = game,
            ForeColor = Color.FromArgb(0x60, 0x80, 0xA0),
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(w - 106, 6),
            Font      = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.MiddleRight,
        };
        string dir = System.IO.Path.GetDirectoryName(path) ?? "";
        if (dir.Length > 72) dir = "..." + dir[^69..];
        var dirLbl = new Label
        {
            Text      = dir,
            ForeColor = Color.FromArgb(0x50, 0x5E, 0x6C),
            BackColor = Color.Transparent,
            AutoSize  = false,
            Size      = new Size(w - 130, 17),
            Location  = new Point(24, 28),
            Font      = new Font("Segoe UI", 8f),
        };
        var bottom = new Panel { Location = new Point(0, 47), Size = new Size(w, 1), BackColor = divider };

        row.Controls.AddRange(new Control[] { nameLbl, gameLbl, dirLbl, bottom });

        var capturedPath = path;
        void OnClick(object? s, EventArgs e)
        {
            _result = new StartupResult { Action = StartupAction.OpenProject, ProjectPath = capturedPath };
            form.Close();
        }
        void OnEnter(object? s, EventArgs e) { isHovered = true;  row.Invalidate(); }
        void OnLeave(object? s, EventArgs e) { isHovered = false; row.Invalidate(); }

        foreach (Control c in row.Controls) { c.Click += OnClick; c.MouseEnter += OnEnter; c.MouseLeave += OnLeave; }
        row.Click      += OnClick;
        row.MouseEnter += OnEnter;
        row.MouseLeave += OnLeave;
        return row;
    }

    private static Button MakeButton(string text, Point location, Size size, Color normal, Color hover)
    {
        var btn = new Button
        {
            Text      = text,
            Location  = location,
            Size      = size,
            BackColor = normal,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Font      = new Font("Segoe UI", 9.5f),
        };
        btn.FlatAppearance.BorderSize         = 0;
        btn.FlatAppearance.MouseOverBackColor = hover;
        btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(normal, 0.1f);
        return btn;
    }
}

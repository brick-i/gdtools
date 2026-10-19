using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Интерфейс для gd_currency.exe в стиле Discord:
//   рельса -> список статов -> лог операций -> панель "Отмечено".
static class GDGui
{
    // ================================================================== тема
    static class Th
    {
        public static bool Dark = true;

        public static Color RailBg, SideBg, ChatBg, InputBg, HoverBg, ActiveBg,
                             Border, Text, Dim, Muted, Accent, AccentH,
                             Green, Red, Yellow, Value, White, TitleBg;

        public static void Set(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                RailBg   = Color.FromArgb(30, 31, 34);
                TitleBg  = Color.FromArgb(30, 31, 34);
                SideBg   = Color.FromArgb(43, 45, 49);
                ChatBg   = Color.FromArgb(49, 51, 56);
                InputBg  = Color.FromArgb(56, 58, 64);
                HoverBg  = Color.FromArgb(53, 55, 60);
                ActiveBg = Color.FromArgb(64, 66, 73);
                Border   = Color.FromArgb(38, 39, 43);
                Text     = Color.FromArgb(219, 222, 225);
                Dim      = Color.FromArgb(148, 155, 164);
                Muted    = Color.FromArgb(128, 132, 142);
                Accent   = Color.FromArgb(88, 101, 242);
                AccentH  = Color.FromArgb(71, 82, 196);
                Green    = Color.FromArgb(35, 165, 90);
                Red      = Color.FromArgb(218, 55, 60);
                Yellow   = Color.FromArgb(240, 178, 50);
                Value    = Color.FromArgb(87, 242, 135);
                White    = Color.White;
            }
            else
            {
                RailBg   = Color.FromArgb(227, 229, 232);
                TitleBg  = Color.FromArgb(227, 229, 232);
                SideBg   = Color.FromArgb(242, 243, 245);
                ChatBg   = Color.FromArgb(255, 255, 255);
                InputBg  = Color.FromArgb(235, 237, 239);
                HoverBg  = Color.FromArgb(230, 232, 236);
                ActiveBg = Color.FromArgb(213, 216, 220);
                Border   = Color.FromArgb(220, 222, 225);
                Text     = Color.FromArgb(6, 6, 7);
                Dim      = Color.FromArgb(95, 99, 104);
                Muted    = Color.FromArgb(128, 132, 142);
                Accent   = Color.FromArgb(88, 101, 242);
                AccentH  = Color.FromArgb(71, 82, 196);
                Green    = Color.FromArgb(35, 165, 90);
                Red      = Color.FromArgb(218, 55, 60);
                Yellow   = Color.FromArgb(200, 140, 20);
                Value    = Color.FromArgb(0, 130, 70);
                White    = Color.White;
            }
        }
    }

    static Font F(float size) { return new Font("Segoe UI", size); }
    static Font FB(float size) { return new Font("Segoe UI", size, FontStyle.Bold); }
    static Font FM(float size) { return new Font("Consolas", size); }

    static GraphicsPath Round(Rectangle r, int radius)
    {
        GraphicsPath p = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ================================================================== данные
    class Stat
    {
        public int Id;
        public string Name;
        public bool Checked;
        public bool Sel;
        public string Val = "—";
        public StatRow Row;
        public Stat(int id, string name) { Id = id; Name = name; }
    }

    static readonly Stat[] CurrencyDef = new Stat[]
    {
        new Stat(14, "Орбы"),
        new Stat(13, "Алмазы"),
        new Stat(29, "Двойные алмазы"),
        new Stat(12, "Пользовательские монеты"),
        new Stat(28, "Луны"),
        new Stat(21, "Ключи подвала"),
        new Stat(22, "Всего орб собрано"),
        new Stat(16, "Теневые осколки"),
        new Stat(17, "Ядовитые осколки"),
        new Stat(18, "Огненные осколки"),
        new Stat(19, "Ледяные осколки"),
        new Stat(20, "Лавовые осколки"),
        new Stat(23, "Земляные осколки"),
        new Stat(24, "Кровяные осколки"),
        new Stat(25, "Световые осколки"),
        new Stat(26, "Металлические осколки"),
        new Stat(27, "Душевные осколки"),
    };

    static readonly Stat[] AccountDef = new Stat[]
    {
        new Stat(6,  "Звёзды"),
        new Stat(8,  "Тайные монеты"),
        new Stat(1,  "Всего прыжков"),
        new Stat(2,  "Всего попыток"),
        new Stat(3,  "Пройдено уровней"),
        new Stat(4,  "Пройдено онлайн-уровней"),
        new Stat(5,  "Демонов побеждено"),
        new Stat(7,  "Пройдено наборов карт"),
        new Stat(9,  "Игроков уничтожено"),
        new Stat(10, "Лайков / дизлайков"),
        new Stat(11, "Оценено уровней"),
        new Stat(15, "Ежедневных пройдено"),
        new Stat(30, "Гаунтлетов пройдено"),
        new Stat(31, "Наград списка собрано"),
    };

    static readonly int[] DefaultChecked = new int[] { 14, 13, 29, 12, 28, 21, 16, 17, 18, 19, 20, 23, 24, 25, 26, 27 };

    static string Dir;
    static string Cli;

    static Skin form;
    static Panel titleBar, body, rail, listHost, markPanel, inputBar, searchBox;
    static TableLayoutPanel side, chat, rightCol;
    static Label titleText, sideHead, sideCount, chatHead, chatHint, rightHead, rightCount, emptyMark;
    static TextBox search, txtVal;
    static ComboBox cboMode;
    static FlowLayoutPanel curList, accList, msgList, markList;
    static ProgressBar prog;

    static Stat[] curStats, accStats;
    static bool curVisible = true;
    static bool busy;
    static string filter = "";

    static List<StatRow> curRows = new List<StatRow>();
    static List<StatRow> accRows = new List<StatRow>();
    static List<MsgItem> msgs = new List<MsgItem>();
    static int dragAnchor = -1;
    static string shotPath = null;
    static bool firstFill = true;
    static bool startAcc = false;
    static bool startToggle = false;

    // ============================================================ кастомное окно
    class Skin : Form
    {
        public void SetMaxBounds(Rectangle r) { MaximizedBounds = r; }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1)
                {
                    Point p = PointToClient(new Point(m.LParam.ToInt32()));
                    int b = 6;
                    bool left = p.X <= b, right = p.X >= ClientSize.Width - b;
                    bool top = p.Y <= b, bot = p.Y >= ClientSize.Height - b;
                    if (top && left) m.Result = (IntPtr)13;
                    else if (top && right) m.Result = (IntPtr)14;
                    else if (bot && left) m.Result = (IntPtr)16;
                    else if (bot && right) m.Result = (IntPtr)17;
                    else if (left) m.Result = (IntPtr)10;
                    else if (right) m.Result = (IntPtr)11;
                    else if (top) m.Result = (IntPtr)12;
                    else if (bot) m.Result = (IntPtr)15;
                }
                return;
            }
            base.WndProc(ref m);
        }
    }

    // ============================================================ элементы
    // иконка на рельсе
    class RailItem : Control
    {
        public string Glyph = "?";
        public Color Tint = Color.Gray;
        public bool Active;
        public string Tip = "";
        bool hover;

        public RailItem(string glyph, Color tint, string tip)
        {
            Glyph = glyph; Tint = tint; Tip = tip;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(72, 52);
            Cursor = Cursors.Hand;
            BackColor = Th.RailBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.RailBg);

            if (Active)
            {
                using (GraphicsPath p = Round(new Rectangle(0, 14, 8, 24), 4))
                using (SolidBrush b = new SolidBrush(Th.Dark ? Th.White : Color.FromArgb(56, 58, 66)))
                    g.FillPath(b, p);
            }

            int size = Active ? 44 : 40;
            Rectangle sq = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);

            Color fill;
            if (Th.Dark)
            {
                if (Active) fill = Tint;
                else if (hover) fill = Color.FromArgb(255, Tint);
                else fill = Blend(Th.RailBg, Tint, 0.35f);
            }
            else
            {
                if (Active) fill = Tint;
                else if (hover) fill = Blend(Tint, Color.White, 0.25f);
                else fill = Color.FromArgb(181, 186, 193);
            }

            using (GraphicsPath p = Round(sq, 14))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);

            using (Font f = FB(Active ? 15f : 14f))
                TextRenderer.DrawText(g, Glyph, f, sq, Th.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
    }

    static Color Blend(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    // реестр элементов с ролями: без него вложенные панели держат цвет от создания
    class SkinEntry { public Control C; public string Role; }
    static readonly List<SkinEntry> skin = new List<SkinEntry>();
    static void Sk(Control c, string role)
    {
        if (c == null) return;
        skin.Add(new SkinEntry { C = c, Role = role });
    }

    // строка стата в списке
    class StatRow : Control
    {
        public Stat S;
        bool hover;

        public StatRow(Stat s)
        {
            S = s;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 40;
            Cursor = Cursors.Hand;
            BackColor = Th.SideBg;
        }

        public Rectangle BoxZone { get { return new Rectangle(0, 0, Math.Min(48, Width), Height); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.SideBg);

            if (S.Sel || hover)
            {
                using (GraphicsPath p = Round(new Rectangle(6, 2, Width - 12, Height - 4), 4))
                using (SolidBrush b = new SolidBrush(S.Sel ? Th.ActiveBg : Th.HoverBg))
                    g.FillPath(b, p);
            }

            // галочка
            Rectangle cb = new Rectangle(16, (Height - 18) / 2, 18, 18);
            using (GraphicsPath p = Round(cb, 5))
            {
                if (S.Checked)
                {
                    using (SolidBrush b = new SolidBrush(Th.Accent)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Th.White, 2f))
                    {
                        g.DrawLine(pen, cb.X + 4, cb.Y + 9, cb.X + 7, cb.Y + 12);
                        g.DrawLine(pen, cb.X + 7, cb.Y + 12, cb.X + 13, cb.Y + 5);
                    }
                }
                else
                {
                    using (Pen pen = new Pen(Th.Dim, 1.6f)) g.DrawPath(pen, p);
                }
            }

            // название
            Rectangle nameR = new Rectangle(44, 0, Width - 44 - 74, Height);
            TextRenderer.DrawText(g, S.Name, F(9.5f), nameR, S.Checked ? Th.Text : Th.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // значение
            Rectangle valR = new Rectangle(Width - 76, 0, 62, Height);
            TextRenderer.DrawText(g, S.Val, FB(9.5f), valR, S.Checked ? Th.Value : Th.Dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
    }

    // строка в панели "Отмечено"
    class MarkRow : Control
    {
        public Stat S;
        public MarkRow(Stat s)
        {
            S = s;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 34;
            BackColor = Th.SideBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(SideBgOf());
            Rectangle dot = new Rectangle(14, Height / 2 - 5, 10, 10);
            using (SolidBrush b = new SolidBrush(Th.Accent)) g.FillEllipse(b, dot);
            Rectangle nameR = new Rectangle(32, 0, Width - 32 - 70, Height);
            TextRenderer.DrawText(g, S.Name, F(9f), nameR, Th.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            Rectangle valR = new Rectangle(Width - 68, 0, 56, Height);
            TextRenderer.DrawText(g, S.Val, FB(9f), valR, Th.Value,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        Color SideBgOf() { return Th.SideBg; }
    }

    // сообщение в логе
    class MsgItem : Control
    {
        public string Author = "gd_currency";
        public string Time = "";
        public string Body = "";
        public bool Err;

        public MsgItem(string author, string time, string body, bool err)
        {
            Author = author; Time = time; Body = body; Err = err;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Th.ChatBg;
        }

        public void Recompute(int width)
        {
            if (width < 120) width = 120;
            Width = width;
            int textW = Math.Max(80, width - 78);
            Size sz = TextRenderer.MeasureText(Body, FM(9f), new Size(textW, 4000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Height = Math.Max(52, 26 + sz.Height + 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.ChatBg);

            Rectangle av = new Rectangle(14, 4, 36, 36);
            using (SolidBrush b = new SolidBrush(Err ? Th.Red : Th.Accent))
                g.FillEllipse(b, av);
            TextRenderer.DrawText(g, Err ? "!" : "gd", FB(10f), av, Th.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            Size aw = TextRenderer.MeasureText(Author, FB(9.5f));
            TextRenderer.DrawText(g, Author, FB(9.5f), new Point(62, 3), Err ? Th.Red : Th.Text);
            TextRenderer.DrawText(g, Time, F(8f), new Point(62 + aw.Width + 8, 6), Th.Muted);

            Rectangle bodyR = new Rectangle(62, 22, Width - 76, Height - 26);
            TextRenderer.DrawText(g, Body, FM(9f), bodyR, Th.Text,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    // пресет-кнопка
    class Chip : Control
    {
        string text;
        bool hover;
        public Chip(string t)
        {
            text = t;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(66, 24);
            Cursor = Cursors.Hand;
            BackColor = Th.ChatBg;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Th.ChatBg);
            using (GraphicsPath p = Round(new Rectangle(0, 0, Width - 1, Height - 1), 6))
            using (SolidBrush b = new SolidBrush(hover ? Th.ActiveBg : Th.InputBg))
                g.FillPath(b, p);
            TextRenderer.DrawText(g, text, F(8.5f), new Rectangle(0, 0, Width, Height), Th.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
    }

    // ================================================================ запуск
    [STAThread]
    static void Main()
    {
        Dir = Path.GetDirectoryName(typeof(GDGui).Assembly.Location);
        Cli = Path.Combine(Dir, "gd_currency.exe");
        if (!File.Exists(Cli))
        {
            MessageBox.Show("Рядом нет gd_currency.exe\nОжидался: " + Cli, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Th.Set(true);

        // скрытый режим снимка: gd_gui.exe --shot C:\path\ui.png [--acc] [--light]
        string[] argv = Environment.GetCommandLineArgs();
        for (int i = 1; i < argv.Length - 1; i++)
            if (argv[i] == "--shot") shotPath = argv[i + 1];
        for (int i = 1; i < argv.Length; i++)
        {
            if (argv[i] == "--acc") startAcc = true;
            if (argv[i] == "--light") Th.Set(false);
            if (argv[i] == "--toggle") startToggle = true;
        }

        BuildUi();
        Application.Run(form);
    }

    // ================================================================ построение
    static void BuildUi()
    {
        form = new Skin();
        form.Text = "Geometry Dash — накрутка";
        form.FormBorderStyle = FormBorderStyle.None;
        form.ClientSize = new Size(1200, 780);
        form.MinimumSize = new Size(1000, 620);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.BackColor = Th.RailBg;
        form.Font = F(9.5f);
        form.KeyPreview = true;
        form.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) CloseAll(); };

        TableLayoutPanel root = new TableLayoutPanel();
        root.Dock = DockStyle.Fill;
        root.ColumnCount = 1;
        root.RowCount = 2;
        root.BackColor = Th.RailBg;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        form.Controls.Add(root);

        BuildTitle();
        root.Controls.Add(titleBar, 0, 0);

        body = new Panel();
        body.Dock = DockStyle.Fill;
        body.Margin = new Padding(0);
        body.BackColor = Th.RailBg;
        root.Controls.Add(body, 0, 1);

        TableLayoutPanel tlp = new TableLayoutPanel();
        tlp.Dock = DockStyle.Fill;
        tlp.Margin = new Padding(0);
        tlp.ColumnCount = 4;
        tlp.RowCount = 1;
        tlp.BackColor = Th.RailBg;
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 258));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252));
        body.Controls.Add(tlp);

        BuildRail(tlp);
        BuildSide(tlp);
        BuildChat(tlp);
        BuildRight(tlp);

        ShowGroup(true);
        Log("gd_currency", "готов к работе. Читаю статистику из процесса игры...", false);
        form.Shown += delegate
        {
            txtVal.Focus();
            if (startAcc) ShowGroup(false);
            if (startToggle) { Th.Set(!Th.Dark); ApplyTheme(); }
            RefreshAll();
        };
    }

    // ---------------------------------------------------------------- заголовок
    static void BuildTitle()
    {
        titleBar = new Panel();
        titleBar.Dock = DockStyle.Fill;
        titleBar.Margin = new Padding(0);
        titleBar.Height = 38;
        titleBar.BackColor = Th.TitleBg;
        Sk(titleBar, "title");

        titleBar.MouseDown += delegate(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        };
        titleBar.MouseDoubleClick += delegate { ToggleMax(); };

        // кнопки окна
        Panel win = new Panel();
        win.Dock = DockStyle.Right;
        win.Width = 138;
        win.BackColor = Th.TitleBg;
        Sk(win, "title");
        titleBar.Controls.Add(win);

        win.Controls.Add(WinBtn("✕", 92, Th.Red, delegate { form.Close(); }));
        win.Controls.Add(WinBtn("❐", 46, Th.HoverBg, delegate { ToggleMax(); }));
        win.Controls.Add(WinBtn("—", 0, Th.HoverBg, delegate { form.WindowState = FormWindowState.Minimized; }));

        // иконка + заголовок по центру
        Panel left = new Panel();
        left.Dock = DockStyle.Fill;
        left.BackColor = Th.TitleBg;
        Sk(left, "title");
        titleBar.Controls.Add(left);
        left.BringToFront();
        left.MouseDown += titleBar_MouseDown;
        left.MouseDoubleClick += delegate { ToggleMax(); };

        titleText = new Label();
        titleText.Text = "Geometry Dash — накрутка валюты";
        titleText.Font = FB(9.5f);
        titleText.ForeColor = Th.Text;
        Sk(titleText, "primary");
        titleText.AutoSize = true;
        titleText.BackColor = Color.Transparent;
        titleText.MouseDown += titleBar_MouseDown;
        titleText.MouseDoubleClick += delegate { ToggleMax(); };
        left.Controls.Add(titleText);
        left.Resize += delegate { CenterTitle(left); };
        CenterTitle(left);
    }

    static void CenterTitle(Panel host)
    {
        if (titleText == null) return;
        titleText.Location = new Point(Math.Max(8, (host.ClientSize.Width - titleText.Width) / 2),
                                       (host.ClientSize.Height - titleText.Height) / 2);
    }

    static void titleBar_MouseDown(object s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    static Panel WinBtn(string glyph, int right, Color hover, EventHandler click)
    {
        Panel p = new Panel();
        p.Size = new Size(46, 38);
        p.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        p.Location = new Point(right, 0);
        p.BackColor = Th.TitleBg;
        p.Cursor = Cursors.Hand;
        Label l = new Label();
        l.Text = glyph;
        l.Font = F(10f);
        l.ForeColor = Th.Dim;
        Sk(p, "winbtn");
        Sk(l, "winbtn");
        l.AutoSize = false;
        l.TextAlign = ContentAlignment.MiddleCenter;
        l.Dock = DockStyle.Fill;
        l.BackColor = Color.Transparent;
        l.Cursor = Cursors.Hand;
        p.Controls.Add(l);
        EventHandler enter = delegate { p.BackColor = hover; l.ForeColor = Th.White; };
        EventHandler leave = delegate { p.BackColor = Th.TitleBg; l.ForeColor = Th.Dim; };
        p.MouseEnter += enter; l.MouseEnter += enter;
        p.MouseLeave += leave; l.MouseLeave += leave;
        p.Click += click; l.Click += click;
        return p;
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);

    static void ToggleMax()
    {
        if (form.WindowState == FormWindowState.Maximized)
        {
            form.WindowState = FormWindowState.Normal;
        }
        else
        {
            form.SetMaxBounds(Screen.FromControl(form).WorkingArea);
            form.WindowState = FormWindowState.Maximized;
        }
    }

    static void CloseAll()
    {
        if (form != null) form.Close();
    }

    // ---------------------------------------------------------------- рельса
    static void BuildRail(TableLayoutPanel tlp)
    {
        rail = new Panel();
        rail.Dock = DockStyle.Fill;
        rail.Margin = new Padding(0);
        rail.BackColor = Th.RailBg;
        Sk(rail, "rail");
        tlp.Controls.Add(rail, 0, 0);

        RailItem a = new RailItem("В", Color.FromArgb(88, 101, 242), "Валюта");
        RailItem b = new RailItem("С", Color.FromArgb(35, 165, 90), "Статы аккаунта");
        RailItem c = new RailItem("↻", Color.FromArgb(240, 178, 50), "Обновить статистику");
        RailItem d = new RailItem("◐", Color.FromArgb(120, 130, 160), "Сменить тему");

        a.Click += delegate { ShowGroup(true); };
        b.Click += delegate { ShowGroup(false); };
        c.Click += delegate { RefreshAll(); };
        d.Click += delegate { Th.Set(!Th.Dark); ApplyTheme(); };

        // располагаем вручную: с Dock в Panel порядок непредсказуем
        RailItem[] items = new RailItem[] { a, b, c, d };
        for (int i = 0; i < items.Length; i++)
        {
            items[i].Location = new Point(0, 6 + i * 52);
            rail.Controls.Add(items[i]);
            ToolTip t = new ToolTip();
            t.SetToolTip(items[i], items[i].Tip);
        }
        a.Active = true;

        prog = new ProgressBar();
        prog.Style = ProgressBarStyle.Marquee;
        prog.MarqueeAnimationSpeed = 0;
        prog.Size = new Size(64, 5);
        prog.Location = new Point(4, 0);
        prog.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        rail.Controls.Add(prog);
    }

    // ---------------------------------------------------------------- колонка 1
    static void BuildSide(TableLayoutPanel tlp)
    {
        side = new TableLayoutPanel();
        side.Dock = DockStyle.Fill;
        side.Margin = new Padding(0);
        side.ColumnCount = 1;
        side.RowCount = 4;
        side.BackColor = Th.SideBg;
        Sk(side, "side");
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        tlp.Controls.Add(side, 1, 0);

        Panel head = new Panel();
        head.Dock = DockStyle.Fill;
        head.Margin = new Padding(0);
        head.BackColor = Th.SideBg;
        Sk(head, "side");
        side.Controls.Add(head, 0, 0);

        sideHead = new Label();
        sideHead.Text = "Валюта";
        sideHead.Font = FB(10.5f);
        sideHead.ForeColor = Th.Text;
        Sk(sideHead, "primary");
        sideHead.AutoSize = true;
        sideHead.BackColor = Color.Transparent;
        sideHead.Location = new Point(16, 15);
        head.Controls.Add(sideHead);

        Panel cntHost = new Panel();
        cntHost.Dock = DockStyle.Right;
        cntHost.Width = 82;
        cntHost.BackColor = Color.Transparent;
        head.Controls.Add(cntHost);

        sideCount = new Label();
        sideCount.Text = "";
        sideCount.Font = F(8.5f);
        sideCount.ForeColor = Th.Muted;
        Sk(sideCount, "muted");
        sideCount.Dock = DockStyle.Fill;
        sideCount.TextAlign = ContentAlignment.MiddleRight;
        sideCount.Padding = new Padding(0, 0, 14, 0);
        sideCount.BackColor = Color.Transparent;
        cntHost.Controls.Add(sideCount);

        // поиск
        searchBox = new Panel();
        searchBox.Dock = DockStyle.Fill;
        searchBox.Margin = new Padding(0);
        searchBox.BackColor = Th.SideBg;
        Sk(searchBox, "side");
        side.Controls.Add(searchBox, 0, 1);

        Panel inner = new Panel();
        inner.Location = new Point(10, 2);
        inner.Size = new Size(238, 32);
        inner.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        inner.BackColor = Th.RailBg;
        Sk(inner, "rail");
        inner.Paint += delegate(object s, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(new Rectangle(0, 0, inner.Width - 1, inner.Height - 1), 6))
            using (SolidBrush b = new SolidBrush(Th.RailBg)) e.Graphics.FillPath(b, p);
        };
        searchBox.Controls.Add(inner);

        search = new TextBox();
        search.BorderStyle = BorderStyle.None;
        search.BackColor = Th.RailBg;
        search.ForeColor = Th.Muted;
        search.Font = F(9f);
        search.Location = new Point(30, 8);
        search.Width = 198;
        search.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        search.Text = "Поиск";

        // плейсхолдер живёт внутри самого поля, поэтому он не спорит с фокусом
        search.GotFocus += delegate
        {
            if (search.Text == "Поиск") search.Select(0, search.Text.Length);
        };
        search.LostFocus += delegate
        {
            if (search.Text.Trim().Length == 0)
            {
                search.Text = "Поиск";
                search.ForeColor = Th.Muted;
            }
        };
        search.TextChanged += delegate
        {
            if (search.Text != "Поиск") search.ForeColor = Th.Text;
            filter = (search.Text == "Поиск") ? "" : search.Text.Trim().ToLowerInvariant();
            ApplyFilter();
        };
        inner.Controls.Add(search);

        // ноги
        Panel foot = new Panel();
        foot.Dock = DockStyle.Fill;
        foot.Margin = new Padding(0);
        foot.BackColor = Th.SideBg;
        Sk(foot, "side");
        side.Controls.Add(foot, 0, 3);

        foot.Controls.Add(SideBtn("Отметить всё", 10, delegate { CheckAll(true); }));
        foot.Controls.Add(SideBtn("Снять всё", 122, delegate { CheckAll(false); }));

        // списки
        listHost = new Panel();
        listHost.Dock = DockStyle.Fill;
        listHost.Margin = new Padding(0);
        listHost.BackColor = Th.SideBg;
        Sk(listHost, "side");
        side.Controls.Add(listHost, 0, 2);

        curStats = CurrencyDef;
        accStats = AccountDef;

        curList = MakeList();
        accList = MakeList();
        accList.Visible = false;
        Sk(curList, "side");
        Sk(accList, "side");
        listHost.Controls.Add(curList);
        listHost.Controls.Add(accList);

        foreach (Stat s in curStats) { s.Row = new StatRow(s); AddRow(s.Row, curRows, curList); }
        foreach (Stat s in accStats) { s.Row = new StatRow(s); AddRow(s.Row, accRows, accList); }
    }

    static Button SideBtn(string text, int x, EventHandler click)
    {
        Button b = new Button();
        b.Text = text;
        b.Size = new Size(110, 28);
        b.Location = new Point(x, 8);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Th.InputBg;
        b.ForeColor = Th.Dim;
        b.Font = F(8.5f);
        b.Cursor = Cursors.Hand;
        Sk(b, "btnflat");
        b.Click += click;
        b.MouseEnter += delegate { b.BackColor = Th.ActiveBg; b.ForeColor = Th.Text; };
        b.MouseLeave += delegate { b.BackColor = Th.InputBg; b.ForeColor = Th.Dim; };
        return b;
    }

    static FlowLayoutPanel MakeList()
    {
        FlowLayoutPanel p = new FlowLayoutPanel();
        p.Dock = DockStyle.Fill;
        p.FlowDirection = FlowDirection.TopDown;
        p.WrapContents = false;
        p.AutoScroll = true;
        p.BackColor = Th.SideBg;
        p.Margin = new Padding(0);
        p.Padding = new Padding(0, 4, 0, 4);
        p.ClientSizeChanged += delegate { SizeRows(p); };
        p.MouseUp += delegate { dragActive = false; };
        return p;
    }

    static void AddRow(StatRow r, List<StatRow> into, FlowLayoutPanel host)
    {
        into.Add(r);
        host.Controls.Add(r);
        HookRow(r);
    }

    static void SizeRows(FlowLayoutPanel p)
    {
        int w = p.ClientSize.Width - 2;
        if (p.VerticalScroll.Visible) w -= 16;
        foreach (Control c in p.Controls) c.Width = Math.Max(60, w);
    }

    // ---------------------------------------------------------- свайп галочек
    static bool dragActive;
    static bool dragState;
    static bool pressedInBox;
    static List<StatRow> dragList;
    static bool[] dragOrig;
    static int dragAnchorIdx;
    static int dragListIdx;

    // ведём диапазон от якоря до текущей строки: не сбивается на быстрой
    // протяжке, а строки вне диапазона возвращаются к исходному состоянию
    static void ApplyDrag(int cur)
    {
        if (dragList == null || dragList.Count == 0) return;
        if (cur < 0) cur = 0;
        if (cur >= dragList.Count) cur = dragList.Count - 1;
        int lo = Math.Min(dragAnchorIdx, cur);
        int hi = Math.Max(dragAnchorIdx, cur);
        for (int i = 0; i < dragList.Count; i++)
        {
            bool want = (i >= lo && i <= hi) ? dragState : dragOrig[i];
            if (dragList[i].S.Checked != want)
            {
                dragList[i].S.Checked = want;
                dragList[i].Invalidate();
            }
        }
        RebuildMarks();
    }

    static void HookRow(StatRow r)
    {
        r.MouseDown += delegate(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            pressedInBox = e.X < 48;
            if (pressedInBox)
            {
                dragState = !r.S.Checked;
                dragActive = true;
                dragList = curVisible ? curRows : accRows;
                dragListIdx = dragList.IndexOf(r);
                dragAnchorIdx = dragListIdx;
                dragOrig = new bool[dragList.Count];
                for (int i = 0; i < dragList.Count; i++) dragOrig[i] = dragList[i].S.Checked;
                ApplyDrag(dragAnchorIdx);
            }
            else
            {
                dragActive = false;
                SelectRow(r, Control.ModifierKeys);
            }
            r.Invalidate();
        };

        r.MouseMove += delegate(object s, MouseEventArgs e)
        {
            if (!dragActive) return;
            if ((Control.MouseButtons & MouseButtons.Left) == 0) { dragActive = false; return; }

            // мышь захвачена строкой, на которой нажали: ищем цель сами
            FlowLayoutPanel host = r.Parent as FlowLayoutPanel;
            if (host == null) return;
            Point pt = host.PointToClient(Control.MousePosition);

            if (pt.Y < 0 || pt.Y > host.ClientSize.Height)
            {
                // автоскролл при вытягивании за край списка
                int step = pt.Y < 0 ? -30 : 30;
                int val = host.VerticalScroll.Value + step;
                if (val < 0) val = 0;
                if (val > host.VerticalScroll.Maximum) val = host.VerticalScroll.Maximum;
                host.VerticalScroll.Value = val;
                host.PerformLayout();
                pt = host.PointToClient(Control.MousePosition);
            }

            StatRow t = RowAt(host, pt);
            if (t == null || dragList == null) return;
            int idx = dragList.IndexOf(t);
            if (idx >= 0) ApplyDrag(idx);
        };

        r.MouseUp += delegate { dragActive = false; };
    }

    // строка под точкой клиента (в прокрученной панели координаты детей сдвинуты)
    static StatRow RowAt(FlowLayoutPanel host, Point pt)
    {
        Point d = new Point(pt.X + host.AutoScrollPosition.X, pt.Y + host.AutoScrollPosition.Y);
        foreach (Control c in host.Controls)
        {
            StatRow sr = c as StatRow;
            if (sr == null || !sr.Visible) continue;
            if (sr.Bounds.Contains(d)) return sr;
        }
        return null;
    }

    static void SelectRow(StatRow r, Keys mods)
    {
        List<StatRow> all = curVisible ? curRows : accRows;
        bool ctrl = (mods & Keys.Control) == Keys.Control;
        bool shift = (mods & Keys.Shift) == Keys.Shift;

        if (ctrl)
        {
            r.S.Sel = !r.S.Sel;
            dragAnchor = all.IndexOf(r);
        }
        else if (shift && dragAnchor >= 0 && dragAnchor < all.Count)
        {
            int a = Math.Min(dragAnchor, all.IndexOf(r));
            int b = Math.Max(dragAnchor, all.IndexOf(r));
            for (int i = 0; i < all.Count; i++) all[i].S.Sel = (i >= a && i <= b);
        }
        else
        {
            foreach (StatRow x in all) x.S.Sel = false;
            r.S.Sel = true;
            dragAnchor = all.IndexOf(r);
        }
        foreach (StatRow x in all) x.Invalidate();
    }

    static void SetChecked(Stat s, bool on)
    {
        s.Checked = on;
        if (s.Row != null) s.Row.Invalidate();
        RebuildMarks();
    }

    // ---------------------------------------------------------------- колонка 2
    static void BuildChat(TableLayoutPanel tlp)
    {
        chat = new TableLayoutPanel();
        chat.Dock = DockStyle.Fill;
        chat.Margin = new Padding(0);
        chat.ColumnCount = 1;
        chat.RowCount = 4;
        chat.BackColor = Th.ChatBg;
        Sk(chat, "chat");
        chat.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        chat.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        chat.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        chat.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        tlp.Controls.Add(chat, 2, 0);

        Panel head = new Panel();
        head.Dock = DockStyle.Fill;
        head.Margin = new Padding(0);
        head.BackColor = Th.ChatBg;
        Sk(head, "chat");
        chat.Controls.Add(head, 0, 0);

        chatHead = new Label();
        chatHead.Text = "#  лог операций";
        chatHead.Font = FB(10.5f);
        chatHead.ForeColor = Th.Text;
        Sk(chatHead, "primary");
        chatHead.AutoSize = true;
        chatHead.BackColor = Color.Transparent;
        chatHead.Location = new Point(16, 15);
        head.Controls.Add(chatHead);

        chatHint = new Label();
        chatHint.Text = "готов";
        chatHint.Font = F(8.5f);
        chatHint.ForeColor = Th.Muted;
        Sk(chatHint, "muted");
        chatHint.AutoSize = true;
        chatHint.BackColor = Color.Transparent;
        chatHint.Location = new Point(150, 17);
        head.Controls.Add(chatHint);

        Button selBtn = new Button();
        selBtn.Text = "накрутить выделенные";
        selBtn.Size = new Size(160, 26);
        selBtn.FlatStyle = FlatStyle.Flat;
        selBtn.FlatAppearance.BorderSize = 0;
        selBtn.BackColor = Th.SideBg;
        selBtn.ForeColor = Th.Dim;
        selBtn.Font = F(8.5f);
        selBtn.Cursor = Cursors.Hand;
        selBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Sk(selBtn, "btngrey");
        selBtn.Click += delegate { ApplySelected(); };
        selBtn.MouseEnter += delegate { selBtn.BackColor = Th.ActiveBg; selBtn.ForeColor = Th.Text; };
        selBtn.MouseLeave += delegate { selBtn.BackColor = Th.SideBg; selBtn.ForeColor = Th.Dim; };
        head.Controls.Add(selBtn);
        head.Resize += delegate { selBtn.Location = new Point(head.ClientSize.Width - 174, 11); };

        Panel line = new Panel();
        line.Dock = DockStyle.Fill;
        line.Margin = new Padding(0);
        line.BackColor = Th.Border;
        Sk(line, "border");
        chat.Controls.Add(line, 0, 1);

        BuildInput();

        msgList = new FlowLayoutPanel();
        msgList.Dock = DockStyle.Fill;
        msgList.Margin = new Padding(0);
        msgList.FlowDirection = FlowDirection.TopDown;
        msgList.WrapContents = false;
        msgList.AutoScroll = true;
        msgList.BackColor = Th.ChatBg;
        msgList.Padding = new Padding(4, 8, 4, 8);
        msgList.ClientSizeChanged += delegate { SizeMessages(); };
        Sk(msgList, "chat");
        chat.Controls.Add(msgList, 0, 2);
    }

    static void SizeMessages()
    {
        int w = msgList.ClientSize.Width - 8;
        if (msgList.VerticalScroll.Visible) w -= 16;
        foreach (MsgItem m in msgs) m.Recompute(Math.Max(140, w));
    }

    static void BuildInput()
    {
        Panel host = new Panel();
        host.Dock = DockStyle.Fill;
        host.Margin = new Padding(0);
        host.BackColor = Th.ChatBg;
        Sk(host, "chat");
        chat.Controls.Add(host, 0, 3);

        // пресеты
        string[] names = new string[] { "100", "1 000", "10 000", "100 000", "999 999" };
        string[] vals = new string[] { "100", "1000", "10000", "100000", "999999" };
        for (int i = 0; i < names.Length; i++)
        {
            Chip c = new Chip(names[i]);
            c.Location = new Point(16 + i * 72, 8);
            string v = vals[i];
            c.Click += delegate { txtVal.Text = v; };
            host.Controls.Add(c);
        }

        inputBar = new Panel();
        inputBar.Location = new Point(16, 40);
        inputBar.Size = new Size(560, 50);
        inputBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        inputBar.BackColor = Th.InputBg;
        Sk(inputBar, "input");
        inputBar.Paint += delegate(object s, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(new Rectangle(0, 0, inputBar.Width - 1, inputBar.Height - 1), 8))
            using (SolidBrush b = new SolidBrush(Th.InputBg)) e.Graphics.FillPath(b, p);
        };
        host.Controls.Add(inputBar);
        host.Resize += delegate
        {
            inputBar.Size = new Size(Math.Max(200, host.ClientSize.Width - 32), 50);
            inputBar.Location = new Point(16, 40);
            LayoutInput();
        };

        cboMode = new ComboBox();
        cboMode.DropDownStyle = ComboBoxStyle.DropDownList;
        cboMode.Items.Add("поставить =");
        cboMode.Items.Add("прибавить +");
        cboMode.SelectedIndex = 0;
        cboMode.FlatStyle = FlatStyle.Flat;
        cboMode.Font = F(9f);
        cboMode.BackColor = Th.InputBg;
        cboMode.ForeColor = Th.Text;
        cboMode.Size = new Size(120, 24);
        cboMode.Location = new Point(12, 13);
        Sk(cboMode, "combo");
        inputBar.Controls.Add(cboMode);

        txtVal = new TextBox();
        txtVal.BorderStyle = BorderStyle.None;
        txtVal.BackColor = Th.InputBg;
        txtVal.ForeColor = Th.Text;
        Sk(txtVal, "inputbox");
        txtVal.Font = FM(13f);
        txtVal.Text = "999999";
        txtVal.Location = new Point(146, 14);
        txtVal.Width = 150;
        inputBar.Controls.Add(txtVal);
        txtVal.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyChecked(); }
        };

        Label hint = new Label();
        hint.Text = "значение";
        hint.Font = F(8f);
        hint.ForeColor = Th.Muted;
        hint.AutoSize = true;
        hint.BackColor = Color.Transparent;
        hint.Location = new Point(146, 2);
        inputBar.Controls.Add(hint);

        inputBar.Resize += delegate { LayoutInput(); };
        inputBar.Controls.Add(btnApply = ApplyBtn());
        LayoutInput();
    }

    static void LayoutInput()
    {
        if (inputBar == null || btnApply == null) return;
        int right = inputBar.ClientSize.Width - 12;
        btnApply.Location = new Point(right - btnApply.Width, 8);
        txtVal.Width = Math.Max(60, btnApply.Left - txtVal.Left - 12);
    }

    static Button btnApply;

    static Button ApplyBtn()
    {
        Button b = new Button();
        b.Text = "НАКРУТИТЬ";
        b.Size = new Size(150, 34);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Th.Accent;
        b.ForeColor = Th.White;
        Sk(b, "btnaccent");
        b.Font = FB(9.5f);
        b.Cursor = Cursors.Hand;
        b.Location = new Point(300, 8);
        b.Click += delegate { ApplyChecked(); };
        b.MouseEnter += delegate { b.BackColor = Th.AccentH; };
        b.MouseLeave += delegate { b.BackColor = Th.Accent; };
        return b;
    }

    // ---------------------------------------------------------------- колонка 3
    static void BuildRight(TableLayoutPanel tlp)
    {
        rightCol = new TableLayoutPanel();
        rightCol.Dock = DockStyle.Fill;
        rightCol.Margin = new Padding(0);
        rightCol.ColumnCount = 1;
        rightCol.RowCount = 2;
        rightCol.BackColor = Th.SideBg;
        Sk(rightCol, "side");
        rightCol.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        rightCol.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tlp.Controls.Add(rightCol, 3, 0);

        Panel head = new Panel();
        head.Dock = DockStyle.Fill;
        head.Margin = new Padding(0);
        head.BackColor = Th.SideBg;
        Sk(head, "side");
        rightCol.Controls.Add(head, 0, 0);

        rightHead = new Label();
        rightHead.Text = "Отмечено";
        rightHead.Font = FB(10.5f);
        rightHead.ForeColor = Th.Text;
        Sk(rightHead, "primary");
        rightHead.AutoSize = true;
        rightHead.BackColor = Color.Transparent;
        rightHead.Location = new Point(16, 15);
        head.Controls.Add(rightHead);

        Panel rcntHost = new Panel();
        rcntHost.Dock = DockStyle.Right;
        rcntHost.Width = 82;
        rcntHost.BackColor = Color.Transparent;
        head.Controls.Add(rcntHost);

        rightCount = new Label();
        rightCount.Text = "0";
        rightCount.Font = FB(9f);
        rightCount.ForeColor = Th.Accent;
        Sk(rightCount, "accent");
        rightCount.Dock = DockStyle.Fill;
        rightCount.TextAlign = ContentAlignment.MiddleRight;
        rightCount.Padding = new Padding(0, 0, 16, 0);
        rightCount.BackColor = Color.Transparent;
        rcntHost.Controls.Add(rightCount);

        markPanel = new Panel();
        markPanel.Dock = DockStyle.Fill;
        markPanel.Margin = new Padding(0);
        markPanel.BackColor = Th.SideBg;
        Sk(markPanel, "side");
        rightCol.Controls.Add(markPanel, 0, 1);

        emptyMark = new Label();
        emptyMark.Text = "Пока пусто.\n\nОтметь галочками статы слева — они появятся здесь.";
        emptyMark.Font = F(9f);
        emptyMark.ForeColor = Th.Muted;
        Sk(emptyMark, "muted");
        emptyMark.AutoSize = false;
        emptyMark.TextAlign = ContentAlignment.MiddleCenter;
        emptyMark.Dock = DockStyle.Fill;
        emptyMark.BackColor = Th.SideBg;
        markPanel.Controls.Add(emptyMark);

        markList = MakeList();
        markList.Visible = false;
        Sk(markList, "side");
        markPanel.Controls.Add(markList);
    }

    static void RebuildMarks()
    {
        if (markList == null) return;
        markList.SuspendLayout();
        markList.Controls.Clear();
        int n = 0;
        foreach (Stat s in curStats) if (s.Checked) { markList.Controls.Add(new MarkRow(s)); n++; }
        foreach (Stat s in accStats) if (s.Checked) { markList.Controls.Add(new MarkRow(s)); n++; }
        markList.ResumeLayout();
        SizeRows(markList);
        markList.Visible = n > 0;
        emptyMark.Visible = n == 0;
        rightCount.Text = n.ToString();
        UpdateStatus();
    }

    // ================================================================ тема
    static void ApplyTheme()
    {
        foreach (SkinEntry s in skin)
        {
            switch (s.Role)
            {
                case "title":    s.C.BackColor = Th.TitleBg; break;
                case "rail":     s.C.BackColor = Th.RailBg; break;
                case "side":     s.C.BackColor = Th.SideBg; break;
                case "chat":     s.C.BackColor = Th.ChatBg; break;
                case "input":    s.C.BackColor = Th.InputBg; break;
                case "border":   s.C.BackColor = Th.Border; break;
                case "primary":  s.C.ForeColor = Th.Text; break;
                case "dim":      s.C.ForeColor = Th.Dim; break;
                case "muted":    s.C.ForeColor = Th.Muted; break;
                case "accent":   s.C.ForeColor = Th.Accent; break;
                case "btnflat":
                    ((Button)s.C).BackColor = Th.InputBg;
                    ((Button)s.C).ForeColor = Th.Dim;
                    break;
                case "btngrey":
                    ((Button)s.C).BackColor = Th.SideBg;
                    ((Button)s.C).ForeColor = Th.Dim;
                    break;
                case "btnaccent":
                    ((Button)s.C).BackColor = Th.Accent;
                    ((Button)s.C).ForeColor = Th.White;
                    break;
                case "combo":
                    s.C.BackColor = Th.InputBg;
                    s.C.ForeColor = Th.Text;
                    break;
                case "inputbox":
                    s.C.BackColor = Th.InputBg;
                    s.C.ForeColor = Th.Text;
                    break;
                case "winbtn":
                    s.C.BackColor = Th.TitleBg;
                    s.C.ForeColor = Th.Dim;
                    break;
            }
        }

        form.BackColor = Th.RailBg;
        sideCount.ForeColor = Th.Muted;
        chatHint.ForeColor = Th.Muted;
        rightCount.ForeColor = Th.Accent;
        emptyMark.ForeColor = Th.Muted;
        emptyMark.BackColor = Th.SideBg;
        if (search != null)
        {
            search.BackColor = Th.RailBg;
            search.ForeColor = (search.Text == "Поиск") ? Th.Muted : Th.Text;
        }

        StyleRec(form);
        RebuildMarks();
        form.Refresh();
    }

    static void StyleRec(Control p)
    {
        foreach (Control c in p.Controls)
        {
            if (c is RailItem) ((RailItem)c).BackColor = Th.RailBg;
            else if (c is StatRow) c.BackColor = Th.SideBg;
            else if (c is MarkRow) c.BackColor = Th.SideBg;
            else if (c is MsgItem) c.BackColor = Th.ChatBg;
            else if (c is Chip) c.BackColor = Th.ChatBg;
            if (c.Controls.Count > 0) StyleRec(c);
        }
    }

    // ================================================================ данные
    static void RefreshAll()
    {
        RunAsync("читаю статистику из игры...", "wallet -csv", delegate(string res) { Fill(res); }, true);
    }

    static void Fill(string csv)
    {
        Dictionary<int, string[]> data = new Dictionary<int, string[]>();
        if (csv != null)
        {
            string[] lines = csv.Replace("\r", "").Split('\n');
            foreach (string ln in lines)
            {
                string s = ln.Trim();
                if (s.Length == 0 || s.StartsWith("id;") || s.StartsWith("[") || s.StartsWith(" ")) continue;
                string[] f = s.Split(';');
                if (f.Length != 5) continue;
                int id;
                if (!int.TryParse(f[0], out id)) continue;
                data[id] = f;
            }
        }

        int ok = 0;
        ok += Apply(curStats, data);
        ok += Apply(accStats, data);

        if (firstFill)
        {
            firstFill = false;
            foreach (Stat s in curStats) foreach (int d in DefaultChecked) if (d == s.Id) s.Checked = true;
        }

        RebuildMarks();
        UpdateStatus(ok);
        if (ok == 0) Log("gd_currency", "статистика не прочитана — игра запущена и меню прогрузилось?", true);

        if (shotPath != null)
        {
            string p = shotPath;
            form.BeginInvoke(new Action(delegate()
            {
                try
                {
                    form.Refresh();
                    Size s = form.ClientSize;
                    using (Bitmap bmp = new Bitmap(s.Width, s.Height))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, s.Width, s.Height));
                        bmp.Save(p, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch { }
                Environment.Exit(0);
            }));
        }
    }

    static int Apply(Stat[] list, Dictionary<int, string[]> data)
    {
        int n = 0;
        foreach (Stat s in list)
        {
            string[] f;
            if (!data.TryGetValue(s.Id, out f)) continue;
            s.Val = f[2];
            if (s.Row != null) s.Row.Invalidate();
            n++;
        }
        return n;
    }

    static void UpdateStatus() { UpdateStatus(-1); }

    static void UpdateStatus(int ok)
    {
        string game = GameInfo();
        int checked_n = 0;
        foreach (Stat s in curStats) if (s.Checked) checked_n++;
        foreach (Stat s in accStats) if (s.Checked) checked_n++;
        if (chatHint != null)
            chatHint.Text = "отмечено " + checked_n + "  •  игра: " + game;
    }

    static string GameInfo()
    {
        try
        {
            Process[] p = Process.GetProcessesByName("GeometryDash");
            if (p.Length == 0) return "не запущена";
            return "PID " + p[0].Id;
        }
        catch { return "?"; }
    }

    // ================================================================ группы
    static void ShowGroup(bool currency)
    {
        curVisible = currency;
        curList.Visible = currency;
        accList.Visible = !currency;
        sideHead.Text = currency ? "Валюта" : "Статы аккаунта";
        sideCount.Text = currency ? curStats.Length + " шт." : accStats.Length + " шт.";
        chatHead.Text = "#  " + (currency ? "валюта" : "статы аккаунта");
        foreach (Control c in rail.Controls)
        {
            RailItem r = c as RailItem;
            if (r == null) continue;
            if (r.Glyph == "В") r.Active = currency;
            else if (r.Glyph == "С") r.Active = !currency;
        }
        rail.Invalidate(true);
        ApplyFilter();
        UpdateStatus();
    }

    static void ApplyFilter()
    {
        FlowLayoutPanel host = curVisible ? curList : accList;
        List<StatRow> rows = curVisible ? curRows : accRows;
        foreach (StatRow r in rows)
        {
            bool show = filter.Length == 0
                        || r.S.Name.ToLowerInvariant().Contains(filter)
                        || r.S.Id.ToString(CultureInfo.InvariantCulture) == filter;
            r.Visible = show;
        }
        SizeRows(host);
    }

    static void CheckAll(bool on)
    {
        List<StatRow> rows = curVisible ? curRows : accRows;
        foreach (StatRow r in rows) if (r.Visible) r.S.Checked = on;
        foreach (StatRow r in rows) r.Invalidate();
        RebuildMarks();
        Log("gd_currency", (on ? "отмечены все" : "сняты все") + " в группе «"
            + (curVisible ? "Валюта" : "Статы аккаунта") + "»", false);
    }

    // ================================================================ лог
    static void Log(string author, string body, bool err)
    {
        if (msgList == null) return;
        MsgItem m = new MsgItem(author, DateTime.Now.ToString("HH:mm:ss"), body == null ? "" : body.TrimEnd(), err);
        msgs.Add(m);
        msgList.Controls.Add(m);
        while (msgs.Count > 60)
        {
            MsgItem old = msgs[0];
            msgs.RemoveAt(0);
            msgList.Controls.Remove(old);
            old.Dispose();
        }
        SizeMessages();
        if (msgList.VerticalScroll.Visible)
            msgList.ScrollControlIntoView(m);
    }

    // ================================================================ действия
    static List<Stat> CheckedStats()
    {
        List<Stat> res = new List<Stat>();
        foreach (Stat s in curStats) if (s.Checked) res.Add(s);
        foreach (Stat s in accStats) if (s.Checked) res.Add(s);
        return res;
    }

    static List<Stat> SelectedStats()
    {
        List<Stat> res = new List<Stat>();
        Stat[] list = curVisible ? curStats : accStats;
        foreach (Stat s in list) if (s.Sel) res.Add(s);
        return res;
    }

    static long ReadValue()
    {
        string t = txtVal.Text.Replace(" ", "").Trim();
        long v;
        if (!long.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out v))
            throw new Exception("«" + txtVal.Text + "» — не число");
        if (v < 0) throw new Exception("число не может быть отрицательным");
        if (v > 99999999) throw new Exception("больше 99 999 999 ставить не советую — игра может округлить");
        return v;
    }

    static string BuildArgs(List<Stat> stats, long v, bool add, out int n)
    {
        Dictionary<int, string> vals = new Dictionary<int, string>();
        n = 0;
        StringBuilder sb = new StringBuilder("bulk");
        foreach (Stat s in stats)
        {
            long final = v;
            if (add)
            {
                long cur;
                if (!long.TryParse(s.Val, NumberStyles.Any, CultureInfo.InvariantCulture, out cur)) continue;
                final = cur + v;
            }
            if (vals.ContainsKey(s.Id)) continue;
            vals[s.Id] = null;
            sb.Append(" ").Append(s.Id).Append("=").Append(final);
            n++;
        }
        if (n == 0) throw new Exception("нечего менять — сначала прочитай статистику (кнопка ↻ на рельсе)");
        return sb.ToString();
    }

    static void ApplyChecked()
    {
        List<Stat> stats = CheckedStats();
        if (stats.Count == 0) { Log("gd_currency", "галочками ничего не отмечено", true); return; }
        Run(stats);
    }

    static void ApplySelected()
    {
        List<Stat> stats = SelectedStats();
        if (stats.Count == 0)
        {
            Log("gd_currency", "в списке не выделено ни одной строки.\nCtrl+клик — по одной, Shift+клик — диапазон.\nЛибо ставь галочки свайпом слева и жми «НАКРУТИТЬ»", true);
            return;
        }
        Run(stats);
    }

    static void Run(List<Stat> stats)
    {
        long v;
        try { v = ReadValue(); }
        catch (Exception e) { Log("gd_currency", e.Message, true); return; }
        bool add = cboMode.SelectedIndex == 1;
        int n;
        string args;
        try { args = BuildArgs(stats, v, add, out n); }
        catch (Exception e) { Log("gd_currency", e.Message, true); return; }
        RunAsync((add ? "прибавляю " : "ставлю ") + v + " — статов: " + n, args,
            delegate { RefreshAll(); }, false);
    }

    // ================================================================ процесс
    static string RunTool(string args)
    {
        ProcessStartInfo psi = new ProcessStartInfo(Cli, args);
        psi.WorkingDirectory = Dir;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
        Process p = Process.Start(psi);
        string o = p.StandardOutput.ReadToEnd();
        string e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return o + e;
    }

    static void RunAsync(string caption, string args, Action<string> after, bool silent)
    {
        if (busy) { Log("gd_currency", "уже занято, подожди", true); return; }
        busy = true;
        SetBusy(true, caption);
        string a = args;
        bool q = silent;
        Thread t = new Thread(delegate()
        {
            string res;
            try { res = RunTool(a); }
            catch (Exception ex) { res = "[!] " + ex.Message; }
            try
            {
                form.BeginInvoke(new Action(delegate()
                {
                    busy = false;
                    SetBusy(false, null);
                    if (!q || res.IndexOf("[!]") >= 0)
                    {
                        Log("gd_currency", "$ gd_currency " + a + "\n" + res.TrimEnd(), res.IndexOf("[!]") >= 0);
                    }
                    if (after != null) after(res);
                }));
            }
            catch { }
        });
        t.IsBackground = true;
        t.Start();
    }

    static void SetBusy(bool on, string caption)
    {
        if (btnApply != null) btnApply.Enabled = !on;
        if (prog != null) prog.MarqueeAnimationSpeed = on ? 30 : 0;
        if (caption != null && chatHint != null) chatHint.Text = caption;
        else UpdateStatus();
        form.Cursor = on ? Cursors.AppStarting : Cursors.Default;
    }
}

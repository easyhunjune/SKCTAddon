// SKCT 모의고사 도구: 타이머(15분 역산) + 메모장/그림판 + 계산기(SKCT 배치, 숫자패드 고정)
// 빌드: csc /nologo /target:winexe /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:SKCTAddon.exe SKCTAddon.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class Theme
{
    public static readonly Color Back = Color.White;
    public static readonly Color Bar = Color.FromArgb(243, 244, 246);
    public static readonly Color Btn = Color.FromArgb(243, 244, 246);
    public static readonly Color BtnHover = Color.FromArgb(229, 231, 235);
    public static readonly Color Line = Color.FromArgb(229, 231, 235);
    public static readonly Color Accent = Color.FromArgb(47, 48, 53);
    public static readonly Color AccentHover = Color.FromArgb(69, 70, 76);
    public static readonly Color Text = Color.FromArgb(17, 24, 39);
    public static readonly Color SubText = Color.FromArgb(156, 163, 175);
    public static readonly Color Red = Color.FromArgb(220, 38, 38);
    public static readonly Color RedBack = Color.FromArgb(254, 226, 226);

    // 오른쪽 패널 (시험 화면의 메모장/계산기 영역)
    public static readonly Color Panel = Color.FromArgb(241, 243, 245);
    public static readonly Color Border = Color.FromArgb(222, 226, 230);
    public static readonly Color KeyGray = Color.FromArgb(233, 236, 239);
    public static readonly Color KeyGrayHover = Color.FromArgb(222, 226, 230);
    public static readonly Color KeyWhiteHover = Color.FromArgb(248, 249, 250);
    public static readonly Color OpText = Color.FromArgb(134, 142, 150);
    public static readonly Color Equal = Color.FromArgb(73, 80, 87);
    public static readonly Color EqualHover = Color.FromArgb(52, 58, 64);

    public static float S = 1f;   // DPI 배율
    public static int Px(float v) { return (int)Math.Round(v * S); }

    public static Color Shade(Color c, float f)
    {
        return Color.FromArgb((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
    }
}

// 포커스를 가져가지 않는 버튼 (계산기 버튼 클릭 후 Enter가 버튼을 다시 누르는 문제 방지)
class FlatButton : Control
{
    public Color Base = Theme.Btn;
    public Color Hover = Theme.BtnHover;
    public int Radius = Theme.Px(4);
    public Color BorderColor = Color.Empty;   // 비어 있으면 테두리 없음
    public event EventHandler Pressed;

    protected bool active, over, down;

    public FlatButton(string text)
    {
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Text = text;
        ForeColor = Theme.Text;
        Font = new Font("맑은 고딕", 10f);
    }

    public bool Active
    {
        get { return active; }
        set { active = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); over = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); over = false; down = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { down = true; Invalidate(); }
    }

    // 빠르게 두 번 눌러도 두 번 다 입력되도록 Click 대신 직접 처리
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        bool fire = down && ClientRectangle.Contains(e.Location);
        down = false;
        Invalidate();
        if (fire && Pressed != null) Pressed(this, EventArgs.Empty);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color bg = active ? (over ? Theme.AccentHover : Theme.Accent) : (over ? Hover : Base);
        if (down) bg = Theme.Shade(bg, 0.88f);
        Color fg = active ? Color.White : ForeColor;

        using (GraphicsPath p = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
        using (SolidBrush b = new SolidBrush(bg))
            g.FillPath(b, p);
        if (BorderColor != Color.Empty && !active)
        {
            g.SmoothingMode = SmoothingMode.None;
            using (Pen pen = new Pen(BorderColor)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    static GraphicsPath RoundRect(Rectangle r, int rad)
    {
        GraphicsPath p = new GraphicsPath();
        int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 1) { p.AddRectangle(new Rectangle(r.X, r.Y, r.Width + 1, r.Height + 1)); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// 비어 있으면 안내 문구를 보여주는 여러 줄 입력칸 (여러 줄 TextBox는 기본 placeholder가 없음)
class CueTextBox : TextBox
{
    public string Cue = "";

    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
    [DllImport("imm32.dll")] static extern int ImmGetCompositionStringW(IntPtr hIMC, int index, IntPtr buf, int len);

    // 한글 조합 중에는 안내 문구를 그리지 않음 (조합 글자와 겹치지 않게)
    bool Composing()
    {
        IntPtr h = ImmGetContext(Handle);
        if (h == IntPtr.Zero) return false;
        try { return ImmGetCompositionStringW(h, 0x0008, IntPtr.Zero, 0) > 0; }
        finally { ImmReleaseContext(Handle, h); }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x010D || m.Msg == 0x010E) Invalidate();   // IME 조합 시작/끝
        if (m.Msg == 0x000F && TextLength == 0 && !Composing())
        {
            using (Graphics g = CreateGraphics())
                TextRenderer.DrawText(g, Cue, Font, new Point(1, 0), Theme.SubText, TextFormatFlags.NoPadding);
        }
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
}

class DBTable : TableLayoutPanel
{
    public DBTable() { DoubleBuffered = true; }
}

// ───────────── 그림판 ─────────────

class Canvas : Control
{
    // 펜 설정 (여기만 바꾸면 됨)
    public static readonly Color PenColor = Color.Black;
    public const float BasePenWidth = 3f;   // 100% 배율 기준 두께(px)

    public float PenWidth = BasePenWidth;

    Bitmap bmp;
    Point last;
    bool drawing;

    public Canvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        Cursor = Cursors.Cross;
        EnsureBitmap(SystemInformation.VirtualScreen.Size);
    }

    void EnsureBitmap(Size need)
    {
        int w = Math.Max(need.Width, 1);
        int h = Math.Max(need.Height, 1);
        if (bmp != null)
        {
            if (bmp.Width >= w && bmp.Height >= h) return;
            w = Math.Max(w, bmp.Width);
            h = Math.Max(h, bmp.Height);
        }
        Bitmap nb = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(nb))
        {
            g.Clear(Color.White);
            if (bmp != null) g.DrawImageUnscaled(bmp, 0, 0);
        }
        if (bmp != null) bmp.Dispose();
        bmp = nb;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        EnsureBitmap(ClientSize);
    }

    public void ClearAll()
    {
        using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        drawing = true;
        last = e.Location;
        Stroke(last, last);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!drawing) return;
        Stroke(last, e.Location);
        last = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) drawing = false;
    }

    void Stroke(Point a, Point b)
    {
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (a == b)
            {
                float rad = PenWidth / 2f;
                using (SolidBrush br = new SolidBrush(PenColor))
                    g.FillEllipse(br, a.X - rad, a.Y - rad, PenWidth, PenWidth);
            }
            else
            {
                using (Pen p = new Pen(PenColor, PenWidth))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawLine(p, a, b);
                }
            }
        }
        int pad = (int)Math.Ceiling(PenWidth) + 2;
        Invalidate(Rectangle.FromLTRB(Math.Min(a.X, b.X) - pad, Math.Min(a.Y, b.Y) - pad,
                                      Math.Max(a.X, b.X) + pad, Math.Max(a.Y, b.Y) + pad));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImage(bmp, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && bmp != null) bmp.Dispose();
        base.Dispose(disposing);
    }
}

// 메모장 ↔ 그림판 전환. 두 내용은 전환해도 각각 유지됨
class MemoPanel : Panel
{
    public const int HeaderHeight = 44;

    readonly Panel header;
    readonly FlatButton tabText, tabDraw, clearBtn;
    readonly Canvas canvas;
    readonly Panel textHost;
    readonly CueTextBox text;
    bool drawMode = true;

    public MemoPanel()
    {
        BackColor = Theme.Panel;

        canvas = new Canvas();
        canvas.Dock = DockStyle.Fill;
        canvas.PenWidth = Canvas.BasePenWidth * Theme.S;

        text = new CueTextBox();
        text.Cue = "메모를 입력하세요.";
        text.Multiline = true;
        text.AcceptsReturn = true;
        text.AcceptsTab = true;
        text.BorderStyle = BorderStyle.None;
        text.ScrollBars = ScrollBars.Vertical;
        text.Font = new Font("맑은 고딕", 10.5f);
        text.ForeColor = Theme.Text;
        text.Dock = DockStyle.Fill;
        textHost = new Panel();
        textHost.BackColor = Color.White;
        textHost.Padding = new Padding(Theme.Px(10));
        textHost.Dock = DockStyle.Fill;
        textHost.Controls.Add(text);

        header = new Panel();
        header.Dock = DockStyle.Top;
        header.Height = Theme.Px(HeaderHeight);
        header.BackColor = Theme.Panel;
        // 사각형 버튼: [메모장|그림판] 붙은 전환 버튼 + 오른쪽 전체 지우기
        tabText = SquareButton("메모장");
        tabDraw = SquareButton("그림판");
        clearBtn = SquareButton("전체 지우기");
        clearBtn.ForeColor = Theme.Text;
        tabText.Pressed += delegate { SetMode(false); };
        tabDraw.Pressed += delegate { SetMode(true); };
        clearBtn.Pressed += delegate { ClearCurrent(); };
        header.Controls.Add(tabText);
        header.Controls.Add(tabDraw);
        header.Controls.Add(clearBtn);
        header.Resize += delegate { LayoutHeader(); };

        Controls.Add(canvas);
        Controls.Add(textHost);
        Controls.Add(header);
        canvas.BringToFront();
        textHost.BringToFront();
        SetMode(true);
    }

    static FlatButton SquareButton(string text)
    {
        FlatButton b = new FlatButton(text);
        b.Radius = 0;
        b.Base = Color.White;
        b.Hover = Theme.KeyWhiteHover;
        b.BorderColor = Theme.Border;
        b.ForeColor = Theme.OpText;
        b.Font = new Font("맑은 고딕", 9.5f);
        return b;
    }

    public bool DrawMode { get { return drawMode; } }
    public bool Typing { get { return text.Focused; } }

    public void SetMode(bool draw)
    {
        drawMode = draw;
        canvas.Visible = draw;
        textHost.Visible = !draw;
        tabDraw.Active = draw;
        tabText.Active = !draw;
        FocusCurrent();
    }

    public void FocusCurrent()
    {
        if (drawMode) canvas.Focus(); else text.Focus();
    }

    void ClearCurrent()
    {
        if (drawMode) canvas.ClearAll();
        else { text.Clear(); text.Focus(); }
    }

    void LayoutHeader()
    {
        int h = Theme.Px(32), tw = Theme.Px(64), cw = Theme.Px(88);
        tabText.SetBounds(0, 0, tw, h);
        tabDraw.SetBounds(tw - 1, 0, tw, h);   // 테두리 한 줄을 겹쳐서 붙인 버튼처럼
        clearBtn.SetBounds(header.Width - cw, 0, cw, h);
    }
}

// ───────────── 계산기 ─────────────

// 즉시 계산 방식(일반 계산기와 동일): 2 + 3 × 4 = 20
public class CalcEngine
{
    decimal acc, lastB;
    char op, lastOp;          // '+', '-', '*', '/' / '\0' = 없음
    string entry = "0";
    bool replace = true;      // 다음 숫자 입력이 새 숫자로 시작하는지
    bool operandSet;          // 연산자 뒤에 두 번째 수가 입력됐는지
    string error;
    string history = "";      // 최근 계산 기록 (C를 눌러도 유지)

    public string Display { get { return error ?? Group(entry); } }

    // 연산자 대기 중이면 "12 +", 아니면 마지막 계산 "12 + 3 = 15"
    public string Expr
    {
        get { return op != '\0' ? Group(Fmt(acc)) + " " + Sym(op) : history; }
    }

    public void Press(string k)
    {
        if (error != null)
        {
            Reset();
            if (!(IsDigit(k) || k == "00" || k == ".")) return;
        }
        try
        {
            if (IsDigit(k)) Digit(k[0]);
            else switch (k)
            {
                case "00": Digit('0'); Digit('0'); break;
                case ".": Dot(); break;
                case "+": case "-": case "*": case "/": Operator(k[0]); break;
                case "=": Equal(); break;
                case "C": Reset(); break;
                case "BS": Backspace(); break;
                case "SQRT": Sqrt(); break;
            }
        }
        catch (DivideByZeroException) { Fail("0으로 나눌 수 없습니다"); }
        catch (OverflowException) { Fail("계산 범위를 넘었습니다"); }
    }

    void StartEntry()
    {
        if (!replace) return;
        entry = "0";
        replace = false;
    }

    void Digit(char d)
    {
        StartEntry();
        int n = 0;
        foreach (char ch in entry) if (char.IsDigit(ch)) n++;
        if (n >= 16) return;
        if (entry == "0") entry = d.ToString();
        else entry += d;
        if (op != '\0') operandSet = true;
    }

    void Dot()
    {
        StartEntry();
        if (entry.IndexOf('.') < 0) entry += ".";
        if (op != '\0') operandSet = true;
    }

    void Operator(char o)
    {
        decimal x = Parse(entry);
        if (op != '\0' && operandSet) { acc = Apply(acc, op, x); entry = Fmt(acc); }
        else if (op == '\0') acc = x;
        op = o;
        replace = true;
        operandSet = false;
    }

    void Equal()
    {
        decimal a, b;
        char o;
        if (op != '\0') { a = acc; b = operandSet ? Parse(entry) : acc; o = op; }
        else if (lastOp != '\0') { a = Parse(entry); b = lastB; o = lastOp; }
        else { replace = true; return; }
        decimal r = Apply(a, o, b);
        history = Group(Fmt(a)) + " " + Sym(o) + " " + Group(Fmt(b)) + " = " + Group(Fmt(r));
        lastOp = o;
        lastB = b;
        op = '\0';
        operandSet = false;
        acc = r;
        entry = Fmt(r);
        replace = true;
    }

    void Backspace()
    {
        if (replace) return;
        entry = entry.Substring(0, entry.Length - 1);
        if (entry == "") entry = "0";
    }

    void Sqrt()
    {
        decimal x = Parse(entry);
        if (x < 0) { Fail("잘못된 입력입니다"); return; }
        decimal r = (decimal)Math.Sqrt((double)x);
        if (op == '\0') history = "√(" + Group(Fmt(x)) + ") = " + Group(Fmt(r));
        entry = Fmt(r);
        replace = true;
        if (op != '\0') operandSet = true;
    }

    void Reset()
    {
        error = null;
        acc = 0; lastB = 0;
        op = '\0'; lastOp = '\0';
        entry = "0";
        replace = true;
        operandSet = false;
    }

    void Fail(string msg)
    {
        Reset();
        error = msg;
    }

    static decimal Apply(decimal a, char o, decimal b)
    {
        switch (o)
        {
            case '+': return a + b;
            case '-': return a - b;
            case '*': return a * b;
            case '/': return a / b;
        }
        return b;
    }

    static bool IsDigit(string k) { return k.Length == 1 && k[0] >= '0' && k[0] <= '9'; }

    static string Sym(char o)
    {
        switch (o)
        {
            case '+': return "+";
            case '-': return "-";
            case '*': return "×";
            case '/': return "÷";
        }
        return "";
    }

    static decimal Parse(string s)
    {
        if (s.EndsWith(".")) s += "0";
        return decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    // 유효숫자 16자리로 반올림 (10 ÷ 3 = 3.333333333333333)
    static string Fmt(decimal v)
    {
        decimal abs = Math.Abs(v);
        int intDigits = abs >= 1 ? (int)Math.Floor(Math.Log10((double)abs)) + 1 : 1;
        int dec = Math.Min(28, Math.Max(0, 16 - intDigits));
        v = Math.Round(v, dec, MidpointRounding.AwayFromZero);
        if (v == 0) return "0";
        return v.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    // 1234567.5 → 1,234,567.5 (입력 중인 "12." 같은 형태도 유지)
    static string Group(string s)
    {
        bool neg = s.StartsWith("-");
        if (neg) s = s.Substring(1);
        int dot = s.IndexOf('.');
        string ip = dot < 0 ? s : s.Substring(0, dot);
        string fp = dot < 0 ? "" : s.Substring(dot);
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < ip.Length; i++)
        {
            if (i > 0 && (ip.Length - i) % 3 == 0) sb.Append(',');
            sb.Append(ip[i]);
        }
        return (neg ? "-" : "") + sb.ToString() + fp;
    }
}

// "계산기" 제목 + 최근 계산 기록 + 결과 칸
class CalcDisplay : Control
{
    public string Expr = "";
    public string Value = "0";

    public CalcDisplay()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = false;
        BackColor = Theme.Panel;
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        Rectangle titleRect = new Rectangle(0, 0, Width, Theme.Px(26));
        using (Font f = new Font("맑은 고딕", 10f, FontStyle.Bold))
            TextRenderer.DrawText(g, "계산기", f, titleRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        Rectangle histRect = new Rectangle(0, titleRect.Bottom, Width, Theme.Px(24));
        using (Font f = new Font("맑은 고딕", 9f))
            TextRenderer.DrawText(g, Expr, f, histRect, Theme.OpText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        Rectangle box = new Rectangle(0, histRect.Bottom + Theme.Px(6), Width - 1, Height - histRect.Bottom - Theme.Px(14));
        g.FillRectangle(Brushes.White, box);
        using (Pen p = new Pen(Theme.Border)) g.DrawRectangle(p, box);

        int pad = Theme.Px(12);
        Rectangle valRect = new Rectangle(box.X + pad, box.Y, box.Width - 2 * pad, box.Height);
        float size = 13f;
        Font vf;
        while (true)
        {
            vf = new Font("Segoe UI", size, FontStyle.Bold);
            Size sz = TextRenderer.MeasureText(g, Value, vf, Size.Empty, TextFormatFlags.NoPadding);
            if (sz.Width <= valRect.Width || size <= 8f) break;
            vf.Dispose();
            size -= 1f;
        }
        TextRenderer.DrawText(g, Value, vf, valRect, Theme.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        vf.Dispose();
    }
}

class CalcPanel : Panel
{
    readonly CalcEngine eng = new CalcEngine();
    readonly CalcDisplay display;
    readonly FlatButton numpadBtn;

    public FlatButton NumpadButton { get { return numpadBtn; } }

    // SKCT 계산기 배치
    static readonly string[,] Labels = {
        { "C", "⌫", "÷", "√" },
        { "7", "8", "9", "×" },
        { "4", "5", "6", "-" },
        { "1", "2", "3", "+" },
        { "0", "00", ".", "=" } };
    static readonly string[,] Keys_ = {
        { "C", "BS", "/", "SQRT" },
        { "7", "8", "9", "*" },
        { "4", "5", "6", "-" },
        { "1", "2", "3", "+" },
        { "0", "00", ".", "=" } };

    public CalcPanel()
    {
        BackColor = Theme.Panel;

        display = new CalcDisplay();
        display.Dock = DockStyle.Top;
        display.Height = Theme.Px(108);

        // "계산기" 제목 오른쪽: 숫자패드 고정 켜기/끄기 (켜지면 어두운 색)
        numpadBtn = new FlatButton("숫자패드 고정");
        numpadBtn.Base = Color.White;
        numpadBtn.Hover = Theme.KeyWhiteHover;
        numpadBtn.ForeColor = Theme.OpText;
        numpadBtn.Font = new Font("맑은 고딕", 8.5f, FontStyle.Bold);
        numpadBtn.Radius = 0;
        numpadBtn.BorderColor = Theme.Border;
        display.Controls.Add(numpadBtn);
        display.Resize += delegate
        {
            int w = Theme.Px(96), h = Theme.Px(24);
            numpadBtn.SetBounds(display.Width - w, Theme.Px(1), w, h);
        };

        DBTable grid = new DBTable();
        grid.Dock = DockStyle.Fill;
        grid.BackColor = Theme.Panel;
        grid.ColumnCount = 4;
        grid.RowCount = 5;
        for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));

        Font keyFont = new Font("Segoe UI Semibold", 10.5f);
        Font korFont = new Font("맑은 고딕", 10f, FontStyle.Bold);
        Font symFont = new Font("Segoe UI Symbol", 11f);
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                string key = Keys_[r, c];
                FlatButton b = new FlatButton(Labels[r, c]);
                b.Tag = key;
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(1);
                b.Radius = 0;
                b.Font = keyFont;
                if (key == "=")
                {
                    b.Base = Theme.Equal; b.Hover = Theme.EqualHover; b.ForeColor = Color.White;
                }
                else if (r == 0 || c == 3)
                {
                    // 윗줄(C ⌫ ÷ √)과 오른쪽 연산자 열은 회색
                    b.Base = Theme.KeyGray; b.Hover = Theme.KeyGrayHover;
                    b.ForeColor = (key == "C" || key == "BS") ? Theme.Text : Theme.OpText;
                    if (key == "BS") b.Font = symFont;
                    if (key == "C") b.Font = korFont;
                }
                else
                {
                    b.Base = Color.White; b.Hover = Theme.KeyWhiteHover;
                }
                b.Pressed += OnButton;
                grid.Controls.Add(b, c, r);
            }
        }

        Controls.Add(grid);
        Controls.Add(display);
        grid.BringToFront();
        Refresh_();
    }

    void OnButton(object sender, EventArgs e)
    {
        display.Focus();   // 메모장에 있던 키보드 입력을 계산기로 가져옴
        Press((string)((Control)sender).Tag);
    }

    public void Press(string k)
    {
        eng.Press(k);
        Refresh_();
    }

    void Refresh_()
    {
        display.Expr = eng.Expr;
        display.Value = eng.Display;
        display.Invalidate();
    }
}

// ───────────── 메인 창 ─────────────

class MainForm : Form
{
    const double CountdownMinutes = 15;   // 역산 시간(분)
    const string PlayGlyph = "", PauseGlyph = "";

    readonly Stopwatch sw = new Stopwatch();
    readonly Timer tick = new Timer();
    readonly ToolTip tip = new ToolTip();
    readonly Panel bar;
    readonly Label timeLabel;
    readonly FlatButton playBtn, resetBtn, pinBtn;
    readonly MemoPanel memo;
    readonly CalcPanel calc;
    readonly SplitContainer split;
    int calcHeight;
    bool startDraw = true;
    bool restoredBounds;
    bool finished;           // 역산이 0에 도달
    bool numpadOn = true;    // 숫자패드 고정 (저장됨)

    public MainForm()
    {
        Text = "SKCT 모의고사 도구";
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        MinimumSize = new Size(Theme.Px(300), Theme.Px(560));
        TopMost = true;

        // 타이머 바
        bar = new Panel();
        bar.Dock = DockStyle.Top;
        bar.Height = Theme.Px(64);
        bar.BackColor = Theme.Bar;
        timeLabel = new Label();
        timeLabel.AutoSize = false;
        timeLabel.Font = new Font("Segoe UI Semibold", 30f);
        timeLabel.ForeColor = Theme.Text;
        timeLabel.BackColor = Theme.Bar;
        timeLabel.TextAlign = ContentAlignment.MiddleLeft;
        playBtn = IconButton(PlayGlyph);
        playBtn.Base = Theme.Accent; playBtn.Hover = Theme.AccentHover; playBtn.ForeColor = Color.White;
        resetBtn = IconButton("");
        pinBtn = IconButton("");
        playBtn.Pressed += delegate { TogglePlay(); };
        resetBtn.Pressed += delegate { ResetTime(); };
        pinBtn.Pressed += delegate { TopMost = !TopMost; pinBtn.Active = TopMost; };
        tip.SetToolTip(playBtn, "시작 / 일시정지");
        tip.SetToolTip(resetBtn, CountdownMinutes + "분으로 초기화");
        tip.SetToolTip(pinBtn, "항상 위에 표시");
        bar.Controls.Add(timeLabel);
        bar.Controls.Add(playBtn);
        bar.Controls.Add(resetBtn);
        bar.Controls.Add(pinBtn);
        bar.Resize += delegate { LayoutBar(bar); };

        memo = new MemoPanel();
        memo.Dock = DockStyle.Fill;
        calc = new CalcPanel();
        calc.Dock = DockStyle.Fill;
        calc.NumpadButton.Pressed += delegate { numpadOn = hook == IntPtr.Zero; SetNumpadHook(numpadOn); };
        tip.SetToolTip(calc.NumpadButton, "켜면 다른 창을 보고 있어도 숫자패드, Backspace, Esc 입력은 계산기로 들어갑니다 (NumLock 켜짐 필요)");
        split = new SplitContainer();
        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Horizontal;
        split.FixedPanel = FixedPanel.Panel2;
        split.SplitterWidth = Theme.Px(16);
        split.BackColor = Theme.Panel;   // 경계선이 막대가 아니라 여백으로 보이게
        split.TabStop = false;
        split.Panel1.Controls.Add(memo);
        split.Panel2.Controls.Add(calc);

        // 오른쪽 패널: 회색 바탕 + 여백 (시험 화면의 메모장/계산기 영역)
        Panel content = new Panel();
        content.Dock = DockStyle.Fill;
        content.BackColor = Theme.Panel;
        content.Padding = new Padding(Theme.Px(16));
        content.Controls.Add(split);
        Panel line = new Panel();
        line.Dock = DockStyle.Top;
        line.Height = 1;
        line.BackColor = Theme.Border;

        Controls.Add(content);
        Controls.Add(line);
        Controls.Add(bar);
        content.BringToFront();

        tick.Interval = 200;
        tick.Tick += delegate { UpdateTime(); };

        LoadSettings();
        UpdateTime();
    }

    static FlatButton IconButton(string glyph)
    {
        FlatButton b = new FlatButton(glyph);
        b.Font = new Font("Segoe MDL2 Assets", 9f);
        b.Base = Color.White;
        b.Hover = Theme.BtnHover;
        return b;
    }

    // 타이머 버튼은 작은 원형으로 (아래 사각형 버튼들과 헷갈리지 않게)
    void LayoutBar(Panel bar)
    {
        int s = Theme.Px(28), gap = Theme.Px(6), y = (bar.Height - s) / 2;
        pinBtn.SetBounds(bar.Width - Theme.Px(14) - s, y, s, s);
        resetBtn.SetBounds(pinBtn.Left - gap - s, y, s, s);
        playBtn.SetBounds(resetBtn.Left - gap - s, y, s, s);
        pinBtn.Radius = resetBtn.Radius = playBtn.Radius = s / 2;
        timeLabel.SetBounds(Theme.Px(10), 0, Math.Max(0, playBtn.Left - Theme.Px(18)), bar.Height);
    }

    void TogglePlay()
    {
        if (sw.IsRunning) { sw.Stop(); tick.Stop(); }
        else
        {
            if (finished) { sw.Reset(); finished = false; }   // 0에서 다시 누르면 15:00부터 새로 시작
            sw.Start(); tick.Start();
        }
        playBtn.Text = sw.IsRunning ? PauseGlyph : PlayGlyph;
        UpdateTime();
    }

    // 초기화: 돌아가던 중이면 처음부터 계속 진행, 멈춰 있으면 처음 값에서 대기
    void ResetTime()
    {
        finished = false;
        if (sw.IsRunning) sw.Restart(); else sw.Reset();
        UpdateTime();
    }

    void UpdateTime()
    {
        TimeSpan left = TimeSpan.FromMinutes(CountdownMinutes) - sw.Elapsed;
        if (left <= TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
            if (sw.IsRunning) { sw.Stop(); tick.Stop(); playBtn.Text = PlayGlyph; }
            finished = true;
        }
        TimeSpan t = TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));

        // 15:00 형식 (역산 시간을 1시간 이상으로 바꾸면 1:00:00 형식)
        string s = t.TotalHours >= 1
            ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
            : string.Format("{0:00}:{1:00}", t.Minutes, t.Seconds);
        if (timeLabel.Text != s) timeLabel.Text = s;

        // 시간 종료: 빨간색으로 표시
        Color fc = finished ? Theme.Red : Theme.Text;
        Color bc = finished ? Theme.RedBack : Theme.Bar;
        if (timeLabel.ForeColor != fc) timeLabel.ForeColor = fc;
        if (bar.BackColor != bc) { bar.BackColor = bc; timeLabel.BackColor = bc; }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            int d = split.Height - split.SplitterWidth - calcHeight;
            split.SplitterDistance = Math.Max(Theme.Px(100), d);
        }
        catch { }
        try { split.Panel1MinSize = Theme.Px(100); } catch { }
        try { split.Panel2MinSize = Theme.Px(300); } catch { }

        // 처음 실행: 그림판 칸을 화면 전체 높이 기준의 절반으로 (위아래 25%씩 줄이고 세로 가운데)
        if (!restoredBounds)
        {
            int cut = Math.Max(0, (split.Panel1.Height - Theme.Px(MemoPanel.HeaderHeight)) / 2);
            SetBounds(Left, Top + cut / 2, Width, Height - cut);
        }
        pinBtn.Active = TopMost;
        memo.SetMode(startDraw);
        SetNumpadHook(numpadOn);
    }

    // ── 숫자패드 고정: 다른 창이 활성이어도 숫자패드 키는 계산기로 ──

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

    LowLevelKeyboardProc hookProc;   // GC에 수거되지 않게 참조 유지
    IntPtr hook = IntPtr.Zero;

    void SetNumpadHook(bool on)
    {
        if (on && hook == IntPtr.Zero)
        {
            hookProc = HookCallback;
            hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, hookProc, GetModuleHandle(null), 0);
        }
        else if (!on && hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
        calc.NumpadButton.Active = hook != IntPtr.Zero;
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && WindowState != FormWindowState.Minimized)
        {
            KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            string key = HookKey((int)k.vkCode, (int)k.flags);
            bool alt = (k.flags & 0x20) != 0;
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            // 이 창의 메모장에 타이핑 중이면 Backspace와 Esc는 메모장이 받게 둠
            bool editKey = k.vkCode == 0x08 || k.vkCode == 0x1B;
            bool memoTyping = GetForegroundWindow() == Handle && memo.Typing;
            // Alt+숫자패드(특수문자 입력), Ctrl 조합은 그대로 통과
            if (key != null && !alt && !ctrl && !(editKey && memoTyping))
            {
                int msg = wParam.ToInt32();
                if (msg == 0x0100 || msg == 0x0104) calc.Press(key);   // 눌림에서만 입력
                return (IntPtr)1;   // 다른 창으로 보내지 않음 (떼기 포함)
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    // 가로채는 키: NumLock이 켜진 숫자패드 키 + Backspace, Esc. 일반 숫자열과 메인 Enter는 제외
    public static string HookKey(int vk, int flags)
    {
        if (vk >= 0x60 && vk <= 0x69) return ((char)('0' + vk - 0x60)).ToString();
        switch (vk)
        {
            case 0x6A: return "*";
            case 0x6B: return "+";
            case 0x6D: return "-";
            case 0x6E: return ".";
            case 0x6F: return "/";
            case 0x0D: return (flags & 0x01) != 0 ? "=" : null;   // 확장 플래그 = 숫자패드 Enter
            case 0x08: return "BS";   // Backspace
            case 0x1B: return "C";    // Esc
        }
        return null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SetNumpadHook(false);
        base.OnFormClosed(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        memo.FocusCurrent();
    }

    // 메모장에 타이핑 중이 아니면 키보드 입력은 계산기로
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!memo.Typing)
        {
            string k = MapKey(keyData);
            if (k != null) { calc.Press(k); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    static string MapKey(Keys kd)
    {
        Keys key = kd & Keys.KeyCode;
        Keys mod = kd & Keys.Modifiers;
        bool shift = mod == Keys.Shift;
        if (mod != Keys.None && !shift) return null;

        if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return ((char)('0' + (key - Keys.NumPad0))).ToString();
        if (!shift && key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
        if (shift && key == Keys.D8) return "*";
        switch (key)
        {
            case Keys.Add: return "+";
            case Keys.Subtract: return "-";
            case Keys.Multiply: return "*";
            case Keys.Divide: return "/";
            case Keys.Decimal: return ".";
            case Keys.OemPeriod: return shift ? null : ".";
            case Keys.Oemplus: return shift ? "+" : "=";
            case Keys.OemMinus: return shift ? null : "-";
            case Keys.OemQuestion: return shift ? null : "/";
            case Keys.Return: return "=";
            case Keys.Back: return "BS";
            case Keys.Escape: return "C";
        }
        return null;
    }

    // ── 창 위치/크기 기억 ──

    static string SettingsFile
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SKCTAddon");
            return Path.Combine(dir, "settings.txt");
        }
    }

    void LoadSettings()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        int w = Theme.Px(420);
        Rectangle bounds = new Rectangle(wa.Right - w, wa.Top, w, wa.Height);   // 기본: 화면 오른쪽에 세로로
        calcHeight = Theme.Px(400);
        try
        {
            if (File.Exists(SettingsFile))
            {
                Dictionary<string, string> d = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                }
                string v;
                // v2 이전(그림판 칸이 크던 버전)에 저장된 창 크기는 무시
                if (d.TryGetValue("v", out v) && v == "2" && d.TryGetValue("bounds", out v))
                {
                    string[] a = v.Split(',');
                    Rectangle r = new Rectangle(int.Parse(a[0]), int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                    if (OnScreen(r)) { bounds = r; restoredBounds = true; }
                }
                if (d.TryGetValue("calc", out v)) calcHeight = int.Parse(v);
                if (d.TryGetValue("topmost", out v)) TopMost = v == "1";
                if (d.TryGetValue("mode", out v)) startDraw = v != "text";
                if (d.TryGetValue("numpad", out v)) numpadOn = v != "0";
            }
        }
        catch { }
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
    }

    static bool OnScreen(Rectangle r)
    {
        foreach (Screen s in Screen.AllScreens)
        {
            Rectangle inter = Rectangle.Intersect(s.WorkingArea, r);
            if (inter.Width >= Theme.Px(100) && inter.Height >= Theme.Px(100)) return true;
        }
        return false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        try
        {
            Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            int ch = split.Height - split.SplitterDistance - split.SplitterWidth;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
            File.WriteAllLines(SettingsFile, new string[] {
                "v=2",
                "bounds=" + b.X + "," + b.Y + "," + b.Width + "," + b.Height,
                "calc=" + ch,
                "topmost=" + (TopMost ? "1" : "0"),
                "mode=" + (memo.DrawMode ? "draw" : "text"),
                "numpad=" + (numpadOn ? "1" : "0") });
        }
        catch { }
    }
}

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        try { SetProcessDPIAware(); } catch { }
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Theme.S = g.DpiX / 96f;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

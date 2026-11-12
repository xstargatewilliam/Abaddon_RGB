using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace AbaddonRGB;

/// <summary>Painel que desenha o pedaço certo da imagem de fundo da janela.</summary>
internal class BackdropPanel : Panel
{
    private static Bitmap? _scaled;

    public BackdropPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    /// <summary>Desenha o fundo cósmico num controle, alinhado com a janela.</summary>
    public static void Draw(Control control, Graphics graphics)
    {
        var form = control.FindForm();
        var source = Ui.Asset("fundo.jpg");
        if (form is null || source is null || form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0)
        {
            graphics.Clear(Ui.Background);
            return;
        }

        if (_scaled is null || _scaled.Size != form.ClientSize)
        {
            _scaled?.Dispose();
            _scaled = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(_scaled);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(Point.Empty, form.ClientSize));
        }

        var offset = ReferenceEquals(control, form)
            ? Point.Empty
            : form.PointToClient(control.PointToScreen(Point.Empty));
        graphics.DrawImage(_scaled, -offset.X, -offset.Y);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => Draw(this, e.Graphics);
}

/// <summary>Cabeçalho da página: título grande sobre o fundo.</summary>
internal sealed class HeaderPanel : BackdropPanel
{
    private string _title = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set { _title = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var titleFont = Ui.Heading(19f, bold: true);
        using var subFont = Ui.Body(9f);
        using var text = new SolidBrush(Ui.Text);
        using var muted = new SolidBrush(Ui.Muted);

        g.DrawString(_title.ToUpperInvariant(), titleFont, text, 0, Height / 2f - titleFont.GetHeight(g) / 2f);

        var size = g.MeasureString(Subtitle, subFont);
        g.DrawString(Subtitle, subFont, muted, Width - size.Width - 2, Height / 2f - size.Height / 2f);
    }
}

/// <summary>Cartão do guia de estilo: fundo de painel, borda de 1 px, cantos chanfrados e título.</summary>
internal sealed class Card : Panel
{
    private const int Cut = 10;
    private FlowLayoutPanel? _body;

    public Card(string title, int x, int y, int width, int height)
    {
        Title = title;
        Bounds = new Rectangle(x, y, width, height);
        BackColor = Ui.Panel;
        ForeColor = Ui.Text;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; }

    /// <summary>Área de conteúdo em coluna, abaixo do título.</summary>
    public FlowLayoutPanel Body
    {
        get
        {
            if (_body is null)
            {
                _body = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor = Ui.Panel,
                    Location = new Point(16, 42),
                    Size = new Size(Width - 32, Height - 54),
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                };
                Controls.Add(_body);
            }

            return _body;
        }
    }

    /// <summary>Largura útil para o conteúdo.</summary>
    public int InnerWidth => Width - 32;

    private Point[] Outline(int inset)
    {
        var w = Width - 1 - inset;
        var h = Height - 1 - inset;
        return new[]
        {
            new Point(inset + Cut, inset), new Point(w, inset), new Point(w, h - Cut),
            new Point(w - Cut, h), new Point(inset, h), new Point(inset, inset + Cut),
        };
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= Cut * 2 || Height <= Cut * 2) return;

        // Recorta os cantos chanfrados para o fundo aparecer atrás deles.
        using var path = new GraphicsPath();
        var w = Width;
        var h = Height;
        path.AddPolygon(new[]
        {
            new Point(Cut, 0), new Point(w, 0), new Point(w, h - Cut),
            new Point(w - Cut, h), new Point(0, h), new Point(0, Cut),
        });
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var border = new Pen(Ui.Border, 1f))
            g.DrawPolygon(border, Outline(0));

        if (Title.Length == 0) return;

        // Marcador inclinado em magenta + título em Oxanium.
        using (var accent = new SolidBrush(Ui.Accent2))
            g.FillPolygon(accent, new[] { new Point(20, 16), new Point(26, 16), new Point(22, 28), new Point(16, 28) });

        using var font = Ui.Heading(10.5f);
        using var text = new SolidBrush(Ui.Text);
        g.DrawString(Title.ToUpperInvariant(), font, text, 32, 13);
    }
}

/// <summary>Botão do guia de estilo: primário roxo ou secundário com borda; brilho magenta ao passar o mouse.</summary>
internal sealed class NeonButton : Button
{
    private bool _hover;
    private bool _down;

    public NeonButton()
    {
        AutoSize = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Ui.Body(9.5f, semibold: true);
        ForeColor = Ui.Text;
        BackColor = Ui.Panel;
        Padding = new Padding(10, 5, 10, 5);
        Cursor = Cursors.Hand;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        g.Clear(Parent?.BackColor ?? Ui.Panel);

        Color fill;
        if (Primary) fill = _down ? Color.FromArgb(120, 20, 190) : _hover ? Color.FromArgb(178, 54, 248) : Ui.Accent;
        else fill = _down ? Color.FromArgb(40, 28, 62) : _hover ? Color.FromArgb(30, 22, 48) : Ui.Panel;

        using (var brush = new SolidBrush(fill))
            g.FillRectangle(brush, bounds);

        var borderColor = _hover || Focused ? Ui.Accent2 : Primary ? Color.FromArgb(196, 110, 255) : Ui.Border;
        using (var pen = new Pen(borderColor, 1f))
            g.DrawRectangle(pen, bounds);

        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), Enabled ? Ui.Text : Ui.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>Item do menu lateral: ícone + nome, com destaque quando é a página atual.</summary>
internal sealed class NavButton : Control
{
    private bool _active;
    private bool _hover;

    public NavButton(string text, Image? icon)
    {
        Text = text;
        Icon = icon;
        Size = new Size(196, 46);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        TabStop = false;
    }

    private Image? Icon { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Sidebar);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        if (_active)
        {
            using var fill = new LinearGradientBrush(ClientRectangle, Color.FromArgb(110, Ui.Accent), Color.FromArgb(0, Ui.Accent), 0f);
            g.FillRectangle(fill, ClientRectangle);
            using var bar = new SolidBrush(Ui.Accent2);
            g.FillRectangle(bar, 0, 0, 3, Height);
        }
        else if (_hover)
        {
            using var fill = new SolidBrush(Color.FromArgb(26, 18, 42));
            g.FillRectangle(fill, ClientRectangle);
        }

        var iconSize = 22;
        if (Icon is not null)
        {
            using var attributes = new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = _active || _hover ? 1f : 0.62f };
            attributes.SetColorMatrix(matrix);
            g.DrawImage(Icon, new Rectangle(20, (Height - iconSize) / 2, iconSize, iconSize),
                0, 0, Icon.Width, Icon.Height, GraphicsUnit.Pixel, attributes);
        }

        using var font = Ui.Heading(10f);
        using var brush = new SolidBrush(_active || _hover ? Ui.Text : Ui.Muted);
        g.DrawString(Text.ToUpperInvariant(), font, brush, 56, Height / 2f - font.GetHeight(g) / 2f);
    }
}

/// <summary>Indicador de conexão no rodapé do menu lateral.</summary>
internal sealed class StatusPill : Control
{
    private bool _ok;
    private string _detail = "";

    public StatusPill()
    {
        Size = new Size(172, 46);
        DoubleBuffered = true;
        TabStop = false;
    }

    public void Set(bool ok, string detail)
    {
        _ok = ok;
        _detail = detail;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Sidebar);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var color = _ok ? Ui.Success : Ui.Error;
        using (var back = new SolidBrush(Ui.Panel))
            g.FillRectangle(back, 0, 0, Width - 1, Height - 1);
        using (var pen = new Pen(Color.FromArgb(150, color), 1f))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        using (var dot = new SolidBrush(color))
            g.FillEllipse(dot, 12, Height / 2 - 5, 10, 10);

        using var font = Ui.Heading(8.5f);
        using var small = Ui.Body(8f);
        using var brush = new SolidBrush(color);
        using var muted = new SolidBrush(Ui.Muted);
        g.DrawString(_ok ? "CONECTADO" : "DESCONECTADO", font, brush, 30, 7);
        g.DrawString(_detail, small, muted, 30, 24);
    }
}

/// <summary>Roda de cores: o ângulo escolhe o tom e a distância do centro, a saturação.</summary>
internal sealed class ColorWheel : Control
{
    private Bitmap? _disc;
    private double _hue;
    private double _saturation;
    private bool _dragging;

    public ColorWheel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Cursor = Cursors.Cross;
        TabStop = false;
    }

    /// <summary>Disparado enquanto a pessoa arrasta.</summary>
    public event EventHandler? ColorChanging;

    /// <summary>Disparado quando a pessoa solta o mouse.</summary>
    public event EventHandler? ColorCommitted;

    /// <summary>A cor escolhida, sempre com luminosidade máxima (o brilho tem controle próprio).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SelectedColor => FromHsv(_hue, _saturation);

    /// <summary>Move o marcador para o tom e a saturação de uma cor, sem disparar eventos.</summary>
    public void ShowColor(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double hue = 0;
        if (delta > 0.0001)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }

        if (hue < 0) hue += 360;
        _hue = hue;
        _saturation = max <= 0.0001 ? 0 : delta / max;
        Invalidate();
    }

    private static Color FromHsv(double hue, double saturation)
    {
        var c = saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = 1 - c;
        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromArgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static int ToByte(double value) => Ui.Clamp((int)Math.Round(value * 255), 0, 255);

    private int Diameter => Math.Max(Math.Min(Width, Height) - 8, 8);

    private void BuildDisc()
    {
        var d = Diameter;
        if (_disc is not null && _disc.Width == d) return;

        _disc?.Dispose();
        _disc = new Bitmap(d, d, PixelFormat.Format32bppArgb);
        var radius = d / 2.0;
        for (var y = 0; y < d; y++)
        {
            for (var x = 0; x < d; x++)
            {
                var dx = x + 0.5 - radius;
                var dy = y + 0.5 - radius;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > radius) continue;

                var hue = Math.Atan2(dy, dx) * 180 / Math.PI;
                if (hue < 0) hue += 360;
                var color = FromHsv(hue, Math.Min(distance / radius, 1));
                var alpha = Ui.Clamp((int)((radius - distance) * 255), 0, 255);
                _disc.SetPixel(x, y, Color.FromArgb(alpha, color));
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Ui.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        BuildDisc();
        if (_disc is null) return;

        var d = Diameter;
        var left = (Width - d) / 2;
        var top = (Height - d) / 2;

        // Halo suave em volta da roda, na cor escolhida.
        using (var halo = new Pen(Color.FromArgb(70, SelectedColor), 6f))
            g.DrawEllipse(halo, left - 1, top - 1, d + 2, d + 2);
        g.DrawImage(_disc, left, top, d, d);
        using (var rim = new Pen(Ui.Border, 1f))
            g.DrawEllipse(rim, left, top, d - 1, d - 1);

        var radius = d / 2.0;
        var angle = _hue * Math.PI / 180;
        var mx = (float)(left + radius + Math.Cos(angle) * _saturation * (radius - 2));
        var my = (float)(top + radius + Math.Sin(angle) * _saturation * (radius - 2));
        using (var fill = new SolidBrush(SelectedColor))
            g.FillEllipse(fill, mx - 8, my - 8, 16, 16);
        using (var dark = new Pen(Color.FromArgb(200, 8, 6, 15), 3f))
            g.DrawEllipse(dark, mx - 8, my - 8, 16, 16);
        using (var white = new Pen(Color.White, 1.5f))
            g.DrawEllipse(white, mx - 8, my - 8, 16, 16);
    }

    private void Pick(Point point)
    {
        var d = Diameter;
        var radius = d / 2.0;
        var dx = point.X - (Width - d) / 2.0 - radius;
        var dy = point.Y - (Height - d) / 2.0 - radius;

        var hue = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (hue < 0) hue += 360;
        _hue = hue;
        _saturation = Math.Min(Math.Sqrt(dx * dx + dy * dy) / radius, 1);
        Invalidate();
        ColorChanging?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        Capture = true;
        Pick(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) Pick(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        ColorCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _disc?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Controle deslizante de 0 a 100, com trilho em degradê roxo → magenta.</summary>
internal sealed class NeonSlider : Control
{
    private int _value = 100;
    private bool _dragging;

    public NeonSlider()
    {
        Size = new Size(200, 26);
        DoubleBuffered = true;
        ResizeRedraw = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public event EventHandler? ValueChanged;

    /// <summary>Disparado quando a pessoa solta o controle (mouse ou teclado).</summary>
    public event EventHandler? Committed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Ui.Clamp(value, 0, 100);
            if (clamped == _value) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private const int Knob = 16;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Ui.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var trackLeft = Knob / 2;
        var trackWidth = Width - Knob;
        var y = Height / 2;
        var filled = (int)Math.Round(trackWidth * _value / 100.0);

        using (var back = new Pen(Color.FromArgb(46, 34, 66), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(back, trackLeft, y, trackLeft + trackWidth, y);

        if (filled > 1)
        {
            using var brush = new LinearGradientBrush(new Rectangle(trackLeft - 1, y - 4, trackWidth + 2, 8), Ui.Accent, Ui.Accent2, 0f);
            using var pen = new Pen(brush, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, trackLeft, y, trackLeft + filled, y);
        }

        var cx = trackLeft + filled;
        using (var glow = new SolidBrush(Color.FromArgb(60, Ui.Accent2)))
            g.FillEllipse(glow, cx - Knob / 2 - 3, y - Knob / 2 - 3, Knob + 6, Knob + 6);
        using (var knob = new SolidBrush(Ui.Text))
            g.FillEllipse(knob, cx - Knob / 2, y - Knob / 2, Knob, Knob);
        using (var ring = new Pen(Focused ? Ui.Accent2 : Ui.Accent, 2f))
            g.DrawEllipse(ring, cx - Knob / 2, y - Knob / 2, Knob, Knob);
    }

    private void SetFromMouse(int x)
    {
        var trackWidth = Math.Max(Width - Knob, 1);
        Value = (int)Math.Round((x - Knob / 2.0) * 100 / trackWidth);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        Capture = false;
        Committed?.Invoke(this, EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) Value -= 5;
        else if (e.KeyCode == Keys.Right) Value += 5;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
            Committed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

/// <summary>Ilustração do gabinete (setup branco) com as luzes pintadas na cor atual.</summary>
internal sealed class PcPreview : Control
{
    private Color _light = Color.White;
    private int _brightness = 100;

    public PcPreview()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = false;
    }

    public void SetLight(Color color, int brightness)
    {
        if (_light == color && _brightness == brightness) return;
        _light = color;
        _brightness = Ui.Clamp(brightness, 0, 100);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Ui.Panel);
        var image = Ui.Asset("gabinete.png");
        if (image is null) return;

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var side = Math.Min(Width, Height);
        var target = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);

        var k = _brightness / 100f;

        // Camada 1: o gabinete branco, levemente banhado pela cor (como a luz refletindo nele).
        float Ambient(int value) => 0.62f + 0.24f * (value / 255f) * k;
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(new ColorMatrix
            {
                Matrix00 = Ambient(_light.R),
                Matrix11 = Ambient(_light.G),
                Matrix22 = Ambient(_light.B),
            });
            g.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }

        // Camada 2: só as luzes (anéis das fans, cooler e memórias), na cor e no brilho atuais.
        var lights = Ui.Asset("gabinete_luz.png");
        if (lights is null || k <= 0f) return;
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(new ColorMatrix
            {
                Matrix00 = _light.R / 255f,
                Matrix11 = _light.G / 255f,
                Matrix22 = _light.B / 255f,
                Matrix33 = k,
            });
            g.DrawImage(lights, target, 0, 0, lights.Width, lights.Height, GraphicsUnit.Pixel, attributes);
        }
    }
}

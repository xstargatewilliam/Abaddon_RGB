namespace AbaddonRGB;

/// <summary>Cores e fábricas de controles do tema escuro.</summary>
internal static class Ui
{
    public static readonly Color Background = Color.FromArgb(30, 30, 36);
    public static readonly Color Surface = Color.FromArgb(42, 42, 50);
    public static readonly Color Border = Color.FromArgb(70, 70, 82);
    public static readonly Color Accent = Color.FromArgb(58, 110, 165);
    public static readonly Color Text = Color.FromArgb(235, 235, 240);
    public static readonly Color Muted = Color.FromArgb(160, 160, 175);

    public const int ContentWidth = 500;

    public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    public static Button Button(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = Text,
            Padding = new Padding(6, 3, 6, 3),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Border;
        return button;
    }

    /// <summary>Botão quadrado que mostra uma cor.</summary>
    public static Button Swatch(Color color, int size = 30)
    {
        var button = new Button
        {
            Size = new Size(size, size),
            BackColor = color,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 6, 6),
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 2;
        return button;
    }

    public static Label Label(string text, bool muted = false) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = muted ? Muted : Text,
        MaximumSize = new Size(ContentWidth, 0),
        Margin = new Padding(0, 0, 0, 2),
    };

    /// <summary>Rótulo que fica alinhado com botões e caixas numa mesma linha.</summary>
    public static Label InlineLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Text,
        Margin = new Padding(0, 7, 6, 0),
    };

    public static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Text,
        Font = new Font("Segoe UI Semibold", 11f),
        Margin = new Padding(0, 6, 0, 4),
    };

    public static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Text,
        Margin = new Padding(0, 0, 0, 6),
    };

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };
        row.Controls.AddRange(controls);
        return row;
    }

    public static FlowLayoutPanel Page()
    {
        return new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Dock = DockStyle.Fill,
            Visible = false,
        };
    }

    public static ComboBox Combo(int width) => new()
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Surface,
        ForeColor = Text,
        Margin = new Padding(0, 3, 8, 0),
    };

    public static NumericUpDown Number(int min, int max, int value, int width = 52) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Clamp(value, min, max),
        Width = width,
        BackColor = Surface,
        ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0, 4, 6, 0),
    };

    public static ListBox List(int height) => new()
    {
        Width = ContentWidth - 10,
        Height = height,
        BackColor = Surface,
        ForeColor = Text,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        Margin = new Padding(0, 2, 0, 8),
    };

    public static Color ToDrawing(Core.RgbColor color) => Color.FromArgb(color.R, color.G, color.B);

    public static Core.RgbColor FromDrawing(Color color) => new(color.R, color.G, color.B);
}

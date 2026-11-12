using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AbaddonRGB;

/// <summary>Tema do Abaddon RGB: paleta do guia de estilo, fontes, imagens e fábricas de controles.</summary>
internal static class Ui
{
    // Paleta oficial (Design System v1.0)
    public static readonly Color Background = ColorTranslator.FromHtml("#08060F");
    public static readonly Color Panel = ColorTranslator.FromHtml("#141020");
    public static readonly Color Border = ColorTranslator.FromHtml("#392650");
    public static readonly Color Accent = ColorTranslator.FromHtml("#A020F0");
    public static readonly Color Accent2 = ColorTranslator.FromHtml("#FF2DAA");
    public static readonly Color Text = ColorTranslator.FromHtml("#F4F0FA");
    public static readonly Color Muted = ColorTranslator.FromHtml("#A69AB8");
    public static readonly Color Success = ColorTranslator.FromHtml("#27E6A2");
    public static readonly Color Error = ColorTranslator.FromHtml("#FF4D73");

    /// <summary>Fundo de caixas de texto e listas: um pouco mais escuro que o painel.</summary>
    public static readonly Color Field = ColorTranslator.FromHtml("#0D0A17");
    public static readonly Color Sidebar = ColorTranslator.FromHtml("#0B0814");

    private static readonly PrivateFontCollection Fonts = new();
    private static readonly Dictionary<string, Image> Images = new();
    private static FontFamily? _heading;
    private static FontStyle _headingStyle = FontStyle.Regular;
    private static string? _installedHeading;
    private static bool _fontsLoaded;

    public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    // ---------------------------------------------------------------- recursos embutidos

    private static Stream? Open(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream("AbaddonRGB.Assets." + name);

    /// <summary>Carrega uma imagem embutida no programa (fica em memória para reutilizar).</summary>
    public static Image? Asset(string name)
    {
        if (Images.TryGetValue(name, out var cached)) return cached;
        try
        {
            using var stream = Open(name);
            if (stream is null) return null;
            // Copia para a memória: o GDI+ precisa do stream aberto enquanto a imagem existir.
            var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;
            var image = Image.FromStream(memory);
            Images[name] = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static Image? Icon(string name) => Asset("ic_" + name + ".png");

    private static void LoadFonts()
    {
        if (_fontsLoaded) return;
        _fontsLoaded = true;

        foreach (var file in new[] { "Oxanium-SemiBold.ttf" })
        {
            try
            {
                using var stream = Open(file);
                if (stream is null) continue;
                var bytes = new byte[stream.Length];
                var read = 0;
                while (read < bytes.Length)
                {
                    var n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }

                var pointer = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                Fonts.AddMemoryFont(pointer, bytes.Length);
                // A memória precisa continuar viva enquanto a fonte for usada: não liberamos.
            }
            catch
            {
                // Sem a fonte, os títulos usam a fonte padrão do Windows.
            }
        }

        try
        {
            // Confere se a fonte embutida realmente funciona antes de adotá-la.
            var family = Fonts.Families.FirstOrDefault();
            if (family is not null)
            {
                _headingStyle = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
                using var probe = new Font(family, 10f, _headingStyle);
                probe.GetHeight();
                _heading = family;
            }
        }
        catch
        {
            _heading = null;
        }

        if (_heading is null)
        {
            // Segunda chance: a Oxanium instalada no Windows.
            try
            {
                using var installed = new Font("Oxanium SemiBold", 10f);
                if (installed.Name.StartsWith("Oxanium", StringComparison.OrdinalIgnoreCase)) _installedHeading = installed.Name;
            }
            catch { }
        }
    }

    /// <summary>Fonte dos títulos (Oxanium). Usar só em texto desenhado com Graphics.DrawString.</summary>
    public static Font Heading(float size, bool bold = false)
    {
        LoadFonts();
        if (_heading is not null)
        {
            try { return new Font(_heading, size, _headingStyle); }
            catch { }
        }

        if (_installedHeading is not null) return new Font(_installedHeading, size);

        return new Font("Segoe UI Semibold", size);
    }

    /// <summary>Fonte dos textos e controles.</summary>
    public static Font Body(float size = 9.75f, bool semibold = false) =>
        new(semibold ? "Segoe UI Semibold" : "Segoe UI", size);

    // ---------------------------------------------------------------- fábricas de controles

    public static NeonButton Button(string text, bool primary = false) => new()
    {
        Text = text,
        Primary = primary,
        Margin = new Padding(0, 0, 8, 0),
    };

    /// <summary>Botão quadrado que mostra uma cor.</summary>
    public static Button Swatch(Color color, int size = 30)
    {
        var button = new Button
        {
            Size = new Size(size, size),
            BackColor = color,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 6, 0),
            TabStop = false,
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = color;
        button.FlatAppearance.MouseDownBackColor = color;
        return button;
    }

    public static Label Label(string text, bool muted = false, int maxWidth = 0) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = muted ? Muted : Text,
        MaximumSize = new Size(maxWidth, 0),
        Margin = new Padding(0, 0, 0, 4),
    };

    /// <summary>Rótulo que fica alinhado com botões e caixas numa mesma linha.</summary>
    public static Label InlineLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Text,
        Margin = new Padding(0, 8, 6, 0),
    };

    public static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Text,
        FlatStyle = FlatStyle.Flat,
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

    public static ComboBox Combo(int width) => new()
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Field,
        ForeColor = Text,
        Margin = new Padding(0, 4, 8, 0),
    };

    public static NumericUpDown Number(int min, int max, int value, int width = 52) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Clamp(value, min, max),
        Width = width,
        BackColor = Field,
        ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0, 5, 6, 0),
    };

    public static ListBox List(int width, int height) => DarkScrollbars(new ListBox
    {
        Width = width,
        Height = height,
        BackColor = Field,
        ForeColor = Text,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        Margin = new Padding(0, 2, 0, 10),
    });

    public static TextBox TextBox(int width) => new()
    {
        Width = width,
        BackColor = Field,
        ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0, 5, 8, 0),
    };

    public static Color ToDrawing(Core.RgbColor color) => Color.FromArgb(color.R, color.G, color.B);

    public static Core.RgbColor FromDrawing(Color color) => new(color.R, color.G, color.B);

    /// <summary>Deixa a barra de título da janela escura (Windows 10/11).</summary>
    public static void UseDarkTitleBar(IntPtr handle)
    {
        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        }
        catch
        {
            // Versões antigas do Windows não têm esse ajuste.
        }
    }

    /// <summary>Barras de rolagem escuras em listas, árvores e caixas de texto (Windows 10/11).</summary>
    public static T DarkScrollbars<T>(T control) where T : Control
    {
        void Apply()
        {
            try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
            catch { }
        }

        if (control.IsHandleCreated) Apply();
        control.HandleCreated += (_, _) => Apply();
        return control;
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

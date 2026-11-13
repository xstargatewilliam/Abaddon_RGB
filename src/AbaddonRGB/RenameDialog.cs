namespace AbaddonRGB;

/// <summary>Janelinha para dar um nome a uma peça ou zona.</summary>
internal sealed class RenameDialog : Form
{
    private readonly TextBox _box;

    private RenameDialog(string original, string current)
    {
        Text = "Renomear";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(400, 150);
        BackColor = Ui.Panel;
        ForeColor = Ui.Text;
        Font = Ui.Body();

        var hint = new Label
        {
            Text = $"Nome original: {original}\nDeixe em branco para voltar ao nome original.",
            ForeColor = Ui.Muted,
            Bounds = new Rectangle(16, 12, 368, 40),
        };

        _box = Ui.TextBox(368);
        _box.Location = new Point(16, 58);
        _box.MaxLength = 40;
        _box.Text = current;

        var ok = Ui.Button("Salvar", primary: true);
        ok.AutoSize = false;
        ok.Bounds = new Rectangle(196, 100, 90, 32);
        ok.DialogResult = DialogResult.OK;

        var cancel = Ui.Button("Cancelar");
        cancel.AutoSize = false;
        cancel.Bounds = new Rectangle(294, 100, 90, 32);
        cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[] { hint, _box, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        HandleCreated += (_, _) => Ui.UseDarkTitleBar(Handle);
    }

    /// <summary>Retorna o nome digitado ("" = voltar ao original) ou null se a pessoa cancelou.</summary>
    public static string? Ask(IWin32Window owner, string original, string current)
    {
        using var dialog = new RenameDialog(original, current);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._box.Text.Trim() : null;
    }
}

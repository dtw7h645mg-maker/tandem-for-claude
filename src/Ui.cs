using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Tandem
{
    /// <summary>Colours, fonts and small building blocks shared by the windows.</summary>
    static class Ui
    {
        public static float Scale = 1f;
        public static Font Small, NameFont;

        public static readonly Color Bg = ColorTranslator.FromHtml("#F5F4EE");
        public static readonly Color Card = Color.White;
        public static readonly Color Border = ColorTranslator.FromHtml("#DDDAD0");
        public static readonly Color Text = ColorTranslator.FromHtml("#1F1E1D");
        public static readonly Color Muted = ColorTranslator.FromHtml("#6B6A65");
        public static readonly Color Accent = ColorTranslator.FromHtml("#C96442");
        public static readonly Color AccentHover = ColorTranslator.FromHtml("#B5573A");
        public static readonly Color Green = ColorTranslator.FromHtml("#2E9E5B");
        public static readonly Color Idle = ColorTranslator.FromHtml("#C4C1B7");

        /// <summary>Border colours a profile's Claude window can take.</summary>
        public static readonly KeyValuePair<string, string>[] Palette =
        {
            new KeyValuePair<string, string>("Orange", "#E8713A"),
            new KeyValuePair<string, string>("Blue", "#2F7DEB"),
            new KeyValuePair<string, string>("Green", "#2E9E5B"),
            new KeyValuePair<string, string>("Purple", "#8B5CF6"),
            new KeyValuePair<string, string>("Pink", "#E0559A"),
            new KeyValuePair<string, string>("Yellow", "#E0A100")
        };

        public static void Init()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            Small = new Font("Segoe UI", 9f);
            NameFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        }

        public static int Px(double n) { return (int)Math.Round(n * Scale); }

        public static Button FlatButton(string text, int width, bool primary)
        {
            var b = new Button();
            b.Text = text;
            b.Size = new Size(width, Px(32));
            b.FlatStyle = FlatStyle.Flat;
            b.Font = Small;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            if (primary)
            {
                b.BackColor = Accent;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = AccentHover;
                b.FlatAppearance.MouseDownBackColor = AccentHover;
            }
            else
            {
                b.BackColor = Card;
                b.ForeColor = Text;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = Bg;
            }
            return b;
        }

        /// <summary>Asks for one line of text. Returns null when cancelled.</summary>
        public static string Ask(IWin32Window owner, string title, string message, string value)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.Font = Small;
                form.BackColor = Bg;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;
                form.AutoScaleMode = AutoScaleMode.None;

                var label = new Label();
                label.Text = message;
                label.ForeColor = Text;
                label.Location = new Point(Px(16), Px(14));
                label.MaximumSize = new Size(Px(368), 0);
                label.AutoSize = true;

                var box = new TextBox();
                box.Text = value ?? "";
                box.MaxLength = 31;
                box.Width = Px(368);

                Button ok = FlatButton("OK", Px(88), true);
                Button cancel = FlatButton("Cancel", Px(88), false);
                ok.DialogResult = DialogResult.OK;
                cancel.DialogResult = DialogResult.Cancel;

                form.Controls.AddRange(new Control[] { label, box, ok, cancel });
                box.Location = new Point(Px(16), label.Bottom + Px(10));
                cancel.Location = new Point(Px(16) + box.Width - cancel.Width, box.Bottom + Px(14));
                ok.Location = new Point(cancel.Left - Px(8) - ok.Width, cancel.Top);
                form.ClientSize = new Size(Px(400), ok.Bottom + Px(14));
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                box.SelectAll();

                if (form.ShowDialog(owner) != DialogResult.OK) return null;
                return box.Text.Trim();
            }
        }
    }
}

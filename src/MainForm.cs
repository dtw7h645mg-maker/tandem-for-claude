using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tandem
{
    class MainForm : Form
    {
        class Row
        {
            public Profile P;
            public Panel Card;
            public Label Status;
            public Button Main, Stop, More;
            public bool Running;
        }

        public const string ShowEventName = @"Local\TandemForClaude.Show";

        readonly Store store;
        readonly string snapshotPath;
        readonly List<Row> rows = new List<Row>();
        readonly FlowLayoutPanel flow = new FlowLayoutPanel();
        readonly Label note = new Label();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Button addButton, tileButton, settingsButton;

        List<RunningClaude> lastRunning = new List<RunningClaude>();
        DateTime noteHold = DateTime.MinValue;
        string claudeVersion = "";
        bool claudeFound = true;
        bool exiting;

        public MainForm(Store store, string snapshotPath)
        {
            this.store = store;
            this.snapshotPath = snapshotPath;

            Text = AppInfo.Title;
            Font = Ui.Small;
            BackColor = Ui.Bg;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var head = new Label();
            head.Text = "Each profile is its own Claude window with its own login. They can run at the same time.";
            head.ForeColor = Ui.Muted;
            head.AutoSize = true;
            head.Location = new Point(Ui.Px(17), Ui.Px(16));

            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoSize = true;
            flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            flow.Location = new Point(Ui.Px(16), Ui.Px(46));

            addButton = Ui.FlatButton("+  Add a profile", Ui.Px(250), false);
            addButton.Click += delegate { AddProfile(); };
            tileButton = Ui.FlatButton("Side by side", Ui.Px(150), false);
            tileButton.Click += delegate { SideBySide(); };
            settingsButton = Ui.FlatButton("Settings", Ui.Px(104), false);
            settingsButton.Click += delegate { ShowSettingsMenu(); };

            note.ForeColor = Ui.Muted;
            note.AutoSize = false;
            note.Size = new Size(Ui.Px(516), Ui.Px(18));

            Controls.AddRange(new Control[] { head, flow, addButton, tileButton, settingsButton, note });

            try { claudeVersion = ClaudeApp.VersionText(); ClaudeApp.FindExe(); }
            catch { claudeFound = false; }

            BuildRows();
            if (store.Notice != null) SetNote(store.Notice);

            if (snapshotPath != null)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-6000, -6000);
                ShowInTaskbar = false;
                return;
            }

            tray.Icon = Icon;
            tray.Text = AppInfo.Title;
            tray.Visible = store.KeepInTray;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += delegate { BuildTrayMenu(); };

            var listener = new Thread(ListenForShowRequests);
            listener.IsBackground = true;
            listener.Start();
        }

        // ------------------------------------------------------------ rows --

        void BuildRows()
        {
            flow.SuspendLayout();
            while (flow.Controls.Count > 0)
            {
                Control old = flow.Controls[0];
                flow.Controls.RemoveAt(0);
                old.Dispose();
            }
            rows.Clear();
            foreach (Profile p in store.Profiles)
            {
                Row row = NewRow(p);
                rows.Add(row);
                flow.Controls.Add(row.Card);
            }
            flow.ResumeLayout(true);
            ApplyRunning(lastRunning);

            flow.PerformLayout();
            int y = flow.Bottom + Ui.Px(2);
            addButton.Location = new Point(Ui.Px(16), y);
            tileButton.Location = new Point(addButton.Right + Ui.Px(8), y);
            settingsButton.Location = new Point(tileButton.Right + Ui.Px(8), y);
            note.Location = new Point(Ui.Px(18), addButton.Bottom + Ui.Px(9));
            ClientSize = new Size(Ui.Px(552), note.Bottom + Ui.Px(12));
        }

        Row NewRow(Profile p)
        {
            var row = new Row();
            row.P = p;
            int width = Ui.Px(520);

            var card = new Panel();
            card.Size = new Size(width, Ui.Px(66));
            card.BackColor = Ui.Card;
            card.Margin = new Padding(0, 0, 0, Ui.Px(8));
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Ui.Border)) e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                if (!string.IsNullOrEmpty(row.P.Color))
                    using (var bar = new SolidBrush(ColorTranslator.FromHtml(row.P.Color)))
                        e.Graphics.FillRectangle(bar, 0, 0, Ui.Px(5), card.Height);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int d = Ui.Px(11);
                using (var dot = new SolidBrush(row.Running ? Ui.Green : Ui.Idle))
                    e.Graphics.FillEllipse(dot, Ui.Px(19), (card.Height - d) / 2, d, d);
            };

            var name = new Label();
            name.Text = p.Name;
            name.Font = Ui.NameFont;
            name.ForeColor = Ui.Text;
            name.AutoSize = true;
            name.Location = new Point(Ui.Px(42), Ui.Px(12));

            var status = new Label();
            status.ForeColor = Ui.Muted;
            status.AutoSize = true;
            status.Location = new Point(Ui.Px(43), Ui.Px(36));

            Button more = Ui.FlatButton("...", Ui.Px(38), false);
            more.Location = new Point(width - Ui.Px(14) - more.Width, Ui.Px(17));
            Button stop = Ui.FlatButton("Stop", Ui.Px(64), false);
            stop.Location = new Point(more.Left - Ui.Px(8) - stop.Width, Ui.Px(17));
            Button main = Ui.FlatButton("Launch", Ui.Px(120), true);
            main.Location = new Point(stop.Left - Ui.Px(8) - main.Width, Ui.Px(17));

            row.Card = card; row.Status = status; row.Main = main; row.Stop = stop; row.More = more;
            main.Click += delegate { LaunchOrShow(row); };
            stop.Click += delegate { StopProfile(row.P, false); };
            more.Click += delegate { ShowRowMenu(row); };

            card.Controls.AddRange(new Control[] { name, status, main, stop, more });
            return row;
        }

        // --------------------------------------------------------- refresh --

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (snapshotPath != null) { TakeSnapshot(); return; }
            RefreshLoop();
        }

        async void RefreshLoop()
        {
            while (!IsDisposed && !exiting)
            {
                await RefreshOnce();
                await Task.Delay(2500);
            }
        }

        async Task RefreshOnce()
        {
            try
            {
                List<RunningClaude> running = await Task.Run(() => ClaudeApp.GetRunning());
                if (IsDisposed || exiting) return;
                lastRunning = running;
                ApplyRunning(running);
            }
            catch { }
        }

        void ApplyRunning(List<RunningClaude> running)
        {
            int open = 0;
            foreach (Row row in rows)
            {
                RunningClaude run = ClaudeApp.Find(row.P, running);
                bool isRunning = run != null;
                string text = "Not running", button = "Launch";
                if (isRunning)
                {
                    open++;
                    text = run.Sessions == 0 ? "Running"
                         : run.Sessions == 1 ? "Running  -  1 Code session"
                         : "Running  -  " + run.Sessions + " Code sessions";
                    button = "Bring to front";
                    if (!string.IsNullOrEmpty(row.P.Color)) { try { ClaudeApp.PaintAccent(row.P, run); } catch { } }
                }
                bool starting = row.Status.Text == "Starting..." && !isRunning;
                if (!starting && row.Status.Text != text) row.Status.Text = text;
                if (row.Main.Text != button) row.Main.Text = button;
                if (row.Stop.Visible != isRunning) row.Stop.Visible = isRunning;
                if (row.Running != isRunning) { row.Running = isRunning; row.Card.Invalidate(); }
            }
            if (tileButton.Enabled != (open >= 2)) tileButton.Enabled = open >= 2;
            if (DateTime.Now > noteHold)
            {
                string status = claudeFound
                    ? "Claude " + claudeVersion + " found  -  " + open + " of " + rows.Count + " open"
                    : "Claude Desktop was not found. Install it from claude.ai/download and open it once.";
                if (note.Text != status) note.Text = status;
            }
        }

        // A message that stays for a few seconds before the status line returns.
        void SetNote(string text)
        {
            note.Text = text;
            noteHold = DateTime.Now.AddSeconds(8);
        }

        void Problem(Exception ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        bool Confirm(string question)
        {
            return MessageBox.Show(this, question, AppInfo.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        // --------------------------------------------------------- actions --

        async void LaunchOrShow(Row row)
        {
            try
            {
                Profile p = row.P;
                bool started = await Task.Run(() => ClaudeApp.StartOrActivate(p));
                if (started) row.Status.Text = "Starting...";
                await RefreshOnce();
            }
            catch (Exception ex) { Problem(ex); }
        }

        async void StopProfile(Profile p, bool relaunch)
        {
            string what = relaunch ? "Relaunch" : "Stop";
            string then = relaunch ? ", and opens it again." : ".";
            if (!Confirm(what + " '" + p.Name + "'?\n\nThis closes that Claude window and ends any task it is running right now" + then)) return;
            try
            {
                await Task.Run(delegate
                {
                    ClaudeApp.Stop(p);
                    if (relaunch) { Thread.Sleep(800); ClaudeApp.Start(p); }
                });
                SetNote((relaunch ? "Relaunched '" : "Stopped '") + p.Name + "'.");
                await RefreshOnce();
            }
            catch (Exception ex) { Problem(ex); }
        }

        void AddProfile()
        {
            try
            {
                string name = Ui.Ask(this, "Add a profile",
                    "Name for the new profile, for example: work\n\nIt opens a new Claude window where you sign in with the other account.", "");
                if (string.IsNullOrEmpty(name)) return;
                Profile p = store.Add(name);
                BuildRows();
                bool reused = !p.Dir.StartsWith(Store.ProfilesRoot, StringComparison.OrdinalIgnoreCase) || Directory.Exists(p.Dir);
                SetNote(reused ? "Added '" + p.Name + "' with its existing folder. Press Launch."
                               : "Added '" + p.Name + "'. Press Launch and sign in with that account.");
            }
            catch (Exception ex) { Problem(ex); }
        }

        void SideBySide()
        {
            try
            {
                int count = ClaudeApp.SideBySide(store.Profiles, Screen.FromControl(this).WorkingArea);
                SetNote("Placed " + count + " Claude windows next to each other.");
            }
            catch (Exception ex) { Problem(ex); }
        }

        void SetColor(Profile p, string color)
        {
            try
            {
                p.Color = color;
                store.Save();
                RunningClaude run = ClaudeApp.Find(p, lastRunning);
                if (run != null) ClaudeApp.PaintAccent(p, run);
                BuildRows();
                SetNote(color != null ? "The '" + p.Name + "' window now has a coloured border (Windows 11)."
                                      : "Removed the border colour from '" + p.Name + "'.");
            }
            catch (Exception ex) { Problem(ex); }
        }

        // ----------------------------------------------------------- menus --

        static ToolStripMenuItem Item(ToolStrip menu, string text, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += onClick;
            menu.Items.Add(item);
            return item;
        }

        void ShowRowMenu(Row row)
        {
            Profile p = row.P;
            var menu = new ContextMenuStrip();
            menu.Font = Ui.Small;

            if (row.Running) Item(menu, "Relaunch", delegate { StopProfile(p, true); });

            Item(menu, "Rename...", delegate
            {
                try
                {
                    string old = p.Name;
                    string name = Ui.Ask(this, "Rename profile", "New name for this profile. The login and data stay as they are.", old);
                    if (string.IsNullOrEmpty(name) || name == old) return;
                    store.Rename(p, name);
                    Shortcuts.Rename(p, old);
                    BuildRows();
                    SetNote("Renamed to '" + name + "'.");
                }
                catch (Exception ex) { Problem(ex); }
            });

            var colors = new ToolStripMenuItem("Window colour");
            var none = new ToolStripMenuItem("None");
            none.Checked = string.IsNullOrEmpty(p.Color);
            none.Click += delegate { SetColor(p, null); };
            colors.DropDownItems.Add(none);
            foreach (KeyValuePair<string, string> choice in Ui.Palette)
            {
                string hex = choice.Value;
                var c = new ToolStripMenuItem(choice.Key);
                c.ForeColor = ColorTranslator.FromHtml(hex);
                c.Checked = string.Equals(p.Color, hex, StringComparison.OrdinalIgnoreCase);
                c.Click += delegate { SetColor(p, hex); };
                colors.DropDownItems.Add(c);
            }
            menu.Items.Add(colors);

            Item(menu, "Create desktop shortcut", delegate
            {
                try
                {
                    Shortcuts.CreateForProfile(p, Environment.SpecialFolder.DesktopDirectory);
                    SetNote("Shortcut 'Claude - " + p.Name + "' is on the desktop.");
                }
                catch (Exception ex) { Problem(ex); }
            });

            if (!p.IsDefault)
            {
                ToolStripMenuItem startup = Item(menu, "Start with Windows", delegate
                {
                    try
                    {
                        string lnk = Shortcuts.ForProfile(p, Environment.SpecialFolder.Startup);
                        if (File.Exists(lnk))
                        {
                            File.Delete(lnk);
                            SetNote("'" + p.Name + "' no longer starts with Windows.");
                        }
                        else
                        {
                            Shortcuts.CreateForProfile(p, Environment.SpecialFolder.Startup);
                            SetNote("'" + p.Name + "' now starts with Windows.");
                        }
                    }
                    catch (Exception ex) { Problem(ex); }
                });
                startup.Checked = File.Exists(Shortcuts.ForProfile(p, Environment.SpecialFolder.Startup));
            }

            Item(menu, "Open Claude Code terminal", delegate
            {
                try
                {
                    ClaudeApp.OpenTerminal(p);
                    SetNote("Terminal for '" + p.Name + "': type claude. The first time it asks you to log in.");
                }
                catch (Exception ex) { Problem(ex); }
            });

            if (!p.IsDefault)
            {
                Item(menu, "Open data folder", delegate
                {
                    try
                    {
                        if (!Directory.Exists(p.Dir)) throw new InvalidOperationException("This profile has no data yet. Launch it once first.");
                        Process.Start("explorer.exe", "\"" + p.Dir + "\"");
                    }
                    catch (Exception ex) { Problem(ex); }
                });

                menu.Items.Add(new ToolStripSeparator());

                Item(menu, "Remove from this list", delegate
                {
                    try
                    {
                        if (ClaudeApp.Find(p, lastRunning) != null) throw new InvalidOperationException("Stop '" + p.Name + "' first.");
                        if (!Confirm("Remove '" + p.Name + "' from the list?\n\nIts login and data stay on the disk. Adding a profile with the same name brings it back.")) return;
                        Shortcuts.Delete(p);
                        store.Remove(p);
                        BuildRows();
                        SetNote("Removed '" + p.Name + "' from the list.");
                    }
                    catch (Exception ex) { Problem(ex); }
                });
            }
            menu.Show(row.More, new Point(0, row.More.Height));
        }

        void ShowSettingsMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Font = Ui.Small;

            ToolStripMenuItem keep = Item(menu, "Keep running in the tray when closed", delegate
            {
                store.KeepInTray = !store.KeepInTray;
                store.Save();
                tray.Visible = store.KeepInTray;
                SetNote(store.KeepInTray ? "Closing this window now hides it in the tray, next to the clock."
                                         : "Closing this window now exits " + AppInfo.Name + ".");
            });
            keep.Checked = store.KeepInTray;

            Item(menu, "Add " + AppInfo.Name + " to the Start menu", delegate
            {
                try
                {
                    Shortcuts.CreateForApp(Environment.SpecialFolder.Programs);
                    SetNote(AppInfo.Title + " is now in the Start menu.");
                }
                catch (Exception ex) { Problem(ex); }
            });
            Item(menu, "Create desktop shortcut for " + AppInfo.Name, delegate
            {
                try
                {
                    Shortcuts.CreateForApp(Environment.SpecialFolder.DesktopDirectory);
                    SetNote("Shortcut '" + AppInfo.Title + "' is on the desktop.");
                }
                catch (Exception ex) { Problem(ex); }
            });
            Item(menu, "Open settings folder", delegate
            {
                try { Process.Start("explorer.exe", "\"" + Store.Folder + "\""); }
                catch (Exception ex) { Problem(ex); }
            });

            menu.Items.Add(new ToolStripSeparator());
            Item(menu, "About", delegate
            {
                string about = AppInfo.Title + " " + AppInfo.Version + "\n" + AppInfo.Tagline + "\n\n"
                    + "Free and open source. It collects nothing and never goes online.\n\n" + AppInfo.Disclaimer;
                if (AppInfo.Website != "") about += "\n\n" + AppInfo.Website;
                MessageBox.Show(this, about, "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            menu.Show(settingsButton, new Point(0, settingsButton.Height));
        }

        void BuildTrayMenu()
        {
            ContextMenuStrip menu = tray.ContextMenuStrip;
            menu.Items.Clear();
            foreach (Row row in rows)
            {
                Row r = row;
                Item(menu, (r.Running ? "Show " : "Open ") + r.P.Name, delegate { LaunchOrShow(r); });
            }
            menu.Items.Add(new ToolStripSeparator());
            Item(menu, "Side by side", delegate { SideBySide(); }).Enabled = tileButton.Enabled;
            Item(menu, "Show " + AppInfo.Name, delegate { ShowWindow(); });
            Item(menu, "Exit", delegate { exiting = true; Close(); });
        }

        // ---------------------------------------------------------- window --

        void ShowWindow()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        // A second start of the app only asks this one to come forward.
        void ListenForShowRequests()
        {
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
            {
                while (true)
                {
                    signal.WaitOne();
                    if (IsDisposed || exiting) return;
                    try { BeginInvoke((MethodInvoker)ShowWindow); } catch { return; }
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && snapshotPath == null && store.KeepInTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            exiting = true;
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        // Test helper: draws the window, with every row's menu opened once, to a PNG.
        void TakeSnapshot()
        {
            try { lastRunning = ClaudeApp.GetRunning(); } catch { }
            ApplyRunning(lastRunning);
            Application.DoEvents();
            foreach (Row row in rows) { ShowRowMenu(row); Application.DoEvents(); }
            ShowSettingsMenu();
            Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(snapshotPath, ImageFormat.Png);
            }
            exiting = true;
            Close();
        }
    }
}

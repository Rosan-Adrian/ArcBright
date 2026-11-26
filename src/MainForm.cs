using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArcBright
{
    internal sealed class MainForm : Form
    {
        private const int HotkeyId = 0xA7B;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "ArcBright";
        private const string RepoUrl = "https://github.com/Rosan-Adrian/ArcBright";

        private static readonly Color Green = Color.FromArgb(26, 127, 55);
        private static readonly Color Blue = Color.FromArgb(9, 105, 218);
        private static readonly Color Gray = Color.FromArgb(89, 99, 110);
        private static readonly Color Amber = Color.FromArgb(154, 103, 0);

        private sealed class SliderRow
        {
            public Label Caption;
            public TrackBar Bar;
            public Label Value;
            public Func<int, string> Format;
        }

        private readonly Settings settings;
        private readonly Engine engine;
        private readonly bool startHidden;
        private readonly float scale;
        private readonly int ownPid = Process.GetCurrentProcess().Id;

        private readonly Timer tick = new Timer();
        private readonly Timer pickTimer = new Timer();
        private readonly Timer saveTimer = new Timer();
        private readonly ToolTip tips = new ToolTip();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolStripMenuItem miShow = new ToolStripMenuItem();
        private readonly ToolStripMenuItem miEnabled = new ToolStripMenuItem();
        private readonly ToolStripMenuItem miExit = new ToolStripMenuItem();

        private readonly Label lblSubtitle = new Label();
        private readonly Label lblStatus = new Label();
        private readonly Label lblDetail = new Label();
        private readonly CheckBox chkEnabled = new CheckBox();
        private readonly CheckBox chkPreview = new CheckBox();
        private readonly CheckBox chkAutostart = new CheckBox();
        private readonly Label lblPresets = new Label();
        private readonly Button btnNormal = new Button();
        private readonly Button btnBright = new Button();
        private readonly Button btnBrighter = new Button();
        private readonly Label lblColourNote = new Label();
        private readonly Label lblGame = new Label();
        private readonly TextBox txtTargets = new TextBox();
        private readonly Button btnPick = new Button();
        private readonly Label lblRange = new Label();
        private readonly Button btnUnlock = new Button();
        private readonly LinkLabel lnkLanguage = new LinkLabel();
        private readonly LinkLabel lnkRepo = new LinkLabel();
        private readonly SliderRow rowBrightness, rowContrast, rowGamma, rowSaturation;

        private Native.WinEventProc winEventProc;
        private IntPtr winEventHook;
        private bool hotkeyOk;
        private bool exiting;
        private bool loading;
        private int pickCountdown;

        public MainForm(Settings settings, Engine engine, bool startHidden)
        {
            this.settings = settings;
            this.engine = engine;
            this.startHidden = startHidden;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                scale = g.DpiX / 96f;
            }

            Text = "ArcBright";
            Font = new Font("Segoe UI", 9f);
            Icon = IconArt.MakeIcon(S(32));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var root = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(S(18), S(14), S(18), S(14)),
            };
            Controls.Add(root);

            var title = new Label
            {
                Text = "ArcBright",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 15f),
                Margin = new Padding(0),
            };
            root.Controls.Add(title);
            lblSubtitle.AutoSize = true;
            lblSubtitle.ForeColor = Gray;
            lblSubtitle.Margin = new Padding(S(2), 0, 0, S(10));
            root.Controls.Add(lblSubtitle);

            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("Segoe UI Semibold", 10f);
            lblStatus.Margin = new Padding(0, 0, 0, S(2));
            root.Controls.Add(lblStatus);
            lblDetail.AutoSize = true;
            lblDetail.MaximumSize = new Size(S(400), 0);
            lblDetail.ForeColor = Gray;
            lblDetail.Margin = new Padding(S(2), 0, 0, S(8));
            root.Controls.Add(lblDetail);

            var toggles = Flow();
            chkEnabled.AutoSize = true;
            chkEnabled.Checked = settings.Enabled;
            chkEnabled.Margin = new Padding(0, S(2), S(18), S(2));
            chkEnabled.CheckedChanged += OnEnabledChanged;
            chkPreview.AutoSize = true;
            chkPreview.Margin = new Padding(0, S(2), 0, S(2));
            chkPreview.CheckedChanged += OnPreviewChanged;
            toggles.Controls.Add(chkEnabled);
            toggles.Controls.Add(chkPreview);
            root.Controls.Add(toggles);

            var sliders = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 3,
                Margin = new Padding(0, S(6), 0, 0),
            };
            rowBrightness = AddSlider(sliders, 0, Settings.BrightnessMin, Settings.BrightnessMax, settings.Brightness,
                v => v > 0 ? "+" + v : v.ToString(CultureInfo.InvariantCulture), v => settings.Brightness = v);
            rowContrast = AddSlider(sliders, 1, Settings.ContrastMin, Settings.ContrastMax, settings.Contrast,
                v => v + " %", v => settings.Contrast = v);
            rowGamma = AddSlider(sliders, 2, Settings.GammaMin, Settings.GammaMax, settings.Gamma,
                v => (v / 100.0).ToString("0.00", CultureInfo.InvariantCulture), v => settings.Gamma = v);
            rowSaturation = AddSlider(sliders, 3, Settings.SaturationMin, Settings.SaturationMax, settings.Saturation,
                v => v + " %", v => settings.Saturation = v);
            root.Controls.Add(sliders);

            lblColourNote.AutoSize = true;
            lblColourNote.ForeColor = Amber;
            lblColourNote.Margin = new Padding(S(2), 0, 0, S(4));
            root.Controls.Add(lblColourNote);
            if (engine.Amd.Available)
            {
                lblColourNote.Visible = false;
            }
            else
            {
                rowSaturation.Bar.Enabled = false;
            }

            var presets = Flow();
            presets.Margin = new Padding(0, S(4), 0, S(8));
            lblPresets.AutoSize = true;
            lblPresets.Anchor = AnchorStyles.Left;
            lblPresets.Margin = new Padding(S(2), 0, S(8), 0);
            presets.Controls.Add(lblPresets);
            AddPreset(presets, btnNormal, Settings.BrightnessDefault, Settings.ContrastDefault, Settings.GammaDefault, Settings.SaturationDefault);
            // Lower contrast keeps the bright parts from burning out while gamma lifts the shadows.
            AddPreset(presets, btnBright, 0, 90, 160, 120);
            AddPreset(presets, btnBrighter, 0, 85, 200, 130);
            root.Controls.Add(presets);

            var game = Flow();
            lblGame.AutoSize = true;
            lblGame.Anchor = AnchorStyles.Left;
            lblGame.Margin = new Padding(S(2), 0, S(8), 0);
            txtTargets.Width = S(150);
            txtTargets.Text = settings.Targets;
            txtTargets.Anchor = AnchorStyles.Left;
            txtTargets.TextChanged += OnTargetsChanged;
            txtTargets.Leave += (s, e) =>
            {
                if (txtTargets.Text.Trim().Length == 0)
                {
                    txtTargets.Text = Settings.DefaultTargets;
                }
            };
            btnPick.AutoSize = true;
            btnPick.Anchor = AnchorStyles.Left;
            btnPick.Click += OnPickClick;
            game.Controls.Add(lblGame);
            game.Controls.Add(txtTargets);
            game.Controls.Add(btnPick);
            root.Controls.Add(game);

            var range = Flow();
            range.Margin = new Padding(0, S(6), 0, 0);
            lblRange.AutoSize = true;
            lblRange.Anchor = AnchorStyles.Left;
            lblRange.ForeColor = Gray;
            lblRange.Margin = new Padding(S(2), 0, S(8), 0);
            btnUnlock.AutoSize = true;
            btnUnlock.Anchor = AnchorStyles.Left;
            btnUnlock.Click += OnUnlockClick;
            range.Controls.Add(lblRange);
            range.Controls.Add(btnUnlock);
            root.Controls.Add(range);

            var bottom = Flow();
            bottom.Margin = new Padding(0, S(10), 0, 0);
            chkAutostart.AutoSize = true;
            chkAutostart.Checked = IsAutostart();
            chkAutostart.Margin = new Padding(0, S(2), S(18), 0);
            chkAutostart.CheckedChanged += OnAutostartChanged;
            lnkLanguage.AutoSize = true;
            lnkLanguage.Margin = new Padding(0, S(4), S(12), 0);
            lnkLanguage.LinkClicked += OnLanguageClicked;
            lnkRepo.AutoSize = true;
            lnkRepo.Text = "GitHub";
            lnkRepo.Margin = new Padding(0, S(4), 0, 0);
            lnkRepo.LinkClicked += (s, e) => Process.Start(RepoUrl);
            bottom.Controls.Add(chkAutostart);
            bottom.Controls.Add(lnkLanguage);
            bottom.Controls.Add(lnkRepo);
            root.Controls.Add(bottom);

            if (chkAutostart.Checked)
            {
                SetAutostart(true); // keeps the path right if the EXE was moved
            }

            var menu = new ContextMenuStrip();
            miShow.Click += (s, e) => ShowFromTray();
            miEnabled.Click += (s, e) => chkEnabled.Checked = !chkEnabled.Checked;
            miExit.Click += (s, e) => ExitApp();
            menu.Items.Add(miShow);
            menu.Items.Add(miEnabled);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);
            tray.ContextMenuStrip = menu;
            tray.Icon = IconArt.MakeIcon(SystemInformation.SmallIconSize.Width);
            tray.Text = "ArcBright";
            tray.Visible = true;
            tray.DoubleClick += (s, e) => ShowFromTray();

            tick.Interval = 1000;
            tick.Tick += (s, e) => RefreshState(false);
            pickTimer.Interval = 1000;
            pickTimer.Tick += OnPickTick;
            saveTimer.Interval = 800;
            saveTimer.Tick += (s, e) =>
            {
                saveTimer.Stop();
                settings.Save();
            };

            ApplyTexts();
        }

        private int S(int pixels)
        {
            return (int)Math.Round(pixels * scale);
        }

        private FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
            };
        }

        private SliderRow AddSlider(TableLayoutPanel table, int row, int min, int max, int value, Func<int, string> format, Action<int> store)
        {
            var r = new SliderRow { Format = format };
            r.Caption = new Label { AutoSize = false, Width = S(92), Height = S(22), TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            r.Bar = new TrackBar
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
                TickStyle = TickStyle.None,
                AutoSize = false,
                Width = S(250),
                Height = S(30),
                SmallChange = 1,
                LargeChange = 5,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
            };
            r.Value = new Label { AutoSize = false, Width = S(52), Height = S(22), TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            r.Value.Text = format(r.Bar.Value);
            r.Bar.ValueChanged += (s, e) =>
            {
                r.Value.Text = r.Format(r.Bar.Value);
                if (loading)
                {
                    return;
                }
                store(r.Bar.Value);
                OnColoursChanged();
            };
            table.Controls.Add(r.Caption, 0, row);
            table.Controls.Add(r.Bar, 1, row);
            table.Controls.Add(r.Value, 2, row);
            return r;
        }

        private void AddPreset(FlowLayoutPanel panel, Button button, int brightness, int contrast, int gamma, int saturation)
        {
            button.AutoSize = true;
            button.Margin = new Padding(0, 0, S(6), 0);
            button.Click += (s, e) =>
            {
                loading = true;
                rowBrightness.Bar.Value = brightness;
                rowContrast.Bar.Value = contrast;
                rowGamma.Bar.Value = gamma;
                rowSaturation.Bar.Value = saturation;
                loading = false;
                settings.Brightness = brightness;
                settings.Contrast = contrast;
                settings.Gamma = gamma;
                settings.Saturation = saturation;
                OnColoursChanged();
            };
            panel.Controls.Add(button);
        }

        private void ApplyTexts()
        {
            lblSubtitle.Text = Lang.T("Brighter Arc Raiders, only while the game is in front.",
                "Arc Raiders mai luminos, doar cât timp jocul e în față.");
            chkEnabled.Text = Lang.T("On", "Pornit") + (hotkeyOk ? "  (Ctrl+Alt+B)" : "");
            chkPreview.Text = Lang.T("Preview on this screen", "Previzualizează pe ecranul ăsta");
            rowBrightness.Caption.Text = Lang.T("Brightness", "Luminozitate");
            rowContrast.Caption.Text = Lang.T("Contrast", "Contrast");
            rowGamma.Caption.Text = Lang.T("Gamma", "Gamma");
            rowSaturation.Caption.Text = Lang.T("Colour", "Culoare");
            lblPresets.Text = Lang.T("Presets:", "Rapid:");
            btnNormal.Text = Lang.T("Normal", "Normal");
            btnBright.Text = Lang.T("Bright", "Luminos");
            btnBrighter.Text = Lang.T("Brighter", "Foarte luminos");
            lblColourNote.Text = string.Format(Lang.T("Colour needs an AMD Radeon card ({0}).", "Culoarea merge doar pe plăci AMD Radeon ({0})."), engine.Amd.Error);
            lblGame.Text = Lang.T("Game:", "Joc:");
            if (pickTimer.Enabled)
            {
                UpdatePickText();
            }
            else
            {
                btnPick.Text = Lang.T("Pick window…", "Alege fereastra…");
            }
            btnUnlock.Text = Lang.T("Unlock (admin)…", "Deblochează (admin)…");
            chkAutostart.Text = Lang.T("Start with Windows", "Pornește cu Windows");
            lnkLanguage.Text = Lang.Romanian ? "English" : "Română";
            miShow.Text = Lang.T("Show ArcBright", "Arată ArcBright");
            miEnabled.Text = Lang.T("On", "Pornit");
            miExit.Text = Lang.T("Exit", "Ieșire");

            tips.SetToolTip(rowBrightness.Caption, Lang.T("Raises everything, black included.", "Ridică tot, inclusiv negrul."));
            tips.SetToolTip(rowContrast.Caption, Lang.T("Spreads dark and light apart.", "Depărtează întunericul de lumină."));
            tips.SetToolTip(rowGamma.Caption, Lang.T("Lifts the shadows, so you see into dark corners. Start here.",
                "Luminează umbrele, ca să vezi în colțurile întunecate. Începe cu asta."));
            tips.SetToolTip(rowSaturation.Caption, Lang.T("Colour saturation through the AMD driver.", "Saturația culorilor, prin driverul AMD."));
            tips.SetToolTip(txtTargets, Lang.T("The game's process name. Arc Raiders is PioneerGame. Several: separate with commas.",
                "Numele procesului jocului. Arc Raiders este PioneerGame. Mai multe: separă cu virgulă."));
            tips.SetToolTip(btnPick, Lang.T("Click, then switch to the game within 5 seconds.", "Apasă, apoi treci în joc în 5 secunde."));

            UpdateRange();
            RefreshState(true);
        }

        private void RefreshState(bool force)
        {
            engine.Evaluate();
            string status, detail;
            Color color;
            bool on = engine.State == EngineState.Active || engine.State == EngineState.Preview;
            switch (engine.State)
            {
                case EngineState.Active:
                    status = Lang.T("● Active: the game is in front", "● Activ: jocul e în față");
                    color = Green;
                    break;
                case EngineState.Preview:
                    status = Lang.T("● Preview on this screen", "● Previzualizare pe ecranul ăsta");
                    color = Blue;
                    break;
                case EngineState.Waiting:
                    status = Lang.T("○ Waiting for the game", "○ Aștept jocul");
                    color = Gray;
                    break;
                default:
                    status = Lang.T("○ Off", "○ Oprit");
                    color = Gray;
                    break;
            }

            if (on && engine.GammaStrength < 0)
            {
                detail = Lang.T("Windows refused the brightness change. Is HDR on? It works with HDR off.",
                    "Windows a refuzat schimbarea luminozității. Ai HDR pornit? Merge cu HDR oprit.");
            }
            else if (on && engine.GammaStrength < 100)
            {
                detail = string.Format(Lang.T("Windows allows only {0}% of this change. Unlock the full range below.",
                    "Windows permite doar {0}% din schimbare. Deblochează intervalul complet mai jos."), engine.GammaStrength);
            }
            else if (engine.State == EngineState.Waiting)
            {
                detail = string.Format(Lang.T("Turns on by itself when {0} is in front, off when you Alt-Tab.",
                    "Pornește singur când {0} e în față și se oprește la Alt-Tab."), FirstTarget());
            }
            else if (engine.State == EngineState.Off)
            {
                detail = Lang.T("Tick On, or press Ctrl+Alt+B.", "Bifează Pornit sau apasă Ctrl+Alt+B.");
            }
            else if (engine.State == EngineState.Preview)
            {
                detail = Lang.T("Untick Preview when you are done.", "Debifează previzualizarea când ai terminat.");
            }
            else
            {
                detail = Lang.T("Alt-Tab and the screen goes back to normal.", "La Alt-Tab ecranul revine la normal.");
            }

            if (force || lblStatus.Text != status)
            {
                lblStatus.Text = status;
                lblStatus.ForeColor = color;
            }
            if (force || lblDetail.Text != detail)
            {
                lblDetail.Text = detail;
            }
            string trayText = "ArcBright: " + status.Substring(2);
            tray.Text = trayText.Length > 63 ? trayText.Substring(0, 63) : trayText;
            miEnabled.Checked = settings.Enabled;
        }

        private string FirstTarget()
        {
            foreach (string t in settings.Targets.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = t.Trim();
                if (name.Length > 0)
                {
                    return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                }
            }
            return "?";
        }

        private void UpdateRange()
        {
            bool unlocked = Diag.GammaRangeValue() == "256";
            lblRange.Text = unlocked
                ? Lang.T("Full brightness range: unlocked.", "Interval complet de luminozitate: deblocat.")
                : Lang.T("Windows limits strong changes.", "Windows limitează schimbările mari.");
            btnUnlock.Visible = !unlocked;
        }

        private void OnColoursChanged()
        {
            RefreshState(false);
            ScheduleSave();
        }

        private void ScheduleSave()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        private void OnEnabledChanged(object sender, EventArgs e)
        {
            settings.Enabled = chkEnabled.Checked;
            RefreshState(false);
            ScheduleSave();
        }

        private void OnPreviewChanged(object sender, EventArgs e)
        {
            engine.Preview = chkPreview.Checked;
            engine.PreviewWindow = Handle;
            RefreshState(false);
        }

        private void OnTargetsChanged(object sender, EventArgs e)
        {
            settings.Targets = txtTargets.Text.Trim();
            engine.Invalidate();
            RefreshState(false);
            ScheduleSave();
        }

        private void OnPickClick(object sender, EventArgs e)
        {
            pickCountdown = 5;
            UpdatePickText();
            pickTimer.Start();
        }

        private void UpdatePickText()
        {
            btnPick.Text = string.Format(Lang.T("Switch to the game… {0}", "Treci în joc… {0}"), pickCountdown);
        }

        private void OnPickTick(object sender, EventArgs e)
        {
            pickCountdown--;
            if (pickCountdown > 0)
            {
                UpdatePickText();
                return;
            }
            pickTimer.Stop();
            btnPick.Text = Lang.T("Pick window…", "Alege fereastra…");
            uint pid;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            string name = pid != 0 && pid != (uint)ownPid ? Engine.ProcessName(pid) : null;
            if (name == null)
            {
                return;
            }
            txtTargets.Text = name;
            tray.ShowBalloonTip(3000, "ArcBright", string.Format(Lang.T("Game set to {0}.exe", "Jocul setat: {0}.exe"), name), ToolTipIcon.Info);
        }

        private void OnUnlockClick(object sender, EventArgs e)
        {
            var psi = new ProcessStartInfo("reg.exe",
                "add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ICM\" /v GdiIcmGammaRange /t REG_DWORD /d 256 /f")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            try
            {
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(15000);
                }
            }
            catch (Win32Exception)
            {
                return; // the person said no to the admin prompt
            }
            UpdateRange();
            if (Diag.GammaRangeValue() == "256")
            {
                MessageBox.Show(this, Lang.T("Done. Restart Windows once and the full range works.",
                    "Gata. Repornește Windows o dată și intervalul complet funcționează."), "ArcBright");
            }
        }

        private void OnLanguageClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Lang.Romanian = !Lang.Romanian;
            settings.Language = Lang.Romanian ? "ro" : "en";
            ApplyTexts();
            ScheduleSave();
        }

        private static bool IsAutostart()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                return key != null && key.GetValue(RunValue) != null;
            }
        }

        private static void SetAutostart(bool on)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on)
                {
                    key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\" --tray");
                }
                else
                {
                    key.DeleteValue(RunValue, false);
                }
            }
        }

        private void OnAutostartChanged(object sender, EventArgs e)
        {
            try
            {
                SetAutostart(chkAutostart.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "ArcBright");
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void HideToTray()
        {
            chkPreview.Checked = false;
            Hide();
            if (!settings.TrayHintShown)
            {
                tray.ShowBalloonTip(5000, "ArcBright", Lang.T("Still running here and waiting for the game. Right-click the icon to exit.",
                    "Rulează în continuare aici și așteaptă jocul. Click dreapta pe iconiță pentru ieșire."), ToolTipIcon.Info);
                settings.TrayHintShown = true;
                ScheduleSave();
            }
        }

        private void ExitApp()
        {
            exiting = true;
            Close();
        }

        private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            RefreshState(false);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !IsHandleCreated)
            {
                CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hotkeyOk = Native.RegisterHotKey(Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.B);
            winEventProc = OnForegroundChanged;
            winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            tick.Start();
            ApplyTexts();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                chkEnabled.Checked = !chkEnabled.Checked;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            tick.Stop();
            pickTimer.Stop();
            if (winEventHook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(winEventHook);
                winEventHook = IntPtr.Zero;
            }
            if (hotkeyOk)
            {
                Native.UnregisterHotKey(Handle, HotkeyId);
            }
            tray.Visible = false;
            engine.RestoreAll();
            settings.Save();
            base.OnFormClosing(e);
        }
    }
}

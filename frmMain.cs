using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Descrambler.Properties;

namespace Descrambler
{
    public partial class frmMain : Form
    {
        bool _roundedBorderless = true;
        static string lastWord = "";
        static Unscramble engine = new Unscramble();

        #region [rounded-borderless]
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        static extern IntPtr CreateRoundRectRgn
        (
            int nLeftRect,
            int nTopRect,
            int nRightRect,
            int nBottomRect,
            int nWidthEllipse,
            int nHeightEllipse
        );
        #endregion
        
        #region [click-n-drag]
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        #endregion

        /// <summary>
        /// [Original Article]
        /// https://www.codeproject.com/Tips/5299463/How-to-Unscramble-Any-Word?fid=1969546&df=90&mpp=25&sort=Position&spc=Relaxed&prof=True&view=Normal&fr=26#x.
        /// </summary>
        public frmMain()
        {
            Application.ThreadException += Application_ThreadException;
            InitializeComponent();

            if (_roundedBorderless)
            {
                this.Text = Program.GetCurrentNamespace();
                this.FormBorderStyle = FormBorderStyle.None;
                this.Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, this.Width, this.Height, 16, 16));
            }
            else
                this.Text = ""; // Doesn't seem to play well with the DwmApi calls unless ALLOW_NCPAINT is also used.

            #region [Acrylic Background]
            // This will determine the glass accent color.
            this.BackColor = Color.FromArgb(20, 20, 29);

            if (DwmHelper.IsDWMCompositionEnabled())
            {
                // If the version is not being observed correctly
                // then make sure to add app.manifest to the project.
                if (Environment.OSVersion.Version.Major > 6)
                {
                    DwmHelper.Windows10EnableBlurBehind(this.Handle);
                    // Enables content rendered in the non-client area to be visible on the frame drawn by DWM.
                    //DwmHelper.WindowSetAttribute(this.Handle, DwmHelper.DWMWINDOWATTRIBUTE.AllowNCPaint, 1);
                }
                else
                {   // This will also work for Windows 10+, but the effect is
                    // only a transparency and not the acrylic/glass effect.
                    DwmHelper.WindowEnableBlurBehind(this.Handle);
                }

                // Set Drop shadow of a border-less Form
                if (this.FormBorderStyle == FormBorderStyle.None)
                    DwmHelper.WindowBorderlessDropShadow(this.Handle, 2);
            }
            #endregion
        }
        void Application_ThreadException(object sender, ThreadExceptionEventArgs e) => Debug.WriteLine($"[ERROR] ThreadException: {e.Exception.Message}");

        #region [UI Events]
        void frmMain_MouseDown(object sender, MouseEventArgs e)
        {
            if (!_roundedBorderless)
                return;

            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
            else if (e.Button == MouseButtons.Right)
                this.Close(); //Application.Exit();
        }

        void frmMain_Shown(object sender, EventArgs e)
        {
            LoadSettings();
            if (string.IsNullOrEmpty(lastWord))
            {
                SetText(tbScrambled, "girngeeenin");
            }
            else
            {
                SetText(tbScrambled, lastWord);
                SetText(lblStatus, "Last word loaded");
            }

            #region [Tool tips]
            ToolTip tt1 = new ToolTip() { IsBalloon = true, AutoPopDelay = 5000, InitialDelay = 500, ReshowDelay = 500, ShowAlways = true, UseFading = true, UseAnimation = true };
            tt1.SetToolTip(tbScrambled, "The word to unscramble");
            tt1.SetToolTip(tbResult, "The unscrambled result");
            tt1.SetToolTip(this, "Right-click here to exit");
            #endregion

            btnDecode.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btnDecode.FlatAppearance.MouseDownBackColor = Color.Transparent;

            SetControlCursor(tbScrambled, Cursors.Hand);
            SetControlCursor(tbResult, Cursors.Hand);
            SetControlCursor(btnDecode, Cursors.Hand);

            #region [Zoom test]
            //this.MouseWheel += (obj, mea) =>
            //{
            //    var scale = 1 + (mea.Delta > 0 ? 0.1f : -0.1f);
            //    ScaleControl(this, scale);
            //    var screen = Screen.FromControl(this);
            //    SetControlLocation(this, new Point(screen.WorkingArea.Width / 2 - (this.Width / 2), screen.WorkingArea.Height / 2 - (this.Height / 2)));
            //};
            #endregion
        }

        void frmMain_FormClosing(object sender, FormClosingEventArgs e) => SaveSettings();

        void tbScrambled_Click(object sender, EventArgs e) => SelectAll(tbScrambled);

        void tbResult_Click(object sender, EventArgs e) => SelectAll(tbResult);

        void tbScrambled_TextChanged(object sender, EventArgs e) => lastWord = GetContents(tbScrambled);

        void tbScrambled_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                btnDecode_Click(this, new EventArgs());
                e.Handled = true;
            }
        }

        void btnDecode_Click(object sender, EventArgs e)
        {
            var word = GetContents(tbScrambled);
            if (string.IsNullOrEmpty(word))
            {
                SetText(lblStatus, "No data");
                tbScrambled.Focus();
                return;
            }
            ClearTextBox(tbResult);
            SetText(lblStatus, "Decoding...");
            Task.Run(async () =>
            {
                ToggleSystemBusy(true);
                await Task.Delay(200);

                List<string> results = engine.UnscrambleWord(word);
                if (results.Count > 0)
                {
                    foreach (string str in results)
                    {
                        AppendTextBox(tbResult, $"{str}\r\n");
                    }
                    SetText(lblStatus, $"Matches: {engine.GetMatchCount()}    Time: {ToReadableString(engine.GetMatchTime())}");
                    Debug.WriteLine($"[INFO] Filtered set: {engine.GetFilterCount()} out of {engine.GetDictionaryCount()}");
                }
                else
                {
                    SetText(lblStatus, "No match. Check your spelling, or dictionary may be missing this word.");
                }

                ToggleSystemBusy(false);
                //GetContentsAsync(tbResult, (txt) => { Debug.WriteLine($"{txt}"); });
            });
        }
        #endregion

        #region [Helper Methods]
        void ToggleSystemBusy(bool busy)
        {
            if (busy)
            {
                SetControlCursor(this, Cursors.WaitCursor);
                ToggleEnabled(btnDecode, false);
                ToggleEnabled(tbScrambled, false);
            }
            else
            {
                ToggleEnabled(btnDecode, true);
                ToggleEnabled(tbScrambled, true);
                SetControlCursor(this, Cursors.Arrow);
            }
        }

        /// <summary>
        /// Similar to <see cref="GetReadableTime(TimeSpan)"/>.
        /// </summary>
        /// <param name="timeSpan"><see cref="TimeSpan"/></param>
        /// <returns>formatted text</returns>
        string ToReadableString(TimeSpan span)
        {
            //return string.Format("{0}{1}{2}{3}",
            //    span.Duration().Days > 0 ? string.Format("{0:0} day{1}, ", span.Days, span.Days == 1 ? string.Empty : "s") : string.Empty,
            //    span.Duration().Hours > 0 ? string.Format("{0:0} hr{1}, ", span.Hours, span.Hours == 1 ? string.Empty : "s") : string.Empty,
            //    span.Duration().Minutes > 0 ? string.Format("{0:0} min{1}, ", span.Minutes, span.Minutes == 1 ? string.Empty : "s") : string.Empty,
            //    span.Duration().Seconds > 0 ? string.Format("{0:0} sec{1}", span.Seconds, span.Seconds == 1 ? string.Empty : "s") : string.Empty);

            var parts = new StringBuilder();
            if (span.Days > 0)
                parts.Append($"{span.Days} day{(span.Days == 1 ? string.Empty : "s")} ");
            if (span.Hours > 0)
                parts.Append($"{span.Hours} hour{(span.Hours == 1 ? string.Empty : "s")} ");
            if (span.Minutes > 0)
                parts.Append($"{span.Minutes} minute{(span.Minutes == 1 ? string.Empty : "s")} ");
            if (span.Seconds > 0)
                parts.Append($"{span.Seconds} second{(span.Seconds == 1 ? string.Empty : "s")} ");
            if (span.Milliseconds > 0)
                parts.Append($"{span.Milliseconds} millisecond{(span.Milliseconds == 1 ? string.Empty : "s")} ");

            if (parts.Length == 0) // result was less than 1 millisecond
                return $"{span.TotalMilliseconds:N4} milliseconds";
            else
                return parts.ToString().Trim();
        }

        /// <summary>
        /// Read in our settings from AssemblyName.exe.config
        /// </summary>
        void LoadSettings()
        {
            try
            {
                foreach (string key in ConfigurationManager.AppSettings)
                {
                    string value = ConfigurationManager.AppSettings[key];
                    if (key.Contains("LASTWORD"))
                    {
                        lastWord = value;
                    }
                }
                SetText(lblStatus, $"Settings were loaded");
            }
            catch (Exception ex)
            {
                SetText(lblStatus, $"LoadSettings: {ex.Message}");
            }
        }

        /// <summary>
        /// Save our settings to AssemblyName.exe.config
        /// </summary>
        void SaveSettings()
        {
            try
            {
                bool keyFound = false;

                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None); // Open App.Config of executable
                //config.AppSettings.Settings.Clear(); // Clear the current buffer state.

                foreach (var key in config.AppSettings.Settings)
                {
                    //string value = ConfigurationManager.AppSettings.[$"{key}"];
                    if (key != null && key is System.Configuration.KeyValueConfigurationElement ele)
                    {
                        //var ele = (System.Configuration.KeyValueConfigurationElement)key;
                        if (ele.Key.Equals("LASTWORD"))
                        {
                            keyFound = true;
                            ele.Value = lastWord;
                        }
                    }
                }

                if (!keyFound)
                {
                    //config.AppSettings.Settings.Remove("LASTGROUP");
                    config.AppSettings.Settings.Add("LASTWORD", lastWord);
                }

                config.Save(ConfigurationSaveMode.Modified); // Save the changes in App.config file.
                ConfigurationManager.RefreshSection("appSettings"); // Force a reload of a changed section.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Settings could not be saved: {ex.Message}");
            }
        }
        #endregion

        #region [Button Movement Effect]
        void btn_MouseEnter(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
                btn.Padding = new Padding() { Left = 2, Top = 2, Right = 0, Bottom = 0 };
        }

        void btn_MouseLeave(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
                btn.Padding = new Padding() { Left = 0, Top = 0, Right = 0, Bottom = 0 };
        }

        void btn_MouseDown(object sender, MouseEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
                btn.Padding = new Padding() { Left = 3, Top = 3, Right = 0, Bottom = 0 };
        }

        void btn_MouseUp(object sender, MouseEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
                btn.Padding = new Padding() { Left = 0, Top = 0, Right = 0, Bottom = 0 };
        }
        #endregion

        #region [Thread-safe Control Methods]
        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="pb"><see cref="ProgressBar"/></param>
        /// <param name="value">the new value</param>
        public void SetValue(ProgressBar pb, int value)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => SetValue(pb, value)));
            else
                pb.Value = value;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="pb"><see cref="ProgressBar"/></param>
        /// <param name="value">the new value</param>
        public void IncrementValue(ProgressBar pb, int value)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => IncrementValue(pb, value)));
            else
                pb.Increment(value);
        }


        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="pb"><see cref="PictureBox"/></param>
        /// <param name="image"><see cref="System.Drawing.Image"/></param>
        public void SetImage(PictureBox pb, System.Drawing.Image image)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => SetImage(pb, image)));
            else
                pb.Image = image;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetPictureBoxSizeMode(PictureBox pb, PictureBoxSizeMode sizeMode)
        {
            if (pb.InvokeRequired)
                pb.Invoke(new Action(() => pb.SizeMode = sizeMode));
            else
                pb.SizeMode = sizeMode;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="cb"><see cref="ComboBox"/></param>
        /// <param name="item"><see cref="object"/></param>
        public void AddItem(ComboBox cb, object item)
        {
            if (InvokeRequired)
                cb.Invoke(new Action(() => cb.Items.Add(item)));
            else
                cb.Items.Add(item);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void SetFocus(Control ctrl)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => SetFocus(ctrl)));
            else
                ctrl.Focus();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        /// <param name="text">string data</param>
        public void SetText(Control ctrl, string text)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => SetText(ctrl, text)));
            else
                ctrl.Text = text;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void HideControl(Control ctrl)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => HideControl(ctrl)));
            else
                ctrl.Hide();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void ShowControl(Control ctrl)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ShowControl(ctrl)));
            else
                ctrl.Show();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void SelectControl(Control ctrl)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => SelectControl(ctrl)));
            else
                ctrl.Select();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void UpdateControl(Control ctrl)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => UpdateControl(ctrl)));
            else
                ctrl.Update();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void InvalidateControl(Control ctrl)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => InvalidateControl(ctrl)));
            else
                ctrl.Invalidate();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void RefreshControl(Control ctrl)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => RefreshControl(ctrl)));
            else
                ctrl.Refresh();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void SetControlCursor(Control ctrl, Cursor cursor)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => SetControlCursor(ctrl, cursor)));
            else
            {
                if (cursor != null)
                    ctrl.Cursor = cursor;
                else
                    ctrl.Cursor = Cursor.Current; // ctrl.Cursor = Cursors.Arrow

                #region [loading from stream]
                //using (MemoryStream cursorStream = new MemoryStream(Resources.pointer1))
                //{
                //    ctrl.Cursor = new System.Windows.Forms.Cursor(cursorStream));
                //}

                //using (Stream stream = this.GetType().Assembly.GetManifestResourceStream($"{Program.GetCurrentNamespace()}.Resources.pointer1.cur"))
                //{
                //    ctrl.Cursor = new System.Windows.Forms.Cursor(stream);
                //}
                #endregion
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void ScaleControl(Control ctrl, float ratio)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ScaleControl(ctrl, ratio)));
            else
                ctrl.Scale(new System.Drawing.SizeF(ratio, ratio));
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public System.Drawing.Rectangle GetControlBounds(Control ctrl)
        {
            if (InvokeRequired)
            {
                return (System.Drawing.Rectangle)Invoke(new Func<System.Drawing.Rectangle>(() => GetControlBounds(ctrl)));
            }
            else
            {
                try { return Screen.FromControl(ctrl).Bounds; }
                catch { return new System.Drawing.Rectangle(); }
                //var screen = Screen.FromControl(ctrl);
                //ctrl.Location = new Point(screen.WorkingArea.Width / 2 - this.Width / 2, screen.WorkingArea.Height / 2 - this.Height / 2);
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public System.Drawing.Rectangle GetControlWorkingArea(Control ctrl)
        {
            if (InvokeRequired)
            {
                return (System.Drawing.Rectangle)Invoke(new Func<System.Drawing.Rectangle>(() => GetControlWorkingArea(ctrl)));
            }
            else
            {
                try { return Screen.FromControl(ctrl).WorkingArea; }
                catch { return new System.Drawing.Rectangle(); }
                //var screen = Screen.FromControl(ctrl);
                //ctrl.Location = new Point(screen.WorkingArea.Width / 2 - (this.Width / 2), screen.WorkingArea.Height / 2 - (this.Height / 2));
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void CenterControl(Control ctrl)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => CenterControl(ctrl)));
            }
            else
            {
                ctrl.BringToFront();
                ctrl.Location = new Point(this.Width / 2 - (ctrl.Width / 2), this.Height / 2 - (ctrl.Height / 2));
            }
        }


        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        public void SetControlLocation(Control ctrl, System.Drawing.Point point)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => SetControlLocation(ctrl, point)));
            else
                ctrl.Location = point;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        /// <param name="state">true=enabled, false=disabled</param>
        public void ToggleVisible(Control ctrl, bool state)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ToggleVisible(ctrl, state)));
            else
                ctrl.Visible = state;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        /// <param name="state">true=enabled, false=disabled</param>
        public void ToggleEnabled(Control ctrl, bool state)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ToggleEnabled(ctrl, state)));
            else
                ctrl.Enabled = state;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="CheckBox"/></param>
        /// <param name="state">true=checked, false=unchecked</param>
        public void ToggleChecked(CheckBox ctrl, bool state)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ToggleChecked(ctrl, state)));
            else
                ctrl.Checked = state;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ctrl"><see cref="Control"/></param>
        /// <param name="data">text for control</param>
        public void UpdateText(Control ctrl, string data)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => UpdateText(ctrl, data)));
            else
                ctrl.Text = data;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="state"><see cref="FormWindowState"/></param>
        public void UpdateWindowState(FormWindowState state)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => UpdateWindowState(state)));
            else
            {
                try { this.WindowState = state; }
                catch { }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="tb"><see cref="TextBox"/></param>
        public void SelectAll(TextBox tb)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => SelectAll(tb)));
            else
            {
                try { tb.SelectAll(); }
                catch { }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="tb"><see cref="TextBox"/></param>
        /// <param name="data">text to append</param>
        public void AppendTextBox(TextBox tb, string data)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => AppendTextBox(tb, data)));
            else
            {
                try { tb.AppendText(data); }
                catch { }
            }
        }

        /// <summary>
        /// Thread-safe method via <see cref="TaskCompletionSource{TResult}"/>
        /// </summary>
        /// <param name="tb"><see cref="TextBox"/></param>
        /// <param name="data">text to append</param>
        public Task AppendTextBoxAsync(TextBox tb, string data)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        tb.AppendText(data);
                        tcs.SetResult(true);
                    }
                    catch (Exception ex) {tcs.SetException(ex); }
                }));
            }
            else
            {
                try
                {
                    tb.AppendText(data);
                    tcs.SetResult(true);
                }
                catch (Exception ex) { tcs.SetException(ex); }
            }
            return tcs.Task;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="tb"><see cref="TextBox"/></param>
        /// <returns>the <see cref="TextBox"/>'s contents</returns>
        public string GetContents(TextBox tb)
        {
            if (InvokeRequired)
            {
                return (string)Invoke(new Func<string>(() => GetContents(tb)));

                // NOTE: It's better to use the TaskCompletionSource version instead of this:
                //return (string)BeginInvoke(new Action(() => GetContents(tb))).AsyncState;
            }
            else
            {
                try { return tb.Text; }
                catch { return string.Empty; }
            }
        }

        /// <summary>
        /// Thread-safe method via <see cref="TaskCompletionSource{TResult}"/>
        /// </summary>
        /// <param name="tb"><see cref="TextBox"/></param>
        /// <returns>the <see cref="TextBox"/>'s contents</returns>
        public Task<string> GetContentsAsync(TextBox tb)
        {
            var tcs = new TaskCompletionSource<string>();
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() =>
                {
                    try { tcs.SetResult(tb.Text); }
                    catch (Exception ex) { tcs.SetException(ex); }
                }));
            }
            else
            {
                try { tcs.SetResult(tb.Text); }
                catch (Exception ex) { tcs.SetException(ex); }
            }
            return tcs.Task;
        }

        /// <summary>
        /// Thread-safe method via <see cref="System.Delegate"/>
        /// <example><code>
        /// GetContentsAsync(txtBoxCtrl, (text) => 
        /// { 
        ///     Debug.WriteLine($"{text}"); 
        /// });
        /// </code></example>
        /// </summary>
        /// <param name="tb"><see cref="TextBox"/></param>
        public void GetContentsAsync(TextBox tb, Action<string> callback)
        {
            if (InvokeRequired)
                tb.BeginInvoke(new Action(() => callback(GetContents(tb))));
            else
            {
                try { callback(tb.Text); }
                catch { callback(string.Empty); }
            }
        }

        /// <summary>
        /// Thread-safe method via <see cref="ValueTask{T}"/>
        /// </summary>
        /// <param name="tb"><see cref="TextBox"/></param>
        public ValueTask<string> GetContentsValueTask(TextBox tb)
        {
            if (tb.InvokeRequired)
                return new ValueTask<string>(tb.Invoke(new Func<string>(() => tb.Text)) as string);
            else
            {
                try { return new ValueTask<string>(tb.Text); }
                catch (Exception ex) { return ValueTask.FromException<string>(ex); }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="data">text to add to log</param>
        public void AddToListBox(ListBox lb, string data = "", bool timeStamp = false)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => AddToListBox(lb, data, timeStamp)));
            else
            {
                if (timeStamp)
                    lb.Items.Add("[" + DateTime.Now.ToString("hh:mm:ss tt fff") + "] " + data);
                else
                    lb.Items.Add(data);

                // auto-scroll the list box
                try { lb.SelectedIndex = lb.Items.Count - 1; }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="items"><see cref="List{T}"/> of items to add</param>
        public void AddToListBox(ListBox lb, List<string> items, bool timeStamp = false)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => AddToListBox(lb, items, timeStamp)));
            else
            {
                if (items.Count == 0)
                    return;

                lb.BeginUpdate(); // This is very important for performance (when adding many items).
                foreach (string item in items)
                {
                    if (timeStamp)
                        lb.Items.Add("[" + DateTime.Now.ToString("hh:mm:ss tt fff") + "] " + item);
                    else
                        lb.Items.Add(item);
                }
                lb.EndUpdate(); // This is very important for performance (when adding many items).

                // auto-scroll the list box
                try { lb.SelectedIndex = lb.Items.Count - 1; }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public object GetListBoxSelectedItem(ListBox lb)
        {
            if (lb.InvokeRequired)
                return (object)Invoke(new Func<object>(() => lb.SelectedItem));
            else
            {
                try { return lb.SelectedItem; }
                catch { return null; }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="data">text to add to log</param>
        public void AddToListView(ListView lv, string data = "", bool timeStamp = false)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => AddToListView(lv, data, timeStamp)));
            else
            {
                if (timeStamp)
                    lv.Items.Add("[" + DateTime.Now.ToString("hh:mm:ss tt fff") + "] " + data);
                else
                    lv.Items.Add(data);
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="items"><see cref="List{T}"/> of items to add</param>
        public void AddToListView(ListView lv, List<string> items, bool timeStamp = false)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => AddToListView(lv, items, timeStamp)));
            else
            {
                lv.BeginUpdate(); // This is very important for performance (when adding many items)
                foreach (string item in items)
                {
                    if (timeStamp)
                        lv.Items.Add("[" + DateTime.Now.ToString("hh:mm:ss tt fff") + "] " + item);
                    else
                        lv.Items.Add(item);
                }
                lv.EndUpdate(); // This is very important for performance (when adding many items)
            }
        }

        /// <summary>
        /// <see cref="System.Windows.Forms.View"/> options...
        /// LargeIcon: Each item appears as a full-sized icon with a label below it.
        /// Details: Each item appears on a separate line with further information about each item arranged in columns.
        /// SmallIcon: Each item appears as a small icon with a label to its right.
        /// List: Each item appears as a small icon with a label to its right.
        /// Tile: Each item appears as a full-sized icon with the item label and subitem information to the right of it.
        /// </summary>
        public void SetListViewView(ListView lv, System.Windows.Forms.View view)
        {
            if (lv.InvokeRequired)
                lv.Invoke(new Action(() => lv.View = view));
            else
                lv.View = view;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void ClearListBox(ListBox lb)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ClearListBox(lb)));
            else
                lb.Items.Clear();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void ClearListView(ListView lv)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ClearListView(lv)));
            else
                lv.Items.Clear();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="tb"><see cref="TextBox"/></param>
        /// <param name="data">text to append</param>
        public void ClearTextBox(TextBox tb)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ClearTextBox(tb)));
            else
                tb.Clear();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="tb"><see cref="TextBox"/></param>
        public void ReplaceTextBox(TextBox tb, string target, string replacement = "")
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ReplaceTextBox(tb, target, replacement)));
            else
                tb.Text.Replace(target, replacement);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void RemoveItemFromListBox(ListBox listBox, object item)
        {
            if (listBox.InvokeRequired)
                listBox.Invoke(new Action(() => listBox.Items.Remove(item)));
            else
                listBox.Items.Remove(item);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="data">text to insert into the list</param>
        /// <param name="index">location of insertion</param>
        public void ChangeListBoxItem(ListBox lb, string data, int index)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ChangeListBoxItem(lb, data, index)));
            else
            {
                if ((lb.Items.Count > 0) && (index <= lb.Items.Count))
                {
                    lb.Items[index] = data;
                    lb.SelectedIndex = index;
                }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="data">text to insert into the list</param>
        /// <param name="index">location of insertion</param>
        public void ChangeListViewItem(ListView lv, string data, int index)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => ChangeListViewItem(lv, data, index)));
            else
            {
                if ((lv.Items.Count > 0) && (index <= lv.Items.Count))
                {
                    lv.Items.RemoveAt(index);
                    lv.Items.Insert(index, data);
                }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="nud"><see cref="NumericUpDown"/></param>
        public void SetNumericUpDownValue(NumericUpDown nud, decimal value)
        {
            if (nud.InvokeRequired)
                nud.Invoke(new Action(() => nud.Value = value));
            else
                nud.Value = value;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetTrackBarValue(TrackBar tb, int value)
        {
            if (tb.InvokeRequired)
                tb.Invoke(new Action(() => tb.Value = value));
            else
                tb.Value = value;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetToolStripStatusLabelText(ToolStripStatusLabel tssl, string text)
        {
            if (tssl.Owner.InvokeRequired)
                tssl.Owner.Invoke(new Action(() => tssl.Text = text));
            else
                tssl.Text = text;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetStatusStripText(StatusStrip ss, string text)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => SetStatusStripText(ss, text)));
            else
            {
                if (ss.Items[0].GetType() == typeof(ToolStripStatusLabel))
                    ss.Items[0].Text = text;
                else
                    MessageBox.Show("The first item in the StatusStrip is not a ToolStripStatusLabel.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void AddRowToDataGridView(DataGridView dgv, params object[] values)
        {
            if (dgv.InvokeRequired)
                dgv.Invoke(new Action(() => dgv.Rows.Add(values)));
            else
                dgv.Rows.Add(values);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="tc"><see cref="TabControl"/></param>
        public void SelectTabControlTabByIndex(TabControl tc, int index)
        {
            if (tc.InvokeRequired)
                tc.Invoke(new Action(() => tc.SelectedIndex = index));
            else
                tc.SelectedIndex = index;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="cb"><see cref="ComboBox"/></param>
        public void SelectComboBoxItemByIndex(ComboBox cb, int index)
        {
            if (cb.InvokeRequired)
                cb.Invoke(new Action(() => cb.SelectedIndex = index));
            else
                cb.SelectedIndex = index;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="cb"><see cref="ComboBox"/></param>
        public object GetComboBoxSelectedItem(ComboBox cb)
        {
            if (cb.InvokeRequired)
                return (object)Invoke(new Func<object>(() => cb.SelectedItem));
            else
            {
                try { return cb.SelectedItem; }
                catch { return null; }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetToolTipText(ToolTip tt, Control ctrl, string text)
        {
            if (ctrl.InvokeRequired)
                ctrl.Invoke(new Action(() => tt.SetToolTip(ctrl, text)));
            else
                tt.SetToolTip(ctrl, text);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetDateTimePickerValue(DateTimePicker dtp, DateTime value)
        {
            if (dtp.InvokeRequired)
                dtp.Invoke(new Action(() => dtp.Value = value));
            else
                dtp.Value = value;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetMaskedTextBoxMask(MaskedTextBox mtb, string mask)
        {
            if (mtb.InvokeRequired)
                mtb.Invoke(new Action(() => mtb.Mask = mask));
            else
                mtb.Mask = mask;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetToolStripMenuItemEnabled(ToolStripMenuItem mi, bool enabled)
        {
            if (mi.Owner.InvokeRequired)
                mi.Owner.Invoke(new Action(() => mi.Enabled = enabled));
            else
                mi.Enabled = enabled;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="gb"><see cref="GroupBox"/></param>
        public void SetGroupBoxText(GroupBox gb, string text)
        {
            if (gb.InvokeRequired)
                gb.Invoke(new Action(() => gb.Text = text));
            else
                gb.Text = text;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void AddControlToFlowLayoutPanel(FlowLayoutPanel flp, Control control)
        {
            if (flp.InvokeRequired)
                flp.Invoke(new Action(() => flp.Controls.Add(control)));
            else
                flp.Controls.Add(control);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetCheckedListBoxItemChecked(CheckedListBox clb, int index, bool isChecked)
        {
            if (clb.InvokeRequired)
                clb.Invoke(new Action(() => clb.SetItemChecked(index, isChecked)));
            else
                clb.SetItemChecked(index, isChecked);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public string GetCheckedListBoxItemText(CheckedListBox clb, int index)
        {
            if (clb.InvokeRequired)
                return (string)Invoke(new Func<string>(() => clb.GetItemText(index)));
            else
            {
                try { return clb.GetItemText(index); }
                catch { return string.Empty; }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public bool GetCheckedListBoxItemChecked(CheckedListBox clb, int index)
        {
            if (clb.InvokeRequired)
                return (bool)Invoke(new Func<bool>(() => clb.GetItemChecked(index)));
            else
            {
                try { return clb.GetItemChecked(index); }
                catch { return false; }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetSplitterDistance(Splitter spltr, int distance)
        {
            if (spltr.InvokeRequired)
                spltr.Invoke(new Action(() => spltr.SplitPosition = distance));
            else
                spltr.SplitPosition = distance;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetHScrollBarValue(HScrollBar hsb, int value)
        {
            if (hsb.InvokeRequired)
                hsb.Invoke(new Action(() => hsb.Value = value));
            else
                hsb.Value = value;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetVScrollBarValue(VScrollBar vsb, int value)
        {
            if (vsb.InvokeRequired)
                vsb.Invoke(new Action(() => vsb.Value = value));
            else
                vsb.Value = value;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void AddNodeToTreeView(TreeView tv, TreeNode node)
        {
            if (tv.InvokeRequired)
                tv.Invoke(new Action(() => tv.Nodes.Add(node)));
            else
                tv.Nodes.Add(node);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void RemoveNodeFromTreeView(TreeView tv, TreeNode node)
        {
            if (tv.InvokeRequired)
                tv.Invoke(new Action(() => tv.Nodes.Remove(node)));
            else
                tv.Nodes.Remove(node);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void ClearTreeViewNodes(TreeView tv)
        {
            if (tv.InvokeRequired)
                tv.Invoke(new Action(() => tv.Nodes.Clear()));
            else
                tv.Nodes.Clear();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void AddTabPageToTabControl(TabControl tc, TabPage tp)
        {
            if (tc.InvokeRequired)
                tc.Invoke(new Action(() => tc.TabPages.Add(tp)));
            else
                tc.TabPages.Add(tp);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void RemoveTabPageFromTabControl(TabControl tc, TabPage tp)
        {
            if (tc.InvokeRequired)
                tc.Invoke(new Action(() => tc.TabPages.Remove(tp)));
            else
                tc.TabPages.Remove(tp);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="lv"><see cref="ListView"/></param>
        public void SetListViewItemText(ListView lv, int itemIndex, int subItemIndex, string text)
        {
            if (lv.InvokeRequired)
                lv.Invoke(new Action(() => lv.Items[itemIndex].SubItems[subItemIndex].Text = text));
            else
                lv.Items[itemIndex].SubItems[subItemIndex].Text = text;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetDataGridViewCellValue(DataGridView dgv, int rowIndex, int columnIndex, object value)
        {
            if (dgv.InvokeRequired)
                dgv.Invoke(new Action(() => dgv.Rows[rowIndex].Cells[columnIndex].Value = value));
            else
                dgv.Rows[rowIndex].Cells[columnIndex].Value = value;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetTabPageText(TabControl tc, int tabIndex, string text)
        {
            if (tc.InvokeRequired)
                tc.Invoke(new Action(() => tc.TabPages[tabIndex].Text = text));
            else
                tc.TabPages[tabIndex].Text = text;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void AddControlToPanel(Panel pnl, Control control)
        {
            if (pnl.InvokeRequired)
                pnl.Invoke(new Action(() => pnl.Controls.Add(control)));
            else
                pnl.Controls.Add(control);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void RemoveControlFromPanel(Panel pnl, Control control)
        {
            if (pnl.InvokeRequired)
                pnl.Invoke(new Action(() => pnl.Controls.Remove(control)));
            else
                pnl.Controls.Remove(control);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        public void SetToolStripProgressBarValue(ToolStripProgressBar tspb, int value)
        {
            if (tspb.Owner.InvokeRequired)
                tspb.Owner.Invoke(new Action(() => tspb.Value = value));
            else
                tspb.Value = value;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="tsb"><see cref="ToolStripButton"/></param>
        public void SetToolStripButtonChecked(ToolStripButton tsb, bool isChecked)
        {
            if (tsb.Owner.InvokeRequired)
                tsb.Owner.Invoke(new Action(() => tsb.Checked = isChecked));
            else
                tsb.Checked = isChecked;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="tv"><see cref="TreeView"/></param>
        public void ExpandAllTreeViewNodes(TreeView tv)
        {
            if (tv.InvokeRequired)
                tv.Invoke(new Action(() => tv.ExpandAll()));
            else
                tv.ExpandAll();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="tv"><see cref="TreeView"/></param>
        public void CollapseAllTreeViewNodes(TreeView tv)
        {
            if (tv.InvokeRequired)
                tv.Invoke(new Action(() => tv.CollapseAll()));
            else
                tv.CollapseAll();
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="ni"><see cref="NotifyIcon"/></param>
        public void ShowNotifyIconBalloonTip(NotifyIcon ni, int timeout, string tipTitle, string tipText, ToolTipIcon icon)
        {
            if (ni.Icon != null && ni.Visible)
            {
                if (ni.Container is ISynchronizeInvoke syncObj && syncObj.InvokeRequired)
                    syncObj.Invoke(new Action(() => ni.ShowBalloonTip(timeout, tipTitle, tipText, icon)), null);
                else
                    ni.ShowBalloonTip(timeout, tipTitle, tipText, icon);
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="mc"><see cref="MonthCalendar"/></param>
        public void SetMonthCalendarSelectionRange(MonthCalendar mc, DateTime start, DateTime end)
        {
            if (mc.InvokeRequired)
                mc.Invoke(new Action(() => mc.SelectionRange = new SelectionRange(start, end)));
            else
                mc.SelectionRange = new SelectionRange(start, end);
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="dtp"><see cref="DateTimePicker"/></param>
        public void SetDateTimePickerFormat(DateTimePicker dtp, DateTimePickerFormat format)
        {
            if (dtp.InvokeRequired)
                dtp.Invoke(new Action(() => dtp.Format = format));
            else
                dtp.Format = format;
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="dgv"><see cref="DataGridView"/></param>
        public void EnableDataGridViewDoubleBuffering(DataGridView dgv, bool enable)
        {
            if (dgv.InvokeRequired)
            {
                dgv.Invoke(new Action(() => {
                    var dgvType = dgv.GetType();
                    var pi = dgvType.GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    pi.SetValue(dgv, enable, null);
                }));
            }
            else
            {
                try
                {
                    var dgvType = dgv.GetType();
                    var pi = dgvType.GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    pi.SetValue(dgv, enable, null);
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Thread-safe method
        /// </summary>
        /// <param name="frm"><see cref="Form"/></param>
        public void EnableFormDoubleBuffering(Form frm, bool enable)
        {
            if (frm.InvokeRequired)
            {
                frm.Invoke(new Action(() => {
                    var dgvType = frm.GetType();
                    var pi = dgvType.GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    pi.SetValue(frm, enable, null);
                }));
            }
            else
            {
                try
                {
                    var dgvType = frm.GetType();
                    var pi = dgvType.GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    pi.SetValue(frm, enable, null);
                }
                catch (Exception) { }
            }
        }
        #endregion
    }
}

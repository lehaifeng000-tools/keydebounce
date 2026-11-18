using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("KeyDebounce")]
[assembly: AssemblyProduct("KeyDebounce")]
[assembly: AssemblyFileVersion("1.2.0.0")]

internal sealed class DebounceKey
{
    private enum Press { None, Passed, Bypassed, Blocked }

    private Press press;
    private uint? lastRelease;
    public int BlockedCount { get; private set; }

    public bool ShouldBlock(bool isRelease, uint time, int windowMs, bool shortcut)
    {
        if (isRelease)
        {
            bool blockRelease = press == Press.Blocked;
            if (press == Press.Passed)
                lastRelease = windowMs > 0 ? (uint?)time : null;
            press = Press.None;
            return blockRelease;
        }

        // Repeated down events from a held key follow its original decision.
        if (press != Press.None)
            return press == Press.Blocked;

        if (windowMs == 0 || shortcut)
        {
            lastRelease = null;
            press = Press.Bypassed;
            return false;
        }

        if (lastRelease.HasValue && unchecked(time - lastRelease.Value) < (uint)windowMs)
        {
            press = Press.Blocked;
            BlockedCount++;
            return true;
        }

        lastRelease = null;
        press = Press.Passed;
        return false;
    }

    public void ClearHistory()
    {
        lastRelease = null;
    }
}

[Flags]
internal enum KeyGroup
{
    None = 0,
    Letters = 1,
    Space = 2,
    Punctuation = 4,
    NumberRow = 8,
    AllKeys = 16,
    Defaults = Letters | Space | Punctuation | NumberRow
}

internal sealed class FilterSettings
{
    private const KeyGroup ValidGroups = KeyGroup.Defaults | KeyGroup.AllKeys;
    internal const int MinimumMs = 5;
    internal const int MaximumMs = 120;

    public KeyGroup Groups = KeyGroup.Defaults;
    public int WindowMs = 30;

    public static FilterSettings Load(string path)
    {
        var settings = new FilterSettings();
        if (!File.Exists(path))
            return settings;

        foreach (string line in File.ReadAllLines(path))
        {
            string[] pair = line.Split('=');
            int value;
            if (pair.Length != 2 || !int.TryParse(pair[1], out value))
                continue;
            if (pair[0] == "Groups" && (value & ~(int)ValidGroups) == 0)
                settings.Groups = (KeyGroup)value;
            else if (pair[0] == "WindowMs" && value >= MinimumMs && value <= MaximumMs)
                settings.WindowMs = value;
        }
        return settings;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string pending = path + ".pending";
        File.WriteAllLines(pending, new[] { "Groups=" + (int)Groups, "WindowMs=" + WindowMs });
        if (File.Exists(path))
            File.Replace(pending, path, null);
        else
            File.Move(pending, path);
    }
}

internal sealed class KeyboardHook : IDisposable
{
    private const int LowLevelKeyboard = 13;
    private const int Injected = 0x10;
    private const int AltDown = 0x20;
    private const int Released = 0x80;

    private sealed class TrackedKey
    {
        public readonly KeyGroup Group;
        public readonly string Name;
        public readonly DebounceKey Filter = new DebounceKey();

        public TrackedKey(KeyGroup group, string name)
        {
            Group = group;
            Name = name;
        }
    }

    private readonly HookProc callback;
    private readonly TrackedKey[] trackedKeys = new TrackedKey[256];
    private IntPtr handle;
    private KeyGroup enabledGroups;
    private int windowMs;

    public KeyboardHook()
    {
        for (int i = 0; i < 26; i++)
            AddKey(Keys.A + i, KeyGroup.Letters, ((char)('A' + i)).ToString());
        for (int i = 0; i < 10; i++)
            AddKey(Keys.D0 + i, KeyGroup.NumberRow, ((char)('0' + i)).ToString());

        AddKey(Keys.Space, KeyGroup.Space, "空格");
        AddKey(Keys.Oemcomma, KeyGroup.Punctuation, ",");
        AddKey(Keys.OemPeriod, KeyGroup.Punctuation, ".");
        AddKey(Keys.OemSemicolon, KeyGroup.Punctuation, ";");
        AddKey(Keys.OemQuotes, KeyGroup.Punctuation, "'");
        AddKey(Keys.OemQuestion, KeyGroup.Punctuation, "/");
        AddKey(Keys.OemMinus, KeyGroup.Punctuation, "-");
        AddKey(Keys.Oemplus, KeyGroup.Punctuation, "=");
        AddKey(Keys.OemOpenBrackets, KeyGroup.Punctuation, "[");
        AddKey(Keys.OemCloseBrackets, KeyGroup.Punctuation, "]");
        AddKey(Keys.OemPipe, KeyGroup.Punctuation, "\\");
        AddKey(Keys.Oemtilde, KeyGroup.Punctuation, "`");

        for (int code = 8; code < 255; code++)
        {
            if (trackedKeys[code] != null)
                continue;
            Keys key = (Keys)code;
            string name = key.ToString();
            AddKey(key, KeyGroup.AllKeys, char.IsDigit(name[0]) ? "VK " + code.ToString("X2") : name);
        }

        callback = OnKeyboard;
    }

    private void AddKey(Keys key, KeyGroup group, string name)
    {
        trackedKeys[(int)key] = new TrackedKey(group, name);
    }

    public void Start()
    {
        if (handle != IntPtr.Zero)
            return;

        handle = SetWindowsHookEx(LowLevelKeyboard, callback, GetModuleHandle(null), 0);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void SetOptions(KeyGroup newGroups, int newWindowMs)
    {
        foreach (TrackedKey key in trackedKeys)
            if (key != null && Includes(enabledGroups, key.Group) != Includes(newGroups, key.Group))
                key.Filter.ClearHistory();
        enabledGroups = newGroups;
        windowMs = newWindowMs;
    }

    private static bool Includes(KeyGroup selected, KeyGroup group)
    {
        return (selected & (group | KeyGroup.AllKeys)) != 0;
    }

    public string FormatBlockedCounts()
    {
        var counts = new List<string>();
        foreach (TrackedKey key in trackedKeys)
            if (key != null && key.Filter.BlockedCount > 0)
                counts.Add(string.Format("{0}  {1}", key.Name, key.Filter.BlockedCount));
        return counts.Count == 0 ? "本次运行尚无拦截记录。" : string.Join("    ", counts);
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code == 0)
        {
            uint virtualKey = unchecked((uint)Marshal.ReadInt32(data));
            TrackedKey key = virtualKey < trackedKeys.Length ? trackedKeys[virtualKey] : null;
            if (key == null)
                return CallNextHookEx(IntPtr.Zero, code, message, data);

            // The DWORD fields match the layout of KBDLLHOOKSTRUCT in both architectures.
            int flags = Marshal.ReadInt32(data, 8);
            if ((flags & Injected) == 0)
            {
                bool isRelease = (flags & Released) != 0;
                uint time = unchecked((uint)Marshal.ReadInt32(data, 12));
                int threshold = Includes(enabledGroups, key.Group) ? windowMs : 0;
                bool shortcut = threshold != 0 && !isRelease && !IsModifier((Keys)virtualKey) &&
                    ((flags & AltDown) != 0 || IsDown(Keys.ControlKey) || IsDown(Keys.LWin) || IsDown(Keys.RWin));

                if (key.Filter.ShouldBlock(isRelease, time, threshold, shortcut))
                    return new IntPtr(1);
            }
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private static bool IsModifier(Keys key)
    {
        return key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu ||
            (key >= Keys.LShiftKey && key <= Keys.RMenu) || key == Keys.LWin || key == Keys.RWin;
    }

    private static bool IsDown(Keys key)
    {
        return GetAsyncKeyState((int)key) < 0;
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(handle);
            handle = IntPtr.Zero;
        }
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string name);
}

internal sealed class DebounceWindow : Form
{
    private readonly KeyboardHook hook = new KeyboardHook();
    private readonly Dictionary<KeyGroup, CheckBox> groups = new Dictionary<KeyGroup, CheckBox>();
    private readonly string settingsPath;
    private readonly NumericUpDown windowMs = new NumericUpDown();
    private readonly Button toggle = new Button();
    private readonly TextBox counts = new TextBox();
    private readonly Label footer = new Label();
    private readonly Icon windowIcon;
    private readonly Icon notificationIcon;
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
    private readonly ToolStripMenuItem trayToggle = new ToolStripMenuItem();
    private readonly CheckBox autoStart = new CheckBox();
    private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
    private bool enabled;
    private bool exiting;
    private bool refreshingAutoStart;

    public DebounceWindow(string settingsPath)
    {
        this.settingsPath = settingsPath;
        var settings = new FilterSettings();
        bool loadFailed = false;
        try { settings = FilterSettings.Load(settingsPath); }
        catch (IOException) { loadFailed = true; }
        catch (UnauthorizedAccessException) { loadFailed = true; }

        using (Stream iconData = Assembly.GetExecutingAssembly().GetManifestResourceStream("KeyDebounce.Icon"))
        {
            windowIcon = new Icon(iconData, SystemInformation.IconSize);
            iconData.Position = 0;
            notificationIcon = new Icon(iconData, SystemInformation.SmallIconSize);
        }
        Text = "KeyDebounce";
        Icon = windowIcon;
        ClientSize = new Size(410, 319);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        AddGroup(KeyGroup.Letters, "字母 A–Z", 16, 14);
        AddGroup(KeyGroup.Space, "空格", 191, 14);
        AddGroup(KeyGroup.Punctuation, "常用标点", 16, 42);
        AddGroup(KeyGroup.NumberRow, "数字行 0–9（含 !@# 等）", 191, 42);
        AddGroup(KeyGroup.AllKeys, "全部按键", 16, 70);
        foreach (KeyValuePair<KeyGroup, CheckBox> group in groups)
        {
            group.Value.Checked = (settings.Groups & group.Key) != 0;
            group.Value.CheckedChanged += delegate { OptionsChanged(); };
        }
        UpdateGroupControls();

        AddLabel("去抖间隔（毫秒）", 16, 112);
        windowMs.Location = new Point(130, 107);
        windowMs.Size = new Size(64, 24);
        windowMs.Minimum = FilterSettings.MinimumMs;
        windowMs.Maximum = FilterSettings.MaximumMs;
        windowMs.Increment = 5;
        windowMs.Value = settings.WindowMs;
        windowMs.ValueChanged += delegate { OptionsChanged(); };
        Controls.Add(windowMs);

        toggle.Text = "开始过滤";
        toggle.SetBounds(16, 146, 97, 31);
        toggle.Click += delegate { SetFiltering(!enabled); };
        Controls.Add(toggle);

        AddLabel("拦截次数（按键）", 130, 155);
        counts.SetBounds(16, 185, 378, 59);
        counts.ReadOnly = true;
        counts.Multiline = true;
        counts.ScrollBars = ScrollBars.Vertical;
        counts.TabStop = false;
        Controls.Add(counts);
        autoStart.Text = "登录 Windows 时自动启动";
        autoStart.AutoSize = true;
        autoStart.Location = new Point(16, 257);
        autoStart.Enabled = WindowsInstallation.IsInstalled;
        UpdateAutoStartCheckbox();
        autoStart.CheckedChanged += AutoStartChanged;
        Controls.Add(autoStart);

        var uninstall = new Button { Text = "卸载…", Enabled = autoStart.Enabled };
        uninstall.SetBounds(307, 251, 87, 28);
        uninstall.Click += UninstallClicked;
        Controls.Add(uninstall);
        footer.AutoSize = true;
        footer.Location = new Point(16, 292);
        footer.Text = loadFailed ? "设置读取失败，已使用默认值。" : "关闭窗口后仍在托盘运行。";
        Controls.Add(footer);
        UpdateCounts();

        tray.Icon = notificationIcon;
        tray.Text = "KeyDebounce（已暂停）";
        trayMenu.Items.Add("打开设置", null, delegate { ShowSettings(); });
        trayToggle.Text = "开始过滤";
        trayToggle.Click += delegate { SetFiltering(!enabled); };
        trayMenu.Items.Add(trayToggle);
        trayMenu.Items.Add("退出", null, delegate { exiting = true; Close(); });
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += delegate { ShowSettings(); };
        tray.Visible = true;

        refresh.Interval = 300;
        refresh.Tick += delegate { UpdateCounts(); };
        VisibleChanged += delegate { refresh.Enabled = Visible; };
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) });
    }

    private void AddGroup(KeyGroup group, string text, int x, int y)
    {
        var checkbox = new CheckBox { Text = text, AutoSize = true };
        checkbox.Location = new Point(x, y);
        groups.Add(group, checkbox);
        Controls.Add(checkbox);
    }

    public void ShowSettings()
    {
        WindowState = FormWindowState.Normal;
        UpdateAutoStartCheckbox();
        Show();
        UpdateCounts();
        Activate();
    }

    public void SetFiltering(bool active)
    {
        try
        {
            if (active)
                hook.Start();
        }
        catch (Win32Exception error)
        {
            if (Visible)
                MessageBox.Show(this, "无法启用过滤。\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else
                tray.ShowBalloonTip(3000, Text, "无法启用过滤。" + error.Message, ToolTipIcon.Error);
            return;
        }

        enabled = active;
        toggle.Text = enabled ? "暂停过滤" : "开始过滤";
        trayToggle.Text = toggle.Text;
        tray.Text = enabled ? "KeyDebounce（过滤中）" : "KeyDebounce（已暂停）";
        ApplyOptions();
        UpdateCounts();
    }

    private void UpdateAutoStartCheckbox()
    {
        refreshingAutoStart = true;
        autoStart.Checked = autoStart.Enabled && WindowsInstallation.StartsWithWindows;
        refreshingAutoStart = false;
    }

    private void AutoStartChanged(object sender, EventArgs args)
    {
        if (refreshingAutoStart)
            return;

        try { WindowsInstallation.SetStartsWithWindows(autoStart.Checked); }
        catch (Exception error)
        {
            if (!(error is IOException || error is UnauthorizedAccessException || error is COMException))
                throw;
            UpdateAutoStartCheckbox();
            MessageBox.Show(this, "无法更新自动启动。\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UninstallClicked(object sender, EventArgs args)
    {
        if (MessageBox.Show(this, "要卸载 KeyDebounce 吗？卸载会停止过滤并移除登录启动项，已保存的过滤设置会保留。",
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        try { WindowsInstallation.Uninstall(); }
        catch (Win32Exception error)
        {
            MessageBox.Show(this, "无法启动卸载程序。\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        exiting = true;
        Close();
    }

    private KeyGroup SelectedGroups()
    {
        KeyGroup selectedGroups = KeyGroup.None;
        foreach (KeyValuePair<KeyGroup, CheckBox> group in groups)
            if (group.Value.Checked)
                selectedGroups |= group.Key;
        return selectedGroups;
    }

    private void UpdateGroupControls()
    {
        bool allKeys = groups[KeyGroup.AllKeys].Checked;
        foreach (KeyValuePair<KeyGroup, CheckBox> group in groups)
            group.Value.Enabled = group.Key == KeyGroup.AllKeys || !allKeys;
    }

    private void OptionsChanged()
    {
        UpdateGroupControls();
        ApplyOptions();
        try
        {
            new FilterSettings { Groups = SelectedGroups(), WindowMs = (int)windowMs.Value }.Save(settingsPath);
            footer.Text = "关闭窗口后仍在托盘运行。";
        }
        catch (IOException) { footer.Text = "设置保存失败，本次运行仍按当前选择过滤。"; }
        catch (UnauthorizedAccessException) { footer.Text = "设置保存失败，本次运行仍按当前选择过滤。"; }
    }

    private void ApplyOptions()
    {
        hook.SetOptions(enabled ? SelectedGroups() : KeyGroup.None, (int)windowMs.Value);
    }

    private void UpdateCounts()
    {
        string updated = hook.FormatBlockedCounts();
        if (counts.Text != updated)
            counts.Text = updated;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
            Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refresh.Dispose();
            tray.Dispose();
            trayMenu.Dispose();
            notificationIcon.Dispose();
            hook.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
            windowIcon.Dispose();
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool startup = Array.IndexOf(args, "--startup") >= 0;
        bool created;
        using (var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Codex.KeyDebounce.Show"))
        using (var singleInstance = new Mutex(false, @"Local\Codex.KeyDebounce", out created))
        {
            if (!created)
            {
                if (!startup)
                    showRequest.Set();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeyDebounce", "settings.ini");
            using (var window = new DebounceWindow(settingsPath))
            using (var context = new ApplicationContext())
            {
                if (window.Handle == IntPtr.Zero)
                    return;
                window.FormClosed += delegate { context.ExitThread(); };
                RegisteredWaitHandle waiter = ThreadPool.RegisterWaitForSingleObject(showRequest, delegate
                {
                    try { window.BeginInvoke(new Action(window.ShowSettings)); }
                    catch (InvalidOperationException) { /* The window has exited. */ }
                }, null, Timeout.Infinite, false);
                try
                {
                    if (startup || Array.IndexOf(args, "--settings") >= 0)
                        window.SetFiltering(true);
                    if (!startup)
                        window.ShowSettings();
                    Application.Run(context);
                }
                finally { waiter.Unregister(null); }
            }
        }
    }
}

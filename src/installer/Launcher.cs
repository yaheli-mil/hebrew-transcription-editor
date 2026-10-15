// "עורך תמלול.exe": what the shortcut opens. Starts the editor's server with no window, opens the
// browser on it, and stays as an icon next to the clock (open / recordings folder / exit). The server
// and any transcription it runs are tied to this process (a job object), so exiting stops them all.
//
// Test flag: /nobrowser   start everything but do not open the browser.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("עורך תמלול")]

static class LauncherProgram
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        if (args.Length == 1 && args[0].StartsWith("/shot="))          // test: a picture of the start-up window
        {
            Splash s = new Splash();
            s.StartPosition = FormStartPosition.Manual;
            s.Location = new Point(-20000, -20000);
            s.Show();
            Application.DoEvents();
            using (Bitmap b = s.Snapshot()) b.Save(args[0].Substring(6));
            return;
        }
        bool noBrowser = Array.Exists(args, delegate (string a) { return a.Trim('/', '-').Equals("nobrowser", StringComparison.OrdinalIgnoreCase); });
        bool created;
        using (Mutex m = new Mutex(true, @"Local\HebrewTranscriptionEditor", out created))
        {
            if (!created)
            {
                int port = Editor.ReadPort(root);                 // already running: just show it again
                if (port > 0 && Editor.Ping(port)) { if (!noBrowser) Editor.Open(port); }
                else Editor.Error("העורך כבר נפתח ועדיין עולה. נסו שוב בעוד כמה שניות.");
                return;
            }
            Application.Run(new Editor(root, noBrowser));
        }
    }
}

class Editor : ApplicationContext
{
    readonly string root;
    readonly bool noBrowser;
    Process server;
    IntPtr job = IntPtr.Zero;
    NotifyIcon tray;
    Splash splash;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly List<string> errors = new List<string>();
    DateTime started;
    int port;
    bool quitting;
    SynchronizationContext ui;

    public Editor(string root, bool noBrowser)
    {
        this.root = root;
        this.noBrowser = noBrowser;
        splash = new Splash();
        splash.FormClosed += delegate { if (port == 0 && !quitting) Quit(); };
        splash.Show();
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        try { StartServer(); }
        catch (Exception ex) { Fail("לא הצלחתי להפעיל את העורך.\n" + ex.Message); return; }
        started = DateTime.Now;
        timer.Interval = 250;
        timer.Tick += delegate { WaitForServer(); };
        timer.Start();
    }

    string PortFile { get { return Path.Combine(root, "port.txt"); } }

    void StartServer()
    {
        string python = Path.Combine(root, "python", "python.exe");
        string script = Path.Combine(root, "app", "server.py");
        if (!File.Exists(python) || !File.Exists(script))
            throw new Exception("קובצי העורך חסרים. התקינו אותו מחדש.");
        try { File.Delete(PortFile); } catch { }
        ProcessStartInfo psi = new ProcessStartInfo(python, "-X utf8 \"" + script + "\" --port-file \"" + PortFile + "\"");
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = Path.Combine(root, "app");
        psi.RedirectStandardError = true;
        psi.RedirectStandardOutput = true;
        server = Process.Start(psi);
        job = Job.KillOnClose();
        if (job != IntPtr.Zero) Job.Add(job, server.Handle);
        server.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { Keep(e.Data); };
        server.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { Keep(e.Data); };
        server.BeginErrorReadLine();
        server.BeginOutputReadLine();
    }

    void Keep(string line)
    {
        if (line == null) return;
        lock (errors) { errors.Add(line); if (errors.Count > 30) errors.RemoveAt(0); }
    }

    void WaitForServer()
    {
        int p = ReadPort(root);
        if (p > 0 && Ping(p))
        {
            timer.Stop();
            port = p;
            Ready();
            return;
        }
        if (server.HasExited)
        {
            timer.Stop();
            string tail;
            lock (errors) tail = string.Join("\n", errors.ToArray());
            Fail("העורך נסגר בזמן שנפתח." + (tail.Length > 0 ? "\n\n" + tail : ""));
            return;
        }
        if ((DateTime.Now - started).TotalSeconds > 90)
        {
            timer.Stop();
            Fail("העורך לא הגיב. נסו לפתוח אותו שוב, ואם זה חוזר, הפעילו מחדש את המחשב.");
        }
    }

    void Ready()
    {
        tray = new NotifyIcon();
        tray.Icon = Theme.AppIcon(16) ?? SystemIcons.Application;
        tray.Text = Theme.AppName + " פועל";
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.RightToLeft = RightToLeft.Yes;
        menu.Font = Theme.Font(13 * DpiScale(), false);
        ToolStripItem open = menu.Items.Add("פתיחת העורך", null, delegate { Open(port); });
        open.Font = Theme.Font(13 * DpiScale(), true);
        menu.Items.Add("תיקיית ההקלטות", null, delegate { OpenFolder(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("נבנה על ידי " + Theme.Credit, null, delegate { Theme.OpenUrl(Theme.Instagram); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("יציאה", null, delegate { AskQuit(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { Open(port); };
        tray.Visible = true;
        server.EnableRaisingEvents = true;
        if (!server.HasExited) server.Exited += delegate
        {
            if (!quitting) ui.Post(delegate { if (!quitting) Fail("העורך נסגר בגלל תקלה. פתחו אותו שוב מקיצור הדרך."); }, null);
        };
        if (!noBrowser) Open(port);
        string seen = Path.Combine(root, ".welcomed");
        if (!File.Exists(seen))
        {
            tray.ShowBalloonTip(8000, Theme.AppName + " פועל",
                "העורך נפתח בדפדפן. הוא ממשיך לפעול כאן, ליד השעון, גם אם סוגרים את הדפדפן. כדי לסגור אותו: לחיצה ימנית על הסמל ← יציאה.",
                ToolTipIcon.None);
            try { File.WriteAllText(seen, DateTime.Now.ToString("s")); } catch { }
        }
        if (splash != null) { splash.Done = true; splash.Close(); splash = null; }
    }

    static float DpiScale()
    {
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
    }

    void OpenFolder()
    {
        string dir = Path.Combine(root, "הקלטות");
        Directory.CreateDirectory(dir);
        try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
    }

    void AskQuit()
    {
        if (Busy() && MessageBox.Show("הקלטה עדיין מתומללת. אם יוצאים עכשיו, התמלול שלה יתחיל מההתחלה בפעם הבאה שהעורך ייפתח.\n\nלצאת בכל זאת?",
                Theme.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2,
                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != DialogResult.Yes)
            return;
        Quit();
    }

    bool Busy()
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/api/list");
            req.Proxy = null;
            req.Timeout = 1500;
            using (WebResponse r = req.GetResponse())
            using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd().Contains("\"status\":\"working\"");
        }
        catch { return false; }
    }

    void Quit()
    {
        quitting = true;
        timer.Stop();
        if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
        if (job != IntPtr.Zero) { Job.Close(job); job = IntPtr.Zero; }     // ends the server and any transcription with it
        try { if (server != null && !server.HasExited) server.Kill(); } catch { }
        try { File.Delete(PortFile); } catch { }
        if (splash != null) { splash.Done = true; splash.Close(); }
        ExitThread();
    }

    void Fail(string msg)
    {
        if (splash != null) { splash.Done = true; splash.Hide(); }
        Error(msg);
        Quit();
    }

    // --------------------------------------------------------------- shared with a second launch

    public static int ReadPort(string root)
    {
        try
        {
            int p;
            return int.TryParse(File.ReadAllText(Path.Combine(root, "port.txt")).Trim(), out p) ? p : 0;
        }
        catch { return 0; }
    }

    public static bool Ping(int port)
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/api/ping");
            req.Proxy = null;
            req.Timeout = 1000;
            using (WebResponse r = req.GetResponse())
            using (StreamReader sr = new StreamReader(r.GetResponseStream()))
                return sr.ReadToEnd().Contains("transcription-editor-class");
        }
        catch { return false; }
    }

    public static void Open(int port)
    {
        Theme.OpenUrl("http://127.0.0.1:" + port + "/");
    }

    public static void Error(string msg)
    {
        MessageBox.Show(msg, Theme.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
    }
}

/// The small window shown while the editor starts (a few seconds).
class Splash : ModernForm
{
    public bool Done;
    float t;
    readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();

    public Splash() : base(380, 250)
    {
        ShowInTaskbar = true;
        TopMost = false;
        anim.Interval = 30;
        anim.Tick += delegate { t = (t + 0.015f) % 1f; Invalidate(BarRect()); };
        anim.Start();
    }

    Rectangle BarRect() { return new Rectangle(D(90), D(196), ClientSize.Width - D(180), D(6)); }

    protected override void PaintContent(Graphics g)
    {
        PaintHeader(g, 36, 60, Theme.AppName, "פותח את העורך…");
        Rectangle bar = BarRect();
        using (GraphicsPath p = Theme.Round(bar, bar.Height / 2f))
        using (SolidBrush b = new SolidBrush(Theme.Raised))
            g.FillPath(b, p);
        float seg = bar.Width * 0.35f;
        float x = bar.Right - (bar.Width + seg) * t;                  // a gold piece sliding right to left
        RectangleF piece = RectangleF.Intersect(new RectangleF(x, bar.Y, seg, bar.Height), bar);
        if (piece.Width > 1)
            using (GraphicsPath p = Theme.Round(piece, bar.Height / 2f))
            using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(piece.X - 1, piece.Y, piece.Width + 2, piece.Height), Theme.AccentLo, Theme.AccentHi, 0f))
                g.FillPath(b, p);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        anim.Stop();
        base.OnFormClosing(e);
    }
}

/// Windows job object: every process in it ends when this launcher ends, even if it crashes.
static class Job
{
    [StructLayout(LayoutKind.Sequential)]
    struct BASIC
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO { public ulong a, b, c, d, e, f; }

    [StructLayout(LayoutKind.Sequential)]
    struct EXTENDED
    {
        public BASIC Basic;
        public IO Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attrs, string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, ref EXTENDED info, int size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public static IntPtr KillOnClose()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return job;
        EXTENDED info = new EXTENDED();
        info.Basic.LimitFlags = 0x2000;                     // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(EXTENDED)))) { CloseHandle(job); return IntPtr.Zero; }
        return job;
    }

    public static void Add(IntPtr job, IntPtr process) { AssignProcessToJobObject(job, process); }
    public static void Close(IntPtr job) { CloseHandle(job); }
}

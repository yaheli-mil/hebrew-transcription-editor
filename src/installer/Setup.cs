// The installer ("התקנת עורך תמלול.exe"). The app itself is a zip appended to the end of this exe;
// installing unpacks it into the user's own Programs folder (no admin rights needed), adds
// shortcuts and an entry under Settings > Apps so it can be removed like any other program.
//
// Test flags: /silent /dir=PATH [/noshortcuts] [/noregistry]   install without the window
//             /shot=PAGE:FILE.png                               save a picture of a page (welcome, installing, done, failed)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("התקנת עורך תמלול")]

static class SetupProgram
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Dictionary<string, string> opt = Installer.ParseArgs(args);
        Installer inst = new Installer();
        if (opt.ContainsKey("dir")) inst.Dir = opt["dir"];
        inst.Shortcuts = !opt.ContainsKey("noshortcuts");
        inst.Registry = !opt.ContainsKey("noregistry");

        if (opt.ContainsKey("debug")) Installer.DebugLog = opt["debug"];
        if (opt.ContainsKey("silent"))
        {
            try { inst.Run(null); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        SetupForm form = new SetupForm(inst);
        if (opt.ContainsKey("shot"))
        {
            string[] parts = opt["shot"].Split(new[] { ':' }, 2);
            form.ShowPage(parts[0]);
            form.StartPosition = FormStartPosition.Manual;          // render off screen
            form.Location = new Point(-20000, -20000);
            form.Show();
            Application.DoEvents();
            using (Bitmap b = form.Snapshot()) b.Save(parts[1]);
            return 0;
        }
        Application.Run(form);
        return 0;
    }
}

/// The work itself, separate from the window so it can also run silently (for testing).
class Installer
{
    public string Dir;
    public bool Shortcuts = true, Registry = true, Desktop = true;
    public volatile bool Cancel;
    public long Total, Done;
    public string Stage = "";
    public bool Update;

    public const long Needed = 2300L * 1024 * 1024;

    public Installer()
    {
        Dir = Existing() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Theme.AppName);
        Update = Existing() != null;
    }

    public static Dictionary<string, string> ParseArgs(string[] args)
    {
        Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string a in args)
        {
            string s = a.TrimStart('/', '-');
            int eq = s.IndexOf('=');
            if (eq > 0) d[s.Substring(0, eq)] = s.Substring(eq + 1).Trim('"');
            else d[s] = "";
        }
        return d;
    }

    /// Where an earlier version is installed, if any.
    public static string Existing()
    {
        try
        {
            using (RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Theme.UninstallKey))
            {
                string loc = k == null ? null : k.GetValue("InstallLocation") as string;
                return loc != null && File.Exists(Path.Combine(loc, Theme.AppName + ".exe")) ? loc : null;
            }
        }
        catch { return null; }
    }

    public static bool InCloud(string path)
    {
        string p = path.ToLowerInvariant();
        return p.Contains("onedrive") || p.Contains("google drive") || p.Contains("googledrive") || p.Contains("dropbox") || p.Contains("icloud");
    }

    static void Payload(string exe, out long offset, out long length)
    {
        using (FileStream fs = File.OpenRead(exe))
        {
            byte[] tail = new byte[16];
            fs.Seek(-16, SeekOrigin.End);
            fs.Read(tail, 0, 16);
            if (Encoding.ASCII.GetString(tail, 0, 8) != "EDITZIP1") throw new Exception("קובץ ההתקנה פגום. הורידו אותו שוב.");
            length = BitConverter.ToInt64(tail, 8);
            offset = fs.Length - 16 - length;
            if (offset <= 0) throw new Exception("קובץ ההתקנה פגום. הורידו אותו שוב.");
        }
    }

    /// Close a copy of the editor that runs from this folder, so its files can be replaced.
    public static void StopRunning(string dir)
    {
        string full = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                string path = p.MainModule.FileName;
                if (path.StartsWith(full, StringComparison.OrdinalIgnoreCase) && p.Id != Process.GetCurrentProcess().Id)
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch { }
        }
    }

    public static string DebugLog;
    static void Log(string m) { if (DebugLog != null) try { File.AppendAllText(DebugLog, DateTime.Now.ToString("HH:mm:ss.fff ") + m + Environment.NewLine); } catch { } }

    public void Run(Action progress)
    {
        Log("run");
        string dir = Path.GetFullPath(Dir);
        if (dir.Length > 120)                                    // the deepest file inside is ~130 characters; Windows allows 260
            throw new Exception("הנתיב של תיקיית ההתקנה ארוך מדי. בחרו תיקייה עם נתיב קצר יותר, או השאירו את ברירת המחדל.");
        string root = Path.GetPathRoot(dir);
        if (new DriveInfo(root).AvailableFreeSpace < Needed)
            throw new Exception("אין מספיק מקום פנוי בכונן " + root.TrimEnd('\\') + ". צריך בערך 2.3GB פנויים.");
        Log("space ok");
        long offset, length;
        Payload(Application.ExecutablePath, out offset, out length);
        Log("payload " + offset + " " + length);
        Stage = "סוגר גרסה פתוחה של העורך…";
        if (progress != null) progress();
        StopRunning(dir);
        Log("stopped");
        Directory.CreateDirectory(dir);
        foreach (string sub in new[] { "app", "python", "model" })              // the recordings ("הקלטות") stay
        {
            string p = Path.Combine(dir, sub);
            if (Directory.Exists(p)) Directory.Delete(p, true);
        }
        string prefix = dir.TrimEnd('\\') + "\\";
        try
        {
            using (FileStream fs = new FileStream(Application.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (SubStream sub = new SubStream(fs, offset, length))
            using (ZipArchive zip = new ZipArchive(sub, ZipArchiveMode.Read))
            {
                Total = 0;
                foreach (ZipArchiveEntry en in zip.Entries) Total += en.Length;
                Done = 0;
                byte[] buf = new byte[1 << 20];
                DateTime last = DateTime.MinValue;
                foreach (ZipArchiveEntry en in zip.Entries)
                {
                    if (Cancel) throw new OperationCanceledException();
                    string target = Path.GetFullPath(Path.Combine(dir, en.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (en.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    Stage = en.Name == "model.bin" ? "מעתיק את מודל התמלול (1.6GB)…" : "מעתיק קבצים…";
                    using (Stream src = en.Open())
                    using (FileStream dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            if (Cancel) throw new OperationCanceledException();
                            dst.Write(buf, 0, n);
                            Done += n;
                            if (progress != null && (DateTime.Now - last).TotalMilliseconds > 80) { last = DateTime.Now; progress(); }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            foreach (string sub in new[] { "app", "python", "model" })
                try { Directory.Delete(Path.Combine(dir, sub), true); } catch { }
            throw;
        }
        Stage = "יוצר קיצורי דרך…";
        if (progress != null) progress();
        string exe = Path.Combine(dir, Theme.AppName + ".exe");
        if (Registry) Register(dir, exe);
        if (Shortcuts)
        {
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Theme.AppName + ".lnk"), exe, dir);
            string desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Theme.AppName + ".lnk");
            if (Desktop) Shortcut(desk, exe, dir);
            else if (File.Exists(desk)) File.Delete(desk);
        }
        Done = Total;
    }

    void Register(string dir, string exe)
    {
        using (RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Theme.UninstallKey))
        {
            k.SetValue("DisplayName", Theme.AppName);
            k.SetValue("DisplayVersion", Theme.Version);
            k.SetValue("Publisher", Theme.Credit);
            k.SetValue("DisplayIcon", exe);
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", "\"" + Path.Combine(dir, "uninstall.exe") + "\"");
            k.SetValue("URLInfoAbout", Theme.Instagram);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(Total / 1024), RegistryValueKind.DWord);
        }
    }

    static void Shortcut(string lnk, string target, string dir)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(t);
        dynamic s = shell.CreateShortcut(lnk);
        s.TargetPath = target;
        s.WorkingDirectory = dir;
        s.IconLocation = target + ",0";
        s.Description = "תמלול ראיונות בעברית";
        s.Save();
        Marshal.FinalReleaseComObject(s);
        Marshal.FinalReleaseComObject(shell);
    }
}

/// A window onto part of a stream: the zip inside this exe.
class SubStream : Stream
{
    readonly Stream s;
    readonly long start, len;
    long pos;

    public SubStream(Stream s, long start, long len) { this.s = s; this.start = start; this.len = len; }
    public override bool CanRead { get { return true; } }
    public override bool CanSeek { get { return true; } }
    public override bool CanWrite { get { return false; } }
    public override long Length { get { return len; } }
    public override long Position { get { return pos; } set { pos = value; } }
    public override void Flush() { }
    public override void SetLength(long v) { throw new NotSupportedException(); }
    public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }

    public override int Read(byte[] b, int o, int c)
    {
        if (pos >= len) return 0;
        if (c > len - pos) c = (int)(len - pos);
        s.Position = start + pos;
        int n = s.Read(b, o, c);
        pos += n;
        return n;
    }

    public override long Seek(long off, SeekOrigin origin)
    {
        if (origin == SeekOrigin.Begin) pos = off;
        else if (origin == SeekOrigin.Current) pos += off;
        else pos = len + off;
        return pos;
    }
}

class SetupForm : ModernForm
{
    enum Page { Welcome, Installing, Done, Failed }
    Page page;
    readonly Installer inst;
    readonly Btn primary, secondary;
    readonly CheckRow desktop, launch;
    readonly LinkText change;
    readonly CreditLink credit;
    string error = "";
    Thread worker;
    float spin;
    readonly System.Windows.Forms.Timer anim;

    public SetupForm(Installer inst) : base(600, 520)
    {
        this.inst = inst;
        Text = "התקנת " + Theme.AppName;
        primary = new Btn(inst.Update ? "עדכון" : "התקנה", true, K);
        secondary = new Btn("ביטול", false, K);
        desktop = new CheckRow("קיצור דרך בשולחן העבודה", true, K);
        launch = new CheckRow("לפתוח את העורך עכשיו", true, K);
        change = new LinkText("שינוי", 13.5f, K);
        change.BackColor = Theme.Raised;
        credit = new CreditLink(K);
        primary.Click += delegate { OnPrimary(); };
        secondary.Click += delegate { OnCloseClicked(); };
        change.Click += delegate { ChooseDir(); };
        desktop.Changed += delegate { inst.Desktop = desktop.Checked; };
        Controls.AddRange(new Control[] { primary, secondary, desktop, launch, change, credit });
        anim = new System.Windows.Forms.Timer();
        anim.Interval = 30;
        anim.Tick += delegate { spin = (spin + 0.012f) % 1f; if (page == Page.Installing) Invalidate(BarRect()); };
        ShowPage("welcome");
    }

    public void ShowPage(string name)
    {
        page = name == "installing" ? Page.Installing : name == "done" ? Page.Done : name == "failed" ? Page.Failed : Page.Welcome;
        if (name == "installing" && inst.Total == 0) { inst.Total = 100; inst.Done = 38; inst.Stage = "מעתיק את מודל התמלול (1.6GB)…"; }
        if (name == "failed" && error == "") error = "אין מספיק מקום פנוי בכונן C. צריך בערך 2.3GB פנויים.";
        Layout_();
    }

    void Layout_()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        int bw = D(150), bh = D(46), by = h - D(28) - bh;
        primary.SetBounds(w - D(32) - bw, by, bw, bh);
        secondary.SetBounds(w - D(32) - bw - D(12) - D(110), by, D(110), bh);
        credit.Location = new Point(D(24), by + (bh - credit.Height) / 2);
        desktop.SetBounds(D(40), D(380), w - D(80), D(30));
        launch.SetBounds(D(40), D(352), w - D(80), D(30));
        change.SetBounds(D(52), D(307), D(60), D(26));
        desktop.Visible = change.Visible = page == Page.Welcome;
        launch.Visible = page == Page.Done;
        primary.Visible = page != Page.Installing;
        secondary.Visible = page == Page.Welcome || page == Page.Installing || page == Page.Failed;
        primary.Text = page == Page.Welcome ? (inst.Update ? "עדכון" : "התקנה") : page == Page.Done ? "סיום" : "לנסות שוב";
        secondary.Text = page == Page.Failed ? "סגירה" : "ביטול";
        if (page == Page.Installing) secondary.SetBounds(w - D(32) - D(110), by, D(110), bh);
        CloseEnabled = true;
        if (page == Page.Installing) anim.Start(); else anim.Stop();
        Invalidate();
        if (primary.Visible) primary.Focus();
    }

    Rectangle BarRect() { return new Rectangle(D(56), D(296), ClientSize.Width - D(112), D(12)); }

    protected override void PaintContent(Graphics g)
    {
        float w = ClientSize.Width;
        PaintHeader(g, 34, 68, Theme.AppName, "תמלול ראיונות בעברית");
        if (page == Page.Welcome) PaintWelcome(g, w);
        else if (page == Page.Installing) PaintInstalling(g, w);
        else if (page == Page.Done) PaintDone(g, w);
        else PaintFailed(g, w);
    }

    void PaintWelcome(Graphics g, float w)
    {
        string about = inst.Update
            ? "כבר יש במחשב גרסה של העורך. העדכון מחליף את קובצי התוכנה, וההקלטות והתמלולים שלך נשארים במקומם."
            : "מתמלל הקלטות של ראיונות לעברית, מפריד בין המראיין למרואיין, ומציג טקסט מסונכרן לשמע כדי שיהיה קל לתקן. הכול רץ על המחשב שלך, בלי אינטרנט.";
        using (Font f = Theme.Font(D(15), false))
            Theme.Text(g, about, f, Theme.Ink2, new RectangleF(D(48), D(192), w - D(96), D(70)), StringAlignment.Center, StringAlignment.Near);

        RectangleF box = new RectangleF(D(40), D(272), w - D(80), D(84));
        using (GraphicsPath p = Theme.Round(box, D(14)))
        using (SolidBrush b = new SolidBrush(Theme.Raised))
            g.FillPath(b, p);
        using (Font f = Theme.Font(D(12.5f), true))
        {
            Theme.Text(g, "מיקום ההתקנה", f, Theme.Ink3, new RectangleF(box.X + D(18), box.Y + D(14), box.Width - D(36), D(20)), StringAlignment.Near, StringAlignment.Near);
            Theme.Text(g, "דרושים 2.3GB", f, Theme.Ink3, new RectangleF(box.X + D(18), box.Y + D(14), box.Width - D(36), D(20)), StringAlignment.Far, StringAlignment.Near);
        }
        using (Font f = Theme.Font(D(13.5f), false))
        using (SolidBrush b = new SolidBrush(Theme.Ink))
        using (StringFormat sf = new StringFormat())                     // a path reads left to right, set at the right edge
        {
            sf.Alignment = StringAlignment.Far;
            sf.Trimming = StringTrimming.EllipsisPath;
            sf.FormatFlags = StringFormatFlags.NoWrap;
            g.DrawString(inst.Dir, f, b, new RectangleF(box.X + D(80), box.Y + D(38), box.Width - D(98), D(22)), sf);
        }
        if (Installer.InCloud(inst.Dir))
            using (Font f = Theme.Font(D(12), false))
                Theme.Text(g, "התיקייה הזו מסונכרנת לענן. עדיף לבחור אחרת, כדי שההקלטות לא יעלו לאינטרנט.", f, Theme.Bad,
                           new RectangleF(box.X + D(18), box.Y + D(60), box.Width - D(36), D(20)), StringAlignment.Near, StringAlignment.Near);
    }

    void PaintInstalling(Graphics g, float w)
    {
        using (Font f = Theme.Font(D(19), true))
            Theme.Text(g, inst.Update ? "מעדכן…" : "מתקין…", f, Theme.Ink, new RectangleF(0, D(200), w, D(30)), StringAlignment.Center, StringAlignment.Near);
        using (Font f = Theme.Font(D(14), false))
            Theme.Text(g, inst.Stage, f, Theme.Ink2, new RectangleF(0, D(238), w, D(24)), StringAlignment.Center, StringAlignment.Near);
        Rectangle bar = BarRect();
        using (GraphicsPath p = Theme.Round(bar, bar.Height / 2f))
        using (SolidBrush b = new SolidBrush(Theme.Raised))
            g.FillPath(b, p);
        float frac = inst.Total > 0 ? (float)inst.Done / inst.Total : 0;
        float fw = Math.Max(bar.Height, bar.Width * frac);
        RectangleF fill = new RectangleF(bar.Right - fw, bar.Y, fw, bar.Height);      // fills from the right
        using (GraphicsPath p = Theme.Round(fill, bar.Height / 2f))
        using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(bar.X - 1, bar.Y, bar.Width + 2, bar.Height), Theme.AccentLo, Theme.AccentHi, 0f))
        {
            g.FillPath(b, p);
            Region old = g.Clip;                                                       // a soft shine moving across the fill
            g.SetClip(p);
            float sx = fill.Right - (fill.Width + D(120)) * spin;
            using (LinearGradientBrush sh = new LinearGradientBrush(new RectangleF(sx - D(120), bar.Y, D(120), bar.Height),
                   Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
            {
                ColorBlend cb = new ColorBlend();
                cb.Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                cb.Positions = new[] { 0f, 0.5f, 1f };
                sh.InterpolationColors = cb;
                g.FillRectangle(sh, sx - D(120), bar.Y, D(120), bar.Height);
            }
            g.Clip = old;
        }
        using (Font f = Theme.Font(D(13), true))
            Theme.Text(g, ((int)(frac * 100)).ToString() + "%", f, Theme.Accent, new RectangleF(0, D(318), w, D(22)), StringAlignment.Center, StringAlignment.Near);
        using (Font f = Theme.Font(D(12.5f), false))
            Theme.Text(g, "זה לוקח בערך דקה, תלוי במחשב.", f, Theme.Ink3, new RectangleF(0, D(350), w, D(20)), StringAlignment.Center, StringAlignment.Near);
    }

    void PaintDone(Graphics g, float w)
    {
        RectangleF c = new RectangleF((w - D(52)) / 2, D(186), D(52), D(52));
        using (SolidBrush b = new SolidBrush(Theme.OkSoft)) g.FillEllipse(b, c);
        Theme.Check(g, new RectangleF(c.X + D(12), c.Y + D(12), D(28), D(28)), Theme.Ok, D(3.4f));
        using (Font f = Theme.Font(D(21), true))
            Theme.Text(g, inst.Update ? "העורך עודכן" : "העורך מותקן", f, Theme.Ink, new RectangleF(0, D(250), w, D(30)), StringAlignment.Center, StringAlignment.Near);
        using (Font f = Theme.Font(D(14), false))
            Theme.Text(g, "אפשר לפתוח אותו מקיצור הדרך בשולחן העבודה או מתפריט התחל. בזמן שהוא פתוח יש לו סמל ליד השעון, ומשם סוגרים אותו.",
                       f, Theme.Ink2, new RectangleF(D(56), D(286), w - D(112), D(56)), StringAlignment.Center, StringAlignment.Near);
    }

    void PaintFailed(Graphics g, float w)
    {
        RectangleF c = new RectangleF((w - D(52)) / 2, D(186), D(52), D(52));
        using (SolidBrush b = new SolidBrush(Theme.BadSoft)) g.FillEllipse(b, c);
        using (Pen pen = new Pen(Theme.Bad, D(3.4f)))
        {
            pen.StartCap = pen.EndCap = LineCap.Round;
            g.DrawLine(pen, c.X + c.Width / 2, c.Y + D(14), c.X + c.Width / 2, c.Y + D(31));
        }
        using (SolidBrush b = new SolidBrush(Theme.Bad))
            g.FillEllipse(b, c.X + c.Width / 2 - D(2.4f), c.Y + D(36), D(4.8f), D(4.8f));
        using (Font f = Theme.Font(D(21), true))
            Theme.Text(g, "ההתקנה לא הצליחה", f, Theme.Ink, new RectangleF(0, D(250), w, D(30)), StringAlignment.Center, StringAlignment.Near);
        using (Font f = Theme.Font(D(14), false))
            Theme.Text(g, error, f, Theme.Ink2, new RectangleF(D(56), D(286), w - D(112), D(80)), StringAlignment.Center, StringAlignment.Near);
    }

    void ChooseDir()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "בחירת תיקייה להתקנת העורך";
            d.SelectedPath = Directory.Exists(inst.Dir) ? inst.Dir : Path.GetDirectoryName(inst.Dir);
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string p = d.SelectedPath;
            if (!Path.GetFileName(p.TrimEnd('\\')).Equals(Theme.AppName, StringComparison.OrdinalIgnoreCase))
                p = Path.Combine(p, Theme.AppName);
            inst.Dir = p;
            Invalidate();
        }
    }

    void OnPrimary()
    {
        if (page == Page.Done)
        {
            if (launch.Checked)
                try { Process.Start(new ProcessStartInfo(Path.Combine(inst.Dir, Theme.AppName + ".exe")) { WorkingDirectory = inst.Dir }); } catch { }
            Close();
            return;
        }
        Start();
    }

    void Start()
    {
        inst.Cancel = false;
        inst.Total = inst.Done = 0;
        inst.Stage = "מתכונן…";
        page = Page.Installing;
        Layout_();
        worker = new Thread(delegate ()
        {
            try
            {
                inst.Run(delegate { BeginInvoke((MethodInvoker)delegate { Invalidate(); }); });
                BeginInvoke((MethodInvoker)delegate { page = Page.Done; Layout_(); });
            }
            catch (OperationCanceledException)
            {
                BeginInvoke((MethodInvoker)delegate { page = Page.Welcome; Layout_(); });
            }
            catch (UnauthorizedAccessException)
            {
                Fail("אין הרשאה לכתוב לתיקייה הזו. נסו את מיקום ברירת המחדל.");
            }
            catch (IOException ex)
            {
                Fail("קובץ של העורך תפוס, כנראה כי העורך פתוח. סגרו אותו (לחיצה ימנית על הסמל שליד השעון ← יציאה) ונסו שוב.\n" + ex.Message);
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        });
        worker.IsBackground = true;
        worker.Start();
    }

    void Fail(string msg)
    {
        BeginInvoke((MethodInvoker)delegate { error = msg; page = Page.Failed; Layout_(); });
    }

    protected override void OnCloseClicked()
    {
        if (page == Page.Installing)
        {
            if (MessageBox.Show(this, "לעצור את ההתקנה?", Theme.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                                MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == DialogResult.Yes)
                inst.Cancel = true;
            return;
        }
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (page == Page.Installing && worker != null && worker.IsAlive && !inst.Cancel) { e.Cancel = true; OnCloseClicked(); }
        base.OnFormClosing(e);
    }
}

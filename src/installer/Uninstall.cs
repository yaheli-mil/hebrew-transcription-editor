// "uninstall.exe": what Settings > Apps runs to remove the editor. Asks first, and keeps the
// user's recordings and transcripts unless they choose to delete them too.
//
// Test flags: /silent [/all]   remove without the window (/all also deletes the recordings)
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("הסרת עורך תמלול")]

static class UninstallProgram
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        bool silent = false, all = false;
        foreach (string a in args)
        {
            string s = a.Trim('/', '-').ToLowerInvariant();
            if (s == "silent") silent = true;
            if (s == "all") all = true;
        }
        if (silent) { Remover.Run(root, all); return 0; }
        if (args.Length == 1 && args[0].StartsWith("/shot="))          // test: a picture of the window
        {
            UninstallForm f = new UninstallForm(root);
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-20000, -20000);
            f.Show();
            Application.DoEvents();
            using (Bitmap b = f.Snapshot()) b.Save(args[0].Substring(6));
            return 0;
        }
        Application.Run(new UninstallForm(root));
        return 0;
    }
}

static class Remover
{
    public static void Run(string root, bool recordings)
    {
        Installer_StopRunning(root);
        foreach (string lnk in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Theme.AppName + ".lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Theme.AppName + ".lnk") })
            try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(Theme.UninstallKey, false); } catch { }
        foreach (string sub in new[] { "app", "python", "model" })
            try { Directory.Delete(Path.Combine(root, sub), true); } catch { }
        foreach (string f in new[] { Theme.AppName + ".exe", "port.txt", ".welcomed" })
            try { File.Delete(Path.Combine(root, f)); } catch { }
        if (recordings)
            try { Directory.Delete(Path.Combine(root, "הקלטות"), true); } catch { }
        // This program cannot delete itself while it runs: a hidden command line finishes the job a moment later.
        string me = Path.Combine(root, "uninstall.exe");
        string cmd = "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + me + "\" & rmdir \"" + root + "\"";
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetTempPath();
            Process.Start(psi);
        }
        catch { }
    }

    static void Installer_StopRunning(string dir)
    {
        string full = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
        int me = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.Id != me && p.MainModule.FileName.StartsWith(full, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch { }
        }
    }
}

class UninstallForm : ModernForm
{
    readonly string root;
    readonly Btn remove, cancel;
    readonly CheckRow all;
    readonly CreditLink credit;
    int stage;                 // 0 ask, 1 removing, 2 done

    public UninstallForm(string root) : base(540, 370)
    {
        this.root = root;
        Text = "הסרת " + Theme.AppName;
        remove = new Btn("הסרה", false, K);
        remove.Danger = true;
        cancel = new Btn("ביטול", false, K);
        all = new CheckRow("למחוק גם את ההקלטות והתמלולים", false, K);
        credit = new CreditLink(K);
        remove.Click += delegate { if (stage == 2) Close(); else Remove(); };
        cancel.Click += delegate { Close(); };
        Controls.AddRange(new Control[] { remove, cancel, all, credit });
        int w = ClientSize.Width, h = ClientSize.Height, bh = D(46), by = h - D(28) - bh;
        remove.SetBounds(w - D(32) - D(130), by, D(130), bh);
        cancel.SetBounds(w - D(32) - D(130) - D(12) - D(110), by, D(110), bh);
        credit.Location = new Point(D(22), by + (bh - credit.Height) / 2);
        all.SetBounds(D(40), D(232), w - D(80), D(30));
    }

    protected override void PaintContent(Graphics g)
    {
        float w = ClientSize.Width;
        PaintHeader(g, 32, 56, stage == 2 ? "העורך הוסר" : "להסיר את " + Theme.AppName + "?", null);
        string text = stage == 0
            ? "קובצי התוכנה יימחקו מהמחשב. ההקלטות והתמלולים שלך יישארו בתיקייה \"הקלטות\", אלא אם מסמנים כאן למטה למחוק גם אותם."
            : stage == 1 ? "מסיר…" : all.Checked ? "העורך, ההקלטות והתמלולים נמחקו." : "ההקלטות והתמלולים שלך נשארו בתיקייה:";
        using (Font f = Theme.Font(D(14.5f), false))
            Theme.Text(g, text, f, Theme.Ink2, new RectangleF(D(48), D(150), w - D(96), D(70)), StringAlignment.Center, StringAlignment.Near);
        if (stage == 2 && !all.Checked)
            using (Font f = Theme.Font(D(13), false))
            using (SolidBrush b = new SolidBrush(Theme.Ink))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisPath;
                g.DrawString(Path.Combine(root, "הקלטות"), f, b, new RectangleF(D(40), D(186), w - D(80), D(40)), sf);
            }
    }

    void Remove()
    {
        stage = 1;
        remove.Enabled = cancel.Enabled = all.Enabled = false;
        CloseEnabled = false;
        Invalidate();
        bool everything = all.Checked;
        Thread t = new Thread(delegate ()
        {
            Remover.Run(root, everything);
            BeginInvoke((MethodInvoker)delegate
            {
                stage = 2;
                all.Visible = false;
                cancel.Visible = false;
                remove.Danger = false;
                remove.Primary = true;
                remove.Text = "סגירה";
                remove.Enabled = true;
                CloseEnabled = true;
                Invalidate();
                remove.Focus();
            });
        });
        t.IsBackground = true;
        t.Start();
    }
}

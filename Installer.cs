// ============================================================================
//  Installer.cs — 单文件安装包源码（安装包内嵌 GUI/CLI/README 三个文件）
//  编译（build.cmd 会自动执行）：
//    csc /target:winexe /out:"release\level convert 安装包.exe" Installer.cs
//        /res:"level convert GUI.exe","LevelConvertGui"
//        /res:"level convert.exe","LevelConvertCli"
//        /res:"README.md","ReadmeMd"
//  行为：双击 → 点"立即安装" → 在安装包同级创建 "level convert by dzdyhzl"
//        文件夹并把内嵌文件写入。命令行 --silent 静默安装（自动化测试用）。
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

internal static class InstallerProgram
{
    internal const string FolderName = "level convert by dzdyhzl";
    internal static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly string Target = Path.Combine(BaseDir, FolderName);

    [STAThread]
    private static void Main(string[] args)
    {
        bool silent = args != null && args.Length > 0 && args[0] == "--silent";
        if (silent)
        {
            string err;
            bool ok = DoInstall(Target, out err);
            string msg = (ok ? "INSTALL-OK " : "INSTALL-FAIL ") + Target + (err == null ? "" : " | " + err);
            try { File.WriteAllText(Path.Combine(BaseDir, "_install-result.txt"), msg, Encoding.UTF8); } catch { }
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Console.WriteLine(msg); } catch { }
            Environment.Exit(ok ? 0 : 1);
            return;
        }

        Application.EnableVisualStyles();
        Application.Run(new InstallerForm());
    }

    // 把内嵌资源写入目标文件夹（覆盖旧版本）
    internal static bool DoInstall(string target, out string err)
    {
        try
        {
            if (!Directory.Exists(target)) Directory.CreateDirectory(target);
            Extract("LevelConvertGui", Path.Combine(target, "level convert GUI.exe"));
            Extract("LevelConvertCli", Path.Combine(target, "level convert.exe"));
            Extract("ReadmeMd", Path.Combine(target, "README.md"));
            err = null;
            return true;
        }
        catch (Exception ex)
        {
            err = ex.Message;
            return false;
        }
    }

    private static void Extract(string resName, string dest)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
        {
            if (s == null) throw new Exception("缺少内嵌资源 " + resName);
            using (FileStream fs = new FileStream(dest, FileMode.Create))
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }
        }
    }
}

internal sealed class InstallerForm : Form
{
    private readonly string target = InstallerProgram.Target;
    private Button btnInstall, btnOpen;
    private Label lblStatus, lblPath;

    internal InstallerForm()
    {
        Text = "level convert by dzdyhzl — 安装程序";
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(500, 190);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        Label title = new Label();
        title.Text = "本安装包会在它所在的文件夹内创建子文件夹并安装：";
        title.AutoSize = true;
        title.Location = new Point(16, 16);
        lblPath = new Label();
        lblPath.Text = target;
        lblPath.AutoSize = true;
        lblPath.Location = new Point(16, 42);
        lblPath.ForeColor = Color.DimGray;
        btnInstall = new Button();
        btnInstall.Text = "立即安装";
        btnInstall.Size = new Size(220, 38);
        btnInstall.Location = new Point(16, 88);
        btnInstall.Click += OnInstall;
        btnOpen = new Button();
        btnOpen.Text = "打开安装文件夹";
        btnOpen.Size = new Size(220, 38);
        btnOpen.Location = new Point(254, 88);
        btnOpen.Enabled = false;
        btnOpen.Click += delegate
        {
            try { Process.Start("explorer.exe", "\"" + target + "\""); } catch { }
        };
        lblStatus = new Label();
        lblStatus.Text = "";
        lblStatus.AutoSize = true;
        lblStatus.Location = new Point(16, 148);
        lblStatus.ForeColor = Color.Green;

        Controls.Add(title);
        Controls.Add(lblPath);
        Controls.Add(btnInstall);
        Controls.Add(btnOpen);
        Controls.Add(lblStatus);
    }

    private void OnInstall(object sender, EventArgs e)
    {
        string err;
        bool ok = InstallerProgram.DoInstall(target, out err);
        if (ok)
        {
            lblStatus.ForeColor = Color.Green;
            lblStatus.Text = "✔ 安装完成：3 个文件已写入上述文件夹";
            btnInstall.Text = "安装完成";
            btnInstall.Enabled = false;
            btnOpen.Enabled = true;
        }
        else
        {
            lblStatus.ForeColor = Color.Red;
            lblStatus.Text = "安装失败：" + err;
        }
    }
}

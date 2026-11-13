// ============================================================================
//  Installer.cs — 单文件安装包源码（安装包内嵌 GUI/CLI/README 三个文件）
//  编译（build.cmd 自动执行）：
//    csc /target:winexe /out:"release\level convert 安装包.exe" Installer.cs
//        /res:"level convert GUI.exe","LevelConvertGui"
//        /res:"level convert.exe","LevelConvertCli"
//        /res:"README.md","ReadmeMd"
//  行为：默认安装到安装包同级；用户可自行选择安装路径（将在所选路径下创建
//        "level convert by dzdyhzl" 文件夹）。命令行 --silent [路径] 静默安装。
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

    [STAThread]
    private static void Main(string[] args)
    {
        bool silent = args != null && args.Length > 0 && args[0] == "--silent";
        if (silent)
        {
            // --silent [父路径]：缺省 = 安装包同级
            string parent = args != null && args.Length > 1 ? args[1] : BaseDir;
            string target = Path.Combine(parent, FolderName);
            string err;
            bool ok = DoInstall(target, out err);
            string msg = (ok ? "INSTALL-OK " : "INSTALL-FAIL ") + target + (err == null ? "" : " | " + err);
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
    private string parentDir = InstallerProgram.BaseDir;   // 安装父路径（用户可选）
    private TextBox txtPath;
    private Label lblTargetValue, lblStatus;
    private Button btnInstall, btnOpen;

    internal InstallerForm()
    {
        Text = "level convert by dzdyhzl — 安装程序";
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(560, 240);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        Label title = new Label();
        title.Text = "选择安装路径（将在所选路径下创建文件夹 \"" + InstallerProgram.FolderName + "\" 并安装）：";
        title.AutoSize = true;
        title.Location = new Point(16, 16);

        txtPath = new TextBox();
        txtPath.Location = new Point(16, 44);
        txtPath.Width = 440;
        txtPath.Text = InstallerProgram.BaseDir;
        txtPath.TextChanged += delegate { UpdateTargetPreview(); };

        Button btnBrowse = new Button();
        btnBrowse.Text = "浏览...";
        btnBrowse.Location = new Point(464, 42);
        btnBrowse.Size = new Size(80, 25);
        btnBrowse.Click += OnBrowse;

        Label lbl2 = new Label();
        lbl2.Text = "最终安装位置：";
        lbl2.AutoSize = true;
        lbl2.Location = new Point(16, 84);

        lblTargetValue = new Label();
        lblTargetValue.AutoSize = false;
        lblTargetValue.Location = new Point(16, 106);
        lblTargetValue.Size = new Size(528, 20);
        lblTargetValue.ForeColor = Color.DimGray;

        btnInstall = new Button();
        btnInstall.Text = "立即安装";
        btnInstall.Size = new Size(220, 38);
        btnInstall.Location = new Point(16, 140);
        btnInstall.Click += OnInstall;

        btnOpen = new Button();
        btnOpen.Text = "打开安装文件夹";
        btnOpen.Size = new Size(220, 38);
        btnOpen.Location = new Point(254, 140);
        btnOpen.Enabled = false;
        btnOpen.Click += delegate
        {
            try { Process.Start("explorer.exe", "\"" + ComposeTarget() + "\""); } catch { }
        };

        lblStatus = new Label();
        lblStatus.Text = "";
        lblStatus.AutoSize = true;
        lblStatus.Location = new Point(16, 196);
        lblStatus.ForeColor = Color.Green;

        Controls.Add(title);
        Controls.Add(txtPath);
        Controls.Add(btnBrowse);
        Controls.Add(lbl2);
        Controls.Add(lblTargetValue);
        Controls.Add(btnInstall);
        Controls.Add(btnOpen);
        Controls.Add(lblStatus);

        UpdateTargetPreview();
    }

    private string ComposeTarget()
    {
        try { return Path.Combine(parentDir.Trim(), InstallerProgram.FolderName); }
        catch { return Path.Combine(InstallerProgram.BaseDir, InstallerProgram.FolderName); }
    }

    private void UpdateTargetPreview()
    {
        parentDir = txtPath.Text.Trim().Length > 0 ? txtPath.Text.Trim() : InstallerProgram.BaseDir;
        lblTargetValue.Text = ComposeTarget();
    }

    private void OnBrowse(object sender, EventArgs e)
    {
        using (FolderBrowserDialog dlg = new FolderBrowserDialog())
        {
            dlg.Description = "选择安装位置（将在该路径下创建 \"" + InstallerProgram.FolderName + "\" 文件夹）";
            dlg.ShowNewFolderButton = true;
            try { dlg.SelectedPath = parentDir; } catch { }
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                parentDir = dlg.SelectedPath;
                txtPath.Text = parentDir;
                UpdateTargetPreview();
            }
        }
    }

    private void OnInstall(object sender, EventArgs e)
    {
        UpdateTargetPreview();
        string target = ComposeTarget();
        string err;
        bool ok = InstallerProgram.DoInstall(target, out err);
        if (ok)
        {
            lblStatus.ForeColor = Color.Green;
            lblStatus.Text = "✔ 安装完成：3 个文件已写入 " + target;
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

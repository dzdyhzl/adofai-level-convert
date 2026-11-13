// 界面布局审计（开发辅助，不随产品发布）
// 用法：csc /target:winexe /main:LevelConvert.AuditProgram ... Core.cs Gui.cs Audit.cs
// 输出：控制台摘要 + _audit-out.txt（UTF-8 详细 findings）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LevelConvert
{
    internal static class AuditProgram
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        private static int issues = 0;
        private static readonly List<string> findings = new List<string>();
        private static readonly string OutFile = @"G:\adofai\code\level convert\_audit-out.txt";

        private static void Rep(Control c, string what)
        {
            issues++;
            findings.Add("[" + c.GetType().Name + "] " + c.Text + " @(" + c.Left + "," + c.Top + "," + c.Width + "," + c.Height + ") :: " + what);
        }

        [STAThread]
        private static void Main()
        {
            SetProcessDPIAware();   // 与 GUI 相同的 DPI 感知：审计环境 = 真实运行环境
            IntPtr hdc0 = GetDC(IntPtr.Zero);
            float dpiScale = GetDeviceCaps(hdc0, 90) / 96f;
            ReleaseDC(IntPtr.Zero, hdc0);
            GuiProgram.SetUiScale(dpiScale);   // 显式设定，消除初始化时序差异
            Application.EnableVisualStyles();
            MainForm f = new MainForm();
            Console.WriteLine("PROBE UiScale=" + GuiProgram.UiScale + " formFont=" + f.Font.Size + "pt");
            IntPtr hdc = GetDC(IntPtr.Zero);
            Console.WriteLine("PROBE GetDeviceCaps LOGPIXELSY=" + GetDeviceCaps(hdc, 90));
            ReleaseDC(IntPtr.Zero, hdc);
            f.Show();
            Application.DoEvents();
            f.PerformLayout();

            // 与真实使用一致：加载谱面（触发 lblInfo 长文本 + 类型面板实测数量）
            System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            f.GetType().GetMethod("LoadLevel", F).Invoke(f,
                new object[] { @"G:\adofai\levels\Fallenera\FallenEra P19\FallenEra\level no vfx remake.adofai" });
            Application.DoEvents();

            Console.WriteLine("== 文本裁剪检查 ==");
            AuditTree(f);

            Console.WriteLine("== 同层控件重叠检查（基准尺寸） ==");
            AuditOverlap(f);

            // 多尺寸检查（设备像素）
            float s = GuiProgram.UiScale;
            Size[] sizes = new Size[]
            {
                new Size((int)(1000 * s), (int)(860 * s)),
                new Size((int)(1400 * s), (int)(1000 * s)),
                new Size((int)(1920 * s), (int)(1040 * s)),
                new Size((int)(880 * s), (int)(860 * s))
            };
            foreach (Size sz in sizes)
            {
                f.ClientSize = sz;
                Application.DoEvents();
                int before = issues;
                AuditOverlap(f);
                Console.WriteLine("SIZE " + sz.Width + "x" + sz.Height + " : " + (issues == before ? "OK" : "FAIL " + (issues - before)));
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("issues=").Append(issues).Append("\n");
            foreach (string l in findings.ToArray()) sb.Append(l).Append("\n");
            File.WriteAllText(OutFile, sb.ToString(), Encoding.UTF8);
            Console.WriteLine("== 完成：详见 _audit-out.txt ==");
            f.Close();
        }

        private static void AuditTree(Control root)
        {
            foreach (Control c in root.Controls)
            {
                bool flowManaged = c.Parent is FlowLayoutPanel;
                string text = c.Text;
                if (!string.IsNullOrEmpty(text) && !c.AutoSize && !flowManaged)
                {
                    Size m = TextRenderer.MeasureText(text, c.Font);
                    if (c is Label)
                    {
                        if (m.Width > c.Width + 1) Rep(c, "Label 文本超宽: 需 " + m.Width + " 实际 " + c.Width);
                        if (m.Height > c.Height + 1) Rep(c, "Label 文本超高: 需 " + m.Height + " 实际 " + c.Height);
                    }
                    else if (c is CheckBox || c is RadioButton)
                    {
                        int need = m.Width + 22 + c.Margin.Horizontal;
                        if (need > c.Width + 1) Rep(c, "复选/单选文本超宽: 需 " + need + " 实际 " + c.Width);
                        if (m.Height > c.Height + 1) Rep(c, "复选/单选文本超高: 需 " + m.Height + " 实际 " + c.Height);
                    }
                    else if (c is Button)
                    {
                        if (m.Width > c.Width - 6) Rep(c, "按钮文本超宽: 需 " + (m.Width + 6) + " 实际 " + c.Width);
                        if (m.Height > c.Height - 2) Rep(c, "按钮文本超高: 需 " + (m.Height + 2) + " 实际 " + c.Height);
                    }
                }
                AuditTree(c);
            }
        }

        private static void AuditOverlap(Control root)
        {
            if (root.Controls.Count > 0 && root.Controls[0].Parent is FlowLayoutPanel) return;
            CheckSiblings(root.Controls);
            foreach (Control c in root.Controls) AuditOverlap(c);
        }

        private static void CheckSiblings(Control.ControlCollection cc)
        {
            for (int i = 0; i < cc.Count; i++)
                for (int j = i + 1; j < cc.Count; j++)
                {
                    Rectangle a = cc[i].Bounds, b = cc[j].Bounds;
                    a.Intersect(b);
                    if (a.Width > 2 && a.Height > 4)
                        Rep(cc[i], "与 [" + cc[j].Text + "] 重叠 " + a.Width + "x" + a.Height);
                }
        }
    }
}

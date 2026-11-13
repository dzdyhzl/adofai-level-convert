// ============================================================================
//  Gui.cs — 图形界面（与 Core.cs 一起编译为 "level convert GUI.exe"）
//  布局方式：全部尺寸按实际字体度量（TextRenderer），水平/垂直双引擎，
//  任意 DPI / 窗口尺寸下均无文字遮挡。见 Audit.cs 自动审计。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LevelConvert
{
    internal static class GuiProgram
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        private static float? uiScaleCached;
        internal static float UiScale
        {
            get
            {
                if (!uiScaleCached.HasValue)
                {
                    IntPtr hdc = GetDC(IntPtr.Zero);
                    int dpi = GetDeviceCaps(hdc, 90); // LOGPIXELSY
                    ReleaseDC(IntPtr.Zero, hdc);
                    uiScaleCached = dpi / 96f;
                }
                return uiScaleCached.Value;
            }
        }
        internal static void SetUiScale(float s) { uiScaleCached = s; }

        [STAThread]
        private static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { PathData.LoadOptionalConfig(); } catch (Exception ex)
            {
                MessageBox.Show("pathdata-chars.json 配置有误：\n" + ex.Message, "level convert",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Font font = new Font("Microsoft YaHei UI", 9F * GuiProgram.UiScale);
        private readonly Font fontBold = new Font("Microsoft YaHei UI", 9F * GuiProgram.UiScale, FontStyle.Bold);
        private readonly Font fontMono = new Font("Consolas", 9F * GuiProgram.UiScale);

        private TextBox txtPath;
        private Label lblInfo;
        private CheckBox chkUpgrade;
        private CheckBox chkAddKeys, chkPathData, chkKeepPathData;
        private CheckBox chkRemove;
        private CheckBox chkRemoveDecor;
        private TextBox txtKeyword;
        private CheckBox chkExact, chkCase;
        private ListBox listKeywordEv;
        private Button btnAddKw, btnRemoveKw;
        private TextBox txtKeywordDecor;
        private CheckBox chkExactDecor, chkCaseDecor;
        private ListBox listKeywordDecor;
        private Button btnAddKwDecor, btnRemoveKwDecor;
        private FlowLayoutPanel flpTypes;
        private Button btnAllTypes, btnNoneTypes;
        private Button btnStats;
        private TextBox txtOutName;
        private CheckBox chkOverwrite;
        private Button btnConvert, btnPreview, btnOpenFolder, btnBrowse;
        private TextBox txtLog;

        private LoadedLevel loaded;
        private string loadedPath;
        private string lastOutputDir;

        // ---------------- 纵/横双引擎 ----------------
        private sealed class VItem { public Control C; public int Kind; public int LogW; } // Kind 0=Left 1=Fill 2=Right
        private sealed class VRow { public List<VItem> Items = new List<VItem>(); public int H; public int Gap; public int GapMin; public int W; public bool FlexH; public int HMin; }
        private readonly List<VRow> rows = new List<VRow>();
        private VRow rowPanel, rowLog;

        private int S(int v) { return (int)Math.Round(v * GuiProgram.UiScale); }
        private int MeasureW(string text) { return TextRenderer.MeasureText(text, font).Width; }
        private int CheckW(string text) { return MeasureW(text) + S(30); }   // 复选框：文本 + 方框 + 余量

        private VRow Row(int h, int gap, int gapMin, int gapW)
        {
            VRow r = new VRow();
            r.H = h; r.Gap = gap; r.GapMin = gapMin; r.W = gapW;
            rows.Add(r);
            return r;
        }
        private VItem Item(Control c, int kind, int logW)
        {
            Controls.Add(c);   // 关键：把控件挂到窗体上
            VItem it = new VItem();
            it.C = c; it.Kind = kind; it.LogW = logW;
            return it;
        }

        public MainForm()
        {
            Text = "level convert — ADOFAI 关卡转换工具";
            Font = font;
            AutoScaleMode = AutoScaleMode.None;   // 缩放完全由本类按字体度量完成
            Rectangle wa = SystemInformation.WorkingArea;
            int baseW = Math.Min(S(1000), (int)(wa.Width * 0.94));
            int baseH = Math.Min(S(940), (int)(wa.Height * 0.95));
            ClientSize = new Size(baseW, baseH);
            MinimumSize = new Size(Math.Min(S(880), baseW), Math.Min(S(720), baseH));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            KeyPreview = true;
            KeyDown += OnKeyDown;              // F11 无边框全屏切换
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            // ---- 字体度量（设备像素）----
            int fh = TextRenderer.MeasureText("Ag过测", font).Height;
            int hLabel = fh + S(6), hCheck = fh + S(10), hInput = fh + S(10), hButton = fh + S(14), hList = fh + S(22);

            // ---- ① 文件 ----
            Label g1 = MkLabel("① 选择关卡文件（可拖入 .adofai 文件）");
            btnBrowse = MkButton("浏览...");
            btnBrowse.Click += delegate { BrowseFile(); };
            txtPath = new TextBox();
            txtPath.ReadOnly = true;
            txtPath.BackColor = Color.White;
            lblInfo = MkLabel("（尚未选择文件）");
            lblInfo.ForeColor = Color.DimGray;
            Row(hLabel, S(18), S(10), 1).Items.Add(Item(g1, 1, 0));
            Row(hInput, S(10), S(6), 1).Items.AddRange(new VItem[] { Item(txtPath, 1, 0), Item(btnBrowse, 2, 88) });
            Row(hLabel, S(26), S(14), 2).Items.Add(Item(lblInfo, 1, 0));

            // ---- ② 更改要求 ----
            Label g2 = MkLabel("② 选择更改要求");
            Row(hLabel, S(12), S(8), 1).Items.Add(Item(g2, 1, 0));

            chkUpgrade = MkCheck("升级到新版本（v19）");
            chkUpgrade.Checked = true;
            Row(hCheck, S(8), S(5), 1).Items.Add(Item(chkUpgrade, 0, 0));
            chkAddKeys = MkCheck("补齐缺失的 v19 核心设置键");
            chkAddKeys.Checked = true;
            chkPathData = MkCheck("pathData → angleData 转换");
            chkPathData.Checked = true;
            chkKeepPathData = MkCheck("保留原 pathData 字符串");
            Row(hCheck, S(8), S(5), 1).Items.AddRange(new VItem[] { Item(chkAddKeys, 0, 0), Item(chkPathData, 0, 0), Item(chkKeepPathData, 0, 0) });

            // —— 删除事件：关键词 + 事件类型多选 ——
            chkRemove = MkCheck("删除事件（行需同时包含全部关键词，且类型已勾选；只给其一时按单项删）");
            Row(hCheck, S(10), S(6), 1).Items.Add(Item(chkRemove, 0, 0));

            Label lk = MkLabel("事件关键词:");
            txtKeyword = new TextBox();
            btnAddKw = MkButton("添加");
            btnRemoveKw = MkButton("移除");
            listKeywordEv = new ListBox();
            listKeywordEv.IntegralHeight = false;
            listKeywordEv.Height = S(46);
            listKeywordEv.BackColor = Color.White;
            chkExact = MkCheck("精确匹配");
            chkCase = MkCheck("区分大小写");
            Row(hInput, S(10), S(6), 1).Items.AddRange(new VItem[] {
                Item(lk, 0, 0), Item(txtKeyword, 1, 0), Item(btnAddKw, 2, 64), Item(btnRemoveKw, 2, 64) });
            Row(hList, S(12), S(7), 1).Items.Add(Item(listKeywordEv, 1, 0));
            btnAddKw.Click += delegate { AddKeyword(txtKeyword, listKeywordEv); };
            btnRemoveKw.Click += delegate { RemoveKeyword(listKeywordEv); };
            listKeywordEv.DoubleClick += delegate { RemoveKeyword(listKeywordEv); };
            txtKeyword.KeyDown += delegate(object s2, KeyEventArgs e2)
            {
                if (e2.KeyCode == Keys.Enter) { e2.Handled = true; e2.SuppressKeyPress = true; AddKeyword(txtKeyword, listKeywordEv); }
            };

            Label lt = MkLabel("事件类型（标准全集，可多选；不选 = 不限）:");
            btnAllTypes = MkButton("全选");
            btnAllTypes.Click += delegate { SetAllTypes(true); };
            btnNoneTypes = MkButton("清空");
            btnNoneTypes.Click += delegate { SetAllTypes(false); };
            Row(hLabel, S(8), S(5), 1).Items.AddRange(new VItem[] {
                Item(lt, 1, 0), Item(btnAllTypes, 2, 64), Item(btnNoneTypes, 2, 64) });

            flpTypes = new FlowLayoutPanel();
            flpTypes.FlowDirection = FlowDirection.LeftToRight;
            flpTypes.WrapContents = true;
            flpTypes.AutoScroll = true;
            flpTypes.BorderStyle = BorderStyle.FixedSingle;
            flpTypes.BackColor = Color.White;
            rowPanel = Row(0, 0, 0, 0); rowPanel.FlexH = true; rowPanel.HMin = S(96);
            rowPanel.Items.Add(Item(flpTypes, 1, 0));

            chkRemoveDecor = MkCheck("删除装饰（行需同时包含全部关键词的装饰全部删除）");
            chkRemoveDecor.CheckedChanged += delegate { SetRemoveEnabled(); };
            Row(hCheck, S(14), S(8), 1).Items.Add(Item(chkRemoveDecor, 0, 0));

            Label lk2 = MkLabel("装饰关键词:");
            txtKeywordDecor = new TextBox();
            txtKeywordDecor.Enabled = false;
            btnAddKwDecor = MkButton("添加");
            btnAddKwDecor.Enabled = false;
            btnRemoveKwDecor = MkButton("移除");
            btnRemoveKwDecor.Enabled = false;
            listKeywordDecor = new ListBox();
            listKeywordDecor.IntegralHeight = false;
            listKeywordDecor.Height = S(46);
            listKeywordDecor.BackColor = Color.White;
            listKeywordDecor.Enabled = false;
            chkExactDecor = MkCheck("精确匹配");
            chkExactDecor.Enabled = false;
            chkCaseDecor = MkCheck("区分大小写");
            chkCaseDecor.Enabled = false;
            Row(hInput, S(10), S(6), 1).Items.AddRange(new VItem[] {
                Item(lk2, 0, 0), Item(txtKeywordDecor, 1, 0), Item(btnAddKwDecor, 2, 64), Item(btnRemoveKwDecor, 2, 64) });
            Row(hList, S(12), S(7), 1).Items.Add(Item(listKeywordDecor, 1, 0));
            btnAddKwDecor.Click += delegate { AddKeyword(txtKeywordDecor, listKeywordDecor); };
            btnRemoveKwDecor.Click += delegate { RemoveKeyword(listKeywordDecor); };
            listKeywordDecor.DoubleClick += delegate { RemoveKeyword(listKeywordDecor); };
            txtKeywordDecor.KeyDown += delegate(object s3, KeyEventArgs e3)
            {
                if (e3.KeyCode == Keys.Enter) { e3.Handled = true; e3.SuppressKeyPress = true; AddKeyword(txtKeywordDecor, listKeywordDecor); }
            };
            Row(hCheck, S(12), S(7), 1).Items.AddRange(new VItem[] {
                Item(chkExactDecor, 0, 0), Item(chkCaseDecor, 0, 0) });

            btnStats = MkButton("查看事件统计...");
            btnStats.Click += delegate { ShowStats(); };
            Row(hCheck, S(24), S(12), 2).Items.Add(Item(btnStats, 2, 138));

            // ---- ③ 输出 ----
            Label g3 = MkLabel("③ 输出（谱面所在文件夹）");
            Row(hLabel, S(12), S(8), 1).Items.Add(Item(g3, 1, 0));
            Label lo = MkLabel("输出文件名:");
            txtOutName = new TextBox();
            chkOverwrite = MkCheck("直接覆盖原文件");
            Row(hInput, S(12), S(7), 1).Items.AddRange(new VItem[] {
                Item(lo, 0, 0), Item(txtOutName, 1, 0), Item(chkOverwrite, 2, 0) });

            btnConvert = MkButton("开始转换");
            btnConvert.Font = fontBold;
            btnConvert.BackColor = Color.FromArgb(0, 122, 204);
            btnConvert.ForeColor = Color.White;
            btnConvert.FlatStyle = FlatStyle.System;
            btnConvert.Click += delegate { ConvertNow(false); };
            btnPreview = MkButton("预览改动（不写文件）");
            btnPreview.Click += delegate { ConvertNow(true); };
            btnOpenFolder = MkButton("打开所在文件夹");
            btnOpenFolder.Enabled = false;
            btnOpenFolder.Click += delegate { OpenFolder(); };
            Row(hButton, S(22), S(12), 2).Items.AddRange(new VItem[] {
                Item(btnConvert, 0, 220), Item(btnPreview, 0, 200), Item(btnOpenFolder, 2, 170) });

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = fontMono;
            txtLog.BackColor = Color.FromArgb(248, 248, 248);
            rowLog = Row(0, 0, 0, 0); rowLog.FlexH = true; rowLog.HMin = S(60);
            rowLog.Items.Add(Item(txtLog, 1, 0));

            // 联动启用/禁用
            chkUpgrade.CheckedChanged += delegate { chkAddKeys.Enabled = chkUpgrade.Checked; chkPathData.Enabled = chkUpgrade.Checked; chkKeepPathData.Enabled = chkUpgrade.Checked && chkPathData.Checked; };
            chkPathData.CheckedChanged += delegate { chkKeepPathData.Enabled = chkUpgrade.Checked && chkPathData.Checked; };
            chkRemove.CheckedChanged += delegate { SetRemoveEnabled(); };
            chkOverwrite.CheckedChanged += delegate { txtOutName.Enabled = !chkOverwrite.Checked; };

            SetRemoveEnabled();
            PopulateTypePanel(null);
            Resize += delegate { LayoutAll(); };
            LayoutAll();
        }

        private Label MkLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            return l;
        }
        private CheckBox MkCheck(string text)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = false;
            return c;
        }
        private RadioButton MkRadio(string text)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.AutoSize = false;
            return r;
        }
        private Button MkButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            return b;
        }
        private void AddKeyword(TextBox box, ListBox list)
        {
            string k = box.Text.Trim();
            if (k.Length == 0) return;
            if (!list.Items.Contains(k)) list.Items.Add(k);
            box.Text = "";
            box.Focus();
        }

        private void RemoveKeyword(ListBox list)
        {
            if (list.SelectedItem != null) list.Items.Remove(list.SelectedItem);
        }

        private void SetRemoveEnabled()
        {
            bool ev = chkRemove.Checked, dec = chkRemoveDecor.Checked;
            txtKeyword.Enabled = ev;       // 删除事件专用
            chkExact.Enabled = ev;
            chkCase.Enabled = ev;
            listKeywordEv.Enabled = ev;
            btnAddKw.Enabled = ev;
            btnRemoveKw.Enabled = ev;
            flpTypes.Enabled = ev;         // 类型面板只服务"删除事件"
            btnAllTypes.Enabled = ev;
            btnNoneTypes.Enabled = ev;
            txtKeywordDecor.Enabled = dec; // 删除装饰专用
            chkExactDecor.Enabled = dec;
            chkCaseDecor.Enabled = dec;
            listKeywordDecor.Enabled = dec;
            btnAddKwDecor.Enabled = dec;
            btnRemoveKwDecor.Enabled = dec;
            btnStats.Enabled = loaded != null;
        }
        private void SetAllTypes(bool on)
        {
            typeGroupSync = true;
            foreach (Control c in flpTypes.Controls)
            {
                CheckBox cb = c as CheckBox;
                if (cb != null && cb.Tag is string) cb.Checked = on;
            }
            typeGroupSync = false;
            foreach (TypeGroup grp in typeGroups) RefreshGroupState(grp);
        }

        // ---------------- 布局：按实际字体度量，横纵双引擎 ----------------
        private void LayoutAll()
        {
            // 字体实际行高（设备像素）
            int fh = TextRenderer.MeasureText("Ag过测", font).Height;

            int W = ClientSize.Width, H = ClientSize.Height;
            int x0 = S(16), rowW = W - 2 * S(16);

            // ---- 垂直分配 ----
            int sumFixed = 0, sumGap = 0, gapW = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                VRow r = rows[i];
                sumFixed += r.FlexH ? 0 : r.H;
                sumGap += r.Gap;
                gapW += r.W;
            }
            int logBase = S(130), logMin = S(50), panelBase = S(120), panelMin = S(96);
            int extra = H - top() - bottom() - sumFixed - sumGap - logBase - panelBase;
            int panelExtra = (int)Math.Round(extra * 0.32);
            int logExtra = (int)Math.Round(extra * 0.26);
            int gapExtra = extra - panelExtra - logExtra;
            int panelH = Math.Max(panelMin, panelBase + panelExtra);
            int logH = Math.Max(logMin, logBase + logExtra);

            // ---- 横向 + 放置 ----
            int y = top();
            int right0 = x0 + rowW;
            for (int i = 0; i < rows.Count; i++)
            {
                VRow r = rows[i];
                int h = r.FlexH ? (r == rowPanel ? panelH : logH) : r.H;
                // 行内宽度先算（Left 顺序，Right 靠右，Fill 占中间）
                int n = r.Items.Count;
                int[] xs = new int[n], ws = new int[n];
                int leftUsed = 0, rightUsed = 0, fillIdx = -1;
                for (int k = 0; k < n; k++)
                {
                    VItem it = r.Items[k];
                    if (it.Kind == 0)
                    {
                        ws[k] = (it.C is CheckBox || it.C is RadioButton) ? CheckW(it.C.Text)
                              : (it.C is Label ? MeasureW(it.C.Text) + S(6) : S(it.LogW));
                        leftUsed += ws[k];
                    }
                    else if (it.Kind == 2)
                    {
                        ws[k] = (it.C is CheckBox || it.C is RadioButton) ? CheckW(it.C.Text) : S(it.LogW);
                        rightUsed += ws[k];
                    }
                    else fillIdx = k;
                }
                int gapsUsed = S(12) * (n - 1);
                if (fillIdx >= 0)
                {
                    int fillW = rowW - leftUsed - rightUsed - gapsUsed;
                    ws[fillIdx] = Math.Max(fillW, S(80));
                }
                // 放置
                int cursor = x0;
                int rightCursor = right0;
                for (int k = 0; k < n; k++)
                {
                    VItem it = r.Items[k];
                    Control c = it.C;
                    // 控件自身高度按字体定（标签/复选/单选）；文本框与按钮用其自然高度
                    if (c is Label) c.Height = fh + S(2);
                    else if (c is CheckBox || c is RadioButton) c.Height = fh + S(4);
                    else if (c is Button) c.Height = fh + S(9);
                    bool isFlexRow = r == rowPanel || r == rowLog;
                    int cy = isFlexRow ? y : y + Math.Max(0, (h - c.Height) / 2);
                    int chh = isFlexRow ? h : c.Height;
                    if (it.Kind == 2)
                    {
                        rightCursor -= ws[k];
                        c.SetBounds(rightCursor, cy, ws[k], chh);
                        rightCursor -= S(12);
                    }
                    else if (k == fillIdx)
                    {
                        c.SetBounds(cursor, cy, ws[k], chh);
                        cursor += ws[k] + S(12);
                    }
                    else
                    {
                        c.SetBounds(cursor, cy, ws[k], chh);
                        cursor += ws[k] + S(12);
                    }
                }
                y += h + (i < rows.Count - 1 ? Math.Max(r.GapMin, r.Gap + (int)Math.Round((double)gapExtra * r.W / Math.Max(1, gapW))) : 0);
            }
        }

        private int top() { return S(16); }
        private int bottom() { return S(16); }

        // ---------------- 文件选择 ----------------
        private void BrowseFile()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 .adofai 关卡文件";
                dlg.Filter = "ADOFAI 关卡 (*.adofai)|*.adofai|所有文件 (*.*)|*.*";
                if (txtPath.Text.Length > 0 && File.Exists(txtPath.Text))
                    dlg.InitialDirectory = Path.GetDirectoryName(txtPath.Text);
                else { try { dlg.InitialDirectory = @"G:\adofai\levels"; } catch { } }
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadLevel(dlg.FileName);
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && files[0].EndsWith(".adofai", StringComparison.OrdinalIgnoreCase))
                    e.Effect = DragDropEffects.Copy;
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0) LoadLevel(files[0]);
        }

        private void LoadLevel(string path)
        {
            try
            {
                LoadedLevel L = Core.LoadFile(path);
                loaded = L;
                loadedPath = path;
                txtPath.Text = path;

                JObj top = L.Root;
                JObj st = top.Get("settings") as JObj;
                string ver = "?";
                if (st != null && st.Get("version") is JNum) ver = ((JNum)st.Get("version")).Raw;
                JArr ang = top.Get("angleData") as JArr;
                JStr pd = top.Get("pathData") as JStr;
                string tiles = ang != null ? ang.Items.Count.ToString(CultureInfo.InvariantCulture) + " 块" : (pd != null ? pd.V.Length + " 块 (pathData)" : "? 块");
                JArr acts = top.Get("actions") as JArr;
                JArr decos = top.Get("decorations") as JArr;
                string parse = L.Repairs.Count > 0 ? "已容错修复(" + string.Join("、", L.Repairs.ToArray()) + ")" : "严格 JSON";
                lblInfo.Text = "v" + ver + " · " + tiles + " · actions " + (acts != null ? acts.Items.Count : 0)
                    + " · decorations " + (decos != null ? decos.Items.Count : 0)
                    + " · " + parse + (L.NonUtf8 ? " · 非UTF-8" : "");
                lblInfo.ForeColor = L.Repairs.Count > 0 ? Color.DarkOrange : Color.Green;

                if (!chkOverwrite.Checked)
                    txtOutName.Text = Path.GetFileName(Core.MakeOutputName(path, null)); // <原名> - converted.adofai

                PopulateTypePanel(L);

                btnStats.Enabled = true;
                btnConvert.Enabled = true;
                btnPreview.Enabled = true;
                AppendLog("已加载: " + path);
            }
            catch (Exception ex)
            {
                loaded = null;
                loadedPath = null;
                lblInfo.Text = "加载失败: " + ex.Message;
                lblInfo.ForeColor = Color.Red;
                btnConvert.Enabled = false;
                btnPreview.Enabled = false;
                AppendLog("[失败] " + ex.Message);
            }
        }

        // ---------------- 转换 ----------------
        private CoreOptions CollectOptions()
        {
            CoreOptions op = new CoreOptions();
            op.Upgrade = chkUpgrade.Checked;
            op.Minimal = !chkAddKeys.Checked;
            op.NoPathData = !chkPathData.Checked;
            op.KeepPathData = chkKeepPathData.Checked;
            op.Remove = chkRemove.Checked;
            op.RemoveDecorations = chkRemoveDecor.Checked;
            op.RemoveDecorationsKeyword = txtKeywordDecor.Text;
            foreach (object it in listKeywordDecor.Items) op.RemoveDecorationsKeywords.Add((string)it);
            op.RemoveDecorationsExact = chkExactDecor.Checked;
            op.RemoveDecorationsCaseSensitive = chkCaseDecor.Checked;
            op.RemoveKeyword = txtKeyword.Text;
            foreach (object it in listKeywordEv.Items) op.RemoveKeywords.Add((string)it);
            op.RemoveMode = "anyfield";   // 条件1：事件行内任一字符串字段（含 eventType/eventTag/嵌套字符串）需同时包含全部关键词
            foreach (Control c in flpTypes.Controls)
            {
                CheckBox cb = c as CheckBox;
                if (cb != null && cb.Checked && cb.Tag is string) op.RemoveTypes.Add((string)cb.Tag);
            }
            op.Exact = chkExact.Checked;
            op.CaseSensitive = chkCase.Checked;
            return op;
        }

        private void ConvertNow(bool dryRun)
        {
            try
            {
                if (loaded == null) { MessageBox.Show("请先选择 .adofai 文件", "level convert"); return; }
                CoreOptions op = CollectOptions();
                if (!op.Upgrade && !op.Remove)
                {
                    MessageBox.Show("请至少选择一项更改要求（升级 或 删除事件）", "level convert");
                    return;
                }
                if (op.Remove && listKeywordEv.Items.Count == 0 && txtKeyword.Text.Length == 0 && op.RemoveTypes.Count == 0)
                {
                    MessageBox.Show("删除事件：请输入关键词并点【添加】，或至少勾选一种事件类型", "level convert");
                    return;
                }
                if (op.RemoveDecorations && listKeywordDecor.Items.Count == 0 && txtKeywordDecor.Text.Length == 0)
                {
                    MessageBox.Show("删除装饰：请输入装饰关键词并点【添加】", "level convert");
                    return;
                }

                string inDir = Path.GetDirectoryName(loadedPath);
                string outPath;
                if (chkOverwrite.Checked) outPath = loadedPath;
                else
                {
                    string name = txtOutName.Text.Trim();
                    if (name.Length == 0) { MessageBox.Show("请填写输出文件名", "level convert"); return; }
                    outPath = Core.MakeOutputName(loadedPath, name); // 规范化：非法字符清理 + 单一 .adofai 后缀
                    if (string.Equals(outPath, loadedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        DialogResult dr = MessageBox.Show("输出名与原文件相同，将覆盖原文件。继续吗？", "level convert", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (dr != DialogResult.Yes) return;
                    }
                    else if (File.Exists(outPath))
                    {
                        DialogResult dr = MessageBox.Show("输出文件已存在：\n" + outPath + "\n\n覆盖它吗？", "level convert", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (dr != DialogResult.Yes) return;
                    }
                }

                // 每次转换都从磁盘重新加载，避免叠加改动
                LoadedLevel L = Core.LoadFile(loadedPath);
                txtLog.Clear();
                if (L.Repairs.Count > 0) AppendLog("[容错] 已修复: " + string.Join("、", L.Repairs.ToArray()));

                bool hadChanges;
                List<string> lines = Core.ApplyOperations(L, op, out hadChanges);
                foreach (string line in lines) AppendLog(line);
                if (!hadChanges) AppendLog("[提示] 没有发生任何改动");

                string outText = Core.Serialize(L, "auto", "auto");
                bool writeBom = L.HadBom;

                if (dryRun)
                {
                    AppendLog("[预览] 未写文件。将写入: " + outPath);
                    return;
                }

                Core.WriteOutput(outPath, outText, writeBom);
                long size = new FileInfo(outPath).Length;
                AppendLog("[输出] " + outPath);
                AppendLog("[完成] " + size + " 字节 · BOM " + (writeBom ? "有" : "无"));
                lastOutputDir = inDir;
                btnOpenFolder.Enabled = true;
                MessageBox.Show("转换完成！\n\n" + outPath + "\n\n" + Summarize(lines), "level convert",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppendLog("[失败] " + ex.Message);
                MessageBox.Show("转换失败：\n" + ex.Message, "level convert", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string Summarize(List<string> lines)
        {
            List<string> keep = new List<string>();
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].StartsWith("[升级]", StringComparison.Ordinal) || lines[i].StartsWith("[删除]", StringComparison.Ordinal))
                    keep.Add(lines[i]);
            return keep.Count > 0 ? string.Join("\n", keep.ToArray()) : "（没有发生改动）";
        }

        private void ShowStats()
        {
            try
            {
                if (loaded == null) { MessageBox.Show("请先选择文件", "level convert"); return; }
                LoadedLevel L = Core.LoadFile(loadedPath);
                string text = Core.GetStatsText(L);
                Form dlg = new Form();
                dlg.Text = "事件统计 — " + Path.GetFileName(loadedPath);
                dlg.Font = font;
                dlg.ClientSize = new Size(ClientSize.Width - 100, ClientSize.Height - 140);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.Sizable;
                TextBox tb = new TextBox();
                tb.Multiline = true;
                tb.ReadOnly = true;
                tb.ScrollBars = ScrollBars.Both;
                tb.WordWrap = false;
                tb.Font = fontMono;
                tb.Dock = DockStyle.Fill;
                dlg.Controls.Add(tb);
                Button ok = new Button();
                ok.Text = "关闭";
                ok.Dock = DockStyle.Bottom;
                ok.Click += delegate { dlg.Close(); };
                dlg.Controls.Add(ok);
                dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show("统计失败：\n" + ex.Message, "level convert", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenFolder()
        {
            try
            {
                string dir = lastOutputDir;
                if (string.IsNullOrEmpty(dir) && loadedPath != null) dir = Path.GetDirectoryName(loadedPath);
                if (dir != null && Directory.Exists(dir))
                    Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { }
        }

        private void AppendLog(string line)
        {
            if (txtLog.Text.Length > 0) txtLog.AppendText(Environment.NewLine);
            txtLog.AppendText(line.Replace("\r\n", Environment.NewLine));
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        // ---------------- 事件类型面板 ----------------
        // 始终按编写标准事件全集显示（未加载文件时也非空）；加载后附实测数量，
        // 谱面中出现但不在标准清单的类型（如旧名 SetDefaultText）追加在"其他"组。
        // 每个大类独立起行（段落）；大类名前有样式同类型复选框的"大类全选"。

        private sealed class TypeGroup { public CheckBox All; public List<CheckBox> Children = new List<CheckBox>(); }
        private readonly List<TypeGroup> typeGroups = new List<TypeGroup>();
        private bool typeGroupSync;

        private void PopulateTypePanel(LoadedLevel L)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (L != null)
                foreach (KeyValuePair<string, int> kv in Core.GetTypeCounts(L))
                    counts[kv.Key] = kv.Value;

            flpTypes.SuspendLayout();
            flpTypes.Controls.Clear();
            typeGroups.Clear();
            HashSet<string> listed = new HashSet<string>();
            foreach (Core.EventGroup grp in Core.EventGroups)
            {
                List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
                foreach (string t in grp.Types)
                {
                    listed.Add(t);
                    int c;
                    bool has = counts.TryGetValue(t, out c);
                    items.Add(new KeyValuePair<string, string>(t, has && c > 0 ? t + "（" + c.ToString(CultureInfo.InvariantCulture) + "）" : t));
                }
                AddTypeGroup(grp.Name + "：", items);
            }
            if (L != null)
            {
                List<KeyValuePair<string, int>> extras = new List<KeyValuePair<string, int>>();
                foreach (KeyValuePair<string, int> kv in counts)
                    if (!listed.Contains(kv.Key)) extras.Add(kv);
                extras.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value - a.Value; });
                if (extras.Count > 0)
                {
                    List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
                    foreach (KeyValuePair<string, int> kv in extras)
                        items.Add(new KeyValuePair<string, string>(kv.Key, kv.Key + "（" + kv.Value.ToString(CultureInfo.InvariantCulture) + "）"));
                    AddTypeGroup("其他（谱面实测，不在标准清单）：", items);
                }
            }
            flpTypes.ResumeLayout();
        }

        // 一个大类 = [□全选] 大类名： 类型□ 类型□ ...（超宽自动换行；下一个大类强制另起一行）
        private void AddTypeGroup(string header, List<KeyValuePair<string, string>> items)
        {
            CheckBox all = new CheckBox();
            all.Text = "全选";
            all.AutoSize = true;
            all.Margin = new Padding(0, 8, 2, 2);
            Label hl = MkFlowHeader(header);
            flpTypes.Controls.Add(all);
            flpTypes.Controls.Add(hl);

            TypeGroup grp = new TypeGroup();
            grp.All = all;
            Control last = hl;
            foreach (KeyValuePair<string, string> kv in items)
            {
                CheckBox cb = MkTypeCheck(kv.Key, kv.Value);
                flpTypes.Controls.Add(cb);
                grp.Children.Add(cb);
                last = cb;
                cb.CheckedChanged += delegate { if (!typeGroupSync) RefreshGroupState(grp); };
            }
            all.CheckedChanged += delegate
            {
                if (typeGroupSync) return;
                typeGroupSync = true;
                foreach (CheckBox cb in grp.Children) cb.Checked = all.Checked;
                typeGroupSync = false;
            };
            typeGroups.Add(grp);
            if (last != null) flpTypes.SetFlowBreak(last, true);   // 大类段落换行
            RefreshGroupState(grp);
        }

        private void RefreshGroupState(TypeGroup grp)
        {
            typeGroupSync = true;
            bool allC = grp.Children.Count > 0;
            foreach (CheckBox cb in grp.Children) if (!cb.Checked) { allC = false; break; }
            grp.All.Checked = allC;
            typeGroupSync = false;
        }

        private Label MkFlowHeader(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = fontBold;
            l.ForeColor = Color.FromArgb(70, 70, 70);
            l.Margin = new Padding(2, 8, 12, 2);
            return l;
        }

        private CheckBox MkTypeCheck(string typeName, string display)
        {
            CheckBox cb = new CheckBox();
            cb.Text = display;
            cb.Tag = typeName;
            cb.AutoSize = true;
            cb.Margin = new Padding(0, 2, 10, 2);
            return cb;
        }

        // ---------------- F11 全屏 ----------------
        private Rectangle preBounds;
        private FormBorderStyle preStyle;
        private FormWindowState preState;
        private bool fullscreen;

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F11) return;
            e.Handled = true;
            if (!fullscreen)
            {
                preBounds = Bounds;
                preStyle = FormBorderStyle;
                preState = WindowState;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
                fullscreen = true;
            }
            else
            {
                FormBorderStyle = preStyle;
                WindowState = preState;
                Bounds = preBounds;
                fullscreen = false;
            }
        }
    }
}

// ============================================================================
//  Cli.cs — 命令行前端（与 Core.cs 一起编译为 level convert.exe）
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LevelConvert
{
    internal static class Program
    {
        private static bool HelpShown;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                CliOptions op = ParseArgs(args);
                if (op == null) return HelpShown ? 0 : 2;
                return Run(op);
            }
            catch (Exception ex)
            {
                try { Console.ForegroundColor = ConsoleColor.Red; } catch { }
                Console.Error.WriteLine("[错误] " + ex.Message);
                try { Console.ResetColor(); } catch { }
                return 1;
            }
        }

        private sealed class CliOptions
        {
            public string Input;
            public string Out;
            public bool InPlace;
            public bool List;
            public bool DryRun;
            public string Eol = "auto";
            public string Indent = "auto";
            public int Bom = 0;
            public CoreOptions Core = new CoreOptions();
        }

        private static void Help()
        {
            HelpShown = true;
            Console.WriteLine(
@"LevelConvert — ADOFAI (.adofai) 关卡转换工具

用法:
  level convert.exe <输入.adofai> [操作与选项]

版本升级:
  --upgrade                升级到 v19: settings.version=19、补齐缺失的 v19 核心 78 键、
                           pathData -> angleData、字符串开关 ""Enabled""/""Disabled"" -> true/false
  --minimal                升级时不补缺失键（仅版本号 / 本体转换 / 布尔归一）
  --no-pathdata            升级时不转换 pathData
  --keep-pathdata          转换后保留原 pathData 字符串（默认删除）

事件删除（actions 与 decorations; --actions-only/--decorations-only 可限定）:
  --remove <关键词>        删除 eventType 或 eventTag 含关键词的事件
  --remove-type <关键词>   仅按 eventType 匹配（关键词包含式）
  --remove-types <列表>    按事件类型精确多选删除，如 ""Twirl,Bookmark""
  --or                     关键词与 --remove-types 同时给出时改为""满足其一""（默认同时满足）
  --remove-tag <关键词>    仅按 eventTag 匹配
  --field <字段名>         配合 --remove: 仅按指定字段匹配
  --any                    配合 --remove: 任一字符串字段含关键词即删除
  --exact                  精确相等匹配（默认包含匹配）
  --case-sensitive         区分大小写（默认不区分）

辅助:
  --list                   列出事件类型与 eventTag 分布（不修改文件，辅助选关键词）
  --dry-run                只报告将发生的改动，不写文件
  -o, --out <文件>         输出路径（默认: <输入名> - converted.adofai，单一后缀）
  --in-place               直接覆盖输入文件（与 --out 互斥）
  --eol <crlf|lf|auto>     换行风格（默认 auto=跟随输入）
  --indent <2|4|tab|compact|auto>  缩进风格（默认 auto=跟随输入）
  --keep-bom / --no-bom    BOM 处理（默认 auto=跟随输入）
  -h, --help               本帮助

示例:
  level convert.exe old.adofai --upgrade
  level convert.exe level.adofai --remove-tag re_light --dry-run
  level convert.exe level.adofai --remove Twirl --actions-only -o fixed.adofai
  level convert.exe level.adofai --remove-types ""Twirl,Bookmark""
  level convert.exe level.adofai --remove-tag re_light --remove-types ""MoveDecorations"" --or
  level convert.exe level.adofai --upgrade --remove-tag re_light --in-place

说明:
  * 输入按容错管线解析: 严格 JSON -> 尾逗号/字符串内裸控制字符/顶层缺失逗号修复。
    输出为等价严格 JSON，保留未知字段。图形界面版: level convert GUI.exe。
  * pathData 解码表来自 ADOFAI-JS（24 字母绝对角 + '!'=999 + 相对偏移 5/6/7/8/9/h/j/t/y），
    可用 exe 同目录 pathdata-chars.json 覆盖（见 README）。");
        }

        private static string Next(string[] args, ref int i, string opt)
        {
            if (i + 1 >= args.Length) throw new Exception(opt + " 缺少参数");
            string v = args[++i];
            if (v.StartsWith("-", StringComparison.Ordinal) && v.Length > 1 && v != "-")
            {
                double d;
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new Exception(opt + " 的参数不应以 - 开头: " + v);
            }
            return v;
        }

        private static CliOptions ParseArgs(string[] args)
        {
            CliOptions op = new CliOptions();
            if (args.Length == 0) { Help(); return null; }
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "/?" || a == "-h" || a == "--help" || a == "help") { Help(); return null; }
                else if (a == "--upgrade") op.Core.Upgrade = true;
                else if (a == "--minimal") op.Core.Minimal = true;
                else if (a == "--no-pathdata") op.Core.NoPathData = true;
                else if (a == "--keep-pathdata") op.Core.KeepPathData = true;
                else if (a == "--remove") { op.Core.Remove = true; op.Core.RemoveMode = "any"; op.Core.RemoveKeyword = Next(args, ref i, a); }
                else if (a == "--remove-type") { op.Core.Remove = true; op.Core.RemoveMode = "type"; op.Core.RemoveKeyword = Next(args, ref i, a); }
                else if (a == "--remove-types") {
                    op.Core.Remove = true;
                    string list = Next(args, ref i, a);
                    foreach (string t in list.Split(new char[] { ',', '|' }))
                    {
                        string s = t.Trim();
                        if (s.Length > 0 && !op.Core.RemoveTypes.Contains(s)) op.Core.RemoveTypes.Add(s);
                    }
                }
                else if (a == "--or") op.Core.TypesOr = true;
                else if (a == "--remove-tag") { op.Core.Remove = true; op.Core.RemoveMode = "tag"; op.Core.RemoveKeyword = Next(args, ref i, a); }
                else if (a == "--field") op.Core.FieldName = Next(args, ref i, a);
                else if (a == "--any") op.Core.RemoveMode = "anyfield";
                else if (a == "--remove-decorations" || a == "--remove-decor") { op.Core.RemoveDecorations = true; op.Core.RemoveDecorationsKeyword = Next(args, ref i, a); }
                else if (a == "--remove-keywords") { op.Core.Remove = true; foreach (string k in Next(args, ref i, a).Split(',')) op.Core.RemoveKeywords.Add(k); }
                else if (a == "--exact") op.Core.Exact = true;
                else if (a == "--case-sensitive") op.Core.CaseSensitive = true;
                else if (a == "--actions-only") op.Core.ActionsOnly = true;
                else if (a == "--decorations-only") op.Core.DecorationsOnly = true;
                else if (a == "--list") op.List = true;
                else if (a == "--dry-run") op.DryRun = true;
                else if (a == "-o" || a == "--out") op.Out = Next(args, ref i, a);
                else if (a == "--in-place") op.InPlace = true;
                else if (a == "--eol") op.Eol = Next(args, ref i, a);
                else if (a == "--indent") op.Indent = Next(args, ref i, a);
                else if (a == "--keep-bom") op.Bom = 1;
                else if (a == "--no-bom") op.Bom = -1;
                else if (a == "--") { }
                else if (a.StartsWith("-", StringComparison.Ordinal))
                    throw new Exception("未知选项 " + a + "（--help 查看用法）");
                else
                {
                    if (op.Input != null) throw new Exception("只允许一个输入文件（已给 " + op.Input + "，又给 " + a + "）");
                    op.Input = a;
                }
            }
            if (op.Input == null) throw new Exception("缺少输入文件（--help 查看用法）");
            if (!File.Exists(op.Input)) throw new Exception("输入文件不存在: " + op.Input);
            if (op.InPlace && op.Out != null) throw new Exception("--in-place 与 --out 互斥");
            if (op.Core.FieldName != null)
            {
                if (op.Core.RemoveMode == "anyfield") op.Core.RemoveMode = "field";
                else if (op.Core.RemoveMode == "any") op.Core.RemoveMode = "field";
            }
            // 兼容旧脚本：--decorations-only + --remove 视作删除装饰（沿用 --remove 的关键词）
            if (op.Core.DecorationsOnly && op.Core.Remove && !op.Core.RemoveDecorations)
            {
                op.Core.RemoveDecorations = true;
                op.Core.RemoveDecorationsKeyword = op.Core.RemoveKeyword;
            }
            // 关键词可选：不填 = 不加关键词限制（--remove 仅类型删；--remove-decorations 删全部装饰）
            return op;
        }

        private static string OutPath(CliOptions op)
        {
            if (op.InPlace) return op.Input;
            if (op.Out != null) return op.Out;
            return Core.MakeOutputName(op.Input, null); // 默认: <原名> - converted.adofai（单一后缀）
        }

        private static int Run(CliOptions op)
        {
            PathData.LoadOptionalConfig();
            LoadedLevel L = Core.LoadFile(op.Input);
            if (L.Repairs.Count > 0) Console.WriteLine("[容错] 已修复: " + string.Join("、", L.Repairs.ToArray()));

            if (op.List)
            {
                Console.Write(Core.GetStatsText(L).Replace("\r\n", Environment.NewLine));
                return 0;
            }

            bool hadChanges;
            List<string> lines = Core.ApplyOperations(L, op.Core, out hadChanges);
            foreach (string line in lines) Console.WriteLine(line);
            if (!hadChanges)
                Console.WriteLine("[提示] 未指定任何操作或没有任何改动（--help 查看用法）");

            string outText = Core.Serialize(L, op.Eol, op.Indent);
            bool writeBom = (op.Bom == 1) || (op.Bom == 0 && L.HadBom);
            if (op.Bom == -1) writeBom = false;

            if (op.DryRun)
            {
                Console.WriteLine("[dry-run] 不写文件。将写入: " + OutPath(op));
                return 0;
            }

            string outPath = OutPath(op);
            Core.WriteOutput(outPath, outText, writeBom);
            SerStyle st = Core.StyleFor(L, op.Eol, op.Indent);
            Console.WriteLine("[输出] " + outPath + "（" + new FileInfo(outPath).Length + " 字节, BOM " + (writeBom ? "有" : "无") + ", " + (st.NewLine == "\r\n" ? "CRLF" : "LF") + "）");
            return 0;
        }
    }

}

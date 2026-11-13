// ============================================================================
//  Core.cs — ADOFAI (.adofai) 转换核心（CLI 与 GUI 共用后端）
//  容错解析 / 序列化 / 版本升级 / 事件删除。零外部依赖（.NET Framework 4.0）。
//  证据来源见 README.md。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LevelConvert
{
    // ------------------------------------------------------------------ JSON 模型
    abstract class JNode { }

    sealed class JObj : JNode
    {
        public List<KeyValuePair<string, JNode>> Entries = new List<KeyValuePair<string, JNode>>();

        public JNode Get(string key)
        {
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Key == key) return Entries[i].Value;
            return null;
        }
        public bool Has(string key) { return Get(key) != null; }
        public void Set(string key, JNode v)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Key == key) { Entries[i] = new KeyValuePair<string, JNode>(key, v); return; }
            Entries.Add(new KeyValuePair<string, JNode>(key, v));
        }
        public void SetAt(int index, string key, JNode v)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Key == key) { Entries.RemoveAt(i); break; }
            if (index > Entries.Count) index = Entries.Count;
            Entries.Insert(index, new KeyValuePair<string, JNode>(key, v));
        }
        public int IndexOf(string key)
        {
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Key == key) return i;
            return -1;
        }
    }

    sealed class JArr : JNode { public List<JNode> Items = new List<JNode>(); }
    sealed class JStr : JNode { public string V; public JStr(string v) { V = v; } }
    sealed class JNum : JNode { public string Raw; public JNum(string raw) { Raw = raw; } }
    sealed class JBool : JNode { public bool V; public JBool(bool v) { V = v; } }
    sealed class JNull : JNode { }

    // ------------------------------------------------------------------ 解析器
    sealed class JsonParseException : Exception
    {
        public JsonParseException(string msg) : base(msg) { }
    }

    sealed class JsonParser
    {
        private readonly string s;
        private int pos;

        public JsonParser(string text) { s = text; pos = 0; }

        public JNode Parse()
        {
            SkipWs();
            JNode n = ParseValue(0);
            SkipWs();
            if (pos < s.Length) throw Err("JSON 结束前有多余内容");
            return n;
        }

        private JsonParseException Err(string what)
        {
            int line = 1, col = 1;
            for (int i = 0; i < pos && i < s.Length; i++)
            {
                if (s[i] == '\n') { line++; col = 1; } else col++;
            }
            int from = Math.Max(0, pos - 40);
            int to = Math.Min(s.Length, pos + 40);
            string ctx = s.Substring(from, to - from).Replace("\r", "").Replace("\n", "\n");
            return new JsonParseException(string.Format(CultureInfo.InvariantCulture,
                "{0}（行 {1} 列 {2}，位置 {3}）\u2026{4}\u2026", what, line, col, pos, ctx));
        }

        private void SkipWs()
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') pos++;
                else break;
            }
        }

        private JNode ParseValue(int depth)
        {
            if (depth > 200) throw Err("嵌套过深");
            SkipWs();
            if (pos >= s.Length) throw Err("意外到达输入末尾");
            char c = s[pos];
            switch (c)
            {
                case '{': return ParseObj(depth);
                case '[': return ParseArr(depth);
                case '"': return new JStr(ParseString());
                case 't': Expect("true"); return new JBool(true);
                case 'f': Expect("false"); return new JBool(false);
                case 'n': Expect("null"); return new JNull();
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNum();
                    throw Err("意外字符 '" + c + "'");
            }
        }

        private void Expect(string word)
        {
            if (pos + word.Length > s.Length || string.CompareOrdinal(s, pos, word, 0, word.Length) != 0)
                throw Err("期望 " + word);
            pos += word.Length;
        }

        private JNum ParseNum()
        {
            int start = pos;
            if (pos < s.Length && s[pos] == '-') pos++;
            while (pos < s.Length && ((s[pos] >= '0' && s[pos] <= '9') || s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E' || s[pos] == '+' || s[pos] == '-'))
                pos++;
            string raw = s.Substring(start, pos - start);
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            { pos = start; throw Err("非法数字 '" + raw + "'"); }
            return new JNum(raw);
        }

        private string ParseString()
        {
            if (s[pos] != '"') throw Err("期望字符串");
            pos++;
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (pos >= s.Length) throw Err("字符串未闭合");
                char c = s[pos];
                if (c == '"') { pos++; return sb.ToString(); }
                if (c == '\\')
                {
                    pos++;
                    if (pos >= s.Length) throw Err("字符串未闭合（转义符后 EOF）");
                    char e = s[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > s.Length) throw Err("\\u 转义不完整");
                            string hex = s.Substring(pos, 4);
                            int code;
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw Err("非法 \\u 转义 " + hex);
                            sb.Append((char)code);
                            pos += 4;
                            break;
                        default: throw Err("非法转义 \\" + e);
                    }
                }
                else { sb.Append(c); pos++; }
            }
        }

        private JObj ParseObj(int depth)
        {
            JObj o = new JObj();
            pos++; // {
            SkipWs();
            if (pos < s.Length && s[pos] == '}') { pos++; return o; }
            while (true)
            {
                SkipWs();
                if (pos >= s.Length) throw Err("对象未闭合");
                if (s[pos] != '"') throw Err("对象键应为字符串");
                string key = ParseString();
                SkipWs();
                if (pos >= s.Length || s[pos] != ':') throw Err("对象键后应有 ':'");
                pos++;
                JNode v = ParseValue(depth + 1);
                o.Entries.Add(new KeyValuePair<string, JNode>(key, v));
                SkipWs();
                if (pos >= s.Length) throw Err("对象未闭合");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return o; }
                throw Err("对象中期望 ',' 或 '}'");
            }
        }

        private JArr ParseArr(int depth)
        {
            JArr a = new JArr();
            pos++; // [
            SkipWs();
            if (pos < s.Length && s[pos] == ']') { pos++; return a; }
            while (true)
            {
                JNode v = ParseValue(depth + 1);
                a.Items.Add(v);
                SkipWs();
                if (pos >= s.Length) throw Err("数组未闭合");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return a; }
                throw Err("数组中期望 ',' 或 ']'");
            }
        }
    }

    // ------------------------------------------------------------------ 容错修复
    internal static class Repair
    {
        private struct ScanState
        {
            public bool InStr;
            public bool Esc;
        }

        public static string StripTrailingCommas(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            ScanState st = new ScanState();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (st.InStr)
                {
                    sb.Append(c);
                    if (st.Esc) st.Esc = false;
                    else if (c == '\\') st.Esc = true;
                    else if (c == '"') st.InStr = false;
                    continue;
                }
                if (c == '"') { st.InStr = true; sb.Append(c); continue; }
                if (c == ',')
                {
                    int j = i + 1;
                    while (j < text.Length && (text[j] == ' ' || text[j] == '\t' || text[j] == '\r' || text[j] == '\n')) j++;
                    if (j < text.Length && (text[j] == '}' || text[j] == ']')) continue;
                    sb.Append(c); continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string EscapeRawControlChars(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            ScanState st = new ScanState();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (st.InStr)
                {
                    if (st.Esc) { sb.Append(c); st.Esc = false; continue; }
                    if (c == '\\') { sb.Append(c); st.Esc = true; continue; }
                    if (c == '"') { sb.Append(c); st.InStr = false; continue; }
                    if (c < 0x20) { sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); continue; }
                    sb.Append(c); continue;
                }
                if (c == '"') { st.InStr = true; sb.Append(c); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static readonly string[] TopKeys = new string[] { "angleData", "settings", "actions", "decorations", "pathData" };

        public static string InsertMissingTopCommas(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length + 64);
            ScanState st = new ScanState();
            int depth = 0;
            bool afterArrayClose = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (st.InStr)
                {
                    sb.Append(c);
                    if (st.Esc) st.Esc = false;
                    else if (c == '\\') st.Esc = true;
                    else if (c == '"') st.InStr = false;
                    continue;
                }
                if (c == '"')
                {
                    if (afterArrayClose)
                    {
                        int j = i + 1, k = j;
                        while (k < text.Length && text[k] != '"') k++;
                        if (k < text.Length)
                        {
                            string word = text.Substring(j, k - j);
                            bool isTop = false;
                            for (int t = 0; t < TopKeys.Length; t++) if (TopKeys[t] == word) { isTop = true; break; }
                            if (isTop) sb.Append(',');
                        }
                        afterArrayClose = false;
                    }
                    st.InStr = true; sb.Append(c); continue;
                }
                if (c == '{' || c == '[') { depth++; afterArrayClose = false; sb.Append(c); continue; }
                if (c == '}' || c == ']')
                {
                    depth--;
                    afterArrayClose = (c == ']' && depth == 1);
                    sb.Append(c); continue;
                }
                if (c == ',') afterArrayClose = false;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ 序列化
    internal sealed class SerStyle
    {
        public string NewLine = "\r\n";
        public string IndentUnit = "  ";
        public bool Compact = false;
    }

    internal static class Ser
    {
        public static string Escape(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Num(double v)
        {
            decimal d;
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15)
                return ((long)v).ToString(CultureInfo.InvariantCulture);
            if (decimal.TryParse(v.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d.ToString(CultureInfo.InvariantCulture);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        public static void Write(JNode n, StringBuilder sb, SerStyle st, int depth)
        {
            if (n is JObj)
            {
                JObj o = (JObj)n;
                if (o.Entries.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                for (int i = 0; i < o.Entries.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (!st.Compact) { sb.Append(st.NewLine).Append(Repeat(st.IndentUnit, depth + 1)); }
                    sb.Append('"').Append(Escape(o.Entries[i].Key)).Append('"');
                    sb.Append(st.Compact ? ":" : ": ");
                    Write(o.Entries[i].Value, sb, st, depth + 1);
                }
                if (!st.Compact) sb.Append(st.NewLine).Append(Repeat(st.IndentUnit, depth));
                sb.Append('}');
            }
            else if (n is JArr)
            {
                JArr a = (JArr)n;
                if (a.Items.Count == 0) { sb.Append("[]"); return; }
                bool flat = st.Compact || IsFlatArray(a);
                sb.Append('[');
                if (flat)
                {
                    for (int i = 0; i < a.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(st.Compact ? "," : ", ");
                        WriteFlat(a.Items[i], sb);
                    }
                }
                else
                {
                    for (int i = 0; i < a.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(st.NewLine).Append(Repeat(st.IndentUnit, depth + 1));
                        Write(a.Items[i], sb, st, depth + 1);
                    }
                    sb.Append(st.NewLine).Append(Repeat(st.IndentUnit, depth));
                }
                sb.Append(']');
            }
            else WriteFlat(n, sb);
        }

        private static void WriteFlat(JNode n, StringBuilder sb)
        {
            if (n is JStr) sb.Append('"').Append(Escape(((JStr)n).V)).Append('"');
            else if (n is JNum) sb.Append(((JNum)n).Raw);
            else if (n is JBool) sb.Append(((JBool)n).V ? "true" : "false");
            else if (n is JNull) sb.Append("null");
            else { StringBuilder t = new StringBuilder(); Write(n, t, new SerStyle { Compact = true }, 0); sb.Append(t.ToString()); }
        }

        private static bool IsFlatArray(JArr a)
        {
            for (int i = 0; i < a.Items.Count; i++)
                if (!(a.Items[i] is JNum || a.Items[i] is JStr || a.Items[i] is JBool || a.Items[i] is JNull)) return false;
            if (a.Items.Count > 24) return true;
            for (int i = 0; i < a.Items.Count; i++) if (a.Items[i] is JNum) return true;
            return false;
        }

        private static string Repeat(string s, int n)
        {
            if (n <= 0) return "";
            StringBuilder sb = new StringBuilder(s.Length * n);
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }

        public static SerStyle Detect(string raw)
        {
            SerStyle st = new SerStyle();
            st.NewLine = raw.Contains("\r\n") ? "\r\n" : "\n";
            int nl = raw.IndexOf('\n');
            if (nl >= 0 && nl + 1 < raw.Length)
            {
                char c = raw[nl + 1];
                if (c == '\t') st.IndentUnit = "\t";
                else if (c == ' ')
                {
                    int k = nl + 1;
                    while (k < raw.Length && raw[k] == ' ') k++;
                    int spaces = k - (nl + 1);
                    st.IndentUnit = spaces <= 1 ? "  " : new string(' ', Math.Min(spaces, 8));
                }
                else st.IndentUnit = "  ";
            }
            else st.Compact = raw.IndexOf('\n') < 0 && raw.IndexOf('\r') < 0;
            return st;
        }
    }

    // ------------------------------------------------------------------ pathData 解码
    internal static class PathData
    {
        private static Dictionary<char, int> AngleChars = BuildAngles();
        private static Dictionary<char, int> OffsetChars = BuildOffsets();
        public const int Midspin = 999;

        private static Dictionary<char, int> BuildAngles()
        {
            Dictionary<char, int> d = new Dictionary<char, int>();
            string keys = "RpJEToUqGQHWLxNZFVDYBCMA";
            for (int i = 0; i < keys.Length; i++) d[keys[i]] = i * 15;
            d['!'] = Midspin;
            return d;
        }

        private static Dictionary<char, int> BuildOffsets()
        {
            // 来源：ADOFAI-JS src/pathdata/index.ts offsetMap（result = 前块角 + offset）
            Dictionary<char, int> d = new Dictionary<char, int>();
            d['5'] = 72; d['6'] = -72; d['7'] = 52; d['8'] = -52; d['9'] = -30;
            d['h'] = 120; d['j'] = -120; d['t'] = 60; d['y'] = 300;
            return d;
        }

        // 可选配置覆盖：exe 同目录 pathdata-chars.json
        public static void LoadOptionalConfig()
        {
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pathdata-chars.json");
            if (!File.Exists(p)) return;
            string txt = File.ReadAllText(p);
            JNode n = new JsonParser(Repair.StripTrailingCommas(txt)).Parse();
            JObj o = n as JObj;
            if (o == null) throw new Exception("pathdata-chars.json 顶层应为 JSON 对象");
            JObj ang = o.Get("angles") as JObj;
            JObj off = o.Get("offsets") as JObj;
            if (ang == null && off == null) throw new Exception("pathdata-chars.json 应含 angles/offsets 至少一项");
            if (ang != null)
            {
                AngleChars.Clear();
                foreach (KeyValuePair<string, JNode> kv in ang.Entries)
                {
                    JNum num = kv.Value as JNum;
                    if (num == null || kv.Key.Length != 1) throw new Exception("pathdata-chars.json angles 值应为单字符键 + 数字: " + kv.Key);
                    AngleChars[kv.Key[0]] = (int)double.Parse(num.Raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
            }
            if (off != null)
            {
                OffsetChars.Clear();
                foreach (KeyValuePair<string, JNode> kv in off.Entries)
                {
                    JNum num = kv.Value as JNum;
                    if (num == null || kv.Key.Length != 1) throw new Exception("pathdata-chars.json offsets 值应为单字符键 + 数字: " + kv.Key);
                    OffsetChars[kv.Key[0]] = (int)double.Parse(num.Raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
            }
        }

        public sealed class DecodeResult
        {
            public List<JNode> Angles = new List<JNode>();
            public int Unknown;
            public int Midspins;
            public int Relative;
            public string UnknownChars = "";
        }

        public static DecodeResult Decode(string pd)
        {
            DecodeResult r = new DecodeResult();
            int prev = 0;
            StringBuilder unknown = new StringBuilder();
            for (int i = 0; i < pd.Length; i++)
            {
                char c = pd[i];
                int a;
                if (AngleChars.TryGetValue(c, out a))
                {
                    r.Angles.Add(new JNum(a.ToString(CultureInfo.InvariantCulture)));
                    prev = a;
                    if (a == Midspin) r.Midspins++;
                    continue;
                }
                if (OffsetChars.TryGetValue(c, out a))
                {
                    int v = prev + a;
                    r.Angles.Add(new JNum(v.ToString(CultureInfo.InvariantCulture)));
                    prev = v;
                    r.Relative++;
                    continue;
                }
                r.Angles.Add(new JNum(prev.ToString(CultureInfo.InvariantCulture)));
                r.Unknown++;
                if (unknown.ToString().IndexOf(c) < 0) unknown.Append(c);
            }
            r.UnknownChars = unknown.ToString();
            return r;
        }
    }

    // ------------------------------------------------------------------ 转换核心
    internal sealed class CoreOptions
    {
        public bool Upgrade;
        public bool Minimal;
        public bool NoPathData;
        public bool KeepPathData;
        public bool Remove;                 // 删除事件（仅 actions 数组）
        public bool RemoveDecorations;      // 删除装饰（仅 decorations 数组；行含全部关键词即删）
        public List<string> RemoveKeywords = new List<string>();          // 删除事件的多关键词（全部命中才删）
        public List<string> RemoveDecorationsKeywords = new List<string>(); // 删除装饰的多关键词（全部命中才删）
        public string RemoveDecorationsKeyword = "";   // 兼容：单关键词入口
        public bool RemoveDecorationsExact;            // 删除装饰：精确匹配
        public bool RemoveDecorationsCaseSensitive;    // 删除装饰：区分大小写
        public string RemoveKeyword = "";   // 删除事件的关键词
        public string RemoveMode = "any";   // any | type | tag | field | anyfield（关键词的匹配字段）
        public List<string> RemoveTypes = new List<string>(); // 事件类型多选（精确匹配；留空=不限类型）
        public bool TypesOr;                // 关键词与类型同时给出时：false=同时满足(且)，true=满足其一(或)
        public string FieldName;
        public bool Exact;
        public bool CaseSensitive;
        public bool ActionsOnly;
        public bool DecorationsOnly;
    }

    internal sealed class LoadedLevel
    {
        public string Path;
        public string RawText;
        public bool HadBom;
        public JObj Root;
        public List<string> Repairs = new List<string>();
        public bool NonUtf8;
    }

    internal static class Core
    {
        internal static readonly string[] Core78Keys = new string[] {
            "version","artist","specialArtistType","artistPermission","song","author",
            "separateCountdownTime","previewImage","previewIcon","previewIconColor","previewSongStart",
            "previewSongDuration","seizureWarning","levelDesc","levelTags","artistLinks","speedTrialAim",
            "difficulty","requiredMods","songFilename","bpm","volume","offset","pitch","hitsound",
            "hitsoundVolume","countdownTicks","tileShape","trackColorType","trackColor",
            "secondaryTrackColor","trackColorAnimDuration","trackColorPulse","trackPulseLength",
            "trackStyle","trackTexture","trackTextureScale","trackGlowIntensity","trackAnimation",
            "beatsAhead","trackDisappearAnimation","beatsBehind","backgroundColor","showDefaultBGIfNoImage",
            "showDefaultBGTile","defaultBGTileColor","defaultBGShapeType","defaultBGShapeColor","bgImage",
            "bgImageColor","parallax","bgDisplayMode","imageSmoothing","lockRot","loopBG","scalingRatio",
            "relativeTo","position","rotation","zoom","pulseOnFloor","bgVideo","loopVideo","vidOffset",
            "floorIconOutlines","stickToFloors","planetEase","planetEaseParts","planetEasePartBehavior",
            "defaultTextColor","defaultTextShadowColor","congratsText","perfectText","legacyFlash",
            "legacyCamRelativeTo","legacySpriteTiles","legacyTween","disableV15Features"
        };

        private static readonly string[] SettingsBoolKeys = new string[] {
            "separateCountdownTime","seizureWarning","showDefaultBGIfNoImage","showDefaultBGTile",
            "imageSmoothing","lockRot","loopBG","pulseOnFloor","loopVideo","floorIconOutlines",
            "stickToFloors","legacyFlash","legacyCamRelativeTo","legacySpriteTiles","legacyTween",
            "disableV15Features"
        };

        public static LoadedLevel LoadFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            LoadedLevel L = new LoadedLevel();
            L.Path = path;
            L.HadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Encoding utf8Strict = new UTF8Encoding(false, true);
            try { L.RawText = L.HadBom ? utf8Strict.GetString(bytes, 3, bytes.Length - 3) : utf8Strict.GetString(bytes); }
            catch (DecoderFallbackException)
            {
                L.RawText = Encoding.UTF8.GetString(bytes, L.HadBom ? 3 : 0, bytes.Length - (L.HadBom ? 3 : 0));
                L.NonUtf8 = true;
                L.Repairs.Add("非UTF-8字节按替换字符解码");
            }

            JNode root;
            JsonParser p = new JsonParser(L.RawText);
            try { root = p.Parse(); }
            catch (JsonParseException e1)
            {
                string t1 = Repair.StripTrailingCommas(L.RawText);
                p = new JsonParser(t1);
                try { root = p.Parse(); L.Repairs.Add("尾逗号"); }
                catch (JsonParseException e2)
                {
                    string t2 = Repair.EscapeRawControlChars(t1);
                    p = new JsonParser(t2);
                    try { root = p.Parse(); L.Repairs.Add("尾逗号+字符串内裸控制字符"); }
                    catch (JsonParseException e3)
                    {
                        string t3 = Repair.InsertMissingTopCommas(t2);
                        p = new JsonParser(t3);
                        try { root = p.Parse(); L.Repairs.Add("尾逗号+裸控制字符+顶层缺失逗号"); }
                        catch (JsonParseException e4)
                        {
                            throw new Exception("无法解析输入（已尝试 尾逗号/裸控制字符/顶层缺逗号 修复）。\n  首次错误: " + e1.Message + "\n  最终错误: " + e4.Message);
                        }
                    }
                }
            }
            L.Root = root as JObj;
            if (L.Root == null) throw new Exception("顶层不是 JSON 对象");
            return L;
        }

        public static bool HasOperations(CoreOptions op)
        {
            return op.Upgrade || RemoveActive(op);
        }

        private static bool RemoveActive(CoreOptions op)
        {
            if (!op.Remove) return false;
            bool kw = !string.IsNullOrEmpty(op.RemoveKeyword);
            bool types = op.RemoveTypes != null && op.RemoveTypes.Count > 0;
            return kw || types;
        }

        /// 执行删除与升级，返回输出行（与 CLI 文案一致）。hadChanges=false 表示没有任何实际改动。
        public static List<string> ApplyOperations(LoadedLevel L, CoreOptions op, out bool hadChanges)
        {
            List<string> lines = new List<string>();
            hadChanges = false;
            JObj top = L.Root;

            // ---- 删除事件（仅 actions；行须同时包含全部关键词 ∧ 类型集合，单给其一时按单项）----
            List<string> kwList = MergeKeywords(op.RemoveKeyword, op.RemoveKeywords);
            bool kwActive = kwList.Count > 0;
            HashSet<string> typeSet = NormalizeTypes(op.RemoveTypes);
            bool typesActive = typeSet != null && typeSet.Count > 0;
            StringComparison cmpTop = op.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int removedActions = 0, removedDecor = 0;
            Dictionary<string, int> removedByType = new Dictionary<string, int>();
            if (op.Remove && !op.DecorationsOnly && (kwActive || typesActive))
            {
                JArr actions = top.Get("actions") as JArr;
                if (actions != null)
                    removedActions = FilterEvents(actions, op, kwList, removedByType, "actions", kwActive, typeSet, typesActive);
            }
            // ---- 删除装饰（仅 decorations；行须同时包含全部关键词，与类型面板无关）----
            List<string> decorKwList = MergeKeywords(op.RemoveDecorationsKeyword, op.RemoveDecorationsKeywords);
            bool decorKwActive = decorKwList.Count > 0;
            StringComparison cmpDecor = op.RemoveDecorationsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (op.RemoveDecorations && !op.ActionsOnly && decorKwActive)
            {
                JArr decorations = top.Get("decorations") as JArr;
                if (decorations != null)
                    removedDecor = FilterDecorations(decorations, decorKwList, op.RemoveDecorationsExact, cmpDecor, removedByType);
            }
            int total = removedActions + removedDecor;
            if (op.Remove && removedActions == 0 && (kwActive || typesActive) && !op.DecorationsOnly)
                lines.Add("[删除事件] 没有匹配的事件（需同时包含: " + JoinKeywords(kwList) + "）");
            if (op.RemoveDecorations && removedDecor == 0 && decorKwActive && !op.ActionsOnly)
                lines.Add("[删除装饰] 没有匹配的装饰（需同时包含: " + JoinKeywords(decorKwList) + "）");
            if (total > 0)
            {
                hadChanges = true;
                if (removedActions > 0)
                {
                    string cond = kwActive && typesActive
                        ? (op.TypesOr ? "关键词∪类型" : "全部关键词(" + kwList.Count + ")∩类型")
                        : (kwActive ? ("全部关键词(" + kwList.Count + ")") : "类型");
                    lines.Add("[删除事件] 共删除 " + removedActions + " 个事件（条件 " + cond + "）");
                    List<KeyValuePair<string, int>> ta = new List<KeyValuePair<string, int>>();
                    foreach (KeyValuePair<string, int> kv in removedByType) if (kv.Key.StartsWith("actions/", StringComparison.Ordinal)) ta.Add(kv);
                    ta.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value - a.Value; });
                    for (int i = 0; i < ta.Count && i < 12; i++)
                        lines.Add("        - " + ta[i].Key.Substring(8) + " ×" + ta[i].Value);
                }
                if (removedDecor > 0)
                {
                    lines.Add("[删除装饰] 共删除 " + removedDecor + " 个装饰（行含全部关键词" + (decorKwList.Count > 1 ? "×" + decorKwList.Count : "") + "）");
                    List<KeyValuePair<string, int>> td = new List<KeyValuePair<string, int>>();
                    foreach (KeyValuePair<string, int> kv in removedByType) if (kv.Key.StartsWith("decorations/", StringComparison.Ordinal)) td.Add(kv);
                    td.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value - a.Value; });
                    for (int i = 0; i < td.Count && i < 12; i++)
                        lines.Add("        - " + td[i].Key.Substring(12) + " ×" + td[i].Value);
                }
            }

            // ---- 升级 ----
            if (op.Upgrade)
            {
                JObj settings = top.Get("settings") as JObj;
                if (settings == null) { settings = new JObj(); top.Set("settings", settings); lines.Add("[升级] 创建 settings"); hadChanges = true; }

                int oldVersion = -1;
                JNode vNode = settings.Get("version");
                if (vNode is JNum)
                {
                    double d;
                    if (double.TryParse(((JNum)vNode).Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) oldVersion = (int)d;
                }
                if (oldVersion != 19)
                {
                    settings.Set("version", new JNum("19"));
                    lines.Add("[升级] version " + (oldVersion < 0 ? "?" : oldVersion.ToString(CultureInfo.InvariantCulture)) + " -> 19");
                    hadChanges = true;
                }

                int boolFixed = 0;
                for (int i = 0; i < SettingsBoolKeys.Length; i++)
                {
                    JNode v = settings.Get(SettingsBoolKeys[i]);
                    if (v is JStr)
                    {
                        string s = ((JStr)v).V;
                        if (string.Equals(s, "Enabled", StringComparison.OrdinalIgnoreCase)) { settings.Set(SettingsBoolKeys[i], new JBool(true)); boolFixed++; }
                        else if (string.Equals(s, "Disabled", StringComparison.OrdinalIgnoreCase)) { settings.Set(SettingsBoolKeys[i], new JBool(false)); boolFixed++; }
                    }
                }
                if (boolFixed > 0) { lines.Add("[升级] 布尔归一 " + boolFixed + " 个字符串开关"); hadChanges = true; }

                int added = 0;
                if (!op.Minimal)
                {
                    for (int i = 0; i < Core78Keys.Length; i++)
                    {
                        string key = Core78Keys[i];
                        if (settings.Has(key)) continue;
                        JNode def = DefaultFor(key, oldVersion);
                        if (def != null) { settings.Set(key, def); added++; }
                    }
                    if (added > 0) { lines.Add("[升级] 补齐缺失核心键 " + added + " 个"); hadChanges = true; }
                }

                if (!op.NoPathData && top.Has("pathData") && !top.Has("angleData"))
                {
                    JStr pd = top.Get("pathData") as JStr;
                    if (pd != null)
                    {
                        PathData.DecodeResult dr = PathData.Decode(pd.V);
                        JArr arr = new JArr();
                        arr.Items.AddRange(dr.Angles.ToArray());
                        int idx = top.IndexOf("pathData");
                        top.SetAt(idx, "angleData", arr);
                        if (op.KeepPathData) top.Set("pathData", new JStr(pd.V));
                        lines.Add("[升级] pathData->angleData（" + dr.Angles.Count + " 块, 中旋 " + dr.Midspins + ", 相对偏移 " + dr.Relative + (dr.Unknown > 0 ? ", 未知字符 " + dr.Unknown + " [" + dr.UnknownChars + "]" : "") + "）");
                        hadChanges = true;
                        if (dr.Unknown > 0)
                            lines.Add("[警告] pathData 含 " + dr.Unknown + " 个未映射字符 [" + dr.UnknownChars + "]，已按 ADOFAI-JS 规则保持前块角；可用 pathdata-chars.json 配置映射");
                    }
                }
            }
            return lines;
        }

        private static JNode DefaultFor(string key, int oldVersion)
        {
            switch (key)
            {
                case "artist": case "artistPermission": case "song": case "author":
                case "previewImage": case "previewIcon": case "levelDesc": case "levelTags":
                case "artistLinks": case "songFilename": case "bgImage": case "bgVideo":
                case "congratsText": case "perfectText": case "trackTexture":
                    return new JStr("");
                case "specialArtistType": return new JStr("None");
                case "previewIconColor": return new JStr("003f52");
                case "previewSongStart": return new JNum("0");
                case "previewSongDuration": return new JNum("10");
                case "separateCountdownTime": return new JBool(true);
                case "seizureWarning": return new JBool(false);
                case "speedTrialAim": return new JNum("0");
                case "difficulty": return new JNum("1");
                case "requiredMods": return new JArr();
                case "bpm": return new JNum("100");
                case "volume": return new JNum("100");
                case "offset": return new JNum("0");
                case "pitch": return new JNum("100");
                case "hitsound": return new JStr("Kick");
                case "hitsoundVolume": return new JNum("100");
                case "countdownTicks": return new JNum("4");
                case "tileShape": return new JStr("Long");
                case "trackColorType": return new JStr("Single");
                case "trackColor": return new JStr("debb7b");
                case "secondaryTrackColor": return new JStr("ffffff");
                case "trackColorAnimDuration": return new JNum("2");
                case "trackColorPulse": return new JStr("None");
                case "trackPulseLength": return new JNum("10");
                case "trackStyle": return new JStr("Standard");
                case "trackTextureScale": return new JNum("1");
                case "trackGlowIntensity": return new JNum("0");
                case "trackAnimation": return new JStr("None");
                case "beatsAhead": return new JNum("3");
                case "trackDisappearAnimation": return new JStr("Fade");
                case "beatsBehind": return new JNum("0");
                case "backgroundColor": return new JStr("000000");
                case "showDefaultBGIfNoImage": return new JBool(true);
                case "showDefaultBGTile": return new JBool(false);
                case "defaultBGTileColor": return new JStr("101121");
                case "defaultBGShapeType": return new JStr("Disabled");
                case "defaultBGShapeColor": return new JStr("ffffff");
                case "bgImageColor": return new JStr("ffffff");
                case "parallax":
                {
                    JArr a = new JArr();
                    a.Items.Add(new JNum("100")); a.Items.Add(new JNum("100"));
                    return a;
                }
                case "bgDisplayMode": return new JStr("FitToScreen");
                case "imageSmoothing": return new JBool(true);
                case "lockRot": return new JBool(false);
                case "loopBG": return new JBool(false);
                case "scalingRatio": return new JNum("100");
                case "relativeTo": return new JStr("Player");
                case "position":
                {
                    JArr a = new JArr();
                    a.Items.Add(new JNum("0")); a.Items.Add(new JNum("0"));
                    return a;
                }
                case "rotation": return new JNum("0");
                case "zoom": return new JNum("100");
                case "pulseOnFloor": return new JBool(true);
                case "loopVideo": return new JBool(false);
                case "vidOffset": return new JNum("0");
                case "floorIconOutlines": return new JBool(false);
                case "stickToFloors": return new JBool(true);
                case "planetEase": return new JStr("Linear");
                case "planetEaseParts": return new JNum("1");
                case "planetEasePartBehavior": return new JStr("Mirror");
                case "defaultTextColor": return new JStr("ffffff");
                case "defaultTextShadowColor": return new JStr("00000050");
                case "legacyFlash": return new JBool(false);
                case "legacyCamRelativeTo": return new JBool(false);
                case "legacySpriteTiles": return new JBool(false);
                case "legacyTween": return new JBool(true);
                case "disableV15Features": return new JBool(oldVersion >= 0 && oldVersion < 15);
                default: return null;
            }
        }

        private static string JNodeToMatchString(JNode n)
        {
            if (n is JStr) return ((JStr)n).V;
            if (n is JNum) return ((JNum)n).Raw;
            if (n is JBool) return ((JBool)n).V ? "true" : "false";
            if (n is JNull) return "null";
            return null;
        }

        // “任一字符串字段”：深度遍历事件对象（含嵌套对象/数组），仅匹配字符串值；
        // 数字/布尔不转文本参与匹配，避免“10”这类关键词误删 floor=10 等数值字段。
        private static bool AnyStringFieldHit(JObj ev, string keyword, bool exact, StringComparison cmp)
        {
            return AnyStringHit(ev, keyword, exact, cmp, 0);
        }

        private static bool AnyStringHit(JNode n, string keyword, bool exact, StringComparison cmp, int depth)
        {
            if (n == null || depth > 8) return false;
            JObj o = n as JObj;
            if (o != null)
            {
                for (int i = 0; i < o.Entries.Count; i++)
                    if (AnyStringHit(o.Entries[i].Value, keyword, exact, cmp, depth + 1)) return true;
                return false;
            }
            JArr a = n as JArr;
            if (a != null)
            {
                for (int i = 0; i < a.Items.Count; i++)
                    if (AnyStringHit(a.Items[i], keyword, exact, cmp, depth + 1)) return true;
                return false;
            }
            if (n is JStr)
            {
                string s = ((JStr)n).V;
                return s.Length > 0 && Match(s, keyword, exact, cmp);
            }
            return false;
        }

        private static HashSet<string> NormalizeTypes(List<string> types)
        {
            if (types == null || types.Count == 0) return null;
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string t in types)
            {
                string s = (t ?? "").Trim();
                if (s.Length > 0) set.Add(s);
            }
            return set;
        }

        private static int FilterEvents(JArr arr, CoreOptions op, List<string> kwList, Dictionary<string, int> removedByType, string which, bool kwActive, HashSet<string> typeSet, bool typesActive)
        {
            List<JNode> keep = new List<JNode>();
            int removed = 0;
            StringComparison cmp = op.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            for (int i = 0; i < arr.Items.Count; i++)
            {
                JObj ev = arr.Items[i] as JObj;
                if (ev == null) { keep.Add(arr.Items[i]); continue; }
                bool hit = false;
                string typeLabel = "?";
                JNode t = ev.Get("eventType");
                if (t != null) { string s = JNodeToMatchString(t); if (s != null) typeLabel = s; }

                // 多关键词：每个关键词按当前匹配字段规则都要命中（AND）
                bool kwHit = false;
                if (kwActive)
                {
                    kwHit = true;
                    foreach (string kw in kwList)
                    {
                        bool h;
                        if (op.RemoveMode == "type")
                        {
                            string s = JNodeToMatchString(t);
                            h = (s != null && Match(s, kw, op.Exact, cmp));
                        }
                        else if (op.RemoveMode == "tag")
                        {
                            string s = JNodeToMatchString(ev.Get("eventTag"));
                            h = (s != null && s.Length > 0 && Match(s, kw, op.Exact, cmp));
                        }
                        else if (op.RemoveMode == "field")
                        {
                            string s = JNodeToMatchString(ev.Get(op.FieldName));
                            h = (s != null && Match(s, kw, op.Exact, cmp));
                        }
                        else if (op.RemoveMode == "anyfield")
                        {
                            h = AnyStringFieldHit(ev, kw, op.Exact, cmp);
                        }
                        else
                        {
                            string s1 = JNodeToMatchString(t);
                            string s2 = JNodeToMatchString(ev.Get("eventTag"));
                            h = (s1 != null && Match(s1, kw, op.Exact, cmp))
                               || (s2 != null && s2.Length > 0 && Match(s2, kw, op.Exact, cmp));
                        }
                        if (!h) { kwHit = false; break; }
                    }
                }

                bool typeHit = false;
                if (typesActive)
                {
                    string et = JNodeToMatchString(t);
                    typeHit = (et != null && typeSet.Contains(et));
                }

                hit = kwActive && typesActive
                    ? (op.TypesOr ? (kwHit || typeHit) : (kwHit && typeHit))
                    : (kwActive ? kwHit : typeHit);

                if (hit)
                {
                    removed++;
                    string kk = which + "/" + typeLabel;
                    if (removedByType.ContainsKey(kk)) removedByType[kk]++;
                    else removedByType[kk] = 1;
                }
                else keep.Add(arr.Items[i]);
            }
            arr.Items = keep;
            return removed;
        }

        // 合并单关键词入口与多关键词列表（去空、去重、保序）
        private static List<string> MergeKeywords(string single, List<string> multi)
        {
            List<string> r = new List<string>();
            if (!string.IsNullOrEmpty(single) && !r.Contains(single)) r.Add(single);
            if (multi != null)
                foreach (string k in multi)
                    if (!string.IsNullOrEmpty(k) && !r.Contains(k)) r.Add(k);
            return r;
        }

        private static string JoinKeywords(List<string> kws)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < kws.Count; i++) { if (i > 0) sb.Append(" 且 "); sb.Append("\"" + kws[i] + "\""); }
            return sb.ToString();
        }

        // 删除装饰：decorations 中任一字符串字段（含嵌套）包含关键词即删除，与类型面板无关
        private static int FilterDecorations(JArr arr, List<string> keywords, bool exact, StringComparison cmp, Dictionary<string, int> removedByType)
        {
            List<JNode> keep = new List<JNode>();
            int removed = 0;
            for (int i = 0; i < arr.Items.Count; i++)
            {
                JObj ev = arr.Items[i] as JObj;
                if (ev == null) { keep.Add(arr.Items[i]); continue; }
                string typeLabel = "?";
                JNode t = ev.Get("eventType");
                if (t != null) { string s = JNodeToMatchString(t); if (s != null) typeLabel = s; }
                bool hit = true;
                foreach (string kw in keywords)
                    if (!AnyStringFieldHit(ev, kw, exact, cmp)) { hit = false; break; }
                if (hit)
                {
                    removed++;
                    string kk = "decorations/" + typeLabel;
                    if (removedByType.ContainsKey(kk)) removedByType[kk]++;
                    else removedByType[kk] = 1;
                }
                else keep.Add(arr.Items[i]);
            }
            arr.Items = keep;
            return removed;
        }

        private static bool Match(string value, string keyword, bool exact, StringComparison cmp)
        {
            if (value == null) return false;
            return exact ? value.Equals(keyword, cmp) : value.IndexOf(keyword, cmp) >= 0;
        }

        // 事件类型基准清单：ADOFAI编写标准.md §5.2 官方 2.8.0 全集（43 项，含 Neo Cosmos DLC 9 项）
        // + §6 decorations 专属 Add* 4 项。语料实测确认：43 项中 6 项 DLC 事件本地零样本
        // （SetHoldSound/MultiPlanet/FreeRoam/FreeRoamTwirl/FreeRoamRemove/ScaleMargin），其余均有实例。
        public sealed class EventGroup
        {
            public string Name;
            public string[] Types;
        }

        public static readonly EventGroup[] EventGroups = new EventGroup[]
        {
            new EventGroup { Name = "玩法", Types = new string[] { "SetSpeed", "Twirl", "Checkpoint", "SetHitsound", "PlaySound", "SetPlanetRotation", "Pause", "AutoPlayTiles", "ScalePlanets" } },
            new EventGroup { Name = "轨道", Types = new string[] { "ColorTrack", "AnimateTrack", "RecolorTrack", "MoveTrack", "PositionTrack" } },
            new EventGroup { Name = "装饰控制", Types = new string[] { "MoveDecorations", "SetText", "SetObject", "SetParticle", "EmitParticle" } },
            new EventGroup { Name = "视觉特效", Types = new string[] { "CustomBackground", "Flash", "MoveCamera", "SetFilter", "SetFilterAdvanced", "HallOfMirrors", "ShakeScreen", "Bloom", "ScreenTile", "ScreenScroll", "SetFrameRate" } },
            new EventGroup { Name = "事件控制", Types = new string[] { "RepeatEvents", "SetConditionalEvents" } },
            new EventGroup { Name = "便利", Types = new string[] { "EditorComment", "Bookmark" } },
            new EventGroup { Name = "Neo Cosmos DLC", Types = new string[] { "Hold", "SetHoldSound", "MultiPlanet", "FreeRoam", "FreeRoamTwirl", "FreeRoamRemove", "Hide", "ScaleMargin", "ScaleRadius" } },
            new EventGroup { Name = "装饰事件（decorations 数组）", Types = new string[] { "AddDecoration", "AddText", "AddObject", "AddParticle" } }
        };

        // actions+decorations 事件类型计数（GUI 类型多选列表用），按次数降序
        public static List<KeyValuePair<string, int>> GetTypeCounts(LoadedLevel L)
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            JObj top = L.Root;
            JArr actions = top.Get("actions") as JArr;
            JArr decorations = top.Get("decorations") as JArr;
            if (actions != null)
                foreach (JNode n in actions.Items)
                {
                    JObj e = n as JObj; if (e == null) continue;
                    JStr t = e.Get("eventType") as JStr;
                    if (t != null) Inc(d, t.V);
                }
            if (decorations != null)
                foreach (JNode n in decorations.Items)
                {
                    JObj e = n as JObj; if (e == null) continue;
                    JStr t = e.Get("eventType") as JStr;
                    if (t != null) Inc(d, t.V);
                }
            List<KeyValuePair<string, int>> l = new List<KeyValuePair<string, int>>(d);
            l.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            { int c = b.Value - a.Value; return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key); });
            return l;
        }

        // ---- 统计（--list / GUI 查看事件统计） ----
        public static string GetStatsText(LoadedLevel L)
        {
            Dictionary<string, int> types = new Dictionary<string, int>();
            Dictionary<string, int> decoTypes = new Dictionary<string, int>();
            Dictionary<string, int> tags = new Dictionary<string, int>();
            JObj top = L.Root;
            JArr actions = top.Get("actions") as JArr;
            JArr decorations = top.Get("decorations") as JArr;
            if (actions != null)
                foreach (JNode n in actions.Items)
                {
                    JObj e = n as JObj; if (e == null) continue;
                    JStr t = e.Get("eventType") as JStr;
                    if (t != null) Inc(types, t.V);
                    JStr g = e.Get("eventTag") as JStr;
                    if (g != null && g.V.Length > 0) Inc(tags, g.V);
                }
            if (decorations != null)
                foreach (JNode n in decorations.Items)
                {
                    JObj e = n as JObj; if (e == null) continue;
                    JStr t = e.Get("eventType") as JStr;
                    if (t != null) Inc(decoTypes, t.V);
                    JStr g = e.Get("eventTag") as JStr;
                    if (g != null && g.V.Length > 0) Inc(tags, g.V);
                }
            JArr ang = top.Get("angleData") as JArr;
            JStr pd = top.Get("pathData") as JStr;
            StringBuilder sb = new StringBuilder();
            sb.Append("谱面: tiles ").Append(ang != null ? ang.Items.Count.ToString(CultureInfo.InvariantCulture) : (pd != null ? pd.V.Length + " (pathData)" : "?"));
            sb.Append("\r\n--- actions 事件类型 ---\r\n");
            AppendCounts(sb, types);
            sb.Append("--- decorations 事件类型 ---\r\n");
            AppendCounts(sb, decoTypes);
            sb.Append("--- eventTag（非空，前 40） ---\r\n");
            List<KeyValuePair<string, int>> tl = new List<KeyValuePair<string, int>>(tags);
            tl.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            { int c = b.Value - a.Value; return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key); });
            for (int i = 0; i < tl.Count && i < 40; i++)
                sb.Append(string.Format(CultureInfo.InvariantCulture, "  {0,6}  {1}", tl[i].Value, tl[i].Key)).Append("\r\n");
            if (tl.Count == 0) sb.Append("  （无）\r\n");
            return sb.ToString();
        }

        private static void Inc(Dictionary<string, int> d, string k)
        {
            if (d.ContainsKey(k)) d[k]++; else d[k] = 1;
        }

        private static void AppendCounts(StringBuilder sb, Dictionary<string, int> d)
        {
            List<KeyValuePair<string, int>> l = new List<KeyValuePair<string, int>>(d);
            l.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value - a.Value; });
            for (int i = 0; i < l.Count; i++)
                sb.Append(string.Format(CultureInfo.InvariantCulture, "  {0,6}  {1}", l[i].Value, l[i].Key)).Append("\r\n");
            if (l.Count == 0) sb.Append("  （无）\r\n");
        }

        // ---- 序列化输出 ----
        public static SerStyle StyleFor(LoadedLevel L, string eol, string indent)
        {
            SerStyle st = Ser.Detect(L.RawText);
            if (eol == "crlf") st.NewLine = "\r\n";
            else if (eol == "lf") st.NewLine = "\n";
            if (indent == "tab") { st.IndentUnit = "\t"; st.Compact = false; }
            else if (indent == "2") { st.IndentUnit = "  "; st.Compact = false; }
            else if (indent == "4") { st.IndentUnit = "    "; st.Compact = false; }
            else if (indent == "compact") st.Compact = true;
            return st;
        }

        public static string Serialize(LoadedLevel L, string eol, string indent)
        {
            SerStyle st = StyleFor(L, eol, indent);
            StringBuilder sb = new StringBuilder();
            Ser.Write(L.Root, sb, st, 0);
            return sb.ToString();
        }

        /// 生成默认/规范化输出名：单一 .adofai 后缀（去除连续点号与非法字符）。
        /// userText 为空时用 "<原名> - converted"；否则把用户输入规范成安全文件名。
        public static string MakeOutputName(string inputPath, string userText)
        {
            string dir = Path.GetDirectoryName(inputPath);
            string stem = Path.GetFileNameWithoutExtension(inputPath).Trim().TrimEnd('.').Trim();
            if (stem.Length == 0) stem = "level";

            string name;
            if (string.IsNullOrWhiteSpace(userText))
            {
                name = stem + " - converted";
            }
            else
            {
                // 清理非法字符与引号
                StringBuilder sb = new StringBuilder();
                char[] invalid = Path.GetInvalidFileNameChars();
                foreach (char c in userText.Trim())
                {
                    bool bad = false;
                    for (int i = 0; i < invalid.Length; i++) if (invalid[i] == c) { bad = true; break; }
                    sb.Append(bad ? '_' : c);
                }
                name = sb.ToString().Trim().Trim('"').Trim();
                while (name.EndsWith(".adofai", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 7).TrimEnd();
                if (name.Length == 0) name = stem + " - converted";
            }
            return Path.Combine(dir, name + ".adofai");
        }

        public static void WriteOutput(string path, string text, bool withBom)
        {
            File.WriteAllText(path, text, new UTF8Encoding(withBom));
        }
    }
}

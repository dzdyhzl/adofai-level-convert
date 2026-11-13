# level convert — ADOFAI 关卡转换工具（GUI + 命令行 + 安装包）

输入一个 `.adofai` 关卡，按要求改动后输出新的 `.adofai` 文件。三大功能：

1. **旧版本 → 新版本**（升级到 v19）
2. **删除事件**（仅 `actions`：行同时包含全部关键词 且 类型在勾选集合）
3. **删除装饰**（仅 `decorations`：行同时包含全部关键词，独立功能）

三者可同时使用，也可只做其一。

## 图形界面版（推荐，无需命令行）

双击运行 `level convert GUI.exe`。

- **界面自适应**：DPI 感知 + 全字体度量布局——所有控件宽高按实际渲染字体测量，
  100%/125%/150% 等任何系统缩放下文字都不裁剪、不重叠；基础画布 1000×940 逻辑像素，
  行距加宽，窗口越大行间距与类型面板/日志越大；可任意缩放/最大化，**F11 无边框全屏**。

### 操作流程

1. **选择文件**：点"浏览..."或把 `.adofai` 拖进窗口；状态行显示版本/块数/actions/decorations/解析情况
2. **勾选更改要求**：
   - **升级到新版本（v19）**：可选"补齐缺失的 v19 核心设置键"、"pathData → angleData 转换"、
     "保留原 pathData 字符串"
   - **删除事件**：行需**同时包含全部事件关键词**，且 eventType 属于勾选集合
     （只填关键词=按关键词删；只勾类型=按类型删）
   - **删除装饰（独立功能）**：行需**同时包含全部装饰关键词**的 decorations 全部删除，
     与类型面板无关
3. **开始转换**：输出到**谱面所在文件夹**（默认名 `<原名> - converted.adofai`，单一后缀；
   输入框内容自动规范化：清理非法字符、去重复 .adofai 后缀；同名先询问；可勾"直接覆盖原文件"）
4. "预览改动"只报告不写文件；"打开所在文件夹"直达输出目录；"查看事件统计"显示
   actions/decorations 的类型与 eventTag 完整分布

### 多关键词（删除事件 / 删除装饰各一套，互不相干）

- 在关键词框输入 → 点**【添加】**（或按回车）加入列表；重复自动去重；双击列表项或点**【移除】**删除
- 匹配要求：**事件/装饰行必须同时包含列表里的全部关键词**（AND）
- 每个关键词独立做"行内任一字符串字段深度包含"判定（含嵌套对象/数组里的字符串；
  数字/布尔不转文本参与，防"10"误删 `floor=10`）；可勾"精确匹配 / 区分大小写"收紧
- 两个功能各有**独立的关键词列表与精确/大小写设置**，互不影响

### 事件类型面板

- 始终显示编写标准事件全集（43 官方类型 + decorations 4 种 Add*），按**大类分段换行**：
  玩法 / 轨道 / 装饰控制 / 视觉特效 / 事件控制 / 便利 / Neo Cosmos DLC / 装饰事件
- 每段大类名前有同款样式的**大类全选框**（只影响本段）；顶部"全选/清空"作用于全部类型
- 加载谱面后每个类型附**实测数量**；谱面中不在标准清单的类型自动归入"其他"段

## 命令行版用法

```
level convert.exe <输入.adofai> [操作与选项]
```

### 功能一：版本升级

| 选项 | 说明 |
| --- | --- |
| `--upgrade` | 升级到 v19：`settings.version=19`、补齐缺失的 v19 核心 78 键、`pathData`→`angleData`、`"Enabled"/"Disabled"` 字符串开关→`true/false` |
| `--minimal` | 升级时不补缺失键（仅版本号 / 本体转换 / 布尔归一） |
| `--no-pathdata` | 升级时不转换 pathData |
| `--keep-pathdata` | 转换后保留原 pathData 字符串（默认删除，两种本体并存不推荐） |

### 功能二：删除事件（仅 actions）

行须**同时包含全部关键词**，且 eventType 属于所选类型集合；只给其一时按单项。

| 选项 | 说明 |
| --- | --- |
| `--remove <关键词>` | 单关键词；默认匹配 eventType 或 eventTag（包含式） |
| `--remove-keywords <a,b,c>` | **多关键词**（逗号分隔），行须同时包含全部才删除 |
| `--remove-type <关键词>` | 仅按 eventType 匹配（关键词包含式） |
| `--remove-types <列表>` | 按事件类型精确多选删除，逗号分隔，如 `"Twirl,Bookmark"` |
| `--remove-tag <关键词>` | 仅按 eventTag 匹配 |
| `--field <字段名>` | 仅按指定字段匹配（标量值含数字） |
| `--any` | 任一字符串字段含关键词即删除（同 GUI 条件1） |
| `--or` | 关键词与类型同时给出时改为"满足其一（或）"（仅脚本；GUI 固定为"且"） |
| `--exact` | 精确相等匹配（默认包含匹配） |
| `--case-sensitive` | 区分大小写（默认不区分） |

### 功能三：删除装饰（仅 decorations，独立）

| 选项 | 说明 |
| --- | --- |
| `--remove-decorations <关键词>` | 删除行含关键词的所有装饰（别名 `--remove-decor`）；多关键词时逗号分隔写在引号内 |
| `--exact` / `--case-sensitive` | 同上，作用于装饰关键词 |

`--decorations-only` + `--remove` 自动等价于删除装饰（旧脚本兼容，沿用 `--remove` 关键词）；
`--actions-only` 抑制装饰删除。注意 `--remove` 现仅作用于 actions——要删装饰请用 `--remove-decorations`。

### 通用选项

`--list`（事件类型/eventTag 分布）· `--dry-run` · `-o/--out <文件>`（默认 `<输入名> - converted.adofai`）·
`--in-place` · `--eol <crlf|lf|auto>` · `--indent <2|4|tab|compact|auto>` · `--keep-bom / --no-bom` · `-h/--help`

### 示例

```bat
rem 旧谱一键升 v19
level convert.exe "老谱.adofai" --upgrade

rem 删除 eventTag 含 re_light 的事件，先预览
level convert.exe "FallenEra.adofai" --remove-tag re_light --dry-run
level convert.exe "FallenEra.adofai" --remove-tag re_light --in-place

rem 多关键词：行须同时含 Twirl 与 re_light 才删
level convert.exe "某谱.adofai" --remove-keywords "Twirl,re_light" -o "成品.adofai"

rem 删除行含 re_light 的所有装饰（不动 actions）
level convert.exe "某谱.adofai" --remove-decorations re_light

rem 看看谱里有哪些事件类型和标签可选
level convert.exe "某谱.adofai" --list
```

## 行为细节

- **输入容错**：严格 JSON → 尾逗号/字符串内裸控制字符 → 顶层缺失逗号逐级修复
  （该管线对 levels 语料 157 个真实谱面 100% 可解析，含 7 个结构性损坏文件）。
- **输出保真**：保留未知字段与键序；BOM/换行/缩进默认跟随输入（官方编辑器风格：
  旧版 Tab 缩进、新版 2 空格；BOM 有；纯数字数组如 angleData 保持单行）。
- **删除匹配**：关键词判定 = 行内任一字符串字段深度包含（嵌套也查；仅字符串值，
  数字/布尔不参与）；多关键词 AND；命中项剔除、未命中项保序；settings/angleData 一概不动；
  旧谱混入 actions 的 Add* 事件仍可按类型删除（兼容）。
- **升级补键默认值**：取自语料 61 个 v19 谱面实测众数（键序取自规范 v19 文件）；
  `disableV15Features` 对 v15 以下旧谱默认 `true`，v15+ 默认 `false`；身份类键
  （artist/song 等）不会覆盖已有值，缺失时补空串。
- **pathData 解码表**（`--upgrade` 用）：
  - 24 字母绝对角：R=0° 起每 15° 一格，`!`=999（中旋）
  - 相对偏移（结果 = 前块角 + 偏移）：`5:+72 6:-72 7:+52 8:-52 9:-30 h:+120 j:-120 t:+60 y:+300`
  - 来源：ADOFAI-JS `src/pathdata/index.ts`（github.com/adofaiex/ADOFAI-JS），
    与 CrackThrough/adofai-angle-calculator 的 `900/7`、`1620/7` 旋转角公式互相印证
  - 未知字符：保持前块角并计数警告
  - **可覆盖**：exe 同目录放 `pathdata-chars.json`（`{"angles":{...},"offsets":{...}}`）
    即整体替换内置表；删除该文件即恢复内置表。

## 构建与源码

| 文件 | 说明 |
| --- | --- |
| `Core.cs` | 转换核心（容错解析/序列化/升级/删除事件/删除装饰），CLI 与 GUI 共用，零外部依赖（.NET Framework 4.x） |
| `Cli.cs` | 命令行前端（`level convert.exe`） |
| `Gui.cs` | 图形界面（WinForms，`level convert GUI.exe`；DPI 感知字体度量布局引擎、多关键词、大类面板） |
| `Installer.cs` | 单文件安装包源码（内嵌 GUI/CLI/README） |
| `AGENTS.md` | DSH 工作区默认规则（自动加载）：项目更改须同步更新 README，检测到 GitHub 远程则执行 commit+push |
| `Audit.cs` | 界面布局审计（DPI 感知、多尺寸重叠/裁剪自动检查，开发辅助） |
| `build.cmd` | 一键重编译两个 exe + 打安装包（调用 Windows 自带 `csc.exe`） |

```bat
build.cmd
```

## 验证记录（对 G:\adofai\levels 语料实测）

- v11 pathData 谱（come on，2447 块）：angleData 逐字符与字母表一致
- v19 含 `!`×4、`8`×7 谱（U SAiD）：`!`→999 ✓；`8`×7 = -52×7 ≈ 整圈（七边形）✓
- v12 字符串开关谱（Amulet，9 个）：`"Enabled"→true / "Disabled"→false` ✓
- v3 / v4 谱：2590/1800/1917 块 pathData 全部转出等长 angleData，settings 56→79 键 ✓
- 损坏谱（1450(Nerfed)，顶层缺逗号）：自动修复后升级，1934 块 / 1071 actions 精确 ✓
- 15000 块 v19 谱（Ludicin）：升级后 angleData/actions/decorations 与原文件逐项一致（无损）✓
- 事件删除：FallenEra `re_light`×24、S.A.T.E.L.L.I.T.E EditorComment×6、Bookmark(--exact)×7、
  类型多选 `Twirl,Bookmark`（417+7 全删、其余零改动）✓
- **拆分语义 9 组**：删除事件不动 decorations、且语义、删除装饰（含嵌套命中/精确匹配/
  数值不参与）、事件+装饰同时、旧谱 actions 内 Add* 兼容、独立关键词互不串扰 ✓
- **多关键词 8 组**：双/三关键词 AND、无一全含→零删除、跨字段 AND、装饰 AND、中文关键词、
  单字符串入口兼容 ✓
- **GUI 端到端**（反射驱动真实窗体）：且语义采集、独立关键词 3 场景、多关键词添加/去重/
  移除/删除链路、类型面板 8 大类分段与分组全选 ✓
- **布局审计**（DPI 感知，150% 缩放，4 种窗口尺寸 1500×1410 / 2100×1500 / 2880×1560 / 最小）：重叠与裁剪 0 ✓
- 所有输出均为严格 JSON；`--dry-run` 不落盘；错误路径 exit 1 并给中文提示

## 分发与安装

- **免安装直接使用**：.NET Framework 4.x 零依赖单文件，Windows 10/11 自带运行时——把
  `level convert GUI.exe` 单个文件发给别人即可直接运行（`level convert.exe` 为可选命令行版）。
  Win7/8 需先装一次 [微软官方 .NET Framework 4.8 运行时](https://dotnet.microsoft.com/download/dotnet-framework/net48)。
- **安装包**：`release\level convert 安装包.exe`（GUI/CLI/README 已内嵌）。双击 → 点"立即安装"
  → 在**安装包同级**创建 `level convert by dzdyhzl` 文件夹并写入 3 个文件；重复安装即升级（覆盖）。
- **GitHub Release 下载**：<https://github.com/dzdyhzl/adofai-level-convert/releases/latest>
  （资产：安装包 `level-convert-by-dzdyhzl-setup.exe`、免安装 `level-convert-gui.exe`、命令行 `level-convert-cli.exe`）
- 安装包由 `build.cmd` 自动生成，或手动：
  `csc /target:winexe /out:"release\level convert 安装包.exe" Installer.cs /res:"level convert GUI.exe",LevelConvertGui /res:"level convert.exe",LevelConvertCli /res:"README.md",ReadmeMd`

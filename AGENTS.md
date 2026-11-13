# AGENTS.md — level convert 工作区默认规则（DSH 自动加载）

本文件由 DSH 注入每个在本工作区工作的 agent 上下文。以下规则为默认要求：

## 1. 文档同步（强制）

每次对项目做出更改后，**必须同步更新 README.md**，使其与当前实现一致：

- 功能新增/删除、选项语义变化 → 更新"三大功能"总览与对应小节（GUI 流程 / CLI 选项表）
- 行为细节变化（匹配规则、默认值、输出格式）→ 更新"行为细节"
- 新增/调整测试或构建步骤 → 更新"构建与源码"与"验证记录"
- 更新后自查：章节结构完整、示例命令可运行、路径写法正确（如 `G:\adofai\levels`）

## 2. Git 推送（检测到远程则执行）

- 项目根有 `.git`，先检测远程：`git remote get-url origin`
- **存在远程（当前为 https://github.com/dzdyhzl/adofai-level-convert，main 分支）时**，
  每轮有效更改（含 README 更新）完成后依次执行：
  ```
  git add -A
  git commit -m "<中文摘要>"
  git push
  ```
  凭据已存于 Windows 凭据管理器（GCM），可直接推送

## 2b. 发布同步（安装包 + GitHub Release，强制）

每次更改代码并重新构建后，**安装包与 Release 资产必须同步更新**：

1. 跑 `build.cmd` 重建 3 个产物：`level convert GUI.exe`、`level convert.exe`、
   `release\level convert 安装包.exe`
2. **更新 GitHub Release 资产**（当前 release：tag `v1.0.0`，id 见
   `GET /repos/dzdyhzl/adofai-level-convert/releases/tags/v1.0.0`；功能级大版本可另建新 tag）：
   - 取凭据：`git credential fill`（stdin: `protocol=https\nhost=github.com`）→ password 即 token，
     **不得打印或写进任何文件**
   - 删除旧资产：`DELETE /repos/.../releases/assets/{id}`
   - 上传新资产：`POST https://uploads.github.com/repos/.../releases/{id}/assets?name=<名>`
     （body = 文件二进制，Content-Type: application/octet-stream）
   - 资产名**只用 ASCII 连字符**（GitHub 会剥中文字符、空格转点号）：
     `level-convert-by-dzdyhzl-setup.exe`、`level-convert-gui.exe`、`level-convert-cli.exe`
   - 正文同步：`PATCH /repos/.../releases/{id}` 更新 body
3. 上传后 `GET` 校验资产数量与字节数与本地一致
- **没有远程**时跳过推送，只做本地提交
- 推送失败（认证/网络）如实报告，最多重试 2 次
- 临时测试产物（`_` 前缀文件）不要提交（.gitignore 已覆盖）

## 3. 构建与验证约定

- `build.cmd` 一键构建两个 exe + 安装包（csc，零依赖）
- 功能改动后必须跑 `build.cmd`；删除逻辑改动时同步维护逐事件断言测试
- 回归基线：4 组 CLI 输出哈希逐字节一致（见 README 验证记录）

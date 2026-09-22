# 交接文档 — Monica Desktop (Avalonia) 商业化迭代

> 面向对象：接手继续开发的 AI。请冷启动阅读，假定你没有任何上文记忆。
> 最后更新：2026-09-22

## 0. 总目标（未达成，勿提前收工）

**"继续迭代，直到达到项目商业级可用水准。"** 这是一个持续性目标，不是单个任务。每一轮都要朝这个终态推进，不要因为某一步变绿就宣布完成。

## 1. 仓库与来源关系

- **本仓库**：`Monica-by-Avalonia`（桌面端，.NET 10 + Avalonia + FluentAvalonia）。
  路径：`C:\Users\joyins\Desktop\Monica-all\Monica-by-Avalonia\monica by avalonia`
- **只读事实来源**：`Monica for Android`（Kotlin）。桌面端从 Android 派生——**适配桌面去贴齐 Android 的数据形状，绝不反向改 Android**。
- 产品是一个**密码管理器**：核心安全边界是"绝不让明文密码落到截图 / 日志 / argv / 可分享产物里"。

## 2. 当前状态（工作树干净）

分支 `main`，`git status` 无未提交改动。功能 HEAD = `ffdac87`（其后只可能有给本节自身标提交号的文档提交），近几轮：

| commit | 立住了什么 |
|---|---|
| `4ee20e6` | 库头部插槽内容真正落地（`WorkspaceHeader.Has*Content` 改为注册的 DirectProperty），一次翻正 10 条 UI 红；并停止离屏页面继续跑仓库元数据搜索 |
| `c962fb8` | 上一步引入的 `dotnet format` 空白违规 |
| `22f018c` | 内存门改为"先压缩再采样"，消除锁定态私有字节采样的竞态假红（阈值仍 120MB，产品运行时未动） |
| `4e91326` | 打包脚本断言产物真实存在（`$ErrorActionPreference` 管不了原生命令退出码，ISCC/tar 失败原本会让 CI 变绿且零产物）；Inno 改用 `x64compatible` |
| `39f725a` | 逐屏走查（#77）：10 个引用了但两张表都没有的 key、3 个只作为裸参数传给失败上报辅助函数的 KeePass key、密码强度文案尾随标点、SecurityAnalysis 空态重复提示、回收站等四屏的裸 `Clear` 标签；`LocalizationParityTests` 加第 4 道引用守卫；新增首条锁屏错误横幅渲染测试 |
| `7acef4a` | 状态消息按语义分类：`StatusMessage` 取消 setter，原先散在 65 个文件的 229 处属性直写全部收进唯一漏斗 `MainWindowViewModel.StatusMessaging.cs`（`SetStatusMessage`/`SetStatusFailure`/`ClearStatusMessage`），顶部琥珀横幅改由**产生处声明的意图**决定，不再回读已翻译文案猜关键词 |
| `b54a040` | 界面信息密度与命名瑕疵：先用一次性探针在真渲染树上量出重复（13 屏 × 可见文本 / 同命令按钮 / 左栏裸路径扫描），再逐条删 12 处——左栏 `%TEMP%\…` 绝对路径改 tooltip、SQLite 来源行"本地路径"栏里塞的其实是一句说明、四屏左栏标题逐字重印页面标题、Generator 结果卡重印头部策略摘要、Timeline / Mdbx / DatabaseManagement 三处"同一命令实例 + 同一标签"的第二个按钮、Settings 侧栏标题重印应用导航当前项、Mdbx 检查器重印左栏空态句（改用已存在的 `SelectItemToInspectHint`）、SecurityAnalysis 唯一"空查询仍显示禁用清除键"；新增 `WorkspaceDensityGuardUiTests` 四条守卫，植入三类假缺陷实测三条同时点名转红（守卫 1 上线即在 Settings 抓到真实重字）；门禁：单测 9+713、UI 17+183 全绿，重新 publish 后产物门 loadMs=525、锁定私有 119.3MB（预算 120，余量仅 0.7MB）、KeePass 20000 条增长 4.7MB |
| `ffdac87` | ① 瞬态回执自己退场：180 处信息写入按 key 逐条判成三种寿命（122 notice／58 standing／52 failure），notice 落地 8 秒后清除、离开产生它的那一屏立即清除，判据是产生处的动作语义而不是文案关键词；计时器按仓库既有的自动锁定范式放在窗口侧（`MainWindow.StatusNotice.cs`），VM 只声明意图 + 可注入 `TimeProvider`，换屏路径不依赖计时器。新守卫 `StatusNoticeRetirementUiTests` 四条（假时钟、零 sleep），四类假缺陷 A/B/C/D 分别把 1、3／3／1、2／4 打红，撤销后 4/4 绿；真产物再加 `--smoke-ui-status-notice` 探针补上无头测不到的 `DispatcherTimer` tick，把驻留改成 60 秒后产物里 `retired=False`、门即红。② 顺带查清锁定态内存门为什么反复假红：同一二进制八次跑出 116.7–131.0，实测锁定后约 30 秒私有字节仍在从 ~123 衰减到 ~114（线程 37→31、托管堆一直 25–28MB），旧门取的是衰减途中的瞬时值；改为每 5 秒压缩后采样共 10 拍、按尾 5 拍中位数判定（阈值仍是 120，未放松），在压缩处根住 15MB 后中位数 133.4、门红，撤销后两次绿 113.9／115.1。门禁：格式 0 改动、Release 0 warning、单测 9+713、UI 17+187 全绿，重新 publish 后产物门 loadMs=597、KeePass 20000 条增长 3.9MB、锁定尾窗中位 113.9MB |

§3 的 use-case 抽取改动已提交（`24d92b0`），OneDrive/WebDAV 冲突副本复用修复已提交（`957c5af`）。

## 3. Task #27（ViewModel 抽 use-case）状态 — 已可判定完成

四子阶段全部处理完毕：

| 子阶段 | 结论 |
|---|---|
| IncExport | 完成：`MonicaJsonExportUseCase` + `MonicaJsonImportUseCase`，VM 侧 `MainWindowViewModel.ImportExportBuild.cs` 惰性构造并委派 |
| Settings | 完成：3 个 use case（见下）已建并接入 VM |
| Mdbx | **分析后判定无需提取**：services 层（`IMdbxVaultService`/`IWebDavBackupService`/`IOneDriveBackupService`）已足够薄，VM 做的是正当编排 + 状态管理，强行抽取只会得到薄包装 |
| Sync | 同上，无需提取 |

已创建的 Settings use case（均在 `Services/VaultOperations/`）：

- `ChangeMasterPasswordUseCase` — 包 `IMasterPasswordMaintenanceService.ChangeMasterPasswordAsync`，返回 `ChangeMasterPasswordResult`（`Success`/`IsCurrentPasswordIncorrect`/`FailureMessage`）。
- `ResetMasterPasswordUseCase` — PBKDF2 答案校验（`Task.Run` 后台化）+ `ResetMasterPasswordFromUnlockedVaultAsync`，返回 `ResetMasterPasswordResult`。
- `SaveSecurityQuestionsUseCase` — `SecurityQuestionService.CreateSetup` 后台化。

命名空间要点（踩过坑）：
- `IMasterPasswordMaintenanceService`、`MasterPasswordMaintenanceFailureReason` 在 **`Monica.Data.Services`**。
- `SecurityQuestionService`、`SecurityQuestionDraft`、`SecurityRecoverySettings` 在 **`Monica.Core.Services` / `Monica.Core.Models`**。
- Result record 里属性名不能和静态工厂方法同名（`Success` 属性 vs `Success()` 方法会 CS0102）——工厂方法用 `Succeeded/Failed/IncorrectPassword/AnswersIncorrect`。

## 4. Git / 提交规范（用户明确要求）

- **直接 commit 到 main。不建 feature 分支、不建 PR**，除非用户显式要求。
- **绝不 `git push` / 加 remote**，除非用户开口。提交后把结果展示给用户即可。
- 偏好一次性打包提交（one bundled commit）。
- 本次会话前已清理：Monica-by-Avalonia 11 个 Dependabot PR 已关闭；Monica(Android) 21 个本地 codex 分支 + 4 个远端分支已删除。保持仓库只有 main。

## 5. 构建与测试命令

```bash
cd "C:\Users\joyins\Desktop\Monica-all\Monica-by-Avalonia\monica by avalonia"
dotnet build src/Monica.App/Monica.App.csproj --no-restore      # 期望 0 error 0 warning
dotnet test tests/Monica.Tests/Monica.Tests.csproj --no-restore # 约 8-9 分钟
```

最近实测（`4e91326`）：

```bash
# 源码级商业门（不启动产物：文件行数/格式化/NuGet 漏洞/Release --warnaserror/两套测试）
./eng/ci/verify-commercial-release.ps1 -Configuration Release     # 全绿，退出 0
# 产物级真跑门（先 publish 再验，报内存/加载数字前必须重跑 publish，否则测的是旧二进制）
./eng/ci/publish-desktop.ps1 -Project src/Monica.App/Monica.App.csproj -RuntimeIdentifier win-x64 `
  -Mode jit -Version 0.1.0-ci.0 -VersionPrefix 0.1.0 -PackageVersion 0.1.0-ci.0 `
  -AssemblyVersion 0.1.0.0 -FileVersion 0.1.0.0 -InformationalVersion 0.1.0-ci.0   # 六个版本参数都是 Mandatory
# 产物落 artifacts/publish/win-x64/jit（脚本会先清空该目录，并顺带 release 构建 crates/monica-crypto）
./eng/ci/verify-artifact-runtime.ps1 -PublishDirectory <dir> -RuntimeIdentifier win-x64 -Mode jit
```

实测数字（本轮 #78 后）：单测 9 perf + 713 functional、UI 17 perf + 179 functional；重新 publish 后
产物门 loadMs=191/642（预算 4000）、锁定态私有字节 112.4MB（预算 120）、KeePass 20000 条增长
3.6MB（预算 24），`release gate completed success=True`。上一次记录（`4e91326`）为单测 716、UI 193、
loadMs=355、113.2MB、5.9MB。

Windows 发布链路（本轮已端到端验过，见 §7）：

```bash
./eng/package/pack-portable.ps1        -InputDirectory <publish> -OutputDirectory artifacts/package -PackageName "Monica-<ver>-win-x64-jit"
./eng/package/package-windows-inno.ps1 -PublishDirectory <publish> -OutputDirectory artifacts/package -Version "<ver>" -Mode jit
```

墙钟类断言历史上确有负载抖动（Task #53 一类）；但锁定态内存采样那个"超预算"已被根因定位为
harness 竞态并修好（锁后 1s 才做的压缩要先落地再采样），不是产品回归，也不要靠调阈值变绿。

## 6. 运行应用（给用户演示时）

```bash
dotnet run --project src/Monica.App/Monica.App.csproj --no-build
```
- 应用数据目录：`%LOCALAPPDATA%\Monica by Avalonia\`（已有 `monica.db` + `mdbx/local.mdbx`，是一个真实测试库）。
- 应用打开后停在**解锁页**，需要用户输入主密码才能进入。
- **主密码不可恢复**：`settings.json` 不存明文，只存派生哈希。我无法"给出密码"。若用户忘记密码，只能清空该测试库另建新库（破坏性，动手前先问）。
- **安全红线**：解锁后**不要截屏已解密的保险库界面**，不要把条目明文写进任何输出/日志。演示 UI 时保持锁定态或空态。
- 注：历史运行可能已在 `%LOCALAPPDATA%\Monica by Avalonia\mdbx\` 下留下 `onedrive-*.local-conflict-*.mdbx` 冲突副本；冲突恢复路径现已复用同一工作副本的既有备份，后续重试不会继续无限增长。
- **空态截图矩阵（逐屏走查用的安全姿势）**：在一个全新的 `MONICA_APPDATA_DIR` 里
  `--init-empty-smoke-vault <一次性口令>`，再
  `--smoke-ui-unlock-env <口令所在环境变量名> --smoke-ui-other-pages-checks --smoke-ui-screenshot-dir <目录> --smoke-ui-exit-after-checks`
  → 13 帧 `<Section>_1280x800.png`，全程不碰真实库。
  - 口令走环境变量而不是 argv；`--smoke-ui-screenshot-dir` 的值**必须整体加引号**，
    因为仓库路径含空格，`cygpath -m $PWD/...` 不加引号会被截断成不存在的目录（实测 0 帧）。
  - 空库是故意的：就绪检查要求 `Passwords.Count > 0`，所以进程**退出码 1 是预期**，13 帧照样落盘。
  - **约束**：Generator 那一帧必然含一个实时生成的口令。它是空态一次性库里的产物、且 `artifacts/`
    已被 gitignore 提交不进去，但这意味着**矩阵只能在一次性库上跑**，绝不能在真实库目录上跑。

## 7. 剩余阻塞项 / 可推进方向（朝"商业级"）

- **Windows 发布链路本轮已实测**（对象是"用户下载到的东西"，不再是构建目录）：
  - `pack-portable` 出 383.1MB zip → 解到 `…\Temp\Monica Pkg Review\win-x64 jit`（路径含空格）→
    文件 315/315 对齐，原生库与 `Assets\AppIcon.ico` 齐全；
  - 对该解包目录跑完整 `verify-artifact-runtime`：`CANONICAL VAULT passed`、UI 门
    loadMs=355/4000、锁定态 113.2MB/120、KeePass 增长 5.9MB/24、`RUNTIME SMOKE passed`；
  - cwd 独立性：以 `cwd=C:\Windows` 启动解包产物，建库/播种/回读全部退出 0
    （27 passwords、14 notes、6 categories、6 attachment owners）；
  - 安装目录自写文件：**整个窗口化会话在安装目录里新增 0 个文件**，说明装到
    `Program Files`（标准用户只读）不需要写自己旁边；
  - Inno：92.5MB setup，编译日志 315 条 `Compressing` 与发布目录 315 文件一一对账，
    `mdbx_ffi.dll`/`monica_crypto.dll`/`Monica.App.exe`/`AppIcon.ico` 均在载荷内，改 `x64compatible` 后无告警。
- **未做的（要说清）**：没有真的执行一次安装。setup 默认 `PrivilegesRequired=admin`，装进
  `Program Files` 需要用户点 UAC；`.iss` 未开 `PrivilegesRequiredOverridesAllowed`，所以
  `/CURRENTUSER` 静默装不了。是否给标准用户留一条免提权安装路径，是一个产品决定，待用户拍。
- **两条探针死路（别再试）**：① Inno 的 `/EXTRACT` 退出 0 但零文件落地，不可信；
  ② 用 `icacls /deny *S-1-5-32-545:(OI)(CI)W` 模拟"只读安装目录"无效——deny 继承到镜像文件上，
  连 `where.exe` 都起不来（实测同样 Access is denied），它测的是加载器不是产品。
- **Task #55 linux/macOS 原生 MDBX 引擎二进制**：用户 2026-09-22 决定**先不做 Linux，把 Windows 做透**。
  实测环境约束：本机 WSL（`homoos`，内核 6.18.33.2）有 gcc 14.2 但**无 rustc/cargo**，而两个原生库都是
  Rust（`crates/monica-crypto` 纯 Rust 零 build.rs；引擎在同级 `../../mdbx/crates/mdbx-ffi`，uniffi 0.31 cdylib）；
  macOS `.dylib` 在本机根本无法产出；且 `verify-artifact-runtime` 的窗口化 UI 门本身是 win-only。
  `runtimes/{linux-x64,osx-x64,osx-arm64}` 目前只有 `.gitkeep`。
- **Task #43 AOT 产物无法解锁**：Layer 1 已修（private row DTO→internal）。Layer 2 是硬骨头——全库 87 个 Dapper 调用点大多传匿名对象，Dapper.AOT 需要具名类型；`IsAotCompatible` 下 Data/Core/Platform 分别有 IL2026 报警。**AOT 非内存手段**（框架空窗 115MB 在 jit+R2R 下实测，AOT 省不掉 Skia/原生库部分）。CI 目前对 aot `continue-on-error`，**jit 是硬门**。这是多日重构，勿轻启。
- 已完成的低风险项：
  1. §6 的 OneDrive/WebDAV 冲突副本堆积已修复：重复重试“使用远端”会复用同一 `.local-conflict-*` 备份，不再每次生成 GUID 文件。
  2. §2 的 use-case 改动已提交（`24d92b0`），冲突备份修复已提交（`957c5af`）。
- **逐屏走查已完成一轮**（13 屏空态截图 + 全量 key 审计）。根因只有一条：
  `LocalizationService.Get` 找不到 key 时**回退返回 key 本身**，所以缺翻译从不报错，只在屏幕上印出标识符。
  现在由 `LocalizationParityTests` 四道守卫钉住（双语互覆盖 / 类型化访问器 / 源码与 XAML 引用扫描 /
  裸参数形状），植入假 key 实测会红、还原即绿；两表各 1382 条，零单边 key。
- **走查第 1 项已修（#78）**：横幅判定不再回读已翻译文案。
  - 形状：`StatusMessage` **没有 setter**（get-only），写入只能走
    `MainWindowViewModel.StatusMessaging.cs` 里的 `SetStatusMessage` / `SetStatusFailure` /
    `ClearStatusMessage`；同一 partial class 内部也编译不过越界写法，所以新调用点不可能漏分类。
  - 计数（对 `39f725a` 重测）：改造前 `StatusMessage = ` 属性直写 **229** 处，散在 **65** 个文件；
    改造后漏斗外直写 **0**。
  - 语言不对称（把旧关键字规则回放到 HEAD 的 161 个 key 上实测）：双语都命中 **1** 个，
    **只在中文命中 11 个**（英文失败只留底部小灰字）：`AttachmentAddFailed`、
    `ExportAuthorizationFailed`、`ImportMarkdownFailed`、`InsertNoteImageFailed`、
    `OpenReferenceFailed`、`ReferenceCannotOpen`、`SettingsSaveFailed`、
    `VaultAccessInitializationFailed`、`VaultAccessUnlockFailed`、`VaultStorageEngineUnavailable`、
    `WrongMasterPassword`；两语言都不命中 149 个（其中含信息语，也含最该醒目的 `VaultLoadFailed`）。
    现在这 **12 个失败全部在产生处声明为 `SetStatusFailure`/`SetUnlockError`**，判定与文案、语言无关。
  - 诚实命名：`HasRecoverableStatusMessage` → `HasFailedStatusMessage`（样式类同步改名）。
    "可恢复"从来不成立——横幅上的 Refresh 治不了 `WrongMasterPassword`；正是这个名字让关键词
    猜法显得合理。
  - 植入实测（守卫必须真的会红）：① 把旧关键字判定塞回 `HasFailedStatusMessage` → 3 条新单测
    全红，且红在 **en-US**（正是原缺陷：英语失败只剩底部小灰字）；② 给 `StatusMessage` 加回
    setter → 反射守卫红；③ 删掉样式的 `BorderThickness` setter → UI 双语两红。三次还原后全绿。
  - 新增覆盖：单测 4 条（双语对称 / 锁定与加载态压制 / 成功后清除 / 无 setter），UI 1 条 theory×2
    （真实可视树里渲染出琥珀条 + 文案不是裸 key + 样式确实命中）。
  - 门禁实测：源码门全绿（格式 0 改动、Release `--warnaserror` 0 warning、单测 9 perf + 713
    functional、UI 17 perf + 179 functional）；重新 publish 后产物门 loadMs=191/642（预算 4000）、
    锁定私有字节 112.4MB（预算 120）、KeePass 20000 条增长 3.6MB（预算 24）。
  - **故意没做**：切换语言时不把当前已显示的那句重译（漏斗现在存的是 key，做得到，留作后续）；
    锁屏期间 `SetUnlockError` 写下的失败不点亮琥珀条（`HasFailedStatusMessage` 要求 `IsUnlocked`），
    由锁屏自己的横幅承载。
- **走查第 2 项已修（#80）：瞬态确认句自行退场**。#78 欠的是一个一个判断"哪句该在什么时候消失"，
  这轮真的把 180 处信息写入按 key 逐条判完，分成三种寿命：
  - **122 处 `SetStatusNotice`**——已完成动作的回执（已复制 / 已保存 / 导入了 N 条 / 测试连接成功）。
    落地 8 秒后自行退场。判据是**产生处的动作语义**，不是文案关键词（那正是 #78 刚拆掉的东西）。
  - **58 处 `SetStatusMessage`**——要么仍然成立的状态（锁定、正在同步、正在编辑某条），要么是需要用户
    接着答的提示（请输入当前主密码、附件超限、文件夹重名）。替换前一直留在屏上。
    `*PartialFailureFormat`（部分条目没导入成功）刻意归在这一类，不自动消失。
  - **52 处 `SetStatusFailure`**——不变，仍然点亮琥珀条且不自动消失。
  - 计时器放在窗口侧（`src/Monica.App/MainWindow.StatusNotice.cs`），VM 只声明意图并用可注入的
    `TimeProvider` 判到期——照 `MainWindow.Security.cs` 自动锁定已经在用的同一范式。理由不是洁癖：
    VM 自己去够 dispatcher，等于让纯 VM 单测进程假装自己有 UI 可投（第一版就是这么写的，改掉是因为
    它在没有 Avalonia 的进程里会留下没人观察的失败任务）。
  - 两条独立退场路径：到期，以及**离开产生它的那一屏**（`OnSelectedSectionChanged`）。后者不需要计时器，
    也是"预览已空但底部还挂着上一句"最直接的修法。
  - 守卫 `tests/Monica.UiTests/StatusNoticeRetirementUiTests.cs` 四条，假时钟推进、零 sleep。
    植假缺陷证明它会红：A 到期不清 → 1、3 红；B 新句沿用旧句的到期点 → 3 红；C 把所有句子都当
    notice（提示也会 8 秒后蒸发）→ 1、2 红；D 去掉换屏清除 → 4 红。撤销后 4/4 绿。
  - **无头测试证明不了的那一环交给真产物**：`DispatcherTimer` 只有在真正跑起来的应用里才会 tick。
    新增 `--smoke-ui-status-notice` 探针在已发布产物里用真时钟等回执退场（`raised` + `armedForNotice`
    + `retired`），并且断言解锁加载留下的那句 standing 没上表（`standingArmed=False`）。它排在所有
    阶段最前面、只用一条不改页面的命令（`ClearTotpFilters`），因为第一版让它走到同步页再走回来，
    实测把锁定态私有字节抬了约 10MB（128.9/131.0 vs 同二进制无探针的 116.7）——探针自己在产物里
    也是一次真实的导航，不该顺手改预算的测量点。`verify-artifact-runtime.ps1` 要求这条探针行必须出现
    且 `success=True`（悄悄不跑不等于门通过）。证伪方式：把 `StatusNoticeDwell` 改成 60 秒重新
    publish，产物里 `retired=False`（15 秒到点），release gate 直接红。
  - HANDOFF 之前记的 `ClearKeePassImportState` 不重置 `StatusMessage` 这一条不再需要单独修：
    `KeePassPreviewReadyFormat` 已归入 notice，同一屏内 8 秒退场、换屏立即退场。
- **锁定态内存门之前在给瞬态打分（#81，同轮修掉）**。#80 期间同一二进制连跑得到
  `lockedPrivateMB=116.7/119.0/119.5/122.5/124.1/124.8/130.4/131.0`，一开始当成新代码的回归去追，
  量下来结论相反：门取样取的是"刚锁上还在往下掉"的那一刻。把锁定后的采样改成每 5 秒一次、每次先
  完整压缩，连打 10 拍，同一二进制两次的轨迹是
  `121.7/123.7/118.6/116.8/118.8/113.9/113.9/114.0/112.5/113.5` 与
  `122.9/125.3/119.6/117.9/120.3/115.1/115.5/113.7/114.7/115.8`——约 30 秒内从 ~123 衰减到 ~114，
  同期线程数 37→31，而托管堆一直是 25–28MB（所以既不是 GC 没跑，也不是托管驻留泄漏）。
  - 预算判定换成**尾 5 拍中位数**（`Smoke UI locked settle result` 一行给出 trajectory/min/median/max，
    数字可复核）。"两次读数一致就停"这条更快的判据也试过，被否掉了：抖动本身就有 ±2MB，同二进制
    一次在第 5 拍"收敛"到 121.2 直接假红。
  - 判据没有放松：`maxMB` 仍是 120。证伪方式是在压缩处根住 15MB 再 publish，尾窗中位数 133.4、门红；
    撤掉后同一套代码两次绿（113.9 / 115.1，余量 5–6MB）。**仍然没解决的是绝对水位**：锁定态里只有
    26–28MB 是托管的，其余 ~90MB 是自包含运行时镜像映射 + Skia/GPU 表面 + 线程栈，要往下压得走
    trimming/AOT（#43）那条路，不是调这个门。
  - 代价：UI smoke 每次多跑约 45 秒（原 180 秒超时仍宽裕）。
- **走查第 3 项已修（#79）：信息密度与命名瑕疵**。改法是先量再删，不是照旧笔记抄：一次性探针在
  1280×800 真渲染树上扫 13 屏（可见文本重复 / "同一命令实例 + 同一标签"的重复按钮 / 左栏裸路径），
  量到才动手，扫描记录落在 `artifacts/density/probe4.txt`（gitignore 目录，不入仓）。
  - 删掉 12 处重字与裸路径（逐条见 §2 本轮行）。左栏行内不再印绝对路径，路径改 `ToolTip.Tip`。
  - 新守卫 `tests/Monica.UiTests/WorkspaceDensityGuardUiTests.cs` 四条：区块标签不得重印屏上已存在的
    文本、同一命令不得由两个同标签可见按钮提供、左栏区域不得出现 `\`、空查询时清除键必须隐藏。
    三类假缺陷同时植入 → 三条各自点名页面与 offenders 原文转红，撤销后 4 绿；第一条上线即在 Settings
    抓到真实重字（侧栏标题"设置"与应用导航当前项同字），不是给已有改动补写的绿灯。
  - **推翻两条旧怀疑，别照抄**：① Archive 头部计数与空列表不一致，只是探针绕过筛选缓存造成的假象，
    搜索往返后一致；② "大面积近空检查器"在零数据 + 无选中时与库页一样只是居中提示，为它做
    master-detail 折叠属过度设计，本轮不做。
  - `CanonicalVault` 两表各删一条：来源行不再往"远端地址"栏塞句子后它就没有引用者了，
    key 覆盖守卫仍绿（两表各 1381 条）。

## 8. 用户协作偏好（务必遵守）

- **中文响应。**
- **实证优先**：任何"内存/耗时/性能"结论必须真跑真测给出实测数字，不许凭读码给理论（包括我自己的）。
- **UI 口味偏"素"**：删重复、删嵌套；铺开前先探一屏 + 截图过口味门。
- **评审后再推**：commit 本地、展示结果，不擅自 push。
- 面对从别处 fork 进来的代码，先问"到底要不要"，再谈"怎么维护"。

---
接手第一步建议：工作树已干净、两套 Windows 门（源码级 + 产物级）实测全绿，直接从 §7 的未做项挑一条推进，
不必再花时间复验已绿的部分。

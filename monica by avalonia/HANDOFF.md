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

分支 `main`，`git status` 无未提交改动。功能 HEAD = `aaa698b`（其后只可能有给本节自身标提交号的文档提交），近几轮：

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

| `69a44e1` | 切语言时把**屏幕上已经停着的那句**也重译（补 #78 留下的缺口）：状态漏斗改存 key + 参数、`StatusMessage` 读取时才解析，语言刷新处顺手重发状态通知。新增 3 条单测（失败句／带参句／已清空句）+ 1 条走 `SettingsLanguage` 真链路的渲染带测试；证伪：只把刷新调用注掉时单测仍全绿、UI 测试在 `"Enter a folder name."` 处转红，说明无头测不到的那一半确实由 UI 测试守着。门禁：格式 0 改动、Release 0 warning、单测 9+716、UI 17+188 全绿；重新 publish 后产物门 loadMs=186、KeePass 20000 条增长 3.4MB、锁定尾窗中位 110.6MB（尾区间 109.2–112.8） |
| `f700bcc` | 最小化到托盘改为**出厂即开**：`MinimizeToTray` 默认 `true` + `SettingsSchemaVersion` 升到 2，老配置文件里那个"当年没人问过就写下去的 false"只被升级一次，之后用户主动关掉的能守住。UI 测试改成走 `InitializeAsync()` 真链路（设置→VM→协调器→托盘），不再靠手工赋值假装默认值生效。真机验证见 §7 第 1 条。门禁：单测 9+718、UI 17+188 全绿；重新 publish 后产物门全绿（loadMs=347、KeePass 增长 4.6MB、锁定尾窗中位 113.3MB/120）——证明托盘默认开没有把 `--smoke-ui-exit-after-checks` 的退出路径拖成挂死 |
| `1d8c3a4` | 桌面端自动输入（#84，走查后用户点名的第二条必须功能）：全局热键 `Ctrl+Shift+Enter` 把"用户名 → Tab → 密码"用 `SendInput(KEYEVENTF_UNICODE)` 打进**当前前台的别人家窗口**——不碰剪贴板（因此不必和 `SecureClipboardService` 的自动清理抢时序），且**从不发 Enter**，提交留给用户；开关默认关，因为"会往别的窗口打字"不该由我们替用户打开；`WindowsGlobalHotkeyService` 槽位化（快速搜索／自动输入各自 `RegisterHotKey` + 各自消息泵线程），两槽填同一组合时先判冲突并说清"快速搜索已占用"，而不是留一条说不清的注册失败。匹配只信前台标题：标题含 host 形状就按 host 判（条目存 `github.com`、标题 `github.com.phishing.test` → 不打），命中 0 条或多于 1 条一律拒打并给原因。**查清的一个假象**：判"前台是不是我自己"原本用 Avalonia 的 `Window.IsActive` + 缓存自身 hwnd，实测进程**零顶层窗口**的时刻它仍读回 `True`（那个缓存 handle 的 pid=0、`IsWindow=False`）→ 会永远误拒，改成问操作系统（`GetWindowThreadProcessId` 比对自身 pid），单测用**真 message-only 窗口**跑通 true 分支。真机正反两例都在**发布产物**上量过（`outcome=Typed`、落点 15/19 字符分别匹配；钓鱼标题 `NoMatch` 且两个框都空），跑法与未覆盖项见 §7 第 2 条。顺带：设置页为过 300 行门把浏览器配对区块拆成 `SettingsBrowserPairingSectionView`，并修掉门禁脚本用 `[IO.Path]::GetRelativePath`（5.1 上不存在）导致行数违规既不变红也不点名的坑。门禁：格式 0 改动、Release 0 warning、单测 9+747、UI 17+191 全绿；重新 publish 后产物门 loadMs=536/4000、KeePass 增长 2.9MB/24、锁定尾窗中位 114.4MB/120 |
| `541069c` | 单实例守卫（#86，托盘默认开引出的用户可见问题）：同一个数据目录只允许一个实例，第二次启动不再自己开第二个窗口，而是**把还活着的那一个叫回前台**后退出 0；锁按数据目录取键（各自 `MONICA_APPDATA_DIR` 的独立实例、CI 顺序跑法都不受影响），接力事件在拿到锁的同一瞬间命名，因此双击发生在 UI 订阅之前也不会丢。顺带修掉守卫的取证工具本身：诊断日志的 append 流只在打开时定位一次末尾，两个进程写同一个 `runtime.log` 会从中间互相盖掉——实测出现过一条记录被拼进另一条的句子中间、接力证据消失；现在每批写之前重新求末尾并整批一次写。真机正反两例都在发布产物上量过（见 §7 第 3 条），门禁：格式 0 改动、Release 0 warning、单测 9+754、UI 17+191 全绿；重新 publish 后产物门 loadMs=1082/4000、KeePass 20000 条增长 8.8MB/24、锁定尾窗中位 107.1MB/120 |
| `61080f6` | 首次收进托盘的可发现性提示（#85，托盘默认开欠下的那一条）：窗口 `Hide()` 进托盘后桌面上**一点痕迹都没有**，图标还大概率在通知区域的溢出区里，所以每个安装的**第一次**收起，会在托盘那一角画一个气泡（标题 + "窗口去哪了、怎么回来" + 一个"显示 Monica"按钮），8 秒后自己退场。Avalonia 12.0.4 的 `TrayIcon`/`NotifyIcon` 都没有 `ShowBalloonTip`（在产物上量过），只能自绘：无边框 + `Topmost` + `Focusable=False` + `ShowActivated=False`，不抢焦点也不夺激活。**"每个安装一次"必须记两层**（本轮会话一个布尔 + 落盘 `TrayHintShown`），这不是审美是被实测逼出来的：`Show()` 之后再 `Hide()` 会重走 `OnOpened`→`InitializeAsync`→`LoadAsync`，内存里的标记被文件里那份（写盘防抖 150ms，还没落）盖掉，只信落盘就会在同一轮里第二次弹。锚点用 `ClientSize` 不用 `Bounds`（实测这个窗口的 `Bounds.Height` 一辈子停在 `SizeToContent` 之前的 707.33，而 client 是 121.33）。覆盖：UI 6 条 + 单测 1 条，两条证伪（注掉会话守卫→第 5 条红；`TrayHintDwell` 改 60 秒→第 4 条在 20.3 秒红）。四阶段真机门在**发布产物**上全绿（§7 第 4 条），单实例探针在同一产物上重跑仍正反两例全绿（§7 第 3 条）。门禁：格式 0 改动、Release 0 warning、单测 9+755、UI 17+197 全绿；重新 publish 后产物门 loadMs=934/4000、KeePass 20000 条增长 4.0MB/24、锁定尾窗中位 114.7MB/120 |
| `aaa698b` | 接力回来的窗口真的浮到用户在看的应用之上（#87，#85 那句文案欠下的另一半）：退出前的那次启动把自己持有的前台权限交给活着的那一份（按数据目录键查到 owner PID → `AllowSetForegroundWindow` → 再发接力信号），owner 由命名 `MemoryMappedFile` 在持锁期间公布 PID。**先量后修**：未修产物上 `windowBack=0.01` 秒窗口就回到桌面，但 `foregroundMonica=-1`、`aboveRival=-1`，Monica 停在 z=150 而 rival 在 z=18，6.6 秒都没浮起来——"再次启动就能回来"当时只有一半是真的。修后同一条门 `foregroundMonica=0.15 aboveRival=0.15`、之后每点都是 z=18<19。反证：把 grant 换成常量 `false` 重新构建，门立刻回红（同样 -1、z=150<18）。顺序由单测钉住（`GrantIndex==0/SignalIndex==1`），非 Windows 返回 `false` 且接力照旧送达。探针演化掉三条死路（注入 ALT 会把 rival 的 WinForms 线程 park 进菜单模态循环、`SwitchToThisWindow` 与 `AttachThreadInput` 都拿不到前台），跑法与判读见 §5。门禁：格式 0 改动、Release 0 warning、单测 9+758 全绿；重新 publish 后产物门 loadMs=261、锁定尾窗中位 108.0MB/120（区间 107.8–109.0）、锁/解循环 25/14/1/4 全数回读，单实例正反两例与托盘提示四阶段在同一产物上重跑仍全绿 |

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

# .ps1 必须显式交给 powershell：在 Git Bash 里直接 `./x.ps1` 会被 bash 当 shell 脚本解释，
# 报 `syntax error near unexpected token 'newline'` 而退出码仍为 0（假绿，实测踩过）。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File eng/ci/verify-artifact-runtime.ps1 ...

# 只跑一条 UI 测试：UI 套是 xUnit v3，用简单过滤器 `-method`（不是 `dotnet test --filter`）。
# 查询式 `-filter "/fullyQualifiedName~X"` 会静默匹配 0 条并打印 Total: 0，别当成通过。
dotnet tests/Monica.UiTests/bin/Release/net10.0/Monica.UiTests.dll -method "*NameFragment*"

# UI 测试方法名用下划线分词，所以片段要写 "*Auto_type*"；写成 "*AutoType*" 会静默匹配 0 条。

# 门禁脚本自身也受 5.1 约束：`[IO.Path]::GetRelativePath` 在 .NET Framework 上不存在，
# 所以"文件超行数"那条违规原先在这里抛 MethodNotFound，既不变红也不点名超线文件（已改回字符串拼接）。
```

真机 GUI 驱动（本轮验证托盘时踩到的四个坑，写脚本前先读）：

```powershell
# 1. Windows PowerShell 5.1 按 ANSI 读无 BOM 的 .ps1：脚本里写中文字面量会解析成乱码并直接语法崩。
#    对策：脚本一律 ASCII-only，需要匹配本地化文案时改用几何/类名定位（ClassName、AutomationId、rect）。
# 2. 日志别用 Write-Output：`if (-not (Aim ...)) { throw }` 会把函数里 Write-Output 的内容吸进返回值，
#    非空数组恒为真 → 守卫被静默吃掉（实测就这样让三次"瞄准失败"的点击照样发了出去）。
#    对策：函数内日志用 [Console]::WriteLine，函数只 return 严格布尔。
# 3. 枚举菜单项必须锚定：按"任意窗口里有 >=2 个 MenuItem"来找，会抓到别的应用的菜单栏
#    （本轮点到了 IDE 的"编辑"标题，只是开了个菜单、没触发命令，属侥幸）。
#    对策：只取中心点落在目标托盘图标 420px 以内的窗口。
# 4. Avalonia 的托盘菜单项不支持 UIA InvokePattern（抛"不支持的模式"）——只能按 rect 中心真实点击；
#    点击前先 Aim()（含重试），光标落点与目标差 >2px 就 throw 不点，这是仓库记忆里既有的规则。
# 5. Avalonia 窗口 GDI 截屏读不到：`CopyFromScreen` 只拿到壁纸，加 `CaptureBlt` 变纯黑（合成表面不被
#    BitBlt 覆盖）。对策：`PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT=2)`，并用亮度极差断言"真的画了"。
# 6. 探针进程要自己 `SetProcessDPIAware()`：unaware 下 PrintWindow 把窗口按真实像素画进按虚拟尺寸开的
#    bitmap，正文右/下边缘被切——实测就这样把一次正常的渲染读成了"文字溢出气泡"的布局缺陷。
# 7. 注入点击的 DOWN/UP 之间必须停 ~80ms：同一轮事件循环里的 0ms 点击 Avalonia 按钮不认（实测：点在建
#    议按钮自己的 UIA 矩形内却没反应，停 80ms 再点立刻把窗口叫回来）。
# 8. 点最小化前先等应用自己写下 `Initialize completed`：`MinimizeToTray` 是设置加载完才赋给 VM 的，早于
#    那一刻的最小化只是普通最小化，气泡不会来（实测假红一次）；而"这次不该有气泡"的两个阶段也会因此
#    假绿。判据用 `runtime.log` 的偏移量，否则复用同一目录的第二次启动会读到上一轮的标记。
# 9. 存盘有 150ms 防抖：气泡刚出现就 Kill 进程时 `settings.json` 还不存在（实测 `<no settings.json>`），
#    必须先轮询到落盘再杀，不然测的是探针的手速而不是持久化。

实测数字（本轮接力浮窗门之后）：源码门全绿（单测 9 perf + 758 functional、`--warnaserror` 0 warning）；
重新 publish 后产物门 `loadMs=261`、锁定态私有字节尾窗中位 108.0MB（区间 107.8–109.0，预算 120）、
锁/解循环 25/25 密码 + 14/14 笔记 + 1/1 TOTP + 4/4 钱包、`release gate completed success=True, loadMs=198`。
上一次记录（`61080f6`）为 loadMs=934、114.7MB（区间 114.6–115.6）。
注意：perf-budget 通道在整串门里紧跟 `dotnet build` 起跑时读到过 483ms（预算 400），单独复跑三次为
9/9 全绿；这是冷启动+构建负载的单次读数，按仓库规则先复跑取分布，不要调阈值。

真机自动输入门（**不在 CI 里**，需要交互桌面 + 外部目标窗口 + 真实按键）：

```powershell
# 正例：默认标题命中 seeded 的 github 条目，脚本自己敲 Ctrl+Shift+Enter，再看两个输入框落成什么
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/verify-autotype-injection.ps1 `
  -ExePath <publish>\Monica.App.exe -AppDataDirectory <空临时目录> -ExpectedOutcome Typed
# 反例：标题含 host 但是钓鱼域，必须拒打且两个框都空
... -WindowTitle "Sign in - github.com.phishing.test" -ExpectedOutcome NoMatch
```

脚本 ASCII-only、只打印长度与布尔（凭据明文一律不落日志），退出码 0 表示观察到的 `outcome` 与预期一致。

单实例守卫真机门（**同样不在 CI 里**，判据是"窗口回到桌面上"，需要交互桌面会话）：

```powershell
# 1. 先建/重播种探针库（会删重建目录；口令只走环境变量，脚本不回显）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/prepare-probe-appdata.ps1 `
  -ExePath <publish>\Monica.App.exe -AppDataDirectory "<空临时目录 A>"
# 2. 正例：同一目录的第二次启动必须接力并退出 0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/verify-single-instance.ps1 `
  -ExePath <publish>\Monica.App.exe -AppDataDirectory "<A>"
# 3. 反向对照：换第二个已播种目录=第二个保险库，第一个窗口必须不自己回来
... -AppDataDirectory "<A>" -SecondAppDataDirectory "<B>"
```

判读要点：探针按 250ms 时间轴采样 `handoff sent → reopen received → surfaced → 窗口重新可见`，并且先
用 6 秒证明"没人管时它一直藏着"。**不要**改回"窗口不再最小化"这种判法——最小化会把窗口 `Hide()` 进托盘，
进程处于零可见窗口状态，此时 `IsIconic(IntPtr.Zero)` 返回 `False`，实测就这样假绿过一次。两个目录要各
自播种（`prepare-probe-appdata.ps1` 跑两遍，第二遍换 `-AppDataDirectory`）。

接力浮窗真机门（**同样不在 CI 里**，量的是托盘提示文案那句"或再次启动 Monica，即可回到窗口"）：

```powershell
# 需要交互桌面 + 一个已播种目录；探针只打印 hwnd/pid/z 序与秒数，不打印任何凭据材料
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/verify-handoff-foreground.ps1 `
  -ExePath <publish>\Monica.App.exe -AppDataDirectory "<A>"
```

与上一条单实例门的分工：单实例门只证明"窗口回到桌面上"，而它启动第二份副本时前台是探针自己的控制台，
所以它读到的 `first restored is foreground: False` 是**该场景的应有值**、不是缺陷；只有这条门会去问
"用户正在看的那个应用还挡在上面吗"。判据：`rival-window.ps1` 画一个盖住 Monica 原位置的窗口，探针把
前台权限交给它（`AllowSetForegroundWindow`），它自己抬自己 → 最小化 Monica → 由这个前台窗口启动第二份
副本 → 按 ~110ms 采样 2.1 秒，要求 `foregroundMonica` 与 `aboveRival` 都在 2 秒预算内落到非负、且 Monica
的 z 序索引小于 rival（`EnumWindows` 从前到后走，索引小=更靠上；前台≠z 序，两个都要读）。

踩过的坑（都在这条门的演化里）：

```powershell
# 1. 不要用 keybd_event 注入 ALT 来绕过前台锁。实测：ALT 确实让 rival 拿到了前台，但那次注入的 ALT
#    在激活之后才落进 rival（WinForms）自己的消息队列，把它停在 user32 的菜单模态循环里——
#    1.5~2.2 秒后 `Application::DoEvents()` 再也不返回，进程活着、stderr 空、窗口冻结，第二份副本永不启动。
# 2. `SwitchToThisWindow` 单独用不够（读不到前台），`AttachThreadInput` 挂到当前前台线程再
#    `SetForegroundWindow` 也不够（那条前台线程是探针自己的控制台）。可行的是"权限传递"：探针由前台
#    控制台启动，因此持有该权限，`AllowSetForegroundWindow(rivalPid)` 后由 rival 自己抬自己。
# 3. 传递的是**一次性**权限：用了就没了。所以 grant 文件里写的是递增序号而不是"存在即真"，最小化 Monica
#    之后探针再 grant 一次，rival 在同一个消息泵里同时盯 grant 序号和 relaunch 标记。
# 4. 后台静默 helper 不可归因：rival 必须留面包屑（步骤+循环计数+CPU 秒），并且用
#    `-RedirectStandardOutput/-RedirectStandardError` 起进程，否则"没启动"和"启动时抛异常"打印得一模一样。
```

托盘提示真机门（**同样不在 CI 里**，判据是桌面上气泡的位置、渲染与退场）：

```powershell
# 四个阶段一次跑完。-ClickOffsetX/-ClickOffsetY 是"显示 Monica"按钮中心相对气泡左上角的 dip 偏移，
# 不传就跳过点击阶段。探针只用空数据目录、从不解锁，所以截图与日志不可能带上凭据材料。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/verify-tray-hint.ps1 `
  -ExePath <exe> -ProbeRoot "<空临时目录>" -ClickOffsetX 68 -ClickOffsetY 93
```

判读要点：阶段 1 量气泡在不在托盘那一角（`rightGap/bottomGap` 是真实设备像素，16dip 在 150% 下应是 24）
并用 `PrintWindow` 断言它真的画出了内容；阶段 2 用一次真点击把窗口叫回来，并确认**同一轮里第二次最小化
不再欠一次说明**；阶段 3 先看 `settings.json` 里的标记再重启，证明"每个安装一次"不是"每次启动一次"；
阶段 4 换一个全新目录，只用真时钟等它自己退场（`visibleWindowsAfterRetire=0`）。四个阶段任一不符即退出码 1。

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

- **走查第 2 项的续集已修（#82）：切语言会把屏幕上已停着的那句一起重译**。#78 当时明写"故意没做"，
  这轮补上。形状：状态漏斗不再存已翻译句子，改存 `key + args`，`StatusMessage` 在**读取时**才解析；
  语言刷新（`RefreshLocalizedProperties`，由 `LocalizationService.SetLanguage` 的
  `PropertyChanged` 驱动）顺手调用 `RaiseStatusMessageState()`。三种寿命、琥珀横幅判定、退场计时器
  都不受影响——判定用的还是意图，不是文案。
  - 覆盖：单测 3 条（失败句重译且 tone 不丢／带参句重译后参数仍是写入时的值／清空后重译不得把裸
    key 印回屏），UI 1 条走 `SettingsLanguage` 真链路并读**渲染带里的文本**，不走 `viewModel.L.SetLanguage`
    近路，所以设置回调→本地化通知→VM 刷新三段接线都在断言路径里。
  - 证伪（守卫必须真的会红）：改动前两条重译单测红在 `"Enter a folder name."`、渲染带测试红在同一句；
    实现后全绿。再单独把 `RaiseStatusMessageState()` 一行注掉：**三条单测仍全绿、只有渲染带测试转红**
    （Expected 请输入文件夹名称。/ Actual "Enter a folder name."），这正是无头 VM 测试看不到绑定刷新的
    证据，也是这条 UI 测试存在的理由。
- **用户点名的必须功能（2026-09-22 拍板）：四条都已出厂**（托盘 `f700bcc`／自动输入 `1d8c3a4`／单实例守卫
  `541069c`／首次收进托盘的一次性提示，见第 4 条）。
  下一轮从各自条目末尾那份"仍未做/仍未验证"清单里挑，别再回头补已经量过的部分：
  1. **最小化到托盘：已出厂即开，并在真机 Windows 会话里逐项量过（`f700bcc`）**。
     - 改了什么：`AppSettingsService.cs` 的 `MinimizeToTray` 默认 `true`，`SettingsSchemaVersion`
       1→2，`Migrate()` 里 `version < 2` 时把 `MinimizeToTray` 置真一次。理由：托盘默认关的那些年，
       存盘的 `false` 是**遗留默认值**而不是用户决定；升级一次之后，用户在设置页主动关掉的值会正常
       持久（新守卫 `App_settings_enables_the_tray_once_for_installs_that_never_chose_it` 就是测这条
       往返：旧文件→翻真→主动关→存盘→重载仍为假）。先红后绿：两条新单测改前都红。
     - UI 测试不再靠手工赋值假装默认值生效：`DesktopIntegrationUiTests` 改走 `await viewModel.InitializeAsync()`
       真链路（设置→`ApplySettings`→`DesktopIntegrationCoordinator.ApplyTraySetting`→托盘），并补了
       "关掉后托盘应随之隐藏"的反向断言。
     - **真机实测（Debug 产物 + `MONICA_APPDATA_DIR` 指向空临时目录，即"全新安装"）**，用 UI Automation
       读任务栏树 + 全屏截图，逐项观察：
       1. 托盘图标**确实注册**：`TopLevelWindowForOverflowXamlIsland` 下出现
          `NotifyItemIcon name='Monica'`；截图里渲染的是应用自己的锁+钥匙图标，不是占位方块。
       2. 最小化→窗口从桌面窗口列表消失（`windows=0`）、进程仍在（`procs=1`）。
       3. 点 X 关窗→同样只隐藏不退出（`windows=0 procs=1`）。
       4. 左键单击托盘图标→窗口回来（`windows=1`）。
       5. 右键菜单出现且是中文三项：显示 Monica／锁定保险库／退出 Monica（菜单窗口 class `TrayPopupRoot`，
          条目 rect 197×49 起于 1786,1357）。
       6. 点"显示 Monica"→`windows` 由 0 变 1；点"退出 Monica"→`procs=0`，进程真的结束。
       7. 产物门在默认开启下仍全绿（含 `--smoke-ui-exit-after-checks` 的退出路径），说明托盘隐藏没有把
       真跑门拖成挂死。
     - **仍未做/仍未验证（下一轮别当成已完事）**：
       - 图标落在**"显示隐藏的图标"溢出区**，不在常驻通知区——这是 Windows 的决定，程序无法强制置顶。
         首次隐藏的一次性提示已作为第 4 条出厂（#85），但**溢出区本身没有验证过**：气泡是绕开"找不到图标"
         的路子，不是把图标拉出溢出区。
       - "锁定保险库"菜单项只在**已锁定**状态下点过一次（无可见变化，等于没验证）；解锁态下它是否真的
         锁上并留在托盘，未测。
       - 双击唤起、气泡通知、`App.axaml` 里的 `<TrayIcons>` 声明（现在是运行期手动挂）仍未做。
  2. **自动填充：桌面端已出厂（#84），正反两例都在发布产物上真机量过**。选定路线＝全局热键把凭据
     输入到**当前前台的别人家窗口**。形状：
     - 开关 `AutoTypeEnabled` **默认关**（opt-in，无 schema 迁移）：它会往别的窗口打字，不该由我们替用户打开。
       手势默认 `Ctrl+Shift+Enter`。`WindowsGlobalHotkeyService` 改成 **slot 化**（`QuickSearch`／`AutoType`
       各自 `RegisterHotKey` + 各自消息泵线程）；两槽填了同一组合时直接判冲突并说"快速搜索已占用这个快捷键"，
       而不是让第二次注册失败成一个说不清的报错。
     - 注入＝`SendInput(KEYEVENTF_UNICODE)`，**完全不碰剪贴板**，因此不必和 `SecureClipboardService` 的
       自动清理抢时序（安卓那套"写剪贴板→粘贴→500ms 还原"在桌面端没有对应需求）。token 序列固定
       用户名 → `Tab` → 密码，**从不发 Enter**，提交留给用户。
     - 匹配（`AutoTypeMatcher`）只看前台窗口标题：标题里出现 host 形状时一律按 host 判，条目存 `github.com`
       而标题是 `github.com.phishing.test` 就**不算匹配**；标题不含 host 才退回整词标签匹配。命中 0 条或多于
       1 条都拒打并说清原因（`NoMatch`／`Ambiguous`）。
     - **真机实测**（`artifacts/publish/win-x64/jit/Monica.App.exe`，`MONICA_APPDATA_DIR` 指向空临时目录，
       WinForms 目标窗体两个输入框 + 外部真实按键），脚本 `artifacts/autotype/verify-autotype-injection.ps1`
       （gitignore 目录；只报布尔和长度，绝不打印凭据明文）：
       1. 正例（标题 `Sign in to GitHub - github.com`）：`armed=True, gesture=Ctrl+Shift+Enter,
          registrationError=False` → `outcome=Typed matches=1`，落点
          `firstLength=15 firstMatchesUsername=True secondLength=19 secondMatchesPassword=True`，`appExit=0`；
          `owner check: targetPid=58400 harnessPid=58400 appPid=11504` 证实字确实落在**别人的**窗口。
       2. 反例（钓鱼标题 `Sign in - github.com.phishing.test`）：`outcome=NoMatch matches=0`，两个框都空
          （`firstLength=0 secondLength=0`）。
     - **查清的一个假象，下一轮别退回旧写法**：协调器最初用 Avalonia 的 `Window.IsActive` + 缓存自身 hwnd
       判断"前台是不是我自己"，实测在进程**一个顶层窗口都没有**的时刻 `IsActive` 仍读回 `True`
       （`totalTopLevel=434 owned=`，那个缓存 handle 的 `GetWindowThreadProcessId` 返回 pid=0、`IsWindow=False`）
       → 真按快捷键会被误判成"焦点在 Monica"、永远拒打。改成问操作系统：
       `IAutoTypeService.IsWindowOwnedByThisProcess(hwnd)`（比对自身 pid）。单测用**真的 message-only 窗口**
       跑通 true 分支，不是只测 false 分支。
     - 门禁：格式 0 改动、Release 0 warning、单测 9+747、UI 17+191；产物侧 `--smoke-ui-autotype` 探针走设置页
       同一条链路（`AutoTypeEnabled=true`→协调器→`RegisterHotKey`），只报 armed/pressed/outcome。
     - **仍未做/仍未验证（别当成已完事）**：
       1. `--smoke-ui-autotype` **没有接进 `verify-artifact-runtime.ps1`**：它要交互桌面会话、一个外部目标窗口
          和一次真按键，CI 里跑不起来，所以现在是"手动真机门"，跑法见上面的脚本与两个场景。
       2. "Monica 真在前台 → 拒打"这一支：单测验了谓词本身（真窗口），UI 测试验了回调→服务→VM 的接线，但
          **端到端没有人在 Monica 获得焦点时按过一次快捷键**。诚实记录一个弱点：把 `window.IsActive ||`
          重新加回去，那条 UI 测试**仍然绿**——它钉的是接线不是这个回归，该回归的实际守卫在真机 harness。
       3. 只覆盖了 WinForms 目标。浏览器登录框、`Tab` 顺序不同的表单、需要 `Ctrl+Enter` 提交的应用未测；
          非 ASCII 用户名／密码（`KEYEVENTF_UNICODE` 走 UTF-16，理论支持）未实测。
       4. "唤起快速搜索→就地送进刚才那个窗口"没做，两条路径目前彼此独立。
       5. 与隐私屏／`WindowCaptureProtection` 并发下的行为未测（自动输入路径刻意不 raise、不 focus Monica 的
          任何窗口，因此既没触发那条路径，也就没验证过它不受影响）。
       6. 浏览器扩展本体仍缺：`WindowsBrowserBridgeService` 的回环 HTTP（`/v1/session/check`、
          `/v1/credentials/query`，默认端口 49152，Origin 校验 + Bearer 会话令牌）在跑，扩展没有
          （安卓 README 说 Monica for Browser 已归档、新扩展重写中）。
  3. **单实例守卫：已出厂（#86 / `541069c`），正反两例都在发布产物上真机量过**。它是托盘默认开直接
     引出的问题：窗口收进托盘后看不出"已经有一个在跑"，双击两次图标就是两个进程指着同一个 `monica.db`。
     - **先实测再设计**（守卫之前的双实例行为）：2 个进程、2 个窗口、各自私有字节 98.4 / 95.7MB、
       不崩溃、也没有任何损坏信号——危害是**两份互不知情的解密副本**（后写的那份会整库盖掉前者），
       不是报错。所以别按"第二实例应该会失败"来理解这条。
     - 形状：`Services/SingleInstanceGate.cs`。锁**按数据目录取键**（目录规范成全路径＋小写后取 SHA-256
       前 8 字节），所以不同的 `MONICA_APPDATA_DIR` 仍是各自独立的实例——CI 与开发用的隔离目录、
       并排的探针库都不受影响（反向对照就是靠这一点跑的）。`Mutex` 句柄的存活期就是锁，进程被杀/崩溃
       时操作系统关句柄即释放，因此不等待所有权；接力用命名 `EventWaitHandle` + 线程池等待注册。
     - **一个只在启动窗口里可见的竞态**：接力事件原本等到外壳订阅时才命名，双击发生在"已拿锁、未订阅"
       之间就没人接收。改成拿锁成功的同一瞬间命名该事件，信号便停在事件上等订阅者来取；
       单测 `A_reopen_that_arrives_before_anyone_is_listening_is_still_delivered` 钉住这一条。
     - 覆盖：单测 10 条（同一目录的两种写法算一个键 / 不同目录各自判定 / 第二个 gate 不是主实例并能接力 /
       没有监听者时握手失败如实上报 / 用**真的命名 `Mutex`+`EventWaitHandle`** 跑通"第二次上锁被拒、
       信号送到第一个监听者" / 订阅前到的信号仍会送达 / 前台权限**先于**接力信号交出 / 交不出权限时接力
       仍然如实送达 / 持有锁期间能用 PID 找到 owner、释放后找不到）。命名原语**跨进程**那一半另用
       `artifacts/autotype/probe-named-primitives.ps1` 起两个进程证过（`owner: mutexCreatedNew=True` /
       `sender: signalled=True`），同进程内的同名对象不足以证明这点。
     - **真机实测**（`artifacts/publish/win-x64/jit/Monica.App.exe`，跑法见 §5）：
       1. 正例（同一目录）：`timeline s: exit=0.27 handoffSent=0.27 reopenLog=0.27 surfacedLog=0.27 windowBack=0.27`、
          `second: exitCode=0 stillRunning=False visibleWindowPeak=0` → `success=True`。在此之前探针先证明
          第一个窗口**没人管的情况下 6 秒一直藏着**，否则"窗口回来了"可以是它自己回来的。
       2. 反向对照（第二个目录=第二个保险库）：`windowBack=-1`、第二实例 `stillRunning=True visibleWindowPeak=1`
          → `success=True`。这一条是判据本身：不接力时窗口不会自己回来。
       3. 记账（守卫之后同一探针库）：`procs=1/2 namedWindows=1 privateMB=101.4`，即第二次启动不再留下
          第二份 ~95MB 的解密副本。
     - **顺带修掉取证工具自身的真缺陷**：`runtime.log` 曾被两个进程从中间互相盖掉——`AppDiagnostics` 的
       append 流只在打开时定位一次末尾，第二个进程写完后，第一个进程按自己的旧偏移续写，实测一条记录被
       拼进另一条的句子中间、并且接力证据整个消失（这就是正例一度 `success=False` 的原因）。现在每批写之前
       重新求末尾、整批一次写；修复后同一次运行里两个进程的四个标记逐条完整。
     - **仍未做/仍未验证（别当成已完事）**：
       1. ~~接力回来的窗口 `foreground=False`，用户是否真的看见窗口浮起**没有量过**~~ →
          **已量清并已修（#87）**。量出来不是"大概抢不到"，是**完全抢不到**：
          - 红基线（未修产物，`artifacts/autotype/run-21/25-handoff-*.log`）：
            `windowBack=0.01~0.13` 秒窗口就回到桌面上，但 `foregroundMonica=-1`、`aboveRival=-1`，
            Monica 停在 z=150、rival 在 z=18，**整整 6.6 秒没有浮到用户在看的那个应用之上**。
            也就是说托盘提示那句"或再次启动 Monica，即可回到窗口"当时只有一半是真的。
          - 修法：退出前的那次启动把它本来就持有的前台权限交给活着的那一份——`SingleInstanceGate`
            在 `Signal` **之前**调 `AllowSetForegroundWindow(ownerPid)`，owner 由一块命名
            `MemoryMappedFile`（`Monica.SingleInstance.Owner.<key>`，只在持锁期间存在）按 PID 公布。
            顺序是被单测钉住的（`GrantIndex==0 / SignalIndex==1`），因为权限属于这个即将退出的进程，
            而 owner 只在听到信号后才抬窗口；把两行调换，实测这条单测会红。
          - 绿（同一探针、同一产物，只换二进制）：`windowBack=0.01 foregroundMonica=0.15 aboveRival=0.15`，
            之后每个采样点都是 Monica z=18 < rival z=19。反证也做了：把 `TryGrantForegroundToOwner`
            换成常量 `false` 重新构建，同一条门立刻回红（`foregroundMonica=-1`、z=150<18）。
          - 非 Windows 上 `TryGrantForegroundToOwner` 直接返回 `false`，接力照旧送达（单测覆盖），
            所以这条修的是 Windows 行为，没有给别的平台加依赖。
       2. 跨会话/以管理员身份运行的两份 Monica 分属不同命名空间，锁拦不住彼此——未测，也未按产品缺陷处理。
       3. 接力失败（`handoff did not land`）时**没有任何用户可见反馈**，只有日志一行。
       4. 这两条探针（单实例、接力浮窗）**都没接进 `verify-artifact-runtime.ps1`**：它们需要交互桌面
          （判据是"窗口回到桌面上"、"回到桌面上并且盖在别的应用之上"），CI 无头会话跑不出可信结果。
       5. 守卫只在**窗口化启动路径**上生效；命令行冒烟分支（`--seed-smoke-vault` 等）在取锁之前就已返回，
          所以并行的两个 CLI 调用不受约束——这是有意的（它们是短命的读写会话），但没有测试钉住这个边界。
  4. **首次收进托盘的可发现性提示：已出厂（#85）**。托盘默认开之后，第一次点最小化的人面对的是"窗口凭空
     没了"，而图标大概率在通知区域的溢出区里。形状：`TrayHintWindow`（无边框、`ShowActivated=False`、
     `Topmost`、`Focusable=False`，不抢焦点也不夺激活）钉在托盘那一角 16dip 处，说清"窗口收进了右下角的
     通知区域，点那里的图标或再次启动 Monica 就能回来"，外加一个"显示 Monica"按钮。
     - **为什么是自己画的窗口**：Avalonia 12.0.4 的 `TrayIcon`/`NotifyIcon` 都没有 `ShowBalloonTip`
       （在产物上量过），系统气泡这条路根本不存在。
     - **"每个安装一次"记两层**：本轮会话一个布尔 + 落盘的 `TrayHintShown`。这个拆分不是审美，是被实测
       逼出来的：`Show()` 之后再 `Hide()` 会重走 `OnOpened`→`InitializeAsync`→`LoadAsync`，`Current` 被
       文件里那份（防抖 150ms，标记还没写进去）盖掉，只信落盘标记就会在同一轮里第二次弹出来。证伪：拿掉
       会话守卫，`A_settings_reload_mid_run_does_not_earn_the_explanation_back` 红在 `ShouldSurfaceTrayHint()`。
     - 锚点用 `ClientSize` 不用 `Bounds`：这个窗口的 `Bounds.Height` 一辈子停在 `SizeToContent` 之前的值
       （实测 bounds 340x707.33 而 client 340x121.33），照 `Bounds` 算气泡会挂在离托盘 603px 高的地方。
     - 覆盖：UI 6 条（第一次最小化会解释／关到托盘也解释／点按钮把窗口叫回并关掉气泡／真时钟等它自己退场／
       设置重载赢不回一次说明／托盘关着就不该解释）+ 单测 1 条（标记往返落盘）。两条证伪：注掉会话守卫→
       第 5 条红；`TrayHintDwell` 改 60 秒→第 4 条在 20.3 秒红。
     - **真机实测**（`artifacts/autotype/verify-tray-hint.ps1` 跑 `artifacts/publish/win-x64/jit/Monica.App.exe`，
       跑法见 §5，四阶段全绿）：气泡 `rect=2026,1322,2536,1504`、`rightGap=24 bottomGap=24`（16dip×150%）、
       `510x182` px、`PrintWindow` 亮度极差 600（确实画出了东西），此时进程只剩这一个窗口；真点击按钮中心
       （68,93 dip → 2128,1462）后 `mainBack=True hintStillThere=False`，窗口回到原位 `228,228,1750,1259`，
       同一轮第二次最小化不再欠一次说明；`settings.json TrayHintShown=True`（进程还活着时就已落盘，
       `flushed while running=True`）且重启后不再出气泡（`relaunch explains the tray again=False`、
       `appHidden=True`）；换全新目录后只用真时钟等它退场，`visibleWindowsAfterRetire=0`。
     - 门禁：格式 0 改动、Release 0 warning、单测 9+755、UI 17+197 全绿；重新 publish 后产物门
       loadMs=934/4000、KeePass 20000 条增长 4.0MB/24、锁定尾窗中位 114.7MB/120。
     - **与单实例守卫并存已在同一产物上复验**：守卫探针正例重跑仍 `success=True`
       （`exit/handoffSent/reopenLog/surfacedLog/windowBack` 全在 0.29 秒）。静置 6 秒那一段桌面上可见的窗口
       恰好就是 `[Monica tray hint]`，守卫探针按标题精确取 `Monica`，因此气泡既没被算成"窗口自己回来了"，
       也没挡住宿主的接力——两条默认开启的托盘功能互不干扰这点是量出来的，不是推出来的。
     - **仍未做/仍未验证（别当成已完事）**：
       1. 与 `WindowCaptureProtectionEnabled`（隐私屏）同时开时气泡会怎样，没测过——这条路径两者从没在同一
          会话里同时打开。
       2. 显示期间显示拓扑变了（拔显示器/改分辨率）不会重新锚点：实测一次分辨率抖动下气泡落在工作区外
          （`rightGap=-193`），产品没监听 `Screens.Changed`。它 8 秒后自己退场，所以危害有限但确实存在。
       3. 设置写入的通用弱点仍在：任何"写入后 150ms 内被 `LoadAsync` 覆盖"的设置都可能丢，本轮只让托盘提示
          这一条靠会话守卫免疫，没有把落盘改成同步写。
       4. 退场只有"到期"一条路：8 秒内点别处、或把主窗口叫回来之外都不会让它提前消失（设计如此，但没验证
          过用户不会把它读成"关不掉的窗口"）。

## 8. 用户协作偏好（务必遵守）

- **中文响应。**
- **实证优先**：任何"内存/耗时/性能"结论必须真跑真测给出实测数字，不许凭读码给理论（包括我自己的）。
- **UI 口味偏"素"**：删重复、删嵌套；铺开前先探一屏 + 截图过口味门。
- **评审后再推**：commit 本地、展示结果，不擅自 push。
- 面对从别处 fork 进来的代码，先问"到底要不要"，再谈"怎么维护"。

---
接手第一步建议：工作树已干净、两套 Windows 门（源码级 + 产物级）实测全绿，用户点名的四条必须功能（托盘、
自动输入、单实例守卫、首次收进托盘的一次性提示）都已出厂，且每一条都在**发布产物**上真机量过。下一轮从 §7
各条末尾的"仍未做/仍未验证"清单里挑，不必再花时间复验已绿的部分。

# 交接文档 — Monica Desktop (Avalonia) 商业化迭代

> 面向对象：接手继续开发的 AI。请冷启动阅读，假定你没有任何上文记忆。
> 最后更新：2026-09-24

## 0. 总目标（未达成，勿提前收工）

**"继续迭代，直到达到项目商业级可用水准。"** 这是一个持续性目标，不是单个任务。每一轮都要朝这个终态推进，不要因为某一步变绿就宣布完成。

## 1. 仓库与来源关系

- **本仓库**：`Monica-by-Avalonia`（桌面端，.NET 10 + Avalonia + FluentAvalonia）。
  路径：`C:\Users\joyins\Desktop\Monica-all\Monica-by-Avalonia\monica by avalonia`
- **只读事实来源**：`Monica for Android`（Kotlin）。桌面端从 Android 派生——**适配桌面去贴齐 Android 的数据形状，绝不反向改 Android**。
- 产品是一个**密码管理器**：核心安全边界是"绝不让明文密码落到截图 / 日志 / argv / 可分享产物里"。

## 2. 当前状态（工作树干净）

分支 `main`，`git status` 只剩给本节标提交号的文档改动。功能 HEAD = `73c89bc`（其后只可能有给本节自身标提交号的文档提交），近几轮：

下面这张表**不是按时间排的**：同一条线的行挨在一起（例如 Bitwarden 写回的两行 `94adacd` → `5cf27c9`），
读某一件事的来龙去脉时按主题往下连，不要按行号当时间线。

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
| `49560f5` | 自动填充弹窗（#89）＋两处只在出货平台上现形的缺陷：① **一键智能分派**取代"多解就拒打"（唯一匹配直接输入／多条匹配弹候选列表／零匹配弹全库并把键盘交给筛选框；枚举里 `NoMatch`／`Ambiguous` 已不存在），列表弹出期间目标窗口一个字符都不提前收到（`typedWhileOpen=False`），确认那一步先退场再把前台交回目标，退场一律带 `reason` 并写日志。② `IsDocumentControl` 窗口在 `Opened` **之前**投递焦点会丢——表现就是用户那句"这是填充不上吗？？？"：列表出来了、字母打不进去。③ **中文输入法把组合中每一次 KeyDown（含确认候选词的那一下回车）都报成 `ImeProcessed`**，写在 KeyDown 上的确认在真机上是死的；确认移到 KeyUp（实测仍带真实键）并**推迟到下一次 `ApplyFilter`**——那一下回车提交的文本还没落进筛选框，当场取行会发出筛选前显示的条目。诚实记录：这条只有无头证据，探针机器上没有组合态的输入法。④ 设置页录制的手势不再被加载层偷换：两槽撞车时保留用户录的那一下、由桌面集成如实报冲突（同目录同产物对照：修复前 `armed=True, gesture=Ctrl+Shift+Enter`，修复后 `armed=False, gesture=Ctrl+Shift+Space, registrationError=True`；钉子先按旧实现跑出红再转绿）。真机侧在**发布产物**上量全：弹窗 `Pick`/`Escape`/`SecondPress` 三式、注入正例 `matches=1 pickerSurfaced=False`、自定义手势 `Ctrl+Alt+F9` 从 `settings.json` 落盘读回并 armed。门禁：格式 0 改动、Release 0 warning、单测 9+759、UI 17+218 全绿；重新 publish 后产物门 loadMs=556/4000、KeePass 20000 条增长 3.7MB/24、锁/解循环 25/14/1/4。**一个不利信号如实记下**：同一份产物连跑四次锁定态尾窗中位 108.9/115.1/116.9/118.0MB（预算 120，最近的一次只剩 2.0MB，上一轮区间 107.8–109.0），该门路径根本不打开弹窗，没有证据指向 #89 也没有证据排除，下一轮先复跑取分布再归因、不要调阈值 |
| `45f01ef` | 滚动条压成 Win11 细条（#90，用户对上一轮外观的直接反馈）。**先证伪了"这里已经修好"**：`App.axaml` 里那三个看着对症的资源键（`ScrollBarSize=10`／`ScrollBarArrowSize=0`／`ScrollBarThumbBoxSize=30`）在真渲染树上一个数字都改不动，实测仍是 `bar bounds=…,14,200` 加两个可见的 `14x10` 箭头按钮；反射查到这三个名字归 `FluentAvalonia.Interop.WinRT.IUISettings`（Windows UISettings COM 互操作），应用资源从来不是它的入口——所以那三个键连同为它们编的注释一起删掉，改为在 `VaultShellStyles` 自持 `ScrollBar` 模板：12px 命中条、4px 居中圆角滑块（hover 时 8px）、轨道不着色、无线条按钮，分页按钮留着因此点通道仍然翻页；三个主题各配一根滑块画刷（`#73000000`／`#73FFFFFF`／高对比纯黑）。新增 `SlimScrollBarStyleUiTests` 3 条钉几何与"拖动仍然滚"（`bar.Value=120` → `ScrollViewer.Offset.Y=120`）；负控：把 `VaultShellStyles` 的 StyleInclude 注掉，两条立刻红在 `Expected: 12 / Actual: 14` 与"箭头 RepeatButton 存在"，恢复后全绿。**没有视觉证据**（截图口味门这轮两条路都不通，见 §7 最后一条），好看与否待用户在运行中的应用里确认。门禁：格式 0 改动、Release 0 warning、单测 9+759、UI 17+221 全绿；重新 publish 后产物门 loadMs=211/4000（库加载 actualMs=1366/4000）、KeePass 20000 条增长 4.9MB/24、锁定尾窗中位 110.5MB/120（同一产物共跑三次：110.5／113.9／104.6，上一轮四次的 108.9–118.0 没有继续上移，绝对水位仍未归因，见 §5）、锁/解循环 25/14/1/4 |
| `6613df6` | 忘记密码这条路的第一半：应急包（#91，用户拍"备份/应急包优先"）。**先量清为什么原有那半不够**：设置页的密保问题区块确实能设问题，但它那条"重设主密码"要求 `IsUnlocked`（`MainWindowViewModel.SettingsRecoveryCommands.cs:122`），也就是恰好"忘记密码时"走不通；而锁定态没有任何入口（`MainWindow.axaml:25` 锁定时只挂 `UnlockViewHost`，Settings 整个不可达）。本轮只做非破坏的一半：用一次性口令封存的加密快照，**Monica 从不保存该口令**（用忘记的主密码封存的备份在忘记主密码时一文不值），写盘前强制回读自检、不通过就不落盘，口令错的失败只说"打不开"、不外泄加密层原因也不回显口令，两个命令在锁定态一律 `VaultLocked` 拒绝，三个口令框打码且离屏/锁定时清空。**顺手抓到一个静默数据缺陷**：`AppSettingsService` 的 `Clone()` 是手写的属性清单，新增设置没在里面登记就会**无声**地从 `settings.json` 里消失——应急包元数据就是这么丢的；新守卫 `App_settings_file_carries_every_declared_setting` 把每个普通设置打上自身名字的标记后落盘重读逐值比对，实测先红在 `2 setting(s) did not survive the save: EmergencyKitLastExportedAtUtc, EmergencyKitLastFileName`（第一版守卫是**空的**：只比"是否变过"，而被丢的字符串序列化后仍是 `""`，改成比期望值才抓到）。③ 走查自己新写的文案又抓到一条：恢复流程复用了"正在加密保险库快照……"，改为独立的 `EmergencyKitRestoreInProgress`。覆盖：单测 9 条（密封性／跨库往返／口令错不导入且不泄密／弱口令不写盘／锁定拒绝／元数据落盘往返／进度文案／设置完整性／瞬态输入清理）+ UI 1 条（三个口令框 `PasswordChar='*'`、两个按钮的命令绑定与 `CanRunEmergencyKit` 随锁定态和维护态联动，并真按一次"口令为空"走到失败文案）。负控三条：去掉 `PasswordChar` 红在打码断言、去掉 `IsEnabled` 绑定红在锁定态禁用、进度 key 改回加密字样红在进度断言。**UI 侧两条新坑**：`dotnet test` 跑 `Monica.UiTests` 现在直接报 `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10`（只能跑产物 exe，见 §5）；设置页的绑定要生效，必须先把 `SelectedSettingsPage` 指到该页并 `Window.Show()`——根 `StackPanel` 的 `IsVisible=false` 会让整棵子树不被度量，`button.Command` 读回来是 `null`（差点写成"UI 测不到绑定"的假结论）。门禁：格式 0 改动、Release 0 warning、单测 9+769、UI 17+222 全绿；重新 publish 后产物门 `CANONICAL VAULT passed`、loadMs=190/4000（库加载 757/4000）、KeePass 20000 条增长 3.9MB/24、锁定尾窗中位 106.4MB/120（区间 105.7–107.8）、锁/解循环 25/14/1/4 |
| `bda943f` | 用户点名三片里的**片1**：passkey 存储 + 自证 relying-party 引擎。`Monica.Core/Passkeys/*` 是 Monica 自己的软件认证器与校验器（ES256/RS256 生成、authData 布局、CBOR 子集、clientDataJSON、`none` 证明，全部照抄 Android 的 `passkey/` 包），私钥改走**保险库会话密钥**加密后落新表 `passkey_private_keys`（schema 75→76），`passkeys` 行只留 `passkey_private_key_v1_` 引用，清库语句同步加 `DELETE FROM passkey_private_keys`。写的时候抓到并修掉一处真缺陷：`PasskeyStore.Validate` 把 `PasskeyRpId.Normalize(...)!` 直接赋回 `entry.RpId`，遇到 `".."`／纯点会归一化成 null 再写库，报的是 SQLite `NOT NULL constraint failed` 而不是参数错误——**`!` 把"归一化可能失败"吃掉了，只有真喂一条脏输入才暴露**。边界要说清：引擎目前是**纯库**，`App.axaml.cs` 注册了 3 个 singleton 而没人解析，所以 UI/产物门并不替 passkey 背书，背书的是那 42 条单测；细节见 §7 的"Passkey 三片"。门禁：格式 0 改动、Release 0 warning、该轮 820 条单测全绿；重新 publish 后产物门 loadMs=595/4000、KeePass 20000 条增长 5.7MB/24、锁定尾窗中位 112.9MB/120、锁/解 25/14/1/4 |
| `94adacd` | **Bitwarden 写回的载荷编码器（#97，接 #94 缺口 1 的前半）**。#94 审计量出来的事实是：推送管道其实已经齐了（协调器先推后拉、传输层把 `PayloadJson` 原样 POST/PUT 到 `/ciphers`、处理器管领取/完成/冲突备份、队列在读取时解密，见 `BitwardenPendingOperationStore.Mapping.cs:33`），**缺的只有生产者**。新增 `BitwardenCipherPayloadBuilder.cs`（359 行，含请求 DTO）。**落点纠一处（下一轮接线时推翻的）**：这里原本写的是"放 Platform 而不是 Core"，理由是载荷形状必须是 `BitwardenCipherDecoder` 的**逆**、只有同侧才能复用那套 internal 的 vault DTO。那个理由是错的：生产者在 Data 层（只有 Data 同时拿得到仓库和同步队列），而 **Data 不能引用 Platform**，编码器留在 Platform 就永远接不上线。文件已 `git mv` 到 `src/Monica.Core/Bitwarden/`，`BitwardenHttpContent.JsonOptions`（Platform internal）改由 Core 侧一份 `new(JsonSerializerDefaults.Web)` 顶上。两份选项的差别只有读取端用的 `PropertyNameCaseInsensitive` 与显式 `MaxDepth=64`，写侧行为同源；而"Core 写得出来、Platform 读得回去"**不是靠读代码认定**的——测试里那份 JSON 正是用 Platform 自己的 `BitwardenHttpContent.Deserialize<VaultCipherDto>` 读回来、再喂生产解码器（`BitwardenCipherPayloadBuilderTests.cs:237`），指纹相等才算数。逆关系仍由同一批往返测试守着（测试同时引用两侧，Platform 的 `InternalsVisibleTo Monica.Tests` 因此还在用）。规则是**只写解码器会读回来的字段**（title/website/username/password/notes/totp + 自定义字段 + 历史 + passkey），其余本地列不进载荷——进了就等于宣称远端有，实际没有。自证用的是合并引擎真正比较的那个值：`Assert.Equal(ForPassword(源), 解码回的 Metadata.PayloadHash)`，因为指纹一旦不等，每次同步都会被当成冲突。核心断言（title/website/username/password/notes/totp/favorite/folderId/type/字段值/历史值与时间戳）**全部走生产解码器**：把编好的 JSON 反序列化成 `VaultCipherDto` 喂给 `BitwardenCipherDecoder.Decode`，为此给 Monica.Platform 开了 `InternalsVisibleTo Monica.Tests`（Core/App 同例）。拒绝表 8 条：非 login 类型（2/3/5）、SSH/SSO/WiFi 登录型、带附件、回收站条目、空标题、自定义字段空名、passkey 绑定畸形、passkey 缺 credentialId。**为什么是拒绝而不是省略**：Bitwarden 的 update 是整体替换，省略 `fido2Credentials` 等于把用户远端的通行密钥删掉，宁可不推。另有 2 MiB 上限，与 `BitwardenMutationHttpTransport.MaximumMutationBytes` 同界（两处各写一份常量，收紧时是 fail-closed 不是放行）。passkey 那条单独自证：源 JSON 与解码回来的 JSON **逐字符相等**（解码器固定 13 个键的顺序，编码器按名取值）。**负控做了而且打中过**：把 `Notes` 改成 `null` 后 11 条里 1 条立刻红在 `Assert.Equal: Strings differ`，恢复后 11/11 绿——证明往返不是空过。门禁：格式 0 改动、commercial-release 绿（单测 846 走常规通道 + 9 条 `perf-budget` 走独立顺序通道、UI 17+226 全绿、Release 0 warning）、重新 publish 后 jit 产物真跑绿（loadMs=143/4000、KeePass 20000 条增长 5.3MB/24、锁定尾窗中位 112.2MB/120、锁/解 25/14/1/4）。**本轮两条红都是负载计时假红，没动阈值**：树重建预算在 855 条并行混跑时红（隔离复跑 3/3 绿、每次约 1 秒 vs 400ms 预算），`Background_memory_minimize...` 在整串 UI 套件里红在 2871ms/2500ms（隔离 3/3 绿）——正是已有的 perf 抖动类。**两条老实的边界**：①编码器还没接线，绑到 Bitwarden 的本地编辑仍然不会入队（#98 记下的收口设计是"`SavePasswordAsync` 不读旧行，需要新增 `bitwarden_synced_fingerprint` 列"——**落地时没走那条**，改成同步基线侧表，理由见下一行）；②从头到尾没对真服务器验过（#94 缺口 4），需要用户点头才动 |
| `5cf27c9` | **把上一行缺的那个生产者接上线（#98，顺手收掉 #94 缺口 1 和 3）**。事实核对过：推送管道本来就是通的（协调器先推后拉、传输层原样 PUT `/ciphers`、处理器管领取/完成/冲突备份），`EnqueueAsync` 在 src/ 里零调用点才是唯一断口。新增 `src/Monica.Data/Bitwarden/BitwardenLocalChangeQueue.cs`，挂在协调器 `Uploading` 阶段、**拉取之前**。**为什么只能比内容指纹**：#94 已经量过普通编辑路径从不置 `BitwardenLocalModified`，所以"这台设备改了、从没上传过"没有别的信号可用；而比对需要一份"上次同步时远端与我各是什么"的存档，那份存档原本不存在 ⇒ 新表 `bitwarden_sync_state`（schema 76→77）。**为什么是侧表不是加列**（推翻上一行记的那份设计）：`password_entries` 有 53 列、写入管道有编辑/克隆/导入/回收站/WebDAV 冲突副本五处，逐处改的半径远大于收益，而且 `secure_items`（27 列）还得再来一份；侧表主键 `(bitwarden_vault_id, cipher_id)` 一次覆盖两类。**基线不加密**：`payload_hash` 只是哈希，与仓库里已有的 `bitwarden_cipher_metadata.payload_hash` 同函数同哈希空间，再加一层只会让推/拉两侧各算一遍。**基线只有一个写入口**：完整快照拉回后 `ReplaceForVaultAsync` 整表替换——全仓 `ApplyAsync` 只有协调器全量路径一个调用点（实测 grep 出来的，不是假设），所以不存在增量同步误删基线的路径；能直接取快照指纹而不重解密，是因为 `ValidateDecodedPayload` 本来就硬校验 `ForPassword(解码明文) == metadata.PayloadHash`。**两条不变量各有测试**：①空基线 ⇒ 直接返回 (0,0)，第一次同步绝不把全库当成本地改动推上去；②**推送成功后必须推进基线**（处理器 `ApplySuccessAsync` 里 `AdvanceAsync`，配 `bitwarden_pending_operations.local_payload_hash` 新列），不推进的话 `EnqueueAsync` 的 `ON CONFLICT(idempotency_key) DO UPDATE SET status='pending'` 会在下一次同步把已完成的那条**复活**，变成每轮重推同一份内容。幂等键 `local-update:{vault}:{cipher}:{内容哈希}` 因此带哈希：同一次改动不重复排队，改一次排一次。**没有基线行的身份不推**（那是远端快照没确认过的条目，推上去等于把服务器已删的 cipher 复活）。`BitwardenProtocolException`（SSH/SSO/WiFi 登录型、附件、回收站、非 login 类型）记成 `Refused` 而不是让整次同步失败；躲过的陷阱是把 secure item 也交给 `BuildLoginCipher`——它只吃 `PasswordEntry`，抛的是 `ArgumentNullException` 而不是可捕获的协议异常，所以 `candidate.Entry is null` 必须先挡。密钥不另存：协调器用（可能刚刷新过的）会话 lease `CreateVaultKey()`。**扫描代价从"没预算"改成量过的门**：2,000 条绑定条目的静默扫描实测 214-272ms（四次），perf-budget 通道设 800ms，**负控真打**——把两次批量读改成逐条读实测 16,604ms、门即刻红，撤销后 10/10 绿。覆盖：`BitwardenLocalChangeQueueTests` 9 条（真 SQLite + 真仓库 + 真 pull，基线就是同步实际留下的那份），包括先钉"pull 留下的基线指纹 == 存储行指纹"（两个哈希空间一旦分叉就是永久冲突循环）、推送成功后下次扫描安静且再改一次又欠一条（共 2 行）、409 被拒 ⇒ 基线不动且下次仍欠 1 条、schema 77 降级重建后欠的工作不丢（`LocalPayloadHash` 回 `DBNull`，重建基线后同一行带哈希重新排队）。写测试踩到两条：裸 ADO reader 返回 `DBNull.Value` 不是 `null`（`Assert.Null` 红在 `Actual:` 空值）；`Assert.Equal(1, list.Count)` 被 xUnit2013 拦下，要用 `Assert.Single`。**合并引擎那条 `sameRevision && !sameState` 冲突备份从"本地编辑的常规归宿"降级为"推送失败的兜底"**——这是 #94 缺口 3 的收口。**诚实边界（仍然）**：secure item（笔记/银行卡/证件）没有编码器，只计数不推；本地**新建**条目没有 cipher id 永远不推；本地**删除**不传播（`BitwardenCipherPayloadBuilder.cs:88` 拒收 `IsDeleted`），从回收站恢复则指纹回到基线、本来就不需要推；只改归档状态对漂移不可见是故意的（指纹不含 `IsArchived`，加进去会让升级后第一次同步把远端的归档盖回本地）；基线只由**完成的 pull** 写入，从未同步过的账户不会凭空产生上传。门禁：格式 0 改动、commercial-release 绿（单测 9+854、UI 17+226、Release 0 warning）、重新 publish 后 jit 产物真跑绿（CANONICAL VAULT、loadMs=134/4000、KeePass 20000 条增长 5.4MB/24、锁定尾窗中位 107.1MB/120 区间 106.7–108.5、锁/解 25/14/1/4）。**perf-budget 那条新门与常规通道是在产物门之后加的，加完各自复跑过（10/10 与 854/854），没有重跑整串 commercial-release**。**仍然从未对真服务器验过**（#94 缺口 4），需要用户点头才动 |
| `59c0fac` | **撤掉一个从不该存在的生产者（#99 的前半，接 #98）**。#98 之后 409 会真的走到 `SaveConflictAsync`，于是"冲突备份有表没有界面"从数据安全议题变成用户眼前会堆出来的行。先把两条写同一张表的路径各跑一遍再判：**实测**——连跑三轮"推送被 409 拒"，`GetUnresolvedAsync` 回 3 行（Id 1/2/3），载荷是**出站的远端 cipher 形状** `{"type":1,"name":"2.…","favorite":false,"login":{…}}`（camelCase，`BitwardenCipherPayloadBuilder` + `JsonSerializerDefaults.Web`）；pull 那侧写的键是 `customFields,password,passwordHistory`（内层 `PasswordEntry` 走默认选项 ⇒ PascalCase `Title`）。**两份共用同一个 `item_kind="password"`**，而还原按 `item_kind` 选解码器 ⇒ 推送那份**永远读不回去**，并且每失败一轮多一行。判据不是审美：被拒的推送**什么都没销毁**（编辑还在屏幕上、还欠着），所以它连"备份"的动机都不成立 ⇒ 删生产者，不加分辨列、不加迁移、不写第二种解码器。`BitwardenMutationProcessor` 不再持有 `IBitwardenConflictBackupStore`，`RecordFailureAsync`／`SaveConflictAsync`／`LocalItems.TryFind` 一起撤，失败直接记给 `operationStore.RecordFailureAsync`；不变量写进类注释（**只有 pull 在覆盖本地内容之前备份**），免得下一轮又加回来。覆盖：4 条新单测——三轮被拒 ⇒ `Assert.Empty`（先红在 3 行、撤销修复后绿）／形状探针逐键断言"还原所读的那份就是 pull 写的那份"（先红在两份键集并列）／还原回来的是**完整的**（自定义字段 + 历史都在、revision 保住）且下一次同步真把它推上去（1 条排队 → 1 条完成 → 下次扫描安静）／放弃只删备份不欠东西。`BitwardenMutationProcessorTests` 里"409 留下一份备份"那条断言改成"不留"。门禁：格式 0 改动、单测 858 常规通道全绿（该轮 854→858） |
| `73c89bc` | **冲突列表与还原出厂（#99 的后半，收掉 #94 缺口 2）**。新增 `src/Monica.Data/Bitwarden/BitwardenConflictRestoreService.cs`（`IBitwardenConflictRestoreService`：列摘要／还原／放弃）+ 同步页一段冲突列表（`MainWindowViewModel.BitwardenConflicts.cs`）。**还原判型看载荷、不看 `item_kind`**（上一行量出来的原因：那一列两种形状共用）；password 分支把备份里的 `PasswordEntry` + 自定义字段 + 历史原样写回，**revision 保留这台设备上一次确认的那个**而不是备份里的陈旧值（revision 是指向服务器历史的指针，不是内容），随后置 `BitwardenLocalModified=true` ⇒ 下一次漂移扫描把它排队推上去，这才叫"拿回来"而不是"再输一次"。**行上只有标题／类型／保存时间**：载荷里是明文字段值，一个列冲突的界面没有任何理由渲染它（`GetSummariesAsync` 只从载荷里读 `Title`）。文案 10 个 key 中英各一份，逐行 `Reason` 不显示——修完之后一行存在的理由只剩一种，section 级说明够用。**接界面时先量到一条真缺陷**：读列表是 fire-and-forget，切账户时上一份读还在跑 ⇒ 实测两行（vault 7 的备份混进 vault 8 的列表），那一行的还原会拿新账户去查一个它不拥有的 id（`GetUnresolvedAsync` 抛 `KeyNotFoundException` → 用户看到"还原失败"）。负控就是那条红：`Assert.Single() Failure: The collection contained 2 items`。修法是每次读带一个最新请求号（`Interlocked.Increment` + 落地前比对），只有最新那次能写列表。覆盖：单测 4 条（上一行）+ UI 3 条——列表随选中的账户出现、还原命令真到达服务且列表清空、**模板里那颗按钮**（`{Binding #BitwardenSyncSourceRoot.DataContext.…}` 这一跳只有真物化模板才证明得了：`window.Show()` 后从视觉树取按钮，断言 `Command` 就是 VM 那个命令实例、`CommandParameter` 就是那一行、`CanExecute=true`），另加放弃分支一条（打到被选中的那一只、不碰还原、清行）。**顺带记两条 Avalonia 实测**：`RaiseEvent(new RoutedEventArgs(Button.ClickEvent))` **不会**触发 `Command`（本仓既有的 4 处点击测试全部点在 `Click=` 事件处理上，照抄会静默拿到 `Restored=[]`），所以断言落在命令对象与参数上；账户列表 `Clear()` 会让 ComboBox 把选中项倒一遍 null ⇒ 每次刷新多读一次，因此测试里那一行必须在 `RunJobs()` 之后取。门禁：格式 0 改动、commercial-release 绿（单测 858 常规 + 10 `perf-budget`、UI 230 常规 + 17 `perf-budget`、Release 0 warning）、重新 publish 后 jit 产物真跑绿（`CANONICAL VAULT passed`、loadMs=480/4000、KeePass 20000 条增长 2.3MB/24、锁定尾窗中位 104.0MB/120、锁/解 25/14/1/4）。**仍未做**：笔记/银行卡/证件没有写回编码器 ⇒ 还原它们只回到本地、推不到远端；本地新建条目仍无 cipher id 不推；本地删除不传播；**全程从未对真 Bitwarden 服务器验过**（#94 缺口 4，要用户点头） |
| `49431a6` | 细滚动条滑块被主题压扁的真因与修法（#93，接 `08c3adb` 记下那个 2px 打脸读数）。**读数不是采样错，是真缺陷**：在真 workspace 的渲染树里读到滑块带一个 FluentAvalonia ScrollBar 主题给的 `scaleX(0.35) translateX(-2px)`——它按模板里 `Thumb` 的**精确类型**命中，用来伪造 WinUI 细条，而这个值的优先级既压过应用层 `Style` setter 也压过 `ControlTemplate` 上的属性值（`RenderTransform="{x:Null}"` 与 `Value="scale(1)"` 两条试法都被实测否掉）。落地的办法是派生 `Monica.App.Controls.SlimScrollBarThumb`：类型选择器只匹配精确类型，派生类因此逃出钳制。修后离屏 1.0/1.5 两次采样都是 4 DIP→6 设备像素、亮度 137（`#73FFFFFF` 压在 40 上应有的值），真屏抓取 `span=1164..1169 maxRun=6` 一致，`:pointerover` 加宽规则确认仍生效（margin 2,0 → 8 DIP → 12 像素），矩阵回到 `distinctFrames=13`。**纠一处 §7 第 4 条的旧解释**：那句"测试是在裸 ListBox 上量的所以证明不了"只是部分对——裸 ListBox 同样吃应用层样式，几何 4 DIP 布局宽是真的，`Bounds` 不含 `RenderTransform`，所以纯几何断言看不见这次钳制；换掉的像素断言在无头下永远量不到（帧缓冲全零），现在正向钉机制（模板滑块必须是 `SlimScrollBarThumb` 且 `RenderTransform` 为 null）。负控：模板改回裸 `Thumb` 后该 4 条里 2 条立刻红在类型与几何上，恢复后全绿。门禁：格式 0 改动、单测 844、UI 243 全绿，重新 publish 后 commercial-release 与 jit 产物真跑门（loadMs=143/4000、KeePass 20000 条增长 1.8MB/24、锁定尾窗中位 111.6MB/120、锁/解循环 25/14/1/4）全绿 |
| `dda2766` | 三片里的**片3：自动输入序列可配置**。原来那条流程写死"用户名 Tab 密码"，回车和不太规矩的登录框都得用户手动补。新增 `src/Monica.App/Services/AutoTypeSequenceParser.cs`（文件名带 Parser 是**没办法**：设置属性和 VM 都叫 `AutoTypeSequence`，同名 class 会被遮蔽），token 拼写用 KeePass 的 `{USERNAME}{TAB}{PASSWORD}{ENTER}{DELAY:ms}`。**为什么是这个拼写而不是 Android 的**——量过 Android 现状：那边根本没有序列解析器，`{USERNAME}{TAB}{PASSWORD}` 只是 `res/values/strings.xml:4605` 的一条 UI 提示，KDBX 的 `AutoTypeData` 读了存了但没有任何人消费，IME 填充写死、从不发回车、没有 `{DELAY}`、也没有按条目一列。所以贴齐的是**数据形状**（同一条语法，将来加按条目覆盖不用换语言），而"全局一条设置、不加 DB 列"是当前的诚实边界，按条目覆盖仍是待办。规则：整条模板要么全解析要么整体拒绝，未知 token／未闭合／非法延时都原样回显用户打的那个 token，**绝不部分生效**；缺字段的行会把它相邻的那一串 Tab 一起丢掉（两遍 `dropped[]` + `IsNextToDroppedField`），免得把光标敲飞；延时上限 5000ms 抽成 `AutoTypeLimits`，解析器和注入服务共用同一个数，不再是两处各写一份。**按下时再解析一次**：`settings.json` 可能在关闭期间被手改，设置页那个绿色勾不构成按下的许可证，新增 `SequenceInvalid`／`NothingToType` 两个结局都保证零按键。文案坑：`L[key]`/`Get` 返回原文所以花括号安全，但**带 `{...}` 的字符串绝不能当 `Format` 模板**，错误提示走 `AutoTypeSequenceInvalidFormat`（"……：{0}"）把 token 当**参数**传。设置持久化按上一轮的教训在 `Clone()` 里登记了，且 normalize 只补**空**值——解析不了的序列原样保留并报错，不静默改写用户输入。覆盖：单测 20 条（默认不发回车／缺字段与相邻 Tab 规则／字面量／大小写／10 行拒绝表含 `{DELAY:5001}` 与 `-1`／不成形 keystroke 序列／超长）+ UI 3 条（序列原样到达注入器、坏序列零按键且报出 token、设置页字段与错误行的绑定联动）；UI 门从 222→225。**两条负控里有一条打了脸**：去掉 `Mode=TwoWay` 测试**不红**——Avalonia 的 `TextBox.Text` 默认就是 TwoWay，这条得记下来；第二条（把错误行写死 `IsVisible="True"`）红在开头那句 `Assert.False`，所以测试有牙。**又踩了一次已知坑**：新 UI 测试读回 `null`，就是因为没先 `SelectedSettingsPage = "Desktop"`（§2 上一行已经写过）。产物真跑（发布 exe，每次一份全新 `MONICA_APPDATA_DIR`，只报布尔值和长度）5 次：默认→Typed 且 submitClicks=0/431ms、`{...}{DELAY:2000}{ENTER}`→submitClicks=1/2211ms、纯字面量→两个框 21/24 字符逐字相符、`{capslock}` 与 `{DELAY:9000}`→SequenceInvalid 且零按键。探针侧新事实：`--init`/`--seed` **不会**写 `settings.json`，要改序列得自己建那个文件（`appExit=1` 是 `--smoke-ui-exit-after-checks` 的既有退出码约定，不是失败）。门禁：格式 0 改动、Release 0 warning、单测 9+835、UI 17+225 全绿；产物门（本轮 publish，其后只加了一条 UI 测试、产品代码未再改动）`CANONICAL VAULT passed`、loadMs=197/4000、KeePass 增长 5.1MB/24、锁定尾窗中位 107.1MB/120（区间 106.5–108.4）。**仍留一条老实话**：切语言时序列错误文案会重译，但 `AutoTypeStatusDescriptionText` 不会——那是本轮之前就有的，没有一起改 |

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

最近实测（`6613df6`，两套门全绿）：

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

# `dotnet test` 只对 Monica.Tests 有效；对 Monica.UiTests 会直接报
# `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK`
# ——UI 套只能跑上面那个产物 exe。整类跑用 `-class Monica.UiTests.SettingsSecurityWorkflowUiTests`
# （`--filter` 这个选项在 v3 in-process runner 上不存在，会报 unknown option 后什么都不跑）。

# UI 测试方法名用下划线分词，所以片段要写 "*Auto_type*"；写成 "*AutoType*" 会静默匹配 0 条。

# 门禁脚本自身也受 5.1 约束：`[IO.Path]::GetRelativePath` 在 .NET Framework 上不存在，
# 所以"文件超行数"那条违规原先在这里抛 MethodNotFound，既不变红也不点名超线文件（已改回字符串拼接）。

# 后台跑的门被会话中断杀掉时，会留下 testhost.exe 孤儿仍然占着临时库文件；
# 下一次 commercial-release 会在 `CleanupMonicaTestTempRoots` 处报
# `MSB3231: Unable to remove directory ...monica-tests\<pid>-<hash>... being used by another process`
# —— 这是环境残留不是产品红（实测：门自己已经打印过 9/9 与后续 build succeeded）。
# 对策：`tasklist | grep testhost` 拿到 pid，核对 CommandLine 里的路径确属本仓，再 Stop-Process 重跑。
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

实测数字（本轮 #91 应急包之后）：门禁全绿——`dotnet format --verify-no-changes` 0 改动、
`--warnaserror` 0 warning、单测 9 perf + 769 functional、UI 17 + 222；重新 publish 后产物门
`CANONICAL VAULT passed`、`loadMs=190/4000`（库加载 `actualMs=757/4000`）、KeePass 20000 条增长
`3.9MB/24`、锁定尾窗中位 `106.4MB/120`（尾区间 105.7–107.8，十拍轨迹 113.6→107.3 一路向下）、
锁/解循环 25/25 密码 + 14/14 笔记 + 1/1 TOTP + 4/4 钱包。**应急包没有真机侧证据**：无头/单测覆盖了
密封性、跨库往返与失败不泄密，但"用户在文件对话框里真的存下这个文件、再选回来恢复"这条只在测试替身上
跑过，真实 shell 对话框未点过。

上一轮（#90 滚动条）记录：单测 9+759、UI 17+221，产物门 loadMs=211/4000（库加载 1366/4000）、
KeePass 4.9MB/24、同一产物三次锁定尾窗中位 `110.5 / 113.9 / 104.6`MB（预算 120，三次全绿；run1 尾区间 110.4–119.9、
run3 轨迹里出现过 `112.7→104.5` 的一步下降，说明尾窗仍在等一次原生释放落地）。分布与更上一轮
（`49560f5`：四次 108.9–118.0）同量级、没有继续上移，一根滚动条模板也解释不了 10MB。**但绝对水位仍然偏高、归因仍未做**（锁定态只有 26–28MB 是托管的，其余是
自包含运行时镜像映射 + Skia/GPU 表面 + 线程栈，往下压要走 trimming/AOT `#43`），别把这条当已解决。
注意：perf-budget 通道在整串门里紧跟 `dotnet build` 起跑时读到过 483ms（预算 400），单独复跑三次为
9/9 全绿；这是冷启动+构建负载的单次读数，按仓库规则先复跑取分布，不要调阈值。

真机自动输入门（**不在 CI 里**，需要交互桌面 + 外部目标窗口 + 真实按键）：

```powershell
# 正例：默认标题命中 seeded 的 github 条目，脚本自己敲 Ctrl+Shift+Enter，再看两个输入框落成什么
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/verify-autotype-injection.ps1 `
  -ExePath <publish>\Monica.App.exe -AppDataDirectory <空临时目录> -ExpectedOutcome Typed
# 弹窗四式：筛到一条→回车打；Escape→退不打；列表还开着再按一次手势→"算了"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/verify-autotype-picker.ps1 `
  -ExePath <publish>\Monica.App.exe -AppDataDirectory <空临时目录> -Action Pick
... -Action Escape
... -Action SecondPress
# 录制自定义手势（模拟设置页落盘后重启）：把组合写进该目录 settings.json 再按那组键
... -Action Pick -Gesture "Ctrl+Alt+F9"
```

坑（都是踩过一次的）：

```powershell
# 1. -ExpectedOutcome 的合法值只剩 Typed／PickerForAllEntries／PickerForMatches／MonicaIsForeground，
#    智能分派之后 NoMatch／Ambiguous 已不存在，传旧值只会得到一条说不清的红。
# 2. settings.json 只有 GUI 首启才会写出来，--init-empty-smoke-vault 与 --seed-smoke-vault 都不写。
#    所以 -Gesture 必须落在一个"已经被 GUI 跑过一次"的目录上，探针找不到文件就直接 throw，不自己造默认值。
# 3. 探针宿主必须 SetProcessDPIAware()（本机 150%），否则点/量到的是缩放后的错位坐标。
# 4. keybd_event 必须带 MapVirtualKey 的真实 scan code：RegisterHotKey 不在乎，但 WM_CHAR 是系统按
#    scan code 造的，0 scan code 打不进筛选框。
# 5. 从 Git Bash 传路径给 -File 脚本时别写 "$env:TEMP\..." —— bash 会先把 $env 吃掉变成空串，
#    PowerShell 收到的就是字面量；要传绝对 Windows 路径。
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

安装链路真机门（**gitignored，只在一次性目录上跑**；跑之前务必确认发布产物是当轮重编的）：

```powershell
# 1. 出一份只改 AppId 的测试 setup。AppId 在 .iss 里写作 AppId={{GUID}（两个开括号是 Inno 的转义），
#    替换值也必须带这个转义，否则 ISCC 报 Unknown constant；脚本对"替换没命中"直接 throw。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/build-install-test-setup.ps1
# 2. 装到一次性目录 → 对账载荷 → 读回 exe 版本/ARP/快捷方式 → 卸载 → 查残留
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/verify-windows-install.ps1 -Phase install
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/verify-windows-install.ps1 -Phase uninstall
# 3. 量"某个真安装目录是否自洽"（载荷齐不齐、ARP 是否指向该目录、快捷方式在不在）。
#    -CheckOnly 只量不装，用来先拿红基线再拿绿。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File artifacts/autotype/reinstall-on-d.ps1 -CheckOnly
```

判读要点：install 阶段那条 `untouched-check` 是**真的红过**的（它抓到过一次 AppId 撞车把机器上另一份
安装的 ARP 键抢走），别因为"我这次没碰那份"就删掉它；`extra` 只允许 `unins000.dat`/`unins000.exe`。

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
  - **抓取会冻结，但矩阵本身是好的**（#92 实测）：修之前 7 次真跑里 **1 次** 13 帧全同（`distinctFrames=1`、
    都是解锁前的那一帧），另外 6 次含两次 9-22 的历史运行都是 **13/13 帧帧不同**。冻结与启动方式
    （bash / `Start-Process` / 是否带 `--smoke-ui-exit-after-checks`）无关，也与窗口可见性无关
    （外部每 60ms `ShowWindow(SW_MINIMIZE)`  hammer 仍然 13/13），**根因未定位**。
  - 现在 `RunSmokeUiOtherPagesScreenshotsAsync` 先跑 `WaitForLiveSmokeCaptureAsync`：切一次 section、
    连抓两帧比对，4 次都不重绘就记 `failures=capture-stale` 并**一帧都不写**。
    实测：正向 5/5 次 `liveness proved attempt=1` + `distinctFrames=13`；
    负向（临时用环境变量掐掉那次切换）`did not repaint attempt=1..4` → `capture-stale`、`frames=0`。
    抓取同时改成 `bitmap.Render((Visual)Content!)`（渲染 Window 自身会拿到它的顶层绘制组）。
    改后 5/5 次都是活的，但冻结本来 7 次才复现 1 次，**所以这条改动的因果还没被证明**，
    真正兜底的是上面那道 liveness 门。

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
- **安装链路本轮真跑过一次**（用户 2026-09-22 拍"装 d 盘"，2026-09-23 又拍"本机统一放 D 盘"）：
  - 换掉 AppId 的测试 setup → `D:\Monica Install Test`：exit 0 / 14.3s、落地 317 文件 386.5MB、与发布
    目录 315 文件对账 `missing=0`（`extra` 只有 `unins000.dat`/`unins000.exe`）、`FileVersion=0.1.0.0`、
    `ProductVersion=0.1.0-ci.0`、ARP 与开始菜单快捷方式都在；
  - 对**已安装目录**而不是构建目录跑 `verify-artifact-runtime`：`CANONICAL VAULT passed`、loadMs=238/4000、
    锁定态 112.0MB/120、KeePass 增长 3.7MB/24、锁/解 25 passwords、14 notes、1 totp、4 wallet；
  - 静默卸载：exit 0 / 26.4s、目录与 ARP 键和快捷方式全部消失、`clean=True`、`C:\Program Files\Monica`
    全程 317→317 未动；
  - 官方 setup 装到 `D:\Monica`：9.1s、315/315、运行时门 `RUNTIME SMOKE passed`、loadMs=269、
    锁定态 114.6MB/120。**这条之前先踩到一个坑**：`artifacts/package` 里那份 09/22 11:00 的 setup 载荷
    比 `aaa698b`（09/23 02:27）旧，装上后读回的是 `ProductVersion=0.1.0-ci.0+HEAD` 而不是含 #87 修复那份。
    setup 编译本身不会告诉你这件事——必须先有当轮的 `publish-desktop` 产物，再 `package-windows-inno`。
- **Inno 的"一条键、一个快捷方式"语义（不装第二次不会知道）**：① 一台机器上一个 AppId 只有**一条**
  Add/Remove Programs 键（`<AppId>_is1`）。往第二个目录再装一次同 AppId，这条键会被改指到第二处，第一处
  立刻失去卸载入口——它的文件都在、磁盘上的 `unins000.exe` 也都在，只是"设置 → 应用"里那扇门没了。
  ② 开始菜单快捷方式路径 `{autoprograms}\Monica\Monica.lnk` 同样是**共享**的：卸载任一份安装会把另一份
  的启动路径一起删掉（本轮实测删出来过一次：目录 540 文件完好而 `shortcut exists=False`）。所以"验证安装"
  必须用**不同 AppId** 的副本，`build-install-test-setup.ps1` 就是为此存在的。
- **`/DIR` 带空格时引号的位置**：Inno 只认 `/DIR="D:\a b"`。`Start-Process -ArgumentList` 传数组时把引号
  加在整个 token 外面（`"/DIR=D:\a b"`），Inno 在空格处截断后**照装别的目录**：实测 exit 0、14.3 秒、目标
  目录 0 文件。传单个字符串而不是数组才是文档要的形式。
- **标准用户免提权安装**：用户 2026-09-23 拍**保持 admin-only**，`.iss` 继续不开
  `PrivilegesRequiredOverridesAllowed`；装进 `Program Files` 需要点 UAC 是预期行为。
- **本轮遗留的机器状态（不是代码问题）**：`C:\Program Files\Monica` 现在是 317 文件 386.4MB 的**孤儿目录**
  ——ARP 键按用户意愿指到 `D:\Monica`，所以 C 那份没有卸载入口但功能正常，删不删等用户点头。
  `D:\Monica` 里另有 225 个不属于本次载荷的文件，是用户 2025-12 那次旧安装留下的。
- **两条探针死路（别再试）**：① Inno 的 `/EXTRACT` 退出 0 但零文件落地，不可信；
  ② 用 `icacls /deny *S-1-5-32-545:(OI)(CI)W` 模拟"只读安装目录"无效——deny 继承到镜像文件上，
  连 `where.exe` 都起不来（实测同样 Access is denied），它测的是加载器不是产品；
  ③ 用"最近 N 小时被写入的文件"判断安装器把载荷落到哪了不可信——Inno 复制时**保留源时间戳**，
    实测整份 315 文件里只有它自己生成的 `unins001.exe`/`.dat` 显示为新。要判落点只能按名字对账载荷。
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
- **用户点名的必须功能：五条都已出厂**（托盘 `f700bcc`／自动输入 `1d8c3a4`／自动填充弹窗与快捷键录制 #89、
  见第 2 条／单实例守卫 `541069c`／首次收进托盘的一次性提示，见第 4 条；2026-09-22 拍板的前四条在此列）。
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
       而标题是 `github.com.phishing.test` 就**不算匹配**；标题不含 host 才退回整词标签匹配。**一键智能分派**
       （#89 拍板的形状，取代了早期"命中 0 条或多于 1 条一律拒打"）：唯一匹配→直接打（`Typed`）；多条匹配→
       弹带筛选项的列表（`PickerForMatches`）；零匹配→弹全库列表并把键盘交给筛选框（`PickerForAllEntries`）。
       枚举里**已经没有** `NoMatch`／`Ambiguous`，别再照旧文档传这两个值。
     - **真机实测**（`artifacts/publish/win-x64/jit/Monica.App.exe`，`MONICA_APPDATA_DIR` 指向空临时目录，
       WinForms 目标窗体两个输入框 + 外部真实按键），脚本 `artifacts/autotype/verify-autotype-injection.ps1`
       （gitignore 目录；只报布尔和长度，绝不打印凭据明文）：
       1. 正例（标题 `Sign in to GitHub - github.com`）：`armed=True, gesture=Ctrl+Shift+Enter,
          registrationError=False` → `outcome=Typed matches=1`，落点
          `firstLength=15 firstMatchesUsername=True secondLength=19 secondMatchesPassword=True`，`appExit=0`；
          `owner check: targetPid=58400 harnessPid=58400 appPid=11504` 证实字确实落在**别人的**窗口。
       2. 反例（钓鱼标题 `Sign in - github.com.phishing.test`）：智能分派出厂前量到的是 `outcome=NoMatch matches=0`、
          两个框都空（`firstLength=0 secondLength=0`）。**该分支已被上面的分派取代，这条反例现在会走
          `PickerForAllEntries`（标题里的 host 被钓鱼域判掉→零匹配→弹全库），本轮没有重量钓鱼专项**，
          下一轮要按新形状重跑一次再引用。
     - **弹窗（#89 已出厂，四条手势都在发布产物上真机量过）**：`AutoTypePickerWindow` 是一棵
         `IsDocumentControl=true` 的裸窗口（安卓侧的"无阴影、无标题栏"形状），锚在屏幕水平居中、
         `ScreenVerticalRatio=0.28`，列表 + 筛选框 + 计数。三个必须功能点在代码里都有钉：
        - **键盘交给谁**：`FocusForMode(filterFirst)` 必须等 `Opened` 之后再投递焦点（`_isOpen` +
          `DeliverFocusRequest()`），否则在 `IsDocumentControl` 窗口上焦点投递会丢，弹窗只是一张图片。
          这是本轮查到的第一个缺陷（表现：列表出来了、字母打不进去）。
        - **打字之前把键盘还回去**：弹窗自己拿过前台，所以确认那一步先关掉自己、再让协调器把前台交回
          目标窗口（`TypedWithoutRestoringForeground` 是 UI 测试里的硬断言）。列表弹出期间目标框保持全空
          （`typedWhileOpen=False`），一个字符都不许提前漏出去。
        - **中文输入法下的 Enter（本轮查到的第二个缺陷，也是"这是填充不上吗"的真因）**：装配中的 IME 会把
          每一次组合键的 **KeyDown**（含确认候选词那一下回车）都报成 `Key.ImeProcessed`，被组合吞掉的 Enter
          永远不是 `Key.Enter`，于是 `KeyDown` 上写的确认 handler 在出货平台上是死的——筛选框 narrows 到一行、
          行也是高亮的，按 Enter 却什么都没发生。修复放在 **KeyUp**（实测那一下仍带真实键）+ **推迟到下一次
          `ApplyFilter`**：Enter 提交的那段文本在 KeyUp 之后才落到筛选框，当场取行会打出筛选前那一刻显示的
          条目。钉子：`The_Enter_that_commits_an_input_method_composition_picks_the_row_it_narrowed_to`
          （先断 `TypeCallCount==0` 证明没抢跑，再喂文本证明它选中的是窄化后的那一行）。
          **诚实记录**：这条只有无头证据。真机探针用 `keybd_event` 打 ASCII、机器上没有装配中的中文输入法在
          组合态，所以 IME 的 KeyDown/KeyUp 形状是在无头里复现的，出货前应在带输入法的人机上再按一次。
        - 退场原因全部走 `CloseAutoTypePicker(reason)` 并写日志（`EntryPicked`／`DismissKey`／
          `SecondHotkeyPress`／`VaultLocked`／`MainWindowClosed`／`ReplacedByFreshList`），真机侧靠这些行判定。
     - **设置里录制自定义手势（#89 的另一半）：已出厂并在发布产物上量过**。`SettingsDesktopView.axaml` 里
       `AutoTypeHotkeyBox` 双向绑到 `AutoTypeHotkey`，录制→写盘→协调器 debounce→`RegisterHotKey` 是同一条链。
       真机测法：探针 `-Gesture` 参数直接把组合写进该目录的 `settings.json`（模拟设置页落盘），再按那组键。
       实测（产物 `artifacts/publish/win-x64/jit/Monica.App.exe`）：
       `recorded gesture into settings: Ctrl+Alt+F9` → `armed=True, gesture=Ctrl+Alt+F9, registrationError=False`
       → `outcome=Typed`、`firstLength=15 secondLength=19` 两个框各自命中。
     - **查清并修掉的一个静默行为（本轮）**：`AppSettingsService` 的归一化原来在"两槽填了同一组合"时把
       `AutoTypeHotkey` 直接改回 `Ctrl+Shift+Enter` 并落盘。用户录制的那一下**当场是看得见报错的**
       （协调器 `ReportAutoTypeGestureConflict` → "快速搜索已占用该快捷键，请换一个。"），但**下一次启动**
       存盘值被悄悄换掉、错误提示也不来了——录进去的键永久丢失，而且把快捷搜索改开后原值也不会再回来。
       现在这一条不再由加载层改写用户记录值：保留原值，启动时若仍冲突就由协调器照旧报冲突，另一槽一改
       录制值立刻生效。**同一目录同一产物的真机对照**：修复前 `armed=True, gesture=Ctrl+Shift+Enter,
       registrationError=False`（偷换），修复后 `armed=False, gesture=Ctrl+Shift+Space, registrationError=True`
       （如实拒注册并保留用户那一下）。钉子：`App_settings_keeps_a_recorded_gesture_even_when_it_collides`
       （先按旧实现跑红：`Assert.Equal() Failure: Strings differ`，再按新实现跑绿）。
     - **弹窗真机实测**（`artifacts/autotype/verify-autotype-picker.ps1`，同一个目标窗体 + 外部真实按键）：
       1. `Pick`（默认手势）：`rows=25 typedWhileOpen=False pickerAboveRival=True pickerExit=0 success=True`，
          应用侧 `outcome=Typed, matches=0`，落点 `firstLength=15 firstMatchesUsername=True
          secondLength=19 secondMatchesPassword=True`（`matches=0` 指的是**弹出时**零匹配，全库列表靠筛选定位）。
       2. `Escape`：`pickerExit=1 success=True`、两个框 `0/0` 全空、`taken down. reason=DismissKey`。
       3. `SecondPress`（列表还开着再按一次手势＝"不是这个，算了"）：`appWindowCount 2→1`、
          `taken down. reason=SecondHotkeyPress`、两个框全空。
       4. 唯一匹配不走弹窗：`pickerSurfaced=False, outcome=Typed, matches=1`。
     - **查清的一个假象，下一轮别退回旧写法**：协调器最初用 Avalonia 的 `Window.IsActive` + 缓存自身 hwnd
       判断"前台是不是我自己"，实测在进程**一个顶层窗口都没有**的时刻 `IsActive` 仍读回 `True`
       （`totalTopLevel=434 owned=`，那个缓存 handle 的 `GetWindowThreadProcessId` 返回 pid=0、`IsWindow=False`）
       → 真按快捷键会被误判成"焦点在 Monica"、永远拒打。改成问操作系统：
       `IAutoTypeService.IsWindowOwnedByThisProcess(hwnd)`（比对自身 pid）。单测用**真的 message-only 窗口**
       跑通 true 分支，不是只测 false 分支。
     - 门禁（本轮 #89 收尾时重量）：`--warnaserror` 0 warning、单测 9 perf + 759 functional、UI 17 + 218 全绿，
       `verify-commercial-release.ps1` 通过；产物侧 `--smoke-ui-autotype` 探针走设置页同一条链路
       （`AutoTypeEnabled=true`→协调器→`RegisterHotKey`），只报 armed/pressed/outcome。
     - **仍未做/仍未验证（别当成已完事）**：
       1. `--smoke-ui-autotype` **没有接进 `verify-artifact-runtime.ps1`**：它要交互桌面会话、一个外部目标窗口
          和一次真按键，CI 里跑不起来，所以现在是"手动真机门"，跑法见上面 §5 的脚本（注入正例 + 弹窗四式 +
          录制自定义手势）。
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
       7. **输入法组合态的那一下 Enter 只有无头证据**。真机探针用 `keybd_event` 打 ASCII，机器上没有装配中的
          中文输入法处在组合态，所以 `ImeProcessed` KeyDown + 真实 KeyUp 的形状是在无头里复现的。
          出货前必须在带输入法的人机上按一次（打字→候选→Enter→再 Enter）。
       8. 真机量过的分派是**两端**：零匹配→全库列表（`matches=0` 弹出、靠筛选定位）、唯一匹配→直接打
          （`matches=1`、`pickerSurfaced=False`）。中间的 **`PickerForMatches`（多条命中直接弹出候选列表）
          没有真机样本**——探针标题故意取"谁都不含"的形状。UI 侧有钉，真机侧缺一条。
       9. 弹窗的鼠标路径未测：双击确认行、滚轮浏览长列表（25 行不到一屏，长库才溢出）、点行不确认只选中。
           键盘只测过 Enter/Escape 与筛选框打字，上下键换行未测。
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
- **#91 忘记密码：本轮只出厂了非破坏的那一半（应急包），破坏的一半先停下来等用户拍板**。
  - 已出厂的：`MainWindowViewModel.EmergencyKit.cs` + 设置→安全与恢复页两行 UI + 单测 9 条 / UI 1 条，
    读数与负控见 §2 的 `6613df6` 行。
  - **为什么另一半不能顺手做**：用户给的硬约束是"重置空库只是本地，keepass／mdbx／bitwarden 不要动"。
    实测对不上：现成的"清空全部"语句 `MonicaRepository.GetClearVaultStatements(VaultClearScope.All)`
    （`src/Monica.Data/Repositories/MonicaRepository.cs:1455-1469`）里带着 `local_mdbx_databases`、
    `mdbx_remote_sources`、`bitwarden_vaults` 三张表的 DELETE，直接复用它必然违反约束；当前软删除路径是
    `MdbxBackedMonicaRepository.ClearVaultDataAsync`（同目录 :727-748），Danger 页入口在
    `MainWindowViewModel.SettingsCommands.cs:44-84`。
  - **更根本的矛盾没解决，下一轮先问、不要自己选**：本地规范库本身就是 `mdbx/local.mdbx`，
    "把本地库重置为空"在任何实现下都必然动 mdbx。那句约束要么读作"只清本地规范库、不碰 KeePass 会话与
    Bitwarden 挂载"，要么要新增一条范围更窄的清空——这是产品语义决定，不是实现细节。
  - 还欠一条**非破坏**的入口：锁定态的解锁页没有任何"忘记密码？"出口（`MainWindow.axaml:25` 锁定时只挂
    `UnlockViewHost`，Settings 整页不可达），所以真正忘记口令的人现在看不到应急包。只做"把话说清楚 +
    指向恢复页"不需要任何破坏性动作，可以先做。
  - 应急包恢复目前要求已解锁（导入要有一个能重新封存明文的会话密钥），所以忘记口令的完整旅程是
    "重置 → 新主密码 → 恢复应急包"，缺的正是上面那条重置。
  - **未验证**：真实 shell 文件对话框的存/选往返（测试里是替身）；口令正确但文件被改名/截断的部分读
    只覆盖了"口令错"这一类失败。
- **Bitwarden 接入审计（用户 2026-09-23 问："能不能正常接入、能不能当完善的第三方客户端、会不会出现同步问题"）**。
  结论：**现在只能当"只读拉取 + JSON 导入"用，不能当完善客户端；修之前它会在下一次拉取时静默吃掉用户对绑定条目的本地改动。**
  - 实测（`tests/Monica.Tests/BitwardenLocalEditSurvivalTests.cs`：走真实编辑器 `PasswordEditorViewModel.BuildEntryFrom`
    + 真实 `MonicaRepository`，再用一份**未变化**的远端快照重放 `BitwardenPullMergeService.ApplyAsync`）：
    - 修前量到的红：`titleAfterPull=Remote baseline, updated=1, conflictsBackedUp=0, unresolvedBackups=0`；
      第二条同时量到 `localModified=False, pendingOperations=0` —— 改动既没了、也没留下任何备份，
      而账户卡照旧显示"已连接 / 无待处理更改 / 无冲突"（`MainWindowViewModel.BitwardenAccountProjection.cs:41-46`）。
    - 对照组（先把夹具钉住）：本地内容与远端一致、revision 一致时 `Unchanged=1` —— 所以上面那条不是
      指纹对不上的假红，是真覆盖。
    - 修后 3/3 绿；整套 Bitwarden 59/59 绿。
  - **修法**（`BitwardenMergeEngine.PlanExisting`）：revision 相同但状态不同 ⇒ 判为"这台设备改了、从没上传过"，
    改走 `CreateConflictBackupThenApplyRemote`。备份载荷经 `VaultDataProtector` 加密入库，实测
    `GetUnresolvedAsync` 能原样读回被改掉的标题。这条**故意不依赖** `BitwardenLocalModified`：实测 src/ 里
    没有任何编辑路径会把它置 true（只有克隆/导入会），逐处补 8 个写入口不如让合并引擎自己看出差异。
  - **缺口现状（按严重度，别当成已完事）**：
    1. ~~**完全没有写回通道**~~ **已修（`94adacd` 编码器 + 下一行那轮接线）**：原状况是
       `IBitwardenPendingOperationStore.EnqueueAsync` 在 src/ 里零调用点（只有它自己的定义与 7 处测试），
       下游 `BitwardenMutationProcessor` 永远在抽一个空队列。现在协调器在拉取之前先扫一遍漂移并入队。
       **仍然缺的**：新建条目连 `BitwardenVaultId`/`BitwardenCipherId` 都不会被赋值（grep `BitwardenVaultId = `
       只命中克隆/导入与合并引擎），所以**本地新建的条目永远不会出现在远端**；笔记/银行卡/证件这三类 secure item
       同样没有编码器可走（只计数不推）；**本地删除也不会传播**——`BitwardenCipherPayloadBuilder.cs:88` 明确拒收
       `IsDeleted` 的条目，所以把绑定条目移进回收站只会算作"欠一次但推不了"，从回收站恢复则指纹回到基线、
       本来就不需要推。⇒ "在 Monica 里改密码其他客户端看不到"这半句已经不成立，"新建"和"删除"这两条仍然成立。
    2. ~~**冲突备份有表、有计数、没有界面**~~ **已修（`59c0fac` 撤掉错误生产者 + 下一轮列表/还原）**：
       原状况是 `IBitwardenConflictBackupStore` 在 Monica.App 里唯一消费者是账户卡那一行计数
       （`...BitwardenAccountProjection.cs:20-23`），没有任何页面能列出或还原备份。现在同步页多出一段
       冲突列表，每行只有标题／类型／保存时间（**永不显示载荷里的明文**）+ 两个按钮：还原（写回本地并把
       条目重新标成"这台设备改了"，下一次同步真把它推上去）与放弃（只删备份）。
       **仍然要说清的**：还原一份 secure item（笔记/银行卡/证件）只回到本地——它没有编码器，推不到远端，
       所以远端那一份还会留在原处；`Reason` 那一句没有逐行显示，因为修完之后一行存在的理由只有一种。
       细节与实测见 §2 的 `59c0fac` 与 `73c89bc` 两行。
    3. ~~**远端同时改过的场景仍会盖掉本地**~~ **已修**：原判读是"revision 比本地新 ⇒ 判不出本地也改过"，
       需要存"上次同步时的载荷指纹"。落地形式不是当初猜的新增列，而是同步基线侧表 `bitwarden_sync_state`
       （schema 76→77），细节与取舍见 §2 下一行。
    4. **真服务器从未验过**：全仓 grep `vaultwarden` 0 命中，`eng/ci` 只有 4 个脚本，没有任何集成测试痕迹。
       登录 / prelogin / KDF / 2FA / captcha / 设备 OTP 只在替身 HTTP 下绿过。要接真账号或本地起 Vaultwarden，
       得先拿用户点头。
- **外观类改动欠一台"截图口味门"机器（#90 把这个缺口撞出来了，#92 修好了其中一条路）**。
  "这个滚动条有点丑了"这类判断按老规矩应该是"探一屏 + 截图过口味门"再铺开，当时两条路都不通、只能拿几何数字交差：
  1. **`--smoke-ui-screenshot-dir` 矩阵已经可用**（#92 修完）：7 次真跑里 1 次全帧相同，现在抓取前先验
     活性，冻结时直接 `failures=capture-stale` 且不写文件。正向 5/5 次 `distinctFrames=13`。
  2. **无头自己画图也不通**：UI 测试里 `RenderTargetBitmap.Render(window)` / `Render(list)` 之后
     `Save(path)` 与 `Save(stream)` 两种写法都写出 **0 字节 PNG**（不抛异常，静默空文件）。
     但**应用内**的抓取（真窗口 + `Render(Content)`）是好的，所以要看图就跑矩阵，不要在测试里画。
  3. **矩阵真的能拍到滚动条**：`--smoke-ui-width 640 --height 900`（实际被 MinWidth/MinHeight 夹到
     800x844）那一轮，`DatabaseManagement_800x844.png` 右侧 x=775..776、y=431..722 有一条 2px 的滑块
     （RGB 107 压在背景 40 上），同页 1000x650 那一帧没有（内容没溢出）。**这是修前的读数**，成因与修后
     数字见下一条。
  4. **那条 2px 已经查清并修掉了（`49431a6`，任务 #93）**：打脸读数不是采样错，是真缺陷。在真
     workspace 的渲染树里读到滑块带一个 `scaleX(0.35) translateX(-2px)`，是 FluentAvalonia 的 ScrollBar
     主题按模板里 `Thumb` 的**精确类型**伪造 WinUI 细条时施加的，而它的值优先级压过应用层 `Style` setter
     与 `ControlTemplate` 上的属性值（`RenderTransform="{x:Null}"`、`Value="scale(1)"` 两条都实测无效）。
     修法：派生 `Monica.App.Controls.SlimScrollBarThumb`（类型选择器只匹配精确类型，派生即逃出）。
     修后离屏 1.0/1.5 采样都是 4 DIP→6 设备像素、亮度 137（正是 `#73FFFFFF` 压在 40 上应有的值），真屏
     抓取 `span=1164..1169 maxRun=6` 一致，`:pointerover` 加宽确认仍生效（margin 2,0 → 8 DIP → 12 像素）。
     **旧解释要纠一半**：那句"测试用的是裸 `Window + ListBox`，证明不了真实工作区的样式优先级"只对了一半——
     裸 ListBox 同样吃应用层样式，它量的 4 DIP 布局宽是真的；错在 `Bounds` 不含 `RenderTransform`，
     纯几何断言天生看不见这次钳制。所以补的不是"真实 workspace 里再读一次 Bounds"，而是正向钉住
     `Thumb` 类型与 `RenderTransform is null` 的守卫（同一文件里那条像素断言在无头下永远量不到，帧缓冲全零，
     已删）。外观本身仍待用户在运行的应用里看一眼。
  5. **仍未拿到的**：滚动条"好不好看"还是要用户在运行的应用里自己看一眼；本轮只多了一张 800x844 的
     `DatabaseManagement` 真帧可看（`artifacts/` 已 gitignore，图不入库）。
- **Passkey 三片：片1、片3 已落地，只剩片2（用户 2026-09-23 点名"应该再支持一下 passkey 这些还有 Windows Hello 解锁客户端"，并选了"片1 Passkey 存储+自证引擎"先做）**。
  1. **片1 已完（上一轮 `bda943f`）**：`Monica.Core/Passkeys/*` 是 Monica 自己的软件认证器 +  relying-party 校验器
     （ES256/RS256 生成、authData 布局、CBOR 子集、clientDataJSON、none 证明、`passkey_private_key_v1_` 引用方案
     全部照抄 Android 的 `passkey/` 包）；`Monica.Data/Passkeys/*` 是落地面：新表 `passkey_private_keys`
     （schema 75→76）用保险库会话密钥加密 PKCS#8，凭据行只留引用，清库语句同步加了 `DELETE FROM passkey_private_keys`。
     取证是 42 条单测（`PasskeyEngineTests` + `PasskeyStoreTests`）+ 两套门 + jit 产物真跑门全绿。
  2. **片1 写的过程中修掉一个真缺陷**：`PasskeyStore.Validate` 原先把 `PasskeyRpId.Normalize(...)!` 直接赋回
     `entry.RpId`，遇到 `".."`/纯点这类 rpId 会归一化成 null 再写库，报的是 SQLite `NOT NULL constraint failed`
     而不是参数错误。现在先判空再写回。**教训：`!` 会把"归一化可能失败"这件事吃掉，只有真跑一条脏输入才暴露。**
  3. **片1 的已知边界（不是 bug，是范围）**：
     - 引擎目前是**纯库**，没有任何界面或协议入口消费它（`App.axaml.cs` 注册了 3 个 singleton，没人解析）。
       所以 UI/产物门不会替 passkey 背书，只有那 42 条单测会。
     - rpId 哈希按 Android 的做法对**原样传入**的字符串取 SHA-256（归一化只用于等价判断），所以一个凭据只在
       它注册时的那个拼写下自证通过。
     - 签名计数器恒为 0（照抄 Android `PasskeyAuthActivity` 的理由：整库备份恢复会让单调计数器跨设备回退，
       表现成"用了若干次后 passkey 突然失效"），`PasskeyVerifier` 读但**不比**计数器。
     - `passkeys` 行的元数据（`user_name`/`notes`/`rp_id`）仍是明文入库，只有私钥走了会话密钥。这一点与 Android
       的表形状一致，动它等于破坏 schema 对齐，因此**没有**擅自改；如果以后要收，得连带设计导出口径。
     - 全部 SQL 都保持编译期常量（包括 `SelectColumns + " WHERE ..."` 这种拼接），因为 Dapper.AOT 只拦截
       常量命令文本；这与 #43（AOT 产物解锁失败）是同一个失效家族，别再退回插值字符串。
  4. **片2 = Windows Hello 解锁客户端 + 平台 passkey（未开始，被外部条件卡住）**：
     - 本机实测 `WebAuthNIsUserVerifyingPlatformAuthenticatorAvailable` 返回 `value=0`，也就是**这台机器还没登记
       Windows Hello**，所以片2 端到端验不了；开工前要么用户先登记 PIN/指纹/人脸，要么只写边界与探针不做承诺。
     - 真实 P/Invoke 名字是 `WebAuthNAuthenticatorMakeCredential` / `WebAuthNGetAssertion`（不是 `WebAuthNMakeCredential`
       / `WebAuthNGetAssertion` 这种直觉拼法），外加 `hmac-secret` 扩展才能走"passkey 解保险库"这条路。
     - 需要升级现有的 `NativePasskey` 能力上报，并对齐 Android 的 `biometric_enabled` / `auto_lock_timeout` 语义。
  5. **片3 = 自动输入（Auto-Type）序列可配置（`dda2766` 已完）**：
     - **先纠一处我上一轮写下的错**：Android **没有**自动输入序列解析器，也没有 `<username>` 这种尖括号拼写。
       `{USERNAME}{TAB}{PASSWORD}` 在 Android 侧只是一句 UI 提示（`keepass_native_auto_type_tokens_hint`，
       `res/values/strings.xml:4605`）加一个 KDBX `AutoTypeData` 字段（读得到、改得了、存得回，但没人消费它）；
       真正的 IME 填充是写死的、从不发 ENTER、没有等待、条目表里也没有 per-entry 的序列列，全局设置里同样没有。
       所以：标记拼写跟 KeePass 的 `{...}`、**不加数据库列**、序列是桌面端的一条全局设置，解析器是桌面端自己的
       能力，不是"补齐 Android 已有的东西"。
     - 落地：`Services/AutoTypeSequenceParser.cs` 支持 `{USERNAME} {PASSWORD} {TAB} {ENTER} {DELAY:毫秒}` +
       原样文字，大小写不敏感、≤256 字符；`{DELAY}` 的上限直接复用注入器的 `AutoTypeLimits.MaxDelayMilliseconds`
       （5000），所以超长的等待是响亮地拒绝而不是被静默截断。`AutoTypeMatcher.BuildTokens` 已删——写死的流程没了，
       默认序列 `{USERNAME}{TAB}{PASSWORD}` 逐字复刻今天的行为，**仍然只有用户自己写了 `{ENTER}` 才会提交表单**。
     - 条目缺某字段时，那个字段和**紧挨着它的 Tab 一起去掉**（Tab 只为在两个值之间走路，留着会让光标离开用户
       已经点进去的框）；连续 Tab 成段判定，所以没用户名时 `{USERNAME}{TAB}{TAB}{PASSWORD}` 只剩密码。这条规则
       是被一条真红逼出来的：第一版只吃掉"后面"的 Tab，`{("octocat","")}` 那行拿 2 个键。
     - 设置页新增一行即时校验：错误文案点名用户写错的那个标记。解析不了的序列**按原样存着**（`AppSettingsService`
       只把**空**序列填回默认值）——载入时偷偷改写会删掉用户输入还不吭声，正是当年自动输入热键冲突那个修复踩过的坑。
       按热键时**再判一次**（设置文件可能在关闭期间被手改），三条退路分别是 `SequenceInvalid`（点名标记）／
       `NothingToType`（序列能解析，但这条条目没内容可发）／`Typed`。新设置已登记进 `Clone()`，另有
       `App_settings_file_carries_every_declared_setting` 那条反射守卫兜底。
     - 覆盖：单测 20 条（默认序列不发 ENTER、缺字段的成对 Tab、字面文字、延迟、大小写、九种拒绝行、坏序列落盘
       不被改写）+ UI 3 条（配置好的序列原样到达注入器、坏序列一个键都不发且状态栏点名 `{capslock}`、设置页
       那一行在真渲染树上双向绑定＋错误行随 `HasAutoTypeSequenceError` 显隐）。负控两条：把错误 `TextBlock` 的
       `IsVisible` 改成常量 `True` → 新 UI 测红在开头的 `Assert.False`；把 `Mode=TwoWay` 去掉 → **仍然绿**，
       因为 Avalonia 的 `TextBox.Text` 默认就是双向（`Mode=TwoWay` 是写下来表意的，不是功能必需的）。
     - **真机实测**（发布产物 `artifacts/publish/win-x64/jit/Monica.App.exe`，每轮一个全新
       `MONICA_APPDATA_DIR`，WinForms 目标窗体两个输入框 + 一个 `AcceptButton`，序列写进产物自己读取的
       `settings.json`，脚本 `artifacts/autotype/verify-autotype-injection.ps1` 只报布尔和长度）：
       1. 默认序列：`outcome=Typed`，两个框逐字对上口令条目，**`submitClicks=0`、pressToVerdict=431ms**——
          出厂默认仍然不会提交表单。
       2. `{USERNAME}{TAB}{PASSWORD}{DELAY:2000}{ENTER}`：`Typed`、两框都对、**`submitClicks=1`、2211ms**
          ——ENTER 真发出去了，DELAY 真的等满了 2 秒。
       3. `smoke-{USERNAME}{TAB}{PASSWORD}@mail`：第一框 21 字符、第二框 24 字符且都匹配预期——字面文字原样进框。
       4. `{capslock}`：`SequenceInvalid`，两个框**长度 0**，一个键都没发。
       5. `{USERNAME}{DELAY:9000}{TAB}{PASSWORD}`：同样 `SequenceInvalid`、零按键——5000ms 上限在产物上生效。
       副作用记录：4/5 两轮的进程退出码是 1，因为 `--smoke-ui-autotype` 自检把"outcome 不是 Typed"记成
       `success=False`；这不是本轮引入的（弹列表的探针一直如此），别按失败读。
     - 仍未做：把 `AutoTypeData`（KDBX 里那条 per-entry 序列）读进来。Android 存了但不用它，桌面端现在也不读；
       要做的话得先决定"条目级序列"和"全局序列"谁优先，那是新范围，不是缺口。

## 8. 用户协作偏好（务必遵守）

- **中文响应。**
- **实证优先**：任何"内存/耗时/性能"结论必须真跑真测给出实测数字，不许凭读码给理论（包括我自己的）。
- **UI 口味偏"素"**：删重复、删嵌套；铺开前先探一屏 + 截图过口味门。
- **评审后再推**：commit 本地、展示结果，不擅自 push。
- 面对从别处 fork 进来的代码，先问"到底要不要"，再谈"怎么维护"。

---
接手第一步建议：工作树只剩本节自身的文档提交、两套 Windows 门（源码级 + 产物级）实测全绿，用户点名的必须功能（托盘、
自动输入、自动填充弹窗与快捷键录制、单实例守卫、首次收进托盘的一次性提示）都已出厂，且每一条都在
**发布产物**上真机量过。Bitwarden 那条（"能不能当完善的第三方客户端、别出同步问题"）现在收到了
`59c0fac` + `73c89bc`：写回接线、被拒推送不再堆垃圾备份、冲突能看见也能拿回来，剩下的三件（新建/删除不传播、
secure item 没有写回编码器、**从未对真服务器验过**）都在 §7 那条缺口清单里逐条标着，其中接真账号或本地起
Vaultwarden 要用户点头才动。
用户排队点名的两条现在只剩一半：忘记密码只出厂了非破坏的那一半（应急包 `6613df6`），
**破坏的那一半（锁定态重置为空库）不要自己开工**——它卡在 §7 那条"只是本地"与"mdbx 不要动"的语义矛盾上，
下一轮第一件事是拿这个问题问用户，第二步才是那条不需要任何破坏动作的锁定态"忘记密码？"入口。
滚动条那条（`45f01ef`）的打脸读数已经查清并修掉了（`49431a6`，#93）：滑块确实被 FluentAvalonia 主题
按类型钳掉到三分之一，派生 `SlimScrollBarThumb` 逃出后离屏与真屏都是 4 DIP→6 设备像素，hover 加宽
也确认生效，守卫与负控都在。**只剩外观口味本身要用户在运行的应用里看一眼**——不需要再开调查任务。
其余可挑的活在各条末尾那份"仍未做/仍未验证"清单里，不必再花时间复验已绿的部分。

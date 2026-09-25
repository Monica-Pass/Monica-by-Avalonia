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

分支 `main`，`git status` 只剩给本节标提交号的文档改动。功能 HEAD = `6e99e22`（其后只有给本节自身标提交号的文档提交），近几轮：

下面这张表**不是按时间排的**：同一条线的行挨在一起（例如 Bitwarden 写回的两行 `94adacd` → `5cf27c9`），
读某一件事的来龙去脉时按主题往下连，不要按行号当时间线。

| commit | 立住了什么 |
|---|---|
| `48d6b21` | **回收站里的"彻底清除"第一次推得出去（#107，用户拍"传播"）**。新增 `src/Monica.Data/Bitwarden/BitwardenPurgeQueue.cs`：在**行被销毁的那一刻**记账，因为漂移扫描从此再也看不见它——tombstone 写入器连 `BitwardenVaultId`/`BitwardenCipherId` 一起丢（`MdbxBackedMonicaRepository.cs:1332-1351`）。`OperationType=Delete` 走 `DELETE /ciphers/{id}`、载荷 `"{}"`、键 `local-purge:{vault}:{cipher}`（不复用 `local-delete:`：`ON CONFLICT DO UPDATE SET status='pending'` 会把服务器已答应的软删原地复活），`LocalPayloadHash: null`（不给一个已经不存在的 cipher 记基线；实测下一轮整表重写把它带走，不需要 `RemoveAsync`）。接线三处清除收口（`RecycleBinCommands` 那条**绕开 core** 的路径 + `RecycleBinUnifiedCommands` 的单条／清空／到期自动清理），无 cipher id 或无 revision 的行**拒绝入队**。处理器另两处：撤销竞态闸门**收窄到只有软删**（硬删没有"用户又放回去了"这回事），以及"目标已经没了即完成"。覆盖 `BitwardenLocalChangeQueueTests` +5（34 条）。**负控两批真打**：批次1（复用软删键 + 闸门放宽）红 3 条、批次2（拔掉生产者 + 删结算分支）红 4 条，而"该拒就拒"那条在两批里都绿。**未做**：推送失败而 pull 成功时 `AddRemote` 会把条目在本地重新长回来（未测）。门禁见文末第四轮实测。
| `201501e` | 上一条那条结算判据**被真服务器当场纠正**：Vaultwarden 对"手里没有的 cipher"答 **400 而不是 404**（活 cipher 控制组 200、从未存在的 id 也 400），所以按 404 写的分支是死代码、erase 落成永久 `Failed`。放宽为"两种删除读到 400 或 404 即完成"，**只限删除**（请求没有体，400 不可能是载荷写错；`Update` 吃 400 仍算失败，新测试在同一批次里两头钉住）。真服务器复跑 `Completed=1/Failed=0` + 下一轮 `Claimed=0`。
| `12ed5a9` | **pull 不再把"还欠一次 erase"的条目养回来（#110，收掉 `48d6b21` 那行末尾的"未做、未测"）**。判据只一条：合并计划里的 `AddRemote` 若撞上"本账户有一条 `Delete` **且状态不是 `Completed`** 的待处理操作" ⇒ 不写库、只计数。为什么只能读队列而不是读行：tombstone 连 cipher 身份一起丢（正是 #107 要当场记账的那条理由在这里反过来），清除之后从行上问不出"它曾经是谁的"。`IBitwardenPendingOperationStore` 是 `BitwardenPullMergeService` 的**必需**构造参数而非可选：可选参数会让「没人给闸门喂数据」看起来一切正常（下一行那个反证就是这个形状），必需参数则在编译期与容器解析期各自点名——实测探针那份 `Program.cs:154` 立刻报 `CS7036: There is no argument given that corresponds to the required parameter 'operationStore'`。结果记录加**尾部可选**字段 `SuppressedResurrections = 0`，5 个既有构造点因此一行不改就能编译。覆盖 2 条单测：① 同一份快照里"欠 erase 的那条"和"别处新建的那条"一起回来 ⇒ `Added=1`（是那个陌生人，不是被清除的那条）+ `SuppressedResurrections=1`，读库 `DoesNotContain cipher-erase`、`Single cipher-stranger`（所以抑制是**逐条**的，不是一轮 pull 整体压住）；再把那条 erase 判完成、拉同一份 ⇒ `Added=1/Suppressed=0` 且 `cipher-erase` 回来了 ⇒ **压住的是欠账，不是身份**。② `RecordFailureAsync(Validation)` 把那条 erase 打成 `Failed` ⇒ 照旧 `Added=0/Suppressed=1`、库空（重试与否是队列的事，用户没重新决定之前不把东西叫回来）。**负控**：摘掉抑制分支 ⇒ 恰好这 2 条红，红因读的是消息不是计数（`Expected: 1 / Actual: 2` 与 `Expected: 0 / Actual: 1`）。真服务器同一 stage 在两版二进制上跑出**相反判决**（含"erase 后来落地、多长出来的那行仍然留在本地"这个最坏形状），见文末第五轮实测。**新边界（别读成账清了）**：`ClaimReadyAsync` 只领 `status='pending'` 且未超尝试预算的行 ⇒ 一条 `Conflict`／`Failed` 的 erase **永不再试**，条目在本地被压住、服务器那份残迹一直留着，直到有人按同一幂等键 `local-purge:{vault}:{cipher}` 重新记账（`ON CONFLICT DO UPDATE` 把它抬回 pending）。正确的产品答案是给这条冲突一条**用户可见的决定**（#99 那一族界面），不是我们自动改判 revision。 |
| `6bbf449` | 上一行补一条**容器级**证据（`48d6b21` 那条"App 层接线没有自动化证明"缺口的**前半**）：UI harness 本来就经 `App.ConfigureServices` 建**生产**容器，所以在 `BitwardenSyncWorkflowUiTests` 里解析 `IBitwardenPurgeQueue`／`IBitwardenPullMergeService`／`IBitwardenPendingOperationStore` 的实现类型，就把"永久删除的两半都在位"钉住了。**反证正是它要防的形状**：删掉 `App.axaml.cs:190` 那行注册重新构建，`MainWindowViewModel` **照样建得出来**（它的队列参数是 `IBitwardenPurgeQueue? bitwardenPurgeQueue = null`，本仓惯例是尾参数一律可选）——应用能启动、能本地清除，只是不再记账、下一轮 pull 把条目养回来；而这条断言即刻红在 `No service for type 'Monica.Data.Bitwarden.IBitwardenPurgeQueue' has been registered.`。还原后 `sha256sum -c` OK、该 UI 类 15/15 绿、格式 0 改动、Release 0 warning。**行为那一半仍缺**（真清除命令走出一条队列行），理由写在 §7 缺口 4 第 ① 条。 |
| `39bb145` | **推不出去的永久删除第一次有一条可见的决定（#111，收掉 `12ed5a9` 那行末尾的"新边界"）**。`12ed5a9` 留下的形状是三方各自正确、没有人能改主意：`ClaimReadyAsync` 只领 `status='pending'` 且未超尝试预算的行 ⇒ 一条 `Conflict`／`Failed` 的 erase **永不再试**，合并引擎照旧按"还欠一次 erase"压住条目，服务器那份残迹长留。修法不是我们自动改判 revision，而是把决定还给用户：新增 `src/Monica.Data/Bitwarden/BitwardenStuckEraseService.cs`（`IBitwardenStuckEraseService`：列出卡住的 erase／放弃其中一条），口径**只认硬删**——软删推不出去时本地行仍在回收站、仍绑着库，pull 看得见这个决定，条目不会复活。界面接在同步页冲突段落之后（新 partial `MainWindowViewModel.BitwardenStuckErasures.cs` + `BitwardenSyncSourceView.axaml` 的 `BitwardenStuckEraseSection`），唯一动作"把服务器那一份放回本机"就是 `CompleteAsync`——正是这一条让 #110 那条抑制判据（"状态不是 `Completed`"）即刻让路。放弃前**重读队列而不是信屏幕上那行**：列表是在同步进行中标出来的，而同一幂等键的重新记账（再清一次就是）会把行抬回 pending，照旧标记完成等于悄悄丢掉用户刚刚要的删除。**刻意不顺带跑一轮同步**：这是个本地决定，跑整轮会连带把队列里别的东西推出去，文案因此说"下次同步会把服务器那一份拉回本机"。v1 **不做**"强行再删一次"：那要的是服务器当前 revision，而 `bitwarden_sync_state` 只存 `cipher_id/payload_hash/synced_at`，`BitwardenMutationGuard` 因此拒绝没有基线的删除。行同样只能显示 cipher id——清除之后没有任何表还留着它的标题（这正是"队列行是那个决定之最后记录"的同一件事）。覆盖 3 条单测（列表**只出那一条**：pending／在途（已被 `ClaimReadyAsync` 领走）／软删／已完成／换账户这五种 id 逐个放弃全抛 `KeyNotFoundException`，且抛完队列状态一字未动；放弃之后同一份快照由 `Added=0/Suppressed=1` 翻成 `Added=1/Suppressed=0`；重记账那行不再是债，放弃必须抛）+ UI 类 15→**17**（分区控件在位、生产容器解析出实现类型、一行放弃只摘那一行且不碰冲突列表、模板外那条命令 `Assert.Same` + 参数对号）。**负控四批真打**：放宽选择口径（把软删或非终态也算卡住）⇒ 列表口径那条红；拔掉 `AbandonAsync` 的重读 ⇒"重记账不是屏幕上那行"那条红；删掉 `App.axaml.cs` 那行注册 ⇒ 容器断言红；拆掉模板按钮的 `Command=` ⇒ 两条命令解析红。还原后 `sha256sum -c`（`probe-appdata/nc111.sha`）三个 OK。真服务器 stage 17 见文末第六轮实测。门禁：格式 0 改动、Release 0 warning、commercial-release `=0`（单测 10 `perf-budget` + 914 常规、UI 17 + 242 常规）、产物级 `pub_rc=0`/`rt_rc=0`。 |
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
| `5a1fe3c` | **本地新建条目第一次能进远端（#100，收掉 #94 缺口 1 剩下的那半"新建"）**。上一轮的漂移扫描只认"远端快照确认过的身份"，所以本地新建的条目无论同步多少次都到不了服务器——不是管道断，是**没人给它一个可以被扫描到的身份**。三处补齐：① App 侧新增库页批量菜单项"上传 N 项到 Bitwarden"（`MainWindowViewModel.BitwardenPublish.cs`，判据 `IsLocalOnly` ∧ `CanEncode`，勾上 `BitwardenVaultId`、`BitwardenCipherId` 留空，然后复用 `SyncBitwardenAccountCommand` 立刻推）；② 扫描侧删掉 `if (baseline.Count == 0) return (0,0)` 的早退、把"无 cipher id"识别成 **create**（幂等键 `local-create:{vault}:{本地身份}` 故意**不带内容哈希**：首推之前改三次也只欠一个 cipher，带哈希会 POST 三份重复，而 update 的键仍按哈希走）；③ 队列行需要一个 id 而 Bitwarden 的 id 是服务器拥有的 GUID ⇒ 新增 `BitwardenLocalCipherIdentity`（`local-password:{entryId}`，不是 GUID 所以与真 id 天然不撞），处理器把服务器回的 `dto.Id`/`revisionDate` 写回那一行，之后同一条目走 update。顺手补 `CanEncode`（就是 `FindUnsupportedChange is null` 的公开问法），让"告诉用户会推几条"和"实际能推几条"是同一个判据。**接线上量到一条会真丢 id 的坑**：`SavePasswordAsync` 的 UPDATE 原本把四列 Bitwarden 身份整列覆盖，而同步是拿**它自己那份行**写库、屏幕上是同步之前加载的那份 ⇒ 在陈旧副本上保存一次编辑就把服务器刚发的 cipher id 抹成 null，下一次扫描把同一条目当新条目再推一遍（重复 cipher，用户只会看到多出来的那一条看不明白为什么）。改成 `COALESCE(@x, x)`（passwords 与 secure_items 两条 UPDATE 各四处），身份只保留不擦除。**负控真打**：`ProcessorCompletesAndDefers...` 里那条 create 的旧 fixture 是"条目已经绑在 cipher 上却排队 create"，正是新加的 `HasPublishableEntry` 闸门要跳过的形状——闸门一上，旧断言立刻红在 `Assert.Equal("remote-created-id", ...)`，把 fixture 改成真的未绑定条目（`BitwardenCipherId = null` + 本地身份）后才绿，说明写回路径是被证到的而不是被绕过的。覆盖：`BitwardenLocalChangeQueueTests` +3（空基线下的一条发布条目走完 排队→POST→写回→基线按服务器 id 记下→下次扫描安静，且指纹与存储行一致；首推前改两次仍只有一行且带最新内容的哈希；进回收站的发布条目不推）、`BitwardenMutationProcessorTests` +1（已绑定的条目排队 create ⇒ `RequestCount=0` 且身份不动，即"第二次上传不复制"）、UI +1（真 DI + 真视图：勾上本地独有条目后菜单里那一项可见、标题带计数、`Command` 就是 VM 那个命令）。**为什么删除仍然不传播**、以及**为什么"只勾了推不出去的条目"时菜单项不出现**（与 Stack/Archive 同一套"不做无用之事的项就不摆出来"的既有惯例）都是故意的，别再当漏项。门禁：格式 0 改动、commercial-release 绿（单测 862 常规 + 10 `perf-budget`、UI 231 常规 + 17 `perf-budget`、Release 0 warning）、重新 publish 后 jit 产物真跑绿（`CANONICAL VAULT passed`、loadMs=140/4000、KeePass 20000 条增长 3.3MB/24、锁定尾窗中位 105.2MB/120 区间 104.3–107.0、锁/解 25/14/1/4）。**本轮新量出来、已记成 #101 的缺陷**：一次拉取带回来的远端改动在解锁期间看不见——`VaultSnapshotLoader.LoadAsync` 全仓只有一个调用点（`LoadCoreAsync`），而 `OnBitwardenSyncStateChanged` 对 Completed 只回 `LoadBitwardenAccountsAsync()`，所以要重新锁定+解锁才看得见（COALESCE 挡住了数据被抹，挡不住界面陈旧）。**仍未做**：笔记/银行卡/证件没有写回编码器；本地删除不传播；`COALESCE` 之后"从 Bitwarden 库解绑"不能靠保存传 null 实现（将来要解绑得走显式 SQL，目前全仓只有克隆/导入那两处在 `Id=0` 的 INSERT 行上置 null，不受影响）；**全程从未对真 Bitwarden 服务器验过**（#94 缺口 4，要用户点头才动）
| `dda2766` | 三片里的**片3：自动输入序列可配置**。原来那条流程写死"用户名 Tab 密码"，回车和不太规矩的登录框都得用户手动补。新增 `src/Monica.App/Services/AutoTypeSequenceParser.cs`（文件名带 Parser 是**没办法**：设置属性和 VM 都叫 `AutoTypeSequence`，同名 class 会被遮蔽），token 拼写用 KeePass 的 `{USERNAME}{TAB}{PASSWORD}{ENTER}{DELAY:ms}`。**为什么是这个拼写而不是 Android 的**——量过 Android 现状：那边根本没有序列解析器，`{USERNAME}{TAB}{PASSWORD}` 只是 `res/values/strings.xml:4605` 的一条 UI 提示，KDBX 的 `AutoTypeData` 读了存了但没有任何人消费，IME 填充写死、从不发回车、没有 `{DELAY}`、也没有按条目一列。所以贴齐的是**数据形状**（同一条语法，将来加按条目覆盖不用换语言），而"全局一条设置、不加 DB 列"是当前的诚实边界，按条目覆盖仍是待办。规则：整条模板要么全解析要么整体拒绝，未知 token／未闭合／非法延时都原样回显用户打的那个 token，**绝不部分生效**；缺字段的行会把它相邻的那一串 Tab 一起丢掉（两遍 `dropped[]` + `IsNextToDroppedField`），免得把光标敲飞；延时上限 5000ms 抽成 `AutoTypeLimits`，解析器和注入服务共用同一个数，不再是两处各写一份。**按下时再解析一次**：`settings.json` 可能在关闭期间被手改，设置页那个绿色勾不构成按下的许可证，新增 `SequenceInvalid`／`NothingToType` 两个结局都保证零按键。文案坑：`L[key]`/`Get` 返回原文所以花括号安全，但**带 `{...}` 的字符串绝不能当 `Format` 模板**，错误提示走 `AutoTypeSequenceInvalidFormat`（"……：{0}"）把 token 当**参数**传。设置持久化按上一轮的教训在 `Clone()` 里登记了，且 normalize 只补**空**值——解析不了的序列原样保留并报错，不静默改写用户输入。覆盖：单测 20 条（默认不发回车／缺字段与相邻 Tab 规则／字面量／大小写／10 行拒绝表含 `{DELAY:5001}` 与 `-1`／不成形 keystroke 序列／超长）+ UI 3 条（序列原样到达注入器、坏序列零按键且报出 token、设置页字段与错误行的绑定联动）；UI 门从 222→225。**两条负控里有一条打了脸**：去掉 `Mode=TwoWay` 测试**不红**——Avalonia 的 `TextBox.Text` 默认就是 TwoWay，这条得记下来；第二条（把错误行写死 `IsVisible="True"`）红在开头那句 `Assert.False`，所以测试有牙。**又踩了一次已知坑**：新 UI 测试读回 `null`，就是因为没先 `SelectedSettingsPage = "Desktop"`（§2 上一行已经写过）。产物真跑（发布 exe，每次一份全新 `MONICA_APPDATA_DIR`，只报布尔值和长度）5 次：默认→Typed 且 submitClicks=0/431ms、`{...}{DELAY:2000}{ENTER}`→submitClicks=1/2211ms、纯字面量→两个框 21/24 字符逐字相符、`{capslock}` 与 `{DELAY:9000}`→SequenceInvalid 且零按键。探针侧新事实：`--init`/`--seed` **不会**写 `settings.json`，要改序列得自己建那个文件（`appExit=1` 是 `--smoke-ui-exit-after-checks` 的既有退出码约定，不是失败）。门禁：格式 0 改动、Release 0 warning、单测 9+835、UI 17+225 全绿；产物门（本轮 publish，其后只加了一条 UI 测试、产品代码未再改动）`CANONICAL VAULT passed`、loadMs=197/4000、KeePass 增长 5.1MB/24、锁定尾窗中位 107.1MB/120（区间 106.5–108.4）。**仍留一条老实话**：切语言时序列错误文案会重译，但 `AutoTypeStatusDescriptionText` 不会——那是本轮之前就有的，没有一起改 |
| `94858aa` | **一次拉取带回来的东西当场就能看见（#101，#100 那行记下的缺口）**。新增 `MainWindowViewModel.BitwardenPullRefresh.cs`：合并结果里 `Added+Updated+Deleted+ConflictsBackedUp > 0` 才重读库（全零的一轮绝不打扰屏幕），重读走 `ReloadVaultKeepingSelectionAsync`——先记下当前行的身份，重载后 `ReopenVaultRowAfterReload`（`VaultLibrary.cs`）把同一行重新打开，不弹编辑器也不把选中行换成邻居。同步页/认证命令/冲突还原三处共用这一条，新增 `BitwardenPullAppliedFormat` 中英各一份把"带回来几条"说清。**自己把自己打红过一次**：冲突还原现在会顺带重读库，而 `A conflict row resolves the command declared outside its template` 只跑一轮 `RunJobs()`，于是红在 `Assert.Empty() Failure: Collection was not empty`——红因是测试的泵不够，不是产品；改用 `RunJobsUntil(条件, 原因)`（20 秒上限、条件不成立即点名），而不是把断言放宽。门禁：格式 0 改动、单测 862 常规 + 10 `perf-budget`、UI 235 常规 + 17 `perf-budget` 全绿，重新 publish 后 jit 产物真跑绿（`CANONICAL VAULT passed`、loadMs=134/4000、KeePass 20000 条增长 5.7MB/24、锁定尾窗中位 107.6MB/120 区间 106.1–109.6、锁/解 25/14/1/4） |
| `5868a1a` | **本地回收站第一次推得出去，并且"服务器也把它收进了回收站"终于是一个能收敛的状态（#102+#103，收掉 #94 缺口 1 的"删除"）**。删除**由路由决定、不带载荷**：新增 `BitwardenMutationOperationType.SoftDelete`（落库是字符串 `soft_delete`，不是序号），传输层走 `PUT /ciphers/{id}/delete`（官方客户端软删用它，把 `DELETE /ciphers/{id}` 留给永久删除），preflight 与"不读响应体"两处判断都按两种删除一并放行；队列对已绑定的漂移条目改判 `deletion`，载荷写 `"{}"`——**所以笔记/银行卡/证件这三类没有编码器也照样删得动**（实测 `A_trashed_secure_item_owes_a_delete_even_though_no_encoder_carries_its_content`）。两处竞态一并处理：① 用户在这台把条目放回架子上了、那条删除还没出门 ⇒ 处理器 `IsHeldAlive` 直接把行判完成、**一次请求都不发**（实测 `transport.Sends == 0`，红过一次的原因是我先写成"队列为空"，而 `GetAsync` 连完成的行一起回）；② `ReplaceForVaultAsync` 不再把回收站条目从同步基线里剔掉，否则"确认删除之后才做的恢复"在漂移扫描眼里零漂移（实测 `Expected: 1 / Actual: 0`），远端那份还会每轮回来再压一遍。随后量出**同族的第三个坑**：解码器对服务器回收站里的 cipher 给的是 `deleted:{revision}` 标记而不是指纹（`BitwardenCipherDecoder.cs:63-73`），拿它比本地指纹永远不等 ⇒ 每拉一次多一条冲突备份、漂移扫描每轮"欠一次删除"（实测安静库 `Enqueued 1`）、而只数**活着的**远端条目的安全闸门把"整库都被删进服务器回收站"直接判成 `EmptyRemoteVault` 抛异常（此后每次同步都失败）。修法是把标记收口成 `BitwardenPayloadFingerprint.ForRemoteDeletion/IsRemoteDeletionMarker`、合并引擎两边同删即短路、队列两边同删视为已同步、闸门改数 `Ciphers.Count`（真·无载荷仍然拦）。**负控**：只让 2 条新测试变红（`NoChange`→`CreateConflictBackupThenApplyRemote`、`Unchanged 1`→0），改回后 871 条单测全绿（上一行 `94858aa` 是 862，本轮净 +9：队列 6 + 传输 1 + 合并 2 + 拉取 1）；UI 235+perf 17、publish jit、真跑门（lockedPrivateMB=104.8/120、KeePass 20000 条 openMs=784 增长 2.9MB/24、锁定尾窗 104.0–107.1）、商业发布门**四道全 rc=0**。永久删除**故意不推**（`Delete` 无生产者）；`/delete` 路由与"对已删 cipher 发 update"仍只在替身下绿过，见缺口 4。 |
| `9b14bdb` | **笔记/银行卡/证件第一次有出站载荷（#104，收掉 #94 缺口 1 最后那块"内容"）**。规则沿用 login 编码器那条：**只写解码器会读回来的东西**，但不靠人工枚举条件，而是让编码器**自己把解码器走一遍**——每个 plan 先把准备发出的载荷投影成"下一次拉取会留下的那一行"（笔记＝对正文重跑 `NoteContentCodec.BuildSavePayload`；银行卡＝按解码器的构造填 7 个字段、`BankName` 抄 `Brand`、`CardTypeString` 钉死 `CREDIT`、其余列留默认；证件＝把 `AdditionalInfo` 按 12 个已知标签反解回 identity 各列、`FullName` 进 `FirstName`、`DocumentNumber` 按类型进 passport/license/ssn、`Nationality` 与 `Country` 是同一格），再比 `ForSecureItem(本地行) == ForSecureItem(投影行)`，**不等就抛**。于是"带标签的笔记、markdown 笔记、图片清单非空、有账单地址/昵称/DEBIT 的卡、有签发或到期日的证件、`ID_CARD` 却带证件号、`Country` 与 `Nationality` 分叉、`Notes` 列与 `ItemData` 里的正文不一致"全部留在本地，而不是被下一次拉取改写；**映射写错的方向只剩"多拒一条"，没有"写坏远端"**。接线只需把 `SecureItem` 挂上队列的 `Candidate`，`candidate.Entry is null && !deletion ⇒ refused` 那条分支整个删掉（它下面已经没有"没有编码器"这一档）。自证 20 条全走生产解码器（`BitwardenSecureItemPayloadBuilderTests`）：每条正向用例先**让真解码器从一份手搓的远端 cipher 拉出一行**——那才是编码器唯一该承诺推得出去的本地形状——编辑它、编码、把编出的 JSON 反序列化回 `VaultCipherDto` 再喂回同一个解码器，断言指纹相等＋关键值逐项相等。**负控两次都打中**：① 摘掉 `EnsureRoundTrips` 的相等判断 ⇒ 12 条"该拒"的立刻红；② 把笔记投影里的 `isMarkdown` 改写成 `true` ⇒ 恰好 2 条红（笔记往返 + "markdown 笔记该被拒"），第二条正说明那条拒绝不是随手写的。**边界（别读成"三类全通"）**：只有**已经长成 Bitwarden 形状**的行走得通——本地新建的卡默认 `DEBIT`、带标签的笔记、有签发日期的证件继续只计数不推；`CanEncode` 仍然只问 login，而"上传 N 项到 Bitwarden"那个批量菜单也只收 `CanEncode` 的行，所以 secure item 至今**没有 create 路径**（没有 cipher id 就扫不到它，这是同一件事）。顺手把队列测试里的 `BoundNoteCipher` 改成解码器真正会留下的形状（`ItemData` 是 save 载荷而不是 `{}`），否则"笔记推得出去"是靠一份假数据绿过的。单测 871→**892**（往返 3＋载荷形状 1＋拒绝 15＋队列 e2e 1），常规通道 892/892 全绿。门禁：格式收敛一次后 0 改动、重新 publish jit 产物真跑绿（`CANONICAL VAULT passed`、loadMs=131/4000、KeePass 20000 条 openMs=806 增长 4.0MB/24、锁定尾窗 103.8MB/120、锁/解 25/14/1/4）、商业发布门 `cr_rc=0`（同一份产物上重跑：NuGet 漏洞审计过、Release 构建 **0 warning / 0 error**、常规 892/892 + 顺序通道 10/10、UI 235 + UI 性能 17，末行 `Commercial release verification passed.`）。**边界重申**：`Notes` 与 `ItemData` 里正文的一致性从此是**推得出去的前提**（不一致的行会被判成"含 Bitwarden 装不下的内容"而永远留在本地），所以将来若给笔记加"仅本地"的字段，必须同时接受它推不出去。 |
| `40e9ae8` | **笔记/银行卡/证件第一次有 create 路径（#105，收掉上一行末尾那句"secure item 至今没有 create"）**。上一轮的编码器只服务"远端已经认识这一行"的更新；库里新建的卡/证件没有 cipher id，`LoadCandidatesAsync` 里 `item.BitwardenCipherId is not null` 那道过滤直接把它筛掉，于是"上传 N 项到 Bitwarden"对这三类**永远数不到**。这轮补齐三处：① `BitwardenLocalCipherIdentity` 多一种本地身份 `local-secure:{id}`（和 `local-password:{id}` 一样不是 GUID，不可能与服务器发的 id 相撞）；② 扫描侧换成与密码同一条规则 `cipherId is not null || !IsDeleted`（先排除"发布后还没上传就被丢进回收站"，否则会把用户刚删的东西推回服务器）；③ 处理器的 `LocalItems` 多一张 `UnboundSecureItems`，`HasPublishableEntry` 认它（重复发布或已删的 create 就地完成，不再多发一份副本），`ApplySuccessAsync` 把服务器回的 cipher id 写回这一行，之后再改内容走 update。**关键设计**：批量菜单问"这一行推得出去吗"必须和编码器**同一个判据**，所以没有再写一份条件清单，而是把三个 plan 拆成"投影（不需要密钥）+ 发射（只有加密需要密钥）"两半，`CanEncode(SecureItem)` 只走前半——于是它能在手里没有保险库密钥的界面上问，答案却和真正写出去时一致；投影判错的后果仍然只有"多拒一条"。**边界（别读成"三类都能新建上传"）**：菜单现在把 `WalletItems` 数进来，**笔记不数**——原因不是"两处判据不一致"，而是**笔记根本进不了批量选择**：库页唯一的选中来源是"全选"，它按 `VaultTreeEntryRow.IsBatchable` 跳过笔记行（`VaultBatch.cs:50-52`、`VaultTreeRows.cs:196-200` 都写明这是故意的："a note would be checked invisibly and then swept up by an action the user never saw offered"），而**库树没有行级复选框**（量过：全仓 `IsBatchable` 只有那一个消费者，`.axaml` 里零引用；行级动作走右键菜单，笔记在那里照样能编辑／移动／删除，不受影响）。于是把笔记计入发布判据在今天**永远数不到东西**。要给笔记一条发布路径，先得回答"批量动作对笔记算什么"——收藏／归档对笔记不适用，移动／删除只有单行版，这是**范围决定，不是缺陷**（记在 #106）；而且 create 推不推得出去取决于那行长成什么形状：**本地新建的卡默认 `CardTypeString = DEBIT`、有签发或到期日的证件、带标签的笔记照旧推不出去**，这不是 create 路径漏了，是投影闸门本来就在这么判。自证 +4：投影一致 1（解码器留下的三种形状 `CanEncode` 均为真）＋队列 2（一张 CREDIT 卡从"已发布、无 cipher id"一路走到服务器回 id 并写回、再扫一次安静；一张 DEBIT 卡 `Enqueued 0 / Refused 1` 且队列为空）＋真视图 UI 1（两张卡全选时菜单写 "Upload 1"，只有 DEBIT 那张时菜单**根本不出现**）。另外给 15 条"该拒"的用例各加一句 `Assert.False(CanEncode(...))`，让"菜单不出现"与"编码器拒绝"从此是同一件事的两面。**负控**：把扫描侧过滤退回 `is not null` ⇒ 恰好 2 条红，正是那两条新队列测试。单测 892→**895** 全绿（单独跑 UI 时 `BackgroundMemoryUiTests` 那条已知的 GC 回收计时 flake 红过一次：预算 2500ms、实测 2592ms，与本轮无关，门禁串跑下同一条 236/236 绿）。**踩过的坑记一笔**：`dotnet format` 之后用 `--no-build` 跑测试，那两条新测试红了，重新构建后 895/895——格式收敛改了源文件时间戳，`--no-build` 测的已经不是刚才那份代码。门禁：publish jit 与产物运行时门 `pub_rc=0 / rt_rc=0`（`CANONICAL VAULT passed`、loadMs=183/4000、KeePass 20000 条 openMs=918 增长 4.1MB/24、锁定尾窗中位 111.5MB/120）、商业发布门 `cr_rc=0`（同一份产物：NuGet 漏洞审计过、Release **0 warning / 0 error**、常规 895/895 + 顺序通道 10/10、UI 236 + UI 性能 17，末行 `Commercial release verification passed.`）。 |
| `979e6d0` | **笔记第一次有自己的发布入口（#106，收掉上一行末尾那句"需产品决定"）**。用户选定"笔记单条入口"：门开在**已经拿着这条笔记的那个编辑器**的工具栏溢出里，批量菜单照旧不收笔记（那是故意的，不是漏）。新增 `MainWindowViewModel.BitwardenNotePublish.cs`：`BitwardenNotePublishOffered` 只在有已连接账户时为真；命令先 `CaptureNoteEditorState` + `SaveNoteTabAsync`（**存草稿不是走过场——编码器判的是库里那一行，草稿还不是那一行**），再问 `BitwardenCipherPayloadBuilder.CanEncode`：不通过就写失败态（`BitwardenPublishNoteNotCarriable` 中英各一份）并**就地返回**——什么都没出门，笔记照样能用；通过才盖 `BitwardenVaultId`、保存、`RebuildVaultTree`，然后交给常规漂移扫描上传（所以"发布时没网"这件事仍然成立，下一轮同步补完）。`RaiseBitwardenPublishState` 顺带把 `BitwardenNotePublishOffered` 一起通知掉，`BitwardenPublish.cs` 那段"为什么这里不数笔记"的注释改成现在真正的原因。**自证方式换了个赛道，值得记一笔**：`LibraryUiHarness` 只把 `IsUnlocked` 设真，任何真写库都抛 `A usable default MDBX vault is required...`（实测过），所以三个分支跑在 `BitwardenSyncWorkflowUiTests` 里——`CreateFixture(..., repository:)` 收一个 `DispatchProxy.Create<IMonicaRepository, ...>()` 替身（替身类不能 `sealed`；`SaveSecureItemAsync` 返回 `Task<long>`，返回类型不对会被生成的代理直接 `InvalidCastException`），配上文件里本来就有的 `FakeSyncCoordinator`，于是**不碰数据库也不碰网络**就把写路径真跑了一遍：markdown 笔记"存了但绝不盖戳"（写次非空 + 所有 `StampedVaultIds` 为 null + `IsStatusMessageFailure`，按语义判而不按已翻译文案判，见 §走查那几行），改成可承载的笔记后最后一次写带 account 7、`Source.BitwardenVaultId == 7`、`coordinator.GetState(7).Phase == Completed`。**负控两条各打中一次**：去掉盖戳 ⇒ 该测试红；绕过编码器判断 ⇒ 该测试红；改回后 sha 一致、重跑绿。另在 `NoteWorkflowUiTests` 补一条"门只在有账户时出现"（工具栏那条只能声明级：溢出菜单的项要点开才绑定，所以断的是 XAML 里声明的 `Command`/`IsVisible`，注释写明没有假装驱动活行）。**踩过的取证坑**：整串 UI 跑用 `-reporter quiet` 时**什么都不打印**（只留一行横幅），"没输出"不等于"跑过"；换 `verbose`/`silent` 才拿到 `Total: 255, Failed: 0`，`-reporter long` 非法取值会 rc=3 静默不跑——三条都写进了 §常用命令。**尚未验证（别读成"和真服务器验过"）**：这条入口推出去的动作至今只在替身传输下绿过，缺口 4 原样还在。门禁（就在 `979e6d0` 这份字节上重跑）：格式 0 改动、Release **0 Warning(s)**、常规单测 895/895 + 顺序通道 10/10、UI 常规 238 + UI 性能 17（独立跑一次 `Total: 255, Failed: 0`，新测试在清单里）、`cr_rc=0 / pub_rc=0 / rt_rc=0`（`CANONICAL VAULT passed`、loadMs=271/4000、KeePass 20000 条 openMs=2096 增长 3.6MB/24、锁定尾窗中位 108.4MB/120、锁/解 25/14/1/4、末行 `Commercial release verification passed.`）。
| `9d9db9d` | **本机自托管的服务器第一次连得上（#94 缺口 4 的前置，不是它本身）**。用户选的是"本地起 Vaultwarden 来验真服务器"，而这条路在此之前根本进不去：`MainWindowViewModel.BitwardenProperties.cs` 的 `CanAuthenticateBitwarden` 与 `BitwardenEndpointPolicy.ValidateBaseAddress` 两道都写死 HTTPS，Vaultwarden 默认端上就是明文 HTTP。规则收成**一处判据** `BitwardenEndpointPolicy.IsTransportSecured`：HTTPS 一律放行，明文 HTTP 只认环回（`localhost`、`127.0.0.1`、`[::1]`、`ip6-loopback`，走 `IPAddress.IsLoopback` 而不是比字符串前缀），其他任何主机仍必须有 TLS。连接按钮与端点校验共用同一个函数，所以"按钮点亮了、下一步才被地址校验拒掉"这种不一致从形状上就不成立。三条面向用户的文案（`BitwardenConnectionDetailsDescription`、`BitwardenSecurityNoticeMessage`、`BitwardenSecureConnectionRequired`）中英各改一份，因为原句写的是"只允许 HTTPS 端点"，改完再照字面读就是假话。**自证 2 条**：单测钉判据本身（环回三种写法与任意 https 为真，`192.168.1.20` 与公网主机为假；`CreateSelfHosted("http://localhost:8080/")` 真的产出 `/identity/` 与 `/api/`，`http://vault.example.test` 仍然抛）；UI 那条钉在用户经过的地方（同一个 VM 连改三次地址：本机 http 可点、局域网 http 不可点、官方 https 可点）。**负控两道方向对称、各打中一次**：摘掉环回那一半 ⇒ 两条新测试红在正向断言（单测红在 `BitwardenProtocolException: Bitwarden serverUrl must use HTTPS, or plain HTTP on this machine.`，UI 红在 `Assert.True() Failure`）；把规则放宽成"任何 http 都放行" ⇒ 红在反向断言（单测 `Assert.False() Failure` 与 `Assert.Throws() Failure: No exception was thrown`，UI `Assert.False() Failure`），并顺带把既有的 `EndpointsRequireHttpsAndRejectAmbientAuthorityData` 一起打红——那条老测试确实是这道门的第二双眼睛。改回后文件 sha 与动手前一致。**这一行没把"和真服务器验过"划掉**：Vaultwarden 仍未起来（Docker Desktop 装着但引擎是停的；1.37.3 的 GitHub Release 页只有 attestation、没有裸二进制，所以只能走镜像），缺口 4 原样保留，只是从"门不让进"变成"进得去、还没进"。门禁（就这份字节）：格式 0 改动、常规单测 896/896 加顺序通道 10/10、Release 0 Warning、`cr_rc=0 / pub_rc=0`（`Commercial release verification passed.`）、UI 独立跑 `Total: 256, Errors: 0, Failed: 0`。**产物真跑门第一次红、同一份产物复跑绿**：首跑 `rt_rc=1`（复合行 `release gate completed. success=False, loadMs=265`，打印出来的各子结果都是 True，**红在哪一项子结果没查清**），复跑 `rt2_rc=0`（`CANONICAL VAULT passed`、loadMs=268/4000、KeePass 20000 条 openMs=2200 增长 5.4MB/24、锁定尾窗 108.8MB/120、锁/解 25/14/1/4）。按既有结论（单次读数不可信、先复跑取分布）这不算把红蒙过去，但下次同一产物两跑不一致时要优先把那一项定位出来。 |
| `916e578` | **无远端修订号的本机改动不再一声不响地被盖掉（#113，被下一行那轮的实测抓出来）**。形状：一条绑着 cipher 却拿不出远端 revision 的行，写回队列按规矩拒绝（送出去就是无守卫的覆盖），而**同一轮的 pull 把它判成普通更新**并用服务器那份盖回来——真服务器量出 `s18b_sync_no_revision merge[Updated=1/ConflictsBackedUp=0]`、冲突备份 `named=0`、按 cipher id 查 `any_conflict_for_cipher=0`，改动到此连一条痕迹都不留。为什么合并引擎只能靠内容判："这台设备改了"在普通编辑路径上从不置 `BitwardenLocalModified`（#94 量过），于是唯一的证据就是内容与远端不同，而"不同"分不清是服务器动了还是本机动了。判据只补一条：`local.RevisionDate` 为空且内容不同 ⇒ 先留一份可恢复的备份再应用远端；内容相同是例外——没有东西要留，留了反而每轮凭空造出一条什么也没报的冲突。覆盖 4 条：`BitwardenMergeTests` 2（判据本体，含"不该备份"那半）+ `BitwardenPullMergeServiceTests` 2（真 SQLite + 真仓库 + 真 pull：备份里取回本机那句标题、应用后的行带远端 revision；同一行改成与远端一致 ⇒ `ConflictsBackedUp=0` 且备份表空）。**负控真打**：摘掉那条分支 ⇒ 恰有 2 条红，正是那两条"必须备份"（Core 那条红在动作判回 `ApplyRemoteUpdate`，Data 那条红在 `ConflictsBackedUp` 的计数），两条"不该备份"照旧绿；还原后文件 sha 与动手前一致。同一 stage 在两版二进制上跑出**相反判决**（`s19b-run.log` 前／`s19c-run.log` 后），见文末第七轮实测。**边界**：这条只服务"被拒而推不出去"那一档，有 revision 可守卫的编辑仍走队列推送、不撞这里；`MissingRemoteRevision` 从哪来（身份取自服务器、revision 却没有）本轮没有回验来源，v1 继续拒绝推送而不是补读一次远端。 |
| `d1fdf59` | **Bitwarden 收不下的本机改动第一次带着原因走到界面（#112，闭上 §7 那条可诊断性缺口）**。旧形状：`EnqueueDriftedAsync` 算出 `Refused` 就把它丢掉、队列不记账、同一轮 pull 再用服务器那份盖回那一行，屏幕上写的是"已同步"——一条永远推不出去的改动唯一的线索是探针自己打印的 `localRev=(none)`。新增 `src/Monica.Core/Bitwarden/BitwardenPayloadRefusal.cs`：六个原因码（`MissingRemoteRevision`／`UnsupportedShape`／`HasAttachments`／`MissingTitle`／`UnsupportedContent`／`PayloadTooLarge`，每一个都是用户接受得了或改得动的状态）、`BitwardenPayloadRefusalException : BitwardenProtocolException`（**子类化而不是替换**，因此既有 catch 一处不改：队列照旧停车、测试照旧断言被拒、协调器照旧消毒消息）、`BitwardenPayloadRefusalInfo`（码与给日志的那句绑在一起，判据和解释不能分头漂）、`BitwardenUnsyncableLocalChange(Title, IsPassword, Reason)`（**只带标题和码，永远不带密钥字段**——这份列表会走到同步页与诊断日志）。编码器 11 处拒绝点各自点名（`UnsupportedShape` 5、`MissingTitle` 2、`PayloadTooLarge` 2、`HasAttachments` 1、`UnsupportedContent` 1），队列那一处 `MissingRemoteRevision` 由判据自己带上；只报了字段名而没分类的老闸门归入 `UnsupportedContent` 并保留原句，所以"有闸门但没分类"也漏不掉。`BitwardenLocalChangeQueueResult(Enqueued, Refused, Unsyncable)` 里 `Refused == Unsyncable.Count` 由构造钉住（只读计数的调用方无法少报它即将展示的清单）；协调器把它带进 `BitwardenSyncResult.Unsyncable`；App 侧新 partial `MainWindowViewModel.BitwardenUnsyncableChanges.cs` + `BitwardenSyncSourceView.axaml` 的 `BitwardenUnsyncableItem` 段落（12 个 key 中英各一份）。可见性按**账户 id** 判而不是"选中变空就清空"——账户列表几乎每个 Bitwarden 动作都会重载，后者会在警告出现的那一刻把它撤掉；列表**不从存储读**：拒绝是一轮的属性，那行后来被修好或被用户删掉（删掉根本不需要编码器）就必须自己从屏幕上消失，不需要谁去清。诊断日志只写 `code:count`，不写标题。覆盖：单测 +4（队列 3：名单点名条目与编码器给的那个原因／一条只因没有 revision 被拒／不再被拒就不再出现在名单上；协调器 1：`SyncAsync` 把被拒条目交给调用方）+ UI +2（真视图里段落带原因出现；只在债站着时可见）。**一条被实测推翻的前提**：动手时的说法是"这一条从此每一轮都被拒"，真服务器量出来是**拒它的那一轮**列得出来（`s18_sync_shape rows=1`）、下一轮同一形状静默（`s18_sync_shape_again rows=0`，因为本地已经没有待推的改动），唯一出路是从冲突列表取回备份、在下一次同步之前把形态改成 Bitwarden 能存的——实测 `enqueued=1 refused=0` ⇒ `Claimed=1/Completed=1` ⇒ `edit_travelled=True conflicts_left=0`；取回而不改形态照旧被拒（`s18_scan_after_restore refused=1`）。中英两句分区说明因此按实测重写，指向"形态"而不是"再同步一次"。 |
| `6e99e22` | **本机文件夹移动第一次推得出去（#114，接 #94 缺口 4 的"位置"）**。库页改的是 `CategoryId`，而漂移指纹与载荷读的是 `bitwarden_folder_id` 列 ⇒ 两边各自都"对"，移动在服务器上看不到（先量红：新测试 `Expected: 1 / Actual: 0`）。修法是一份共享投影 `src/Monica.Data/Bitwarden/BitwardenLocalFolderProjection.cs`：`BitwardenLocalChangeQueue`（构造函数多一个 `IBitwardenRemoteFolderStore`）与 `BitwardenPullMergeService.LoadLocalContextAsync` **都**先投影再算指纹，判断与载荷因此不可能各说一套。**第一版留的"本机的夹没有 counterpart 就沿用那一列"回退，被同一轮真服务器实测推翻**（每一步都 `local_folder=(none)`：那一列只有 pull 会写、而成功的 push 之后 pull 读到 NoChange 什么都不写；加上 `SavePasswordAsync` 的 `COALESCE` 让它永不清零），已改成**只从本地树推导**：绑到远端夹答那个夹，其余一律答根。覆盖 `BitwardenLocalChangeQueueTests` +4（42 条）、负控四批 + 批次4 真打（把旧回退装回去 → 恰那一条红 `Expected: 1 / Actual: 0`）。真服务器第九轮 8 项读数同形：`into_first/into_second` 各 `enqueued=1` → 服务器落点等于目标夹、`at_root=True keeps_local_folder=True`、每步 `owed_again=0 conflicts=0`（见文末第八、九轮）。**未做**：界面对"放进 Bitwarden 收不下的本机夹 = 服务器上到根"没有任何提示，镜像夹下面用户自建的子夹同属这一类。

| `517eec9` | **桌面端建出的库第一次按 Android 的形状落盘（#115，用户要求"两边创建的文件必须一模一样、完全相同"）**。先量清 Android 到底认什么：隐藏根项目 `.monica-root`（`Mdbx2VaultSessionExecutor.kt:665/673`，id = `UUID.nameUUIDFromBytes("monica-root:{vaultId}")`）、夹的父子关系**只**落在 `MdbxCollectionSummary.groupId`、条目写入一律 `execute_write_operation` + 客户端自带 id。桌面端此前既不建根、又无条件写 `mdbx_folder_id`，于是 **Android 往桌面端建的库存一条"没有分类"的条目会直接失败**（实测：向不存在的项目写条目，引擎抛 Storage 错误）。补的东西：① `MdbxAndroidRoot`（`src/Monica.Data/Mdbx/`）复刻 Java 的 v3 UUID，读写路径经 `EnsureRootProjectAsync` 惰性补齐，无分类条目落根且载荷里**不含** `mdbx_folder_id`（实测键不存在，不是写 null）；② 夹走 `CreateProjectWithIdentityAsync(clientUuid, title, parent=root)` + kind `monica-create-folder`。**最关键的一条证据不是我们自己算的**：本机有且只有两份 Android 真跑出来的 `.mdbx`（`Monica-all/.codex-tasks/20260731-mdbx2-local-create-failure/raw/`，schema 17），用只读 SQLite 读出来——`vault_meta` **15 列同名同序**、取值 `MDBX-2/17/MDBX-1/MDBX-2/multi/2/compliant/mdbx-vault-header-hmac-sha256-v1`，而两份都是 `commits=1 / commit_operations=0 / projects=0 / entries=0` 且**都没有根项目**；建库日期（07-31）晚于 Android 根功能落地（`3a3086b6` 07-28、`81ef058b` 07-29，`git log -S` 查过），`createMigrationFolders` 是 MDBX-1→2 的夹迁移不是补根 ⇒ **"建完还没写过东西"的库天生没有根**，桌面端的惰性补齐是必要的，而 Android 自己的打开路径（`:338-392` 三条只做 openVault）对这种半成品库照样会踩 §2.2 那条写失败。**六条负控真打**：① `ToFfiMode` 的 `Multi` 临时接到 `Power` ⇒ 外壳测试立刻红，而且红在 `tiga_compliance_status`（`Expected: compliant / Actual: remediation-required`，比 `default_tiga_mode` 更早炸）——建库模式会决定合规位，要对齐的不止那一格；② 新建后先插一个 project ⇒ 空库那条红在 `Expected: projects=0 / Actual: projects=1`（连带 `commits 1→2`、`commit_operations 0→1`）；③ 版本 nibble 写成 v4 ⇒ **只有** Java 向量那组红，存储层测试全绿（两边都用自家函数算 id，对称地错——这就是为什么必须钉跨语言向量：`nameUUIDFromBytes("test")=098f6bcd-4621-3373-8ade-4e832627b4f6`、`rootProjectId("test")=a422cc5f-e505-3b65-b298-fbf94de90dd8`）；④ 拔掉父链接 ⇒ 只红 `groupId` 那一条；⑤ 不建根 ⇒ 3 条红；⑥ 无条件写 `mdbx_folder_id` ⇒ 3 条红。覆盖：`MdbxVaultShapeParityTests` 2 + `MdbxAndroidRootTests`（Theory + 形状）+ `MdbxUniffiBindingTests` 真 dll 布局 1 + `MdbxRepositoryTests` 树形 1，另删掉零读写的 `PasswordEntry.MdbxLogicalEntryId`。**诚实边界（别读成"整库互通已验"）**：那两份 Android 库的**密码不在手**，桌面端从未解锁过任何一座 Android 建的库，能对的只有外壳与写出形状；`.kdbx` 两边**不等价**（Android kotpass 0.10.0 读+写，桌面端 KPCLib 2.0.4 只导入、丢历史/自定义图标/AutoType）；桌面端 passkey 私钥在本机 SQLite 而非 mdbx 载荷，Android 看不见；Android 的 `steam-mafile` 桌面端不认识；随包 dll 来源链与那 5 个 overlay 补丁未核。跨仓规格写在 `Monica-all/mdbx-android-desktop-interop-handoff.md`（工作区根，不改 Android 代码）。门禁（就 `517eec9` 这份字节）：格式 0 改动、Release **0 Warning(s)**、commercial-release `cr_rc=0`（单测 10 `perf-budget` + **969 常规**、UI 17 + 244，末行 `Commercial release verification passed.`）、`pub_rc=0 / art_rc=0`（`CANONICAL VAULT passed`、loadMs=224/4000、KeePass 20000 条 openMs=1324 增长 **2.5MB**/24、锁定态 **112.5MB**/120、锁/解 25/14/1/4、canonical passwords=27 notes=14 categories=6 attachmentOwners=6）。**另记一条不利的取证事实**：整串单测在**同一进程并行**跑时会出两条墙钟红（`Vault_snapshot_loader_fans_out_reads_after_password_snapshot` 842ms 对上限 4×188=752ms、`Searching_a_library_of_payloads_rebuilds_the_tree_within_its_budget` 444ms 对 400ms），隔离复跑 3/3 绿、按 CI 的通道拆开跑 979/979 全绿 ⇒ 是负载计时抖动，不是新缺陷；上一轮那条"没抓到测试名的单次红"很可能就是同一族，但没有名字证据，不许写成已确认。 |

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
#
# 整串跑要 `-reporter verbose`（或 `silent`）才会打印 `Monica.UiTests  Total: N, Errors, Failed` 汇总；
# `-reporter quiet` 只打失败行——**没有输出不等于跑过**（实测一次整串 quiet 跑只留下一行横幅）。
# 同理 `-reporter long` 不是合法取值，runner 会打印 usage 后以退出码 3 结束、一条测试都没跑。
#
# UI 套能驱动真正的写路径，不需要真的 MDBX 库：`BitwardenSyncWorkflowUiTests.CreateFixture(..., repository:)`
# 接受一个 `DispatchProxy.Create<IMonicaRepository, XxxProxy>()` 替身（注意替身类不能 `sealed`，
# 且写方法的返回类型要与接口一致，`SaveSecureItemAsync` 是 `Task<long>`），配上 `FakeSyncCoordinator`
# 就不碰数据库也不碰网络。模板见 `Note_publish_stamps_only_a_note_the_encoder_can_carry`。
# 反过来说：`LibraryUiHarness` 只是把 `IsUnlocked` 设真，任何真写库都会抛
# `A usable default MDBX vault is required for canonical business-data operations.`

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
    1. ~~**完全没有写回通道**~~ **已修（`94adacd` 编码器 + `5cf27c9` 接线 + §2 最后一行补上"本地新建"）**：原状况是
       `IBitwardenPendingOperationStore.EnqueueAsync` 在 src/ 里零调用点（只有它自己的定义与 7 处测试），
       下游 `BitwardenMutationProcessor` 永远在抽一个空队列。现在协调器在拉取之前先扫一遍漂移并入队。
       ~~**仍然缺的**：新建条目连 `BitwardenVaultId`/`BitwardenCipherId` 都不会被赋值~~ ⇒ 库页批量菜单多了一项
       "上传 N 项到 Bitwarden"，把勾选的本地独有条目打上 `BitwardenVaultId`（`BitwardenCipherId` 留空），漂移扫描
       为它入队 **create**，服务器回的 cipher id 写回本地，之后同一行改内容走 update。~~**本地删除也不会传播**~~
       **已修（§2 的删除传播那行）**：原状况确实如旧记录所说——`BitwardenCipherPayloadBuilder.cs:88` 拒收
       `IsDeleted`，把绑定条目移进回收站只算"欠一次但推不了"；而 `IsDeleted` 在载荷指纹里，所以每轮同步都会
       把这当成"本地改了没上传"，既把条目还回来又留下一条冲突备份。~~**仍然缺的**：笔记/银行卡/证件这三类 secure item
       没有**内容**编码器~~ ⇒ **已修（`9b14bdb`）**：三类都有出站载荷，编码器在发出前把载荷**投影回下一次拉取会留下的那一行**
       并比指纹，不相等就抛，所以"改笔记/改卡/改证件"推得出去而**不会被下一次拉取改写**；代价是只有已经长成 Bitwarden
       形状的行可推（带标签的笔记、`DEBIT`/有账单地址的卡、有签发到期日期的证件仍只计数不推）。
       ⇒ "在 Monica 里改密码其他客户端看不到"这半句已经不成立，"新建"、"删除"、这三类的**内容改动**都修完了；
       ~~剩下的两件事是：secure item **没有 create 路径**（`CanEncode` 只问 login，批量上传菜单也就收不下它们），以及缺口 4。~~
       ⇒ **已修（`40e9ae8`）**：这三类第一次能**新建上传**——本地身份 `local-secure:{id}`、扫描侧与密码同一条规则
       （`cipherId is not null || !IsDeleted`，所以"发布后没上传就被丢进回收站"不推）、服务器回的 cipher id 写回那一行。
       批量菜单问"推得出去吗"与编码器是**同一个判据**（plan 拆成"投影不需要密钥 / 发射才需要密钥"两半）。
       **笔记仍进不了批量上传菜单，但这不是漏**：库页唯一的选中来源是"全选"，它按 `IsBatchable` 跳过笔记行（故意的，
       见 `VaultBatch.cs:50-52`），库树也没有行级复选框 ⇒ `NoteItems` 里永远不会出现 `IsSelected`，把笔记计入判据
       也数不到东西。要開这条路径需要先做一个产品决定："批量动作对笔记算什么"（收藏／归档不适用，移动／删除只有单行版）
       ——记在 #106，是范围决定不是缺陷。
       ⇒ **已定并已实现（`979e6d0`）**：用户选了"笔记单条入口"——发布笔记的门开在**拿着这条笔记的那个编辑器**的工具栏溢出里
       （`MainWindowViewModel.BitwardenNotePublish.cs` + `NoteEditorToolbarView.axaml`），批量菜单照旧不收笔记。
       于是缺口清单现在只剩两条：永久删除（`Delete` 无生产者，#107）、缺口 4（从未对真服务器验过）。
       ~~永久删除~~ ⇒ **已实现（#107，见下面第 4 条末尾的"已实现"块）**，清单回到只剩缺口 4 的未验项。
       ⇒ **2026-09-24 更新（见文末"真服务器第二轮实测"）**：缺口 4 的主链路已经对真 Vaultwarden 1.37.3 验完
       （登录／建／改／软删／收敛／远端改→本地／冲突备份→还原→再推），量出来一条**新的**产品缺陷 #108
       （"从回收站还原"推不出去：update 不能把 cipher 拿出服务器回收站），永久删除的路由也当场量通了（#107 的预飞已完成）。
       ⇒ **2026-09-25 更新（见文末"真服务器第三轮实测"）**：**#108 已修并在真服务器上按三种口径验通**（新增 `Restore`
       操作档 + `local-restore:` 键 + `PUT /ciphers/{id}/restore` 无体 + 撤销竞态闸门）。真跑还量到 `restore` 对未知
       cipher 答 **400 不是 404**（#107 那条"404 即终局停放"认不了这种），以及对活条目再 restore **还会推高一次 revision**。
       顺带抓到一条**比 #108 更严重的缺陷并修掉（#109）**：pull 落库三处拿 `decision.LocalId` 按"先查密码表、否则查
       安全条目表"解析，而两张表各自独立编号 ⇒ 属于笔记的远端删除落到同号的在用登录行上（实测那条登录几轮里
       `live→del→live→del`），笔记自己的行 revision 永不前进——那才是"笔记还原推不出去"的真因。现在一律按 cipher id
       定位，笔记 restore 在真服务器上收敛（`Restore/Completed` + 下一轮 `Claimed=0`）。
       删除那一轮又量出两个**同族的死循环**（都已修，见 §2 回收站标记那行）：解码器对服务器回收站里的 cipher
       **不带载荷**、`PayloadHash` 位置写的是 `deleted:{revision}` 标记（`BitwardenCipherDecoder.cs:63-73`），
       于是 ① 合并引擎把"两边都在回收站"当成内容不一致，每拉一次就多存一条冲突备份；② 漂移扫描拿本地指纹去比
       那个标记，永远"欠一次删除"（实测 `Enqueued 1`，安静库里）；③ 顺带量出安全闸门只数**活着的**远端条目，
       所以"用户把整库都删进服务器回收站"会被 `EmptyRemoteVault` 直接抛异常，此后每次同步都失败。
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
       得先拿用户点头。删除传播上线后又添了两条同类未验项：① `PUT /ciphers/{id}/delete` 这个路由（官方
       客户端用它软删、把 `DELETE /ciphers/{id}` 留给永久删除）只在替身下绿过；② **对一台已进回收站的
       cipher 再发 update** 是"用户在这台恢复了、远端还躺着"那条路会做的事，真服务器答什么没人知道
       （本轮的选择是如实尝试让下一次同步把它带回活区，而不是悄悄留着）。③ 整条"服务器回收站"读法建立在
       **sync 响应会把已删 cipher 一起带回来（带 `deletedDate`）** 这个假设上——真服务器若干脆省略它们，
       本地那一份会走进 `PreserveLocalUnmatched`（保留、不删），同步不会坏，但"别处删了这里也跟着进回收站"
       就不成立。永久删除（Monica 里从回收站
       彻底清除）**要传播**（用户已定"传播"，#107）——不再是"故意的没有生产者"。可执行规格已量清，
       冷开工照下面做即可（**下面 ①-⑤ 已全部照做并负控验证，实现与仍未验的分账在本条末尾"已实现"块**）：
       ① **传输层不用动**：`BitwardenMutationHttpTransport.SendAsync:83-96` 已把 `Delete` 走
       `DELETE {Api}ciphers/{id}`，preflight（`:62-81`，`Update or Delete or SoftDelete` 一并）、
       `WritesNoBody`（`:240-245`）、`BitwardenMutationGuard.ValidateResponse`（`BitwardenMutationContracts.cs:127-133`
       对两种删除免除"必须回 revision"）三处都已覆盖，`BitwardenPendingOperationStore.Mapping.cs:50,59`
       的 `"delete"` 字符串往返现成，认领顺序（`BitwardenPendingOperationStore.cs:137-142`）还把它排在
       create/update/soft_delete **之前**。
       ② **缺的是生产者，而且不能指望漂移扫描**（实测结论）：永久删除会把行写成 tombstone，
       `CreatePasswordTombstone/CreateSecureItemTombstone`（`MdbxBackedMonicaRepository.cs:1332-1351`）只留
       `Id/Mdbx*Id/IsDeleted/DeletedAt=UnixEpoch`，**`BitwardenVaultId` 与 `BitwardenCipherId` 一起丢掉**，
       而队列与处理器一律按 `BitwardenVaultId == vaultId` 过滤（`BitwardenLocalChangeQueue.cs:149,159`、
       `BitwardenMutationProcessor.cs:185-202`）⇒ 扫描永远看不见被清除的行。所以必须**在清除的那一刻入队**。
       入队位置要盖住**两条**路径：`RecycleBinCommands.cs:82`（`PurgeDeletedPasswordGroupAsync` 走这条，
       **绕开** core 方法）与 `RecycleBinUnifiedCommands.cs:80/95`（批量、清空回收站、到期自动清理都经这条）；
       另有 `VaultSmokeReadback.cs:210` 直接调仓储。VM 里已注入 `IBitwardenPendingOperationStore`
       （`MainWindowViewModel.cs:81,106`）但没有本地队列服务、手里也没有密钥 ⇒ 干净做法是加一个小的
       data-layer 服务，按软删的既有形状写：载荷 `"{}"`、带 `ExpectedRemoteRevision`。
       ③ **两个已经量到的坑**：a) 幂等键**不能复用** `local-delete:{vaultId}:{identity}`
       （`BitwardenLocalChangeQueue.cs:103-107`）——store 的 `ON CONFLICT(idempotency_key) DO UPDATE SET ... status='pending'`
       会把一条**已完成**的软删原地改回 pending（`BitwardenPendingOperationStore.cs:69-83`，完成的行不删），
       要么另起前缀（如 `local-purge:`）要么明确接受"purge 覆盖 trash"这个语义；
       b) `ValidateForQueue`（`BitwardenMutationContracts.cs:100-107`）对 `Delete` 同样要求 revision，
       所以 `BitwardenRevisionDate` 为 null 的行只能像扫描那样**拒绝入队**，不能裸发。
       ④ **成功后要收的尾**：`IBitwardenSyncStateStore` 今天**没有**按 cipher 删除基线的方法（只有整库
       `ReplaceForVaultAsync`，`BitwardenSyncStateStore.cs:38-77`），而 `ApplySuccessAsync:149-157` 只要
       `LocalPayloadHash` 非空就会给一个已不存在的 cipher `AdvanceAsync` 一条基线 ⇒ 硬删要么以
       `LocalPayloadHash: null` 入队，要么补一个 `RemoveAsync`。残留基线的真实代价不是幻影计数，而是
       "冲突还原把那一行复活"（`BitwardenConflictRestoreService.cs:80-107` 会重新绑回同一个 cipher id）
       时，它既推不出去（扫描 `:54` "没有基线就不推已绑定行"）、拉回来又落进 `PreserveLocalUnmatched`
       ⇒ 一个永久的本地幻影。远端 404（早就没了）今天会被 `BitwardenRetryPolicy.cs:18` 判成 `Validation`
       而**停在终态 failed**，"已抹掉即完成"要显式特判。`EmptyRemoteVault`/`SharpDataReduction` 已核过
       不会被硬删新触发（`BitwardenPullSafetyEvaluator.cs:68-85` 数的是活着的绑定行，硬删只把两边一起降下来）。
       ⑤ **自证模板**：`BitwardenLocalChangeQueueTests.cs:258-303`（真 SQLite + 真基线库 + `AcceptedTransport`
       走完"入队→发送→下一轮扫描安静→确认拉取 0 冲突"）与 `:444-474`（secure item 只欠路由不带载荷）；
       负控至少两条——去掉入队生产者 ⇒ 队列测试红；让硬删复用软删的幂等键 ⇒ "下一轮扫描安静"那条红。
       **仍然未验**：~~`DELETE /ciphers/{id}` 至今只在替身 HTTP 下绿过，缺口 4 原样。~~
       ⇒ **已验（真 Vaultwarden 1.37.3）**：`DELETE /ciphers/{id}` 真服务器 2xx，回收站内外的 cipher 都删得掉；
       但**必须带当前 revision**，否则被我们自己 `BitwardenMutationHttpTransport.cs:62-80` 的 preflight 挡成
       409，服务器根本不会被问到（实测 `stage=update_without_expected_revision status=409 note=client_side_gate`）。
       删干净之后本地行留在原地、`BitwardenCipherId` 保留（悬空）、后续两轮 pull 稳定在 `PreservedLocalOnly=1`
       且不再动作 ⇒ 收敛，不会循环；代价是那一行从此推不动（下次编辑它推出去会撞上什么，**没验**）。
       ⇒ **已实现（#107，本轮）**：按上面 ②③④ 的规格做完，且**每一条都留了红过的证据**。
       - **生产者**：`src/Monica.Data/Bitwarden/BitwardenPurgeQueue.cs`（新）。`IBitwardenPurgeQueue` 两个方法
         只吃"行还在手上"的那一份身份，载荷 `"{}"`、`OperationType=Delete`、键 `local-purge:{vault}:{cipher}`
         （③a 的坑照规格躲开），`LocalPayloadHash: null`（④ 的坑：不给一个已经不存在的 cipher 记基线）。
         `vaultId`／`cipherId`／`BitwardenRevisionDate` 任一为空即**返回 false 不入队**（③b：无基线可守卫就不裸发）。
       - **接线**：`MainWindowViewModel.BitwardenPurge.cs`（新 partial）+ 三个清除收口点各一行——
         `RecycleBinCommands.PurgeDeletedPasswordGroupAsync`（②里那条**绕开** core 的路径）与
         `RecycleBinUnifiedCommands.DeleteRecycleBinItemPermanentlyCoreAsync`（单条清除／清空回收站／到期自动清理共用这条）。
         DI 在 `App.axaml.cs` 注册。**`VaultSmokeReadback.cs:210` 故意没接**：那条探针行从来不绑 vault，接上是死代码。
       - **④ 的收尾按"自愈"处理，没加 `RemoveAsync`**：残留基线由下一次**完整快照 pull** 的
         `ReplaceForVaultAsync` 整表重写自然带走（实测 `A_purged_entry_is_queued_...` 的最后一句：
         清除后的那一轮 pull 里 `cipher-edit` 不再出现在基线里）。
       - **两处处理器判断**：404 结算（④末）+ 撤销竞态闸门**收窄到只有 `SoftDelete`**——硬删没有"用户又放回去了"
         这回事，让 pull 重新加回来的行取消一次硬删，等于机器替用户撤销一个不可撤销的决定。
       - **负控真打两批**（去掉哪条机制 ⇒ 恰好哪条测试红）：批次1 让硬删复用 `local-delete:` 键 +
         把闸门重新放宽到 `Delete or SoftDelete` ⇒ 红 3 条（键前缀断言 / 期望 2 行实得 1 行——`ON CONFLICT`
         把已完成的软删原地复活 / `Sends` 期望 1 实得 0）；批次2 拔掉生产者（入队一律 false）+ 删掉 404
         结算分支 ⇒ 红 4 条（前三条 + "404 即完成"那条），而"该拒就拒"那条在两批里都保持绿，
         说明它盯的是拒绝条件、不是被改的机制。规格要求的"去掉入队生产者 ⇒ 队列测试红"是批次2 抓到的。
       - **诚实边界（三条，别当已验）**：① App 层接线**当时**只有 Data 层证据 +
         真服务器上手工走的那条 `Purge.EnqueuePasswordAsync`（UI 无头 harness 只 fake `IsUnlocked`，而队列行要真实
         `bitwarden_vaults` 行才过外键；按本仓惯例也不写"读源码文本"的断言测试）。**注册那一半现在有了容器级证明**
         （`6bbf449`：从生产容器解析 `IBitwardenPurgeQueue`／`IBitwardenPullMergeService`／
         `IBitwardenPendingOperationStore` 三个实现类型；反证正是它要防的那个形状——删掉 `App.axaml.cs:190`
         那行注册重新构建，容器**照样建得出** `MainWindowViewModel`（队列参数是可选的 ⇒ 静默拿到 null），
         而这条断言即刻红）。**行为那一半仍是缺口**，要跨过三道墙：唯一公开入口是 `[RelayCommand] DeleteRecycleBinItemPermanentlyAsync`，它前面挡着打字确认
         对话框（`_confirmationDialogService.ConfirmTypedAsync`，可在 `CreateFixture` 的覆盖回调里顶掉，需要多传
         一个参数），核心方法 `DeleteRecycleBinItemPermanentlyCoreAsync` 是 private，而队列行要真实
         `bitwarden_vaults` 行才过外键 ⇒ 需要一个已解锁、带绑定回收站行的 MDBX 库（按本仓惯例也不补一条"读源码文本"的断言测试）。② ~~**硬删推送失败而 pull 成功**的那一岔：合并引擎的 `AddRemote` 会把已清除的条目
         **在本地重新长回来**（远端有、本地无 ⇒ AddRemote）。干净修法是对"欠一次 erase"的 cipher 抑制 AddRemote，
         需要把 `IBitwardenPendingOperationStore` 注进 `BitwardenPullMergeService` 并决定要不要给用户一条提示——
         **未做、未测**~~ **已修并双重自证（`12ed5a9`，实机读数见文末第五轮实测）**：抑制判据是"本账户有一条
         `Delete` 且状态不是 `Completed`"，`IBitwardenPendingOperationStore` 走**必需**参数注入合并服务，计数走
         `BitwardenPullMergeResult.SuppressedResurrections`。⇒ **#111 接上了这条缺口的另一半（`39bb145`，实机读数见文末第六轮实测）**：那条判成 `Conflict`／`Failed` 的 erase 现在在同步页有一条可见的决定"把服务器那一份放回本机"，走 `BitwardenStuckEraseService.AbandonAsync`⇒`CompleteAsync`，正好让上面的抑制判据让路；v1 只给这一个动作，"强行再删一次"要的是服务器当前 revision，而 `bitwarden_sync_state` 不存它，`BitwardenMutationGuard` 因此拒绝无基线的删除。③ ~~404 结算分支的状态码口径来自替身~~ **已实测并当场改判（`201501e`）**：真
         Vaultwarden 对"手里没有的 cipher"答 **400，不是 404**（GET／DELETE／PUT \/delete 三条全 400，
         一个从未存在的 id 也答 400，而同一探针在活 cipher 上的控制组是 200 —— 所以 400 不是探针自己把 URL
         打错了）。按 404 写的分支在这台服务器上是**死代码**，erase 落成 `Delete/Failed/validation` 永久停放
         （实测第一轮 `mutations[Claimed=1/Completed=0/Failed=1]`）。判据放宽成"删除类操作的 preflight 读到
         400 或 404 ⇒ 已抹掉即完成"，**只限两种删除**（请求只有路由 + id、没有体，400 不可能是载荷写错；
         update 吃 400 仍然是失败，同一条测试在同一个批次里两头都钉住）。改后真服务器复跑：
         `mutations[Claimed=1/Completed=1/Failed=0]` + `Delete/Completed` + 下一轮 `Claimed=0`。
         顺带在真服务器上确认了另两条本轮的设计假设：`s15_scan_after_purge enqueued=0`（漂移扫描对 tombstone
         确实全盲）而 `s15_producer queued=True`，以及清除后的那一轮 pull `baseline_present=False`
         （④ 的"自愈"说法成立）。跑法与全部读数见文末"真服务器第四轮实测"。
    5. ~~**一条疑似真缺陷：非 login cipher 编号**~~ **核对后判定：本仓是对的，别改**。上一轮记成"官方是
       `2=Card / 3=Identity / 4=SecureNote`"，那句话本身就是错的——Bitwarden 的 `CipherType` 是
       `Login=1 / SecureNote=2 / Card=3 / Identity=4 / SshKey=5`。证据取只读事实来源 Android 仓两处：
       `PasswordEntry.kt:146` 的列注释 `1=Login, 2=SecureNote, 3=Card, 4=Identity`，和
       `bitwarden/mapper/SecureNoteMapper.kt:14` 的 `Monica SecureItem (NOTE) <-> Bitwarden SecureNote (Type 2)`。
       与本仓 `MatchesSecureItemType` / `ToCipherType` 完全一致，**不需要动任何代码**（这条留着是因为差点
       顺手"修"它就会把三处自洽的映射改坏）。顺带记一份现成参考：Android 那批 `bitwarden/mapper/*.kt` 是
       这三类 cipher 的**出站载荷**写法（含 `toCreateRequest`），桌面端还缺的正是这个编码器。
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
**发布产物**上真机量过。Bitwarden 那条（"能不能当完善的第三方客户端、别出同步问题"）现在收到了 `979e6d0`：写回
接线、被拒推送不再堆垃圾备份、冲突能看见也能拿回来、本地新建与本地删除都会传播、拉回来的改动当场可见、
四类条目（登录/笔记/银行卡/证件）都有出站载荷与 create 路径，笔记另有编辑器里的单条"上传"入口（#106）。
剩下的只列在 §7 那条缺口清单里，两条：**永久删除传播**（#107，规格已量到行号，照 §7 那段冷开工即可，
关键结论是"传输层已就绪、缺的是**清除那一刻的入队生产者**，因为 tombstone 丢了 Bitwarden 身份"）、
以及~~**从未对真服务器验过**（缺口 4）~~**已验**（2026-09-24 本地 Vaultwarden 1.37.3，见文末两轮实测：主链路全绿，
新量出 #108"回收站还原推不出去"；2FA／captcha／设备 OTP／离线队列恢复仍未验）。
用户排队点名的两条现在只剩一半：忘记密码只出厂了非破坏的那一半（应急包 `6613df6`），
**破坏的那一半（锁定态重置为空库）不要自己开工**——它卡在 §7 那条"只是本地"与"mdbx 不要动"的语义矛盾上，
下一轮第一件事是拿这个问题问用户，第二步才是那条不需要任何破坏动作的锁定态"忘记密码？"入口。
滚动条那条（`45f01ef`）的打脸读数已经查清并修掉了（`49431a6`，#93）：滑块确实被 FluentAvalonia 主题
按类型钳掉到三分之一，派生 `SlimScrollBarThumb` 逃出后离屏与真屏都是 4 DIP→6 设备像素，hover 加宽
也确认生效，守卫与负控都在。**只剩外观口味本身要用户在运行的应用里看一眼**——不需要再开调查任务。
其余可挑的活在各条末尾那份"仍未做/仍未验证"清单里，不必再花时间复验已绿的部分。

---

## 附：本地起 Vaultwarden 验真服务器的执行清单（缺口 4 的下一步，**已执行，结果见下面两节**）

前提说清：这一步要动本机的 Docker Desktop（已安装，引擎是停的）。已经就此问过用户、**答复还没拿到**，所以没人替它启动。1.37.3 的 GitHub Release 页只有 attestation、没有裸二进制，所以只能走镜像。

1. 起引擎：启动 `C:\Program Files\Docker\Docker Desktop.exe`，轮询 `docker version` 直到拿到 Server 版本（冷启实测过的量级是 30–90s，别把第一次连接失败当成装坏了）。
2. 起一次性服务（数据不出机器）：`docker run -d --name vw-probe -p 127.0.0.1:8080:80 -e SIGNUPS_ALLOWED=true -e DOMAIN=http://localhost:8080 -v vw-probe-data:/data vaultwarden/server:latest`。绑 `127.0.0.1` 而不是 `8080:80`，是为了即使防火墙没拦住也只暴露给本机；App 侧现在连得上明文环回（`9d9db9d`），所以不再需要自签证书。
3. 建测试账号：走 `POST /identity/accounts/register`，载荷用文件经 `--data-binary @…` 传，**主密码绝不进 argv、不进日志、不进聊天、不进 git**；KDF 参数以服务器 `prelogin` 回来的为准（这正是本轮要验的事情之一）。
4. 用**发布产物** `artifacts/publish/win-x64/jit/Monica.App.exe` 配一份全新 `MONICA_APPDATA_DIR`，逐条走并记下"真服务器答了什么"：
   登录（无 2FA / 有 2FA / captcha 出现时三态）、`prelogin` 的 KDF 与本地默认不一致时的行为、拉取后库页当场可见、本地新建密码推上去并写回服务器回的 cipher id、改一条后另一端能看见、删除走 `PUT /ciphers/{id}/delete`、**对已进回收站的 cipher 再发 update**（恢复路径，服务器答什么没人知道）、发一条笔记与一张 CREDIT 卡、两端都改时冲突备份出现且能还原。
5. 判据只用布尔值与计数（`success=`、`Total=`、条数）。**不截任何含明文的图**；界面停在锁定或空库态。
6. 收尾：`docker rm -f vw-probe`、`docker volume rm vw-probe-data`，然后按实际结果改本节与 §7 缺口 4——验过就划掉，没验过的部分改成"已验到 X、Y 仍未验"，不整条勾掉。

同一条没定位的旧账顺手记在这里：产物真跑门出现过 `release gate completed. success=False` 而打印出来的各子结果全是 True，同一份产物复跑绿。**下次遇到同一产物两跑不一致，先把红的那一项揪出来**，别把复跑当结论。

## 附：真服务器第一轮实测（2026-09-24，**只跑到注册，未验成同步链路**）

按上一节的清单动了，实际状态与量到的事实：

- Docker 引擎与 Vaultwarden 都真起来了：`docker_server=27.1.1`，容器 `vw-probe`（`vaultwarden/server:latest`，`-p 127.0.0.1:8080:80`，`SIGNUPS_ALLOWED=true`，卷 `vw-probe-data`），`GET /alive => 200`。**它此刻仍在运行**，Docker Desktop 也是本次会话起的；收法：`docker rm -f vw-probe && docker volume rm vw-probe-data`，再退出 Docker Desktop。
- **一条替身给不出的真协议事实**：`POST /identity/connect/token` 若不带 `Bitwarden-Client-Version` 头，服务器直接 `auth][ERROR] Unauthorized Error: No Bitwarden-Client-Version header provided` 并回 400，同时报 `client_id cannot be blank`。本仓的客户端在 `BitwardenIdentityClient.cs:105-106` 是**发**这两个头的（`Bitwarden-Client-Name: desktop`、`Bitwarden-Client-Version: 2025.9.1`、`Auth-Email`、`device-type: 8`），所以这条真服务器的硬性要求在代码里已经满足——但**"满足"目前只有代码阅读与替身断言作保，握手本身还没成功过一次**。
- 生产代码真的打了一次真服务器并读回了真答案：`BitwardenAuthenticationService.PreloginAsync` 对未知邮箱走 `POST /identity/accounts/prelogin => 200`，拿到 `Algorithm=Pbkdf2Sha256 Iterations=600000`（`MemoryMb`/`Parallelism` 为空）。这是缺口 4 的第一次真实握手。
- 探针在 `D:\Monica\probe-appdata\bwprobe`（一次性、不进仓）：控制台工程只 `ProjectReference` 到 `src/Monica.Platform`，注册用**生产**的 `DeriveMasterKey` / `DeriveMasterPasswordHash` / `StretchMasterKey` / `BitwardenCipherStringCrypto.Encrypt` 拼载荷，之后 登录→`GET /sync`→队列 create→update→软删→"对已进回收站的 cipher 再发 update" 都写好了，跑的就是 §Option A 那条真管道（真 SQLite + 真 `MonicaRepository` + 真 coordinator）。输出侧按属性名把 `*Password*`/`*Key*`/`*Token*`/`*Hash*` 一律 `[redacted]`，随机主密码只写进 `account.json`，从不打印。
- **卡住的位置**：`POST /identity/accounts/register => 422`，服务器 `Data guard Json < RegisterData > failed: ... untagged enum RegisterDataCompat`。逐轮缩小后量到两个真事实：① `keys` 里那个字段的真名是 **`encryptedPrivateKey`**（服务器原话 `Error("missing field \`encryptedPrivateKey\`")`，不是本仓任何文档里的 `encrypted`）；② 换上真名并试过现代 `accountDecryption{masterKey{encryptedKey,macKey},kdf,kdfIterations}` 形状之后，仍是 untagged enum 整体不匹配。
- **下一格实验（最高置信度，先做这个再考虑别的）**：V1 变体大概还要求顶层 `kdf` 与 `kdfIterations`（V2 把它们放在 `accountDecryption` 里）。也就是说：`key` + `keys{publicKey,encryptedPrivateKey}` + `email` + `masterPasswordHash` + `name` + **`kdf:0` + `kdfIterations:600000`** + `collectionGroups:[]` + `passwordHints:[]`，**不要**带 `accountDecryption`。注册一旦 200/204，探针会自己往下跑完登录与同步，那一轮的输出才是缺口 4 的判据。
- 因此本节**没有**、也**不能**被读成"和真服务器验过了"：登录（除 prelogin 外）、2FA、captcha、设备 OTP、`POST /ciphers`、`PUT /ciphers/{id}`、`PUT /ciphers/{id}/delete`、"对已删 cipher 发 update"、sync 带回 `deletedDate` 的假设，**全部仍未验**。`9d9db9d` 那行的"门不让进→进得去、还没进"依然成立。
  ⇒ **本节到这一行为止是历史。** 上一格实验（顶层 `kdf` + `kdfIterations` 的老形状）已被下一节证实，注册通了，上面那句"全部仍未验"里除了 2FA/captcha/设备 OTP 之外都已验完。

## 附：真服务器第二轮实测（2026-09-24，**同步链路验通了，并量出一条新缺陷 #108**）

跑法：`D:\Monica\probe-appdata\bwprobe` 那份一次性控制台探针（不进仓），`ProjectReference` 只指向 `src/Monica.Platform`，注册之后每一步用的都是**生产类型**（`BitwardenAuthenticationService` / `SqliteConnectionFactory` + 真 `MonicaRepository` / `BitwardenAccountStore` / `BitwardenSyncCoordinator` / `BitwardenLocalChangeQueue` / `BitwardenMutationProcessor` / `BitwardenPullMergeService` / `BitwardenMutationHttpTransport`），服务器是本机的 Vaultwarden **1.37.3**（容器 `vw-probe`，`http://127.0.0.1:8080`）。输出只有布尔值、计数和"探针里写死的替身值的字母代号"（A/B/C/D/E 各代一个假值），**没有任何凭据落进日志**：`Dump()` 按属性名把 `*Password*`/`*Key*`/`*Token*`/`*Hash*` 一律 `[redacted]`，随机主密码只进 `account.json`。日志本身在 `D:\Monica\probe-appdata\probe_run*.log`（一次性目录，不进仓）。

- **注册为什么一直 422**：是形状问题，不是签名问题。`RegisterDataCompat` 的两种形状直接从 1.37.3 的 `src/api/core/accounts.rs` 读到，并用容器里那份 46MB 二进制的 rodata 佐证——`accountDecryption`、`collectionGroups`、`passwordHints` 出现次数**都是 0**，也就是这一版服务器根本不认上一轮试的那个现代形状。真相是 `RegisterData` 顶层**没有** `masterPasswordHash`，它和 `key`/KDF 一起在 `#[serde(flatten)] compat` 里：老形状 = `email + kdf + kdfIterations + key + masterPasswordHash`（可带 `keys{publicKey,encryptedPrivateKey}`）；新形状 = `masterPasswordAuthentication{kdf,salt,hash}` + `masterPasswordUnlock{kdf,salt,key}`，且服务器要求两处 `salt` 等于 trim+lowercase 的邮箱、两处 kdf 相等。**实测**：新形状仍然 422（没继续追，注册不是产品范围），**老形状 200**（`{"captchaBypassToken":"","object":"register"}`）。
- **第一次真登录**：`AuthenticateAsync` 对真服务器 `succeeded=True challenge=None factors=<空>` ⇒ 第一轮那句"握手本身还没成功过一次"可以划掉了。**2FA / captcha / 设备 OTP 仍未验**（这台服务器没开任何 2FA）。
- **create**：本地新建一条密码 ⇒ `mutations[Claimed=1/Completed=1]`，紧接着一次全新的 `DownloadAsync` 用**生产解码器**读回：`ciphers=1 title=Monica live probe entry user=probe-user pw=A id=<服务器发的 GUID>` ⇒ `POST /ciphers` 与"服务器回的 cipher id 写回那一行"（#100）为真。
- **update**：`Completed=1`，revision 由 `…44.063493Z` 前进到 `…44.175484Z`，重新拉取解出 `pw=B` ⇒ `PUT /ciphers/{id}` 通，且那道"preflight GET + 比 revision"的闸门没有误伤。
- **软删**：`Completed=1` ⇒ `PUT /ciphers/{id}/delete` 真服务器接受；随后 `merge[Deleted=1]` 把回收站状态落回本地，而**再连做两轮 Manual pull 全零**（`Claimed=0 / Unchanged=1`）⇒ #103 那三条死循环（每轮多一条冲突备份、每轮欠一次删除、整库被误判成 `EmptyRemoteVault`）在真服务器上确认不再出现。
- **远端改 → 本地**：绕过本地仓库、直接用传输层 PUT 一份"别的客户端"的改动，再 `SyncAsync(Manual)` ⇒ `merge[Updated=1]`，本地行的值成为 C 且 `dirty=False`。
- **冲突闭环（#99 + #100 的真服务器版）**：本地脏成 D（未同步）+ 远端被改到 E ⇒ `mutations[Claimed=1/Completed=0/Conflicts=1]`、`merge[Updated=1/ConflictsBackedUp=1]`；冲突列表**恰好 1 行、标题正确**；`RestoreAsync` 之后本地是 D 且 `dirty=True`；下一次 `LocalMutation` 同步 `Completed=1`，**远端读回 D**，且那一条冲突行被清掉（`still_listed=0`；后面某轮里 `still_listed=1` 是 #108 另造出来的第二条，不是这条没清）。这是本仓第一次在真服务器上把"冲突→备份→拿回来→再推上去"整圈走完。
- **一直悬着的那条：对已进回收站的 cipher 再发 update，服务器怎么答**：**2xx 接受，但载荷里的 `deleted:false` 被忽略**——`listed=True isDeleted=True`，只有 revision 前进。替身永远给不出这个答案。
- **⇒ 新缺陷 #108（用户可见，已实测）**：正因为上一条，**"从回收站还原"推不出去**。走产品路径（置 `IsDeleted=false` + `BitwardenLocalModified=true` → `SyncAsync(LocalMutation)`）量到：`local_restore_push mutations[Claimed=1/Completed=1]`（服务器收了这一发）、`remote_after_local_restore listed=True isDeleted=True`（远端还在回收站）、于是同一轮 pull `merge[Deleted=1/ConflictsBackedUp=1]` **把本地再删回去**，并把用户刚恢复的那条备份成冲突。净效果：**用户点"还原"，条目立刻弹回回收站，并在同步页留下一条他没发起的冲突记录**；两轮之后状态稳定（`local_after_second_restore_pull isDeleted=True`、后续 pull 全零），所以它不是死循环，是**功能性错误 + 一条误导性残留**。根因不在接线：`BitwardenMutationOperationType`（`src/Monica.Core/Bitwarden/BitwardenMutationContracts.cs:3-20`）只有 Create/Update/Delete/SoftDelete，**没有 restore 这一档**，漂移扫描只能把它判成 update。
- **修法已经被量到可行性**：`PUT /api/ciphers/{id}/restore` 真服务器 **200**，之后 `remote_after_raw_restore listed=True isDeleted=False`；再走一次常规 pull ⇒ `merge[Updated=1]`，本地行 `isDeleted=False` **复活** ⇒ 拉取侧不需要任何改动（这一条实测就是证据），缺的是"新增 `Restore` 操作类型 + 队列侧'本地活、基线判删 ⇒ restore'那一格分派 + 界面少留那条假冲突"。#108 要的是完整一轮（单测／负控／四道门），别把这行文档读成已修。
- **#107 的预飞（永久删除）**：`Delete` 那条路由真服务器 2xx，回收站内外都删得掉；删干净之后本仓拉取侧**收敛**（连续两轮 `PreservedLocalOnly=1`、零请求、本地行留在原地但 `BitwardenCipherId` 悬空）。剩下的确实只有"清除那一刻的入队生产者 + tombstone 保留 Bitwarden 身份"，规格照旧在 §7。
- **两条新踩的取证坑（值得单列）**：① `dotnet build` 失败之后 `dotnet run --no-build` 会**静静跑上一份二进制**——有一轮日志看起来"跑完了"，其实新加的那几个 stage 根本不在里面。判据：跑之前必须看见 `Build succeeded`，跑之后**在日志里搜新 stage 的名字**。（这是 §2 `40e9ae8` 那条 `--no-build` 坑的第三次。）② `status=409` 不一定是服务器答的：`ExpectedRemoteRevision` 传 null 必然被自己的 preflight 挡下（实测 `note=client_side_gate`）。凡是对端状态码，先问"请求出门了吗"，再记成"服务器行为"。
- **仍未验（缺口 4 剩下的部分，别读成全绿）**：2FA／captcha／设备 OTP／令牌过期后 `RefreshingToken` 那条自动刷新路径／离线把变更堆进队列再回来推的恢复路径／官方 Bitwarden 云（只验过自托管 1.37.3）／笔记与银行卡、证件这三类在真服务器上的 create+update 往返（本轮只跑登录型条目）。
- **机器状态**：容器 `vw-probe` 与 Docker Desktop 仍是本会话起的、此刻还在跑；收法照旧 `docker rm -f vw-probe && docker volume rm vw-probe-data` 后退出 Docker Desktop（卷里只有探针账号，删掉即净）。**本轮没有改任何产品代码**，只有这份文档。


## 附：真服务器第三轮实测（2026-09-25，**#108 已修并验通；顺带抓到一条更严重的串表污染 #109，也修了**）

跑法照旧：`D:\Monica\probe-appdata\bwrestore` 那份一次性控制台探针（不进仓，`AssemblyName=bwprobe`，`ProjectReference` 只指向 `src/Monica.Platform`，注册之后每一步用的都是**生产类型**），服务器是本机 Vaultwarden **1.37.3**（容器 `vw-probe`，端口映射实测是 `127.0.0.1:8080->80/tcp`）。日志 `D:\Monica\probe-appdata\probe_run14.log`（105 行，判据只有 id／revision／布尔／计数／字节数；`grep -c probe-value` = **0**，没有任何载荷落进日志）。

### #108 已经出厂，并在真服务器上按三种口径验通

- 改了什么：`BitwardenMutationOperationType` 多出一档 `Restore`；漂移扫描在"基线是 `deleted:` 标记而本地行活着"那一格入队 `local-restore:{vault}:{id}`（**键必须和 update 分开**，因为 `BitwardenPendingOperationStore.EnqueueAsync` 是 `ON CONFLICT(idempotency_key) DO UPDATE SET status='pending'`，同键会把两件事并成一件）；传输走 `PUT /ciphers/{id}/restore`、**不带体**；processor 加了撤销竞态闸门（用户在这一发出门之前又把条目丢回回收站 ⇒ 不发，直接 `CompleteAsync` 落定）。
- 原始路由电池（stage 12 的 `r*`）：`r2_restore_no_body status=200 bytes=971 revMoved=True`（服务器回**整份 cipher JSON**、`deletedDate=null`、revision 前进）；`r3_restore_on_alive status=200 revMoved=True`（对活条目照样 200，**并且还会再推高一次 revision** ⇒ "重放是幂等的"这个假设不成立，重试计数的语义要按这个读）；`r4_restore_with_body status=200`；`r5_restore_unknown_id status=400` ⇒ **未知 cipher 的 restore 答 400 而不是 404**，所以 #107 那条"404 即终局停放"的分派认不了这种形状，实测记在这儿。
- 产品路径登录型（stage 9）：`local_restore_push mutations[Claimed=1/Completed=1]` → `remote_after_local_restore listed=True isDeleted=False` → `local_after_local_restore isDeleted=False`，**再拉一轮仍 `Claimed=0 / Unchanged=1 / isDeleted=False`** ⇒ 第二轮量到的"点还原立刻弹回回收站"已经没有了。
- 干净的一进一出（stage 13 `x*` 与 stage 14 `s*`，登录型与笔记各一遍）：`x3_local_restore_push` 之后 `merge` 里 `ConflictsBackedUp=0`、`x3_after_restore_remote isDeleted=False revMatchesRemote=True`、`x4_second_sync Claimed=0`；笔记同形状 `x7_note_after_restore remoteIsDeleted=False localIsDeleted=False revMatchesRemote=True`、`x8_note_second_sync Claimed=0` ⇒ **收敛，并且这条路径一条冲突记录都不留**（第二轮那句"在同步页留下一条他没发起的冲突记录"跟着消失了）。
- 还剩一枚观察，不算缺陷但要说清：**只有在"条目已经躺在服务器回收站里时被别处改过"那种形状**下，还原那一轮仍会多出一条冲突备份（stage 9 正是这种，前面用裸传输 PUT 过一条 `update_on_trashed_cipher`，实测 `merge[Updated=1/ConflictsBackedUp=1]`）。这是合并引擎既有规则（同 revision 内容不同 ⇒ 先备份本地再让远端赢）的正常产物，可用户看见"冲突"两个字仍然会费解 —— 要不要换一句更准的措辞属于界面范围，本轮没动。

### ⇒ 本轮真正的新东西：#109，pull 落库按 id 解析会**串表**（不是笔记专属）

- 根因：`BitwardenPullMergeService` 的三处落库（`ApplyRemoteDeletionAsync`／`MarkLocalCleanAsync`／`SaveConflictBackupAsync`）拿 `decision.LocalId` 去"先查密码表、查不到再查安全条目表"，而 `passwords.id` 与 `secure_items.id` 是**两条各自独立的自增序列** —— 库里第一条登录和第一条笔记都是 `#1`（本轮实测打印：`pw=[#3,#2,#1]`、`si=[#2,#1]`）。
- run13 量到的后果：**属于笔记 cipher 的远端删除落到同号的那条在用登录上**（它因此在几轮同步里 `live→del→live→del` 反复），而笔记自己的行 revision 永远不前进（`localRev=…01.0430` vs `remoteRev/baselineRev=…01.2594`）。**这才是"笔记的还原推不出去、永远停在 Conflict"的真因** —— #108 只是它外面的一层皮，只修 #108 治不到笔记。
- 修法：三处一律按 **cipher id** 定位行（cipher id 在两张表之间唯一，`BitwardenMergeEngine.BuildUniqueLocalMap` 早就把它当唯一身份在用）；冲突备份那行记的是**命中那一行的 Id**（还原侧按 `itemKind` + id 解析，本来就是对的）。
- 负控与守卫：新测试 `BitwardenPullMergeServiceTests.A_remote_note_deletion_cannot_touch_the_login_sharing_its_row_id` —— 先造出同号的登录＋笔记（断言 `localLogin.Id == localNote.Id` 把前提钉住），再推一条"笔记被远端删除"的快照。**去掉修复即红**（`Assert.False(password.IsDeleted)` 在该文件 line 135 失败：登录被投进了回收站），**带修复绿**；整串 `Category!=perf-budget` **903 条全绿**（加这条之前是 902）。
- 真服务器复验（stage 14，笔记全程走产品路径）：笔记行 `si=#2` 的 revision 逐轮 `29.0973 live → 29.2755 del → 29.4960 live`，**每一轮都与 `remoteRev` 相等**；同时三条登录 `pw=[#3,#2,#1]` 从头到尾都是 `live`、没被碰过；队列侧 `rows=[SoftDelete/Completed | Restore/Completed]`、基线从 `marker` 回到 `content`、`s14_scan_after_restore_push enqueued=0`、下一轮整场 `Claimed=0` ⇒ **#108+#109 合起来把笔记的回收站还原走通了**。
- 没动的那一格（诚实记下）：`ApplyActiveRemoteAsync` 仍然按远端 `cipherType` 决定落哪张表、并复用 `decision.LocalId`。只有"同一个 cipher 在登录型与安全条目型之间翻转类型"这种服务器行为才会撞上它，**本轮没有实测过那种形状**，所以没顺手改 —— 改法是同样按 cipher id 在目标表里解析，但会引出"两行同时绑一个 cipher id ⇒ 下一轮 `BuildUniqueLocalMap` 抛错、整库同步被拒"的新问题，需要单独一轮把"类型翻转"当一等公民设计（入 #94 的未验清单）。

### 这一轮新踩的取证坑（三条）

- `dotnet format --verify-no-changes` 会因为**编辑工具往 CRLF 文件里写了裸 LF 行**而报一片 WHITESPACE 红，**连内容与 HEAD 逐字节相同的那些行也一起报**（本轮 27 条里有这种）。判据：`grep -c $'\r$'` 对 `grep -c -v $'\r$'`，本轮修完的两个文件是 254／0。
- UI 套件那份 dll **不带 `-filter` 直接跑**：exit 0、日志里只剩一行 runner 头、**0 条被计数** —— 那是"没跑"，不能读成"跑绿"。正解是照 `eng/ci/verify-commercial-release.ps1:253-274` 那样带 `-filter '/[Category!=perf-budget]'` + `-trx` 再用 `Assert-TestReportRanTests` 验总数。
- `BitwardenLocalChangeQueue.EnqueueDriftedAsync` 的 **`Refused` 计数在 `BitwardenSyncResult` 里根本不出现**，界面与日志都看不见。本轮有一条笔记因为载荷建错（`Notes` 留空、只填 `ItemData`）从头到尾没入过队、也没上过服务器，唯一看得见的线索是探针打印的 `localRev=(none)`；~~**为什么被拒这一格至今未定位**（是编码器拒绝还是别的判断，无从分辨）~~ **已修（`d1fdf59`，#112）**：拒绝现在由编码器点名（`BitwardenPayloadRefusal`）、由队列带出、经 `BitwardenSyncResult.Unsyncable` 同时到达同步页与诊断日志，真服务器读数见文末第七轮实测；**那条笔记具体是哪一格本轮没有回验**，不替它改判。这是 #94 的可诊断性缺口（已闭），不是那条笔记的根因结论。
- 还有一条本轮自己造的假绿：后台跑门禁时写成 `powershell … | tail -60`，**管道会把真 exit code 吞成 `tail` 的 0**，而日志文件是 0 字节、`TestResults/**` 的时间戳一动没动 —— 看着"完成了"，其实一步没跑。判据改成：重定向到文件，跑完**看 trx 的时间戳与总数**，别信 `$?`（这是 §2 那条 `--no-build` 假绿的同族）。

**机器状态**：Docker Desktop 与容器 `vw-probe` 都是本会话起的，**此刻仍在跑**（`GET http://127.0.0.1:8080/alive => 200`）；收法照旧 `docker rm -f vw-probe && docker volume rm vw-probe-data` 后退出 Docker Desktop（卷里只有探针账号）。探针与日志都在 `D:\Monica\probe-appdata\`（一次性目录，不进仓）。**本轮改了产品代码**：`Restore` 那一档（#108）与 pull 落库按 cipher id 解析（#109）。


## 附：真服务器第四轮实测（2026-09-25，**#107 出厂，并且它那条"404 即完成"被判据错了当场抓到**）

跑法照旧：`D:\Monica\probe-appdata\bwrestore` 那份一次性控制台探针（不进仓，`AssemblyName=bwprobe`，
`ProjectReference` 只指向 `src/Monica.Platform`，注册之后每一步用的都是**生产类型**），服务器是本机
Vaultwarden **1.37.3**（容器 `vw-probe`，`127.0.0.1:8080->80/tcp`）。日志 `D:\Monica\probe-appdata\probe_run15.log`
（122 行，判据只有 id／revision／状态码／布尔／计数；`grep -c probe-value` = **0**，没有任何载荷落进日志）。
新增的是 stage 15（`s15*`）：**别处把这条 cipher 彻底抹掉之后，本仓那一次欠着的 erase 会怎样**。

### 一、把本轮的产品假设拿到真机上撞了一遍，三条成立

- **tombstone 全盲**：`DeletePasswordPermanentlyAsync` 之后 `stage=s15_tombstone exists=False vaultId=(none) cipherId=(cleared)`，
  紧接着常规漂移扫描 `stage=s15_scan_after_purge enqueued=0 refused=0` ⇒ §7 那句"不能指望漂移扫描"在真服务器上成立，
  而同一个清除用本轮的生产者入队是 `stage=s15_producer queued=True`。
- **基线自愈**：erase 落地之后再拉一轮 `stage=s15_after_second_sync baseline_present=False local_row=False remote_listed=(gone)`
  ⇒ 没加 `RemoveAsync` 也确实不留幻影行（④ 那格按"下一次整表重写带走"设计）。
- **不循环**：第二轮 `stage=s15_second_sync mutations[Claimed=0…]`。

### 二、⇒ 抓到并当场修掉一条：真服务器对"手里没有的 cipher"答 **400，不是 404**

- 控制组先立住：同一条探针、同一种 URL 形状，**活着的** cipher `GET` 回 `stage=s15_control_alive_get status=200`；
  一个**从未存在过**的 id 也回 `status=400`（所以 400 不是探针把 URL 打错，也不是"路径不支持"）。
- 彻底抹掉之后（先裸 `DELETE` 拿到 `stage=s15_remote_purge status=200`，即第三轮那句"DELETE 真服务器 2xx"复验）：
  `s15_preflight_get status=400`、`s15_soft_delete_again status=400`、`s15_delete_again status=400`，
  而**产品自己的传输层**在同一意图上给回 `s15_product_delete status=400 succeeded=False`。
- 于是第一轮跑的 erase 是 `s15_purge_sync mutations[Claimed=1/Completed=0/Failed=1]` +
  `s15_queue_rows=[Delete/Failed/failure=Validation/att=1]` —— 按 404 写的结算分支在这台服务器上
  **一次都没触发**，那条"远端已经没有、请求已被完全答复"的 erase 变成一条永远失败的队列行。
  这与第三轮 `r5_restore_unknown_id status=400` 是**同一族事实**：Vaultwarden 把"实体找不到"统一报 400。
- 修法（`201501e`）：判据放宽为"删除类操作读到 400 **或** 404 ⇒ 已抹掉即完成"，并且**只限 `Delete`/`SoftDelete`**——
  这两种删除的请求只有路由 + id、没有体，400 不可能是载荷写错；`Update` 吃 400 仍然是失败（那是我们自己把
  载荷建坏了）。新测试 `An_erase_answered_400_settles_while_an_update_answered_400_still_fails` 在**同一个批次**里
  两头钉：`transport.Sends=2`、`Completed=1`（erase 结算）、`Failed=1` 且 `Update/Failed/Validation`。
  负控：把判据退回只认 404 ⇒ 该测试即刻红。
- 改后同一 stage 真服务器复跑：`s15_purge_sync mutations[Claimed=1/Completed=1/Deferred=0/Conflicts=0/Failed=0]` +
  `s15_queue_rows=[Delete/Completed/failure=None/att=1/err=(none)]`，第二轮仍 `Claimed=0`。

### 三、门禁与诚实边界

- 门禁：格式 0 改动、`dotnet build --warnaserror` 0 warning、commercial-release 全绿
  （单测 **10 `perf-budget` + 909 常规**、UI **17 `perf-budget` + 239 常规**）。本轮代码分两个提交：
  `48d6b21`（生产者 + 接线 + 两处处理器判断 + 5 条测试）、`201501e`（400 结算 + 1 条测试）。
- **产物级真跑门在"新接线的那份二进制"上重跑过一遍**（`App.axaml.cs` 多了一条 DI 注册、`MainWindowViewModel`
  多了一个可选构造参数，不重 publish 就等于没验）：`publish=0` ⇒ `artifacts/publish/win-x64/jit`，
  `runtime=0` ⇒ `CANONICAL VAULT passed`（native=mdbx_ffi.dll，canonicalVaultFiles=1）、
  库读取 passwords=27/notes=14/categories=6/attachmentOwners=6、UI 门 loadMs=521/4000（发布判据那次 139）、
  KeePass 20000 条 3.23MB：openMs=817 / streamMs=229 / 明细 20000 全读、峰值 189.9MB、
  强制回收后 collectedMB=109.9（相对基线 115.3 为 growthMB=-5.4，预算 24）、
  锁定 10 轮尾窗中位数 **107.9MB / 预算 120**、锁/解往返 25/14/1/4 全等，`UI SMOKE passed` →
  `RUNTIME SMOKE passed`（日志 `D:\Monica\probe-appdata\pub107.log`、`run107.log`，只取退出码与这些读数）。
- **仍然没验**（别读成全绿）：① App 层那三处调用点**当时**无自动化证明（UI harness 只 fake `IsUnlocked`，队列行要真实
  `bitwarden_vaults` 行才过外键），本轮的"接线正确"只有 Data 层证据 + 真服务器上手工走的那条 `Purge.EnqueuePasswordAsync`
  ——**后半已由 `6bbf449` 的生产容器断言收掉**（注册在位、类型正确，并且拿"删掉注册行"反证过），行为级仍缺，见 §7 缺口 4 ①；
  ② ~~**硬删推送失败而 pull 成功**时 `AddRemote` 会把条目在本地重新长回来（未做、未测，设计草图在 §7 第 4 条末尾）~~
  **已修（`12ed5a9`），并且在真服务器上跑出过相反判决**，见文末第五轮实测；
  ③ 官方 Bitwarden 云、2FA／captcha／设备 OTP、`RefreshingToken` 自动刷新、离线堆队再回来推、卡片与证件的真服务器
  create+update 往返，全部照旧未验（#94 缺口 4）。
- **同一条 stage 在两版二进制上跑出过两个相反的判决**，这本身就是"改判被证到"的方式：`probe-appdata/s15b.log`
  （`48d6b21` 那份，只认 404）= `Failed=1` + `Delete/Failed`，`probe_run15.log`（`201501e` 那份）=
  `Completed=1` + `Delete/Completed`。中间那次 `dotnet build` 红过一轮（`row` 与外层同名 CS0136），
  而**每一次跑之前都看见过 `0 Error(s)`**，所以这不是 §5 记过三次的"`--no-build` 悄悄跑陈旧二进制"那族坑；
  留这条是留**对照**，不是新坑。

## 附：真服务器第五轮实测（2026-09-25，**#110 出厂：pull 不再把"还欠一次 erase"的条目养回来**）

跑法照旧：`D:\Monica\probe-appdata\bwrestore` 那份一次性控制台探针（不进仓，注册之后每一步用的都是
**生产类型**），服务器仍是本机 Vaultwarden **1.37.3**（容器 `vw-probe`，`127.0.0.1:8080->80/tcp`，healthy）。
新增的是 stage 16（`s16*`），它问的正是 §7 缺口 4 ② 那一岔：**别处把这条 cipher 改了，我们此刻把本地行彻底清除**
——erase 的 preflight 带着手里那份 revision，服务器上的已经前进 ⇒ 这次 erase 注定被判冲突，而这恰好是
pull 会看见"远端有一条本地没有的 cipher"的时刻。三份日志：`probe_run16b.log`（修好的那份，**exit 0**，132 行）、
`probe_run16.log`（同一份代码，退出码 **127**：最后一句打印崩在探针自己的 `Short()`——`RemoteStateOf` 对已消失的
cipher 回的是字面量 `"(gone)"`，被 `[17..24]` 切片绊倒；**产品读数到前一行都完好**，补判空后重跑即 16b）、
`s16pre.log`（**负控**：把合并服务里那条抑制分支摘掉、重新构建再跑的同一 stage）。判据只有 id／revision／
状态码／布尔／计数，三份日志 `grep -c probe-value` 全是 **0**（没有载荷落进日志）。

### 一、修好的那份：债在，条目就压得住；债清，闸门即刻让路

- `s16_create mutations[Claimed=1/Completed=1/…] merge[…SuppressedResurrections=0]` ⇒ 探针新建并推上去，
  `s16_located id=057901cc`。
- `s16_other_client_put succeeded=True` ⇒ **另一个客户端**把这条改了（远端 revision 前进）。
- `s16_purge booked=True carried_rev=02.2057` ⇒ 本地彻底清除 + 生产者记账成功，带上的是清除**前**那份 revision。
- `s16_sync_with_owed_erase mutations[Claimed=1/Completed=0/Conflicts=1/Failed=0] merge[Added=0/…/PreservedLocalOnly=1/Unchanged=4/SuppressedResurrections=1]`
  ⇒ 真服务器上这次 erase **确实**落成 `Conflict`（设计草图成立），而同一个 pull **没有**把条目长回来。
- `s16_owed_state rows=[Delete/Conflict/failure=Conflict] bound_local_rows=0 remote_rev=02.3268`
  ⇒ 本地零行、服务器仍持有它、账还欠着。
- 探针按服务器**当前** revision 用同一幂等键重新记账（`s16_rebooked granted=True at=02.3268`）⇒
  `s16_sync_after_grant mutations[Claimed=1/Completed=1/Conflicts=0/Failed=0] merge[Added=0/…/SuppressedResurrections=0]`、
  第三轮 `s16_sync_third_round mutations[Claimed=0/…]`（不循环）、
  `s16_after_grant rows=[Delete/Completed/failure=None] bound_local_rows=0 baseline_present=False remote_rev=(gone)`
  ⇒ 债一清，抑制即刻退场，基线按第四轮那条"自愈"说法被下一轮整表重写带走。

### 二、负控那份：同一 stage 在**没有抑制**的二进制上给出相反判决

`s16_sync_with_owed_erase … merge[Added=1/…/SuppressedResurrections=0]` + `s16_owed_state bound_local_rows=1`
——条目被当成"别处新建的东西"写回了本地。更难看的是**债还上之后**：`s16_sync_after_grant` 的 erase 是
`Claimed=1/Completed=1`，可那一条多长出来的行**还在**（`s16_after_grant rows=[Delete/Completed/failure=None]` +
`bound_local_rows=1` + `baseline_present=False` + `remote_rev=(gone)`，`PreservedLocalOnly` 从 1 涨到 2）
⇒ 留下的是一条**绑在"服务器已经不持有的 cipher"上的永久僵尸行**：它会带着 `BitwardenCipherId` 继续出现在列表里、
继续被漂移扫描当成同步条目，而远端根本没有对应物可以收敛。修回后 `sha256sum -c`（`probe-appdata/s16.sha`）对
`BitwardenPullMergeService.cs` = OK，重建后 136 条 `~Bitwarden` 单测全绿。

### 三、门禁与剩下的口子

- 门禁：格式 0 改动、Release 0 warning。`12ed5a9` 那份字节上 commercial-release `=0`（单测 10 `perf-budget` + 911 常规、
  UI 17 + 239）；**HEAD（含 `6bbf449` 那条容器断言）整串重跑仍 `cr_rc=0`**：单测 **10 `perf-budget` + 911 常规**、
  UI **17 `perf-budget` + 240 常规**（四份 trx 逐一读 `recorded N executed tests`，不看汇总行；日志 `cr110.log`）。
  另外单独复跑 `--filter FullyQualifiedName~Bitwarden` ⇒ **136/136 绿**（负控还原后也用它守过）。
  产物级真跑门用的是 **#110 之后重新 publish 的那份二进制**（publish 的是 HEAD `6bbf449`，那条只动测试，
  所以产品二进制与 `12ed5a9` 同源）：`pub_rc=0` →
  `artifacts/publish/win-x64/jit`，`rt_rc=0` → `CANONICAL VAULT passed`（native=mdbx_ffi.dll、canonicalVaultFiles=1、
  回读 passwords=27/notes=14/categories=6/attachmentOwners=6）、UI 门 loadMs=263/4000、KeePass 20000 条 3.23MB：
  openMs=2058 / streamMs=493 / collectedMB=105.2 → growthMB=**5.1**/24、锁定 10 轮尾窗中位 **99.2MB / 120**
  （区间 98.9–100.6）、锁/解往返 25/14/1/4 全等，`UI SMOKE passed` → `RUNTIME SMOKE passed`
  （日志 `D:\Monica\probe-appdata\pub110.log`、`run110.log`，只取退出码与这些读数）。
- **仍然没验**（别读成全绿）：① App 层"真清除命令写出一条队列行"的**行为**证明（`6bbf449` 只钉住注册与类型，
  三条墙写在 §7 缺口 4 ①）；② 一条 `Conflict`／`Failed` 的 erase **谁也不会替用户重下决定**——队列不重试、
  本地压住、服务器留残迹，缺的是界面上那条可见的冲突决定（#99 那一族），不是把 revision 自动改判 ⇒ **#111 已把这条决定交给用户（`39bb145`，读数见文末第六轮实测）**；
  ③ 官方 Bitwarden 云、2FA／captcha／设备 OTP、`RefreshingToken` 自动刷新、离线堆队再回来推、卡片与证件的
  真服务器 create+update 往返、`IsArchived` 不进漂移指纹、~~`Refused` 不出现在界面与诊断里~~ **已修（`d1fdf59`，#112：`Refused` 现在带着原因码同时出现在同步页与诊断日志，读数见文末第七轮实测）**（#94 缺口 4 其余各项原样）。

## 附：真服务器第六轮实测（2026-09-25，**#111 出厂：那条推不出去的删除第一次有人能改主意**）

跑法照旧：`D:\Monica\probe-appdata\bwrestore` 那份一次性控制台探针（不进仓，注册之后每一步用的都是
**生产类型**），服务器仍是本机 Vaultwarden **1.37.3**（容器 `vw-probe`，`127.0.0.1:8080->80/tcp`，healthy；
本轮结束**没有** teardown——用户要求本地的 keepass／mdbx／bitwarden 这些数据一律不动）。新增的是 stage 17
（`s17*`），它问的正是第五轮那份读数的下一步：同一条 `Delete/Conflict` 的 erase，这次由**产品自己那个
`BitwardenStuckEraseService`**（不是替身、也不是探针手写 SQL）列出来、放弃掉，再看下一轮 pull 把服务器
那一份交回成什么样子。日志 `probe-appdata/bwrestore/s17-run.log`（**exit 0**，stage 1…17 整串一次跑完）。
判据只有 id／revision／状态／内容类别／计数，`grep -c probe-value` = **0**（没有载荷落进日志）。

### 一、四条读数：债列得出来、放得掉、放掉之后拿回来的是"干净且服务器确认过"的那一份

- `s17_listed rows=1 detail=[57fc029f/Conflict] bound_local_rows=0 remote_rev=46.2013`
  ⇒ 与第五轮 `s16_owed_state` 同一形状（真服务器确实把它判成 `Conflict` 并且永不再试），区别只有一个：
  这一条是**界面用的那个方法**列出来的，探针没有另写查询。
- `s17_after_abandon rows=[Delete/Completed] still_listed=0`
  ⇒ 放弃 = 队列行落成 `Completed`，同一份列表即刻不再包含它——#110 那条抑制判据读的正是"状态不是
  `Completed`"，所以这里不需要任何新的让路逻辑。
- `s17_sync_after_abandon mutations[Claimed=0/Completed=0/Deferred=0/Conflicts=0/Failed=0]
  merge[Added=1/Updated=0/Deleted=0/ConflictsBackedUp=0/MarkedClean=0/PreservedLocalOnly=1/Unchanged=4/SuppressedResurrections=0]`
  + `s17_restored rows=1 title=Monica live probe s17 edited elsewhere pw=C modified=False rev=46.2013
  remote_rev=46.2013`
  ⇒ 条目走的是**普通 add 路径**回来（没为它开后门），带回来的是**另一个客户端那份内容**（`pw=C` 而不是
  本机那份 `A`）、`modified=False`（本地没有待推的改动）、本地 revision **等于**服务器 revision。
- `s17_rebooked booked=True rev=46.2013` ⇒ `s17_sync_second_erase mutations[Claimed=1/Completed=1/Deferred=0/Conflicts=0/Failed=0]`
  + `s17_second_erase rows=[Delete/Completed/failure=None] stuck=0 bound_local_rows=0 remote_rev=(gone)`
  ⇒ 同一个用户决定重下一次**这一次落地了**。这一条才是本轮真正的收获：放弃不是"把东西叫回来就算了"，
  而是把那条债**从死账变回有基线、可谈判的账**——放回的那份带着服务器确认过的 revision，所以再删一次是
  一次普通 erase，而不是又一条谁也不会再问的账。

### 二、对照：同一份日志里 stage 16 仍是"没有出口"的样子

`s16_owed_state rows=[Delete/Conflict/failure=Conflict] bound_local_rows=0 remote_rev=45.3909`，而探针不按
当前 revision 重新记账时 `s16_sync_third_round mutations[Claimed=0/Completed=0/...]` ——债永远不动。
**这不是负控**：真服务器这一轮没有拆件重跑，#111 的四批负控全在单元与 UI 级（逐条红因写在 §2 那行）。

### 三、门禁与剩下的口子

- 门禁全部在**出货的那份字节**（`39bb145`）上跑：格式 `--verify-no-changes` 退出 0、Release **0 warning**；
  `verify-commercial-release.ps1` ⇒ `cr_rc=0`，四份 trx 逐一读 `recorded N executed tests`：单测
  **10 `perf-budget` + 914 常规**、UI **17 `perf-budget` + 242 常规**（日志 `probe-appdata/gate111.log`）。
  产物级：`publish-desktop.ps1` ⇒ `pub_rc=0` → `artifacts/publish/win-x64/jit`，
  `verify-artifact-runtime.ps1` ⇒ `rt_rc=0`：`CANONICAL VAULT passed`（native=`mdbx_ffi.dll`、
  canonicalVaultFiles=1、回读 passwords=27/notes=14/categories=6/attachmentOwners=6）、UI 门
  loadMs=131、KeePass 20000 条 3.22MB：openMs=781 / streamMs=205 / collectedMB=109.1 → growthMB=**3.2**/24、
  锁定态 106.7MB/120、锁/解往返 25/14/1/4 全等 → `RUNTIME SMOKE passed`
  （日志 `probe-appdata/pub111.log`、`run111.log`，只取退出码与这些读数）。
- **仍然没验**（别读成全绿）：① 上一轮那条"App 层真清除命令写出一条队列行"的**行为**证明照旧缺
  （`6bbf449` 只钉住注册与类型，三道墙写在 §7 缺口 4 ①）——本轮的 stage 17 用的是 Data 层服务，
  不是从按钮走下去的；② **这一段界面在真机上没人看过**：#111 的界面证据是无头 UI 树的命令解析与
  行数变化，没有截图口味门（该节要 `HasBitwardenStuckErasures` 为真才可见，而它只在有一条卡住的 erase
  时出现）；③ 行上只有 cipher id：v1 不打算补标题，因为那要在清除时另存一份名字，而 §2 那行记的理由是
  "队列行是那个决定之最后记录"，多存一份名字等于再开一条数据寿命；④ 官方 Bitwarden 云、2FA／captcha／
  设备 OTP、`RefreshingToken` 自动刷新、离线堆队再回来推、卡片与证件的真服务器 create+update 往返、
  `IsArchived` 不进漂移指纹、~~`Refused` 不出现在界面与诊断里~~ **已修（`d1fdf59`，#112：`Refused` 现在带着原因码同时出现在同步页与诊断日志，读数见文末第七轮实测）**（#94 缺口 4 其余各项原样）。


## 附：真服务器第七轮实测（2026-09-25，**#112 出厂：拒绝第一次带着原因走到界面，并且它当场推翻了自己的一条前提**）

跑法照旧：`D:\Monica\probe-appdata\bwrestore` 那份一次性控制台探针（不进仓，注册之后每一步用的都是
**生产类型**：真 SQLite + 真 `MonicaRepository` + 真 `BitwardenLocalChangeQueue` + 真协调器 + 真合并引擎），
服务器仍是本机 Vaultwarden **1.37.3**（容器 `vw-probe`，`127.0.0.1:8080->80/tcp`，healthy；本轮结束**没有**
teardown——用户要求本地的 keepass／mdbx／bitwarden 这些数据一律不动）。本轮新增 stage 18 与 18b（`s18*`）：
前者拿一条 **Bitwarden 存不下的形状**（`UnsupportedShape`）走完整条路，后者拿另一条独立条目专门走
`MissingRemoteRevision` 那一档——两条各问一件事，不是一条测两遍。主日志 `probe-appdata/bwrestore/s19d-run.log`
（**exit 0**，stage 1…18b 整串一次跑完，184 行），另有两份对照：`s19b-run.log` 跑在 #113 **之前**的那份二进制、
`s19c-run.log` 跑在之后。判据只有 id／revision／状态／布尔／计数与代码样的原因串，`grep -c probe-value` =
**0**（没有任何载荷落进日志；条目标题只以 `named=` 布尔参与判断，从不打印）。

### 一、#112 的读数：清单在同步之前就在，但只在"盖回它的那一轮"挂着

- `s18_pre_sync_scan enqueued=0 refused=1 listed=1 count_matches_list=True` +
  `rows=1 named=True is_password=True codes=[UnsupportedShape]`，连跑两次（`s18_pre_sync_scan_again`）读数不变
  ⇒ **不调用协调器**、直接问队列，就已经知道"哪一条推不出去、为什么"；`Refused == Unsyncable.Count` 不是
  推断而是每轮量出来的（`count_matches_list=True`），所以界面不可能少列队列拒了的东西。
- `s18_pre_sync_owed=0 row_has_edit=True` ⇒ 这条改动**没有欠任何一次推送**：队列拒了它、没记账，而本机那一行
  确实带着用户刚打的改动。这正是旧形状里"屏幕上写已同步"的那一格。
- `s18_sync_shape merge[Updated=1/ConflictsBackedUp=1]` + `s18_row_after_refusing_pull has_edit=False
  modified=False` + `s18_conflict_after_pull count=2 named=1` ⇒ 同一轮的 pull 用服务器那份盖回这一行，改动
  **只以一条冲突备份存在**（列表里那条就是它），而不是留在条目上。
- `s18_sync_shape_again merge[...ConflictsBackedUp=0...]` + `rows=0 named=False codes=[]` ⇒ **下一轮同一形状
  静默**：本地已经没有"这台设备改了"这件事，警告自然退场。
- `s18_scan_after_restore refused=1`（从冲突列表取回备份之后）+ `s18_sync_after_restore
  merge[Updated=1/ConflictsBackedUp=1]` ⇒ 取回而不改形态，取回来的正是刚被拒收的那个形状，再拒一次。
- `s18_scan_after_fix enqueued=1 refused=0 listed=0` ⇒ `s18_sync_shape_fixed
  mutations[Claimed=1/Completed=1]` ⇒ `s18_edit_travelled=True conflicts_left=0` ⇒ 唯一出路是**改形态**；
  改完不仅推得出去，前几轮攒下的冲突备份也归零。

### 二、被推翻的那条前提（记下来因为它是本轮最贵的一条）

动手时写下的说法是"一条被拒的改动从此**每一轮**都被拒，界面因此永远挂着这条警告"。实测不成立：警告只挂在
**盖回它的那一轮**，下一轮 `rows=0`。真相比原说法更糟而不是更好——改动不是"一直推不出去"，而是**一次都没留下
痕迹就被覆盖**，除非队列或合并引擎替它留一份备份。所以本轮做了两件事：① 中英两句分区说明按实测重写
（`BitwardenUnsyncableSectionDescription`），指向"改成 Bitwarden 能存的形态"而不是"再同步一次"，并明说从冲突
列表取回它等于取回刚被拒的那个形状；② 顺着"下一轮凭什么静默"往下问，才撞出下面 #113 那一档。

### 三、⇒ #113：同一 stage 在两版二进制上跑出相反判决

- **修复前**（`s19b-run.log`）：`s18b_sync_no_revision merge[Added=0/Updated=1/Deleted=0/ConflictsBackedUp=0/
  ...]`、`s18b_conflict_after_pull count=2 named=0`、`s18b_any_conflict_for_cipher=0` ⇒ 一条 `MissingRemoteRevision`
  被拒的改动被 pull 判成普通更新盖掉，**按 cipher id 查不出任何冲突备份**——一声不响地没了。
- **修复后**（`s19c-run.log`、`s19d-run.log` 两份独立注册各跑一遍，读数同形）：`ConflictsBackedUp=1`、
  `named=1`、`any_conflict_for_cipher=1`，且备份里那条的原因点名是新判据那句
  `The local row has no remote revision to compare against; keep its content recoverable, then apply remote.`，
  与既有那句 `Local state differs at the remote revision; ...` 并存于同一份列表 ⇒ 两条不同的判据各有各的记录，
  没有互相顶替。
- **单元级负控真打**（本轮在出货字节之外重跑过一次）：摘掉 `BitwardenMergeEngine` 那条分支、重建、跑那 5 条
  ⇒ 恰有 2 条红，红因分别是 `Expected: CreateConflictBackupThenApplyRemote / Actual: ApplyRemoteUpdate`（Core
  的判据本体）与 `Expected: 1 / Actual: 0`（Data 侧真 SQLite + 真 pull 的 `ConflictsBackedUp` 计数）；两条
  "不该备份"的照旧绿。还原后 `sha1sum -c` 读回 `OK`（`7e7547aea2af04ea5cfd2cbc426847438da4eb67`），重建后
  5/5 绿。

### 四、门禁与剩下的口子（别读成账清了）

- 门禁全部在**出货的那份字节**（`d1fdf59`）上跑：`dotnet format --verify-no-changes` 退出 **0**（0 改动）；
  `verify-commercial-release.ps1 -Configuration Release` ⇒ `cr_rc=0`，末行 `Commercial release verification
  passed.`，四份 trx 逐一读总数（日志 `probe-appdata/gate112.log`）：单测 **10 `perf-budget` + 922 常规**、
  UI **17 `perf-budget` + 244 常规**；同一串里的 `dotnet build -c Release --warnaserror` 因此也是 **0 warning**。
  产物级：`publish-desktop.ps1` ⇒ `pub_rc=0`，`verify-artifact-runtime.ps1` ⇒ `art_rc=0`：
  `UI SMOKE passed` + `RUNTIME SMOKE passed`，loadMs=**148**/4000（库加载 actualMs=634/4000）、KeePass 20000 条
  3.23MB：openMs=1191 / streamMs=305 / collectedMB=109.5 → growthMB=**-3.5**/24（开库释放后比基线还低，
  这是既有那条形同"-增长"的读数，不是新事实）、锁定态 10 拍轨迹 115.1/117.3/109.7/110.5/111.8/106.1/106.2/
  110.0/106.0/107.9，尾 5 拍 **中位 106.2MB／区间 106.0–110.0**（预算 120）、锁/解往返 passwords=25/25、
  notes=14/14、totp=1/1、wallet=4/4，瞬态回执 `retired=True`。**时序要写清**：产物门跑在 16:18 那次 publish 上，
  而上面那次负控只是把同一份源文件取走又放回（sha1 逐字节一致，见 §三），放回的时间在 `cr_rc` 落盘之后
  （gate 日志末尾时间戳 16:28:28，摘分支在 16:29），所以三份读数测的都是出货那份字节。
- **仍然没验**：① **这一段界面在真机上没人看过**——和 #111 一样，证据是无头 UI 树里段落控件在位、命令解析、
  以及"只在债站着时可见"，没有截图口味门（它只在真有一条被拒改动时出现）；② `MissingRemoteRevision` 的**来源**
  没回验（身份取自服务器而 revision 缺失是从哪一步开始的），v1 继续拒绝推送而不是补读一次远端；③ 上一轮那条
  "载荷建错的笔记到底是被哪一格拒的"没有回验，本轮只是把"拒了会说出来"这件事做到位；④ App 层"真清除命令写出
  一条队列行"的**行为**证明照旧缺（`6bbf449` 只钉注册与类型）；⑤ 官方 Bitwarden 云、2FA／captcha／设备 OTP、
  `RefreshingToken` 自动刷新、离线堆队再回来推、卡片与证件的真服务器 create+update 往返、`IsArchived` 不进漂移
  指纹（#94 缺口 4 其余各项原样）。

**机器状态**：容器 `vw-probe` 仍在跑（本轮探针又注册了一个新账号，卷里只有探针数据），`D:\Monica\probe-appdata\`
按用户要求一字不动；收法照旧 `docker rm -f vw-probe && docker volume rm vw-probe-data`，且要等用户点头。

## 附：真服务器第八轮实测（2026-09-25，**#114 文件夹移动第一次推得出去——然后它自己的第一版规则在同一轮被推翻**）

跑法照旧：`D:\Monica\probe-appdata\bwrestore` 那份一次性控制台探针（不进仓，注册之后的每一步用的都是
**生产类型**：真 SQLite + 真 `MonicaRepository` + 真 `BitwardenLocalChangeQueue` + 真协调器 + 真合并引擎），
服务器仍是本机 Vaultwarden **1.37.3**（容器 `vw-probe`，`127.0.0.1:8080->80/tcp`，healthy；本轮结束**没有**
teardown——用户要求本地的 keepass／mdbx／bitwarden 这些数据一律不动）。本轮新增 stage 20（`s20*`）：先用
`POST /folders` **在真服务器上建两个夹**（名字必须是加密串——解码器 `DecryptRequired(folder.Name, ...)` 会拒
明文），拉一次让绑定建立，再拿一条新建条目走"夹 A → 夹 B → 只有本机的夹 → 根"四步，每步量：队列欠不欠、
推完服务器上这条落在哪个夹、本机那一行的 category／folder 列／revision、冲突备份数、以及**推完再扫一次还欠
不欠**。主日志 `probe-appdata/bwrestore/s20-run.log`（**exit 0**，201 行），判据只有 id／revision／状态／布尔／
计数：全量 `grep -ciE` 三条已知 fixture 字面量 = **0**、44 字符以上的 base64 形态串 = **0**（条目标题只以
`found=`／`named=` 参与判断，从不打印）。

### 一、#114 要修的那件事，和它确实修好的部分

- 旧形状：库页移动条目只改本地 `CategoryId`，而漂移指纹读的是 `bitwarden_folder_id` 列 ⇒ 判断与载荷读同一个
  来源，**移动从来没有成为一次上传**。红是先量出来的：新测试 `Expected: 1 / Actual: 0`。
- 修法：一份共享投影 `src/Monica.Data/Bitwarden/BitwardenLocalFolderProjection.cs`，让**判漂移的和建载荷的走
  同一个函数**（队列 `LoadCandidatesAsync` 与合并引擎 `LoadLocalContextAsync` 各自先投影再指纹）。
- 实测：`s20_into_first_scan enqueued=1` → `remote_folder=f459701e`、`s20_into_second_scan enqueued=1` →
  `remote_folder=17bb1b39`，两步 `conflicts=0 owed_again=0` ⇒ 真服务器认了这两次移动，而且移动**不会被下一轮
  同步又判成一次待办**（这才是"没有同步问题"的口径）；`s20_out_to_root enqueued=0 remote_folder=(none)`。

### 二、被同一轮推翻的第一版规则（本轮最贵的一条）

第一版给"本机的夹在服务器上没有 counterpart"留了一条回退：拿不准就用 `bitwarden_folder_id` 列里那份，注释写
的是"让服务器继续持有它已经持有的那个夹"。实测 `s20_into_local_only`：`enqueued=1` 之后 `remote_folder=(none)`、
`s20_local_only_still_holds_folder=False` —— 条目在服务器上被抹平到了根。往下挖到的原因比这条规则本身重要：
**每一步的读数都是 `local_folder=(none)`**。那一列只有 pull 会写，而一次成功的 push 之后 pull 读到的是
NoChange，于是它什么都不改写 ⇒ 列里要么是空的、要么滞留在更早某个夹，"读它"等于读一个没人保证盖过的戳。它
在单元测试里之所以成立，只因为那份 fixture 恰好先做过一次会改写该列的 pull——**fixture 替规则撒了谎**。

⇒ 规则改成**只从本地树推导**：绑到远端夹的 category 答那个夹，其余一律答根（根、以及服务器没有 counterpart 的
本机夹）。这不是"简单点"，而是把判断从"某一列的历史状态"换成"用户屏幕上那棵树"：同一棵树永远欠同一次上传，
不需要别处有人在恰好的时机替它盖戳。代价是同一条被量化的事实换了方向——把条目放进一个 Bitwarden 收不下的本机
夹，服务器上它就是到了根；所以第九轮把断言换成这件事真正要问的两条。

### 三、四条负控真打（每条都重新构建、只看它该红的那一条）

- 批次1（队列不再投影）⇒ 夹→夹那条红在 `Expected: 1 / Actual: 0`：移动又变回"没人欠"。
- 批次2（合并引擎的本地引用不再投影）⇒ 同一条测试红在**后半**的 `ConflictsBackedUp`（`Actual: 1`）：判漂移的
  一边投影、另一边不投影，服务器就会把这台设备自己的移动当成一份要盖回去的远端改动。
- 批次3A（secure item 不投影）⇒ 笔记那条红；批次3B（`categoryId is null` 读列而不是答根）⇒ 挪出到根那条红。
- "挪进没有 counterpart 的夹"那条守卫在以上每一批里都保持绿——它当时守的还是**错的**规则，这一点由第二节的
  实测补上，不是由负控补上。

## 附：真服务器第九轮实测（2026-09-25，**#114 按改正后的规则重跑：八项读数全部同形**）

同一份探针、同一个容器（`vw-probe`，Vaultwarden **1.37.3**，本轮结束仍不 teardown），只改两件事：投影换成
"只从本地树推导"，以及 stage 20 那一步的断言从"服务器上还在原夹"换成它实际该问的两条 —— `at_root`（服务器上
这条已挪到根）与 `keeps_local_folder`（本机那一行仍在用户放进去的夹里）。日志
`probe-appdata/bwrestore/s20b-run.log`（**exit 0**，202 行，与上一轮同一组泄漏扫描：fixture 字面量 **0**、
44+ 字符 base64 形态串 **0**）。

- 建夹与绑定：`s20_folders made=2 distinct=2 ids=[228a0f90,37ce2f8f]`、`s20_bindings first=True second=True
  categories=2` ⇒ "建 → 拉 → 绑定成本机的 category"这条前置路走通。
- 移动：`s20_into_first_scan enqueued=1 refused=0` → `remote_folder=228a0f90`、`s20_into_second_scan enqueued=1`
  → `remote_folder=37ce2f8f`，两步 `conflicts=0 owed_again=0`、`merge[...ConflictsBackedUp=0/Unchanged=7...]`
  ⇒ 推得出去、服务器认、合并引擎不把它当成一份要盖回的远端改动。
- 挪进没有 counterpart 的本机夹：`s20_into_local_only_scan enqueued=1` → `remote_folder=(none)`
  `local_category=3`、`s20_local_only_at_root=True keeps_local_folder=True`、`owed_again=0` `conflicts=0`
  ⇒ 服务器上落到根，而**本机那一行留在原地**（pull 没把它踢回它曾经的夹、也没把 category 清成根），下一轮
  不再欠任何东西。
- 再挪到根：`s20_out_to_root_scan enqueued=0` → `remote_folder=(none)`、`local_category=`（空）⇒ 上一条已经在
  根了，这一步不该有幻影工作，队列确实什么都没欠。
- 每一步仍是 `local_folder=(none)` ⇒ 第八轮那条结论（push 不盖这列的戳）在这份字节上继续成立，也正是它判了
  回退规则死刑。

单元侧同轮换钉 4 条（`BitwardenLocalChangeQueueTests` 现 **42 条全绿**）：夹→夹（登录项与笔记各一条，笔记走
另一个编码器）、挪出到根（载荷里 `folderId` 属性**不存在**，而不是为空）、以及上面那条改判后的"挪进没有
counterpart 的夹"。**负控批次4 真打**：把第八轮那条被推翻的回退原样装回去、重建、只跑这一条 ⇒ 红在
`Expected: 1 / Actual: 0`（正是真服务器量到的"什么都不欠、服务器却在原夹"），换回新规则 42/42 绿。

### 四、门禁与剩下的口子（别读成账清了）

- 门禁全部跑在**出货的那份字节**上（代码提交 `6e99e22`，publish 与工作区源文件之间没有再动过任何一行）：`dotnet format Monica.slnx --verify-no-changes` 退出 **0**（0 改动）；`verify-commercial-release.ps1 -Configuration Release` ⇒ `cr_rc=0`，末行 `Commercial release verification passed.`（日志 `probe-appdata/gate114.log`），四份 trx 逐一读总数：单测 **10 `perf-budget` + 926 常规**（上一轮 922 ⇒ 本轮 +4 条夹移动覆盖）、UI **17 `perf-budget` + 244 常规**；同一串里的 `dotnet build -c Release --warnaserror` 因此是 **0 warning / 0 error**。产物级：`publish-desktop.ps1` ⇒ `pub_rc=0`（六个版本参数都是 Mandatory，漏一个就 exit 1 而门照跑旧字节——本轮踩过一次），`verify-artifact-runtime.ps1` ⇒ `art_rc=0`：`UI SMOKE passed` + `RUNTIME SMOKE passed`，loadMs=**293**/4000（库加载 actualMs=598/4000）、KeePass 20000 条 3.23MB：openMs=2059 / streamMs=479 / collectedMB=117.2 → growthMB=**7.2**/24、锁定态 10 拍轨迹 120.8/122.4/116.1/114.5/115.2/112.7/112.7/111.9/112.6/113.9，尾 5 拍 **中位 112.7MB／区间 111.9–113.9**（预算 120）、锁/解往返 passwords=25/25、notes=14/14、totp=1/1、wallet=4/4，瞬态回执 `retired=True`。**一条时序要写清**：第一次重跑产物门是**红**的（`smoke-ui` exit 1，KeePass 那一段没出读数、loadMs 从 119 跳到 1327），而它当时测的还是上一轮 16:18 那份旧字节（publish 因缺参数没跑成）；同一份旧字节在前一次跑是绿的 ⇒ 那是并发把机器压满时的计时抖动，不是新缺陷。重新 publish（18:12）后在安静机器上一次通过，上面所有数字出自那一次。
- **踩到的一条工具坑（记下来，它会再犯）**：`BitwardenLocalChangeQueue.cs` 被 python 以 LF 重写过，
  `dotnet format --verify-no-changes` 当场报 **21 处 WHITESPACE**，而**同一份纯 LF 的另两个文件一条都不报**——
  这条规则只在注释挤进对象初始化器／实参列表的位置才生效，所以"整文件是 LF"并不预示它一定红。改法：把该文件
  按仓库多数派换回 CRLF（`attr: text=auto` ⇒ 索引里仍是 LF，`git diff` 不会因此变脏）。
- **仍然没验 / 新规则的后果**：① 界面对"把条目放进一个 Bitwarden 收不下的本机夹"没有任何提示——按新规则它就
  是在服务器上到根，行为只有单元与探针读数证明，用户在屏幕上看不到这件事将会解释不了；② 镜像夹下面用户自己
  建的**子夹**同属"没有 counterpart"，其中的条目也按根发布（Bitwarden 的夹本来就是平面的，服务器下发的层级
  会各自绑定、不受影响）；③ #94 缺口 4 的其余各项原样：官方 Bitwarden 云、2FA／captcha／设备 OTP、
  `RefreshingToken` 自动刷新、离线堆队再回来推、卡片与证件的真服务器 create+update 往返、`IsArchived` 不进漂移
  指纹；④ App 层"真清除命令写出一条队列行"的行为证明照旧缺（`6bbf449` 只钉注册与类型）。

**机器状态**：容器 `vw-probe` 仍在跑（第八、九两轮各注册一个新账号，卷里只有探针数据），
`D:\Monica\probe-appdata\` 按用户要求一字不动；收法照旧 `docker rm -f vw-probe && docker volume rm vw-probe-data`，
且要等用户点头。

## 附：Android↔桌面端本地库互通对拍（2026-09-25，本轮唯一目标是"两边创建的文件必须一模一样"）

### 一、手上真正有什么证据（先把最弱的一条摆前面）

桌面上有**两份 Android 真跑出来的 `.mdbx`**：`Monica-all/.codex-tasks/20260731-mdbx2-local-create-failure/raw/`
里的 `ascii-create.mdbx` 与 `中文创建.mdbx`（2026-07-31 建，schema 17）。用只读 SQLite 直接读出来的原话是：

- 两份的 `vault_meta` **同为 15 列、同序**，取值一致：`format_version=MDBX-2`、`schema_version=17`、
  `min_reader_version=MDBX-1`、`min_writer_version=MDBX-2`、`default_tiga_mode=multi`、`tiga_policy_version=2`、
  `tiga_compliance_status=compliant`、`header_integrity_profile=mdbx-vault-header-hmac-sha256-v1`；
- 两份都是 `commits=1 / commit_operations=0 / projects=0 / entries=0`，**都没有 `.monica-root`**。
  建库时间晚于 Android 根功能落地（`3a3086b6` 07-28、`81ef058b` 07-29），`git log -S` 查过；
  `createMigrationFolders`（`Mdbx2Repository.kt:524`）是 MDBX-1→2 的夹迁移，不是补根。
  ⇒ **结论：Android 自己"新建后尚未写入"的库就是没有根项目的**，桌面端 `EnsureRootProjectAsync` 的惰性补齐是必要的，不是防御性的多余。
- 它们的密码不在手，所以"桌面端解锁一座 Android 建的库"**这一轮仍然没验过**，不许读成已确认。

### 二、把"一模一样"钉成了什么

1. `MdbxVaultShapeParityTests`（新）：桌面端新建库的 `vault_meta` 与上面那 15 列**同名同序同值**，
   且新建出来的文件与那两份"建了没写"的 Android 库**同样空**。
   **负控两条真打**：① 把 `ToFfiMode` 的 `Multi` 临时接到 `Power` ⇒ 外壳那条立刻红，而且红在
   `tiga_compliance_status`：`Expected: compliant / Actual: remediation-required`（比 `default_tiga_mode` 更早炸）——
   建库用的 TIGA 模式会决定合规位，两边要对齐的不止那一格；② 新建之后先插一个 project ⇒ 空库那条红在
   `Expected: projects=0 / Actual: projects=1`（同时 `commits 1→2`、`commit_operations 0→1`）。还原后 2/2 绿。
2. `MdbxAndroidRootTests`（新）：`UUID.nameUUIDFromBytes` 的复刻用 **Java 真跑出来的三条向量**钉死
   （`test`→`a422cc5f-e505-3b65-b298-fbf94de90dd8` 等），外加 v3/variant 两个 nibble 的形状断言。
   **为什么非要外部向量**：负控③把版本 nibble 写错成 v4 时，只有这 4 条红，存储层测试**全绿**——
   因为两边都用自己的函数算 id，对称地错。这类缺陷只能靠跨语言向量抓。
3. `MdbxUniffiBindingTests.Native_store_lays_out_the_root_project_and_folder_links_android_expects`（新）：
   在**真 dll** 上量布局——根项目标题/id、文件夹的 `groupId` 指向根、无分类条目落在根且载荷里**没有** `mdbx_folder_id`、
   有分类条目落 own 夹且载荷里有。负控④拔掉父链接 ⇒ 只红 `groupId` 那一条；负控⑤不建根 ⇒ 3 条红；
   负控⑥无条件写 `mdbx_folder_id` ⇒ 3 条红。
4. `MdbxRepositoryTests.Repository_files_the_project_tree_the_way_android_reads_it`（新）：同一件事的存储层口径，
   fake 桥补了 `GetParentProjectId / GetProjectIdByTitle / GetProjectIdForEntry` 才问得出来。
5. 顺手清掉一处死码：`PasswordEntry.MdbxLogicalEntryId` 全仓零读写，删。

### 三、门禁

跑在提交 `517eec9` 那份字节上：格式 0 改动、Release `--warnaserror` 0 warning、
commercial-release `cr_rc=0`（单测 10 `perf-budget` + 969 常规、UI 17 `perf-budget` + 244 常规）、
产物级 `pub_rc=0 / art_rc=0`（`CANONICAL VAULT passed`、loadMs=224/4000、KeePass 20000 条增长 2.5MB/24、
锁定态 112.5MB/120、锁/解往返 25/14/1/4、canonical passwords=27 notes=14 categories=6 attachmentOwners=6）。

**两条只在"整串单测同进程并行"下出现的红，如实记下**：`Vault_snapshot_loader_fans_out_reads_after_password_snapshot`
（842ms 对上限 4×188=752ms）与 `Searching_a_library_of_payloads_rebuilds_the_tree_within_its_budget`（444ms 对 400ms）。
隔离复跑三次 5/5 全绿，按 CI 的通道拆开跑（10 + 969）全绿 ⇒ 判定为负载计时抖动。
上一轮那次"1 条红但没抓到测试名"的运行很可能是同一族，但**没有名字证据，不写成已确认的 flake**。

### 四、仍然不等价的部分（**这些是产品决定，不是实现欠账**）

- **`.kdbx` 不是一条线**：Android 用 kotpass 0.10.0（读+写），桌面端用 KPCLib 2.0.4（只导入、不写回），
  并且桌面端丢历史、自定义图标、AutoType。两边写出的 `.kdbx` 字节**不等价**，也不打算在这一轮等价——
  要用户拍：桌面端要不要写回 kdbx，还是明确定位成"只能导入"。
- **passkey 私钥**只在本机 SQLite（`passkey_private_keys`），不在 mdbx 载荷里 ⇒ Android 看不见桌面端建的 passkey，反之亦然。
- Android 的 `steam-mafile` 载荷类型桌面端不认识；两边"谁认识哪些类型"仍然只写在各自己的常量里（交接文档 §2.3）。
- 随包 `mdbx_ffi.dll` 的来源链、Android 那 5 个 overlay 补丁是否影响保险库字节：都未核。

跨仓规格写在 `Monica-all/mdbx-android-desktop-interop-handoff.md`（工作区根，**不在任何仓里，不改 Android 代码**）。

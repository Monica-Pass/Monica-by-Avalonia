# Monica Avalonia 发布就绪与证据矩阵

最近本地验证：2026-09-29；历史远端配置审计：2026-07-26（本轮未重新查询远端）

本文件区分四种状态，避免把“代码已存在”“自动化测试通过”和“可以公开分发”
混为一谈：

- **已验证**：当前实现存在，并有源代码、自动化测试或工作流证据。
- **平台受限**：能力边界已明确，不能宣称与 Android 系统集成完全等价。
- **实验性**：可以构建或测试，但不是默认受支持的发布路径。
- **外部待完成**：需要证书、商店、真实设备或 GitHub 管理员权限，仓库代码不能替代。

## 产品与平台基线

| 要求 | 状态 | 实现证据 | 测试或决策证据 |
| --- | --- | --- | --- |
| Android 是功能与安全基线，桌面采用 Avalonia 跨平台交互 | 已验证 | `README.md`、`src/Monica.App/Features/` | `UiArchitectureTests.cs` 及各工作区 Headless 测试 |
| 密码与笔记支持嵌套分类 | 已验证 | `LocalCategoryPath`、密码/笔记目录投影与管理命令 | `LocalCategoryPathTests.cs`、`SecureNoteTests.cs` |
| Bitwarden 在线账户双向同步 | 已验证 | `Core/Bitwarden`、`Data/Bitwarden`、`Platform/Bitwarden`、同步工作区 | Bitwarden protocol、authentication、transport、merge、queue、conflict 和 UI 测试 |
| 浏览器本地配对与站点凭据查询 | 已验证 | `WindowsBrowserBridgeService`、Manifest V3 扩展 | `BrowserBridgeServiceTests.cs`、`DesktopIntegrationUiTests.cs`、协议文档 |
| Windows 托盘与全局快速搜索 | 已验证 | `AvaloniaTrayService`、`WindowsGlobalHotkeyService` | `DesktopIntegrationUiTests.cs`；非 Windows 平台按 capability 明示限制 |
| Android 钱包类型的桌面等价实现 | 已验证 | `ExtendedWalletItemData.cs`、钱包编辑器和详情投影 | `WalletParityTests.cs`、`WalletWorkflowUiTests.cs` |
| Windows 原生 passkey 状态 | 平台受限 | `NativePasskeyService.cs` 仅探测 WebAuthn client API | `PlatformServiceTests.cs`、`native-passkey-boundary.md`；Monica 不是系统 Credential Provider |
| 截图保护 | 已验证 | Windows capture-affinity adapter 与设置开关 | `AppSettingsTests.WindowCapture.cs`；能力由用户选择，不强制启用 |

## 数据与安全边界

| 控制 | 状态 | 实现证据 | 测试证据 |
| --- | --- | --- | --- |
| canonical vault 真源 | 已验证 | `MdbxBackedMonicaRepository`、`MdbxVaultStore`、`CanonicalVaultBootstrapService` | `MdbxRepositoryTests.cs`、`MdbxUniffiBindingTests.cs`、`SmokeVaultSeedTests.cs` |
| 解锁期 native handle 复用与锁定释放 | 已验证 | `MdbxVaultStore.Session.cs`、`VaultSessionService` | MDBX session/lease 测试及 dispatcher responsiveness 测试 |
| 主密码、短期密钥和账户秘密生命周期 | 已验证 | vault credential、Bitwarden secret container、lock-aware session manager | `VaultCredentialTests.cs`、Bitwarden account/session 测试 |
| 剪贴板最小暴露 | 已验证 | `SecureClipboardService` 的所有权检查与定时清除 | `SecurityBaselineTests.cs` 和 clipboard lifecycle 测试 |
| 后台敏感状态释放 | 已验证 | 最小化时释放工作区、详情、预热编辑器和可重建缓存 | `BackgroundMemoryUiTests.cs`、`BackgroundSensitiveDetailUiTests.cs`、`BackgroundTransientSecretUiTests.cs` |
| 浏览器桥接隔离 | 已验证 | IPv4 loopback、256 位会话令牌、HTTPS origin/extension caller 校验 | `BrowserBridgeServiceTests.cs`、`browser-bridge-protocol.md` |
| Bitwarden 网络与密码学限制 | 已验证（限制） | HTTPS endpoint policy、KDF 协议上限、Type 2 authenticated CipherString、固定时间 MAC 校验；`BitwardenProtocolTests.cs` | 协议上限允许 Argon2 m=256 MB；实测该参数使进程私有字节永久停在 270 MB（托管堆不回还），见 `native-hot-path-boundary.md` |
| 导入、同步和设置失败时不泄露秘密 | 已验证 | 错误净化、临时状态清理、原子设置持久化 | `*FailureSecurity.cs`、`AppSettingsTests.AtomicPersistence.cs` |

## 桌面体验、性能与可维护性

| 维度 | 状态 | 当前证据 | 剩余边界 |
| --- | --- | --- | --- |
| Avalonia 桌面任务布局 | 已验证 | 密码、笔记、动态口令、钱包、安全分析、同步、设置等拆分工作区及真实截图 | 仍需持续做人工信息层级与视觉一致性审查 |
| 键盘与基础辅助功能 | 已验证（自动化范围） | focusable command、AutomationProperties、live region 和焦点释放测试 | 屏幕阅读器、高对比度和系统缩放仍需真实 Windows 人工验收 |
| 本地化 | 已验证（自动化范围） | 中英文 localization service、语言持久化和界面绑定 | 仍需逐页人工校对截断、术语和复数规则 |
| 冷启动与首次导航 | 已验证（当前预算） | `ColdStartupPerformanceTests.cs`、延迟工作区物化和编辑器预热 | 必须在发布硬件上继续记录真实启动、解锁和大 vault 指标 |
| MDBX UI 响应性 | 已验证 | blocking UniFFI 工作移出 Avalonia dispatcher | `MdbxUiResponsivenessTests.cs` |
| 后台内存 | 已验证（行为） | 最小化释放可重建视觉树、投影和图片缓存 | 自动化验证对象可回收，不替代多小时进程 RSS/working-set soak test |
| 功能拆分 | 渐进进行 | 生成器已独立为 `GeneratorWorkspaceViewModel`；保留 300 行文件门与独立实例、绑定、锁定清理测试 | 其他工作区仍有大量共享 partial 状态；文件门不代表职责已经隔离 |

真实 AppHost 截图烟雾测试使用临时 canonical MDBX vault，验证了 26 个密码、
14 个笔记、1 个 TOTP、2 个钱包项目和 12 个页面截图。一次审计中的 vault 加载为
约 2.97 秒；该数据只用于诊断，不是跨硬件发布 SLO。

## 构建、发布与供应链

| 项目 | 状态 | 证据或边界 |
| --- | --- | --- |
| 统一商业质量门 | 已验证 | `eng/ci/verify-commercial-release.ps1` 执行卫生、文件体积、格式、漏洞、零警告构建、核心和 Headless UI 测试 |
| Windows JIT 包 | 已验证（默认） | Build/Release 工作流覆盖 `win-x64`；产物门真跑 canonical vault 与带窗口冒烟 |
| Linux / macOS JIT 包 | 受阻 | 缺自研原生引擎：产物里没有 `libmdbx_ffi.so` / `libmdbx_ffi.dylib`，还缺对应 `monica_crypto` 平台库，不能作为可用 vault 客户端交付。MDBX 引擎需由 Rust 源仓库 `crates/mdbx-ffi` 交叉编译后放入 `src/Monica.Platform/Mdbx/runtimes/<rid>/`；产物门现在会因缺引擎直接失败，不再警告后跳过 |
| NativeAOT 包 | 实验性 | 保留构建/冒烟信号，但 Build 打包与上传必须检查 smoke 的原始 outcome；`continue-on-error` 不再放行失败产物 |
| Action 供应链固定 | 已验证 | 所有第三方 Action 固定完整 commit SHA，checkout 不保留凭据 |
| 依赖更新 | 已验证（配置） | `.github/dependabot.yml` 每周检查 GitHub Actions 与 NuGet |
| 产物校验 | 已验证（工作流） | Draft Release 生成 `SHA256SUMS` 并执行 GitHub build provenance attestation |
| Release 可见性 | 已验证（限制） | 工作流移除非草稿输入并硬编码 `draft: true` |
| Windows 代码签名 | 外部待完成 | 当前无受信任 Authenticode 证书和签名验证证据 |
| macOS 签名与公证 | 外部待完成 | 当前无 Developer ID、notarization 和 Gatekeeper 验证证据 |
| Linux 仓库签名 | 外部待完成 | 当前生成 `.deb`，没有发行仓库元数据和仓库签名 |
| 多平台人工验收 | 外部待完成 | 安装、升级、卸载、窗口管理、辅助技术和真实硬件性能需在目标系统执行 |

## 远端 GitHub 安全设置

2026-07-26 的只读 API 审计观察到：

- `main` 没有 branch protection。
- Vulnerability alerts、secret scanning、push protection 和 Dependabot security updates
  处于关闭状态。
- GitHub Actions 允许所有 Action，远端没有强制 SHA pinning。

这些是 GitHub 管理员设置，不属于普通代码提交。本次审计没有擅自修改。正式公开发布前，
仓库管理员应明确批准并配置分支保护、必需状态检查、漏洞警报、秘密扫描、推送保护、
Dependabot security updates，以及组织允许的 Action 策略。

## 历史验证快照（2026-07-26）

以下为历史结果，不能视为本轮变更后的证据：

- `630/630` 个核心与集成测试通过。
- Cold-start 测试进程通过。
- 其余 Avalonia Headless UI 套件通过。
- `dotnet format --verify-no-changes` 通过。
- NuGet 直接与传递依赖漏洞审计通过。
- Release warnings-as-errors 构建为 `0 warning / 0 error`。

重新验证命令：

```powershell
cd ".\monica by avalonia"
.\eng\ci\verify-commercial-release.ps1 -Configuration Release
```

## 2026-09-29 增量改进与验证

范围仅限本仓库，未改 Android、外部 MDBX 引擎或真实个人 vault；本地验证未触发发行发布。

- **数据保真**：自定义字段的 title/value/is_protected/sort_order 均显式存在、值和顺序相等时，
  保留整个原始数组的未知元数据；
  `AndroidMdbxPayloadMergeTests` 与真实 native `MdbxCrossClientCompatTests` 覆盖备注编辑往返。
  编辑、删除、重排仍无稳定 ID 可安全归属未知元数据，这一边界没有被宣称解决。
- **旧会话隔离**：加载流程在异步恢复点校验版本/取消信号，旧 catch/finally 不清空或锁定
  新会话；账户列表和延迟时间线同样拒绝旧结果。`AppSettingsTests.VaultLoadLifecycle.cs`
  用显式阻塞/释放覆盖晚成功、取消、异常、锁后重新解锁和新旧加载重叠，而非用 sleep 赌时序。
- **生成器独立化**：选项、历史、算法与命令由子 ViewModel 持有；主窗口只提供生命周期及
  “保存为登录项”接点。现有控件、算法与布局保持不变。新增独立实例测试和真实绑定/锁定
  Headless 测试，保留既有生成规则、导航、最小化和快捷键验证。
- **失败产物门禁**：Build 的 pack/upload 检查 `steps.runtime-smoke.outcome`；publish
  命令非零退出直接失败。除静态回归外，本地 PowerShell 探针模拟“已有部分输出目录但
  publish 返回 23”，确认不会导出产物路径；返回 0 才报告路径。
- **支持范围**：当前库创建格式为 MDBX-2 / schema 17，可读清单来自运行时 manifest，
  文件自己的格式、schema、critical extensions 决定实际访问权限。Windows x64 是当前
  原生库齐全的验证目标，Linux/macOS 仍受原生库阻塞；Linux arm64 尚不在当前 Build 矩阵。

修改前基线：2026-09-28，`ade0e18cee5eca4aca2b806187c7dbbbc38dd99a`，
核心功能 1,166、核心性能 11、UI 功能 266、UI 性能 17，全部通过。
基线原始日志与 TRX 位于本地 `monica by avalonia/artifacts/improvement-20260928-baseline/`。

本轮修改后完整质量门（2026-09-29）通过：

| 通道 | 通过 / 执行 | 失败 / 跳过 |
| --- | --- | --- |
| 核心功能 | 1,200 / 1,200 | 0 / 0 |
| 核心性能预算（独占） | 11 / 11 | 0 / 0 |
| UI 功能 | 267 / 267 | 0 / 0 |
| UI 性能预算（独占） | 17 / 17 | 0 / 0 |

合计 **1,495 项**，格式、NuGet 直接/传递依赖漏洞审计与 Release warnings-as-errors
构建同时通过。该证据不是代码覆盖率报告，未测量覆盖率百分比。对应四份 TRX 为
`TestResults/Monica.Tests/{Monica.Tests,PerfBudget}.trx` 与
`TestResults/Monica.UiTests/{Monica.UiTests,PerfBudget}.trx`。

新的 Windows x64 self-contained JIT 产物已实际通过 `verify-artifact-runtime.ps1`
默认门限（未放宽任何预算）：

- canonical MDBX 初始化、填充、正确/错误密码与读写冒烟通过。
- 1280×800 真实窗口的页面、键盘、笔记编辑、状态通知、锁定/重新解锁通过。
- 首轮 vault 加载 **782 ms / 4,000 ms**；锁定稳定窗口私有字节中位数
  **117.0 MB / 120 MB**。这是本机本次样本，不是跨硬件性能保证。
- 20,000 项 KeePass 读取/详情流式访问后回收增量 **3.5 MB / 24 MB**；
  新建、编辑、回收站、搜索、历史与第二进程文件交接检查通过。
- 解锁前后数量一致：密码 25、笔记 14、TOTP 1、钱包 4。

开发期先复现了 11 项旧会话缺陷回归与 3 项发布门禁回归的失败，修复后均通过；
最终结论以完整质量门及真实产物门为准。原始开发日志仍保留于当前会话工具记录，
失败的尝试未作为通过证据。退出码探针结果见 `publish-failure-probe.log`。

补充截图验收通过：浅色 **1280×800** 与深色 **900×700** 各捕获 13 个页面，共 26 张。
两轮均通过页面、键盘与锁定/重新解锁检查，进程退出码为 0。生成器两张截图已目视检查：
选项、生成结果、历史遮罩及生成/复制/保存命令正常显示，无空白页面；本次没有重新设计
布局。截图目录为 `shots-1280-light/` 与 `shots-900-dark/`，日志为 `shots-*.runtime.log`。
本任务启动的 AppHost 进程均已退出，使用的 vault、配置和截图均为隔离合成数据。

本地日志目录：`monica by avalonia/artifacts/improvement-20260929/`（不纳入版本控制）。

本轮不证明所有网络同步/切库/退出竞态已被穷尽，也不替代屏幕阅读器、系统高对比度、
多显示器缩放、安装升级与多小时内存 soak 验收；这些仍需单独执行。

## 发布决策

- **内部测试或候选包**：当前 Draft Release 工作流可用于生成带校验和与 provenance 的候选包。
- **公开正式发布**：在 Windows/macOS 原生签名、目标平台人工验收和远端仓库安全设置完成前，
  状态仍为阻塞。
- **NativeAOT**：保持实验性，不应替代默认 JIT 包。

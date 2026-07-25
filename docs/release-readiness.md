# Monica Avalonia 发布就绪与证据矩阵

审计日期：2026-07-26

本文件区分四种状态，避免把“代码已存在”“自动化测试通过”和“可以公开分发”
混为一谈：

- **已验证**：当前实现存在，并有源代码、自动化测试或工作流证据。
- **平台受限**：能力边界已明确，不能宣称与 Android 系统集成完全等价。
- **实验性**：可以构建或测试，但不是默认受支持的发布路径。
- **外部待完成**：需要证书、商店、真实设备或 GitHub 管理员权限，仓库代码不能替代。

## 产品与平台基线

| 要求 | 状态 | 实现证据 | 测试或决策证据 |
| --- | --- | --- | --- |
| Android 是功能与安全基线，桌面按 WinUI 3 交互 | 已验证 | `README.md`、`src/Monica.App/Features/` | `UiArchitectureTests.cs` 及各工作区 Headless 测试 |
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
| Bitwarden 网络与密码学限制 | 已验证 | HTTPS endpoint policy、KDF 上限、Type 2 authenticated CipherString、固定时间 MAC 校验 | `BitwardenProtocolTests.cs`、network authentication 和 transport 测试 |
| 导入、同步和设置失败时不泄露秘密 | 已验证 | 错误净化、临时状态清理、原子设置持久化 | `*FailureSecurity.cs`、`AppSettingsTests.AtomicPersistence.cs` |

## 桌面体验、性能与可维护性

| 维度 | 状态 | 当前证据 | 剩余边界 |
| --- | --- | --- | --- |
| WinUI 风格任务布局 | 已验证 | 密码、笔记、动态口令、钱包、安全分析、同步、设置等拆分工作区及真实截图 | 仍需持续做人工信息层级与视觉一致性审查 |
| 键盘与基础辅助功能 | 已验证（自动化范围） | focusable command、AutomationProperties、live region 和焦点释放测试 | 屏幕阅读器、高对比度和系统缩放仍需真实 Windows 人工验收 |
| 本地化 | 已验证（自动化范围） | 中英文 localization service、语言持久化和界面绑定 | 仍需逐页人工校对截断、术语和复数规则 |
| 冷启动与首次导航 | 已验证（当前预算） | `ColdStartupPerformanceTests.cs`、延迟工作区物化和编辑器预热 | 必须在发布硬件上继续记录真实启动、解锁和大 vault 指标 |
| MDBX UI 响应性 | 已验证 | blocking UniFFI 工作移出 Avalonia dispatcher | `MdbxUiResponsivenessTests.cs` |
| 后台内存 | 已验证（行为） | 最小化释放可重建视觉树、投影和图片缓存 | 自动化验证对象可回收，不替代多小时进程 RSS/working-set soak test |
| 功能拆分 | 已验证 | 商业质量门限制重点功能源文件不超过 300 行 | 跨功能共享规则必须继续下沉到 Core/Data/Platform |

真实 AppHost 截图烟雾测试使用临时 canonical MDBX vault，验证了 26 个密码、
14 个笔记、1 个 TOTP、2 个钱包项目和 12 个页面截图。一次审计中的 vault 加载为
约 2.97 秒；该数据只用于诊断，不是跨硬件发布 SLO。

## 构建、发布与供应链

| 项目 | 状态 | 证据或边界 |
| --- | --- | --- |
| 统一商业质量门 | 已验证 | `eng/ci/verify-commercial-release.ps1` 执行卫生、文件体积、格式、漏洞、零警告构建、核心和 Headless UI 测试 |
| JIT 桌面包 | 已验证（默认） | Build/Release 工作流覆盖 Windows、Linux、macOS；Release 默认 `jit` |
| NativeAOT 包 | 实验性 | CI 保留 AOT 构建信号，但 Release 输入明确标为 experimental，且不再默认选择 |
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

## 当前验证快照

在文档与工作流修订前的最后一次 Release 商业质量门结果：

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

## 发布决策

- **内部测试或候选包**：当前 Draft Release 工作流可用于生成带校验和与 provenance 的候选包。
- **公开正式发布**：在 Windows/macOS 原生签名、目标平台人工验收和远端仓库安全设置完成前，
  状态仍为阻塞。
- **NativeAOT**：保持实验性，不应替代默认 JIT 包。

# Monica 系统级通行密钥路线

2026-10-06 核对；本文记录目标、平台入口和验收门槛，不代表功能已全部交付。

## 产品目标

注册网站通行密钥时可以选择 Monica 保存；登录时系统或浏览器列出 Monica 账户，用户经
指纹、面容、PIN 或主密码验证后，Monica 返回标准 WebAuthn 响应。管理页能查看、删除、
迁移和备份受支持的凭据。私钥不会显示在界面、日志或剪贴板中。

通行密钥必须使用网站发出的注册/登录挑战，并把公钥注册到网站。手工填写网站和用户名后
生成一把密钥，只能生成本地对象，不能代替网站注册。

## 三端范围

本项目的“三端”固定指 **Windows、macOS、Linux**。Android 是移动端功能对齐和数据格式的
参考实现，但不计入这三个桌面端目标；iOS 也不在本阶段的交付范围内。

“应用内能用平台认证器”和“浏览器/系统能发现 Monica”是两个独立的能力。前者可以由
Avalonia 进程调用平台 API，后者必须交付该平台认可的原生供应器扩展或插件。

## 按平台接入

| 平台 | 系统入口 | 本地用户验证 | 仓库现状与限制 |
| --- | --- | --- | --- |
| Windows | 应用内：Win32 WebAuthn API + Windows Hello；系统级：Windows 11 WebAuthn Plugin API、`IPluginAuthenticator` | Windows Hello 指纹/面容/PIN | 应用内客户端边界已编译和模拟验证；系统插件、管理页和真实网站验收尚未交付 |
| macOS | 应用内/浏览器：AuthenticationServices；系统级：Credential Provider Extension | Touch ID、设备认证 | 需要原生扩展、App Group/Keychain 共享存储、entitlement 和签名；Avalonia UI 本身不是供应器 |
| Linux | 应用内：Monica 软件密钥；浏览器/硬件：WebAuthn、FIDO2、USB/NFC/BLE 按浏览器和桌面环境接入 | 桌面密钥环、FIDO2 设备或 Monica 主密码 | 没有可直接等同 Windows Plugin API 的统一桌面供应器入口；不能承诺所有浏览器/应用自动发现 Monica |

Android 的 Credential Provider Service 继续作为移动端对齐实现，但它不改变本项目“三端”
的定义。

## 凭据归属与同步

1. Monica 管理的凭据：密钥由保险库保护，平台供应器负责系统接入和用户验证。统一条目
   格式、canonical MDBX、备份恢复和冲突策略后，可在多个客户端使用同一凭据。
2. 系统管理的凭据：调用 Windows Hello 或其他系统认证器，Monica 保存公开元数据并发起
   使用操作。不能假定系统私钥可以导出进 Monica；Apple/Google 等系统服务也可能自行同步。

目前桌面软件密钥仍存在独立 SQLite 表中。只加 `MdbxDatabaseId` 字段不会把密钥放入 MDBX；
原生 `passkey` 的只读查看也不代表已能管理、备份或使用它。交付跨端功能前必须补齐真正的
共享存储、主密码变更、导出导入及锁定生命周期。

## 实施与验收顺序

- 校准 WebAuthn 字节格式、原始 client data、DER 签名、RP/origin 与操作级用户验证。
- 建立统一 passkey 存储与迁移，验证多账户、删除、密钥恢复、主密码变更和丢失设备。
- Windows 增加打包 COM 插件及注册/启用页，验证系统签名请求、Hello 验证、取消与锁定，
  再将创建/获取结果接入现有保险库。使用微软样例的 SDK 和 OS 版本门槛。
- macOS 增加独立 AuthenticationServices Credential Provider Extension，并以 App Group/Keychain
  共享安全存储连接主应用。
- Linux 先交付浏览器 WebAuthn/FIDO2 路径和软件密钥的保存、读取、使用；系统级供应器按桌面
  环境单独适配，不能伪装成统一能力。
- 补齐 Android 共用格式，保证移动端与三端共享格式、备份和恢复策略。
- 在真实网站和自有 WebAuthn 测试服务验收：保存、列出、选账户、登录、删凭据、注销供应器，
  并验证跨设备和无网络场景。编译与模拟认证器测试不能代替这些结果。

## 官方依据

- [Windows WebAuthn Plugin APIs](https://learn.microsoft.com/en-us/windows/security/identity-protection/hello-for-business/webauthn-apis)
- [Microsoft Passkey Manager sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/passkeymanager/)
- [Android credential provider](https://developer.android.com/identity/sign-in/credential-provider)
- [Apple credential provider controller](https://developer.apple.com/documentation/authenticationservices/ascredentialproviderviewcontroller)
- [WebAuthn Level 3](https://www.w3.org/TR/webauthn-3/)

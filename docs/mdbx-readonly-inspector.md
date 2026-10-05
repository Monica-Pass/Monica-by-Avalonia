# MDBX 原生条目只读查看

桌面端的 MDBX > 健康检查页会列出默认保险库中没有业务编辑器的原生类型，以及
`payload_schema_version != 1` 的对象。类型匹配区分大小写；原生 `ssh-key`、`identity`、
`api-token`、`passkey`、Steam 类型和其他扩展类型保留原生身份，通过同一个通用查看区呈现。
它们不进入普通密码、钱包编辑、自动填充或 Bitwarden 登录项写回流程。

## 读取与清理

- 列表通过 `ListObjectSummaries` 分页读取标题、原生类型、对象 ID、集合 ID、版本和修订，
  不读取正文，也不会为了查看列表创建根项目或写审计事件。
- 点击“查看原始字段”后才调用 `RevealObjectWithLimits`，上限为 4 MiB。非允许的授权结果
  和当前桌面无法落实的授权约束都不会向查看界面返回正文。
- 每个字段默认隐藏，支持逐项显示与敏感剪贴板复制。JSON 数字保持原始文本；空字符串、
  `null`、布尔值、数组、嵌套对象和 Unicode 分别展示。非对象、无效 JSON、过深或超过
  1,000 个顶层字段的正文使用完整原始 JSON 查看，避免丢字段和大量控件分配。
- 锁定、失焦、后台、退出、导航、切换默认库和刷新列表都会撤销当前详情，并清理字段缓存。
  迟到的读取结果不会重新显示；回到窗口后需要再次点击查看。
- 关闭剪贴板定时清理仍会保留敏感内容归属，锁定和迟到复制后的清理继续生效。清理会核对
  剪贴板当前内容，保留用户后来复制的外部内容。

## 写入保护与兼容边界

Data 仓储和 Native adapter 会检查真实的原生类型与载荷版本。未知对象和高版本对象不能
经密码保存、移动、删除或恢复入口改写为普通登录项。完整清空与 Monica JSON 备份遇到
当前格式无法承载的原生对象时明确失败，避免不完整备份被误认为全库备份。按类型的 CSV
导出仍是该业务类型的导出。

已知版本业务条目的回收站继续使用原生 `GetObject` 兼容读取：当前引擎的授权 disclosure
不允许已删除对象。该兼容路径在读取正文前检查摘要，限制为本端拥有的准确类型/版本，
不能作为未知对象的授权回退。它还不是全业务类型的 summary/disclosure 迁移。

原生 disclosure 会写入审计记录，因此文件级只读会话只允许浏览摘要，拒绝正文 disclosure。
对象摘要与 disclosure 前后的修订检查可检测期间发生的对象变更，但尚未提供所有写入的
原子 CAS，也不代表返回后发生的远端策略撤销能实时清除界面。

返回的 JSON 已经过原生 FFI 的序列化，不能把字段值保留解释为输入文件逐字节不变。
本次验证了 `9007199254740993` 与上述代表性字段，没有声明任意精度数字的全面互通。

## 验证

在 `monica by avalonia` 下先构建应用，再使用 PowerShell 7.6 / .NET 10 检查真实原生引擎：

```powershell
dotnet build src/Monica.App/Monica.App.csproj -c Release --no-restore
pwsh -NoProfile -File eng/mdbx/verify-object-reader.ps1
# Optional native business-read timing, independently from fixture creation:
pwsh -NoProfile -File eng/mdbx/verify-object-reader.ps1 -PerformanceEntryCount 5000
```

脚本只加载产品程序集，不构建、加载或恢复 `Monica.Tests.dll`。它创建独立临时 vault，
验证摘要无正文/无审计、原始字段、准确类型/版本识别、误写拒绝、回收站兼容、全量清空与
不完整 JSON 备份拒绝、4 MiB 限额、取消与只读会话保护。fixture 与随机凭据只在本机临时
目录和进程内使用，输出只包含检查名称与结果，结束后清理。

2026-10-05：27 项原生检查通过；另加 5,000 个登录项后，真实原生业务读取返回
5,001 项，单次耗时 1,241.9 ms（含重新开库）。这是本机的合成 fixture 读数，
不是大库、真实硬件或完整 UI 的性能验收。应用 Release 构建为 0 警告、0 错误。

这不是 Android 真机双向互操作验收，也不替代完整测试套件和界面人工验收。

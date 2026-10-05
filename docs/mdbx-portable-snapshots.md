# MDBX 一致性文件快照

WebDAV 与 OneDrive 的 MDBX 上传、下载和冲突恢复现通过同一快照服务执行。
这是整库文件级 portable snapshot：原生 SQLite online backup 将已提交 WAL 内容
合入一个 `.mdbx` 文件，再验证并发布。未知原生类型、较高载荷版本、历史、墓碑和
库内附件随数据库保留，不经过桌面业务模型反序列化或 JSON 重新导出。

## 导出与验证

- `OpenSnapshotStreamAsync` 返回不可写、可定位的临时快照流；释放流时清理其临时目录。
  `CreateSnapshotAsync` 提供文件导出 API，目标及其 WAL、SHM、journal、`.blobs`
  必须不存在，发布时不覆盖已有文件。`OpenLocalStreamAsync` 也改为只读流。
- 活动源文件通过原生便携备份 API 以只读方式读取。原生复制执行完整性和 vault 身份校验，
  输出使用 DELETE journal mode，不依赖旁边的 WAL/SHM。
  已关闭且没有 sidecar 的源文件先在只读保护句柄下复制到专用暂存目录，再由原生
  API 处理；这避免 SQLite 即使只读打开 WAL 模式文件也可能创建空 WAL/SHM 的副作用。
- 解锁和 `HealthCheck` 在第二份专用验证副本上执行。原生 `OpenVault` 可能迁移或写入
  解锁记账，因此不能直接打开用户原文件或随后上传的候选文件来做验证。
  仅接受 `Healthy` 且没有 Error/Critical 的检查结果；Info/Warning 不单独阻断。
- 本服务仅接受运行时当前可写格式与当前 schema，拒绝未知关键扩展、待升级结构和无法
  判定的格式。未知条目类型与高版本载荷可以原样保留，但不等同于支持更高数据库 schema。

## 外部附件边界

本轮不打包外部附件存储。源文件旁存在非空 `<vault>.blobs`，或原生检查发现外部附件
引用时，导出与恢复均返回 `external-blobs`，避免把缺少附件的单文件当作完整备份。
引用检查包括当前及已删除附件的 chunk、保留快照中的外部引用，即使实体 blob 缺失也
会拒绝；原生 inventory 不扫描孤立 blob 或任意 `object_versions` 载荷。库存维护限额
超出或检查失败会阻断操作，不会忽略未检查的记录。

## 恢复与回滚

下载先写独立 incoming 文件并刷盘，再构建和验证候选快照。替换阶段持有 Data 仓储的
文件替换 gate：等活动 CRUD 结束，关闭缓存原生句柄，阻止新读取复用旧文件状态。
该 gate 是进程内协调；Windows 另用文件共享模式拒绝外部写句柄。目标存在 WAL、SHM、
journal 或不安全的重解析点时拒绝替换，不删除这些可能仍属于其他进程的文件。

已有目标必须与候选属于同一 vault。内容相同只提交远端版本元数据；内容不同先保存
经过验证的 `*.local-conflict-*.mdbx` 恢复副本，再原子替换目标并提交元数据。
文件发布或元数据回调失败时尝试恢复原文件，元数据对象也恢复原状态。
回滚受阻则返回 `rollback-failed`，保留原文件的临时回滚副本和已生成的 recovery 文件。
元数据提交成功后，迟到的取消不会再撤销成功结果。
Windows 的部分替换失败也可能已把原文件移入回滚位置，失败路径会检查并恢复该文件。
保护句柄在发布后重新打开；本轮不提供对任意外部进程或进程崩溃的完整事务保证。

元数据目前没有独立持久化 VaultId。之前已同步或离线可用的库若丢失本地副本，恢复
会以 `identity-unavailable` 拒绝，避免仅凭同一密码接受另一座库；首次未绑定目标可
在验证凭据后创建。后续需要持久化身份来支持缓存丢失后的安全自动恢复。

云端恢复前取消并等待 Bitwarden、导入及敏感后台任务；已有编辑对话框或不可取消
操作时拒绝替换；默认库存在未保存笔记时也拒绝，先由用户保存或关闭。默认库恢复
成功后清理旧业务集合、编辑状态和缓存，重新加载工作区。
该屏障由云端和本地恢复入口共同使用。

## 上传期间的本地变更

上传的是固定快照。远端接受后，服务持有同一个仓储 gate，重新复制当前源文件，比较
两份 portable snapshot 的 SHA-256，并在 gate 内提交远端 ETag/时间和同步状态。
内容仍相同标记 `Synced`；上传期间出现本地变更则保留 `PendingUpload`，下次继续上传。
该检查协调本进程的仓储操作，不宣称防止任意其他进程在检查后修改文件。
仓储在每次原生写入尝试结束后保守标记 Pending，覆盖多步骤操作的部分失败和取消。
它通过元数据 gate 读取最新记录再保存，仅修改状态和错误，保留刚提交的远端版本号；
普通上传提交只获取原生 gate，不提升文件替换代次，也不拒绝排队的正常编辑。

## 会话、错误与当前交付范围

云传输关联解锁会话的取消令牌。同步原生备份调用不能中途打断，取消会等其结束；
只清理本次成功创建的文件，不删除原生拒绝覆盖的已有目标。快照原生异常只返回稳定
原因码，不保留包含凭据或载荷的底层 message/inner exception；界面显示对应的本地化
错误，诊断不读取或记录密码、JSON 正文和附件内容。

这项工作提供整库快照服务、云传输及本地备份/恢复入口，不代表 Android `.sync`
分段、增量合并、外部 blob 传输协议已经完整对齐。

## 本地备份与恢复

MDBX 工作区选中保险库后，在“详情 → 本地加密备份”使用“导出备份”或“从备份恢复”。
需要解锁、可用的原生快照功能、文件选择能力及现存本地副本。备份沿用当前库的密码；
恢复要求相同 vault 身份和有效凭据。含外部存储附件的库会拒绝，不能把不完整的单文件
当作完整备份；之前已绑定但本地副本丢失的库仍会保守拒绝。

- 导出沿用现有导出授权策略。保存对话框只选择路径，不先打开或截断文件；目标及
  sidecar 在验证前和发布前各检查一次，已有备份不会覆盖。
- 恢复选择器只返回原始本地路径，不把整个数据库载入内存，也不复制走原文件的 WAL
  或 `.blobs` 关联。选中的备份始终由用户保留；服务只清理自身创建的暂存文件。
- 确认替换目标后使用同一恢复屏障、同身份验证、recovery 与回滚流程。未保存笔记、
  活动编辑、取消或验证失败会保留当前内容。
- 本地/外部库恢复后为 `LocalOnly`，WebDAV/OneDrive 工作副本恢复后为 `PendingUpload`。
  当前远端 ETag、修改时间及 LastSyncedAt 保留，恢复不执行网络上传。
- 最近的恢复副本与库 ID 绑定，详情区显示文件名和可选择复制的路径；换库不显示其他
  库的回执，锁定清理回执展示。默认库恢复后清理并重新加载业务工作区。
- 文件已提交后的取消不显示“操作已取消”；刷新异常单独提示操作已完成而界面刷新失败，
  保留已提交的数据与远端版本，不把它改记为下载失败。

切换语言会重建 MDBX 列表的本地化显示项，同时保留选中的库 ID。

主要实现：

- [快照服务](../monica%20by%20avalonia/src/Monica.Platform/Services/MdbxVaultService.Snapshots.cs)
- [恢复服务](../monica%20by%20avalonia/src/Monica.Platform/Services/MdbxVaultService.Restore.cs)
- [原生适配器](../monica%20by%20avalonia/src/Monica.Platform/Services/MdbxUniffiNativeBridge.Snapshots.cs)
- [文件替换协调](../monica%20by%20avalonia/src/Monica.Data/Mdbx/MdbxVaultStore.FileReplacement.cs)
- [传输元数据提交](../monica%20by%20avalonia/src/Monica.App/Features/Mdbx/MainWindowViewModel.MdbxSnapshotTransfers.cs)
- [本地命令](../monica%20by%20avalonia/src/Monica.App/Features/Mdbx/MainWindowViewModel.MdbxLocalSnapshotCommands.cs)
- [本地界面](../monica%20by%20avalonia/src/Monica.App/Features/Mdbx/MdbxLocalSnapshotView.axaml)

## 验证记录

2026-10-05 在 Windows x64、PowerShell 7.6、.NET 10 和随产品交付的原生运行时执行：

- `dotnet build src/Monica.App/Monica.App.csproj -c Release --no-restore`：成功，0 warning / 0 error。
- `dotnet format Monica.slnx --verify-no-changes --no-restore`：退出码 0。
- `eng/mdbx/verify-snapshots.ps1`：49 项通过。验证器在内存编译，加载产品程序集；
  覆盖已提交 WAL、固定只读流、原文件及 incoming 不被验证修改、未知载荷与库内附件、
  非覆盖导出、无源 sidecar 副作用、上传 freshness、缓存句柄重开、同身份恢复、元数据
  失败回滚、初次目标失败清理、错误凭据/损坏/未来格式/不同身份/丢失绑定缓存拒绝、
  外部 blobs/ref 拒绝、外部写句柄与 journal 拒绝、排队操作代次，以及两种云来源的
  仓储写入在上传提交后仍标记 Pending 并保留最新 validator。
- `eng/mdbx/verify-object-reader.ps1`：既有 27 项原生检查通过。
- 项目既有的重点功能文件 300 行门与 `git diff --check`：通过。

测试源已按新接口更新，但本轮未生成、恢复或运行此前报毒的 `Monica.Tests.dll`。
单元/界面测试套件未执行；原生脚本不替代真实云服务器或界面交互验证。

本地入口这一轮的实际验证：

- 应用 Release 构建再次通过，0 warning / 0 error；全解决方案格式检查退出码 0。
- `eng/mdbx/verify-local-snapshots.ps1`：31 项通过。在内存编译验证器，使用产品程序集、
  真实原生运行时及 Avalonia 的第三方 headless 后端，不加载测试程序集。通过生产 DI
  装配业务服务，仅替换 picker、确认、导出授权和剪贴板交互，并对刷新失败单独注入故障。
  覆盖编译 XAML 命令/参数绑定、中英文自动化名称、焦点、无整库缓冲导出、目标不覆盖、
  取消/拒绝、四类源的恢复状态与 validators、用户备份原样保留、恢复回执、已提交后
  刷新失败、未保存笔记、默认库业务列表重载及锁定禁用/清理。
- 原生快照脚本 49 项回归通过；重点功能 300 行门与 `git diff --check` 通过。
- 用虚构数据生成并检查实际 UI 位图：`artifacts/mdbx-local-snapshots/local-snapshots-en.png`。
  验证器需已安装的 `Avalonia.Headless.dll`，可由 `-HeadlessAssembly` 指定；截图仅为本地 QA
  产物，没有用户密码或真实库内容。原生文件对话框、Narrator 和高对比度手动走查未执行。
- 新增 10 个本地命令单元测试案例的源码；本轮仍未生成、恢复或执行 `Monica.Tests.dll`。

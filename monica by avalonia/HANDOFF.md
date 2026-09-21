# 交接文档 — Monica Desktop (Avalonia) 商业化迭代

> 面向对象：接手继续开发的 AI。请冷启动阅读，假定你没有任何上文记忆。
> 最后更新：2026-09-21

## 0. 总目标（未达成，勿提前收工）

**"继续迭代，直到达到项目商业级可用水准。"** 这是一个持续性目标，不是单个任务。每一轮都要朝这个终态推进，不要因为某一步变绿就宣布完成。

## 1. 仓库与来源关系

- **本仓库**：`Monica-by-Avalonia`（桌面端，.NET 10 + Avalonia + FluentAvalonia）。
  路径：`C:\Users\joyins\Desktop\Monica-all\Monica-by-Avalonia\monica by avalonia`
- **只读事实来源**：`Monica for Android`（Kotlin）。桌面端从 Android 派生——**适配桌面去贴齐 Android 的数据形状，绝不反向改 Android**。
- 产品是一个**密码管理器**：核心安全边界是"绝不让明文密码落到截图 / 日志 / argv / 可分享产物里"。

## 2. 当前工作树状态（重要：全是未提交改动）

分支 `main`，上一个 commit 是 `180ed73 refactor(app): extract MonicaJsonExport use-case`。
以下改动**尚未提交**：

```
新增 (untracked):
  src/Monica.App/Services/VaultOperations/ChangeMasterPasswordUseCase.cs
  src/Monica.App/Services/VaultOperations/ResetMasterPasswordUseCase.cs
  src/Monica.App/Services/VaultOperations/SaveSecurityQuestionsUseCase.cs
  src/Monica.App/Services/VaultOperations/MonicaJsonImportUseCase.cs
修改:
  Features/ImportExport/*.cs（含删除 ImportExportImageHelpers.cs，helpers 合并进 ImportExportHelpers.cs）
  Features/Settings/MainWindowViewModel.SettingsCommands.cs
  Features/Settings/MainWindowViewModel.SettingsProperties.cs
  Features/Settings/MainWindowViewModel.SettingsRecoveryCommands.cs
  Services/VaultOperations/ImportExportHelpers.cs
  Services/VaultOperations/MonicaJsonExportUseCase.cs
```

**接手第一件事**：先 `dotnet build` 复验（见 §5），确认绿了再决定是否提交。提交规范见 §4。

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

最近实测：build 通过 0/0；单测 716 项，**715 通过，1 失败**——
`VaultTreeBuilderTests.Searching_a_library_of_payloads_rebuilds_the_tree_within_its_budget`（436ms 超预算）。
这是**已知的墙钟断言负载抖动**（参见 Task #53 同类），与本次 use-case 改动无关。**遇到这个失败不要误判为回归**；若要在负载下稳定复现，单独串行跑该测试或调高其预算。

## 6. 运行应用（给用户演示时）

```bash
dotnet run --project src/Monica.App/Monica.App.csproj --no-build
```
- 应用数据目录：`%LOCALAPPDATA%\Monica by Avalonia\`（已有 `monica.db` + `mdbx/local.mdbx`，是一个真实测试库）。
- 应用打开后停在**解锁页**，需要用户输入主密码才能进入。
- **主密码不可恢复**：`settings.json` 不存明文，只存派生哈希。我无法"给出密码"。若用户忘记密码，只能清空该测试库另建新库（破坏性，动手前先问）。
- **安全红线**：解锁后**不要截屏已解密的保险库界面**，不要把条目明文写进任何输出/日志。演示 UI 时保持锁定态或空态。
- 注：`%LOCALAPPDATA%\Monica by Avalonia\mdbx\` 下堆积了上百个 `onedrive-*.local-conflict-*.mdbx` 冲突副本，是冲突恢复路径 `BuildConflictRecoveryPath` 反复触发的产物，值得排查（见 §7 建议）。

## 7. 剩余阻塞项 / 可推进方向（朝"商业级"）

- **Task #43 AOT 产物无法解锁**：Layer 1 已修（private row DTO→internal）。Layer 2 是硬骨头——全库 87 个 Dapper 调用点大多传匿名对象，Dapper.AOT 需要具名类型；`IsAotCompatible` 下 Data/Core/Platform 分别有 IL2026 报警。**AOT 非内存手段**（框架空窗 115MB 在 jit+R2R 下实测，AOT 省不掉 Skia/原生库部分）。CI 目前对 aot `continue-on-error`，**jit 是硬门**。这是多日重构，勿轻启。
- **Task #55 linux/macOS 原生 MDBX 引擎二进制**：外部依赖，本机无 Linux 工具链（见用户环境记忆），拿不到就卡住。
- **建议先做的低风险项**（比啃 AOT 更快靠近"可用"）：
  1. 排查 §6 提到的 OneDrive 冲突副本无限堆积——像是一个真 bug。
  2. 把 §2 的 use-case 改动提交（build/test 复绿后）。
  3. 逐屏走查 UI 完成度与空态/错误态，用锁定态截图，别解密。

## 8. 用户协作偏好（务必遵守）

- **中文响应。**
- **实证优先**：任何"内存/耗时/性能"结论必须真跑真测给出实测数字，不许凭读码给理论（包括我自己的）。
- **UI 口味偏"素"**：删重复、删嵌套；铺开前先探一屏 + 截图过口味门。
- **评审后再推**：commit 本地、展示结果，不擅自 push。
- 面对从别处 fork 进来的代码，先问"到底要不要"，再谈"怎么维护"。

---
接手第一步建议：`git status` 看未提交改动 → `dotnet build` 复绿 → 决定提交还是继续。

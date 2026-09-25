# 主目录归并审计

日期：2026-09-25。主目录 `D:/GodotALS`，分支 `main`，检查基线 `a53cf82`。下述审计记录保留清理前的状态；随后用户明确要求删除已合入的多余目录，实际归档与删除结果见末节。没有执行 merge 或覆盖正式资产。

## 代码提交

| 历史目录 | HEAD | 相对 main 未合并提交 | 未提交跟踪文件/非 ignored 未跟踪文件 |
|---|---|---:|---|
| `D:/GodotALS-p3-direction-alignment` | `b7ab2e9` | 0 | 无 |
| `D:/GodotALS-p4-pose-foot-placement` | `2b7628a` | 0 | 无 |
| `D:/GodotALS-p5a-events-actions` | `253cb25` | 0 | 无 |

证据：逐目录 `git status --short`、`git log main..HEAD`、`git diff main...HEAD --stat`、`git ls-files --others --exclude-standard` 均无输出；三个 HEAD 的 `merge-base --is-ancestor HEAD main` 都返回 0。主仓库 `git branch --no-merged main` 和 `git stash list` 均为空。因此三个旧分支没有需要再合入的代码提交，不能用旧工作树与当前 main 的普通差异反向覆盖新实现。

## 本地生成资产与诊断产物

Git 已合并不代表 ignored 文件已经搬迁。对各目录 `git ls-files --others --ignored --exclude-standard` 的资产逐文件做同路径存在性及 SHA256 比较，对 artifacts 做同路径存在性检查：

| 目录 | ignored 资产数 | 主目录缺少 | 同路径字节不同 | artifacts 数 | 主目录同路径缺少的 artifacts |
|---|---:|---:|---:|---:|---:|
| P3 | 288 | 0 | 4 | 26 | 26 |
| P4 | 288 | 0 | 144 | 191 | 6 |
| P5A | 403 | 115 | 0 | 28,844 | 28,805 |

- P5A 的 288 个 `assets/generated/als_v4` 文件在主目录齐全且哈希一致；缺少的 115 个全部位于 `assets/generated/web_export`，含 clips JSON、report.json 和 skeleton_38.json。它们属于旧导出产物，尚未搬迁；本审计不把它们算作普通 Godot 正式资产缺失。
- P3/P4 的非 `.import` 差异均为 als_manifest.json、compiled/als_animation_set.tres、partial/als_manifest.partial.json；其余分别为 1/141 个 `.import` 文件。主目录已有后续版本，不能为追求目录相同而覆盖。
- P3 未迁移的 26 个 artifacts 包含输入探针/方向截图和日志；P4 的 6 个为 locktest.json、p2a-task2-final-error.log、p2a-task2-final-output.log、review-path-escape/lock.json、review-path-escape/manifest.json、tamper-lock.json。
- P5A 未迁移的 28,805 个 artifacts 包含旧导出日志、逐帧截图/报告及测试证据。这里仅证明主目录没有同路径文件；未对全部 artifacts 做内容去重，不能声称这些全是唯一内容，也不能声称所有证据已归档。
- 旧目录还留有 `.godot`、bin/obj、`.superpowers` 会话产物及 P5A `.tmp-p5a-diag` / `.tmp-schema-*` 临时诊断构建目录。它们被 Git 忽略，不是待合并的分支代码；本次未迁移、未删除。

`D:/GodotALS-alt-fixture-verify-6b4582cb1c0240da98d2c913ef7322b6` 本次枚举为空，不是注册 worktree。`D:/GodotALS-References/ALS-Refactored` 为参考源码目录，不作为本项目待合并 worktree。UE 项目 `D:/AdvancedLocomotionSystemV` 是导出源工程，不应整体合入 Godot 项目；最新 Standing exporter 镜像一致性见对应原生对照记录。

## 主目录已有但尚未提交的内容

审计开始时已有四个跟踪文件修改：P4 计划、project.godot、scenes/demo/p4_locomotion_demo.tscn、AlsLayerBlendingRuntime.cs。另有头颈诊断文档/场景/脚本以及一批 `.cs.uid` 未跟踪文件。它们已经在主目录，不属于旧目录遗漏；保留原状，本次文档工作不顺带提交或撤销它们。

## 后续处理

1. 日常实现仍只在主目录 main；当前不需要再 merge 三个历史分支。
2. 若要清理旧目录，先将缺失诊断、web_export 和需要留存的临时实验产物做独立归档，生成来源/哈希清单并核实文档引用；不要直接复制旧 compiled/manifest/import 覆盖主目录。
3. 上述归档前不得清理的条件已在用户后续授权清理时满足，见下节。正式资产可运行性的最终证明仍是干净导入/构建/运行，不由本次文件哈希检查替代。

## 用户授权后的归档与删除结果

用户明确要求删除 GodotALS 相关、已合入主目录的多余目录。执行前再次确认三个工作树无未提交/非 ignored 未跟踪内容、HEAD 均为 main 祖先、根及内部无 reparse point，并未发现命令行指向这些目录的 Godot/.NET/testhost 进程。

使用 `scripts/archive-merged-worktree.ps1` 归档全部 ignored 文件，排除可重新生成的 `.godot`、bin、obj、__pycache__。每个文件先计算源 SHA256，再写 ZIP，最后重新打开 ZIP 逐项校验数量、长度和 SHA256；独立 manifest 保存源 HEAD、文件清单/哈希、排除清单及 ZIP 哈希。总共保存 30,129 个文件，包括原生验证日志、截图、web_export、旧生成资产和会话资料。

归档目录：`D:/GodotALS/artifacts/history/worktrees/2026-09-25/`。

| ZIP | 文件数 | 压缩包字节数 | SHA256 |
|---|---:|---:|---|
| GodotALS-p3-direction-alignment.zip | 314 | 22,675,168 | A8D9791DA5704CC44C13B503D441AF1D9C481D112F47E901A65D9F4776C069C5 |
| GodotALS-p4-pose-foot-placement.zip | 491 | 65,819,991 | ED08789D4649C0C48DF3659BFAD3787E55D964F25B490053C21130FB1B03F580 |
| GodotALS-p5a-events-actions.zip | 29,324 | 8,385,955,542 | 9E0F43687C5550DC1DD0F08B8019BAC29497F0ED2D2D1ABFC125CC77CD7FA733 |

每个 ZIP 旁有同名 `.zip.manifest.json`。归档是本地 ignored 数据，不随 Git 提交；需要跨机器保留时应备份这整个归档目录。

校验成功后使用 Git worktree remove 移除注册关系和三个明确路径的工作目录；因存在已归档 ignored 文件使用 force，没有重置 main、删除分支或清理主目录。另核实临时目录为空后，以非递归删除移除 `D:/GodotALS-alt-fixture-verify-6b4582cb1c0240da98d2c913ef7322b6`。

可恢复性：三个分支与提交历史保留，跟踪代码可从 Git 恢复；ignored 资料可从 ZIP 按相对路径提取；排除的缓存未备份，应通过构建/导入重新生成。查旧证据时提取所需文件到单独的临时查阅目录，不把整个 ZIP 解压覆盖主项目。

主目录用户修改未动。`D:/GodotALS-References`、`D:/AdvancedLocomotionSystemV` 和 `D:/UnrealEngine` 保留，不属于本次冗余工作树清理范围。

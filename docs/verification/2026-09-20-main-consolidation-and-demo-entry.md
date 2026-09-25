# 主目录整合与普通 Demo 完整入口

## 交付目录与 Git

后续唯一开发入口为 `${env:GODOT_ALS_ROOT}` 的 `main`。本次没有创建新的项目副本或 worktree。

- 原主目录停在 `a69fda2`，P5A 工作目录原 HEAD 为 `d6b45e3`，之后积累了大量未提交的实现、原生对照数据及报告。
- `253cb25` 保存该开发状态，随后 fast-forward 合入主目录。它是开发基线，包含尚未验收的部分，不是完成全部 ALS 的声明。
- 生成资产不受 Git 管理：从 P5A 目录同步 144 个差异文件，覆盖前备份到主目录 `artifacts/consolidation-20260920/assets-before-sync`。未复制 `.godot`、bin/obj 缓存，主目录重新构建并导入。
- 主目录原有的 `2026-08-28-p4-aim-layering-foot-placement.md` 未提交修改保持原样，SHA-256 前后一致：`78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。
- 历史工作目录保留为可恢复副本，其中未纳入 Git 的大型截图/逐帧诊断仍在原位置。新的实现、测试和证据都写入主目录。
- `AGENTS.md` 记录主目录约定；Python 缓存加入忽略规则。

## 修复检出后的资产字节变化

Windows Git 的自动换行转换将原始采样 JSON 改成 CRLF，导致 source index 的 SHA-256 校验失败。
进一步核实发现：部分已导出的图定义本身就是 CRLF，并且也被其他导出数据按文件字节引用。
因此不能简单把所有 JSON 统一成 LF。

`fd8582b` 将 `assets/config/*.json` 和 `raw_sequences/*.json` 标记为 `-text`，
保存原始导出字节。167 个文件的磁盘内容和 Git index 对象逐一核对一致。
43 个文件在 Git 中显示整文件变化，忽略行尾空白后无内容变化；没有重算哈希来迁就被改变的数据。

修复了一项 Import 回归对 ignored 文件 `artifacts/raw-sequence-source-request.json` 的依赖。
测试改读已提交 source index 内保留的原始 request，仍断言固定 binding digest、definition digest、
75 players、109 samples、76 root assets，以及所有原始文件哈希和依赖闭包。

## 普通运行入口

`project.godot` 主场景改为 `scenes/demo/als_demo.tscn`。
启动脚本先确定不可变的动画选项，再实例化原 `p4_locomotion_demo.tscn`，保证角色构造时的
Worker/Query/Commit 分组和实际动画求值使用同一配置。未改用户编辑的工作场景布局。

默认完整入口启用分层、Aim、完整 Root 来源、Refactored 姿势/移动曲线、分阶段 Foot IK、
基于支撑的 Foot Lock、gravity twist、最终接触、未锁脚穿地修正及脚趾接触约束。
这些是此前已有的组件，本次完成普通入口接线，没有用新算法替代旧算法。
最终接触/脚趾约束仍属于 Godot 几何策略，不能称为 UE 整角色逐帧 1:1 已通过。

显式诊断场景继续按原命令行参数运行。普通入口带 `-- --legacy-animation` 可复查旧链路，
选择 `-- --overlay=Rifle` 可运行对应姿势。道具模型的装备玩法没有因此完成。

Editor 使用 F5 运行项目；F6 直接运行旧内部场景仍是诊断入口。

## 本次验证

全部命令从 `${env:GODOT_ALS_ROOT}` 执行，日志位于 `artifacts/consolidation-20260920`。

| 检查 | 结果与范围 |
| --- | --- |
| 主目录 Godot 构建 | `dotnet build GodotALS.csproj --no-restore -p:Optimize=true`，0 警告/0 错误 |
| 主目录 Editor 导入 | headless import 正常退出；不是导入缓存复制 |
| Import Release 全库 | 2263 通过，1 项原有 Skip，0 失败；`import-release-all.log` |
| 导入/闭包/共享缓存专项 | 修复后 159/159；`import-byte-regression-fixed.log` |
| Core Release 专项 | 脚部/锁脚/Ragdoll 57 项通过；同次额外复查的旧 P4 oracle 1 项失败，见下文 |
| 普通入口渲染键鼠回放 | 360 帧；Alt 行走 180 帧，释放 Alt 移动 90 帧，左右输入 150/120 帧，4 次鼠标事件；`keyboard-final.log` |
| 实际完整链路 | 每帧断言分层启用、最终 Root/Foot 身份、锁脚曲线生产者一致；脚趾锚定实际触发 83 帧 |
| 多帧截图 | `keyboard-frames/frame-0060.png` 至 `frame-0360.png`，每 60 帧一张，共 6 张；检查了起步、横移换向和跑步阶段 |
| 旧入口回放 | `keyboard-legacy-final.log`，360 帧通过，脚趾约束 0 帧，退出无引擎错误 |
| Rifle Overlay 回放 | `keyboard-rifle-final.log`，360 帧通过，断言实际 Overlay 与所选项一致，退出无引擎错误 |
| 普通主场景 | 不传动画参数启动，角色/场景正常显示，HUD Errors=0；`demo-default.log` |
| 十角色单线程/并行 | 各 3621 帧；2 次取消、1 次提交等待、520 空中帧、600 蹲姿帧、2400 Overlay 帧；`dispatch-single.log` / `dispatch-parallel.log` |

十角色两种模式的摘要一致：pose `BF25422552331C92`、root `8B519543E987009F`、
result `02BF0BAAA9542349`。脚趾接触覆盖 981 帧/10 角色，未锁脚修正覆盖 1140 帧/10 角色。
该测试保留显式诊断参数，同时证明这次入口整合没有破坏原有单/多线程诊断路径。

键鼠回放使用 Godot 输入事件注入，不能替代用户实际硬件的鼠标验收。
该回放把地形整理成平地夹具，不能当成斜坡、台阶、平台全程接触验收。
截图/HUD 的 FPS 不作为性能认证。

## 未关闭的回归及下一步

初次 Debug 全库记录保留：Core 2503 通过/24 失败；Import 因数据字节校验失败和大值类型栈溢出中止。
后者修复字节和临时文件依赖后，在 Release 全库通过。Ragdoll 零分配测试在 Release 专项通过，
不以未优化 Debug 的分配结果作性能结论。

Core 仍不能宣称全绿：

- 旧 P4 `AlsPoseGoldenTests.Production_core_replays_every_port_oracle_without_issues` 在 Release 仍失败：
  四个蹲姿转身 case 的 `turnYawDelta` 与冻结 oracle 不一致。没有修改容差或重写期望值来掩盖它。
- P5A 22 项冻结计划相关测试报 snapshot 不符合 frozen native plan inputs。这次不是找不到原生对照程序，
  而是程序构建后拒绝当前 snapshot；需要核对共同播放所有者/绑定变化与原计划的对应关系。
- 本次没有重新运行 UE，也没有签收完整 P4 或 P5A 阶段证书。

后续按原范围推进：先核对这些冻结回归、完整地形接触与上下身/起停换髋联合观感，
再收尾 P5A 的玩法消费者和动作摘要，继续 P5B 道具/Overlay gameplay、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/Pose Recovery/完整 Camera，最后 P7 十分钟预算。音频继续暂缓。

后续修复及复验见 [冻结回放修复记录](2026-09-20-frozen-reference-replay.md)。
上文保留本批当时的失败证据，不用后续结果覆盖历史记录。

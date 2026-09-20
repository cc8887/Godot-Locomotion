# 平台脚部输入的 UE 原生回放（第一百八十批）

## 结论与边界

当前约 1.51 mm 平台漂移已经定位到脚部位置节点的腿长限制；相同实际输入
交给 UE `FAlsRigUnit_ApplyFootOffsetLocation.Execute`，得到与 Godot 相同的
位置及弹簧历史。没有通过取消约束、改腿长、改锁量或放宽原平台门槛修饰结果。
这证明该节点在这两条轨迹上的移植一致，尚不能证明上游姿势、腿长绑定、
锁目标和完整 UE 角色一致。原平台测试仍失败，默认完整入口仍未启用。

## 本批实现

- `AlsRefactoredFootRigFrame.CommittedFeet` 提供已提交脚骨及位置节点输入，
  包含真实髋高度、腿根位置、锁目标、环境偏移、腿长和插值设置。候选与取消
  不覆盖诊断快照；跳过节点的帧显式标记为未求值。
- `MovementPlatformGraphSmoke` 增加显式 `--platform-trace=绝对路径`，
  在原验收之前保存全部 360 帧输入、查询身份、Rig 输出和实际骨骼位置。
  未开启时不收集逐帧对象；原运动/脚锁验收条件保持。
- 导出插件新增 `ReplayFootOffsetLocations`，使用临时骨架与两个原生节点
  独立历史回放实际输入。它不读取 Godot 的预期位置，不保存 UE 资产。
  本批轨迹从首帧开始连续播放，不覆盖运行中重建/重初始化。
- Python 负责调用与帧身份校验，Node 比较原生结果和 Godot 输出；另一个
  分析脚本区分位置限制、IK 求解及 Godot 写回误差。

## 对照结果

输入 `artifacts/foot-platform-180-inputs-{rotation,translation}.json`；
冷启动原生输出 `foot-platform-180-native-{rotation,translation}.json`；
普通 Editor 输出 `foot-platform-180-editor-{rotation,translation}.json`。

| 指标 | 旋转平台 | 平移平台 |
| --- | ---: | ---: |
| 完整输入帧 | 360 | 360 |
| 实际左右脚节点求值 | 718 | 718 |
| 位置与 UE 最大差异（cm） | 1.465e-14 | 1.476e-14 |
| 弹簧偏移最大差异（cm） | 9.945e-8 | 9.945e-8 |
| 全轨迹触发腿长限制的脚部样本 | 57 | 57 |
| 锁定期间触发限制的脚部样本 | 45 | 45 |
| Godot 实际骨骼与 Rig 写回差异（m） | 8.50e-7 | 3.45e-7 |
| 锁定期间 IK 末端与位置节点差异（m） | 0 | 0 |
| 原平台整段最大相对漂移（m） | 0.00150918 | 0.00154901 |
| 原生判断未约束的连续窗口漂移（m） | 0.000014601 | 0.000000564 |

未约束窗口按左右脚独立分段，排除原锁目标的腿根约束及原生位置节点实际
裁剪，各有 192 个连续脚部样本。这个附加诊断不能替代原测试，也不能当作
真实脚掌接触的完整判据。最大整段漂移均出现在第 320 帧右脚。

原生节点先插值 Z 偏移，再把腿向量限制到 `LegLength * MaxLegStretchRatio`
（原默认 0.99）。因此锁目标固定也不保证最终脚骨不动，裁剪会随髋/腿根变化。
下一步必须核对这些上游输入的来源及版本，不能仅把阈值提高到 2 mm。

冷启动与普通 Editor 输出分别字节一致，SHA-256：

- rotation：`4A1374E00434C0348FB0DB559D31C55BC43C88E848412041D665CB7086DB1413`
- translation：`E729FE79BEBBC12962C948791C5BD0F1621DE2CB97E6AD0C2C0D219D8A1A3918`

## 工程验证

完整项目 Editor 构建与插件依赖审计通过。首次构建因系统缺少 .NET 10 失败，
保留原日志；改用所选引擎自带的 x64 .NET 10 后成功，没有修改系统运行时。
冷启动及普通 Editor 回放均退出 0，四份输出通过独立比较，资产保存数 0。
普通 Editor 启动包含两条 `LogAutomationTest: Condition failed`，第 173 批
普通 Editor 日志也有同样两条；尚未归因，不宣称启动日志零错误。
DataValidation 退出 0，0 错误/3 警告，涉及旧 AI 的 PawnActionsComponent
和 Navmesh 版本；未在这次脚部诊断中改动这些资产。

新增提交/取消与跳过节点诊断测试，相关 Import 共 10 项通过：
`artifacts/tests/foot-platform-180-diagnostics.trx`。Godot 优化 Debug 构建
0 警告/0 错误，Python 编译检查和 `git diff --check` 通过。
插件以 ALS 显式依赖在全新 `artifacts/unreal/foot-platform-180-package` 中
隔离打包成功（退出 0，2 分 14 秒），未部署覆盖项目插件；打包后项目审计
再次 `AUDIT_PASS`。完整项目、冷启动、普通 Editor、DataValidation、打包
日志保留在项目 `Saved/Logs/PluginBuild` 和仓库 `artifacts/unreal/`。

## 下一步及原规划归属

本项仍属 P4 的脚部/pelvis/平台闭环，修复当前基础姿势问题必须在 P3/P4
完成，不推迟到 Ragdoll 后。继续核对 V4 源姿势与 Refactored 脚部消费者的
腿长、参考骨架、髋偏移和约束输入，再完成生产分发多帧率/多角色、中断恢复、
起步支撑、换髋及上身的 UE 整角色/人工验收，随后启用默认完整入口。

P5A 通用通知/同步/动作、P5B 全 Overlay 和道具、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/Pose Recovery/完整 Camera、P7 十分钟性能预算仍在原计划。
音频暂缓。第 178 批 Core 23 项失败、Import public surface 与嵌套遍历栈溢出
仍未解决，不能用本批专项通过替代全量回归。总目标保持进行中。

# 实际静止帧 Gather 对照与运行时舍入修复

本批在主目录 `.` / `main` 完成。普通 demo 未切换到实验 Core 物理，四项整链失败仍未关闭。

## 捕获边界

`AlsPersistentContactPair` 按值保留真正传入 Gather 的身体、shape-world 姿态、速度和最终生效设置。几何读取保持 solver 顺序，排除已禁用的历史点；原始 initialPhi 与 Gather 后 initialPhi 分开保存。读取仅允许在 pending 事务内，提交/回滚后拒绝读取，不增加每步堆分配。

`AlsIslandStepCapture` 在原 schema 1 中追加 `contacts[].gather`，保留旧行与共同求解阶段，兼容旧 coupled 导出器。该边界位于历史匹配之后，尚不能证明历史匹配本身与 UE 相同。

捕获高速120Hz AnimMan 1140/1141/1142、普通60Hz Mannequin 540/541/542，共六帧、156对、369点。其中369点全部已有锚点，27对采用共享 initialPhi；没有无锚点首次碰撞样本。首次采集遗漏必需 report 参数、在第0帧被拒绝，保留原日志；补齐参数后六帧采集成功。采集和整链矩阵日志分名保存。

## 原生独立重算

新增 `PhysicsRawGatherInputs=` / `PhysicsRawGatherOutput=`。UE 原生 collision container 从原始几何、锚点、初始穿透与身体输入执行 GatherInput，不使用捕获行生成输出。替身形状仅用于选择并核验原生逐点/共享 initialPhi 策略，不运行窄相；通过公开 history API 初始化之前的最小穿透深度，不写私有成员。不声称本导出器重新验证了真实形状碰撞或历史匹配。

新参考 `assets/config/v4_physics_raw_gather_reference.json`：1,178,066 bytes，重复冷导出退出0且逐字节相同，SHA256：

`7652916505E2904E7792914D892728ACA71BFF36757BFC2878E803362F28ED8A`

旧格式捕获缺少 raw Gather 时明确拒绝，进程退出1且没有输出文件。原有 coupled / synthetic 导出路径不变。

## 实际发现与修复

同样的369个输入，修复前 .NET8 Core 重算对 UE 最大差0，而 .NET9 重算与实际Godot捕获均为 `1.0662403e-6`。沿用上一批胶囊中的定位方式，确认 `Vector3.Cross` 在宿主.NET9上的舍入变化影响 Gather。改为显式float乘法/减法后，当前Core在.NET8/.NET9均与这批原生点完全一致。没有修改门槛、正交化切线或改变休眠阈值。

新参考保留修复前Godot输出，测试分别断言“当前重算最大差0”和“旧捕获偏差1.0662403e-6”，避免抹掉真实回归证据。Godot contact smoke在实际.NET9.0.17宿主独立重放全部369点并严格比较。

Core全量首轮发现上一批已添加 `Als.Import.Tests` friend，但旧契约测试仍只允许Core.Tests。本批同步明确允许这两个测试程序集，运行时适配器仍非friend；没有扩大生产访问面。修复后的Core全量2835通过（沿用排除P5aGolden/P5aTraceSchema的既有命令）。

## 验证结果和限制

- Godot优化构建：0错误/0警告。
- Import Release固定JIT串行全量：2424通过、1既有条件跳过，退出0。
- .NET9：新raw、三套contact、四套coupled共8项通过；新raw差0，原有432组synthetic Gather差0。
- Godot 30/60/120Hz：各369项新raw回放、246项胶囊实际输入回放及所有旧contact smoke通过。
- 全部十二项整链重新运行，仍8/12；八份成功报告均有数值变化，不是字节不变。
- 普通60Hz Mannequin第600帧才睡（之前595）；高速120Hz Mannequin第881帧睡（之前869），AnimMan仍未睡。后者末秒最大线速度2.062793cm/s、角速度0.550925rad/s。平移30/旋转30仍在平台启动前不满足静止门槛。没有宣称整链稳定性改善。
- UE完整Editor构建与插件审计通过，最终fingerprint `F114DE2A7716944E1784D3ABF09B83C944978E27FA29FC76243E22C731014C0D`；首次编译的废弃Geometry API警告已改GetGeometry，最终构建无该警告。
- DataValidation退出0，0error/3既有warning。未发现本批所需额外打包规则。
- 普通Editor PID34788成功加载导出器并输出标记，原生退出 `3221225477`（0xC0000005）；两个既有Condition failed仍在，DLL已释放。普通重启门禁仍失败，不能称UE退出问题修复。

全部日志、原始快照、矩阵报告和重复导出在 `artifacts/physics-raw-gather-20260922/`。UE项目镜像与主仓库三个导出器源码文件哈希相同。用户P4规划未修改，原hash保留。

## 下一步

继续从历史匹配之前的真实输入核对：检测点、保存锚点、摩擦比、initialPhi、帧间恢复/失效与提交生命周期；本批raw快照仅覆盖准备后的历史状态。结合首次落地与低频平台失败的帧序列，定位仍存在的整链休眠差异。

普通Ragdoll/Get-up/Pose Recovery、Mantle、完整ALS Camera及最终十分钟性能预算继续保留在总目标中，未因独立参考通过而宣告完成。

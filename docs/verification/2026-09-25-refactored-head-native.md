# 原始 Head 图连续求值对照

在主目录 main 扩展已有 View 导出器，新增 `ALS_VIEW_HEAD_GRAPH=1`。原始 `AB_Als_Head_C` 的六个编译节点负责实际更新和求值；绑定临时真实 `AB_Als_C` 父实例、原始 Settings、实际骨架和 raw 数据。父实例先执行原生 View/Spine 更新，再运行 Head 图。图模式完全忽略请求里的 initializeHead / updateHead 标记，InitializeHead / RefreshHead 由真实图的相关性回调触发。

基础输入固定为原始 `A_Als_Stand_Pose`，附带一条 `HeadProbe=.37` 曲线。不是完整 Locomotion/Layering 上游，也不是普通 Godot Demo。

## 验证

三频率各五秒共 1050 帧，79 个逻辑骨，共 82,950 个骨骼姿态。C# 从自身状态和图 counter 推进，不读取 UE 输出作为下一帧状态或 Look 权重。覆盖权重 .2 / 1 / 0、Head 隐藏半秒后恢复、瞄准/第一人称/动作切换、平台旋转及零游戏 delta。

每个频率均自主产生两次初始化；隐藏帧数分别 15 / 30 / 60。Head 初始化和换侧布尔与原生逐帧一致，浮点状态使用上一批明确的单位预算（权重 2e-6、角度及角速度各 .001），不宣称逐位一致。

| Hz | 姿态数 | 位置最大差（cm） | 四元数分量最大差（处理 q/-q） | 缩放最大差 |
|---|---:|---:|---:|---:|
| 30 | 11,850 | 2.3853341344e-6 | 6.1594988943e-8 | 0 |
| 60 | 23,700 | 2.3858631700e-6 | 6.8375500803e-8 | 0 |
| 120 | 47,400 | 8.4732751964e-6 | 1.8731580520e-7 | 0 |

姿态门槛在首轮前设为位置 .001 cm、四元数分量 1e-5、缩放 1e-5，首轮及最终均通过，未修改生产算法或门槛。基础曲线的存在性和数值完全相同。资源参考绑定 Head inputs、graph、compiled inventory、base inputs 的字节哈希，并记录实际类和基础 Sequence 路径。

Import View/Head/Look/LayerNative 最终 33 项通过，Godot Optimize 构建 0 warning / 0 error。未执行全量 Core/Import 或 Godot 场景验收。日志保存在 `artifacts/refactored-head-native`。

UE 完整 Editor 构建和插件审计通过，最终 fingerprint `05A9CE0F51DDC7551C7BD4BB9F38FB66FEADD9F8B6FA126D064F1ABA9512FE88`，日志前缀 `20260924T185010000Z-6abe47c64b474f3f90138ac36ddfd430`。两次构建均成功，无本轮编译失败。冷导退出 0，无 error/warning；普通 Editor PID 32616 实际退出 0，输出字节相同，SHA256 `A9F1218BD6A054065FD9B003DB60C50A0D196A98AC7802BF1ABA12E4C3A9844D`。普通日志两条旧 Condition failed 和五项旧 warning 仍保留，不宣称这些问题已修复。DataValidation 退出 0，0 error / 3 旧 warning。未做游戏打包验收。

## 边界与下一步

新增可选模式后，旧显式状态导出冷重跑退出 0，与已提交参考字节一致（SHA256 `DBADBB04BC2E9A01D37EA5E74CDAE1AECCF188992429E13D89340307E119DCBB`）。

这批补齐了实际 Head 图遍历→回调→Look采样→MeshAdditive→最终姿态的证据，但只有固定 Stand 基础输入。尚未验证整条 Locomotion/Layering/Head/ControlRig 链的交互、整图不被遍历后的恢复或场景视觉。下一步推进完整 Refactored 布局和宿主整合，保持既有事务提交、冻结资源及线程边界，再完成普通 Mantle、Ragdoll 和相机/性能验收。普通 Demo 本批未切换；头颈拉伸调查、道具物理和音频保持用户指定的暂缓。

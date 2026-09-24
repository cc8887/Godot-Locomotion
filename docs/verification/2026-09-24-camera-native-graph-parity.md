# Camera 原生连续图对照

在 `D:/GodotALS` 的 `main` 为上一批相机图运行时增加独立 UE 对照。本批没有替换普通 Demo 相机，也没有修改 Godot 生产运行时。

## 独立参考的边界

新增 Editor-only `ExportCameraGraphTrace`，加载真实 Refactored `AB_Als_Camera` 生成类，使用 `B_Als_CameraComponent` 默认对象实际引用的骨架网格。每条轨迹使用新建的瞬态 AnimInstance；保持原始节点与连接，连续执行原生 proxy update 和实例 ParallelEvaluateAnimation，后者提供真实的 cached-pose 求值作用域。

输入仅为五个 gameplay tag 和左右肩值。绕过 NativeUpdateAnimation 从 Character/CDO 重新覆盖输入；不是复制曲线公式，也没有将 Godot 输出送回 UE 作为期望值。逐帧使用 FMemMark 和独立 GFrameCounter，结束时恢复全局帧值，不保存 UE 资产。

这证明受控输入下的 Camera AnimGraph 输出，不包含 Character 的 desired/actual rotation mode 选择、camera component tick、socket、平台、FOV、场景碰撞或完整游戏输入链路。

## 覆盖与结果

- 三档 30/60/120 Hz，各一组 9 秒组合切换与一组 4 秒逐帧 Look 中断，共 2,730 连续帧。组合轨迹覆盖 Stance/Gait/View/Shoulder/Roll、独占 Ragdoll 与重新进入 Look States。
- 增加 384 种组合：RotationMode 三态及无效回退 × Stance 两态 × Gait 三态 × ViewMode 两态 × 默认/Mantle/Roll/Ragdoll × 左右肩。每个组合以 3 秒步长验证稳定分支，不能替代真实频率的过渡验收。
- 最终共 3,114 帧、29,701 个曲线值，曲线存在性逐帧一致。最大绝对差 `0.0002746582`；预设位置曲线容差 0.001 UE cm，其他曲线 0.000003，未因结果放宽。
- Import 相机专项 24 项通过，`artifacts/camera-native-tests/camera-final.trx`。包括上一批候选丢弃重试、图编译拒绝检查、富曲线与旋转参考。测试项目 Release 构建 0 警告/错误。没有重跑 Core/Import 全量或 Godot 渲染，也没有新的生产 C# 变更需要优化构建。
- 原生图 `AB_Als_Camera.uasset` SHA256：`B94F55FDBEC51F61AD8398C290B080429935A774113F8D8BB2EDC99EC3088737`。
- 正式参考 `assets/config/refactored_camera_graph_reference.json` SHA256：`8A26256E898B4A62D012F594B47F9059E5E3B273C2FF41ABB240EF6BBCE222F9`。数据内保留输入、帧序号、原生源/mesh/引擎及请求摘要；Python 脚本可重建输入。

## UE 构建及过程问题

按 ue-diagnosing-plugin-build-load 执行整个 Editor 目标构建与全部项目插件审计，没有复制 DLL 或修改 BuildId。最终日志前缀为 `D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260924T095648206Z-2d6cd452d76544d0bc552787dd75a259`，fingerprint `2AABEBF5350E74B6E46D1E5F32785955F3F7DB1710B7EFCA7EB305D42B1CFAB5`，四项目插件审计通过。最终构建没有首轮的重名警告。

首轮命令行导出因新接口漏建 FMemMark 触发内存栈断言，退出 3，`artifacts/camera-graph-native-1.log` 保留。补每帧内存作用域后，又按 UE proxy 源码确认并补入每帧 GFrameCounter，确保属性访问子系统不因整段循环共享帧号而被跳过。辅助访问函数改名为 UpdateRoot，消除遮蔽基类 Update 的警告。主目录与 UE 插件中的最终 CPP SHA256 均为 `69BD65F827863F0EF8C40D0ACFA9257BD5A2EA6FEE71CFCE638AA45FBE6BF878`。

最终二进制的 2,730 帧两次冷导出均退出 0，数据字节一致，日志 `camera-graph-native-2.log`/`-3.log`。扩展后的 3,114 帧由普通 Editor PID 42464 导出，随后正常退出 0；日志 `camera-graph-normal-editor.log`。启动仍有两条既有 Condition failed，未声称修复。DataValidation 退出 0，0 error/3 既有 warning，日志 `camera-graph-data-validation.log`。本插件仅为 Editor 模块，未进行打包游戏验收。

扩展后再以冷命令行复导，`artifacts/camera-graph-native-4.log` 退出 0；与普通 Editor 的完整输出字节一致，均为上述 `8A26256E...222F9`。原生请求副本保留在 `artifacts/camera-graph-native-final.request.json`。

## 下一步

相机图已获得独立连续帧与稳定分支证据，可以继续接真实骨骼 socket/pivot、平台/瞬移、第一人称与碰撞输出。普通 Demo 目前仍使用旧 AlsOrbitCamera；显示相机阻尼不能改变鼠标控制 yaw 和 WASD 方向。

富曲线仍来自 T3D 的有限精度键值，这次是容差内一致，不是 bit exact。完整 Camera、Mantle、静态物理稳定性剩余 3 项、Flail 稳定性和最终十分钟性能/视觉验收仍未完成；头颈与道具物理继续按用户要求暂缓。

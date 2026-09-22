# Ragdoll 逐步驱动输入组装

在主目录 main 增加 `AlsRagdollMotorInputs`，把资产原始约束帧、已提交动画局部姿态、独立的已提交关节目标和骨盆速度组装为 `AlsIslandAngularDrive`。保持资产 joint index，输出有序部分更新，完全停用电机的根连接不输出。调用者须保留同样的 solver joint 顺序，提供完整已提交目标；本类不读取骨骼显示结果或推进物理状态。

按本地 ALS `AlsCharacter_Actions.cpp` 的 RefreshRagdolling 实现厘米每秒速度长度 / 1000 → float Clamp01 → float × 25000。运行时阻尼为零、无限扭矩。按原生启用标志分别屏蔽 twist/swing 刚度与阻尼；无姿态驱动或父链跳过时保留调用者目标。构造时拒绝 SLERP 和非零目标角速度。缩放参数显式传入，不硬编码所有引擎都使用 1.5。

源码复核 `Engine/Private/PhysicsEngine/Experimental/PhysInterface_Chaos.cpp:487` 发现缩放发生在 float 参数转入 Chaos FVec3 后；组装器已使用 double 乘法，小数刚度测试证明未误用 float 乘积。最初整数 golden 不足以覆盖此差异，未改 golden 或放宽阈值。

候选缓冲预分配，全部成功后才覆盖调用者输出。后部无效目标触发异常时，输出保持原样；修复输入后可重试。持有者为单线程 owner，不承诺并发/重入。

## 验证与范围

- 既有真实 Flail 五姿态 × 两模型参考现在经过输入组装器，逐关节验证目标和启用轴的 K/C。原始目标数学仍为 190 motor / 180 启用目标，最大分量差 3.33e-16。
- 六项定向测试在 .NET 8/9 通过：包括速度 0/500/1000/2000 的强度、非对称轴向屏蔽、后部失败不覆盖输出、重试、2048 次零分配、无效数值拒绝和小数缩放精度。
- Godot Optimize 最终构建通过，零警告/错误。日志在 `artifacts/physics-motor-inputs-20260922/`，最终结果使用 `*-final.log`。
- 本批未改 UE 导出器或参考资产，无新 UE 构建/启动、全量测试或物理矩阵。原生证据沿用上一批 motor targets，速度公式为源码及解析测试，尚无完整速度驱动轨迹 golden。

本批完成输入组装层，尚未添加 Godot 生产调用。下一步把已提交动画姿态转换并交接给此输入层、绑定实际物理步及初始化/退出生命周期。不得把此层对照通过称为普通 Ragdoll 完成。普通 demo Ragdoll、Get-up/Pose Recovery、Mantle、完整 Camera、十分钟性能仍未完成；最近矩阵仍 9/12（三项 30 Hz 旧失败）。用户 P4 修改保留。

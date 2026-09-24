# Mantle 起始时间基础

本批开始补齐尚未实现的 Mantle 主线。当前运行时只有 Mantling 枚举和空探测结果，本批新增 Core 起始时间算法；没有宣称 demo 已能攀爬。

依据本机 ALS `Source/ALS/Private/AlsCharacter_Actions.cpp::CalculateMantlingStartTime` 和引擎 `UnrealMathUtility.h::GetRangePct/GetMappedRangeValueClamped` 实现 `AlsMantlingStartTime`：

- 手动模式保留夹紧高度映射、逆序区间和接近点区间的阶跃行为。
- 自动模式先采样 montage 首尾绝对根骨 Z，目标为 `max(0, endZ - height)`，先判断起点是否在 1 cm 容差内，再按原生二分顺序查询。
- 搜索时间保持 float；动画采样帧率的倒数保持 double，与原生 AsDecimal 一致。高度误差不超过 1 cm 或区间不超过动画一帧时返回当前中点，不强行吸附到端点，不修造单调曲线。
- 输入采样接口要求绝对、未锁定的 montage 根骨高度；不能传 Root Motion 区间增量、重定向骨架或显示骨架。非法数值和超出 float 分辨率的搜索明确拒绝。

Release 定向测试覆盖映射/夹紧/逆序/点区间、带非零根骨起始高度的搜索及实际采样顺序、起点容差、不可达平台的停止条件、采样帧率精度和非法源。首轮 11 过/1 失败是测试将 30 Hz 的预期中点多算一次二分；按原生区间停止条件改正测试，未改算法门槛。首轮日志 `artifacts/mantle-start-time/mantle-start-time.trx` 保留。

这批没有导出新的 native 动态 oracle，也没有修改 UE 或启动 Editor。算法尚无 gameplay 调用，尚未用 Refactored 的实际 Mantle montage 验证。用户 P4 计划和三份头颈文件保留。

最终 Release 定向 14/14 通过：`artifacts/mantle-start-time/mantle-start-time-precision.trx`。新增 32 与 32.0000001 fps 案例区分 double 帧间隔和提前取 float 的错误停止边界。最终 Optimize 构建零警告、零错误；没有重跑无调用变化的 Godot 场景或全量测试。

## 后续接入顺序

1. 从实际 Refactored 角色及数据资产导出 High/Low/InAir 的设置选择、montage、warp 时间区间/混合曲线、StartTime 与高度区间；不能把 C++ 默认值当作蓝图实际配置。
2. 接 montage 绝对根骨采样及起止根变换，导出原生起始时间与 RootMotionSource 连续轨迹，核对算法和输入数据。
3. 移植前向胶囊/向下球扫、斜坡和速度过滤、目标及起始空间检查；场景查询留在 Main，结果带目标身份进入运行时。
4. 实现移动基座下的 warp、独立 source 时钟与 montage 位置同步、动作播放身份/通知、运动锁定、完成和中断；目标销毁按原生进入 Ragdoll。
5. 普通 demo 验证地面/空中、低/高台、移动平台及取消恢复，再进行完整视觉和性能验收。

相机复杂碰撞/完整视觉验收、缩放角色的物理链路、旧静态 9/12 与 Flail 0/3、最终十分钟预算等仍未完成；头颈和道具物理暂缓。

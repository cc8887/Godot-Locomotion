# Mantle 绝对根骨与 montage 时间映射

新增 Core `AlsMontageRootTransformSampler`，并为已有原始根轨道采样器开放 `SampleAbsolute`。原 `Extract` 区间 Root Motion 接口继续保留，内部复用同一绝对采样。

依据本机 `UAlsMontageUtility::ExtractRootTransformFromMontage/ExtractLastRootTransformFromMontage` 以及 `FAnimSegment::GetValidPlayRate/ConvertTrackPosToAnimPos`、`FAnimTrack::GetSegmentIndexAtTime`：

- 仅消费第一条 authored slot 的有序 sequence 段；不排序，构造时复制段定义。
- 逆序查找包含查询时间的段，保证相邻段共享端点时后段优先；查询时间先夹到非负，仅用于查找。空隙或越界时回退最后段，时间转换仍用原始查询时间。
- 有效段速率为 SequenceRate × SegmentRate，近零时按原生回退 1；保留 trim、loop 和负速率转换运算，不替换为显示姿势采样的时间算法。
- `SampleAbsolute` 返回原始根骨 TRS，保留源位移和缩放，不做参考姿态逆变换、不施加区间 Root Motion 的归一化缩放策略。
- `SampleLast` 独立实现原生的最后 sequence 在 `Segment.GetEndPos()` 处采样，未擅自改成 montage 末尾的映射采样。二者在带 trim/偏移的段上可能不同。

输入拒绝空 first slot、非法/非有限段、零长度、无效循环次数及溢出；目前绑定明确要求原始 sequence sampler，不接受未实现的 composite 动画引用。没有消费先前导出的 60 Hz 参考样本来伪造生产采样。

## 验证

Release 定向 17/17 通过：新增 5 项绝对变换/端点和空隙/速率与循环/独立 SampleLast/构造后输入隔离与非法输入测试，以及原有 12 项 Root Motion 回归（含原分配检查）。报告 `artifacts/mantle-root-sampling/mantle-root-sampling.trx`。

这些测试使用受控原始轨道检验源码规则，尚未将实际 Refactored montage/原始动画绑定到新 sampler，不能称为新的 UE 原生轨迹对照或已完成真实资产采样验收。

最终 Optimize 构建零警告、零错误。用户 P4 计划 SHA256 仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。

## 下一步与边界

从已导出的 6 个 montage 的第一 slot 读取完整段设置，绑定其实际 sequence 原始根轨道和参考根变换，用独立 616 个原生绝对根骨样本检验精度，再补非网格时间与边界原生采样。之后继续 RootMotionSource warp/时钟与 montage 同步、Main 障碍探测、移动基座、动作生命周期与普通 demo 入口。

本批无 UE 改动/启动/资产更新，无 Import 全量或 Godot 运行场景/视觉验收；Mantle 尚不可实际游玩。相机复杂场景、缩放物理、旧静态 9/12、Flail 0/3、最终性能预算仍保留。头颈和道具物理暂缓，用户 P4 计划及三份头颈诊断文件保持不变。

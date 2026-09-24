# Mantle authored 选择与设置编译

新增 Core `AlsMantlingType`（保持原生 High/Low/InAir 顺序）和 Import `AlsMantlingSettingsCompiler`。从已冻结 `refactored_mantle_inputs.json` 编译不可变设置及选择 profile，不消费 `selection` 参考表或 `rootSamples`。

编译器读取实际 `SelectMantlingSettings` 蓝图：校验入口到类型 Switch 的执行与数据连接，沿 High/InAir 返回引脚读取资产；Low 沿 GameplayTag Switch 的 PinTags/PinNames 和执行连接读取返回资产。校验双向连接、OverlayMode self 来源、原生枚举、标签比较引脚和返回引用，未编写手工 Overlay 占手分类。运行时使用精确标签选择，遵循 FName 大小写不敏感语义；子标签不会自动命中父标签，未命中时走 authored Default。

7 份设置保留实际 montage 路径、长度、RateScale、起始时间模式/区间及 warp 区间/混合类型，并接入 Core 的手动或自动起始时间算法。实际配置全部为手动模式。校验资产绑定、有限值、正向播放速率和时间范围；目前支持已导出资产使用的 Linear/HermiteCubic，未知/custom 混合明确拒绝，不能静默替换。

## 验证

Release 定向 8/8 通过，日志 `artifacts/mantle-settings/mantle-settings-final.trx`：

- 编译前删除 selection 和 rootSamples，再逐值对比独立原生 39 例，全部一致。
- 7 个实际设置分别检验低于/高于参考区间和中点的起始时间，确认手动模式没有访问根骨采样回调。
- 保持 oracle 不变，只改变蓝图 High 返回引用，编译结果相应改变，证明生产数据来自 authored 图。
- 非法 warp 区间、缺失 montage、未知混合、标签引脚不一致、断开的入口执行连接均被拒绝。
- 另检查标签大小写及未知子标签的 Default 回退；这些边界依据标签节点/FName 语义，不称为新 UE 原生样本。

首轮 7 项通过后补充执行边界与标签比较检查，最终 8 项通过；未修改任何原生参考值或放宽门槛。

最终 Optimize 项目构建通过，零警告、零错误。冻结 JSON SHA256 仍为 `94B9618B08623A380028C53D84F08CD6C5BD700F3B073DCD5F776424D7484165`；用户 P4 计划 SHA256 仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。

## 接入边界

这批没有普通 gameplay 调用，没有生产 montage 任意时间绝对根骨采样，也没有编译通用 Mantle 探测设置。profile 不能被当成完整动作播放器；montage 通知、source 时钟/warp、移动基座、障碍查询和动作开始/结束/中断仍需继续接入。

无新 UE 修改/启动/导出，也没有 Godot 场景或视觉验收、Core/Import 全量重跑。冻结 Mantle 参考保持不变。其他目标（相机复杂场景、缩放物理、旧静态 9/12 与 Flail 0/3、最终性能预算）仍未完成；头颈与道具物理暂缓。用户 P4 计划及三份头颈文件保留。

# Mantle 原生设置、选择与根骨参考

新增只读 `tools/unreal/export_mantle_inputs.py`，从实际 Refactored 资产读取 7 份 Mantle 设置、6 个 montage、原始 SelectMantlingSettings 蓝图和角色通用设置。临时角色直接设置 OverlayMode 后调用原生蓝图选择函数，覆盖 13 个原生 Overlay × High/Low/InAir 共 39 例；这是选择结果，不是能否开始攀爬的 gameplay 判断。

## 实际配置

7 份设置均为 `autoCalculateStartTime=false`，实际先使用高度区间映射：

| 设置 | 参考高度 cm | 起始时间 s | Warp 区间 s |
|---|---|---|---|
| High | 125–200 | 0.6–0 | 0.3–0.6 |
| High InAir | 125–200 | 0.8–0 | 0.3–0.6 |
| 五种 Low | 50–100 | 0.5–0 | 0.3–0.7 |

表格为便于阅读的十进制，JSON 保留反射得到的 float 原值。位置混合均 Linear，旋转混合均 HermiteCubic；原始配置及 montage 原文一并保存，不用默认值替代省略的后续配置解析。

Low 选择：Default/Masculine/Feminine→Low；Injured/Bow/Torch/Barrel→Left；Rifle/PistolOneHanded/PistolTwoHanded/Binoculars→Right；HandsTied→BothHands；Box→Box。High 与 InAir 选择不同设置但共用 High montage。

通过真实 `UAlsMontageUtility::ExtractRootTransformFromMontage`，按 60 Hz 时间采样并包含终点，共 616 个绝对根骨 TRS。传入时间先量化为 float 并记录同一值；检查有限值。High 为 161 个样本、时长约 2.6667 s，五个 Low 各 91 个样本、时长 1.5 s；终点 Z 分别约 200 cm 和 100 cm。

这些是原生 montage 根骨参考，不是 Core 重算结果，也不是 RootMotionSource 或角色移动轨迹。不能将固定 60 Hz 参考直接冒充完整任意时间生产采样器。

## 验证与问题记录

完整 Editor 目标构建及全部适用项目插件审计通过，0 个构建 action；fingerprint `5507EC2FCBD1D34CCE8C479248800626CB6EFA1E0DE171EF91A041FCBDA81BC6`。日志前缀为 UE `Saved/Logs/PluginBuild/20260924T122935943Z-f7230eb0acee40309945ad227f3d24a3`。没有修改原生插件源码、配置或二进制，也没有保存 UE 资产。

首轮未同步扫描注册表，未找到资产；第二轮 Vector2f 没有直接 x/y 属性，改为 get_editor_property；扩展 Overlay 首轮误用 Python wrapper 相等比较，改为反射回读 TagName 并与请求值比较。分别保留 `artifacts/mantle-inputs-first.log`、`second.log`、`all-overlays.log`。默认三例的中间成功数据为 third.json，不作为最终 39 例数据。

最终导出 `artifacts/mantle-inputs-final.json`，命令行退出 0，汇总 0 error/0 warning；核验 7 设置、6 montage、39 选择、1 原始图，以及全部根骨采样首尾时间。没有修改 C#，没有重跑 Core/Import/Godot 场景或普通 Editor、DataValidation。

独立冷启动复导 `artifacts/mantle-inputs-repeat.json` 同样退出 0、零错误/警告，两次字节完全相同，原 CRLF 文件 SHA256 为 `04C830E00B730E9F7AB250C3C2213F3D38F9C7B9EF74996EC487550E3DA8A7E4`。提交检查发现 CRLF 与仓库 JSON 规则冲突，导出器改为显式 LF，并将同一数据机械规范化为 LF，未改变 JSON 值。冻结文件 `assets/config/refactored_mantle_inputs.json` 为 329909 字节，SHA256 `94B9618B08623A380028C53D84F08CD6C5BD700F3B073DCD5F776424D7484165`。

## 后续

显式 LF 导出器再次冷启动运行退出 0、零错误/警告，`artifacts/mantle-inputs-lf.json` 与冻结文件 SHA256 相同；最终 `git diff --cached --check` 通过。

继续编译实际设置及 authored 选择结构，用独立 39 例验证选择行为；接 montage 原始根骨采样及原生起始时间/RootMotionSource 连续对照。然后接 Main 障碍探测、移动基座 warp、source 时钟与 montage 同步、动作身份/通知、结束中断和目标销毁。Mantle 尚无普通 demo 运行入口。

相机复杂场景/视觉验收、缩放角色物理链路、旧静态 9/12、Flail 0/3 和最终十分钟预算仍待完成；头颈/道具物理暂缓。用户 P4 计划及三份头颈诊断文件保持不变。

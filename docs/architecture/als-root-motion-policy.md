# ALS 根运动开关与离线轨迹曲线

普通 ALS 使用 `assets/config/als_animation_settings.json` 的 `enableRootMotion`，默认 `false`。文件位于版本包之外，切换策略不修改动画内容和包哈希；进程启动时解析一次，修改后重启 Demo。`schemaVersion` 当前为 1。

```json
{"schemaVersion":1,"enableRootMotion":false}
```

命令行 `-- --enable-root-motion` 或 `-- --disable-root-motion` 可覆盖文件，两者同时传入报错。策略作用于 ALS；Lyra 保持自己的运动机制。原生对照诊断入口默认保留作者采样，以免把既有 native 夹具解释成关闭根运动的新结果；验证关闭策略时须显式传入 `--disable-root-motion`。普通 `als_demo.tscn` 入口直接遵循配置文件。

## 运行契约

关闭时，float/double 原始姿态采样器直接用根骨骼参考变换，跳过动画 root 轨道，之后再构建组件空间和 virtual bones。目标适配后仍使用目标参考 root；作者 ForceRootLock/首帧 root lock 不会恢复动画位移。骨盆和四肢继续按相同时刻采样，加法参考遵循相同策略。Core、Import 原生读者的可选参数默认保持 `true`，Godot 普通 ALS 显式传入策略。

关闭时不创建 Montage 根轨迹采样库，不提取或提交根运动增量；事件、通知、播放时间和角色提交/取消事务仍使用既有时钟。普通行走由输入/物理 motor 移动。动作玩法状态仍启用：翻滚的蹲伏/朝向和硬落地自动 Ragdoll 不依赖根运动开关；翻滚动画产生的根位移须开启策略。翻越关闭时不读取根轨迹或由其寻找起始高度，使用既有高度参考时间映射、平台目标和 correction/location/rotation 插值完成程序化移动；开启时恢复根轨迹驱动的原路径。

普通 Demo 的完整链在两种根策略下均启用 `--rolling-gameplay`，开启根策略时额外启用 `--montage-root-motion`；显式 `--action-preview` 仍表达动画预览意图。配置不是运行中热切换，不另建角色时钟、独立求值或最终写骨掩盖路径。

## 生产契约

生产图按依赖完成 master、镜像/反向/参数派生、原资产身份打包，然后原生重定向到目标 rig。`publish stage` 在最终 root/IK 分解及采样接口确定后，必须调用 `root_curves.export`，再登记文件并写版本包。曲线读取尚未受运行策略或 root lock 影响的最终目标 raw；运行关闭根运动仍完整导出。SOMA BVH 外层静态 Root 不作为目标轨迹来源。

每个存在平移或旋转的目标 root，或原合同声明启用 root motion 的资产，单独产出：

```text
motion_curves/<edition>/<OriginalAssetName>.root-motion.json
```

保留原动画名；V4/Refactored 按目录隔离。纯原地、未声明根运动的静态 root 不伪造距离。已转换为原地的步态若需生成阶段的行进距离，应另外保留 master 轨迹，不能将静态最终 root 文件当作移动曲线。

目标水平分解现先生成 authored-root，再生成 ALS runtime 视图。`extractedRootMotionCurves` 保存锁定 root XY 之前的真实轨迹，`rootMotionCurves` 仍绑定最终 runtime raw；作者阶段已经移除的行进路径另存 `authoringTrajectories`。每份姿态都有哈希和重建回执，最终 FBX 经过原生重新烘焙／回读。详见[水平运动所有权](kimodo-planar-root-ownership.md)。

曲线文件加入 `resource-pack.json.rootMotionCurves` 及 `files` 的 SHA256 清单，普通加载器验证其字节。激活必须具有曲线索引，索引内每个文件必须属于包 payload。导出拒绝在同一版本路径覆盖不同输入；用新版本重新生产。

## 曲线语义

schema 1 保留最终目标的采样帧数、播放长度、frameRate 分子/分母和 interpolation。时间为原采样键的 `timeSeconds`；不循环、不换 FPS、不根据播放速率重计时。坐标为原生目标 root 局部轴，XY 地面、Z 向上，位置及距离以厘米计；不是 Godot 米/Y-up。

| 每键通道 | 含义 |
| --- | --- |
| rootDisplacementX/Y/ZCm | 相对首帧的位移 |
| relativeRootQuaternionX/Y/Z/W | 相对首帧 root 旋转 |
| rootYawDegrees | 根据 root 朝向投影解包的累计 yaw |
| cumulativePlanarDistanceCm | XY 相邻采样键路程累计，含折返 |
| remainingPlanarDistanceCm | 总平面路程减累计路程 |
| signedDistanceToEndCm | 累计路程减总路程，从负值增至零 |
| cumulativeSpatialDistanceCm | XYZ 三维路程累计 |
| cumulativeRotationDegrees | 相邻旋转最短弧角累计 |

旋转动作可有零距离和非零角度。纯竖直翻越的距离参考应取三维或 Z 通道。`signedDistanceToEndCm` 表示剩余轨迹路程的负值，不是到目标点的带符号欧氏距离。原地段可能出现重复距离键；后续 Distance Matching 的逆查找需要明确处理这些区间。

文件同时记录 BVH 字节哈希、原生 raw 字节哈希和完整最终 raw 的规范 JSON 语义哈希。哈希用于追溯对应采样姿态，独立于运行开关。当前提供可用于迁移的轨迹数据，尚未实现 Distance Matching 播放控制、距离反查或状态切换算法。

已发布包可用 `publish bake-root-curves` 补齐曲线：先验证既有全部 payload 和原生回执，读取最终 raw（未在运行 bank 使用的 V4 身份从同一原生回执适配），写独立文件并原子更新包清单。不会重写动画和配置资源字节。入口与命令见 [生产脚本说明](../../tools/kimodo_pipeline/README.md)，本批实测见 [验证记录](../verification/2026-10-07-als-root-motion-policy-and-distance-curves.md)。

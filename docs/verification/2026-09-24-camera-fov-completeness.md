# 原生组件核对：FovOffset 消费补齐

主目录 `.`，分支 `main`。

检查本地 `../AdvancedLocomotionSystemV/Plugins/ALS/Source/ALSCamera/Private/AlsCameraComponent.cpp` 的 TickCamera 和 CalculateFovOffset，发现组合运行时遗漏了相机图 FovOffset 曲线：Core Follow 已支持 FOV 偏移，但 Import Runtime 没有把该曲线传给它，普通宿主的外部偏移又固定为零。

## 修改

组合运行时把候选图的 FovOffset 加到已有宿主偏移后交给 Follow。保留原生计算顺序：第一/第三人称混合 → 显式 FOV 覆盖 → 偏移 → 5..175 限制。全第一人称提前返回仍直接使用第一人称或覆盖 FOV，跳过偏移与末端限制。普通宿主外部偏移仍为零。

原始 3114 帧相机图参考中 FovOffset 存在次数为零。因此这是移植完整性缺项，不是当前 Demo 某个非零原始曲线被错误表现的证据。未修改原始资产或编造默认 FOV 曲线。

## 验证

七个显式夹具用已有 ModifyCurve 节点包裹原始图，提供非零 FovOffset 和部分/完整 FirstPersonOverride；测试不同第一/第三人称 FOV、显式覆盖、正负极值限制，以及 Discard/重试/Commit。

- 修复前：五项失败、两项全第一人称通过；`artifacts/camera-fov-tests/camera-fov-before.trx`。
- 修复后：全部相机 Import 专项 40 项通过；`artifacts/camera-fov-tests/camera-fov-after.trx`，其中包含既有原生图/旋转参考回放。
- Optimize 构建：0 警告、0 错误。
- 普通入口 Parallel60 八秒回归通过：480 相机提交，换肩、人称切换、Ragdoll/起身及 control yaw 保持；`artifacts/native-camera-fov-regression.log`。无新渲染截图。

## 原生位置对照仍待完成

本批只读核对 UE 源码，没有新建或运行组件轨迹导出，不宣称已获得位置 oracle。读取了 UE 构建/加载技能；由于没有修改或启动 UE，本批未执行新的 UE 构建事务。

后续导出应调用真实组件 TickComponent/完成动画评价路径，记录实际曲线、插槽/mesh/base/view 输入和内部位置历史；先覆盖无碰撞连续旋转/移动/瞬移、FP 混合与 FOV，再加入已知几何的 sweep/穿透以及动态基座。对照时区分观测的原生曲线驱动位置层与 Godot 自身图驱动完整链，不能把抄写公式的导出程序当原生组件 oracle。

当前完整 Camera、Mantle、旧物理稳定性和最终十分钟性能仍未完成；头颈和道具物理暂缓，用户修改保留。

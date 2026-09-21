# 物理世界包围盒筛选

在主目录 `.`、`main` 实现。本批修复实验 Core 后端漏掉 whole-particle 粗碰撞筛选的问题；普通 demo 尚未切换。完整 ALS 目标未完成。

## 实现及依据

对照本机 UE 源码 `Chaos/GeometryParticles.h`、`PBDRigidsEvolutionGBF.cpp`、`ShapeInstance.cpp`、`SpatialAccelerationBroadPhase.h` 与 ChaosCore `AABB`：

- 每步先计算所有形状的预测世界姿态，再将各形状的世界包围盒合并到所属身体。包括注册但 simulation filter 不参与接触的形状。
- 原生球使用中心和半径，胶囊使用变换后的段端点加半径；盒和凸包变换局部包围盒的八个角。真实资产凸包使用既有 runtime observation 的 wrapper bounds，未重新推测源顶点范围。
- 动态身体先对称扩张 detector 的 3 cm，再沿本帧积分后速度的反向扩张，各轴最多 3 cm。采用 native float dt 边界。静态与非 CCD kinematic 不扩张。
- 该速度与 narrow-phase cull 使用的 PreV 分开保存。包围盒相接算相交。
- `PrepareBounds` / `AllowsPair` 放在 manifold restore 之前。提交的缺席步骤使旧几何和摩擦历史在重入时失效；失败尝试保留已提交历史。诊断 trace 转发新接口。

本批没有新增 UE 独立 bounds 导出或声称包围盒逐位等价。胶囊仍沿用现有 float endpoint0/axis/height 重建端点，原生记录端点的末位差异仍需独立参考验证。未实现 CCD/MACD；非原生场景形状可用保守 proxy AABB。当前仍为固定 registry 的成对遍历，并非空间树加速。

## 实际连续轨迹

相同初态、30 Hz 普通落地，与既有原生完整世界参考比较。捕获实际生产求解路径，不向 Core 注入 UE 求解输入。

| 模型与完成步 | 上批最大线速度差 cm/s | 本批 cm/s |
| --- | ---: | ---: |
| AnimMan 19 | 37.524195 | 0.000872492 |
| Mannequin 19 | 15.218464 | 0.003156571 |
| AnimMan 20 | 47.116824 | 0.000718844 |
| Mannequin 20 | 12.802465 | 0.001621142 |

扩展捕获两角色各 60 步：AnimMan 第 40 步差 0.000565776，第 50 步 0.01711185，第 52 步首次超过 0.1（0.15367254）；Mannequin 第 40 步 0.00545318，第 50 步 0.04206581，第 51 步首次超过 0.1（0.10173468）。第 60 步分别为 0.82742946 / 0.24361746。0.1 只是本次定位渐进分歧的观察门槛，不是修改验收阈值或宣称之前逐位一致。

## 验证与回归

- Godot 优化构建 0 warning / 0 error。
- Core Release 固定 JIT 串行 2851 通过，沿用既有两个排除类；新增 bounds / 生命周期 7 项 LatestMajor roll-forward 通过。
- 30/60/120 Hz 实际接触 smoke 全过；60 Hz 场景接触 smoke 通过。新增动态/静态扩张、反向速度 sweep、cull 命中但粗碰撞不相交的拒绝检查。
- 两项旧 smoke 原先要求只有 PreV 扩大 cull、身体包围盒仍分离时也生成接触，新增粗碰撞后预期不再成立。现分别检查拒绝，以及给正确方向的积分速度后重新进入；未关闭测试或扩大阈值。首轮失败日志保留。
- 十二矩阵 **8/12**，不能沿用上批 9/12：高速 30、全部 60、高速/平移/旋转 120 通过；普通/平移/旋转 30 仍失败，普通 120 新增 Mannequin 休眠失败。
- 普通 30：AnimMan142 帧睡，Mannequin未睡，末秒 V6.84947 cm/s / W0.383241 rad/s。普通120：AnimMan687 帧睡，Mannequin未睡，末秒 V2.22707 / W0.269835；失败发生于十秒休眠预算，非崩溃。
- 本批没有修改 Import、原生 exporter 或 UE 插件，也未重跑 Import 全量或 UE 构建/重启。旧 Editor 退出 AV 和两条 Condition failed 未修复。

证据目录 `artifacts/physics-bounds-20260922/`。`capture/` 是 0..19 步，`extended/` 是 0..59；`differences.json`、`extended-differences.json` 包含输入 SHA256。矩阵日志为各 mode-hz.log，最终 smoke 是 `smoke-verified-*`。失败验收不生成成功报告，不能把缺失 JSON 当未执行。

下一步优先定位普通120休眠回归，并检查第 40–60 步几何末位、持久缓存及完整 native midphase 生命周期，补包围盒独立参考。保留三个30 Hz失败；之后继续普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能预算，不提前切换普通入口。

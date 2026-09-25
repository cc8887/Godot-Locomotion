# 大平面接触选面修复

本批直接在 `${env:GODOT_ALS_ROOT}` 的 `main` 完成。普通角色尚未切换物理后端；本批修复的是 Core 物理验证场景使用的 Godot 几何查询。
运行引擎为 Godot 4.7.2 Mono `ed1daf0bf`。全部产物保留在 `artifacts/physics-contact-trace-20260921/`。

## 已复现的原因

此前实际 World 普通 120 Hz 落地的最大锚点误差为 6.708990 cm，发生于 AnimMan `foot_l` 第 54 帧。
新增可选 `--trace-frames=50:55 --trace-bones=foot_l,calf_l`，记录求解前的实际接触点、法向、形状世界姿态和有符号间距；不更改接触结果。

第 53 帧，左脚凸包对 StartFloor 的查询选中了地板底面：地板顶面 native Z 为 -43.398404 cm，底面为 -93.398404 cm；脚部接触点约 -31.9 cm。
引擎返回的法向 Z 接近 -1、间距约 -61.5 cm，导致随后向下的大幅修正。这不同于前批已修复的“由分离接触点差计算法向”问题：本次 `GetRestInfo` 自身也返回了错误朝向。

独立探针使用实际编译资产 `foot_l` 凸包、第 53 帧求解前形状姿态及真实地板尺寸 83.17676 × 0.5 × 63.723648 m。
保留场景默认 margin 0.04 m 后稳定复现：正序法向接近 -1、地板选面误差 50 cm，反序在该姿态下正常。
首轮误把 margin 设成零的 `foot-red.*` 实际通过，不能作为红测；有效红测为 `foot-red-margin.log`。
仅统一查询次序的候选在旋转、大坐标变体及整链后续帧仍失败，`contact-verified-30.log` 和 `order-*` 保留该失败，不作为最终验证。

已核对当前引擎版本的源码：Godot 碰撞点和 rest-info 是独立查询，Jolt 凸体路径使用 GJK/EPA；不能假定两个接口对大扁盒子的选面总是可靠。
源码位置：[Godot 查询封装](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/jolt_physics/spaces/jolt_physics_direct_space_state_3d.cpp)、[Jolt 凸体路径](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/thirdparty/jolt_physics/Jolt/Physics/Collision/Shape/ConvexShape.cpp)、[Jolt 平面路径](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/thirdparty/jolt_physics/Jolt/Physics/Collision/Shape/PlaneShape.cpp)。本批定位到查询结果错误，未宣称定位或修复 Jolt 内部具体算法缺陷。

## 修复范围

凸包对盒子统一以凸包作为查询形状，结果转换回原身体次序和 shape-local 法向。
保留原来的有限 AABB 排除和 double 相对原点转换。
在盒子局部坐标中检查凸包的保守包围盒：只有整个包围盒在两个切向轴都远离边缘，且余量超过该面的穿透退出距离、盒子 margin 和 1 mm 保护量，才使用该面的局部半空间查询。
这时局部碰撞区域是盒子的平面内部；用同一盒子变换下的 `WorldBoundaryShape3D` 查询实际接触点和法向。
不修改场景资源，不把整个有限盒子替换为无限地面。边角、边界外和其他形状继续走原路径。
查询适配器持有并释放自己的平面资源；仍是 Main 线程查询，未宣称零分配或完成性能预算。

没有改变 Core 关节/接触公式、迭代次数、重力、材质或原生睡眠阈值。

## 最终验证

Godot 优化构建通过。新增六项实际脚部接触检查：原姿态、旋转、旋转加大世界平移，各自覆盖两个查询次序。
地板选面误差最大 0.0000152588 cm，法向均指向脚部。
另加盒子六面、边角不替换、有限范围外无接触，共八项检查。
连同原有检查，`contact-final-{30,60,120}.json/log` 各通过 31 项接触精度、6 项几何、5 项休眠生命周期及 3 个动态场景；双动态总动量误差为零。
`scene-contact-{30,60,120}.json/log` 各通过真实 World 13 个身体/形状、13 项几何、9 项生命周期及地面/平移/旋转三场景。

两模型完整链普通及高速落地，实际 World 和旧厚地板共十二项均通过十秒探针，全部自然休眠并保持；末秒线/角速度均为零。

| 地面 | 模式 | 30 Hz 最大锚点 cm | 60 Hz | 120 Hz |
| --- | --- | ---: | ---: | ---: |
| 实际 World | 普通 | 1.484637 | 0.957243 | 0.376757 |
| 实际 World | 高速 | 4.505715 | 1.739571 | 0.914908 |
| 旧厚地板 | 普通 | 1.484638 | 0.957236 | 0.376761 |
| 旧厚地板 | 高速 | 4.505092 | 1.73957 | 0.914908 |

实际 World 普通 120 Hz 对应 `face-foot-chain.json/log`，其余为 `scene-normal-*`、`scene-high-*`、`floor-normal-*`、`floor-high-*`。
修复前后的同场景最大锚点由 6.708990 cm 降至 0.376757 cm；第 53–55 帧左脚接触改为地板顶面、法向 +Z，错误的约 61 cm 穿透消失。
最大世界代理回写误差在上述实际场景回归中不超过 1.47e-6 m。
这些是数值回放检查，不代替普通角色视觉验收或原生整链轨迹对照。

本批未修改 Core/Import，未重跑其全量测试；不把前批全量结果记作本批的新运行。

## 仍未完成

完整链平台 `face-platform-{translate,rotate}-{30,60,120}` 仍只有 120 Hz 的平移、旋转两项通过。
最大水平中心滞后分别为 0.008858 / 0.001523 m；停止后两模型重新自然休眠。
30/60 Hz 四项仍在平台静止十秒、尚未运动阶段失败，日志保留，没有成功 JSON。
60 Hz 右上臂/手的逐帧查询法向朝上、接触深度约 0.22–0.30 cm；本批左脚选面修复没有解决其角速度持续超过原生休眠阈值的问题。
不能把这一失败简单归因于同一选面问题，也没有通过放宽门槛或延长等待绕过。

下一步继续定位横卧姿态下接触与关节共同迭代的右臂残余运动，必要时补原生整链对照。
随后将已验证的动画姿态 seed/capture 接到普通角色物理 owner、pelvis/胶囊/相机跟随，并完成退出、Get-up/Pose Recovery。
flail joint motor、初始速度限制、环境材质组合、接触图休眠、CCD、Mantle、完整 Camera 及最终十分钟性能预算仍未完成。

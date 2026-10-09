# Kimodo Pipeline Viewer 固定原点对照

本批范围为 `tools/motion-viewer`。所有单栏、A/B、多阶段视口改为共享原点、正交相机及 cm 比例尺，修复 root 绑定别名丢失标记/轨迹、只展示较大轨迹、从属栏渲染后才更新时间、同步开关丢失 master 的问题。最终 FBX/raw 目录读取实际活动包。设计见 [原点对照契约](../architecture/kimodo-viewer-comparison-space.md)。

## 实测

真实浏览器打开本地 10015 服务，选择 `ALS_N_Walk_F`，暂停并回到首帧。五栏可同屏显示；RootIK 无此资源，明确显示空缺。下载实际显示数据，四个已加载栏的相机位置均为 `(400,250,450)`、zoom=1，时刻均为 0。

| 栏 | root 到原点 cm | pelvis 到 root 水平距离 cm |
| --- | ---: | ---: |
| SOMA BVH（旧中间产物） | 0 | 9.555 |
| Manny FBX（旧中间产物） | 0 | 7.820 |
| 当前活动 v8 的 V4 Walk FBX | 0 | 148.611 |
| 原版 V4 Walk FBX | 0 | 3.542 |

当前 v8 的水平常量在同原点画面中可见且标红，原版仍靠近原点；上述不是 Refactored 原始资产或 Godot 最终 Rig 的新验收。

![五栏同原点首帧对照](../../artifacts/tests/2026-10-08-origin-visibility-v1/walk-fixed-origin-verified.jpg)

[最终首帧显示报告](../../artifacts/tests/2026-10-08-origin-visibility-v1/walk-origin-verified.json)。拖动任一视口相机、关闭再开启同步、拖到末端后，四栏真实相机状态逐字一致，归一化时间均约 0.999375（滑杆 0.001 s 步长），见 [联动末端报告](../../artifacts/tests/2026-10-08-origin-visibility-v1/walk-end-orbit-report.json)。重置视角及回到首帧正常。

最终复测发现旧异步加载会在快速切换资源时覆盖新骨架，造成 Walk 标签与其他动作画面不一致。保留 `walk-fixed-origin-final.jpg` 等失败截图；增加视口加载代次及链路请求代次校验，切换时清空旧模型，取消过期加载。重新加载页面，在默认动作尚未加载完成时切换到 Walk，等待四栏就绪、暂停、回首帧后，确认实际文件路径和上述坐标一致。最终截图和报告使用 `verified` 文件名，不覆盖失败证据。加载期间禁用播放、拖动时间与导出，避免部分链路报告。

## 检查与边界

三个几何回归通过，验证 150 cm 恒定偏移不被 `max-min` 掩盖、root 行进与身体分离分开计量、别名/缺少 root 的语义。服务回归通过，验证活动指针、越界指针拒绝、无指针原版路径。日志在 [证据目录](../../artifacts/tests/2026-10-08-origin-visibility-v1/)。JS 语法及 Python 编译检查通过。

首次服务测试把 Windows 短路径直接 mock 成根，未按真实 server 初始化调用 realpath，因此失败；修正测试夹具为真实路径解析后通过，没有放宽路径检查。服务重启初期浏览器遭遇 connection refused，重新建立标签页后成功，旧服务日志保留。

本批未生成/重写任何 BVH/FBX/raw JSON、未修改活动 v8 指针、未变更 ALS 运行代码、未执行新的 Godot E2E、UE 预览或全资产视觉矩阵。工具显示改进不代表 v8 动作质量通过；真实 DAG 中间版本关联及支撑相位对齐尚未接入。

# Motion Viewer 跨批次与真实阶段链路（2026-10-09）

## 范围与原因

链路原来把选定生成批次的源 NPZ 与历史同名 BVH/FBX 并列，后者不具备该批依赖回执。最新密集周期试验源门禁失败，没有目标重定向、水平分解或最终 FBX；原生候选 Hips 的行进轨迹不能证明目标分解失效。活动包仍为存在已知骨盆偏移问题的 v8。

实现两种明确选择：当前批次各阶段，以及两个批次的同一阶段。当前批次六阶段为源生成、周期母动作、目标重定向 raw、水平分解 Root、ALS 原地 raw、最终 FBX；缺失阶段显示“本批未产出此阶段”。活动包与原版标为独立对照，历史中间目录不再填充当前批次空阶段。切换保留链路模式/资产/相机，暂停在首帧；重新创建列时释放旧渲染器，防止跨模式积累 WebGL 上下文。

新增 `viewer_pack.py` 验证真实目标输入/ownership/raw/最终 FBX 摘要并登记；`publish stage/finalize-fbx` 调用该入口。只读预览保存完整局部平移/旋转/缩放、真实 root、Z-up/cm 和原始时间。源批次和独立目标包没有同一回执时不拼成一批。

## 实际资产对照

登记 `als_nonprop_20261008_root_ownership_v5-a03b1dc1ea46` 的 88 个 V4 非道具身份，每个 3 份目标 raw +1 份最终 FBX，共 352 个制品；此包仍非活动包。源批次保留此前两批的 16 份预览，最新注册指针仍选择密集周期失败候选。

| 动作 | 重定向 raw 的 pelvis→root 最大距离 | 分解后 pelvis→root 最大距离 | authored root→原点最大距离 |
| --- | ---: | ---: | ---: |
| ALS_N_Walk_F | 150.81 cm | 3.94 cm | 0 cm |
| ALS_N_Run_LF | 351.83 cm | 2.33 cm | 0 cm |
| ALS_N_LandRoll_F | 68.02 cm | 6.00 cm | 61.44 cm |

Walk/Run 的目标输入已是原地动作，分解消除远离原点的常量，并保留自然重心残差；不能凭空创建行进 root。翻滚保存实际整体轨迹，骨盆残差保留。普通原地视图的 root 固定，authored 轨迹按原生产合同另存。这些仅证明旧 v5 分解制品的所有权变化，不证明最新侧跑源已处理，更不认证动作语义/接触质量。

## 验证

- Pipeline 全量 136 项通过：[日志](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/viewer-chain-pipeline-tests-20261009.log)。其中临时检出测试曾触发可见性诊断，不影响测试结果；后续加入不登记项目外临时包的边界，publish/target 18 项复跑通过：[最终日志](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/viewer-chain-publish-tests-final-20261009.log)。
- 真实浏览器首轮发现参考姿态表 79 条含虚拟尾部，但物理 raw 只有 68 骨，预览 offsets 维度错误。修正为物理 raw 骨数，并添加回归；目标预览 3 项复跑通过，包括完整 FK/真实 root/原地残差与错误采样时域拒绝。
- Viewer 服务器 3 项复跑通过，新增断言验证跨批次来源分别正确、本批缺失 Root 阶段不回退历史目录；Node 原点 3 项通过，修改后的 JS 语法检查通过。
- HTTP 验证 352 个目标制品字节哈希；两批 LF 源 NPZ 摘要不同；失败源批次无 Root 分解制品；活动包 v8 摘要保持：[证据](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/viewer-chain-api-20261009.json)。
- 浏览器实际加载两批 LF 同阶段及 v5 的重定向/分解/原地/final FBX，首帧 raw 与最终 FBX 水平距离一致；旧活动 v8 保留约 349.8 cm 偏移。
- 带缺失阶段的链路首轮播放按钮把空视口默认 playing 状态纳入判断，点击播放仍停在首帧；改为只检查有 action 的已加载视口，实际翻滚运动中确认 raw/最终 FBX 的 root 均随播放移动。时间同步仍按各自时长比例，不宣称同源键逐帧对齐。
- [跨批次 LF 截图](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/viewer-cross-batch-lf-20261009.jpg)、[目标 LF 分解截图](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/viewer-target-lf-root-chain-20261009.jpg)、[翻滚 Root 轨迹截图](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/viewer-target-roll-root-chain-20261009.jpg)。原维度错误截图保留为 `viewer-target-lf-root-chain-initial-failure-20261009.jpg`。

所有截图属于 Motion Viewer 独立播放；本次未执行新生成/重定向/Godot E2E，未激活包、提交或推送。LF 方向、稳态循环与支撑质量的重新生成仍开放。

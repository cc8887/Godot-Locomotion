# ALS 逐资产 Kimodo 约束清单

由 `tools/unreal/build_als_kimodo_asset_contracts.py` 从当前原资源/图引用导出；设计规格，未生成新动作。
配合 [生产设计](kimodo-als-resource-production.md) 和 [完整 JSON](kimodo-als-asset-contracts.json) 阅读。
提示词是候选草稿；完整 JSON 保存每项约束、后处理、验收和 source/hash 证据。`{stance}` 必须按 frame 状态替换，不能直接发送。

## v4 资源

| 资产 | 作用/生成方法 | 原关键帧/时长 | 原加法参考 | 提示词草稿 |
| --- | --- | --- | --- | --- |
| `ALS_N_LocoDetail_Accel_B` | 加速度驱动的身体动态细节 / `acceleration_additive` | 31 / 1s | AAT_LocalSpaceBase；ALS_N_Run_BasePose frame 0 | A person adjusts their running posture while accelerating backward. |
| `ALS_N_LocoDetail_Accel_F` | 加速度驱动的身体动态细节 / `acceleration_additive` | 31 / 1s | AAT_LocalSpaceBase；ALS_N_Run_BasePose frame 0 | A person adjusts their running posture while accelerating forward. |
| `ALS_N_LocoDetail_Accel_L` | 加速度驱动的身体动态细节 / `acceleration_additive` | 31 / 1s | AAT_LocalSpaceBase；ALS_N_Run_BasePose frame 0 | A person adjusts their running posture while accelerating left. |
| `ALS_N_LocoDetail_Accel_R` | 加速度驱动的身体动态细节 / `acceleration_additive` | 31 / 1s | AAT_LocalSpaceBase；ALS_N_Run_BasePose frame 0 | A person adjusts their running posture while accelerating right. |
| `ALS_CLF_Pose` | 固定基底、站蹲/Lean 零参考 / `base_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a stable crouching pose with both feet planted. |
| `ALS_N_Pose` | 固定基底、站蹲/Lean 零参考 / `base_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a stable balanced standing pose with relaxed arms. |
| `ALS_N_Run_BasePose` | 固定基底、站蹲/Lean 零参考 / `base_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a balanced running reference pose without translating. |
| `ALS_N_FallLoop` | 正常/快速下落循环 / `fall` | 31 / 1s | AAT_None；无 | A person falls through the air with a relaxed bent-knee posture. |
| `ALS_N_FallLoop_Fast` | 正常/快速下落循环 / `fall` | 31 / 1s | AAT_None；无 | A person falls through the air with a spread protective posture. |
| `ALS_Flail` | ragdoll 空中摆动/恢复辅助 / `flail` | 21 / 0.666667s | AAT_None；无 | A person falling through the air moves their arms and legs in a controlled flailing motion. |
| `ALS_N_Stop_L_Down` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；ALS_N_Pose frame 0 | A person walking forward comes to a balanced stop with the left foot in the designated support phase. |
| `ALS_N_Stop_R_Down` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；ALS_N_Pose frame 0 | A person walking forward comes to a balanced stop with the right foot in the designated support phase. |
| `ALS_N_Transition_L` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；ALS_N_Pose frame 0 | A person standing makes a small corrective step with the left foot and settles into a balanced stance. |
| `ALS_N_Transition_R` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；ALS_N_Pose frame 0 | A person standing makes a small corrective step with the right foot and settles into a balanced stance. |
| `ALS_CLF_Walk_B` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks backward at a steady pace in a crouch , keeping the torso facing forward. |
| `ALS_CLF_Walk_F` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks forward at a steady pace in a crouch , keeping the torso facing forward. |
| `ALS_CLF_Walk_L` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks left at a steady pace in a crouch , keeping the torso facing forward. |
| `ALS_CLF_Walk_R` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks right at a steady pace in a crouch , keeping the torso facing forward. |
| `ALS_CRF_Walk_B` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks backward at a steady pace in a crouch holding a rifle with both hands, keeping the torso facing forward. |
| `ALS_CRF_Walk_F` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks forward at a steady pace in a crouch holding a rifle with both hands, keeping the torso facing forward. |
| `ALS_CRF_Walk_L` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks left at a steady pace in a crouch holding a rifle with both hands, keeping the torso facing forward. |
| `ALS_CRF_Walk_R` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks right at a steady pace in a crouch holding a rifle with both hands, keeping the torso facing forward. |
| `ALS_N_Run_B` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 21 / 0.666667s | AAT_None；无 | A person runs backward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Run_F` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 25 / 0.8s | AAT_None；无 | A person runs forward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Run_LB` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 21 / 0.666667s | AAT_None；无 | A person runs left backward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Run_LF` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 25 / 0.8s | AAT_None；无 | A person runs left forward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Run_RB` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 21 / 0.666667s | AAT_None；无 | A person runs right backward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Run_RF` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 25 / 0.8s | AAT_None；无 | A person runs right forward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Sprint_F` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 19 / 0.6s | AAT_None；无 | A person sprints forward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Walk_B` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks backward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Walk_F` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks forward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Walk_LB` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks left backward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Walk_LF` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks left forward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Walk_RB` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks right backward at a steady pace , keeping the torso facing forward. |
| `ALS_N_Walk_RF` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks right forward at a steady pace , keeping the torso facing forward. |
| `ALS_CLF_WalkPose` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a stable walking reference posture for moving forward, keeping the torso facing forward. |
| `ALS_CRF_WalkPose` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a stable walking reference posture for moving forward, keeping the torso facing forward. |
| `ALS_N_RunPose_F` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a stable running reference posture for moving forward, keeping the torso facing forward. |
| `ALS_N_RunPose_L` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a stable running reference posture for moving left, keeping the torso facing forward. |
| `ALS_N_RunPose_R` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a stable running reference posture for moving right, keeping the torso facing forward. |
| `ALS_N_WalkPose_F` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 35 / 1.13333s | AAT_None；无 | A person holds a stable walking reference posture for moving forward, keeping the torso facing forward. |
| `ALS_N_WalkPose_L` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 35 / 1.13333s | AAT_None；无 | A person holds a stable walking reference posture for moving left, keeping the torso facing forward. |
| `ALS_N_WalkPose_R` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 35 / 1.13333s | AAT_None；无 | A person holds a stable walking reference posture for moving right, keeping the torso facing forward. |
| `ALS_CLF_GetUp_Back` | 仰卧/俯卧起身 / `getup` | 46 / 1.5s | AAT_None；无 | A person lying on their back gets up into a balanced standing stance. |
| `ALS_CLF_GetUp_Front` | 仰卧/俯卧起身 / `getup` | 46 / 1.5s | AAT_None；无 | A person lying face down gets up into a balanced standing stance. |
| `ALS_N_SecondaryMotion` | 呼吸/轻微重心变化 / `idle_additive` | 81 / 2.66667s | AAT_LocalSpaceBase；ALS_N_Pose frame 0 | A person stands still with subtle breathing, keeping both feet planted and the arms relaxed. |
| `ALS_N_JumpLoop` | 走/跑起跳及空中循环 / `jump` | 21 / 0.666667s | AAT_None；无 | A person remains airborne in a compact balanced jumping pose. |
| `ALS_N_JumpRun_LF` | 走/跑起跳及空中循环 / `jump` | 51 / 1.66667s | AAT_None；无 | A person running forward jumps from a left foot lead phase. |
| `ALS_N_JumpRun_RF` | 走/跑起跳及空中循环 / `jump` | 51 / 1.66667s | AAT_None；无 | A person running forward jumps from a right foot lead phase. |
| `ALS_N_JumpWalk_LF` | 走/跑起跳及空中循环 / `jump` | 51 / 1.66667s | AAT_None；无 | A person walking forward jumps from a left foot lead phase. |
| `ALS_N_JumpWalk_RF` | 走/跑起跳及空中循环 / `jump` | 51 / 1.66667s | AAT_None；无 | A person walking forward jumps from a right foot lead phase. |
| `ALS_N_Land_Heavy` | 轻/重着地反应 / `land` | 36 / 1.16667s | AAT_None；无 | A person lands on both feet heavily and absorbs the impact with bent knees. |
| `ALS_N_Land_Light` | 轻/重着地反应 / `land` | 36 / 1.16667s | AAT_None；无 | A person lands on both feet softly and settles into a balanced stance. |
| `ALS_N_Land_Heavy_Additive` | 可叠加的着地反应 / `land_additive` | 36 / 1.16667s | AAT_RotationOffsetMeshSpace；ALS_N_Pose frame 0 | 确定性派生；不调用 Kimodo |
| `ALS_N_Land_Light_Additive` | 可叠加的着地反应 / `land_additive` | 36 / 1.16667s | AAT_RotationOffsetMeshSpace；ALS_N_Pose frame 0 | 确定性派生；不调用 Kimodo |
| `ALS_N_Falling_LeanPose_0` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_FallLoop frame 0 | A person holds a stable airborne pose without leaning to either side. |
| `ALS_N_Falling_LeanPose_B` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_FallLoop frame 0 | A person holds a stable airborne pose while leaning backward. |
| `ALS_N_Falling_LeanPose_F` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_FallLoop frame 0 | A person holds a stable airborne pose while leaning forward. |
| `ALS_N_Falling_LeanPose_L` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_FallLoop frame 0 | A person holds a stable airborne pose while leaning left. |
| `ALS_N_Falling_LeanPose_R` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_FallLoop frame 0 | A person holds a stable airborne pose while leaning right. |
| `ALS_N_LeanPose_0` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_Run_BasePose frame 0 | A person holds a stable running pose without leaning to either side. |
| `ALS_N_LeanPose_B` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_Run_BasePose frame 0 | A person holds a stable running pose while leaning backward. |
| `ALS_N_LeanPose_F` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_Run_BasePose frame 0 | A person holds a stable running pose while leaning forward. |
| `ALS_N_LeanPose_L` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_Run_BasePose frame 0 | A person holds a stable running pose while leaning left. |
| `ALS_N_LeanPose_R` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；ALS_N_Run_BasePose frame 0 | A person holds a stable running pose while leaning right. |
| `ALS_N_Look_D_Sweep` | 基础观察 yaw×pitch 参数域 / `look_grid` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_N_Pose frame 0 | A person standing still slowly turns their gaze from left to right while looking downward. |
| `ALS_N_Look_F_Sweep` | 基础观察 yaw×pitch 参数域 / `look_grid` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_N_Pose frame 0 | A person standing still slowly turns their gaze from left to right while looking horizontally. |
| `ALS_N_Look_U_Sweep` | 基础观察 yaw×pitch 参数域 / `look_grid` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_N_Pose frame 0 | A person standing still slowly turns their gaze from left to right while looking upward. |
| `ALS_N_Mantle_1m_LH` | 低/高翻越及左右手变体 / `mantle` | 46 / 1.5s | AAT_None；无 | A person steps over a low ledge using the left hand for support. |
| `ALS_N_Mantle_1m_RH` | 低/高翻越及左右手变体 / `mantle` | 46 / 1.5s | AAT_None；无 | A person steps over a low ledge using the right hand for support. |
| `ALS_N_Mantle_2m` | 低/高翻越及左右手变体 / `mantle` | 81 / 2.66667s | AAT_None；无 | A person climbs onto a high ledge using both hands. |
| `ALS_Props_Barrel_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose carrying a barrel with both hands. |
| `ALS_Props_Binoculars_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 7 / 0.2s | AAT_None；无 | A person holds a stable {stance} pose holding binoculars. |
| `ALS_Props_Bow_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 12 / 0.366667s | AAT_None；无 | A person holds a stable {stance} pose holding a bow. |
| `ALS_Props_Box_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose carrying a box with both hands. |
| `ALS_Props_M4A1_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 13 / 0.4s | AAT_None；无 | A person holds a stable {stance} pose holding a rifle with both hands. |
| `ALS_Props_Pistol_1H_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 10 / 0.3s | AAT_None；无 | A person holds a stable {stance} pose holding a pistol in the right hand. |
| `ALS_Props_Pistol_2H_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 11 / 0.333333s | AAT_None；无 | A person holds a stable {stance} pose holding a pistol with both hands. |
| `ALS_Props_Torch_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 7 / 0.2s | AAT_None；无 | A person holds a stable {stance} pose holding a torch. |
| `ALS_StanceVariation_Feminine` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose with a feminine posture. |
| `ALS_StanceVariation_HandsTied` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose with both wrists held together behind the back. |
| `ALS_StanceVariation_Injured` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose with a guarded injured posture. |
| `ALS_StanceVariation_Masculine` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose with a masculine posture. |
| `ALS_StanceVariation_Normal` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose. |
| `ALS_Props_Binoculars_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 7 / 0.2s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_Props_Bow_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 12 / 0.366667s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_Props_M4A1_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 13 / 0.4s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_Props_Pistol_1H_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 10 / 0.3s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_Props_Pistol_2H_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 11 / 0.333333s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_Props_Torch_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 7 / 0.2s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_Props_Binoculars_Aim_Sweep` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Binoculars_Poses_NoCurves frame 3 | A person standing holding binoculars slowly aims from upward to downward. |
| `ALS_Props_Binoculars_Aim_Sweep_Crouched` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Binoculars_Poses_NoCurves frame 6 | A person crouching holding binoculars slowly aims from upward to downward. |
| `ALS_Props_Bow_Aim_Sweep` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Bow_Poses_NoCurves frame 7 | A person standing holding a bow slowly aims from upward to downward. |
| `ALS_Props_Bow_Aim_Sweep_Crouched` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Bow_Poses_NoCurves frame 11 | A person crouching holding a bow slowly aims from upward to downward. |
| `ALS_Props_M4A1_Aim_Sweep` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_M4A1_Poses_NoCurves frame 5 | A person standing holding a rifle with both hands slowly aims from upward to downward. |
| `ALS_Props_M4A1_Aim_Sweep_Crouched` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_M4A1_Poses_NoCurves frame 9 | A person crouching holding a rifle with both hands slowly aims from upward to downward. |
| `ALS_Props_Pistol_1H_Aim_Sweep` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Pistol_1H_Poses_NoCurves frame 5 | A person standing holding a pistol in the right hand slowly aims from upward to downward. |
| `ALS_Props_Pistol_1H_Aim_Sweep_Crouched` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Pistol_1H_Poses_NoCurves frame 9 | A person crouching holding a pistol in the right hand slowly aims from upward to downward. |
| `ALS_Props_Pistol_2H_Aim_Sweep` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Pistol_2H_Poses_NoCurves frame 6 | A person standing holding a pistol with both hands slowly aims from upward to downward. |
| `ALS_Props_Pistol_2H_Aim_Sweep_Crouched` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Pistol_2H_Poses_NoCurves frame 10 | A person crouching holding a pistol with both hands slowly aims from upward to downward. |
| `ALS_Props_Torch_Aim_Sweep` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Torch_Poses_NoCurves frame 3 | A person standing holding a torch slowly aims from upward to downward. |
| `ALS_Props_Torch_Aim_Sweep_Crouched` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；ALS_Props_Torch_Poses_NoCurves frame 6 | A person crouching holding a torch slowly aims from upward to downward. |
| `Bow_Draw` | 弓道具骨架变形 / `prop_animation` | 31 / 1s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_Mannequin_T_Pose` | 骨骼参考/绑定姿态 / `reference` | 2 / 0.0166667s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_N_LandRoll_F` | 翻滚动作/落地翻滚 / `roll` | 46 / 1.5s | AAT_None；无 | A person rolls forward over a shoulder and returns to a balanced standing stance. |
| `ALS_CLF_Rotate_L90` | 瞄准/摄像机持续旋转时的踏步 / `rotate` | 31 / 1s | AAT_None；无 | A person turns 90 degrees to the left by stepping in place while crouching. |
| `ALS_CLF_Rotate_R90` | 瞄准/摄像机持续旋转时的踏步 / `rotate` | 31 / 1s | AAT_None；无 | A person turns 90 degrees to the right by stepping in place while crouching. |
| `ALS_N_Rotate_L90` | 瞄准/摄像机持续旋转时的踏步 / `rotate` | 31 / 1s | AAT_None；无 | A person turns 90 degrees to the left by stepping in place. |
| `ALS_N_Rotate_R90` | 瞄准/摄像机持续旋转时的踏步 / `rotate` | 31 / 1s | AAT_None；无 | A person turns 90 degrees to the right by stepping in place. |
| `ALS_N_Sprint_F_Impulse` | 冲刺加速/Impulse 起步 / `sprint_start` | 19 / 0.6s | AAT_None；无 | A person running forward accelerates into a sprint with coordinated arm swings. |
| `ALS_CLF_to_N` | 站到蹲/蹲到站过渡 / `stance_transition` | 31 / 1s | AAT_None；无 | A person rises from a stable crouch to standing while keeping both feet planted. |
| `ALS_N_to_CLF` | 站到蹲/蹲到站过渡 / `stance_transition` | 31 / 1s | AAT_None；无 | A person lowers from standing into a stable crouch while keeping both feet planted. |
| `ALS_CLF_TurnIP_L180` | 90/180 左右 Turn-in-place / `turn` | 71 / 2.33333s | AAT_None；无 | A person turns 180 degrees to the left by stepping in place while crouching. |
| `ALS_CLF_TurnIP_L90` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；无 | A person turns 90 degrees to the left by stepping in place while crouching. |
| `ALS_CLF_TurnIP_R180` | 90/180 左右 Turn-in-place / `turn` | 71 / 2.33333s | AAT_None；无 | A person turns 180 degrees to the right by stepping in place while crouching. |
| `ALS_CLF_TurnIP_R90` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；无 | A person turns 90 degrees to the right by stepping in place while crouching. |
| `ALS_N_TurnIP_L180` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；ALS_N_Pose frame 0 | A person turns 180 degrees to the left by stepping in place. |
| `ALS_N_TurnIP_L90` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；ALS_N_Pose frame 0 | A person turns 90 degrees to the left by stepping in place. |
| `ALS_N_TurnIP_R180` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；ALS_N_Pose frame 0 | A person turns 180 degrees to the right by stepping in place. |
| `ALS_N_TurnIP_R90` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；ALS_N_Pose frame 0 | A person turns 90 degrees to the right by stepping in place. |
| `ALS_Props_M4A1_Run_F` | Rifle 的整身/手臂移动资源 / `weapon_cycle` | 25 / 0.8s | AAT_None；无 | A person runs forward at a steady pace while holding a rifle with both hands. |
| `ALS_Props_M4A1_Run_F_Arms` | Rifle 的整身/手臂移动资源 / `weapon_cycle` | 25 / 0.8s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_Props_M4A1_Sprint_F` | Rifle 的整身/手臂移动资源 / `weapon_cycle` | 19 / 0.6s | AAT_None；无 | A person sprints forward at a steady pace while holding a rifle with both hands. |
| `ALS_Props_M4A1_Sprint_F_Arms` | Rifle 的整身/手臂移动资源 / `weapon_cycle` | 19 / 0.6s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `ALS_Props_M4A1_Sprint_F_Impulse` | Rifle sprint impulse 配对资源 / `weapon_start` | 19 / 0.6s | AAT_None；无 | A person accelerates forward into a sprint while holding a rifle with both hands. |
| `ALS_Props_M4A1_Sprint_F_Impulse_Arms` | Rifle sprint impulse 配对资源 / `weapon_start` | 19 / 0.6s | AAT_None；无 | 确定性派生；不调用 Kimodo |
## refactored 资源

| 资产 | 作用/生成方法 | 原关键帧/时长 | 原加法参考 | 提示词草稿 |
| --- | --- | --- | --- | --- |
| `A_Als_Acceleration_Backward` | 加速度驱动的身体动态细节 / `acceleration_additive` | 31 / 1s | AAT_LocalSpaceBase；A_Als_Lean_Pose frame 0 | A person adjusts their running posture while accelerating backward. |
| `A_Als_Acceleration_Forward` | 加速度驱动的身体动态细节 / `acceleration_additive` | 31 / 1s | AAT_LocalSpaceBase；A_Als_Lean_Pose frame 0 | A person adjusts their running posture while accelerating forward. |
| `A_Als_Acceleration_Left` | 加速度驱动的身体动态细节 / `acceleration_additive` | 31 / 1s | AAT_LocalSpaceBase；A_Als_Lean_Pose frame 0 | A person adjusts their running posture while accelerating left. |
| `A_Als_Acceleration_Right` | 加速度驱动的身体动态细节 / `acceleration_additive` | 31 / 1s | AAT_LocalSpaceBase；A_Als_Lean_Pose frame 0 | A person adjusts their running posture while accelerating right. |
| `A_Als_Crouch_Pose` | 固定基底、站蹲/Lean 零参考 / `base_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a stable crouching pose with both feet planted. |
| `A_Als_Lean_Pose` | 固定基底、站蹲/Lean 零参考 / `base_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a balanced running reference pose without translating. |
| `A_Als_Stand_Pose` | 固定基底、站蹲/Lean 零参考 / `base_pose` | 2 / 0.0333333s | AAT_None；无 | A person holds a stable balanced standing pose with relaxed arms. |
| `A_Als_Fall` | 正常/快速下落循环 / `fall` | 31 / 1s | AAT_None；无 | A person falls through the air with a relaxed bent-knee posture. |
| `A_Als_Fall_Fast` | 正常/快速下落循环 / `fall` | 31 / 1s | AAT_None；无 | A person falls through the air with a spread protective posture. |
| `A_Als_Flail` | ragdoll 空中摆动/恢复辅助 / `flail` | 21 / 0.666667s | AAT_None；无 | A person falling through the air moves their arms and legs in a controlled flailing motion. |
| `A_Als_Crouch_DynamicTransition_Left` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 47 / 1.53333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person crouching makes a small corrective step with the left foot and settles into a balanced stance. |
| `A_Als_Crouch_DynamicTransition_Right` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 47 / 1.53333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person crouching makes a small corrective step with the right foot and settles into a balanced stance. |
| `A_Als_Crouch_Transition_Left` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person crouching makes a small corrective step with the left foot and settles into a balanced stance. |
| `A_Als_Crouch_Transition_Right` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person crouching makes a small corrective step with the right foot and settles into a balanced stance. |
| `A_Als_Stand_DynamicTransition_Left` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 47 / 1.53333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person standing makes a small corrective step with the left foot and settles into a balanced stance. |
| `A_Als_Stand_DynamicTransition_Right` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 47 / 1.53333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person standing makes a small corrective step with the right foot and settles into a balanced stance. |
| `A_Als_Stand_Transition_Left` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person standing makes a small corrective step with the left foot and settles into a balanced stance. |
| `A_Als_Stand_Transition_Right` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person standing makes a small corrective step with the right foot and settles into a balanced stance. |
| `A_Als_Stop_Left` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person walking forward comes to a balanced stop with the left foot in the designated support phase. |
| `A_Als_Stop_Right` | 左右脚停止、静止脚距纠正与动态切换 / `foot_transition` | 71 / 2.33333s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person walking forward comes to a balanced stop with the right foot in the designated support phase. |
| `A_Als_Crouch_Walk_Backward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks backward at a steady pace in a crouch , keeping the torso facing forward. |
| `A_Als_Crouch_Walk_Forward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks forward at a steady pace in a crouch , keeping the torso facing forward. |
| `A_Als_Crouch_Walk_Left_Backward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks left backward at a steady pace in a crouch , keeping the torso facing forward. |
| `A_Als_Crouch_Walk_Left_Forward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks left forward at a steady pace in a crouch , keeping the torso facing forward. |
| `A_Als_Crouch_Walk_Right_Backward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks right backward at a steady pace in a crouch , keeping the torso facing forward. |
| `A_Als_Crouch_Walk_Right_Forward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks right forward at a steady pace in a crouch , keeping the torso facing forward. |
| `A_Als_Run_Backward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 21 / 0.666667s | AAT_None；无 | A person runs backward at a steady pace , keeping the torso facing forward. |
| `A_Als_Run_Forward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 25 / 0.8s | AAT_None；无 | A person runs forward at a steady pace , keeping the torso facing forward. |
| `A_Als_Run_Left_Backward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 21 / 0.666667s | AAT_None；无 | A person runs left backward at a steady pace , keeping the torso facing forward. |
| `A_Als_Run_Left_Forward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 25 / 0.8s | AAT_None；无 | A person runs left forward at a steady pace , keeping the torso facing forward. |
| `A_Als_Run_Right_Backward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 21 / 0.666667s | AAT_None；无 | A person runs right backward at a steady pace , keeping the torso facing forward. |
| `A_Als_Run_Right_Forward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 25 / 0.8s | AAT_None；无 | A person runs right forward at a steady pace , keeping the torso facing forward. |
| `A_Als_Sprint` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 19 / 0.6s | AAT_None；无 | A person sprints forward at a steady pace , keeping the torso facing forward. |
| `A_Als_Walk_Backward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks backward at a steady pace , keeping the torso facing forward. |
| `A_Als_Walk_Forward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks forward at a steady pace , keeping the torso facing forward. |
| `A_Als_Walk_Left_Backward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks left backward at a steady pace , keeping the torso facing forward. |
| `A_Als_Walk_Left_Forward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks left forward at a steady pace , keeping the torso facing forward. |
| `A_Als_Walk_Right_Backward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks right backward at a steady pace , keeping the torso facing forward. |
| `A_Als_Walk_Right_Forward` | 常速站/蹲/持枪方向移动循环 / `gait_cycle` | 35 / 1.13333s | AAT_None；无 | A person walks right forward at a steady pace , keeping the torso facing forward. |
| `A_Als_Crouch_Walk_Pose` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 35 / 1.13333s | AAT_None；无 | A person holds a stable walking reference posture for moving forward, keeping the torso facing forward. |
| `A_Als_Run_Forward_Pose` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 25 / 0.8s | AAT_None；无 | A person holds a stable running reference posture for moving forward, keeping the torso facing forward. |
| `A_Als_Run_Left_Pose` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 25 / 0.8s | AAT_None；无 | A person holds a stable running reference posture for moving left, keeping the torso facing forward. |
| `A_Als_Run_Right_Pose` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 25 / 0.8s | AAT_None；无 | A person holds a stable running reference posture for moving right, keeping the torso facing forward. |
| `A_Als_Walk_Forward_Pose` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 35 / 1.13333s | AAT_None；无 | A person holds a stable walking reference posture for moving forward, keeping the torso facing forward. |
| `A_Als_Walk_Left_Pose` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 35 / 1.13333s | AAT_None；无 | A person holds a stable walking reference posture for moving left, keeping the torso facing forward. |
| `A_Als_Walk_Right_Pose` | 步幅/方向混合的参考姿态样本 / `gait_pose` | 35 / 1.13333s | AAT_None；无 | A person holds a stable walking reference posture for moving right, keeping the torso facing forward. |
| `A_Als_GetUp_Back` | 仰卧/俯卧起身 / `getup` | 46 / 1.5s | AAT_None；无 | A person lying on their back gets up into a balanced standing stance. |
| `A_Als_GetUp_Front` | 仰卧/俯卧起身 / `getup` | 46 / 1.5s | AAT_None；无 | A person lying face down gets up into a balanced standing stance. |
| `A_Als_Idle` | 呼吸/轻微重心变化 / `idle_additive` | 81 / 2.66667s | AAT_LocalSpaceBase；A_Als_Stand_Pose frame 0 | A person stands still with subtle breathing, keeping both feet planted and the arms relaxed. |
| `A_Als_Jump_Loop` | 走/跑起跳及空中循环 / `jump` | 21 / 0.666667s | AAT_None；无 | A person remains airborne in a compact balanced jumping pose. |
| `A_Als_Jump_Run_Left` | 走/跑起跳及空中循环 / `jump` | 51 / 1.66667s | AAT_None；无 | A person running forward jumps from a left foot lead phase. |
| `A_Als_Jump_Run_Right` | 走/跑起跳及空中循环 / `jump` | 51 / 1.66667s | AAT_None；无 | A person running forward jumps from a right foot lead phase. |
| `A_Als_Jump_Walk_Left` | 走/跑起跳及空中循环 / `jump` | 51 / 1.66667s | AAT_None；无 | A person walking forward jumps from a left foot lead phase. |
| `A_Als_Jump_Walk_Right` | 走/跑起跳及空中循环 / `jump` | 51 / 1.66667s | AAT_None；无 | A person walking forward jumps from a right foot lead phase. |
| `A_Als_Land_Heavy` | 轻/重着地反应 / `land` | 36 / 1.16667s | AAT_None；无 | A person lands on both feet heavily and absorbs the impact with bent knees. |
| `A_Als_Land_Light` | 轻/重着地反应 / `land` | 36 / 1.16667s | AAT_None；无 | A person lands on both feet softly and settles into a balanced stance. |
| `A_Als_Land_Heavy_Additive` | 可叠加的着地反应 / `land_additive` | 36 / 1.16667s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | 确定性派生；不调用 Kimodo |
| `A_Als_Land_Light_Additive` | 可叠加的着地反应 / `land_additive` | 36 / 1.16667s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | 确定性派生；不调用 Kimodo |
| `A_Als_Air_Lean` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Fall frame 0 | A person holds a stable airborne pose without leaning to either side. |
| `A_Als_Air_Lean_Backward` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Fall frame 0 | A person holds a stable airborne pose while leaning backward. |
| `A_Als_Air_Lean_Forward` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Fall frame 0 | A person holds a stable airborne pose while leaning forward. |
| `A_Als_Air_Lean_Left` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Fall frame 0 | A person holds a stable airborne pose while leaning left. |
| `A_Als_Air_Lean_Right` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Fall frame 0 | A person holds a stable airborne pose while leaning right. |
| `A_Als_Lean` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Lean_Pose frame 0 | A person holds a stable running pose without leaning to either side. |
| `A_Als_Lean_Backward` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Lean_Pose frame 0 | A person holds a stable running pose while leaning backward. |
| `A_Als_Lean_Forward` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Lean_Pose frame 0 | A person holds a stable running pose while leaning forward. |
| `A_Als_Lean_Left` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Lean_Pose frame 0 | A person holds a stable running pose while leaning left. |
| `A_Als_Lean_Right` | 地面/空中二维倾斜修正 / `lean_additive` | 2 / 0.0333333s | AAT_LocalSpaceBase；A_Als_Lean_Pose frame 0 | A person holds a stable running pose while leaning right. |
| `A_Als_Look_Down` | 基础观察 yaw×pitch 参数域 / `look_grid` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person standing still slowly turns their gaze from left to right while looking downward. |
| `A_Als_Look_Forward` | 基础观察 yaw×pitch 参数域 / `look_grid` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person standing still slowly turns their gaze from left to right while looking horizontally. |
| `A_Als_Look_Up` | 基础观察 yaw×pitch 参数域 / `look_grid` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Stand_Pose frame 0 | A person standing still slowly turns their gaze from left to right while looking upward. |
| `A_Als_Mantle_High` | 低/高翻越及左右手变体 / `mantle` | 81 / 2.66667s | AAT_None；无 | A person climbs onto a high ledge using both hands. |
| `A_Als_Mantle_Low_Left` | 低/高翻越及左右手变体 / `mantle` | 46 / 1.5s | AAT_None；无 | A person steps over a low ledge using the left hand for support. |
| `A_Als_Mantle_Low_Right` | 低/高翻越及左右手变体 / `mantle` | 46 / 1.5s | AAT_None；无 | A person steps over a low ledge using the right hand for support. |
| `A_Als_Barrel_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose carrying a barrel with both hands. |
| `A_Als_Binoculars_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 7 / 0.2s | AAT_None；无 | A person holds a stable {stance} pose holding binoculars. |
| `A_Als_Bow_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 12 / 0.366667s | AAT_None；无 | A person holds a stable {stance} pose holding a bow. |
| `A_Als_Box_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose carrying a box with both hands. |
| `A_Als_Default_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose. |
| `A_Als_Feminine_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose with a feminine posture. |
| `A_Als_HandsTied_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose with both wrists held together behind the back. |
| `A_Als_Injured_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose with a guarded injured posture. |
| `A_Als_Masculine_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 4 / 0.1s | AAT_None；无 | A person holds a stable {stance} pose with a masculine posture. |
| `A_Als_PistolOneHanded_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 10 / 0.3s | AAT_None；无 | A person holds a stable {stance} pose holding a pistol in the right hand. |
| `A_Als_PistolTwoHanded_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 11 / 0.333333s | AAT_None；无 | A person holds a stable {stance} pose holding a pistol with both hands. |
| `A_Als_Rifle_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 13 / 0.4s | AAT_None；无 | A person holds a stable {stance} pose holding a rifle with both hands. |
| `A_Als_Torch_Poses` | Overlay 按帧读取的状态姿态表 / `pose_table` | 7 / 0.2s | AAT_None；无 | A person holds a stable {stance} pose holding a torch. |
| `A_Als_Binoculars_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 7 / 0.2s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `A_Als_Bow_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 12 / 0.366667s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `A_Als_PistolOneHanded_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 10 / 0.3s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `A_Als_PistolTwoHanded_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 11 / 0.333333s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `A_Als_Rifle_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 13 / 0.4s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `A_Als_Torch_Poses_NoCurves` | 无曲线的 additive 参考姿态表 / `pose_table_copy` | 7 / 0.2s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `A_Als_Binoculars_Aim` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Binoculars_Poses_NoCurves frame 3 | A person standing holding binoculars slowly aims from upward to downward. |
| `A_Als_Binoculars_Aim_Crouch` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Binoculars_Poses_NoCurves frame 6 | A person crouching holding binoculars slowly aims from upward to downward. |
| `A_Als_Bow_Aim` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Bow_Poses_NoCurves frame 7 | A person standing holding a bow slowly aims from upward to downward. |
| `A_Als_Bow_Aim_Crouch` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Bow_Poses_NoCurves frame 11 | A person crouching holding a bow slowly aims from upward to downward. |
| `A_Als_PistolOneHanded_Aim` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_PistolOneHanded_Poses_NoCurves frame 5 | A person standing holding a pistol in the right hand slowly aims from upward to downward. |
| `A_Als_PistolOneHanded_Aim_Crouch` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_PistolOneHanded_Poses_NoCurves frame 9 | A person crouching holding a pistol in the right hand slowly aims from upward to downward. |
| `A_Als_PistolTwoHanded_Aim` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_PistolTwoHanded_Poses_NoCurves frame 6 | A person standing holding a pistol with both hands slowly aims from upward to downward. |
| `A_Als_PistolTwoHanded_Aim_Crouch` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_PistolTwoHanded_Poses_NoCurves frame 10 | A person crouching holding a pistol with both hands slowly aims from upward to downward. |
| `A_Als_Rifle_Aim` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Rifle_Poses_NoCurves frame 5 | A person standing holding a rifle with both hands slowly aims from upward to downward. |
| `A_Als_Rifle_Aim_Crouch` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Rifle_Poses_NoCurves frame 9 | A person crouching holding a rifle with both hands slowly aims from upward to downward. |
| `A_Als_Torch_Aim` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Torch_Poses_NoCurves frame 3 | A person standing holding a torch slowly aims from upward to downward. |
| `A_Als_Torch_Aim_Crouch` | 持物站蹲瞄准 pitch 参数轴 / `prop_aim` | 31 / 1s | AAT_RotationOffsetMeshSpace；A_Als_Torch_Poses_NoCurves frame 6 | A person crouching holding a torch slowly aims from upward to downward. |
| `A_Als_Roll` | 翻滚动作/落地翻滚 / `roll` | 46 / 1.5s | AAT_None；无 | A person rolls forward over a shoulder and returns to a balanced standing stance. |
| `A_Als_Crouch_Rotate_90_Left` | 瞄准/摄像机持续旋转时的踏步 / `rotate` | 31 / 1s | AAT_None；无 | A person turns 90 degrees to the left by stepping in place while crouching. |
| `A_Als_Crouch_Rotate_90_Right` | 瞄准/摄像机持续旋转时的踏步 / `rotate` | 31 / 1s | AAT_None；无 | A person turns 90 degrees to the right by stepping in place while crouching. |
| `A_Als_Rotate_90_Left` | 瞄准/摄像机持续旋转时的踏步 / `rotate` | 31 / 1s | AAT_None；无 | A person turns 90 degrees to the left by stepping in place. |
| `A_Als_Rotate_90_Right` | 瞄准/摄像机持续旋转时的踏步 / `rotate` | 31 / 1s | AAT_None；无 | A person turns 90 degrees to the right by stepping in place. |
| `A_Als_Sprint_Acceleration` | 冲刺加速/Impulse 起步 / `sprint_start` | 19 / 0.6s | AAT_None；无 | A person running forward accelerates into a sprint with coordinated arm swings. |
| `A_Als_CrouchToStand` | 站到蹲/蹲到站过渡 / `stance_transition` | 31 / 1s | AAT_None；无 | A person rises from a stable crouch to standing while keeping both feet planted. |
| `A_Als_StandToCrouch` | 站到蹲/蹲到站过渡 / `stance_transition` | 31 / 1s | AAT_None；无 | A person lowers from standing into a stable crouch while keeping both feet planted. |
| `A_Als_Crouch_Turn_180_Left` | 90/180 左右 Turn-in-place / `turn` | 71 / 2.33333s | AAT_None；无 | A person turns 180 degrees to the left by stepping in place while crouching. |
| `A_Als_Crouch_Turn_180_Right` | 90/180 左右 Turn-in-place / `turn` | 71 / 2.33333s | AAT_None；无 | A person turns 180 degrees to the right by stepping in place while crouching. |
| `A_Als_Crouch_Turn_90_Left` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；无 | A person turns 90 degrees to the left by stepping in place while crouching. |
| `A_Als_Crouch_Turn_90_Right` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；无 | A person turns 90 degrees to the right by stepping in place while crouching. |
| `A_Als_Turn_180_Left` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；A_Als_Stand_Pose frame 0 | A person turns 180 degrees to the left by stepping in place. |
| `A_Als_Turn_180_Right` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；A_Als_Stand_Pose frame 0 | A person turns 180 degrees to the right by stepping in place. |
| `A_Als_Turn_90_Left` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；A_Als_Stand_Pose frame 0 | A person turns 90 degrees to the left by stepping in place. |
| `A_Als_Turn_90_Right` | 90/180 左右 Turn-in-place / `turn` | 61 / 2s | AAT_None；A_Als_Stand_Pose frame 0 | A person turns 90 degrees to the right by stepping in place. |
| `A_Als_Rifle_Run` | Rifle 的整身/手臂移动资源 / `weapon_cycle` | 25 / 0.8s | AAT_None；无 | A person runs forward at a steady pace while holding a rifle with both hands. |
| `A_Als_Rifle_Run_Arms` | Rifle 的整身/手臂移动资源 / `weapon_cycle` | 25 / 0.8s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `A_Als_Rifle_Sprint` | Rifle 的整身/手臂移动资源 / `weapon_cycle` | 19 / 0.6s | AAT_None；无 | A person sprints forward at a steady pace while holding a rifle with both hands. |
| `A_Als_Rifle_Sprint_Arms` | Rifle 的整身/手臂移动资源 / `weapon_cycle` | 19 / 0.6s | AAT_None；无 | 确定性派生；不调用 Kimodo |
| `A_Als_Rifle_Sprint_Impulse` | Rifle sprint impulse 配对资源 / `weapon_start` | 19 / 0.6s | AAT_None；无 | A person accelerates forward into a sprint while holding a rifle with both hands. |
| `A_Als_Rifle_Sprint_Impulse_Arms` | Rifle sprint impulse 配对资源 / `weapon_start` | 19 / 0.6s | AAT_None；无 | 确定性派生；不调用 Kimodo |

## 逐资产生成约束

坐标、共享人物风格与 sparse 条件限制见生产设计；以下目标仍需用批准的 pose/socket 数值实例化。

### refactored `A_Als_Acceleration_Backward` — 约束

作用：加速度驱动的身体动态细节。沿图输入参数的各个样本制作修正，所有方向共享基底；用原 sampler/图确认是姿态轴还是播放，不把时间随意当启动过程。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Acceleration_Forward` — 约束

作用：加速度驱动的身体动态细节。沿图输入参数的各个样本制作修正，所有方向共享基底；用原 sampler/图确认是姿态轴还是播放，不把时间随意当启动过程。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Acceleration_Left` — 约束

作用：加速度驱动的身体动态细节。沿图输入参数的各个样本制作修正，所有方向共享基底；用原 sampler/图确认是姿态轴还是播放，不把时间随意当启动过程。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Acceleration_Right` — 约束

作用：加速度驱动的身体动态细节。沿图输入参数的各个样本制作修正，所有方向共享基底；用原 sampler/图确认是姿态轴还是播放，不把时间随意当启动过程。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Pose` — 约束

作用：固定基底、站蹲/Lean 零参考。生成候选后选定一个目标 rig 姿态并冻结；为所有依赖资产提供同一版本的零差参考。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Lean_Pose` — 约束

作用：固定基底、站蹲/Lean 零参考。生成候选后选定一个目标 rig 姿态并冻结；为所有依赖资产提供同一版本的零差参考。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Stand_Pose` — 约束

作用：固定基底、站蹲/Lean 零参考。生成候选后选定一个目标 rig 姿态并冻结；为所有依赖资产提供同一版本的零差参考。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Fall` — 约束

作用：正常/快速下落循环。制作空中姿态循环；可生成悬空/下落候选，再剥离全局坠落轨迹，不能施加地面锁脚。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Fall_Fast` — 约束

作用：正常/快速下落循环。制作空中姿态循环；可生成悬空/下落候选，再剥离全局坠落轨迹，不能施加地面锁脚。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Flail` — 约束

作用：ragdoll 空中摆动/恢复辅助。生成可循环的悬空四肢摆动，幅度受关节及物理混合限制；不锁脚，不用动作根轨迹驱动胶囊。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_DynamicTransition_Left` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_DynamicTransition_Right` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Transition_Left` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Transition_Right` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Stand_DynamicTransition_Left` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Stand_DynamicTransition_Right` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Stand_Transition_Left` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Stand_Transition_Right` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Stop_Left` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Stop_Right` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Walk_Backward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, -1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Walk_Forward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Walk_Left_Backward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, -0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Walk_Left_Forward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, 0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Walk_Right_Backward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, -0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Walk_Right_Forward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, 0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Run_Backward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, -1]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Run_Forward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Run_Left_Backward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, -0.707107]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Run_Left_Forward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, 0.707107]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Run_Right_Backward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, -0.707107]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Run_Right_Forward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, 0.707107]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Sprint` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Walk_Backward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, -1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Walk_Forward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Walk_Left_Backward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, -0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Walk_Left_Forward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, 0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Walk_Right_Backward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, -0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Walk_Right_Forward` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, 0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Walk_Pose` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Run_Forward_Pose` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Run_Left_Pose` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Run_Right_Pose` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Walk_Forward_Pose` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Walk_Left_Pose` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Walk_Right_Pose` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_GetUp_Back` — 约束

作用：仰卧/俯卧起身。从真实恢复起始姿态连接蹲伏/站立终点，支撑手脚接触及根/胶囊恢复路径明确。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 姿态恢复起点（仰卧/俯卧）和规定站蹲终点；支撑手/脚 contact；不能混淆 Back/Front。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_GetUp_Front` — 约束

作用：仰卧/俯卧起身。从真实恢复起始姿态连接蹲伏/站立终点，支撑手脚接触及根/胶囊恢复路径明确。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 姿态恢复起点（仰卧/俯卧）和规定站蹲终点；支撑手/脚 contact；不能混淆 Back/Front。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Idle` — 约束

作用：呼吸/轻微重心变化。相对同一站姿的局部加法循环；小幅运动、首尾姿态及一阶速度连续，避免恒定肩髋偏移。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 固定 XZ 与 heading；fullbody 起止采用同一 standing zero pose。
- end-effector: 稀疏双脚支撑目标；连续锁脚由后处理完成。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 相对当前站姿生成 local additive；去恒定姿态偏移；首尾位置/四元数和速度联合闭合。
- 禁止对 BasePose 与 SecondaryMotion 独立自由抽样后仅复制 additive metadata。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Jump_Loop` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Jump_Run_Left` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Jump_Run_Right` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Jump_Walk_Left` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Jump_Walk_Right` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Land_Heavy` — 约束

作用：轻/重着地反应。接触时刻、骨盆缓冲和末 standing pose 明确；非加法与加法版本共享表演/接触事件。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Land_Light` — 约束

作用：轻/重着地反应。接触时刻、骨盆缓冲和末 standing pose 明确；非加法与加法版本共享表演/接触事件。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Land_Heavy_Additive` — 约束

作用：可叠加的着地反应。从匹配的 full-pose 着地表演按 exact basePoseType/baseFrame 转换，剔除全局飞行，不能独立生成另一段着地。
生产方式：`derive_from_paired_full_pose_master`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。
- 与非加法 Land 同源；ABPT_AnimFrame/ABPT_AnimScaled 分别依原评价策略求差，末态残差收敛。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Land_Light_Additive` — 约束

作用：可叠加的着地反应。从匹配的 full-pose 着地表演按 exact basePoseType/baseFrame 转换，剔除全局飞行，不能独立生成另一段着地。
生产方式：`derive_from_paired_full_pose_master`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。
- 与非加法 Land 同源；ABPT_AnimFrame/ABPT_AnimScaled 分别依原评价策略求差，末态残差收敛。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Air_Lean` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Air_Lean_Backward` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Air_Lean_Forward` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Air_Lean_Left` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Air_Lean_Right` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Lean` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Lean_Backward` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Lean_Forward` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Lean_Left` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Lean_Right` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Look_Down` — 约束

作用：基础观察 yaw×pitch 参数域。F/D/U 是固定俯仰行，时间对应 yaw；同一基础姿态/根位置，逐参数目标后组装 mesh-space additive。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- 目标 pitch 行=-90°；time u 的 yaw 参数=-180+360*u，中心 u=.5 必须与共同前视 base 对齐；参数不是 root heading。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。

验收：

- pitch=0/yaw=0 零差；三行相同 yaw 时姿态连续，端点解剖极限不可用扭曲髋膝补偿。
- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Look_Forward` — 约束

作用：基础观察 yaw×pitch 参数域。F/D/U 是固定俯仰行，时间对应 yaw；同一基础姿态/根位置，逐参数目标后组装 mesh-space additive。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- 目标 pitch 行=0°；time u 的 yaw 参数=-180+360*u，中心 u=.5 必须与共同前视 base 对齐；参数不是 root heading。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。

验收：

- pitch=0/yaw=0 零差；三行相同 yaw 时姿态连续，端点解剖极限不可用扭曲髋膝补偿。
- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Look_Up` — 约束

作用：基础观察 yaw×pitch 参数域。F/D/U 是固定俯仰行，时间对应 yaw；同一基础姿态/根位置，逐参数目标后组装 mesh-space additive。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- 目标 pitch 行=90°；time u 的 yaw 参数=-180+360*u，中心 u=.5 必须与共同前视 base 对齐；参数不是 root heading。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。

验收：

- pitch=0/yaw=0 零差；三行相同 yaw 时姿态连续，端点解剖极限不可用扭曲髋膝补偿。
- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Mantle_High` — 约束

作用：低/高翻越及左右手变体。人体相对平台目标生成，约束手触边/脚登顶/末站姿；按现有 Mantle motion source 的局部空间和 correction 曲线制作。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 起始 approach、手触边、身体越边、脚登顶、同版本终点姿态。
- end-effector: 指定 left/right 或双手 ledge targets；root2d 只控水平，竖直高度由 fullbody root_positions 的 Y 控制。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按本项目 Mantle target-relative motion source、start offset/correction curves 重制；不直接把 BVH Hips trajectory 当 runtime root。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Mantle_Low_Left` — 约束

作用：低/高翻越及左右手变体。人体相对平台目标生成，约束手触边/脚登顶/末站姿；按现有 Mantle motion source 的局部空间和 correction 曲线制作。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 起始 approach、手触边、身体越边、脚登顶、同版本终点姿态。
- end-effector: 指定 left/right 或双手 ledge targets；root2d 只控水平，竖直高度由 fullbody root_positions 的 Y 控制。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按本项目 Mantle target-relative motion source、start offset/correction curves 重制；不直接把 BVH Hips trajectory 当 runtime root。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Mantle_Low_Right` — 约束

作用：低/高翻越及左右手变体。人体相对平台目标生成，约束手触边/脚登顶/末站姿；按现有 Mantle motion source 的局部空间和 correction 曲线制作。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 起始 approach、手触边、身体越边、脚登顶、同版本终点姿态。
- end-effector: 指定 left/right 或双手 ledge targets；root2d 只控水平，竖直高度由 fullbody root_positions 的 Y 控制。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按本项目 Mantle target-relative motion source、start offset/correction curves 重制；不直接把 BVH Hips trajectory 当 runtime root。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Barrel_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Binoculars_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Bow_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Box_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Default_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Feminine_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_HandsTied_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Injured_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Masculine_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_PistolOneHanded_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_PistolTwoHanded_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Torch_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Binoculars_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Bow_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_PistolOneHanded_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_PistolTwoHanded_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Torch_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Binoculars_Aim` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Binoculars_Aim_Crouch` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Bow_Aim` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Bow_Aim_Crouch` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_PistolOneHanded_Aim` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_PistolOneHanded_Aim_Crouch` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_PistolTwoHanded_Aim` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_PistolTwoHanded_Aim_Crouch` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Aim` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Aim_Crouch` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Torch_Aim` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Torch_Aim_Crouch` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Roll` — 约束

作用：翻滚动作/落地翻滚。明确初始接触、滚动身体碰撞和末姿态；根轨迹仅由现有 Root Motion owner 消费一次，通知与碰撞窗口跟随生成表演。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody + root2d: 入滚/肩背接触/出滚及末 standing pose；无持续脚锁，目标 rig 碰撞需独立检查。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Rotate_90_Left` — 约束

作用：瞄准/摄像机持续旋转时的踏步。原地连续踏步的循环；去累计 heading 后局部姿态闭合，转速/转向交给原 runtime 和曲线。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Rotate_90_Right` — 约束

作用：瞄准/摄像机持续旋转时的踏步。原地连续踏步的循环；去累计 heading 后局部姿态闭合，转速/转向交给原 runtime 和曲线。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rotate_90_Left` — 约束

作用：瞄准/摄像机持续旋转时的踏步。原地连续踏步的循环；去累计 heading 后局部姿态闭合，转速/转向交给原 runtime 和曲线。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rotate_90_Right` — 约束

作用：瞄准/摄像机持续旋转时的踏步。原地连续踏步的循环；去累计 heading 后局部姿态闭合，转速/转向交给原 runtime 和曲线。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Sprint_Acceleration` — 约束

作用：冲刺加速/Impulse 起步。从对应移动相位进入 sprint；根路径加速、末姿态与 sprint 同相位；不可随机生成后硬压缩到 0.6 秒。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- root2d 速度连续递增到 sprint master；结束关键帧采用其相同相位。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_CrouchToStand` — 约束

作用：站到蹲/蹲到站过渡。从固定 Stand/Crouch 同源姿态开始结束；脚锚保持，骨盆高度变化符合胶囊切换。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody 起止使用同版本 Stand_Pose/Crouch_Pose；双脚锚不改变，骨盆高度不可来自随意 standing idle。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_StandToCrouch` — 约束

作用：站到蹲/蹲到站过渡。从固定 Stand/Crouch 同源姿态开始结束；脚锚保持，骨盆高度变化符合胶囊切换。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody 起止使用同版本 Stand_Pose/Crouch_Pose；双脚锚不改变，骨盆高度不可来自随意 standing idle。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Turn_180_Left` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到180°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Turn_180_Right` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-180°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Turn_90_Left` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Crouch_Turn_90_Right` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Turn_180_Left` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到180°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Turn_180_Right` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-180°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Turn_90_Left` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Turn_90_Right` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Run` — 约束

作用：Rifle 的整身/手臂移动资源。Run/Sprint 全身与 Arms 必须来自同一生成结果和同一相位；手臂版本按已验证分层合同派生。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Run_Arms` — 约束

作用：Rifle 的整身/手臂移动资源。Run/Sprint 全身与 Arms 必须来自同一生成结果和同一相位；手臂版本按已验证分层合同派生。
生产方式：`derive_from_paired_full_body_master`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Sprint` — 约束

作用：Rifle 的整身/手臂移动资源。Run/Sprint 全身与 Arms 必须来自同一生成结果和同一相位；手臂版本按已验证分层合同派生。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Sprint_Arms` — 约束

作用：Rifle 的整身/手臂移动资源。Run/Sprint 全身与 Arms 必须来自同一生成结果和同一相位；手臂版本按已验证分层合同派生。
生产方式：`derive_from_paired_full_body_master`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Sprint_Impulse` — 约束

作用：Rifle sprint impulse 配对资源。与通用 sprint 起步同相位/时长；Arms 与全身同源，并维持双手 grip。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- root2d 速度连续递增到 sprint master；结束关键帧采用其相同相位。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### refactored `A_Als_Rifle_Sprint_Impulse_Arms` — 约束

作用：Rifle sprint impulse 配对资源。与通用 sprint 起步同相位/时长；Arms 与全身同源，并维持双手 grip。
生产方式：`derive_from_paired_full_body_master`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- root2d 速度连续递增到 sprint master；结束关键帧采用其相同相位。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LocoDetail_Accel_B` — 约束

作用：加速度驱动的身体动态细节。沿图输入参数的各个样本制作修正，所有方向共享基底；用原 sampler/图确认是姿态轴还是播放，不把时间随意当启动过程。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LocoDetail_Accel_F` — 约束

作用：加速度驱动的身体动态细节。沿图输入参数的各个样本制作修正，所有方向共享基底；用原 sampler/图确认是姿态轴还是播放，不把时间随意当启动过程。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LocoDetail_Accel_L` — 约束

作用：加速度驱动的身体动态细节。沿图输入参数的各个样本制作修正，所有方向共享基底；用原 sampler/图确认是姿态轴还是播放，不把时间随意当启动过程。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LocoDetail_Accel_R` — 约束

作用：加速度驱动的身体动态细节。沿图输入参数的各个样本制作修正，所有方向共享基底；用原 sampler/图确认是姿态轴还是播放，不把时间随意当启动过程。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_Pose` — 约束

作用：固定基底、站蹲/Lean 零参考。生成候选后选定一个目标 rig 姿态并冻结；为所有依赖资产提供同一版本的零差参考。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Pose` — 约束

作用：固定基底、站蹲/Lean 零参考。生成候选后选定一个目标 rig 姿态并冻结；为所有依赖资产提供同一版本的零差参考。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Run_BasePose` — 约束

作用：固定基底、站蹲/Lean 零参考。生成候选后选定一个目标 rig 姿态并冻结；为所有依赖资产提供同一版本的零差参考。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_FallLoop` — 约束

作用：正常/快速下落循环。制作空中姿态循环；可生成悬空/下落候选，再剥离全局坠落轨迹，不能施加地面锁脚。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_FallLoop_Fast` — 约束

作用：正常/快速下落循环。制作空中姿态循环；可生成悬空/下落候选，再剥离全局坠落轨迹，不能施加地面锁脚。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Flail` — 约束

作用：ragdoll 空中摆动/恢复辅助。生成可循环的悬空四肢摆动，幅度受关节及物理混合限制；不锁脚，不用动作根轨迹驱动胶囊。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Stop_L_Down` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Stop_R_Down` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Transition_L` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Transition_R` — 约束

作用：左右脚停止、静止脚距纠正与动态切换。相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_Walk_B` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, -1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_Walk_F` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_Walk_L` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[1, 0]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_Walk_R` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-1, 0]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CRF_Walk_B` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, -1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CRF_Walk_F` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CRF_Walk_L` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[1, 0]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CRF_Walk_R` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-1, 0]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Run_B` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, -1]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Run_F` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Run_LB` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, -0.707107]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Run_LF` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, 0.707107]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Run_RB` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, -0.707107]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Run_RF` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, 0.707107]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Sprint_F` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Walk_B` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, -1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Walk_F` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Walk_LB` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, -0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Walk_LF` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0.707107, 0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Walk_RB` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, -0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Walk_RF` — 约束

作用：常速站/蹲/持枪方向移动循环。先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[-0.707107, 0.707107]，v 起始设计值 1.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_WalkPose` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CRF_WalkPose` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_RunPose_F` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_RunPose_L` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_RunPose_R` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_WalkPose_F` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_WalkPose_L` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_WalkPose_R` — 约束

作用：步幅/方向混合的参考姿态样本。从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_GetUp_Back` — 约束

作用：仰卧/俯卧起身。从真实恢复起始姿态连接蹲伏/站立终点，支撑手脚接触及根/胶囊恢复路径明确。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 姿态恢复起点（仰卧/俯卧）和规定站蹲终点；支撑手/脚 contact；不能混淆 Back/Front。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_GetUp_Front` — 约束

作用：仰卧/俯卧起身。从真实恢复起始姿态连接蹲伏/站立终点，支撑手脚接触及根/胶囊恢复路径明确。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 姿态恢复起点（仰卧/俯卧）和规定站蹲终点；支撑手/脚 contact；不能混淆 Back/Front。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_SecondaryMotion` — 约束

作用：呼吸/轻微重心变化。相对同一站姿的局部加法循环；小幅运动、首尾姿态及一阶速度连续，避免恒定肩髋偏移。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 固定 XZ 与 heading；fullbody 起止采用同一 standing zero pose。
- end-effector: 稀疏双脚支撑目标；连续锁脚由后处理完成。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 相对当前站姿生成 local additive；去恒定姿态偏移；首尾位置/四元数和速度联合闭合。
- 禁止对 BasePose 与 SecondaryMotion 独立自由抽样后仅复制 additive metadata。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_JumpLoop` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_JumpRun_LF` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_JumpRun_RF` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_JumpWalk_LF` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_JumpWalk_RF` — 约束

作用：走/跑起跳及空中循环。按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Land_Heavy` — 约束

作用：轻/重着地反应。接触时刻、骨盆缓冲和末 standing pose 明确；非加法与加法版本共享表演/接触事件。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Land_Light` — 约束

作用：轻/重着地反应。接触时刻、骨盆缓冲和末 standing pose 明确；非加法与加法版本共享表演/接触事件。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Land_Heavy_Additive` — 约束

作用：可叠加的着地反应。从匹配的 full-pose 着地表演按 exact basePoseType/baseFrame 转换，剔除全局飞行，不能独立生成另一段着地。
生产方式：`derive_from_paired_full_pose_master`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。
- 与非加法 Land 同源；ABPT_AnimFrame/ABPT_AnimScaled 分别依原评价策略求差，末态残差收敛。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Land_Light_Additive` — 约束

作用：可叠加的着地反应。从匹配的 full-pose 着地表演按 exact basePoseType/baseFrame 转换，剔除全局飞行，不能独立生成另一段着地。
生产方式：`derive_from_paired_full_pose_master`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。
- 与非加法 Land 同源；ABPT_AnimFrame/ABPT_AnimScaled 分别依原评价策略求差，末态残差收敛。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Falling_LeanPose_0` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Falling_LeanPose_B` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Falling_LeanPose_F` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Falling_LeanPose_L` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Falling_LeanPose_R` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LeanPose_0` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LeanPose_B` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LeanPose_F` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LeanPose_L` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LeanPose_R` — 约束

作用：地面/空中二维倾斜修正。相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- 方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- 用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Look_D_Sweep` — 约束

作用：基础观察 yaw×pitch 参数域。F/D/U 是固定俯仰行，时间对应 yaw；同一基础姿态/根位置，逐参数目标后组装 mesh-space additive。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- 目标 pitch 行=-90°；time u 的 yaw 参数=-180+360*u，中心 u=.5 必须与共同前视 base 对齐；参数不是 root heading。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。

验收：

- pitch=0/yaw=0 零差；三行相同 yaw 时姿态连续，端点解剖极限不可用扭曲髋膝补偿。
- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Look_F_Sweep` — 约束

作用：基础观察 yaw×pitch 参数域。F/D/U 是固定俯仰行，时间对应 yaw；同一基础姿态/根位置，逐参数目标后组装 mesh-space additive。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- 目标 pitch 行=0°；time u 的 yaw 参数=-180+360*u，中心 u=.5 必须与共同前视 base 对齐；参数不是 root heading。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。

验收：

- pitch=0/yaw=0 零差；三行相同 yaw 时姿态连续，端点解剖极限不可用扭曲髋膝补偿。
- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Look_U_Sweep` — 约束

作用：基础观察 yaw×pitch 参数域。F/D/U 是固定俯仰行，时间对应 yaw；同一基础姿态/根位置，逐参数目标后组装 mesh-space additive。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- 目标 pitch 行=90°；time u 的 yaw 参数=-180+360*u，中心 u=.5 必须与共同前视 base 对齐；参数不是 root heading。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。

验收：

- pitch=0/yaw=0 零差；三行相同 yaw 时姿态连续，端点解剖极限不可用扭曲髋膝补偿。
- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Mantle_1m_LH` — 约束

作用：低/高翻越及左右手变体。人体相对平台目标生成，约束手触边/脚登顶/末站姿；按现有 Mantle motion source 的局部空间和 correction 曲线制作。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 起始 approach、手触边、身体越边、脚登顶、同版本终点姿态。
- end-effector: 指定 left/right 或双手 ledge targets；root2d 只控水平，竖直高度由 fullbody root_positions 的 Y 控制。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按本项目 Mantle target-relative motion source、start offset/correction curves 重制；不直接把 BVH Hips trajectory 当 runtime root。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Mantle_1m_RH` — 约束

作用：低/高翻越及左右手变体。人体相对平台目标生成，约束手触边/脚登顶/末站姿；按现有 Mantle motion source 的局部空间和 correction 曲线制作。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 起始 approach、手触边、身体越边、脚登顶、同版本终点姿态。
- end-effector: 指定 left/right 或双手 ledge targets；root2d 只控水平，竖直高度由 fullbody root_positions 的 Y 控制。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按本项目 Mantle target-relative motion source、start offset/correction curves 重制；不直接把 BVH Hips trajectory 当 runtime root。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Mantle_2m` — 约束

作用：低/高翻越及左右手变体。人体相对平台目标生成，约束手触边/脚登顶/末站姿；按现有 Mantle motion source 的局部空间和 correction 曲线制作。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody: 起始 approach、手触边、身体越边、脚登顶、同版本终点姿态。
- end-effector: 指定 left/right 或双手 ledge targets；root2d 只控水平，竖直高度由 fullbody root_positions 的 Y 控制。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按本项目 Mantle target-relative motion source、start offset/correction curves 重制；不直接把 BVH Hips trajectory 当 runtime root。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Barrel_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Binoculars_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Bow_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Box_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Pistol_1H_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Pistol_2H_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Torch_Poses` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_StanceVariation_Feminine` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_StanceVariation_HandsTied` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_StanceVariation_Injured` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_StanceVariation_Masculine` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_StanceVariation_Normal` — 约束

作用：Overlay 按帧读取的状态姿态表。逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。
生产方式：`pose_candidates_then_extract_or_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: 固定 XZ=(0,0) 与 heading=0。
- fullbody: shared pose reference 的起止/中间关键目标。

后处理：

- 在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。
- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Binoculars_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Bow_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Pistol_1H_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Pistol_2H_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Torch_Poses_NoCurves` — 约束

作用：无曲线的 additive 参考姿态表。从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。

验收：

- 所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Binoculars_Aim_Sweep` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Binoculars_Aim_Sweep_Crouched` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Bow_Aim_Sweep` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Bow_Aim_Sweep_Crouched` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Aim_Sweep` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Aim_Sweep_Crouched` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Pistol_1H_Aim_Sweep` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Pistol_1H_Aim_Sweep_Crouched` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Pistol_2H_Aim_Sweep` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Pistol_2H_Aim_Sweep_Crouched` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Torch_Aim_Sweep` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_Torch_Aim_Sweep_Crouched` — 约束

作用：持物站蹲瞄准 pitch 参数轴。时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。
生产方式：`parameter_pose_samples_then_pack`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- root2d: XZ 固定，朝向不随瞄准变动。
- fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。
- u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。
- 中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `Bow_Draw` — 约束

作用：弓道具骨架变形。独立 Bow_Skeleton 的拉弦；由人体手部/拉弦参数驱动道具 rig，Kimodo 不输出弓实体骨。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：


验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Mannequin_T_Pose` — 约束

作用：骨骼参考/绑定姿态。确定性 rig 资源，不调用 Kimodo；T pose 是骨架合同，不能由随机站姿代替。
生产方式：`deterministic_derive`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- 不调用 Kimodo。

后处理：


验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_LandRoll_F` — 约束

作用：翻滚动作/落地翻滚。明确初始接触、滚动身体碰撞和末姿态；根轨迹仅由现有 Root Motion owner 消费一次，通知与碰撞窗口跟随生成表演。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody + root2d: 入滚/肩背接触/出滚及末 standing pose；无持续脚锁，目标 rig 碰撞需独立检查。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_Rotate_L90` — 约束

作用：瞄准/摄像机持续旋转时的踏步。原地连续踏步的循环；去累计 heading 后局部姿态闭合，转速/转向交给原 runtime 和曲线。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_Rotate_R90` — 约束

作用：瞄准/摄像机持续旋转时的踏步。原地连续踏步的循环；去累计 heading 后局部姿态闭合，转速/转向交给原 runtime 和曲线。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Rotate_L90` — 约束

作用：瞄准/摄像机持续旋转时的踏步。原地连续踏步的循环；去累计 heading 后局部姿态闭合，转速/转向交给原 runtime 和曲线。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Rotate_R90` — 约束

作用：瞄准/摄像机持续旋转时的踏步。原地连续踏步的循环；去累计 heading 后局部姿态闭合，转速/转向交给原 runtime 和曲线。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_Sprint_F_Impulse` — 约束

作用：冲刺加速/Impulse 起步。从对应移动相位进入 sprint；根路径加速、末姿态与 sprint 同相位；不可随机生成后硬压缩到 0.6 秒。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- root2d 速度连续递增到 sprint master；结束关键帧采用其相同相位。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_to_N` — 约束

作用：站到蹲/蹲到站过渡。从固定 Stand/Crouch 同源姿态开始结束；脚锚保持，骨盆高度变化符合胶囊切换。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody 起止使用同版本 Stand_Pose/Crouch_Pose；双脚锚不改变，骨盆高度不可来自随意 standing idle。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_to_CLF` — 约束

作用：站到蹲/蹲到站过渡。从固定 Stand/Crouch 同源姿态开始结束；脚锚保持，骨盆高度变化符合胶囊切换。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- fullbody 起止使用同版本 Stand_Pose/Crouch_Pose；双脚锚不改变，骨盆高度不可来自随意 standing idle。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_TurnIP_L180` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到180°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_TurnIP_L90` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_TurnIP_R180` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-180°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_CLF_TurnIP_R90` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_TurnIP_L180` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到180°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_TurnIP_L90` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_TurnIP_R180` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-180°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_N_TurnIP_R90` — 约束

作用：90/180 左右 Turn-in-place。root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: 路径 XZ 固定，heading 从0到-90°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。

验收：

- additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Run_F` — 约束

作用：Rifle 的整身/手臂移动资源。Run/Sprint 全身与 Arms 必须来自同一生成结果和同一相位；手臂版本按已验证分层合同派生。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Run_F_Arms` — 约束

作用：Rifle 的整身/手臂移动资源。Run/Sprint 全身与 Arms 必须来自同一生成结果和同一相位；手臂版本按已验证分层合同派生。
生产方式：`derive_from_paired_full_body_master`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 3.5 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Sprint_F` — 约束

作用：Rifle 的整身/手臂移动资源。Run/Sprint 全身与 Arms 必须来自同一生成结果和同一相位；手臂版本按已验证分层合同派生。
生产方式：`generate_motion_candidates`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Sprint_F_Arms` — 约束

作用：Rifle 的整身/手臂移动资源。Run/Sprint 全身与 Arms 必须来自同一生成结果和同一相位；手臂版本按已验证分层合同派生。
生产方式：`derive_from_paired_full_body_master`；循环闭合要求：是。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。
- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Sprint_F_Impulse` — 约束

作用：Rifle sprint impulse 配对资源。与通用 sprint 起步同相位/时长；Arms 与全身同源，并维持双手 grip。
生产方式：`generate_motion_candidates`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- root2d 速度连续递增到 sprint master；结束关键帧采用其相同相位。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。

### v4 `ALS_Props_M4A1_Sprint_F_Impulse_Arms` — 约束

作用：Rifle sprint impulse 配对资源。与通用 sprint 起步同相位/时长；Arms 与全身同源，并维持双手 grip。
生产方式：`derive_from_paired_full_body_master`；循环闭合要求：按图状态连接，非强制循环。

Kimodo / rig 目标：

- fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。
- root2d: XZ(t)=v*t*[0, 1]，v 起始设计值 6.0 m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。
- fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。
- root2d 速度连续递增到 sprint master；结束关键帧采用其相同相位。
- end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。

后处理：

- 按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。
- 保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。
- 制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。
- Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。

验收：

- 检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。
- 在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。


## 姿态表读取与加法参考

这里只列实际引用；未列出的帧仍保留原打包索引，生成前应检查其曲线、分数时间采样及未来用途。

### v4 `ALS_Props_Bow_Poses`

原始 12 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_Props_Pistol_1H_Poses_NoCurves`

原始 10 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：ALS_Props_Pistol_1H_Aim_Sweep_Crouched → frame 9；ALS_Props_Pistol_1H_Aim_Sweep → frame 5

### v4 `ALS_Props_M4A1_Poses_NoCurves`

原始 13 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：ALS_Props_M4A1_Aim_Sweep_Crouched → frame 9；ALS_Props_M4A1_Aim_Sweep → frame 5

### v4 `ALS_Props_Binoculars_Poses_NoCurves`

原始 7 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：ALS_Props_Binoculars_Aim_Sweep_Crouched → frame 6；ALS_Props_Binoculars_Aim_Sweep → frame 3

### v4 `ALS_Props_Torch_Poses`

原始 7 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_Props_Barrel_Poses`

原始 4 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_Props_Binoculars_Poses`

原始 7 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_Props_Pistol_2H_Poses`

原始 11 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_Props_Box_Poses`

原始 4 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_StanceVariation_Masculine`

原始 4 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_Props_Pistol_2H_Poses_NoCurves`

原始 11 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：ALS_Props_Pistol_2H_Aim_Sweep → frame 6；ALS_Props_Pistol_2H_Aim_Sweep_Crouched → frame 10

### v4 `ALS_Props_Pistol_1H_Poses`

原始 10 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_Props_Torch_Poses_NoCurves`

原始 7 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：ALS_Props_Torch_Aim_Sweep → frame 3；ALS_Props_Torch_Aim_Sweep_Crouched → frame 6

### v4 `ALS_Props_Bow_Poses_NoCurves`

原始 12 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：ALS_Props_Bow_Aim_Sweep_Crouched → frame 11；ALS_Props_Bow_Aim_Sweep → frame 7

### v4 `ALS_StanceVariation_Injured`

原始 4 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_StanceVariation_Normal`

原始 4 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_Props_M4A1_Poses`

原始 13 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_StanceVariation_Feminine`

原始 4 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### v4 `ALS_StanceVariation_HandsTied`

原始 4 keys；插值 1。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：无清单内引用。

### refactored `A_Als_Binoculars_Poses`

原始 7 keys；插值 Step。
图固定读取：frame 0: Overlay；frame 2: Overlay；frame 3: Overlay；frame 4: Overlay；frame 5: Overlay
加法引用：无清单内引用。

### refactored `A_Als_Binoculars_Poses_NoCurves`

原始 7 keys；插值 Step。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：A_Als_Binoculars_Aim → frame 3；A_Als_Binoculars_Aim_Crouch → frame 6

### refactored `A_Als_Bow_Poses`

原始 12 keys；插值 Step。
图固定读取：frame 0: Overlay, Relaxed；frame 1: Relaxed；frame 2: Relaxed；frame 3: Overlay, Relaxed；frame 4: Ready；frame 5: Ready；frame 6: Aiming；frame 7: Aiming；frame 8: Overlay, Relaxed；frame 9: Ready；frame 10: Aiming
加法引用：无清单内引用。

### refactored `A_Als_Bow_Poses_NoCurves`

原始 12 keys；插值 Step。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：A_Als_Bow_Aim → frame 7；A_Als_Bow_Aim_Crouch → frame 11

### refactored `A_Als_Barrel_Poses`

原始 4 keys；插值 Step。
图固定读取：frame 0: Overlay；frame 1: Overlay；frame 2: Overlay
加法引用：无清单内引用。

### refactored `A_Als_Box_Poses`

原始 4 keys；插值 Step。
图固定读取：frame 0: Overlay；frame 1: Overlay；frame 2: Overlay
加法引用：无清单内引用。

### refactored `A_Als_Default_Poses`

原始 4 keys；插值 Step。
图固定读取：frame 0: Overlay；frame 1: Overlay；frame 2: Overlay
加法引用：无清单内引用。

### refactored `A_Als_Feminine_Poses`

原始 4 keys；插值 Step。
图固定读取：frame 0: Overlay；frame 1: Overlay；frame 2: Overlay
加法引用：无清单内引用。

### refactored `A_Als_HandsTied_Poses`

原始 4 keys；插值 Step。
图固定读取：frame 0: Overlay；frame 1: Overlay；frame 2: Overlay
加法引用：无清单内引用。

### refactored `A_Als_Injured_Poses`

原始 4 keys；插值 Step。
图固定读取：frame 0: Overlay；frame 1: Overlay；frame 2: Overlay
加法引用：无清单内引用。

### refactored `A_Als_Masculine_Poses`

原始 4 keys；插值 Step。
图固定读取：frame 0: Overlay；frame 1: Overlay；frame 2: Overlay
加法引用：无清单内引用。

### refactored `A_Als_PistolOneHanded_Poses`

原始 10 keys；插值 Step。
图固定读取：frame 0: Relaxed；frame 1: Relaxed；frame 2: Ready；frame 3: Overlay, Ready；frame 4: Aiming；frame 5: Aiming；frame 6: Relaxed；frame 7: Overlay, Ready；frame 8: Aiming
加法引用：无清单内引用。

### refactored `A_Als_PistolOneHanded_Poses_NoCurves`

原始 10 keys；插值 Step。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：A_Als_PistolOneHanded_Aim → frame 5；A_Als_PistolOneHanded_Aim_Crouch → frame 9

### refactored `A_Als_PistolTwoHanded_Poses`

原始 11 keys；插值 Step。
图固定读取：frame 0: Relaxed；frame 1: Relaxed；frame 2: Overlay, Relaxed；frame 3: Overlay, Ready；frame 4: Ready；frame 5: Aiming；frame 6: Aiming；frame 7: Overlay, Relaxed；frame 8: Ready；frame 9: Aiming
加法引用：无清单内引用。

### refactored `A_Als_PistolTwoHanded_Poses_NoCurves`

原始 11 keys；插值 Step。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：A_Als_PistolTwoHanded_Aim → frame 6；A_Als_PistolTwoHanded_Aim_Crouch → frame 10

### refactored `A_Als_Rifle_Poses`

原始 13 keys；插值 Step。
图固定读取：frame 0: Relaxed；frame 1: Relaxed；frame 2: Ready；frame 3: Ready；frame 4: Aiming；frame 5: Aiming；frame 6: Relaxed；frame 7: Overlay, Ready；frame 8: Aiming；frame 9: Overlay, Relaxed；frame 10: Relaxed；frame 11: Overlay
加法引用：无清单内引用。

### refactored `A_Als_Rifle_Poses_NoCurves`

原始 13 keys；插值 Step。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：A_Als_Rifle_Aim → frame 5；A_Als_Rifle_Aim_Crouch → frame 9

### refactored `A_Als_Torch_Poses`

原始 7 keys；插值 Step。
图固定读取：frame 0: Overlay；frame 2: Overlay；frame 3: Overlay；frame 4: Overlay；frame 5: Overlay
加法引用：无清单内引用。

### refactored `A_Als_Torch_Poses_NoCurves`

原始 7 keys；插值 Step。
图固定读取：见 sourceBindings / V4 authored graph 的时间/表达式采样。
加法引用：A_Als_Torch_Aim → frame 3；A_Als_Torch_Aim_Crouch → frame 6

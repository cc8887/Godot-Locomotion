"""Read-only inventory analysis; writes proposed Kimodo contracts, never animation assets.

The output is a design specification with symbolic pose/prop references, not a
Kimodo --constraints file. Every row keeps its source namespace and evidence hash.
"""
from __future__ import annotations

import hashlib
import json
import re
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / 'assets/config'
OUTPUT = ROOT / 'docs/architecture'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def evidence(path):
    return {'file':path.relative_to(ROOT).as_posix(), 'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}

V4_ALIASES = {
    'ALS_N_Pose':'Stand_Pose','ALS_CLF_Pose':'Crouch_Pose',
    'ALS_N_Run_BasePose':'Lean_Pose','ALS_N_SecondaryMotion':'Idle',
    'ALS_Flail':'Flail','ALS_Mannequin_T_Pose':'Reference_T_Pose','Bow_Draw':'Prop_Bow_Draw',
    'ALS_N_FallLoop':'Fall','ALS_N_FallLoop_Fast':'Fall_Fast',
    'ALS_N_JumpLoop':'Jump_Loop','ALS_N_LandRoll_F':'Roll',
    'ALS_N_Mantle_1m_LH':'Mantle_Low_Left','ALS_N_Mantle_1m_RH':'Mantle_Low_Right','ALS_N_Mantle_2m':'Mantle_High',
    'ALS_N_to_CLF':'StandToCrouch','ALS_CLF_to_N':'CrouchToStand',
}
DIR={'F':'Forward','B':'Backward','L':'Left','R':'Right','LF':'Left_Forward','LB':'Left_Backward','RF':'Right_Forward','RB':'Right_Backward','0':''}

def canonical(name):
    if name.startswith('A_Als_'):return name[6:]
    if name in V4_ALIASES:return V4_ALIASES[name]
    if name.startswith('ALS_StanceVariation_'):return name.replace('ALS_StanceVariation_','')+'_Poses'
    if name.startswith('ALS_Props_'):
        value=name[len('ALS_Props_'):]
        for a,b in [('Pistol_1H','PistolOneHanded'),('Pistol_2H','PistolTwoHanded'),('M4A1','Rifle')]:value=value.replace(a,b)
        return value.replace('Aim_Sweep_Crouched','Aim_Crouch').replace('Aim_Sweep','Aim').replace('Sprint_F_Impulse','Sprint_Impulse').replace('Sprint_F','Sprint').replace('Run_F','Run')
    for prefix,group in [('ALS_N_Falling_LeanPose_','Air_Lean'),('ALS_N_LeanPose_','Lean'),('ALS_N_LocoDetail_Accel_','Acceleration')]:
        if name.startswith(prefix):
            direction=DIR[name[len(prefix):]]
            return group+('_'+direction if direction else '')
    for p,st in [('ALS_N_',''),('ALS_CLF_','Crouch_'),('ALS_CRF_','Crouch_Rifle_')]:
        if not name.startswith(p):continue
        n=name[len(p):]
        if n.startswith('JumpRun_') or n.startswith('JumpWalk_'):
            return ('Jump_Run_' if n.startswith('JumpRun_') else 'Jump_Walk_')+('Left' if n.endswith('LF') else 'Right')
        if n.startswith('Look_'):return 'Look_'+{'F':'Forward','D':'Down','U':'Up'}[n.split('_')[1]]
        if n.startswith('TurnIP_'):
            match=re.fullmatch('TurnIP_([LR])(90|180)',n);return st+'Turn_'+match[2]+'_'+DIR[match[1]]
        if n.startswith('Rotate_'):
            match=re.fullmatch('Rotate_([LR])90',n);return st+'Rotate_90_'+DIR[match[1]]
        if n.startswith('Stop_'):return 'Stop_'+DIR[n.split('_')[1]]
        if n.startswith('Transition_'):return st+'Transition_'+DIR[n.split('_')[1]]
        if n.startswith('GetUp_'):return 'GetUp_'+n.split('_')[1]
        if n.startswith('Land_'):return n
        if n=='Sprint_F_Impulse':return 'Sprint_Acceleration'
        if n=='Sprint_F':return 'Sprint'
        if n.startswith('WalkPose_'):return 'Walk_'+DIR[n.split('_')[1]]+'_Pose'
        if n.startswith('RunPose_'):return 'Run_'+DIR[n.split('_')[1]]+'_Pose'
        if n=='WalkPose':return st+'Walk_Pose'
        if n.startswith('Walk_') or n.startswith('Run_'):
            gait,d=n.split('_');return st+gait+'_'+DIR[d]
    raise ValueError('Unclassified V4 name: '+name)

FAMILIES = {
 'reference':('骨骼参考/绑定姿态', '确定性 rig 资源，不调用 Kimodo；T pose 是骨架合同，不能由随机站姿代替。'),
 'prop_animation':('弓道具骨架变形', '独立 Bow_Skeleton 的拉弦；由人体手部/拉弦参数驱动道具 rig，Kimodo 不输出弓实体骨。'),
 'base_pose':('固定基底、站蹲/Lean 零参考', '生成候选后选定一个目标 rig 姿态并冻结；为所有依赖资产提供同一版本的零差参考。'),
 'idle_additive':('呼吸/轻微重心变化', '相对同一站姿的局部加法循环；小幅运动、首尾姿态及一阶速度连续，避免恒定肩髋偏移。'),
 'pose_table':('Overlay 按帧读取的状态姿态表', '逐 frame 生成/编辑静态目标后按原索引打包；每个 frame 是状态，时间不是连续动作。'),
 'pose_table_copy':('无曲线的 additive 参考姿态表', '从对应 Poses 精确复制全部骨姿态与时序，仅去除曲线；禁止单独随机生成。'),
 'gait_pose':('步幅/方向混合的参考姿态样本', '从对应 gait master 选定参考姿态，保持图的采样时域；原35键 WalkPose 已核实为静态，不按键数生成步行。其他条目制作前检查原轨道与 sampler。'),
 'gait_cycle':('常速站/蹲/持枪方向移动循环', '先生成带位移的自然步态，以 root 路径规定方向和速度；切出完整步态周期并去全局位移，再制作接触/同步元数据。'),
 'sprint_start':('冲刺加速/Impulse 起步', '从对应移动相位进入 sprint；根路径加速、末姿态与 sprint 同相位；不可随机生成后硬压缩到 0.6 秒。'),
 'lean_additive':('地面/空中二维倾斜修正', '相对指定 baseAsset/baseFrame 制作固定方向残差；neutral 为零差，非相关关节保持基底，不生成一整段方向移动。'),
 'acceleration_additive':('加速度驱动的身体动态细节', '沿图输入参数的各个样本制作修正，所有方向共享基底；用原 sampler/图确认是姿态轴还是播放，不把时间随意当启动过程。'),
 'look_grid':('基础观察 yaw×pitch 参数域', 'F/D/U 是固定俯仰行，时间对应 yaw；同一基础姿态/根位置，逐参数目标后组装 mesh-space additive。'),
 'prop_aim':('持物站蹲瞄准 pitch 参数轴', '时间对应俯仰而非真实播放进度；围绕匹配 Poses_NoCurves 的精确 baseFrame，约束手部和道具 socket。'),
 'weapon_cycle':('Rifle 的整身/手臂移动资源', 'Run/Sprint 全身与 Arms 必须来自同一生成结果和同一相位；手臂版本按已验证分层合同派生。'),
 'weapon_start':('Rifle sprint impulse 配对资源', '与通用 sprint 起步同相位/时长；Arms 与全身同源，并维持双手 grip。'),
 'jump':('走/跑起跳及空中循环', '按左/右起跳相位成组制作，连接对应 gait 和空中基底；运行时物理负责轨迹，动画不可再次把全局飞行位移作用到网格。'),
 'fall':('正常/快速下落循环', '制作空中姿态循环；可生成悬空/下落候选，再剥离全局坠落轨迹，不能施加地面锁脚。'),
 'land':('轻/重着地反应', '接触时刻、骨盆缓冲和末 standing pose 明确；非加法与加法版本共享表演/接触事件。'),
 'land_additive':('可叠加的着地反应', '从匹配的 full-pose 着地表演按 exact basePoseType/baseFrame 转换，剔除全局飞行，不能独立生成另一段着地。'),
 'roll':('翻滚动作/落地翻滚', '明确初始接触、滚动身体碰撞和末姿态；根轨迹仅由现有 Root Motion owner 消费一次，通知与碰撞窗口跟随生成表演。'),
 'mantle':('低/高翻越及左右手变体', '人体相对平台目标生成，约束手触边/脚登顶/末站姿；按现有 Mantle motion source 的局部空间和 correction 曲线制作。'),
 'getup':('仰卧/俯卧起身', '从真实恢复起始姿态连接蹲伏/站立终点，支撑手脚接触及根/胶囊恢复路径明确。'),
 'turn':('90/180 左右 Turn-in-place', 'root2d 固定位置+heading 目标，稀疏支撑脚/姿态约束；生成后拆分 gameplay actor yaw 与局部 pose，重制 RotationAmount。'),
 'rotate':('瞄准/摄像机持续旋转时的踏步', '原地连续踏步的循环；去累计 heading 后局部姿态闭合，转速/转向交给原 runtime 和曲线。'),
 'stance_transition':('站到蹲/蹲到站过渡', '从固定 Stand/Crouch 同源姿态开始结束；脚锚保持，骨盆高度变化符合胶囊切换。'),
 'foot_transition':('左右脚停止、静止脚距纠正与动态切换', '相位/支撑脚和目标脚锚按 Stop/Transition/DynamicTransition 的调用分别设定；使用精确 additive 参考，不能把所有资源当普通收步。'),
 'flail':('ragdoll 空中摆动/恢复辅助', '生成可循环的悬空四肢摆动，幅度受关节及物理混合限制；不锁脚，不用动作根轨迹驱动胶囊。'),
}

def family(n):
    if n=='Reference_T_Pose':return 'reference'
    if n=='Prop_Bow_Draw':return 'prop_animation'
    if n.endswith('_Poses_NoCurves'):return 'pose_table_copy'
    if n.endswith('_Poses'):return 'pose_table'
    if n in ('Stand_Pose','Crouch_Pose','Lean_Pose'):return 'base_pose'
    if n=='Idle':return 'idle_additive'
    if n.startswith('Look_'):return 'look_grid'
    if '_Aim' in n:return 'prop_aim'
    if n.startswith('Air_Lean') or n=='Lean' or n.startswith('Lean_'):return 'lean_additive'
    if n.startswith('Acceleration_'):return 'acceleration_additive'
    if n.startswith('Rifle_') and ('Run' in n or 'Sprint' in n):return 'weapon_start' if 'Impulse' in n else 'weapon_cycle'
    if n=='Sprint_Acceleration':return 'sprint_start'
    if 'Jump_' in n:return 'jump'
    if n.startswith('Fall'):return 'fall'
    if n.startswith('Land_'):return 'land_additive' if 'Additive' in n else 'land'
    if n=='Roll':return 'roll'
    if n.startswith('Mantle_'):return 'mantle'
    if n.startswith('GetUp_'):return 'getup'
    if '_Turn_' in n or n.startswith('Turn_'):return 'turn'
    if '_Rotate_' in n or n.startswith('Rotate_'):return 'rotate'
    if n in ('StandToCrouch','CrouchToStand'):return 'stance_transition'
    if 'Transition_' in n or n.startswith('Stop_'):return 'foot_transition'
    if n=='Flail':return 'flail'
    if n.endswith('_Pose'):return 'gait_pose'
    if any(s in n for s in ('Walk_','Run_')) or n=='Sprint':return 'gait_cycle'
    raise ValueError('Unclassified semantic asset: '+n)

def prop(n):
    for tag in ('PistolOneHanded','PistolTwoHanded','Rifle','Binoculars','Bow','Torch','Barrel','Box','HandsTied','Injured','Feminine','Masculine','Default','Normal'):
        if tag in n:return tag
    return None

def direction(n):
    # SOMA native zero heading: LeftLeg has +X, RightLeg has -X.
    for label,vec in [('Left_Backward',[.707107,-.707107]),('Right_Backward',[-.707107,-.707107]),
                      ('Left_Forward',[.707107,.707107]),('Right_Forward',[-.707107,.707107]),
                      ('Forward',[0,1]),('Backward',[0,-1]),('Left',[1,0]),('Right',[-1,0])]:
        if label in n:return label,vec
    return 'Forward',[0,1]

def prompt(n,f):
    if f=='land_additive' or (f in ('weapon_cycle','weapon_start') and 'Arms' in n):return None
    direction_text=direction(n)[0].replace('_',' ').lower()
    obj=prop(n)
    hold={'Rifle':'holding a rifle with both hands','PistolOneHanded':'holding a pistol in the right hand',
          'PistolTwoHanded':'holding a pistol with both hands','Binoculars':'holding binoculars',
          'Bow':'holding a bow','Torch':'holding a torch','Box':'carrying a box with both hands','Barrel':'carrying a barrel with both hands'}.get(obj,'')
    p=''
    if f=='base_pose':p='A person holds a stable crouching pose with both feet planted.' if n=='Crouch_Pose' else 'A person holds a balanced running reference pose without translating.' if n=='Lean_Pose' else 'A person holds a stable balanced standing pose with relaxed arms.'
    elif f=='idle_additive':p='A person stands still with subtle breathing, keeping both feet planted and the arms relaxed.'
    elif f=='pose_table':
        style={'Feminine':'with a feminine posture','Masculine':'with a masculine posture','HandsTied':'with both wrists held together behind the back','Injured':'with a guarded injured posture'}.get(obj,'')
        p=f'A person holds a stable {{stance}} pose {hold or style}.'
    elif f=='gait_pose':p=f'A person holds a stable {"walking" if "Walk" in n else "running"} reference posture for moving {direction_text}, keeping the torso facing forward.'
    elif f=='gait_cycle':
        gait='sprints' if n=='Sprint' else 'runs' if 'Run' in n else 'walks'
        stance='in a crouch' if 'Crouch' in n else ''
        hold=hold or ('holding a rifle with both hands' if 'Rifle' in n else '')
        p=f'A person {gait} {direction_text} at a steady pace {stance} {hold}, keeping the torso facing forward.'
    elif f=='sprint_start':p='A person running forward accelerates into a sprint with coordinated arm swings.'
    elif f in ('weapon_cycle','weapon_start'):p='A person '+('accelerates forward into a sprint' if f=='weapon_start' else 'sprints forward at a steady pace' if 'Sprint' in n else 'runs forward at a steady pace')+' while holding a rifle with both hands.'
    elif f=='lean_additive':p='A person holds a stable '+('airborne' if 'Air' in n else 'running')+' pose'+(' while leaning '+direction_text if n not in ('Lean','Air_Lean') else ' without leaning to either side')+'.'
    elif f=='acceleration_additive':p='A person adjusts their running posture while accelerating '+direction_text+'.'
    elif f=='look_grid':p='A person standing still slowly turns their gaze from left to right while looking '+{'Look_Forward':'horizontally','Look_Up':'upward','Look_Down':'downward'}[n]+'.'
    elif f=='prop_aim':p=f'A person {"crouching" if "Crouch" in n else "standing"} {hold} slowly aims from upward to downward.'
    elif f=='jump':
        p='A person remains airborne in a compact balanced jumping pose.' if n=='Jump_Loop' else 'A person '+('running' if 'Run' in n else 'walking')+' forward jumps from a '+('left' if 'Left' in n else 'right')+' foot lead phase.'
    elif f=='fall':p='A person falls through the air with '+('a spread protective posture.' if 'Fast' in n else 'a relaxed bent-knee posture.')
    elif f in ('land','land_additive'):p='A person lands on both feet '+('heavily and absorbs the impact with bent knees.' if 'Heavy' in n else 'softly and settles into a balanced stance.')
    elif f=='roll':p='A person rolls forward over a shoulder and returns to a balanced standing stance.'
    elif f=='mantle':
        p='A person climbs onto a high ledge using both hands.' if 'High' in n else f'A person steps over a low ledge using the {"left" if "Left" in n else "right"} hand for support.'
    elif f=='getup':p='A person lying '+('on their back' if 'Back' in n else 'face down')+' gets up into a balanced standing stance.'
    elif f in ('turn','rotate'):
        deg='180' if '180' in n else '90';left='left' if 'Left' in n else 'right';stance='while crouching' if 'Crouch' in n else ''
        p=f'A person turns {deg} degrees to the {left} by stepping in place {stance}.'
    elif f=='stance_transition':p='A person '+('lowers from standing into a stable crouch' if n=='StandToCrouch' else 'rises from a stable crouch to standing')+' while keeping both feet planted.'
    elif f=='foot_transition':
        p=f'A person {"crouching" if "Crouch" in n else "standing"} makes a small corrective step with the {"left" if "Left" in n else "right"} foot and settles into a balanced stance.' if not n.startswith('Stop_') else f'A person walking forward comes to a balanced stop with the {"left" if "Left" in n else "right"} foot in the designated support phase.'
    elif f=='flail':p='A person falling through the air moves their arms and legs in a controlled flailing motion.'
    return re.sub(r' +',' ',p).replace(' .','.').strip() or None

def recipe(n,f,timing,evaluation):
    duration=timing.get('playLength')
    rules=['共享 character_style、目标骨架、bind/rest pose、根朝向、鞋底/手部 socket 和确定的 BasePose 版本。',
           '在 SOMA Y-up 米制空间生成，目标 rig 上重新验收；Kimodo 接触预测不是最终鞋底接触证据。']
    controls=[];post=[];checks=[]
    required_loop=f in ('idle_additive','gait_cycle','weapon_cycle','fall','flail','rotate') or n=='Jump_Loop'
    if f in ('reference','prop_animation','pose_table_copy'):
        controls=['不调用 Kimodo。'];generation={'method':'deterministic_derive','seconds':None}
    elif f in ('base_pose','pose_table','gait_pose','lean_additive'):
        generation={'method':'pose_candidates_then_extract_or_derive','candidate_seconds':[1,2]}
        controls=['root2d: 固定 XZ=(0,0) 与 heading=0。', 'fullbody: shared pose reference 的起止/中间关键目标。']
        post=['在 target rig 解脚底、膝极向和手/道具锚；选定后冻结或按语义索引打包。']
    elif f in ('look_grid','prop_aim'):
        generation={'method':'parameter_pose_samples_then_pack','candidate_seconds':[2,4],'delivery_axis_seconds':1}
        controls=['root2d: XZ 固定，朝向不随瞄准变动。','fullbody/end-effector: 0/中点/末及中间参数目标，锁定非瞄准姿态与必要 grip。']
        post=['生成候选后在参数域重采样，不把真实时间与参数刻度混同；以 exact baseAsset/baseFrame 形成 mesh-space additive。']
    else:
        generation={'method':'generate_motion_candidates','candidate_seconds':[2,4] if required_loop else [max(1,float(duration or 1)), min(8,max(2,float(duration or 1)*2))]}
        controls=['fullbody: 起点、关键接触/动作点与终点的 sparse pose anchors。']
        post=['按关键事件对齐/裁剪，允许重定时但须重制曲线、通知和依赖时序；不机械复制原 event 秒数。']
    if f in ('gait_cycle','weapon_cycle','weapon_start','sprint_start'):
        gait='sprint' if ('Sprint' in n or n=='Sprint_Acceleration') else 'run' if 'Run' in n else 'crouch' if 'Crouch' in n else 'walk'
        velocity={'walk':1.5,'run':3.5,'sprint':6.0,'crouch':1.5}[gait]
        label,vec=direction(n)
        controls.append(f'root2d: XZ(t)=v*t*{vec}，v 起始设计值 {velocity} m/s；用现有角色参数/可实现 stride 校准，不把提示词当速度控制。')
        controls.append('fullbody/end-effector: 左/右支撑、摆动峰值和同相位终点；相位从 gait master 派生。')
        if f in ('weapon_start','sprint_start'):controls.append('root2d 速度连续递增到 sprint master；结束关键帧采用其相同相位。')
        post.extend(['保存生成 global trajectory 供速度/接触分析，移除动画全局行进轨迹后交角色物理驱动。','制作 Left/Right 同步标记及真实足底接触/FootLock 曲线；相同方向 walk/run/pose 共享相位。'])
    if f=='gait_pose':
        post.append('从对应 gait master 选取经过批准的静态参考；按原时长重复关键帧。原 WalkPose_F 为35键但旋转完全静态，不能把 key count 当成动作时长去生成步行。')
    if f=='idle_additive':
        controls.extend(['root2d: 固定 XZ 与 heading；fullbody 起止采用同一 standing zero pose。','end-effector: 稀疏双脚支撑目标；连续锁脚由后处理完成。'])
        post.extend(['相对当前站姿生成 local additive；去恒定姿态偏移；首尾位置/四元数和速度联合闭合。','禁止对 BasePose 与 SecondaryMotion 独立自由抽样后仅复制 additive metadata。'])
    if f.startswith('pose_table'):
        post.append('Poses_NoCurves 与 Poses 骨姿态逐帧完全相同；包括未播放但作为 additive baseFrame 的帧。')
        checks.append('所有被 evaluator/加法依赖读取的 frame 都有正确状态姿态；Step 插值或分数时间采样按原调用分别保持。')
    if f=='look_grid':
        pitch={'Look_Forward':0,'Look_Down':-90,'Look_Up':90}[n]
        controls.append(f'目标 pitch 行={pitch}°；time u 的 yaw 参数=-180+360*u，中心 u=.5 必须与共同前视 base 对齐；参数不是 root heading。')
        checks.append('pitch=0/yaw=0 零差；三行相同 yaw 时姿态连续，端点解剖极限不可用扭曲髋膝补偿。')
    if f=='prop_aim':
        controls.append('u=0/.5/1 分别是 pitch +90/0/-90 的接口参数；端点手/道具方向根据实际 socket IK 求解。')
        post.append('中性 u=.5 与本 row 的 exact additive baseFrame 完全一致；站/蹲分别使用各自参考帧。')
    if f in ('lean_additive','acceleration_additive'):
        controls.append('方向按图的 LR/FB 或 acceleration 参数；neutral 的非目标残差为 identity，方向对称性在 target rig 校验。')
        post.append('用本 row evaluation 中的 baseAsset/baseFrame/basePoseType，在实际局部/网格空间转换；未参与关节保持零残差。')
    if f in ('jump','fall','land','land_additive','flail'):
        controls.append('fullbody: 空中/顶点/接触姿态目标；空中不施加地面脚锁，着地资源才设置接触 end-effector。')
        post.append('保留躯干/骨盆反应，移除 runtime 物理已经消费的全局跳跃/坠落位移，避免双重移动。')
    if f=='land_additive':post.append('与非加法 Land 同源；ABPT_AnimFrame/ABPT_AnimScaled 分别依原评价策略求差，末态残差收敛。')
    if f in ('turn','rotate'):
        sign=1 if 'Left' in n else -1;angle=180 if '180' in n else 90
        controls.append(f'root2d: 路径 XZ 固定，heading 从0到{sign*angle}°；global_root_heading 用 [cos(theta),sin(theta)]，实际坐标符号经转换探针确认。')
        post.append('按原 actor rotation owner 消费 heading；不同时让 actor 与动画 root 再各旋转一次；RotationAmount/EnableTransition 按支撑与累计角度重制。')
    if f=='stance_transition':controls.append('fullbody 起止使用同版本 Stand_Pose/Crouch_Pose；双脚锚不改变，骨盆高度不可来自随意 standing idle。')
    if f=='foot_transition':
        controls.append('end-effector: 支撑脚保持世界/平台局部锚，移动脚进入指定目标；左右变体反射后重验，Stop 初始相位来自 gait。')
        post.append('Stop/普通 Transition/DynamicTransition 依各自 Slot、baseAsset 和触发时机制作，保持原引用而非互相替代。')
    if f=='mantle':
        controls.extend(['fullbody: 起始 approach、手触边、身体越边、脚登顶、同版本终点姿态。','end-effector: 指定 left/right 或双手 ledge targets；root2d 只控水平，竖直高度由 fullbody root_positions 的 Y 控制。'])
        post.append('按本项目 Mantle target-relative motion source、start offset/correction curves 重制；不直接把 BVH Hips trajectory 当 runtime root。')
    if f=='getup':controls.append('fullbody: 姿态恢复起点（仰卧/俯卧）和规定站蹲终点；支撑手/脚 contact；不能混淆 Back/Front。')
    if f=='roll':controls.append('fullbody + root2d: 入滚/肩背接触/出滚及末 standing pose；无持续脚锁，目标 rig 碰撞需独立检查。')
    if prop(n) in ('Rifle','PistolOneHanded','PistolTwoHanded','Bow','Binoculars','Torch','Box','Barrel'):
        controls.append('end-effector: 目标 rig 上实际道具握点与手腕方向；指骨/道具 rig 不由 SOMA30 模型保证，需 socket IK/手型制作。')
    if f in ('weapon_cycle','weapon_start'):post.append('Arms=配对整身候选的精确派生轨道；同一长度、同一 phase，同一手握点，不单独抽样生成。')
    if f=='land_additive':
        generation={'method':'derive_from_paired_full_pose_master','candidate_seconds':None}
    if f in ('weapon_cycle','weapon_start') and 'Arms' in n:
        generation={'method':'derive_from_paired_full_body_master','candidate_seconds':None}
    if evaluation.get('baseAsset'):checks.append('additive base 存在、baseFrame 有效且语义匹配；完整姿态转差只做一次，不把 baked delta 再求差。')
    if required_loop:checks.append('循环首尾 P/Q 与导数连续；连续多周期 final skeleton 无可见跳变。')
    checks.extend(['检查根消费唯一性、真实接触窗口、最终蒙皮足底穿透/滑移。','在当前普通 Demo 全链及该资产依赖组合中验收；单文件导入/hash PASS 不代表生产姿态可用。'])
    return {'generation':generation,'kimodo_conditioning':controls,'postprocess':post,'acceptance':checks,'required_loop':required_loop,'global_contracts':rules}

def collect_bindings(manifest,index):
    bindings=defaultdict(list)
    for fn in ('v4_layering_inputs.json','v4_anim_graph_inventory.json'):
        for n in read(CONFIG/fn).get('compiledNodeInventory',[]):
            source=n.get('asset');p=n.get('properties',{}).get('Node',{})
            if source and p:
                bindings[source].append({'evidence':fn,'node':n.get('path'),'class':n.get('class'),
                    'sampling':{k:p[k] for k in ('bUseExplicitFrame','explicitFrame','explicitTime','bShouldLoop','bLoopAnimation','groupName','sequence') if k in p}})
    for a in index['assets']:
        path=CONFIG/a['file'];payload=read(path)
        if a['class']=='AnimBlueprint':
            for n in payload.get('compiled',{}).get('nodes',[]):
                p=n.get('runtime',{});source=p.get('sequence')
                if source:bindings[source].append({'evidence':a['file'],'node':n.get('path'),'graph':n.get('graph'),'class':n.get('class'),
                    'sampling':{k:p[k] for k in ('bUseExplicitFrame','explicitFrame','explicitTime','bShouldLoop','bLoopAnimation','groupName') if k in p}})
        if a['class'] in ('BlendSpace','BlendSpace1D','AimOffsetBlendSpace','AimOffsetBlendSpace1D'):
            for sample in payload.get('samples',[]):
                if sample.get('sequence'):bindings[sample['sequence']].append({'evidence':a['file'],'blendSpace':a['source'],'samplePoint':sample.get('point')})
    for a in manifest['blendSpaces']+manifest['aimOffsets']:
        for s in a['metadata'].get('samples',[]):
            source=s.get('animationObjectPath') or s.get('animation')
            if source:bindings[source].append({'evidence':'assets/generated/als_v4/als_manifest.json','blendSpace':a['objectPath'],'sample':s})
    return bindings

def main():
    manifest_path=ROOT/'assets/generated/als_v4/als_manifest.json'
    index_path=CONFIG/'refactored_animation_sources.json'
    manifest=read(manifest_path);index=read(index_path)
    bindings=collect_bindings(manifest,index)
    rows=[]
    for edition,assets in [('v4',manifest['animations']),('refactored',[a for a in index['assets'] if a['class']=='AnimSequence'])]:
        for a in assets:
            source=a.get('objectPath') or a['source'];name=source.rsplit('.',1)[-1];n=canonical(name);f=family(n)
            if edition=='v4':
                meta=a['metadata'];path=CONFIG/'raw_sequences'/f"{hashlib.sha1(source.encode()).hexdigest()}.json"
                raw=read(path) if path.exists() else None
                ev=raw['evaluation'] if raw else {'additiveType':{0:'AAT_None',1:'AAT_LocalSpaceBase',2:'AAT_RotationOffsetMeshSpace'}[meta['additiveType']],
                    'baseAsset':meta['additiveBasePoseObjectPath'],'baseFrame':meta['additiveBasePoseFrame'],
                    'basePoseType':meta['additiveBasePoseType'],'enableRootMotion':meta['rootMotionEnabled'],'forceRootLock':meta['forceRootLock']}
                timing={k:meta[k] for k in ('playLength','sampledKeyCount','frameRateNumerator','frameRateDenominator','interpolation')}
                curves=meta.get('curves',[]);markers=meta.get('syncMarkers',[]);notifies=meta.get('timeline',[])
                evd=[evidence(manifest_path)]+([evidence(path)] if raw else [])
            else:
                path=CONFIG/a['file'];payload=read(path);raw=payload['raw'];ev=payload['evaluation']
                timing={k:raw[k] for k in ('playLength','sampledKeyCount','frameRateNumerator','frameRateDenominator')};timing['interpolation']=ev['interpolation']
                curves=payload.get('curves',{}).get('curves',[])
                markers=[];notifies=[]
                # Some runtime metadata is in nativeText rather than structured fields.
                evd=[evidence(index_path),evidence(path)]
            compact_ev={k:ev.get(k) for k in ('additiveType','baseAsset','baseFrame','basePoseType','enableRootMotion','forceRootLock','rootMotionRootLock')}
            use=bindings.get(source,[])
            curve_names=sorted({c.get('name') or c.get('sourceName') for c in curves if c.get('name') or c.get('sourceName')})
            fixed_frames=sorted({u['sampling']['explicitFrame'] for u in use if u.get('sampling',{}).get('bUseExplicitFrame')})
            rows.append({'edition':edition,'asset':name,'source':source,'semanticName':n,'family':f,'purpose':FAMILIES[f][0],
                'familyContract':FAMILIES[f][1],'promptDraft':prompt(n,f),'evidence':evd,'originalTiming':timing,
                'originalEvaluation':compact_ev,'originalCurveNames':curve_names,'originalSyncMarkers':markers,
                'originalNotifies':notifies,'sourceBindings':use,'fixedFramesReadByGraph':fixed_frames,
                'curveAndNotifyPolicy':'常量层权重只在状态语义相同的帧继承；接触、角度、mask 和事件必须按新姿态/时序重制。',
                'constraintRecipe':recipe(n,f,timing,ev)})
    by_source={r['source']:r for r in rows}
    for r in rows:
        ev=r['originalEvaluation'];base=by_source.get(ev['baseAsset'])
        if base:
            base.setdefault('additiveDependents',[]).append({'source':r['source'],'baseFrame':ev['baseFrame'],'additiveType':ev['additiveType']})
    non_sequences=[]
    for kind in ('montages','blendSpaces','aimOffsets','curves','configAssets','skeletons','skeletalMeshes','staticMeshes','materials','textures','physicsAssets'):
        for a in manifest.get(kind,[]):
            non_sequences.append({'edition':'v4','kind':kind,'source':a.get('objectPath') or a.get('assetName') or a.get('id'),
                                  'production':'derive_from_new_sequence_semantics' if kind in ('montages','blendSpaces','aimOffsets','curves','configAssets') else 'reuse_or_author_rig_assets_outside_kimodo'})
    for a in index['assets']:
        if a['class']!='AnimSequence':non_sequences.append({'edition':'refactored','kind':a['class'],'source':a['source'],
             'production':'preserve_graph_contract_rebind_new_assets' if a['class'] in ('AnimBlueprint','AnimMontage','BlendSpace','BlendSpace1D','AlsAnimationInstanceSettings','CurveFloat') else 'non_motion_reference'})
    report={'schemaVersion':1,'status':'design_only_not_executable_constraints','characterStyle':'默认自然、稳定、不过度扭髋/耸肩；后续风格替换必须成组统一。',
            'scope':{'v4Sequences':sum(r['edition']=='v4' for r in rows),'refactoredSequences':sum(r['edition']=='refactored' for r in rows),
                     'note':'253 个引用身份，不等于 253 次独立生成；对应版本的同语义产物可由一个经过验证的 master 派生。完整 snapshot，未声称每个资产当前都生产可达。'},
            'kimodoSpace':{'up':'Y','forwardAtHeadingZero':'+Z','units':'meters','root2dComponents':['x','z'],'fullbodyRotations':'axis_angle_radians',
                           'fullbodyIsJointPositionConstraintNotExactLocalRotation':True,'contactConditioningAvailableInPublicAPI':False},
            'assets':rows,'nonSequenceAssets':non_sequences}
    OUTPUT.mkdir(parents=True,exist_ok=True)
    (OUTPUT/'kimodo-als-asset-contracts.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    lines=['# ALS 逐资产 Kimodo 约束清单','','由 `tools/unreal/build_als_kimodo_asset_contracts.py` 从当前原资源/图引用导出；设计规格，未生成新动作。',
           '配合 [生产设计](kimodo-als-resource-production.md) 和 [完整 JSON](kimodo-als-asset-contracts.json) 阅读。',
           '提示词是候选草稿；完整 JSON 保存每项约束、后处理、验收和 source/hash 证据。`{stance}` 必须按 frame 状态替换，不能直接发送。','']
    for edition in ('v4','refactored'):
        lines.extend([f'## {edition} 资源','','| 资产 | 作用/生成方法 | 原关键帧/时长 | 原加法参考 | 提示词草稿 |','| --- | --- | --- | --- | --- |'])
        for r in sorted((r for r in rows if r['edition']==edition),key=lambda r:(r['family'],r['asset'])):
            ev=r['originalEvaluation'];base=ev['baseAsset'];ref=(base.rsplit('.',1)[-1]+f" frame {ev['baseFrame']}") if base else '无'
            timing=r['originalTiming'];p=r['promptDraft'] or '确定性派生；不调用 Kimodo'
            lines.append(f"| `{r['asset']}` | {r['purpose']} / `{r['family']}` | {timing['sampledKeyCount']} / {timing['playLength']:.6g}s | {ev['additiveType']}；{ref} | {p} |")
    lines.extend(['','## 逐资产生成约束','','坐标、共享人物风格与 sparse 条件限制见生产设计；以下目标仍需用批准的 pose/socket 数值实例化。',''])
    for r in sorted(rows,key=lambda r:(r['edition'],r['family'],r['asset'])):
        contract=r['constraintRecipe']
        lines.extend([f"### {r['edition']} `{r['asset']}` — 约束",'',
                      f"作用：{r['purpose']}。{r['familyContract']}",
                      f"生产方式：`{contract['generation']['method']}`；循环闭合要求：{'是' if contract['required_loop'] else '按图状态连接，非强制循环'}。",'',
                      'Kimodo / rig 目标：', ''])
        lines.extend('- '+s for s in contract['kimodo_conditioning'])
        lines.extend(['','后处理：',''])
        lines.extend('- '+s for s in contract['postprocess'])
        lines.extend(['','验收：',''])
        lines.extend('- '+s for s in contract['acceptance'])
        lines.append('')
    lines.extend(['','## 姿态表读取与加法参考','','这里只列实际引用；未列出的帧仍保留原打包索引，生成前应检查其曲线、分数时间采样及未来用途。',''])
    for r in rows:
        if r['family'] not in ('pose_table','pose_table_copy'):continue
        refs=defaultdict(set)
        for use in r['sourceBindings']:
            if use.get('sampling',{}).get('bUseExplicitFrame'):
                graph=use.get('graph') or use.get('node','');refs[use['sampling']['explicitFrame']].add(graph.split(':')[-1].split('.')[-1])
        deps=r.get('additiveDependents',[])
        lines.extend([f"### {r['edition']} `{r['asset']}`",'',f"原始 {r['originalTiming']['sampledKeyCount']} keys；插值 {r['originalTiming']['interpolation']}。",
                      '图固定读取：'+('；'.join(f"frame {k}: {', '.join(sorted(v))}" for k,v in sorted(refs.items())) or '见 sourceBindings / V4 authored graph 的时间/表达式采样。'),
                      '加法引用：'+('；'.join(f"{d['source'].rsplit('.',1)[-1]} → frame {d['baseFrame']}" for d in deps) or '无清单内引用。'),''])
    (OUTPUT/'kimodo-als-asset-catalog.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps({'sequences':len(rows),'editions':dict(Counter(r['edition'] for r in rows)),
                      'families':dict(Counter(r['family'] for r in rows)),'otherAssetIdentities':len(non_sequences)},ensure_ascii=False))

if __name__=='__main__':main()

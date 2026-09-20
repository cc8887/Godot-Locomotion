using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

public partial class PhysicsJointSetSmoke : Node3D
{
    private sealed record Case(AlsRagdollPhysicsDefinition Definition,AlsPhysicsJointSettings[] Settings,
        AlsPhysicsBodySet Bodies,AlsPhysicsJointSet Joints,Skeleton3D Skeleton,Transform3D World,AlsLocalPose[] Pose,int Pelvis,Vector3 InitialPelvis);
    private readonly List<Case> _cases=[];
    private int _hz,_frame; private bool _done,_highDrop,_noContact,_pair,_alsDrives,_noSoftSolve,_noSelfCollision;
    private int _pairAxis;private float _pairAngle;
    private bool _assertDampingOnlyRejected;
    private float _maxAnchor,_maxSpeed,_finalSpeed,_finalAngularSpeed; private double _maxLimit,_finalLimit;
    private string _finalSpeedBody="none";
    public override void _Ready()
    {
        try
        {
            _hz=int.Parse(OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--hz="))?[5..]??"60");
            Require(_hz is 30 or 60 or 120,"Expected 30/60/120 Hz."); Engine.PhysicsTicksPerSecond=_hz;
            _highDrop=OS.GetCmdlineUserArgs().Contains("--high-drop");
            _noContact=OS.GetCmdlineUserArgs().Contains("--no-contact");
            _assertDampingOnlyRejected=OS.GetCmdlineUserArgs().Contains("--assert-damping-only-rejected");
            _pair=OS.GetCmdlineUserArgs().Contains("--pair")||_assertDampingOnlyRejected;
            _alsDrives=OS.GetCmdlineUserArgs().Contains("--als-drives");
            _noSoftSolve=OS.GetCmdlineUserArgs().Contains("--no-soft-solve");
            _noSelfCollision=OS.GetCmdlineUserArgs().Contains("--no-self-collision");
            _pairAxis=int.Parse(OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--pair-axis="))?[12..]??"0");
            _pairAngle=float.Parse(OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--pair-angle="))?[13..]??"0.6",System.Globalization.CultureInfo.InvariantCulture);
            Require(_pairAxis is >=0 and <=2&&float.IsFinite(_pairAngle),"Invalid pair perturbation.");
            GD.Print($"JOINT_TEST_CONFIG hz={_hz} pair={_pair} pair_axis={_pairAxis} pair_angle={_pairAngle} no_contact={_noContact} no_self_collision={_noSelfCollision} no_soft_solve={_noSoftSolve} als_drives={_alsDrives} high_drop={_highDrop}");
            var floor=new StaticBody3D { CollisionLayer=1,CollisionMask=2,Position=new(0,-.1f,0) };
            floor.AddChild(new CollisionShape3D { Shape=new BoxShape3D { Size=new(100,.2f,100) } }); AddChild(floor);
            var set=ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            foreach (var name in new[] {"Mannequin","AnimMan"})
            {
                var definition=AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"),AlsPhysicsAssetCompiler.MeshRoot+name+"."+name);
                var settings=AlsPhysicsJointCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_joint_reference.json"),definition);
                var asset=set.SkeletalMeshes.Single(m=>m.ObjectPath==definition.Mesh);
                var model=ResourceLoader.Load<PackedScene>(AlsGodotImportCoordinator.AssetRoot+"/"+asset.ResourcePath).Instantiate<Node3D>(); AddChild(model);
                model.GlobalTransform=new(_highDrop ? Basis.FromEuler(new(.5f,1.1f,.2f)) : new Basis(Vector3.Up,.37f),new(_cases.Count*5,_highDrop ? 10:3,0));
                var skeleton=AlsImportedResourceAuditor.FindFirst<Skeleton3D>(model)!;
                var names=Enumerable.Range(0,skeleton.GetBoneCount()).Select(skeleton.GetBoneName).Select(n=>n.ToString()).ToArray();
                var parents=Enumerable.Range(0,names.Length).Select(skeleton.GetBoneParent).ToArray();
                var reference=Enumerable.Range(0,names.Length).Select(i=>AlsPhysicsBodySet.Pose(skeleton.GetBoneRest(i))).ToArray();
                if(_pair)
                {
                    var joint=definition.Joints.Single(j=>definition.Bodies[j.ChildBody].Bone=="spine_02");
                    settings=[settings[joint.Index] with {Index=0}];
                    definition=definition with {Bodies=[definition.Bodies[joint.ParentBody] with {Index=0,PhysicsType=1},definition.Bodies[joint.ChildBody] with {Index=1}],
                        Joints=[joint with {Index=0,ParentBody=0,ChildBody=1}],DisabledCollisions=[(0,1)]};
                }
                var mask=_noContact||_pair?0u:_noSelfCollision?1u:3u;
                // Clear both sides of filtering for the isolation cases: a zero
                // body mask alone can still match the floor's mask against its layer.
                var bodies=new AlsPhysicsBodySet(this,definition,names,parents,7,1,collisionLayer:_noContact||_pair?0u:2u,collisionMask:mask);
                bodies.Seed(new(1,7,1),skeleton.GlobalTransform,reference,new(2,_highDrop ? -12:0,0),new(.3f,.7f,-.2f)); bodies.Start();
                var rawInertia=OS.GetCmdlineUserArgs().Contains("--raw-inertia");
                var joints=new AlsPhysicsJointSet(bodies,definition,settings,conditionBodyInertia:!rawInertia);
                GD.Print($"JOINT_INERTIA_CONFIG mesh={name} computed_conditioning={!rawInertia}");
                Require(joints.BoundJointCount==definition.Joints.Length-(_pair?0:1),"Free root unexpectedly bound.");
                var pelvis=_pair?1:Array.FindIndex(definition.Bodies,b=>b.Bone=="pelvis");
                var c=new Case(definition,settings,bodies,joints,skeleton,skeleton.GlobalTransform,new AlsLocalPose[names.Length],pelvis,bodies.BodyAt(pelvis).GlobalPosition);
                _cases.Add(c);
                foreach (var joint in definition.Joints.Where(j=>definition.Bodies[j.ParentBody].Bone!="root"))
                { var frames=joints.Frames(joint.Index); Require(frames.Parent.Origin.DistanceTo(frames.Child.Origin)<.001,"Mass-local joint anchors do not coincide in reference pose."); }
                if(_pair)
                {
                    // A fixed support, not a kinematic move from its creation origin.
                    bodies.BodyAt(0).FreezeMode=RigidBody3D.FreezeModeEnum.Static;
                    for(var i=0;i<2;i++){bodies.BodyAt(i).GravityScale=0;bodies.BodyAt(i).LinearVelocity=Vector3.Zero;bodies.BodyAt(i).AngularVelocity=Vector3.Zero;bodies.BodyAt(i).CollisionMask=0;}
                    var frames=joints.Frames(0);var child=bodies.BodyAt(1);var turn=new Basis(frames.Parent.Basis[_pairAxis].Normalized(),_pairAngle);
                    child.GlobalTransform=new(turn*child.GlobalBasis,frames.Parent.Origin+turn*(child.GlobalPosition-frames.Parent.Origin));
                }
            }
        }
        catch(Exception e){Fail(e);}
    }

    public override void _PhysicsProcess(double dt)
    {
        if(_done)return;
        try
        {
            if(_assertDampingOnlyRejected)
            {
                foreach(var c in _cases)
                {
                    // Align both connectors so no soft-limit stiffness can
                    // hide the unsupported pure damping motor underneath.
                    var frames=c.Joints.Frames(0);var child=c.Bodies.BodyAt(1);
                    var local=child.GlobalTransform.AffineInverse()*frames.Child;
                    child.GlobalTransform=frames.Parent*local.AffineInverse();
                    child.AngularVelocity=Vector3.Zero;
                    c.Joints.SetEffectiveAngularDrive(0,1.5);
                    var rejected=false;
                    try{c.Joints.Step(dt);}
                    catch(NotSupportedException e) when(e.Message=="Jolt position motors cannot represent damping-only rows."){rejected=true;}
                    Require(rejected,"Damping-only motor was silently accepted.");
                }
                GD.Print("JOINT_DAMPING_ONLY_REJECTION_OK meshes=2");
                _done=true;GetTree().Quit();return;
            }
            _frame++; float anchor=0,speed=0,angularSpeed=0; double limit=0;string speedBody="none";
            foreach(var c in _cases)
            {
                foreach(var j in c.Definition.Joints)
                {
                    if(c.Definition.Bodies[j.ParentBody].Bone=="root")continue;
                    var f=c.Joints.Frames(j.Index); anchor=Math.Max(anchor,f.Parent.Origin.DistanceTo(f.Child.Origin));
                    var p=f.Parent.Basis.Orthonormalized().GetRotationQuaternion(); var q=f.Child.Basis.Orthonormalized().GetRotationQuaternion(); if(p.Dot(q)<0)q=-q;
                    var angles=AlsJointAngularKinematics.Evaluate(new AlsQuaternion(p.X,p.Y,p.Z,p.W).Normalized(),new AlsQuaternion(q.X,q.Y,q.Z,q.W).Normalized()).Angles;
                    var s=c.Settings[j.Index];
                    for(var axis=0;axis<3;axis++)
                    {
                        var motion=axis==0?s.AngularMotion.X:axis==1?s.AngularMotion.Y:s.AngularMotion.Z;
                        if(motion==AlsJointMotion.Free)continue;
                        var value=axis==0?angles.X:axis==1?angles.Y:angles.Z;
                        var allowed=motion==AlsJointMotion.Locked?0:axis==0?s.AngularLimitsRad.X:axis==1?s.AngularLimitsRad.Y:s.AngularLimitsRad.Z;
                        limit=Math.Max(limit,Math.Max(0,Math.Abs(value)-allowed));
                        if(_frame%_hz==0 && Math.Abs(value)-allowed>.1)
                            GD.Print($"JOINT_LIMIT_DETAIL t={_frame/_hz} body={c.Definition.Bodies[j.ChildBody].Bone} axis={axis} motion={motion} angle={value:G5} allowed={allowed:G5} sleeping={c.Bodies.BodyAt(j.ChildBody).Sleeping}");
                    }
                }
                for(var i=0;i<c.Bodies.BodyCount;i++)
                {
                    var b=c.Bodies.BodyAt(i); Require(b.GlobalTransform.IsFinite()&&b.LinearVelocity.IsFinite()&&b.AngularVelocity.IsFinite(),"Nonfinite joint chain.");
                    if(!_noContact)Require(b.GlobalPosition.Y>-.25f,"Constrained body fell through the floor.");
                    if(!b.Freeze)
                    {
                        var bodySpeed=b.LinearVelocity.Length();
                        if(bodySpeed>speed){speed=bodySpeed;speedBody=c.Definition.Mesh+":"+c.Definition.Bodies[i].Bone;}
                        angularSpeed=Math.Max(angularSpeed,b.AngularVelocity.Length());
                    }
                }
                c.Bodies.CaptureLocalPose(c.World,c.Pose);
                for(var bone=0;bone<c.Pose.Length;bone++) c.Skeleton.SetBonePose(bone,AlsPhysicsBodySet.Local(c.Pose[bone]));
                if(_frame==_hz && !_pair)
                    Require(c.Bodies.BodyAt(c.Pelvis).GlobalPosition.Y<c.InitialPelvis.Y-.5f,"Free root anchored pelvis.");
                if(_alsDrives)
                    c.Joints.SetEffectiveAngularDrive(25000*Math.Clamp(c.Bodies.BodyAt(c.Pelvis).LinearVelocity.Length()/10,0,1)*1.5,0);
                if(!_noSoftSolve)c.Joints.Step(dt);
                if(_frame==1&&!_noSoftSolve)c.Joints.VerifySpringReadback();
                if(_frame==2)c.Joints.VerifyInertiaReadback();
                if(_frame%_hz==0)GD.Print($"JOINT_SOLVE_DETAIL rows={c.Joints.LastRowCount} pelvis_angular={c.Bodies.BodyAt(c.Pelvis).AngularVelocity}");
            }
            _maxAnchor=Math.Max(_maxAnchor,anchor); _maxLimit=Math.Max(_maxLimit,limit); _maxSpeed=Math.Max(_maxSpeed,speed);
            if(_frame>_hz*9)
            {
                if(speed>_finalSpeed){_finalSpeed=speed;_finalSpeedBody=speedBody;}
                _finalAngularSpeed=Math.Max(_finalAngularSpeed,angularSpeed);_finalLimit=Math.Max(_finalLimit,limit);
            }
            if(_frame%_hz==0) GD.Print($"JOINT_CHAIN_TICK t={_frame/_hz} anchor_m={anchor:G5} limit_error_rad={limit:G5} speed={speed:G5}");
            Require(anchor<.2f,$"Joint chain separated by more than 20 cm at frame {_frame}: anchor={anchor} speed={speed}.");
            if(_frame==_hz*10)
            {
                GD.Print($"JOINT_FINAL_BUDGET max_anchor={_maxAnchor} max_limit={_maxLimit} final_speed={_finalSpeed} final_speed_body={_finalSpeedBody} final_angular_speed={_finalAngularSpeed} final_limit={_finalLimit}");
                if(!_noContact)Require(_finalSpeed<.2f,"Joint chain did not settle below 0.2 m/s.");
                Require(_finalLimit<.1,"Joint chain limits did not settle within 0.1 rad.");
                foreach(var c in _cases)
                {
                    c.Joints.Dispose();c.Joints.Dispose();
                    for(var i=0;i<c.Bodies.BodyCount;i++)
                    {
                        var raw=c.Definition.Bodies[i].InertiaKgCm2*.0001;
                        Require(c.Bodies.BodyAt(i).Inertia==new Vector3((float)raw.X,(float)raw.Y,(float)raw.Z),"Joint disposal did not restore original body inertia.");
                    }
                    c.Bodies.Dispose();
                }
                GD.Print($"PHYSICS_JOINT_SET_OK hz={_hz} pair={_pair} pair_axis={_pairAxis} pair_angle={_pairAngle} high_drop={_highDrop} frames={_frame} bound={(_pair?2:36)} free_root={(_pair?0:2)} max_anchor_m={_maxAnchor} max_limit_rad={_maxLimit} max_speed={_maxSpeed} final_speed={_finalSpeed} final_limit_rad={_finalLimit}");
                _done=true;GetTree().Quit();
            }
        }
        catch(Exception e){Fail(e);}
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private void Fail(Exception e){GD.PushError("PHYSICS_JOINT_SET_FAILED "+e);_done=true;GetTree().Quit(1);}
    public override void _ExitTree(){foreach(var c in _cases){c.Joints.Dispose();c.Bodies.Dispose();}}
}

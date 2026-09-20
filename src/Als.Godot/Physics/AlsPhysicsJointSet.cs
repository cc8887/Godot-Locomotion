using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// Main-only native-frame transport. Springs are solved inside Jolt's constraint
// island, alongside hard joints and contacts. Jolt's motor error and integration
// still differ from Chaos: this adapter requires force/trajectory validation.
// Experimental: the full-chain 10-second stability gate is not yet satisfied.
// Only the dedicated physics fixture consumes it; gameplay must wait for that gate.
internal sealed class AlsPhysicsJointSet : IDisposable
{
    private readonly AlsPhysicsBodySet _bodies;
    private readonly AlsRagdollPhysicsDefinition _definition;
    private readonly AlsPhysicsJointSettings[] _settings;
    private readonly Transform3D[] _parentFrames,_childFrames;
    private readonly Rid[] _joints;
    // The limit channel shares the hard constraint, followed by a separate
    // drive constraint. Never combine targets or accumulated motor impulses.
    // This lets each Jolt limit motor solve before its point constraint.
    private const int Drive=0,Limit=1;
    private readonly Rid[] _springs;
    private readonly float[] _springCache;
    private readonly bool[] _springEnabled;
    private readonly Vector3[] _previousInertia;
    private readonly AlsPrecisePose[] _projectionPoses;
    private readonly AlsProjectionDelta[] _projectionDeltas;
    private readonly AlsProjectionVelocity[] _projectionVelocities;
    private readonly AlsLockedLinearProjection[] _projections;
    private bool _disposed;
    private double _driveStiffness=-1,_driveDamping=-1;
    internal int BoundJointCount { get; private set; }
    internal int LastRowCount { get; private set; }
    internal int LastDriveRowCount { get; private set; }
    internal int LastLimitRowCount { get; private set; }
    internal int BackendConstraintCount=>_springs.Count(r=>r.IsValid);
    internal void SetEffectiveAngularDrive(double stiffness,double damping)
    {
        Check();if(!double.IsFinite(stiffness)||stiffness<0||!double.IsFinite(damping)||damping<0)throw new ArgumentOutOfRangeException(nameof(stiffness));
        _driveStiffness=stiffness;_driveDamping=damping;
    }

    internal AlsPhysicsJointSet(AlsPhysicsBodySet bodies,AlsRagdollPhysicsDefinition definition,AlsPhysicsJointSettings[] settings,bool conditionBodyInertia=true)
    {
        Main();
        if (!bodies.Active || settings.Length!=definition.Joints.Length || bodies.BodyCount!=definition.Bodies.Length)
            throw new InvalidOperationException("Joint binding requires matching active bodies and settings.");
        if(ProjectSettings.GetSetting("physics/3d/physics_engine").AsString()!="Jolt Physics")
            throw new NotSupportedException("This joint transport requires Jolt Physics.");
        foreach(var s in settings)Validate(s);
        _bodies=bodies;_definition=definition;_settings=settings.ToArray();
        _joints=new Rid[settings.Length];_parentFrames=new Transform3D[settings.Length];_childFrames=new Transform3D[settings.Length];
        _springs=new Rid[settings.Length*2];
        _springCache=Enumerable.Repeat(float.NaN,_springs.Length*9).ToArray();_springEnabled=new bool[_springs.Length*3];
        _previousInertia=Enumerable.Range(0,bodies.BodyCount).Select(i=>bodies.BodyAt(i).Inertia).ToArray();
        _projectionPoses=new AlsPrecisePose[bodies.BodyCount];_projectionDeltas=new AlsProjectionDelta[bodies.BodyCount];
        _projectionVelocities=new AlsProjectionVelocity[bodies.BodyCount];_projections=new AlsLockedLinearProjection[settings.Length];
        try
        {
            if(conditionBodyInertia)
            {
                var conditioned=AlsBodyInertiaCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_inertia_reference.json"),definition,settings);
                for(var i=0;i<conditioned.Length;i++)
                {
                    var scale=conditioned[i].InverseInertiaScale;var raw=definition.Bodies[i].InertiaKgCm2;
                    bodies.BodyAt(i).Inertia=new((float)(raw.X*.0001/scale.X),(float)(raw.Y*.0001/scale.Y),(float)(raw.Z*.0001/scale.Z));
                }
            }
            foreach(var j in definition.Joints)
            {
                var s=settings[j.Index];if(s.Index!=j.Index)throw new InvalidDataException("Joint index differs.");
                _parentFrames[j.Index]=bodies.BoneToMass(j.ParentBody).AffineInverse()*AlsPhysicsBodySet.NativeToFbx(j.ParentFrame);
                _childFrames[j.Index]=bodies.BoneToMass(j.ChildBody).AffineInverse()*AlsPhysicsBodySet.NativeToFbx(j.ChildFrame);
                if(Unconstrained(s))continue;
                var rid=PhysicsServer3D.JointCreate();_joints[j.Index]=rid;_springs[j.Index*2+Limit]=rid;
                PhysicsServer3D.JointMakeGeneric6Dof(rid,bodies.BodyAt(j.ParentBody).GetRid(),_parentFrames[j.Index],bodies.BodyAt(j.ChildBody).GetRid(),_childFrames[j.Index]);
                for(var axis=0;axis<3;axis++)
                {
                    var a=(Vector3.Axis)axis;var motion=Motion(s.AngularMotion,axis);var soft=axis==0?s.TwistSoftLimit:s.SwingSoftLimit;
                    Param(rid,a,PhysicsServer3D.G6DofJointAxisParam.LinearLowerLimit,0);
                    Param(rid,a,PhysicsServer3D.G6DofJointAxisParam.LinearUpperLimit,0);
                    PhysicsServer3D.Generic6DofJointSetFlag(rid,a,PhysicsServer3D.G6DofJointAxisFlag.EnableLinearLimit,Motion(s.LinearMotion,axis)==AlsJointMotion.Locked);
                    var limit=motion==AlsJointMotion.Limited?(float)Component(s.AngularLimitsRad,axis):0;
                    Param(rid,a,PhysicsServer3D.G6DofJointAxisParam.AngularLowerLimit,-limit);
                    Param(rid,a,PhysicsServer3D.G6DofJointAxisParam.AngularUpperLimit,limit);
                    PhysicsServer3D.Generic6DofJointSetFlag(rid,a,PhysicsServer3D.G6DofJointAxisFlag.EnableAngularLimit,
                        motion==AlsJointMotion.Locked || (motion==AlsJointMotion.Limited&&!soft.Enabled));
                }
                var excluded=definition.DisabledCollisions.Any(pair=>pair.A==Math.Min(j.ParentBody,j.ChildBody)&&pair.B==Math.Max(j.ParentBody,j.ChildBody));
                PhysicsServer3D.JointDisableCollisionsBetweenBodies(rid,excluded||!s.CollisionEnabled);
                {
                    var spring=PhysicsServer3D.JointCreate();_springs[j.Index*2+Drive]=spring;
                    PhysicsServer3D.JointMakeGeneric6Dof(spring,bodies.BodyAt(j.ParentBody).GetRid(),_parentFrames[j.Index],bodies.BodyAt(j.ChildBody).GetRid(),_childFrames[j.Index]);
                    for(var axis=0;axis<3;axis++)
                    {
                        var a=(Vector3.Axis)axis;
                        PhysicsServer3D.Generic6DofJointSetFlag(spring,a,PhysicsServer3D.G6DofJointAxisFlag.EnableLinearLimit,false);
                        PhysicsServer3D.Generic6DofJointSetFlag(spring,a,PhysicsServer3D.G6DofJointAxisFlag.EnableAngularLimit,false);
                    }
                    PhysicsServer3D.JointDisableCollisionsBetweenBodies(spring,excluded||!s.CollisionEnabled);
                }
                BoundJointCount++;
            }
        }
        catch{Dispose();throw;}
    }

    // Experimental post-Jolt projection. Cache the whole chain before applying
    // any deltas; committing a joint immediately corrupts downstream geometry.
    // This runs after Jolt's contact pass, not inside its island solver.
    internal void Project(double dt)
    {
        Check();if(!double.IsFinite(dt)||dt<=0||dt>.1)throw new ArgumentOutOfRangeException(nameof(dt));
        if(!_bodies.Active)return;
        Array.Clear(_projectionDeltas);Array.Clear(_projectionVelocities);
        for(var i=0;i<_bodies.BodyCount;i++)_projectionPoses[i]=Precise(_bodies.BodyAt(i).GlobalTransform);
        foreach(var j in _definition.Joints)
        {
            var s=_settings[j.Index];if(!_joints[j.Index].IsValid||!s.Projection.Enabled)continue;
            if(s.LinearMotion!=new AlsJointMotions(AlsJointMotion.Locked,AlsJointMotion.Locked,AlsJointMotion.Locked)||s.Projection.AngularAlpha!=0)
                throw new NotSupportedException("Projection requires locked linear axes and zero angular alpha.");
            var child=_bodies.BodyAt(j.ChildBody);var inertia=child.Inertia;
            _projections[j.Index]=new(_projectionPoses[j.ParentBody],_projectionPoses[j.ChildBody],
                Precise(_parentFrames[j.Index]),Precise(_childFrames[j.Index]),child.Freeze?0:1/child.Mass,
                new(1/inertia.X,1/inertia.Y,1/inertia.Z),(float)s.Stiffness,(float)s.Projection.LinearAlpha,
                (float)(s.Projection.TeleportDistanceCm*.01));
        }
        foreach(var j in _definition.Joints)
        {
            if(!_joints[j.Index].IsValid||!_settings[j.Index].Projection.Enabled)continue;
            var velocity=_projections[j.Index].Apply(_projectionDeltas[j.ParentBody],ref _projectionDeltas[j.ChildBody],
                dt,AlsLockedLinearProjection.ReferenceVelocityAlpha);
            var previous=_projectionVelocities[j.ChildBody];
            _projectionVelocities[j.ChildBody]=new(previous.Linear+velocity.Linear,previous.Angular+velocity.Angular);
        }
        for(var i=0;i<_bodies.BodyCount;i++)
        {
            var body=_bodies.BodyAt(i);var delta=_projectionDeltas[i];
            if(body.Freeze||delta==default)continue;
            var corrected=AlsLockedLinearProjection.Correct(_projectionPoses[i],delta);var q=corrected.Rotation;
            body.GlobalTransform=new(new Basis(new Quaternion((float)q.X,(float)q.Y,(float)q.Z,(float)q.W)),GodotVector(corrected.Position.ToSingle()));
            body.LinearVelocity+=GodotVector(_projectionVelocities[i].Linear);
            body.AngularVelocity+=GodotVector(_projectionVelocities[i].Angular);
        }
    }
    private static AlsPrecisePose Precise(Transform3D t)=>new(new(t.Origin.X,t.Origin.Y,t.Origin.Z),
        Core(t.Basis.Orthonormalized().GetRotationQuaternion()),AlsDoubleVector.One);
    private static Vector3 GodotVector(System.Numerics.Vector3 v)=>new(v.X,v.Y,v.Z);

    internal void Step(double dt)
    {
        Check();if(!double.IsFinite(dt)||dt<=0||dt>.1)throw new ArgumentOutOfRangeException(nameof(dt));
        LastRowCount=LastDriveRowCount=LastLimitRowCount=0;if(!_bodies.Active)return;
        var angleTolerance=AlsJointRowActivation.AngleTolerance(dt);
        foreach(var j in _definition.Joints)
        {
            var rid=_joints[j.Index];if(!rid.IsValid)continue;var s=_settings[j.Index];
            var pBody=_bodies.BodyAt(j.ParentBody);var cBody=_bodies.BodyAt(j.ChildBody);
            var frames=Frames(j.Index);var p=frames.Parent.Basis.Orthonormalized().GetRotationQuaternion();var c=frames.Child.Basis.Orthonormalized().GetRotationQuaternion();
            var current=Angles(p,c);
            var predictedP=Predict(p,pBody,dt);var predictedC=Predict(c,cBody,dt);
            var predicted=Angles(predictedP,predictedC);
            var t=s.AngularDrive.Target;var targetRotation=new Quaternion((float)-t.X,(float)t.Y,(float)-t.Z,(float)t.W);
            var driveError=AlsJointAngularKinematics.SwingTwistDriveError(Core(predictedP),Core(predictedC),Core(targetRotation));
            var inverseP=InverseInertia(pBody);var inverseC=InverseInertia(cBody);var childBasis=new Basis(c);
            var desired=Vector3.Zero;
            for(var axis=0;axis<3;axis++)
            {
                var motion=Motion(s.AngularMotion,axis);var soft=axis==0?s.TwistSoftLimit:s.SwingSoftLimit;
                var position=axis==0?s.AngularDrive.TwistPosition:s.AngularDrive.SwingPosition;
                var velocity=axis==0?s.AngularDrive.TwistVelocity:s.AngularDrive.SwingVelocity;
                var coefficient=axis==0?0:2;var value=Component(current,axis);var next=Component(predicted,axis);var angleLimit=Component(s.AngularLimitsRad,axis);
                // Chaos builds these rows from predicted connector rotations.
                // A body already moving back inside the limit must retain its
                // momentum; testing the old angle too adds an extra braking row.
                var limitActive=motion==AlsJointMotion.Limited&&soft.Enabled&&AlsJointRowActivation.SoftLimitActive(next,angleLimit,angleTolerance);
                var direction=childBasis[axis];var inv=direction.Dot(inverseP*direction+inverseC*direction);
                var effective=inv>0?1/inv:0;
                var driveScale=s.AngularDrive.ForceMode==AlsJointForceMode.Acceleration?effective:.0001;
                var limitScale=s.AngularSoftForceMode==AlsJointForceMode.Acceleration?effective:.0001;
                var stiffness=position?(_driveStiffness>=0?_driveStiffness:Component(s.AngularDrive.Stiffness,coefficient)):0;
                var drag=velocity?(_driveDamping>=0?_driveDamping:Component(s.AngularDrive.Damping,coefficient)):0;
                var driveActive=AlsJointRowActivation.DriveActive(motion!=AlsJointMotion.Locked,Component(driveError,axis),stiffness,drag,angleTolerance);
                var kd=driveActive?stiffness*driveScale:0;
                var cd=driveActive?drag*driveScale:0;
                var kl=limitActive?soft.Stiffness*limitScale:0;var cl=limitActive?soft.Damping*limitScale:0;
                var boundary=Math.CopySign(angleLimit,next);
                desired[axis]=(float)(limitActive?boundary:value);
                if(ConfigureSpring(j.Index*2+Drive,axis,kd,cd))LastDriveRowCount++;
                if(ConfigureSpring(j.Index*2+Limit,axis,kl,cl))LastLimitRowCount++;
            }
            // Reconstruct the target from native stereographic swing angles and
            // twist. The Godot 6DOF equilibrium API uses negated ZYX Euler angles.
            var y=Math.Tan(desired.Y*.25);var z=Math.Tan(desired.Z*.25);var denominator=1+y*y+z*z;
            var swing=new Quaternion(0,(float)(2*y/denominator),(float)(2*z/denominator),(float)((1-y*y-z*z)/denominator));
            var orientation=swing*new Quaternion(Vector3.Right,desired.X);
            SetTarget(j.Index*2+Drive,targetRotation);
            SetTarget(j.Index*2+Limit,orientation);
        }
        LastRowCount=LastDriveRowCount+LastLimitRowCount;
    }

    private bool ConfigureSpring(int spring,int axis,double stiffness,double damping)
    {
        // Jolt's SixDOF position motor is deactivated at zero stiffness. Check
        // each channel even if the other has positive stiffness.
        if(stiffness==0&&damping>0)throw new NotSupportedException("Jolt position motors cannot represent damping-only rows.");
        var enabled=stiffness>0;
        SpringParam(spring,axis,0,PhysicsServer3D.G6DofJointAxisParam.AngularSpringStiffness,(float)stiffness);
        SpringParam(spring,axis,1,PhysicsServer3D.G6DofJointAxisParam.AngularSpringDamping,(float)damping);
        if(_springEnabled[spring*3+axis]!=enabled)
        {
            PhysicsServer3D.Generic6DofJointSetFlag(_springs[spring],(Vector3.Axis)axis,PhysicsServer3D.G6DofJointAxisFlag.EnableAngularSpring,enabled);
            _springEnabled[spring*3+axis]=enabled;
        }
        return enabled;
    }
    private void SetTarget(int spring,Quaternion orientation)
    {
        var euler=-new Basis(orientation).GetEuler(EulerOrder.Zyx);
        for(var axis=0;axis<3;axis++)SpringParam(spring,axis,2,PhysicsServer3D.G6DofJointAxisParam.AngularSpringEquilibriumPoint,euler[axis]);
    }
    private void SpringParam(int spring,int axis,int field,PhysicsServer3D.G6DofJointAxisParam key,float value)
    {
        var index=spring*9+axis*3+field;
        if(_springCache[index]==value)return;
        Param(_springs[spring],(Vector3.Axis)axis,key,value);_springCache[index]=value;
    }

    internal void VerifySpringReadback()
    {
        Check();
        for(var j=0;j<_springs.Length;j++)if(_springs[j].IsValid)
        for(var axis=0;axis<3;axis++)
        {
            var a=(Vector3.Axis)axis;
            if(j%2==Drive&&(PhysicsServer3D.Generic6DofJointGetFlag(_springs[j],a,PhysicsServer3D.G6DofJointAxisFlag.EnableLinearLimit)||
               PhysicsServer3D.Generic6DofJointGetFlag(_springs[j],a,PhysicsServer3D.G6DofJointAxisFlag.EnableAngularLimit)))
                throw new InvalidOperationException("Spring channel must not duplicate hard constraints.");
            if(j%2==Limit)
            {
                var s=_settings[j/2];var motion=Motion(s.AngularMotion,axis);var soft=axis==0?s.TwistSoftLimit:s.SwingSoftLimit;
                var linear=Motion(s.LinearMotion,axis)==AlsJointMotion.Locked;
                var angular=motion==AlsJointMotion.Locked||(motion==AlsJointMotion.Limited&&!soft.Enabled);
                if(PhysicsServer3D.Generic6DofJointGetFlag(_springs[j],a,PhysicsServer3D.G6DofJointAxisFlag.EnableLinearLimit)!=linear||
                   PhysicsServer3D.Generic6DofJointGetFlag(_springs[j],a,PhysicsServer3D.G6DofJointAxisFlag.EnableAngularLimit)!=angular)
                    throw new InvalidOperationException("Limit channel lost its authored hard constraints.");
            }
            if(PhysicsServer3D.Generic6DofJointGetFlag(_springs[j],a,PhysicsServer3D.G6DofJointAxisFlag.EnableAngularSpring)!=_springEnabled[j*3+axis])
                throw new InvalidOperationException("Backend did not accept angular spring flag.");
            for(var field=0;field<3;field++)
            {
                var key=field==0?PhysicsServer3D.G6DofJointAxisParam.AngularSpringStiffness:field==1?PhysicsServer3D.G6DofJointAxisParam.AngularSpringDamping:PhysicsServer3D.G6DofJointAxisParam.AngularSpringEquilibriumPoint;
                if(PhysicsServer3D.Generic6DofJointGetParam(_springs[j],a,key)!=_springCache[j*9+axis*3+field])
                    throw new InvalidOperationException("Backend did not accept angular spring value.");
            }
        }
    }

    // Fixture probe: no physics step runs between these drive changes. The
    // backend limit channel must retain its own coefficients and target.
    internal void VerifyDriveIsolation(double dt)
    {
        Check();var previousStiffness=_driveStiffness;var previousDamping=_driveDamping;
        try
        {
            SetEffectiveAngularDrive(75,1.5);Step(dt);VerifySpringReadback();
            if(LastDriveRowCount==0||LastLimitRowCount==0)
                throw new InvalidOperationException("Isolation probe requires simultaneous drive and limit rows.");
            var limits=ReadChannelState(Limit);var drives=ReadChannelState(Drive);
            SetEffectiveAngularDrive(37500,0);Step(dt);VerifySpringReadback();
            if(!limits.SequenceEqual(ReadChannelState(Limit))||drives.SequenceEqual(ReadChannelState(Drive)))
                throw new InvalidOperationException("Drive update changed limits or failed to reach its own channel.");
            SetEffectiveAngularDrive(0,0);Step(dt);VerifySpringReadback();
            if(LastDriveRowCount!=0||LastLimitRowCount==0||!limits.SequenceEqual(ReadChannelState(Limit)))
                throw new InvalidOperationException("Disabling drives disturbed active limits.");
        }
        finally{_driveStiffness=previousStiffness;_driveDamping=previousDamping;Step(dt);}
    }

    private float[] ReadChannelState(int channel)
    {
        var result=new float[_joints.Length*12];
        for(var joint=0;joint<_joints.Length;joint++)
        {
            var rid=_springs[joint*2+channel];if(!rid.IsValid)continue;
            if(rid==_springs[joint*2+1-channel])throw new InvalidOperationException("Drive and limit share a backend constraint.");
            for(var axis=0;axis<3;axis++)
            {
                var a=(Vector3.Axis)axis;var offset=joint*12+axis*4;
                result[offset]=PhysicsServer3D.Generic6DofJointGetParam(rid,a,PhysicsServer3D.G6DofJointAxisParam.AngularSpringStiffness);
                result[offset+1]=PhysicsServer3D.Generic6DofJointGetParam(rid,a,PhysicsServer3D.G6DofJointAxisParam.AngularSpringDamping);
                result[offset+2]=PhysicsServer3D.Generic6DofJointGetParam(rid,a,PhysicsServer3D.G6DofJointAxisParam.AngularSpringEquilibriumPoint);
                result[offset+3]=PhysicsServer3D.Generic6DofJointGetFlag(rid,a,PhysicsServer3D.G6DofJointAxisFlag.EnableAngularSpring)?1:0;
            }
        }
        return result;
    }

    internal void VerifyInertiaReadback()
    {
        Check();
        for(var i=0;i<_bodies.BodyCount;i++)
        {
            var body=_bodies.BodyAt(i);if(body.Freeze)continue;
            var state=PhysicsServer3D.BodyGetDirectState(body.GetRid())??throw new InvalidOperationException("No backend inertia state.");
            var axes=state.Transform.Basis.Orthonormalized();var inertia=body.Inertia;
            var expected=axes*Basis.FromScale(new(1/inertia.X,1/inertia.Y,1/inertia.Z))*axes.Transposed();
            for(var axis=0;axis<3;axis++)
                if((expected[axis]-state.InverseInertiaTensor[axis]).Length()/MathF.Max(expected[axis].Length(),1)>1e-4f)
                    throw new InvalidOperationException("Backend did not accept conditioned principal inertia.");
        }
    }

    internal (Transform3D Parent,Transform3D Child) Frames(int index)
    {
        Check();var j=_definition.Joints[index];
        return(_bodies.BodyAt(j.ParentBody).GlobalTransform*_parentFrames[index],_bodies.BodyAt(j.ChildBody).GlobalTransform*_childFrames[index]);
    }
    private static void Param(Rid rid,Vector3.Axis axis,PhysicsServer3D.G6DofJointAxisParam key,float value)
        =>PhysicsServer3D.Generic6DofJointSetParam(rid,axis,key,value);
    private static Basis InverseInertia(RigidBody3D b)
    {
        if(b.Freeze)return new(Vector3.Zero,Vector3.Zero,Vector3.Zero);
        var basis=b.GlobalBasis.Orthonormalized();var i=b.Inertia;
        return basis*Basis.FromScale(new(1/i.X,1/i.Y,1/i.Z))*basis.Transposed();
    }
    private static Quaternion Advance(Quaternion q,Vector3 w,double dt)
    {var dq=new Quaternion(w.X,w.Y,w.Z,0)*q;var h=(float)dt*.5f;return new Quaternion(q.X+dq.X*h,q.Y+dq.Y*h,q.Z+dq.Z*h,q.W+dq.W*h).Normalized();}
    private static Quaternion Predict(Quaternion connector,RigidBody3D body,double dt)
    {
        // Chaos and Jolt both apply authored angular drag before integration.
        // This predicts only: the backend still applies the actual damping once.
        var velocity=body.Freeze?Vector3.Zero:body.AngularVelocity*Mathf.Max(0,1-body.AngularDamp*(float)dt);
        return Advance(connector,velocity,dt);
    }
    private static AlsDoubleVector Angles(Quaternion p,Quaternion c)
    {if(p.Dot(c)<0)c=-c;return AlsJointAngularKinematics.Evaluate(new AlsQuaternion(p.X,p.Y,p.Z,p.W).Normalized(),new AlsQuaternion(c.X,c.Y,c.Z,c.W).Normalized()).Angles;}
    private static AlsQuaternion Core(Quaternion q)=>new AlsQuaternion(q.X,q.Y,q.Z,q.W).Normalized();
    private static void Validate(AlsPhysicsJointSettings s)
    {
        if(s.LinearMotion.X==AlsJointMotion.Limited||s.LinearMotion.Y==AlsJointMotion.Limited||s.LinearMotion.Z==AlsJointMotion.Limited||
            s.AngularDrive.SlerpPosition||s.AngularDrive.SlerpVelocity||s.ShockPropagationEnabled||s.ParentInvMassScale!=1||
            s.AngularDrive.MaxTorque!=AlsDoubleVector.Zero||s.AngularDrive.VelocityTarget!=AlsDoubleVector.Zero)
            throw new NotSupportedException("Joint mode needs a separate solver adapter.");
    }
    private static bool Unconstrained(AlsPhysicsJointSettings s)=>s.LinearMotion==new AlsJointMotions(0,0,0)&&s.AngularMotion==new AlsJointMotions(0,0,0)&&
        !s.AngularDrive.TwistPosition&&!s.AngularDrive.TwistVelocity&&!s.AngularDrive.SwingPosition&&!s.AngularDrive.SwingVelocity;
    private static AlsJointMotion Motion(AlsJointMotions m,int i)=>i==0?m.X:i==1?m.Y:m.Z;
    private static double Component(AlsDoubleVector v,int i)=>i==0?v.X:i==1?v.Y:v.Z;
    private static void Main(){if(!GodotThread.IsMainThread())throw new InvalidOperationException("Joint access requires Main.");}
    private void Check(){Main();ObjectDisposedException.ThrowIf(_disposed,this);}
    public void Dispose()
    {
        Main();if(_disposed)return;_disposed=true;
        foreach(var rid in _springs)if(rid.IsValid)PhysicsServer3D.FreeRid(rid);
        Array.Clear(_springs);Array.Clear(_joints);
        for(var i=0;i<_previousInertia.Length;i++)_bodies.BodyAt(i).Inertia=_previousInertia[i];
        BoundJointCount=0;
    }
}

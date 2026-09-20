using System.Numerics;
using GodotAls.Core.Locomotion;
using M = System.Math;

namespace GodotAls.Core.Physics;

public enum AlsAngularMotion { Free, Limited, Locked }

// UE axis order: X twist, Y swing2, Z swing1. Disabled drives have zero K/C.
public readonly record struct AlsAngularAxisSettings(AlsAngularMotion Motion, double Limit,
    bool SoftLimit, double LimitStiffness, double LimitDamping, double DriveStiffness, double DriveDamping);

public readonly record struct AlsAngularJointSettings(
    AlsAngularAxisSettings X, AlsAngularAxisSettings Y, AlsAngularAxisSettings Z,
    AlsQuaternion DriveTarget, bool ConditionMass = true, bool LimitAcceleration = true,
    bool DriveAcceleration = true, bool UseSimd = true, double HardStiffness = 1,
    double AngleTolerance = .001f, double MinParentMassRatio = .2f, double MaxInertiaRatio = 5);

public readonly record struct AlsAngularSolverBody(AlsQuaternion Initial, AlsQuaternion Predicted,
    AlsQuaternion Connector, AlsJointInverseMass InverseMass);

// Cached Chaos position-phase angular rows. No world access or shared-body mass
// mutation. Construct once per physics step (resets lambda), then interleave
// SolveLimits / SolveDrives with the world's linear and contact constraints.
// Scope: unit parent mass scale, no shock propagation, zero drive velocity
// target, unlimited drive torque, swing/twist drives (no SLERP drive).
// This is not a Jolt adapter or a complete rigid-body integrator.
public struct AlsCachedAngularJoint
{
    private struct Row
    {
        public AlsDoubleVector Axis, ParentResponse, ChildResponse;
        public double Error, Limit, InverseMass, K, C, Denominator, Lambda;
        public bool Active, Soft, Limited;
    }

    private readonly record struct Tensor(AlsDoubleVector X, AlsDoubleVector Y, AlsDoubleVector Z)
    {
        public static Tensor World(AlsQuaternion q, AlsJointInverseMass mass)
        {
            if (mass.Mass == 0) return default;
            var x = new AlsDoubleVector(1,0,0).Rotate(q);
            var y = new AlsDoubleVector(0,1,0).Rotate(q);
            var z = new AlsDoubleVector(0,0,1).Rotate(q);
            var i = mass.Inertia;
            return new(x*(i.X*x.X)+y*(i.Y*y.X)+z*(i.Z*z.X),
                x*(i.X*x.Y)+y*(i.Y*y.Y)+z*(i.Z*z.Y),
                x*(i.X*x.Z)+y*(i.Y*y.Z)+z*(i.Z*z.Z));
        }
        public AlsDoubleVector Multiply(AlsDoubleVector a, bool single) => single
            ? new(X.ToSingle()*(float)a.X + (Y.ToSingle()*(float)a.Y + Z.ToSingle()*(float)a.Z))
            : X*a.X+Y*a.Y+Z*a.Z;
    }

    private Row _limitX, _limitY, _limitZ, _driveX, _driveY, _driveZ;
    private readonly AlsDoubleVector _parentWdt, _childWdt;
    private readonly double _tolerance, _hardStiffness;
    public bool SimultaneousLimits { get; }
    public bool SimultaneousDrives { get; }
    public AlsDoubleVector LimitLambda => new(_limitX.Lambda,_limitY.Lambda,_limitZ.Lambda);
    public AlsDoubleVector DriveLambda => new(_driveX.Lambda,_driveY.Lambda,_driveZ.Lambda);

    public AlsCachedAngularJoint(in AlsAngularSolverBody parent, in AlsAngularSolverBody child,
        in AlsAngularJointSettings settings, double dt)
    {
        this = default;
        Validate(parent); Validate(child); Validate(settings.X); Validate(settings.Y); Validate(settings.Z);
        Validate(settings.DriveTarget);
        if (!double.IsFinite(dt) || dt <= 0 || !double.IsFinite(settings.AngleTolerance) || settings.AngleTolerance < 0 ||
            !double.IsFinite(settings.HardStiffness) || settings.HardStiffness is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(dt));
        if ((settings.Y.Motion == AlsAngularMotion.Free && settings.Z.Motion == AlsAngularMotion.Limited) ||
            (settings.Z.Motion == AlsAngularMotion.Free && settings.Y.Motion == AlsAngularMotion.Limited))
            throw new NotSupportedException("One free and one limited swing needs dual-cone rows; ALS assets do not use this mode.");
        var masses = AlsJointMassConditioning.Apply(parent.InverseMass,child.InverseMass,
            settings.ConditionMass ? settings.MinParentMassRatio : 0,settings.ConditionMass ? settings.MaxInertiaRatio : 0);
        var pi = Tensor.World(parent.Predicted,masses.Parent); var ci = Tensor.World(child.Predicted,masses.Child);
        var p = parent.Predicted*parent.Connector; var c = child.Predicted*child.Connector;
        if (AlsQuaternion.Dot(p,c)<0) c=-c;
        _parentWdt = AngularDelta(parent.Initial*parent.Connector,p);
        _childWdt = AngularDelta(child.Initial*child.Connector,c);
        _tolerance = M.Min(1,3600*dt*dt)*settings.AngleTolerance;
        _hardStiffness = settings.HardStiffness;
        var g = AlsJointAngularKinematics.Evaluate(p,c); var relative = p.Conjugate()*c;
        SimultaneousLimits = settings.UseSimd && IsSoftLimited(settings.X) && IsSoftLimited(settings.Y) && IsSoftLimited(settings.Z);
        var degenerate = AlsDoubleVector.Dot(new AlsDoubleVector(1,0,0).Rotate(p),g.TwistAxis)<-.998f;
        _limitX = MakeLimit(settings.X,g.TwistAxis,g.LockedX,g.Angles.X,relative.X,pi,ci,settings.LimitAcceleration,SimultaneousLimits,dt);
        _limitY = MakeLimit(settings.Y,g.PyramidY,g.LockedY,g.Angles.Y,relative.Y,pi,ci,settings.LimitAcceleration,SimultaneousLimits,dt);
        _limitZ = MakeLimit(settings.Z,g.PyramidZ,g.LockedZ,g.Angles.Z,relative.Z,pi,ci,settings.LimitAcceleration,SimultaneousLimits,dt);
        if (!SimultaneousLimits && degenerate)
        {
            if (_limitX.Limited) _limitX.Active=false;
            if (settings.Z.Motion == AlsAngularMotion.Locked && _limitY.Limited) _limitY.Active=false;
            if (settings.Y.Motion == AlsAngularMotion.Locked && _limitZ.Limited) _limitZ.Active=false;
        }
        var error = AlsJointAngularKinematics.SwingTwistDriveError(p,c,settings.DriveTarget);
        var dx = DriveActive(settings.X,error.X,_tolerance);
        var dy = DriveActive(settings.Y,error.Y,_tolerance);
        var dz = DriveActive(settings.Z,error.Z,_tolerance);
        SimultaneousDrives = settings.UseSimd && dx && dy && dz;
        _driveX = MakeDrive(settings.X,new AlsDoubleVector(1,0,0).Rotate(c),error.X,pi,ci,settings.DriveAcceleration,SimultaneousDrives,dt,dx);
        _driveY = MakeDrive(settings.Y,new AlsDoubleVector(0,1,0).Rotate(c),error.Y,pi,ci,settings.DriveAcceleration,SimultaneousDrives,dt,dy);
        _driveZ = MakeDrive(settings.Z,new AlsDoubleVector(0,0,1).Rotate(c),error.Z,pi,ci,settings.DriveAcceleration,SimultaneousDrives,dt,dz);
    }

    public void SolveLimits(ref Vector3 parentDQ, ref Vector3 childDQ) =>
        Solve(ref _limitX,ref _limitY,ref _limitZ,ref parentDQ,ref childDQ,SimultaneousLimits,false);
    public void SolveDrives(ref Vector3 parentDQ, ref Vector3 childDQ) =>
        Solve(ref _driveX,ref _driveY,ref _driveZ,ref parentDQ,ref childDQ,SimultaneousDrives,true);

    private void Solve(ref Row x,ref Row y,ref Row z,ref Vector3 p,ref Vector3 c,bool simultaneous,bool drive)
    {
        if (simultaneous)
        {
            var dp=Vector3.Zero; var dc=Vector3.Zero;
            ApplySingle(ref x,p,c,ref dp,ref dc,drive);
            ApplySingle(ref y,p,c,ref dp,ref dc,drive);
            ApplySingle(ref z,p,c,ref dp,ref dc,drive);
            p+=dp; c+=dc;
        }
        else
        {
            ApplyDouble(ref x,ref p,ref c,drive);
            ApplyDouble(ref y,ref p,ref c,drive);
            ApplyDouble(ref z,ref p,ref c,drive);
        }
    }

    private void ApplyDouble(ref Row r,ref Vector3 p,ref Vector3 c,bool drive)
    {
        if (!r.Active) return;
        var error = r.Error+AlsDoubleVector.Dot(new(c-p),r.Axis);
        if (!drive && r.Limited && !Outside(ref error,r.Limit,_tolerance)) return;
        if (drive && r.K<=1e-4f) error=0;
        var velocity = r.C>1e-4f ? AlsDoubleVector.Dot(r.Axis,new AlsDoubleVector(p)+_parentWdt-new AlsDoubleVector(c)-_childWdt) : 0;
        var delta = r.Soft ? (r.K*error-r.C*velocity-r.Lambda)/r.Denominator : _hardStiffness*error/r.InverseMass;
        r.Lambda+=delta;
        // Limits use the full inverse tensor response; drives project that
        // response onto the drive axis. The two channels have separate lambdas.
        var pr = drive ? r.Axis*(delta*AlsDoubleVector.Dot(r.Axis,r.ParentResponse)) : r.ParentResponse*delta;
        var cr = drive ? r.Axis*(delta*AlsDoubleVector.Dot(r.Axis,r.ChildResponse)) : r.ChildResponse*delta;
        p+=pr.ToSingle(); c+=cr.ToSingle();
    }

    private void ApplySingle(ref Row r,Vector3 p,Vector3 c,ref Vector3 dp,ref Vector3 dc,bool drive)
    {
        if (!r.Active) return;
        var axis=r.Axis.ToSingle(); var error=(float)r.Error+Vector3.Dot(c-p,axis);
        if (!drive)
        {
            var limit=(float)r.Limit;
            if (error>limit) error-=limit;
            else if (error < -limit) error+=limit;
            else return;
            if (MathF.Abs(error)<=(float)_tolerance) return;
        }
        var w = drive ? (p+_parentWdt.ToSingle())-(c+_childWdt.ToSingle())
            : _parentWdt.ToSingle()-((c-p)+_childWdt.ToSingle());
        var velocity=Vector3.Dot(axis,w);
        var delta=((float)r.K*error-((float)r.C*velocity+(float)r.Lambda))/(float)r.Denominator;
        r.Lambda=(float)r.Lambda+delta;
        var pr=r.ParentResponse.ToSingle(); var cr=r.ChildResponse.ToSingle();
        if (drive)
        {
            var impulse=axis*delta;
            dp+=impulse*Vector3.Dot(axis,pr); dc+=impulse*Vector3.Dot(axis,cr);
        }
        else { dp+=pr*delta; dc+=cr*delta; }
    }

    private static Row MakeLimit(AlsAngularAxisSettings s,AlsDoubleVector axis,AlsDoubleVector locked,
        double angle,double lockedError,Tensor pi,Tensor ci,bool acceleration,bool single,double dt)
    {
        if (s.Motion==AlsAngularMotion.Free) return default;
        var limited=s.Motion==AlsAngularMotion.Limited;
        if (!limited) { axis=locked; angle=lockedError; }
        if (angle<0) { angle=-angle; axis*= -1; }
        var row=MakeRow(axis,angle,pi,ci,limited && s.SoftLimit,s.LimitStiffness,s.LimitDamping,acceleration,single,dt);
        row.Limited=limited; row.Limit=single?(float)s.Limit:s.Limit;
        return row;
    }

    private static Row MakeDrive(AlsAngularAxisSettings s,AlsDoubleVector axis,double error,Tensor pi,Tensor ci,
        bool acceleration,bool single,double dt,bool active) => active
        ? MakeRow(axis,error,pi,ci,true,s.DriveStiffness,s.DriveDamping,acceleration,single,dt) : default;

    private static Row MakeRow(AlsDoubleVector axis,double error,Tensor pi,Tensor ci,bool soft,
        double stiffness,double damping,bool acceleration,bool single,double dt)
    {
        if (single) axis=new(axis.ToSingle());
        var p=pi.Multiply(axis,single); var c=ci.Multiply(axis,single)*-1;
        var im=single ? Vector3.Dot(axis.ToSingle(),p.ToSingle())-Vector3.Dot(axis.ToSingle(),c.ToSingle())
            : AlsDoubleVector.Dot(axis,p)-AlsDoubleVector.Dot(axis,c);
        if (im<=0) return default;
        var scale=acceleration?1/im:1;
        var k=scale*stiffness*dt*dt; var d=scale*damping*dt; var denom=1+(k+d)*im;
        if (single)
        {
            var sf=acceleration?1/(float)im:1; var tf=(float)dt;
            k=(sf*(float)stiffness)*(tf*tf); d=sf*((float)damping*tf);
            denom=1+((float)k+(float)d)*(float)im;
        }
        if (!double.IsFinite(im)||!double.IsFinite(k)||!double.IsFinite(d)||!double.IsFinite(denom)||
            !p.IsFinite||!c.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(stiffness),"Angular row coefficients overflowed.");
        return new(){Axis=axis,ParentResponse=p,ChildResponse=c,Error=single?(float)error:error,
            InverseMass=im,K=k,C=d,Denominator=denom,Soft=soft,Active=true};
    }

    private static bool Outside(ref double error,double limit,double tolerance)
    {
        if (error>limit) error-=limit;
        else if (error < -limit) error+=limit;
        else return false;
        return M.Abs(error)>tolerance;
    }
    private static bool IsSoftLimited(AlsAngularAxisSettings s)=>s.Motion==AlsAngularMotion.Limited && s.SoftLimit;
    private static bool DriveActive(AlsAngularAxisSettings s,double error,double tolerance)=>s.Motion!=AlsAngularMotion.Locked &&
        ((M.Abs(error)>tolerance && s.DriveStiffness>0)||s.DriveDamping>0);
    private static AlsDoubleVector AngularDelta(AlsQuaternion initial,AlsQuaternion predicted)
    {
        if (AlsQuaternion.Dot(initial,predicted)<0) predicted=-predicted;
        var w=(predicted + -initial)*initial.Conjugate()*2;
        return new(w.X,w.Y,w.Z);
    }
    private static void Validate(AlsAngularSolverBody b) { Validate(b.Initial); Validate(b.Predicted); Validate(b.Connector); }
    private static void Validate(AlsQuaternion q)
    {
        if (!double.IsFinite(q.LengthSquared)||M.Abs(q.LengthSquared-1)>1e-6)
            throw new ArgumentException("Angular rows require finite unit rotations.");
    }
    private static void Validate(AlsAngularAxisSettings s)
    {
        if (s.Motion is < AlsAngularMotion.Free or > AlsAngularMotion.Locked)
            throw new ArgumentOutOfRangeException(nameof(s));
        ReadOnlySpan<double> values=stackalloc double[]{s.Limit,s.LimitStiffness,s.LimitDamping,s.DriveStiffness,s.DriveDamping};
        foreach(var v in values) if (!double.IsFinite(v)||v<0) throw new ArgumentOutOfRangeException(nameof(s));
    }
}

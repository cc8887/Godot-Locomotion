using M=System.Math;
namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFootPlane(AlsDoubleVector Normal,double W)
{
    public static AlsFootPlane At(AlsDoubleVector point,AlsDoubleVector normal)=>new(normal,AlsDoubleVector.Dot(point,normal));
    public double Dot(AlsDoubleVector point)=>AlsDoubleVector.Dot(Normal,point)-W;
    public AlsDoubleVector Intersect(AlsDoubleVector point,AlsDoubleVector direction)=>point+direction*((W-AlsDoubleVector.Dot(point,Normal))/AlsDoubleVector.Dot(direction,Normal));
    public float Distance(AlsDoubleVector point,AlsDoubleVector approach)
    {if(M.Abs(AlsDoubleVector.Dot(approach,Normal))<=(double)1e-4f)return 0;return (float)AlsDoubleVector.Dot(point-Intersect(point,approach*-1),approach*-1);}
    public AlsFootPlane Transform(in AlsPrecisePose t)
    {
        var origin=Normal*W;var normal=new AlsDoubleVector(Normal.X/t.Scale.X,Normal.Y/t.Scale.Y,Normal.Z/t.Scale.Z).Rotate(t.Rotation);
        normal=AlsFootPlacementMath.SafeNormal(normal);return At(AlsFootPlacementMath.Position(t,origin),normal);
    }
    public AlsFootPlane Translate(AlsDoubleVector delta)=>this with{W=W+AlsDoubleVector.Dot(delta,Normal)};
}
public readonly record struct AlsFootFloatSpring(float Velocity,float Target,bool Valid);
public readonly record struct AlsFootVectorSpring(AlsDoubleVector Velocity,AlsDoubleVector Target,bool Valid);
public readonly record struct AlsFootQuaternionSpring(AlsDoubleVector Velocity,AlsQuaternion Target,bool Valid)
{public static AlsFootQuaternionSpring Default=>new(default,AlsQuaternion.Identity,false);}

internal static class AlsFootPlacementMath
{
    public static readonly AlsDoubleVector Down=new(0,0,-1),Up=new(0,0,1);
    public static AlsDoubleVector Position(in AlsPrecisePose t,AlsDoubleVector point)=>(t.Scale*point).Rotate(t.Rotation)+t.Position;
    public static AlsDoubleVector InversePosition(in AlsPrecisePose t,AlsDoubleVector point)
    {var v=(point-t.Position).Rotate(t.Rotation.Conjugate());return new(v.X/t.Scale.X,v.Y/t.Scale.Y,v.Z/t.Scale.Z);}
    public static AlsPrecisePose Inverse(in AlsPrecisePose t)
    {var s=new AlsDoubleVector(1/t.Scale.X,1/t.Scale.Y,1/t.Scale.Z);var q=t.Rotation.Conjugate();return new((s*(t.Position*-1)).Rotate(q),q,s);}
    public static AlsDoubleVector SafeNormal(AlsDoubleVector v)=>v.SafeNormal();
    public static bool NearlyZero(AlsDoubleVector v)=>v.NearlyZero(1e-4f);
    public static AlsDoubleVector ClampSize(AlsDoubleVector v,float max)=>v.LengthSquared>max*max?SafeNormal(v)*max:v;
    public static AlsDoubleVector SphereLine(AlsDoubleVector center,float radius,AlsDoubleVector origin,AlsDoubleVector direction)
    {
        var oc=center-origin;var b=-2d*AlsDoubleVector.Dot(direction,oc);var c=oc.LengthSquared-(double)(radius*radius);var d=b*b-4d*c;
        if(d<=(double)1e-4f)return center+SafeNormal(origin+direction*(-b*.5)-center)*radius;
        var e=M.Sqrt(d);var t1=(-b+e)*.5;var t2=(-b-e)*.5;var t=M.Abs(t1)==M.Abs(t2)?M.Abs(t1):M.Abs(t1)<M.Abs(t2)?t1:t2;
        return origin+direction*t;
    }
    public static AlsQuaternion BetweenVectors(AlsDoubleVector a,AlsDoubleVector b)
    {
        var n=M.Sqrt(a.LengthSquared*b.LengthSquared);var w=n+AlsDoubleVector.Dot(a,b);
        var axis=w>=1e-6f*n?AlsDoubleVector.Cross(a,b):AlsDoubleVector.Cross(a,M.Abs(a.X)>M.Abs(a.Y)&&M.Abs(a.X)>M.Abs(a.Z)?new(0,1,0):new(-1,0,0));
        if(w<1e-6f*n)w=0;return AlsComponentQuaternion.Normalize(new AlsQuaternion(axis.X,axis.Y,axis.Z,w));
    }
    public static AlsQuaternion Twist(AlsQuaternion q,AlsDoubleVector axis)
    {var p=axis*AlsDoubleVector.Dot(axis,new(q.X,q.Y,q.Z));var t=new AlsQuaternion(p.X,p.Y,p.Z,q.W);return t.LengthSquared==0?AlsQuaternion.Identity:AlsComponentQuaternion.Normalize(t);}
    public static AlsQuaternion Slerp(AlsQuaternion a,AlsQuaternion b,double alpha)
    {
        var dot=AlsQuaternion.Dot(a,b);var sign=dot>=0?1d:-1d;dot*=sign;var first=1d-alpha;var second=alpha*sign;
        if(dot<.9999){var omega=M.Acos(M.Clamp(dot,-1,1));var inv=1/M.Sin(omega);first=M.Sin(first*omega)*inv;second=M.Sin(second*omega)*inv;}
        return AlsComponentQuaternion.Normalize(a*first+b*second);
    }
    private static float W(float stiffness)=>MathF.Sqrt(stiffness)/(2f*MathF.PI)*(2f*MathF.PI);
    private static float E(float x)=>1f/(1f+1.00746054f*x+.45053901f*x*x+.25724632f*x*x*x);
    public static float Spring(float current,float target,ref AlsFootFloatSpring state,float delta,float stiffness)
    {
        if(delta<=1e-8f)return current;var velocity=state.Velocity;
        AlsRefactoredSpring.Evaluate(ref current,ref velocity,target,0,delta,MathF.Sqrt(stiffness)/(2f*MathF.PI),1,true);
        state=new(velocity,target,true);return current;
    }
    public static AlsDoubleVector Spring(AlsDoubleVector current,AlsDoubleVector target,ref AlsFootVectorSpring state,float delta,float stiffness)
    {
        if(delta<=1e-8f)return current;var w=W(stiffness);var e=E(w*delta);var error=current-target;var c2=state.Velocity+error*w;
        current=target+(error+c2*delta)*e;state=new((c2-error*w-c2*(w*delta))*e,target,true);return current;
    }
    public static AlsQuaternion Spring(AlsQuaternion current,AlsQuaternion target,ref AlsFootQuaternionSpring state,float delta,float stiffness)
    {
        if(AlsComponentQuaternion.Dot(target,current)<0)target*= -1;
        var velocity=current*new AlsQuaternion(state.Velocity.X,state.Velocity.Y,state.Velocity.Z,0)*.5;
        if(delta>1e-8f)
        {
            var w=W(stiffness);var e=E(w*delta);var error=current+target*-1;var c2=velocity+error*w;
            current=target+(error+c2*delta)*e;velocity=(c2+error*(-w)+c2*(-(w*delta)))*e;
            state=state with{Target=target,Valid=true};
        }
        current=AlsComponentQuaternion.Normalize(current);var angular=current.Conjugate()*2*velocity;
        state=state with{Velocity=new(angular.X,angular.Y,angular.Z)};return current;
    }
}

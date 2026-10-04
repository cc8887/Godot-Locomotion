using System.Numerics;
using ScalarMath = System.Math;

namespace GodotAls.Core.Locomotion;

public static class AlsCharacterSweepMath
{
    public static float Dot(Vector3 a,Vector3 b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    public static Vector3 Normalize(Vector3 v)
    {
        float squared=Dot(v,v);if(squared==0)return Vector3.Zero;
        float length=MathF.Sqrt(squared);return new(v.X/length,v.Y/length,v.Z/length);
    }
    public static Vector3 ProjectPlane(Vector3 value,Vector3 normal)=>value-normal*Dot(value,normal);
    public static Vector3 LimitLength(Vector3 value,float maximum=1)
    {
        float length=MathF.Sqrt(Dot(value,value));
        if(length>0&&maximum<length)
        {value=new(value.X/length,value.Y/length,value.Z/length);value*=maximum;}
        return value;
    }
    // Axis-angle scalar matrix order at the binary32 input boundary. This is
    // distinct from UE rotator degrees and the existing double pose math.
    public static Vector3 Rotate(Vector3 value,Vector3 axis,float radians)
    {
        var squared=new Vector3(axis.X*axis.X,axis.Y*axis.Y,axis.Z*axis.Z);
        var (sin,cos)=MathF.SinCos(radians);float t=1-cos;
        float xy=axis.X*axis.Y*t,zs=axis.Z*sin;
        float xz=axis.X*axis.Z*t,ys=axis.Y*sin;
        float yz=axis.Y*axis.Z*t,xs=axis.X*sin;
        var row0=new Vector3(squared.X+cos*(1-squared.X),xy-zs,xz+ys);
        var row1=new Vector3(xy+zs,squared.Y+cos*(1-squared.Y),yz-xs);
        var row2=new Vector3(xz-ys,yz+xs,squared.Z+cos*(1-squared.Z));
        return new(Dot(row0,value),Dot(row1,value),Dot(row2,value));
    }
    public static float PenetrationDepth(Vector3 origin,Vector3 capsuleAxis,Vector3 point,Vector3 normal,float half,float radius)
    {
        var axis=Normalize(capsuleAxis);
        var support=origin-axis*(Dot(normal,axis)>=0?half-radius:radius-half)-normal*radius;
        return ScalarMath.Max(0,Dot(point-support,normal));
    }

    // PrimitiveComponent PullBackHit: preserve scalar length/reduction order.
    public static float PullBackFraction(Vector3 motion,float rawTime)
    {
        float cm=MathF.Sqrt(motion.X*motion.X+motion.Y*motion.Y+motion.Z*motion.Z)*100;
        if(cm==0)return rawTime;
        float back=ScalarMath.Clamp(.1f,.1f/cm,1f/cm)+.001f;
        return ScalarMath.Clamp(rawTime-back,0,1);
    }
}

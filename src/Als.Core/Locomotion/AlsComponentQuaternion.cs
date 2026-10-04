using M=System.Math;
namespace GodotAls.Core.Locomotion;

// UE Win64 SSE2 VectorDot4/Normalize used by FCSPose and native LegIK.
// Grouping matters when the quaternion blend's shortest-route dot is zero.
internal static class AlsComponentQuaternion
{
    internal static double Dot(AlsQuaternion a,AlsQuaternion b)=>(a.X*b.X+a.Z*b.Z)+(a.Y*b.Y+a.W*b.W);
    internal static AlsQuaternion Normalize(AlsQuaternion q)
    {var length=Dot(q,q);return length<(double)1e-8f?AlsQuaternion.Identity:q*(1/M.Sqrt(length));}
    internal static AlsPrecisePose Normalize(in AlsPrecisePose pose)=>pose with{Rotation=Normalize(pose.Rotation)};
    internal static AlsQuaternion Between(AlsDoubleVector a,AlsDoubleVector b)
    {
        var w=1+AlsDoubleVector.Dot(a,b);AlsDoubleVector axis;
        if(w>=(double)1e-6f)axis=AlsDoubleVector.Cross(a,b);
        else{w=0;axis=AlsDoubleVector.Cross(a,M.Abs(a.X)>M.Abs(a.Y)&&M.Abs(a.X)>M.Abs(a.Z)?new(0,1,0):new(-1,0,0));}
        return Normalize(new AlsQuaternion(axis.X,axis.Y,axis.Z,w));
    }
}

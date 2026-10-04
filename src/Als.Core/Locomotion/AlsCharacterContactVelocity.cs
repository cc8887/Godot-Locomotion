using System.Numerics;

namespace GodotAls.Core.Locomotion;

public interface IAlsCharacterContactNormals
{
    int Count {get;}
    Vector3 Normal(int index);
}

// End-of-step velocity projection for a backend move. Preserve physical
// contact order and only remove components pointing into each contact.
public static class AlsCharacterContactVelocity
{
    public static Vector3 Resolve(Vector3 velocity,bool grounded,IAlsCharacterContactNormals contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        for(int i=0;i<contacts.Count;i++)
        {
            var normal=contacts.Normal(i);
            if(AlsCharacterSweepMath.Dot(velocity,normal)<0)velocity=AlsCharacterSweepMath.ProjectPlane(velocity,normal);
        }
        if(grounded&&velocity.Y<0)velocity.Y=0;
        return velocity;
    }
}

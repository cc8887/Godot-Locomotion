using Godot;
using GodotAls.Core.Locomotion;
using NVector=System.Numerics.Vector3;

namespace GodotAls.Locomotion;

// Compare Core with the actual loaded Godot float operators, independently of
// the source checkout used to locate their arithmetic ordering.
public partial class LyraCharacterInputContactSmoke:Node
{
    private sealed class Contacts(Vector3[] values):IAlsCharacterContactNormals
    {
        public int Count=>values.Length;
        public NVector Normal(int index)=>N(values[index]);
    }
    private static NVector N(Vector3 v)=>new(v.X,v.Y,v.Z);
    private static void Equal(NVector actual,Vector3 expected,string label)
    {
        if(BitConverter.SingleToInt32Bits(actual.X)!=BitConverter.SingleToInt32Bits(expected.X)||
            BitConverter.SingleToInt32Bits(actual.Y)!=BitConverter.SingleToInt32Bits(expected.Y)||
            BitConverter.SingleToInt32Bits(actual.Z)!=BitConverter.SingleToInt32Bits(expected.Z))
            throw new InvalidOperationException($"Core/Godot float bits differ: {label} actual={actual} expected={expected}");
    }
    public override void _Ready()
    {
        try
        {
            int inputs=0,contacts=0,rotations=0;
            for(int i=0;i<512;i++)
            {
                var v=new Vector3((i%17-8)*.31f,0,(i%23-11)*.21f);float yaw=(i-256)*.037f;
                foreach(bool world in new[]{false,true})foreach(float scale in new[]{0f,.25f,.5f,1f})
                {
                    var expected=v.LimitLength();if(!world)expected=expected.Rotated(Vector3.Up,yaw);if(scale!=1)expected*=scale;
                    Equal(AlsCharacterInput.Consume(N(v),N(Vector3.Up),yaw,world,scale),expected,$"input {i}/{world}/{scale}");inputs++;
                }
                var axis=new Vector3(i%3+.1f,i%5-.7f,i%7-.9f).Normalized();
                Equal(AlsCharacterSweepMath.Rotate(N(v),N(axis),yaw),v.Rotated(axis,yaw),$"axis {i}");rotations++;
                var ns=new[]{Vector3.Right,new Vector3(-1,1,0).Normalized(),Vector3.Back};
                foreach(bool ground in new[]{false,true})
                {
                    var velocity=new Vector3(v.X*17,(i%13-6)*2.1f,v.Z*19);var expected=velocity;
                    foreach(var normal in ns)if(expected.Dot(normal)<0)expected=expected.Slide(normal);
                    if(ground&&expected.Y<0)expected.Y=0;
                    Equal(AlsCharacterContactVelocity.Resolve(N(velocity),ground,new Contacts(ns)),expected,$"contact {i}/{ground}");contacts++;
                }
            }
            GD.Print($"LYRA_INPUT_CONTACT_CORE_GODOT_OK inputs={inputs} rotations={rotations} contacts={contacts} bitMismatches=0 actualGodotOperators=true");GetTree().Quit();
        }
        catch(Exception e){GD.PushError("Input/contact Core smoke failed: "+e);GetTree().Quit(1);}
    }
}

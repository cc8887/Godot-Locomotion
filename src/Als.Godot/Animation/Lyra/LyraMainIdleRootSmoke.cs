using System.Globalization;
using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainIdleRootSmoke:Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static double Double(JsonElement row,string name)=>BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(row.GetProperty(name+"Bits").GetString()!,NumberStyles.HexNumber)));
    private static void Exact(double actual,double expected,string label)=>Require(BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(expected),$"{label}: {actual:R} != {expected:R}");
    public override void _Ready()
    {
        try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Main Idle root failed: "+e);GetTree().Quit(1);}
    }
    private static void Run()
    {
        using var capture=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://artifacts/lyra-analysis/main-idle-root-native.json"));
        var data=capture.RootElement;var requests=data.GetProperty("requests").GetProperty("frames");var native=data.GetProperty("native").GetProperty("frames");
        var applied=0;var hidden=0;var fading=0;var zero=0;var rejected=0;var previous=0d;
        var host=new LyraMainUpdateHost();
        for(var n=0;n<requests.GetArrayLength();n++)
        {
            var f=requests[n];var row=native[n];var before=row.GetProperty("before");var after=row.GetProperty("after");var fields=f.GetProperty("main");
            if(fields.TryGetProperty("TurnYawCurveValue",out var seeded))previous=seeded.GetDouble();
            Exact(previous,Double(before,"TurnYawCurveValue"),$"idle/{n}/previous");
            var result=LyraMainIdleRoot.Update(f.GetProperty("visited").GetBoolean(),f.GetProperty("current").GetInt32(),f.GetProperty("previousIdleWeight").GetSingle(),
                previous,before.GetProperty("mode").GetInt32(),Double(before,"RootYawOffset"),f.GetProperty("remaining").GetSingle(),f.GetProperty("curveWeight").GetSingle());
            Exact(result.TurnYaw,Double(after,"TurnYawCurveValue"),$"idle/{n}/curve");Require(result.Mode==after.GetProperty("mode").GetInt32(),$"idle/{n}/mode");
            var input=new LyraMainUpdateInput(new(default,new(0,0,0,false,false,false),default,default,true,fields.GetProperty("IsCrouching").GetBoolean(),1,false,false,0),
                0,-980,false,false,fields.GetProperty("bEnableRootYawOffset").GetBoolean(),0);
            var old=host.State;var oldTail=host.Tail;var c=host.Prepare(input,f.GetProperty("delta").GetSingle());
            if(result.ApplyYaw)
            {
                var original=c;c=host.ApplyGraphRootYaw(c,result.RootYaw);
                Exact(c.State.RootYaw,Double(after,"RootYawOffset"),$"idle/{n}/root");Exact(c.Tail.AimYaw,Double(after,"AimYaw"),$"idle/{n}/aim");applied++;
                var stale=false;try{host.Commit(original);}catch(InvalidOperationException){stale=true;}Require(stale,$"idle/{n}/old macro candidate");rejected++;
            }
            else
            {Exact(Double(before,"RootYawOffset"),Double(after,"RootYawOffset"),$"idle/{n}/unmodified root");Exact(Double(before,"AimYaw"),Double(after,"AimYaw"),$"idle/{n}/unmodified aim");}
            host.Cancel();Require(host.State==old && host.Tail==oldTail,$"idle/{n}/cancel publication");
            var retry=host.Prepare(input,f.GetProperty("delta").GetSingle());if(result.ApplyYaw)retry=host.ApplyGraphRootYaw(retry,result.RootYaw);
            Require(retry.State==c.State && retry.Tail==c.Tail,$"idle/{n}/retry");host.Commit(retry,result.Mode);
            Require(host.Tail.Mode==result.Mode,$"idle/{n}/graph mode commit");previous=result.TurnYaw;
            hidden+=f.GetProperty("visited").GetBoolean()?0:1;fading+=result.BlendingOut?1:0;zero+=f.GetProperty("curveWeight").GetSingle()==0?1:0;
        }
        Require(requests.GetArrayLength()==2520 && applied>200 && hidden>50 && fading>500 && zero>200 && rejected==applied,"Incomplete Main Idle native coverage.");
        GD.Print($"LYRA_MAIN_IDLE_ROOT_GODOT_OK frames=2520 applied={applied} hidden={hidden} fading={fading} zero={zero} rejected={rejected} exactBits=true retry=true wholeMain=false production=false");
    }
}

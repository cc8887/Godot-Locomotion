using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredPostLayerProfile
{
    public AlsLayerBlendingDefinition Layering { get; }
    public AlsRefactoredHeadProfile Head { get; }
    public IReadOnlyDictionary<int,AlsRefactoredBasePoseResource> BasePoses { get; }
    internal AlsRefactoredPostLayerProfile(AlsLayerBlendingDefinition layering,AlsRefactoredHeadProfile head,
        IReadOnlyDictionary<int,AlsRefactoredBasePoseResource> bases)
    {Layering=layering;Head=head;BasePoses=bases;}
    public AlsRefactoredPostLayerRuntime CreateRuntime(uint character,uint generation,ReadOnlySpan<string> curveNames)
    {
        var source=BasePoses[37].Pose;
        var reference=source.ReferencePose.ToArray().Select(p=>new AlsLocalPose(
            new((float)p.Position.X,(float)p.Position.Y,(float)p.Position.Z),
            new((float)p.Rotation.X,(float)p.Rotation.Y,(float)p.Rotation.Z,(float)p.Rotation.W),
            new((float)p.Scale.X,(float)p.Scale.Y,(float)p.Scale.Z))).ToArray();
        return new(character,generation,Layering,source.BoneNames,source.Parents,curveNames,reference,Head.Settings,Head.Look.CreateSampler());
    }
}

public static class AlsRefactoredPostLayerCompiler
{
    public static AlsRefactoredPostLayerProfile Compile(string graphs,string inventory,string baseInputs,string headInputs)
    {
        var layering=AlsRefactoredLayerGraphCompiler.Compile(graphs,inventory,baseInputs);
        var bases=AlsRefactoredBasePoseCompiler.Compile(baseInputs,inventory,graphs);
        var head=AlsRefactoredHeadGraphCompiler.Compile(graphs,inventory,headInputs);
        foreach(var source in bases.Values)
            if(!source.Pose.BoneNames.SequenceEqual(head.Look.BoneNames)||!source.Pose.Parents.SequenceEqual(head.Look.Parents))
                throw new ArgumentException("Refactored Layering and Head require the same complete logical skeleton.");
        return new(layering,head,bases);
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public enum AlsRefactoredAirValue { One, Speed, VerticalVelocity, Prediction, Aiming }
public enum AlsRefactoredLocomotionPoseKind { Link, GroundedInput, CacheRead, MainMachine, JumpMachine, Callback,
    Inertia, Player, Frame, Lean, TwoWay, LocalAdditive, MeshAdditive, ModifyCurve }
public readonly record struct AlsRefactoredLocomotionPoseNode(int Id, AlsRefactoredLocomotionPoseKind Kind, int A=-1, int B=-1,
    AlsRefactoredAirValue Value=AlsRefactoredAirValue.One, AlsOverlayAlphaPolicy Policy=default, string Function="");
public readonly record struct AlsRefactoredAirPlayer(int PropertyIndex,string Source,string Group,float Rate,float Start,bool Loop,bool JumpRate);

/// <summary>Complete original Locomotion pose topology, including nested Jump.
/// Grounded is an explicitly bound external pose cache, never a reference-pose substitute.</summary>
public sealed class AlsRefactoredLocomotionPoseProfile
{
    public const string LeanSource="/ALS/ALS/Animations/Air/Lean/BS_Als_Air_Lean.BS_Als_Air_Lean";
    public AlsRefactoredLocomotionResources Main {get;}
    public AlsRefactoredLocomotionResources Jump {get;}
    public string CatalogDigest=>Main.CatalogDigest;
    public AlsRefactoredBlendPoseSource Lean {get;}
    public ReadOnlySpan<string> BoneNames=>Lean.BoneNames;
    public ReadOnlySpan<int> Parents=>Lean.Parents;
    public ReadOnlySpan<string> CurveNames=>_curveNames;
    public ReadOnlySpan<string> GroundedCurveNames=>_groundedCurves;
    public ReadOnlySpan<AlsRefactoredAirPlayer> Players=>_players;
    internal readonly AlsRefactoredLocomotionPoseNode?[] Nodes=new AlsRefactoredLocomotionPoseNode?[84];
    internal readonly Dictionary<int,string[]> ModifiedCurves=[];
    internal readonly AlsPrecisePose[][] PredictionPoses=new AlsPrecisePose[2][];
    internal readonly AlsInertialCurve[][] PredictionCurves=new AlsInertialCurve[2][];
    internal readonly string[][] PredictionNames=new string[2][];
    internal readonly AlsPoseCacheDefinition Caches=new(84,[2],[new(81,2),new(22,2),new(25,2)]);
    internal readonly AlsRefactoredAnimationCatalog Catalog;
    private readonly string[] _curveNames,_groundedCurves;
    private readonly AlsRefactoredAirPlayer[] _players;
    public AlsRefactoredLocomotionPoseNode Node(int id)=>Nodes[id]??throw new ArgumentException("Not a Locomotion pose node.");

    public AlsRefactoredLocomotionPoseProfile(AlsRefactoredAnimationCatalog catalog,
        IReadOnlyDictionary<string,AlsRefactoredTriangulationProfile> triangles,string machines,ReadOnlySpan<string> groundedCurves)
    {
        Catalog=catalog;Main=new(machines,catalog);Jump=new(machines,catalog,true);
        var source=AlsRefactoredLocomotionResources.Source;var payload=catalog.Read(source);var native=Text(payload,"nativeText");
        var nodes=payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("propertyIndex").GetInt32());
        var cacheOrder=payload.GetProperty("compiled").GetProperty("orderedSavedPoseNodes").EnumerateArray().ToArray();
        Require(cacheOrder.Length==1,"Foreign Locomotion cache roots.");
        Expect(cacheOrder[0],new{root="AnimGraph",compiledNodeIndices=new[]{nodes[2].GetProperty("compiledNodeIndex").GetInt32()}});
        var graphs=nodes.Values.Select(n=>Text(n,"graph")).Distinct().ToDictionary(p=>p,p=>
            new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native,source,p),true));
        AlsYawOffsetCompiler.Node Auth(int id)=>graphs[Text(nodes[id],"graph")].Named(Text(nodes[id],"path").Split('.')[^1]);
        JsonElement Runtime(int id)=>nodes[id].GetProperty("runtime");
        JsonElement[] Policies(int id)=>[Runtime(id),nodes[id].GetProperty("authoredProperties").EnumerateObject().Single().Value];
        void Bind(int id,params (string Pin,string Path)[] expected)
        {
            var bindings=string.Join("\n",Regex.Matches(Auth(id).Body,"(?m)^         PropertyBindings=[^\\r\\n]*").Select(m=>m.Value));
            var actual=Regex.Matches(bindings,"PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                .Select(m=>(m.Groups[1].Value,string.Join(".",Regex.Matches(m.Groups[2].Value,"\"([^\"]+)\"").Select(v=>v.Groups[1].Value))));
            Require(actual.OrderBy(x=>x.Item1).SequenceEqual(expected.OrderBy(x=>x.Pin)),"Locomotion property binding differs: "+id);
        }
        void Add(int id,string kind,AlsRefactoredLocomotionPoseKind type,int a=-1,int b=-1,
            AlsRefactoredAirValue value=AlsRefactoredAirValue.One,AlsOverlayAlphaPolicy policy=default,string function="")
        {
            Expect(nodes[id],new {@class="AnimGraphNode_"+kind});Require(Auth(id).Kind=="AnimGraphNode_"+kind,"Authored pose class differs.");
            foreach(var p in Policies(id))foreach(var cb in new[]{"initialUpdateFunction","becomeRelevantFunction","updateFunction"})
                Expect(p.GetProperty(cb),new {className="None",functionName="None"});
            Nodes[id]=new(id,type,a,b,value,policy,function);
        }
        void Link(int id,string field,string pin,int child)
        {
            Expect(Runtime(id).GetProperty(field),new{linkId=child,sourceLinkId=id});
            if(field=="linkToCachingNode")return;
            var (other,output)=graphs[Text(nodes[id],"graph")].Follow(Auth(id),pin);
            Require(output.Output&&Text(nodes[child],"path")==Text(nodes[id],"graph")+"."+other.Name,"Locomotion authored pose link differs: "+id);
        }
        foreach(var (id,kind,field,pin,child) in new (int,string,string,string,int)[]{(0,"Root","result","Result",3),(2,"SaveCachedPose","pose","Pose",1),
            (82,"StateResult","result","Result",81),(80,"StateResult","result","Result",67),(65,"StateResult","result","Result",37),
            (35,"StateResult","result","Result",30),(29,"StateResult","result","Result",23),(63,"StateResult","result","Result",60),
            (59,"StateResult","result","Result",56),(55,"StateResult","result","Result",54),(53,"StateResult","result","Result",52)})
        {Add(id,kind,AlsRefactoredLocomotionPoseKind.Link,child);Link(id,field,pin,child);Bind(id);}
        Add(1,"LinkedInputPose",AlsRefactoredLocomotionPoseKind.GroundedInput);Bind(1);
        Expect(Runtime(1),new{name="Grounded Input",graph="AnimGraph",inputPose=new{linkId=-1,sourceLinkId=-1},bIsOutputLinked=true});
        foreach(var id in new[]{81,22,25}){Add(id,"UseCachedPose",AlsRefactoredLocomotionPoseKind.CacheRead,2);Link(id,"linkToCachingNode","",2);Bind(id);Expect(Runtime(id),new{cachePoseName="Grounded"});}
        Add(83,"StateMachine",AlsRefactoredLocomotionPoseKind.MainMachine);Bind(83);
        Add(64,"StateMachine",AlsRefactoredLocomotionPoseKind.JumpMachine);Bind(64);
        foreach(var (id,child,name,site) in new[]{(3,4,"InitializeLean","OnBecomeRelevant"),(67,74,"RefreshInAir","OnUpdate"),
            (37,40,"RefreshInAir","OnUpdate"),(30,31,"RefreshRotateInPlace","OnUpdate")})
        {
            Add(id,"CallFunction",AlsRefactoredLocomotionPoseKind.Callback,child,function:name);Link(id,"source","Source",child);Bind(id);
            foreach(var p in Policies(id))Expect(p,new{callSite=site});
            var inner=Regex.Match(Auth(id).Body,"(?m)^      InnerGraph=\"/Script/Engine.EdGraph'([^']+)'\"").Groups[1].Value;
            var g=new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(native,source,Text(nodes[id],"path")+"."+inner),true);
            var call=g.Nodes.Single();g.Self(call);Require(call.Kind=="K2Node_CallFunction"&&call.Member==name&&call.Pins.Values.All(p=>p.Links==""),"Locomotion callback differs.");
        }
        foreach(var (id,child) in new[]{(4,83),(39,64)})
        {
            Add(id,"Inertialization",AlsRefactoredLocomotionPoseKind.Inertia,child);Link(id,"source","Source",child);Bind(id);
            foreach(var p in Policies(id))Expect(p,new{defaultBlendProfile="",bResetOnBecomingRelevant=true,bForwardRequestsThroughSkippedCachedPoseNodes=true,
                tag="None",filteredCurves=id==4?new[]{"RotationYawSpeed"}:Array.Empty<string>(),filteredBones=Array.Empty<string>()});
        }
        var playerList=new List<AlsRefactoredAirPlayer>();
        foreach(var (id,path,group,rate,start,loop,dynamicRate) in new (int,string,string,float,float,bool,bool)[]{
            (78,"Fall/A_Als_Flail","None",1,0,true,false),(77,"Fall/A_Als_Fall","Fall",1,0,true,false),(76,"Fall/A_Als_Fall_Fast","Fall",1,0,true,false),
            (62,"Jump/A_Als_Jump_Walk_Left","Jump",1,.12f,false,true),(61,"Jump/A_Als_Jump_Run_Left","Jump",1,0,false,true),
            (58,"Jump/A_Als_Jump_Walk_Right","Jump",1,.12f,false,true),(57,"Jump/A_Als_Jump_Run_Right","Jump",1,0,false,true),
            (54,"Jump/A_Als_Jump_Loop","Flail",1,0,true,true),(52,"Fall/A_Als_Flail","Flail",1.2f,0,true,false),
            (34,"Land/A_Als_Land_Heavy","Land",1,0,false,false),(33,"Land/A_Als_Land_Light","Land",1,0,false,false),
            (28,"Land/A_Als_Land_Light_Additive","Land",1.75f,0,false,false),(27,"Land/A_Als_Land_Heavy_Additive","Land",1.5f,0,false,false)})
        {
            var clip="/ALS/ALS/Animations/Air/"+path+"."+path.Split('/')[^1];
            Add(id,"SequencePlayer",AlsRefactoredLocomotionPoseKind.Player,playerList.Count);
            Bind(id,dynamicRate?[("PlayRate","GetParent.InAirState.JumpPlayRate")]:[]);
            foreach(var p in Policies(id)){Expect(p,new{sequence=clip,groupName=group,groupRole="CanBeLeader",method=group=="None"?"DoNotSync":"SyncGroup",
                bIgnoreForRelevancyTest=false,bLoopAnimation=loop,bStartFromMatchingPose=false,bOverridePositionWhenJoiningSyncGroupAsLeader=false,playRateBasis=1});Clamp(p.GetProperty("playRateScaleBiasClampConstants"));}
            Expect(Runtime(id),new{playRate=(double)rate,startPosition=(double)start});
            Require(Auth(id).Pins.Values.All(p=>p.Output||p.Links==""),"Connected player input differs.");
            playerList.Add(new(id,clip,group,rate,start,loop,dynamicRate));
        }
        _players=playerList.ToArray();
        Lean=new(triangles[LeanSource],catalog);Require(Lean.IsAdditive,"Air Lean must be local additive.");
        foreach(var id in new[]{38,68})
        {
            Add(id,"BlendSpaceEvaluator",AlsRefactoredLocomotionPoseKind.Lean,id==38?0:1);
            Bind(id,("X","GetParent.LeanState.RightAmount"),("Y","GetParent.LeanState.ForwardAmount"));
            foreach(var p in Policies(id))Expect(p,new{blendSpace=LeanSource,normalizedTime=0,bTeleportToNormalizedTime=true,method="DoNotSync",groupName="None",
                groupRole="CanBeLeader",bIgnoreForRelevancyTest=false,bOverridePositionWhenJoiningSyncGroupAsLeader=false,playRate=1,bLoop=true,bResetPlayTimeWhenBlendSpaceChanges=true,startPosition=0});
        }
        foreach(var (id,index) in new[]{(43,0),(42,1),(72,0),(71,1)})
        {
            var clip="/ALS/ALS/Animations/Air/Land/A_Als_Land_"+(index==0?"Heavy":"Light");clip+="."+clip.Split('/')[^1];
            Add(id,"SequenceEvaluator",AlsRefactoredLocomotionPoseKind.Frame,index);Bind(id);
            foreach(var p in Policies(id))Expect(p,new{sequence=clip,method="DoNotSync",groupName="None",bUseExplicitFrame=true,explicitFrame=0,
                bShouldLoop=true,bTeleportToExplicitTime=true,reinitializationBehavior="ExplicitTime",startPosition=0,bIgnoreForRelevancyTest=false});
            if(PredictionPoses[index] is not null)continue;
            var clipData=catalog.CompileAbsolutePoseWithCurves(clip);Require(clipData.Pose.BoneNames.SequenceEqual(BoneNames),"Prediction skeleton differs.");
            PredictionPoses[index]=new AlsPrecisePose[BoneNames.Length];PredictionNames[index]=clipData.Curves.Names.ToArray();PredictionCurves[index]=new AlsInertialCurve[PredictionNames[index].Length];
            clipData.Pose.CreateSampler(clipData.Curves).Sample(0,true,false,false,PredictionPoses[index],PredictionCurves[index]);
        }
        foreach(var (id,child,curves,prediction) in new (int,int,string[],bool)[]{(31,32,["FootLeftIk","FootRightIk","FootLeftLock","FootRightLock","PoseStanding","PoseGrounded"],false),
            (40,36,["PoseStanding","PoseInAir"],false),(74,66,["PoseStanding","PoseInAir"],false),
            (36,44,["FootLeftIk","FootRightIk"],true),(66,73,["FootLeftIk","FootRightIk"],true)})
        {
            Add(id,"ModifyCurve",AlsRefactoredLocomotionPoseKind.ModifyCurve,child,value:prediction?AlsRefactoredAirValue.Prediction:AlsRefactoredAirValue.One);
            Link(id,"sourcePose","SourcePose",child);Bind(id,prediction?[("Alpha","GetParent.InAirState.GroundPredictionAmount")]:[]);
            foreach(var p in Policies(id))Expect(p,new{curveNames=curves,curveMap=new{},alpha=1,applyMode="Blend"});
            Expect(Runtime(id),new{curveValues=Enumerable.Repeat(1,curves.Length).ToArray()});ModifiedCurves[id]=curves;
            for(var c=0;c<curves.Length;c++)Require(float.Parse(graphs[Text(nodes[id],"graph")].Literal(Auth(id),"CurveValues_"+c),CultureInfo.InvariantCulture)==1,"Modified air curve pin differs.");
        }
        foreach(var (id,a,b,value,policy) in new (int,int,int,AlsRefactoredAirValue,AlsOverlayAlphaPolicy)[]{
            (79,75,78,AlsRefactoredAirValue.VerticalVelocity,Map(-500,-3000,interpolate:true,up:5,down:5)),
            (75,76,77,AlsRefactoredAirValue.VerticalVelocity,Map(0,-1000,interpolate:true,up:5,down:5)),
            (73,69,70,AlsRefactoredAirValue.Prediction,new(1,0,false,0,1,true,20,5)),
            (70,72,71,AlsRefactoredAirValue.VerticalVelocity,Map(-1000,-500)),
            (44,45,41,AlsRefactoredAirValue.Prediction,new(1,0,false,0,1,true,20,5)),
            (41,43,42,AlsRefactoredAirValue.VerticalVelocity,Map(-1000,-500)),
            (60,62,61,AlsRefactoredAirValue.Speed,Map(200,500,interpolate:true,up:5,down:5)),
            (56,58,57,AlsRefactoredAirValue.Speed,Map(200,500,interpolate:true,up:5,down:5)),
            (32,33,34,AlsRefactoredAirValue.VerticalVelocity,Map(-500,-1000)),
            (26,28,27,AlsRefactoredAirValue.VerticalVelocity,Map(-750,-1500,maximum:.75f)),
            (23,22,24,AlsRefactoredAirValue.VerticalVelocity,Map(0,-200,minimum:.25f,clamp:true))})
        {
            Add(id,"TwoWayBlend",AlsRefactoredLocomotionPoseKind.TwoWay,a,b,value,policy);Link(id,"a","A",a);Link(id,"b","B",b);
            Bind(id,("Alpha",value==AlsRefactoredAirValue.Speed?"GetParent.LocomotionState.Speed":value==AlsRefactoredAirValue.Prediction?"GetParent.InAirState.GroundPredictionAmount":"GetParent.InAirState.VerticalVelocityWorldSpace"));
            foreach(var p in Policies(id)){Expect(p,new{alphaInputType="Float",bAlphaBoolEnabled=true,bResetChildOnActivation=false,bAlwaysUpdateChildren=false,alphaCurveName="None"});Alpha(p,policy);}
        }
        foreach(var (id,a,b) in new[]{(69,79,68),(45,39,38),(24,25,26)})
        {
            var mesh=id==24;var policy=mesh?Map(0,1,minimum:1,maximum:.5f,interpolate:true,up:2.5f,down:20):new AlsOverlayAlphaPolicy(1,0,false,0,1,false,10,10);
            Add(id,mesh?"ApplyMeshSpaceAdditive":"ApplyAdditive",mesh?AlsRefactoredLocomotionPoseKind.MeshAdditive:AlsRefactoredLocomotionPoseKind.LocalAdditive,
                a,b,mesh?AlsRefactoredAirValue.Aiming:AlsRefactoredAirValue.One,policy);Link(id,"base","Base",a);Link(id,"additive","Additive",b);Bind(id);
            foreach(var p in Policies(id)){Expect(p,new{alphaInputType="Float",alphaCurveName="None",lODThreshold=-1});Alpha(p,policy);if(mesh)Expect(p,new{bRootSpaceAdditive=false});}
        }
        var aimGraph=graphs[Text(nodes[24],"graph")];var (convert,convertPin)=aimGraph.Follow(Auth(24),"Alpha");
        aimGraph.Function(convert,"Conv_BoolToDouble","Engine.KismetMathLibrary");Require(convertPin.Name=="ReturnValue","Foreign landing additive alpha output.");
        var (equal,equalPin)=aimGraph.Follow(convert,"InBool");Require(equal.Kind=="K2Node_PromotableOperator"&&equal.Member=="EqualEqual_GameplayTag"&&
            equal.Body.Contains("/Script/GameplayTags.BlueprintGameplayTagLibrary",StringComparison.Ordinal),"Foreign landing tag comparison.");
        var (property,propertyPin)=aimGraph.Follow(equal,"A");Require(equalPin.Name=="ReturnValue"&&propertyPin.Name=="Value"&&property.Kind=="K2Node_PropertyAccess"&&
            property.Body.Contains("Path(1)=\"RotationMode\"",StringComparison.Ordinal)&&property.Body.Contains("Path(0)=\"GetParent\"",StringComparison.Ordinal)&&
            equal.Body.Contains("DefaultValue=\"(TagName=\\\"Als.RotationMode.Aiming\\\")\"",StringComparison.Ordinal),"Landing aiming expression differs.");
        var expectedIds=nodes.Values.Where(n=>Text(n,"class")!="AnimGraphNode_TransitionResult").Select(n=>n.GetProperty("propertyIndex").GetInt32()).Order();
        Require(Nodes.Where(n=>n.HasValue).Select(n=>n!.Value.Id).Order().SequenceEqual(expectedIds),"Incomplete Locomotion pose closure.");
        _groundedCurves=groundedCurves.ToArray();Require(_groundedCurves.Distinct(StringComparer.OrdinalIgnoreCase).Count()==_groundedCurves.Length,"Duplicate Grounded curve.");
        var sourceCurves=_players.SelectMany(p=>catalog.Read(p.Source).GetProperty("evaluation").GetProperty("additiveType").GetString()=="AAT_None"
            ?catalog.CompileAbsolutePoseWithCurves(p.Source).Curves.Names.ToArray():catalog.CompileAdditivePose(p.Source).CurveNames.ToArray());
        _curveNames=_groundedCurves.Concat(sourceCurves).Concat(Lean.CurveNames.ToArray()).Concat(PredictionNames.SelectMany(n=>n))
            .Concat(ModifiedCurves.Values.SelectMany(n=>n)).Append("RotationYawSpeed").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static AlsOverlayAlphaPolicy Map(float low,float high,float minimum=0,float maximum=1,bool interpolate=false,float up=10,float down=10,bool clamp=false)=>
        new(1,0,clamp,clamp?minimum:0,1,interpolate,up,down,true,low,high,minimum,maximum);
    private static void Alpha(JsonElement p,AlsOverlayAlphaPolicy a)
    {
        Expect(p.GetProperty("alphaScaleBias"),new{scale=1,bias=0});
        Expect(p.GetProperty("alphaScaleBiasClamp"),new{bMapRange=a.MapRange,bClampResult=a.Clamp,bInterpResult=a.Interpolate,
            inRange=new{min=(double)a.InputMin,max=(double)a.InputMax},outRange=new{min=(double)a.OutputMin,max=(double)a.OutputMax},
            scale=(double)a.Scale,bias=(double)a.Bias,clampMin=(double)a.Minimum,clampMax=(double)a.Maximum,
            interpSpeedIncreasing=(double)a.Increasing,interpSpeedDecreasing=(double)a.Decreasing});
    }
    private static string Text(JsonElement n,string key)=>n.GetProperty(key).GetString()!;
    private static void Require(bool ok,string message){if(!ok)throw new ArgumentException(message);}
}

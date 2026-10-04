#include "AlsLyraAirLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimSync.h"
#include "Animation/AnimSyncScope.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "Animation/BlendProfile.h"
#include "Animation/Skeleton.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "AnimNodes/AnimNode_LayeredBoneBlend.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "UObject/StrongObjectPtr.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace LyraCycleProbe
{
bool Set(UObject*,const TCHAR*,const TSharedPtr<FJsonValue>&);
bool Bind(UAnimInstance*,const TSharedPtr<FJsonObject>&,const TMap<FString,UAnimSequence*>*);
int32 LayerRoot(const IAnimClassInterface*,FName);
}
namespace LyraAirProbe
{
FString Fail(int32 Line) {UE_LOG(LogTemp,Error,TEXT("LYRA_AIR_CAPTURE_FAILED line=%d"),Line);return {};}
struct FInstanceAccess : UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
// Oracle instrumentation: typed member access, actual Main Sync, no layout offsets.
struct FSyncMember {using Type=UE::Anim::FAnimSync FAnimInstanceProxy::*;friend Type NativeSync(FSyncMember);};
template<class Tag,typename Tag::Type Member> struct TOracleMember
{friend typename Tag::Type NativeSync(Tag){return Member;}};
template struct TOracleMember<FSyncMember,&FAnimInstanceProxy::Sync>;
struct FProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D)
    {(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
    static void Tick(FAnimInstanceProxy& P,float D){(P.*NativeSync(FSyncMember{})).TickAssetPlayerInstances(P,D);}
};
struct FSourceAccess : FAnimNode_AssetPlayerBase
{
    static float Time(const FAnimNode_AssetPlayerBase& N){return N.*&FSourceAccess::InternalTimeAccumulator;}
    static const FDeltaTimeRecord& Delta(const FAnimNode_AssetPlayerBase& N){return N.*&FSourceAccess::DeltaTimeRecord;}
    static FMarkerTickRecord& Marker(FAnimNode_AssetPlayerBase& N){return N.*&FSourceAccess::MarkerTickRecord;}
};
struct FRoot
{FPoseLink Link;FAnimNode_AssetPlayerBase* Base=nullptr;FAnimNode_SequenceEvaluator* Hip=nullptr;FAnimNode_LayeredBoneBlend* Blend=nullptr;bool Initialized=false;};
TSharedPtr<FJsonObject> Source(FAnimNode_AssetPlayerBase* N,const TMap<const UAnimSequence*,FString>& Paths)
{
    auto R=MakeShared<FJsonObject>();const auto& D=FSourceAccess::Delta(*N);const auto& M=FSourceAccess::Marker(*N);
    R->SetStringField(TEXT("asset"),Paths.FindRef(Cast<UAnimSequence>(N->GetAnimAsset())));
    R->SetNumberField(TEXT("time"),FSourceAccess::Time(*N));R->SetNumberField(TEXT("publicTime"),N->GetAccumulatedTime());
    R->SetNumberField(TEXT("weight"),N->GetCachedBlendWeight());R->SetNumberField(TEXT("previous"),D.GetPrevious());R->SetNumberField(TEXT("delta"),D.Delta);
    R->SetNumberField(TEXT("markerPrevious"),M.PreviousMarker.MarkerIndex);R->SetNumberField(TEXT("markerNext"),M.NextMarker.MarkerIndex);
    R->SetNumberField(TEXT("markerPreviousDistance"),M.PreviousMarker.MarkerIndex==-2?0:M.PreviousMarker.TimeToMarker);
    R->SetNumberField(TEXT("markerNextDistance"),M.NextMarker.MarkerIndex==-2?0:M.NextMarker.TimeToMarker);return R;
}
}
FString UAlsLyraAirLibrary::ReadAirTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraAirProbe;using namespace LyraCycleProbe;
    TSharedPtr<FJsonObject> Input;
    if(!MainClass || !Mesh || !Skeleton || Skeleton->GetReferenceSkeleton().GetNum()!=81 ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
    const auto& Names=Input->GetArrayField(TEXT("sequencePaths"));if(Names.Num()!=Sequences.Num())return Fail(__LINE__);
    TMap<FString,UAnimSequence*> Assets;TMap<const UAnimSequence*,FString> Paths;
    for(int32 I=0;I<Sequences.Num();++I)
    {auto* S=Sequences[I];if(!S || S->GetSkeleton()!=Skeleton || S->IsValidAdditive())return Fail(__LINE__);
     S->WaitOnExistingCompression(true);Assets.Add(Names[I]->AsString(),S);Paths.Add(S,Names[I]->AsString());}
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& T : Input->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());const auto Trace=T->AsObject();auto* LC=LoadObject<UClass>(nullptr,*Trace->GetStringField(TEXT("class")));
        const auto* LI=LC?IAnimClassInterface::GetFromClass(LC):nullptr;if(!LI)return Fail(__LINE__);
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
        auto* Owner=W->SpawnActor<AActor>(Spawn);if(!Owner)return Fail(__LINE__);
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MainClass);Owner->SetRootComponent(C.Get());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();
        auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);Main->LinkAnimClassLayers(LC);
        auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Layer || Layer->GetClass()!=LC || !Bind(Layer,Trace->GetObjectField(TEXT("bindings")),&Assets))return Fail(__LINE__);
        auto& MP=FInstanceAccess::Proxy(Main);auto& LP=FInstanceAccess::Proxy(Layer);TArray<FRoot> Roots;Roots.SetNum(5);
        TStrongObjectPtr<UBlendProfile> Mask(NewObject<UBlendProfile>(GetTransientPackage()));Mask->OwningSkeleton=Skeleton;Mask->Mode=EBlendProfileMode::BlendMask;
        const auto& LayerNames=Input->GetArrayField(TEXT("layers"));if(LayerNames.Num()!=5)return Fail(__LINE__);
        for(int32 I=0;I<5;++I)
        {
            const auto D=LayerNames[I]->AsObject();const int32 R=LayerRoot(LI,FName(*D->GetStringField(TEXT("name"))));if(R<0)return Fail(__LINE__);
            auto& Root=Roots[I];auto* Node=LP.GetMutableNodeFromIndex<FAnimNode_Base>(R);if(!Node)return Fail(__LINE__);Root.Link.SetLinkNode(Node);
            const int32 B=D->GetNumberField(TEXT("base")),H=D->GetNumberField(TEXT("hip")),L=D->GetNumberField(TEXT("blend"));
            const auto& P=LI->GetAnimNodeProperties();const int32 BP=P.Num()-1-B;
            if(!P.IsValidIndex(BP) || !P[BP]->Struct->IsChildOf(FAnimNode_AssetPlayerBase::StaticStruct()))return Fail(__LINE__);
            Root.Base=P[BP]->ContainerPtrToValuePtr<FAnimNode_AssetPlayerBase>(Layer);Root.Hip=LP.GetMutableNodeFromIndex<FAnimNode_SequenceEvaluator>(H);Root.Blend=LP.GetMutableNodeFromIndex<FAnimNode_LayeredBoneBlend>(L);
            if(!Root.Hip || !Root.Blend || Root.Blend->BlendMasks.Num()!=1 || !Root.Blend->BlendMasks[0])return Fail(__LINE__);
            if(I==0)for(int32 Bone=0;Bone<81;++Bone)
            {float Weight=0;for(const auto& E:Root.Blend->BlendMasks[0]->ProfileEntries)if(E.BoneReference.BoneName==Skeleton->GetReferenceSkeleton().GetBoneName(Bone)){Weight=E.BlendScale;break;}
             Mask->SetBoneBlendScale(Bone,Weight,false,true);}
            Root.Blend->BlendMasks[0]=Mask.Get();Root.Blend->InvalidatePerBoneBlendWeights();
            for(auto* Source:{Root.Base,static_cast<FAnimNode_AssetPlayerBase*>(Root.Hip)})
            {auto& M=FSourceAccess::Marker(*Source);M.PreviousMarker.TimeToMarker=0;M.NextMarker.TimeToMarker=0;}
        }
        Carrier->SetSkeleton(Skeleton);TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add(static_cast<FBoneIndexType>(I));
        LP.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*Skeleton);LP.GetRequiredBones().SetUseRAWData(true);LP.GetRequiredBones().SetDisableRetargeting(false);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& V:Trace->GetArrayField(TEXT("frames")))
        {
            FMemMark FrameMark(FMemStack::Get());const auto F=V->AsObject();const float Delta=F->GetNumberField(TEXT("delta"));
            for(const auto& E:F->GetObjectField(TEXT("main"))->Values)if(!Set(Main,*E.Key,E.Value))return Fail(__LINE__);
            for(const auto& E:F->GetObjectField(TEXT("layer"))->Values)if(!Set(Layer,*E.Key,E.Value))return Fail(__LINE__);
            FProxyAccess::Pre(MP,Main,Delta);FProxyAccess::Pre(LP,Layer,Delta);
            LI->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& S)
            {if(S.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct()){FAnimSubsystemUpdateContext G(S,Layer,Delta);S.Subsystem.OnPreUpdate_GameThread(G);S.Subsystem.OnPostUpdate_GameThread(G);FAnimSubsystemParallelUpdateContext P(S,LP,Delta);S.Subsystem.OnPreUpdate_WorkerThread(P);}return EAnimSubsystemEnumeration::Continue;});
            FAnimationUpdateSharedContext Shared;FAnimationUpdateContext Context(&LP,Delta,&Shared);
            FAnimationUpdateContext RootContext(&MP,Delta,&Shared);
            UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> Scope(Context,RootContext);
            TArray<TSharedPtr<FJsonValue>> Rows;for(int32 I=0;I<5;++I)Rows.Add(MakeShared<FJsonValueObject>(MakeShared<FJsonObject>()));
            for(const auto& O:F->GetArrayField(TEXT("order")))
            {
                const int32 I=O->AsNumber();if(!Roots.IsValidIndex(I))return Fail(__LINE__);auto& Root=Roots[I];const auto Visit=F->GetArrayField(TEXT("visits"))[I]->AsObject();
                if(!Root.Initialized || Visit->GetBoolField(TEXT("initialize")))
                {Root.Link.Initialize(FAnimationInitializeContext(&LP));Root.Link.CacheBones(FAnimationCacheBonesContext(&LP));Root.Initialized=true;}
                if(Visit->GetBoolField(TEXT("visited")))
                {auto Child=Context.FractionalWeight(Visit->GetNumberField(TEXT("weight")));Root.Link.Update(Visit->GetBoolField(TEXT("active"))?Child:Child.AsInactive());}
            }
            FProxyAccess::Tick(MP,Delta);
            for(int32 I=0;I<5;++I)
            {
                auto& Root=Roots[I];const auto Row=Rows[I]->AsObject();Row->SetObjectField(TEXT("base"),Source(Root.Base,Paths));Row->SetObjectField(TEXT("hip"),Source(Root.Hip,Paths));
                Row->SetNumberField(TEXT("blend"),Root.Blend->BlendWeights[0]);
                if(F->GetArrayField(TEXT("visits"))[I]->AsObject()->GetBoolField(TEXT("visited")))
                {FPoseContext Pose(&LP);Root.Link.Evaluate(Pose);Row->SetObjectField(TEXT("output"),LyraCyclePoseProbe::PoseData(Pose.Pose,Pose.Curve,Pose.CustomAttributes,Skeleton->GetReferenceSkeleton()));}
            }
            const auto Row=MakeShared<FJsonObject>();Row->SetArrayField(TEXT("roots"),Rows);Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("profile"),Trace->GetStringField(TEXT("profile")));Row->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz")));Row->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Row));C->UnregisterComponent();
    }
    auto Out=MakeShared<FJsonObject>();Out->SetArrayField(TEXT("traces"),Traces);FString Json;FJsonSerializer::Serialize(Out,TJsonWriterFactory<>::Create(&Json));return Json;
}

#include "AlsLyraAimingLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_LinkedInputPose.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AnimSequence.h"
#include "Animation/BlendSpace.h"
#include "Animation/AnimSync.h"
#include "Animation/AnimSyncScope.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "AnimNodes/AnimNode_RotationOffsetBlendSpace.h"
#include "AnimNodes/AnimNode_TwoWayBlend.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"
namespace LyraCycleProbe { bool Set(UObject*,const TCHAR*,const TSharedPtr<FJsonValue>&); }
namespace LyraAimWeightProbe { TSharedPtr<FJsonObject> Weights(UAnimInstance*); }
namespace LyraAimingProbe
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_AIMING_FAILED line=%d"),Line);return {};}
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FSyncMember{using Type=UE::Anim::FAnimSync FAnimInstanceProxy::*;friend Type NativeAimingSync(FSyncMember);};
template<class Tag,typename Tag::Type Member>struct TOracleMember{friend typename Tag::Type NativeAimingSync(Tag){return Member;}};
template struct TOracleMember<FSyncMember,&FAnimInstanceProxy::Sync>;
struct FRootMember{using Type=FAnimNode_Base* FAnimInstanceProxy::*;friend Type NativeAimingRoot(FRootMember);};
template<class Tag,typename Tag::Type Member>struct TOracleRoot{friend typename Tag::Type NativeAimingRoot(Tag){return Member;}};
template struct TOracleRoot<FRootMember,&FAnimInstanceProxy::RootNode>;
struct FProxyAccess:FAnimInstanceProxy
{
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S)
    {
        (P.*&FProxyAccess::InitializeObjects)(A);TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add((FBoneIndexType)I);
        P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*S);P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);(P.*&FProxyAccess::CachedBonesCounter).Increment();
    }
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D)
    {P.FlipBufferWriteIndex();(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
    static void Init(FAnimInstanceProxy& P){(P.*&FProxyAccess::InitializationCounter).Increment();}
    static FAnimNode_Base* SwapRoot(FAnimInstanceProxy& P,FAnimNode_Base* Root)
    {auto* Old=P.*NativeAimingRoot(FRootMember{});P.*NativeAimingRoot(FRootMember{})=Root;return Old;}
    static void Tick(FAnimInstanceProxy& P,float D){(P.*NativeAimingSync(FSyncMember{})).TickAssetPlayerInstances(P,D);}
    static void Publish(FAnimInstanceProxy& P,const TSharedPtr<FJsonObject>& Values)
    {
        using FRead=TMap<FName,float>&(FAnimInstanceProxy::*)(EAnimCurveType);
        auto& C=(P.*static_cast<FRead>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve);C.Reset();
        for(const auto& V:Values->Values)C.Add(FName(*V.Key),static_cast<float>(V.Value->AsNumber()));
    }
};
struct FNodeAccess:FAnimNode_BlendSpacePlayerBase
{
    static TSharedPtr<FJsonObject> Read(FAnimNode_RotationOffsetBlendSpace& Node)
    {
        auto R=MakeShared<FJsonObject>();const auto& D=Node.*&FNodeAccess::DeltaTimeRecord;
        R->SetNumberField(TEXT("time"),Node.GetAccumulatedTime());R->SetNumberField(TEXT("weight"),Node.GetCachedBlendWeight());
        R->SetNumberField(TEXT("previous"),D.GetPrevious());R->SetNumberField(TEXT("delta"),D.Delta);
        R->SetNumberField(TEXT("cache"),Node.*&FNodeAccess::CachedTriangulationIndex);R->SetNumberField(TEXT("alpha"),Node.ActualAlpha);
        R->SetNumberField(TEXT("x"),Node.GetPosition().X);R->SetNumberField(TEXT("y"),Node.GetPosition().Y);
        TArray<TSharedPtr<FJsonValue>> Samples;for(const auto& S:Node.*&FNodeAccess::BlendSampleDataCache)
        {
            auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("index"),S.SampleDataIndex);V->SetNumberField(TEXT("weight"),S.TotalWeight);V->SetNumberField(TEXT("weightRate"),S.WeightRate);V->SetNumberField(TEXT("rate"),S.SamplePlayRate);
            PRAGMA_DISABLE_DEPRECATION_WARNINGS
            V->SetNumberField(TEXT("time"),S.Time);V->SetNumberField(TEXT("previous"),S.PreviousTime);
            PRAGMA_ENABLE_DEPRECATION_WARNINGS
            V->SetNumberField(TEXT("deltaPrevious"),S.DeltaTimeRecord.GetPrevious());V->SetNumberField(TEXT("delta"),S.DeltaTimeRecord.Delta);Samples.Add(MakeShared<FJsonValueObject>(V));
        }
        R->SetArrayField(TEXT("samples"),Samples);return R;
    }
};
struct FInputLeaf:FAnimNode_Base
{
    UAnimSequence* Sequence=nullptr;float Time=0,Previous=0,Delta=0;uint32 Flags=0;int32 Updates=0,Evaluations=0;float Weight=0;
    void Update_AnyThread(const FAnimationUpdateContext& C)override{++Updates;Weight=C.GetFinalBlendWeight();}
    void Evaluate_AnyThread(FPoseContext& C)override
    {
        ++Evaluations;FDeltaTimeRecord D;D.Set(Previous,Delta);FAnimationPoseData Data(C);FAnimExtractContext E(static_cast<double>(Time),false,D,true);E.bExtractWithRootMotionProvider=true;Sequence->GetAnimationPose(Data,E);
        C.Curve.Set(TEXT("Distance"),17.f+Time*.25f);C.Curve.SetFlags(TEXT("Distance"),static_cast<UE::Anim::ECurveElementFlags>(Flags));
    }
};
}
FString UAlsLyraAimingLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UAnimSequence*>& AimSequences,const TArray<UAnimSequence*>& BaseSequences,const FString& RequestsJson)
{
    using namespace LyraAimingProbe;TSharedPtr<FJsonObject> Input;
    if(!MainClass||!Mesh||!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||AimSequences.Num()!=45||BaseSequences.IsEmpty()||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
    TArray<TStrongObjectPtr<UBlendSpace>> Spaces;
    for(int32 I=0;I<3;++I)
    {
        auto* Original=LoadObject<UBlendSpace>(nullptr,*Input->GetArrayField(TEXT("spaces"))[I]->AsString());if(!Original||Original->GetBlendSamples().Num()!=15)return Fail(__LINE__);
        TStrongObjectPtr<UBlendSpace> Space(DuplicateObject<UBlendSpace>(Original,GetTransientPackage()));Space->ClearFlags(RF_Public|RF_Standalone);Space->SetFlags(RF_Transient);
        for(int32 S=0;S<15;++S){auto* Sequence=AimSequences[I*15+S];if(!Sequence||Sequence->GetSkeleton()!=Skeleton||!Sequence->IsValidAdditive()||!Space->ReplaceSampleAnimation(S,Sequence))return Fail(__LINE__);Sequence->WaitOnExistingCompression(true);}
        Space->SetSkeleton(Skeleton);Space->ValidateSampleData();Space->ResampleData();if(Space->GetBlendSamples().Num()!=15)return Fail(__LINE__);Spaces.Add(MoveTemp(Space));
    }
    for(auto* S:BaseSequences){if(!S||S->GetSkeleton()!=Skeleton||S->IsValidAdditive())return Fail(__LINE__);S->WaitOnExistingCompression(true);}
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Input->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());auto T=TV->AsObject();auto* LC=LoadObject<UClass>(nullptr,*T->GetStringField(TEXT("class")));auto* LI=LC?IAnimClassInterface::GetFromClass(LC):nullptr;if(!LI)return Fail(__LINE__);
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* Owner=W->SpawnActor<ACharacter>(Spawn);if(!Owner)return Fail(__LINE__);
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MainClass);C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();
        auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);Main->LinkAnimClassLayers(LC);auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Layer||Layer->GetClass()!=LC)return Fail(__LINE__);
        auto* IdleField=FindFProperty<FObjectPropertyBase>(LC,TEXT("IdleAimOffset"));auto* RelaxedField=FindFProperty<FObjectPropertyBase>(LC,TEXT("RelaxedAimOffset"));if(!IdleField||!RelaxedField)return Fail(__LINE__);
        IdleField->SetObjectPropertyValue_InContainer(Layer,Spaces[static_cast<int32>(T->GetNumberField(TEXT("space")))].Get());RelaxedField->SetObjectPropertyValue_InContainer(Layer,Spaces[0].Get());
        auto& MP=FInstanceAccess::Proxy(Main);auto& LP=FInstanceAccess::Proxy(Layer);Carrier->SetSkeleton(Skeleton);FProxyAccess::Setup(MP,Main,Skeleton);FProxyAccess::Setup(LP,Layer,Skeleton);
        const auto* Function=IAnimClassInterface::FindAnimBlueprintFunction(LI,TEXT("FullBody_Aiming"));auto* WeightFunction=Layer->FindFunction(TEXT("Update Blend Weight Data"));auto* DeltaProperty=WeightFunction?FindFProperty<FDoubleProperty>(WeightFunction,TEXT("DeltaTime")):nullptr;
        auto* In=LP.GetMutableNodeFromIndex<FAnimNode_LinkedInputPose>(80);auto* Cache=LP.GetMutableNodeFromIndex<FAnimNode_SaveCachedPose>(78);auto* Blend=LP.GetMutableNodeFromIndex<FAnimNode_TwoWayBlend>(77);
        auto* A=LP.GetMutableNodeFromIndex<FAnimNode_RotationOffsetBlendSpace>(79);auto* B=LP.GetMutableNodeFromIndex<FAnimNode_RotationOffsetBlendSpace>(74);if(!Function||!DeltaProperty||!In||!Cache||!Blend||!A||!B)return Fail(__LINE__);
        FInputLeaf Source;FPoseLink SourceLink;SourceLink.SetLinkNode(&Source);SourceLink.Initialize(FAnimationInitializeContext(&MP));SourceLink.CacheBones(FAnimationCacheBonesContext(&MP));In->DynamicUnlink();In->DynamicLink(&MP,&SourceLink,1);
        struct FUnlink{FAnimNode_LinkedInputPose* In;~FUnlink(){In->DynamicUnlink();}}Unlink{In};FPoseLink Root;Root.SetLinkNode(Function->OutputPoseNodeProperty->ContainerPtrToValuePtr<FAnimNode_Base>(Layer));
        bool Initialized=false;TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark FrameMark(FMemStack::Get());auto F=FV->AsObject();const float D=F->GetNumberField(TEXT("delta"));
            for(const auto& Field:F->GetObjectField(TEXT("main"))->Values)if(!LyraCycleProbe::Set(Main,*Field.Key,Field.Value))return Fail(__LINE__);
            for(const TCHAR* Name:{TEXT("AimYaw"),TEXT("AimPitch")})if(!LyraCycleProbe::Set(Layer,Name,F->GetField<EJson::Number>(Name)))return Fail(__LINE__);
            FProxyAccess::Pre(MP,Main,D);FProxyAccess::Pre(LP,Layer,D);
            LI->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& S){if(S.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct()){FAnimSubsystemUpdateContext G(S,Layer,D);S.Subsystem.OnPreUpdate_GameThread(G);S.Subsystem.OnPostUpdate_GameThread(G);FAnimSubsystemParallelUpdateContext P(S,LP,D);S.Subsystem.OnPreUpdate_WorkerThread(P);}return EAnimSubsystemEnumeration::Continue;});
            auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("weightsBefore"),LyraAimWeightProbe::Weights(Layer));R->SetNumberField(TEXT("feedbackBefore"),Layer->GetCurveValue(TEXT("applyHipfireOverridePose")));
            TArray<uint8> Params;Params.SetNumZeroed(WeightFunction->ParmsSize);DeltaProperty->SetPropertyValue_InContainer(Params.GetData(),static_cast<double>(D));Layer->ProcessEvent(WeightFunction,Params.GetData());R->SetObjectField(TEXT("weights"),LyraAimWeightProbe::Weights(Layer));
            if(!Initialized||F->GetBoolField(TEXT("initialize"))){FProxyAccess::Init(LP);Root.Initialize(FAnimationInitializeContext(&LP));Root.CacheBones(FAnimationCacheBonesContext(&LP));Initialized=true;}
            Source.Sequence=BaseSequences[static_cast<int32>(F->GetNumberField(TEXT("asset")))];Source.Time=F->GetNumberField(TEXT("time"));Source.Previous=F->GetNumberField(TEXT("previous"));Source.Delta=F->GetNumberField(TEXT("sourceDelta"));Source.Flags=static_cast<uint32>(F->GetNumberField(TEXT("flags")));Source.Updates=Source.Evaluations=0;Source.Weight=0;
            R->SetObjectField(TEXT("aBefore"),FNodeAccess::Read(*A));R->SetObjectField(TEXT("bBefore"),FNodeAccess::Read(*B));
            FAnimationUpdateSharedContext Shared;FAnimationUpdateContext Context(&LP,D,&Shared);Context=Context.FractionalWeight(static_cast<float>(F->GetNumberField(TEXT("weight"))));if(!F->GetBoolField(TEXT("active")))Context=Context.AsInactive();FAnimationUpdateContext MainContext(&MP,D,&Shared);
            UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> SyncScope(Context,MainContext);
            if(F->GetBoolField(TEXT("visited"))){Root.Update(Context);Cache->PostGraphUpdate();}FProxyAccess::Tick(MP,D);
            R->SetObjectField(TEXT("a"),FNodeAccess::Read(*A));R->SetObjectField(TEXT("b"),FNodeAccess::Read(*B));R->SetNumberField(TEXT("blendPin"),Blend->Alpha);R->SetNumberField(TEXT("inputUpdates"),Source.Updates);R->SetNumberField(TEXT("inputWeight"),Source.Weight);
            if(F->GetBoolField(TEXT("visited"))&&F->GetBoolField(TEXT("evaluate")))
            {
                FPoseContext Base(&MP);SourceLink.Evaluate(Base);Source.Evaluations=0;
                FPoseContext Output(&LP);Output.ResetToRefPose();FBlendedHeapCurve HeapCurves;UE::Anim::FHeapAttributeContainer HeapAttributes;
                FParallelEvaluationData EvaluationData{HeapCurves,Output.Pose,HeapAttributes};
                auto* OldRoot=FProxyAccess::SwapRoot(LP,Function->OutputPoseNodeProperty->ContainerPtrToValuePtr<FAnimNode_Base>(Layer));
                Layer->ParallelEvaluateAnimation(false,Carrier.Get(),EvaluationData);FProxyAccess::SwapRoot(LP,OldRoot);
                Output.Curve.CopyFrom(HeapCurves);Output.CustomAttributes.CopyFrom(HeapAttributes);R->SetNumberField(TEXT("inputEvaluations"),Source.Evaluations);
                R->SetObjectField(TEXT("input"),LyraCyclePoseProbe::PoseData(Base.Pose,Base.Curve,Base.CustomAttributes,Skeleton->GetReferenceSkeleton()));R->SetObjectField(TEXT("output"),LyraCyclePoseProbe::PoseData(Output.Pose,Output.Curve,Output.CustomAttributes,Skeleton->GetReferenceSkeleton()));
                FProxyAccess::Publish(MP,F->GetObjectField(TEXT("finalFeedback")));Layer->CopyCurveValues(*Main);
            }
            R->SetNumberField(TEXT("feedbackAfter"),Layer->GetCurveValue(TEXT("applyHipfireOverridePose")));Rows.Add(MakeShared<FJsonValueObject>(R));
        }
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));R->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));R->SetArrayField(TEXT("frames"),Rows);Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);FString Result;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Result));return Result;
}

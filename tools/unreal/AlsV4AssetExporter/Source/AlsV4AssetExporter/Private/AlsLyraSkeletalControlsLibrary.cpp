#include "AlsLyraSkeletalControlsLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "Animation/Skeleton.h"
#include "Animation/AnimNode_LinkedInputPose.h"
#include "Animation/AnimNode_Root.h"
#include "BoneControllers/AnimNode_ModifyBone.h"
#include "BoneControllers/AnimNode_FootPlacement.h"
#include "BoneControllers/AnimNode_HandIKRetargeting.h"
#include "BoneControllers/AnimNode_CopyBone.h"
#include "BoneControllers/AnimNode_TwoBoneIK.h"
#include "BoneControllers/AnimNode_LegIK.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/BoxComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace LyraCycleProbe{bool Set(UObject*,const TCHAR*,const TSharedPtr<FJsonValue>&);}
namespace LyraFootSeededProbe{TSharedPtr<FJsonObject> History(const FAnimNode_FootPlacement&);}
namespace LyraLegIKProbe{TSharedPtr<FJsonObject> History(const FAnimNode_LegIK&);}
namespace LyraSkeletalControlsProbe
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_SKELETAL_CONTROLS_FAILED line=%d"),L);return {};}
struct FInstanceAccess:UAnimInstance{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S)
    {
        (P.*&FProxyAccess::InitializeObjects)(A);TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add((FBoneIndexType)I);
        P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*S);P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);(P.*&FProxyAccess::CachedBonesCounter).Increment();
    }
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D){(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
    static void Publish(FAnimInstanceProxy& P,const TSharedPtr<FJsonObject>& V)
    {using FRead=TMap<FName,float>&(FAnimInstanceProxy::*)(EAnimCurveType);auto& C=(P.*static_cast<FRead>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve);C.Reset();for(const auto& E:V->Values)C.Add(FName(*E.Key),static_cast<float>(E.Value->AsNumber()));}
};
template<class Tag,typename Tag::Type Member>struct TAccess{friend typename Tag::Type Access(Tag){return Member;}};
struct FDeltaTag{using Type=float FAnimNode_FootPlacement::*;friend Type Access(FDeltaTag);};template struct TAccess<FDeltaTag,&FAnimNode_FootPlacement::CachedDeltaTime>;
struct FFirstTag{using Type=bool FAnimNode_FootPlacement::*;friend Type Access(FFirstTag);};template struct TAccess<FFirstTag,&FAnimNode_FootPlacement::bIsFirstUpdate>;
struct FCounterTag{using Type=FGraphTraversalCounter FAnimNode_FootPlacement::*;friend Type Access(FCounterTag);};template struct TAccess<FCounterTag,&FAnimNode_FootPlacement::UpdateCounter>;
struct FLegsTag{using Type=TArray<UE::Anim::FootPlacement::FLegRuntimeData> FAnimNode_FootPlacement::*;friend Type Access(FLegsTag);};template struct TAccess<FLegsTag,&FAnimNode_FootPlacement::LegsData>;
TSharedPtr<FJsonValue> Vector(const FVector& V){return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{MakeShared<FJsonValueNumber>(V.X),MakeShared<FJsonValueNumber>(V.Y),MakeShared<FJsonValueNumber>(V.Z)});}
FVector ReadVector(const TSharedPtr<FJsonObject>& O,const TCHAR* N){const auto& V=O->GetArrayField(N);return FVector(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber());}
FQuat ReadQuat(const TSharedPtr<FJsonObject>& O,const TCHAR* N){const auto& V=O->GetArrayField(N);return FQuat(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber(),V[3]->AsNumber());}
TSharedPtr<FJsonObject> BoolState(const FInputAlphaBoolBlend& B)
{
    auto R=MakeShared<FJsonObject>();R->SetBoolField(TEXT("initialized"),B.bInitialized);
    R->SetNumberField(TEXT("begin"),B.AlphaBlend.GetBeginValue());R->SetNumberField(TEXT("target"),B.AlphaBlend.GetDesiredValue());
    R->SetNumberField(TEXT("alpha"),B.AlphaBlend.GetAlpha());R->SetNumberField(TEXT("value"),B.AlphaBlend.GetBlendedValue());
    R->SetNumberField(TEXT("time"),B.AlphaBlend.GetBlendTime());R->SetNumberField(TEXT("remaining"),B.AlphaBlend.GetBlendTimeRemaining());return R;
}
TSharedPtr<FJsonObject> State(FAnimNode_ModifyBone& Root,FAnimNode_FootPlacement& Foot,const TArray<FAnimNode_SkeletalControlBase*>& Nodes)
{
    auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("rootBlend"),BoolState(Root.AlphaBoolBlend));R->SetObjectField(TEXT("footBlend"),BoolState(Foot.AlphaBoolBlend));
    R->SetNumberField(TEXT("footDelta"),Foot.*Access(FDeltaTag{}));R->SetBoolField(TEXT("footFirst"),Foot.*Access(FFirstTag{}));R->SetNumberField(TEXT("footCounter"),(Foot.*Access(FCounterTag{})).Get());
    TArray<TSharedPtr<FJsonValue>> A;for(auto* N:Nodes)A.Add(MakeShared<FJsonValueNumber>(N->GetAlpha()));R->SetArrayField(TEXT("alphas"),A);return R;
}
double Number(UObject* O,const TCHAR* Name){auto* P=FindFProperty<FDoubleProperty>(O->GetClass(),Name);check(P);return P->GetPropertyValue_InContainer(O);}
struct FSkeletalLeaf:FAnimNode_Base
{
    UAnimSequence* Sequence=nullptr;float Time=0,Previous=0,Delta=0;int32 Updates=0;
    virtual void Update_AnyThread(const FAnimationUpdateContext&)override{++Updates;}
    virtual void Evaluate_AnyThread(FPoseContext& O)override
    {FDeltaTimeRecord I;I.Set(Previous,Delta);FAnimationPoseData D(O);FAnimExtractContext E(Time,false,I,true);E.bExtractWithRootMotionProvider=true;Sequence->GetAnimationPose(D,E);}
};
TSharedPtr<FJsonObject> Modify(FAnimNode_ModifyBone& Node,const FPoseContext& Input,FAnimInstanceProxy& Proxy,USkeleton* Skeleton)
{
    FComponentSpacePoseContext O(&Proxy);O.Pose.InitPose(Input.Pose);O.Curve.CopyFrom(Input.Curve);O.CustomAttributes.CopyFrom(Input.CustomAttributes);
    if(Node.GetAlpha()>ZERO_ANIMWEIGHT_THRESH){TArray<FBoneTransform> Changes;Node.EvaluateSkeletalControl_AnyThread(O,Changes);if(!Changes.IsEmpty())O.Pose.LocalBlendCSBoneTransforms(Changes,Node.GetAlpha());}
    FCompactPose After=O.Pose.GetPose();FCSPose<FCompactPose>::ConvertComponentPosesToLocalPosesSafe(O.Pose,After);
    return LyraCyclePoseProbe::PoseData(After,O.Curve,O.CustomAttributes,Skeleton->GetReferenceSkeleton());
}
}

FString UAlsLyraSkeletalControlsLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraSkeletalControlsProbe;TSharedPtr<FJsonObject> Input;
    if(!MainClass||!Mesh||!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||Sequences.IsEmpty()||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Input->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());auto T=TV->AsObject();auto* LC=LoadObject<UClass>(nullptr,*T->GetStringField(TEXT("class")));auto* LI=LC?IAnimClassInterface::GetFromClass(LC):nullptr;if(!LI)return Fail(__LINE__);
        auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* Owner=W->SpawnActor<ACharacter>(Spawn);if(!Owner)return Fail(__LINE__);
        auto* Ground=W->SpawnActor<AActor>(Spawn);if(!Ground)return Fail(__LINE__);
        TStrongObjectPtr<UBoxComponent> Box(NewObject<UBoxComponent>(Ground,NAME_None,RF_Transient));Ground->SetRootComponent(Box.Get());Box->SetBoxExtent(FVector(10000,10000,5));Box->SetCollisionProfileName(TEXT("BlockAll"));Ground->AddInstanceComponent(Box.Get());Box->RegisterComponent();
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MainClass);C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();
        auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);Main->LinkAnimClassLayers(LC);auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Layer||Layer->GetClass()!=LC)return Fail(__LINE__);
        auto& MP=FInstanceAccess::Proxy(Main);auto& LP=FInstanceAccess::Proxy(Layer);Carrier->SetSkeleton(Skeleton);FProxyAccess::Setup(MP,Main,Skeleton);FProxyAccess::Setup(LP,Layer,Skeleton);
        auto* Root=LP.GetMutableNodeFromIndex<FAnimNode_Root>(113);auto* In=LP.GetMutableNodeFromIndex<FAnimNode_LinkedInputPose>(112);
        auto* Offset=LP.GetMutableNodeFromIndex<FAnimNode_ModifyBone>(104);auto* Weapon=LP.GetMutableNodeFromIndex<FAnimNode_ModifyBone>(106);auto* Foot=LP.GetMutableNodeFromIndex<FAnimNode_FootPlacement>(105);
        TArray<FAnimNode_SkeletalControlBase*> Nodes{LP.GetMutableNodeFromIndex<FAnimNode_HandIKRetargeting>(103),LP.GetMutableNodeFromIndex<FAnimNode_CopyBone>(102),Offset,
            LP.GetMutableNodeFromIndex<FAnimNode_TwoBoneIK>(110),LP.GetMutableNodeFromIndex<FAnimNode_TwoBoneIK>(109),Foot,LP.GetMutableNodeFromIndex<FAnimNode_LegIK>(107),Weapon};
        if(!Root||!In||Nodes.Contains(nullptr))return Fail(__LINE__);auto* Global=Layer->FindFunction(TEXT("UpdateSkelControlData"));if(!Global||Global->ParmsSize!=0)return Fail(__LINE__);
        FSkeletalLeaf Leaf;FPoseLink Source;Source.SetLinkNode(&Leaf);Source.Initialize(FAnimationInitializeContext(&MP));Source.CacheBones(FAnimationCacheBonesContext(&MP));In->DynamicUnlink();In->DynamicLink(&MP,&Source,1);
        FPoseLink Link;Link.SetLinkNode(Root);Link.Initialize(FAnimationInitializeContext(&LP));
        // Define first-use runtime storage only; original v1 probe stays unchanged.
        auto& Storage=(*Foot).*Access(FLegsTag{});for(int32 I=0;I<Storage.Num();++I){Storage[I]=UE::Anim::FootPlacement::FLegRuntimeData();Storage[I].Idx=I;}
        Link.CacheBones(FAnimationCacheBonesContext(&LP));auto* Leg=LP.GetMutableNodeFromIndex<FAnimNode_LegIK>(107);if(!Leg)return Fail(__LINE__);
        auto Trace=MakeShared<FJsonObject>();Trace->SetObjectField(TEXT("initial"),State(*Offset,*Foot,Nodes));Trace->SetNumberField(TEXT("initialCounter"),LP.GetUpdateCounter().Get());Trace->SetObjectField(TEXT("initialFoot"),LyraFootSeededProbe::History(*Foot));Trace->SetObjectField(TEXT("initialLegs"),LyraLegIKProbe::History(*Leg));
        TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark Frame(FMemStack::Get());auto F=FV->AsObject();float D=F->GetNumberField(TEXT("delta"));
            for(const auto& P:F->GetObjectField(TEXT("main"))->Values)if(!LyraCycleProbe::Set(Main,*P.Key,P.Value))return Fail(__LINE__);
            const FTransform Component(ReadQuat(F,TEXT("componentQ")),ReadVector(F,TEXT("componentP")));C->SetWorldTransform(Component,false,nullptr,ETeleportType::TeleportPhysics);
            const FVector Normal=ReadVector(F,TEXT("floorNormal")),Point=ReadVector(F,TEXT("floorPoint"));Box->SetCollisionEnabled(F->GetBoolField(TEXT("geometry"))?ECollisionEnabled::QueryOnly:ECollisionEnabled::NoCollision);Box->SetWorldTransform(FTransform(FQuat::FindBetweenNormals(FVector::UpVector,Normal),Point-Normal*5),false,nullptr,ETeleportType::TeleportPhysics);
            auto* Move=Owner->GetCharacterMovement();Move->MovementMode=F->GetBoolField(TEXT("walking"))?MOVE_Walking:MOVE_Falling;Move->CurrentFloor.bBlockingHit=F->GetBoolField(TEXT("blocking"));Move->CurrentFloor.HitResult.ImpactPoint=Point;Move->CurrentFloor.HitResult.ImpactNormal=Normal;Move->Velocity=ReadVector(F,TEXT("velocity"));
            FProxyAccess::Pre(MP,Main,D);FProxyAccess::Pre(LP,Layer,D);
            LI->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& S){if(S.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct()){FAnimSubsystemUpdateContext G(S,Layer,D);S.Subsystem.OnPreUpdate_GameThread(G);S.Subsystem.OnPostUpdate_GameThread(G);FAnimSubsystemParallelUpdateContext P(S,LP,D);S.Subsystem.OnPreUpdate_WorkerThread(P);}return EAnimSubsystemEnumeration::Continue;});
            auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("before"),State(*Offset,*Foot,Nodes));R->SetObjectField(TEXT("footBefore"),LyraFootSeededProbe::History(*Foot));R->SetObjectField(TEXT("legBefore"),LyraLegIKProbe::History(*Leg));
            auto Feedback=MakeShared<FJsonObject>();for(const TCHAR* Name:{TEXT("DisableRHandIK"),TEXT("DisableLHandIK"),TEXT("DisableHandIKRetargeting"),TEXT("DisableLegIK"),TEXT("ScaleDownWeaponR")})Feedback->SetNumberField(Name,Layer->GetCurveValue(FName(Name)));R->SetObjectField(TEXT("feedbackBefore"),Feedback);
            Layer->ProcessEvent(Global,nullptr);R->SetNumberField(TEXT("rightWeight"),Number(Layer,TEXT("HandIK_Right_Alpha")));R->SetNumberField(TEXT("leftWeight"),Number(Layer,TEXT("HandIK_Left_Alpha")));
            if(F->GetBoolField(TEXT("initialize")))Link.Initialize(FAnimationInitializeContext(&LP));
            Leaf.Updates=0;Leaf.Sequence=Sequences[(int32)F->GetNumberField(TEXT("asset"))];Leaf.Time=F->GetNumberField(TEXT("time"));Leaf.Previous=F->GetNumberField(TEXT("previous"));Leaf.Delta=F->GetNumberField(TEXT("sourceDelta"));
            if(F->GetBoolField(TEXT("visited")))Link.Update(FAnimationUpdateContext(&LP,D));R->SetObjectField(TEXT("updated"),State(*Offset,*Foot,Nodes));R->SetNumberField(TEXT("inputUpdates"),Leaf.Updates);R->SetObjectField(TEXT("footUpdated"),LyraFootSeededProbe::History(*Foot));R->SetNumberField(TEXT("handWeight"),LP.GetMutableNodeFromIndex<FAnimNode_HandIKRetargeting>(103)->HandFKWeight);
            if(F->GetBoolField(TEXT("visited"))&&F->GetBoolField(TEXT("evaluate")))
            {
                FPoseContext Base(&MP);Source.Evaluate(Base);R->SetObjectField(TEXT("input"),LyraCyclePoseProbe::PoseData(Base.Pose,Base.Curve,Base.CustomAttributes,Skeleton->GetReferenceSkeleton()));
                FPoseContext Out(&LP);Link.Evaluate(Out);R->SetObjectField(TEXT("output"),LyraCyclePoseProbe::PoseData(Out.Pose,Out.Curve,Out.CustomAttributes,Skeleton->GetReferenceSkeleton()));
            }
            if(F->GetBoolField(TEXT("visited"))&&F->GetBoolField(TEXT("evaluate")))
            {
                TArray<TSharedPtr<FJsonValue>> Hits;const FVector Direction=Component.TransformVectorNoScale(-FVector::UpVector);
                if(Foot->GetAlpha()>ZERO_ANIMWEIGHT_THRESH)for(const auto& L:(*Foot).*Access(FLegsTag{}))
                {
                    const FVector StartPosition=L.UnalignedFootTransformWS.GetLocation();FHitResult Hit;FCollisionQueryParams Params;Params.bTraceComplex=true;Params.AddIgnoredActor(Owner);
                    const bool HitFound=W->SweepSingleByChannel(Hit,StartPosition+Direction*Foot->TraceSettings.StartOffset,StartPosition+Direction*Foot->TraceSettings.EndOffset,FQuat::Identity,UEngineTypes::ConvertToCollisionChannel(Foot->TraceSettings.ComplexTraceChannel),FCollisionShape::MakeSphere(Foot->TraceSettings.SweepRadius),Params);
                    auto H=MakeShared<FJsonObject>();H->SetField(TEXT("start"),Vector(StartPosition));H->SetBoolField(TEXT("hit"),HitFound);H->SetBoolField(TEXT("walkable"),HitFound&&Move->IsWalkable(Hit));H->SetField(TEXT("point"),Vector(Hit.ImpactPoint));H->SetField(TEXT("normal"),Vector(Hit.ImpactNormal));Hits.Add(MakeShared<FJsonValueObject>(H));
                }R->SetArrayField(TEXT("hits"),Hits);
            }
            R->SetObjectField(TEXT("after"),State(*Offset,*Foot,Nodes));R->SetObjectField(TEXT("footAfter"),LyraFootSeededProbe::History(*Foot));R->SetObjectField(TEXT("legAfter"),LyraLegIKProbe::History(*Leg));
            if(F->GetBoolField(TEXT("evaluateMain"))){FProxyAccess::Publish(MP,F->GetObjectField(TEXT("finalFeedback")));Layer->CopyCurveValues(*Main);}Rows.Add(MakeShared<FJsonValueObject>(R));
        }
        Trace->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));Trace->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));Trace->SetArrayField(TEXT("frames"),Rows);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);FString Json;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Json));return Json;
}

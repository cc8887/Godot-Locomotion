#include "LyraWholeMainOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "AnimNodes/AnimNode_BlendSpacePlayer.h"
#include "AnimNode_ControlRig.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/BoxComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/PlayerController.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraDefaultMain
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_DEFAULT_MAIN_FAILED line=%d"),Line);return {};}
TArray<TSharedPtr<FJsonValue>> Values(std::initializer_list<double> X)
{TArray<TSharedPtr<FJsonValue>> A;for(double V:X){check(FMath::IsFinite(V));A.Add(MakeShared<FJsonValueNumber>(V));}return A;}
FVector Vector(const TSharedPtr<FJsonObject>& O,const TCHAR* Name)
{const auto& V=O->GetArrayField(Name);check(V.Num()==3);return FVector(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber());}
TSharedPtr<FJsonObject> Fields(UObject* A)
{
    auto R=MakeShared<FJsonObject>();
    for(TFieldIterator<FProperty> I(A->GetClass());I;++I)
    {
        const auto* P=*I;const void* V=P->ContainerPtrToValuePtr<void>(A);
        if(auto* B=CastField<FBoolProperty>(P))R->SetBoolField(P->GetName(),B->GetPropertyValue(V));
        else if(auto* N=CastField<FNumericProperty>(P))
        {
            R->SetNumberField(P->GetName(),N->IsFloatingPoint()?N->GetFloatingPointPropertyValue(V):N->GetSignedIntPropertyValue(V));
            if(N->IsFloatingPoint()&&N->GetSize()==8){uint64 Bits;FMemory::Memcpy(&Bits,V,8);R->SetStringField(P->GetName()+TEXT("Bits"),FString::Printf(TEXT("%016llx"),Bits));}
        }
        else if(auto* S=CastField<FStructProperty>(P);S&&S->Struct==TBaseStructure<FVector>::Get())
        {const auto Q=*static_cast<const FVector*>(V);R->SetArrayField(P->GetName(),Values({Q.X,Q.Y,Q.Z}));}
    }
    return R;
}
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Publish(FAnimInstanceProxy& P,const FPoseContext& C)
    {auto& M=(P.*static_cast<TMap<FName,float>&(FAnimInstanceProxy::*)(EAnimCurveType)>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve);M.Reset();C.Curve.ForEachElement([&](const auto& E){M.Add(E.Name,E.Value);});}
};
TSharedPtr<FJsonObject> Pose(const FPoseContext& O)
{
    auto R=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Bones,Curves;
    for(auto B:O.Pose.ForEachBoneIndex())
    {
        const auto& T=O.Pose[B];const auto P=T.GetTranslation(),S=T.GetScale3D();const auto Q=T.GetRotation();auto V=MakeShared<FJsonObject>();
        V->SetArrayField(TEXT("position"),Values({P.X,P.Y,P.Z}));V->SetArrayField(TEXT("rotation"),Values({Q.X,Q.Y,Q.Z,Q.W}));V->SetArrayField(TEXT("scale"),Values({S.X,S.Y,S.Z}));Bones.Add(MakeShared<FJsonValueObject>(V));
    }
    O.Curve.ForEachElement([&](const auto& E){auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("name"),E.Name.ToString());V->SetNumberField(TEXT("value"),E.Value);V->SetNumberField(TEXT("flags"),static_cast<uint32>(E.Flags));Curves.Add(MakeShared<FJsonValueObject>(V));});
    R->SetArrayField(TEXT("pose"),Bones);R->SetArrayField(TEXT("curves"),Curves);return R;
}
struct FTap:FAnimNode_Base
{
    FPoseLink Child;int32 Updates=0,Evaluations=0;TSharedPtr<FJsonObject> Output;
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override{Child.Initialize(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override{Child.CacheBones(C);}
    void Update_AnyThread(const FAnimationUpdateContext& C) override{++Updates;Child.Update(C);}
    void Evaluate_AnyThread(FPoseContext& C) override{++Evaluations;Child.Evaluate(C);Output=Pose(C);}
    void Reset(){Updates=Evaluations=0;Output.Reset();}
};
}

FString ULyraWholeMainOracleLibrary::ReadDefaultMain(const FString& RequestsJson)
{
    using namespace LyraDefaultMain;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* MainClass=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("main")));
    auto* Provider=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("provider")));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));
    const auto* Interface=MainClass?IAnimClassInterface::GetFromClass(MainClass):nullptr;
    if(!Interface||!Provider||!Mesh)return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Q->GetArrayField(TEXT("traces")))
    {
        const FMemMark Mark(FMemStack::Get());const auto T=TV->AsObject();
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{World.Get()};
        auto* Owner=World->SpawnActor<ACharacter>();auto* Controller=World->SpawnActor<APlayerController>();auto* Ground=World->SpawnActor<AActor>();if(!Owner||!Controller||!Ground)return Fail(__LINE__);Controller->Possess(Owner);
        TStrongObjectPtr<UBoxComponent> Floor(NewObject<UBoxComponent>(Ground,NAME_None,RF_Transient));Ground->SetRootComponent(Floor.Get());Floor->SetBoxExtent(FVector(10000,10000,5));Floor->SetCollisionProfileName(TEXT("BlockAll"));Floor->SetWorldLocation(FVector(0,0,-5));Ground->AddInstanceComponent(Floor.Get());Floor->RegisterComponent();
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMeshAsset(Mesh);C->SetAnimInstanceClass(MainClass);C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();
        auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);auto& Proxy=FInstanceAccess::Proxy(Main);
        TArray<FBoneIndexType> Required;for(int32 I=0;I<Mesh->GetRefSkeleton().GetNum();++I)Required.Add(I);
        Proxy.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*Mesh);Proxy.GetRequiredBones().SetUseRAWData(true);
        FAnimNode_LinkedAnimLayer* Skeletal=nullptr;
        for(auto* P:Interface->GetLinkedAnimLayerNodeProperties()){auto* N=P->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);if(N->Layer==TEXT("FullBody_SkeletalControls"))Skeletal=N;}
        auto* Rig=Proxy.GetMutableNodeFromIndex<FAnimNode_ControlRig>(73);
        if(!Skeletal||Skeletal->InputPoses.Num()!=1||!Rig)return Fail(__LINE__);
        auto* SourceProperty=FindFProperty<FStructProperty>(FAnimNode_ControlRig::StaticStruct(),TEXT("Source"));
        if(!SourceProperty||SourceProperty->Struct!=FPoseLink::StaticStruct())return Fail(__LINE__);
        auto* RigSource=SourceProperty->ContainerPtrToValuePtr<FPoseLink>(Rig);
        FTap Upstream,PreRig;Upstream.Child=Skeletal->InputPoses[0];PreRig.Child=*RigSource;
        const auto OriginalInput=Skeletal->InputPoses[0],OriginalRig=*RigSource;
        struct FRestore{FAnimNode_LinkedAnimLayer* N;FPoseLink* R;FPoseLink I,S;~FRestore(){N->InputPoses[0]=I;*R=S;}} Restore{Skeletal,RigSource,OriginalInput,OriginalRig};
        Skeletal->InputPoses[0].SetLinkNode(&Upstream);RigSource->SetLinkNode(&PreRig);
        FAnimationInitializeContext Initialize(&Proxy);FAnimationCacheBonesContext Cache(&Proxy);Upstream.Initialize_AnyThread(Initialize);Upstream.CacheBones_AnyThread(Cache);PreRig.CacheBones_AnyThread(Cache);
        TArray<TSharedPtr<FJsonValue>> Rows;int32 Frame=0;
        const uint64 InitialFrame=GFrameCounter;
        struct FCounterRestore{uint64 Value;~FCounterRestore(){GFrameCounter=Value;}} CounterRestore{InitialFrame};
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            const FMemMark FrameMark(FMemStack::Get());
            // The commandlet otherwise keeps GFrameCounter fixed. Proxy worker
            // callbacks execute once per engine frame, independently of graph
            // Update counters. Each requested physical sample is a new frame.
            ++GFrameCounter;
            const auto F=FV->AsObject();const float Delta=F->GetNumberField(TEXT("delta"));FString Operation;
            if(F->TryGetStringField(TEXT("operation"),Operation))
            {if(Operation==TEXT("link"))Main->LinkAnimClassLayers(Provider);else if(Operation==TEXT("unlink"))Main->UnlinkAnimClassLayers(Provider);else if(Operation==TEXT("unlink-null"))Main->UnlinkAnimClassLayers(nullptr);else return Fail(__LINE__);}
            auto* Move=Owner->GetCharacterMovement();Owner->SetActorLocation(Vector(F,TEXT("location")));Owner->SetActorRotation(FRotator(0,F->GetNumberField(TEXT("yaw")),0));Move->Velocity=Vector(F,TEXT("velocity"));
            *FindFProperty<FStructProperty>(Move->GetClass(),TEXT("Acceleration"))->ContainerPtrToValuePtr<FVector>(Move)=Vector(F,TEXT("acceleration"));
            *FindFProperty<FStructProperty>(Move->GetClass(),TEXT("LastUpdateVelocity"))->ContainerPtrToValuePtr<FVector>(Move)=Move->Velocity;
            Move->MovementMode=F->GetBoolField(TEXT("ground"))?MOVE_Walking:MOVE_Falling;
            FindFProperty<FBoolProperty>(Owner->GetClass(),TEXT("bIsCrouched"))->SetPropertyValue_InContainer(Owner,F->GetBoolField(TEXT("crouching")));
            Controller->SetControlRotation(FRotator(F->GetNumberField(TEXT("pitch")),0,0));Move->CurrentFloor.bBlockingHit=Move->IsMovingOnGround();Move->CurrentFloor.HitResult.ImpactNormal=FVector::UpVector;
            for(const auto& E:F->GetObjectField(TEXT("mainProperties"))->Values)
            {
                auto* P=MainClass->FindPropertyByName(FName(*E.Key));
                if(auto* B=CastField<FBoolProperty>(P))B->SetPropertyValue_InContainer(Main,E.Value->AsBool());
                else if(auto* N=CastField<FNumericProperty>(P);N&&N->IsFloatingPoint())N->SetFloatingPointPropertyValue(N->ContainerPtrToValuePtr<void>(Main),E.Value->AsNumber());else return Fail(__LINE__);
            }
            auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("frame"),Frame++);Row->SetObjectField(TEXT("mainBefore"),Fields(Main));
            const FRotator ObservedRotation=Owner->GetActorRotation();auto Physical=MakeShared<FJsonObject>();
            Physical->SetArrayField(TEXT("rotation"),Values({ObservedRotation.Pitch,ObservedRotation.Yaw,ObservedRotation.Roll}));
            Physical->SetNumberField(TEXT("aimPitch"),Owner->GetBaseAimRotation().Pitch);Row->SetObjectField(TEXT("physicalInput"),Physical);
            Upstream.Reset();PreRig.Reset();
            Main->PreUpdateLinkedInstances(Delta);
            const auto Linked=static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances();for(auto* A:Linked)A->UpdateAnimation(Delta,false,UAnimInstance::EUpdateAnimationFlag::ForceParallelUpdate);
            Main->UpdateAnimation(Delta,false,UAnimInstance::EUpdateAnimationFlag::ForceParallelUpdate);Main->ParallelUpdateAnimation();Main->PostUpdateAnimation();Row->SetObjectField(TEXT("mainUpdated"),Fields(Main));
            Row->SetNumberField(TEXT("linkedInstances"),Linked.Num());TArray<TSharedPtr<FJsonValue>> Calls;
            for(auto* P:Interface->GetLinkedAnimLayerNodeProperties())
            {auto* N=P->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);auto* A=N->GetTargetInstance<UAnimInstance>();auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("hook"),N->Layer.ToString());V->SetBoolField(TEXT("self"),A==Main);V->SetStringField(TEXT("class"),A?A->GetClass()->GetPathName():TEXT(""));Calls.Add(MakeShared<FJsonValueObject>(V));}
            Row->SetArrayField(TEXT("calls"),Calls);
            if(F->GetBoolField(TEXT("evaluate")))
            {
                Main->PreEvaluateAnimation();FCompactPose Bones;FBlendedHeapCurve Curves;UE::Anim::FHeapAttributeContainer Attributes;FParallelEvaluationData Data{Curves,Bones,Attributes};Main->ParallelEvaluateAnimation(false,Mesh,Data);
                FPoseContext Out(&Proxy);Out.Pose.CopyBonesFrom(Bones);Out.Curve.CopyFrom(Curves);Out.CustomAttributes.CopyFrom(Attributes);Row->SetObjectField(TEXT("output"),Pose(Out));FProxyAccess::Publish(Proxy,Out);for(auto* A:Linked)A->CopyCurveValues(*Main);Main->PostEvaluateAnimation();
            }
            Row->SetNumberField(TEXT("upstreamUpdates"),Upstream.Updates);Row->SetNumberField(TEXT("upstreamEvaluations"),Upstream.Evaluations);Row->SetNumberField(TEXT("preRigUpdates"),PreRig.Updates);Row->SetNumberField(TEXT("preRigEvaluations"),PreRig.Evaluations);if(PreRig.Output)Row->SetObjectField(TEXT("preRig"),PreRig.Output);
            TArray<TSharedPtr<FJsonValue>> Lean;
            for(int32 Index:{12,16,22}){auto* N=Proxy.GetMutableNodeFromIndex<FAnimNode_BlendSpacePlayer>(Index);if(!N)return Fail(__LINE__);auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("node"),Index);V->SetNumberField(TEXT("time"),N->GetAccumulatedTime());Lean.Add(MakeShared<FJsonValueObject>(V));}Row->SetArrayField(TEXT("mainLean"),Lean);
            auto* Machine=Proxy.GetMutableNodeFromIndex<FAnimNode_StateMachine>(7);Row->SetNumberField(TEXT("machineState"),Machine->GetCurrentState());Row->SetNumberField(TEXT("machineElapsed"),Machine->GetCurrentStateElapsedTime());
            Row->SetNumberField(TEXT("locomotionCacheWeight"),Proxy.GetMutableNodeFromIndex<FAnimNode_SaveCachedPose>(83)->GlobalWeight);
            Row->SetObjectField(TEXT("mainAfter"),Fields(Main));Main->DispatchQueuedAnimEvents();Rows.Add(MakeShared<FJsonValueObject>(Row));
        }
        auto Trace=MakeShared<FJsonObject>();Trace->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));Trace->SetArrayField(TEXT("frames"),Rows);Traces.Add(MakeShared<FJsonValueObject>(Trace));C->UnregisterComponent();
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Json;FJsonSerializer::Serialize(Result,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json));return Json;
}

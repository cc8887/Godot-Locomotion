#include "AlsLyraLocomotionLibrary.h"
#include "AlsLyraMainObservationProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_StateResult.h"
#include "Animation/AnimNode_TransitionResult.h"
#include "Animation/AnimNode_Inertialization.h"
#include "Animation/AnimNode_RelevantAssetPlayerBase.h"
#include "Animation/AnimSyncScope.h"
#include "Animation/AnimSync.h"
#include "Animation/AnimInertializationSyncScope.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "Animation/AnimationAsset.h"
#include "Animation/BlendProfile.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/PlayerController.h"
#include "UObject/StrongObjectPtr.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace LyraMachineProbe
{
FString Failure(int32 Line)
{ UE_LOG(LogTemp,Error,TEXT("LYRA_MAIN_MACHINE_CAPTURE_FAILED line=%d"),Line);return {}; }
struct FInstanceAccess : UAnimInstance
{ static FAnimInstanceProxy& Proxy(UAnimInstance* A) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A); } };
// Native oracle instrumentation only: a typed pointer-to-member obtained by
// explicit instantiation (no layout offsets, engine edits or copied Sync).
// The deprecated proxy tick flips twice in this engine; the current native
// path calls FAnimSync::TickAssetPlayerInstances exactly once.
struct FSyncMember
{ using Type=UE::Anim::FAnimSync FAnimInstanceProxy::*;friend Type NativeSync(FSyncMember); };
template<class Tag,typename Tag::Type Member> struct TOracleMember
{ friend typename Tag::Type NativeSync(Tag) { return Member; } };
template struct TOracleMember<FSyncMember,&FAnimInstanceProxy::Sync>;
struct FProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D)
    { (P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D); }
    static void Sync(FAnimInstanceProxy& P,float D)
    { (P.*NativeSync(FSyncMember{})).TickAssetPlayerInstances(P,D); }
};
struct FMachineAccess : FAnimNode_StateMachine
{
    static TSharedPtr<FJsonObject> Relevant(const FAnimNode_StateMachine& M,FAnimInstanceProxy& P)
    {
        const auto R=MakeShared<FJsonObject>();
        const auto* S=(M.*&FMachineAccess::GetMachineDescription)();
        const auto* Player=(M.*static_cast<const FAnimNode_AssetPlayerRelevancyBase*(FAnimNode_StateMachine::*)(const FAnimInstanceProxy*,const FBakedAnimationState&) const>(
            &FMachineAccess::GetRelevantAssetPlayerInterfaceFromState))(&P,S->States[M.GetCurrentState()]);
        const auto* Asset=Player ? Player->GetAnimAsset() : nullptr;
        R->SetBoolField(TEXT("valid"),Asset!=nullptr);R->SetStringField(TEXT("asset"),Asset ? Asset->GetPathName() : TEXT(""));
        R->SetNumberField(TEXT("length"),Asset ? Asset->GetPlayLength() : 0);R->SetNumberField(TEXT("time"),Player ? Player->GetAccumulatedTime() : 0);
        R->SetBoolField(TEXT("looping"),Player && Player->IsLooping());
        const auto* D=Player ? Player->GetDeltaTimeRecord() : nullptr;
        R->SetBoolField(TEXT("previousValid"),D && D->IsPreviousValid());R->SetNumberField(TEXT("previous"),D ? D->GetPrevious() : 0);R->SetNumberField(TEXT("delta"),D ? D->Delta : 0);
        return R;
    }
    static TArray<TSharedPtr<FJsonValue>> Active(const FAnimNode_StateMachine& M)
    {
        TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& T : M.*&FMachineAccess::ActiveTransitionArray)
        {
            auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("previous"),T.PreviousState);R->SetNumberField(TEXT("next"),T.NextState);
            R->SetNumberField(TEXT("duration"),T.CrossfadeDuration);R->SetNumberField(TEXT("elapsed"),T.ElapsedTime);R->SetNumberField(TEXT("alpha"),T.Alpha);
            R->SetBoolField(TEXT("inertial"),T.LogicType==ETransitionLogicType::TLT_Inertialization);
            TArray<TSharedPtr<FJsonValue>> Path;for(int32 I : T.SourceTransitionIndices) Path.Add(MakeShared<FJsonValueNumber>(I));R->SetArrayField(TEXT("path"),Path);
            Rows.Add(MakeShared<FJsonValueObject>(R));
        }
        return Rows;
    }
};
struct FTap : FAnimNode_Base
{
    FPoseLink Child;int32 State=0;int32 Initializations=0;TArray<TSharedPtr<FJsonValue>>* Updates=nullptr;
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override { ++Initializations;Child.Initialize(C); }
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override { Child.CacheBones(C); }
    void Update_AnyThread(const FAnimationUpdateContext& C) override
    {
        auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("state"),State);R->SetNumberField(TEXT("weight"),C.GetFinalBlendWeight());R->SetBoolField(TEXT("active"),C.IsActive());
        R->SetBoolField(TEXT("inertial"),C.GetMessage<UE::Anim::FAnimInertializationSyncScope>()!=nullptr);Updates->Add(MakeShared<FJsonValueObject>(R));Child.Update(C);
    }
    void Evaluate_AnyThread(FPoseContext& C) override { Child.Evaluate(C); }
};
struct FRequests : UE::Anim::IInertializationRequester
{
    TArray<TSharedPtr<FJsonValue>>& Rows;
    explicit FRequests(TArray<TSharedPtr<FJsonValue>>& InRows) : Rows(InRows) {}
    void RequestInertialization(float D,const UBlendProfile* B) override
    { auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("duration"),D);R->SetBoolField(TEXT("useBlendMode"),false);R->SetStringField(TEXT("profile"),B ? B->GetPathName() : TEXT(""));Rows.Add(MakeShared<FJsonValueObject>(R)); }
    void RequestInertialization(const FInertializationRequest& Q) override
    { auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("duration"),Q.Duration);R->SetBoolField(TEXT("useBlendMode"),Q.bUseBlendMode);R->SetNumberField(TEXT("blendMode"),(int32)Q.BlendMode);R->SetStringField(TEXT("profile"),Q.BlendProfile ? Q.BlendProfile->GetPathName() : TEXT(""));Rows.Add(MakeShared<FJsonValueObject>(R)); }
    void AddDebugRecord(const FAnimInstanceProxy&,int32) override {} FName GetTag() const override { return TEXT("LyraMachineProbe"); }
};
TSharedPtr<FJsonObject> Fields(UAnimInstance* Main)
{
    auto R=MakeShared<FJsonObject>();
    for(const TCHAR* N : {TEXT("HasAcceleration"),TEXT("HasVelocity"),TEXT("GameplayTag_IsMelee"),TEXT("IsRunningIntoWall"),TEXT("LinkedLayerChanged"),TEXT("CrouchStateChange"),TEXT("ADSStateChanged"),
        TEXT("IsJumping"),TEXT("IsFalling"),TEXT("IsOnGround"),TEXT("LocalVelocity2D"),TEXT("LocalAcceleration2D"),TEXT("StartDirection"),TEXT("LocalVelocityDirection"),TEXT("PivotInitialDirection"),
        TEXT("DisplacementSpeed"),TEXT("RootYawOffset"),TEXT("LastPivotTime"),TEXT("TimeToJumpApex"),TEXT("GroundDistance")})
    {
        auto* P=Main->GetClass()->FindPropertyByName(N);if(!P) return {};
        if(auto* B=CastField<FBoolProperty>(P)) R->SetBoolField(N,B->GetPropertyValue_InContainer(Main));
        else if(auto* E=CastField<FEnumProperty>(P)) R->SetNumberField(N,E->GetUnderlyingProperty()->GetSignedIntPropertyValue(E->ContainerPtrToValuePtr<void>(Main)));
        else if(auto* V=CastField<FNumericProperty>(P)) {const void* A=V->ContainerPtrToValuePtr<void>(Main);R->SetNumberField(N,V->IsFloatingPoint() ? V->GetFloatingPointPropertyValue(A) : V->GetSignedIntPropertyValue(A));}
        else if(auto* Struct=CastField<FStructProperty>(P);Struct && Struct->Struct==TBaseStructure<FVector>::Get())
        {const auto& A=*Struct->ContainerPtrToValuePtr<FVector>(Main);R->SetArrayField(N,{MakeShared<FJsonValueNumber>(A.X),MakeShared<FJsonValueNumber>(A.Y),MakeShared<FJsonValueNumber>(A.Z)});}
        else return {};
    }
    return R;
}
}

FString UAlsLyraLocomotionLibrary::ReadMachineTrace(UClass* MainClass,USkeletalMesh* Mesh,const FString& RequestsJson)
{
    using namespace LyraMachineProbe;
    TSharedPtr<FJsonObject> Input;const auto* MI=MainClass ? IAnimClassInterface::GetFromClass(MainClass) : nullptr;
    if(!MI || !Mesh || MI->GetBakedStateMachines().Num()!=1 || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input)) return Failure(__LINE__);
    const auto& Definition=MI->GetBakedStateMachines()[0];if(Definition.States.Num()!=12) return Failure(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TraceValue : Input->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());const auto Trace=TraceValue->AsObject();auto* LC=LoadObject<UClass>(nullptr,*Trace->GetStringField(TEXT("class")));
        const IAnimClassInterface* LI=LC ? IAnimClassInterface::GetFromClass(LC) : nullptr;if(!LI) return Failure(__LINE__);
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid()) return Failure(__LINE__);
        struct FCleanup { UWorld* W;~FCleanup(){W->DestroyWorld(false);} } Cleanup{World.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
        auto* Owner=World->SpawnActor<ACharacter>(Spawn);auto* Controller=World->SpawnActor<APlayerController>(Spawn);if(!Owner || !Controller) return Failure(__LINE__);Controller->Possess(Owner);
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        Component->bUseRefPoseOnInitAnim=true;Component->SetDisablePostProcessBlueprint(true);Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Component->SetSkeletalMesh(Mesh);Component->SetAnimInstanceClass(MainClass);Component->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(Component.Get());Component->RegisterComponent();
        auto* Main=Component->GetAnimInstance();if(!Main) return Failure(__LINE__);Main->LinkAnimClassLayers(LC);auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Layer || Layer->GetClass()!=LC) return Failure(__LINE__);
        auto& MP=FInstanceAccess::Proxy(Main);FAnimNode_StateMachine* Machine=nullptr;int32 MachineProperty=-1;
        for(int32 I=0;I<MI->GetAnimNodeProperties().Num();++I) if(MI->GetAnimNodeProperties()[I]->Struct==FAnimNode_StateMachine::StaticStruct())
        { Machine=MI->GetAnimNodeProperties()[I]->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Main);MachineProperty=I; }
        if(!Machine || Machine->StateMachineIndexInClass!=0) return Failure(__LINE__);Machine->CacheMachineDescription(const_cast<IAnimClassInterface*>(MI));
        TArray<FTap> Taps;Taps.SetNum(12);TArray<TSharedPtr<FJsonValue>> Updates;
        TArray<FAnimNode_StateResult*> Roots;Roots.SetNumZeroed(12);TArray<FPoseLink> Originals;Originals.SetNum(12);
        struct FRestore { TArray<FAnimNode_StateResult*>& R;TArray<FPoseLink>& O;~FRestore(){for(int32 I=0;I<R.Num();++I) if(R[I]) R[I]->Result=O[I];} } Restore{Roots,Originals};
        for(int32 S=0;S<12;++S) if(!Definition.States[S].bIsAConduit)
        {Roots[S]=MP.GetMutableNodeFromIndex<FAnimNode_StateResult>(Definition.States[S].StateRootNodeIndex);if(!Roots[S]) return Failure(__LINE__);
         Originals[S]=Roots[S]->Result;Taps[S].Child=Originals[S];Taps[S].State=S;Taps[S].Updates=&Updates;Roots[S]->Result.SetLinkNode(&Taps[S]);}
        bool Initialized=false;TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FrameValue : Trace->GetArrayField(TEXT("frames")))
        {
            const auto Frame=FrameValue->AsObject();const float Delta=Frame->GetNumberField(TEXT("delta"));int32 Counts[12];for(int32 S=0;S<12;++S) Counts[S]=Taps[S].Initializations;
            const FString Provider=Frame->GetStringField(TEXT("providerClass"));
            if(Provider!=Layer->GetClass()->GetPathName())
            {auto* NextClass=LoadObject<UClass>(nullptr,*Provider);if(!NextClass) return Failure(__LINE__);Main->LinkAnimClassLayers(NextClass);Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));
             if(!Layer || Layer->GetClass()!=NextClass) return Failure(__LINE__);LI=IAnimClassInterface::GetFromClass(NextClass);if(!LI) return Failure(__LINE__);}
            auto& LP=FInstanceAccess::Proxy(Layer);
            const auto Obs=Frame->GetObjectField(TEXT("observation"));auto* Mode=FindFProperty<FNumericProperty>(MainClass,TEXT("RootYawOffsetMode"));if(!Mode) return Failure(__LINE__);
            Obs->SetNumberField(TEXT("mode"),Mode->GetSignedIntPropertyValue(Mode->ContainerPtrToValuePtr<void>(Main)));
            MP.FlipBufferWriteIndex();auto Observation=LyraMainObservationProbe::Tick(Main,Owner,MP,MI,Obs,Delta);if(!Observation) return Failure(__LINE__);
            FProxyAccess::Pre(LP,Layer,Delta);
            LI->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& C)
            {if(C.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct()){FAnimSubsystemUpdateContext G(C,Layer,Delta);C.Subsystem.OnPreUpdate_GameThread(G);C.Subsystem.OnPostUpdate_GameThread(G);FAnimSubsystemParallelUpdateContext W(C,LP,Delta);C.Subsystem.OnPreUpdate_WorkerThread(W);}return EAnimSubsystemEnumeration::Continue;});
            if(!Initialized || Frame->GetBoolField(TEXT("reinitialize")))
            {Machine->Initialize_AnyThread(FAnimationInitializeContext(&MP));Machine->CacheBones_AnyThread(FAnimationCacheBonesContext(&MP));Initialized=true;}
            auto* GD=FindFProperty<FNumericProperty>(MainClass,TEXT("GroundDistance"));if(!GD || !GD->IsFloatingPoint()) return Failure(__LINE__);GD->SetFloatingPointPropertyValue(GD->ContainerPtrToValuePtr<void>(Main),Frame->GetNumberField(TEXT("groundDistance")));
            auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("providerClass"),Layer->GetClass()->GetPathName());R->SetObjectField(TEXT("observation"),Observation);R->SetObjectField(TEXT("fields"),Fields(Main));R->SetObjectField(TEXT("relevant"),FMachineAccess::Relevant(*Machine,MP));
            R->SetNumberField(TEXT("beforeState"),Machine->GetCurrentState());R->SetNumberField(TEXT("beforeElapsed"),Machine->GetCurrentStateElapsedTime());
            R->SetBoolField(TEXT("syncValid"),MP.IsSyncGroupValid(TEXT("Locomotion")));
            TArray<TSharedPtr<FJsonValue>> Previous;for(int32 S=0;S<12;++S) Previous.Add(MakeShared<FJsonValueNumber>(MP.GetRecordedStateWeight(0,S)));R->SetArrayField(TEXT("previousWeights"),Previous);
            Updates.Reset();FAnimationUpdateSharedContext Shared;auto C=FAnimationUpdateContext(&MP,Delta,&Shared).FractionalWeight(Frame->GetNumberField(TEXT("weight")));C.SetNodeId(MachineProperty);
            TArray<TSharedPtr<FJsonValue>> RequestRows;UE::Anim::TScopedGraphMessage<FRequests> RequestScope(C,RequestRows);UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> SyncScope(C,C);
            const auto* PivotExit=Definition.States[4].Transitions.FindByPredicate([](const FBakedStateExitTransition& E){return E.TransitionIndex==19;});if(!PivotExit) return Failure(__LINE__);
            auto* PivotRule=MP.GetMutableNodeFromIndex<FAnimNode_TransitionResult>(PivotExit->CanTakeDelegateIndex);if(!PivotRule) return Failure(__LINE__);
            PivotRule->GetEvaluateGraphExposedInputs().Execute(C);R->SetBoolField(TEXT("pivotNotify"),PivotRule->bCanEnterTransition);
            if(Frame->GetBoolField(TEXT("active"))) Machine->Update_AnyThread(Frame->GetBoolField(TEXT("contextActive")) ? C : C.AsInactive());
            FProxyAccess::Sync(MP,Delta);
            R->SetNumberField(TEXT("state"),Machine->GetCurrentState());R->SetNumberField(TEXT("elapsed"),Machine->GetCurrentStateElapsedTime());R->SetArrayField(TEXT("active"),FMachineAccess::Active(*Machine));
            R->SetArrayField(TEXT("updates"),Updates);R->SetArrayField(TEXT("requests"),RequestRows);R->SetObjectField(TEXT("fieldsAfter"),Fields(Main));
            TArray<TSharedPtr<FJsonValue>> Weights,Initializations;for(int32 S=0;S<12;++S){Weights.Add(MakeShared<FJsonValueNumber>(Machine->GetStateWeight(S)));Initializations.Add(MakeShared<FJsonValueNumber>(Taps[S].Initializations-Counts[S]));}
            R->SetArrayField(TEXT("weights"),Weights);R->SetArrayField(TEXT("initializations"),Initializations);Frames.Add(MakeShared<FJsonValueObject>(R));
        }
        auto T=MakeShared<FJsonObject>();T->SetStringField(TEXT("profile"),Trace->GetStringField(TEXT("profile")));T->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz")));T->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(T));Component->UnregisterComponent();
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Json;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}

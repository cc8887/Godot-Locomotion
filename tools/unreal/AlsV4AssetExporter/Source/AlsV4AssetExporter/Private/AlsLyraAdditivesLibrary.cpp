#include "AlsLyraAdditivesLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_StateResult.h"
#include "Animation/AnimNode_TransitionResult.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "AnimNodes/AnimNode_TwoWayBlend.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "Animation/AnimSync.h"
#include "Animation/AnimSyncScope.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
namespace LyraCycleProbe { bool Set(UObject*, const TCHAR*, const TSharedPtr<FJsonValue>&); bool Bind(UAnimInstance*,const TSharedPtr<FJsonObject>&,const TMap<FString,UAnimSequence*>*); }
namespace LyraAirProbe { TSharedPtr<FJsonObject> Source(FAnimNode_AssetPlayerBase*,const TMap<const UAnimSequence*,FString>&); }
namespace LyraAdditivesProbe
{
FString Fail(int32 Line) { UE_LOG(LogTemp,Error,TEXT("LYRA_ADDITIVES_CAPTURE_FAILED line=%d"),Line); return {}; }
double Number(UObject* Object,const TCHAR* Name)
{auto* P=FindFProperty<FNumericProperty>(Object->GetClass(),Name);check(P&&P->IsFloatingPoint());return P->GetFloatingPointPropertyValue(P->ContainerPtrToValuePtr<void>(Object));}
bool JumpFall(UAnimInstance* Layer,double Delta)
{
    auto* Function=Layer->FindFunction(TEXT("UpdateJumpFallData"));if(!Function)return false;
    TArray<uint8> Params;Params.SetNumZeroed(Function->ParmsSize);
    auto* P=FindFProperty<FNumericProperty>(Function,TEXT("DeltaTime"));if(!P||!P->IsFloatingPoint())return false;
    P->SetFloatingPointPropertyValue(P->ContainerPtrToValuePtr<void>(Params.GetData()),Delta);Layer->ProcessEvent(Function,Params.GetData());return true;
}
struct FInstanceAccess : UAnimInstance
{ static FAnimInstanceProxy& Proxy(UAnimInstance* A) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A); } };
struct FSyncMember{using Type=UE::Anim::FAnimSync FAnimInstanceProxy::*;friend Type NativeSync(FSyncMember);};
template<class Tag,typename Tag::Type Member>struct TOracleMember{friend typename Tag::Type NativeSync(Tag){return Member;}};
template struct TOracleMember<FSyncMember,&FAnimInstanceProxy::Sync>;
struct FProxyAccess : FAnimInstanceProxy
{
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S)
    {
        (P.*&FProxyAccess::InitializeObjects)(A);
        TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add((FBoneIndexType)I);
        P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*S);
        P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);
        (P.*&FProxyAccess::CachedBonesCounter).Increment();
    }
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D)
    { P.FlipBufferWriteIndex();(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D); }
    static void Tick(FAnimInstanceProxy& P,float D){(P.*NativeSync(FSyncMember{})).TickAssetPlayerInstances(P,D);}
};
struct FMachineAccess : FAnimNode_StateMachine
{
    static TSharedPtr<FJsonObject> Read(const FAnimNode_StateMachine& M,FAnimInstanceProxy& P)
    {
        auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("state"),M.GetCurrentState());R->SetNumberField(TEXT("elapsed"),M.GetCurrentStateElapsedTime());
        TArray<TSharedPtr<FJsonValue>> Weights,Previous,Active;
        for(int32 S=0;S<3;++S){Weights.Add(MakeShared<FJsonValueNumber>(M.GetStateWeight(S)));Previous.Add(MakeShared<FJsonValueNumber>(P.GetRecordedStateWeight(0,S)));}
        for(const auto& T:M.*&FMachineAccess::ActiveTransitionArray)
        {auto A=MakeShared<FJsonObject>();A->SetNumberField(TEXT("previous"),T.PreviousState);A->SetNumberField(TEXT("next"),T.NextState);A->SetNumberField(TEXT("elapsed"),T.ElapsedTime);A->SetNumberField(TEXT("duration"),T.CrossfadeDuration);A->SetNumberField(TEXT("alpha"),T.Alpha);Active.Add(MakeShared<FJsonValueObject>(A));}
        R->SetArrayField(TEXT("weights"),Weights);R->SetArrayField(TEXT("previousWeights"),Previous);R->SetArrayField(TEXT("active"),Active);return R;
    }
};
struct FTap : FAnimNode_Base
{
    FPoseLink Child;int32 State=0,Initializations=0;TArray<TSharedPtr<FJsonValue>>* Updates=nullptr;
    void Initialize_AnyThread(const FAnimationInitializeContext& C)override {++Initializations;Child.Initialize(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C)override {Child.CacheBones(C);}
    void Update_AnyThread(const FAnimationUpdateContext& C)override
    {auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("state"),State);R->SetNumberField(TEXT("weight"),C.GetFinalBlendWeight());R->SetBoolField(TEXT("active"),C.IsActive());Updates->Add(MakeShared<FJsonValueObject>(R));Child.Update(C);}
    void Evaluate_AnyThread(FPoseContext& C)override {Child.Evaluate(C);}
};
}
FString UAlsLyraAdditivesLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraAdditivesProbe;
    TSharedPtr<FJsonObject> Input;
    if(!MainClass||!Mesh||!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
    const auto& Names=Input->GetArrayField(TEXT("sequencePaths"));if(Names.Num()!=Sequences.Num())return Fail(__LINE__);
    TMap<FString,UAnimSequence*> Assets;TMap<const UAnimSequence*,FString> Paths;
    for(int32 I=0;I<Sequences.Num();++I){auto* S=Sequences[I];if(!S||S->GetSkeleton()!=Skeleton||!S->IsValidAdditive())return Fail(__LINE__);S->WaitOnExistingCompression(true);Assets.Add(Names[I]->AsString(),S);Paths.Add(S,Names[I]->AsString());}
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Input->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());auto T=TV->AsObject();auto* LC=LoadObject<UClass>(nullptr,*T->GetStringField(TEXT("class")));auto* LI=LC?IAnimClassInterface::GetFromClass(LC):nullptr;
        if(!LI||LI->GetBakedStateMachines().Num()!=4)return Fail(__LINE__);
        const auto& Definition=LI->GetBakedStateMachines()[0];
        const auto* Function=IAnimClassInterface::FindAnimBlueprintFunction(LI,TEXT("FullBodyAdditives"));
        if(!Function||Definition.InitialState!=0||Definition.States.Num()!=3||Definition.Transitions.Num()!=4)return Fail(__LINE__);
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* Owner=W->SpawnActor<AActor>(Spawn);if(!Owner)return Fail(__LINE__);
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MainClass);Owner->SetRootComponent(C.Get());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();
        auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);Main->LinkAnimClassLayers(LC);auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Layer||Layer->GetClass()!=LC||!LyraCycleProbe::Bind(Layer,T->GetObjectField(TEXT("bindings")),&Assets))return Fail(__LINE__);
        auto& MP=FInstanceAccess::Proxy(Main);auto& LP=FInstanceAccess::Proxy(Layer);Carrier->SetSkeleton(Skeleton);FProxyAccess::Setup(MP,Main,Skeleton);FProxyAccess::Setup(LP,Layer,Skeleton);
        if(MP.GetSkeleton()!=Skeleton||LP.GetSkeleton()!=Skeleton)return Fail(__LINE__);
        auto* Machine=LP.GetMutableNodeFromIndex<FAnimNode_StateMachine>(1);if(!Machine||Machine->StateMachineIndexInClass!=0)return Fail(__LINE__);Machine->CacheMachineDescription(const_cast<IAnimClassInterface*>(LI));
        const auto& Properties=LI->GetAnimNodeProperties();if(Properties.Num()!=118||Properties.IndexOfByKey(Function->OutputPoseNodeProperty)!=105)return Fail(__LINE__);
        FPoseLink Root;Root.SetLinkNode(Function->OutputPoseNodeProperty->ContainerPtrToValuePtr<FAnimNode_Base>(Layer));
        auto* Player=LP.GetMutableNodeFromIndex<FAnimNode_SequencePlayer>(5);auto* Blend=LP.GetMutableNodeFromIndex<FAnimNode_TwoWayBlend>(6);if(!Player||!Blend)return Fail(__LINE__);
        FTap Taps[3];FAnimNode_StateResult* States[3]={};FPoseLink Originals[3];TArray<TSharedPtr<FJsonValue>> Updates;
        struct FRestore{FAnimNode_StateResult** States;FPoseLink* Originals;~FRestore(){for(int32 I=0;I<3;++I)if(States[I])States[I]->Result=Originals[I];}}Restore{States,Originals};
        for(int32 S=0;S<3;++S){States[S]=LP.GetMutableNodeFromIndex<FAnimNode_StateResult>(Definition.States[S].StateRootNodeIndex);if(!States[S])return Fail(__LINE__);Originals[S]=States[S]->Result;Taps[S].Child=Originals[S];Taps[S].State=S;Taps[S].Updates=&Updates;States[S]->Result.SetLinkNode(&Taps[S]);}
        bool Initialized=false;int32 FrameIndex=0;TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark FrameMark(FMemStack::Get());auto F=FV->AsObject();const float D=F->GetNumberField(TEXT("delta"));
            if(!LyraCycleProbe::Set(Main,TEXT("IsOnGround"),F->GetField<EJson::Boolean>(TEXT("ground"))))return Fail(__LINE__);
            for(const TCHAR* Name:{TEXT("IsFalling"),TEXT("IsJumping"),TEXT("IsCrouching")})
                if(!LyraCycleProbe::Set(Main,Name,F->GetField<EJson::Boolean>(Name)))return Fail(__LINE__);
            FProxyAccess::Pre(MP,Main,D);FProxyAccess::Pre(LP,Layer,D);
            LI->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& S){if(S.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct()){FAnimSubsystemUpdateContext G(S,Layer,D);S.Subsystem.OnPreUpdate_GameThread(G);S.Subsystem.OnPostUpdate_GameThread(G);FAnimSubsystemParallelUpdateContext P(S,LP,D);S.Subsystem.OnPreUpdate_WorkerThread(P);}return EAnimSubsystemEnumeration::Continue;});
            const double BeforeFalling=Number(Layer,TEXT("TimeFalling"));if(!JumpFall(Layer,D))return Fail(__LINE__);
            int32 Counts[3];for(int32 S=0;S<3;++S)Counts[S]=Taps[S].Initializations;
            if(!Initialized||F->GetBoolField(TEXT("initialize"))){Root.Initialize(FAnimationInitializeContext(&LP));Root.CacheBones(FAnimationCacheBonesContext(&LP));Initialized=true;}
            auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("before"),FMachineAccess::Read(*Machine,LP));
            R->SetNumberField(TEXT("timeFallingBefore"),BeforeFalling);R->SetNumberField(TEXT("timeFalling"),Number(Layer,TEXT("TimeFalling")));
            R->SetObjectField(TEXT("sourceBefore"),LyraAirProbe::Source(Player,Paths));R->SetNumberField(TEXT("landAlphaBefore"),Number(Layer,TEXT("LandRecoveryAlpha")));
            FAnimationUpdateSharedContext Shared;auto Context=FAnimationUpdateContext(&LP,D,&Shared).FractionalWeight(F->GetNumberField(TEXT("weight")));if(!F->GetBoolField(TEXT("active")))Context=Context.AsInactive();
            FAnimationUpdateContext MainContext(&MP,D,&Shared);UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> SyncScope(Context,MainContext);
            TArray<TSharedPtr<FJsonValue>> Rules;
            for(const auto& State:Definition.States)for(const auto& Exit:State.Transitions)if(!Exit.bAutomaticRemainingTimeRule)
            {auto* Node=LP.GetMutableNodeFromIndex<FAnimNode_TransitionResult>(Exit.CanTakeDelegateIndex);if(!Node)return Fail(__LINE__);Node->GetEvaluateGraphExposedInputs().Execute(Context);auto Q=MakeShared<FJsonObject>();Q->SetNumberField(TEXT("edge"),Exit.TransitionIndex);Q->SetNumberField(TEXT("delegate"),Exit.CanTakeDelegateIndex);Q->SetBoolField(TEXT("result"),Node->bCanEnterTransition);Rules.Add(MakeShared<FJsonValueObject>(Q));}
            R->SetArrayField(TEXT("rules"),Rules);Updates.Reset();if(F->GetBoolField(TEXT("visited")))Root.Update(Context);
            FProxyAccess::Tick(MP,D);
            if(FrameIndex<3)UE_LOG(LogTemp,Display,TEXT("LYRA_ADDITIVES_INITIAL_OBSERVATION frame=%d state=%d airToLand=%d"),FrameIndex,Machine->GetCurrentState(),Rules[1]->AsObject()->GetBoolField(TEXT("result")));
            R->SetObjectField(TEXT("after"),FMachineAccess::Read(*Machine,LP));R->SetArrayField(TEXT("updates"),Updates);
            R->SetObjectField(TEXT("source"),LyraAirProbe::Source(Player,Paths));R->SetNumberField(TEXT("landAlpha"),Number(Layer,TEXT("LandRecoveryAlpha")));R->SetNumberField(TEXT("blendAlpha"),Blend->Alpha);
            TArray<TSharedPtr<FJsonValue>> IR;for(int32 S=0;S<3;++S)IR.Add(MakeShared<FJsonValueNumber>(Taps[S].Initializations-Counts[S]));R->SetArrayField(TEXT("initializations"),IR);
            if(F->GetBoolField(TEXT("visited"))&&F->GetBoolField(TEXT("evaluate")))
            {FPoseContext Pose(&LP,true);Root.Evaluate(Pose);auto Output=LyraCyclePoseProbe::PoseData(Pose.Pose,Pose.Curve,Pose.CustomAttributes,Skeleton->GetReferenceSkeleton());if(!Output)return Fail(__LINE__);R->SetObjectField(TEXT("output"),Output);}
            Frames.Add(MakeShared<FJsonValueObject>(R));
            ++FrameIndex;
        }
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));R->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}

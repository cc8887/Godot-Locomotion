#include "AlsLyraIdleLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_StateResult.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "Animation/AnimNode_Inertialization.h"
#include "Animation/AnimInertializationSyncScope.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimSync.h"
#include "Animation/AnimSyncScope.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "Animation/Skeleton.h"
#include "Animation/BlendProfile.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "UObject/StrongObjectPtr.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace LyraCycleProbe
{bool Set(UObject*,const TCHAR*,const TSharedPtr<FJsonValue>&);bool Bind(UAnimInstance*,const TSharedPtr<FJsonObject>&,const TMap<FString,UAnimSequence*>*);int32 LayerRoot(const IAnimClassInterface*,FName);}
namespace LyraAirProbe
{TSharedPtr<FJsonObject> Source(FAnimNode_AssetPlayerBase*,const TMap<const UAnimSequence*,FString>&);}
namespace LyraIdleProbe
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_IDLE_CAPTURE_FAILED line=%d"),Line);return {};}
struct FInstanceAccess:UAnimInstance
{
 static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}
 static void TeardownStoppedMontages(UAnimInstance* A)
 {(A->*&FInstanceAccess::Montage_Advance)(0.f);(A->*&FInstanceAccess::DispatchQueuedAnimEvents)();}
};
struct FSyncMember{using Type=UE::Anim::FAnimSync FAnimInstanceProxy::*;friend Type NativeSync(FSyncMember);};
template<class Tag,typename Tag::Type Member>struct TOracleMember{friend typename Tag::Type NativeSync(Tag){return Member;}};
template struct TOracleMember<FSyncMember,&FAnimInstanceProxy::Sync>;
struct FProxyAccess:FAnimInstanceProxy
{
 static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D){P.FlipBufferWriteIndex();(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
 static void Tick(FAnimInstanceProxy& P,float D){(P.*NativeSync(FSyncMember{})).TickAssetPlayerInstances(P,D);}
 static void Publish(FAnimInstanceProxy& P,const TSharedPtr<FJsonObject>& Output)
 {using FRead=TMap<FName,float>&(FAnimInstanceProxy::*)(EAnimCurveType);auto& Curves=(P.*static_cast<FRead>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve);Curves.Reset();for(const auto& E:Output->GetObjectField(TEXT("curves"))->Values)Curves.Add(FName(*E.Key),E.Value->AsObject()->GetNumberField(TEXT("value")));}
};
struct FSourceAccess:FAnimNode_AssetPlayerBase
{static FMarkerTickRecord& Marker(FAnimNode_AssetPlayerBase& N){return N.*&FSourceAccess::MarkerTickRecord;}};
struct FMachineAccess:FAnimNode_StateMachine
{
 static TSharedPtr<FJsonObject> Read(const FAnimNode_StateMachine& M,FAnimInstanceProxy& P,int32 Count)
 {
  auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("state"),M.GetCurrentState());R->SetNumberField(TEXT("elapsed"),M.GetCurrentStateElapsedTime());
  TArray<TSharedPtr<FJsonValue>> Weights,Previous,Active;
  for(int32 S=0;S<Count;++S){Weights.Add(MakeShared<FJsonValueNumber>(M.GetStateWeight(S)));Previous.Add(MakeShared<FJsonValueNumber>(P.GetRecordedStateWeight(M.StateMachineIndexInClass,S)));}
  for(const auto& T:M.*&FMachineAccess::ActiveTransitionArray)
  {auto A=MakeShared<FJsonObject>();A->SetNumberField(TEXT("previous"),T.PreviousState);A->SetNumberField(TEXT("next"),T.NextState);A->SetNumberField(TEXT("duration"),T.CrossfadeDuration);A->SetNumberField(TEXT("elapsed"),T.ElapsedTime);A->SetNumberField(TEXT("alpha"),T.Alpha);TArray<TSharedPtr<FJsonValue>> Path;for(int32 I:T.SourceTransitionIndices)Path.Add(MakeShared<FJsonValueNumber>(I));A->SetArrayField(TEXT("path"),Path);Active.Add(MakeShared<FJsonValueObject>(A));}
  R->SetArrayField(TEXT("weights"),Weights);R->SetArrayField(TEXT("previousWeights"),Previous);R->SetArrayField(TEXT("active"),Active);return R;
 }
};
struct FTap:FAnimNode_Base
{
 FPoseLink Child;int32 Machine=0,State=0,Initializations=0;TArray<TSharedPtr<FJsonValue>>* Updates=nullptr;
 void Initialize_AnyThread(const FAnimationInitializeContext& C)override{++Initializations;Child.Initialize(C);}
 void CacheBones_AnyThread(const FAnimationCacheBonesContext& C)override{Child.CacheBones(C);}
 void Update_AnyThread(const FAnimationUpdateContext& C)override
 {auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("machine"),Machine);R->SetNumberField(TEXT("state"),State);R->SetNumberField(TEXT("weight"),C.GetFinalBlendWeight());R->SetBoolField(TEXT("active"),C.IsActive());R->SetBoolField(TEXT("inertial"),C.GetMessage<UE::Anim::FAnimInertializationSyncScope>()!=nullptr);Updates->Add(MakeShared<FJsonValueObject>(R));Child.Update(C);}
 void Evaluate_AnyThread(FPoseContext& C)override{Child.Evaluate(C);}
};
struct FRequests:UE::Anim::IInertializationRequester
{
 TArray<TSharedPtr<FJsonValue>>& Rows;explicit FRequests(TArray<TSharedPtr<FJsonValue>>& R):Rows(R){}
 void RequestInertialization(float D,const UBlendProfile*)override{auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("duration"),D);R->SetBoolField(TEXT("useBlendMode"),false);Rows.Add(MakeShared<FJsonValueObject>(R));}
 void RequestInertialization(const FInertializationRequest& Q)override{auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("duration"),Q.Duration);R->SetBoolField(TEXT("useBlendMode"),Q.bUseBlendMode);R->SetNumberField(TEXT("blendMode"),(int32)Q.BlendMode);R->SetStringField(TEXT("profile"),Q.BlendProfile?Q.BlendProfile->GetPathName():TEXT(""));Rows.Add(MakeShared<FJsonValueObject>(R));}
 void AddDebugRecord(const FAnimInstanceProxy&,int32)override{}FName GetTag()const override{return TEXT("LyraIdleProbe");}
};
TSharedPtr<FJsonObject> Fields(UAnimInstance* A)
{
 auto R=MakeShared<FJsonObject>();
 for(const TCHAR* N:{TEXT("IdleBreakDelayTime"),TEXT("TimeUntilNextIdleBreak"),TEXT("CurrentIdleBreakIndex"),TEXT("TurnInPlaceRotationDirection"),TEXT("TurnInPlaceRecoveryDirection"),TEXT("TurnInPlaceAnimTime")})
 {auto* P=FindFProperty<FNumericProperty>(A->GetClass(),N);if(!P)return {};const void* V=P->ContainerPtrToValuePtr<void>(A);
  if(P->IsFloatingPoint()){const double Value=P->GetFloatingPointPropertyValue(V);uint64 Bits;FMemory::Memcpy(&Bits,&Value,sizeof(Bits));R->SetNumberField(N,Value);R->SetStringField(FString(N)+TEXT("Bits"),FString::Printf(TEXT("%016llx"),static_cast<unsigned long long>(Bits)));}
  else R->SetNumberField(N,P->GetSignedIntPropertyValue(V));}return R;
}
}
FString UAlsLyraIdleLibrary::ReadIdleTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
 using namespace LyraIdleProbe;using namespace LyraCycleProbe;
 TSharedPtr<FJsonObject> Input;if(!MainClass||!Mesh||!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
 const auto& Names=Input->GetArrayField(TEXT("sequencePaths"));if(Names.Num()!=Sequences.Num())return Fail(__LINE__);TMap<FString,UAnimSequence*> Assets;TMap<const UAnimSequence*,FString> Paths;
 for(int32 I=0;I<Sequences.Num();++I){auto* S=Sequences[I];if(!S||S->GetSkeleton()!=Skeleton||S->IsValidAdditive())return Fail(__LINE__);S->WaitOnExistingCompression(true);Assets.Add(Names[I]->AsString(),S);Paths.Add(S,Names[I]->AsString());}
 TArray<TSharedPtr<FJsonValue>> Traces;
 for(const auto& TV:Input->GetArrayField(TEXT("traces")))
 {
  FMemMark Mark(FMemStack::Get());auto Trace=TV->AsObject();auto* LC=LoadObject<UClass>(nullptr,*Trace->GetStringField(TEXT("class")));auto* LI=LC?IAnimClassInterface::GetFromClass(LC):nullptr;if(!LI)return Fail(__LINE__);
  const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
  TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* Owner=W->SpawnActor<AActor>(Spawn);if(!Owner)return Fail(__LINE__);
  TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
  TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MainClass);Owner->SetRootComponent(C.Get());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);Main->LinkAnimClassLayers(LC);auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Layer||Layer->GetClass()!=LC||!Bind(Layer,Trace->GetObjectField(TEXT("bindings")),&Assets))return Fail(__LINE__);
  auto* Array=FindFProperty<FArrayProperty>(LC,TEXT("Idle_Breaks"));auto* Inner=Array?CastField<FObjectPropertyBase>(Array->Inner):nullptr;if(!Inner)return Fail(__LINE__);FScriptArrayHelper Breaks(Array,Array->ContainerPtrToValuePtr<void>(Layer));const auto& Bs=Trace->GetArrayField(TEXT("breaks"));Breaks.Resize(Bs.Num());for(int32 I=0;I<Bs.Num();++I){auto* S=Assets.FindRef(Bs[I]->AsString());if(!S)return Fail(__LINE__);Inner->SetObjectPropertyValue(Breaks.GetRawPtr(I),S);}
  auto& MP=FInstanceAccess::Proxy(Main);auto& LP=FInstanceAccess::Proxy(Layer);auto* Idle=LP.GetMutableNodeFromIndex<FAnimNode_StateMachine>(13);auto* Stance=LP.GetMutableNodeFromIndex<FAnimNode_StateMachine>(15);if(!Idle||!Stance||Idle->StateMachineIndexInClass!=1||Stance->StateMachineIndexInClass!=2)return Fail(__LINE__);Idle->CacheMachineDescription(const_cast<IAnimClassInterface*>(LI));Stance->CacheMachineDescription(const_cast<IAnimClassInterface*>(LI));
  FPoseLink Link;Link.SetLinkNode(LP.GetMutableNodeFromIndex<FAnimNode_Base>(LayerRoot(LI,TEXT("FullBody_IdleState"))));
  TArray<FTap> Taps;Taps.SetNum(6);TArray<FAnimNode_StateResult*> TapRoots;TArray<FPoseLink> Originals;TArray<TSharedPtr<FJsonValue>> Updates;
  struct FRestore{TArray<FAnimNode_StateResult*>& R;TArray<FPoseLink>& O;~FRestore(){for(int32 I=0;I<R.Num();++I)R[I]->Result=O[I];}}Restore{TapRoots,Originals};
  const int32 RootIndices[]={14,23,25,27,16,18};for(int32 I=0;I<6;++I){auto* R=LP.GetMutableNodeFromIndex<FAnimNode_StateResult>(RootIndices[I]);if(!R)return Fail(__LINE__);TapRoots.Add(R);Originals.Add(R->Result);Taps[I].Child=R->Result;Taps[I].Machine=I<4?0:1;Taps[I].State=I<4?I:I-4;Taps[I].Updates=&Updates;R->Result.SetLinkNode(&Taps[I]);}
  TArray<FAnimNode_AssetPlayerBase*> Sources;const int32 SourceIndices[]={17,19,24,26,28};for(int32 I:SourceIndices){const auto& Props=LI->GetAnimNodeProperties();auto* S=Props[Props.Num()-1-I]->ContainerPtrToValuePtr<FAnimNode_AssetPlayerBase>(Layer);Sources.Add(S);auto& M=FSourceAccess::Marker(*S);M.PreviousMarker.TimeToMarker=0;M.NextMarker.TimeToMarker=0;}
  Carrier->SetSkeleton(Skeleton);TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add((FBoneIndexType)I);LP.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*Skeleton);LP.GetRequiredBones().SetUseRAWData(true);LP.GetRequiredBones().SetDisableRetargeting(false);
  bool Initialized=false;TArray<TSharedPtr<FJsonValue>> Frames;
  for(const auto& FV:Trace->GetArrayField(TEXT("frames")))
  {
   FMemMark FrameMark(FMemStack::Get());auto F=FV->AsObject();const float Delta=F->GetNumberField(TEXT("delta"));for(const auto& E:F->GetObjectField(TEXT("main"))->Values)if(!Set(Main,*E.Key,E.Value))return Fail(__LINE__);
   // Property Access calls UAnimInstance::IsAnyMontagePlaying; it is not a
   // reflected boolean. Supply real activity without advancing a playing clip.
   const bool Montage=F->GetBoolField(TEXT("montage"));
   if(Montage&&!Main->IsAnyMontagePlaying())
   {auto* S=LoadObject<UAnimSequence>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Unarmed/MM_Unarmed_Jog_Fwd.MM_Unarmed_Jog_Fwd"));if(!S||!Main->PlaySlotAnimationAsDynamicMontage(S,TEXT("DefaultSlot"),0,0,1,100))return Fail(__LINE__);}
   else if(!Montage&&Main->IsAnyMontagePlaying()){Main->StopAllMontages(0);FInstanceAccess::TeardownStoppedMontages(Main);}
   if(Main->IsAnyMontagePlaying()!=Montage)return Fail(__LINE__);
   auto* Loc=FindFProperty<FStructProperty>(MainClass,TEXT("WorldLocation"));if(!Loc||Loc->Struct!=TBaseStructure<FVector>::Get())return Fail(__LINE__);const auto& V=F->GetArrayField(TEXT("location"));*Loc->ContainerPtrToValuePtr<FVector>(Main)=FVector(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber());
   FProxyAccess::Pre(MP,Main,Delta);FProxyAccess::Pre(LP,Layer,Delta);LI->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& S){if(S.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct()){FAnimSubsystemUpdateContext G(S,Layer,Delta);S.Subsystem.OnPreUpdate_GameThread(G);S.Subsystem.OnPostUpdate_GameThread(G);FAnimSubsystemParallelUpdateContext P(S,LP,Delta);S.Subsystem.OnPreUpdate_WorkerThread(P);}return EAnimSubsystemEnumeration::Continue;});
   int32 Counts[6];for(int32 I=0;I<6;++I)Counts[I]=Taps[I].Initializations;if(!Initialized||F->GetBoolField(TEXT("initialize"))){Link.Initialize(FAnimationInitializeContext(&LP));Link.CacheBones(FAnimationCacheBonesContext(&LP));Initialized=true;}
   auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("beforeFields"),Fields(Layer));Row->SetObjectField(TEXT("beforeIdle"),FMachineAccess::Read(*Idle,LP,4));Row->SetObjectField(TEXT("beforeStance"),FMachineAccess::Read(*Stance,LP,2));Row->SetNumberField(TEXT("turnYawBefore"),Layer->GetCurveValue(TEXT("TurnYawWeight")));
   Updates.Reset();TArray<TSharedPtr<FJsonValue>> Requests;FAnimationUpdateSharedContext Shared;FAnimationUpdateContext Context(&LP,Delta,&Shared),RootContext(&MP,Delta,&Shared);auto Child=Context.FractionalWeight(F->GetNumberField(TEXT("weight")));UE::Anim::TScopedGraphMessage<FRequests> RequestScope(Child,Requests);UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> SyncScope(Child,RootContext);
   if(F->GetBoolField(TEXT("visited")))Link.Update(F->GetBoolField(TEXT("active"))?Child:Child.AsInactive());FProxyAccess::Tick(MP,Delta);
   Row->SetObjectField(TEXT("fields"),Fields(Layer));Row->SetObjectField(TEXT("idle"),FMachineAccess::Read(*Idle,LP,4));Row->SetObjectField(TEXT("stance"),FMachineAccess::Read(*Stance,LP,2));Row->SetArrayField(TEXT("updates"),Updates);Row->SetArrayField(TEXT("requests"),Requests);
   TArray<TSharedPtr<FJsonValue>> SR,IR;for(auto* S:Sources)SR.Add(MakeShared<FJsonValueObject>(LyraAirProbe::Source(S,Paths)));for(int32 I=0;I<6;++I)IR.Add(MakeShared<FJsonValueNumber>(Taps[I].Initializations-Counts[I]));Row->SetArrayField(TEXT("sources"),SR);Row->SetArrayField(TEXT("initializations"),IR);
   if(F->GetBoolField(TEXT("visited"))&&F->GetBoolField(TEXT("evaluate")))
   {FPoseContext Pose(&LP);Link.Evaluate(Pose);auto Output=LyraCyclePoseProbe::PoseData(Pose.Pose,Pose.Curve,Pose.CustomAttributes,Skeleton->GetReferenceSkeleton());Row->SetObjectField(TEXT("output"),Output);FProxyAccess::Publish(LP,Output);}
   Frames.Add(MakeShared<FJsonValueObject>(Row));
  }
  auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("profile"),Trace->GetStringField(TEXT("profile")));R->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz")));R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));C->UnregisterComponent();
 }
 auto Out=MakeShared<FJsonObject>();Out->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Out,TJsonWriterFactory<>::Create(&Text));return Text;
}

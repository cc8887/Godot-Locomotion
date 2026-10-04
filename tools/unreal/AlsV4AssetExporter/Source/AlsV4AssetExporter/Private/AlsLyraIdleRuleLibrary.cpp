#include "AlsLyraIdleRuleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_TransitionResult.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "Animation/AnimSequence.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "UObject/StrongObjectPtr.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
namespace LyraCycleProbe {bool Set(UObject*,const TCHAR*,const TSharedPtr<FJsonValue>&);}
namespace LyraIdleRuleProbe
{
struct FAccess:UAnimInstance
{
 static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}
 static void Stop(UAnimInstance* A){A->StopAllMontages(0);(A->*&FAccess::Montage_Advance)(0);(A->*&FAccess::DispatchQueuedAnimEvents)();}
};
}
FString UAlsLyraIdleRuleLibrary::ReadIdleRules(UClass* MainClass,USkeletalMesh* Mesh,const FString& RequestsJson)
{
 using namespace LyraIdleRuleProbe;
 TSharedPtr<FJsonObject> Input;if(!MainClass||!Mesh||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return {};
 TArray<TSharedPtr<FJsonValue>> Traces;
 for(const auto& TV:Input->GetArrayField(TEXT("traces")))
 {
  auto T=TV->AsObject();auto* Class=LoadObject<UClass>(nullptr,*T->GetStringField(TEXT("class")));auto* LI=Class?IAnimClassInterface::GetFromClass(Class):nullptr;if(!LI)return {};
  const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
  TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return {};
  struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
  auto* Owner=W->SpawnActor<AActor>(Spawn);if(!Owner)return {};
  TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
  C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMesh(Mesh);C->SetAnimInstanceClass(MainClass);
  Owner->SetRootComponent(C.Get());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();auto* Main=C->GetAnimInstance();if(!Main)return {};
  Main->LinkAnimClassLayers(Class);auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Layer||Layer->GetClass()!=Class)return {};
  auto& LP=FAccess::Proxy(Layer);TArray<TSharedPtr<FJsonValue>> Rows;
  for(const auto& RV:T->GetArrayField(TEXT("rows")))
  {
   auto R=RV->AsObject();for(const auto& E:R->GetObjectField(TEXT("main"))->Values)if(!LyraCycleProbe::Set(Main,*E.Key,E.Value))return {};
   const bool Montage=R->GetBoolField(TEXT("montage"));
   if(Montage&&!Main->IsAnyMontagePlaying())
   {auto* S=LoadObject<UAnimSequence>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Unarmed/MM_Unarmed_Jog_Fwd.MM_Unarmed_Jog_Fwd"));if(!S||!Main->PlaySlotAnimationAsDynamicMontage(S,TEXT("DefaultSlot"),0,0,1,100))return {};}
   else if(!Montage&&Main->IsAnyMontagePlaying())FAccess::Stop(Main);
   if(Main->IsAnyMontagePlaying()!=Montage)return {};
   LI->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& S){if(S.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct())
    {FAnimSubsystemUpdateContext G(S,Layer,0);S.Subsystem.OnPreUpdate_GameThread(G);S.Subsystem.OnPostUpdate_GameThread(G);FAnimSubsystemParallelUpdateContext P(S,LP,0);S.Subsystem.OnPreUpdate_WorkerThread(P);}return EAnimSubsystemEnumeration::Continue;});
   FAnimationUpdateSharedContext Shared;FAnimationUpdateContext Context(&LP,0,&Shared);TArray<TSharedPtr<FJsonValue>> Rules;
   for(int32 Machine:{1,2})
   for(const auto& State:LI->GetBakedStateMachines()[Machine].States)
   for(const auto& Exit:State.Transitions)
   {
    if(Exit.bAutomaticRemainingTimeRule)continue;auto* Node=LP.GetMutableNodeFromIndex<FAnimNode_TransitionResult>(Exit.CanTakeDelegateIndex);if(!Node)return {};
    Node->GetEvaluateGraphExposedInputs().Execute(Context);auto Rule=MakeShared<FJsonObject>();Rule->SetNumberField(TEXT("machine"),Machine);Rule->SetNumberField(TEXT("edge"),Exit.TransitionIndex);
    Rule->SetNumberField(TEXT("delegate"),Exit.CanTakeDelegateIndex);Rule->SetBoolField(TEXT("result"),Node->bCanEnterTransition);Rules.Add(MakeShared<FJsonValueObject>(Rule));
   }
   auto Row=MakeShared<FJsonObject>();Row->SetArrayField(TEXT("rules"),Rules);Rows.Add(MakeShared<FJsonValueObject>(Row));
  }
  auto Out=MakeShared<FJsonObject>();Out->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));Out->SetArrayField(TEXT("rows"),Rows);Traces.Add(MakeShared<FJsonValueObject>(Out));C->UnregisterComponent();
 }
 auto Out=MakeShared<FJsonObject>();Out->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Out,TJsonWriterFactory<>::Create(&Text));return Text;
}

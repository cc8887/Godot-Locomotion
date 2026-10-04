#include "AlsLyraMainIdleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_StateResult.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "UObject/StrongObjectPtr.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
namespace LyraCycleProbe {bool Set(UObject*,const TCHAR*,const TSharedPtr<FJsonValue>&);}
namespace LyraMainIdleProbe
{
struct FAccess:UAnimInstance {static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
 static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D){(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
 static void Curves(FAnimInstanceProxy& P,float Remaining,float Weight)
 {using FRead=TMap<FName,float>&(FAnimInstanceProxy::*)(EAnimCurveType);auto& C=(P.*static_cast<FRead>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve);C.Reset();C.Add(TEXT("RemainingTurnYaw"),Remaining);C.Add(TEXT("TurnYawWeight"),Weight);}
};
struct FMachineAccess:FAnimNode_StateMachine {static void Current(FAnimNode_StateMachine& M,int32 S){M.*&FMachineAccess::CurrentState=S;}};
struct FEmpty:FAnimNode_Base {void Update_AnyThread(const FAnimationUpdateContext&)override{}};
TSharedPtr<FJsonObject> Fields(UAnimInstance* A)
{
 auto R=MakeShared<FJsonObject>();
 for(const TCHAR* N:{TEXT("RootYawOffset"),TEXT("AimYaw"),TEXT("TurnYawCurveValue")})
 {auto* P=FindFProperty<FDoubleProperty>(A->GetClass(),N);if(!P)return {};double V=P->GetPropertyValue_InContainer(A);uint64 B;FMemory::Memcpy(&B,&V,8);R->SetNumberField(N,V);R->SetStringField(FString(N)+TEXT("Bits"),FString::Printf(TEXT("%016llx"),static_cast<unsigned long long>(B)));}
 auto* M=FindFProperty<FByteProperty>(A->GetClass(),TEXT("RootYawOffsetMode"));if(!M)return {};R->SetNumberField(TEXT("mode"),M->GetPropertyValue_InContainer(A));return R;
}
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_MAIN_IDLE_CAPTURE_FAILED line=%d"),L);return {};}
}
FString UAlsLyraMainIdleLibrary::ReadMainIdleTrace(UClass* MainClass,USkeletalMesh* Mesh,const FString& RequestsJson)
{
 using namespace LyraMainIdleProbe;auto* I=MainClass?IAnimClassInterface::GetFromClass(MainClass):nullptr;
 TSharedPtr<FJsonObject> Input;if(!I||!Mesh||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
 const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
 TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
 struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* A=W->SpawnActor<AActor>(Spawn);if(!A)return Fail(__LINE__);
 TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(A,NAME_None,RF_Transient));C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMesh(Mesh);C->SetAnimInstanceClass(MainClass);A->SetRootComponent(C.Get());A->AddInstanceComponent(C.Get());C->RegisterComponent();auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);
 auto& P=FAccess::Proxy(Main);FAnimNode_StateMachine* Machine=nullptr;int32 Property=-1;
 for(int32 N=0;N<I->GetAnimNodeProperties().Num();++N)if(I->GetAnimNodeProperties()[N]->Struct==FAnimNode_StateMachine::StaticStruct()){Machine=I->GetAnimNodeProperties()[N]->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Main);Property=N;}
 if(!Machine||Machine->StateMachineIndexInClass!=0)return Fail(__LINE__);Machine->CacheMachineDescription(const_cast<IAnimClassInterface*>(I));
 const auto& Def=I->GetBakedStateMachines()[0];if(Def.States[0].StateName!=TEXT("Idle"))return Fail(__LINE__);
 auto* Root=P.GetMutableNodeFromIndex<FAnimNode_StateResult>(Def.States[0].StateRootNodeIndex);if(!Root)return Fail(__LINE__);
 FEmpty Empty;struct FRestore{FAnimNode_StateResult* R;FPoseLink Original;~FRestore(){R->Result=Original;}}Restore{Root,Root->Result};Root->Result.SetLinkNode(&Empty);
 FPoseLink Link;Link.LinkID=I->GetAnimNodeProperties().Num()-1-Def.States[0].StateRootNodeIndex;Link.SetLinkNode(Root);Link.Initialize(FAnimationInitializeContext(&P));
 TArray<TSharedPtr<FJsonValue>> Rows;
 for(const auto& Value:Input->GetArrayField(TEXT("frames")))
 {
  auto F=Value->AsObject();float D=F->GetNumberField(TEXT("delta"));FProxyAccess::Pre(P,Main,D);
  for(const auto& E:F->GetObjectField(TEXT("main"))->Values)if(!LyraCycleProbe::Set(Main,*E.Key,E.Value))return Fail(__LINE__);
  const float Weight=F->GetNumberField(TEXT("previousIdleWeight"));P.RecordStateWeight(0,0,Weight,0);P.FlipBufferWriteIndex();P.RecordStateWeight(0,0,Weight,0);
  FMachineAccess::Current(*Machine,F->GetIntegerField(TEXT("current")));FProxyAccess::Curves(P,F->GetNumberField(TEXT("remaining")),F->GetNumberField(TEXT("curveWeight")));
  if(Main->GetCurveValue(TEXT("RemainingTurnYaw"))!=static_cast<float>(F->GetNumberField(TEXT("remaining"))) || Main->GetCurveValue(TEXT("TurnYawWeight"))!=static_cast<float>(F->GetNumberField(TEXT("curveWeight"))))return Fail(__LINE__);
  auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("before"),Fields(Main));
  FAnimationUpdateSharedContext Shared;FAnimationUpdateContext Context(&P,D,&Shared);Context.SetNodeId(Property);
  if(F->GetBoolField(TEXT("visited")))Link.Update(Context.FractionalWeight(.7f));
  auto After=Fields(Main);if(!After)return Fail(__LINE__);R->SetObjectField(TEXT("after"),After);Rows.Add(MakeShared<FJsonValueObject>(R));
 }
 auto Out=MakeShared<FJsonObject>();Out->SetNumberField(TEXT("root"),Def.States[0].StateRootNodeIndex);Out->SetArrayField(TEXT("frames"),Rows);FString Text;FJsonSerializer::Serialize(Out,TJsonWriterFactory<>::Create(&Text));return Text;
}

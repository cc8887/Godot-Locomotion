#include "AlsLyraFootPlantBindingLibrary.h"
#include "AnimNode_ControlRig.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"
namespace LyraFootPlantBindingProbe
{
struct FAccess:UAnimInstance{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A){(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,1.f/60);}
    static void Curve(FAnimInstanceProxy& P,float V)
    {using FRead=TMap<FName,float>&(FAnimInstanceProxy::*)(EAnimCurveType);auto& C=(P.*static_cast<FRead>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve);C.Reset();C.Add(TEXT("DisableLegIK"),V);}
};
bool Set(UObject* O,const TCHAR* N,bool V)
{auto* P=FindFProperty<FBoolProperty>(O->GetClass(),N);if(!P)return false;P->SetPropertyValue_InContainer(O,V);return true;}
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_FOOTPLANT_BINDING_FAILED line=%d"),L);return {};}
}
FString UAlsLyraFootPlantBindingLibrary::ReadBindings(UClass* MainClass,USkeletalMesh* Mesh,const FString& RequestsJson)
{
    using namespace LyraFootPlantBindingProbe;TSharedPtr<FJsonObject> Input;if(!MainClass||!Mesh||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
    struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* A=W->SpawnActor<AActor>(Spawn);if(!A)return Fail(__LINE__);
    TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(A,NAME_None,RF_Transient));C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMesh(Mesh);C->SetAnimInstanceClass(MainClass);A->SetRootComponent(C.Get());A->AddInstanceComponent(C.Get());C->RegisterComponent();auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);
    auto& Proxy=FAccess::Proxy(Main);auto* Node=Proxy.GetMutableNodeFromIndex<FAnimNode_ControlRig>(73);auto* Enabled=FindFProperty<FBoolProperty>(FAnimNode_ControlRig::StaticStruct(),TEXT("bAlphaBoolEnabled"));if(!Node||!Enabled)return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(const auto& V:Input->GetArrayField(TEXT("frames")))
    {
        FProxyAccess::Pre(Proxy,Main);
        auto F=V->AsObject();if(!Set(Main,TEXT("EnableControlRig"),F->GetBoolField(TEXT("enableField")))||!Set(Main,TEXT("UseFootPlacement"),F->GetBoolField(TEXT("useFootPlacement")))||!Set(Main,TEXT("IsCrouching"),F->GetBoolField(TEXT("crouching")))||!Set(Main,TEXT("HasVelocity"),F->GetBoolField(TEXT("moving"))))return Fail(__LINE__);
        const float Curve=static_cast<float>(F->GetNumberField(TEXT("disableLegIK")));FProxyAccess::Curve(Proxy,Curve);if(Main->GetCurveValue(TEXT("DisableLegIK"))!=Curve)return Fail(__LINE__);
        // The exposed copy reads the compiler's function-result cache, which
        // belongs to PropertyAccess's game/worker batches. Refresh it first.
        IAnimClassInterface::GetFromClass(MainClass)->ForEachSubsystem(Main,[&](const FAnimSubsystemInstanceContext& S)
        {if(S.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct()){FAnimSubsystemUpdateContext G(S,Main,1.f/60);S.Subsystem.OnPreUpdate_GameThread(G);S.Subsystem.OnPostUpdate_GameThread(G);FAnimSubsystemParallelUpdateContext P(S,Proxy,1.f/60);S.Subsystem.OnPreUpdate_WorkerThread(P);S.Subsystem.OnPostUpdate_WorkerThread(P);}return EAnimSubsystemEnumeration::Continue;});
        Node->GetEvaluateGraphExposedInputs().Execute(FAnimationUpdateContext(&Proxy,1.f/60));auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("curve"),Main->GetCurveValue(TEXT("DisableLegIK")));R->SetBoolField(TEXT("enabled"),Enabled->GetPropertyValue_InContainer(Node));
        const TCHAR* Fields[]={TEXT("__CustomProperty_isCrouching_BB3E79F34D4AF55996D9D7BC4E44561C"),TEXT("__CustomProperty_isMoving2D_BB3E79F34D4AF55996D9D7BC4E44561C")};const TCHAR* Names[]={TEXT("crouching"),TEXT("moving")};for(int32 I=0;I<2;++I){auto* P=FindFProperty<FBoolProperty>(Main->GetClass(),Fields[I]);if(!P)return Fail(__LINE__);R->SetBoolField(Names[I],P->GetPropertyValue_InContainer(Main));}Rows.Add(MakeShared<FJsonValueObject>(R));
    }
    auto O=MakeShared<FJsonObject>();O->SetArrayField(TEXT("frames"),Rows);FString Text;FJsonSerializer::Serialize(O,TJsonWriterFactory<>::Create(&Text));return Text;
}

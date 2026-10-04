#include "AlsLyraAimWeightLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "AnimNodes/AnimNode_TwoWayBlend.h"
#include "AnimNodes/AnimNode_RotationOffsetBlendSpace.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace LyraCycleProbe { bool Set(UObject*,const TCHAR*,const TSharedPtr<FJsonValue>&); }
namespace LyraAimWeightProbe
{
FString Fail(int32 Line) { UE_LOG(LogTemp,Error,TEXT("LYRA_AIM_WEIGHT_FAILED line=%d"),Line);return {}; }
double Number(UObject* O,const TCHAR* Name)
{auto* P=FindFProperty<FNumericProperty>(O->GetClass(),Name);check(P&&P->IsFloatingPoint());return P->GetFloatingPointPropertyValue(P->ContainerPtrToValuePtr<void>(O));}
struct FInstanceAccess : UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D)
    {(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
    static void Publish(FAnimInstanceProxy& P,const TSharedPtr<FJsonObject>& Values)
    {
        using FRead=TMap<FName,float>&(FAnimInstanceProxy::*)(EAnimCurveType);
        auto& C=(P.*static_cast<FRead>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve);C.Reset();
        for(const auto& V:Values->Values)C.Add(FName(*V.Key),static_cast<float>(V.Value->AsNumber()));
    }
};
TSharedPtr<FJsonObject> Weights(UAnimInstance* Layer)
{
    auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("hipFire"),Number(Layer,TEXT("HipFireUpperBodyOverrideWeight")));
    R->SetNumberField(TEXT("aim"),Number(Layer,TEXT("AimOffsetBlendWeight")));return R;
}
}
FString UAlsLyraAimWeightLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,const FString& RequestsJson)
{
    using namespace LyraAimWeightProbe;TSharedPtr<FJsonObject> Input;
    if(!MainClass||!Mesh||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Input->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());auto T=TV->AsObject();auto* LC=LoadObject<UClass>(nullptr,*T->GetStringField(TEXT("class")));
        auto* LI=LC?IAnimClassInterface::GetFromClass(LC):nullptr;if(!LI)return Fail(__LINE__);
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
        auto* Owner=W->SpawnActor<ACharacter>(Spawn);if(!Owner)return Fail(__LINE__);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        C->SetSkeletalMesh(Mesh);C->SetAnimInstanceClass(MainClass);C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();
        auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);Main->LinkAnimClassLayers(LC);
        auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Layer||Layer->GetClass()!=LC)return Fail(__LINE__);
        auto* Function=Layer->FindFunction(TEXT("Update Blend Weight Data"));if(!Function)return Fail(__LINE__);
        auto* DeltaProperty=FindFProperty<FDoubleProperty>(Function,TEXT("DeltaTime"));if(!DeltaProperty)return Fail(__LINE__);
        for(const TCHAR* Name:{TEXT("HipFireUpperBodyOverrideWeight"),TEXT("AimOffsetBlendWeight"),TEXT("AimYaw"),TEXT("AimPitch")})
            if(!FindFProperty<FDoubleProperty>(LC,Name))return Fail(__LINE__);
        auto& MP=FInstanceAccess::Proxy(Main);auto& LP=FInstanceAccess::Proxy(Layer);
        auto* Blend=LP.GetMutableNodeFromIndex<FAnimNode_TwoWayBlend>(77);
        auto* Relaxed=LP.GetMutableNodeFromIndex<FAnimNode_RotationOffsetBlendSpace>(79);
        auto* Idle=LP.GetMutableNodeFromIndex<FAnimNode_RotationOffsetBlendSpace>(74);
        if(!Blend||!Relaxed||!Idle)return Fail(__LINE__);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            auto F=FV->AsObject();const double D=F->GetNumberField(TEXT("delta"));
            for(const auto& Field:F->GetObjectField(TEXT("main"))->Values)
                if(!LyraCycleProbe::Set(Main,*Field.Key,Field.Value))return Fail(__LINE__);
            for(const TCHAR* Name:{TEXT("AimYaw"),TEXT("AimPitch")})
                if(!LyraCycleProbe::Set(Layer,Name,F->GetField<EJson::Number>(Name)))return Fail(__LINE__);
            FProxyAccess::Pre(MP,Main,static_cast<float>(D));FProxyAccess::Pre(LP,Layer,static_cast<float>(D));
            LI->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& S){if(S.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct()){FAnimSubsystemUpdateContext G(S,Layer,static_cast<float>(D));S.Subsystem.OnPreUpdate_GameThread(G);S.Subsystem.OnPostUpdate_GameThread(G);FAnimSubsystemParallelUpdateContext P(S,LP,static_cast<float>(D));S.Subsystem.OnPreUpdate_WorkerThread(P);}return EAnimSubsystemEnumeration::Continue;});
            auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("before"),Weights(Layer));
            R->SetNumberField(TEXT("feedbackBefore"),Layer->GetCurveValue(TEXT("applyHipfireOverridePose")));
            R->SetNumberField(TEXT("rootYaw"),Number(Main,TEXT("RootYawOffset")));
            TArray<uint8> Params;Params.SetNumZeroed(Function->ParmsSize);DeltaProperty->SetPropertyValue_InContainer(Params.GetData(),D);Layer->ProcessEvent(Function,Params.GetData());
            R->SetObjectField(TEXT("after"),Weights(Layer));
            if(F->GetBoolField(TEXT("visited")))
            {
                FAnimationUpdateContext Context(&LP,static_cast<float>(D));
                Blend->GetEvaluateGraphExposedInputs().Execute(Context);Relaxed->GetEvaluateGraphExposedInputs().Execute(Context);Idle->GetEvaluateGraphExposedInputs().Execute(Context);
                R->SetNumberField(TEXT("blendPin"),Blend->Alpha);
                auto Pins=MakeShared<FJsonObject>();for(auto* Node:{Relaxed,Idle})
                {const FVector P=Node->GetPosition();auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("x"),P.X);V->SetNumberField(TEXT("y"),P.Y);V->SetNumberField(TEXT("alpha"),Node->Alpha);Pins->SetObjectField(Node==Relaxed?TEXT("relaxed"):TEXT("idle"),V);}R->SetObjectField(TEXT("pins"),Pins);
            }
            // Emulate only the enclosing Main's final curve-copy boundary. Hidden
            // or update-only frames retain previously committed feedback.
            if(F->GetBoolField(TEXT("evaluateMain")))
            {FProxyAccess::Publish(MP,F->GetObjectField(TEXT("finalFeedback")));Layer->CopyCurveValues(*Main);}
            R->SetNumberField(TEXT("feedbackAfter"),Layer->GetCurveValue(TEXT("applyHipfireOverridePose")));Frames.Add(MakeShared<FJsonValueObject>(R));
        }
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));R->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto Root=MakeShared<FJsonObject>();Root->SetArrayField(TEXT("traces"),Traces);FString Result;FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Result));return Result;
}

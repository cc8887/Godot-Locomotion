#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "AlsCharacter.h"
#include "Settings/AlsTurnInPlaceSettings.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequenceBase.h"
#include "Async/Async.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/World.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
struct FRestTraceProxy : FAnimInstanceProxy
{
    static void Prepare(FAnimInstanceProxy& P,UAnimInstance* I,float D){(P.*&FRestTraceProxy::PreUpdate)(I,D);}
};
FVector RestVector(const TSharedPtr<FJsonObject>& R,const TCHAR* Field)
{const auto& V=R->GetArrayField(Field);return FVector(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber());}
struct FRestTraceAccess : UAlsAnimationInstance
{
    static void Input(UAlsAnimationInstance* I,AAlsCharacter* C,const TSharedPtr<FJsonObject>& R)
    {
        I->*&FRestTraceAccess::Character=C;
        auto& L=I->*&FRestTraceAccess::LocomotionState;L.bMoving=R->GetBoolField(TEXT("moving"));L.ScaleWorldSpace=R->GetNumberField(TEXT("scale"));
        auto& V=I->*&FRestTraceAccess::ViewState;V.YawAngle=R->GetNumberField(TEXT("yaw"));V.YawSpeed=R->GetNumberField(TEXT("speed"));
        const int32 Rotation=static_cast<int32>(R->GetNumberField(TEXT("rotation")));
        I->*&FRestTraceAccess::RotationMode=Rotation==0?AlsRotationModeTags::VelocityDirection:Rotation==1?AlsRotationModeTags::ViewDirection:AlsRotationModeTags::Aiming;
        I->*&FRestTraceAccess::ViewMode=R->GetBoolField(TEXT("firstPerson"))?AlsViewModeTags::FirstPerson:AlsViewModeTags::ThirdPerson;
        const int32 Stance=static_cast<int32>(R->GetNumberField(TEXT("stance")));
        I->*&FRestTraceAccess::Stance=Stance==0?AlsStanceTags::Standing:Stance==1?AlsStanceTags::Crouching:FGameplayTag();
        (I->*&FRestTraceAccess::TransitionsState).bTransitionsAllowed=R->GetBoolField(TEXT("allowed"));
        FindFProperty<FBoolProperty>(I->GetClass(),TEXT("bPendingUpdate"))->SetPropertyValue_InContainer(I,R->GetBoolField(TEXT("pending")));
        auto& Feet=I->*&FRestTraceAccess::FeetState;
        Feet.Left.LockAmount=R->GetNumberField(TEXT("leftLock"));Feet.Right.LockAmount=R->GetNumberField(TEXT("rightLock"));
        Feet.Left.TargetLocationWorldSpace=RestVector(R,TEXT("leftTarget"));Feet.Left.LockLocationWorldSpace=RestVector(R,TEXT("leftLocation"));
        Feet.Right.TargetLocationWorldSpace=RestVector(R,TEXT("rightTarget"));Feet.Right.LockLocationWorldSpace=RestVector(R,TEXT("rightLocation"));
        (I->*&FRestTraceAccess::RotateInPlaceState).bUpdatedThisFrame=false;
        (I->*&FRestTraceAccess::TurnInPlaceState).bUpdatedThisFrame=false;
        (I->*&FRestTraceAccess::DynamicTransitionsState).bUpdatedThisFrame=false;
        FRestTraceProxy::Prepare(*GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I),I,R->GetNumberField(TEXT("delta")));
    }
    static TSharedPtr<FJsonObject> State(UAlsAnimationInstance* I)
    {
        auto R=MakeShared<FJsonObject>();const auto& Rotate=I->*&FRestTraceAccess::RotateInPlaceState;
        R->SetBoolField(TEXT("left"),Rotate.bRotatingLeft);R->SetBoolField(TEXT("right"),Rotate.bRotatingRight);R->SetNumberField(TEXT("rotateRate"),Rotate.PlayRate);
        const auto& Turn=I->*&FRestTraceAccess::TurnInPlaceState;
        R->SetNumberField(TEXT("turnDelay"),Turn.ActivationDelay);R->SetNumberField(TEXT("turnRate"),Turn.PlayRate);
        R->SetStringField(TEXT("turnSequence"),Turn.QueuedSettings?Turn.QueuedSettings->Sequence->GetPathName():TEXT(""));
        if(Turn.QueuedSettings)
        {
            R->SetStringField(TEXT("turnSlot"),Turn.QueuedSlotName.ToString());R->SetNumberField(TEXT("turnYaw"),Turn.QueuedTurnYawAngle);
            R->SetNumberField(TEXT("turnAssetRate"),Turn.QueuedSettings->PlayRate);R->SetNumberField(TEXT("turnAnimatedAngle"),Turn.QueuedSettings->AnimatedTurnAngle);
            R->SetBoolField(TEXT("turnScale"),Turn.QueuedSettings->bScalePlayRateByAnimatedTurnAngle);
        }
        R->SetNumberField(TEXT("dynamicDelay"),(I->*&FRestTraceAccess::DynamicTransitionsState).FrameDelay);
        const auto& T=I->*&FRestTraceAccess::TransitionsState;
        R->SetStringField(TEXT("transition"),T.QueuedTransitionSequence?T.QueuedTransitionSequence->GetPathName():TEXT(""));
        if(T.QueuedTransitionSequence)
        {
            R->SetNumberField(TEXT("transitionRate"),T.QueuedTransitionPlayRate);R->SetNumberField(TEXT("transitionStart"),T.QueuedTransitionStartTime);
            R->SetNumberField(TEXT("transitionIn"),T.QueuedTransitionBlendInDuration);R->SetNumberField(TEXT("transitionOut"),T.QueuedTransitionBlendOutDuration);
        }
        return R;
    }
};
}
bool UAlsAnimationGraphLibrary::ExportRestParentTrace(const FString& RequestPath,const FString& OutputPath)
{
    if(FPaths::IsRelative(RequestPath)||FPaths::IsRelative(OutputPath)||RequestPath==OutputPath)return false;
    FString Text;TSharedPtr<FJsonObject> Request;
    if(!FFileHelper::LoadFileToString(Text,*RequestPath)||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Request))return false;
    auto* Class=LoadClass<UAlsAnimationInstance>(nullptr,TEXT("/ALS/ALS/Character/AB_Als.AB_Als_C"));if(!Class)return false;
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false)
        .CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
    if(!World.IsValid()||!World->IsGameWorld())return false;ON_SCOPE_EXIT{World->DestroyWorld(false);};
    auto* Character=World->SpawnActor<AAlsCharacter>();if(!Character)return false;Character->SetActorTickEnabled(false);
    TArray<TSharedPtr<FJsonValue>> Traces;int32 Count=0;
    for(const auto& TraceValue:Request->GetArrayField(TEXT("traces")))
    {
        TStrongObjectPtr<UAlsAnimationInstance> Instance(NewObject<UAlsAnimationInstance>(Character->GetMesh(),Class));
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& Value:TraceValue->AsObject()->GetArrayField(TEXT("frames")))
        {
            const auto Frame=Value->AsObject();const auto Input=Frame->GetObjectField(TEXT("input"));FMemMark Mark(FMemStack::Get());
            FRestTraceAccess::Input(Instance.Get(),Character,Input);
            TArray<UFunction*> Functions;
            for(const auto& Operation:Frame->GetArrayField(TEXT("operations")))
            {
                const auto Name=Operation->AsString();
                if(Name!=TEXT("RefreshRotateInPlace")&&Name!=TEXT("RefreshTurnInPlace")&&Name!=TEXT("InitializeTurnInPlace")&&Name!=TEXT("RefreshDynamicTransitions"))return false;
                auto* Function=Instance->FindFunction(FName(*Name));if(!Function)return false;Functions.Add(Function);
            }
            const auto WorldType=World->WorldType;World->WorldType=Input->GetBoolField(TEXT("gameWorld"))?EWorldType::GamePreview:EWorldType::Editor;
            Async(EAsyncExecution::ThreadPool,[I=Instance.Get(),&Functions](){for(auto* Function:Functions)I->ProcessEvent(Function,nullptr);}).Get();
            World->WorldType=WorldType;
            auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("request"),Frame);Row->SetObjectField(TEXT("state"),FRestTraceAccess::State(Instance.Get()));
            Frames.Add(MakeShared<FJsonValueObject>(Row));++Count;
        }
        auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("name"),TraceValue->AsObject()->GetStringField(TEXT("name")));Trace->SetArrayField(TEXT("frames"),Frames);
        Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetNumberField(TEXT("schemaVersion"),1);Result->SetObjectField(TEXT("resourceHashes"),Request->GetObjectField(TEXT("resourceHashes")));
    Result->SetArrayField(TEXT("traces"),Traces);FString Json;
    if(!FJsonSerializer::Serialize(Result,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json))||!FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_REST_PARENT_NATIVE_OK frames=%d assets_saved=0"),Count);return true;
}

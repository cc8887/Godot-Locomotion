#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "AlsCharacter.h"
#include "Animation/AnimInstanceProxy.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/World.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
struct FViewProxyAccess : FAnimInstanceProxy
{
    static void Prepare(FAnimInstanceProxy& P,UAnimInstance* I,float D){(P.*&FViewProxyAccess::PreUpdate)(I,D);}
};
struct FViewInstanceAccess : UAlsAnimationInstance
{
    static auto& View(UAlsAnimationInstance* I){return I->*&FViewInstanceAccess::ViewState;}
    static auto& Spine(UAlsAnimationInstance* I){return I->*&FViewInstanceAccess::SpineState;}
    static auto& Head(UAlsAnimationInstance* I){return I->*&FViewInstanceAccess::HeadState;}
    static void Input(UAlsAnimationInstance* I,AAlsCharacter* C,const TSharedPtr<FJsonObject>& V)
    {
        I->*&FViewInstanceAccess::Character=C;
        auto& ViewState=View(I);ViewState.RotationWorldSpace=FRotator(V->GetNumberField(TEXT("ViewPitch")),V->GetNumberField(TEXT("ViewYaw")),0);
        ViewState.YawSpeed=V->GetNumberField(TEXT("ViewYawSpeed"));
        auto& L=I->*&FViewInstanceAccess::LocomotionState;
        L.RotationWorldSpace=FRotator(V->GetNumberField(TEXT("CharacterPitch")),V->GetNumberField(TEXT("CharacterYaw")),0);
        L.YawVelocityWorldSpace=V->GetNumberField(TEXT("CharacterYawVelocity"));
        L.InputYawAngleWorldSpace=V->GetNumberField(TEXT("InputYaw"));L.TargetYawAngleWorldSpace=V->GetNumberField(TEXT("TargetYaw"));L.bHasInput=V->GetBoolField(TEXT("HasInput"));
        auto& B=I->*&FViewInstanceAccess::MovementBase;B.bHasRelativeRotation=V->GetBoolField(TEXT("RelativeBaseRotation"));B.DeltaRotation.Yaw=V->GetNumberField(TEXT("BaseDeltaYaw"));
        const int32 Mode=V->GetIntegerField(TEXT("RotationMode"));
        I->*&FViewInstanceAccess::RotationMode=Mode==0?AlsRotationModeTags::VelocityDirection:Mode==2?AlsRotationModeTags::Aiming:AlsRotationModeTags::ViewDirection;
        I->*&FViewInstanceAccess::ViewMode=V->GetBoolField(TEXT("FirstPerson"))?AlsViewModeTags::FirstPerson:AlsViewModeTags::ThirdPerson;
        I->*&FViewInstanceAccess::LocomotionAction=V->GetBoolField(TEXT("HasAction"))?AlsLocomotionActionTags::Rolling:FGameplayTag();
        I->*&FViewInstanceAccess::DeltaTimeWithoutTimeDilation=V->GetNumberField(TEXT("RealDelta"));
        FindFProperty<FBoolProperty>(I->GetClass(),TEXT("bPendingUpdate"))->SetPropertyValue_InContainer(I,V->GetBoolField(TEXT("PendingUpdate")));
        // No foot/scene simulation is part of this state probe.
        (I->*&FViewInstanceAccess::FeetState).bValid=false;
        FViewProxyAccess::Prepare(*GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I),I,V->GetNumberField(TEXT("Delta")));
        I->OverrideCurveValue(TEXT("ViewBlock"),V->GetNumberField(TEXT("ViewBlock")));
        I->OverrideCurveValue(TEXT("PoseAiming"),V->GetNumberField(TEXT("PoseAiming")));
    }
    static void UpdateParent(UAlsAnimationInstance* I,float Delta){(I->*&FViewInstanceAccess::NativeThreadSafeUpdateAnimation)(Delta);}
    static auto* SettingsAsset(UAlsAnimationInstance* I){return (I->*&FViewInstanceAccess::Settings).Get();}
};
TArray<TSharedPtr<FJsonValue>> Numbers(std::initializer_list<double> Values)
{TArray<TSharedPtr<FJsonValue>> R;for(double V:Values)R.Add(MakeShared<FJsonValueNumber>(V));return R;}
}

bool UAlsAnimationGraphLibrary::ExportRefactoredViewTrace(const FString& RequestPath,const FString& OutputPath)
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
    TArray<TSharedPtr<FJsonValue>> Traces;int32 Total=0;
    for(const auto& TraceValue:Request->GetArrayField(TEXT("traces")))
    {
        TStrongObjectPtr<UAlsAnimationInstance> Instance(NewObject<UAlsAnimationInstance>(Character->GetMesh(),Class));
        if(!FViewInstanceAccess::SettingsAsset(Instance.Get()))return false;
        auto* Initialize=Instance->FindFunction(TEXT("InitializeHead"));auto* Refresh=Instance->FindFunction(TEXT("RefreshHead"));if(!Initialize||!Refresh)return false;
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FrameValue:TraceValue->AsObject()->GetArrayField(TEXT("frames")))
        {
            const auto Frame=FrameValue->AsObject();const auto Input=Frame->GetObjectField(TEXT("input"));
            FViewInstanceAccess::Input(Instance.Get(),Character,Input);
            FViewInstanceAccess::UpdateParent(Instance.Get(),Input->GetNumberField(TEXT("Delta")));
            if(Frame->GetBoolField(TEXT("initializeHead")))Instance->ProcessEvent(Initialize,nullptr);
            if(Frame->GetBoolField(TEXT("updateHead")))Instance->ProcessEvent(Refresh,nullptr);
            const auto& V=FViewInstanceAccess::View(Instance.Get());const auto& S=FViewInstanceAccess::Spine(Instance.Get());const auto& H=FViewInstanceAccess::Head(Instance.Get());
            const auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("request"),Frame);
            Row->SetArrayField(TEXT("view"),Numbers({V.YawAngle,V.PitchAngle,V.PitchAmount,V.HeadBlendAmount}));
            Row->SetArrayField(TEXT("spine"),Numbers({double(S.bSpineRotationAllowed),S.SpineAmount,S.SpineAmountScale,S.SpineAmountBias,S.LastYawAngle,S.LastYawAngleWorldSpace,S.YawAngle,S.FinalYawAngle}));
            Row->SetArrayField(TEXT("head"),Numbers({double(H.bInitializationRequired),double(H.bSwitchingLookSides),H.PitchAngle,H.YawAngle,H.YawVelocity,H.YawAmount}));
            Frames.Add(MakeShared<FJsonValueObject>(Row));++Total;
        }
        const auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("name"),TraceValue->AsObject()->GetStringField(TEXT("name")));Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    const auto Result=MakeShared<FJsonObject>();Result->SetNumberField(TEXT("schemaVersion"),1);Result->SetStringField(TEXT("inputsSha256"),Request->GetStringField(TEXT("inputsSha256")));Result->SetArrayField(TEXT("traces"),Traces);
    FString Json;if(!FJsonSerializer::Serialize(Result,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json))||!FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_REFACTORED_VIEW_TRACE_OK frames=%d assets_saved=0"),Total);return true;
}

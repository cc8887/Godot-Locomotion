#include "AlsAnimationGraphLibrary.h"
#include "AlsCharacter.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/ActiveMontageInstanceScope.h"
#include "Components/SkeletalMeshComponent.h"
#include "Settings/AlsMantlingSettings.h"
#include "Editor.h"
#include "Engine/World.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/UnrealType.h"

namespace
{
// Obtain protected member pointers through a derived access scope; invoke on the
// real UAnimInstance, without downcasting it or substituting its implementation.
struct FMantleBranchAnimAccess : UAnimInstance
{
    static FAnimNotifyQueue& Queue(UAnimInstance* Instance)
    {const auto Member=&FMantleBranchAnimAccess::NotifyQueue;return Instance->*Member;}
    static void Advance(UAnimInstance* Instance,float Delta)
    {
        const auto Weight=&FMantleBranchAnimAccess::Montage_UpdateWeight;
        const auto Move=&FMantleBranchAnimAccess::Montage_Advance;
        (Instance->*Weight)(Delta);(Instance->*Move)(Delta);
    }
};
}

bool UAlsAnimationGraphLibrary::ExportMantlingBranchTrace(const FString& OutputPath)
{
    if (FPaths::IsRelative(OutputPath)) return false;
    auto* World=GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    auto* Class=LoadClass<AAlsCharacter>(nullptr,TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"));
    auto* ActiveProperty=FindFProperty<FArrayProperty>(FAnimMontageInstance::StaticStruct(),TEXT("ActiveStateBranchingPoints"));
    if (!World || !Class || !ActiveProperty) return false;
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const TCHAR* Name : {TEXT("High"),TEXT("Low"),TEXT("Low_BothHands"),TEXT("Low_Box"),TEXT("Low_Left"),TEXT("Low_Right")})
    {
        const FString Path=FString::Printf(TEXT("/ALS/ALS/Data/Character/Mantle/MS_Als_%s.MS_Als_%s"),Name,Name);
        auto* Settings=LoadObject<UAlsMantlingSettings>(nullptr,*Path);
        if (!Settings || !Settings->Montage) return false;
        auto* Montage=Settings->Montage.Get();
        for (int32 Hz : {30,60,120}) for (int32 Mode=0;Mode<8;++Mode)
        {
            FActorSpawnParameters Spawn; Spawn.ObjectFlags|=RF_Transient;
            Spawn.SpawnCollisionHandlingOverride=ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
            auto* Character=World->SpawnActor<AAlsCharacter>(Class,FVector(0,0,100000),FRotator::ZeroRotator,Spawn);
            if (!Character) return false;
            ON_SCOPE_EXIT { World->DestroyActor(Character); };
            // Controlled captured inputs only. Do not invoke character gameplay update or change source assets.
            auto SetTag=[&](const TCHAR* Field,const TCHAR* Tag)
            {
                auto* Property=FindFProperty<FStructProperty>(Class,Field);
                if (!Property || Property->Struct!=FGameplayTag::StaticStruct()) return false;
                const auto Value=FGameplayTag::RequestGameplayTag(FName(Tag),false);if (!Value.IsValid()) return false;
                *Property->ContainerPtrToValuePtr<FGameplayTag>(Character)=Value;
                return true;
            };
            if (!SetTag(TEXT("LocomotionMode"),Mode==2?TEXT("Als.LocomotionMode.InAir"):TEXT("Als.LocomotionMode.Grounded")) ||
                !SetTag(TEXT("RotationMode"),Mode==3?TEXT("Als.RotationMode.Aiming"):TEXT("Als.RotationMode.ViewDirection")) ||
                !SetTag(TEXT("Stance"),Mode==4?TEXT("Als.Stance.Crouching"):TEXT("Als.Stance.Standing"))) return false;
            auto* StateProperty=FindFProperty<FStructProperty>(Class,TEXT("LocomotionState"));
            if (!StateProperty || StateProperty->Struct!=FAlsLocomotionState::StaticStruct()) return false;
            StateProperty->ContainerPtrToValuePtr<FAlsLocomotionState>(Character)->bHasInput=Mode==1 || Mode==7;
            auto* Anim=Character->GetMesh()->GetAnimInstance();
            if (!Anim || Anim->Montage_Play(Montage)<=0) return false;
            auto* Instance=Anim->GetActiveInstanceForMontage(Montage); if (!Instance) return false;
            const int32 Id=Instance->GetInstanceID();
            auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("montage"),Montage->GetPathName());
            Trace->SetNumberField(TEXT("hz"),Hz);Trace->SetNumberField(TEXT("mode"),Mode);
            TArray<TSharedPtr<FJsonValue>> Frames;
            for (int32 Frame=0;Frame<Hz*5;++Frame)
            {
                const float Delta=Frame==0 && Mode==6 ? 4.f : Frame==0 && Mode==7 ? 2.f : 1.f/Hz;
                // Action callbacks may apply the character's desired stance. Capture
                // the next controlled input again, as the Godot host will do per frame.
                if (!SetTag(TEXT("Stance"),Mode==4?TEXT("Als.Stance.Crouching"):TEXT("Als.Stance.Standing"))) return false;
                const bool bStop=Mode==5 && Frame==3;
                if (bStop)
                {
                    Instance=Anim->GetMontageInstanceForID(Id);if (!Instance) return false;
                    FMontageBlendSettings Blend(Montage->BlendOut);Blend.Blend.BlendTime=.4f;Instance->Stop(Blend);
                }
                auto& Queue=FMantleBranchAnimAccess::Queue(Anim);
                // Reset is not DLL-exported. Initialization already bound World;
                // clear the per-frame arrays and refresh LOD, preserving native RNG.
                Queue.AnimNotifies.Reset();Queue.UnfilteredMontageAnimNotifies.Reset();
                Queue.PredictedLODLevel=Character->GetMesh()->GetPredictedLODLevel();
                FMantleBranchAnimAccess::Advance(Anim,Delta);
                Instance=Anim->GetMontageInstanceForID(Id);
                if (Instance && !Instance->IsValid()) Instance=nullptr;
                auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("delta"),Delta);Row->SetBoolField(TEXT("stopBefore"),bStop);
                Row->SetBoolField(TEXT("exists"),Instance!=nullptr);Row->SetStringField(TEXT("action"),Character->GetLocomotionAction().ToString());
                Row->SetNumberField(TEXT("directNotifyCount"),Queue.AnimNotifies.Num());
                Row->SetNumberField(TEXT("notifyRandomSeed"),Queue.RandomStream.GetCurrentSeed());
                TArray<TSharedPtr<FJsonValue>> Queued;
                // Only one authored slot. Read the original filtered-by-policy queue,
                // before proxy slot relevance; do not dispatch sounds or effects.
                for (const auto& Slot : Queue.UnfilteredMontageAnimNotifies)
                {
                    if (Slot.Key!=FName(TEXT("PostLocomotion"))) return false;
                    for (const auto& Ref : Slot.Value.Notifies)
                    {
                        const auto* Event=Ref.GetNotify();
                        const auto* Context=Ref.GetContextData<UE::Anim::FAnimNotifyMontageInstanceContext>();
                        if (!Event || !Event->Notify || !Context || Context->MontageInstanceID!=Id) return false;
                        auto Notify=MakeShared<FJsonObject>();Notify->SetStringField(TEXT("object"),Event->Notify->GetPathName());
                        Notify->SetNumberField(TEXT("currentTime"),Ref.GetCurrentAnimationTime());
                        // Normalize the process-global engine ID only after checking ownership.
                        Notify->SetNumberField(TEXT("instance"),1);Queued.Add(MakeShared<FJsonValueObject>(Notify));
                    }
                }
                Row->SetArrayField(TEXT("queued"),Queued);
                if (Instance)
                {
                    Row->SetNumberField(TEXT("position"),Instance->GetPosition());Row->SetNumberField(TEXT("weight"),Instance->GetWeight());
                    Row->SetNumberField(TEXT("desired"),Instance->GetDesiredWeight());Row->SetNumberField(TEXT("blendTime"),Instance->GetBlendTime());
                    Row->SetBoolField(TEXT("playing"),Instance->IsPlaying());
                    TArray<TSharedPtr<FJsonValue>> Active;
                    for (const auto& Event : *ActiveProperty->ContainerPtrToValuePtr<TArray<FAnimNotifyEvent>>(Instance))
                        Active.Add(MakeShared<FJsonValueString>(Event.NotifyStateClass->GetClass()->GetPathName()));
                    Row->SetArrayField(TEXT("active"),Active);
                }
                Frames.Add(MakeShared<FJsonValueObject>(Row));if (!Instance) break;
            }
            if (Instance) return false;
            Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
        }
    }
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);Root->SetArrayField(TEXT("traces"),Traces);
    FString Json;if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))) return false;
    return FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

#include "AlsNotifyClassRegistry.h"

#include "Animation/AnimTypes.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "Dom/JsonObject.h"
#include "Misc/SecureHash.h"
#include "UObject/UnrealType.h"

namespace
{
    enum class EAlsPayloadResult : uint8
    {
        Success,
        Unavailable,
        Invalid,
    };

    struct FAlsNotifyClassAlias
    {
        const TCHAR* ClassPath;
        const TCHAR* Kind;
    };

    constexpr FAlsNotifyClassAlias ClassAliases[] = {
        {TEXT("/Script/ALS.AlsAnimNotify_FootstepEffects"), TEXT("Footstep")},
        {TEXT("/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/Footstep_AnimNotify.Footstep_AnimNotify_C"), TEXT("Footstep")},
        {TEXT("/Script/ALS.AlsAnimNotifyState_SetLocomotionAction"), TEXT("SetAction")},
        {TEXT("/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/MovementAction_NotifyState.MovementAction_NotifyState_C"), TEXT("SetAction")},
        {TEXT("/Script/ALS.AlsAnimNotify_SetGroundedEntryMode"), TEXT("SetGroundedEntry")},
        {TEXT("/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/GroundedEntryState_AnimNotify.GroundedEntryState_AnimNotify_C"), TEXT("SetGroundedEntry")},
        {TEXT("/Script/ALS.AlsAnimNotifyState_EarlyBlendOut"), TEXT("EarlyBlendOut")},
        {TEXT("/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/EarlyBlendOut_NotifyState.EarlyBlendOut_NotifyState_C"), TEXT("EarlyBlendOut")},
        {TEXT("/Script/ALS.AlsAnimNotifyState_SetRootMotionScale"), TEXT("RootMotionScale")},
    };

    FString CreateSha1(const FString& Value)
    {
        const FTCHARToUTF8 Utf8(*Value);
        uint8 Hash[FSHA1::DigestSize];
        FSHA1::HashBuffer(Utf8.Get(), Utf8.Length(), Hash);
        FString Result = BytesToHex(Hash, UE_ARRAY_COUNT(Hash));
        Result.ToLowerInline();
        return Result;
    }

    const UObject* GetNotifyObject(const FAnimNotifyEvent& NotifyEvent)
    {
        if (NotifyEvent.Notify)
        {
            return NotifyEvent.Notify;
        }
        return NotifyEvent.NotifyStateClass;
    }

    FString GetEnumToken(const UEnum& Enum, const int64 Value)
    {
        FString Name = Enum.GetNameStringByValue(Value);
        int32 Separator = INDEX_NONE;
        if (Name.FindLastChar(TEXT(':'), Separator))
        {
            Name.RightChopInline(Separator + 1);
        }
        if (Name.StartsWith(TEXT("NewEnumerator")))
        {
            const FString DisplayName = Enum.GetDisplayNameTextByValue(Value).ToString();
            if (!DisplayName.IsEmpty())
            {
                Name = DisplayName;
            }
        }
        Name.ReplaceInline(TEXT(" "), TEXT(""));
        return Name;
    }

    bool TryReadEnum(const UObject& Object, const FName PropertyName, FString& OutValue)
    {
        const FProperty* Property = Object.GetClass()->FindPropertyByName(PropertyName);
        if (!Property)
        {
            return false;
        }
        const void* ValueAddress = Property->ContainerPtrToValuePtr<void>(&Object);
        if (const FEnumProperty* EnumProperty = CastField<FEnumProperty>(Property))
        {
            const int64 Value = EnumProperty->GetUnderlyingProperty()->GetSignedIntPropertyValue(ValueAddress);
            OutValue = GetEnumToken(*EnumProperty->GetEnum(), Value);
            return !OutValue.IsEmpty();
        }
        if (const FByteProperty* ByteProperty = CastField<FByteProperty>(Property); ByteProperty && ByteProperty->Enum)
        {
            OutValue = GetEnumToken(*ByteProperty->Enum, ByteProperty->GetPropertyValue(ValueAddress));
            return !OutValue.IsEmpty();
        }
        return false;
    }

    bool TryReadBool(const UObject& Object, const FName PropertyName, bool& OutValue)
    {
        const FBoolProperty* Property = FindFProperty<FBoolProperty>(Object.GetClass(), PropertyName);
        if (!Property)
        {
            return false;
        }
        OutValue = Property->GetPropertyValue_InContainer(&Object);
        return true;
    }

    bool TryReadNumber(const UObject& Object, const FName PropertyName, double& OutValue)
    {
        const FProperty* Property = Object.GetClass()->FindPropertyByName(PropertyName);
        if (const FFloatProperty* FloatProperty = CastField<FFloatProperty>(Property))
        {
            OutValue = FloatProperty->GetPropertyValue_InContainer(&Object);
            return true;
        }
        if (const FDoubleProperty* DoubleProperty = CastField<FDoubleProperty>(Property))
        {
            OutValue = DoubleProperty->GetPropertyValue_InContainer(&Object);
            return true;
        }
        return false;
    }

    bool TryReadGameplayTag(const UObject& Object, const FName PropertyName, FString& OutValue)
    {
        const FStructProperty* Property = FindFProperty<FStructProperty>(Object.GetClass(), PropertyName);
        if (!Property || !Property->Struct)
        {
            return false;
        }
        const FNameProperty* TagNameProperty = FindFProperty<FNameProperty>(Property->Struct, TEXT("TagName"));
        if (!TagNameProperty)
        {
            return false;
        }
        const void* ValueAddress = Property->ContainerPtrToValuePtr<void>(&Object);
        const FName TagName = TagNameProperty->GetPropertyValue_InContainer(ValueAddress);
        if (TagName.IsNone())
        {
            OutValue = TEXT("None");
            return true;
        }
        OutValue = TagName.ToString();
        int32 Separator = INDEX_NONE;
        if (OutValue.FindLastChar(TEXT('.'), Separator))
        {
            OutValue.RightChopInline(Separator + 1);
        }
        return !OutValue.IsEmpty();
    }

    EAlsPayloadResult BuildFootstepPayload(const UObject& Object, FJsonObject& Payload, FString& OutError)
    {
        FString Foot;
        if (!TryReadEnum(Object, TEXT("Foot"), Foot) && !TryReadEnum(Object, TEXT("FootstepFoot"), Foot) &&
            !TryReadEnum(Object, TEXT("FootBone"), Foot))
        {
            return EAlsPayloadResult::Unavailable;
        }
        Payload.SetStringField(TEXT("foot"), Foot);
        return EAlsPayloadResult::Success;
    }

    EAlsPayloadResult BuildSetActionPayload(const UObject& Object, FJsonObject& Payload, FString& OutError)
    {
        FString Action;
        if (!TryReadEnum(Object, TEXT("Action"), Action) && !TryReadEnum(Object, TEXT("MovementAction"), Action) &&
            !TryReadGameplayTag(Object, TEXT("LocomotionAction"), Action))
        {
            return EAlsPayloadResult::Unavailable;
        }
        if (Action == TEXT("HighMantle") || Action == TEXT("LowMantle"))
        {
            Action = TEXT("Mantling");
        }
        if (Action.StartsWith(TEXT("NewEnumerator")) ||
            (Action != TEXT("None") && Action != TEXT("Rolling") && Action != TEXT("Mantling") &&
                Action != TEXT("Ragdolling") && Action != TEXT("GettingUp")))
        {
            OutError = FString::Printf(TEXT("Invalid SetAction action value on %s: %s."), *Object.GetClass()->GetPathName(), *Action);
            return EAlsPayloadResult::Invalid;
        }
        Payload.SetStringField(TEXT("action"), Action);
        return EAlsPayloadResult::Success;
    }

    EAlsPayloadResult BuildSetGroundedEntryPayload(const UObject& Object, FJsonObject& Payload, FString& OutError)
    {
        FString Mode;
        if (!TryReadEnum(Object, TEXT("Mode"), Mode) && !TryReadEnum(Object, TEXT("GroundedEntryState"), Mode) &&
            !TryReadGameplayTag(Object, TEXT("GroundedEntryMode"), Mode))
        {
            return EAlsPayloadResult::Unavailable;
        }
        if (Mode.StartsWith(TEXT("NewEnumerator")) || (Mode != TEXT("None") && Mode != TEXT("FromRoll")))
        {
            OutError = FString::Printf(TEXT("Invalid SetGroundedEntry mode value on %s: %s."), *Object.GetClass()->GetPathName(), *Mode);
            return EAlsPayloadResult::Invalid;
        }
        Payload.SetStringField(TEXT("mode"), Mode);
        return EAlsPayloadResult::Success;
    }

    EAlsPayloadResult BuildEarlyBlendOutPayload(const UObject& Object, FJsonObject& Payload, FString& OutError)
    {
        double BlendOutSeconds = 0.0;
        bool bCheckInput = false;
        bool bCheckLocomotionMode = false;
        bool bCheckRotationMode = false;
        bool bCheckStance = false;
        FString LocomotionMode;
        FString RotationMode;
        FString Stance;
        if ((!TryReadNumber(Object, TEXT("BlendOutSeconds"), BlendOutSeconds) &&
                !TryReadNumber(Object, TEXT("BlendOutTime"), BlendOutSeconds) &&
                !TryReadNumber(Object, TEXT("BlendOutDuration"), BlendOutSeconds)) ||
            !TryReadBool(Object, TEXT("bCheckInput"), bCheckInput) ||
            !TryReadBool(Object, TEXT("bCheckLocomotionMode"), bCheckLocomotionMode) ||
            !TryReadBool(Object, TEXT("bCheckRotationMode"), bCheckRotationMode) ||
            !TryReadBool(Object, TEXT("bCheckStance"), bCheckStance) ||
            (!TryReadEnum(Object, TEXT("LocomotionMode"), LocomotionMode) &&
                !TryReadGameplayTag(Object, TEXT("LocomotionModeEquals"), LocomotionMode)) ||
            (!TryReadEnum(Object, TEXT("RotationMode"), RotationMode) &&
                !TryReadGameplayTag(Object, TEXT("RotationModeEquals"), RotationMode)) ||
            (!TryReadEnum(Object, TEXT("Stance"), Stance) &&
                !TryReadGameplayTag(Object, TEXT("StanceEquals"), Stance)))
        {
            return EAlsPayloadResult::Unavailable;
        }
        if (!FMath::IsFinite(BlendOutSeconds) || BlendOutSeconds < 0.0)
        {
            OutError = FString::Printf(TEXT("Invalid EarlyBlendOut payload on %s."), *Object.GetClass()->GetPathName());
            return EAlsPayloadResult::Invalid;
        }
        Payload.SetNumberField(TEXT("blendOutSeconds"), BlendOutSeconds);
        Payload.SetBoolField(TEXT("checkInput"), bCheckInput);
        Payload.SetBoolField(TEXT("checkLocomotionMode"), bCheckLocomotionMode);
        Payload.SetStringField(TEXT("locomotionMode"), LocomotionMode);
        Payload.SetBoolField(TEXT("checkRotationMode"), bCheckRotationMode);
        Payload.SetStringField(TEXT("rotationMode"), RotationMode);
        Payload.SetBoolField(TEXT("checkStance"), bCheckStance);
        Payload.SetStringField(TEXT("stance"), Stance);
        return EAlsPayloadResult::Success;
    }

    EAlsPayloadResult BuildRootMotionScalePayload(const UObject& Object, FJsonObject& Payload, FString& OutError)
    {
        double TranslationScale = 0.0;
        if (!TryReadNumber(Object, TEXT("TranslationScale"), TranslationScale))
        {
            return EAlsPayloadResult::Unavailable;
        }
        if (!FMath::IsFinite(TranslationScale) || TranslationScale < 0.0)
        {
            OutError = FString::Printf(TEXT("Invalid RootMotionScale translationScale on %s: %.17g."),
                *Object.GetClass()->GetPathName(), TranslationScale);
            return EAlsPayloadResult::Invalid;
        }
        Payload.SetNumberField(TEXT("translationScale"), TranslationScale);
        return EAlsPayloadResult::Success;
    }

    EAlsPayloadResult BuildPayload(const FString& Kind, const UObject& Object, FJsonObject& Payload, FString& OutError)
    {
        if (Kind == TEXT("Footstep"))
        {
            return BuildFootstepPayload(Object, Payload, OutError);
        }
        if (Kind == TEXT("SetAction"))
        {
            return BuildSetActionPayload(Object, Payload, OutError);
        }
        if (Kind == TEXT("SetGroundedEntry"))
        {
            return BuildSetGroundedEntryPayload(Object, Payload, OutError);
        }
        if (Kind == TEXT("EarlyBlendOut"))
        {
            return BuildEarlyBlendOutPayload(Object, Payload, OutError);
        }
        if (Kind == TEXT("RootMotionScale"))
        {
            return BuildRootMotionScalePayload(Object, Payload, OutError);
        }
        return EAlsPayloadResult::Success;
    }
}

bool FAlsNotifyClassRegistry::Export(const FAnimNotifyEvent& NotifyEvent, const FString& AssetStableId,
    const int32 SourceIndex, FAlsExportedTimelineEntry& OutEntry, FString& OutError)
{
    OutEntry = {};
    OutError.Reset();
    const UObject* NotifyObject = GetNotifyObject(NotifyEvent);
    if (!NotifyObject || AssetStableId.IsEmpty() || SourceIndex < 0)
    {
        OutError = FString::Printf(TEXT("Invalid notify identity: asset=%s sourceIndex=%d hasClass=%d."),
            *AssetStableId, SourceIndex, NotifyObject != nullptr);
        return false;
    }

    OutEntry.SourceClassPath = NotifyObject->GetClass()->GetPathName();
    OutEntry.DisplayName = NotifyEvent.GetNotifyEventName().ToString();
    OutEntry.TimeSeconds = NotifyEvent.GetTime();
    OutEntry.DurationSeconds = NotifyEvent.GetDuration();
    OutEntry.TriggerWeightThreshold = NotifyEvent.TriggerWeightThreshold;
    OutEntry.SourceIndex = SourceIndex;
    OutEntry.TrackIndex = NotifyEvent.TrackIndex;
    OutEntry.Payload = MakeShared<FJsonObject>();
    if (OutEntry.SourceClassPath.IsEmpty() || OutEntry.DisplayName.IsEmpty() || OutEntry.DisplayName == TEXT("None") ||
        !FMath::IsFinite(OutEntry.TimeSeconds) || OutEntry.TimeSeconds < 0.0 ||
        !FMath::IsFinite(OutEntry.DurationSeconds) || OutEntry.DurationSeconds < 0.0 ||
        !FMath::IsFinite(OutEntry.TriggerWeightThreshold) || OutEntry.TriggerWeightThreshold < 0.0 ||
        OutEntry.TriggerWeightThreshold > 1.0 || OutEntry.TrackIndex < 0)
    {
        OutError = FString::Printf(TEXT("Invalid notify fields: asset=%s sourceIndex=%d class=%s name=%s time=%.17g duration=%.17g threshold=%.17g track=%d."),
            *AssetStableId, SourceIndex, *OutEntry.SourceClassPath, *OutEntry.DisplayName, OutEntry.TimeSeconds,
            OutEntry.DurationSeconds, OutEntry.TriggerWeightThreshold, OutEntry.TrackIndex);
        return false;
    }

    switch (NotifyEvent.MontageTickType.GetValue())
    {
    case EMontageNotifyTickType::Queued:
        OutEntry.TickMode = TEXT("Queued");
        break;
    case EMontageNotifyTickType::BranchingPoint:
        OutEntry.TickMode = TEXT("BranchingPoint");
        break;
    default:
        OutError = FString::Printf(TEXT("Unsupported notify tick mode: asset=%s sourceIndex=%d value=%d."),
            *AssetStableId, SourceIndex, static_cast<int32>(NotifyEvent.MontageTickType.GetValue()));
        return false;
    }

    OutEntry.Kind = TEXT("Generic");
    for (const FAlsNotifyClassAlias& Alias : ClassAliases)
    {
        if (OutEntry.SourceClassPath == Alias.ClassPath)
        {
            OutEntry.Kind = Alias.Kind;
            break;
        }
    }
    if (OutEntry.Kind != TEXT("Generic"))
    {
        const EAlsPayloadResult PayloadResult = BuildPayload(OutEntry.Kind, *NotifyObject, *OutEntry.Payload, OutError);
        if (PayloadResult == EAlsPayloadResult::Invalid)
        {
            return false;
        }
        if (PayloadResult == EAlsPayloadResult::Unavailable)
        {
            OutEntry.Kind = TEXT("Generic");
            OutEntry.Payload = MakeShared<FJsonObject>();
        }
    }

    OutEntry.StableEventId = CreateSha1(FString::Printf(TEXT("%s|timeline|%d|%s"),
        *AssetStableId, SourceIndex, *OutEntry.SourceClassPath));
    return true;
}

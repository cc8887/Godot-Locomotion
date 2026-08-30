#include "AlsNotifyClassRegistry.h"

#include "Animation/AnimTypes.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "Dom/JsonObject.h"
#include "Misc/SecureHash.h"
#include "UObject/UnrealType.h"
#include "UObject/UObjectGlobals.h"

#include <initializer_list>

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

    struct FAlsEnumValueAlias
    {
        const TCHAR* RawToken;
        const TCHAR* CanonicalValue;
    };

    constexpr TCHAR MovementActionEnumPath[] =
        TEXT("/Game/AdvancedLocomotionV4/Data/Enums/ALS_MovementAction.ALS_MovementAction");
    constexpr TCHAR NativeFootBoneEnumPath[] = TEXT("/Script/ALS.EAlsFootBone");
    constexpr TCHAR GameplayTagStructPath[] = TEXT("/Script/GameplayTags.GameplayTag");
    constexpr FAlsEnumValueAlias MovementActionAliases[] = {
        {TEXT("NewEnumerator0"), TEXT("Mantling")},
        {TEXT("NewEnumerator1"), TEXT("Mantling")},
        {TEXT("NewEnumerator2"), TEXT("Rolling")},
        {TEXT("NewEnumerator3"), TEXT("GettingUp")},
        {TEXT("NewEnumerator4"), TEXT("None")},
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

    EAlsPayloadResult NormalizeEnumToken(const FString& EnumPath, FString RawToken, FString& OutValue)
    {
        int32 Separator = INDEX_NONE;
        if (RawToken.FindLastChar(TEXT(':'), Separator))
        {
            RawToken.RightChopInline(Separator + 1);
        }
        if (EnumPath == MovementActionEnumPath)
        {
            for (const FAlsEnumValueAlias& Alias : MovementActionAliases)
            {
                if (RawToken == Alias.RawToken)
                {
                    OutValue = Alias.CanonicalValue;
                    return EAlsPayloadResult::Success;
                }
            }
            if (RawToken == TEXT("NewEnumerator5"))
            {
                return EAlsPayloadResult::Invalid;
            }
            return EAlsPayloadResult::Invalid;
        }
        if (EnumPath == NativeFootBoneEnumPath)
        {
            if (RawToken == TEXT("Left") || RawToken == TEXT("Right"))
            {
                OutValue = MoveTemp(RawToken);
                return EAlsPayloadResult::Success;
            }
            return EAlsPayloadResult::Invalid;
        }
        return EAlsPayloadResult::Unavailable;
    }

    EAlsPayloadResult TryReadEnum(const UObject& Object, const std::initializer_list<FName> PropertyNames,
        FString& OutValue)
    {
        const FProperty* Property = nullptr;
        for (const FName PropertyName : PropertyNames)
        {
            Property = Object.GetClass()->FindPropertyByName(PropertyName);
            if (Property)
            {
                break;
            }
        }
        if (!Property)
        {
            return EAlsPayloadResult::Unavailable;
        }
        const void* ValueAddress = Property->ContainerPtrToValuePtr<void>(&Object);
        if (const FEnumProperty* EnumProperty = CastField<FEnumProperty>(Property); EnumProperty && EnumProperty->GetEnum())
        {
            const int64 Value = EnumProperty->GetUnderlyingProperty()->GetSignedIntPropertyValue(ValueAddress);
            return NormalizeEnumToken(EnumProperty->GetEnum()->GetPathName(),
                EnumProperty->GetEnum()->GetNameStringByValue(Value), OutValue);
        }
        if (const FByteProperty* ByteProperty = CastField<FByteProperty>(Property); ByteProperty && ByteProperty->Enum)
        {
            return NormalizeEnumToken(ByteProperty->Enum->GetPathName(),
                ByteProperty->Enum->GetNameStringByValue(ByteProperty->GetPropertyValue(ValueAddress)), OutValue);
        }
        return EAlsPayloadResult::Invalid;
    }

    EAlsPayloadResult TryReadBool(const UObject& Object, const FName PropertyName, bool& OutValue)
    {
        const FProperty* BaseProperty = Object.GetClass()->FindPropertyByName(PropertyName);
        if (!BaseProperty)
        {
            return EAlsPayloadResult::Unavailable;
        }
        const FBoolProperty* Property = CastField<FBoolProperty>(BaseProperty);
        if (!Property)
        {
            return EAlsPayloadResult::Invalid;
        }
        OutValue = Property->GetPropertyValue_InContainer(&Object);
        return EAlsPayloadResult::Success;
    }

    EAlsPayloadResult TryReadNumber(const UObject& Object, const std::initializer_list<FName> PropertyNames,
        double& OutValue)
    {
        const FProperty* Property = nullptr;
        for (const FName PropertyName : PropertyNames)
        {
            Property = Object.GetClass()->FindPropertyByName(PropertyName);
            if (Property)
            {
                break;
            }
        }
        if (!Property)
        {
            return EAlsPayloadResult::Unavailable;
        }
        if (const FFloatProperty* FloatProperty = CastField<FFloatProperty>(Property))
        {
            OutValue = FloatProperty->GetPropertyValue_InContainer(&Object);
            return EAlsPayloadResult::Success;
        }
        if (const FDoubleProperty* DoubleProperty = CastField<FDoubleProperty>(Property))
        {
            OutValue = DoubleProperty->GetPropertyValue_InContainer(&Object);
            return EAlsPayloadResult::Success;
        }
        return EAlsPayloadResult::Invalid;
    }

    bool IsGameplayTagStruct(const UScriptStruct* Struct)
    {
        return Struct && Struct->GetPathName() == GameplayTagStructPath;
    }

    EAlsPayloadResult TryReadGameplayTag(const UObject& Object,
        const std::initializer_list<FName> PropertyNames, FString& OutValue)
    {
        const FProperty* BaseProperty = nullptr;
        for (const FName PropertyName : PropertyNames)
        {
            BaseProperty = Object.GetClass()->FindPropertyByName(PropertyName);
            if (BaseProperty)
            {
                break;
            }
        }
        if (!BaseProperty)
        {
            return EAlsPayloadResult::Unavailable;
        }
        const FStructProperty* Property = CastField<FStructProperty>(BaseProperty);
        if (!Property || !IsGameplayTagStruct(Property->Struct))
        {
            return EAlsPayloadResult::Invalid;
        }
        const FNameProperty* TagNameProperty = FindFProperty<FNameProperty>(Property->Struct, TEXT("TagName"));
        if (!TagNameProperty)
        {
            return EAlsPayloadResult::Invalid;
        }
        const void* ValueAddress = Property->ContainerPtrToValuePtr<void>(&Object);
        const FName TagName = TagNameProperty->GetPropertyValue_InContainer(ValueAddress);
        if (TagName.IsNone())
        {
            OutValue = TEXT("None");
            return EAlsPayloadResult::Success;
        }
        OutValue = TagName.ToString();
        int32 Separator = INDEX_NONE;
        if (OutValue.FindLastChar(TEXT('.'), Separator))
        {
            OutValue.RightChopInline(Separator + 1);
        }
        return OutValue.IsEmpty() ? EAlsPayloadResult::Invalid : EAlsPayloadResult::Success;
    }

    EAlsPayloadResult TryReadEnumOrGameplayTag(const UObject& Object,
        const std::initializer_list<FName> EnumPropertyNames,
        const std::initializer_list<FName> GameplayTagPropertyNames, FString& OutValue)
    {
        const EAlsPayloadResult EnumResult = TryReadEnum(Object, EnumPropertyNames, OutValue);
        return EnumResult == EAlsPayloadResult::Unavailable
            ? TryReadGameplayTag(Object, GameplayTagPropertyNames, OutValue)
            : EnumResult;
    }

    bool NormalizeFootstep(FString& Foot)
    {
        return Foot == TEXT("Unspecified") || Foot == TEXT("Left") || Foot == TEXT("Right");
    }

    bool NormalizeSetAction(FString& Action)
    {
        if (Action == TEXT("HighMantle") || Action == TEXT("LowMantle"))
        {
            Action = TEXT("Mantling");
        }
        return Action == TEXT("None") || Action == TEXT("Rolling") || Action == TEXT("Mantling") ||
            Action == TEXT("Ragdolling") || Action == TEXT("GettingUp");
    }

    bool NormalizeGroundedEntry(FString& Mode)
    {
        return Mode == TEXT("None") || Mode == TEXT("FromRoll");
    }

    bool NormalizeEarlyBlendOutDomains(FString& LocomotionMode, FString& RotationMode, FString& Stance)
    {
        if (LocomotionMode == TEXT("Mantle"))
        {
            LocomotionMode = TEXT("Mantling");
        }
        else if (LocomotionMode == TEXT("Ragdolling"))
        {
            LocomotionMode = TEXT("Ragdoll");
        }
        if (RotationMode == TEXT("ViewDirection"))
        {
            RotationMode = TEXT("LookingDirection");
        }
        const bool bValidLocomotionMode = LocomotionMode == TEXT("Grounded") || LocomotionMode == TEXT("InAir") ||
            LocomotionMode == TEXT("Mantling") || LocomotionMode == TEXT("Ragdoll") || LocomotionMode == TEXT("Recovering");
        const bool bValidRotationMode = RotationMode == TEXT("VelocityDirection") ||
            RotationMode == TEXT("LookingDirection") || RotationMode == TEXT("Aiming");
        const bool bValidStance = Stance == TEXT("Standing") || Stance == TEXT("Crouching");
        return bValidLocomotionMode && bValidRotationMode && bValidStance;
    }

    EAlsPayloadResult ReportReadFailure(const EAlsPayloadResult Result, const TCHAR* Kind,
        const UObject& Object, FString& OutError)
    {
        if (Result == EAlsPayloadResult::Invalid)
        {
            OutError = FString::Printf(TEXT("Invalid reflected %s payload shape or value on %s."),
                Kind, *Object.GetClass()->GetPathName());
        }
        return Result;
    }

    bool HasAnyProperty(const UObject& Object, const std::initializer_list<FName> PropertyNames)
    {
        for (const FName PropertyName : PropertyNames)
        {
            if (Object.GetClass()->FindPropertyByName(PropertyName))
            {
                return true;
            }
        }
        return false;
    }

    bool HasCompleteEarlyBlendOutShape(const UObject& Object)
    {
        return HasAnyProperty(Object, {TEXT("BlendOutSeconds"), TEXT("BlendOutTime"), TEXT("BlendOutDuration")}) &&
            HasAnyProperty(Object, {TEXT("bCheckInput")}) &&
            HasAnyProperty(Object, {TEXT("bCheckLocomotionMode")}) &&
            HasAnyProperty(Object, {TEXT("bCheckRotationMode")}) &&
            HasAnyProperty(Object, {TEXT("bCheckStance")}) &&
            HasAnyProperty(Object, {TEXT("LocomotionMode"), TEXT("LocomotionModeEquals")}) &&
            HasAnyProperty(Object, {TEXT("RotationMode"), TEXT("RotationModeEquals")}) &&
            HasAnyProperty(Object, {TEXT("Stance"), TEXT("StanceEquals")});
    }

    EAlsPayloadResult BuildFootstepPayload(const UObject& Object, FJsonObject& Payload, FString& OutError)
    {
        FString Foot;
        const EAlsPayloadResult ReadResult = TryReadEnum(Object,
            {TEXT("Foot"), TEXT("FootstepFoot"), TEXT("FootBone")}, Foot);
        if (ReadResult != EAlsPayloadResult::Success)
        {
            return ReportReadFailure(ReadResult, TEXT("Footstep"), Object, OutError);
        }
        if (!NormalizeFootstep(Foot))
        {
            OutError = FString::Printf(TEXT("Invalid Footstep foot value on %s: %s."),
                *Object.GetClass()->GetPathName(), *Foot);
            return EAlsPayloadResult::Invalid;
        }
        Payload.SetStringField(TEXT("foot"), Foot);
        return EAlsPayloadResult::Success;
    }

    EAlsPayloadResult BuildSetActionPayload(const UObject& Object, FJsonObject& Payload, FString& OutError)
    {
        FString Action;
        const EAlsPayloadResult ReadResult = TryReadEnumOrGameplayTag(Object,
            {TEXT("Action"), TEXT("MovementAction")}, {TEXT("LocomotionAction")}, Action);
        if (ReadResult != EAlsPayloadResult::Success)
        {
            return ReportReadFailure(ReadResult, TEXT("SetAction"), Object, OutError);
        }
        if (!NormalizeSetAction(Action))
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
        const EAlsPayloadResult ReadResult = TryReadEnumOrGameplayTag(Object,
            {TEXT("Mode"), TEXT("GroundedEntryState")}, {TEXT("GroundedEntryMode")}, Mode);
        if (ReadResult != EAlsPayloadResult::Success)
        {
            return ReportReadFailure(ReadResult, TEXT("SetGroundedEntry"), Object, OutError);
        }
        if (!NormalizeGroundedEntry(Mode))
        {
            OutError = FString::Printf(TEXT("Invalid SetGroundedEntry mode value on %s: %s."), *Object.GetClass()->GetPathName(), *Mode);
            return EAlsPayloadResult::Invalid;
        }
        Payload.SetStringField(TEXT("mode"), Mode);
        return EAlsPayloadResult::Success;
    }

    EAlsPayloadResult BuildEarlyBlendOutPayload(const UObject& Object, FJsonObject& Payload, FString& OutError)
    {
        if (!HasCompleteEarlyBlendOutShape(Object))
        {
            return EAlsPayloadResult::Unavailable;
        }
        double BlendOutSeconds = 0.0;
        bool bCheckInput = false;
        bool bCheckLocomotionMode = false;
        bool bCheckRotationMode = false;
        bool bCheckStance = false;
        FString LocomotionMode;
        FString RotationMode;
        FString Stance;
        const EAlsPayloadResult BlendOutResult = TryReadNumber(Object,
            {TEXT("BlendOutSeconds"), TEXT("BlendOutTime"), TEXT("BlendOutDuration")}, BlendOutSeconds);
        const EAlsPayloadResult CheckInputResult = TryReadBool(Object, TEXT("bCheckInput"), bCheckInput);
        const EAlsPayloadResult CheckLocomotionResult = TryReadBool(Object,
            TEXT("bCheckLocomotionMode"), bCheckLocomotionMode);
        const EAlsPayloadResult CheckRotationResult = TryReadBool(Object,
            TEXT("bCheckRotationMode"), bCheckRotationMode);
        const EAlsPayloadResult CheckStanceResult = TryReadBool(Object, TEXT("bCheckStance"), bCheckStance);
        const EAlsPayloadResult LocomotionResult = TryReadEnumOrGameplayTag(Object,
            {TEXT("LocomotionMode")}, {TEXT("LocomotionModeEquals")}, LocomotionMode);
        const EAlsPayloadResult RotationResult = TryReadEnumOrGameplayTag(Object,
            {TEXT("RotationMode")}, {TEXT("RotationModeEquals")}, RotationMode);
        const EAlsPayloadResult StanceResult = TryReadEnumOrGameplayTag(Object,
            {TEXT("Stance")}, {TEXT("StanceEquals")}, Stance);
        const EAlsPayloadResult Results[] = {BlendOutResult, CheckInputResult, CheckLocomotionResult,
            CheckRotationResult, CheckStanceResult, LocomotionResult, RotationResult, StanceResult};
        bool bHasUnavailableField = false;
        for (const EAlsPayloadResult Result : Results)
        {
            if (Result == EAlsPayloadResult::Invalid)
            {
                return ReportReadFailure(Result, TEXT("EarlyBlendOut"), Object, OutError);
            }
            bHasUnavailableField |= Result == EAlsPayloadResult::Unavailable;
        }
        if (bHasUnavailableField)
        {
            return EAlsPayloadResult::Unavailable;
        }
        if (!FMath::IsFinite(BlendOutSeconds) || BlendOutSeconds < 0.0 ||
            !NormalizeEarlyBlendOutDomains(LocomotionMode, RotationMode, Stance))
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
        const EAlsPayloadResult ReadResult = TryReadNumber(Object, {TEXT("TranslationScale")}, TranslationScale);
        if (ReadResult != EAlsPayloadResult::Success)
        {
            return ReportReadFailure(ReadResult, TEXT("RootMotionScale"), Object, OutError);
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

    UObject* CreateSelfTestObject(const TCHAR* ClassPath, FString& OutError)
    {
        UClass* Class = FindObject<UClass>(nullptr, ClassPath);
        if (!Class)
        {
            Class = LoadObject<UClass>(nullptr, ClassPath);
        }
        if (!Class)
        {
            OutError = FString::Printf(TEXT("Unable to load timeline self-test class: %s."), ClassPath);
            return nullptr;
        }
        UObject* Object = NewObject<UObject>(GetTransientPackage(), Class);
        if (!Object)
        {
            OutError = FString::Printf(TEXT("Unable to construct timeline self-test class: %s."), ClassPath);
        }
        return Object;
    }

    bool SetSelfTestEnumValue(UObject& Object, const FName PropertyName, const int64 Value, FString& OutError)
    {
        FProperty* Property = Object.GetClass()->FindPropertyByName(PropertyName);
        void* ValueAddress = Property ? Property->ContainerPtrToValuePtr<void>(&Object) : nullptr;
        if (FEnumProperty* EnumProperty = CastField<FEnumProperty>(Property))
        {
            EnumProperty->GetUnderlyingProperty()->SetIntPropertyValue(ValueAddress, Value);
            return true;
        }
        if (FByteProperty* ByteProperty = CastField<FByteProperty>(Property); ByteProperty && ByteProperty->Enum)
        {
            ByteProperty->SetPropertyValue(ValueAddress, static_cast<uint8>(Value));
            return true;
        }
        OutError = FString::Printf(TEXT("Timeline self-test enum property is unavailable: %s.%s."),
            *Object.GetClass()->GetPathName(), *PropertyName.ToString());
        return false;
    }

    bool SetSelfTestGameplayTag(UObject& Object, const FName PropertyName, const FName TagName, FString& OutError)
    {
        FStructProperty* Property = FindFProperty<FStructProperty>(Object.GetClass(), PropertyName);
        if (!Property || !IsGameplayTagStruct(Property->Struct))
        {
            OutError = FString::Printf(TEXT("Timeline self-test GameplayTag property is unavailable: %s.%s."),
                *Object.GetClass()->GetPathName(), *PropertyName.ToString());
            return false;
        }
        FNameProperty* TagNameProperty = FindFProperty<FNameProperty>(Property->Struct, TEXT("TagName"));
        if (!TagNameProperty)
        {
            OutError = TEXT("Timeline self-test GameplayTag has no TagName field.");
            return false;
        }
        void* ValueAddress = Property->ContainerPtrToValuePtr<void>(&Object);
        TagNameProperty->SetPropertyValue_InContainer(ValueAddress, TagName);
        return true;
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

bool FAlsNotifyClassRegistry::RunSelfTest(int32& OutCaseCount, FString& OutError)
{
    constexpr int32 ExpectedCaseCount = 14;
    OutCaseCount = 0;
    OutError.Reset();

    if (CreateSha1(TEXT("\u8d44\u6e90|timeline|7|/Script/\u6d4b\u8bd5.\u7c7b")) !=
        TEXT("6c352ece91861e5a728baae0272834d99aad0a42"))
    {
        OutError = TEXT("Timeline self-test UTF-8 SHA-1 mismatch.");
        return false;
    }
    ++OutCaseCount;

    const FAlsEnumValueAlias ExpectedMovementActions[] = {
        {TEXT("NewEnumerator0"), TEXT("Mantling")},
        {TEXT("NewEnumerator1"), TEXT("Mantling")},
        {TEXT("NewEnumerator2"), TEXT("Rolling")},
        {TEXT("NewEnumerator3"), TEXT("GettingUp")},
        {TEXT("NewEnumerator4"), TEXT("None")},
    };
    for (const FAlsEnumValueAlias& Expected : ExpectedMovementActions)
    {
        FString Value;
        if (NormalizeEnumToken(MovementActionEnumPath, Expected.RawToken, Value) != EAlsPayloadResult::Success ||
            Value != Expected.CanonicalValue)
        {
            OutError = FString::Printf(TEXT("Timeline self-test Blueprint enum mismatch: raw=%s value=%s."),
                Expected.RawToken, *Value);
            return false;
        }
        ++OutCaseCount;
    }
    FString InvalidEnumValue;
    if (NormalizeEnumToken(MovementActionEnumPath, TEXT("NewEnumerator5"), InvalidEnumValue) !=
        EAlsPayloadResult::Invalid)
    {
        OutError = TEXT("Timeline self-test accepted the Blueprint enum sentinel.");
        return false;
    }
    ++OutCaseCount;

    FString UnknownEnumValue;
    if (NormalizeEnumToken(TEXT("/Script/Unknown.Future"), TEXT("Left"), UnknownEnumValue) !=
        EAlsPayloadResult::Unavailable)
    {
        OutError = TEXT("Timeline self-test typed an enum from an unknown object path.");
        return false;
    }
    ++OutCaseCount;

    UScriptStruct* GameplayTagStruct = FindObject<UScriptStruct>(nullptr, GameplayTagStructPath);
    UScriptStruct* VectorStruct = FindObject<UScriptStruct>(nullptr, TEXT("/Script/CoreUObject.Vector"));
    if (!IsGameplayTagStruct(GameplayTagStruct) || IsGameplayTagStruct(VectorStruct))
    {
        OutError = TEXT("Timeline self-test GameplayTag struct identity mismatch.");
        return false;
    }
    ++OutCaseCount;

    UObject* FootstepObject = CreateSelfTestObject(TEXT("/Script/ALS.AlsAnimNotify_FootstepEffects"), OutError);
    if (!FootstepObject)
    {
        return false;
    }
    TSharedRef<FJsonObject> FootstepPayload = MakeShared<FJsonObject>();
    if (BuildFootstepPayload(*FootstepObject, *FootstepPayload, OutError) != EAlsPayloadResult::Success ||
        FootstepPayload->GetStringField(TEXT("foot")) != TEXT("Left"))
    {
        OutError = TEXT("Timeline self-test failed to export a typed Footstep payload.");
        return false;
    }
    ++OutCaseCount;
    if (!SetSelfTestEnumValue(*FootstepObject, TEXT("FootBone"), 127, OutError))
    {
        return false;
    }
    TSharedRef<FJsonObject> InvalidFootstepPayload = MakeShared<FJsonObject>();
    FString InvalidFootstepError;
    if (BuildFootstepPayload(*FootstepObject, *InvalidFootstepPayload, InvalidFootstepError) !=
            EAlsPayloadResult::Invalid || InvalidFootstepError.IsEmpty())
    {
        OutError = TEXT("Timeline self-test accepted an invalid Footstep payload.");
        return false;
    }
    ++OutCaseCount;

    UObject* ActionObject = CreateSelfTestObject(TEXT("/Script/ALS.AlsAnimNotifyState_SetLocomotionAction"), OutError);
    if (!ActionObject || !SetSelfTestGameplayTag(*ActionObject, TEXT("LocomotionAction"),
        TEXT("Als.LocomotionAction.Ragdolling"), OutError))
    {
        return false;
    }
    TSharedRef<FJsonObject> ActionPayload = MakeShared<FJsonObject>();
    if (BuildSetActionPayload(*ActionObject, *ActionPayload, OutError) != EAlsPayloadResult::Success ||
        ActionPayload->GetStringField(TEXT("action")) != TEXT("Ragdolling"))
    {
        OutError = TEXT("Timeline self-test failed to export a typed SetAction payload.");
        return false;
    }
    ++OutCaseCount;

    UObject* EarlyBlendOutObject = CreateSelfTestObject(TEXT("/Script/ALS.AlsAnimNotifyState_EarlyBlendOut"), OutError);
    if (!EarlyBlendOutObject || !SetSelfTestGameplayTag(*EarlyBlendOutObject, TEXT("RotationModeEquals"),
        TEXT("Als.RotationMode.ViewDirection"), OutError))
    {
        return false;
    }
    TSharedRef<FJsonObject> EarlyBlendOutPayload = MakeShared<FJsonObject>();
    if (BuildEarlyBlendOutPayload(*EarlyBlendOutObject, *EarlyBlendOutPayload, OutError) != EAlsPayloadResult::Success ||
        EarlyBlendOutPayload->GetStringField(TEXT("rotationMode")) != TEXT("LookingDirection"))
    {
        OutError = TEXT("Timeline self-test failed to normalize a typed EarlyBlendOut payload.");
        return false;
    }
    ++OutCaseCount;
    if (!SetSelfTestGameplayTag(*EarlyBlendOutObject, TEXT("RotationModeEquals"),
        TEXT("Als.RotationMode.Future"), OutError))
    {
        return false;
    }
    TSharedRef<FJsonObject> InvalidEarlyBlendOutPayload = MakeShared<FJsonObject>();
    FString InvalidEarlyBlendOutError;
    if (BuildEarlyBlendOutPayload(*EarlyBlendOutObject, *InvalidEarlyBlendOutPayload, InvalidEarlyBlendOutError) !=
            EAlsPayloadResult::Invalid || InvalidEarlyBlendOutError.IsEmpty())
    {
        OutError = TEXT("Timeline self-test accepted an invalid EarlyBlendOut payload.");
        return false;
    }
    ++OutCaseCount;

    if (OutCaseCount != ExpectedCaseCount)
    {
        OutError = FString::Printf(TEXT("Timeline registry self-test case count mismatch: expected=%d actual=%d."),
            ExpectedCaseCount, OutCaseCount);
        return false;
    }
    return true;
}

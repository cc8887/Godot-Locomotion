#include "AlsMovementDirectionCommandlet.h"

#include "Animation/AnimInstance.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/Blueprint.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "UObject/StructOnScope.h"
#include "UObject/UnrealType.h"

namespace
{
FNumericProperty* Numeric(const UStruct* Owner, const TCHAR* Name)
{
    FProperty* Property = FindFProperty<FProperty>(Owner, Name);
    checkf(Property, TEXT("Missing property %s"), Name);
    if (auto* Enum = CastField<FEnumProperty>(Property)) return Enum->GetUnderlyingProperty();
    auto* Number = CastField<FNumericProperty>(Property);
    checkf(Number, TEXT("Non-numeric property %s"), Name);
    return Number;
}

void SetNumber(const UStruct* Owner, void* Container, const TCHAR* Name, double Value)
{
    FProperty* Property = FindFProperty<FProperty>(Owner, Name);
    checkf(Property, TEXT("Missing property %s"), Name);
    void* Address = Property->ContainerPtrToValuePtr<void>(Container);
    FNumericProperty* Number = Numeric(Owner, Name);
    if (Number->IsFloatingPoint()) Number->SetFloatingPointPropertyValue(Address, Value);
    else Number->SetIntPropertyValue(Address, static_cast<int64>(Value));
}

int64 GetReturn(UFunction* Function, void* Params)
{
    for (TFieldIterator<FProperty> It(Function); It; ++It)
    {
        if (!It->HasAnyPropertyFlags(CPF_OutParm)) continue;
        return Numeric(Function, *It->GetName())->GetSignedIntPropertyValue(It->ContainerPtrToValuePtr<void>(Params));
    }
    checkNoEntry();
    return -1;
}

int64 EnumValue(const UStruct* Owner, const TCHAR* PropertyName, const TCHAR* EntryName)
{
    FProperty* Property = FindFProperty<FProperty>(Owner, PropertyName);
    const UEnum* Enum = nullptr;
    if (const auto* Byte = CastField<FByteProperty>(Property)) Enum = Byte->Enum;
    if (const auto* Entry = CastField<FEnumProperty>(Property)) Enum = Entry->GetEnum();
    check(Enum);
    const int64 Value = Enum->GetValueByNameString(EntryName);
    checkf(Value != INDEX_NONE, TEXT("Missing enum entry %s"), EntryName);
    return Value;
}
}

UAlsMovementDirectionCommandlet::UAlsMovementDirectionCommandlet()
{
    IsClient = false;
    IsServer = false;
    IsEditor = true;
    LogToConsole = true;
}

int32 UAlsMovementDirectionCommandlet::Main(const FString& Params)
{
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    const UBlueprint* Blueprint = LoadObject<UBlueprint>(nullptr,
        TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"));
    if (!Blueprint || !Blueprint->GeneratedClass) return 2;
    USkeletalMeshComponent* Component = NewObject<USkeletalMeshComponent>(GetTransientPackage());
    UAnimInstance* Instance = NewObject<UAnimInstance>(Component, Blueprint->GeneratedClass);
    UFunction* Quadrant = Instance->FindFunctionChecked(TEXT("CalculateQuadrant"));
    UFunction* Movement = Instance->FindFunctionChecked(TEXT("CalculateMovementDirection"));
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("ALS V4 compiled AnimBP ProcessEvent; transient instance; function probes, not full animation traces"));

    TArray<TSharedPtr<FJsonValue>> Quadrants;
    const double Angles[] = {-180, -115.001, -115, -114.999, -110, -105, -90, -75.001, -75,
        -74.999, -70, -65, 0, 65, 70, 74.999, 75, 75.001, 90, 105, 110, 114.999, 115, 115.001, 180};
    for (int32 Current = 0; Current < 4; ++Current)
    for (double Angle : Angles)
    {
        FStructOnScope Scope(Quadrant);
        void* Data = Scope.GetStructMemory();
        SetNumber(Quadrant, Data, TEXT("Current"), Current);
        SetNumber(Quadrant, Data, TEXT("FR-Threshold"), 70);
        SetNumber(Quadrant, Data, TEXT("FL-Threshold"), -70);
        SetNumber(Quadrant, Data, TEXT("BR-Threshold"), 110);
        SetNumber(Quadrant, Data, TEXT("BL-Threshold"), -110);
        SetNumber(Quadrant, Data, TEXT("Buffer"), 5);
        SetNumber(Quadrant, Data, TEXT("Angle"), Angle);
        Instance->ProcessEvent(Quadrant, Data);
        const auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("current"), Current);
        Row->SetNumberField(TEXT("angle"), Angle);
        Row->SetNumberField(TEXT("direction"), GetReturn(Quadrant, Data));
        Quadrants.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("quadrants"), Quadrants);

    TArray<TSharedPtr<FJsonValue>> Movements;
    const double MovementAngles[] = {-180, -150, -116, -114, -90, -76, -60, 0, 60, 76, 90, 114, 116, 150, 180};
    const double AimYaws[] = {0, 123, -179};
    // Blueprint entry suffixes are stable names, not numeric enum values.
    const int64 RotationModes[] = {
        EnumValue(Instance->GetClass(), TEXT("RotationMode"), TEXT("NewEnumerator0")),
        EnumValue(Instance->GetClass(), TEXT("RotationMode"), TEXT("NewEnumerator1")),
        EnumValue(Instance->GetClass(), TEXT("RotationMode"), TEXT("NewEnumerator3"))};
    for (int32 Current = 0; Current < 4; ++Current)
    for (int32 Gait = 0; Gait < 3; ++Gait)
    for (int32 ModeIndex = 0; ModeIndex < 3; ++ModeIndex)
    for (double AimYaw : AimYaws)
    for (double Angle : MovementAngles)
    {
        SetNumber(Instance->GetClass(), Instance, TEXT("MovementDirection"), Current);
        SetNumber(Instance->GetClass(), Instance, TEXT("Gait"), Gait);
        SetNumber(Instance->GetClass(), Instance, TEXT("RotationMode"), RotationModes[ModeIndex]);
        auto* Velocity = FindFProperty<FStructProperty>(Instance->GetClass(), TEXT("Velocity"));
        check(Velocity && Velocity->Struct == TBaseStructure<FVector>::Get());
        *Velocity->ContainerPtrToValuePtr<FVector>(Instance) = FRotator(0, AimYaw + Angle, 0).Vector() * 200;
        auto* Aim = FindFProperty<FStructProperty>(Instance->GetClass(), TEXT("AimingRotation"));
        check(Aim && Aim->Struct == TBaseStructure<FRotator>::Get());
        *Aim->ContainerPtrToValuePtr<FRotator>(Instance) = FRotator(0, AimYaw, 0);
        FStructOnScope Scope(Movement);
        Instance->ProcessEvent(Movement, Scope.GetStructMemory());
        const auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("current"), Current);
        Row->SetNumberField(TEXT("gait"), Gait);
        Row->SetNumberField(TEXT("rotationMode"), ModeIndex);
        Row->SetNumberField(TEXT("sourceRotationMode"), RotationModes[ModeIndex]);
        Row->SetNumberField(TEXT("aimYaw"), AimYaw);
        Row->SetNumberField(TEXT("angle"), Angle);
        Row->SetNumberField(TEXT("direction"), GetReturn(Movement, Scope.GetStructMemory()));
        Movements.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("movements"), Movements);
    FString Text;
    const auto Writer = TJsonWriterFactory<>::Create(&Text);
    if (!FJsonSerializer::Serialize(Root, Writer) || !FFileHelper::SaveStringToFile(Text, *OutputPath)) return 3;
    UE_LOG(LogTemp, Display, TEXT("ALS_MOVEMENT_DIRECTION_OK quadrants=%d movements=%d assets_saved=0"), Quadrants.Num(), Movements.Num());
    return 0;
}

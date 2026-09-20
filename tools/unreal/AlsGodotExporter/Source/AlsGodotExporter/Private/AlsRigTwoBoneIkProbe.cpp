#include "AlsAnimationGraphLibrary.h"

#include "Dom/JsonObject.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Rigs/RigHierarchy.h"
#include "Rigs/RigHierarchyController.h"
#include "Serialization/JsonSerializer.h"
#include "Units/Highlevel/Hierarchy/RigUnit_TwoBoneIKSimple.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsRigIkProbe
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& Value)
{
    return {MakeShared<FJsonValueNumber>(Value.X), MakeShared<FJsonValueNumber>(Value.Y), MakeShared<FJsonValueNumber>(Value.Z)};
}
TSharedPtr<FJsonValue> P(const FTransform& Value)
{
    auto Result = MakeShared<FJsonObject>();
    Result->SetArrayField(TEXT("p"), V(Value.GetLocation()));
    Result->SetArrayField(TEXT("s"), V(Value.GetScale3D()));
    const FQuat Q = Value.GetRotation();
    Result->SetArrayField(TEXT("q"), {MakeShared<FJsonValueNumber>(Q.X), MakeShared<FJsonValueNumber>(Q.Y),
        MakeShared<FJsonValueNumber>(Q.Z), MakeShared<FJsonValueNumber>(Q.W)});
    return MakeShared<FJsonValueObject>(Result);
}
}

bool UAlsAnimationGraphLibrary::ExportRigTwoBoneIk(const FString& OutputPath)
{
    using namespace AlsRigIkProbe;
    if (FPaths::IsRelative(OutputPath) || IFileManager::Get().FileExists(*OutputPath)) return false;
    TArray<TSharedPtr<FJsonValue>> Rows;
    for (const bool Left : {false, true})
    for (const float Weight : {0.f, 1e-9f, 1e-7f, .25f, .75f, 1.f, 1.2f})
    for (const float SecondaryWeight : {0.f, .35f, 1.f})
    for (int32 Scenario = 0; Scenario < 6; ++Scenario)
    for (const bool Propagate : {false, true})
    {
        TStrongObjectPtr<URigHierarchy> Hierarchy(NewObject<URigHierarchy>(GetTransientPackage()));
        auto* Controller = Hierarchy->GetController(true);
        TArray<FRigElementKey> Bones;
        const TArray<int32> Parents{-1, 0, 1, 2, 0, 1};
        const TArray<FVector> Positions{FVector(7, -5, 90), FVector(20, -5, 45), FVector(0, -5, 0),
            FVector(12, -5, 0), FVector(10, 3, 80), FVector(23, 5, 42)};
        for (int32 Index = 0; Index < Parents.Num(); ++Index)
        {
            const FVector Scale = Scenario >= 4 ? FVector(1 + Index * .03, .9, 1.1) : FVector::OneVector;
            const FTransform Initial(FRotator(Index * 5, 15 - Index * 12, -Index * 3).Quaternion(), Positions[Index], Scale);
            Bones.Add(Controller->AddBone(*FString::Printf(TEXT("bone%d"), Index), Parents[Index] < 0 ? FRigElementKey{} : Bones[Parents[Index]], Initial));
        }
        TArray<TSharedPtr<FJsonValue>> Initial;
        for (const auto Bone : Bones) Initial.Add(P(Hierarchy->GetInitialGlobalTransform(Bone)));
        for (int32 Index = 0; Index < Bones.Num(); ++Index)
        {
            const FVector Scale = Scenario >= 4 ? FVector(1.2, 1.1 + Index * .01, .95) : FVector::OneVector;
            Hierarchy->SetGlobalTransform(Bones[Index], FTransform(FRotator(13 + Index * 5, -35 + Index * 14, 7).Quaternion(),
                Positions[Index] + FVector(2 * Index, 1, 0), Scale), false, false);
        }
        TArray<TSharedPtr<FJsonValue>> Before;
        for (const auto Bone : Bones) Before.Add(P(Hierarchy->GetGlobalTransform(Bone)));
        FRigUnit_TwoBoneIKSimplePerItem Unit;
        Unit.ItemA = Bones[0]; Unit.ItemB = Bones[1]; Unit.EffectorItem = Bones[2];
        Unit.PrimaryAxis = Left ? -FVector::ForwardVector : FVector::ForwardVector;
        Unit.SecondaryAxis = Left ? FVector::RightVector : -FVector::RightVector;
        Unit.SecondaryAxisWeight = SecondaryWeight;
        Unit.Weight = Weight;
        Unit.bPropagateToChildren = Propagate;
        Unit.PoleVectorKind = Scenario == 5 ? EControlRigVectorKind::Direction : EControlRigVectorKind::Location;
        Unit.PoleVector = FVector(60, 35, 55);
        Unit.Effector = FTransform(FRotator(20, 80, -15).Quaternion(), FVector(28, -10, 15), FVector(1.05, 1.1, .9));
        if (Scenario == 0) Unit.Effector.SetLocation(Hierarchy->GetGlobalTransform(Bones[0]).GetLocation());
        if (Scenario == 2 || Scenario == 3) Unit.Effector.SetLocation(FVector(160, -5, 90));
        if (Scenario == 2) Unit.PoleVector = Unit.Effector.GetLocation();
        Unit.bEnableStretch = Scenario == 3;
        Unit.ItemALength = Scenario == 1 ? 43.f : 0.f;
        Unit.ItemBLength = Scenario == 1 ? 47.f : 0.f;
        const bool HasSpace = Scenario >= 4;
        FTransform Space = FTransform::Identity;
        if (HasSpace)
        {
            Space = FTransform(FRotator(10, 45, -20).Quaternion(), FVector(10, -7, 30), FVector(2, 3, 4));
            Unit.PoleVectorSpace = Controller->AddBone(TEXT("pole_space"), {}, Space);
        }
        auto Row = MakeShared<FJsonObject>();
        Row->SetArrayField(TEXT("initial"), Initial);
        Row->SetArrayField(TEXT("before"), Before);
        Row->SetArrayField(TEXT("primary"), V(Unit.PrimaryAxis));
        Row->SetArrayField(TEXT("secondary"), V(Unit.SecondaryAxis));
        Row->SetNumberField(TEXT("secondaryWeight"), Unit.SecondaryAxisWeight);
        Row->SetNumberField(TEXT("weight"), Weight);
        Row->SetNumberField(TEXT("lengthA"), Unit.ItemALength);
        Row->SetNumberField(TEXT("lengthB"), Unit.ItemBLength);
        Row->SetBoolField(TEXT("stretch"), Unit.bEnableStretch);
        Row->SetBoolField(TEXT("propagate"), Propagate);
        Row->SetField(TEXT("target"), P(Unit.Effector));
        Row->SetArrayField(TEXT("pole"), V(Unit.PoleVector));
        Row->SetBoolField(TEXT("hasSpace"), HasSpace);
        Row->SetBoolField(TEXT("direction"), Unit.PoleVectorKind == EControlRigVectorKind::Direction);
        Row->SetField(TEXT("space"), P(Space));
        FControlRigExecuteContext Context;
        Context.Hierarchy = Hierarchy.Get();
        Unit.Execute(Context);
        TArray<TSharedPtr<FJsonValue>> After;
        for (const auto Bone : Bones) After.Add(P(Hierarchy->GetGlobalTransform(Bone)));
        Row->SetArrayField(TEXT("after"), After);
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE5.9 actual FRigUnit_TwoBoneIKSimplePerItem; transient hierarchy; no AnimBP"));
    Root->SetArrayField(TEXT("parents"), {MakeShared<FJsonValueNumber>(-1), MakeShared<FJsonValueNumber>(0),
        MakeShared<FJsonValueNumber>(1), MakeShared<FJsonValueNumber>(2), MakeShared<FJsonValueNumber>(0), MakeShared<FJsonValueNumber>(1)});
    Root->SetArrayField(TEXT("rows"), Rows);
    FString Json;
    return FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Json)) &&
        FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

#include "AlsAnimationGraphLibrary.h"

#include "Dom/JsonObject.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Nodes/AlsRigUnit_ApplyFootOffsetLocation.h"
#include "Rigs/RigHierarchy.h"
#include "Rigs/RigHierarchyController.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsFootLocationReplay
{
FVector ReadVector(const TSharedPtr<FJsonObject>& Object)
{
    return FVector(Object->GetNumberField(TEXT("X")), Object->GetNumberField(TEXT("Y")), Object->GetNumberField(TEXT("Z")));
}
TArray<TSharedPtr<FJsonValue>> Vector(const FVector& Value)
{
    return {MakeShared<FJsonValueNumber>(Value.X), MakeShared<FJsonValueNumber>(Value.Y), MakeShared<FJsonValueNumber>(Value.Z)};
}
}

bool UAlsAnimationGraphLibrary::ReplayFootOffsetLocations(const FString& RequestPath, const FString& OutputPath)
{
    using namespace AlsFootLocationReplay;
    if (FPaths::IsRelative(RequestPath) || FPaths::IsRelative(OutputPath) || IFileManager::Get().FileExists(*OutputPath)) return false;
    FString Text;
    TArray<TSharedPtr<FJsonValue>> Inputs;
    if (!FFileHelper::LoadFileToString(Text, *RequestPath) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Inputs) || Inputs.IsEmpty()) return false;

    TStrongObjectPtr<URigHierarchy> Hierarchy(NewObject<URigHierarchy>(GetTransientPackage()));
    auto* Controller = Hierarchy->GetController(true);
    const auto Pelvis = Controller->AddBone(TEXT("pelvis"), {}, FTransform(FVector(0, 0, 100)));
    const auto Thigh = Controller->AddBone(TEXT("thigh"), Pelvis, FTransform(FVector(0, 0, 90)));
    FControlRigExecuteContext Context;
    Context.Hierarchy = Hierarchy.Get();
    FAlsRigUnit_ApplyFootOffsetLocation Units[2];
    for (auto& Unit : Units) { Unit.PelvisItem = Pelvis; Unit.ThighItem = Thigh; Unit.Initialize(); }

    TArray<TSharedPtr<FJsonValue>> Rows;
    int32 ExpectedFrame = 1;
    for (const auto& Value : Inputs)
    {
        const auto Source = Value->AsObject();
        if (!Source.IsValid() || Source->GetIntegerField(TEXT("Frame")) != ExpectedFrame++) return false;
        const auto Feet = Source->GetObjectField(TEXT("RefactoredRigFeet"));
        const bool Evaluated = Feet->GetBoolField(TEXT("LocationEvaluated"));
        auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("frame"), ExpectedFrame - 1);
        Row->SetBoolField(TEXT("evaluated"), Evaluated);
        for (int32 Side = 0; Side < 2; ++Side)
        {
            auto& Unit = Units[Side];
            if (Evaluated)
            {
                const auto Input = Feet->GetObjectField(Side == 0 ? TEXT("LeftInput") : TEXT("RightInput"));
                const FVector Target = ReadVector(Input->GetObjectField(TEXT("TargetLocation")));
                const FVector ThighLocation = ReadVector(Input->GetObjectField(TEXT("ThighLocation")));
                if (Target.ContainsNaN() || ThighLocation.ContainsNaN()) return false;
                Hierarchy->SetGlobalTransform(Pelvis, FTransform(FVector(0, 0, Input->GetNumberField(TEXT("PelvisZ")))), false, false);
                Hierarchy->SetGlobalTransform(Thigh, FTransform(ThighLocation), false, false);
                Context.SetDeltaTime(Input->GetNumberField(TEXT("DeltaTime")));
                Unit.FootTargetLocation = Target;
                Unit.FootOffsetLocationZ = Input->GetNumberField(TEXT("OffsetZ"));
                Unit.PelvisOffset = Input->GetNumberField(TEXT("PelvisOffset"));
                Unit.LegLength = Input->GetNumberField(TEXT("LegLength"));
                Unit.MinPelvisToFootDistanceZ = Input->GetNumberField(TEXT("MinPelvisToFootDistance"));
                Unit.MaxLegStretchRatio = Input->GetNumberField(TEXT("MaxLegStretchRatio"));
                Unit.OffsetInterpolationFrequency = Input->GetNumberField(TEXT("Frequency"));
                Unit.OffsetInterpolationDampingRatio = Input->GetNumberField(TEXT("DampingRatio"));
                Unit.OffsetInterpolationTargetVelocityAmount = Input->GetNumberField(TEXT("TargetVelocityAmount"));
                Unit.Execute(Context);
            }
            auto Result = MakeShared<FJsonObject>();
            Result->SetArrayField(TEXT("location"), Vector(Unit.FootLocation));
            Result->SetNumberField(TEXT("offset"), Unit.OffsetLocationZ);
            Result->SetNumberField(TEXT("velocity"), Unit.OffsetSpringState.Velocity);
            Result->SetNumberField(TEXT("previousTarget"), Unit.OffsetSpringState.PreviousTarget);
            Result->SetBoolField(TEXT("valid"), Unit.OffsetSpringState.bStateValid);
            Row->SetObjectField(Side == 0 ? TEXT("left") : TEXT("right"), Result);
        }
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("FAlsRigUnit_ApplyFootOffsetLocation.Execute; real Godot platform input; transient hierarchy; no asset saves"));
    Root->SetArrayField(TEXT("frames"), Rows);
    FString Json;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Json))) return false;
    return FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

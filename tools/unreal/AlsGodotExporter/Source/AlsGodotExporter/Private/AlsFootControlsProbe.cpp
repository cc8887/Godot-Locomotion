#include "AlsAnimationGraphLibrary.h"

#include "Dom/JsonObject.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Nodes/AlsRigUnit_ApplyFootOffsetLocation.h"
#include "Nodes/AlsRigUnit_ApplyFootOffsetRotation.h"
#include "Nodes/AlsRigUnits.h"
#include "Rigs/RigHierarchy.h"
#include "Rigs/RigHierarchyController.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsFootControlsProbe
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& Value)
{
    return {MakeShared<FJsonValueNumber>(Value.X), MakeShared<FJsonValueNumber>(Value.Y), MakeShared<FJsonValueNumber>(Value.Z)};
}
TArray<TSharedPtr<FJsonValue>> Q(const FQuat& Value)
{
    return {MakeShared<FJsonValueNumber>(Value.X), MakeShared<FJsonValueNumber>(Value.Y),
        MakeShared<FJsonValueNumber>(Value.Z), MakeShared<FJsonValueNumber>(Value.W)};
}
}

bool UAlsAnimationGraphLibrary::ExportRefactoredFootControls(const FString& OutputPath)
{
    using namespace AlsFootControlsProbe;
    if (FPaths::IsRelative(OutputPath) || IFileManager::Get().FileExists(*OutputPath)) return false;
    TArray<TSharedPtr<FJsonValue>> Cases;
    for (const int32 Hz : {30, 60, 120})
    for (const float Damping : {0.0f, 0.5f, 1.0f, 2.0f})
    {
        TStrongObjectPtr<URigHierarchy> Hierarchy(NewObject<URigHierarchy>(GetTransientPackage()));
        auto* Controller = Hierarchy->GetController(true);
        const auto Pelvis = Controller->AddBone(TEXT("pelvis"), {}, FTransform(FVector(0, 0, 100)));
        const auto Thigh = Controller->AddBone(TEXT("thigh"), Pelvis, FTransform(FVector(0, 0, 90)));
        const FQuat InitialCalf = FRotator(12, 23, -7).Quaternion();
        const FQuat InitialFoot = InitialCalf * FRotator(-15, 8, 4).Quaternion();
        const auto Calf = Controller->AddBone(TEXT("calf"), Thigh, FTransform(InitialCalf, FVector(15, 0, 45)));
        const auto Foot = Controller->AddBone(TEXT("foot"), Calf, FTransform(InitialFoot, FVector(0, 0, 0)));
        FControlRigExecuteContext Context;
        Context.Hierarchy = Hierarchy.Get();
        FAlsRigUnit_ApplyFootOffsetLocation Location;
        Location.PelvisItem = Pelvis;
        Location.ThighItem = Thigh;
        Location.OffsetInterpolationDampingRatio = Damping;
        Location.Initialize();
        FAlsRigUnit_ApplyFootOffsetRotation Rotation;
        Rotation.CalfItem = Calf;
        Rotation.FootItem = Foot;
        Rotation.Initialize();
        FAlsRigUnit_CalculatePoleVector Pole;
        Pole.ItemA = Thigh;
        Pole.ItemB = Calf;
        Pole.ItemC = Foot;
        FAlsRigVMFunction_DamperExactVector Smooth;
        Smooth.HalfLife = .05f;
        Smooth.Initialize();
        auto Case = MakeShared<FJsonObject>();
        Case->SetNumberField(TEXT("hz"), Hz);
        Case->SetNumberField(TEXT("damping"), Damping);
        Case->SetArrayField(TEXT("initialCalf"), Q(InitialCalf));
        Case->SetArrayField(TEXT("initialFoot"), Q(InitialFoot));
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 0; Frame < 120; ++Frame)
        {
            const bool Reset = Frame == 80;
            if (Reset) { Location.Initialize(); Rotation.Initialize(); Smooth.Initialize(); Pole = FAlsRigUnit_CalculatePoleVector(); Pole.ItemA = Thigh; Pole.ItemB = Calf; Pole.ItemC = Foot; }
            const float Dt = Frame == 2 || Frame == 48 ? 0.f : 1.f / Hz;
            Context.SetDeltaTime(Dt);
            const double Phase = static_cast<double>(Frame) / Hz;
            const double Moving = Frame < 40 ? 0.0 : Frame < 80 ? 1.0 : 0.5;
            const FVector ThighLocation(3 * FMath::Sin(Phase), -5, 90);
            FVector CalfLocation(15 * FMath::Cos(Phase * 2), -5, 45);
            FVector FootLocation(0, -5, 0);
            if (Frame == 0 || Frame >= 24 && Frame <= 26) CalfLocation = ThighLocation;
            if (Frame == 27) CalfLocation = (ThighLocation + FootLocation) * .5;
            if (Frame == 28) FootLocation = ThighLocation;
            const FQuat CalfRotation = FRotator(10 * FMath::Sin(Phase), 50 * FMath::Cos(Phase), -12).Quaternion();
            const FQuat FootRotation = CalfRotation * FRotator(-10, Frame % 2 ? 65 : -45, 4).Quaternion();
            Hierarchy->SetGlobalTransform(Pelvis, FTransform(FVector(0, 0, 100)), false, false);
            Hierarchy->SetGlobalTransform(Thigh, FTransform(ThighLocation), false, false);
            Hierarchy->SetGlobalTransform(Calf, FTransform(CalfRotation, CalfLocation), false, false);
            Hierarchy->SetGlobalTransform(Foot, FTransform(FootRotation, FootLocation), false, false);
            Location.FootTargetLocation = FVector(Frame > 90 ? 140 : 20, -5, 6 * FMath::Sin(Phase * 5));
            Location.FootOffsetLocationZ = Frame < 20 ? 15 : Frame < 70 ? -35 : 120;
            Location.PelvisOffset = -20;
            Location.LegLength = Frame == 119 ? 0 : 95;
            Location.MinPelvisToFootDistanceZ = static_cast<float>(FMath::Lerp(20.0, 50.0, Moving));
            Location.OffsetInterpolationFrequency = Frame >= 95 && Frame <= 100 ? 0 : 12;
            Location.OffsetInterpolationTargetVelocityAmount = Frame < 60 ? 0 : .75f;
            Rotation.FootTargetRotation = FRotator(35 * FMath::Sin(Phase), Frame % 2 ? 90 : -70, 25).Quaternion();
            Rotation.FootOffsetNormal = FVector(.3 * FMath::Sin(Phase * 3), .2 * FMath::Cos(Phase), 1).GetSafeNormal();
            if (Frame == 50) Rotation.FootOffsetNormal = FVector::ZeroVector;
            if (Frame == 51) Rotation.FootOffsetNormal = -FVector::UpVector;
            Rotation.OffsetInterpolationHalfLife = Frame >= 50 && Frame <= 52 ? 0 : .1f;
            Rotation.Swing2LimitAngle = FFloatInterval(static_cast<float>(FMath::Lerp(-15.0, 0.0, Moving)), static_cast<float>(FMath::Lerp(5.0, 0.0, Moving)));
            auto Row = MakeShared<FJsonObject>();
            Row->SetBoolField(TEXT("reset"), Reset);
            Row->SetNumberField(TEXT("dt"), Dt);
            Row->SetNumberField(TEXT("moving"), Moving);
            Row->SetArrayField(TEXT("thigh"), V(ThighLocation));
            Row->SetArrayField(TEXT("calf"), V(CalfLocation));
            Row->SetArrayField(TEXT("foot"), V(FootLocation));
            Row->SetArrayField(TEXT("calfRotation"), Q(CalfRotation));
            Row->SetArrayField(TEXT("footRotation"), Q(FootRotation));
            Row->SetArrayField(TEXT("targetRotation"), Q(Rotation.FootTargetRotation));
            Row->SetArrayField(TEXT("normal"), V(Rotation.FootOffsetNormal));
            Row->SetNumberField(TEXT("halfLife"), Rotation.OffsetInterpolationHalfLife);
            Row->SetArrayField(TEXT("targetLocation"), V(Location.FootTargetLocation));
            Row->SetNumberField(TEXT("offsetZ"), Location.FootOffsetLocationZ);
            Row->SetNumberField(TEXT("legLength"), Location.LegLength);
            Row->SetNumberField(TEXT("frequency"), Location.OffsetInterpolationFrequency);
            Row->SetNumberField(TEXT("targetVelocityAmount"), Location.OffsetInterpolationTargetVelocityAmount);
            Location.Execute(Context);
            Rotation.Execute(Context);
            Pole.Execute(Context);
            Smooth.Target = Pole.ItemBLocation + Pole.PoleDirection * 40.0;
            Smooth.Execute(Context);
            Row->SetArrayField(TEXT("resultLocation"), V(Location.FootLocation));
            Row->SetNumberField(TEXT("resultOffsetZ"), Location.OffsetLocationZ);
            Row->SetNumberField(TEXT("springVelocity"), Location.OffsetSpringState.Velocity);
            Row->SetNumberField(TEXT("springPreviousTarget"), Location.OffsetSpringState.PreviousTarget);
            Row->SetBoolField(TEXT("springValid"), Location.OffsetSpringState.bStateValid);
            Row->SetArrayField(TEXT("resultRotation"), Q(Rotation.FootRotation));
            Row->SetArrayField(TEXT("resultNormal"), V(Rotation.OffsetNormal));
            Row->SetBoolField(TEXT("poleSuccess"), Pole.bSuccess);
            Row->SetArrayField(TEXT("poleB"), V(Pole.ItemBLocation));
            Row->SetArrayField(TEXT("poleProjection"), V(Pole.ItemBProjectionLocation));
            Row->SetArrayField(TEXT("poleDirection"), V(Pole.PoleDirection));
            Row->SetArrayField(TEXT("smoothedPole"), V(Smooth.Current));
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        Case->SetArrayField(TEXT("frames"), Frames);
        Cases.Add(MakeShared<FJsonValueObject>(Case));
    }
    auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("ALS Refactored actual Rig Units; isolated input hierarchy; not full AnimBP/ControlRig evaluation"));
    Root->SetArrayField(TEXT("cases"), Cases);
    FString Json;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Json))) return false;
    return FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

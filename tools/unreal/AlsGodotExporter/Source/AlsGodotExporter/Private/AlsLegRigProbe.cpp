#include "AlsAnimationGraphLibrary.h"
#include "Dom/JsonObject.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Nodes/AlsRigUnit_ApplyFootOffsetLocation.h"
#include "Nodes/AlsRigUnit_ApplyFootOffsetRotation.h"
#include "Nodes/AlsRigUnit_ChainLength.h"
#include "Nodes/AlsRigUnits.h"
#include "Rigs/RigHierarchy.h"
#include "Rigs/RigHierarchyController.h"
#include "Serialization/JsonSerializer.h"
#include "Units/Hierarchy/RigUnit_SetTransform.h"
#include "Units/Highlevel/Hierarchy/RigUnit_TwoBoneIKSimple.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsLegRigProbe
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& A)
{ return {MakeShared<FJsonValueNumber>(A.X), MakeShared<FJsonValueNumber>(A.Y), MakeShared<FJsonValueNumber>(A.Z)}; }
TArray<TSharedPtr<FJsonValue>> Q(const FQuat& A)
{ return {MakeShared<FJsonValueNumber>(A.X), MakeShared<FJsonValueNumber>(A.Y), MakeShared<FJsonValueNumber>(A.Z), MakeShared<FJsonValueNumber>(A.W)}; }
TSharedPtr<FJsonValue> P(const FTransform& A)
{
    auto O = MakeShared<FJsonObject>(); O->SetArrayField(TEXT("p"), V(A.GetLocation()));
    O->SetArrayField(TEXT("q"), Q(A.GetRotation())); O->SetArrayField(TEXT("s"), V(A.GetScale3D()));
    return MakeShared<FJsonValueObject>(O);
}
}

bool UAlsAnimationGraphLibrary::ExportLegRig(const FString& OutputPath)
{
    using namespace AlsLegRigProbe;
    if (FPaths::IsRelative(OutputPath) || IFileManager::Get().FileExists(*OutputPath)) return false;
    TArray<TSharedPtr<FJsonValue>> Cases;
    for (const int32 Hz : {30, 60, 120})
    for (const bool Left : {false, true})
    for (const bool Scaled : {false, true})
    {
        TStrongObjectPtr<URigHierarchy> H(NewObject<URigHierarchy>(GetTransientPackage()));
        auto* C = H->GetController(true);
        const TArray<int32> Parents{-1, 0, 1, 2, 3, 1};
        const TArray<FVector> Positions{FVector(0, 0, 103), FVector(0, -7, 96), FVector(15, -7, 56),
            FVector(0, -7, 13.5), FVector(15, -7, 13.5), FVector(0, 3, 86)};
        TArray<FRigElementKey> Bones;
        TArray<TSharedPtr<FJsonValue>> Initial;
        for (int32 I = 0; I < Positions.Num(); ++I)
        {
            Bones.Add(C->AddBone(*FString::Printf(TEXT("bone%d"), I), Parents[I] < 0 ? FRigElementKey{} : Bones[Parents[I]],
                FTransform(FRotator(I * 3, I * 8, -I * 4).Quaternion(), Positions[I], Scaled ? FVector(1.1, .9, 1.2) : FVector::OneVector)));
            Initial.Add(P(H->GetInitialGlobalTransform(Bones[I])));
        }
        FControlRigExecuteContext Context; Context.Hierarchy = H.Get();
        FAlsRigUnit_ChainLength Chain; Chain.AncestorItem = Bones[1]; Chain.DescendantItem = Bones[3]; Chain.bInitial = true; Chain.Execute(Context);
        FAlsRigUnit_ApplyFootOffsetLocation Location;
        FAlsRigUnit_ApplyFootOffsetRotation Rotation;
        FAlsRigUnit_CalculatePoleVector Pole;
        FAlsRigVMFunction_DamperExactVector Smooth;
        auto ResetUnits = [&]()
        {
            Location = FAlsRigUnit_ApplyFootOffsetLocation(); Location.PelvisItem = Bones[0]; Location.ThighItem = Bones[1]; Location.Initialize();
            Rotation = FAlsRigUnit_ApplyFootOffsetRotation(); Rotation.CalfItem = Bones[2]; Rotation.FootItem = Bones[3]; Rotation.Initialize();
            Pole = FAlsRigUnit_CalculatePoleVector(); Pole.ItemA = Bones[1]; Pole.ItemB = Bones[2]; Pole.ItemC = Bones[3];
            Smooth = FAlsRigVMFunction_DamperExactVector(); Smooth.HalfLife = .05f; Smooth.Initialize();
        };
        ResetUnits();
        FRigUnit_TwoBoneIKSimplePerItem Ik;
        Ik.ItemA = Bones[1]; Ik.ItemB = Bones[2]; Ik.EffectorItem = Bones[3];
        Ik.PrimaryAxis = Left ? -FVector::ForwardVector : FVector::ForwardVector;
        Ik.SecondaryAxis = Left ? FVector::RightVector : -FVector::RightVector;
        Ik.bEnableStretch = false; Ik.bPropagateToChildren = true;
        FRigUnit_SetRotation Write; Write.Item = Bones[3]; Write.Space = ERigVMTransformSpace::GlobalSpace; Write.bPropagateToChildren = true;
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 0; Frame < 120; ++Frame)
        {
            const bool Reset = Frame == 70; if (Reset) ResetUnits();
            const bool Valid = !(Frame >= 40 && Frame < 45);
            const float Dt = Frame == 2 || Frame == 73 ? 0.f : 1.f / Hz; Context.SetDeltaTime(Dt);
            const double Phase = static_cast<double>(Frame) / Hz;
            const double Moving = Frame < 30 ? 0.0 : Frame < 60 ? 1.0 : Frame < 90 ? .5 : 1.2;
            const float Weights[]{0.f, 1.e-8f, .25f, .75f, 1.f, 1.2f};
            Ik.Weight = Weights[(Frame / 5) % 6]; Write.Weight = Ik.Weight;
            TArray<TSharedPtr<FJsonValue>> Before;
            for (int32 I = 0; I < Bones.Num(); ++I)
            {
                FVector Pos = Positions[I] + FVector(5 * FMath::Sin(Phase * 2 + I), 2 * FMath::Cos(Phase), -8);
                if ((Frame == 0 || Frame == 26) && I == 2) Pos = H->GetGlobalTransform(Bones[1]).GetLocation();
                H->SetGlobalTransform(Bones[I], FTransform(FRotator(8 + I * 5, 35 * FMath::Sin(Phase) + I * 12, -6).Quaternion(),
                    Pos, Scaled ? FVector(1.15, .95, 1.05) : FVector::OneVector), false, false);
                Before.Add(P(H->GetGlobalTransform(Bones[I])));
            }
            Location.FootTargetLocation = FVector(Frame > 100 ? 140 : 20, -7, 13.5 + 8 * FMath::Sin(Phase * 3));
            Location.FootOffsetLocationZ = Frame < 20 ? 12 : Frame < 70 ? -32 : 80;
            Location.PelvisOffset = -8; Location.LegLength = Chain.Length;
            Location.MinPelvisToFootDistanceZ = static_cast<float>(FMath::Lerp(20.0, 50.0, Moving));
            Rotation.FootTargetRotation = FRotator(20 * FMath::Sin(Phase), Frame < 60 ? 70 : -65, 12).Quaternion();
            Rotation.FootOffsetNormal = FVector(.3 * FMath::Sin(Phase), .2, 1).GetSafeNormal();
            Rotation.Swing2LimitAngle = FFloatInterval(static_cast<float>(FMath::Lerp(-15.0, 0.0, Moving)), static_cast<float>(FMath::Lerp(5.0, 0.0, Moving)));
            auto Row = MakeShared<FJsonObject>();
            Row->SetArrayField(TEXT("before"), Before); Row->SetBoolField(TEXT("reset"), Reset); Row->SetBoolField(TEXT("valid"), Valid);
            Row->SetNumberField(TEXT("dt"), Dt); Row->SetNumberField(TEXT("moving"), Moving); Row->SetNumberField(TEXT("weight"), Ik.Weight);
            Row->SetArrayField(TEXT("target"), V(Location.FootTargetLocation)); Row->SetArrayField(TEXT("targetRotation"), Q(Rotation.FootTargetRotation));
            Row->SetNumberField(TEXT("offset"), Location.FootOffsetLocationZ); Row->SetArrayField(TEXT("normal"), V(Rotation.FootOffsetNormal));
            if (Valid)
            {
                Location.Execute(Context);
                Pole.Execute(Context); Smooth.Target = Pole.ItemBLocation + Pole.PoleDirection * 40.; Smooth.Execute(Context);
                Ik.Effector = FTransform(Rotation.FootTargetRotation, Location.FootLocation); Ik.PoleVector = Smooth.Current; Ik.Execute(Context);
                Rotation.Execute(Context); Write.Value = Rotation.FootRotation; Write.Execute(Context);
            }
            TArray<TSharedPtr<FJsonValue>> After;
            for (const auto Bone : Bones) After.Add(P(H->GetGlobalTransform(Bone)));
            Row->SetArrayField(TEXT("after"), After); Row->SetNumberField(TEXT("springOffset"), Location.OffsetLocationZ);
            Row->SetNumberField(TEXT("springVelocity"), Location.OffsetSpringState.Velocity);
            Row->SetBoolField(TEXT("springValid"), Location.OffsetSpringState.bStateValid);
            Row->SetArrayField(TEXT("smooth"), V(Smooth.Current)); Row->SetArrayField(TEXT("rotationNormal"), V(Rotation.OffsetNormal));
            Row->SetBoolField(TEXT("poleSuccess"), Pole.bSuccess);
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        auto Case = MakeShared<FJsonObject>(); Case->SetArrayField(TEXT("initial"), Initial); Case->SetArrayField(TEXT("frames"), Frames);
        Case->SetBoolField(TEXT("left"), Left); Case->SetNumberField(TEXT("hz"), Hz); Case->SetBoolField(TEXT("scaled"), Scaled);
        Case->SetNumberField(TEXT("legLength"), Chain.Length); Case->SetNumberField(TEXT("footHeight"), H->GetInitialGlobalTransform(Bones[3]).GetLocation().Z);
        Cases.Add(MakeShared<FJsonValueObject>(Case));
    }
    auto Root = MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("Actual ALS/UE leg nodes in exported ApplyFootIk order; controlled inputs, not full CR_Als VM/AnimBP"));
    Root->SetArrayField(TEXT("cases"), Cases);
    FString Json; return FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Json)) &&
        FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

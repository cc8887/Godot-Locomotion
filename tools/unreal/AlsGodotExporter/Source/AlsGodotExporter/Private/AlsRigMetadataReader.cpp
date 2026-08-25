#include "AlsRigMetadataReader.h"

#include "Animation/Skeleton.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/StaticMesh.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"

namespace
{
    TArray<TSharedPtr<FJsonValue>> NumberArray(const double X, const double Y, const double Z)
    {
        return {
            MakeShared<FJsonValueNumber>(X),
            MakeShared<FJsonValueNumber>(Y),
            MakeShared<FJsonValueNumber>(Z),
        };
    }

    TArray<TSharedPtr<FJsonValue>> QuaternionArray(const FQuat& Rotation)
    {
        return {
            MakeShared<FJsonValueNumber>(Rotation.X),
            MakeShared<FJsonValueNumber>(Rotation.Y),
            MakeShared<FJsonValueNumber>(Rotation.Z),
            MakeShared<FJsonValueNumber>(Rotation.W),
        };
    }
}

bool FAlsRigMetadataReader::Read(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& OutMetadata, FString& OutError)
{
    UObject* Object = Asset.AssetData.GetAsset();
    if (!Object)
    {
        OutError = FString::Printf(TEXT("Unable to load rig asset: %s"), *Asset.AssetData.GetObjectPathString());
        return false;
    }

    if (const USkeleton* Skeleton = Cast<USkeleton>(Object))
    {
        const FReferenceSkeleton& ReferenceSkeleton = Skeleton->GetReferenceSkeleton();
        const TArray<FTransform>& RefPose = ReferenceSkeleton.GetRefBonePose();
        TArray<TSharedPtr<FJsonValue>> Bones;
        for (int32 BoneIndex = 0; BoneIndex < ReferenceSkeleton.GetNum(); ++BoneIndex)
        {
            const FTransform& Transform = RefPose[BoneIndex];
            const TSharedRef<FJsonObject> Bone = MakeShared<FJsonObject>();
            Bone->SetStringField(TEXT("name"), ReferenceSkeleton.GetBoneName(BoneIndex).ToString());
            Bone->SetNumberField(TEXT("parentIndex"), ReferenceSkeleton.GetParentIndex(BoneIndex));
            Bone->SetArrayField(TEXT("translation"), NumberArray(Transform.GetTranslation().X, Transform.GetTranslation().Y, Transform.GetTranslation().Z));
            Bone->SetArrayField(TEXT("rotation"), QuaternionArray(Transform.GetRotation()));
            Bone->SetArrayField(TEXT("scale"), NumberArray(Transform.GetScale3D().X, Transform.GetScale3D().Y, Transform.GetScale3D().Z));
            Bones.Add(MakeShared<FJsonValueObject>(Bone));
        }
        OutMetadata->SetArrayField(TEXT("bones"), Bones);

        TArray<TSharedPtr<FJsonValue>> VirtualBones;
        for (const FVirtualBone& VirtualBone : Skeleton->GetVirtualBones())
        {
            const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
            Value->SetStringField(TEXT("name"), VirtualBone.VirtualBoneName.ToString());
            Value->SetStringField(TEXT("source"), VirtualBone.SourceBoneName.ToString());
            Value->SetStringField(TEXT("target"), VirtualBone.TargetBoneName.ToString());
            VirtualBones.Add(MakeShared<FJsonValueObject>(Value));
        }
        OutMetadata->SetArrayField(TEXT("virtualBones"), VirtualBones);
        OutMetadata->SetNumberField(TEXT("boneCount"), ReferenceSkeleton.GetNum());
        return true;
    }

    if (const USkeletalMesh* SkeletalMesh = Cast<USkeletalMesh>(Object))
    {
        OutMetadata->SetStringField(TEXT("skeletonObjectPath"),
            SkeletalMesh->GetSkeleton() ? SkeletalMesh->GetSkeleton()->GetPathName() : FString());
        OutMetadata->SetNumberField(TEXT("materialSlotCount"), SkeletalMesh->GetMaterials().Num());
        return true;
    }

    if (const UStaticMesh* StaticMesh = Cast<UStaticMesh>(Object))
    {
        OutMetadata->SetNumberField(TEXT("materialSlotCount"), StaticMesh->GetStaticMaterials().Num());
        return true;
    }

    if (const UPhysicsAsset* PhysicsAsset = Cast<UPhysicsAsset>(Object))
    {
        TArray<TSharedPtr<FJsonValue>> Bodies;
        for (const USkeletalBodySetup* BodySetup : PhysicsAsset->SkeletalBodySetups)
        {
            if (!BodySetup)
            {
                continue;
            }
            const TSharedRef<FJsonObject> Body = MakeShared<FJsonObject>();
            Body->SetStringField(TEXT("bone"), BodySetup->BoneName.ToString());
            const int32 PrimitiveCount = BodySetup->AggGeom.SphereElems.Num() + BodySetup->AggGeom.BoxElems.Num() +
                BodySetup->AggGeom.SphylElems.Num() + BodySetup->AggGeom.ConvexElems.Num();
            Body->SetNumberField(TEXT("primitiveCount"), PrimitiveCount);
            Bodies.Add(MakeShared<FJsonValueObject>(Body));
        }
        OutMetadata->SetArrayField(TEXT("bodies"), Bodies);
        OutMetadata->SetNumberField(TEXT("constraintCount"), PhysicsAsset->ConstraintSetup.Num());
        return true;
    }

    OutError = FString::Printf(TEXT("Unsupported rig asset class: %s"), *Object->GetClass()->GetPathName());
    return false;
}

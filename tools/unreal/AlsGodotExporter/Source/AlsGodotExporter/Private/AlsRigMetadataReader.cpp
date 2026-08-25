#include "AlsRigMetadataReader.h"

#include "AlsStableAssetId.h"
#include "Animation/Skeleton.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/SkeletalMeshSocket.h"
#include "Engine/StaticMesh.h"
#include "Misc/SecureHash.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/PhysicsConstraintTemplate.h"
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

    void UpdateUint32(FSHA1& Hash, const uint32 Value)
    {
        const uint8 Bytes[] = {
            static_cast<uint8>(Value),
            static_cast<uint8>(Value >> 8),
            static_cast<uint8>(Value >> 16),
            static_cast<uint8>(Value >> 24),
        };
        Hash.Update(Bytes, UE_ARRAY_COUNT(Bytes));
    }

    void UpdateFloat(FSHA1& Hash, const double Value)
    {
        const float FloatValue = static_cast<float>(Value);
        uint32 Bits = 0;
        FMemory::Memcpy(&Bits, &FloatValue, sizeof(Bits));
        UpdateUint32(Hash, Bits);
    }

    void UpdateString(FSHA1& Hash, const FString& Value)
    {
        const FTCHARToUTF8 Utf8(*Value);
        UpdateUint32(Hash, Utf8.Length());
        Hash.Update(reinterpret_cast<const uint8*>(Utf8.Get()), Utf8.Length());
    }

    FString CalculateRestPoseHash(const FReferenceSkeleton& ReferenceSkeleton, const TArray<FTransform>& RefPose)
    {
        FSHA1 Hash;
        UpdateUint32(Hash, ReferenceSkeleton.GetNum());
        for (int32 BoneIndex = 0; BoneIndex < ReferenceSkeleton.GetNum(); ++BoneIndex)
        {
            UpdateString(Hash, ReferenceSkeleton.GetBoneName(BoneIndex).ToString());
            UpdateUint32(Hash, static_cast<uint32>(ReferenceSkeleton.GetParentIndex(BoneIndex)));
            const FTransform& Transform = RefPose[BoneIndex];
            const FVector Translation = Transform.GetTranslation();
            const FQuat Rotation = Transform.GetRotation();
            const FVector Scale = Transform.GetScale3D();
            UpdateFloat(Hash, Translation.X);
            UpdateFloat(Hash, Translation.Y);
            UpdateFloat(Hash, Translation.Z);
            UpdateFloat(Hash, Rotation.X);
            UpdateFloat(Hash, Rotation.Y);
            UpdateFloat(Hash, Rotation.Z);
            UpdateFloat(Hash, Rotation.W);
            UpdateFloat(Hash, Scale.X);
            UpdateFloat(Hash, Scale.Y);
            UpdateFloat(Hash, Scale.Z);
        }
        Hash.Final();
        uint8 Digest[FSHA1::DigestSize];
        Hash.GetHash(Digest);
        return BytesToHex(Digest, UE_ARRAY_COUNT(Digest)).ToLower();
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
        OutMetadata->SetStringField(TEXT("restPoseHash"), CalculateRestPoseHash(ReferenceSkeleton, RefPose));

        TArray<const USkeletalMeshSocket*> SortedSockets;
        for (const TObjectPtr<USkeletalMeshSocket>& Socket : Skeleton->Sockets)
        {
            if (Socket)
            {
                SortedSockets.Add(Socket);
            }
        }
        SortedSockets.Sort([](const USkeletalMeshSocket& Left, const USkeletalMeshSocket& Right)
        {
            const int32 NameComparison = Left.SocketName.ToString().Compare(
                Right.SocketName.ToString(), ESearchCase::CaseSensitive);
            return NameComparison == 0 ? Left.BoneName.LexicalLess(Right.BoneName) : NameComparison < 0;
        });
        TArray<TSharedPtr<FJsonValue>> Sockets;
        for (const USkeletalMeshSocket* Socket : SortedSockets)
        {
            const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
            Value->SetStringField(TEXT("name"), Socket->SocketName.ToString());
            Value->SetStringField(TEXT("bone"), Socket->BoneName.ToString());
            Value->SetArrayField(TEXT("translation"), NumberArray(
                Socket->RelativeLocation.X, Socket->RelativeLocation.Y, Socket->RelativeLocation.Z));
            Value->SetArrayField(TEXT("rotation"), QuaternionArray(Socket->RelativeRotation.Quaternion()));
            Value->SetArrayField(TEXT("scale"), NumberArray(
                Socket->RelativeScale.X, Socket->RelativeScale.Y, Socket->RelativeScale.Z));
            Sockets.Add(MakeShared<FJsonValueObject>(Value));
        }
        OutMetadata->SetArrayField(TEXT("sockets"), Sockets);

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
        const FString SkeletonPath = SkeletalMesh->GetSkeleton() ? SkeletalMesh->GetSkeleton()->GetPathName() : FString();
        OutMetadata->SetStringField(TEXT("skeletonObjectPath"), SkeletonPath);
        OutMetadata->SetStringField(TEXT("skeletonId"),
            SkeletonPath.StartsWith(TEXT("/Game/AdvancedLocomotionV4/")) ? FAlsStableAssetId::Create(SkeletonPath) : FString());
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

        TArray<TPair<FString, FString>> ConstraintBones;
        for (const UPhysicsConstraintTemplate* Constraint : PhysicsAsset->ConstraintSetup)
        {
            if (Constraint)
            {
                ConstraintBones.Emplace(
                    Constraint->DefaultInstance.GetChildBoneName().ToString(),
                    Constraint->DefaultInstance.GetParentBoneName().ToString());
            }
        }
        ConstraintBones.Sort([](const TPair<FString, FString>& Left, const TPair<FString, FString>& Right)
        {
            const int32 ChildComparison = Left.Key.Compare(Right.Key, ESearchCase::CaseSensitive);
            return ChildComparison == 0 ? Left.Value < Right.Value : ChildComparison < 0;
        });
        TArray<TSharedPtr<FJsonValue>> Constraints;
        for (const TPair<FString, FString>& Constraint : ConstraintBones)
        {
            const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
            Value->SetStringField(TEXT("childBone"), Constraint.Key);
            Value->SetStringField(TEXT("parentBone"), Constraint.Value);
            Constraints.Add(MakeShared<FJsonValueObject>(Value));
        }
        OutMetadata->SetArrayField(TEXT("constraints"), Constraints);
        OutMetadata->SetNumberField(TEXT("constraintCount"), Constraints.Num());
        return true;
    }

    OutError = FString::Printf(TEXT("Unsupported rig asset class: %s"), *Object->GetClass()->GetPathName());
    return false;
}

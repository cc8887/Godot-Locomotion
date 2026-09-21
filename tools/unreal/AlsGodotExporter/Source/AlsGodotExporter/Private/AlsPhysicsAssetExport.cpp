#include "AlsPhysicsAssetExport.h"
#include "AlsAnimationGraphLibrary.h"
#include "Chaos/ImplicitObjectScaled.h"
#include "Chaos/ShapeInstance.h"
#include "PhysicsProxy/SingleParticlePhysicsProxy.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Engine.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "HAL/FileManager.h"
#include "JsonObjectConverter.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PhysicalMaterials/PhysicalMaterial.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/PhysicsConstraintTemplate.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsPhysicsExport
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& P)
{ return {MakeShared<FJsonValueNumber>(P.X), MakeShared<FJsonValueNumber>(P.Y), MakeShared<FJsonValueNumber>(P.Z)}; }
TSharedRef<FJsonObject> T(const FTransform& Transform)
{
    auto Result = MakeShared<FJsonObject>(); const auto Q = Transform.GetRotation();
    Result->SetArrayField(TEXT("translation"), V(Transform.GetTranslation()));
    Result->SetArrayField(TEXT("rotation"), {MakeShared<FJsonValueNumber>(Q.X), MakeShared<FJsonValueNumber>(Q.Y),
        MakeShared<FJsonValueNumber>(Q.Z), MakeShared<FJsonValueNumber>(Q.W)});
    Result->SetArrayField(TEXT("scale"), V(Transform.GetScale3D())); return Result;
}
template<typename S> TSharedRef<FJsonObject> Native(const S& Value)
{
    auto Result = MakeShared<FJsonObject>();
    check(FJsonObjectConverter::UStructToJsonObject(S::StaticStruct(), &Value, Result, 0, CPF_Transient | CPF_Deprecated));
    return Result;
}
TSharedRef<FJsonObject> Shape(const FKShapeElem& Element, const TCHAR* Type)
{
    auto Result = MakeShared<FJsonObject>(); Result->SetStringField(TEXT("type"), Type);
    Result->SetStringField(TEXT("name"), Element.GetName().ToString());
    Result->SetObjectField(TEXT("local"), T(Element.GetTransform()));
    Result->SetBoolField(TEXT("contributesToMass"), Element.GetContributeToMass());
    Result->SetNumberField(TEXT("collisionEnabled"), Element.GetCollisionEnabled());
    Result->SetNumberField(TEXT("restOffsetCm"), Element.RestOffset); return Result;
}
}

bool ExportAlsPhysicsAssets(const FString& Output, FString& Error, bool ObserveRuntimeShapes)
{
    using namespace AlsPhysicsExport;
    const auto Fail = [&](const FString& Message) { Error = Message; return false; };
    if (Output.IsEmpty() || FPaths::IsRelative(Output) || IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Physics export requires a new absolute output file."));
    const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None,
        nullptr, false, ERHIFeatureLevel::Num, &Initialization));
    if (!World.IsValid()) return Fail(TEXT("Cannot create physics reference world."));
    GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
    struct FCleanup
    {
        UWorld* World;
        ~FCleanup() { World->DestroyWorld(false); GEngine->DestroyWorldContext(World); }
    } Cleanup{World.Get()};
    auto Root = MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("engine"), FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("coordinates"), TEXT("UE mesh-local, centimeters, kilograms, degrees; inertia kg*cm^2"));
    Root->SetStringField(TEXT("massObservation"), TEXT("Reference pose, identity component transform, native FBodyInstance after physics creation, no simulation tick"));
    if (ObserveRuntimeShapes)
        Root->SetStringField(TEXT("shapeObservation"), TEXT("External game-thread particle shapes after physics creation; leaf wrapper and margin observed, authored index matched by shape user-data identity; no simulation tick"));
    const FString CharacterPath = TEXT("/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_AnimMan_CharacterBP.ALS_AnimMan_CharacterBP_C");
    auto* Class = LoadClass<ACharacter>(nullptr, *CharacterPath);
    const auto* Defaults = Class ? Class->GetDefaultObject<ACharacter>() : nullptr;
    const auto* DefaultMesh = Defaults ? Defaults->GetMesh() : nullptr;
    if (!DefaultMesh || !DefaultMesh->GetSkeletalMeshAsset() || !DefaultMesh->GetPhysicsAsset())
        return Fail(TEXT("Missing native character mesh/physics binding."));
    auto Binding = MakeShared<FJsonObject>(); Binding->SetStringField(TEXT("characterClass"), CharacterPath);
    Binding->SetStringField(TEXT("mesh"), DefaultMesh->GetSkeletalMeshAsset()->GetPathName());
    Binding->SetStringField(TEXT("effectivePhysicsAsset"), DefaultMesh->GetPhysicsAsset()->GetPathName());
    Binding->SetObjectField(TEXT("meshToCharacter"), T(DefaultMesh->GetRelativeTransform()));
    Root->SetObjectField(TEXT("characterBinding"), Binding);
    TArray<TSharedPtr<FJsonValue>> Meshes;
    for (const TCHAR* Name : {TEXT("Mannequin"), TEXT("AnimMan")})
    {
        const FString MeshPath = FString(TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/")) + Name + TEXT(".") + Name;
        auto* Mesh = LoadObject<USkeletalMesh>(nullptr, *MeshPath);
        auto* Asset = Mesh ? Mesh->GetPhysicsAsset() : nullptr;
        if (!Asset) return Fail(TEXT("Missing physics asset: ") + MeshPath);
        auto Row = MakeShared<FJsonObject>(); Row->SetStringField(TEXT("mesh"), MeshPath);
        Row->SetStringField(TEXT("physicsAsset"), Asset->GetPathName());
        const auto& Ref = Mesh->GetRefSkeleton(); TArray<TSharedPtr<FJsonValue>> Bones;
        for (int32 I = 0; I < Ref.GetNum(); ++I)
        {
            auto Bone = MakeShared<FJsonObject>(); Bone->SetStringField(TEXT("name"), Ref.GetBoneName(I).ToString());
            Bone->SetNumberField(TEXT("parent"), Ref.GetParentIndex(I)); Bone->SetObjectField(TEXT("local"), T(Ref.GetRefBonePose()[I]));
            Bones.Add(MakeShared<FJsonValueObject>(Bone));
        }
        Row->SetArrayField(TEXT("bones"), Bones);
        auto* Owner = World->SpawnActor<AActor>(); if (!Owner) return Fail(TEXT("Cannot spawn reference owner."));
        auto* Component = NewObject<USkeletalMeshComponent>(Owner); Owner->SetRootComponent(Component); Owner->AddInstanceComponent(Component);
        Component->SetSkeletalMesh(Mesh); Component->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
        Component->SetSimulatePhysics(true); Component->RegisterComponent(); Component->SetComponentTickEnabled(false);
        TArray<TSharedPtr<FJsonValue>> Bodies;
        for (int32 I = 0; I < Asset->SkeletalBodySetups.Num(); ++I)
        {
            const USkeletalBodySetup* Setup = Asset->SkeletalBodySetups[I];
            if (!Setup || Ref.FindBoneIndex(Setup->BoneName) < 0) return Fail(TEXT("Null or unbound physics body."));
            const auto* Instance = Component->GetBodyInstance(Setup->BoneName);
            if (!Instance || !Instance->IsValidBodyInstance()) return Fail(TEXT("Native body was not created: ") + Setup->BoneName.ToString());
            auto Body = MakeShared<FJsonObject>(); Body->SetNumberField(TEXT("index"), I);
            Body->SetStringField(TEXT("bone"), Setup->BoneName.ToString());
            Body->SetNumberField(TEXT("physicsType"), Setup->PhysicsType);
            Body->SetObjectField(TEXT("defaults"), Native(Setup->DefaultInstance));
            Body->SetNumberField(TEXT("nativeMassKg"), Instance->GetBodyMass());
            Body->SetArrayField(TEXT("nativeInertiaKgCm2"), V(Instance->GetBodyInertiaTensor()));
            Body->SetObjectField(TEXT("nativeMassLocal"), T(Instance->GetMassSpaceLocal()));
            Body->SetObjectField(TEXT("nativeBodyComponent"), T(Instance->GetUnrealWorldTransform()));
            const auto* Material = Instance->GetSimplePhysicalMaterial();
            if (!Material) return Fail(TEXT("Missing effective physical material."));
            auto Mat = MakeShared<FJsonObject>(); Mat->SetStringField(TEXT("path"), Material->GetPathName());
            Mat->SetNumberField(TEXT("densityGPerCm3"), Material->Density); Mat->SetNumberField(TEXT("raiseMassToPower"), Material->RaiseMassToPower);
            Mat->SetNumberField(TEXT("friction"), Material->Friction); Mat->SetNumberField(TEXT("staticFriction"), Material->StaticFriction);
            Mat->SetNumberField(TEXT("restitution"), Material->Restitution);
            Mat->SetBoolField(TEXT("overrideFrictionCombine"), Material->bOverrideFrictionCombineMode);
            Mat->SetNumberField(TEXT("frictionCombine"), Material->FrictionCombineMode);
            Mat->SetBoolField(TEXT("overrideRestitutionCombine"), Material->bOverrideRestitutionCombineMode);
            Mat->SetNumberField(TEXT("restitutionCombine"), Material->RestitutionCombineMode);
            Body->SetObjectField(TEXT("material"), Mat);
            TArray<TSharedPtr<FJsonValue>> Shapes; const auto& Geometry = Setup->AggGeom;
            for (const auto& S : Geometry.SphereElems)
            { auto J = Shape(S, TEXT("sphere")); J->SetNumberField(TEXT("radiusCm"), S.Radius); Shapes.Add(MakeShared<FJsonValueObject>(J)); }
            for (const auto& S : Geometry.BoxElems)
            { auto J = Shape(S, TEXT("box")); J->SetArrayField(TEXT("sizeCm"), V(FVector(S.X,S.Y,S.Z))); Shapes.Add(MakeShared<FJsonValueObject>(J)); }
            for (const auto& S : Geometry.SphylElems)
            { auto J = Shape(S, TEXT("capsule")); J->SetNumberField(TEXT("radiusCm"), S.Radius); J->SetNumberField(TEXT("cylinderLengthCm"), S.Length); Shapes.Add(MakeShared<FJsonValueObject>(J)); }
            for (const auto& S : Geometry.ConvexElems)
            {
                auto J = Shape(S, TEXT("convex")); TArray<TSharedPtr<FJsonValue>> Vertices, Indices;
                for (const auto& P : S.VertexData) Vertices.Add(MakeShared<FJsonValueArray>(V(P)));
                for (int32 Index : S.IndexData) Indices.Add(MakeShared<FJsonValueNumber>(Index));
                J->SetArrayField(TEXT("verticesCm"), Vertices); J->SetArrayField(TEXT("indices"), Indices); Shapes.Add(MakeShared<FJsonValueObject>(J));
            }
            for (const auto& S : Geometry.TaperedCapsuleElems)
            { auto J = Shape(S, TEXT("taperedCapsule")); J->SetNumberField(TEXT("radius0Cm"), S.Radius0); J->SetNumberField(TEXT("radius1Cm"), S.Radius1); J->SetNumberField(TEXT("cylinderLengthCm"), S.Length); Shapes.Add(MakeShared<FJsonValueObject>(J)); }
            if (Shapes.Num() != Geometry.GetElementCount()) return Fail(TEXT("Unsupported physics geometry; export cannot omit shapes."));
            if (ObserveRuntimeShapes)
            {
                // Do not assume Chaos shape order matches the authored export order.
                TArray<const FKShapeElem*> Elements;
                for (const auto& S : Geometry.SphereElems) Elements.Add(&S);
                for (const auto& S : Geometry.BoxElems) Elements.Add(&S);
                for (const auto& S : Geometry.SphylElems) Elements.Add(&S);
                for (const auto& S : Geometry.ConvexElems) Elements.Add(&S);
                for (const auto& S : Geometry.TaperedCapsuleElems) Elements.Add(&S);
                const auto* Actor = Instance->GetPhysicsActorHandle();
                if (!Actor) return Fail(TEXT("Missing external physics actor."));
                const auto& NativeShapes = Actor->GetGameThreadAPI().ShapesArray();
                if (NativeShapes.Num() != Elements.Num()) return Fail(TEXT("Runtime/authored shape count mismatch."));
                TSet<int32> Seen; TArray<TSharedPtr<FJsonValue>> Observations;
                for (int32 NativeIndex = 0; NativeIndex < NativeShapes.Num(); ++NativeIndex)
                {
                    const auto& S = NativeShapes[NativeIndex];
                    int32 AuthoredIndex = INDEX_NONE;
                    for (int32 K = 0; K < Elements.Num(); ++K)
                        if (S->GetUserData() == Elements[K]->GetUserData())
                        {
                            if (AuthoredIndex != INDEX_NONE) return Fail(TEXT("Ambiguous authored shape identity."));
                            AuthoredIndex = K;
                        }
                    if (AuthoredIndex == INDEX_NONE || Seen.Contains(AuthoredIndex))
                        return Fail(TEXT("Unmatched or repeated native shape identity."));
                    Seen.Add(AuthoredIndex);
                    const auto* Leaf = S->GetLeafGeometry();
                    if (!Leaf) return Fail(TEXT("Missing native leaf geometry."));
                    auto J = MakeShared<FJsonObject>();
                    J->SetNumberField(TEXT("nativeIndex"), NativeIndex);
                    J->SetNumberField(TEXT("authoredIndex"), AuthoredIndex);
                    J->SetStringField(TEXT("type"), Leaf->GetTypeName().ToString());
                    J->SetNumberField(TEXT("typeCode"), static_cast<uint8>(Leaf->GetType()));
                    J->SetNumberField(TEXT("marginCm"), Leaf->GetMarginf());
                    J->SetObjectField(TEXT("leafLocal"), T(FTransform(S->GetLeafRelativeTransform())));
                    J->SetArrayField(TEXT("boundsMinCm"), V(FVector(Leaf->BoundingBox().Min())));
                    J->SetArrayField(TEXT("boundsMaxCm"), V(FVector(Leaf->BoundingBox().Max())));
                    const Chaos::FImplicitObject* Inner = nullptr;
                    if (Chaos::IsScaled(Leaf->GetType()))
                    {
                        const auto* Scaled = static_cast<const Chaos::FImplicitObjectScaled*>(Leaf);
                        J->SetStringField(TEXT("wrapper"), TEXT("scaled"));
                        J->SetArrayField(TEXT("scale"), V(FVector(Scaled->GetScale())));
                        Inner = Scaled->GetInnerObject().Get();
                    }
                    else if (Chaos::IsInstanced(Leaf->GetType()))
                    {
                        J->SetStringField(TEXT("wrapper"), TEXT("instanced"));
                        J->SetArrayField(TEXT("scale"), V(FVector::OneVector));
                        Inner = static_cast<const Chaos::FImplicitObjectInstanced*>(Leaf)->GetInnerObject().Get();
                    }
                    else
                    {
                        J->SetStringField(TEXT("wrapper"), TEXT("plain"));
                        J->SetArrayField(TEXT("scale"), V(FVector::OneVector));
                    }
                    if (Inner)
                    {
                        J->SetStringField(TEXT("innerType"), Inner->GetTypeName().ToString());
                        J->SetNumberField(TEXT("innerMarginCm"), Inner->GetMarginf());
                    }
                    Observations.Add(MakeShared<FJsonValueObject>(J));
                }
                Body->SetArrayField(TEXT("runtimeShapes"), Observations);
            }
            Body->SetArrayField(TEXT("shapes"), Shapes); Bodies.Add(MakeShared<FJsonValueObject>(Body));
        }
        Row->SetArrayField(TEXT("bodies"), Bodies);
        TArray<TSharedPtr<FJsonValue>> Constraints;
        for (int32 I = 0; I < Asset->ConstraintSetup.Num(); ++I)
        {
            const UPhysicsConstraintTemplate* Template = Asset->ConstraintSetup[I]; if (!Template) return Fail(TEXT("Null constraint."));
            const auto& C = Template->DefaultInstance;
            auto Joint = MakeShared<FJsonObject>(); Joint->SetNumberField(TEXT("index"), I);
            Joint->SetStringField(TEXT("childBone"), C.GetChildBoneName().ToString());
            Joint->SetStringField(TEXT("parentBone"), C.GetParentBoneName().ToString());
            Joint->SetObjectField(TEXT("childFrame"), T(C.GetRefFrame(EConstraintFrame::Frame1)));
            Joint->SetObjectField(TEXT("parentFrame"), T(C.GetRefFrame(EConstraintFrame::Frame2)));
            Joint->SetObjectField(TEXT("nativeInstance"), Native(C));
            TArray<TSharedPtr<FJsonValue>> Profiles;
            for (const auto& P : Template->ProfileHandles) Profiles.Add(MakeShared<FJsonValueObject>(Native(P)));
            Joint->SetArrayField(TEXT("profiles"), Profiles); Constraints.Add(MakeShared<FJsonValueObject>(Joint));
        }
        Row->SetArrayField(TEXT("constraints"), Constraints);
        // Preserve original body indices; sort map-derived pairs for deterministic output.
        TArray<FRigidBodyIndexPair> Pairs; Asset->CollisionDisableTable.GetKeys(Pairs);
        Pairs.Sort([](const auto& A, const auto& B) { return A.Indices[0] == B.Indices[0] ? A.Indices[1] < B.Indices[1] : A.Indices[0] < B.Indices[0]; });
        TArray<TSharedPtr<FJsonValue>> Disabled;
        for (const auto& P : Pairs)
        {
            auto Pair = MakeShared<FJsonObject>(); Pair->SetNumberField(TEXT("a"), P.Indices[0]); Pair->SetNumberField(TEXT("b"), P.Indices[1]);
            Pair->SetBoolField(TEXT("value"), Asset->CollisionDisableTable.FindChecked(P)); Disabled.Add(MakeShared<FJsonValueObject>(Pair));
        }
        Row->SetArrayField(TEXT("collisionDisableTable"), Disabled);
        Meshes.Add(MakeShared<FJsonValueObject>(Row)); Owner->Destroy();
    }
    Root->SetArrayField(TEXT("meshes"), Meshes); FString Json;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Json)) ||
        !IFileManager::Get().MakeDirectory(*FPaths::GetPath(Output), true) ||
        !FFileHelper::SaveStringToFile(Json, *Output, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
        return Fail(TEXT("Cannot publish physics export."));
    return true;
}

bool UAlsAnimationGraphLibrary::ExportPhysicsAssets(const FString& OutputPath)
{
    FString Error;
    if (!ExportAlsPhysicsAssets(OutputPath, Error))
    { UE_LOG(LogTemp, Error, TEXT("ALS_PHYSICS_ASSETS_FAILED %s"), *Error); return false; }
    UE_LOG(LogTemp, Display, TEXT("ALS_PHYSICS_ASSETS_OK meshes=2 assets_saved=0")); return true;
}

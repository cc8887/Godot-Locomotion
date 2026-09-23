#include "AlsPhysicsAssetExport.h"
#include "Chaos/Framework/Parallel.h"
#include "Components/CapsuleComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Engine.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Physics/Experimental/PhysScene_Chaos.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "PBDRigidsSolver.h"
#include "PhysicsProxy/SingleParticlePhysicsProxy.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsJointSolverReference {
TArray<TSharedPtr<FJsonValue>> V(const FVector& P);
TSharedRef<FJsonObject> T(const FTransform& P);
}

// Controlled synchronous entry transitions, not an animation/PIE trajectory.
// Seeded per-body velocities distinguish retained state from reconstructed state.
bool ExportAlsPhysicsEntryReference(const FString& Output,FString& Error)
{
    using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Entry reference requires a new absolute output."));
    TArray<TSharedPtr<FJsonValue>> Cases;
    for(const FString Name:{FString(TEXT("Mannequin")),FString(TEXT("AnimMan"))})
    for(const bool QueryOnly:{true,false})
    {
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false)
            .CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(true).EnableTraceCollision(true).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
        if(!World.IsValid())return Fail(TEXT("Cannot create entry world."));
        GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
        struct FCleanup { UWorld* W;~FCleanup(){W->GetPhysicsScene()->WaitPhysScenes();W->DestroyWorld(false);GEngine->DestroyWorldContext(W);} } Cleanup{World.Get()};
        auto* Scene=World->GetPhysicsScene();auto* Solver=Scene->GetSolver();
        Solver->SetThreadingMode_External(Chaos::EThreadingModeTemp::SingleThread);
        Solver->DisableAsyncMode();Solver->SetIsPaused_External(false);
        const FString Path=TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/")+Name+TEXT(".")+Name;
        auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Path);auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;
        if(!Asset)return Fail(TEXT("Missing entry mesh physics asset."));
        auto* Owner=World->SpawnActor<ACharacter>();if(!Owner)return Fail(TEXT("Cannot spawn entry character."));
        auto* Component=Owner->GetMesh();Component->SetSkeletalMesh(Mesh);
        Component->SetCollisionEnabled(QueryOnly?ECollisionEnabled::QueryOnly:ECollisionEnabled::QueryAndPhysics);
        Component->SetSimulatePhysics(false);Component->SetComponentTickEnabled(false);
        Owner->GetCharacterMovement()->Velocity=FVector(300,-400,120);
        TArray<TSharedPtr<FJsonValue>> Stages;
        const auto Capture=[&](const TCHAR* Stage)->bool
        {
            auto J=MakeShared<FJsonObject>();J->SetStringField(TEXT("stage"),Stage);
            J->SetNumberField(TEXT("collisionEnabled"),static_cast<int32>(Component->GetCollisionEnabled()));
            J->SetArrayField(TEXT("ownerVelocity"),V(Owner->GetVelocity()));
            TArray<TSharedPtr<FJsonValue>> Rows;
            for(int32 I=0;I<Asset->SkeletalBodySetups.Num();++I)
            {
                const auto* Setup=Asset->SkeletalBodySetups[I].Get();auto* Body=Component->GetBodyInstance(Setup->BoneName);
                if(!Body||!Body->IsValidBodyInstance())return false;
                auto B=MakeShared<FJsonObject>();B->SetStringField(TEXT("bone"),Setup->BoneName.ToString());
                B->SetNumberField(TEXT("physicsType"),static_cast<int32>(Setup->PhysicsType));
                B->SetBoolField(TEXT("simulating"),Body->IsInstanceSimulatingPhysics());
                FPhysicsCommand::ExecuteRead(Body->ActorHandle,[&](const FPhysicsActorHandle& Actor)
                {
                    B->SetArrayField(TEXT("linear"),V(FPhysicsInterface::GetLinearVelocity_AssumesLocked(Actor)));
                    B->SetArrayField(TEXT("angular"),V(FPhysicsInterface::GetAngularVelocity_AssumesLocked(Actor)));
                    B->SetObjectField(TEXT("actor"),T(FPhysicsInterface::GetTransform_AssumesLocked(Actor)));
                    const auto* Handle=Actor->GetHandle_LowLevel();
                    if(const auto* Particle=Handle?Handle->CastToKinematicParticle():nullptr)
                    {
                        B->SetArrayField(TEXT("physicsLinear"),V(FVector(Particle->GetV())));
                        B->SetArrayField(TEXT("physicsAngular"),V(FVector(Particle->GetW())));
                    }
                });
                Rows.Add(MakeShared<FJsonValueObject>(B));
            }
            J->SetArrayField(TEXT("bodies"),Rows);Stages.Add(MakeShared<FJsonValueObject>(J));return true;
        };
        if(!Capture(TEXT("created")))return Fail(TEXT("Missing created body."));
        for(int32 I=0;I<Asset->SkeletalBodySetups.Num();++I)
        {
            auto* Body=Component->GetBodyInstance(Asset->SkeletalBodySetups[I]->BoneName);
            Body->SetLinearVelocity(FVector(600+I,40-I,-80),false);
            Body->SetAngularVelocityInRadians(FVector(.1+I*.01,.2,-.3),false);
        }
        if(!Capture(TEXT("seeded")))return Fail(TEXT("Missing seeded body."));
        Component->DetachFromComponent(FDetachmentTransformRules::KeepWorldTransform);
        Owner->GetCapsuleComponent()->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Component->SetCollisionObjectType(ECC_PhysicsBody);Component->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
        if(!Capture(TEXT("collision_enabled")))return Fail(TEXT("Missing recreated body."));
        Component->SetSimulatePhysics(true);
        if(!Capture(TEXT("simulate")))return Fail(TEXT("Missing simulated body."));
        Component->ResetAllBodiesSimulatePhysics();
        if(!Capture(TEXT("reset_asset_types")))return Fail(TEXT("Missing reset body."));
        auto Case=MakeShared<FJsonObject>();Case->SetStringField(TEXT("mesh"),Path);Case->SetBoolField(TEXT("startedQueryOnly"),QueryOnly);
        Case->SetArrayField(TEXT("stages"),Stages);Stages.Reset();
        // The target is queued on the game thread; velocities are produced by
        // the solver step. Keep those boundaries visible rather than inferring
        // inherited velocity from the actor's movement component.
        const float Dt=1.f/60;const FVector Gravity=FVector::ZeroVector;
        const auto Step=[&]()->bool
        {
            const double Before=Solver->GetSolverTime();Scene->SetUpForFrame(&Gravity,Dt,0,Dt,Dt,1,false);
            Scene->StartFrame();Scene->WaitPhysScenes();Scene->EndFrame();
            return FMath::IsNearlyEqual(Solver->GetSolverTime()-Before,static_cast<double>(Dt),1.e-6);
        };
        Component->SetSimulatePhysics(false);
        for(const auto& Setup:Asset->SkeletalBodySetups)
        {
            auto* Body=Component->GetBodyInstance(Setup->BoneName);Body->SetEnableGravity(false);
            Body->SetBodyTransform(Body->GetUnrealWorldTransform(),ETeleportType::TeleportPhysics);
        }
        if(!Step()||!Capture(TEXT("history_initial")))return Fail(TEXT("Cannot initialize native history."));
        for(int32 I=0;I<Asset->SkeletalBodySetups.Num();++I)
        {
            auto* Body=Component->GetBodyInstance(Asset->SkeletalBodySetups[I]->BoneName);
            auto Target=Body->GetUnrealWorldTransform();Target.AddToTranslation(FVector(3+I*.1,-2,1));
            Target.SetRotation(FQuat(FVector::UpVector,.04)*Target.GetRotation());
            Scene->SetKinematicTarget_AssumesLocked(Body,Target,true);
        }
        if(!Capture(TEXT("target_queued"))||!Step()||!Capture(TEXT("target_stepped")))return Fail(TEXT("Cannot step native target."));
        Component->SetSimulatePhysics(true);Component->ResetAllBodiesSimulatePhysics();
        if(!Capture(TEXT("entered_after_step")))return Fail(TEXT("Cannot enter from native target history."));
        Component->SetSimulatePhysics(false);
        if(!Step()||!Capture(TEXT("no_target_step")))return Fail(TEXT("Cannot reset native target velocity."));
        for(const auto& Setup:Asset->SkeletalBodySetups)
        {
            auto* Body=Component->GetBodyInstance(Setup->BoneName);auto Target=Body->GetUnrealWorldTransform();
            Target.AddToTranslation(FVector(1,2,3));Target.SetRotation(FQuat(FVector::RightVector,.03)*Target.GetRotation());
            Scene->SetKinematicTarget_AssumesLocked(Body,Target,true);
        }
        if(!Step()||!Capture(TEXT("before_teleport")))return Fail(TEXT("Cannot establish nonzero pre-teleport velocity."));
        for(const auto& Setup:Asset->SkeletalBodySetups)
        {
            auto* Body=Component->GetBodyInstance(Setup->BoneName);auto Target=Body->GetUnrealWorldTransform();
            Target.AddToTranslation(FVector(1000,-2000,3000));Body->SetBodyTransform(Target,ETeleportType::TeleportPhysics);
        }
        if(!Capture(TEXT("teleported"))||!Step()||!Capture(TEXT("teleport_stepped")))return Fail(TEXT("Cannot observe native teleport."));
        Case->SetNumberField(TEXT("historyDt"),Dt);Case->SetArrayField(TEXT("history"),Stages);
        Cases.Add(MakeShared<FJsonValueObject>(Case));
    }
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("scope"),TEXT("controlled ACharacter body entry and synchronous kinematic targets; no animation or PIE"));
    const FString CharacterPath=TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C");
    auto* CharacterClass=LoadClass<ACharacter>(nullptr,*CharacterPath);
    if(!CharacterClass)return Fail(TEXT("Missing ALS reference character class."));
    const auto* Defaults=CharacterClass->GetDefaultObject<ACharacter>();
    Root->SetStringField(TEXT("alsCharacterClass"),CharacterPath);
    Root->SetNumberField(TEXT("alsMeshCollisionEnabled"),static_cast<int32>(Defaults->GetMesh()->GetCollisionEnabled()));
    Root->SetBoolField(TEXT("alsDeferKinematicBoneUpdate"),Defaults->GetMesh()->bDeferKinematicBoneUpdate);
    Root->SetArrayField(TEXT("cases"),Cases);FString Text;
    return (FJsonSerializer::Serialize(Root,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Text))&&
        FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))||Fail(TEXT("Entry reference write failed."));
}

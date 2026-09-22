#include "AlsPhysicsAssetExport.h"
#include "Chaos/PBDJointConstraints.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Engine.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PBDRigidsSolver.h"
#include "Physics/Experimental/PhysScene_Chaos.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/PhysicsConstraintTemplate.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "PhysicsProxy/SingleParticlePhysicsProxy.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsJointSolverReference {
TArray<TSharedPtr<FJsonValue>> V(const FVector& P);
bool Step(FPhysScene* Scene,float Dt);
}
namespace AlsPhysicsExport { TSharedRef<FJsonObject> T(const FTransform& Transform); }

bool ExportAlsPhysicsJointFrameInputs(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;using AlsPhysicsExport::T;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Joint frame inputs require a new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("coordinates"),TEXT("UE actor-local joint frames; centimeters; creation scale before physics"));
    TArray<TSharedPtr<FJsonValue>> Rigs;
    for(const TCHAR* Name:{TEXT("Mannequin"),TEXT("AnimMan")})
    {
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
            .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(true)
            .EnableTraceCollision(true).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
        if(!World.IsValid())return Fail(TEXT("Cannot create joint frame world."));
        GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
        struct FCleanup{UWorld* W;~FCleanup(){W->GetPhysicsScene()->WaitPhysScenes();W->DestroyWorld(false);GEngine->DestroyWorldContext(W);}} Cleanup{World.Get()};
        auto* Scene=World->GetPhysicsScene();auto* Solver=Scene->GetSolver();
        Solver->SetThreadingMode_External(EThreadingModeTemp::SingleThread);Solver->DisableAsyncMode();Solver->SetIsPaused_External(false);
        const FString Path=FString(TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/"))+Name+TEXT(".")+Name;
        auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Path);auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;
        if(!Asset)return Fail(TEXT("Missing joint frame asset."));
        auto* Owner=World->SpawnActor<AActor>();auto* Component=NewObject<USkeletalMeshComponent>(Owner);
        Owner->SetRootComponent(Component);Owner->AddInstanceComponent(Component);Component->SetSkeletalMesh(Mesh);
        Component->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);Component->SetCollisionResponseToAllChannels(ECR_Ignore);
        Component->SetSimulatePhysics(true);Component->RegisterComponent();Component->SetComponentTickEnabled(false);
        auto Rig=MakeShared<FJsonObject>();Rig->SetStringField(TEXT("mesh"),Path);Rig->SetStringField(TEXT("physicsAsset"),Asset->GetPathName());
        Rig->SetArrayField(TEXT("componentScale"),V(Component->GetComponentScale()));
        TArray<TSharedPtr<FJsonValue>> Bodies;
        for(int32 I=0;I<Asset->SkeletalBodySetups.Num();++I)
        {
            const USkeletalBodySetup* Setup=Asset->SkeletalBodySetups[I];auto* Body=Component->GetBodyInstance(Setup->BoneName);
            if(!Body||!Body->IsValidBodyInstance())return Fail(TEXT("Missing joint frame body."));
            Body->SetEnableGravity(false);Body->SetResponseToAllChannels(ECR_Ignore);
            auto B=MakeShared<FJsonObject>();B->SetNumberField(TEXT("index"),I);B->SetStringField(TEXT("bone"),Setup->BoneName.ToString());
            B->SetArrayField(TEXT("defaultScale"),V(Setup->DefaultInstance.Scale3D));B->SetArrayField(TEXT("instanceScale"),V(Body->Scale3D));
            B->SetNumberField(TEXT("nativeMassKg"),Body->GetBodyMass());B->SetArrayField(TEXT("nativeInertiaKgCm2"),V(Body->GetBodyInertiaTensor()));
            Bodies.Add(MakeShared<FJsonValueObject>(B));
        }
        Rig->SetArrayField(TEXT("bodies"),Bodies);
        if(!Step(Scene,1.f/60))return Fail(TEXT("Joint frame warmup did not advance."));
        TArray<TSharedPtr<FJsonValue>> Joints;
        for(int32 I=0;I<Asset->ConstraintSetup.Num();++I)
        {
            const auto& Authored=Asset->ConstraintSetup[I]->DefaultInstance;
            auto* Instance=Component->FindConstraintInstance(Authored.JointName);
            auto* Child=Component->GetBodyInstance(Authored.ConstraintBone1);auto* Parent=Component->GetBodyInstance(Authored.ConstraintBone2);
            if(!Instance||!Child||!Parent)return Fail(TEXT("Missing joint frame binding."));
            auto* ChildParticle=Child->GetPhysicsActorHandle()->GetHandle_LowLevel();
            auto* ParentParticle=Parent->GetPhysicsActorHandle()->GetHandle_LowLevel();
            const FPBDJointConstraintHandle* Found=nullptr;int32 ChildEnd=INDEX_NONE;
            for(auto* Handle:ChildParticle->ParticleConstraints())
            {
                const auto* Joint=Handle->As<FPBDJointConstraintHandle>();if(!Joint)continue;
                const auto Pair=Joint->GetConstrainedParticles();
                if((Pair[0]==ChildParticle&&Pair[1]==ParentParticle)||(Pair[1]==ChildParticle&&Pair[0]==ParentParticle))
                {
                    if(Found)return Fail(TEXT("Ambiguous joint frame endpoints."));
                    Found=Joint;ChildEnd=Pair[0]==ChildParticle?0:1;
                }
            }
            if(!Found)return Fail(TEXT("Missing internal joint frame."));
            auto J=MakeShared<FJsonObject>();J->SetNumberField(TEXT("index"),I);
            J->SetStringField(TEXT("childBone"),Authored.ConstraintBone1.ToString());J->SetStringField(TEXT("parentBone"),Authored.ConstraintBone2.ToString());
            J->SetObjectField(TEXT("authoredChildFrame"),T(Authored.GetRefFrame(EConstraintFrame::Frame1)));
            J->SetObjectField(TEXT("authoredParentFrame"),T(Authored.GetRefFrame(EConstraintFrame::Frame2)));
            J->SetObjectField(TEXT("instanceChildFrame"),T(Instance->GetRefFrame(EConstraintFrame::Frame1)));
            J->SetObjectField(TEXT("instanceParentFrame"),T(Instance->GetRefFrame(EConstraintFrame::Frame2)));
            J->SetObjectField(TEXT("actualChildFrame"),T(FTransform(Found->GetSettings().ConnectorTransforms[ChildEnd])));
            J->SetObjectField(TEXT("actualParentFrame"),T(FTransform(Found->GetSettings().ConnectorTransforms[1-ChildEnd])));
            Joints.Add(MakeShared<FJsonValueObject>(J));
        }
        Rig->SetArrayField(TEXT("joints"),Joints);Rigs.Add(MakeShared<FJsonValueObject>(Rig));
    }
    Root->SetArrayField(TEXT("rigs"),Rigs);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json)))return Fail(TEXT("Cannot serialize joint frames."));
    IFileManager::Get().MakeDirectory(*FPaths::GetPath(Output),true);
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)||Fail(TEXT("Cannot write joint frames."));
}

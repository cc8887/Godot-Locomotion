#include "LyraContextEffectsOracleLibrary.h"
#include "Feedback/ContextEffects/AnimNotify_LyraContextEffects.h"
#include "Feedback/ContextEffects/LyraContextEffectsLibrary.h"
#include "Feedback/ContextEffects/LyraContextEffectsSubsystem.h"
#include "GameplayTagsManager.h"
#include "GameplayTagsSettings.h"
#include "Misc/ScopeExit.h"
#include "Animation/AnimSequenceBase.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/BoxComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "PhysicalMaterials/PhysicalMaterial.h"
#include "Physics/Experimental/PhysScene_Chaos.h"
#include "PBDRigidsSolver.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace {
TArray<TSharedPtr<FJsonValue>>* Messages=nullptr;
int32 Call=-1;
TArray<TSharedPtr<FJsonValue>> Vector(FVector V){return {MakeShared<FJsonValueNumber>(V.X),MakeShared<FJsonValueNumber>(V.Y),MakeShared<FJsonValueNumber>(V.Z)};}
void Capture(const TCHAR* Receiver,FName Bone,FGameplayTag Effect,USceneComponent* Mesh,FVector Location,FRotator Rotation,
    const UAnimSequenceBase* Animation,bool Hit,FHitResult Result,FGameplayTagContainer Contexts,FVector Scale,float Volume,float Pitch)
{
    if(!Messages)return;
    auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("call"),Call);Row->SetStringField(TEXT("receiver"),Receiver);
    Row->SetStringField(TEXT("bone"),Bone.ToString());Row->SetStringField(TEXT("effect"),Effect.ToString());
    Row->SetStringField(TEXT("animation"),Animation->GetPathName());Row->SetBoolField(TEXT("hit"),Hit);
    Row->SetArrayField(TEXT("location"),Vector(Location));Row->SetArrayField(TEXT("rotation"),Vector(FVector(Rotation.Pitch,Rotation.Yaw,Rotation.Roll)));
    Row->SetArrayField(TEXT("scale"),Vector(Scale));Row->SetNumberField(TEXT("volume"),Volume);Row->SetNumberField(TEXT("pitch"),Pitch);
    Row->SetArrayField(TEXT("point"),Vector(Result.ImpactPoint));Row->SetArrayField(TEXT("normal"),Vector(Result.ImpactNormal));
    Row->SetBoolField(TEXT("physicalMaterial"),Result.PhysMaterial.IsValid());
    Row->SetNumberField(TEXT("surface"),Result.PhysMaterial.IsValid()?int32(Result.PhysMaterial->SurfaceType):-1);
    TArray<TSharedPtr<FJsonValue>> Tags;for(auto Tag:Contexts)Tags.Add(MakeShared<FJsonValueString>(Tag.ToString()));Row->SetArrayField(TEXT("contexts"),Tags);
    Messages->Add(MakeShared<FJsonValueObject>(Row));
}
int32 ArrayCount(UObject* Object,const TCHAR* Name)
{
    auto Property=FindFProperty<FArrayProperty>(Object->GetClass(),Name);
    return Property?FScriptArrayHelper(Property,Property->ContainerPtrToValuePtr<void>(Object)).Num():-1;
}
}
void ALyraContextOracleActor::AnimMotionEffect_Implementation(FName Bone,FGameplayTag Effect,USceneComponent* Mesh,FVector Location,
    FRotator Rotation,const UAnimSequenceBase* Animation,bool Hit,FHitResult Result,FGameplayTagContainer Contexts,FVector Scale,float Volume,float Pitch)
{Capture(TEXT("actor"),Bone,Effect,Mesh,Location,Rotation,Animation,Hit,Result,Contexts,Scale,Volume,Pitch);}
void ULyraContextOracleComponent::AnimMotionEffect_Implementation(FName Bone,FGameplayTag Effect,USceneComponent* Mesh,FVector Location,
    FRotator Rotation,const UAnimSequenceBase* Animation,bool Hit,FHitResult Result,FGameplayTagContainer Contexts,FVector Scale,float Volume,float Pitch)
{
    Capture(TEXT("component"),Bone,Effect,Mesh,Location,Rotation,Animation,Hit,Result,Contexts,Scale,Volume,Pitch);
    // Execute the original component aggregation and subsystem. Audio is disabled
    // in this world; selected SoundBase entries still append null spawn results.
    Super::AnimMotionEffect_Implementation(Bone,Effect,Mesh,Location,Rotation,Animation,Hit,Result,Contexts,Scale,Volume,Pitch);
    auto Row=Messages->Last()->AsObject();Row->SetNumberField(TEXT("audioCount"),ArrayCount(this,TEXT("ActiveAudioComponents")));
    Row->SetNumberField(TEXT("vfxCount"),ArrayCount(this,TEXT("ActiveNiagaraComponents")));
}
FString ULyraContextEffectsOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    TSharedPtr<FJsonObject> Request;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Request))return {};
    auto Tags=GetMutableDefault<UGameplayTagsSettings>();auto PreviousTables=Tags->GameplayTagTableList;
    auto Settings=GetMutableDefault<ULyraContextEffectsSettings>();auto PreviousMap=Settings->SurfaceTypeToContextMap;
    ON_SCOPE_EXIT {Messages=nullptr;Settings->SurfaceTypeToContextMap=PreviousMap;Tags->GameplayTagTableList=PreviousTables;UGameplayTagsManager::Get().EditorRefreshGameplayTagTree();};
    Tags->GameplayTagTableList.AddUnique(FSoftObjectPath(TEXT("/Game/ContextEffects/DT_AnimEffectTags.DT_AnimEffectTags")));
    Tags->GameplayTagTableList.AddUnique(FSoftObjectPath(TEXT("/Game/ContextEffects/DT_SurfaceTypes.DT_SurfaceTypes")));
    UGameplayTagsManager::Get().EditorRefreshGameplayTagTree();
    Settings->SurfaceTypeToContextMap.Empty();const TCHAR* Names[]={TEXT("SurfaceType.Default"),TEXT("SurfaceType.Character"),TEXT("SurfaceType.Concrete"),TEXT("SurfaceType.Glass")};
    for(int32 I=0;I<4;I++)Settings->SurfaceTypeToContextMap.Add(TEnumAsByte<EPhysicalSurface>(I),FGameplayTag::RequestGameplayTag(Names[I]));
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).RequiresHitProxies(false).CreatePhysicsScene(true)
        .CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::PIE,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return {};
    auto Actor=World->SpawnActor<ALyraContextOracleActor>();auto Mesh=NewObject<USkeletalMeshComponent>(Actor);Actor->AddInstanceComponent(Mesh);Actor->SetRootComponent(Mesh);
    auto Skin=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin"));if(!Skin)return {};
    Mesh->SetSkeletalMesh(Skin);Mesh->SetCollisionEnabled(ECollisionEnabled::NoCollision);Mesh->RegisterComponent();Mesh->RefreshBoneTransforms();
    auto Receiver=NewObject<ULyraContextOracleComponent>(Actor);Actor->AddInstanceComponent(Receiver);Receiver->RegisterComponent();
    auto Library=LoadObject<ULyraContextEffectsLibrary>(nullptr,TEXT("/Game/ContextEffects/CFX_DefaultSkin.CFX_DefaultSkin"));
    Receiver->UpdateLibraries({TSoftObjectPtr<ULyraContextEffectsLibrary>(Library)});
    auto FloorActor=World->SpawnActor<AActor>();auto Floor=NewObject<UBoxComponent>(FloorActor);FloorActor->SetRootComponent(Floor);FloorActor->AddInstanceComponent(Floor);
    Floor->SetBoxExtent(FVector(1000,1000,5));Floor->SetCollisionEnabled(ECollisionEnabled::QueryOnly);Floor->SetCollisionResponseToAllChannels(ECR_Block);Floor->RegisterComponent();
    TStrongObjectPtr<UPhysicalMaterial> Material(NewObject<UPhysicalMaterial>(World.Get()));
    Floor->SetPhysMaterialOverride(Material.Get());
    // Flush native material creation commands with the first physics step.
    // This isolated query-only world has no dynamic simulated bodies.
    const FVector Gravity=FVector::ZeroVector;const float Delta=1.f/60;auto Scene=World->GetPhysicsScene();
    Scene->SetUpForFrame(&Gravity,Delta,0,Delta,Delta,1,false);Scene->StartFrame();Scene->WaitPhysScenes();Scene->EndFrame();
    World->GetPhysicsScene()->GetSolver()->SyncQueryMaterials_External();
    TArray<TSharedPtr<FJsonValue>> Calls,Results;Messages=&Results;Call=-1;
    for(const auto& Value:Request->GetArrayField(TEXT("calls")))
    {
        auto Input=Value->AsObject();auto Asset=LoadObject<UAnimSequenceBase>(nullptr,*Input->GetStringField(TEXT("asset")));const auto Index=Input->GetIntegerField(TEXT("index"));
        if(!Asset||!Asset->Notifies.IsValidIndex(Index))return {};
        const auto& Definition=Asset->Notifies[Index];auto Notify=Cast<UAnimNotify_LyraContextEffects>(Definition.Notify);if(!Notify)return {};
        const int32 Scenario=Input->GetIntegerField(TEXT("scenario"));Call++;
        FGameplayTagContainer CurrentContexts;const TArray<TSharedPtr<FJsonValue>>* TagsInput=nullptr;
        if(Input->TryGetArrayField(TEXT("contexts"),TagsInput))for(const auto& Tag:*TagsInput)CurrentContexts.AddTag(FGameplayTag::RequestGameplayTag(FName(*Tag->AsString())));
        Receiver->UpdateEffectContexts(CurrentContexts);bool Convert=true,Loaded=true;
        Input->TryGetBoolField(TEXT("convert"),Convert);Input->TryGetBoolField(TEXT("library"),Loaded);Receiver->bConvertPhysicalSurfaceToContext=Convert;
        Receiver->UpdateLibraries(Loaded?TSet<TSoftObjectPtr<ULyraContextEffectsLibrary>>{TSoftObjectPtr<ULyraContextEffectsLibrary>(Library)}:TSet<TSoftObjectPtr<ULyraContextEffectsLibrary>>{});
        bool Unload=false;Input->TryGetBoolField(TEXT("unload"),Unload);
        if(Unload)World->GetSubsystem<ULyraContextEffectsSubsystem>()->UnloadAndRemoveContextEffectsLibraries(Actor);
        Mesh->SetWorldTransform(Scenario==5?FTransform(FRotator(0,37,0),FVector(120,-55,80),FVector(1.7)):FTransform(FVector(0,0,50)));
        Mesh->RefreshBoneTransforms();const auto Start=Notify->bAttached?Mesh->GetSocketLocation(Notify->SocketName):Mesh->GetComponentLocation();
        const double Top=Start.Z-(Scenario==4?100:25);Floor->SetWorldLocation(FVector(Start.X,Start.Y,Top-5));
        Material->SurfaceType=TEnumAsByte<EPhysicalSurface>(Scenario<4?Scenario:2);Floor->SetPhysMaterialOverride(Material.Get());
        Notify->Notify(Mesh,Asset,FAnimNotifyEventReference(&Definition,Asset));
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("object"),Notify->GetPathName());Row->SetArrayField(TEXT("start"),Vector(Start));
        Row->SetArrayField(TEXT("end"),Vector(Start+Notify->TraceProperties.EndTraceLocationOffset));Row->SetNumberField(TEXT("floorTop"),Top);
        Row->SetArrayField(TEXT("component"),Vector(Mesh->GetComponentLocation()));Row->SetNumberField(TEXT("messages"),Results.Num());Calls.Add(MakeShared<FJsonValueObject>(Row));
    }
    Messages=nullptr;auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("calls"),Calls);Result->SetArrayField(TEXT("messages"),Results);
    FString Text;auto Writer=TJsonWriterFactory<>::Create(&Text);const bool Success=FJsonSerializer::Serialize(Result,Writer);
    World->DestroyWorld(false);return Success?Text:FString();
}

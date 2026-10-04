#include "LyraMotionWarpingOracleLibrary.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimSequence.h"
#include "AnimNotifyState_MotionWarping.h"
#include "Components/CapsuleComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Blueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "Engine/SCS_Node.h"
#include "Engine/SimpleConstructionScript.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "MotionWarpingComponent.h"
#include "MotionWarpingAdapter.h"
#include "RootMotionModifier_SkewWarp.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace MotionWarpingProbe
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_MOTION_WARPING_FAILED line=%d"),L);return {};}
TArray<TSharedPtr<FJsonValue>> Vector(const FVector& P){return {MakeShared<FJsonValueNumber>(P.X),MakeShared<FJsonValueNumber>(P.Y),MakeShared<FJsonValueNumber>(P.Z)};}
TSharedPtr<FJsonObject> Atom(const FTransform& T)
{
    auto R=MakeShared<FJsonObject>();const auto Q=T.GetRotation();R->SetArrayField(TEXT("position"),Vector(T.GetTranslation()));R->SetArrayField(TEXT("scale"),Vector(T.GetScale3D()));
    R->SetArrayField(TEXT("rotation"),{MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)});return R;
}
FTransform Parse(const TSharedPtr<FJsonObject>& R)
{const auto P=R->GetArrayField(TEXT("position"));const auto Q=R->GetArrayField(TEXT("rotation"));const auto S=R->GetArrayField(TEXT("scale"));return FTransform(FQuat(Q[0]->AsNumber(),Q[1]->AsNumber(),Q[2]->AsNumber(),Q[3]->AsNumber()),FVector(P[0]->AsNumber(),P[1]->AsNumber(),P[2]->AsNumber()),FVector(S[0]->AsNumber(),S[1]->AsNumber(),S[2]->AsNumber()));}
FString ToJson(const TSharedRef<FJsonObject>& R){FString S;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&S));return S;}
struct FInstanceAccess:UAnimInstance
{
    static void Tick(UAnimInstance* A,float D){A->NotifyQueue.AnimNotifies.Reset();A->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();(A->*&FInstanceAccess::Montage_UpdateWeight)(D);(A->*&FInstanceAccess::Montage_Advance)(D);}
};
TSharedPtr<FJsonObject> Snapshot(URootMotionModifier* M,int32 Id)
{
    auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("id"),Id);R->SetStringField(TEXT("animation"),M->Animation.IsValid()?M->Animation->GetPathName():TEXT(""));
    R->SetNumberField(TEXT("state"),(int32)M->GetState());R->SetNumberField(TEXT("start"),M->StartTime);R->SetNumberField(TEXT("end"),M->EndTime);
    R->SetNumberField(TEXT("previous"),M->PreviousPosition);R->SetNumberField(TEXT("current"),M->CurrentPosition);R->SetNumberField(TEXT("weight"),M->Weight);R->SetNumberField(TEXT("rate"),M->PlayRate);
    R->SetNumberField(TEXT("actualStart"),M->ActualStartTime);R->SetObjectField(TEXT("startTransform"),Atom(M->StartTransform));R->SetObjectField(TEXT("totalWindow"),Atom(M->TotalRootMotionWithinWindow));
    auto* Warp=Cast<URootMotionModifier_Warp>(M);if(Warp)
    {
        auto S=StaticCastSharedPtr<FRootMotionModifier_WarpSwapState>(Warp->GetSwapState());R->SetObjectField(TEXT("target"),Atom(S->CachedTargetTransform));
        R->SetObjectField(TEXT("remaining"),Atom(S->RootMotionRemainingAfterNotify));R->SetBoolField(TEXT("offsetPresent"),S->CachedOffsetFromWarpPoint.IsSet());
        R->SetObjectField(TEXT("offset"),Atom(S->CachedOffsetFromWarpPoint.Get(FTransform::Identity)));R->SetBoolField(TEXT("rootPaused"),S->bRootMotionPaused);R->SetBoolField(TEXT("warpPaused"),S->bWarpingPaused);
    }
    return R;
}
}

FString ULyraMotionWarpingOracleLibrary::ReadUsage()
{
    using namespace MotionWarpingProbe;auto R=MakeShared<FJsonObject>();
    auto* B=LoadObject<UBlueprint>(nullptr,TEXT("/ShooterCore/Game/Emote/GA_Emote.GA_Emote"));if(!B||!B->GeneratedClass)return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Defaults;
    for(TFieldIterator<FProperty> It(B->GeneratedClass,EFieldIteratorFlags::IncludeSuper);It;++It)
    {
        if(It->GetOwnerStruct()!=B->GeneratedClass)continue;FString Text;It->ExportTextItem_Direct(Text,It->ContainerPtrToValuePtr<void>(B->GeneratedClass->GetDefaultObject()),nullptr,B->GeneratedClass->GetDefaultObject(),PPF_None);
        auto D=MakeShared<FJsonObject>();D->SetStringField(TEXT("name"),It->GetName());D->SetStringField(TEXT("value"),Text);Defaults.Add(MakeShared<FJsonValueObject>(D));
    }
    R->SetArrayField(TEXT("abilityDefaults"),Defaults);TArray<TSharedPtr<FJsonValue>> Nodes;
    for(UEdGraph* G:B->UbergraphPages)for(UEdGraphNode* N:G->Nodes)
    {
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("graph"),G->GetName());Row->SetStringField(TEXT("guid"),N->NodeGuid.ToString());Row->SetStringField(TEXT("class"),N->GetClass()->GetPathName());TArray<TSharedPtr<FJsonValue>> Pins;
        for(UEdGraphPin* P:N->Pins)
        {
            auto Pin=MakeShared<FJsonObject>();Pin->SetStringField(TEXT("name"),P->PinName.ToString());Pin->SetNumberField(TEXT("direction"),(int32)P->Direction);Pin->SetStringField(TEXT("category"),P->PinType.PinCategory.ToString());
            Pin->SetStringField(TEXT("defaultValue"),P->DefaultValue);Pin->SetStringField(TEXT("defaultObject"),P->DefaultObject?P->DefaultObject->GetPathName():TEXT(""));TArray<TSharedPtr<FJsonValue>> Links;
            for(UEdGraphPin* L:P->LinkedTo){auto Link=MakeShared<FJsonObject>();Link->SetStringField(TEXT("node"),L->GetOwningNode()->NodeGuid.ToString());Link->SetStringField(TEXT("pin"),L->PinName.ToString());Links.Add(MakeShared<FJsonValueObject>(Link));}
            Pin->SetArrayField(TEXT("links"),Links);Pins.Add(MakeShared<FJsonValueObject>(Pin));
        }
        Row->SetArrayField(TEXT("pins"),Pins);Nodes.Add(MakeShared<FJsonValueObject>(Row));
    }
    R->SetArrayField(TEXT("abilityNodes"),Nodes);
    auto* Hero=LoadObject<UBlueprint>(nullptr,TEXT("/ShooterCore/Game/B_Hero_ShooterMannequin.B_Hero_ShooterMannequin"));if(!Hero)return Fail(__LINE__);TArray<TSharedPtr<FJsonValue>> Components;
    if(Hero->SimpleConstructionScript)for(USCS_Node* N:Hero->SimpleConstructionScript->GetAllNodes())
    {auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("name"),N->GetVariableName().ToString());V->SetStringField(TEXT("class"),N->ComponentClass->GetPathName());Components.Add(MakeShared<FJsonValueObject>(V));}
    R->SetArrayField(TEXT("heroSCSComponents"),Components);
    TArray<TSharedPtr<FJsonValue>> Roots;
    for(const TCHAR* Path:{TEXT("/Game/Characters/Heroes/Mannequin/Animations/Actions/AM_MF_Emote_FingerGuns_Emote_MW.AM_MF_Emote_FingerGuns_Emote_MW"),
        TEXT("/Game/Weapons/Pistol/Animations/AM_MM_Pistol_Reload_Emote_MW.AM_MM_Pistol_Reload_Emote_MW"),
        TEXT("/Game/Weapons/Rifle/Animations/AM_MM_Rifle_Reload_Emote_MW.AM_MM_Rifle_Reload_Emote_MW"),
        TEXT("/Game/Weapons/Shotgun/Animations/AM_MM_Shotgun_Reload_Emote_MW.AM_MM_Shotgun_Reload_Emote_MW")})
    {
        auto* M=LoadObject<UAnimMontage>(nullptr,Path);if(!M)return Fail(__LINE__);
        for(const auto& Track:M->SlotAnimTracks)for(const auto& Segment:Track.AnimTrack.AnimSegments)if(auto* S=Cast<UAnimSequence>(Segment.GetAnimReference().Get()))S->WaitOnExistingCompression(true);
        for(const auto& E:M->Notifies)if(Cast<UAnimNotifyState_MotionWarping>(E.NotifyStateClass.Get()))
        {auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("montage"),Path);V->SetNumberField(TEXT("end"),FMath::Clamp(E.GetEndTriggerTime(),0.f,M->GetPlayLength()));V->SetObjectField(TEXT("root"),Atom(UMotionWarpingUtilities::ExtractRootTransformFromAnimation(M,FMath::Clamp(E.GetEndTriggerTime(),0.f,M->GetPlayLength()))));Roots.Add(MakeShared<FJsonValueObject>(V));}
    }
    R->SetArrayField(TEXT("windowEndRoots"),Roots);return ToJson(R);
}

FString ULyraMotionWarpingOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    using namespace MotionWarpingProbe;TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* Manny=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));if(!Manny)return Fail(__LINE__);TArray<UAnimMontage*> Assets;
    for(const auto& V:Q->GetArrayField(TEXT("montages")))
    {auto* M=LoadObject<UAnimMontage>(nullptr,*V->AsString());if(!M)return Fail(__LINE__);Assets.Add(M);for(const auto& Track:M->SlotAnimTracks)for(const auto& Segment:Track.AnimTrack.AnimSegments)if(auto* S=Cast<UAnimSequence>(Segment.GetAnimReference().Get()))S->WaitOnExistingCompression(true);}
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Q->GetArrayField(TEXT("traces")))
    {
        const auto T=TV->AsObject();const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{World.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* Actor=World->SpawnActor<ACharacter>(Spawn);if(!Actor)return Fail(__LINE__);
        Actor->SetActorTransform(Parse(T->GetObjectField(TEXT("actor"))),false,nullptr,ETeleportType::TeleportPhysics);Actor->GetCapsuleComponent()->SetCapsuleHalfHeight(T->GetNumberField(TEXT("capsuleHalfHeight")));
        auto* Mesh=Actor->GetMesh();Mesh->SetSkeletalMesh(Manny);Mesh->SetRelativeTransform(Parse(T->GetObjectField(TEXT("relative"))));Actor->CacheInitialMeshOffset(Mesh->GetRelativeLocation(),Mesh->GetRelativeRotation());Mesh->SetAnimInstanceClass(UAnimInstance::StaticClass());Mesh->InitAnim(true);
        auto* A=Mesh->GetAnimInstance();if(!A)return Fail(__LINE__);A->SetRootMotionMode(ERootMotionMode::RootMotionFromMontagesOnly);auto* Movement=Actor->GetCharacterMovement();
        auto* Comp=NewObject<UMotionWarpingComponent>(Actor,NAME_None,RF_Transient);Actor->AddInstanceComponent(Comp);Comp->RegisterComponent();if(!Comp->HasBeenInitialized())Comp->InitializeComponent();
        if(!Comp->GetOwnerAdapter()||!Movement->ProcessRootMotionPreConvertToWorld.IsBound())return Fail(__LINE__);
        auto Settings=MakeShared<FJsonObject>();Settings->SetBoolField(TEXT("searchSegments"),Comp->bSearchForWindowsInAnimsWithinMontages);Settings->SetNumberField(TEXT("capsuleHalfHeight"),Actor->GetCapsuleComponent()->GetScaledCapsuleHalfHeight());
        Settings->SetObjectField(TEXT("baseOffset"),Atom(FTransform(Actor->GetBaseRotationOffset(),Actor->GetBaseTranslationOffset())));
        TMap<URootMotionModifier*,int32> Ids;int32 Serial=0;TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            auto F=FV->AsObject();const float D=F->GetNumberField(TEXT("delta"));const int32 Op=F->GetIntegerField(TEXT("targetOperation"));
            if(Op==1){FMotionWarpingTarget Target(TEXT("Align"),Parse(F->GetObjectField(TEXT("target"))));Target.bWarpingPaused=F->GetBoolField(TEXT("warpPaused"));Target.bRootMotionPaused=F->GetBoolField(TEXT("rootPaused"));Comp->AddOrUpdateWarpTarget(Target);}else if(Op==2)Comp->RemoveWarpTarget(TEXT("Align"));
            if(F->GetBoolField(TEXT("disable")))Comp->DisableAllRootMotionModifiers();
            for(const auto& PV:F->GetArrayField(TEXT("plays"))){const auto P=PV->AsObject();A->Montage_Play(Assets[P->GetIntegerField(TEXT("asset"))],P->GetNumberField(TEXT("rate")),EMontagePlayReturnType::MontageLength,P->GetNumberField(TEXT("start")),true);}
            const int32 Stop=F->GetIntegerField(TEXT("stop"));if(Stop>=0)A->Montage_Stop(F->GetNumberField(TEXT("stopTime")),Assets[Stop]);
            const float Seek=F->GetNumberField(TEXT("seek"));if(Seek>=0)if(auto* M=A->GetRootMotionMontageInstance())M->SetPosition(Seek);
            FInstanceAccess::Tick(A,D);const auto Local=A->ConsumeExtractedRootMotion(1);auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("actor"),Atom(Actor->GetActorTransform()));Row->SetObjectField(TEXT("component"),Atom(Mesh->GetComponentTransform()));
            Row->SetObjectField(TEXT("visualRoot"),Atom(FTransform(Actor->GetActorQuat(),Comp->GetOwnerAdapter()->GetVisualRootLocation())));Row->SetBoolField(TEXT("present"),Local.bHasRootMotion);Row->SetObjectField(TEXT("local"),Atom(Local.GetRootMotionTransform()));
            auto* Current=A->GetRootMotionMontageInstance();auto Context=MakeShared<FJsonObject>();Context->SetNumberField(TEXT("asset"),Current?Assets.IndexOfByKey(Current->Montage.Get()):-1);Context->SetNumberField(TEXT("previous"),Current?Current->GetPreviousPosition():0);Context->SetNumberField(TEXT("current"),Current?Current->GetPosition():0);Context->SetNumberField(TEXT("weight"),Current?Current->GetWeight():0);Context->SetNumberField(TEXT("rate"),Current?Current->GetPlayRate()*Current->Montage->RateScale:1);Row->SetObjectField(TEXT("context"),Context);
            const auto Warped=Local.bHasRootMotion?Movement->ProcessRootMotionPreConvertToWorld.Execute(Local.GetRootMotionTransform(),Movement,D):Local.GetRootMotionTransform();Row->SetObjectField(TEXT("warped"),Atom(Warped));
            const auto WorldMotion=Mesh->ConvertLocalRootMotionToWorld(Warped);Row->SetObjectField(TEXT("world"),Atom(WorldMotion));
            TArray<TSharedPtr<FJsonValue>> Mods;for(auto* M:Comp->GetModifiers()){auto* Id=Ids.Find(M);if(!Id){const int32 New=++Serial;Ids.Add(M,New);Id=Ids.Find(M);}Mods.Add(MakeShared<FJsonValueObject>(Snapshot(M,*Id)));}Row->SetArrayField(TEXT("modifiers"),Mods);
            const auto Before=Actor->GetActorTransform();if(Local.bHasRootMotion){Actor->SetActorLocation(Before.GetLocation()+WorldMotion.GetTranslation(),false,nullptr,ETeleportType::TeleportPhysics);Actor->SetActorRotation(WorldMotion.GetRotation()*Before.GetRotation(),ETeleportType::TeleportPhysics);}Row->SetObjectField(TEXT("nextActor"),Atom(Actor->GetActorTransform()));Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("settings"),Settings);Row->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);return ToJson(R);
}

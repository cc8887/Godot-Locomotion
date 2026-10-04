#include "LyraEmoteOracleLibrary.h"
#include "AbilitySystemComponent.h"
#include "AbilitySystem/LyraAbilitySystemComponent.h"
#include "AbilitySystem/Abilities/LyraGameplayAbility.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimSequence.h"
#include "Character/LyraCharacterWithAbilities.h"
#include "Components/CapsuleComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Blueprint.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "K2Node_CallFunction.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace EmoteProbe
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_EMOTE_FAILED line=%d"),L);return {};}
FString ToJson(const TSharedRef<FJsonObject>& R){FString S;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&S));return S;}
TArray<TSharedPtr<FJsonValue>> Vector(FVector V){return {MakeShared<FJsonValueNumber>(V.X),MakeShared<FJsonValueNumber>(V.Y),MakeShared<FJsonValueNumber>(V.Z)};}
FVector Parse(const TArray<TSharedPtr<FJsonValue>>& V){return FVector(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber());}
struct FInstanceAccess:UAnimInstance
{
    static void Tick(UAnimInstance* A,float D){A->NotifyQueue.AnimNotifies.Reset();A->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();(A->*&FInstanceAccess::Montage_UpdateWeight)(D);(A->*&FInstanceAccess::Montage_Advance)(D);}
    static void Dispatch(UAnimInstance* A){A->NotifyQueue.AnimNotifies.Reset();A->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();(A->*&FInstanceAccess::DispatchQueuedAnimEvents)();}
};
TSharedPtr<FJsonObject> Snapshot(ALyraCharacterWithAbilities* C,UAbilitySystemComponent* ASC,FGameplayAbilitySpecHandle Handle,
    UGameplayAbility* Ability,UAnimInstance* A,UAnimMontage* Montage,ULyraEmoteProbeListener* Listener)
{
    auto R=MakeShared<FJsonObject>();auto* Spec=ASC->FindAbilitySpecFromHandle(Handle);
    R->SetBoolField(TEXT("active"),Spec&&Spec->IsActive());R->SetBoolField(TEXT("bound"),Ability&&C->OnCharacterMovementUpdated.GetAllObjects().Contains(Ability));
    R->SetNumberField(TEXT("listeners"),C->OnCharacterMovementUpdated.GetAllObjects().Num());R->SetNumberField(TEXT("observerCalls"),Listener->Calls);
    R->SetBoolField(TEXT("crouched"),C->bIsCrouched);R->SetBoolField(TEXT("wantsCrouch"),C->GetCharacterMovement()->bWantsToCrouch);
    R->SetBoolField(TEXT("animatingAbility"),ASC->GetAnimatingAbility()==Ability&&Ability);R->SetBoolField(TEXT("currentMontage"),ASC->GetCurrentMontage()==Montage);
    R->SetBoolField(TEXT("stopped"),A->Montage_GetIsStopped(Montage));R->SetNumberField(TEXT("rootScale"),C->GetAnimRootMotionTranslationScale());
    auto* M=A->GetActiveInstanceForMontage(Montage);R->SetBoolField(TEXT("lookup"),M!=nullptr);
    R->SetNumberField(TEXT("position"),M?M->GetPosition():0);R->SetNumberField(TEXT("previous"),M?M->GetPreviousPosition():0);
    R->SetNumberField(TEXT("weight"),M?M->GetWeight():0);R->SetBoolField(TEXT("playing"),M&&M->IsPlaying());
    R->SetBoolField(TEXT("rootOwner"),A->GetRootMotionMontageInstance()!=nullptr);return R;
}
}

FString ULyraEmoteOracleLibrary::ReadPolicy()
{
    using namespace EmoteProbe;auto R=MakeShared<FJsonObject>();auto* B=LoadObject<UBlueprint>(nullptr,TEXT("/ShooterCore/Game/Emote/GA_Emote.GA_Emote"));
    if(!B||!B->GeneratedClass)return Fail(__LINE__);auto* D=Cast<UGameplayAbility>(B->GeneratedClass->GetDefaultObject());if(!D)return Fail(__LINE__);
    R->SetStringField(TEXT("class"),B->GeneratedClass->GetPathName());R->SetNumberField(TEXT("instancing"),(int32)D->GetInstancingPolicy());
    R->SetNumberField(TEXT("netExecution"),(int32)D->GetNetExecutionPolicy());TArray<TSharedPtr<FJsonValue>> Properties,Functions;
    for(TFieldIterator<FProperty> It(B->GeneratedClass,EFieldIteratorFlags::IncludeSuper);It;++It)
    {
        FString Value;It->ExportTextItem_Direct(Value,It->ContainerPtrToValuePtr<void>(D),nullptr,D,PPF_None);
        auto P=MakeShared<FJsonObject>();P->SetStringField(TEXT("name"),It->GetName());P->SetStringField(TEXT("owner"),It->GetOwnerStruct()->GetPathName());P->SetStringField(TEXT("value"),Value);Properties.Add(MakeShared<FJsonValueObject>(P));
    }
    for(UEdGraph* G:B->UbergraphPages)for(UEdGraphNode* N:G->Nodes)if(auto* Call=Cast<UK2Node_CallFunction>(N))
    {auto F=MakeShared<FJsonObject>();F->SetStringField(TEXT("node"),N->NodeGuid.ToString());F->SetStringField(TEXT("function"),Call->GetFunctionName().ToString());Functions.Add(MakeShared<FJsonValueObject>(F));}
    R->SetArrayField(TEXT("properties"),Properties);R->SetArrayField(TEXT("functions"),Functions);return ToJson(R);
}

FString ULyraEmoteOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    using namespace EmoteProbe;TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* BP=LoadObject<UBlueprint>(nullptr,TEXT("/ShooterCore/Game/Emote/GA_Emote.GA_Emote"));auto* Manny=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));
    auto* Montage=LoadObject<UAnimMontage>(nullptr,*Q->GetStringField(TEXT("montage")));auto* Other=LoadObject<UAnimMontage>(nullptr,*Q->GetStringField(TEXT("otherMontage")));
    if(!BP||!BP->GeneratedClass||!Manny||!Montage||!Other)return Fail(__LINE__);
    for(auto* Asset:{Montage,Other})for(const auto& T:Asset->SlotAnimTracks)for(const auto& S:T.AnimTrack.AnimSegments)if(auto* Sequence=Cast<UAnimSequence>(S.GetAnimReference().Get()))Sequence->WaitOnExistingCompression(true);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& V:Q->GetArrayField(TEXT("traces")))
    {
        auto Input=V->AsObject();const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
        auto* C=World->SpawnActor<ALyraCharacterWithAbilities>(FVector(0,0,90),FRotator::ZeroRotator);if(!C)return Fail(__LINE__);
        auto* Mesh=C->GetMesh();Mesh->SetSkeletalMeshAsset(Manny);Mesh->SetRelativeLocation(FVector(0,0,-90));Mesh->SetAnimInstanceClass(UAnimInstance::StaticClass());
        auto* A=Mesh->GetAnimInstance();auto* ASC=C->GetAbilitySystemComponent();if(!A||!ASC)return Fail(__LINE__);A->SetRootMotionMode(ERootMotionMode::RootMotionFromMontagesOnly);
        C->GetCharacterMovement()->SetMovementMode(MOVE_Walking);ASC->InitAbilityActorInfo(C,C);
        auto Handle=ASC->GiveAbility(FGameplayAbilitySpec(Cast<UGameplayAbility>(BP->GeneratedClass->GetDefaultObject()),1));if(!Handle.IsValid())return Fail(__LINE__);
        auto* Listener=NewObject<ULyraEmoteProbeListener>(C);C->OnCharacterMovementUpdated.AddDynamic(Listener,&ULyraEmoteProbeListener::Movement);
        UGameplayAbility* Ability=nullptr;TArray<TSharedPtr<FJsonValue>> Frames;int32 Ends=0;
        ASC->OnAbilityEnded.AddLambda([&](const FAbilityEndedData&){++Ends;});
        for(const auto& FV:Input->GetArrayField(TEXT("frames")))
        {
            auto F=FV->AsObject();const float D=F->GetNumberField(TEXT("delta"));auto Row=MakeShared<FJsonObject>();
            C->bIsCrouched=F->GetBoolField(TEXT("crouched"));C->GetCharacterMovement()->bWantsToCrouch=C->bIsCrouched;
            if(F->GetBoolField(TEXT("activate")))
            {bool Success=ASC->TryActivateAbility(Handle,false);Row->SetBoolField(TEXT("activationAccepted"),Success);Ability=ASC->FindAbilitySpecFromHandle(Handle)->GetPrimaryInstance();}
            else Row->SetBoolField(TEXT("activationAccepted"),false);
            Row->SetObjectField(TEXT("beforeAdvance"),Snapshot(C,ASC,Handle,Ability,A,Montage,Listener));
            if(F->GetBoolField(TEXT("cancel")))ASC->CancelAbilityHandle(Handle);
            if(F->GetBoolField(TEXT("interrupt")))A->Montage_Play(Other);
            FInstanceAccess::Tick(A,D);A->ConsumeExtractedRootMotion(1);
            Row->SetObjectField(TEXT("afterAdvance"),Snapshot(C,ASC,Handle,Ability,A,Montage,Listener));
            if(F->GetBoolField(TEXT("applyUncrouch"))&&!C->GetCharacterMovement()->bWantsToCrouch)C->bIsCrouched=false;
            if(F->GetBoolField(TEXT("movement")))C->OnCharacterMovementUpdated.Broadcast(D,FVector::ZeroVector,Parse(F->GetArrayField(TEXT("oldVelocity"))));
            Row->SetObjectField(TEXT("afterMovement"),Snapshot(C,ASC,Handle,Ability,A,Montage,Listener));
            FInstanceAccess::Dispatch(A);Row->SetObjectField(TEXT("afterDispatch"),Snapshot(C,ASC,Handle,Ability,A,Montage,Listener));Row->SetNumberField(TEXT("ends"),Ends);Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        auto T=MakeShared<FJsonObject>();T->SetArrayField(TEXT("frames"),Frames);T->SetNumberField(TEXT("duration"),Montage->GetPlayLength());T->SetNumberField(TEXT("blendOut"),Montage->BlendOut.GetBlendTime());
        Traces.Add(MakeShared<FJsonValueObject>(T));ASC->CancelAllAbilities();World->DestroyWorld(false);
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);return ToJson(R);
}

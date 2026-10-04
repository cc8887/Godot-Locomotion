#include "LyraRootMovementOracleLibrary.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimSequence.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
namespace RootMovementProbe
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_ROOT_MOVEMENT_FAILED line=%d"),L);return {};}
struct FInstanceAccess:UAnimInstance
{
    static void Tick(UAnimInstance* A,float D)
    {A->NotifyQueue.AnimNotifies.Reset();A->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();(A->*&FInstanceAccess::Montage_UpdateWeight)(D);(A->*&FInstanceAccess::Montage_Advance)(D);}
};
struct FMovementAccess:UCharacterMovementComponent
{
    static FVector Calc(UCharacterMovementComponent* M,const FVector& P,float D,const FVector& V){return (M->*&FMovementAccess::CalcAnimRootMotionVelocity)(P,D,V);}
    static FVector Constrain(UCharacterMovementComponent* M,const FVector& R,const FVector& V){return (M->*&FMovementAccess::ConstrainAnimRootMotionVelocity)(R,V);}
};
TArray<TSharedPtr<FJsonValue>> Vector(const FVector& P){return {MakeShared<FJsonValueNumber>(P.X),MakeShared<FJsonValueNumber>(P.Y),MakeShared<FJsonValueNumber>(P.Z)};}
TSharedPtr<FJsonObject> Atom(const FTransform& T)
{
    auto R=MakeShared<FJsonObject>();const auto Q=T.GetRotation();R->SetArrayField(TEXT("position"),Vector(T.GetTranslation()));R->SetArrayField(TEXT("scale"),Vector(T.GetScale3D()));
    R->SetArrayField(TEXT("rotation"),{MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)});return R;
}
FTransform ReadAtom(const TSharedPtr<FJsonObject>& R)
{const auto P=R->GetArrayField(TEXT("position"));const auto Q=R->GetArrayField(TEXT("rotation"));const auto S=R->GetArrayField(TEXT("scale"));return FTransform(FQuat(Q[0]->AsNumber(),Q[1]->AsNumber(),Q[2]->AsNumber(),Q[3]->AsNumber()),FVector(P[0]->AsNumber(),P[1]->AsNumber(),P[2]->AsNumber()),FVector(S[0]->AsNumber(),S[1]->AsNumber(),S[2]->AsNumber()));}
}
FString ULyraRootMovementOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    using namespace RootMovementProbe;TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* Parent=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("mainClass")));auto* Defaults=Parent?Cast<UAnimInstance>(Parent->GetDefaultObject()):nullptr;
    auto* Manny=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));if(!Defaults||!Manny||Defaults->RootMotionMode!=ERootMotionMode::RootMotionFromMontagesOnly)return Fail(__LINE__);
    TArray<UAnimMontage*> Montages;TArray<TSharedPtr<FJsonValue>> References;
    for(const auto& V:Q->GetArrayField(TEXT("montages")))
    {
        auto* M=LoadObject<UAnimMontage>(nullptr,*V->AsString());if(!M||M->SlotAnimTracks.IsEmpty())return Fail(__LINE__);Montages.Add(M);
        auto* S=Cast<UAnimSequence>(M->SlotAnimTracks[0].AnimTrack.AnimSegments[0].GetAnimReference().Get());if(!S)return Fail(__LINE__);S->WaitOnExistingCompression(true);
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("montage"),M->GetPathName());R->SetStringField(TEXT("sequence"),S->GetPathName());R->SetBoolField(TEXT("enabled"),M->HasRootMotion());
        R->SetObjectField(TEXT("reference"),Atom(S->GetSkeleton()->GetReferenceSkeleton().GetRefBonePose()[0]));R->SetBoolField(TEXT("normalizedScale"),S->bUseNormalizedRootMotionScale);References.Add(MakeShared<FJsonValueObject>(R));
    }
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Q->GetArrayField(TEXT("traces")))
    {
        const auto T=TV->AsObject();const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{World.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
        auto* Actor=World->SpawnActor<ACharacter>(Spawn);if(!Actor)return Fail(__LINE__);auto* Mesh=Actor->GetMesh();Mesh->SetSkeletalMesh(Manny);Mesh->SetAnimInstanceClass(UAnimInstance::StaticClass());Mesh->InitAnim(true);
        auto* A=Mesh->GetAnimInstance();auto* Movement=Actor->GetCharacterMovement();if(!A)return Fail(__LINE__);A->SetRootMotionMode(Defaults->RootMotionMode);int32 Serial=0;TMap<int32,int32> Ids;
        auto Identity=[&](FAnimMontageInstance* I){if(!I)return 0;auto* Known=Ids.Find(I->GetInstanceID());if(Known)return *Known;const int32 N=++Serial;Ids.Add(I->GetInstanceID(),N);return N;};
        TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            const auto F=FV->AsObject();const float D=F->GetNumberField(TEXT("delta"));
            for(const auto& PV:F->GetArrayField(TEXT("plays")))
            {const auto P=PV->AsObject();const int32 Asset=P->GetIntegerField(TEXT("asset"));if(!Montages.IsValidIndex(Asset))return Fail(__LINE__);A->Montage_Play(Montages[Asset],P->GetNumberField(TEXT("rate")),EMontagePlayReturnType::MontageLength,P->GetNumberField(TEXT("start")),P->GetBoolField(TEXT("stopGroup")));Identity(A->GetActiveInstanceForMontage(Montages[Asset]));}
            const int32 Stop=F->GetIntegerField(TEXT("stop"));if(Stop>=0)A->Montage_Stop(F->GetNumberField(TEXT("stopTime")),Montages[Stop]);
            auto* Before=A->GetRootMotionMontageInstance();const int32 BeforeId=Identity(Before);const float Start=Before?Before->GetPosition():0;
            FInstanceAccess::Tick(A,D);auto Local=A->ConsumeExtractedRootMotion(1);auto Empty=A->ConsumeExtractedRootMotion(1);
            auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("before"),BeforeId);R->SetNumberField(TEXT("after"),Identity(A->GetRootMotionMontageInstance()));R->SetNumberField(TEXT("start"),Start);
            R->SetBoolField(TEXT("present"),Local.bHasRootMotion);R->SetBoolField(TEXT("secondPresent"),Empty.bHasRootMotion);R->SetObjectField(TEXT("local"),Atom(Local.GetRootMotionTransform()));
            Actor->SetActorTransform(ReadAtom(F->GetObjectField(TEXT("actor"))),false,nullptr,ETeleportType::TeleportPhysics);Mesh->SetRelativeTransform(ReadAtom(F->GetObjectField(TEXT("relative"))));
            R->SetObjectField(TEXT("actor"),Atom(Actor->GetActorTransform()));R->SetObjectField(TEXT("component"),Atom(Mesh->GetComponentTransform()));
            const auto LocalMotion=Local.GetRootMotionTransform();const auto WorldMotion=Mesh->ConvertLocalRootMotionToWorld(LocalMotion);R->SetObjectField(TEXT("world"),Atom(WorldMotion));
            const auto V=F->GetArrayField(TEXT("velocity"));const FVector Current(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber());Movement->MovementMode=F->GetBoolField(TEXT("falling"))?MOVE_Falling:MOVE_Walking;
            const auto Velocity=D>0?FMovementAccess::Calc(Movement,WorldMotion.GetTranslation(),D,Current):Current;
            R->SetArrayField(TEXT("calculated"),Vector(Velocity));R->SetArrayField(TEXT("constrained"),Vector(FMovementAccess::Constrain(Movement,Velocity,Current)));
            Rows.Add(MakeShared<FJsonValueObject>(R));
        }
        auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("frames"),Rows);Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("mode"),(int32)Defaults->RootMotionMode.GetValue());R->SetArrayField(TEXT("references"),References);R->SetArrayField(TEXT("traces"),Traces);
    FString Output;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Output));return Output;
}

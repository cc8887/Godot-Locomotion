#include "AlsLyraFootPlacementSeededLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "Animation/Skeleton.h"
#include "BoneControllers/AnimNode_FootPlacement.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/BoxComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace LyraFootSeededProbe
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_FOOT_PLACEMENT_FAILED line=%d"),L);return {};}
struct FInstanceAccess:UAnimInstance{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S)
    {(P.*&FProxyAccess::InitializeObjects)(A);TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add((FBoneIndexType)I);P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*S);P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);}
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D){(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
};
struct FNodeAccess:FAnimNode_SkeletalControlBase{static void Cache(FAnimNode_FootPlacement& N,const FBoneContainer& B){(N.*&FNodeAccess::InitializeBoneReferences)(B);}};
template<class Tag,typename Tag::Type Member>struct TAccess{friend typename Tag::Type Access(Tag){return Member;}};
struct FLegsTag{using Type=TArray<UE::Anim::FootPlacement::FLegRuntimeData> FAnimNode_FootPlacement::*;friend Type Access(FLegsTag);};template struct TAccess<FLegsTag,&FAnimNode_FootPlacement::LegsData>;
struct FPelvisTag{using Type=UE::Anim::FootPlacement::FPelvisRuntimeData FAnimNode_FootPlacement::*;friend Type Access(FPelvisTag);};template struct TAccess<FPelvisTag,&FAnimNode_FootPlacement::PelvisData>;
struct FCharacterTag{using Type=UE::Anim::FootPlacement::FCharacterData FAnimNode_FootPlacement::*;friend Type Access(FCharacterTag);};template struct TAccess<FCharacterTag,&FAnimNode_FootPlacement::CharacterData>;
struct FDeltaTag{using Type=float FAnimNode_FootPlacement::*;friend Type Access(FDeltaTag);};template struct TAccess<FDeltaTag,&FAnimNode_FootPlacement::CachedDeltaTime>;
struct FFirstTag{using Type=bool FAnimNode_FootPlacement::*;friend Type Access(FFirstTag);};template struct TAccess<FFirstTag,&FAnimNode_FootPlacement::bIsFirstUpdate>;
struct FCounterTag{using Type=FGraphTraversalCounter FAnimNode_FootPlacement::*;friend Type Access(FCounterTag);};template struct TAccess<FCounterTag,&FAnimNode_FootPlacement::UpdateCounter>;
TSharedPtr<FJsonValue> Vector(const FVector& V){return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{MakeShared<FJsonValueNumber>(V.X),MakeShared<FJsonValueNumber>(V.Y),MakeShared<FJsonValueNumber>(V.Z)});}
TSharedPtr<FJsonValue> Quat(const FQuat& Q){return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)});}
TSharedPtr<FJsonObject> Transform(const FTransform& T){auto O=MakeShared<FJsonObject>();O->SetField(TEXT("p"),Vector(T.GetLocation()));O->SetField(TEXT("q"),Quat(T.GetRotation()));O->SetField(TEXT("s"),Vector(T.GetScale3D()));return O;}
TSharedPtr<FJsonObject> Plane(const FPlane& P){auto O=MakeShared<FJsonObject>();O->SetField(TEXT("normal"),Vector(P.GetNormal()));O->SetNumberField(TEXT("w"),P.W);return O;}
TSharedPtr<FJsonObject> Spring(const FVectorSpringState& S){auto O=MakeShared<FJsonObject>();O->SetField(TEXT("velocity"),Vector(S.Velocity));O->SetField(TEXT("target"),Vector(S.PrevTarget));O->SetBoolField(TEXT("valid"),S.bPrevTargetValid);return O;}
TSharedPtr<FJsonObject> Spring(const FFloatSpringState& S){auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("velocity"),S.Velocity);O->SetNumberField(TEXT("target"),S.PrevTarget);O->SetBoolField(TEXT("valid"),S.bPrevTargetValid);return O;}
TSharedPtr<FJsonObject> Spring(const FQuaternionSpringState& S){auto O=MakeShared<FJsonObject>();O->SetField(TEXT("velocity"),Vector(S.AngularVelocity));O->SetField(TEXT("target"),Quat(S.PrevTarget));O->SetBoolField(TEXT("valid"),S.bPrevTargetValid);return O;}
TSharedPtr<FJsonObject> History(const FAnimNode_FootPlacement& N)
{
    auto O=MakeShared<FJsonObject>();O->SetBoolField(TEXT("first"),N.*Access(FFirstTag{}));O->SetNumberField(TEXT("delta"),N.*Access(FDeltaTag{}));O->SetNumberField(TEXT("counter"),(N.*Access(FCounterTag{})).Get());
    const auto& P=N.*Access(FPelvisTag{});O->SetField(TEXT("pelvisOffset"),Vector(P.Interpolation.PelvisTranslationOffset));O->SetObjectField(TEXT("pelvisSpring"),Spring(P.Interpolation.PelvisTranslationSpringState));
    const auto& C=N.*Access(FCharacterTag{});O->SetObjectField(TEXT("component"),Transform(C.ComponentTransformWS));O->SetField(TEXT("componentDelta"),Vector(C.ComponentMoveDeltaWS));O->SetField(TEXT("groundNormal"),Vector(C.SmoothCapsuleGroundNormalWS));O->SetObjectField(TEXT("groundSpring"),Spring(C.SmoothCapsuleGroundNormalSpringState));O->SetBoolField(TEXT("onGround"),C.bIsOnGround);
    TArray<TSharedPtr<FJsonValue>> Legs;for(const auto& L:N.*Access(FLegsTag{}))
    {
        auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("limbLength"),L.Bones.LimbLength);V->SetNumberField(TEXT("footLength"),L.Bones.FootLength);V->SetObjectField(TEXT("plane"),Plane(L.Plant.PlantPlaneRS));V->SetObjectField(TEXT("alignedRS"),Transform(L.AlignedFootTransformRS));V->SetObjectField(TEXT("alignedWS"),Transform(L.AlignedFootTransformWS));V->SetObjectField(TEXT("unalignedRS"),Transform(L.UnalignedFootTransformRS));V->SetObjectField(TEXT("unalignedWS"),Transform(L.UnalignedFootTransformWS));V->SetObjectField(TEXT("heightSpring"),Spring(L.Interpolation.GroundHeightSpringState));V->SetObjectField(TEXT("rotationSpring"),Spring(L.Interpolation.GroundRotationSpringState));V->SetObjectField(TEXT("offsetSpring"),Spring(L.Interpolation.PlantOffsetTranslationSpringState));V->SetObjectField(TEXT("offsetRotationSpring"),Spring(L.Interpolation.PlantOffsetRotationSpringState));V->SetNumberField(TEXT("alignment"),L.InputPose.AlignmentAlpha);V->SetNumberField(TEXT("distance"),L.InputPose.DistanceToPlant);V->SetNumberField(TEXT("speed"),L.InputPose.Speed);V->SetBoolField(TEXT("reachable"),L.Plant.bCanReachTarget);V->SetNumberField(TEXT("plantType"),(int32)L.Plant.PlantType);Legs.Add(MakeShared<FJsonValueObject>(V));
    }O->SetArrayField(TEXT("legs"),Legs);return O;
}
FVector ReadVector(const TSharedPtr<FJsonObject>& F,const TCHAR* K){const auto& A=F->GetArrayField(K);return FVector(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber());}
FQuat ReadQuat(const TSharedPtr<FJsonObject>& F,const TCHAR* K){const auto& A=F->GetArrayField(K);return FQuat(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber(),A[3]->AsNumber());}
}
FString UAlsLyraFootPlacementSeededLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UClass*>& LayerClasses,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraFootSeededProbe;TSharedPtr<FJsonObject> Input;if(!MainClass||!Mesh||!Skeleton||LayerClasses.Num()!=3||Sequences.IsEmpty()||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Input->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());auto T=TV->AsObject();int32 Provider=T->GetNumberField(TEXT("provider"));FAnimNode_FootPlacement N;int32 Count=0;
        for(TFieldIterator<FStructProperty> P(LayerClasses[Provider]);P;++P)if(P->Struct==FAnimNode_FootPlacement::StaticStruct()){N=*P->ContainerPtrToValuePtr<FAnimNode_FootPlacement>(LayerClasses[Provider]->GetDefaultObject());++Count;}
        if(Count!=1||N.PlantSettings.LockType!=EFootPlacementLockType::Unlocked)return Fail(__LINE__);N.ComponentPose=FComponentSpacePoseLink();
        auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* Owner=W->SpawnActor<ACharacter>(Spawn);auto* Ground=W->SpawnActor<AActor>(Spawn);
        if(!Owner||!Ground)return Fail(__LINE__);TStrongObjectPtr<UBoxComponent> Box(NewObject<UBoxComponent>(Ground,NAME_None,RF_Transient));Ground->SetRootComponent(Box.Get());Box->SetBoxExtent(FVector(10000,10000,5));Box->SetCollisionProfileName(TEXT("BlockAll"));Ground->AddInstanceComponent(Box.Get());Box->RegisterComponent();
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MainClass);C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);
        Carrier->SetSkeleton(Skeleton);auto& Proxy=FInstanceAccess::Proxy(Main);FProxyAccess::Setup(Proxy,Main,Skeleton);N.Initialize_AnyThread(FAnimationInitializeContext(&Proxy));
        // Controlled defined initial storage: the installed ResetRuntimeData uses
        // SetNumUninitialized and otherwise leaves first-use plant fields undefined.
        auto& Storage=N.*Access(FLegsTag{});for(int32 I=0;I<Storage.Num();++I){Storage[I]=UE::Anim::FootPlacement::FLegRuntimeData();Storage[I].Idx=I;}
        FNodeAccess::Cache(N,Proxy.GetRequiredBones());
        auto Trace=MakeShared<FJsonObject>();Trace->SetObjectField(TEXT("initial"),History(N));Trace->SetNumberField(TEXT("initialCounter"),Proxy.GetUpdateCounter().Get());TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark Frame(FMemStack::Get());auto F=FV->AsObject();auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("before"),History(N));
            const FTransform Component(ReadQuat(F,TEXT("componentQ")),ReadVector(F,TEXT("componentP")));C->SetWorldTransform(Component,false,nullptr,ETeleportType::TeleportPhysics);
            const FVector Normal=ReadVector(F,TEXT("floorNormal"));const FVector Point=ReadVector(F,TEXT("floorPoint"));Box->SetCollisionEnabled(F->GetBoolField(TEXT("geometry"))?ECollisionEnabled::QueryOnly:ECollisionEnabled::NoCollision);Box->SetWorldTransform(FTransform(FQuat::FindBetweenNormals(FVector::UpVector,Normal),Point-Normal*5),false,nullptr,ETeleportType::TeleportPhysics);
            auto* Move=Owner->GetCharacterMovement();Move->MovementMode=F->GetBoolField(TEXT("walking"))?MOVE_Walking:MOVE_Falling;Move->CurrentFloor.bBlockingHit=F->GetBoolField(TEXT("blocking"));Move->CurrentFloor.HitResult.ImpactPoint=Point;Move->CurrentFloor.HitResult.ImpactNormal=Normal;Move->Velocity=ReadVector(F,TEXT("velocity"));
            const float D=F->GetNumberField(TEXT("delta")),Alpha=F->GetNumberField(TEXT("alpha"));FProxyAccess::Pre(Proxy,Main,D);if(F->GetBoolField(TEXT("initialize"))){N.Initialize_AnyThread(FAnimationInitializeContext(&Proxy));FNodeAccess::Cache(N,Proxy.GetRequiredBones());}
            if(F->GetBoolField(TEXT("visited"))&&Alpha>ZERO_ANIMWEIGHT_THRESH)N.UpdateInternal(FAnimationUpdateContext(&Proxy,D));R->SetObjectField(TEXT("updated"),History(N));
            if(F->GetBoolField(TEXT("visited"))&&F->GetBoolField(TEXT("evaluate")))
            {
                FPoseContext Base(&Proxy);FDeltaTimeRecord I;I.Set(F->GetNumberField(TEXT("previous")),F->GetNumberField(TEXT("sourceDelta")));FAnimationPoseData Data(Base);FAnimExtractContext E(F->GetNumberField(TEXT("time")),false,I,true);E.bExtractWithRootMotionProvider=true;Sequences[(int32)F->GetNumberField(TEXT("asset"))]->GetAnimationPose(Data,E);
                R->SetObjectField(TEXT("input"),LyraCyclePoseProbe::PoseData(Base.Pose,Base.Curve,Base.CustomAttributes,Skeleton->GetReferenceSkeleton()));FComponentSpacePoseContext Out(&Proxy);Out.Pose.InitPose(Base.Pose);Out.Curve.CopyFrom(Base.Curve);Out.CustomAttributes.CopyFrom(Base.CustomAttributes);
                TArray<TSharedPtr<FJsonValue>> Hits;const FVector Direction=Component.TransformVectorNoScale(-FVector::UpVector);
                for(const auto& Def:N.LegDefinitions)
                {
                    const FVector StartPosition=Component.TransformPosition(Out.Pose.GetComponentSpaceTransform(Def.IKFootBone.GetCompactPoseIndex(Proxy.GetRequiredBones())).GetLocation());FHitResult Hit;FCollisionQueryParams Params;Params.bTraceComplex=true;Params.AddIgnoredActor(Owner);
                    const bool HitFound=W->SweepSingleByChannel(Hit,StartPosition+Direction*N.TraceSettings.StartOffset,StartPosition+Direction*N.TraceSettings.EndOffset,FQuat::Identity,UEngineTypes::ConvertToCollisionChannel(N.TraceSettings.ComplexTraceChannel),FCollisionShape::MakeSphere(N.TraceSettings.SweepRadius),Params);
                    auto H=MakeShared<FJsonObject>();H->SetField(TEXT("start"),Vector(StartPosition));H->SetBoolField(TEXT("hit"),HitFound);H->SetBoolField(TEXT("walkable"),HitFound&&Move->IsWalkable(Hit));H->SetField(TEXT("point"),Vector(Hit.ImpactPoint));H->SetField(TEXT("normal"),Vector(Hit.ImpactNormal));Hits.Add(MakeShared<FJsonValueObject>(H));
                }R->SetArrayField(TEXT("hits"),Hits);
                TArray<FBoneTransform> Changes;if(Alpha>ZERO_ANIMWEIGHT_THRESH){if(!N.IsValidToEvaluate(Skeleton,Proxy.GetRequiredBones()))return Fail(__LINE__);N.EvaluateSkeletalControl_AnyThread(Out,Changes);if(!Changes.IsEmpty())Out.Pose.LocalBlendCSBoneTransforms(Changes,Alpha);}
                FCompactPose After=Out.Pose.GetPose();FCSPose<FCompactPose>::ConvertComponentPosesToLocalPosesSafe(Out.Pose,After);R->SetObjectField(TEXT("output"),LyraCyclePoseProbe::PoseData(After,Out.Curve,Out.CustomAttributes,Skeleton->GetReferenceSkeleton()));R->SetNumberField(TEXT("changedBones"),Changes.Num());
            }
            R->SetObjectField(TEXT("after"),History(N));Rows.Add(MakeShared<FJsonValueObject>(R));
        }
        Trace->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));Trace->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));Trace->SetArrayField(TEXT("frames"),Rows);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);FString Json;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Json));return Json;
}

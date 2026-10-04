#include "LyraWholeMainOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/Skeleton.h"
#include "Animation/AnimNode_Root.h"
#include "BoneControllers/AnimNode_SkeletalControlBase.h"
#include "BoneControllers/AnimNode_FootPlacement.h"
#include "BoneControllers/AnimNode_LegIK.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Kismet/KismetMathLibrary.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace LyraSkeletalInitialize
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_SKELETAL_INITIALIZE_FAILED line=%d"),L);return {};}
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FControlAccess:FAnimNode_SkeletalControlBase
{static bool Valid(FAnimNode_SkeletalControlBase& N,const USkeleton* S,const FBoneContainer& B){return (N.*&FControlAccess::IsValidToEvaluate)(S,B);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S)
    {
        (P.*&FProxyAccess::InitializeObjects)(A);TArray<FBoneIndexType> B;
        for(int32 I=0;I<81;++I)B.Add(static_cast<FBoneIndexType>(I));
        P.GetRequiredBones().InitializeTo(B,UE::Anim::FCurveFilterSettings(),*S);
        P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);
    }
};
template<class Tag,typename Tag::Type Member>struct TAccess{friend typename Tag::Type Access(Tag){return Member;}};
struct FLegsTag{using Type=TArray<UE::Anim::FootPlacement::FLegRuntimeData> FAnimNode_FootPlacement::*;friend Type Access(FLegsTag);};template struct TAccess<FLegsTag,&FAnimNode_FootPlacement::LegsData>;
struct FPelvisTag{using Type=UE::Anim::FootPlacement::FPelvisRuntimeData FAnimNode_FootPlacement::*;friend Type Access(FPelvisTag);};template struct TAccess<FPelvisTag,&FAnimNode_FootPlacement::PelvisData>;
struct FCharacterTag{using Type=UE::Anim::FootPlacement::FCharacterData FAnimNode_FootPlacement::*;friend Type Access(FCharacterTag);};template struct TAccess<FCharacterTag,&FAnimNode_FootPlacement::CharacterData>;
struct FDeltaTag{using Type=float FAnimNode_FootPlacement::*;friend Type Access(FDeltaTag);};template struct TAccess<FDeltaTag,&FAnimNode_FootPlacement::CachedDeltaTime>;
struct FFirstTag{using Type=bool FAnimNode_FootPlacement::*;friend Type Access(FFirstTag);};template struct TAccess<FFirstTag,&FAnimNode_FootPlacement::bIsFirstUpdate>;
struct FCounterTag{using Type=FGraphTraversalCounter FAnimNode_FootPlacement::*;friend Type Access(FCounterTag);};template struct TAccess<FCounterTag,&FAnimNode_FootPlacement::UpdateCounter>;
TSharedPtr<FJsonValue> Vector(const FVector& V)
{return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{MakeShared<FJsonValueNumber>(V.X),MakeShared<FJsonValueNumber>(V.Y),MakeShared<FJsonValueNumber>(V.Z)});}
TSharedPtr<FJsonValue> Quat(const FQuat& Q)
{return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)});}
TSharedPtr<FJsonObject> Transform(const FTransform& T)
{auto O=MakeShared<FJsonObject>();O->SetField(TEXT("p"),Vector(T.GetLocation()));O->SetField(TEXT("q"),Quat(T.GetRotation()));O->SetField(TEXT("s"),Vector(T.GetScale3D()));return O;}
TSharedPtr<FJsonObject> Spring(const FVectorSpringState& S)
{auto O=MakeShared<FJsonObject>();O->SetField(TEXT("velocity"),Vector(S.Velocity));O->SetField(TEXT("target"),Vector(S.PrevTarget));O->SetBoolField(TEXT("valid"),S.bPrevTargetValid);return O;}
TSharedPtr<FJsonObject> Spring(const FFloatSpringState& S)
{auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("velocity"),S.Velocity);O->SetNumberField(TEXT("target"),S.PrevTarget);O->SetBoolField(TEXT("valid"),S.bPrevTargetValid);return O;}
TSharedPtr<FJsonObject> Spring(const FQuaternionSpringState& S)
{auto O=MakeShared<FJsonObject>();O->SetField(TEXT("velocity"),Vector(S.AngularVelocity));O->SetField(TEXT("target"),Quat(S.PrevTarget));O->SetBoolField(TEXT("valid"),S.bPrevTargetValid);return O;}
TSharedPtr<FJsonObject> Bool(const FInputAlphaBoolBlend& B)
{
    auto O=MakeShared<FJsonObject>();O->SetBoolField(TEXT("initialized"),B.bInitialized);
    O->SetNumberField(TEXT("begin"),B.AlphaBlend.GetBeginValue());O->SetNumberField(TEXT("target"),B.AlphaBlend.GetDesiredValue());
    O->SetNumberField(TEXT("alpha"),B.AlphaBlend.GetAlpha());O->SetNumberField(TEXT("value"),B.AlphaBlend.GetBlendedValue());
    O->SetNumberField(TEXT("time"),B.AlphaBlend.GetBlendTime());O->SetNumberField(TEXT("remaining"),B.AlphaBlend.GetBlendTimeRemaining());return O;
}
void BoneRefs(UStruct* Type,void* Data,const FString& Prefix,const FBoneContainer& Bones,const TSharedPtr<FJsonObject>& Out,int32 Depth=0)
{
    check(Depth<12);
    if(Type==FBoneReference::StaticStruct())
    {auto& B=*static_cast<FBoneReference*>(Data);auto O=MakeShared<FJsonObject>();O->SetStringField(TEXT("name"),B.BoneName.ToString());O->SetNumberField(TEXT("index"),B.GetCompactPoseIndex(Bones).GetInt());Out->SetObjectField(Prefix.LeftChop(1),O);return;}
    for(TFieldIterator<FProperty> It(Type);It;++It)
    {
        FProperty* P=*It;FString Name=Prefix+P->GetName();void* Value=P->ContainerPtrToValuePtr<void>(Data);
        if(auto* S=CastField<FStructProperty>(P))
        {
            if(S->Struct==FBoneReference::StaticStruct())
            {auto& B=*static_cast<FBoneReference*>(Value);auto O=MakeShared<FJsonObject>();O->SetStringField(TEXT("name"),B.BoneName.ToString());O->SetNumberField(TEXT("index"),B.GetCompactPoseIndex(Bones).GetInt());Out->SetObjectField(Name,O);}
            else BoneRefs(S->Struct,Value,Name+TEXT("."),Bones,Out,Depth+1);
        }
        else if(auto* A=CastField<FArrayProperty>(P))if(auto* InnerStruct=CastField<FStructProperty>(A->Inner))
        {FScriptArrayHelper Array(A,Value);for(int32 I=0;I<Array.Num();++I)BoneRefs(InnerStruct->Struct,Array.GetRawPtr(I),Name+FString::Printf(TEXT("[%d]."),I),Bones,Out,Depth+1);}
    }
}
TSharedPtr<FJsonObject> Node(FAnimNode_SkeletalControlBase& N,UScriptStruct* Type,FAnimInstanceProxy& P)
{
    auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("alpha"),N.GetAlpha());O->SetObjectField(TEXT("bool"),Bool(N.AlphaBoolBlend));
    O->SetBoolField(TEXT("clampInitialized"),N.AlphaScaleBiasClamp.bInitialized);O->SetNumberField(TEXT("clampValue"),N.AlphaScaleBiasClamp.InterpolatedResult);
    O->SetBoolField(TEXT("clampInterp"),N.AlphaScaleBiasClamp.bInterpResult);O->SetNumberField(TEXT("alphaType"),static_cast<int32>(N.AlphaInputType));
    auto Refs=MakeShared<FJsonObject>();BoneRefs(Type,&N,TEXT(""),P.GetRequiredBones(),Refs);O->SetObjectField(TEXT("bones"),Refs);return O;
}
TSharedPtr<FJsonObject> Foot(FAnimNode_FootPlacement& N)
{
    auto O=MakeShared<FJsonObject>();O->SetBoolField(TEXT("first"),N.*Access(FFirstTag{}));O->SetNumberField(TEXT("delta"),N.*Access(FDeltaTag{}));O->SetNumberField(TEXT("counter"),(N.*Access(FCounterTag{})).Get());
    const auto& P=N.*Access(FPelvisTag{});O->SetField(TEXT("pelvisOffset"),Vector(P.Interpolation.PelvisTranslationOffset));O->SetObjectField(TEXT("pelvisSpring"),Spring(P.Interpolation.PelvisTranslationSpringState));
    O->SetObjectField(TEXT("root"),Transform(P.InputPose.RootTransformCS));
    const auto& C=N.*Access(FCharacterTag{});O->SetObjectField(TEXT("component"),Transform(C.ComponentTransformWS));O->SetField(TEXT("componentDelta"),Vector(C.ComponentMoveDeltaWS));
    O->SetField(TEXT("groundNormal"),Vector(C.SmoothCapsuleGroundNormalWS));O->SetObjectField(TEXT("groundSpring"),Spring(C.SmoothCapsuleGroundNormalSpringState));O->SetBoolField(TEXT("onGround"),C.bIsOnGround);
    TArray<TSharedPtr<FJsonValue>> Legs;for(const auto& L:N.*Access(FLegsTag{}))
    {auto V=MakeShared<FJsonObject>();V->SetObjectField(TEXT("heightSpring"),Spring(L.Interpolation.GroundHeightSpringState));V->SetObjectField(TEXT("rotationSpring"),Spring(L.Interpolation.GroundRotationSpringState));V->SetObjectField(TEXT("offsetSpring"),Spring(L.Interpolation.PlantOffsetTranslationSpringState));V->SetObjectField(TEXT("offsetRotationSpring"),Spring(L.Interpolation.PlantOffsetRotationSpringState));Legs.Add(MakeShared<FJsonValueObject>(V));}
    O->SetArrayField(TEXT("legs"),Legs);return O;
}
TSharedPtr<FJsonObject> Leg(FAnimNode_LegIK& N,FAnimInstanceProxy& P)
{
    auto O=MakeShared<FJsonObject>();O->SetBoolField(TEXT("proxyBound"),N.MyAnimInstanceProxy==&P);TArray<TSharedPtr<FJsonValue>> Legs;
    for(const auto& L:N.LegsData)
    {auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("fk"),L.LegDefPtr?L.LegDefPtr->FKFootBone.BoneName.ToString():TEXT(""));V->SetNumberField(TEXT("ik"),L.IKFootBoneIndex.GetInt());TArray<TSharedPtr<FJsonValue>> Chain;for(auto I:L.FKLegBoneIndices)Chain.Add(MakeShared<FJsonValueNumber>(I.GetInt()));V->SetArrayField(TEXT("chain"),Chain);V->SetNumberField(TEXT("links"),L.IKChain.Links.Num());if(L.IKChain.Links.Num()==3){V->SetField(TEXT("real"),Vector(L.IKChain.Links[1].RealBendDir));V->SetField(TEXT("base"),Vector(L.IKChain.Links[1].BaseBendDir));}Legs.Add(MakeShared<FJsonValueObject>(V));}
    O->SetArrayField(TEXT("legs"),Legs);return O;
}
USkeleton* TargetSkeleton(const TSharedPtr<FJsonObject>& Q)
{
    auto* Source=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("sourceMesh")));auto* Target=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("targetMesh")));if(!Source||!Target)return nullptr;
    const auto& S=Source->GetSkeleton()->GetReferenceSkeleton();const auto& T=Target->GetSkeleton()->GetReferenceSkeleton();
    // UE Python's existing unreal.Quat constructor uses this float-argument
    // native make function. Preserve that boundary for the resource reference.
    const auto& A=Q->GetArrayField(TEXT("handBasis"));FQuat Basis=UKismetMathLibrary::MakeQuat(static_cast<float>(A[0]->AsNumber()),static_cast<float>(A[1]->AsNumber()),static_cast<float>(A[2]->AsNumber()),static_cast<float>(A[3]->AsNumber()));
    int32 W=S.FindRawBoneIndex(TEXT("weapon_r")),H=T.FindRawBoneIndex(TEXT("hand_r"));if(W==INDEX_NONE||H==INDEX_NONE||T.GetRawBoneNum()!=68||T.GetNum()!=79||!Basis.IsNormalized())return nullptr;
    auto* R=DuplicateObject<USkeleton>(Target->GetSkeleton(),GetTransientPackage());R->ClearFlags(RF_Public|RF_Standalone);R->SetFlags(RF_Transient);
    const auto& Rest=S.GetRawRefBonePose()[W];{FReferenceSkeletonModifier M(R);M.Add(FMeshBoneInfo(TEXT("weapon_r"),TEXT("weapon_r"),H),FTransform((Basis*Rest.GetRotation()).GetNormalized(),Basis.RotateVector(Rest.GetTranslation()),Rest.GetScale3D()));}
    if(!R->AddNewNamedVirtualBone(TEXT("weapon_r"),TEXT("hand_l"),TEXT("VB IK_Hand_L_weaponSpace"))||R->GetReferenceSkeleton().GetNum()!=81)return nullptr;return R;
}
}
FString ULyraWholeMainOracleLibrary::ReadSkeletalInitialization(const FString& RequestsJson)
{
    using namespace LyraSkeletalInitialize;TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* Class=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("main")));auto* Provider=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("provider")));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));TStrongObjectPtr<USkeleton> Skeleton(TargetSkeleton(Q));if(!Class||!Provider||!Mesh||!Skeleton.IsValid())return Fail(__LINE__);
    const FMemMark Mark(FMemStack::Get());const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{World.Get()};
    auto* Actor=World->SpawnActor<AActor>();if(!Actor)return Fail(__LINE__);TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Actor,NAME_None,RF_Transient));
    Component->bUseRefPoseOnInitAnim=true;Component->SetDisablePostProcessBlueprint(true);Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
    Component->SetSkeletalMeshAsset(Carrier.Get());Component->SetAnimInstanceClass(Class);Actor->SetRootComponent(Component.Get());Actor->AddInstanceComponent(Component.Get());Component->RegisterComponent();
    auto* Main=Component->GetAnimInstance();if(!Main)return Fail(__LINE__);Main->LinkAnimClassLayers(Provider);auto* Owner=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Owner||Owner->GetClass()!=Provider)return Fail(__LINE__);
    Carrier->SetSkeleton(Skeleton.Get());auto& Proxy=FInstanceAccess::Proxy(Owner);FProxyAccess::Setup(Proxy,Owner,Skeleton.Get());
    auto* Interface=IAnimClassInterface::GetFromClass(Provider);const auto& Properties=Interface->GetAnimNodeProperties();
    auto* Root=Proxy.GetMutableNodeFromIndex<FAnimNode_Root>(113);auto* F=Proxy.GetMutableNodeFromIndex<FAnimNode_FootPlacement>(105);auto* L=Proxy.GetMutableNodeFromIndex<FAnimNode_LegIK>(107);if(!Root||!F||!L)return Fail(__LINE__);
    const TArray<int32> Ids{103,102,104,110,109,105,107,106};TArray<FAnimNode_SkeletalControlBase*> Nodes;TArray<UScriptStruct*> Types;
    for(int32 Id:Ids){int32 I=Properties.Num()-1-Id;if(!Properties.IsValidIndex(I)||!Properties[I]->Struct->IsChildOf(FAnimNode_SkeletalControlBase::StaticStruct()))return Fail(__LINE__);Nodes.Add(Proxy.GetMutableNodeFromIndex<FAnimNode_SkeletalControlBase>(Id));Types.Add(Properties[I]->Struct);}
    FAnimationInitializeContext Context(&Proxy);Context.SetNodeId(113);FAnimationCacheBonesContext Bones(&Proxy);Bones.SetNodeId(113);
    Root->Initialize_AnyThread(Context);Root->CacheBones_AnyThread(Bones);TArray<TSharedPtr<FJsonValue>> Rounds;
    for(int32 Round=0;Round<2;++Round)
    {
        for(int32 I=0;I<Nodes.Num();++I)
        {auto& N=*Nodes[I];N.ActualAlpha=(I+1)*.125f;N.AlphaBoolBlend.bInitialized=true;N.AlphaBoolBlend.AlphaBlend.SetBlendTime(.2f);N.AlphaBoolBlend.AlphaBlend.SetValueRange(Round?.125f:.25f,Round?.875f:.75f);N.AlphaBoolBlend.AlphaBlend.Update(.03125f);N.AlphaScaleBiasClamp.bInitialized=true;N.AlphaScaleBiasClamp.InterpolatedResult=Round?.375f:.625f;}
        F->*Access(FFirstTag{})=false;F->*Access(FDeltaTag{})=Round?.375f:.03125f;FGraphTraversalCounter Counter;while(Counter.Get()!=(Round?32767:25))Counter.Increment();F->*Access(FCounterTag{})=Counter;
        auto& P=F->*Access(FPelvisTag{});P.Interpolation.PelvisTranslationOffset=FVector(1,2,3);P.Interpolation.PelvisTranslationSpringState.Velocity=FVector(4,5,6);P.Interpolation.PelvisTranslationSpringState.PrevTarget=FVector(7,8,9);P.Interpolation.PelvisTranslationSpringState.bPrevTargetValid=true;P.InputPose.RootTransformCS=FTransform(FVector(10,11,12));
        auto& C=F->*Access(FCharacterTag{});C.ComponentTransformWS=FTransform(FVector(21,22,23));C.ComponentMoveDeltaWS=FVector(24,25,26);C.SmoothCapsuleGroundNormalWS=FVector(0,0,1);C.bIsOnGround=true;C.SmoothCapsuleGroundNormalSpringState.AngularVelocity=FVector(1,2,3);C.SmoothCapsuleGroundNormalSpringState.PrevTarget=FQuat::Identity;C.SmoothCapsuleGroundNormalSpringState.bPrevTargetValid=true;
        for(auto& D:F->*Access(FLegsTag{}))
        {D.Interpolation.GroundHeightSpringState.Velocity=4;D.Interpolation.GroundHeightSpringState.PrevTarget=5;D.Interpolation.GroundHeightSpringState.bPrevTargetValid=true;D.Interpolation.GroundRotationSpringState.AngularVelocity=FVector(1,2,3);D.Interpolation.GroundRotationSpringState.PrevTarget=FQuat::Identity;D.Interpolation.GroundRotationSpringState.bPrevTargetValid=true;D.Interpolation.PlantOffsetTranslationSpringState.Velocity=FVector(4,5,6);D.Interpolation.PlantOffsetTranslationSpringState.PrevTarget=FVector(7,8,9);D.Interpolation.PlantOffsetTranslationSpringState.bPrevTargetValid=true;D.Interpolation.PlantOffsetRotationSpringState=D.Interpolation.GroundRotationSpringState;}
        for(auto& D:L->LegsData){D.IKChain.Links.SetNum(3);D.IKChain.Links[1].RealBendDir=FVector(.25,.5,.75);D.IKChain.Links[1].BaseBendDir=FVector(-.25,-.5,-.75);}L->MyAnimInstanceProxy=nullptr;
        auto Snapshot=[&](){auto O=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Values;for(int32 I=0;I<Nodes.Num();++I){auto V=Node(*Nodes[I],Types[I],Proxy);V->SetNumberField(TEXT("node"),Ids[I]);Values.Add(MakeShared<FJsonValueObject>(V));}O->SetArrayField(TEXT("nodes"),Values);O->SetObjectField(TEXT("foot"),Foot(*F));O->SetObjectField(TEXT("leg"),Leg(*L,Proxy));return O;};
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("round"),Round);Row->SetObjectField(TEXT("before"),Snapshot());Root->Initialize_AnyThread(Context);Row->SetObjectField(TEXT("after"),Snapshot());Root->CacheBones_AnyThread(Bones);Row->SetObjectField(TEXT("cached"),Snapshot());
        TArray<TSharedPtr<FJsonValue>> Lengths;for(const auto& D:F->*Access(FLegsTag{})){Lengths.Add(MakeShared<FJsonValueNumber>(D.Bones.LimbLength));Lengths.Add(MakeShared<FJsonValueNumber>(D.Bones.FootLength));}Row->SetArrayField(TEXT("footLengths"),Lengths);
        for(auto* N:Nodes)if(!FControlAccess::Valid(*N,Skeleton.Get(),Proxy.GetRequiredBones()))return Fail(__LINE__);Rounds.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("rounds"),Rounds);Result->SetBoolField(TEXT("sourceUpdate"),false);Result->SetBoolField(TEXT("poseEvaluate"),false);TArray<TSharedPtr<FJsonValue>> Reference;
    const auto& Ref=Skeleton->GetReferenceSkeleton();for(int32 I=0;I<Ref.GetNum();++I){auto B=Transform(Ref.GetRefBonePose()[I]);B->SetStringField(TEXT("name"),Ref.GetBoneName(I).ToString());B->SetNumberField(TEXT("parent"),Ref.GetParentIndex(I));Reference.Add(MakeShared<FJsonValueObject>(B));}Result->SetArrayField(TEXT("reference"),Reference);
    FString Json;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}

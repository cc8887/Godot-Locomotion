#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "AlsLinkedAnimationInstance.h"
#include "AlsCharacter.h"
#include "Animation/AnimBlueprint.h"
#include "Animation/AnimBlueprintGeneratedClass.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "AnimNodes/AnimNode_BlendSpacePlayer.h"
#include "Animation/Skeleton.h"
#include "Async/Async.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
bool StandingTraceFail(int32 Line)
{UE_LOG(LogTemp,Error,TEXT("ALS_STANDING_HOST_FAILED line=%d frame=%llu"),Line,GFrameCounter);return false;}
struct FStandingTraceProxy : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* I,float D){(P.*&FStandingTraceProxy::PreUpdate)(I,D);}
    static void UpdateRoot(FAnimInstanceProxy& P){(P.*&FStandingTraceProxy::UpdateAnimation)();}
    static void Post(FAnimInstanceProxy& P,UAnimInstance* I){(P.*&FStandingTraceProxy::PostUpdate)(I);}
};
struct FStandingTraceInstance : UAlsLinkedAnimationInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* I){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I);}
    static void ParentTo(UAlsLinkedAnimationInstance* I,UAlsAnimationInstance* P){I->*&FStandingTraceInstance::Parent=P;}
    static void MontageTick(UAnimInstance* I,float D)
    {
        I->NotifyQueue.AnimNotifies.Reset();I->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
        (I->*&FStandingTraceInstance::Montage_UpdateWeight)(D);(I->*&FStandingTraceInstance::Montage_Advance)(D);
        (I->*&FStandingTraceInstance::UpdateMontageEvaluationData)();
    }
};
struct FStandingTraceParent : UAlsAnimationInstance
{
    static void BeforeGraph(UAlsAnimationInstance* I,bool Pivot)
    {(I->*&FStandingTraceParent::RefreshGrounded)();if(Pivot)(I->*&FStandingTraceParent::ActivatePivot)();}
    static void Input(UAlsAnimationInstance* I,AAlsCharacter* C,const TSharedPtr<FJsonObject>& R)
    {
        I->*&FStandingTraceParent::Character=C;
        const bool Moving=R->GetBoolField(TEXT("moving"));const float Side=R->GetNumberField(TEXT("side")),Speed=R->GetNumberField(TEXT("speed"));
        auto& L=I->*&FStandingTraceParent::LocomotionState;
        L.VelocityWorldSpace=Speed>0?FVector(170,100*Side,0):FVector::ZeroVector;L.AccelerationWorldSpace=FVector(200,100,0);
        L.RotationQuaternionWorldSpace=FQuat::Identity;L.RotationWorldSpace=FRotator::ZeroRotator;
        L.Speed=Speed;L.ScaleWorldSpace=1;L.VelocityYawAngleWorldSpace=55*Side;L.MaxAcceleration=1000;L.MaxBrakingDeceleration=800;
        L.bMoving=Moving;L.bMovingSmooth=R->GetBoolField(TEXT("movingSmooth"));L.bHasInput=Moving;L.InputYawAngleWorldSpace=90;L.TargetYawAngleWorldSpace=0;
        auto& V=I->*&FStandingTraceParent::ViewState;V.RotationWorldSpace=FRotator::ZeroRotator;
        V.YawAngle=R->GetNumberField(TEXT("yaw"));V.YawSpeed=0;
        I->*&FStandingTraceParent::RotationMode=R->GetBoolField(TEXT("aiming"))?AlsRotationModeTags::Aiming:AlsRotationModeTags::ViewDirection;
        I->*&FStandingTraceParent::ViewMode=AlsViewModeTags::ThirdPerson;I->*&FStandingTraceParent::Stance=AlsStanceTags::Standing;
        I->*&FStandingTraceParent::Gait=AlsGaitTags::Running;
        auto& P=I->*&FStandingTraceParent::PoseState;P.GroundedAmount=1;P.StandingAmount=1;P.UnweightedGaitRunningAmount=1;P.UnweightedGaitSprintingAmount=0;
        auto& Feet=I->*&FStandingTraceParent::FeetState;Feet.FootPlantedAmount=R->GetNumberField(TEXT("foot"));Feet.FeetCrossingAmount=0;
        Feet.Left.LockAmount=R->GetBoolField(TEXT("dynamic"))?1:0;Feet.Right.LockAmount=0;
        Feet.Left.TargetLocationWorldSpace=FVector(20,0,0);Feet.Left.LockLocationWorldSpace=FVector::ZeroVector;
        Feet.Right.TargetLocationWorldSpace=Feet.Right.LockLocationWorldSpace=FVector::ZeroVector;
        (I->*&FStandingTraceParent::TransitionsState).bTransitionsAllowed=true;
        (I->*&FStandingTraceParent::RotateInPlaceState).bUpdatedThisFrame=false;
        (I->*&FStandingTraceParent::TurnInPlaceState).bUpdatedThisFrame=false;
        (I->*&FStandingTraceParent::DynamicTransitionsState).bUpdatedThisFrame=false;
        FindFProperty<FBoolProperty>(I->GetClass(),TEXT("bPendingUpdate"))->SetPropertyValue_InContainer(I,R->GetBoolField(TEXT("pending")));
        FStandingTraceProxy::Pre(FStandingTraceInstance::Proxy(I),I,R->GetNumberField(TEXT("delta")));
        I->OverrideCurveValue(TEXT("HipsDirectionLock"),0);I->OverrideCurveValue(TEXT("SprintBlock"),.2f);
    }
    static TSharedPtr<FJsonObject> State(UAlsAnimationInstance* I)
    {
        auto R=MakeShared<FJsonObject>();const auto& Rotate=I->*&FStandingTraceParent::RotateInPlaceState;
        R->SetBoolField(TEXT("left"),Rotate.bRotatingLeft);R->SetBoolField(TEXT("right"),Rotate.bRotatingRight);R->SetNumberField(TEXT("rate"),Rotate.PlayRate);
        const auto& Turn=I->*&FStandingTraceParent::TurnInPlaceState;
        R->SetNumberField(TEXT("turnDelay"),Turn.ActivationDelay);R->SetNumberField(TEXT("turnRate"),Turn.PlayRate);
        R->SetNumberField(TEXT("dynamicDelay"),(I->*&FStandingTraceParent::DynamicTransitionsState).FrameDelay);
        const auto& G=I->*&FStandingTraceParent::GroundedState;const auto& B=G.VelocityBlend;const auto& Y=G.RotationYawOffsets;
        const auto& Lean=I->*&FStandingTraceParent::LeanState;const auto& S=I->*&FStandingTraceParent::StandingState;
        TArray<TSharedPtr<FJsonValue>> M;
        for(double V:{double(B.ForwardAmount),double(B.BackwardAmount),double(B.LeftAmount),double(B.RightAmount),double(Lean.RightAmount),double(Lean.ForwardAmount),
            double(Y.ForwardAngle),double(Y.BackwardAngle),double(Y.LeftAngle),double(Y.RightAngle),double(S.StrideBlendAmount),double(S.WalkRunBlendAmount),double(S.PlayRate),
            double(S.SprintBlockAmount),double(S.SprintTime),double(S.SprintAccelerationAmount),double(S.bPivotActive),double(G.HipsDirection)})M.Add(MakeShared<FJsonValueNumber>(V));
        R->SetArrayField(TEXT("movement"),M);return R;
    }
};
}

bool UAlsAnimationGraphLibrary::ExportStandingHostTrace(const FString& RequestPath,const FString& OutputPath)
{
    if(FPaths::IsRelative(RequestPath)||FPaths::IsRelative(OutputPath)||RequestPath==OutputPath)return StandingTraceFail(__LINE__);
    FString Text;TSharedPtr<FJsonObject> Request;
    if(!FFileHelper::LoadFileToString(Text,*RequestPath)||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Request))return StandingTraceFail(__LINE__);
    auto* Blueprint=LoadObject<UAnimBlueprint>(nullptr,TEXT("/ALS/ALS/Character/AnimationInstances/Stances/AB_Als_Standing.AB_Als_Standing"));
    auto* Generated=Blueprint?Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass):nullptr;
    auto* ParentClass=LoadClass<UAlsAnimationInstance>(nullptr,TEXT("/ALS/ALS/Character/AB_Als.AB_Als_C"));
    auto* CharacterClass=LoadClass<AAlsCharacter>(nullptr,TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"));
    auto* Mesh=CharacterClass?CastChecked<AAlsCharacter>(CharacterClass->GetDefaultObject())->GetMesh()->GetSkeletalMeshAsset():nullptr;
    if(!Generated||!ParentClass||!Mesh||!Mesh->GetSkeleton())return StandingTraceFail(__LINE__);
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false)
        .CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
    if(!World.IsValid()||!World->IsGameWorld())return StandingTraceFail(__LINE__);ON_SCOPE_EXIT{World->DestroyWorld(false);};
    const auto& Reference=Mesh->GetSkeleton()->GetReferenceSkeleton();TArray<FBoneIndexType> Required;TArray<TSharedPtr<FJsonValue>> Names;
    for(int32 I=0;I<Reference.GetNum();++I){Required.Add(static_cast<FBoneIndexType>(I));Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(I).ToString()));}
    if(Required.Num()!=79)return StandingTraceFail(__LINE__);
    const auto& Properties=Generated->GetAnimNodeProperties();
    if(Properties.Num()<=203||Properties[65]->Struct!=FAnimNode_StateMachine::StaticStruct())return StandingTraceFail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;int32 Total=0;
    for(const auto& TraceValue:Request->GetArrayField(TEXT("traces")))
    {
        auto* Character=World->SpawnActor<AAlsCharacter>();if(!Character)return StandingTraceFail(__LINE__);ON_SCOPE_EXIT{World->DestroyActor(Character);};
        Character->SetActorTickEnabled(false);auto* Component=Character->GetMesh();Component->SetRelativeTransform(FTransform::Identity);
        Component->SetSkeletalMesh(Mesh);Component->SetAnimInstanceClass(ParentClass);
        auto* Parent=Cast<UAlsAnimationInstance>(Component->GetAnimInstance());if(!Parent)return StandingTraceFail(__LINE__);
        TStrongObjectPtr<UAlsLinkedAnimationInstance> Instance(NewObject<UAlsLinkedAnimationInstance>(Component,Generated));
        Instance->InitializeAnimation(true);FStandingTraceInstance::ParentTo(Instance.Get(),Parent);
        auto& Proxy=FStandingTraceInstance::Proxy(Instance.Get());
        for(auto* P:{&Proxy,&FStandingTraceInstance::Proxy(Parent)})
        {
            P->GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*Mesh->GetSkeleton());
            P->GetRequiredBones().SetUseRAWData(true);P->GetRequiredBones().SetUseSourceData(false);
        }
        auto* Machine=Properties[65]->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Instance.Get());
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FrameValue:TraceValue->AsObject()->GetArrayField(TEXT("frames")))
        {
            const auto Input=FrameValue->AsObject();const float Delta=Input->GetNumberField(TEXT("delta"));
            if(!FMath::IsFinite(Delta)||Delta<0)return StandingTraceFail(__LINE__);
            const TGuardValue<uint64> FrameCounter(GFrameCounter,static_cast<uint64>(950000+Total));
            FStandingTraceParent::Input(Parent,Character,Input);
            FStandingTraceInstance::MontageTick(Parent,Delta);FStandingTraceProxy::Pre(Proxy,Instance.Get(),Delta);
            if(Input->GetBoolField(TEXT("evaluate")))Instance->PreEvaluateAnimation();
            auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("input"),Input);
            Async(EAsyncExecution::ThreadPool,[&]()
            {
                const FMemMark Mark(FMemStack::Get());
                FStandingTraceParent::BeforeGraph(Parent,Input->GetBoolField(TEXT("pivot")));
                FStandingTraceProxy::UpdateRoot(Proxy);Proxy.FlipBufferWriteIndex();
                Row->SetObjectField(TEXT("parent"),FStandingTraceParent::State(Parent));Row->SetNumberField(TEXT("state"),Machine->GetCurrentState());
                TArray<TSharedPtr<FJsonValue>> Players;
                for(int32 Index=0;Index<Properties.Num();++Index)
                {
                    FAnimNode_AssetPlayerBase* Player=nullptr;
                    if(Properties[Index]->Struct==FAnimNode_SequencePlayer::StaticStruct())Player=Properties[Index]->ContainerPtrToValuePtr<FAnimNode_SequencePlayer>(Instance.Get());
                    else if(Properties[Index]->Struct==FAnimNode_BlendSpacePlayer::StaticStruct())Player=Properties[Index]->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayer>(Instance.Get());
                    if(Player){auto P=MakeShared<FJsonObject>();P->SetNumberField(TEXT("node"),Index);P->SetNumberField(TEXT("time"),Player->GetAccumulatedTime());P->SetNumberField(TEXT("weight"),Player->GetCachedBlendWeight());Players.Add(MakeShared<FJsonValueObject>(P));}
                }
                Row->SetArrayField(TEXT("players"),Players);
                if(Input->GetBoolField(TEXT("evaluate")))
                {
                    FPoseContext Pose(&Proxy,true);FBlendedHeapCurve Curve;UE::Anim::FHeapAttributeContainer Attributes;
                    FParallelEvaluationData Evaluation{Curve,Pose.Pose,Attributes};Instance->ParallelEvaluateAnimation(false,Mesh,Evaluation);
                    TArray<TSharedPtr<FJsonValue>> Values;
                    for(const auto Bone:Pose.Pose.ForEachBoneIndex())
                    {
                        const auto& T=Pose.Pose[Bone];const auto P=T.GetTranslation(),S=T.GetScale3D();const auto Q=T.GetRotation();
                        for(double V:{P.X,P.Y,P.Z,Q.X,Q.Y,Q.Z,Q.W,S.X,S.Y,S.Z})Values.Add(MakeShared<FJsonValueNumber>(V));
                    }
                    auto Curves=MakeShared<FJsonObject>();Curve.ForEachElement([&](const auto& C){Curves->SetNumberField(C.Name.ToString(),C.Value);});
                    Row->SetArrayField(TEXT("pose"),Values);Row->SetObjectField(TEXT("curves"),Curves);
                }
            }).Get();
            Instance->NotifyQueue.AnimNotifies.Reset();Instance->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
            FStandingTraceProxy::Post(Proxy,Instance.Get());Parent->NativePostUpdateAnimation();
            int32 Quick=0;
            for(const auto& Event:Instance->NotifyQueue.AnimNotifies)if(const auto* Notify=Event.GetNotify())
                if(Notify->NotifyName==TEXT("StopQuick"))++Quick;
            auto* Function=Instance->FindFunction(TEXT("AnimNotify_StopQuick"));if(!Function)return StandingTraceFail(__LINE__);
            for(int32 I=0;I<Quick;++I)Instance->ProcessEvent(Function,nullptr);
            Row->SetNumberField(TEXT("quick"),Quick);Row->SetObjectField(TEXT("postParent"),FStandingTraceParent::State(Parent));
            TArray<TSharedPtr<FJsonValue>> Montages;
            for(const auto* I:Parent->MontageInstances)
            {
                // Native termination can leave an invalid instance in this list
                // until the next Montage_Advance removes it.
                if(!I||!I->Montage)continue;
                if(I->Montage->SlotAnimTracks.Num()!=1)return StandingTraceFail(__LINE__);
                const auto& Track=I->Montage->SlotAnimTracks[0];if(Track.AnimTrack.AnimSegments.Num()!=1)return StandingTraceFail(__LINE__);
                auto M=MakeShared<FJsonObject>();M->SetStringField(TEXT("source"),Track.AnimTrack.AnimSegments[0].GetAnimReference()->GetPathName());
                M->SetStringField(TEXT("slot"),Track.SlotName.ToString());M->SetNumberField(TEXT("time"),I->GetPosition());M->SetNumberField(TEXT("weight"),I->GetWeight());
                M->SetNumberField(TEXT("rate"),I->GetPlayRate());M->SetBoolField(TEXT("playing"),I->IsPlaying());Montages.Add(MakeShared<FJsonValueObject>(M));
            }
            Row->SetArrayField(TEXT("montages"),Montages);Frames.Add(MakeShared<FJsonValueObject>(Row));++Total;
        }
        Instance->UninitializeAnimation();auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("name"),TraceValue->AsObject()->GetStringField(TEXT("name")));
        Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetNumberField(TEXT("schemaVersion"),1);Result->SetArrayField(TEXT("names"),Names);
    Result->SetObjectField(TEXT("resourceHashes"),Request->GetObjectField(TEXT("resourceHashes")));Result->SetArrayField(TEXT("traces"),Traces);
    FString Json;if(!FJsonSerializer::Serialize(Result,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return StandingTraceFail(__LINE__);
    UE_LOG(LogTemp,Display,TEXT("ALS_STANDING_HOST_NATIVE_OK frames=%d assets_saved=0"),Total);return true;
}

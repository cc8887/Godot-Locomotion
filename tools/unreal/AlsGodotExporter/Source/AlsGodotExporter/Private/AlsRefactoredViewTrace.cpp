#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "AlsCharacter.h"
#include "AlsLinkedAnimationInstance.h"
#include "Animation/AnimBlueprintGeneratedClass.h"
#include "Animation/AnimNode_LinkedInputPose.h"
#include "Animation/AnimSequence.h"
#include "Animation/Skeleton.h"
#include "Engine/SkeletalMesh.h"
#include "Animation/AnimInstanceProxy.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/World.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
struct FViewProxyAccess : FAnimInstanceProxy
{
    static void Prepare(FAnimInstanceProxy& P,UAnimInstance* I,float D){(P.*&FViewProxyAccess::PreUpdate)(I,D);}
    static void UpdateRoot(FAnimInstanceProxy& P){(P.*&FViewProxyAccess::UpdateAnimation)();}
    static void Post(FAnimInstanceProxy& P,UAnimInstance* I){(P.*&FViewProxyAccess::PostUpdate)(I);}
};
struct FHeadInstanceAccess : UAlsLinkedAnimationInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* I){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I);}
    static void SetParent(UAlsLinkedAnimationInstance* I,UAlsAnimationInstance* P){I->*&FHeadInstanceAccess::Parent=P;}
};
struct FViewInstanceAccess : UAlsAnimationInstance
{
    static auto& View(UAlsAnimationInstance* I){return I->*&FViewInstanceAccess::ViewState;}
    static auto& Spine(UAlsAnimationInstance* I){return I->*&FViewInstanceAccess::SpineState;}
    static auto& Head(UAlsAnimationInstance* I){return I->*&FViewInstanceAccess::HeadState;}
    static void Input(UAlsAnimationInstance* I,AAlsCharacter* C,const TSharedPtr<FJsonObject>& V)
    {
        I->*&FViewInstanceAccess::Character=C;
        auto& ViewState=View(I);ViewState.RotationWorldSpace=FRotator(V->GetNumberField(TEXT("ViewPitch")),V->GetNumberField(TEXT("ViewYaw")),0);
        ViewState.YawSpeed=V->GetNumberField(TEXT("ViewYawSpeed"));
        auto& L=I->*&FViewInstanceAccess::LocomotionState;
        L.RotationWorldSpace=FRotator(V->GetNumberField(TEXT("CharacterPitch")),V->GetNumberField(TEXT("CharacterYaw")),0);
        L.YawVelocityWorldSpace=V->GetNumberField(TEXT("CharacterYawVelocity"));
        L.InputYawAngleWorldSpace=V->GetNumberField(TEXT("InputYaw"));L.TargetYawAngleWorldSpace=V->GetNumberField(TEXT("TargetYaw"));L.bHasInput=V->GetBoolField(TEXT("HasInput"));
        auto& B=I->*&FViewInstanceAccess::MovementBase;B.bHasRelativeRotation=V->GetBoolField(TEXT("RelativeBaseRotation"));B.DeltaRotation.Yaw=V->GetNumberField(TEXT("BaseDeltaYaw"));
        const int32 Mode=V->GetIntegerField(TEXT("RotationMode"));
        I->*&FViewInstanceAccess::RotationMode=Mode==0?AlsRotationModeTags::VelocityDirection:Mode==2?AlsRotationModeTags::Aiming:AlsRotationModeTags::ViewDirection;
        I->*&FViewInstanceAccess::ViewMode=V->GetBoolField(TEXT("FirstPerson"))?AlsViewModeTags::FirstPerson:AlsViewModeTags::ThirdPerson;
        I->*&FViewInstanceAccess::LocomotionAction=V->GetBoolField(TEXT("HasAction"))?AlsLocomotionActionTags::Rolling:FGameplayTag();
        I->*&FViewInstanceAccess::DeltaTimeWithoutTimeDilation=V->GetNumberField(TEXT("RealDelta"));
        FindFProperty<FBoolProperty>(I->GetClass(),TEXT("bPendingUpdate"))->SetPropertyValue_InContainer(I,V->GetBoolField(TEXT("PendingUpdate")));
        // No foot/scene simulation is part of this state probe.
        (I->*&FViewInstanceAccess::FeetState).bValid=false;
        FViewProxyAccess::Prepare(*GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I),I,V->GetNumberField(TEXT("Delta")));
        I->OverrideCurveValue(TEXT("ViewBlock"),V->GetNumberField(TEXT("ViewBlock")));
        I->OverrideCurveValue(TEXT("PoseAiming"),V->GetNumberField(TEXT("PoseAiming")));
    }
    static void UpdateParent(UAlsAnimationInstance* I,float Delta){(I->*&FViewInstanceAccess::NativeThreadSafeUpdateAnimation)(Delta);}
    static auto* SettingsAsset(UAlsAnimationInstance* I){return (I->*&FViewInstanceAccess::Settings).Get();}
};
TArray<TSharedPtr<FJsonValue>> Numbers(std::initializer_list<double> Values)
{TArray<TSharedPtr<FJsonValue>> R;for(double V:Values)R.Add(MakeShared<FJsonValueNumber>(V));return R;}
TSharedPtr<FJsonObject> PoseAtom(const FTransform& T)
{
    auto R=MakeShared<FJsonObject>();auto P=T.GetTranslation();auto Q=T.GetRotation();auto S=T.GetScale3D();
    R->SetArrayField(TEXT("position"),Numbers({P.X,P.Y,P.Z}));R->SetArrayField(TEXT("rotation"),Numbers({Q.X,Q.Y,Q.Z,Q.W}));
    R->SetArrayField(TEXT("scale"),Numbers({S.X,S.Y,S.Z}));return R;
}
}

bool UAlsAnimationGraphLibrary::ExportRefactoredViewTrace(const FString& RequestPath,const FString& OutputPath)
{
    if(FPaths::IsRelative(RequestPath)||FPaths::IsRelative(OutputPath)||RequestPath==OutputPath)return false;
    FString Text;TSharedPtr<FJsonObject> Request;
    if(!FFileHelper::LoadFileToString(Text,*RequestPath)||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Request))return false;
    auto* Class=LoadClass<UAlsAnimationInstance>(nullptr,TEXT("/ALS/ALS/Character/AB_Als.AB_Als_C"));if(!Class)return false;
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false)
        .CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
    if(!World.IsValid()||!World->IsGameWorld())return false;ON_SCOPE_EXIT{World->DestroyWorld(false);};
    auto* Character=World->SpawnActor<AAlsCharacter>();if(!Character)return false;Character->SetActorTickEnabled(false);
    bool GraphMode=false;Request->TryGetBoolField(TEXT("headGraph"),GraphMode);
    UAnimBlueprintGeneratedClass* HeadClass=nullptr;USkeletalMesh* Mesh=nullptr;UAnimSequence* Stand=nullptr;
    TArray<FBoneIndexType> Required;TArray<TSharedPtr<FJsonValue>> Names;
    if(GraphMode)
    {
        HeadClass=Cast<UAnimBlueprintGeneratedClass>(LoadClass<UAlsLinkedAnimationInstance>(nullptr,TEXT("/ALS/ALS/Character/AnimationInstances/AB_Als_Head.AB_Als_Head_C")));
        auto* CharacterClass=LoadClass<AAlsCharacter>(nullptr,TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"));
        auto* Defaults=CharacterClass?Cast<AAlsCharacter>(CharacterClass->GetDefaultObject()):nullptr;
        Mesh=Defaults?Defaults->GetMesh()->GetSkeletalMeshAsset():nullptr;
        Stand=LoadObject<UAnimSequence>(nullptr,TEXT("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose"));
        if(!HeadClass||!Mesh||!Mesh->GetSkeleton()||!Stand)return false;
        Character->GetMesh()->SetSkeletalMesh(Mesh);
        const auto& Reference=Mesh->GetSkeleton()->GetReferenceSkeleton();
        for(int32 Bone=0;Bone<Reference.GetNum();++Bone){Required.Add(static_cast<FBoneIndexType>(Bone));Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Bone).ToString()));}
    }
    TArray<TSharedPtr<FJsonValue>> Traces;int32 Total=0;
    for(const auto& TraceValue:Request->GetArrayField(TEXT("traces")))
    {
        TStrongObjectPtr<UAlsAnimationInstance> Instance(NewObject<UAlsAnimationInstance>(Character->GetMesh(),Class));
        if(!FViewInstanceAccess::SettingsAsset(Instance.Get()))return false;
        TStrongObjectPtr<UAlsLinkedAnimationInstance> Linked;
        FAnimNode_LinkedInputPose* LinkedInput=nullptr;
        if(GraphMode)
        {
            Linked.Reset(NewObject<UAlsLinkedAnimationInstance>(Character->GetMesh(),HeadClass));
            Linked->InitializeAnimation(true);FHeadInstanceAccess::SetParent(Linked.Get(),Instance.Get());
            auto& Proxy=FHeadInstanceAccess::Proxy(Linked.Get());
            Proxy.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*Mesh->GetSkeleton());
            Proxy.GetRequiredBones().SetUseRAWData(true);Proxy.GetRequiredBones().SetUseSourceData(false);
            for(const auto* Property:HeadClass->GetAnimNodeProperties())
                if(Property->Struct==FAnimNode_LinkedInputPose::StaticStruct())
                {if(LinkedInput)return false;LinkedInput=Property->ContainerPtrToValuePtr<FAnimNode_LinkedInputPose>(Linked.Get());}
            if(!LinkedInput||LinkedInput->Name!=TEXT("View Input"))return false;
        }
        auto* Initialize=Instance->FindFunction(TEXT("InitializeHead"));auto* Refresh=Instance->FindFunction(TEXT("RefreshHead"));if(!Initialize||!Refresh)return false;
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FrameValue:TraceValue->AsObject()->GetArrayField(TEXT("frames")))
        {
            FMemMark Mark(FMemStack::Get());const TGuardValue<uint64> GlobalFrame(GFrameCounter,static_cast<uint64>(800000+Total));
            const auto Frame=FrameValue->AsObject();const auto Input=Frame->GetObjectField(TEXT("input"));
            FViewInstanceAccess::Input(Instance.Get(),Character,Input);
            FViewInstanceAccess::UpdateParent(Instance.Get(),Input->GetNumberField(TEXT("Delta")));
            TArray<TSharedPtr<FJsonValue>> Atoms;auto Curves=MakeShared<FJsonObject>();
            if(GraphMode)
            {
                auto& Proxy=FHeadInstanceAccess::Proxy(Linked.Get());
                FViewProxyAccess::Prepare(Proxy,Linked.Get(),Input->GetNumberField(TEXT("Delta")));
                FPoseContext Source(&Proxy);FAnimationPoseData Data(Source);FAnimExtractContext Context(0.,false);Context.bExtractWithRootMotionProvider=false;
                Stand->GetAnimationPose(Data,Context);Source.Curve.Set(TEXT("HeadProbe"),.37f);
                LinkedInput->CachedInputPose.CopyBonesFrom(Source.Pose);LinkedInput->CachedInputCurve.CopyFrom(Source.Curve);LinkedInput->bIsCachedInputPoseInitialized=true;
                FViewProxyAccess::UpdateRoot(Proxy);Proxy.FlipBufferWriteIndex();FViewProxyAccess::Post(Proxy,Linked.Get());
                FPoseContext Pose(&Proxy,true);FBlendedHeapCurve Curve;UE::Anim::FHeapAttributeContainer Attributes;
                FParallelEvaluationData Evaluation{Curve,Pose.Pose,Attributes};Linked->PreEvaluateAnimation();Linked->ParallelEvaluateAnimation(false,Mesh,Evaluation);
                for(auto Bone:Pose.Pose.ForEachBoneIndex())Atoms.Add(MakeShared<FJsonValueObject>(PoseAtom(Pose.Pose[Bone])));
                Curve.ForEachElement([&](const auto& C){Curves->SetNumberField(C.Name.ToString(),C.Value);});
            }
            else
            {
                if(Frame->GetBoolField(TEXT("initializeHead")))Instance->ProcessEvent(Initialize,nullptr);
                if(Frame->GetBoolField(TEXT("updateHead")))Instance->ProcessEvent(Refresh,nullptr);
            }
            const auto& V=FViewInstanceAccess::View(Instance.Get());const auto& S=FViewInstanceAccess::Spine(Instance.Get());const auto& H=FViewInstanceAccess::Head(Instance.Get());
            const auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("request"),Frame);
            if(GraphMode){Row->SetArrayField(TEXT("pose"),Atoms);Row->SetObjectField(TEXT("curves"),Curves);}
            Row->SetArrayField(TEXT("view"),Numbers({V.YawAngle,V.PitchAngle,V.PitchAmount,V.HeadBlendAmount}));
            Row->SetArrayField(TEXT("spine"),Numbers({double(S.bSpineRotationAllowed),S.SpineAmount,S.SpineAmountScale,S.SpineAmountBias,S.LastYawAngle,S.LastYawAngleWorldSpace,S.YawAngle,S.FinalYawAngle}));
            Row->SetArrayField(TEXT("head"),Numbers({double(H.bInitializationRequired),double(H.bSwitchingLookSides),H.PitchAngle,H.YawAngle,H.YawVelocity,H.YawAmount}));
            Frames.Add(MakeShared<FJsonValueObject>(Row));++Total;
        }
        if(GraphMode)Linked->UninitializeAnimation();
        const auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("name"),TraceValue->AsObject()->GetStringField(TEXT("name")));Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    const auto Result=MakeShared<FJsonObject>();Result->SetNumberField(TEXT("schemaVersion"),1);Result->SetStringField(TEXT("inputsSha256"),Request->GetStringField(TEXT("inputsSha256")));Result->SetArrayField(TEXT("traces"),Traces);
    if(GraphMode)
    {
        Result->SetBoolField(TEXT("headGraph"),true);Result->SetArrayField(TEXT("names"),Names);
        Result->SetStringField(TEXT("graphClass"),HeadClass->GetPathName());Result->SetStringField(TEXT("baseSequence"),Stand->GetPathName());
        Result->SetObjectField(TEXT("resourceHashes"),Request->GetObjectField(TEXT("resourceHashes")));
    }
    FString Json;if(!FJsonSerializer::Serialize(Result,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json))||!FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_REFACTORED_VIEW_TRACE_OK frames=%d assets_saved=0"),Total);return true;
}

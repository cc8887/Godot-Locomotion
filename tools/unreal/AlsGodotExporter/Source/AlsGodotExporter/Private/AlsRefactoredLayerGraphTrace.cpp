#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "AlsLinkedAnimationInstance.h"
#include "AlsCharacter.h"
#include "Animation/AnimBlueprint.h"
#include "Animation/AnimBlueprintGeneratedClass.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_LinkedInputPose.h"
#include "Animation/AnimSequence.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "Editor.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
struct FLayerProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* I,float D){(P.*&FLayerProxyAccess::PreUpdate)(I,D);}
    static void UpdateRoot(FAnimInstanceProxy& P){(P.*&FLayerProxyAccess::UpdateAnimation)();}
    static void Post(FAnimInstanceProxy& P,UAnimInstance* I){(P.*&FLayerProxyAccess::PostUpdate)(I);}
};
struct FLayerInstanceAccess : UAlsLinkedAnimationInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* I){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I);}
    static void SetParent(UAlsLinkedAnimationInstance* I,UAlsAnimationInstance* P){I->*&FLayerInstanceAccess::Parent=P;}
};
bool SetFloats(UObject* Object,const TCHAR* StructName,const TSharedPtr<FJsonObject>& Values)
{
    auto* Struct=FindFProperty<FStructProperty>(Object->GetClass(),StructName);if(!Struct)return false;
    void* Memory=Struct->ContainerPtrToValuePtr<void>(Object);
    for(const auto& Pair:Values->Values)
    {
        auto* Field=FindFProperty<FFloatProperty>(Struct->Struct,FName(*Pair.Key));if(!Field)return false;
        const float Value=static_cast<float>(Pair.Value->AsNumber());if(!FMath::IsFinite(Value))return false;
        Field->SetPropertyValue_InContainer(Memory,Value);
    }
    return true;
}
TSharedPtr<FJsonObject> Atom(const FTransform& T)
{
    const auto Result=MakeShared<FJsonObject>();
    auto Values=[](std::initializer_list<double> V){TArray<TSharedPtr<FJsonValue>> A;for(double X:V)A.Add(MakeShared<FJsonValueNumber>(X));return A;};
    const auto P=T.GetTranslation();const auto Q=T.GetRotation();const auto S=T.GetScale3D();
    Result->SetArrayField(TEXT("position"),Values({P.X,P.Y,P.Z}));Result->SetArrayField(TEXT("rotation"),Values({Q.X,Q.Y,Q.Z,Q.W}));
    Result->SetArrayField(TEXT("scale"),Values({S.X,S.Y,S.Z}));return Result;
}
}

bool UAlsAnimationGraphLibrary::ExportRefactoredLayerGraphTrace(const FString& RequestPath,const FString& OutputPath)
{
    if(FPaths::IsRelative(RequestPath)||FPaths::IsRelative(OutputPath)||RequestPath==OutputPath)return false;
    FString Text;TSharedPtr<FJsonObject> Request;
    if(!FFileHelper::LoadFileToString(Text,*RequestPath)||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Request))return false;
    auto* Blueprint=LoadObject<UAnimBlueprint>(nullptr,TEXT("/ALS/ALS/Character/AnimationInstances/AB_Als_Layering.AB_Als_Layering"));
    auto* Generated=Blueprint?Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass):nullptr;
    auto* CharacterClass=LoadClass<AAlsCharacter>(nullptr,TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"));
    auto* Defaults=CharacterClass?Cast<AAlsCharacter>(CharacterClass->GetDefaultObject()):nullptr;
    auto* Mesh=Defaults?Defaults->GetMesh()->GetSkeletalMeshAsset():nullptr;
    auto* Stand=LoadObject<UAnimSequence>(nullptr,TEXT("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose"));
    auto* Crouch=LoadObject<UAnimSequence>(nullptr,TEXT("/ALS/ALS/Animations/Base/A_Als_Crouch_Pose.A_Als_Crouch_Pose"));
    auto* World=GEditor?GEditor->GetEditorWorldContext().World():nullptr;
    if(!Generated||!Mesh||!Mesh->GetSkeleton()||!Stand||!Crouch||!World)return false;
    FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;Spawn.SpawnCollisionHandlingOverride=ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    auto* Owner=World->SpawnActor<AActor>(Spawn);if(!Owner)return false;Owner->SetActorTickEnabled(false);
    ON_SCOPE_EXIT{World->DestroyActor(Owner);};
    const auto& Reference=Mesh->GetSkeleton()->GetReferenceSkeleton();TArray<FBoneIndexType> Required;
    TArray<TSharedPtr<FJsonValue>> Names;
    for(int32 Bone=0;Bone<Reference.GetNum();++Bone){Required.Add(static_cast<FBoneIndexType>(Bone));Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Bone).ToString()));}
    TArray<TSharedPtr<FJsonValue>> Traces;int32 Total=0;
    for(const auto& TraceValue:Request->GetArrayField(TEXT("traces")))
    {
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner));Component->SetSkeletalMesh(Mesh);
        TStrongObjectPtr<UAlsAnimationInstance> Parent(NewObject<UAlsAnimationInstance>(Component.Get()));
        TStrongObjectPtr<UAlsLinkedAnimationInstance> Instance(NewObject<UAlsLinkedAnimationInstance>(Component.Get(),Generated));
        Instance->InitializeAnimation(true);FLayerInstanceAccess::SetParent(Instance.Get(),Parent.Get());
        auto& Proxy=FLayerInstanceAccess::Proxy(Instance.Get());
        Proxy.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*Mesh->GetSkeleton());
        Proxy.GetRequiredBones().SetUseRAWData(true);Proxy.GetRequiredBones().SetUseSourceData(false);
        TArray<FAnimNode_LinkedInputPose*> Inputs;
        for(const auto* Property:Generated->GetAnimNodeProperties())
            if(Property->Struct==FAnimNode_LinkedInputPose::StaticStruct())Inputs.Add(Property->ContainerPtrToValuePtr<FAnimNode_LinkedInputPose>(Instance.Get()));
        if(Inputs.Num()!=2)return false;
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FrameValue:TraceValue->AsObject()->GetArrayField(TEXT("frames")))
        {
            FMemMark Mark(FMemStack::Get());const TGuardValue<uint64> GlobalFrame(GFrameCounter,static_cast<uint64>(700000+Total));
            const auto Frame=FrameValue->AsObject();const float Delta=static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            if(!SetFloats(Parent.Get(),TEXT("LayeringState"),Frame->GetObjectField(TEXT("layering")))||
                !SetFloats(Parent.Get(),TEXT("PoseState"),Frame->GetObjectField(TEXT("stance"))))return false;
            FLayerProxyAccess::Pre(Proxy,Instance.Get(),Delta);
            for(auto* Input:Inputs)
            {
                const bool Locomotion=Input->Name==TEXT("Locomotion Input");
                if(!Locomotion&&Input->Name!=TEXT("Overlay Input"))return false;
                FPoseContext Source(&Proxy);FAnimationPoseData Data(Source);
                auto* Sequence=Frame->GetBoolField(Locomotion?TEXT("locomotionStanding"):TEXT("overlayStanding"))?Stand:Crouch;
                FAnimExtractContext Context(0.,false);Context.bExtractWithRootMotionProvider=false;
                Sequence->GetAnimationPose(Data,Context);
                for(const auto& Pair:Frame->GetObjectField(Locomotion?TEXT("locomotionCurves"):TEXT("overlayCurves"))->Values)
                    Source.Curve.Set(FName(*Pair.Key),static_cast<float>(Pair.Value->AsNumber()));
                Input->CachedInputPose.CopyBonesFrom(Source.Pose);Input->CachedInputCurve.CopyFrom(Source.Curve);Input->bIsCachedInputPoseInitialized=true;
            }
            FLayerProxyAccess::UpdateRoot(Proxy);Proxy.FlipBufferWriteIndex();FLayerProxyAccess::Post(Proxy,Instance.Get());
            FPoseContext Pose(&Proxy,true);FBlendedHeapCurve Curve;UE::Anim::FHeapAttributeContainer Attributes;
            FParallelEvaluationData Evaluation{Curve,Pose.Pose,Attributes};Instance->PreEvaluateAnimation();Instance->ParallelEvaluateAnimation(false,Mesh,Evaluation);
            TArray<TSharedPtr<FJsonValue>> Atoms;for(auto Bone:Pose.Pose.ForEachBoneIndex())Atoms.Add(MakeShared<FJsonValueObject>(Atom(Pose.Pose[Bone])));
            const auto Curves=MakeShared<FJsonObject>();Curve.ForEachElement([&](const auto& C){Curves->SetNumberField(C.Name.ToString(),C.Value);});
            const auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("input"),Frame);Row->SetArrayField(TEXT("pose"),Atoms);Row->SetObjectField(TEXT("curves"),Curves);
            Frames.Add(MakeShared<FJsonValueObject>(Row));++Total;
        }
        Instance->UninitializeAnimation();const auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("name"),TraceValue->AsObject()->GetStringField(TEXT("name")));
        Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    const auto Result=MakeShared<FJsonObject>();Result->SetNumberField(TEXT("schemaVersion"),1);Result->SetStringField(TEXT("source"),Blueprint->GetPathName());
    Result->SetStringField(TEXT("requestDigest"),Request->GetStringField(TEXT("requestDigest")));Result->SetArrayField(TEXT("names"),Names);Result->SetArrayField(TEXT("traces"),Traces);
    FString Json;if(!FJsonSerializer::Serialize(Result,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_REFACTORED_LAYER_NATIVE_OK traces=%d frames=%d assets_saved=0"),Traces.Num(),Total);return true;
}

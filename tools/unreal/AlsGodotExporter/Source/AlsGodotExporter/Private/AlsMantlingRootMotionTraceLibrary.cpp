#include "AlsAnimationGraphLibrary.h"
#include "AlsCharacter.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontage.h"
#include "Components/BoxComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "RootMotionSources/AlsRootMotionSource_Mantling.h"
#include "Settings/AlsMantlingSettings.h"
#include "Utility/AlsMontageUtility.h"
#include "Editor.h"
#include "Engine/World.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"

namespace
{
TArray<TSharedPtr<FJsonValue>> MantleTraceNumbers(std::initializer_list<double> V)
{ TArray<TSharedPtr<FJsonValue>> R; for (double X : V) R.Add(MakeShared<FJsonValueNumber>(X)); return R; }
TSharedPtr<FJsonObject> MantleTracePose(const FTransform& T)
{
    auto J=MakeShared<FJsonObject>(); const auto P=T.GetLocation(); const auto Q=T.GetRotation(); const auto S=T.GetScale3D();
    J->SetArrayField(TEXT("position"),MantleTraceNumbers({P.X,P.Y,P.Z}));
    J->SetArrayField(TEXT("rotation"),MantleTraceNumbers({Q.X,Q.Y,Q.Z,Q.W}));
    J->SetArrayField(TEXT("scale"),MantleTraceNumbers({S.X,S.Y,S.Z})); return J;
}
}

bool UAlsAnimationGraphLibrary::ExportMantlingRootMotionTrace(const FString& OutputPath)
{
    if (FPaths::IsRelative(OutputPath)) return false;
    auto* World=GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    auto* Class=LoadClass<AAlsCharacter>(nullptr,TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"));
    if (!World || !Class) return false;
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const TCHAR* Name : {TEXT("High"),TEXT("High_InAir"),TEXT("Low"),TEXT("Low_BothHands"),TEXT("Low_Box"),TEXT("Low_Left"),TEXT("Low_Right")})
    {
        const FString Path=FString::Printf(TEXT("/ALS/ALS/Data/Character/Mantle/MS_Als_%s.MS_Als_%s"),Name,Name);
        auto* Settings=LoadObject<UAlsMantlingSettings>(nullptr,*Path);
        if (!Settings || !Settings->Montage) return false;
        auto* Montage=Settings->Montage.Get();
        for (int32 Hz : {30,60,120}) for (int32 Mode=0;Mode<3;++Mode)
        {
            FActorSpawnParameters Spawn; Spawn.ObjectFlags|=RF_Transient;
            Spawn.SpawnCollisionHandlingOverride=ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
            auto* Character=World->SpawnActor<AAlsCharacter>(Class,FVector(10,20,100000),FRotator(0,23,0),Spawn);
            if (!Character) return false;
            ON_SCOPE_EXIT { World->DestroyActor(Character); };
            auto* Owner=World->SpawnActor<AActor>(Spawn); if (!Owner) return false;
            ON_SCOPE_EXIT { World->DestroyActor(Owner); };
            auto* Base=NewObject<UBoxComponent>(Owner,NAME_None,RF_Transient);
            Base->SetMobility(Mode==0 ? EComponentMobility::Static : EComponentMobility::Movable);
            Base->SetCollisionEnabled(ECollisionEnabled::NoCollision);
            const FTransform InitialBase(FRotator(0,17,0),FVector(30,-20,100000),FVector(1.2,0.8,1.1));
            Base->SetWorldTransform(InitialBase); Base->RegisterComponent();
            auto* Anim=Character->GetMesh()->GetAnimInstance();
            if (!Anim || Anim->Montage_Play(Montage)<=0) return false;
            FMovementBaseInterfaceData BaseData{Base};
            const bool bRelative=MovementBaseUtility::UseRelativeLocation(&BaseData);
            FAlsRootMotionSource_Mantling Source;
            Source.MantlingSettings=Settings; Source.TargetPrimitive=Base;
            Source.MontageStartTime=(Settings->StartTime.X+Settings->StartTime.Y)*0.5f;
            Source.Duration=(Montage->GetPlayLength()-Source.MontageStartTime)/Montage->RateScale;
            const FTransform Mesh(Character->GetBaseRotationOffset());
            const FTransform Actor=Character->GetActorTransform();
            const FTransform DesiredWorld(FRotator(0,71,0),FVector(90,80,100150));
            auto Desired=bRelative ? DesiredWorld.GetRelativeTransform(InitialBase) : DesiredWorld;
            Desired.SetScale3D(FVector::OneVector);
            auto StartRoot=UAlsMontageUtility::ExtractRootTransformFromMontage(Montage,Source.MontageStartTime);
            StartRoot.SetScale3D(FVector::OneVector);
            auto Start=StartRoot.GetRelativeTransformReverse(Mesh)*Actor;
            if (bRelative) Start.SetToRelativeTransform(InitialBase);
            Source.StartLocation=Start.GetLocation(); Source.StartRotation=Start.Rotator();
            auto LastRoot=UAlsMontageUtility::ExtractLastRootTransformFromMontage(Montage); LastRoot.SetScale3D(FVector::OneVector);
            auto Target=LastRoot.GetRelativeTransformReverse(Mesh)*DesiredWorld;
            if (bRelative) Target.SetToRelativeTransform(InitialBase);
            Source.TargetLocation=Target.GetLocation(); Source.TargetRotation=Target.Rotator();
            auto Trace=MakeShared<FJsonObject>();
            Trace->SetStringField(TEXT("settings"),Path);Trace->SetStringField(TEXT("montage"),Montage->GetPathName());
            Trace->SetNumberField(TEXT("hz"),Hz);Trace->SetNumberField(TEXT("mode"),Mode);Trace->SetBoolField(TEXT("relative"),bRelative);
            Trace->SetNumberField(TEXT("startTime"),Source.MontageStartTime);Trace->SetNumberField(TEXT("duration"),Source.Duration);
            Trace->SetNumberField(TEXT("rate"),Montage->RateScale);
            Trace->SetNumberField(TEXT("blendInTime"),Montage->BlendIn.GetBlendTime());
            Trace->SetStringField(TEXT("blendInOption"),StaticEnum<EAlphaBlendOption>()->GetNameStringByValue(static_cast<int64>(Montage->BlendIn.GetBlendOption())));
            Trace->SetBoolField(TEXT("customBlend"),Montage->BlendIn.GetCustomCurve()!=nullptr);
            Trace->SetObjectField(TEXT("actor"),MantleTracePose(Actor));Trace->SetObjectField(TEXT("desired"),MantleTracePose(Desired));
            Trace->SetObjectField(TEXT("mesh"),MantleTracePose(Mesh));Trace->SetObjectField(TEXT("base"),MantleTracePose(InitialBase));
            Trace->SetObjectField(TEXT("startAnchor"),MantleTracePose(FTransform(Source.StartRotation,Source.StartLocation)));
            Trace->SetObjectField(TEXT("targetAnchor"),MantleTracePose(FTransform(Source.TargetRotation,Source.TargetLocation)));
            TArray<TSharedPtr<FJsonValue>> Frames;
            for (int32 Frame=0;Frame<Hz+2;++Frame)
            {
                const float Delta=Frame==Hz ? 0.f : 1.f/Hz;
                const float SimulationDelta=Mode==2 ? 0.5f/Hz : 1.f/Hz;
                if (Mode!=0 && Frame<Hz)
                {
                    const double T=static_cast<double>(Frame+1)/Hz;
                    Base->SetWorldTransform(FTransform(FRotator(6*T,17+40*T,-4*T),FVector(30+25*T,-20-12*T,100000+8*T),InitialBase.GetScale3D()));
                }
                const FTransform CurrentBase=Base->GetComponentTransform();
                if (Frame==Hz+1) Source.TargetPrimitive.Reset();
                auto Row=MakeShared<FJsonObject>();
                Row->SetNumberField(TEXT("delta"),Delta);Row->SetNumberField(TEXT("simulationDelta"),SimulationDelta);
                Row->SetObjectField(TEXT("base"),MantleTracePose(CurrentBase));Row->SetBoolField(TEXT("targetExists"),Source.TargetPrimitive.IsValid());
                Source.PrepareRootMotion(SimulationDelta,Delta,*Character,*Character->GetCharacterMovement());
                Row->SetNumberField(TEXT("time"),Source.GetTime());Row->SetBoolField(TEXT("hasMotion"),Source.RootMotionParams.bHasRootMotion);
                Row->SetNumberField(TEXT("montagePosition"),Anim->Montage_GetPosition(Montage));
                const auto Motion=Source.RootMotionParams.GetRootMotionTransform();Row->SetObjectField(TEXT("motion"),MantleTracePose(Motion));
                if (Source.RootMotionParams.bHasRootMotion)
                    Character->SetActorLocationAndRotation(Character->GetActorLocation()+Motion.GetLocation()*Delta,
                        Motion.GetRotation()*Character->GetActorQuat(),false,nullptr,ETeleportType::TeleportPhysics);
                Row->SetObjectField(TEXT("actor"),MantleTracePose(Character->GetActorTransform()));
                Frames.Add(MakeShared<FJsonValueObject>(Row));
            }
            Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
        }
    }
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);Root->SetArrayField(TEXT("traces"),Traces);
    FString Json;if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))) return false;
    return FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

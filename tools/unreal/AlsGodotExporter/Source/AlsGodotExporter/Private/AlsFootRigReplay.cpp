#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "AlsCharacter.h"
#include "AlsCharacterMovementComponent.h"
#include "Animation/AnimInstanceProxy.h"
#include "ControlRig.h"
#include "Components/BoxComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/SceneComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "Engine/Engine.h"
#include "GameFramework/PlayerController.h"
#include "GameFramework/WorldSettings.h"
#include "Misc/App.h"
#include "GameFramework/Actor.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Rigs/RigHierarchy.h"
#include "Rigs/RigHierarchyController.h"
#include "Rendering/SkeletalMeshRenderData.h"
#include "Rendering/SkeletalMeshLODRenderData.h"
#include "Serialization/JsonSerializer.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "Settings/AlsCharacterSettings.h"
#include "State/AlsControlRigInput.h"
#include "Units/Execution/RigUnit_BeginExecution.h"
#include "Units/Execution/RigUnit_PrepareForExecution.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"
#include "Utility/AlsPrivateMemberAccessor.h"

ALS_DEFINE_PRIVATE_MEMBER_ACCESSOR(FAlsClosedRigGatherFeet, &UAlsAnimationInstance::RefreshFeetOnGameThread,
    void (UAlsAnimationInstance::*)())
ALS_DEFINE_PRIVATE_MEMBER_ACCESSOR(FAlsClosedRigUpdateFeet, &UAlsAnimationInstance::RefreshFeet,
    void (UAlsAnimationInstance::*)(float))
ALS_DEFINE_PRIVATE_MEMBER_ACCESSOR(FAlsClosedRigUpdatePose, &UAlsAnimationInstance::RefreshPose,
    void (UAlsAnimationInstance::*)())
ALS_DEFINE_PRIVATE_MEMBER_ACCESSOR(FAlsClosedRigCurves, &FAnimInstanceProxy::GetAnimationCurves,
    TMap<FName, float>& (FAnimInstanceProxy::*)(EAnimCurveType))

namespace AlsFootRigReplay
{
FVector V(const TSharedPtr<FJsonObject>& O)
{ return FVector(O->GetNumberField(TEXT("X")), O->GetNumberField(TEXT("Y")), O->GetNumberField(TEXT("Z"))); }
FQuat Q(const TSharedPtr<FJsonObject>& O)
{ return FQuat(O->GetNumberField(TEXT("X")), O->GetNumberField(TEXT("Y")), O->GetNumberField(TEXT("Z")), O->GetNumberField(TEXT("W"))); }
FTransform Pose(const TSharedPtr<FJsonObject>& O)
{
    const auto& P = O->GetArrayField(TEXT("position")); const auto& R = O->GetArrayField(TEXT("rotation"));
    const auto& S = O->GetArrayField(TEXT("scale"));
    check(P.Num() == 3 && R.Num() == 4 && S.Num() == 3);
    return FTransform(FQuat(R[0]->AsNumber(), R[1]->AsNumber(), R[2]->AsNumber(), R[3]->AsNumber()).GetNormalized(),
        FVector(P[0]->AsNumber(), P[1]->AsNumber(), P[2]->AsNumber()), FVector(S[0]->AsNumber(), S[1]->AsNumber(), S[2]->AsNumber()));
}
TArray<TSharedPtr<FJsonValue>> Vector(const FVector& P)
{ return {MakeShared<FJsonValueNumber>(P.X), MakeShared<FJsonValueNumber>(P.Y), MakeShared<FJsonValueNumber>(P.Z)}; }
TSharedPtr<FJsonValue> SerializePose(const FTransform& T)
{
    auto O = MakeShared<FJsonObject>(); const auto R = T.GetRotation();
    O->SetArrayField(TEXT("position"), Vector(T.GetLocation())); O->SetArrayField(TEXT("scale"), Vector(T.GetScale3D()));
    O->SetArrayField(TEXT("rotation"), {MakeShared<FJsonValueNumber>(R.X), MakeShared<FJsonValueNumber>(R.Y),
        MakeShared<FJsonValueNumber>(R.Z), MakeShared<FJsonValueNumber>(R.W)});
    return MakeShared<FJsonValueObject>(O);
}
FTransform NativePlatform(const TSharedPtr<FJsonObject>& O)
{
    const auto P = V(O->GetObjectField(TEXT("Position"))); const auto R = Q(O->GetObjectField(TEXT("Rotation")));
    return FTransform(FQuat(R.Z, -R.X, -R.Y, R.W).GetNormalized(), FVector(-P.Z, P.X, P.Y) * 100.0);
}
FTransform NativeFloor(const TSharedPtr<FJsonObject>& O)
{
    FMatrix M = FMatrix::Identity;
    for (int32 R = 0; R < 4; ++R)
        for (int32 C = 0; C < 4; ++C)
            M.M[R][C] = O->GetNumberField(FString::Printf(TEXT("M%d%d"), R + 1, C + 1));
    const FQuat R(M); const FVector P = M.GetOrigin();
    return FTransform(FQuat(R.Z, -R.X, -R.Y, R.W).GetNormalized(), FVector(-P.Z, P.X, P.Y) * 100.0);
}
struct FFeedbackAccess : UAlsAnimationInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance& I) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(&I); }
    static const FAlsFeetState& Feet(const UAlsAnimationInstance& I) { return I.*&FFeedbackAccess::FeetState; }
    static const FAlsMovementBaseState& Base(const UAlsAnimationInstance& I) { return I.*&FFeedbackAccess::MovementBase; }
    static FAlsControlRigInput Input(const UAlsAnimationInstance& I) { return (I.*&FFeedbackAccess::GetControlRigInput)(); }
    static void Configure(UAlsAnimationInstance& I, UAlsAnimationInstanceSettings* S, bool Pending,
        bool Moving, double Elapsed, bool Changed, bool Relative, const FTransform& Base)
    {
        I.*&FFeedbackAccess::Settings = S;
        I.*&FFeedbackAccess::LocomotionMode = AlsLocomotionModeTags::Grounded;
        (I.*&FFeedbackAccess::LocomotionState).bMovingSmooth = Moving;
        I.*&FFeedbackAccess::TeleportedTime = I.GetWorld()->GetTimeSeconds() - Elapsed;
        auto* Property = FindFProperty<FBoolProperty>(I.GetClass(), TEXT("bPendingUpdate"));
        check(Property); Property->SetPropertyValue_InContainer(&I, Pending);
        auto& Movement = I.*&FFeedbackAccess::MovementBase;
        Movement.bBaseChanged = Changed; Movement.bHasRelativeLocation = Relative;
        Movement.Location = Base.GetLocation(); Movement.Rotation = Base.GetRotation();
    }
};
struct FFeedbackProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P, UAnimInstance& I, float Dt) { (P.*&FFeedbackProxyAccess::PreUpdate)(&I, Dt); }
};
TSharedPtr<FJsonObject> SerializeFoot(const FAlsFootState& F)
{
    auto O = MakeShared<FJsonObject>(); O->SetNumberField(TEXT("amount"), F.LockAmount);
    O->SetField(TEXT("targetWorld"), SerializePose(FTransform(F.TargetRotationWorldSpace, F.TargetLocationWorldSpace)));
    O->SetField(TEXT("worldLock"), SerializePose(FTransform(F.LockRotationWorldSpace, F.LockLocationWorldSpace)));
    O->SetField(TEXT("baseLock"), SerializePose(FTransform(FQuat(F.LockRotationMovementBaseSpace), FVector(F.LockLocationMovementBaseSpace))));
    O->SetField(TEXT("componentLock"), SerializePose(FTransform(FQuat(F.LockRotation), FVector(F.LockLocation))));
    O->SetField(TEXT("finalComponent"), SerializePose(FTransform(FQuat(F.FinalRotation), FVector(F.FinalLocation))));
    return O;
}
struct FNativeSoles
{
    struct FSolePoint { uint32 Vertex; bool Left; double RestHeight; };
    USkeletalMeshComponent& Component;
    double BindErrorCm = 0;
    TArray<FSolePoint> SolePoints;
    explicit FNativeSoles(USkeletalMeshComponent& C) : Component(C) {}
    bool InitializeSoles(USkeletalMesh& Mesh, USkeletalMeshComponent* ReferenceComponent = nullptr)
    {
        auto& Reference = ReferenceComponent ? *ReferenceComponent : Component;
        auto* Data = Mesh.GetResourceForRendering(); auto* Weights = Reference.GetSkinWeightBuffer(0);
        if (!Data || Data->LODRenderData.Num() == 0 || !Weights || Mesh.GetMorphTargets().Num() != 0) return false;
        const auto& LOD = Data->LODRenderData[0];
        if (Weights->GetNumVertices() != LOD.GetNumVertices()) return false;
        TArray<FMatrix44f> Matrices; TArray<FVector3f> Rest;
        Reference.CacheRefToLocalMatrices(Matrices);
        USkinnedMeshComponent::ComputeSkinnedPositions(&Reference, Rest, Matrices, LOD, *Weights);
        double LowestLeft = DBL_MAX, LowestRight = DBL_MAX;
        TArray<FSolePoint> Candidates;
        for (const auto& Section : LOD.RenderSections)
            for (uint32 V = Section.BaseVertexIndex; V < Section.BaseVertexIndex + Section.NumVertices; ++V)
            {
                double Left = 0, Right = 0, Total = 0;
                for (uint32 I = 0; I < Weights->GetMaxBoneInfluences(); ++I)
                {
                    const double Weight = Weights->GetBoneWeight(V, I) / 65535.0;
                    if (Weight == 0) continue;
                    const int32 LocalBone = Weights->GetBoneIndex(V, I);
                    if (!Section.BoneMap.IsValidIndex(LocalBone)) return false;
                    const auto Name = Mesh.GetRefSkeleton().GetBoneName(Section.BoneMap[LocalBone]);
                    if (Name == TEXT("foot_l") || Name == TEXT("ball_l")) Left += Weight;
                    if (Name == TEXT("foot_r") || Name == TEXT("ball_r")) Right += Weight;
                    Total += Weight;
                }
                if (FMath::Abs(Total - 1) > .001) return false;
                BindErrorCm = FMath::Max(BindErrorCm, static_cast<double>(FVector3f::Distance(Rest[V], LOD.StaticVertexBuffers.PositionVertexBuffer.VertexPosition(V))));
                if (Left >= .99 || Right >= .99)
                {
                    Candidates.Add({V, Left >= .99, Rest[V].Z});
                    if (Left >= .99) LowestLeft = FMath::Min(LowestLeft, static_cast<double>(Rest[V].Z));
                    else LowestRight = FMath::Min(LowestRight, static_cast<double>(Rest[V].Z));
                }
            }
        if (BindErrorCm > .01 || LowestLeft == DBL_MAX || LowestRight == DBL_MAX) return false;
        for (const auto& Point : Candidates)
            if (Point.RestHeight <= (Point.Left ? LowestLeft : LowestRight) + .4) SolePoints.Add(Point);
        return !SolePoints.IsEmpty();
    }
    TSharedPtr<FJsonObject> CaptureSoles(USkeletalMesh& Mesh, const FTransform& Platform)
    {
        TArray<FMatrix44f> Matrices; TArray<FVector3f> Positions;
        Component.CacheRefToLocalMatrices(Matrices);
        USkinnedMeshComponent::ComputeSkinnedPositions(&Component, Positions, Matrices,
            Mesh.GetResourceForRendering()->LODRenderData[0], *Component.GetSkinWeightBuffer(0));
        TArray<TSharedPtr<FJsonValue>> Left, Right;
        double MinimumLeft = DBL_MAX, MinimumRight = DBL_MAX;
        for (const auto& Point : SolePoints)
        {
            auto Local = Platform.InverseTransformPosition(Component.GetComponentTransform().TransformPosition(FVector(Positions[Point.Vertex])));
            Local.Z -= 50; // native box top, centimeters; points remain fixed in bind pose
            (Point.Left ? Left : Right).Add(MakeShared<FJsonValueArray>(Vector(Local)));
            if (Point.Left) MinimumLeft = FMath::Min(MinimumLeft, Local.Z); else MinimumRight = FMath::Min(MinimumRight, Local.Z);
        }
        auto O = MakeShared<FJsonObject>(); O->SetArrayField(TEXT("left"), Left); O->SetArrayField(TEXT("right"), Right);
        O->SetNumberField(TEXT("leftMinimumCm"), MinimumLeft); O->SetNumberField(TEXT("rightMinimumCm"), MinimumRight);
        O->SetNumberField(TEXT("bindErrorCm"), BindErrorCm);
        O->SetStringField(TEXT("scope"), TEXT("Original mesh LOD0 native CPU skinning; fixed lowest 4mm foot/ball vertices; plane distances, not support-force acceptance"));
        return O;
    }
};
struct FClosedFeedback : FNativeSoles
{
    TStrongObjectPtr<UAlsAnimationInstance> Instance;
    TStrongObjectPtr<UAlsAnimationInstanceSettings> Settings;
    TMap<FName, float> Curves;
    uint64 BaseId = 0, TeleportSequence = 0;
    double Elapsed = 1;
    FClosedFeedback(USkeletalMeshComponent& C, USkeletalMesh& Mesh)
        : FNativeSoles(C), Instance(NewObject<UAlsAnimationInstance>(&C, NAME_None, RF_Transient)),
          Settings(NewObject<UAlsAnimationInstanceSettings>(GetTransientPackage(), NAME_None, RF_Transient))
    {
        Component.SetComponentSpaceTransformsDoubleBuffering(false);
        auto& Pose = Component.GetEditableComponentSpaceTransforms();
        const auto& Ref = Mesh.GetRefSkeleton(); Pose.SetNum(Ref.GetNum());
        for (int32 B = 0; B < Pose.Num(); ++B)
            Pose[B] = Ref.GetParentIndex(B) < 0 ? Ref.GetRefBonePose()[B] : Ref.GetRefBonePose()[B] * Pose[Ref.GetParentIndex(B)];
        Instance->InitializeAnimation(true);
    }
    bool Update(int32 Frame, const TSharedPtr<FJsonObject>& Capture, FAlsControlRigInput& Input, TSharedPtr<FJsonObject>& Trace)
    {
        // Scope is the actual grounded platform fixture. Do not silently fake
        // in-air prediction or substitute captured foot targets/history.
        if (Capture->GetObjectField(TEXT("Movement"))->GetObjectField(TEXT("properties"))->GetIntegerField(TEXT("MovementState")) != 1) return false;
        const auto Motor = Capture->GetObjectField(TEXT("MotorInput"));
        const auto Floor = Motor->GetObjectField(TEXT("Floor")); const auto Scene = Motor->GetObjectField(TEXT("FootIk"));
        const float Dt = Motor->GetNumberField(TEXT("DeltaTime"));
        const uint64 CurrentTeleport = Scene->GetNumberField(TEXT("TeleportSequence"));
        Elapsed = CurrentTeleport == TeleportSequence ? FMath::Min(1.0, Elapsed + Dt) : 0; TeleportSequence = CurrentTeleport;
        const uint64 CurrentBase = Floor->GetIntegerField(TEXT("IsGrounded")) == 1 && Floor->GetIntegerField(TEXT("PlatformId")) >= 0 &&
            Floor->GetNumberField(TEXT("ColliderId")) > 0 ? static_cast<uint64>(Floor->GetNumberField(TEXT("ColliderId"))) : 0;
        const auto Base = CurrentBase ? NativeFloor(Floor->GetObjectField(TEXT("PlatformTransform"))) : FTransform::Identity;
        const auto Axes = Motor->GetObjectField(TEXT("Command"))->GetObjectField(TEXT("MovementAxes"));
        const auto Velocity = V(Scene->GetObjectField(TEXT("MovementVelocity")));
        const double Speed = FVector2D(Velocity.X, Velocity.Z).Size() * 100;
        const bool HasInput = FMath::Square(Axes->GetNumberField(TEXT("X"))) + FMath::Square(Axes->GetNumberField(TEXT("Y"))) > 1e-4f;
        FFeedbackAccess::Configure(*Instance, Settings.Get(), Frame == 1, (HasInput && Speed >= 1) || Speed > Settings->General.MovingSmoothSpeedThreshold,
            Elapsed, CurrentBase != BaseId, CurrentBase != 0, Base); BaseId = CurrentBase;
        auto& Proxy = FFeedbackAccess::Proxy(*Instance); FFeedbackProxyAccess::Pre(Proxy, *Instance, Dt);
        FAlsClosedRigCurves::Access(Proxy, EAnimCurveType::AttributeCurve) = Curves;
        FAlsClosedRigGatherFeet::Access(*Instance);
        FAlsClosedRigUpdatePose::Access(*Instance);
        FAlsClosedRigUpdateFeet::Access(*Instance, Dt);
        Input = FFeedbackAccess::Input(*Instance);
        const auto& Feet = FFeedbackAccess::Feet(*Instance);
        Trace = MakeShared<FJsonObject>(); Trace->SetBoolField(TEXT("valid"), Feet.bValid);
        Trace->SetBoolField(TEXT("becameValid"), Feet.bBecameValid);
        Trace->SetNumberField(TEXT("pelvisAmount"), Input.PelvisOffsetAmount);
        Trace->SetNumberField(TEXT("baseIdentity"), static_cast<double>(BaseId)); Trace->SetField(TEXT("base"), SerializePose(Base));
        Trace->SetObjectField(TEXT("left"), SerializeFoot(Feet.Left)); Trace->SetObjectField(TEXT("right"), SerializeFoot(Feet.Right));
        Trace->SetField(TEXT("pelvis"), SerializePose(Component.GetSocketTransform(TEXT("pelvis"), RTS_Component)));
        auto Values = MakeShared<FJsonObject>(); for (const auto& Pair : Curves) Values->SetNumberField(Pair.Key.ToString(), Pair.Value);
        Trace->SetObjectField(TEXT("previousCurves"), Values);
        return Proxy.GetComponentTransform().Equals(Component.GetComponentTransform(), 1e-8);
    }
    void Commit(URigHierarchy& H, USkeletalMesh& Mesh)
    {
        auto& Pose = Component.GetEditableComponentSpaceTransforms(); const auto& Ref = Mesh.GetRefSkeleton();
        for (int32 B = 0; B < Pose.Num(); ++B)
            Pose[B] = H.GetGlobalTransform(FRigElementKey(Ref.GetBoneName(B), ERigElementType::Bone));
        Curves.Reset();
        for (const auto& Key : H.GetCurveKeys()) Curves.Add(Key.Name, H.GetCurveValue(Key));
    }
};
}

bool UAlsAnimationGraphLibrary::ReplayRefactoredFootRig(const FString& RequestPath, const FString& OutputPath, bool bClosedFeedback)
{
    using namespace AlsFootRigReplay;
    if (FPaths::IsRelative(RequestPath) || FPaths::IsRelative(OutputPath) || IFileManager::Get().FileExists(*OutputPath)) return false;
    FString Text; TArray<TSharedPtr<FJsonValue>> Inputs;
    if (!FFileHelper::LoadFileToString(Text, *RequestPath) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Inputs) || Inputs.IsEmpty()) return false;
    const auto Skeleton = Inputs[0]->AsObject()->GetObjectField(TEXT("RigCapture"))->GetObjectField(TEXT("Skeleton"));
    const auto& Names = Skeleton->GetArrayField(TEXT("names"));
    const auto& Parents = Skeleton->GetArrayField(TEXT("parents"));
    if (Names.Num() != Parents.Num()) return false;
    auto* RigClass = LoadClass<UControlRig>(nullptr, TEXT("/ALS/ALS/Character/CR_Als.CR_Als_C"));
    auto* Mesh = LoadObject<USkeletalMesh>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin"));
    if (!RigClass || !Mesh) { UE_LOG(LogTemp, Error, TEXT("ALS_RIG_REPLAY missing native class/mesh")); return false; }
    const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None,
        nullptr, false, ERHIFeatureLevel::Num, &Initialization));
    if (!World.IsValid()) return false;
    struct FWorldCleanup { UWorld* Value; ~FWorldCleanup() { Value->DestroyWorld(false); } } Cleanup{World.Get()};
    auto* Floor = World->SpawnActor<AActor>(); auto* Owner = World->SpawnActor<AActor>();
    if (!Floor || !Owner) return false;
    auto* Box = NewObject<UBoxComponent>(Floor); Floor->SetRootComponent(Box); Floor->AddInstanceComponent(Box);
    Box->SetMobility(EComponentMobility::Movable); Box->SetBoxExtent(FVector(5000, 5000, 50));
    Box->SetCollisionEnabled(ECollisionEnabled::QueryOnly); Box->SetCollisionResponseToAllChannels(ECR_Block); Box->RegisterComponent();
    USceneComponent* Component = bClosedFeedback ? NewObject<USkeletalMeshComponent>(Owner) : NewObject<USceneComponent>(Owner);
    if (bClosedFeedback) { auto* C = CastChecked<USkeletalMeshComponent>(Component); C->SetSkeletalMesh(Mesh); C->SetCollisionEnabled(ECollisionEnabled::NoCollision); }
    Owner->SetRootComponent(Component);
    Owner->AddInstanceComponent(Component); Component->RegisterComponent();
    TUniquePtr<FClosedFeedback> Feedback;
    if (bClosedFeedback) Feedback = MakeUnique<FClosedFeedback>(*CastChecked<USkeletalMeshComponent>(Component), *Mesh);
    if (Feedback && !Feedback->InitializeSoles(*Mesh))
    { UE_LOG(LogTemp, Error, TEXT("ALS_RIG_REPLAY native sole binding failed")); return false; }
    TStrongObjectPtr<UControlRig> Rig(NewObject<UControlRig>(Component, RigClass, NAME_None, RF_Transient));
    Rig->Initialize();
    auto* H = Rig->GetHierarchy(); auto* Controller = H->GetController(true);
    Controller->ImportBones(Mesh, NAME_None, true, true, false, false);
    // Preserve the actual bound mesh's reference pose during construction,
    // using the same public binding API as a runtime skeletal mesh consumer.
    Rig->SetBoneInitialTransformsFromSkeletalMesh(Mesh);
    auto* InputProperty = FindFProperty<FStructProperty>(Rig->GetVariablesStruct(), TEXT("RigInput"));
    if (!InputProperty || InputProperty->Struct != FAlsControlRigInput::StaticStruct())
    { UE_LOG(LogTemp, Error, TEXT("ALS_RIG_REPLAY missing RigInput of type FAlsControlRigInput")); return false; }
    auto* RigInput = InputProperty->ContainerPtrToValuePtr<FAlsControlRigInput>(Rig->GetVariablesMemory());
    *RigInput = FAlsControlRigInput(); RigInput->bUseFootIkBones = true; RigInput->bUseHandIkBones = true;
    Rig->SetDeltaTime(0);
    if (!Rig->ExecuteEvent(FRigUnit_PrepareForExecution::EventName))
    { UE_LOG(LogTemp, Error, TEXT("ALS_RIG_REPLAY construction event unavailable")); return false; }
    // Freeze the curve layout before the continuous run; adding curves during
    // a frame must not become another source of hierarchy/history changes.
    for (const auto& Value : Inputs)
        for (const auto& Pair : Value->AsObject()->GetObjectField(TEXT("RigCapture"))->GetObjectField(TEXT("Stages"))
            ->GetObjectField(TEXT("PreFoot"))->GetObjectField(TEXT("curves"))->Values)
        {
            const FRigElementKey Key(*Pair.Key, ERigElementType::Curve);
            if (H->GetIndex(Key) == INDEX_NONE) Controller->AddCurve(Key.Name, 0, false);
        }
    TArray<FRigElementKey> Keys; TArray<int32> SourceIndices;
    TArray<TSharedPtr<FJsonValue>> OutputNames, MissingNames, Initial;
    for (int32 I = 0; I < Names.Num(); ++I)
    {
        const FRigElementKey Key(*Names[I]->AsString(), ERigElementType::Bone);
        if (H->GetIndex(Key) == INDEX_NONE) { MissingNames.Add(Names[I]); continue; }
        Keys.Add(Key); SourceIndices.Add(I); OutputNames.Add(Names[I]); Initial.Add(SerializePose(H->GetInitialGlobalTransform(Key)));
    }
    for (const auto Name : {TEXT("pelvis"), TEXT("thigh_l"), TEXT("calf_l"), TEXT("foot_l"), TEXT("thigh_r"), TEXT("calf_r"), TEXT("foot_r"), TEXT("ik_foot_root"), TEXT("ik_foot_l"), TEXT("ik_foot_r")})
        if (!Keys.Contains(FRigElementKey(Name, ERigElementType::Bone)))
        { UE_LOG(LogTemp, Error, TEXT("ALS_RIG_REPLAY missing required bone %s"), Name); return false; }
    TArray<TSharedPtr<FJsonValue>> Rows;
    for (int32 F = 0; F < Inputs.Num(); ++F)
    {
        const auto Source = Inputs[F]->AsObject();
        if (Source->GetIntegerField(TEXT("Frame")) != F + 1) return false;
        const auto Capture = Source->GetObjectField(TEXT("RigCapture")); const auto Input = Capture->GetObjectField(TEXT("Input"));
        const auto Before = Capture->GetObjectField(TEXT("Stages"))->GetObjectField(TEXT("PreFoot"));
        const auto& Local = Before->GetArrayField(TEXT("pose")); if (Local.Num() != Names.Num()) return false;
        TArray<FTransform> Components;
        for (int32 B = 0; B < Local.Num(); ++B)
        {
            const int32 Parent = static_cast<int32>(Parents[B]->AsNumber()); if (Parent < -1 || Parent >= B) return false;
            auto T = Pose(Local[B]->AsObject()); if (Parent >= 0) T = T * Components[Parent]; T.NormalizeRotation(); Components.Add(T);
        }
        H->ResetPoseToInitial(ERigElementType::Bone);
        // Input transfer is a pose copy, not an authored SetTransform rig unit.
        // The optimized AnimNode pose adapter copies bones and dirties their
        // dependent transforms without the hierarchy's near-equality filter.
        // Copy this controlled component-space snapshot exactly, then leave
        // every authored write inside the original VM unchanged.
        for (const auto& Key : H->GetBoneKeys())
            H->Get<FRigBoneElement>(H->GetIndex(Key))->GetDirtyState().MarkDirty(ERigTransformType::CurrentGlobal);
        for (int32 B = 0; B < Keys.Num(); ++B)
        {
            auto* Bone = H->Get<FRigBoneElement>(H->GetIndex(Keys[B]));
            Bone->GetTransform().Set(ERigTransformType::CurrentGlobal, Components[SourceIndices[B]]);
            Bone->GetDirtyState().MarkClean(ERigTransformType::CurrentGlobal);
            Bone->GetDirtyState().MarkDirty(ERigTransformType::CurrentLocal);
        }
        TArray<TSharedPtr<FJsonValue>> Transferred;
        for (int32 B = 0; B < Keys.Num(); ++B)
        {
            const auto Actual = H->GetGlobalTransform(Keys[B]);
            if (!Actual.Equals(Components[SourceIndices[B]], 1e-10))
            { UE_LOG(LogTemp, Error, TEXT("ALS_RIG_REPLAY input transfer mismatch at frame %d bone %s"), F + 1, *Keys[B].Name.ToString()); return false; }
            Transferred.Add(SerializePose(Actual));
        }
        for (const auto& Key : H->GetCurveKeys()) H->SetCurveValue(Key, 0);
        for (const auto& Pair : Before->GetObjectField(TEXT("curves"))->Values)
        {
            const FRigElementKey Key(*Pair.Key, ERigElementType::Curve);
            if (H->GetIndex(Key) == INDEX_NONE) return false;
            H->SetCurveValue(Key, Pair.Value->AsNumber());
        }
        const auto ToWorld = Input->GetObjectField(TEXT("ToWorld"));
        Component->SetWorldTransform(FTransform(Q(ToWorld->GetObjectField(TEXT("Rotation"))).GetNormalized(),
            V(ToWorld->GetObjectField(TEXT("Position"))), V(ToWorld->GetObjectField(TEXT("Scale")))));
        Box->SetWorldTransform(NativePlatform(Source->GetObjectField(TEXT("Platform"))));
        RigInput->bFootTransformsValid = Input->GetBoolField(TEXT("FootTransformsValid"));
        RigInput->PelvisOffsetAmount = Input->GetNumberField(TEXT("PelvisAmount"));
        const auto Left = Input->GetObjectField(TEXT("Left")); const auto Right = Input->GetObjectField(TEXT("Right"));
        RigInput->FootLeftLocation = V(Left->GetObjectField(TEXT("Location"))); RigInput->FootLeftRotation = Q(Left->GetObjectField(TEXT("Rotation")));
        RigInput->FootRightLocation = V(Right->GetObjectField(TEXT("Location"))); RigInput->FootRightRotation = Q(Right->GetObjectField(TEXT("Rotation")));
        TSharedPtr<FJsonObject> FeedbackTrace;
        if (Feedback && !Feedback->Update(F + 1, Capture, *RigInput, FeedbackTrace)) return false;
        Rig->SetDeltaTime(Input->GetNumberField(TEXT("DeltaTime")));
        if (Input->GetBoolField(TEXT("ExecuteRig")) && !Rig->ExecuteEvent(FRigUnit_BeginExecution::EventName))
        { UE_LOG(LogTemp, Error, TEXT("ALS_RIG_REPLAY forwards event unavailable at frame %d"), F + 1); return false; }
        auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("frame"), F + 1);
        Row->SetArrayField(TEXT("inputComponents"), Transferred);
        TArray<TSharedPtr<FJsonValue>> After;
        for (const auto& Key : Keys) After.Add(SerializePose(H->GetGlobalTransform(Key)));
        Row->SetArrayField(TEXT("components"), After);
        if (Feedback)
        {
            Feedback->Commit(*H, *Mesh);
            Row->SetObjectField(TEXT("feedback"), FeedbackTrace);
            Row->SetObjectField(TEXT("sole"), Feedback->CaptureSoles(*Mesh, Box->GetComponentTransform()));
            auto Curves = MakeShared<FJsonObject>(); for (const auto& Pair : Feedback->Curves) Curves->SetNumberField(Pair.Key.ToString(), Pair.Value);
            Row->SetObjectField(TEXT("finalCurves"), Curves);
        }
        auto Variables = MakeShared<FJsonObject>();
        for (const auto& Variable : Rig->GetExternalVariables())
            Variables->SetStringField(Variable.GetName().ToString(), Rig->GetVariableAsString(Variable.GetName()));
        Row->SetObjectField(TEXT("variables"), Variables);
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("schemaVersion"), 2);
    Result->SetStringField(TEXT("inputTransfer"), TEXT("Exact controlled component pose copy; not a full AnimNode/character evaluation"));
    Result->SetBoolField(TEXT("closedFeedback"), bClosedFeedback);
    if (bClosedFeedback) Result->SetStringField(TEXT("feedbackScope"), TEXT("Original continuous RefreshFeetOnGameThread/RefreshPose/RefreshFeet/GetControlRigInput/Rig with native prior mesh pose and curve history; captured grounded movement and pre-rig animation only"));
    Result->SetStringField(TEXT("source"), bClosedFeedback
        ? TEXT("Original continuous ALS foot animation functions and CR_Als VM; native mesh/socket/curve history and CPU-skinned soles; captured pre-rig animation and movement; no asset saves")
        : TEXT("Original CR_Als Forwards Solve VM; native mesh reference; captured pre-rig pose and lock targets; independent world collision; no asset saves"));
    Result->SetArrayField(TEXT("names"), OutputNames); Result->SetArrayField(TEXT("missingInputNames"), MissingNames);
    Result->SetArrayField(TEXT("initialComponents"), Initial); Result->SetArrayField(TEXT("frames"), Rows);
    FString Json; return FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)) &&
        FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

bool UAlsAnimationGraphLibrary::ReplayRefactoredCharacterPlatform(const FString& RequestPath, const FString& OutputPath)
{
    using namespace AlsFootRigReplay;
    if (!IsInGameThread() || FPaths::IsRelative(RequestPath) || FPaths::IsRelative(OutputPath) ||
        IFileManager::Get().FileExists(*OutputPath)) return false;
    // Control the initialization frame as well as the subsequent world ticks.
    // Cold commandlets start at frame zero whereas an Editor script does not;
    // native animation relevance caches must see the same nonzero lifecycle.
    const TGuardValue<uint64> InitializationFrame(GFrameCounter, 500000);
    FString Text; TArray<TSharedPtr<FJsonValue>> Inputs;
    if (!FFileHelper::LoadFileToString(Text, *RequestPath) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Inputs) || Inputs.Num() != 360) return false;
    auto* CharacterClass = LoadClass<AAlsCharacter>(nullptr, TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"));
    if (!CharacterClass) return false;
    const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(true)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::Game, false, NAME_None,
        nullptr, false, ERHIFeatureLevel::Num, &Initialization));
    if (!World.IsValid()) return false;
    GEngine->CreateNewWorldContext(EWorldType::Game).SetCurrentWorld(World.Get());
    struct FCleanup
    {
        UWorld* World; double Delta = FApp::GetDeltaTime();
        ~FCleanup() { World->EndPlay(EEndPlayReason::Quit); World->DestroyWorld(false);
            GEngine->DestroyWorldContext(World); FApp::SetDeltaTime(Delta); }
    } Cleanup{World.Get()};
    FActorSpawnParameters Spawn; Spawn.ObjectFlags = RF_Transient;
    Spawn.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    auto* Floor = World->SpawnActor<AActor>(AActor::StaticClass(), FTransform::Identity, Spawn);
    if (!Floor) return false;
    auto* Box = NewObject<UBoxComponent>(Floor); Floor->SetRootComponent(Box); Floor->AddInstanceComponent(Box);
    Box->SetMobility(EComponentMobility::Movable); Box->SetBoxExtent(FVector(5000, 5000, 50));
    Box->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics); Box->SetCollisionResponseToAllChannels(ECR_Block);
    Box->RegisterComponent(); Box->SetWorldTransform(NativePlatform(Inputs[0]->AsObject()->GetObjectField(TEXT("Platform"))));
    const auto First = Inputs[0]->AsObject()->GetObjectField(TEXT("RigCapture"))->GetObjectField(TEXT("Input"))->GetObjectField(TEXT("ToWorld"));
    const auto* Defaults = CharacterClass->GetDefaultObject<AAlsCharacter>();
    const auto ActorRotation = Q(First->GetObjectField(TEXT("Rotation"))) * Defaults->GetMesh()->GetRelativeRotation().Quaternion().Inverse();
    const auto ActorLocation = V(First->GetObjectField(TEXT("Position"))) - ActorRotation.RotateVector(Defaults->GetMesh()->GetRelativeLocation());
    auto* Character = World->SpawnActor<AAlsCharacter>(CharacterClass, FTransform(ActorRotation, ActorLocation), Spawn);
    auto* Controller = World->SpawnActor<APlayerController>(APlayerController::StaticClass(), FTransform::Identity, Spawn);
    if (!Character || !Controller) return false;
    auto* MeshComponent = Character->GetMesh(); auto* Mesh = MeshComponent->GetSkeletalMeshAsset();
    if (!Mesh) return false;
    MeshComponent->SetForcedLOD(1); // Full-quality LOD0 observation in the headless world.
    // An independent reference component selects fixed sole vertices. Never
    // overwrite the real character's initial or subsequent animation poses.
    auto* ReferenceOwner = World->SpawnActor<AActor>(AActor::StaticClass(), FTransform::Identity, Spawn);
    if (!ReferenceOwner) return false;
    auto* Reference = NewObject<USkeletalMeshComponent>(ReferenceOwner);
    ReferenceOwner->SetRootComponent(Reference); ReferenceOwner->AddInstanceComponent(Reference);
    Reference->SetSkeletalMesh(Mesh); Reference->SetCollisionEnabled(ECollisionEnabled::NoCollision); Reference->RegisterComponent();
    Reference->SetComponentTickEnabled(false);
    Reference->SetComponentSpaceTransformsDoubleBuffering(false);
    auto& ReferencePose = Reference->GetEditableComponentSpaceTransforms(); const auto& Ref = Mesh->GetRefSkeleton();
    ReferencePose.SetNum(Ref.GetNum());
    for (int32 B = 0; B < ReferencePose.Num(); ++B)
        ReferencePose[B] = Ref.GetParentIndex(B) < 0 ? Ref.GetRefBonePose()[B] : Ref.GetRefBonePose()[B] * ReferencePose[Ref.GetParentIndex(B)];
    FNativeSoles Soles(*MeshComponent);
    if (!Soles.InitializeSoles(*Mesh, Reference)) { UE_LOG(LogTemp, Error, TEXT("ALS_CHARACTER_PLATFORM sole binding failed")); return false; }
    // PostInitializeComponents binds the original animation instance. Possess
    // only after that lifecycle step; ALS accesses it in PossessedBy.
    World->InitializeActorsForPlay(FURL());
    auto* Animation = Cast<UAlsAnimationInstance>(MeshComponent->GetAnimInstance());
    if (!Animation || !Character->GetSettings())
    { UE_LOG(LogTemp, Error, TEXT("ALS_CHARACTER_PLATFORM original actor/animation not initialized")); return false; }
    // This isolated standalone controller has no viewport ULocalPlayer. Use
    // the engine's local-controller initialization so ControlledCharacterMove
    // runs, rather than silently testing an un-driven remote pawn.
    Controller->SetAsLocalPlayerController();
    Controller->Possess(Character); Controller->SetControlRotation(ActorRotation.Rotator());
    Character->SetDesiredGait(AlsGaitTags::Running);
    World->BeginPlay(); World->GetWorldSettings()->NotifyBeginPlay();
    if (!Character->HasActorBegunPlay()) return false;
    TArray<TSharedPtr<FJsonValue>> Rows;
    int32 ValidFrames = 0, GroundedFrames = 0, MovingFrames = 0, SkippedAnimationFrames = 0;
    const uint64 FirstGlobalFrame = GFrameCounter;
    for (int32 F = 0; F < Inputs.Num(); ++F)
    {
        const FMemMark Mark(FMemStack::Get());
        const TGuardValue<uint64> FrameGuard(GFrameCounter, FirstGlobalFrame + F + 1);
        const auto Source = Inputs[F]->AsObject(); const auto Capture = Source->GetObjectField(TEXT("RigCapture"));
        if (Source->GetIntegerField(TEXT("Frame")) != F + 1) return false;
        const float Dt = Capture->GetObjectField(TEXT("MotorInput"))->GetNumberField(TEXT("DeltaTime"));
        if (!FMath::IsFinite(Dt) || Dt <= 0 || Dt > .1f) return false;
        FApp::SetDeltaTime(Dt);
        Box->SetWorldTransform(NativePlatform(Source->GetObjectField(TEXT("Platform"))), false, nullptr, ETeleportType::None);
        const auto Axes = Capture->GetObjectField(TEXT("MotorInput"))->GetObjectField(TEXT("Command"))->GetObjectField(TEXT("MovementAxes"));
        Character->AddMovementInput(FVector::ForwardVector, Axes->GetNumberField(TEXT("Y")));
        Character->AddMovementInput(FVector::RightVector, Axes->GetNumberField(TEXT("X")));
        // The isolated world has no viewport. Supply visibility only, so the
        // original character's visibility policy still controls its mesh tick.
        MeshComponent->SetLastRenderTime(World->GetTimeSeconds()); MeshComponent->bRecentlyRendered = true;
        const int16 PreviousUpdate = Animation->GetUpdateCounter().Get();
        World->Tick(LEVELTICK_All, Dt);
        MeshComponent->HandleExistingParallelEvaluationTask(true, true);
        const int16 CurrentUpdate = Animation->GetUpdateCounter().Get();
        if (CurrentUpdate == PreviousUpdate) ++SkippedAnimationFrames;
        const auto& Feet = FFeedbackAccess::Feet(*Animation); const auto& Base = FFeedbackAccess::Base(*Animation);
        if (Feet.bValid) ++ValidFrames;
        if (Character->GetCharacterMovement()->IsMovingOnGround()) ++GroundedFrames;
        if (Character->GetVelocity().Size2D() > 1) ++MovingFrames;
        auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("frame"), F + 1);
        Row->SetNumberField(TEXT("worldTime"), World->GetTimeSeconds());
        Row->SetNumberField(TEXT("animationUpdateCounter"), CurrentUpdate);
        Row->SetField(TEXT("actor"), SerializePose(Character->GetActorTransform()));
        Row->SetField(TEXT("mesh"), SerializePose(MeshComponent->GetComponentTransform()));
        Row->SetField(TEXT("platform"), SerializePose(Box->GetComponentTransform()));
        Row->SetField(TEXT("animationBase"), SerializePose(FTransform(Base.Rotation, Base.Location)));
        Row->SetBoolField(TEXT("relativeLocation"), Base.bHasRelativeLocation);
        Row->SetBoolField(TEXT("relativeRotation"), Base.bHasRelativeRotation);
        Row->SetBoolField(TEXT("baseChanged"), Base.bBaseChanged);
        Row->SetBoolField(TEXT("onExpectedBase"), Character->GetCharacterMovement()->GetMovementBaseObject() == Box);
        Row->SetBoolField(TEXT("grounded"), Character->GetCharacterMovement()->IsMovingOnGround());
        Row->SetArrayField(TEXT("velocity"), Vector(Character->GetVelocity()));
        Row->SetBoolField(TEXT("valid"), Feet.bValid);
        Row->SetObjectField(TEXT("left"), SerializeFoot(Feet.Left)); Row->SetObjectField(TEXT("right"), SerializeFoot(Feet.Right));
        Row->SetObjectField(TEXT("sole"), Soles.CaptureSoles(*Mesh, Box->GetComponentTransform()));
        auto Curves = MakeShared<FJsonObject>();
        for (const auto& Pair : Animation->GetAnimationCurves(EAnimCurveType::AttributeCurve))
            Curves->SetNumberField(Pair.Key.ToString(), Pair.Value);
        Row->SetObjectField(TEXT("curves"), Curves);
        TArray<TSharedPtr<FJsonValue>> Components;
        for (const auto& Pose : MeshComponent->GetComponentSpaceTransforms()) Components.Add(SerializePose(Pose));
        Row->SetArrayField(TEXT("components"), Components); Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    if (ValidFrames < 300 || GroundedFrames < 300 || MovingFrames < 100 || SkippedAnimationFrames > 0)
    { UE_LOG(LogTemp, Error, TEXT("ALS_CHARACTER_PLATFORM coverage valid=%d grounded=%d moving=%d skipped_animation=%d"), ValidFrames, GroundedFrames, MovingFrames, SkippedAnimationFrames); return false; }
    auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("schemaVersion"), 1);
    Result->SetStringField(TEXT("scope"), TEXT("Original B_Als_Character, original mesh and AnimBP, CharacterMovement and World Tick; controlled platform/input/visibility; no Godot pose, curve, foot state or per-frame actor transform injection; standalone isolated world, not network/camera acceptance"));
    Result->SetStringField(TEXT("characterClass"), CharacterClass->GetPathName()); Result->SetStringField(TEXT("meshSource"), Mesh->GetPathName());
    Result->SetStringField(TEXT("animationClass"), Animation->GetClass()->GetPathName());
    Result->SetStringField(TEXT("characterSettings"), Character->GetSettings()->GetPathName());
    Result->SetBoolField(TEXT("locallyControlled"), Character->IsLocallyControlled());
    Result->SetBoolField(TEXT("ignoreBaseRotation"), Character->GetCharacterMovement()->bIgnoreBaseRotation);
    Result->SetBoolField(TEXT("useControllerRotationPitch"), Character->bUseControllerRotationPitch);
    Result->SetBoolField(TEXT("useControllerRotationYaw"), Character->bUseControllerRotationYaw);
    Result->SetBoolField(TEXT("useControllerRotationRoll"), Character->bUseControllerRotationRoll);
    TArray<TSharedPtr<FJsonValue>> Names; for (int32 B = 0; B < Ref.GetNum(); ++B) Names.Add(MakeShared<FJsonValueString>(Ref.GetBoneName(B).ToString()));
    Result->SetArrayField(TEXT("names"), Names); Result->SetArrayField(TEXT("frames"), Rows);
    FString Json; return FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)) &&
        FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

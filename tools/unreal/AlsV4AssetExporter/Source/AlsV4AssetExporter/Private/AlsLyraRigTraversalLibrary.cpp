#include "AlsLyraRigTraversalLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "AnimNode_ControlRig.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/BoxComponent.h"
#include "ControlRig.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "Engine/CollisionProfile.h"
#include "GameFramework/Character.h"
#include "Kismet/KismetMathLibrary.h"
#include "RigVMCore/RigVM.h"
#include "RigVMCore/RigVMProfilingInfo.h"
#include "RigVMCore/RigVMMemoryStorageStruct.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"
#include "Serialization/JsonSerializer.h"

namespace LyraRigTraversalProbe
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_RIG_TRAVERSAL_FAILED line=%d"),Line);return {};}
TSharedPtr<FJsonValue> Number(double V){check(FMath::IsFinite(V));return MakeShared<FJsonValueNumber>(V);}
TSharedPtr<FJsonValue> Vector(const FVector& V){return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{Number(V.X),Number(V.Y),Number(V.Z)});}
TSharedPtr<FJsonValue> Quat(const FQuat& V){return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{Number(V.X),Number(V.Y),Number(V.Z),Number(V.W)});}
TSharedPtr<FJsonObject> Transform(const FTransform& V){auto O=MakeShared<FJsonObject>();O->SetField(TEXT("p"),Vector(V.GetLocation()));O->SetField(TEXT("q"),Quat(V.GetRotation()));O->SetField(TEXT("s"),Vector(V.GetScale3D()));return O;}
TSharedPtr<FJsonValue> Property(const FProperty* P,const void* Value)
{
    if(auto* B=CastField<FBoolProperty>(P))return MakeShared<FJsonValueBoolean>(B->GetPropertyValue(Value));
    if(auto* N=CastField<FNumericProperty>(P))return Number(N->IsFloatingPoint()?N->GetFloatingPointPropertyValue(Value):static_cast<double>(N->GetSignedIntPropertyValue(Value)));
    if(auto* S=CastField<FStructProperty>(P))
    {
        if(S->Struct==FFloatSpringState::StaticStruct())
        {const auto& V=*static_cast<const FFloatSpringState*>(Value);auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("velocity"),V.Velocity);O->SetNumberField(TEXT("target"),V.PrevTarget);O->SetBoolField(TEXT("valid"),V.bPrevTargetValid);return MakeShared<FJsonValueObject>(O);}
        if(S->Struct==FVectorSpringState::StaticStruct())
        {const auto& V=*static_cast<const FVectorSpringState*>(Value);auto O=MakeShared<FJsonObject>();O->SetField(TEXT("velocity"),Vector(V.Velocity));O->SetField(TEXT("target"),Vector(V.PrevTarget));O->SetBoolField(TEXT("valid"),V.bPrevTargetValid);return MakeShared<FJsonValueObject>(O);}
        if(S->Struct==FQuaternionSpringState::StaticStruct())
        {const auto& V=*static_cast<const FQuaternionSpringState*>(Value);auto O=MakeShared<FJsonObject>();O->SetField(TEXT("velocity"),Vector(V.AngularVelocity));O->SetField(TEXT("target"),Quat(V.PrevTarget));O->SetBoolField(TEXT("valid"),V.bPrevTargetValid);return MakeShared<FJsonValueObject>(O);}
        if(S->Struct==TBaseStructure<FVector>::Get())return Vector(*static_cast<const FVector*>(Value));
        if(S->Struct==TBaseStructure<FQuat>::Get())return Quat(*static_cast<const FQuat*>(Value));
        if(S->Struct==TBaseStructure<FTransform>::Get())return MakeShared<FJsonValueObject>(Transform(*static_cast<const FTransform*>(Value)));
        auto O=MakeShared<FJsonObject>();for(TFieldIterator<FProperty> F(S->Struct);F;++F)O->SetField(F->GetName(),Property(*F,F->ContainerPtrToValuePtr<void>(Value)));
        if(S->Struct==FInputScaleBiasClamp::StaticStruct()){const auto& V=*static_cast<const FInputScaleBiasClamp*>(Value);O->SetBoolField(TEXT("initialized"),V.bInitialized);O->SetNumberField(TEXT("interpolated"),V.InterpolatedResult);}return MakeShared<FJsonValueObject>(O);
    }
    if(auto* A=CastField<FArrayProperty>(P))
    {FScriptArrayHelper H(A,Value);TArray<TSharedPtr<FJsonValue>> Values;for(int32 I=0;I<H.Num();++I)Values.Add(Property(A->Inner,H.GetRawPtr(I)));return MakeShared<FJsonValueArray>(Values);}
    FString Text;P->ExportTextItem_Direct(Text,Value,nullptr,nullptr,PPF_None);return MakeShared<FJsonValueString>(Text);
}
TSharedPtr<FJsonObject> Memory(FRigVMMemoryStorageStruct* M)
{
    auto O=MakeShared<FJsonObject>();if(!M)return O;for(int32 I=0;I<M->Num();++I)O->SetField(M->GetProperty(I)->GetName(),Property(M->GetProperty(I),M->GetData<uint8>(I)));return O;
}
TSharedPtr<FJsonObject> RigState(UControlRig* Rig,bool FullHierarchy=false)
{
    auto O=MakeShared<FJsonObject>();auto Variables=MakeShared<FJsonObject>();
    for(const auto& V:Rig->GetExternalVariables())if(auto* P=FindFProperty<FProperty>(Rig->GetClass(),V.GetName()))Variables->SetField(P->GetName(),Property(P,P->ContainerPtrToValuePtr<void>(Rig)));
    O->SetObjectField(TEXT("variables"),Variables);O->SetNumberField(TEXT("delta"),Rig->GetDeltaTime());auto* VM=Rig->GetVM();O->SetObjectField(TEXT("work"),Memory(VM->GetWorkMemory(Rig->GetRigVMExtendedExecuteContext())));
    auto H=MakeShared<FJsonObject>();for(const auto& K:Rig->GetHierarchy()->GetAllKeys())
    {
        if(K.Type==ERigElementType::Curve)continue;
        if(!FullHierarchy&&K.Type!=ERigElementType::Control&&K.Name!=TEXT("pelvis")&&K.Name!=TEXT("foot_l")&&K.Name!=TEXT("foot_r")&&K.Name!=TEXT("ik_foot_l")&&K.Name!=TEXT("ik_foot_r")&&K.Name!=TEXT("ik_foot_root"))continue;
        auto E=MakeShared<FJsonObject>();E->SetObjectField(TEXT("local"),Transform(Rig->GetHierarchy()->GetLocalTransform(K)));E->SetObjectField(TEXT("global"),Transform(Rig->GetHierarchy()->GetGlobalTransform(K)));E->SetObjectField(TEXT("initialLocal"),Transform(Rig->GetHierarchy()->GetLocalTransform(K,true)));E->SetObjectField(TEXT("initialGlobal"),Transform(Rig->GetHierarchy()->GetGlobalTransform(K,true)));
        if(K.Type==ERigElementType::Control)
        {auto* C=Rig->GetHierarchy()->Find<FRigControlElement>(K);E->SetObjectField(TEXT("offsetLocal"),Transform(Rig->GetHierarchy()->GetControlOffsetTransform(C,ERigTransformType::CurrentLocal)));E->SetObjectField(TEXT("offsetGlobal"),Transform(Rig->GetHierarchy()->GetControlOffsetTransform(C,ERigTransformType::CurrentGlobal)));E->SetObjectField(TEXT("initialOffsetLocal"),Transform(Rig->GetHierarchy()->GetControlOffsetTransform(C,ERigTransformType::InitialLocal)));E->SetObjectField(TEXT("initialOffsetGlobal"),Transform(Rig->GetHierarchy()->GetControlOffsetTransform(C,ERigTransformType::InitialGlobal)));}
        H->SetObjectField(K.Name.ToString(),E);
    }O->SetObjectField(TEXT("hierarchy"),H);return O;
}
TSharedPtr<FJsonObject> Program(UControlRig* Rig)
{
    auto O=MakeShared<FJsonObject>();auto* VM=Rig->GetVM();auto& Context=Rig->GetRigVMExtendedExecuteContext();const auto& B=VM->GetByteCode();
    TArray<TSharedPtr<FJsonValue>> Functions;for(const auto& Name:VM->GetFunctionNames())Functions.Add(MakeShared<FJsonValueString>(Name.ToString()));O->SetArrayField(TEXT("functions"),Functions);
    TArray<TSharedPtr<FJsonValue>> Entries;for(int32 I=0;I<B.NumEntries();++I){const auto& E=B.GetEntry(I);auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("name"),E.Name.ToString());Row->SetNumberField(TEXT("instruction"),E.InstructionIndex);Entries.Add(MakeShared<FJsonValueObject>(Row));}O->SetArrayField(TEXT("entries"),Entries);
    // ReadProgram runs before external-memory binding; operand names come from
    // VM definitions, whose register order matches the compiled bytecode.
    TArray<TSharedPtr<FJsonValue>> Instructions;const auto Text=VM->DumpByteCodeAsTextArray(Context);const auto Ops=B.GetInstructions();const auto& External=VM->GetExternalVariableDefs();for(int32 I=0;I<Text.Num();++I)
    {
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("index"),I);Row->SetStringField(TEXT("text"),Text[I]);auto* Subject=B.GetSubjectForInstruction(I);Row->SetStringField(TEXT("subject"),Subject?Subject->GetPathName():FString());
        TArray<TSharedPtr<FJsonValue>> Operands;for(const auto& A:B.GetOperandsForOp(Ops[I]))
        {
            auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("memory"),static_cast<int32>(A.GetMemoryType()));V->SetNumberField(TEXT("register"),A.GetRegisterIndex());V->SetNumberField(TEXT("offset"),A.GetRegisterOffset());FString Name,Path;
            FRigVMMemoryStorageStruct* M=A.GetMemoryType()==ERigVMMemoryType::Work?VM->GetWorkMemory(Context):(A.GetMemoryType()==ERigVMMemoryType::Literal?VM->GetLiteralMemory():nullptr);
            if(M){check(A.GetRegisterIndex()>=0&&A.GetRegisterIndex()<M->Num());Name=M->GetProperty(A.GetRegisterIndex())->GetName();if(A.GetRegisterOffset()!=INDEX_NONE)Path=M->GetPropertyPaths()[A.GetRegisterOffset()].ToString();}
            else if(A.GetMemoryType()==ERigVMMemoryType::External){check(External.IsValidIndex(A.GetRegisterIndex()));Name=External[A.GetRegisterIndex()].GetName().ToString();if(A.GetRegisterOffset()!=INDEX_NONE)Path=VM->ExternalPropertyPaths[A.GetRegisterOffset()].ToString();}
            V->SetStringField(TEXT("name"),Name);V->SetStringField(TEXT("path"),Path);Operands.Add(MakeShared<FJsonValueObject>(V));
        }
        Row->SetArrayField(TEXT("operands"),Operands);Instructions.Add(MakeShared<FJsonValueObject>(Row));
    }O->SetArrayField(TEXT("instructions"),Instructions);
    auto Flow=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> FlowInstructions,Branches;
    for(int32 I=0;I<Ops.Num();++I)
    {
        const auto& Instruction=Ops[I];auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("index"),I);
        Row->SetStringField(TEXT("opcode"),StaticEnum<ERigVMOpCode>()->GetNameStringByValue(static_cast<int64>(Instruction.OpCode)));
        if(Instruction.OpCode==ERigVMOpCode::JumpToBranch){const auto& Op=B.GetOpAt<FRigVMJumpToBranchOp>(Instruction);Row->SetNumberField(TEXT("firstBranch"),Op.FirstBranchInfoIndex);}
        else if(Instruction.OpCode==ERigVMOpCode::RunInstructions){const auto& Op=B.GetOpAt<FRigVMRunInstructionsOp>(Instruction);Row->SetNumberField(TEXT("first"),Op.StartInstruction);Row->SetNumberField(TEXT("last"),Op.EndInstruction);}
        else if(Instruction.OpCode==ERigVMOpCode::JumpAbsolute||Instruction.OpCode==ERigVMOpCode::JumpForward||Instruction.OpCode==ERigVMOpCode::JumpBackward)
        {Row->SetNumberField(TEXT("distance"),B.GetOpAt<FRigVMJumpOp>(Instruction).InstructionIndex);}
        FlowInstructions.Add(MakeShared<FJsonValueObject>(Row));
    }
    for(int32 I=0;I<B.NumBranches();++I)
    {
        const auto& Branch=B.GetBranch(I);auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("index"),Branch.Index);
        Row->SetNumberField(TEXT("instruction"),Branch.InstructionIndex);Row->SetNumberField(TEXT("argument"),Branch.ArgumentIndex);
        Row->SetStringField(TEXT("label"),Branch.Label.ToString());Row->SetNumberField(TEXT("first"),Branch.FirstInstruction);Row->SetNumberField(TEXT("last"),Branch.LastInstruction);Branches.Add(MakeShared<FJsonValueObject>(Row));
    }
    Flow->SetArrayField(TEXT("instructions"),FlowInstructions);Flow->SetArrayField(TEXT("branches"),Branches);O->SetObjectField(TEXT("flow"),Flow);
    O->SetObjectField(TEXT("literal"),Memory(VM->GetLiteralMemory()));O->SetObjectField(TEXT("initial"),RigState(Rig,true));auto Types=MakeShared<FJsonObject>();for(const auto* P:VM->GetWorkMemory(Context)->GetProperties())Types->SetStringField(P->GetName(),P->GetCPPType());O->SetObjectField(TEXT("workTypes"),Types);return O;
}
FString Json(const TSharedPtr<FJsonObject>& O){FString Text;FJsonSerializer::Serialize(O.ToSharedRef(),TJsonWriterFactory<>::Create(&Text));return Text;}
struct FInstanceAccess:UAnimInstance{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S)
    {(P.*&FProxyAccess::InitializeObjects)(A);check(P.GetSkeleton()==S);TArray<FBoneIndexType> Bones;for(int32 I=0;I<81;++I)Bones.Add(static_cast<FBoneIndexType>(I));P.GetRequiredBones().InitializeTo(Bones,UE::Anim::FCurveFilterSettings(),*S);P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);(P.*&FProxyAccess::CachedBonesCounter).Increment();}
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D){(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
};
struct FNodeAccess:FAnimNode_ControlRigBase
{
    static void SetSource(FAnimNode_ControlRig& N,FAnimNode_Base* Leaf){(N.*&FNodeAccess::Source).SetLinkNode(Leaf);}
    static float Alpha(const FAnimNode_ControlRig& N){return N.*&FNodeAccess::InternalBlendAlpha;}
};
struct FRigSourceLeaf:FAnimNode_Base
{
    UAnimSequence* Sequence=nullptr;TSharedPtr<FJsonObject> Frame;TSharedPtr<FJsonObject> Input;USkeleton* Skeleton=nullptr;
    void Evaluate_AnyThread(FPoseContext& O)override
    {FDeltaTimeRecord D;D.Set(Frame->GetNumberField(TEXT("previous")),Frame->GetNumberField(TEXT("sourceDelta")));FAnimationPoseData Data(O);FAnimExtractContext E(Frame->GetNumberField(TEXT("time")),false,D,true);E.bExtractWithRootMotionProvider=true;Sequence->GetAnimationPose(Data,E);Input=LyraCyclePoseProbe::PoseData(O.Pose,O.Curve,O.CustomAttributes,Skeleton->GetReferenceSkeleton());}
};
FVector ReadVector(const TSharedPtr<FJsonObject>& O,const TCHAR* K){const auto& A=O->GetArrayField(K);return FVector(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber());}
FQuat ReadQuat(const TSharedPtr<FJsonObject>& O,const TCHAR* K){const auto& A=O->GetArrayField(K);return FQuat(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber(),A[3]->AsNumber());}
bool SetBool(UObject* O,const TCHAR* Name,bool V){auto* P=FindFProperty<FBoolProperty>(O->GetClass(),Name);if(!P)return false;P->SetPropertyValue_InContainer(O,V);return true;}
void SetVector(UObject* O,const TCHAR* Name,const FVector& V){if(auto* P=FindFProperty<FStructProperty>(O->GetClass(),Name)){check(P->Struct==TBaseStructure<FVector>::Get());*P->ContainerPtrToValuePtr<FVector>(O)=V;}}
TSharedPtr<FJsonObject> BoolBlendState(const FAnimNode_ControlRig& N)
{
    const auto* P=FindFProperty<FStructProperty>(FAnimNode_ControlRig::StaticStruct(),TEXT("AlphaBoolBlend"));check(P);const auto& B=*P->ContainerPtrToValuePtr<FInputAlphaBoolBlend>(&N);
    auto R=MakeShared<FJsonObject>();R->SetBoolField(TEXT("initialized"),B.bInitialized);R->SetNumberField(TEXT("begin"),B.AlphaBlend.GetBeginValue());R->SetNumberField(TEXT("target"),B.AlphaBlend.GetDesiredValue());R->SetNumberField(TEXT("alpha"),B.AlphaBlend.GetAlpha());R->SetNumberField(TEXT("value"),B.AlphaBlend.GetBlendedValue());R->SetNumberField(TEXT("time"),B.AlphaBlend.GetBlendTime());R->SetNumberField(TEXT("remaining"),B.AlphaBlend.GetBlendTimeRemaining());return R;
}
TSharedPtr<FJsonObject> NodeSettings(const FAnimNode_ControlRig& N)
{
    auto O=MakeShared<FJsonObject>();for(const TCHAR* Name:{TEXT("AlphaInputType"),TEXT("bAlphaBoolEnabled"),TEXT("bSetRefPoseFromSkeleton"),TEXT("SourcePropertyNames"),TEXT("DestPropertyNames")})
    {const auto* P=FindFProperty<FProperty>(FAnimNode_ControlRig::StaticStruct(),Name);check(P);O->SetField(Name,Property(P,P->ContainerPtrToValuePtr<void>(&N)));}
    const auto& H=N.GetEvaluateGraphExposedInputs();auto Handler=MakeShared<FJsonObject>();if(const auto* S=H.GetHandlerStruct())for(TFieldIterator<FProperty> P(S);P;++P)Handler->SetField(P->GetName(),Property(*P,P->ContainerPtrToValuePtr<void>(H.GetHandler())));O->SetObjectField(TEXT("handler"),Handler);return O;
}
}

FString UAlsLyraRigTraversalLibrary::ReadProgram(UClass* RigClass)
{
    using namespace LyraRigTraversalProbe;if(!RigClass||!RigClass->IsChildOf(UControlRig::StaticClass()))return Fail(__LINE__);
    TStrongObjectPtr<UControlRig> Rig(NewObject<UControlRig>(GetTransientPackage(),RigClass,NAME_None,RF_Transient));Rig->Initialize();if(!Rig->GetVM()||!Rig->GetHierarchy())return Fail(__LINE__);return Json(Program(Rig.Get()));
}
FString UAlsLyraRigTraversalLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraRigTraversalProbe;TSharedPtr<FJsonObject> Input;if(!MainClass||!Mesh||!Skeleton||Sequences.IsEmpty()||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
    auto Result=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Input->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());auto T=TV->AsObject();auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* World;~FCleanup(){World->DestroyWorld(false);}} Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* Owner=W->SpawnActor<ACharacter>(Spawn);auto* Ground=W->SpawnActor<AActor>(Spawn);if(!Owner||!Ground)return Fail(__LINE__);
        TStrongObjectPtr<UBoxComponent> Box(NewObject<UBoxComponent>(Ground,NAME_None,RF_Transient));Ground->SetRootComponent(Box.Get());Box->SetBoxExtent(FVector(10000,10000,5));Box->SetCollisionProfileName(TEXT("BlockAll"));Box->SetCollisionResponseToAllChannels(ECR_Block);Ground->AddInstanceComponent(Box.Get());Box->RegisterComponent();
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MainClass);C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();
        auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);auto& Proxy=FInstanceAccess::Proxy(Main);Carrier->SetSkeleton(Skeleton);FProxyAccess::Setup(Proxy,Main,Skeleton);auto* Node=Proxy.GetMutableNodeFromIndex<FAnimNode_ControlRig>(73);if(!Node||Skeleton->GetReferenceSkeleton().GetNum()!=81)return Fail(__LINE__);
        // Separate unbound native node for operator coverage. Its default exposed
        // handler is empty; the original compiled node remains untouched.
        auto* CompiledNode=Node;FAnimNode_ControlRig Operator;
        const bool OperatorMode=T->GetStringField(TEXT("mode"))==TEXT("OperatorBool");
        if(OperatorMode)
        {
            for(const TCHAR* Name:{TEXT("Alpha"),TEXT("AlphaInputType"),TEXT("bAlphaBoolEnabled"),TEXT("bSetRefPoseFromSkeleton"),TEXT("AlphaScaleBias"),TEXT("AlphaBoolBlend"),TEXT("AlphaScaleBiasClamp"),TEXT("AlphaCurveName"),TEXT("LODThreshold"),TEXT("SourcePropertyNames"),TEXT("DestPropertyNames")})
            {const auto* P=FindFProperty<FProperty>(FAnimNode_ControlRig::StaticStruct(),Name);check(P);P->CopyCompleteValue_InContainer(&Operator,CompiledNode);}
            Operator.SetControlRigClass(CompiledNode->GetControlRigAssetReference().GetBlueprintClass());Node=&Operator;
        }
        FRigSourceLeaf Leaf;Leaf.Skeleton=Skeleton;FNodeAccess::SetSource(*Node,&Leaf);Node->OnInitializeAnimInstance(&Proxy,Main);Node->Initialize_AnyThread(FAnimationInitializeContext(&Proxy));Node->CacheBones_AnyThread(FAnimationCacheBonesContext(&Proxy));auto* Rig=Node->GetControlRig();if(!Rig||!Rig->GetVM())return Fail(__LINE__);TStrongObjectPtr<UControlRig> KeepRig(Rig);
        const auto Channel=UEngineTypes::ConvertToCollisionChannel(static_cast<ETraceTypeQuery>(2));auto Trace=MakeShared<FJsonObject>();Trace->SetNumberField(TEXT("resolvedTraceChannel"),static_cast<int32>(Channel));Trace->SetStringField(TEXT("traceChannelName"),UCollisionProfile::Get()->ReturnChannelNameFromContainerIndex(static_cast<int32>(Channel)).ToString());Trace->SetNumberField(TEXT("groundResponse"),static_cast<int32>(Box->GetCollisionResponseToChannel(Channel)));Trace->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));Trace->SetStringField(TEXT("mode"),T->GetStringField(TEXT("mode")));Trace->SetObjectField(TEXT("program"),Program(Rig));Trace->SetObjectField(TEXT("settings"),NodeSettings(*Node));Trace->SetObjectField(TEXT("initialBoolBlend"),BoolBlendState(*Node));
        TArray<TSharedPtr<FJsonValue>> BoneNames;for(int32 B=0;B<81;++B)BoneNames.Add(MakeShared<FJsonValueString>(Skeleton->GetReferenceSkeleton().GetBoneName(B).ToString()));Trace->SetArrayField(TEXT("skeletonNames"),BoneNames);TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            if(auto* Visits=Rig->GetRigVMExtendedExecuteContext().GetRigVMInstructionVisitInfo())Visits->Reset();
            FMemMark FrameMark(FMemStack::Get());auto F=FV->AsObject();auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("before"),RigState(Rig));const float D=static_cast<float>(F->GetNumberField(TEXT("delta")));
            C->SetWorldTransform(FTransform(ReadQuat(F,TEXT("componentQ")),ReadVector(F,TEXT("componentP"))),false,nullptr,ETeleportType::TeleportPhysics);
            const FVector Normal=ReadVector(F,TEXT("floorNormal")),Point=ReadVector(F,TEXT("floorPoint"));Box->SetCollisionEnabled(F->GetBoolField(TEXT("geometry"))?ECollisionEnabled::QueryOnly:ECollisionEnabled::NoCollision);Box->SetWorldTransform(FTransform(FQuat::FindBetweenNormals(FVector::UpVector,Normal),Point-Normal*5),false,nullptr,ETeleportType::TeleportPhysics);
            FHitResult Sanity;FCollisionQueryParams Params;Params.bTraceComplex=true;Params.AddIgnoredActor(Owner);const bool SanityHit=W->SweepSingleByChannel(Sanity,Point+Normal*100,Point-Normal*100,FQuat::Identity,Channel,FCollisionShape::MakeSphere(5),Params);Row->SetBoolField(TEXT("groundSanityHit"),SanityHit);
            if(!SetBool(Main,TEXT("EnableControlRig"),F->GetBoolField(TEXT("enabled")))||!SetBool(Main,TEXT("IsCrouching"),F->GetBoolField(TEXT("crouching")))||!SetBool(Main,TEXT("HasVelocity"),F->GetBoolField(TEXT("moving"))))return Fail(__LINE__);
            const FVector Velocity=F->GetBoolField(TEXT("moving"))?FVector(120,30,0):FVector::ZeroVector;SetVector(Main,TEXT("WorldVelocity"),Velocity);SetVector(Main,TEXT("LocalVelocity2D"),Velocity);
            Leaf.Frame=F;Leaf.Sequence=Sequences[static_cast<int32>(F->GetNumberField(TEXT("asset")))];FProxyAccess::Pre(Proxy,Main,D);
            if(OperatorMode&&(F->GetBoolField(TEXT("initialize"))||F->GetBoolField(TEXT("visited"))))CompiledNode->GetEvaluateGraphExposedInputs().Execute(FAnimationUpdateContext(&Proxy,D));
            if(F->GetBoolField(TEXT("initialize"))){Node->Initialize_AnyThread(FAnimationInitializeContext(&Proxy));Node->CacheBones_AnyThread(FAnimationCacheBonesContext(&Proxy));}
            Row->SetObjectField(TEXT("boolBefore"),BoolBlendState(*Node));
            if(F->GetBoolField(TEXT("visited")))
            {
                FAnimationUpdateContext Context(&Proxy,D);
                if(T->GetStringField(TEXT("mode"))==TEXT("OperatorBool"))
                {
                    // Both actual Main property bindings were copied by the
                    // compiled handler. The separate operator accepts resolved bool.
                    FindFProperty<FBoolProperty>(FAnimNode_ControlRig::StaticStruct(),TEXT("bAlphaBoolEnabled"))->SetPropertyValue_InContainer(Node,F->GetBoolField(TEXT("enabled")));
                    Node->Update_AnyThread(Context);
                }
                else Node->Update_AnyThread(Context);
            }
            Row->SetNumberField(TEXT("alpha"),FNodeAccess::Alpha(*Node));Row->SetObjectField(TEXT("boolUpdated"),BoolBlendState(*Node));Row->SetObjectField(TEXT("nodeUpdated"),NodeSettings(*Node));Row->SetObjectField(TEXT("updated"),RigState(Rig));
            if(F->GetBoolField(TEXT("visited"))&&F->GetBoolField(TEXT("evaluate")))
            {FPoseContext Out(&Proxy);Node->Evaluate_AnyThread(Out);if(!Leaf.Input.IsValid())return Fail(__LINE__);Row->SetObjectField(TEXT("input"),Leaf.Input);Row->SetObjectField(TEXT("output"),LyraCyclePoseProbe::PoseData(Out.Pose,Out.Curve,Out.CustomAttributes,Skeleton->GetReferenceSkeleton()));}
            TArray<TSharedPtr<FJsonValue>> Order;for(int32 Index:Rig->GetVM()->GetInstructionVisitOrder(Rig->GetRigVMExtendedExecuteContext()))Order.Add(MakeShared<FJsonValueNumber>(Index));Row->SetArrayField(TEXT("visits"),Order);
            Row->SetObjectField(TEXT("after"),RigState(Rig));Rows.Add(MakeShared<FJsonValueObject>(Row));
        }
        Trace->SetArrayField(TEXT("frames"),Rows);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    Result->SetArrayField(TEXT("traces"),Traces);return Json(Result);
}

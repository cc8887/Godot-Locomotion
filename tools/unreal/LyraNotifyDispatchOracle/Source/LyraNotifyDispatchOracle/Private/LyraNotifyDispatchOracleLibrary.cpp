#include "LyraNotifyDispatchOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_TransitionResult.h"
#include "Animation/AnimNotifyQueue.h"
#include "Animation/AnimNotifyLibrary.h"
#include "Animation/ActiveStateMachineScope.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimSequenceBase.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "AnimStateTransitionNode.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Blueprint.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "K2Node_CallFunction.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace DispatchProbe
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_NOTIFY_DISPATCH_FAILED line=%d"),Line);return {};}
FString Write(const TSharedRef<FJsonObject>& R){FString S;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&S));return S;}
struct FAccess:UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}
    static void Trigger(UAnimInstance* A,float Delta){(A->*&FAccess::TriggerAnimNotifies)(Delta,ENotifyTriggerMode::ForceAnimGraphOnly);}
    static TArray<TSharedPtr<FJsonValue>> States(UAnimInstance* A)
    {TArray<TSharedPtr<FJsonValue>> R;for(const auto& E:A->*&FAccess::ActiveAnimNotifyState)R.Add(MakeShared<FJsonValueString>(E.NotifyStateClass->GetPathName()));return R;}
};
struct FProxyAccess:FAnimInstanceProxy
{static void Init(FAnimInstanceProxy& P,UAnimInstance* A){(P.*&FProxyAccess::Initialize)(A);}};
TSharedPtr<FJsonObject> Query(UAnimInstance* A,UClass* Type)
{
    auto R=MakeShared<FJsonObject>();R->SetBoolField(TEXT("any"),A->WasAnimNotifyStateActiveInAnyState(Type));
    auto& P=FAccess::Proxy(A);auto* Rule=P.GetMutableNodeFromIndex<FAnimNode_TransitionResult>(54);
    if(!Rule)return {};FAnimationUpdateContext C(&P,1.f/60);Rule->GetEvaluateGraphExposedInputs().Execute(C);
    R->SetBoolField(TEXT("pivotRule"),Rule->bCanEnterTransition);return R;
}
}

FString ULyraNotifyDispatchOracleLibrary::ReadPolicy(const FString& ClassesJson)
{
    using namespace DispatchProbe;TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(ClassesJson),Q))return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Classes,Calls;
    for(const auto& V:Q->GetArrayField(TEXT("classes")))
    {
        auto* C=LoadObject<UClass>(nullptr,*V->AsString());if(!C)return Fail(__LINE__);auto* D=Cast<UAnimInstance>(C->GetDefaultObject());if(!D)return Fail(__LINE__);
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("class"),C->GetPathName());R->SetBoolField(TEXT("receive"),D->bReceiveNotifiesFromLinkedInstances);R->SetBoolField(TEXT("propagate"),D->bPropagateNotifiesToLinkedInstances);
        TArray<TSharedPtr<FJsonValue>> Functions;
        for(const TCHAR* N:{TEXT("AnimNotify_SaveAttack"),TEXT("AnimNotify_ResetCombo")})
        {auto F=MakeShared<FJsonObject>();auto* Function=C->FindFunctionByName(N);F->SetStringField(TEXT("name"),N);F->SetBoolField(TEXT("found"),Function!=nullptr);F->SetNumberField(TEXT("parameters"),Function?Function->NumParms:0);Functions.Add(MakeShared<FJsonValueObject>(F));}
        R->SetArrayField(TEXT("functions"),Functions);Classes.Add(MakeShared<FJsonValueObject>(R));
    }
    auto* B=LoadObject<UBlueprint>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base"));if(!B)return Fail(__LINE__);
    TArray<UEdGraph*> Graphs;B->GetAllGraphs(Graphs);
    for(auto* G:Graphs)for(auto Node:G->Nodes)if(auto* Call=Cast<UK2Node_CallFunction>(Node.Get());Call&&Call->GetFunctionName().ToString().Contains(TEXT("Notify")))
    {
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("graph"),G->GetPathName());R->SetStringField(TEXT("function"),Call->GetFunctionName().ToString());
        if(auto* Edge=Cast<UAnimStateTransitionNode>(G->GetOuter())){R->SetStringField(TEXT("previous"),Edge->GetPreviousState()->GetStateName());R->SetStringField(TEXT("next"),Edge->GetNextState()->GetStateName());}
        TArray<TSharedPtr<FJsonValue>> Pins;for(auto* Pin:Call->Pins){auto P=MakeShared<FJsonObject>();P->SetStringField(TEXT("name"),Pin->PinName.ToString());P->SetStringField(TEXT("default"),Pin->DefaultValue);P->SetStringField(TEXT("object"),Pin->DefaultObject?Pin->DefaultObject->GetPathName():TEXT(""));Pins.Add(MakeShared<FJsonValueObject>(P));}
        R->SetArrayField(TEXT("pins"),Pins);Calls.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("classes"),Classes);R->SetArrayField(TEXT("notifyCalls"),Calls);
    auto* MC=LoadObject<UClass>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C"));
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>());
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny"));if(!Mesh)return Fail(__LINE__);Component->SetSkeletalMeshAsset(Mesh);
    TStrongObjectPtr<UAnimInstance> A(NewObject<UAnimInstance>(Component.Get(),MC));auto& P=FAccess::Proxy(A.Get());FProxyAccess::Init(P,A.Get());
    auto* Interface=IAnimClassInterface::GetFromClass(MC);FAnimNode_StateMachine* Machine=nullptr;
    for(auto* Property:Interface->GetAnimNodeProperties())if(Property->Struct==FAnimNode_StateMachine::StaticStruct())Machine=Property->ContainerPtrToValuePtr<FAnimNode_StateMachine>(A.Get());
    if(!Machine)return Fail(__LINE__);
    R->SetNumberField(TEXT("mainMachineContextIndex"),UE::Anim::FActiveStateMachineScope::GetStateMachineIndex(Machine,FAnimationUpdateContext(&P,0)));
    R->SetBoolField(TEXT("mainMachineNotifyMetadata"),Machine->bCreateNotifyMetaData);return Write(R);
}

FString ULyraNotifyDispatchOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    using namespace DispatchProbe;TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* MC=LoadObject<UClass>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C"));
    auto* Type=LoadObject<UClass>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Animations/AnimNotifies/TransitionToLocomotion.TransitionToLocomotion_C"));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny"));if(!MC||!Type||!Mesh)return Fail(__LINE__);
    TMap<FString,TStrongObjectPtr<UAnimSequenceBase>> Assets;TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Q->GetArrayField(TEXT("traces")))
    {
        auto T=TV->AsObject();auto World=NewObject<UWorld>();World->WorldType=EWorldType::GamePreview;
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(World));Component->SetSkeletalMeshAsset(Mesh);
        TStrongObjectPtr<UAnimInstance> A(NewObject<UAnimInstance>(Component.Get(),MC));auto& P=FAccess::Proxy(A.Get());FProxyAccess::Init(P,A.Get());
        auto* Observer=NewObject<ULyraNamedNotifyObserver>(A.Get());A->AddExternalNotifyHandler(Observer,TEXT("AnimNotify_SaveAttack"));A->AddExternalNotifyHandler(Observer,TEXT("AnimNotify_ResetCombo"));
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark Mark(FMemStack::Get());auto F=FV->AsObject();float D=F->GetNumberField(TEXT("delta"));auto R=MakeShared<FJsonObject>();
            A->ClearQueuedAnimEvents(F->GetBoolField(TEXT("replaceHistory")));auto Before=Query(A.Get(),Type);if(!Before)return Fail(__LINE__);R->SetObjectField(TEXT("before"),Before);
            if(F->GetBoolField(TEXT("removeHandlers"))){A->RemoveExternalNotifyHandler(Observer,TEXT("AnimNotify_SaveAttack"));A->RemoveExternalNotifyHandler(Observer,TEXT("AnimNotify_ResetCombo"));}
            for(const auto& WV:F->GetArrayField(TEXT("windows")))
            {
                auto W=WV->AsObject();FString Path=W->GetStringField(TEXT("asset"));if(!Assets.Contains(Path))Assets.Add(Path,TStrongObjectPtr<UAnimSequenceBase>(LoadObject<UAnimSequenceBase>(nullptr,*Path)));
                auto* Asset=Assets[Path].Get();if(!Asset)return Fail(__LINE__);float Time=W->GetNumberField(TEXT("current"));FAnimTickRecord Tick;
                Tick.TimeAccumulator=&Time;Tick.bLooping=W->GetBoolField(TEXT("looping"));Tick.bActiveContext=W->GetBoolField(TEXT("active"));FAnimNotifyContext Context(Tick);
                Asset->GetAnimNotifies(W->GetNumberField(TEXT("previous")),W->GetNumberField(TEXT("delta")),Context);
                if(W->HasField(TEXT("states")))for(auto& Ref:Context.ActiveNotifies)
                {
                    FEncounteredStateMachineStack Stack;
                    for(const auto& SV:W->GetArrayField(TEXT("states"))){auto S=SV->AsArray();Stack.StateStack.Emplace(S[0]->AsNumber(),S[1]->AsNumber());}
                    Ref.AddContextData<UE::Anim::FAnimNotifyStateMachineContext>(Stack);
                }
                A->NotifyQueue.AddAnimNotifies(W->GetBoolField(TEXT("leader")),Context.ActiveNotifies,W->GetNumberField(TEXT("weight")));
            }
            auto Raw=A->NotifyQueue.AnimNotifies;
            // Dispatch the original empty Transition state and named events only;
            // other object consumers already have separate native references.
            A->NotifyQueue.AnimNotifies.RemoveAll([Type](const FAnimNotifyEventReference& E){const auto* N=E.GetNotify();return N->Notify||N->NotifyStateClass&&!N->NotifyStateClass->IsA(Type);});
            Observer->Events.Reset();FAccess::Trigger(A.Get(),D);R->SetArrayField(TEXT("states"),FAccess::States(A.Get()));
            TArray<TSharedPtr<FJsonValue>> Events;for(auto Name:Observer->Events)Events.Add(MakeShared<FJsonValueString>(Name.ToString()));R->SetArrayField(TEXT("named"),Events);
            A->NotifyQueue.AnimNotifies=Raw;R->SetObjectField(TEXT("after"),Query(A.Get(),Type));Frames.Add(MakeShared<FJsonValueObject>(R));
        }
        auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);return Write(R);
}

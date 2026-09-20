#include "AlsTransitionMathCommandlet.h"

#include "AlphaBlend.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "AnimNodes/AnimNode_BlendListBase.h"
#include "Components/SkeletalMeshComponent.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Dom/JsonObject.h"
#include "EdGraph/EdGraph.h"
#include "Engine/Blueprint.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "UObject/UnrealType.h"

namespace
{
// Injected entries isolate the engine weight function. This is not a recorded AnimBP run.
struct FBlendChildProbe : FAnimNode_Base
{
    float Weight = 0;
    bool Active = false;
    int32 Updates = 0;
    virtual void Update_AnyThread(const FAnimationUpdateContext& Context) override
    { Weight = Context.GetFinalBlendWeight(); Active = Context.IsActive(); ++Updates; }
};

struct FBlendListProbe : FAnimNode_BlendListBase
{
    int32 Child = 0;
    virtual int32 GetActiveChildIndex() override { return Child; }
    void Setup(FBlendChildProbe& A, FBlendChildProbe& B)
    {
        AddPose(); AddPose();
        const_cast<TArray<float>&>(GetBlendTimes()) = {.3f, .2f};
        auto Property = FindFProperty<FProperty>(StaticStruct(), TEXT("BlendType"));
        check(Property && Property->ImportText_Direct(TEXT("Cubic"), Property->ContainerPtrToValuePtr<void>(this), nullptr, PPF_None));
        BlendPose[0].SetLinkNode(&A); BlendPose[1].SetLinkNode(&B);
    }
    float Weight(int32 Index) const { return PerBlendData[Index].Weight; }
};

int32 ObserveBlendList(const FString& Path)
{
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE FAnimNode_BlendListBase Initialize/Update, two children, Cubic .3/.2, Default child update"));
    TArray<TSharedPtr<FJsonValue>> Cases;
    for (const int32 Hz : {30, 60, 120})
    for (int32 Scenario = 0; Scenario < 4; ++Scenario)
    {
        auto Component = NewObject<USkeletalMeshComponent>();
        auto Instance = NewObject<UAnimInstance>(Component);
        FAnimInstanceProxy Proxy(Instance);
        FBlendChildProbe A, B;
        FBlendListProbe Node; Node.Setup(A, B);
        Node.Initialize_AnyThread(FAnimationInitializeContext(&Proxy));
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 0; Frame < Hz * 3; ++Frame)
        {
            Node.Child = Scenario == 0 ? (Frame >= Hz / 2 && Frame < Hz * 2 ? 1 : 0) :
                Scenario == 1 ? (Frame / FMath::Max(1, Hz / 12)) % 2 :
                Scenario == 2 ? (Frame < Hz ? 1 : 0) : (Frame / 3) % 2;
            const bool Reset = Scenario == 2 && Frame == Hz * 2;
            if (Reset) Node.Initialize_AnyThread(FAnimationInitializeContext(&Proxy));
            const float Delta = Scenario == 3 ? (Frame % 11 == 0 ? 0.f : Frame % 17 == 0 ? .5f : 1.f / Hz) : 1.f / Hz;
            A = FBlendChildProbe(); B = FBlendChildProbe();
            FAnimationUpdateSharedContext Shared;
            Node.Update_AnyThread(FAnimationUpdateContext(&Proxy, Delta, &Shared));
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("child"), Node.Child); Row->SetBoolField(TEXT("reset"), Reset);
            Row->SetNumberField(TEXT("delta"), Delta);
            Row->SetNumberField(TEXT("a"), Node.Weight(0)); Row->SetNumberField(TEXT("b"), Node.Weight(1));
            Row->SetNumberField(TEXT("aUpdateWeight"), A.Weight); Row->SetNumberField(TEXT("bUpdateWeight"), B.Weight);
            Row->SetNumberField(TEXT("aUpdates"), A.Updates); Row->SetNumberField(TEXT("bUpdates"), B.Updates);
            Row->SetBoolField(TEXT("aActive"), A.Active); Row->SetBoolField(TEXT("bActive"), B.Active);
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Case = MakeShared<FJsonObject>();
        Case->SetNumberField(TEXT("hz"), Hz); Case->SetNumberField(TEXT("scenario"), Scenario);
        Case->SetArrayField(TEXT("frames"), Frames); Cases.Add(MakeShared<FJsonValueObject>(Case));
    }
    Root->SetArrayField(TEXT("cases"), Cases);
    FString Text;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Text)) || !FFileHelper::SaveStringToFile(Text, *Path)) return 8;
    UE_LOG(LogTemp, Display, TEXT("ALS_BLEND_LIST_OK cases=%d frames=2520 assets_saved=0"), Cases.Num());
    return 0;
}

struct FWeightProbe : FAnimNode_StateMachine
{
    FWeightProbe() { CurrentState = 0; }
    void Add(int32 From, int32 To, float Alpha)
    {
        const int32 Index = ActiveTransitionArray.AddUninitialized();
        auto& Entry = ActiveTransitionArray[Index];
        FAnimationActiveTransitionEntry::StaticStruct()->InitializeStruct(&Entry);
        Entry.PreviousState = From;
        Entry.NextState = To;
        Entry.Alpha = Alpha;
        Entry.bActive = true;
        CurrentState = To;
    }
};
}

UAlsTransitionMathCommandlet::UAlsTransitionMathCommandlet()
{
    IsClient = false;
    IsServer = false;
    IsEditor = true;
    LogToConsole = true;
}

int32 UAlsTransitionMathCommandlet::Main(const FString& Params)
{
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    if (FParse::Param(*Params, TEXT("BlendList"))) return ObserveBlendList(OutputPath);
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE FAlphaBlend::AlphaToBlendOption + FAnimNode_StateMachine::GetStateWeight; injected stacks, not AnimBP traces"));
    FString SettingsPath;
    if (!FParse::Value(*Params, TEXT("Settings="), SettingsPath)) return 3;
    const UBlueprint* Blueprint = LoadObject<UBlueprint>(nullptr,
        TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"));
    if (!Blueprint) return 4;
    TArray<UEdGraph*> Graphs;
    Blueprint->GetAllGraphs(Graphs);
    TSharedPtr<FJsonObject> Settings;
    for (const UEdGraph* Graph : Graphs)
    {
        if (!Graph->GetPathName().EndsWith(TEXT(":(N) CycleBlending.AnimGraphNode_StateMachine_1.(N) Directional States"))) continue;
        const UObject* Owner = Graph->GetOuter();
        const auto* Property = FindFProperty<FStructProperty>(Owner->GetClass(), TEXT("Node"));
        if (Settings || !Property || Property->Struct != FAnimNode_StateMachine::StaticStruct()) return 5;
        const auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Owner);
        Settings = MakeShared<FJsonObject>();
        Settings->SetNumberField(TEXT("schemaVersion"), 1);
        Settings->SetStringField(TEXT("sourceGraph"), Graph->GetPathName());
        Settings->SetNumberField(TEXT("maxTransitionsPerFrame"), Node->MaxTransitionsPerFrame);
        Settings->SetBoolField(TEXT("skipFirstUpdateTransition"), Node->bSkipFirstUpdateTransition);
        Settings->SetBoolField(TEXT("reinitializeOnBecomingRelevant"), Node->bReinitializeOnBecomingRelevant);
    }
    if (!Settings) return 6;
    Root->SetObjectField(TEXT("directionMachine"), Settings);
    TArray<TSharedPtr<FJsonValue>> Alphas;
    const EAlphaBlendOption Modes[] = { EAlphaBlendOption::Linear, EAlphaBlendOption::Cubic, EAlphaBlendOption::HermiteCubic };
    const TCHAR* Names[] = { TEXT("Linear"), TEXT("Cubic"), TEXT("HermiteCubic") };
    for (int32 Mode = 0; Mode < 3; ++Mode)
    for (int32 Index = -10; Index <= 110; ++Index)
    {
        const float Progress = Index / 100.f;
        const auto Row = MakeShared<FJsonObject>();
        Row->SetStringField(TEXT("mode"), Names[Mode]);
        Row->SetNumberField(TEXT("progress"), Progress);
        Row->SetNumberField(TEXT("alpha"), FAlphaBlend::AlphaToBlendOption(Progress, Modes[Mode]));
        Alphas.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("alphas"), Alphas);
    TArray<TSharedPtr<FJsonValue>> Stacks;
    for (int32 Case = 0; Case < 65; ++Case)
    {
        FWeightProbe Machine;
        TArray<TSharedPtr<FJsonValue>> Entries;
        int32 Current = 0;
        for (int32 Index = 0; Index < Case; ++Index)
        {
            const int32 Next = (Current + 1 + Index % 5) % 6;
            const float Alpha = ((Case * 17 + Index * 13) % 101) / 100.f;
            const auto Entry = MakeShared<FJsonObject>();
            Entry->SetNumberField(TEXT("from"), Current);
            Entry->SetNumberField(TEXT("to"), Next);
            Entry->SetNumberField(TEXT("alpha"), Alpha);
            Entries.Add(MakeShared<FJsonValueObject>(Entry));
            Machine.Add(Current, Next, Alpha);
            Current = Next;
        }
        TArray<TSharedPtr<FJsonValue>> Weights;
        for (int32 State = 0; State < 6; ++State)
            Weights.Add(MakeShared<FJsonValueNumber>(Machine.GetStateWeight(State)));
        const auto Row = MakeShared<FJsonObject>();
        Row->SetArrayField(TEXT("entries"), Entries);
        Row->SetArrayField(TEXT("weights"), Weights);
        Stacks.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("stacks"), Stacks);
    FString Text;
    const auto Writer = TJsonWriterFactory<>::Create(&Text);
    if (!FJsonSerializer::Serialize(Root, Writer) || !FFileHelper::SaveStringToFile(Text, *OutputPath)) return 2;
    FString SettingsText;
    const auto SettingsWriter = TJsonWriterFactory<>::Create(&SettingsText);
    if (!FJsonSerializer::Serialize(Settings.ToSharedRef(), SettingsWriter) ||
        !FFileHelper::SaveStringToFile(SettingsText, *SettingsPath)) return 7;
    UE_LOG(LogTemp, Display, TEXT("ALS_TRANSITION_MATH_OK alpha_rows=%d stacks=%d assets_saved=0"), Alphas.Num(), Stacks.Num());
    return 0;
}

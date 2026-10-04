#include "AlsLyraMainCacheLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AnimNode_UseCachedPose.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

// UE's public header does not export this message's name symbol. The external
// diagnostic module uses the exact definition from AnimNode_SaveCachedPose.cpp;
// both resolve to the same interned FName. No Engine file or production node is
// replaced; the real SaveCachedPose dispatches this real handler type.
IMPLEMENT_ANIMGRAPH_MESSAGE(UE::Anim::FCachedPoseSkippedUpdateHandler);

namespace LyraMainCacheProbe
{
template<class T>T* Node(const IAnimClassInterface* I,UAnimInstance* A,int32 Index)
{const auto& Props=I->GetAnimNodeProperties();auto* P=Props[Props.Num()-1-Index];return P->Struct==T::StaticStruct()?P->ContainerPtrToValuePtr<T>(A):nullptr;}
struct FCacheDriver:FAnimNode_Base
{
    int32 Stage=0;FPoseLink First,Second;TSharedPtr<FJsonObject> Frame;
    TArray<TSharedPtr<FJsonValue>>* Rows=nullptr;int32* LastStage=nullptr;
    void Update_AnyThread(const FAnimationUpdateContext& C) override
    {
        *LastStage=Stage;auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("cache"),Stage);
        R->SetNumberField(TEXT("weight"),C.GetFinalBlendWeight());R->SetNumberField(TEXT("rootMotionWeight"),C.GetRootMotionWeightModifier());
        R->SetBoolField(TEXT("active"),C.IsActive());R->SetBoolField(TEXT("shared"),C.GetSharedContext()!=nullptr);Rows->Add(MakeShared<FJsonValueObject>(R));
        if(Stage==181){First.Update(C);return;}
        if(Stage==78)
        {
            // These are already-resolved source contexts at the original
            // PreAim and UpperBody Slot boundaries, not Montage simulation.
            float Pre=Frame->GetNumberField(TEXT("preAimSource"));
            if(Pre>ZERO_ANIMWEIGHT_THRESH)
            {
                auto S=C.FractionalWeight(Pre);if(Frame->GetBoolField(TEXT("preAimInactive")))S=S.AsInactive();
                float Upper=Frame->GetNumberField(TEXT("upperSource"));
                if(Upper>ZERO_ANIMWEIGHT_THRESH){auto U=S.FractionalWeightAndRootMotion(Upper,0);if(Frame->GetBoolField(TEXT("upperInactive")))U=U.AsInactive();First.Update(U);}
                Second.Update(S);
            }
        }
    }
};
TArray<int32> Order(const IAnimClassInterface* I,FName Name)
{const auto* O=I->GetOrderedSavedPoseNodeIndicesMap().Find(Name);return O?O->OrderedSavedPoseNodeIndices:TArray<int32>();}
TArray<TSharedPtr<FJsonValue>> Numbers(const TArray<int32>& Values)
{TArray<TSharedPtr<FJsonValue>> R;for(auto V:Values)R.Add(MakeShared<FJsonValueNumber>(V));return R;}
}
FString UAlsLyraMainCacheLibrary::ReadTrace(UClass* MainClass,const FString& RequestsJson)
{
    using namespace LyraMainCacheProbe;TSharedPtr<FJsonObject> Requests;
    const auto* MI=MainClass?IAnimClassInterface::GetFromClass(MainClass):nullptr;
    if(!MI||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Requests))return {};
    const auto MainOrder=Order(MI,TEXT("AnimGraph"));if(MainOrder!=TArray<int32>{78,83})return {};
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Requests->GetArrayField(TEXT("traces")))
    {
        auto T=TV->AsObject();auto* PC=LoadObject<UClass>(nullptr,*T->GetStringField(TEXT("class")));
        const auto* LayerInterface=PC?IAnimClassInterface::GetFromClass(PC):nullptr;if(!LayerInterface)return {};
        const auto ProviderOrder=Order(LayerInterface,TEXT("FullBody_Aiming"));if(ProviderOrder!=TArray<int32>{78})return {};
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(GetTransientPackage(),NAME_None,RF_Transient));
        TStrongObjectPtr<UAnimInstance> Main(NewObject<UAnimInstance>(Component.Get(),MainClass,NAME_None,RF_Transient));
        TStrongObjectPtr<UAnimInstance> Provider(NewObject<UAnimInstance>(Component.Get(),PC,NAME_None,RF_Transient));
        FAnimInstanceProxy Proxy(Main.Get());
        auto* Split=Node<FAnimNode_SaveCachedPose>(MI,Main.Get(),78);auto* Loc=Node<FAnimNode_SaveCachedPose>(MI,Main.Get(),83);
        auto* Input=Node<FAnimNode_SaveCachedPose>(LayerInterface,Provider.Get(),78);
        auto* P0=Node<FAnimNode_UseCachedPose>(LayerInterface,Provider.Get(),76);auto* P1=Node<FAnimNode_UseCachedPose>(LayerInterface,Provider.Get(),75);
        auto* M0=Node<FAnimNode_UseCachedPose>(MI,Main.Get(),77);auto* Upper=Node<FAnimNode_UseCachedPose>(MI,Main.Get(),82);auto* Base=Node<FAnimNode_UseCachedPose>(MI,Main.Get(),80);
        if(!Split||!Loc||!Input||!P0||!P1||!M0||!Upper||!Base)return {};
        P0->LinkToCachingNode.SetLinkNode(Input);P1->LinkToCachingNode.SetLinkNode(Input);M0->LinkToCachingNode.SetLinkNode(Split);
        Upper->LinkToCachingNode.SetLinkNode(Loc);Base->LinkToCachingNode.SetLinkNode(Loc);
        TArray<TSharedPtr<FJsonValue>> Updates,Skipped,Frames;int32 LastStage=-1;
        FCacheDriver Drivers[3];int32 Ids[]={181,78,83};
        for(int32 I=0;I<3;++I){Drivers[I].Stage=Ids[I];Drivers[I].Rows=&Updates;Drivers[I].LastStage=&LastStage;}
        Drivers[0].First.SetLinkNode(M0);Drivers[1].First.SetLinkNode(Upper);Drivers[1].Second.SetLinkNode(Base);
        Input->Pose.SetLinkNode(&Drivers[0]);Split->Pose.SetLinkNode(&Drivers[1]);Loc->Pose.SetLinkNode(&Drivers[2]);
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            auto F=FV->AsObject();Updates.Reset();Skipped.Reset();LastStage=-1;Drivers[1].Frame=F;
            if(F->GetBoolField(TEXT("visited")))for(const auto& RV:F->GetArrayField(TEXT("readers")))
            {
                auto R=RV->AsObject();FAnimationUpdateSharedContext Shared;
                FAnimationUpdateContext C(&Proxy,static_cast<float>(F->GetNumberField(TEXT("delta"))),R->GetBoolField(TEXT("shared"))?&Shared:nullptr);
                C=C.FractionalWeightAndRootMotion(static_cast<float>(R->GetNumberField(TEXT("weight"))),static_cast<float>(R->GetNumberField(TEXT("rootMotionWeight"))));
                if(!R->GetBoolField(TEXT("active")))C=C.AsInactive();
                UE::Anim::TOptionalScopedGraphMessage<UE::Anim::FCachedPoseSkippedUpdateHandler> Handler(R->GetBoolField(TEXT("shared")),C,
                    [&Skipped,&LastStage](TArrayView<const UE::Anim::FMessageStack> S){auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("cache"),LastStage);Row->SetNumberField(TEXT("count"),S.Num());Skipped.Add(MakeShared<FJsonValueObject>(Row));});
                (R->GetNumberField(TEXT("reader"))==76?P0:P1)->Update_AnyThread(C);
            }
            Input->PostGraphUpdate();Split->PostGraphUpdate();Loc->PostGraphUpdate();
            auto Row=MakeShared<FJsonObject>();Row->SetArrayField(TEXT("updates"),Updates);Row->SetArrayField(TEXT("skipped"),Skipped);
            Row->SetArrayField(TEXT("globalWeights"),{MakeShared<FJsonValueNumber>(Input->GlobalWeight),MakeShared<FJsonValueNumber>(Split->GlobalWeight),MakeShared<FJsonValueNumber>(Loc->GlobalWeight)});
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));Trace->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));
        Trace->SetArrayField(TEXT("mainOrder"),Numbers(MainOrder));Trace->SetArrayField(TEXT("providerOrder"),Numbers(ProviderOrder));Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}

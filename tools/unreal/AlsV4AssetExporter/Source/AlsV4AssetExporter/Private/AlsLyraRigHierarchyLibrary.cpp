#include "AlsLyraRigHierarchyLibrary.h"
#include "ControlRig.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraRigHierarchyProbe
{
TSharedPtr<FJsonValue> Vector(const FVector& V)
{ return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{MakeShared<FJsonValueNumber>(V.X),MakeShared<FJsonValueNumber>(V.Y),MakeShared<FJsonValueNumber>(V.Z)}); }
TSharedPtr<FJsonObject> Transform(const FTransform& T)
{
    check(!T.ContainsNaN()); auto O=MakeShared<FJsonObject>();O->SetField(TEXT("p"),Vector(T.GetTranslation()));O->SetField(TEXT("s"),Vector(T.GetScale3D()));
    const FQuat Q=T.GetRotation();O->SetArrayField(TEXT("q"),{MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)});return O;
}
TSharedPtr<FJsonObject> Snapshot(URigHierarchy* H)
{
    auto O=MakeShared<FJsonObject>();
    for(const auto& K:H->GetAllKeys())
    {
        if(K.Type!=ERigElementType::Bone&&K.Type!=ERigElementType::Control)continue;
        auto* E=H->Find<FRigTransformElement>(K);check(E);auto V=MakeShared<FJsonObject>();
        for(const auto& Pair:TArray<TPair<FString,ERigTransformType::Type>>{{TEXT("local"),ERigTransformType::CurrentLocal},{TEXT("global"),ERigTransformType::CurrentGlobal},{TEXT("initialLocal"),ERigTransformType::InitialLocal},{TEXT("initialGlobal"),ERigTransformType::InitialGlobal}})
            V->SetObjectField(Pair.Key,Transform(H->GetTransform(E,Pair.Value)));
        if(auto* C=Cast<FRigControlElement>(E))
            for(const auto& Pair:TArray<TPair<FString,ERigTransformType::Type>>{{TEXT("offsetLocal"),ERigTransformType::CurrentLocal},{TEXT("offsetGlobal"),ERigTransformType::CurrentGlobal},{TEXT("initialOffsetLocal"),ERigTransformType::InitialLocal},{TEXT("initialOffsetGlobal"),ERigTransformType::InitialGlobal}})
                V->SetObjectField(Pair.Key,Transform(H->GetControlOffsetTransform(C,Pair.Value)));
        O->SetObjectField(K.Name.ToString(),V);
    }
    return O;
}
FVector ReadVector(const TSharedPtr<FJsonObject>& O,const TCHAR* Name)
{const auto& V=O->GetArrayField(Name);check(V.Num()==3);return FVector(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber());}
FTransform ReadTransform(const TSharedPtr<FJsonObject>& O)
{const auto& Q=O->GetArrayField(TEXT("q"));check(Q.Num()==4);return FTransform(FQuat(Q[0]->AsNumber(),Q[1]->AsNumber(),Q[2]->AsNumber(),Q[3]->AsNumber()),ReadVector(O,TEXT("p")),ReadVector(O,TEXT("s")));}
ERigTransformType::Type Type(const TSharedPtr<FJsonObject>& O)
{
    const bool I=O->GetBoolField(TEXT("initial")),L=O->GetBoolField(TEXT("local"));
    return I?(L?ERigTransformType::InitialLocal:ERigTransformType::InitialGlobal):(L?ERigTransformType::CurrentLocal:ERigTransformType::CurrentGlobal);
}
}

FString UAlsLyraRigHierarchyLibrary::ReadHierarchy(UClass* RigClass,const FString& RequestsJson)
{
    using namespace LyraRigHierarchyProbe;TSharedPtr<FJsonObject> Requests;
    if(!RigClass||!RigClass->IsChildOf(UControlRig::StaticClass())||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Requests))return {};
    auto Result=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Requests->GetArrayField(TEXT("traces")))
    {
        TStrongObjectPtr<UControlRig> Rig(NewObject<UControlRig>(GetTransientPackage(),RigClass,NAME_None,RF_Transient));Rig->Initialize();auto* H=Rig->GetHierarchy();check(H);
        auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("name"),TV->AsObject()->GetStringField(TEXT("name")));Trace->SetObjectField(TEXT("initial"),Snapshot(H));
        auto Layout=MakeShared<FJsonObject>();
        for(const auto& K:H->GetAllKeys())
        {
            if(K.Type!=ERigElementType::Bone&&K.Type!=ERigElementType::Control)continue;
            auto* E=H->Find<FRigTransformElement>(K);auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("type"),K.Type==ERigElementType::Control?TEXT("Control"):TEXT("Bone"));
            TArray<TSharedPtr<FJsonValue>> Parents;for(const auto* P:H->GetParents(E))Parents.Add(MakeShared<FJsonValueString>(P->GetName()));V->SetArrayField(TEXT("parents"),Parents);
            if(auto* C=Cast<FRigControlElement>(E))
            {
                TArray<TSharedPtr<FJsonValue>> Weights;
                for(const auto& P:C->ParentConstraints)
                {auto W=MakeShared<FJsonObject>();W->SetField(TEXT("current"),Vector(FVector(P.Weight.Location,P.Weight.Rotation,P.Weight.Scale)));W->SetField(TEXT("initial"),Vector(FVector(P.InitialWeight.Location,P.InitialWeight.Rotation,P.InitialWeight.Scale)));Weights.Add(MakeShared<FJsonValueObject>(W));}
                V->SetArrayField(TEXT("weights"),Weights);bool Limit=false;for(const auto& L:C->Settings.LimitEnabled)Limit|=L.bMinimum||L.bMaximum;
                V->SetBoolField(TEXT("limitsEnabled"),Limit);V->SetBoolField(TEXT("animationChannel"),C->IsAnimationChannel());
            }
            Layout->SetObjectField(K.Name.ToString(),V);
        }
        Trace->SetObjectField(TEXT("layout"),Layout);TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& BV:TV->AsObject()->GetArrayField(TEXT("batches")))
        {
            TArray<TSharedPtr<FJsonValue>> Reads;
            for(const auto& OV:BV->AsObject()->GetArrayField(TEXT("operations")))
            {
                const auto O=OV->AsObject();const auto Action=O->GetStringField(TEXT("action"));
                if(Action==TEXT("reset")){H->ResetPoseToInitial(ERigElementType::All);continue;}
                FRigElementKey K(*O->GetStringField(TEXT("name")),O->GetBoolField(TEXT("control"))?ERigElementType::Control:ERigElementType::Bone);
                auto* E=H->Find<FRigTransformElement>(K);check(E);auto* C=Cast<FRigControlElement>(E);const auto T=Type(O);
                if(Action==TEXT("read"))Reads.Add(MakeShared<FJsonValueObject>(Transform(O->GetBoolField(TEXT("offset"))?H->GetControlOffsetTransform(C,T):H->GetTransform(E,T))));
                else if(Action==TEXT("offset")){check(C);H->SetControlOffsetTransform(C,ReadTransform(O->GetObjectField(TEXT("transform"))),T,O->GetBoolField(TEXT("children")),false,O->GetBoolField(TEXT("force")));}
                else {check(Action==TEXT("pose"));H->SetTransform(E,ReadTransform(O->GetObjectField(TEXT("transform"))),T,O->GetBoolField(TEXT("children")),false,O->GetBoolField(TEXT("force")));}
            }
            auto Row=MakeShared<FJsonObject>();Row->SetArrayField(TEXT("reads"),Reads);Row->SetObjectField(TEXT("hierarchy"),Snapshot(H));Rows.Add(MakeShared<FJsonValueObject>(Row));
        }
        Trace->SetArrayField(TEXT("batches"),Rows);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    Result->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}

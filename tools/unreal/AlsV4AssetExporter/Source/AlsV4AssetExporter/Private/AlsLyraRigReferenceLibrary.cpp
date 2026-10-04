#include "AlsLyraRigReferenceLibrary.h"
#include "Animation/Skeleton.h"
#include "BoneContainer.h"
#include "ControlRig.h"
#include "Serialization/JsonSerializer.h"
#include "Tools/ControlRigHierarchyMappings.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace LyraRigReferenceProbe
{
TSharedPtr<FJsonValue> Vector(const FVector& V)
{
    return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{
        MakeShared<FJsonValueNumber>(V.X), MakeShared<FJsonValueNumber>(V.Y), MakeShared<FJsonValueNumber>(V.Z)});
}
TSharedPtr<FJsonObject> Transform(const FTransform& T)
{
    check(!T.ContainsNaN()); auto O=MakeShared<FJsonObject>();
    O->SetField(TEXT("p"),Vector(T.GetTranslation())); O->SetField(TEXT("s"),Vector(T.GetScale3D()));
    const FQuat Q=T.GetRotation(); O->SetArrayField(TEXT("q"),{
        MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),
        MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)}); return O;
}
TSharedPtr<FJsonObject> Snapshot(URigHierarchy* H)
{
    auto O=MakeShared<FJsonObject>();
    for(const auto& K:H->GetAllKeys())
    {
        if(K.Type!=ERigElementType::Bone&&K.Type!=ERigElementType::Control)continue;
        auto* E=H->Find<FRigTransformElement>(K); check(E); auto V=MakeShared<FJsonObject>();
        for(const auto& Pair:TArray<TPair<FString,ERigTransformType::Type>>{
            {TEXT("local"),ERigTransformType::CurrentLocal},{TEXT("global"),ERigTransformType::CurrentGlobal},
            {TEXT("initialLocal"),ERigTransformType::InitialLocal},{TEXT("initialGlobal"),ERigTransformType::InitialGlobal}})
            V->SetObjectField(Pair.Key,Transform(H->GetTransform(E,Pair.Value)));
        if(auto* C=Cast<FRigControlElement>(E))
            for(const auto& Pair:TArray<TPair<FString,ERigTransformType::Type>>{
                {TEXT("offsetLocal"),ERigTransformType::CurrentLocal},{TEXT("offsetGlobal"),ERigTransformType::CurrentGlobal},
                {TEXT("initialOffsetLocal"),ERigTransformType::InitialLocal},{TEXT("initialOffsetGlobal"),ERigTransformType::InitialGlobal}})
                V->SetObjectField(Pair.Key,Transform(H->GetControlOffsetTransform(C,Pair.Value)));
        O->SetObjectField(K.Name.ToString(),V);
    }
    return O;
}
TSharedPtr<FJsonObject> Variables(UControlRig* Rig)
{
    auto O=MakeShared<FJsonObject>();
    for(const TCHAR* Name:{TEXT("LeftFootOffset"),TEXT("RightFootOffset"),TEXT("ThighLength"),TEXT("CalfLength")})
    {
        const auto* P=FindFProperty<FProperty>(Rig->GetClass(),Name); check(P);
        if(const auto* N=CastField<FNumericProperty>(P)) O->SetNumberField(Name,N->GetFloatingPointPropertyValue(P->ContainerPtrToValuePtr<void>(Rig)));
        else {const auto* S=CastField<FStructProperty>(P); check(S&&S->Struct==TBaseStructure<FVector>::Get()); O->SetField(Name,Vector(*S->ContainerPtrToValuePtr<FVector>(Rig)));}
    }
    return O;
}
}

FString UAlsLyraRigReferenceLibrary::ReadReference(UClass* RigClass,USkeleton* Skeleton)
{
    using namespace LyraRigReferenceProbe;
    if(!RigClass||!RigClass->IsChildOf(UControlRig::StaticClass())||!Skeleton)return {};
    FMemMark Mark(FMemStack::Get()); auto Result=MakeShared<FJsonObject>(); TArray<TSharedPtr<FJsonValue>> Profiles;
    const auto& Ref=Skeleton->GetReferenceSkeleton(); TArray<FBoneIndexType> Indices;
    for(int32 I=0;I<Ref.GetNum();++I)Indices.Add(static_cast<FBoneIndexType>(I));
    FBoneContainer Bones; Bones.InitializeTo(Indices,UE::Anim::FCurveFilterSettings(),*Skeleton);
    FCompactPose Pose; Pose.ResetToRefPose(Bones);
    for(bool Target:{false,true})
    {
        TStrongObjectPtr<UControlRig> Rig(NewObject<UControlRig>(GetTransientPackage(),RigClass,NAME_None,RF_Transient));
        Rig->Initialize(); auto* H=Rig->GetHierarchy(); check(H);
        auto Row=MakeShared<FJsonObject>(); Row->SetBoolField(TEXT("targetReference"),Target);
        Row->SetObjectField(TEXT("before"),Snapshot(H));
        TArray<TSharedPtr<FJsonValue>> Mappings;
        H->ForEach<FRigBoneElement>([&](FRigBoneElement* B)->bool
        {
            auto M=MakeShared<FJsonObject>(); M->SetStringField(TEXT("name"),B->GetName());
            M->SetBoolField(TEXT("imported"),B->BoneType==ERigBoneType::Imported);
            M->SetNumberField(TEXT("targetIndex"),Ref.FindBoneIndex(B->GetFName())); Mappings.Add(MakeShared<FJsonValueObject>(M)); return true;
        });
        Row->SetArrayField(TEXT("mapping"),Mappings);
        if(Target)
        {
            // The setter is private; invoke its actual public AnimNode bridge.
            FControlRigHierarchyMappings Mapping;
            Mapping.UpdateControlRigRefPoseIfNeeded(Rig.Get(),Rig.Get(),nullptr,Bones,true,false);
        }
        Row->SetObjectField(TEXT("afterSetter"),Snapshot(H));
        H->ResetPoseToInitial(ERigElementType::All); Row->SetObjectField(TEXT("afterReset"),Snapshot(H));
        if(!Rig->Execute(TEXT("Construction")))return {};
        Row->SetObjectField(TEXT("afterConstruction"),Snapshot(H)); Row->SetObjectField(TEXT("variables"),Variables(Rig.Get()));
        H->ResetPoseToInitial(ERigElementType::All); Rig->RequestConstruction();
        if(!Rig->Execute(TEXT("Construction")))return {};
        Row->SetObjectField(TEXT("afterRepeatedConstruction"),Snapshot(H)); Row->SetObjectField(TEXT("repeatedVariables"),Variables(Rig.Get()));
        Profiles.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("profiles"),Profiles); FString Text;
    FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text)); return Text;
}

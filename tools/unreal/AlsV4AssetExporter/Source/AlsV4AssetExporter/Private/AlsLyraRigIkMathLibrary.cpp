#include "AlsLyraRigIkMathLibrary.h"
#include "Math/ControlRigMathLibrary.h"
#include "Serialization/JsonSerializer.h"

namespace LyraRigIkMathProbe
{
FVector Vector(const TSharedPtr<FJsonObject>& O)
{return FVector(O->GetNumberField(TEXT("X")),O->GetNumberField(TEXT("Y")),O->GetNumberField(TEXT("Z")));}
FTransform Transform(const TSharedPtr<FJsonObject>& O)
{
    const auto Q=O->GetObjectField(TEXT("Rotation"));
    return FTransform(FQuat(Q->GetNumberField(TEXT("X")),Q->GetNumberField(TEXT("Y")),Q->GetNumberField(TEXT("Z")),Q->GetNumberField(TEXT("W"))),
        Vector(O->GetObjectField(TEXT("Position"))),Vector(O->GetObjectField(TEXT("Scale"))));
}
TArray<TSharedPtr<FJsonValue>> Vector(const FVector& V)
{return {MakeShared<FJsonValueNumber>(V.X),MakeShared<FJsonValueNumber>(V.Y),MakeShared<FJsonValueNumber>(V.Z)};}
TSharedPtr<FJsonObject> Transform(const FTransform& T)
{
    auto O=MakeShared<FJsonObject>();const auto Q=T.GetRotation();O->SetArrayField(TEXT("p"),Vector(T.GetLocation()));
    O->SetArrayField(TEXT("q"),{MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)});
    O->SetArrayField(TEXT("s"),Vector(T.GetScale3D()));return O;
}
}
FString UAlsLyraRigIkMathLibrary::ReadTrace(const FString& RequestsJson)
{
    using namespace LyraRigIkMathProbe;
    TSharedPtr<FJsonObject> Requests;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Requests))return {};
    TArray<TSharedPtr<FJsonValue>> Results;
    for(const auto& V:Requests->GetArrayField(TEXT("calls")))
    {
        const auto F=V->AsObject();FTransform A=Transform(F->GetObjectField(TEXT("A"))),B=Transform(F->GetObjectField(TEXT("B"))),C=Transform(F->GetObjectField(TEXT("C")));
        FControlRigMathLibrary::SolveBasicTwoBoneIK(A,B,C,Vector(F->GetObjectField(TEXT("Pole"))),Vector(F->GetObjectField(TEXT("Primary"))),
            Vector(F->GetObjectField(TEXT("Secondary"))),1.f,F->GetNumberField(TEXT("LengthA")),F->GetNumberField(TEXT("LengthB")),true,
            F->GetNumberField(TEXT("StretchStart")),F->GetNumberField(TEXT("StretchMaximum")));
        auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("A"),Transform(A));R->SetObjectField(TEXT("B"),Transform(B));R->SetObjectField(TEXT("C"),Transform(C));
        Results.Add(MakeShared<FJsonValueObject>(R));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("calls"),Results);FString Text;
    FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}

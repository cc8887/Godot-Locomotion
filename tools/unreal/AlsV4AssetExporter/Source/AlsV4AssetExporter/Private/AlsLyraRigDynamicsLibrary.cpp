#include "AlsLyraRigDynamicsLibrary.h"
#include "Units/Simulation/RigUnit_SpringInterp.h"
#include "RigVMFunctions/Simulation/RigVMFunction_AlphaInterp.h"
#include "RigVMCore/RigVMExecuteContext.h"
#include "Serialization/JsonSerializer.h"

namespace LyraRigDynamicsProbe
{
FVector Vector(const TSharedPtr<FJsonValue>& V)
{const auto& A=V->AsArray();check(A.Num()==3);return FVector(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber());}
TArray<TSharedPtr<FJsonValue>> Vector(const FVector& V)
{return {MakeShared<FJsonValueNumber>(V.X),MakeShared<FJsonValueNumber>(V.Y),MakeShared<FJsonValueNumber>(V.Z)};}
TSharedPtr<FJsonObject> State(const FRigUnit_SpringInterpV2& U)
{
    auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("result"),U.Result);O->SetNumberField(TEXT("outputVelocity"),U.Velocity);
    O->SetNumberField(TEXT("simulated"),U.SimulatedResult);O->SetNumberField(TEXT("velocity"),U.SpringState.Velocity);
    O->SetNumberField(TEXT("target"),U.SpringState.PrevTarget);O->SetBoolField(TEXT("valid"),U.SpringState.bPrevTargetValid);return O;
}
TSharedPtr<FJsonObject> State(const FRigUnit_SpringInterpVectorV2& U)
{
    auto O=MakeShared<FJsonObject>();O->SetArrayField(TEXT("result"),Vector(U.Result));O->SetArrayField(TEXT("outputVelocity"),Vector(U.Velocity));
    O->SetArrayField(TEXT("simulated"),Vector(U.SimulatedResult));O->SetArrayField(TEXT("velocity"),Vector(U.SpringState.Velocity));
    O->SetArrayField(TEXT("target"),Vector(U.SpringState.PrevTarget));O->SetBoolField(TEXT("valid"),U.SpringState.bPrevTargetValid);return O;
}
TSharedPtr<FJsonObject> State(const FRigVMFunction_AlphaInterp& U)
{auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("result"),U.Result);O->SetNumberField(TEXT("interpolated"),U.ScaleBiasClamp.InterpolatedResult);O->SetBoolField(TEXT("initialized"),U.ScaleBiasClamp.bInitialized);return O;}
template<class T>void Configure(T& U,const TSharedPtr<FJsonObject>& C)
{
    U.Strength=C->GetNumberField(TEXT("strength"));U.CriticalDamping=C->GetNumberField(TEXT("damping"));
    U.TargetVelocityAmount=C->GetNumberField(TEXT("targetVelocity"));U.bUseCurrentInput=C->GetBoolField(TEXT("useCurrent"));U.bInitializeFromTarget=C->GetBoolField(TEXT("initializeFromTarget"));
}
}

FString UAlsLyraRigDynamicsLibrary::ReadTrace(const FString& RequestsJson)
{
    using namespace LyraRigDynamicsProbe;
    TSharedPtr<FJsonObject> Requests;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Requests))return {};
    const auto& Configs=Requests->GetArrayField(TEXT("configs"));check(Configs.Num()==7);
    auto Result=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Requests->GetArrayField(TEXT("traces")))
    {
        const auto T=TV->AsObject();FRigUnit_SpringInterpVectorV2 V[2];FRigUnit_SpringInterpV2 S[3];FRigVMFunction_AlphaInterp A[2];
        auto ConfigureAll=[&]()
        {
            for(int32 I=0;I<2;++I)Configure(V[I],Configs[I]->AsObject());
            for(int32 I=0;I<3;++I)Configure(S[I],Configs[I+2]->AsObject());
            for(int32 I=0;I<2;++I)
            {
                const auto C=Configs[I+5]->AsObject();A[I].Scale=C->GetNumberField(TEXT("scale"));A[I].Bias=C->GetNumberField(TEXT("bias"));
                A[I].bMapRange=C->GetBoolField(TEXT("map"));A[I].bClampResult=C->GetBoolField(TEXT("clamp"));A[I].bInterpResult=C->GetBoolField(TEXT("interp"));
                A[I].InterpSpeedIncreasing=C->GetNumberField(TEXT("increasing"));A[I].InterpSpeedDecreasing=C->GetNumberField(TEXT("decreasing"));
            }
        };ConfigureAll();
        auto Snapshot=[&]() {TArray<TSharedPtr<FJsonValue>> O;for(const auto& U:V)O.Add(MakeShared<FJsonValueObject>(State(U)));for(const auto& U:S)O.Add(MakeShared<FJsonValueObject>(State(U)));for(const auto& U:A)O.Add(MakeShared<FJsonValueObject>(State(U)));return O;};
        auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("name"),T->GetStringField(TEXT("name")));Trace->SetArrayField(TEXT("initial"),Snapshot());TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            const auto F=FV->AsObject();if(F->GetBoolField(TEXT("reset"))){for(auto& U:V)U=FRigUnit_SpringInterpVectorV2();for(auto& U:S)U=FRigUnit_SpringInterpV2();for(auto& U:A)U=FRigVMFunction_AlphaInterp();ConfigureAll();}
            FRigVMExecuteContext Context;Context.SetDeltaTime(F->GetNumberField(TEXT("delta")));TArray<TSharedPtr<FJsonValue>> Outputs;
            for(const auto& CV:F->GetArrayField(TEXT("calls")))
            {
                const auto C=CV->AsObject();const int32 Id=C->GetIntegerField(TEXT("owner"));check(Id>=0&&Id<7);
                if(Id<2){V[Id].Target=Vector(C->GetField<EJson::Array>(TEXT("target")));V[Id].Current=Vector(C->GetField<EJson::Array>(TEXT("current")));V[Id].Execute(Context);Outputs.Add(MakeShared<FJsonValueObject>(State(V[Id])));}
                else if(Id<5){auto& U=S[Id-2];U.Target=C->GetNumberField(TEXT("target"));U.Current=C->GetNumberField(TEXT("current"));U.Execute(Context);Outputs.Add(MakeShared<FJsonValueObject>(State(U)));}
                else {A[Id-5].Value=C->GetNumberField(TEXT("target"));A[Id-5].Execute(Context);Outputs.Add(MakeShared<FJsonValueObject>(State(A[Id-5])));}
            }
            auto Row=MakeShared<FJsonObject>();Row->SetArrayField(TEXT("calls"),Outputs);Row->SetArrayField(TEXT("after"),Snapshot());Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    Result->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}

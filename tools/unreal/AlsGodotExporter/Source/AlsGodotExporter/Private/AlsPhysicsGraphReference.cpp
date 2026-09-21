#include "AlsPhysicsAssetExport.h"
#include "Algo/Reverse.h"
#include "Chaos/Island/IslandManager.h"
#include "Chaos/PBDJointConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "HAL/FileManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"

bool ExportAlsPhysicsGraphReference(const FString& Output, FString& Error)
{
    using namespace Chaos;
    if (Output.IsEmpty() || FPaths::IsRelative(Output) || IFileManager::Get().FileExists(*Output))
    { Error=TEXT("Graph reference requires a new absolute file."); return false; }
    auto Root=MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native island graph levels/order only; two joint containers represent contact and joint edges; no solver, geometry, integration or asset writes"));
    TArray<TSharedPtr<FJsonValue>> Cases;
    for (int32 Mode=0;Mode<8;++Mode) for (bool Reverse:{false,true})
    {
        FParticleUniqueIndicesMultithreaded Unique; FPBDRigidsSOAs Particles(Unique);
        const auto Bodies=Particles.CreateDynamicParticles(8);
        for (int32 I=0;I<8;++I)
        {
            auto* P=Bodies[I]; P->SetX(FVec3(0)); P->SetR(FRotation3::Identity);
            P->SetP(FVec3(0)); P->SetQ(FRotation3::Identity); P->SetV(FVec3(0)); P->SetW(FVec3(0));
            if (I<2) P->SetObjectStateLowLevel(EObjectStateType::Kinematic);
        }
        FPBDJointConstraints Contacts, Joints; Contacts.SetContainerId(0); Joints.SetContainerId(1);
        Private::FPBDIslandManager Graph(Particles); Graph.SetAssignLevels(true);
        Graph.AddConstraintContainer(Contacts); Graph.AddConstraintContainer(Joints);
        for (auto* P:Bodies) Graph.AddParticle(P);
        TArray<FIntVector> Edges;
        if (Mode==0) Edges={{1,2,3}};
        else if (Mode==1) Edges={{0,0,2}};
        else
        {
            Edges={{1,2,3},{1,3,4},{1,4,5},{1,3,6},{1,6,7}};
            if (Mode!=2) Edges.Add({0,0,5});
            if (Mode>=4) Edges.Add({0,1,7});
            if (Mode>=5) Edges.Add({0,0,5}); // distinct constraints sharing endpoints
            if (Mode>=6) Edges.Add({0,2,7}); // closed loop
            if (Mode==7) Edges.Add({1,4,6});
        }
        if (Reverse) Algo::Reverse(Edges);
        TArray<FPBDJointConstraintHandle*> Handles;
        TArray<TSharedPtr<FJsonValue>> Input;
        for (int32 I=0;I<Edges.Num();++I)
        {
            const auto E=Edges[I]; auto& Container=E.X==0?Contacts:Joints;
            auto* H=Container.AddConstraint({Bodies[E.Y],Bodies[E.Z]},FPBDJointSettings());
            Handles.Add(H); Graph.AddConstraint(E.X,H,H->GetConstrainedParticles());
            auto O=MakeShared<FJsonObject>(); O->SetNumberField(TEXT("id"),I); O->SetNumberField(TEXT("container"),E.X);
            O->SetNumberField(TEXT("body0"),E.Y); O->SetNumberField(TEXT("body1"),E.Z); Input.Add(MakeShared<FJsonValueObject>(O));
        }
        Graph.UpdateIslands();
        auto Row=MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("mode"),Mode); Row->SetBoolField(TEXT("reverse"),Reverse);
        Row->SetArrayField(TEXT("edges"),Input);
        TArray<TSharedPtr<FJsonValue>> Dynamic, Levels, Ordered;
        for (int32 I=0;I<8;++I) { Dynamic.Add(MakeShared<FJsonValueBoolean>(I>=2)); Levels.Add(MakeShared<FJsonValueNumber>(Graph.GetParticleLevel(Bodies[I]))); }
        for (int32 C=0;C<2;++C) Graph.VisitAwakeConstConstraints(C,[&](const Private::FPBDIslandConstraint* E)
        {
            auto O=MakeShared<FJsonObject>(); O->SetNumberField(TEXT("id"),Handles.IndexOfByKey(E->GetConstraint()));
            O->SetNumberField(TEXT("level"),Graph.GetConstraintLevel(E)); O->SetNumberField(TEXT("key"),static_cast<uint32>(E->GetSortKey()));
            Ordered.Add(MakeShared<FJsonValueObject>(O));
        });
        Row->SetArrayField(TEXT("dynamic"),Dynamic); Row->SetArrayField(TEXT("bodyLevels"),Levels); Row->SetArrayField(TEXT("ordered"),Ordered);
        Cases.Add(MakeShared<FJsonValueObject>(Row));
        Graph.Reset();
    }
    Root->SetArrayField(TEXT("cases"),Cases); FString Text;
    if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
    { Error=TEXT("Cannot write graph reference."); return false; }
    return true;
}

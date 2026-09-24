#include "AlsAnimationGraphLibrary.h"
#include "Animation/AnimBlueprint.h"
#include "Animation/AnimBlueprintGeneratedClass.h"
#include "Animation/AnimNodeBase.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "Dom/JsonObject.h"
#include "JsonObjectConverter.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/UnrealType.h"

FString UAlsAnimationGraphLibrary::ReadCompiledAnimationGraph(UAnimBlueprint* Blueprint)
{
    auto* Generated = Blueprint ? Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass) : nullptr;
    if (!Generated) return TEXT("");
    const auto ExportObject = FJsonObjectConverter::CustomExportCallback::CreateLambda(
        [](FProperty* Property, const void* Value) -> TSharedPtr<FJsonValue>
        {
            if (const auto* ObjectProperty = CastField<FObjectPropertyBase>(Property))
            {
                const UObject* Object = ObjectProperty->GetObjectPropertyValue(Value);
                return MakeShared<FJsonValueString>(Object ? Object->GetPathName() : TEXT(""));
            }
            return nullptr;
        });
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), Blueprint->GetPathName());
    Root->SetStringField(TEXT("generatedClass"), Generated->GetPathName());
    const auto& Properties = Generated->GetAnimNodeProperties();
    Root->SetNumberField(TEXT("compiledPropertyCount"), Properties.Num());
    TArray<UEdGraph*> Graphs;
    Blueprint->GetAllGraphs(Graphs);
    Graphs.Sort([](const UEdGraph& A, const UEdGraph& B) { return A.GetPathName() < B.GetPathName(); });
    TArray<TSharedPtr<FJsonValue>> Nodes;
    for (const UEdGraph* Graph : Graphs)
    {
        TArray<UEdGraphNode*> Sorted = Graph->Nodes;
        Sorted.Sort([](const UEdGraphNode& A, const UEdGraphNode& B) { return A.GetPathName() < B.GetPathName(); });
        for (const UEdGraphNode* Node : Sorted)
        {
            const int32* Index = Generated->GetNodePropertyIndexFromGuid(Node->NodeGuid);
            const auto Authored = MakeShared<FJsonObject>();
            // Reflect the runtime struct instead of filtering class names: ALS has custom graph nodes.
            for (TFieldIterator<FStructProperty> Field(Node->GetClass()); Field; ++Field)
                if (Field->Struct->IsChildOf(FAnimNode_Base::StaticStruct()))
                {
                    const auto Value = FJsonObjectConverter::UPropertyToJsonValue(*Field,
                        Field->ContainerPtrToValuePtr<void>(Node), 0, 0, &ExportObject);
                    if (!Value.IsValid()) return TEXT("");
                    Authored->SetField(Field->GetName(), Value);
                }
            if (!Index && Authored->Values.IsEmpty()) continue;
            const auto Item = MakeShared<FJsonObject>();
            Item->SetStringField(TEXT("path"), Node->GetPathName());
            Item->SetStringField(TEXT("graph"), Graph->GetPathName());
            Item->SetStringField(TEXT("class"), Node->GetClass()->GetName());
            Item->SetStringField(TEXT("nodeGuid"), Node->NodeGuid.ToString(EGuidFormats::Digits));
            Item->SetNumberField(TEXT("compiledNodeIndex"), Index ? *Index : INDEX_NONE);
            const int32 PropertyIndex = Generated->GetNodeIndexFromGuid(Node->NodeGuid);
            Item->SetNumberField(TEXT("propertyIndex"), PropertyIndex);
            Item->SetObjectField(TEXT("authoredProperties"), Authored);
            if (Index)
            {
                if (!Properties.IsValidIndex(PropertyIndex)) return TEXT("");
                const auto* Property = Properties[PropertyIndex];
                const auto Runtime = MakeShared<FJsonObject>();
                if (!FJsonObjectConverter::UStructToJsonObject(Property->Struct,
                    Property->ContainerPtrToValuePtr<void>(Generated->GetDefaultObject()), Runtime, 0, 0, &ExportObject)) return TEXT("");
                Item->SetStringField(TEXT("runtimeType"), Property->Struct->GetPathName());
                Item->SetObjectField(TEXT("runtime"), Runtime);
            }
            Nodes.Add(MakeShared<FJsonValueObject>(Item));
        }
    }
    Root->SetArrayField(TEXT("nodes"), Nodes);
    TArray<TSharedPtr<FJsonValue>> Orders;
    const auto& NativeOrders = Generated->GetOrderedSavedPoseNodeIndicesMap();
    TArray<FName> Names;
    NativeOrders.GetKeys(Names);
    Names.Sort(FNameLexicalLess());
    for (const FName Name : Names)
    {
        const auto Order = MakeShared<FJsonObject>();
        Order->SetStringField(TEXT("root"), Name.ToString());
        TArray<TSharedPtr<FJsonValue>> Indices;
        for (const int32 Index : NativeOrders[Name].OrderedSavedPoseNodeIndices)
            Indices.Add(MakeShared<FJsonValueNumber>(Index));
        Order->SetArrayField(TEXT("compiledNodeIndices"), Indices);
        Orders.Add(MakeShared<FJsonValueObject>(Order));
    }
    Root->SetArrayField(TEXT("orderedSavedPoseNodes"), Orders);
    FString Output;
    const auto Writer = TJsonWriterFactory<>::Create(&Output);
    return FJsonSerializer::Serialize(Root, Writer) ? Output : TEXT("");
}

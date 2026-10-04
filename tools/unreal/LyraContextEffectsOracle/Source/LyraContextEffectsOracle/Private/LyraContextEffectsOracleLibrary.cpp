#include "LyraContextEffectsOracleLibrary.h"
#include "Feedback/ContextEffects/LyraContextEffectsLibrary.h"
#include "Feedback/ContextEffects/LyraContextEffectsSubsystem.h"
#include "GameplayTagsManager.h"
#include "GameplayTagsSettings.h"
#include "Misc/ScopeExit.h"
#include "Engine/DataTable.h"
#include "PhysicalMaterials/PhysicalMaterial.h"
#include "NiagaraSystem.h"
#include "Sound/SoundBase.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

FString ULyraContextEffectsOracleLibrary::ReadPolicy()
{
    auto TagSettings=GetMutableDefault<UGameplayTagsSettings>();const auto PreviousTables=TagSettings->GameplayTagTableList;
    ON_SCOPE_EXIT {TagSettings->GameplayTagTableList=PreviousTables;UGameplayTagsManager::Get().EditorRefreshGameplayTagTree();};
    TagSettings->GameplayTagTableList.AddUnique(FSoftObjectPath(TEXT("/Game/ContextEffects/DT_AnimEffectTags.DT_AnimEffectTags")));
    TagSettings->GameplayTagTableList.AddUnique(FSoftObjectPath(TEXT("/Game/ContextEffects/DT_SurfaceTypes.DT_SurfaceTypes")));
    UGameplayTagsManager::Get().EditorRefreshGameplayTagTree();
    TArray<TSharedPtr<FJsonValue>> Tables,Rows,Materials,Settings;
    for(const auto Path:{TEXT("/Game/ContextEffects/DT_AnimEffectTags.DT_AnimEffectTags"),TEXT("/Game/ContextEffects/DT_SurfaceTypes.DT_SurfaceTypes")})
    {
        auto Table=LoadObject<UDataTable>(nullptr,Path);if(!Table)return {};
        TArray<FGameplayTagTableRow*> Tags;Table->GetAllRows(TEXT("Context Effects Oracle"),Tags);
        TArray<TSharedPtr<FJsonValue>> Values;
        for(const auto Tag:Tags)Values.Add(MakeShared<FJsonValueString>(Tag->Tag.ToString()));
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("path"),Table->GetPathName());Row->SetArrayField(TEXT("tags"),Values);Tables.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Library=LoadObject<ULyraContextEffectsLibrary>(nullptr,TEXT("/Game/ContextEffects/CFX_DefaultSkin.CFX_DefaultSkin"));if(!Library)return {};
    Library->LoadEffects();
    for(const auto& Definition:Library->ContextEffects)
    {
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("effect"),Definition.EffectTag.ToString());
        TArray<TSharedPtr<FJsonValue>> Contexts,Effects;
        for(const auto Tag:Definition.Context)Contexts.Add(MakeShared<FJsonValueString>(Tag.ToString()));
        for(const auto& Path:Definition.Effects)
        {
            auto Effect=Path.TryLoad();if(!Effect)return {};
            auto Value=MakeShared<FJsonObject>();Value->SetStringField(TEXT("path"),Effect->GetPathName());Value->SetStringField(TEXT("class"),Effect->GetClass()->GetPathName());
            Value->SetBoolField(TEXT("audio"),Effect->IsA<USoundBase>());Value->SetBoolField(TEXT("vfx"),Effect->IsA<UNiagaraSystem>());Effects.Add(MakeShared<FJsonValueObject>(Value));
        }
        Row->SetArrayField(TEXT("contexts"),Contexts);Row->SetArrayField(TEXT("effects"),Effects);Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    for(const auto Path:{TEXT("/Game/PhysicsMaterials/PM_Character.PM_Character"),TEXT("/Game/PhysicsMaterials/PM_Concrete.PM_Concrete"),TEXT("/Game/PhysicsMaterials/PM_Glass.PM_Glass")})
    {
        auto Material=LoadObject<UPhysicalMaterial>(nullptr,Path);if(!Material)return {};
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("path"),Material->GetPathName());Row->SetNumberField(TEXT("surface"),Material->SurfaceType);Materials.Add(MakeShared<FJsonValueObject>(Row));
    }
    const auto Default=GetDefault<ULyraContextEffectsSettings>();
    for(const auto& Pair:Default->SurfaceTypeToContextMap){auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("surface"),Pair.Key);Row->SetStringField(TEXT("tag"),Pair.Value.ToString());Settings.Add(MakeShared<FJsonValueObject>(Row));}
    TArray<TSharedPtr<FJsonValue>> Queries;TSet<FGameplayTag> EffectTags;
    for(const auto& Row:Library->ContextEffects)EffectTags.Add(Row.EffectTag);
    EffectTags.Add(FGameplayTag());EffectTags.Add(FGameplayTag::RequestGameplayTag(TEXT("AnimEffect.Footstep")));
    TArray<FGameplayTag> Effects=EffectTags.Array();Effects.Sort([](const auto& A,const auto& B){return A.ToString()<B.ToString();});
    const FGameplayTag SurfaceTags[]={FGameplayTag::RequestGameplayTag(TEXT("SurfaceType.Default")),FGameplayTag::RequestGameplayTag(TEXT("SurfaceType.Character")),FGameplayTag::RequestGameplayTag(TEXT("SurfaceType.Concrete")),FGameplayTag::RequestGameplayTag(TEXT("SurfaceType.Glass"))};
    for(const auto Effect:Effects)for(int32 Mask=0;Mask<16;Mask++)
    {
        FGameplayTagContainer Context;TArray<TSharedPtr<FJsonValue>> Tags,Audio,Vfx;
        for(int32 I=0;I<4;I++)if(Mask&(1<<I)){Context.AddTag(SurfaceTags[I]);Tags.Add(MakeShared<FJsonValueString>(SurfaceTags[I].ToString()));}
        TArray<USoundBase*> Sounds;TArray<UNiagaraSystem*> Systems;Library->GetEffects(Effect,Context,Sounds,Systems);
        for(const auto Sound:Sounds)Audio.Add(MakeShared<FJsonValueString>(Sound->GetPathName()));
        for(const auto System:Systems)Vfx.Add(MakeShared<FJsonValueString>(System->GetPathName()));
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("effect"),Effect.ToString());Row->SetArrayField(TEXT("contexts"),Tags);
        Row->SetArrayField(TEXT("audio"),Audio);Row->SetArrayField(TEXT("vfx"),Vfx);Queries.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("tables"),Tables);Result->SetArrayField(TEXT("rows"),Rows);Result->SetArrayField(TEXT("queries"),Queries);
    Result->SetStringField(TEXT("library"),Library->GetPathName());Result->SetArrayField(TEXT("materials"),Materials);Result->SetArrayField(TEXT("settings"),Settings);
    FString Text;auto Writer=TJsonWriterFactory<>::Create(&Text);return FJsonSerializer::Serialize(Result,Writer)?Text:FString();
}

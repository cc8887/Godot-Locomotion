#include "AlsMaterialMetadataReader.h"

#include "Algo/Unique.h"
#include "AlsStableAssetId.h"
#include "Engine/Texture.h"
#include "Engine/Texture2D.h"
#include "Materials/MaterialInstance.h"
#include "Materials/MaterialInterface.h"

namespace
{
    template <typename TParameterValue>
    bool ParameterValueLess(const TParameterValue& Left, const TParameterValue& Right)
    {
        const int32 NameComparison = Left.ParameterInfo.Name.ToString().Compare(
            Right.ParameterInfo.Name.ToString(), ESearchCase::CaseSensitive);
        if (NameComparison != 0)
        {
            return NameComparison < 0;
        }
        if (Left.ParameterInfo.Association != Right.ParameterInfo.Association)
        {
            return static_cast<int32>(Left.ParameterInfo.Association) < static_cast<int32>(Right.ParameterInfo.Association);
        }
        return Left.ParameterInfo.Index < Right.ParameterInfo.Index;
    }

    TSharedRef<FJsonObject> MakeParameterValue(const FMaterialParameterInfo& ParameterInfo)
    {
        const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
        Value->SetStringField(TEXT("name"), ParameterInfo.Name.ToString());
        Value->SetNumberField(TEXT("association"), static_cast<int32>(ParameterInfo.Association));
        Value->SetNumberField(TEXT("index"), ParameterInfo.Index);
        return Value;
    }
}

bool FAlsMaterialMetadataReader::Read(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& OutMetadata, FString& OutError)
{
    UObject* Object = Asset.AssetData.GetAsset();
    if (!Object)
    {
        OutError = FString::Printf(TEXT("Unable to load material asset: %s"), *Asset.AssetData.GetObjectPathString());
        return false;
    }

    if (const UMaterialInterface* Material = Cast<UMaterialInterface>(Object))
    {
        TArray<FString> TexturePaths;
        for (const TObjectPtr<UObject>& ReferencedTexture : Material->GetReferencedTextures())
        {
            if (ReferencedTexture)
            {
                TexturePaths.Add(ReferencedTexture->GetPathName());
            }
        }
        TexturePaths.Sort();
        TexturePaths.SetNum(Algo::Unique(TexturePaths));
        TArray<TSharedPtr<FJsonValue>> Textures;
        for (const FString& TexturePath : TexturePaths)
        {
            const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
            Value->SetStringField(TEXT("id"), TexturePath.StartsWith(TEXT("/Game/")) ? FAlsStableAssetId::Create(TexturePath) : FString());
            Value->SetStringField(TEXT("objectPath"), TexturePath);
            Textures.Add(MakeShared<FJsonValueObject>(Value));
        }
        OutMetadata->SetArrayField(TEXT("referencedTextures"), Textures);

        if (const UMaterialInstance* Instance = Cast<UMaterialInstance>(Material))
        {
            const FString ParentPath = Instance->Parent ? Instance->Parent->GetPathName() : FString();
            OutMetadata->SetStringField(TEXT("parentObjectPath"), ParentPath);
            OutMetadata->SetStringField(TEXT("parentId"), ParentPath.StartsWith(TEXT("/Game/")) ? FAlsStableAssetId::Create(ParentPath) : FString());

            TArray<FScalarParameterValue> ScalarParameters = Instance->ScalarParameterValues;
            ScalarParameters.Sort(ParameterValueLess<FScalarParameterValue>);
            TArray<TSharedPtr<FJsonValue>> ScalarOverrides;
            for (const FScalarParameterValue& Parameter : ScalarParameters)
            {
                const TSharedRef<FJsonObject> Value = MakeParameterValue(Parameter.ParameterInfo);
                Value->SetNumberField(TEXT("value"), Parameter.ParameterValue);
                ScalarOverrides.Add(MakeShared<FJsonValueObject>(Value));
            }
            OutMetadata->SetArrayField(TEXT("scalarParameterOverrides"), ScalarOverrides);

            TArray<FVectorParameterValue> VectorParameters = Instance->VectorParameterValues;
            VectorParameters.Sort(ParameterValueLess<FVectorParameterValue>);
            TArray<TSharedPtr<FJsonValue>> VectorOverrides;
            for (const FVectorParameterValue& Parameter : VectorParameters)
            {
                const TSharedRef<FJsonObject> Value = MakeParameterValue(Parameter.ParameterInfo);
                Value->SetArrayField(TEXT("value"), {
                    MakeShared<FJsonValueNumber>(Parameter.ParameterValue.R),
                    MakeShared<FJsonValueNumber>(Parameter.ParameterValue.G),
                    MakeShared<FJsonValueNumber>(Parameter.ParameterValue.B),
                    MakeShared<FJsonValueNumber>(Parameter.ParameterValue.A),
                });
                VectorOverrides.Add(MakeShared<FJsonValueObject>(Value));
            }
            OutMetadata->SetArrayField(TEXT("vectorParameterOverrides"), VectorOverrides);

            TArray<FTextureParameterValue> TextureParameters = Instance->TextureParameterValues;
            TextureParameters.Sort(ParameterValueLess<FTextureParameterValue>);
            TArray<TSharedPtr<FJsonValue>> TextureOverrides;
            for (const FTextureParameterValue& Parameter : TextureParameters)
            {
                const TSharedRef<FJsonObject> Value = MakeParameterValue(Parameter.ParameterInfo);
                const FString TexturePath = Parameter.ParameterValue ? Parameter.ParameterValue->GetPathName() : FString();
                Value->SetStringField(TEXT("objectPath"), TexturePath);
                Value->SetStringField(TEXT("id"), TexturePath.StartsWith(TEXT("/Game/")) ? FAlsStableAssetId::Create(TexturePath) : FString());
                TextureOverrides.Add(MakeShared<FJsonValueObject>(Value));
            }
            OutMetadata->SetArrayField(TEXT("textureParameterOverrides"), TextureOverrides);
        }
        return true;
    }

    if (const UTexture* Texture = Cast<UTexture>(Object))
    {
        OutMetadata->SetStringField(TEXT("pixelFormatSource"), StaticEnum<ETextureSourceFormat>()->GetNameStringByValue(Texture->Source.GetFormat()));
        if (const UTexture2D* Texture2D = Cast<UTexture2D>(Texture))
        {
            OutMetadata->SetNumberField(TEXT("width"), Texture2D->GetSizeX());
            OutMetadata->SetNumberField(TEXT("height"), Texture2D->GetSizeY());
        }
        return true;
    }

    OutError = FString::Printf(TEXT("Unsupported material asset class: %s"), *Object->GetClass()->GetPathName());
    return false;
}

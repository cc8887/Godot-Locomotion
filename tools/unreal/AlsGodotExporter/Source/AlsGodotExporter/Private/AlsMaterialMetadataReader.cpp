#include "AlsMaterialMetadataReader.h"

#include "Algo/Unique.h"
#include "AlsStableAssetId.h"
#include "Engine/Texture.h"
#include "Engine/Texture2D.h"
#include "Materials/MaterialInstance.h"
#include "Materials/MaterialInterface.h"

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

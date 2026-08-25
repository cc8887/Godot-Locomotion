#include "AlsOutputAuditor.h"

#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"

#include <openssl/sha.h>

bool FAlsOutputAuditor::Audit(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
    const TArray<FString>& NormalizedFbxKeys, TArray<FAlsExportFile>& OutFiles, FString& OutError)
{
    OutFiles.Reset();
    TSet<FString> RelativePaths;
    for (const FAlsExportAsset& Asset : Assets)
    {
        if (!AlsAssetKindIsExportable(Asset.Kind))
        {
            continue;
        }
        if (Asset.OutputPath.IsEmpty() || Asset.OutputPath.Contains(TEXT("\\")) ||
            Asset.OutputPath.Contains(TEXT("..")) || !FPaths::IsRelative(Asset.OutputPath))
        {
            OutError = FString::Printf(TEXT("Unsafe or missing output path for %s: %s"),
                *Asset.AssetData.GetObjectPathString(), *Asset.OutputPath);
            return false;
        }
        if (RelativePaths.Contains(Asset.OutputPath))
        {
            OutError = FString::Printf(TEXT("Duplicate output path: %s"), *Asset.OutputPath);
            return false;
        }
        RelativePaths.Add(Asset.OutputPath);

        const FString Filename = FPaths::Combine(OutputDirectory, Asset.OutputPath);
        const int64 FileSize = IFileManager::Get().FileSize(*Filename);
        if (FileSize <= 0 || FileSize > MAX_uint32)
        {
            OutError = FString::Printf(TEXT("Output file is missing, empty, or too large to hash: %s"), *Filename);
            return false;
        }
        TArray<uint8> Bytes;
        if (!FFileHelper::LoadFileToArray(Bytes, *Filename))
        {
            OutError = FString::Printf(TEXT("Unable to read output file: %s"), *Filename);
            return false;
        }
        uint8 Digest[SHA256_DIGEST_LENGTH];
        if (!SHA256(Bytes.GetData(), static_cast<size_t>(Bytes.Num()), Digest))
        {
            OutError = FString::Printf(TEXT("Unable to hash output file: %s"), *Filename);
            return false;
        }
        FAlsExportFile& File = OutFiles.AddDefaulted_GetRef();
        File.RelativePath = Asset.OutputPath;
        File.Sha256 = BytesToHex(Digest, UE_ARRAY_COUNT(Digest)).ToLower();
        File.Size = FileSize;
    }

    OutFiles.Sort([](const FAlsExportFile& Left, const FAlsExportFile& Right)
    {
        return Left.RelativePath < Right.RelativePath;
    });

    const FString AuditDirectory = FPaths::Combine(OutputDirectory, TEXT("audit"));
    if (!IFileManager::Get().MakeDirectory(*AuditDirectory, true))
    {
        OutError = FString::Printf(TEXT("Unable to create audit directory: %s"), *AuditDirectory);
        return false;
    }
    FString Report = FString::Printf(TEXT("{\n  \"status\": \"complete\",\n  \"assetCount\": %d,\n  \"fileCount\": %d,\n  \"errorCount\": 0,\n  \"warningCount\": 0,\n  \"normalizedFbxKeys\": ["),
        Assets.Num(), OutFiles.Num());
    for (int32 Index = 0; Index < NormalizedFbxKeys.Num(); ++Index)
    {
        Report += FString::Printf(TEXT("%s\"%s\""), Index > 0 ? TEXT(", ") : TEXT(""), *NormalizedFbxKeys[Index]);
    }
    Report += TEXT("]\n}\n");
    if (!FFileHelper::SaveStringToFile(Report, *FPaths::Combine(AuditDirectory, TEXT("export_report.json")),
        FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
    {
        OutError = TEXT("Unable to write JSON export audit report.");
        return false;
    }
    const FString TextReport = FString::Printf(TEXT("status=complete\nassets=%d\nfiles=%d\nerrors=0\nwarnings=0\n"),
        Assets.Num(), OutFiles.Num());
    if (!FFileHelper::SaveStringToFile(TextReport, *FPaths::Combine(AuditDirectory, TEXT("export_report.txt")),
        FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
    {
        OutError = TEXT("Unable to write text export audit report.");
        return false;
    }
    return true;
}

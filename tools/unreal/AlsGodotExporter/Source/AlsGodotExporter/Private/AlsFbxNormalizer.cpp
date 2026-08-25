#include "AlsFbxNormalizer.h"

#include "Misc/FileHelper.h"

namespace
{
    FString LeadingWhitespace(const FString& Line)
    {
        int32 Index = 0;
        while (Index < Line.Len() && FChar::IsWhitespace(Line[Index]))
        {
            ++Index;
        }
        return Line.Left(Index);
    }

    int32 CountCharacter(const FString& Line, const TCHAR Character)
    {
        int32 Count = 0;
        for (const TCHAR Value : Line)
        {
            Count += Value == Character ? 1 : 0;
        }
        return Count;
    }
}

bool FAlsFbxNormalizer::Normalize(const FString& Filename, TArray<FString>& OutModifiedKeys, FString& OutError)
{
    FString Contents;
    if (!FFileHelper::LoadFileToString(Contents, *Filename))
    {
        OutError = FString::Printf(TEXT("Unable to read FBX for normalization: %s"), *Filename);
        return false;
    }
    if (!Contents.StartsWith(TEXT("; FBX")))
    {
        OutError = FString::Printf(TEXT("FBX is not ASCII text: %s"), *Filename);
        return false;
    }

    Contents.ReplaceInline(TEXT("\r\n"), TEXT("\n"));
    TArray<FString> Lines;
    Contents.ParseIntoArrayLines(Lines, false);
    bool bInCreationTimestamp = false;
    int32 TimestampDepth = 0;
    for (FString& Line : Lines)
    {
        const FString Trimmed = Line.TrimStart();
        const FString Indent = LeadingWhitespace(Line);
        if (Trimmed.StartsWith(TEXT("CreationTimeStamp:")))
        {
            bInCreationTimestamp = true;
            TimestampDepth = 1;
            continue;
        }
        if (bInCreationTimestamp)
        {
            TimestampDepth += CountCharacter(Line, TEXT('{')) - CountCharacter(Line, TEXT('}'));
            struct FTimestampField { const TCHAR* Key; const TCHAR* Value; };
            static const FTimestampField Fields[] = {
                { TEXT("Year:"), TEXT("1970") }, { TEXT("Month:"), TEXT("1") }, { TEXT("Day:"), TEXT("1") },
                { TEXT("Hour:"), TEXT("0") }, { TEXT("Minute:"), TEXT("0") }, { TEXT("Second:"), TEXT("0") },
                { TEXT("Millisecond:"), TEXT("0") },
            };
            for (const FTimestampField& Field : Fields)
            {
                if (Trimmed.StartsWith(Field.Key))
                {
                    Line = Indent + Field.Key + TEXT(" ") + Field.Value;
                    OutModifiedKeys.AddUnique(FString(TEXT("CreationTimeStamp.")) + FString(Field.Key).LeftChop(1));
                    break;
                }
            }
            if (TimestampDepth <= 0)
            {
                bInCreationTimestamp = false;
            }
        }
        if (Trimmed.StartsWith(TEXT("FileId:")))
        {
            Line = Indent + TEXT("FileId: \"00000000000000000000000000000000\"");
            OutModifiedKeys.AddUnique(TEXT("FileId"));
        }
        else if (Trimmed.StartsWith(TEXT("CreationTime:")))
        {
            Line = Indent + TEXT("CreationTime: \"1970-01-01 00:00:00:000\"");
            OutModifiedKeys.AddUnique(TEXT("CreationTime"));
        }
        else if (Trimmed.StartsWith(TEXT("LastSaved:")))
        {
            Line = Indent + TEXT("LastSaved: \"1970-01-01 00:00:00:000\"");
            OutModifiedKeys.AddUnique(TEXT("LastSaved"));
        }
    }

    FString Normalized = FString::Join(Lines, TEXT("\n")) + TEXT("\n");
    if (!FFileHelper::SaveStringToFile(Normalized, *Filename, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
    {
        OutError = FString::Printf(TEXT("Unable to write normalized FBX: %s"), *Filename);
        return false;
    }
    OutModifiedKeys.Sort();
    return true;
}

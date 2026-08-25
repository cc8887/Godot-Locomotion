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

    bool TryReadDeclaredObjectId(const FString& Line, FString& OutId)
    {
        int32 ColonIndex = INDEX_NONE;
        if (!Line.FindChar(TEXT(':'), ColonIndex))
        {
            return false;
        }
        int32 Index = ColonIndex + 1;
        while (Index < Line.Len() && FChar::IsWhitespace(Line[Index]))
        {
            ++Index;
        }
        const int32 StartIndex = Index;
        while (Index < Line.Len() && FChar::IsDigit(Line[Index]))
        {
            ++Index;
        }
        const int32 EndIndex = Index;
        if (Index == StartIndex || Index >= Line.Len() || Line[Index] != TEXT(','))
        {
            return false;
        }
        ++Index;
        while (Index < Line.Len() && FChar::IsWhitespace(Line[Index]))
        {
            ++Index;
        }
        if (Index >= Line.Len() || Line[Index] != TEXT('"'))
        {
            return false;
        }
        OutId = Line.Mid(StartIndex, EndIndex - StartIndex);
        return !OutId.IsEmpty() && OutId != TEXT("0");
    }

    bool TryReadPoseNodeId(const FString& Line, FString& OutId)
    {
        const FString Trimmed = Line.TrimStart();
        if (!Trimmed.StartsWith(TEXT("Node:")))
        {
            return false;
        }
        OutId = Trimmed.RightChop(5).TrimStartAndEnd();
        if (OutId.IsEmpty() || OutId == TEXT("0"))
        {
            return false;
        }
        for (const TCHAR Character : OutId)
        {
            if (!FChar::IsDigit(Character))
            {
                return false;
            }
        }
        return true;
    }

    FString RemapNumericTokens(const FString& Line, const TMap<FString, FString>& ObjectIds)
    {
        FString Result;
        Result.Reserve(Line.Len());
        int32 Index = 0;
        while (Index < Line.Len())
        {
            if (!FChar::IsDigit(Line[Index]))
            {
                Result.AppendChar(Line[Index++]);
                continue;
            }
            const int32 StartIndex = Index;
            while (Index < Line.Len() && FChar::IsDigit(Line[Index]))
            {
                ++Index;
            }
            const FString Token = Line.Mid(StartIndex, Index - StartIndex);
            if (const FString* Replacement = ObjectIds.Find(Token))
            {
                Result += *Replacement;
            }
            else
            {
                Result += Token;
            }
        }
        return Result;
    }

    bool ContainsNumericToken(const FString& Line, const TSet<FString>& Tokens)
    {
        int32 Index = 0;
        while (Index < Line.Len())
        {
            if (!FChar::IsDigit(Line[Index]))
            {
                ++Index;
                continue;
            }
            const int32 StartIndex = Index;
            while (Index < Line.Len() && FChar::IsDigit(Line[Index]))
            {
                ++Index;
            }
            if (Tokens.Contains(Line.Mid(StartIndex, Index - StartIndex)))
            {
                return true;
            }
        }
        return false;
    }

    bool StripExternalTextureObjects(TArray<FString>& Lines, bool& bOutModified, FString& OutError)
    {
        TSet<FString> TextureObjectIds;
        TArray<FString> FilteredLines;
        FilteredLines.Reserve(Lines.Num());
        for (int32 Index = 0; Index < Lines.Num(); ++Index)
        {
            const FString Trimmed = Lines[Index].TrimStart();
            FString ObjectId;
            const bool bTextureObject = Trimmed.StartsWith(TEXT("Texture:")) || Trimmed.StartsWith(TEXT("Video:"));
            if (!bTextureObject || !TryReadDeclaredObjectId(Lines[Index], ObjectId))
            {
                FilteredLines.Add(Lines[Index]);
                continue;
            }

            TextureObjectIds.Add(ObjectId);
            int32 Depth = CountCharacter(Lines[Index], TEXT('{')) - CountCharacter(Lines[Index], TEXT('}'));
            while (Depth > 0 && ++Index < Lines.Num())
            {
                Depth += CountCharacter(Lines[Index], TEXT('{')) - CountCharacter(Lines[Index], TEXT('}'));
            }
            if (Depth != 0)
            {
                OutError = TEXT("FBX contains an unterminated Texture or Video object.");
                return false;
            }
        }

        if (TextureObjectIds.IsEmpty())
        {
            bOutModified = false;
            return true;
        }

        Lines.Reset(FilteredLines.Num());
        for (const FString& Line : FilteredLines)
        {
            const FString Trimmed = Line.TrimStart();
            if (Trimmed.StartsWith(TEXT("C:")) && ContainsNumericToken(Trimmed, TextureObjectIds))
            {
                continue;
            }
            Lines.Add(Line);
        }
        bOutModified = true;
        return true;
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
    bool bStrippedExternalTextures = false;
    if (!StripExternalTextureObjects(Lines, bStrippedExternalTextures, OutError))
    {
        return false;
    }
    if (bStrippedExternalTextures)
    {
        OutModifiedKeys.AddUnique(TEXT("ExternalTextureObject"));
    }
    TMap<FString, FString> ObjectIds;
    for (const FString& Line : Lines)
    {
        FString ObjectId;
        if (TryReadDeclaredObjectId(Line, ObjectId) && !ObjectIds.Contains(ObjectId))
        {
            ObjectIds.Add(ObjectId, FString::Printf(TEXT("%lld"), 1000000000000LL + ObjectIds.Num() + 1));
        }
    }
    for (const FString& Line : Lines)
    {
        FString ObjectId;
        if (TryReadPoseNodeId(Line, ObjectId) && !ObjectIds.Contains(ObjectId))
        {
            ObjectIds.Add(ObjectId, FString::Printf(TEXT("%lld"), 1000000000000LL + ObjectIds.Num() + 1));
        }
    }
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
        else if (Trimmed.StartsWith(TEXT("P: \"DocumentUrl\",")))
        {
            Line = Indent + TEXT("P: \"DocumentUrl\", \"KString\", \"Url\", \"\", \"als://normalized.fbx\"");
            OutModifiedKeys.AddUnique(TEXT("DocumentUrl"));
        }
        else if (Trimmed.StartsWith(TEXT("P: \"SrcDocumentUrl\",")))
        {
            Line = Indent + TEXT("P: \"SrcDocumentUrl\", \"KString\", \"Url\", \"\", \"als://normalized.fbx\"");
            OutModifiedKeys.AddUnique(TEXT("SrcDocumentUrl"));
        }
    }

    for (FString& Line : Lines)
    {
        Line = RemapNumericTokens(Line, ObjectIds);
    }
    if (!ObjectIds.IsEmpty())
    {
        OutModifiedKeys.AddUnique(TEXT("ObjectId"));
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

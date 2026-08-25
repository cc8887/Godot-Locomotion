#pragma once

class FAlsFbxNormalizer
{
public:
    static bool Normalize(const FString& Filename, TArray<FString>& OutModifiedKeys, FString& OutError);
};

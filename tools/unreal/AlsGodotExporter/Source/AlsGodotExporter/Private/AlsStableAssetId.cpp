#include "AlsStableAssetId.h"

#include "Misc/SecureHash.h"

FString FAlsStableAssetId::Create(const FString& ObjectPath)
{
    const FTCHARToUTF8 Utf8(*ObjectPath);
    uint8 Hash[FSHA1::DigestSize];
    FSHA1::HashBuffer(Utf8.Get(), Utf8.Length(), Hash);
    return BytesToHex(Hash, UE_ARRAY_COUNT(Hash)).ToLower();
}

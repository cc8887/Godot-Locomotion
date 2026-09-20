#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsMeshSpaceBlendCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsMeshSpaceBlendCommandlet final : public UCommandlet
{
    GENERATED_BODY()
public:
    UAlsMeshSpaceBlendCommandlet();
    virtual int32 Main(const FString& Params) override;
};

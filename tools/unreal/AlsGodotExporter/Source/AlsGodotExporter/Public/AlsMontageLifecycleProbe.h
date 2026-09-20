#pragma once
#include "Animation/AnimInstance.h"
#include "AlsMontageLifecycleProbe.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsMontageLifecycleProbe final : public UAnimInstance
{
    GENERATED_BODY()
public:
    // Real UE weight/advance/evaluation stages, before any simulated Blueprint request.
    void ProbeTick(float Delta);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportTrace(const FString& Output, bool IncludeActions = false, bool IncludeRootMotion = false);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportAdditiveTrace(const FString& Output);
protected:
    virtual FAnimInstanceProxy* CreateAnimInstanceProxy() override;
};

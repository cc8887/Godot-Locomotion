#pragma once
#include "CoreMinimal.h"
class UAnimInstance;
class ACharacter;
struct FAnimInstanceProxy;
class IAnimClassInterface;
class FJsonObject;
namespace LyraMainObservationProbe
{
TSharedPtr<FJsonObject> Tick(UAnimInstance* Main, ACharacter* Owner, FAnimInstanceProxy& Proxy,
    const IAnimClassInterface* Interface, const TSharedPtr<FJsonObject>& Frame, float Delta);
TSharedPtr<FJsonObject> TailSnapshot(UAnimInstance* Main);
}

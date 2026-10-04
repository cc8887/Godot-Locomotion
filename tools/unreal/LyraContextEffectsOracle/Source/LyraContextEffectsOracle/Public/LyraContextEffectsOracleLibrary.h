#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "Feedback/ContextEffects/LyraContextEffectComponent.h"
#include "GameFramework/Actor.h"
#include "LyraContextEffectsOracleLibrary.generated.h"

UCLASS()
class LYRACONTEXTEFFECTSORACLE_API ULyraContextEffectsOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Context Effects Oracle")
    static FString ReadPolicy();
    UFUNCTION(BlueprintCallable,Category="Lyra Context Effects Oracle")
    static FString ReadTrace(const FString& RequestsJson);
};

UCLASS()
class LYRACONTEXTEFFECTSORACLE_API ALyraContextOracleActor final:public AActor,public ILyraContextEffectsInterface
{
    GENERATED_BODY()
public:
    virtual void AnimMotionEffect_Implementation(FName Bone,FGameplayTag Effect,USceneComponent* Mesh,
        FVector Location,FRotator Rotation,const UAnimSequenceBase* Animation,bool Hit,FHitResult Result,
        FGameplayTagContainer Contexts,FVector Scale,float Volume,float Pitch) override;
};

UCLASS()
class LYRACONTEXTEFFECTSORACLE_API ULyraContextOracleComponent final:public ULyraContextEffectComponent
{
    GENERATED_BODY()
public:
    virtual void AnimMotionEffect_Implementation(FName Bone,FGameplayTag Effect,USceneComponent* Mesh,
        FVector Location,FRotator Rotation,const UAnimSequenceBase* Animation,bool Hit,FHitResult Result,
        FGameplayTagContainer Contexts,FVector Scale,float Volume,float Pitch) override;
};

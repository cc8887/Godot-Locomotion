#include "AlsAnimationGraphLibrary.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Engine/World.h"
#include "Components/PrimitiveComponent.h"

namespace
{
// Access the actual object's protected data through legal member pointers.
// This does not instantiate/downcast to a replacement movement component.
struct FMovementProbeAccess : UCharacterMovementComponent
{
    static void Set(UCharacterMovementComponent& Component, const FVector& Input, float Analog)
    {
        Component.*&FMovementProbeAccess::Acceleration = Input;
        Component.*&FMovementProbeAccess::AnalogInputModifier = Analog;
    }
};
}

bool UAlsAnimationGraphLibrary::SetMovementProbeInput(UCharacterMovementComponent* Component, FVector Acceleration, float Analog)
{
    if (!IsInGameThread() || !IsValid(Component) || !Component->HasValidData() ||
        Component->HasAnyFlags(RF_ClassDefaultObject | RF_ArchetypeObject) ||
        !Component->GetOwner()->HasAnyFlags(RF_Transient) || Component->GetWorld()->WorldType != EWorldType::Editor ||
        Acceleration.ContainsNaN() || !FMath::IsFinite(Analog) || Analog < 0 || Analog > 1)
        return false;
    FMovementProbeAccess::Set(*Component, Acceleration, Analog);
    return true;
}

bool UAlsAnimationGraphLibrary::AdvanceMovementProbeBase(UCharacterMovementComponent* Component, UPrimitiveComponent* Base, float DeltaSeconds, bool Initialize)
{
    if (!IsInGameThread() || !IsValid(Component) || !Component->HasValidData() || !IsValid(Base) ||
        Component->HasAnyFlags(RF_ClassDefaultObject | RF_ArchetypeObject) ||
        Base->HasAnyFlags(RF_ClassDefaultObject | RF_ArchetypeObject) ||
        !Component->GetOwner()->HasAnyFlags(RF_Transient) || !IsValid(Base->GetOwner()) ||
        !Base->GetOwner()->HasAnyFlags(RF_Transient) || Component->GetWorld() != Base->GetWorld() ||
        Component->GetWorld()->WorldType != EWorldType::Editor ||
        !FMath::IsFinite(DeltaSeconds) || DeltaSeconds <= 0 || DeltaSeconds > 1)
        return false;
    if (Initialize)
    {
        auto BaseData = MovementBaseUtility::GetMovementBaseDataFromPhysicsOwner(Base);
        Component->SetBase(&BaseData);
    }
    if (Component->GetMovementBaseObject() != Base) return false;
    if (!Initialize) Component->UpdateBasedMovement(DeltaSeconds);
    Component->SaveBaseLocation();
    return Component->GetMovementBaseObject() == Base;
}

AActor* UAlsAnimationGraphLibrary::SpawnMovementProbeActor(UWorld* World, TSubclassOf<AActor> ActorClass, FVector Location)
{
    if (!IsInGameThread() || !IsValid(World) || World->WorldType != EWorldType::Editor || !ActorClass ||
        ActorClass->HasAnyClassFlags(CLASS_Abstract) || Location.ContainsNaN())
        return nullptr;
    FActorSpawnParameters Parameters;
    Parameters.ObjectFlags = RF_Transient;
    Parameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    return World->SpawnActor<AActor>(ActorClass, Location, FRotator::ZeroRotator, Parameters);
}

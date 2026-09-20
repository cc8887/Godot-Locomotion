#pragma once
#include "CoreMinimal.h"

// Read authored data and native mass properties in an isolated reference-pose world.
bool ExportAlsPhysicsAssets(const FString& Output, FString& Error);

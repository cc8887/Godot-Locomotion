#pragma once
#include "CoreMinimal.h"

// Read authored data and native mass properties in an isolated reference-pose world.
bool ExportAlsPhysicsAssets(const FString& Output, FString& Error);
bool ExportAlsPhysicsJointReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsJointSolverReference(const FString& Output, FString& Error, bool DisableSleep = false);
bool ExportAlsPhysicsInertiaReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsProjectionReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsAngularRowReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsJointStepReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsContactReference(const FString& Output, FString& Error, bool GatherGeometry = false);
bool ExportAlsPhysicsContactHistoryReference(const FString& Output, FString& Error);

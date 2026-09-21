#pragma once
#include "CoreMinimal.h"
bool ExportAlsPhysicsConvexMarginSupport(const FString& Output, FString& Error);

// Read authored data and native mass properties in an isolated reference-pose world.
bool ExportAlsPhysicsAssets(const FString& Output, FString& Error, bool ObserveRuntimeShapes = false);
bool ExportAlsPhysicsJointReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsJointSolverReference(const FString& Output, FString& Error, bool DisableSleep = false, bool SleepDiagnostics = false);
bool ExportAlsPhysicsInertiaReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsProjectionReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsAngularRowReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsJointStepReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsContactReference(const FString& Output, FString& Error, bool GatherGeometry = false, bool ShockPropagation = false);
bool ExportAlsPhysicsContactHistoryReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsGraphReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsCoupledStepReference(const FString& Inputs, const FString& Output, FString& Error);
bool ExportAlsPhysicsCapsuleGeometryReference(const FString& Input, const FString& Output, FString& Error);
bool ExportAlsPhysicsManifoldRestoreReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsCullReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsContactSettings(const FString& Output, FString& Error);
bool ExportAlsPhysicsBoxGeometryReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsConvexTopology(const FString& Output, FString& Error);
bool ExportAlsPhysicsFaceClipReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsGjkPrimitivesReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsGjkSearchReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsMarginReference(const FString& Output, FString& Error);
bool ExportAlsPhysicsConvexPairReference(const FString& Output, FString& Error, bool Scaled = false, bool BoxPairs = false, bool MarginPairs = false);

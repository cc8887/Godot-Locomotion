#include "AlsGodotExportCommandlet.h"

#include "AlsAssetDiscovery.h"
#include "AlsExportPlanner.h"
#include "AlsFbxExporter.h"
#include "AlsAnimationMetadataReader.h"
#include "AlsCompositeAssetReader.h"
#include "AlsManifestWriter.h"
#include "AlsOutputAuditor.h"
#include "AlsPhysicsAssetExport.h"
#include "AlsTextureExporter.h"
#include "Misc/App.h"
#include "Misc/EngineVersion.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"

DEFINE_LOG_CATEGORY_STATIC(LogAlsGodotExporter, Log, All);

UAlsGodotExportCommandlet::UAlsGodotExportCommandlet()
{
    IsClient = false;
    IsEditor = true;
    LogToConsole = true;
    ShowErrorCount = true;
}

int32 UAlsGodotExportCommandlet::Main(const FString& Params)
{
    FString PhysicsOutput;
    if (FParse::Value(*Params, TEXT("PhysicsNativeCapsuleMixedOutput="), PhysicsOutput))
    {
        FString Input, Error; FParse::Value(*Params, TEXT("PhysicsNativeCapsuleMixedInput="), Input);
        if (!ExportAlsPhysicsNativeCapsuleMixedTrace(Input, PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Native capsule mixed trace failed: %s"), *Error); return 48; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsNativeCapsuleTraceOutput="), PhysicsOutput))
    {
        FString Input, Error; FParse::Value(*Params, TEXT("PhysicsNativeCapsuleTraceInput="), Input);
        if (!ExportAlsPhysicsNativeCapsuleTrace(Input, PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Native capsule trace failed: %s"), *Error); return 47; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsCapsuleConvexOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsCapsuleConvexReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Capsule convex reference failed: %s"), *Error); return 46; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsCapsulePairDegenerateOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsCapsulePairReference(PhysicsOutput, Error, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Degenerate capsule pair reference failed: %s"), *Error); return 45; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsCapsulePairOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsCapsulePairReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Capsule pair reference failed: %s"), *Error); return 44; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsCapsuleBoxOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsCapsuleBoxReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Full capsule-box reference failed: %s"), *Error); return 43; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsSphereBoxOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsSphereBoxReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Sphere-box reference failed: %s"), *Error); return 42; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsPrimitiveGeometryOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsAssets(PhysicsOutput, Error, true, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Primitive geometry failed: %s"), *Error); return 41; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsContactSettingsOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsContactSettings(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Contact settings failed: %s"), *Error); return 40; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsConvexMarginPairOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsConvexPairReference(PhysicsOutput, Error, false, true, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Convex margin pair failed: %s"), *Error); return 39; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CONVEX_MARGIN_PAIR_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsConvexMarginSupportOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsConvexMarginSupport(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Convex margin support failed: %s"), *Error); return 38; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CONVEX_MARGIN_SUPPORT_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsRuntimeShapesOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsAssets(PhysicsOutput, Error, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Runtime shape export failed: %s"), *Error); return 37; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_RUNTIME_SHAPES_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsBoxPairOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsConvexPairReference(PhysicsOutput, Error, false, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Box pair reference failed: %s"), *Error); return 36; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_BOX_PAIR_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsScaledConvexPairOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsConvexPairReference(PhysicsOutput, Error, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Scaled convex pair reference failed: %s"), *Error); return 35; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_SCALED_CONVEX_PAIR_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsConvexPairOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsConvexPairReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Convex pair reference failed: %s"), *Error); return 34; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CONVEX_PAIR_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsMarginOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsMarginReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Margin reference failed: %s"), *Error); return 33; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_MARGIN_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsGjkSearchOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsGjkSearchReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("GJK search failed: %s"), *Error); return 32; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_GJK_SEARCH_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsGjkPrimitivesOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsGjkPrimitivesReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("GJK primitives failed: %s"), *Error); return 31; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_GJK_PRIMITIVES_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsFaceClipOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsFaceClipReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Face clipping reference failed: %s"), *Error); return 30; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_FACE_CLIP_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsConvexTopologyOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsConvexTopology(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Convex topology failed: %s"), *Error); return 29; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CONVEX_TOPOLOGY_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsBoxGeometryOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsBoxGeometryReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Box geometry failed: %s"), *Error); return 28; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_BOX_GEOMETRY_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsCullOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsCullReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Cull export failed: %s"), *Error); return 27; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CULL_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsShapeBoundsOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsShapeBoundsReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Shape bounds export failed: %s"), *Error); return 52; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_SHAPE_BOUNDS_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsContactShockOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsContactReference(PhysicsOutput, Error, false, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Contact shock export failed: %s"), *Error); return 26; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CONTACT_SHOCK_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsManifoldRestoreOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsManifoldRestoreReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Manifold restoration export failed: %s"), *Error); return 25; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_MANIFOLD_RESTORE_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsCapsuleGeometryOutput="), PhysicsOutput))
    {
        FString Input, Error; FParse::Value(*Params, TEXT("PhysicsCapsuleGeometryInput="), Input);
        if (!ExportAlsPhysicsCapsuleGeometryReference(Input, PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Capsule geometry export failed: %s"), *Error); return 24; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CAPSULE_GEOMETRY_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsWorldOutput="), PhysicsOutput))
    {
        FString Inputs, Error; FParse::Value(*Params, TEXT("PhysicsWorldInputs="), Inputs);
        int32 ContactFrames=0; FParse::Value(*Params, TEXT("PhysicsWorldContactFrames="), ContactFrames);
        int32 ContactStart=1; FParse::Value(*Params, TEXT("PhysicsWorldContactStart="), ContactStart);
        if (!ExportAlsPhysicsWorldReference(Inputs, PhysicsOutput, Error, ContactFrames, ContactStart))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("World reference export failed: %s"), *Error); return 51; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_WORLD_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsBoxCaptureOutput="), PhysicsOutput))
    {
        FString Input, Error; FParse::Value(*Params, TEXT("PhysicsBoxCaptureInput="), Input);
        if (!ExportAlsPhysicsBoxCaptureReference(Input, PhysicsOutput, Error))
        { UE_LOG(LogTemp, Error, TEXT("%s"), *Error); return 53; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsActualHistoryOutput="), PhysicsOutput))
    {
        FString Inputs, Error; FParse::Value(*Params, TEXT("PhysicsActualHistoryInputs="), Inputs);
        if (!ExportAlsPhysicsActualHistoryReference(Inputs, PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Actual history export failed: %s"), *Error); return 50; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_ACTUAL_HISTORY_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsRawGatherOutput="), PhysicsOutput))
    {
        FString Inputs, Error; FParse::Value(*Params, TEXT("PhysicsRawGatherInputs="), Inputs);
        if (!ExportAlsPhysicsRawGatherReference(Inputs, PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Raw Gather export failed: %s"), *Error); return 49; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_RAW_GATHER_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsSphereConvexOutput="), PhysicsOutput))
    {
        FString Properties, Error; FParse::Value(*Params, TEXT("PhysicsConvexPropertiesOutput="), Properties);
        if (!ExportAlsPhysicsSphereConvexReference(PhysicsOutput, Properties, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Sphere-convex export failed: %s"), *Error); return 52; }
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsCoupledOutput="), PhysicsOutput))
    {
        FString Inputs, Error; FParse::Value(*Params, TEXT("PhysicsCoupledInputs="), Inputs);
        if (!ExportAlsPhysicsCoupledStepReference(Inputs, PhysicsOutput, Error, FParse::Param(*Params, TEXT("PhysicsCoupledJointTrace")),
            FParse::Param(*Params, TEXT("PhysicsCoupledJointGather"))))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Coupled reference export failed: %s"), *Error); return 23; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_COUPLED_OK assets_saved=0")); return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsGraphOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsGraphReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Graph reference export failed: %s"), *Error); return 22; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_GRAPH_OK cases=16 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsSleepOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsJointSolverReference(PhysicsOutput, Error, false, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Sleep reference export failed: %s"), *Error); return 21; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_SLEEP_OK cases=144 steps=60 wake_frame=31 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsContactHistoryOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsContactHistoryReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Contact history export failed: %s"), *Error); return 20; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CONTACT_HISTORY_OK cases=192 frames=1152 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsContactGatherOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsContactReference(PhysicsOutput, Error, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Contact gather export failed: %s"), *Error); return 19; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CONTACT_GATHER_OK cases=432 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsContactOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsContactReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Contact export failed: %s"), *Error); return 18; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_CONTACT_OK cases=288 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsAwakeSolverOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsJointSolverReference(PhysicsOutput, Error, true))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Awake solver export failed: %s"), *Error); return 17; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_AWAKE_SOLVER_OK cases=144 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsJointStepOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsJointStepReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Joint step export failed: %s"), *Error); return 16; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_JOINT_STEP_OK cases=288 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsAngularRowOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsAngularRowReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Angular row export failed: %s"), *Error); return 15; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_ANGULAR_ROWS_OK cases=516 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsProjectionOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsProjectionReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Projection export failed: %s"), *Error); return 14; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_PROJECTION_REFERENCE_OK cases=262 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsJointFramesOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsJointFrameInputs(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Joint frames export failed: %s"), *Error); return 13; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_JOINT_FRAMES_OK rigs=2 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsInertiaOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsInertiaReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Inertia export failed: %s"), *Error); return 13; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_INERTIA_REFERENCE_OK rigs=6 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsJointSolverOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsJointSolverReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Joint solver export failed: %s"), *Error); return 12; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_JOINT_SOLVER_REFERENCE_OK cases=144 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsJointOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsJointReference(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Joint reference export failed: %s"), *Error); return 11; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_JOINT_REFERENCE_OK meshes=2 assets_saved=0"));
        return 0;
    }
    if (FParse::Value(*Params, TEXT("PhysicsAssetOutput="), PhysicsOutput))
    {
        FString Error;
        if (!ExportAlsPhysicsAssets(PhysicsOutput, Error))
        { UE_LOG(LogAlsGodotExporter, Error, TEXT("Physics asset export failed: %s"), *Error); return 10; }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("ALS_PHYSICS_ASSETS_OK meshes=2 assets_saved=0"));
        return 0;
    }
    if (FParse::Param(*Params, TEXT("ReadyCheck")))
    {
        int32 SelfTestCaseCount = 0;
        FString SelfTestError;
        if (!FAlsAnimationMetadataReader::RunCurveKeySelfTest(SelfTestCaseCount, SelfTestError))
        {
            UE_LOG(LogAlsGodotExporter, Error, TEXT("Curve export self-test failed: %s"), *SelfTestError);
            return 7;
        }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_CURVE_EXPORT_SELF_TEST_OK cases=%d"), SelfTestCaseCount);
        int32 TimelineSelfTestCaseCount = 0;
        FString TimelineSelfTestError;
        if (!FAlsAnimationMetadataReader::RunTimelineSelfTest(TimelineSelfTestCaseCount, TimelineSelfTestError))
        {
            UE_LOG(LogAlsGodotExporter, Error, TEXT("Timeline export self-test failed: %s"), *TimelineSelfTestError);
            return 8;
        }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_TIMELINE_EXPORT_SELF_TEST_OK cases=%d"),
            TimelineSelfTestCaseCount);
        int32 CompositeSelfTestCaseCount = 0;
        FString CompositeSelfTestError;
        if (!FAlsCompositeAssetReader::RunSelfTest(CompositeSelfTestCaseCount, CompositeSelfTestError))
        {
            UE_LOG(LogAlsGodotExporter, Error, TEXT("Composite export self-test failed: %s"), *CompositeSelfTestError);
            return 9;
        }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_COMPOSITE_EXPORT_SELF_TEST_OK cases=%d"),
            CompositeSelfTestCaseCount);
        const FEngineVersion EngineVersion = FEngineVersion::Current();
        UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_EXPORTER_READY engine=%d.%d.%d plugin=2.0.0"),
            EngineVersion.GetMajor(), EngineVersion.GetMinor(), EngineVersion.GetPatch());
        return 0;
    }

    const bool bDryRun = FParse::Param(*Params, TEXT("DryRun"));
    const bool bExport = FParse::Param(*Params, TEXT("Export"));
    if (bDryRun == bExport)
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("ALS export commandlet requires -ReadyCheck, -DryRun, or -Export."));
        return 2;
    }

    FString OutputDirectory;
    if (!FParse::Value(*Params, TEXT("Output="), OutputDirectory) || OutputDirectory.IsEmpty() || FPaths::IsRelative(OutputDirectory))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("-DryRun or -Export requires an absolute -Output path."));
        return 2;
    }
    OutputDirectory = FPaths::ConvertRelativePathToFull(OutputDirectory);

    TArray<FAlsExportAsset> Assets;
    FString Error;
    if (!FAlsAssetDiscovery::Discover(Assets, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Asset discovery failed: %s"), *Error);
        return 3;
    }

    int32 ExportableCount = 0;
    int32 ConfigCount = 0;
    if (!FAlsExportPlanner::Write(OutputDirectory, Assets, ExportableCount, ConfigCount, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Export plan failed: %s"), *Error);
        return 3;
    }

    if (!FAlsManifestWriter::WritePlanned(OutputDirectory, Assets, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Metadata extraction failed: %s"), *Error);
        return 5;
    }

    UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_P2A_PLAN_OK assets=%d exportable=%d config=%d excluded=0"),
        Assets.Num(), ExportableCount, ConfigCount);
    if (bDryRun)
    {
        return 0;
    }

    int32 FbxFileCount = 0;
    int32 TextureFileCount = 0;
    TArray<FString> NormalizedFbxKeys;
    if (!FAlsFbxExporter::Export(OutputDirectory, Assets, FbxFileCount, NormalizedFbxKeys, Error) ||
        !FAlsTextureExporter::Export(OutputDirectory, Assets, TextureFileCount, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Asset export failed: %s"), *Error);
        return 4;
    }

    TArray<FAlsExportFile> Files;
    if (!FAlsOutputAuditor::Audit(OutputDirectory, Assets, NormalizedFbxKeys, Files, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Output audit failed: %s"), *Error);
        return 6;
    }
    if (!FAlsManifestWriter::WriteComplete(OutputDirectory, Assets, Files, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Formal manifest publication failed: %s"), *Error);
        return 6;
    }

    UE_LOG(LogAlsGodotExporter, Display,
        TEXT("GODOT_ALS_P2A_EXPORT_OK assets=%d files=%d fbx=%d textures=%d warnings=0"),
        Assets.Num(), Files.Num(), FbxFileCount, TextureFileCount);
    return 0;
}

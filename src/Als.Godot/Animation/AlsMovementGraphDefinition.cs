using GodotAls.Core.Contracts;
using GodotAls.Core.Actions;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Compiled once on the main thread, shared by the exclusive per-character runtime owners.
// The library, Grounded, Air and notify binder must all use this one source closure.
internal sealed record AlsMovementGraphDefinition(AlsLocomotionSourceProfile Sources,
    AlsP5CoreRuntimeBindingSnapshot Binding, AlsGroundedMachineProfile Movement,
    AlsMainGroundedCachedGraphProfile Grounded, AlsCrouchingCycleProfile Crouching,
    AlsGroundedPoseDependencies Dependencies, AlsAirPoseProfile Air,
    AlsLandingPoseProfile Landing, AlsBaseLayerProfile BaseLayer, AlsMovementInputCurveProfile InputCurves,
    AlsMovementInputFunctions InputFunctions, AlsLandPredictionModel LandPrediction, AlsInAirAnimationInputModel AirInput,
    AlsGroundedInputFunctions GroundedInputFunctions, AlsGroundedRateFunctions GroundedRates, AlsMovementInputStateDefaults InputStateDefaults,
    AlsGroundedAnimationInputModel GroundedInput, AlsJumpAnimationInputModel JumpInput, AlsGroundedControlInputModel GroundedControl,
    AlsIdleControlInputModel IdleControl, AlsTurnInPlaceModel TurnInPlace, AlsDynamicMontageAsset[] TurnMontageAssets,
    AlsTurnNotifyBinding TurnNotifies, AlsAuthoredMontageAsset[] AuthoredMontageAssets, AlsMontageActionPolicy[] ActionPolicies,
    AlsMontageNotifyBinding MontageNotifies)
{
    public AlsMontageActionPlaybackReader ActionPlayback { get; init; } = null!;
    public int RollDefinitionId { get; init; }
    public int GetUpFrontDefinitionId { get; init; }
    public int GetUpBackDefinitionId { get; init; }
    public AlsGetUpSelectionProfile GetUpSelection { get; init; } = null!;
    public AlsOverlayOverrideNotifyProfile OverlayOverrideNotifies { get; init; } = null!;
    public AlsGroundedEntryNotifyProfile GroundedEntryNotify { get; init; } = null!;
    public AlsCharacterAnimationBridgeSettings CharacterBridge { get; init; }
    public AlsCharacterRotationModel CharacterRotation { get; init; } = null!;
    public AlsCharacterMovementModel CharacterMovement { get; init; } = null!;
    public AlsCharacterMovementRuntime CharacterMovementRuntime { get; init; } = null!;
    public AlsLayerBlendingDefinition LayerBlending { get; init; } = null!;
    public AlsLayeringInputModel LayeringInput { get; init; } = null!;
    public AlsAimingInputModel AimingInput { get; init; } = null!;
    public AlsAimPoseDefinition AimPose { get; init; } = null!;
    public AlsAimLayerDefinition AimLayer { get; init; } = null!;
    public AlsHandIkDefinition HandIk { get; init; } = null!;
    public AlsPelvisIkInputModel PelvisIkInput { get; init; } = null!;
    public AlsFootIkInputModel FootIkInput { get; init; } = null!;
    public AlsFootIkDefinition FootIk { get; init; } = null!;
    public AlsRootPoseDefinition RootPose { get; init; } = null!;
    public AlsRagdollPoseProfile RagdollPose { get; init; } = null!;
    public AlsRagdollFrameDefinition RagdollFrame { get; init; } = null!;
    public AlsRootSharedSourceProfile RootSharedSources { get; init; } = null!;
    public AlsP5CoreRuntimeBindingSnapshot RootSharedBinding { get; init; } = null!;
    public int MannequinMeshId { get; init; } = -1;
    public AlsRawAnimationSourceBank RagdollRawSources { get; init; } = null!;
    public AlsAimSamplingProfile AimSampling { get; init; } = null!;
    public AlsRawAnimationSourceBank AimRawSources { get; init; } = null!;
    public AlsOverlaySourceProfile OverlaySources { get; init; } = null!;
    public AlsOverlayClockDefinition OverlayClocks { get; init; } = null!;
    public AlsOverlaySharedSourceProfile OverlaySharedSources { get; init; } = null!;
    public AlsP5CoreRuntimeBindingSnapshot OverlaySharedBinding { get; init; } = null!;
    public AlsOverlayStateGraph OverlayStates { get; init; } = null!;
    public AlsOverlayTransitionDefinition OverlayTransitions { get; init; } = null!;
    public AlsSequenceMontageAsset[] OverlayTransitionAssets { get; init; } = [];
    public AlsStopTransitionDefinition StopTransitions { get; init; } = null!;
    public AlsSequenceMontageAsset[] GroundedTransitionAssets { get; init; } = [];
    public AlsRawAnimationSourceBank StopRawSources { get; init; } = null!;
    public string GroundedTransitionNotifyJson { get; init; } = "";
    public AlsOverlayPoseDefinition OverlayPose { get; init; } = null!;
    public AlsRawAnimationSourceBank OverlayRawSources { get; init; } = null!;
    public AlsBasePosesDefinition BasePoses { get; init; } = null!;
    public AlsBasePoseRetargetDefinition[] BasePoseRetarget { get; init; } = [];
    public AlsSourcePoseKeyTable[] BasePoseSourceKeys { get; init; } = [];
    public AlsRawAnimationSourceBank RawSources { get; init; } = null!;
    public float ComponentTeleportDistance => CharacterBridge.ComponentTeleportDistance;
    public AlsMovementGraphDefinition WithSharedOverlaySources(AlsAnimationSetDefinition set)
    {
        return WithSharedSources(set, OverlaySharedSources.Sources, OverlaySharedBinding);
    }
    public AlsMovementGraphDefinition WithSharedRootSources(AlsAnimationSetDefinition set) =>
        WithSharedSources(set, RootSharedSources.Sources, RootSharedBinding);
    private AlsMovementGraphDefinition WithSharedSources(AlsAnimationSetDefinition set, AlsLocomotionSourceProfile sources, AlsP5CoreRuntimeBindingSnapshot binding)
    {
        return (this with { Sources = sources, Binding = binding,
            TurnNotifies = AlsTurnNotifyCompiler.Compile(Read("v4_turn_notify_inputs.json"), set, sources, binding, TurnMontageAssets),
            MontageNotifies = AlsMontageNotifyCompiler.Compile(Read("v4_recovery_action_notify_inputs.json"), Read("v4_turn_notify_inputs.json"),
                set, sources, binding, TurnMontageAssets, AuthoredMontageAssets, GroundedTransitionAssets, GroundedTransitionNotifyJson) }).WithActionPlayback(set);
    }
    private AlsMovementGraphDefinition WithActionPlayback(AlsAnimationSetDefinition set) => this with
    { ActionPlayback = AlsMontageActionPlaybackCompiler.Compile(set, AuthoredMontageAssets, MontageNotifies) };
    public static AlsMovementGraphDefinition Load(AlsAnimationSetDefinition set,
        AlsLocomotionAnimationProfile locomotion, AlsPoseAnimationProfile? pose = null, bool loadRawSources = true)
    {
        var json = Read("v4_main_movement_graph.json"); var cache = Read("v4_pose_cache_graph.json");
        pose ??= AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var sources = AlsLocomotionSourceCompiler.CompileWithMovement(json, set, locomotion.SkeletonId);
        var binding = AlsLocomotionGraphBuilder.CompileSourceBindings(set, locomotion, pose, sources);
        var inputJson = Read("v4_movement_runtime_inputs.json");
        var inputCurves = AlsMovementInputCurveCompiler.Compile(inputJson);
        var functions = AlsMovementInputFunctionCompiler.Compile(inputJson, inputCurves);
        var landing = AlsLandPredictionCompiler.Compile(inputJson, inputCurves);
        var groundedFunctions = AlsGroundedInputFunctionCompiler.Compile(inputJson, inputCurves);
        var groundedRates = AlsGroundedRateCompiler.Compile(inputJson, inputCurves, functions);
        var defaults = AlsMovementInputStateCompiler.Compile(inputJson);
        var turns = AlsTurnInPlaceCompiler.CompileMontageAssets(Read("v4_turn_montage_inputs.json"), set, pose);
        var actionProfile = AlsRecoveryActionProfileCompiler.Compile(Read("p5a_animation_runtime.json"), Read("p5_get_up_actions.json"), set);
        var actions = AlsAuthoredMontageCompiler.Compile(Read("v4_recovery_action_montage_inputs.json"),
            Read("v4_turn_montage_inputs.json"), set, actionProfile, locomotion.SkeletonId);
        var layeringJson = Read("v4_layering_inputs.json");
        AlsAnimationUpdateGraphCompiler.Validate(layeringJson);
        var footIkJson = Read("v4_foot_ik_inputs.json");
        var footIkInput = AlsFootIkInputCompiler.Compile(footIkJson);
        var basePoses = AlsBasePosesCompiler.Compile(layeringJson, set, locomotion.SkeletonId);
        var characterMovement = AlsCharacterMovementCompiler.Compile(Read("v4_character_movement_inputs.json"), Read("v4_character_rotation_inputs.json"));
        var definition = new AlsMovementGraphDefinition(sources, binding, AlsGroundedMachineCompiler.CompileMovement(json).Movement!,
            AlsMainGroundedCachedGraphCompiler.Compile(json, cache, Read("v4_locomotion_detail_graph.json"), sources, set),
            AlsCrouchingCycleCompiler.Compile(json, cache, Read("v4_locomotion_curves.json"), Read("v4_lean_sampling.json"), sources, set),
            AlsGroundedPoseDependencyCompiler.Compile(Read("v4_grounded_dependencies.json"), set.Skeletons[locomotion.SkeletonId]),
            AlsAirPoseCompiler.Compile(json, cache, Read("v4_falling_lean_sampling.json"), sources, set),
            AlsLandingPoseCompiler.Compile(json, cache, sources, set), AlsBaseLayerCompiler.Compile(json, cache),
            inputCurves, functions, landing, AlsInAirUpdateCompiler.Compile(inputJson, inputCurves, functions, landing),
            groundedFunctions, groundedRates, defaults, AlsGroundedUpdateCompiler.Compile(inputJson, inputCurves, groundedFunctions, groundedRates, defaults),
            AlsJumpInputCompiler.Compile(inputJson, Read("v4_jump_event_inputs.json")),
            AlsYawOffsetCompiler.CompileGlobalControl(inputJson, Read("v4_yaw_inputs.json"), Read("v4_grounded_control_inputs.json")),
            AlsIdleControlInputCompiler.Compile(Read("v4_idle_control_inputs.json")),
            AlsTurnInPlaceCompiler.Compile(Read("v4_idle_control_inputs.json"), set, pose),
            turns, AlsTurnNotifyCompiler.Compile(Read("v4_turn_notify_inputs.json"), set, sources, binding, turns),
            actions, AlsAuthoredMontageCompiler.CompileRequests(actionProfile, actions),
            AlsMontageNotifyCompiler.Compile(Read("v4_recovery_action_notify_inputs.json"),Read("v4_turn_notify_inputs.json"),set,sources,binding,turns,actions))
            { RollDefinitionId = actionProfile.DemoCases.RollActionDefinitionId,
                GetUpSelection = AlsGetUpSelectionCompiler.Compile(Read("v4_get_up_selection_inputs.json"), set, actionProfile),
                OverlayOverrideNotifies = AlsOverlayOverrideNotifyCompiler.Compile(Read("v4_get_up_selection_inputs.json"),
                    Read("v4_recovery_action_notify_inputs.json"), set, actionProfile),
                GetUpFrontDefinitionId = actionProfile.Actions.Single(a => set.Montages[a.MontageId].StableId == "35b984a2215f8d238fed23d8322bea3267dfd93f").DefinitionId,
                GetUpBackDefinitionId = actionProfile.Actions.Single(a => set.Montages[a.MontageId].StableId == "5657e37ac49745e09e5a7c4ad1c46aaa6491cb7f").DefinitionId,
                CharacterBridge = AlsCharacterAnimationBridgeCompiler.Compile(Read("v4_character_animation_bridge.json")),
                CharacterRotation = AlsCharacterRotationCompiler.Compile(Read("v4_character_rotation_inputs.json")),
                CharacterMovement = characterMovement,
                CharacterMovementRuntime = AlsCharacterMovementCompiler.CompileRuntime(Read("v4_character_movement_runtime.json"), characterMovement),
                LayerBlending = AlsLayerBlendingCompiler.Compile(layeringJson),
                LayeringInput = AlsLayeringInputCompiler.Compile(layeringJson),
                AimingInput = AlsAimingInputCompiler.Compile(layeringJson, Read("v4_aiming_inputs.json")),
                AimPose = AlsAimPoseCompiler.Compile(layeringJson, Read("v4_aim_pose_inputs.json")),
                AimLayer = AlsAimLayerCompiler.Compile(layeringJson),
                HandIk = AlsHandIkCompiler.Compile(layeringJson),
                PelvisIkInput = footIkInput.Pelvis,
                FootIkInput = footIkInput,
                FootIk = AlsFootIkCompiler.Compile(footIkJson),
                RootPose = AlsRootPoseCompiler.Compile(layeringJson),
                RagdollPose = AlsRagdollPoseCompiler.Compile(Read("v4_ragdoll_inputs.json"), layeringJson, set),
                OverlaySources = AlsOverlaySourceCompiler.Compile(layeringJson, Read("v4_overlay_inputs.json"), set),
                BasePoses = basePoses,
                BasePoseRetarget = AlsBasePoseRetargetCompiler.Compile(Read("v4_base_poses_inputs.json"), set, basePoses),
                BasePoseSourceKeys = AlsBasePoseSourceKeysCompiler.Compile(Read("v4_base_pose_source_keys.json"), set, basePoses) };
        definition = definition with { AimSampling = AlsAimSamplingCompiler.Compile(Read("v4_aim_sampling.json"), definition.AimPose, set) };
        definition = definition with { OverlayStates = AlsOverlayStateCompiler.Compile(layeringJson, Read("v4_overlay_inputs.json"), set, definition.OverlaySources) };
        definition = definition with { OverlayTransitions = AlsOverlayTransitionCompiler.Compile(Read("v4_overlay_transition_inputs.json"), layeringJson,
            Read("v4_overlay_inputs.json"), set, definition.OverlayStates) };
        definition = definition with { OverlayTransitionAssets = AlsOverlayTransitionCompiler.CompileAssets(Read("v4_turn_montage_inputs.json"), set, definition.OverlayTransitions) };
        definition = definition with { StopTransitions = AlsStopTransitionCompiler.Compile(Read("v4_overlay_transition_inputs.json"), set,
            AlsGroundedMachineCompiler.CompileGrounded(Read("v4_grounded_dependencies.json"))) };
        definition = definition with { GroundedTransitionAssets = [.. definition.OverlayTransitionAssets,
                .. AlsStopTransitionCompiler.CompileAssets(Read("v4_turn_montage_inputs.json"), set, definition.StopTransitions)],
            GroundedTransitionNotifyJson = AlsStopTransitionCompiler.MergeNotifyMetadata(Read("v4_transition_notify_inputs.json"), Read("v4_stop_notify_inputs.json")) };
        definition = definition with { MontageNotifies = AlsMontageNotifyCompiler.Compile(Read("v4_recovery_action_notify_inputs.json"), Read("v4_turn_notify_inputs.json"),
            set, sources, binding, turns, actions, definition.GroundedTransitionAssets, definition.GroundedTransitionNotifyJson) };
        definition = definition with { OverlayClocks = AlsOverlaySyncCompiler.Compile(Read("v4_overlay_sync_inputs.json"), Read("v4_overlay_inputs.json"), definition.OverlaySources, set) };
        var sharedSources = AlsOverlaySharedSourceCompiler.Compile(sources, definition.OverlaySources, definition.OverlayClocks,
            Read("v4_overlay_inputs.json"), Read("v4_overlay_notify_inputs.json"), set);
        definition = definition with { OverlaySharedSources = sharedSources,
            OverlaySharedBinding = AlsLocomotionGraphBuilder.CompileSourceBindings(set, locomotion, pose, sharedSources.Sources) };
        definition = definition with { OverlayPose = AlsOverlayPoseCompiler.Compile(layeringJson, Read("v4_overlay_inputs.json"), set, definition.OverlaySources, definition.OverlayStates) };
        var ragdollFrame = AlsRagdollFrameCompiler.Compile(Read("v4_ragdoll_inputs.json"), layeringJson, inputJson, definition.RagdollPose, set);
        var rootSources = AlsRootSharedSourceCompiler.Compile(sharedSources, definition.RagdollPose, ragdollFrame, set);
        definition = definition with { RagdollFrame = ragdollFrame, MannequinMeshId = locomotion.MannequinMeshId,
            RootSharedSources = rootSources, RootSharedBinding = AlsLocomotionGraphBuilder.CompileSourceBindings(set, locomotion, pose, rootSources.Sources) };
        definition = definition.WithActionPlayback(set) with { GroundedEntryNotify = AlsGroundedEntryNotifyCompiler.Compile(
            Read("v4_grounded_notify_semantics.json"), Read("v4_overlay_transition_inputs.json"), set,
            AlsGroundedMachineCompiler.CompileMovement(json), actionProfile) };
        if (!loadRawSources) return definition;
        var rawSources = AlsRawAnimationSourceLoader.Load(set, definition);
        var aimSources = AlsRawAnimationSourceLoader.LoadAim(set, definition.AimSampling).ReuseResourcesFrom(rawSources);
        var overlaySources = AlsRawAnimationSourceLoader.LoadOverlay(set, definition.OverlaySources)
            .ReuseOverlappingResourcesFrom(rawSources).ReuseOverlappingResourcesFrom(aimSources);
        var ragdollSources = AlsRawAnimationSourceLoader.LoadRagdoll(set, definition.RagdollPose).ReuseOverlappingResourcesFrom(rawSources);
        var stopSources = AlsRawAnimationSourceLoader.LoadStop(set, definition.StopTransitions).ReuseOverlappingResourcesFrom(rawSources);
        return definition with { RawSources = rawSources, AimRawSources = aimSources, OverlayRawSources = overlaySources,
            RagdollRawSources = ragdollSources, StopRawSources = stopSources };
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
}

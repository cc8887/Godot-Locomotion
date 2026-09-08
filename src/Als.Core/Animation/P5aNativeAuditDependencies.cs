using System.Text.Json.Nodes;

namespace GodotAls.Core.Animation;

internal static class P5aNativeAuditDependencies
{
    // Frozen from the complete 2026-09-08 native discovery, independently package-hash checked.
    internal static JsonArray Create() => new(
        Asset("/ALS/ALS/Animations/Base/A_Als_Idle.A_Als_Idle",
            "ceaa927b6490d1888218c3bb6ce29f366ef7c3a6",
            "158a1b25e84dad022c542bb1110c5437365c05a68cc30baaa3735f31852a5335",
            "/Script/Engine.AnimSequence"),
        Asset("/ALS/ALS/Animations/Grounded/Lean/BS_Als_Lean.BS_Als_Lean",
            "35e22ed712645c6d57b7371befdcc030d907e88f",
            "2ead99c309d3cdfef5eca07531b6b66dd5ae8a9c6e424356bcedd11d392fed3b",
            "/Script/Engine.BlendSpace"),
        Asset("/ALS/ALS/Animations/Grounded/WalkRun/BS_Als_WalkRun_Forward.BS_Als_WalkRun_Forward",
            "bb00e88951be3325f13775800fd63251b9724b91",
            "268d460ed37f4160e5aaef7472f7bf9fcaea1572ee854dbceded02661ec551b9",
            "/Script/Engine.BlendSpace"),
        Asset("/ALS/ALS/Animations/Overlays/Other/A_Als_Default_Poses.A_Als_Default_Poses",
            "1ad662f4a2fc8d7c9419eeaef260381b73cf6bf9",
            "65be815622ed239f3d6307c1c4334d41d4ca761e3a43f88776bd5c8fc74317c6",
            "/Script/Engine.AnimSequence"),
        Asset("/ALS/ALS/Animations/Transitions/A_Als_CrouchToStand.A_Als_CrouchToStand",
            "b2e6ea0e7ce74ededb53549b700b00f6838280bc",
            "e808b588310dfb336578f77fb049ad5fd23cec11f6cdebd3d9e552a5826ccead",
            "/Script/Engine.AnimSequence",
            Footstep("b8fb558141cba57d764453522a35a2e41d14f48c", 0, .6000000238418579f),
            Footstep("54b1f5d2f42f2c4e347e3b2f0daf2df277773a8c", 1, .9000000357627869f)),
        Asset("/ALS/ALS/Animations/Transitions/A_Als_StandToCrouch.A_Als_StandToCrouch",
            "b5235cd25824e3fd20254e51c87b50e944504ffd",
            "80a849aa62a4e33c1acd2b3aa2b9d3c3c375ff4a87dcf4c51d6b683587169234",
            "/Script/Engine.AnimSequence",
            Footstep("c1bad251b4a8b5c278b2b176c0511e57927efba7", 0, .4333333671092987f),
            Footstep("d7f672e5d8d58833394ff17bbf23f8bf40d4adae", 1, .7333333492279053f)),
        Asset("/ALS/ALS/Animations/Transitions/A_Als_Stand_Transition_Right.A_Als_Stand_Transition_Right",
            "4f945227a5e8871503adebf5ba655b1f6496a91c",
            "5c9f4142ac02511b6fd41ca11589af827540d5f28a02a44ea81f26bab9310de4",
            "/Script/Engine.AnimSequence",
            Footstep("31009e422b1cc9903e0d851dd26d730acd39e802", 0, .8333333134651184f),
            Footstep("3b22dbb668b87ea06ce5b7c593a6b3d6acb42dcd", 1, 1.3666666746139526f)),
        Asset("/ALS/ALS/Animations/Transitions/A_Als_Stop_Left.A_Als_Stop_Left",
            "d465ed267486e89f76da56103c64c16dd91dadd8",
            "5fb1488498248f962d7e441c249db07ce8f2c0b49eb0833a162b09b6b7bf2684",
            "/Script/Engine.AnimSequence",
            Footstep("7df629e5830201a5308443184652a050529411ef", 0, .8333332538604736f),
            Footstep("51b2b24f1f74a61ee88e2589d9beca46759b7340", 1, 1.366666555404663f)),
        Asset("/ALS/ALS/Animations/View/BS_Als_Look.BS_Als_Look",
            "5d8b03a91dec28796e444b4f9c5646da8dcd6a51",
            "8e7b16f6919fa7541168ab44cc2f716831e533e471059712cac378fc163c6c73",
            "/Script/Engine.BlendSpace1D"));

    private static JsonObject Asset(string path, string id, string hash, string classPath,
        params JsonObject[] events) => new()
    {
        ["assetObjectPath"] = path,
        ["assetStableId"] = id,
        ["assetPackageSha256"] = hash,
        ["assetClassPath"] = classPath,
        ["events"] = new JsonArray(events.Select(value => (JsonNode)value).ToArray()),
    };

    private static JsonObject Footstep(string id, int index, float time) => new()
    {
        ["stableEventId"] = id,
        ["sourceClassPath"] = "/Script/ALS.AlsAnimNotify_FootstepEffects",
        ["sourceIndex"] = index,
        ["trackIndex"] = 0,
        ["timeSeconds"] = time,
        ["durationSeconds"] = 0f,
        ["triggerWeightThreshold"] = .3f,
        ["tickMode"] = "Queued",
    };
}

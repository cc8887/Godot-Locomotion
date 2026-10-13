# ALS character model binding

The ordinary ALS demo selects a presentation model with
`assets/config/als_character_model.json`. The reserved `mannequin` selection
uses the active animation pack's original mesh; configured selections name
Godot scenes. `--als-model=<name>` overrides the default for one process.

In the shared demo, `character_model_toggle` (M) switches the ALS player between
`girl` and `mannequin`. `DemoLocomotionSwitcher` queues the request until the
existing committed handoff boundary, including completion of ragdoll recovery.
It reuses `ReplaceDemoPlayer` with a forked context containing the requested
model. The incoming player rebuilds its model and sole geometry together;
world feet, yaw, velocity, stance and look input survive the handoff. Animation
history initializes with the new player, as in the ALS/Lyra handoff. Preparation
failure resumes the retained player. Key echoes are ignored, Lyra model selection
is unaffected, and the default configuration is not rewritten.

Model selection belongs to the Godot character configuration. It is copied
into spare workers and player handoff contexts. `AlsCharacterModel.Install`
runs before graph construction, warmup and worker admission. Animation resources,
the character clock, logical/virtual bones, physics and commit/cancel ownership
remain on the existing pipeline. Lyra model selection is independent.

A model scene must contain one skeleton and directly attached skinned meshes.
Its physical bone count, exact names/order and parents must match ALS. Every
skin binding must resolve. An identical-reference model transfers its meshes
onto the original skeleton. A `preserve-proportions-v1` model retains a separate
presentation skeleton with ALS reference axes and its own reference translations.
Animation players, trees and skeleton modifiers are rejected on the model.
Compatibility is checked before removing the working meshes; matching bone
names alone does not establish pose compatibility.

The B53 authoring FBX retains the source rest pose. `rebind_als_girl.gd` defaults
to `girl_high_als_retarget.tscn`: align character orientation and reference-pose
directions using rotations and one uniform size conversion, preserve every
anatomical bone length ratio, and rebuild axes/inverse binds for the model's own
skeleton. Normals/tangents follow those transforms. Twist weights remain active;
IK helpers remain unweighted. `--fit-mannequin` reproduces the earlier diagnostic
mesh fit, which changed proportions and is no longer the default.

`AlsSkeletonPoseRetargetModel` in pure .NET Core reuses the existing precise
reference retarget and two-bone IK. Root/pelvis and IK helpers retain
reference-relative motion; anatomical bones retain target translations and
scale. Foot end effectors
follow the ALS contact pose with the model's actual leg lengths and no stretching.
Unreachable targets clamp to reach. References must already share bone axes.

Mantle supplies the candidate's left/right contact weights to conditional arm
chains in this same Core mapper. Their end positions and palm rotations follow
the already solved driver effectors, using the target's immutable upper/lower
arm lengths. Zero actual contact weight does not lock the hand. The stateless
mapping restores the ordinary pose; the transactional path may prepare or
release its body correction using explicit character-owned state. The mapper
reports the actual reach residual.

For a shorter arm or higher shoulder, arm IK alone can be unreachable. An
optional common contact root permits body translation within a bound derived
from the source/target reference-translation differences along those limbs and
their ancestors. Bounded projection into the active reach spheres moves the
body; the ordinary leg solver then restores foot goals before arm solving.
This corrects presentation proportions without stretching bones or moving the
character motor/capsule. The correction and its reference bound are observable;
infeasible targets retain a residual and fail contact acceptance. This mechanism
does not adapt ragdoll collision bodies or certify body-motion aesthetics.

Full limb length defines hard reach; the source pose's current elbow bend and
the target's actual two segment lengths define a softer preferred radius.
The authored bend plane is transported to the target reach direction. This
avoids manufacturing a straight support arm from a bent input without forcing
every style to one elbow angle. Contact, the original correction budget and
actual body clearance take priority; conflicting preferences retain measured
bend loss and genuine infeasibility remains visible.

The binding imports immutable body skin points whose complete influence sets
translate with the contact root and are not changed by limb IK. Core skins
these points in the candidate pose and projects body translation against the
current ledge's front/top exterior, the hard arm reach balls and the same
budget. This is a trunk/head subset; mixed limb vertices, hands and legs still
require independent full-skin acceptance.

Each hand's acquisition interval comes from its existing contact curve. A
0.2-character-second C2 preparation envelope uses the same montage time and
rate, current source pose and fixed future anchor. It plans body position
without advancing another animation graph or enabling the hand early. Release
reuses `AlsInertialDecay` before hard contact/geometry projection. The explicit
`AlsRetargetContactState` is stored in the existing `AlsRuntimeState`, copied to
the same frame candidate and committed/discarded with the character. The 0.2 s
response is a presentation policy, not a universal biomechanical threshold.

The mapper has no calibrated wrist joint limits.
`AlsTwistBoneReconstruction` adds half of the IK-induced axial roll
to the eight weighted leaf branches while retaining incoming authored twist.
It runs after local pose reconstruction inside the same candidate; zero roll
change preserves the original value. Driver Mantle hand IK uses the same Core
policy before proportional model adaptation. It does not distribute wrist swing.
Passing endpoint,
bone-length and skin-penetration checks does not certify support joint posture
or wrist deformation. See the [High Mantle wrist diagnosis](../verification/2026-10-12-mantle-high-wrist-twist-diagnosis.md).
The production policy and actual twist verification are recorded in
[physical twist reconstruction](../verification/2026-10-12-mantle-twist-reconstruction.md).

This foot mapping does not constrain rolling contacts at the head, back or hands.
Preserving target proportions can change those support positions even when arm
rotations match the driver. Rolling acceptance must separately observe the
target's actual skin and support phases; foot matching alone cannot certify it.
See [rolling contact diagnosis](../verification/2026-10-12-roll-arms-drop-contact-diagnosis.md).

The Godot binding converts and validates the complete candidate before writing
the presentation skeleton in the original visual worker. Its original pose
checkpoint now also captures the actual model's local position/rotation/scale
and presentation diagnostics. Rollback restores that checkpoint exactly and
checks it before allowing a retry; it does not re-evaluate a prior physical
display with animation IK. Ragdoll uses contact matching disabled and clears
the contact transition state. Recovery cancellation uses the same cancellation
signal as the animation owner. Model contacts share the existing character
clock, worker checkpoint and commit/cancel transaction.
`PhysicalDisplaySkeleton` remains the ALS animation/physics driver, while
`PresentationSkeleton` identifies the actual skin. The model has no process,
animation sampler, second clock or independent commit chain.

Foot support geometry evaluates this same Core mapping inside the existing foot
contact solver. Imported support vertices retain the presentation model's inverse
binds; logical references cross the FBX meters/native centimeters boundary before
mapping. Independently observed sole quality always skins the published model.

Game acceptance must observe committed displayed poses and actual skin
vertices. Split-foot startup/recovery may commit an initial hidden cold pose;
generation replacement also classifies a retired result before recovery.
These phases follow the existing character transaction and cannot be treated
as displayed frames. Model changes do not certify the selected animation pack,
full-action deformation quality or platform performance.

Evidence and reproducible commands:
[model production and game verification](../verification/2026-10-11-girl-als-model-import.md)
and [Mantle Root/model contact verification](../verification/2026-10-12-mantle-root-model-contact.md),
plus [authoring tools](../../tools/models/README.md).

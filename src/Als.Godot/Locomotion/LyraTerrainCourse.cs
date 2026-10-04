using System.Text.Json;
using Godot;
using GodotAls.Animation.Lyra;

namespace GodotAls.Locomotion;

// Visible geometry and scene observations. Movement, animation and Rig remain
// the ordinary player/NPC services; this class supplies no recorded contacts.
internal sealed class LyraTerrainCourse : IDisposable
{
    private sealed class Role
    {
        public int Steps, Slopes, Air, Drops, Landings, Crouch, Ads;
        public float Height;
        public bool WasGrounded;
        public readonly HashSet<string> Profiles = [];
    }
    private readonly Node3D _scene;
    private readonly LyraSceneCharacter? _npc;
    private readonly Role[] _roles = [new(), new()];
    private readonly List<object> _rows = [], _captures = [];
    private readonly HashSet<string> _captured = [];
    private readonly string? _report, _captureDirectory;
    private readonly int _hz;
    private readonly Camera3D? _camera;
    private string? _pendingCapture;
    private LyraCharacterAnimation? _player;
    private CharacterBody3D? _body;
    private int _frame, _retries;
    public bool Smoke { get; }
    public bool Done { get; private set; }
    private static readonly string[] CaptureNames = ["overview", "step", "slope", "crouch", "drop", "landing", "rifle", "pistol", "jump"];
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    public LyraTerrainCourse(Node3D scene, LyraLocomotionResources resources, LyraMontageCatalog catalog,
        bool smoke, string? report, string? captures, int hz)
    {
        _scene = scene; Smoke = smoke; _report = report; _captureDirectory = captures; _hz = hz;
        if (smoke)
        {
            Require(hz is 30 or 60 or 120 && report is not null && Path.IsPathFullyQualified(report) && !File.Exists(report),
                "Terrain run requires a new absolute report path and 30/60/120 Hz.");
            Engine.PhysicsTicksPerSecond = hz;
        }
        if (captures is not null)
        {
            Require(smoke && DisplayServer.GetName() != "headless" && Path.IsPathFullyQualified(captures) && !Directory.Exists(captures),
                "Terrain capture requires a rendered run and a new absolute directory.");
            Directory.CreateDirectory(captures);
        }
        for (int lane = 0; lane < 2; lane++)
        {
            float x = lane * 4;
            var color = lane == 0 ? new Color(.42f, .55f, .66f) : new Color(.62f, .49f, .32f);
            Box(new(x, .125f, -2.4f), new(2.8f, .25f, 2), color);
            float angle = MathF.Atan(.2f);
            Box(new(x, .55f - .1f * MathF.Cos(angle), -4.9f - .1f * MathF.Sin(angle)),
                new(2.8f, .2f, 3 / MathF.Cos(angle)), color.Lightened(.12f), angle);
            Box(new(x, .425f, -7.4f), new(2.8f, .85f, 2), color.Darkened(.12f));
            Label(new(x, .55f, -1.4f), "25 cm 台阶");
            Label(new(x, 1.25f, -5.2f), "斜坡");
            Label(new(x, 1.4f, -8.4f), "85 cm 落差");
        }
        Box(new(-4, .3f, -2.4f), new(2.8f, .6f, 2), new(.65f, .3f, .26f));
        Label(new(-4, .9f, -1.4f), "60 cm 阻挡");
        if (!smoke) return;
        _npc = new(scene, resources, catalog, "TerrainPistol", new(4, .92f, 0), "pistol");
        if (captures is not null)
        {
            _camera = new Camera3D { Name = "TerrainCapture", Current = true, Fov = 55 };
            scene.AddChild(_camera);
            RenderingServer.FramePostDraw += Capture;
        }
    }
    private void Box(Vector3 position, Vector3 size, Color color, float angle = 0)
    {
        var body = new StaticBody3D { Position = position, Rotation = new(angle, 0, 0), CollisionLayer = 5, CollisionMask = 2 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Margin = 0, Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = .8f } });
        _scene.AddChild(body);
    }
    private void Label(Vector3 position, string text) => _scene.AddChild(new Label3D
    { Position = position, Text = text, FontSize = 36, PixelSize = .004f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });

    public void DriveInput(CharacterBody3D body, int frame)
    {
        double t = (double)frame / _hz;
        foreach (var action in new[] { "move_forward", "walk", "aim", "jump", "crouch_toggle", "switch_item_layer", "lyra_fire", "lyra_reload" })
            Input.ActionRelease(action);
        void Press(string action, bool value) { if (value) Input.ActionPress(action); }
        Press("move_forward", t >= .35 && t < 9.5 && body.Position.Z > -9.6f);
        Press("walk", true); Press("aim", t >= 9.5 && t < 12);
        Press("crouch_toggle", frame == _hz * 18 / 10 || frame == _hz * 33 / 10);
        Press("switch_item_layer", frame == _hz * 105 / 10 || frame == _hz * 106 / 10 || frame == _hz * 115 / 10);
        Press("jump", frame == _hz * 125 / 10);
        Press("lyra_fire", frame == _hz / 10 || frame == _hz * 109 / 10);
        Press("lyra_reload", frame == _hz / 5 || frame == _hz * 112 / 10);
    }
    public void Advance(LyraCharacterAnimation player, CharacterBody3D body, LyraCharacterMovementSettings settings, float delta, int frame)
    {
        if (!Smoke || Done) return;
        _player = player; _body = body; _frame = frame;
        double t = (double)frame / _hz;
        var npc = _npc!;
        if (frame == _hz / 10 || frame == _hz * 109 / 10) npc.Animation.RequestWeaponAction(false);
        if (frame == _hz / 5 || frame == _hz * 112 / 10) npc.Animation.RequestWeaponAction(true);
        if (frame == _hz * 105 / 10) npc.Animation.Rebind("rifle");
        if (frame == _hz * 115 / 10) npc.Animation.Rebind("pistol");
        var observation = npc.Move(new(t >= .35 && t < 9.5 && npc.Body.Position.Z > -9.6f ? new(0, -1) : Vector2.Zero,
            0, 0, t >= 1.8 && t < 3.3, t >= 9.5 && t < 12, true, frame == _hz * 125 / 10), delta);
        var physical = npc.Body.GlobalTransform;
        var first = observation.Prepare(npc.Animation, delta); var pose = first.Output.Pose.ToArray();
        npc.Animation.Cancel(); var retry = observation.Prepare(npc.Animation, delta);
        Require(pose.SequenceEqual(retry.Output.Pose), "Terrain NPC animation retry differs.");
        npc.Animation.Commit(retry); _retries++;
        Require(npc.Body.GlobalTransform == physical && npc.Animation.CapsuleMoves == frame + 1, "Terrain retry repeated physical movement.");
        Observe(0, player, body, settings, t); Observe(1, npc.Animation, npc.Body, npc.MovementSettings, t);
        SelectCapture(t);
        if (_camera is not null)
        {
            bool grip = _pendingCapture is "rifle" or "pistol";
            _camera.GlobalPosition = body.GlobalPosition + (grip ? new Vector3(1.15f, .45f, -1.4f) : new Vector3(2.7f, .75f, 2.6f));
            _camera.LookAt(body.GlobalPosition + (grip ? new Vector3(0, .1f, -.25f) : new Vector3(0, -.15f, 0)));
        }
        if (frame + 1 == _hz * 15) Finish(player);
    }
    private void Observe(int role, LyraCharacterAnimation animation, CharacterBody3D body, LyraCharacterMovementSettings settings, double t)
    {
        var s = _roles[role]; var movement = animation.LastMovement;
        Require(animation.Binding.PublishedFrames == _frame + 1 && animation.SourceNotifies.CommittedFrame == _frame,
            "Terrain role lost its ordinary publication/notify frame.");
        s.Profiles.Add(animation.Profile); s.Steps += movement.Stepped ? 1 : 0;
        s.Slopes += movement.Grounded && movement.FloorAdjustment.Floor.Hit.Normal.Y is > .8f and < .99f ? 1 : 0;
        s.Air += !movement.Grounded ? 1 : 0;
        s.Drops += !movement.Grounded && body.Position.Z < -8.3f && t < 12 ? 1 : 0;
        s.Landings += movement.Grounded && !s.WasGrounded && _frame > 0 ? 1 : 0; s.WasGrounded = movement.Grounded;
        s.Crouch += animation.Observation.Crouching ? 1 : 0; s.Ads += animation.Observation.Ads ? 1 : 0;
        float half = animation.Observation.Crouching ? settings.CrouchedHalfHeight : settings.StandingHalfHeight;
        if (movement.Grounded) s.Height = Math.Max(s.Height, body.Position.Y - half);
        var skin = animation.Binding.Skeleton;
        object Foot(string name)
        {
            int bone = skin.FindBone(name); Require(bone >= 0, "Missing ALS terrain foot.");
            var point = (skin.GlobalTransform * skin.GetBoneGlobalPose(bone)).Origin;
            using var query = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * .5f, point - Vector3.Up * 2, 5);
            var hit = body.GetWorld3D().DirectSpaceState.IntersectRay(query);
            var floor = hit.Count > 0 ? hit["position"].AsVector3() : Vector3.Zero;
            return new { position = V(point), hit = hit.Count > 0, floor = V(floor), gap = point.Y - floor.Y };
        }
        _rows.Add(new { frame = _frame, role, animation.Profile, position = V(body.Position), movement.Grounded,
            movement.Stepped, state = animation.Host.Main.Machine.State, crouch = animation.Observation.Crouching,
            slopeNormal = V(movement.FloorAdjustment.Floor.Hit.Normal), left = Foot("foot_l"), right = Foot("foot_r"),
            rigQueries = animation.PhysicsQueries, weaponPublications = animation.Weapons.Publications });
    }
    private static float[] V(Vector3 vector) => [vector.X, vector.Y, vector.Z];
    private void SelectCapture(double t)
    {
        if (_captureDirectory is null || _pendingCapture is not null) return;
        var movement = _player!.LastMovement;
        void Pick(string name, bool condition) { if (_pendingCapture is null && condition && !_captured.Contains(name)) _pendingCapture = name; }
        Pick("overview", t >= .25); Pick("step", movement.Stepped);
        Pick("slope", movement.Grounded && movement.FloorAdjustment.Floor.Hit.Normal.Y is > .8f and < .99f);
        Pick("crouch", t >= 2.2 && _player.Observation.Crouching); Pick("drop", !movement.Grounded && _body!.Position.Z < -8.3f && t < 12);
        Pick("landing", movement.Grounded && _roles[0].Drops > 0 && t < 12);
        Pick("rifle", t >= 9.6 && _player.Profile == "rifle"); Pick("pistol", t >= 10.8 && _player.Profile == "pistol");
        Pick("jump", t >= 12.6 && !movement.Grounded);
    }
    private void Capture()
    {
        if (Done || _pendingCapture is not { } name || _captureDirectory is null) return;
        try
        {
            using var image = _scene.GetViewport().GetTexture().GetImage();
            Require(image.SavePng(Path.Combine(_captureDirectory, name + ".png")) == Error.Ok, "Terrain frame save failed.");
            _captures.Add(new { name, physicsFrame = _frame, tick = Engine.GetPhysicsFrames(), drawn = Engine.GetFramesDrawn(),
                profile = _player!.Profile, state = _player.Host.Main.Machine.State, position = V(_body!.Position) });
            _captured.Add(name); _pendingCapture = null;
        }
        catch (Exception e) { Done = true; GD.PushError("Terrain capture failed: " + e); _scene.GetTree().Quit(1); }
    }
    private void Finish(LyraCharacterAnimation player)
    {
        foreach (var s in _roles) Require(s.Steps > 0 && s.Slopes > 0 && s.Drops > 0 && s.Landings >= 2 && s.Crouch > 0 && s.Ads > 0 && s.Height > .8f,
            $"Incomplete ordinary terrain path: steps={s.Steps} slopes={s.Slopes} drop={s.Drops} land={s.Landings} height={s.Height}.");
        Require(_roles.All(s => s.Profiles.Contains("pistol") && s.Profiles.Contains("rifle")) &&
            player.WeaponNotifies.Played > 0 && _npc!.Animation.WeaponNotifies.Played > 0, "Terrain equipment did not execute.");
        Require(_captureDirectory is null || _captured.SetEquals(CaptureNames), "Missing actual terrain/near captures.");
        if (_captureDirectory is not null)
        {
            using var frames = new FileStream(Path.Combine(_captureDirectory, "frames.json"), FileMode.CreateNew);
            JsonSerializer.Serialize(frames, new { phase = "FramePostDraw", frames = _captures });
        }
        using var stream = new FileStream(_report!, FileMode.CreateNew);
        JsonSerializer.Serialize(stream, new { hz = _hz, frames = _frame + 1, actors = 2, retries = _retries,
            actualJolt = true, ordinaryMovement = true, completeMain = true, finalRig = true, skinBones = 68, logicalBones = 81,
            movementPassed = true, hardwareKeyboardAccepted = false, visualAccepted = false,
            roles = _roles.Select(s => new { s.Steps, s.Slopes, s.Drops, s.Landings, s.Crouch, s.Ads, s.Height, profiles = s.Profiles.ToArray() }),
            captures = _captures, rows = _rows });
        Done = true; GD.Print($"LYRA_TERRAIN_COURSE_GODOT_OK hz={_hz} frames={_frame + 1} actors=2 captures={_captures.Count}"); _scene.GetTree().Quit();
    }
    public void Dispose()
    { if (_camera is not null) RenderingServer.FramePostDraw -= Capture; _npc?.Dispose(); }
}

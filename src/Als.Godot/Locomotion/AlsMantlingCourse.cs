using Godot;

namespace GodotAls.Locomotion;

internal static class AlsMantlingCourse
{
    internal static void Add(Node3D world)
    {
        var floor = world.GetNode<CollisionShape3D>("StartFloor/CollisionShape3D");
        var groundY = floor.Shape is BoxShape3D shape ? floor.GlobalPosition.Y + shape.Size.Y * floor.GlobalBasis.Scale.Y * .5f : 0;
        var course = new Node3D { Name = "MantlingCourse", Position = new(-15, groundY, -6) };
        world.AddChild(course);
        for (var i = 0; i < 3; i++)
        {
            var height = new[] { .75f, 1.35f, 2f }[i];
            var size = new Vector3(3.5f, height, 3);
            var body = new StaticBody3D { Name = $"Ledge{i + 1}", Position = new(-i * 5, height / 2, 0) };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.16f + i * .1f, .45f, .55f), Roughness = .85f } });
            body.AddChild(new Label3D { Text = $"攀爬 / Mantle  {height:0.00} m\n靠近边缘按空格", Position = new(0, height / 2 + .65f, 0),
                FontSize = 38, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
            course.AddChild(body);
        }
    }
}

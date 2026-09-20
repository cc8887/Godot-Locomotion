using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// Explicit diagnostic ownership: Core integrates; Godot bodies are frozen,
// non-colliding pose proxies. Never attach Jolt joints or enable gameplay with
// this host without full acceptance. Contacts use the same Core solver buffers.
internal sealed class AlsCoreJointHost
{
    private readonly AlsPhysicsBodySet _proxies;
    private readonly AlsPrecisePose[] _massLocal;
    internal AlsJointIsland Island { get; }

    internal AlsCoreJointHost(AlsPhysicsBodySet proxies, AlsRagdollPhysicsDefinition definition, AlsJointIsland island)
    {
        _proxies = proxies; Island = island;
        if (proxies.BodyCount != definition.Bodies.Length || island.BodyCount < proxies.BodyCount)
            throw new ArgumentException("Core and proxy body counts differ.");
        _massLocal = definition.Bodies.Select(b => b.MassLocal).ToArray();
        CheckOwnership(); Publish();
    }

    internal void Step(double dt)
    {
        CheckOwnership(); Island.StepForceFree(dt); Publish();
    }

    // Registered environment bodies may follow the contiguous asset-body prefix.
    internal void Step(double dt, AlsDoubleVector gravity, IAlsIslandContacts contacts)
    {
        CheckOwnership(); Island.Step(dt, gravity, contacts: contacts); Publish();
    }
    internal void Step(double dt, AlsDoubleVector gravity, IAlsIslandContacts contacts, ReadOnlySpan<AlsBodyStepForces> forces)
    {
        CheckOwnership(); Island.Step(dt, gravity, forces, contacts); Publish();
    }
    internal void StepScene(double dt, AlsDoubleVector gravity, IAlsIslandContacts contacts, ReadOnlySpan<AlsIslandKinematicTarget> targets)
    {
        CheckOwnership(); Island.Step(dt, gravity, contacts: contacts, kinematicTargets: targets); Publish();
    }

    private void CheckOwnership()
    {
        if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Physics proxy access requires Main.");
        if (_proxies.Active) throw new InvalidOperationException("Core and Jolt cannot both own body integration.");
        for (var i = 0; i < _proxies.BodyCount; i++)
        {
            var proxy = _proxies.BodyAt(i);
            if (!proxy.Freeze || proxy.FreezeMode != RigidBody3D.FreezeModeEnum.Static || proxy.CollisionLayer != 0 || proxy.CollisionMask != 0)
                throw new InvalidOperationException("Core probes require frozen proxies with both collision filters cleared.");
        }
    }

    private void Publish()
    {
        for (var i = 0; i < _proxies.BodyCount; i++)
        {
            var transform = AlsPhysicsBodySet.NativeToFbx(AlsPrecisePose.Compose(_massLocal[i], Island.BodyAt(i).Actor));
            var body = _proxies.BodyAt(i);
            body.GlobalTransform = transform;
            PhysicsServer3D.BodySetState(body.GetRid(), PhysicsServer3D.BodyState.Transform, transform);
        }
    }
}

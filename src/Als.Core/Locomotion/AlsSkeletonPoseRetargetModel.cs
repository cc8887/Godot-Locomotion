namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRetargetLimb(int Upper, int Joint, int End);
public readonly record struct AlsRetargetContactMarker(int End, AlsDoubleVector SourceLocal, AlsDoubleVector TargetLocal);

// References share bone axes after authoring, but retain independent bone lengths.
// This maps a caller-owned pose inside its existing frame transaction.
public sealed class AlsSkeletonPoseRetargetModel
{
    private readonly int[] _parents;
    private readonly AlsPrecisePose[] _reference, _candidate, _sourceComponents, _targetComponents, _localScratch,
        _componentInput, _componentLocal;
    private readonly bool[] _changed;
    private readonly AlsRetargetLimb[] _limbs, _conditionalLimbs;
    private readonly AlsRetargetContactMarker[] _markers;
    private readonly int _contactRoot;
    private readonly AlsPrecisePoseRetargetModel _retarget;
    private readonly AlsTwistBoneReconstruction? _twists;
    private readonly AlsPrecisePose[] _beforeIk;
    private readonly AlsDoubleVector[] _contactCenters, _projectionCorrections;
    private readonly double[] _contactRadii, _preferredContactRadii;
    private readonly bool[] _activeContacts;
    private readonly AlsDoubleVector[] _preparationCenters;
    private readonly double[] _preparationRadii, _preparationWeights;
    private readonly AlsRetargetBodyGeometry? _bodyGeometry;
    private readonly double[] _bodyRequiredUp, _bodyRequiredOut;
    private const int MaximumBodyProjections = 64;
    private readonly BodyProjectionNode[] _bodyQueue = new BodyProjectionNode[MaximumBodyProjections+1];
    private readonly double[] _visitedBodyUp = new double[MaximumBodyProjections], _visitedBodyOut = new double[MaximumBodyProjections];
    private AlsDoubleVector _bodyUpNormal, _bodyOutNormal;
    private readonly record struct BodyProjectionNode(double Up,double Out,AlsDoubleVector Correction,double Cost,int Witness);
    public int BoneCount => _parents.Length;
    public double MaximumConditionalResidual { get; private set; }
    public double MaximumConditionalBendLossDegrees { get; private set; }
    public double MaximumContactRootCorrection { get; }
    public AlsDoubleVector ContactRootCorrection { get; private set; }
    public double MaximumBodyPenetrationCm { get; private set; }
    public double MaximumBodyClearanceResidualCm { get; private set; }
    public int BodyGeometryPointCount => _bodyGeometry?.PointCount ?? 0;
    public int BodyProjectionCount { get; private set; }
    public double PreparationWeight { get; private set; }
    public double MaximumPreparationSourceReachExcessCm { get; private set; }
    public double MaximumPreparationStyleResidualCm { get; private set; }

    public AlsSkeletonPoseRetargetModel(ReadOnlySpan<int> parents, ReadOnlySpan<AlsPrecisePose> sourceReference,
        ReadOnlySpan<AlsPrecisePose> targetReference, ReadOnlySpan<int> translatedBones, ReadOnlySpan<AlsRetargetLimb> limbs,
        ReadOnlySpan<AlsRetargetLimb> conditionalLimbs = default, int contactRoot = -1,
        ReadOnlySpan<string> boneNames = default, ReadOnlySpan<AlsRetargetContactMarker> contactMarkers = default,
        AlsRetargetBodyGeometry? bodyGeometry = null)
    {
        if (parents.IsEmpty || sourceReference.Length != parents.Length || targetReference.Length != parents.Length)
            throw new ArgumentException("Incomplete compatible-skeleton references.");
        var modes = new int[parents.Length];
        var presence = new bool[parents.Length];
        var mapping = new int[parents.Length];
        for (var bone = 0; bone < parents.Length; bone++)
        {
            sourceReference[bone].Validate(); targetReference[bone].Validate();
            if (parents[bone] < -1 || parents[bone] >= bone ||
                1 - System.Math.Abs(AlsQuaternion.Dot(sourceReference[bone].Rotation, targetReference[bone].Rotation)) > 1e-5 ||
                sourceReference[bone].Scale.X <= 0 || sourceReference[bone].Scale.Y <= 0 || sourceReference[bone].Scale.Z <= 0 ||
                targetReference[bone].Scale.X <= 0 || targetReference[bone].Scale.Y <= 0 || targetReference[bone].Scale.Z <= 0)
                throw new ArgumentException("Retarget references require parent-first bones and aligned positive-scale axes.");
            mapping[bone] = bone; modes[bone] = 1; presence[bone] = true;
        }
        foreach (var bone in translatedBones)
        {
            if ((uint)bone >= parents.Length) throw new ArgumentException("Invalid translated retarget bone.");
            modes[bone] = 3;
        }
        foreach (var limb in limbs)
            ValidateLimb(limb, parents);
        foreach (var limb in conditionalLimbs)
            ValidateLimb(limb, parents);
        _parents = parents.ToArray(); _reference = targetReference.ToArray(); _limbs = limbs.ToArray();
        _conditionalLimbs = conditionalLimbs.ToArray();
        _markers = contactMarkers.ToArray();
        var marked = new HashSet<int>();
        foreach (var marker in _markers)
            if (!marker.SourceLocal.IsFinite || !marker.TargetLocal.IsFinite || !marked.Add(marker.End) ||
                !_limbs.Any(limb => limb.End == marker.End))
                throw new ArgumentException("Retarget contact marker requires one finite pair on an unconditional limb.");
        _contactCenters = new AlsDoubleVector[_conditionalLimbs.Length];
        _projectionCorrections = new AlsDoubleVector[_conditionalLimbs.Length + 3];
        _contactRadii = new double[_conditionalLimbs.Length];
        _preferredContactRadii = new double[_conditionalLimbs.Length];
        _activeContacts = new bool[_conditionalLimbs.Length];
        _preparationCenters = new AlsDoubleVector[_conditionalLimbs.Length];
        _preparationRadii = new double[_conditionalLimbs.Length];
        _preparationWeights = new double[_conditionalLimbs.Length];
        if (contactRoot < -1 || contactRoot >= parents.Length)
            throw new ArgumentException("Invalid retarget contact root.");
        _contactRoot = contactRoot;
        _bodyGeometry = bodyGeometry;
        bodyGeometry?.ValidateTranslationSupport(parents,contactRoot,limbs,conditionalLimbs);
        _bodyRequiredUp = new double[bodyGeometry?.PointCount ?? 0];
        _bodyRequiredOut = new double[_bodyRequiredUp.Length];
        if (contactRoot >= 0)
        {
            // The permitted body translation comes from this model's actual
            // proportion differences, rather than an arbitrary reach allowance.
            var rootDifference = System.Math.Sqrt((targetReference[contactRoot].Position-sourceReference[contactRoot].Position).LengthSquared);
            foreach (var limb in conditionalLimbs)
            {
                var bound = rootDifference;
                var ancestor = limb.End;
                while (ancestor != contactRoot && ancestor >= 0)
                {
                    bound += System.Math.Sqrt((targetReference[ancestor].Position-sourceReference[ancestor].Position).LengthSquared);
                    ancestor = parents[ancestor];
                }
                if (ancestor != contactRoot || contactRoot >= limb.Upper)
                    throw new ArgumentException("Conditional limbs must descend from their contact root.");
                if (!double.IsFinite(bound)) throw new ArgumentException("Nonfinite retarget contact-root bound.");
                MaximumContactRootCorrection = System.Math.Max(MaximumContactRootCorrection, bound);
            }
        }
        _retarget = new(mapping, modes, presence, targetReference, sourceReference);
        _candidate = new AlsPrecisePose[parents.Length]; _sourceComponents = new AlsPrecisePose[parents.Length];
        _targetComponents = new AlsPrecisePose[parents.Length]; _localScratch = new AlsPrecisePose[parents.Length];
        _componentInput = new AlsPrecisePose[parents.Length]; _componentLocal = new AlsPrecisePose[parents.Length];
        _changed = new bool[parents.Length];
        _beforeIk = new AlsPrecisePose[parents.Length];
        if (!boneNames.IsEmpty) _twists = new(boneNames, parents, targetReference);
    }

    public void Evaluate(ReadOnlySpan<AlsPrecisePose> source, Span<AlsPrecisePose> target, bool matchEndEffectors = true,
        ReadOnlySpan<float> contactWeights = default)
        => EvaluateCore(source,target,matchEndEffectors,contactWeights,false,false,default,0,0,default,default);

    public void EvaluateContactFrame(ReadOnlySpan<AlsPrecisePose> source, Span<AlsPrecisePose> target,
        ReadOnlySpan<float> contactWeights, in AlsRetargetContactState previous, float characterDeltaSeconds,
        float releaseDurationSeconds, out AlsRetargetContactState candidateState,
        in AlsRetargetContactEnvironment environment = default,
        ReadOnlySpan<AlsRetargetContactPreparation> preparations = default)
    {
        if (!float.IsFinite(characterDeltaSeconds) || characterDeltaSeconds < 0 ||
            !float.IsFinite(releaseDurationSeconds) || releaseDurationSeconds < 0)
            throw new ArgumentException("Invalid retarget contact frame timing.");
        previous.Validate(MaximumContactRootCorrection,_conditionalLimbs.Length);
        environment.Validate();
        if(environment.Enabled && _bodyGeometry is null)throw new ArgumentException("Contact environment requires imported body geometry.");
        if (!preparations.IsEmpty && preparations.Length != _conditionalLimbs.Length)
            throw new ArgumentException("Incomplete retarget contact preparations.");
        foreach (var preparation in preparations) preparation.Validate();
        candidateState = EvaluateCore(source,target,true,contactWeights,true,false,previous,
            characterDeltaSeconds,releaseDurationSeconds,environment,preparations);
    }

    public void EvaluateCommittedContactFrame(ReadOnlySpan<AlsPrecisePose> source, Span<AlsPrecisePose> target,
        ReadOnlySpan<float> contactWeights, in AlsRetargetContactState committed)
    {
        committed.Validate(MaximumContactRootCorrection,_conditionalLimbs.Length);
        EvaluateCore(source,target,true,contactWeights,false,true,committed,0,0,default,default);
    }

    private AlsRetargetContactState EvaluateCore(ReadOnlySpan<AlsPrecisePose> source, Span<AlsPrecisePose> target,
        bool matchEndEffectors, ReadOnlySpan<float> contactWeights, bool advancing, bool restoring,
        in AlsRetargetContactState previous, float delta, float duration,
        in AlsRetargetContactEnvironment environment, ReadOnlySpan<AlsRetargetContactPreparation> preparations)
    {
        if (source.Length != BoneCount || target.Length != BoneCount || source.Overlaps(target))
            throw new ArgumentException("Invalid retarget pose buffers.");
        if (!contactWeights.IsEmpty && contactWeights.Length != _conditionalLimbs.Length)
            throw new ArgumentException("Incomplete conditional retarget contacts.");
        var hasContacts = false;
        double totalContactWeight = 0;
        foreach (var weight in contactWeights)
        {
            if (!float.IsFinite(weight) || weight is < 0 or > 1)
                throw new ArgumentException("Invalid conditional retarget weight.");
            hasContacts |= weight > AlsPoseBlender.WeightThreshold;
            totalContactWeight += weight;
        }
        foreach (var atom in source) atom.Validate();
        MaximumConditionalResidual = 0;
        MaximumConditionalBendLossDegrees = 0;
        ContactRootCorrection = default;
        MaximumBodyPenetrationCm = MaximumBodyClearanceResidualCm = 0;
        BodyProjectionCount = 0;
        PreparationWeight = MaximumPreparationSourceReachExcessCm = MaximumPreparationStyleResidualCm = 0;
        _preparationWeights.AsSpan().Clear();
        source.CopyTo(_candidate);
        _retarget.Apply(_candidate, true);
        for (var bone = 0; bone < BoneCount; bone++)
        {
            _candidate[bone] = _candidate[bone] with { Scale = _reference[bone].Scale };
            _candidate[bone].Validate();
        }
        if (!matchEndEffectors || _limbs.Length == 0 && !hasContacts && !advancing && !restoring)
        {
            _candidate.CopyTo(target);
            return default;
        }
        Compose(source, _sourceComponents);
        if (_twists is not null) _candidate.CopyTo(_beforeIk, 0);
        Compose(_candidate, _targetComponents);
        var state = default(AlsRetargetContactState);
        var rawCorrection = _contactRoot >= 0 ? CalculateContactRoot(contactWeights) : AlsDoubleVector.Zero;
        if (_contactRoot >= 0 && !preparations.IsEmpty)
        {
            var prepared = CalculatePreparationRoot(contactWeights,preparations);
            if (PreparationWeight > 0) rawCorrection = prepared;
        }
        var correction = rawCorrection;
        if (advancing)
        {
            state = AlsRetargetContactState.Preference(previous,rawCorrection,totalContactWeight,delta,duration);
            correction = _contactRoot >= 0 ? ProjectContactRoot(false,state.RealizedCorrection) : AlsDoubleVector.Zero;
            // Keep true infeasibility visible, but do not discard an already
            // feasible raw solution for a finite-iteration release preference.
            if (!WithinReach(correction) && WithinReach(rawCorrection)) correction = rawCorrection;
            if(environment.Enabled)correction=ConstrainBodyGeometry(correction,environment);
            state = state with
            {
                RealizedCorrection = correction,
                Velocity = previous.Initialized && delta > 0 ?
                    (correction-previous.RealizedCorrection)*(1d/delta) : previous.Velocity
            };
            state.Validate(MaximumContactRootCorrection,_conditionalLimbs.Length);
        }
        else if (restoring && previous.Initialized) correction = previous.RealizedCorrection;
        for (var i=0;i<_preparationWeights.Length;i++)
            if (_preparationWeights[i]>0)
                MaximumPreparationStyleResidualCm = System.Math.Max(MaximumPreparationStyleResidualCm,
                    System.Math.Max(0,System.Math.Sqrt((correction-_preparationCenters[i]).LengthSquared)-_preparationRadii[i]));
        if (_contactRoot >= 0) ApplyContactRoot(correction);
        foreach (var limb in _limbs)
            Solve(limb, 1);
        for (var i = 0; i < contactWeights.Length; i++)
            if (contactWeights[i] > AlsPoseBlender.WeightThreshold)
                MaximumConditionalResidual = System.Math.Max(MaximumConditionalResidual,
                    Solve(_conditionalLimbs[i], contactWeights[i], preserveBendPlane: true));
        for (var bone = 0; bone < BoneCount; bone++)
        {
            var parent = _parents[bone];
            _candidate[bone] = (parent < 0 ? _targetComponents[bone] :
                AlsPrecisePose.Relative(_targetComponents[bone], _targetComponents[parent])).Normalized();
            _candidate[bone].Validate();
        }
        _twists?.Adjust(_beforeIk, _candidate);
        _candidate.CopyTo(target);
        return state;
    }

    private double Solve(in AlsRetargetLimb limb, float weight, bool preserveBendPlane = false)
    {
        var upper = _targetComponents[limb.Upper];
        var joint = _targetComponents[limb.Joint];
        var end = _targetComponents[limb.End];
        var effector = _sourceComponents[limb.End];
        foreach (var marker in _markers)
            if (marker.End == limb.End)
            {
                // Match the same physical contact point, not merely bone origins.
                // Bone-local markers rotate with the authored foot, retaining its
                // swing clearance and avoiding a fixed world-space height shift.
                var offset = marker.SourceLocal * effector.Scale - marker.TargetLocal * end.Scale;
                effector = effector with { Position = effector.Position + offset.Rotate(effector.Rotation) };
                break;
            }
        var target = weight == 1 ? effector.Position : end.Position + (effector.Position - end.Position) * weight;
        var pole = joint.Position;
        if (preserveBendPlane)
        {
            var sourceUpper = _sourceComponents[limb.Upper].Position;
            var sourceJoint = _sourceComponents[limb.Joint].Position;
            var sourceDelta = effector.Position - sourceUpper;
            var targetDelta = target - upper.Position;
            // Transport the authored elbow plane to the new reach direction.
            // This preserves the current motion's handedness and style instead
            // of supplying a fixed elbow axis or one angle for every animation.
            if (sourceDelta.LengthSquared > 1e-12 && targetDelta.LengthSquared > 1e-12 &&
                AlsDoubleVector.Cross(sourceDelta, sourceJoint-sourceUpper).LengthSquared > 1e-12)
            {
                var rotation = AlsTwoBoneIk.Between(sourceDelta.SafeNormal(), targetDelta.SafeNormal());
                var authoredPole = upper.Position + (sourceJoint-sourceUpper).Rotate(rotation);
                pole += (authoredPole-pole)*weight;
            }
        }
        var solveTarget = target;
        if (preserveBendPlane)
        {
            var inner = System.Math.Abs(System.Math.Sqrt((joint.Position-upper.Position).LengthSquared) -
                                        System.Math.Sqrt((end.Position-joint.Position).LengthSquared));
            var delta = target-upper.Position;
            var distance = System.Math.Sqrt(delta.LengthSquared);
            if (distance < inner)
                solveTarget = upper.Position + (distance > 1e-8 ? delta*(1/distance) :
                    (end.Position-upper.Position).SafeNormal())*inner;
        }
        AlsTwoBoneIk.Solve(ref upper, ref joint, ref end, pole, solveTarget);
        end = end with { Rotation = weight == 1 ? effector.Rotation :
            AlsRigTwoBoneIk.Slerp(end.Rotation, effector.Rotation, weight) };
        var solved = new AlsRigTwoBoneIkResult(true, upper.Normalized(), joint.Normalized(), end.Normalized());
        AlsRigTwoBoneIk.Apply(solved, _targetComponents, _parents, limb.Upper, limb.Joint, limb.End,
            true, _localScratch, _changed);
        if (preserveBendPlane)
        {
            var originalBend = Bend(_sourceComponents[limb.Upper].Position,
                _sourceComponents[limb.Joint].Position, effector.Position);
            var actualBend = Bend(upper.Position, joint.Position, end.Position);
            MaximumConditionalBendLossDegrees = System.Math.Max(MaximumConditionalBendLossDegrees,
                (originalBend-actualBend)*180/System.Math.PI);
        }
        return System.Math.Sqrt((end.Position - target).LengthSquared);
    }

    private AlsDoubleVector CalculatePreparationRoot(ReadOnlySpan<float> weights,
        ReadOnlySpan<AlsRetargetContactPreparation> preparations)
    {
        for (var i=0;i<preparations.Length;i++)
        {
            var preparation=preparations[i];
            if (!preparation.Enabled || preparation.BodyWeight<=0) continue;
            var limb=_conditionalLimbs[i];
            var sourceUpper=_sourceComponents[limb.Upper].Position;
            var sourceJoint=_sourceComponents[limb.Joint].Position;
            var sourceEnd=_sourceComponents[limb.End].Position;
            var a=System.Math.Sqrt((sourceJoint-sourceUpper).LengthSquared);
            var b=System.Math.Sqrt((sourceEnd-sourceJoint).LengthSquared);
            var distance=System.Math.Sqrt((preparation.AnchorComponent.Position-sourceUpper).LengthSquared);
            MaximumPreparationSourceReachExcessCm=System.Math.Max(MaximumPreparationSourceReachExcessCm,
                System.Math.Max(0,System.Math.Max(distance-a-b,System.Math.Abs(a-b)-distance)));
            var predicted=a*b>1e-12 ? System.Math.Acos(System.Math.Clamp(
                (distance*distance-a*a-b*b)/(2*a*b),-1,1)) : 0;
            var actual=Bend(sourceUpper,sourceJoint,sourceEnd);
            var handWeight=i<weights.Length ? weights[i] : 0;
            var angle=predicted+(actual-predicted)*handWeight;
            var upper=_targetComponents[limb.Upper].Position;
            a=System.Math.Sqrt((_targetComponents[limb.Joint].Position-upper).LengthSquared);
            b=System.Math.Sqrt((_targetComponents[limb.End].Position-_targetComponents[limb.Joint].Position).LengthSquared);
            _preparationCenters[i]=preparation.AnchorComponent.Position-upper;
            _preparationRadii[i]=System.Math.Sqrt(System.Math.Max(0,a*a+b*b+2*a*b*System.Math.Cos(angle)));
            _preparationWeights[i]=preparation.BodyWeight;
            PreparationWeight=System.Math.Max(PreparationWeight,preparation.BodyWeight);
        }
        if (PreparationWeight<=0) return default;
        // Squared distances to preferred balls form a convex SOFT objective.
        // A tiny origin term provides a unique, continuous tie-break when a
        // second hand starts preparing inside the first hand's feasible ball.
        // This is numerical regularization, not a contact or bone-angle limit.
        const double regularization=1e-4;
        double total=regularization;
        for (var i=0;i<_preparationWeights.Length;i++) total+=_preparationWeights[i]/PreparationWeight;
        var result=AlsDoubleVector.Zero;
        for (var pass=0;pass<128;pass++)
        {
            var gradient=result*regularization;
            for (var i=0;i<_preparationWeights.Length;i++)
            {
                var weight=_preparationWeights[i]/PreparationWeight;
                if (weight<=0) continue;
                var delta=result-_preparationCenters[i];
                var distance=System.Math.Sqrt(delta.LengthSquared);
                if (distance>_preparationRadii[i] && distance>1e-12)
                    gradient+=delta*(weight*(1-_preparationRadii[i]/distance));
            }
            result-=gradient*(1/total);
            var length=System.Math.Sqrt(result.LengthSquared);
            if (length>MaximumContactRootCorrection && length>0)
                result*=MaximumContactRootCorrection/length;
        }
        // The outer envelope makes the zero-preparation limit exactly zero;
        // small optimization weights alone would not give that guarantee.
        return result*PreparationWeight;
    }

    private AlsDoubleVector CalculateContactRoot(ReadOnlySpan<float> weights)
    {
        for (var i = 0; i < _activeContacts.Length; i++)
        {
            var weight = i < weights.Length ? weights[i] : 0;
            _activeContacts[i] = weight > AlsPoseBlender.WeightThreshold;
            if (!_activeContacts[i]) continue;
            var limb = _conditionalLimbs[i];
            var upper = _targetComponents[limb.Upper].Position;
            var joint = _targetComponents[limb.Joint].Position;
            var end = _targetComponents[limb.End].Position;
            var a = System.Math.Sqrt((joint-upper).LengthSquared);
            var b = System.Math.Sqrt((end-joint).LengthSquared);
            var sourceBend = Bend(_sourceComponents[limb.Upper].Position,
                _sourceComponents[limb.Joint].Position, _sourceComponents[limb.End].Position);
            var currentBend = Bend(upper, joint, end);
            var bend = currentBend+(sourceBend-currentBend)*weight;
            var reach = System.Math.Sqrt(System.Math.Max(0, a*a+b*b+2*a*b*System.Math.Cos(bend)));
            var goal = end+(_sourceComponents[limb.End].Position-end)*weight;
            // Both contact balls use the identical blended effector equation.
            // The smaller one is an authored-pose preference, never permission
            // to break an anchor or to enlarge the reference-derived budget.
            _contactCenters[i] = (goal-upper)*(1d/weight);
            _contactRadii[i] = (a+b)/weight;
            _preferredContactRadii[i] = reach/weight;
        }
        var shift = ProjectContactRoot(preferred: false);
        if (WithinReach(shift))
        {
            var preferred = ProjectContactRoot(preferred: true);
            if (WithinReach(preferred)) shift = preferred;
            else
            {
                // Conflicting hands may prevent full style preservation. The
                // feasible set is convex, so retain only the preferred change
                // that keeps every hard reach constraint satisfied.
                double low = 0, high = 1;
                for (var pass = 0; pass < 32; pass++)
                {
                    var alpha = (low+high)*.5;
                    if (WithinReach(shift+(preferred-shift)*alpha)) low = alpha;
                    else high = alpha;
                }
                shift += (preferred-shift)*low;
            }
        }
        return shift;
    }

    private void ApplyContactRoot(AlsDoubleVector shift)
    {
        ContactRootCorrection = shift;
        _changed.AsSpan().Clear();
        for (var bone = _contactRoot; bone < BoneCount; bone++)
        {
            if (bone != _contactRoot && (_parents[bone] < 0 || !_changed[_parents[bone]])) continue;
            _targetComponents[bone] = _targetComponents[bone] with {Position=_targetComponents[bone].Position+shift};
            _changed[bone] = true;
        }
    }

    private AlsDoubleVector ProjectContactRoot(bool preferred, AlsDoubleVector input = default,
        double bodyUp = double.NegativeInfinity,double bodyOut = double.NegativeInfinity)
    {
        // Dykstra projects the input preference into the limb balls, fixed
        // budget and optional body halfspaces. Zero input asks for the smallest
        // translation. No previous frame is retained.
        _projectionCorrections.AsSpan().Clear();
        var result = input;
        var radii = preferred ? _preferredContactRadii : _contactRadii;
        for (var pass = 0; pass < 96; pass++)
        {
            for (var i = 0; i < _projectionCorrections.Length; i++)
            {
                if(i>_activeContacts.Length)
                {
                    var up=i==_activeContacts.Length+1;
                    var bound=up ? bodyUp : bodyOut;
                    if(double.IsNegativeInfinity(bound))continue;
                    var normal=up ? _bodyUpNormal : _bodyOutNormal;
                    var value=result+_projectionCorrections[i];
                    var gap=bound-AlsDoubleVector.Dot(value,normal);
                    var clamped=gap>0 ? value+normal*gap : value;
                    _projectionCorrections[i]=value-clamped;
                    result=clamped;
                    continue;
                }
                var budget = i == _activeContacts.Length;
                if (!budget && !_activeContacts[i]) continue;
                var candidate = result+_projectionCorrections[i];
                var center = budget ? AlsDoubleVector.Zero : _contactCenters[i];
                var radius = budget ? MaximumContactRootCorrection : radii[i];
                var delta = candidate-center;
                var distance = System.Math.Sqrt(delta.LengthSquared);
                var projected = distance > radius && distance > 0 ? center+delta*(radius/distance) : candidate;
                _projectionCorrections[i] = candidate-projected;
                result = projected;
            }
        }
        return result;
    }

    private AlsDoubleVector ConstrainBodyGeometry(AlsDoubleVector preferred,
        in AlsRetargetContactEnvironment environment)
    {
        var geometry=_bodyGeometry!;
        geometry.BuildRequirements(_targetComponents,environment,_bodyRequiredUp,_bodyRequiredOut);
        _bodyUpNormal=new AlsDoubleVector(0,0,1).Rotate(environment.LedgeToComponent.Rotation);
        _bodyOutNormal=new AlsDoubleVector(0,-1,0).Rotate(environment.LedgeToComponent.Rotation);
        var initialResidual=BodyResidual(preferred,out var witness);
        var best=preferred;var bestResidual=initialResidual;var bestCost=double.PositiveInfinity;
        const double clearanceTolerance=1e-7;
        var queueCount=0;var visited=0;
        if(initialResidual>clearanceTolerance && WithinReach(preferred))
            _bodyQueue[queueCount++]=new(double.NegativeInfinity,double.NegativeInfinity,preferred,0,witness);
        while(queueCount>0 && BodyProjectionCount<MaximumBodyProjections)
        {
            var selected=0;
            for(var i=1;i<queueCount;i++)if(_bodyQueue[i].Cost<_bodyQueue[selected].Cost)selected=i;
            var node=_bodyQueue[selected];_bodyQueue[selected]=_bodyQueue[--queueCount];
            if(node.Cost>=bestCost)continue;
            for(var face=0;face<2 && BodyProjectionCount<MaximumBodyProjections;face++)
            {
                var up=face==0 ? System.Math.Max(node.Up,_bodyRequiredUp[node.Witness]) : node.Up;
                var outside=face==1 ? System.Math.Max(node.Out,_bodyRequiredOut[node.Witness]) : node.Out;
                var seen=false;
                for(var i=0;i<visited;i++)if(_visitedBodyUp[i]==up && _visitedBodyOut[i]==outside){seen=true;break;}
                if(seen)continue;
                _visitedBodyUp[visited]=up;_visitedBodyOut[visited++]=outside;
                var candidate=ProjectContactRoot(false,preferred,up,outside);BodyProjectionCount++;
                // Infeasible body/hand/budget intersections never enlarge the
                // model budget or silently claim that geometry was repaired.
                if(!WithinReach(candidate) || AlsDoubleVector.Dot(candidate,_bodyUpNormal)<up-clearanceTolerance ||
                    AlsDoubleVector.Dot(candidate,_bodyOutNormal)<outside-clearanceTolerance)continue;
                var residual=BodyResidual(candidate,out var nextWitness);
                var cost=(candidate-preferred).LengthSquared;
                if(residual<=clearanceTolerance)
                {
                    if(cost<bestCost){best=candidate;bestResidual=residual;bestCost=cost;}
                }
                else
                {
                    if(double.IsPositiveInfinity(bestCost) && residual<bestResidual){best=candidate;bestResidual=residual;}
                    if(cost<bestCost)_bodyQueue[queueCount++]=new(up,outside,candidate,cost,nextWitness);
                }
            }
        }
        MaximumBodyClearanceResidualCm=System.Math.Max(0,BodyResidual(best,out _));
        MaximumBodyPenetrationCm=System.Math.Max(0,MaximumBodyClearanceResidualCm-environment.ClearanceCm);
        return best;
    }

    private double BodyResidual(AlsDoubleVector correction,out int witness)
    {
        var up=AlsDoubleVector.Dot(correction,_bodyUpNormal);
        var outside=AlsDoubleVector.Dot(correction,_bodyOutNormal);
        double maximum=0;witness=-1;
        for(var i=0;i<_bodyRequiredUp.Length;i++)
        {
            var residual=_bodyGeometry!.ClearanceResidual(i,_bodyRequiredUp[i]-up,_bodyRequiredOut[i]-outside);
            if(residual>maximum){maximum=residual;witness=i;}
        }
        return maximum;
    }

    private bool WithinReach(AlsDoubleVector shift)
    {
        const double tolerance = 1e-8;
        if (shift.LengthSquared > (MaximumContactRootCorrection+tolerance)*(MaximumContactRootCorrection+tolerance)) return false;
        for (var i = 0; i < _activeContacts.Length; i++)
            if (_activeContacts[i] && (shift-_contactCenters[i]).LengthSquared >
                (_contactRadii[i]+tolerance)*(_contactRadii[i]+tolerance)) return false;
        return true;
    }

    private static double Bend(AlsDoubleVector upper, AlsDoubleVector joint, AlsDoubleVector end)
    {
        var a = joint-upper; var b = end-joint;
        var length = System.Math.Sqrt(a.LengthSquared*b.LengthSquared);
        return length > 1e-12 ? System.Math.Acos(System.Math.Clamp(AlsDoubleVector.Dot(a,b)/length,-1,1)) : 0;
    }

    private static void ValidateLimb(in AlsRetargetLimb limb, ReadOnlySpan<int> parents)
    {
        if ((uint)limb.End >= parents.Length || limb.Joint <= limb.Upper || limb.End <= limb.Joint ||
            limb.Upper < 0 || parents[limb.Joint] != limb.Upper || parents[limb.End] != limb.Joint)
            throw new ArgumentException("Retarget contact requires a complete two-bone limb.");
    }

    public void EvaluateComponents(ReadOnlySpan<AlsPrecisePose> source, Span<AlsPrecisePose> target)
    {
        if (source.Length != BoneCount || target.Length != BoneCount || source.Overlaps(target))
            throw new ArgumentException("Invalid component retarget buffers.");
        foreach (var atom in source) atom.Validate();
        for (var bone = 0; bone < BoneCount; bone++)
        {
            var parent = _parents[bone];
            _localScratch[bone] = (parent < 0 ? source[bone] : AlsPrecisePose.Relative(source[bone], source[parent])).Normalized();
        }
        // Evaluate uses localScratch for IK propagation, so preserve the complete input first.
        _localScratch.CopyTo(_componentInput, 0);
        Evaluate(_componentInput, _componentLocal);
        Compose(_componentLocal, _targetComponents);
        foreach (var atom in _targetComponents) atom.Validate();
        _targetComponents.CopyTo(target);
    }

    private void Compose(ReadOnlySpan<AlsPrecisePose> local, Span<AlsPrecisePose> components)
    {
        for (var bone = 0; bone < BoneCount; bone++)
        {
            var parent = _parents[bone];
            components[bone] = (parent < 0 ? local[bone] : AlsPrecisePose.Compose(local[bone], components[parent])).Normalized();
        }
    }
}

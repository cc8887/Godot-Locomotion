using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal interface IAlsPostLayeringPoseSource
{
    void EvaluatePostLayering(Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves);
}

// Owns aiming properties and their seven evaluators with the outer Aim/spine
// result. Post Layering and its source/event transaction remain with the parent.
internal sealed class AlsAimLayerFrameStage : IAlsPoseCachePoseSink
{
    private readonly AlsAimingInputModel _input;
    private readonly AlsAimFrameRuntime _history;
    private readonly AlsAimPoseRuntime _aimPose;
    private readonly AlsAimLayerRuntime _layer;
    private readonly AlsLocalPose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private AlsAimingInputState _candidateInput;
    private readonly AlsAimLayerDefinition _definition;
    private AlsPoseCacheEvaluation _committedCache, _candidateCache;
    private readonly AlsLocalPose[] _postPose;
    private readonly AlsInertialCurve[] _postCurves;
    private AlsAnimationGraphFrame _traversal;
    private IAlsPostLayeringPoseSource? _postSource;
    private bool _cacheEnabled, _scopeOpen;
    private bool _prepared, _unvisited;
    private AlsFrameIdentity _identity;
    private AlsPoseCacheScope _scope;
    public int PostCacheReads { get; private set; }
    public int PostSourceEvaluations => _cacheEnabled ? _candidateCache.SourceEvaluations : 0;
    public AlsAimingInputState CommittedInput { get; private set; }
    public AlsAimingInputState Input => _candidateInput;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public AlsAimFrameState CommittedAim => _history.Committed;
    public AlsAimFrameState CandidateAim => _history.Candidate;
    public AlsPoseUpdateContext PostContext => _prepared && !_unvisited ? _layer.PostContext : throw new InvalidOperationException("Unvisited Post Layering context.");
    public bool AimRelevant => _prepared && !_unvisited && _layer.AimRelevant;
    public float SpineAlpha => _layer.SpineAlpha;
    public ReadOnlySpan<AlsLocalPose> Pose => _prepared && !_unvisited ? _layer.Pose : throw new InvalidOperationException("Unvisited Aim layer pose.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _prepared && !_unvisited ? _layer.Curves : throw new InvalidOperationException("Unvisited Aim layer curves.");

    public AlsAimLayerFrameStage(AlsMovementGraphDefinition definition, AlsAnimationSetDefinition set,
        ReadOnlySpan<string> names, uint character, uint generation)
    {
        var skeleton = definition.AimRawSources.GetSkeleton(definition.AimSampling.SkeletonId);
        _input = definition.AimingInput; CommittedInput = _input.InitialState;
        _history = new(definition.AimPose, character, generation);
        _aimPose = new(definition.AimPose, new AlsAimAnimationSourceSampler(definition.AimSampling,
            definition.AimRawSources, set, names), names.Length, character, generation);
        _layer = new(definition.AimLayer, skeleton.LogicalBoneNames, skeleton.LogicalParents, names);
        _pose = new AlsLocalPose[skeleton.LogicalBoneCount]; _curves = new AlsInertialCurve[names.Length];
        _definition=definition.AimLayer;
        _postPose=new AlsLocalPose[_pose.Length]; _postCurves=new AlsInertialCurve[names.Length];
        _committedCache=NewCache(); _candidateCache=NewCache();
    }
    public void Prepare(in AlsPoseUpdateContext context, in AlsLayeringInput layer,
        in AlsAimingObservation observation, ReadOnlySpan<AlsInertialCurve> feedback, AlsAnimationGraphFrame? traversal=null,
        bool updateSource=true, bool cachePostLayering=true)
    {
        try { Prepare(context,layer,_input.Evaluate(observation,CommittedInput),observation.Mode,observation.HasInput,feedback,traversal,updateSource,cachePostLayering); }
        catch { Cancel(); throw; }
    }

    // Production uses the already-updated global Blueprint state. Do not run
    // its interpolation a second time when the outer pose graph becomes active.
    public void Prepare(in AlsPoseUpdateContext context, in AlsLayeringInput layer,
        in AlsAimingInputState aiming, AlsRotationMode mode, bool hasInput, ReadOnlySpan<AlsInertialCurve> feedback,
        AlsAnimationGraphFrame? traversal=null, bool updateSource=true, bool cachePostLayering=true)
    {
        if(_prepared || context.Identity!=aiming.Identity || layer.Identity!=context.Identity ||
            CommittedIdentity!=default && (context.Identity.CharacterId!=CommittedIdentity.CharacterId ||
                context.Identity.SlotGeneration!=CommittedIdentity.SlotGeneration || context.Identity.FrameId<=CommittedIdentity.FrameId) ||
            !updateSource && !traversal.HasValue)
            throw new ArgumentException("Invalid Aim layer lifecycle candidate.");
        try
        {
            _prepared=true; _unvisited=!updateSource; _identity=context.Identity;
            _candidateInput = aiming;
            _cacheEnabled=traversal.HasValue && cachePostLayering; PostCacheReads=0;
            if (traversal is { } validated)
            {
                validated.Validate(context.Identity); _traversal=validated;
            }
            if(_cacheEnabled)
            {
                var frame=_traversal;
                _candidateCache.BeginCandidate(context.Identity,_committedCache);
                // Both TwoWay branches receive initialization/cache-bones visits,
                // including when only one branch will update. SaveCachedPose
                // suppresses the second visit; the parent forwards these same
                // lifecycle counters to the deferred LayerBlending stage.
                _candidateCache.Initialize(_definition.AimCacheRead,frame.Initialization,this);
                _candidateCache.Initialize(_definition.SpineCacheRead,frame.Initialization,this);
                _candidateCache.CacheBones(_definition.AimCacheRead,frame.Bones,this);
                _candidateCache.CacheBones(_definition.SpineCacheRead,frame.Bones,this);
            }
            if(updateSource)_layer.Prepare(traversal is { } path ? context.WithUpdateCounter(path.Update) : context, layer, _candidateInput, feedback);
            // Aim updates immediately; the Post Layering cached source is
            // drained later. Hidden Aim still receives cold initialization.
            _history.Prepare(_candidateInput, mode, hasInput, context.Delta,
                context.Identity.FrameId, updateSource && _layer.AimRelevant,
                updateSource ? _layer.AimContext.Weight : 0, updateSource && !_layer.AimContext.IsActive,traversal);
        }
        catch { Cancel(); throw; }
    }
    public void Evaluate(ReadOnlySpan<AlsLocalPose> post, ReadOnlySpan<AlsInertialCurve> curves)
    {
        if(!_prepared || _unvisited)throw new InvalidOperationException("Unvisited Aim layer cannot evaluate.");
        try
        {
            if (_layer.AimRelevant) _aimPose.Evaluate(_history.Candidate, _pose, _curves);
            _layer.Evaluate(post, curves, _layer.AimRelevant ? _pose : [], _layer.AimRelevant ? _curves : []);
        }
        catch { Cancel(); throw; }
    }
    public void Evaluate(IAlsPostLayeringPoseSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!_prepared || _unvisited || !_cacheEnabled || _scopeOpen) throw new InvalidOperationException("Missing scoped Post Layering candidate.");
        try
        {
            _postSource=source; _scope=_candidateCache.PushScope(); _scopeOpen=true;
            if (SpineAlpha<1-AlsPoseBlender.WeightThreshold) Read(_definition.AimCacheRead);
            if (SpineAlpha>AlsPoseBlender.WeightThreshold) Read(_definition.SpineCacheRead);
            Evaluate(_postPose,_postCurves);
            _candidateCache.PopScope(_scope); _scopeOpen=false; _postSource=null;
        }
        catch { Cancel(); throw; }
        void Read(int node)
        {
            _candidateCache.Evaluate(node,_traversal.Evaluation,_scope,this,_postPose,_postCurves); PostCacheReads++;
        }
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if(!_prepared || identity!=_identity)throw new InvalidOperationException("Missing Aim layer candidate.");
        if(!_unvisited)_layer.ValidateCommit(identity);
        _history.ValidateCommit(identity);
        if (_scopeOpen || _cacheEnabled && (_candidateCache.IsFaulted ||
            (_unvisited ? _candidateCache.SourceEvaluations!=0 || PostCacheReads!=0 : _candidateCache.SourceEvaluations!=1 || PostCacheReads==0)))
            throw new InvalidOperationException("Post Layering cache did not finish its root evaluation scope.");
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); if(!_unvisited)_layer.Commit(identity);
        _history.Commit(identity); CommittedInput = _candidateInput; CommittedIdentity=identity; _prepared=false;
        if (_cacheEnabled) (_committedCache,_candidateCache)=(_candidateCache,_committedCache);
    }
    public void Cancel()
    {
        _layer.Cancel(); _history.Cancel(); _candidateInput = CommittedInput; _postSource=null;
        _prepared=false;
        if (_candidateCache.IsFaulted) { _candidateCache=NewCache(); _scopeOpen=false; }
        else if (_scopeOpen) { _candidateCache.PopScope(_scope); _scopeOpen=false; }
    }
    private AlsPoseCacheEvaluation NewCache()=>new(_definition.Cache,_postPose.Length,_postCurves.Length,1);
    public void InitializeSource(int cache) { if(cache!=_definition.Cache.UpdateOrder[0])throw new ArgumentException("Foreign Post Layering cache."); }
    public void CacheSourceBones(int cache) { if(cache!=_definition.Cache.UpdateOrder[0])throw new ArgumentException("Foreign Post Layering cache."); }
    public void EvaluateSource(int cache,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
    {
        if(cache!=_definition.Cache.UpdateOrder[0] || _postSource is null)throw new InvalidOperationException("Post Layering source is unavailable.");
        _postSource.EvaluatePostLayering(pose,curves);
    }
}

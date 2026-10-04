// Included after the shared native capture helpers. Instrumentation only:
// original Main callbacks, linked provider, Pivot machine and Warp nodes run.
namespace LyraCycleProbe
{
struct FGroundPivotProbe
{
    struct FTap : FAnimNode_Base
    {
        FPoseLink Child; TFunction<void()> Before;
        void Initialize_AnyThread(const FAnimationInitializeContext& C) override { Child.Initialize(C); }
        void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override { Child.CacheBones(C); }
        void Update_AnyThread(const FAnimationUpdateContext& C) override { if(Before) Before();Child.Update(C); }
        void Evaluate_AnyThread(FPoseContext& C) override { Child.Evaluate(C); }
    } Tap;
    UAnimInstance* Main=nullptr;UAnimInstance* Layer=nullptr;
    FAnimNode_StateResult* State=nullptr;FPoseLink Original,Link;
    FAnimNode_StateMachine* Machine=nullptr;FAnimNode_BlendSpacePlayer* Lean=nullptr;
    FAnimNode_LayeredBoneBlend* Blend=nullptr;FAnimNode_SequenceEvaluator* Hip=nullptr;
    FAnimNode_SequenceEvaluator* Sources[2]={nullptr,nullptr};
    FAnimNode_OrientationWarping* Orientations[2]={nullptr,nullptr};
    FAnimNode_StrideWarping* Strides[2]={nullptr,nullptr};
    TStrongObjectPtr<UBlendProfile> Mask;
    TFunction<FString(const UAnimSequenceBase*)> Path;
    TSharedPtr<FJsonObject> Row;bool Initialized=false;bool Active=false;
    ~FGroundPivotProbe() { if(State) State->Result=Original; }
    int64 Direction() const
    { auto* P=FindFProperty<FNumericProperty>(Main->GetClass(),TEXT("PivotInitialDirection"));check(P);return P->GetSignedIntPropertyValue(P->ContainerPtrToValuePtr<void>(Main)); }
    double Timer() const
    { auto* P=FindFProperty<FDoubleProperty>(Main->GetClass(),TEXT("LastPivotTime"));check(P);return P->GetPropertyValue_InContainer(Main); }
    TSharedPtr<FJsonObject> Shared() const
    {
        const auto R=MakeShared<FJsonObject>();
        auto* A=FindFProperty<FStructProperty>(Layer->GetClass(),TEXT("PivotStartingAcceleration"));check(A && A->Struct==TBaseStructure<FVector>::Get());
        const auto& V=*A->ContainerPtrToValuePtr<FVector>(Layer);const auto Vector=MakeShared<FJsonObject>();
        Double(Vector,TEXT("x"),V.X);Double(Vector,TEXT("y"),V.Y);Double(Vector,TEXT("z"),V.Z);R->SetObjectField(TEXT("acceleration"),Vector);
        for(const TCHAR* Name : {TEXT("TimeAtPivotStop"),TEXT("StrideWarpingPivotAlpha")})
        { auto* P=FindFProperty<FNumericProperty>(Layer->GetClass(),Name);check(P && P->IsFloatingPoint());Double(R,Name,P->GetFloatingPointPropertyValue(P->ContainerPtrToValuePtr<void>(Layer))); }
        Double(R,TEXT("LastPivotTime"),Timer());return R;
    }
    bool Setup(UAnimInstance* InMain,UAnimInstance* InLayer,USkeleton* Skeleton,UBlendSpace* LeanSpace,
        TFunction<FString(const UAnimSequenceBase*)> InPath)
    {
        Main=InMain;Layer=InLayer;Path=MoveTemp(InPath);
        const auto* MI=IAnimClassInterface::GetFromClass(Main->GetClass());const auto* LI=IAnimClassInterface::GetFromClass(Layer->GetClass());
        if(!MI || !LI) return false;
        const auto& MP=MI->GetAnimNodeProperties();const auto& LP=LI->GetAnimNodeProperties();
        auto MainProperty=[&](int32 Node,UScriptStruct* Type)->FStructProperty*
        { const int32 I=MP.Num()-1-Node;return MP.IsValidIndex(I) && MP[I]->Struct==Type ? MP[I] : nullptr; };
        auto LayerProperty=[&](int32 Node,UScriptStruct* Type)->FStructProperty*
        { const int32 I=LP.Num()-1-Node;return LP.IsValidIndex(I) && LP[I]->Struct==Type ? LP[I] : nullptr; };
        auto* S=MainProperty(20,FAnimNode_StateResult::StaticStruct());auto* A=MainProperty(23,FAnimNode_ApplyAdditive::StaticStruct());
        auto* B=MainProperty(22,FAnimNode_BlendSpacePlayer::StaticStruct());auto* L=MainProperty(21,FAnimNode_LinkedAnimLayer::StaticStruct());
        if(!S || !A || !B || !L) return false;
        auto* Root=S->ContainerPtrToValuePtr<FAnimNode_StateResult>(Main);auto* Add=A->ContainerPtrToValuePtr<FAnimNode_ApplyAdditive>(Main);
        auto* Linked=L->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
        if(Root->GetStateIndex()!=4 || Root->Result.LinkID!=MP.Num()-1-23 || Add->Base.LinkID!=MP.Num()-1-21 ||
            Add->Additive.LinkID!=MP.Num()-1-22 || Linked->Layer!=TEXT("FullBody_PivotState") || Linked->GetTargetInstance<UAnimInstance>()!=Layer ||
            FNodeAccess::UpdateName(*Root)!=TEXT("UpdatePivotState") || FNodeAccess::FunctionName(*Root,TEXT("BecomeRelevantFunction"))!=TEXT("SetUpPivotState")) return false;
        Lean=B->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayer>(Main);Lean->SetBlendSpace(LeanSpace);
        auto* M=LayerProperty(59,FAnimNode_StateMachine::StaticStruct());auto* H=LayerProperty(57,FAnimNode_SequenceEvaluator::StaticStruct());
        auto* BlendProperty=LayerProperty(58,FAnimNode_LayeredBoneBlend::StaticStruct());
        if(!M || !H || !BlendProperty) return false;
        Machine=M->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Layer);Machine->CacheMachineDescription(const_cast<IAnimClassInterface*>(LI));
        Hip=H->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);Blend=BlendProperty->ContainerPtrToValuePtr<FAnimNode_LayeredBoneBlend>(Layer);
        if(Machine->StateMachineIndexInClass!=3 || Blend->BasePose.LinkID!=LP.Num()-1-59 || Blend->bUpdateBasePoseFirst ||
            Blend->BlendPoses.Num()!=1 || Blend->BlendPoses[0].LinkID!=LP.Num()-1-57 || Blend->BlendMasks.Num()!=1 || !Blend->BlendMasks[0]) return false;
        Mask.Reset(NewObject<UBlendProfile>(GetTransientPackage()));Mask->OwningSkeleton=Skeleton;Mask->Mode=EBlendProfileMode::BlendMask;
        const auto& Reference=Skeleton->GetReferenceSkeleton();
        for(int32 Bone=0;Bone<Reference.GetNum();++Bone)
        { float Weight=0;for(const auto& E : Blend->BlendMasks[0]->ProfileEntries) if(E.BoneReference.BoneName==Reference.GetBoneName(Bone)) { Weight=E.BlendScale;break; }
          Mask->SetBoneBlendScale(Bone,Weight,false,true); }
        Blend->BlendMasks[0]=Mask.Get();Blend->InvalidatePerBoneBlendWeights();
        auto& HM=FNodeAccess::Marker(*Hip);HM.PreviousMarker.TimeToMarker=0;HM.NextMarker.TimeToMarker=0;
        for(int32 I=0;I<2;++I)
        {
            auto* Source=LayerProperty(I==0 ? 61 : 67,FAnimNode_SequenceEvaluator::StaticStruct());
            auto* O=LayerProperty(I==0 ? 62 : 68,FAnimNode_OrientationWarping::StaticStruct());
            auto* T=LayerProperty(I==0 ? 65 : 71,FAnimNode_StrideWarping::StaticStruct());if(!Source || !O || !T) return false;
            Sources[I]=Source->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);Orientations[I]=O->ContainerPtrToValuePtr<FAnimNode_OrientationWarping>(Layer);
            Strides[I]=T->ContainerPtrToValuePtr<FAnimNode_StrideWarping>(Layer);TArray<FBoneReference> Spines;
            for(auto Bone : Orientations[I]->SpineBones)
            { if(Bone.BoneName==TEXT("spine_04") || Bone.BoneName==TEXT("spine_05")) Bone.BoneName=TEXT("spine_03");
              if(!Spines.ContainsByPredicate([Bone](const FBoneReference& Other){return Other.BoneName==Bone.BoneName;})) Spines.Add(Bone); }
            Orientations[I]->SpineBones=Spines;auto& Marker=FNodeAccess::Marker(*Sources[I]);Marker.PreviousMarker.TimeToMarker=0;Marker.NextMarker.TimeToMarker=0;
        }
        State=Root;Original=State->Result;Tap.Child=Original;State->Result.SetLinkNode(&Tap);Link.LinkID=MP.Num()-1-20;Link.SetLinkNode(State);return true;
    }
    void Begin(FAnimInstanceProxy& Proxy,const TSharedPtr<FJsonObject>& Frame)
    {
        if(!Initialized || Frame->GetBoolField(TEXT("reinitialize")))
        { Link.Initialize(FAnimationInitializeContext(&Proxy));Link.CacheBones(FAnimationCacheBonesContext(&Proxy));Initialized=true; }
        Active=Frame->GetBoolField(TEXT("active"));Row=MakeShared<FJsonObject>();Row->SetBoolField(TEXT("active"),Active);
        Row->SetNumberField(TEXT("directionBefore"),Direction());Double(Row,TEXT("timeBeforeRoot"),Timer());
        Row->SetNumberField(TEXT("directionAfterRoot"),Direction());Double(Row,TEXT("timeAfterRoot"),Timer());Row->SetBoolField(TEXT("becameRelevant"),false);
        Row->SetObjectField(TEXT("beforeShared"),Shared());
        Tap.Before=[this]()
        { Row->SetNumberField(TEXT("directionAfterRoot"),Direction());Double(Row,TEXT("timeAfterRoot"),Timer());
          Row->SetBoolField(TEXT("becameRelevant"),Main->GetSubsystem<FAnimSubsystemInstance_NodeRelevancy>().GetNodeRelevancy(*State).HasJustBecomeRelevant());
          Row->SetObjectField(TEXT("beforeShared"),Shared()); };
    }
    void Prepared()
    {
        Row->SetNumberField(TEXT("directionAfter"),Direction());Double(Row,TEXT("timeAfter"),Timer());Row->SetObjectField(TEXT("shared"),Shared());
        Row->SetNumberField(TEXT("state"),Machine->GetCurrentState());Number(Row,TEXT("elapsed"),Machine->GetCurrentStateElapsedTime());
        TArray<TSharedPtr<FJsonValue>> Entries;
        for(int32 I=0;I<2;++I)
        { const auto E=MakeShared<FJsonObject>();E->SetStringField(TEXT("asset"),Path(Sources[I]->GetSequence()));
          E->SetBoolField(TEXT("active"),Active && Machine->GetCurrentState()==I);Number(E,TEXT("prepared"),FNodeAccess::Time(*Sources[I]));
          Number(E,TEXT("explicit"),Sources[I]->GetExplicitTime());Number(E,TEXT("cachedWeight"),Sources[I]->GetCachedBlendWeight());
          Number(E,TEXT("orientationAngle"),Orientations[I]->LocomotionAngle);Number(E,TEXT("orientationAlpha"),Orientations[I]->GetAlpha());
          Number(E,TEXT("strideSpeed"),Strides[I]->LocomotionSpeed);Number(E,TEXT("strideAlpha"),Strides[I]->GetAlpha());Entries.Add(MakeShared<FJsonValueObject>(E)); }
        Row->SetArrayField(TEXT("sources"),Entries);
    }
    static void Clock(FAnimNode_AssetPlayerBase& Source,const TSharedPtr<FJsonObject>& E)
    {
        const auto& D=FNodeAccess::Delta(Source);const auto& M=FNodeAccess::Marker(Source);
        Number(E,TEXT("time"),FNodeAccess::Time(Source));Number(E,TEXT("previous"),D.GetPrevious());Number(E,TEXT("delta"),D.Delta);
        E->SetNumberField(TEXT("markerPrevious"),M.PreviousMarker.MarkerIndex);E->SetNumberField(TEXT("markerNext"),M.NextMarker.MarkerIndex);
        Number(E,TEXT("markerPreviousDistance"),M.PreviousMarker.MarkerIndex==-2 ? 0 : M.PreviousMarker.TimeToMarker);
        Number(E,TEXT("markerNextDistance"),M.NextMarker.MarkerIndex==-2 ? 0 : M.NextMarker.TimeToMarker);
    }
    bool Finish(FSyncProxy& Sync,FAnimInstanceProxy& Proxy,USkeleton* Skeleton)
    {
        const auto* Group=Sync.GetSyncGroupMapRead().Find(TEXT("Locomotion"));
        for(int32 I=0;I<2;++I)
        {
            const auto E=Row->GetArrayField(TEXT("sources"))[I]->AsObject();Clock(*Sources[I],E);
            const auto* Tick=Group ? Group->ActivePlayers.FindByPredicate([&](const FAnimTickRecord& T){return T.TimeAccumulator==FNodeAccess::TimeAddress(*Sources[I]);}) : nullptr;
            E->SetBoolField(TEXT("tickRegistered"),Tick!=nullptr);if(Tick) { Number(E,TEXT("rate"),Tick->PlayRateMultiplier);E->SetNumberField(TEXT("leader"),Group->GroupLeaderIndex); }
        }
        Number(Row,TEXT("blendWeight"),Blend->BlendWeights[0]);Number(Row,TEXT("hipFireWeight"),Hip->GetCachedBlendWeight());
        Row->SetStringField(TEXT("hipFireAsset"),Path(Hip->GetSequence()));Row->SetBoolField(TEXT("hipFireActive"),Active && FAnimWeight::IsRelevant(Blend->BlendWeights[0]));
        const auto H=MakeShared<FJsonObject>();Clock(*Hip,H);Row->SetObjectField(TEXT("hipClock"),H);
        const auto L=MakeShared<FJsonObject>();const auto& D=FNodeAccess::Delta(*Lean);
        Number(L,TEXT("time"),Lean->GetAccumulatedTime());Number(L,TEXT("pin"),Lean->GetPosition().X);Number(L,TEXT("cachedWeight"),Lean->GetCachedBlendWeight());
        Number(L,TEXT("previous"),D.GetPrevious());Number(L,TEXT("delta"),D.Delta);TArray<TSharedPtr<FJsonValue>> Samples;
        for(const auto& S : FLeanAccess::Samples(*Lean))
        { const auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("index"),S.SampleDataIndex);Number(V,TEXT("weight"),S.TotalWeight);Number(V,TEXT("weightRate"),S.WeightRate);
          PRAGMA_DISABLE_DEPRECATION_WARNINGS
          Number(V,TEXT("time"),S.Time);Number(V,TEXT("previous"),S.PreviousTime);
          PRAGMA_ENABLE_DEPRECATION_WARNINGS
          Number(V,TEXT("deltaPrevious"),S.DeltaTimeRecord.GetPrevious());Number(V,TEXT("delta"),S.DeltaTimeRecord.Delta);Samples.Add(MakeShared<FJsonValueObject>(V)); }
        L->SetArrayField(TEXT("samples"),Samples);Row->SetObjectField(TEXT("lean"),L);
        if(Active)
        { FPoseContext Pose(&Proxy);Link.Evaluate(Pose);auto Output=LyraCyclePoseProbe::PoseData(Pose.Pose,Pose.Curve,Pose.CustomAttributes,Skeleton->GetReferenceSkeleton());
          if(!Output) return false;Row->SetObjectField(TEXT("output"),Output); }
        return true;
    }
};
}

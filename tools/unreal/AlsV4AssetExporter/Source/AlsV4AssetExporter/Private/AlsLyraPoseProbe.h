#pragma once
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AttributesRuntime.h"
#include "ReferenceSkeleton.h"
#include "Dom/JsonObject.h"

namespace LyraCyclePoseProbe
{
TSharedPtr<FJsonObject> PoseData(const FCompactPose& Pose, const FBlendedCurve& Curves,
    const UE::Anim::FStackAttributeContainer& Attributes, const FReferenceSkeleton& Reference);
}

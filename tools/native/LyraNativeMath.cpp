#include <immintrin.h>
#include <cmath>

// UE Win64 FRotator::Quaternion uses MSVC's vector sin/cos. Its rounding
// differs from scalar CRT sin/cos before RootYaw reaches the FootPlant rig.
// This bridge has no Unreal headers, assets, state or engine dependency.
extern "C" __declspec(dllexport) void lyra_root_sincos(float yaw, double* sine, double* cosine)
{
    const double angle = (double(yaw) - 360.0 * std::trunc(double(yaw) / 360.0)) *
        ((3.1415926535897932384626433832795 / 180.0) * 0.5);
    __m128d cosines;
    const __m128d sines = _mm_sincos_pd(&cosines, _mm_set1_pd(angle));
    *sine = _mm_cvtsd_f64(sines);
    *cosine = _mm_cvtsd_f64(cosines);
}

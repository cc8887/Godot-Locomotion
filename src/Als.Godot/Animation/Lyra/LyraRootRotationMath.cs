using System.Runtime.InteropServices;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal static class LyraRootRotationMath
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SinCos(float yaw, out double sine, out double cosine);
    private static readonly Lazy<AlsYawSinCos?> Native=new(()=>
    {
        // Godot may load managed assemblies from bytes, so normal adjacent
        // assembly DLL probing does not cover the local project runtime.
        var paths=new[]{
            System.IO.Path.Combine(AppContext.BaseDirectory,"LyraNativeMath.dll"),
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!,"LyraNativeMath.dll"),
            ProjectSettings.GlobalizePath("res://assets/generated/lyra_als/native_win64/LyraNativeMath.dll")};
        foreach(var path in paths)
            if(System.IO.File.Exists(path))
            {
                var bridge=Marshal.GetDelegateForFunctionPointer<SinCos>(NativeLibrary.GetExport(NativeLibrary.Load(path),"lyra_root_sincos"));
                return bridge.Invoke;
            }
        return null;
    });

    public static AlsQuaternion Quaternion(float yaw)
        // Original Win64 rounding remains available for existing references.
        // The shared managed path also works without the optional DLL.
        => AlsRootRotationMath.Quaternion(yaw,
            OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture==Architecture.X64 ? Native.Value : null);
}

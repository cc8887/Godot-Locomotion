namespace GodotAls.Core.Math;

public static class AlsRandomStream
{
    // UE FRandomStream fractional draw, sharing the existing notify algorithm.
    public static float NextFraction(ref uint seed)
    {
        seed=unchecked(seed*196314165u+907633515u);
        return BitConverter.UInt32BitsToSingle(0x3f800000u|(seed>>9))-1f;
    }
}

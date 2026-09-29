namespace GardenGuardians;

/// <summary>
/// How far a garden's making has got, for the loading screen to show while it happens on another thread:
/// the steps that take a while (growing a big terrain, measuring its water, settling the Bramblekin) report here.
/// </summary>
public static class Loading
{
    private static int _fractionBits;
    private static volatile string _text = "";

    /// <summary>How far along, 0..1.</summary>
    public static float Fraction => BitConverter.Int32BitsToSingle(Volatile.Read(ref _fractionBits));

    /// <summary>What is being done now, in a few words.</summary>
    public static string Text => _text;

    /// <summary>Says the making has reached <paramref name="fraction"/> (0..1), doing <paramref name="text"/>.</summary>
    public static void Report(float fraction, string text)
    {
        Volatile.Write(ref _fractionBits, BitConverter.SingleToInt32Bits(Math.Clamp(fraction, 0f, 1f)));
        _text = text;
    }

    /// <summary>Starts over, for a new garden.</summary>
    public static void Reset() => Report(0f, "Starting");
}

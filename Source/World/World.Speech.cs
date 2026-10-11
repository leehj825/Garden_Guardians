namespace GardenGuardians;

/// <summary>A line a Bramblekin is saying aloud, shown in a bubble over its head for a few seconds.</summary>
public sealed class Speech
{
    public Speech(Bramblekin kin, string text, float seconds)
    {
        Kin = kin;
        Text = text;
        TimeLeft = Total = seconds;
    }

    public Bramblekin Kin { get; }
    public string Text { get; }
    public float TimeLeft { get; set; }
    public float Total { get; }
}

public sealed partial class World
{
    /// <summary>At most this many bubbles are up at once.</summary>
    private const int MaxSpeeches = 10;

    /// <summary>The bubbles up now (see <see cref="Say"/>).</summary>
    public List<Speech> Speeches { get; } = new();

    /// <summary><paramref name="kin"/> says <paramref name="text"/> in a bubble over its head for <paramref name="seconds"/> (replacing what it was saying).</summary>
    public void Say(Bramblekin kin, string text, float seconds = 5f)
    {
        Speeches.RemoveAll(s => s.Kin == kin);
        Speeches.Add(new Speech(kin, text, seconds));
        if (Speeches.Count > MaxSpeeches)
            Speeches.RemoveAt(0);
    }

    /// <summary>Whether <paramref name="kin"/> has a bubble up.</summary>
    public bool IsSpeaking(Bramblekin kin) => Speeches.Any(s => s.Kin == kin);

    private void UpdateSpeech(float deltaTime)
    {
        for (int i = Speeches.Count - 1; i >= 0; i--)
        {
            Speeches[i].TimeLeft -= deltaTime;
            if (Speeches[i].TimeLeft <= 0f || Speeches[i].Kin.IsDead)
                Speeches.RemoveAt(i);
        }
    }
}

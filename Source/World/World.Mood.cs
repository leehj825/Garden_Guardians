namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>How the garden is doing: 0 well, 1 worth a look, 2 needs help (for the colour of <see cref="MoodLabel"/>).</summary>
    public int MoodLevel { get; private set; }

    /// <summary>One word for the garden's state ("Thriving", "Hungry", "At war"...), recalculated every couple of seconds.</summary>
    public string MoodLabel { get; private set; } = "Calm";

    private float _moodTimer;

    private void UpdateMood(float deltaTime)
    {
        _moodTimer -= deltaTime;
        if (_moodTimer > 0f)
            return;
        _moodTimer = 2f;

        int living = 0, hungry = 0, sick = 0;
        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead)
                continue;
            living++;
            if (kin.IsHungry)
                hungry++;
            if (kin.IsSick)
                sick++;
        }
        float hungryShare = living == 0 ? 0f : hungry / (float)living;
        float sickShare = living == 0 ? 0f : sick / (float)living;
        (MoodLabel, MoodLevel) =
            living == 0 ? ("Empty", 2)
            : hungryShare > 0.35f || _starvedThisSeason >= 2 ? ("Hungry", 2)
            : CurrentWars > 0 ? ("At war", 2)
            : sickShare > 0.2f ? ("Sickly", 2)
            : IsStorming ? ("Stormy", 1)
            : hungryShare > 0.15f ? ("Lean", 1)
            : Spider is { IsDead: false, State: SpiderState.Hunting or SpiderState.Pouncing } ? ("Spider!", 1)
            : hungryShare < 0.08f && sickShare < 0.05f && living >= 12 ? ("Thriving", 0)
            : ("Calm", 0);
    }

    /// <summary>A clan's larder in a word, from its stored food against its members: "well stocked", "lean" or "short of food".</summary>
    public string DescribeLarder(KinGroup clan)
    {
        int members = Math.Max(1, clan.Members.Count(m => !m.IsDead));
        float perMember = StoredFood(clan) / (float)members;
        return perMember >= WinterFoodPerMember ? "well stocked" : perMember >= 1f ? "lean" : "short of food";
    }
}

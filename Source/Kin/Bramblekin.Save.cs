using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>Save/Load: its lasting state — who it is, its family and ties, its standing, what it knows and remembers.</summary>
    public KinSave ToSave() => new()
    {
        Id = ID,
        GivenName = GivenName,
        FamilyName = FamilyName,
        Sex = Sex,
        Aggression = Personality.Aggression,
        Sociability = Personality.Sociability,
        Intelligence = Personality.Intelligence,
        Position = Position,
        Health = Health,
        Hunger = Hunger,
        Age = _age,
        Lifespan = _lifespan,
        TimeHere = _timeHere,
        BornHere = _bornHere,
        Generation = Generation,
        MotherId = ParentIds?.Mother,
        FatherId = ParentIds?.Father,
        MotherName = ParentNames?.Mother,
        FatherName = ParentNames?.Father,
        Children = Children,
        GroupId = GroupId,
        Home = Home is { IsCollapsed: false } home ? home.ID : null,
        Job = Job,
        Loyalty = Loyalty,
        Reputation = Reputation,
        HasLeftGroup = HasLeftGroup,
        FormerGroups = _formerGroups.ToList(),
        KnownKins = new Dictionary<int, RelationshipState>(_knownKins),
        Partner = Partner is { IsDead: false } partner ? partner.ID : null,
        Widowed = IsWidowed,
        Mourning = _mourningTimer,
        KnowsFarming = KnowsFarming,
        JoinedAt = _joinedAt,
        Dangers = _dangers.Places.Select(p => new PlaceSave(p.Where, p.When)).ToList(),
        FoodMemory = _foodMemory is { } memory ? memory : null,
        CarryingFood = _carried is not null,
        LeaderSeconds = LeaderSeconds,
        SpiderKills = SpiderKills,
        Errand = _errand is { } errand
            ? new ErrandSave
            {
                Kind = errand.Kind, From = errand.From, To = errand.To, Destination = errand.Destination.ID, Load = errand.Load,
                TwigsOwed = errand.TwigsOwed, Payment = errand.Payment, Returning = errand.Returning,
            }
            : null,
    };

    /// <summary>Save/Load: a Bramblekin as it was saved (its home, partner and errand are linked up afterwards — see <see cref="LinkSave"/>).</summary>
    public static Bramblekin FromSave(KinSave save, Random rng)
    {
        var kin = new Bramblekin(save.Position, rng, new Personality(save.Aggression, save.Sociability, save.Intelligence))
        {
            ID = save.Id,
            Sex = save.Sex,
            Health = save.Health,
            Hunger = save.Hunger,
            _age = save.Age,
            _lifespan = save.Lifespan,
            _timeHere = save.TimeHere,
            _bornHere = save.BornHere,
            Generation = save.Generation,
            Children = save.Children,
            GroupId = save.GroupId,
            Job = save.Job,
            Loyalty = save.Loyalty,
            Reputation = save.Reputation,
            HasLeftGroup = save.HasLeftGroup,
            IsWidowed = save.Widowed,
            _mourningTimer = save.Mourning,
            KnowsFarming = save.KnowsFarming,
            _joinedAt = save.JoinedAt,
            _foodMemory = save.FoodMemory is { } memory ? memory : null,
            LeaderSeconds = save.LeaderSeconds,
            SpiderKills = save.SpiderKills,
        };
        _nextId = Math.Max(_nextId, save.Id + 1);
        kin.Christen(save.GivenName, save.FamilyName);
        if (save.MotherId is { } mother && save.FatherId is { } father)
            kin.ParentIds = (mother, father);
        if (save.MotherName is { } motherName && save.FatherName is { } fatherName)
            kin.ParentNames = (motherName, fatherName);
        foreach (Guid former in save.FormerGroups)
            kin._formerGroups.Add(former);
        foreach (var (id, relationship) in save.KnownKins)
            kin._knownKins[id] = relationship;
        kin._dangers.Load(save.Dangers.Select(p => ((Vector3)p.Where, p.When)));
        return kin;
    }

    /// <summary>Save/Load, second pass: its home, partner and errand, once every Bramblekin and shelter exists again.</summary>
    public void LinkSave(KinSave save, IReadOnlyDictionary<int, Bramblekin> kin, IReadOnlyDictionary<int, Shelter> shelters)
    {
        Home = save.Home is { } homeId && shelters.TryGetValue(homeId, out Shelter? home) ? home : null;
        Partner = save.Partner is { } partnerId && kin.TryGetValue(partnerId, out Bramblekin? partner) ? partner : null;
        if (save.Errand is { } errand && shelters.TryGetValue(errand.Destination, out Shelter? destination))
        {
            _errand = new Errand
            {
                Kind = errand.Kind, From = errand.From, To = errand.To, Destination = destination, Load = errand.Load,
                TwigsOwed = errand.TwigsOwed, Payment = errand.Payment, Returning = errand.Returning,
            };
        }
    }
}

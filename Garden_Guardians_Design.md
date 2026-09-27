# Garden_Guardians_Design.md

*Last updated: 2026-09-27 — the **Emergent Survival** pivot, now with
**settling and society**, and a living population: **seasons**, **births**
and **villages** that bud off daughter groups. The game is no longer a
macro-RTS faction simulator: there are no factions, no top-down economy
and no faction wars. Every Bramblekin is an individual agent with its own
Personality and needs.
Individuals survive alone, settle into homes they build, and band together
because groups survive better; groups are run by a Leader whose decisions
followers can obey, resent, or rebel against. The macro-RTS design that
preceded this is kept below, clearly marked, as a historical record; the
original "player-as-god" pitch that preceded *that* is kept at the very
bottom.*

## Core Concept
**Genre:** Emergent Individual-Agent Survival Simulation / Spectator Game
**The Hook:** A 100m×100m procedurally hilly backyard is home to a
population of Bramblekin — tiny creatures who each have to find their own
food, survive the local wildlife, and decide for themselves whom to trust.
Nobody assigns them teams. A newcomer forages alone, then builds a tent
from fallen twigs and stocks it with food. Some stay homesteaders; others
band together, move into one home, upgrade it into a house and hunt big
game as a pack, under a Leader who decides what the group does and who
eats first. Followers who don't like how they're led can walk out,
split off, or challenge the Leader for the job — at the cost of losing the
group's protection. The year turns from a plentiful summer to a lean
winter; thriving groups raise young who inherit their parents' traits,
outgrow their House, build a village around it, and bud off daughter
groups. The player is a spectator with a camera, and can tap any single
Bramblekin to see what makes it tick.

## The Core Loop (as implemented)
Hunger rises → each Bramblekin forages loose Food within its own
Intelligence-scaled senses, or eats from its home's store → threats (the
Wolf Spider, Hornets, raiders, hostile Bramblekin) are fought or fled on a
per-individual Aggression roll — or hidden from at home → fed and safe, a
group member does the job its Leader gave it, and anyone else builds,
stocks and rests in its home → the rest of the time Bramblekin wander and
meet each other → every meeting is resolved from both sides' situation
and traits: a friendship, a new or bigger group, a shared meal, a
struggling loner taken in, or a robbery and a lifelong enmity → Leaders
decide group goals every few seconds; followers' loyalty rises and falls
with how they're treated, and the disloyal leave, split off or stage a
coup → thriving groups raise young and grow into villages, while the
seasons swing the food supply from plenty to scarcity and back → new
solitary wanderers drift in from the map's edge whenever the population
runs low.

## The Spectator Camera
The player's entire "input" is camera control — a smooth, Google-Maps-style
controller (`TouchCameraController`) layered over a fixed-angle
overhead camera:
*   **One-finger drag** (or a held left mouse button, for desktop
    testing) pans the camera across the terrain's X/Z plane, scaled by
    how far zoomed out the camera currently is so it feels consistent at
    any zoom level.
*   **Two-finger twist** rotates the whole world around the camera's
    current focus point (Target) on the Y axis — the camera's height and
    distance don't change, only the compass direction it's looking from.
*   **Two-finger pinch** zooms in/out, clamped between 8m and 220m from
    Target.
*   **Two fingers sliding up/down together** tilts the camera's pitch —
    dragging down flattens toward a top-down view, dragging up tilts
    into a lower, more oblique angle — clamped to roughly 15°–85° so it
    can never flatten to the horizon or flip past straight down.
*   All four gestures have tunable sensitivity constants
    (`PanSensitivity`, `RotationSensitivity`) so the overall feel can be
    dialed in without touching the underlying math.
*   A tap that never turns into a drag still reaches `WorldTapInput`
    (see below) — the two systems track drag distance independently so
    they never fight over the same touch.
*   Two correctness fixes worth remembering if this code is touched
    again: raylib's touch-point *index* (0, 1) is not guaranteed to stay
    mapped to the same physical finger between frames, so the two-finger
    gesture matches each frame's points to whichever of last frame's
    points they're actually closest to rather than trusting index order;
    and `Atan2`'s branch cut at ±180° means a raw frame-to-frame angle
    delta must be wrapped back into `(-π, π]` before use, or a rotation
    that happens to cross that boundary produces a spurious ~2π jump
    (a "flip").

## Player Interaction (what's left of it)
*   **Kin inspection:** tapping a Bramblekin selects it and shows its
    live state in the top-right Kin Inspector — State, Health, Hunger,
    its three Personality traits (and the detection radius its
    Intelligence buys it), its sex, its group and role (Solitary/Leader/
    Follower, and whether it's still young), its generation and mother
    and father, its home
    (tent or house, construction progress, store), its job and
    its group's current goal (and whether the Leader eats first), its
    Loyalty (followers) and Reputation, and how many Friends, Enemies and
    Neutral acquaintances it has. The selected Bramblekin is ringed on
    the map, with its detection radius traced over the hills. Tapping
    empty ground clears the selection.
*   Everything else — eating, fighting, fleeing, building, befriending,
    robbing, raiding, grouping, leading, obeying and rebelling — is the
    Bramblekin's own business.
*   The HUD shows the year and season (with its food multiplier), homes
    (tents, houses, sites, food stored, villages and buddings), what the
    colony is doing, deaths by cause, births and the highest generation,
    and the politics so far (walk-outs, splits, coups, exiles, raids).
    The event console on the left narrates the seasons, alliances,
    settlements, villages, births, hunts, leader decisions, rebellions
    and deaths.

## Code Layout & Performance
*   **Layout:** `Program.cs` is only the entry point. Everything else is
    under `Source/`, one type per file: `Engine/` (camera, terrain, tap
    input, spatial grid, movement), `World/` (the `World` partial class
    split by concern — core, society, group homes & villages, leadership,
    rebellion, seasons, births, shelters, hunting, interactions, spawning,
    stats, rendering — plus Food, Twigs, Shelters and Garden Props), `Kin/`
    (the `Bramblekin` partial class with one file per need — Hunger,
    Safety, Duty, Settle, Social — plus Hunting, Loyalty, Lineage, Actions
    and Drawing, and the
    Personality, relationship, group and society types), `Wildlife/` and
    `Game/`.
*   **Squared-distance math everywhere:** targeting/aggro/encounter checks
    compare squared distances against a squared threshold.
*   **Staggered perception:** each Bramblekin rescans its surroundings
    (nearest Food, Twig, Grub, Stag Beetle, most pressing threat) every
    0.25s on its own randomly offset timer; movement and combat run every
    frame off the cached results. Leaders decide every 5s.
*   **A spatial grid** (`SpatialGrid<T>`) buckets loose Food, Twigs and
    the Colony into 10m×10m chunks, rebuilt once a frame; `QueryRadius`
    walks exactly the chunks a detection radius overlaps.
*   **Object pooling:** Food and Twigs are pre-allocated fixed-size pools.
*   **Deferred spawns/removals** are applied once per frame
    (`World.CommitPendingChanges`); rebellions are queued during the
    Leaders' decision pass and applied after it.
*   **Raylib culling** skips anything off-screen or beyond
    `World.RenderRadius`.
*   **Headless mode:** `dotnet run -f net8.0 -p:DesktopOnly=true --
    --headless 600 --seed 1` steps the simulation with no window (roughly
    150× real time) and prints a population report every 30 simulated
    seconds, every notable event, and a summary with the **survival
    trend** (deaths and meat hunted per kin-hour lived as a Wanderer,
    Homesteader, Member or Independent), how differently each style of
    Leader runs its group, the tally of rebellions, villages and buddings,
    and the lineage (births, generations, and how the living's traits have
    drifted). A 4-hour run takes under 3 minutes. Measuring per
    kin-hour, rather than by average lifespan, keeps the comparison fair
    however late each Bramblekin arrived.

## Individuals: Personality & the Needs Hierarchy
*   **Personality (DNA):** every newcomer rolls three traits, each
    uniformly random in 0..1, the moment it arrives; one born here
    inherits the average of its parents' (see Growth below). Fixed for
    life:
    *   **Aggression** — the odds of fighting rather than fleeing a
        threat, of turning on a neighbour or raiding a store when
        starving, of challenging a Leader; how hard it hits (5–11 per
        strike); and, as a Leader, how much it prizes hunting and whether
        it eats first. Its body is tinted redder the more Aggressive it is.
    *   **Sociability** — the urge to meet strangers (vs. walking away
        from anyone who crowds it, below 0.35), the odds a meeting ends
        in friendship, whether two meeting Bramblekin band together (both
        ≥ 0.6), how readily a struggling loner asks to join a group, how
        loyal it naturally is, and — as a Leader — whether it shares the
        store fairly and takes in newcomers.
    *   **Intelligence** — detection radius from 5m to 20m, how soon a
        newcomer settles (10–50s), and its claim to lead.
*   **Hunger:** rises 1 point per second from 0 to 100. At 60 a
    Bramblekin is *hungry*; at 80 *starving*; at 100 it loses 1 HP a
    second until it eats or dies. One piece of Food removes 40 Hunger and
    restores 6 HP. Resting at home heals too (1 HP per 1.5s; faster in a
    House).
*   **The strict needs hierarchy** — every frame, each Bramblekin serves
    exactly one need, in this order:
    0.  **A leadership duel,** once started, is settled first.
    1.  **Critical (Hunger):** eat what it's carrying; else keep robbing
        the neighbour it committed to; else, with a predator about, go
        home to eat from the store; else walk to the nearest loose Food it
        can see; else eat from its home's store (if the group's sharing
        rule allows); else scavenge an abandoned store; else hunt a
        visible Grub, or a Stag Beetle with its pack; else (starving and
        Aggression ≥ 0.55) raid someone's store, or stalk the nearest
        outsider carrying food; else a follower goes for Food its Leader
        can see, or tags along if the Leader is searching too; else it
        searches — first back where it last saw Food, then further afield.
        A hungry Bramblekin *will* brave a Hornet swarm for a berry.
    2.  **Safety:** the most pressing threat inside its detection radius
        — whoever just hit it, else the nearest Wolf Spider, Hornet,
        Bramblekin attacking it, raider heading for its home, or foe
        attacking/fighting a groupmate — gets one fight-or-flight roll:
        Aggression, +0.15 per groupmate within 6m, +0.3 if defending a
        groupmate or its home, −0.25 against the Wolf Spider; an idle
        Hornet at its nest is always avoided, never fought. Fighters flee
        once at or below 40% Health. Fleers run home if it's close,
        otherwise straight away; inside a home they simply hide.
    3.  **Duty:** a group member loyal enough to take orders does the job
        its Leader gave it (see Leadership below).
    4.  **Settle:** a loner with no home, once it has looked around a
        while, moves into an abandoned shelter it can see or marks out a
        site near where it last found food and builds a Tent. With a home
        (its own, or its group's), it rests there when hurt, carries food
        into the store, fetches food lying within 20m of home, and hunts
        Grubs near home while the store is low.
    5.  **Social:** a homeless loner pockets one spare bite; a group
        member helps any Stag Beetle hunt its pack starts; a member of a
        homeless group stays within 3m of its Leader; anyone settled
        wanders around home (and spends some time inside) — in winter it
        huddles inside instead (see Growth below); anyone else
        rests and then — with odds of Sociability × 0.8 — goes to meet a
        stranger, or (as a loner) walks away from whoever is crowding it,
        or just wanders.

## Settling: Shelters & Stores
*   **Twigs** fall around the big Twig props (up to 50 loose, pooled);
    they never rot. A builder carries one at a time to its site.
*   **Tent:** 3 twigs. Room for 2, a store of 4 Food that never rots.
*   **House:** a group's upgrade of a Tent, once the group outgrows its
    housing — 6 more twigs. Room for 6, a store of 12, and healing half
    again as fast. Drawn as walls under a roof, flying its group's colour.
*   A built home hides whoever is inside it from the Wolf Spider's pounce
    and from Hornets — unless more residents are crammed inside than it
    has room for, in which case it protects nobody.
*   **Ownership:** a home belongs to one Bramblekin or to a group. Nobody
    else may eat from it except by raiding. A home whose owner died (or
    whose group dissolved) is **abandoned**: anyone hungry may scavenge its
    store, a homeless Bramblekin may move in, and after 90s it collapses,
    spilling its store as loose Food.
*   **Group homes:** a group adopts the best home any member already has
    (a House over a Tent, finished over a site, then the fuller store);
    failing that, its Leader marks out a site. Every member moves in,
    carrying its old store over; old personal tents are abandoned. The
    last survivor of a group keeps its main home. A group can grow into a
    **village** of several homes — see Growth below.

## Hunting & Defending
*   **Stag Beetles** (60 HP, at most 2 on the map) graze peacefully until
    attacked, then turn on their attackers and bite for 7. A lone
    Bramblekin usually has to break off before it wins; a pack brings one
    down for **8 pieces of meat**. A Bramblekin attacks one only when a
    groupmate is on it or within 8m — or alone when hungry and at least
    0.75 Aggressive — or when its Leader sends it as a Hunter.
*   **Grubs** are small prey for anyone: a hungry Bramblekin that can't
    see Food hunts one; a settler with a low store hunts those near home.
*   **Defending home:** residents who see a raider heading for their home
    (or any of their village's homes) treat it as a threat they're keen to
    fight; a Leader rallies its group to Defend against anything that
    comes within 10m of home.
*   **Raids:** a starving, highly Aggressive Bramblekin raids someone
    else's store, making Enemies of everyone who lives there.

## Encounters, Relationships & Groups
*   **Relationships:** each Bramblekin keeps `KnownKins` — every other
    Bramblekin it has met, by ID, as **Friend**, **Neutral** or
    **Enemy**. Enemy is permanent. Dead Bramblekin are forgotten.
*   **The Encounter:** whenever two living Bramblekin come within 1.2m of
    each other (at most once per pair every 12s), the World resolves it,
    in priority order:
    1.  **Groupmates** never fight — a fed one carrying food hands it to
        a hungry, empty-handed one.
    2.  **Hostility:** a Bramblekin that's starving, empty-handed, can't
        see any loose Food, and has Aggression ≥ 0.55 rolls its
        Aggression (halved against a Friend) to attack the other — which
        must be carrying food. Both become Enemies for good; the attacker's
        first landed blow steals the food.
    3.  **Enemies** simply pass each other by.
    4.  **Friends** may share food (odds: the giver's Sociability).
    5.  **Alliance:** if both are threatened by a predator right now, or
        both have Sociability ≥ 0.6, they band together under one
        `GroupId` — a new group, the one either already belongs to, or
        (both grouped) the larger absorbs the smaller, keeping its homes
        near its own as part of its village. Refused past the group's
        housing (at least 6, at most 18), with a known Enemy inside, or
        into a group either once left.
    6.  **Joining for survival:** failing that, a *struggling* loner
        (hungry, hurt or homeless) meeting a member of a group with a
        finished home may ask to join (odds 0.3 + 0.5 × its Sociability),
        and is taken in if there's room and — once the group has 3 or
        more — its Leader is welcoming enough (odds 0.3 + 0.7 × the
        Leader's Sociability).
    7.  Otherwise they become **acquainted**: Friends with odds of the
        product of their Sociability, Neutral otherwise.
*   **Group Dynamics:** followers of a homeless group stay near their
    Leader; a settled group lives around its home. Members lend each
    other courage in a fight, treat anything attacking or fighting a
    groupmate as their own threat, borrow their Leader's sharper senses
    when hungry, and — the Wolf Spider leaves anyone with 2 or more others
    within 2.5m alone — are safer in company. A group whittled down to one
    survivor dissolves.

## Leadership & Society
*   **The Leader** is elected when a group forms (or its Leader is gone):
    the member with the highest **leadership score**, Intelligence +
    0.15 × **Reputation**. Reputation (up to 3) is earned by felling a
    Stag Beetle or the Wolf Spider (+1), finishing a home (+0.5) and
    winning a leadership duel (+1). A sitting Leader stays until it dies,
    leaves, or loses a challenge.
*   **Decisions:** every 5s (at once if a threat turns up near home) the
    Leader scores four **goals** from the group's situation, weighted by
    its own personality, and picks the best:
    *   **Defend** — a threat within 10m of home: 5 + 2 × Aggression.
    *   **Settle** — something is under construction: the first home (3 +
        2 × Intelligence — urgent), or an upgrade or new village home (1.5
        + 2 × Intelligence).
    *   **Hunt** — a Stag Beetle within 20–40m of home (further for a
        bolder Leader) and at least 2 members: 0.8 + 2 × Aggression +
        how empty the store is.
    *   **Stockpile** — 1 + 2 × how empty the stores are + 0.5 × (1 −
        Aggression); in autumn a far-sighted Leader adds up to 1.5 ×
        Intelligence more to stock up for winter.
    *   In winter, Hunt gets +1: big game carries a group through the
        lean months.
*   **Jobs:** the Leader hands out jobs to match — Guards from the
    Aggressive and healthy (Defend), Builders from the most Intelligent
    half (Settle), Hunters from the most Aggressive half (Hunt), and
    Gatherers otherwise (with one Guard kept home in a group of 4+).
    Members carry them out in the Duty need: Guards attack the threat or
    keep within 3m of home; Hunters chase the chosen beetle (or a Grub);
    Builders fetch twigs; Gatherers bring food within 25m of home into the
    stores (their own home's, or the nearest village home with room).
    Hunters and Guards stand down to rest at half Health. The young get
    no job.
*   **The sharing rule:** an unsociable (< 0.4), Aggressive (≥ 0.5)
    Leader **eats first** — everyone else may only take from the store once
    starving. Otherwise the store is shared equally.
*   **Leader styles:** Warlike (Aggression ≥ 0.6), Planner (Intelligence
    ≥ 0.6) or Moderate. In headless runs, Warlike-led groups spend roughly
    a quarter of their time hunting; Planners and Moderates mostly
    stockpile and build. Leader-first groups are rare — the rule needs an
    unsociable, Aggressive Leader, and those tend to lose their followers.

## Loyalty & Rebellion
*   **Loyalty** (0..1, followers only) is re-weighed at every Leader
    decision. Left alone it settles back toward a natural level (0.45 +
    0.3 × Sociability). It rises with meals from the store, a home with
    food in it, having been defended by a groupmate and friendship with
    the Leader; it falls with hunger (more when starving), being turned
    away from the store, a Leader who eats first, dangerous orders
    (Hunter on a hunt, Guard on a defence — felt less by the Aggressive)
    and injury. Members of a badly run group share most of those
    grievances, so they tend to sour together.
*   **Obedience:** below 0.3 a follower ignores its job and fends for
    itself.
*   **Rebellion:** below 0.2, at each decision a follower rebels with odds
    of 0.35, according to its personality:
    *   **Challenge** — Aggression ≥ 0.6, healthy, and at least 85% as
        strong as the Leader (leadership score + half its Aggression +
        a little for its current health): a **duel** fought until one
        yields at half Health. It's a contest, not a feud — no enmity, and
        groupmates stay out of it. A winning challenger becomes Leader; a
        Leader who wins exiles the challenger if at all Aggressive (≥ 0.4),
        otherwise keeps it on, humbled.
    *   **Splinter** — Sociability ≥ 0.4, with at least one other follower
        below 0.4 loyalty: the unhappy members leave together as a new,
        homeless group with a Leader of their own.
    *   **Leave** — otherwise: it walks out and goes it alone as an
        **Independent**, losing the group's home, store and defenders.
*   **Exile:** an Aggressive Leader (≥ 0.5) throws out a follower below
    0.15 loyalty (odds 0.25 per decision); the two become Enemies.
*   Nobody ever rejoins a group it left. An Independent can be taken in
    by another group, or build a home of its own.
*   **The survival trend** (headless, 3 seeds × 4 hours, with seasons and
    births): deaths per kin-hour are roughly 2.3 for Wanderers, 0.9 for
    Homesteaders, 1.1 for grown group Members and 1.9 for Independents;
    the young almost never die. Members hunt 15–20 pieces of meat per
    kin-hour against 5–8 for anyone alone. The summary breaks each rate
    down by cause: Members starve no more often than Homesteaders (about
    0.6 per kin-hour each), but die more often in fights with other kin
    (0.35 against 0.2) — the price of hunting, defending and politics.
    Settling more than halves a Wanderer's death rate; walking out of a
    group nearly doubles a Member's. And groups are where the future is: after a
    few years nearly everyone alive was born into one.

## Growth: Seasons, Births & Villages
*   **Seasons:** a year is four 150s seasons (10 minutes). Wild Berries
    grow at, and the lawn holds up to, 1.0× the normal amount in Spring,
    1.3× in Summer, 0.8× in Autumn and just 0.3× in Winter — the stores
    and the hunt have to carry everyone through. The lawn and sky shift
    colour (fresh green, summer green, yellowing brown, frost) and ease
    into each new season; the HUD shows the year, season and food
    multiplier, and each change is announced in the log.
*   **Leaders plan for the year:** in Autumn an Intelligent Leader
    stockpiles harder for the winter ahead; in Winter hunting big game
    is worth more.
*   **Wintering in:** in Winter, a Bramblekin with a home and nothing
    pressing to do huddles inside it — safe from Hornets and the Spider —
    and while sheltered its Hunger rises at half the usual rate.
*   **Births:** a thriving group raises young — checked at each Leader
    decision. It needs a House with at least 6 Food stored, 2 Food stored
    per member across its homes (3 in Autumn, enough for the winter; no
    births at all in Winter), a healthy, fed grown female and male to be
    the mother and father, no more than a third of the group hungry, room to grow (see villages)
    and 60s since its last birth. A birth costs 3 Food from the store.
*   **Sex:** every Bramblekin is female or male at even odds, newcomers
    and newborns alike. For now the only difference is that a birth takes
    one of each; the sexes forage, fight, build, lead and rebel alike.
*   **The young:** a newborn inherits the average of its parents'
    Personalities, ±0.15 on each trait; its Generation is one more than
    its older parent's. For 90s it's young: drawn small and growing, it
    stays close to home, may always eat from the store, and takes no job,
    no fights, no hunts, no raids and no part in politics (it can't be
    elected while there's an adult to lead). Over many generations the
    traits of the living drift away from a newcomer's 0.5 average — in
    whichever direction the garden rewards.
*   **Villages:** a group's size limit follows its housing — whatever its
    finished homes have room for (Tent 2, House 6), never below 6 or
    above 18 — and it may raise young up to 2 past that. A group that has
    outgrown its housing upgrades one of its Tents into a House; one whose
    homes are all Houses and full, with its stores at least half stocked,
    marks out another home within 12m of its main one — up to two more,
    making a village. One construction at a time: the Leader's Settle
    goal and its Builders work on whichever home is under way. Members
    live in the roomiest home, spread out of an overcrowded one, eat
    from and stock any of the village's stores, and defend them all. If
    the main home is lost, the village's next home becomes the main one.
*   **Budding:** a village with at least 10 members (plus up to 6 more
    under a Sociable Leader, who holds a bigger village together) lets
    the residents of one of its other Houses — at least 3 grown ones, not
    the Leader — set up as a daughter group of their own, keeping that
    House and electing a Leader of their own. No quarrel: nobody holds it
    against the old group. The parent village can then build again.
*   **The population** is no longer held at a fixed number: it rises
    through Summer and Autumn and thins in Winter and early Spring, and
    over hours settles wherever the garden's food and its pairings allow
    — about 28–50 in 4-hour headless runs (averaging ~37), where the old
    design sat at 30–40. Wandering
    Arrivals only top it up when it falls below 30; a safety cap of 150
    is never reached in practice.

## Food & Wildlife
*   **Food:** wild Berries grow passively (one every 0.6s, up to 100 on
    the map, both scaled by the season), about two-thirds in eight Berry
    Patches around Dandelions.
    Hunted Grubs, Stag Beetles and a slain Wolf Spider drop meat (same
    value). Loose Food rots after 60s; stored Food never does. A
    Bramblekin walking to a piece claims it (Dibs) so others look
    elsewhere.
*   **The Wolf Spider** (50 HP): hunts by vibration — any Bramblekin
    busy foraging, eating, hunting, robbing or raiding within 7m, unless
    it's inside a home or has company (2+ others within 2.5m) — and
    pounces; whatever it touches mid-pounce dies, except a Bramblekin
    that is Fighting it, which it Bites (10 damage) instead. After a kill
    it feeds for 20s. Brought down, it leaves 6 pieces of meat and a new
    spider moves in 60s later.
*   **Hornet swarms** (4 HP each, up to 12 on the map): nest in clusters
    of 3–5 around a random Garden Prop, chase anything within 3m that
    isn't inside a home, and sting for 3 damage a second.
*   **Grubs** (12 HP, up to 4): burrow in from the edge, eat loose Food
    (ignoring claims), skitter away from nearby Bramblekin, and drop 1–4
    pieces of meat when hunted down.
*   **Stag Beetles** (60 HP, up to 2): see Hunting & Defending.
*   **Garden Props:** Pebbles (solid), Twigs (where fallen twigs gather)
    and Dandelions (where berries grow).
*   **Wandering Arrivals:** every 15s, while fewer than 30 Bramblekin are
    alive, a new solitary one with a freshly rolled Personality wanders
    in from a random edge of the map — so a hard winter never ends the
    world, but growth beyond that has to come from births.

---

## The Macro-RTS Era (superseded — kept for history only)

Everything in this section describes the faction-based design the game
had *before* the Emergent Survival pivot. None of it exists in the
current build: `VillageHeart`, `Blueprint`, `Building` (Spore Farms,
Granaries, Tents, Cabins, Trading Posts, Breweries, Monuments), the Job
Managers, Crusades, Invasions, Vassal Tributes, Diplomacy/Goodwill, Amber,
Acorns, Aphids, the Rival Ant Colony and the Elder Spider were all
removed. It's kept purely as a record of what was built and why the game
moved on from it.

### Core Concept
**Genre:** Autonomous Multi-Agent Colony Simulation / Spectator God Game
**The Hook:** A 100m×100m backyard terrain is home to one or more tribes
of Bramblekin — tiny creatures who gather, build, fight and expand
entirely on their own. There is no player lever on the simulation
itself: no miracles, no resource spending, no unit orders. The player is
a spectator with a camera — panning, rotating, tilting and zooming over
a living, autonomous world — watching tribes rise, trade, go to war,
splinter into new factions, and (if they're wealthy and populous enough)
build a civilization-defining Monument.

### The Core Loop (as it was)
Bramblekin gather Food and Amber for their Village Heart → the Village
Heart autonomously sprouts new Bramblekin, drafts Militia, and queues its
own buildings in a fixed priority order → the tribe defends itself
against the Wolf Spider and, once provoked, rival tribes → a wealthy
tribe's territory grows and its economy diversifies (Nectar, Trading
Post) → an overcrowded tribe splits into a new faction (the True
Schism) → an exceptionally advanced tribe commits everything to the
Great Monument, an endgame civilization goal. The player watches all of
this happen in real time (with a debug time-scale control), and their
only interactions are re-seeding a dead world (Genesis) and tapping a
Village Heart to inspect that faction's stats.

### Player Interaction (what's left of it)
*   **Faction inspection:** tapping a Village Heart selects that faction
    and shows its live stats (Food, Population, Militia/Builder counts,
    Morale, Amber, Nectar) in the top-right Faction Ledger.
*   **Genesis:** once every Village Heart is gone (`World.IsWorldExtinct`),
    a blinking center-screen prompt invites a tap anywhere on the ground
    to reseed the world for free — the only way back from total
    extinction, since nothing else in the game can restart it.
*   Everything else — conscription, sprouting, building, trading,
    fighting, splintering, brewing Nectar, building the Monument — is
    the Village Heart's own autonomous business.

### Performance Architecture
The map grew from an original 20m×20m prototype to a full 100m×100m
terrain, which forced real engine work rather than just gameplay work:
*   **Squared-distance math everywhere:** every AI targeting/aggro/
    territory check compares squared distances against a squared
    threshold instead of calling `Vector3.Distance`/`MathF.Sqrt`.
*   **AI time-slicing:** each Bramblekin's expensive "Brain" work (target
    scanning) only runs on its own staggered frame
    (`World.FrameCounter % 15 == bramblekin.ID % 15`), spreading the cost
    evenly across the whole colony instead of every unit re-scanning
    every frame. The "Legs" (movement, combat, pickups) still run every
    frame off whatever target was cached on the last scan.
*   **A spatial grid** (`SpatialGrid<T>`) buckets Food Shards, Acorns,
    Amber Nodes and the Colony into 10m×10m chunks, rebuilt once a frame;
    a nearest-target search only ever looks at a searcher's own chunk and
    its 8 neighbors instead of the whole map.
*   **Object pooling:** Food Shards, Acorns and Amber Nodes are
    pre-allocated as fixed-size pools at startup (`IsActive` flag,
    `Activate`/`Deactivate`) instead of being constructed and destroyed
    on every spawn/pickup/despawn.
*   **Raylib culling:** entities and health bars that project entirely
    outside the camera's current viewport skip their draw call.
*   The window renders at the device's actual native resolution on
    Android (no more fixed 1280×720 virtual canvas letterboxed to fit),
    and UI text was scaled up across the board for legibility on a phone
    held at arm's length.

### Factions, Territory & Diplomacy (as it was)
*   **Default Peace:** every faction starts and stays at peace with
    every other by default. War (a Blood Feud) is only ever declared by
    a specific provocation — landing a damaging hit, or a caught
    trespasser turning out to be armed — never by mere proximity.
*   **Cultural Borders (dynamic territory):** a Village Heart's territory
    ring is no longer a flat 20m for every tribe alike. It's computed
    live as `15.0 + AmberStored × 0.5 + NectarStored × 2.0` meters
    (`VillageHeart.TerritoryRadius`) — a wealthy, advanced tribe's
    cultural reach physically grows, and its ring can overlap into a
    poorer neighbor's, letting it work resources closer to that rival's
    own base without ever counting as trespassing (Strict Border
    Control only ever excludes what falls inside the *other* faction's
    own ring — a bigger ring simply reaches further).
*   **Thievery:** a Gatherer caught picking up food inside a rival's
    territory is flagged as trespassing; that faction's Militia
    confronts it with a non-lethal Warning Shove unless the confrontation
    itself escalates into a Blood Feud (an armed trespasser, or one that
    lands a hit back).
*   **Base Razing & the Refugee Protocol:** a Village Heart ground down
    to 0 HP is razed, scattering loot and running the Refugee Protocol —
    survivors either found a new Village Heart as Pioneers (reusing the
    Schism machinery) or, if the map has no safe ground left, are
    assimilated wholesale into the attacker's faction.
*   **The True Schism:** an overcrowded, food-rich tribe splits roughly
    in half; the splinter group migrates well clear of every existing
    Village Heart and founds a new, differently-colored faction with a
    temporary Pioneer's Truce toward its parent.
*   **Faction Personalities:** each Village Heart rolls a fixed-for-life
    `FactionTrait` (Balanced, Militaristic, Agrarian) at creation, which
    only changes its Auto-Conscription ratio (how many Gatherers it
    keeps per Militia unit).
*   Diplomacy beyond this (a Diplomat unit, negotiated assimilation, paid
    truces) was designed but never built — see "Original Vision" below.

### The Economy (as it was)
*   **Food:** wild Berries (passive spawn), cracked Acorns (Cooperative
    Acorn Cracking — up to 3 Chitin-Mallet Gatherers working one Acorn
    at once), hunted Aphids, and a Spore Farm's steady passive income
    all become Food Shards, carried home and banked as `FoodStored`
    against a cap raised permanently by each completed Granary.
*   **Amber:** a scarce, map-wide resource. A well-fed Village Heart's
    Gatherers pursue it ahead of ordinary food (Maslow's Hierarchy) and
    bank it as `AmberStored` — spent on the Trading Post, Emergency Food
    Imports, the Nectar Brewery, the Great Monument, and (passively)
    widening the tribe's own Cultural Borders.
*   **The Nectar Brewery:** once a tribe reaches 20 Population and 10
    Amber Stored, it queues a Brewery (25 Construction Progress). Once
    built, it consumes 2 Food + 1 Amber every 30 seconds to brew 1
    Nectar — skipping a cycle rather than failing outright if the
    village can't currently afford it. Nectar is a permanent
    civilization buff, never spent: every point permanently adds 5%
    Gatherer walk speed (capped at +50% total) and further widens the
    tribe's Cultural Borders. It shows on the Faction Ledger in a
    distinct pink/purple.
*   **Auto-Construction priority order** (checked every frame, per
    Village Heart): Housing (Tent, once Population is capped) → Growth
    (Auto-Sprout) → Storage (Granary) → Spore Farm scaling → Trading
    Post → the Nectar Brewery. The Great Monument, once eligible,
    overrides all of it except population growth (see below).
*   **Upkeep & starvation:** a real food tax every 30 seconds; a village
    that can't pay it drains to zero Food Stored and loses one
    Bramblekin (a Gatherer over a Militia unit, when there's a choice).

### The Great Monument (civilization goal)
Once a Village Heart reaches 40 Population *and* has hoarded 50 Amber,
it commits its entire Builder effort to a Monument: every other Auto-
Construction phase (Tent, Granary, Spore Farm, Trading Post, Brewery)
stops queuing new work until the Monument is finished — only Auto-Sprout
(population growth) keeps running, since it isn't "a building." The
Monument itself requires 200 Construction Progress — by far the largest
single build in the game — and renders as a stepped, three-tier stone
pyramid with a gold capstone, dwarfing every other structure on the map.
Completing one raises a permanent, non-blinking, faction-colored banner
across the top of the screen — "*[Faction]* has completed the
Monument!" — that stays up for the rest of the game, and marks that
faction's advanced-civilization status even if its Village Heart is
later razed.

### Combat
*   **The Wolf Spider** (50 HP): hunts via a vibration-based aggro radius
    against Gathering/Returning Bramblekin, pounces, then feeds; a
    Militia unit is always its top defensive priority the instant one
    exists in territory, regardless of any standing Blood Feud
    ("Enemy of My Enemy" calls a temporary truce on every rival while a
    hunting/pouncing spider is in the ring). Squashed, it drops a
    Spider Fang and a Chitin piece, and respawns near an edge after a
    delay.
*   **Militia** (30 HP each): Poke for 15 damage (30 once that faction's
    Fang Pikes upgrade is unlocked, from banking 2 Spider Fangs) on a 1
    second cooldown; the spider Bites back for 10 on a 1.5 second
    cooldown. Either side hitting 0 HP is a real, permanent kill.
*   **Individual Equipment:** an un-upgraded Militia unit's first
    priority is fetching a Spider Fang (doubles its own Poke damage,
    pike renders silver); an un-upgraded Gatherer's is fetching a Chitin
    piece (unlocks targeting whole Acorns for Cooperative Cracking).
    Both are per-unit and perish with the Bramblekin that dies holding
    them.

---

## Original Vision (superseded — kept for history only)

Everything below this line was the game's original pitch, written before
the project committed to Pure Simulation. Some pieces were prototyped
and later deliberately removed (the Faith Pool, the Gust, the
Pebble-Drop, God's Shadow); most were never built at all (the Dewdrop,
the Sunbeam, the Diplomat, the tech tree, the seasonal Ark win
condition). None of it reflects the current game. It's preserved here in
case any of it is revisited as a genuinely new feature later.

### Original Core Concept
**Genre:** God Simulation / Real-Time Strategy
**The Hook:** You act as a benevolent spirit in a suburban backyard protecting the Bramblekin—tiny, industrious creatures. You cannot control them directly; instead, you manipulate the environment using physics-based "miracles" to guide their survival, economy, and wars.

### Original Core Gameplay Loop
Help creatures survive basic threats $\rightarrow$ Grow their society $\rightarrow$ Unlock new, powerful (but indirect) "miracles" $\rightarrow$ Manage larger conflicts and complex ecosystems $\rightarrow$ Face the unpredictable consequences of a maturing, independent civilization.

### The Miracles (Physics & Environment Manipulation)
*   **The Gust (Directional Force):** Draw a wind vector to blow away heavy leaves, push dropped resources, scatter insect swarms, or flip jumping spiders mid-air. *(Prototyped, later removed.)*
*   **The Pebble-Drop (Kinetic Impact):** Spawn a heavy physics object to crack hard nuts, crush enemies, or alter the NavMesh to create stepping stones across water. *(Prototyped, later removed.)*
*   **The Dewdrop (Weight & Moisture):** Spawn heavy water to weigh down grass blades (creating ramps), extinguish fires, or wash away enemy pheromone trails. *(Never built.)*
*   **The Sunbeam (Thermal Grid):** Focus light to bake mud into fast-walking clay paths, accelerate crop growth, or temporarily blind aerial predators. *(Never built.)*

### Miracle Economy: The "Faith" Resource
To prevent players from spamming physics objects and trivializing the survival aspect, miracles draw from a central, regenerative Faith Pool. *(Prototyped, later removed entirely along with the miracles it powered.)*

*   **Generation:** Faith regenerates slowly at a baseline rate. This rate is multiplied by village Morale (happy Bramblekin worship more) and by constructing Shrines.
*   **Action Costs:**
    *   *The Gust:* Low cost per swipe. Allows for quick, reactionary defense, but spamming it will drain the pool before a heavy predator arrives.
    *   *The Dewdrop:* Medium cost.
    *   *The Pebble-Drop:* High cost. Dropping a rock is a massive expenditure of energy, forcing the player to use it only for high-value targets or critical puzzle-solving.
    *   *The Sunbeam:* Continuous drain. It consumes Faith per second while held, requiring the player to be precise rather than sweeping it across the whole map.

### Miracle Unlocks: The Worship Milestones
Miracles unlock dynamically as the society evolves, tying your "God" powers directly to their technological and cultural milestones. You can only manipulate elements the Bramblekin have learned to revere. *(Never built — miracles were removed before this system was needed.)*

*   **The Gust:** Unlocked by default. Your "awakening" breath that saves the initial colony.
*   **The Pebble-Drop:** Unlocks at Population 10 when the Builders construct the Stone Altar (an arrangement of small gravel). They recognize the earth, giving you power over it.
*   **The Dewdrop:** Unlocks after surviving the first summer heatwave. The Builders construct a Rain Catcher (a curled leaf funnel), unlocking water manipulation.
*   **The Sunbeam:** Unlocks in the mid-game when scouts recover the Prism Relic (a shard of broken glass from a bottle). Placing this in the village center grants you thermal/light manipulation.

### Friendly Fire & The "God's Shadow" Mechanic
Yes, miracles can crush your own units. Treating the Bramblekin as immune to physics breaks the immersion of their fragility. However, to prevent frustrating accidental deaths, the AI actively tries to survive you. *(Prototyped, later removed along with the Pebble-Drop.)*

*   **The Telegraph:** When you initiate a Pebble-Drop or Dewdrop, a dark shadow appears on the ground for 1.5 seconds before the object impacts.
*   **Self-Preservation AI:** This shadow acts as an absolute override for Bramblekin AI. If a Bramblekin is inside the shadow, they instantly drop everything and dive-roll out of the radius.
*   **The Consequence:** Friendly fire only happens if you drop a pebble on a Bramblekin who is physically trapped (e.g., stuck in a sap moat, cornered by a wall, or webbed by a spider). This forces you to aim carefully when bailing out trapped units.

### Predators & Prey AI (original framing)
*   **The Wolf Spider (Ground Stalker):** Hunts via a "vibration grid" triggered by Bramblekin carrying heavy resources. Players can counter by dropping pebbles as seismic decoys or flipping the spider with The Gust. *(The vibration-aggro spider itself survived into the current game — see "Combat" above — but the player-counterplay described here did not; there is no player intervention left at all.)*
*   **The Scurry System (Prey AI):** Predators emit a "Fear Aura." Unarmed Bramblekin enter Panic Mode, drop their cargo to increase speed, and run to objects tagged `Cover_Small`. If trapped, they may "Play Dead" to drop off the targeting array. *(The Fear Aura and Fleeing state exist today; "Cover_Small" and "Play Dead" were never built.)*

### Mid-Game: Militarization & Defenses (original framing)
*   **The Militia AI:** Building a Village Bell changes the Fear Aura response to "Alert." Gatherers flee, while Militia units flock into a Phalanx formation to block the threat. *(Never built — no Village Bell, no Phalanx formation logic.)*
*   **The Diplomat (Diplomatic Assimilation):** A unit unlocked by advanced factions — the same role that begs a hoarding neighbor for food in the Resource Greed loop below, put to conquest instead. Rather than a Militia raid grinding an enemy Village Heart down to 0 HP and razing it (Base Razing), a late-game faction can instead send a Diplomat to path to the enemy Village Heart and start a negotiation timer there. Success doesn't destroy the rival tribe: their Village Heart, Granaries, Spore Farms and every living Bramblekin are assimilated wholesale, instantly switching FactionID and color to join the conqueror's empire. *(Never built. Assimilation itself exists today, but only as a Refugee Protocol fallback for a razed faction's survivors — see "Factions, Territory & Diplomacy" above — never as a Diplomat's peaceful alternative to Base Razing.)*
*   **Found-Object Weaponry:**
    *   *Rose-Thorn Pikes:* Planted in the ground to counter leaping spiders. *(Militia carry a Rose-Thorn Pike model today, but purely cosmetic/melee — nothing about "planting" it.)*
    *   *Pollen Grenades:* Create AoE dust clouds that disable enemy insect targeting. *(Never built.)*
    *   *Acorn-Cap Armor:* Grants a health shield and prevents knockback. *(Never built.)*
*   **Autonomous Defenses:** Sap Moats (reduce enemy speed by 80%), Canopy Roofs (block vertical aerial attacks), and Spore-Minefields (explosive knockback traps). *(Never built.)*
*   **God-Commander Synergies:** The player casts The Gust behind charging troops for a speed boost, or spreads pollen grenade clouds over a wider area. *(Never built — moot, since there is no player casting anything any more.)*

### Faction Warfare (original framing)
*   **The Dynamic Frontline:** The garden is divided into hex zones. Expansion requires Militia to hold "No Man's Land" long enough for Builders to erect Boundary Totems. *(Never built — territory today is a simple circular ring per Village Heart, not a hex-zone frontline.)*
*   **The Ironjaw Legion (Ant-Folk):** Highly disciplined. They use pheromone highways for rapid resource stripping. Units include Acid-Spitters and Aphid-Cavalry. Countered by washing away pheromone trails with The Dewdrop. *(Never built. There is no named-faction roster at all — every faction is a procedurally-colored Schism split of the same Bramblekin species.)*
*   **The Gloomkin (Spore-Cult):** They expand by planting Blight-Shrooms that emit toxic miasma. Units include Spore-Druids (parasitic slowing pods) and Pillbug Siege Engines. Countered by burning the Blight-Shrooms with The Sunbeam. *(Never built.)*

### Diplomacy & Greed (original framing — see "Factions, Territory & Diplomacy" above for what actually shipped)
Today, any two Bramblekin tribes (the original Village Heart and every Faction a Schism has since split off from it) are instantly and permanently hostile the moment their territories brush up against each other — the only truce is the temporary Pioneer's Truce a fresh splinter gets with the parent it just left. This is the next layer on top of that: a spectrum between peace and war driven by what each tribe actually needs, not a flat switch.

*   **Default Peace:** *(This part shipped — see above.)* Rival tribes maintain a truce by default instead of instant hostility. Two Factions that have never wronged each other simply coexist — Militia patrol their own territory ring and answer the Wolf Spider together, but leave a peaceful neighbor's Gatherers and Village Heart alone. War has to be provoked, not assumed.
*   **Resource Greed (The Beggar & The Raider):** If one Faction is starving (0 Food Stored) while a neighbor is hoarding (Food Stored capped at MaxFoodCapacity), the starving tribe sends a Diplomat — a single unarmed Bramblekin — to the rich neighbor's Village Heart to beg. If the player doesn't intervene to share resources (e.g. nudging a Gust-load of loose food across the border, or otherwise prompting the hoarder to donate) within a grace window, the Diplomat returns home empty-handed and the starving tribe launches a desperate raid on the rich granaries instead — the same Base Razing playbook already in the game, just motivated by hunger rather than open war. *(Never built — moot now that there's no Gust to nudge food with.)*
*   **Territorial Greed:** If populations grow large enough that two Factions' 20-meter territory rings physically overlap, the intersection becomes a contested warzone — Gatherers from either side risk a fight just foraging there, and Militia from both tribes converge on it rather than waiting for a border violation deeper in their own territory. *(Partially superseded: territory rings do now overlap and matter economically — see Cultural Borders above — but there's no separate "contested warzone" state; the existing Strict Border Control / Thievery rules just apply based on each ring's own current radius.)*
*   **Thievery:** *(This part shipped — see above.)* If a Gatherer sneaks into a rival's territory to steal food and is caught and killed by that faction's Militia, that specific death — not the general existence of two nearby tribes — is what triggers a permanent war between those two Factions. Peace is the default right up until someone gets caught with their hand in the granary.

### The Economy & Crafting Engine (original framing)
The simulation economy will be expanded with new resources, buildings, and entities to support trading and RPG-style crafting systems.

*   **Currency (Amber):** *(This shipped, and Nectar was later added alongside it — see "The Economy" above.)* Amber acts as the primary medium of exchange. It is mined from new sap nodes and is used by factions to trade resources, purchase missing Tech Blueprints, or pay off neighboring tribes.
*   **New PvE Entities:**
    *   *Stag Beetle:* A heavily armored, defensive insect. Upon death, it yields Chitin, which can be crafted into +Max HP Chestplates for Militia. *(Never built as an entity — Chitin exists, but drops from the Wolf Spider, not a Stag Beetle, and unlocks Cooperative Acorn Cracking for Gatherers rather than Militia armor.)*
    *   *Silkworm:* A fast, fleeing insect. It yields Silk, which can be used to upgrade Gatherer speed and carrying capacity. *(Never built. Gatherer speed upgrades exist today, but come from the Nectar Brewery, not Silk/a Silkworm.)*
*   **New Buildings:**
    *   *Trading Post:* Automates supply and demand logistics between factions. Factions can post buy/sell orders here, which are fulfilled by wandering Merchant units. *(Built, but simpler: it unlocks a Village Heart's own Emergency Food Import — Amber-for-Food on demand — not cross-faction buy/sell orders or Merchant units.)*
    *   *Armory:* Processes rare materials (Chitin, Silk, Spider Fangs) into permanent unit upgrades via the Tech Tree. *(Never built as a building — Fang Pike/Chitin Mallet upgrades exist, but unlock automatically at a banked-item threshold, with no Armory structure or player-facing Tech Tree.)*
    *   *Aphid Pen:* Used in Advanced Agriculture. Gatherers can capture wandering Aphids alive and pen them here for a passive, continuous drip of high-value Nectar food. *(Never built. Nectar exists today, but is brewed from Food+Amber at a Brewery, not farmed from live Aphids.)*

### The Economy of War (original framing — never built)
*   **The Caloric Tax:** Militia units burn calories at 2x-3x the normal rate and require high-tier rations (aphid meat, nut stores). Famine causes the army to desert and revert to Gatherer AI.
*   **The Labor Vacuum:** Conscription instantly removes workers from the economy. Maintaining weapons cannibalizes civilian repair resources (wood, silk, sap).
*   **The Morale Engine:** *(Morale itself shipped — see War Weariness in the Roadmap — but the specifics below did not.)* Prolonged mobilization builds "War Weariness," slowing all village activity. Casualties cause grieving states. Winning battles grants a "Triumphant" economic buff. Extreme low morale triggers civilian strikes, which the player must fix via divine intervention.

### Win and Loss Conditions (original framing — never built)
The game operates on a seasonal timer, pushing the colony toward a definitive endgame rather than an endless sandbox. *(No seasonal timer exists. Extinction — see below — is the only implemented loss state, and it's recoverable via Genesis; the closest thing to a win condition today is the Great Monument, which marks a civilization's success without ending the simulation.)*

*   **The Loss Condition (Extinction):** *(This part shipped, and is recoverable — see Genesis above, which this original doc didn't anticipate.)* The game ends if the Bramblekin population drops to zero, or if the Village Heart (the original terra-cotta pot or seedling they built around) is destroyed by a rival faction.
*   **The Win Condition (The Great Migration):** The ultimate realization is that the backyard is too hostile to sustain a massive, permanent civilization. The overarching goal is to build The Ark before Winter arrives. *(Never built. The Great Monument is the closest implemented equivalent — an endgame civilization goal that a wealthy tribe commits its whole economy to — but it doesn't end the game or require evacuating the map.)*
    *   *The Objective:* Gather exorbitant amounts of rare, guarded resources (like silk, specific light-weight bark, and dandelion parachutes) to construct a massive wind-ship.
    *   *The Climax:* Launching the Ark triggers an endless wave of predators and rival factions desperate to steal the vessel. You must expend all your Faith defending the launch platform until the wind catches the Ark, carrying the Bramblekin over the fence to the "Promised Land" (winning the game).

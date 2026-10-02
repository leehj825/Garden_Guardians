# Garden_Guardians_Design.md

*Last updated: 2026-10-02 — society levels (villages, paid jobs, kingdoms, invasions) and playing a Bramblekin added; the **Emergent Survival** pivot, now with
**settling and society**, and a living population: **seasons**, **births**
and **villages** that bud off daughter groups, **named** Bramblekin who
pair up as **couples** and die of **old age**, a **follow camera**,
**farming**, villages that live as **neighbours** — allies or enemies —
and **Infamy**, a reputation that travels beyond who you've personally
met. The game is no longer a
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
**The Hook:** A 100m×100m hilly backyard (a 3D model with two ponds and a hollow oak) is home to a
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
groups. The player is mostly a spectator with a camera, who can tap any single
Bramblekin to see what makes it tick, or take the wheel of one
(see *Playing a Bramblekin*).

## The Core Loop (as implemented)
Hunger and thirst rise → each Bramblekin walks to the water to drink, and forages loose Food within its own
Intelligence-scaled senses, or eats from its home's store → threats (the
Wolf Spider, Hornets, raiders, hostile Bramblekin) are fought or fled on a
per-individual Aggression roll — or, if wildlife, hidden from at home →
fed and safe, a group member does the job its Leader gave it, and anyone
else builds, stocks and rests in its home → the rest of the time
Bramblekin wander and meet each other → every meeting is resolved from both sides' situation
and traits: a friendship, a new or bigger group, a shared meal, a
struggling loner taken in, or a robbery and a lifelong enmity → Leaders
decide group goals every few seconds; followers' loyalty rises and falls
with how they're treated, and the disloyal leave, split off or stage a
coup → singles pair up as couples, and thriving groups raise their young
and grow into villages, while the seasons swing the food supply from
plenty to scarcity and back → clever clans work out farming and plant
berry bushes; neighbouring villages ally, send each other food, or go to
war and raid each other's stores → the old grow grey and die, and their
children carry the family name on → new solitary wanderers drift in
from the map's edge whenever the population runs low.

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
*   **Two-finger pinch** (or the mouse wheel, on desktop) zooms in/out,
    from 8m to just far enough that the whole garden fits on screen from
    any angle (the circle through its corners, worked out from the
    screen's shape — about 185m on a 16:9 screen, never less than the
    starting overview). There's no draw distance: zoomed out, everything
    in the garden shows.
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

*   **The Director ("Auto" button):** an automatic camera for watching
    hands-free, above all at 20x or 50x. Once a second it looks over
    everything worth watching (`World.DirectorShots`): headlines and
    births put in its spotlight (fading over 30s), leadership duels,
    fights between kin, raids, the Wolf Spider hunting or pouncing,
    beetle hunts — and, when nothing's
    happening, the liveliest village. It flies to the best, keeps the
    camera on its subject for 3–8s (cutting in early for anything much
    bigger), avoids repeating what it showed in the last 40s, and names
    what it's showing in a strip under the top buttons. A pan, the Map
    button, a banner tap or picking a Bramblekin hands the camera back.

## Playing a Bramblekin
*   **Taking the wheel:** with a Bramblekin selected, the **Control**
    button (left of **Follow**, under the Kin Inspector) hands it to the
    player: its own mind stops (no job, no fleeing, no sleeping, no
    errands) and the game runs at 1x. It still gets hungry and thirsty,
    sick, and hurt, and it can die — then control ends. **Exit** (or Esc)
    gives it back to its own mind and flies the camera up over it again.
*   **Standing alone:** while controlled the kin belongs to no clan, village
    or kingdom: it takes no village job, is not rationed, counts for nothing
    in its clan's defence, and no group can recruit or court it. A clan of
    two is not dissolved for want of it. It eats only from its own home's
    store (whatever the clan's sharing rule). The **Job** button (above
    **Kin**) steps through **Normal, Hunter, Guard**: Guard gives the
    soldier's blow and toughness and the guard kit, Hunter the hunter's kit;
    it has no shield (shields come from its clan's crafts). **Exit** (or Esc) gives it back: it rejoins its old clan if that still
    stands (with its old loyalty and job) and otherwise stays solitary. A garden saved
    while someone is controlled saves it as still in its clan.
*   **The view:** a third-person camera 4.8m behind it, over its shoulder,
    like an action game, kept above the ground.
*   **Controls (touch):** a stick on the lower left walks it, relative to
    where the camera looks (it jogs 35% faster than it walks, slides along
    obstacles, the water's edge and the map's edge); a drag anywhere else
    turns the camera (sideways) and tilts it (up and down); **Attack** on
    the right swings. A **Kin: off/on** switch beside it lets the swing hit
    Bramblekin of other clans (always off to begin with); wildlife, ants
    and the like are always fair game. Desktop: W A S D or the arrows to
    move, Q/E or the mouse to turn, Space to attack.
*   **The swing:** it strikes the nearest thing in front within about 1.4m
    of its edge, with the blow it would deal in a hunt (a Bramblekin takes
    its fighting blow), every 0.7s, and plays the combat animation. A
    hill guard takes 1 from anyone not in an assault.
*   **Eating and drinking:** stand still by loose food, at your store (its
    own home or its clan's) or at the water's edge while hungry or thirsty
    and it eats or drinks on its own; the HUD says so.
*   **Close-up ground:** while a kin is controlled, the ground within about
    40 m of it is dressed in real geometry (`World.GroundCover.cs`): blades of
    grass (about 9 tufts a square metre of bent three-triangle blades within 12 m,
    3 of single-triangle blades to 24 m, 1.2 coarser ones beyond), flowers (a stem and
    five petals) and small stones, with colours on the vertices (lush to dry in slow
    patches) and the season's tint. It is built in 6 m chunks as the kin walks (three
    a frame), never over water, homes, plantings, wells, the oak or worn paths, and
    dropped when control ends. Nothing is simulated or saved.
*   **Showing it:** the HUD shows its name, Health, Hunger and Thirst and
    what it needs; the garden carries on around it. The control itself is not saved: a loaded
    garden starts with nobody controlled.

## Player Interaction (what's left of it)
*   **Kin inspection & the follow camera:** tapping a Bramblekin selects
    it: the camera swoops in (to 18m) and follows it as it goes about its
    life, its name floating above it (and its partner's, fainter). Pinch,
    twist and tilt still work while following; a one-finger pan takes the
    camera back and stops following, and the **Follow** button under the
    Kin Inspector starts or stops it again. The **Map** button beside the
    speed controls flies back out to the whole garden. The top-right Kin
    Inspector shows its name, sex and role (Solitary/Leader/Follower,
    young or elder), its age and generation, its mother and father, its
    partner and how many children it has, State, Health, Hunger, Thirst (and how far its home is from water), its
    nature and all seven Personality traits (and the detection radius its
    Intelligence buys it), its group, its home
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
    (tents, houses, sites, food stored, villages and buddings, crops),
    groups (how many farm) and current alliances and wars, what the
    colony is doing (foraging, eating, drinking, fleeing, fighting,
    robbing), deaths by cause (thirst among them), births and the highest generation,
    and the politics so far (walk-outs, splits, coups, exiles, raids).
    The year line names the weather (drought, harsh winter, bountiful,
    storm), and the Kin Inspector shows its clan's tradition and
    neighbours, and any errand it's on.
*   **Clans on the map:** every village's name and head count floats over
    its main home, and its ground is washed faintly in the clan's colour
    with a stronger rim (reaching 4m past its outermost home). Tapping a
    home — rather than a Bramblekin standing at its door — picks its clan:
    a clan card takes the Kin Inspector's place (tradition, members,
    Leader, founding, homes, stores, crops, neighbours), and the History
    screen then shows that clan. A loner's tent picks its owner. A clan
    with nothing built yet — a new one, budded off or split away, whose
    first tent is still a site — says so ("Homes: none yet - building its
    first tent (1/3 twigs)", or "setting out for new ground"), and a
    site shows on the map as a patch of bare earth with a stake flying
    the clan's colour, the twigs laid so far standing on it.
*   **Speed:** 1x, 2x, 5x, 10x, 20x and 50x. However fast, the garden
    always advances in the same fixed 1/60s steps as the headless tuning
    runs — bigger steps played a different game (about half as much
    starvation again at a phone's 0.046s steps) — so fast-forward is more
    steps per frame, never bigger ones. A device that can't keep up runs
    as fast as it can, and the HUD says so ("Speed 20x (running 9x)").
    The event console on the left narrates the seasons, alliances,
    settlements, villages, births, hunts, leader decisions, rebellions
    and deaths.
*   **The log, as much as you like:** the **Log** button under the event
    console steps it through *brief* (the newest 3 entries, a line each —
    the default), *off* (just the button, counting what you've missed:
    "Log: off (+4)") and *full* (the last 15 entries, word-wrapped, as
    tall as the screen allows). The choice is remembered
    (`settings.txt`, beside the save) — and kept through a new garden.
*   **Health, hunger and thirst bars** (green, orange, blue) float over
    any Bramblekin that's hurt, hungry or thirsty (and a wounded Spider). They grow with the zoom — about 2.4×
    as wide as the creature looks on screen — so zoomed in close they're
    easy to read, while at the whole-map view they shrink back to the
    same small 34px bars (the floor, and 150px the ceiling, both scaled
    up on bigger screens).

## Code Layout & Performance
*   **Layout:** `Program.cs` is only the entry point. Everything else is
    under `Source/`, one type per file: `Engine/` (touch and follow
    cameras, terrain, tap input, spatial grid, movement, the water map),
    `World/` (the `World` partial class split by concern — core, society,
    families, neighbours, errands, war outcomes, farming, wild food,
    materials, group homes & villages, leadership, rebellion, culture,
    seasons, weather, births, chronicle, lives, save, shelters, hunting,
    interactions, spawning, stats, rendering —
    plus Food, Crops, Twigs, Materials, Shelters and Garden Props), `Kin/`
    (the `Bramblekin` partial class with one file per need — Hunger,
    Safety, Duty, Settle, Social — plus Hunting, Farming, Fishing,
    Materials, Loyalty, Lineage, Family, Aging,
    Errands, Memory, Fame, Save, Actions and Drawing, and the Personality, names,
    sex, relationship, group, errand, place-memory, clan-culture and
    society types), `Wildlife/`, `Save/` (the save file's data types and
    the JSON save system) and `Game/` (the main loop, HUD, History screen
    with its Stats and Heroes tabs, banners, and saving).
*   **Per-step budget:** the simulation runs 60 steps per simulated
    second, so at 20x a phone needs 1,200 a second. Spatial queries ask
    only for the radius they need (an encounter check used to scan a
    30×30m window for a 1.2m radius), the grid is a flat array, and the
    hot paths — movement, a group's homes, store searches — allocate
    nothing: together about a quarter less CPU and a tenth of the
    garbage, with results identical to the step.
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
*   **Raylib culling** skips anything off-screen; there's no draw
    distance. The lawn's 2,500 cells are worked out once (corners and
    grass colour) and only tinted for the season each frame.
*   **Obstacles by cell:** the rocks, the oak's trunk and the circles
    along its roots (some 150 in all) are bucketed into 5m cells, each
    listing every obstacle within 3m of it, so a walker only ever checks
    the handful near it.
*   **Walking round the water** (`WaterMap`): a 1m grid of the garden
    marks the pond (at each of its drought levels) and the oak with room to spare; a walker whose straight way is cut
    by it runs A* (eight ways, no cutting corners) and pulls the path
    tight into a few waypoints — worked out again only when its target
    moves a meter. A fine 0.25m grid answers "is this spot wet?" for every
    step. Over a 13-year run that's some 30,000 routes, well under a
    second in all.
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
*   **Personality (DNA):** every newcomer rolls seven traits, each
    uniformly random in 0..1, the moment it arrives; one born here
    inherits the average of its parents' (see Growth below), and its
    clan's traditions pull it part of the way toward what the clan
    prizes (see Clan Culture). Fixed for life. The four newer traits are
    all centred on 0.5 — a middling Bramblekin behaves just as before —
    and each has something to see it do:
    *   **Aggression** — the odds of turning on another Bramblekin (to
        rob it when starving, in a feud, in a war) and — with Courage —
        of fighting rather than fleeing; how hard it hits (5–11 per
        strike); and, as a Leader, its appetite for war and whether it
        eats first. Its body is tinted redder the more Aggressive it is.
    *   **Sociability** (extrovert ↔ introvert) — the urge to meet
        strangers (vs. walking away from anyone who crowds it, below
        0.35), the odds a meeting ends in friendship, whether two meeting
        Bramblekin band together (both ≥ 0.6), how readily a struggling
        loner asks to join a group, how loyal it naturally is, and — as a
        Leader — whether it shares the store fairly and takes in
        newcomers.
    *   **Intelligence** — detection radius from 5m to 20m, how soon a
        newcomer settles (10–50s), its claim to lead, planning ahead, and
        working out crafts.
    *   **Rebelliousness** (rebellious ↔ obedient) — a rebellious
        follower's loyalty drains up to 1.4× as fast and recovers at
        0.6× the pace (an obedient one's, the other way round); it stops
        obeying sooner (the obedience line moves ±0.075); it's up to 1.6×
        as likely to rebel once disloyal, readier to challenge the Leader,
        and less willing to sit out the winter before walking out.
    *   **Persuasiveness** (persuasive ↔ passive) — adds up to ±0.3 to its
        claim to lead; as a Leader it keeps its followers' loyalty up (or
        lets it slide), talks neighbours into alliances, peace and
        learning a craft (odds 0.6–1.4×); a persuasive member talks
        struggling loners into joining; a persuasive rebel draws the
        wavering after it into a split (members up to 0.125 more loyal
        follow), or can lead one without being sociable.
    *   **Courage** (brave ↔ cautious) — standing up to predators and big
        game is 70% nerve and 30% temper (against another Bramblekin, the
        reverse); the brave hold their nerve down to 0.16 of their Health,
        the cautious break at 0.56 — but however brave, a fighter breaks
        off while it can still survive one more blow from its foe (a
        spider's bite is 10), and backs away on guard, safe from the
        spider's pounce, for 1–3s (longer the braver); a place of danger is shunned for 150s
        by the bravest, 450s by the most cautious; Leaders make the brave
        their Hunters and Guards, and the brave (with the fierce) drive
        off enemies near home in a war. Dangerous orders cost the brave
        less loyalty.
    *   **Diligence** (diligent ↔ idle) — at work (gathering, building,
        stocking, farming, carrying for its group) it goes at 0.85–1.15×
        pace, and it dawdles and rests between things 0.6–1.4× as long.
*   **In words:** the Kin Inspector sums a Bramblekin up ("Nature:
    brave, persuasive, rebellious" — every trait 0.2 or more from the
    middle) above all seven values; the chronicle calls a rebel or a
    usurper by its most striking trait ("The rebellious Pip Thornwood led
    four unhappy members out…"). The headless lineage report tracks the
    living's average of every trait, so the garden's evolution shows.
*   **Skills:** besides its fixed nature, every Bramblekin gets better at
    what it does. Hunting (each blow struck at a creature, twice for big
    game), farming (planting, and a little for each picking), building
    (every twig, stone and branch delivered), fishing (every cast and
    catch) and healing (every patient tended) each run 0..1: every act of
    practice closes a fortieth of the gap to perfect (faster for a sharp
    mind), and skills rust very slowly without use. Skill pays: a skilled
    hunter hits creatures up to half as hard again; a skilled builder,
    farmer, fisher or healer works up to 30% faster; a fisher lands up to
    half as many again; and a skilled farmer now and then coaxes a second
    piece out of a crop, straight into the store. Leaders hand jobs to
    whoever is best at them, so veterans keep their trades. A newborn
    starts with a quarter of its handier parent's skill in each — a family
    trade. Reaching 0.75 makes it a **master**, and the chronicle says so.
    The Kin Inspector lists its skills and names its trade ("skilled
    farmer").
*   **Hunger:** rises 1 point per second from 0 to 100. At 60 a
    Bramblekin is *hungry*; at 80 *starving*; at 100 it loses 1 HP a
    second until it eats or dies. One piece of Food removes 40 Hunger and
    restores 6 HP. Resting at home heals too (1 HP per 1.5s; faster in a
    House).
*   **Thirst:** rises 0.4 points per second (a quarter faster in summer,
    a quarter slower in winter, faster when sick) from 0 to 100 — a drink
    lasts about two and a half minutes. At 60 a Bramblekin is *thirsty*; at
    100 it loses 1 HP every 2s until it drinks or dies. It drinks from
    whichever is nearest: the pond (the nearest stretch of shore, however
    far — it slakes its thirst entirely), a well it may use (its clan's,
    an ally's, or an abandoned one — drawing water up takes 3.5s and slakes
    it entirely too), or its home's cistern (a sip takes off 70). Out in a
    storm it drinks the rain (2 points a second). Juicy food helps a
    little: a sprig of cress takes off 15, a fish 5, a berry or a
    mushroom 3; seed, meat and acorns nothing. So **living far from water
    costs**: every drink is a walk there and back (from the garden's dry
    south-west corner, some 70m each way — most of a minute and a half),
    time not spent foraging, working or resting — and a drinker crouched
    at the water's edge is busy enough for the Wolf Spider to feel.
*   **The strict needs hierarchy** — every frame, each Bramblekin serves
    exactly one need, in this order:
    0.  **A leadership duel,** once started, is settled first.
    1.  **Critical (Thirst or Hunger, whichever is worse):** thirsty and
        no hungrier than it is thirsty, it goes for a drink — its home's
        cistern if that has water and is nearer, else the nearest shore —
        and a drink under way is finished unless it's starving and
        hungrier still. Otherwise, hungry, it eats what it's carrying; else keep robbing
        the neighbour it committed to; else, with a predator about, go
        home to eat from the store; else go for the nearest loose Food it
        can see or eat from its home's store (or a village home's, if the
        group's sharing rule allows), whichever is closer — though once
        starving it takes the sure meal at home over any loose Food more
        than 3m away; else scavenge an abandoned store; else hunt a
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
        once at or below 40% Health. Fleeing wildlife, it runs home if
        it's close and hides inside; walls don't stop another Bramblekin,
        so from one of those it runs straight away, even out of its home.
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
    Drawn as an acorn cap propped on three twig legs.
*   **House:** a group's upgrade of a Tent, once the group outgrows its
    housing — 6 more twigs. Room for 6, a store of 12, and healing half
    again as fast. Drawn as a whole hollowed acorn under its scaly cap,
    with a round door and a warm round window, flying its group's colour
    from the cap's stalk. A granary is a hazelnut beside it; a palisade a
    ring of rose thorns curving outward, with a gap at the door. (An
    acorn village: the Bramblekin are tiny, and live like it.)
*   **Burrow:** a loner settling on sloping ground above any flood's reach
    may dig in instead of pitching a Tent — odds 0.8 × (1 − its
    Sociability), so mostly introverts. It needs only 2 twigs to shore up
    its doorway, houses 2 and stores 4, like a Tent; its earthen walls
    keep folk warmer through the winter (hunger at 0.4 of the usual rate
    wintering in, against 0.5), and a lived-in burrow's store is tucked
    away where raiders and ants won't find it. But it can't be built up:
    a clan that outgrows one puts up a Tent beside it, and a clan choosing
    a home prefers a Tent or a House. Drawn as a turfed mound of earth
    with a dark round doorway shored with twigs.
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
*   **Small game** — Grubs (see Food & Wildlife) — is prey for anyone: a hungry Bramblekin that can't see
    Food hunts it; a settler with a low store hunts it near home; a
    Hunter goes after it when there's no Stag Beetle to hunt.
*   **Defending home:** residents who see a raider heading for their home
    (or any of their village's homes, or an ally's) treat it as a threat
    they're keen to fight; a Leader rallies its group to Defend against
    anything that comes within 10m of home — though not against an
    outsider who's only defending its own home next door, or against
    defenders fighting off its own raiding party.
*   **Raids:** a starving, highly Aggressive Bramblekin raids someone
    else's store, making Enemies of everyone who lives there — never a
    home where its parent, child or sibling lives.
*   **Blood is thicker than water:** a Bramblekin never robs a close
    relative (parent, child or sibling), and never deals one the blow
    that would kill it.

## Encounters, Relationships & Groups
*   **Relationships:** each Bramblekin keeps `KnownKins` — every other
    Bramblekin it has met, by ID, as **Friend**, **Neutral** or
    **Enemy**. Enemy is permanent. Dead Bramblekin are forgotten.
*   **Infamy — reputation beyond who you've met:** a starving robbery, a
    killing blow against another Bramblekin, or carrying off a piece of an
    enemy's store on a war raid earns Infamy, which fades slowly on its
    own (like a grievance). At 1.2 or more a Bramblekin is **notorious**:
    word has travelled, so even a stranger who has never crossed its path
    is warier of it (odds 0.7 of shying away) when the two might band
    together, when a struggling loner asks to join a settled group, or
    when two strangers might warm into a first friendship. Nothing about
    it stops two strangers banding together to survive a predator that's
    after them both right now — that's survival, not trust. Unlike
    Reputation (standing earned *within* a Bramblekin's own group, feeding
    its claim to lead), Infamy is what the wider garden has heard about
    it. The Kin Inspector shows it once any has been earned.
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
*   **Heirs:** a Leader who is an elder, sick or below half Health names
    an **heir** at its next decision, if it has none — the member with the
    best claim to lead, leaning toward its own close kin (+0.3) and
    friends (+0.15), so dynasties form. When the Leader dies or leaves, a
    named heir still in the group takes over (with +0.5 Reputation), and
    the chronicle records the succession. A Leader lost with no heir
    leaves a scramble: the group elects as before, but every follower's
    loyalty drops by 0.08. A usurper names its own heir. The Kin Inspector
    marks the heir.
*   **The council:** up to three of the group's most respected members
    (Reputation 0.5 or more) sit on the Leader's council. At each
    decision the Leader's Aggression, Sociability, Intelligence and
    Courage are drawn part of the way toward the council's average — 0.6
    × (0.5 + the council's persuasiveness − the Leader's) × how full the
    council is, at most half the gap — and that tempered nature scores
    the goals, sets the sharing rule, and weighs war and peace. A council
    can talk a Leader out of eating first, or hold a hot head back from
    war. In headless runs it sways decisions about 16% of the way. The
    Kin Inspector marks councillors.
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
    (Hunter on a hunt, Guard on a defence, Raider on a raid — felt less by
    the Aggressive) and injury. Members of a badly run group share most of
    those grievances, so they tend to sour together. But in a **shared
    hardship** — winter, the stores empty and shared fairly — hunger is
    nobody's fault, and costs the Leader only 40% of the usual loyalty.
*   **New members** get 90s to settle in before they can rebel or be
    thrown out.
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
    *   **Plot, then splinter** — Sociability ≥ 0.4 (or persuasive): it
        hatches a **plot** rather than leaving on the spot (a rebel who
        rebels while a plot is afoot joins it). At each decision the plot
        quietly recruits the unhappiest member not yet in on it (odds 0.4 +
        0.5 × the plotter's Persuasiveness), and plotters keep their heads
        down — they neither rebel nor get thrown out in the usual way. But
        the Leader may get wind of it: odds 0.05 × (0.5 + its Intelligence)
        × (1 + conspirators) per decision, so a big plot is hard to hide.
        Uncovered, an Aggressive Leader exiles the plotter (and cows the
        rest); a milder one talks them round. A plot that goes three
        decisions unnoticed with at least one conspirator leads them out as
        a new, homeless group with a Leader of their own (never into the
        snow); one nobody will join is given up, and its plotter walks out
        alone. An instigator whose loyalty recovers gives it up. The Kin
        Inspector marks plotters.
    *   **Leave** — otherwise: it walks out and goes it alone as an
        **Independent**, losing the group's home, store and defenders.
    *   In winter a rebel who'd leave or split off waits for spring
        instead, with odds of its Intelligence — walking out into the snow
        is how loners starve.
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
*   **Names:** every newcomer has a given name ("Pipwick", "Nella") and
    founds a garden-flavoured family ("Thornwood", "Mossbrook"); a child
    takes one of its parents' family names at even odds, so families
    spread or die out with their descendants. A group is named for the
    family of the Leader it was founded under — "the Thornwood clan"
    (then "the Thornwood clan II" while the first is still around). The
    event log uses names throughout.
*   **Sex:** every Bramblekin is female or male at even odds, newcomers
    and newborns alike. For now the only difference is that a couple is
    one of each; the sexes forage, fight, build, lead and rebel alike.
*   **Couples:** when two single, grown (not elder), unrelated (not
    parent, child or sibling) Bramblekin of opposite sex who aren't
    Enemies meet, they pair up with odds 0.2 + 0.5 × their average
    Sociability. Groupmates simply become a couple (and move into the same
    home if there's room); two loners set up together as a new group; a
    loner joins its new partner's group if there's room. Members of two
    different groups don't pair — neither would leave its own. Couples
    live together: a newcomer to the group moves in with its partner if
    there's room, and it's singles who move out of an overcrowded home.
    If one leaves the group (walking out, being exiled, or splitting
    off), its partner goes too if its own Loyalty is below 0.6 — the two
    set up as a household of their own — otherwise the couple separates.
    When a village buds, a settler's partner goes with it. A death leaves
    its partner widowed, and it mourns 90s before it looks for another.
*   **Courtship gifts:** when two singles who could pair up meet and one
    is fed with food in hand while the other is empty-handed, the suitor
    offers it. The gift is given whatever comes of it — they part as
    Friends at least — and adds 0.25 × (0.5 + the giver's Persuasiveness)
    to the odds of pairing up.
*   **Births:** a thriving group raises young — checked at each Leader
    decision. It takes a couple in the group, both fit to be parents
    (grown but not elders, fed, at least 70% Health), living in one of
    the group's finished homes — a Tent will do; the crowding then makes
    the group upgrade it. The group needs at least 4 Food stored, and 1
    per member across its homes (2 in Autumn, enough for the winter; no
    births at all in Winter), no more than a third of the group hungry,
    room to grow (see villages) and 60s since its last birth. A birth
    costs 3 Food from the stores, the nursery's first.
*   **Old age:** a year is 600s. Newcomers arrive grown, 0.3–1.5 years
    old; each Bramblekin's natural lifespan is rolled between 4 and 6.5
    years. From 3.5 years it's an **elder**: drawn grey, 20% slower,
    hitting 30% less hard, past raising young and no longer courting —
    but looked up to (+0.3 to its claim to lead, so elders often lead).
    At the end of its lifespan it dies of old age ("…died of old age at
    4.8 years, leaving 5 children"). Old age is now a common death: about
    a third of all deaths over long runs.
*   **The young:** a newborn inherits the average of its parents'
    Personalities, ±0.15 on each trait; its Generation is one more than
    its older parent's. For 90s it's young: drawn small and growing, it
    stays close to home, may always eat from the store, and takes no job,
    no fights, no hunts, no raids and no part in politics (it can't be
    elected while there's an adult to lead). Over many generations the
    traits of the living drift away from a newcomer's 0.5 average — in
    whichever direction the garden rewards.
*   **Adoption:** a single child under a year old whose parents have both
    died is taken in at the next Leader decision by a couple in its clan —
    a grown brother or sister first, else the most sociable pair — who
    count it among their children and take it into their home. From then
    on they're close kin: never robbed, courted or struck down by one
    another. A young one left with no clan at all is taken in by a
    settled clan it meets (and adopted there). The Kin Inspector shows who
    raised it.
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
    the Leader, with their partners and young — set out as a daughter
    group of their own, electing a Leader of their own. No quarrel: the
    two start out allied. The House stays with the old village, which can
    grow into it again.
*   **Spreading out:** the settlers take a share of the old village's
    stores as a dowry (a third, up to 6) and head for **open ground**:
    the spot, 15–55m away, that's furthest from every other home (at
    least 25m if it can be found), with a bonus for Berry Patches nearby.
    There they mark out a site and build from scratch; the dowry goes
    into their store once it's finished. A splinter group and a couple
    who walk out together look for open ground the same way, well away
    from the group they left. Before this, daughter villages budded off
    right next door and neighbours fought at close quarters; spreading
    out cut kin-on-kin killings by about 70% and made wars rare.
*   **The population** is no longer held at a fixed number: it rises
    through Summer and Autumn and thins in Winter and early Spring, and
    over hours settles wherever the garden's food allows — with farming,
    about 35–70 in 4-hour headless runs, with births (250–360 per run)
    far ahead of newcomers (10–25). Wandering Arrivals only top it up when
    it falls below 30; a safety cap of 150 is never reached in practice.

## Farming
*   **Working it out:** at each Leader decision, a group with a House
    that doesn't yet farm works it out with odds 0.008 × the Intelligence
    of its cleverest grown member — a bright clan within a year or two, a
    dull one much later. Farming is something **individuals** know: the
    whole group learns it, children of a farming parent are born knowing
    it, a group teaches everyone who joins, and kin carry it with them
    when they split off, bud off or marry into another group. An ally
    may also teach it (see Neighbours).
*   **Crops:** a farming group keeps up to 2 crops per House and 1 per
    Tent (at least 1.4m apart, clear of shelters; 60 on the map at most).
    Planting costs a piece of food from the stores as seed — except grain
    (below) — and never happens in winter. Each kind needs its own craft and bears in its own
    seasons (pace relative to normal, spring/summer/autumn/winter):

    | Crop | Craft | Where | Grows, then one every | Holds | Pace | Lasts |
    |---|---|---|---|---|---|---|
    | Berry bush | Farming | 2.5–6m from home | 90s, 40s | 4 | 1.0/1.3/0.8/0.3 | 3 years |
    | Grain patch | Grain | 2.5–6m from home | 60s, 26s | 6 | 0.5/1.3/1.3/0 | 1 year |
    | Mushroom bed | Mushrooms | against a House wall | 70s, 45s | 3 | 0.9/0.5/1.4/0.4 | 2 years |
    | Cress bed | Cress | on the shore, within 14m | 45s, 32s | 3 | 1.3/1.0/0.8/0.3 | 2 years |

    The weather scales them too (a drought halves them, a bountiful
    season adds half). A crop within 8m of the pond is **watered**: a
    quarter faster, and a drought doesn't touch it (cress beds always
    are), and so is one within 6m of a dug well. A Farmer plants whichever kind the clan knows and has fewest
    of, so its fields spread across the year: grain for late summer,
    mushrooms for autumn (and a little in winter), cress in spring. A worn-out crop is simply
    gone, and replanted. Overripe fruit drops for anyone. Each is drawn in
    its own way — a leafy bush dotted with berries, a tuft of stalks
    nodding under golden seed heads, a mound of dark soil sprouting
    russet caps, a mat of round green leaves — with a stake in its
    clan's colour.
*   **Seed corn:** a grain patch is sown from the clan's **seed corn**
    (2 a patch), not from its stores. A clan that learns grain starts with
    4 from the wild grass; after that, grain and wild seed brought home go
    into the seed corn first while it's short of what the clan keeps (2 a
    patch for its share of crops, plus a patch spare — the clan card
    shows "Seed corn: 3 (keeps 8)"), and into the store after. Grain
    patches last a year, so each spring's sowing is last year's harvest:
    a drought year leaves little seed and few patches the next. A starving
    Bramblekin with nothing else to hand eats the seed corn — the log
    notes when a clan eats the last of it. A budding village takes half
    the seed corn with it.
*   **Farmers:** a farming group makes one Farmer per 4 grown members (at
    least one), from its most Intelligent Gatherers, under any goal but
    Defend. A Farmer picks what's ripe into the stores, and plants while
    the group has room for more crops and food to spare; otherwise it
    gathers. Gatherers pick ripe crops too, and a hungry member eats
    straight off one.
*   **Fishing:** a Gatherer (or Farmer) that knows fishing, with nothing
    ripe and no food in sight, walks to a stretch of shore within 20m of
    home and casts — a rod held out over the water, its line dropping to
    a red float — every 8s (quicker for the diligent), landing a minnow
    or tadpole at 0.55/0.45/0.5/0.15 odds by season, which it carries to
    the stores. Winter's poor catch still beats an empty lawn.
*   **Wild crops:** when its group is gone, a crop runs wild — still
    bearing (and dropping food for anyone) — and withers after 600s.
    Crops go with the house nearest them when a village buds or two
    groups merge.

## Crafts
*   **Know-how clans work out**, the way they work out farming: at each
    Leader decision a clan ready for a craft discovers it with odds
    0.006 × its cleverest member's Intelligence (one at a time). Crafts
    live in individuals like farming does: taught to everyone in the clan,
    inherited by children (both parents' crafts), carried along by anyone
    who leaves, and taught to allies (odds 0.05 per decision, farming
    first, then granary, spears, palisade, grain, mushrooms, cress,
    fishing, stonework, cisterns, wells, slings, hearth, snares, herb-lore, herding, shields). When a clan is ready for several, the one it
    works out is picked at random.
    *   **Granary** (needs farming and a House): each House gets a round
        granary beside it and holds half as much again in store.
    *   **Spears** (needs a hunting tradition or a Wolf Spider brought
        down): half as much again of a blow against the Stag Beetle, the
        Wolf Spider, Grubs, Hornets and ants — never against kin.
    *   **Palisade** (needs a House and a martial tradition, or three
        remembered dangers): a ring of thorny stakes round each home, once
        its Builders have dragged in 3 branches (the ring goes up branch
        by branch). The Wolf Spider won't hunt anyone inside it, ants
        can't get at its store, and a raider must spend 4s breaking in
        first — time for the defenders to come.
    *   **Grain, Mushrooms, Cress** (need farming; mushrooms a House,
        cress a home within 20m of the shore): new crops (see Farming).
        A clan that learns grain starts with 4 seed corn from the wild.
    *   **Fishing** (needs a home within 20m of the shore): see Farming.
    *   **Cisterns** (needs a House more than 25m from water — necessity
        is the mother of invention): an acorn-cup cistern out front of
        each House, holding 6 sips. It fills in the rain (a sip every 2s
        of a storm), and whoever drinks at the pond carries a cupful home
        (a sip) while it isn't full. A thirsty Bramblekin drinks from it
        if it's nearer than the pond — so a far-off village makes one
        long trip do for two drinks.
    *   **Wells** (needs stonework and a main home more than 20m from
        water, walking): the clan marks out a well 1–5m from its main
        home, on the lowest ground to hand, and its Builders carry stones
        to line the shaft — 3, plus one for every 2m the ground there
        stands above the pond (the water lies deeper under a hill), so a
        hilltop well takes some 8. Stones go to a well before any footing.
        Once dug it's water at the door, all year and through any drought;
        crops within 6m of it count as watered. A clan of just two keeps
        a Builder on it too. While it's being dug it's a dark shaft with a
        heap of earth beside it and only as many stones round it as have
        been carried in (the clan card counts them: "digging a well (3/7
        stones)"); dug, the ring stands three courses high round dark
        water, under a wooden frame with a rope and an acorn-cup bucket, a
        pennant in its clan's colour. A well
        outlives its clan — anyone may drink from it then — but a
        half-dug one nobody's digging any more is filled in.
    *   **Stonework** (needs a House): each House is raised on a stone
        footing, once its Builders have carried in 4 stones (a ring of
        grey stones round its foot): its store holds 2 more, stays dry in
        a flood, and ants can't dig into it.
    *   **Slings** (needs spears): slings of twisted grass that loose
        pebbles at anything too quick to catch or too dangerous to close
        with — a Hornet. A slinger in range (3.5m) stands its ground and
        looses a pebble every 1.4s (hitting a Hornet 60% of the time) for
        its full blow; the pebble is seen flying. Close enough to strike,
        it strikes instead (surer, and quicker). Knowing it can hit back
        from a few paces off, it's 25% likelier to stand and fight a
        chasing swarm rather than run. And a Guard with a sling doesn't give a
        hornets' nest within 14m of home a wide berth: while it's fit (60%
        Health or more) it picks the swarm off from just outside its reach.
    *   **Hearth** (needs a House, and worked out in autumn or winter,
        when the cold sets a clan thinking about fire): a ring of stones out
        front of each House where a fire burns while it's fed — one twig
        keeps it going 120s, and it holds three. A clan's Builder (or, with
        nothing to build, its most diligent Gatherer) keeps it topped up.
        Food taken from a store beside a lit hearth is **cooked**: it fills
        a little more and heals a little more. Folk wintering in beside a
        lit hearth get hungry more slowly than in an unheated home, and
        heal faster resting there. Drawn as a ring of pebbles with a fire
        that shrinks as the fuel burns down (ash when it's out).
    *   **Snares** (needs a House): two baited grass snares set 3–7m from
        each House. A Grub smells the bait from 12m, crawls in and is
        caught, its meat left on the spot for the Gatherers to bring home.
        A Gatherer with nothing better to do sets a sprung snare again.
        Snares rot away with their clan.
    *   **Herb-lore** (needs farming, a House, and someone sick to try it
        on): the Leader keeps a clever, kindly member as **Healer** while
        anyone is sick or below 60% Health. The Healer goes to the patient
        near home most in need — the sick before the hurt — and tends it
        for a few seconds: the sickness passes 20–40s sooner, and the
        wounds close by 3–6 Health, even on the sick (who can't otherwise
        mend). A Healer at work holds out a green poultice.
    *   **Herding** (needs farming, a House, and the aphids of spring or
        summer): the clan fences a pen of grass stems by its main home and
        gathers a pair of aphids off the stems. Each aphid gives a drop of
        **honeydew** about every 150s (a third as fast in winter) — food
        that lies four times as long as a berry before it spoils, and a
        little thirst-quenching — which Gatherers bring in like any food
        near home; a pen stops giving with three drops lying uncollected.
        A pair or more breeds up by one every 240s outside winter, to five.
        The ants go after pens as well as stores (two in five ants try a pen
        first) and carry aphids off to their hill; the Wolf Spider takes one
        when it passes close (at most one every 40s); a war raider may drive
        one off to its own clan's pen (35%, adding grievance). A clan whose
        herd dies out gathers a new pair in time; one that moves house
        drives its herd along; a pen whose clan is gone empties as the herd
        strays.
    *   **Shields** (needs spears, and a hunting or martial tradition):
        round shields of stag-beetle shell — every blow and bite on a grown
        member does two-thirds of its damage (at least 1). Carried on the
        arm when fighting, guarding, raiding or duelling, with a boss in
        the clan's colour.
*   The Stats tab and the clan card list a clan's crafts; the headless
    summary counts them. Over 13 years a clan works out about six and
    teaches eleven; most clans end up knowing all of them.

## Sickness, Ants & Floods
*   **Sickness:** anyone may fall ill (odds 1/8000 a second, doubled in
    Winter and again when hungry), and it spreads: when the sick meet
    someone (see Encounters) the other catches it at 0.2 odds. An illness
    lasts 60–150s: the sick walk 25% slower, get hungry 30% faster,
    can't heal, and lose 1 Health every 7s (every 4.7s for elders and the
    young). Recovered, they're immune for a year. It kills mostly elders
    and the already weak — about 17 deaths in 13 years out of some 290
    cases. Three ill at once in a clan is an outbreak (a headline). The
    sick carry a pale green blotch over their heads; the HUD counts them.
*   **Ants:** a colony lives in a fixed ant hill in a corner of the garden
    (see The Ant Hill below). From spring to autumn single thief ants
    leave it — 2, plus one per 15 food it has taken, up to 6 — and rob the
    nearest store within 70m (never a palisaded one), or glean loose food
    within 30m of the hill, and carry it home; in winter they stay
    underground.
    Easily swatted (5 Health), they bite back at whoever hits them.
    Bramblekin go for any ant near their home, or one biting them. There
    are no mass raids on villages: a thief is one ant at a time, and the
    kin defend against it as before.
*   **Floods:** a spring or autumn storm is a downpour 40% of the time.
    The water rises through it over the lowest 15% of the garden, then
    drains away over a minute. Under water, loose food and twigs float
    away and bushes lose their ripe berries; a flooded Tent loses its
    store and is swept away at even odds, a flooded House loses half its
    store. About three floods in 13 years.

## The Ant Hill
The ant colony is the garden's dungeon: a big, fixed hill in one corner,
guarded by ants, which only a Kingdom's army can break into. Each
assault that wipes out the guards and brings its prize home makes the
next hill tougher and the reward richer.

*   **The hill:** a model (`Assets/Models/Props/AntHill.glb`, drawn 10m
    wide, solid for 4.4m round its middle) that stands in a corner of the
    map from the first day and never moves. The corner is the terrain's
    own, the same for every garden on it: of the four, the one with room
    for it that is farthest from the oak and the water and has the
    gentlest ground (`World.PlaceAnthill`). Its mouth faces the garden.
    It can't be destroyed: it keeps growing.
*   **Thief ants** (`Assets/Models/Props/Ant.glb`, rigged with a six-legged
    tripod walk by Tools/convert_beetle.py, about 0.8m long): from
    spring to autumn single ants come out of the mouth — 2, plus one per
    15 food the hill has taken, up to 6 — and rob the nearest store within
    70m (never a palisaded one), or glean loose food within 30m, and carry
    it home; in winter they stay underground. Easily swatted (5 Health,
    a bite of 2 back at whoever hits them). A thief shut out by a wall for
    8s drops what it carries, gives the errand up and keeps off that store
    for two minutes. Bramblekin go for any thief
    near their home. There are no mass raids on villages: a thief is one
    ant at a time, and the kin defend against it as before.
*   **The no-go zone:** round the hill is a zone 12m out (the attack range). Nothing is
    built, planted, settled, set down (food, stones, branches, snares,
    wells, walls, feast sites, shrines) or wandered to in it, and every
    clan treats it as a standing danger. The only way in is a Kingdom
    assault.
*   **Guards:** the hill's level L (1 to start) sets how many guard it:
    **5 × L ants** — 5, 10, 15, 20 and so on, with no cap. A guard (24
    Health, bites for 4 every 1s, walks at 2 m/s, about 1m long) stands
    falls on any Bramblekin that enters the zone and chases
    it until it is dead or 6m clear of the zone. They fight to the death. Only an army can kill them: a Bramblekin
    that is not in a Kingdom's assault scratches a guard for 1 at most
    (a bystander that wanders in can't clear a hill, only die), so the
    guards are only ever brought down by a Kingdom's picked soldiers.
*   **Sentries and alert:** in quiet times only 2 guards (the sentries)
    roam about the hill, within 6m of its foot; all the rest wait inside.
    When any Bramblekin comes within 6m of the zone's edge the hill is on
    alert: the guards inside come out one by one over a couple of seconds
    and stand ready — they still attack only inside the zone. Ten seconds
    after the last Bramblekin has gone, all but the sentries go back in.
*   **Healing and refilling:** when no kin has been in the zone for 10s,
    wounded guards go back inside the hill and heal (1 Health a second),
    and every guard that died is replaced (one every 3s, up to the hill's
    level), so the next fight meets the full 5 × L. In winter the guards
    stay inside unless someone comes in.
*   **A fight:** from the first blow at the hill until no kin is in the
    zone. It is won when no guard is left alive while the party is in the
    zone. If it ends with any guard alive, nothing changes: the hill heals,
    refills, and the next fight starts from the same level again.
*   **The prize:** after a win, the victorious troops climb the hill's
    garden side (6s up, 1.5s to take an egg from the crater at the top, 4s
    down; they are drawn up the slope) and pick up the reward: **L eggs**, where L is the level just beaten (a
    win at 5 ants gives 1, at 10 gives 2, at 15 gives 3, at 20 gives 4,
    with no cap). They carry the eggs back to the capital. **The hill's
    level goes up only when a carrier reaches the capital with the
    eggs**: until then it stays at the beaten level (and its guards refill
    at that level). If the carriers die, or the eggs aren't home in 180s,
    there is no level-up and no reward; the fight has to be won again.
*   **The Kingdom decides:** at each look at a kingdom (every 5s) the crown
    may order an assault (one look in four once it is ready). It needs a
    king and enough fit soldiers (Guards and Hunters of any of its
    villages, grown, not elderly or sick, at 80% Health or more and not
    hungry) — **half as many as there are guards, rounded up**: 3 for 5,
    5 for 10, 8 for 15, 10 for 20 — **and half as many again for each
    assault on this level that has failed since the last win** (a failed
    first try means 5 soldiers next time, then 6, 8…); the Kingdom waits
    until it has them, and the count starts again after a win. It goes in
    daylight and not in winter. After a
    fight (won or not), no new order for a season. The strongest and
    bravest are picked. A bigger hill needs a bigger army, with no cap. A
    Headline, a Chronicle entry and the Director's spotlight follow it.
*   **The march:** the party **musters** at the capital's middle (up to
    60s for stragglers), **marches** together to a staging point 8m
    outside the zone (up to 90s), then **fights**. On the road a soldier
    still answers a threat (a Hornet, the Wolf Spider) as any kin would
    and then goes on.
*   **Retreat:** the Kingdom calls a retreat when half of the party has
    died or run away, or a fight drags on past 240s. A retreat takes
    everyone home, with no blame.
*   **Running away:** each kin in the fight decides for itself. Once its
    Health is at 35% or lower it weighs fleeing against fighting on, once
    (again if it is patched up past 60% and hurt again): it stays with
    odds of Courage × 1.15, so the very brave (0.87 and up) never run.
    A kin that runs, when the party hasn't been ordered back, flees the
    hill and is **shaken**: its Strength drops by a fifth for two days and
    it loses 0.05 Reputation. A recovered kin is as strong as before.
*   **Eggs and the reward:** the eggs go to the capital's middle with the
    carriers. When a carrier gets there, the eggs are **shared down the
    ranking** of the raid: the survivors (not those who ran) are ranked by
    guards killed, then by Health left, and the eggs are handed out one
    each from the top (round again if there are more eggs than survivors).
    Each kin eats its egg on the spot. An egg fills Hunger by 40 and is a
    permanent gain. Eggs are not capped; to keep a pile from making one
    kin unbeatable, the gains fade as they stack (each egg adds 85% of the
    one before):
    *   **Strength:** +0.04 for the first egg, +0.034 for the second, and
        so on, adding to the kin's own Strength with no hard ceiling.
        Strength makes blows harder and blows taken lighter (see Hunting &
        Defending). A shaken kin has a fifth less.
    *   **Vigor (a new stat):** the kin's energy. The first egg slows its
        hunger by 3% and speeds its walk by 1%; later eggs add less each,
        by the same fade.
*   **Showing it:** the Kin Inspector shows Strength (born, and with
    eggs), shaken, and Vigor; the Stats tab lists the hill's level and
    guards, assaults sent, won and called off, guards slain, best level
    beaten, eggs eaten and soldiers who ran; the headless summary counts
    them. A saved garden keeps the hill's level, the assaults won, the
    eggs eaten and each kin's own eggs eaten (not an assault under way:
    a loaded garden starts with none, like invasions).
*   **Tuning:** headless `--assault N [--assault-at S]` sends a test
    assault on a level-N hill at S seconds (default 60): the strongest
    grown kin there are, as many as the hill needs, made Guards. In a
    mature garden (12 seeds each, hornets and the Wolf Spider about) it
    won about 75% at level 1, 65% at 3, 65% at 6 and 50% at 8; a real
    kingdom's picked soldiers, shielded and with spears, did better still
    (nearly every assault up to level 8 in the long runs, usually with no
    losses) — so the guards may need to be tougher in later levels. Still
    first guesses, to tune by watching real fights: the
    zone's 12m, the guards' strength, the retreat and flee numbers, the
    party size and the egg numbers.

## Villages, Paid Jobs and Kingdoms
Full rationale and balance numbers: `Garden_Guardians_Society_Design.md`.
Technology (the ages) says what a society can build; society (clan,
village, kingdom) says who belongs together and who leads.
*   **Village:** homes within 14 m of each other, clans not at war, one
    clan in the Farming Age. It has a name, a middle, one or more clans and
    a **headman** chosen from the clan chiefs (leadership score, clan
    size, friends' votes). Clans keep their chiefs. Shown as a gold-edged
    tag on the map.
*   **Rations:** the village's store is the stock of its homes. Its
    `AllowedPaid` (from smoothed income and stock above a reserve of 0.7
    pieces per villager) is how many consumer jobs it can feed; those kin
    are brought food and never leave their post.
*   **Jobs:** the headman gives soldiers (patrol a ring of 8 waypoints,
    answer any home's alarm), a healer, builders and a scout; fishers and
    producers (farmer, gatherer, hunter) stay unpaid. Strong kin (the
    inherited `Strength` trait) and trained guards hit harder and take
    less; shields go to fighters only.
*   **Kingdom:** three or more villages within 90 m at peace, one in the
    Kingdom Age. The most populous such village is the capital and its
    headman is king. Vassal villages send tribute and pledge every second soldier (at least one) to march to a sister village's alarm. The old clan-over-clan
    vassalage stays for clans outside any village.
*   **The castle:** the capital's king's home (else the house nearest the
    village's middle) is raised into a **castle** model (`Castle.glb`, from
    `Tools/kin_src/Castle.glb` by `convert_tripo_prop.py`): about twice as wide as
    an acorn house (a 2.6 m radius, about 5.4 m across, 4.4 m tall), with its palisade
    ring, yard and fittings scaled to match. Everything standing on the ground
    it takes is cleared: scenery, crops, snares, a well, pieces of wall are
    removed, aphid pens and neighbouring homes are moved out beyond the ring
    (a home with no room is pulled down). If the kingdom falls, or the capital
    changes, the castle goes back to a house. Not saved: worked out afresh
    from the kingdoms.
*   **Invasions:** swarms of small spiders march on a village (light) or a
    kingdom (hard, in waves), sized to the target's defence; soldiers
    fight them, they withdraw after 5 minutes. Not saved.

## Neighbours: Alliances & War
*   *Kingdoms are formed by peace between villages, not by formal alliance (alliances proved too rare); see Villages, Paid Jobs and Kingdoms.*
*   **Neighbours** are groups whose main homes are within 30m. Between
    any two groups the World keeps a **stance** — Neutral, Allied or At
    War — and a shared **grievance** that fades by 0.004 a second (about
    2.4 a year): a killing adds 3, a raid 1, a robbery 1, a splinter
    leaving in anger 2, an exile's household 1.5.
*   **Leader decisions** (each Leader weighs every group it neighbours or
    has history with):
    *   **War** — a neighbour it holds a grievance of 10+ against, and a
        Leader at least 0.4 Aggressive (odds 0.1 × Aggression per decision).
    *   **Alliance** — a neighbour with little grievance (under 1), both
        Leaders at least 0.35 Sociable, neither with 2 allies already, and
        not Enemies: odds 0.02 × their average Sociability per decision,
        three times that if they're family or friends. A village and the
        daughter group that buds off it start out allied.
    *   **Rift** — a grievance of 3+ breaks an alliance.
    *   **Peace** — once the grievance has faded below 1.5, or after three
        seasons of war however bitter, or once its side is clearly beaten
        (see war outcomes): odds 0.02 + 0.08 × (1 − Aggression) per
        decision (halved in a fully warlike clan), plus 0.1 when beaten.
*   **Allies** defend each other (an ally being hit, or a raider heading
    for an ally's home, is a threat), never rob each other, meet as
    friends (sharing food), and marry across: a couple from allied groups
    forms, and one of them (not a Leader) moves to the other's village. A
    farming ally may teach the other to farm (odds 0.05 per decision).
*   **Aid in person:** a well-stocked group (5+ stored, 40% full) whose
    ally has run out (1 or less stored, a quarter of it hungry) sends a
    **runner** — its most Sociable fit, fed, grown member (never the
    Leader) — with up to 5 food in a sack slung on its back (odds: the
    Leader's Sociability per decision; up to 3 runners on the road to one
    ally at once). The runner walks it over and fills the ally's stores;
    anything that won't fit is left at the door. On the way it's
    vulnerable: a thief's successful blow grabs from the sack, and if the
    runner dies (or leaves its group, or the ally is gone) the sack
    spills on the ground for anyone to find.
*   **Trade — labour for food:** a group with building under way and food
    to spare hires a **helper** from an ally whose stores are under half
    full (odds 0.3 × the Leader's Sociability; one helper at a time). The
    helper walks over, fetches 3 twigs for the construction, and carries
    3 food home to its own store as pay. An employer that can't pay in
    full earns a grievance.
*   **Trade — stones and branches for food:** a clan that needs stones
    (for its well or a footing) or branches (for a palisade), with none
    lying within 20m of its main home and 4 food to spare, buys one from
    an ally that doesn't need that kind itself and has one lying within
    18m of its own home. The ally sends a **hauler**: it picks up the
    stone (or branch), carries it to wherever the buyers need it, lays it
    for them, and carries 2 food home as pay (one haul at a time per
    buyer). So a clan by the rocks comes to supply stone, and one by the
    oak, wood. If the buyers no longer need it, or there's none to be
    found, the hauler goes home unpaid.
*   **War:** members of warring groups keep their distance when they meet;
    a bold resident (Aggression 0.5+) drives off an enemy that comes within
    6m of home. An Aggressive Leader at war sends a **raiding party**: the
    boldest healthy half of its group goes for the richest enemy store
    within 60m, each Raider pushing past the defenders to take a piece of
    Food and carry it home (standing down at half Health). A party keeps
    at it for 45s or until the store is empty; the next can't set out for
    240s.
*   **War outcomes:** each side of a war keeps a score — 3 for every enemy
    it kills, 1 for every piece of food it carries off. A side with at
    least 4 and twice the other's has **won**; the beaten side is likelier
    to sue for peace, and the peace comes on terms. A small beaten group
    (5 or fewer, against a winner at least twice its size) is **absorbed**:
    its survivors join the winner (only grudgingly loyal, at 0.35) and its
    homes near the winner's village become part of it. A bigger one pays
    **tribute** — a runner carries 3 food to the victors every season for a
    year; a missed payment adds 2 to the grievance. Terms settle half the
    bitterness; a war with no clear winner ends in a plain peace.
*   Allied and warring villages are joined on the map by a green or red
    line between their main homes; the HUD counts current alliances and
    wars, and the Kin Inspector shows its group's.

## Festivals: Harvest Feasts
*   **Holding one:** at an autumn decision, by day, a clan with a good
    harvest in store (at least 12, and 3 a member) that isn't defending
    or raiding holds a feast, with odds 0.25 × (0.5 + its Leader's
    Sociability) — once a year. It lays out 3 pieces from its stores and
    sets up tables by its main home, ringed with bunting in its colours
    (lanterns glow after dark), for 40s. The Leader gains standing, and
    the Director puts the feast in its spotlight.
*   **Who comes:** the clan, its allies, and neighbours at peace within
    40m whose grievance is low — any grown member within 45m, unless on
    the night watch, a raid or an errand. A peckish guest is fed from the
    host's stores on arrival; everyone mills about the tables.
*   **What comes of it:** at a feast, kin of different clans can court
    even if their clans aren't allies (and nobody robs anybody). When it
    ends, each clan that came has 4 less grievance with the host, and a
    neutral clan that came two or more strong becomes an ally with odds
    0.3. The chronicle records the feast and its guests.

## Shrines and Beliefs
*   **Coming to a belief:** at each decision, a settled clan with no belief
    may come to revere something it has lived close to, with odds 0.006 ×
    (0.5 + half its Leader's Intelligence and Persuasiveness): **the Great
    Oak** (living within 35m of it), **the
    Still Water** (living within 12m of the pond), **the Spider** (having
    brought it down, or lost kin to danger near home), or **the Moon**
    (keeping a hearth through the nights). A guest clan with no belief of
    its own may take up its host's at a feast (odds 0.2). Daughter
    villages and splinters keep their parent's belief.
*   **The shrine:** the clan raises a shrine by its main home over a few
    decisions — a small cairn in a ring of pebbles, topped with its token
    (a carved acorn, a blue pebble, an eight-legged figure, a pale disc)
    and a pennant in its colours, with a candle that glows after dark.
    Once raised, it binds the clan: every member's loyalty settles about
    0.08 higher, so fewer rebel.
*   **Between clans:** neighbours who share a belief let grievances fade
    twice as fast and are twice as likely to ally; neighbours with rival
    beliefs slowly build up grievance (about one point a year).
*   **Prophets and schisms:** now and then (0.0015 a decision) a
    persuasive, rebellious member of a clan with a shrine has a vision of
    another belief, and leads the restless (the rebellious, or the less
    loyal) out to found a new clan and raise a shrine to it — a splinter
    with some bad blood.

## Champions
*   **Settling it by single combat:** a Leader with a feud (a grievance of
    5 or more) with neutral neighbours may, instead of letting it fester
    toward war, call for champions — odds 0.04 × (0.5 + its
    Persuasiveness) a decision; at war, 0.05 × the same, to end the war.
    Each clan sends its best fighter (fit, grown, not old; healthiest,
    fiercest, bravest, handiest, better with a shield), and the two fight
    it out between the villages as a duel (up to 70s) until one yields at
    half Health.
*   **What it settles:** the grievance is forgotten. A feud's loser pays
    3 food from its stores to the winner; a war's loser sues for peace
    and pays tribute, as if beaten in the war. The winner gains standing,
    counts a champion's win, and goes in the chronicle; the Director
    cuts to any contest under way. A contest that times out (or loses a
    clan) settles nothing.

## Weather
*   **Good and bad years:** each season rolls its weather. A Winter is
    **harsh** 20% of the time: food at 0.7× even the usual winter pace,
    and anyone caught outdoors gets hungry 15% faster. A Summer or Autumn
    brings a **drought** 15% of the time: berries and bushes grow at half
    pace, and the lawn turns parched gold. Any season but Winter may be
    **bountiful** (15%): food at 1.5×. The HUD's year line names the
    weather, the log announces it, and droughts and harsh winters go into
    the chronicle.
*   **Storms:** outside Winter and droughts, a storm blows up about once
    every 400s and lasts 25s: the sky darkens, rain streaks down, half the
    loose berries nobody has claimed are blown away, and twigs come down
    (up to 25 more than usual lying about) — good for builders. Anyone
    with a home and nothing pressing shelters from it, as in Winter.

## Day and Night
*   **The day:** 75s from dawn to dawn — two days to a season. Night falls
    about two-thirds of the way through (earlier in winter, later in
    summer), with a short dusk and dawn; the sky deepens to blue and the
    garden darkens, and the HUD's year line names the time of day.
*   **Sleep:** fed and safe, a Bramblekin goes to bed at night — home and
    inside if it has one (healing as if resting there), else where it
    stands, near its Leader if it follows one. Asleep, Hunger and Thirst
    rise at half the usual rate, and it only notices a threat within 2m;
    a raider creeping in doesn't wake it at all. Hunger or thirst still get
    it up for a bite or a drink. Sleepers out of doors show a drift of
    pale "z"s.
*   **Who stays up:** a settled clan of three or more posts a **night
    watch** by home — a Guard if it has one, else its bravest — who cries
    the alarm at anything it sees coming; so does a sleeper that's
    attacked. An alarm wakes the whole clan for 8s. Raiding parties keep
    going (and a shrewd Leader waits for dark to send one), and errands
    are seen through.
*   **Lights in the dark:** lit hearths throw a warm glow, Houses' windows
    shine and fireflies blink over the grass (not in winter) — drawn after
    the darkness, so they stand out.

## Memory
*   **Danger:** a Bramblekin stung by a Hornet or bitten by the Spider
    remembers the spot (up to 4 places), and so does its group; a group
    also remembers where a member was killed by a predator. For 300s it
    passes up food within 6m of a remembered danger — unless it's
    starving, when nothing is too risky — and wanders elsewhere.
*   **Good places:** where a member last saw food, its group remembers too
    (up to 6 spots). With no food in sight, a Bramblekin heads for where
    it last saw some, else the freshest spot its group knows from the last
    240s (that isn't dangerous); a spot that turns out empty is forgotten.

## Clan Culture
*   **Traditions:** a clan grows into what it does, a little at each
    Leader decision: at war, raiding or defending home grows its
    **Martial** tradition; hunting big game its **Hunting** one; tending
    bushes (in proportion to how many it keeps) its **Farming** one. Each
    runs 0–1 and fades if neglected (a half-life of about two years).
    Once one reaches 0.4 and leads, the clan is *known* for it — "a
    warlike clan" — and the chronicle notes it; the Kin Inspector and the
    History screen show it. The name sticks: a clan keeps it until that
    tradition fades below 0.3 or another overtakes it by 0.1, so two
    close traditions don't flip its name back and forth.
*   **Raised in the tradition:** a child is pulled part of the way toward
    0.8 on the traits its clan prizes — Aggression and Courage in a
    warlike clan, Courage, Aggression and Sociability in a hunting one,
    Intelligence and Diligence in a farming one — so a favoured trait
    settles there over the generations rather than piling up at 1.
*   **Traditions outlast Leaders:** they sway any Leader's choices — a
    warlike clan raids more, goes to war up to three times as readily,
    makes peace more reluctantly and is shunned as an ally; a hunting
    clan hunts more; a farming clan stockpiles more and keeps more
    Farmers. Children raised in the clan lean its way (bolder in a
    warlike or hunting clan, more sociable in a hunting one, sharper in a
    farming one), and daughter and splinter groups start with their
    parent's traditions.

## The Chronicle & History
*   **The chronicle** is the story of the garden's clans: foundings and
    endings, new Leaders and how old ones died, wars and how they ended,
    alliances, conquests and tribute, farming learned and taught, new
    villages and daughter groups, famines, great elders, the traditions
    clans became known for, and the worst weather. A clan's Wolf Spider
    hunts make it only as milestones — its first, then every fifth. Each
    entry is dated (year and season) and filed under the clans it
    concerns.
*   **The History screen** (the **History** button beside Map) has three
    tabs, each showing the selected Bramblekin's clan (or a clan picked by
    tapping one of its homes), else the whole garden; drag or scroll to
    read back:
    *   **Story** — the garden's timeline: the population and the number
        of groups over the whole run (sampled every 30s, with every clan's
        size), the six biggest clans' sizes as thin lines in their colours
        (with a clan selected, just its own), year marks, and every
        headline as a diamond along the top, coloured by kind — wars red,
        peace and alliances green, feasts orange, beliefs purple,
        champions and coups gold, splits and endings slate, hard times
        blue-grey. Tapping a diamond scrolls the chronicle below to that
        entry and highlights it. Then the chronicle, newest first.
    *   **Stats** — a chart of food stored and berry bushes, the clan at a
        glance (members, Leader, founding, homes, stores, bushes, spiders
        slain, traditions, neighbours), and the garden's totals since it
        began: population, deaths by cause, food, homes, clans and
        politics, neighbours (alliances, wars, conquests, tribute, aid,
        trade), farming, hunting and weather.
    *   **Heroes** — the hall of fame, living or dead: the longest reign,
        most children, most descendants, the oldest, the top Wolf Spider
        slayer, the biggest family alive and the oldest clan standing —
        and the selected Bramblekin's family tree: parents and
        grandparents (with the year each died), partner, children,
        grandchildren and all its descendants.
    Headless runs print the chronicle at the end.
*   **Every life is kept:** the World keeps a short record of every
    Bramblekin that has ever lived — name, parents, generation, when it
    arrived and died, how it died, its clan, children, reign and spider
    kills — so family trees and the hall of fame reach back through the
    generations.
*   **Big moments** — a war, a conquest, a peace or tribute, a famine
    (4 starving to death in one season), a new village, a clan splitting
    or ending, a coup, an alliance, farming worked out, a drought or a
    harsh winter — go up on a **banner** at the top of the screen, under the buttons, for a few
    seconds (the urgent ones — war, conquest, famine — longer, in red).
    Tap it to fly the camera there. The speed is the player's alone: no
    banner ever changes it. The **Alerts: on/off** button beside **Stats**
    hides them (the headlines still reach the log and the chronicle); the
    choice is remembered.

## Save & Load
*   **The garden carries on:** the game autosaves every 30s of real time
    and on the way out, and picks up where it left off at the next start.
    A save keeps everything lasting — the props, homes and their stores,
    bushes, loose food and twigs, every Bramblekin (who it is, its family,
    partner, home, group, job, loyalty, reputation, relationships, what it
    knows and remembers, its errand), every group (Leader, homes, goal,
    traditions, memories), the relations and tributes between groups,
    the weather, the chronicle and history, and every counter and
    statistic, and the record of every life. What a Bramblekin was doing
    that very second (and where the Hornets and the Spider were) isn't
    kept; each picks up again within moments.
*   **New garden:** the **New** button (shown while the History screen is
    open) starts over — tap it, then tap **Sure?** within 3s. It only
    starts the open garden over.
*   **Three gardens:** beside **New**, the **Garden N** button keeps the
    open garden (saving it) and opens the next of three — carrying on
    where it was left, or a fresh garden if that slot's empty — so a
    favourite garden can be kept aside while another grows. The History
    header names the garden ("Garden 2, year 5"); which one is open is
    remembered between runs (`garden=Garden2` in `settings.txt`).
*   The save is JSON (`garden.json`, `garden2.json`, `garden3.json` in the app's local data folder),
    written to a temporary file and moved into place so a crash can't
    leave half a save. A save that can't be read, or is from an
    incompatible version, is ignored and a fresh garden begins. Headless
    runs can `--load` a save and `--save` one at the end.

## A Small World: the Pond and the Giant Oak
*   **The pond:** water always stands in the lowest 5% of the garden —
    a pond in the main hollow, and a pool cut by the east edge. Nothing
    is built, planted, spawned or set down in it (it counts as blocked
    ground for every site, spawn and wander point), and nothing that
    walks ever steps into it: Bramblekin, the Wolf Spider, beetles, grubs
    and ants all find their way round (see Code Layout & Performance);
    Hornets fly over. A flood rises out of the pond and drains back into
    it — flood water is shallow, and walkable. The water is drawn only
    over the ground that dips below it.
*   **Droughts drink the pond down:** through a drought the water sinks,
    step by step over some 110s, to 0.92m below its usual level — about
    a sixth of the pond left, a puddle at the bottom of each hollow —
    laying bare a ring of mud that anyone can walk across. Drinkers and
    fishers follow the water out across it (a longer walk for everyone,
    a crowd at the puddles), a smaller pond holds fewer fish (catch odds
    ×0.3–1), and watercress stops springing up on the old shore. After
    the drought the pond fills back over 120s — four times as fast in the
    rain — and a storm in a drought fills it too. Five levels are worked
    out once (`WaterMap.Levels`), each with its own wet grid, route grid,
    distance field and shore; walkers look again at their way round
    whenever the level changes. Nothing is ever built in the pond's bed.
*   **What the water gives:** a drink (see Thirst) — the one thing
    nobody can do without, and the reason the garden's dry south-west is
    the hardest place to live. Watercress grows wild along the shore
    (most in spring, and never minding a drought); clans near it can
    fish and plant cress beds; crops within 8m of it are watered — faster,
    and drought-proof (see Farming).
*   **Settling by the water:** a Bramblekin marking out a home takes, of
    the open spots it tries, the one nearest water; pioneers founding a
    new village weigh every meter to the water's edge (0.35 per meter, up
    to 50m) against open ground and berries. How far is measured walking
    round the pond (`WaterMap.DistanceToWater`); the Kin Inspector shows
    how far its home is from water.
*   **The creek:** a spring rises in the garden's driest corner, some 70m
    from either pond, and its brook runs about 28m downhill (worked out
    from the ground, a meter a step) into a small pool in the next hollow.
    It's narrow enough to step across, so it blocks no one, and
    spring-fed, so a drought doesn't touch it. Its banks are somewhere to
    drink and to grow cress; it counts as water for anyone weighing up how
    far they live from some (settling, cisterns, wells, watered crops,
    the Kin Inspector); and nothing is built or planted on it. Fishing
    stays at the ponds. About a third of all trips for
    water end at the creek. Drawn as a ribbon of water with the spring's
    stone at its head and the pool at its foot.
*   **The Giant Oak:** the foot of a real tree stands at the garden's back
    edge — a trunk 12m across, ridged bark and a mossy foot, rising far
    out of sight, with one great bough overhead and ten great roots
    arching out from high on its flank and diving into the lawn 7–13m
    from the trunk (up to 2m thick) — so the garden reads as the small
    world it is. Trunk and roots are solid: nothing that walks can climb
    over them (they're obstacles, and in the route grid), so getting
    round the oak means walking round the root tips, and nothing is built,
    planted or set down on them. Loading an older garden clears whatever
    stood where the roots are now. Its shade darkens the lawn around
    it. In autumn it drops an acorn every 4s (up to 12 lying about at
    once) up to 16m from the trunk: food like any other, for whoever
    gathers it. Loading an older garden clears away anything that stood
    where the trunk is now.

## Food & Wildlife
*   **Invader spiders** (`InvaderSpider`, 0.7 m, 20 Health, 5-damage bites) arrive in waves against villages and kingdoms; see Villages, Paid Jobs and Kingdoms.
*   **Food:** wild Berries grow passively (one every 0.8s, up to 75 on
    the map, both scaled by the season), about two-thirds in eight Berry
    Patches around Dandelions. Besides them, all at their season's pace
    (the same curves as the crops): **watercress** springs up on the shore
    (every 7s, up to 5); **mushrooms** come up in the oak's shade and at
    the foot of the rocks (every 9s, up to 6, twice as fast for a minute
    after rain); **grass seed** is shed in twos and threes on the open
    lawn (every 8s, up to 6, high summer into autumn). The oak drops
    acorns in autumn, fishers land fish, and hunted Grubs, Stag Beetles
    and a slain Wolf Spider drop meat. Every kind is worth the same one
    bite; what differs is where and when it turns up. The headless summary
    and the Stats tab count how much of each was eaten or stored. Loose Food rots after 60s; stored Food never does. A
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
*   **Garden Props:** Pebbles (solid rocks), Twigs (big sticks, where
    fallen twigs gather) and Dandelions (where berries grow).
*   **Stones and branches:** building material bigger than a twig. Stones
    work loose at the foot of the rocks (16 to start, one every 12s up to
    24; they never rot); thorny branches come down off the oak in a storm
    (one every 6s of it) and off the big sticks now and then (every 90s),
    up to 10, rotting after 900s or carried off by a flood. A clan with a
    footing or palisade to finish keeps its most diligent Gatherer as a
    Builder, fetching them from up to 60m away — a stone carried in front,
    a branch dragged behind (at three-quarters pace). Allies trade them
    too (see Neighbours).
*   **Wandering Arrivals:** every 15s, while fewer than 30 Bramblekin are
    alive, a new solitary one with a freshly rolled Personality wanders
    in from a random edge of the map — so a hard winter never ends the
    world, but growth beyond that has to come from births.

---

The superseded **Macro-RTS Era** design and the **Original Vision** pitch
are kept for history in `docs/archive/Garden_Guardians_Superseded_Design.md`.

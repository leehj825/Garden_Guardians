# Garden_Guardians_Roadmap.md

**Status key:** ✅ Done · 🟡 In progress (partly done) · ⬜ Not started · ❌ Removed/superseded

*Last updated: 2026-09-29*

## Progress Snapshot
The game is an **Emergent Survival** simulation (Phase 7) that has grown
a **society** (Phase 8), a **living population** (Phase 9), **lives &
lineages** (Phase 10) and **farming & neighbours** (Phase 11). There are no factions or top-down economy: the
map is a 3D terrain model and loose entities — wild Food, Twigs, a
Wolf Spider, Hornet swarms, Grubs, Stag Beetles, Garden Props — and the
Bramblekin. Every Bramblekin is an individual agent with a random
Personality (Aggression, Sociability, Intelligence) serving a strict
Hunger → Safety → Duty → Settle → Social hierarchy of needs. Loners build
tents and stock them; groups form from encounters, share a home they
upgrade into a house, hunt big game as a pack, and are run by a Leader
who picks the group's goal, hands out jobs and decides who eats first.
Followers' loyalty rises and falls with how they're treated; the disloyal
walk out, split off, or challenge the Leader, and Leaders exile
troublemakers. The year turns through four seasons of plenty and
scarcity; named Bramblekin pair up as couples, thriving groups raise
young who inherit their parents' traits and family names, grow into
villages of up to three homes, and bud off daughter groups; the old grow
grey and die. A follow camera keeps any one of them in view.
Since Phase 26 the garden has **day and night** (sleep, a night watch, the
Owl), **aphid herding**, a **beehive** in the oak, **harvest feasts**,
**shrines and beliefs**, **shields and champions**, an automatic
**Director** camera and a **timeline** on the History screen.
Code lives under `Source/` (one type per file; see the
Design doc's Code Layout). The player is a spectator with a
Google-Maps-style camera whose only action is tapping a Bramblekin to
inspect it. See `Garden_Guardians_Design.md` for the full current design.

## Phase 0: Engine & Tooling
*   ✅ **Engine:** Raylib via Raylib-cs on a .NET 8 project, 1 unit = 1
    meter.
*   ✅ **Android build:** `Platforms/Android/build-raylib.sh` builds
    raylib for Android with the NDK; the C# game loop runs inside a
    `NativeActivity`, sharing all code with desktop. The window now opens
    at the device's actual native resolution (`InitWindow(0, 0, ...)`)
    rather than a fixed 1280×720 virtual canvas letterboxed to fit —
    every UI/culling call reads `Raylib.GetScreenWidth()/GetScreenHeight()`
    dynamically, so it fills whatever size that turns out to be.
    Landscape orientation is locked via `MainActivity`'s
    `ScreenOrientation` attribute.
*   ✅ **CI:** GitHub Actions builds a debug APK on every push to a
    branch other than `main`; pushes to `main` build a keystore-signed
    release APK.

## Phase 1: The Original Micro-Loop (miracles) — ❌ Removed
Everything in this phase was built against the original "player casts
miracles" pitch. The Gust and Pebble-Drop were fully prototyped —
physics, God's Shadow, misdirection, the works — and then deliberately
torn out once the game committed to Pure Simulation in Phase 3. None of
it exists in the current build. It's kept here, marked removed, purely
as a historical record; see `Garden_Guardians_Design.md`'s "Original
Vision" section for the full original write-up.
*   ❌ Custom physics loop, Pebble-Drop, God's Shadow, rock clutter
    control, The Gust — all built, then removed.
*   ❌ The Dewdrop, The Sunbeam — designed, never built.
*   🟡→✅ **The Scurry AI state machine survived the pivot** in spirit:
    Bramblekin still run a priority-ordered state machine (Walking,
    Pausing, Gathering, Returning, Fleeing, Defending, Hunting, Raiding,
    Building, Equipping, Migrating), and Fear Aura/Fleeing still exists —
    just triggered by proximity to a live threat, not a God's Shadow.
    "Seeking `Cover_Small`" and "Play Dead" were never built.
*   ✅ **The Wolf Spider** survived the pivot essentially intact:
    Prowling, Hunting, Pouncing, Recovering, Investigating and Feeding
    states; vibration-based aggro (7m) against Gathering/Returning
    Bramblekin; a pounce that kills a caught Gatherer outright; a feeding
    period after a kill. What changed: it's now pure health-based combat
    (50 HP, killed by Militia Pokes) rather than a pebble-crush kill, and
    every faction gets its own independent spider spawn/respawn cycle
    tied to its own territory rather than one shared map-wide spider.

## Phase 2: Economy & Combat Infrastructure (survived, then expanded) — ❌ Superseded by Phase 7
*Only loose Berries, the Wolf Spider (reworked), Hornets and Grubs
(reworked) survived the Phase 7 pivot; the rest of this phase describes
systems that no longer exist.*
*   ✅ **Gathering loop:** wild Berries spawn passively; Acorns are
    cracked via **Cooperative Acorn Cracking** — up to 3 Chitin-Mallet
    Gatherers working the same Acorn at once, their crack rates simply
    adding together, yielding 4 Food Shards on shatter. Rocks/obstacles
    shove loose food aside instead of burying it.
    *   ❌ The original "drop a pebble on the Acorn" trigger is gone —
        cracking is now a pure Gatherer-labor loop with no player
        involvement.
*   ✅ **Ambient prey:** wandering Aphids populate the map; Militia hunt
    them within their own territory ring, and a kill drops Food Shards.
*   ✅ **Storage capacity:** `FoodStored` caps at `MaxFoodCapacity` (a
    base value, permanently raised by each completed Granary), Food
    delivered past the cap is still collected but wasted.
*   ✅ **The Smarter Economy — Dynamic Goals:** each Village Heart
    compares Population against `MaxPopulation` (a separate, Tent-driven
    ceiling — see the Housing System below) every frame and runs three
    independent, fixed-priority phases: **Housing** (queue a Tent once
    population-capped), **Growth** (Auto-Sprout while there's room),
    **Storage** (bank toward a Granary once Food Stored is effectively
    full). A Spore Farm scaling system and the Trading Post/Brewery ride
    on top as auxiliary, population/wealth-gated one-time builds.
*   ✅ **The Housing System:** `MaxPopulation` starts at 10 and rises
    permanently per completed Tent, hard-capped at `MaxPopulationCap`
    (40) — fully decoupled from `MaxFoodCapacity`/Granaries.
*   ❌ **The Faith Pool** (the old miracle economy) is gone entirely —
    there's nothing left in the game that spends it.
*   ✅ **Upkeep & Starvation:** every 30 seconds (doubled from an
    original 15s once the economy no longer needed to feed a
    Faith-hungry player) the Village Heart pays a food tax of
    `Math.Max(1, Population / 5)`; if it can't afford it, Food Stored
    drains to zero and one Bramblekin (a Gatherer over a Militia unit,
    when there's a choice) dies of starvation, with a floating pop-up
    either way.
*   ✅ **Auto-Construction, generalized:** Granary, Spore Farm, Trading
    Post, Tent, the Nectar Brewery and the Great Monument (see Phase 5)
    are all built from the same Blueprint/Building pattern — a Blueprint
    is placed for a resource cost, any of that faction's Builders work it
    up to its own Construction Progress requirement, and
    `World.CompleteBlueprint` turns it into a permanent Building with
    whatever stat bonus that kind grants.
*   ✅ **Builder AI priority:** an incomplete Blueprint unconditionally
    outranks Gathering the instant a Gatherer evaluates its next state;
    exactly one Builder is conscripted per faction while a Blueprint is
    outstanding.
*   ✅ **The Spore Farm:** scales with population (one more queued every
    time Population crosses another multiple of a threshold, up to a
    per-village cap), each one a permanent passive-Berry-income
    structure once built.
*   ✅ **Auto-Conscription:** no player-facing slider — the Job Manager
    recomputes each faction's own Militia Target every frame from its
    living population, using a per-faction ratio driven by that Village
    Heart's fixed-for-life `FactionTrait` (Balanced/Militaristic/
    Agrarian).
*   ✅ **War Weariness / Morale:** a per-faction Morale stat (0–100%,
    starts at 100). A Bramblekin kill and an actively-hunting spider both
    drain it; it recovers on its own otherwise. Below a low threshold,
    Gatherers/Builders are Weary and move slower; above a high threshold,
    Builders work faster ("Inspired").
*   ✅ **Found-Object Weaponry:** the Wolf Spider drops a Spider Fang
    *and* a Chitin piece wherever it dies. Banking 2 Fangs permanently
    unlocks Fang Pikes for that faction's whole Militia (doubled Poke
    damage, pike renders silver); touching one Chitin piece unlocks that
    specific Gatherer's Chitin Mallet (Cooperative Acorn Cracking
    access). Both are per-unit equipment that perishes with the
    Bramblekin holding it.
*   ✅ **Sustained health-based combat:** Militia Poke the spider (and
    each other, and enemy Village Hearts) for real damage on a strict
    per-unit cooldown; the spider Bites back on its own cooldown. Either
    side hitting 0 HP is a permanent kill/razing.

## Phase 3: Factions, Territory & the Pivot to Pure Simulation — ❌ Superseded by Phase 7
*Pure Simulation (no player lever) survived the Phase 7 pivot; factions,
territory, schisms and wars did not.*
*   ✅ **Dynamic Factions & the True Schism:** an overcrowded, food-rich
    Village Heart splits roughly in half; Pioneers migrate well clear of
    every existing Village Heart (a strict minimum distance, not a fixed
    35m in the current constants) to found a new, differently-colored
    faction.
*   ✅ **Default Peace:** every faction starts and stays at peace with
    every other; war is only ever provoked, never assumed from mere
    proximity.
*   ✅ **Territory — now Cultural Borders (dynamic radius):** a Village
    Heart's territory ring is no longer a flat 20m for every tribe. It's
    computed live as `15.0 + AmberStored × 0.5 + NectarStored × 2.0`
    meters — a wealthy, advanced tribe's ring physically grows and can
    overlap into a poorer neighbor's without either side's Gatherers
    ever counting as trespassing there, since Strict Border Control only
    ever excludes what falls inside the *other* faction's own current
    ring.
*   ✅ **Thievery & Escalation:** stealing from foreign territory
    triggers a Warning Shove; retaliation (or an armed trespasser)
    escalates into a Blood Feud — mutual, timed, all-out hostility
    between those two specific factions.
*   ✅ **Base Razing & the Refugee Protocol:** a razed Village Heart
    scatters loot; surviving Gatherers/Builders either found a new
    Village Heart as Pioneers, or are assimilated into the attacker's
    faction if the map has no safe ground left for a new one.
*   ✅ **Survival failsafes:** the Wolf Spider overrides every faction
    war while actively threatening a tribe's own territory ("Enemy of My
    Enemy"); total extinction is recoverable via a free Genesis tap.
*   ✅ **The Pivot — Pure Simulation:** this is the phase where the
    original miracle-casting player role was removed entirely. From here
    on, `WorldTapInput` only ever does two things: faction inspection and
    Genesis. Every other system in the game — including everything in
    Phases 4–6 below — was designed and built with zero player
    intervention as a hard constraint.

## Phase 4: The Spectator Camera & Mobile UI
*   ✅ **The God-Camera:** replaced the original fixed 45° isometric view
    with one pulled back and up far enough to see the whole 100m×100m
    map at once, correctly centered on the terrain's actual origin.
*   ✅ **One-finger panning:** drags the camera across the X/Z plane,
    scaled by current zoom distance so it feels consistent whether
    zoomed in or out; also works with a held left mouse button for
    desktop testing.
*   ✅ **Two-finger rotation:** twisting two fingers around each other
    orbits the camera around its current focus point on the Y axis.
    Fixed two real correctness bugs that caused sudden "flips": raylib's
    touch-point index isn't stably mapped to the same physical finger
    frame to frame (fixed by matching points to their nearest neighbor
    from the previous frame instead of trusting index order), and
    `Atan2`'s ±180° branch cut produces a spurious ~2π jump if not
    explicitly wrapped back into `(-π, π]` before use.
*   ✅ **Two-finger pinch-zoom** and **two-finger tilt** (sliding both
    fingers up/down together adjusts camera pitch), both layered onto
    the same two touch points as rotation.
*   ✅ **Tunable sensitivity:** `PanSensitivity`/`RotationSensitivity`
    constants isolate "how the gesture math works" from "how fast it
    feels," so overall feel can be tuned without touching the geometry.
*   ✅ **Tap-vs-drag disambiguation:** `WorldTapInput` now fires its tap
    (faction-select/Genesis) only on release, and only if the press
    never crossed a small drag threshold — so panning and tapping never
    fight over the same touch.
*   ✅ **Mobile-scale UI:** the Faction Ledger and bottom status bar's
    text were both scaled up significantly (with the status bar gaining
    a background bar for contrast) for legibility on a phone at arm's
    length; the Debug Time Scale +/- buttons were scaled up 3x.

## Phase 5: Performance for a 5x Larger Map
*Squared-distance math, the spatial grid, Food pooling and culling all
survived Phase 7; frame-counter AI time-slicing was replaced by staggered
per-Bramblekin perception timers.*
The map grew from 20m×20m to 100m×100m over the course of the project, a
25x increase in area, which made the original "scan every entity every
frame" approach to AI targeting a real bottleneck at high time-scales.
*   ✅ **Squared-distance math:** every AI targeting/aggro/territory
    check was converted from `Vector3.Distance`/`MathF.Sqrt` comparisons
    to squared-distance comparisons against a squared threshold.
*   ✅ **AI time-slicing:** `World.FrameCounter` plus a stable per-unit
    `Bramblekin.ID` gate each unit's expensive target-scanning logic to
    run once every 15 frames, staggered across the colony, while
    movement/combat/pickups (driven off whatever was cached on the last
    scan) keep running every frame.
*   ✅ **A spatial grid** (`SpatialGrid<T>`) buckets Food Shards, Acorns,
    Amber Nodes and the Colony into 10m chunks, rebuilt once a frame;
    nearest-target searches only look at a searcher's own chunk and its
    8 neighbors.
*   ✅ **Object pooling:** Food Shards, Acorns and Amber Nodes are
    pre-allocated fixed-size pools at startup instead of being
    constructed/destroyed on every spawn, pickup, or despawn.
*   ✅ **Raylib culling:** entities and health bars entirely outside the
    camera's current viewport skip their draw call.

## Phase 6: The Refined Economy & Civilization Goals — ❌ Superseded by Phase 7
*   ✅ **The Nectar Brewery:** auto-queued once a Village Heart reaches
    20 Population and 10 Amber Stored (25 Construction Progress to
    build). Once built, it consumes 2 Food + 1 Amber every 30 seconds to
    brew 1 Nectar — skipping a cycle rather than failing if the village
    can't currently afford it that round.
*   ✅ **Nectar as a permanent civilization buff:** every point
    permanently adds 5% Gatherer walk speed (capped at +50% total) and
    further widens that faction's Cultural Borders (see Phase 3). Shown
    on the Faction Ledger in a distinct pink/purple.
*   ✅ **The Great Monument:** once a tribe hits 40 Population and 50
    Amber Stored, it commits its entire Builder effort to one — every
    other Auto-Construction phase (Tent/Granary/Spore Farm/Trading
    Post/Brewery) stops queuing new work until it's finished; population
    growth is unaffected, since it isn't a building. Requires 200
    Construction Progress (by a wide margin the largest build in the
    game) and renders as a stepped, three-tier stone pyramid with a gold
    capstone.
*   ✅ **The Monument completion alert:** finishing one raises a
    permanent, non-blinking, faction-colored banner across the top of
    the screen for the rest of the game, marking that faction's
    transition into an advanced civilization even if its Village Heart
    is later razed.

## Phase 7: The Emergent Survival Pivot
*   ✅ **The Engine Purge:** kept Raylib, the procedural terrain
    (`World.GetHeightAt`/`GetNormalAt`), the cached Bramblekin body model,
    the spectator camera and the environmental entities; deleted
    `VillageHeart`, `Blueprint`, `Building` (Spore Farms, Granaries and
    every other building), the Job Managers, Crusades, Invasions, Vassal
    Tributes, Diplomacy/Goodwill, Amber, Acorns, Aphids, the Rival Ant
    Colony and the Elder Spider. `Program.cs` went from ~14,300 lines to
    ~5,600 (and was later split into `Source/` — see Phase 8).
*   ✅ **Personality (DNA):** Aggression, Sociability and Intelligence,
    each rolled uniformly 0..1 whenever a Bramblekin is spawned.
    Intelligence scales detection radius from 5m to 20m.
*   ✅ **The autonomous survival loop:** a strict Hunger → Safety →
    Social hierarchy (see the Design doc). Hunger rises constantly and
    kills at the top; threats get a per-threat fight-or-flight roll off
    Aggression; fed and safe, Bramblekin wander, pocket a spare bite, and
    seek out (or avoid) others by Sociability.
*   ✅ **Emergent relationships & grouping:** each Bramblekin has a
    nullable `GroupId` (a `Guid`) and `KnownKins` (Friend / Neutral /
    Enemy). Encounters resolve into robbery (starving + Aggressive),
    alliance (both threatened by a predator, or both Sociable), food
    sharing or plain acquaintance. Groups cap at 6, are led by their most
    Intelligent member (Phase 8 replaced this with a leadership score and
    challenges), follow their Leader, borrow its senses when hungry, and
    defend each other from predators and hostile Bramblekin.
*   ✅ **Wildlife reworked for individuals:** the Wolf Spider hunts any
    Bramblekin busy with food and drops meat when a group brings it
    down; Grubs now compete for loose Food and are fallback prey;
    Berries grow in patches around Dandelions; Pebbles are solid.
*   ✅ **Wandering Arrivals:** new solitary Bramblekin drift in from the
    edge while the population is under 40, so the world never ends.
*   ✅ **Kin Inspector & HUD:** tap a Bramblekin to see its Personality,
    needs, group role and relationships; the HUD tracks activity, deaths
    by cause, thefts and sharing; group tethers, Leader banners and
    social pop-ups ("+Ally", "Stolen!", "Shared") show the social web on
    the map.
*   ✅ **Headless mode:** `--headless [seconds] [--seed N]` runs the
    simulation with no window and prints population reports, for tuning
    and smoke-testing.

## Phase 8: Settling & Society
Each step was checked against the headless **survival trend** — deaths
and meat hunted per kin-hour lived as a Wanderer, Homesteader, group
Member or Independent, aggregated over 8 seeds × 30 simulated minutes.
*   ✅ **Code split:** `Program.cs` now holds only the entry point;
    everything else moved under `Source/`, one type per file, with
    `World` and `Bramblekin` as partial classes split by concern (one
    Bramblekin file per need). Verified byte-identical headless output
    before and after.
*   ✅ **Survival trend metric** in the headless summary.
*   ✅ **A — Settling:** Twigs (pooled, never rot) fall around the big
    Twig props. After looking around (10–50s, sooner for sharper minds)
    a loner moves into an abandoned shelter or builds a Tent (3 twigs):
    a store of 4 that never rots, healing while resting inside, and
    shelter from the Wolf Spider's pounce and Hornets. Abandoned shelters
    can be scavenged or moved into, and collapse after 90s. *Result:
    homesteaders died at roughly half the rate of wanderers.*
*   ✅ **B — Hunting & defending:** Stag Beetles (60 HP, bite back,
    8 meat) are pack work; settlers hunt Grubs near home while the store
    is low; starving, aggressive kin raid other stores, and residents
    defend them; a hungry kin with a predator about eats at home. *Result:
    members hunted ~25 meat per kin-hour vs 5–9 for loners.*
*   ✅ **C — Group homes:** a group adopts the best member home (or its
    Leader marks out a site), everyone moves in with their stores, and a
    group of 3+ upgrades its Tent into a House (room for 6, store of 12,
    faster healing); an overcrowded shelter protects nobody; a struggling
    loner may ask to join a settled group. Fixes found on the way: a
    fighter's nerve now breaks at 40% (at 30% a spider bite always killed
    first), the spider leaves company alone, and idle Hornet nests are
    avoided rather than fought. *Result: members outlived loners overall.*
*   ✅ **D — Leadership:** every 5s the Leader scores Defend / Settle /
    Hunt / Stockpile from the situation and its own personality, assigns
    Guard / Builder / Hunter / Gatherer jobs by fit (carried out in the new
    Duty need), and sets the sharing rule — unsociable, aggressive Leaders
    eat first. *Result: Warlike-led groups hunted ~24% of the time and ate
    leader-first ~37% of the time, vs ~0–4% and ~10% for other Leaders.*
*   ✅ **E — Loyalty & rebellion:** persistent Leaders elected by
    Intelligence + Reputation; per-follower loyalty that settles toward a
    Sociability-based baseline and moves with meals, hunger, denial,
    greed, danger and friendship; disobedience below 0.3; below 0.2 a
    follower challenges the Leader to a duel, splinters off with the other
    unhappy members, or walks out as an Independent; aggressive Leaders
    exile the disloyal. Nobody rejoins a group it left. *Result:
    independents died at ~1.9 per kin-hour vs ~1.2 for members; some were
    taken in elsewhere or survived alone with a home.*

## Phase 9: A Living Population
The population had stopped growing at about 30: arrivals stopped at a
hard cap of 40 and groups were capped at 6, so a busy garden simply
froze. Phase 9 replaced the caps with a population the garden's food
regulates. Checked with 8 seeds × 1 hour and 3 seeds × 4 hours headless.
*   ✅ **Seasons:** a 10-minute year of four 150s seasons; wild Berries
    grow at 1.0× / 1.3× / 0.8× / 0.3× (Spring → Winter), and the lawn and
    sky change colour with them. Leaders stockpile harder in Autumn
    (Intelligent ones most) and value big game more in Winter.
*   ✅ **Births & inheritance:** a thriving group (a House with 6+ Food,
    2 Food stored per member — 3 in Autumn, none in Winter — two healthy
    fed adults, few hungry, room to grow, 60s since the last) raises a
    young one for 3 Food. It inherits its parents' averaged traits ±0.15
    and is protected, fed first and kept out of work, fights and politics
    for 90s while it grows. Arrivals now only come below 30 alive; a
    safety cap of 150 is never reached.
*   ✅ **Villages & budding:** a group's size limit follows its housing
    (6 to 18); a crowded group upgrades a Tent, and a full, well-stocked
    one of Houses builds up to two more homes nearby. Members share all
    the village's stores and defend all its homes. At 10–16 members
    (larger under a Sociable Leader) the residents of one House bud off as
    a daughter group that keeps it. Merging groups keep the smaller one's
    nearby homes.
*   ✅ **Wintering in:** idle kin with a home huddle inside it in Winter,
    where Hunger rises at half rate.
*   ✅ **Female & male:** every Bramblekin is one or the other at even
    odds, and a birth takes a fit mother and father; otherwise no role
    difference yet. It cost about 45% of births over 4-hour runs (a group
    often has nobody of one sex fit to be a parent right then), with fewer
    villages and buddings and a population averaging ~37 instead of ~42.
*   ✅ **Survival stats:** the young are counted separately, and each
    status's death rate is broken down by cause.
*   *Result* (before sexes): over 4-hour runs the population swings with
    the seasons between about 35 and 55 (it used to sit at 30), 15–21
    villages are founded and 8–19 daughter groups bud off per run,
    generations reach 14–19, and after a few years nearly everyone alive
    was born here. With sexes: 8–13 villages, 3–6 buddings, generations
    6–15.
    Group members starve no more often than homesteaders; they die a
    little more often overall (1.1 vs 0.9 per kin-hour), from fights.
    A first cut that only limited births by housing let groups breed
    until winter starved them. Members then died at 1.6 per kin-hour, so
    births now need stored food per head, and none happen in Winter.

## Phase 10: Lives & Lineages
*   ✅ **Follow camera:** tapping a Bramblekin swoops the camera in and
    follows it, with its name (and its partner's) floating above it;
    pinch/twist/tilt still work, a pan stops following, a **Follow**
    button under the Kin Inspector toggles it, and a **Map** button flies
    back out to the whole garden.
*   ✅ **Names:** given names and garden-flavoured family names; newcomers
    found families, children take a parent's family name, groups are
    named for their founding Leader's family ("the Thornwood clan"), and
    the event log reads as a story instead of a list of #IDs.
*   ✅ **Couples:** single, grown, unrelated kin of opposite sex pair up
    on meeting (odds rising with Sociability), live together, raise the
    young (births now need a fit couple rather than any fit female and
    male), leave together when one walks out or is exiled — unless the
    other is loyal enough to stay, which splits them up — and are widowed
    by death.
*   ✅ **Old age:** lifespans of 4–6.5 years (a year is 600s), newcomers
    arriving 0.3–1.5 years old, a grey, slower, weaker but respected elder
    stage from 3.5 years, and death of old age as its own cause in the
    log, HUD and survival stats.
*   ✅ **Fixes found in a 9-year review:** hungry kin chased loose food
    past a full store until they starved (now they pick the nearer, and
    the sure meal at home once starving); kin at home sat unresisting
    while other kin killed them (home now only hides them from wildlife);
    dirt patches now take the season's tint.
*   *Result* (3 seeds × 4 hours): stable, no exceptions; population
    30–50; 150–210 births per run against 60–120 newcomers, so the world
    sustains itself; ~110 couples per run; generations up to 11–14; old
    age about a third of all deaths. Births now had to be tuned back to 1
    Food stored per member (2 in autumn) — with old age thinning the
    population, food per head was the main thing holding births back.

## Phase 11: Farming, Neighbours & a 13-Year Review
*   ✅ **Farming:** clever clans work out how to grow berry bushes from
    seed; the knowledge lives in individuals and spreads through
    families, splits, budding, marriage and allies. Farmers plant (a berry
    as seed) and harvest into the stores; bushes fruit at the season's
    pace, drop overripe berries, and run wild when their group is gone.
*   ✅ **Villages as neighbours:** per-pair stances (Neutral / Allied /
    At War) driven by fading grievances (killings, raids, robberies,
    splits, exiles) and Leader personalities. Allies defend each other,
    send food to an ally in need, teach farming and marry across; wars
    bring bold residents out against intruders and send raiding parties
    after enemy stores; peace comes as grudges fade or war-weariness sets
    in. Green/red lines on the map, counts on the HUD.
*   ✅ **Fixes from a 13-year review:** close relatives killed each other
    in feuds between their groups (now: no killing blow, robbery or raid
    against parent, child or sibling); winter famine made followers walk
    out and starve alone (now: shared hardship costs the Leader less
    loyalty, and a sharp rebel waits for spring); newly joined members
    churned straight back out (now: a 90s grace); neighbouring villages
    "defended" against each other in endless brawls (now: an outsider
    defending its own home isn't a threat, and allies only join when their
    ally is actually hit); raiding parties were recalled before they
    arrived (now: a party sees its raid through unless home is threatened).
*   *Result* (6 seeds × 13 years, against the previous build): population
    ~52 on average (was ~34), starvation about the same in number on a far
    bigger population, newcomers ~17 per run (was ~54) — the world now
    sustains itself — and 0 close-kin killings (was 14). Most worlds stay
    peaceful with a few lasting alliances; some fall into cycles of war
    with dozens of kin deaths. Stable over 24 years with 34–69 kin.

## Phase 12: The Wider Garden
*   ✅ **Rarer wars:** a grievance of 10 (not 6) before a Leader can
    declare war — 28 wars in 12 × 13-year runs instead of 47.
*   ✅ **Villages spread out:** budding settlers, splinters and departing
    couples found new villages on open ground 15–55m away (far from other
    homes, near berry patches), a budding party taking a dowry of food.
    *Result* (12 seeds × 13 years): kin-on-kin killings 551 → 159, wars
    28 → 1, starvation 639 → 494, average population 53 → 64, and twice
    as much food aid between allies.
*   ✅ **Aid in person:** food aid between allies is carried by a runner
    with a sack — visible on the map, robbable, and spilled if the runner
    dies on the way.
*   ✅ **Trade between allies:** a group with building under way hires a
    helper from a hard-up ally, paying in food carried home.
*   ✅ **War outcomes:** war scores (killings, food carried off); a
    clearly beaten side sues for peace and either is absorbed (if small)
    or pays a year of tribute, carried by runners.
*   ✅ **Weather:** droughts, harsh winters and bountiful seasons; storms
    that blow away loose berries and bring down twigs.
*   ✅ **Memory:** kin and their groups remember where danger struck
    (and keep away from there unless starving) and where food was found.
*   ✅ **Chronicle & History screen:** a dated story of every clan, with a
    population/groups chart; per-clan view for the selected Bramblekin.
*   ✅ **Save & load:** autosave every 30s and on exit, carried on at the
    next start; a New button (with confirmation) starts over; headless
    `--save`/`--load`.
*   ✅ **Clan culture:** Martial, Hunting and Farming traditions that grow
    from what a clan does, sway its Leaders, nudge its children, and pass
    to daughter clans.
*   *Result* (24 seeds × 13 years, no exceptions): the first cut of the
    weather was too harsh (harsh winters 30% of the time, 0.5× food,
    1.25× cold): population ~70 → ~58, starvation 52 → 68 a run,
    old-age deaths 78 → 60, and one winter in ten after a drought
    killing up to 65%. Ablations put it on the weather (off: 31 starved)
    and cleared danger memory (off: no better). Softened (20%, 0.7×,
    1.15×): population ~65, 54 starved, 69 old-age deaths, newcomers 18
    a run; a harsh winter costs 13% of the garden on average (7% for a
    fair one), and the worst 38%.

## Phase 13: Watching the Garden
*   ✅ **Fast-forward plays the same game:** the garden always advances in
    fixed 1/60s steps, whatever the speed. At a phone's 20x (0.046s
    steps) the old loop starved half as many again (69 vs 47 a run, 12
    seeds × 13 years); ablations put the difference in how Bramblekin and
    creatures act, not in the world's bookkeeping. A device that can't
    keep up says so ("running 9x"). 50x added.
*   ✅ **A tidier chronicle:** a clan's tradition name sticks (it used to
    flip up to 26 times in a run, now at most twice); spider hunts are
    recorded only as milestones.
*   ✅ **Stats tab:** the garden's totals and the selected clan at a
    glance, with a food-and-bushes chart.
*   ✅ **Clans on the map:** name tags over villages, clan-coloured
    territory, and a clan card on tapping a home.
*   ✅ **Big-moment banners:** wars, conquests, famines, new villages and
    more; tap to fly there; urgent ones slow a fast-forwarded game to 1x.
*   ✅ **Heroes and dynasties:** a record of every life ever lived, the
    hall of fame, and family trees reaching back to grandparents and
    forward to every descendant.
*   ✅ **Speed:** narrower spatial queries, an array-backed grid, and no
    allocation on the hot paths — about a quarter less CPU and a tenth of
    the garbage, with results identical step for step.
*   ✅ **New pressures:** crafts clans work out and teach (granary, spears,
    palisade); sickness that spreads by contact; a rival ant colony
    raiding stores; floods in spring and autumn downpours.
    *Result* (24 seeds × 13 years, no exceptions): population unchanged
    (65.5 vs 65.1), starvation 59 vs 54, predators 30 vs 31, kin
    killings 25 vs 30; sickness takes about 17 a run, mostly elders, so
    old-age deaths fall from 69 to 58.

## Phase 14: A Small World
*   ✅ **Acorn village:** homes redrawn at Bramblekin scale — an acorn cap
    on twig legs for a Tent, a whole acorn for a House, a hazelnut
    granary, a palisade of rose thorns.
*   ✅ **The pond:** permanent water in the lowest ground; no building or
    spawning in it, wading at half pace; floods rise from it.
*   ✅ **The Giant Oak:** a trunk rising out of sight at the back edge,
    with roots, shade, and acorns falling in autumn (about 26 a year).
*   *Result* (24 seeds × 13 years, no exceptions): average population
    57.5 vs 65.5 before (within the run-to-run spread), starvation 48 vs
    59, predators 26 vs 30, kin killings 31 vs 25, old age unchanged.

## Phase 15: Richer Natures
*   ✅ **Four new traits:** rebellious ↔ obedient, persuasive ↔ passive,
    brave ↔ cautious, diligent ↔ idle — inherited, drawn by clan
    culture, saved (rolled afresh for a garden saved before them), shown
    in words in the Kin Inspector and the chronicle, and each wired to
    behaviour: loyalty and rebellion; leadership, splits, alliances,
    peace, recruiting and teaching; fight-or-flight, nerve, danger memory
    and who hunts and guards; the pace of work and rest.
*   ✅ **Clan culture no longer maxes traits out:** a clan's pull on its
    children's traits now draws them toward 0.8 rather than adding a
    fixed push each generation (farming clans' Intelligence used to
    climb to ~0.96).
*   **Benchmark (24 seeds × 7800s, vs. the pond-and-oak build):**
    population 52.2 ± 3.1 vs 57.5 ± 3.4 (within the spread); deaths to
    predators up from 26.4 to 34.1 — brave Bramblekin stand and fight —
    and to other Bramblekin down from 31.3 to 25.2; starvation unchanged
    (48.5 vs 47.9); splits and challenges unchanged (165 vs 163). By the
    end the living lean diligent (0.73) and a little brave (0.59), with
    rebellion still at the middle (0.50). No crashes.

## Phase 16: Easier to Watch
*   ✅ **The log, your way:** a **Log** button under the event console
    steps it through brief (newest 3 entries, a line each — the default),
    off (with a count of what's been missed) and full; the choice is
    remembered in `settings.txt` beside the save.
*   ✅ **Bars that grow with the zoom:** health and hunger bars scale with
    how big the creature looks on screen (2.4× its width, 34–150px scaled
    to the screen), so they're readable up close and unchanged at the
    whole-map view.

## Phase 17: Water, Harvests, Stone and Branch
*   ✅ **Brave, not reckless:** however brave, a fighter breaks off while
    it can survive one more blow, and backs away on guard (safe from the
    spider's pounce). Benchmark vs Phase 15: deaths to predators 27.3 vs
    34.1 (back to where they were before the new traits), population 53.6
    vs 52.2.
*   ✅ **The whole map:** no draw distance; zooming out stops once the
    garden fits the screen; the mouse wheel zooms on desktop.
*   ✅ **Water you can't walk on:** nothing that walks enters the pond;
    walkers find their way round it (A* on a 1m grid).
*   ✅ **Food from many places:** grass seed, mushrooms, watercress and
    fish join the berries, acorns and meat, each with its own place and
    season.
*   ✅ **Four crops:** berry bushes, grain patches, mushroom beds and
    cress beds, each its own craft, season and lifespan; crops by the
    pond are watered.
*   ✅ **Fishing** from the shore.
*   ✅ **Stones and branches:** Builders carry stones home for stone
    footings (more store, dry in a flood, no ants) and drag branches home
    for palisades, which now have to be built.
*   **Benchmark (24 seeds × 7800s, no crashes):** the first cut grew the
    population from 53.6 to 76.0 — mushrooms, winter fishing and bigger
    stores carried many more through the winter — so winter mushrooms,
    winter fishing, the footing's store and the wild berries were
    trimmed. After that: population 51.2 ± 2.9 (vs 53.6), with a calmer
    garden — starvation 40.7 vs 60.8, kin killed by kin 15.6 vs 30.8,
    births 155 vs 202, predators 26.6 vs 27.3. The new foods are about a
    fifth of what's eaten (per run: berries 9,700, seed 1,100, meat 680,
    mushrooms 590, cress 560, fish 165, acorns 140). Each run lays about
    13 stone footings and 10 palisades and finds some 7,700 ways round
    the pond. Ants steal a little more (52 vs 35 a run) now that
    palisades take branches to build.

## Phase 18: Thirst
*   ✅ **Water to drink:** Thirst is a Critical need beside Hunger (it
    rises 0.4 a second; at 60 a Bramblekin walks to the nearest shore;
    at 100 it loses 1 HP every 2s), so living far from water costs time,
    and a drinker at the water's edge draws the Wolf Spider. Rain slakes
    thirst; juicy food (cress, fish, berries, mushrooms) takes a little off.
*   ✅ **Cisterns:** clans living more than 25m from water work out
    acorn-cup cisterns that fill in the rain and with cupfuls carried
    home from the pond.
*   ✅ **Settling by the water:** new homes and new villages lean toward
    water.
*   **Benchmark (24 seeds × 7800s, no crashes):** at first (thirst 0.3 a
    second, 8-sip cisterns, 2-sip cupfuls) distance showed only in who
    settled where, so thirst was made stronger. After that: population
    53.0 ± 2.9 (vs 51.2 before thirst); some 1,140 drinks at the pond and
    325 from cisterns a run, an 18m walk on average; almost nobody dies
    of thirst (0.3 a run) — they walk instead — but lives are shorter:
    deaths of old age 45.7 vs 52.8, to predators 33.2 vs 26.6, and kin
    killed by kin 30.6 vs 15.6 as crowds meet at the water. Clans near
    water are bigger: 7.5 kin within 10m of it, 6.4 at 10–20m, 5.7 at
    20–30m; only 8 clans in 24 runs ever lived more than 45m out.

## Phase 19: Roots and a Drying Pond
*   ✅ **The oak's roots:** ten great roots, up to 2m thick, arching 7–13m
    out from the trunk; trunk and roots are solid, so walkers go round
    the root tips (obstacles, bucketed by cell so it stays cheap, and in
    the route grid), and nothing stands on them.
*   ✅ **Droughts drink the pond down** to about a sixth of its size,
    laying bare a muddy bed; drinkers and fishers follow the water out,
    fish get scarcer, and the pond fills again after (faster in rain).
*   ✅ **New clans without a home yet** say so on their clan card, and
    their site shows on the map (bare earth, a clan flag, the twigs laid).
*   **Benchmark (24 seeds × 7800s, no crashes):** the pond all but dries
    up in almost every drought (3.7 times a run, of 4 droughts), and
    droughts now bite: starvation 64.5 vs 45.8, deaths of old age 40.0 vs
    45.7, deaths of thirst 0.8 vs 0.3 a run, fish caught 199 vs 247 —
    while population holds at 52.3 vs 53.0 (more newcomers wander in to
    fill the gaps: 33.5 vs 26.3). Walkers find some 24,000 ways round
    the pond and the roots a run (was 12,000), still well under a second.

## Phase 20: Wells
*   ✅ **Wells:** clans with stonework whose main home is more than 20m
    from water dig a well beside it, lined with stones their Builders
    carry in (3, plus one per 2m the ground stands above the pond) —
    water at the door, all year and through droughts, for the clan, its
    allies, or anyone once the clan is gone; crops beside it are watered.
*   **Benchmark (24 seeds × 7800s, no crashes):** 2.6 wells dug a run
    (and 1.8 still being dug at the end — stones are scarce, and a
    hilltop well takes ten), some 105 drinks drawn from them (vs ~990 at
    the pond and ~370 from cisterns). Population 47.0 ± 3.0 vs 52.3 ± 2.7
    (within the spread; the stone-fetching Builder is one fewer
    Gatherer); clans 30–45m from water average 6.1 kin, 45m+ 6.2 (was
    5.7).
*   ✅ **Fixed: walkers stopping a pace short.** A route to a target in
    blocked ground (by a root, a rock or the shore) ended a meter off,
    and the walker waited there for good — Builders sat for years beside
    a stone. Routes now always end at the target. With that, clans of two
    keeping a Builder on their well, a 60m stone search and cheaper wells
    (3 stones plus one per 2m of height): 3.0 wells dug a run, 126 drinks
    from them; population 54.8 ± 4.1 (vs 47.0 before the fix, 52.3 before
    wells); no crashes.

## Phase 21: Slings, the Pond's Wildlife, Seed Corn, Trade and More Gardens
*   ✅ **Slings** (a craft, after spears): pebbles loosed from 3.5m at a
    Hornet, a frog on the bank or the Heron; a slinger is likelier to
    stand and fight a chasing swarm or the Heron.
*   ✅ **Frogs and the Heron:** frogs sit and hop along the bank from
    spring to autumn (fewer in a drought), diving when startled — small
    game worth 2 meat. A heron comes down to the pond every 4–8 minutes,
    stands stock still (hard to see) or wades, spears frogs and lunges at
    anyone at the water's edge; driven off when hurt, a feast if brought
    down.
*   ✅ **Seed corn:** grain is sown from seed kept back from the harvest
    (and wild seed), not from the stores; a poor year means fewer patches
    the next, and the starving eat the seed corn last of all.
*   ✅ **Trade in stones and branches:** a clan needing stones or branches
    with none close by buys one from an ally that has them lying near
    home — a hauler carries it over and takes 2 food home. Clans by the
    rocks come to supply stone; by the oak, wood.
*   ✅ **Three gardens:** a Garden button beside New on the History screen
    keeps the open garden and opens the next of three (fresh if empty).
*   **Benchmark (48 seeds × 7800s against the Phase 20 build on the same
    seeds, no crashes):** population 54.9 ± 2.8 vs 54.2 ± 2.7 — no change
    (24 seeds weren't enough to tell: this build's first 24 averaged 46.1,
    its next 24 63.8). Starvation 49.4 vs 56.9 a run and kin killed by kin
    19.4 vs 31.6, with deaths of old age up to 48.8 from 43.8; deaths to
    predators 32.4 vs 29.6 — the Heron spears 1.9 a run, and the Wolf
    Spider takes 14.3 (was 12.4) as frog hunters busy themselves near the
    water, while hornets sting 16.3 to death (was 17.3). A run sees 129
    pebbles loosed (91 hits, 60 kills; hornets swatted 47, was 33), 67
    frogs caught, 13 heron visits (27 lunges; driven off 8 times, brought
    down once), 200 seed corn kept and 93 sown (60 eaten in famine), and
    12 stones and branches hauled between allies. The first sling build
    stood slingers off at point-blank range and never went for nests —
    hornet deaths didn't move until slingers struck close up and Guards
    cleared nests near home; and until the Heron took a moment to get
    airborne, nobody could bring it down.

## Phase 22: Reputation Between Individuals, and a Balance Check
*   ✅ **Infamy:** a Bramblekin that robs someone starving, deals another
    Bramblekin its killing blow, or carries off a piece of an enemy's
    store on a war raid earns Infamy (fading slowly on its own, like a
    grievance). At 1.2 or more it's **notorious**: word travels ahead of
    it, so even a stranger who's never met it is warier — with odds 0.7
    it shies away from banding together with a notorious Bramblekin,
    taking one in as a struggling loner, or warming into a first
    friendship, exactly as if it had heard the stories. It never stands
    in the way of two strangers banding together to survive a predator
    right now — that's a matter of life or death, not reputation. Unlike
    Reputation (standing earned *within* a Bramblekin's own group, toward
    its claim to lead), Infamy is what the wider garden thinks of it. The
    Kin Inspector shows it once it's earned any ("Infamy: 0.6", or
    "Notorious!" past the threshold).
*   ✅ **Group vs. homestead balance, re-measured:** the Phase 8-era
    finding that groups and homesteaders die at similar rates no longer
    holds — the wells, cisterns, defensive bonuses, food-sharing and
    crafts added in later phases have shifted it. No code change was
    needed; this closes out that backlog item with the current numbers.
    *Result* (10 seeds × 7800s, survival trend): Members die at 1.79 per
    kin-hour against 2.32 for Homesteaders — groups are already about a
    quarter safer, consistently across 9 of 10 seeds — while Independents
    (3.42/h) and Wanderers (3.28/h) confirm settling and grouping still
    pay off in that order. Final population averaged 39.1 across the
    seeds, in the normal range; no crashes. The Infamy change above ran
    across the same seeds with no sign of destabilizing alliance-forming
    or population.

## Phase 23: Fixes from Play on a Phone
*   ✅ **The pond vanished after a saved garden was loaded:** the save
    writes every numeric World field by reflection, which swept up two
    render caches (the water level the pond's squares were worked out
    for, and the drought mud's). A loaded garden restored the markers but
    not the squares, so the pond was never drawn again — the water was
    there, kin walked round it and drank from it, but it was invisible
    from the second session on (only a New garden showed it). Render
    caches are now marked `[NotSaved]` and the save skips them, which also
    mends gardens already saved.
*   ✅ **The Wolf Spider got stuck at the map's edge:** walkers turn back
    once 48m from the centre, but destinations were picked up to 49m out,
    so about one prowl target in 25 could never be reached — the spider
    paced on the spot at the limit for good (45 minutes in one place in a
    13-year run). Goals past the limit are now clamped to it, for every
    walker.

*   ✅ **The speed kept dropping to 1x:** urgent headlines (a famine — 4
    starved in a season, most winters — a drought drying the pond, a war)
    reset a fast-forwarded game to 1x so they could be watched, at most
    every 90s; at 50x that was every game-year or so, and read as the
    setting resetting itself. Banners no longer touch the speed; urgent
    ones still stay up longer, in red.

## Phase 24: Hearth and Home, Kin and Clan
*   ✅ **Hearths:** a craft (a House, worked out in autumn or winter): a
    fire out front of each House, kept fed with twigs; food from its
    store is cooked (a little more filling and healing), and wintering
    beside it keeps folk warmer.
*   ✅ **Snares:** a craft (a House): baited snares round each House catch
    Grubs without a hunt; Gatherers set sprung ones again.
*   ✅ **Adoption:** an orphaned child is taken in by a couple in its clan
    (a grown sibling first), who are close kin from then on; a lone young
    one is taken in by a settled clan it meets.
*   ✅ **Courtship gifts:** a fed suitor with food in hand offers it — a
    friend at least, and better odds of a partner (more from the
    persuasive).
*   ✅ **Skill mastery:** hunting, farming, building, fishing and healing
    grow with practice (and rust slowly); skill makes work faster and
    blows harder, Leaders give jobs to whoever's best, children start with
    a quarter of their handier parent's skill, and masters make the
    chronicle.
*   ✅ **Healers:** herb-lore (farming, a House, and someone sick): a
    Healer tends the sick and wounded near home.
*   ✅ **Named succession:** an elder, sick or badly hurt Leader names an
    heir, leaning toward its kin — dynasties — who takes over smoothly; a
    Leader lost with none leaves a scramble that costs everyone loyalty.
*   ✅ **The council:** a Leader's three most respected members temper its
    goals, sharing rule, and war and peace (about 16% of the way).
*   ✅ **Quiet plotting:** a would-be splinter plots first, recruiting the
    unhappy over several decisions, and may be found out — exiled by a
    harsh Leader, talked round by a mild one.
*   ✅ **Burrows:** a loner on a hillside may dig in — cheap, warm in
    winter, its store hidden from raiders and ants, but only room for two
    and no way to build it up.
*   ✅ **The creek:** a spring in the driest corner feeds a brook running
    ~28m downhill into a pool; it never dries, blocks no one, and counts
    as water for drinking, cress, settling and watered crops. About a
    third of all trips for water end there.
*   **Tuning:** with every new food in, the population ran two-thirds
    higher and kin killings nearly tripled from the crowding, so cooked
    meals were cut from +12 to +5 of a meal's 40 (+2 health, was +3),
    wintering by a hearth to 0.42 of the usual hunger (was 0.35), and a
    skilled farmer's bonus piece to at most 20% of pickings (was 35%).
*   **Benchmark (12 seeds × 7800s against the Phase 22 build on the same
    seeds, no crashes):** population 50.8 time-averaged vs 40.7; births
    198 vs 149 a run; starvation 45.0 vs 46.2; deaths to predators 33.8 vs
    34.6; kin killed by kin 25.7 vs 19.8; old age 56.6 vs 44.0 — more live
    out their years; sickness 18.0 vs 18.0. Members die at 1.61 per
    kin-hour vs 1.79, Homesteaders at 2.06 vs 2.31. Before tuning:
    population 56.6, kin killings 34.2.

## Phase 25: Fast-Forward You Can Watch
At 50x a phone (debug APK) managed only 8–11x with 70 kin. Three causes,
three fixes:
*   ✅ **Android builds are optimized even in Debug:** the branch APKs CI
    builds are Debug, and an unoptimized build runs the simulation 2.7x
    slower (42x vs 115x real time on the same busy garden, desktop).
*   ✅ **Simulation profiling (perf):** a cached grounded position per
    walker, a per-group index of ripe crops, cached group lookups, the
    threats near a home gathered once per decision round instead of per
    group, and plain loops in the hot crop queries. The busy garden runs
    ~140–150x real time on desktop, up from ~106x, with byte-identical
    results on fixed seeds.
*   ✅ **Level of detail in drawing:** raylib builds every sphere's
    vertices on the CPU each frame (16×16 segments by default). Spheres
    now take segments by their size on screen (`Detail`), and a home only
    a few pixels across skips its cap scales, footing stones, thorn tips
    and hearth pebbles. Drawing the whole busy garden fell from 52–66ms a
    frame to ~18ms (software GL); close up, everything looks as before.

## Phase 26: Day and Night
*   ✅ **Day and night:** a 75s day (two to a season) with dusk, night
    and dawn; nights are longer in winter. The sky deepens, the garden
    darkens, and hearths, windows, fireflies and the Owl's eyes shine.
*   ✅ **Sleep:** kin sleep at home (or in the open without one), at half
    the hunger and thirst, and barely notice anything asleep.
*   ✅ **The night watch:** settled clans post a watch who raises the
    alarm and wakes everyone; raids are likelier after dark.
*   ✅ **The Owl:** a night hunter from the oak that drops on kin in the
    open — sleepers first — but never near a lit hearth; it can be fought
    on the ground and driven off or brought down.
*   **Benchmark (12 seeds × 7800s against Phase 25):** population 43.0
    time-averaged vs 50.8; births 147 vs 198 a run (a quarter less daylight
    to gather in); starvation 28.8 vs 45.0; kin killed by kin 8.0 vs 25.7 —
    far fewer robberies with everyone abed; predators 33.2 vs 33.8; old
    age 53.2 vs 56.6. The Owl comes out ~40 nights a run and strikes ~37
    times, killing 1–3; it's driven off ~20 times.

## Phase 27: The Director
*   ✅ **An automatic camera ("Auto"):** cuts between whatever is most
    worth watching — duels, fights, raids, the spider pouncing, the owl
    striking, headlines, births, beetle hunts, else the liveliest village
    — for a few seconds each, captioned; any pan or tap hands the camera
    back. Watching only: the simulation is untouched (results identical
    on fixed seeds).

## Phase 28: Aphid Herding
*   ✅ **Herding** (a craft): a pen of aphids by the main home gives
    honeydew — steady, slow-to-spoil food — and the herd breeds up
    outside winter. Ants carry aphids off, the Wolf Spider picks them
    off, and war raiders rustle them.
*   **Tuning:** first cut, a full pen gave a drop every 10s and the
    population ran to 58.6; honeydew now comes every 150s per aphid
    (was 60), breeding every 240s (was 150), five to a pen (was six), and
    two in five ants go for pens first.
*   **Benchmark (12 seeds × 7800s against Phase 26):** population 52.9
    vs 43.0 — herding gives back what shorter working days took; births
    196 vs 147; starvation 34.4 vs 28.8; predators 30.7 vs 33.2; kin
    killed by kin 23.8 vs 8.0 (back to the crowded Phase 25 level);
    old age 61.2 vs 53.2. Clans fence ~20 pens a run and collect ~600
    drops of honeydew; ants take ~12 aphids, the spider ~1.

## Phase 29: The Beehive in the Oak
*   ✅ **Honey:** a hive on the oak fills with combs spring to autumn; bold
    kin climb for them, and Leaders in reach send their boldest. A comb
    is the richest food (fills 20 more, stores as two, never spoils) and
    the best courtship gift (twice the sway).
*   ✅ **Bees:** more than half the time the bees rouse and chase the taker,
    stinging, until it gets indoors or swats them.
*   ✅ **Smoking** (a craft, after a hearth and a first comb): the bees
    seldom rouse.
*   **Tuning:** at first only idle kin went, and a run saw ~8 combs; now
    Leaders send their boldest (15% a decision), and a run sees ~85, with
    ~30 swarms roused and ~70 stings.
*   **Benchmark (24 seeds × 7800s against Phase 28 on the same seeds):**
    population 59.9 vs 56.4 time-averaged (the difference is within the
    spread between seeds); starvation 37.7 vs 31.4; predators 34.2 vs
    31.8; kin killed by kin 22.9 vs 24.6; old age 67.8 vs 63.5.

## Phase 30: Harvest Feasts
*   ✅ **Feasts:** a clan with a good autumn harvest holds a feast by its
    home and invites its allies and friendly neighbours; guests are fed
    from its stores, kin from different clans court there, and grudges
    cool — a neutral clan that came in numbers may become an ally.
*   **Tuning:** first cut, clans needed 2 stored a member and fed any
    guest with a little hunger; starvation rose by 4 a run, so now it
    takes 3 a member and only the properly peckish are fed.
*   **Benchmark (24 seeds × 7800s against Phase 29):** ~21 feasts a run
    with ~280 guests from other clans, ~20 couples meeting across clans
    and ~6 alliances made over one. Population 57.0 vs 59.9; starvation
    38.9 vs 37.7; kin killed by kin 21.4 vs 22.9; predators 36.6 vs 34.2;
    old age 64.2 vs 67.8.

## Phase 31: Shrines and Beliefs
*   ✅ **Beliefs:** clans come to revere the Oak, the Pond, the Spider or
    the Moon from what they've lived through, and raise a shrine to it
    that binds them closer (steadier loyalty).
*   ✅ **Between clans:** shared beliefs soothe grievances and help
    alliances; rival ones slowly sour neighbours; a feast can convert.
*   ✅ **Prophets:** a persuasive rebel with a vision leads a schism.
*   **Fixed on the way:** a schism split a clan while the Leaders' round
    was still going through the clans (a crash); visions are now acted on
    once every clan has decided, like plots.
*   **Tuning:** at first a prophet needed followers already set to leave
    and a run saw 0.1 schisms; with the restless following, 0.008 a
    decision gave 7 a run — now 0.0015 gives about 1.7.
*   **Benchmark (24 seeds × 7800s against Phase 30):** ~7.5 beliefs taken
    up a run, ~27 shrines raised (daughter villages raise their own),
    1.7 schisms. Departures 6.7 vs 7.4 and coups 1.2 vs 1.4 (shrines
    steady loyalty); splinters 4.0 vs 2.6 (schisms among them); wars 0.4
    vs 0.3 a run. Population 51.5 vs 57.0; starvation 35.0 vs 38.9;
    kin killed by kin 20.8 vs 21.4.

## Phase 32: Armour and Champions
*   ✅ **Shields** (a craft, after spears and a hunting or martial
    tradition): beetle-shell shields take a third off every blow and bite.
*   ✅ **Champions:** feuding neighbours — and clans at war — may settle it
    by single combat between their best fighters; the loser pays a
    forfeit (or tribute, ending a war), the grievance is forgotten, and
    the winner goes in the chronicle.
*   **Benchmark (24 seeds × 7800s against Phase 31):** ~2 contests a run,
    settling ~1.8 feuds and ~0.2 wars; ~10 clans carry shields by the
    end. Population 51.1 vs 51.5; predators 30.9 vs 33.9 (shields);
    starvation 31.3 vs 35.0; kin killed by kin 23.9 vs 20.8; wars 0.4 a
    run as before.

## Phase 33: The Garden Timeline
*   ✅ **The History chart as a timeline:** every clan's size is kept with
    each 30s snapshot; the Story chart draws the six biggest clans' lines
    in their colours (or the selected clan's alone), year marks, and every
    headline as a coloured diamond along the top — tap one to jump the
    chronicle to it. Older saves load as before (their clan lines start
    from the load).

## Phases 26–33 Together
*   **Benchmark (24 seeds × 7800s, the Phase 33 build against Phase 25 on
    the same seeds):** population 51.1 time-averaged vs 50.4 — about the
    same, but a healthier garden: starvation 31.3 a run vs 45.0; kin
    killed by kin 23.9 vs 27.7; predators 30.9 vs 34.9; old age 59.6 vs
    56.3; sickness 16.3 vs 16.8; births 195 vs 198. No crashes.
*   **Speed:** the busy garden (112 kin) keeps up at 50x on desktop with
    software rendering (37 FPS), with the Director on.

## Phase 34: A Modelled World
*   ✅ **The terrain is a 3D model** (`Assets/Models/Terrain/terrain.glb`,
    made by `Tools/convert_terrain.py`): a grassy 100m square with a hollow
    dead oak and its roots, two ponds and dirt patches. Ground height is a
    grid sampled from the model and embedded as C# (`TerrainData.cs`), so
    headless and Android builds need no model to walk on. Both ponds share
    one water level (the higher pond's basin was lowered); the oak's trunk,
    roots and the reed clumps and boulders on the banks are walker
    obstacles; hive, owl roost and branch fall follow the trunk. Season
    tint kept.
*   ✅ **Custom models** (Tripo, shrunk by `Tools/convert_*.py`): the male
    and female Bramblekin (the female rigged to the male skeleton, so one
    set of clips drives both), the acorn house, the berry bush, the Wolf
    Spider, and the village asset sheet — tent, burrow, granary, stone
    footing, palisade, hearth, cistern, well, aphid pen, building site,
    shrine, grain/mushroom/cress plots, and kin's shield, food sack,
    fishing rod, poultice and water cup. Kin, buildings, spider and crops
    were scaled up to read better; a kin's lean on slopes is capped.
*   ⬜ **Still procedural (no custom model yet):** ant, bee swarm, frog,
    grub, heron, hornet, owl, stag beetle, aphids, caught fish; the
    beehive, snare, feast lanterns; loose food (berries, acorns, seeds,
    meat, fish, honey, honeydew), loose and carried twigs, stones and
    branches, garden props, sling pebbles; the sleep and sickness markers;
    the construction stages of footings and palisades. The carried-twigs
    bundle is in the asset sheet but not wired in. Water, rain, fireflies
    and night lights stay procedural on purpose.
*   ✅ **Levels of detail.** Kin have three meshes per sex (about 50,000,
    9,000 and 2,500 triangles, `Tools/convert_kin_lod.py` — decimated as
    plain geometry with the nearest full-detail vertex's skin weights, so
    they stay in step with the skeleton; the old `Walking_lod.glb` was
    removed), the village models three each (full, a quarter, a
    fourteenth), and the house, bush and spider a cheap copy; each is chosen
    by how big the thing looks on screen, and far-off kin are still pegs.
    The terrain is 60,000 triangles (was 150,000), and kin off screen or
    behind the camera are neither animated nor drawn.
*   ✅ **Wells now get dug.** They almost never were: a clan needed to live
    over 20m from water (two ponds and a creek leave few homes that far)
    and dug five stones deep for a trickle of stones. The reach is now 10m
    and a well takes 2 + ground height ÷ 3 stones (seed 13, 5,000s: 9
    dug, 248 drinks from them; seeds with no far-off clan still dig none).
*   ✅ **Scale and slopes.** Kin and buildings drawn at 80% of their
    earlier size (the terrain looked small beside them); the ground's hills
    and hollows flattened to 65% by `convert_terrain.py` (`RELIEF`) while
    the oak, roots, reeds and stones keep their height; drought drop and
    flood rise scaled to match.

## Phase 36: Cleaner Map, Easier Taps
*   ✅ **Crops keep clear of homes:** a crop is planted outside a home's
    palisade ring (3.8-7.5m out), and crops left inside a home's yard after a
    tent grows into a house are ploughed under.
*   ✅ **Map guide toggles:** Clans (clan range), Links (follower tethers and
    ally/war lines) and Range (the selected kin's detection ring) buttons
    down the left edge, each on its own and remembered between runs.
*   ✅ **Easier stat taps:** tapping a clan's name tag opens its clan card;
    taps on homes and kin forgive more the farther the camera is zoomed out.

## Phase 37: More Gardens (Terrains)
*   ✅ **Four terrains:** the original and three more (from
    `terrain2/3/4.glb`), each with its own ponds (one or two), Giant Oak,
    reed clumps and boulders, and creek. A new garden picks one at random;
    a saved garden keeps its own (saved as "terrain"; older saves stay on the
    original). Each garden slot can be on a different terrain.
*   ✅ **Flatter ground:** the new terrains keep 40% of their hills and
    hollows (the original keeps 65%); rocks and plants keep their height.
*   ✅ **Tools:** `Tools/detect_terrain.py` finds a model's ponds, oak
    roots, rocks and plants (with overlays under `Tools/terrain_features/`);
    `Tools/convert_terrain.py --features ... --relief ... --index N` turns a
    model into `Assets/Models/Terrain/*.glb` and `Terrains/TerrainN.cs`
    (ground heights, one shared water level, oak and hive, footprint circles,
    and where the creek rises, picked so the brook runs downhill).
*   ✅ **Ground fills the whole square:** the new models' slabs fall short
    of the 100 m square along their ragged rims, so kin could stand on
    ground that wasn't drawn; the converter now extends the ground to the
    full square (same height as the nearest ground, textured from just
    inside the rim).
*   ✅ **New gardens use the original terrain only for now** (`TerrainData.NewGardenTerrains`); saved gardens keep theirs.
*   ✅ **New gardens use the original terrain only for now**
    (`TerrainData.NewGardenTerrains`); a saved garden keeps its own.
*   ⬜ **Not yet:** a terrain picker (New garden always uses the original for now), the
    ragged raised corner of terrain2's model, terrain-specific tuning of
    where clans start.

## Phase 38: Procedural Terrain (plan, not started)
Goal: a new garden gets its own ground from a seed — ponds, oak, rocks and
plants in different places, a chosen size, and a mesh with far fewer
triangles. The four baked terrains stay as they are.
*   ✅ **Stage A (offline, game unchanged):** `Tools/procedural/`:
    `extract_tiles.py` cuts seamless grass (4) and dirt (3) tiles out of the
    models' textures (no clean sand patch turned up; dirt stands in);
    `extract_props.py` cuts each model's oak, boulders and plant clumps out
    as a prop kit (`kit/`, 3 oaks, 11 rocks, 15 plants, with footprint
    circles); `generate_terrain.py --seeds 1-12` makes seeded gardens
    (rolling ground with ~4 degrees mean slope, 1-3 ponds at one water
    level, an oak on a levelled patch, boulders and plant rings on the banks,
    a creek spring) and draws them with the real tiles
    (`preview_seeds.png`). Not yet: sand tile, more kit variety (terrain2
    has no ferns), tile repeat still visible up close.
*   ✅ **Stage B:** make the map size a setting (`TerrainData.Half`) in the
    ~15 places that assume ±50 m (height lookup, obstacle grid, water map,
    known map, flood scan, map-edge drawing, walkers' limits).
*   ✅ **Stage C:** `TerrainGenerator` (C#, its own seeded `SeededRandom`, so
    a seed always regrows the same garden) makes a terrain number 1,000,001
    and up from its seed: ground, 1-3 ponds, the oak and its hive, rocks and
    plants from the prop kit (`ProceduralKit`, 21 prop models in
    `Assets/Models/Procedural/props`, each with its own cropped texture), the
    creek's spring. It fills the same `TerrainSet` the baked terrains use, so
    water, routes, building and the rest are unchanged. `ProceduralView`
    draws it: the ground mesh simplified by `TerrainMesh` (right-triangulated
    network, within 4 cm of the height grid, cracks-free), one 2048 px texture
    baked at start from the models' own grass and dirt tiles
    (`Assets/Textures/Ground`), and the props as models. A **Fixed/Random**
    button beside Garden on the History screen picks what a new garden gets
    (Fixed is the default: the original terrain); the terrain number is saved
    with the garden. `--check-terrains N` grows and checks N terrains;
    `GARDEN_TERRAIN=n` forces one for a new garden or headless run;
    `GARDEN_SCREENSHOT`/`GARDEN_CAMERA` take a picture without a person
    (`DebugShot`, works under Xvfb). Not yet: sand tile, more kit variety,
    a smaller kit (14 MB), the extra pass that pins props to steep ground.
*   ✅ **Start menu:** the game opens on a menu: pick one of the three kept
    gardens (year, Bramblekin and clans, terrain kind, when saved), then
    Resume it, or start a new garden on the original terrain or a random
    grown one (over a kept garden it asks for a second tap). The ground
    texture of a grown terrain is baked on another thread (the ground shows
    plain green for a moment), which stopped Android's "isn't responding"
    prompt at garden start. The oak's dark shade circle is gone.
*   ✅ **Stage D:** bigger gardens. The start menu picks Small 100 m, Medium 150 m or Large 200 m; ponds, Bramblekin, food, hornets and grubs scale with the map area, and a progress bar shows while the ground is grown.
*   ✅ **Distance detail:** Bramblekin status bars are hidden when they would be tiny, and far Bramblekin use simpler pegs.
    and camera limits with the area; check speed on a phone.

## Phases 35+: Advancing Civilizations (ideas, not scheduled)
Ideas for the clans to grow past today's crafts, farming, herding,
fishing, wells, palisades, shrines, feasts, alliances and wars. Suggested
first picks: roads, workshops with tools, and the tech tree with eras.
*   🟡 **Tech tree with eras:** Stone Age, Farming Age, Village Age,
    Kingdom Age. Done: a clan's age is worked out from its crafts (farming
    → Farming Age; stonework and 9 crafts → Village Age; 15 crafts
    including Roads and Markets → Kingdom Age), announced as a headline
    and chronicle entry, shown on the clan card, and gates Tools (Farming
    Age), Roads and Markets (Village Age). Seeds now end in different ages
    (Farming only, Village, Kingdom). Not yet: more crafts and buildings per
    age.
*   🟡 **Roads and bridges:** Done: feet wear dirt paths into the ground
    (a little faster to walk), and once a clan knows Roads the hardest-worn
    cells are paved for good (faster still; stonecutting lets paving start
    at 60% of the wear); drawn over the terrain, saved with the garden.
    Not yet: clans deliberately laying roads between homes, bridges over
    the creek or a pond neck, a model for the paving.
*   🟡 **Workshops and specialists:** Tools puts a workbench by each House
    (clan works 25% faster); Weaving makes cloth and Stonecutting cut
    stone (each needs Tools, the second Stonework), a good made every
    25-35s per clan while it has a home and adults, wearing out slowly.
    Not yet: individual specialists with their own jobs, goods with uses
    beyond trade, a workbench/loom model.
*   🟡 **Markets and currency:** Done (first slice): Markets (Village Age,
    Roads and a trade good) puts a stall by the main home; allied clans
    that both hold one swap two goods for 5 food from the buyer's stores,
    at most every 30s, reported in the headless summary. Goods are saved
    with the garden. Not yet: coins, caravans that walk between markets
    and can be raided, prices that follow scarcity.
*   🟡 **Writing and history:** Done (first slice): Writing (Village Age and
    Stonecutting) puts a runed standing stone by the main home; from then on
    the clan's discoveries and new ages are carved into the chronicle, and it
    teaches allies 1.5x as readily. Not yet: runes that improve teaching
    beyond crafts, lore that outlives a clan, readable stone text in-game.
*   🟡 **Kingdoms and vassals:** Done (first slice): a clan that loses a war
    and agrees to tribute is its conqueror's vassal while it pays; a Kingdom
    Age clan can also win a small allied neighbour's fealty (tribute each
    season, no end); vassals are taught the liege's crafts 1.5x as readily.
    A clan with vassals is a kingdom's capital (shown on the clan card and
    Stats tab); gold lines join liege and vassal homes (Links toggle). A
    vassal that grows as big as its liege breaks free, and one whose term
    is served is released. Not yet: a leader title, liege protection of
    vassals in war, a capital model, territory taken in conquest.
*   🟡 **Defense and siege:** Done (first slice): Watchtowers (Village Age,
    Palisade and Spears) raise a lookout with an alarm horn by the main home;
    it spots the Wolf Spider, chasing Hornets or a warring clan's fighters
    within 30m, sounds the horn (waking and warning the clan) and kin near
    home see 6m farther. Not yet: gates in the palisade, siege tools (sling
    catapults, battering logs), organized traps for the spider and owl.
*   🟡 **Seasonal calendar and festivals:** Done (first slice): Calendar
    (needs Writing and farming) puts a sundial by the main home; the clan
    keeps midsummer and midwinter with a solstice festival — loyalty rises,
    grudges with neighbours at peace within 40m ease, and a neutral
    neighbour may become an ally. Not yet: planting and hunting plans that
    follow the calendar, a calendar-stone model, festivals with games.
*   🟡 **Medicine and disease:** Done (first slice): Medicine (herb-lore,
    a House and the Village Age) grows a herb garden by the main home; the
    clan's sick recover 1.5x as fast and pass it on only about a third as
    often (quarantine). Sickness can still travel: when allies trade goods
    and the seller has sick folk it may reach the buyer (a third as likely if
    the buyer knows Medicine). Not yet: healer huts, quarantined homes the
    sick are moved to, wider epidemics along caravans, a herb-garden yield.
*   🟡 **Exploration and expeditions:** Done (first slice, scouts and the
    known map): every clan keeps a 20x20 grid of the garden it has seen
    (5m cells, marked round its homes and wherever its people walk). The
    Exploration craft (Farming Age and a House) puts one bold, clever
    Gatherer of a clan of four or more out as a Scout, walking to the
    nearest unseen ground within 45m of home and mapping 16m round it; the
    clan card shows how much of the garden it knows, mapping milestones
    (a quarter, half, three quarters) reach the chronicle, and pioneers
    prefer settling on ground their clan knows. A Fog toggle greys out what
    the selected clan hasn't seen. The map isn't saved (a loaded garden
    re-marks round homes and people). Also done: the pond's far side is a
    place to discover — the bank farthest from a clan's main home (at least
    20m from all its homes) gives a cache of food, more for the first clan
    ever, with a headline and chronicle entry; scouts head for it half the
    time while unseen, and clans that know it count ground within 14m of it
    as good for a new village. Rafts: Fishing, Tools and Exploration let a
    clan lash logs into rafts; a scout whose target lies across the pond,
    where walking round is 1.6x the straight way or more (and the banks
    are within 40m), walks to the bank, poles straight across at 1.3 m/s
    (a small chance per second of capsizing back to the launch) and
    carries on. Not yet: rafts for settling parties, moored rafts on the
    bank, other finds beyond the far shore.
*   ⬜ **Religion and culture branches:** beliefs (Oak, Pond, Spider, Moon)
    gain temples, priests, holy days and schisms; culture traits (warlike,
    farming, scholarly) shape each clan's tech choices.
*   ⬜ **Ecology and domestication:** herd beetles or aphids beyond the
    current pens, plant orchards, manage overfishing; wild food responds to
    how clans treat the land.

## What's Left / Not Yet Scheduled
These are real gaps in the current build, in roughly the order they'd
matter most:
*   ⬜ **Real pathfinding round rocks and homes.** Walkers find their way
    round the pond on a grid (Phase 17), but still steer round rocks,
    homes and the oak with a short sideways detour when stuck. Fine at
    current density.
*   ⬜ **A bigger garden.** The terrain takes its size as a parameter, but
    a dozen places (water and route grids, the obstacle grid, flood
    heights, the edge limit, drawing, the camera) assume ±50m, and spawn
    amounts would need to scale with area.
*   ⬜ **Tuning.** Every rate and threshold is a constant at the top of its
    class (`World`, `Bramblekin`, `Shelter`, the wildlife); the headless
    survival trend is the tool for revisiting them.

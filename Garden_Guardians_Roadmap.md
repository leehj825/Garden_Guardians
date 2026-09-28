# Garden_Guardians_Roadmap.md

**Status key:** ✅ Done · 🟡 In progress (partly done) · ⬜ Not started · ❌ Removed/superseded

*Last updated: 2026-09-27*

## Progress Snapshot
The game is an **Emergent Survival** simulation (Phase 7) that has grown
a **society** (Phase 8), a **living population** (Phase 9), **lives &
lineages** (Phase 10) and **farming & neighbours** (Phase 11). There are no factions or top-down economy: the
map is the procedural terrain and loose entities — wild Food, Twigs, a
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

## What's Left / Not Yet Scheduled
These are real gaps in the current build, in roughly the order they'd
matter most:
*   ⬜ **Reputation between groups.** Groups remember grievances and
    places, but not individuals: a notorious raider is no more feared by
    the next village than anyone else.
*   ⬜ **Group vs. homestead balance.** Settling alone and living in a group
    now have similar death rates; groups win on food and on numbers, and
    lose some of that edge to risky hunts, defence and politics. Worth
    tuning if groups should be the clearly safer choice.
*   ⬜ **Real pathfinding round rocks and homes.** Walkers find their way
    round the pond on a grid (Phase 17), but still steer round rocks,
    homes and the oak with a short sideways detour when stuck. Fine at
    current density.
*   ⬜ **Tuning.** Every rate and threshold is a constant at the top of its
    class (`World`, `Bramblekin`, `Shelter`, the wildlife); the headless
    survival trend is the tool for revisiting them.

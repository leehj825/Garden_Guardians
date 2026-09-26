# Garden_Guardians_Roadmap.md

**Status key:** ✅ Done · 🟡 In progress (partly done) · ⬜ Not started · ❌ Removed/superseded

*Last updated: 2026-09-26*

## Progress Snapshot
The game is now a **Pure Simulation**: the player has no lever on the
world at all, not even a miracle. All game code lives in `Program.cs`.
Every faction's Village Heart runs its own economy end to end —
sprouting, drafting Militia, farming, building, and now brewing Nectar
and (eventually) building a Monument — entirely inside `World.Update()`.
The map is a 100m×100m terrain (grown from an original 20m×20m
prototype), viewed through a Google-Maps-style spectator camera that
pans, rotates, tilts and zooms but never touches the simulation itself.
The only two player actions left are tapping a Village Heart to inspect
that faction, and tapping anywhere to reseed the world (Genesis) once
every faction is extinct. Combat, factions, territory, and the economy
are all described in detail below and in `Garden_Guardians_Design.md`.

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

## Phase 2: Economy & Combat Infrastructure (survived, then expanded)
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

## Phase 3: Factions, Territory & the Pivot to Pure Simulation
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

## Phase 6: The Refined Economy & Civilization Goals
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

## What's Left / Not Yet Scheduled
These are real gaps in the current build, in roughly the order they'd
matter most:
*   ⬜ **Real pathfinding.** Bramblekin still steer around a rebuilt-
    every-frame obstacle list with a short sideways detour when stuck,
    rather than any actual NavMesh/grid pathfinding. Fine at current
    obstacle density; would need work if the map ever gets denser
    terrain features.
*   ⬜ **A genuine win/loss/endgame arc.** Extinction is recoverable
    (Genesis) and the Great Monument marks an achievement, but nothing
    currently *ends* the simulation or declares an overall winner across
    multiple competing factions.
*   ⬜ **Diplomacy beyond Default Peace/Thievery/Blood Feud.** No
    Diplomat unit, no negotiated (as opposed to Refugee-Protocol)
    assimilation, no paid truces — see `Garden_Guardians_Design.md`'s
    "Original Vision" for what was designed but never built here.
*   ⬜ **Further Individual Equipment / tech-tree entries** beyond Fang
    Pikes and the Chitin Mallet — no Stag Beetle/Silkworm-style new
    PvE entities, no Armory building, no player-visible tech tree (every
    unlock so far is an automatic, banked-item threshold).
*   ⬜ **Multiple simultaneous predators / ecosystem cascades.** Each
    faction gets its own independent Wolf Spider, but there's no
    population-cascade ecology (e.g. aphids exploding if too many
    spiders die) beyond the fixed respawn timers already in place.

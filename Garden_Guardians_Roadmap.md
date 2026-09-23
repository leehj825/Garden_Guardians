# Garden_Guardians_Roadmap.md

**Status key:** ✅ Done · 🟡 In progress (partly done) · ⬜ Not started

*Last updated: 2026-09-23*

## Progress Snapshot
The game runs as a Raylib-cs (C#/.NET 8) prototype on desktop and Android. So far it covers the first micro-loop: cast the Pebble-Drop miracle, the Bramblekin get out of the way, and cracked acorns feed the village. All game code is in `Program.cs`.

## Phase 0: Engine & Tooling
*   ✅ **Engine:** Switched to Raylib via Raylib-cs 8.1 (raylib 6.0) on a .NET 8 project. A fixed 45° isometric camera looks down at a 20 m × 20 m terrain, at 1 unit = 1 m.
*   ✅ **Android build:** `Platforms/Android/build-raylib.sh` builds raylib for Android with the NDK. The C# game loop runs inside a `NativeActivity`, using the same code as desktop. The game renders at 1280×720, and raylib scales that to fit the phone and maps touches back.
*   ✅ **CI:** GitHub Actions builds a debug APK on every push to a branch other than `main` (`debug-build.yml`). Pushes to `main` build a keystore-signed release APK (`release-build.yml`).

## Phase 1: Prototyping Core Physics & AI (The Micro-Loop)
*   🟡 **Task 1:** Implement the 3D physics engine interaction for the core miracles (Gust vector logic, Pebble-Drop mass/impact calculations, Sunbeam thermal grid).
    *   ✅ Custom physics loop: gravity, ground contact, pebbles that collide with each other (they stack or roll off), friction, a solid Village Heart, and hard-landing detection.
    *   ✅ Pebble-Drop: tap "Equip Pebble", then tap the ground. The drop point comes from a raycast through the tap onto the ground.
    *   ✅ God's Shadow: a 1.5 s dark shadow at the target warns of the drop before the pebble falls from 10 m.
    *   ✅ Rock clutter control: pebbles last 30 s and then shrink away, with at most 25 at once.
    *   ⬜ The Gust, The Dewdrop, The Sunbeam.
*   🟡 **Task 2:** Build the dynamic NavMesh system that updates in real-time when miracles alter the terrain (e.g., a pebble dropping, grass bending).
    *   ✅ Interim version: the list of obstacles (pebbles and the village) is rebuilt every frame. Bramblekin steer around obstacles, get pushed back out if they overlap one, take a short detour if stuck, and never pick a destination inside a rock.
    *   ⬜ Real pathfinding (NavMesh or grid). Terrain features like grass ramps and stepping stones.
*   🟡 **Task 3:** Program the "Scurry" AI state machine: idle, gather, fear radius detection, drop payload, and seek cover.
    *   ✅ States: Walking, Pausing, Gathering, Returning and Fleeing, checked in priority order.
    *   ✅ Fear: standing under a God's Shadow overrides everything. The Bramblekin drops any food it carries and flees at 3x speed to the nearest safe spot.
    *   ⬜ Predator "Fear Aura", seeking `Cover_Small`, "Play Dead".
*   ⬜ **Task 4:** Create the Wolf Spider prototype to test vibration-based aggro and player misdirection.

## Phase 2: Economy & UI Infrastructure (The Macro-Loop)
*   🟡 **Task 1:** Implement the resource gathering loop, storage capacities, and the caloric burn rate system for different unit states.
    *   ✅ First gathering loop: drop a pebble on or near the Acorn to crack it into 3 Food Shards. Bramblekin carry them to the Village Heart, and "Food Stored" counts them. A new acorn appears after 4 s. Rocks shove shards aside instead of burying them.
    *   ⬜ Storage capacities, caloric burn rates.
*   ⬜ **Task 2:** Build the UI for the Conscription Slider and the War Weariness/Morale engine.
*   ⬜ **Task 3:** Create the tech tree/crafting logic for Found-Object Weaponry and the transition from Gatherer AI to Militia AI (Phalanx flocking).

## Phase 3: Faction Systems & Territory
*   ⬜ **Task 1:** Develop the Hex-based Dynamic Frontline system and Boundary Totem logic.
*   ⬜ **Task 2:** Build the Ironjaw Legion AI (pheromone trail pathing, rapid swarming).
*   ⬜ **Task 3:** Build the Gloomkin AI (territory creep via Blight-Shrooms, AoE debuffs).
*   ⬜ **Task 4:** Implement skirmish logic, loot drops (chitin/spores), and the economic drain of continuous combat.

## Phase 4: Late-Game Bosses (Next Design Steps)
*   ⬜ **Design The Lawnmower Event:** A massive, scrolling environmental hazard. It forces the player to rapidly dig trenches, build underground bunkers, and sacrifice surface structures to save the population.
*   ⬜ **Design The Stray Cat Event:** A stealth/distraction encounter. The cat acts as an invincible entity; the player must use miracles (dropping acorns on metal cans, rustling distant bushes with The Gust) to misdirect the cat's attention while the village stays dead silent.

## Phase 5: Deepening the Simulation
*   ⬜ **Neutral Factions & Trade:** Design wandering merchant bugs (like a heavily armored Rhinoceros Beetle caravan) where players can trade surplus resources for rare tech.
*   ⬜ **Ecosystem Cascades:** Design the systemic chain reactions. If the player kills too many spiders, the aphid population explodes and eats the farms. If it rains too much, the Gloomkin territory expands faster. Force the player to manage the balance, not just win wars.

## Design Doc Mechanics Not Yet Scheduled
These are in `Garden_Guardians_Design.md` but have no roadmap task yet:
*   ⬜ **Faith Pool:** miracle costs and regeneration. Right now pebbles are free and limited only by the 25-pebble cap.
*   ⬜ **Worship Milestones:** unlocking miracles as the village grows.
*   ⬜ **Friendly fire:** pebbles crushing trapped Bramblekin. They currently dodge, and a pebble never hurts them.
*   ⬜ **Win/loss conditions:** Extinction, the Village Heart being destroyed, the Ark, and the seasonal timer.

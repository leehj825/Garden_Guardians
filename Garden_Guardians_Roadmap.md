# Garden_Guardians_Roadmap.md

**Status key:** ✅ Done · 🟡 In progress (partly done) · ⬜ Not started

*Last updated: 2026-09-23 (Health-based combat brawl: Health/Health Bars, Sustained Combat & Retaliation, target-priority and Tumble-lock fixes, Walls removed)*

## Progress Snapshot
The game is a pure God Game: the player's only lever on the world is a miracle (Pebble-Drop or Gust). Everything else — conscription, sprouting, Village Building — is the Village Heart's own autonomous business, run entirely inside `World.Update()` with no UI of its own. Cast the Pebble-Drop miracle, the Bramblekin get out of the way, and cracked acorns feed the village; the Village Heart auto-sprouts new Bramblekin the moment 5 Food Stored is banked, auto-drafts roughly one Militia per three Gatherers, and auto-places Granary blueprints on its own (Gatherers build them) once food crosses its threshold. Combat is a true health-based brawl now: the Wolf Spider (100 HP) and every Bramblekin (30 HP) have a Health bar that appears above them once they're hurt. A Militia unit is always the spider's top priority target it engages the instant one exists, however far away — it Pokes for 15 damage on a rapid 1 s cooldown, especially punishing while the spider is Tumbled (a strict lock: nothing wakes it early, damage included); an awake spider bites back for 15 on a 1.5 s cooldown. Either side dying at 0 HP is a real kill — a lone Militia loses a straight fight, but a small swarm's combined Pokes can bring a spider down outright. A well-placed pebble still distracts it, and a direct hit still crushes it instantly, even mid-Tumble. War Weariness (Morale) ties it together: losses and an actively hunting spider wear the village down, weary Gatherers move slower, and high Morale builds faster. All game code is in `Program.cs`.

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
    *   ✅ The Gust: click-drag-release aims a wide wind corridor across the terrain (10 Faith). It flings loose Food Shards (friction slows them), gently nudges Bramblekin without interrupting their AI, and knocks the Wolf Spider back — tumbling it for 4 s if it was Hunting or Pouncing.
    *   ⬜ The Dewdrop, The Sunbeam.
*   🟡 **Task 2:** Build the dynamic NavMesh system that updates in real-time when miracles alter the terrain (e.g., a pebble dropping, grass bending).
    *   ✅ Interim version: the list of obstacles (pebbles and the village) is rebuilt every frame. Bramblekin steer around obstacles, get pushed back out if they overlap one, take a short detour if stuck, and never pick a destination inside a rock.
    *   ⬜ Real pathfinding (NavMesh or grid). Terrain features like grass ramps and stepping stones.
*   🟡 **Task 3:** Program the "Scurry" AI state machine: idle, gather, fear radius detection, drop payload, and seek cover.
    *   ✅ States: Walking, Pausing, Gathering, Returning and Fleeing, checked in priority order.
    *   ✅ Fear: standing under a God's Shadow overrides everything. The Bramblekin drops any food it carries and flees at 3x speed to the nearest safe spot.
    *   ✅ Predator Fear Aura (from the Wolf Spider).
    *   ⬜ Seeking `Cover_Small`, "Play Dead".
*   ✅ **Task 4:** Create the Wolf Spider prototype to test vibration-based aggro and player misdirection.
    *   ✅ States: Prowling, Hunting, Pouncing, Recovering, Investigating and Feeding. It's drawn as a dark two-part body with eight jointed legs, twice a Bramblekin's size, and spawns near an edge.
    *   ✅ Vibration aggro: it feels Gathering or Returning Bramblekin within 7 m, hunts them, and pounces from 2.5 m. The one it catches drops its food and dies. It then feeds for 20 s, which stops one kill from luring the next victim to the dropped food.
    *   ✅ Misdirection: a pebble thud within 12 m interrupts anything except feeding. The spider walks to the impact and stares at it for 3 s.
    *   ✅ Squishing: a pebble whose centre lands within 0.4 m of the spider crushes it, and a new spider arrives at an edge 45 s later. The skill play is to distract it first, then drop a second pebble while it stares. This check runs every frame from `World.Update()` itself, entirely outside the spider's own state machine — Tumbled included, it's never invincible.
    *   ✅ **Health-based combat:** the spider (100 HP) and every Bramblekin (30 HP) now have real Health, with a red/green bar drawn above them (2D, projected from their 3D position) whenever they're below max. Tumbled is a strict lock — checked and handled before anything else in `WolfSpider.Update()` — so taking Poke damage can never wake it early; the only way out is its own timer counting fully to zero.
    *   ✅ Bramblekin Fear Aura (2 m): drop the food and run directly away at 3x speed. The God's Shadow still takes priority.

## Phase 2: Economy & UI Infrastructure (The Macro-Loop)
*   🟡 **Task 1:** Implement the resource gathering loop, storage capacities, and the caloric burn rate system for different unit states.
    *   ✅ Gathering loop: drop a pebble on or near the Acorn to crack it into 4 high-yield Food Shards (up from 3, rewarding the Faith spent). Bramblekin carry them to the Village Heart, and "Food Stored" counts them. A new acorn appears after 4 s. Rocks shove shards aside instead of burying them.
    *   ✅ Passive Foraging: a Berry appears on its own every 8 s (up to 5 on the map at once) and is grabbable without spending Faith — worth 1 food, same as any shard.
    *   ✅ Ambient Prey: up to 3 slow-wandering Aphids populate the map; Militia hunt them down, and a kill drops 2 Food Shards for the Gatherers to collect.
    *   ✅ Storage capacity: the Village Heart's Food Stored caps at 10 (raised +10 permanently per completed Granary — see Auto-Construction below). Food delivered past the cap is still collected, just wasted, so there's a reason to spend it.
    *   ✅ Reproduction (The Sprout): fully automatic again — the pure God Game pivot removed the manual Sprout button. The instant Food Stored reaches 5, the Village Heart spends it and spawns one new Bramblekin next to itself, repeating until there's less than a Sprout's worth banked. There's no population cap.
    *   ✅ Faith Pool (the miracle economy): up to 100 Faith, refilling at 0.5 × population per second, so every lost Bramblekin also weakens your miracles. A Pebble-Drop costs 30, paid the moment the tap lands on the ground. Below 30 the button is greyed out and shows "Not Enough Faith" when tapped. The HUD shows a Faith meter, Food Stored against the cap, Population and Morale — the player's whole UI now, alongside the two miracle buttons.
    *   ⬜ Caloric burn rates.
    *   ✅ **Auto-Construction:** the Village Heart places Granary Blueprints on its own, no UI of its own. Once Food Stored reaches 90% of the current cap, it places one at a random open spot within 5 m of itself (guarded so only one is ever queued at a time), getting first claim on Food Stored over Auto-Sprout (checked first in `World.Update()`), since Auto-Sprout's own greedy trigger would otherwise drain the stash back below the 90% threshold every frame before Construction ever saw it. Idle Gatherers with no food waiting still path to the nearest Blueprint (the Building state) and add Construction Progress while touching it. A completed Granary permanently raises the food cap.
    *   ⬜ ~~Bramble-Wall~~ (removed): it blocked the spider but also cluttered the autonomous pathfinding, so this turn tore it back out — blueprints, auto-placement, and the custom AABB collision check all gone. Combat now runs entirely through the health-based Poke/Bite exchange below instead.
*   🟡 **Task 2:** Build the Auto-Conscription system and the War Weariness/Morale engine.
    *   ✅ **Auto-Conscription:** no player-facing slider any more — the Job Manager recomputes its own Militia Target every frame from the living population (one Militia per three Gatherers, population/4) and works the colony toward it, one promotion/demotion at a time. Losses re-shrink the ratio automatically, with no player input needed.
    *   ✅ **War Weariness / Morale:** a Morale stat (0–100%, starts at 100) shown in the Colony panel. A Bramblekin kill costs 20 points immediately; every second the spider is actively Hunting or Pouncing costs 2 more; Morale recovers at 1/s whenever it isn't. Below 50%, Gatherers are Weary and walk 40% slower; above 80%, Builders work at 2x Construction Progress.
*   🟡 **Task 3:** Create the tech tree/crafting logic for Found-Object Weaponry and the transition from Gatherer AI to Militia AI (Phalanx flocking).
    *   ✅ The Armory: the Job Manager (Auto-Conscription) promotes the nearest Gatherer to the Village Heart to Militia when under target, and demotes the nearest Militia back to Gatherer (pike put away, sent back to Wandering) when over target. Militia carry a small Rose-Thorn Pike and never gather.
    *   ✅ Phalanx AI, now target-priority-fixed: a spider is a Militia unit's absolute top priority the instant one exists, at any distance — it abandons Aphid Hunting and Defends, closing in to intercept, rather than only reacting once the spider wanders within the 2 m Fear Aura a Gatherer uses.
    *   ✅ **Sustained Combat & Retaliation:** Pike Defense (blocking a pounce) and the old instant-Tumble/Lethal-Swarm poke are gone — Militia and the spider now trade real Health damage. In range (1.5 m), a Defending Militia Pokes for 15 damage on a fast 1 s cooldown; an awake (non-Tumbled) spider Bites the nearest Militia in range for 15 on a 1.5 s cooldown. Either dropping to 0 HP is a kill. A lone Militia loses a straight fight (30 HP against 100, and the Bite hits harder one-on-one), but several attacking together can win the DPS race and bring a spider down. A Gatherer caught by a Pounce is still an instant kill — that hasn't changed.
    *   ⬜ Found-Object Weaponry crafting/tech tree, multi-unit Phalanx spacing.

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
*   ⬜ **Worship Milestones:** unlocking miracles as the village grows.
*   ⬜ **Friendly fire:** pebbles crushing trapped Bramblekin. They currently dodge, and a pebble never hurts them.
*   ⬜ **Win/loss conditions:** Extinction, the Village Heart being destroyed, the Ark, and the seasonal timer.

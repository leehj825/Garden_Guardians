# Garden_Guardians_Design.md

## Core Concept
**Genre:** God Simulation / Real-Time Strategy
**The Hook:** You act as a benevolent spirit in a suburban backyard protecting the Bramblekin—tiny, industrious creatures. You cannot control them directly; instead, you manipulate the environment using physics-based "miracles" to guide their survival, economy, and wars.

## The Core Gameplay Loop
Help creatures survive basic threats $\rightarrow$ Grow their society $\rightarrow$ Unlock new, powerful (but indirect) "miracles" $\rightarrow$ Manage larger conflicts and complex ecosystems $\rightarrow$ Face the unpredictable consequences of a maturing, independent civilization.

## The Miracles (Physics & Environment Manipulation)
*   **The Gust (Directional Force):** Draw a wind vector to blow away heavy leaves, push dropped resources, scatter insect swarms, or flip jumping spiders mid-air. 
*   **The Pebble-Drop (Kinetic Impact):** Spawn a heavy physics object to crack hard nuts, crush enemies, or alter the NavMesh to create stepping stones across water.
*   **The Dewdrop (Weight & Moisture):** Spawn heavy water to weigh down grass blades (creating ramps), extinguish fires, or wash away enemy pheromone trails.
*   **The Sunbeam (Thermal Grid):** Focus light to bake mud into fast-walking clay paths, accelerate crop growth, or temporarily blind aerial predators.

## Miracle Economy: The "Faith" Resource
To prevent players from spamming physics objects and trivializing the survival aspect, miracles draw from a central, regenerative Faith Pool.

*   **Generation:** Faith regenerates slowly at a baseline rate. This rate is multiplied by village Morale (happy Bramblekin worship more) and by constructing Shrines.
*   **Action Costs:**
    *   *The Gust:* Low cost per swipe. Allows for quick, reactionary defense, but spamming it will drain the pool before a heavy predator arrives.
    *   *The Dewdrop:* Medium cost.
    *   *The Pebble-Drop:* High cost. Dropping a rock is a massive expenditure of energy, forcing the player to use it only for high-value targets or critical puzzle-solving.
    *   *The Sunbeam:* Continuous drain. It consumes Faith per second while held, requiring the player to be precise rather than sweeping it across the whole map.

## Miracle Unlocks: The Worship Milestones
Miracles unlock dynamically as the society evolves, tying your "God" powers directly to their technological and cultural milestones. You can only manipulate elements the Bramblekin have learned to revere.

*   **The Gust:** Unlocked by default. Your "awakening" breath that saves the initial colony.
*   **The Pebble-Drop:** Unlocks at Population 10 when the Builders construct the Stone Altar (an arrangement of small gravel). They recognize the earth, giving you power over it.
*   **The Dewdrop:** Unlocks after surviving the first summer heatwave. The Builders construct a Rain Catcher (a curled leaf funnel), unlocking water manipulation.
*   **The Sunbeam:** Unlocks in the mid-game when scouts recover the Prism Relic (a shard of broken glass from a bottle). Placing this in the village center grants you thermal/light manipulation.

## Friendly Fire & The "God's Shadow" Mechanic
Yes, miracles can crush your own units. Treating the Bramblekin as immune to physics breaks the immersion of their fragility. However, to prevent frustrating accidental deaths, the AI actively tries to survive you.

*   **The Telegraph:** When you initiate a Pebble-Drop or Dewdrop, a dark shadow appears on the ground for 1.5 seconds before the object impacts.
*   **Self-Preservation AI:** This shadow acts as an absolute override for Bramblekin AI. If a Bramblekin is inside the shadow, they instantly drop everything and dive-roll out of the radius.
*   **The Consequence:** Friendly fire only happens if you drop a pebble on a Bramblekin who is physically trapped (e.g., stuck in a sap moat, cornered by a wall, or webbed by a spider). This forces you to aim carefully when bailing out trapped units.

## Predators & Prey AI
*   **The Wolf Spider (Ground Stalker):** Hunts via a "vibration grid" triggered by Bramblekin carrying heavy resources. Players can counter by dropping pebbles as seismic decoys or flipping the spider with The Gust.
*   **The Robin (AoE Boss):** Telegraphs its landing with a massive shadow. It rapid-fires pecks at moving targets. Players must ensure the village has physical canopy cover or use The Sunbeam as a flashbang to break its targeting.
*   **The Scurry System (Prey AI):** Predators emit a "Fear Aura." Unarmed Bramblekin enter Panic Mode, drop their cargo to increase speed, and run to objects tagged `Cover_Small`. If trapped, they may "Play Dead" to drop off the targeting array.

## Mid-Game: Militarization & Defenses
*   **The Militia AI:** Building a Village Bell changes the Fear Aura response to "Alert." Gatherers flee, while Militia units flock into a Phalanx formation to block the threat.
*   **Found-Object Weaponry:** 
    *   *Rose-Thorn Pikes:* Planted in the ground to counter leaping spiders.
    *   *Pollen Grenades:* Create AoE dust clouds that disable enemy insect targeting.
    *   *Acorn-Cap Armor:* Grants a health shield and prevents knockback.
*   **Autonomous Defenses:** Sap Moats (reduce enemy speed by 80%), Canopy Roofs (block vertical aerial attacks), and Spore-Minefields (explosive knockback traps).
*   **God-Commander Synergies:** The player casts The Gust behind charging troops for a speed boost, or spreads pollen grenade clouds over a wider area.

## Faction Warfare
*   **The Dynamic Frontline:** The garden is divided into hex zones. Expansion requires Militia to hold "No Man's Land" long enough for Builders to erect Boundary Totems.
*   **The Ironjaw Legion (Ant-Folk):** Highly disciplined. They use pheromone highways for rapid resource stripping. Units include Acid-Spitters and Aphid-Cavalry. Countered by washing away pheromone trails with The Dewdrop.
*   **The Gloomkin (Spore-Cult):** They expand by planting Blight-Shrooms that emit toxic miasma. Units include Spore-Druids (parasitic slowing pods) and Pillbug Siege Engines. Countered by burning the Blight-Shrooms with The Sunbeam.

## Diplomacy & Greed
Today, any two Bramblekin tribes (the original Village Heart and every Faction a Schism has since split off from it) are instantly and permanently hostile the moment their territories brush up against each other — the only truce is the temporary Pioneer's Truce a fresh splinter gets with the parent it just left. This is the next layer on top of that: a spectrum between peace and war driven by what each tribe actually needs, not a flat switch.

*   **Default Peace:** Rival tribes maintain a truce by default instead of instant hostility. Two Factions that have never wronged each other simply coexist — Militia patrol their own territory ring and answer the Wolf Spider together, but leave a peaceful neighbor's Gatherers and Village Heart alone. War has to be provoked, not assumed.
*   **Resource Greed (The Beggar & The Raider):** If one Faction is starving (0 Food Stored) while a neighbor is hoarding (Food Stored capped at MaxFoodCapacity), the starving tribe sends a Diplomat — a single unarmed Bramblekin — to the rich neighbor's Village Heart to beg. If the player doesn't intervene to share resources (e.g. nudging a Gust-load of loose food across the border, or otherwise prompting the hoarder to donate) within a grace window, the Diplomat returns home empty-handed and the starving tribe launches a desperate raid on the rich granaries instead — the same Base Razing playbook already in the game, just motivated by hunger rather than open war.
*   **Territorial Greed:** If populations grow large enough that two Factions' 20-meter territory rings physically overlap, the intersection becomes a contested warzone — Gatherers from either side risk a fight just foraging there, and Militia from both tribes converge on it rather than waiting for a border violation deeper in their own territory.
*   **Thievery:** If a Gatherer sneaks into a rival's territory to steal food and is caught and killed by that faction's Militia, that specific death — not the general existence of two nearby tribes — is what triggers a permanent war between those two Factions. Peace is the default right up until someone gets caught with their hand in the granary.

## The Economy of War
*   **The Caloric Tax:** Militia units burn calories at 2x-3x the normal rate and require high-tier rations (aphid meat, nut stores). Famine causes the army to desert and revert to Gatherer AI.
*   **The Labor Vacuum:** Conscription instantly removes workers from the economy. Maintaining weapons cannibalizes civilian repair resources (wood, silk, sap).
*   **The Morale Engine:** Prolonged mobilization builds "War Weariness," slowing all village activity. Casualties cause grieving states. Winning battles grants a "Triumphant" economic buff. Extreme low morale triggers civilian strikes, which the player must fix via divine intervention.

## Win and Loss Conditions
The game operates on a seasonal timer, pushing the colony toward a definitive endgame rather than an endless sandbox.

*   **The Loss Condition (Extinction):** The game ends if the Bramblekin population drops to zero, or if the Village Heart (the original terra-cotta pot or seedling they built around) is destroyed by a rival faction or boss event.
*   **The Win Condition (The Great Migration):** The ultimate realization is that the backyard is too hostile to sustain a massive, permanent civilization. The overarching goal is to build The Ark before Winter arrives.
    *   *The Objective:* Gather exorbitant amounts of rare, guarded resources (like silk, specific light-weight bark, and dandelion parachutes) to construct a massive wind-ship.
    *   *The Climax:* Launching the Ark triggers an endless wave of predators and rival factions desperate to steal the vessel. You must expend all your Faith defending the launch platform until the wind catches the Ark, carrying the Bramblekin over the fence to the "Promised Land" (winning the game).

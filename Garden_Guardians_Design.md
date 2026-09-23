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

## The Economy of War
*   **The Caloric Tax:** Militia units burn calories at 2x-3x the normal rate and require high-tier rations (aphid meat, nut stores). Famine causes the army to desert and revert to Gatherer AI.
*   **The Labor Vacuum:** Conscription instantly removes workers from the economy. Maintaining weapons cannibalizes civilian repair resources (wood, silk, sap).
*   **The Morale Engine:** Prolonged mobilization builds "War Weariness," slowing all village activity. Casualties cause grieving states. Winning battles grants a "Triumphant" economic buff. Extreme low morale triggers civilian strikes, which the player must fix via divine intervention.

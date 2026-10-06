# Garden_Guardians_Roadmap.md

**Status key:** ✅ Done · 🟡 Partly done · ⬜ Not started · ❌ Removed or superseded

*Last updated: 2026-10-06. This is the short version: one line per phase. The full detail of every phase (numbers, tuning, benchmarks) is in
[`docs/archive/Garden_Guardians_Roadmap_Detailed.md`](docs/archive/Garden_Guardians_Roadmap_Detailed.md). What is still to do is in
[`Garden_Guardians_TODO.md`](Garden_Guardians_TODO.md); how the game works is in [`Garden_Guardians_Design.md`](Garden_Guardians_Design.md).*

## Where the game stands

An **emergent survival simulation** in a hilly backyard at the foot of a giant oak, beside a pond. Every Bramblekin is an individual with a
personality and needs; they settle, pair up, raise young, form clans, villages and kingdoms, farm, trade, feast, believe, and go to war; seasons,
day and night, droughts, floods and spider invasions shape the years. The player mostly watches (a Google-Maps-style camera, tap to inspect, follow,
an automatic Director camera, a History screen) and, in the Release build, can **Explore** as one Bramblekin. Plays upright or sideways on Android;
the desktop build is for development. A Google AdMob banner and a Google Play release are being prepared.

## Now

| What | Status |
| --- | --- |
| AdMob banner: ads fail with HTTP 403 until AdMob approves the app ("Requires review"); app-ads.txt is verified | ⬜ waiting on AdMob |
| Google Play: AAB workflow, AD_ID declared, target API 36, store assets, listing text | ✅ built · ⬜ release not yet public |
| Explore mode in the Release build | ✅ |
| Next ideas (first-run hint, drop food at the clan's store, real-phone testing…) | ⬜ see the To-do list |

## Phases

### Foundations (0–7)
| # | Phase | |
| --- | --- | --- |
| 0 | Engine and tooling: raylib on .NET, Android through a NativeActivity and an NDK-built raylib, CI builds | ✅ |
| 1 | The original micro-loop (miracles, Pebble-Drop, God's Shadow) | ❌ |
| 2 | Economy and combat infrastructure (berries, Wolf Spider, hornets, grubs survived) | ❌ superseded by 7 |
| 3 | Factions, territory, and the pivot to pure simulation | ❌ superseded by 7 |
| 4 | Spectator camera and mobile UI: pan, rotate, pinch zoom, tap to inspect | ✅ |
| 5 | Performance for a 5x larger map: spatial grid, AI time-slicing, pooling, culling | ✅ |
| 6 | Refined economy and civilization goals (Nectar Brewery, Great Monument) | ❌ superseded by 7 |
| 7 | The emergent survival pivot: personalities, the hunger → safety → duty needs | ✅ |

### Society and lives (8–16)
| # | Phase | |
| --- | --- | --- |
| 8 | Settling and society: twigs, tents and houses, stores, groups, leaders | ✅ |
| 9 | A living population: seasons, births and inheritance, villages and budding | ✅ |
| 10 | Lives and lineages: follow camera, names, couples, old age | ✅ |
| 11 | Farming, neighbours (neutral, allied, at war), and a 13-year balance review | ✅ |
| 12 | The wider garden: rarer wars, villages spread out | ✅ |
| 13 | Watching the garden: fast-forward plays the same game, tidier chronicle, Stats tab | ✅ |
| 14 | A small world: acorn villages, the pond, the giant oak | ✅ |
| 15 | Richer natures: four new traits, clan culture | ✅ |
| 16 | Easier to watch: log button, bars that grow with the zoom | ✅ |

### Water, wildlife and craft (17–25)
| # | Phase | |
| --- | --- | --- |
| 17 | Water, harvests, stone and branch; the whole map; water you can't walk on | ✅ |
| 18 | Thirst: a need, cisterns, settling by the water | ✅ |
| 19 | The oak's roots, and droughts that shrink the pond | ✅ |
| 20 | Wells | ✅ |
| 21 | Slings, frogs and the heron, seed corn, trade, more gardens | ✅ |
| 22 | Reputation between individuals (infamy), and a balance check | ✅ |
| 23 | Fixes from play on a phone | ✅ |
| 24 | Hearths, snares, adoption | ✅ |
| 25 | Fast-forward you can watch: optimized debug APKs, profiling, level of detail | ✅ |

### Atmosphere and culture (26–33)
| # | Phase | |
| --- | --- | --- |
| 26 | Day and night: sleep, the night watch, the owl | ✅ |
| 27 | The Director: an automatic camera | ✅ |
| 28 | Aphid herding | ✅ |
| 29 | The beehive in the oak: honey, bees, smoking | ✅ |
| 30 | Harvest feasts | ✅ |
| 31 | Shrines and beliefs, prophets and schisms | ✅ |
| 32 | Shields and champions' contests | ✅ |
| 33 | The garden timeline on the History screen | ✅ |

### A modelled world (34–43)
| # | Phase | |
| --- | --- | --- |
| 34 | A modelled world: the terrain and many props as 3D models (some things still drawn in code: ant, bee swarm, frog…) | ✅ · 🟡 |
| 36 | Cleaner map, easier taps: map-guide toggles, tap a clan's name tag (Phase 35 was never used) | ✅ |
| 37 | More gardens: four terrains | ✅ |
| 38 | Procedural terrain: generated gardens, a start menu, map sizes (100, 150, 200 m) | ✅ |
| 39 | Low-poly look, textured ground, stone walls | ✅ |
| 40 | Society: villages, rations, paid jobs, kingdoms, spider invasions | ✅ |
| 41 | Close-up ground cover | ✅ · ⬜ finer ground mesh |
| 42 | Models for the oak and the village fittings | ✅ · ⬜ a few fittings still in code |
| 43 | New kin models and animations; weapons, shields, bow and arrows; running, jumping; music; Settings page and Menu button | ✅ |

### Going to players (44)
| What | |
| --- | --- |
| Upright and sideways: one layout for both, follows a rotating phone (a native patch for raylib's Android backend), insets for notches and system bars | ✅ |
| Explore mode in the Release build: walk, run (stick to its edge), jump, pick up, bag; auto pick-up, eat and drink; the explorer cannot be hurt | ✅ |
| AdMob banner along the bottom (PopupWindow, SDK 25.5 on .NET 9); the ids are in `AdConfig.cs`; Debug stats bar shows the ad's state | ✅ built · ⬜ ads not showing yet (AdMob review) |
| Google Play: signed AAB on every push to `main`, version code from the run number, AD_ID permission, target API 36, app-ads.txt, store assets (`store/`) | ✅ |
| Small fixes: the "thorn" placeholder weapon removed; bigger item messages; eating no longer plays the pick-up clip; filling a bottle does | ✅ |

## Ideas for the clans (not scheduled)

First picks if this is revisited: roads, workshops with tools, a fuller tech tree. "Done" is what exists; "Not yet" is the gap.

| Idea | Done | Not yet |
| --- | --- | --- |
| Tech tree with eras (Stone, Farming, Village, Kingdom) | ages worked out from crafts, shown on the clan card, gate Tools, Roads, Markets | more crafts and buildings per age |
| Roads and bridges | worn dirt paths, paved roads | clans laying roads on purpose, bridges |
| Workshops and specialists | workbench, weaving, stonecutting, goods | individual specialists, uses for goods |
| Markets and currency | stalls, trade between allies | coins, caravans, prices that follow scarcity |
| Writing and history | runed stones, deeds carved into the chronicle | lore that outlives a clan |
| Kingdoms and vassals | ✅ (Phase 40: capital, king, tribute, pledged soldiers) | protection of vassals, territory in conquest |
| Defense and siege | watchtowers with an alarm horn | gates, siege tools, traps |
| Calendar and festivals | sundial, solstice festivals | plans that follow the calendar |
| Medicine and disease | herb gardens, quarantine, sickness through trade | healer huts, epidemics |
| Exploration | scouts, a known map, fog, a far-shore cache, rafts | rafts for settlers, more finds |
| Religion and culture branches | beliefs, shrines (Phase 31) | temples, priests, holy days |
| Ecology and domestication | aphid herds, beehive | orchards, overfishing, wild food that reacts to clans |

## Known gaps

*   **Society follow-ups:** a sticky crown (fewer coronations), a test that pledged soldiers arrive at a sister village, balancing invasions and saving them mid-way, village landmarks, rations that follow the season, worn roads between a kingdom's villages.
*   **Pathfinding round rocks and homes** is a short sideways detour; fine at today's density.
*   **A bigger garden** than ±50 m needs several grids and spawn amounts to scale (the generated gardens already scale some).
*   **Tuning:** every rate is a constant at the top of its class; the headless survival trend is the tool for revisiting them.

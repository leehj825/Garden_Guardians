# Garden Guardians — Society & Jobs Design

Status: **steps 1 (villages), 2 (rations), 3 (soldiers, fishers), 4 (the headman's jobs) and 5 (kingdoms) are built; step 6 (events and UI polish) is not.** Written 2026-10-01, updated 2026-10-02. Decisions are in section 11; the roadmap entry is Phase 40 in `Garden_Guardians_Roadmap.md`.

---

## 1. Goal

Today the game has two ideas tangled together: *how advanced a clan is* (its "age") and *how big its society is*. This design separates them, and makes bigger societies behave differently from small ones:

* **Technology (the ages)** decides *what a society can build and know*. Stone → Farming → Village → Kingdom. This stays as it is (counted from crafts; see `World.EraOf`).
* **Society (the levels)** decides *who belongs together and who leads*. Family → Clan → Village → Kingdom, each a real object with a name, members and a leader.
* **Provisioning** decides *who is free to do a job*. In a small society everyone feeds themselves first. In a settled society a pooled store feeds the people whose job is not food — soldiers, scouts, builders — so they can work *without hunting for their own meal*. That is what makes a village or a kingdom *play* differently from a clan.

## 2. What exists today (checked in the code)

| Piece | Where | What it does now |
|---|---|---|
| Clan | `Kin/KinGroup.cs` | `Members`, one `Leader`, `Home` + `Annexes` (extra homes), `Era`, `LiegeId`. Founded from encounters; named after the leader's family. |
| Jobs | `Kin/Society.cs` `KinJob` | **Eight existed at the start (Fisher is the ninth, added in step 3):** Gatherer, Builder, Hunter, Guard, Farmer, Raider, Healer, Scout. Handed out by the Leader (`World.AssignJobs`). |
| Duty | `Kin/Bramblekin.Duty.cs` | A kin does its job **only when "fed and safe"** (the need order is hunger/thirst → safety → duty → settling → social). So today no job is ever *paid*; a Guard who is hungry stops guarding and forages. |
| Store | `World/Shelter.cs` | Each home has `StoredFood`; clan's `SharingRule` (Equal / LeaderFirst) says who may eat from it. |
| "Village" | `World/World.GroupHomes.cs` | **Only a counter** (`VillagesFounded`): a clan that builds a second home (annex) is "a village". It has no name, members, centre or leader of its own. |
| Alliance | `World/World.Neighbours.cs` | Pairwise, `MaxAllies = 2` per clan. |
| Kingdom | `World/World.Kingdoms.cs` | A Kingdom-Age clan makes a smaller ally swear fealty (`LiegeId`); vassals pay tribute and can break free. The "king" is simply that clan's leader. |
| Governance | `World.Council`, `World.Succession`, `World.Champions` | Leader's council of up to 3, named heirs, duels settling disputes, tribute. Re-usable for the new levels. |

*As of the start of this work:* jobs existed but were unpaid, and "village" and "kingdom" were not objects. Steps 1-5 changed both.

## 3. Principles

1. **Two ladders, never mixed.** Tech age is a *cap* (a village needs farming; a kingdom needs the Kingdom Age); society level is the *structure*.
2. **A society only pays what it earns.** Rations come from a real surplus. No surplus → no paid jobs → back to foraging. Starvation stays the pressure.
3. **Leaders at every level**, with a title: family head, clan chief, village headman, king. Each level's leader answers to the one above.
4. **Reuse what works.** Councils, heirs, duels, tribute, errands and the Leader-decision tick are kept; new levels plug into them.
5. **Everything testable headless.** Each phase ends with numbers from the headless runs (food, deaths, jobs held), not just looks.

## 4. Society levels

### 4.1 Family (Stone Age)
* A pair and their children, living in a tent. Head of household = the older/more reputable parent.
* Forage and hunt for themselves. No paid jobs. (This is today's "solitary / small group".)

### 4.2 Clan (Farming Age)
* Related families settling together in houses; they farm. Leader = **clan chief**. (This is today's `KinGroup`; mostly unchanged.)
* May assign jobs, but each job-holder still eats on their own time (unpaid) *unless the clan has a surplus rule — see 5.4*.

### 4.3 Village (Village Age) — **new object**
```
Village
  Id, Name ("Mossbridge"), Centre (position), FoundedAt
  Clans: list of clan ids        // one or several
  Homes: derived from member clans' homes within VillageRadius of Centre
  Headman: Bramblekin?           // the village leader
  Store: VillageStore            // shared pooled food (see 5)
  Landmarks: well, market, shrine, granary (shared)
```
* **Formation:** when ≥ 2 homes (from one or more clans) stand within `VillageLink` (14 m) of each other and the owning clans are not at war, a village *forms around the best-connected home* and is named. A clan with only annexes (today's "village") becomes a one-clan village.
* **Headman:** chosen by the same claim-to-lead score the game already uses (Intelligence + Reputation + kin support), *among clan chiefs*; ties and disputes use council vote then duel.
* **Clans keep their chiefs** under the headman; internal clan matters (inheritance, loyalty) stay with the clan.
* **Dissolves** when homes drop below 2 or all clans leave; its store is shared out to its clans.

### 4.4 Kingdom (Kingdom Age) — **new object**
```
Kingdom
  Id, Name, Capital (village id), King: Bramblekin?
  Villages: list of village ids (target 5–6)
  Treasury/Tribute rules, Army: soldiers pledged by villages
```
* **Formation:** **as built:** ≥ 3 villages within 90 m of each other and *at peace* (alliance proved too rare), at least one in the Kingdom Age; the most populous Kingdom Age village is the capital. Villages in reach and at peace swear fealty. (Originally planned: allied villages and the crafts Stonework + Roads + Markets.) Fealty replaces today's clan-over-clan vassalage for these cases (the small-realm vassal mechanism stays for clans not in any village).
* **King:** the capital's headman by default; contested by strongest village (council vote → duel). Succession reuses `ConsiderHeir`.
* **Vassal villages** keep headmen; owe tribute (food to the Capital store) and pledged soldiers; may rebel when too large (existing independence rule).
* **Dissolves** if villages fall below 3; villages revert to independent.

## 5. Provisioning — paying people to do jobs

### 5.1 The idea
Today: need order = Hunger → Safety → **Duty** → Settle → Social. A kin does its job only after it has fed itself.

New: a kin with a **paid job** is **fed from the community store** (village or kingdom store) as part of its job, *before* it would go foraging. Hunger still exists (they must eat), but eating becomes a short trip to the store, not a hunt.

### 5.2 Rations
* A paid kin has a **ration entitlement**: while the store holds food, a hungry paid kin walks to the store and eats from it (using the existing `TryWithdraw`). It does not forage unless the store is empty.
* **Unpaid** kin (family members, free farmers when not rationed) behave exactly as today.
* If the store runs empty, paid kin **lose their ration** and revert to foraging; their job pauses. Leaders re-balance.

### 5.3 Who produces, who consumes
* **Producers** (never rationed by default): Farmer, Fisher, Gatherer, Hunter. They fill the store.
* **Consumers** (rationed): Soldier (Guard), Scout, Builder, Healer, and the Leader/council.
* Producers eat first from what they carry/grow (and may eat from the store like anyone), so the system cannot starve them for others' sake.

### 5.4 Balance cap (the thing that makes it not collapse)
Each Leader tick the community computes:
```
income  = food entering the store over the last season (moving average)
upkeep  = rations per paid kin per day × days
allowedPaid = floor( (income - reserve) / rationPerPaidKin )
```
and may assign **at most `allowedPaid`** consumer jobs (with a hard floor of 0). Reserve = enough for one winter month. This is the single dial that stops a village from hiring more soldiers than its farms can feed. Start conservative; tune from headless runs.

### 5.5 Where the store lives
* A **village granary** (existing `Granary` craft/model) becomes the village store; each home's own `StoredFood` still exists as the household store and spills to the village store above a threshold.
* A kingdom has a **capital store** that receives tribute and supplies the army in the field.

## 6. Jobs (final list)

| Job | Level | Produces/Consumes | Behaviour |
|---|---|---|---|
| Farmer | clan+ | produces | Plants/harvests plots → store. (exists) |
| **Fisher** | clan+ (needs Fishing craft) | produces | Walks to shore, fishes, carries catch → store. (Fishing exists as an action; becomes a job.) |
| Gatherer | clan+ | produces | Collects loose food/acorns/honey within range → store. (exists) |
| Hunter | clan+ | produces | Hunts the picked prey. (exists) |
| **Soldier** (Guard, renamed/extended) | village+ | **consumes** | Patrols the village boundary, answers alarms, fights predators/raiders; **picks up food only within a few metres of its patrol route** (never leaves post), delivers it to the store. |
| Scout | village+ | consumes | Explores and warns. (exists; becomes paid) |
| Builder | village+ | consumes | Walls, houses, wells. (exists; becomes paid) |
| Healer | village+ | consumes | Tends the sick. (exists; becomes paid) |
| Raider | war only | consumes | Raids enemy stores. (exists) |
| **Army** | kingdom | consumes | Soldiers pledged by villages marching to a threat or a front. |

Assignment: the **Leader of that level** assigns (clan chief in a clan; headman in a village; king for the army), scoring members by skills + personality (e.g. Courage/Aggression → Soldier; Farming skill → Farmer). Existing `AssignJobs` is extended with the cap in 5.4.

### 6.1 Soldiers are stronger (added 2026-10-01; built)

* **Born strength:** every kin now has a `Strength` trait (0..1, inherited from its parents like the other traits, nudged up by a martial clan culture, saved). A strong kin hits harder (±2 damage around the average) and takes a little less from every blow (±10%).
* **Soldier training:** the Guard job makes a kin strike **30% harder** (a Raider 15%) and take **10% less**. Guards are picked by courage, aggression **and born strength** (`Personality.Strength`; ant-hill eggs and the "shaken" penalty do not count when jobs are handed out).
* **Shields go to fighters:** a clan that knows Shields issues them to Guards, Raiders and Hunters only (not to everyone, as before). A shield takes **a third** off every blow and bite; a Guard holds it up in front and takes **15% less again**. Shields are drawn on those who carry them.
* Later (steps 2-3): soldiers fed from the village store stay on post, so this strength is actually in the field when predators and raiders come.

## 7. Leaders and titles

| Level | Leader | Chosen by | Replaced when |
|---|---|---|---|
| Family | head of household | seniority | death/old age |
| Clan | chief | existing claim-to-lead; council | existing rebellion/heir rules |
| Village | headman | best claim among clan chiefs; council vote; duel if tied | headman dies; a clan breaks away |
| Kingdom | king | capital's headman by default; strongest-village contest | death → named heir; rebellion |

Each has a banner/colour and a line in the Clans and History views.

## 8. Data model & save

* New `Village` and `Kingdom` classes (`Source/World/Village.cs`, `Kingdom.cs`), held in `World._villages`, `World._kingdoms`; clans carry `VillageId?`, villages carry `KingdomId?`.
* New per-kin field: `Rationed` (bool, derived each tick) — **not saved**; recomputed from job + store.
* Save: `VillageSave`, `KingdomSave` lists (nullable → old saves load unchanged, villages/kingdoms re-formed on first tick). Follows the `WallSave` pattern.
* `KinJob` gains `Fisher` (appended — enum values saved as numbers, so new values go at the end).
* Existing `VillagesFounded` counter stays for stats but is replaced as the source of truth by `_villages.Count`.

## 9. Build plan (each step independently testable)


1. **Village object** — formation, name, centre, headman election, dissolve; shown in the Clans view and as a banner on the map. *Test:* headless run: villages form/dissolve sensibly; no kin lose their clan.

   *Step 1 done (2026-10-01):* `Village` objects (`Source/World/Village.cs`, `World.Villages.cs`): formed from homes within 14 m of each other (clans not at war), at least one clan in the Farming Age; a clan belongs to the village its main home stands in; a name, middle and headman chosen from the clans' chiefs (claim to lead + clan size + friends' votes); merge/abandon handling; saved; shown as a gold-edged tag over the village and in the clan card, stats and History. Headless runs: 3 seeds x 50 min of game time give 2-4 stable villages each, multi-clan ones included, no renaming churn.
2. **Village store + rations** *(built 2026-10-01: see below)* — pooled store, paid kin eat from it, cap formula. Start with Guard only. *Test:* with rations on, guards guard longer, village food stays ≥ reserve, no new starvation deaths vs. baseline.
3. **Soldier + Gatherer + Fisher jobs** — patrol-route pickup rule, fishing as a job. *Test:* job counts vs. cap; food income vs. upkeep.
4. **Remaining paid jobs** — Scout, Builder, Healer on rations; headman assigns.
5. **Kingdom object** — alliance of ≥ 3 villages, king, capital store, tribute, pledged soldiers (army).
6. **Events & UI polish** *(not built)* — founding/succession/revolt headlines, chronicle entries, a Villages tab in History, stats lines.

Walls (already built) become a **village** project paid from the village store in step 4.

*Step 2 done (2026-10-01):* `World.Rations.cs`. The village's pooled store is the stock of every home of its clans. Each look it works out `income` (food deposited by its clans, smoothed over ~1.5 min) and `AllowedPaid = floor(income x 0.5 / mealsPerKinSecond + (stock - reserve) / (mealsPerKinSecond x 900 s))`, reserve = 0.7 pieces per villager. That many Guards (lowest IDs first) are *paid*: when hungry they are brought a ration from the nearest village store (so they eat in place and do not leave the post), and never when stock is at the reserve. Headless result (6 seeds x 50 min, Village-Age start): paid guards spent 19.7% of their time food-seeking (incl. eating) vs 24.3% for unpaid guards, and were starving 1.2% of samples vs 3.8%. The effect is real but modest because few guards exist yet (Guard is a single job in peace time) — step 3 adds the Soldier role that makes it matter.

*Step 3 done (2026-10-01):* **Soldier** — in a village a Guard patrols a ring of 8 waypoints just outside the homes (looks about 3 s at each), answers any clan's alarm near any of the village's homes, and picks up food within 4 m of its route and carries it to the stores (it never leaves the route for more). The garrison is as large as the village can feed (the `AllowedPaid` of step 2, at most two in five adults, picked by courage + aggression + strength). **Fisher** — `KinJob.Fisher`: a clan that knows Fishing, with shore within 20 m of home, keeps one fisher per 6 people on the shore all day (before, fishing was only a fallback). Headless A/B (6 seeds x 40 min, Village-Age start, jobs off vs on): alive 206 -> 238, starved 122 -> 100, killed by predators 85 -> 65, soldiers on average 17 -> 29, fishers 0 -> 11, food in stores 227 -> 270, guards on patrol/post 33% -> 53% of the time. Gatherer, Farmer and Hunter are unchanged (they are the producers).

*Step 4 done (2026-10-01):* **The headman gives the jobs.** Each look, the village's headman (no headman, no orders) turns the village's `AllowedPaid` into jobs: 60% soldiers (at least one, at most two in five adults), then a healer while anyone needs care and a clan knows herb-lore, up to two builders while a wall or home is being built, a scout while a clan can map ground. He picks the best of any clan's people (soldier: courage + aggression + strength; healer: intelligence + sociability + healing; builder: intelligence + diligence + building; scout: courage + intelligence), never a farmer, fisher, sick or badly hurt kin, nor himself, keeps those doing well and lets the rest go. These `VillageJob`s override the clans' own job lists, and the fed are the first of them in that order. Fed builders speed their clan's wall (each adds a pair of hands; walls used to go up on a fixed clock). Headless A/B (6 seeds x 40 min, Kingdom-Age start, village jobs off vs on; off = no soldiers/healers/builders/scouts at all): alive 239 -> 244, starved 136 -> 120, wall pieces raised 360 -> 438 (+22%), cells mapped 2,832 -> 2,899, tendings 60 -> 67; killed by predators 70 -> 79 and sickness deaths 13 -> 14 (both within the noise of these runs). Builders and healers average under one per village: after the soldiers take their 60% there is rarely food to spare for more.

## 10. Risks

* **Collapse from over-hiring** → the 5.4 cap, plus a winter reserve; tune from headless sweeps.
* **Complexity in Leader decisions** (already large) → keep the new logic in its own files (`World.Villages.cs`, `World.Rations.cs`, `World.Kingdoms2.cs`), called once per Leader tick.
* **Old saves** → all new save fields nullable; first tick re-derives villages from existing homes.
* **Performance** → village/kingdom updates run at Leader-tick rate (seconds), not per frame.

## 11. Decisions

| # | Question | Decision |
|---|---|---|
| 1 | Village membership | Several clans may share a village; headman chosen among the chiefs. **Built.** |
| 2 | Leader selection | Claim-to-lead score + votes; duel only as tie-break. **Built** for headmen; kings follow the capital's headman. |
| 3 | Old clan-over-clan kingdoms | Kept for clans outside any village. **Built.** |
| 4 | Soldier food pickup | Side effect near the patrol route only (4 m). **Built.** |
| 5 | Economy harshness | Started generous (reserve 0.7 per villager, half of income spent). Tighten later from headless sweeps. |
| 6 | Scale target | **Open.** Kingdoms formed with 3-5 villages on the 100 m map; 5-6 per kingdom and larger maps are untested. |

## 12. Build notes

*Step 5 done (2026-10-02):* **Kingdoms.** `Kingdom.cs`, `World.Realms.cs`. Three or more villages within 90 m of each other and at peace (no war between any of their clans), one of them Kingdom Age, are united: the most populous Kingdom Age village is the capital, its headman is king (a new headman is a new king), and the kingdom gets a name ("Thornreach"). Villages in reach and at peace swear fealty to an existing kingdom; one at war with the capital leaves; under three villages it falls apart; a lost capital passes to the most populous village. Vassal villages send the capital a piece of food about every 30 s while their stores are above 1.5x their reserve; one soldier in three of a kingdom's villages is pledged, and marches to a sister village's alarm (`RealmAlarmFor`) after its own village's. Saved in `KingdomSave`. Shown on village tags ("capital of X" / "in X"), the clan card, the kin inspector (KING) and the stats panel. As built, formal alliances were too rare to bind three villages (3 villages, all Kingdom Age and near, had 0 allied pairs), so peace is the test, not alliance. Headless (6 seeds x 50 min, Kingdom-Age start, 24 kin): a kingdom formed in 6 of 6 worlds (3-5 villages at most), tribute 4-67 pieces per world, 1-3 soldiers pledged; kings crowned 1-4 per world, which is high turnover (the king follows the capital's headman). Not checked: that pledged soldiers really march across the map in a raid.

*Extra: spider invasions (2026-10-02).* `Wildlife/InvaderSpider.cs`, `World.Invasions.cs`. Swarms of small spiders (0.7 m across, 20 health, 5-damage bites each second) appear 30-39 m from a village and march on its middle, biting any kin outside its home or stakes. A light invasion (3-5) hits a village, a hard one (10-14) a kingdom: 40% at its capital, the rest split over its other villages. First after 7 min with a village standing, then every 5-9 min, one at a time; 40% hard once a kingdom stands. Soldiers (and a kingdom's pledged soldiers) fight them; unbeaten after 5 min they withdraw. Headless, Kingdom-Age start, 24 kin (46 by then): a light invasion (4) and a hard one (12) were both beaten in under 40 s with no kin lost, so as built they are easy for a village with soldiers; raise the counts or toughness, or scale them to the target's strength, to hurt. Not saved with the game.

*Invasions, update:* size now scales with the target's defence (a grown soldier counts 1, other grown kin 0.25): light = 3 + defence/4 (3-12), hard = 10 + defence/2 (10-30), plus or minus one. Hard invasions come in 3 waves, a light one of 6+ in 2: the next wave sets out 45 s after the last, or 8 s after it is wiped out. Headless (46-kin kingdom, 16 spiders in waves of 5, 5 and 6): all slain in about 80 s, no kin lost. Still an easy win for a well-defended kingdom; the dials are the share per defence point, spider toughness and the wave count.

# Lucky Number — GDD

Unity Game Jam 2026 · Team **Lucky Number** · Unity 6000.5.1f1 · URP 2D

## Pitch

A 2D side-view **wheel-of-fortune boss rush**. A dapper gambler-hero climbs the five
floors of a cursed casino tower and fights each floor's boss 1v1. The only input is
**SPIN**: the wheel's number is the damage dealt, and the boss must be brought to
**exactly zero**. Overshoot and the extra wraps back from max HP. It should feel like a
premium casino machine: anticipation, near-misses, jackpot payoffs.

## Platform and input (confirmed)

- **WebGL is mandatory** (jam validity; itch.io-hosted). Windows build optional.
- 16:9 landscape, reference 1920×1080, scales to any 16:9-ish window.
- Spin: left click / tap on SPIN, or Space / Enter. Click or tap during a spin to fast-forward.
- First screen is click-to-start (browser audio unlock).

## Core rules (user-supplied)

- Turn-based, 1v1, automatic apart from SPIN.
- Hero turn: spin → landed value `d` is damage.
  - `d < hp` → `hp -= d`
  - `d == hp` → boss defeated (**JACKPOT**)
  - `d > hp` → **BUST**: `hp = max − (d − hp)` (e.g. 3 left, max 40, roll 8 → 35)
- Boss turn: boss attacks the hero (damage from the balance sheet).
- Hero at 0 HP → **run over, restart from boss 1** (confirmed). Hero fully heals between bosses
  (provisional).
- Beat 8 bosses → win.

## Balance (provisional, data-driven)

- Key math: with the wrap rule, **expected spins to kill ≈ boss max HP** regardless of
  wheel values (HP is a walk on a clock; Kac's lemma). So boss HP stays small.
- Wheel: 12 equal slices, values 1–12, ordered so high and low values alternate
  (near-misses happen naturally): `12, 1, 8, 5, 10, 3, 7, 2, 11, 4, 9, 6`.
- **Hot slice (pity):** while the exact kill number is on the wheel, each miss adds
  +0.4 weight to that slice. Shown visibly: the kill slice glows hotter per miss.
  Set to 0 in the sheet to disable.
- Bosses: HP 10 / 14 / 17 / 20 / 23 / 26 / 28 / 32; damage per turn 6–7 rising to 12–13; hero base HP 20.
- Designed to be unwinnable without upgrades: a fresh save loses to the goblin ~65% of the time and
  almost always by the golem. Simulated progression (greedy shop policy, 2k players): first win at run
  ~10 (p10 7, p90 13), ~30 minutes; fully upgraded without bombs wins ~46%.
- Source of truth: `Assets/Resources/Data/bosses.csv`, `wheel.csv`, `rules.csv`, `shop.csv`
  (spreadsheet-editable). `python .playbot/tools/progression_sim.py` reads them directly.

## Progression (meta shop)

- **Gold** is earned per point of damage dealt and as a bonus per boss kill; it is **banked across runs**
  (PlayerPrefs). The **shop** (title and game-over screens) spends it on permanent upgrades and consumables.
- Items (source of truth `Assets/Resources/Data/shop.csv`):
  - **Vitality**: +15 max HP per level, 8 levels. Ace starts with only 20 HP.
  - **Loaded Die** (passive): landing 1 below the lucky number tips it over, +1 → exact kill.
  - **Lucky Horseshoe** (passive, requires the die): landing 1 off either way is pulled in, ±1.
  - **Firebomb Flask** (consumable, carry 3): free action before a spin, exactly 5 damage under the same
    exact-zero/bust rules; one per turn.
- Charms are shown on the wheel: slices they would convert get a gold tint and the charm's icon; the reveal
  shows the landed number, then the charm ticks it onto the lucky number.
- Math: with the wrap rule HP is a walk on Z_max, so kill states {0} give ~max spins; the die makes it
  {0, 1} (~half), the horseshoe {−1, 0, 1} (~a third).

## World and characters (confirmed: fantasy dungeons with gambling motifs — NOT a casino)

Style: bold stylized hand-painted 2D, clean ink outlines (Style Pack "Lucky Number — Dungeon of
Fortune", id `Kjieicz_tCXwa3s5BpFid`; target look `.playbot/docs/concepts/concept_c_dungeon.png`).

- **Setting:** five cursed depths beneath the earth; the hero runs right between them
  (side-scrolling transition through dark gaps and parallax pillars).
- **Hero — "Ace, the Masked Chevalier":** Tuxedo-Mask-style dashing masked gentleman (user request):
  white domino mask, clover top hat, black gold-trimmed tailcoat, black/crimson cape, white gloves,
  sword-cane, ace of hearts. Faces right.
- **Bosses (face left):**
  1. **Grubb the Card Goblin** — goblin jester juggling enchanted cards; The Goblin Warren.
  2. **The Loaded Golem** — golem of carved dice and amber runes; The Rune Vault.
  3. **Madame Fortuna** — fate witch with tarot and crystal ball; The Tarot Sanctum.
  4. **Snake-Eyes, the Dice Dragon** — ivory dice-scaled dragon; The Dragon's Hoard.
  5. **Mortis, the Lich of Ill Fortune** — lich king with a wheel-of-fate staff; The Throne of Fate.
- Depth order: Goblin, Golem, **Gildmaw the Hoard Mimic** (treasure-chest mimic with coin teeth; The Cursed
  Treasury; spits coins), Witch, **Sir Blackjack the Cursed Champion** (ghost-fire armor, Ace of Spades shield;
  The Hall of Wagers), Dragon, **Arachna, Weaver of Odds** (spider with a wheel-of-fate abdomen; The Silk
  Catacombs; spits venom), Lich.
- Every boss pose and flipbook faces **left** (toward the hero); sprites are never mirrored at runtime.

## Animation and rendering (user direction)

- Static generated key poses (idle/attack/hurt/run/victory) with a flashy pose-switch blend (ghost
  afterimage + white flash + squash), tween/shader idles, and VFX for everything else.
- Boss idles are real flipbooks extracted from generated videos (chroma key, feathered edges).
- URP 2D lighting with generated normal maps on characters and backgrounds; flickering lights at
  detected torches/candles/braziers; light flares on impacts, spells, jackpots.
- Animated backgrounds: generated ambient video loops streamed from StreamingAssets (WebGL-safe),
  lit with the still's normal map; the still is the instant fallback.
- Juice (user: "sensory overload"): hitstop, slow-mo with music pitch-down, impact frames,
  trauma shake + kicks + zoom punches, bloom/chromatic/lens punches, coin storms into the counter,
  confetti, rings, sparks, count-ups, slam-in banners. Shake/flash toggles in Options.

## Presentation and feel

- Palette: weathered stone, torch orange, crimson and gold accents, cool magic glows;
  each depth gets a dominant accent.
- Layout (16:9): hero left, boss right, both mid-height; the **prize wheel** sits bottom-center
  like a game-show stage prop (top ~60% visible, pointer at top). HUD: hero HP top-left, boss
  plate and HP top-right, floor counter top-center, chips counter, big SPIN button bottom-right.
- Wheel slice colors encode outcome before a spin: normal damage, **kill slice** (gold + crown
  icon), **bust slices** (dim red + "BUST" tick). Never color alone.

### Wheel feel spec (from research)

- Result chosen at press, animated to target: 4–6 turns, ~3.2 s ease-out, flapper physics
  (deflects per peg, springs back), occasional final-peg creep (≤35% of spins), short settle.
  Wheel moves on the press frame.
- One click per peg, ±5% pitch; pitch rises as the kill slice nears the pointer.
- Chase bulbs ~20 ms step while spinning; staged light build when a kill is possible.
- Click/tap fast-forwards a spin. SPIN button pulses after 4 s idle.
- Celebration scales with outcome: normal hit < big hit (≥9) < JACKPOT kill (hitstop,
  slow-mo, flash, coin burst, count-up, stinger in key with music).
- BUST is a spectacle, not a sad sound: rewind whoosh, HP bar visibly wraps and refills.
- Hitstop 3–12 frames scaled by damage, camera kick toward hit, boss squash/flash.
- Accessibility: text ≥28 px at 1080p (key numbers ≥52 px), 4.5:1 contrast, ≤3 flashes/s
  over large areas, low adjacent-slice contrast at speed; toggles for screen shake and flash.

## Audio

- Every SFX is built from 2–4 generated layers, mixed and mastered offline.
- Music: title lounge theme, battle loop (~130 BPM casino electro-swing), final-boss loop,
  victory and game-over stingers in the same key (C minor / E♭ major).

## UI rules (summary of research)

Bold, simple HUD over rich art; display font (Cinzel Decorative) for titles/boss names only,
Lilita One for numbers/labels; button feedback ≤100 ms; panels 200–300 ms in, 200 ms out;
ease-out in, ease-in out, never linear; information the player needs never auto-fades
(boss HP, "needs exactly N").

## First milestone scope

Included: title screen, 5-boss run, spin/attack/bust/jackpot loop, hero and boss attacks,
death → game over → restart, victory screen, banked chips, music + layered SFX, settings
(volume, shake/flash toggles), WebGL build.

Deferred: unlock tree, weapons, items, bonuses, multiple worlds.

### Completion criteria

- WebGL build boots in a browser, title → run → all 5 bosses → victory, and death → restart.
- Exact-zero, bust-wrap and boss damage follow the rules above.
- Wheel spin has ticks, flapper, lights, near-miss creep, jackpot celebration.
- No console errors; 60 fps on a mid laptop in the browser.

## Visual references

Concepts under `.playbot/docs/concepts/`: `concept_c_dungeon.png` is the adopted target look
(casino concepts A/B superseded by the user's fantasy direction).

## Currency

Chips became **gold** (fantasy). Gold earned per hit and per boss bonus; banked across runs.

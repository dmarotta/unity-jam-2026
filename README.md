# Lucky Number

Unity Game Jam 2026 · Team **Lucky Number** · Unity 6000.5.1f1 (URP 2D) · **WebGL**

A wheel-of-fortune boss rush. Press **SPIN** (Space / click / tap). The number you land is your damage,
and the boss must hit **exactly 0**. Overshoot and its HP wraps back from max. Beat eight bosses to break the
curse; dying restarts the run, and the gold you earned is banked. Spend it in the **shop** between runs on max HP,
lucky charms that nudge the wheel onto the lucky number, and firebombs. You start too weak to win without them.

## Project layout

| Path | What |
| --- | --- |
| `Assets/Scenes/Main.unity` | The only scene (built by the builder below) |
| `Assets/Scripts/Core` | Balance loader, rules (`FightMath`), save data, tweens/easing, asset registry |
| `Assets/Scripts/Game` | `GameDirector` (run flow), `PrizeWheel`, `Fighter`, `World`, `FX`, `Juice`, `CameraRig`, `PostFX`, `AudioManager`, `Flipbook` |
| `Assets/Scripts/UI` + `Assets/UI` | UI Toolkit HUD (`Main.uxml` / `Main.uss`) |
| `Assets/Scripts/Editor/LuckyBuilder.cs` | **Lucky ▸ Build All**: import settings, normal maps, registry, fonts, post FX, lights, scene |
| `Assets/Resources/Data/*.csv` | **Balance spreadsheets**: bosses, wheel slots, rules, shop items |
| `Assets/Resources/Data/lights.json` | Detected torch/candle light positions per arena |
| `Assets/Resources/Anim` | Boss idle flipbooks (sheet + normal sheet + JSON) |
| `Assets/StreamingAssets/Video` | Looping animated backdrops (streamed; WebGL-safe) |
| `.playbot/docs/gdd.md` | Game design document |
| `.playbot/tools` | Python pipelines (below) |

## Tools (`.playbot/tools`)

- `progression_sim.py`: Monte-Carlo of the meta progression from the CSVs (runs to first win, where runs end, shop policy).
- `balance_sim.py`: Monte-Carlo of single runs straight from the CSVs (win rate, deaths per boss; ignores the shop).
- `extract_frames.py`: chroma-key a generated video into a flipbook sheet + metadata.
- `make_normals.py`: normal maps for sprites/backgrounds + light detection → `lights.json`.
- `loop_backgrounds.py`: seamless, web-friendly backdrop loops.
- `mix_sfx.py` + `sfx_recipes.json`: layered SFX mixer (every sound is 2–4 generated layers).
- `make_portraits.py`, `make_procedural.py`, `remap_rim.py`: HUD portraits, procedural VFX textures, rim art.

After changing art, audio or data, run **Lucky ▸ Build All** in Unity, then build for **WebGL**.

## Credits

Fonts: Lilita One and Cinzel Decorative (SIL Open Font License, see `Assets/Fonts`).
Art, animation, music and SFX generated with Playbot.

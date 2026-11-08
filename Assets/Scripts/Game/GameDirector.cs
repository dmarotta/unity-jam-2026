using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Lucky
{
    /// <summary>Owns the run: title → depths (intro, spin/attack turns, boss turns) → victory or defeat.</summary>
    public class GameDirector : MonoBehaviour
    {
        public GameConfig config;
        public Volume volume;

        const float WheelY = -5.7f, WheelHiddenY = -12.5f;
        const float MinLoad = 1.4f, MaxLoad = 8f;
        static readonly Color Gold = new(1f, 0.82f, 0.35f);

        HUD hud;
        World world;
        PrizeWheel wheel;
        Transform wheelRoot;
        Fighter hero;
        readonly List<Fighter> bosses = new();
        readonly List<Sprite> bossPortraits = new();
        Camera cam;
        readonly System.Random rng = new();

        int depth, heroHp, heroMax, bossHp, misses, runGold, spins, jackpots, busts;
        bool inRun, awaitingSpin, spinRequested, slipped, warded;
        float spinStartedAt, lastTick;
        Coroutine runRoutine;

        // Loadout bought in the shop, read at the start of each run.
        Stats stats;
        ShopItem charm;
        List<ShopItem> consumables = new();
        readonly Dictionary<ShopItem, int> charges = new();
        ShopItem itemRequested;
        int cloverWeight, revives;
        SpriteRenderer wardBubble;

        // Boss quirks: the Lich's spare life, Arachna's poison stacks, Snake-Eyes' burning slices (turns left per slice).
        bool phylactery;
        int poison, turn;
        int[] burnTurns;
        float emberTimer;

        // Madame Fortuna's card this turn: the wheel's values (The Tower doubles them), Justice, The Star's pull,
        // Wheel of Fortune's second spin, and the sting added to her next attack.
        int[] wheelValues;
        bool noBust, spinTwice;
        int starWeight, sting;

        /// <summary>Short-lived effect objects owned by run coroutines; destroyed on reset in case a quit cut them short.</summary>
        readonly List<GameObject> transients = new();

        BossDef Boss => Balance.Bosses[depth];
        Fighter BossFighter => bosses[depth];

        /// <summary>Why an item can't be used right now, or null if it can: any number per turn, before the spin, and only when it would do something.</summary>
        string Blocker(ShopItem item) =>
            charges.GetValueOrDefault(item) == 0 ? "None left this run"
            : !awaitingSpin ? "Wait for your turn"
            : item.Stat switch
            {
                "potion" when heroHp >= heroMax && poison == 0 => "Already at full HP",
                "ward" when warded => "Ward already up",
                _ => null,
            };

        bool CanUse(ShopItem item) => Blocker(item) == null;

        // ---------- Setup ----------

        void Awake()
        {
            Application.targetFrameRate = 60;
            Balance.Load();
            cam = Camera.main;
            gameObject.AddComponent<Juice>();
            gameObject.AddComponent<AudioManager>().Init(config);
            gameObject.AddComponent<FX>().Init(config);
            gameObject.AddComponent<PostFX>().Init(volume);
            cam.gameObject.AddComponent<CameraRig>();

            world = new GameObject("World").AddComponent<World>();
            world.Build(config, Balance.Bosses);

            hero = Fighter.Create("Hero", world.transform, config, new Dictionary<Pose, Sprite>
            {
                { Pose.Idle, S("hero_idle") }, { Pose.Attack, S("hero_attack") }, { Pose.Hurt, S("hero_hurt") },
                { Pose.Run, S("hero_run") }, { Pose.Victory, S("hero_victory") },
            }, new Dictionary<Pose, string>
            {
                { Pose.Idle, "hero_idle" }, { Pose.Attack, "hero_attack" }, { Pose.Hurt, "hero_hurt" },
                { Pose.Run, "hero_run" }, { Pose.Victory, "hero_victory" }, { Pose.Death, "hero_death" },
            }, 4.7f, 30);

            // Hand-held lantern glow: as the hero runs and lunges it rakes across the arena walls.
            var lantern = Instantiate(config.lightPrefab, hero.transform).GetComponent<Light2D>();
            lantern.transform.localPosition = new Vector3(0.7f, hero.Height * 0.7f, 0f);
            lantern.color = new Color(1f, 0.72f, 0.42f);
            lantern.intensity = 0.8f;
            lantern.pointLightOuterRadius = 5f;

            for (int i = 0; i < Balance.Bosses.Count; i++)
            {
                var def = Balance.Bosses[i];
                var boss = Fighter.Create(def.Id, world.transform, config, new Dictionary<Pose, Sprite>
                {
                    { Pose.Idle, S(def.IdleSprite) }, { Pose.Attack, S(def.AttackSprite) }, { Pose.Hurt, S(def.HurtSprite) },
                }, new Dictionary<Pose, string> { { Pose.Idle, def.Id + "_idle" } }, def.Height, 20);
                boss.Home = new Vector3(World.ArenaX(i) + World.BossX + def.OffsetX, World.GroundY);
                boss.transform.position = boss.Home;
                boss.Accent = def.Accent;
                bosses.Add(boss);
                bossPortraits.Add(S("portrait_" + def.Id));
            }

            wheelRoot = new GameObject("WheelRoot").transform;
            wheelRoot.SetParent(cam.transform, false);
            wheelRoot.localPosition = new Vector3(0f, WheelHiddenY, 10f);
            wheel = new GameObject("Wheel").AddComponent<PrizeWheel>();
            wheel.transform.SetParent(wheelRoot, false);
            wheel.Build(config, Balance.Wheel);
            wheelValues = Balance.Wheel;
            burnTurns = new int[Balance.Wheel.Length];
            wheel.PegCrossed += OnPeg;
            wheel.Teased += OnTeased;
            wheel.Slipped += OnSlipped;

            // Warm key light drifting with the camera, and a gold underlight from the wheel that swells as it spins.
            keyLight = Instantiate(config.lightPrefab, cam.transform).GetComponent<Light2D>();
            keyLight.color = new Color(1f, 0.85f, 0.7f);
            keyLight.intensity = 0.6f;
            keyLight.pointLightOuterRadius = 13f;
            wheelLight = Instantiate(config.lightPrefab, wheelRoot).GetComponent<Light2D>();
            wheelLight.transform.localPosition = new Vector3(0f, 4.6f, 0f);
            wheelLight.color = new Color(1f, 0.78f, 0.4f);
            wheelLight.pointLightOuterRadius = 8f;
        }

        Light2D wheelLight, keyLight;

        void LateUpdate()
        {
            // Slow sweep so the normal-mapped relief visibly rolls across the arena.
            float drift = Time.time;
            keyLight.transform.localPosition = new Vector3(Mathf.Sin(drift * 0.31f) * 6f, 1.5f + Mathf.Sin(drift * 0.53f) * 1.8f, 10f);
            float spin = Mathf.InverseLerp(0f, 1200f, wheel.Speed);
            wheelLight.intensity = 0.35f + spin * 1.1f + (wheel.KillSlice >= 0 ? 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f)) : 0f);
            if (wardBubble != null) wardBubble.transform.localScale = Vector3.one * hero.Height * (0.62f + 0.04f * Mathf.Sin(Time.time * 6f)) / wardBubble.sprite.bounds.size.x;

            // Lingering status: embers off scorched slices, venom bubbles off a poisoned hero.
            emberTimer -= Time.deltaTime;
            if (emberTimer > 0f) return;
            emberTimer = 0.07f;
            for (int i = 0; i < burnTurns.Length; i++)
                if (burnTurns[i] > 0) Flames(wheel.SlicePoint(i, Random.Range(0.55f, 0.95f)), FireColor, 1, 0.5f);
            if (poison > 0 && Random.value < 0.5f) Flames(hero.transform.position + Vector3.up * hero.Height * Random.Range(0.1f, 0.7f), VenomColor, 1, 0.6f);
        }

        /// <summary>Additive blobs licking upward: fire off burnt slices and burns, bubbles off poison.</summary>
        static void Flames(Vector3 pos, Color color, int count, float power) => FX.I.Emit(pos, new FX.Burst
        {
            Sprite = "vfx_glow", Count = count, SpeedMin = 1f * power, SpeedMax = 4f * power, AngleMin = 70f, AngleMax = 110f,
            Gravity = 2f, Drag = 1.5f, LifeMin = 0.4f, LifeMax = 0.8f, SizeMin = 0.25f, SizeMax = 0.6f + 0.4f * power, EndSize = 0.1f,
            Color = color, EndColor = new Color(color.r * 0.8f, color.g * 0.2f, color.b * 0.1f, 0f), Additive = true,
            Spread = new Vector2(0.3f, 0.15f) * (1f + power),
        });

        static readonly Color FireColor = new(1f, 0.5f, 0.1f), VenomColor = new(0.55f, 1f, 0.3f), TarotColor = new(0.78f, 0.49f, 1f);
        static readonly Color SoulColor = new(0.36f, 1f, 0.69f), StoneColor = new(0.85f, 0.78f, 0.68f), HealColor = new(0.5f, 1f, 0.6f);

        Sprite S(string name) => config.Sprite(name);

        void Start()
        {
            // UIDocument builds its tree in OnEnable, so the HUD binds here rather than in Awake.
            hud = GetComponent<HUD>();
            hud.Init(config);
            hud.SpinPressed += () => { if (awaitingSpin) spinRequested = true; };
            hud.ItemPressed += item => { if (CanUse(item)) itemRequested = item; };
            hud.PlayPressed += BeginRun;
            hud.RetryPressed += BeginRun;
            hud.MenuPressed += ShowTitle;
            hud.QuitPressed += QuitRun;
            StartCoroutine(Boot());
        }

        /// <summary>Holds the loading screen until the opening arena's video and the title music are ready, then reveals the title.</summary>
        IEnumerator Boot()
        {
            CameraRig.I.SnapTo(World.ArenaX(0));
            var music = config.Clip("music_title");
            if (music != null) music.LoadAudioData();
            float start = Time.unscaledTime, shown = 0f;
            while (shown < 1f)
            {
                float elapsed = Time.unscaledTime - start;
                bool musicReady = music == null || music.loadState != AudioDataLoadState.Loading;
                float ready = (world.BackdropProgress + (musicReady ? 1f : 0f)) * 0.5f;
                // The bar never outruns the real work, never flashes past in under MinLoad, and gives up waiting at MaxLoad.
                float target = elapsed >= MaxLoad ? 1f : Mathf.Min(ready, elapsed / MinLoad);
                shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 1.5f);
                hud.SetLoading(shown);
                yield return null;
            }
            yield return Tween.Wait(0.15f, true);
            ShowTitle();
            AudioManager.I.Play("whoosh_ui", 0.6f);
            yield return hud.HideLoading();
        }

        void ShowTitle()
        {
            if (runRoutine != null) StopCoroutine(runRoutine);
            inRun = false;
            hud.HideEnd();
            hud.ShowHud(false);
            ResetStage();
            hero.transform.position = new Vector3(World.ArenaX(0) + World.HeroX, World.GroundY);
            hud.ShowTitle(true);
            AudioManager.I.PlayMusic("music_title");
        }

        void ResetStage()
        {
            depth = 0;
            CameraRig.I.SnapTo(World.ArenaX(0));
            wheelRoot.localPosition = new Vector3(0f, WheelHiddenY, 10f);
            hero.ResetVisuals();
            if (wardBubble != null) Destroy(wardBubble.gameObject);
            foreach (var go in transients) if (go != null) Destroy(go);
            transients.Clear();
            hero.Home = new Vector3(World.ArenaX(0) + World.HeroX, World.GroundY);
            for (int i = 0; i < bosses.Count; i++)
            {
                bosses[i].ResetVisuals();
                bosses[i].transform.position = bosses[i].Home;
            }
            PostFX.I.SetDanger(0f);
            AudioManager.I.Loop("heartbeat", false);
            AudioManager.I.Loop("run_steps", false);
            ClearQuirks();
        }

        /// <summary>Poison and scorched slices end with the fight; Madame Fortuna's card ends with its turn.</summary>
        void ClearQuirks()
        {
            poison = 0;
            hud.SetPoison(0);
            for (int i = 0; i < burnTurns.Length; i++)
            {
                if (burnTurns[i] == 0) continue;
                burnTurns[i] = 0;
                wheel.SetBurnt(i, false);
            }
            ClearWheelRules();
            sting = 0;
            hud.ShowActiveCard(null);
        }

        /// <summary>The card's hold on the wheel wears off once the spin's number is in: plain values, busts back on.</summary>
        void ClearWheelRules()
        {
            starWeight = 0;
            spinTwice = false;
            if (!noBust && wheelValues == Balance.Wheel) return;
            noBust = wheel.NoBust = false;
            wheelValues = Balance.Wheel;
            wheel.SetValues(wheelValues);
        }

        // ---------- Input ----------

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var touch = Touchscreen.current;
            bool confirm = kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame);
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame || touch != null && touch.primaryTouch.press.wasPressedThisFrame;

            bool back = kb != null && kb.escapeKey.wasPressedThisFrame;
            if (!inRun)
            {
                if (back) hud.Back();
                else if (confirm) hud.Confirm();
                return;
            }
            if (back) hud.ShowSettings(!hud.SettingsOpen);
            if (hud.SettingsOpen) return;
            if (hud.PickingCard)
            {
                for (int i = 0; kb != null && i < 9; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame) hud.ChooseCard(i);
                return;
            }
            if (awaitingSpin && confirm) spinRequested = true;
            for (int i = 0; kb != null && i < consumables.Count && i < 9; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame && CanUse(consumables[i])) itemRequested = consumables[i];
            if (wheel.Spinning && Time.unscaledTime - spinStartedAt > 0.2f && (confirm || click)) wheel.FastForward();
        }

        // ---------- Run flow ----------

        void BeginRun()
        {
            if (runRoutine != null) StopCoroutine(runRoutine);
            runRoutine = StartCoroutine(Run());
        }

        /// <summary>Pause menu → Quit to menu: abandon the run mid-whatever, keep the gold earned so far.</summary>
        void QuitRun()
        {
            StopAllCoroutines();
            runRoutine = null;
            StartCoroutine(QuitToTitle());
        }

        IEnumerator QuitToTitle()
        {
            inRun = false;
            SetAwaitingSpin(false);
            hud.ShowSettings(false);
            yield return hud.Fade(1f, 0.3f);
            BankRun();
            wheel.SetLights(PrizeWheel.Lights.Idle);
            ShowTitle();
            yield return hud.Fade(0f, 0.45f);
        }

        IEnumerator Run()
        {
            hud.ShowTitle(false);
            hud.ShowShop(false);
            hud.HideEnd();
            yield return hud.Fade(1f, 0.25f);
            ResetStage();
            inRun = true;
            LoadLoadout();
            heroHp = heroMax;
            runGold = spins = jackpots = busts = 0;
            SaveData.Runs++;
            hud.SetGold(0);
            hud.SetHeroHp(heroHp, heroMax, false);
            hud.SetCharm(charm != null ? S(charm.Icon) : null, stats.CharmAbove > 0 ? "±1" : "+1");
            hud.BuildItems(consumables);
            hud.RefreshItems(Blocker, charges);
            hero.transform.position = new Vector3(World.ArenaX(0) - OffscreenX, World.GroundY);
            AudioManager.I.PlayMusic("music_battle");
            yield return hud.Fade(0f, 0.35f);

            for (depth = 0; depth < bosses.Count; depth++)
            {
                yield return EnterArena();
                yield return Fight();
                if (heroHp <= 0)
                {
                    yield return Defeat();
                    yield break;
                }
                yield return DepthCleared();
            }
            yield return Victory();
        }

        /// <summary>Stats from shop levels, the strongest owned charm, and the consumables, restocked to their bought level.</summary>
        void LoadLoadout()
        {
            stats = Stats.Current();
            heroMax = stats.MaxHp;
            charm = Balance.Shop.FindLast(i => i.Stat is "charm_below" or "charm_above" && i.Level > 0);
            consumables = Balance.Shop.FindAll(i => i.Consumable);
            charges.Clear();
            foreach (var item in consumables) charges[item] = Mathf.Min(item.Level, item.Max);
            warded = false;
            cloverWeight = 0;
            revives = stats.Revives;
        }

        /// <summary>Can any wheel value (after charms) land exactly on the boss's HP?</summary>
        bool Killable()
        {
            foreach (int v in wheelValues)
                if (FightMath.Charm(v, bossHp, stats.CharmBelow, stats.CharmAbove) == bossHp) return true;
            return false;
        }

        void RefreshTarget()
        {
            wheel.SetTarget(bossHp, misses + cloverWeight + starWeight, stats.CharmBelow, stats.CharmAbove, charm?.Icon);
            hud.SetNeed(bossHp, Killable());
        }

        IEnumerator EnterArena()
        {
            var def = Boss;
            bossHp = def.Hp;
            misses = turn = 0;
            phylactery = def.Quirk == "phylactery";
            ClearQuirks();
            hud.SetBossInfo(def);
            hud.SetPortraits(S("portrait_hero"), bossPortraits[depth]);
            hud.SetBossHp(bossHp, def.Hp, false);
            hud.SetDepth(depth, bossPortraits);
            RefreshTarget();
            hero.Home = new Vector3(World.ArenaX(depth) + World.HeroX, World.GroundY);

            // The hero waits just off the left edge and runs in once the arena is on screen.
            yield return RunTo(hero.Home, 0.75f, Ease.OutQuad, true);
            if (depth == bosses.Count - 1) AudioManager.I.PlayMusic("music_final", 0.8f);

            // Boss intro: roar, accent flash, slam-in name card.
            var boss = BossFighter;
            hud.ShowHud(true);
            AudioManager.I.Play("boss_intro", 0.9f);
            BossVoice("intro", 0.9f);
            boss.SetPose(Pose.Attack, 0.7f);
            boss.Flash(def.Accent, 0.9f);
            boss.Squash(0.8f, 1.25f);
            FX.I.Ring(boss.Chest, def.Accent, 9f, 0.6f);
            FX.I.Sparks(boss.Chest, def.Accent, 30, 1.4f);
            FX.I.Light(boss.Chest, def.Accent, 3.5f, 10f, 1.2f);
            CameraRig.I.Shake(0.45f);
            CameraRig.I.ZoomPunch(-0.05f);
            PostFX.I.Punch(bloomAdd: 1.5f, chromaAdd: 0.35f, lensAdd: -0.2f);
            hud.Banner(def.Name.ToUpperInvariant(), $"Depth {depth + 1}  ·  {def.Arena}", HUD.BannerStyle.Intro, 1.25f);
            StartCoroutine(hud.PulseTraits());
            yield return new WaitForSeconds(1.5f);
            yield return ShowWheel(true);
        }

        IEnumerator Fight()
        {
            while (true)
            {
                turn++;
                sting = 0;
                hud.ShowActiveCard(null);
                if (Boss.Quirk == "tarot" && (turn - 1) % Boss.QuirkValue == 0) yield return TarotDraw();

                spinRequested = false;
                itemRequested = null;
                float readyAt = SetAwaitingSpin(true);
                while (!spinRequested)
                {
                    if (itemRequested != null)
                    {
                        var item = itemRequested;
                        itemRequested = null;
                        charges[item]--;
                        SetAwaitingSpin(false);
                        yield return UseItem(item);
                        if (bossHp == 0) yield break;
                        yield return TryRevive();
                        if (heroHp <= 0) yield break;
                        readyAt = SetAwaitingSpin(true);
                    }
                    if (SaveData.AutoSpin && !hud.SettingsOpen && Time.unscaledTime - readyAt > 0.6f) spinRequested = true;
                    yield return null;
                }
                SetAwaitingSpin(false);

                yield return SpinTurn();
                if (bossHp == 0) yield break;
                yield return TryRevive();
                if (heroHp <= 0) yield break;

                yield return BossTurn();
                hud.ShowActiveCard(null);
                yield return TryRevive();
                if (heroHp <= 0) yield break;
            }
        }

        /// <summary>Phoenix Feather: a hero who just fell rises if a revive is left (a counter-attack can fell them mid-turn).</summary>
        IEnumerator TryRevive()
        {
            if (heroHp > 0 || revives == 0) yield break;
            revives--;
            yield return Revive();
        }

        // ---------- Madame Fortuna's tarot ----------

        /// <summary>She deals two different cards; the player must take one, and it plays at once.</summary>
        IEnumerator TarotDraw()
        {
            var boss = BossFighter;
            var deck = Balance.Tarot;
            int a = rng.Next(deck.Count), b = (a + 1 + rng.Next(deck.Count - 1)) % deck.Count;
            AudioManager.I.Play("boss_attack_magic", 0.6f, 1.3f);
            BossVoice("attack", 0.8f);
            boss.SetPose(Pose.Attack, 0.7f);
            boss.Flash(TarotColor, 0.8f);
            boss.Squash(1.1f, 0.92f);
            FX.I.Cards(boss.Chest, 10);
            FX.I.Sparks(boss.Chest, TarotColor, 30, 1.3f);
            FX.I.Light(boss.Chest, TarotColor, 4f, 9f, 0.8f);
            yield return new WaitForSeconds(0.35f);

            TarotCard card = null;
            yield return hud.PickCard(new[] { deck[a], deck[b] }, c => card = c);
            hud.ShowActiveCard(card);
            yield return PlayCard(card);
        }

        IEnumerator PlayCard(TarotCard card)
        {
            var at = wheel.PointerTip + Vector3.up * 1.8f;
            FX.I.Pop(card.Sprite, at, 0.5f, 2.6f, 0.8f, Color.white, 0f, false);
            FX.I.Ring(at, TarotColor, 10f, 0.5f);
            FX.I.Sparks(at, TarotColor, 36, 1.4f, true);
            FX.I.Light(at, TarotColor, 4f, 9f, 0.6f);
            wheel.Flare(TarotColor, 1f);
            wheel.Punch(0.1f);
            CameraRig.I.Shake(0.25f);
            PostFX.I.Punch(bloomAdd: 1.3f, chromaAdd: 0.35f);
            var caption = at + Vector3.up * 1.6f;
            switch (card.Id)
            {
                case "tower":
                    sting = card.Cost;
                    wheelValues = System.Array.ConvertAll(Balance.Wheel, v => v * card.Gain);
                    wheel.SetValues(wheelValues);
                    AudioManager.I.Play("hit_heavy", 0.9f, 0.7f);
                    FX.I.Sparks(wheel.Center, Gold, 50, 2f, true);
                    FX.I.Text(caption, $"×{card.Gain}!", Gold, 3f, 1f, 1.2f, true);
                    break;
                case "justice":
                    noBust = wheel.NoBust = true;
                    AudioManager.I.Play("hp_upgrade", 0.9f, 0.9f);
                    FX.I.Text(caption, "NO BUST!", TarotColor, 2.2f, 1f, 1.2f, true);
                    HealBoss(card.Cost);
                    break;
                case "star":
                    starWeight = card.Gain;
                    AudioManager.I.Play("charm_nudge", 1f, 1.15f);
                    wheel.SetLights(PrizeWheel.Lights.Jackpot, 1.2f);
                    FX.I.Text(caption, "FEELING LUCKY!", Gold, 1.8f, 1f, 1.2f, true);
                    Singe(card.Cost, TarotColor, "");
                    break;
                case "temperance":
                    if (heroHp < heroMax) HealHero(Mathf.Min(card.Gain, heroMax - heroHp), HealColor);
                    HealBoss(card.Cost);
                    break;
                case "wheel":
                    spinTwice = true;
                    sting = card.Cost;
                    AudioManager.I.Play("spin_start", 0.8f, 1.2f);
                    FX.I.Text(caption, "SPIN TWICE!", Gold, 2.2f, 1f, 1.2f, true);
                    break;
            }
            RefreshTarget();
            yield return new WaitForSeconds(0.75f);
        }

        /// <summary>A boss mends (Justice, Temperance): green sparkles rise and the bar climbs.</summary>
        void HealBoss(int amount)
        {
            int healed = Mathf.Min(amount, Boss.Hp - bossHp);
            if (healed <= 0) return;
            var boss = BossFighter;
            bossHp += healed;
            hud.SetBossHp(bossHp, Boss.Hp, true);
            RefreshTarget();
            AudioManager.I.Play("hp_refill", 0.8f, 1.1f);
            boss.Flash(HealColor, 0.8f);
            boss.Squash(1.1f, 0.92f);
            FX.I.Emit(boss.transform.position + Vector3.up * 0.3f, new FX.Burst
            {
                Sprite = "vfx_sparkle", Count = 30, SpeedMin = 2f, SpeedMax = 6f, AngleMin = 75f, AngleMax = 105f,
                Gravity = 2f, Drag = 1f, LifeMin = 0.6f, LifeMax = 1.2f, SizeMin = 0.25f, SizeMax = 0.55f, EndSize = 0f,
                Spin = 200f, Color = HealColor, EndColor = Color.white, Additive = true, Spread = new Vector2(1.2f, 0.2f),
            });
            FX.I.Text(boss.Head + Vector3.up * 0.6f, "+" + healed, HealColor, 1.8f, 1.3f, 1.1f, true);
        }

        /// <summary>Damage the hero pays for something (The Star, a scorched slice) rather than takes in a fight; never lethal.</summary>
        void Singe(int amount, Color color, string label)
        {
            int dmg = Mathf.Min(amount, heroHp - 1);
            if (dmg <= 0) return;
            heroHp -= dmg;
            hud.SetHeroHp(heroHp, heroMax, true);
            AudioManager.I.Play("hero_hurt", 0.8f, 1.1f);
            hero.SetPose(Pose.Hurt, 0.4f);
            hero.Flash(color, 1f);
            hero.Squash(0.85f, 1.12f);
            hero.Knock(-3f);
            FX.I.Sparks(hero.Chest, color, 24, 1.2f);
            FX.I.Text(hero.Head + Vector3.up * 0.6f, $"-{dmg} {label}".TrimEnd(), color, 1.7f, 1.3f, 1.1f, true);
            CameraRig.I.Shake(0.3f);
            UpdateDanger();
        }

        /// <summary>Open or close the player's turn window; returns when it opened (for auto-spin).</summary>
        float SetAwaitingSpin(bool on)
        {
            awaitingSpin = on;
            hud.SetSpinReady(on, on ? "SPIN" : "···");
            hud.RefreshItems(Blocker, charges);
            return Time.unscaledTime;
        }

        // ---------- Spin turn ----------

        IEnumerator SpinTurn()
        {
            spins++;
            var def = Boss;
            bool killable = Killable();
            float luck = stats.PityPerMiss * misses + cloverWeight + starWeight;
            int target = FightMath.PickSlice(rng, wheelValues, bossHp, luck);
            if (killable) cloverWeight = 0; // banked clovers wait until the Lucky Number is on the wheel
#if UNITY_EDITOR
            int forced = forcedValue >= 0 ? System.Array.IndexOf(wheelValues, forcedValue == 0 ? bossHp : forcedValue) : -1;
            if (forced >= 0) target = forced;
            forcedValue = -1;
#endif
            yield return SpinWheel(target);
            if (spinTwice) yield return SpinAgain(target, luck, t => target = t);

            int landed = wheelValues[target];
            bool blocked = def.Quirk == "block" && rng.NextDouble() < def.QuirkChance;
            int afterBlock = blocked ? Mathf.Max(0, landed - def.QuirkValue) : landed;
            int value = FightMath.Charm(afterBlock, bossHp, stats.CharmBelow, stats.CharmAbove);
            bool charmed = value != afterBlock;
            var result = Preview(afterBlock);
            bool swallowed = def.Quirk == "swallow" && value > 0 && rng.NextDouble() < def.QuirkChance;
            bool dodged = !swallowed && rng.NextDouble() < def.Dodge;
            bool big = value >= 9 || result.Outcome == HitOutcome.Jackpot;

            bool nearMiss = result.Outcome != HitOutcome.Jackpot && wheel.KillSlice >= 0 && SliceDistance(target, wheel.KillSlice) == 1;
            if (nearMiss && !slipped)
            {
                AudioManager.I.Play("near_miss", 0.9f);
                FX.I.Text(wheel.PointerTip + Vector3.up * 2.4f, "SO CLOSE!", new Color(1f, 0.55f, 0.75f), 0.9f, 0.8f, 1.2f, true);
            }

            // Number reveal: pops out of the pointer and hangs above the wheel. A blocked or charmed number is revealed as landed first.
            AudioManager.I.Play(big ? "reveal_big" : "reveal_small", 1f, 0.9f + landed * 0.02f);
            var revealColor = charmed || blocked ? new Color(1f, 0.97f, 0.88f) : result.Outcome switch
            {
                HitOutcome.Jackpot => Gold,
                HitOutcome.Bust => new Color(1f, 0.4f, 0.5f),
                HitOutcome.Miss => TarotColor,
                _ => new Color(1f, 0.97f, 0.88f),
            };
            FX.I.Text(wheel.PointerTip + Vector3.up * 1.3f, landed.ToString(), revealColor, big ? 3.2f : 2.4f, 0.6f, 0.9f, big);
            FX.I.Pop("vfx_glow", wheel.PointerTip + Vector3.up * 1.3f, 1f, 5f, 0.45f, revealColor);
            if (blocked) yield return StoneBlock(landed, afterBlock);
            if (charmed) yield return CharmNudge(afterBlock, value);
            if (burnTurns[target] > 0) yield return Scorched(def.QuirkValue);
            if (result.Outcome == HitOutcome.Jackpot && !dodged) yield return LuckyReveal();
            else yield return new WaitForSeconds(0.32f);

            ClearWheelRules();
            yield return HeroAttack(result, killable, dodged, swallowed);
        }

        /// <summary>What a wheel number would do to the boss right now, after charms and this turn's card.</summary>
        HitResult Preview(int value) => FightMath.Apply(bossHp, Boss.Hp, FightMath.Charm(value, bossHp, stats.CharmBelow, stats.CharmAbove), !noBust);

        /// <summary>Wheel of Fortune: the first number hangs to the left, the wheel spins again, and the better result is kept.</summary>
        IEnumerator SpinAgain(int first, float luck, System.Action<int> keep)
        {
            var held = wheel.PointerTip + new Vector3(-3.6f, 1.6f);
            AudioManager.I.Play("card_deal", 0.9f, 0.9f);
            FX.I.Text(held, wheelValues[first].ToString(), Color.white, 2.8f, 0.3f, 2.9f, true);
            FX.I.Pop("vfx_glow", held, 1f, 4f, 0.5f, TarotColor);
            FX.I.Text(wheel.PointerTip + Vector3.up * 2.4f, "AGAIN!", TarotColor, 1.8f, 0.9f, 1f, true);
            yield return new WaitForSeconds(0.45f);

            int second = FightMath.PickSlice(rng, wheelValues, bossHp, luck);
            yield return SpinWheel(second);
            int kept = FightMath.Better(Preview(wheelValues[second]), Preview(wheelValues[first])) ? second : first;
            var at = kept == first ? held : wheel.PointerTip + Vector3.up * 1.6f;
            AudioManager.I.Play("card_pick", 0.8f);
            wheel.HighlightWin(kept);
            FX.I.Ring(at, Gold, 7f, 0.45f);
            FX.I.Sparks(at, Gold, 30, 1.3f, true);
            FX.I.Text(wheel.PointerTip + Vector3.up * 2.6f, $"KEEP {wheelValues[kept]}!", Gold, 2.2f, 1f, 1.2f, true);
            keep(kept);
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>One spin of the wheel onto <paramref name="target"/>, with all its drama, up to the clack of the stop.</summary>
        IEnumerator SpinWheel(int target)
        {
            var drama = ChooseDrama(target);

            AudioManager.I.Play("spin_start");
            hud.DimNeed(true);
            wheel.SetLights(PrizeWheel.Lights.Spin);
            wheel.Punch(0.07f);
            wheel.Flare(Gold, 0.6f);
            CameraRig.I.Kick(Vector2.down, 0.06f);
            FX.I.Sparks(wheel.PointerTip, Gold, 12, 0.8f);
            FX.I.Light(wheel.PointerTip, Gold, 2f, 6f, 0.4f);
            hero.Squash(0.9f, 1.1f);
            spinStartedAt = Time.unscaledTime;
            slipped = false;

            StartCoroutine(TensionWatch(target));
            yield return wheel.Spin(target, SaveData.AutoSpin ? Balance.AutoSpinSeconds : Balance.SpinSeconds, drama);
            if (!slipped) wheel.SetLights(PrizeWheel.Lights.Idle);

            AudioManager.I.Play("wheel_stop");
            wheel.HighlightWin(target);
            wheel.Punch(0.1f);
            CameraRig.I.Shake(0.15f);
            FX.I.Sparks(wheel.PointerTip, Gold, 16, 1f, true);
        }

        /// <summary>The Golem's stone shield slams over the revealed number and knocks it down.</summary>
        IEnumerator StoneBlock(int landed, int after)
        {
            var at = wheel.PointerTip + Vector3.up * 1.3f;
            var boss = BossFighter;
            yield return new WaitForSeconds(0.3f);
            AudioManager.I.Play("stone_block");
            AudioManager.I.Play("hit_heavy", 0.7f, 0.6f);
            Juice.I.Hitstop(0.08f);
            boss.SetPose(Pose.Attack, 0.5f);
            boss.Squash(1.15f, 0.88f);
            boss.Flash(StoneColor, 0.8f);
            FX.I.Pop("icon_block", at + Vector3.right * 2f, 0.6f, 2.3f, 0.9f, Color.white, 0f, false);
            FX.I.Text(at + new Vector3(2f, 1.4f), $"-{landed - after} BLOCKED", StoneColor, 1.6f, 1f, 1.2f, true);
            FX.I.Dust(at, 10, new Color(0.6f, 0.55f, 0.5f, 0.8f));
            FX.I.Sparks(at, Boss.Accent, 28, 1.3f, true);
            FX.I.Ring(at, StoneColor, 6f, 0.4f);
            wheel.Punch(0.09f);
            CameraRig.I.Shake(0.4f);
            PostFX.I.Punch(chromaAdd: 0.4f, saturationAdd: -25f);
            yield return new WaitForSeconds(0.28f);
            FX.I.Text(at, after.ToString(), StoneColor, 3f, 0.6f, 0.9f, true);
            FX.I.Pop("vfx_glow", at, 1f, 5f, 0.45f, StoneColor);
        }

        /// <summary>Landing on a scorched slice: the fire leaps off the wheel at the hero. The hit itself still goes in.</summary>
        IEnumerator Scorched(int amount)
        {
            var from = wheel.PointerTip;
            AudioManager.I.Play("fire_sizzle");
            Flames(from, FireColor, 30, 1.6f);
            FX.I.Light(from, FireColor, 5f, 9f, 0.6f);
            FX.I.Text(from + Vector3.up * 2.5f, "SCORCHED!", FireColor, 1.8f, 1f, 1.1f, true);
            wheel.Punch(0.08f);
            yield return Projectile("vfx_glow", from, hero.Chest, 0.3f, 1.6f, FireColor, true, 0f, 2f);
            Flames(hero.Chest, FireColor, 24, 1.2f);
            Singe(amount, FireColor, "BURN");
        }

        PrizeWheel.Drama ChooseDrama(int target)
        {
            int k = wheel.KillSlice, n = wheel.Count;
            float r = Random.value;
            if (k < 0) return r < 0.25f ? PrizeWheel.Drama.TipIn : PrizeWheel.Drama.Center;
            if (target == k) return r < 0.65f ? PrizeWheel.Drama.TipIn : PrizeWheel.Drama.Center;
            // Angle decreases while spinning, so slice k+1 stops just short of k and k-1 just slipped past it.
            if (target == (k + 1) % n) return r < 0.4f ? PrizeWheel.Drama.EdgeOut : r < 0.8f ? PrizeWheel.Drama.RockBack : PrizeWheel.Drama.Center;
            if (target == (k - 1 + n) % n) return r < 0.6f ? PrizeWheel.Drama.TipIn : PrizeWheel.Drama.Center;
            return r < 0.2f ? PrizeWheel.Drama.TipIn : PrizeWheel.Drama.Center;
        }

        int SliceDistance(int a, int b)
        {
            int d = Mathf.Abs(a - b) % wheel.Count;
            return Mathf.Min(d, wheel.Count - d);
        }

        /// <summary>When the lucky slice is near the pointer late in the spin: riser, slow-mo crawl, gold vignette.</summary>
        IEnumerator TensionWatch(int target)
        {
            bool fired = false;
            while (wheel.Spinning)
            {
                if (!fired && wheel.KillSlice >= 0 && wheel.Speed < 300f && SliceDistance(wheel.SliceAtPointer(), wheel.KillSlice) <= 1)
                {
                    fired = true;
                    AudioManager.I.Play("tension_riser", 0.85f);
                    AudioManager.I.Duck(0.45f, 1.4f, 1f);
                    PostFX.I.VignettePulse(new Color(0.6f, 0.4f, 0f), 0.2f);
                    CameraRig.I.ZoomPunch(-0.025f);
                    if (SliceDistance(target, wheel.KillSlice) <= 1) Juice.I.SlowMo(0.55f, 0.9f, 0.4f);
                }
                yield return null;
            }
        }

        void OnPeg(float speed)
        {
            float now = Time.unscaledTime;
            if (now - lastTick < 0.035f) return;
            lastTick = now;
            float s01 = Mathf.InverseLerp(0f, 1400f, speed);
            bool approaching = wheel.KillSlice >= 0 && speed < 500f && SliceDistance(wheel.SliceAtPointer(), wheel.KillSlice) <= 2;
            float pitch = Mathf.Lerp(1.15f, 0.92f, s01) + (approaching ? 0.2f : 0f);
            AudioManager.I.Play("wheel_tick_" + Random.Range(1, 4), Mathf.Lerp(0.55f, 0.35f, s01), pitch, 0.05f, 0f);
            if (speed > 600f && Random.value < 0.4f) FX.I.Sparks(wheel.PointerTip, Gold, 2, 0.6f);
            if (speed < 350f) wheel.Punch(0.008f);
        }

        /// <summary>The wheel rests on the lucky slice for a heartbeat: fake jackpot lights and a hopeful "LUCKY...?".</summary>
        void OnTeased()
        {
            AudioManager.I.Play("reveal_small", 0.7f, 1.45f);
            AudioManager.I.Play("coin_tick", 0.6f, 1.6f);
            wheel.SetLights(PrizeWheel.Lights.Jackpot);
            wheel.Flare(Gold, 1f);
            wheel.Punch(0.05f);
            FX.I.Sparks(wheel.PointerTip, Gold, 18, 1f, true);
            FX.I.Light(wheel.PointerTip, Gold, 3f, 8f, 0.4f);
            FX.I.Text(wheel.PointerTip + Vector3.up * 1.6f, "LUCKY...?", Gold, 1.4f, 0.5f, 0.55f, true);
        }

        /// <summary>Fate kicks the wheel off the lucky slice: hard clack, colour drains, the boss gloats.</summary>
        void OnSlipped()
        {
            slipped = true;
            var boss = BossFighter;
            var deny = new Color(0.85f, 0.15f, 0.45f);
            AudioManager.I.Play("hit_heavy", 0.9f, 1.25f);
            AudioManager.I.Play("shop_deny", 1f, 0.85f);
            AudioManager.I.Play("near_miss", 0.9f);
            BossVoice("taunt", 0.7f);
            Juice.I.Hitstop(0.1f);
            wheel.SetLights(PrizeWheel.Lights.Bust, 1f);
            wheel.Flare(deny, 1f);
            wheel.Punch(0.12f);
            hud.Flash(deny, 0.4f, 0.35f);
            PostFX.I.Punch(chromaAdd: 1f, saturationAdd: -60f, lensAdd: 0.25f);
            PostFX.I.VignettePulse(new Color(0.5f, 0f, 0.25f), 0.35f);
            CameraRig.I.Shake(0.6f);
            CameraRig.I.Kick(Vector2.left, 0.2f);
            CameraRig.I.ZoomPunch(0.04f);
            FX.I.Sparks(wheel.PointerTip, deny, 36, 1.5f, true);
            FX.I.Ring(wheel.PointerTip, deny, 7f, 0.4f);
            FX.I.Light(wheel.PointerTip, deny, 4f, 9f, 0.5f);
            FX.I.Text(wheel.PointerTip + Vector3.up * 2.4f, "DENIED!", deny, 2.2f, 0.9f, 1.3f, true);
            boss.SetPose(Pose.Attack, 0.6f);
            boss.Flash(boss.Accent, 0.8f);
            boss.Squash(1.15f, 0.9f);
            FX.I.Text(boss.Head + Vector3.up * 0.8f, "HAH!", boss.Accent, 1.5f, 1.2f, 1f, true);
        }

        IEnumerator LuckyReveal()
        {
            AudioManager.I.Duck(0.3f, 2.5f);
            wheel.SetLights(PrizeWheel.Lights.Jackpot, 3f);
            wheel.Flare(Gold, 1f);
            Juice.I.SlowMo(0.35f, 0.5f, 0.3f);
            hud.Banner("JACKPOT!", $"Exactly {bossHp}!", HUD.BannerStyle.Jackpot, 1.3f);
            hud.Flash(new Color(1f, 0.9f, 0.6f), 0.35f, 0.3f);
            PostFX.I.Punch(bloomAdd: 1.8f, chromaAdd: 0.6f, saturationAdd: 30f);
            FX.I.Sparks(wheel.PointerTip, Gold, 40, 1.6f, true);
            FX.I.Ring(wheel.PointerTip, Gold, 10f, 0.6f);
            CameraRig.I.Shake(0.5f);
            yield return new WaitForSecondsRealtime(0.55f);
        }

        /// <summary>A lucky charm tips the landed number onto the lucky one: icon pop, ±1 tick, then the new number.</summary>
        IEnumerator CharmNudge(int landed, int value)
        {
            var at = wheel.PointerTip + Vector3.up * 1.3f;
            var luck = new Color(0.6f, 1f, 0.45f);
            yield return new WaitForSeconds(0.38f);
            AudioManager.I.Play("charm_nudge");
            FX.I.Pop(charm.Icon, at + Vector3.left * 2.1f, 0.5f, 1.7f, 0.9f, Color.white, 0f, false);
            FX.I.Text(at + new Vector3(-2.1f, 1.1f), charm.Name.ToUpperInvariant() + "!", luck, 1.1f, 0.9f, 1.2f, true);
            FX.I.Text(at + new Vector3(1.4f, 0.6f), value > landed ? "+1" : "-1", luck, 1.9f, 1.1f, 0.9f, true);
            FX.I.Sparks(at, luck, 34, 1.3f, true);
            FX.I.Ring(at, luck, 6f, 0.5f);
            FX.I.Light(wheel.PointerTip, luck, 3f, 8f, 0.6f);
            wheel.Flare(luck, 0.9f);
            wheel.Punch(0.06f);
            CameraRig.I.Shake(0.2f);
            yield return new WaitForSeconds(0.25f);
            FX.I.Text(at, value.ToString(), Gold, 3.4f, 0.6f, 0.9f, true);
            FX.I.Pop("vfx_glow", at, 1f, 5.5f, 0.45f, Gold);
        }

        // ---------- Hero attack & outcomes ----------

        IEnumerator HeroAttack(HitResult result, bool wasKillable, bool dodged, bool swallowed)
        {
            var boss = BossFighter;
            // The attack flipbook bakes in a lunge that carries Ace ~1.05 body heights forward by its hit frame,
            // so the dash only closes the rest of the gap and lands exactly on that frame.
            bool animated = hero.HasAnim(Pose.Attack);
            var strike = new Vector3(boss.transform.position.x - 2.6f - boss.Height * 0.12f - (animated ? hero.Height * 1.05f : 0f), World.GroundY);

            // Anticipation, then dash with afterimages.
            hero.Squash(1.18f, 0.84f);
            hero.Tilt(-7f);
            yield return new WaitForSeconds(0.09f);
            float swing = animated ? hero.AttackDuration : 0.55f;
            float swingEnd = Time.time + swing;
            hero.SetPose(Pose.Attack, swing);
            AudioManager.I.Play("whoosh_ui", 0.9f, 0.8f);
            FX.I.Dust(hero.transform.position, 4, new Color(0.8f, 0.75f, 0.7f, 0.6f));
            yield return Move(hero, strike, animated ? hero.AttackHitTime : 0.13f, Ease.OutCubic, true);

            if (swallowed) yield return Swallowed(result.Damage);
            else if (dodged) BossDodged();
            else if (result.Outcome == HitOutcome.Miss) Spared();
            else yield return Impact(result);

            // Let the swing (and the flipbook's hop back) finish before running home.
            yield return new WaitForSeconds(Mathf.Max(0.12f, swingEnd - Time.time));
            hero.SetPose(Pose.Idle);
            yield return Move(hero, hero.Home, 0.3f, Ease.InOutCubic, false, 0.6f);
            hero.Squash(1.12f, 0.9f);

            if (bossHp == 0) yield break;
            if (wasKillable) misses++;
            RefreshTarget();
            if (result.Outcome == HitOutcome.Bust && !dodged && !swallowed && Boss.Quirk == "counter") yield return Counter();
        }

        /// <summary>Gildmaw gulps the hit whole: no damage, but the hoard coughs up its multiple in gold.</summary>
        IEnumerator Swallowed(int damage)
        {
            var boss = BossFighter;
            var mouth = boss.Chest + Vector3.left * 0.6f;
            int gold = Mathf.RoundToInt(damage * stats.GoldPerDamage * Boss.QuirkValue);
            AudioManager.I.Play("mimic_chomp", 1f, 0.9f);
            AudioManager.I.Play("coin_burst", 0.9f, 0.9f);
            Juice.I.Hitstop(0.12f);
            boss.SetPose(Pose.Attack, 0.6f);
            boss.Squash(1.3f, 0.75f);
            boss.Flash(Gold, 1f);
            FX.I.Pop("icon_swallow", mouth + Vector3.up * 1.8f, 0.5f, 2.4f, 0.9f, Color.white, 0f, false);
            FX.I.Text(boss.Head + Vector3.up * 0.9f, "GULP!", Gold, 2.6f, 1.2f, 1.2f, true);
            FX.I.Sparks(mouth, Gold, 40, 1.5f, true);
            FX.I.Ring(mouth, Gold, 8f, 0.5f);
            FX.I.Light(mouth, Gold, 5f, 10f, 0.8f);
            CameraRig.I.Shake(0.5f);
            CameraRig.I.ZoomPunch(-0.04f);
            PostFX.I.Punch(bloomAdd: 1.6f, saturationAdd: 25f);
            hud.Flash(Gold, 0.3f, 0.3f);
            yield return new WaitForSeconds(0.25f);
            FX.I.Text(mouth + Vector3.up * 2.6f, $"×{Boss.QuirkValue} GOLD", Gold, 2f, 1.4f, 1.3f, true);
            GrantGold(gold, mouth, Mathf.Min(30, 6 + gold / 2));
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Justice holds an overshoot back: golden scales flare and the boss is untouched instead of healed.</summary>
        void Spared()
        {
            var at = BossFighter.Chest + Vector3.left * 0.8f;
            AudioManager.I.Play("hit_heavy", 0.7f, 1.6f);
            AudioManager.I.Play("hp_upgrade", 0.7f, 1.2f);
            Juice.I.Hitstop(0.08f);
            FX.I.Pop("tarot_justice", at + Vector3.up * 0.8f, 0.5f, 2.2f, 0.8f, Color.white, 0f, false);
            FX.I.Ring(at, TarotColor, 8f, 0.45f);
            FX.I.Sparks(at, Gold, 30, 1.3f, true);
            FX.I.Text(at + Vector3.up * 2.4f, "NO BUST!", TarotColor, 2f, 1.2f, 1.1f, true);
            CameraRig.I.Shake(0.25f);
        }

        /// <summary>Sir Blackjack punishes a bust: a parry clang and an instant extra attack.</summary>
        IEnumerator Counter()
        {
            var boss = BossFighter;
            AudioManager.I.Play("counter_clang");
            Juice.I.Hitstop(0.1f);
            boss.SetPose(Pose.Attack, 0.6f);
            boss.Flash(Color.white, 1f);
            boss.Squash(1.2f, 0.85f);
            FX.I.Pop("icon_counter", boss.Head + Vector3.up * 1.2f, 0.5f, 2.4f, 0.8f, Color.white, 0f, false);
            FX.I.Text(boss.Head + Vector3.up * 2.4f, "RIPOSTE!", boss.Accent, 2.4f, 1f, 1.2f, true);
            FX.I.Sparks(boss.Chest, Color.white, 30, 1.4f, true);
            FX.I.Ring(boss.Chest, boss.Accent, 8f, 0.4f);
            hud.Flash(boss.Accent, 0.25f, 0.25f);
            CameraRig.I.ZoomPunch(-0.04f);
            yield return new WaitForSeconds(0.35f);
            yield return Strike(0);
            yield return new WaitForSeconds(0.35f);
        }

        IEnumerator Impact(HitResult result)
        {
            var boss = BossFighter;
            var def = Boss;
            int dmg = result.Damage;
            float power = Mathf.Lerp(0.45f, 1f, dmg / 12f);
            var hitPos = boss.Chest + new Vector3(-0.3f, 0f);
            bool jackpot = result.Outcome == HitOutcome.Jackpot;

            if (jackpot) yield return ImpactFrames(boss);
            Juice.I.Hitstop(Mathf.Lerp(0.05f, 0.2f, dmg / 12f) + (jackpot ? 0.18f : 0f));

            boss.SetPose(Pose.Hurt, 0.5f);
            boss.Flash(Color.white, 1f);
            boss.Squash(0.72f, 1.22f);
            boss.Knock(5f * power);
            boss.Tilt(9f * power);
            FX.I.Pop("vfx_slash", hitPos, 1.6f, 4.6f * power + 1f, 0.26f, new Color(1f, 0.95f, 0.8f), Random.Range(-25f, 15f));
            FX.I.Pop("vfx_impact", hitPos, 0.4f, 3.4f * power, 0.22f, Color.white);
            FX.I.Ring(hitPos, def.Accent, 4f + 4f * power);
            FX.I.Sparks(hitPos, Gold, (int)(14 + 22 * power), 1f + power);
            FX.I.Light(hitPos, Color.Lerp(def.Accent, Color.white, 0.5f), 1.5f + 3f * power, 5f + 4f * power, 0.35f);
            CameraRig.I.Shake(0.3f + 0.45f * power);
            CameraRig.I.Kick(Vector2.right, 0.12f + 0.12f * power);
            CameraRig.I.ZoomPunch(-0.03f * power);
            PostFX.I.Punch(bloomAdd: 1.4f * power, chromaAdd: 0.45f * power, lensAdd: -0.22f * power);
            hud.Flash(Color.white, 0.22f * power, 0.14f);
            AudioManager.I.Play(dmg >= 8 ? "hit_heavy" : "hit_light");
            BossVoice("hurt", 0.8f);
            FX.I.Text(hitPos + new Vector3(0.4f, 1.4f), "-" + dmg, jackpot ? Gold : Color.white, 1.3f + power, 1.6f, 1.1f, dmg >= 9);

            yield return Land(result, hitPos);
        }

        /// <summary>Damage lands: gold for the hit, then HP drops, wraps (BUST) or hits zero (JACKPOT).</summary>
        IEnumerator Land(HitResult result, Vector3 hitPos)
        {
            int gold = Mathf.RoundToInt(result.Damage * stats.GoldPerDamage);
            GrantGold(gold, hitPos, Mathf.Min(16, 4 + gold));

            switch (result.Outcome)
            {
                case HitOutcome.Hit:
                    bossHp = result.HpAfter;
                    hud.SetBossHp(bossHp, Boss.Hp, true);
                    break;
                case HitOutcome.Bust:
                    yield return Bust(result);
                    break;
                case HitOutcome.Jackpot:
                    yield return phylactery ? Phylactery() : JackpotKill();
                    break;
            }
        }

        /// <summary>Mortis's first death: the phylactery bursts, a soul flame roars up, and he rises with part of his HP.</summary>
        IEnumerator Phylactery()
        {
            var boss = BossFighter;
            var def = Boss;
            var c = boss.Chest;
            phylactery = false;
            jackpots++;
            bossHp = 0;
            hud.SetBossHp(0, def.Hp, true);
            Juice.I.SlowMo(0.3f, 0.9f, 0.6f);
            AudioManager.I.Play("phylactery_shatter");
            BossVoice("hurt", 1f, 0.8f);
            boss.SetPose(Pose.Hurt, 1f);
            boss.Flash(SoulColor, 1f);
            boss.Squash(0.7f, 1.3f);
            FX.I.Pop("icon_phylactery", c + Vector3.up * 1.6f, 0.6f, 3f, 0.55f, Color.white, 0f, false);
            yield return new WaitForSeconds(0.4f);

            FX.I.Emit(c + Vector3.up * 1.6f, new FX.Burst
            {
                Sprite = "vfx_sparkle", Count = 70, SpeedMin = 4f, SpeedMax = 13f, AngleMin = 0f, AngleMax = 360f,
                Gravity = -8f, Drag = 1.2f, LifeMin = 0.6f, LifeMax = 1.3f, SizeMin = 0.3f, SizeMax = 0.8f, EndSize = 0f,
                Spin = 600f, Color = Color.white, EndColor = SoulColor, Additive = true,
            });
            FX.I.Ring(c, SoulColor, 13f, 0.6f);
            FX.I.Ring(c, Color.white, 7f, 0.4f);
            FX.I.Light(c, SoulColor, 8f, 16f, 1.4f);
            CameraRig.I.Shake(0.9f);
            CameraRig.I.ZoomPunch(-0.07f);
            PostFX.I.Punch(bloomAdd: 2.2f, chromaAdd: 1f, saturationAdd: -60f, lensAdd: -0.35f);
            hud.Flash(SoulColor, 0.5f, 0.5f);
            hud.SpendLife();
            hud.Banner("HE RISES!", "Phylactery shattered  ·  one life left", HUD.BannerStyle.Danger, 1.2f);
            yield return new WaitForSeconds(0.5f);

            bossHp = Mathf.CeilToInt(def.Hp * def.QuirkValue / 100f);
            AudioManager.I.Play("hp_refill", 1f, 0.8f);
            boss.SetPose(Pose.Attack, 1f);
            boss.Squash(1.2f, 0.85f);
            for (int i = 0; i < 3; i++) Flames(boss.transform.position + Vector3.up * boss.Height * (0.2f + i * 0.3f), SoulColor, 20, 1.5f);
            FX.I.Text(boss.Head + Vector3.up * 0.6f, "+" + bossHp, SoulColor, 2.2f, 1.3f, 1.3f, true);
            yield return hud.BossWrap(def.Hp, bossHp);
        }

        // ---------- Consumables ----------

        IEnumerator UseItem(ShopItem item)
        {
            hud.PopItem(item);
            switch (item.Stat)
            {
                case "bomb": yield return ThrowBomb(item); break;
                case "potion": yield return DrinkPotion(item); break;
                case "clover": yield return PlantClover(item); break;
                case "ward": yield return RaiseWard(item); break;
            }
        }

        /// <summary>Free action before the spin: lob a flask for exactly its damage under the same exact-zero rules.</summary>
        IEnumerator ThrowBomb(ShopItem bomb)
        {
            var boss = BossFighter;
            hero.Squash(1.15f, 0.86f);
            hero.Tilt(-6f);
            yield return new WaitForSeconds(0.08f);
            hero.SetPose(Pose.Attack, 0.45f);
            AudioManager.I.Play("potion_throw");
            yield return Projectile(bomb.Icon, hero.Chest + new Vector3(0.9f, 0.6f), boss.Chest, 0.5f, 1.1f, Color.white, false, 760f, 3.2f);

            var c = boss.Chest;
            var fire = new Color(1f, 0.55f, 0.15f);
            var result = FightMath.Apply(bossHp, Boss.Hp, bomb.Value);
            AudioManager.I.Play("potion_explode");
            BossVoice("hurt", 0.8f);
            Juice.I.Hitstop(0.12f);
            boss.SetPose(Pose.Hurt, 0.5f);
            boss.Flash(fire, 1f);
            boss.Squash(0.75f, 1.2f);
            boss.Knock(4f);
            boss.Tilt(8f);
            FX.I.Pop("vfx_impact", c, 0.5f, 5f, 0.28f, fire);
            FX.I.Pop("vfx_glow", c, 1f, 8f, 0.5f, fire);
            FX.I.Ring(c, fire, 9f, 0.5f);
            FX.I.Sparks(c, fire, 44, 1.8f);
            FX.I.Emit(c, new FX.Burst
            {
                Sprite = "vfx_glow", Count = 28, SpeedMin = 2f, SpeedMax = 7f, AngleMin = 20f, AngleMax = 160f,
                Gravity = 5f, Drag = 2f, LifeMin = 0.4f, LifeMax = 0.9f, SizeMin = 0.7f, SizeMax = 1.5f, EndSize = 0.2f,
                Color = new Color(1f, 0.75f, 0.3f), EndColor = new Color(0.8f, 0.1f, 0.02f, 0f), Additive = true, Spread = new Vector2(0.5f, 0.4f),
            });
            FX.I.Dust(c + Vector3.down * 0.6f, 8, new Color(0.25f, 0.2f, 0.2f, 0.7f));
            FX.I.Light(c, fire, 6f, 12f, 0.8f);
            FX.I.Text(c + new Vector3(0.4f, 1.4f), "-" + bomb.Value, fire, 2.2f, 1.6f, 1.1f, true);
            CameraRig.I.Shake(0.7f);
            CameraRig.I.Kick(Vector2.right, 0.25f);
            CameraRig.I.ZoomPunch(-0.05f);
            PostFX.I.Punch(bloomAdd: 2f, chromaAdd: 0.5f, lensAdd: -0.3f);
            hud.Flash(fire, 0.3f, 0.3f);

            yield return Land(result, c);
            if (bossHp == 0) yield break;
            RefreshTarget();
            if (result.Outcome == HitOutcome.Bust && Boss.Quirk == "counter") yield return Counter();
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Gulp a potion: green geyser, the HP bar surges back up.</summary>
        IEnumerator DrinkPotion(ShopItem potion)
        {
            var heal = new Color(0.45f, 1f, 0.55f);
            hero.Squash(0.88f, 1.14f);
            hero.Tilt(-8f);
            AudioManager.I.Play("potion_throw", 0.6f, 1.4f);
            FX.I.Pop(potion.Icon, hero.Head + new Vector3(0.6f, 0.4f), 0.4f, 1.4f, 0.5f, Color.white, -30f, false);
            yield return new WaitForSeconds(0.28f);

            int amount = Mathf.Min(heroMax - heroHp, potion.Value + stats.PotionBonus);
            HealHero(amount, heal);
            if (poison > 0)
            {
                poison = 0;
                hud.SetPoison(0);
                FX.I.Text(hero.Head + new Vector3(0f, 2f), "CURED!", VenomColor, 1.6f, 1.2f, 1.1f, true);
                Flames(hero.Chest, VenomColor, 20, 1f);
            }
            hero.SetPose(Pose.Victory, 0.6f);
            Juice.I.Hitstop(0.06f);
            CameraRig.I.ZoomPunch(-0.03f);
            PostFX.I.Punch(bloomAdd: 1.4f, saturationAdd: 25f);
            hud.Flash(heal, 0.25f, 0.35f);
            FX.I.Ring(hero.Chest, heal, 8f, 0.5f);
            FX.I.Light(hero.Chest, heal, 4f, 9f, 0.7f);
            yield return new WaitForSeconds(0.6f);
        }

        /// <summary>A clover blooms over the wheel and pumps the Lucky Number's odds for the next spin.</summary>
        IEnumerator PlantClover(ShopItem clover)
        {
            var luck = new Color(0.45f, 1f, 0.4f);
            var at = wheel.PointerTip + Vector3.up * 1.2f;
            cloverWeight += clover.Value + stats.CloverBonus;
            AudioManager.I.Play("charm_nudge", 1f, 1.15f);
            AudioManager.I.Play("reveal_big", 0.6f, 1.3f);
            FX.I.Pop(clover.Icon, at, 0.3f, 2.4f, 1f, Color.white, 0f, false);
            FX.I.Emit(at, new FX.Burst
            {
                Sprite = "vfx_sparkle", Count = 50, SpeedMin = 3f, SpeedMax = 9f, AngleMin = 0f, AngleMax = 360f,
                Gravity = 2f, Drag = 2f, LifeMin = 0.6f, LifeMax = 1.2f, SizeMin = 0.3f, SizeMax = 0.7f, EndSize = 0f,
                Spin = 400f, Color = luck, EndColor = Gold, Additive = true, Spread = new Vector2(0.4f, 0.4f),
            });
            FX.I.Ring(at, luck, 10f, 0.6f);
            FX.I.Light(at, luck, 5f, 10f, 0.8f);
            FX.I.Text(at + Vector3.up * 1.4f, "FEELING LUCKY!", luck, 1.6f, 1f, 1.2f, true);
            wheel.Flare(luck, 1f);
            wheel.Punch(0.1f);
            wheel.SetLights(PrizeWheel.Lights.Jackpot, 1.2f);
            hud.Flash(luck, 0.3f, 0.4f);
            PostFX.I.Punch(bloomAdd: 1.6f, saturationAdd: 35f);
            CameraRig.I.Shake(0.25f);
            RefreshTarget();
            yield return new WaitForSeconds(0.7f);
        }

        /// <summary>A shimmering bubble wraps the hero until the boss's next attack bounces off it.</summary>
        IEnumerator RaiseWard(ShopItem ward)
        {
            warded = true;
            AudioManager.I.Play("hp_upgrade", 0.9f, 1.2f);
            AudioManager.I.Play("whoosh_ui", 0.8f, 0.7f);
            hero.SetPose(Pose.Victory, 0.5f);
            hero.Flash(WardColor, 0.9f);
            FX.I.Pop(ward.Icon, hero.Head + Vector3.up * 0.8f, 0.4f, 1.8f, 0.8f, Color.white, 0f, false);
            FX.I.Ring(hero.Chest, WardColor, 9f, 0.5f);
            FX.I.Sparks(hero.Chest, WardColor, 30, 1.3f, true);
            FX.I.Light(hero.Chest, WardColor, 4f, 9f, 0.6f);
            FX.I.Text(hero.Head + Vector3.up * 1.6f, "WARDED", WardColor, 1.5f, 1f, 1.1f, true);
            hud.Flash(WardColor, 0.2f, 0.3f);
            CameraRig.I.ZoomPunch(-0.02f);

            wardBubble = new GameObject("WardBubble").AddComponent<SpriteRenderer>();
            wardBubble.sprite = S("vfx_glow");
            wardBubble.sharedMaterial = config.additive;
            wardBubble.sortingOrder = FX.OrderFront - 20;
            wardBubble.color = new Color(WardColor.r, WardColor.g, WardColor.b, 0.45f);
            wardBubble.transform.SetParent(hero.transform, false);
            wardBubble.transform.localPosition = new Vector3(0f, hero.Height * 0.5f);
            yield return new WaitForSeconds(0.55f);
        }

        static readonly Color WardColor = new(0.4f, 0.9f, 1f);

        /// <summary>Phoenix Feather: the hero falls, then bursts back up in flame with half their HP.</summary>
        IEnumerator Revive()
        {
            var fire = new Color(1f, 0.55f, 0.15f);
            hero.SetPose(hero.HasAnim(Pose.Death) ? Pose.Death : Pose.Hurt, 99f);
            AudioManager.I.Loop("heartbeat", false);
            Juice.I.SlowMo(0.3f, 0.9f, 0.4f);
            PostFX.I.Punch(saturationAdd: -80f, lensAdd: 0.2f);
            yield return new WaitForSecondsRealtime(1f);

            AudioManager.I.Play("jackpot", 0.8f, 0.8f);
            AudioManager.I.Play("potion_explode", 0.9f, 0.7f);
            AudioManager.I.Play("hp_upgrade", 1f, 0.9f);
            hero.SetPose(Pose.Victory, 1f);
            hero.Flash(Color.white, 1f);
            hero.Squash(0.7f, 1.35f);
            Juice.I.Hitstop(0.15f);
            var c = hero.Chest;
            for (int i = 0; i < 3; i++) FX.I.Ring(c, i == 1 ? Gold : fire, 7f + i * 4f, 0.5f + i * 0.15f);
            FX.I.Emit(hero.transform.position, new FX.Burst
            {
                Sprite = "vfx_glow", Count = 60, SpeedMin = 4f, SpeedMax = 11f, AngleMin = 60f, AngleMax = 120f,
                Gravity = -2f, Drag = 1.5f, LifeMin = 0.6f, LifeMax = 1.3f, SizeMin = 0.6f, SizeMax = 1.6f, EndSize = 0.1f,
                Color = new Color(1f, 0.8f, 0.3f), EndColor = new Color(0.9f, 0.15f, 0.02f, 0f), Additive = true, Spread = new Vector2(1f, 0.3f),
            });
            FX.I.Sparks(c, Gold, 60, 2f, true);
            FX.I.Light(c, fire, 8f, 16f, 1.4f);
            CameraRig.I.Shake(0.8f);
            CameraRig.I.ZoomPunch(-0.07f);
            PostFX.I.Punch(bloomAdd: 2.4f, chromaAdd: 0.7f, saturationAdd: 40f, exposureAdd: 0.3f);
            hud.Flash(fire, 0.55f, 0.5f);
            hud.Banner("REBORN!", "The Phoenix Feather burns away", HUD.BannerStyle.Jackpot, 1.3f);
            HealHero(Mathf.Max(1, heroMax / 2 - heroHp), fire);
            yield return new WaitForSeconds(1.3f);
        }

        void HealHero(int amount, Color tint)
        {
            heroHp += amount;
            hud.HealHero(heroHp, heroMax);
            AudioManager.I.Play("hp_refill", 0.9f, 1.25f);
            hero.Flash(tint, 0.8f);
            hero.Squash(1.12f, 0.9f);
            FX.I.Emit(hero.transform.position + Vector3.up * 0.3f, new FX.Burst
            {
                Sprite = "vfx_sparkle", Count = 30 + amount, SpeedMin = 2f, SpeedMax = 6f, AngleMin = 75f, AngleMax = 105f,
                Gravity = 2f, Drag = 1f, LifeMin = 0.6f, LifeMax = 1.2f, SizeMin = 0.25f, SizeMax = 0.55f, EndSize = 0f,
                Spin = 200f, Color = tint, EndColor = Color.white, Additive = true, Spread = new Vector2(0.8f, 0.2f),
            });
            FX.I.Text(hero.Head + new Vector3(0f, 0.8f), "+" + amount, tint, 1.8f, 1.4f, 1.1f, true);
            UpdateDanger();
        }

        void UpdateDanger()
        {
            float frac = heroHp / (float)heroMax;
            PostFX.I.SetDanger(frac < 0.3f ? 1f - frac / 0.3f * 0.6f : 0f);
            AudioManager.I.Loop("heartbeat", frac < 0.3f && heroHp > 0, 0.8f);
        }

        /// <summary>Two anime impact frames: black world + white silhouettes, then inverted.</summary>
        IEnumerator ImpactFrames(Fighter boss)
        {
            if (!SaveData.Flashes) yield break;
            var cover = new GameObject("ImpactFrame").AddComponent<SpriteRenderer>();
            transients.Add(cover.gameObject);
            cover.sprite = S("vfx_circle");
            cover.sharedMaterial = config.spriteDefault;
            cover.sortingOrder = 15;
            cover.transform.SetParent(cam.transform, false);
            cover.transform.localPosition = new Vector3(0f, 0f, 10f);
            cover.transform.localScale = Vector3.one * 60f / cover.sprite.bounds.size.x;
            cover.color = Color.black;
            hero.Silhouette(Color.white, true);
            boss.Silhouette(Color.white, true);
            yield return new WaitForSecondsRealtime(0.07f);
            cover.color = Color.white;
            hero.Silhouette(Color.black, true);
            boss.Silhouette(Color.black, true);
            yield return new WaitForSecondsRealtime(0.06f);
            Destroy(cover.gameObject);
            hero.Silhouette(Color.white, false);
            boss.Silhouette(Color.white, false);
        }

        IEnumerator Bust(HitResult result)
        {
            var boss = BossFighter;
            var def = Boss;
            busts++;
            bossHp = result.HpAfter;
            AudioManager.I.Play("bust");
            wheel.SetLights(PrizeWheel.Lights.Bust, 1.2f);
            hud.Banner("BUST!", $"Overshot by {result.Overshoot}  ·  healed to {bossHp}", HUD.BannerStyle.Bust, 1.2f);
            hud.Flash(new Color(0.6f, 0.1f, 0.5f), 0.35f, 0.4f);
            FX.I.Light(boss.Chest, new Color(0.7f, 0.2f, 1f), 3f, 10f, 1.2f);
            PostFX.I.Punch(chromaAdd: 0.8f, saturationAdd: -70f, lensAdd: 0.25f);
            CameraRig.I.Shake(0.5f);
            yield return new WaitForSeconds(0.25f);

            // The boss gloats while its HP rewinds and refills.
            AudioManager.I.Play("hp_refill");
            BossVoice("taunt", 0.8f);
            boss.SetPose(Pose.Attack, 0.9f);
            boss.Flash(def.Accent, 0.8f);
            boss.Squash(1.15f, 0.9f);
            FX.I.Emit(boss.transform.position + Vector3.up * 0.5f, new FX.Burst
            {
                Sprite = "vfx_sparkle", Count = 40, SpeedMin = 2f, SpeedMax = 6f, AngleMin = 70f, AngleMax = 110f,
                Gravity = 3f, Drag = 1f, LifeMin = 0.6f, LifeMax = 1.2f, SizeMin = 0.25f, SizeMax = 0.6f, EndSize = 0f,
                Spin = 300f, Color = def.Accent, EndColor = Color.white, Additive = true, Spread = new Vector2(1.4f, 0.3f),
            });
            FX.I.Text(boss.Head + Vector3.up * 0.6f, "+" + (bossHp), new Color(0.5f, 1f, 0.6f), 1.6f, 1.2f, 1.2f, true);
            yield return hud.BossWrap(def.Hp, bossHp);
        }

        IEnumerator JackpotKill()
        {
            var boss = BossFighter;
            var def = Boss;
            jackpots++;
            bossHp = 0;
            hud.SetBossHp(0, def.Hp, true);
            Juice.I.SlowMo(0.25f, 1.1f, 0.8f);
            AudioManager.I.Play("jackpot");
            hud.Flash(new Color(1f, 0.92f, 0.6f), 0.5f, 0.45f);
            PostFX.I.Punch(bloomAdd: 2.6f, chromaAdd: 1f, lensAdd: -0.45f, saturationAdd: 35f, exposureAdd: 0.35f);
            CameraRig.I.Shake(1f);
            CameraRig.I.ZoomPunch(-0.08f);
            var c = boss.Chest;
            FX.I.Light(c, Gold, 7f, 16f, 1.6f);
            for (int i = 0; i < 3; i++) FX.I.Ring(c, i == 1 ? def.Accent : Gold, 6f + i * 4f, 0.5f + i * 0.15f);
            FX.I.Sparks(c, Gold, 70, 2f, true);
            FX.I.Coins(c, 60, 1.5f);
            FX.I.Cards(c, 14);
            FX.I.Confetti(c + Vector3.up * 2f, 90);

            yield return new WaitForSeconds(0.45f);
            BossVoice("death", 1f);
            boss.Flash(Color.white, 1f);
            StartCoroutine(boss.Dissolve(1.3f, def.Accent));
            for (int i = 0; i < 6; i++)
            {
                FX.I.Coins(boss.Chest, 10, 1.2f);
                FX.I.Sparks(boss.Chest, def.Accent, 10, 1.2f);
                CameraRig.I.Shake(0.25f);
                yield return new WaitForSeconds(0.16f);
            }
            int bonus = Mathf.RoundToInt(def.GoldBonus * stats.BossGoldMultiplier);
            hud.Banner("BOSS DEFEATED", $"+{bonus} gold bonus", HUD.BannerStyle.Jackpot, 1.2f);
            GrantGold(bonus, c, 30);
            yield return new WaitForSeconds(0.9f);
        }

        void GrantGold(int amount, Vector3 from, int coins)
        {
            runGold += amount;
            int landed = 0;
            FX.I.FlyCoins(from, () => hud.GoldWorldPosition(cam), coins, () =>
            {
                // Each coin carries its share, so the counter climbs as the coins land rather than ahead of them.
                landed++;
                hud.AddGold(amount * landed / coins - amount * (landed - 1) / coins);
                hud.PopGold();
                AudioManager.I.Play("coin_tick", 0.5f, Random.Range(1f, 1.3f), 0.03f, 0.02f);
            });
        }

        // ---------- Boss turn ----------

        IEnumerator BossTurn()
        {
            var boss = BossFighter;
            var def = Boss;
            yield return new WaitForSeconds(0.3f);

            boss.Squash(1.15f, 0.85f);
            boss.Tilt(6f);
            yield return new WaitForSeconds(0.12f);
            boss.SetPose(Pose.Attack, 0.7f);
            BossVoice("attack", 0.8f);
            yield return Strike(sting);
            yield return new WaitForSeconds(0.45f);
            if (poison > 0 && heroHp > 0) yield return PoisonTick();
            if (def.Quirk == "scorch") yield return Scorch();
        }

        /// <summary>
        /// The boss's attack: Hits strikes of DmgMin..DmgMax, each dodged or soaked by armor on its own, so armor shines
        /// against one big slam and dodge against a few heavy blows. A ward eats the whole volley. <paramref name="sting"/>
        /// (a tarot card's cost) rides on the first strike that lands; Arachna's venom stacks when any strike lands.
        /// </summary>
        IEnumerator Strike(int sting)
        {
            var boss = BossFighter;
            var def = Boss;
            bool shielded = warded;
            int landedHits = 0;
            if (sting > 0) FX.I.Text(boss.Head + Vector3.up * 0.6f, $"+{sting} CURSE", TarotColor, 1.5f, 1.1f, 1.1f, true);

            void Hit(int i, Color accent)
            {
                if (shielded)
                {
                    if (i == 0) Blocked();
                    else Deflect();
                    return;
                }
                if (heroHp <= 0) return;
                int raw = rng.Next(def.DmgMin, def.DmgMax + 1) + (landedHits == 0 ? sting : 0);
                if (HeroHit(raw, accent)) landedHits++;
            }

            if (def.Attack == "melee")
            {
                // Dash in, then one swing per hit with a quick wind-back between.
                AudioManager.I.Play("boss_attack_swipe");
                var strike = new Vector3(hero.transform.position.x + 2.4f + def.Height * 0.15f, World.GroundY);
                yield return Move(boss, strike, 0.16f, Ease.InCubic, true);
                for (int i = 0; i < def.Hits; i++)
                {
                    if (i > 0)
                    {
                        boss.SetPose(Pose.Attack, 0.4f);
                        AudioManager.I.Play("boss_attack_swipe", 0.9f, 1.15f + i * 0.1f);
                        yield return Move(boss, strike + Vector3.right * 1.1f, 0.1f, Ease.OutQuad, false);
                        yield return Move(boss, strike, 0.09f, Ease.InCubic, true);
                    }
                    Hit(i, def.Accent);
                    yield return new WaitForSeconds(0.12f);
                }
                yield return new WaitForSeconds(0.15f);
                yield return Move(boss, boss.Home, 0.35f, Ease.InOutCubic, false, 0.4f);
            }
            else
            {
                // One projectile per hit; each strikes as it lands.
                switch (def.Attack)
                {
                    case "cards": AudioManager.I.Play("boss_attack_swipe", 0.8f, 1.3f); break;
                    case "coins": AudioManager.I.Play("mimic_chomp"); AudioManager.I.Play("coin_burst", 0.7f, 1.15f); break;
                    case "venom": AudioManager.I.Play("venom_spit"); break;
                    case "fire": AudioManager.I.Play("boss_attack_magic", 0.9f, 0.8f); break;
                    default: AudioManager.I.Play("boss_attack_magic"); break;
                }
                var hitColor = def.Attack switch { "venom" => VenomColor, "fire" => FireColor, _ => def.Accent };
                int pending = def.Hits;
                for (int i = 0; i < def.Hits; i++)
                {
                    int index = i;
                    var shot = ShotFor(boss, def);
                    var to = hero.Chest + (Vector3)Random.insideUnitCircle * 0.5f;
                    StartCoroutine(Then(Projectile(shot.sprite, shot.from, to, shot.duration, shot.size * Random.Range(0.85f, 1.15f), shot.tint, shot.additive, shot.spin, shot.arc), () =>
                    {
                        Hit(index, hitColor);
                        pending--;
                    }));
                    if (i > 0) AudioManager.I.Play("whoosh_ui", 0.35f, 1.3f + i * 0.06f);
                    yield return new WaitForSeconds(shot.stagger);
                }
                while (pending > 0) yield return null;
            }
            if (landedHits > 0 && def.Quirk == "venom") AddPoison(def.QuirkValue);
        }

        /// <summary>How each ranged attack flies: sprite, launch point, flight time, size, tint, glow, spin, arc height, gap between shots.</summary>
        static (string sprite, Vector3 from, float duration, float size, Color tint, bool additive, float spin, float arc, float stagger) ShotFor(Fighter boss, BossDef def) => def.Attack switch
        {
            "cards" => ("vfx_card", boss.Chest + new Vector3(-0.8f, 0.3f), 0.32f, 0.8f, Color.white, false, 900f, 1.2f, 0.13f),
            "coins" => ("vfx_coin", boss.Chest + new Vector3(-1.1f, 0f), 0.3f, 0.45f, Color.white, false, 1100f, Random.Range(0.4f, 1.8f), 0.07f),
            "venom" => ("vfx_glow", boss.transform.position + new Vector3(-1.6f, boss.Height * 0.4f), 0.34f, 1.1f, VenomColor, true, 0f, 1.3f, 0.22f),
            "fire" => ("vfx_glow", boss.Head + new Vector3(-1.2f, -0.4f), 0.3f, 1.4f, new Color(1f, Random.Range(0.35f, 0.7f), 0.1f), true, 0f, 0.4f, 0.11f),
            _ => ("vfx_darkorb", boss.Chest + new Vector3(-1f, 0.4f), 0.42f, 1.5f, Color.Lerp(def.Accent, Color.white, 0.2f), true, 360f, 1.6f, 0.2f),
        };

        static IEnumerator Then(IEnumerator routine, System.Action after)
        {
            yield return routine;
            after();
        }

        /// <summary>Arachna's bite leaves venom: one more stack, ticking every boss turn until a potion or the fight's end.</summary>
        void AddPoison(int stacks)
        {
            poison += stacks;
            hud.SetPoison(poison);
            AudioManager.I.Play("poison_tick", 0.7f, 1.3f);
            hero.Flash(VenomColor, 0.8f);
            Flames(hero.Chest, VenomColor, 16, 0.8f);
            FX.I.Text(hero.Head + new Vector3(0.8f, 1.6f), $"+{stacks} POISON", VenomColor, 1.4f, 1f, 1.1f, true);
        }

        IEnumerator PoisonTick()
        {
            AudioManager.I.Play("poison_tick");
            heroHp = Mathf.Max(0, heroHp - poison);
            hud.SetHeroHp(heroHp, heroMax, true);
            hud.SetPoison(poison);
            hero.SetPose(Pose.Hurt, 0.4f);
            hero.Flash(VenomColor, 1f);
            hero.Squash(0.9f, 1.08f);
            Flames(hero.transform.position + Vector3.up * hero.Height * 0.4f, VenomColor, 30, 1.2f);
            FX.I.Text(hero.Head + Vector3.up * 0.6f, $"-{poison} POISON", VenomColor, 1.8f, 1.3f, 1.1f, true);
            PostFX.I.VignettePulse(new Color(0.2f, 0.6f, 0.05f), 0.3f);
            CameraRig.I.Shake(0.25f);
            UpdateDanger();
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>Snake-Eyes breathes on the wheel: old burns cool off, a fresh slice catches fire for a few turns.</summary>
        IEnumerator Scorch()
        {
            var boss = BossFighter;
            var fresh = new List<int>();
            for (int i = 0; i < burnTurns.Length; i++)
            {
                if (burnTurns[i] > 0 && --burnTurns[i] == 0)
                {
                    wheel.SetBurnt(i, false);
                    FX.I.Dust(wheel.SlicePoint(i, 0.8f), 6, new Color(0.3f, 0.28f, 0.28f, 0.7f));
                }
                if (burnTurns[i] == 0) fresh.Add(i);
            }
            int slot = fresh[rng.Next(fresh.Count)];
            AudioManager.I.Play("boss_attack_magic", 0.8f, 0.7f);
            boss.SetPose(Pose.Attack, 0.5f);
            var to = wheel.SlicePoint(slot, 0.75f);
            yield return Projectile("vfx_glow", boss.Head + new Vector3(-1.2f, -0.4f), to, 0.4f, 1.8f, FireColor, true, 0f, 2.5f);
            burnTurns[slot] = BossDef.ScorchTurns;
            wheel.SetBurnt(slot, true);
            RefreshTarget();
            AudioManager.I.Play("fire_sizzle", 0.9f);
            Flames(to, FireColor, 30, 1.4f);
            FX.I.Ring(to, FireColor, 5f, 0.4f);
            FX.I.Light(to, FireColor, 4f, 8f, 0.6f);
            FX.I.Text(to + Vector3.up * 1.2f, "SCORCHED", FireColor, 1.4f, 1f, 1f, true);
            wheel.Punch(0.08f);
            CameraRig.I.Shake(0.3f);
            yield return new WaitForSeconds(0.35f);
        }

        IEnumerator Projectile(string sprite, Vector3 from, Vector3 to, float duration, float size, Color tint, bool additive, float spin, float arc)
        {
            var sr = new GameObject("Projectile").AddComponent<SpriteRenderer>();
            transients.Add(sr.gameObject);
            sr.sprite = S(sprite);
            sr.sharedMaterial = additive ? config.additive : config.spriteDefault;
            sr.sortingOrder = FX.OrderFront - 10;
            sr.color = tint;
            if (sr.sprite != null) sr.transform.localScale = Vector3.one * size / sr.sprite.bounds.size.x;
            if (additive)
            {
                var glow = Instantiate(config.lightPrefab, sr.transform).GetComponent<Light2D>();
                glow.color = tint;
                glow.intensity = 1.8f;
                glow.pointLightOuterRadius = 3.5f;
            }
            var ctrl = (from + to) * 0.5f + Vector3.up * arc;
            yield return Tween.Run(duration, t =>
            {
                var a = Vector3.Lerp(from, ctrl, t);
                var b = Vector3.Lerp(ctrl, to, t);
                sr.transform.position = Vector3.Lerp(a, b, t);
                sr.transform.Rotate(0f, 0f, spin * Time.deltaTime);
                if (Random.value < 0.5f) FX.I.Sparks(sr.transform.position, tint, 1, 0.3f);
            }, Ease.InQuad);
            FX.I.Pop("vfx_impact", to, 0.3f, 2f, 0.2f, tint);
            Destroy(sr.gameObject);
        }

        /// <summary>
        /// One boss strike of <paramref name="raw"/> damage lands unless dodged; Iron Hide soaks all but 1 of its armor.
        /// Small strikes (coins, cards) shake less so a volley rattles instead of stunning. True when it landed.
        /// </summary>
        bool HeroHit(int raw, Color accent)
        {
            if (rng.NextDouble() < stats.Dodge)
            {
                Dodged(hero, -1f, new Color(1f, 0.97f, 0.88f));
                return false;
            }
            int dmg = Mathf.Max(1, raw - stats.Armor);
            if (dmg < raw) StartCoroutine(ArmorBlock(raw, raw - dmg));
            heroHp = Mathf.Max(0, heroHp - dmg);
            float power = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(1f, 10f, dmg));
            Juice.I.Hitstop(0.09f * power);
            hero.SetPose(Pose.Hurt, 0.5f);
            hero.Flash(new Color(1f, 0.25f, 0.25f), 1f);
            hero.Knock(-6f * power);
            hero.Squash(0.8f, 1.15f);
            hero.Tilt(10f * power);
            CameraRig.I.Shake(0.5f * power);
            CameraRig.I.Kick(Vector2.left, 0.18f * power);
            PostFX.I.VignettePulse(new Color(0.7f, 0f, 0.05f), 0.3f * power);
            PostFX.I.Punch(chromaAdd: 0.5f * power);
            hud.Flash(new Color(1f, 0.1f, 0.1f), 0.25f * power, 0.3f);
            hud.SetHeroHp(heroHp, heroMax, true);
            FX.I.Pop("vfx_impact", hero.Chest + (Vector3)Random.insideUnitCircle * 0.3f, 0.3f, 2.8f * power, 0.2f, accent);
            FX.I.Sparks(hero.Chest, new Color(1f, 0.3f, 0.3f), (int)(8 + 10 * power), 1.2f);
            FX.I.Light(hero.Chest, Color.Lerp(accent, Color.red, 0.5f), 3f * power, 7f, 0.35f);
            FX.I.Text(hero.Head + new Vector3(Random.Range(-0.5f, 0.5f), 0.6f), "-" + dmg, new Color(1f, 0.35f, 0.35f), 1.1f + 0.6f * power, 1.4f, 1.1f, true);
            AudioManager.I.Play("hero_hurt", 0.6f + 0.4f * power);
            AudioManager.I.Play("hit_light", 0.6f, 0.8f + (1f - power) * 0.4f);
            UpdateDanger();
            return true;
        }

        /// <summary>Later strikes of a volley glancing off the ward the first strike shattered.</summary>
        void Deflect()
        {
            var at = hero.Chest + Vector3.right * 0.6f;
            AudioManager.I.Play("hit_light", 0.6f, 1.7f);
            hero.Flash(WardColor, 0.6f);
            FX.I.Sparks(at, WardColor, 16, 1.1f, true);
            FX.I.Ring(at, WardColor, 4f, 0.25f);
        }

        /// <summary>The ward eats the whole attack and shatters in a burst of cyan glass.</summary>
        void Blocked()
        {
            warded = false;
            Destroy(wardBubble.gameObject);
            var at = hero.Chest + Vector3.right * 0.6f;
            AudioManager.I.Play("hit_heavy", 0.9f, 1.5f);
            AudioManager.I.Play("coin_burst", 0.6f, 1.6f);
            Juice.I.Hitstop(0.14f);
            hero.Flash(WardColor, 1f);
            hero.Squash(0.9f, 1.1f);
            hero.Knock(-2f);
            FX.I.Pop("vfx_impact", at, 0.4f, 4f, 0.25f, WardColor);
            FX.I.Ring(hero.Chest, WardColor, 11f, 0.45f);
            FX.I.Ring(hero.Chest, Color.white, 6f, 0.3f);
            FX.I.Sparks(at, WardColor, 50, 1.8f, true);
            FX.I.Light(at, WardColor, 6f, 11f, 0.6f);
            FX.I.Text(hero.Head + new Vector3(0f, 0.8f), "BLOCKED!", WardColor, 2f, 1.4f, 1.2f, true);
            CameraRig.I.Shake(0.45f);
            CameraRig.I.Kick(Vector2.right, 0.15f);
            PostFX.I.Punch(bloomAdd: 1.8f, chromaAdd: 0.6f);
            hud.Flash(WardColor, 0.35f, 0.3f);
        }

        /// <summary>The boss slips the hero's swing: no damage, no gold.</summary>
        void BossDodged()
        {
            var boss = BossFighter;
            Dodged(boss, 1f, boss.Accent);
            AudioManager.I.Play("near_miss", 0.8f);
            BossVoice("taunt", 0.6f);
            boss.SetPose(Pose.Attack, 0.5f);
        }

        /// <summary>A sidestep away from the blow: afterimages, a whoosh and a floating DODGE!</summary>
        void Dodged(Fighter f, float away, Color color)
        {
            AudioManager.I.Play("whoosh_ui", 1f, 1.35f);
            FX.I.AfterImage(f.Body, new Color(color.r, color.g, color.b, 0.6f));
            f.Knock(9f * away, 0.6f);
            f.Squash(1.2f, 0.85f);
            f.Tilt(-12f * away);
            FX.I.Dust(f.transform.position, 5, new Color(0.75f, 0.7f, 0.65f, 0.6f));
            FX.I.Text(f.Head + new Vector3(0f, 0.8f), "DODGE!", color, 1.8f, 1.2f, 1.1f, true);
            CameraRig.I.ZoomPunch(-0.02f);
        }

        static readonly Color SteelColor = new(0.72f, 0.85f, 1f);

        /// <summary>
        /// Iron Hide: a shield slams up between Ace and the blow, the full hit is shown struck out, and the share it
        /// soaked rings off the steel. Runs unscaled so it plays straight through the hit's hitstop.
        /// </summary>
        IEnumerator ArmorBlock(int raw, int blocked)
        {
            var at = hero.Chest + new Vector3(1.1f, 0.15f);
            AudioManager.I.Play("hit_heavy", 0.8f, 1.9f);
            AudioManager.I.Play("coin_burst", 0.5f, 0.75f);
            AudioManager.I.Play("charm_nudge", 0.6f, 0.7f);
            FX.I.Pop("vfx_impact", at, 0.4f, 3.4f, 0.22f, Color.white);
            FX.I.Ring(at, SteelColor, 7f, 0.35f);
            FX.I.Sparks(at, SteelColor, 26, 1.6f, true);
            FX.I.Sparks(at, Gold, 12, 1.2f, true);
            FX.I.Light(at, SteelColor, 5f, 8f, 0.45f);
            FX.I.Text(at + new Vector3(0.3f, 1.9f), $"<s>-{raw}</s>", new Color(0.75f, 0.75f, 0.8f), 1.1f, 0.6f, 0.8f);
            FX.I.Text(at + new Vector3(0.5f, 0.9f), $"{blocked} BLOCKED", SteelColor, 1.6f, 1.6f, 1.3f, true);
            PostFX.I.Punch(bloomAdd: 0.8f);

            var sprite = S("vfx_shield");
            if (sprite == null) yield break;
            var sr = new GameObject("ArmorShield").AddComponent<SpriteRenderer>();
            transients.Add(sr.gameObject);
            sr.sprite = sprite;
            sr.sharedMaterial = config.spriteDefault;
            sr.sortingOrder = FX.OrderFront - 5;
            float size = hero.Height * 0.55f / sprite.bounds.size.y;
            var tr = sr.transform;

            // Slam in with an overshoot, shudder under the blow, then tip back and fade.
            yield return Tween.Run(0.12f, t =>
            {
                tr.position = at + Vector3.right * (1f - t) * 0.8f;
                tr.localScale = Vector3.one * size * Mathf.LerpUnclamped(0.3f, 1f, Ease.OutBackBig(t));
                tr.rotation = Quaternion.Euler(0f, 0f, (1f - t) * -25f);
            }, Ease.Linear, true);
            FX.I.Ring(at, Color.white, 4f, 0.25f);
            yield return Tween.Run(0.3f, t =>
            {
                tr.position = at + (Vector3)(Random.insideUnitCircle * 0.12f * (1f - t)) + Vector3.left * Mathf.Sin(t * Mathf.PI) * 0.25f;
            }, Ease.Linear, true);
            yield return Tween.Run(0.35f, t =>
            {
                tr.position = at + new Vector3(-0.2f * t, 0.5f * t);
                tr.localScale = Vector3.one * size * Mathf.Lerp(1f, 0.7f, t);
                tr.rotation = Quaternion.Euler(0f, 0f, 20f * t);
                sr.color = new Color(1f, 1f, 1f, 1f - t);
            }, Ease.InQuad, true);
            Destroy(sr.gameObject);
        }

        // ---------- Between depths ----------

        IEnumerator DepthCleared()
        {
            ClearQuirks();
            yield return ShowWheel(false);
            hero.SetPose(Pose.Victory, 1.4f);
            AudioManager.I.Play("victory_stinger", 0.8f);
            FX.I.Sparks(hero.Head, Gold, 24, 1.2f);
            yield return new WaitForSeconds(0.9f);

            // Mending: heal a share of max HP between depths, not all of it.
            int regen = Mathf.Min(heroMax - heroHp, Mathf.CeilToInt(heroMax * stats.RegenFraction));
            if (regen > 0)
            {
                HealHero(regen, new Color(0.5f, 1f, 0.6f));
                hud.Banner("MENDED", $"+{regen} HP  ·  {heroHp} / {heroMax}", HUD.BannerStyle.Info, 0.7f);
                yield return new WaitForSeconds(0.8f);
            }
            if (depth == bosses.Count - 1) yield break;

            hud.Banner("DEPTH CLEARED", "Descending deeper...", HUD.BannerStyle.Info, 0.9f);
            yield return new WaitForSeconds(0.6f);

            // Old-school screen change: run off the right edge, then scroll to the next arena with the hero hidden
            // (the camera sweeps over where they left). Footsteps keep going until they run back in from the left.
            yield return RunTo(new Vector3(World.ArenaX(depth) + OffscreenX, World.GroundY), 1.15f, Ease.InQuad, false);
            hero.gameObject.SetActive(false);
            float nextX = World.ArenaX(depth + 1);
            yield return CameraRig.I.PanTo(nextX, 1.6f);
            hero.transform.position = new Vector3(nextX - OffscreenX, World.GroundY);
            hero.gameObject.SetActive(true);
        }

        /// <summary>How far from the view's centre the hero stands fully off-screen.</summary>
        float OffscreenX => CameraRig.BaseSize * cam.aspect + 3f;

        /// <summary>Run to a spot; settle stops the footsteps and lands in idle, otherwise the hero keeps running.</summary>
        IEnumerator RunTo(Vector3 target, float duration, System.Func<float, float> ease, bool settle)
        {
            hero.SetPose(Pose.Run);
            AudioManager.I.Loop("run_steps", true, 0.7f);
            var start = hero.transform.position;
            float dustTimer = 0f;
            yield return Tween.Run(duration, t =>
            {
                hero.transform.position = Vector3.Lerp(start, target, t) + Vector3.up * Mathf.Abs(Mathf.Sin(t * duration * 9f)) * 0.12f;
                dustTimer -= Time.deltaTime;
                if (dustTimer <= 0f)
                {
                    dustTimer = 0.14f;
                    FX.I.Dust(hero.transform.position, 1, new Color(0.75f, 0.7f, 0.65f, 0.5f));
                }
            }, ease);
            if (!settle) yield break;
            AudioManager.I.Loop("run_steps", false);
            hero.transform.position = target;
            hero.SetPose(Pose.Idle);
            hero.Squash(1.2f, 0.85f);
            FX.I.Dust(target, 4, new Color(0.75f, 0.7f, 0.65f, 0.6f));
        }

        IEnumerator ShowWheel(bool show)
        {
            AudioManager.I.Play("whoosh_ui", 0.8f, show ? 1f : 0.85f);
            float from = wheelRoot.localPosition.y, to = show ? WheelY : WheelHiddenY;
            yield return Tween.Run(show ? 0.55f : 0.35f, t => wheelRoot.localPosition = new Vector3(0f, Mathf.LerpUnclamped(from, to, t), 10f), show ? Ease.OutBack : Ease.InBack);
            if (!show) yield break;
            wheel.Punch(0.08f);
            CameraRig.I.Shake(0.2f);
            FX.I.Sparks(wheel.PointerTip, Gold, 20, 1.2f);
        }

        IEnumerator Move(Fighter f, Vector3 target, float duration, System.Func<float, float> ease, bool trail, float hop = 0f)
        {
            var start = f.transform.position;
            float trailTimer = 0f;
            yield return Tween.Run(duration, t =>
            {
                f.transform.position = Vector3.LerpUnclamped(start, target, t) + Vector3.up * Ease.Bump(t) * hop;
                if (!trail) return;
                trailTimer -= Time.deltaTime;
                if (trailTimer > 0f) return;
                trailTimer = 0.02f;
                FX.I.AfterImage(f.Body, new Color(1f, 0.85f, 0.5f, 0.55f));
            }, ease);
        }

        /// <summary>The current boss's own voice line: intro, attack, hurt, death or taunt (Audio/SFX/Voices/{id}_{kind}).</summary>
        void BossVoice(string kind, float volume, float pitch = 1f) => AudioManager.I.Play($"{Boss.Id}_{kind}", volume, pitch, 0.05f, 0.15f);

#if UNITY_EDITOR
        /// <summary>Editor QA hook: force the next spin's value (0 = exact kill).</summary>
        int forcedValue = -1;
        public void ForceNextValue(int value) => forcedValue = value;
#endif

        // ---------- Endings ----------

        IEnumerator Defeat()
        {
            inRun = false;
            AudioManager.I.Loop("heartbeat", false);
            AudioManager.I.Play("gameover_stinger");
            AudioManager.I.Duck(0.15f, 1.5f);
            hero.Flash(Color.white, 1f);
            hud.Banner("DEFEATED", "Fate always wins...", HUD.BannerStyle.Danger, 0.8f);
            if (hero.HasAnim(Pose.Death))
            {
                // The collapse plays at its own pace behind the end screen, with a heavy thud as the body hits the floor.
                hero.SetPose(Pose.Death, 99f);
                StartCoroutine(Thud(hero.AnimDuration(Pose.Death) * 0.65f));
                yield return new WaitForSeconds(0.625f);

                IEnumerator Thud(float delay)
                {
                    yield return new WaitForSeconds(delay);
                    AudioManager.I.Play("hit_heavy", 0.8f, 0.7f);
                    FX.I.Dust(hero.transform.position, 10, new Color(0.6f, 0.55f, 0.5f, 0.7f));
                    CameraRig.I.Shake(0.4f);
                }
            }
            else
            {
                hero.SetPose(Pose.Hurt, 99f);
                yield return Tween.Run(0.4f, t => hero.transform.rotation = Quaternion.Euler(0f, 0f, 80f * t), Ease.OutBounce);
            }
            bool record = BankRun();
            hud.ShowHud(false);
            hud.ShowEnd(false, "FATE WINS", $"{Boss.Name} stopped you on depth {depth + 1} of {bosses.Count}", GoldNote(),
                record ? "NEW BEST DEPTH!" : null, RunStats());
            AudioManager.I.PlayMusic("music_title", 2f);
        }

        IEnumerator Victory()
        {
            inRun = false;
            SaveData.Wins++;
            AudioManager.I.PlayMusic("music_victory", 0.5f);
            hero.SetPose(Pose.Victory, 99f);
            for (int i = 0; i < 4; i++)
            {
                FX.I.Confetti(hero.Head + Vector3.up * 2f + Vector3.right * Random.Range(-4f, 4f), 60);
                FX.I.Coins(hero.Chest, 20, 1.3f);
                yield return new WaitForSeconds(0.25f);
            }
            hud.Banner("CURSE BROKEN!", "You beat the odds", HUD.BannerStyle.Jackpot, 2f);
            yield return new WaitForSeconds(2.4f);
            BankRun();
            hud.ShowHud(false);
            hud.ShowEnd(true, "CURSE BROKEN!", $"All {bosses.Count} depths cleared", GoldNote(),
                SaveData.Wins == 1 ? "FIRST VICTORY!" : null, RunStats());
        }

        /// <summary>Adds the run's gold to the bank; true when the run went deeper than any before it (the first run sets no record).</summary>
        bool BankRun()
        {
            int reached = Mathf.Min(depth + 1, bosses.Count);
            bool record = SaveData.Runs > 1 && reached > SaveData.BestDepth;
            SaveData.Gold += runGold;
            SaveData.BestDepth = Mathf.Max(SaveData.BestDepth, reached);
            return record;
        }

        IEnumerable<(string, int, string, bool)> RunStats()
        {
            yield return ("SPINS", spins, "wheel_hub", false);
            yield return ("JACKPOTS", jackpots, "icon_crown", false);
            yield return ("BUSTS", busts, "icon_bust", false);
            yield return ("GOLD", runGold, "vfx_coin", true);
        }

        static string GoldNote() => $"{SaveData.Gold:N0} gold to spend in the shop";
    }
}

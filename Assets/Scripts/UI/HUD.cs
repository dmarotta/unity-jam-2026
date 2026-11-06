using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Lucky
{
    /// <summary>UI Toolkit HUD and menus. All animation runs on unscaled time so hitstop never freezes the UI.</summary>
    public class HUD : MonoBehaviour
    {
        public event Action SpinPressed, PlayPressed, RetryPressed, MenuPressed, BombPressed;

        VisualElement root, hud, flash, fade, banner, titleScreen, endScreen, settingsScreen, shopScreen;
        VisualElement heroFill, heroGhost, bossFill, bossGhost, heroBar, bossBar, heroPortrait, bossPortrait;
        VisualElement pips, goldBox, needPill, endStats, shopItems, charmBadge, charmIcon;
        Label heroHp, bossHp, bossName, bossTitle, depthLabel, goldLabel, needCaption, needValue, spinLabel;
        Label bannerTitle, bannerSub, titleStats, endTitle, endSub, musicValue, sfxValue, shopGold, bombCount, bombHint, charmLabel;
        Button spinButton, autoButton, shakeToggle, flashToggle, titlePlay, bombButton;
        VisualElement titleLogo;
        FontAsset titleFont;
        GameConfig config;
        bool bombUsable;

        int shownGold, targetGold;
        float goldTick, lastInputTime;
        bool spinReady;
        Coroutine heroGhostRoutine, bossGhostRoutine, bannerRoutine, flashRoutine;

        public bool SettingsOpen => settingsScreen.style.display == DisplayStyle.Flex;

        public void Init(GameConfig cfg)
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            config = cfg;
            titleFont = cfg.uiTitleFont;
            if (cfg.uiNumberFont != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(cfg.uiNumberFont);

            hud = Q<VisualElement>("hud");
            flash = Q<VisualElement>("flash");
            fade = Q<VisualElement>("fade");
            banner = Q<VisualElement>("banner");
            titleScreen = Q<VisualElement>("title-screen");
            endScreen = Q<VisualElement>("end-screen");
            settingsScreen = Q<VisualElement>("settings-screen");
            heroFill = Q<VisualElement>("hero-fill");
            heroGhost = Q<VisualElement>("hero-ghost");
            bossFill = Q<VisualElement>("boss-fill");
            bossGhost = Q<VisualElement>("boss-ghost");
            heroBar = Q<VisualElement>("hero-bar");
            bossBar = Q<VisualElement>("boss-bar");
            heroPortrait = Q<VisualElement>("hero-portrait");
            bossPortrait = Q<VisualElement>("boss-portrait");
            pips = Q<VisualElement>("depth-pips");
            goldBox = Q<VisualElement>("gold-box");
            needPill = Q<VisualElement>("need-pill");
            endStats = Q<VisualElement>("end-stats");
            heroHp = Q<Label>("hero-hp");
            bossHp = Q<Label>("boss-hp");
            bossName = Q<Label>("boss-name");
            bossTitle = Q<Label>("boss-title");
            depthLabel = Q<Label>("depth-label");
            goldLabel = Q<Label>("gold-label");
            needCaption = Q<Label>("need-caption");
            needValue = Q<Label>("need-value");
            spinLabel = Q<Label>("spin-label");
            bannerTitle = Q<Label>("banner-title");
            bannerSub = Q<Label>("banner-sub");
            titleStats = Q<Label>("title-stats");
            endTitle = Q<Label>("end-title");
            endSub = Q<Label>("end-sub");
            musicValue = Q<Label>("music-value");
            sfxValue = Q<Label>("sfx-value");
            spinButton = Q<Button>("spin-button");
            autoButton = Q<Button>("auto-button");
            shakeToggle = Q<Button>("shake-toggle");
            flashToggle = Q<Button>("flash-toggle");
            shopScreen = Q<VisualElement>("shop-screen");
            shopItems = Q<VisualElement>("shop-items");
            shopGold = Q<Label>("shop-gold-label");
            bombButton = Q<Button>("bomb-button");
            bombCount = Q<Label>("bomb-count");
            bombHint = Q<Label>("bomb-hint");
            charmBadge = Q<VisualElement>("charm-badge");
            charmIcon = Q<VisualElement>("charm-icon");
            charmLabel = Q<Label>("charm-label");

            if (titleFont != null)
                root.Query(className: "title-font").ForEach(e => e.style.unityFontDefinition = FontDefinition.FromSDFFont(titleFont));

            spinButton.clicked += () => { if (spinReady) SpinPressed?.Invoke(); };
            autoButton.clicked += () =>
            {
                SaveData.AutoSpin = !SaveData.AutoSpin;
                RefreshAuto();
                Click();
            };
            Q<Button>("settings-button").clicked += () => ShowSettings(true);
            Q<Button>("settings-close").clicked += () => ShowSettings(false);
            titlePlay = Q<Button>("title-play");
            titleLogo = Q<VisualElement>("title-logo");
            titlePlay.clicked += () => { Click(); PlayPressed?.Invoke(); };
            Q<Button>("end-retry").clicked += () => { Click(); RetryPressed?.Invoke(); };
            Q<Button>("end-menu").clicked += () => { Click(); MenuPressed?.Invoke(); };
            Q<Button>("title-shop").clicked += () => { Click(); ShowShop(true); };
            Q<Button>("end-shop").clicked += () => { Click(); ShowShop(true); };
            Q<Button>("shop-back").clicked += () => { Click(); ShowShop(false); };
            Q<Button>("shop-begin").clicked += () => { Click(); PlayPressed?.Invoke(); };
            bombButton.clicked += () => { if (bombUsable) BombPressed?.Invoke(); };
            BuildShop();
            Q<Button>("music-down").clicked += () => StepVolume(true, -0.1f);
            Q<Button>("music-up").clicked += () => StepVolume(true, 0.1f);
            Q<Button>("sfx-down").clicked += () => StepVolume(false, -0.1f);
            Q<Button>("sfx-up").clicked += () => StepVolume(false, 0.1f);
            shakeToggle.clicked += () => { SaveData.ScreenShake = !SaveData.ScreenShake; RefreshSettings(); Click(); };
            flashToggle.clicked += () => { SaveData.Flashes = !SaveData.Flashes; RefreshSettings(); Click(); };
            root.Query<Button>().ForEach(b => b.RegisterCallback<PointerEnterEvent>(_ => AudioManager.I?.Play("ui_hover", 0.5f)));

            RefreshAuto();
            RefreshSettings();
            ShowHud(false);
        }

        T Q<T>(string name) where T : VisualElement
        {
            var e = root.Q<T>(name);
            if (e == null) Debug.LogError($"HUD element '{name}' ({typeof(T).Name}) not found");
            return e;
        }

        static void Click() => AudioManager.I?.Play("ui_click", 0.8f);
        static string Num(int v) => v.ToString("N0", CultureInfo.InvariantCulture);

        // ---------- HUD visibility & bars ----------

        public void ShowHud(bool on)
        {
            hud.EnableInClassList("hidden", !on);
            hud.pickingMode = PickingMode.Ignore;
        }

        public void SetPortraits(Sprite hero, Sprite boss)
        {
            if (hero != null) heroPortrait.style.backgroundImage = new StyleBackground(hero);
            if (boss != null) bossPortrait.style.backgroundImage = new StyleBackground(boss);
        }

        public void SetBossInfo(BossDef def)
        {
            bossName.text = def.Name.ToUpperInvariant();
            bossTitle.text = def.Title;
            bossName.style.color = Color.Lerp(def.Accent, Color.white, 0.35f);
        }

        const float TrackWidth = 348f;

        static void SetBar(VisualElement e, float frac) => e.style.width = TrackWidth * Mathf.Clamp01(frac);

        public void SetHeroHp(int hp, int max, bool hit)
        {
            heroHp.text = $"{hp} / {max}";
            SetBar(heroFill, hp / (float)max);
            if (!hit) { SetBar(heroGhost, hp / (float)max); return; }
            Restart(ref heroGhostRoutine, Ghost(heroGhost, heroGhost.resolvedStyle.width, TrackWidth * hp / max));
            StartCoroutine(Shake(heroBar, 14f, 0.35f));
            StartCoroutine(PopScale(heroHp, 1.5f, 0.4f));
            StartCoroutine(FlashFill(heroFill));
        }

        public void SetBossHp(int hp, int max, bool hit)
        {
            bossHp.text = $"{hp} / {max}";
            SetBar(bossFill, hp / (float)max);
            if (!hit) { SetBar(bossGhost, hp / (float)max); return; }
            Restart(ref bossGhostRoutine, Ghost(bossGhost, bossGhost.resolvedStyle.width, TrackWidth * hp / max));
            StartCoroutine(Shake(bossBar, 18f, 0.4f));
            StartCoroutine(PopScale(bossHp, 1.6f, 0.45f));
            StartCoroutine(FlashFill(bossFill));
        }

        /// <summary>BUST: the bar drains to empty, then visibly refills to the wrapped value.</summary>
        public IEnumerator BossWrap(int max, int wrappedHp)
        {
            if (bossGhostRoutine != null) StopCoroutine(bossGhostRoutine);
            float from = bossFill.resolvedStyle.width;
            yield return Tween.Run(0.22f, t => { SetBar(bossFill, from / TrackWidth * (1f - t)); SetBar(bossGhost, from / TrackWidth * (1f - t)); }, Ease.InQuad, true);
            bossHp.text = $"0 / {max}";
            yield return new WaitForSecondsRealtime(0.12f);
            float target = wrappedHp / (float)max;
            yield return Tween.Run(0.75f, t =>
            {
                SetBar(bossFill, target * t);
                SetBar(bossGhost, target * t);
                bossHp.text = $"{Mathf.RoundToInt(wrappedHp * t)} / {max}";
            }, Ease.OutCubic, true);
            StartCoroutine(PopScale(bossHp, 1.5f, 0.4f));
            StartCoroutine(Shake(bossBar, 10f, 0.3f));
        }

        IEnumerator Ghost(VisualElement ghost, float from, float to)
        {
            yield return new WaitForSecondsRealtime(0.35f);
            yield return Tween.Run(0.45f, t => ghost.style.width = Mathf.Lerp(from, to, t), Ease.InOutQuad, true);
        }

        IEnumerator FlashFill(VisualElement fill)
        {
            fill.AddToClassList("flash");
            yield return new WaitForSecondsRealtime(0.07f);
            fill.RemoveFromClassList("flash");
        }

        void Restart(ref Coroutine slot, IEnumerator routine)
        {
            if (slot != null) StopCoroutine(slot);
            slot = StartCoroutine(routine);
        }

        // ---------- Depth & gold ----------

        public void SetDepth(int current, IReadOnlyList<Sprite> portraits)
        {
            if (pips.childCount != portraits.Count)
            {
                pips.Clear();
                foreach (var p in portraits)
                {
                    var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                    pip.AddToClassList("pip");
                    if (p != null) pip.style.backgroundImage = new StyleBackground(p);
                    pips.Add(pip);
                }
            }
            for (int i = 0; i < pips.childCount; i++)
            {
                pips[i].EnableInClassList("current", i == current);
                pips[i].EnableInClassList("defeated", i < current);
            }
            depthLabel.text = $"DEPTH {current + 1} / {portraits.Count}";
        }

        public void SetGold(int value, bool instant)
        {
            targetGold = value;
            if (!instant) return;
            shownGold = value;
            goldLabel.text = Num(value);
        }

        public void PopGold() => StartCoroutine(PopScale(goldBox, 1.18f, 0.2f));

        public Vector3 GoldWorldPosition(Camera cam)
        {
            var r = goldBox.worldBound;
            var panelSize = root.layout.size;
            if (panelSize.x <= 0f) return cam.transform.position;
            var screen = new Vector3(r.xMin + 30f, r.center.y, 0f);
            screen.x *= Screen.width / panelSize.x;
            screen.y = Screen.height - screen.y * Screen.height / panelSize.y;
            var w = cam.ScreenToWorldPoint(screen);
            w.z = 0f;
            return w;
        }

        // ---------- Need pill ----------

        /// <summary>Dim the pill while the wheel spins so the revealed number owns the moment.</summary>
        public void DimNeed(bool dim) => needPill.style.opacity = dim ? 0.15f : 1f;

        public void SetNeed(int hp, bool lucky)
        {
            DimNeed(false);
            needPill.EnableInClassList("lucky", lucky);
            needCaption.text = lucky ? "LUCKY NUMBER" : "EXACTLY";
            needValue.text = hp.ToString();
            StartCoroutine(PopScale(needPill, lucky ? 1.25f : 1.1f, 0.45f));
        }

        // ---------- Spin button ----------

        public void SetSpinReady(bool ready, string label = "SPIN")
        {
            spinReady = ready;
            spinButton.EnableInClassList("locked", !ready);
            spinLabel.text = label;
            if (ready) lastInputTime = Time.unscaledTime;
        }

        void RefreshAuto() => autoButton.EnableInClassList("on", SaveData.AutoSpin);

        // ---------- Loadout: firebomb button and charm badge ----------

        public void SetBomb(int count, bool usable)
        {
            bombUsable = usable;
            bombCount.text = "×" + count;
            bombButton.EnableInClassList("empty", count == 0);
            bombHint.EnableInClassList("empty", count == 0);
            bombButton.EnableInClassList("locked", !usable);
        }

        public void SetCharm(Sprite icon, string label)
        {
            charmBadge.EnableInClassList("on", icon != null);
            if (icon == null) return;
            charmIcon.style.backgroundImage = new StyleBackground(icon);
            charmLabel.text = label;
        }

        // ---------- Banner, flash, fade ----------

        public enum BannerStyle { Jackpot, Bust, Intro, Info, Danger }

        public void Banner(string title, string sub, BannerStyle style, float hold = 1.1f)
        {
            Restart(ref bannerRoutine, BannerRoutine(title, sub, style, hold));
        }

        IEnumerator BannerRoutine(string title, string sub, BannerStyle style, float hold)
        {
            bannerTitle.text = title;
            bannerSub.text = sub;
            bannerTitle.style.unityFontDefinition = style == BannerStyle.Intro && titleFont != null
                ? new StyleFontDefinition(FontDefinition.FromSDFFont(titleFont))
                : new StyleFontDefinition(StyleKeyword.Null);
            bannerTitle.style.fontSize = style == BannerStyle.Intro ? 104f : style == BannerStyle.Jackpot ? 170f : 150f;
            bannerTitle.style.color = style switch
            {
                BannerStyle.Bust => new Color(1f, 0.35f, 0.45f),
                BannerStyle.Danger => new Color(1f, 0.3f, 0.3f),
                BannerStyle.Info => new Color(1f, 0.95f, 0.85f),
                _ => new Color(1f, 0.84f, 0.36f),
            };
            float spin = style == BannerStyle.Bust ? -8f : style == BannerStyle.Jackpot ? 6f : 0f;
            banner.style.opacity = 1f;
            yield return Tween.Run(0.32f, t =>
            {
                float s = Mathf.LerpUnclamped(3f, 1f, Ease.OutBackBig(t));
                banner.style.scale = new Scale(new Vector3(s, s, 1f));
                banner.style.rotate = new Rotate(spin * (1f - t));
            }, Ease.Linear, true);
            float until = Time.unscaledTime + hold;
            while (Time.unscaledTime < until)
            {
                if (style == BannerStyle.Jackpot)
                {
                    // Warm shimmer only (rose → orange → gold), never off-palette hues.
                    float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f);
                    bannerTitle.style.color = Color.HSVToRGB(Mathf.Lerp(0.97f, 1.13f, k) % 1f, Mathf.Lerp(0.35f, 0.75f, k), 1f);
                }
                float wobble = 1f + Mathf.Sin(Time.unscaledTime * 9f) * 0.02f;
                banner.style.scale = new Scale(new Vector3(wobble, wobble, 1f));
                yield return null;
            }
            yield return Tween.Run(0.22f, t =>
            {
                banner.style.opacity = 1f - t;
                float s = 1f + t * 0.3f;
                banner.style.scale = new Scale(new Vector3(s, s, 1f));
            }, Ease.InQuad, true);
        }

        public void Flash(Color color, float alpha, float duration)
        {
            if (!SaveData.Flashes) alpha *= 0.25f;
            Restart(ref flashRoutine, FlashRoutine(color, alpha, duration));
        }

        IEnumerator FlashRoutine(Color color, float alpha, float duration)
        {
            flash.style.backgroundColor = color;
            yield return Tween.Run(duration, t => flash.style.opacity = alpha * (1f - t), Ease.OutQuad, true);
        }

        public IEnumerator Fade(float to, float duration)
        {
            float from = fade.resolvedStyle.opacity;
            yield return Tween.Run(duration, t => fade.style.opacity = Mathf.Lerp(from, to, t), Ease.InOutQuad, true);
        }

        // ---------- Element animation helpers ----------

        IEnumerator PopScale(VisualElement e, float amount, float duration)
        {
            yield return Tween.Run(duration, t =>
            {
                float s = Mathf.LerpUnclamped(amount, 1f, Ease.OutElastic(t));
                e.style.scale = new Scale(new Vector3(s, s, 1f));
            }, Ease.Linear, true);
        }

        IEnumerator Shake(VisualElement e, float amount, float duration)
        {
            yield return Tween.Run(duration, t =>
            {
                float k = amount * (1f - t);
                e.style.translate = new Translate(UnityEngine.Random.Range(-k, k), UnityEngine.Random.Range(-k, k) * 0.6f);
            }, Ease.Linear, true);
            e.style.translate = new Translate(0, 0);
        }

        // ---------- Screens ----------

        public void ShowTitle(bool on)
        {
            titleScreen.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            RefreshTitleStats();
            StartCoroutine(PopScale(titleLogo, 1.12f, 0.9f));
        }

        void RefreshTitleStats()
        {
            titleStats.text = SaveData.Runs > 0
                ? $"Gold banked: {Num(SaveData.Gold)}   ·   Deepest: Depth {Mathf.Max(1, SaveData.BestDepth)}   ·   Curses broken: {SaveData.Wins}"
                : "Click BEGIN to descend";
        }

        // ---------- Shop ----------

        readonly List<(ShopItem item, VisualElement card)> shopCards = new();

        public void ShowShop(bool on)
        {
            bool wasOpen = shopScreen.style.display == DisplayStyle.Flex;
            shopScreen.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on)
            {
                if (wasOpen) AudioManager.I?.Play("whoosh_ui", 0.6f, 0.85f);
                RefreshTitleStats();
                return;
            }
            AudioManager.I?.Play("whoosh_ui", 0.7f);
            RefreshShop();
            var panel = Q<VisualElement>("shop-panel");
            StartCoroutine(Tween.Run(0.35f, t =>
            {
                float s = Mathf.LerpUnclamped(0.7f, 1f, Ease.OutBack(t));
                panel.style.scale = new Scale(new Vector3(s, s, 1f));
                panel.style.opacity = t;
            }, Ease.Linear, true));
        }

        void BuildShop()
        {
            foreach (var item in Balance.Shop)
            {
                var card = new VisualElement();
                card.AddToClassList("shop-card");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("card-icon");
                var sprite = config.Sprite(item.Icon);
                if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                card.Add(icon);
                card.Add(CardLabel("card-name", item.Name));
                card.Add(CardLabel("card-level", ""));
                card.Add(CardLabel("card-desc", item.Desc.Replace("{value}", item.Value.ToString()).Replace("{base}", Balance.HeroHp.ToString())));
                var buy = new Button(() => Buy(item, card));
                buy.AddToClassList("card-buy");
                var coin = new VisualElement { pickingMode = PickingMode.Ignore };
                coin.AddToClassList("card-coin");
                buy.Add(coin);
                buy.Add(CardLabel("card-price", ""));
                card.Add(buy);
                shopItems.Add(card);
                shopCards.Add((item, card));
            }
        }

        static Label CardLabel(string className, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }

        void RefreshShop()
        {
            shopGold.text = Num(SaveData.Gold);
            foreach (var (item, card) in shopCards)
            {
                int level = item.Level;
                card.EnableInClassList("done", item.Maxed);
                card.EnableInClassList("locked", !item.Maxed && item.Locked);
                card.EnableInClassList("poor", !item.Maxed && !item.Locked && item.Price > SaveData.Gold);
                card.Q<Label>(className: "card-level").text = item.Consumable ? $"Carrying {level} / {item.Max}"
                    : item.Max > 1 ? $"Level {level} / {item.Max}"
                    : level > 0 ? "Owned" : "";
                var requires = Balance.Shop.Find(i => i.Id == item.Requires);
                card.Q<Label>(className: "card-price").text = item.Maxed ? (item.Consumable ? "FULL" : item.Max > 1 ? "MAXED" : "OWNED")
                    : item.Locked ? $"Needs {requires?.Name}"
                    : Num(item.Price);
            }
        }

        void Buy(ShopItem item, VisualElement card)
        {
            int before = SaveData.Gold;
            if (!item.TryBuy())
            {
                AudioManager.I?.Play("shop_deny", 0.6f);
                StartCoroutine(Shake(card, 12f, 0.3f));
                return;
            }
            AudioManager.I?.Play("shop_buy");
            if (item.Kind == ItemKind.MaxHp) AudioManager.I?.Play("hp_upgrade", 0.8f);
            RefreshShop();
            StartCoroutine(PopScale(card, 1.1f, 0.45f));
            StartCoroutine(PopScale(card.Q(className: "card-icon"), 1.4f, 0.6f));
            int after = SaveData.Gold;
            StartCoroutine(Tween.Run(0.45f, t => shopGold.text = Num(Mathf.RoundToInt(Mathf.Lerp(before, after, t))), Ease.OutCubic, true));
        }

        public void ShowEnd(bool victory, string title, string sub, IEnumerable<(string name, string value, bool gold)> stats)
        {
            endScreen.style.display = DisplayStyle.Flex;
            endTitle.text = title;
            endTitle.style.color = victory ? new Color(1f, 0.84f, 0.36f) : new Color(1f, 0.42f, 0.45f);
            endSub.text = sub;
            endStats.Clear();
            foreach (var (name, value, gold) in stats)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("stat-row");
                var n = new Label(name) { pickingMode = PickingMode.Ignore };
                n.AddToClassList("stat-name");
                var v = new Label(value) { pickingMode = PickingMode.Ignore };
                v.AddToClassList("stat-value");
                v.EnableInClassList("gold", gold);
                row.Add(n);
                row.Add(v);
                endStats.Add(row);
            }
            var panel = Q<VisualElement>("end-panel");
            StartCoroutine(Tween.Run(0.45f, t =>
            {
                float s = Mathf.LerpUnclamped(0.6f, 1f, Ease.OutBack(t));
                panel.style.scale = new Scale(new Vector3(s, s, 1f));
                panel.style.opacity = t;
            }, Ease.Linear, true));
        }

        public void HideEnd() => endScreen.style.display = DisplayStyle.None;

        public void ShowSettings(bool on)
        {
            Click();
            settingsScreen.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshSettings();
            Juice.I.Paused = on;
        }

        void StepVolume(bool music, float delta)
        {
            if (music) SaveData.MusicVolume = Mathf.Clamp01(Mathf.Round((SaveData.MusicVolume + delta) * 10f) / 10f);
            else SaveData.SfxVolume = Mathf.Clamp01(Mathf.Round((SaveData.SfxVolume + delta) * 10f) / 10f);
            RefreshSettings();
            Click();
        }

        void RefreshSettings()
        {
            musicValue.text = $"{Mathf.RoundToInt(SaveData.MusicVolume * 100)}%";
            sfxValue.text = $"{Mathf.RoundToInt(SaveData.SfxVolume * 100)}%";
            shakeToggle.text = SaveData.ScreenShake ? "ON" : "OFF";
            shakeToggle.EnableInClassList("on", SaveData.ScreenShake);
            flashToggle.text = SaveData.Flashes ? "ON" : "OFF";
            flashToggle.EnableInClassList("on", SaveData.Flashes);
        }

        void Update()
        {
            // Count-up toward the target gold, ticking a coin sound.
            if (shownGold < targetGold)
            {
                goldTick -= Time.unscaledDeltaTime;
                if (goldTick <= 0f)
                {
                    goldTick = 0.03f;
                    int step = Mathf.Max(1, (targetGold - shownGold) / 12);
                    shownGold = Mathf.Min(targetGold, shownGold + step);
                    goldLabel.text = Num(shownGold);
                    AudioManager.I?.Play("coin_tick", 0.3f, 1f + Mathf.Min(0.5f, (targetGold - shownGold) * 0.01f), 0.03f, 0.025f);
                }
            }

            // Title: floating logo and a breathing BEGIN button.
            if (titleScreen.style.display == DisplayStyle.Flex)
            {
                float t = Time.unscaledTime;
                titleLogo.style.translate = new Translate(0f, Mathf.Sin(t * 1.6f) * 10f);
                float s = 1f + Mathf.Max(0f, Mathf.Sin(t * 3.2f)) * 0.05f;
                titlePlay.style.scale = new Scale(new Vector3(s, s, 1f));
            }

            // Idle nudge: pulse the SPIN button after 4 s without input.
            bool nudge = spinReady && Time.unscaledTime - lastInputTime > 4f;
            if (nudge)
            {
                float s = 1f + 0.06f * Mathf.Max(0f, Mathf.Sin((Time.unscaledTime - lastInputTime) * 5f));
                spinButton.style.scale = new Scale(new Vector3(s, s, 1f));
            }
            else if (nudging) spinButton.style.scale = StyleKeyword.Null;
            nudging = nudge;
        }

        bool nudging;
    }
}

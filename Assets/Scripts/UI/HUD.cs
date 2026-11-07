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
        public event Action SpinPressed, PlayPressed, RetryPressed, MenuPressed, QuitPressed;
        public event Action<ShopItem> ItemPressed;

        VisualElement root, hud, flash, fade, banner, titleScreen, endScreen, settingsScreen, shopScreen, resetScreen, resetPanel;
        VisualElement heroFill, heroGhost, bossFill, bossGhost, heroBar, bossBar, heroPortrait, bossPortrait;
        VisualElement pips, goldBox, needPill, endStats, shopStats, shopDetail, shopDetailIcon, shopDetailCoin, charmBadge, charmIcon, itemBar;
        Label heroHp, bossHp, bossName, bossTitle, depthLabel, goldLabel, needValue, spinLabel;
        Label bannerTitle, bannerSub, endTitle, endSub, endNote, endRibbon, shopGold, charmLabel;
        Label shopDetailName, shopDetailLevel, shopDetailDesc, shopDetailCost;
        Button spinButton, autoButton, shakeToggle, flashToggle, titlePlay, endRetry, settingsQuit, titleReset;
        VisualElement titleLogo, titleTagline, titleButtons, titleStats, titleWinsStat, titlePlayGlow;
        Label titleGold, titleDepth, titleWins, titleShopBadge, endShopBadge;
        VisualElement endPanel, endButtons, endFx, settingsPanel, musicPips, sfxPips;
        VisualElement loadingScreen, loadingLogo, loadingRing, loadingHub, loadingFill;
        VisualElement bossTraits, heroStatus, quirkChip, poisonChip, tarotPicker, tarotRow, tarotCaption, activeCard, activeCardArt;
        Label activeCardName, activeCardUp, activeCardDown;
        Label loadingLabel;
        List<VisualElement> shines;
        FontAsset titleFont;
        GameConfig config;

        int shownGold, targetGold;
        float goldTick, lastInputTime;
        bool spinReady, endReady, quitArmed;
        Coroutine heroGhostRoutine, bossGhostRoutine, bannerRoutine, flashRoutine, titleRoutine, endRoutine;

        public bool SettingsOpen => settingsScreen.style.display == DisplayStyle.Flex;
        public bool ShopOpen => shopScreen.style.display == DisplayStyle.Flex;
        bool ResetOpen => resetScreen.style.display == DisplayStyle.Flex;
        bool TitleOpen => titleScreen.style.display == DisplayStyle.Flex && !ShopOpen && !ResetOpen;
        bool EndOpen => endScreen.style.display == DisplayStyle.Flex && !ShopOpen;

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
            resetScreen = Q<VisualElement>("reset-screen");
            resetPanel = Q<VisualElement>("reset-panel");
            titleReset = Q<Button>("title-reset");
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
            needValue = Q<Label>("need-value");
            spinLabel = Q<Label>("spin-label");
            bannerTitle = Q<Label>("banner-title");
            bannerSub = Q<Label>("banner-sub");
            titleTagline = Q<VisualElement>("title-tagline");
            titleButtons = Q<VisualElement>("title-buttons");
            titleStats = Q<VisualElement>("title-stats");
            titleWinsStat = Q<VisualElement>("title-wins-stat");
            titlePlayGlow = Q<VisualElement>("title-play-glow");
            titleGold = Q<Label>("title-gold");
            titleDepth = Q<Label>("title-depth");
            titleWins = Q<Label>("title-wins");
            titleShopBadge = Q<Label>("title-shop-badge");
            endPanel = Q<VisualElement>("end-panel");
            endTitle = Q<Label>("end-title");
            endSub = Q<Label>("end-sub");
            endRibbon = Q<Label>("end-ribbon");
            endNote = Q<Label>("end-note");
            endButtons = Q<VisualElement>("end-buttons");
            endFx = Q<VisualElement>("end-fx");
            endRetry = Q<Button>("end-retry");
            endShopBadge = Q<Label>("end-shop-badge");
            settingsPanel = Q<VisualElement>("settings-panel");
            settingsQuit = Q<Button>("settings-quit");
            musicPips = Q<VisualElement>("music-pips");
            sfxPips = Q<VisualElement>("sfx-pips");
            spinButton = Q<Button>("spin-button");
            autoButton = Q<Button>("auto-button");
            shakeToggle = Q<Button>("shake-toggle");
            flashToggle = Q<Button>("flash-toggle");
            shopScreen = Q<VisualElement>("shop-screen");
            shopGold = Q<Label>("shop-gold-label");
            shopStats = Q<VisualElement>("shop-stats");
            shopDetail = Q<VisualElement>("shop-detail");
            shopDetailIcon = Q<VisualElement>("shop-detail-icon");
            shopDetailCoin = Q<VisualElement>("shop-detail-coin");
            shopDetailName = Q<Label>("shop-detail-name");
            shopDetailLevel = Q<Label>("shop-detail-level");
            shopDetailDesc = Q<Label>("shop-detail-desc");
            shopDetailCost = Q<Label>("shop-detail-cost");
            itemBar = Q<VisualElement>("item-bar");
            charmBadge = Q<VisualElement>("charm-badge");
            charmIcon = Q<VisualElement>("charm-icon");
            charmLabel = Q<Label>("charm-label");
            loadingScreen = Q<VisualElement>("loading-screen");
            loadingLogo = Q<VisualElement>("loading-logo");
            loadingRing = Q<VisualElement>("loading-ring");
            loadingHub = Q<VisualElement>("loading-hub");
            loadingFill = Q<VisualElement>("loading-fill");
            loadingLabel = Q<Label>("loading-label");
            bossTraits = Q<VisualElement>("boss-traits");
            heroStatus = Q<VisualElement>("hero-status");
            tarotPicker = Q<VisualElement>("tarot-picker");
            tarotRow = Q<VisualElement>("tarot-row");
            tarotCaption = Q<VisualElement>("tarot-caption");
            activeCard = Q<VisualElement>("active-card");
            activeCardArt = Q<VisualElement>("active-card-art");
            activeCardName = Q<Label>("active-card-name");
            activeCardUp = Q<Label>("active-card-up");
            activeCardDown = Q<Label>("active-card-down");

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
            endRetry.clicked += () => { Click(); RetryPressed?.Invoke(); };
            settingsQuit.clicked += () =>
            {
                Click();
                if (quitArmed) QuitPressed?.Invoke();
                else ArmQuit(true);
            };
            shines = root.Query(className: "btn-shine").ToList();
            Q<Button>("end-menu").clicked += () => { Click(); MenuPressed?.Invoke(); };
            Q<Button>("title-shop").clicked += () => { Click(); ShowShop(true); };
            Q<Button>("end-shop").clicked += () => { Click(); ShowShop(true); };
            titleReset.clicked += () => ShowReset(true);
            Q<Button>("reset-cancel").clicked += () => ShowReset(false);
            Q<Button>("reset-confirm").clicked += () =>
            {
                SaveData.ResetProgress();
                ShowReset(false);
                RefreshTitleStats();
                RefreshShopBadges();
            };
            Q<Button>("shop-back").clicked += () => { Click(); ShowShop(false); };
            Q<Button>("shop-begin").clicked += () => { Click(); PlayPressed?.Invoke(); };
            tree = new SkillTree(Q<VisualElement>("tree-viewport"), cfg, this);
            tree.Hovered += ShowDetail;
            tree.Bought += OnBought;
            Q<Button>("music-down").clicked += () => SetVolume(true, SaveData.MusicVolume - 0.1f);
            Q<Button>("music-up").clicked += () => SetVolume(true, SaveData.MusicVolume + 0.1f);
            Q<Button>("sfx-down").clicked += () => SetVolume(false, SaveData.SfxVolume - 0.1f);
            Q<Button>("sfx-up").clicked += () => SetVolume(false, SaveData.SfxVolume + 0.1f);
            BuildVolumePips(musicPips, true);
            BuildVolumePips(sfxPips, false);
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

        /// <summary>Name, title, and chips under the HP bar: attack shape (hits × damage), dodge, and the signature quirk.</summary>
        public void SetBossInfo(BossDef def)
        {
            bossName.text = def.Name.ToUpperInvariant();
            bossTitle.text = def.Title;
            bossName.style.color = Color.Lerp(def.Accent, Color.white, 0.35f);
            bossTraits.Clear();
            string perHit = def.DmgMin == def.DmgMax ? def.DmgMin.ToString() : $"{def.DmgMin}-{def.DmgMax}";
            bossTraits.Add(Trait("icon_attack", def.Hits > 1 ? $"{def.Hits} × {perHit}" : perHit, null));
            if (def.Dodge > 0f) bossTraits.Add(Trait("icon_dodge", Pct(def.Dodge), null));
            quirkChip = Quirks.TryGetValue(def.Quirk, out var quirk) ? Trait(quirk.icon, quirk.text(def), "quirk") : null;
            if (quirkChip != null) bossTraits.Add(quirkChip);
        }

        static string Pct(float chance) => $"{Mathf.RoundToInt(chance * 100f)}%";

        static readonly Dictionary<string, (string icon, Func<BossDef, string> text)> Quirks = new()
        {
            { "block", ("icon_block", d => $"BLOCKS {d.QuirkValue}  ·  {Pct(d.QuirkChance)}") },
            { "swallow", ("icon_swallow", d => $"GULPS HITS  ·  {Pct(d.QuirkChance)}") },
            { "tarot", ("tarot_back", _ => "DEALS TAROT") },
            { "counter", ("icon_counter", _ => "COUNTERS BUSTS") },
            { "scorch", ("icon_flame", _ => "SCORCHES SLICES") },
            { "venom", ("icon_poison", _ => "VENOMOUS") },
            { "phylactery", ("icon_phylactery", _ => "2 LIVES") },
        };

        VisualElement Trait(string icon, string text, string extraClass)
        {
            var chip = Box("trait");
            if (extraClass != null) chip.AddToClassList(extraClass);
            var image = Box("trait-icon");
            var sprite = config.Sprite(icon);
            if (sprite != null) image.style.backgroundImage = new StyleBackground(sprite);
            chip.Add(image);
            chip.Add(CardLabel("trait-label", text));
            return chip;
        }

        /// <summary>The boss's chips pop one after another so the new rules get noticed on arrival.</summary>
        public IEnumerator PulseTraits()
        {
            for (int i = bossTraits.childCount - 1; i >= 0; i--)
            {
                StartCoroutine(PopScale(bossTraits[i], 1.6f, 0.5f));
                AudioManager.I?.Play("ui_hover", 0.7f, 1.2f + i * 0.1f);
                yield return new WaitForSecondsRealtime(0.12f);
            }
        }

        /// <summary>The Lich's phylactery is gone: his quirk chip cracks to "LAST LIFE".</summary>
        public void SpendLife()
        {
            if (quirkChip == null) return;
            quirkChip.Q<Label>().text = "LAST LIFE";
            quirkChip.AddToClassList("spent");
            StartCoroutine(PopScale(quirkChip, 1.8f, 0.6f));
            StartCoroutine(Shake(quirkChip, 12f, 0.5f));
        }

        /// <summary>Poison stacks on the hero, shown as a chip under their bar; 0 clears it.</summary>
        public void SetPoison(int stacks)
        {
            if (stacks <= 0)
            {
                heroStatus.Clear();
                poisonChip = null;
                return;
            }
            if (poisonChip == null) heroStatus.Add(poisonChip = Trait("icon_poison", "", "poison"));
            poisonChip.Q<Label>().text = $"POISON {stacks}";
            StartCoroutine(PopScale(poisonChip, 1.5f, 0.45f));
        }

        // ---------- Tarot ----------

        public bool PickingCard { get; private set; }
        int cardChoice = -1;

        public void ChooseCard(int index)
        {
            if (!PickingCard || index < 0 || index >= tarotRow.childCount) return;
            cardChoice = index;
        }

        /// <summary>
        /// Madame Fortuna deals the offer face down, flips it, and waits for a click or number key. The pick pops, the
        /// rest fall away, then <paramref name="chosen"/> gets the card.
        /// </summary>
        public IEnumerator PickCard(IReadOnlyList<TarotCard> offer, Action<TarotCard> chosen)
        {
            tarotRow.Clear();
            var cards = new List<(VisualElement root, VisualElement art, VisualElement plate)>();
            for (int i = 0; i < offer.Count; i++)
            {
                int index = i;
                var card = new Button(() => ChooseCard(index));
                card.AddToClassList("tarot-card");
                card.Add(Box("tarot-glow"));
                var art = Box("tarot-art");
                art.style.backgroundImage = new StyleBackground(config.Sprite("tarot_back"));
                card.Add(art);
                var plate = Box("tarot-plate");
                var name = CardLabel("tarot-name", offer[i].Name.ToUpperInvariant());
                if (titleFont != null) name.style.unityFontDefinition = FontDefinition.FromSDFFont(titleFont);
                plate.Add(name);
                plate.Add(CardLabel("tarot-up", offer[i].Up));
                plate.Add(CardLabel("tarot-down", offer[i].Down));
                plate.style.opacity = 0f;
                card.Add(plate);
                card.Add(CardLabel("tarot-key", (i + 1).ToString()));
                card.RegisterCallback<PointerEnterEvent>(_ => AudioManager.I?.Play("ui_hover", 0.5f));
                card.style.opacity = 0f;
                tarotRow.Add(card);
                cards.Add((card, art, plate));
            }

            cardChoice = -1;
            tarotPicker.style.display = DisplayStyle.Flex;
            tarotPicker.pickingMode = PickingMode.Position;
            StartCoroutine(Tween.Run(0.3f, t => tarotPicker.style.backgroundColor = new Color(0.05f, 0.02f, 0.08f, 0.65f * t), Ease.OutQuad, true));
            StartCoroutine(Rise(tarotCaption, 40f, 0.35f));
            for (int i = 0; i < cards.Count; i++)
            {
                AudioManager.I?.Play("card_deal", 0.9f, 1f + i * 0.1f);
                StartCoroutine(DealIn(cards[i].root));
                yield return new WaitForSecondsRealtime(0.14f);
            }
            yield return new WaitForSecondsRealtime(0.35f);
            for (int i = 0; i < cards.Count; i++)
            {
                AudioManager.I?.Play("reveal_small", 0.7f, 1.25f + i * 0.1f);
                StartCoroutine(FlipUp(cards[i].art, cards[i].plate, config.Sprite(offer[i].Sprite)));
                yield return new WaitForSecondsRealtime(0.12f);
            }
            PickingCard = true;
            while (cardChoice < 0) yield return null;
            PickingCard = false;
            tarotPicker.pickingMode = PickingMode.Ignore;

            var pick = cards[cardChoice].root;
            AudioManager.I?.Play("card_pick");
            Flash(new Color(0.8f, 0.5f, 1f), 0.3f, 0.35f);
            StartCoroutine(PopScale(pick, 1.25f, 0.5f));
            for (int i = 0; i < cards.Count; i++)
                if (i != cardChoice) StartCoroutine(Discard(cards[i].root));
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Tween.Run(0.3f, t =>
            {
                pick.style.opacity = 1f - t;
                pick.style.translate = new Translate(0f, -140f * t);
                tarotCaption.style.opacity = 1f - t;
                tarotPicker.style.backgroundColor = new Color(0.05f, 0.02f, 0.08f, 0.65f * (1f - t));
            }, Ease.InQuad, true);
            tarotPicker.style.display = DisplayStyle.None;
            chosen(offer[cardChoice]);
        }

        IEnumerator DealIn(VisualElement card) => Tween.Run(0.4f, t =>
        {
            card.style.opacity = Mathf.Clamp01(t * 3f);
            card.style.translate = new Translate(900f * (1f - t), -220f * (1f - t));
            card.style.rotate = new Rotate(35f * (1f - t));
        }, Ease.OutBack, true);

        IEnumerator FlipUp(VisualElement art, VisualElement plate, Sprite face)
        {
            yield return Tween.Run(0.12f, t => art.style.scale = new Scale(new Vector3(1f - t, 1f, 1f)), Ease.InQuad, true);
            if (face != null) art.style.backgroundImage = new StyleBackground(face);
            StartCoroutine(Rise(plate, 24f, 0.25f));
            yield return Tween.Run(0.18f, t => art.style.scale = new Scale(new Vector3(t, 1f, 1f)), Ease.OutBack, true);
        }

        IEnumerator Discard(VisualElement card) => Tween.Run(0.4f, t =>
        {
            card.style.opacity = 1f - t;
            card.style.translate = new Translate(0f, 280f * t);
            card.style.rotate = new Rotate(-25f * t);
        }, Ease.InQuad, true);

        /// <summary>The card in play this turn, as a badge above the arena; null hides it.</summary>
        public void ShowActiveCard(TarotCard card)
        {
            activeCard.style.display = card != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (card == null) return;
            activeCardArt.style.backgroundImage = new StyleBackground(config.Sprite(card.Sprite));
            activeCardName.text = card.Name.ToUpperInvariant();
            activeCardUp.text = card.Up;
            activeCardDown.text = card.Down;
            StartCoroutine(PopScale(activeCard, 1.4f, 0.5f));
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

        /// <summary>Healing: the bar surges up to the new value with a pop.</summary>
        public void HealHero(int hp, int max)
        {
            heroHp.text = $"{hp} / {max}";
            if (heroGhostRoutine != null) StopCoroutine(heroGhostRoutine);
            float from = heroFill.resolvedStyle.width, to = TrackWidth * hp / max;
            StartCoroutine(Tween.Run(0.5f, t =>
            {
                float w = Mathf.Lerp(from, to, t);
                heroFill.style.width = w;
                heroGhost.style.width = w;
            }, Ease.OutCubic, true));
            StartCoroutine(PopScale(heroHp, 1.6f, 0.5f));
            StartCoroutine(PopScale(heroBar, 1.08f, 0.4f));
            StartCoroutine(FlashFill(heroFill));
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

        public void SetGold(int value)
        {
            targetGold = shownGold = value;
            goldLabel.text = Num(value);
        }

        /// <summary>Raises the gold target; the label counts up to it.</summary>
        public void AddGold(int amount) => targetGold += amount;

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

        /// <summary>Shown only while some slice can land exactly on the boss's HP.</summary>
        public void SetNeed(int hp, bool lucky)
        {
            DimNeed(false);
            needPill.style.display = lucky ? DisplayStyle.Flex : DisplayStyle.None;
            needValue.text = hp.ToString();
            if (lucky) StartCoroutine(PopScale(needPill, 1.25f, 0.45f));
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

        // ---------- Loadout: consumable bar and charm badge ----------

        readonly List<(ShopItem item, Button button)> itemButtons = new();
        readonly Dictionary<ShopItem, string> itemBlockers = new();
        VisualElement itemTip;
        Label itemTipName, itemTipDesc, itemTipStatus;
        ShopItem tipItem;

        /// <summary>One button per consumable, hotkeyed 1..n in shop order; unbought ones hide, spent ones grey out.</summary>
        public void BuildItems(List<ShopItem> items)
        {
            itemBar.Clear();
            itemButtons.Clear();
            itemTip = new VisualElement { pickingMode = PickingMode.Ignore };
            itemTip.AddToClassList("item-tip");
            itemTip.Add(itemTipName = CardLabel("item-tip-name", ""));
            itemTipName.AddToClassList("title-font");
            itemTip.Add(itemTipDesc = CardLabel("item-tip-desc", ""));
            itemTip.Add(itemTipStatus = CardLabel("item-tip-status", ""));
            itemBar.Add(itemTip);
            tipItem = null;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var button = new Button(() => ItemPressed?.Invoke(item));
                button.AddToClassList("item-button");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("item-icon");
                var sprite = config.Sprite(item.Icon);
                if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                button.Add(icon);
                button.Add(CardLabel("item-count", ""));
                button.Add(CardLabel("item-key", (i + 1).ToString()));
                button.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    AudioManager.I?.Play("ui_hover", 0.5f);
                    tipItem = item;
                    itemTip.style.top = button.layout.center.y;
                    RefreshTip();
                });
                button.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (tipItem != item) return;
                    tipItem = null;
                    RefreshTip();
                });
                itemBar.Add(button);
                itemButtons.Add((item, button));
            }
        }

        /// <summary><paramref name="blocker"/> gives why an item can't be used right now, or null if it can.</summary>
        public void RefreshItems(Func<ShopItem, string> blocker, Dictionary<ShopItem, int> charges)
        {
            foreach (var (item, button) in itemButtons)
            {
                int left = charges.GetValueOrDefault(item), stock = Mathf.Min(item.Level, item.Max);
                itemBlockers[item] = blocker(item);
                button.EnableInClassList("empty", stock == 0);
                button.EnableInClassList("spent", stock > 0 && left == 0);
                button.EnableInClassList("locked", itemBlockers[item] != null);
                button.Q<Label>(className: "item-count").text = $"{left}/{stock}";
            }
            RefreshTip();
        }

        /// <summary>Hover card beside the toolbar: what the item does, and why it's locked or its hotkey.</summary>
        void RefreshTip()
        {
            itemTip.style.display = tipItem != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (tipItem == null) return;
            itemTipName.text = tipItem.Name.ToUpperInvariant();
            itemTipDesc.text = tipItem.Desc.Replace("{value}", tipItem.Value.ToString());
            var reason = itemBlockers.GetValueOrDefault(tipItem);
            itemTipStatus.text = reason ?? $"Click or press {itemButtons.FindIndex(b => b.item == tipItem) + 1} to use";
            itemTipStatus.EnableInClassList("blocked", reason != null);
        }

        public void PopItem(ShopItem item)
        {
            var (_, button) = itemButtons.Find(b => b.item == item);
            if (button == null) return;
            StartCoroutine(PopScale(button, 1.45f, 0.5f));
            StartCoroutine(FlashFill(button));
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

        // ---------- Loading screen ----------

        static readonly string[] LoadingLines =
        {
            "Shuffling the cards of fate", "Oiling the Wheel of Fate", "Lighting the torches", "Waking the bosses", "Loading the dice",
        };

        bool Loading => loadingScreen.style.display != DisplayStyle.None;

        public void SetLoading(float progress)
        {
            loadingFill.style.width = Length.Percent(progress * 100f);
            loadingLabel.text = LoadingLines[Mathf.Min(LoadingLines.Length - 1, (int)(progress * LoadingLines.Length))];
        }

        /// <summary>Fades the loading screen off the title; the title is clickable as soon as the fade starts.</summary>
        public IEnumerator HideLoading()
        {
            loadingScreen.pickingMode = PickingMode.Ignore;
            yield return Tween.Run(0.6f, t =>
            {
                loadingScreen.style.opacity = 1f - t;
                float s = 1f + t * 0.08f;
                loadingLogo.style.scale = new Scale(new Vector3(s, s, 1f));
            }, Ease.InOutQuad, true);
            loadingScreen.style.display = DisplayStyle.None;
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

        /// <summary>Fade in while rising <paramref name="distance"/> px into place.</summary>
        IEnumerator Rise(VisualElement e, float distance, float duration)
        {
            yield return Tween.Run(duration, t =>
            {
                e.style.opacity = t;
                e.style.translate = new Translate(0f, distance * (1f - t));
            }, Ease.OutCubic, true);
        }

        /// <summary>Gold sparkles that burst up from <paramref name="around"/> and fall away, drawn in the end panel's fx layer.</summary>
        IEnumerator Sparkles(VisualElement around, int count)
        {
            var origin = around.worldBound.center - endFx.worldBound.position;
            var parts = new List<(VisualElement e, Vector2 velocity, float size, float spin)>();
            for (int i = 0; i < count; i++)
            {
                var e = Box("fx-sparkle");
                float size = UnityEngine.Random.Range(18f, 42f);
                e.style.width = size;
                e.style.height = size;
                endFx.Add(e);
                parts.Add((e, new Vector2(UnityEngine.Random.Range(-280f, 280f), UnityEngine.Random.Range(-460f, -160f)), size, UnityEngine.Random.Range(-360f, 360f)));
            }
            yield return Tween.Run(0.9f, t =>
            {
                foreach (var (e, velocity, size, spin) in parts)
                {
                    var p = origin + velocity * t + new Vector2(0f, 520f * t * t);
                    e.style.left = p.x - size * 0.5f;
                    e.style.top = p.y - size * 0.5f;
                    e.style.opacity = 1f - t * t;
                    e.style.rotate = new Rotate(spin * t);
                }
            }, Ease.Linear, true);
            foreach (var part in parts) part.e.RemoveFromHierarchy();
        }

        // ---------- Screens ----------

        public void ShowTitle(bool on)
        {
            titleScreen.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            // A run quit from the pause menu can leave a banner mid-animation.
            if (bannerRoutine != null) StopCoroutine(bannerRoutine);
            banner.style.opacity = 0f;
            RefreshTitleStats();
            RefreshShopBadges();
            Restart(ref titleRoutine, TitleIntro());
        }

        /// <summary>Logo settles in, then tagline, buttons and stats rise into place one after another.</summary>
        IEnumerator TitleIntro()
        {
            var parts = new[] { titleTagline, titleButtons, titleStats };
            foreach (var p in parts) p.style.opacity = 0f;
            StartCoroutine(Tween.Run(0.7f, t =>
            {
                float s = Mathf.LerpUnclamped(1.25f, 1f, Ease.OutBack(t));
                titleLogo.style.scale = new Scale(new Vector3(s, s, 1f));
                titleLogo.style.opacity = Mathf.Clamp01(t * 3f);
            }, Ease.Linear, true));
            yield return new WaitForSecondsRealtime(0.25f);
            foreach (var p in parts)
            {
                StartCoroutine(Rise(p, 40f, 0.45f));
                yield return new WaitForSecondsRealtime(0.12f);
            }
        }

        void RefreshTitleStats()
        {
            titleStats.style.display = SaveData.Runs > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            titleGold.text = Num(SaveData.Gold);
            titleDepth.text = $"Best depth {Mathf.Max(1, SaveData.BestDepth)}";
            titleWinsStat.style.display = SaveData.Wins > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            titleWins.text = $"{SaveData.Wins} curse{(SaveData.Wins == 1 ? "" : "s")} broken";
            titleReset.style.display = SaveData.Runs > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Confirm dialog in front of the title before progress is wiped.</summary>
        void ShowReset(bool on)
        {
            Click();
            resetScreen.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            StartCoroutine(Tween.Run(0.25f, t =>
            {
                resetScreen.style.opacity = t;
                float s = Mathf.LerpUnclamped(0.85f, 1f, Ease.OutBack(t));
                resetPanel.style.scale = new Scale(new Vector3(s, s, 1f));
            }, Ease.Linear, true));
        }

        /// <summary>The SHOP buttons show how many upgrades the banked gold can buy right now.</summary>
        void RefreshShopBadges()
        {
            int count = Balance.Shop.FindAll(i => i.Affordable).Count;
            foreach (var badge in new[] { titleShopBadge, endShopBadge })
            {
                badge.text = count.ToString();
                badge.EnableInClassList("on", count > 0);
            }
        }

        /// <summary>Keyboard confirm (Enter/Space) presses the current screen's primary button.</summary>
        public void Confirm()
        {
            if (TitleOpen) Press(titlePlay, PlayPressed);
            else if (EndOpen && endReady) Press(endRetry, RetryPressed);
        }

        /// <summary>Keyboard back (Esc) cancels the reset dialog or closes the shop.</summary>
        public void Back()
        {
            if (ResetOpen) { ShowReset(false); return; }
            if (!ShopOpen) return;
            Click();
            ShowShop(false);
        }

        void Press(Button button, Action action)
        {
            Click();
            StartCoroutine(PopScale(button, 0.9f, 0.25f));
            action?.Invoke();
        }

        // ---------- Shop ----------

        SkillTree tree;
        (string label, string value)[] shownStats;
        int shownShopGold;

        public void ShowShop(bool on)
        {
            bool wasOpen = shopScreen.style.display == DisplayStyle.Flex;
            shopScreen.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on)
            {
                tree.Hide();
                if (wasOpen) AudioManager.I?.Play("whoosh_ui", 0.6f, 0.85f);
                RefreshTitleStats();
                RefreshShopBadges();
                return;
            }
            RefreshShopHeader();
            ShowDetail(null);
            tree.Show();
            StartCoroutine(Tween.Run(0.3f, t => shopScreen.style.opacity = t, Ease.OutQuad, true));
        }

        static VisualElement Box(string className)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(className);
            return e;
        }

        static Label CardLabel(string className, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }

        /// <summary>The card in the corner describing the hovered node (null: the wheel hub).</summary>
        void ShowDetail(ShopItem item)
        {
            bool fog = item != null && item.Locked && !item.Owned;
            shopDetail.EnableInClassList("fog", fog);
            shopDetail.EnableInClassList("gate", item != null && item.Gate);
            var icon = item == null ? "wheel_hub" : item.Gate ? "portrait_" + item.GateBoss.Id : item.Icon;
            shopDetailIcon.style.backgroundImage = new StyleBackground(config.Sprite(icon));
            if (item == null)
            {
                shopDetailName.text = "THE WHEEL OF FATE";
                shopDetailLevel.text = "";
                shopDetailDesc.text = "Every path grows from here. Buy a node to light its path and open the ones beyond it. Seals break as you reach deeper depths.";
                shopDetailCost.text = "";
                shopDetailCoin.style.display = DisplayStyle.None;
                return;
            }
            if (fog)
            {
                shopDetailName.text = "???";
                shopDetailLevel.text = "UNDISCOVERED";
                shopDetailDesc.text = "Light the path leading here to find out what it is.";
                shopDetailCost.text = "";
                shopDetailCoin.style.display = DisplayStyle.None;
                return;
            }
            shopDetailName.text = item.Name.ToUpperInvariant();
            shopDetailDesc.text = item.Desc.Replace("{value}", item.Value.ToString()).Replace("{base}", Balance.HeroHp.ToString())
                .Replace("{boss}", item.Gate ? item.GateBoss.Name : "");
            shopDetailLevel.text = item.Gate ? (item.Owned ? "SEAL BROKEN" : "SEALED")
                : item.Consumable ? $"STOCK {item.Level} / {item.Max} · REFILLS EVERY RUN"
                : $"LEVEL {Mathf.Min(item.Level, item.Max)} / {item.Max}";
            var missing = Array.ConvertAll(Array.FindAll(item.Requires, r => !Balance.Item(r).Owned), Balance.Item);
            bool priced = !item.Gate && !item.Maxed && missing.Length == 0;
            shopDetailCoin.style.display = priced ? DisplayStyle.Flex : DisplayStyle.None;
            shopDetailCost.EnableInClassList("poor", priced && item.Price > SaveData.Gold);
            shopDetailCost.text = priced ? Num(item.Price)
                : missing.Length > 0 ? "Needs " + string.Join(" + ", Array.ConvertAll(missing, r => r.Name))
                : item.Gate ? (item.Owned ? "Path open" : $"Reach Depth {item.Value}")
                : item.Consumable ? "Stock full" : "Fully upgraded";
        }

        void RefreshShopHeader()
        {
            shownShopGold = SaveData.Gold;
            shopGold.text = Num(shownShopGold);
            var stats = Stats.Current().Summary();
            if (shopStats.childCount != stats.Length)
            {
                shopStats.Clear();
                foreach (var (label, _) in stats)
                {
                    var chip = Box("stat-chip");
                    chip.Add(CardLabel("chip-value", ""));
                    chip.Add(CardLabel("chip-name", label));
                    shopStats.Add(chip);
                }
            }
            for (int i = 0; i < stats.Length; i++)
            {
                var chip = shopStats[i];
                chip.Q<Label>(className: "chip-value").text = stats[i].value;
                if (shownStats == null || shownStats[i].value == stats[i].value) continue;
                StartCoroutine(PopScale(chip, 1.45f, 0.7f));
                StartCoroutine(Glow(chip, "fresh", 0.9f));
            }
            shownStats = stats;
        }

        void OnBought(ShopItem item)
        {
            int goldBefore = shownShopGold;
            RefreshShopHeader();
            ShowDetail(item);
            Flash(new Color(1f, 0.85f, 0.45f), 0.12f, 0.2f);
            StartCoroutine(PopScale(shopDetail, 1.06f, 0.4f));
            var gold = Q<VisualElement>("shop-gold");
            StartCoroutine(PopScale(gold, 1.25f, 0.45f));
            StartCoroutine(Shake(gold, 8f, 0.25f));
            int after = SaveData.Gold;
            StartCoroutine(Tween.Run(0.5f, t => shopGold.text = Num(Mathf.RoundToInt(Mathf.Lerp(goldBefore, after, t))), Ease.OutCubic, true));
        }

        IEnumerator Glow(VisualElement e, string className, float duration)
        {
            e.AddToClassList(className);
            yield return new WaitForSecondsRealtime(duration);
            e.RemoveFromClassList(className);
        }

        /// <param name="ribbon">Record callout under the subtitle (e.g. NEW BEST DEPTH!), or null.</param>
        /// <param name="stats">Tiles in order; gold tiles read "+N" and burst into sparkles.</param>
        public void ShowEnd(bool victory, string title, string sub, string note, string ribbon, IEnumerable<(string name, int value, string icon, bool gold)> stats)
        {
            endScreen.style.display = DisplayStyle.Flex;
            endReady = false;
            endTitle.text = title;
            endTitle.style.color = victory ? new Color(1f, 0.84f, 0.36f) : new Color(1f, 0.42f, 0.45f);
            endSub.text = sub;
            endNote.text = note;
            endRibbon.text = ribbon ?? "";
            endRibbon.EnableInClassList("on", !string.IsNullOrEmpty(ribbon));
            endStats.Clear();
            var tiles = new List<(VisualElement tile, Label label, int value, bool gold)>();
            foreach (var (name, value, icon, gold) in stats)
            {
                var tile = Box("stat-tile");
                tile.EnableInClassList("gold", gold);
                var iconBox = Box("stat-tile-icon");
                iconBox.style.backgroundImage = new StyleBackground(config.Sprite(icon));
                tile.Add(iconBox);
                var label = CardLabel("stat-tile-value", gold ? "+0" : "0");
                tile.Add(label);
                tile.Add(CardLabel("stat-tile-name", name));
                endStats.Add(tile);
                tiles.Add((tile, label, value, gold));
            }
            RefreshShopBadges();
            Restart(ref endRoutine, EndReveal(victory, tiles));
        }

        /// <summary>Panel pops, the title slams down, then the tiles pop in one by one and count up before the buttons rise.</summary>
        IEnumerator EndReveal(bool victory, List<(VisualElement tile, Label label, int value, bool gold)> tiles)
        {
            foreach (var e in new[] { endTitle, endSub, endRibbon, endNote, endButtons }) e.style.opacity = 0f;
            foreach (var entry in tiles) entry.tile.style.opacity = 0f;

            StartCoroutine(Tween.Run(0.3f, t => endScreen.style.opacity = t, Ease.OutQuad, true));
            yield return Tween.Run(0.35f, t =>
            {
                float s = Mathf.LerpUnclamped(0.7f, 1f, Ease.OutBack(t));
                endPanel.style.scale = new Scale(new Vector3(s, s, 1f));
                endPanel.style.opacity = t;
            }, Ease.Linear, true);

            yield return Tween.Run(0.22f, t =>
            {
                float s = Mathf.Lerp(2.2f, 1f, t);
                endTitle.style.scale = new Scale(new Vector3(s, s, 1f));
                endTitle.style.opacity = t;
            }, Ease.InQuad, true);
            AudioManager.I?.Play(victory ? "reveal_big" : "hit_heavy", 0.6f, victory ? 1f : 0.8f);
            StartCoroutine(Shake(endPanel, 12f, 0.3f));
            if (victory) StartCoroutine(Sparkles(endTitle, 28));

            StartCoroutine(Rise(endSub, 16f, 0.3f));
            yield return new WaitForSecondsRealtime(0.12f);
            if (endRibbon.ClassListContains("on"))
            {
                endRibbon.style.opacity = 1f;
                StartCoroutine(PopScale(endRibbon, 1.7f, 0.6f));
                AudioManager.I?.Play("reveal_small", 0.6f, 1.2f);
                yield return new WaitForSecondsRealtime(0.15f);
            }

            foreach (var (tile, label, value, gold) in tiles)
            {
                tile.style.opacity = 1f;
                StartCoroutine(PopScale(tile, 1.3f, 0.45f));
                StartCoroutine(CountUp(label, value, gold, tile));
                AudioManager.I?.Play("ui_click", 0.5f, 1.25f);
                yield return new WaitForSecondsRealtime(0.1f);
            }

            yield return new WaitForSecondsRealtime(0.25f);
            StartCoroutine(Rise(endNote, 16f, 0.3f));
            yield return new WaitForSecondsRealtime(0.1f);
            yield return Rise(endButtons, 30f, 0.3f);
            endReady = true;
        }

        IEnumerator CountUp(Label label, int value, bool gold, VisualElement tile)
        {
            string prefix = gold ? "+" : "";
            int shown = 0;
            yield return Tween.Run(0.5f, t =>
            {
                int v = Mathf.RoundToInt(value * t);
                if (v == shown) return;
                shown = v;
                label.text = prefix + Num(v);
                if (gold) AudioManager.I?.Play("coin_tick", 0.3f, 1f + 0.5f * t, 0.03f, 0.03f);
            }, Ease.OutCubic, true);
            label.text = prefix + Num(value);
            if (!gold || value <= 0) yield break;
            AudioManager.I?.Play("coin_burst", 0.5f);
            StartCoroutine(Sparkles(tile, 16));
            StartCoroutine(PopScale(label, 1.4f, 0.5f));
        }

        public void HideEnd()
        {
            if (endRoutine != null) StopCoroutine(endRoutine);
            endScreen.style.display = DisplayStyle.None;
            endFx.Clear();
        }

        public void ShowSettings(bool on)
        {
            Click();
            settingsScreen.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            Juice.I.Paused = on;
            if (!on) return;
            ArmQuit(false);
            RefreshSettings();
            StartCoroutine(Tween.Run(0.25f, t =>
            {
                settingsScreen.style.opacity = t;
                float s = Mathf.LerpUnclamped(0.85f, 1f, Ease.OutBack(t));
                settingsPanel.style.scale = new Scale(new Vector3(s, s, 1f));
            }, Ease.Linear, true));
        }

        /// <summary>Quitting a run takes two clicks; the first only asks for confirmation.</summary>
        void ArmQuit(bool armed)
        {
            quitArmed = armed;
            settingsQuit.text = armed ? "Click again to quit (gold is kept)" : "Quit to menu";
            settingsQuit.EnableInClassList("confirm", armed);
        }

        void BuildVolumePips(VisualElement row, bool music)
        {
            for (int i = 0; i < 10; i++)
            {
                var pip = Box("volume-pip");
                pip.pickingMode = PickingMode.Position;
                pip.style.height = 14f + i * 2.8f;
                float volume = (i + 1) / 10f;
                pip.RegisterCallback<PointerDownEvent>(_ => SetVolume(music, volume));
                row.Add(pip);
            }
        }

        void SetVolume(bool music, float volume)
        {
            volume = Mathf.Clamp01(Mathf.Round(volume * 10f) / 10f);
            if (music) SaveData.MusicVolume = volume;
            else SaveData.SfxVolume = volume;
            RefreshSettings();
            Click();
        }

        void RefreshSettings()
        {
            for (int i = 0; i < 10; i++)
            {
                musicPips[i].EnableInClassList("on", i < Mathf.RoundToInt(SaveData.MusicVolume * 10f));
                sfxPips[i].EnableInClassList("on", i < Mathf.RoundToInt(SaveData.SfxVolume * 10f));
            }
            shakeToggle.EnableInClassList("on", SaveData.ScreenShake);
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

            // Loading: the rune ring winds like the wheel, the hub breathes and the logo floats.
            if (Loading)
            {
                float t = Time.unscaledTime;
                loadingRing.style.rotate = new Rotate(t * 140f);
                float hub = 1f + Mathf.Sin(t * 4f) * 0.06f;
                loadingHub.style.scale = new Scale(new Vector3(hub, hub, 1f));
                loadingLogo.style.translate = new Translate(0f, Mathf.Sin(t * 1.6f) * 8f);
            }

            // Title: floating logo and a warm glow breathing behind PLAY (the button itself keeps its hover scale).
            if (titleScreen.style.display == DisplayStyle.Flex)
            {
                float t = Time.unscaledTime;
                titleLogo.style.translate = new Translate(0f, Mathf.Sin(t * 1.6f) * 10f);
                float g = 0.5f + 0.5f * Mathf.Sin(t * 3.2f);
                titlePlayGlow.style.opacity = 0.2f + 0.5f * g;
                titlePlayGlow.style.scale = new Scale(Vector3.one * (0.95f + 0.1f * g));
            }

            // Menus: a light sweep crosses each primary button every few seconds, and SHOP badges throb.
            if (TitleOpen || EndOpen)
            {
                float cycle = Time.unscaledTime % 3.2f;
                foreach (var shine in shines)
                {
                    float width = shine.parent.resolvedStyle.width;
                    shine.style.translate = new Translate(Mathf.Lerp(-120f, width + 60f, Ease.InOutQuad(Mathf.Clamp01(cycle / 0.8f))), 0f);
                }
                float b = 1f + 0.1f * Mathf.Max(0f, Mathf.Sin(Time.unscaledTime * 5f));
                titleShopBadge.style.scale = new Scale(new Vector3(b, b, 1f));
                endShopBadge.style.scale = new Scale(new Vector3(b, b, 1f));
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

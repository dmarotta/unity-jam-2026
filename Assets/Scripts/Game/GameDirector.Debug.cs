#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lucky
{
    /// <summary>
    /// Editor-only test panel. F1 toggles it, F2 kills the boss, F3 plays the victory finale. Not compiled into builds.
    /// Kills and defeats land at the start of the player's next turn so every sequence plays out the normal way.
    /// </summary>
    public partial class GameDirector
    {
        bool debugOpen, immortal, forceKill, forceDefeat;
        int forcedValue = -1;
        Rect debugRect;

        /// <summary>QA hook: force the next spin's value (0 = the exact kill).</summary>
        public void ForceNextValue(int value) => forcedValue = value;

        partial void DebugUpdate()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) debugOpen = !debugOpen;
                if (kb.f2Key.wasPressedThisFrame && inRun) forceKill = true;
                if (kb.f3Key.wasPressedThisFrame) StartFrom(PlayFinale());
            }
            // A queued kill doesn't wait on Madame Fortuna's offer.
            if (forceKill && hud.PickingCard) hud.ChooseCard(0);
        }

        /// <summary>Runs while the player's turn is open. Lands a queued kill (by requesting a rigged spin) or a defeat; true when the hero fell.</summary>
        bool DebugTurn()
        {
            if (forceDefeat)
            {
                forceDefeat = false;
                heroHp = 0;
                hud.SetHeroHp(0, heroMax, true);
                return true;
            }
            if (!forceKill) return false;
            // The forced spin can only land the boss's exact HP if that number is on the wheel.
            if (System.Array.IndexOf(wheelValues, bossHp) < 0)
            {
                bossHp = wheelValues.Where(v => v <= bossHp).DefaultIfEmpty(wheelValues.Min()).Max();
                hud.SetBossHp(bossHp, Boss.Hp, true);
                RefreshTarget();
            }
            phylactery = false;
            forcedValue = 0;
            spinRequested = true;
            return false;
        }

        /// <summary>Abandons whatever is running, like quitting a run, and starts <paramref name="routine"/> in its place.</summary>
        void StartFrom(IEnumerator routine)
        {
            if (hud.Loading) return;
            StopAllCoroutines();
            if (hud.SettingsOpen) hud.ShowSettings(false);
            if (awaitingSpin) SetAwaitingSpin(false);
            forceKill = forceDefeat = false;
            forcedValue = -1;
            runRoutine = StartCoroutine(routine);
        }

        /// <summary>Straight to the win: the last arena with its boss already gone, then the real finale. Leaves the save as it found it.</summary>
        IEnumerator PlayFinale()
        {
            hud.ShowTitle(false);
            hud.ShowShop(false);
            hud.HideEnd();
            yield return hud.Fade(1f, 0.25f);
            ResetStage();
            LoadLoadout();
            depth = bosses.Count - 1;
            heroHp = heroMax;
            runGold = 1250;
            spins = 41;
            jackpots = bosses.Count + 1;
            busts = 5;
            CameraRig.I.SnapTo(World.ArenaX(depth));
            hero.Home = new Vector3(World.ArenaX(depth) + World.HeroX, World.GroundY);
            hero.transform.position = hero.Home;
            BossFighter.gameObject.SetActive(false);
            AudioManager.I.PlayMusic("music_final", 0.3f);
            yield return hud.Fade(0f, 0.35f);
            yield return new WaitForSeconds(0.4f);

            // Victory counts a win and banks the gold; put the save back as soon as the scoreboard has read it.
            int wins = SaveData.Wins, gold = SaveData.Gold, best = SaveData.BestDepth;
            bool restored = false;
            var finale = Victory();
            while (finale.MoveNext())
            {
                if (!restored && SaveData.Gold != gold)
                {
                    restored = true;
                    SaveData.Wins = wins;
                    SaveData.Gold = gold;
                    SaveData.BestDepth = best;
                }
                yield return finale.Current;
            }
        }

        void OnGUI()
        {
            if (!debugOpen) return;
            float scale = Mathf.Max(1f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            if (debugRect.width == 0f) debugRect = new Rect(Screen.width / scale - 330f, 170f, 310f, 0f);
            debugRect = GUILayout.Window(7741, debugRect, DebugWindow, "Lucky debug  ·  F1");
        }

        void DebugWindow(int id)
        {
            GUILayout.Label(inRun
                ? $"Depth {depth + 1}/{bosses.Count}  ·  {Boss.Name}\nBoss {bossHp}/{Boss.Hp}    Hero {heroHp}/{heroMax}"
                : "No fight in progress");

            GUI.enabled = inRun;
            if (GUILayout.Button(forceKill ? "Kill boss: on your turn…" : "Kill boss  (F2)")) forceKill = true;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Next spin: exact")) ForceNextValue(0);
            if (GUILayout.Button("Next spin: bust"))
            {
                int over = wheelValues.Where(v => v > bossHp).DefaultIfEmpty(-1).Min();
                if (over > 0) ForceNextValue(over);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Full heal") && heroHp < heroMax) HealHero(heroMax - heroHp, HealColor);
            if (GUILayout.Button(forceDefeat ? "Losing on your turn…" : "Lose run")) forceDefeat = true;
            GUILayout.EndHorizontal();

            GUI.enabled = !hud.Loading;
            GUILayout.Space(6f);
            GUILayout.Label("Jump to depth (fresh run from there)");
            for (int i = 0; i < bosses.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, bosses.Count); j++)
                {
                    string boss = Balance.Bosses[j].Id;
                    if (GUILayout.Button($"{j + 1}  {char.ToUpperInvariant(boss[0])}{boss.Substring(1)}")) StartFrom(Run(j));
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Play victory finale  (F3)")) StartFrom(PlayFinale());

            GUI.enabled = true;
            GUILayout.Space(6f);
            immortal = GUILayout.Toggle(immortal, " Immortal hero");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Speed");
            foreach (float speed in new[] { 1f, 2f, 4f })
                if (GUILayout.Toggle(Juice.I.DebugSpeed == speed, $"×{speed}", GUI.skin.button)) Juice.I.DebugSpeed = speed;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("+1,000 banked gold")) SaveData.Gold += 1000;
            if (GUILayout.Button("Max every upgrade (from next run)"))
            {
                // Carry raises consumable stock caps, so it has to be maxed before the consumables are.
                foreach (var item in Balance.Shop.Where(i => !i.Gate).OrderBy(i => i.Consumable)) SaveData.SetLevel(item.Id, item.Max);
                SaveData.BestDepth = Mathf.Max(SaveData.BestDepth, bosses.Count);
            }
            GUI.DragWindow();
        }
    }
}
#endif

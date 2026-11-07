using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lucky
{
    /// <summary>
    /// The shop as an incremental path tree: nodes on a grid around the wheel hub, joined by paths. Owning a node
    /// lights the paths into it and opens its neighbours; seals break when the player reaches deeper depths.
    /// Drag to pan, scroll to zoom. All animation runs on unscaled time.
    /// </summary>
    public class SkillTree
    {
        const float Cell = 140f, NodeSize = 84f, GateSize = 108f, HubSize = 150f, Margin = 240f;
        const float MinZoom = 0.45f, MaxZoom = 1.6f;
        static readonly Color Gold = new(1f, 0.8f, 0.32f);
        static readonly Color Fresh = new(0.6f, 1f, 0.45f);
        static readonly Color Deny = new(1f, 0.32f, 0.4f);
        static readonly Color OpenPath = new(0.95f, 0.7f, 0.35f, 0.7f);
        static readonly Color DarkPath = new(0.42f, 0.36f, 0.4f, 0.55f);

        /// <summary>Hover target: a node's item, or null for the wheel hub.</summary>
        public event Action<ShopItem> Hovered;
        public event Action<ShopItem> Bought;

        class Node
        {
            public ShopItem Item;
            public VisualElement Root, Glow;
            public Label Badge;
            public Vector2 Pos;
            public int Depth;
        }

        /// <summary>A path from a required node (null: the hub) into a node. Fill grows a freshly lit or opened path.</summary>
        class Edge
        {
            public Node From, To;
            public float Fill = 1f;
        }

        readonly MonoBehaviour host;
        readonly VisualElement viewport, shaker, content, edgeLayer, fxLayer, hubRim;
        readonly List<Node> nodes = new();
        readonly List<Edge> edges = new();
        readonly IVisualElementScheduledItem ticker;
        readonly Rect gridBounds;
        readonly Vector2 origin;
        readonly int maxDepth;
        readonly Sprite sparkle, coin, ring;
        Vector2 offset, pressStart, panStart;
        float zoom = 1f, reveal = 1f, time;
        bool pressing, dragged;

        static readonly Dictionary<string, string> StatLabels = new()
        {
            { "max_hp", " MAX HP" }, { "regen", "% REGEN" }, { "armor", " ARMOR" }, { "pity", "% LUCK/MISS" },
            { "gold", "% GOLD" }, { "boss_gold", "% BOUNTY" }, { "carry", " MAX STOCK" }, { "potion_heal", " POTION HEAL" },
            { "clover_bonus", " CLOVER SLICES" }, { "dodge", "% DODGE" },
        };

        public SkillTree(VisualElement viewport, GameConfig config, MonoBehaviour host)
        {
            this.viewport = viewport;
            this.host = host;
            sparkle = config.Sprite("vfx_sparkle");
            coin = config.Sprite("vfx_coin");
            ring = config.Sprite("vfx_ring");

            var items = Balance.Shop;
            gridBounds = Rect.MinMaxRect(Mathf.Min(0, items.Min(i => i.X)), Mathf.Min(0, items.Min(i => i.Y)),
                Mathf.Max(0, items.Max(i => i.X)), Mathf.Max(0, items.Max(i => i.Y)));
            origin = new Vector2(-gridBounds.xMin * Cell + Margin, -gridBounds.yMin * Cell + Margin);

            shaker = Box("tree-shaker");
            content = Box("tree-content");
            content.style.width = gridBounds.width * Cell + Margin * 2f;
            content.style.height = gridBounds.height * Cell + Margin * 2f;
            edgeLayer = Box("tree-edges");
            edgeLayer.generateVisualContent += DrawEdges;
            fxLayer = Box("tree-fx");
            viewport.Add(shaker);
            shaker.Add(content);
            content.Add(edgeLayer);

            // The wheel hub everything grows from: a slowly turning jewelled rim around the ruby boss.
            var hub = new VisualElement();
            hub.AddToClassList("tree-hub");
            Place(hub, origin, HubSize);
            hub.Add(Box("hub-glow"));
            hubRim = Box("hub-rim");
            hubRim.style.backgroundImage = new StyleBackground(config.Sprite("wheel_rim"));
            hub.Add(hubRim);
            var core = Box("hub-core");
            core.style.backgroundImage = new StyleBackground(config.Sprite("wheel_hub"));
            hub.Add(core);
            hub.RegisterCallback<PointerEnterEvent>(_ => Hovered?.Invoke(null));
            content.Add(hub);

            foreach (var item in items)
            {
                var n = new Node { Item = item, Pos = origin + new Vector2(item.X, item.Y) * Cell, Root = new VisualElement() };
                n.Root.AddToClassList("tree-node");
                n.Root.EnableInClassList("gate", item.Gate);
                n.Root.EnableInClassList("consumable", item.Consumable);
                Place(n.Root, n.Pos, item.Gate ? GateSize : NodeSize);
                n.Glow = Box("node-glow");
                n.Root.Add(n.Glow);
                n.Root.Add(Box("node-back"));
                var icon = Box("node-icon");
                var sprite = config.Sprite(item.Gate ? "portrait_" + item.GateBoss.Id : item.Icon);
                if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
                n.Root.Add(icon);
                var frame = Box(item.Gate ? "node-frame" : "node-socket");
                n.Root.Add(frame);
                n.Root.Add(Box("node-lock"));
                n.Badge = new Label { pickingMode = PickingMode.Ignore };
                n.Badge.AddToClassList("node-badge");
                n.Root.Add(n.Badge);
                n.Root.RegisterCallback<PointerEnterEvent>(_ => Hover(n, true));
                n.Root.RegisterCallback<PointerLeaveEvent>(_ => Hover(n, false));
                n.Root.RegisterCallback<ClickEvent>(_ => Click(n));
                content.Add(n.Root);
                nodes.Add(n);
            }

            foreach (var n in nodes)
            {
                n.Depth = DepthOf(n.Item);
                if (n.Item.Requires.Length == 0) edges.Add(new Edge { To = n });
                foreach (var r in n.Item.Requires) edges.Add(new Edge { From = nodes.Find(m => m.Item.Id == r), To = n });
            }
            maxDepth = nodes.Max(n => n.Depth);
            content.Add(fxLayer);

            viewport.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            viewport.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            viewport.RegisterCallback<WheelEvent>(OnWheel);
            ticker = viewport.schedule.Execute(Tick).Every(16);
            ticker.Pause();
        }

        static int DepthOf(ShopItem item) => item.Requires.Length == 0 ? 1 : 1 + item.Requires.Max(r => DepthOf(Balance.Item(r)));

        static VisualElement Box(string className)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(className);
            return e;
        }

        static void Place(VisualElement e, Vector2 center, float size)
        {
            e.style.left = center.x - size * 0.5f;
            e.style.top = center.y - size * 0.5f;
            e.style.width = size;
            e.style.height = size;
        }

        // ---------- State ----------

        static bool Revealed(ShopItem item) => item.Owned || !item.Locked;

        /// <summary>Revealed nodes, plus "???" silhouettes one step past them.</summary>
        static bool Visible(ShopItem item) => Revealed(item) || Array.Exists(item.Requires, r => Revealed(Balance.Item(r)));

        static bool Visible(Node n) => n == null || Visible(n.Item);

        public void Show()
        {
            Refresh();
            ticker.Resume();
            viewport.schedule.Execute(() => host.StartCoroutine(Intro()));
        }

        public void Hide() => ticker.Pause();

        public void Refresh()
        {
            foreach (var n in nodes)
            {
                var item = n.Item;
                bool revealed = Revealed(item), visible = Visible(item);
                bool buyable = revealed && !item.Gate && !item.Maxed;
                n.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                n.Root.EnableInClassList("fog", visible && !revealed);
                n.Root.EnableInClassList("owned", item.Owned);
                n.Root.EnableInClassList("maxed", item.Maxed);
                n.Root.EnableInClassList("sealed", item.Gate && revealed && !item.Owned);
                n.Root.EnableInClassList("affordable", buyable && item.Price <= SaveData.Gold);
                n.Root.EnableInClassList("poor", buyable && item.Price > SaveData.Gold);
                if (!n.Root.ClassListContains("affordable")) n.Glow.style.opacity = StyleKeyword.Null;
                n.Badge.text = item.Gate ? $"DEPTH {item.Value}" : !revealed ? "?" : $"{Mathf.Min(item.Level, item.Max)}/{item.Max}";
            }
            edgeLayer.MarkDirtyRepaint();
        }

        // ---------- Drawing ----------

        void DrawEdges(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineCap = LineCap.Round;

            // Faint dotted grid, like chips on felt.
            p.fillColor = new Color(1f, 0.85f, 0.55f, 0.07f);
            for (int x = (int)gridBounds.xMin - 1; x <= gridBounds.xMax + 1; x++)
            for (int y = (int)gridBounds.yMin - 1; y <= gridBounds.yMax + 1; y++)
            {
                p.BeginPath();
                p.Arc(origin + new Vector2(x, y) * Cell, 3f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Fill();
            }

            float front = reveal * (maxDepth + 1);
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if (!Visible(e.To) || !Visible(e.From)) continue;
                float reach = Mathf.Clamp01(front - (e.To.Depth - 1)) * e.Fill;
                if (reach <= 0f) continue;
                var a = e.From?.Pos ?? origin;
                var b = Vector2.Lerp(a, e.To.Pos, reach);
                bool fromOwned = e.From == null || e.From.Item.Owned;
                if (fromOwned && e.To.Item.Owned) DrawLit(p, a, b, e.To.Pos, i);
                else if (fromOwned) DrawMarching(p, a, b);
                else Stroke(p, a, b, DarkPath, 5f);
            }
        }

        static void Stroke(Painter2D p, Vector2 a, Vector2 b, Color color, float width)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        static void Dot(Painter2D p, Vector2 at, float radius, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.Arc(at, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }

        /// <summary>An owned path: wide glow, gold core, hot centre, and sparks streaming outward along it.</summary>
        void DrawLit(Painter2D p, Vector2 a, Vector2 b, Vector2 end, int seed)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 3f + seed);
            Stroke(p, a, b, new Color(Gold.r, Gold.g, Gold.b, 0.12f + 0.08f * pulse), 26f);
            Stroke(p, a, b, Gold, 9f);
            Stroke(p, a, b, new Color(1f, 0.97f, 0.85f), 3f);
            float length = Vector2.Distance(a, end), drawn = Vector2.Distance(a, b);
            int sparks = Mathf.Max(1, Mathf.RoundToInt(length / 70f));
            for (int k = 0; k < sparks; k++)
            {
                float t = Mathf.Repeat(time * 0.45f + (k + seed * 0.37f) / sparks, 1f);
                if (t * length > drawn) continue;
                var at = Vector2.Lerp(a, end, t);
                Dot(p, at, 10f, new Color(1f, 0.85f, 0.4f, 0.25f));
                Dot(p, at, 4.5f, new Color(1f, 1f, 0.9f, 0.95f));
            }
        }

        /// <summary>An open path to a node you can buy: dashes marching outward from what you own.</summary>
        void DrawMarching(Painter2D p, Vector2 a, Vector2 b)
        {
            const float dash = 14f, gap = 10f;
            float length = Vector2.Distance(a, b);
            if (length < 1f) return;
            var dir = (b - a) / length;
            p.strokeColor = OpenPath;
            p.lineWidth = 6f;
            p.BeginPath();
            for (float s = Mathf.Repeat(time * 36f, dash + gap) - (dash + gap); s < length; s += dash + gap)
            {
                float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(length, s + dash);
                if (s1 <= s0) continue;
                p.MoveTo(a + dir * s0);
                p.LineTo(a + dir * s1);
            }
            p.Stroke();
        }

        void Tick()
        {
            time += Time.unscaledDeltaTime;
            hubRim.style.rotate = new Rotate(time * 10f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 5f);
            foreach (var n in nodes)
                if (n.Root.ClassListContains("affordable")) n.Glow.style.opacity = 0.3f + 0.7f * pulse;
            edgeLayer.MarkDirtyRepaint();
        }

        // ---------- Camera ----------

        void Apply()
        {
            content.style.translate = new Translate(offset.x, offset.y);
            content.style.scale = new Scale(new Vector3(zoom, zoom, 1f));
        }

        Rect NodeBounds()
        {
            var r = Rect.MinMaxRect(origin.x, origin.y, origin.x, origin.y);
            foreach (var n in nodes)
            {
                if (!Visible(n.Item)) continue;
                r.xMin = Mathf.Min(r.xMin, n.Pos.x);
                r.yMin = Mathf.Min(r.yMin, n.Pos.y);
                r.xMax = Mathf.Max(r.xMax, n.Pos.x);
                r.yMax = Mathf.Max(r.yMax, n.Pos.y);
            }
            return Rect.MinMaxRect(r.xMin - 130f, r.yMin - 130f, r.xMax + 130f, r.yMax + 130f);
        }

        /// <summary>Frames what's discovered so far below the header, then grows the tree out of the hub.</summary>
        IEnumerator Intro()
        {
            var view = viewport.layout.size;
            if (float.IsNaN(view.x) || view.x < 10f) view = new Vector2(1920f, 1080f);
            var area = new Rect(0f, 150f, view.x, view.y - 200f);
            var bounds = NodeBounds();
            float target = Mathf.Clamp(Mathf.Min(area.width / bounds.width, area.height / bounds.height), MinZoom, 1.15f);
            AudioManager.I?.Play("whoosh_ui", 0.7f, 0.9f);
            int lastRing = -1;
            yield return Tween.Run(1f, t =>
            {
                zoom = Mathf.LerpUnclamped(target * 0.7f, target, Ease.OutBack(Mathf.Min(1f, t * 1.4f)));
                offset = area.center - bounds.center * zoom;
                reveal = t;
                Apply();
                float front = reveal * (maxDepth + 1);
                foreach (var n in nodes)
                {
                    float k = Mathf.Clamp01((front - n.Depth) * 2.5f);
                    n.Root.style.scale = new Scale(Vector3.one * Ease.OutBack(k));
                }
                int ringNow = Mathf.FloorToInt(front);
                if (ringNow != lastRing && ringNow <= maxDepth)
                {
                    lastRing = ringNow;
                    AudioManager.I?.Play("coin_tick", 0.5f, 0.9f + ringNow * 0.07f);
                }
            }, Ease.Linear, true);
            foreach (var n in nodes) n.Root.style.scale = StyleKeyword.Null;

            // Seals broken since the last visit shatter now, one by one.
            var broken = nodes.Where(n => n.Item.Gate && n.Item.Owned && !SaveData.SealCelebrated(n.Item.Id)).ToList();
            for (int i = 0; i < broken.Count; i++)
            {
                SaveData.MarkSealCelebrated(broken[i].Item.Id);
                yield return BreakSeal(broken[i]);
            }
        }

        IEnumerator BreakSeal(Node n)
        {
            var reached = edges.Where(e => e.From == n || e.To == n).ToList();
            foreach (var e in reached) e.Fill = 0f;
            n.Root.AddToClassList("sealed");
            AudioManager.I?.Play("tension_riser", 0.6f, 1.2f);
            yield return Jitter(n.Root, 9f, 0.6f);

            n.Root.RemoveFromClassList("sealed");
            AudioManager.I?.Play("jackpot", 0.8f);
            AudioManager.I?.Play("potion_explode", 0.6f, 1.2f);
            host.StartCoroutine(Pop(n.Root, 1.6f, 0.7f));
            host.StartCoroutine(Flag(n.Root, "flash", 0.15f));
            Ring(n.Pos, Color.white, 5f, 0.6f);
            Ring(n.Pos, Gold, 3.5f, 0.5f);
            Burst(n.Pos, sparkle, Gold, 60, 900f, 26f);
            Burst(n.Pos, coin, Color.white, 24, 700f, 26f);
            Float(n.Pos + Vector2.up * -84f, "SEAL BROKEN!", Gold, 46f);
            host.StartCoroutine(Jitter(shaker, 16f, 0.4f));
            foreach (var e in reached) host.StartCoroutine(Grow(e, 0.45f, e.To == n ? 0f : 0.35f));
            yield return new WaitForSecondsRealtime(0.8f);
        }

        void OnPointerDown(PointerDownEvent e)
        {
            pressing = true;
            dragged = false;
            pressStart = e.position;
            panStart = offset;
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (!pressing) return;
            var delta = (Vector2)e.position - pressStart;
            if (!dragged && delta.magnitude < 8f) return;
            if (!dragged)
            {
                dragged = true;
                viewport.CapturePointer(e.pointerId);
                viewport.AddToClassList("dragging");
            }
            offset = panStart + delta;
            ClampOffset();
            Apply();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            pressing = false;
            viewport.RemoveFromClassList("dragging");
            if (viewport.HasPointerCapture(e.pointerId)) viewport.ReleasePointer(e.pointerId);
        }

        /// <summary>Zoom around the cursor.</summary>
        void OnWheel(WheelEvent e)
        {
            var mouse = viewport.WorldToLocal(e.mousePosition);
            var world = (mouse - offset) / zoom;
            zoom = Mathf.Clamp(zoom * Mathf.Pow(1.12f, -Mathf.Sign(e.delta.y)), MinZoom, MaxZoom);
            offset = mouse - world * zoom;
            ClampOffset();
            Apply();
            e.StopPropagation();
        }

        /// <summary>Keep some of the tree on screen.</summary>
        void ClampOffset()
        {
            var view = viewport.layout.size;
            var b = NodeBounds();
            offset.x = Mathf.Clamp(offset.x, view.x * 0.25f - b.xMax * zoom, view.x * 0.75f - b.xMin * zoom);
            offset.y = Mathf.Clamp(offset.y, view.y * 0.25f - b.yMax * zoom, view.y * 0.75f - b.yMin * zoom);
        }

        // ---------- Interaction ----------

        void Hover(Node n, bool on)
        {
            n.Root.EnableInClassList("hover", on);
            if (!on) return;
            AudioManager.I?.Play("ui_hover", 0.4f, 1f + n.Depth * 0.04f);
            Hovered?.Invoke(n.Item);
        }

        void Click(Node n)
        {
            if (dragged) return;
            var item = n.Item;
            if (item.Gate)
            {
                Reject(n, item.Owned ? "SEAL BROKEN" : $"DEFEAT {item.GateBoss.Name.ToUpperInvariant()}");
                return;
            }
            if (item.Locked) { Reject(n, "LOCKED"); return; }
            if (item.Maxed) { Reject(n, item.Consumable ? "FULL" : "MAXED"); return; }
            if (item.Price > SaveData.Gold) { Reject(n, "NOT ENOUGH GOLD"); return; }

            var wasVisible = new HashSet<Node>(nodes.Where(m => Visible(m.Item)));
            var wasRevealed = new HashSet<Node>(nodes.Where(m => Revealed(m.Item)));
            bool firstLevel = !item.Owned;
            item.TryBuy();
            Refresh();
            Bought?.Invoke(item);
            host.StartCoroutine(BuyBurst(n, firstLevel, wasVisible, wasRevealed));
        }

        void Reject(Node n, string reason)
        {
            AudioManager.I?.Play("shop_deny", 0.7f);
            host.StartCoroutine(Jitter(n.Root, 10f, 0.3f));
            host.StartCoroutine(Flag(n.Root, "deny", 0.3f));
            Float(n.Pos + Vector2.up * -60f, reason, Deny, 26f);
        }

        IEnumerator BuyBurst(Node n, bool firstLevel, HashSet<Node> wasVisible, HashSet<Node> wasRevealed)
        {
            var item = n.Item;
            var color = item.Consumable ? new Color(1f, 0.6f, 0.25f) : Gold;

            // Freshly reached paths and nodes start hidden and grow in below.
            var opened = nodes.Where(m => Visible(m.Item) && !wasVisible.Contains(m) || Revealed(m.Item) && !wasRevealed.Contains(m)).ToList();
            foreach (var e in edges)
                if (firstLevel && e.To == n || opened.Contains(e.To)) e.Fill = 0f;
            foreach (var m in opened) m.Root.style.scale = new Scale(Vector3.zero);

            AudioManager.I?.Play("shop_buy", 1f, 0.9f + item.Level * 0.07f);
            if (item.Stat == "max_hp") AudioManager.I?.Play("hp_upgrade", 0.7f);
            host.StartCoroutine(Pop(n.Root, 1.4f, 0.55f));
            host.StartCoroutine(Flag(n.Root, "flash", 0.12f));
            Ring(n.Pos, color, 3.2f, 0.45f);
            Burst(n.Pos, sparkle, color, 26, 520f, 20f);
            Burst(n.Pos, coin, Color.white, 10, 420f, 22f);
            Float(n.Pos + Vector2.up * -56f, Effect(item), color, 34f);
            host.StartCoroutine(Jitter(shaker, 7f, 0.22f));

            if (item.Maxed)
            {
                yield return new WaitForSecondsRealtime(0.18f);
                AudioManager.I?.Play("reveal_big", 0.8f, 1.15f);
                Ring(n.Pos, Color.white, 4.5f, 0.55f);
                Burst(n.Pos, sparkle, Gold, 44, 760f, 26f);
                Float(n.Pos + Vector2.up * -100f, item.Consumable ? "FULL!" : "MAXED!", new Color(0.7f, 1f, 0.5f), 44f);
                host.StartCoroutine(Jitter(shaker, 13f, 0.3f));
            }

            // Light the paths into this node, then burst open whatever they reach, one after another.
            if (firstLevel)
                foreach (var e in edges)
                    if (e.To == n) host.StartCoroutine(Grow(e, 0.35f, 0f));
            yield return new WaitForSecondsRealtime(firstLevel ? 0.3f : 0f);
            for (int i = 0; i < opened.Count; i++) host.StartCoroutine(Open(opened[i], i * 0.18f, i));
        }

        IEnumerator Open(Node m, float delay, int index)
        {
            yield return new WaitForSecondsRealtime(delay);
            foreach (var e in edges)
                if (e.To == m) host.StartCoroutine(Grow(e, 0.3f, 0f));
            yield return new WaitForSecondsRealtime(0.26f);
            bool revealed = Revealed(m.Item);
            AudioManager.I?.Play(revealed ? "charm_nudge" : "coin_tick", revealed ? 0.8f : 0.5f, 1.05f + index * 0.08f);
            if (revealed)
            {
                AudioManager.I?.Play("coin_burst", 0.35f, 1.3f);
                Ring(m.Pos, Fresh, 3f, 0.5f);
                Burst(m.Pos, sparkle, Fresh, 22, 460f, 18f);
                Float(m.Pos + Vector2.up * -58f, m.Item.Gate ? "SEAL FOUND" : "NEW!", Fresh, 30f);
                host.StartCoroutine(Flag(m.Root, "unlocked", 1.2f));
            }
            yield return Tween.Run(0.5f, t => m.Root.style.scale = new Scale(Vector3.one * Ease.OutBack(t)), Ease.Linear, true);
            m.Root.style.scale = StyleKeyword.Null;
        }

        IEnumerator Grow(Edge e, float duration, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            yield return Tween.Run(duration, t => e.Fill = t, Ease.OutCubic, true);
        }

        static string Effect(ShopItem item)
        {
            if (item.Consumable) return $"+1 {item.Name.ToUpperInvariant()} / RUN";
            if (item.Stat == "revive") return "PHOENIX!";
            if (item.Stat.StartsWith("charm")) return item.Stat == "charm_above" ? "LUCKY ±1" : "LUCKY +1";
            return StatLabels.TryGetValue(item.Stat, out var label) ? $"+{item.Value}{label}" : "+" + item.Value;
        }

        // ---------- UI particles ----------

        void Ring(Vector2 at, Color color, float scale, float life)
        {
            var e = Box("tree-ring");
            e.style.backgroundImage = new StyleBackground(ring);
            e.style.unityBackgroundImageTintColor = color;
            Place(e, at, 90f);
            fxLayer.Add(e);
            host.StartCoroutine(Fade(e, life, t => e.style.scale = new Scale(Vector3.one * Mathf.Lerp(0.3f, scale, Ease.OutCubic(t)))));
        }

        void Burst(Vector2 at, Sprite sprite, Color color, int count, float speed, float size)
        {
            for (int i = 0; i < count; i++)
            {
                var e = Box("tree-particle");
                e.style.backgroundImage = new StyleBackground(sprite);
                e.style.unityBackgroundImageTintColor = color;
                float s = size * UnityEngine.Random.Range(0.6f, 1.4f);
                Place(e, at, s);
                fxLayer.Add(e);
                var dir = UnityEngine.Random.insideUnitCircle.normalized;
                host.StartCoroutine(Fly(e, at, dir * speed * UnityEngine.Random.Range(0.4f, 1f) + Vector2.up * -220f, s, UnityEngine.Random.Range(0.5f, 0.9f)));
            }
        }

        IEnumerator Fly(VisualElement e, Vector2 pos, Vector2 velocity, float size, float life)
        {
            float spin = UnityEngine.Random.Range(-540f, 540f);
            for (float t = 0f; t < life; t += Time.unscaledDeltaTime)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                velocity.y += 1100f * dt;
                velocity *= 1f - 1.5f * dt;
                pos += velocity * dt;
                e.style.left = pos.x - size * 0.5f;
                e.style.top = pos.y - size * 0.5f;
                e.style.opacity = 1f - t / life;
                e.style.rotate = new Rotate(spin * t);
                yield return null;
            }
            e.RemoveFromHierarchy();
        }

        void Float(Vector2 at, string text, Color color, float fontSize)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("tree-float");
            label.style.color = color;
            label.style.fontSize = fontSize;
            label.style.left = at.x;
            label.style.top = at.y;
            fxLayer.Add(label);
            host.StartCoroutine(Fade(label, 1.1f, t =>
            {
                label.style.top = at.y - 70f * Ease.OutCubic(t);
                label.style.scale = new Scale(Vector3.one * Mathf.LerpUnclamped(1.8f, 1f, Ease.OutBack(Mathf.Min(1f, t * 4f))));
            }));
        }

        IEnumerator Fade(VisualElement e, float life, Action<float> step)
        {
            yield return Tween.Run(life, t =>
            {
                step(t);
                e.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            }, Ease.Linear, true);
            e.RemoveFromHierarchy();
        }

        IEnumerator Pop(VisualElement e, float amount, float duration)
        {
            yield return Tween.Run(duration, t => e.style.scale = new Scale(Vector3.one * Mathf.LerpUnclamped(amount, 1f, Ease.OutElastic(t))), Ease.Linear, true);
            e.style.scale = StyleKeyword.Null;
        }

        IEnumerator Jitter(VisualElement e, float amount, float duration)
        {
            yield return Tween.Run(duration, t =>
            {
                float k = amount * (1f - t);
                e.style.translate = new Translate(UnityEngine.Random.Range(-k, k), UnityEngine.Random.Range(-k, k));
            }, Ease.Linear, true);
            e.style.translate = StyleKeyword.Null;
        }

        IEnumerator Flag(VisualElement e, string className, float duration)
        {
            e.AddToClassList(className);
            yield return new WaitForSecondsRealtime(duration);
            e.RemoveFromClassList(className);
        }
    }
}

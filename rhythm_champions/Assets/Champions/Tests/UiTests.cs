using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Champions.Client.Calibration;
using Champions.Client.Loop;
using Champions.Client.Music;
using Champions.Client.UI;
using Champions.Render;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Key = Champions.Sim.Key;
using Object = UnityEngine.Object;

namespace Champions.Tests
{
    /// <summary>
    /// The UI (design.md, UI; plan.md, The Client > Input) against the real sim at its first offer: a pointer's click on a
    /// card picks it, the keys pick (1-3, or the arrows and Enter), every interactive element takes the pointer, the menus
    /// answer their keys, and every screen keeps to the design's word budget: one or two words a menu row, a card's name
    /// and one brief description, no text at all during play.
    /// </summary>
    public sealed class UiTests
    {
        static string ContentDir => GameContent.Find(Application.dataPath);

        GameContent _content;
        Palette _palette;
        CardFaces _faces;
        GameObject _host;
        Camera _camera;
        readonly List<IDisposable> _disposables = new List<IDisposable>();
        Keyboard _keyboard;

        /// <summary>A key heard just before the sim's time now.</summary>
        sealed class SimClock : IGameClock
        {
            public Simulation Sim;
            public bool Ready => true;
            public FrameTimes Frame(in ClockReadings now) => default;
            public double KeySong(double real) => Sim.Now - 0.02;
            public bool TryDspAt(double song, out double dsp) { dsp = 0; return false; }
            public double EarliestDsp => 0;
        }

        [SetUp]
        public void Make()
        {
            _content = GameContent.Load(ContentDir);
            _palette = Palette.Load(ContentDir);
            _faces = CardFaces.Load(ContentDir, _content);
            _host = new GameObject("UI tests");
            _camera = new GameObject("UI tests camera").AddComponent<Camera>();
        }

        [TearDown]
        public void Clean()
        {
            foreach (var d in _disposables) d.Dispose();
            _disposables.Clear();
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            _keyboard = null;
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_camera.gameObject);
        }

        T Keep<T>(T d) where T : IDisposable
        {
            _disposables.Add(d);
            return d;
        }

        /// <summary>A sim on the content at its first offer, the offer's events taken by <paramref name="ui"/>.</summary>
        Simulation FirstOffer(RunUi ui, out SimEvent offer)
        {
            var sim = new Simulation(new RunInputs { Start = RunStart.FirstOffer }.Build(_content));
            sim.Progression.CollectExperience(sim, sim.State.ExperienceNeeded, sim.Now);
            sim.StepTo(0.5);
            offer = sim.EventsSince(0).ToArray().Single(e => e.Kind == SimEventKind.Offer);
            foreach (var e in sim.EventsSince(0)) ui.Take(e);
            ui.Present(sim.Now, sim, _camera);
            return sim;
        }

        static List<SimEvent> Since(Simulation sim, ref int cursor, RunUi ui)
        {
            var list = sim.EventsSince(cursor).ToArray().ToList();
            foreach (var e in list) ui.Take(e);
            cursor = sim.EventCount;
            return list;
        }

        static void Send(IPanel panel, EventType type, Vector2 at)
        {
            var imgui = new Event { type = type, mousePosition = at, button = 0, clickCount = 1 };
            using var e = type == EventType.MouseDown ? (EventBase)PointerDownEvent.GetPooled(imgui) : PointerUpEvent.GetPooled(imgui);
            panel.visualTree.SendEvent(e);
        }

        [UnityTest]
        public IEnumerator APointersClickOnACardPicksItInTheSim()
        {
            var ui = Keep(new RunUi(_host.transform, _palette, _content, _faces));
            var keys = Keep(new KeyInput());
            ui.Picked += slot => keys.Tap((Key)((int)Key.One + slot), 0);
            var sim = FirstOffer(ui, out var offer);
            Assert.That(ui.Cards.Up && ui.Cards.Count == 3, "the first offer's three cards, at once");
            yield return null; // the panel's layout
            var card = ui.Cards.CardAt(1);
            var centre = card.worldBound.center;
            Assert.That(card.panel.Pick(centre), Is.SameAs(card), "the card itself takes the pointer");
            Send(card.panel, EventType.MouseDown, centre);
            Send(card.panel, EventType.MouseUp, centre);
            keys.Drain(sim, new SimClock { Sim = sim });
            sim.StepTo(sim.Now + 0.05);
            var events = sim.EventsSince(0).ToArray();
            var picks = events.Where(e => e.Kind == SimEventKind.Pick).ToList();
            Assert.That(picks, Has.Count.EqualTo(1));
            Assert.That(picks[0].I0, Is.EqualTo(1), "the middle card's slot");
            Assert.That(picks[0].I1, Is.EqualTo(offer.I1), "the middle card");
            foreach (var e in events) ui.Take(e);
            Assert.That(ui.Cards.Up, Is.False, "put away on the pick");
            Assert.That(sim.State.Offer.Up, Is.False);
        }

        /// <summary>
        /// The keyboard's state with <paramref name="held"/> down, as an event straight into <paramref name="keys"/>: a
        /// batch-mode editor runs no native input, so the Input System's queue never dispatches there.
        /// </summary>
        void Press(KeyInput keys, params UnityEngine.InputSystem.Key[] held)
        {
            _keyboard ??= InputSystem.AddDevice<Keyboard>("UI tests keyboard");
            using (StateEvent.From(_keyboard, out var ev))
            {
                foreach (var key in _keyboard.allKeys) key.WriteValueIntoEvent(Array.IndexOf(held, key.keyCode) >= 0 ? 1f : 0f, ev);
                keys.OnEvent(ev, _keyboard);
            }
        }

        [Test]
        public void KeyboardFReachesTheSimAndSuspendRequiresRelease()
        {
            var keys = Keep(new KeyInput());
            var sim = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>() });
            sim.StepTo(1);
            Press(keys, UnityEngine.InputSystem.Key.F);
            keys.Drain(sim, new SimClock { Sim = sim });
            Assert.That(sim.State.Hero.Dashes, Is.EqualTo(1));
            Assert.That(sim.EventsSince(0).ToArray().Single(e => e.Kind == SimEventKind.HeroDash).InPlaceCleave, Is.True);
            keys.Suspend(sim, new SimClock { Sim = sim }, 1);
            keys.Resume(); sim.StepTo(2);
            Press(keys, UnityEngine.InputSystem.Key.F);
            keys.Drain(sim, new SimClock { Sim = sim });
            Assert.That(sim.State.Hero.Dashes, Is.EqualTo(1));
            Press(keys); Press(keys, UnityEngine.InputSystem.Key.F);
            keys.Drain(sim, new SimClock { Sim = sim });
            Assert.That(sim.State.Hero.Dashes, Is.EqualTo(2));
        }

        [Test]
        public void TheKeysPickACardByItsNumber()
        {
            var ui = Keep(new RunUi(_host.transform, _palette, _content, _faces));
            var keys = Keep(new KeyInput());
            var sim = FirstOffer(ui, out var offer);
            int cursor = sim.EventCount;
            Press(keys, UnityEngine.InputSystem.Key.Digit3);
            Press(keys);
            Assert.That(keys.Drain(sim, new SimClock { Sim = sim }), Is.EqualTo(2), "a press and a release");
            var pick = Since(sim, ref cursor, ui).Single(e => e.Kind == SimEventKind.Pick);
            Assert.That(pick.I0, Is.EqualTo(2));
            Assert.That(pick.I1, Is.EqualTo(offer.I2), "the third card");
            Assert.That(ui.Cards.Up, Is.False);
        }

        [Test]
        public void TheArrowsFocusACardAndEnterPicksIt()
        {
            var ui = Keep(new RunUi(_host.transform, _palette, _content, _faces));
            var keys = Keep(new KeyInput());
            var sim = FirstOffer(ui, out var offer);
            var clock = new SimClock { Sim = sim };
            int cursor = sim.EventCount;
            Press(keys, UnityEngine.InputSystem.Key.RightArrow);
            Press(keys);
            keys.Drain(sim, clock);
            Assert.That(Since(sim, ref cursor, ui).Single(e => e.Kind == SimEventKind.CardFocus).I0, Is.EqualTo(1), "the middle card first");
            Assert.That(ui.Cards.CardAt(1).ClassListContains("card--focused"));
            Press(keys, UnityEngine.InputSystem.Key.D);
            Press(keys);
            keys.Drain(sim, clock);
            Since(sim, ref cursor, ui);
            Assert.That(ui.Cards.CardAt(2).ClassListContains("card--focused") && !ui.Cards.CardAt(1).ClassListContains("card--focused"));
            Press(keys, UnityEngine.InputSystem.Key.Enter);
            Press(keys);
            keys.Drain(sim, clock);
            var pick = Since(sim, ref cursor, ui).Single(e => e.Kind == SimEventKind.Pick);
            Assert.That(pick.I0, Is.EqualTo(2));
            Assert.That(pick.I1, Is.EqualTo(offer.I2));
        }

        [Test]
        public void QueuedChoicesRequireReleaseAndPausePreservesTheOffer()
        {
            var ui = Keep(new RunUi(_host.transform, _palette, _content, _faces));
            var keys = Keep(new KeyInput());
            var sim = FirstOffer(ui, out _);
            var clock = new SimClock { Sim = sim };
            int cursor = sim.EventCount;
            sim.Progression.CollectExperience(sim, sim.State.ExperienceNeeded * 2, sim.Now);
            sim.StepTo(sim.Now + Simulation.TickSeconds);
            Press(keys, UnityEngine.InputSystem.Key.Digit1, UnityEngine.InputSystem.Key.Space);
            keys.Drain(sim, clock);
            var first = Since(sim, ref cursor, ui);
            Assert.That(first.Count(e => e.Kind == SimEventKind.Pick), Is.EqualTo(1));
            Assert.That(ui.Cards.Up && sim.State.Held);
            Assert.That(ui.Cards.CardAt(0).ClassListContains("card--focused"), Is.False, "new offer resets focus");
            Press(keys, UnityEngine.InputSystem.Key.Digit1, UnityEngine.InputSystem.Key.Space);
            keys.Drain(sim, clock);
            Assert.That(Since(sim, ref cursor, ui).Any(e => e.Kind == SimEventKind.Pick), Is.False);
            keys.Suspend(sim, clock, sim.Now);
            sim.Control(RunControl.Pause, sim.Now);
            var offer = sim.State.Offer;
            Press(keys, UnityEngine.InputSystem.Key.Digit3);
            keys.Drain(sim, clock);
            Assert.That(sim.State.Offer, Is.EqualTo(offer));
            sim.Control(RunControl.Resume, sim.Now);
            keys.Resume();
            Press(keys);
            keys.Drain(sim, clock);
            while (sim.State.Offer.Up)
            {
                Press(keys, UnityEngine.InputSystem.Key.Digit1);
                Press(keys);
                keys.Drain(sim, clock);
                Since(sim, ref cursor, ui);
            }
            sim.StepTo(sim.Now + 0.5);
            Assert.That(ui.Cards.Up, Is.False);
            Assert.That(sim.EventsSince(0).ToArray().Any(e => e.Kind == SimEventKind.HeroDash), Is.False);
            Assert.That(sim.State.Hero.X, Is.Zero);
            Assert.That(sim.State.Hero.Y, Is.Zero);
        }

        [Test]
        public void WeaponFormsHaveDistinctIconsAndSupportPipsUseTheirContentCaps()
        {
            var cards = new CardsView();
            foreach (WeaponKind weapon in Enum.GetValues(typeof(WeaponKind)))
            {
                var icons = new HashSet<Icon>();
                foreach (int level in new[] { 1, 3, 5, 8 })
                {
                    var face = _faces.Of(Card.Of(weapon, level, level - 1));
                    icons.Add(face.Icon);
                    cards.Show(new[] { face }, 0);
                    Assert.That(cards.CardAt(0).Query(className: "pip").ToList().Count, Is.EqualTo(8));
                    Assert.That(cards.CardAt(0).Query(className: "pip--lit").ToList().Count, Is.EqualTo(level));
                    Assert.That(face.FormChange, Is.EqualTo(level > 1));
                }
                Assert.That(icons.Count, Is.EqualTo(4), weapon.ToString());
            }
            foreach (SupportKind support in Enum.GetValues(typeof(SupportKind)))
            {
                int cap = _content.Combat.Support(support).MaxLevel;
                cards.Show(new[] { _faces.Of(Card.Of(support, cap, cap - 1)) }, 0);
                Assert.That(cards.CardAt(0).Query(className: "pip--lit").ToList().Count, Is.EqualTo(cap));
            }
        }

        [Test]
        public void ProvisionalWeaponIconsWorkWithoutFormAssets()
        {
            string dir = Path.Combine(Path.GetTempPath(), "champions-faces-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "icons"));
            try
            {
                string json = File.ReadAllText(Path.Combine(ContentDir, CardFaces.File));
                json = System.Text.RegularExpressions.Regex.Replace(json, @", ""formIcons"": \[[^\]]*\]", "");
                File.WriteAllText(Path.Combine(dir, CardFaces.File), json);
                foreach (var icon in _faces.Icons.Keys.Where(n => !n.Contains("-form-")))
                    File.Copy(Path.Combine(ContentDir, "icons", icon + ".svg"), Path.Combine(dir, "icons", icon + ".svg"));
                var faces = CardFaces.Load(dir, _content);
                foreach (WeaponKind weapon in Enum.GetValues(typeof(WeaponKind)))
                    Assert.That(faces.Of(Card.Of(weapon, 8, 7)).Icon.Strokes, Is.Not.Empty);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void BossHudShowsOnlyHealthAndXpWithoutTakingThePointer()
        {
            using var hud = new Hud(_palette);
            hud.Present(0.4f, new BossBar { Up = true, Health = 0.6f, Colour = Color.red });
            Assert.That(hud.Query(className: "hud__fill").ToList().Count, Is.EqualTo(2));
            Assert.That(hud.Q(className: "hud__xp").Q(className: "hud__fill").style.width.value.value, Is.EqualTo(40).Within(0.01));
            Assert.That(hud.Q(className: "hud__health").Q(className: "hud__fill").style.width.value.value, Is.EqualTo(60).Within(0.01));
            Assert.That(hud.pickingMode, Is.EqualTo(PickingMode.Ignore));
            hud.Present(0.5f, default);
            Assert.That(hud.Q(className: "hud__boss").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator BossHealthHasVisibleBoundsBelowThePhaseClock()
        {
            var panel = Keep(new UiPanel(_host.transform, "HUD layout", 0));
            var hud = Keep(new Hud(_palette)); panel.Root.Add(hud);
            hud.Present(new SimSnapshot { HeroHealth = 8, HeroMaxHealth = 100 }, new BossBar { Up = true, Health = .6f, Colour = Color.red });
            foreach (var size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1440), new Vector2(1280, 1024) })
            {
                panel.Root.style.width = size.x; panel.Root.style.height = size.y;
                yield return null;
                var bar = hud.Q(className: "hud__health");
                var vessel = hud.Q(className: "hud__vessel").worldBound;
                Assert.That(bar.worldBound.width, Is.GreaterThan(100));
                Assert.That(bar.worldBound.height, Is.GreaterThanOrEqualTo(5));
                Assert.That(bar.worldBound.yMin, Is.GreaterThan(hud.Q(className: "hud__timer").worldBound.yMax));
                Assert.That(vessel.Overlaps(bar.worldBound), Is.False);
                Assert.That(vessel.xMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(vessel.yMax, Is.LessThanOrEqualTo(size.y));
            }
        }

        [Test]
        public void HealthOrbIsEmptyAtZeroFilledAtFullAndAnimatedBetweenFrames()
        {
            var material = new Material(Resources.Load<Shader>("UI/HealthOrb"));
            var target = new RenderTexture(64, 64, 0);
            var read = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            Color[] Sample(float fill, float time)
            {
                material.SetFloat("_Fill", fill); material.SetFloat("_Motion", time);
                Graphics.Blit(null, target, material);
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); read.Apply();
                return read.GetPixels();
            }
            try
            {
                var empty = Sample(0, 0); var low = Sample(.08f, 0); var full = Sample(1, 0);
                int Liquid(Color[] colors) => colors.Count(c => c.r > .3f && c.r > c.g * 2);
                Assert.That(Liquid(low), Is.GreaterThan(Liquid(empty)), "low health remains visibly filled");
                Assert.That(Liquid(full), Is.GreaterThan(Liquid(low) * 2), "the boundary tracks health");
                Assert.That(full[32 * 64 + 32].r, Is.GreaterThan(empty[32 * 64 + 32].r + .2f));
                var later = Sample(.5f, .5f); var earlier = Sample(.5f, 0);
                Assert.That(later.Where((c, i) => (c - earlier[i]).maxColorComponent > .01f).Count(), Is.GreaterThan(50));
                Assert.That(empty[0].a, Is.LessThan(.01f), "outside the orb stays transparent");
            }
            finally
            {
                RenderTexture.active = previous; target.Release();
                Object.DestroyImmediate(target); Object.DestroyImmediate(read); Object.DestroyImmediate(material);
            }
        }

        [TestCase(179.99, 99, false, "HORDE  02:59")]
        [TestCase(180, 0, true, "BOSS  00:00")]
        [TestCase(180, 61.9, true, "BOSS  01:01")]
        [TestCase(361, 0, false, "HORDE  06:01")]
        public void HudFormatsTheActivePhaseClock(double horde, double boss, bool up, string expected)
        {
            Assert.That(Hud.ClockText(horde, boss, up), Is.EqualTo(expected));
        }

        /// <summary>Every screen, built as the game builds it, by name.</summary>
        IEnumerable<(string Name, VisualElement Screen)> Screens()
        {
            void None() { }
            var apps = new[] { new Plugin.App(1, "Spotify"), new Plugin.App(2, "Google Chrome Helper") };
            yield return ("title", new TitleScreen(None, None, None));
            yield return ("picker", new MusicPicker(() => apps, _ => { }, None, None));
            yield return ("picker, nothing playing", new MusicPicker(() => Array.Empty<Plugin.App>(), _ => { }, None, None));
            yield return ("pause", new PauseMenu(None, None, None));
            yield return ("calibration choice", new MenuScreen(new[] { new MenuList.Row("Video", None), new MenuList.Row("Input", None) }, None, false));
            yield return ("calibration video", Keep(new CalibrationScreen(_host.transform, CalibrationScreen.Mode.Video, true, None)));
            yield return ("calibration input", Keep(new CalibrationScreen(_host.transform, CalibrationScreen.Mode.Input, true, None)));
            foreach (bool victory in new[] { true, false })
            {
                var summary = new EndSummary { Victory = victory, CombatSeconds = 847.3, Kills = 2318 };
                summary.Build.AddRange(new[] { "sword-form-1", "bow-form-2", "orbitBlades-form-3", "tempo" }.Select(n => _faces.Icons[n]));
                yield return (victory ? "victory" : "death", new EndCard(summary, Icon.Load(ContentDir, "time"), Icon.Load(ContentDir, "kills"), None, None));
            }
        }

        [UnityTest]
        public IEnumerator EveryInteractiveElementTakesThePointer()
        {
            var panel = Keep(new UiPanel(_host.transform, "UI tests", 0));
            var ui = Keep(new RunUi(_host.transform, _palette, _content, _faces));
            FirstOffer(ui, out _);
            foreach (var (name, screen) in Screens())
            {
                panel.Root.Clear();
                panel.Root.Add(screen);
                yield return null; // the panel's layout
                var rows = screen.Query<Label>(className: "menu__row").ToList().Where(r => !r.ClassListContains("menu__row--dim")).ToList();
                Assert.That(rows.Count > 0 || name == "calibration input", name); // the tap-along's rows come once it is over
                foreach (var row in rows)
                {
                    Assert.That(row.pickingMode, Is.EqualTo(PickingMode.Position));
                    Assert.That(row.panel.Pick(row.worldBound.center), Is.SameAs(row), $"{name}: \"{row.text}\" takes the pointer");
                }
            }
            yield return null;
            for (int i = 0; i < ui.Cards.Count; i++)
            {
                var card = ui.Cards.CardAt(i);
                Assert.That(card.pickingMode, Is.EqualTo(PickingMode.Position));
                Assert.That(card.panel.Pick(card.worldBound.center), Is.SameAs(card), $"card {i}");
            }
        }

        [Test]
        public void TheMenusMoveAndChooseByKeys()
        {
            var chosen = new List<string>();
            var title = new TitleScreen(() => chosen.Add("play"), () => chosen.Add("calibrate"), () => chosen.Add("quit"));
            Assert.That(title.Menu.Focused, Is.Zero, "Play first");
            title.Key(MenuKey.Down);
            title.Key(MenuKey.Choose);
            title.Key(MenuKey.Up);
            title.Key(MenuKey.Up); // wraps to Quit
            title.Key(MenuKey.Choose);
            Assert.That(chosen, Is.EqualTo(new[] { "calibrate", "quit" }));
            bool back = false;
            var picker = new MusicPicker(Array.Empty<Plugin.App>, _ => { }, () => chosen.Add("file"), () => back = true);
            Assert.That(picker.Menu.LabelAt(picker.Menu.Focused).text, Is.EqualTo(MusicPicker.FileRow), "the note is passed over");
            picker.Key(MenuKey.Choose);
            picker.Key(MenuKey.Back);
            Assert.That(chosen.Last(), Is.EqualTo("file"));
            Assert.That(back);
        }

        [Test]
        public void ThePauseMenusQualityStepsThroughTheTiers()
        {
            var before = QualityTiers.Current;
            string kept = PlayerPrefs.HasKey(QualityTiers.PrefKey) ? PlayerPrefs.GetString(QualityTiers.PrefKey) : null;
            try
            {
                var pause = new PauseMenu(() => { }, () => { }, () => { });
                pause.Key(MenuKey.Down);
                pause.Key(MenuKey.Down);
                QualityTiers.Apply(Tier.Low);
                pause.Key(MenuKey.Right);
                Assert.That(QualityTiers.Current, Is.EqualTo(Tier.Medium));
                Assert.That(pause.Menu.LabelAt(2).text, Is.EqualTo("Quality Medium"));
                pause.Key(MenuKey.Left);
                pause.Key(MenuKey.Left);
                Assert.That(QualityTiers.Current, Is.EqualTo(Tier.High), "wrapping");
            }
            finally
            {
                QualityTiers.Apply(before);
                if (kept != null) PlayerPrefs.SetString(QualityTiers.PrefKey, kept);
                else PlayerPrefs.DeleteKey(QualityTiers.PrefKey);
            }
        }

        static IEnumerable<string> Texts(VisualElement screen) =>
            screen.Query<TextElement>().ToList().Where(t => !string.IsNullOrEmpty(t.text)).Select(t => t.text);

        [Test]
        public void EveryScreenKeepsToTheDesignsWordBudget()
        {
            foreach (var (name, screen) in Screens())
            {
                var apps = new HashSet<string> { "Spotify", "Google Chrome Helper" }; // the apps' own names, not the game's words
                foreach (var row in screen.Query<Label>(className: "menu__row").ToList())
                    if (!apps.Contains(row.text))
                        Assert.That(CardFaces.Words(row.text), Is.InRange(1, 2), $"{name}: the row \"{row.text}\"");
                foreach (var text in Texts(screen).Where(t => !apps.Contains(t)))
                    Assert.That(CardFaces.Words(text), Is.LessThanOrEqualTo(CardFaces.LineWords), $"{name}: \"{text}\"");
                int words = Texts(screen).Where(t => !apps.Contains(t)).Sum(CardFaces.Words);
                int budget = name == "victory" || name == "death" ? 1 : name == "title" ? 8 : 14;
                Assert.That(words, Is.LessThanOrEqualTo(budget), $"{name}: {string.Join(" / ", Texts(screen))}");
            }
            var endCard = Screens().Single(s => s.Name == "death").Screen;
            Assert.That(Texts(endCard).Where(t => CardFaces.Words(t) > 0), Is.EqualTo(new[] { "Again" }), "the end card's numbers wear icons");
        }

        [Test]
        public void DuringPlayTextIsLimitedToHealthClockAndCards()
        {
            var ui = Keep(new RunUi(_host.transform, _palette, _content, _faces));
            var root = ui.Hud.parent;
            ui.Hud.Present(new SimSnapshot { HeroHealth = 8, HeroMaxHealth = 100, HordeClock = 299 }, default);
            Assert.That(Texts(ui.Hud), Is.EquivalentTo(new[] { "8 / 100", "HORDE  04:59" }));
            FirstOffer(ui, out _);
            for (int i = 0; i < ui.Cards.Count; i++)
            {
                var texts = Texts(ui.Cards.CardAt(i)).ToList();
                Assert.That(texts, Has.Count.EqualTo(2), "a name and a line, nothing else");
                Assert.That(CardFaces.Words(texts[0]), Is.InRange(1, CardFaces.NameWords));
                Assert.That(CardFaces.Words(texts[1]), Is.InRange(1, CardFaces.LineWords));
            }
            Assert.That(Texts(root).Count(), Is.EqualTo(2 * ui.Cards.Count + 2));
        }

        [Test]
        public void EveryCardTheContentCanOfferHasAFaceWithinItsBudget()
        {
            var all = _faces.All().ToList();
            int dashes = _content.Dashes.Kinds.Sum(k => k.Ranks.Length + (k.Evolution != null ? 1 : 0));
            Assert.That(all, Has.Count.EqualTo(dashes + Upgrade.AttackCount * (Upgrade.AttackTop + 1) + Upgrade.ModCount * Upgrade.ModTop
                + CombatRules.WeaponCount * 8 + Enumerable.Range(0, CombatRules.SupportCount).Sum(i => _content.Combat.Support((SupportKind)i).MaxLevel)));
            foreach (var f in all)
            {
                Assert.That(CardFaces.Words(f.Name), Is.InRange(1, CardFaces.NameWords), f.Id);
                Assert.That(CardFaces.Words(f.Line), Is.InRange(1, CardFaces.LineWords), $"{f.Id} {f.Rank}: {f.Line}");
                Assert.That(f.Icon.Strokes, Is.Not.Empty, f.Id);
            }
            Assert.That(_faces.Of(CardKind.Attack, "swordSlash", Upgrade.Evolution).Name, Is.EqualTo("Tempest"));
            Assert.That(_faces.Of(Card.Of(ModKind.Tempo, 2, 1)).Line, Is.EqualTo("Every attack fires faster"));
        }

        [Test]
        public void IconsReadTheSvgSubset()
        {
            var icon = Icon.Parse("<svg viewBox=\"0 0 64 64\" stroke-width=\"2\"><path d=\"M10 10 L20 10 l0 10 H5 V5 Z\"/>" +
                                  "<path d=\"M0 32 A32 32 0 0 1 64 32\" opacity=\"0.5\" stroke-width=\"1\"/><circle cx=\"32\" cy=\"32\" r=\"4\"/></svg>", "test");
            Assert.That(icon.Size, Is.EqualTo(new Vector2(64, 64)));
            Assert.That(icon.Strokes, Has.Count.EqualTo(3));
            Assert.That(icon.Strokes[0].Points, Is.EqualTo(new[] { new Vector2(10, 10), new Vector2(20, 10), new Vector2(20, 20), new Vector2(5, 20), new Vector2(5, 5) }));
            Assert.That(icon.Strokes[0].Closed && icon.Strokes[0].Width == 2);
            var arc = icon.Strokes[1];
            Assert.That(arc.Opacity == 0.5f && arc.Width == 1);
            Assert.That(arc.Points.Last(), Is.EqualTo(new Vector2(64, 32)));
            Assert.That(arc.Points.Min(p => p.y), Is.EqualTo(0).Within(0.1f), "the half circle's top");
            foreach (string file in Directory.GetFiles(Path.Combine(ContentDir, "icons"), "*.svg"))
                Assert.That(Icon.Load(ContentDir, Path.GetFileNameWithoutExtension(file)).Strokes, Is.Not.Empty, file);
        }

        [Test]
        public void CalibrationKeepsEachDevicesOffsetsInWholeMilliseconds()
        {
            string path = Path.Combine(Path.GetTempPath(), "champions-calibration-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = CalibrationStore.Load(path);
                Assert.That(store.Has("dev"), Is.False);
                Assert.That(store.Set(new Plugin.Device("dev", "Speakers", 0.01, false), new Offsets { Video = 0.0123, Input = -0.0301 }));
                var again = CalibrationStore.Load(path).For("dev");
                Assert.That(again.Video, Is.EqualTo(0.012).Within(1e-9));
                Assert.That(again.Input, Is.EqualTo(-0.030).Within(1e-9));
                Assert.That(CalibrationStore.Load(path).For("other").Video, Is.Zero, "a device not calibrated plays with none");
            }
            finally { File.Delete(path); }
            var clicks = Enumerable.Range(0, TapAlong.Clicks).Select(i => 1 + i * TapAlong.Period).ToList();
            var taps = clicks.Select((c, i) => c + (i < 2 ? 0.1 : 0.02 + 0.001 * (i % 3))).ToList();
            Assert.That(TapAlong.Offset(taps, clicks), Is.EqualTo(0.021).Within(1e-9), "the median after the first two");
            Assert.That(TapAlong.Offset(taps.Take(4).ToList(), clicks), Is.Null, "too few counted taps");
        }
    }
}

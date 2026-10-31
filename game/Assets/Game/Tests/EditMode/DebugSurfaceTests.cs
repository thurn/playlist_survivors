using System;
using System.IO;
using System.Linq;
using Game.Client.Loop;
using NUnit.Framework;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;
using UnityEngine;
using InputKey = UnityEngine.InputSystem.Key;

namespace Game.Tests
{
    /// <summary>
    /// The client's debug surfaces (unity-game.md, Debug Surfaces): `--debug`'s names and arguments, the F1 menu's
    /// rows giving their commands to the sim, and `--level`'s files in place of the bake's.
    /// </summary>
    public class DebugSurfaceTests
    {
        static GameContent Content => GameContent.Load(Path.Combine(Application.dataPath, "..", "Content"));

        static Simulation Run(GameContent content) =>
            new Simulation(RunLevel.Config(content, null, new[] { -18.5, -9.25, 18.5, -9.25, 24.75, 15.5, -24.75, 15.5 }));

        [Test]
        public void DebugNamesTheirArgumentsByIdAndTheJumpByMinutes()
        {
            var content = Content;
            var list = DebugCommands.Parse("jump:12.5@3,spawn:werewolf@1, weapon:magic-wand@2,passive:spinach@2,evolve:bloody-tear@4," +
                                           "boss:boss-xlflower@5,invulnerable@6,invulnerable:0@7,change@0.5", content);
            CollectionAssert.AreEqual(new[]
            {
                (0.5, DebugCommand.SectionChange, 0), (1.0, DebugCommand.SpawnRole, content.RoleIndex("werewolf")),
                (2.0, DebugCommand.GrantWeapon, content.WeaponIndex("magic-wand")), (2.0, DebugCommand.GrantPassive, content.PassiveIndex("spinach")),
                (3.0, DebugCommand.JumpClock, 750), (4.0, DebugCommand.GrantEvolution, content.WeaponIndex("bloody-tear")),
                (5.0, DebugCommand.SpawnBoss, content.RoleIndex("boss-xlflower")), (6.0, DebugCommand.Invulnerable, 1),
                (7.0, DebugCommand.Invulnerable, 0),
            }, list);
            Assert.Throws<ArgumentException>(() => DebugCommands.Parse("spawn:nobody@1", content));
            Assert.Throws<ArgumentException>(() => DebugCommands.Parse("weapon:bloody-tear@1", content), "an evolution is evolve's");
            Assert.Throws<ArgumentException>(() => DebugCommands.Parse("evolve:sword@1", content));
            Assert.Throws<ArgumentException>(() => DebugCommands.Parse("spawn@1", content), "spawn needs a role");
            Assert.Throws<ArgumentException>(() => DebugCommands.Parse("accent:1@1", content), "accent takes none");
        }

        [Test]
        public void TheMenusLettersGiveTheirRowsCommandsAndShiftMovesAChoiceOn()
        {
            var content = Content;
            var sim = Run(content);
            var commands = new DebugCommands(null, content);
            var menu = new DebugMenu(null, content);
            Assert.IsTrue(menu.Press(InputKey.M, false, sim, commands));
            Assert.IsTrue(menu.Press(InputKey.M, true, sim, commands));
            Assert.IsTrue(menu.Press(InputKey.M, false, sim, commands));
            Assert.IsTrue(menu.Press(InputKey.J, false, sim, commands));
            Assert.IsTrue(menu.Press(InputKey.I, false, sim, commands));
            Assert.IsTrue(menu.Press(InputKey.I, false, sim, commands));
            Assert.IsTrue(menu.Press(InputKey.N, true, sim, commands));
            Assert.IsTrue(menu.Press(InputKey.N, false, sim, commands));
            Assert.IsTrue(menu.Press(InputKey.E, false, sim, commands));
            Assert.IsFalse(menu.Press(InputKey.W, false, sim, commands), "the hero's keys are none of the menu's");
            CollectionAssert.AreEqual(new[]
            {
                (DebugCommand.SpawnRole, 0), (DebugCommand.SpawnRole, 1), (DebugCommand.JumpClock, 60),
                (DebugCommand.Invulnerable, 1), (DebugCommand.Invulnerable, 0), (DebugCommand.KickLine, 0),
                (DebugCommand.GrantEvolution, content.WeaponIndex("bloody-tear")),
            }, sim.Commands.ToArray().Select(c => (c.Command, c.Arg)));
            Assert.AreEqual(7, commands.Given);
            Assert.AreEqual(content.WeaponIndex("bloody-tear"), sim.State.WeaponSlots[0].Index, "the sword evolved");
        }

        [Test]
        public void LevelFilesTakeThePlaceOfTheBakesByName()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ps-level-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var schedule = File.ReadAllText(Path.Combine(Application.dataPath, "..", "Content", "schedule.json"));
                File.WriteAllText(Path.Combine(dir, "schedule.json"), schedule.Replace("\"durationS\": 1800", "\"durationS\": 600"));
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a level file");
                var level = LevelFiles.Load(dir);
                CollectionAssert.AreEquivalent(new[] { "schedule.json" }, level.Replaced);
                StringAssert.Contains("\"durationS\": 600", level.Text("schedule.json"));
                Assert.Throws<ArgumentException>(() => LevelFiles.Load(Path.Combine(dir, "notes.txt")));
                var single = LevelFiles.Load(Path.Combine(dir, "schedule.json"));
                CollectionAssert.AreEquivalent(new[] { "schedule.json" }, single.Replaced);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}

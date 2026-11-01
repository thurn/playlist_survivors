using System.IO;
using Game.Client.Crowd;
using NUnit.Framework;
using PS.Game.Sim.Content;
using UnityEngine;
using PS.Look.Crowd;

namespace Game.Tests
{
    /// <summary>The crowd's stable slots (CrowdSlots) at the budget CrowdView gives each type for the content's cap on the living.</summary>
    public class CrowdSlotsTests
    {
        [Test]
        public void AWholeCrowdOfOneTypeReplacedOnOneBeatFitsItsTypesSlots()
        {
            int n = GameContent.Load(Path.Combine(Application.dataPath, "..", "Content")).Schedule.Cap;
            var slots = new CrowdSlots(1, CrowdView.SlotsPerType(n));
            slots.Begin();
            for (int e = 0; e < n; e++) slots.Assign(0, e);
            slots.End();
            // The next frame, every one gone and as many new: the old slots stay Hidden this frame.
            slots.Begin();
            for (int e = n; e < 2 * n; e++) slots.Assign(0, e);
            slots.End();
            Assert.AreEqual(2 * n, slots.Count(0));
            // A frame later the freed slots are taken again, and the high-water mark holds.
            slots.Begin();
            for (int e = 2 * n; e < 3 * n; e++) slots.Assign(0, e);
            slots.End();
            Assert.AreEqual(2 * n, slots.Count(0));
            for (int s = 0; s < n; s++) Assert.IsTrue(slots.Live(0, s));
        }

        [Test]
        public void PastItsSlotsAnEntityGoesUndrawnForTheFrameAndTheCrowdGoesOn()
        {
            var slots = new CrowdSlots(1, 4);
            slots.Begin();
            for (int e = 0; e < 4; e++) Assert.That(slots.Assign(0, e), Is.GreaterThanOrEqualTo(0));
            Assert.AreEqual(-1, slots.Assign(0, 4), "no slot left");
            slots.End();
            // The first four leave: their slots free at this frame's end, and the next frame the fifth takes one.
            slots.Begin();
            Assert.AreEqual(-1, slots.Assign(0, 4), "theirs are freed only at the frame's end");
            slots.End();
            slots.Begin();
            int slot = slots.Assign(0, 4);
            slots.End();
            Assert.That(slot, Is.InRange(0, 3));
            Assert.IsTrue(slots.Live(0, slot));
        }
    }
}

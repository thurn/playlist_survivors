using Game.Client.Crowd;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>The crowd's stable slots (CrowdSlots) at the budget CrowdView gives each type.</summary>
    public class CrowdSlotsTests
    {
        [Test]
        public void AWholeCrowdOfOneTypeReplacedOnOneBeatFitsItsTypesSlots()
        {
            var slots = new CrowdSlots(1, CrowdView.SlotsPerType);
            const int n = CrowdView.Capacity;
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
    }
}

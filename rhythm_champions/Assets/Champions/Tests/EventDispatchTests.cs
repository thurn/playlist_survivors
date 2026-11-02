using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Champions.Client.Loop;
using Champions.Sim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Champions.Tests
{
    /// <summary>
    /// The run's EventDispatch (plan.md, The Client): every presenter gets every event once, in order, then each frame; one
    /// that throws is contained, logged once per call and exception type and counted, and the others go on.
    /// </summary>
    public class EventDispatchTests
    {
        [Test]
        public void APresenterThatThrowsLeavesTheOthersTheirEventsAndFrames()
        {
            var sim = new Simulation(new RunInputs().Build(null));
            sim.StepTo(1);
            var d = new EventDispatch();
            var seen = new List<SimEventKind>();
            int frames = 0, flushed = 0;
            d.Add("broken", (in SimEvent e) => throw new InvalidOperationException("broken"), null, (in PresentFrame f) => throw new InvalidOperationException("frame"));
            d.Add("kept", (in SimEvent e) => seen.Add(e.Kind), () => flushed++, (in PresentFrame f) => frames++);
            // Each call's first throw is logged (Unity shows the presenter's own exception); the rest are only counted.
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: broken"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: frame"));
            int events = sim.EventCount;
            Assert.Greater(events, 0);
            d.Drain(sim);
            d.Present(new PresentFrame { Song = 1, Sim = sim });
            d.Present(new PresentFrame { Song = 1.1, Sim = sim });
            Assert.AreEqual(events, seen.Count, "every event, once");
            Assert.AreEqual(1, flushed);
            Assert.AreEqual(2, frames);
            Assert.AreEqual(events + 2, d.Faults);
            StringAssert.Contains("Take of broken: InvalidOperationException x " + events, d.FaultReport());
            Assert.AreEqual(events, d.Cursor);
        }

        [Test]
        public void ARunningAheadDrainFeedsOnlyTheNamedPresenters()
        {
            var sim = new Simulation(new RunInputs().Build(null));
            sim.StepTo(1);
            var d = new EventDispatch();
            int a = 0, b = 0;
            d.Add("a", (in SimEvent e) => a++);
            d.Add("b", (in SimEvent e) => b++);
            d.Drain(sim, new[] { "a" });
            Assert.Greater(a, 0);
            Assert.AreEqual(0, b);
        }
    }
}

using System;
using System.IO;
using Champions.Client.Loop;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class RunDiagnosticsTests
    {
        [Test]
        public void DelayedInputBuffersOriginalGradeAndLogSurvivesAnAbortedRun()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                var s = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>() });
                string path;
                using (var log = new RunDiagnostics(s, root, "test scripted build=fixture"))
                {
                    path = log.Path;
                    s.StepTo(1); s.Press(Key.F, 1); s.Release(Key.F, 1);
                    s.StepTo(1.2); s.Press(Key.Space, 1.2); s.Release(Key.Space, 1.2);
                    // A delayed frame: heard at 1.5, delivered at 1.55, fires at ready=1.6.
                    s.StepTo(1.55); s.Press(Key.Space, 1.5); s.Release(Key.Space, 1.5);
                    s.StepTo(1.7); log.Sample(true, "inputCalibrationMs=25");
                    Assert.That(File.ReadAllText(path), Does.Contain("Space buffered heard=1.500000").And.Contain("Space buffer-fired heard=1.500000").And.Contain("Space cooldown"));
                    // Active file cannot be pruned by another player even beyond retention.
                    for (int i = 0; i < RunDiagnostics.RetainedRuns + 2; i++)
                    { string f = System.IO.Path.Combine(root, "old" + i + ".log"); File.WriteAllText(f, "old"); File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(1)); }
                    using (var concurrent = new RunDiagnostics(s, root, "concurrent"))
                        Assert.That(File.Exists(path), Is.True);
                }
                string summary = File.ReadAllText(path + ".summary");
                Assert.That(summary, Does.Contain("outcome=aborted").And.Contain("F:accepted=1").And.Contain("Space:buffer-fired=1").And.Contain("inputCalibrationMs=25"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void SaturatedDetailsStayBoundedWhileTotalsContinueAndRunsAreDistinct()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                var s = new Simulation(new SimConfig { SurvivorWeapons = true, Loadout = Array.Empty<Upgrade>() });
                string first;
                using (var log = new RunDiagnostics(s, root, "test"))
                {
                    first = log.Path;
                    s.StepTo(1); s.Press(Key.F, 1); s.Release(Key.F, 1);
                    for (int i = 0; i < 5000; i++) { s.Press(Key.F, 1.1); s.Release(Key.F, 1.1); }
                }
                Assert.That(new FileInfo(first).Length, Is.LessThanOrEqualTo(RunDiagnostics.DetailLimit));
                Assert.That(File.ReadAllText(first + ".summary"), Does.Contain("F:cooldown=5000").And.Not.Contain("detailOmitted=0"));
                using (var second = new RunDiagnostics(s, root, "test")) Assert.That(second.Path, Is.Not.EqualTo(first));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}

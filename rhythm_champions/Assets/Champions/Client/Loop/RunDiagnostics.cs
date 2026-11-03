using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Champions.Sim;

namespace Champions.Client.Loop
{
    /// <summary>Local, bounded run evidence, flushed independently of Unity's rotating log.
    /// The open detail file also leases the run against retention by another player.</summary>
    public sealed class RunDiagnostics : IDisposable
    {
        public const int DetailLimit = 512 * 1024, RetainedRuns = 100;
        readonly Simulation sim;
        readonly Dictionary<string, int> actions = new Dictionary<string, int>();
        readonly long[] counts = new long[Enum.GetValues(typeof(SimEventKind)).Length];
        FileStream detail;
        readonly string path, header;
        double next, lastCombat, lastAura, aura, damage, xp;
        long omitted;
        bool closed;
        string lastClock = "";
        public string Path => path;
        public string Error { get; private set; }

        public RunDiagnostics(Simulation sim, string root, string header, double bufferS = 0.15)
        {
            this.sim = sim; this.header = header;
            try
            {
                Directory.CreateDirectory(root);
                Trim(root);
                path = System.IO.Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".log");
                detail = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                Write(header);
                Write(string.Format(CultureInfo.InvariantCulture, "rules beatMs={0:F1} halfMs={1:F1} onsetMs={2:F1} beatCap=0.20 halfCap=0.16 cooldown={3:F3} buffer={4:F3}",
                    sim.Beats.Rules.BeatWindowS * 1000, sim.Beats.Rules.HalfBeatWindowS * 1000, sim.Beats.Rules.StrongOnsetWindowS * 1000, sim.State.Hero.DashCooldownS, bufferS));
                sim.ActionDiagnostic += Action;
                Sample(true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Error = e.GetType().Name; }
        }

        static void Trim(string root)
        {
            var files = new DirectoryInfo(root).GetFiles("*.log");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = RetainedRuns - 1; i < files.Length; i++)
            {
                try
                {
                    // Sharing violation means an active player: never remove its evidence.
                    using (new FileStream(files[i].FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    File.Delete(files[i].FullName);
                    File.Delete(files[i].FullName + ".summary");
                    File.Delete(files[i].FullName + ".tmp");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        void Write(string text)
        {
            if (detail == null) return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text + "\n");
                if (detail.Length + bytes.Length > DetailLimit) { omitted++; return; }
                detail.Write(bytes, 0, bytes.Length);
                detail.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Error = e.GetType().Name; }
        }

        public void KeyTimestamp(Key key, double real, double heard) => Write(string.Format(CultureInfo.InvariantCulture,
            "key {0} real={1:F6} mappedHeard={2:F6}", key, real, heard));

        void Action(Key key, double heard, string reason, Judged judged)
        {
            string k = key + ":" + reason;
            actions[k] = actions.TryGetValue(k, out int n) ? n + 1 : 1;
            string grade = k + ":" + judged.By;
            actions[grade] = actions.TryGetValue(grade, out int g) ? g + 1 : 1;
            Write(string.Format(CultureInfo.InvariantCulture,
                "action {0} {1} heard={2:F6} sim={3:F6} ageMs={4:F1} ready={5:F6} grade={6} candidate={7} point={8:F6} deltaMs={9:F1} windowMs={10:F1} reliability={11} revealedBeats={12}",
                key, reason, heard, sim.Now, 1000 * (sim.Now - heard), sim.State.Hero.DashReadyAt,
                judged.By, judged.Candidate, judged.Point, 1000 * (heard - judged.Point), 1000 * judged.Window, sim.Beats.StateAt(heard), sim.Beats.Revealed.Length));
        }

        public void Take(in SimEvent e)
        {
            counts[(int)e.Kind]++;
            if (e.Kind == SimEventKind.HeroHit) damage += Math.Max(0, e.D0);
            if (e.Kind == SimEventKind.PickupCollect && e.I1 == (int)PickupKind.Experience) xp += e.D2;
            switch (e.Kind)
            {
                case SimEventKind.LandingBlast:
                case SimEventKind.HeroHit:
                case SimEventKind.Pick:
                case SimEventKind.Hold:
                case SimEventKind.RunEnd:
                case SimEventKind.BossArrive:
                case SimEventKind.BossPhase:
                case SimEventKind.BossDeath:
                    Write(string.Format(CultureInfo.InvariantCulture, "event {0} song={1:F3} horde={2:F3} i={3},{4},{5} d0={6:F3}", e.Kind, e.T, sim.State.HordeClock, e.I0, e.I1, e.I2, e.D0));
                    break;
            }
        }

        public void Sample(bool force = false, string clock = "", string outcome = "running-or-interrupted")
        {
            if (closed || path == null) return;
            double combat = sim.CombatTime(sim.Now);
            aura += Math.Max(0, Math.Min(combat, lastAura) - lastCombat);
            lastCombat = combat; lastAura = sim.State.Build.AuraUntil;
            if (!force && sim.Now < next) return;
            next = sim.Now + 5;
            var s = sim.State;
            var b = new StringBuilder(header).AppendLine();
            b.AppendFormat(CultureInfo.InvariantCulture, "outcome={0} phase={1} end={2} song={3:F3} combat={4:F3} horde={5:F3} health={6:F1}/{7:F1} level={8} xp={9:F1}/{10:F1} collectedXp={11:F1} damage={12:F1} auraSampledSeconds={13:F3} detailOmitted={14}\n",
                outcome, s.Phase, s.End, sim.Now, s.RunClock, s.HordeClock, s.Hero.Health, s.Hero.MaxHealth, s.Level, s.Experience, s.ExperienceNeeded, xp, damage, aura, omitted);
            if (clock.Length > 0) lastClock = clock;
            b.AppendLine(lastClock);
            foreach (var a in actions) b.AppendLine(a.Key + "=" + a.Value);
            for (int i = 0; i < counts.Length; i++) if (counts[i] != 0) b.AppendLine(((SimEventKind)i) + "=" + counts[i]);
            foreach (WeaponKind w in Enum.GetValues(typeof(WeaponKind))) if (s.Build.Level(w) > 0) b.AppendLine("weapon " + w + "=" + s.Build.Level(w));
            foreach (SupportKind support in Enum.GetValues(typeof(SupportKind))) if (s.Build.Level(support) > 0) b.AppendLine("support " + support + "=" + s.Build.Level(support));
            try
            {
                File.WriteAllText(path + ".tmp", b.ToString());
                if (File.Exists(path + ".summary")) File.Replace(path + ".tmp", path + ".summary", null);
                else File.Move(path + ".tmp", path + ".summary");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Error = e.GetType().Name; }
        }

        public void Dispose()
        {
            if (closed) return;
            Sample(true, outcome: sim.State.Phase == RunPhase.Ended ? sim.State.End.ToString() : "aborted");
            closed = true;
            sim.ActionDiagnostic -= Action;
            detail?.Dispose(); detail = null;
        }
    }
}

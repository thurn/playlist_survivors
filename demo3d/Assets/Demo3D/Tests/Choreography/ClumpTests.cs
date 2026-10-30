using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    /// <summary>
    /// The song spawns its monster types in same-type clumps (the level script's <c>clumps</c>, the look
    /// pass's ps-e8r.9), against the same script with every type picked at random.
    /// </summary>
    public class ClumpTests
    {
        static Timeline mixed;

        /// <summary>The song with the level script's clumps removed: every monster's type picked at random.</summary>
        static Timeline Mixed
        {
            get
            {
                if (mixed != null) return mixed;
                LevelScript level = ChoreographyFiles.LoadLevel(Fixtures.Root);
                level.ClumpSize = 0;
                return mixed = Choreographer.Run(Fixtures.Song, level, Fixtures.Data);
            }
        }

        [Test]
        public void MostOfAMonstersNearestNeighboursAtSpawnShareItsType()
        {
            Assert.That(Fixtures.Level.ClumpSize, Is.GreaterThan(0), "the song's level script spawns in clumps");
            double clumped = NeighbourShare(Fixtures.SongTimeline), random = NeighbourShare(Mixed);
            TestContext.WriteLine("same-type share of the 4 nearest neighbours at spawn: clumped " + clumped.ToString("F3") + ", random " + random.ToString("F3"));
            Assert.That(clumped, Is.GreaterThanOrEqualTo(0.7));
        }

        /// <summary>
        /// Each section's spawns per type hold its mix: every type's share of the section's spawns is within 5
        /// points of its weight's share. (The random picks only hold the mix on average: a few dozen spawns of
        /// a type stray from it by more than that, so they are logged beside the clumped ones, not compared.)
        /// </summary>
        [Test]
        public void EachSectionsCountPerTypeHoldsItsMix()
        {
            Timeline tl = Fixtures.SongTimeline;
            int[,] clumped = SpawnsPerSectionAndType(tl), random = SpawnsPerSectionAndType(Mixed);
            var failures = new List<string>();
            for (int s = 0; s < tl.Sections.Length; s++)
            {
                LevelSection ls = Fixtures.Level.For(tl.Sections[s].Label);
                double weights = 0;
                foreach (MixEntry e in ls.Mix) weights += e.Weight;
                int total = 0, totalRandom = 0;
                for (int t = 0; t < tl.TypeNames.Length; t++) { total += clumped[s, t]; totalRandom += random[s, t]; }
                if (total == 0) continue;   // the outro and the ending only thin the horde out
                foreach (MixEntry e in ls.Mix)
                {
                    int t = Array.IndexOf(tl.TypeNames, e.Type);
                    double share = clumped[s, t] / (double)total, mix = e.Weight / weights;
                    string line = tl.Sections[s].Label + " " + e.Type + ": mix " + mix.ToString("P1") + ", clumped " + share.ToString("P1") + " (" + clumped[s, t]
                        + " of " + total + "), random " + (random[s, t] / (double)totalRandom).ToString("P1") + " (" + random[s, t] + " of " + totalRandom + ")";
                    TestContext.WriteLine(line);
                    if (Math.Abs(share - mix) > 0.05) failures.Add(line);
                }
            }
            Assert.That(failures, Is.Empty);
        }

        /// <summary>
        /// The mean, over every monster but the boss, of the share of its 4 nearest monsters (alive or
        /// dying) at its spawn time that are its type.
        /// </summary>
        static double NeighbourShare(Timeline tl)
        {
            Roster roster = Fixtures.Data.Roster;
            var monsters = new List<int>();
            foreach (EntityInfo e in tl.Entities)
                if (e.Kind == EntityKind.Monster && roster.Monsters[e.Type].Rank != MonsterRank.Boss) monsters.Add(e.Id);
            monsters.Sort((a, b) => tl.Entities[a].Spawn.CompareTo(tl.Entities[b].Spawn));
            var alive = new List<int>();
            var at = new List<Vector2>();
            var nearest = new float[4];
            var nearestType = new int[4];
            double sum = 0;
            int counted = 0;
            for (int i = 0; i < monsters.Count;)
            {
                double t = tl.Entities[monsters[i]].Spawn;
                int end = i;
                while (end < monsters.Count && tl.Entities[monsters[end]].Spawn == t) end++;
                alive.Clear();
                at.Clear();
                for (int k = 0; k < end; k++)
                {
                    int m = monsters[k];
                    if (!tl.Exists(m, t)) continue;
                    alive.Add(m);
                    at.Add(tl.PositionAt(m, t));
                }
                for (int k = i; k < end; k++)
                {
                    int m = monsters[k];
                    Vector2 p = tl.PositionAt(m, t);
                    int found = 0;
                    for (int j = 0; j < alive.Count; j++)
                    {
                        if (alive[j] == m) continue;
                        float d = Vector2.DistanceSquared(p, at[j]);
                        if (found == 4 && d >= nearest[3]) continue;
                        int slot = found < 4 ? found++ : 3;
                        while (slot > 0 && nearest[slot - 1] > d) { nearest[slot] = nearest[slot - 1]; nearestType[slot] = nearestType[slot - 1]; slot--; }
                        nearest[slot] = d;
                        nearestType[slot] = tl.Entities[alive[j]].Type;
                    }
                    if (found == 0) continue;
                    int same = 0;
                    for (int j = 0; j < found; j++) if (nearestType[j] == tl.Entities[m].Type) same++;
                    sum += same / (double)found;
                    counted++;
                }
                i = end;
            }
            return sum / counted;
        }

        static int[,] SpawnsPerSectionAndType(Timeline tl)
        {
            var counts = new int[tl.Sections.Length, tl.TypeNames.Length];
            foreach (EntityInfo e in tl.Entities)
                if (e.Kind == EntityKind.Monster) counts[tl.Map.SectionIndexAt(e.Spawn), e.Type]++;
            return counts;
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Champions.Sim.Tests
{
    public class IdentitySetTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(16)] [TestCase(2048)]
        public void MembershipMatchesTheReferenceAcrossGrowthCollisionsAndClears(int capacity)
        {
            var actual = new IdentitySet(capacity);
            var expected = new HashSet<long>();
            var random = new Random(604);
            for (int window = 0; window < 3; window++)
            {
                Check(0); Check(long.MinValue); Check(long.MaxValue);
                // Deliberately collide both halves of the identity hash, as well as
                // exercising ordinary generations and negative receiver/structure keys.
                for (uint high = 1; high <= 64; high++)
                    Check(unchecked((long)((ulong)high << 32 | high * 0x9E3779B9u)));
                for (int i = 0; i < 3000; i++)
                {
                    long key = i % 4 == 0 ? i : i % 4 == 1 ? long.MinValue | (2L << 32) | (uint)i : random.NextInt64();
                    Check(key);
                    Assert.That(actual.Contains(key ^ 0x145937AA), Is.EqualTo(expected.Contains(key ^ 0x145937AA)));
                }
                foreach (long key in expected) Assert.That(actual.Contains(key), Is.True);
                actual.Clear();
                foreach (long key in expected) Assert.That(actual.Contains(key), Is.False);
                expected.Clear();
            }
            void Check(long key)
            {
                Assert.That(actual.Contains(key), Is.EqualTo(expected.Contains(key)));
                Assert.That(actual.Add(key), Is.EqualTo(expected.Add(key)));
                Assert.That(actual.Add(key), Is.False, "duplicates do not claim a second hit");
                Assert.That(actual.Contains(key), Is.True);
            }
        }
        [Test]
        public void PositiveGenerationPagesAndFallbackIdentitiesStayIndependentAcrossWindows()
        {
            var actual = new IdentitySet(16);
            var expected = new HashSet<long>();
            foreach (long first in new[] { 1L, 2047L, long.MaxValue })
            {
                Check(first); Check(0); Check(long.MinValue);
                for (long key = 1; key <= 2300; key++) { Check(key); Check(~key); }
                foreach (long key in expected) Assert.That(actual.Contains(key), Is.True);
                actual.Clear();
                foreach (long key in expected) Assert.That(actual.Contains(key), Is.False);
                expected.Clear();
            }
            void Check(long key)
            {
                Assert.That(actual.Contains(key), Is.EqualTo(expected.Contains(key)));
                Assert.That(actual.Add(key), Is.EqualTo(expected.Add(key)));
                Assert.That(actual.Contains(key), Is.True);
            }
        }
        [Test]
        public void NegativeInitialCapacityIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => new IdentitySet(-1));
    }
}

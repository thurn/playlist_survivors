using NUnit.Framework;
using PS.Choreography;

namespace Demo3D.Choreography.Tests
{
    public class LayerInfoTests
    {
        [Test]
        public void TimelineFormatVersionIsPositive()
        {
            Assert.That(LayerInfo.TimelineFormatVersion, Is.GreaterThan(0));
        }
    }
}

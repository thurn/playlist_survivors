namespace PS.Choreography
{
    /// <summary>
    /// The choreography layer: plain C# (C# 9, .NET Standard 2.1) with no UnityEngine
    /// dependency, shared by the demos as a local Unity package and compiled by `dotnet test`
    /// (demo3d/dotnet/). Each demo keeps its own data and loads it itself.
    /// </summary>
    public static class LayerInfo
    {
        /// <summary>Bumped whenever the timeline's dump format changes.</summary>
        public const int TimelineFormatVersion = 2; // 2: attacks carry clipStart and clipSpeed
    }
}

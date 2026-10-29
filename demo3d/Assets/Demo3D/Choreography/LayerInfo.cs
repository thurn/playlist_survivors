namespace Demo3D.Choreography
{
    /// <summary>
    /// The choreography layer: plain C# (C# 9, .NET Standard 2.1) with no UnityEngine
    /// dependency, compiled both by Unity and by `dotnet test` (demo3d/dotnet/).
    /// </summary>
    public static class LayerInfo
    {
        /// <summary>Bumped whenever the timeline's dump format changes.</summary>
        public const int TimelineFormatVersion = 2; // 2: attacks carry clipStart and clipSpeed
    }
}

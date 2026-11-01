namespace PS.Look
{
    public static partial class LookAxes
    {
        /// <summary>
        /// The world kit's floor and grid (neon-look.md, The Axes &gt; World); the presets name void-grid, hued-zones,
        /// pure-void. Each draws the reactive grid floor (<see cref="GridFloor"/>, ps-wnj.7) in place of the arena's ground.
        /// </summary>
        public static readonly LookAxis World = new LookAxis("world", "--world", GridFloor.VoidGrid, GridFloor.HuedZones, GridFloor.PureVoid);
    }
}

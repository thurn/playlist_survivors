namespace PS.Game.Sim
{
    /// <summary>
    /// The belief stream's groups, as the plugin's <c>ps_entry.kind</c> numbers them
    /// (unity-game.md, "Stream Entries and What the Sim May Know"; the source of truth is native's
    /// <c>native/crates/analysis/src/flat.rs</c>). Each kind's <c>v</c> slots are documented on its
    /// factory in <see cref="StreamEntry"/> and read through its typed view.
    /// </summary>
    public enum EntryKind
    {
        None = 0,
        /// <summary>A committed beat: final, never revised.</summary>
        Beat = 1,
        /// <summary>The analyzer's 50 ms reading: evidence, energy, level, key, drum bands, brightness.</summary>
        Tick = 2,
        /// <summary>A note attack.</summary>
        Onset = 3,
        /// <summary>A hindsight window: its header, followed by one <see cref="HindsightBeat"/> per grid beat.</summary>
        Hindsight = 4,
        /// <summary>One beat of a hindsight window's offline or online grid.</summary>
        HindsightBeat = 5,
        /// <summary>A structure change: the music moves to a new section.</summary>
        Change = 6,
        /// <summary>A sudden loudness increase.</summary>
        Jump = 7,
        /// <summary>A confirmed song end.</summary>
        End = 8,
        /// <summary>An accent forecast: a predicted accented beat.</summary>
        Forecast = 9,
        /// <summary>A spectral colour reading.</summary>
        Timbre = 10,
    }
}

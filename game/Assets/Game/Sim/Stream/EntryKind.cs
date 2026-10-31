namespace PS.Game.Sim
{
    /// <summary>
    /// The belief stream's groups, as the plugin's <c>ps_entry.kind</c> numbers them
    /// (unity-game.md, "Stream Entries and What the Sim May Know"). Each kind's <c>v</c> slots are
    /// documented on its factory in <see cref="StreamEntry"/> and read through its typed view.
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
        /// <summary>A chunk of one hindsight window's beat grid.</summary>
        Hindsight = 4,
        /// <summary>A spectral colour reading.</summary>
        Timbre = 5,
        /// <summary>A structure change: the music moves to a new section.</summary>
        Change = 6,
        /// <summary>A sudden loudness increase.</summary>
        Jump = 7,
        /// <summary>A confirmed song end.</summary>
        End = 8,
        /// <summary>An accent forecast: a predicted accented beat.</summary>
        Forecast = 9,
    }
}

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The development build's command-line flags (qa.md, Manual QA). The demo takes no input,
    /// so these put it into each state manual QA needs.
    /// </summary>
    public sealed class DevFlags
    {
        public bool Overlay;          // --overlay: counts and frame times against the pass lines
        public bool Click;            // --click: a click track on every beat, louder on downbeats
        public double StartAt;        // --start-at <s>
        public float Seconds;         // --seconds <n>: exit after n seconds (0: run to the end)
        public bool FixedTime;        // --fixed-time: 1/60 s frames, sound muted
        public string Scene = "song"; // --scene <S0..S6|song>
        public int N;                 // --n <count> (stress scenes)
        public float ViewWidth;       // --view-width <m>: a stress scene's width, or the song's widest, its zoom arc scaled (0: the script's)
        public string Load = "demo";  // --load <preset>
        public string Aa = "msaa4";   // --aa <mode>
        public double FreezeAt = -1;  // --freeze-at <s>
        public bool ShowLod;          // --show-lod
        public bool Overdraw;         // --overdraw
        public bool ShowNav;          // --show-nav
        public string Crowd = "auto"; // --crowd <stock|bone|auto>
        public string CaptureStills;  // --capture-stills <dir>: fixed-time stills (with --stills-at)
        public string StillsAt;       // --stills-at <s,s,...>
        public string CaptureVideo;   // --capture-video <file.mp4>: fixed-time 1080p60 video
        public double TimeOffset;     // --time-offset <s>: shift song time (TD11's invisible anchor: 0.001)
        public float PoseRate;        // --pose-rate <hz>: round pose times down (TD11's visible anchor: 15)
        public string DumpSounds;     // --dump-sounds <file.json>: the timeline's sound events, for the offline mix
        public bool NoFeel;           // --no-feel: no punch, shake or post feel (A/B captures may use it)
        public bool NoPostFeel;       // --no-post-feel: the post stack at its steady state (the look harness's beauty shots)
        public int Envelope;          // --envelope <count>: override the level script's envelope count
        public string Stats;          // --stats <file.csv>: on-screen counts against targets, twice a second
        public string SongCheck;      // --song-check <file.json>: TD13's full-song verdict (SongRunCheck), uncapped
        public string Arena = "e2";   // --arena <e0|e2|off>: the arena's rung (TD9), or TD4's flat bench ground
        public float Density = 1f;    // --density <0.5|1|2|4>: the arena's decoration density
        public string Resident;       // --resident <env,env,...>: those environments resident beside --environment's (trailer.md, Environments in one player)
        public float SwitchEvery;     // --switch-every <s>: with --resident, the drawn environment cycles through them every s song seconds from --start-at
        public string Trailer;        // --trailer [name]: the trailer file trailer-NAME.json, "action" by default (trailer.md); null: the song
        public int Shot;              // --shot <n>: with --trailer, only shot n, from its cut to its end (0: every shot)
        public bool FromPreroll;      // --from-preroll: with --shot, from the start of its pre-roll
        public Demo3D.Runtime.Arena.ArenaOptions ArenaOptions; // --arena, --density and TD14's --grd, --merged, --tex-cap, --prepass
        // --cuts default|none|all|name,-name (TD11's cuts; Demo3D.Runtime.Cuts reads it), --lod-px, --light-cell, --merge-px

        public static DevFlags FromCommandLine()
        {
            var f = new DevFlags
            {
                Overlay = Args.Has("--overlay"),
                Click = Args.Has("--click"),
                StartAt = Args.GetFloat("--start-at", 0f),
                Seconds = Args.GetFloat("--seconds", 0f),
                FixedTime = Args.Has("--fixed-time"),
                Scene = Args.Get("--scene", "song"),
                N = Args.GetInt("--n", 0),
                ViewWidth = Args.GetFloat("--view-width", 0f),
                Load = Args.Get("--load", "demo"),
                Aa = Args.Get("--aa", "msaa4"),
                FreezeAt = Args.GetFloat("--freeze-at", -1f),
                ShowLod = Args.Has("--show-lod"),
                Overdraw = Args.Has("--overdraw"),
                ShowNav = Args.Has("--show-nav"),
                Crowd = Args.Get("--crowd", "auto"),
                CaptureStills = Args.Get("--capture-stills"),
                StillsAt = Args.Get("--stills-at"),
                CaptureVideo = Args.Get("--capture-video"),
                Stats = Args.Get("--stats"),
                SongCheck = Args.Get("--song-check"),
                Envelope = Args.GetInt("--envelope", 0),
                NoFeel = Args.Has("--no-feel"),
                NoPostFeel = Args.Has("--no-post-feel"),
                DumpSounds = Args.Get("--dump-sounds"),
                Arena = Args.Get("--arena", "e2"),
                Density = Args.GetFloat("--density", 1f),
                TimeOffset = Args.GetFloat("--time-offset", 0f),
                PoseRate = Args.GetFloat("--pose-rate", 0f),
                Resident = Args.Get("--resident"),
                SwitchEvery = Args.GetFloat("--switch-every", 0f),
                Trailer = Args.Has("--trailer") ? TrailerName(Args.Get("--trailer")) : null,
                Shot = Args.GetInt("--shot", 0),
                FromPreroll = Args.Has("--from-preroll"),
            };
            f.ArenaOptions = Demo3D.Runtime.Arena.ArenaOptions.FromCommandLine(Demo3D.Runtime.Arena.ArenaOptions.Final());
            f.Arena = f.ArenaOptions.Rung;
            f.Density = f.ArenaOptions.Density;
            if (f.CaptureStills != null || f.CaptureVideo != null) f.FixedTime = true;
            return f;
        }

        /// <summary>`--trailer`'s optional name: the next argument unless it is the next flag.</summary>
        static string TrailerName(string next) => next == null || next.StartsWith("--", System.StringComparison.Ordinal) ? "action" : next;
    }
}

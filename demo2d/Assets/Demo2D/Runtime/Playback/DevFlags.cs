namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The player's development flags (PX5). The demo takes no input, so these put it into each
    /// state a check needs.
    /// </summary>
    public sealed class DevFlags
    {
        public bool Click;            // --click: a click track on every beat, louder on downbeats
        public double StartAt;        // --start-at <s>
        public float Seconds;         // --seconds <n>: quit after n seconds of song (0: run to the end)
        public bool FixedTime;        // --fixed-time: 1/60 s frames, sound muted
        public double FreezeAt = -1;  // --freeze-at <s>: hold song time there
        public bool Overlay;          // --overlay: song time, section, on-screen counts and frame time
        public string CaptureStills;  // --capture-stills <dir>: fixed-time stills (with --stills-at)
        public string StillsAt;       // --stills-at <s,s,...>
        public string Zoom = PixelPipeline.TargetZoom; // --zoom <target|soft>: how a wider view keeps the grid (PX5's choice)
        public bool NoFeel;           // --no-feel: no punch or shake

        public static DevFlags FromCommandLine()
        {
            var f = new DevFlags
            {
                Click = Args.Has("--click"),
                StartAt = Args.GetFloat("--start-at", 0f),
                Seconds = Args.GetFloat("--seconds", 0f),
                FixedTime = Args.Has("--fixed-time"),
                FreezeAt = Args.GetFloat("--freeze-at", -1f),
                Overlay = Args.Has("--overlay"),
                CaptureStills = Args.Get("--capture-stills"),
                StillsAt = Args.Get("--stills-at"),
                Zoom = Args.Get("--zoom", PixelPipeline.TargetZoom),
                NoFeel = Args.Has("--no-feel"),
            };
            if (f.CaptureStills != null) f.FixedTime = true;
            return f;
        }
    }
}

namespace PS.Look
{
    public static partial class LookAxes
    {
        /// <summary>
        /// The post-processing stack (neon-look.md, The Axes &gt; Post-processing stack): each option a file
        /// Resources/Look/Post/NAME.json that <see cref="PostStack"/> builds.
        /// </summary>
        public static readonly LookAxis Post = new LookAxis("post", "--post", "neon-bloom", "punchy-bloom", "scanlines", "prism-bloom");
    }
}

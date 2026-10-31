using System.IO;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>Files elsewhere in the repository, such as native's fixtures and the plugin's header.</summary>
    public static class Checkout
    {
        /// <summary>A file by its path from the repository's root: under Unity the current directory is the project (game/); under .NET the test binaries' folder.</summary>
        public static string File(params string[] parts)
        {
            string relative = Path.Combine(parts);
            foreach (string start in new[] { Directory.GetCurrentDirectory(), TestContext.CurrentContext.TestDirectory })
                for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                {
                    string candidate = Path.Combine(d.FullName, relative);
                    if (System.IO.File.Exists(candidate)) return candidate;
                }
            throw new FileNotFoundException(relative);
        }
    }
}

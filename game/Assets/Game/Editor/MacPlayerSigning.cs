using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace Game.EditorTools
{
    /// <summary>
    /// The Mac player's permission to tap an app's audio (unity-game.md, Platform Capture and
    /// Crash Safety): after every Mac build it adds the System Audio Recording usage description
    /// to the app's Info.plist and signs the whole bundle again, so macOS asks once and remembers
    /// the player's grant (the bundle ID is the project's, ca.thurn.playlist-survivors.game).
    ///
    /// TCC keys the grant on the signature's designated requirement. An ad hoc signature's is its
    /// code hash, which every build changes, so the player is signed with native's stable
    /// self-signed identity when this Mac has it (`native/scripts/signing-identity` creates it;
    /// `$PS_SIGNING_IDENTITY` names another), and the grant survives rebuilds as native measured
    /// (docs/design/native-app.md). Without one it is signed ad hoc, and asks again after each build.
    /// </summary>
    public sealed class MacPlayerSigning : IPostprocessBuildWithReport
    {
        public const string AudioCaptureUsage =
            "Playlist Survivors plays the music you pick 3 s late, so it knows every beat before you hear it.";
        const string DefaultIdentity = "Playlist Survivors Dev";

        public int callbackOrder => 1000; // after Unity's own steps

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneOSX) return;
            string app = report.summary.outputPath;
            string plist = Path.Combine(app, "Contents", "Info.plist");
            Run("/usr/bin/plutil", "-replace", "NSAudioCaptureUsageDescription", "-string", AudioCaptureUsage, plist);
            string identity = Identity();
            Run("/usr/bin/codesign", "--force", "--deep", "--sign", identity, app);
            Run("/usr/bin/codesign", "--verify", "--deep", "--strict", app);
            Debug.Log($"MacPlayerSigning: {app} has the audio capture usage description, signed by " +
                      (identity == "-" ? "an ad hoc signature (macOS asks for the grant again after each build)" : identity));
        }

        /// <summary>The stable signing identity if the keychain has it, else "-": ad hoc.</summary>
        static string Identity()
        {
            string name = Environment.GetEnvironmentVariable("PS_SIGNING_IDENTITY");
            if (string.IsNullOrEmpty(name)) name = DefaultIdentity;
            return Exec("/usr/bin/security", "find-certificate", "-c", name).code == 0 ? name : "-";
        }

        static void Run(string exe, params string[] args)
        {
            var (code, output) = Exec(exe, args);
            if (code != 0) throw new BuildFailedException($"MacPlayerSigning: {Path.GetFileName(exe)} exited {code}: {output}");
        }

        static (int code, string output) Exec(string exe, params string[] args)
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) info.ArgumentList.Add(a);
            using (var p = Process.Start(info))
            {
                var stderr = p.StandardError.ReadToEndAsync();
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, (stdout + stderr.Result).Trim());
            }
        }
    }
}

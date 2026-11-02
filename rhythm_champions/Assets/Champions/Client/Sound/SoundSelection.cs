using System;
using System.Collections.Generic;
using System.IO;
using Champions.Sim;

namespace Champions.Client.Sound
{
    /// <summary>
    /// The sounds' selection (rhythm_champions/Content/sounds.json): each <see cref="SoundKind"/>'s level and vendor clips
    /// (by file name, synced from manifest.json), and the mix's limit. Read strictly: every category must be there, an
    /// unknown or malformed field fails with its path. The editor's SoundBaker bakes it into the <see cref="SoundBank"/>.
    /// </summary>
    public sealed class SoundSelection
    {
        public const string File = "sounds.json";

        public sealed class Category
        {
            public float Gain;
            public string[] Clips;
        }

        public float Limit;
        /// <summary>By <see cref="SoundKind"/>.</summary>
        public readonly Category[] Categories = new Category[Enum.GetValues(typeof(SoundKind)).Length];

        public static SoundSelection Load(string contentDir)
        {
            string path = Path.Combine(contentDir, File);
            try
            {
                return Read(JsonValue.Parse(System.IO.File.ReadAllText(path)));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                throw new ContentException($"{File}: can't read {path}: {e.Message}", e);
            }
        }

        public static SoundSelection Read(JsonValue json)
        {
            var s = new SoundSelection();
            var r = new ContentReader(json, File);
            r.String("note");
            s.Limit = (float)r.Number("limit");
            var categories = r.Object("categories");
            foreach (SoundKind k in Enum.GetValues(typeof(SoundKind)))
            {
                var c = categories.Object(SoundScheduler.Name(k));
                var clips = c.Strings("clips");
                if (clips.Length == 0) throw ContentReader.Fail(c.Child("clips"), "at least one clip");
                s.Categories[(int)k] = new Category { Gain = (float)c.Number("gain"), Clips = clips };
                c.End();
            }
            categories.End();
            r.End();
            return s;
        }

        /// <summary>Every clip name the selection uses, once.</summary>
        public IEnumerable<string> ClipNames()
        {
            var seen = new HashSet<string>();
            foreach (var c in Categories)
                foreach (var name in c.Clips)
                    if (seen.Add(name)) yield return name;
        }
    }
}

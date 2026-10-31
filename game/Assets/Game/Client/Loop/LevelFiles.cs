using System;
using System.Collections.Generic;
using System.IO;
using PS.Game.Sim.Content;
using PS.Game.Sim.Level;

namespace Game.Client.Loop
{
    /// <summary>
    /// The level the sim plays: the build's <see cref="LevelBake"/>, or with `--level PATH` (unity-game.md, Debug
    /// Surfaces) files that take the place of its copies. PATH is a folder holding any of the level's files (the
    /// content files, <see cref="GameContent.Files"/>, and arena.json), such as game/Content itself, or one of
    /// those files by name; the rest come from the bake. So an edited schedule.json plays without a rebake. The
    /// arena's files change the sim's tiles only: the arena drawn is the build's own.
    /// </summary>
    public sealed class LevelFiles
    {
        readonly LevelBake _bake;
        readonly Dictionary<string, string> _files = new Dictionary<string, string>();

        LevelFiles(LevelBake bake) => _bake = bake;

        /// <summary>The files given in place of the bake's, by name.</summary>
        public ICollection<string> Replaced => _files.Keys;

        /// <summary>
        /// The bake's level with the files at the absolute `path` in place of its own (null: the bake alone), or null with
        /// neither. A path that holds none of the level's files fails.
        /// </summary>
        public static LevelFiles Load(string path)
        {
            var bake = LevelBake.Load();
            if (path == null) return bake != null ? new LevelFiles(bake) : null;
            var level = new LevelFiles(bake);
            var names = new List<string>(GameContent.Files) { LevelBake.ArenaFile };
            if (Directory.Exists(path))
            {
                foreach (var name in names)
                    if (File.Exists(System.IO.Path.Combine(path, name))) level._files[name] = File.ReadAllText(System.IO.Path.Combine(path, name));
            }
            else if (File.Exists(path) && names.Contains(System.IO.Path.GetFileName(path)))
                level._files[System.IO.Path.GetFileName(path)] = File.ReadAllText(path);
            if (level._files.Count == 0)
                throw new ArgumentException($"--level: {path} is neither a level file ({string.Join(", ", names)}) nor a folder holding one");
            return level;
        }

        public string Text(string name)
        {
            if (_files.TryGetValue(name, out var text)) return text;
            return _bake != null ? _bake.Text(name) : throw new ArgumentException($"--level gave no {name}, and this build has no level bake");
        }

        public GameContent Content() => GameContent.Parse(Text);

        public ArenaData Arena() => ArenaData.Parse(Text(LevelBake.ArenaFile), LevelBake.ArenaFile);
    }
}

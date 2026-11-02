using System;
using System.Collections.Generic;
using System.IO;
using Champions.Sim;

namespace Champions.Client.UI
{
    /// <summary>One card as drawn: its kind (its outline's shape), icon, name, line, and the rank it gives (4: an evolution, drawn larger).</summary>
    public readonly struct CardFace
    {
        public readonly CardKind Kind;
        public readonly string Id, Name, Line;
        public readonly Icon Icon;
        public readonly int Rank;

        public CardFace(CardKind kind, string id, string name, string line, Icon icon, int rank)
        {
            Kind = kind;
            Id = id;
            Name = name;
            Line = line;
            Icon = icon;
            Rank = rank;
        }

        public bool Evolution => Rank == Upgrade.Evolution;
    }

    /// <summary>
    /// The cards' faces from Content/cards.json and their icons (Content/icons/): every dash, attack and mod the content
    /// holds has a name of at most <see cref="NameWords"/> words, an icon, a line of at most <see cref="LineWords"/> words for
    /// each rank it gives, and, for an attack or a damaging dash, its evolution's name and line.
    /// </summary>
    public sealed class CardFaces
    {
        public const string File = "cards.json";
        public const int NameWords = 3, LineWords = 6;

        sealed class Entry
        {
            public string Name, Evolution, EvolutionLine;
            public string[] Lines;
            public Icon Icon;
        }

        readonly Dictionary<string, Entry>[] _entries = { new Dictionary<string, Entry>(), new Dictionary<string, Entry>(), new Dictionary<string, Entry>() };
        readonly string[] _dashIds;

        CardFaces(string[] dashIds)
        {
            _dashIds = dashIds;
        }

        /// <summary>Every icon, by its file's name.</summary>
        public readonly Dictionary<string, Icon> Icons = new Dictionary<string, Icon>();

        /// <summary>Loads the faces for <paramref name="content"/>'s dashes, attacks and mods from <paramref name="contentDir"/>.</summary>
        public static CardFaces Load(string contentDir, GameContent content)
        {
            string path = Path.Combine(contentDir, File);
            JsonValue json;
            try { json = JsonValue.Parse(System.IO.File.ReadAllText(path)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                throw new ContentException($"{File}: can't read {path}: {e.Message}", e);
            }
            var dashIds = Array.ConvertAll(content.Dashes.Kinds, k => k.Id);
            var faces = new CardFaces(dashIds);
            var r = new ContentReader(json, File);
            r.String("note");
            var attackIds = new string[Upgrade.AttackCount];
            for (int i = 0; i < attackIds.Length; i++) attackIds[i] = Upgrade.IdOf((AttackKind)i);
            var modIds = new string[Upgrade.ModCount];
            for (int i = 0; i < modIds.Length; i++) modIds[i] = Upgrade.IdOf((ModKind)i);
            var dashes = content.Dashes.Kinds;
            faces.Read(r.Object("dashes"), CardKind.Dash, dashIds, contentDir, id => dashes[Array.IndexOf(dashIds, id)].Ranks.Length,
                id => dashes[Array.IndexOf(dashIds, id)].Evolution != null);
            faces.Read(r.Object("attacks"), CardKind.Attack, attackIds, contentDir, _ => Upgrade.AttackTop, _ => true);
            faces.Read(r.Object("mods"), CardKind.Mod, modIds, contentDir, _ => Upgrade.ModTop, _ => false);
            r.End();
            return faces;
        }

        void Read(ContentReader group, CardKind kind, string[] ids, string contentDir, Func<string, int> top, Func<string, bool> evolves)
        {
            foreach (string id in ids)
            {
                var c = group.Object(id);
                var e = new Entry { Name = c.String("name"), Lines = c.Strings("lines") };
                string icon = c.String("icon");
                if (!Icons.TryGetValue(icon, out e.Icon)) Icons[icon] = e.Icon = Icon.Load(contentDir, icon);
                if (e.Lines.Length != top(id)) throw c.Fail($"lines: {e.Lines.Length} for ranks 1-{top(id)}");
                if (evolves(id))
                {
                    var evo = c.Object("evolution");
                    e.Evolution = evo.String("name");
                    e.EvolutionLine = evo.String("line");
                    evo.End();
                }
                c.End();
                Check(c, "name", e.Name, NameWords);
                Check(c, "evolution.name", e.Evolution, NameWords);
                foreach (string line in e.Lines) Check(c, "lines", line, LineWords);
                Check(c, "evolution.line", e.EvolutionLine, LineWords);
                _entries[(int)kind][id] = e;
            }
            group.End();
        }

        static void Check(ContentReader c, string key, string text, int words)
        {
            if (text != null && Words(text) > words) throw c.Fail($"{key}: \"{text}\" has {Words(text)} words, more than {words}");
        }

        /// <summary>The words in <paramref name="text"/>: its space-separated runs holding a letter (numbers, "+40%" and marks are not words).</summary>
        public static int Words(string text)
        {
            int n = 0;
            foreach (string run in text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                foreach (char ch in run)
                    if (char.IsLetter(ch))
                    {
                        n++;
                        break;
                    }
            return n;
        }

        /// <summary>The face of the sim's <paramref name="card"/> (an <see cref="SimEventKind.Offer"/>'s, by <see cref="Card.FromCode"/>).</summary>
        public CardFace Of(in Card card) => Of(card.Kind, card.Kind switch
        {
            CardKind.Dash => _dashIds[card.Id],
            CardKind.Attack => Upgrade.IdOf(card.Attack),
            _ => Upgrade.IdOf(card.Mod),
        }, card.Rank);

        /// <summary>The face of the card giving the content id <paramref name="id"/> at <paramref name="rank"/>.</summary>
        public CardFace Of(CardKind kind, string id, int rank)
        {
            if (!_entries[(int)kind].TryGetValue(id, out var e)) throw new ArgumentException($"no {kind} card {id}");
            bool evolution = rank == Upgrade.Evolution && e.Evolution != null;
            if (!evolution && (rank < 1 || rank > e.Lines.Length)) throw new ArgumentException($"{id} has no rank {rank}");
            return new CardFace(kind, id, evolution ? e.Evolution : e.Name, evolution ? e.EvolutionLine : e.Lines[rank - 1], e.Icon, rank);
        }

        /// <summary>Every face the content can offer, each rank of each dash, attack and mod and each evolution.</summary>
        public IEnumerable<CardFace> All()
        {
            for (int o = 0; o < _entries.Length; o++)
                foreach (var kv in _entries[o])
                {
                    for (int rank = 1; rank <= kv.Value.Lines.Length; rank++) yield return Of((CardKind)o, kv.Key, rank);
                    if (kv.Value.Evolution != null) yield return Of((CardKind)o, kv.Key, Upgrade.Evolution);
                }
        }
    }
}

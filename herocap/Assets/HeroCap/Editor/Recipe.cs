using System;
using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using HeroEditor.Common.Data;

namespace HeroCap.EditorTools
{
    /// <summary>
    /// One character recipe, herocap/recipes/&lt;atlas&gt;.json: the HeroEditor parts by their
    /// sprite-collection ids (an id may end `#RRGGBBAA`, a tint), the weapon type, one face for
    /// every expression, an overall scale and the clips to capture. The tools check the face
    /// (herocap.py, FACE_PARTS) before every capture.
    /// </summary>
    [Serializable]
    public class Recipe
    {
        public string atlas;
        public string role;
        public float scale = 1;
        public string weapon = "Melee1H";
        public string skin;  // a tint for the head, body and ears, "#RRGGBB" or "#RRGGBBAA"
        public Parts parts = new Parts();
        public Face face = new Face();
        public float[] bodyScale;
        public string[] clips;

        [Serializable]
        public class Parts
        {
            public string head = "Common.Basic.Head.Human", body = "Common.Basic.Body.Human",
                ears = "Common.Basic.Ears.HumanEars", hair, beard, helmet, glasses, mask, cape, back,
                shield, armor, primary, secondary;
        }

        [Serializable]
        public class Face
        {
            public string eyes, eyesColor = "#FFFFFFFF", mouth, eyebrows;
        }

        // Character.FromJson's expressions: every one gets the recipe's face, so an expression event
        // in a clip can't swap in another (the capture also turns the Animator's events off).
        static readonly string[] ExpressionNames = { "Default", "Angry", "Dead" };

        /// <summary>
        /// The recipe as HeroEditor's CharacterSerializer description, for Character.FromJson.
        /// </summary>
        public string ToCharacterJson(Character character)
        {
            var tint = string.IsNullOrEmpty(skin) ? "" : Tint(skin);
            var helmet = Find(character.SpriteCollection.Helmet, parts.helmet);
            var hair = Find(character.SpriteCollection.Hair, parts.hair);
            var d = new Dictionary<string, string>
            {
                ["Head"] = Tinted(parts.head, tint),
                ["Body"] = Tinted(parts.body, tint),
                ["Ears"] = Tinted(parts.ears, tint),
                ["Hair"] = parts.hair,
                ["Beard"] = parts.beard,
                ["Helmet"] = parts.helmet,
                ["Glasses"] = parts.glasses,
                ["Mask"] = parts.mask,
                ["Cape"] = parts.cape,
                ["Back"] = parts.back,
                ["Shield"] = parts.shield,
                ["WeaponType"] = weapon,
                ["PrimaryMeleeWeapon"] = parts.primary,
                ["SecondaryMeleeWeapon"] = parts.secondary,
                ["Expression"] = "Default",
                ["HideEars"] = (hair != null && Tagged(hair, "HideEars")).ToString(),
                ["FullHair"] = (helmet != null && Tagged(helmet, "FullHair")).ToString(),
                ["BodyScaleX"] = (bodyScale?.Length == 2 ? bodyScale[0] : 1f).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["BodyScaleY"] = (bodyScale?.Length == 2 ? bodyScale[1] : 1f).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            for (var i = 0; i < character.ArmorRenderers.Count; i++)
                d[$"Armor[{i}]"] = parts.armor;
            foreach (var e in ExpressionNames)
            {
                d[$"Expression.{e}.Eyebrows"] = face.eyebrows;
                d[$"Expression.{e}.Eyes"] = face.eyes;
                d[$"Expression.{e}.EyesColor"] = face.eyesColor;
                d[$"Expression.{e}.Mouth"] = face.mouth;
            }
            // HeroEditor's own format (its internal Serializer): a flat object of strings.
            return "{" + string.Join(",", d.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"")) + "}";
        }

        static string Tint(string hex) => (hex.StartsWith("#") ? hex.Substring(1) : hex).PadRight(8, 'F');

        static string Tinted(string id, string tint) =>
            string.IsNullOrEmpty(id) || string.IsNullOrEmpty(tint) || id.Contains("#") ? id : id + "#" + tint;

        static ItemSprite Find(IEnumerable<ItemSprite> collection, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var bare = id.Split('#')[0];
            return collection.FirstOrDefault(i => i.Id == bare)
                   ?? throw new Exception($"Recipe: no sprite-collection entry {bare}");
        }

        static bool Tagged(ItemSprite item, string tag) => item.Tags.Contains(tag) || item.Id.Contains($"[{tag}]");
    }

    /// <summary>herocap/clips.json: the capture's rate and scale and every clip a recipe may name.</summary>
    [Serializable]
    public class ClipTable
    {
        public float fps = 30;
        public float targetHeightPx = 96;  // the hero's height, idle, head to feet
        public int supersample = 2;
        public Clip[] clips;

        /// <summary>
        /// A clip: an upper-body state (Human.controller's Upper layer) and a lower-body one (its
        /// Lower layer, which overrides it), sampled at the same normalized time over the length of
        /// the `lead` one. `impact` (normalized) annotates a strike by hand; otherwise it is the lead
        /// clip's `Hit` event, if any.
        /// </summary>
        [Serializable]
        public class Clip
        {
            public string name, upper, lower, lead = "upper";
            public bool loop;
            public float impact = -1;
        }
    }
}

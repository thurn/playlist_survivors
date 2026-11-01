using System.Linq;
using Game.Client.Crowd;
using Game.EditorTools.Crowd;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// The elites' weapons are held (unity-game.md, The crowd, Weapons): the handle runs through the
    /// fist, square to the palm, with the weapon's pivot in the hand, not sticking out of the palm as
    /// a Synty weapon on the bare hand bone did. Spawns the vendor prefabs, as the crowd bake does.
    /// </summary>
    public class GripTests
    {
        [TestCase("bigork")]
        [TestCase("troll")]
        public void AGrippedWeaponSitsInTheFist(string id)
        {
            var t = Roster.Load().Single(x => x.id == id);
            Assert.That(t.weapons.Single().grip, Is.Not.Null, $"{id}'s weapon has a grip");
            var go = Roster.Spawn(t);
            try
            {
                var animator = go.GetComponentInChildren<Animator>();
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var fist = Fist.Of(animator, right: true);
                var weapon = hand.GetComponentInChildren<MeshFilter>().transform;
                var up = hand.InverseTransformDirection(weapon.up).normalized;
                Assert.That(Mathf.Abs(Vector3.Dot(up, fist.Palm)), Is.LessThan(0.05f), "the handle lies across the fist, square to the palm");
                Assert.That(Vector3.Dot(up, fist.Axis), Is.GreaterThan(0.7f), "the weapon leaves the fist on the thumb's side");
                float metres = Vector3.Distance(weapon.position, hand.TransformPoint(fist.Knuckles)) / go.transform.lossyScale.x;
                Assert.That(metres, Is.LessThan(0.06f), "the pivot (the grip) is at the knuckles, at the prefab's own scale");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Hands out every prefab once before repeating any of them. Drawing independently at
    /// random clusters duplicates, and the same building twice in a row along one terrace
    /// is the most obvious tell of a generated city.
    ///
    /// Shared by BlockBuilder (terrace pieces) and VehiclePicker - the vehicle groups are
    /// small enough that independent draws would put two identical cars nose to tail.
    /// </summary>
    public sealed class ShuffleBag
    {
        readonly GameObject[] items;
        readonly System.Random rng;
        int cursor;

        public ShuffleBag(GameObject[] source, System.Random rng)
        {
            this.rng = rng;
            items = source == null ? System.Array.Empty<GameObject>() : (GameObject[])source.Clone();
            Shuffle();
        }

        public int Count => items.Length;

        public GameObject Peek() => items.Length == 0 ? null : items[cursor % items.Length];

        public void Advance()
        {
            cursor++;
            if (items.Length > 0 && cursor % items.Length == 0)
                Shuffle();
        }

        void Shuffle()
        {
            for (var i = items.Length - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}

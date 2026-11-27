using System.Collections.Generic;
using UnityEngine;
using LifeAfter;

namespace WildCardStudio
{
    /// Holds the opponent's original deck list without consuming or reordering its entries.
    public sealed class EnemyDeckManager : MonoBehaviour
    {
        [SerializeField, Tooltip("The original cards available to the enemy AI, in draw order.")]
        private List<Card> initialDeck = new List<Card>();

        /// Cards in their original deck order.
        public IReadOnlyList<Card> InitialDeck => initialDeck;
    }
}

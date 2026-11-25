using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Code used from Sinous: https://youtu.be/j36rIYTvLkI?si=ChnzjeBP1k1vf6VF

namespace LifeAfter
{
    /// Data asset that defines a card's visual, rank, suit, type, and description.
    [CreateAssetMenu(fileName = "New Card", menuName = "Card")]
    public class Card : ScriptableObject
    {
        /// Display name of the card.
        public string cardName;
        /// Numeric rank used by matching and scoring rules.
        public int cardRank;
        /// Suits assigned to this card.
        public List<WildCardStudio.Suit> suit;
        /// Card types assigned to this card, including special types.
        public List<WildCardStudio.CardType> cardType;
        /// Human-readable description of the card.
        public string cardDescription;
        /// Sprite shown in the card's UI representation.
        public Sprite cardImage;
    }
}

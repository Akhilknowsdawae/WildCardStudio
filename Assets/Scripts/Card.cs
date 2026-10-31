using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LifeAfter
{
    [CreateAssetMenu(fileName = "New Card", menuName = "Card")]
    public class Card : ScriptableObject
    {
        public string cardName;
        public int cardRank;
        public List<Suit> suit;
        public List<CardType> cardType;
        public string cardDescription;
        public Sprite cardImage;

        public enum Suit
        {
            Skulls,
            Candles,
            Bones,
            Bells
        }

        public enum CardType
        {
            Numbered,
            Face
        }
    }
}

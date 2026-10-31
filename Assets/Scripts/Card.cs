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
        public List<WildCardStudio.Suit> suit;
        public List<WildCardStudio.CardType> cardType;
        public string cardDescription;
        public Sprite cardImage;
    }
}

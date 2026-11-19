using System.Collections.Generic;
using UnityEngine;

namespace WildCardStudio
{
    /// Calculates drawing probabilities for single cards and groups of desired cards.
    public sealed class DeckProbabilityCalculator
    {
        /// Returns the probability of drawing at least one card from the desired collection.
        public float ProbabilityOfDrawingAny(
            IReadOnlyCollection<CardInstance> desiredCards,
            IReadOnlyCollection<CardInstance> unseenCards,
            int draws)
        {
            if (desiredCards == null || desiredCards.Count == 0
                || unseenCards == null || unseenCards.Count == 0 || draws <= 0)
            {
                return 0f;
            }

            HashSet<CardInstance> desired = new HashSet<CardInstance>(desiredCards);
            int unseenCount = 0;
            int matchingCount = 0;
            foreach (CardInstance card in unseenCards)
            {
                if (card == null)
                {
                    continue;
                }

                unseenCount++;
                if (desired.Contains(card))
                {
                    matchingCount++;
                }
            }

            if (matchingCount == 0 || unseenCount == 0)
            {
                return 0f;
            }

            int drawCount = Mathf.Min(draws, unseenCount);
            int nonMatchingCount = unseenCount - matchingCount;
            if (drawCount > nonMatchingCount)
            {
                return 1f;
            }

            // Multiply the probability of missing on every draw to obtain the complement.
            float probabilityOfNoMatch = 1f;
            for (int i = 0; i < drawCount; i++)
            {
                probabilityOfNoMatch *= (float)(nonMatchingCount - i) / (unseenCount - i);
            }

            return 1f - probabilityOfNoMatch;
        }

        /// Returns the probability of drawing the supplied specific card.
        public float ProbabilityOfDrawingSpecificCard(
            CardInstance desiredCard,
            IReadOnlyCollection<CardInstance> unseenCards,
            int draws)
        {
            if (desiredCard == null)
            {
                return 0f;
            }

            return ProbabilityOfDrawingAny(
                new[] { desiredCard },
                unseenCards,
                draws);
        }
    }
}

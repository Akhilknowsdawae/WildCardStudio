using System;
using System.Collections.Generic;
using System.Linq;

namespace WildCardStudio
{
    /// Contains a valid combo found in a player's hand.
    public sealed class HandCombo
    {
        /// The kind of combo contained in this selection.
        public ComboType Type { get; }
        /// The exact card instances that form the combo.
        public IReadOnlyList<CardInstance> Cards { get; }

        internal HandCombo(ComboType type, IReadOnlyList<CardInstance> cards)
        {
            Type = type;
            Cards = cards;
        }
    }

    public sealed class HandAnalyzer
    {
        private readonly IComboValidator validator;

        /// Creates an analyzer that uses the supplied combo validation rules.
        public HandAnalyzer(IComboValidator validator)
        {
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        /// Enumerates every valid set and straight that can be formed from the hand.
        public IReadOnlyList<HandCombo> FindCombos(IReadOnlyList<CardInstance> hand)
        {
            // Copy the hand before exploring combinations so the caller's collection is never mutated.
            List<CardInstance> cards = hand == null
                ? new List<CardInstance>()
                : new List<CardInstance>(hand);
            List<HandCombo> combos = new List<HandCombo>();
            if (cards.Count >= 2)
            {
                CollectCombos(cards, 0, new List<CardInstance>(), cards.Count, combos);
            }
            return combos;
        }

        private void CollectCombos(
            IReadOnlyList<CardInstance> hand,
            int startIndex,
            List<CardInstance> selection,
            int maxComboSize,
            List<HandCombo> combos)
        {
            // Validate each partial selection once it has at least two cards.
            if (selection.Count >= 2)
            {
                AddComboIfValid(selection, ComboType.Set, combos);
                AddComboIfValid(selection, ComboType.Straight, combos);
            }

            if (selection.Count >= maxComboSize)
            {
                return;
            }

            for (int i = startIndex; i < hand.Count; i++)
            {
                if (hand[i] == null)
                {
                    continue;
                }

                // Use backtracking to enumerate every non-repeating subset of the hand.
                selection.Add(hand[i]);
                CollectCombos(hand, i + 1, selection, maxComboSize, combos);
                selection.RemoveAt(selection.Count - 1);
            }
        }

        private void AddComboIfValid(
            IReadOnlyList<CardInstance> cards,
            ComboType type,
            List<HandCombo> combos)
        {
            if (validator.CanCreateCombo(cards, type, null).IsValid)
            {
                combos.Add(new HandCombo(type, cards.ToArray()));
            }
        }
    }
}

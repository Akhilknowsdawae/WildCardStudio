using System;
using System.Collections.Generic;
using System.Linq;

namespace WildCardStudio
{
    public sealed class HandCombo
    {
        public ComboType Type { get; }
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

        public HandAnalyzer(IComboValidator validator)
        {
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public IReadOnlyList<HandCombo> FindCombos(IReadOnlyList<CardInstance> hand)
        {
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

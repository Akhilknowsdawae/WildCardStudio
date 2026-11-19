using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WildCardStudio
{
    /// Estimates the cost of discarding each card in the enemy hand.
    public sealed class DiscardEvaluator
    {
        private readonly EnemyAISettings settings;
        private readonly ComboEvaluator comboEvaluator;

        public DiscardEvaluator(EnemyAISettings settings, ComboEvaluator comboEvaluator)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.comboEvaluator = comboEvaluator ?? throw new ArgumentNullException(nameof(comboEvaluator));
        }

        /// Builds one negative-value discard candidate for each card in the hand.
        public IReadOnlyList<EnemyAIAction> Evaluate(
            IReadOnlyList<CardInstance> hand,
            IReadOnlyList<CardInstance> unseenCards,
            EnemyAIHandAnalysis currentHand)
        {
            List<EnemyAIAction> actions = new List<EnemyAIAction>();
            // A discard decision requires both a valid hand and baseline hand analysis.
            if (hand == null || currentHand == null || comboEvaluator == null)
            {
                return actions;
            }

            foreach (CardInstance card in hand)
            {
                if (card == null)
                {
                    continue;
                }

                // Measure the hand's lost immediate and future points after removing this card.
                List<CardInstance> remainingHand = hand.Where(handCard => handCard != card).ToList();
                EnemyAIHandAnalysis afterDiscard = comboEvaluator.Analyze(remainingHand, unseenCards);
                float lostImmediatePoints = Mathf.Max(
                    0f,
                    currentHand.BestImmediatePoints - afterDiscard.BestImmediatePoints);
                float lostFuturePoints = Mathf.Max(
                    0f,
                    currentHand.ExpectedFuturePoints - afterDiscard.ExpectedFuturePoints);
                float cardRetentionValue = Mathf.Max(0, card.EffectiveValue)
                    * Mathf.Max(0f, settings.cardRetentionWeight);
                float utility = lostImmediatePoints * Mathf.Max(0f, settings.immediatePointsWeight)
                    + lostFuturePoints * Mathf.Max(0f, settings.opportunityCostWeight)
                    + cardRetentionValue;

                actions.Add(new EnemyAIAction(
                    EnemyAIActionType.DiscardCard,
                    cards: new[] { card },
                    expectedFuturePoints: afterDiscard.ExpectedFuturePoints,
                    opportunityCost: utility,
                    value: -utility));
            }

            return actions;
        }
    }
}

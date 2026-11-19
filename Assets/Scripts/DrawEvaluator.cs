using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WildCardStudio
{
    /// Estimates the value of drawing from the deck or taking the top discard.
    public sealed class DrawEvaluator
    {
        private readonly EnemyAISettings settings;
        private readonly ComboEvaluator comboEvaluator;

        public DrawEvaluator(EnemyAISettings settings, ComboEvaluator comboEvaluator)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.comboEvaluator = comboEvaluator ?? throw new ArgumentNullException(nameof(comboEvaluator));
        }

        /// Builds deck-draw and discard-pickup candidates for the supplied hand.
        public IReadOnlyList<EnemyAIAction> Evaluate(
            IReadOnlyList<CardInstance> hand,
            IReadOnlyList<CardInstance> unseenCards,
            CardInstance discardTop,
            EnemyAIHandAnalysis currentHand)
        {
            List<EnemyAIAction> actions = new List<EnemyAIAction>();
            // Drawing is only evaluable when the hand and current analysis exist.
            if (hand == null || currentHand == null || comboEvaluator == null)
            {
                return actions;
            }

            List<CardInstance> drawPool = GetUnseenCards(hand, unseenCards);
            if (drawPool.Count > 0)
            {
                // Average the projected hand improvements across every possible deck draw.
                float expectedImmediatePoints = 0f;
                float expectedFuturePoints = 0f;
                foreach (CardInstance drawCard in drawPool)
                {
                    List<CardInstance> nextHand = new List<CardInstance>(hand) { drawCard };
                    List<CardInstance> remainingDeck = drawPool
                        .Where(card => card != drawCard)
                        .ToList();
                    EnemyAIHandAnalysis afterDraw = comboEvaluator.Analyze(nextHand, remainingDeck);
                    expectedImmediatePoints += Mathf.Max(
                        0f,
                        afterDraw.BestImmediatePoints - currentHand.BestImmediatePoints);
                    expectedFuturePoints += Mathf.Max(
                        0f,
                        afterDraw.ExpectedFuturePoints - currentHand.ExpectedFuturePoints);
                }

                expectedImmediatePoints /= drawPool.Count;
                expectedFuturePoints /= drawPool.Count;
                float expectedValue = expectedImmediatePoints * Mathf.Max(0f, settings.immediatePointsWeight)
                    + expectedFuturePoints * Mathf.Max(0f, settings.futurePointsWeight)
                    - Mathf.Max(0f, settings.drawActionCost);
                actions.Add(new EnemyAIAction(
                    EnemyAIActionType.DrawFromDeck,
                    immediatePoints: expectedImmediatePoints,
                    expectedFuturePoints: expectedFuturePoints,
                    value: expectedValue));
            }

            if (discardTop != null)
            {
                // Evaluate pickup separately because the discard card is known, not probabilistic.
                List<CardInstance> nextHand = new List<CardInstance>(hand) { discardTop };
                EnemyAIHandAnalysis afterPickup = comboEvaluator.Analyze(nextHand, drawPool);
                float immediateGain = Mathf.Max(
                    0f,
                    afterPickup.BestImmediatePoints - currentHand.BestImmediatePoints);
                float futureGain = Mathf.Max(
                    0f,
                    afterPickup.ExpectedFuturePoints - currentHand.ExpectedFuturePoints);
                float pickupBonus = immediateGain + futureGain > 0f
                    ? Mathf.Max(0f, settings.discardPickupPriority)
                    : 0f;
                float pickupValue = immediateGain * Mathf.Max(0f, settings.immediatePointsWeight)
                    + futureGain * Mathf.Max(0f, settings.futurePointsWeight)
                    + pickupBonus
                    - Mathf.Max(0f, settings.drawActionCost);
                actions.Add(new EnemyAIAction(
                    EnemyAIActionType.DrawFromDiscard,
                    immediatePoints: immediateGain,
                    expectedFuturePoints: futureGain,
                    value: pickupValue));
            }

            return actions;
        }

        private static List<CardInstance> GetUnseenCards(
            IReadOnlyList<CardInstance> hand,
            IReadOnlyList<CardInstance> unseenCards)
        {
            HashSet<string> handIds = new HashSet<string>(
                hand.Where(card => card != null && !string.IsNullOrEmpty(card.InstanceId))
                    .Select(card => card.InstanceId));
            return unseenCards == null
                ? new List<CardInstance>()
                : unseenCards
                    .Where(card => card != null
                        && (string.IsNullOrEmpty(card.InstanceId) || !handIds.Contains(card.InstanceId)))
                    .ToList();
        }
    }
}

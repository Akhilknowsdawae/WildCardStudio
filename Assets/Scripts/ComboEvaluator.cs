using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WildCardStudio
{
    public sealed class EnemyAIHandAnalysis
    {
        public IReadOnlyList<EnemyAIAction> AvailableCombos { get; }
        public float BestImmediatePoints { get; }
        public float ExpectedFuturePoints { get; }

        internal EnemyAIHandAnalysis(
            IReadOnlyList<EnemyAIAction> availableCombos,
            float bestImmediatePoints,
            float expectedFuturePoints)
        {
            AvailableCombos = availableCombos;
            BestImmediatePoints = bestImmediatePoints;
            ExpectedFuturePoints = expectedFuturePoints;
        }
    }

    public sealed class ComboEvaluator
    {
        private readonly HandAnalyzer handAnalyzer;
        private readonly IComboValidator validator;
        private readonly DeckProbabilityCalculator probabilityCalculator;
        private readonly EnemyAISettings settings;

        public ComboEvaluator(
            HandAnalyzer handAnalyzer,
            IComboValidator validator,
            DeckProbabilityCalculator probabilityCalculator,
            EnemyAISettings settings)
        {
            this.handAnalyzer = handAnalyzer ?? throw new ArgumentNullException(nameof(handAnalyzer));
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
            this.probabilityCalculator = probabilityCalculator
                ?? throw new ArgumentNullException(nameof(probabilityCalculator));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public EnemyAIHandAnalysis Analyze(
            IReadOnlyList<CardInstance> hand,
            IReadOnlyList<CardInstance> unseenCards)
        {
            List<CardInstance> cards = hand == null
                ? new List<CardInstance>()
                : hand.Where(card => card != null).ToList();
            List<CardInstance> drawPool = GetUnseenCards(cards, unseenCards);
            IReadOnlyList<HandCombo> handCombos = handAnalyzer.FindCombos(cards);
            List<EnemyAIAction> combos = new List<EnemyAIAction>(handCombos.Count);
            float bestImmediatePoints = 0f;
            foreach (HandCombo combo in handCombos)
            {
                float points = CalculateComboScore(combo.Cards, combo.Type);
                bestImmediatePoints = Mathf.Max(bestImmediatePoints, points);
                combos.Add(new EnemyAIAction(
                    EnemyAIActionType.PlayCombo,
                    combo.Type,
                    combo.Cards,
                    immediatePoints: points));
            }

            float futurePoints = drawPool.Count == 0
                ? 0f
                : FindBestFutureOpportunity(cards, drawPool);
            return new EnemyAIHandAnalysis(combos, bestImmediatePoints, futurePoints);
        }

        public IReadOnlyList<EnemyAIAction> EvaluatePlayActions(
            EnemyAIHandAnalysis currentHand,
            IReadOnlyList<CardInstance> hand,
            IReadOnlyList<CardInstance> unseenCards)
        {
            List<EnemyAIAction> actions = new List<EnemyAIAction>();
            if (currentHand == null || hand == null)
            {
                return actions;
            }

            foreach (EnemyAIAction combo in currentHand.AvailableCombos)
            {
                HashSet<CardInstance> committedCards = new HashSet<CardInstance>(combo.Cards);
                List<CardInstance> remainingHand = hand
                    .Where(card => card != null && !committedCards.Contains(card))
                    .ToList();
                float futureAfterPlay = Analyze(remainingHand, unseenCards).ExpectedFuturePoints;
                float lostFutureValue = Mathf.Max(0f, currentHand.ExpectedFuturePoints - futureAfterPlay);
                float retainedCardValue = combo.Cards.Sum(card => Mathf.Max(0, card.EffectiveValue))
                    * Mathf.Max(0f, settings.cardRetentionWeight);
                float opportunityCost = (lostFutureValue + retainedCardValue)
                    * Mathf.Max(0f, settings.opportunityCostWeight);
                float value = combo.ImmediatePoints * Mathf.Max(0f, settings.immediatePointsWeight)
                    + futureAfterPlay * Mathf.Max(0f, settings.futurePointsWeight)
                    - opportunityCost;

                actions.Add(new EnemyAIAction(
                    EnemyAIActionType.PlayCombo,
                    combo.ComboType,
                    combo.Cards,
                    combo.ImmediatePoints,
                    futureAfterPlay,
                    opportunityCost,
                    value));
            }

            return actions;
        }

        public float CalculateComboScore(IReadOnlyList<CardInstance> cards, ComboType type)
        {
            ComboValidationResult validation = validator.CanCreateCombo(cards, type, null);
            if (!validation.IsValid)
            {
                return 0f;
            }

            float score = cards.Sum(card => validation.ScoringContext.GetCardScore(card, cards));
            if (type == ComboType.Straight)
            {
                score *= 2f;
            }

            if (cards.Count > 1 && cards.All(card => card.Suit == cards[0].Suit))
            {
                score *= 1.5f;
            }

            return score * Mathf.Max(0f, settings.comboScoreMultiplier);
        }

        private float FindBestFutureOpportunity(
            IReadOnlyList<CardInstance> hand,
            IReadOnlyList<CardInstance> drawPool)
        {
            float bestValue = 0f;
            int maxHeldCards = hand.Count;
            FindFutureOpportunities(
                hand,
                drawPool,
                0,
                new List<CardInstance>(),
                maxHeldCards,
                ref bestValue);
            return bestValue;
        }

        private void FindFutureOpportunities(
            IReadOnlyList<CardInstance> hand,
            IReadOnlyList<CardInstance> drawPool,
            int startIndex,
            List<CardInstance> selection,
            int maxHeldCards,
            ref float bestValue)
        {
            if (selection.Count > 0)
            {
                bestValue = Mathf.Max(
                    bestValue,
                    EvaluateDrawOpportunity(selection, drawPool, ComboType.Set),
                    EvaluateDrawOpportunity(selection, drawPool, ComboType.Straight));
            }

            if (selection.Count >= maxHeldCards)
            {
                return;
            }

            for (int i = startIndex; i < hand.Count; i++)
            {
                selection.Add(hand[i]);
                FindFutureOpportunities(hand, drawPool, i + 1, selection, maxHeldCards, ref bestValue);
                selection.RemoveAt(selection.Count - 1);
            }
        }

        private float EvaluateDrawOpportunity(
            IReadOnlyList<CardInstance> heldCards,
            IReadOnlyList<CardInstance> drawPool,
            ComboType type)
        {
            List<CardInstance> completingCards = new List<CardInstance>();
            float totalGain = 0f;
            foreach (CardInstance drawCard in drawPool)
            {
                List<CardInstance> completedCombo = new List<CardInstance>(heldCards) { drawCard };
                float completedScore = CalculateComboScore(completedCombo, type);
                if (completedScore <= 0f)
                {
                    continue;
                }

                float heldComboScore = CalculateComboScore(heldCards, type);
                float gain = Mathf.Max(0f, completedScore - heldComboScore);
                if (gain <= 0f)
                {
                    continue;
                }

                completingCards.Add(drawCard);
                totalGain += gain;
            }

            if (completingCards.Count == 0)
            {
                return 0f;
            }

            float probability = probabilityCalculator.ProbabilityOfDrawingAny(
                completingCards,
                drawPool,
                settings.futureDrawHorizon);
            float conditionalGain = totalGain / completingCards.Count;
            return conditionalGain * probability;
        }

        private static List<CardInstance> GetUnseenCards(
            IReadOnlyList<CardInstance> hand,
            IReadOnlyList<CardInstance> unseenCards)
        {
            HashSet<string> handIds = new HashSet<string>(
                hand.Where(card => !string.IsNullOrEmpty(card.InstanceId))
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

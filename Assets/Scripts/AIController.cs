using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WildCardStudio
{
    public enum EnemyAIActionType
    {
        None,
        PlayCombo,
        DrawFromDeck,
        DrawFromDiscard,
        DiscardCard
    }

    [Serializable]
    public sealed class EnemyAISettings
    {
        [Min(1)] public int futureDrawHorizon = 2;
        [Min(0f)] public float immediatePointsWeight = 1f;
        [Min(0f)] public float futurePointsWeight = 1f;
        [Min(0f)] public float opportunityCostWeight = 1f;
        [Min(0f)] public float cardRetentionWeight = 0.05f;
        [Min(0f)] public float drawActionCost;
        [Min(0f)] public float discardPickupPriority;
        [Min(0f)] public float comboScoreMultiplier = 1f;
    }

    public sealed class EnemyAIAction
    {
        public EnemyAIActionType Type { get; }
        public ComboType ComboType { get; }
        public IReadOnlyList<CardInstance> Cards { get; }
        public float ImmediatePoints { get; }
        public float ExpectedFuturePoints { get; }
        public float OpportunityCost { get; }
        public float Value { get; }

        public EnemyAIAction(
            EnemyAIActionType type,
            ComboType comboType = ComboType.Set,
            IReadOnlyList<CardInstance> cards = null,
            float immediatePoints = 0f,
            float expectedFuturePoints = 0f,
            float opportunityCost = 0f,
            float value = 0f)
        {
            Type = type;
            ComboType = comboType;
            Cards = cards ?? Array.Empty<CardInstance>();
            ImmediatePoints = immediatePoints;
            ExpectedFuturePoints = expectedFuturePoints;
            OpportunityCost = opportunityCost;
            Value = value;
        }
    }

    public interface IEnemyAIActionExecutor
    {
        bool TryExecute(PlayerState player, EnemyAIAction action);
    }

    public sealed class AIController : MonoBehaviour
    {
        [SerializeField] private EnemyAISettings settings = new EnemyAISettings();
        [SerializeField] private AIPersonality personality = new AIPersonality();

        private IComboValidator comboValidator;
        private HandAnalyzer handAnalyzer;
        private DeckProbabilityCalculator probabilityCalculator;
        private ComboEvaluator comboEvaluator;
        private DrawEvaluator drawEvaluator;
        private DiscardEvaluator discardEvaluator;

        public EnemyAIHandAnalysis LastHandAnalysis { get; private set; }

        private void Awake()
        {
            InitializeSystems();
        }

        public EnemyAIAction ChooseAction(
            PlayerState player,
            IReadOnlyList<CardInstance> unseenCards,
            CardInstance discardTop,
            bool canDraw,
            bool canPlayCombos,
            bool canDiscard = false)
        {
            if (player == null || player.Hand == null)
            {
                return null;
            }

            InitializeSystems();
            LastHandAnalysis = comboEvaluator.Analyze(player.Hand, unseenCards);
            List<EnemyAIAction> candidates = new List<EnemyAIAction>();
            if (canPlayCombos)
            {
                candidates.AddRange(comboEvaluator.EvaluatePlayActions(
                    LastHandAnalysis, player.Hand, unseenCards));
            }

            if (canDraw)
            {
                candidates.AddRange(drawEvaluator.Evaluate(
                    player.Hand, unseenCards, discardTop, LastHandAnalysis));
            }

            if (canDiscard)
            {
                candidates.AddRange(discardEvaluator.Evaluate(
                    player.Hand, unseenCards, LastHandAnalysis));
            }

            return candidates
                .Where(action => action != null && action.Type != EnemyAIActionType.None)
                .OrderByDescending(action => action.Value)
                .FirstOrDefault();
        }

        public bool TryExecuteBestAction(
            PlayerState player,
            IReadOnlyList<CardInstance> unseenCards,
            CardInstance discardTop,
            bool canDraw,
            bool canPlayCombos,
            bool canDiscard,
            IEnemyAIActionExecutor executor,
            out EnemyAIAction selectedAction)
        {
            selectedAction = ChooseAction(
                player,
                unseenCards,
                discardTop,
                canDraw,
                canPlayCombos,
                canDiscard);
            return selectedAction != null
                && ExecuteSelectedAction(player, selectedAction, unseenCards, executor);
        }

        public bool ExecuteSelectedAction(
            PlayerState player,
            EnemyAIAction action,
            IReadOnlyList<CardInstance> unseenCards,
            IEnemyAIActionExecutor executor)
        {
            if (player == null || action == null || executor == null || !executor.TryExecute(player, action))
            {
                return false;
            }

            InitializeSystems();
            LastHandAnalysis = comboEvaluator.Analyze(player.Hand, unseenCards);
            return true;
        }

        private void InitializeSystems()
        {
            if (settings == null)
            {
                settings = new EnemyAISettings();
            }

            if (comboEvaluator != null)
            {
                return;
            }

            EnemyAISettings adjustedSettings = personality != null
                ? personality.ApplyTo(settings)
                : settings;
            comboValidator = new BasicComboValidator();
            handAnalyzer = new HandAnalyzer(comboValidator);
            probabilityCalculator = new DeckProbabilityCalculator();
            comboEvaluator = new ComboEvaluator(
                handAnalyzer,
                comboValidator,
                probabilityCalculator,
                adjustedSettings);
            drawEvaluator = new DrawEvaluator(adjustedSettings, comboEvaluator);
            discardEvaluator = new DiscardEvaluator(adjustedSettings, comboEvaluator);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WildCardStudio
{
    /// Identifies the high-level action an enemy AI may choose.
    public enum EnemyAIActionType
    {
        None,
        PlayCombo,
        DrawFromDeck,
        DrawFromDiscard,
        DiscardCard
    }

    /// Configures the weights and costs used when evaluating enemy AI actions.
    [Serializable]
    public sealed class EnemyAISettings
    {
        /// Number of future draws considered when estimating combo opportunities.
        [Min(1)] public int futureDrawHorizon = 2; 
        /// Weight applied to points gained immediately by an action.
        [Min(0f)] public float immediatePointsWeight = 1f; 
        /// Weight applied to projected points gained from future hands.
        [Min(0f)] public float futurePointsWeight = 1f;
        /// Weight applied to cards or opportunities lost by taking an action.
        [Min(0f)] public float opportunityCostWeight = 1f;
        /// Penalty value assigned to cards committed to a combo.
        [Min(0f)] public float cardRetentionWeight = 0.05f;
        /// Fixed cost of drawing from the deck or discard pile.
        [Min(0f)] public float drawActionCost;
        /// Bonus applied when the discard pile contains an especially useful card.
        [Min(0f)] public float discardPickupPriority;
        /// Multiplier applied to the calculated score of a valid combo.
        [Min(0f)] public float comboScoreMultiplier = 1f;
    }

    /// Represents one evaluated enemy action and all information used to rank it.
    public sealed class EnemyAIAction
    {
        /// The type of action to execute.
        public EnemyAIActionType Type { get; }
        /// The combo type when this action plays a combo.
        public ComboType ComboType { get; }
        /// The cards involved in this action.
        public IReadOnlyList<CardInstance> Cards { get; }
        /// Immediate points expected from this action.
        public float ImmediatePoints { get; }
        /// Points expected to remain available after this action.
        public float ExpectedFuturePoints { get; }
        /// Estimated cost of losing future or retained value.
        public float OpportunityCost { get; }
        /// Final weighted value used to rank this action.
        public float Value { get; }

        /// Creates an action with the points, opportunity cost, and final rank value it provides.
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

    /// Defines how an action selected by the enemy AI is executed in the game.
    public interface IEnemyAIActionExecutor
    {
        /// Attempts to execute the supplied action for the given player.
        bool TryExecute(PlayerState player, EnemyAIAction action);
    }

    /// Builds, ranks, and optionally executes enemy actions through the supporting evaluators.
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

        /// Initializes the AI evaluators before the first decision is made.
        private void Awake()
        {
            InitializeSystems();
        }

        /// Collects all currently legal candidate actions and returns the highest-ranked one.
        public EnemyAIAction ChooseAction(
            PlayerState player,
            IReadOnlyList<CardInstance> unseenCards,
            CardInstance discardTop,
            bool canDraw,
            bool canPlayCombos,
            bool canDiscard = false)
        {
            // A player without a hand cannot make a legal or meaningful AI decision.
            if (player == null || player.Hand == null)
            {
                return null;
            }

            InitializeSystems();
            // Analyze the current hand once so every action evaluator compares against the same state.
            LastHandAnalysis = comboEvaluator.Analyze(player.Hand, unseenCards);
            List<EnemyAIAction> candidates = new List<EnemyAIAction>();
            // Only include actions that are currently allowed by the turn state.
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

        /// Chooses the highest-value action and delegates execution through the supplied executor.
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

        /// Sends an already-selected action to the executor and refreshes the latest hand analysis.
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

        /// Creates the AI analysis pipeline and applies the configured personality settings.
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

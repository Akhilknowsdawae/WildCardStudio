using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LifeAfter;

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
        /// Maximum number of combos the AI may play during one turn.
        [Min(0)] public int maxCombosPerTurn = 1;
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
        [Header("AI Opponent")]
        [SerializeField, Tooltip("Drag the GameObject that represents the AI opponent or its character.")]
        private GameObject aiOpponentGameObject;

        [Header("AI Deck")]
        [SerializeField, Tooltip("Assign the card assets available to this AI. This list is not populated automatically.")]
        private List<Card> aiDeck = new List<Card>();

        [Header("AI Settings")]
        [SerializeField] private EnemyAISettings settings = new EnemyAISettings();
        [SerializeField] private AIPersonality personality = new AIPersonality();

        private IComboValidator comboValidator;
        private HandAnalyzer handAnalyzer;
        private DeckProbabilityCalculator probabilityCalculator;
        private ComboEvaluator comboEvaluator;
        private DrawEvaluator drawEvaluator;
        private DiscardEvaluator discardEvaluator;
        private int nextAIDeckIndex;

        public EnemyAIHandAnalysis LastHandAnalysis { get; private set; }
        public GameObject AIOpponentGameObject => aiOpponentGameObject;
        public IReadOnlyList<Card> AIDeck => aiDeck;
        public int MaxCombosPerTurn => Mathf.Max(0, settings != null ? settings.maxCombosPerTurn : 1);

        /// Returns whether a runtime player represents this AI's opponent.
        public bool IsAIOpponent(PlayerState player)
        {
            if (player == null)
            {
                return false;
            }

            if (aiOpponentGameObject != null
                && (MatchesPlayerIdentifier(player.Name, aiOpponentGameObject.name)
                    || MatchesPlayerIdentifier(player.PlayerId, aiOpponentGameObject.name)))
            {
                return true;
            }

            return !player.IsHuman;
        }

        private static bool MatchesPlayerIdentifier(string identifier, string gameObjectName)
        {
            return !string.IsNullOrWhiteSpace(identifier)
                && string.Equals(identifier.Trim(), gameObjectName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// Assigns the GameObject that represents this AI opponent at runtime.
        public void SetAIOpponentGameObject(GameObject aiOpponent)
        {
            aiOpponentGameObject = aiOpponent;
        }

        /// Creates runtime opponent state from the deck explicitly assigned in the Inspector.
        public PlayerState CreateAIOpponent(int startingHandSize)
        {
            string opponentName = aiOpponentGameObject != null
                ? aiOpponentGameObject.name
                : gameObject.name;
            PlayerState opponent = new PlayerState
            {
                PlayerId = "ai-opponent",
                Name = opponentName,
                IsHuman = false
            };

            if (aiDeck == null)
            {
                nextAIDeckIndex = 0;
                return opponent;
            }

            int cardsToDeal = Mathf.Min(Mathf.Max(0, startingHandSize), aiDeck.Count);
            nextAIDeckIndex = cardsToDeal;
            for (int i = 0; i < cardsToDeal; i++)
            {
                Card definition = aiDeck[i];
                if (definition == null)
                {
                    Debug.LogWarning($"AI deck entry {i} is not assigned and will be skipped.", this);
                    continue;
                }

                opponent.Hand.Add(new CardInstance
                {
                    InstanceId = Guid.NewGuid().ToString(),
                    Definition = definition,
                    Rank = definition.cardRank,
                    Suit = definition.suit != null && definition.suit.Count > 0
                        ? definition.suit[0]
                        : default(Suit),
                    Type = definition.cardType != null && definition.cardType.Count > 0
                        ? definition.cardType[0]
                        : CardType.Number
                });
            }

            return opponent;
        }

        /// Returns card instances representing the AI deck cards that have not been dealt or drawn.
        public IReadOnlyList<CardInstance> GetRemainingAIDeckCards()
        {
            List<CardInstance> remainingCards = new List<CardInstance>();
            if (aiDeck == null)
            {
                return remainingCards;
            }

            for (int i = nextAIDeckIndex; i < aiDeck.Count; i++)
            {
                Card definition = aiDeck[i];
                if (definition != null)
                {
                    remainingCards.Add(CreateCardInstance(definition));
                }
            }

            return remainingCards;
        }

        /// Draws the next assigned card from the AI deck, skipping unassigned entries.
        public CardInstance DrawNextAIDeckCard()
        {
            if (aiDeck == null)
            {
                return null;
            }

            while (nextAIDeckIndex < aiDeck.Count)
            {
                Card definition = aiDeck[nextAIDeckIndex++];
                if (definition == null)
                {
                    Debug.LogWarning("Skipping an unassigned card in the AI deck.", this);
                    continue;
                }

                return CreateCardInstance(definition);
            }

            return null;
        }

        private static CardInstance CreateCardInstance(Card definition)
        {
            return new CardInstance
            {
                InstanceId = Guid.NewGuid().ToString(),
                Definition = definition,
                Rank = definition.cardRank,
                Suit = definition.suit != null && definition.suit.Count > 0
                    ? definition.suit[0]
                    : default(Suit),
                Type = definition.cardType != null && definition.cardType.Count > 0
                    ? definition.cardType[0]
                    : CardType.Number
            };
        }

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
            // An opponent without a hand cannot make a legal or meaningful AI decision.
            if (player == null || player.Hand == null)
            {
                return null;
            }

            InitializeSystems();
            // Analyze once so logging and candidate evaluation use the same hand state.
            LastHandAnalysis = comboEvaluator.Analyze(player.Hand, unseenCards);
            Debug.Log(
                $"[Enemy AI] Evaluating turn for {GetPlayerLabel(player)}. "
                + $"Hand: {player.Hand.Count}, available combos: "
                + $"{LastHandAnalysis.AvailableCombos.Count}, "
                + $"draw={canDraw}, play={canPlayCombos}, discard={canDiscard}.",
                this);

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

            EnemyAIAction selectedAction = candidates
                .Where(action => action != null && action.Type != EnemyAIActionType.None)
                .OrderByDescending(action => action.Value)
                .FirstOrDefault();

            if (selectedAction == null)
            {
                Debug.Log($"[Enemy AI] {GetPlayerLabel(player)} has no valid action.", this);
            }
            else
            {
                Debug.Log(
                    $"[Enemy AI] {GetPlayerLabel(player)} chose {selectedAction.Type} "
                    + $"with value {selectedAction.Value:F1} "
                    + $"(cards: {selectedAction.Cards.Count}).",
                    this);
            }

            return selectedAction;
        }

        /// Selects the lowest-value hand card that is not useful in a current or discard-top combo.
        public CardInstance ChooseDiscardCandidate(PlayerState player, CardInstance discardTop)
        {
            if (player == null || player.Hand == null || player.Hand.Count == 0)
            {
                return null;
            }

            InitializeSystems();
            HashSet<CardInstance> protectedByHandCombos = new HashSet<CardInstance>();
            foreach (HandCombo combo in handAnalyzer.FindCombos(player.Hand))
            {
                foreach (CardInstance card in combo.Cards)
                {
                    protectedByHandCombos.Add(card);
                }
            }

            HashSet<CardInstance> protectedByDiscardTop = new HashSet<CardInstance>();
            if (discardTop != null)
            {
                List<CardInstance> handWithDiscardTop = player.Hand
                    .Where(card => card != null)
                    .ToList();
                handWithDiscardTop.Add(discardTop);

                foreach (HandCombo combo in handAnalyzer.FindCombos(handWithDiscardTop))
                {
                    if (!combo.Cards.Contains(discardTop))
                    {
                        continue;
                    }

                    foreach (CardInstance card in combo.Cards)
                    {
                        if (card != discardTop)
                        {
                            protectedByDiscardTop.Add(card);
                        }
                    }
                }
            }

            List<CardInstance> safeDiscards = player.Hand
                .Where(card => card != null
                    && !protectedByHandCombos.Contains(card)
                    && !protectedByDiscardTop.Contains(card))
                .OrderBy(card => card.EffectiveValue)
                .ThenBy(card => card.Rank)
                .ToList();

            string discardTopLabel = discardTop == null ? "none" : GetCardLabel(discardTop);
            bool forcedDiscard = safeDiscards.Count == 0;
            CardInstance candidate = forcedDiscard
                ? player.Hand
                    .Where(card => card != null)
                    .OrderBy(card => card.EffectiveValue)
                    .ThenBy(card => card.Rank)
                    .FirstOrDefault()
                : safeDiscards[0];
            if (candidate == null)
            {
                return null;
            }

            Debug.Log(
                $"[AI Discard] {GetPlayerLabel(player)} selected {GetCardLabel(candidate)} "
                + $"(value {candidate.EffectiveValue}) as the "
                + $"{(forcedDiscard ? "lowest-value available card; all cards are combo-useful" : "lowest-value card outside current combos")} "
                + $"(discard pile top: {discardTopLabel}).",
                this);
            return candidate;
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
                Debug.LogWarning(
                    $"[Enemy AI] Could not execute action "
                    + $"{action?.Type.ToString() ?? "None"} for {GetPlayerLabel(player)}.",
                    this);
                return false;
            }

            InitializeSystems();
            LastHandAnalysis = comboEvaluator.Analyze(player.Hand, unseenCards);
            Debug.Log(
                $"[Enemy AI] Successfully executed {action.Type} for {GetPlayerLabel(player)}.",
                this);
            return true;
        }

        private static string GetPlayerLabel(PlayerState player)
        {
            if (player == null)
            {
                return "null player";
            }

            return string.IsNullOrWhiteSpace(player.Name)
                ? player.PlayerId
                : player.Name;
        }

        private static string GetCardLabel(CardInstance card)
        {
            if (card == null)
            {
                return "missing card";
            }

            if (card.Definition != null && !string.IsNullOrWhiteSpace(card.Definition.cardName))
            {
                return card.Definition.cardName;
            }

            return card.Type == CardType.Number
                ? $"{card.Rank} of {card.Suit}"
                : card.Type.ToString();
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

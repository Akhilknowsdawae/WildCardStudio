using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using LifeAfter;

namespace WildCardStudio
{
    public enum CameraState
    {
        Normal,
        DeskView,
        HideCard
    }

    public enum PlayerDrawSourceRule
    {
        DrawPileOnly,
        DiscardPileOnly,
        EitherPile
    }

    /// Coordinates turns, phases, combo validation, scoring, events, and match completion.
    public class GameController : MonoBehaviour
    {
        [Header("Players")]
        [SerializeField] private List<PlayerState> players = new List<PlayerState>();

        [Header("Core Rules")]
        [SerializeField] private TurnPhase currentPhase = TurnPhase.Draw;
        [SerializeField] private int activePlayerIndex;
        [SerializeField] private int currentTurnNumber = 1;
        [SerializeField] private bool matchEnded;
        [SerializeField] private bool enterAdvancesPhase = true;
        [SerializeField] private CameraState cameraState = CameraState.Normal;

        [Header("Draw Rules")]
        [SerializeField] private PlayerDrawSourceRule playerDrawSourceRule = PlayerDrawSourceRule.EitherPile;

        [Header("Phase Display")]
        [SerializeField] private TMP_Text phaseDisplayText;
        [SerializeField] private string drawPhaseText = "Draw";
        [SerializeField] private string playPhaseText = "Play";
        [SerializeField] private string discardPhaseText = "Discard";
        [SerializeField] private string resolveTurnPhaseText = "Resolve Turn";
        [SerializeField] private string aiTurnPhaseText = "AI Turn";
        [SerializeField] private string gameOverPhaseText = "Game Over";

        [Header("AI Turn")]
        [SerializeField, Min(0f)] private float aiPhaseDisplayDelay = 0.35f;
        [SerializeField] private EnemyHandDisplay enemyHandDisplay;

        [Header("Systems")]
        private IComboValidator comboValidator;
        private EventDispatcher eventDispatcher;

        [Header("Scoring")]
        [SerializeField] private float baseScoreMultiplier = 1f;

        [Header("Starting Hand")]
        [SerializeField, Min(0)] private int startingHandSize = 10;

        private readonly List<Combo> activeCombos = new List<Combo>();
        private bool isAIRunningTurn;

        public TurnPhase CurrentPhase => currentPhase;
        public int ActivePlayerIndex => activePlayerIndex;
        public PlayerState ActivePlayer => players != null && players.Count > 0 ? players[activePlayerIndex] : null;
        public bool MatchEnded => matchEnded;
        public CameraState CurrentCameraState => cameraState;
        public bool EnterAdvancesPhaseEnabled => enterAdvancesPhase;
        public EventDispatcher Events => eventDispatcher;
        public IReadOnlyList<PlayerState> Players => players;
        public IReadOnlyList<Combo> ActiveCombos => activeCombos;
        public bool CanPlayerDrawFromDrawPile => playerDrawSourceRule != PlayerDrawSourceRule.DiscardPileOnly;
        public bool CanPlayerDrawFromDiscardPile => playerDrawSourceRule != PlayerDrawSourceRule.DrawPileOnly;
        /// Starting hand size used when a match initializes.
        public int StartingHandSize => Mathf.Max(0, startingHandSize);

        /// Updates the camera/view state used by the human hand controls.
        public void SetCameraState(CameraState nextState)
        {
            if (cameraState == nextState)
            {
                return;
            }

            CameraState previousState = cameraState;
            cameraState = nextState;
            Debug.Log($"[Camera State] {previousState} -> {cameraState}.", this);
        }

        /// Initializes turn systems before scene objects begin interacting.
        private void Awake()
        {
            if (eventDispatcher == null)
            {
                eventDispatcher = new EventDispatcher();
            }

            if (comboValidator == null)
            {
                comboValidator = new BasicComboValidator();
            }

            currentPhase = TurnPhase.Draw;
            UpdatePhaseDisplay();
        }

        /// Handles Enter and Space shortcuts for advancing or completing the discard phase.
        private void Update()
        {
            if (isAIRunningTurn)
            {
                return;
            }

            if (currentPhase == TurnPhase.Discard && Input.GetKeyDown(KeyCode.Space))
            {
                DiscardManager discardManager = FindFirstObjectByType<DiscardManager>();
                ScoreManager scoreManager = FindFirstObjectByType<ScoreManager>();
                if (discardManager == null)
                {
                    Debug.LogError("Cannot discard cards: no DiscardManager exists in the scene.", this);
                }
                else if (discardManager.DiscardSelectedCards(scoreManager))
                {
                    AdvancePhase();
                }

                return;
            }

            if (enterAdvancesPhase
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                if (currentPhase == TurnPhase.Discard)
                {
                    DiscardManager discardManager = FindFirstObjectByType<DiscardManager>();
                    ScoreManager scoreManager = FindFirstObjectByType<ScoreManager>();
                    if (discardManager == null)
                    {
                        Debug.LogError("Cannot advance from the discard phase: no DiscardManager exists in the scene.", this);
                    }
                    else if (discardManager.DiscardSelectedCards(scoreManager))
                    {
                        AdvancePhase();
                    }

                    return;
                }

                AdvancePhase();
            }
        }

        /// Resets match state and initializes the supplied player list.
        public void InitializeMatch(List<PlayerState> startingPlayers)
        {
            players = startingPlayers ?? new List<PlayerState>();
            if (players.Count == 0)
            {
                return;
            }

            activePlayerIndex = 0;
            currentTurnNumber = 1;
            matchEnded = false;
            currentPhase = TurnPhase.Draw;
            UpdatePhaseDisplay();
            activeCombos.Clear();

            ScoreManager scoreManager = FindFirstObjectByType<ScoreManager>();
            if (scoreManager != null)
            {
                scoreManager.ResetTotalScore();
            }

            if (eventDispatcher == null)
            {
                eventDispatcher = new EventDispatcher();
            }

            if (comboValidator == null)
            {
                comboValidator = new BasicComboValidator();
            }

            eventDispatcher.Publish(new MatchStartedEvent(players));
        }

        /// Initializes human and AI runtime players from the scene's hand and assigned AI deck.
        public bool InitializeMatchFromScene(HandManager humanHand, AIController aiController)
        {
            if (humanHand == null)
            {
                Debug.LogError("Cannot initialize the match: no human HandManager was provided.", this);
                return false;
            }

            if (aiController == null)
            {
                Debug.LogError("Cannot initialize the match: no AIController exists in the scene.", this);
                return false;
            }

            PlayerState humanPlayer = new PlayerState
            {
                PlayerId = "human-player",
                Name = "Player",
                IsHuman = true
            };

            foreach (GameObject cardObject in humanHand.cardsInHand)
            {
                CardDisplay display = cardObject != null ? cardObject.GetComponent<CardDisplay>() : null;
                Card definition = display != null ? display.cardData : null;
                if (definition == null)
                {
                    Debug.LogError("Cannot initialize the human hand: a hand card is missing its card data.", cardObject);
                    return false;
                }

                humanPlayer.Hand.Add(CreateCardInstance(definition, cardObject.GetInstanceID().ToString()));
            }

            PlayerState aiPlayer = aiController.CreateAIOpponent(startingHandSize);
            InitializeMatch(new List<PlayerState> { humanPlayer, aiPlayer });
            if (enemyHandDisplay == null)
            {
                enemyHandDisplay = FindFirstObjectByType<EnemyHandDisplay>();
            }

            if (enemyHandDisplay == null)
            {
                Debug.LogWarning("The AI opponent has no EnemyHandDisplay; its cards will not be shown.", this);
            }
            else
            {
                enemyHandDisplay.SynchronizeHand(aiPlayer);
                enemyHandDisplay.RefreshTotalScore(aiPlayer, activeCombos);
            }

            return true;
        }

        /// Starts a new turn for the active player.
        public void StartTurn()
        {
            if (matchEnded)
            {
                return;
            }

            PlayerState activePlayer = ActivePlayer;
            bool activePlayerIsAI = IsAIOpponent(activePlayer);
            currentPhase = TurnPhase.Draw;
            UpdatePhaseDisplay();
            if (activePlayerIsAI)
            {
                Debug.Log(
                    $"[Enemy Turn] Turn {currentTurnNumber} started for "
                    + $"{GetPlayerLabel(activePlayer)} ({activePlayer.Hand.Count} cards).",
                    this);
            }
            eventDispatcher.Publish(new TurnStartedEvent(activePlayer, currentTurnNumber));
            ResolveTurnBasedEffects();
            if (activePlayerIsAI)
            {
                StartCoroutine(RunAITurn(activePlayer));
            }
        }

        /// Advances through player phases and the AI opponent's play turn.
        public void AdvancePhase()
        {
            if (matchEnded)
            {
                return;
            }

            TurnPhase previousPhase = currentPhase;
            int previousTurnNumber = currentTurnNumber;
            PlayerState previousPlayer = ActivePlayer;
            switch (currentPhase)
            {
                case TurnPhase.Draw:
                    currentPhase = TurnPhase.Play;
                    break;
                case TurnPhase.Play:
                    currentPhase = TurnPhase.Discard;
                    break;
                case TurnPhase.Discard:
                    currentPhase = TurnPhase.ResolveTurn;
                    break;
                case TurnPhase.ResolveTurn:
                    ResolveTurnEnd();
                    return;
                case TurnPhase.AITurn:
                    ResolveTurnEnd();
                    return;
                case TurnPhase.GameOver:
                    return;
                default:
                    currentPhase = TurnPhase.Draw;
                    break;
            }

            Debug.Log(
                $"[Turn Flow] Turn {previousTurnNumber} ({GetPlayerLabel(previousPlayer)}): "
                + $"{previousPhase} -> {currentPhase}.",
                this);
            UpdatePhaseDisplay();
            PlayerState activePlayer = ActivePlayer;
            if (IsAIOpponent(activePlayer))
            {
                Debug.Log(
                    $"[Enemy Turn] {GetPlayerLabel(activePlayer)} entered phase {currentPhase} "
                    + $"on turn {currentTurnNumber}.",
                    this);
            }
            eventDispatcher.Publish(new PhaseChangedEvent(currentPhase, ActivePlayer, currentTurnNumber));
        }

        /// Attempts to enter the discard phase from the current phase.
        public bool EnterDiscardPhase()
        {
            if (matchEnded)
            {
                return false;
            }

            if (currentPhase == TurnPhase.Discard)
            {
                return true;
            }

            if (currentPhase != TurnPhase.Draw && currentPhase != TurnPhase.Play)
            {
                return false;
            }

            currentPhase = TurnPhase.Discard;
            UpdatePhaseDisplay();
            eventDispatcher.Publish(new PhaseChangedEvent(currentPhase, ActivePlayer, currentTurnNumber));
            return true;
        }

        /// Draws a card for a player from either the deck or discard pile.
        public bool DrawCardFromDeck(PlayerState player, bool fromDiscard)
        {
            if (matchEnded || player == null || player != ActivePlayer)
            {
                return false;
            }

            if (player.IsHuman
                && (fromDiscard ? !CanPlayerDrawFromDiscardPile : !CanPlayerDrawFromDrawPile))
            {
                Debug.LogWarning(
                    $"The configured draw rule does not allow {GetPlayerLabel(player)} "
                    + $"to draw from the {(fromDiscard ? "discard" : "draw")} pile.",
                    this);
                return false;
            }

            if (currentPhase != TurnPhase.Draw)
            {
                return false;
            }

            var card = fromDiscard ? DrawFromDiscardPile() : DrawFromDeck();
            if (card == null)
            {
                return false;
            }

            player.Hand.Add(card);
            eventDispatcher.Publish(new CardDrawnEvent(player, card, fromDiscard));
            return true;
        }

        /// Creates a combo after validating the cards, player, and active turn state.
        public ComboValidationResult TryCreateCombo(PlayerState player, List<CardInstance> cards, ComboType comboType)
        {
            if (matchEnded)
            {
                return ComboValidationResult.Failure(ComboValidationReason.GameOver, "The match is over.");
            }

            if (player == null || player != ActivePlayer)
            {
                return ComboValidationResult.Failure(ComboValidationReason.NotPlayerTurn, "It is not this player's turn.");
            }

            var validation = comboValidator.CanCreateCombo(cards, comboType, player, this);
            if (!validation.IsValid)
            {
                return validation;
            }

            var combo = new Combo
            {
                ComboId = $"combo-{Guid.NewGuid():N}",
                Type = comboType,
                Cards = cards ?? new List<CardInstance>(),
                Owner = player,
                ScoringContext = validation.ScoringContext,
                Score = 0,
                IsValid = true
            };

            combo.Score = CalculateComboScore(combo, validation.ScoringContext);
            activeCombos.Add(combo);
            eventDispatcher.Publish(new ComboCreatedEvent(combo, player));
            return validation;
        }

        /// Attempts to add a card to an existing combo.
        public ComboValidationResult TryAddCardToCombo(PlayerState player, Combo combo, CardInstance card)
        {
            if (matchEnded)
            {
                return ComboValidationResult.Failure(ComboValidationReason.GameOver, "The match is over.");
            }

            if (player == null || player != ActivePlayer)
            {
                return ComboValidationResult.Failure(ComboValidationReason.NotPlayerTurn, "It is not this player's turn.");
            }

            if (combo == null)
            {
                return ComboValidationResult.Failure(ComboValidationReason.InvalidAction, "The target combo does not exist.");
            }

            var validation = comboValidator.CanAddCard(combo, card, player, this);
            if (!validation.IsValid)
            {
                return validation;
            }

            if (card != null && player.Hand.Contains(card))
            {
                player.Hand.Remove(card);
            }

            combo.Cards.Add(card);
            RevalidateCombo(combo);

            if (combo.Owner == null)
            {
                combo.Owner = player;
            }

            eventDispatcher.Publish(new ComboModifiedEvent(combo, player, card));
            CheckWinConditionAfterAction(player);
            return validation;
        }

        /// Discards a card instance from the supplied player's hand.
        public void DiscardCard(PlayerState player, CardInstance card)
        {
            if (matchEnded || player == null || player != ActivePlayer)
            {
                return;
            }

            if (currentPhase != TurnPhase.Discard)
            {
                return;
            }

            if (card == null || !player.Hand.Contains(card))
            {
                return;
            }

            player.Hand.Remove(card);
            eventDispatcher.Publish(new CardDiscardedEvent(player, card));
            CheckWinConditionAfterAction(player);
        }

        /// Resolves end-of-turn effects and advances to the next player.
        public void ResolveTurnEnd()
        {
            if (matchEnded)
            {
                return;
            }

            PlayerState resolvedPlayer = ActivePlayer;
            SynchronizeHumanHand(resolvedPlayer);
            if (resolvedPlayer != null && resolvedPlayer.Hand.Count == 0)
            {
                Debug.Log($"[Turn Flow] {GetPlayerLabel(resolvedPlayer)} has no cards left. Ending the match.", this);
                ResolveMatchEnd();
                return;
            }

            TurnPhase resolvedPhase = currentPhase;
            int resolvedTurnNumber = currentTurnNumber;
            bool completingAITurn = IsAIOpponent(resolvedPlayer)
                && (currentPhase == TurnPhase.ResolveTurn || currentPhase == TurnPhase.AITurn);

            if (IsAIOpponent(resolvedPlayer))
            {
                Debug.Log(
                    $"[Enemy Turn] {GetPlayerLabel(resolvedPlayer)} completed turn "
                    + $"{currentTurnNumber}.",
                    this);
            }

            foreach (var combo in activeCombos)
            {
                RevalidateCombo(combo);
            }

            currentTurnNumber++;
            AdvanceToNextPlayer();
            PlayerState nextPlayer = ActivePlayer;
            bool nextPlayerIsAI = !completingAITurn && IsAIOpponent(nextPlayer);
            currentPhase = TurnPhase.Draw;
            Debug.Log(
                $"[Turn Flow] Turn {resolvedTurnNumber} ({GetPlayerLabel(resolvedPlayer)}) "
                + $"{resolvedPhase} -> turn {currentTurnNumber} "
                + $"({GetPlayerLabel(nextPlayer)}) {currentPhase}.",
                this);
            UpdatePhaseDisplay();
            if (nextPlayerIsAI)
            {
                Debug.Log(
                    $"[AI Turn] Turn {currentTurnNumber} started for {GetPlayerLabel(nextPlayer)} "
                    + "after resolving the previous turn.",
                    this);
            }
            else if (nextPlayer != null)
            {
                Debug.Log(
                    $"[Turn Resolution] Next player is {GetPlayerLabel(nextPlayer)} "
                    + $"(PlayerId: {nextPlayer.PlayerId ?? "none"}, IsHuman: {nextPlayer.IsHuman}); "
                    + $"entering {currentPhase}.",
                    this);
            }
            eventDispatcher.Publish(new PhaseChangedEvent(currentPhase, nextPlayer, currentTurnNumber));
            eventDispatcher.Publish(new TurnResolvedEvent(nextPlayer, currentTurnNumber));
            if (nextPlayerIsAI)
            {
                StartCoroutine(RunAITurn(nextPlayer));
            }
        }

        private bool DiscardAICard(PlayerState player)
        {
            AIController aiController = FindFirstObjectByType<AIController>();
            if (aiController == null)
            {
                Debug.LogError("Cannot discard an AI card: no AIController exists in the scene.", this);
                return false;
            }

            DiscardManager discardManager = FindFirstObjectByType<DiscardManager>();
            if (discardManager == null)
            {
                Debug.LogError("Cannot discard an AI card: no DiscardManager exists in the scene.", this);
                return false;
            }

            CardInstance discardTop = null;
            if (discardManager.DiscardedCards.Count > 0)
            {
                Card topDefinition = discardManager.DiscardedCards[discardManager.DiscardedCards.Count - 1];
                if (topDefinition != null)
                {
                    discardTop = CreateCardInstance(topDefinition, $"discard-top-{topDefinition.GetInstanceID()}");
                }
            }

            Debug.Log(
                $"[AI Discard] Evaluating {GetPlayerLabel(player)}'s {player.Hand.Count} card(s). "
                + $"Discard pile top: {(discardTop == null ? "none" : FormatCardName(discardTop))}.",
                this);

            CardInstance candidate = aiController.ChooseDiscardCandidate(player, discardTop);
            if (candidate == null)
            {
                Debug.Log(
                    $"[AI Discard] {GetPlayerLabel(player)} ends the turn without discarding; "
                    + "every card is useful in a current or discard-top combo.",
                    this);
                return true;
            }

            if (candidate.Definition == null)
            {
                Debug.LogError("Cannot discard the AI's selected card: its card definition is missing.", this);
                return false;
            }

            if (!discardManager.AddCardToDiscardPile(candidate.Definition))
            {
                return false;
            }

            player.Hand.Remove(candidate);
            if (enemyHandDisplay != null)
            {
                enemyHandDisplay.RemoveCardFromHand(candidate);
            }

            Debug.Log(
                $"[AI Discard] {GetPlayerLabel(player)} discarded {FormatCardName(candidate)}. "
                + $"Hand now contains {player.Hand.Count} card(s).",
                this);
            return true;
        }

        private void SynchronizeHumanHand(PlayerState player)
        {
            if (player == null || !player.IsHuman)
            {
                return;
            }

            HandManager handManager = FindFirstObjectByType<HandManager>();
            if (handManager == null)
            {
                Debug.LogError("Cannot synchronize the human hand: no HandManager exists in the scene.", this);
                return;
            }

            List<CardInstance> currentHand = new List<CardInstance>(handManager.cardsInHand.Count);
            foreach (GameObject cardObject in handManager.cardsInHand)
            {
                CardDisplay display = cardObject != null ? cardObject.GetComponent<CardDisplay>() : null;
                Card definition = display != null ? display.cardData : null;
                if (definition == null)
                {
                    Debug.LogError("Cannot synchronize the human hand: a hand card is missing its card data.", cardObject);
                    return;
                }

                currentHand.Add(CreateCardInstance(definition, cardObject.GetInstanceID().ToString()));
            }

            player.Hand = currentHand;
        }

        private static CardInstance CreateCardInstance(Card definition, string instanceId)
        {
            return new CardInstance
            {
                InstanceId = instanceId,
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

        private bool IsAIOpponent(PlayerState player)
        {
            if (player == null)
            {
                return false;
            }

            AIController aiController = FindFirstObjectByType<AIController>();
            return !player.IsHuman
                || (aiController != null && aiController.IsAIOpponent(player));
        }

        private IEnumerator RunAITurn(PlayerState player)
        {
            isAIRunningTurn = true;
            try
            {
                Debug.Log($"[AI Turn] Starting Draw -> Play -> Discard -> Resolve for {GetPlayerLabel(player)}.", this);

                if (player.Hand == null || player.Hand.Count == 0)
                {
                    Debug.LogWarning(
                        $"[AI Turn] {GetPlayerLabel(player)} has no cards to draw or play. Ending the match.",
                        this);
                    ResolveMatchEnd();
                    yield break;
                }

                AIController aiController = FindFirstObjectByType<AIController>();
                if (aiController == null)
                {
                    Debug.LogError("Cannot evaluate the AI turn: no AIController exists in the scene.", this);
                    yield break;
                }

                Debug.Log($"[AI Turn] {GetPlayerLabel(player)} entered Draw phase.", this);
                yield return new WaitForSeconds(aiPhaseDisplayDelay);
                RunAIDrawPhase(player, aiController);
                if (matchEnded)
                {
                    yield break;
                }

                SetAIPhase(player, TurnPhase.Play);
                yield return new WaitForSeconds(aiPhaseDisplayDelay);
                yield return PlayAICombos(player, aiController);
                if (matchEnded)
                {
                    yield break;
                }

                SetAIPhase(player, TurnPhase.Discard);
                yield return new WaitForSeconds(aiPhaseDisplayDelay);
                if (!DiscardAICard(player))
                {
                    yield break;
                }

                if (player.Hand.Count == 0)
                {
                    Debug.Log($"[AI Discard] {GetPlayerLabel(player)} has no cards left after discarding.", this);
                    ResolveMatchEnd();
                    yield break;
                }

                SetAIPhase(player, TurnPhase.ResolveTurn);
                yield return new WaitForSeconds(aiPhaseDisplayDelay);
                ResolveTurnEnd();
            }
            finally
            {
                isAIRunningTurn = false;
            }
        }

        private void RunAIDrawPhase(PlayerState player, AIController aiController)
        {
            DiscardManager discardManager = FindFirstObjectByType<DiscardManager>();
            CardInstance discardTop = null;
            if (discardManager != null && discardManager.DiscardedCards.Count > 0)
            {
                Card topCard = discardManager.DiscardedCards[discardManager.DiscardedCards.Count - 1];
                if (topCard != null)
                {
                    discardTop = CreateCardInstance(topCard, $"discard-top-{topCard.GetInstanceID()}");
                }
            }

            IReadOnlyList<CardInstance> remainingDeck = aiController.GetRemainingAIDeckCards();
            EnemyAIAction drawAction = aiController.ChooseAction(
                player,
                remainingDeck,
                discardTop,
                canDraw: true,
                canPlayCombos: false,
                canDiscard: false);

            if (drawAction == null)
            {
                Debug.LogWarning($"[AI Draw] {GetPlayerLabel(player)} has no available draw.", this);
                return;
            }

            CardInstance drawnCard = null;
            if (drawAction.Type == EnemyAIActionType.DrawFromDiscard)
            {
                if (discardManager == null)
                {
                    Debug.LogError("AI selected the discard pile, but no DiscardManager exists.", this);
                    return;
                }

                Card cardDefinition = discardManager.DrawTopCardFromDiscardPile();
                if (cardDefinition != null)
                {
                    drawnCard = CreateCardInstance(cardDefinition, Guid.NewGuid().ToString());
                }
            }
            else if (drawAction.Type == EnemyAIActionType.DrawFromDeck)
            {
                drawnCard = aiController.DrawNextAIDeckCard();
            }

            if (drawnCard == null)
            {
                Debug.LogWarning(
                    $"[AI Draw] {GetPlayerLabel(player)} could not complete its selected {drawAction.Type} action.",
                    this);
                return;
            }

            player.Hand.Add(drawnCard);
            Debug.Log(
                $"[AI Draw] {GetPlayerLabel(player)} drew {FormatCardName(drawnCard)} "
                + $"from {(drawAction.Type == EnemyAIActionType.DrawFromDiscard ? "the discard pile" : "the deck")}. "
                + $"Hand now contains {player.Hand.Count} card(s).",
                this);
        }

        private IEnumerator PlayAICombos(PlayerState player, AIController aiController)
        {
            int comboLimit = aiController.MaxCombosPerTurn;
            int combosPlayed = 0;
            Debug.Log(
                $"[AI Play] {GetPlayerLabel(player)} may play up to {comboLimit} combo(s) this turn.",
                this);

            while (combosPlayed < comboLimit && player.Hand.Count > 0)
            {
                EnemyAIAction action = aiController.ChooseAction(
                    player,
                    aiController.GetRemainingAIDeckCards(),
                    null,
                    canDraw: false,
                    canPlayCombos: true,
                    canDiscard: false);
                if (action == null || action.Type != EnemyAIActionType.PlayCombo)
                {
                    Debug.Log($"[AI Play] {GetPlayerLabel(player)} has no more valid combos.", this);
                    break;
                }

                List<CardInstance> comboCards = action.Cards.ToList();
                ComboValidationResult validation = TryCreateCombo(player, comboCards, action.ComboType);
                if (!validation.IsValid)
                {
                    Debug.LogWarning(
                        $"[AI Play] Could not play the selected {action.ComboType}: {validation.Message}",
                        this);
                    break;
                }

                foreach (CardInstance card in comboCards)
                {
                    player.Hand.Remove(card);
                }

                combosPlayed++;
                Debug.Log(
                    $"[AI Play] {GetPlayerLabel(player)} played {action.ComboType} "
                    + $"({string.Join(", ", comboCards.Select(FormatCardName))}). "
                    + $"Combos this turn: {combosPlayed}/{comboLimit}; hand: {player.Hand.Count}.",
                    this);

                if (enemyHandDisplay != null)
                {
                    if (enemyHandDisplay.StageCombo(comboCards))
                    {
                        enemyHandDisplay.RefreshTotalScore(player, activeCombos);
                        yield return new WaitForSeconds(aiPhaseDisplayDelay);
                        if (!enemyHandDisplay.PlayStagedCombo(comboCards, action.ComboType))
                        {
                            Debug.LogError(
                                $"[AI Play] Could not transfer {action.ComboType} visuals to the shared table.",
                                this);
                        }
                    }
                    else
                    {
                        Debug.LogError(
                            $"[AI Play] Could not stage {action.ComboType} visuals in the enemy scoring area.",
                            this);
                    }
                }

                if (player.Hand.Count == 0)
                {
                    Debug.Log($"[AI Play] {GetPlayerLabel(player)} has no cards left. Ending the match.", this);
                    ResolveMatchEnd();
                    yield break;
                }
            }
        }

        private void SetAIPhase(PlayerState player, TurnPhase phase)
        {
            currentPhase = phase;
            UpdatePhaseDisplay();
            Debug.Log($"[AI Turn] {GetPlayerLabel(player)} entered {phase} phase.", this);
            eventDispatcher.Publish(new PhaseChangedEvent(currentPhase, player, currentTurnNumber));
            if (enemyHandDisplay != null)
            {
                enemyHandDisplay.SynchronizeHand(player);
            }
        }

        private static string FormatCardName(CardInstance card)
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

        /// Ends the match and publishes the match-ended event.
        public void ResolveMatchEnd()
        {
            if (matchEnded)
            {
                return;
            }

            matchEnded = true;
            currentPhase = TurnPhase.GameOver;
            UpdatePhaseDisplay();

            foreach (var player in players)
            {
                foreach (var charm in player.Charms)
                {
                    charm?.Charm.ApplyToPlayer(player, this);
                }
            }

            foreach (var combo in activeCombos)
            {
                RevalidateCombo(combo);
            }

            var winner = DetermineWinner();
            eventDispatcher.Publish(new MatchEndedEvent(winner, players, activeCombos));
        }

        /// Updates the Unity text that describes the current phase.
        private void UpdatePhaseDisplay()
        {
            if (phaseDisplayText == null)
            {
                return;
            }

            string phaseText = currentPhase switch
            {
                TurnPhase.Draw => drawPhaseText,
                TurnPhase.Play => playPhaseText,
                TurnPhase.Discard => discardPhaseText,
                TurnPhase.ResolveTurn => resolveTurnPhaseText,
                TurnPhase.GameOver => gameOverPhaseText,
                TurnPhase.AITurn => string.IsNullOrWhiteSpace(aiTurnPhaseText)
                    ? "AI Turn"
                    : aiTurnPhaseText,
                _ => currentPhase.ToString()
            };
            phaseDisplayText.text = phaseText;
        }

        /// Revalidates an existing combo and updates its validity and lock state.
        public void RevalidateCombo(Combo combo)
        {
            if (combo == null)
            {
                return;
            }

            ComboValidationResult validation = comboValidator.CanCreateCombo(
                combo.Cards,
                combo.Type,
                combo.Owner ?? ActivePlayer,
                this);
            combo.IsValid = validation.IsValid;
            combo.ScoringContext = validation.ScoringContext;
            combo.Score = validation.IsValid
                ? CalculateComboScore(combo, validation.ScoringContext)
                : 0f;
        }

        /// Calculates the final score for a combo.
        public float CalculateComboScore(Combo combo)
        {
            if (combo == null || combo.Cards == null || combo.Cards.Count == 0)
            {
                return 0f;
            }

            ComboValidationResult validation = comboValidator.CanCreateCombo(
                combo.Cards,
                combo.Type,
                combo.Owner ?? ActivePlayer,
                this);
            if (!validation.IsValid)
            {
                combo.ScoringContext = null;
                return 0f;
            }

            combo.ScoringContext = validation.ScoringContext;
            return CalculateComboScore(combo, validation.ScoringContext);
        }

        /// Applies the configured base score multiplier to a validated combo.
        private float CalculateComboScore(Combo combo, ComboScoringContext scoringContext)
        {
            float baseScore = combo.Cards.Sum(card => scoringContext.GetCardScore(card, combo.Cards));
            if (combo.Type == ComboType.Straight)
            {
                baseScore *= 2f;
            }

            var flushMultiplier = combo.Cards.All(card => card.Suit == combo.Cards[0].Suit) ? 1.5f : 1f;
            return baseScore * flushMultiplier * baseScoreMultiplier;
        }

        /// Evaluates win conditions after an action changes match state.
        public void CheckWinConditionAfterAction(PlayerState player)
        {
            if (player == null || player.Hand == null)
            {
                return;
            }

            if (player.Hand.Count == 0)
            {
                ResolveMatchEnd();
            }
        }

        /// Advances the active-player index and starts that player's turn.
        public void AdvanceToNextPlayer()
        {
            if (players == null || players.Count == 0)
            {
                return;
            }

            activePlayerIndex = (activePlayerIndex + 1) % players.Count;
            eventDispatcher.Publish(new ActivePlayerChangedEvent(ActivePlayer, activePlayerIndex));
        }

        /// Resolves turn-start and turn-end status effects.
        private void ResolveTurnBasedEffects()
        {
            var player = ActivePlayer;
            if (player == null)
            {
                return;
            }

            foreach (var card in player.Hand)
            {
                foreach (var statusEffect in card.StatusEffects)
                {
                    statusEffect?.Effect.OnTurnStart(player, this);
                }
            }
        }

        /// Draws and creates an instance from the next card in the deck.
        private CardInstance DrawFromDeck()
        {
            return new CardInstance
            {
                InstanceId = Guid.NewGuid().ToString(),
                Rank = 1,
                Suit = Suit.Skulls,
                Type = CardType.Number
            };
        }

        /// Returns the top card from the discard pile.
        private CardInstance DrawFromDiscardPile()
        {
            DiscardManager discardManager = FindFirstObjectByType<DiscardManager>();
            if (discardManager == null)
            {
                Debug.LogError("Cannot draw from the discard pile: no DiscardManager exists in the scene.", this);
                return null;
            }

            Card topCard = discardManager.DrawTopCardFromDiscardPile();
            if (topCard == null)
            {
                Debug.LogWarning("Cannot draw from the discard pile: it has no card on top.", this);
                return null;
            }

            return CreateCardInstance(topCard, Guid.NewGuid().ToString());
        }

        /// Determines the winning player or returns null if none has won.
        private PlayerState DetermineWinner()
        {
            if (players == null || players.Count == 0)
            {
                return null;
            }

            return players.OrderByDescending(player => player.Hand.Count == 0 ? 1 : 0)
                .ThenByDescending(player => activeCombos.Where(combo => combo.Owner == player).Sum(combo => combo.Score))
                .FirstOrDefault();
        }
    }

    public sealed class MatchStartedEvent
    {
        /// Players included in the newly initialized match.
        public List<PlayerState> Players { get; }

        public MatchStartedEvent(List<PlayerState> players)
        {
            Players = players ?? new List<PlayerState>();
        }
    }

    public sealed class TurnStartedEvent
    {
        /// Player whose turn is starting.
        public PlayerState Player { get; }
        /// Current turn number.
        public int TurnNumber { get; }

        public TurnStartedEvent(PlayerState player, int turnNumber)
        {
            Player = player;
            TurnNumber = turnNumber;
        }
    }

    public sealed class PhaseChangedEvent
    {
        /// New turn phase.
        public TurnPhase Phase { get; }
        /// Player affected by the phase change.
        public PlayerState Player { get; }
        /// Current turn number.
        public int TurnNumber { get; }

        public PhaseChangedEvent(TurnPhase phase, PlayerState player, int turnNumber)
        {
            Phase = phase;
            Player = player;
            TurnNumber = turnNumber;
        }
    }

    public sealed class CardDrawnEvent
    {
        /// Player who drew the card.
        public PlayerState Player { get; }
        /// Card instance that was drawn.
        public CardInstance Card { get; }
        /// True when the card came from the discard pile.
        public bool FromDiscard { get; }

        public CardDrawnEvent(PlayerState player, CardInstance card, bool fromDiscard)
        {
            Player = player;
            Card = card;
            FromDiscard = fromDiscard;
        }
    }

    public sealed class ComboCreatedEvent
    {
        /// Newly created combo.
        public Combo Combo { get; }
        /// Player who created the combo.
        public PlayerState Player { get; }

        public ComboCreatedEvent(Combo combo, PlayerState player)
        {
            Combo = combo;
            Player = player;
        }
    }

    public sealed class ComboModifiedEvent
    {
        /// Modified combo.
        public Combo Combo { get; }
        /// Player who modified the combo.
        public PlayerState Player { get; }
        /// Card added to the combo.
        public CardInstance Card { get; }

        public ComboModifiedEvent(Combo combo, PlayerState player, CardInstance card)
        {
            Combo = combo;
            Player = player;
            Card = card;
        }
    }

    public sealed class CardDiscardedEvent
    {
        /// Player discarding the card.
        public PlayerState Player { get; }
        /// Card instance being discarded.
        public CardInstance Card { get; }

        public CardDiscardedEvent(PlayerState player, CardInstance card)
        {
            Player = player;
            Card = card;
        }
    }

    public sealed class ActivePlayerChangedEvent
    {
        /// Newly active player.
        public PlayerState Player { get; }
        /// Index of the active player.
        public int PlayerIndex { get; }

        public ActivePlayerChangedEvent(PlayerState player, int playerIndex)
        {
            Player = player;
            PlayerIndex = playerIndex;
        }
    }

    public sealed class TurnResolvedEvent
    {
        /// Player whose turn was resolved.
        public PlayerState Player { get; }
        /// Resolved turn number.
        public int TurnNumber { get; }

        public TurnResolvedEvent(PlayerState player, int turnNumber)
        {
            Player = player;
            TurnNumber = turnNumber;
        }
    }

    public sealed class MatchEndedEvent
    {
        /// Winner of the match, or null if no winner was determined.
        public PlayerState Winner { get; }
        /// All players that participated.
        public List<PlayerState> Players { get; }
        /// Combos active when the match ended.
        public List<Combo> Combos { get; }

        public MatchEndedEvent(PlayerState winner, List<PlayerState> players, List<Combo> combos)
        {
            Winner = winner;
            Players = players ?? new List<PlayerState>();
            Combos = combos ?? new List<Combo>();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace WildCardStudio
{
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

        [Header("Phase Display")]
        [SerializeField] private TMP_Text phaseDisplayText;
        [SerializeField] private string drawPhaseText = "Draw";
        [SerializeField] private string playPhaseText = "Play";
        [SerializeField] private string discardPhaseText = "Discard";
        [SerializeField] private string resolveTurnPhaseText = "Resolve Turn";
        [SerializeField] private string gameOverPhaseText = "Game Over";

        [Header("Systems")]
        private IComboValidator comboValidator;
        private EventDispatcher eventDispatcher;

        [Header("Scoring")]
        [SerializeField] private float baseScoreMultiplier = 1f;

        [Header("Starting Hand")]
        [SerializeField, Min(0)] private int startingHandSize = 10;

        private readonly List<Combo> activeCombos = new List<Combo>();

        public TurnPhase CurrentPhase => currentPhase;
        public int ActivePlayerIndex => activePlayerIndex;
        public PlayerState ActivePlayer => players != null && players.Count > 0 ? players[activePlayerIndex] : null;
        public bool MatchEnded => matchEnded;
        public bool EnterAdvancesPhaseEnabled => enterAdvancesPhase;
        public EventDispatcher Events => eventDispatcher;
        public IReadOnlyList<PlayerState> Players => players;
        public IReadOnlyList<Combo> ActiveCombos => activeCombos;
        /// Starting hand size used when a match initializes.
        public int StartingHandSize => Mathf.Max(0, startingHandSize);

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

            UpdatePhaseDisplay();
        }

        /// Handles Enter and Space shortcuts for advancing or completing the discard phase.
        private void Update()
        {
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

        /// Starts a new turn for the active player.
        public void StartTurn()
        {
            if (matchEnded)
            {
                return;
            }

            currentPhase = TurnPhase.Draw;
            UpdatePhaseDisplay();
            eventDispatcher.Publish(new TurnStartedEvent(ActivePlayer, currentTurnNumber));
            ResolveTurnBasedEffects();
        }

        /// Advances through draw, play, discard, resolution, and game-over phases.
        public void AdvancePhase()
        {
            if (matchEnded)
            {
                return;
            }

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
                case TurnPhase.GameOver:
                    return;
                default:
                    currentPhase = TurnPhase.Draw;
                    break;
            }

            UpdatePhaseDisplay();
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

            foreach (var combo in activeCombos)
            {
                RevalidateCombo(combo);
            }

            currentTurnNumber++;
            AdvanceToNextPlayer();
            currentPhase = TurnPhase.Draw;
            UpdatePhaseDisplay();
            eventDispatcher.Publish(new TurnResolvedEvent(ActivePlayer, currentTurnNumber));
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

            phaseDisplayText.text = currentPhase switch
            {
                TurnPhase.Draw => drawPhaseText,
                TurnPhase.Play => playPhaseText,
                TurnPhase.Discard => discardPhaseText,
                TurnPhase.ResolveTurn => resolveTurnPhaseText,
                TurnPhase.GameOver => gameOverPhaseText,
                _ => currentPhase.ToString()
            };
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
            return new CardInstance
            {
                InstanceId = Guid.NewGuid().ToString(),
                Rank = 2,
                Suit = Suit.Candles,
                Type = CardType.Number
            };
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

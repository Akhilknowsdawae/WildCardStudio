using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WildCardStudio
{
    public class GameController : MonoBehaviour
    {
        [Header("Players")]
        [SerializeField] private List<PlayerState> players = new List<PlayerState>();

        [Header("Core Rules")]
        [SerializeField] private TurnPhase currentPhase = TurnPhase.Draw;
        [SerializeField] private int activePlayerIndex;
        [SerializeField] private int currentTurnNumber = 1;
        [SerializeField] private bool matchEnded;

        [Header("Systems")]
        private IComboValidator comboValidator;
        private EventDispatcher eventDispatcher;

        [Header("Scoring")]
        [SerializeField] private float baseScoreMultiplier = 1f;

        private readonly List<Combo> activeCombos = new List<Combo>();

        public TurnPhase CurrentPhase => currentPhase;
        public int ActivePlayerIndex => activePlayerIndex;
        public PlayerState ActivePlayer => players != null && players.Count > 0 ? players[activePlayerIndex] : null;
        public bool MatchEnded => matchEnded;
        public EventDispatcher Events => eventDispatcher;
        public IReadOnlyList<PlayerState> Players => players;
        public IReadOnlyList<Combo> ActiveCombos => activeCombos;

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
        }

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
            activeCombos.Clear();

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

        public void StartTurn()
        {
            if (matchEnded)
            {
                return;
            }

            currentPhase = TurnPhase.Draw;
            eventDispatcher.Publish(new TurnStartedEvent(ActivePlayer, currentTurnNumber));
            ResolveTurnBasedEffects();
        }

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

            eventDispatcher.Publish(new PhaseChangedEvent(currentPhase, ActivePlayer, currentTurnNumber));
        }

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
                Score = 0,
                IsValid = true
            };

            activeCombos.Add(combo);
            eventDispatcher.Publish(new ComboCreatedEvent(combo, player));
            return ComboValidationResult.Success();
        }

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
            return ComboValidationResult.Success();
        }

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
            eventDispatcher.Publish(new TurnResolvedEvent(ActivePlayer, currentTurnNumber));
        }

        public void ResolveMatchEnd()
        {
            if (matchEnded)
            {
                return;
            }

            matchEnded = true;
            currentPhase = TurnPhase.GameOver;

            foreach (var player in players)
            {
                foreach (var charm in player.Charms)
                {
                    charm?.Charm.ApplyToPlayer(player, this);
                }
            }

            foreach (var combo in activeCombos)
            {
                combo.Score = CalculateComboScore(combo);
            }

            var winner = DetermineWinner();
            eventDispatcher.Publish(new MatchEndedEvent(winner, players, activeCombos));
        }

        public void RevalidateCombo(Combo combo)
        {
            if (combo == null)
            {
                return;
            }

            combo.IsValid = comboValidator.IsValid(combo, combo.Owner ?? ActivePlayer, this);
            combo.Score = CalculateComboScore(combo);
        }

        public float CalculateComboScore(Combo combo)
        {
            if (combo == null || combo.Cards == null || combo.Cards.Count == 0)
            {
                return 0f;
            }

            var baseScore = combo.Type == ComboType.Set
                ? combo.Cards.Sum(card => card.EffectiveValue)
                : combo.Cards.Sum(card => card.EffectiveValue) * 2;

            var flushMultiplier = combo.Cards.All(card => card.Suit == combo.Cards[0].Suit) ? 1.5f : 1f;
            return baseScore * flushMultiplier * baseScoreMultiplier;
        }

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

        public void AdvanceToNextPlayer()
        {
            if (players == null || players.Count == 0)
            {
                return;
            }

            activePlayerIndex = (activePlayerIndex + 1) % players.Count;
            eventDispatcher.Publish(new ActivePlayerChangedEvent(ActivePlayer, activePlayerIndex));
        }

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

        private CardInstance DrawFromDeck()
        {
            return new CardInstance
            {
                InstanceId = Guid.NewGuid().ToString(),
                Rank = 1,
                Suit = Suit.Clubs,
                Type = CardType.Number
            };
        }

        private CardInstance DrawFromDiscardPile()
        {
            return new CardInstance
            {
                InstanceId = Guid.NewGuid().ToString(),
                Rank = 2,
                Suit = Suit.Diamonds,
                Type = CardType.Number
            };
        }

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
        public List<PlayerState> Players { get; }

        public MatchStartedEvent(List<PlayerState> players)
        {
            Players = players ?? new List<PlayerState>();
        }
    }

    public sealed class TurnStartedEvent
    {
        public PlayerState Player { get; }
        public int TurnNumber { get; }

        public TurnStartedEvent(PlayerState player, int turnNumber)
        {
            Player = player;
            TurnNumber = turnNumber;
        }
    }

    public sealed class PhaseChangedEvent
    {
        public TurnPhase Phase { get; }
        public PlayerState Player { get; }
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
        public PlayerState Player { get; }
        public CardInstance Card { get; }
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
        public Combo Combo { get; }
        public PlayerState Player { get; }

        public ComboCreatedEvent(Combo combo, PlayerState player)
        {
            Combo = combo;
            Player = player;
        }
    }

    public sealed class ComboModifiedEvent
    {
        public Combo Combo { get; }
        public PlayerState Player { get; }
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
        public PlayerState Player { get; }
        public CardInstance Card { get; }

        public CardDiscardedEvent(PlayerState player, CardInstance card)
        {
            Player = player;
            Card = card;
        }
    }

    public sealed class ActivePlayerChangedEvent
    {
        public PlayerState Player { get; }
        public int PlayerIndex { get; }

        public ActivePlayerChangedEvent(PlayerState player, int playerIndex)
        {
            Player = player;
            PlayerIndex = playerIndex;
        }
    }

    public sealed class TurnResolvedEvent
    {
        public PlayerState Player { get; }
        public int TurnNumber { get; }

        public TurnResolvedEvent(PlayerState player, int turnNumber)
        {
            Player = player;
            TurnNumber = turnNumber;
        }
    }

    public sealed class MatchEndedEvent
    {
        public PlayerState Winner { get; }
        public List<PlayerState> Players { get; }
        public List<Combo> Combos { get; }

        public MatchEndedEvent(PlayerState winner, List<PlayerState> players, List<Combo> combos)
        {
            Winner = winner;
            Players = players ?? new List<PlayerState>();
            Combos = combos ?? new List<Combo>();
        }
    }
}

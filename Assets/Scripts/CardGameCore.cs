using System;
using System.Collections.Generic;
using System.Linq;
using LifeAfter;

namespace WildCardStudio
{
    /// Suits available to number and special cards.
    public enum Suit
    {
        Skulls,
        Candles,
        Bones,
        Bells
    }

    /// Card categories used by combo and scoring rules.
    public enum CardType
    {
        Number,
        King,
        Queen,
        Jack
    }

    /// Types of valid card combinations.
    public enum ComboType
    {
        Set,
        Straight
    }

    /// Ordered phases of a player's turn.
    public enum TurnPhase
    {
        Draw,
        Play,
        Discard,
        ResolveTurn,
        GameOver
    }

    /// Reasons a combo validation operation may fail.
    public enum ComboValidationReason
    {
        None,
        InsufficientCards,
        InvalidSequence,
        ComboLocked,
        CardNotOwned,
        InvalidComboType,
        InvalidRank,
        IllegalCardTransfer,
        NotPlayerTurn,
        GameOver,
        InvalidAction
    }

    /// Result of attempting to validate or create a combo.
    public struct ComboValidationResult
    {
        public bool IsValid;
        public ComboValidationReason Reason;
        public string Message;
        public ComboScoringContext ScoringContext;

        /// Creates a successful validation result with optional scoring substitutions.
        public static ComboValidationResult Success(ComboScoringContext scoringContext = null)
        {
            return new ComboValidationResult
            {
                IsValid = true,
                Reason = ComboValidationReason.None,
                Message = string.Empty,
                ScoringContext = scoringContext ?? new ComboScoringContext()
            };
        }

        /// Creates a failed validation result with a reason and user-facing message.
        public static ComboValidationResult Failure(ComboValidationReason reason, string message)
        {
            return new ComboValidationResult
            {
                IsValid = false,
                Reason = reason,
                Message = message,
                ScoringContext = null
            };
        }
    }

    /// Defines a card-specific effect that can be applied during gameplay.
    public interface ICardEffect
    {
        string EffectId { get; }
        void Apply(CardInstance card, Combo targetCombo, PlayerState actingPlayer, GameController controller);
    }

    /// Defines turn, combo, and status hooks for status effects.
    public interface IStatusEffect
    {
        string EffectId { get; }
        void OnTurnStart(PlayerState owner, GameController controller);
        void OnTurnEnd(PlayerState owner, GameController controller);
        void OnComboValidated(Combo combo, PlayerState owner, GameController controller);
    }

    /// Defines persistent player modifiers and combo-validation modifications.
    public interface ICharm
    {
        string CharmId { get; }
        void ApplyToPlayer(PlayerState owner, GameController controller);
        ComboValidationResult ModifyValidation(ComboValidationResult baseResult, Combo combo, CardInstance card, PlayerState player, GameController controller);
    }

    /// Defines the combo validation operations used by the game.
    public interface IComboValidator
    {
        ComboValidationResult CanCreateCombo(IReadOnlyList<CardInstance> cards, ComboType comboType, PlayerState player, GameController controller = null);
        ComboValidationResult CanAddCard(Combo combo, CardInstance card, PlayerState player, GameController controller = null);
        bool IsValid(Combo combo, PlayerState player, GameController controller = null);
    }

    /// Dispatches typed game events to subscribed handlers.
    public sealed class EventDispatcher
    {
        private readonly Dictionary<Type, Delegate> _subscribers = new Dictionary<Type, Delegate>();

        /// Registers a handler for a specific event type.
        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null)
            {
                return;
            }

            var key = typeof(T);
            if (_subscribers.TryGetValue(key, out var existing))
            {
                _subscribers[key] = Delegate.Combine(existing, handler);
            }
            else
            {
                _subscribers[key] = handler;
            }
        }

        /// Removes a previously registered event handler.
        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null)
            {
                return;
            }

            var key = typeof(T);
            if (_subscribers.TryGetValue(key, out var existing))
            {
                _subscribers[key] = Delegate.Remove(existing, handler);
            }
        }

        /// Sends an event to all handlers registered for its type.
        public void Publish<T>(T message)
        {
            if (!_subscribers.TryGetValue(typeof(T), out var handler))
            {
                return;
            }

            var callback = (Action<T>)handler;
            callback?.Invoke(message);
        }
    }

    /// Holds a status effect implementation and its runtime identifier.
    public sealed class StatusEffectInstance
    {
        public string Id;
        public IStatusEffect Effect;

        public StatusEffectInstance(string id, IStatusEffect effect)
        {
            Id = id;
            Effect = effect;
        }
    }

    /// Holds a charm implementation and its display name.
    public sealed class CharmInstance
    {
        public string Name;
        public ICharm Charm;

        public CharmInstance(string name, ICharm charm)
        {
            Name = name;
            Charm = charm;
        }
    }

    /// Runtime representation of a drawn card instance.
    public sealed class CardInstance
    {
        public string InstanceId;
        public Card Definition;
        public int Rank;
        public Suit Suit;
        public CardType Type;
        public List<StatusEffectInstance> StatusEffects = new List<StatusEffectInstance>();

        /// True when the card is a Queen, King, or Jack.
        public bool IsSpecialCard => Type == CardType.Queen || Type == CardType.King || Type == CardType.Jack;

        /// Numeric value used when the card is not special.
        public int EffectiveValue
        {
            get { return IsSpecialCard ? 0 : Rank; }
        }
    }

    /// Stores rank substitutions assigned to special cards during combo validation.
    public sealed class ComboScoringContext
    {
        private readonly Dictionary<CardInstance, int> substitutedRanks =
            new Dictionary<CardInstance, int>();

        public IReadOnlyDictionary<CardInstance, int> Substitutions => substitutedRanks;

        /// Attempts to retrieve the substituted rank for a special card.
        public bool TryGetSubstitutedRank(CardInstance card, out int rank)
        {
            return substitutedRanks.TryGetValue(card, out rank);
        }

        /// Calculates this card's contribution to the combo score.
        public int GetCardScore(CardInstance card, IReadOnlyList<CardInstance> comboCards)
        {
            if (card == null || comboCards == null)
            {
                return 0;
            }

            if (card.Type == CardType.Jack)
            {
                return comboCards.Count(comboCard => comboCard != null && comboCard.Suit == card.Suit);
            }

            if (TryGetSubstitutedRank(card, out int substitutedRank))
            {
                if (card.Type == CardType.Queen)
                {
                    return substitutedRank / 2;
                }

                if (card.Type == CardType.King)
                {
                    return (substitutedRank + 1) / 2;
                }
            }

            return card.EffectiveValue;
        }

        internal void AddSubstitution(CardInstance card, int rank)
        {
            substitutedRanks.Add(card, rank);
        }

        internal void RemoveSubstitution(CardInstance card)
        {
            substitutedRanks.Remove(card);
        }
    }

    /// Runtime state for a player participating in the match.
    public sealed class PlayerState
    {
        public string PlayerId;
        public string Name;
        public List<CardInstance> Hand = new List<CardInstance>();
        public List<CharmInstance> Charms = new List<CharmInstance>();
        public bool IsHuman;

        /// True when the player still has cards in hand.
        public bool HasCardsRemaining => Hand != null && Hand.Count > 0;
    }

    /// Runtime representation of a validated combo owned by a player.
    public sealed class Combo
    {
        public string ComboId;
        public ComboType Type;
        public List<CardInstance> Cards = new List<CardInstance>();
        public ComboScoringContext ScoringContext = new ComboScoringContext();
        public PlayerState Owner;
        public bool IsLocked;
        public bool IsValid;
        public float Score;
    }

    /// Implements set and straight validation and combo-addition rules.
    public sealed class BasicComboValidator : IComboValidator
    {
        /// Minimum number of cards required to form a set.
        public int MinimumSetCount { get; set; } = 2;
        /// Minimum number of cards required to form a straight.
        public int MinimumStraightCount { get; set; } = 4;
        /// Enables Queen, King, and Jack substitutions in valid combos.
        public bool AllowSpecialCardSubstitutions { get; set; } = true;

        /// Validates and creates the scoring context for a proposed set or straight.
        public ComboValidationResult CanCreateCombo(IReadOnlyList<CardInstance> cards, ComboType comboType, PlayerState player, GameController controller = null)
        {
            if (cards == null || cards.Count == 0)
            {
                return ComboValidationResult.Failure(ComboValidationReason.InsufficientCards, "Combo cannot be empty.");
            }

            if (cards.Any(card => card == null))
            {
                return ComboValidationResult.Failure(ComboValidationReason.InvalidRank, "A combo cannot contain a missing card.");
            }

            if (cards.Distinct().Count() != cards.Count)
            {
                return ComboValidationResult.Failure(ComboValidationReason.InvalidRank, "A combo cannot contain the same card more than once.");
            }

            if (comboType == ComboType.Set)
            {
                if (cards.Count < MinimumSetCount)
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InsufficientCards, "Sets require at least two cards.");
                }

                if (!AllowSpecialCardSubstitutions && cards.Any(card => card.IsSpecialCard))
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InvalidRank, "Special-card substitutions are disabled.");
                }

                if (cards.Any(card => card.Type == CardType.Queen || card.Type == CardType.King))
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InvalidRank, "Queens and kings can only substitute in straights.");
                }

                var rankedCards = cards.Where(card => card.Type != CardType.Jack).ToList();
                int substitutedRank = rankedCards.Count > 0 ? rankedCards[0].Rank : 1;
                if (rankedCards.Any(card => card.Rank != substitutedRank))
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InvalidRank, "A set must contain cards with the same rank.");
                }

                var scoringContext = new ComboScoringContext();
                foreach (var card in cards.Where(card => card.Type == CardType.Jack))
                {
                    scoringContext.AddSubstitution(card, substitutedRank);
                }

                return ComboValidationResult.Success(scoringContext);
            }

            if (comboType == ComboType.Straight)
            {
                if (cards.Count < MinimumStraightCount)
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InsufficientCards, "Straights require at least four cards.");
                }

                if (!AllowSpecialCardSubstitutions && cards.Any(card => card.IsSpecialCard))
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InvalidSequence, "Special-card substitutions are disabled.");
                }

                return ValidateStraight(cards);
            }

            return ComboValidationResult.Failure(ComboValidationReason.InvalidComboType, "Unsupported combo type.");
        }

        private ComboValidationResult ValidateStraight(IReadOnlyList<CardInstance> cards)
        {
            List<CardInstance> rankedCards = cards.Where(card => !card.IsSpecialCard).ToList();
            HashSet<int> rankedValues = new HashSet<int>();
            foreach (CardInstance card in rankedCards)
            {
                if (!rankedValues.Add(card.Rank))
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InvalidSequence, "Straight cards must have distinct ranks.");
                }
            }

            int minimumStart = rankedCards.Count == 0
                ? 1
                : rankedCards.Max(card => card.Rank) - cards.Count + 1;
            int maximumStart = rankedCards.Count == 0
                ? 2
                : rankedCards.Min(card => card.Rank);

            // Prefer the highest-scoring valid replacement ranks when multiple straights fit.
            for (int startRank = maximumStart; startRank >= minimumStart; startRank--)
            {
                bool[] occupiedRanks = new bool[cards.Count];
                bool ranksFit = true;
                foreach (CardInstance card in rankedCards)
                {
                    int slot = card.Rank - startRank;
                    if (slot < 0 || slot >= occupiedRanks.Length || occupiedRanks[slot])
                    {
                        ranksFit = false;
                        break;
                    }

                    occupiedRanks[slot] = true;
                }

                if (!ranksFit)
                {
                    continue;
                }

                List<CardInstance> specialCards = cards
                    .Where(card => card.IsSpecialCard)
                    .OrderBy(card => card.Type == CardType.Jack ? 1 : 0)
                    .ToList();
                var scoringContext = new ComboScoringContext();
                if (TryAssignSpecialCards(specialCards, 0, startRank, occupiedRanks, scoringContext))
                {
                    return ComboValidationResult.Success(scoringContext);
                }
            }

            return ComboValidationResult.Failure(
                ComboValidationReason.InvalidSequence,
                "Straight must form a sequence with valid special-card substitutions.");
        }

        private static bool TryAssignSpecialCards(
            IReadOnlyList<CardInstance> specialCards,
            int specialCardIndex,
            int startRank,
            bool[] occupiedRanks,
            ComboScoringContext scoringContext)
        {
            if (specialCardIndex >= specialCards.Count)
            {
                return true;
            }

            CardInstance card = specialCards[specialCardIndex];
            for (int slot = occupiedRanks.Length - 1; slot >= 0; slot--)
            {
                if (occupiedRanks[slot])
                {
                    continue;
                }

                int substitutedRank = startRank + slot;
                if (card.Type == CardType.Queen && substitutedRank % 2 != 0
                    || card.Type == CardType.King && substitutedRank % 2 == 0)
                {
                    continue;
                }

                occupiedRanks[slot] = true;
                scoringContext.AddSubstitution(card, substitutedRank);
                if (TryAssignSpecialCards(specialCards, specialCardIndex + 1, startRank, occupiedRanks, scoringContext))
                {
                    return true;
                }

                scoringContext.RemoveSubstitution(card);
                occupiedRanks[slot] = false;
            }

            return false;
        }

        /// Validates a card addition against the existing combo and turn owner.
        public ComboValidationResult CanAddCard(Combo combo, CardInstance card, PlayerState player, GameController controller = null)
        {
            if (combo == null)
            {
                return ComboValidationResult.Failure(ComboValidationReason.InvalidAction, "Combo is missing.");
            }

            if (combo.IsLocked)
            {
                return ComboValidationResult.Failure(ComboValidationReason.ComboLocked, "This combo is locked and cannot receive new cards.");
            }

            if (card == null)
            {
                return ComboValidationResult.Failure(ComboValidationReason.InvalidAction, "Card is missing.");
            }

            if (player == null)
            {
                return ComboValidationResult.Failure(ComboValidationReason.NotPlayerTurn, "There is no acting player.");
            }

            if (combo.Owner != null && combo.Owner != player && !CanTransferFromOpponentCombo(player, combo, card))
            {
                return ComboValidationResult.Failure(ComboValidationReason.CardNotOwned, "The acting player cannot add cards to another player's combo.");
            }

            var nextCards = combo.Cards.ToList();
            nextCards.Add(card);
            var result = CanCreateCombo(nextCards, combo.Type, player, controller);
            if (!result.IsValid)
            {
                return result;
            }

            return result;
        }

        /// Checks whether an existing combo is valid under the current rules.
        public bool IsValid(Combo combo, PlayerState player, GameController controller = null)
        {
            if (combo == null)
            {
                return false;
            }

            if (combo.Cards == null || combo.Cards.Count == 0)
            {
                return false;
            }

            return CanCreateCombo(combo.Cards, combo.Type, player, controller).IsValid;
        }

        private bool CanTransferFromOpponentCombo(PlayerState player, Combo combo, CardInstance card)
        {
            if (player == null || combo == null || card == null)
            {
                return false;
            }

            if (combo.Owner == null)
            {
                return false;
            }

            return combo.Owner != player;
        }

    }
}

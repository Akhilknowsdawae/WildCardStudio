using System;
using System.Collections.Generic;
using System.Linq;

namespace WildCardStudio
{
    public enum Suit
    {
        Clubs,
        Diamonds,
        Hearts,
        Spades
    }

    public enum CardType
    {
        Number,
        Queen,
        King,
        Joker
    }

    public enum ComboType
    {
        Set,
        Straight
    }

    public enum TurnPhase
    {
        Draw,
        Play,
        Discard,
        ResolveTurn,
        GameOver
    }

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

    public struct ComboValidationResult
    {
        public bool IsValid;
        public ComboValidationReason Reason;
        public string Message;

        public static ComboValidationResult Success()
        {
            return new ComboValidationResult
            {
                IsValid = true,
                Reason = ComboValidationReason.None,
                Message = string.Empty
            };
        }

        public static ComboValidationResult Failure(ComboValidationReason reason, string message)
        {
            return new ComboValidationResult
            {
                IsValid = false,
                Reason = reason,
                Message = message
            };
        }
    }

    public interface ICardEffect
    {
        string EffectId { get; }
        void Apply(CardInstance card, Combo targetCombo, PlayerState actingPlayer, GameController controller);
    }

    public interface IStatusEffect
    {
        string EffectId { get; }
        void OnTurnStart(PlayerState owner, GameController controller);
        void OnTurnEnd(PlayerState owner, GameController controller);
        void OnComboValidated(Combo combo, PlayerState owner, GameController controller);
    }

    public interface ICharm
    {
        string CharmId { get; }
        void ApplyToPlayer(PlayerState owner, GameController controller);
        ComboValidationResult ModifyValidation(ComboValidationResult baseResult, Combo combo, CardInstance card, PlayerState player, GameController controller);
    }

    public interface IComboValidator
    {
        ComboValidationResult CanCreateCombo(IReadOnlyList<CardInstance> cards, ComboType comboType, PlayerState player, GameController controller = null);
        ComboValidationResult CanAddCard(Combo combo, CardInstance card, PlayerState player, GameController controller = null);
        bool IsValid(Combo combo, PlayerState player, GameController controller = null);
    }

    public sealed class EventDispatcher
    {
        private readonly Dictionary<Type, Delegate> _subscribers = new Dictionary<Type, Delegate>();

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

    public sealed class CardInstance
    {
        public string InstanceId;
        public int Rank;
        public Suit Suit;
        public CardType Type;
        public List<StatusEffectInstance> StatusEffects = new List<StatusEffectInstance>();

        public bool IsSpecialCard => Type == CardType.Queen || Type == CardType.King || Type == CardType.Joker;

        public int EffectiveValue
        {
            get
            {
                if (Type == CardType.Queen || Type == CardType.King)
                {
                    return Rank;
                }

                return Rank;
            }
        }
    }

    public sealed class PlayerState
    {
        public string PlayerId;
        public string Name;
        public List<CardInstance> Hand = new List<CardInstance>();
        public List<CharmInstance> Charms = new List<CharmInstance>();
        public bool IsHuman;

        public bool HasCardsRemaining => Hand != null && Hand.Count > 0;
    }

    public sealed class Combo
    {
        public string ComboId;
        public ComboType Type;
        public List<CardInstance> Cards = new List<CardInstance>();
        public PlayerState Owner;
        public bool IsLocked;
        public bool IsValid;
        public float Score;
    }

    public sealed class BasicComboValidator : IComboValidator
    {
        public int MinimumSetCount { get; set; } = 2;
        public int MinimumStraightCount { get; set; } = 4;
        public bool AllowSpecialCardSubstitutions { get; set; } = true;

        public ComboValidationResult CanCreateCombo(IReadOnlyList<CardInstance> cards, ComboType comboType, PlayerState player, GameController controller = null)
        {
            if (cards == null || cards.Count == 0)
            {
                return ComboValidationResult.Failure(ComboValidationReason.InsufficientCards, "Combo cannot be empty.");
            }

            if (comboType == ComboType.Set)
            {
                if (cards.Count < MinimumSetCount)
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InsufficientCards, "Sets require at least two cards.");
                }

                var rank = cards[0].Rank;
                if (cards.Any(card => card == null || card.Rank != rank))
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InvalidRank, "A set must contain cards with the same printed rank.");
                }

                return ComboValidationResult.Success();
            }

            if (comboType == ComboType.Straight)
            {
                if (cards.Count < MinimumStraightCount)
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InsufficientCards, "Straights require at least four cards.");
                }

                var ordered = cards
                    .OrderBy(card => card.Rank)
                    .Select(card => card)
                    .ToList();

                if (!IsValidStraight(ordered))
                {
                    return ComboValidationResult.Failure(ComboValidationReason.InvalidSequence, "Straight must be a valid numerical sequence.");
                }

                return ComboValidationResult.Success();
            }

            return ComboValidationResult.Failure(ComboValidationReason.InvalidComboType, "Unsupported combo type.");
        }

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

            return ComboValidationResult.Success();
        }

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

        private bool IsValidStraight(IReadOnlyList<CardInstance> orderedCards)
        {
            if (orderedCards.Count < MinimumStraightCount)
            {
                return false;
            }

            var uniqueRanks = orderedCards
                .Select(card => card.Rank)
                .Distinct()
                .OrderBy(rank => rank)
                .ToList();

            if (uniqueRanks.Count < orderedCards.Count)
            {
                return false;
            }

            for (var index = 1; index < orderedCards.Count; index++)
            {
                if (orderedCards[index].Rank != orderedCards[index - 1].Rank + 1)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

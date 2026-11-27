using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LifeAfter;
using WildCardStudio;

/// Controls entering the discard phase and removing selected cards from the scoring area.
public class DiscardManager : MonoBehaviour
{
    [SerializeField] private GameController gameController;
    [SerializeField] private List<Card> discardedCards = new List<Card>();
    [SerializeField, Min(1)] private int cardsToDiscard = 1;
    [SerializeField] private Transform discardPilePosition;
    [SerializeField, Min(0f)] private float verticalOffset = 10f;

    private readonly List<GameObject> discardedCardVisuals = new List<GameObject>();
    private GameObject cardPrefab;
    private Vector3 cardScale = Vector3.one;

    /// Read-only access to card assets that have been discarded.
    public IReadOnlyList<Card> DiscardedCards => discardedCards;
    public Transform DiscardPilePosition => discardPilePosition;
    public GameObject TopDiscardCardVisual => discardedCardVisuals.Count > 0
        ? discardedCardVisuals[discardedCardVisuals.Count - 1]
        : null;
    /// Required number of cards that must be selected to complete the discard.
    public int CardsToDiscard => Mathf.Max(1, cardsToDiscard);

    /// Builds visuals for any cards already assigned to the discard pile.
    private void Start()
    {
        HandManager handManager = FindFirstObjectByType<HandManager>();
        if (handManager != null)
        {
            cardPrefab = handManager.cardPrefab;
        }

        PlayManager playManager = FindFirstObjectByType<PlayManager>();
        if (playManager != null)
        {
            cardScale = playManager.CardScale;
        }

        if (discardPilePosition == null)
        {
            Debug.LogWarning("Discarded cards will not be displayed until a Discard Pile Position is assigned.", this);
            return;
        }

        if (cardPrefab == null)
        {
            Debug.LogError("Cannot display discarded cards: no card prefab is assigned to the HandManager.", this);
            return;
        }

        if (playManager == null)
        {
            Debug.LogError("Cannot size discarded cards: no PlayManager exists in the scene.", this);
            return;
        }

        RefreshDiscardPileVisuals();
    }

    /// Adds a card asset to the top of the discard pile.
    public bool AddCardToDiscardPile(Card card)
    {
        if (card == null)
        {
            Debug.LogError("Cannot add a missing card to the discard pile.", this);
            return false;
        }

        discardedCards.Add(card);
        RefreshDiscardPileVisuals();
        Debug.Log($"Added {card.cardName} to the discard pile.", this);
        return true;
    }

    /// Removes and returns the current top card from the discard pile.
    public Card DrawTopCardFromDiscardPile()
    {
        if (discardedCards.Count == 0)
        {
            return null;
        }

        int topIndex = discardedCards.Count - 1;
        Card topCard = discardedCards[topIndex];
        if (topCard == null)
        {
            Debug.LogWarning("The top discard-pile entry is missing its card asset.", this);
            return null;
        }

        discardedCards.RemoveAt(topIndex);
        RefreshDiscardPileVisuals();
        Debug.Log($"Drew {topCard.cardName} from the top of the discard pile.", this);
        return topCard;
    }

    /// Enters or completes the discard phase as appropriate.
    public bool HandleEnter(HandManager handManager, ScoreManager scoreManager)
    {
        if (gameController == null)
        {
            gameController = FindFirstObjectByType<GameController>();
        }

        if (gameController == null)
        {
            Debug.LogError("Cannot enter the discard phase: no GameController exists in the scene.", this);
            return false;
        }

        if (gameController.CurrentPhase != TurnPhase.Discard)
        {
            if (!gameController.EnterDiscardPhase())
            {
                Debug.LogWarning($"Cannot enter discard phase from {gameController.CurrentPhase}.", this);
                return false;
            }

            Debug.Log("Entered the discard phase. Select the required cards in the scoring area, then press Enter again.", this);
            return true;
        }

        if (!DiscardSelectedCards(scoreManager))
        {
            return false;
        }

        gameController.AdvancePhase();
        return true;
    }

    /// Validates and removes the configured number of cards from the scoring area.
    public bool DiscardSelectedCards(ScoreManager scoreManager)
    {
        if (scoreManager == null)
        {
            scoreManager = FindFirstObjectByType<ScoreManager>();
        }

        if (scoreManager == null || scoreManager.cardsInPlay == null)
        {
            Debug.LogError("Cannot discard scored cards: no ScoreManager or scoring list is available.", this);
            return false;
        }

        List<GameObject> cardsToDiscard = new List<GameObject>();
        foreach (GameObject card in scoreManager.cardsInPlay)
        {
            if (card != null)
            {
                cardsToDiscard.Add(card);
            }
        }

        Debug.Log(
            $"Discard requested: scoring list has {cardsToDiscard.Count} card(s); exactly {CardsToDiscard} required.",
            this);

        if (cardsToDiscard.Count != CardsToDiscard)
        {
            Debug.LogWarning($"The scoring list must contain exactly {CardsToDiscard} card(s) to discard.", this);
            return false;
        }

        List<Card> cardDataToDiscard = new List<Card>(cardsToDiscard.Count);
        foreach (GameObject selectedCard in cardsToDiscard)
        {
            CardDisplay cardDisplay = selectedCard.GetComponent<CardDisplay>();
            if (cardDisplay == null || cardDisplay.cardData == null)
            {
                Debug.LogError($"Cannot discard {selectedCard.name}: it is missing its CardDisplay or card data.", selectedCard);
                return false;
            }

            cardDataToDiscard.Add(cardDisplay.cardData);
        }

        for (int i = 0; i < cardsToDiscard.Count; i++)
        {
            GameObject selectedCard = cardsToDiscard[i];
            if (!scoreManager.RemovePlayedCard(selectedCard))
            {
                Debug.LogError($"Failed to remove {selectedCard.name} from the scoring list; discard was incomplete.", selectedCard);
                return false;
            }

            discardedCards.Add(cardDataToDiscard[i]);
            Debug.Log($"Discarded {cardDataToDiscard[i].name} from the scoring area.", selectedCard);
            Destroy(selectedCard);
        }

        RefreshDiscardPileVisuals();
        Debug.Log($"Discard complete. Discard pile now contains {discardedCards.Count} card(s).", this);
        return true;
    }

    /// Recreates the visual stack so the newest discard is visible at the top.
    private void RefreshDiscardPileVisuals()
    {
        foreach (GameObject visual in discardedCardVisuals)
        {
            if (visual != null)
            {
                Destroy(visual);
            }
        }

        discardedCardVisuals.Clear();
        if (discardPilePosition == null || cardPrefab == null)
        {
            return;
        }

        for (int i = 0; i < discardedCards.Count; i++)
        {
            Card card = discardedCards[i];
            if (card == null)
            {
                Debug.LogWarning($"Discard pile entry {i} has no card asset and cannot be displayed.", this);
                discardedCardVisuals.Add(null);
                continue;
            }

            GameObject visual = Instantiate(cardPrefab, discardPilePosition, false);
            visual.name = $"{card.cardName} Discard Visual";
            visual.transform.localPosition = Vector3.up * verticalOffset * i;
            visual.transform.localScale = cardScale;

            CardDisplay display = visual.GetComponent<CardDisplay>();
            if (display == null)
            {
                Debug.LogError("Cannot display a discarded card: the card prefab has no CardDisplay component.", visual);
                Destroy(visual);
                discardedCardVisuals.Add(null);
                continue;
            }

            display.cardData = card;
            display.UpdateCardDisplay();

            CardMovement movement = visual.GetComponent<CardMovement>();
            if (movement != null)
            {
                movement.enabled = false;
            }

            foreach (Graphic graphic in visual.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            visual.transform.SetAsLastSibling();
            discardedCardVisuals.Add(visual);
        }
    }
}

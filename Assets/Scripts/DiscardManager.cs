using System.Collections.Generic;
using UnityEngine;
using LifeAfter;
using WildCardStudio;

public class DiscardManager : MonoBehaviour
{
    [SerializeField] private GameController gameController;
    [SerializeField] private List<Card> discardedCards = new List<Card>();
    [SerializeField, Min(1)] private int cardsToDiscard = 1;

    public IReadOnlyList<Card> DiscardedCards => discardedCards;
    public int CardsToDiscard => Mathf.Max(1, cardsToDiscard);

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

        Debug.Log($"Discard complete. Discard pile now contains {discardedCards.Count} card(s).", this);
        return true;
    }
}

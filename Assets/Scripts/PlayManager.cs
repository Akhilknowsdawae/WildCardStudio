using System.Collections.Generic;
using UnityEngine;

public class PlayManager : MonoBehaviour
{
    [System.Serializable]
    private class PlayedCardSet
    {
        public List<GameObject> cards = new List<GameObject>();
    }

    public List<GameObject> cardsInPlay = new List<GameObject>();
    [SerializeField] private List<PlayedCardSet> playedCardSets = new List<PlayedCardSet>();

    [Header("Card Layout")]
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private Transform playedCardsParent;
    [SerializeField] private GameObject playPositionReference;
    [SerializeField] private float fanSpread = 7.55f;
    [SerializeField] private float cardSpacing = 100f;
    [SerializeField] private float spaceBetweenCardSets = 250f;
    [SerializeField] private float verticalSpacing = 100f;
    [SerializeField] private Vector3 cardScale = Vector3.one;
    [SerializeField] private bool updatePlayedCardPositionsContinuously;

    private void Update()
    {
        if (updatePlayedCardPositionsContinuously)
        {
            UpdatePlayedCardPositions();
        }
    }

    public void PlaySelectedCards(HandManager handManager)
    {
        if (handManager == null)
        {
            Debug.LogError("Cannot play selected cards: assign a HandManager.", this);
            return;
        }

        if (playPositionReference == null)
        {
            Debug.LogError("Cannot play selected cards: assign a Play Position Reference.", this);
            return;
        }

        List<GameObject> cardsToPlay = new List<GameObject>();
        if (scoreManager != null)
        {
            cardsToPlay.AddRange(scoreManager.cardsInPlay);
        }
        else
        {
            Debug.LogError("Cannot move scored cards: assign a ScoreManager in the PlayManager.", this);
        }

        foreach (GameObject card in handManager.cardsInHand)
        {
            CardMovement movement = card != null ? card.GetComponent<CardMovement>() : null;
            if (movement != null && movement.IsSelected && !cardsToPlay.Contains(card))
            {
                cardsToPlay.Add(card);
            }
        }

        AddPlayedCardSet(cardsToPlay);
    }

    public bool AddPlayedCard(GameObject card)
    {
        if (card == null)
        {
            Debug.LogError("Cannot add a null card to the play area.", this);
            return false;
        }

        if (cardsInPlay.Contains(card))
        {
            return true;
        }

        return AddPlayedCardSet(new List<GameObject> { card });
    }

    private bool AddPlayedCardSet(List<GameObject> cards)
    {
        if (playedCardsParent == null)
        {
            Debug.LogError("Cannot add played cards: assign a Played Cards Parent in the PlayManager.", this);
            return false;
        }

        PlayedCardSet newSet = new PlayedCardSet();
        foreach (GameObject card in cards)
        {
            if (card == null || cardsInPlay.Contains(card))
            {
                continue;
            }

            card.transform.SetParent(playedCardsParent, false);
            card.transform.SetAsLastSibling();
            cardsInPlay.Add(card);
            newSet.cards.Add(card);

            if (scoreManager != null)
            {
                scoreManager.RemovePlayedCard(card);
            }

            CardMovement movement = card.GetComponent<CardMovement>();
            if (movement != null)
            {
                movement.MarkAsPlayed(true);
            }
        }

        if (newSet.cards.Count > 0)
        {
            playedCardSets.Add(newSet);
            UpdatePlayedCardPositions();
        }

        return true;
    }

    private void UpdatePlayedCardPositions()
    {
        cardsInPlay.RemoveAll(card => card == null);
        if (cardsInPlay.Count == 0)
        {
            return;
        }

        foreach (GameObject card in cardsInPlay)
        {
            card.transform.localScale = cardScale;
        }

        playedCardSets.RemoveAll(set => set == null);
        foreach (PlayedCardSet set in playedCardSets)
        {
            if (set.cards == null)
            {
                set.cards = new List<GameObject>();
            }

            set.cards.RemoveAll(card => card == null || !cardsInPlay.Contains(card));
        }

        PlayedCardSet ungroupedCards = new PlayedCardSet();
        foreach (GameObject card in cardsInPlay)
        {
            bool isGrouped = false;
            foreach (PlayedCardSet set in playedCardSets)
            {
                if (set.cards.Contains(card))
                {
                    isGrouped = true;
                    break;
                }
            }

            if (!isGrouped)
            {
                ungroupedCards.cards.Add(card);
            }
        }

        if (ungroupedCards.cards.Count > 0)
        {
            playedCardSets.Add(ungroupedCards);
        }

        if (playedCardsParent == null || playPositionReference == null)
        {
            return;
        }

        Vector3 referencePosition = playedCardsParent.InverseTransformPoint(playPositionReference.transform.position);
        playedCardSets.RemoveAll(set => set.cards.Count == 0);
        float totalWidth = 0f;
        foreach (PlayedCardSet set in playedCardSets)
        {
            if (set.cards.Count == 0)
            {
                continue;
            }

            totalWidth += cardSpacing * (set.cards.Count - 1);
        }

        totalWidth += spaceBetweenCardSets * Mathf.Max(0, playedCardSets.Count - 1);
        float nextSetStart = -totalWidth / 2f;
        int nonEmptySetIndex = 0;
        foreach (PlayedCardSet set in playedCardSets)
        {
            int cardCount = set.cards.Count;
            if (cardCount == 0)
            {
                continue;
            }

            float setWidth = cardSpacing * (cardCount - 1);
            float setCenter = nextSetStart + setWidth / 2f;
            for (int i = 0; i < cardCount; i++)
            {
                float centeredIndex = i - (cardCount - 1) / 2f;
                float normalizedPosition = cardCount > 1 ? 2f * i / (cardCount - 1) - 1f : 0f;
                float verticalOffset = verticalSpacing * (1f - normalizedPosition * normalizedPosition);
                GameObject card = set.cards[i];
                card.transform.localScale = cardScale;
                card.transform.localRotation = Quaternion.Euler(0f, 0f, fanSpread * centeredIndex);
                card.transform.localPosition = referencePosition
                    + new Vector3(setCenter + cardSpacing * centeredIndex, verticalOffset, 0f);
            }

            nextSetStart += setWidth;
            nonEmptySetIndex++;
            if (nonEmptySetIndex < playedCardSets.Count)
            {
                nextSetStart += spaceBetweenCardSets;
            }
        }
    }
}

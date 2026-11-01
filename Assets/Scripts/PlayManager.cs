using System.Collections.Generic;
using UnityEngine;

public class PlayManager : MonoBehaviour
{
    public List<GameObject> cardsInPlay = new List<GameObject>();

    [Header("Card Layout")]
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private Transform playedCardsParent;
    [SerializeField] private GameObject playPositionReference;
    [SerializeField] private float fanSpread = 7.55f;
    [SerializeField] private float cardSpacing = 100f;
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

        MoveScoreManagerCards();

        List<GameObject> selectedCards = new List<GameObject>();
        foreach (GameObject card in handManager.cardsInHand)
        {
            CardMovement movement = card != null ? card.GetComponent<CardMovement>() : null;
            if (movement != null && movement.IsSelected)
            {
                selectedCards.Add(card);
            }
        }

        foreach (GameObject card in selectedCards)
        {
            if (AddPlayedCard(card))
            {
                card.GetComponent<CardMovement>().MarkAsPlayed();
                UpdatePlayedCardPositions();
            }
        }
    }

    private void MoveScoreManagerCards()
    {
        if (scoreManager == null)
        {
            Debug.LogError("Cannot move scored cards: assign a ScoreManager in the PlayManager.", this);
            return;
        }

        List<GameObject> scoredCards = new List<GameObject>(scoreManager.cardsInPlay);
        foreach (GameObject card in scoredCards)
        {
            if (AddPlayedCard(card))
            {
                scoreManager.RemovePlayedCard(card);
                CardMovement movement = card.GetComponent<CardMovement>();
                if (movement != null)
                {
                    movement.MarkAsPlayed();
                    UpdatePlayedCardPositions();
                }
            }
        }
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

        if (playedCardsParent == null)
        {
            Debug.LogError("Cannot add played card: assign a Played Cards Parent in the PlayManager.", this);
            return false;
        }

        card.transform.SetParent(playedCardsParent, false);
        card.transform.SetAsLastSibling();
        cardsInPlay.Add(card);
        UpdatePlayedCardPositions();
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

        if (playedCardsParent == null || playPositionReference == null)
        {
            return;
        }

        Vector3 referencePosition = playedCardsParent.InverseTransformPoint(playPositionReference.transform.position);
        int cardCount = cardsInPlay.Count;
        if (cardCount == 1)
        {
            cardsInPlay[0].transform.localRotation = Quaternion.identity;
            cardsInPlay[0].transform.localPosition = referencePosition;
            return;
        }

        for (int i = 0; i < cardCount; i++)
        {
            float centeredIndex = i - (cardCount - 1) / 2f;
            float normalizedPosition = 2f * i / (cardCount - 1) - 1f;
            float horizontalOffset = cardSpacing * centeredIndex;
            float verticalOffset = verticalSpacing * (1f - normalizedPosition * normalizedPosition);

            cardsInPlay[i].transform.localRotation = Quaternion.Euler(0f, 0f, fanSpread * centeredIndex);
            cardsInPlay[i].transform.localPosition = referencePosition + new Vector3(horizontalOffset, verticalOffset, 0f);
        }
    }
}

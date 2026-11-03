using System.Collections.Generic;
using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public List<GameObject> cardsInPlay = new List<GameObject>();

    [Header("Card Layout")]
    [SerializeField] private Transform playedCardsParent;
    [SerializeField] private GameObject playPositionReference;
    [SerializeField] private Vector3 playPosition = new Vector3(-600f, 400f, 0f);
    [SerializeField, Min(0f), Tooltip("Expands the play area downward from the play position reference, in UI units.")]
    private float playAreaPadding;
    [SerializeField] private float fanSpread = 7.55f;
    [SerializeField] private float cardSpacing = 100f;
    [SerializeField] private float verticalSpacing = 100f;
    [SerializeField] private bool updatePlayedCardPositionsContinuously;
    [SerializeField, Range(0f, 1f)] private float scoreCardsAlphaWhenHandDown = 0.25f;

    public float CardSpacing => cardSpacing;
    public float ScoreCardsAlphaWhenHandDown => scoreCardsAlphaWhenHandDown;

    private void Update()
    {
        if (updatePlayedCardPositionsContinuously)
        {
            UpdatePlayedCardPositions();
        }
    }

    public Vector3 GetPlayPosition(Transform relativeTo)
    {
        if (playPositionReference == null)
        {
            return playPosition;
        }

        Vector3 referencePosition = playPositionReference.transform.position;
        return relativeTo != null ? relativeTo.InverseTransformPoint(referencePosition) : referencePosition;
    }

    public bool IsPointerInPlayArea(Vector2 localPosition, Transform relativeTo)
    {
        float playAreaBoundary = GetPlayPosition(relativeTo).y - Mathf.Max(0f, playAreaPadding);
        return localPosition.y > playAreaBoundary;
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
            Debug.LogError("Cannot add played card: assign a Played Cards Parent in the ScoreManager.", this);
            return false;
        }

        card.transform.SetParent(playedCardsParent, false);
        card.transform.SetAsLastSibling();
        cardsInPlay.Add(card);
        UpdatePlayedCardPositions();
        RefreshCardDisplay(card);
        return true;
    }

    public bool RemovePlayedCard(GameObject card)
    {
        if (!cardsInPlay.Remove(card))
        {
            return false;
        }

        UpdatePlayedCardPositions();
        return true;
    }

    private static void RefreshCardDisplay(GameObject card)
    {
        CardDisplay cardDisplay = card.GetComponent<CardDisplay>();
        if (cardDisplay != null)
        {
            cardDisplay.UpdateCardDisplay();
        }
    }

    public void RefreshPlayedCardPositions()
    {
        UpdatePlayedCardPositions();
    }

    private void UpdatePlayedCardPositions()
    {
        cardsInPlay.RemoveAll(card => card == null);
        if (cardsInPlay.Count == 0 || playedCardsParent == null)
        {
            return;
        }

        int cardCount = cardsInPlay.Count;
        Vector3 referencePosition = GetPlayPosition(playedCardsParent);
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

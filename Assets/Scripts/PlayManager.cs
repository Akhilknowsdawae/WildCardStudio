using System.Collections.Generic;
using UnityEngine;
using LifeAfter;
using WildCardStudio;

public class PlayManager : MonoBehaviour
{
    private readonly BasicComboValidator comboValidator = new BasicComboValidator();

    private sealed class ValidComboCandidate
    {
        public List<int> cardIndices;
    }

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
    [SerializeField] private Vector3 firstRowPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 nextRowPositionOffset = new Vector3(0f, 160f, 0f);
    [SerializeField, Min(1)] private int maxCardSetsPerRow = 6;
    [SerializeField, Min(0f)] private float scaleReductionPerSet = 0.1f;
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
        CardMovement.HideInvalidComboIndicator();

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

        if (cardsToPlay.Count == 0)
        {
            return;
        }

        List<GameObject> invalidCards = GetCardsNotInValidCombos(cardsToPlay);
        if (invalidCards.Count > 0)
        {
            Debug.LogError(
                $"Cannot play cards: {invalidCards.Count} card(s) do not belong to a valid set or straight.",
                this);
            ShowInvalidComboIndicators(invalidCards);
            return;
        }

        AddPlayedCardSet(cardsToPlay);
    }

    private List<GameObject> GetCardsNotInValidCombos(List<GameObject> cards)
    {
        List<GameObject> cardsWithData = new List<GameObject>(cards.Count);
        List<CardInstance> cardInstances = new List<CardInstance>(cards.Count);
        List<GameObject> invalidCards = new List<GameObject>();
        foreach (GameObject card in cards)
        {
            CardDisplay cardDisplay = card != null ? card.GetComponent<CardDisplay>() : null;
            if (cardDisplay == null || cardDisplay.cardData == null)
            {
                Debug.LogError("Cannot validate played cards: a card is missing its CardDisplay or card data.", this);
                if (card != null)
                {
                    invalidCards.Add(card);
                }

                continue;
            }

            Card definition = cardDisplay.cardData;
            CardType cardType = definition.cardType != null && definition.cardType.Count > 0
                ? definition.cardType[0]
                : CardType.Number;
            Suit suit = definition.suit != null && definition.suit.Count > 0
                ? definition.suit[0]
                : default(Suit);

            cardInstances.Add(new CardInstance
            {
                InstanceId = card.GetInstanceID().ToString(),
                Definition = definition,
                Rank = definition.cardRank,
                Suit = suit,
                Type = cardType
            });
            cardsWithData.Add(card);
        }

        List<ValidComboCandidate> candidates = new List<ValidComboCandidate>();
        CollectValidComboCandidates(cardInstances, 0, new List<int>(), candidates);
        candidates.Sort((left, right) => right.cardIndices.Count.CompareTo(left.cardIndices.Count));

        bool[] available = new bool[cardInstances.Count];
        bool[] bestCoverage = new bool[cardInstances.Count];
        int bestCoverageCount = -1;
        FindBestComboCoverage(candidates, available, new bool[cardInstances.Count], 0, 0, bestCoverage, ref bestCoverageCount);

        for (int i = 0; i < bestCoverage.Length; i++)
        {
            if (!bestCoverage[i])
            {
                invalidCards.Add(cardsWithData[i]);
            }
        }

        return invalidCards;
    }

    private void CollectValidComboCandidates(
        List<CardInstance> cards,
        int startIndex,
        List<int> currentIndices,
        List<ValidComboCandidate> candidates)
    {
        if (currentIndices.Count >= Mathf.Min(comboValidator.MinimumSetCount, comboValidator.MinimumStraightCount))
        {
            List<CardInstance> candidateCards = new List<CardInstance>(currentIndices.Count);
            foreach (int index in currentIndices)
            {
                candidateCards.Add(cards[index]);
            }

            if (comboValidator.CanCreateCombo(candidateCards, ComboType.Set, null).IsValid
                || comboValidator.CanCreateCombo(candidateCards, ComboType.Straight, null).IsValid)
            {
                candidates.Add(new ValidComboCandidate
                {
                    cardIndices = new List<int>(currentIndices)
                });
            }
        }

        for (int i = startIndex; i < cards.Count; i++)
        {
            currentIndices.Add(i);
            CollectValidComboCandidates(cards, i + 1, currentIndices, candidates);
            currentIndices.RemoveAt(currentIndices.Count - 1);
        }
    }

    private void FindBestComboCoverage(
        List<ValidComboCandidate> candidates,
        bool[] unavailable,
        bool[] covered,
        int decidedCount,
        int coveredCount,
        bool[] bestCoverage,
        ref int bestCoverageCount)
    {
        if (coveredCount + unavailable.Length - decidedCount <= bestCoverageCount)
        {
            return;
        }

        int firstAvailableIndex = System.Array.FindIndex(unavailable, unavailableCard => !unavailableCard);
        if (firstAvailableIndex < 0)
        {
            bestCoverageCount = coveredCount;
            System.Array.Copy(covered, bestCoverage, covered.Length);
            return;
        }

        foreach (ValidComboCandidate candidate in candidates)
        {
            if (!candidate.cardIndices.Contains(firstAvailableIndex))
            {
                continue;
            }

            bool canUseCandidate = true;
            foreach (int index in candidate.cardIndices)
            {
                if (unavailable[index])
                {
                    canUseCandidate = false;
                    break;
                }
            }

            if (!canUseCandidate)
            {
                continue;
            }

            foreach (int index in candidate.cardIndices)
            {
                unavailable[index] = true;
                covered[index] = true;
            }

            FindBestComboCoverage(
                candidates,
                unavailable,
                covered,
                decidedCount + candidate.cardIndices.Count,
                coveredCount + candidate.cardIndices.Count,
                bestCoverage,
                ref bestCoverageCount);

            foreach (int index in candidate.cardIndices)
            {
                unavailable[index] = false;
                covered[index] = false;
            }
        }

        unavailable[firstAvailableIndex] = true;
        FindBestComboCoverage(
            candidates,
            unavailable,
            covered,
            decidedCount + 1,
            coveredCount,
            bestCoverage,
            ref bestCoverageCount);
        unavailable[firstAvailableIndex] = false;
    }

    private void ShowInvalidComboIndicators(List<GameObject> cards)
    {
        foreach (GameObject card in cards)
        {
            CardMovement movement = card != null ? card.GetComponent<CardMovement>() : null;
            if (movement == null)
            {
                Debug.LogError("Cannot show invalid combo feedback: a card is missing its CardMovement.", this);
                continue;
            }

            movement.ShowInvalidComboIndicator();
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

        playedCardSets.RemoveAll(set => set.cards.Count == 0);
        float scaleMultiplier = 1f / (1f + Mathf.Max(0f, scaleReductionPerSet) * (playedCardSets.Count - 1));
        Vector3 currentCardScale = cardScale * scaleMultiplier;
        int setsPerRow = Mathf.Max(1, maxCardSetsPerRow);
        foreach (GameObject card in cardsInPlay)
        {
            card.transform.localScale = currentCardScale;
        }

        if (playedCardsParent == null || playPositionReference == null)
        {
            return;
        }

        Vector3 referencePosition = playedCardsParent.InverseTransformPoint(playPositionReference.transform.position);
        float scaledCardSpacing = cardSpacing * scaleMultiplier;
        float scaledSetSpacing = spaceBetweenCardSets * scaleMultiplier;
        for (int rowStart = 0; rowStart < playedCardSets.Count; rowStart += setsPerRow)
        {
            int rowEnd = Mathf.Min(rowStart + setsPerRow, playedCardSets.Count);
            float totalWidth = 0f;
            for (int setIndex = rowStart; setIndex < rowEnd; setIndex++)
            {
                totalWidth += scaledCardSpacing * (playedCardSets[setIndex].cards.Count - 1);
            }

            totalWidth += scaledSetSpacing * Mathf.Max(0, rowEnd - rowStart - 1);
            float nextSetStart = -totalWidth / 2f;
            int rowIndex = rowStart / setsPerRow;
            Vector3 rowPositionOffset = (firstRowPositionOffset + nextRowPositionOffset * rowIndex) * scaleMultiplier;
            for (int setIndex = rowStart; setIndex < rowEnd; setIndex++)
            {
                PlayedCardSet set = playedCardSets[setIndex];
                int cardCount = set.cards.Count;
                float setWidth = scaledCardSpacing * (cardCount - 1);
                float setCenter = nextSetStart + setWidth / 2f;
                for (int i = 0; i < cardCount; i++)
                {
                    float centeredIndex = i - (cardCount - 1) / 2f;
                    float normalizedPosition = cardCount > 1 ? 2f * i / (cardCount - 1) - 1f : 0f;
                    float verticalOffset = verticalSpacing * scaleMultiplier * (1f - normalizedPosition * normalizedPosition);
                    GameObject card = set.cards[i];
                    card.transform.localRotation = Quaternion.Euler(0f, 0f, fanSpread * centeredIndex);
                    card.transform.localPosition = referencePosition
                        + rowPositionOffset
                        + new Vector3(setCenter + scaledCardSpacing * centeredIndex, verticalOffset, 0f);
                }

                nextSetStart += setWidth;
                if (setIndex + 1 < rowEnd)
                {
                    nextSetStart += scaledSetSpacing;
                }
            }
        }
    }
}

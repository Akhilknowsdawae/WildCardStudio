using System.Collections.Generic;
using UnityEngine;
using LifeAfter;
using WildCardStudio;

/// Coordinates staged cards, played combo sets, and zoomed-in play-area interactions.
public class PlayManager : MonoBehaviour
{
    private readonly BasicComboValidator comboValidator = new BasicComboValidator();

    private sealed class ValidComboCandidate
    {
        /// Indices of cards that form a valid staged combination.
        public List<int> cardIndices;
    }

    [System.Serializable]
    private class PlayedCardSet
    {
        /// Cards grouped into this played set.
        public List<GameObject> cards = new List<GameObject>();
        /// Whether this set is a set or straight.
        public ComboType type = ComboType.Set;
    }

    private sealed class PendingCardPlacement
    {
        /// Card currently moved into the play view.
        public GameObject card;
        /// Card's original parent before staging.
        public Transform originalParent;
        public Vector3 originalLocalPosition;
        public Quaternion originalLocalRotation;
        public Vector3 originalLocalScale;
        public Vector3 zoomedInLocalPosition;
        public PlayedCardSet targetSet;
    }

    public List<GameObject> cardsInPlay = new List<GameObject>();
    [SerializeField] private List<PlayedCardSet> playedCardSets = new List<PlayedCardSet>();
    private readonly List<PendingCardPlacement> pendingCardPlacements = new List<PendingCardPlacement>();
    private HandManager zoomedInHandManager;

    [Header("Card Layout")]
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private Transform playedCardsParent;
    [SerializeField] private GameObject playPositionReference;
    [SerializeField] private Transform zoomedInCardPositionReference;
    [SerializeField] private float fanSpread = 7.55f;
    [SerializeField] private float cardSpacing = 100f;
    [SerializeField] private float spaceBetweenCardSets = 250f;
    [SerializeField, Min(0f), Tooltip("Gap between a staged card and its combo, in played-card parent local units.")]
    private float stagedCardHoverDistance = 0.5f;
    [SerializeField] private Vector3 firstRowPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 nextRowPositionOffset = new Vector3(0f, 160f, 0f);
    [SerializeField, Min(1)] private int maxCardSetsPerRow = 6;
    [SerializeField, Min(0f)] private float scaleReductionPerSet = 0.1f;
    [SerializeField] private float verticalSpacing = 100f;
    [SerializeField] private Vector3 cardScale = Vector3.one;
    [SerializeField] private bool updatePlayedCardPositionsContinuously;

    /// Refreshes played-card layouts when continuous layout updates are enabled.
    private void Update()
    {
        if (updatePlayedCardPositionsContinuously)
        {
            UpdatePlayedCardPositions();
        }
    }

        /// Moves scoring-hand cards onto the table and begins zoomed-in play view.
        public bool BeginZoomedInView(HandManager handManager)
        {
            if (handManager == null)
            {
                Debug.LogError("Cannot enter the zoomed-in view: assign a HandManager.", this);
                return false;
            }

            if (scoreManager == null)
            {
                Debug.LogError("Cannot move scoring-hand cards to the table: assign a ScoreManager in the PlayManager.", this);
                return false;
            }

            if (playedCardsParent == null)
            {
                Debug.LogError("Cannot move scoring-hand cards to the table: assign a Played Cards Parent in the PlayManager.", this);
                return false;
            }

            if (zoomedInCardPositionReference == null)
            {
                Debug.LogError("Cannot move scoring-hand cards to the table: assign a Zoomed In Card Position Reference.", this);
                return false;
            }

            zoomedInHandManager = handManager;
            List<GameObject> scoringCards = new List<GameObject>(scoreManager.cardsInPlay);
            foreach (GameObject card in scoringCards)
            {
                if (card == null)
                {
                    continue;
                }

                PendingCardPlacement placement = new PendingCardPlacement
                {
                    card = card,
                    originalParent = card.transform.parent,
                    originalLocalPosition = card.transform.localPosition,
                    originalLocalRotation = card.transform.localRotation,
                    originalLocalScale = card.transform.localScale
                };
                pendingCardPlacements.Add(placement);
                scoreManager.RemovePlayedCard(card);
                card.transform.SetParent(playedCardsParent, false);
                card.transform.SetAsLastSibling();
            }

            UpdateZoomedInCardPositions();
            return true;
        }

        /// Restores every staged card to the scoring hand and ends zoomed-in view.
        public void EndZoomedInView()
        {
            if (pendingCardPlacements.Count == 0)
            {
                zoomedInHandManager = null;
                return;
            }

            CardMovement.HideInvalidComboIndicator();
            foreach (PendingCardPlacement placement in pendingCardPlacements)
            {
                if (placement.card == null)
                {
                    continue;
                }

                if (scoreManager != null && !scoreManager.cardsInPlay.Contains(placement.card))
                {
                    scoreManager.AddPlayedCard(placement.card);
                }

                if (placement.originalParent == null)
                {
                    Debug.LogError($"Cannot restore {placement.card.name}: its original parent no longer exists.", placement.card);
                    continue;
                }

                placement.card.transform.SetParent(placement.originalParent, false);
                placement.card.transform.localPosition = placement.originalLocalPosition;
                placement.card.transform.localRotation = placement.originalLocalRotation;
                placement.card.transform.localScale = placement.originalLocalScale;
            }

            pendingCardPlacements.Clear();
            zoomedInHandManager = null;
        }

        /// Stages a hand card beside an existing combo or pending target card.
        public bool TryStageCardForCombo(GameObject card, GameObject targetCard)
        {
            if (zoomedInHandManager == null || !zoomedInHandManager.IsZoomedIn)
            {
                return false;
            }

            PendingCardPlacement placement = pendingCardPlacements.Find(item => item.card == card);
            if (placement == null || targetCard == null || card == targetCard)
            {
                return false;
            }

            PlayedCardSet targetSet = FindPlayedCardSet(targetCard);
            if (targetSet == null)
            {
                PendingCardPlacement pendingTarget = pendingCardPlacements.Find(item => item.card == targetCard);
                targetSet = pendingTarget != null ? pendingTarget.targetSet : null;
            }

            if (targetSet == null)
            {
                return false;
            }

            if (targetSet.cards == null)
            {
                Debug.LogError("Cannot add a card to a table combo: the target combo has no card list.", this);
                return false;
            }

            List<GameObject> proposedCards = new List<GameObject>(targetSet.cards);
            foreach (PendingCardPlacement stagedPlacement in pendingCardPlacements)
            {
                if (stagedPlacement != placement
                    && stagedPlacement.targetSet == targetSet
                    && stagedPlacement.card != null)
                {
                    proposedCards.Add(stagedPlacement.card);
                }
            }

            proposedCards.Add(card);
            if (!IsValidCombo(proposedCards, targetSet.type, out ComboType validatedType))
            {
                placement.targetSet = null;
                return false;
            }

            if (!TryPositionStagedCardAboveCombo(placement, targetSet, targetCard))
            {
                return false;
            }

            targetSet.type = validatedType;
            placement.targetSet = targetSet;
            return true;
        }

        private bool TryPositionStagedCardAboveCombo(
            PendingCardPlacement placement,
            PlayedCardSet targetSet,
            GameObject fallbackTarget)
        {
            GameObject anchorCard = null;
            Vector3 anchorLocalPosition = Vector3.zero;
            bool useSavedPendingPosition = false;

            foreach (GameObject existingCard in targetSet.cards)
            {
                if (existingCard != null)
                {
                    anchorCard = existingCard;
                    break;
                }
            }

            if (anchorCard == null)
            {
                foreach (PendingCardPlacement stagedPlacement in pendingCardPlacements)
                {
                    if (stagedPlacement != placement
                        && stagedPlacement.targetSet == targetSet
                        && stagedPlacement.card != null)
                    {
                        anchorCard = stagedPlacement.card;
                        anchorLocalPosition = stagedPlacement.zoomedInLocalPosition;
                        useSavedPendingPosition = true;
                        break;
                    }
                }
            }

            if (anchorCard == null)
            {
                anchorCard = fallbackTarget;
            }

            RectTransform stagedRect = placement.card.GetComponent<RectTransform>();
            RectTransform anchorRect = anchorCard != null
                ? anchorCard.GetComponent<RectTransform>()
                : null;
            if (stagedRect == null || anchorRect == null || playedCardsParent == null)
            {
                Debug.LogError("Cannot position the staged card above its combo: a card RectTransform or Played Cards Parent is missing.", this);
                return false;
            }

            if (!useSavedPendingPosition)
            {
                anchorLocalPosition = playedCardsParent.InverseTransformPoint(anchorCard.transform.position);
            }

            Vector3 anchorUp = playedCardsParent.InverseTransformDirection(anchorCard.transform.up).normalized;
            float cardHalfHeight = stagedRect.rect.height * stagedRect.localScale.y * 0.5f;
            float anchorHalfHeight = anchorRect.rect.height * anchorRect.localScale.y * 0.5f;
            float hoverOffset = cardHalfHeight + anchorHalfHeight + stagedCardHoverDistance;

            placement.card.transform.localPosition = anchorLocalPosition + anchorUp * hoverOffset;
            placement.card.transform.localRotation = anchorCard.transform.localRotation;
            return true;
        }

        /// Validates staged cards and updates visual feedback for invalid combinations.
        public void ValidateZoomedInCards(HandManager handManager)
        {
            if (handManager == null || handManager != zoomedInHandManager || !handManager.IsZoomedIn)
            {
                return;
            }

            CardMovement.HideInvalidComboIndicator();
            if (pendingCardPlacements.Count == 0)
            {
                return;
            }

            List<GameObject> invalidCards = new List<GameObject>();
            HashSet<PlayedCardSet> checkedSets = new HashSet<PlayedCardSet>();
            foreach (PendingCardPlacement placement in pendingCardPlacements)
            {
                PlayedCardSet targetSet = placement.targetSet;
                if (targetSet == null || !checkedSets.Add(targetSet))
                {
                    continue;
                }

                List<GameObject> cardsInCombo = new List<GameObject>(targetSet.cards);
                foreach (PendingCardPlacement addition in pendingCardPlacements)
                {
                    if (addition.targetSet == targetSet && addition.card != null)
                    {
                        cardsInCombo.Add(addition.card);
                    }
                }

                if (!IsValidCombo(cardsInCombo, targetSet.type, out ComboType validatedType))
                {
                    foreach (PendingCardPlacement addition in pendingCardPlacements)
                    {
                        if (addition.targetSet == targetSet && addition.card != null)
                        {
                            invalidCards.Add(addition.card);
                        }
                    }
                }
                else
                {
                    targetSet.type = validatedType;
                }
            }

            List<GameObject> unassignedCards = new List<GameObject>();
            foreach (PendingCardPlacement placement in pendingCardPlacements)
            {
                if (placement.targetSet == null && placement.card != null)
                {
                    unassignedCards.Add(placement.card);
                }
            }

            if (unassignedCards.Count > 0)
            {
                invalidCards.AddRange(GetCardsNotInValidCombos(unassignedCards));
            }

            if (invalidCards.Count > 0)
            {
                ShowInvalidComboIndicators(invalidCards);
                foreach (PendingCardPlacement placement in pendingCardPlacements)
                {
                    if (invalidCards.Contains(placement.card))
                    {
                        CardMovement movement = placement.card.GetComponent<CardMovement>();
                        if (movement != null)
                        {
                            movement.SetDragReturnPosition(placement.zoomedInLocalPosition);
                        }
                        else
                        {
                            placement.card.transform.localPosition = placement.zoomedInLocalPosition;
                        }

                        placement.targetSet = null;
                    }
                }

                return;
            }

            List<GameObject> unassignedValidCards = new List<GameObject>();
            foreach (PendingCardPlacement placement in pendingCardPlacements)
            {
                if (placement.targetSet == null && placement.card != null)
                {
                    unassignedValidCards.Add(placement.card);
                }
            }

            foreach (PendingCardPlacement placement in pendingCardPlacements)
            {
                if (placement.card == null)
                {
                    continue;
                }

                if (placement.targetSet != null)
                {
                    placement.targetSet.cards.Add(placement.card);
                }

                if (!cardsInPlay.Contains(placement.card))
                {
                    cardsInPlay.Add(placement.card);
                }

                CardMovement movement = placement.card.GetComponent<CardMovement>();
                if (movement != null)
                {
                    movement.MarkAsPlayed(true);
                }
            }

            if (unassignedValidCards.Count > 0)
            {
                PlayedCardSet newSet = new PlayedCardSet
                {
                    cards = unassignedValidCards,
                    type = DetermineComboType(unassignedValidCards)
                };
                playedCardSets.Add(newSet);
            }

            int scoreToAdd = CalculateScoringHandScore(unassignedValidCards);
            HashSet<PlayedCardSet> scoredSets = new HashSet<PlayedCardSet>();
            foreach (PendingCardPlacement placement in pendingCardPlacements)
            {
                if (placement.targetSet != null && scoredSets.Add(placement.targetSet))
                {
                    scoreToAdd += CalculateAddedComboCardScore(placement.targetSet);
                }
            }

            if (scoreManager != null)
            {
                scoreManager.AddToTotalScore(scoreToAdd);
            }

            pendingCardPlacements.Clear();
            UpdatePlayedCardPositions();
        }

        private void UpdateZoomedInCardPositions()
        {
            Vector3 referencePosition = playedCardsParent.InverseTransformPoint(zoomedInCardPositionReference.position);
            float centeredStart = (pendingCardPlacements.Count - 1) / 2f;
            for (int i = 0; i < pendingCardPlacements.Count; i++)
            {
                PendingCardPlacement placement = pendingCardPlacements[i];
                placement.zoomedInLocalPosition = referencePosition
                    + Vector3.right * (cardSpacing * (i - centeredStart));
                placement.card.transform.localPosition = placement.zoomedInLocalPosition;
                placement.card.transform.localRotation = Quaternion.identity;
                placement.card.transform.localScale = cardScale;
            }
        }

        private PlayedCardSet FindPlayedCardSet(GameObject card)
        {
            foreach (PlayedCardSet set in playedCardSets)
            {
                if (set != null && set.cards != null && set.cards.Contains(card))
                {
                    return set;
                }
            }

            return null;
        }

        private bool IsValidCombo(List<GameObject> cards, ComboType preferredType, out ComboType validatedType)
        {
            validatedType = preferredType;
            List<CardInstance> instances = new List<CardInstance>(cards.Count);
            foreach (GameObject card in cards)
            {
                if (!TryCreateCardInstance(card, out CardInstance instance))
                {
                    return false;
                }

                instances.Add(instance);
            }

            ComboValidationResult validation = comboValidator.CanCreateCombo(instances, preferredType, null);
            if (validation.IsValid)
            {
                return true;
            }

            ComboType alternateType = preferredType == ComboType.Set ? ComboType.Straight : ComboType.Set;
            validation = comboValidator.CanCreateCombo(instances, alternateType, null);
            if (validation.IsValid)
            {
                validatedType = alternateType;
                return true;
            }

            return false;
        }

        private int CalculateScoringHandScore(List<GameObject> cards)
        {
            if (cards.Count == 0)
            {
                return 0;
            }

            List<CardInstance> instances = new List<CardInstance>(cards.Count);
            foreach (GameObject card in cards)
            {
                if (!TryCreateCardInstance(card, out CardInstance instance))
                {
                    return 0;
                }

                instances.Add(instance);
            }

            ComboValidationResult validation = comboValidator.CanCreateCombo(instances, ComboType.Set, null);
            if (!validation.IsValid)
            {
                validation = comboValidator.CanCreateCombo(instances, ComboType.Straight, null);
            }

            int score = 0;
            foreach (CardInstance card in instances)
            {
                score += validation.IsValid
                    ? validation.ScoringContext.GetCardScore(card, instances)
                    : card.EffectiveValue;
            }

            return score;
        }

        private int CalculateAddedComboCardScore(PlayedCardSet targetSet)
        {
            List<CardInstance> instances = new List<CardInstance>(targetSet.cards.Count);
            foreach (GameObject card in targetSet.cards)
            {
                if (!TryCreateCardInstance(card, out CardInstance instance))
                {
                    return 0;
                }

                instances.Add(instance);
            }

            ComboValidationResult validation = comboValidator.CanCreateCombo(instances, targetSet.type, null);
            if (!validation.IsValid)
            {
                ComboType alternateType = targetSet.type == ComboType.Set ? ComboType.Straight : ComboType.Set;
                validation = comboValidator.CanCreateCombo(instances, alternateType, null);
            }

            if (!validation.IsValid)
            {
                Debug.LogError("Cannot calculate a played card's score: the updated table combo is invalid.", this);
                return 0;
            }

            int score = 0;
            foreach (PendingCardPlacement placement in pendingCardPlacements)
            {
                if (placement.targetSet != targetSet || placement.card == null)
                {
                    continue;
                }

                int cardIndex = targetSet.cards.IndexOf(placement.card);
                if (cardIndex >= 0)
                {
                    score += validation.ScoringContext.GetCardScore(instances[cardIndex], instances);
                }
            }

            return score;
        }

        private bool TryCreateCardInstance(GameObject card, out CardInstance instance)
        {
            CardDisplay display = card != null ? card.GetComponent<CardDisplay>() : null;
            if (display == null || display.cardData == null)
            {
                Debug.LogError("Cannot process a table card: it is missing its CardDisplay or card data.", card);
                instance = null;
                return false;
            }

            Card definition = display.cardData;
            instance = new CardInstance
            {
                InstanceId = card.GetInstanceID().ToString(),
                Definition = definition,
                Rank = definition.cardRank,
                Suit = definition.suit != null && definition.suit.Count > 0 ? definition.suit[0] : default(Suit),
                Type = definition.cardType != null && definition.cardType.Count > 0
                    ? definition.cardType[0]
                    : CardType.Number
            };
            return true;
        }

        private ComboType DetermineComboType(List<GameObject> cards)
        {
            return IsValidCombo(cards, ComboType.Set, out ComboType type) ? type : ComboType.Straight;
        }

    /// Commits all valid staged cards as played combos.
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

    /// Adds a card to the play area and creates or updates its played-card set.
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

        int scoreForTurn = scoreManager != null ? scoreManager.CurrentScoringHandScore : 0;
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
            newSet.type = DetermineComboType(newSet.cards);
            playedCardSets.Add(newSet);
            if (scoreManager != null)
            {
                scoreManager.AddToTotalScore(scoreForTurn);
            }

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

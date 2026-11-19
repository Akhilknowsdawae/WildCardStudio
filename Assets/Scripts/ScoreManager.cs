using System.Collections.Generic;
using TMPro;
using UnityEngine;
using LifeAfter;
using WildCardStudio;

/// Tracks the cards in the scoring area, their layout, and both hand and total scores.
public class ScoreManager : MonoBehaviour
{
    /// Cards currently moved into the scoring area.
    public List<GameObject> cardsInPlay = new List<GameObject>();

    [SerializeField] private TMP_Text scoringHandScoreText;
    [SerializeField] private TMP_Text totalScoreText;
    private int totalScore;

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

    /// Initializes the displayed scoring and total scores.
    private void Start()
    {
        RefreshScoringHandScore();
        RefreshTotalScore();
    }

    /// Updates card positions when continuous layout refresh is enabled.
    private void Update()
    {
        if (updatePlayedCardPositionsContinuously)
        {
            UpdatePlayedCardPositions();
        }
    }

    /// Translates a play-area position into the coordinate system of a supplied transform.
    public Vector3 GetPlayPosition(Transform relativeTo)
    {
        if (playPositionReference == null)
        {
            return playPosition;
        }

        Vector3 referencePosition = playPositionReference.transform.position;
        return relativeTo != null ? relativeTo.InverseTransformPoint(referencePosition) : referencePosition;
    }

    /// Checks whether a screen-space-relative position is inside the active play area.
    public bool IsPointerInPlayArea(Vector2 localPosition, Transform relativeTo)
    {
        float playAreaBoundary = GetPlayPosition(relativeTo).y - Mathf.Max(0f, playAreaPadding);
        return localPosition.y > playAreaBoundary;
    }

    /// Adds a card to the scoring area and refreshes its score and layout.
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
        RefreshScoringHandScore();
        return true;
    }

    /// Removes a card from the scoring area and refreshes the layout and score.
    public bool RemovePlayedCard(GameObject card)
    {
        if (!cardsInPlay.Remove(card))
        {
            return false;
        }

        UpdatePlayedCardPositions();
        RefreshScoringHandScore();
        return true;
    }

    /// Returns the current score for cards in the scoring area.
    public int CurrentScoringHandScore => CalculateScoringHandScore(out _, out _);

    /// Adds points to the match total and refreshes the total-score display.
    public void AddToTotalScore(int score)
    {
        totalScore += score;
        RefreshTotalScore();
    }

    /// Resets the match total to zero.
    public void ResetTotalScore()
    {
        totalScore = 0;
        RefreshTotalScore();
    }

    /// Refreshes the scoring-hand text and visibility.
    private void RefreshScoringHandScore()
    {
        if (scoringHandScoreText == null)
        {
            Debug.LogError("Cannot update the scoring-hand score: assign a Score Text in the ScoreManager.", this);
            return;
        }

        int score = CalculateScoringHandScore(out int scoringCardCount, out bool flushBonusActive);
        scoringHandScoreText.text = flushBonusActive ? $"{score} x1.5" : score.ToString();
        scoringHandScoreText.gameObject.SetActive(scoringCardCount > 0);
    }

    /// Converts cards to instances and calculates set or straight scoring with flush bonuses.
    private int CalculateScoringHandScore(out int scoringCardCount, out bool flushBonusActive)
    {
        List<CardInstance> scoringCards = new List<CardInstance>();
        scoringCardCount = 0;
        bool allCardsHaveSuit = true;
        bool allSuitsMatch = true;
        Suit scoringSuit = default(Suit);
        if (cardsInPlay != null)
        {
            foreach (GameObject card in cardsInPlay)
            {
                if (card == null)
                {
                    continue;
                }

                scoringCardCount++;
                CardDisplay cardDisplay = card.GetComponent<CardDisplay>();
                if (cardDisplay == null || cardDisplay.cardData == null)
                {
                    Debug.LogError("Cannot calculate the scoring-hand score: a card is missing its CardDisplay or card data.", card);
                    allSuitsMatch = false;
                    continue;
                }

                Card definition = cardDisplay.cardData;
                Suit suit = definition.suit != null && definition.suit.Count > 0
                    ? definition.suit[0]
                    : default(Suit);
                CardType type = definition.cardType != null && definition.cardType.Count > 0
                    ? definition.cardType[0]
                    : CardType.Number;
                scoringCards.Add(new CardInstance
                {
                    InstanceId = card.GetInstanceID().ToString(),
                    Definition = definition,
                    Rank = definition.cardRank,
                    Suit = suit,
                    Type = type
                });

                if (definition.suit == null || definition.suit.Count == 0)
                {
                    allCardsHaveSuit = false;
                }
                else if (scoringCards.Count == 1)
                {
                    scoringSuit = suit;
                }
                else if (suit != scoringSuit)
                {
                    allSuitsMatch = false;
                }
            }
        }

        var validator = new BasicComboValidator();
        ComboType comboType = ComboType.Set;
        ComboValidationResult validation = validator.CanCreateCombo(scoringCards, comboType, null);
        if (!validation.IsValid)
        {
            comboType = ComboType.Straight;
            validation = validator.CanCreateCombo(scoringCards, comboType, null);
        }

        int score = 0;
        if (validation.IsValid)
        {
            foreach (CardInstance card in scoringCards)
            {
                score += validation.ScoringContext.GetCardScore(card, scoringCards);
            }
        }
        else
        {
            foreach (CardInstance card in scoringCards)
            {
                score += card.EffectiveValue;
            }
        }

        flushBonusActive = validation.IsValid
            && scoringCardCount >= 2
            && allCardsHaveSuit
            && allSuitsMatch;
        return score;
    }

    /// Refreshes the on-screen match-total text.
    private void RefreshTotalScore()
    {
        if (totalScoreText == null)
        {
            Debug.LogError("Cannot update the total score: assign a Total Score Text in the ScoreManager.", this);
            return;
        }

        totalScoreText.text = totalScore.ToString();
    }

    /// Reinitializes a card display before it is laid out in the scoring area.
    private static void RefreshCardDisplay(GameObject card)
    {
        CardDisplay cardDisplay = card.GetComponent<CardDisplay>();
        if (cardDisplay != null)
        {
            cardDisplay.UpdateCardDisplay();
        }
    }

    /// Recalculates the positions of all cards currently in the scoring area.
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
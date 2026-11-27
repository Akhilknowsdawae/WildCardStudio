using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LifeAfter;
using WildCardStudio;

/// Displays the opponent hand and stages opponent combos before sending them to the shared table.
public sealed class EnemyHandDisplay : MonoBehaviour
{
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private Transform handCardsParent;
    [SerializeField] private Transform scoringCardsParent;
    [SerializeField] private PlayManager tablePlayManager;
    [SerializeField] private TMP_Text totalScoreText;
    [SerializeField] private Vector3 handCardScale = new Vector3(120f, 125f, 1f);
    [SerializeField] private Vector3 scoringCardScale = new Vector3(120f, 125f, 1f);
    [SerializeField] private float handCardSpacing = 100f;
    [SerializeField] private float handVerticalSpacing = 100f;
    [SerializeField] private float handFanSpread = 7.55f;
    [SerializeField] private float scoringCardSpacing = 100f;

    private readonly Dictionary<string, GameObject> cardVisuals = new Dictionary<string, GameObject>();
    private readonly List<string> handCardIds = new List<string>();
    private readonly List<string> scoringCardIds = new List<string>();

    /// Creates missing visuals and lays out the opponent hand.
    public void SynchronizeHand(PlayerState player)
    {
        if (player == null || player.Hand == null)
        {
            Debug.LogError("Cannot display the opponent hand: player state or hand is missing.", this);
            return;
        }

        if (!ValidateReferences())
        {
            return;
        }

        HashSet<string> desiredCardIds = new HashSet<string>();
        foreach (CardInstance card in player.Hand)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.InstanceId))
            {
                Debug.LogError("Cannot display an opponent hand card without an instance ID.", this);
                continue;
            }

            desiredCardIds.Add(card.InstanceId);
            if (cardVisuals.TryGetValue(card.InstanceId, out GameObject existingVisual))
            {
                if (!handCardIds.Contains(card.InstanceId))
                {
                    RemoveFromScoringArea(card.InstanceId);
                    existingVisual.transform.SetParent(handCardsParent, false);
                    handCardIds.Add(card.InstanceId);
                }

                continue;
            }

            GameObject visual = CreateCardVisual(card);
            if (visual == null)
            {
                continue;
            }

            visual.transform.SetParent(handCardsParent, false);
            cardVisuals.Add(card.InstanceId, visual);
            handCardIds.Add(card.InstanceId);
        }

        for (int i = handCardIds.Count - 1; i >= 0; i--)
        {
            string cardId = handCardIds[i];
            if (desiredCardIds.Contains(cardId))
            {
                continue;
            }

            handCardIds.RemoveAt(i);
            if (cardVisuals.TryGetValue(cardId, out GameObject visual))
            {
                cardVisuals.Remove(cardId);
                Destroy(visual);
            }
        }

        UpdateHandCardPositions();
    }

    /// Moves an AI combo from its hand to the enemy scoring area.
    public bool StageCombo(IReadOnlyList<CardInstance> comboCards)
    {
        if (comboCards == null || comboCards.Count == 0)
        {
            Debug.LogError("Cannot stage the opponent combo: no cards were supplied.", this);
            return false;
        }

        if (!ValidateReferences())
        {
            return false;
        }

        List<GameObject> visuals = new List<GameObject>(comboCards.Count);
        HashSet<string> stagedIds = new HashSet<string>();
        foreach (CardInstance card in comboCards)
        {
            if (card == null
                || !stagedIds.Add(card.InstanceId)
                || !cardVisuals.TryGetValue(card.InstanceId, out GameObject visual)
                || !handCardIds.Contains(card.InstanceId))
            {
                Debug.LogError("Cannot stage the opponent combo: a card has no hand visual.", this);
                return false;
            }

            visuals.Add(visual);
        }

        for (int i = 0; i < comboCards.Count; i++)
        {
            CardInstance card = comboCards[i];
            GameObject visual = visuals[i];
            handCardIds.Remove(card.InstanceId);
            visual.transform.SetParent(scoringCardsParent, false);
            visual.transform.localScale = scoringCardScale;
            visual.transform.localRotation = Quaternion.identity;
            SetEnemyHighlight(visual, false);
            scoringCardIds.Add(card.InstanceId);
        }

        UpdateHandCardPositions();
        UpdateScoringCardPositions();
        return true;
    }

    /// Transfers staged opponent combo cards to the shared table and highlights them.
    public bool PlayStagedCombo(IReadOnlyList<CardInstance> comboCards, ComboType comboType)
    {
        if (comboCards == null || comboCards.Count == 0 || tablePlayManager == null)
        {
            Debug.LogError("Cannot play the opponent combo: cards or Table PlayManager are missing.", this);
            return false;
        }

        List<GameObject> visuals = new List<GameObject>(comboCards.Count);
        foreach (CardInstance card in comboCards)
        {
            if (card == null
                || !scoringCardIds.Contains(card.InstanceId)
                || !cardVisuals.TryGetValue(card.InstanceId, out GameObject visual))
            {
                Debug.LogError("Cannot play the opponent combo: a staged card visual is missing.", this);
                return false;
            }

            visuals.Add(visual);
        }

        if (!tablePlayManager.AddOpponentCombo(visuals, comboType))
        {
            return false;
        }

        foreach (GameObject visual in visuals)
        {
            SetEnemyHighlight(visual, true);
        }

        foreach (CardInstance card in comboCards)
        {
            scoringCardIds.Remove(card.InstanceId);
            cardVisuals.Remove(card.InstanceId);
        }

        UpdateScoringCardPositions();
        return true;
    }

    /// Removes a discarded card visual from the opponent hand.
    public void RemoveCardFromHand(CardInstance card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.InstanceId))
        {
            Debug.LogError("Cannot remove the opponent's discarded card visual: card identity is missing.", this);
            return;
        }

        handCardIds.Remove(card.InstanceId);
        if (cardVisuals.TryGetValue(card.InstanceId, out GameObject visual))
        {
            cardVisuals.Remove(card.InstanceId);
            Destroy(visual);
        }

        UpdateHandCardPositions();
    }

    /// Displays the total score accumulated by the opponent's table combos.
    public void RefreshTotalScore(PlayerState player, IReadOnlyList<Combo> combos)
    {
        if (totalScoreText == null)
        {
            Debug.LogError("Cannot display the opponent's total score: no score text is assigned.", this);
            return;
        }

        float score = combos == null
            ? 0f
            : combos
                .Where(combo => combo != null && combo.Owner == player)
                .Sum(combo => combo.Score);
        totalScoreText.text = score.ToString("0.#");
    }

    private GameObject CreateCardVisual(CardInstance card)
    {
        if (cardPrefab == null)
        {
            Debug.LogError("Cannot display the opponent hand: no card prefab is assigned.", this);
            return null;
        }

        if (card.Definition == null)
        {
            Debug.LogError($"Cannot display opponent card {card.InstanceId}: its card definition is missing.", this);
            return null;
        }

        GameObject visual = Instantiate(cardPrefab, handCardsParent, false);
        visual.name = $"Enemy {card.Definition.cardName}";
        visual.transform.localScale = handCardScale;

        CardDisplay display = visual.GetComponent<CardDisplay>();
        if (display == null)
        {
            Debug.LogError("Cannot display an opponent card: the card prefab has no CardDisplay component.", visual);
            Destroy(visual);
            return null;
        }

        display.cardData = card.Definition;
        display.UpdateCardDisplay();

        CardMovement movement = visual.GetComponent<CardMovement>();
        if (movement != null)
        {
            movement.enabled = false;
        }

        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (Graphic graphic in visual.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
        }

        foreach (Collider2D collider in visual.GetComponentsInChildren<Collider2D>(true))
        {
            collider.enabled = false;
        }

        SetEnemyHighlight(visual, false);
        return visual;
    }

    private bool ValidateReferences()
    {
        if (cardPrefab == null || handCardsParent == null || scoringCardsParent == null)
        {
            Debug.LogError("Cannot display the opponent's cards: assign the card prefab, hand parent, and scoring parent.", this);
            return false;
        }

        return true;
    }

    private void RemoveFromScoringArea(string cardId)
    {
        if (scoringCardIds.Remove(cardId))
        {
            UpdateScoringCardPositions();
        }
    }

    private void UpdateHandCardPositions()
    {
        int cardCount = handCardIds.Count;
        for (int i = 0; i < cardCount; i++)
        {
            if (!cardVisuals.TryGetValue(handCardIds[i], out GameObject visual) || visual == null)
            {
                continue;
            }

            float centeredIndex = i - (cardCount - 1) / 2f;
            float normalizedPosition = cardCount > 1 ? 2f * i / (cardCount - 1) - 1f : 0f;
            float verticalOffset = handVerticalSpacing * (1f - normalizedPosition * normalizedPosition);
            visual.transform.localPosition = new Vector3(handCardSpacing * centeredIndex, verticalOffset, 0f);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, handFanSpread * centeredIndex);
            visual.transform.localScale = handCardScale;
        }
    }

    private void UpdateScoringCardPositions()
    {
        int cardCount = scoringCardIds.Count;
        for (int i = 0; i < cardCount; i++)
        {
            if (!cardVisuals.TryGetValue(scoringCardIds[i], out GameObject visual) || visual == null)
            {
                continue;
            }

            float centeredIndex = i - (cardCount - 1) / 2f;
            visual.transform.localPosition = Vector3.right * (scoringCardSpacing * centeredIndex);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = scoringCardScale;
        }
    }

    private static void SetEnemyHighlight(GameObject card, bool isHighlighted)
    {
        foreach (Transform child in card.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name.Trim(), "CardEnemyHighlight", System.StringComparison.OrdinalIgnoreCase))
            {
                child.gameObject.SetActive(isHighlighted);
            }
        }
    }
}

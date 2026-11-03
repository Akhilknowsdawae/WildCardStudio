using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using LifeAfter;
using WildCardStudio;

public class CardMovement : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private static readonly List<CardMovement> cardsInPlay = new List<CardMovement>();
    private static readonly HashSet<CardMovement> cardsWithActiveInvalidComboIndicators = new HashSet<CardMovement>();
    private static readonly HashSet<CardMovement> cardsBeingDragged = new HashSet<CardMovement>();

    private RectTransform rectTransform;
    private Canvas canvas;
    private RectTransform dragPlane;
    private HandManager handManager;
    private ScoreManager scoreManager;
    private GameController gameController;
    private Vector2 originalLocalPointerPosition;
    private Vector3 originalPanelLocalPosition;
    private Vector3 originalScale;
    private int currentState = 0; // 0: Normal, 1: Hovered, 2: Dragged
    private Quaternion originalRotation;
    private Vector3 originalPosition;
    private bool isPlayed;
    private bool isLockedInPlay;
    private bool isSelected;
    private bool wasDragged;
    private bool isDraggingPlayedCardToHand;

    public bool IsSelected => isSelected;
    public static bool IsAnyCardBeingDragged => cardsBeingDragged.Count > 0;

    [SerializeField] private float selectScale = 1.2f; // Scale when selected
    [SerializeField] private GameObject glowEffect;
    [SerializeField] private GameObject playArrow;
    [SerializeField] private GameObject invalidComboIndicator;
    [SerializeField] private float lerpFactor = 0.1f; // Lerp factor for smooth movement

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
        dragPlane = rectTransform.parent as RectTransform;
        if (dragPlane == null && canvas != null)
        {
            dragPlane = canvas.GetComponent<RectTransform>();
        }
        scoreManager = FindFirstObjectByType<ScoreManager>();
        gameController = FindFirstObjectByType<GameController>();
        originalScale = rectTransform.localScale;
        originalRotation = rectTransform.localRotation;
        originalPosition = rectTransform.localPosition;
        if (invalidComboIndicator != null)
        {
            invalidComboIndicator.SetActive(false);
        }
    }

    public bool ShowInvalidComboIndicator()
    {
        if (invalidComboIndicator == null)
        {
            Debug.LogError("Cannot show invalid combo feedback: assign an Invalid Combo Indicator in the CardMovement.", this);
            return false;
        }

        if (cardsWithActiveInvalidComboIndicators.Add(this))
        {
            invalidComboIndicator.SetActive(true);
        }

        return true;
    }

    public void HideOwnInvalidComboIndicator()
    {
        cardsWithActiveInvalidComboIndicators.Remove(this);
        if (invalidComboIndicator != null)
        {
            invalidComboIndicator.SetActive(false);
        }
    }

    public static void HideInvalidComboIndicator()
    {
        foreach (CardMovement card in cardsWithActiveInvalidComboIndicators)
        {
            if (card != null && card.invalidComboIndicator != null)
            {
                card.invalidComboIndicator.SetActive(false);
            }
        }

        cardsWithActiveInvalidComboIndicators.Clear();
    }

    void Update()
    {
        switch(currentState)
        {
            case 1: // Normal
                HandleHoverState();
                break;
            case 2: // Hovered
                HandleDragState();
                if (!Input.GetMouseButton(0)) // If mouse button is released
                {
                    TransitionToState0();
                }
                break;
            case 3:
                HandlePlayState();
                if (!Input.GetMouseButton(0)) // If mouse button is released
                {
                    CommitPlayState();
                }
                break;
            case 4:
                if (!Input.GetMouseButton(0))
                {
                    CommitReturnToHand();
                }
                break;
        }
    }

    private void TransitionToState0()
    {
        cardsBeingDragged.Remove(this);
        currentState = isSelected ? 1 : 0;
        rectTransform.localScale = originalScale; //Reset Scale
        rectTransform.localRotation = originalRotation; //Reset Rotation
        rectTransform.localPosition = originalPosition; //Reset Position
        if (glowEffect != null)
        {
            glowEffect.SetActive(isSelected);
        }

        if (playArrow != null)
        {
            playArrow.SetActive(false);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (currentState == 0 && !isLockedInPlay) // Only transition to hovered state if currently in normal state
        {
            originalPosition = rectTransform.localPosition; // Store the original position
            originalRotation = rectTransform.localRotation; // Store the original rotation
            originalScale = rectTransform.localScale; // Store the original scale
            
            currentState = 1;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (currentState == 1 && !isSelected) // Keep selected cards highlighted after the pointer leaves
        {
            currentState = 0;
            TransitionToState0();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (currentState == 1 && !isLockedInPlay)
        {
            wasDragged = false;
            if (gameController == null)
            {
                gameController = FindFirstObjectByType<GameController>();
            }

            if (gameController != null && gameController.CurrentPhase == TurnPhase.Draw)
            {
                return;
            }

            dragPlane = rectTransform.parent as RectTransform;
            if (dragPlane == null && canvas != null)
            {
                dragPlane = canvas.GetComponent<RectTransform>();
            }

            if (dragPlane == null)
            {
                return;
            }

            isDraggingPlayedCardToHand = false;
            currentState = isPlayed || IsInScoringList() ? 4 : 2;
            cardsBeingDragged.Add(this);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(dragPlane, eventData.position, eventData.pressEventCamera, out originalLocalPointerPosition);
            originalPanelLocalPosition = rectTransform.localPosition;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (isLockedInPlay || wasDragged)
        {
            return;
        }

        if (gameController == null)
        {
            gameController = FindFirstObjectByType<GameController>();
        }

        if (isPlayed || IsInScoringList())
        {
            if (gameController == null || gameController.CurrentPhase != TurnPhase.Discard)
            {
                return;
            }

            Debug.Log($"Keeping {name} in the scoring list for discard; drag it back to hand to keep it.", this);
            return;
        }

        if (gameController != null && gameController.CurrentPhase == TurnPhase.Play)
        {
            if (scoreManager == null)
            {
                scoreManager = FindFirstObjectByType<ScoreManager>();
            }

            if (scoreManager == null)
            {
                Debug.LogError("Cannot add card to the scoring area: no ScoreManager exists in the scene.", this);
                return;
            }

            if (scoreManager.AddPlayedCard(gameObject))
            {
                MarkAsPlayed();
            }

            return;
        }

        isSelected = !isSelected;
        currentState = isSelected ? 1 : 0;
        if (!isSelected)
        {
            TransitionToState0();
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (gameController == null)
        {
            gameController = FindFirstObjectByType<GameController>();
        }

        if (gameController != null && gameController.CurrentPhase == TurnPhase.Draw
            && (currentState == 2 || currentState == 3 || currentState == 4))
        {
            RemoveFromPlayArea();
            TransitionToState0();
            return;
        }

        if (!isLockedInPlay && (currentState == 2 || currentState == 3 || currentState == 4) && dragPlane != null)
        {
            HideInvalidComboIndicator();

            wasDragged = true;
            Vector2 localPointerPosition;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(dragPlane, eventData.position, eventData.pressEventCamera, out localPointerPosition))
            {
                if (currentState == 4)
                {
                    Vector3 targetPosition = originalPanelLocalPosition + (Vector3)(localPointerPosition - originalLocalPointerPosition);
                    rectTransform.localPosition = Vector3.Lerp(rectTransform.localPosition, targetPosition, lerpFactor);
                    isDraggingPlayedCardToHand = scoreManager != null
                        && !scoreManager.IsPointerInPlayArea(localPointerPosition, dragPlane);
                }
                else if (currentState == 2)
                {
                    Vector3 targetPosition = originalPanelLocalPosition + (Vector3)(localPointerPosition - originalLocalPointerPosition);
                    rectTransform.localPosition = Vector3.Lerp(rectTransform.localPosition, targetPosition, lerpFactor);
                    if (scoreManager != null && scoreManager.IsPointerInPlayArea(localPointerPosition, dragPlane))
                    {
                        AddToPlayArea();
                        currentState = 3;
                        playArrow.SetActive(true);
                    }
                }
                else if (scoreManager != null && !scoreManager.IsPointerInPlayArea(localPointerPosition, dragPlane))
                {
                    RemoveFromPlayArea();
                    currentState = 2;
                    playArrow.SetActive(false);
                    Vector3 targetPosition = originalPanelLocalPosition + (Vector3)(localPointerPosition - originalLocalPointerPosition);
                    rectTransform.localPosition = Vector3.Lerp(rectTransform.localPosition, targetPosition, lerpFactor);
                }
            }
        }
    }

    private void HandleHoverState()
    {
        rectTransform.localScale = originalScale * selectScale; //Scale up the card
        if (glowEffect != null)
        {
            glowEffect.SetActive(true);
        }
    }

    private void HandleDragState()
    {
        // Dragging logic is handled in OnDrag
        //Set card rotation
        rectTransform.localRotation = Quaternion.identity;
    }

    private void HandlePlayState()
    {
        rectTransform.localRotation = Quaternion.identity;
        UpdatePlayPositions();
    }

    private void AddToPlayArea()
    {
        if (!cardsInPlay.Contains(this))
        {
            cardsInPlay.Add(this);
            UpdatePlayPositions();
        }
    }

    private void RemoveFromPlayArea()
    {
        if (cardsInPlay.Remove(this))
        {
            UpdatePlayPositions();
        }
    }

    private static void UpdatePlayPositions()
    {
        cardsInPlay.RemoveAll(card => card == null);
        if (cardsInPlay.Count == 0)
        {
            return;
        }

        float spacing = cardsInPlay[0].scoreManager.CardSpacing;
        for (int i = 0; i < cardsInPlay.Count; i++)
        {
            CardMovement card = cardsInPlay[i];
            Vector3 firstPlayPosition = card.scoreManager.GetPlayPosition(card.rectTransform.parent);
            card.rectTransform.localPosition = firstPlayPosition + Vector3.right * (spacing * i);
        }
    }

    private void CommitPlayState()
    {
        if (gameController == null)
        {
            gameController = FindFirstObjectByType<GameController>();
        }

        if (gameController != null && gameController.CurrentPhase == TurnPhase.Discard
            && (scoreManager == null || scoreManager.cardsInPlay == null
                || !scoreManager.cardsInPlay.Contains(gameObject)))
        {
            if (scoreManager == null || scoreManager.cardsInPlay == null)
            {
                Debug.LogError("Cannot add a discard candidate: no ScoreManager scoring list is available.", this);
                ReturnRejectedDiscardCandidateToHand();
                return;
            }

            DiscardManager discardManager = FindFirstObjectByType<DiscardManager>();
            if (discardManager == null)
            {
                Debug.LogError("Cannot add a discard candidate: no DiscardManager exists in the scene.", this);
                ReturnRejectedDiscardCandidateToHand();
                return;
            }

            int scoringCardCount = scoreManager.cardsInPlay.FindAll(card => card != null).Count;
            if (scoringCardCount >= discardManager.CardsToDiscard)
            {
                Debug.Log($"Discard selection is full ({discardManager.CardsToDiscard} card(s)); returning {name} to the hand.", this);
                ReturnRejectedDiscardCandidateToHand();
                return;
            }
        }

        if (scoreManager != null && scoreManager.AddPlayedCard(gameObject))
        {
            MarkAsPlayed();
            return;
        }

        if (scoreManager == null)
        {
            Debug.LogError("Cannot record played card: no ScoreManager exists in the scene.", this);
        }

        RemoveFromPlayArea();
        TransitionToState0();
    }

    private void ReturnRejectedDiscardCandidateToHand()
    {
        RemoveFromPlayArea();
        if (handManager == null)
        {
            Debug.LogError("Cannot return the rejected discard candidate: no HandManager is assigned.", this);
            TransitionToState0();
            return;
        }

        if (!handManager.ReturnCardToHand(gameObject))
        {
            Debug.LogError("Failed to return the rejected discard candidate to the hand.", this);
            TransitionToState0();
        }
    }

    private bool IsInScoringList()
    {
        if (scoreManager == null)
        {
            scoreManager = FindFirstObjectByType<ScoreManager>();
        }

        return scoreManager != null && scoreManager.cardsInPlay != null
            && scoreManager.cardsInPlay.Contains(gameObject);
    }

    private void CommitReturnToHand()
    {
        if (isDraggingPlayedCardToHand && scoreManager != null && handManager != null
            && handManager.ReturnCardToHand(gameObject))
        {
            scoreManager.RemovePlayedCard(gameObject);
            isPlayed = false;
            isSelected = false;
            currentState = 0;
            if (glowEffect != null)
            {
                glowEffect.SetActive(false);
            }

            if (playArrow != null)
            {
                playArrow.SetActive(false);
            }

            return;
        }

        currentState = 0;
        if (scoreManager != null)
        {
            scoreManager.RefreshPlayedCardPositions();
        }

        TransitionToState0();
    }

    public void MarkAsPlayed(bool lockInPlay = false)
    {
        cardsBeingDragged.Remove(this);
        RemoveFromPlayArea();
        isPlayed = true;
        isLockedInPlay = lockInPlay;
        isSelected = false;
        currentState = 0;
        rectTransform.localScale = originalScale;
        if (glowEffect != null)
        {
            glowEffect.SetActive(false);
        }

        if (playArrow != null)
        {
            playArrow.SetActive(false);
        }

        if (handManager != null)
        {
            handManager.RemoveCardFromHand(gameObject);
        }
    }

    public void MarkAsReturnedToHand()
    {
        cardsBeingDragged.Remove(this);
        isPlayed = false;
        isLockedInPlay = false;
        isSelected = false;
        currentState = 0;
        rectTransform.localScale = originalScale;
        if (glowEffect != null)
        {
            glowEffect.SetActive(false);
        }

        if (playArrow != null)
        {
            playArrow.SetActive(false);
        }
    }

    public void SetHandManager(HandManager manager)
    {
        handManager = manager;
    }

    void OnDestroy()
    {
        cardsBeingDragged.Remove(this);
        HideOwnInvalidComboIndicator();
        RemoveFromPlayArea();
    }

    private void OnDisable()
    {
        cardsBeingDragged.Remove(this);
    }
}

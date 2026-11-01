using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using LifeAfter;
using WildCardStudio;

public class CardMovement : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    private static readonly List<CardMovement> cardsInPlay = new List<CardMovement>();

    private RectTransform rectTransform;
    private Canvas canvas;
    private RectTransform dragPlane;
    private HandManager handManager;
    private ScoreManager scoreManager;
    private Vector2 originalLocalPointerPosition;
    private Vector3 originalPanelLocalPosition;
    private Vector3 originalScale;
    private int currentState = 0; // 0: Normal, 1: Hovered, 2: Dragged
    private Quaternion originalRotation;
    private Vector3 originalPosition;
    private bool isPlayed;

    [SerializeField] private float selectScale = 1.2f; // Scale when selected
    [SerializeField] private GameObject glowEffect;
    [SerializeField] private GameObject playArrow;
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
        originalScale = rectTransform.localScale;
        originalRotation = rectTransform.localRotation;
        originalPosition = rectTransform.localPosition;
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
        }
    }

    private void TransitionToState0()
    {
        currentState = 0;
        rectTransform.localScale = originalScale; //Reset Scale
        rectTransform.localRotation = originalRotation; //Reset Rotation
        rectTransform.localPosition = originalPosition; //Reset Position
        glowEffect.SetActive(false); //Disable gloweffect
        playArrow.SetActive(false); //Disable playArrow
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (currentState == 0 && !isPlayed) // Only transition to hovered state if currently in normal state
        {
            originalPosition = rectTransform.localPosition; // Store the original position
            originalRotation = rectTransform.localRotation; // Store the original rotation
            originalScale = rectTransform.localScale; // Store the original scale
            
            currentState = 1;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (currentState == 1) // Only transition back to normal state if currently in hovered state
        {
            currentState = 0;
            TransitionToState0();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (currentState == 1 && dragPlane != null) // Only transition to dragged state if currently in hovered state
        {
            currentState = 2;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(dragPlane, eventData.position, eventData.pressEventCamera, out originalLocalPointerPosition);
            originalPanelLocalPosition = rectTransform.localPosition;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if ((currentState == 2 || currentState == 3) && dragPlane != null)
        {
            Vector2 localPointerPosition;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(dragPlane, eventData.position, eventData.pressEventCamera, out localPointerPosition))
            {
                if (currentState == 2)
                {
                    Vector3 targetPosition = originalPanelLocalPosition + (Vector3)(localPointerPosition - originalLocalPointerPosition);
                    rectTransform.localPosition = Vector3.Lerp(rectTransform.localPosition, targetPosition, lerpFactor);
                    if (scoreManager != null && localPointerPosition.y > scoreManager.GetPlayPosition(dragPlane).y)
                    {
                        AddToPlayArea();
                        currentState = 3;
                        playArrow.SetActive(true);
                    }
                }
                else if (scoreManager != null && localPointerPosition.y < scoreManager.GetPlayPosition(dragPlane).y)
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
        glowEffect.SetActive(true); //Enable gloweffect
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
        if (scoreManager != null && scoreManager.AddPlayedCard(gameObject))
        {
            RemoveFromPlayArea();
            isPlayed = true;
            currentState = 0;
            rectTransform.localScale = originalScale;
            rectTransform.localRotation = Quaternion.identity;
            glowEffect.SetActive(false);
            playArrow.SetActive(false);
            if (handManager != null)
            {
                handManager.RemoveCardFromHand(gameObject);
            }
            return;
        }

        if (scoreManager == null)
        {
            Debug.LogError("Cannot record played card: no ScoreManager exists in the scene.", this);
        }

        RemoveFromPlayArea();
        TransitionToState0();
    }

    public void SetHandManager(HandManager manager)
    {
        handManager = manager;
    }

    void OnDestroy()
    {
        RemoveFromPlayArea();
    }
}

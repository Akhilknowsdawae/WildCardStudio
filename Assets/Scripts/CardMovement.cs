using UnityEngine;
using UnityEngine.EventSystems;
using LifeAfter;
using WildCardStudio;

public class CardMovement : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler
{
    private RectTransform rectTransform;
    private Canvas canvas;
    private Vector2 originalLocalPointerPosition;
    private Vector3 originalPanelLocalPosition;
    private Vector3 originalScale;
    private int currentState = 0; // 0: Normal, 1: Hovered, 2: Dragged
    private Quaternion originalRotation;
    private Vector3 originalPosition;

    [SerializeField] private float selectScale = 1.2f; // Scale when selected
    [SerializeField] private Vector2 cardPlay;
    [SerializeField] private Vector3 playPosition;
    [SerializeField] private GameObject glowEffect;
    [SerializeField] private GameObject playArrow;
    [SerializeField] private float lerpFactor = 0.1f; // Lerp factor for smooth movement

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
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
                    TransitionToState0();
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
        if (currentState == 0) // Only transition to hovered state if currently in normal state
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
        if (currentState == 1) // Only transition to dragged state if currently in hovered state
        {
            currentState = 2;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.GetComponent<RectTransform>(), eventData.position, eventData.pressEventCamera, out originalLocalPointerPosition); //Using the event system to detect what is clicked on
            originalPanelLocalPosition = rectTransform.localPosition;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (currentState == 2) // Only allow dragging if currently in dragged state
        {
            Vector2 localPointerPosition;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.GetComponent<RectTransform>(), eventData.position, eventData.pressEventCamera, out localPointerPosition))
            {
                
                rectTransform.localPosition = Vector3.Lerp(rectTransform.position, Input.mousePosition, lerpFactor);

                // Check if the card is in the play area
                if (rectTransform.localPosition.y > cardPlay.y) //&& rectTransform.localPosition.x > cardPlay.x && rectTransform.localPosition.x < cardPlay.x + cardPlay.width)
                {
                    currentState = 3; // Transition to play state
                    playArrow.SetActive(true); //Enable playArrow
                    rectTransform.localPosition = Vector3.Lerp(rectTransform.position, playPosition, lerpFactor); //Snap to play position
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
        // Play logic is handled in OnDrag
        //Set card rotation
        rectTransform.localPosition = playPosition; //Snap to play position
        rectTransform.localRotation = Quaternion.identity; //Scale up the card

        if (Input.mousePosition.y < cardPlay.y) // If the mouse is dragged below the play area
        {
            currentState = 2; // Transition back to dragged state
            playArrow.SetActive(false); //Disable playArrow
        }
    }

    void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }
}

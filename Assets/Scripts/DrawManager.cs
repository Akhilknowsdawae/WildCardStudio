using UnityEngine;
using UnityEngine.EventSystems;
using LifeAfter;
using WildCardStudio;

/// Handles pointer hover and mouse input for drawing from the configured piles.
public class DrawManager : MonoBehaviour
{
    [SerializeField] private GameObject drawObject;
    [SerializeField, Min(0)] private int cardsToDraw = 1;
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private DiscardManager discardManager;
    [SerializeField] private HandManager handManager;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private GameController gameController;
    [SerializeField, Min(0f)] private float hoverHeight = 0.2f;
    [SerializeField, Min(0f)] private float hoverMoveSpeed = 10f;
    [SerializeField, Min(0f)] private float discardPileHoverHeight = 20f;
    [SerializeField, Min(0f)] private float discardPileHoverMoveSpeed = 10f;

    private Vector3 drawObjectOriginalLocalPosition;
    private Vector3 discardPileOriginalLocalPosition;
    private Transform discardPilePosition;
    private Camera mainCamera;

    /// Saves the draw object's resting position.
    private void Awake()
    {
        if (drawObject != null)
        {
            drawObjectOriginalLocalPosition = drawObject.transform.localPosition;
        }
    }

    /// Resolves scene references used for input and turn validation.
    private void Start()
    {
        mainCamera = Camera.main;
        if (deckManager == null)
        {
            deckManager = FindFirstObjectByType<DeckManager>();
        }

        if (discardManager == null)
        {
            discardManager = FindFirstObjectByType<DiscardManager>();
        }

        if (discardManager != null)
        {
            discardPilePosition = discardManager.DiscardPilePosition;
            if (discardPilePosition != null)
            {
                discardPileOriginalLocalPosition = discardPilePosition.localPosition;
            }
        }

        if (handManager == null)
        {
            handManager = FindFirstObjectByType<HandManager>();
        }

        if (scoreManager == null)
        {
            scoreManager = FindFirstObjectByType<ScoreManager>();
        }

        if (gameController == null)
        {
            gameController = FindFirstObjectByType<GameController>();
        }
    }

    /// Updates hover feedback and executes a draw click when the turn permits it.
    private void Update()
    {
        bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool interactionBlocked = IsCardInteractionActive();
        bool pointerOverDrawObject = !interactionBlocked && !pointerOverUI && IsPointerOverDrawObject();
        bool pointerOverDiscardPile = !interactionBlocked && !pointerOverUI && IsPointerOverDiscardPile();
        bool canDrawFromDrawPile = gameController != null && gameController.CanPlayerDrawFromDrawPile;
        bool canDrawFromDiscardPile = gameController != null && gameController.CanPlayerDrawFromDiscardPile;
        bool discardPileHasTopCard = discardManager != null
            && discardManager.DiscardedCards.Count > 0
            && discardManager.DiscardedCards[discardManager.DiscardedCards.Count - 1] != null;
        bool isHoveringDrawObject = pointerOverDrawObject && canDrawFromDrawPile;
        bool isHoveringDiscardPile = pointerOverDiscardPile && canDrawFromDiscardPile && discardPileHasTopCard;
        UpdatePilePosition(
            drawObject != null ? drawObject.transform : null,
            drawObjectOriginalLocalPosition,
            isHoveringDrawObject,
            hoverHeight,
            hoverMoveSpeed);
        UpdatePilePosition(
            discardPilePosition,
            discardPileOriginalLocalPosition,
            isHoveringDiscardPile,
            discardPileHoverHeight,
            discardPileHoverMoveSpeed);

        if (!Input.GetMouseButtonDown(0) || interactionBlocked || pointerOverUI)
        {
            return;
        }

        if (pointerOverDiscardPile)
        {
            if (canDrawFromDiscardPile)
            {
                DrawFromDiscardPile();
            }
            else
            {
                Debug.LogWarning("The configured draw rule does not allow drawing from the discard pile.", this);
            }

            return;
        }

        if (pointerOverDrawObject && !canDrawFromDrawPile)
        {
            Debug.LogWarning("The configured draw rule does not allow drawing from the draw pile.", this);
            return;
        }

        if (drawObject == null)
        {
            Debug.LogError("Cannot draw cards: assign a Draw Object in the DrawManager.", this);
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            Debug.LogError("Cannot draw cards: no Main Camera exists in the scene.", this);
            return;
        }

        if (!isHoveringDrawObject)
        {
            return;
        }

        DrawCards();
    }

    /// Returns whether the pointer is over the discard pile or one of its visible cards.
    private bool IsPointerOverDiscardPile()
    {
        GameObject topCardVisual = discardManager != null ? discardManager.TopDiscardCardVisual : null;
        if (topCardVisual == null)
        {
            return false;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        RectTransform rectTransform = topCardVisual.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            Canvas canvas = rectTransform.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas != null && canvas.worldCamera != null
                    ? canvas.worldCamera
                    : mainCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(
                rectTransform,
                Input.mousePosition,
                eventCamera);
        }

        if (mainCamera == null)
        {
            return false;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out RaycastHit hit)
            && (hit.transform == topCardVisual.transform
                || hit.transform.IsChildOf(topCardVisual.transform));
    }

    /// Checks whether card interaction is blocked by phase or active UI state.
    private bool IsCardInteractionActive()
    {
        if (gameController == null)
        {
            gameController = FindFirstObjectByType<GameController>();
        }

        if (gameController == null || gameController.CurrentPhase != TurnPhase.Draw)
        {
            return true;
        }

        if (handManager == null)
        {
            handManager = FindFirstObjectByType<HandManager>();
        }

        if (handManager != null && handManager.IsHandDown)
        {
            return true;
        }

        if (CardMovement.IsAnyCardBeingDragged)
        {
            return true;
        }

        if (scoreManager == null)
        {
            scoreManager = FindFirstObjectByType<ScoreManager>();
        }

        if (scoreManager == null || scoreManager.cardsInPlay == null)
        {
            return false;
        }

        foreach (GameObject card in scoreManager.cardsInPlay)
        {
            if (card != null)
            {
                return true;
            }
        }

        return false;
    }

    /// Returns whether the pointer ray intersects the assigned draw object.
    private bool IsPointerOverDrawObject()
    {
        if (drawObject == null)
        {
            return false;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            return false;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out RaycastHit hit)
            && (hit.transform == drawObject.transform || hit.transform.IsChildOf(drawObject.transform));
    }

    /// Raises a pile while hovered and eases it back to its original position.
    private static void UpdatePilePosition(
        Transform pile,
        Vector3 originalLocalPosition,
        bool isHovering,
        float hoverHeight,
        float moveSpeed)
    {
        if (pile == null)
        {
            return;
        }

        Vector3 targetPosition = originalLocalPosition;
        if (isHovering)
        {
            targetPosition += Vector3.up * hoverHeight;
        }

        pile.localPosition = moveSpeed <= 0f
            ? targetPosition
            : Vector3.Lerp(
                pile.localPosition,
                targetPosition,
                1f - Mathf.Exp(-moveSpeed * Time.deltaTime));
    }

    /// Validates the turn state and requests the configured number of deck draws.
    private void DrawCards()
    {
        if (gameController == null)
        {
            gameController = FindFirstObjectByType<GameController>();
        }

        if (gameController == null)
        {
            Debug.LogError("Cannot draw cards: no GameController exists in the scene.", this);
            return;
        }

        if (gameController.CurrentPhase != TurnPhase.Draw)
        {
            Debug.LogWarning($"Cannot draw cards during the {gameController.CurrentPhase} phase.", this);
            return;
        }

        if (!gameController.CanPlayerDrawFromDrawPile)
        {
            Debug.LogWarning("The configured draw rule does not allow drawing from the draw pile.", this);
            return;
        }

        if (deckManager == null)
        {
            Debug.LogError("Cannot draw cards: no DeckManager is assigned or exists in the scene.", this);
            return;
        }

        if (handManager == null)
        {
            Debug.LogError("Cannot draw cards: no HandManager is assigned or exists in the scene.", this);
            return;
        }

        int cardsDrawn = 0;
        for (int i = 0; i < Mathf.Max(0, cardsToDraw); i++)
        {
            if (!deckManager.DrawCard(handManager))
            {
                break;
            }

            cardsDrawn++;
        }

        if (cardsDrawn > 0 && gameController != null && gameController.CurrentPhase == TurnPhase.Draw)
        {
            gameController.AdvancePhase();
        }
    }

    /// Takes the top discard card, if present, and advances to Play after drawing it.
    private void DrawFromDiscardPile()
    {
        if (gameController == null)
        {
            gameController = FindFirstObjectByType<GameController>();
        }

        if (discardManager == null)
        {
            discardManager = FindFirstObjectByType<DiscardManager>();
        }

        if (gameController == null || handManager == null || discardManager == null)
        {
            Debug.LogError("Cannot draw from the discard pile: a required game manager is missing.", this);
            return;
        }

        if (gameController.CurrentPhase != TurnPhase.Draw)
        {
            Debug.LogWarning($"Cannot draw from the discard pile during the {gameController.CurrentPhase} phase.", this);
            return;
        }

        if (!gameController.CanPlayerDrawFromDiscardPile)
        {
            Debug.LogWarning("The configured draw rule does not allow drawing from the discard pile.", this);
            return;
        }

        Card topCard = discardManager.DrawTopCardFromDiscardPile();
        if (topCard == null)
        {
            Debug.LogWarning("Cannot draw from the discard pile because it is empty.", this);
            return;
        }

        handManager.AddCardToHand(topCard);
        Debug.Log($"Drew {topCard.cardName} from the top of the discard pile.", this);
        gameController.AdvancePhase();
    }
}
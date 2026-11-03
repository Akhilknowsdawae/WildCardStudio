using UnityEngine;
using UnityEngine.EventSystems;
using LifeAfter;
using WildCardStudio;

public class DrawManager : MonoBehaviour
{
    [SerializeField] private GameObject drawObject;
    [SerializeField, Min(0)] private int cardsToDraw = 1;
    [SerializeField] private DeckManager deckManager;
    [SerializeField] private HandManager handManager;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private GameController gameController;
    [SerializeField, Min(0f)] private float hoverHeight = 0.2f;
    [SerializeField, Min(0f)] private float hoverMoveSpeed = 10f;

    private Vector3 drawObjectOriginalLocalPosition;
    private Camera mainCamera;

    private void Awake()
    {
        if (drawObject != null)
        {
            drawObjectOriginalLocalPosition = drawObject.transform.localPosition;
        }
    }

    private void Start()
    {
        mainCamera = Camera.main;
        if (deckManager == null)
        {
            deckManager = FindFirstObjectByType<DeckManager>();
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

    private void Update()
    {
        bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool interactionBlocked = IsCardInteractionActive();
        bool isHoveringDrawObject = !interactionBlocked && !pointerOverUI && IsPointerOverDrawObject();
        UpdateDrawObjectPosition(isHoveringDrawObject);

        if (!Input.GetMouseButtonDown(0) || interactionBlocked || pointerOverUI)
        {
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

    private void UpdateDrawObjectPosition(bool isHovering)
    {
        if (drawObject == null)
        {
            return;
        }

        Vector3 targetPosition = drawObjectOriginalLocalPosition;
        if (isHovering)
        {
            targetPosition += Vector3.up * hoverHeight;
        }

        drawObject.transform.localPosition = hoverMoveSpeed <= 0f
            ? targetPosition
            : Vector3.Lerp(
                drawObject.transform.localPosition,
                targetPosition,
                1f - Mathf.Exp(-hoverMoveSpeed * Time.deltaTime));
    }

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
}
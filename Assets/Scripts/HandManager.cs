using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LifeAfter;
using WildCardStudio;
using System;

/// Manages the player's hand, card layout, camera views, and hand interactions.
public class HandManager : MonoBehaviour
{
    public enum HandViewState
    {
        /// Normal hand view.
        Neutral,
        /// Table view with scoring cards shown as a zoomed-in hand.
        ZoomedIn,
        /// View used while the hand is hidden.
        HandsHidden
    }

    private struct ScoreCardCanvasGroupState
    {
        public CanvasGroup canvasGroup;
        public float alpha;
        public bool interactable;
        public bool blocksRaycasts;
    }

    private readonly HashSet<CardMovement> movementsDisabledWhileHandDown = new HashSet<CardMovement>();
    private readonly Dictionary<GameObject, ScoreCardCanvasGroupState> scoreCardCanvasGroupStates =
        new Dictionary<GameObject, ScoreCardCanvasGroupState>();
    private ScoreManager scoreManager;
    private GameController gameController;
    private bool handIsDown;
    private CanvasGroup handCanvasGroup;
    private float handCanvasGroupAlpha = 1f;
    private bool handCanvasGroupInteractable = true;
    private bool handCanvasGroupBlocksRaycasts = true;
    private float targetHandCanvasGroupAlpha = 1f;
    private Vector3 neutralCameraPosition;
    private Quaternion neutralCameraRotation;
    private Vector3 targetCameraPosition;
    private Quaternion targetCameraRotation;
    private Vector3 neutralHandPosition;
    private readonly List<Canvas> canvasesUsingMainCamera = new List<Canvas>();
    private bool tableCameraIsActive;
    private bool activateTableCameraAfterTransition;
    private HandViewState handViewState = HandViewState.Neutral;

    public DeckManager deckManager; //Reference to the DeckManager script
    [SerializeField] private PlayManager playManager;
    [SerializeField] private DiscardManager discardManager;
    public GameObject cardPrefab; //Assign card prefab in the inspector
    public Transform handTransform; //Hand position
    public float fanSpread = 7.55f; //Angle between cards
    public float cardSpacing = 100f; //Spacing between cards
    public float verticalSpacing = 100f; //Vertical spacing between cards
    [Header("Hand Positions")]
    [SerializeField] private Vector3 handPositionUp = new Vector3(0f, 100f, 0f);
    [SerializeField] private Vector3 handPositionDown = Vector3.zero;
    [SerializeField, Min(0f)] private float handPositionMoveSpeed = 8f;
    [SerializeField] private bool updateHandCardPositionsContinuously;
    [SerializeField, Min(0.01f)] private float handFadeDuration = 0.25f;
    [Header("Play View")]
    [SerializeField] private Camera cameraToMove;
    [SerializeField] private Camera tableCamera;
    [SerializeField] private Transform zoomedInCameraPositionReference;
    [SerializeField] private Vector3 zoomedInCameraPositionOffset;
    [SerializeField, Min(0f)] private float cameraMoveSpeed = 3f;
    [SerializeField] private bool updateZoomedInCameraPositionContinuously;
    public List<GameObject> cardsInHand = new List<GameObject>(); //Hold a list of card objects in hand
    
    private Vector3 targetHandPosition;
    public bool IsHandDown => handIsDown;
    public bool IsZoomedIn => handViewState == HandViewState.ZoomedIn;
    public HandViewState CurrentHandViewState => handViewState;
    public PlayManager PlayManager => playManager;

    /// Resolves scene references and initializes hand and camera state.
    void Start()
    {
        scoreManager = FindFirstObjectByType<ScoreManager>();
        gameController = FindFirstObjectByType<GameController>();
        if (handTransform != null)
        {
            targetHandPosition = handTransform.localPosition;
            neutralHandPosition = handTransform.localPosition;
            handCanvasGroup = handTransform.GetComponent<CanvasGroup>();
            if (handCanvasGroup != null)
            {
                handCanvasGroupAlpha = handCanvasGroup.alpha;
                handCanvasGroupInteractable = handCanvasGroup.interactable;
                handCanvasGroupBlocksRaycasts = handCanvasGroup.blocksRaycasts;
            }
        }

        if (cameraToMove == null)
        {
            cameraToMove = Camera.main;
        }

        if (tableCamera == cameraToMove && tableCamera != null)
        {
            Debug.LogError("The Table Camera must be different from the Camera To Move.", this);
            tableCamera = null;
        }

        if (cameraToMove != null)
        {
            neutralCameraPosition = cameraToMove.transform.position;
            neutralCameraRotation = cameraToMove.transform.rotation;
            targetCameraPosition = neutralCameraPosition;
            targetCameraRotation = neutralCameraRotation;
        }

        if (tableCamera != null && cameraToMove != null)
        {
            foreach (Canvas sceneCanvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (sceneCanvas.worldCamera == cameraToMove)
                {
                    canvasesUsingMainCamera.Add(sceneCanvas);
                }
            }

            SetActiveViewCamera(cameraToMove);
        }
    }

    /// Creates a card object for the supplied card asset and adds it to the hand.
    public void AddCardToHand(Card cardData)
    {
        //Creating the cards here
        GameObject newCard = Instantiate(cardPrefab, handTransform.position, Quaternion.identity, handTransform);
        cardsInHand.Add(newCard);
        CardMovement movement = newCard.GetComponent<CardMovement>();
        movement.SetHandManager(this);
        if (handIsDown)
        {
            DisableHandCardMovement(movement);
        }
        
        newCard.GetComponent<CardDisplay>().cardData = cardData; //Set the card data to the new card

        UpdateCardPositions();
    }

    /// Removes a card object from the hand and relayouts remaining cards.
    public void RemoveCardFromHand(GameObject card)
    {
        if (cardsInHand.Remove(card))
        {
            UpdateCardPositions();
        }
    }

    /// Restores a card object to the hand if it is not already present.
    public bool ReturnCardToHand(GameObject card)
    {
        if (card == null)
        {
            Debug.LogError("Cannot return a null card to the hand.", this);
            return false;
        }

        if (handTransform == null)
        {
            Debug.LogError("Cannot return card to hand: assign a Hand Transform.", this);
            return false;
        }

        if (!cardsInHand.Contains(card))
        {
            card.transform.SetParent(handTransform, false);
            card.transform.SetAsLastSibling();
            cardsInHand.Add(card);
        }

        CardMovement movement = card.GetComponent<CardMovement>();
        if (movement != null)
        {
            movement.SetHandManager(this);
            movement.MarkAsReturnedToHand();
            if (handIsDown)
            {
                DisableHandCardMovement(movement);
            }
        }

        UpdateCardPositions();
        CardDisplay cardDisplay = card.GetComponent<CardDisplay>();
        if (cardDisplay != null)
        {
            cardDisplay.UpdateCardDisplay();
        }

        return true;
    }

    void Update()
    {
        UpdateHandPosition();
        UpdateHandFade();
        UpdateCameraPosition();

        bool playPhase = gameController != null && gameController.CurrentPhase == TurnPhase.Play;
        if (!playPhase && handViewState != HandViewState.Neutral)
        {
            SetHandViewState(HandViewState.Neutral);
        }

        if (playPhase && Input.GetKeyDown(KeyCode.UpArrow))
        {
            if (handViewState == HandViewState.Neutral)
            {
                SetHandViewState(HandViewState.ZoomedIn);
            }
            else
            {
                SetHandViewState(HandViewState.Neutral);
            }
        }
        else if (playPhase && Input.GetKeyDown(KeyCode.DownArrow))
        {
            if (handViewState == HandViewState.Neutral)
            {
                SetHandViewState(HandViewState.HandsHidden);
            }
            else
            {
                SetHandViewState(HandViewState.Neutral);
            }
        }
        else if (!playPhase && Input.GetKeyDown(KeyCode.UpArrow))
        {
            SetHandPosition(handPositionUp, false);
        }
        else if (!playPhase && Input.GetKeyDown(KeyCode.DownArrow))
        {
            SetHandPosition(handPositionDown, true);
        }

        bool enterPressed = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (!handIsDown && enterPressed
            && (gameController == null || !gameController.EnterAdvancesPhaseEnabled))
        {
            if (discardManager == null)
            {
                discardManager = FindFirstObjectByType<DiscardManager>();
            }

            if (discardManager == null)
            {
                Debug.LogError("Cannot discard cards: no DiscardManager exists in the scene.", this);
            }
            else
            {
                discardManager.HandleEnter(this, scoreManager);
            }
        }

        if (Input.GetKeyDown(KeyCode.Space)
            && (gameController == null || gameController.CurrentPhase == TurnPhase.Play))
        {
            if (handViewState != HandViewState.ZoomedIn && handIsDown)
            {
                return;
            }

            if (playManager == null)
            {
                Debug.LogError("Cannot play selected cards: assign a PlayManager in the HandManager.", this);
            }
            else if (handViewState == HandViewState.ZoomedIn)
            {
                playManager.ValidateZoomedInCards(this);
            }
            else
            {
                playManager.PlaySelectedCards(this);
            }
        }

        if (updateHandCardPositionsContinuously)
        {
            UpdateCardPositions();
        }
    }

    /// Moves the hand transform and updates the down/up state.
    private void SetHandPosition(Vector3 position, bool moveHandDown)
    {
        if (handTransform == null)
        {
            Debug.LogError("Cannot move the hand: assign a Hand Transform in the HandManager.", this);
            return;
        }

        targetHandPosition = position;
        if (handIsDown == moveHandDown)
        {
            return;
        }

        handIsDown = moveHandDown;
        if (handIsDown)
        {
            SetHandCardMovementEnabled(false);
            FadeScoreCards();
        }
        else
        {
            SetHandCardMovementEnabled(true);
            RestoreScoreCards();
        }
    }

    /// Updates the current view state and its associated camera and UI transitions.
    private void SetHandViewState(HandViewState nextState)
    {
        if (nextState == handViewState)
        {
            return;
        }

        if (nextState == HandViewState.ZoomedIn)
        {
            if (playManager == null)
            {
                Debug.LogError("Cannot enter the zoomed-in view: assign a PlayManager in the HandManager.", this);
                return;
            }

            if (cameraToMove == null
                || (tableCamera == null && zoomedInCameraPositionReference == null))
            {
                Debug.LogError(
                    "Cannot enter the zoomed-in view: assign the Camera To Move and Table Camera (or a Zoomed In Camera Position Reference).",
                    this);
                return;
            }

            if (!playManager.BeginZoomedInView(this))
            {
                return;
            }
        }
        else if (handViewState == HandViewState.ZoomedIn)
        {
            SwitchToMainCamera();
            if (playManager != null)
            {
                playManager.EndZoomedInView();
            }
        }

        handViewState = nextState;
        handIsDown = handViewState != HandViewState.Neutral;
        if (handViewState == HandViewState.Neutral)
        {
            targetHandPosition = neutralHandPosition;
            targetCameraPosition = neutralCameraPosition;
            targetCameraRotation = neutralCameraRotation;
            RestoreHandVisibility();
            RestoreScoreCards();
            SetHandCardMovementEnabled(true);
            return;
        }

        HideHandCards();
        FadeScoreCards(0f);
        SetHandCardMovementEnabled(false);
        if (handViewState == HandViewState.ZoomedIn)
        {
            activateTableCameraAfterTransition = tableCamera != null;
            UpdateCameraTargetFromReference();
        }
    }

    private void HideHandCards()
    {
        if (handTransform == null)
        {
            Debug.LogError("Cannot hide the hand: assign a Hand Transform in the HandManager.", this);
            return;
        }

        if (handCanvasGroup == null)
        {
            handCanvasGroup = handTransform.GetComponent<CanvasGroup>();
            if (handCanvasGroup == null)
            {
                handCanvasGroup = handTransform.gameObject.AddComponent<CanvasGroup>();
            }

            handCanvasGroupAlpha = handCanvasGroup.alpha;
            handCanvasGroupInteractable = handCanvasGroup.interactable;
            handCanvasGroupBlocksRaycasts = handCanvasGroup.blocksRaycasts;
        }

        targetHandCanvasGroupAlpha = 0f;
        handCanvasGroup.interactable = false;
        handCanvasGroup.blocksRaycasts = false;
    }

    private void RestoreHandVisibility()
    {
        if (handCanvasGroup == null)
        {
            return;
        }

        targetHandCanvasGroupAlpha = handCanvasGroupAlpha;
        if (Mathf.Approximately(handCanvasGroup.alpha, targetHandCanvasGroupAlpha))
        {
            handCanvasGroup.interactable = handCanvasGroupInteractable;
            handCanvasGroup.blocksRaycasts = handCanvasGroupBlocksRaycasts;
        }
        else
        {
            handCanvasGroup.interactable = false;
            handCanvasGroup.blocksRaycasts = false;
        }
    }

    private void UpdateHandFade()
    {
        if (handCanvasGroup == null)
        {
            return;
        }

        float fadeSpeed = 1f / Mathf.Max(0.01f, handFadeDuration);
        handCanvasGroup.alpha = Mathf.MoveTowards(
            handCanvasGroup.alpha,
            targetHandCanvasGroupAlpha,
            fadeSpeed * Time.deltaTime);

        if (Mathf.Approximately(handCanvasGroup.alpha, targetHandCanvasGroupAlpha))
        {
            handCanvasGroup.interactable = targetHandCanvasGroupAlpha > 0f && handCanvasGroupInteractable;
            handCanvasGroup.blocksRaycasts = targetHandCanvasGroupAlpha > 0f && handCanvasGroupBlocksRaycasts;
        }
    }

    private void UpdateCameraPosition()
    {
        if (cameraToMove == null || tableCameraIsActive)
        {
            return;
        }

        if (handViewState == HandViewState.ZoomedIn && updateZoomedInCameraPositionContinuously)
        {
            UpdateCameraTargetFromReference();
        }

        if (cameraMoveSpeed <= 0f)
        {
            cameraToMove.transform.SetPositionAndRotation(targetCameraPosition, targetCameraRotation);
            ActivateTableCameraWhenReady();
            return;
        }

        float interpolation = 1f - Mathf.Exp(-cameraMoveSpeed * Time.deltaTime);
        cameraToMove.transform.position = Vector3.Lerp(
            cameraToMove.transform.position,
            targetCameraPosition,
            interpolation);
        cameraToMove.transform.rotation = Quaternion.Slerp(
            cameraToMove.transform.rotation,
            targetCameraRotation,
            interpolation);
        ActivateTableCameraWhenReady();
    }

    private void UpdateCameraTargetFromReference()
    {
        Transform cameraReference = tableCamera != null
            ? tableCamera.transform
            : zoomedInCameraPositionReference;
        if (cameraReference == null)
        {
            return;
        }

        targetCameraPosition = cameraReference.TransformPoint(zoomedInCameraPositionOffset);
        targetCameraRotation = cameraReference.rotation;
    }

    private void ActivateTableCameraWhenReady()
    {
        if (!activateTableCameraAfterTransition || tableCamera == null
            || Vector3.Distance(cameraToMove.transform.position, targetCameraPosition) > 0.01f
            || Quaternion.Angle(cameraToMove.transform.rotation, targetCameraRotation) > 0.1f)
        {
            return;
        }

        cameraToMove.transform.SetPositionAndRotation(targetCameraPosition, targetCameraRotation);
        tableCamera.transform.SetPositionAndRotation(targetCameraPosition, targetCameraRotation);
        SetActiveViewCamera(tableCamera);
        tableCameraIsActive = true;
        activateTableCameraAfterTransition = false;
    }

    private void SwitchToMainCamera()
    {
        activateTableCameraAfterTransition = false;
        if (!tableCameraIsActive || tableCamera == null || cameraToMove == null)
        {
            return;
        }

        cameraToMove.transform.SetPositionAndRotation(
            tableCamera.transform.position,
            tableCamera.transform.rotation);
        SetActiveViewCamera(cameraToMove);
        tableCameraIsActive = false;
    }

    private void SetActiveViewCamera(Camera activeCamera)
    {
        bool useTableCamera = tableCamera != null && activeCamera == tableCamera;
        if (useTableCamera && !tableCamera.gameObject.activeSelf)
        {
            tableCamera.gameObject.SetActive(true);
        }

        if (cameraToMove != null)
        {
            cameraToMove.enabled = activeCamera == cameraToMove;
        }

        if (tableCamera != null)
        {
            tableCamera.enabled = useTableCamera;
        }

        AudioListener mainListener = cameraToMove != null
            ? cameraToMove.GetComponent<AudioListener>()
            : null;
        AudioListener tableListener = tableCamera != null
            ? tableCamera.GetComponent<AudioListener>()
            : null;
        if (mainListener != null)
        {
            mainListener.enabled = activeCamera != tableCamera || tableListener == null;
        }

        if (tableListener != null)
        {
            tableListener.enabled = activeCamera == tableCamera || mainListener == null;
        }

        foreach (Canvas sceneCanvas in canvasesUsingMainCamera)
        {
            if (sceneCanvas != null)
            {
                sceneCanvas.worldCamera = activeCamera;
            }
        }

        if (tableCamera != null && !useTableCamera && tableCamera.gameObject.activeSelf)
        {
            tableCamera.gameObject.SetActive(false);
        }
    }

    /// Attempts to stage a selected hand card beside a target played combo.
    public bool TryStageCardForCombo(GameObject card, GameObject targetCard)
    {
        if (playManager == null)
        {
            Debug.LogError("Cannot add a card to a table combo: assign a PlayManager in the HandManager.", this);
            return false;
        }

        return playManager.TryStageCardForCombo(card, targetCard);
    }

    private void UpdateHandPosition()
    {
        if (handTransform == null)
        {
            return;
        }

        if (handPositionMoveSpeed <= 0f)
        {
            handTransform.localPosition = targetHandPosition;
            return;
        }

        handTransform.localPosition = Vector3.Lerp(
            handTransform.localPosition,
            targetHandPosition,
            1f - Mathf.Exp(-handPositionMoveSpeed * Time.deltaTime));
    }

    private void SetHandCardMovementEnabled(bool enabled)
    {
        if (enabled)
        {
            foreach (CardMovement movement in movementsDisabledWhileHandDown)
            {
                if (movement != null)
                {
                    movement.enabled = true;
                }
            }

            movementsDisabledWhileHandDown.Clear();
            return;
        }

        foreach (GameObject card in cardsInHand)
        {
            CardMovement movement = card != null ? card.GetComponent<CardMovement>() : null;
            DisableHandCardMovement(movement);
        }
    }

    private void DisableHandCardMovement(CardMovement movement)
    {
        if (movement != null && movement.enabled && movementsDisabledWhileHandDown.Add(movement))
        {
            movement.enabled = false;
        }
    }

    private void FadeScoreCards(float alpha = -1f)
    {
        if (scoreManager == null)
        {
            scoreManager = FindFirstObjectByType<ScoreManager>();
        }

        if (scoreManager == null)
        {
            Debug.LogError("Cannot fade score cards: no ScoreManager exists in the scene.", this);
            return;
        }

        foreach (GameObject card in scoreManager.cardsInPlay)
        {
            if (card == null || scoreCardCanvasGroupStates.ContainsKey(card))
            {
                continue;
            }

            CanvasGroup canvasGroup = card.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = card.AddComponent<CanvasGroup>();
            }

            scoreCardCanvasGroupStates.Add(card, new ScoreCardCanvasGroupState
            {
                canvasGroup = canvasGroup,
                alpha = canvasGroup.alpha,
                interactable = canvasGroup.interactable,
                blocksRaycasts = canvasGroup.blocksRaycasts
            });
            canvasGroup.alpha = alpha < 0f ? scoreManager.ScoreCardsAlphaWhenHandDown : alpha;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void RestoreScoreCards()
    {
        foreach (KeyValuePair<GameObject, ScoreCardCanvasGroupState> cardState in scoreCardCanvasGroupStates)
        {
            ScoreCardCanvasGroupState state = cardState.Value;
            if (cardState.Key != null && state.canvasGroup != null)
            {
                state.canvasGroup.alpha = state.alpha;
                state.canvasGroup.interactable = state.interactable;
                state.canvasGroup.blocksRaycasts = state.blocksRaycasts;
            }
        }

        scoreCardCanvasGroupStates.Clear();
    }

    /// Recalculates the fan layout for every card in the hand.
    private void UpdateCardPositions()
    {
        int cardCount = cardsInHand.Count;

        if (cardCount == 1)
        {
            cardsInHand[0].transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            cardsInHand[0].transform.localPosition = new Vector3(0f, 0f, 0f);
            return;
        }

        for (int i=0; i < cardCount; i++)
        {
            float rotationAngle = (fanSpread * ( i - (cardCount - 1) / 2)); //Calculate rotation for each card
            cardsInHand[i].transform.localRotation = Quaternion.Euler(0f, 0f, rotationAngle); //Apply rotation

            //Hand spread
            float horizontalOffset = (cardSpacing * ( i - (cardCount - 1) / 2));; //Calculate horizontal offset for each card
            
            float normalizedPosition = (2f * i / (cardCount - 1) - 1f); //Normalized position between -1 and 1
            float verticalOffset = verticalSpacing * (1 - normalizedPosition * normalizedPosition); //Calculate vertical offset for each card
            
            //set card positions
            cardsInHand[i].transform.localPosition = new Vector3(horizontalOffset, verticalOffset, 0f); //Apply position
        }
    }
}
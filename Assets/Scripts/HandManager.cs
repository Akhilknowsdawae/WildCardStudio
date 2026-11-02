using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LifeAfter;
using WildCardStudio;
using System;

public class HandManager : MonoBehaviour
{
    private readonly HashSet<CardMovement> movementsDisabledWhileHandDown = new HashSet<CardMovement>();
    private readonly Dictionary<GameObject, bool> scoreCardActiveStates = new Dictionary<GameObject, bool>();
    private ScoreManager scoreManager;
    private bool handIsDown;

    public DeckManager deckManager; //Reference to the DeckManager script
    [SerializeField] private PlayManager playManager;
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
    public List<GameObject> cardsInHand = new List<GameObject>(); //Hold a list of card objects in hand
    
    private Vector3 targetHandPosition;
    public bool IsHandDown => handIsDown;

    void Start()
    {
        scoreManager = FindFirstObjectByType<ScoreManager>();
        if (handTransform != null)
        {
            targetHandPosition = handTransform.localPosition;
        }
    }

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

    public void RemoveCardFromHand(GameObject card)
    {
        if (cardsInHand.Remove(card))
        {
            UpdateCardPositions();
        }
    }

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

        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            SetHandPosition(handPositionUp, false);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            SetHandPosition(handPositionDown, true);
        }

        if (!handIsDown && Input.GetKeyDown(KeyCode.Space))
        {
            if (playManager == null)
            {
                Debug.LogError("Cannot play selected cards: assign a PlayManager in the HandManager.", this);
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
            HideScoreCards();
        }
        else
        {
            SetHandCardMovementEnabled(true);
            RestoreScoreCards();
        }
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

    private void HideScoreCards()
    {
        if (scoreManager == null)
        {
            scoreManager = FindFirstObjectByType<ScoreManager>();
        }

        if (scoreManager == null)
        {
            Debug.LogError("Cannot hide score cards: no ScoreManager exists in the scene.", this);
            return;
        }

        foreach (GameObject card in scoreManager.cardsInPlay)
        {
            if (card == null || scoreCardActiveStates.ContainsKey(card))
            {
                continue;
            }

            scoreCardActiveStates.Add(card, card.activeSelf);
            card.SetActive(false);
        }
    }

    private void RestoreScoreCards()
    {
        foreach (KeyValuePair<GameObject, bool> cardState in scoreCardActiveStates)
        {
            if (cardState.Key != null)
            {
                cardState.Key.SetActive(cardState.Value);
            }
        }

        scoreCardActiveStates.Clear();
    }

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
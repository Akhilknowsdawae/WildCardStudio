using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LifeAfter;
using System;

public class HandManager : MonoBehaviour
{
    public DeckManager deckManager; //Reference to the DeckManager script
    public GameObject cardPrefab; //Assign card prefab in the inspector
    public Transform handTransform; //Hand position
    public float fanSpread = 7.55f; //Angle between cards
    public float cardSpacing = 100f; //Spacing between cards
    public float verticalSpacing = 100f; //Vertical spacing between cards
    public List<GameObject> cardsInHand = new List<GameObject>(); //Hold a list of card objects in hand
    
    void Start()
    {
        
    }

    public void AddCardToHand(Card cardData)
    {
        //Creating the cards here
        GameObject newCard = Instantiate(cardPrefab, handTransform.position, Quaternion.identity, handTransform);
        cardsInHand.Add(newCard);
        
        newCard.GetComponent<CardDisplay>().cardData = cardData; //Set the card data to the new card

        UpdateCardPositions();
    }

    void Update()
    {
        UpdateCardPositions();
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

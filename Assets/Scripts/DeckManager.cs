using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LifeAfter;
using WildCardStudio;
using System;

public class DeckManager : MonoBehaviour
{
    public List<Card> allCards = new List<Card>(); //List of cards in the deck

    private int currentIndex = 0; //Index to keep track of the current card

    void Start()
    {
        //Load all card assets from the reo
        Card[] cards = Resources.LoadAll<Card>("Cards");

        //add the loaded cards to the allCards list
        allCards.AddRange(cards);

        // Find the managers that control the starting hand.
        HandManager hand = FindFirstObjectByType<HandManager>();
        GameController gameController = FindFirstObjectByType<GameController>();
        if (hand == null)
        {
            Debug.LogError("Cannot deal starting cards: no HandManager exists in the scene.", this);
            return;
        }

        if (gameController == null)
        {
            Debug.LogError("Cannot deal starting cards: no GameController exists in the scene.", this);
            return;
        }

        for (int i = 0; i < gameController.StartingHandSize; i++)
        {
            DrawCard(hand);
        }
    }

    public void DrawCard(HandManager handManager)
    {
        if (allCards.Count == 0)
        {
            Debug.LogWarning("Deck is empty!");
            return;
        }

        Card nextCard = allCards[currentIndex]; //Get the next card from the deck'
        handManager.AddCardToHand(nextCard); //Add the card to the hand
        currentIndex = (currentIndex + 1) % allCards.Count; //Move to the next card, loop back to start if at the end
    }
}

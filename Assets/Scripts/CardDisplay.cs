using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using LifeAfter;

// Code used from Sinous: https://youtu.be/j36rIYTvLkI?si=ChnzjeBP1k1vf6VF

/// Displays a Card data asset on a card UI object.
public class CardDisplay : MonoBehaviour
{
    /// Source card data rendered by this component.
    public Card cardData;

    /// Image used for the card artwork.
    public Image cardImage;
    /// Text used for the card rank or special rank letter.
    public TMP_Text rankText;
    /// Suit indicator images, ordered by Suit enum value.
    public Image[] typeImages;

    /// Stores the artwork fallback displayed when the card has no image.
    private Sprite defaultCardSprite;

    /// Captures the default artwork before card data is assigned.
    private void Awake()
    {
        if (cardImage != null)
        {
            defaultCardSprite = cardImage.sprite;
        }
    }

    /// Performs the initial UI update after Unity initializes the component.
    private void Start()
    {
        UpdateCardDisplay();
    }

    /// Refreshes the card image, rank, and all suit indicators.
    public void UpdateCardDisplay()
    {
        if (cardImage != null)
        {
            cardImage.sprite = cardData != null && cardData.cardImage != null
                ? cardData.cardImage
                : defaultCardSprite;
        }

        if (rankText != null)
        {
            rankText.text = GetDisplayedRank();
        }

        UpdateSuitImages();
    }

    /// Returns the displayed rank text for the assigned card.
    private string GetDisplayedRank()
    {
        if (cardData == null)
        {
            return string.Empty;
        }

        if (cardData.cardType != null)
        {
            if (cardData.cardType.Contains(WildCardStudio.CardType.King))
            {
                return "K";
            }

            if (cardData.cardType.Contains(WildCardStudio.CardType.Queen))
            {
                return "Q";
            }

            if (cardData.cardType.Contains(WildCardStudio.CardType.Jack))
            {
                return "J";
            }
        }

        return cardData.cardRank.ToString();
    }

    /// Updates which suit indicators are visible for the assigned card.
    public void UpdateSuitImages()
    {
        if (typeImages == null)
        {
            return;
        }

        int suitCount = System.Enum.GetValues(typeof(WildCardStudio.Suit)).Length;
        for (int i = 0; i < typeImages.Length; i++)
        {
            Image suitImage = typeImages[i];
            if (suitImage == null)
            {
                continue;
            }

            suitImage.enabled = cardData != null
                && cardData.suit != null
                && i < suitCount
                && cardData.suit.Contains((WildCardStudio.Suit)i);
            suitImage.gameObject.SetActive(suitImage.enabled);
        }
    }
}

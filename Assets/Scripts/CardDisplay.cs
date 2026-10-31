using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using LifeAfter;

public class CardDisplay : MonoBehaviour
{
    public Card cardData;

    public Image cardImage;
    public TMP_Text rankText;
    public Image[] typeImages;

    private Sprite defaultCardSprite;

    private void Awake()
    {
        if (cardImage != null)
        {
            defaultCardSprite = cardImage.sprite;
        }
    }

    private void Start()
    {
        UpdateCardDisplay();
    }

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

using UnityEngine;
using TMPro;
using Ink.Runtime;
using System.Collections;
using System.Collections.Generic;
using Ink.Parsed;

public class DialogueSystem : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI dialogueText;
    [SerializeField] GameObject dialoguePanel;
    [Header("Text")]
    [Range(0.01f, 2f)]
    [SerializeField] float normalTypingSpeed = 0.1f;
    [Range(0.01f, 2f)]
    [SerializeField] float shortPauseSpeed = 0.15f;
    [Range(0.01f, 2f)]
    [SerializeField] float longPauseSpeed = 0.2f;
    [Header("Audio")]
    [SerializeField] private DialogueAudioInfoSO defaultBlipInfo;
    [SerializeField] private DialogueAudioInfoSO[] blipInfos;
    [SerializeField] private bool makePredictable;

    private DialogueAudioInfoSO currentBlipInfo;
    private Dictionary<string, DialogueAudioInfoSO> blipInfoDictionary;
    private AudioSource audioSource;
    private Script enemyDialogue;
    private string currentEnemy;
    private bool isDialogueActive = false;  //is there currently dialogue on screen
    private bool isLineComplete = false;    //is the dialogue finished typing out
    private bool viewLineNow = false;       //flag to display entire line instantly because we don't have a proper input system set up yet
    private float currentPitchDrift = 0;
    private AudioClip[] dialogueBlips;
    private bool stopAudioSourceOverlap;
    private int blipFrequency;
    private float minPitch = 0.5f;
    private float maxPitch = 1.5f;
    private bool driftPitch = true;
    private float pitchDriftAmount = 0.05f;
    private float maxPitchDrift = 1f;


    void Awake()
    {
        audioSource = this.gameObject.AddComponent<AudioSource>();
        currentBlipInfo = defaultBlipInfo;
    }

    void Start()
    {
        enemyDialogue = GetComponent<Script>();

        InitializeBlipInfoDictionary();

        SetCurrentEnemy("fishhead"); //for prototype purposes.



        ShowDialoguePanel();
        ShowIntroDialogue(); //for testing
    }


    void Update()
    {
        if (!isDialogueActive)
        {
            return;
        }
        if (Input.GetMouseButtonDown(0))
        {
            Debug.Log("left mouse click");

            if (isLineComplete)
            {
                Debug.Log("closing dialogue box");
                EndDialogue();
            }
            else
            {
                viewLineNow = true;
            }

        }
    }

    //types text out letter by letter
    private IEnumerator TypeText()
    {
        dialogueText.maxVisibleCharacters = 0;

        foreach (char letter in dialogueText.text)
        {
            //clunky cause no proper input system yet
            //if the mouse is clicked while the line is still typing (code in Update)
            //then it just makes all the characters visible instantly
            if (viewLineNow)
            {
                Debug.Log("displaying entire line");
                dialogueText.maxVisibleCharacters = dialogueText.text.Length;
                viewLineNow = false;
                break;
            }

            PlayDialogueSound(dialogueText.maxVisibleCharacters, dialogueText.text[dialogueText.maxVisibleCharacters]);
            dialogueText.maxVisibleCharacters++;

            float typingSpeed = SelectTypingSpeed(dialogueText.text[dialogueText.maxVisibleCharacters - 1]);

            yield return new WaitForSeconds(typingSpeed);
        }
        isLineComplete = true;
    }

    private void PlayDialogueSound(int currentDisplayedCharacterCount, char currentCharacter)
    {

        //we always want to reset the pitch drift at punctuation, even if that specific character isn't playing a blip
        if (driftPitch)
        {
            if (CheckForPitchDriftReset(currentCharacter))
            {
                //Debug.Log("resetting pitch drift");
                currentPitchDrift = 0;
            }
        }
        if (currentDisplayedCharacterCount % blipFrequency == 0)
        {
            if (stopAudioSourceOverlap)
            {
                audioSource.Stop();
            }
            AudioClip blip = null;
            //make the randomness consistent between characters with hashing
            //thank you youtube video
            if (makePredictable)
            {
                //select predictably random clip
                int hashcode = currentCharacter.GetHashCode();
                int predictableIndex = hashcode % dialogueBlips.Length;
                blip = dialogueBlips[predictableIndex];

                //predictably randomize pitch
                int minPitchInt = (int)(minPitch * 100);
                int maxPitchInt = (int)(maxPitch * 100);
                int pitchRangeInt = maxPitchInt - minPitchInt;
                //in case there's somehow no range (so we don't divide by 0)
                if (pitchRangeInt != 0)
                {
                    int predictablePitchInt = (hashcode % pitchRangeInt) + minPitchInt;
                    float predictablePitch = predictablePitchInt / 100f;
                    audioSource.pitch = predictablePitch;
                }
                else
                {
                    audioSource.pitch = minPitch;
                }
            }
            else
            {
                //select random clip
                int randomIndex = Random.Range(0, dialogueBlips.Length);
                blip = dialogueBlips[randomIndex];

                //randomize pitch
                audioSource.pitch = Random.Range(minPitch, maxPitch);
            }

            //drift pitch
            if (driftPitch)
            {
                currentPitchDrift += pitchDriftAmount;

                if (currentPitchDrift > maxPitchDrift)
                {
                    currentPitchDrift = maxPitchDrift;
                }

                audioSource.pitch -= currentPitchDrift;
            }

            //play sound
            audioSource.PlayOneShot(blip);
        }
    }
    private bool CheckForPitchDriftReset(char character)
    {
        switch (character)
        {
            case ',':
            case '.':
            case '!':
            case '?':
                return true;
        }
        return false;
    }

    private float SelectTypingSpeed(char character)
    {
        float speed;

        switch (character)
        {
            case ',':
                speed = shortPauseSpeed;
                break;
            case '.':
            case '!':
            case '?':
                speed = longPauseSpeed;
                break;
            default:
                speed = normalTypingSpeed;
                break;
        }


        return speed;
    }

    private void SetCurrentBlipInfo()
    {
        DialogueAudioInfoSO blipInfo = null;
        blipInfoDictionary.TryGetValue(currentEnemy, out blipInfo);
        if (blipInfo != null)
        {
            this.currentBlipInfo = blipInfo;
        }
        else
        {
            Debug.Log("enemy " + currentEnemy + "does not have blip info");
            this.currentBlipInfo = defaultBlipInfo;
        }

        UpdateBlipVariablesWithConfig();
    }
    private void UpdateBlipVariablesWithConfig()
    {
        //set variables based on blip audio config
        dialogueBlips = currentBlipInfo.dialogueBlips;
        stopAudioSourceOverlap = currentBlipInfo.stopAudioSourceOverlap;
        blipFrequency = currentBlipInfo.blipFrequency;
        minPitch = currentBlipInfo.minPitch;
        maxPitch = currentBlipInfo.maxPitch;
        driftPitch = currentBlipInfo.driftPitch;
        pitchDriftAmount = currentBlipInfo.pitchDriftAmount;
        maxPitchDrift = currentBlipInfo.maxPitchDrift;
    }

    private void InitializeBlipInfoDictionary()
    {
        blipInfoDictionary = new Dictionary<string, DialogueAudioInfoSO>();
        blipInfoDictionary.Add(defaultBlipInfo.id, defaultBlipInfo);
        foreach (DialogueAudioInfoSO blipInfo in blipInfos)
        {
            blipInfoDictionary.Add(blipInfo.id, blipInfo);
        }
    }

    //call this to make the speech bubble appear before you call any of the text
    public void ShowDialoguePanel()
    {
        dialoguePanel.SetActive(true);
        isDialogueActive = true;
        isLineComplete = false;
    }

    //call this when the enemy should be saying their intro text
    public void ShowIntroDialogue()
    {
        dialogueText.text = enemyDialogue.GetIntroText(currentEnemy);
        StartCoroutine(TypeText());
    }

    //call this when the enemy should be saying their victory text
    public void ShowWinDialogue()
    {
        dialogueText.text = enemyDialogue.GetWinText(currentEnemy);
        StartCoroutine(TypeText());
    }

    //call this when the enemy should be saying their losing text
    public void ShowLoseDialogue()
    {
        dialogueText.text = enemyDialogue.GetLoseText(currentEnemy);
        StartCoroutine(TypeText());
    }

    //call this to make them shut the hell up
    public void EndDialogue()
    {
        dialogueText.text = null;
        dialoguePanel.SetActive(false);
        isDialogueActive = false;
    }

    public string GetCurrentEnemy()
    {
        return currentEnemy;
    }
    public void SetCurrentEnemy(string newEnemy)
    {
        currentEnemy = newEnemy;
        SetCurrentBlipInfo();
    }
}

//heavily refrenced this video on how to do the audio blips
//How to Create Dialogue Audio like in Celeste and Animal Crossing using Unity
//https://youtu.be/P3FcXHEai_E?si=Mkoq8QHMQm23ODGU
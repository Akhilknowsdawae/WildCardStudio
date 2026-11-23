using UnityEngine;
using TMPro;
using Ink.Runtime;

public class DialogueSystem : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI dialogueText;
    [SerializeField] GameObject dialoguePanel;
    private Script enemyDialogue;
    private string currentEnemy;


    void Start()
    {
        enemyDialogue = GetComponent<Script>();
        SetCurrentEnemy("fishhead"); //for prototype purposes.

        ShowIntroDialogue(); //for testing
    }


    void Update()
    {

    }

    //call this to make the speech bubble appear before you call any of the text
    public void ShowDialoguePanel()
    {
        dialoguePanel.SetActive(true);
    }

    //call this when the enemy should be saying their intro text
    public void ShowIntroDialogue()
    {
        dialogueText.text = enemyDialogue.GetIntroText(currentEnemy);
    }

    //call this when the enemy should be saying their victory text
    public void ShowWinDialogue()
    {
        dialogueText.text = enemyDialogue.GetWinText(currentEnemy);
    }

    //call this when the enemy should be saying their losing text
    public void ShowLoseDialogue()
    {
        dialogueText.text = enemyDialogue.GetLoseText(currentEnemy);
    }

    //call this to make them shut the hell up
    public void EndDialogue()
    {
        dialogueText.text = null;
        dialoguePanel.SetActive(false);
    }

    public string GetCurrentEnemy()
    {
        return currentEnemy;
    }
    public void SetCurrentEnemy(string newEnemy)
    {
        currentEnemy = newEnemy;
    }
}

using UnityEngine;
using TMPro;

public class DialogueSystem : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI dialogueText;
    [SerializeField] GameObject dialoguePanel;

    void Start()
    {

    }


    void Update()
    {

    }


    public void ShowDialogue(string dialogue, string name)
    {
        dialogueText.text = dialogue;
        dialoguePanel.SetActive(true);
    }

    public void EndDialogue()
    {
        dialogueText.text = null; ;
        dialoguePanel.SetActive(false);
    }
}

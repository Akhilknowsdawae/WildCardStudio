using Ink.Runtime;
using UnityEngine;

public class Script : MonoBehaviour
{

    public TextAsset inkAsset;

    Story _inkStory;

    void Awake()
    {
        _inkStory = new Story(inkAsset.text);
    }

    //ink files are organized by knots and stitches
    //for our purposes, knots are the different enemies, and stitches are their different bits of dialogue (intro, win, etc)
    //to refrence a specific bit of dialogue, you use the path format "knot.stitch"
    //for us it'll be something like "enemyName.intro" or "enemyName.win"

    //takes the enemy's name as a string and uses it to find and return the corresponding intro text
    public string GetIntroText(string enemyName)
    {
        string path = string.Concat(enemyName, ".intro");
        string text;
        //as far as I can tell, ink just throws an error at you if you give it an invalid path
        //so I made it if you give an invalid path, it goes to a default enemy dialogue instead yay
        try
        {
            _inkStory.ChoosePathString(path); //ChoosePathString is the function for going to a specific knot/stitch
        }
        catch
        {
            Debug.Log("Invalid enemy name. Using default enemy dialogue.");
            _inkStory.ChoosePathString("defaultenemy.intro");
        }

        text = _inkStory.Continue(); //Continue returns the next line of the ink file as a string
        return text;
    }

    //both GetWinText and GetLoseText are essentially identical to GetIntroText but with a different stitch path 
    public string GetWinText(string enemyName)
    {
        string path = string.Concat(enemyName, ".win");
        string text;
        try
        {
            _inkStory.ChoosePathString(path);
        }
        catch
        {
            Debug.Log("Invalid enemy name. Using default enemy dialogue.");
            _inkStory.ChoosePathString("defaultenemy.win");
        }

        text = _inkStory.Continue();
        return text;
    }

    public string GetLoseText(string enemyName)
    {
        string path = string.Concat(enemyName, ".lose");
        string text;
        try
        {
            _inkStory.ChoosePathString(path);
        }
        catch
        {
            Debug.Log("Invalid enemy name. Using default enemy dialogue.");
            _inkStory.ChoosePathString("defaultenemy.lose");
        }

        text = _inkStory.Continue();
        return text;
    }
}

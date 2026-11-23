using Ink.Runtime;
using UnityEngine;

public class Script : MonoBehaviour {

	// Set this file to your compiled json asset
	public TextAsset inkAsset;

	// The ink story that we're wrapping
	Story _inkStory;

    void Awake()
    {
     _inkStory = new Story(inkAsset.text);
    }

    void Start(){
        _inkStory.ChoosePathString("fishhead.intro");
    }

    void Update(){
       while (_inkStory.canContinue) {
        Debug.Log (_inkStory.Continue ());
        }
    }
}

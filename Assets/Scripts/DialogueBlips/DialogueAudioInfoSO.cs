using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DialogueAudioInfo", menuName = "ScriptableObjects/DialogueAudioInfoSO", order = 1)] //no I have no idea what this means
public class DialogueAudioInfoSO : ScriptableObject
{
    public string id;
    [SerializeField] public AudioClip[] dialogueBlips;
    [SerializeField] public bool stopAudioSourceOverlap;
    [Range(1, 5)]
    [SerializeField] public int blipFrequency = 1;
    [Range(-3, 3)]
    [SerializeField] public float minPitch = 0.5f;
    [Range(-3, 3)]
    [SerializeField] public float maxPitch = 1.5f;
    [SerializeField] public bool driftPitch = true;
    [Range(0.01f, 1f)]
    [SerializeField] public float pitchDriftAmount = 0.05f;
    [Range(0.1f, 1.5f)]
    [SerializeField] public float maxPitchDrift = 0.1f;
}

//heavily refrenced this video on how to do the audio blips
//How to Create Dialogue Audio like in Celeste and Animal Crossing using Unity
//https://youtu.be/P3FcXHEai_E?si=Mkoq8QHMQm23ODGU
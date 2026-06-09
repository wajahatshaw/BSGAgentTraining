using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Guide : MonoBehaviour
{
    [SerializeField] AudioSource audioSource;
    public Action AC_OnAudioFinished;

    public void PlayDialouge(AudioClip audioClip)
    {
        if(audioClip == null) return;  
        audioSource.Stop();
        audioSource.clip = audioClip;
        StartCoroutine(WaitForAudioToEnd(this.audioSource));
    }


    IEnumerator WaitForAudioToEnd(AudioSource audioSource)
    {
        audioSource.Play();

        // Wait while audio is playing
        while (audioSource.isPlaying)
        {
            yield return null;
        }
        

        AC_OnAudioFinished?.Invoke();
    }










    public Renderer eyeRenderer;           // The mesh with eye material
    public Color glowColor = Color.cyan;   // Glow color
    public float glowIntensity = 2f;       // Max glow intensity
    public float pulseSpeed = 2f;          // How fast it pulses

    private Material eyeMaterial;
    private float baseIntensity = 0.2f;
    [Header("Hover")]
    public float hoverAmplitude = 0.1f; // how much it moves up and down
    public float hoverFrequency = 1f;   // how fast it oscillates
    [SerializeField] Transform guideMesh;
//    [SerializeField] SmoothLookAt smoothLookAt;
    public Action AC_ReachedToDest;

    private Vector3 startPosition;
    private Action updateDel;
    private Transform myTransform;
    private Vector3 resetPos;
    


    void Start()
    {
        myTransform = transform;
        resetPos = myTransform.position;
        if(!eyeMaterial) return;
        // Get a unique instance of the material
        eyeMaterial = eyeRenderer.materials[2];
        eyeMaterial.EnableKeyword("_EMISSION");

        startPosition = guideMesh.localPosition;

        // updateDel += FloatinAir;
        // updateDel += GlowEye;

    }

    void Update()
    {

        updateDel?.Invoke();

    }

    void FloatinAir()
    {
        float newY = Mathf.Sin(Time.time * hoverFrequency) * hoverAmplitude;
        guideMesh.localPosition = startPosition + new Vector3(0f, newY, 0f);
    }

    void GlowEye()
    {
        if (audioSource.isPlaying)
        {
            float pulse = baseIntensity + Mathf.PingPong(Time.time * pulseSpeed, glowIntensity);
            eyeMaterial.SetColor("_EmissionColor", glowColor * pulse);
        }
        else
        {
            // Fade to low or no emission when not speaking
            Color current = eyeMaterial.GetColor("_EmissionColor");
            Color target = glowColor * baseIntensity;
            eyeMaterial.SetColor("_EmissionColor", Color.Lerp(current, target, Time.deltaTime * 5));
        }
    }

    // public void MoveTo(Vector3 movePos, float time)
    // {
    //     myTransform.LeanMove(movePos, time).setOnComplete(OnMoveDone);
    // }
    // public void MoveTo(Vector3 movePos)
    // {
    //     myTransform.LeanMove(movePos,  Vector3.Distance(myTransform.position, movePos)).setOnComplete(OnMoveDone);
    // }
    // void OnMoveDone() => AC_ReachedToDest?.Invoke();

    // public void MoveBackToOriginalpos()
    // {
    //     myTransform.LeanMove(resetPos, Vector3.Distance(myTransform.position, resetPos));
    // }

    // public void SetLookAt(Transform lookAt)
    // {
    //     smoothLookAt.SetTarget(lookAt);
    // }
    


   

    
}

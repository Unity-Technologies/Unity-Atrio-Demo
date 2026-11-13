using UnityEngine;

// Sits on the first person arms rig (next to its Animator) in Gameplay.prefab.
// PetCat plays on both the cat and the arms, so its events fire on both. The cat's CatAnimationAudio
// does the work; these empty receivers stop Unity logging "AnimationEvent has no receiver" for the arms.
public class PlayerArmsAnimationEvents : MonoBehaviour
{
    public void CatFussedAudioStart() { }
    public void CatFussedAudioStop() { }
}

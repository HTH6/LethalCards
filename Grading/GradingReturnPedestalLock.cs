using UnityEngine;

namespace LethalCards.Grading;

public class GradingReturnPedestalLock :
    MonoBehaviour
{
    private GrabbableObject? grabbable;

    private Vector3 lockedPosition;
    private Quaternion lockedRotation;

    private bool released;

    public void Initialize(
        Vector3 position,
        Quaternion rotation)
    {
        grabbable =
            GetComponent<GrabbableObject>();

        lockedPosition =
            position;

        lockedRotation =
            rotation;

        transform.position =
            lockedPosition;

        transform.rotation =
            lockedRotation;
    }

    private void LateUpdate()
    {
        if (released)
            return;

        if (grabbable == null)
            return;

        // Once the player actually grabs the card,
        // stop controlling its transform forever.
        if (
            grabbable.isHeld ||
            grabbable.heldByPlayerOnServer)
        {
            released = true;

            Destroy(this);

            return;
        }

        // Vanilla GrabbableObject falling logic runs during
        // the frame. Correct the returned card afterward so
        // that it remains displayed on the pedestal.
        transform.position =
            lockedPosition;

        transform.rotation =
            lockedRotation;

        grabbable.startFallingPosition =
            transform.localPosition;

        grabbable.targetFloorPosition =
            transform.localPosition;

        grabbable.fallTime =
            1f;

        grabbable.hasHitGround =
            true;

        grabbable.reachedFloorTarget =
            true;
    }
}
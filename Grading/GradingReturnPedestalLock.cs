using UnityEngine;

namespace LethalCards.Grading;

public class GradingReturnPedestalLock :
    MonoBehaviour
{
    private GrabbableObject? grabbable;

    private Vector3 lockedPosition;
    private Quaternion lockedRotation;

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
        if (grabbable == null)
            return;

        // Suspend during local pickup prediction. The replicated return state
        // removes this component after acceptance; rejected grabs can relock.
        if (
            grabbable.isHeld ||
            grabbable.heldByPlayerOnServer)
        {
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

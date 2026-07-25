using UnityEngine;

namespace NavianChallenge
{
    // Marks a ray target MouseGrab should never pick up as a handle, the same way it already skips buttons.
    // On the MPR board this sits on the backdrop behind the slice images: that surface exists only to keep
    // the pointer ray lit while hovering, and the images read as content to look at, not a place to grab
    // the whole board from. A click there should do nothing rather than yank the board out of position.
    public class MouseGrabExempt : MonoBehaviour
    {
    }
}

using UnityEngine;

/// <summary>
/// Scene-serialized catalog of the playable robot models (rigged walker
/// prefabs forged from Meshy rigs, plus the script-forged strider).
/// ArenaBuilder fills it at build time by scanning for *-robot assets, so
/// runtime UI can list and swap robots without any editor-only asset loading.
/// Entry 0 is the default robot the scene's bots are built with (ranger).
/// </summary>
public class RobotRoster : MonoBehaviour
{
    [System.Serializable]
    public struct Entry
    {
        public string displayName;
        public GameObject modelPrefab;

        /// <summary>
        /// Ground-vehicle form for this robot (Assets/Models/Meshy/*-vehicle.glb).
        /// Null for robots that have none — they fold and no more.
        /// </summary>
        public GameObject vehiclePrefab;

        /// <summary>
        /// Transformation stages in order, robot first and vehicle last
        /// (Assets/Models/Stages/&lt;robot&gt;/stage*.glb).
        ///
        /// Each stage is a separate model built from a frame of a generated
        /// transformation video, so there is nothing to interpolate between —
        /// playback swaps whole meshes. That is the point: the fold could only
        /// ever crouch a humanoid, because a humanoid mesh contains no tracks,
        /// no hull and no turret to unfold. These do.
        ///
        /// Empty for robots that have not been through the pipeline yet; they
        /// keep the old fold.
        /// </summary>
        public GameObject[] transformStages;

        /// <summary>
        /// The generated transformation clip this robot's stages were sampled
        /// from (Assets/Video/&lt;robot&gt;-transform.mp4). Shown in the robot
        /// inspector, where there is room and time for the real thing; the
        /// cards get the stop-motion instead.
        /// </summary>
        public UnityEngine.Video.VideoClip transformVideo;

        public bool HasStages => transformStages != null && transformStages.Length > 1;
    }

    public Entry[] robots = new Entry[0];

    public bool HasRobots => robots != null && robots.Length > 0;

    public Entry Get(int index)
    {
        return robots[Mathf.Clamp(index, 0, robots.Length - 1)];
    }
}

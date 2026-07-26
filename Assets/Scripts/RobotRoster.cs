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
    }

    public Entry[] robots = new Entry[0];

    public bool HasRobots => robots != null && robots.Length > 0;

    public Entry Get(int index)
    {
        return robots[Mathf.Clamp(index, 0, robots.Length - 1)];
    }
}

using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// Runtime arena remixing for the Arena Builder mode: deletes the existing
/// cover blocks, lays out a fresh random arrangement (respecting spawn zones
/// and prop positions), and rebakes the NavMesh so bots path correctly on the
/// new layout. The layout persists for matches started afterward.
/// </summary>
public class ArenaRandomizer : MonoBehaviour
{
    public Transform environmentRoot;
    public Material coverMaterial;
    public int minBlocks = 8;
    public int maxBlocks = 13;

    NavMeshSurface _surface;

    void Awake()
    {
        if (environmentRoot != null)
            _surface = environmentRoot.GetComponent<NavMeshSurface>();
    }

    public void Regenerate()
    {
        if (environmentRoot == null)
            return;

        // Remove current cover. DestroyImmediate so the NavMesh bake below
        // doesn't still see the old geometry this frame.
        var old = new List<GameObject>();
        foreach (Transform child in environmentRoot)
            if (child.name == "Cover")
                old.Add(child.gameObject);
        foreach (var go in old)
            DestroyImmediate(go);

        int count = Random.Range(minBlocks, maxBlocks + 1);

        // Also avoid every character's CURRENT position (bots may have wandered;
        // the player may be inactive but keeps a position) so no one gets a
        // cover block dropped on their head and stranded off the NavMesh.
        var occupied = new List<Vector3>();
        foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            occupied.Add(shield.transform.position);

        var placed = new List<Vector3>();
        int safety = 200;
        while (placed.Count < count && safety-- > 0)
        {
            float extent = ArenaContext.CoverHalfExtent;
            var pos = new Vector3(Random.Range(-extent, extent), 0f, Random.Range(-extent, extent));
            if (!IsClear(pos, placed) || !IsClear(pos, occupied))
                continue;
            placed.Add(pos);

            var size = new Vector3(Random.Range(1.5f, 4f), Random.Range(1.2f, 2.5f), Random.Range(1.2f, 2f));
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Cover";
            block.transform.SetParent(environmentRoot);
            block.transform.position = new Vector3(pos.x, size.y * 0.5f, pos.z);
            block.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
            block.transform.localScale = size;
            if (coverMaterial != null)
                block.GetComponent<MeshRenderer>().sharedMaterial = coverMaterial;
        }

        if (_surface != null)
            _surface.BuildNavMesh();
    }

    bool IsClear(Vector3 pos, List<Vector3> placed)
    {
        // Spawns and fixed props of whichever arena is currently loaded.
        foreach (var keep in ArenaContext.KeepOut)
            if (Vector3.Distance(pos, keep) < 4f)
                return false;
        foreach (var other in placed)
            if (Vector3.Distance(pos, other) < 3.5f)
                return false;
        return true;
    }
}

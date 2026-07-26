using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// X-Ray Scope attachment: while active, reveals enemy silhouettes through
/// walls (a ZTest-Greater duplicate of each enemy's meshes) at the cost of
/// slowed movement. A player tool — bots already "see" via raycasts, so this is
/// purely the rendering/reveal side.
/// </summary>
public class XRayScope : MonoBehaviour
{
    [Tooltip("Silhouette colour for revealed enemies (HDR feeds bloom).")]
    public Color enemyColor = new Color(1f, 0.3f, 0.9f) * 1.5f;
    [Range(0.2f, 1f)] public float scopedSpeedMultiplier = 0.45f;

    CharacterMotor _motor;
    EnergyShield _myShield;
    Material _xrayMaterial;
    readonly List<GameObject> _silhouettes = new List<GameObject>();
    bool _scoped;
    bool _built;

    void Awake()
    {
        _motor = GetComponent<CharacterMotor>();
        _myShield = GetComponent<EnergyShield>();
    }

    public void SetScoped(bool scoped)
    {
        if (scoped == _scoped)
            return;
        _scoped = scoped;

        if (scoped && !_built)
            BuildSilhouettes();

        foreach (var s in _silhouettes)
            if (s != null)
                s.SetActive(scoped);

        if (_motor != null)
            _motor.speedMultiplier = scoped ? scopedSpeedMultiplier : 1f;
    }

    /// <summary>
    /// Drops the cached silhouettes so they rebuild on next scope. Called
    /// after a robot reskin — the old silhouettes died with the old model's
    /// meshes, and the replacement meshes need fresh duplicates.
    /// </summary>
    public void InvalidateSilhouettes()
    {
        if (_scoped)
            SetScoped(false);
        foreach (var s in _silhouettes)
            if (s != null)
                Destroy(s);
        _silhouettes.Clear();
        _built = false;
    }

    void BuildSilhouettes()
    {
        _built = true;
        var shader = Shader.Find("PhotonArena/XRay");
        if (shader == null)
            return;
        _xrayMaterial = new Material(shader);
        _xrayMaterial.SetColor("_Color", enemyColor);

        int myTeam = _myShield != null ? _myShield.teamId : -1;
        foreach (var shield in FindObjectsByType<EnergyShield>(FindObjectsSortMode.None))
        {
            if (shield.teamId == myTeam)
                continue;

            foreach (var mf in shield.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null)
                    continue;

                var sil = new GameObject("XRaySilhouette");
                sil.transform.SetParent(mf.transform, false);
                sil.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = sil.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _xrayMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                sil.SetActive(false);
                _silhouettes.Add(sil);
            }

            // Rigged walkers render through a SkinnedMeshRenderer, which has no
            // MeshFilter — duplicate the renderer instead, sharing the original's
            // bones so the silhouette walks along with it.
            foreach (var skin in shield.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.sharedMesh == null)
                    continue;

                var sil = new GameObject("XRaySilhouette");
                sil.transform.SetParent(skin.transform, false);
                var copy = sil.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = skin.sharedMesh;
                copy.bones = skin.bones;
                copy.rootBone = skin.rootBone;
                copy.localBounds = skin.localBounds;

                var materials = new Material[skin.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = _xrayMaterial;
                copy.sharedMaterials = materials;

                copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                copy.receiveShadows = false;
                sil.SetActive(false);
                _silhouettes.Add(sil);
            }
        }
    }
}

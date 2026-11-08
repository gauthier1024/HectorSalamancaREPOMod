using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HectorSalamanca
{
    [BepInPlugin("local.repo.hectorsalamanca", "HectorSalamanca", "1.0.0")]
    [BepInDependency(BasePluginGuid)]
    public sealed class HectorSalamancaPlugin : BaseUnityPlugin
    {
        private const string BasePluginGuid = "UmamusumeSemiBots";
        private const string CharName = "HectorSalamanca";
        private const string BundleFile = "hectorassets";
        private const string ModelAsset = "Hector";
        private const string PrefabKey = "hectorsalamanca/hectorsalamanca.prefab";

        private const int Speed = -10, Stamina = -10, Power = 50, Guts = -10, Wit = -10;

        private sealed class Placement
        {
            public float HeightMultiplier, RotationY, OffsetX, OffsetY, OffsetZ;
        }

        private Placement bodyPlacement;
        private Placement chibiPlacement;
        private AssetBundle bundle;

        private void Awake()
        {
            bodyPlacement = BindPlacement("Body");
            chibiPlacement = BindPlacement("Chibi");

            SceneManager.sceneLoaded += OnFirstScene;
            Logger.LogInfo("Registration of " + CharName + " queued for the first loaded scene.");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnFirstScene;
        }

        private Placement BindPlacement(string section)
        {
            return new Placement
            {
                HeightMultiplier = Config.Bind(section, "HeightMultiplier", 1f,
                    "Model height as a multiple of the reference character's height.").Value,
                RotationY = Config.Bind(section, "RotationY", 0f,
                    "Rotation around the vertical axis in degrees (use 180 if the model faces backwards).").Value,
                OffsetX = Config.Bind(section, "OffsetX", 0f, "Sideways offset in meters.").Value,
                OffsetY = Config.Bind(section, "OffsetY", 0f, "Vertical offset in meters.").Value,
                OffsetZ = Config.Bind(section, "OffsetZ", 0f, "Forward/backward offset in meters.").Value,
            };
        }

        private void OnFirstScene(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnFirstScene;
            try
            {
                Register();
            }
            catch (Exception ex)
            {
                Logger.LogError(CharName + " was not registered: " + ex);
            }
        }

        private void Register()
        {
            if (!Chainloader.PluginInfos.TryGetValue(BasePluginGuid, out var baseInfo))
                throw new InvalidOperationException("UmamusumeSemibots is required.");

            Type baseType = baseInfo.Instance.GetType();

            var names = Field<List<string>>(baseType, "ModelNames");
            var paths = Field<List<string>>(baseType, "ModelAssetPaths");
            var thumbs = Field<List<Texture2D>>(baseType, "Thumbnails");
            var chibis = Field<List<Texture2D>>(baseType, "Chibis");
            var chibisTalk = Field<List<Texture2D>>(baseType, "ChibisTalking");
            var groups = Field<List<List<int>>>(baseType, "ModelOutfitGroups");
            var slotOutfit = Field<Dictionary<int, int>>(baseType, "SlotOutfitSelection");
            var colorModel = Field<Dictionary<int, int>>(baseType, "ColorToModel");
            var prefabs = Field<Dictionary<string, GameObject>>(baseType, "_loadedPrefabs");

            if (names.Contains(CharName))
                throw new InvalidOperationException(CharName + " is already registered.");

            int model = names.Count;
            int slot = groups.Count;

            if (model == 0 || paths.Count != model || thumbs.Count != model || chibis.Count != model || chibisTalk.Count != model)
                throw new InvalidOperationException("Base mod lists are misaligned; registration refused.");
            if (slotOutfit.ContainsKey(slot) || colorModel.ContainsKey(slot) || prefabs.ContainsKey(PrefabKey))
                throw new InvalidOperationException("Slot or cache key collision; registration refused.");

            Type growthDataType = baseType.Assembly.GetType("UmamusumeSemiBots.UmaGrowthRateData", true);
            Type growthRatesType = baseType.Assembly.GetType("UmamusumeSemiBots.UmaGrowthRates", true);
            Type crouchType = baseType.Assembly.GetType("UmamusumeSemiBots.UmaModelCrouchData", true);

            var growthTable = Field<IDictionary>(growthDataType, "UmaGrowthRateTable");
            var crouch = Field<Dictionary<string, float>>(crouchType, "CrouchOffsets");
            if (growthTable.Contains(CharName))
                throw new InvalidOperationException("Growth rates for " + CharName + " already exist.");

            MethodInfo getPrefab = baseType.GetMethod("GetPrefabFlat", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getPrefab == null)
                throw new InvalidOperationException("Reference prefab loader not found.");
            var reference = getPrefab.Invoke(null, new object[] { 0 }) as GameObject;
            if (reference == null)
                throw new InvalidOperationException("Reference prefab not found.");

            string dir = Path.GetDirectoryName(Info.Location);

            string bundlePath = Path.Combine(dir, BundleFile);
            if (!File.Exists(bundlePath))
                throw new FileNotFoundException("Bundle not found: " + bundlePath);
            bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
                throw new InvalidOperationException("Could not read the bundle (wrong Unity version?).");
            var modelPrefab = bundle.LoadAsset<GameObject>(ModelAsset);
            if (modelPrefab == null)
                throw new InvalidOperationException("Prefab '" + ModelAsset + "' not found in the bundle.");

            Texture2D thumb = LoadPng(dir, "thumbnail.png", thumbs[0]);
            Texture2D chibi = LoadPng(dir, "chibi.png", chibis[0]);
            Texture2D chibiTalk = LoadPng(dir, "chibitalk.png", chibisTalk[0]);

            var storage = new GameObject(CharName + "_Templates");
            DontDestroyOnLoad(storage);
            storage.SetActive(false);

            GameObject clone;
            try
            {
                clone = BuildClone(reference, modelPrefab, storage.transform);
            }
            catch
            {
                Destroy(storage);
                throw;
            }

            string referenceKey = Path.GetFileNameWithoutExtension(names[0]);
            if (!crouch.TryGetValue(referenceKey, out float crouchOffset))
                throw new InvalidOperationException("Reference model has no crouch camera offset.");
            crouch[CharName] = crouchOffset;

            growthTable.Add(CharName, Activator.CreateInstance(growthRatesType, Speed, Stamina, Power, Guts, Wit));

            prefabs.Add(PrefabKey, clone);
            names.Add(CharName);
            paths.Add(PrefabKey);
            thumbs.Add(thumb);
            chibis.Add(chibi);
            chibisTalk.Add(chibiTalk);
            groups.Add(new List<int> { model });
            slotOutfit.Add(slot, 0);
            colorModel.Add(slot, model);

            Logger.LogInfo(CharName + " registered: slot=" + slot + ", model=" + model
                + ". All players need the base mod and this add-on.");

            MethodInfo loadSavedModel = baseType.GetMethod("LoadSavedModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (loadSavedModel != null)
            {
                try { loadSavedModel.Invoke(baseInfo.Instance, null); }
                catch (Exception ex) { Logger.LogWarning("Could not restore the saved selection: " + ex.Message); }
            }
        }

        private GameObject BuildClone(GameObject reference, GameObject modelPrefab, Transform parent)
        {
            GameObject clone = Instantiate(reference, parent, false);
            clone.name = CharName;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;

            Animator rootAnimator = clone.GetComponent<Animator>();
            Transform bodyT = clone.transform.Find("Body");
            Transform smolT = clone.transform.Find("Smol");
            if (rootAnimator == null || bodyT == null || smolT == null)
                throw new InvalidOperationException("Reference prefab does not have the expected structure (Animator, Body, Smol).");

            var bodySmr = bodyT.GetComponent<SkinnedMeshRenderer>();
            var smolSmr = smolT.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (bodySmr == null || bodySmr.sharedMesh == null || smolSmr == null || smolSmr.sharedMesh == null)
                throw new InvalidOperationException("Reference meshes not found (Body or Smol).");

            Transform bodyHips = clone.transform.Find("Armature/Hips") ?? FindHips(clone.transform, rootAnimator, bodySmr);
            Transform smolHips = FindHips(smolT, smolT.GetComponent<Animator>(), smolSmr);
            if (bodyHips == null || smolHips == null)
                throw new InvalidOperationException("Hips bone not found (body or chibi).");

            HideOriginalRenderers(clone);

            AttachModel(modelPrefab, clone.transform, bodyHips, bodySmr, bodyT, bodyPlacement, CharName + "_Static");
            AttachModel(modelPrefab, clone.transform, smolHips, smolSmr, smolT, chibiPlacement, CharName + "_StaticSmol");

            return clone;
        }

        private void HideOriginalRenderers(GameObject clone)
        {
            Material invisible = CreateInvisibleMaterial();
            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                if (invisible != null)
                {
                    var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                    for (int i = 0; i < materials.Length; i++) materials[i] = invisible;
                    renderer.sharedMaterials = materials;
                }
                else
                {
                    renderer.enabled = false;
                }
            }

            if (invisible == null)
                Logger.LogWarning("No transparent shader found; the original model is hidden with 'enabled' instead and may reappear.");
        }

        private Material CreateInvisibleMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) return null;

            var material = new Material(shader);
            material.name = CharName + "_Invisible";
            if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(0f, 0f, 0f, 0f));
            material.renderQueue = 3000;
            return material;
        }

        private static void AttachModel(GameObject modelPrefab, Transform root, Transform hips,
            SkinnedMeshRenderer referenceRenderer, Transform source, Placement placement, string holderName)
        {
            GameObject holder = AttachStatic(modelPrefab, root, hips, referenceRenderer, placement, holderName);
            var mirror = holder.AddComponent<MirrorVisibility>();
            mirror.Source = source.gameObject;
            mirror.SourceRenderer = referenceRenderer;
            mirror.Visual = holder.transform.GetChild(0).gameObject;
        }

        private static GameObject AttachStatic(GameObject modelPrefab, Transform space, Transform hips,
            SkinnedMeshRenderer referenceRenderer, Placement placement, string holderName)
        {
            Bounds referenceBounds = BoundsIn(space, referenceRenderer.transform, referenceRenderer.sharedMesh.bounds);

            var holder = new GameObject(holderName);
            holder.transform.SetParent(space, false);
            holder.transform.localRotation = Quaternion.Euler(0f, placement.RotationY, 0f);

            GameObject visual = Instantiate(modelPrefab, holder.transform, false);
            visual.SetActive(true);

            bool found = false;
            Bounds modelBounds = new Bounds();
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds bounds = BoundsIn(space, filter.transform, filter.sharedMesh.bounds);
                if (!found) { modelBounds = bounds; found = true; } else modelBounds.Encapsulate(bounds);
            }
            foreach (SkinnedMeshRenderer renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                Bounds bounds = BoundsIn(space, renderer.transform, renderer.sharedMesh.bounds);
                if (!found) { modelBounds = bounds; found = true; } else modelBounds.Encapsulate(bounds);
            }
            if (!found || modelBounds.size.y <= 1e-6f)
            {
                Destroy(holder);
                throw new InvalidOperationException("No usable mesh in the model.");
            }

            float scale = referenceBounds.size.y * placement.HeightMultiplier / modelBounds.size.y;
            Vector3 modelBottom = new Vector3(modelBounds.center.x, modelBounds.min.y, modelBounds.center.z);
            Vector3 referenceBottom = new Vector3(referenceBounds.center.x, referenceBounds.min.y, referenceBounds.center.z);
            holder.transform.localScale = Vector3.one * scale;
            holder.transform.localPosition = referenceBottom - modelBottom * scale
                + new Vector3(placement.OffsetX, placement.OffsetY, placement.OffsetZ);

            holder.transform.SetParent(hips, true);
            return holder;
        }

        private static Bounds BoundsIn(Transform space, Transform from, Bounds local)
        {
            Bounds result = new Bounds(space.InverseTransformPoint(from.TransformPoint(local.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));
                result.Encapsulate(space.InverseTransformPoint(from.TransformPoint(corner)));
            }
            return result;
        }

        private static Transform FindHips(Transform root, Animator animator, SkinnedMeshRenderer renderer)
        {
            Transform hips = null;
            try
            {
                if (animator != null && animator.isHuman) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            }
            catch (Exception) { }

            if (hips == null)
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.EndsWith("Hips", StringComparison.OrdinalIgnoreCase)) { hips = child; break; }
                }
            }
            if (hips == null && renderer != null) hips = renderer.rootBone;
            return hips;
        }

        private Texture2D LoadPng(string dir, string file, Texture2D fallback)
        {
            string path = Path.Combine(dir, file);
            if (!File.Exists(path))
            {
                Logger.LogWarning(file + " not found, using the reference character's image.");
                return fallback;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
                throw new InvalidDataException("Could not decode " + file);
            texture.name = Path.GetFileNameWithoutExtension(file);
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static T Field<T>(Type type, string name) where T : class
        {
            FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            T value = field == null ? null : field.GetValue(null) as T;
            if (value == null)
                throw new InvalidOperationException("Base mod field missing or incompatible: " + name);
            return value;
        }
    }

    public sealed class MirrorVisibility : MonoBehaviour
    {
        public GameObject Source;
        public SkinnedMeshRenderer SourceRenderer;
        public GameObject Visual;
        private int lastLayer = -1;

        private void LateUpdate()
        {
            if (Source == null || Visual == null) return;

            bool visible = Source.activeInHierarchy && (SourceRenderer == null || SourceRenderer.enabled);
            if (Visual.activeSelf != visible) Visual.SetActive(visible);

            int layer = Source.layer;
            if (layer != lastLayer)
            {
                lastLayer = layer;
                SetLayer(Visual.transform, layer);
            }
        }

        private static void SetLayer(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            foreach (Transform child in target) SetLayer(child, layer);
        }
    }
}

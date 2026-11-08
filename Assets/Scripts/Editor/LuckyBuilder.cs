using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;
using UiFontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace Lucky.EditorTools
{
    /// <summary>Rebuilds import settings, the asset registry, fonts, materials, post-processing and the main scene.</summary>
    public static class LuckyBuilder
    {
        const string ConfigPath = "Assets/Data/GameConfig.asset";
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Lucky/Import TMP Essentials")]
        public static string ImportTmpEssentials()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0) return "TMP essentials already present";
            var pkg = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(pkg, false);
            return "Imported TMP essentials";
        }

        [MenuItem("Lucky/Build All")]
        public static string BuildAll()
        {
            var log = new List<string>();
            ConfigureTextures(log);
            ConfigureAudio(log);
            var cfg = BuildConfig(log);
            var panel = BuildPanelSettings();
            var profile = BuildVolumeProfile();
            BuildScene(cfg, panel, profile);
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            log.Add("Scene saved: " + ScenePath);
            return string.Join("\n", log);
        }

        // ---------- Imports ----------

        static void ConfigureTextures(List<string> log)
        {
            int count = 0, normals = 0;
            // Normal maps first so sprites can reference them as secondary textures.
            foreach (var path in Find("t:Texture2D", "Assets/Art/Normals").Concat(Find("t:Texture2D", "Assets/Resources/Anim").Where(p => p.EndsWith("_n.png"))))
            {
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                ti.textureType = TextureImporterType.NormalMap;
                ti.mipmapEnabled = false;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = 2048;
                Compress(ti);
                ti.SaveAndReimport();
                normals++;
            }
            foreach (var path in Find("t:Texture2D", "Assets/Art"))
            {
                if (path.Contains("/Normals/")) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Art/Normals/{Path.GetFileNameWithoutExtension(path)}_n.png");
                ti.secondarySpriteTextures = normal != null
                    ? new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normal } }
                    : new SecondarySpriteTexture[0];
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = 100;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                // Sources are pre-sized and 4-aligned by web_optimize_textures.py, so no import-time downscale.
                ti.maxTextureSize = 2048;
                Compress(ti);
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                settings.spriteAlignment = path.Contains("/Characters/") ? (int)SpriteAlignment.BottomCenter
                    : path.EndsWith("wheel_pointer.png") ? (int)SpriteAlignment.TopCenter
                    : (int)SpriteAlignment.Center;
                settings.spriteMeshType = path.Contains("/Characters/") ? SpriteMeshType.Tight : SpriteMeshType.FullRect;
                ti.SetTextureSettings(settings);
                ti.SaveAndReimport();
                count++;
            }
            foreach (var path in Find("t:Texture2D", "Assets/Resources/Anim"))
            {
                if (path.EndsWith("_n.png")) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = 4096;
                Compress(ti);
                ti.SaveAndReimport();
                count++;
            }
            log.Add($"Textures configured: {count} (+{normals} normal maps)");
        }

        /// <summary>Crunched DXT: small downloads for the web build (requires 4-aligned sources).</summary>
        static void Compress(TextureImporter ti)
        {
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.crunchedCompression = true;
            ti.compressionQuality = 60;
        }

        static void ConfigureAudio(List<string> log)
        {
            int count = 0;
            foreach (var path in Find("t:AudioClip", "Assets/Audio"))
            {
                if (AssetImporter.GetAtPath(path) is not AudioImporter ai) continue;
                bool music = path.Contains("/Music/");
                var s = ai.defaultSampleSettings;
                s.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = music ? 0.6f : 0.75f;
                ai.defaultSampleSettings = s;
                ai.loadInBackground = music;
                ai.SaveAndReimport();
                count++;
            }
            log.Add($"Audio configured: {count}");
        }

        // ---------- Registry, materials, fonts ----------

        static GameConfig BuildConfig(List<string> log)
        {
            var cfg = LoadOrCreate<GameConfig>(ConfigPath);
            cfg.sprites = Find("t:Sprite", "Assets/Art").Select(AssetDatabase.LoadAssetAtPath<Sprite>).Where(s => s != null).ToList();
            cfg.clips = Find("t:AudioClip", "Assets/Audio").Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c != null).ToList();
            cfg.spriteFX = Material("SpriteFX", "Lucky/SpriteFXLit");
            cfg.additive = Material("Additive", "Lucky/Additive");
            cfg.additive.SetFloat("_Intensity", 2.2f);
            cfg.vertexColor = Material("VertexColor", "Lucky/VertexColor");
            cfg.spriteDefault = Material("SpriteDefault", "Universal Render Pipeline/2D/Sprite-Unlit-Default");
            cfg.spriteLit = Material("SpriteLit", "Universal Render Pipeline/2D/Sprite-Lit-Default");
            cfg.videoLit = Material("VideoLit", "Lucky/VideoLit");
            cfg.lightPrefab = LightPrefab();
            cfg.numberFont = TmpFont("Assets/Fonts/LilitaOne-Regular.ttf", "Assets/Fonts/LilitaOne SDF.asset");
            cfg.titleFont = TmpFont("Assets/Fonts/CinzelDecorative-Black.ttf", "Assets/Fonts/CinzelDecorative SDF.asset");
            cfg.uiNumberFont = UiFont("Assets/Fonts/LilitaOne-Regular.ttf", "Assets/Fonts/LilitaOne UI.asset");
            cfg.uiTitleFont = UiFont("Assets/Fonts/CinzelDecorative-Black.ttf", "Assets/Fonts/CinzelDecorative UI.asset");
            EditorUtility.SetDirty(cfg);
            log.Add($"Config: {cfg.sprites.Count} sprites, {cfg.clips.Count} clips");
            return cfg;
        }

        static Material Material(string name, string shader)
        {
            string path = $"Assets/Data/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                mat.shader = Shader.Find(shader);
                EditorUtility.SetDirty(mat);
                return mat;
            }
            Directory.CreateDirectory("Assets/Data/Materials");
            mat = new Material(Shader.Find(shader)) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Point light template; normal-map settings and target layers are only serializable, not scriptable.</summary>
        static GameObject LightPrefab()
        {
            const string path = "Assets/Data/PointLight2D.prefab";
            var go = new GameObject("PointLight2D");
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.pointLightInnerRadius = 0f;
            light.pointLightOuterRadius = 4f;
            light.falloffIntensity = 0.6f;
            ConfigureLight(light, Light2D.NormalMapQuality.Fast, 1.5f);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void ConfigureLight(Light2D light, Light2D.NormalMapQuality quality, float distance)
        {
            var so = new SerializedObject(light);
            so.FindProperty("m_NormalMapQuality").intValue = (int)quality;
            so.FindProperty("m_NormalMapDistance").floatValue = distance;
            var layers = so.FindProperty("m_ApplyToSortingLayers");
            layers.arraySize = SortingLayer.layers.Length;
            for (int i = 0; i < SortingLayer.layers.Length; i++) layers.GetArrayElementAtIndex(i).intValue = SortingLayer.layers[i].id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static TMP_FontAsset TmpFont(string ttf, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            var fa = TMP_FontAsset.CreateFontAsset(font, 64, 7, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            fa.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(fa, path);
            fa.atlasTextures[0].name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            return fa;
        }

        static UiFontAsset UiFont(string ttf, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<UiFontAsset>(path);
            if (existing != null) return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            var fa = UiFontAsset.CreateFontAsset(font, 64, 7, GlyphRenderMode.SDFAA, 1024, 1024, UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, false);
            fa.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(fa, path);
            fa.atlasTextures[0].name = fa.name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            return fa;
        }

        // ---------- UI & post-processing ----------

        static PanelSettings BuildPanelSettings()
        {
            var ps = LoadOrCreate<PanelSettings>("Assets/UI/PanelSettings.asset");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI/LuckyTheme.tss");
            EditorUtility.SetDirty(ps);
            return ps;
        }

        static VolumeProfile BuildVolumeProfile()
        {
            var profile = LoadOrCreate<VolumeProfile>("Assets/Settings/LuckyVolume.asset");
            var bloom = Component<Bloom>(profile);
            bloom.threshold.Override(0.92f);
            bloom.intensity.Override(0.75f);
            bloom.scatter.Override(0.65f);
            var vignette = Component<Vignette>(profile);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            var color = Component<ColorAdjustments>(profile);
            color.contrast.Override(12f);
            color.saturation.Override(10f);
            color.postExposure.Override(0f);
            Component<ChromaticAberration>(profile).intensity.Override(0.05f);
            Component<LensDistortion>(profile).intensity.Override(0f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) return existing;
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        // ---------- Scene & player ----------

        static void BuildScene(GameConfig cfg, PanelSettings panel, VolumeProfile profile)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.01f, 0.02f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 50f;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camGo.AddComponent<AudioListener>();

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // Dim, warm ambient so torches, spells and impacts do the lighting.
            var global = new GameObject("Global Light 2D").AddComponent<Light2D>();
            global.lightType = Light2D.LightType.Global;
            global.intensity = 0.5f;
            global.color = new Color(1f, 0.9f, 0.82f);
            ConfigureLight(global, Light2D.NormalMapQuality.Disabled, 1f);

            var game = new GameObject("Game");
            var doc = game.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Main.uxml");
            game.AddComponent<HUD>();
            var director = game.AddComponent<GameDirector>();
            director.config = cfg;
            director.volume = volume;
            director.ambient = global;
            EditorUtility.SetDirty(doc);
            EditorUtility.SetDirty(director);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Lucky Number";
            PlayerSettings.companyName = "Team Lucky Number";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:LuckyNumber";
        }

        // ---------- Helpers ----------

        static IEnumerable<string> Find(string filter, string folder) =>
            AssetDatabase.IsValidFolder(folder)
                ? AssetDatabase.FindAssets(filter, new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).Distinct()
                : Enumerable.Empty<string>();

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}

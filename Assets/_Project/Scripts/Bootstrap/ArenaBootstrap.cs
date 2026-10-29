using UnityEngine;
using UnityEngine.SceneManagement;
using BR.Combat;
using BR.Inventory;
using BR.Managers;
using BR.Player;

namespace BR.Bootstrap
{
    /// <summary>
    /// Entrada da cena Arena. Constrói o mundo gray-box e os managers da partida:
    ///   chão, prédios, luz, câmera, player, avião, zona, loot, 49 bots e MatchManager.
    ///
    /// Os objetos são criados depois de SetActiveScene(arena), então tudo pertence à cena
    /// Arena e é destruído no unload (o pool guarda apenas o que foi devolvido).
    /// </summary>
    public sealed class ArenaBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            // Garante que tudo criado aqui (e pelo pool durante a partida) fique na Arena.
            SceneManager.SetActiveScene(gameObject.scene);

            if (GameManager.Instance == null)
                Debug.LogWarning("[ArenaBootstrap] Arena iniciada sem GameManager. Abra Boot.unity e dê Play.");

            RuntimeContent.Initialize();

            var config = RuntimeContent.Config;

            BuildLighting();
            BuildGround();
            BuildProps();

            var cameraRig = BuildCamera();
            var player = BuildPlayer(cameraRig);

            var plane = BuildPlane();
            var zone = BuildZone();
            var loot = BuildLoot(config.mapRadius);
            var bots = BuildBots(config.totalPlayers - 1);

            var matchObject = new GameObject("[MatchManager]");
            var match = matchObject.AddComponent<MatchManager>();
            match.Bind(player, bots, zone, loot, plane, plane.Anchor);

            Debug.Log("[ArenaBootstrap] Arena gray-box pronta: player, " + (config.totalPlayers - 1) + " bots, loot e zona.");
        }

        // ------------------------------------------------------------ Mundo

        private static void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.45f, 0.5f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.55f, 0.63f, 0.72f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 460f;
        }

        private static void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(90f, 1f, 90f);

            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null && RuntimeContent.GroundMaterial != null)
                renderer.sharedMaterial = RuntimeContent.GroundMaterial;
        }

        private static void BuildProps()
        {
            var parent = new GameObject("Props").transform;

            for (var i = 0; i < 34; i++)
            {
                var position = new Vector3(Random.Range(-290f, 290f), 0f, Random.Range(-290f, 290f));

                // Mantém a área de spawn/ilha limpa.
                if (new Vector2(position.x, position.z).sqrMagnitude < 45f * 45f) continue;

                var width = Random.Range(8f, 26f);
                var depth = Random.Range(8f, 26f);
                var height = Random.Range(4f, 16f);

                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Building_" + i;
                block.transform.SetParent(parent, false);
                block.transform.position = position + Vector3.up * (height * 0.5f);
                block.transform.localScale = new Vector3(width, height, depth);
                block.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);

                var renderer = block.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var material = i % 3 == 0 ? RuntimeContent.BuildingMaterialB : RuntimeContent.BuildingMaterialA;
                    if (material != null) renderer.sharedMaterial = material;
                }
            }

            // 4 torres altas para orientação no mapa.
            var towers = new[]
            {
                new Vector3(-210f, 0f, 210f),
                new Vector3(210f, 0f, 210f),
                new Vector3(-210f, 0f, -210f),
                new Vector3(210f, 0f, -210f)
            };

            for (var i = 0; i < towers.Length; i++)
            {
                var tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tower.name = "Tower_" + i;
                tower.transform.SetParent(parent, false);
                tower.transform.position = towers[i] + Vector3.up * 18f;
                tower.transform.localScale = new Vector3(16f, 36f, 16f);

                var renderer = tower.GetComponent<Renderer>();
                if (renderer != null && RuntimeContent.BuildingMaterialB != null)
                    renderer.sharedMaterial = RuntimeContent.BuildingMaterialB;
            }
        }

        // ------------------------------------------------------------ Câmera / Player

        private static ThirdPersonCamera BuildCamera()
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";

            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 900f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.45f, 0.60f, 0.78f);

            go.AddComponent<AudioListener>();
            return go.AddComponent<ThirdPersonCamera>();
        }

        private static PlayerController BuildPlayer(ThirdPersonCamera cameraRig)
        {
            var root = new GameObject("Player");
            root.transform.position = new Vector3(0f, 1.2f, 0f);

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.4f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            var bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null) DestroyImmediate(bodyCollider);
            var bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null && RuntimeContent.PlayerMaterial != null)
                bodyRenderer.sharedMaterial = RuntimeContent.PlayerMaterial;

            root.AddComponent<Health>();

            var weaponGo = new GameObject("Weapon");
            weaponGo.transform.SetParent(root.transform, false);
            weaponGo.transform.localPosition = new Vector3(0.22f, 1.3f, 0.4f);

            var weapon = weaponGo.AddComponent<WeaponController>();
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(weaponGo.transform, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.7f);

            weapon.Configure(RuntimeContent.Pistol, muzzle, RuntimeContent.TracerPrefab, ~0, "Você");

            root.AddComponent<InventoryComponent>();

            var combat = root.AddComponent<PlayerCombat>();
            combat.Configure(weapon, Camera.main);

            return root.AddComponent<PlayerController>();
        }

        // ------------------------------------------------------------ Managers

        private static PlaneDirector BuildPlane()
        {
            var go = new GameObject("[Plane]");
            var plane = go.AddComponent<PlaneDirector>();
            plane.SetAnchor(go.transform);
            return plane;
        }

        private static ZoneManager BuildZone()
        {
            var go = new GameObject("[Zone]");
            var zone = go.AddComponent<ZoneManager>();

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(go.transform, false);

            var line = ringGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 0;
            line.startWidth = 2.5f;
            line.endWidth = 2.5f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = new Color(0.2f, 0.85f, 1f, 0.9f);
            line.endColor = new Color(0.2f, 0.85f, 1f, 0.9f);

            if (RuntimeContent.RingMaterial != null) line.sharedMaterial = RuntimeContent.RingMaterial;

            zone.SetRing(line);
            return zone;
        }

        private static LootManager BuildLoot(float mapRadius)
        {
            var go = new GameObject("[LootManager]");
            var loot = go.AddComponent<LootManager>();
            loot.Configure(RuntimeContent.LootPrefab, RuntimeContent.LootTable, mapRadius);
            return loot;
        }

        private static BotManager BuildBots(int count)
        {
            var go = new GameObject("[BotManager]");
            var bots = go.AddComponent<BotManager>();
            bots.Configure(RuntimeContent.BotPrefab, count, go.transform);
            return bots;
        }
    }
}

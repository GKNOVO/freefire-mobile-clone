using UnityEngine;
using BR.Combat;
using BR.Inventory;
using BR.Managers;

namespace BR.Bootstrap
{
    /// <summary>
    /// Conteúdo criado 100% por código: armas, itens, config de partida, materiais e
    /// prefabs (bot, loot, tracer). Permite que as cenas geradas pelo Editor sejam
    /// apenas dois GameObjects (Boot/Arena) e ainda assim o jogo funcione no APK.
    ///
    /// Nada aqui depende do Inspector: os ScriptableObjects e prefabs são criados
    /// uma única vez e mantidos em DontDestroyOnLoad para sobreviver ao unload da Arena.
    /// </summary>
    public static class RuntimeContent
    {
        public static MatchConfig Config { get; private set; }
        public static GameObject BotPrefab { get; private set; }
        public static GameObject LootPrefab { get; private set; }
        public static GameObject TracerPrefab { get; private set; }
        public static ItemDefinition[] LootTable { get; private set; }

        public static WeaponDefinition Pistol { get; private set; }
        public static WeaponDefinition Smg { get; private set; }
        public static WeaponDefinition Rifle { get; private set; }
        public static WeaponDefinition Shotgun { get; private set; }
        public static WeaponDefinition Sniper { get; private set; }
        public static WeaponDefinition BotWeapon { get; private set; }

        public static Material GroundMaterial { get; private set; }
        public static Material BuildingMaterialA { get; private set; }
        public static Material BuildingMaterialB { get; private set; }
        public static Material PlayerMaterial { get; private set; }
        public static Material BotMaterial { get; private set; }
        public static Material LootMaterial { get; private set; }
        public static Material RingMaterial { get; private set; }
        public static Material TracerMaterial { get; private set; }

        private static Transform _prefabRoot;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            var root = new GameObject("[RuntimePrefabs]");
            Object.DontDestroyOnLoad(root);
            _prefabRoot = root.transform;

            CreateMaterials();
            CreateWeapons();
            CreateItems();
            CreateConfig();
            CreateTracerPrefab();
            CreateLootPrefab();
            CreateBotPrefab();

            Debug.Log("[RuntimeContent] Conteúdo de runtime pronto.");
        }

        // ------------------------------------------------------------ Materiais

        private static Shader FindLitShader()
        {
            var shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            return shader;
        }

        private static Material CreateMaterial(string materialName, Color color)
        {
            var shader = FindLitShader();
            if (shader == null)
            {
                Debug.LogError("[RuntimeContent] Nenhum shader encontrado para criar materiais.");
                return null;
            }

            return new Material(shader) { name = materialName, color = color };
        }

        private static void CreateMaterials()
        {
            GroundMaterial = CreateMaterial("MAT_Ground", new Color(0.34f, 0.37f, 0.32f));
            BuildingMaterialA = CreateMaterial("MAT_BuildingA", new Color(0.58f, 0.60f, 0.63f));
            BuildingMaterialB = CreateMaterial("MAT_BuildingB", new Color(0.44f, 0.47f, 0.52f));
            PlayerMaterial = CreateMaterial("MAT_Player", new Color(0.25f, 0.72f, 0.42f));
            BotMaterial = CreateMaterial("MAT_Bot", new Color(0.82f, 0.30f, 0.26f));
            LootMaterial = CreateMaterial("MAT_Loot", new Color(1f, 0.82f, 0.18f));
            RingMaterial = CreateMaterial("MAT_ZoneRing", new Color(0.20f, 0.85f, 1f, 0.9f));
            TracerMaterial = CreateMaterial("MAT_Tracer", new Color(1f, 0.95f, 0.6f, 0.9f));
        }

        // ------------------------------------------------------------ Armas

        private static WeaponDefinition CreateWeapon(
            string itemId, string displayName, WeaponCategory category,
            float damage, float rpm, int magazine, float range, float spread,
            bool automatic, float reload, int reserve, float headshot, int pellets = 1, float knockback = 4f)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.itemId = itemId;
            weapon.displayName = displayName;
            weapon.category = category;
            weapon.damage = damage;
            weapon.roundsPerMinute = rpm;
            weapon.magazineSize = magazine;
            weapon.range = range;
            weapon.spreadDegrees = spread;
            weapon.automatic = automatic;
            weapon.reloadSeconds = reload;
            weapon.maxReserve = reserve;
            weapon.headshotMultiplier = headshot;
            weapon.pellets = pellets;
            weapon.knockback = knockback;
            return weapon;
        }

        private static void CreateWeapons()
        {
            Pistol = CreateWeapon("wpn_pistol", "Pistola", WeaponCategory.Pistol,
                18f, 300f, 12, 60f, 1.6f, false, 1.4f, 60, 2.2f, 1, 3f);

            Smg = CreateWeapon("wpn_smg", "SMG", WeaponCategory.SMG,
                14f, 780f, 30, 70f, 2.2f, true, 1.6f, 120, 2.0f, 1, 3f);

            Rifle = CreateWeapon("wpn_rifle", "Rifle", WeaponCategory.Rifle,
                22f, 600f, 30, 120f, 1.1f, true, 1.8f, 90, 2.2f, 1, 5f);

            Shotgun = CreateWeapon("wpn_shotgun", "Shotgun", WeaponCategory.Shotgun,
                12f, 90f, 6, 35f, 6f, false, 2.2f, 24, 1.6f, 8, 6f);

            Sniper = CreateWeapon("wpn_sniper", "Sniper", WeaponCategory.Sniper,
                85f, 45f, 5, 250f, 0.2f, false, 2.6f, 20, 2.6f, 1, 10f);

            BotWeapon = Smg;
        }

        // ------------------------------------------------------------ Itens

        private static ItemDefinition CreateItem(
            string itemId, string displayName, ItemType type, int maxStack, float spawnWeight,
            float effectAmount, int minAmount, int maxAmount, WeaponDefinition weapon = null)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = itemId;
            item.displayName = displayName;
            item.type = type;
            item.maxStack = maxStack;
            item.spawnWeight = spawnWeight;
            item.effectAmount = effectAmount;
            item.minAmount = minAmount;
            item.maxAmount = maxAmount;
            item.weapon = weapon;
            return item;
        }

        private static void CreateItems()
        {
            var ammo = CreateItem("itm_ammo", "Munição", ItemType.Ammo, 90, 0.30f, 30f, 1, 2);
            var bandage = CreateItem("itm_bandage", "Bandagem", ItemType.Medkit, 4, 0.22f, 25f, 1, 2);
            var medkit = CreateItem("itm_medkit", "Kit Médico", ItemType.Medkit, 2, 0.18f, 60f, 1, 1);
            var armor = CreateItem("itm_armor", "Colete", ItemType.Armor, 1, 0.18f, 50f, 1, 1);

            var pistolItem = CreateItem("itm_wpn_pistol", "Pistola", ItemType.Weapon, 1, 0.10f, 0f, 1, 1, Pistol);
            var smgItem = CreateItem("itm_wpn_smg", "SMG", ItemType.Weapon, 1, 0.11f, 0f, 1, 1, Smg);
            var rifleItem = CreateItem("itm_wpn_rifle", "Rifle", ItemType.Weapon, 1, 0.10f, 0f, 1, 1, Rifle);
            var shotgunItem = CreateItem("itm_wpn_shotgun", "Shotgun", ItemType.Weapon, 1, 0.08f, 0f, 1, 1, Shotgun);
            var sniperItem = CreateItem("itm_wpn_sniper", "Sniper", ItemType.Weapon, 1, 0.05f, 0f, 1, 1, Sniper);

            LootTable = new[]
            {
                ammo, bandage, medkit, armor,
                pistolItem, smgItem, rifleItem, shotgunItem, sniperItem
            };
        }

        // ------------------------------------------------------------ Partida

        private static void CreateConfig()
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            config.totalPlayers = 50;
            config.waitingIslandSeconds = 15f;
            config.planeFlightSeconds = 22f;
            config.matchEndDelay = 3f;

            config.mapRadius = 300f;
            config.mapCenter = Vector3.zero;
            config.planeStart = new Vector3(-340f, 400f, -340f);
            config.planeEnd = new Vector3(340f, 400f, 340f);
            config.planeAltitude = 400f;
            config.waitingIslandSpawn = new Vector3(0f, 0.6f, -14f);
            config.lootSpawnCount = 380;

            config.zonePhases = new[]
            {
                new ZoneManager.ZonePhase { waitSeconds = 25f, shrinkSeconds = 25f, targetRadius = 200f, damagePerSecond = 2f },
                new ZoneManager.ZonePhase { waitSeconds = 20f, shrinkSeconds = 20f, targetRadius = 140f, damagePerSecond = 4f },
                new ZoneManager.ZonePhase { waitSeconds = 18f, shrinkSeconds = 18f, targetRadius = 90f,  damagePerSecond = 7f },
                new ZoneManager.ZonePhase { waitSeconds = 15f, shrinkSeconds = 15f, targetRadius = 55f,  damagePerSecond = 10f },
                new ZoneManager.ZonePhase { waitSeconds = 12f, shrinkSeconds = 12f, targetRadius = 28f,  damagePerSecond = 15f },
                new ZoneManager.ZonePhase { waitSeconds = 10f, shrinkSeconds = 10f, targetRadius = 6f,   damagePerSecond = 20f }
            };

            Config = config;
        }

        // ------------------------------------------------------------ Prefabs

        private static void CreateTracerPrefab()
        {
            var go = new GameObject("TracerPrefab");
            go.transform.SetParent(_prefabRoot, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = 0.06f;
            line.endWidth = 0.02f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = new Color(1f, 0.95f, 0.6f, 0.9f);
            line.endColor = new Color(1f, 0.8f, 0.3f, 0.1f);

            if (TracerMaterial != null) line.sharedMaterial = TracerMaterial;

            go.AddComponent<Tracer>();
            go.SetActive(false);
            TracerPrefab = go;
        }

        private static void CreateLootPrefab()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "LootPrefab";
            go.transform.SetParent(_prefabRoot, false);
            go.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

            var box = go.GetComponent<Collider>();
            if (box != null) box.isTrigger = true;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null && LootMaterial != null) renderer.sharedMaterial = LootMaterial;

            go.AddComponent<LootPickup>();
            go.SetActive(false);
            LootPrefab = go;
        }

        private static void CreateBotPrefab()
        {
            var root = new GameObject("BotPrefab");
            root.transform.SetParent(_prefabRoot, false);

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.4f;

            // Corpo visual sem collider (o CharacterController já é o collider).
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            var bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null) Object.DestroyImmediate(bodyCollider);
            var bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null && BotMaterial != null) bodyRenderer.sharedMaterial = BotMaterial;

            root.AddComponent<Health>();

            var weaponGo = new GameObject("Weapon");
            weaponGo.transform.SetParent(root.transform, false);
            weaponGo.transform.localPosition = new Vector3(0.25f, 1.3f, 0.35f);

            var weapon = weaponGo.AddComponent<WeaponController>();
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(weaponGo.transform, false);
            muzzle.localPosition = new Vector3(0f, 0f, 0.6f);
            weapon.Configure(BotWeapon != null ? BotWeapon : Pistol, muzzle, TracerPrefab, ~0, "Bot");

            var inventory = root.AddComponent<InventoryComponent>();
            inventory.AutoScan = false;

            root.AddComponent<BotAI>();
            root.SetActive(false);
            BotPrefab = root;
        }
    }
}

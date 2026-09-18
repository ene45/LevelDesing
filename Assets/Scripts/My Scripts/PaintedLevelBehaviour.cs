using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer.Mechanics
{
    /// <summary>
    /// Adds gameplay to a level that was already painted in a single Tilemap.
    /// It alternates blue/orange platform tiles and replaces painted nut tiles
    /// with functional collectible prefabs at runtime.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class PaintedLevelBehaviour : MonoBehaviour
    {
        [Header("Level Tilemap")]
        [SerializeField] private Tilemap levelTilemap;
        [SerializeField] private TilemapCollider2D levelCollider;

        [Header("Inactive Platform Opacity")]
        [SerializeField, Range(0f, 1f)] private float inactiveAlpha = 0.3f;

        [Header("Blue Platform Tiles")]
        [SerializeField] private TileBase blueFull;
        [SerializeField] private TileBase blueThin;

        [Header("Orange Platform Tiles")]
        [SerializeField] private TileBase orangeFull;
        [SerializeField] private TileBase orangeThin;

        [Header("Painted Collectibles")]
        [SerializeField] private TileBase goldenNutA;
        [SerializeField] private TileBase goldenNutB;
        [SerializeField] private GameObject collectiblePrefab;
        [SerializeField] private Transform collectiblesParent;

        [Header("Switch Settings")]
        [SerializeField] private bool blueStartsActive = true;
        [SerializeField] private PlayerController player;

        private readonly List<PaintedTile> blueTiles = new List<PaintedTile>();
        private readonly List<PaintedTile> orangeTiles = new List<PaintedTile>();
        private readonly List<Vector3Int> collectibleCells = new List<Vector3Int>();

        private Collider2D playerCollider;
        private Tilemap blueMap;
        private Tilemap orangeMap;
        private TilemapCollider2D blueCollider;
        private TilemapCollider2D orangeCollider;
        private bool blueActive;

        private struct PaintedTile
        {
            public Vector3Int position;
            public TileBase tile;
            public Matrix4x4 matrix;
            public Color color;

            public PaintedTile(Vector3Int position, TileBase tile, Tilemap source)
            {
                this.position = position;
                this.tile = tile;
                matrix = source.GetTransformMatrix(position);
                color = source.GetColor(position);
            }
        }

        private void Reset()
        {
            levelTilemap = GetComponent<Tilemap>();
            levelCollider = GetComponent<TilemapCollider2D>();
        }

        private void Awake()
        {
            if (levelTilemap == null)
                levelTilemap = GetComponent<Tilemap>();

            if (levelCollider == null)
                levelCollider = GetComponent<TilemapCollider2D>();

            if (levelTilemap == null)
            {
                Debug.LogError("PaintedLevelBehaviour needs a Tilemap reference.", this);
                enabled = false;
                return;
            }

            if (player != null)
                playerCollider = player.GetComponent<Collider2D>();

            ScanPaintedLevel();
            PreparePlatformGroups();
            SpawnCollectibles();
            RefreshTokenController();

            blueActive = blueStartsActive;
            ApplyPlatformState();
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(1) && player != null && player.controlEnabled)
                TrySwitchPlatforms();
        }

        private void Start()
        {
            // PlayerController.Awake has run by now, even with our early execution order.
            if (player == null)
                player = FindObjectOfType<PlayerController>();

            if (player != null)
                playerCollider = player.collider2d != null
                    ? player.collider2d : player.GetComponent<Collider2D>();

            if (playerCollider == null)
            {
                Debug.LogError("Assign a PlayerController with a Collider2D to PaintedLevelBehaviour.", this);
                enabled = false;
            }
        }

        private void PreparePlatformGroups()
        {
            blueMap = CreatePlatformMap("Blue Platforms", blueTiles, out blueCollider);
            orangeMap = CreatePlatformMap("Orange Platforms", orangeTiles, out orangeCollider);

            // Only remove circuit tiles. Ground and other painted tiles stay in place.
            foreach (PaintedTile tile in blueTiles)
                levelTilemap.SetTile(tile.position, null);
            foreach (PaintedTile tile in orangeTiles)
                levelTilemap.SetTile(tile.position, null);

            RefreshCollider();
        }

        private Tilemap CreatePlatformMap(
            string groupName, List<PaintedTile> tiles, out TilemapCollider2D groupCollider)
        {
            GameObject group = new GameObject(groupName);
            group.layer = levelTilemap.gameObject.layer;
            group.transform.SetParent(levelTilemap.transform.parent, false);
            group.transform.localPosition = levelTilemap.transform.localPosition;
            group.transform.localRotation = levelTilemap.transform.localRotation;
            group.transform.localScale = levelTilemap.transform.localScale;

            Tilemap map = group.AddComponent<Tilemap>();
            map.tileAnchor = levelTilemap.tileAnchor;
            map.orientation = levelTilemap.orientation;
            map.orientationMatrix = levelTilemap.orientationMatrix;
            TilemapRenderer renderer = group.AddComponent<TilemapRenderer>();
            TilemapRenderer source = levelTilemap.GetComponent<TilemapRenderer>();
            if (source != null)
            {
                renderer.sharedMaterials = source.sharedMaterials;
                renderer.sortingLayerID = source.sortingLayerID;
                renderer.sortingOrder = source.sortingOrder;
            }

            foreach (PaintedTile tile in tiles)
            {
                map.SetTile(tile.position, tile.tile);
                map.SetTileFlags(tile.position, TileFlags.None);
                map.SetTransformMatrix(tile.position, tile.matrix);
                map.SetColor(tile.position, tile.color);
            }

            groupCollider = group.AddComponent<TilemapCollider2D>();
            if (levelCollider != null)
                groupCollider.sharedMaterial = levelCollider.sharedMaterial;
            groupCollider.ProcessTilemapChanges();
            return map;
        }

        private void ScanPaintedLevel()
        {
            blueTiles.Clear();
            orangeTiles.Clear();
            collectibleCells.Clear();

            foreach (Vector3Int position in levelTilemap.cellBounds.allPositionsWithin)
            {
                TileBase tile = levelTilemap.GetTile(position);

                if (tile == null)
                    continue;

                if (IsBlueTile(tile))
                    blueTiles.Add(new PaintedTile(position, tile, levelTilemap));
                else if (IsOrangeTile(tile))
                    orangeTiles.Add(new PaintedTile(position, tile, levelTilemap));
                else if (IsCollectibleTile(tile))
                    collectibleCells.Add(position);
            }
        }

        private bool IsBlueTile(TileBase tile)
        {
            return (blueFull != null && tile == blueFull)
                || (blueThin != null && tile == blueThin);
        }

        private bool IsOrangeTile(TileBase tile)
        {
            return (orangeFull != null && tile == orangeFull)
                || (orangeThin != null && tile == orangeThin);
        }

        private bool IsCollectibleTile(TileBase tile)
        {
            return (goldenNutA != null && tile == goldenNutA)
                || (goldenNutB != null && tile == goldenNutB);
        }

        private void SpawnCollectibles()
        {
            if (collectibleCells.Count == 0)
                return;

            if (collectiblePrefab == null)
            {
                Debug.LogWarning(
                    "Painted nut tiles were found, but no collectible prefab was assigned. " +
                    "The painted tiles were left unchanged.",
                    this);
                return;
            }

            foreach (Vector3Int position in collectibleCells)
            {
                Vector3 worldPosition = levelTilemap.GetCellCenterWorld(position);
                Instantiate(
                    collectiblePrefab,
                    worldPosition,
                    Quaternion.identity,
                    collectiblesParent);

                levelTilemap.SetTile(position, null);
            }
        }

        private void RefreshTokenController()
        {
            TokenController tokenController = FindObjectOfType<TokenController>();

            if (tokenController != null)
                tokenController.tokens = FindObjectsOfType<TokenInstance>();
        }

        private void TrySwitchPlatforms()
        {
            if (playerCollider == null)
                return;

            blueActive = !blueActive;
            ApplyPlatformState();
            Physics2D.SyncTransforms();

            // Match PlataformasCambiantes: test ONLY the newly enabled circuit.
            Collider2D newCircuit = blueActive ? blueCollider : orangeCollider;
            if (!newCircuit.enabled || newCircuit.isTrigger)
                return;

            ColliderDistance2D contact = newCircuit.Distance(playerCollider);
            if (contact.isValid && contact.isOverlapped)
            {
                blueActive = !blueActive;
                ApplyPlatformState();
                Physics2D.SyncTransforms();
            }
        }

        private void ApplyPlatformState()
        {
            ApplyGroup(blueMap, blueCollider, blueActive);
            ApplyGroup(orangeMap, orangeCollider, !blueActive);
        }

        private void ApplyGroup(Tilemap map, TilemapCollider2D collider, bool active)
        {
            collider.enabled = active;
            Color color = levelTilemap.color;
            color.a *= active ? 1f : inactiveAlpha;
            map.color = color;
        }

        private void RefreshCollider()
        {
            if (levelCollider != null && levelCollider.hasTilemapChanges)
                levelCollider.ProcessTilemapChanges();

            Physics2D.SyncTransforms();
        }


    }
}

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

        [Header("Inactive Platform Preview")]
        [SerializeField] private Tilemap inactivePreviewTilemap;
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
        private bool blueActive;

        private struct PaintedTile
        {
            public Vector3Int position;
            public TileBase tile;

            public PaintedTile(Vector3Int position, TileBase tile)
            {
                this.position = position;
                this.tile = tile;
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

            PrepareInactivePreview();

            if (player != null)
                playerCollider = player.GetComponent<Collider2D>();

            ScanPaintedLevel();
            SpawnCollectibles();
            RefreshTokenController();

            blueActive = blueStartsActive;
            ApplyPlatformState();
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(1))
                TrySwitchPlatforms();
        }

        private void PrepareInactivePreview()
        {
            if (inactivePreviewTilemap == null)
            {
                GameObject previewObject = new GameObject("Inactive Platform Preview");
                previewObject.layer = levelTilemap.gameObject.layer;
                previewObject.transform.SetParent(levelTilemap.transform.parent, false);
                previewObject.transform.localPosition = levelTilemap.transform.localPosition;
                previewObject.transform.localRotation = levelTilemap.transform.localRotation;
                previewObject.transform.localScale = levelTilemap.transform.localScale;

                inactivePreviewTilemap = previewObject.AddComponent<Tilemap>();
                TilemapRenderer previewRenderer =
                    previewObject.AddComponent<TilemapRenderer>();

                TilemapRenderer sourceRenderer =
                    levelTilemap.GetComponent<TilemapRenderer>();

                if (sourceRenderer != null)
                {
                    previewRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
                    previewRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
                    previewRenderer.sortingOrder = sourceRenderer.sortingOrder - 1;
                }

                inactivePreviewTilemap.tileAnchor = levelTilemap.tileAnchor;
            }

            inactivePreviewTilemap.ClearAllTiles();
            inactivePreviewTilemap.color =
                new Color(1f, 1f, 1f, inactiveAlpha);
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
                    blueTiles.Add(new PaintedTile(position, tile));
                else if (IsOrangeTile(tile))
                    orangeTiles.Add(new PaintedTile(position, tile));
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
            bool previousState = blueActive;

            blueActive = !blueActive;
            ApplyPlatformState();

            if (IsPlayerOverlappingLevel())
            {
                blueActive = previousState;
                ApplyPlatformState();
            }
        }

        private bool IsPlayerOverlappingLevel()
        {
            if (playerCollider == null || levelCollider == null)
                return false;

            ColliderDistance2D distance =
                playerCollider.Distance(levelCollider);

            return distance.isOverlapped;
        }

        private void ApplyPlatformState()
        {
            SetTiles(levelTilemap, blueTiles, blueActive);
            SetTiles(levelTilemap, orangeTiles, !blueActive);

            SetTiles(inactivePreviewTilemap, blueTiles, !blueActive);
            SetTiles(inactivePreviewTilemap, orangeTiles, blueActive);

            levelTilemap.RefreshAllTiles();
            inactivePreviewTilemap.RefreshAllTiles();
            RefreshCollider();
        }

        private void SetTiles(
            Tilemap targetTilemap,
            List<PaintedTile> tiles,
            bool visible)
        {
            foreach (PaintedTile paintedTile in tiles)
            {
                targetTilemap.SetTile(
                    paintedTile.position,
                    visible ? paintedTile.tile : null);
            }
        }

        private void RefreshCollider()
        {
            if (levelCollider != null && levelCollider.hasTilemapChanges)
                levelCollider.ProcessTilemapChanges();

            Physics2D.SyncTransforms();
        }


    }
}

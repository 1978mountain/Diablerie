﻿using System;
using System.Collections.Generic;
using Diablerie.Engine.Datasheets;
using Diablerie.Engine.IO.D2Formats;
using Diablerie.Engine.Utility;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Diablerie.Engine.World
{
    public class LevelBuilder
    {
        public readonly int gridX;
        public readonly int gridY;
        public readonly int gridWidth;
        public readonly int gridHeight;

        public LevelInfo info;
        string name;
        DS1[] grid;
        List<Popup> popups = new List<Popup>();
        private int popPad = 0;
        DT1.Sampler tileSampler = new DT1.Sampler();
        MonStat[] monStats;
        Color32[] palette;

        static readonly int mapEntryIndex = DT1.Tile.Index(30, 11, 10);
        static readonly int townEntryIndex = DT1.Tile.Index(30, 0, 10);
        static readonly int townEntry2Index = DT1.Tile.Index(31, 0, 10);
        static readonly int corpseLocationIndex = DT1.Tile.Index(32, 0, 10);
        static readonly int portalLocationIndex = DT1.Tile.Index(33, 0, 10);

        static DT1.Sampler specialTiles = new DT1.Sampler();
        static LevelBuilder()
        {
            // todo: Maybe add customized palette
            var palette = Palette.GetPalette(PaletteType.Act1);
            var dt1 = DT1.Load(Application.streamingAssetsPath + "/ds1edit.dt1", palette, mpq: false);
            specialTiles.Add(dt1.tiles);
        }

        public LevelBuilder(string name, Color32[] palette, int gridX = -1, int gridY = -1)
        {
            info = LevelInfo.Find(name);
            this.name = info.levelName;
            this.palette = palette;

            if (info.preset != null)
            {
                var ds1 = DS1.Load(info.preset.ds1Files[0]);
                this.gridX = ds1.width - 1;
                this.gridY = ds1.height - 1;
                gridWidth = 1;
                gridHeight = 1;
                grid = new DS1[1] { ds1 };
                popPad = info.preset.popPad;
            }
            else
            {
                if (info.maze != null)
                {
                    this.gridX = info.maze.sizeX;
                    this.gridY = info.maze.sizeY;
                }
                else
                {
                    this.gridX = gridX;
                    this.gridY = gridY;
                }
                gridWidth = info.sizeX / this.gridX;
                gridHeight = info.sizeY / this.gridY;
                grid = new DS1[gridWidth * gridHeight];
            }

            InitTileSampler();
        }

        public LevelBuilder(DS1 ds1)
        {
            name = System.IO.Path.GetFileName(ds1.filename);
            grid = new DS1[1] { ds1 };
            gridWidth = 1;
            gridHeight = 1;
            gridX = ds1.width - 1;
            gridY = ds1.height - 1;
        }

        private void InitTileSampler()
        {
            tileSampler = new DT1.Sampler();
            if (info != null)
            {
                foreach (var dt1Filename in info.type.dt1Files)
                {
                    var dt1 = DT1.Load(dt1Filename, palette);
                    tileSampler.Add(dt1.tiles);
                }
            }
        }

        public void Place(LevelPreset preset, Vector2i pos, int minIndex = 0, int maxIndex = -1)
        {
            if (maxIndex == -1)
                maxIndex = preset.ds1Files.Count;
            var ds1Filename = preset.ds1Files[Random.Range(minIndex, maxIndex)];
            var ds1 = DS1.Load(ds1Filename);
            Place(ds1, pos);
        }

        public void Place(DS1 ds1, Vector2i pos)
        {
            Debug.Assert(ds1.width - 1 == gridX);
            Debug.Assert(ds1.height - 1 == gridY);
            grid[pos.y * gridWidth + pos.x] = ds1;
        }

        void InstantiatePopups(DS1 ds1, int offsetX, int offsetY, Transform parent = null)
        {
            var startPos = new Vector2i[7];
            var firstFound = new bool[7];

            for (int layerIndex = 0; layerIndex < ds1.walls.Length; ++layerIndex)
            {
                var walls = ds1.walls[layerIndex];
                int i = 0;
                for (int y = 0; y < ds1.height; ++y)
                {
                    for (int x = 0; x < ds1.width; ++x, ++i)
                    {
                        if (walls[i].orientation != 10)
                            continue;
                        int mainIndex = walls[i].mainIndex;
                        int group;
                        // TODO merge 8, 9, 10
                        // TODO merge 12, 13
                        if (mainIndex == 8)
                            group = 0;
                        else if (mainIndex == 9)
                            group = 1;
                        else if (mainIndex == 10)
                            group = 2;
                        else if (mainIndex == 12)
                            group = 3;
                        else if (mainIndex == 13)
                            group = 4;
                        else if (mainIndex == 16)
                            group = 5;
                        else if (mainIndex == 20)
                            group = 6;
                        else
                            continue;
                    
                        if (firstFound[group])
                        {
                            int x1 = startPos[group].x;
                            int y1 = startPos[group].y;
                            int width = x - x1;
                            int height = y - y1;
                            var triggerArea = new IntRect(x1 + offsetX, y1 + offsetY, width, height);
                            var revealArea = new IntRect(
                                x1 + offsetX - 1, 
                                y1 + offsetY - 1, 
                                width + 3, 
                                height + 3
                            );
                            var popup = Popup.Create(triggerArea, revealArea, walls[i].subIndex);
                            popup.gameObject.name += "_pad" + popPad;  // TODO use popPad to adjust triggerArea
                            popup.transform.SetParent(parent);
                            popups.Add(popup);
                        }
                        else
                        {
                            startPos[group] = new Vector2i(x, y);
                            firstFound[group] = true;
                        }
                    }
                }
            }
        }

        public GameObject Instantiate(Vector2i offset)
        {
            UnityEngine.Profiling.Profiler.BeginSample("LevelBuilder.Instantiate");
            var root = new GameObject(name);

            if (info != null)
            {
                var collider = root.AddComponent<PolygonCollider2D>();
                collider.offset = Iso.MapTileToWorld(offset);
                collider.points = Iso.CreateTileRectPoints(info.sizeX, info.sizeY);
                collider.isTrigger = true;

                var level = root.AddComponent<Level>();
                level.info = info;

                SelectMonsterTypes();
            }

            InstantiateGrid(offset, root.transform);
            InstantiateDebugGrid(offset, root.transform);

            UnityEngine.Profiling.Profiler.EndSample();
            return root;
        }

        private void Instantiate(DS1 ds1, int x, int y, Transform root)
        {
            UnityEngine.Profiling.Profiler.BeginSample("LevelBuilder.InstantiateDS1");
            InstantiatePopups(ds1, x, y, root);
            InstantiateFloors(ds1, x, y, root);
            InstantiateWalls(ds1, x, y, root);
            InstantiateObjects(ds1, x, y, root);
            UnityEngine.Profiling.Profiler.EndSample();
        }

        private void InstantiateMonsters(int offsetX, int offsetY, Transform root)
        {
            if (info == null)
                return;

            UnityEngine.Profiling.Profiler.BeginSample("LevelBuilder.InstantiateMonsters");

            int density = info.monDen[0];

            for (int x = offsetX; x < offsetX + gridX; ++x)
            {
                for (int y = offsetY; y < offsetY + gridY; ++y)
                {
                    int sample = Random.Range(0, 100000);
                    if (sample >= density)
                        continue;

                    var monStat = monStats[Random.Range(0, monStats.Length)];
                    Spawn(monStat, x, y, info.id, root);
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();
        }

        private static void Spawn(MonStat monStat, int x, int y, int level, Transform root)
        {
            CollisionLayers collisionMask = CollisionLayers.Walk;
            if (!CollisionMap.Passable(new Vector2i(x, y) * Iso.SubTileCount, collisionMask, monStat.ext.sizeX))
                return;

            int count = Random.Range(monStat.minGrp, monStat.maxGrp + 1);
            for (int i = 0; i < count; ++i)
            {
                var mon = global::Diablerie.Game.World.WorldBuilder.SpawnMonster(monStat, Iso.MapTileToWorld(x, y), root);
                mon.level = level;
            }

            if (monStat.minion1 != null)
            {
                int minionCount = Random.Range(monStat.partyMin, monStat.partyMax);
                for (int i = 0; i < minionCount; ++i)
                {
                    var mon = global::Diablerie.Game.World.WorldBuilder.SpawnMonster(monStat.minion1, Iso.MapTileToWorld(x, y), root);
                    mon.level = level;
                }
            }
        }

        private void SelectMonsterTypes()
        {
            if (info == null)
                return;

            monStats = new MonStat[info.numMon];
            int[] monsterColumns = new int[info.numMon];
            for (int i = 0; i < info.numMon; ++i)
                monsterColumns[i] = -1;

            for (int i = 0; i < info.numMon; ++i)
            {
                int index;
                do
                {
                    index = Random.Range(0, info.monsters.Count);
                }
                while (System.Array.IndexOf(monsterColumns, index) != -1);
                monsterColumns[i] = index;
                monStats[i] = MonStat.Find(info.monsters[index]);
            }
        }

        private void InstantiateGrid(Vector2i offset, Transform root)
        {
            UnityEngine.Profiling.Profiler.BeginSample("LevelBuilder.InstantiateGrid");
            int i = 0;
            for (int y = 0; y < gridHeight; ++y)
            {
                for (int x = 0; x < gridWidth; ++x, ++i)
                {
                    var ds1 = grid[i];
                    int offsetX = offset.x + x * gridX;
                    int offsetY = offset.y + y * gridY;
                    if (ds1 != null)
                    {
                        Instantiate(ds1, offsetX, offsetY, root.transform);
                        InstantiateMonsters(offsetX, offsetY, root.transform);
                    }
                    else if (info != null && info.drlgType == 3)
                    {
                        FillGap(offset, x, y, root.transform);
                        InstantiateMonsters(offsetX, offsetY, root.transform);
                    }
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();
        }

        private void InstantiateDebugGrid(Vector2i offset, Transform root)
        {
            UnityEngine.Profiling.Profiler.BeginSample("LevelBuilder.InstantiateDebugGrid");
            var grid = new GameObject("debug grid");
            grid.transform.SetParent(root);
            grid.layer = UnityLayers.SpecialTiles;

            for (int y = 0; y < gridHeight; ++y)
            {
                for (int x = 0; x < gridWidth; ++x)
                {
                    var cellObject = new GameObject(x + ", " + y);
                    cellObject.transform.position = Iso.MapToWorld(
                        (x * gridX + offset.x) * Iso.SubTileCount - 2,
                        (y * gridY + offset.y) * Iso.SubTileCount - 2);
                    cellObject.transform.SetParent(grid.transform);
                    cellObject.layer = UnityLayers.SpecialTiles;
                    var line = cellObject.AddComponent<LineRenderer>();
                    line.startWidth = 0.1f;
                    line.endWidth = 0.1f;
                    line.material = Materials.normal;
                    line.useWorldSpace = false;
                    var corners = new Vector3[] {
                        Iso.MapTileToWorld(0, 0),
                        Iso.MapTileToWorld(0 + gridX, 0),
                        Iso.MapTileToWorld(0 + gridX, gridY),
                        Iso.MapTileToWorld(0, gridY),
                        Iso.MapTileToWorld(0, 0)
                    };
                    line.positionCount = corners.Length;
                    line.SetPositions(corners);
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();
        }

        private void InstantiateFloors(DS1 ds1, int offsetX, int offsetY, Transform root)
        {
            var layerObject = new GameObject("floors");
            var layerTransform = layerObject.transform;
            layerTransform.SetParent(root);

            for (int f = 0; f < ds1.floors.Length; ++f)
            {
                var floors = ds1.floors[f];
                int i = 0;
                for (int y = 0; y < ds1.height - 1; ++y)
                {
                    for (int x = 0; x < ds1.width - 1; ++x)
                    {
                        var cell = floors[i + x];
                        if (cell.prop1 == 0) // no tile here
                            continue;

                        if ((cell.prop4 & 0x80) != 0)
                            continue;

                        DT1.Tile tile;
                        if (ds1.tileSampler.Sample(cell.tileIndex, out tile))
                        {
                            CreateTile(tile, offsetX + x, offsetY + y, parent: layerTransform);
                        }
                    }
                    i += ds1.width;
                }
            }
        }

        private void InstantiateWalls(DS1 ds1, int offsetX, int offsetY, Transform root)
        {
            for (int w = 0; w < ds1.walls.Length; ++w)
            {
                var layerObject = new GameObject("walls " + (w + 1));
                var layerTransform = layerObject.transform;
                layerTransform.SetParent(root);
                var sampler = ds1.tileSampler;

                var cells = ds1.walls[w];
                int i = 0;
                for (int y = 0; y < ds1.height - 1; ++y)
                {
                    for (int x = 0; x < ds1.width - 1; ++x)
                    {
                        var cell = cells[i + x];
                        if (cell.prop1 == 0) // no tile here
                            continue;

                        DT1.Tile tile;

                        if (cell.orientation == 10 || cell.orientation == 11)
                        {
                            CreateSpecialTile(cell, offsetX + x, offsetY + y, parent: root);
                            continue;
                        }

                        if (sampler.Sample(cell.tileIndex, out tile))
                        {
                            var renderer = CreateTile(tile, offsetX + x, offsetY + y, parent: layerTransform);
                            PutToPopup(cell, renderer, offsetX + x, offsetY + y);
                        }
                        else
                        {
                            Debug.LogWarning("wall tile not found (index " + cell.mainIndex + " " + cell.subIndex + " " + cell.orientation + ") at " + x + ", " + y);
                        }

                        if (cell.orientation == 3)
                        {
                            int orientation = 4;
                            int index = DT1.Tile.Index(cell.mainIndex, cell.subIndex, orientation);
                            if (sampler.Sample(index, out tile))
                            {
                                CreateTile(tile, offsetX + x, offsetY + y, parent: layerTransform);
                            }
                            else
                            {
                                Debug.LogWarning("wall tile not found (index " + cell.mainIndex + " " + cell.subIndex + " " + orientation + ") at " + x + ", " + y);
                            }
                        }
                    }
                    i += ds1.width;
                }
            }
        }
    
        private void PutToPopup(DS1.Cell cell, Renderer renderer, int x, int y)
        {
            foreach (Popup popup in popups)
            {
                if (popup.revealMainIndex == cell.mainIndex && popup.revealArea.Contains(x, y))
                {
                    popup.roofs.Add(renderer);
                }
            }
        }

        private void CreateSpecialTile(DS1.Cell cell, int x, int y, Transform parent)
        {
            // debug visualization
            DT1.Tile tile;
            if (specialTiles.Sample(cell.tileIndex, out tile))
            {
                var renderer = CreateTile(tile, x, y, parent: parent);
                renderer.gameObject.layer = UnityLayers.SpecialTiles;
            }

            if (info == null)
                return;

            if (cell.mainIndex < 8)
            {
                int targetLevelId = info.vis[cell.mainIndex];
                int warpId = info.warp[cell.mainIndex];
                var targetLevel = LevelInfo.Find(targetLevelId);
                var levelWarpInfo = LevelWarpInfo.Find(warpId);
                if (levelWarpInfo == null)
                {
                    Debug.LogWarning("Warp info wasn't found");
                    return;
                }
                Warp.Create(x, y, levelWarpInfo, info, targetLevel, parent);
            }
        }

        private void InstantiateObjects(DS1 ds1, int offsetX, int offsetY, Transform root)
        {
            offsetX *= Iso.SubTileCount;
            offsetY *= Iso.SubTileCount;
            int monsterLevel = info != null ? info.id : 1;
            foreach (var spawnInfo in ds1.objects)
            {
                var preset = spawnInfo.preset;
                bool created = CreateObject(preset, offsetX + spawnInfo.x, offsetY + spawnInfo.y, monsterLevel, root);
                if (!created)
                    Debug.LogWarning("Failed to instantiate objects from \"" + ds1.filename + "\" act " + preset.act + ", type " + preset.type + ", id " + preset.id);
            }
        }

        private void FillGap(Vector2i offset, int x, int y, Transform root)
        {
            UnityEngine.Profiling.Profiler.BeginSample("LevelBuilder.FillGap");
            int offsetX = x * gridX;
            int offsetY = y * gridY;

            for (y = offsetY; y < offsetY + gridY; ++y)
            {
                for (x = offsetX; x < offsetX + gridX; ++x)
                {
                    DT1.Tile tile;
                    tileSampler.Sample(0, out tile);
                    CreateTile(tile, offset.x + x, offset.y + y, parent: root);
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();
        }

        static Renderer CreateTile(DT1.Tile tile, int x, int y, int orderInLayer = 0, Transform parent = null)
        {
            var texture = tile.texture;
            var pos = Iso.MapTileToWorld(x, y);

            GameObject gameObject = new GameObject();
            gameObject.name = tile.mainIndex + "_" + tile.subIndex + "_" + tile.orientation;
            gameObject.transform.position = pos;
            if (parent)
                gameObject.transform.SetParent(parent);
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            Mesh mesh = new Mesh();
            float x0 = tile.textureX;
            float y0 = tile.textureY;
            float w = tile.width / Iso.pixelsPerUnit;
            float h = (-tile.height) / Iso.pixelsPerUnit;
            if (tile.orientation == 0 || tile.orientation == 15)
            {
                var topLeft = new Vector3(-1f, 0.5f);
                if (tile.orientation == 15)
                    topLeft.y += tile.roofHeight / Iso.pixelsPerUnit;
                mesh.vertices = new Vector3[]
                {
                    topLeft,
                    topLeft + new Vector3(0, -h),
                    topLeft + new Vector3(w, -h),
                    topLeft + new Vector3(w, 0)
                };
                mesh.triangles = new int[] {2, 1, 0, 3, 2, 0};
                mesh.uv = new Vector2[]
                {
                    new Vector2(x0 / texture.width, -y0 / texture.height),
                    new Vector2(x0 / texture.width, (-y0 + tile.height) / texture.height),
                    new Vector2((x0 + tile.width) / texture.width, (-y0 + tile.height) / texture.height),
                    new Vector2((x0 + tile.width) / texture.width, -y0 / texture.height)
                };

                meshRenderer.sortingLayerID = tile.orientation == 0 ? SortingLayers.Floor : SortingLayers.Roof;
                meshRenderer.sortingOrder = orderInLayer;

                gameObject.name += tile.orientation == 0 ? " (floor)" : " (roof)";
            }
            else if (tile.orientation > 15)
            {
                int upperPart = Math.Min(96, -tile.height);
                y0 -= upperPart;
                var topLeft = new Vector3(-1f, upperPart / Iso.pixelsPerUnit - 0.5f);
                mesh.vertices = new Vector3[] {
                    topLeft,
                    topLeft + new Vector3(0, -h),
                    topLeft + new Vector3(w, -h),
                    topLeft + new Vector3(w, 0)
                };
                mesh.triangles = new int[] { 2, 1, 0, 3, 2, 0 };
                mesh.uv = new Vector2[] {
                    new Vector2 (x0 / texture.width, -y0 / texture.height),
                    new Vector2 (x0 / texture.width, (-y0 + tile.height) / texture.height),
                    new Vector2 ((x0 + tile.width) / texture.width, (-y0 + tile.height) / texture.height),
                    new Vector2 ((x0 + tile.width) / texture.width, -y0 / texture.height)
                };
                meshRenderer.sortingLayerID = SortingLayers.LowerWall;
                meshRenderer.sortingOrder = orderInLayer;
            
                gameObject.name += " (lower wall)";
            }
            else
            {
                var topLeft = new Vector3(-1f, h - 0.5f);
                mesh.vertices = new Vector3[] {
                    topLeft,
                    topLeft + new Vector3(0, -h),
                    topLeft + new Vector3(w, -h),
                    topLeft + new Vector3(w, 0)
                };
                mesh.triangles = new int[] { 2, 1, 0, 3, 2, 0 };
                mesh.uv = new Vector2[] {
                    new Vector2 (x0 / texture.width, (-y0 - tile.height) / texture.height),
                    new Vector2 (x0 / texture.width, -y0 / texture.height),
                    new Vector2 ((x0 + tile.width) / texture.width, -y0 / texture.height),
                    new Vector2 ((x0 + tile.width) / texture.width, (-y0 - tile.height) / texture.height)
                };
                meshRenderer.sortingOrder = Iso.SortingOrder(pos) - 4;
            }
            meshFilter.mesh = mesh;

            int flagIndex = 0;
            var collisionMapOffset = Iso.Snap(Iso.MapToIso(pos));
            DT1.BlockFlags mask = DT1.BlockFlags.Walk | DT1.BlockFlags.PlayerWalk;
            for (int dy = 2; dy > -3; --dy)
            {
                for (int dx = -2; dx < 3; ++dx, ++flagIndex)
                {
                    Vector2i subCellPos = collisionMapOffset + new Vector2i(dx, dy);
                    bool passable = (tile.flags[flagIndex] & mask) == 0;
                    CollisionLayers blockedLayers = passable ? CollisionLayers.None : CollisionLayers.Walk;
                    if (tile.orientation == 0)
                    {
                        CollisionMap.SetBlocked(subCellPos, blockedLayers);
                    }
                    else if (CollisionMap.Passable(subCellPos, CollisionLayers.Walk) && !passable)
                    {
                        CollisionMap.SetBlocked(subCellPos, blockedLayers);
                    }
                }
            }

            meshRenderer.material = tile.material;
            return meshRenderer;
        }

        static bool CreateObject(SpawnPreset obj, int x, int y, int level, Transform root)
        {
            var pos = Iso.MapToWorld(x - 2, y - 2);
            if (obj.type == 2)
            {
                if (obj.objectId >= ObjectInfo.sheet.Count)
                {
                    var go = new GameObject("spawn failure");
                    go.transform.position = pos;
                    return false;
                }
                ObjectInfo objectInfo = ObjectInfo.sheet[obj.objectId];
                var staticObject = global::Diablerie.Game.World.WorldBuilder.SpawnObject(objectInfo, pos, parent: root);
                staticObject.modeName = obj.mode;
                return true;
            }

            string monPreset = MonPreset.Find(obj.act, obj.id);
            MonStat monStat = null;
            SuperUnique superUnique = null;

            if (monPreset != null)
            {
                monStat = MonStat.Find(monPreset);
                if (monStat == null)
                    superUnique = SuperUnique.Find(monPreset);
            }
            else
            {
                monStat = MonStat.sheet[obj.id];
            }

            if (monStat != null)
            {
                global::Diablerie.Game.World.WorldBuilder.SpawnMonster(monStat, pos, root);
                return true;
            }

            if (superUnique != null)
            {
                var monster = global::Diablerie.Game.World.WorldBuilder.SpawnMonster(superUnique.monStat, pos, root);
                monster.gameObject.name = superUnique.nameStr;
                monster.title = superUnique.name;
                monster.level = level;
                int minionCount = Random.Range(superUnique.minGrp, superUnique.maxGrp + 1);
                for (int i = 0; i < minionCount; ++i)
                {
                    var minion = global::Diablerie.Game.World.WorldBuilder.SpawnMonster(superUnique.monStat, pos, root);
                    minion.level = level;
                }
                return true;
            }

            if (obj.id == 10)
            {
                // Fallens
                for (int i = 0; i < 4; ++i)
                    global::Diablerie.Game.World.WorldBuilder.SpawnMonster("fallen1", pos, root);
                return true;
            }

            if (obj.id == 11)
            {
                // Fallen shaman + fallens
                Spawn(MonStat.Find("fallenshaman1"), x, y, level, root);
                for (int i = 0; i < 4; ++i)
                {
                    var fallen = global::Diablerie.Game.World.WorldBuilder.SpawnMonster("fallen1", pos, root);
                    fallen.level = level;
                }
                return true;
            }

            if (obj.id == 27)
            {
                // Fallen shaman
                Spawn(MonStat.Find("fallenshaman1"), x, y, level, root);
                return true;
            }

            return false;
        }

        public Vector2i FindEntry()
        {
            foreach (var ds1 in grid)
            {
                if (ds1 == null)
                    continue;

                foreach (var cells in ds1.walls)
                {
                    int i = 0;
                    for (int y = 0; y < ds1.height; ++y)
                    {
                        for (int x = 0; x < ds1.width; ++x, ++i)
                        {
                            var cell = cells[i];
                            if (cell.tileIndex == mapEntryIndex)
                            {
                                return new Vector2i(x, y);
                            }
                            else if (cell.tileIndex == townEntryIndex)
                            {
                                return new Vector2i(x, y);
                            }
                            else if (cell.tileIndex == townEntry2Index)
                            {
                                return new Vector2i(x, y);
                            }
                        }
                    }
                }
            }

            return new Vector2i(info.sizeX, info.sizeY) / 2;
        }
    }
}

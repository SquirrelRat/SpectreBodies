using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Nodes;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using SharpDX;
using SDXVector2 = SharpDX.Vector2;
using SDXVector3 = SharpDX.Vector3;
using SDXVector4 = SharpDX.Vector4;
using SDXColor = SharpDX.Color;

namespace SpectreBodies
{
    public class SpectreBodies : BaseSettingsPlugin<SpectreBodiesSettings>
    {
        private const string MONSTER_METADATA_PATH = "/Monsters/";
        private const int MAX_CACHE_SIZE = 1000;
        private const int FRAME_UPDATE_INTERVAL = 10;
        
        // Thread-safe collections for corpse tracking
        private readonly ConcurrentQueue<string> _recentCorpseQueue = new ConcurrentQueue<string>();
        private readonly HashSet<string> _recentCorpseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _corpseSetLock = new object();

        // Caches with size limits to prevent memory leaks
        private readonly Dictionary<string, string> _displayNameCache = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _renderNameCache = new Dictionary<string, string>();
        private readonly object _cacheLock = new object();

        // UI state variables
        private string _newSpectreBuffer = "";
        private string _cachedSpectreListSource = "";
        private HashSet<string> _cachedValidSpectreBodies = new HashSet<string>();
        private bool _showSpectreEditor = false;
        private ExileCore.Shared.Coroutine _corpseScanningCoroutine;

        // Frame data cache for performance - important for FPS.
        // _filteredEntities is the shared cache (written under _frameCacheLock);
        // _drawBuffer is render-thread-local, _scanBuffer is coroutine-local scratch.
        private SDXVector3 _cachedPlayerPos;
        private float _cachedDrawDistanceSqr;
        private List<Entity> _filteredEntities = new List<Entity>();
        private List<Entity> _drawBuffer = new List<Entity>();
        private List<Entity> _scanBuffer = new List<Entity>();
        private int _frameCounter;
        private readonly object _frameCacheLock = new object();

        // Bundled spectre info database (friendly names, roles, acquisition hints).
        private SpectreDatabase _spectreDb = new SpectreDatabase();

        public override bool Initialise()
        {
            _spectreDb = SpectreDatabase.Load();
            // Start background coroutine for corpse scanning - important for performance
            _corpseScanningCoroutine = new ExileCore.Shared.Coroutine(CorpseScanning(), this, "SpectreBodies");
            Core.ParallelRunner.Run(_corpseScanningCoroutine);
            return true;
        }

        private void DrawSpectreEditorWindow()
        {
            if (ImGui.Begin("Spectre Editor", ref _showSpectreEditor, ImGuiWindowFlags.None))
            {
                var titleColor = new System.Numerics.Vector4(1.0f, 0.84f, 0.0f, 1.0f);
                ImGui.TextColored(titleColor, "Spectre Body List Editor");

                var currentList = ParseSpectreList(Settings.SpectreListSource).ToList();

                var listChanged = false;

                string spectreToDelete = null;
                foreach (var spectre in currentList)
                {
                    if (!Settings.SpectreColors.ContainsKey(spectre))
                    {
                        Settings.SpectreColors[spectre] = new ColorNode(Settings.TextColor.Value);
                    }
                    
                    var colorNode = Settings.SpectreColors[spectre];
                    var color = colorNode.Value;
                    var colorVec = new System.Numerics.Vector4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
                    
                    ImGui.PushItemWidth(60);
                    if (ImGui.ColorEdit4($"##color_{spectre}", ref colorVec, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoInputs))
                    {
                        var newColor = new SDXColor((int)(colorVec.X * 255), (int)(colorVec.Y * 255), (int)(colorVec.Z * 255), 255);
                        Settings.SpectreColors[spectre].Value = newColor;
                    }
                    ImGui.PopItemWidth();
                    
                    ImGui.SameLine();
                    ImGui.Text(_spectreDb.TryLookup(spectre, out var dbEntry) ? dbEntry.Name : spectre);
                    if (_renderNameCache.TryGetValue(spectre, out var renderName))
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f), $" ({renderName})");
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##{spectre}")) spectreToDelete = spectre;
                }

                if (spectreToDelete != null)
                {
                    currentList.Remove(spectreToDelete);
                    listChanged = true;
                }

                ImGui.Separator();
                ImGui.InputTextWithHint("##NewSpectreInput", "Metadata/Path/To/Spectre", ref _newSpectreBuffer, 256);
                ImGui.SameLine();

                if (ImGui.Button("Add"))
                {
                    var newSpectre = _newSpectreBuffer.Trim();
                    if (!string.IsNullOrWhiteSpace(newSpectre) && !currentList.Contains(newSpectre, StringComparer.OrdinalIgnoreCase))
                    {
                        currentList.Add(newSpectre);
                        _newSpectreBuffer = "";
                        listChanged = true;
                    }
                }

                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.Separator();

                ImGui.TextColored(titleColor, "Recently Seen Corpses");

                string spectreToAdd = null;

                // Snapshot the recent-corpse queue under the lock, then render ImGui
                // lock-free so the background coroutine isn't stalled by the UI loop.
                List<string> recentCorpses;
                lock (_corpseSetLock)
                {
                    recentCorpses = new List<string>(_recentCorpseQueue);
                }
                recentCorpses.Reverse();

                foreach (var recentSpectre in recentCorpses)
                {
                    ImGui.Text(_spectreDb.TryLookup(recentSpectre, out var dbRecent) ? dbRecent.Name : recentSpectre);
                    if (_renderNameCache.TryGetValue(recentSpectre, out var renderName))
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f), $" ({renderName})");
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"+##{recentSpectre}")) spectreToAdd = recentSpectre;
                }

                if (spectreToAdd != null)
                {
                    if (!currentList.Contains(spectreToAdd, StringComparer.OrdinalIgnoreCase))
                    {
                        currentList.Add(spectreToAdd);
                        listChanged = true;
                    }
                }

                if (listChanged)
                {
                    Settings.SpectreListSource = string.Join(",\n", currentList);
                }
            }
            ImGui.End();
        }
        
        public override void OnUnload()
        {
            // Stop the background coroutine first so it can't race the cleanup below.
            _corpseScanningCoroutine?.Done(true);
            _corpseScanningCoroutine = null;

            lock (_corpseSetLock)
            {
                _recentCorpseQueue.Clear();
                _recentCorpseSet.Clear();
            }

            lock (_cacheLock)
            {
                _displayNameCache.Clear();
                _renderNameCache.Clear();
            }

            lock (_frameCacheLock)
            {
                _filteredEntities.Clear();
            }
        }

        private IEnumerator CorpseScanning()
        {
            // Background loop throttles corpse-tracking work off the render thread.
            // The filtered entity cache itself is refreshed on the render thread
            // (frame-throttled in Draw); here we only maintain the recent-corpse state.
            while (true)
            {
                yield return new WaitTime(Settings.UpdateIntervalMs.Value);

                if (!GameController.InGame || GameController.Area.CurrentArea.IsTown)
                    continue;

                ProcessCorpseScanning();
            }
        }
        
        private void UpdateFrameCache()
        {
            lock (_frameCacheLock)
            {
                _cachedPlayerPos = GameController.Player.Pos;
                var drawDistance = Settings.DrawDistance.Value;
                _cachedDrawDistanceSqr = drawDistance * drawDistance;
                
                _filteredEntities.Clear();

                // Pre-filter entities to reduce iteration count - important for FPS
                var entities = GameController.Entities;
                _filteredEntities.Capacity = entities.Count;

                foreach (var entity in entities)
                {
                    if (IsEntityValidForProcessing(entity))
                    {
                        _filteredEntities.Add(entity);
                    }
                }
            }
        }
        
        private bool IsEntityValidForProcessing(Entity entity)
        {
            var metadata = entity.Metadata;
            return entity.IsDead && 
                   entity.Type == EntityType.Monster &&
                   SDXVector3.DistanceSquared(entity.Pos, _cachedPlayerPos) <= _cachedDrawDistanceSqr &&
                   !string.IsNullOrEmpty(metadata) &&
                   metadata.Contains(MONSTER_METADATA_PATH, StringComparison.OrdinalIgnoreCase);
        }
        
        private void ProcessCorpseScanning()
        {
            // Snapshot the shared filtered cache under the lock, then iterate the
            // coroutine-local scratch buffer lock-free. This avoids an enumeration
            // race with UpdateFrameCache, which mutates _filteredEntities on the
            // render thread.
            lock (_frameCacheLock)
            {
                _scanBuffer.Clear();
                _scanBuffer.AddRange(_filteredEntities);
            }

            foreach (var entity in _scanBuffer)
            {
                var metadata = entity.Metadata;

                lock (_corpseSetLock)
                {
                    if (!_recentCorpseSet.Contains(metadata))
                    {
                        _recentCorpseQueue.Enqueue(metadata);
                        _recentCorpseSet.Add(metadata);

                        // Maintain queue size limit - important for memory management
                        while (_recentCorpseQueue.Count > Settings.MaxRecentCorpses.Value)
                        {
                            if (_recentCorpseQueue.TryDequeue(out var oldestCorpse))
                            {
                                _recentCorpseSet.Remove(oldestCorpse);
                            }
                        }
                    }
                }

                CacheRenderName(metadata, entity.RenderName);
            }
        }
        
        private void CacheRenderName(string metadata, string renderName)
        {
            if (string.IsNullOrEmpty(renderName))
                return;
                
            lock (_cacheLock)
            {
                if (!_renderNameCache.ContainsKey(metadata))
                {
                    EvictOldestCacheSlot(_renderNameCache);
                    _renderNameCache[metadata] = renderName;
                }
            }
        }


        public override void AreaChange(AreaInstance area)
        {
            // Clear all caches when changing areas - important for performance
            lock (_corpseSetLock)
            {
                _recentCorpseQueue.Clear();
                _recentCorpseSet.Clear();
            }
            
            lock (_cacheLock)
            {
                _displayNameCache.Clear();
                _renderNameCache.Clear();
            }
            
            lock (_frameCacheLock)
            {
                _filteredEntities.Clear();
            }
        }

        public override void Render()
        {
            if (Settings.SpectreEditorHotKey.PressedOnce())
            {
                _showSpectreEditor = !_showSpectreEditor;
            }

            if (_showSpectreEditor)
            {
                DrawSpectreEditorWindow();
            }

            if (!GameController.InGame || GameController.Area.CurrentArea.IsTown)
                return;

            Draw();
        }

        private void Draw()
        {
            UpdateSpectreCache();

            // Frame-based update: refresh the filtered entity cache every N frames
            // to reduce per-frame overhead (heavy entity iteration is throttled,
            // not done every frame).
            if (_frameCounter % FRAME_UPDATE_INTERVAL == 0)
            {
                UpdateFrameCache();
            }
            _frameCounter++;

            var camera = GameController.Game.IngameState.Camera;

            // Copy the shared cache into the render-thread-local buffer under the
            // lock, then enumerate lock-free. Reusing _drawBuffer avoids a
            // per-frame allocation on the hot render path.
            lock (_frameCacheLock)
            {
                _drawBuffer.Clear();
                _drawBuffer.AddRange(_filteredEntities);
            }

            foreach (var entity in _drawBuffer)
            {
                if (!entity.IsHostile || !entity.IsTargetable)
                    continue;
                    
                var metadata = entity.Metadata;
                var isKnownSpectre = _cachedValidSpectreBodies.Contains(metadata);
                var shouldDrawLabel = Settings.ShowAllCorpses.Value || isKnownSpectre;

                if (shouldDrawLabel)
                {
                    DrawCorpseLabel(entity, camera);
                    if (Settings.HighlightCorpse.Value)
                    {
                        DrawCorpseHighlight(entity, camera, metadata);
                    }
                }
            }
        }
        
        private void UpdateSpectreCache()
        {
            // Update cache only when settings change - important for performance
            if (_cachedSpectreListSource != Settings.SpectreListSource)
            {
                _cachedSpectreListSource = Settings.SpectreListSource;
                _cachedValidSpectreBodies = new HashSet<string>(
                    ParseSpectreList(_cachedSpectreListSource), StringComparer.OrdinalIgnoreCase);
            }
        }
        
        private void DrawCorpseLabel(Entity entity, Camera camera)
        {
            var textWorldPos = entity.Pos.Translate(0, 0, Settings.TextOffset.Value);
            var textScreenPos = camera.WorldToScreen(textWorldPos);
            
            if (!IsOnScreen(textScreenPos))
                return;
                
            var metadata = entity.Metadata;
            var displayName = GetDisplayName(entity, metadata, Settings.UseRenderNames.Value, Settings.ShowAllCorpses.Value);
            var textColor = GetCustomColor(metadata, Settings.TextColor.Value);
            
            Graphics.DrawTextWithBackground(displayName, new System.Numerics.Vector2(textScreenPos.X, textScreenPos.Y), 
                textColor, null, FontAlign.Center, Settings.BackgroundColor.Value);
        }
        
        private void DrawCorpseHighlight(Entity entity, Camera camera, string metadata)
        {
            var circleWorldPos = entity.Pos.Translate(0, 0, Settings.HighlightZOffset.Value);
            var circleScreenPos = camera.WorldToScreen(circleWorldPos);
            
            if (!IsOnScreen(circleScreenPos))
                return;
                
            var highlightColor = GetCustomColor(metadata, Settings.HighlightColor.Value);
            
            Graphics.DrawCircle(new System.Numerics.Vector2(circleScreenPos.X, circleScreenPos.Y), 
                Settings.HighlightRadius.Value, highlightColor, 2, Settings.HighlightSegments.Value);
        }
        
        private Color GetCustomColor(string metadata, Color defaultColor)
        {
            if (Settings.SpectreColors.TryGetValue(metadata, out var colorNode))
            {
                return colorNode.Value;
            }
            return defaultColor;
        }
        
        private static IEnumerable<string> ParseSpectreList(string source)
            => source.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => s.Trim());

        private void EvictOldestCacheSlot(Dictionary<string, string> cache)
        {
            // Size-capped FIFO: drops the first-inserted entry when full.
            // (Dictionary iteration follows insertion order, not recency.)
            if (cache.Count >= MAX_CACHE_SIZE)
            {
                cache.Remove(cache.Keys.First());
            }
        }

        private static bool IsOnScreen(SDXVector2 screenPos) => screenPos != new SDXVector2();

        private string GetDisplayName(Entity entity, string metadata, bool useRenderNames, bool showAllMode)
        {
            if (showAllMode)
                return metadata;

            // Prefer the bundled database's friendly name when available.
            if (_spectreDb.TryLookup(metadata, out var dbEntry) && !string.IsNullOrEmpty(dbEntry.Name))
                return dbEntry.Name;

            lock (_cacheLock)
            {
                if (_displayNameCache.TryGetValue(metadata, out var cachedName))
                {
                    return cachedName;
                }
            }

            var lastSlashIndex = metadata.LastIndexOf('/');
            var metadataName = lastSlashIndex >= 0 ? metadata.Substring(lastSlashIndex + 1) : metadata;
            var renderName = entity.RenderName;

            var preferredName = useRenderNames ? renderName : metadataName;
            var fallbackName = useRenderNames ? metadataName : renderName;
            
            var finalName = !string.IsNullOrEmpty(preferredName) ? preferredName : fallbackName;
            
            lock (_cacheLock)
            {
                EvictOldestCacheSlot(_displayNameCache);
                _displayNameCache[metadata] = finalName;
            }

            return finalName;
        }
    }
}
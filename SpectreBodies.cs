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
        // Constants
        private const string MONSTER_METADATA_PATH = "/Monsters/";
        private const int MAX_CACHE_SIZE = 1000;
        
        // Thread-safe collections
        private readonly ConcurrentQueue<string> _recentCorpseQueue = new ConcurrentQueue<string>();
        private readonly HashSet<string> _recentCorpseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _corpseSetLock = new object();
        
        // Caches with size limits
        private readonly Dictionary<string, string> _displayNameCache = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _renderNameCache = new Dictionary<string, string>();
        private readonly object _cacheLock = new object();
        
        // UI state
        private string _newSpectreBuffer = "";
        private string _cachedSpectreListSource = "";
        private HashSet<string> _cachedValidSpectreBodies = new HashSet<string>();
        private bool _showSpectreEditor = false;
        private ExileCore.Shared.Coroutine _corpseScanningCoroutine;
        
        // Frame data cache for performance
        private SDXVector3 _cachedPlayerPos;
        private float _cachedDrawDistanceSqr;
        private List<Entity> _cachedFilteredEntities = new List<Entity>();
        private List<Entity> _drawEntities = new List<Entity>();
        private int _lastFrameUpdate = -1;
        private readonly object _frameCacheLock = new object();

        public override bool Initialise()
        {
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

                var currentList = Settings.SpectreListSource
                    .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToList();

                var listChanged = false;

                string spectreToDelete = null;
                foreach (var spectre in currentList)
                {
                    // Color picker - inline implementation
                    if (!Settings.SpectreColors.ContainsKey(spectre))
                    {
                        Settings.SpectreColors[spectre] = new ColorNode(Settings.TextColor.Value);
                    }
                    
                    var colorNode = Settings.SpectreColors[spectre];
                    var color = colorNode.Value;
                    var colorVec = new System.Numerics.Vector4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
                    
                    // Inline color picker
                    ImGui.PushItemWidth(60);
                    if (ImGui.ColorEdit4($"##color_{spectre}", ref colorVec, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoInputs))
                    {
                        // Update the color when changed
                        var newColor = new SDXColor((int)(colorVec.X * 255), (int)(colorVec.Y * 255), (int)(colorVec.Z * 255), 255);
                        Settings.SpectreColors[spectre].Value = newColor;
                    }
                    ImGui.PopItemWidth();
                    
                    ImGui.SameLine();
                    ImGui.Text(spectre);
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
                foreach (var recentSpectre in _recentCorpseQueue.Reverse())
                {
                    ImGui.Text(recentSpectre);
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
            _corpseScanningCoroutine.Done(true);
            _recentCorpseQueue.Clear();
            _recentCorpseSet.Clear();
            _displayNameCache.Clear();
        }

        private IEnumerator CorpseScanning()
        {
            while (true)
            {
                yield return new WaitTime(Settings.UpdateIntervalMs.Value);
                
                if (!GameController.InGame || GameController.Area.CurrentArea.IsTown)
                    continue;

                UpdateFrameCache();
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
                
                _cachedFilteredEntities.Clear();
                
                // Pre-filter entities to reduce iteration count
                var entities = GameController.Entities;
                _cachedFilteredEntities.Capacity = entities.Count;
                
                foreach (var entity in entities)
                {
                    if (IsEntityValidForProcessing(entity))
                    {
                        _cachedFilteredEntities.Add(entity);
                    }
                }
                
                // Create snapshot for drawing
                _drawEntities.Clear();
                _drawEntities.AddRange(_cachedFilteredEntities);
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
            foreach (var entity in _cachedFilteredEntities)
            {
                var metadata = entity.Metadata;
                
                lock (_corpseSetLock)
                {
                    if (!_recentCorpseSet.Contains(metadata))
                    {
                        _recentCorpseQueue.Enqueue(metadata);
                        _recentCorpseSet.Add(metadata);
                        
                        while (_recentCorpseQueue.Count > Settings.MaxRecentCorpses.Value)
                        {
                            if (_recentCorpseQueue.TryDequeue(out var oldestCorpse))
                            {
                                _recentCorpseSet.Remove(oldestCorpse);
                            }
                        }
                    }
                }
                
                // Cache render name outside lock to reduce contention
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
                    // Implement simple LRU eviction
                    if (_renderNameCache.Count >= MAX_CACHE_SIZE)
                    {
                        var oldestKey = _renderNameCache.Keys.First();
                        _renderNameCache.Remove(oldestKey);
                    }
                    _renderNameCache[metadata] = renderName;
                }
            }
        }


        public override void AreaChange(AreaInstance area)
        {
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
                _cachedFilteredEntities.Clear();
                _drawEntities.Clear();
            }
            
            _lastFrameUpdate = -1;
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
            
            // Only update frame cache every 10 frames to reduce overhead
            if (_lastFrameUpdate % 10 == 0)
            {
                UpdateFrameCache();
            }

            var camera = GameController.Game.IngameState.Camera;

            // Use the snapshot created in UpdateFrameCache to avoid modification during enumeration
            foreach (var entity in _drawEntities)
            {
                if (!entity.IsHostile || !entity.IsTargetable)
                    continue;
                    
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
            if (_cachedSpectreListSource != Settings.SpectreListSource)
            {
                _cachedSpectreListSource = Settings.SpectreListSource;
                var spectreListFromSettings = _cachedSpectreListSource
                    .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                _cachedValidSpectreBodies = new HashSet<string>(spectreListFromSettings.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            }
        }
        
        private void DrawCorpseLabel(Entity entity, Camera camera)
        {
            var textWorldPos = entity.Pos.Translate(0, 0, Settings.TextOffset.Value);
            var textScreenPos = camera.WorldToScreen(textWorldPos);
            
            if (textScreenPos == new SDXVector2())
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
            
            if (circleScreenPos == new SDXVector2())
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
        
        private Color GetSpectreColor(string spectre)
        {
            if (Settings.SpectreColors.TryGetValue(spectre, out var colorNode))
            {
                return colorNode.Value;
            }
            return Settings.TextColor.Value;
        }

        private string GetDisplayName(Entity entity, string metadata, bool useRenderNames, bool showAllMode)
        {
            if (showAllMode)
                return metadata;

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
                // Implement LRU eviction if cache is full
                if (_displayNameCache.Count >= MAX_CACHE_SIZE)
                {
                    var oldestKey = _displayNameCache.Keys.First();
                    _displayNameCache.Remove(oldestKey);
                }
                _displayNameCache[metadata] = finalName;
            }

            return finalName;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using SDXVector2 = SharpDX.Vector2;
using SDXVector3 = SharpDX.Vector3;

namespace SpectreBodies
{
    public class SpectreBodies : BaseSettingsPlugin<SpectreBodiesSettings>
    {
        private readonly Queue<string> _recentCorpseQueue = new Queue<string>();
        private readonly HashSet<string> _recentCorpseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _displayNameCache = new Dictionary<string, string>();
        
        private string _newSpectreBuffer = "";
        private string _cachedSpectreListSource = "";
        private HashSet<string> _cachedValidSpectreBodies = new HashSet<string>();

        public override void DrawSettings()
        {
            base.DrawSettings();
            
            ImGui.Separator();
            
            var titleColor = new Vector4(1.0f, 0.84f, 0.0f, 1.0f);
            ImGui.TextColored(titleColor, "Spectre Body List Editor");

            var currentList = Settings.SpectreListSource
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            var listChanged = false;
            
            string spectreToDelete = null;
            foreach (var spectre in currentList)
            {
                ImGui.Text(spectre);
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
        
        public override void OnUnload()
        {
            _recentCorpseQueue.Clear();
            _recentCorpseSet.Clear();
            _displayNameCache.Clear();
        }

        public override void AreaChange(AreaInstance area)
        {
            _recentCorpseQueue.Clear();
            _recentCorpseSet.Clear();
            _displayNameCache.Clear();
        }

        public override void Render()
        {
            if (!GameController.InGame || GameController.Area.CurrentArea.IsTown)
                return;

            if (_cachedSpectreListSource != Settings.SpectreListSource)
            {
                _cachedSpectreListSource = Settings.SpectreListSource;
                var spectreListFromSettings = _cachedSpectreListSource
                    .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                _cachedValidSpectreBodies = new HashSet<string>(spectreListFromSettings.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            }

            var textColor = Settings.TextColor.Value;
            var backgroundColor = Settings.BackgroundColor.Value;
            var textZOffset = Settings.TextOffset.Value;
            var useRenderNames = Settings.UseRenderNames.Value;
            var drawDistance = Settings.DrawDistance.Value;
            var drawDistanceSqr = drawDistance * drawDistance;
            
            var playerPos = GameController.Player.Pos;
            var camera = GameController.Game.IngameState.Camera;

            foreach (var entity in GameController.Entities)
            {
                if (!entity.IsDead || entity.Type != EntityType.Monster)
                    continue;

                if (SDXVector3.DistanceSquared(entity.Pos, playerPos) > drawDistanceSqr)
                    continue;

                if (!entity.IsHostile || !entity.IsTargetable)
                    continue;
                
                var metadata = entity.Metadata;
                if (string.IsNullOrEmpty(metadata) || !metadata.Contains("/Monsters/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var isKnownSpectre = _cachedValidSpectreBodies.Contains(metadata);

                if (!isKnownSpectre)
                {
                    if (!_recentCorpseSet.Contains(metadata))
                    {
                        _recentCorpseQueue.Enqueue(metadata);
                        _recentCorpseSet.Add(metadata);

                        if (_recentCorpseQueue.Count > Settings.MaxRecentCorpses.Value)
                        {
                            var oldestCorpse = _recentCorpseQueue.Dequeue();
                            _recentCorpseSet.Remove(oldestCorpse);
                        }
                    }
                }
                
                var shouldDrawLabel = Settings.ShowAllCorpses.Value || isKnownSpectre;

                if (shouldDrawLabel)
                {
                    var textWorldPos = entity.Pos.Translate(0, 0, textZOffset);
                    var textScreenPos = camera.WorldToScreen(textWorldPos);
                    
                    if (textScreenPos != new SDXVector2())
                    {
                        var displayName = GetDisplayName(entity, metadata, useRenderNames, Settings.ShowAllCorpses.Value);
                        Graphics.DrawTextWithBackground(displayName, new Vector2(textScreenPos.X, textScreenPos.Y), textColor, null, FontAlign.Center, backgroundColor);
                    }
                    
                    if (Settings.HighlightCorpse.Value)
                    {
                        var circleWorldPos = entity.Pos.Translate(0, 0, Settings.HighlightZOffset.Value);
                        var circleScreenPos = camera.WorldToScreen(circleWorldPos);
                        if (circleScreenPos != new SDXVector2())
                        {
                            Graphics.DrawCircle(new Vector2(circleScreenPos.X, circleScreenPos.Y), Settings.HighlightRadius.Value, Settings.HighlightColor.Value, 2, Settings.HighlightSegments.Value);
                        }
                    }
                }
            }
        }

        private string GetDisplayName(Entity entity, string metadata, bool useRenderNames, bool showAllMode)
        {
            if (showAllMode)
                return metadata;

            if (_displayNameCache.TryGetValue(metadata, out var cachedName))
            {
                return cachedName;
            }

            var metadataName = metadata.Substring(metadata.LastIndexOf('/') + 1);
            var renderName = entity.RenderName;

            var preferredName = useRenderNames ? renderName : metadataName;
            var fallbackName = useRenderNames ? metadataName : renderName;
            
            var finalName = !string.IsNullOrEmpty(preferredName) ? preferredName : fallbackName;
            
            _displayNameCache.Add(metadata, finalName);

            return finalName;
        }
    }
}
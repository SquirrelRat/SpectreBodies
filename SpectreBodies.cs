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
        private const int MaxRecentCorpses = 5;
        private readonly Dictionary<long, Monster> _nearbyMonsters = new Dictionary<long, Monster>();
        private readonly List<string> _recentlySeenCorpses = new List<string>();
        
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
            for (var i = _recentlySeenCorpses.Count - 1; i >= 0; i--)
            {
                var recentSpectre = _recentlySeenCorpses[i];
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
                _recentlySeenCorpses.Remove(spectreToAdd);
            }
            
            if (listChanged)
            {
                Settings.SpectreListSource = string.Join(",\n", currentList);
            }
        }
        
        public override void EntityAdded(Entity entity)
        {
            if (entity.Type == EntityType.Monster)
            {
                var monster = entity.AsObject<Monster>();
                if (monster != null && monster.Address != 0x0 && !_nearbyMonsters.ContainsKey(monster.Address))
                    _nearbyMonsters.Add(monster.Address, monster);
            }
        }

        public override void EntityRemoved(Entity entity)
        {
            if (entity.Type == EntityType.Monster)
            {
                var monster = entity.AsObject<Monster>();
                if (monster != null)
                    _nearbyMonsters.Remove(monster.Address);
            }
        }

        public override void AreaChange(AreaInstance area)
        {
            base.AreaChange(area);
            _nearbyMonsters.Clear();
            _recentlySeenCorpses.Clear();
        }

        public override void Render()
        {
            base.Render();

            if (!GameController.InGame || GameController.Area.CurrentArea.IsTown || _nearbyMonsters.Count == 0)
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

            foreach (var monster in _nearbyMonsters.Values)
            {
                var entity = monster?.AsObject<Entity>();

                if (entity == null || !entity.IsValid || string.IsNullOrEmpty(entity.Metadata))
                    continue;

                if (!entity.IsDead || !entity.IsHostile || !entity.IsTargetable)
                    continue;
                
                var metadata = entity.Metadata;
                if (!_cachedValidSpectreBodies.Contains(metadata) && !_recentlySeenCorpses.Contains(metadata))
                {
                    _recentlySeenCorpses.Add(metadata);
                    if (_recentlySeenCorpses.Count > MaxRecentCorpses)
                    {
                        _recentlySeenCorpses.RemoveAt(0);
                    }
                }
                
                if (SDXVector3.Distance(entity.Pos, GameController.Player.Pos) > drawDistance)
                    continue;

                var shouldDrawLabel = Settings.ShowAllCorpses.Value || IsSpectreBody(metadata, _cachedValidSpectreBodies);

                if (shouldDrawLabel)
                {
                    var camera = GameController.Game.IngameState.Camera;
                    
                    var textWorldPos = entity.Pos.Translate(0, 0, textZOffset);
                    var textScreenPos = camera.WorldToScreen(textWorldPos);
                    
                    if (textScreenPos != new SDXVector2())
                    {
                        var displayName = GetDisplayName(entity, useRenderNames, Settings.ShowAllCorpses.Value);
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

        private string GetDisplayName(Entity entity, bool useRenderNames, bool showAllMode)
        {
            if (showAllMode)
                return entity.Metadata;

            string GetMetadataDisplayName() =>
                entity.Metadata.Substring(entity.Metadata.LastIndexOf('/') + 1);

            if (entity == null) return string.Empty;

            var preferredName = useRenderNames ? entity.RenderName : GetMetadataDisplayName();
            if (!string.IsNullOrEmpty(preferredName))
                return preferredName;

            return useRenderNames ? GetMetadataDisplayName() : entity.RenderName;
        }

        private bool IsSpectreBody(string metaData, IReadOnlySet<string> validSpectreBodies)
        {
            if (string.IsNullOrWhiteSpace(metaData) || validSpectreBodies.Count == 0)
                return false;

            return validSpectreBodies.Contains(metaData);
        }
    }
}
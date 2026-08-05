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
        private string _librarySearch = "";
        private int _libRoleFilter;      // 0 = All, 1 = Damage, 2 = Utility
        private int _libStatusFilter;    // 0 = All, 1 = Confirmed, 2 = Untested
        private bool _resetConfirmArmed;
        private ExileCore.Shared.Coroutine _corpseScanningCoroutine;

        // Frame data cache for performance - important for FPS.
        // _filteredEntities is the shared cache (written under _frameCacheLock);
        // _drawBuffer is render-thread-local, _scanBuffer is coroutine-local scratch.
        private System.Numerics.Vector3 _cachedPlayerPos;
        private float _cachedDrawDistanceSqr;
        private List<Entity> _filteredEntities = new List<Entity>();
        private List<Entity> _drawBuffer = new List<Entity>();
        private List<Entity> _scanBuffer = new List<Entity>();
        private int _frameCounter;
        private readonly object _frameCacheLock = new object();

        // Metadata of the player's currently-summoned minions (alive + non-hostile),
        // refreshed each frame-cache update. Used to mark wishlist spectres as "raised".
        private readonly HashSet<string> _raisedMinions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                if (ImGui.BeginTabBar("##SpectreBodiesTabs"))
                {
                    if (ImGui.BeginTabItem("My Spectres"))
                    {
                        DrawMySpectresTab();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Library"))
                    {
                        DrawLibraryTab();
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
        }

        private void DrawMySpectresTab()
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
                    ImGui.Text(Esc(_spectreDb.TryLookup(spectre, out var dbEntry) ? dbEntry.Name : spectre));
                    if (_raisedMinions.Contains(spectre))
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new System.Numerics.Vector4(0.4f, 1.0f, 0.4f, 1.0f), " raised");
                    }
                    var renderName = GetRenderName(spectre);
                    if (renderName != null)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f), Esc($" ({renderName})"));
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##{spectre}")) spectreToDelete = spectre;
                }

                if (spectreToDelete != null)
                {
                    currentList.Remove(spectreToDelete);
                    Settings.SpectreColors.Remove(spectreToDelete);
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
                    ImGui.Text(Esc(_spectreDb.TryLookup(recentSpectre, out var dbRecent) ? dbRecent.Name : recentSpectre));
                    var renderName = GetRenderName(recentSpectre);
                    if (renderName != null)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f), Esc($" ({renderName})"));
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
        
        private void DrawLibraryTab()
        {
            var titleColor = new System.Numerics.Vector4(1.0f, 0.84f, 0.0f, 1.0f);
            ImGui.TextColored(titleColor, "Spectre Library");
            ImGui.SameLine();
            ImGui.TextDisabled($"({_spectreDb.All.Count} entries)");

            if (!_spectreDb.IsLoaded)
            {
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.3f, 0.3f, 1.0f),
                    "Database failed to load (spectre-data.json missing or invalid).");
                return;
            }

            // Snapshot the current wishlist for membership checks this frame.
            var wishlist = new HashSet<string>(
                ParseSpectreList(Settings.SpectreListSource), StringComparer.OrdinalIgnoreCase);

            ImGui.SetNextItemWidth(180);
            ImGui.InputTextWithHint("##LibrarySearch", "Search name or tag...", ref _librarySearch, 128);

            ImGui.TextUnformatted("Role:");
            ImGui.SameLine();
            ImGui.RadioButton("All##rf", ref _libRoleFilter, 0); ImGui.SameLine();
            ImGui.RadioButton("Damage##rf", ref _libRoleFilter, 1); ImGui.SameLine();
            ImGui.RadioButton("Utility##rf", ref _libRoleFilter, 2);

            ImGui.TextUnformatted("Status:");
            ImGui.SameLine();
            ImGui.RadioButton("All##sf", ref _libStatusFilter, 0); ImGui.SameLine();
            ImGui.RadioButton("Confirmed##sf", ref _libStatusFilter, 1); ImGui.SameLine();
            ImGui.RadioButton("Untested##sf", ref _libStatusFilter, 2);

            ImGui.Separator();

            // Reset-to-library action with a two-stage confirm (a stray click can't wipe the list).
            if (_resetConfirmArmed)
            {
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.6f, 0.2f, 1.0f),
                    "Replace your current list with all Confirmed spectres?");
                ImGui.SameLine();
                if (ImGui.Button("Confirm##reset")) { ResetWishlistToLibrary(); _resetConfirmArmed = false; }
                ImGui.SameLine();
                if (ImGui.Button("Cancel##reset")) _resetConfirmArmed = false;
            }
            else
            {
                if (ImGui.Button("Reset wishlist to all Confirmed spectres"))
                    _resetConfirmArmed = true;
            }

            ImGui.Separator();

            string roleFilter = _libRoleFilter switch { 1 => "Damage", 2 => "Utility", _ => null };
            string statusFilter = _libStatusFilter switch { 1 => "Confirmed", 2 => "Untested", _ => null };
            var search = (_librarySearch ?? "").Trim();

            foreach (var entry in _spectreDb.All)
            {
                if (roleFilter != null && !string.Equals(entry.Role, roleFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (statusFilter != null && !string.Equals(entry.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (search.Length > 0
                    && entry.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && (entry.Tags == null || entry.Tags.All(t => t.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0))
                    && entry.Metadata.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DrawLibraryEntry(entry, wishlist);
                ImGui.Spacing();
            }
        }

        private void DrawLibraryEntry(SpectreEntry e, HashSet<string> wishlist)
        {
            bool untested = string.Equals(e.Status, "Untested", StringComparison.OrdinalIgnoreCase);
            var nameColor = untested
                ? new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1.0f)
                : string.Equals(e.Role, "Damage", StringComparison.OrdinalIgnoreCase)
                    ? new System.Numerics.Vector4(1.0f, 0.55f, 0.15f, 1.0f)
                    : new System.Numerics.Vector4(0.35f, 0.65f, 1.0f, 1.0f);

            ImGui.TextColored(nameColor, Esc(e.Name));
            ImGui.SameLine();
            ImGui.TextColored(TierColor(e.Tier), Esc($"[{(string.IsNullOrEmpty(e.Tier) ? "?" : e.Tier)}]"));
            ImGui.SameLine();
            ImGui.TextDisabled(Esc($"({e.Role}{(untested ? ", Untested" : "")})"));

            ImGui.SameLine();
            if (wishlist.Contains(e.Metadata))
            {
                ImGui.TextColored(new System.Numerics.Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Added");
            }
            else if (ImGui.Button($"+ Add##{e.Metadata}"))
            {
                AddToWishlist(e.Metadata);
                wishlist.Add(e.Metadata);
            }
            if (_raisedMinions.Contains(e.Metadata))
            {
                ImGui.SameLine();
                ImGui.TextColored(new System.Numerics.Vector4(0.4f, 1.0f, 0.4f, 1.0f), "raised");
            }

            if (e.Tags != null && e.Tags.Count > 0)
                ImGui.TextDisabled(Esc("Tags: " + string.Join(", ", e.Tags)));

            if (!string.IsNullOrEmpty(e.Acquisition))
                ImGui.TextUnformatted($"Location: {e.Acquisition}");

            if (!string.IsNullOrEmpty(e.Note))
                TextWrappedSafe(e.Note);
            if (!string.IsNullOrEmpty(e.AcquisitionNote))
                TextWrappedSafe(e.AcquisitionNote);
        }

        // ImGui Text*() helpers treat their string as a printf format, so a stray '%'
        // (e.g. "+20% action speed") gets parsed as a format specifier and prints garbage.
        // Escape '%' for the format-based overloads, and use TextUnformatted for wrapped text.
        private static string Esc(string s) => (s ?? "").Replace("%", "%%");

        private static void TextWrappedSafe(string text)
        {
            ImGui.PushTextWrapPos(0.0f);
            ImGui.TextUnformatted(text ?? "");
            ImGui.PopTextWrapPos();
        }

        private void AddToWishlist(string metadata)
        {
            var list = ParseSpectreList(Settings.SpectreListSource).ToList();
            if (!list.Contains(metadata, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(metadata);
                Settings.SpectreListSource = string.Join(",\n", list);
            }
        }

        private void ResetWishlistToLibrary()
        {
            var confirmed = _spectreDb.All
                .Where(e => string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Metadata)
                .ToList();
            Settings.SpectreListSource = string.Join(",\n", confirmed);
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

                // Respect the plugin's Enable toggle: the framework gates Render/Tick on
                // it, but coroutines keep running on the ParallelRunner unless checked here.
                if (!Settings.Enable.Value)
                    continue;

                if (!GameController.InGame || GameController.Area.CurrentArea.IsTown)
                    continue;

                ProcessCorpseScanning();
            }
        }
        
        private void UpdateFrameCache()
        {
            lock (_frameCacheLock)
            {
                // Defensive: Player can be null transiently during area transitions
                // even when InGame is true. Skip this refresh rather than throw; the
                // next frame-based refresh retries shortly.
                var player = GameController.Player;
                if (player == null)
                    return;

                _cachedPlayerPos = player.PosNum;
                var drawDistance = Settings.DrawDistance.Value;
                _cachedDrawDistanceSqr = drawDistance * drawDistance;
                
                _filteredEntities.Clear();

                // Pre-filter entities to reduce iteration count - important for FPS
                var entities = GameController.Entities;
                _filteredEntities.Capacity = entities.Count;

                _raisedMinions.Clear();

                foreach (var entity in entities)
                {
                    if (IsEntityValidForProcessing(entity))
                    {
                        _filteredEntities.Add(entity);
                    }

                    // Track the player's summoned minions (alive + non-hostile) so the
                    // editor can mark wishlist spectres the player already has raised.
                    if (entity.IsAlive && entity.Type == EntityType.Monster && !entity.IsHostile)
                    {
                        _raisedMinions.Add(entity.Metadata);
                    }
                }
            }
        }
        
        private bool IsEntityValidForProcessing(Entity entity)
        {
            var metadata = entity.Metadata;
            return entity.IsDead && 
                   entity.Type == EntityType.Monster &&
                   System.Numerics.Vector3.DistanceSquared(entity.PosNum, _cachedPlayerPos) <= _cachedDrawDistanceSqr &&
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

        // Locked read of the render-name cache. The cache is written by the background
        // coroutine (CacheRenderName), so UI/rendering threads must read it under the
        // same lock instead of touching the Dictionary directly.
        private string GetRenderName(string metadata)
        {
            if (string.IsNullOrEmpty(metadata))
                return null;

            lock (_cacheLock)
            {
                return _renderNameCache.TryGetValue(metadata, out var renderName) ? renderName : null;
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
            var textWorldPos = MathHepler.Translate(entity.PosNum, 0, 0, Settings.TextOffset.Value);
            var textScreenPos = camera.WorldToScreen(textWorldPos);
            
            if (!IsOnScreen(textScreenPos))
                return;
                
            var metadata = entity.Metadata;
            var displayName = GetDisplayName(entity, metadata, Settings.UseRenderNames.Value, Settings.ShowAllCorpses.Value);
            var textColor = GetCustomColor(metadata, Settings.TextColor.Value);

            // Per the README, the in-game name is shown in green parentheses when it
            // differs from the displayed name (e.g. metadata path vs. actual monster name).
            var renderName = GetRenderName(metadata) ?? entity.RenderName;
            var label = displayName;
            string renderSuffix = null;
            if (!string.IsNullOrEmpty(renderName) && !string.Equals(renderName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                renderSuffix = $" ({renderName})";
                label = displayName + renderSuffix;
            }

            var labelPos = new System.Numerics.Vector2(textScreenPos.X, textScreenPos.Y);
            Graphics.DrawTextWithBackground(label, labelPos, textColor, null, FontAlign.Center, Settings.BackgroundColor.Value);

            // Overlay the suffix in green at the exact position the combined label put it,
            // so the rendered width and background stay aligned.
            if (renderSuffix != null)
            {
                var fullSize = Graphics.MeasureText(label);
                var baseSize = Graphics.MeasureText(displayName);
                var leftX = textScreenPos.X - fullSize.X / 2.0f;
                Graphics.DrawText(renderSuffix, new System.Numerics.Vector2(leftX + baseSize.X, textScreenPos.Y),
                    new SDXColor(0, 255, 0, 255));
            }
        }
        
        private void DrawCorpseHighlight(Entity entity, Camera camera, string metadata)
        {
            var circleWorldPos = MathHepler.Translate(entity.PosNum, 0, 0, Settings.HighlightZOffset.Value);
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

        private static System.Numerics.Vector4 TierColor(string tier) => tier switch
        {
            "S" => new System.Numerics.Vector4(1.0f, 0.84f, 0.0f, 1.0f),   // gold
            "A" => new System.Numerics.Vector4(0.4f, 0.85f, 0.4f, 1.0f),   // green
            "B" => new System.Numerics.Vector4(0.5f, 0.7f, 1.0f, 1.0f),    // blue
            _   => new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1.0f)     // grey (?/unknown)
        };

        private static bool IsOnScreen(System.Numerics.Vector2 screenPos) => screenPos != new System.Numerics.Vector2();

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
            var renderName = GetRenderName(metadata) ?? entity.RenderName;

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
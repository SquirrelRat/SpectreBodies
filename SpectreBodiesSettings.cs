using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SpectreBodies
{
    public class SpectreBodiesSettings : ISettings
    {
        public SpectreBodiesSettings()
        {
            Enable = new ToggleNode(true);
            SpectreEditorHotKey = new HotkeyNode(Keys.F6);
            ShowAllCorpses = new ToggleNode(false);
            UseRenderNames = new ToggleNode(true);
            
            SpectreListSource =
                "Metadata/Monsters/KaomWarrior/KaomWarrior7,\n" +
                "Metadata/Monsters/WickerMan/WickerMan,\n" +
                "Metadata/Monsters/Miner/MinerLantern";

            MaxRecentCorpses = new RangeNode<int>(10, 5, 15);

            HighlightCorpse = new ToggleNode(true);
            HighlightColor = new ColorNode(new Color(255, 255, 0, 150));
            HighlightRadius = new RangeNode<int>(12, 5, 50);
            HighlightSegments = new RangeNode<int>(12, 3, 40);
            HighlightZOffset = new RangeNode<int>(0, -50, 50);

            TextColor = new ColorNode(new Color(255, 255, 255, 255));
            BackgroundColor = new ColorNode(new Color(0, 0, 0, 255));
            TextOffset = new RangeNode<int>(20, -360, 360);
            DrawDistance = new RangeNode<int>(1050, 0, 2000);
            UpdateIntervalMs = new RangeNode<int>(250, 100, 1000);
        }
        
        public ToggleNode Enable { get; set; }
        public HotkeyNode SpectreEditorHotKey { get; set; }
        
        [Menu("Show All Nearby Corpses")]
        public ToggleNode ShowAllCorpses { get; set; }

        [Menu("Use Render Names")]
        public ToggleNode UseRenderNames { get; set; }

        public string SpectreListSource { get; set; }

        [Menu("Recent List Size", "Number of corpses to remember in the 'Recently Seen' list.")]
        public RangeNode<int> MaxRecentCorpses { get; set; }
        
        [Menu("Highlight Corpse")]
        public ToggleNode HighlightCorpse { get; set; }

        [Menu("Highlight Color")]
        public ColorNode HighlightColor { get; set; }

        [Menu("Highlight Radius")]
        public RangeNode<int> HighlightRadius { get; set; }

        [Menu("Highlight Segments")]
        public RangeNode<int> HighlightSegments { get; set; }
        
        [Menu("Highlight Z-Offset")]
        public RangeNode<int> HighlightZOffset { get; set; }

        [Menu("Text Color")] 
        public ColorNode TextColor { get; set; }

        [Menu("Background Color")] 
        public ColorNode BackgroundColor { get; set; }

        [Menu("Text Z-Offset")] 
        public RangeNode<int> TextOffset { get; set; }

        [Menu("Draw Distance")] 
        public RangeNode<int> DrawDistance { get; set; }

        [Menu("Update Interval (ms)", "The time in milliseconds between corpse scans.")]
        public RangeNode<int> UpdateIntervalMs { get; set; }

        [Menu("Spectre Colors", "Custom colors for individual spectre corpses.")]
        public Dictionary<string, ColorNode> SpectreColors { get; set; } = new Dictionary<string, ColorNode>();
    }
}
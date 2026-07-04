#region Using declarations
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenBiasV0003 : Indicator
    {
        #region Variables
        private List<int>       pivotBars;
        private List<double>    pivotPrices;
        private List<bool>      pivotIsHigh;
        private HashSet<string> previousLineTags = new HashSet<string>();
        private const string    tagPrefix       = "ZZ_line_";
        private const string    horizLinePrefix = "ZZ_Horz_";
        private string          instanceId;
        private int             previousPivotCount = 0;
        #endregion

        #region Properties

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Bars To Process (0 = all)", GroupName = "Parameters", Order = 0)]
        public int BarsToProcess { get; set; } = 200;

        [Display(Name = "Draw ZigZags", GroupName = "Parameters", Order = 1)]
        public bool DrawZigZags { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1,100)]
        [Display(Name = "Number of Levels", GroupName = "Parameters", Order = 2)]
        public int NumberOfLevels { get; set; } = 6;

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Relevance Factor (Bars)", GroupName = "Parameters", Order = 3)]
        public int RelevanceFactor { get; set; } = 300;

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "ZigZag Color", GroupName = "Parameters", Order = 4)]
        public Brush ZigZagColor { get; set; } = Brushes.Yellow;
        [Browsable(false)]
        public string ZigZagColorSerialize
        {
            get => Serialize.BrushToString(ZigZagColor);
            set => ZigZagColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Level Line Color (Neutral)", GroupName = "Color Settings", Order = 4)]
        public Brush LevelLineColor { get; set; } = Brushes.DodgerBlue;
        [Browsable(false)]
        public string LevelLineColorSerialize
        {
            get => Serialize.BrushToString(LevelLineColor);
            set => LevelLineColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Level Line Color (Gained)", GroupName = "Color Settings", Order = 5)]
        public Brush LevelLineColorGained { get; set; } = Brushes.DarkGreen;
        [Browsable(false)]
        public string LevelLineColorGainedSerialize
        {
            get => Serialize.BrushToString(LevelLineColorGained);
            set => LevelLineColorGained = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Level Line Color (Lost)", GroupName = "Color Settings", Order = 6)]
        public Brush LevelLineColorLost { get; set; } = Brushes.Crimson;
        [Browsable(false)]
        public string LevelLineColorLostSerialize
        {
            get => Serialize.BrushToString(LevelLineColorLost);
            set => LevelLineColorLost = Serialize.StringToBrush(value);
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Level Line Width", GroupName = "Color Settings", Order = 7)]
        public int LevelLineWidth { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Level Line Dash Style", GroupName = "Color Settings", Order = 8)]
        public DashStyleHelper LevelLineDashStyle { get; set; } = DashStyleHelper.Solid;

        // --- New Properties for V0003 ---

        [NinjaScriptProperty]
        [Display(Name = "Show Containment Box", GroupName = "New Settings (V0003)", Order = 9)]
        public bool ShowContainmentBox { get; set; } = true;

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Containment Box Color", GroupName = "New Settings (V0003)", Order = 10)]
        public Brush ContainmentBoxColor { get; set; } = Brushes.DarkSlateGray;
        [Browsable(false)]
        public string ContainmentBoxColorSerialize
        {
            get => Serialize.BrushToString(ContainmentBoxColor);
            set => ContainmentBoxColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Gain Text Color", GroupName = "New Settings (V0003)", Order = 11)]
        public Brush GainColor { get; set; } = Brushes.LimeGreen;
        [Browsable(false)]
        public string GainColorSerialize
        {
            get => Serialize.BrushToString(GainColor);
            set => GainColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Loss Text Color", GroupName = "New Settings (V0003)", Order = 12)]
        public Brush LossColor { get; set; } = Brushes.Red;
        [Browsable(false)]
        public string LossColorSerialize
        {
            get => Serialize.BrushToString(LossColor);
            set => LossColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Failed Gain (FTG) Color", GroupName = "New Settings (V0003)", Order = 13)]
        public Brush FailedGainColor { get; set; } = Brushes.Orange;
        [Browsable(false)]
        public string FailedGainColorSerialize
        {
            get => Serialize.BrushToString(FailedGainColor);
            set => FailedGainColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Failed Loss (FTL) Color", GroupName = "New Settings (V0003)", Order = 14)]
        public Brush FailedLossColor { get; set; } = Brushes.DodgerBlue;
        [Browsable(false)]
        public string FailedLossColorSerialize
        {
            get => Serialize.BrushToString(FailedLossColor);
            set => FailedLossColor = Serialize.StringToBrush(value);
        }

        [NinjaScriptProperty]
        [Display(Name = "Show Gain/Loss Markers", GroupName = "New Settings (V0003)", Order = 15)]
        public bool ShowGainLoss { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Show FTG/FTL Markers", GroupName = "New Settings (V0003)", Order = 16)]
        public bool ShowFTGFTL { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Enable Debug Output", GroupName = "Debugging", Order = 100)]
        public bool DebugPrints { get; set; } = false;

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Bias { get { return Values[0]; } }
        #endregion 

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description               = "AlightenBiasV0003 with dynamic multi-level FTG/FTL evaluation";
                Name                      = "AlightenBiasV0003";
                Calculate                 = Calculate.OnBarClose;
                IsOverlay                 = true;
                DrawOnPricePanel          = true;
                DisplayInDataBox          = true;
                ShowTransparentPlotsInDataBox = true;
                PaintPriceMarkers         = true;
                ScaleJustification        = ScaleJustification.Right;
                IsSuspendedWhileInactive  = true;
                MaximumBarsLookBack       = MaximumBarsLookBack.Infinite;
                AddPlot(Brushes.Transparent, "Bias");
            }
            else if (State == State.Configure)
            {
                instanceId = $"BiasV3_{Instrument.FullName}_{BarsPeriod.BarsPeriodType}_{BarsPeriod.Value}_{Guid.NewGuid()}";
            }
            else if (State == State.DataLoaded)
            {
                pivotBars    = new List<int>();
                pivotPrices  = new List<double>();
                pivotIsHigh  = new List<bool>();
                previousLineTags.Clear();
                previousPivotCount = 0;
            }
            else if (State == State.Terminated)
            {
                foreach (var tag in previousLineTags)
                    RemoveDrawObject(tag);
                previousLineTags.Clear();
            }
        }
        #endregion

        #region OnBarUpdate
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2) return;

            if (BarsToProcess > 0)
            {
                int cutoffBar = Count - BarsToProcess;
                if (CurrentBar < cutoffBar)
                    return;
            }

            bool isHigh    = High[0] > High[1];
            bool isLow     = Low[0]  < Low[1];
            bool prevGreen = Close[1] >= Open[1];
            bool prevRed   = Close[1] < Open[1];
            bool currGreen = Close[0] >= Open[0];
            bool currRed   = Close[0] < Open[0];

            if (prevGreen && currGreen && isHigh)
                ProcessPivot(CurrentBar, High[0], true);

            if (prevRed && currRed && isLow)
                ProcessPivot(CurrentBar, Low[0], false);

            if (prevGreen && currRed && isHigh)
                ProcessPivot(CurrentBar, High[0], true);

            if (prevRed && currGreen && isLow)
                ProcessPivot(CurrentBar, Low[0], false);

            if (prevRed && currGreen && isHigh)
                ProcessPivot(CurrentBar, High[0], true);

            if (prevGreen && currRed && isLow)
                ProcessPivot(CurrentBar, Low[0], false);

            // --- Bias State Machine ---
            Value[0] = CurrentBar == 0 ? 0 : Value[1];

            if (pivotBars.Count > 1) // Must have at least one confirmed pivot
            {
                double price = Close[0];
                var confirmedPivotsWithDistance = pivotBars
                    .Take(pivotBars.Count - 1) // Exclude developing pivot
                    .Select((b, idx) => new
                    {
                        BarIndex  = b,
                        ListIndex = idx,
                        Level     = pivotIsHigh[idx]
                                     ? Math.Max(Open[CurrentBar - b], Close[CurrentBar - b])
                                     : Math.Min(Open[CurrentBar - b], Close[CurrentBar - b]),
                        IsHigh    = pivotIsHigh[idx]
                    });

                var visibleConfirmedPivots = confirmedPivotsWithDistance
                    .Where(x => (CurrentBar - x.BarIndex) <= RelevanceFactor)
                    .Where(x => x.Level != price)
                    .OrderByDescending(x => x.ListIndex)
                    .Take(NumberOfLevels)
                    .ToList();

                int currentBias = (int)Value[0];

                // 1. Check if any pivots were JUST confirmed on this bar (evaluate historical events first)
                if (pivotBars.Count > previousPivotCount && previousPivotCount > 0)
                {
                    for (int idx = previousPivotCount - 1; idx <= pivotBars.Count - 2; idx++)
                    {
                        var newlyConfirmedPivot = visibleConfirmedPivots.FirstOrDefault(x => x.ListIndex == idx);
                        if (newlyConfirmedPivot != null)
                        {
                            int lastEventState = 0;
                            
                            // Scan from creation down to 1 (CurrentBar is handled below)
                            for (int barsAgo = CurrentBar - newlyConfirmedPivot.BarIndex - 1; barsAgo >= 1; barsAgo--)
                            {
                                double h_open = Open[barsAgo];
                                double h_close = Close[barsAgo];
                                double h_prevClose = Close[barsAgo + 1];
                                double h_high = High[barsAgo];
                                double h_low = Low[barsAgo];

                                bool h_bull = false;
                                bool h_bear = false;

                                if (Math.Min(h_open, h_prevClose) < newlyConfirmedPivot.Level && h_close > newlyConfirmedPivot.Level)
                                    h_bull = true;
                                else if (Math.Max(h_open, h_prevClose) > newlyConfirmedPivot.Level && h_close < newlyConfirmedPivot.Level)
                                    h_bear = true;

                                if (newlyConfirmedPivot.Level > h_open)
                                {
                                    if (h_high > newlyConfirmedPivot.Level && h_close < newlyConfirmedPivot.Level) h_bear = true;
                                }
                                else if (newlyConfirmedPivot.Level < h_open)
                                {
                                    if (h_low < newlyConfirmedPivot.Level && h_close > newlyConfirmedPivot.Level) h_bull = true;
                                }
                                else
                                {
                                    if (h_high > newlyConfirmedPivot.Level && h_close < newlyConfirmedPivot.Level) h_bear = true;
                                    if (h_low < newlyConfirmedPivot.Level && h_close > newlyConfirmedPivot.Level) h_bull = true;
                                }

                                if (h_bull && h_bear) lastEventState = 2;
                                else if (h_bull) lastEventState = 1;
                                else if (h_bear) lastEventState = -1;
                            }

                            if (lastEventState != 0)
                                currentBias = lastEventState;
                        }
                    }
                }

                // 2. Check for events on CurrentBar across all visible confirmed pivots (evaluate live edge last)
                bool isGainOnCurrentBar = false;
                bool isLossOnCurrentBar = false;
                bool isFTGOnCurrentBar = false;
                bool isFTLOnCurrentBar = false;
                
                bool hasBullishEvent = false;
                bool hasBearishEvent = false;

                DPrint($"--- Bar Update: {Time[0]:yyyy-MM-dd HH:mm:ss} | O: {Open[0]} H: {High[0]} L: {Low[0]} C: {Close[0]} PrevC: {Close[1]} ---");

                foreach (var p in visibleConfirmedPivots)
                {
                    double open = Open[0];
                    double close = Close[0];
                    double prevClose = Close[1];

                    DPrint($"  Evaluating Confirmed Pivot at Level: {p.Level} (Created {CurrentBar - p.BarIndex} bars ago)");

                    // Gain/Loss check evaluates against all visible confirmed pivots
                    bool isGain = Math.Min(open, prevClose) < p.Level && close > p.Level;
                    bool isLoss = Math.Max(open, prevClose) > p.Level && close < p.Level;

                    if (isGain)
                    {
                        isGainOnCurrentBar = true;
                        hasBullishEvent = true;
                        DPrint($"    -> GAIN detected for level {p.Level}");
                    }
                    else if (isLoss)
                    {
                        isLossOnCurrentBar = true;
                        hasBearishEvent = true;
                        DPrint($"    -> LOSS detected for level {p.Level}");
                    }

                    // FTG: Started below or at the level, wicked above it, and closed below it.
                    // If it Gained the level, it cannot be a Failed Gain.
                    if (!isGain && Math.Min(open, prevClose) < p.Level && High[0] > p.Level && close < p.Level)
                    {
                        isFTGOnCurrentBar = true;
                        hasBearishEvent = true;
                        DPrint($"    -> FTG detected against level {p.Level}");
                    }

                    // FTL: Started above or at the level, wicked below it, and closed above it.
                    // If it Lost the level, it cannot be a Failed Loss.
                    if (!isLoss && Math.Max(open, prevClose) > p.Level && Low[0] < p.Level && close > p.Level)
                    {
                        isFTLOnCurrentBar = true;
                        hasBullishEvent = true;
                        DPrint($"    -> FTL detected against level {p.Level}");
                    }
                }
                
                DPrint($"  Final Events -> Bullish: {hasBullishEvent}, Bearish: {hasBearishEvent}");

                if (hasBullishEvent && hasBearishEvent)
                    currentBias = 2; // Conflict
                else if (hasBullishEvent)
                    currentBias = 1;
                else if (hasBearishEvent)
                    currentBias = -1;

                Value[0] = currentBias;

                // --- Drawing Text Markers ---
                if (ShowGainLoss)
                {
                    if (isGainOnCurrentBar)
                        Draw.Text(this, "Gain_" + CurrentBar, "▲", 0, Low[0] - TickSize * 5, GainColor);
                    if (isLossOnCurrentBar)
                        Draw.Text(this, "Loss_" + CurrentBar, "▼", 0, High[0] + TickSize * 5, LossColor);
                }
                
                if (ShowFTGFTL)
                {
                    if (isFTGOnCurrentBar)
                        Draw.Text(this, "FTG_" + CurrentBar, "🡇", 0, High[0] + TickSize * 15, FailedGainColor);
                    if (isFTLOnCurrentBar)
                        Draw.Text(this, "FTL_" + CurrentBar, "🡅", 0, Low[0] - TickSize * 15, FailedLossColor);
                }

                // --- Containment Logic ---
                double? recentHighPivot = null;
                double? recentLowPivot = null;
                int? recentHighBar = null;
                int? recentLowBar = null;

                // visibleConfirmedPivots is sorted by most recent first
                foreach (var p in visibleConfirmedPivots)
                {
                    if (recentHighPivot == null && p.IsHigh) 
                    {
                        recentHighPivot = p.Level;
                        recentHighBar = p.BarIndex;
                    }
                    if (recentLowPivot == null && !p.IsHigh)
                    {
                        recentLowPivot = p.Level;
                        recentLowBar = p.BarIndex;
                    }
                    if (recentHighPivot != null && recentLowPivot != null) break;
                }

                bool isContained = false;
                if (recentHighPivot != null && recentLowPivot != null)
                {
                    if (Close[0] <= recentHighPivot.Value && Close[0] >= recentLowPivot.Value)
                    {
                        isContained = true;
                    }
                }

                if (ShowContainmentBox && isContained && recentHighBar.HasValue && recentLowBar.HasValue)
                {
                    int startBar = Math.Min(recentHighBar.Value, recentLowBar.Value);
                    string boxTag = "ContainBox_" + instanceId + "_" + recentHighBar.Value + "_" + recentLowBar.Value;
                    Draw.Rectangle(this, boxTag, false, CurrentBar - startBar, recentHighPivot.Value, 0, recentLowPivot.Value, Brushes.Transparent, ContainmentBoxColor, 20);
                }
            }

            previousPivotCount = pivotBars.Count;
        }
        #endregion

        #region Pivot Processing
        private void ProcessPivot(int barIndex, double price, bool isHigh)
        {
            if (pivotBars.Count == 0)
            {
                pivotBars.Add(barIndex);
                pivotPrices.Add(price);
                pivotIsHigh.Add(isHigh);
            }
            else
            {
                bool lastIsHigh  = pivotIsHigh.Last();
                double lastPrice = pivotPrices.Last();
                int    lastIdx   = pivotPrices.Count - 1;

                if (isHigh == lastIsHigh)
                {
                    if ((isHigh && price > lastPrice) || (!isHigh && price < lastPrice))
                    {
                        pivotBars[lastIdx]   = barIndex;
                        pivotPrices[lastIdx] = price;
                    }
                }
                else
                {
                    pivotBars.Add(barIndex);
                    pivotPrices.Add(price);
                    pivotIsHigh.Add(isHigh);
                }
            }

            RedrawZigZag();
        }
        #endregion

        #region Drawing Logic
        private void RedrawZigZag()
        {
            if (ChartControl == null) return; // Prevent drawing exceptions when hosted as a headless child

            foreach (var tag in previousLineTags)
                RemoveDrawObject(tag);
            previousLineTags.Clear();

            if (DrawZigZags)
            {
                for (int i = 1; i < pivotBars.Count; i++)
                {
                    string zzTag = $"{tagPrefix}{instanceId}_{i}";
                    Draw.Line(this, zzTag, false,
                        CurrentBar - pivotBars[i - 1], pivotPrices[i - 1],
                        CurrentBar - pivotBars[i],     pivotPrices[i],
                        ZigZagColor, DashStyleHelper.Solid, 2);
                    previousLineTags.Add(zzTag);
                }
            }

            double price = Close[0];
            var pivotsWithDistance = pivotBars.Select((b, idx) => new
            {
                BarIndex  = b,
                ListIndex = idx,
                Level     = pivotIsHigh[idx]
                             ? Math.Max(Open[CurrentBar - b], Close[CurrentBar - b])
                             : Math.Min(Open[CurrentBar - b], Close[CurrentBar - b]),
                IsHigh    = pivotIsHigh[idx]
            });

            var visiblePivots = pivotsWithDistance
                .Where(x => (CurrentBar - x.BarIndex) <= RelevanceFactor)
                .Where(x => x.Level != price)
                .OrderByDescending(x => x.ListIndex)
                .Take(NumberOfLevels);

            foreach (var p in visiblePivots)
            {
                int state = 0; // 0 = Neutral, 1 = Gained, -1 = Lost
                
                // ONLY evaluate Gain/Loss if it is CONFIRMED (not the last pivot)
                bool isConfirmed = p.ListIndex < pivotBars.Count - 1;
                
                if (isConfirmed)
                {
                    // Evaluate state from the bar after the pivot was created up to current bar
                    for (int barsAgo = CurrentBar - p.BarIndex - 1; barsAgo >= 0; barsAgo--)
                    {
                        double open = Open[barsAgo];
                        double close = Close[barsAgo];
                        double prevClose = Close[barsAgo + 1];

                        // Gain condition: minReach < Level AND Close > Level
                        double minReach = Math.Min(open, prevClose);
                        if (minReach < p.Level && close > p.Level)
                        {
                            state = 1;
                        }
                        // Loss condition: maxReach > Level AND Close < Level
                        else if (Math.Max(open, prevClose) > p.Level && close < p.Level)
                        {
                            state = -1;
                        }
                    }
                } // End if (isConfirmed)

                Brush lineBrush = LevelLineColor;
                if (state == 1)      lineBrush = LevelLineColorGained;
                else if (state == -1) lineBrush = LevelLineColorLost;

                string tag = $"{horizLinePrefix}{instanceId}_{p.BarIndex}_{(p.IsHigh ? "H" : "L")}";
                
                Draw.Line(this, tag, false,
                    CurrentBar - p.BarIndex, p.Level,
                    -1,                    p.Level,
                    lineBrush, LevelLineDashStyle, LevelLineWidth);

                previousLineTags.Add(tag);
            }
        }
        #endregion

        #region Utilities
        private void DPrint(string message) { if (DebugPrints) Print(message); }
        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenBiasV0003[] cacheAlightenBiasV0003;
		public AlightenBiasV0003 AlightenBiasV0003(int barsToProcess, int numberOfLevels, int relevanceFactor, Brush zigZagColor, Brush levelLineColor, Brush levelLineColorGained, Brush levelLineColorLost, int levelLineWidth, DashStyleHelper levelLineDashStyle, bool showContainmentBox, Brush containmentBoxColor, Brush gainColor, Brush lossColor, Brush failedGainColor, Brush failedLossColor, bool showGainLoss, bool showFTGFTL, bool debugPrints)
		{
			return AlightenBiasV0003(Input, barsToProcess, numberOfLevels, relevanceFactor, zigZagColor, levelLineColor, levelLineColorGained, levelLineColorLost, levelLineWidth, levelLineDashStyle, showContainmentBox, containmentBoxColor, gainColor, lossColor, failedGainColor, failedLossColor, showGainLoss, showFTGFTL, debugPrints);
		}

		public AlightenBiasV0003 AlightenBiasV0003(ISeries<double> input, int barsToProcess, int numberOfLevels, int relevanceFactor, Brush zigZagColor, Brush levelLineColor, Brush levelLineColorGained, Brush levelLineColorLost, int levelLineWidth, DashStyleHelper levelLineDashStyle, bool showContainmentBox, Brush containmentBoxColor, Brush gainColor, Brush lossColor, Brush failedGainColor, Brush failedLossColor, bool showGainLoss, bool showFTGFTL, bool debugPrints)
		{
			if (cacheAlightenBiasV0003 != null)
				for (int idx = 0; idx < cacheAlightenBiasV0003.Length; idx++)
					if (cacheAlightenBiasV0003[idx] != null && cacheAlightenBiasV0003[idx].BarsToProcess == barsToProcess && cacheAlightenBiasV0003[idx].NumberOfLevels == numberOfLevels && cacheAlightenBiasV0003[idx].RelevanceFactor == relevanceFactor && cacheAlightenBiasV0003[idx].ZigZagColor == zigZagColor && cacheAlightenBiasV0003[idx].LevelLineColor == levelLineColor && cacheAlightenBiasV0003[idx].LevelLineColorGained == levelLineColorGained && cacheAlightenBiasV0003[idx].LevelLineColorLost == levelLineColorLost && cacheAlightenBiasV0003[idx].LevelLineWidth == levelLineWidth && cacheAlightenBiasV0003[idx].LevelLineDashStyle == levelLineDashStyle && cacheAlightenBiasV0003[idx].ShowContainmentBox == showContainmentBox && cacheAlightenBiasV0003[idx].ContainmentBoxColor == containmentBoxColor && cacheAlightenBiasV0003[idx].GainColor == gainColor && cacheAlightenBiasV0003[idx].LossColor == lossColor && cacheAlightenBiasV0003[idx].FailedGainColor == failedGainColor && cacheAlightenBiasV0003[idx].FailedLossColor == failedLossColor && cacheAlightenBiasV0003[idx].ShowGainLoss == showGainLoss && cacheAlightenBiasV0003[idx].ShowFTGFTL == showFTGFTL && cacheAlightenBiasV0003[idx].DebugPrints == debugPrints && cacheAlightenBiasV0003[idx].EqualsInput(input))
						return cacheAlightenBiasV0003[idx];
			return CacheIndicator<AlightenBiasV0003>(new AlightenBiasV0003(){ BarsToProcess = barsToProcess, NumberOfLevels = numberOfLevels, RelevanceFactor = relevanceFactor, ZigZagColor = zigZagColor, LevelLineColor = levelLineColor, LevelLineColorGained = levelLineColorGained, LevelLineColorLost = levelLineColorLost, LevelLineWidth = levelLineWidth, LevelLineDashStyle = levelLineDashStyle, ShowContainmentBox = showContainmentBox, ContainmentBoxColor = containmentBoxColor, GainColor = gainColor, LossColor = lossColor, FailedGainColor = failedGainColor, FailedLossColor = failedLossColor, ShowGainLoss = showGainLoss, ShowFTGFTL = showFTGFTL, DebugPrints = debugPrints }, input, ref cacheAlightenBiasV0003);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenBiasV0003 AlightenBiasV0003(int barsToProcess, int numberOfLevels, int relevanceFactor, Brush zigZagColor, Brush levelLineColor, Brush levelLineColorGained, Brush levelLineColorLost, int levelLineWidth, DashStyleHelper levelLineDashStyle, bool showContainmentBox, Brush containmentBoxColor, Brush gainColor, Brush lossColor, Brush failedGainColor, Brush failedLossColor, bool showGainLoss, bool showFTGFTL, bool debugPrints)
		{
			return indicator.AlightenBiasV0003(Input, barsToProcess, numberOfLevels, relevanceFactor, zigZagColor, levelLineColor, levelLineColorGained, levelLineColorLost, levelLineWidth, levelLineDashStyle, showContainmentBox, containmentBoxColor, gainColor, lossColor, failedGainColor, failedLossColor, showGainLoss, showFTGFTL, debugPrints);
		}

		public Indicators.AlightenBiasV0003 AlightenBiasV0003(ISeries<double> input , int barsToProcess, int numberOfLevels, int relevanceFactor, Brush zigZagColor, Brush levelLineColor, Brush levelLineColorGained, Brush levelLineColorLost, int levelLineWidth, DashStyleHelper levelLineDashStyle, bool showContainmentBox, Brush containmentBoxColor, Brush gainColor, Brush lossColor, Brush failedGainColor, Brush failedLossColor, bool showGainLoss, bool showFTGFTL, bool debugPrints)
		{
			return indicator.AlightenBiasV0003(input, barsToProcess, numberOfLevels, relevanceFactor, zigZagColor, levelLineColor, levelLineColorGained, levelLineColorLost, levelLineWidth, levelLineDashStyle, showContainmentBox, containmentBoxColor, gainColor, lossColor, failedGainColor, failedLossColor, showGainLoss, showFTGFTL, debugPrints);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenBiasV0003 AlightenBiasV0003(int barsToProcess, int numberOfLevels, int relevanceFactor, Brush zigZagColor, Brush levelLineColor, Brush levelLineColorGained, Brush levelLineColorLost, int levelLineWidth, DashStyleHelper levelLineDashStyle, bool showContainmentBox, Brush containmentBoxColor, Brush gainColor, Brush lossColor, Brush failedGainColor, Brush failedLossColor, bool showGainLoss, bool showFTGFTL, bool debugPrints)
		{
			return indicator.AlightenBiasV0003(Input, barsToProcess, numberOfLevels, relevanceFactor, zigZagColor, levelLineColor, levelLineColorGained, levelLineColorLost, levelLineWidth, levelLineDashStyle, showContainmentBox, containmentBoxColor, gainColor, lossColor, failedGainColor, failedLossColor, showGainLoss, showFTGFTL, debugPrints);
		}

		public Indicators.AlightenBiasV0003 AlightenBiasV0003(ISeries<double> input , int barsToProcess, int numberOfLevels, int relevanceFactor, Brush zigZagColor, Brush levelLineColor, Brush levelLineColorGained, Brush levelLineColorLost, int levelLineWidth, DashStyleHelper levelLineDashStyle, bool showContainmentBox, Brush containmentBoxColor, Brush gainColor, Brush lossColor, Brush failedGainColor, Brush failedLossColor, bool showGainLoss, bool showFTGFTL, bool debugPrints)
		{
			return indicator.AlightenBiasV0003(input, barsToProcess, numberOfLevels, relevanceFactor, zigZagColor, levelLineColor, levelLineColorGained, levelLineColorLost, levelLineWidth, levelLineDashStyle, showContainmentBox, containmentBoxColor, gainColor, lossColor, failedGainColor, failedLossColor, showGainLoss, showFTGFTL, debugPrints);
		}
	}
}

#endregion

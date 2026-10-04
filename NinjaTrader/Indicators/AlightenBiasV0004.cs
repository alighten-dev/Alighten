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
using BiasScoreMode = NinjaTrader.NinjaScript.Indicators.AlightenBiasV0004.BiasScoreMode;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    // V0004: adds Kris's MidPoint Mania level scoring as a second, combinable way
    // of choosing the pivots the bias is evaluated against.
    //   Recent levels  - V0003 behaviour: newest pivots inside the relevance window.
    //   Scored levels  - score = w * log(1 + swing/ATR) + (1 - w) * (1 - distance/window)
    // Either or both can be on; the bias and markers use the union of the two sets.
    public class AlightenBiasV0004 : Indicator
    {
        public enum BiasScoreMode
        {
            RelevantPlusNearby,
            LargestSwings
        }

        private class LevelInfo
        {
            public int    BarIndex;
            public int    ListIndex;
            public double Level;
            public bool   IsHigh;
            public bool   Recent;
            public bool   Scored;
        }

        #region Variables
        private List<int>       pivotBars;
        private List<double>    pivotPrices;
        private List<bool>      pivotIsHigh;
        private List<double>    pivotAtr;          // ATR when the pivot was confirmed (NaN while developing)
        private HashSet<string> previousLineTags = new HashSet<string>();
        private const string    tagPrefix       = "ZZ_line_";
        private const string    horizLinePrefix = "ZZ_Horz_";
        private string          instanceId;
        private int             previousPivotCount = 0;
        private bool            pivotsChanged;
        private ATR             atr;
        private List<int>       scoredIdx       = new List<int>();
        private HashSet<int>    scoredRetained  = new HashSet<int>();
        private string          lastScoredSig   = "";
        private const double    stabilityBonus  = 0.10;
        #endregion

        #region Properties

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Bars To Process (0 = all)", GroupName = "Parameters", Order = 0)]
        public int BarsToProcess { get; set; } = 500;

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

        // --- V0003 Properties ---

        [NinjaScriptProperty]
        [Display(Name = "Show Containment Box", GroupName = "New Settings (V0003)", Order = 9)]
        public bool ShowContainmentBox { get; set; } = false;

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

        // --- V0004 Properties: level selection ---

        [NinjaScriptProperty]
        [Display(Name = "Use Recent Levels", Description = "V0003 selection: the newest pivots inside the Relevance Factor, up to Number of Levels.", GroupName = "Level Selection (V0004)", Order = 20)]
        public bool UseRecentLevels { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use Scored Levels", Description = "MidPoint Mania ranking: blends log(1 + swing / ATR) with proximity to price. Adds these levels to the recent ones when both are on.", GroupName = "Level Selection (V0004)", Order = 21)]
        public bool UseScoredLevels { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Score Mode", Description = "Relevant + Nearby: size and proximity, only levels inside the nearby window. Largest Swings: size only, any distance.", GroupName = "Level Selection (V0004)", Order = 22)]
        public BiasScoreMode ScoreMode { get; set; } = BiasScoreMode.RelevantPlusNearby;

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Scored Levels Count", GroupName = "Level Selection (V0004)", Order = 23)]
        public int ScoredLevelCount { get; set; } = 4;

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Scored Lookback (Bars)", Description = "How far back scored candidates may come from. Independent of the Relevance Factor so large older swings can be found.", GroupName = "Level Selection (V0004)", Order = 24)]
        public int ScoredLookbackBars { get; set; } = 1000;

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Swing-Size Weight (%)", Description = "100 ranks by swing size only, 0 by distance only. Largest Swings always uses size.", GroupName = "Level Selection (V0004)", Order = 25)]
        public int SizeWeightPct { get; set; } = 65;

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Length", GroupName = "Level Selection (V0004)", Order = 26)]
        public int ScoreAtrLength { get; set; } = 14;

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "Nearby Window (ATR)", Description = "Levels farther than this from price are not scored in Relevant + Nearby mode.", GroupName = "Level Selection (V0004)", Order = 27)]
        public double NearbyWindowAtr { get; set; } = 2.0;

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Extra Exit Distance (%)", Description = "A level already selected may stay selected this far beyond the window, and gets a small score bonus, to reduce switching.", GroupName = "Level Selection (V0004)", Order = 28)]
        public int ExitExtraPct { get; set; } = 25;

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Cluster Distance (Ticks)", Description = "A scored level this close to a recent level or a higher-ranked scored level is skipped.", GroupName = "Level Selection (V0004)", Order = 29)]
        public int ClusterTicks { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name = "Scored Line Dash Style", GroupName = "Level Selection (V0004)", Order = 30)]
        public DashStyleHelper ScoredLineDashStyle { get; set; } = DashStyleHelper.Dash;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Scored Line Width", GroupName = "Level Selection (V0004)", Order = 31)]
        public int ScoredLineWidth { get; set; } = 2;

        // Display-only (not NinjaScriptProperty) so the generated factory signature is unchanged.
        [XmlIgnore]
        [Display(Name = "Scored Long Level Color", Description = "Scored level price has gained, or an untouched low below price.", GroupName = "Level Selection (V0004)", Order = 32)]
        public Brush ScoredLongColor { get; set; } = Brushes.Cyan;
        [Browsable(false)]
        public string ScoredLongColorSerialize
        {
            get => Serialize.BrushToString(ScoredLongColor);
            set => ScoredLongColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [Display(Name = "Scored Short Level Color", Description = "Scored level price has lost, or an untouched high above price.", GroupName = "Level Selection (V0004)", Order = 33)]
        public Brush ScoredShortColor { get; set; } = Brushes.White;
        [Browsable(false)]
        public string ScoredShortColorSerialize
        {
            get => Serialize.BrushToString(ScoredShortColor);
            set => ScoredShortColor = Serialize.StringToBrush(value);
        }

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
                Description               = "AlightenBiasV0004: V0003 plus MidPoint Mania scored level selection";
                Name                      = "AlightenBiasV0004";
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
                instanceId = $"BiasV4_{Instrument.FullName}_{BarsPeriod.BarsPeriodType}_{BarsPeriod.Value}_{Guid.NewGuid()}";
            }
            else if (State == State.DataLoaded)
            {
                pivotBars    = new List<int>();
                pivotPrices  = new List<double>();
                pivotIsHigh  = new List<bool>();
                pivotAtr     = new List<double>();
                atr          = ATR(ScoreAtrLength);
                previousLineTags.Clear();
                scoredIdx.Clear();
                scoredRetained.Clear();
                lastScoredSig = "";
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

            pivotsChanged = false;

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

            // --- Level selection (scored set is computed once per bar) ---
            List<int> recentConfirmed = RecentIndices(false);
            UpdateScoredSelection(recentConfirmed);

            string scoredSig = string.Join(",", scoredIdx);
            if (pivotsChanged || scoredSig != lastScoredSig)
            {
                RedrawLevels(BuildLevelSet(RecentIndices(true)));
                lastScoredSig = scoredSig;
            }

            // --- Bias State Machine ---
            Value[0] = CurrentBar == 0 ? 0 : Value[1];

            if (pivotBars.Count > 1) // Must have at least one confirmed pivot
            {
                // Recent and scored levels combined, most recent first
                List<LevelInfo> visibleConfirmedPivots = BuildLevelSet(recentConfirmed);

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

                    DPrint($"  Evaluating Confirmed Pivot at Level: {p.Level} (Created {CurrentBar - p.BarIndex} bars ago){(p.Scored ? " [scored]" : "")}");

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
                pivotAtr.Add(double.NaN);
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
                    // The previous developing pivot is now confirmed: record ATR at confirmation.
                    pivotAtr[lastIdx] = atr[0];
                    pivotBars.Add(barIndex);
                    pivotPrices.Add(price);
                    pivotIsHigh.Add(isHigh);
                    pivotAtr.Add(double.NaN);
                }
            }

            pivotsChanged = true;
        }
        #endregion

        #region Level Selection
        // Body extreme of the pivot bar: the level the bias is measured against.
        private double BodyLevel(int idx)
        {
            int barsAgo = CurrentBar - pivotBars[idx];
            return pivotIsHigh[idx]
                ? Math.Max(Open[barsAgo], Close[barsAgo])
                : Math.Min(Open[barsAgo], Close[barsAgo]);
        }

        // V0003 recency selection, newest first. includeDeveloping adds the last (unconfirmed) pivot, as the drawing did.
        private List<int> RecentIndices(bool includeDeveloping)
        {
            var result = new List<int>();
            if (!UseRecentLevels) return result;

            double price = Close[0];
            int top = includeDeveloping ? pivotBars.Count - 1 : pivotBars.Count - 2;
            for (int idx = top; idx >= 0 && result.Count < NumberOfLevels; idx--)
            {
                if (CurrentBar - pivotBars[idx] > RelevanceFactor) break;
                if (BodyLevel(idx) == price) continue;
                result.Add(idx);
            }
            return result;
        }

        // Kris's MidPoint Mania priority ranking applied to confirmed pivots.
        private void UpdateScoredSelection(List<int> recentConfirmed)
        {
            scoredIdx.Clear();
            if (!UseScoredLevels || pivotBars.Count < 3)
            {
                scoredRetained.Clear();
                return;
            }

            double price      = Close[0];
            double atrNow     = atr[0];
            double window     = Math.Max(NearbyWindowAtr * atrNow, TickSize);
            double cluster    = ClusterTicks * TickSize;
            double sizeWeight = SizeWeightPct / 100.0;
            bool   nearby     = ScoreMode == BiasScoreMode.RelevantPlusNearby;

            var candidates = new List<KeyValuePair<int, double>>();
            for (int idx = pivotBars.Count - 2; idx >= 1; idx--)
            {
                if (CurrentBar - pivotBars[idx] > ScoredLookbackBars) break;

                double level = BodyLevel(idx);
                if (level == price) continue;

                double atrConf = pivotAtr[idx];
                if (double.IsNaN(atrConf) || atrConf <= 0) continue;

                double strength = Math.Abs(pivotPrices[idx] - pivotPrices[idx - 1]) / atrConf;
                double distance = Math.Abs(level - price);
                bool   retained = scoredRetained.Contains(idx);

                double score;
                if (nearby)
                {
                    double exitWindow = window * (retained ? 1.0 + ExitExtraPct / 100.0 : 1.0);
                    if (distance > exitWindow) continue;
                    score = sizeWeight * Math.Log(1.0 + strength) + (1.0 - sizeWeight) * (1.0 - distance / window);
                    // Stability bonus applies only to Nearby.
                    if (retained) score += stabilityBonus;
                }
                else
                    score = Math.Log(1.0 + strength);

                candidates.Add(new KeyValuePair<int, double>(idx, score));
            }

            // Recent levels are always shown, so scored slots go to levels they don't already cover.
            var occupied = recentConfirmed.Select(BodyLevel).ToList();

            foreach (var c in candidates.OrderByDescending(x => x.Value))
            {
                if (scoredIdx.Count >= ScoredLevelCount) break;
                double level = BodyLevel(c.Key);
                if (occupied.Any(o => Math.Abs(level - o) <= cluster)) continue;

                scoredIdx.Add(c.Key);
                occupied.Add(level);
                DPrint($"  Scored level {level} (pivot bar {pivotBars[c.Key]}) score {c.Value:F3}");
            }

            scoredRetained = new HashSet<int>(scoredIdx);
        }

        // Union of the recent indices and the current scored selection, most recent first.
        private List<LevelInfo> BuildLevelSet(List<int> recent)
        {
            var recentSet = new HashSet<int>(recent);
            return recent.Union(scoredIdx)
                .Distinct()
                .OrderByDescending(idx => idx)
                .Select(idx => new LevelInfo
                {
                    BarIndex  = pivotBars[idx],
                    ListIndex = idx,
                    Level     = BodyLevel(idx),
                    IsHigh    = pivotIsHigh[idx],
                    Recent    = recentSet.Contains(idx),
                    Scored    = scoredIdx.Contains(idx)
                })
                .ToList();
        }
        #endregion

        #region Drawing Logic
        private void RedrawLevels(List<LevelInfo> visiblePivots)
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

                bool scoredStyle = p.Scored && !p.Recent;

                Brush lineBrush = LevelLineColor;
                if (scoredStyle)
                {
                    // Untouched levels take their side from the pivot: a low sits below price, a high above.
                    bool isLong = state == 1 || (state == 0 && !p.IsHigh);
                    lineBrush = isLong ? ScoredLongColor : ScoredShortColor;
                }
                else if (state == 1)  lineBrush = LevelLineColorGained;
                else if (state == -1) lineBrush = LevelLineColorLost;

                string tag = $"{horizLinePrefix}{instanceId}_{p.BarIndex}_{(p.IsHigh ? "H" : "L")}";

                Draw.Line(this, tag, false,
                    CurrentBar - p.BarIndex, p.Level,
                    -1,                    p.Level,
                    lineBrush,
                    scoredStyle ? ScoredLineDashStyle : LevelLineDashStyle,
                    scoredStyle ? ScoredLineWidth : LevelLineWidth);

                previousLineTags.Add(tag);
            }
        }
        #endregion

        #region Utilities
        private void DPrint(string message) { if (DebugPrints) Print(message); }
        #endregion
    }
}

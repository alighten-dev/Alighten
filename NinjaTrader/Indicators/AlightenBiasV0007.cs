// AlightenBiasV0007 (2026-11-08): AlightenBiasV0006 build l as a version, at the user's request. New since the V0006 promotion
// (build k): 4.5 POC Row 3 = either the volume POC or the |delta| row, whichever is in the wick (both -> the more extreme), and the
// DEFAULT POC Row is 2 = largest delta against the pivot (net buying into a pivot high / net selling into a pivot low), the user's
// pick after trying it live. Log: Mirror Logs\BiasPocV0007.log (same line format). Everything below is V0006 build l renamed.
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
    // BUILD 2026-11-07 01:55 (build k). The newest build carries the latest BUILD stamp here and in the Description.
    // Build h: footprint rows anchor at the TRUE session open (walk back to the session's first bar when the window
    //   starts mid-session; before, the first processed bar's open anchored a grid shifted vs the footprint's, which
    //   split the 14:06 11/06 -19 row into -17 + rest and let a +18 body row win the Delta read). '1.1 Sessions To
    //   Process' (default 1) replaces Bars To Process: the window starts at a session's first bar.
    // AlightenBiasV0007 (build 2026-11-05d) = AlightenBiasV0004 + the user's POC-at-the-extreme read (moved here from
    // AlightenZigZagv7 build b, which shared V4's pivot rules; the user wanted it in the Bias, 2026-11-05).
    //   A 1-tick series is added (as AlightenFootprintOrderFlowV00022 does) and every tick is binned per primary bar into
    //   "Cell Size (ticks)" rows anchored at the session open (or the bar low), ask = traded at/above the L1 ask, bid =
    //   at/below the L1 bid, mid prints dropped - the rows FootprintV0022 shows with BidAsk + Volume Profile.
    //   When a pivot is confirmed (ProcessPivot: the opposite pivot starts) it is queued; once the pivot bar and the
    //   "Bars After Pivot" following bars are complete (2 bars of slack for tick ordering) each is read: POC row = the row
    //   with the most volume; "in the wick" = the whole row lies beyond the body on the pivot side; wick = pivot extreme -
    //   body extreme in ticks; pocF = where the row centre sits in the wick (0 = at the body, 1 = at the extreme).
    //   Signal = in the wick AND the "extreme" test (mode: 0 wick >= min OR pocF >= fraction, 1 both, 2 pocF only,
    //   3 wick only). The first qualifying bar wins: a diamond on that bar, PocSignal plot (Values[1]) = +1 (low pivot) /
    //   -1 (high pivot) on the bar of the read, optional sound, and one log line per read bar (hit or not) in
    //   Mirror Logs\BiasPocV0007.log - realtime always, history only with "Log Historical Pivots". The pivotBar time is
    //   the merge key against the Mirror's structure / retest logs.
    //   Build f (2026-11-06) - 'POC Row' option: 0 = heaviest row (volume POC, as before); 1 = the row with the largest
    //   |delta| (what the footprint's yellow box marks in Delta visual mode - the 15:15 5-min bar that looked like a
    //   short had a 22x74 row at the top while the volume POC sat mid-body); 2 = the largest delta AGAINST the pivot
    //   (buying into a high / selling into a low). Every log line carries all three rows (vpoc/dpoc/spoc with their
    //   in-wick flags and wick positions) plus rowMode, so the forward log can score the three reads side by side.
    //   Research note (RESULTS_footprint_poc_wick_refined.md, 2026-11-05): the read did not select winners on 307 lab
    //   days, big bars included; the log exists so the forward prints can be merged with the level logs.
    //   Build b - two stages, as the Mirror's structure signal (same tag redrawn in place):
    //     PROVISIONAL  the newest pivot (still replaceable by a more extreme same-side bar) is read as soon as its bar
    //                  has closed: faint diamond in the provisional colour, PocSignal +1 / -1, a medium alert, a log line
    //                  with stage=PROV. Removed if the pivot is replaced or the read is not there.
    //     CONFIRMED    the opposite pivot has started and the bars are complete: the diamond is redrawn in the confirmed
    //                  colour, PocSignal +2 / -2 on that bar, a high alert, log lines with stage=CONF.
    //   Build c: no waiting. In realtime a bar's footprint is complete at its own close (every tick of bar k has been
    //   binned before bar k+1's first tick closes it), so the provisional is read at the pivot bar's own close and the
    //   confirmed read happens the bar the opposite pivot starts (once the bars after the pivot are closed). Build b waited
    //   one bar for the provisional (which a V-reversal never granted) and two bars of slack for the confirmed read.
    //   History keeps one bar of slack (same-second tick/bar ordering is not guaranteed there).
    //   Build d: a pivot replaced in place by a more extreme same-side bar drops its provisional mark right there, in
    //   ProcessPivot. Build c only dropped it when that side was still the newest pivot, so an outside bar that replaced
    //   the high AND started the low in one go (23:51:00 Nov 5) left the old mark orphaned on the previous bar.
    //   Everything below the V0004 comment is V0004 unchanged apart from the marked "V0006" lines.
    // Build g (2026-11-06): the V0004 scored-levels feature (Kris's MidPoint Mania ranking: Use Scored Levels, Score Mode,
    //   Scored Levels Count/Lookback, Swing-Size Weight, ATR Length, Nearby Window, Extra Exit Distance, Cluster Distance,
    //   Scored line style/colours, Use Recent Levels) is REMOVED at the user's request - never used. Levels are the V0003
    //   recent set only. Every property is numbered group.item in the dialog.
    // V0004 (history): added that scoring as a second, combinable way of choosing the pivots the bias is evaluated against.
    public class AlightenBiasV0007 : Indicator
    {
        private class LevelInfo
        {
            public int    BarIndex;
            public int    ListIndex;
            public double Level;
            public bool   IsHigh;
        }

        #region Variables
        private List<int>       pivotBars;
        private List<double>    pivotPrices;
        private List<bool>      pivotIsHigh;
        private HashSet<string> previousLineTags = new HashSet<string>();
        private const string    tagPrefix       = "ZZ_line_";
        private const string    horizLinePrefix = "ZZ_Horz_";
        private string          instanceId;
        private int             previousPivotCount = 0;
        private bool            pivotsChanged;

        // --- V0006: footprint bins and the POC read ---
        private Dictionary<int, Dictionary<double, double>> binAsk = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> binBid = new Dictionary<int, Dictionary<double, double>>();
        private double          sessionOpenPrice = 0.0;
        private int             processCutoffBar = -1;   // build h: first primary bar inside the Sessions To Process window (-1 = not computed)
        private List<int>       pocPending       = new List<int>();     // pivot list indices confirmed, not yet read
        private List<string>    pocMarkerTags    = new List<string>();
        private string          loadId           = "";
        private string          tfTag            = "";   // chart bar period for the log (30s, 5m, ...)
        private bool            pocLogOn         = true;
        private const string    pocMarkerPrefix  = "BiasV7_Poc_";
        private Dictionary<string, string> pocProv = new Dictionary<string, string>();   // provisional marks: pivot key -> tag
        private int             pocProvDir       = 0;                  // +1 long / -1 short while the newest pivot's provisional stands
        #endregion

        #region Properties

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "1.1 Sessions To Process (0 = all)", Description = "Build h: pivots and footprint rows are computed from the first bar of the Nth most recent session (1 = the current session only), so the row grid always anchors at a real session open. Replaces Bars To Process.", GroupName = "1. Parameters", Order = 1)]
        public int SessionsToProcess { get; set; } = 1;

        [Display(Name = "1.2 Draw ZigZags", GroupName = "1. Parameters", Order = 2)]
        public bool DrawZigZags { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1,100)]
        [Display(Name = "1.3 Number of Levels", GroupName = "1. Parameters", Order = 3)]
        public int NumberOfLevels { get; set; } = 6;

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "1.4 Relevance Factor (Bars)", GroupName = "1. Parameters", Order = 4)]
        public int RelevanceFactor { get; set; } = 300;

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "1.5 ZigZag Color", GroupName = "1. Parameters", Order = 5)]
        public Brush ZigZagColor { get; set; } = Brushes.Yellow;
        [Browsable(false)]
        public string ZigZagColorSerialize
        {
            get => Serialize.BrushToString(ZigZagColor);
            set => ZigZagColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "2.1 Level Line Color (Neutral)", GroupName = "2. Color Settings", Order = 1)]
        public Brush LevelLineColor { get; set; } = Brushes.DodgerBlue;
        [Browsable(false)]
        public string LevelLineColorSerialize
        {
            get => Serialize.BrushToString(LevelLineColor);
            set => LevelLineColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "2.2 Level Line Color (Gained)", GroupName = "2. Color Settings", Order = 2)]
        public Brush LevelLineColorGained { get; set; } = Brushes.DarkGreen;
        [Browsable(false)]
        public string LevelLineColorGainedSerialize
        {
            get => Serialize.BrushToString(LevelLineColorGained);
            set => LevelLineColorGained = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "2.3 Level Line Color (Lost)", GroupName = "2. Color Settings", Order = 3)]
        public Brush LevelLineColorLost { get; set; } = Brushes.Crimson;
        [Browsable(false)]
        public string LevelLineColorLostSerialize
        {
            get => Serialize.BrushToString(LevelLineColorLost);
            set => LevelLineColorLost = Serialize.StringToBrush(value);
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "2.4 Level Line Width", GroupName = "2. Color Settings", Order = 4)]
        public int LevelLineWidth { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "2.5 Level Line Dash Style", GroupName = "2. Color Settings", Order = 5)]
        public DashStyleHelper LevelLineDashStyle { get; set; } = DashStyleHelper.Solid;

        // --- V0003 Properties ---

        [NinjaScriptProperty]
        [Display(Name = "3.1 Show Containment Box", GroupName = "3. New Settings (V0003)", Order = 1)]
        public bool ShowContainmentBox { get; set; } = false;

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "3.2 Containment Box Color", GroupName = "3. New Settings (V0003)", Order = 2)]
        public Brush ContainmentBoxColor { get; set; } = Brushes.DarkSlateGray;
        [Browsable(false)]
        public string ContainmentBoxColorSerialize
        {
            get => Serialize.BrushToString(ContainmentBoxColor);
            set => ContainmentBoxColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "3.3 Gain Text Color", GroupName = "3. New Settings (V0003)", Order = 3)]
        public Brush GainColor { get; set; } = Brushes.LimeGreen;
        [Browsable(false)]
        public string GainColorSerialize
        {
            get => Serialize.BrushToString(GainColor);
            set => GainColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "3.4 Loss Text Color", GroupName = "3. New Settings (V0003)", Order = 4)]
        public Brush LossColor { get; set; } = Brushes.Red;
        [Browsable(false)]
        public string LossColorSerialize
        {
            get => Serialize.BrushToString(LossColor);
            set => LossColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "3.5 Failed Gain (FTG) Color", GroupName = "3. New Settings (V0003)", Order = 5)]
        public Brush FailedGainColor { get; set; } = Brushes.Orange;
        [Browsable(false)]
        public string FailedGainColorSerialize
        {
            get => Serialize.BrushToString(FailedGainColor);
            set => FailedGainColor = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "3.6 Failed Loss (FTL) Color", GroupName = "3. New Settings (V0003)", Order = 6)]
        public Brush FailedLossColor { get; set; } = Brushes.DodgerBlue;
        [Browsable(false)]
        public string FailedLossColorSerialize
        {
            get => Serialize.BrushToString(FailedLossColor);
            set => FailedLossColor = Serialize.StringToBrush(value);
        }

        [NinjaScriptProperty]
        [Display(Name = "3.7 Show Gain/Loss Markers", GroupName = "3. New Settings (V0003)", Order = 7)]
        public bool ShowGainLoss { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "3.8 Show FTG/FTL Markers", GroupName = "3. New Settings (V0003)", Order = 8)]
        public bool ShowFTGFTL { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "4.1 Enable POC Signal", Description = "Read the footprint of each confirmed pivot bar: POC row in the wick and at the extreme.", GroupName = "4. POC Signal (V0007)", Order = 1)]
        public bool EnablePocSignal { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "4.2 Cell Size (ticks)", Description = "Footprint row height, as FootprintV0022's Aggregation Interval.", GroupName = "4. POC Signal (V0007)", Order = 2)]
        public int PocCellTicks { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name = "4.3 Align Cells To Session Open", Description = "Rows start at the session open price (FootprintV0022's Align Aggregation To Session Open); off = each bar's low.", GroupName = "4. POC Signal (V0007)", Order = 3)]
        public bool PocAlignToSessionOpen { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 3)]
        [Display(Name = "4.4 Extreme Mode: 0 Wick OR Position, 1 Both, 2 Position only, 3 Wick only", GroupName = "4. POC Signal (V0007)", Order = 4)]
        public int PocExtremeMode { get; set; } = 0;

        [NinjaScriptProperty]
        [Range(0, 3)]
        [Display(Name = "4.5 POC Row: 0 Heaviest row (volume POC), 1 Largest |delta| row (footprint Delta box), 2 Largest delta against the pivot, 3 Either 0 or 1", Description = "Build f: which row the in-wick read uses. All three rows are logged on every line. Build l: 3 = the volume POC or the |delta| row, whichever lies in the wick (both -> the more extreme one).", GroupName = "4. POC Signal (V0007)", Order = 5)]
        public int PocRowMode { get; set; } = 2;

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "4.6 Min Wick (ticks)", Description = "Pivot extreme to body extreme.", GroupName = "4. POC Signal (V0007)", Order = 6)]
        public int PocMinWickTicks { get; set; } = 8;

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "4.7 POC Position In Wick (0 body .. 1 extreme)", Description = "Where the POC row centre sits in the wick; 0.5 = nearer the extreme than the body, 0.75 = top quarter.", GroupName = "4. POC Signal (V0007)", Order = 7)]
        public double PocExtremeFraction { get; set; } = 0.5;

        [NinjaScriptProperty]
        [Range(0, 5)]
        [Display(Name = "4.8 Bars After Pivot (also read)", Description = "0 = pivot bar only (default since build i); 1 = the bar after the pivot may also supply the read.", GroupName = "4. POC Signal (V0007)", Order = 8)]
        public int PocBarsAfterPivot { get; set; } = 0;   // build i: default 0 (the bar after the pivot gave more false positives than it caught)

        [NinjaScriptProperty]
        [Display(Name = "4.9 Draw POC Diamonds", GroupName = "4. POC Signal (V0007)", Order = 9)]
        public bool DrawPocMarkers { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "4.10 Diamond Offset (ticks)", GroupName = "4. POC Signal (V0007)", Order = 10)]
        public int PocMarkerOffsetTicks { get; set; } = 12;

        [XmlIgnore]
        [Display(Name = "4.11 Short Diamond Color (high pivot)", GroupName = "4. POC Signal (V0007)", Order = 11)]
        public Brush PocShortBrush { get; set; } = Brushes.Magenta;

        [Browsable(false)]
        public string PocShortBrushSerialize
        {
            get => Serialize.BrushToString(PocShortBrush);
            set => PocShortBrush = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [Display(Name = "4.12 Long Diamond Color (low pivot)", GroupName = "4. POC Signal (V0007)", Order = 12)]
        public Brush PocLongBrush { get; set; } = Brushes.Lime;

        [Browsable(false)]
        public string PocLongBrushSerialize
        {
            get => Serialize.BrushToString(PocLongBrush);
            set => PocLongBrush = Serialize.StringToBrush(value);
        }

        [NinjaScriptProperty]
        [Display(Name = "4.13 Show Provisional Diamonds", Description = "Draw the faint diamond as soon as the newest pivot's bar closes with the read, before the opposite pivot confirms it. Removed if a more extreme bar replaces the pivot.", GroupName = "4. POC Signal (V0007)", Order = 13)]
        public bool ShowPocProvisional { get; set; } = true;

        [XmlIgnore]
        [Display(Name = "4.14 Provisional Diamond Color", GroupName = "4. POC Signal (V0007)", Order = 14)]
        public Brush PocProvisionalBrush { get; set; } = Brushes.Khaki;

        [Browsable(false)]
        public string PocProvisionalBrushSerialize
        {
            get => Serialize.BrushToString(PocProvisionalBrush);
            set => PocProvisionalBrush = Serialize.StringToBrush(value);
        }

        [NinjaScriptProperty]
        [Display(Name = "4.15 Also Alert on Provisional", Description = "Medium-priority alert when the provisional diamond appears (the confirmed one is high priority).", GroupName = "4. POC Signal (V0007)", Order = 15)]
        public bool PocAlertOnProvisional { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "4.16 Enable POC Sound Alerts", GroupName = "4. POC Signal (V0007)", Order = 16)]
        public bool EnablePocSoundAlerts { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "4.17 POC Short Sound", GroupName = "4. POC Signal (V0007)", Order = 17)]
        public string PocShortSound { get; set; } = "Alert2.wav";

        [NinjaScriptProperty]
        [Display(Name = "4.18 POC Long Sound", GroupName = "4. POC Signal (V0007)", Order = 18)]
        public string PocLongSound { get; set; } = "Alert1.wav";

        [NinjaScriptProperty]
        [Display(Name = "4.19 Log Folder", Description = "Relative to Documents\\NinjaTrader 8, or an absolute path. The Mirror's logs live here too.", GroupName = "4. POC Signal (V0007)", Order = 19)]
        public string LogFolder { get; set; } = "Mirror Logs";

        [NinjaScriptProperty]
        [Display(Name = "4.20 Log Historical Pivots", Description = "Also log the pivots read while history loads (otherwise realtime only).", GroupName = "4. POC Signal (V0007)", Order = 20)]
        public bool LogHistoricalPivots { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "5.1 Enable Debug Output", GroupName = "5. Debugging", Order = 1)]
        public bool DebugPrints { get; set; } = false;

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> Bias { get { return Values[0]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PocSignal { get { return Values[1]; } }   // V0006: +1/-1 while provisional, +2/-2 on the confirming bar
        #endregion

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description               = "AlightenBiasV0007: V0006 build l as a version - the POC-at-the-extreme footprint read at each confirmed pivot (diamonds, PocSignal plot, Mirror Logs\\BiasPocV0007.log); POC Row 0 volume / 1 |delta| / 2 delta against the pivot (DEFAULT since V0007) / 3 either 0 or 1 - build a 2026-11-08 23:40";
                Name                      = "AlightenBiasV0007";
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
                AddPlot(Brushes.Transparent, "PocSignal");   // V0006
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);   // V0006: footprint rows, as AlightenFootprintOrderFlowV00022
                instanceId = $"BiasV7_{Instrument.FullName}_{BarsPeriod.BarsPeriodType}_{BarsPeriod.Value}_{Guid.NewGuid()}";
            }
            else if (State == State.DataLoaded)
            {
                pivotBars    = new List<int>();
                pivotPrices  = new List<double>();
                pivotIsHigh  = new List<bool>();
                previousLineTags.Clear();
                previousPivotCount = 0;

                binAsk.Clear(); binBid.Clear();          // V0006
                sessionOpenPrice = 0.0;
                processCutoffBar = -1;
                pocPending.Clear();
                pocMarkerTags.Clear();
                pocProv.Clear(); pocProvDir = 0;
                loadId   = DateTime.Now.ToString("yyyyMMddHHmmss");
                tfTag    = TfTag();
                pocLogOn = true;
            }
            else if (State == State.Terminated)
            {
                foreach (var tag in previousLineTags)
                    RemoveDrawObject(tag);
                previousLineTags.Clear();
                foreach (var tag in pocMarkerTags)        // V0006
                    RemoveDrawObject(tag);
                pocMarkerTags.Clear();
            }
        }
        #endregion

        #region OnBarUpdate
        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)                      // V0006: the 1-tick series feeds the footprint rows
            {
                AccumulateTick();
                return;
            }
            if (BarsInProgress != 0) return;

            if (CurrentBar < 2) return;

            if (CurrentBar < ProcessCutoff())   // build h: session window
                return;

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

            // --- Level selection: the newest pivots inside the relevance window (build g: scored levels removed) ---
            List<int> recentConfirmed = RecentIndices(false);
            if (pivotsChanged)
                RedrawLevels(BuildLevelSet(RecentIndices(true)));

            // --- Bias State Machine ---
            Value[0] = CurrentBar == 0 ? 0 : Value[1];

            if (pivotBars.Count > 1) // Must have at least one confirmed pivot
            {
                // Recent levels, most recent first
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

            if (CurrentBar % 500 == 0) PruneBins();       // V0006
            EvaluatePocSignals();
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
                        DropProvisional(pivotBars[lastIdx] + (isHigh ? "H" : "L"));   // V0006: the replaced pivot's mark goes
                        pivotBars[lastIdx]   = barIndex;
                        pivotPrices[lastIdx] = price;
                    }
                }
                else
                {
                    // The previous developing pivot is now confirmed.
                    pocPending.Add(lastIdx);              // V0006: read its footprint once its bars are complete
                    pivotBars.Add(barIndex);
                    pivotPrices.Add(price);
                    pivotIsHigh.Add(isHigh);
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

        // The recent indices as level records, most recent first.
        private List<LevelInfo> BuildLevelSet(List<int> recent)
        {
            return recent
                .Distinct()
                .OrderByDescending(idx => idx)
                .Select(idx => new LevelInfo
                {
                    BarIndex  = pivotBars[idx],
                    ListIndex = idx,
                    Level     = BodyLevel(idx),
                    IsHigh    = pivotIsHigh[idx]
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

                Brush lineBrush = LevelLineColor;
                if (state == 1)       lineBrush = LevelLineColorGained;

                string tag = $"{horizLinePrefix}{instanceId}_{p.BarIndex}_{(p.IsHigh ? "H" : "L")}";

                Draw.Line(this, tag, false,
                    CurrentBar - p.BarIndex, p.Level,
                    -1,                    p.Level,
                    lineBrush,
                    LevelLineDashStyle,
                    LevelLineWidth);

                previousLineTags.Add(tag);
            }
        }
        #endregion

        #region POC Signal (V0006)

        // Footprint rows per primary bar, the way AlightenFootprintOrderFlowV00022 builds them: each 1-tick bar is mapped
        // to the primary bar containing its time, classified against the L1 book (at/above ask = ask volume, at/below bid
        // = bid volume, mid prints dropped) and added to the row [origin + n*cell, +cell) that holds its price.
        // Build h: the first primary bar of the Sessions To Process window - the first bar of the Nth most recent session,
        // counted back from the last loaded bar (0 = everything). Computed once per load.
        private int ProcessCutoff()
        {
            if (processCutoffBar >= 0) return processCutoffBar;
            if (SessionsToProcess <= 0 || BarsArray == null || BarsArray.Length < 1) { processCutoffBar = 0; return 0; }
            int last = BarsArray[0].Count - 1, seen = 0, cut = 0;
            for (int i = last; i >= 0; i--)
            {
                if (!BarsArray[0].IsFirstBarOfSessionByIndex(i)) continue;
                seen++;
                if (seen >= SessionsToProcess) { cut = i; break; }
            }
            processCutoffBar = cut;
            return cut;
        }

        private void AccumulateTick()
        {
            try
            {
                if (!EnablePocSignal || BarsArray == null || BarsArray.Length < 2) return;
                if (CurrentBars[0] < 0 || CurrentBars[1] < 0) return;

                int  indexOffset  = BarsArray[1].Count - 1 - CurrentBars[1];
                bool inTransition = (State == State.Realtime) && (indexOffset > 1);
                bool useCurrent   = State == State.Historical || inTransition || (Calculate != Calculate.OnBarClose);
                int  ti           = useCurrent ? CurrentBars[1] : Math.Min(CurrentBars[1] + 1, BarsArray[1].Count - 1);

                DateTime t   = BarsArray[1].GetTime(ti);
                int      idx = BarsArray[0].GetBar(t);
                if (idx < 0 || idx > CurrentBars[0] + 1) return;
                if (idx < ProcessCutoff() - 5) return;   // build h: session window (5 bars of slack for the tick/bar join)

                if (BarsArray[0].IsFirstBarOfSessionByIndex(idx))
                    sessionOpenPrice = BarsArray[0].GetOpen(idx);
                if (sessionOpenPrice <= 0.0)
                {   // build h: not at a session start - walk back to this session's first bar so the row grid anchors
                    // where the footprint's does (the old fallback took the first PROCESSED bar's open: a grid shifted by
                    // up to cell-1 ticks, which split rows differently from the footprint and flipped close calls)
                    int s = idx;
                    while (s > 0 && !BarsArray[0].IsFirstBarOfSessionByIndex(s)) s--;
                    sessionOpenPrice = BarsArray[0].GetOpen(s);
                }

                double price = BarsArray[1].GetClose(ti);
                double vol   = BarsArray[1].GetVolume(ti);
                double askPx = BarsArray[1].GetAsk(ti);
                double bidPx = BarsArray[1].GetBid(ti);
                bool   isBuy  = (askPx > 0 && price >= askPx);
                bool   isSell = (bidPx > 0 && price <= bidPx);
                if (!isBuy && !isSell) return;

                double cell   = Math.Max(1, PocCellTicks) * TickSize;
                double origin = PocAlignToSessionOpen ? sessionOpenPrice : BarsArray[0].GetLow(idx);
                origin = Math.Round(origin / TickSize) * TickSize;
                double binStart = origin + Math.Floor(((price - origin) / cell) + 1e-9) * cell;
                binStart = Math.Round(binStart / TickSize) * TickSize;

                Dictionary<double, double> a, b;
                if (!binAsk.TryGetValue(idx, out a)) { a = new Dictionary<double, double>(); binAsk[idx] = a; }
                if (!binBid.TryGetValue(idx, out b)) { b = new Dictionary<double, double>(); binBid[idx] = b; }
                double va, vb;
                a.TryGetValue(binStart, out va); b.TryGetValue(binStart, out vb);
                if (isBuy) va += vol; else vb += vol;
                a[binStart] = va; b[binStart] = vb;
            }
            catch (Exception ex)
            {
                DPrint("AccumulateTick: " + ex.Message);
            }
        }

        private void PruneBins()
        {
            int keep = CurrentBar - 400;
            foreach (int k in binAsk.Keys.Where(k => k < keep).ToList()) { binAsk.Remove(k); binBid.Remove(k); }
            while (pocMarkerTags.Count > 600)
            {
                RemoveDrawObject(pocMarkerTags[0]);
                pocMarkerTags.RemoveAt(0);
            }
        }

        // The read of one primary bar for a pivot side. False when the bar has no rows.
        // Row position vs the pivot-side wick: inWick = the row does not reach INTO the body (build k: its body-side
        // tick may sit ON the open/close price, as the footprint draws it; before, that shared tick disqualified the row);
        // pocF = the row centre's position in the wick (0 body .. 1 extreme).
        private void RowInWick(double start, double cell, bool isHigh, double hi, double lo, double bodyHi, double bodyLo,
                               out bool inWick, out double pocF)
        {
            double rowHi  = start + cell - TickSize;
            double centre = start + (cell - TickSize) / 2.0;
            if (isHigh)
            {
                double wick = hi - bodyHi;
                inWick = start >= bodyHi - TickSize * 0.01;   // build k: >= (was >)
                pocF   = wick > 0 ? Math.Max(0.0, Math.Min(1.0, (centre - bodyHi) / wick)) : 0.0;
            }
            else
            {
                double wick = bodyLo - lo;
                inWick = rowHi <= bodyLo + TickSize * 0.01;   // build k: <= (was <)
                pocF   = wick > 0 ? Math.Max(0.0, Math.Min(1.0, (bodyLo - centre) / wick)) : 0.0;
            }
        }

        private bool ReadPocBar(int barIndex, bool isHigh, out bool inWick, out int wickTicks, out double pocF, out double pocLo, out double pocHi, out double vol, out string extra)
        {
            inWick = false; wickTicks = 0; pocF = 0.0; pocLo = 0.0; pocHi = 0.0; vol = 0.0; extra = "";
            Dictionary<double, double> a, b;
            if (!binAsk.TryGetValue(barIndex, out a) || !binBid.TryGetValue(barIndex, out b) || a.Count == 0)
                return false;

            double cell = Math.Max(1, PocCellTicks) * TickSize;
            // Build f: three candidate rows. Build j: ties go to the row nearest the PIVOT (highest tied row on a pivot
            // high, lowest on a pivot low) - the old lowest-wins rule favoured the body on every pivot high (12:36 11/06:
            // -18 in the upper wick lost to +18 at the body bottom). Rows are walked pivot-side first; '>' keeps the first.
            double bestV = -1.0, startV = 0.0;                    // heaviest row (volume POC)
            double bestD = -1.0, startD = 0.0, deltaD = 0.0;      // largest |delta| row (the footprint's Delta-mode box)
            double bestS = double.NegativeInfinity, startS = 0.0, deltaS = 0.0;   // largest delta AGAINST the pivot
            foreach (double start in (isHigh ? a.Keys.OrderByDescending(x => x) : a.Keys.OrderBy(x => x)))
            {
                double vb;
                b.TryGetValue(start, out vb);
                double va = a[start];
                double v  = va + vb;
                double d  = va - vb;
                vol += v;
                if (v > bestV) { bestV = v; startV = start; }
                if (Math.Abs(d) > bestD) { bestD = Math.Abs(d); startD = start; deltaD = d; }
                double against = isHigh ? d : -d;              // buying into a high / selling into a low
                if (against > bestS) { bestS = against; startS = start; deltaS = d; }
            }
            if (bestV < 0) return false;

            int    barsAgo = CurrentBar - barIndex;
            if (barsAgo < 0 || barsAgo > CurrentBar) return false;
            double hi = High[barsAgo], lo = Low[barsAgo];
            double bodyHi = Math.Max(Open[barsAgo], Close[barsAgo]);
            double bodyLo = Math.Min(Open[barsAgo], Close[barsAgo]);
            wickTicks = (int)Math.Round((isHigh ? hi - bodyHi : bodyLo - lo) / TickSize);

            bool   inV, inD, inS; double fV, fD, fS;
            RowInWick(startV, cell, isHigh, hi, lo, bodyHi, bodyLo, out inV, out fV);
            RowInWick(startD, cell, isHigh, hi, lo, bodyHi, bodyLo, out inD, out fD);
            RowInWick(startS, cell, isHigh, hi, lo, bodyHi, bodyLo, out inS, out fS);

            double bestStart;
            switch (PocRowMode)
            {
                case 1:  bestStart = startD; inWick = inD; pocF = fD; break;
                case 2:  bestStart = startS; inWick = inS; pocF = fS; break;
                case 3:  // either row: the one in the wick; both in the wick -> the more extreme (larger pocF), ties -> |delta|
                    if (inD && (!inV || fD >= fV)) { bestStart = startD; inWick = inD; pocF = fD; }
                    else if (inV)                  { bestStart = startV; inWick = inV; pocF = fV; }
                    else                           { bestStart = startD; inWick = false; pocF = fD; }
                    break;
                default: bestStart = startV; inWick = inV; pocF = fV; break;
            }
            pocLo = bestStart;
            pocHi = bestStart + cell - TickSize;

            extra = " rowMode=" + PocRowMode
                  + " vpoc=" + startV.ToString("F2") + "-" + (startV + cell - TickSize).ToString("F2") + " vIn=" + (inV ? 1 : 0) + " vF=" + fV.ToString("F2")
                  + " dpoc=" + startD.ToString("F2") + "-" + (startD + cell - TickSize).ToString("F2") + " dDelta=" + deltaD.ToString("F0") + " dIn=" + (inD ? 1 : 0) + " dF=" + fD.ToString("F2")
                  + " spoc=" + startS.ToString("F2") + "-" + (startS + cell - TickSize).ToString("F2") + " sDelta=" + deltaS.ToString("F0") + " sIn=" + (inS ? 1 : 0) + " sF=" + fS.ToString("F2");
            return true;
        }

        private bool PassesExtreme(bool inWick, int wickTicks, double pocF)
        {
            if (!inWick) return false;
            bool w = wickTicks >= PocMinWickTicks;
            bool p = pocF >= PocExtremeFraction;
            switch (PocExtremeMode)
            {
                case 1:  return w && p;
                case 2:  return p;
                case 3:  return w;
                default: return w || p;
            }
        }

        // One pivot's read over the pivot bar and the bars after it. minAgo = bars the read bar must be behind CurrentBar.
        // Returns the offset of the first qualifying bar (-1 none) and the log lines (one per bar read).
        private int ReadPivot(int idx, int minAgo, string stage, out string lines)
        {
            lines = "";
            int    sigOffset  = -1;
            bool   isHigh     = pivotIsHigh[idx];
            int    barsAgo    = CurrentBar - pivotBars[idx];
            double pivotPrice = pivotPrices[idx];
            double bodyLevel  = BodyLevel(idx);

            for (int off = 0; off <= PocBarsAfterPivot; off++)
            {
                int b = pivotBars[idx] + off;
                if (CurrentBar - b < minAgo) break;
                bool inWick; int wickTicks; double pocF, pocLo, pocHi, vol;
                string extra;
                if (!ReadPocBar(b, isHigh, out inWick, out wickTicks, out pocF, out pocLo, out pocHi, out vol, out extra))
                    continue;
                bool pass = PassesExtreme(inWick, wickTicks, pocF);
                if (pass && sigOffset < 0) sigOffset = off;

                int bAgo = CurrentBar - b;
                lines += (lines.Length > 0 ? Environment.NewLine : "")
                    + "L" + loadId + " [" + State + "] read=" + Time[0].ToString("yyyy-MM-dd HH:mm:ss")
                    + " stage=" + stage + " PIVOT " + (isHigh ? "H" : "L")
                    + " pivotBar=" + Time[barsAgo].ToString("yyyy-MM-dd HH:mm:ss")
                    + " pivot=" + pivotPrice.ToString("F2") + " body=" + bodyLevel.ToString("F2")
                    + " off=" + off + " bar=" + Time[bAgo].ToString("yyyy-MM-dd HH:mm:ss")
                    + " o=" + Open[bAgo].ToString("F2") + " h=" + High[bAgo].ToString("F2") + " l=" + Low[bAgo].ToString("F2") + " c=" + Close[bAgo].ToString("F2")
                    + " wick=" + wickTicks + " pocF=" + pocF.ToString("F2") + " poc=" + pocLo.ToString("F2") + "-" + pocHi.ToString("F2")
                    + " vol=" + vol.ToString("F0") + " inWick=" + (inWick ? 1 : 0) + " pass=" + (pass ? 1 : 0)
                    + " cell=" + PocCellTicks + " mode=" + PocExtremeMode + " minWick=" + PocMinWickTicks + " frac=" + PocExtremeFraction.ToString("F2")
                    + " signal=" + (sigOffset == off ? (isHigh ? "S" : "L") : "-")
                    + extra + " tf=" + tfTag;
            }
            return sigOffset;
        }

        private string TfTag()
        {
            switch (BarsPeriod.BarsPeriodType)
            {
                case BarsPeriodType.Second: return BarsPeriod.Value + "s";
                case BarsPeriodType.Minute: return BarsPeriod.Value + "m";
                case BarsPeriodType.Day:    return BarsPeriod.Value + "d";
                case BarsPeriodType.Tick:   return BarsPeriod.Value + "t";
                case BarsPeriodType.Range:  return BarsPeriod.Value + "r";
                default:                    return BarsPeriod.Value + BarsPeriod.BarsPeriodType.ToString();
            }
        }

        private string PocTag(int signalBar, bool isHigh)
        {
            return pocMarkerPrefix + instanceId + "_" + signalBar + (isHigh ? "H" : "L");
        }

        private void DrawPocDiamond(string tag, int signalBar, bool isHigh, Brush brush)
        {
            int sAgo = CurrentBar - signalBar;
            if (sAgo < 0 || sAgo > CurrentBar) return;
            double y = isHigh ? High[sAgo] + PocMarkerOffsetTicks * TickSize : Low[sAgo] - PocMarkerOffsetTicks * TickSize;
            Draw.Diamond(this, tag, false, sAgo, y, brush);
        }

        private void PocAlert(bool isHigh, double pivotPrice, bool confirmed)
        {
            if (!EnablePocSoundAlerts || State != State.Realtime) return;
            if (!confirmed && !PocAlertOnProvisional) return;
            string id  = Name + "_" + (confirmed ? "Poc" : "PocProv") + (isHigh ? "Short" : "Long") + "_" + CurrentBar + "_" + pivotPrice.ToString("0.#####");
            string msg = "AlightenBiasV0007: POC at the extreme, " + (confirmed ? "CONFIRMED " : "provisional ") + (isHigh ? "short" : "long") + " @ " + pivotPrice.ToString("0.#####");
            Alert(id, confirmed ? Priority.High : Priority.Medium, msg, ResolveSoundPath(isHigh ? PocShortSound : PocLongSound), 0, Brushes.Black, Brushes.White);
        }

        // Drop the provisional mark of one pivot key (its drawing too).
        private void DropProvisional(string key)
        {
            string tag;
            if (!pocProv.TryGetValue(key, out tag)) return;
            RemoveDrawObject(tag);
            pocProv.Remove(key);
        }

        // Drop every provisional mark of one side except the given key: those pivots were replaced by a more extreme bar.
        private void DropReplacedProvisionals(bool isHigh, string keepKey)
        {
            string side = isHigh ? "H" : "L";
            foreach (string k in pocProv.Keys.Where(k => k.EndsWith(side) && k != keepKey).ToList())
                DropProvisional(k);
        }

        // Stage 2 for every confirmed pivot whose bars are complete; stage 1 for the newest (developing) pivot.
        private void EvaluatePocSignals()
        {
            int plot = 0;
            if (!EnablePocSignal) { Values[1][0] = 0; return; }

            int slack = State == State.Realtime ? 0 : 1;     // build c: a closed bar is readable at once in realtime

            // --- CONFIRMED: the opposite pivot has started; read once the bars after the pivot are closed ---
            for (int q = pocPending.Count - 1; q >= 0; q--)
            {
                int idx = pocPending[q];
                if (idx < 0 || idx >= pivotBars.Count) { pocPending.RemoveAt(q); continue; }
                int barsAgo = CurrentBar - pivotBars[idx];
                if (barsAgo < PocBarsAfterPivot + slack) continue;
                pocPending.RemoveAt(q);
                if (barsAgo > CurrentBar) continue;

                bool   isHigh = pivotIsHigh[idx];
                string key    = pivotBars[idx] + (isHigh ? "H" : "L");
                string lines;
                int sigOffset = ReadPivot(idx, slack, "CONF", out lines);

                if (sigOffset >= 0)
                {
                    plot = isHigh ? -2 : 2;
                    int    sb  = pivotBars[idx] + sigOffset;
                    string tag = PocTag(sb, isHigh);
                    string provTag;
                    if (pocProv.TryGetValue(key, out provTag))
                    {
                        if (provTag != tag) RemoveDrawObject(provTag);      // the provisional sat on another bar
                        pocProv.Remove(key);
                    }
                    if (DrawPocMarkers)
                    {
                        DrawPocDiamond(tag, sb, isHigh, isHigh ? PocShortBrush : PocLongBrush);   // same tag: redrawn in place
                        pocMarkerTags.Add(tag);
                    }
                    PocAlert(isHigh, pivotPrices[idx], true);
                }
                else
                    DropProvisional(key);                                   // the complete read disagrees: mark goes away

                if (lines.Length > 0 && (State == State.Realtime || LogHistoricalPivots))
                    PocLog(lines);
            }

            // --- PROVISIONAL: the newest pivot, read at its own close (realtime) ---
            pocProvDir = 0;
            if (ShowPocProvisional && pivotBars.Count > 0)
            {
                int    idx    = pivotBars.Count - 1;
                bool   isHigh = pivotIsHigh[idx];
                string key    = pivotBars[idx] + (isHigh ? "H" : "L");
                DropReplacedProvisionals(isHigh, key);                      // same side, older bar = replaced pivot
                if (CurrentBar - pivotBars[idx] >= slack)
                {
                    string lines;
                    int sigOffset = ReadPivot(idx, slack, "PROV", out lines);
                    if (sigOffset >= 0)
                    {
                        int    sb  = pivotBars[idx] + sigOffset;
                        string tag = PocTag(sb, isHigh);
                        string have;
                        if (!pocProv.TryGetValue(key, out have) || have != tag)
                        {
                            if (have != null) RemoveDrawObject(have);
                            if (DrawPocMarkers) DrawPocDiamond(tag, sb, isHigh, PocProvisionalBrush);
                            pocProv[key] = tag;
                            PocAlert(isHigh, pivotPrices[idx], false);
                            if (lines.Length > 0 && (State == State.Realtime || LogHistoricalPivots))
                                PocLog(lines);
                        }
                        pocProvDir = isHigh ? -1 : 1;
                    }
                    else
                        DropProvisional(key);
                }
            }
            else if (pocProv.Count > 0)
            {
                foreach (string k in pocProv.Keys.ToList()) DropProvisional(k);
            }

            Values[1][0] = plot != 0 ? plot : pocProvDir;
        }

        private string ResolveSoundPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (Path.IsPathRooted(raw)) return raw;
            string install = Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", raw);
            if (File.Exists(install)) return install;
            string user = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds", raw);
            return File.Exists(user) ? user : raw;
        }

        // Same folder as the Mirror's logs (Documents\NinjaTrader 8\Mirror Logs by default).
        private string LogFile(string fileName)
        {
            string folder = string.IsNullOrWhiteSpace(LogFolder) ? "Mirror Logs" : LogFolder.Trim();
            if (!Path.IsPathRooted(folder))
                folder = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, folder);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }

        private void PocLog(string text)
        {
            if (!pocLogOn) return;
            try { File.AppendAllText(LogFile("BiasPocV0007.log"), text + Environment.NewLine); }
            catch { pocLogOn = false; }
        }

        #endregion

        #region Utilities
        private void DPrint(string message) { if (DebugPrints) Print(message); }
        #endregion
    }
}

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
    // =====================================================================
    // Pattern H (V0002 — perf-optimized: no per-tick allocations, O(1) expired-
    // level lookups, LINQ-free rebuild, bounded pivot lookback window)
    // The "flipped Pattern G"
    //
    // SHORT (from a pivot LOW level):
    //   1) The full Pattern G LONG plays out on the level:
    //      close below -> close above (GAINED) -> wick retest with body
    //      above the level = Pattern G long signal.
    //   2) The level is then LOST: a bar closes BELOW the level.
    //      -> Pattern H is ARMED short.
    //      (Re-arming allowed: close back above -> close below again,
    //       until an H signal occurs.)
    //   3) A later bar WICKS the level from below (high >= level) while its
    //      body holds below the level -> H SHORT SIGNAL. Consecutive wick
    //      bars all signal. Once the signal sequence ends (a closed bar no
    //      longer wicks/holds), the level is dead permanently.
    //
    // LONG (from a pivot HIGH level): exact mirror —
    //   Pattern G SHORT signal -> level GAINED (close above) -> wick retest
    //   from above with body above -> H LONG signals.
    //
    // NOTE: The drawn level line starts at the Pattern G signal bar (NOT at
    // the originating pivot).
    //
    // Architecture mirrors AlightenMirrorPtGV0001:
    //   - Same HTF ZigZag pivot engine on BIP 0
    //   - Heavy rebuild on first tick of bar (historical signals)
    //   - Live intrabar signal/level update on every tick
    //   - Same plot layout (Signal, SignalMA, Long/Short Level + timestamps)
    // =====================================================================
    public class AlightenMirrorPtHV0002 : Indicator
    {

		#region Class Variables

        // HTF pivots (BIP 0 only)
        private List<DateTime> pivotTimes;
        private List<double>   pivotPrices;
        private List<bool>     pivotIsHigh;
        private List<double>   pivotGuidePrices;

        private double lastPivotPrice = double.NaN;
        private int    lastPivotType  = 0; // +1 high, -1 low

        // ---- Lookback window index (no list pruning / no RemoveAt churn) ----
        private int lookbackStartPivotIdx = 0;

        // ---- Debug draw internals ----
        private string instanceId;
        private HashSet<string> zzTags;

		// Pattern H results (MA-friendly persistent state)
		// barIndex is primary-series (BIP 0) absolute bar index (0..CurrentBar)
		private Dictionary<int, double> patternHLongByBar;
		private Dictionary<int, double> patternHShortByBar;
		private Dictionary<int, double> patternHLongLevelByBar;
		private Dictionary<int, double> patternHShortLevelByBar;

		// Pattern H derived levels (logical objects; optionally drawn in debug)
		private List<PatternHLevel> patternHLevels;

		// Pattern H ARMED level states as of the last closed bar.
		// Used by the LIVE intrabar update to evaluate the forming bar.
		private List<PatternHLevelState> patternHArmedStates;

		// PERF: reused per-tick scratch lists (avoid allocating two Lists every tick)
		private List<PatternHLevelState> liveLongStatesScratch;
		private List<PatternHLevelState> liveShortStatesScratch;


		// Represents one pivot-derived Pattern H level and its state
		private class PatternHLevelState
		{
		    public int    PivotIdx;         // index into pivot lists
		    public bool   IsLong;           // H direction: pivot HIGH -> H long; pivot LOW -> H short
		    public double Level;            // pivotGuidePrices[PivotIdx] (body/guide price)

		    public int    GSignalBarIndex;  // FIRST Pattern G signal bar (level draw start)

		    public bool   IsActive;         // false once dead (H signal sequence ended / expired)
		    public bool   IsArmed;          // H armed (lost after G long / gained after G short)
		    public int    ArmedBarIndex;    // bar index of the H-arming close (-1 if never armed)

		    public PatternHLevelState()
		    {
		        IsActive        = true;
		        IsArmed         = false;
		        ArmedBarIndex   = -1;
		        GSignalBarIndex = -1;
		    }
		}


		// Represents a horizontal Pattern H level to draw, starting at the
		// Pattern G signal bar and ending at the last H wick/signal bar
		private class PatternHLevel
		{
		    public bool     IsLong;
		    public int      OgBarIndex;    // Pattern G signal bar index (line start)
		    public DateTime OgTime;        // Pattern G signal bar time

		    public int      EntryBarIndex;
		    public DateTime EntryTime;

		    public double   Price; // level (guide)

		    public PatternHLevel() { }
		}

		// ---- LIVE (intrabar) draw tags for Pattern H ----
		private const string PH_LIVE_TAG_L = "PATTERNH_LIVE_TAG_L";
		private const string PH_LIVE_TAG_S = "PATTERNH_LIVE_TAG_S";
		private const string PH_LIVE_LVL_L = "PATTERNH_LIVE_LVL_L";
		private const string PH_LIVE_LVL_S = "PATTERNH_LIVE_LVL_S";

		// --- Pattern H live recency (BIP0) state ---
		private bool phLivePrevLong  = false;
		private bool phLivePrevShort = false;

		private int  phLiveLongLastEdgeSeq  = -1;   // last tick-seq where long became true (false->true)
		private int  phLiveShortLastEdgeSeq = -1;   // last tick-seq where short became true (false->true)
		private int  phLiveTickSeq          = 0;    // increments each BIP0 call within a bar

		private int  phLiveLastChosenDir    = 0;    // +1 long, -1 short, 0 none (stability tie-break)

        private DateTime lastTickTime;
        private DateTime liveLongFirstHitTime;
        private DateTime liveShortFirstHitTime;

		// Helper for safe precise time access
		private double CurrentPreciseTime
		{
		    get
		    {
		        // Valid check for secondary series availability
		        if (BarsArray.Length > 1 && CurrentBars[1] >= 0)
		            return Times[1][0].ToOADate();

		        // Fallback to primary bar time if secondary not ready
		        return Times[0][0].ToOADate();
		    }
		}

        // Persistent storage for live timestamps across bar closes
        private Dictionary<int, double> livePreciseEndTimesL;
        private Dictionary<int, double> livePreciseEndTimesS;

        #endregion

        #region Properties


        [NinjaScriptProperty]
		[Range(0, 500000)]
		[Display(Name="Bars To Process", GroupName="ZigZag", Order=53)]
		public int BarsToProcess { get; set; } = 200; // 0 = process all

		// ---- Pattern H toggles ----


		[NinjaScriptProperty]
		[Display(Name="Enable Pattern H Signals", GroupName="Pattern H", Order=1)]
		public bool EnablePatternHSignal { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name="Enable Pattern H Levels", GroupName="Pattern H", Order=2)]
		public bool EnablePatternHLevels { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name = "Draw Pattern H", GroupName = "Pattern H", Order = 3)]
		public bool DrawPatternH { get; set; } = true;


		// Optional styling knobs (safe defaults)
		[NinjaScriptProperty]
		[Range(6, 30)]
		[Display(Name="Pattern H Marker Font Size", GroupName="Pattern H", Order=20)]
		public int PatternHMarkerFontSize { get; set; } = 18;

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name="Pattern H Marker Offset Ticks", GroupName="Pattern H", Order=21)]
		public int PatternHMarkerOffsetTicks { get; set; } = 5;

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern H Long Color", GroupName="Pattern H", Order=22)]
		public System.Windows.Media.Brush PatternHLongColor { get; set; } = Brushes.Yellow;

		[Browsable(false)]
		public string PatternHLongColorSerialize
		{
		    get => Serialize.BrushToString(PatternHLongColor);
		    set => PatternHLongColor = Serialize.StringToBrush(value);
		}

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern H Short Color", GroupName="Pattern H", Order=23)]
		public System.Windows.Media.Brush PatternHShortColor { get; set; } = Brushes.Orange;

		[Browsable(false)]
		public string PatternHShortColorSerialize
		{
		    get => Serialize.BrushToString(PatternHShortColor);
		    set => PatternHShortColor = Serialize.StringToBrush(value);
		}

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Pattern H Level Width", GroupName="Pattern H", Order=30)]
		public int PatternHLevelWidth { get; set; } = 2;

		[NinjaScriptProperty]
		[Display(Name="Pattern H Level Dash", GroupName="Pattern H", Order=31)]
		public DashStyleHelper PatternHLevelDashStyle { get; set; } = DashStyleHelper.Dash;


        [NinjaScriptProperty]
        [Display(Name="Show Debug Labels", GroupName="Pattern H", Order=40)]
        public bool ShowDebugLabels { get; set; } = false;

        // ---- Debug (chart-only) ZigZag drawing ----

        [NinjaScriptProperty]
        [Display(Name="Show ZigZag (Chart Only)", GroupName="ZigZag", Order=1)]
        public bool ShowZigZag { get; set; } = true;

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="ZigZag Color", GroupName="ZigZag", Order=2)]
        public System.Windows.Media.Brush ZigZagColor { get; set; } = Brushes.Yellow;

        [Browsable(false)]
        public string ZigZagColorSerialize
        {
            get => Serialize.BrushToString(ZigZagColor);
            set => ZigZagColor = Serialize.StringToBrush(value);
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="ZigZag Width", GroupName="ZigZag", Order=3)]
        public int ZigZagWidth { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name="ZigZag Style", GroupName="ZigZag", Order=4)]
        public DashStyleHelper ZigZagStyle { get; set; } = DashStyleHelper.Solid;




		private Series<double> patternHSignal;

		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternHSignal
		{
		    get { return patternHSignal; }
		}

		private Series<double> patternHSignalMA;

		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternHSignalMA
		{
		    get { return patternHSignalMA; }
		}

        private Series<double> patternHLongLevel;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternHLongLevel => patternHLongLevel;

        private Series<double> patternHLongStartTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternHLongStartTimestamp => patternHLongStartTimestamp;

        private Series<double> patternHLongEndTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternHLongEndTimestamp => patternHLongEndTimestamp;

        private Series<double> patternHShortLevel;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternHShortLevel => patternHShortLevel;

        private Series<double> patternHShortStartTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternHShortStartTimestamp => patternHShortStartTimestamp;

        private Series<double> patternHShortEndTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternHShortEndTimestamp => patternHShortEndTimestamp;

		private Series<double> patternHLongSignalBarTimestamp;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternHLongSignalBarTimestamp => patternHLongSignalBarTimestamp;

		private Series<double> patternHShortSignalBarTimestamp;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternHShortSignalBarTimestamp => patternHShortSignalBarTimestamp;

		#endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AlightenMirrorPtHV0002";
                Description = "Pattern H: after a Pattern G signal, the level flips (lost after G long / gained after G short), then wick retest signals the reverse direction.";
                Calculate   = Calculate.OnEachTick;

                // For MA, overlay doesn’t matter. For chart debugging, overlay helps.
                IsOverlay               		= true;
                DrawOnPricePanel        		= true;

                DisplayInDataBox        		= true;
                PaintPriceMarkers       		= false;
                IsSuspendedWhileInactive 		= false;
				ShowTransparentPlotsInDataBox 	= true;


				AddPlot(Brushes.Transparent, "PatternHSignal");
				AddPlot(Brushes.Transparent, "PatternHSignalMA");
				AddPlot(Brushes.Transparent, "PatternHLongLevel");
				AddPlot(Brushes.Transparent, "PatternHLongStartTimestamp");
				AddPlot(Brushes.Transparent, "PatternHLongEndTimestamp");
				AddPlot(Brushes.Transparent, "PatternHShortLevel");
				AddPlot(Brushes.Transparent, "PatternHShortStartTimestamp");
				AddPlot(Brushes.Transparent, "PatternHShortEndTimestamp");
				AddPlot(Brushes.Transparent, "PatternHLongSignalBarTimestamp");
				AddPlot(Brushes.Transparent, "PatternHShortSignalBarTimestamp");

            }
            else if (State == State.Configure)
            {
                instanceId = $"PH_{Guid.NewGuid()}";

                livePreciseEndTimesL = new Dictionary<int, double>();
                livePreciseEndTimesS = new Dictionary<int, double>();
            }
            else if (State == State.DataLoaded)
            {

                pivotTimes       				= new List<DateTime>(Math.Min(400, Math.Max(16, BarsToProcess)));
                pivotPrices      				= new List<double>(Math.Min(400, Math.Max(16, BarsToProcess)));
                pivotIsHigh      				= new List<bool>(Math.Min(400, Math.Max(16, BarsToProcess)));
                pivotGuidePrices 				= new List<double>(Math.Min(400, Math.Max(16, BarsToProcess)));

                zzTags 							= new HashSet<string>();

				patternHLongByBar  				= new Dictionary<int, double>(256);
				patternHShortByBar 				= new Dictionary<int, double>(256);
				patternHLongLevelByBar			= new Dictionary<int, double>(256);
				patternHShortLevelByBar			= new Dictionary<int, double>(256);
				patternHLevels     				= new List<PatternHLevel>(128);
				patternHArmedStates				= new List<PatternHLevelState>(128);
				liveLongStatesScratch			= new List<PatternHLevelState>(32);
				liveShortStatesScratch			= new List<PatternHLevelState>(32);

                lastTickTime = DateTime.MinValue;

				patternHSignal					= Values[0];
				patternHSignalMA				= Values[1];
                patternHLongLevel               = Values[2];
                patternHLongStartTimestamp      = Values[3];
                patternHLongEndTimestamp        = Values[4];
                patternHShortLevel              = Values[5];
                patternHShortStartTimestamp     = Values[6];
                patternHShortEndTimestamp       = Values[7];
				patternHLongSignalBarTimestamp  = Values[8];
				patternHShortSignalBarTimestamp = Values[9];

            }
            else if (State == State.Terminated)
            {
                if (zzTags != null)
                {
                    foreach (var t in zzTags)
                        RemoveDrawObject(t);
                    zzTags.Clear();
                }
            }
        }

        protected override void OnBarUpdate()
		{
            if (BarsInProgress == 1)
            {
                lastTickTime = Times[1][0];
                return;
            }

		    try
		    {
		        // ---- BIP 0 only (this indicator is single-series right now) ----
		        if (BarsInProgress != 0)
		            return;

		        if (patternHSignal == null || patternHSignalMA == null)
		            return;

		        // Ultra-fast early-exit windowing — keep this for heavy work only
		        bool allowHeavy = IsFirstTickOfBar;
		        if (allowHeavy && BarsToProcess > 0)
		        {
		            int totalLoaded = BarsArray[0].Count;
		            int cutoffBar   = totalLoaded - BarsToProcess;
		            if (CurrentBars[0] < cutoffBar)
		                return;
		        }

		        if (CurrentBar < 1)
		        {
		            // Intrabar-safe clearing
		            patternHSignal[0]   = 0;
		            patternHSignalMA[0] = 0;

		            patternHLongLevel[0]          = 0;
		            patternHLongStartTimestamp[0] = 0;
		            patternHLongEndTimestamp[0]   = 0;

		            patternHShortLevel[0]          = 0;
		            patternHShortStartTimestamp[0] = 0;
		            patternHShortEndTimestamp[0]   = 0;

					patternHLongSignalBarTimestamp[0]  = 0;
					patternHShortSignalBarTimestamp[0] = 0;

		            if (CanDrawDebug() && DrawPatternH)
		            {
		                RemoveDrawObject(PH_LIVE_TAG_L);
		                RemoveDrawObject(PH_LIVE_TAG_S);
		                RemoveDrawObject(PH_LIVE_LVL_L);
		                RemoveDrawObject(PH_LIVE_LVL_S);
		            }
		            return;
		        }

		        // -----------------------------
		        // 1) BAR-CLOSE semantics
		        //    Only do the heavy rebuild on first tick of bar.
		        // -----------------------------
		        if (allowHeavy)
		        {
		            if (CurrentBar < 3)
		                return;

		            ProcessClosedBarPivot_BIP0();  // uses [1]
		            RebuildPatternH_BIP0();

		            if (EnablePatternHSignal)
		            {
		                // Default clear (current bar)
		                patternHSignal[0]   = 0;
		                patternHSignalMA[0] = 0;

		                patternHLongLevel[0]          = 0;
		                patternHLongStartTimestamp[0] = 0;
		                patternHLongEndTimestamp[0]   = 0;

		                patternHShortLevel[0]          = 0;
		                patternHShortStartTimestamp[0] = 0;
		                patternHShortEndTimestamp[0]   = 0;

						patternHLongSignalBarTimestamp[0]  = 0;
						patternHShortSignalBarTimestamp[0] = 0;

						// Clear inherited live values from previous bar's intra-bar updates
						patternHLongLevel[1]  = 0;
						patternHShortLevel[1] = 0;

		                if (CurrentBars[0] >= 1)
		                {
		                    int sigBar = CurrentBars[0] - 1;

		                    bool longNow  = patternHLongByBar  != null && patternHLongByBar.ContainsKey(sigBar);
		                    bool shortNow = patternHShortByBar != null && patternHShortByBar.ContainsKey(sigBar);

		                    double sig = 0;
		                    if (longNow && !shortNow)
		                    {
		                        sig = +1;
		                        if (patternHLongLevelByBar != null && patternHLongLevelByBar.TryGetValue(sigBar, out double lvlPrice))
		                            patternHLongLevel[1] = lvlPrice;
		                    }
		                    else if (shortNow && !longNow)
		                    {
		                        sig = -1;
		                        if (patternHShortLevelByBar != null && patternHShortLevelByBar.TryGetValue(sigBar, out double lvlPrice))
		                            patternHShortLevel[1] = lvlPrice;
		                    }

		                    // DataBox / chart-aligned plot (belongs to bar that just closed)
		                    patternHSignal[1] = sig;

		                    // MA-friendly plot (belongs to current bar so MA reads [0] reliably)
		                    patternHSignalMA[0] = sig;
		                }
		            }
		        }

		        // -----------------------------
		        // 2) LIVE intrabar semantics
		        //    Update on EVERY tick so it can appear/disappear while bar forms.
		        // -----------------------------
		        if (EnablePatternHSignal)
		        {
		            UpdatePatternH_Live_BIP0();
		        }
		    }
		    catch (Exception ex)
		    {
		        Print(string.Format("[PH EX][OnBarUpdate] t={0:yyyy-MM-dd HH:mm:ss} BIP={1} CB0={2} CurrentBar={3} Msg={4}\nStack={5}",
		            Times[0][0], BarsInProgress, (CurrentBars != null && CurrentBars.Length > 0) ? CurrentBars[0] : -1,
		            CurrentBar, ex.Message, ex.StackTrace));
		    }
		}



        #region ZigZag (HTF on BIP 0)

		private void ProcessClosedBarPivot_BIP0()
		{
		    // Called only when IsFirstTickOfBar == true on BIP 0,
		    // so bar [1] is the bar that just closed.

		    if (CurrentBar < 3)
		        return;

		    // Just-closed bar = [1], prior closed bar = [2]
		    double h0 = High[1];
		    double h1 = High[2];
		    double l0 = Low[1];
		    double l1 = Low[2];
		    double o0 = Open[1];
		    double o1 = Open[2];
		    double c0 = Close[1];
		    double c1 = Close[2];

		    bool isHigh        = h0 > h1;
		    bool isLow         = l0 < l1;
		    bool previousGreen = c1 >= o1;
		    bool previousRed   = c1 <  o1;
		    bool currentGreen  = c0 >= o0;
		    bool currentRed    = c0 <  o0;

		    // Use the closed bar's time
		    DateTime time = Times[0][1];

		    double bodyHigh = Math.Max(o0, c0);
		    double bodyLow  = Math.Min(o0, c0);

		    if (previousGreen && currentGreen && isHigh) ProcessPivot(time, h0, true,  bodyHigh);
		    if (previousRed   && currentRed   && isLow ) ProcessPivot(time, l0, false, bodyLow );
		    if (previousGreen && currentRed   && isHigh) ProcessPivot(time, h0, true,  bodyHigh);
		    if (previousRed   && currentGreen && isLow ) ProcessPivot(time, l0, false, bodyLow );
		    if (previousRed   && currentGreen && isHigh) ProcessPivot(time, h0, true,  bodyHigh);
		    if (previousGreen && currentRed   && isLow ) ProcessPivot(time, l0, false, bodyLow );

		    // No pivot created => do nothing
		}


        private void ProcessPivot(DateTime time, double price, bool isHigh, double guidePrice)
        {
            // Basic alternating-pivot ZigZag:
            // - First pivot: add
            // - Same-side pivot: replace if more extreme
            // - Opposite-side pivot: append
            int n = pivotTimes.Count;

            if (n == 0)
            {
                AddPivot(time, price, isHigh, guidePrice);
                return;
            }

            bool lastIsHigh  = pivotIsHigh[n - 1];
            double lastPrice = pivotPrices[n - 1];

            if (lastIsHigh == isHigh)
            {
                if (isHigh && price > lastPrice)
                    ReplaceLastPivot(time, price, isHigh, guidePrice);
                else if (!isHigh && price < lastPrice)
                    ReplaceLastPivot(time, price, isHigh, guidePrice);

                return; // ignore weaker same-side pivot
            }

            AddPivot(time, price, isHigh, guidePrice);
        }

        private void AddPivot(DateTime time, double price, bool isHigh, double guidePrice)
        {
            pivotTimes.Add(time);
            pivotPrices.Add(price);
            pivotIsHigh.Add(isHigh);
            pivotGuidePrices.Add(guidePrice);

            lastPivotPrice = price;
            lastPivotType  = isHigh ? 1 : -1;

            DrawOrUpdateLastSegment();
        }

        private void ReplaceLastPivot(DateTime time, double price, bool isHigh, double guidePrice)
        {
            int idx = pivotTimes.Count - 1;

            pivotTimes[idx]       = time;
            pivotPrices[idx]      = price;
            pivotIsHigh[idx]      = isHigh;
            pivotGuidePrices[idx] = guidePrice;

            lastPivotPrice = price;
            lastPivotType  = isHigh ? 1 : -1;

            DrawOrUpdateReplacedLastSegment();
        }

        #endregion

		#region Pattern H

		private void UpdatePatternH_Live_BIP0()
		{
		    // Per-bar reset for recency tracking (BIP0)
		    if (IsFirstTickOfBar)
		    {
		        phLivePrevLong           = false;
		        phLivePrevShort          = false;
		        phLiveLongLastEdgeSeq    = -1;
		        phLiveShortLastEdgeSeq   = -1;
		        phLiveTickSeq            = 0;
		        phLiveLastChosenDir      = 0;
		    }
		    else
		    {
		        // advance intra-bar tick sequence
		        phLiveTickSeq++;
		    }

		    // Safety
		    if (CurrentBar < 1)
		    {
		        patternHSignal[0] = 0;

		        // Reset edge state to avoid carrying across early exits
		        phLivePrevLong  = false;
		        phLivePrevShort = false;

		        if (CanDrawDebug() && DrawPatternH)
		        {
		            RemoveDrawObject(PH_LIVE_TAG_L);
		            RemoveDrawObject(PH_LIVE_TAG_S);
		            RemoveDrawObject(PH_LIVE_LVL_L);
		            RemoveDrawObject(PH_LIVE_LVL_S);
		        }
		        return;
		    }

		    // If armed states haven't been built yet, just clear live.
		    if (patternHArmedStates == null || patternHArmedStates.Count == 0)
		    {
		        patternHSignal[0] = 0;

		        // Reset edge state to avoid carrying across early exits
		        phLivePrevLong  = false;
		        phLivePrevShort = false;

		        if (CanDrawDebug() && DrawPatternH)
		        {
		            RemoveDrawObject(PH_LIVE_TAG_L);
		            RemoveDrawObject(PH_LIVE_TAG_S);
		        }
		        return;
		    }

		    double eps = TickSize * 0.5;

		    // Current forming bar (BIP0) intrabar state
		    double o0 = Open[0];
		    double c0 = Close[0];
		    double h0 = High[0];
		    double l0 = Low[0];

		    double bodyHigh = Math.Max(o0, c0);
		    double bodyLow  = Math.Min(o0, c0);

		    bool liveLong  = false;
		    bool liveShort = false;

            var liveLongStates  = liveLongStatesScratch;
            var liveShortStates = liveShortStatesScratch;
            liveLongStates.Clear();
            liveShortStates.Clear();

		    foreach (var st in patternHArmedStates)
		    {
		        if (st == null) continue;
		        if (!st.IsActive || !st.IsArmed) continue;

                if (st.IsLong)
                {
                    double level = st.Level;
                    // LONG: wick touches level + body strictly above level
	                bool wickTouches = l0 <= level + eps;
	                bool bodyAbove   = bodyLow >= level - eps;

	                if (wickTouches && bodyAbove)
	                {
                        liveLong = true;
                        liveLongStates.Add(st);
                    }
                }
                else
                {
                    double level = st.Level;
                    // SHORT: wick touches level + body strictly below level
	                bool wickTouches = h0 >= level - eps;
	                bool bodyBelow   = bodyHigh <= level + eps;

	                if (wickTouches && bodyBelow)
	                {
                        liveShort = true;
                        liveShortStates.Add(st);
                    }
                }
            }

		    // --- Recency tracking: capture false->true edges for each side on this bar ---
		    if (liveLong && !phLivePrevLong)
		        phLiveLongLastEdgeSeq = phLiveTickSeq;

		    if (liveShort && !phLivePrevShort)
		        phLiveShortLastEdgeSeq = phLiveTickSeq;

		    // Update prev flags after edge detection
		    phLivePrevLong  = liveLong;
		    phLivePrevShort = liveShort;

		    // --- Choose liveSig ---
		    double liveSig = 0;


		    if (liveLong && !liveShort)
		    {
		        liveSig = +1;
		        phLiveLastChosenDir = +1;
		    }
		    else if (liveShort && !liveLong)
		    {
		        liveSig = -1;
		        phLiveLastChosenDir = -1;
		    }
		    else if (liveLong && liveShort)
		    {
		        // Recency wins
		        if (phLiveLongLastEdgeSeq > phLiveShortLastEdgeSeq)
		        {
		            liveSig = +1;
		            phLiveLastChosenDir = +1;
		        }
		        else if (phLiveShortLastEdgeSeq > phLiveLongLastEdgeSeq)
		        {
		            liveSig = -1;
		            phLiveLastChosenDir = -1;
		        }
		        else
		        {
                    // Tie-break: Max Penetration
		            double maxLongPen  = 0;
		            double maxShortPen = 0;

                    foreach(var st in liveLongStates)
		            {
		                double lvl = st.Level;
                        double p = (lvl - l0) / TickSize;
                        if (p > maxLongPen) maxLongPen = p;
		            }
                    foreach(var st in liveShortStates)
		            {
		                double lvl = st.Level;
                        double p = (h0 - lvl) / TickSize;
                        if (p > maxShortPen) maxShortPen = p;
		            }

		            if (maxLongPen > maxShortPen + 0.0001)
		            {
		                liveSig = +1;
		                phLiveLastChosenDir = +1;
		            }
		            else if (maxShortPen > maxLongPen + 0.0001)
		            {
		                liveSig = -1;
		                phLiveLastChosenDir = -1;
		            }
		            else
		            {
		                // Still tied: stabilize
		                if (phLiveLastChosenDir == +1) liveSig = +1;
		                else if (phLiveLastChosenDir == -1) liveSig = -1;
                        else liveSig = 0;
		            }
		        }
		    }
		    else
		    {
		        liveSig = 0;
		        phLiveLastChosenDir = 0;
		    }

            if (CurrentBars[0] > 0)
            {
                int sigBar = CurrentBars[0];
                DateTime now = lastTickTime != DateTime.MinValue ? lastTickTime : Times[0][0];

                if (liveSig == +1 && liveLongStates.Count > 0)
                {
                    if (!livePreciseEndTimesL.ContainsKey(sigBar))
                    {
                        double preciseTime = CurrentPreciseTime;
                        livePreciseEndTimesL[sigBar] = preciseTime;
                    }

                    if (phLiveLongLastEdgeSeq == phLiveTickSeq || liveLongFirstHitTime == DateTime.MinValue)
                        liveLongFirstHitTime = now;

                    patternHLongLevel[0] = liveLongStates[0].Level;
                    patternHLongStartTimestamp[0] = liveLongFirstHitTime.ToOADate();
                    patternHLongEndTimestamp[0] = now.ToOADate();
					patternHLongSignalBarTimestamp[0] = Time[0].ToOADate();
                }
                else
                {
                    patternHLongLevel[0] = 0;
                    patternHLongStartTimestamp[0] = 0;
                    patternHLongEndTimestamp[0] = 0;
					patternHLongSignalBarTimestamp[0] = 0;
                    liveLongFirstHitTime = DateTime.MinValue;
                }

                if (liveSig == -1 && liveShortStates.Count > 0)
                {
                    if (!livePreciseEndTimesS.ContainsKey(sigBar))
                    {
                        double preciseTime = CurrentPreciseTime;
                        livePreciseEndTimesS[sigBar] = preciseTime;
                    }

                    if (phLiveShortLastEdgeSeq == phLiveTickSeq || liveShortFirstHitTime == DateTime.MinValue)
                        liveShortFirstHitTime = now;

                    patternHShortLevel[0] = liveShortStates[0].Level;
                    patternHShortStartTimestamp[0] = liveShortFirstHitTime.ToOADate();
                    patternHShortEndTimestamp[0] = now.ToOADate();
					patternHShortSignalBarTimestamp[0] = Time[0].ToOADate();
                }
                else
                {
                    patternHShortLevel[0] = 0;
                    patternHShortStartTimestamp[0] = 0;
                    patternHShortEndTimestamp[0] = 0;
					patternHShortSignalBarTimestamp[0] = 0;
                    liveShortFirstHitTime = DateTime.MinValue;
                }
            }

		    // LIVE plot (chart):
		    patternHSignal[0] = liveSig;

		    // --- Draw live artifacts ---
		    if (CanDrawDebug() && DrawPatternH)
		    {
                // Fixed set of recycled live-line tags up to a max (clear all slots first)
                int MAX_LIVE_LINES = 20;

		        RemoveDrawObject(PH_LIVE_TAG_L);
		        RemoveDrawObject(PH_LIVE_TAG_S);

                // Clear all slots
                for(int i=0; i<MAX_LIVE_LINES; i++)
                {
                    RemoveDrawObject(PH_LIVE_LVL_L + "_" + i);
                    RemoveDrawObject(PH_LIVE_LVL_S + "_" + i);
                }

		        // Draw LONG live
		        if (liveLong && liveLongStates.Count > 0)
		        {
		            double y = l0 - (PatternHMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PH_LIVE_TAG_L, false,
		                "▲",
		                0,
		                y,
		                0,
		                PatternHLongColor,
		                new SimpleFont("Arial", PatternHMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);

                    for(int i=0; i < liveLongStates.Count && i < MAX_LIVE_LINES; i++)
                    {
                        var st = liveLongStates[i];
                        double level = st.Level;

                        // Level line starts at the Pattern G signal bar (NOT the pivot)
		                if (st.GSignalBarIndex >= 0)
		                {
		                    int gBarsAgo = CurrentBars[0] - st.GSignalBarIndex;
		                    if (gBarsAgo >= 0)
		                    {
		                        Draw.Line(this, PH_LIVE_LVL_L + "_" + i, false,
		                            gBarsAgo, level,
		                            0,        level,
		                            PatternHLongColor, PatternHLevelDashStyle, PatternHLevelWidth);
		                    }
		                }
                    }
		        }

		        // Draw SHORT live
		        if (liveShort && liveShortStates.Count > 0)
		        {
		            double y = h0 + (PatternHMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PH_LIVE_TAG_S, false,
		                "▼",
		                0,
		                y,
		                0,
		                PatternHShortColor,
		                new SimpleFont("Arial", PatternHMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);

                    for(int i=0; i < liveShortStates.Count && i < MAX_LIVE_LINES; i++)
                    {
                        var st = liveShortStates[i];
                        double level = st.Level;

                        // Level line starts at the Pattern G signal bar (NOT the pivot)
		                if (st.GSignalBarIndex >= 0)
		                {
		                    int gBarsAgo = CurrentBars[0] - st.GSignalBarIndex;
		                    if (gBarsAgo >= 0)
		                    {
		                        Draw.Line(this, PH_LIVE_LVL_S + "_" + i, false,
		                            gBarsAgo, level,
		                            0,        level,
		                            PatternHShortColor, PatternHLevelDashStyle, PatternHLevelWidth);
		                    }
		                }
                    }
		        }
		    }
		}



		private void RebuildPatternH_BIP0()
		{
		    if (!EnablePatternHSignal && !EnablePatternHLevels)
		        return;

		    BuildPatternH(out var newPHLong, out var newPHShort, out var newPHLongLevel, out var newPHShortLevel, out var newPHLevels);

		    ApplyPatternHResults(newPHLong, newPHShort, newPHLongLevel, newPHShortLevel, newPHLevels);

		    // chart-only
		    if (DrawPatternH && CanDrawDebug())
		        DrawPatternHDebug(newPHLong, newPHShort, newPHLevels);
		}

		private void BuildPatternH(
		    out Dictionary<int, double> newLongByBar,
		    out Dictionary<int, double> newShortByBar,
		    out Dictionary<int, double> newLongLevelByBar,
		    out Dictionary<int, double> newShortLevelByBar,
		    out List<PatternHLevel> newLevels)
		{
		    newLongByBar  = new Dictionary<int, double>();
		    newShortByBar = new Dictionary<int, double>();
		    newLongLevelByBar = new Dictionary<int, double>();
		    newShortLevelByBar = new Dictionary<int, double>();
		    newLevels     = new List<PatternHLevel>();

		    if ((!EnablePatternHSignal && !EnablePatternHLevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 1)
		        return;

		    ScanPatternH(newLongByBar, newShortByBar, newLongLevelByBar, newShortLevelByBar, newLevels);
		}


		private void ApplyPatternHResults(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    Dictionary<int, double> newLongLevelByBar,
		    Dictionary<int, double> newShortLevelByBar,
		    List<PatternHLevel> newLevels)
		{
		    if (patternHLongByBar == null)
		        patternHLongByBar = new Dictionary<int, double>();
		    if (patternHShortByBar == null)
		        patternHShortByBar = new Dictionary<int, double>();
		    if (patternHLongLevelByBar == null)
		        patternHLongLevelByBar = new Dictionary<int, double>();
		    if (patternHShortLevelByBar == null)
		        patternHShortLevelByBar = new Dictionary<int, double>();
		    if (patternHLevels == null)
		        patternHLevels = new List<PatternHLevel>();

		    // Signals
		    if (EnablePatternHSignal)
		    {
		        patternHLongByBar.Clear();
		        foreach (var kv in newLongByBar)
		            patternHLongByBar[kv.Key] = kv.Value;

		        patternHShortByBar.Clear();
		        foreach (var kv in newShortByBar)
		            patternHShortByBar[kv.Key] = kv.Value;

		        patternHLongLevelByBar.Clear();
		        foreach (var kv in newLongLevelByBar)
		            patternHLongLevelByBar[kv.Key] = kv.Value;

		        patternHShortLevelByBar.Clear();
		        foreach (var kv in newShortLevelByBar)
		            patternHShortLevelByBar[kv.Key] = kv.Value;
		    }
		    else
		    {
		        patternHLongByBar.Clear();
		        patternHShortByBar.Clear();
		        patternHLongLevelByBar.Clear();
		        patternHShortLevelByBar.Clear();
		    }

		    // Levels
		    if (EnablePatternHLevels)
		    {
		        patternHLevels.Clear();
		        patternHLevels.AddRange(newLevels);
		    }
		    else
		    {
		        patternHLevels.Clear();
		    }
		}


        // Helper class to collect potential signals before filtering
        private class PatternHCandidate
        {
            public int      BarIndex;
            public bool     IsLong;
            public double   LevelPrice;
            public double   SignalPrice;
            public int      PivotIdx;
            public PatternHLevelState State;
        }

		// =========================
		// Pattern H scan
		// =========================
		// For each pivot, derive a level (guide price) and run the extended
		// gain/lose reclaim state machine across closed bars:
		//
		// (Described for a pivot LOW; pivot HIGH is the exact mirror.)
		//   phase 0: waiting for a CLOSE below the level (far side)
		//   phase 1: far side seen; waiting for the reclaiming CLOSE above
		//            (= GAINED, Pattern G armed)
		//   phase 2: G ARMED. Bars that wick the level with body above are
		//            Pattern G LONG signal bars (internal only, not plotted).
		//            - Close back below BEFORE any G signal -> back to phase 1
		//              (G re-arm allowed).
		//            - Once a G signal has occurred: a close BELOW the level
		//              arms Pattern H short (phase 4); any other non-signal
		//              bar moves to phase 3.
		//   phase 3: G signaled; level still gained. Waiting for the flip:
		//            a CLOSE below the level -> phase 4.
		//   phase 4: H ARMED (short). Bars that wick the level from below
		//            (high >= level) with body below -> H SHORT SIGNALS
		//            (consecutive bars all signal).
		//            - Close back above BEFORE any H signal -> back to phase 3
		//              (H re-arm allowed).
		//            - Once an H signal sequence has started, the first closed
		//              bar that no longer satisfies wick+body kills the level
		//              permanently.
		private void ScanPatternH(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    Dictionary<int, double> newLongLevelByBar,
		    Dictionary<int, double> newShortLevelByBar,
		    List<PatternHLevel> newLevels)
		{
		    patternHArmedStates.Clear();
            var candidates = new List<PatternHCandidate>();

            // PERF: expired levels keyed by tick-rounded price -> O(1) lookups
            HashSet<long> expiredLevelsL = new HashSet<long>();
            HashSet<long> expiredLevelsS = new HashSet<long>();

		    if ((!EnablePatternHSignal && !EnablePatternHLevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 1)
		        return;

		    int pivotCount = pivotTimes.Count;
		    double eps = TickSize * 0.5;

		    long LevelKey(double lvl) { return (long)Math.Round(lvl / TickSize); }

		    // PERF: advance the lookback window start so long-running sessions do not
		    // rescan pivots that have fallen out of the BarsToProcess window.
		    if (BarsToProcess > 0)
		    {
		        int cutoffBar = BarsArray[0].Count - BarsToProcess;
		        while (lookbackStartPivotIdx < pivotCount
		            && BarsArray[0].GetBar(pivotTimes[lookbackStartPivotIdx]) < cutoffBar)
		            lookbackStartPivotIdx++;
		    }

		    // Map pivot index -> primary (BIP0) bar index once (window only)
		    int[] pivotBarIndex = new int[pivotCount];
		    for (int i = lookbackStartPivotIdx; i < pivotCount; i++)
		        pivotBarIndex[i] = BarsArray[0].GetBar(pivotTimes[i]);

		    // Last CLOSED bar index. The current forming bar (CurrentBars[0]) is
		    // handled exclusively by the LIVE intrabar update.
		    int lastClosedBar = CurrentBars[0] - 1;

		    for (int idx = lookbackStartPivotIdx; idx < pivotCount; idx++)
		    {
		        int pivotBar = pivotBarIndex[idx];
		        if (pivotBar < 0)
		            continue;

		        // H direction: pivot HIGH -> (G short -> gained) -> H LONG
		        //              pivot LOW  -> (G long  -> lost)   -> H SHORT
		        bool   hIsLong = pivotIsHigh[idx];
		        double level   = pivotGuidePrices[idx];   // body/guide price

		        // Skip if this price level already expired via a completed H signal sequence
		        if (hIsLong  && expiredLevelsL.Contains(LevelKey(level))) continue;
		        if (!hIsLong && expiredLevelsS.Contains(LevelKey(level))) continue;

		        var st = new PatternHLevelState
		        {
		            PivotIdx = idx,
		            IsLong   = hIsLong,
		            Level    = level
		        };

		        int  phase             = 0;     // see state machine comment above
		        bool gSignalOccurred   = false;
		        bool hSequenceStarted  = false;
		        bool dead              = false;

		        // NOTE: the expired-price check happens once before this loop — the expired
		        // sets cannot change while this level is being scanned (a level only adds
		        // its own price on death, then breaks).
		        for (int barIndex = pivotBar + 1; barIndex <= lastClosedBar; barIndex++)
		        {
		            int barsAgo = CurrentBars[0] - barIndex;
		            if (barsAgo < 0) continue;

		            double o  = Opens[0][barsAgo];
		            double c  = Closes[0][barsAgo];
		            double hi = Highs[0][barsAgo];
		            double lo = Lows[0][barsAgo];

		            double bodyLow  = Math.Min(o, c);
		            double bodyHigh = Math.Max(o, c);

		            if (!hIsLong)
		            {
		                // ---- Pivot LOW: G long -> lost -> H SHORT ----
		                if (phase == 0)
		                {
		                    if (c < level - eps)
		                        phase = 1;                       // far side (below) seen
		                }
		                else if (phase == 1)
		                {
		                    if (c > level + eps)
		                        phase = 2;                       // GAINED (G armed)
		                }
		                else if (phase == 2)
		                {
		                    bool wickTouches = lo <= level + eps;
		                    bool bodyAbove   = bodyLow >= level - eps;

		                    if (wickTouches && bodyAbove)
		                    {
		                        // Pattern G LONG signal bar (internal only)
		                        gSignalOccurred = true;
		                        if (st.GSignalBarIndex < 0)
		                            st.GSignalBarIndex = barIndex;
		                    }
		                    else if (gSignalOccurred)
		                    {
		                        // G sequence ended; watch for the flip (loss)
		                        if (c < level - eps)
		                        {
		                            phase = 4;                   // LOST -> H armed short
		                            st.ArmedBarIndex = barIndex;
		                        }
		                        else
		                            phase = 3;
		                    }
		                    else
		                    {
		                        // No G signal yet: closing back below dis-arms G (re-arm allowed)
		                        if (c < level - eps)
		                            phase = 1;
		                    }
		                }
		                else if (phase == 3)
		                {
		                    // G signaled; waiting for the level to be LOST
		                    if (c < level - eps)
		                    {
		                        phase = 4;                       // H armed short
		                        st.ArmedBarIndex = barIndex;
		                    }
		                }
		                else // phase == 4 (H armed short)
		                {
		                    bool wickTouches = hi >= level - eps;
		                    bool bodyBelow   = bodyHigh <= level + eps;

		                    if (wickTouches && bodyBelow)
		                    {
		                        // H SHORT SIGNAL bar (consecutive wick bars all signal)
		                        hSequenceStarted = true;
		                        candidates.Add(new PatternHCandidate
		                        {
		                            BarIndex    = barIndex,
		                            IsLong      = false,
		                            LevelPrice  = level,
		                            SignalPrice = hi + (PatternHMarkerOffsetTicks * TickSize),
		                            PivotIdx    = idx,
		                            State       = st
		                        });
		                    }
		                    else
		                    {
		                        if (hSequenceStarted)
		                        {
		                            // Sequence ended on a closed bar -> level dead permanently
		                            dead = true;
		                            expiredLevelsS.Add(LevelKey(level));
		                            break;
		                        }

		                        // No H signal yet: closing back above dis-arms (re-arm allowed)
		                        if (c > level + eps)
		                            phase = 3;
		                        // else: still armed, keep waiting for the wick retest
		                    }
		                }
		            }
		            else
		            {
		                // ---- Pivot HIGH: G short -> gained -> H LONG ----
		                if (phase == 0)
		                {
		                    if (c > level + eps)
		                        phase = 1;                       // far side (above) seen
		                }
		                else if (phase == 1)
		                {
		                    if (c < level - eps)
		                        phase = 2;                       // LOST (G armed short)
		                }
		                else if (phase == 2)
		                {
		                    bool wickTouches = hi >= level - eps;
		                    bool bodyBelow   = bodyHigh <= level + eps;

		                    if (wickTouches && bodyBelow)
		                    {
		                        // Pattern G SHORT signal bar (internal only)
		                        gSignalOccurred = true;
		                        if (st.GSignalBarIndex < 0)
		                            st.GSignalBarIndex = barIndex;
		                    }
		                    else if (gSignalOccurred)
		                    {
		                        // G sequence ended; watch for the flip (gain)
		                        if (c > level + eps)
		                        {
		                            phase = 4;                   // GAINED -> H armed long
		                            st.ArmedBarIndex = barIndex;
		                        }
		                        else
		                            phase = 3;
		                    }
		                    else
		                    {
		                        // No G signal yet: closing back above dis-arms G (re-arm allowed)
		                        if (c > level + eps)
		                            phase = 1;
		                    }
		                }
		                else if (phase == 3)
		                {
		                    // G signaled; waiting for the level to be GAINED
		                    if (c > level + eps)
		                    {
		                        phase = 4;                       // H armed long
		                        st.ArmedBarIndex = barIndex;
		                    }
		                }
		                else // phase == 4 (H armed long)
		                {
		                    bool wickTouches = lo <= level + eps;
		                    bool bodyAbove   = bodyLow >= level - eps;

		                    if (wickTouches && bodyAbove)
		                    {
		                        // H LONG SIGNAL bar (consecutive wick bars all signal)
		                        hSequenceStarted = true;
		                        candidates.Add(new PatternHCandidate
		                        {
		                            BarIndex    = barIndex,
		                            IsLong      = true,
		                            LevelPrice  = level,
		                            SignalPrice = lo - (PatternHMarkerOffsetTicks * TickSize),
		                            PivotIdx    = idx,
		                            State       = st
		                        });
		                    }
		                    else
		                    {
		                        if (hSequenceStarted)
		                        {
		                            // Sequence ended on a closed bar -> level dead permanently
		                            dead = true;
		                            expiredLevelsL.Add(LevelKey(level));
		                            break;
		                        }

		                        // No H signal yet: closing back below dis-arms (re-arm allowed)
		                        if (c < level - eps)
		                            phase = 3;
		                        // else: still armed, keep waiting for the wick retest
		                    }
		                }
		            }
		        }

		        st.IsActive = !dead;
		        st.IsArmed  = !dead && phase == 4;

		        // Armed & alive as of the last closed bar -> LIVE candidate on the forming bar
		        if (st.IsActive && st.IsArmed)
		            patternHArmedStates.Add(st);
		    }

            // --- FILTERING STEP (PERF: single pass, no LINQ/group allocations) ---
            // Overwrite semantics match the previous GroupBy implementation:
            // the last candidate written for a bar+direction wins the plot slot.
            // Candidates are appended in ascending bar order per state, so the
            // last write per state is also its latest signal in the sequence.
            var lastCandidateByState = new Dictionary<PatternHLevelState, PatternHCandidate>();

            if (EnablePatternHSignal)
            {
                foreach (var cand in candidates)
                {
                    if (cand.IsLong)
                    {
                        newLongByBar[cand.BarIndex] = cand.SignalPrice;
                        newLongLevelByBar[cand.BarIndex] = cand.LevelPrice;
                    }
                    else
                    {
                        newShortByBar[cand.BarIndex] = cand.SignalPrice;
                        newShortLevelByBar[cand.BarIndex] = cand.LevelPrice;
                    }

                    lastCandidateByState[cand.State] = cand;
                }
            }

            // --- LEVELS STEP ---
            // Add levels for ALL states that signaled (PERF: O(1) last-signal lookup).
            // Level line starts at the Pattern G signal bar (NOT the pivot).
            if (EnablePatternHLevels)
            {
                foreach (var kv in lastCandidateByState)
                {
                    var st = kv.Key;
                    var lastSignal = kv.Value;

                    if (st.GSignalBarIndex < 0) continue;

                    int gBarsAgo = CurrentBars[0] - st.GSignalBarIndex;
                    if (gBarsAgo < 0) continue;

                    int barsAgo = CurrentBars[0] - lastSignal.BarIndex;
                    if (barsAgo >= 0)
                    {
                         newLevels.Add(new PatternHLevel
                        {
                            IsLong        = st.IsLong,
                            OgBarIndex    = st.GSignalBarIndex,
                            OgTime        = Times[0][gBarsAgo],
                            EntryBarIndex = lastSignal.BarIndex,
                            EntryTime     = Times[0][barsAgo],
                            Price         = st.Level
                        });
                    }
                }
            }
		}



		private void DrawPatternHDebug(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternHLevel> newLevels)
		{
		    if (!CanDrawDebug() || !DrawPatternH)
		        return;

            // ---- Debug Labels ----
            if (ShowDebugLabels)
            {
                 foreach (var kv in patternHLongByBar)
                 {
                    int barIndex = kv.Key;
                    int barsAgo = CurrentBar - barIndex;
                    if (barsAgo < 0 || barsAgo >= CurrentBar) continue;

                    double level = PatternHLongLevel[barsAgo];
                    if (level < 0.0001) continue;

                    double startTs = PatternHLongStartTimestamp[barsAgo];
                    double endTs   = PatternHLongEndTimestamp[barsAgo];

                    DrawPatternHDetailLabel(true, barIndex, level, startTs, endTs);
                 }

                 foreach (var kv in patternHShortByBar)
                 {
                    int barIndex = kv.Key;
                    int barsAgo = CurrentBar - barIndex;
                    if (barsAgo < 0 || barsAgo >= CurrentBar) continue;

                    double level = PatternHShortLevel[barsAgo];
                    if (level < 0.0001) continue;

                    double startTs = PatternHShortStartTimestamp[barsAgo];
                    double endTs   = PatternHShortEndTimestamp[barsAgo];

                    DrawPatternHDetailLabel(false, barIndex, level, startTs, endTs);
                 }
            }

		    // ---- SIGNALS (triangles) ----
		    // Clear stale markers (only if we have previous state)
		    if (patternHLongByBar != null)
		        foreach (var kv in patternHLongByBar.ToList())
		            if (!newLongByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNH_L_{kv.Key}");

		    if (patternHShortByBar != null)
		        foreach (var kv in patternHShortByBar.ToList())
		            if (!newShortByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNH_S_{kv.Key}");

		    // Draw current (or none if disabled)
		    if (EnablePatternHSignal)
		    {
		        foreach (var kv in newLongByBar)
		            DrawPatternHText(true, kv.Key, kv.Value);

		        foreach (var kv in newShortByBar)
		            DrawPatternHText(false, kv.Key, kv.Value);
		    }
		    else
		    {
		        // If signals disabled, remove any existing markers
		        if (patternHLongByBar != null)
		            foreach (var kv in patternHLongByBar)
		                RemoveDrawObject($"PATTERNH_L_{kv.Key}");

		        if (patternHShortByBar != null)
		            foreach (var kv in patternHShortByBar)
		                RemoveDrawObject($"PATTERNH_S_{kv.Key}");
		    }

		    // ---- LEVELS (horizontal lines) ----
		    // For simplicity, clear prior drawn levels and redraw current.
		    // (Still cheap because this is debug-only.)
		    if (patternHLevels != null)
		    {
		        foreach (var lvl in patternHLevels)
		        {
		            string tagOld = lvl.IsLong
		                ? $"PATTERNH_LVL_L_{lvl.OgBarIndex}"
		                : $"PATTERNH_LVL_S_{lvl.OgBarIndex}";
		            RemoveDrawObject(tagOld);
		        }
		    }

		    if (EnablePatternHLevels)
		    {
		        foreach (var lvl in newLevels)
		            DrawPatternHLevelLine(lvl);
		    }
		}

        // ---- Debug Labels ----
        private void DrawPatternHDetailLabel(bool isLong, int barIndex, double level, double startTsOA, double endTsOA)
        {
             if (!CanDrawDebug()) return;

             int barsAgo = CurrentBar - barIndex;
             if (barsAgo < 0) return;

             DateTime startT = DateTime.FromOADate(startTsOA);
             DateTime endT   = DateTime.FromOADate(endTsOA);

             string text = string.Format("{0}\nLvl: {1:F2}\nS: {2:MM/dd HH:mm}\nE: {3:MM/dd HH:mm:ss}",
                isLong ? "LONG" : "SHORT",
                level,
                startT,
                endT);

             string tag = isLong ? $"PH_DET_L_{barIndex}" : $"PH_DET_S_{barIndex}";

             // Position: slightly above/below the level logic
             double y = isLong
                ? Low[barsAgo] - (PatternHMarkerOffsetTicks + 15) * TickSize
                : High[barsAgo] + (PatternHMarkerOffsetTicks + 15) * TickSize;

             Draw.Text(this, tag, false, text, barsAgo, y, 0,
                isLong ? PatternHLongColor : PatternHShortColor,
                new SimpleFont("Arial", 10),
                TextAlignment.Center,
                Brushes.Transparent,
                Brushes.Transparent,
                0);
        }

		private void DrawPatternHText(bool isLong, int barIndex, double price)
		{
		    if (!CanDrawDebug() || !DrawPatternH)
		        return;

		    if (barIndex < 0 || barIndex > CurrentBar)
		        return;

		    int barsAgo = CurrentBar - barIndex;
		    if (barsAgo < 0)
		        return;

		    string tag  = isLong ? $"PATTERNH_L_{barIndex}" : $"PATTERNH_S_{barIndex}";
		    string txt  = isLong ? "▲" : "▼";

		    Brush brush = isLong ? PatternHLongColor : PatternHShortColor;

		    // If you want to ensure updates replace old text, clear first
		    RemoveDrawObject(tag);

		    Draw.Text(this, tag, false,
		        txt,
		        barsAgo,
		        price,
		        0,
		        brush,
		        new SimpleFont("Arial", PatternHMarkerFontSize),
		        TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}

		private void DrawPatternHLevelLine(PatternHLevel lvl)
		{
		    if (!CanDrawDebug() || !DrawPatternH)
		        return;

		    if (lvl == null)
		        return;

		    string tag = lvl.IsLong
		        ? $"PATTERNH_LVL_L_{lvl.OgBarIndex}"
		        : $"PATTERNH_LVL_S_{lvl.OgBarIndex}";

		    Brush brush = lvl.IsLong ? PatternHLongColor : PatternHShortColor;

		    RemoveDrawObject(tag);

		    DateTime t1 = lvl.OgTime;
		    DateTime t2 = (lvl.EntryTime != default(DateTime)) ? lvl.EntryTime : Times[0][0];

		    Draw.Line(this, tag, false,
		        t1, lvl.Price,
		        t2, lvl.Price,
		        brush, PatternHLevelDashStyle, PatternHLevelWidth);
		}

		#endregion

		#region Draw ZigZag

		private void DrawOrUpdateLastSegment()
        {
            if (!CanDrawDebug() || !ShowZigZag)
                return;

            int n = pivotTimes.Count;
            if (n < 2)
                return;

            int segIdx = n - 1;
            string tag = $"{instanceId}_SEG_{segIdx}";

            RemoveDrawObject(tag);

            Draw.Line(this, tag, false,
                pivotTimes[segIdx - 1], pivotPrices[segIdx - 1],
                pivotTimes[segIdx],     pivotPrices[segIdx],
                ZigZagColor, ZigZagStyle, ZigZagWidth);

            if (zzTags != null)
                zzTags.Add(tag);
        }

        private void DrawOrUpdateReplacedLastSegment()
        {
           if (!CanDrawDebug() || !ShowZigZag)
                return;

            int n = pivotTimes.Count;
            if (n < 2)
                return;

            int segIdx = n - 1;
            string tag = $"{instanceId}_SEG_{segIdx}";

            RemoveDrawObject(tag);

            Draw.Line(this, tag, false,
                pivotTimes[segIdx - 1], pivotPrices[segIdx - 1],
                pivotTimes[segIdx],     pivotPrices[segIdx],
                ZigZagColor, ZigZagStyle, ZigZagWidth);

            if (zzTags != null)
                zzTags.Add(tag);
        }
		#endregion

        #region Helpers


		private void DPrint(string message)
		{
		    Print(string.Format(
		        "{0:yyyy-MM-dd HH:mm:ss.fff} | {1}",
		        Times[BarsInProgress][0],
		        message
		    ));
		}


		private string F(double v)
		{
		    if (double.IsNaN(v) || double.IsInfinity(v))
		        return "NaN";
		    return v.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
		}


		private bool CanDrawDebug()
		{
		    // Chart-only debug guard:
		    // - Market Analyzer has no chart surface (ChartControl == null)
		    if (ChartControl == null)
		        return false;

		    return true;
		}



        #endregion
    }
}

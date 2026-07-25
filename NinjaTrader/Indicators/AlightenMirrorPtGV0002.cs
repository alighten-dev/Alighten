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
    // Pattern G (V0002 — perf-optimized: no per-tick allocations, O(1) expired-
    // level lookups, LINQ-free rebuild, bounded pivot lookback window)
    //
    // LONG:
    //   A pivot LOW creates a level (body/guide price of the pivot bar).
    //   1) Price closes BELOW the level (far side).
    //   2) Price then closes ABOVE the level  -> level is "GAINED" (armed).
    //      (Re-arming is allowed: armed -> close back below -> close above again.)
    //   3) A later bar WICKS the level (low <= level) while its body holds
    //      above the level -> SIGNAL. Consecutive wick bars all signal.
    //   Once a signal sequence ends (a closed bar no longer wicks/holds),
    //   the level is dead permanently.
    //
    // SHORT: exact mirror on pivot HIGH levels ("LOST" instead of "GAINED").
    //
    // Architecture mirrors AlightenMirrorPtAV0009 (Pattern A):
    //   - Same HTF ZigZag pivot engine on BIP 0
    //   - Heavy rebuild on first tick of bar (historical signals)
    //   - Live intrabar signal/level update on every tick
    //   - Same plot layout (Signal, SignalMA, Long/Short Level + timestamps)
    // =====================================================================
    public class AlightenMirrorPtGV0002 : Indicator
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

		// Pattern G results (MA-friendly persistent state)
		// barIndex is primary-series (BIP 0) absolute bar index (0..CurrentBar)
		private Dictionary<int, double> patternGLongByBar;
		private Dictionary<int, double> patternGShortByBar;
		private Dictionary<int, double> patternGLongLevelByBar;
		private Dictionary<int, double> patternGShortLevelByBar;

		// Pattern G derived levels (logical objects; optionally drawn in debug)
		private List<PatternGLevel> patternGLevels;

		// Pattern G ARMED level states as of the last closed bar.
		// Used by the LIVE intrabar update to evaluate the forming bar.
		private List<PatternGLevelState> patternGArmedStates;

		// PERF: reused per-tick scratch lists (avoid allocating two Lists every tick)
		private List<PatternGLevelState> liveLongStatesScratch;
		private List<PatternGLevelState> liveShortStatesScratch;


		// Represents one pivot-derived Pattern G level and its reclaim state
		private class PatternGLevelState
		{
		    public int    PivotIdx;        // index into pivot lists
		    public bool   IsLong;          // pivot LOW -> long level; pivot HIGH -> short level
		    public double Level;           // pivotGuidePrices[PivotIdx] (body/guide price)

		    public bool   IsActive;        // false once dead (signal sequence ended / expired)
		    public bool   IsArmed;         // gained (long) / lost (short) as of last closed bar
		    public int    ArmedBarIndex;   // bar index of the arming close (-1 if never armed)

		    public PatternGLevelState()
		    {
		        IsActive      = true;
		        IsArmed       = false;
		        ArmedBarIndex = -1;
		    }
		}


		// Represents a horizontal Pattern G level to draw (at pivot guide price),
		// ending at the last wick/signal bar
		private class PatternGLevel
		{
		    public bool     IsLong;
		    public int      OgBarIndex;
		    public DateTime OgTime;

		    public int      EntryBarIndex;
		    public DateTime EntryTime;

		    public double   Price; // level (guide)

		    public PatternGLevel() { }
		}

		// ---- LIVE (intrabar) draw tags for Pattern G ----
		private const string PG_LIVE_TAG_L = "PATTERNG_LIVE_TAG_L";
		private const string PG_LIVE_TAG_S = "PATTERNG_LIVE_TAG_S";
		private const string PG_LIVE_LVL_L = "PATTERNG_LIVE_LVL_L";
		private const string PG_LIVE_LVL_S = "PATTERNG_LIVE_LVL_S";

		// --- Pattern G live recency (BIP0) state ---
		private bool pgLivePrevLong  = false;
		private bool pgLivePrevShort = false;

		private int  pgLiveLongLastEdgeSeq  = -1;   // last tick-seq where long became true (false->true)
		private int  pgLiveShortLastEdgeSeq = -1;   // last tick-seq where short became true (false->true)
		private int  pgLiveTickSeq          = 0;    // increments each BIP0 call within a bar

		private int  pgLiveLastChosenDir    = 0;    // +1 long, -1 short, 0 none (stability tie-break)

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

		// ---- Pattern G toggles ----


		[NinjaScriptProperty]
		[Display(Name="Enable Pattern G Signals", GroupName="Pattern G", Order=1)]
		public bool EnablePatternGSignal { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name="Enable Pattern G Levels", GroupName="Pattern G", Order=2)]
		public bool EnablePatternGLevels { get; set; } = true;

		[NinjaScriptProperty]
		[Display(Name = "Draw Pattern G", GroupName = "Pattern G", Order = 3)]
		public bool DrawPatternG { get; set; } = true;


		// Optional styling knobs (safe defaults)
		[NinjaScriptProperty]
		[Range(6, 30)]
		[Display(Name="Pattern G Marker Font Size", GroupName="Pattern G", Order=20)]
		public int PatternGMarkerFontSize { get; set; } = 18;

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name="Pattern G Marker Offset Ticks", GroupName="Pattern G", Order=21)]
		public int PatternGMarkerOffsetTicks { get; set; } = 5;

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern G Long Color", GroupName="Pattern G", Order=22)]
		public System.Windows.Media.Brush PatternGLongColor { get; set; } = Brushes.Lime;

		[Browsable(false)]
		public string PatternGLongColorSerialize
		{
		    get => Serialize.BrushToString(PatternGLongColor);
		    set => PatternGLongColor = Serialize.StringToBrush(value);
		}

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern G Short Color", GroupName="Pattern G", Order=23)]
		public System.Windows.Media.Brush PatternGShortColor { get; set; } = Brushes.Magenta;

		[Browsable(false)]
		public string PatternGShortColorSerialize
		{
		    get => Serialize.BrushToString(PatternGShortColor);
		    set => PatternGShortColor = Serialize.StringToBrush(value);
		}

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Pattern G Level Width", GroupName="Pattern G", Order=30)]
		public int PatternGLevelWidth { get; set; } = 2;

		[NinjaScriptProperty]
		[Display(Name="Pattern G Level Dash", GroupName="Pattern G", Order=31)]
		public DashStyleHelper PatternGLevelDashStyle { get; set; } = DashStyleHelper.Dash;


        [NinjaScriptProperty]
        [Display(Name="Show Debug Labels", GroupName="Pattern G", Order=40)]
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




		private Series<double> patternGSignal;

		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternGSignal
		{
		    get { return patternGSignal; }
		}

		private Series<double> patternGSignalMA;

		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternGSignalMA
		{
		    get { return patternGSignalMA; }
		}

        private Series<double> patternGLongLevel;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternGLongLevel => patternGLongLevel;

        private Series<double> patternGLongStartTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternGLongStartTimestamp => patternGLongStartTimestamp;

        private Series<double> patternGLongEndTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternGLongEndTimestamp => patternGLongEndTimestamp;

        private Series<double> patternGShortLevel;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternGShortLevel => patternGShortLevel;

        private Series<double> patternGShortStartTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternGShortStartTimestamp => patternGShortStartTimestamp;

        private Series<double> patternGShortEndTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternGShortEndTimestamp => patternGShortEndTimestamp;

		private Series<double> patternGLongSignalBarTimestamp;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternGLongSignalBarTimestamp => patternGLongSignalBarTimestamp;

		private Series<double> patternGShortSignalBarTimestamp;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternGShortSignalBarTimestamp => patternGShortSignalBarTimestamp;

		#endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AlightenMirrorPtGV0002";
                Description = "Pattern G: pivot level gained/lost by close, then wick retest of the level (long/short).";
                Calculate   = Calculate.OnEachTick;

                // For MA, overlay doesn’t matter. For chart debugging, overlay helps.
                IsOverlay               		= true;
                DrawOnPricePanel        		= true;

                DisplayInDataBox        		= true;
                PaintPriceMarkers       		= false;
                IsSuspendedWhileInactive 		= false;
				ShowTransparentPlotsInDataBox 	= true;


				AddPlot(Brushes.Transparent, "PatternGSignal");
				AddPlot(Brushes.Transparent, "PatternGSignalMA");
				AddPlot(Brushes.Transparent, "PatternGLongLevel");
				AddPlot(Brushes.Transparent, "PatternGLongStartTimestamp");
				AddPlot(Brushes.Transparent, "PatternGLongEndTimestamp");
				AddPlot(Brushes.Transparent, "PatternGShortLevel");
				AddPlot(Brushes.Transparent, "PatternGShortStartTimestamp");
				AddPlot(Brushes.Transparent, "PatternGShortEndTimestamp");
				AddPlot(Brushes.Transparent, "PatternGLongSignalBarTimestamp");
				AddPlot(Brushes.Transparent, "PatternGShortSignalBarTimestamp");

            }
            else if (State == State.Configure)
            {
                instanceId = $"PG_{Guid.NewGuid()}";

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

				patternGLongByBar  				= new Dictionary<int, double>(256);
				patternGShortByBar 				= new Dictionary<int, double>(256);
				patternGLongLevelByBar			= new Dictionary<int, double>(256);
				patternGShortLevelByBar			= new Dictionary<int, double>(256);
				patternGLevels     				= new List<PatternGLevel>(128);
				patternGArmedStates				= new List<PatternGLevelState>(128);
				liveLongStatesScratch			= new List<PatternGLevelState>(32);
				liveShortStatesScratch			= new List<PatternGLevelState>(32);

                lastTickTime = DateTime.MinValue;

				patternGSignal					= Values[0];
				patternGSignalMA				= Values[1];
                patternGLongLevel               = Values[2];
                patternGLongStartTimestamp      = Values[3];
                patternGLongEndTimestamp        = Values[4];
                patternGShortLevel              = Values[5];
                patternGShortStartTimestamp     = Values[6];
                patternGShortEndTimestamp       = Values[7];
				patternGLongSignalBarTimestamp  = Values[8];
				patternGShortSignalBarTimestamp = Values[9];

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

		        if (patternGSignal == null || patternGSignalMA == null)
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
		            patternGSignal[0]   = 0;
		            patternGSignalMA[0] = 0;

		            patternGLongLevel[0]          = 0;
		            patternGLongStartTimestamp[0] = 0;
		            patternGLongEndTimestamp[0]   = 0;

		            patternGShortLevel[0]          = 0;
		            patternGShortStartTimestamp[0] = 0;
		            patternGShortEndTimestamp[0]   = 0;

					patternGLongSignalBarTimestamp[0]  = 0;
					patternGShortSignalBarTimestamp[0] = 0;

		            if (CanDrawDebug() && DrawPatternG)
		            {
		                RemoveDrawObject(PG_LIVE_TAG_L);
		                RemoveDrawObject(PG_LIVE_TAG_S);
		                RemoveDrawObject(PG_LIVE_LVL_L);
		                RemoveDrawObject(PG_LIVE_LVL_S);
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
		            RebuildPatternG_BIP0();

		            if (EnablePatternGSignal)
		            {
		                // Default clear (current bar)
		                patternGSignal[0]   = 0;
		                patternGSignalMA[0] = 0;

		                patternGLongLevel[0]          = 0;
		                patternGLongStartTimestamp[0] = 0;
		                patternGLongEndTimestamp[0]   = 0;

		                patternGShortLevel[0]          = 0;
		                patternGShortStartTimestamp[0] = 0;
		                patternGShortEndTimestamp[0]   = 0;

						patternGLongSignalBarTimestamp[0]  = 0;
						patternGShortSignalBarTimestamp[0] = 0;

						// Clear inherited live values from previous bar's intra-bar updates
						patternGLongLevel[1]  = 0;
						patternGShortLevel[1] = 0;

		                if (CurrentBars[0] >= 1)
		                {
		                    int sigBar = CurrentBars[0] - 1;

		                    bool longNow  = patternGLongByBar  != null && patternGLongByBar.ContainsKey(sigBar);
		                    bool shortNow = patternGShortByBar != null && patternGShortByBar.ContainsKey(sigBar);

		                    double sig = 0;
		                    if (longNow && !shortNow)
		                    {
		                        sig = +1;
		                        if (patternGLongLevelByBar != null && patternGLongLevelByBar.TryGetValue(sigBar, out double lvlPrice))
		                            patternGLongLevel[1] = lvlPrice;
		                    }
		                    else if (shortNow && !longNow)
		                    {
		                        sig = -1;
		                        if (patternGShortLevelByBar != null && patternGShortLevelByBar.TryGetValue(sigBar, out double lvlPrice))
		                            patternGShortLevel[1] = lvlPrice;
		                    }

		                    // DataBox / chart-aligned plot (belongs to bar that just closed)
		                    patternGSignal[1] = sig;

		                    // MA-friendly plot (belongs to current bar so MA reads [0] reliably)
		                    patternGSignalMA[0] = sig;
		                }
		            }
		        }

		        // -----------------------------
		        // 2) LIVE intrabar semantics
		        //    Update on EVERY tick so it can appear/disappear while bar forms.
		        // -----------------------------
		        if (EnablePatternGSignal)
		        {
		            UpdatePatternG_Live_BIP0();
		        }
		    }
		    catch (Exception ex)
		    {
		        Print(string.Format("[PG EX][OnBarUpdate] t={0:yyyy-MM-dd HH:mm:ss} BIP={1} CB0={2} CurrentBar={3} Msg={4}\nStack={5}",
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

		#region Pattern G

		private void UpdatePatternG_Live_BIP0()
		{
		    // Per-bar reset for recency tracking (BIP0)
		    if (IsFirstTickOfBar)
		    {
		        pgLivePrevLong           = false;
		        pgLivePrevShort          = false;
		        pgLiveLongLastEdgeSeq    = -1;
		        pgLiveShortLastEdgeSeq   = -1;
		        pgLiveTickSeq            = 0;
		        pgLiveLastChosenDir      = 0;
		    }
		    else
		    {
		        // advance intra-bar tick sequence
		        pgLiveTickSeq++;
		    }

		    // Safety
		    if (CurrentBar < 1)
		    {
		        patternGSignal[0] = 0;

		        // Reset edge state to avoid carrying across early exits
		        pgLivePrevLong  = false;
		        pgLivePrevShort = false;

		        if (CanDrawDebug() && DrawPatternG)
		        {
		            RemoveDrawObject(PG_LIVE_TAG_L);
		            RemoveDrawObject(PG_LIVE_TAG_S);
		            RemoveDrawObject(PG_LIVE_LVL_L);
		            RemoveDrawObject(PG_LIVE_LVL_S);
		        }
		        return;
		    }

		    // If armed states haven't been built yet, just clear live.
		    if (patternGArmedStates == null || patternGArmedStates.Count == 0)
		    {
		        patternGSignal[0] = 0;

		        // Reset edge state to avoid carrying across early exits
		        pgLivePrevLong  = false;
		        pgLivePrevShort = false;

		        if (CanDrawDebug() && DrawPatternG)
		        {
		            RemoveDrawObject(PG_LIVE_TAG_L);
		            RemoveDrawObject(PG_LIVE_TAG_S);
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

		    foreach (var st in patternGArmedStates)
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
		    if (liveLong && !pgLivePrevLong)
		        pgLiveLongLastEdgeSeq = pgLiveTickSeq;

		    if (liveShort && !pgLivePrevShort)
		        pgLiveShortLastEdgeSeq = pgLiveTickSeq;

		    // Update prev flags after edge detection
		    pgLivePrevLong  = liveLong;
		    pgLivePrevShort = liveShort;

		    // --- Choose liveSig ---
		    double liveSig = 0;


		    if (liveLong && !liveShort)
		    {
		        liveSig = +1;
		        pgLiveLastChosenDir = +1;
		    }
		    else if (liveShort && !liveLong)
		    {
		        liveSig = -1;
		        pgLiveLastChosenDir = -1;
		    }
		    else if (liveLong && liveShort)
		    {
		        // Recency wins
		        if (pgLiveLongLastEdgeSeq > pgLiveShortLastEdgeSeq)
		        {
		            liveSig = +1;
		            pgLiveLastChosenDir = +1;
		        }
		        else if (pgLiveShortLastEdgeSeq > pgLiveLongLastEdgeSeq)
		        {
		            liveSig = -1;
		            pgLiveLastChosenDir = -1;
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
		                pgLiveLastChosenDir = +1;
		            }
		            else if (maxShortPen > maxLongPen + 0.0001)
		            {
		                liveSig = -1;
		                pgLiveLastChosenDir = -1;
		            }
		            else
		            {
		                // Still tied: stabilize
		                if (pgLiveLastChosenDir == +1) liveSig = +1;
		                else if (pgLiveLastChosenDir == -1) liveSig = -1;
                        else liveSig = 0;
		            }
		        }
		    }
		    else
		    {
		        liveSig = 0;
		        pgLiveLastChosenDir = 0;
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

                    if (pgLiveLongLastEdgeSeq == pgLiveTickSeq || liveLongFirstHitTime == DateTime.MinValue)
                        liveLongFirstHitTime = now;

                    patternGLongLevel[0] = liveLongStates[0].Level;
                    patternGLongStartTimestamp[0] = liveLongFirstHitTime.ToOADate();
                    patternGLongEndTimestamp[0] = now.ToOADate();
					patternGLongSignalBarTimestamp[0] = Time[0].ToOADate();
                }
                else
                {
                    patternGLongLevel[0] = 0;
                    patternGLongStartTimestamp[0] = 0;
                    patternGLongEndTimestamp[0] = 0;
					patternGLongSignalBarTimestamp[0] = 0;
                    liveLongFirstHitTime = DateTime.MinValue;
                }

                if (liveSig == -1 && liveShortStates.Count > 0)
                {
                    if (!livePreciseEndTimesS.ContainsKey(sigBar))
                    {
                        double preciseTime = CurrentPreciseTime;
                        livePreciseEndTimesS[sigBar] = preciseTime;
                    }

                    if (pgLiveShortLastEdgeSeq == pgLiveTickSeq || liveShortFirstHitTime == DateTime.MinValue)
                        liveShortFirstHitTime = now;

                    patternGShortLevel[0] = liveShortStates[0].Level;
                    patternGShortStartTimestamp[0] = liveShortFirstHitTime.ToOADate();
                    patternGShortEndTimestamp[0] = now.ToOADate();
					patternGShortSignalBarTimestamp[0] = Time[0].ToOADate();
                }
                else
                {
                    patternGShortLevel[0] = 0;
                    patternGShortStartTimestamp[0] = 0;
                    patternGShortEndTimestamp[0] = 0;
					patternGShortSignalBarTimestamp[0] = 0;
                    liveShortFirstHitTime = DateTime.MinValue;
                }
            }

		    // LIVE plot (chart):
		    patternGSignal[0] = liveSig;

		    // --- Draw live artifacts ---
		    if (CanDrawDebug() && DrawPatternG)
		    {
                // Fixed set of recycled live-line tags up to a max (clear all slots first)
                int MAX_LIVE_LINES = 20;

		        RemoveDrawObject(PG_LIVE_TAG_L);
		        RemoveDrawObject(PG_LIVE_TAG_S);

                // Clear all slots
                for(int i=0; i<MAX_LIVE_LINES; i++)
                {
                    RemoveDrawObject(PG_LIVE_LVL_L + "_" + i);
                    RemoveDrawObject(PG_LIVE_LVL_S + "_" + i);
                }

		        // Draw LONG live
		        if (liveLong && liveLongStates.Count > 0)
		        {
		            double y = l0 - (PatternGMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PG_LIVE_TAG_L, false,
		                "▲",
		                0,
		                y,
		                0,
		                PatternGLongColor,
		                new SimpleFont("Arial", PatternGMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);

                    for(int i=0; i < liveLongStates.Count && i < MAX_LIVE_LINES; i++)
                    {
                        var st = liveLongStates[i];
                        double level = st.Level;

		                int ogBarIndex = (pivotTimes != null && st.PivotIdx >= 0 && st.PivotIdx < pivotTimes.Count)
		                    ? BarsArray[0].GetBar(pivotTimes[st.PivotIdx])
		                    : -1;

		                if (ogBarIndex >= 0)
		                {
		                    int ogBarsAgo = CurrentBars[0] - ogBarIndex;
		                    if (ogBarsAgo >= 0)
		                    {
		                        Draw.Line(this, PG_LIVE_LVL_L + "_" + i, false,
		                            ogBarsAgo, level,
		                            0,        level,
		                            PatternGLongColor, PatternGLevelDashStyle, PatternGLevelWidth);
		                    }
		                }
                    }
		        }

		        // Draw SHORT live
		        if (liveShort && liveShortStates.Count > 0)
		        {
		            double y = h0 + (PatternGMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PG_LIVE_TAG_S, false,
		                "▼",
		                0,
		                y,
		                0,
		                PatternGShortColor,
		                new SimpleFont("Arial", PatternGMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);

                    for(int i=0; i < liveShortStates.Count && i < MAX_LIVE_LINES; i++)
                    {
                        var st = liveShortStates[i];
                        double level = st.Level;

		                int ogBarIndex = (pivotTimes != null && st.PivotIdx >= 0 && st.PivotIdx < pivotTimes.Count)
		                    ? BarsArray[0].GetBar(pivotTimes[st.PivotIdx])
		                    : -1;

		                if (ogBarIndex >= 0)
		                {
		                    int ogBarsAgo = CurrentBars[0] - ogBarIndex;
		                    if (ogBarsAgo >= 0)
		                    {
		                        Draw.Line(this, PG_LIVE_LVL_S + "_" + i, false,
		                            ogBarsAgo, level,
		                            0,        level,
		                            PatternGShortColor, PatternGLevelDashStyle, PatternGLevelWidth);
		                    }
		                }
                    }
		        }
		    }
		}



		private void RebuildPatternG_BIP0()
		{
		    if (!EnablePatternGSignal && !EnablePatternGLevels)
		        return;

		    BuildPatternG(out var newPGLong, out var newPGShort, out var newPGLongLevel, out var newPGShortLevel, out var newPGLevels);

		    ApplyPatternGResults(newPGLong, newPGShort, newPGLongLevel, newPGShortLevel, newPGLevels);

		    // chart-only
		    if (DrawPatternG && CanDrawDebug())
		        DrawPatternGDebug(newPGLong, newPGShort, newPGLevels);
		}

		private void BuildPatternG(
		    out Dictionary<int, double> newLongByBar,
		    out Dictionary<int, double> newShortByBar,
		    out Dictionary<int, double> newLongLevelByBar,
		    out Dictionary<int, double> newShortLevelByBar,
		    out List<PatternGLevel> newLevels)
		{
		    newLongByBar  = new Dictionary<int, double>();
		    newShortByBar = new Dictionary<int, double>();
		    newLongLevelByBar = new Dictionary<int, double>();
		    newShortLevelByBar = new Dictionary<int, double>();
		    newLevels     = new List<PatternGLevel>();

		    if ((!EnablePatternGSignal && !EnablePatternGLevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 1)
		        return;

		    ScanPatternG(newLongByBar, newShortByBar, newLongLevelByBar, newShortLevelByBar, newLevels);
		}


		private void ApplyPatternGResults(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    Dictionary<int, double> newLongLevelByBar,
		    Dictionary<int, double> newShortLevelByBar,
		    List<PatternGLevel> newLevels)
		{
		    if (patternGLongByBar == null)
		        patternGLongByBar = new Dictionary<int, double>();
		    if (patternGShortByBar == null)
		        patternGShortByBar = new Dictionary<int, double>();
		    if (patternGLongLevelByBar == null)
		        patternGLongLevelByBar = new Dictionary<int, double>();
		    if (patternGShortLevelByBar == null)
		        patternGShortLevelByBar = new Dictionary<int, double>();
		    if (patternGLevels == null)
		        patternGLevels = new List<PatternGLevel>();

		    // Signals
		    if (EnablePatternGSignal)
		    {
		        patternGLongByBar.Clear();
		        foreach (var kv in newLongByBar)
		            patternGLongByBar[kv.Key] = kv.Value;

		        patternGShortByBar.Clear();
		        foreach (var kv in newShortByBar)
		            patternGShortByBar[kv.Key] = kv.Value;

		        patternGLongLevelByBar.Clear();
		        foreach (var kv in newLongLevelByBar)
		            patternGLongLevelByBar[kv.Key] = kv.Value;

		        patternGShortLevelByBar.Clear();
		        foreach (var kv in newShortLevelByBar)
		            patternGShortLevelByBar[kv.Key] = kv.Value;
		    }
		    else
		    {
		        patternGLongByBar.Clear();
		        patternGShortByBar.Clear();
		        patternGLongLevelByBar.Clear();
		        patternGShortLevelByBar.Clear();
		    }

		    // Levels
		    if (EnablePatternGLevels)
		    {
		        patternGLevels.Clear();
		        patternGLevels.AddRange(newLevels);
		    }
		    else
		    {
		        patternGLevels.Clear();
		    }
		}


        // Helper class to collect potential signals before filtering
        private class PatternGCandidate
        {
            public int      BarIndex;
            public bool     IsLong;
            public double   LevelPrice;
            public double   SignalPrice;
            public int      PivotIdx;
            public PatternGLevelState State;
        }

		// =========================
		// Pattern G scan
		// =========================
		// For each pivot, derive a level (guide price) and run the gain/lose
		// reclaim state machine across closed bars:
		//   phase 0: waiting for a CLOSE on the far side of the level
		//            (long: close < level; short: close > level)
		//   phase 1: far side seen; waiting for the reclaiming CLOSE
		//            (long: close > level = GAINED; short: close < level = LOST)
		//   phase 2: ARMED. Bars that wick the level while the body holds the
		//            reclaim side produce signals (consecutive bars all signal).
		//            - Close back on the far side BEFORE any signal -> back to
		//              phase 1 (re-arm allowed).
		//            - Once a signal sequence has started, the first closed bar
		//              that no longer satisfies wick+body kills the level
		//              permanently.
		private void ScanPatternG(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    Dictionary<int, double> newLongLevelByBar,
		    Dictionary<int, double> newShortLevelByBar,
		    List<PatternGLevel> newLevels)
		{
		    patternGArmedStates.Clear();
            var candidates = new List<PatternGCandidate>();

            // PERF: expired levels keyed by tick-rounded price -> O(1) lookups
            HashSet<long> expiredLevelsL = new HashSet<long>();
            HashSet<long> expiredLevelsS = new HashSet<long>();

		    if ((!EnablePatternGSignal && !EnablePatternGLevels)
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

		        bool   isLong = !pivotIsHigh[idx];       // pivot LOW -> long level; pivot HIGH -> short level
		        double level  = pivotGuidePrices[idx];   // body/guide price

		        // Skip if this price level already expired via a completed signal sequence
		        if (isLong  && expiredLevelsL.Contains(LevelKey(level))) continue;
		        if (!isLong && expiredLevelsS.Contains(LevelKey(level))) continue;

		        var st = new PatternGLevelState
		        {
		            PivotIdx = idx,
		            IsLong   = isLong,
		            Level    = level
		        };

		        int  phase           = 0;     // 0 = wait far-side close, 1 = wait reclaim close, 2 = armed
		        bool sequenceStarted = false;
		        bool dead            = false;

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

		            if (isLong)
		            {
		                if (phase == 0)
		                {
		                    if (c < level - eps)
		                        phase = 1;                       // far side (below) seen
		                }
		                else if (phase == 1)
		                {
		                    if (c > level + eps)
		                    {
		                        phase = 2;                       // GAINED (armed)
		                        st.ArmedBarIndex = barIndex;
		                    }
		                }
		                else // phase == 2 (armed)
		                {
		                    bool wickTouches = lo <= level + eps;
		                    bool bodyAbove   = bodyLow >= level - eps;

		                    if (wickTouches && bodyAbove)
		                    {
		                        // SIGNAL bar (consecutive wick bars all signal)
		                        sequenceStarted = true;
		                        candidates.Add(new PatternGCandidate
		                        {
		                            BarIndex    = barIndex,
		                            IsLong      = true,
		                            LevelPrice  = level,
		                            SignalPrice = lo - (PatternGMarkerOffsetTicks * TickSize),
		                            PivotIdx    = idx,
		                            State       = st
		                        });
		                    }
		                    else
		                    {
		                        if (sequenceStarted)
		                        {
		                            // Sequence ended on a closed bar -> level dead permanently
		                            dead = true;
		                            expiredLevelsL.Add(LevelKey(level));
		                            break;
		                        }

		                        // No signal yet: closing back below dis-arms (re-arm allowed)
		                        if (c < level - eps)
		                            phase = 1;
		                        // else: still armed, keep waiting for the wick retest
		                    }
		                }
		            }
		            else
		            {
		                if (phase == 0)
		                {
		                    if (c > level + eps)
		                        phase = 1;                       // far side (above) seen
		                }
		                else if (phase == 1)
		                {
		                    if (c < level - eps)
		                    {
		                        phase = 2;                       // LOST (armed)
		                        st.ArmedBarIndex = barIndex;
		                    }
		                }
		                else // phase == 2 (armed)
		                {
		                    bool wickTouches = hi >= level - eps;
		                    bool bodyBelow   = bodyHigh <= level + eps;

		                    if (wickTouches && bodyBelow)
		                    {
		                        // SIGNAL bar (consecutive wick bars all signal)
		                        sequenceStarted = true;
		                        candidates.Add(new PatternGCandidate
		                        {
		                            BarIndex    = barIndex,
		                            IsLong      = false,
		                            LevelPrice  = level,
		                            SignalPrice = hi + (PatternGMarkerOffsetTicks * TickSize),
		                            PivotIdx    = idx,
		                            State       = st
		                        });
		                    }
		                    else
		                    {
		                        if (sequenceStarted)
		                        {
		                            // Sequence ended on a closed bar -> level dead permanently
		                            dead = true;
		                            expiredLevelsS.Add(LevelKey(level));
		                            break;
		                        }

		                        // No signal yet: closing back above dis-arms (re-arm allowed)
		                        if (c > level + eps)
		                            phase = 1;
		                        // else: still armed, keep waiting for the wick retest
		                    }
		                }
		            }
		        }

		        st.IsActive = !dead;
		        st.IsArmed  = !dead && phase == 2;

		        // Armed & alive as of the last closed bar -> LIVE candidate on the forming bar
		        if (st.IsActive && st.IsArmed)
		            patternGArmedStates.Add(st);
		    }

            // --- FILTERING STEP (PERF: single pass, no LINQ/group allocations) ---
            // Overwrite semantics match the previous GroupBy implementation:
            // the last candidate written for a bar+direction wins the plot slot.
            // Candidates are appended in ascending bar order per state, so the
            // last write per state is also its latest signal in the sequence.
            var lastCandidateByState = new Dictionary<PatternGLevelState, PatternGCandidate>();

            if (EnablePatternGSignal)
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
            // Add levels for ALL states that signaled (PERF: O(1) last-signal lookup)
            if (EnablePatternGLevels)
            {
                foreach (var kv in lastCandidateByState)
                {
                    var st = kv.Key;
                    var lastSignal = kv.Value;

                    int ogBarIdx = pivotBarIndex[st.PivotIdx];
                    if (ogBarIdx < 0) continue;

                    int barsAgo = CurrentBars[0] - lastSignal.BarIndex;
                    if (barsAgo >= 0)
                    {
                         newLevels.Add(new PatternGLevel
                        {
                            IsLong        = st.IsLong,
                            OgBarIndex    = ogBarIdx,
                            OgTime        = pivotTimes[st.PivotIdx],
                            EntryBarIndex = lastSignal.BarIndex,
                            EntryTime     = Times[0][barsAgo],
                            Price         = st.Level
                        });
                    }
                }
            }
		}



		private void DrawPatternGDebug(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternGLevel> newLevels)
		{
		    if (!CanDrawDebug() || !DrawPatternG)
		        return;

            // ---- Debug Labels ----
            if (ShowDebugLabels)
            {
                 foreach (var kv in patternGLongByBar)
                 {
                    int barIndex = kv.Key;
                    int barsAgo = CurrentBar - barIndex;
                    if (barsAgo < 0 || barsAgo >= CurrentBar) continue;

                    double level = PatternGLongLevel[barsAgo];
                    if (level < 0.0001) continue;

                    double startTs = PatternGLongStartTimestamp[barsAgo];
                    double endTs   = PatternGLongEndTimestamp[barsAgo];

                    DrawPatternGDetailLabel(true, barIndex, level, startTs, endTs);
                 }

                 foreach (var kv in patternGShortByBar)
                 {
                    int barIndex = kv.Key;
                    int barsAgo = CurrentBar - barIndex;
                    if (barsAgo < 0 || barsAgo >= CurrentBar) continue;

                    double level = PatternGShortLevel[barsAgo];
                    if (level < 0.0001) continue;

                    double startTs = PatternGShortStartTimestamp[barsAgo];
                    double endTs   = PatternGShortEndTimestamp[barsAgo];

                    DrawPatternGDetailLabel(false, barIndex, level, startTs, endTs);
                 }
            }

		    // ---- SIGNALS (triangles) ----
		    // Clear stale markers (only if we have previous state)
		    if (patternGLongByBar != null)
		        foreach (var kv in patternGLongByBar.ToList())
		            if (!newLongByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNG_L_{kv.Key}");

		    if (patternGShortByBar != null)
		        foreach (var kv in patternGShortByBar.ToList())
		            if (!newShortByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNG_S_{kv.Key}");

		    // Draw current (or none if disabled)
		    if (EnablePatternGSignal)
		    {
		        foreach (var kv in newLongByBar)
		            DrawPatternGText(true, kv.Key, kv.Value);

		        foreach (var kv in newShortByBar)
		            DrawPatternGText(false, kv.Key, kv.Value);
		    }
		    else
		    {
		        // If signals disabled, remove any existing markers
		        if (patternGLongByBar != null)
		            foreach (var kv in patternGLongByBar)
		                RemoveDrawObject($"PATTERNG_L_{kv.Key}");

		        if (patternGShortByBar != null)
		            foreach (var kv in patternGShortByBar)
		                RemoveDrawObject($"PATTERNG_S_{kv.Key}");
		    }

		    // ---- LEVELS (horizontal lines) ----
		    // For simplicity, clear prior drawn levels and redraw current.
		    // (Still cheap because this is debug-only.)
		    if (patternGLevels != null)
		    {
		        foreach (var lvl in patternGLevels)
		        {
		            string tagOld = lvl.IsLong
		                ? $"PATTERNG_LVL_L_{lvl.OgBarIndex}"
		                : $"PATTERNG_LVL_S_{lvl.OgBarIndex}";
		            RemoveDrawObject(tagOld);
		        }
		    }

		    if (EnablePatternGLevels)
		    {
		        foreach (var lvl in newLevels)
		            DrawPatternGLevelLine(lvl);
		    }
		}

        // ---- Debug Labels ----
        private void DrawPatternGDetailLabel(bool isLong, int barIndex, double level, double startTsOA, double endTsOA)
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

             string tag = isLong ? $"PG_DET_L_{barIndex}" : $"PG_DET_S_{barIndex}";

             // Position: slightly above/below the level logic
             double y = isLong
                ? Low[barsAgo] - (PatternGMarkerOffsetTicks + 15) * TickSize
                : High[barsAgo] + (PatternGMarkerOffsetTicks + 15) * TickSize;

             Draw.Text(this, tag, false, text, barsAgo, y, 0,
                isLong ? PatternGLongColor : PatternGShortColor,
                new SimpleFont("Arial", 10),
                TextAlignment.Center,
                Brushes.Transparent,
                Brushes.Transparent,
                0);
        }

		private void DrawPatternGText(bool isLong, int barIndex, double price)
		{
		    if (!CanDrawDebug() || !DrawPatternG)
		        return;

		    if (barIndex < 0 || barIndex > CurrentBar)
		        return;

		    int barsAgo = CurrentBar - barIndex;
		    if (barsAgo < 0)
		        return;

		    string tag  = isLong ? $"PATTERNG_L_{barIndex}" : $"PATTERNG_S_{barIndex}";
		    string txt  = isLong ? "▲" : "▼";

		    Brush brush = isLong ? PatternGLongColor : PatternGShortColor;

		    // If you want to ensure updates replace old text, clear first
		    RemoveDrawObject(tag);

		    Draw.Text(this, tag, false,
		        txt,
		        barsAgo,
		        price,
		        0,
		        brush,
		        new SimpleFont("Arial", PatternGMarkerFontSize),
		        TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}

		private void DrawPatternGLevelLine(PatternGLevel lvl)
		{
		    if (!CanDrawDebug() || !DrawPatternG)
		        return;

		    if (lvl == null)
		        return;

		    string tag = lvl.IsLong
		        ? $"PATTERNG_LVL_L_{lvl.OgBarIndex}"
		        : $"PATTERNG_LVL_S_{lvl.OgBarIndex}";

		    Brush brush = lvl.IsLong ? PatternGLongColor : PatternGShortColor;

		    RemoveDrawObject(tag);

		    DateTime t1 = lvl.OgTime;
		    DateTime t2 = (lvl.EntryTime != default(DateTime)) ? lvl.EntryTime : Times[0][0];

		    Draw.Line(this, tag, false,
		        t1, lvl.Price,
		        t2, lvl.Price,
		        brush, PatternGLevelDashStyle, PatternGLevelWidth);
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

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

//using SharpDX;
//using SharpDX.Direct2D1;
//using SharpDX.DirectWrite;
#endregion


namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenMirrorPtCV0004 : Indicator
    {
		
		#region Class Variables

        // HTF pivots (BIP 0 only)
        private List<DateTime> pivotTimes;
        private List<double>   pivotPrices;
        private List<bool>     pivotIsHigh;
        private List<double>   pivotGuidePrices;

        // Important levels derived from pivots (BIP 0 only)
        private List<double> importantLevels;

        // Latest LTF proximity readings (updated on BIP 1/2, published on BIP 0)
        private int  lastDist1mTicks = int.MaxValue;
        private int  lastDist5mTicks = int.MaxValue;

        private double lastPivotPrice = double.NaN;
        private int    lastPivotType  = 0; // +1 high, -1 low

        // ---- V0003-style “export pivot” DTO pattern (serialization-safe) ----
        private class ExportPivot
        {
            public double   Level;
            public DateTime Time;
            public string   Status;
            public bool     IsHigh;

            // Keep this if you want to mirror your guide/bodies later:
            public double   GuideLevel;

            // Explicit parameterless ctor for NinjaTrader serialization
            public ExportPivot()
            {
            }
        }
        private List<ExportPivot> exportPivots = new List<ExportPivot>();

        // ---- Lookback window index (no list pruning / no RemoveAt churn) ----
        private int lookbackStartPivotIdx = 0;

        // ---- Debug draw internals ----
        private string instanceId;
        private HashSet<string> zzTags;
		
		// Pattern C results (MA-friendly persistent state)
		// barIndex is primary-series (BIP 0) absolute bar index (0..CurrentBar)
		private Dictionary<int, double> patternCLongByBar;
		private Dictionary<int, double> patternCShortByBar;
		
		// Pattern C derived levels (logical objects; optionally drawn in debug)
		private List<PatternCLevel> patternCLevels;
		
		// Pattern C structure tracking (triplets/pivots). This mirrors the V0003 concept.
		// These are used inside ScanPatternC() to detect OG/support/regional relationships.
		private List<PatternCTriplet> patternCTriplets;
		private List<PatternCTriplet> patternCTripletsPotential; // potential (structural-only)

		
		// Represents a single Pattern C triple (OG, Support, TrendStart)
		private class PatternCTriplet
		{
		    public int    OgIdx;
		    public int    SupportIdx;
		    public int    TrendStartIdx;
		    public bool   IsLong;
		    public bool   IsActive;
		
		    public double OgPrice;         // OG guide price
		    public double SupportPrice;    // Support guide price
		    public double TrendStartPrice; // TrendStart extreme (actual pivot price)
		
		    public PatternCTriplet() { }
		}
		
		// Represents a horizontal Pattern C level to draw (at OG price), ending at the wick/entry pivot
		private class PatternCLevel
		{
		    public bool     IsLong;
		    public int      OgBarIndex;
		    public DateTime OgTime;
		
		    public int      EntryBarIndex;
		    public DateTime EntryTime;
		
		    public double   Price; // OG level (guide)
		
		    public PatternCLevel() { }
		}
		
		// ---- Pattern C proximity candidates (evaluated/updated on BIP 1) ----
		private class PatternCCandidate
		{
		    public bool   IsLong;
		
		    public int    OgPivotIdx;
		    public int    SupportPivotIdx;
		    public int    TrendStartPivotIdx;
		
		    public double OgLevel;          // OG guide price (the level we measure to)
		    public double SupportGuide;     // Support/resistance guide
		    public double TrendStartExtreme;// TrendStart pivot extreme (low for long, high for short)
		
		    // State that becomes true/false as time progresses
		    public bool   HasCleared;       // cleared support (long) / cleared resistance (short)
		    public bool   Aborted;          // invalidated by new extreme beyond TrendStart
		    public bool   Touched;          // wick touched OG w/ body constraint met
		
		    public DateTime TouchTime;      // time of the 1m bar that first touched
		    public int      TouchBip0BarIdx; // snapshot of CurrentBars[0] when touched (for latch lifecycle)
		
		    public PatternCCandidate() { }
		}
		
		private List<PatternCCandidate> patternCCandidates;
		
		private PatternCTriplet activePatternCLong;
		private PatternCTriplet activePatternCShort;
		
		private long _lastMapDebugTicks = 0;
		
		private Dictionary<string, int> lastSignalBip0BarByKey;

		private string MakeTripletKey(PatternCTriplet t)
		{
		    if (t == null) return string.Empty;
		    return (t.IsLong ? "L" : "S") + "|" + t.OgIdx + "|" + t.SupportIdx + "|" + t.TrendStartIdx;
		}
		
		// --- Pattern C live recency (BIP0) state ---
		private bool pcLivePrevLong  = false;
		private bool pcLivePrevShort = false;
		
		private int  pcLiveLongLastEdgeSeq  = -1;   // last tick-seq where long became true (false->true)
		private int  pcLiveShortLastEdgeSeq = -1;   // last tick-seq where short became true (false->true)
		private int  pcLiveTickSeq          = 0;    // increments each BIP0 call within a bar
		
		private int  pcLiveLastChosenDir    = 0;    // +1 long, -1 short, 0 none (stability tie-break)
		
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

        private HashSet<string> recordedPatternKeys;
	
        #endregion
		
        #region Properties
		

        [NinjaScriptProperty]
		[Range(0, 500000)]
		[Display(Name="Bars To Process", GroupName="ZigZag", Order=53)]
		public int BarsToProcess { get; set; } = 200; // 0 = process all
		
		// ---- Pattern C toggles ----

		[NinjaScriptProperty]
		[Display(Name="Enable Pattern C Signals", GroupName="Pattern C", Order=1)]
		public bool EnablePatternCSignal { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Enable Pattern C Levels", GroupName="Pattern C", Order=2)]
		public bool EnablePatternCLevels { get; set; } = true;
	
		[NinjaScriptProperty]
		[Display(Name = "Draw Pattern C", GroupName = "Pattern C", Order = 3)]
		public bool DrawPatternC { get; set; } = true;
		// Optional styling knobs (safe defaults)
		[NinjaScriptProperty]
		[Range(6, 30)]
		[Display(Name="Pattern C Marker Font Size", GroupName="Pattern C", Order=20)]
		public int PatternCMarkerFontSize { get; set; } = 18;
		
		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name="Pattern C Marker Offset Ticks", GroupName="Pattern C", Order=21)]
		public int PatternCMarkerOffsetTicks { get; set; } = 15;
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern C Long Color", GroupName="Pattern C", Order=22)]
		public System.Windows.Media.Brush PatternCLongColor { get; set; } = Brushes.Cyan;
		
		[Browsable(false)]
		public string PatternCLongColorSerialize
		{
		    get => Serialize.BrushToString(PatternCLongColor);
		    set => PatternCLongColor = Serialize.StringToBrush(value);
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern C Short Color", GroupName="Pattern C", Order=23)]
		public System.Windows.Media.Brush PatternCShortColor { get; set; } = Brushes.Crimson;
		
		[Browsable(false)]
		public string PatternCShortColorSerialize
		{
		    get => Serialize.BrushToString(PatternCShortColor);
		    set => PatternCShortColor = Serialize.StringToBrush(value);
		}
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Pattern C Level Width", GroupName="Pattern C", Order=30)]
		public int PatternCLevelWidth { get; set; } = 2;
		
		[NinjaScriptProperty]
		[Display(Name="Pattern C Level Dash", GroupName="Pattern C", Order=31)]
		public DashStyleHelper PatternCLevelDashStyle { get; set; } = DashStyleHelper.Dash;
		
		[NinjaScriptProperty]
		[Display(Name="Gate: Require Pivot for Pattern C Signal", GroupName="Pattern C", Order=91)]
		public bool RequirePivotForPatternCSignal { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="Show Debug Labels", GroupName="Pattern C", Order=92)]
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
		
		
		
        #endregion
		
		#region Plots
		
		private Series<double> patternCSignal;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternCSignal
		{
		    get { return patternCSignal; }
		}
		
		private Series<double> patternCSignalMA;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternCSignalMA
		{
		    get { return patternCSignalMA; }
		}

        private Series<double> patternCLongLevel;
        private DateTime lastTickTime;

        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternCLongLevel => patternCLongLevel;
        
        private DateTime liveLongFirstHitTime;
        private DateTime liveShortFirstHitTime;

        private Series<double> patternCLongStartTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternCLongStartTimestamp => patternCLongStartTimestamp;

        private Series<double> patternCLongEndTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternCLongEndTimestamp => patternCLongEndTimestamp;

        private Series<double> patternCShortLevel;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternCShortLevel => patternCShortLevel;

        private Series<double> patternCShortStartTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternCShortStartTimestamp => patternCShortStartTimestamp;

        private Series<double> patternCShortEndTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternCShortEndTimestamp => patternCShortEndTimestamp;
		
		private Series<double> patternCLongSignalBarTimestamp;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternCLongSignalBarTimestamp => patternCLongSignalBarTimestamp;
		
		private Series<double> patternCShortSignalBarTimestamp;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternCShortSignalBarTimestamp => patternCShortSignalBarTimestamp;
		
		#endregion


        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AlightenMirrorPtCV0004";
                Description = "Market Analyzer core: HTF ZigZag/levels on primary series with 1m/5m proximity columns.";
                Calculate   = Calculate.OnEachTick;

                // For MA, overlay doesn’t matter. For chart debugging, overlay helps.
                IsOverlay               		= true;
                DrawOnPricePanel        		= true;

                DisplayInDataBox        		= true;
                PaintPriceMarkers       		= false;
                IsSuspendedWhileInactive 		= false;
				ShowTransparentPlotsInDataBox 	= true;

				AddPlot(Brushes.Transparent, "PatternCSignal");
				AddPlot(Brushes.Transparent, "PatternCSignalMA");

                // Data Plots for First Occurrence
                AddPlot(Brushes.Transparent, "PatternCLongLevel");
                AddPlot(Brushes.Transparent, "PatternCLongStartTimestamp");
                AddPlot(Brushes.Transparent, "PatternCLongEndTimestamp");
                AddPlot(Brushes.Transparent, "PatternCShortLevel");
                AddPlot(Brushes.Transparent, "PatternCShortStartTimestamp");
                AddPlot(Brushes.Transparent, "PatternCShortEndTimestamp");
				AddPlot(Brushes.Transparent, "PatternCLongSignalBarTimestamp");
				AddPlot(Brushes.Transparent, "PatternCShortSignalBarTimestamp");
                
            }
            else if (State == State.Configure)
            {
                //AddDataSeries(BarsPeriodType.Tick, 1);
                instanceId = $"ZZ_MA_{Guid.NewGuid()}";
                
                livePreciseEndTimesL = new Dictionary<int, double>();
                livePreciseEndTimesS = new Dictionary<int, double>();
            }
            else if (State == State.DataLoaded)
            {
                pivotTimes       				= new List<DateTime>(Math.Min(400, BarsToProcess));
                pivotPrices      				= new List<double>(Math.Min(400, BarsToProcess));
                pivotIsHigh      				= new List<bool>(Math.Min(400, BarsToProcess));
                pivotGuidePrices 				= new List<double>(Math.Min(400, BarsToProcess));

                exportPivots     				= new List<ExportPivot>(Math.Min(400, BarsToProcess));

                zzTags 							= new HashSet<string>();
				
				patternCLongByBar  				= new Dictionary<int, double>(256);
				patternCShortByBar 				= new Dictionary<int, double>(256);
				patternCLevels     				= new List<PatternCLevel>(128);
				patternCTriplets   				= new List<PatternCTriplet>(128);
				patternCTripletsPotential   	= new List<PatternCTriplet>(128);
				patternCCandidates 				= new List<PatternCCandidate>(128);
				
				lastSignalBip0BarByKey = new Dictionary<string, int>(512, StringComparer.Ordinal);
                
                lastTickTime = DateTime.MinValue;

				patternCSignal					= Values[0];
				patternCSignalMA				= Values[1];                
                patternCLongLevel               = Values[2];
                patternCLongStartTimestamp      = Values[3];
                patternCLongEndTimestamp        = Values[4];
                patternCShortLevel              = Values[5];
                patternCShortStartTimestamp     = Values[6];
                patternCShortEndTimestamp       = Values[7];
				patternCLongSignalBarTimestamp  = Values[8];
				patternCShortSignalBarTimestamp = Values[9];

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

		    // ---- BIP 0 (HTF / chart timeframe) ----
		    if (BarsInProgress == 0)
		    {
		        // NEW: Live intrabar check runs EVERY tick on BIP0
		        if (EnablePatternCSignal)
		        {
		            //Print($"[LIVE] t={Times[0][0]:HH:mm:ss.fff} Close0={Close[0]}");
		            UpdatePatternC_Live_BIP0();
		        }
		
		        // Heavy HTF logic only once per HTF bar
		        if (!IsFirstTickOfBar)
		            return;
		
		        // Ultra-fast early-exit windowing — only relevant for heavy work
		        if (BarsToProcess > 0)
		        {
		            int totalLoaded = BarsArray[0].Count;
		            int cutoffBar   = totalLoaded - BarsToProcess;
		            if (CurrentBars[0] < cutoffBar)
		                return;
		        }
		
		        if (CurrentBar < 3)
		            return;
		
		        ProcessClosedBarPivot_BIP0();  // uses [1]
		        RebuildPatternC_BIP0();
		
		        if (EnablePatternCSignal)
		        {
		            // Default clear
		            patternCSignal[0]   = 0;
		            patternCSignalMA[0] = 0;

					patternCLongLevel[0]          = 0;
		            patternCLongStartTimestamp[0] = 0;
		            patternCLongEndTimestamp[0]   = 0;
		            
		            patternCShortLevel[0]          = 0;
		            patternCShortStartTimestamp[0] = 0;
		            patternCShortEndTimestamp[0]   = 0;
					
					patternCLongSignalBarTimestamp[0]  = 0;
					patternCShortSignalBarTimestamp[0] = 0;
					
		
		            if (CurrentBars[0] < 1)
		                return;
		
		            int sigBar = CurrentBars[0] - 1;
		
		            bool longNow  = patternCLongByBar  != null && patternCLongByBar.ContainsKey(sigBar);
		            bool shortNow = patternCShortByBar != null && patternCShortByBar.ContainsKey(sigBar);
					
					//Print($"[PC-OC:SIGBAR] nowT={Times[0][0]:MM-dd HH:mm:ss} sigBar={sigBar} longNow={longNow} shortNow={shortNow}");
		
		            double sig = 0;
		            if (longNow && !shortNow) sig = +1;
		            else if (shortNow && !longNow) sig = -1;
		
		            // DataBox / chart-aligned plot (belongs to bar that just closed)
		            patternCSignal[1] = sig;
		
		            // MA-friendly plot (belongs to current bar so MA reads [0] reliably)
		            patternCSignalMA[0] = sig;
		        }
		
		        return;
		    }
		
		    // For other BIPs, do nothing (unless you explicitly want a BIP1 live check too)
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
			
			 bool createdPivot = false;
		
		    if (previousGreen && currentGreen && isHigh) { ProcessPivot(time, h0, true,  bodyHigh); createdPivot = true; }
		    if (previousRed   && currentRed   && isLow ) { ProcessPivot(time, l0, false, bodyLow ); createdPivot = true; }
		    if (previousGreen && currentRed   && isHigh) { ProcessPivot(time, h0, true,  bodyHigh); createdPivot = true; }
		    if (previousRed   && currentGreen && isLow ) { ProcessPivot(time, l0, false, bodyLow ); createdPivot = true; }
		    if (previousRed   && currentGreen && isHigh) { ProcessPivot(time, h0, true,  bodyHigh); createdPivot = true; }
		    if (previousGreen && currentRed   && isLow ) { ProcessPivot(time, l0, false, bodyLow ); createdPivot = true; }
		
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

            // Export DTO (kept aligned)
            exportPivots.Add(new ExportPivot
            {
                Level     = price,
                Time      = time,
                Status    = "NEW",
                IsHigh    = isHigh,
                GuideLevel= guidePrice
            });

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

            // Keep export list aligned with pivot arrays
            if (exportPivots != null && exportPivots.Count == pivotTimes.Count && idx >= 0)
            {
                ExportPivot ep = exportPivots[idx];
                if (ep == null)
                {
                    ep = new ExportPivot();
                    exportPivots[idx] = ep;
                }

                ep.Level      = price;
                ep.Time       = time;
                ep.IsHigh     = isHigh;
                ep.Status     = "REPLACED";
                ep.GuideLevel = guidePrice;
            }

            lastPivotPrice = price;
            lastPivotType  = isHigh ? 1 : -1;


            DrawOrUpdateReplacedLastSegment();
        }

        #endregion
		
		#region Pattern C
		
		// Live tags (kept distinct from your historical per-bar tags)
		private const string PC_LIVE_TAG_L = "PATTERNC_LIVE_L";
		private const string PC_LIVE_TAG_S = "PATTERNC_LIVE_S";
		private const string PC_LIVE_LVL_L = "PATTERNC_LIVE_LVL_L";
		private const string PC_LIVE_LVL_S = "PATTERNC_LIVE_LVL_S";

        // Persistent storage for live timestamps across bar closes
        private Dictionary<int, double> livePreciseEndTimesL;
        private Dictionary<int, double> livePreciseEndTimesS;

		private void UpdatePatternC_Live_BIP0()
		{
		    // Per-bar reset for recency tracking (BIP0)
		    if (IsFirstTickOfBar)
		    {
		        pcLivePrevLong           = false;
		        pcLivePrevShort          = false;
		        pcLiveLongLastEdgeSeq    = -1;
		        pcLiveShortLastEdgeSeq   = -1;
		        pcLiveTickSeq            = 0;
		        pcLiveLastChosenDir      = 0;
                
                liveLongFirstHitTime     = DateTime.MinValue;
                liveShortFirstHitTime    = DateTime.MinValue;
		    }
		    else
		    {
		        pcLiveTickSeq++;
		    }
			
		
		    // Safety
		    if (CurrentBar < 1)
		    {
		        patternCSignal[0] = 0;
		
		        pcLivePrevLong  = false;
		        pcLivePrevShort = false;
		
		        if (CanDrawDebug() && DrawPatternC)
		        {
		            RemoveDrawObject(PC_LIVE_TAG_L);
		            RemoveDrawObject(PC_LIVE_TAG_S);
		            RemoveDrawObject(PC_LIVE_LVL_L);
		            RemoveDrawObject(PC_LIVE_LVL_S);
                    RemoveDrawObject(PC_LIVE_TAG_L + "_Detail");
                    RemoveDrawObject(PC_LIVE_TAG_S + "_Detail");
		        }
                
                // Reset live plots
                PatternCLongLevel[0] = 0;
                PatternCLongStartTimestamp[0] = 0;
                PatternCLongEndTimestamp[0] = 0;
                PatternCShortLevel[0] = 0;
                PatternCShortStartTimestamp[0] = 0;
                PatternCShortEndTimestamp[0] = 0;
				PatternCLongSignalBarTimestamp[0] = 0;
				PatternCShortSignalBarTimestamp[0] = 0;

		        return;
		    }
		
		    if (patternCCandidates == null || patternCCandidates.Count == 0 || pivotTimes == null || pivotTimes.Count == 0)
		    {
		        patternCSignal[0] = 0;
		
		        pcLivePrevLong  = false;
		        pcLivePrevShort = false;
		
		        if (CanDrawDebug() && DrawPatternC)
		        {
		            RemoveDrawObject(PC_LIVE_TAG_L);
		            RemoveDrawObject(PC_LIVE_TAG_S);
		            RemoveDrawObject(PC_LIVE_LVL_L);
		            RemoveDrawObject(PC_LIVE_LVL_S);
		        }
		        return;
		    }
		
		    double eps = TickSize * 0.5;
		
		    // Current forming bar (BIP0) intrabar state
		    double o0 = Open[0];
		    double c0 = Close[0];
		    double h0 = High[0];
		    double l0 = Low[0];
		
		    double bodyHigh0 = Math.Max(o0, c0);
		    double bodyLow0  = Math.Min(o0, c0);
		
		    // Build pivot lookup once (barIndex -> isHigh) so closed-bar pivot gating matches historical
		    Dictionary<int, bool> pivotTypeByBar = null;
		    if (RequirePivotForPatternCSignal)
		    {
		        pivotTypeByBar = new Dictionary<int, bool>(128);
		        for (int i = 0; i < pivotTimes.Count; i++)
		        {
		            int bi = BarsArray[0].GetBar(pivotTimes[i]);
		            if (bi >= 0 && !pivotTypeByBar.ContainsKey(bi))
		                pivotTypeByBar[bi] = pivotIsHigh[i];
		        }
		    }
		
		    // Wick predicates (strict body-side, no body-cross)
		    bool WickOk_Long_Bar(int barIndex)
		    {
		        int barsAgo = CurrentBar - barIndex;
		        double o = Open[barsAgo];
		        double c = Close[barsAgo];
		        double lo = Low[barsAgo];
		
		        double bodyLow = Math.Min(o, c);
		
		        bool wickTouches = lo <= (/*og*/0) + eps; // placeholder, replaced inline
		        bool bodyAbove   = bodyLow >= (/*og*/0) - eps;
		
		        return wickTouches && bodyAbove;
		    }
		
		    bool WickOk_Short_Bar(int barIndex)
		    {
		        int barsAgo = CurrentBar - barIndex;
		        double o = Open[barsAgo];
		        double c = Close[barsAgo];
		        double hi = High[barsAgo];
		
		        double bodyHigh = Math.Max(o, c);
		
		        bool wickTouches = hi >= (/*og*/0) - eps; // placeholder, replaced inline
		        bool bodyBelow   = bodyHigh <= (/*og*/0) + eps;
		
		        return wickTouches && bodyBelow;
		    }
		
		    // Current-bar wick (strict body-side, no body-cross)
		    bool WickOk_Long_Current(double og)
		    {
		        bool wickTouches = l0 <= og + eps;
		        bool bodyAbove   = bodyLow0 >= og - eps;
		        return wickTouches && bodyAbove;
		    }
		
		    bool WickOk_Short_Current(double og)
		    {
		        bool wickTouches = h0 >= og - eps;
		        bool bodyBelow   = bodyHigh0 <= og + eps;
		        return wickTouches && bodyBelow;
		    }
		
		    bool liveLong  = false;
		    bool liveShort = false;
		
            // Multi-level collections
            List<PatternCCandidate> liveLongCands = new List<PatternCCandidate>();
            List<PatternCCandidate> liveShortCands = new List<PatternCCandidate>();
		
            // For Recency/Tie-break (still track best)
		    double bestLongDistTicks  = double.MaxValue;
		    double bestShortDistTicks = double.MaxValue;

            // These are ONLY used for signal tie-break logic now, not drawing restriction
            double bestLongOgForSig  = double.NaN;
            double bestShortOgForSig = double.NaN;
		
		    // Evaluate each candidate using the SAME BIP0 regime + wick-streak semantics as historical.
		    foreach (var cand in patternCCandidates)
		    {
		        if (cand == null || cand.Aborted)
		            continue;
		
		        double og = cand.OgLevel;
		
		        // Determine OG anchor bar index (start scanning from OG bar)
		        int ogBarIndex = -1;
		        if (cand.OgPivotIdx >= 0 && cand.OgPivotIdx < pivotTimes.Count)
		            ogBarIndex = BarsArray[0].GetBar(pivotTimes[cand.OgPivotIdx]);
		
		        if (ogBarIndex < 0)
		            continue;

				
				// closed bars are 0..CurrentBar-1, but eligibility may complete on CURRENT bar
				int lastClosedBar = CurrentBar - 1;
				
				if (lastClosedBar < ogBarIndex && CurrentBar < ogBarIndex)
				    continue;
		
		        // --------------------------
		        // 1) Eligibility (closed bars)
		        // --------------------------
		        bool eligibleForWick = false;
		        int  eligibleBarIx   = -1;
		
		        if (cand.IsLong)
		        {
		            // LONG: close below OG, then close above OG (closed bars only)
		            bool sawBelow = false;
		
		            for (int bi = ogBarIndex; bi <= lastClosedBar; bi++)
		            {
		//                int barsAgo = CurrentBar - bi;
		//                double c = Close[barsAgo];
						double c = (bi == CurrentBar) ? c0 : Close[CurrentBar - bi];
		
		                if (!sawBelow)
		                {
		                    if (c < og - eps)
		                        sawBelow = true;
		                }
		                else
		                {
		                    if (c > og + eps)
		                    {
		                        eligibleForWick = true;
		                        eligibleBarIx   = bi;   // MUST NOT be a signal bar
		                        break;
		                    }
		                }
		            }
		        }
		        else
		        {
		            // SHORT: close above OG, then close below OG (closed bars only)
		            bool sawAbove = false;
		
		            for (int bi = ogBarIndex; bi <= lastClosedBar; bi++)
		            {
		//                int barsAgo = CurrentBar - bi;
		//                double c = Close[barsAgo];
						double c = (bi == CurrentBar) ? c0 : Close[CurrentBar - bi];
		
		                if (!sawAbove)
		                {
		                    if (c > og + eps)
		                        sawAbove = true;
		                }
		                else
		                {
		                    if (c < og - eps)
		                    {
		                        eligibleForWick = true;
		                        eligibleBarIx   = bi;   // MUST NOT be a signal bar
		                        break;
		                    }
		                }
		            }
		        }
		
		        if (!eligibleForWick)
		            continue;
		
		
		        // -----------------------------------------
		        // 2) Determine whether a wick streak is active
		        //    on the immediately prior closed bars
		        // -----------------------------------------
		        int firstWickIx = -1;
		        int lastWickIx  = -1;
		
		        // Find first wick bar AFTER eligibility bar, then extend until first non-wick.
		        // If RequirePivotForPatternCSignal is ON, enforce pivotOk on CLOSED bars (matches historical).
		        for (int bi = eligibleBarIx + 1; bi <= lastClosedBar; bi++)
		        {
		            int barsAgo = CurrentBar - bi;
		
		            bool pivotOkClosed = true;
		            if (RequirePivotForPatternCSignal)
		            {
		                if (pivotTypeByBar == null || !pivotTypeByBar.TryGetValue(bi, out bool isHigh))
		                    pivotOkClosed = false;
		                else
		                    pivotOkClosed = cand.IsLong ? (!isHigh) : (isHigh); // long requires pivot LOW, short requires pivot HIGH
		            }
		
		            bool isWickBar;
		            if (cand.IsLong)
		            {
		                double o = Open[barsAgo];
		                double c = Close[barsAgo];
		                double lo = Low[barsAgo];
		                double bodyLow = Math.Min(o, c);
		
		                bool wickTouches = lo <= og + eps;
		                bool bodyAbove   = bodyLow >= og - eps;
		
		                isWickBar = wickTouches && bodyAbove && pivotOkClosed;
		            }
		            else
		            {
		                double o = Open[barsAgo];
		                double c = Close[barsAgo];
		                double hi = High[barsAgo];
		                double bodyHigh = Math.Max(o, c);
		
		                bool wickTouches = hi >= og - eps;
		                bool bodyBelow   = bodyHigh <= og + eps;
		
		                isWickBar = wickTouches && bodyBelow && pivotOkClosed;
		            }
		
		            if (firstWickIx < 0)
		            {
		                if (isWickBar)
		                {
		                    firstWickIx = bi;
		                    lastWickIx  = bi;
		                }
		            }
		            else
		            {
		                if (isWickBar)
		                {
		                    lastWickIx = bi;
		                }
		                else
		                {
		                    // streak ended historically before current bar
		                    break;
		                }
		            }
		        }
		
		        bool streakActiveIntoCurrent = (lastWickIx == lastClosedBar);
		
		        // -----------------------------------------
		        // 3) Current bar live wick check (and optional pivot requirement handling)
		        // -----------------------------------------
		        // Per your requirement: eligibility bar itself is never a signal bar.
		        // Current bar is always > lastClosedBar, so fine.
		
		        // If pivot requirement is ON, we cannot know if CURRENT bar will confirm as pivot.
		        // So: allow live preview on current bar if wick/body conditions are met,
		        // but historical will still enforce pivot on close.
		        bool pivotOkCurrent = true;
		        // If you want "strict" behavior (no live unless pivot already confirmed),
		        // you can uncomment this block:
		        /*
		        if (RequirePivotForPatternCSignal)
		        {
		            pivotOkCurrent = pivotTypeByBar != null
		                             && pivotTypeByBar.TryGetValue(CurrentBar, out bool isHighNow)
		                             && (cand.IsLong ? !isHighNow : isHighNow);
		        }
		        */
		
		        bool currentIsWick = cand.IsLong
		            ? (WickOk_Long_Current(og)  && pivotOkCurrent)
		            : (WickOk_Short_Current(og) && pivotOkCurrent);
				
				// DEBUG: right before/after you compute currentIsWick
				double bodyHi0 = Math.Max(o0, c0);
				double bodyLo0 = Math.Min(o0, c0);
				
				bool wickTs = cand.IsLong ? (l0 <= og + eps) : (h0 >= og - eps);
				bool bodyOk      = cand.IsLong ? (bodyLo0 >= og - eps) : (bodyHi0 <= og + eps);
				
		
		        bool liveThisCandidate = false;
		
		        if (!streakActiveIntoCurrent)
		        {
		            // streak not active coming into current bar; current bar may START streak if it wicks
		            // (first wick must occur AFTER eligibility bar – already satisfied because current bar > eligibleBarIx)
		            if (currentIsWick)
		                liveThisCandidate = true;
		        }
		        else
		        {
		            // streak was active through the last closed bar; current bar must continue it
		            if (currentIsWick)
		                liveThisCandidate = true;
		            else
		                liveThisCandidate = false; // streak ends; live artifacts removed by outer logic
		        }
		
		        if (!liveThisCandidate)
		            continue;
		
		        // Collect ALL candidates
		        double distTicks = Math.Abs(c0 - og) / TickSize;
		
		        if (cand.IsLong)
		        {
                    liveLongCands.Add(cand);
                    liveLong = true;

		            if (distTicks < bestLongDistTicks)
		            {
		                bestLongDistTicks  = distTicks;
                        bestLongOgForSig   = og;
		            }
		        }
		        else
		        {
                    liveShortCands.Add(cand);
                    liveShort = true;

		            if (distTicks < bestShortDistTicks)
		            {
		                bestShortDistTicks  = distTicks;
                        bestShortOgForSig   = og;
		            }
		        }
		    }
		
		    // --- Recency tracking: capture false->true edges for each side on this bar ---
		    if (liveLong && !pcLivePrevLong)
		        pcLiveLongLastEdgeSeq = pcLiveTickSeq;
		
		    if (liveShort && !pcLivePrevShort)
		        pcLiveShortLastEdgeSeq = pcLiveTickSeq;
		
		    pcLivePrevLong  = liveLong;
		    pcLivePrevShort = liveShort;
		
		    // --- Choose liveSig for the plot (single scalar), but DRAW both sides when both are true ---
		    double liveSig = 0;
		
		    if (liveLong && !liveShort)
		    {
		        liveSig = +1;
		        pcLiveLastChosenDir = +1;
		    }
		    else if (liveShort && !liveLong)
		    {
		        liveSig = -1;
		        pcLiveLastChosenDir = -1;
		    }
		    else if (liveLong && liveShort)
		    {
		        // Recency wins
		        if (pcLiveLongLastEdgeSeq > pcLiveShortLastEdgeSeq)
		        {
		            liveSig = +1;
		            pcLiveLastChosenDir = +1;
		        }
		        else if (pcLiveShortLastEdgeSeq > pcLiveLongLastEdgeSeq)
		        {
		            liveSig = -1;
		            pcLiveLastChosenDir = -1;
		        }
		        else
		        {
		            // Tie-break by penetration beyond OG on this tick
		            double longPen  = 0;
		            double shortPen = 0;
		
		            if (!double.IsNaN(bestLongOgForSig))
		                longPen = (bestLongOgForSig - l0) / TickSize;
		
		            if (!double.IsNaN(bestShortOgForSig))
		                shortPen = (h0 - bestShortOgForSig) / TickSize;
		
		            if (longPen > shortPen + 0.0001)
		            {
		                liveSig = +1;
		                pcLiveLastChosenDir = +1;
		            }
		            else if (shortPen > longPen + 0.0001)
		            {
		                liveSig = -1;
		                pcLiveLastChosenDir = -1;
		            }
		            else
		            {
		                // stabilize
		                liveSig = pcLiveLastChosenDir;
		            }
		        }
		    }
		    else
		    {
		        liveSig = 0;
		        pcLiveLastChosenDir = 0;
		    }
		
		    // LIVE plot (chart)
		    patternCSignal[0] = liveSig;

            // Reset live plots first
            PatternCLongLevel[0] = 0;
            PatternCLongStartTimestamp[0] = 0;
            PatternCLongEndTimestamp[0] = 0;
            PatternCShortLevel[0] = 0;
            PatternCShortStartTimestamp[0] = 0;
            PatternCShortEndTimestamp[0] = 0;

            // --- V0005-style Live Latching & Persistence ---
            // LONG
            if (liveLongCands.Count > 0)
            {
                 int curBar = CurrentBars[0];
                 // LATCH on first detection
                 if (!livePreciseEndTimesL.ContainsKey(curBar))
                 {
                      double now = CurrentPreciseTime; // Safe access
                      livePreciseEndTimesL[curBar] = now;
                      
                      // CLAIM KEYS to prevent historical overwrite
                      foreach(var c in liveLongCands)
                      {
                           string key = (c.IsLong ? "L" : "S") + "|" + c.OgPivotIdx + "|" + c.SupportPivotIdx + "|" + c.TrendStartPivotIdx + "_Historical";
                           recordedPatternKeys.Add(key);
                      }
                 }
                 
                 // PLOT CONTINUOUSLY (every tick) using latched value
                 PatternCLongEndTimestamp[0]   = livePreciseEndTimesL[curBar];
				 PatternCLongSignalBarTimestamp[0] = Time[0].ToOADate();
                 
                 var best = liveLongCands[0];
                 PatternCLongLevel[0]          = best.OgLevel;
                 // Note: OgTime might not be perfectly aligned with pivotTimes index if purely candidate-based, 
                 // but typically OgPivotIdx is valid.
                 if (best.OgPivotIdx >= 0 && best.OgPivotIdx < pivotTimes.Count)
                     PatternCLongStartTimestamp[0] = pivotTimes[best.OgPivotIdx].ToOADate();
            }

            // SHORT
            if (liveShortCands.Count > 0)
            {
                 int curBar = CurrentBars[0];
                 if (!livePreciseEndTimesS.ContainsKey(curBar))
                 {
                      double now = CurrentPreciseTime;
                      livePreciseEndTimesS[curBar] = now;
                      
                      foreach(var c in liveShortCands)
                      {
                           string key = (c.IsLong ? "L" : "S") + "|" + c.OgPivotIdx + "|" + c.SupportPivotIdx + "|" + c.TrendStartPivotIdx + "_Historical";
                           recordedPatternKeys.Add(key);
                      }
                 }
                 
                 PatternCShortEndTimestamp[0]   = livePreciseEndTimesS[curBar];
				 PatternCShortSignalBarTimestamp[0] = Time[0].ToOADate();
                 
                 var best = liveShortCands[0];
                 PatternCShortLevel[0]          = best.OgLevel;
                 if (best.OgPivotIdx >= 0 && best.OgPivotIdx < pivotTimes.Count)
                     PatternCShortStartTimestamp[0] = pivotTimes[best.OgPivotIdx].ToOADate();
            }
		
		    // --- Draw live artifacts (both sides allowed) ---
		    if (CanDrawDebug() && DrawPatternC)
		    {
                const int MAX_LIVE_LINES = 50;

                // Clear old
                for(int i=0; i < MAX_LIVE_LINES; i++)
                {
                    RemoveDrawObject(PC_LIVE_TAG_L + "_" + i);
                    RemoveDrawObject(PC_LIVE_TAG_S + "_" + i);
                    RemoveDrawObject(PC_LIVE_LVL_L + "_" + i);
                    RemoveDrawObject(PC_LIVE_LVL_S + "_" + i);
                    RemoveDrawObject(PC_LIVE_TAG_L + "_Detail_" + i);
                    RemoveDrawObject(PC_LIVE_TAG_S + "_Detail_" + i);
                }
		        RemoveDrawObject(PC_LIVE_TAG_L);
		        RemoveDrawObject(PC_LIVE_TAG_S);
		        RemoveDrawObject(PC_LIVE_LVL_L);
		        RemoveDrawObject(PC_LIVE_LVL_S);
                RemoveDrawObject(PC_LIVE_TAG_L + "_Detail");
                RemoveDrawObject(PC_LIVE_TAG_S + "_Detail");
		
		        DateTime t2 = Times[0][0];
		
		        // LONG
                int drawnCount = 0;
                foreach(var c in liveLongCands)
		        {
                    if (drawnCount >= MAX_LIVE_LINES) break;

                    // First occurrence check is used for PLOTS, but NOT for DRAWING.
                    // Visuals should always appear for valid candidates.
                    // string key = (c.IsLong ? "L" : "S") + "|" + c.OgPivotIdx + "|" + c.SupportPivotIdx + "|" + c.TrendStartPivotIdx + "_Historical";
                    // if (recordedPatternKeys.Contains(key)) continue;

		            DateTime t1 = t2;
		
		            if (c.OgPivotIdx >= 0 && c.OgPivotIdx < pivotTimes.Count)
		            {
		                t1 = pivotTimes[c.OgPivotIdx];
		                int ogBI = BarsArray[0].GetBar(t1);
		                if (ogBI < 0)
		                    t1 = t2;
		            }

                    string suffix = "_" + drawnCount;
		
		            Draw.Line(this, PC_LIVE_LVL_L + suffix, false,
		                t1, c.OgLevel,
		                t2, c.OgLevel,
		                PatternCLongColor, PatternCLevelDashStyle, PatternCLevelWidth);
		
		            double yL = l0 - (PatternCMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PC_LIVE_TAG_L + suffix, false,
		                "▲",
		                0,
		                yL,
		                0,
		                PatternCLongColor,
		                new SimpleFont("Arial", PatternCMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);

                    if (ShowDebugLabels)
                    {
                         string text = string.Format("LONG\nLvl: {0:F2}\nS: {1:MM/dd HH:mm}\nE: {2:MM/dd HH:mm:ss}",
                            c.OgLevel,
                            t1,
                             PatternCLongEndTimestamp[0] > 0
                                ? DateTime.FromOADate(PatternCLongEndTimestamp[0]) 
                                : (CurrentPreciseTime > 0 // Fallback
                                    ? DateTime.FromOADate(CurrentPreciseTime) 
                                    : (lastTickTime > DateTime.MinValue ? lastTickTime : Times[0][0])));
                         
                         Draw.Text(this, PC_LIVE_TAG_L + "_Detail" + suffix, false,
                            text,
                            0,
                            yL - 15 * TickSize,
                            0,
                            PatternCLongColor,
                            new SimpleFont("Arial", 10),
                            TextAlignment.Center,
                            Brushes.Transparent,
                            Brushes.Transparent,
                            0);
                    }

                    drawnCount++;
		        }
		
		        // SHORT
                drawnCount = 0;
                foreach(var c in liveShortCands)
		        {
                    if (drawnCount >= MAX_LIVE_LINES) break;

                    // Visuals (always draw)
                    // string key = (c.IsLong ? "L" : "S") + "|" + c.OgPivotIdx + "|" + c.SupportPivotIdx + "|" + c.TrendStartPivotIdx + "_Historical";
                    // if (recordedPatternKeys.Contains(key)) continue;

		            DateTime t1 = t2;
		
		            if (c.OgPivotIdx >= 0 && c.OgPivotIdx < pivotTimes.Count)
		            {
		                t1 = pivotTimes[c.OgPivotIdx];
		                int ogBI = BarsArray[0].GetBar(t1);
		                if (ogBI < 0)
		                    t1 = t2;
		            }
		
                    string suffix = "_" + drawnCount;

		            Draw.Line(this, PC_LIVE_LVL_S + suffix, false,
		                t1, c.OgLevel,
		                t2, c.OgLevel,
		                PatternCShortColor, PatternCLevelDashStyle, PatternCLevelWidth);
		
		            double yS = h0 + (PatternCMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PC_LIVE_TAG_S + suffix, false,
		                "▼",
		                0,
		                yS,
		                0,
		                PatternCShortColor,
		                new SimpleFont("Arial", PatternCMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);

                     if (ShowDebugLabels)
                    {
                         string text = string.Format("SHORT\nLvl: {0:F2}\nS: {1:MM/dd HH:mm}\nE: {2:MM/dd HH:mm:ss}",
                            c.OgLevel,
                            t1,
                             PatternCShortEndTimestamp[0] > 0
                                ? DateTime.FromOADate(PatternCShortEndTimestamp[0]) 
                                : (CurrentPreciseTime > 0 // Fallback
                                    ? DateTime.FromOADate(CurrentPreciseTime) 
                                    : (lastTickTime > DateTime.MinValue ? lastTickTime : Times[0][0])));
                         
                         Draw.Text(this, PC_LIVE_TAG_S + "_Detail" + suffix, false,
                            text,
                            0,
                            yS + 15 * TickSize,
                            0,
                            PatternCShortColor,
                            new SimpleFont("Arial", 10),
                            TextAlignment.Center,
                            Brushes.Transparent,
                            Brushes.Transparent,
                            0);
                    }

                    drawnCount++;
		        }
		    }
		}



		private void RebuildPatternC_BIP0()
		{
		    if (!EnablePatternCSignal && !EnablePatternCLevels)
		        return;
		
		    BuildPatternC(out var newPBLong, out var newPBShort, out var newPBLevels);
		
		    ApplyPatternCResults(newPBLong, newPBShort, newPBLevels);
			
			RebuildPatternCCandidates_BIP0();
		
		    // chart-only
		    if (DrawPatternC && CanDrawDebug())
		        DrawPatternCDebug(newPBLong, newPBShort, newPBLevels);
		}
		
		private void RebuildPatternCCandidates_BIP0()
		{
		    if (patternCCandidates == null)
		        patternCCandidates = new List<PatternCCandidate>(128);
		
		    patternCCandidates.Clear();
		
		    // Ensure map exists (safe for chart + MA)
		    if (lastSignalBip0BarByKey == null)
		        lastSignalBip0BarByKey = new Dictionary<string, int>(256, StringComparer.Ordinal);
		
		    int      bip0 = CurrentBars[0];
		    DateTime t0   = Times[0][0];
		
		    int completedCount = (patternCTriplets == null) ? 0 : patternCTriplets.Count;
		    int potentialCount = (patternCTripletsPotential == null) ? 0 : patternCTripletsPotential.Count;
		
		
		    if (completedCount == 0 && potentialCount == 0)
		        return;
		
		    int added             = 0;
		    int skippedInactive   = 0;
		    int skippedDuplicate  = 0;
		    int skippedExpired    = 0;
		    int skippedOgPolarity = 0;
		
		    // Completed wins; stable de-dupe across completed + potential
		    HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		
		    void TryAddTriplet(PatternCTriplet t)
		    {
		        if (t == null)
		            return;
		
		        if (!t.IsActive)
		        {
		            skippedInactive++;
		            return;
		        }
		
		        // Use the shared helper so Scan + Rebuild always match exactly
		        string key = MakeTripletKey(t);
		        if (string.IsNullOrEmpty(key))
		            return;
		
		        if (!seen.Add(key))
		        {
		            skippedDuplicate++;
		            return;
		        }
		
		        // Expire candidates 1 bar after the last signal on that triplet:
		        // Keep on signal bar (N) and next bar (N+1). Remove starting at N+2.
		        if (lastSignalBip0BarByKey.TryGetValue(key, out int lastSigBar) && bip0 > lastSigBar + 1)
		        {
		            skippedExpired++;
		            return;
		        }
				
				
				// ---- NEW: Enforce TrendStart body extreme vs OG guide (structural validity) ----
				double eps = TickSize * 0.5;

				
				// ---- END NEW ----
				// End Added Gate for itsCam Change
		
		        var c = new PatternCCandidate
		        {
		            IsLong             = t.IsLong,
		            OgPivotIdx         = t.OgIdx,
		            SupportPivotIdx    = t.SupportIdx,
		            TrendStartPivotIdx = t.TrendStartIdx,
		            OgLevel            = t.OgPrice,
		            SupportGuide       = t.SupportPrice,
		            TrendStartExtreme  = t.TrendStartPrice,
		
		            HasCleared         = false,
		            Aborted            = false,
		            Touched            = false,
		            TouchTime          = default(DateTime),
		            TouchBip0BarIdx    = -1
		        };
		
		        patternCCandidates.Add(c);
		        added++;
		
		    }
		
		    // 1) Completed first (wins)
		    if (completedCount > 0)
		        foreach (var t in patternCTriplets)
		            TryAddTriplet(t);
		
		    // 2) Potential second (only if not already present)
		    if (potentialCount > 0)
		        foreach (var t in patternCTripletsPotential)
		            TryAddTriplet(t);
		
		}
		
		private bool PassesGate_TrendStartBodyBeyondOg(PatternCTriplet t, double eps, out string failReason)
		{
		    failReason = string.Empty;
		
		    if (t == null)
		    {
		        failReason = "t null";
		        return false;
		    }
		
		    int ogIdx = t.OgIdx;
		    int tsIdx = t.TrendStartIdx;
		
		    if (pivotGuidePrices == null || pivotIsHigh == null)
		    {
		        failReason = "pivot lists null";
		        return false;
		    }
		
		    if (ogIdx < 0 || ogIdx >= pivotGuidePrices.Count
		        || tsIdx < 0 || tsIdx >= pivotGuidePrices.Count)
		    {
		        failReason = "idx OOR";
		        return false;
		    }
		
		    double ogGuide = pivotGuidePrices[ogIdx];
		    double tsBody  = pivotGuidePrices[tsIdx]; // bodyLow for long, bodyHigh for short (by construction)
		
		    if (t.IsLong)
		    {
		        // Long: TrendStart BODY LOW must be below OG
		        if (tsBody >= ogGuide - eps)
		        {
		            failReason = "TS body not below OG";
		            return false;
		        }
		    }
		    else
		    {
		        // Short: TrendStart BODY HIGH must be above OG
		        if (tsBody <= ogGuide + eps)
		        {
		            failReason = "TS body not above OG";
		            return false;
		        }
		    }
		
		    return true;
		}

		private bool PassesGate_PreOgSamePolarityBeyondOg(PatternCTriplet t, double eps, out string failReason)
		{
		    failReason = string.Empty;
		
		    if (t == null)
		    {
		        failReason = "t null";
		        return false;
		    }
		
		    int ogIdx = t.OgIdx;
		
		    if (pivotGuidePrices == null || pivotIsHigh == null)
		    {
		        failReason = "pivot lists null";
		        return false;
		    }
		
		    int preOgIdx = ogIdx - 2; // same-polarity pivot before OG in alternating pivot list
		
		    if (ogIdx < 0 || ogIdx >= pivotGuidePrices.Count
		        || preOgIdx < 0 || preOgIdx >= pivotGuidePrices.Count
		        || preOgIdx >= pivotIsHigh.Count)
		    {
		        failReason = "idx OOR";
		        return false;
		    }
		
		    double ogGuide = pivotGuidePrices[ogIdx];
		
		    if (t.IsLong)
		    {
		        // Long: OG is pivot LOW; preOG must be pivot LOW; its BODY LOW must be ABOVE OG
		        if (pivotIsHigh[preOgIdx])
		        {
		            failReason = "preOG not low";
		            return false;
		        }
		
		        double preOgBodyLow = pivotGuidePrices[preOgIdx];
		        if (preOgBodyLow <= ogGuide + eps)
		        {
		            failReason = "preOG bodyLow not above OG";
		            return false;
		        }
		    }
		    else
		    {
		        // Short: OG is pivot HIGH; preOG must be pivot HIGH; its BODY HIGH must be BELOW OG
		        if (!pivotIsHigh[preOgIdx])
		        {
		            failReason = "preOG not high";
		            return false;
		        }
		
		        double preOgBodyHigh = pivotGuidePrices[preOgIdx];
		        if (preOgBodyHigh >= ogGuide - eps)
		        {
		            failReason = "preOG bodyHigh not below OG";
		            return false;
		        }
		    }
		
		    return true;
		}


		private void BuildPatternC(
		    out Dictionary<int, double> newLongByBar,
		    out Dictionary<int, double> newShortByBar,
		    out List<PatternCLevel> newLevels)
		{
		    newLongByBar  = new Dictionary<int, double>();
		    newShortByBar = new Dictionary<int, double>();
		    newLevels     = new List<PatternCLevel>();
		
		    if ((!EnablePatternCSignal && !EnablePatternCLevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 3)
		        return;
		
		    ScanPatternC(newLongByBar, newShortByBar, newLevels);
		}

		
		private void ApplyPatternCResults(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternCLevel> newLevels)
		{
		    if (patternCLongByBar == null)
		        patternCLongByBar = new Dictionary<int, double>();
		    if (patternCShortByBar == null)
		        patternCShortByBar = new Dictionary<int, double>();
		    if (patternCLevels == null)
		        patternCLevels = new List<PatternCLevel>();
		
		    // Signals
		    if (EnablePatternCSignal)
		    {
		        patternCLongByBar.Clear();
		        foreach (var kv in newLongByBar)
		            patternCLongByBar[kv.Key] = kv.Value;
		
		        patternCShortByBar.Clear();
		        foreach (var kv in newShortByBar)
		            patternCShortByBar[kv.Key] = kv.Value;
		    }
		    else
		    {
		        patternCLongByBar.Clear();
		        patternCShortByBar.Clear();
		    }
		
		    // Levels
		    if (EnablePatternCLevels)
		    {
		        patternCLevels.Clear();
		        patternCLevels.AddRange(newLevels);
		    }
		    else
		    {
		        patternCLevels.Clear();
		    }
		}
		
		private void ScanPatternC(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternCLevel> newLevels)
		{
		    patternCTriplets.Clear();
		    patternCTripletsPotential.Clear();
		
		    if ((!EnablePatternCSignal && !EnablePatternCLevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 2)
		        return;
			
			// Clear recording set for historical rebuild
            if (recordedPatternKeys == null)
                recordedPatternKeys = new HashSet<string>(StringComparer.Ordinal);
            else
                recordedPatternKeys.Clear();
		
		    int pivotCount = pivotTimes.Count;
		
		    // Map pivot index -> primary bar index (BIP0)
		    int GetPrimaryBarIndexFromPivot(int pivotIdx)
		    {
		        if (pivotIdx < 0 || pivotIdx >= pivotTimes.Count)
		            return -1;
		
		        return BarsArray[0].GetBar(pivotTimes[pivotIdx]);
		    }
		
		    // We now form Pattern C "triplets" from *two pivots*:
		    // Long:  L-H (OG = L, Support = H)
		    // Short: H-L (OG = H, Support = L)
		    //
		    // TrendStartIdx is now a placeholder; do NOT rely on it in scanning logic.
		
		    for (int i = pivotCount - 1; i >= 1; i--)
		    {
		        int idx1 = i - 1; // OG pivot
		        int idx2 = i;     // Support pivot
		
		        int ogBarIndex = GetPrimaryBarIndexFromPivot(idx1);
		        if (ogBarIndex < 0)
		            continue;
		
		        // Optional lookback filter (BarsToProcess applies to BIP0 bars)
		        if (BarsToProcess > 0)
		        {
		            int barsAgo = CurrentBar - ogBarIndex;
		            if (barsAgo < 0 || barsAgo > BarsToProcess)
		                continue;
		        }
		
		        bool longPair  = !pivotIsHigh[idx1] &&  pivotIsHigh[idx2]; // L-H
		        bool shortPair =  pivotIsHigh[idx1] && !pivotIsHigh[idx2]; // H-L
		
		        if (longPair)
		        {
		            double ogGuide = pivotGuidePrices[idx1];
		
		            var trip = new PatternCTriplet
		            {
		                OgIdx           = idx1,
		                SupportIdx      = idx2,
		                TrendStartIdx   = idx2,                  // placeholder
		                IsLong          = true,
		                IsActive        = true,
		                OgPrice         = ogGuide,               // OG guide
		                SupportPrice    = pivotGuidePrices[idx2],
		                TrendStartPrice = pivotPrices[idx2]      // placeholder
		            };
		
		            if (ScanPatternCFromTriple_Long_PotentialGate(trip))
		                patternCTripletsPotential.Add(trip);
		
		            if (ScanPatternCFromTriple_Long(trip, newLongByBar, newLevels))
		                patternCTriplets.Add(trip);
		        }
		
		        if (shortPair)
		        {
		            double ogGuide = pivotGuidePrices[idx1];
		
		            var trip = new PatternCTriplet
		            {
		                OgIdx           = idx1,
		                SupportIdx      = idx2,
		                TrendStartIdx   = idx2,                  // placeholder
		                IsLong          = false,
		                IsActive        = true,
		                OgPrice         = ogGuide,               // OG guide
		                SupportPrice    = pivotGuidePrices[idx2],
		                TrendStartPrice = pivotPrices[idx2]      // placeholder
		            };
		
		            if (ScanPatternCFromTriple_Short_PotentialGate(trip))
		                patternCTripletsPotential.Add(trip);
		
		            if (ScanPatternCFromTriple_Short(trip, newShortByBar, newLevels))
		                patternCTriplets.Add(trip);
		        }
		    }
		}

		
		private bool TryGetPivotClose_BIP0(int pivotIdx, out double pivotClose)
		{
		    pivotClose = double.NaN;
		
		    if (pivotTimes == null || pivotIdx < 0 || pivotIdx >= pivotTimes.Count)
		        return false;
		
		    int barIndex = BarsArray[0].GetBar(pivotTimes[pivotIdx]);
		    if (barIndex < 0)
		        return false;
		
		    int barsAgo = CurrentBar - barIndex;
		    if (barsAgo < 0 || barsAgo > CurrentBar)
		        return false;
		
		    pivotClose = Close[barsAgo];
		    return true;
		}


		
		// Gate for POTENTIAL Pattern C LONG:
		private bool ScanPatternCFromTriple_Long_PotentialGate(PatternCTriplet trip)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    // Optional: enforce one-shot per OG key even for potentials
		    string key = MakeTripletKey(trip);
		    if (!string.IsNullOrEmpty(key)
		        && lastSignalBip0BarByKey != null
		        && lastSignalBip0BarByKey.TryGetValue(key, out int lastSigBar)
		        && lastSigBar >= 0)
		    {
		        // already signaled once on this OG (one-shot), do not keep as potential candidate
		        return false;
		    }
		
		    // Pattern C has NO "clearance" / "trendstart extreme" gating.
		    // If OG+Support pivots exist, allow it as a potential candidate.
		    return true;
		}

		

		// Gate for POTENTIAL Pattern C SHORT:
		private bool ScanPatternCFromTriple_Short_PotentialGate(PatternCTriplet trip)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    // Optional: enforce one-shot per OG key even for potentials
		    string key = MakeTripletKey(trip);
		    if (!string.IsNullOrEmpty(key)
		        && lastSignalBip0BarByKey != null
		        && lastSignalBip0BarByKey.TryGetValue(key, out int lastSigBar)
		        && lastSigBar >= 0)
		    {
		        return false;
		    }
		
		    return true;
		}
	
		private bool ScanPatternCFromTriple_Long(
		    PatternCTriplet trip,
		    Dictionary<int, double> newLongByBar,
		    List<PatternCLevel> newLevels)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    double eps     = TickSize * 0.5;
		    double ogLevel = trip.OgPrice;
		
		    int ogBarIndex = BarsArray[0].GetBar(pivotTimes[trip.OgIdx]);
		    int tsBarIndex = BarsArray[0].GetBar(pivotTimes[trip.TrendStartIdx]);
		
		    if (ogBarIndex < 0 || tsBarIndex < 0)
		        return false;
		
		    // Optional pivot lookup (barIndex -> isHigh)
		    Dictionary<int, bool> pivotTypeByBar = null;
		    if (RequirePivotForPatternCSignal)
		    {
		        pivotTypeByBar = new Dictionary<int, bool>(128);
		        for (int i = 0; i < pivotTimes.Count; i++)
		        {
		            int bi = BarsArray[0].GetBar(pivotTimes[i]);
		            if (bi >= 0 && !pivotTypeByBar.ContainsKey(bi))
		                pivotTypeByBar[bi] = pivotIsHigh[i];
		        }
		    }
		
		    int startBar = Math.Max(tsBarIndex, ogBarIndex);
		
		    // LONG sequence: close BELOW OG at least once, then close ABOVE OG at least once
		    bool sawCloseBelowOg = false;
		    bool eligibleForWick = false;
		    int  eligibleBarIx   = -1;  // the bar that completed eligibility (MUST NOT be a signal bar)
		
		    bool inWickStreak  = false;
		    int  lastWickBarIx = -1;
            double streakStartTimestamp = 0; // NEW: Track start of consistency
		
		    for (int barIndex = startBar; barIndex < CurrentBar; barIndex++)
		    {
		        int barsAgo = CurrentBar - barIndex;
		        if (barsAgo < 0 || barsAgo > CurrentBar)
		            continue;
		
		        double c  = Close[barsAgo];
		        double lo = Low[barsAgo];
				double o = Open[barsAgo];
		
		        // ---- Eligibility state machine (bar-close based) ----
		        if (!eligibleForWick)
		        {
		            if (!sawCloseBelowOg)
		            {
		                if (c < ogLevel - eps)
		                    sawCloseBelowOg = true;
		            }
		            else
		            {
		                if (c > ogLevel + eps)
		                {
		                    eligibleForWick = true;
		                    eligibleBarIx   = barIndex; // IMPORTANT: do not allow this bar to start the wick streak
		                }
		            }
		        }
		
		        // ---- Wick logic (only after eligibility AND not on the eligibility bar itself) ----
		        if (!eligibleForWick)
		            continue;
		
		        // Critical fix: if RequirePivot==false, we must start signals only AFTER the eligibility bar.
		        if (!RequirePivotForPatternCSignal && barIndex <= eligibleBarIx)
		            continue;
		
		        
				
				double bodyLow = Math.Min(o, c);
				
				bool wickTouchesOg = lo <= ogLevel + eps;
				
				// IMPORTANT: reject body-cross bars
				bool bodyFullyAboveOg = bodyLow >= ogLevel - eps;
				
				bool pivotOk = true;
		
				if (RequirePivotForPatternCSignal)
				{
				    // Pivot LOW required for long
				    if (pivotTypeByBar == null
				        || !pivotTypeByBar.TryGetValue(barIndex, out bool isHigh)
				        || isHigh) // pivot LOW means isHigh == false
				    {
				        pivotOk = false;
				    }
				}
		
				
				bool isWickBar = wickTouchesOg && bodyFullyAboveOg && pivotOk;
		
		
		        if (!inWickStreak)
		        {
		            if (isWickBar)
		            {
		                inWickStreak  = true;
		                lastWickBarIx = barIndex;
		
		                if (EnablePatternCSignal)
                        {
                            // 1. Always show visual signal (Triangle)
                            newLongByBar[barIndex] = lo - (PatternCMarkerOffsetTicks * TickSize);

                            // 2. Record Data Plots (First Occurrence Only)
                            string plotKey = MakeTripletKey(trip) + "_Historical"; 

                            if (!recordedPatternKeys.Contains(plotKey))
                            {
                                recordedPatternKeys.Add(plotKey);
                                
                                // barsAgo is already defined in outer loop
                                if (barsAgo >= 0 && barsAgo < CurrentBar) 
                                {
                                    double preciseTime = 0;
                                    
                                    // 1. Check persistence dictionary (Highest Priority)
                                    if (livePreciseEndTimesL != null && livePreciseEndTimesL.ContainsKey(barIndex))
                                        preciseTime = livePreciseEndTimesL[barIndex];
                                    
                                    // 2. Check existing plot (Reload protection)
                                    else if (PatternCLongEndTimestamp[barsAgo] > 0)
                                        preciseTime = PatternCLongEndTimestamp[barsAgo];
                                    
                                    // 3. Fallback to Bar Time (Historical default)
                                    if (preciseTime == 0)
                                        preciseTime = Times[0][barsAgo].ToOADate();
                                        
                                    // STORE for subsequent bars in streak
                                    streakStartTimestamp = preciseTime;
                                        
                                    // ALWAYS WRITE (ensures upgrade to precise if available)
                                    PatternCLongEndTimestamp[barsAgo]   = preciseTime;
                                    PatternCLongStartTimestamp[barsAgo] = pivotTimes[trip.OgIdx].ToOADate();
                                    PatternCLongLevel[barsAgo]          = ogLevel;
									PatternCLongSignalBarTimestamp[barsAgo] = Times[0][barsAgo].ToOADate();
                                }
                            }
                        }
		            }
		        }
		        else
		        {
		            if (isWickBar)
		            {
		                lastWickBarIx = barIndex;
		
		                // Subsequent wicks in streak
                        // 1. Still show visual signal? Yes, usually.
		                if (EnablePatternCSignal)
                            newLongByBar[barIndex] = lo - (PatternCMarkerOffsetTicks * TickSize);
                            
                        // 2. Record Data Plots using STREAK START timestamp
                        // Only if we captured a start timestamp
                        if (EnablePatternCSignal && streakStartTimestamp > 0 && barsAgo >= 0 && barsAgo < CurrentBar)
                        {
                             PatternCLongEndTimestamp[barsAgo]   = streakStartTimestamp;
                             PatternCLongStartTimestamp[barsAgo] = pivotTimes[trip.OgIdx].ToOADate();
                             PatternCLongLevel[barsAgo]          = ogLevel;
							 PatternCLongSignalBarTimestamp[barsAgo] = Times[0][barsAgo].ToOADate();
                        }
		            }
		            else
		            {
		                // first non-wick bar after streak begins invalidates (one-shot)
		                break;
		            }
		        }
		    }
		
		    if (lastWickBarIx < 0)
		        return false;
		
		    string key = MakeTripletKey(trip);
		    if (!string.IsNullOrEmpty(key))
		        lastSignalBip0BarByKey[key] = lastWickBarIx;
		
		    if (EnablePatternCLevels)
		    {
		        int entryBarsAgo = CurrentBar - lastWickBarIx;
		        if (entryBarsAgo >= 0 && entryBarsAgo <= CurrentBar)
		        {
		            newLevels.Add(new PatternCLevel
		            {
		                IsLong        = true,
		                OgBarIndex    = ogBarIndex,
		                OgTime        = pivotTimes[trip.OgIdx],
		                EntryBarIndex = lastWickBarIx,
		                EntryTime     = Times[0][entryBarsAgo], // Visual line ends at the LAST wick bar
		                Price         = ogLevel
		            });
		        }
		    }
		
		    return true;
		}
		
		private bool ScanPatternCFromTriple_Short(
		    PatternCTriplet trip,
		    Dictionary<int, double> newShortByBar,
		    List<PatternCLevel> newLevels)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    double eps     = TickSize * 0.5;
		    double ogLevel = trip.OgPrice;
		
		    int ogBarIndex = BarsArray[0].GetBar(pivotTimes[trip.OgIdx]);
		    int tsBarIndex = BarsArray[0].GetBar(pivotTimes[trip.TrendStartIdx]);
		
		    if (ogBarIndex < 0 || tsBarIndex < 0)
		        return false;
		
		    Dictionary<int, bool> pivotTypeByBar = null;
		    if (RequirePivotForPatternCSignal)
		    {
		        pivotTypeByBar = new Dictionary<int, bool>(128);
		        for (int i = 0; i < pivotTimes.Count; i++)
		        {
		            int bi = BarsArray[0].GetBar(pivotTimes[i]);
		            if (bi >= 0 && !pivotTypeByBar.ContainsKey(bi))
		                pivotTypeByBar[bi] = pivotIsHigh[i];
		        }
		    }
		
		    int startBar = Math.Max(tsBarIndex, ogBarIndex);
		
		    // SHORT sequence: close ABOVE OG at least once, then close BELOW OG at least once
		    bool sawCloseAboveOg = false;
		    bool eligibleForWick = false;
		    int  eligibleBarIx   = -1; // bar that completed eligibility (MUST NOT be a signal bar)
		
		    bool inWickStreak  = false;
		    int  lastWickBarIx = -1;
            double streakStartTimestamp = 0; // NEW
		
		    for (int barIndex = startBar; barIndex < CurrentBar; barIndex++)
		    {
		        int barsAgo = CurrentBar - barIndex;
		        if (barsAgo < 0 || barsAgo > CurrentBar)
		            continue;
		
		        double c  = Close[barsAgo];
		        double hi = High[barsAgo];
				double o = Open[barsAgo];
		
		        if (!eligibleForWick)
		        {
		            if (!sawCloseAboveOg)
		            {
		                if (c > ogLevel + eps)
		                    sawCloseAboveOg = true;
		            }
		            else
		            {
		                if (c < ogLevel - eps)
		                {
		                    eligibleForWick = true;
		                    eligibleBarIx   = barIndex; // IMPORTANT: do not allow this bar to start the wick streak
		                }
		            }
		        }
		
		        if (!eligibleForWick)
		            continue;
		
		        // Critical fix: if RequirePivot==false, signals begin only AFTER eligibility bar.
		        if (!RequirePivotForPatternCSignal && barIndex <= eligibleBarIx)
		            continue;
		
		      		
				double bodyHigh = Math.Max(o, c);
				
				bool wickTouchesOg = hi >= ogLevel - eps;
				
				// IMPORTANT: reject body-cross bars
				bool bodyFullyBelowOg = bodyHigh <= ogLevel + eps;
				
				bool pivotOk = true;
		
				if (RequirePivotForPatternCSignal)
				{
				    // Pivot HIGH required for short
				    if (pivotTypeByBar == null
				        || !pivotTypeByBar.TryGetValue(barIndex, out bool isHigh)
				        || !isHigh)
				    {
				        pivotOk = false;
				    }
				}
		
				
				bool isWickBar = wickTouchesOg && bodyFullyBelowOg && pivotOk;
		
		        if (!inWickStreak)
		        {
		            if (isWickBar)
		            {
		                inWickStreak  = true;
		                lastWickBarIx = barIndex;
		
		                if (EnablePatternCSignal)
                        {
                            // 1. Always show visual signal (Triangle)
                            newShortByBar[barIndex] = hi + (PatternCMarkerOffsetTicks * TickSize);

                            // 2. Record Data Plots (First Occurrence Only)
                            string plotKey = MakeTripletKey(trip) + "_Historical";

                            if (!recordedPatternKeys.Contains(plotKey))
                            {
                                recordedPatternKeys.Add(plotKey);

                                // barsAgo is already defined in outer loop
                                if (barsAgo >= 0 && barsAgo < CurrentBar)
                                {
                                    double preciseTime = 0;
                                    
                                    // 1. Check persistence dictionary (Highest Priority)
                                    if (livePreciseEndTimesS != null && livePreciseEndTimesS.ContainsKey(barIndex))
                                        preciseTime = livePreciseEndTimesS[barIndex];
                                    
                                    // 2. Check existing plot (Reload protection)
                                    else if (PatternCShortEndTimestamp[barsAgo] > 0)
                                        preciseTime = PatternCShortEndTimestamp[barsAgo];
                                    
                                    // 3. Fallback to Bar Time (Historical default)
                                    if (preciseTime == 0)
                                        preciseTime = Times[0][barsAgo].ToOADate();
                                        
                                    // ALWAYS WRITE
                                    PatternCShortEndTimestamp[barsAgo]   = preciseTime;
                                    PatternCShortStartTimestamp[barsAgo] = pivotTimes[trip.OgIdx].ToOADate();
                                    PatternCShortLevel[barsAgo]          = ogLevel;
									PatternCShortSignalBarTimestamp[barsAgo] = Times[0][barsAgo].ToOADate();
                                }
                            }
                        }
		            }
		        }
		        else
				{
				    if (isWickBar)
				    {
				        lastWickBarIx = barIndex;
				
				        if (EnablePatternCSignal)
				            newShortByBar[barIndex] = hi + (PatternCMarkerOffsetTicks * TickSize);
				
				        if (EnablePatternCSignal && streakStartTimestamp > 0 && barsAgo >= 0 && barsAgo < CurrentBar)
				        {
				            PatternCShortEndTimestamp[barsAgo]   = streakStartTimestamp;
				            PatternCShortStartTimestamp[barsAgo] = pivotTimes[trip.OgIdx].ToOADate();
				            PatternCShortLevel[barsAgo]          = ogLevel;
				            PatternCShortSignalBarTimestamp[barsAgo] = Times[0][barsAgo].ToOADate();
				        }
				    }
				    else
				    {
				        break;
				    }
				}
		    }
		
		    if (lastWickBarIx < 0)
		        return false;
		
		    string key = MakeTripletKey(trip);
		    if (!string.IsNullOrEmpty(key))
		        lastSignalBip0BarByKey[key] = lastWickBarIx;
		
		    if (EnablePatternCLevels)
		    {
		        int entryBarsAgo = CurrentBar - lastWickBarIx;
		        if (entryBarsAgo >= 0 && entryBarsAgo <= CurrentBar)
		        {
		            newLevels.Add(new PatternCLevel
		            {
		                IsLong        = false,
		                OgBarIndex    = ogBarIndex,
		                OgTime        = pivotTimes[trip.OgIdx],
		                EntryBarIndex = lastWickBarIx,
		                EntryTime     = Times[0][entryBarsAgo], // Visual line ends at the LAST wick bar
		                Price         = ogLevel
		            });
		        }
		    }
		
		    return true;
		}


		private void DrawPatternCDebug(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternCLevel> newLevels)
		{
		    if (!CanDrawDebug() || !DrawPatternC)
		        return;
		
		    // ---- SIGNALS (triangles) ----
		    // Clear stale markers (only if we have previous state)
		    if (patternCLongByBar != null)
		        foreach (var kv in patternCLongByBar.ToList())
		            if (!newLongByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNC_L_{kv.Key}");
		
		    if (patternCShortByBar != null)
		        foreach (var kv in patternCShortByBar.ToList())
		            if (!newShortByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNC_S_{kv.Key}");
		
		    // Draw current (or none if disabled)
		    if (EnablePatternCSignal)
		    {
		        foreach (var kv in newLongByBar)
		            DrawPatternCText(true, kv.Key, kv.Value);
		
		        foreach (var kv in newShortByBar)
		            DrawPatternCText(false, kv.Key, kv.Value);
		    }
		    else
		    {
		        // If signals disabled, remove any existing markers
		        if (patternCLongByBar != null)
		            foreach (var kv in patternCLongByBar)
		                RemoveDrawObject($"PATTERNC_L_{kv.Key}");
		
		        if (patternCShortByBar != null)
		            foreach (var kv in patternCShortByBar)
		                RemoveDrawObject($"PATTERNC_S_{kv.Key}");
		    }
		
		    // ---- LEVELS (horizontal lines) ----
		    // For simplicity, clear prior drawn levels and redraw current.
		    // (Still cheap because this is debug-only.)
		    if (patternCLevels != null)
		    {
		        foreach (var lvl in patternCLevels)
		        {
		            string tagOld = lvl.IsLong
		                ? $"PATTERNC_LVL_L_{lvl.OgBarIndex}"
		                : $"PATTERNC_LVL_S_{lvl.OgBarIndex}";
		            RemoveDrawObject(tagOld);
		        }
		    }
		
		    if (EnablePatternCLevels)
		    {
		        foreach (var lvl in newLevels)
		            DrawPatternCLevelLine(lvl);
		    }

            // ---- New Debug Labels ----
            if (ShowDebugLabels)
            {
                 foreach (var kv in newLongByBar)
                 {
                    int barIndex = kv.Key;
                    int barsAgo = CurrentBar - barIndex;
                    if (barsAgo < 0 || barsAgo >= CurrentBar) continue;

                    double level = PatternCLongLevel[barsAgo];
                    // If plot not set (e.g. suppressed secondary occurrence), skip
                    // Check if level is valid (non-zero/non-empty?) Series defaults to 0. 
                    // But level is price, so non-zero.
                    if (level < 0.0001) continue; 

                    double startTs = PatternCLongStartTimestamp[barsAgo];
                    double endTs   = PatternCLongEndTimestamp[barsAgo];

                    DrawPatternCDetailLabel(true, barIndex, level, startTs, endTs);
                 }

                 foreach (var kv in newShortByBar)
                 {
                    int barIndex = kv.Key;
                    int barsAgo = CurrentBar - barIndex;
                    if (barsAgo < 0 || barsAgo >= CurrentBar) continue;

                    double level = PatternCShortLevel[barsAgo];
                    if (level < 0.0001) continue; 

                    double startTs = PatternCShortStartTimestamp[barsAgo];
                    double endTs   = PatternCShortEndTimestamp[barsAgo];

                    DrawPatternCDetailLabel(false, barIndex, level, startTs, endTs);
                 }
            }
		}
		
		private void DrawPatternCDetailLabel(bool isLong, int barIndex, double level, double startTsOA, double endTsOA)
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
            
             string tag = isLong ? $"PC_DET_L_{barIndex}" : $"PC_DET_S_{barIndex}";
             
             // Position: slightly above/below the triangle
             double y = isLong 
                ? Low[barsAgo] - (PatternCMarkerOffsetTicks + 15) * TickSize 
                : High[barsAgo] + (PatternCMarkerOffsetTicks + 15) * TickSize;

             Draw.Text(this, tag, false, text, barsAgo, y, 0,
                isLong ? PatternCLongColor : PatternCShortColor,
                new SimpleFont("Arial", 10),
                TextAlignment.Center,
                Brushes.Transparent,
                Brushes.Transparent,
                0);
        }
		
		private void DrawPatternCText(bool isLong, int barIndex, double price)
		{
		    if (!CanDrawDebug() || !DrawPatternC)
		        return;
		
		    if (barIndex < 0 || barIndex > CurrentBar)
		        return;
		
		    int barsAgo = CurrentBar - barIndex;
		    if (barsAgo < 0)
		        return;
		
		    string tag  = isLong ? $"PATTERNC_L_{barIndex}" : $"PATTERNC_S_{barIndex}";
		    string text = isLong ? "▲" : "▼";
		
		    Brush brush = isLong ? PatternCLongColor : PatternCShortColor;
		
		    // If you want to ensure updates replace old text, clear first
		    RemoveDrawObject(tag);
		
		    Draw.Text(this, tag, false,
		        text,
		        barsAgo,
		        price,
		        0,
		        brush,
		        new SimpleFont("Arial", PatternCMarkerFontSize),
		        TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}
		
		private void DrawPatternCLevelLine(PatternCLevel lvl)
		{
		    if (!CanDrawDebug() || !DrawPatternC)
		        return;
		
		    if (lvl == null)
		        return;
		
		    string tag = lvl.IsLong
		        ? $"PATTERNC_LVL_L_{lvl.OgBarIndex}"
		        : $"PATTERNC_LVL_S_{lvl.OgBarIndex}";
		
		    Brush brush = lvl.IsLong ? PatternCLongColor : PatternCShortColor;
		
		    RemoveDrawObject(tag);
		
		    DateTime t1 = lvl.OgTime;
		    DateTime t2 = (lvl.EntryTime != default(DateTime)) ? lvl.EntryTime : Times[0][0];
		
		    Draw.Line(this, tag, false,
		        t1, lvl.Price,
		        t2, lvl.Price,
		        brush, PatternCLevelDashStyle, PatternCLevelWidth);
		}

		
		private double GetPatternCMarkerOffsetPrice(bool isLong)
		{
		    double ticks = PatternCMarkerOffsetTicks * TickSize;
		    return isLong ? ticks : -ticks;
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
		   
		    // Append time ONLY when enabled (avoid string work otherwise)
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
		    // - Debug drawing must be explicitly enabled
		    if (ChartControl == null)
		        return false;
		
//		    // Any debug visualization enabled?
//		    if (!ShowZigZagDebug && !DebugDrawPatternC /*&& !DebugDrawPatternA */)
//		        return false;
		
		    return true;
		}

        

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenMirrorPtCV0004[] cacheAlightenMirrorPtCV0004;
		public AlightenMirrorPtCV0004 AlightenMirrorPtCV0004(int barsToProcess, bool enablePatternCSignal, bool enablePatternCLevels, bool drawPatternC, int patternCMarkerFontSize, int patternCMarkerOffsetTicks, System.Windows.Media.Brush patternCLongColor, System.Windows.Media.Brush patternCShortColor, int patternCLevelWidth, DashStyleHelper patternCLevelDashStyle, bool requirePivotForPatternCSignal, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return AlightenMirrorPtCV0004(Input, barsToProcess, enablePatternCSignal, enablePatternCLevels, drawPatternC, patternCMarkerFontSize, patternCMarkerOffsetTicks, patternCLongColor, patternCShortColor, patternCLevelWidth, patternCLevelDashStyle, requirePivotForPatternCSignal, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public AlightenMirrorPtCV0004 AlightenMirrorPtCV0004(ISeries<double> input, int barsToProcess, bool enablePatternCSignal, bool enablePatternCLevels, bool drawPatternC, int patternCMarkerFontSize, int patternCMarkerOffsetTicks, System.Windows.Media.Brush patternCLongColor, System.Windows.Media.Brush patternCShortColor, int patternCLevelWidth, DashStyleHelper patternCLevelDashStyle, bool requirePivotForPatternCSignal, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			if (cacheAlightenMirrorPtCV0004 != null)
				for (int idx = 0; idx < cacheAlightenMirrorPtCV0004.Length; idx++)
					if (cacheAlightenMirrorPtCV0004[idx] != null && cacheAlightenMirrorPtCV0004[idx].BarsToProcess == barsToProcess && cacheAlightenMirrorPtCV0004[idx].EnablePatternCSignal == enablePatternCSignal && cacheAlightenMirrorPtCV0004[idx].EnablePatternCLevels == enablePatternCLevels && cacheAlightenMirrorPtCV0004[idx].DrawPatternC == drawPatternC && cacheAlightenMirrorPtCV0004[idx].PatternCMarkerFontSize == patternCMarkerFontSize && cacheAlightenMirrorPtCV0004[idx].PatternCMarkerOffsetTicks == patternCMarkerOffsetTicks && cacheAlightenMirrorPtCV0004[idx].PatternCLongColor == patternCLongColor && cacheAlightenMirrorPtCV0004[idx].PatternCShortColor == patternCShortColor && cacheAlightenMirrorPtCV0004[idx].PatternCLevelWidth == patternCLevelWidth && cacheAlightenMirrorPtCV0004[idx].PatternCLevelDashStyle == patternCLevelDashStyle && cacheAlightenMirrorPtCV0004[idx].RequirePivotForPatternCSignal == requirePivotForPatternCSignal && cacheAlightenMirrorPtCV0004[idx].ShowDebugLabels == showDebugLabels && cacheAlightenMirrorPtCV0004[idx].ShowZigZag == showZigZag && cacheAlightenMirrorPtCV0004[idx].ZigZagColor == zigZagColor && cacheAlightenMirrorPtCV0004[idx].ZigZagWidth == zigZagWidth && cacheAlightenMirrorPtCV0004[idx].ZigZagStyle == zigZagStyle && cacheAlightenMirrorPtCV0004[idx].EqualsInput(input))
						return cacheAlightenMirrorPtCV0004[idx];
			return CacheIndicator<AlightenMirrorPtCV0004>(new AlightenMirrorPtCV0004(){ BarsToProcess = barsToProcess, EnablePatternCSignal = enablePatternCSignal, EnablePatternCLevels = enablePatternCLevels, DrawPatternC = drawPatternC, PatternCMarkerFontSize = patternCMarkerFontSize, PatternCMarkerOffsetTicks = patternCMarkerOffsetTicks, PatternCLongColor = patternCLongColor, PatternCShortColor = patternCShortColor, PatternCLevelWidth = patternCLevelWidth, PatternCLevelDashStyle = patternCLevelDashStyle, RequirePivotForPatternCSignal = requirePivotForPatternCSignal, ShowDebugLabels = showDebugLabels, ShowZigZag = showZigZag, ZigZagColor = zigZagColor, ZigZagWidth = zigZagWidth, ZigZagStyle = zigZagStyle }, input, ref cacheAlightenMirrorPtCV0004);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenMirrorPtCV0004 AlightenMirrorPtCV0004(int barsToProcess, bool enablePatternCSignal, bool enablePatternCLevels, bool drawPatternC, int patternCMarkerFontSize, int patternCMarkerOffsetTicks, System.Windows.Media.Brush patternCLongColor, System.Windows.Media.Brush patternCShortColor, int patternCLevelWidth, DashStyleHelper patternCLevelDashStyle, bool requirePivotForPatternCSignal, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenMirrorPtCV0004(Input, barsToProcess, enablePatternCSignal, enablePatternCLevels, drawPatternC, patternCMarkerFontSize, patternCMarkerOffsetTicks, patternCLongColor, patternCShortColor, patternCLevelWidth, patternCLevelDashStyle, requirePivotForPatternCSignal, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenMirrorPtCV0004 AlightenMirrorPtCV0004(ISeries<double> input , int barsToProcess, bool enablePatternCSignal, bool enablePatternCLevels, bool drawPatternC, int patternCMarkerFontSize, int patternCMarkerOffsetTicks, System.Windows.Media.Brush patternCLongColor, System.Windows.Media.Brush patternCShortColor, int patternCLevelWidth, DashStyleHelper patternCLevelDashStyle, bool requirePivotForPatternCSignal, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenMirrorPtCV0004(input, barsToProcess, enablePatternCSignal, enablePatternCLevels, drawPatternC, patternCMarkerFontSize, patternCMarkerOffsetTicks, patternCLongColor, patternCShortColor, patternCLevelWidth, patternCLevelDashStyle, requirePivotForPatternCSignal, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenMirrorPtCV0004 AlightenMirrorPtCV0004(int barsToProcess, bool enablePatternCSignal, bool enablePatternCLevels, bool drawPatternC, int patternCMarkerFontSize, int patternCMarkerOffsetTicks, System.Windows.Media.Brush patternCLongColor, System.Windows.Media.Brush patternCShortColor, int patternCLevelWidth, DashStyleHelper patternCLevelDashStyle, bool requirePivotForPatternCSignal, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenMirrorPtCV0004(Input, barsToProcess, enablePatternCSignal, enablePatternCLevels, drawPatternC, patternCMarkerFontSize, patternCMarkerOffsetTicks, patternCLongColor, patternCShortColor, patternCLevelWidth, patternCLevelDashStyle, requirePivotForPatternCSignal, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenMirrorPtCV0004 AlightenMirrorPtCV0004(ISeries<double> input , int barsToProcess, bool enablePatternCSignal, bool enablePatternCLevels, bool drawPatternC, int patternCMarkerFontSize, int patternCMarkerOffsetTicks, System.Windows.Media.Brush patternCLongColor, System.Windows.Media.Brush patternCShortColor, int patternCLevelWidth, DashStyleHelper patternCLevelDashStyle, bool requirePivotForPatternCSignal, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenMirrorPtCV0004(input, barsToProcess, enablePatternCSignal, enablePatternCLevels, drawPatternC, patternCMarkerFontSize, patternCMarkerOffsetTicks, patternCLongColor, patternCShortColor, patternCLevelWidth, patternCLevelDashStyle, requirePivotForPatternCSignal, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

#endregion

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
    public class AlightenLevelMultiTimeframeMATickPatternBV0002 : Indicator
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

        // ---- V00029-style “export pivot” DTO pattern (serialization-safe) ----
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
		
		// Pattern B results (MA-friendly persistent state)
		// barIndex is primary-series (BIP 0) absolute bar index (0..CurrentBar)
		private Dictionary<int, double> patternBLongByBar;
		private Dictionary<int, double> patternBShortByBar;
		
		// Pattern B derived levels (logical objects; optionally drawn in debug)
		private List<PatternBLevel> patternBLevels;
		
		// Pattern B structure tracking (triplets/pivots). This mirrors the V00029 concept.
		// These are used inside ScanPatternB() to detect OG/support/regional relationships.
		private List<PatternBTriplet> patternBTriplets;
		private List<PatternBTriplet> patternBTripletsPotential; // potential (structural-only)

		
		// Represents a single Pattern B triple (OG, Support, TrendStart)
		private class PatternBTriplet
		{
		    public int    OgIdx;
		    public int    SupportIdx;
		    public int    TrendStartIdx;
		    public bool   IsLong;
		    public bool   IsActive;
		
		    public double OgPrice;         // OG guide price
		    public double SupportPrice;    // Support guide price
		    public double TrendStartPrice; // TrendStart extreme (actual pivot price)
		
		    public PatternBTriplet() { }
		}
		
		// Represents a horizontal Pattern B level to draw (at OG price), ending at the wick/entry pivot
		private class PatternBLevel
		{
		    public bool     IsLong;
		    public int      OgBarIndex;
		    public DateTime OgTime;
		
		    public int      EntryBarIndex;
		    public DateTime EntryTime;
		
		    public double   Price; // OG level (guide)
		
		    public PatternBLevel() { }
		}
		
		// ---- Pattern B proximity candidates (evaluated/updated on BIP 1) ----
		private class PatternBCandidate
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
		
		    public PatternBCandidate() { }
		}
		
		private List<PatternBCandidate> patternBCandidates;
		
		private PatternBTriplet activePatternBLong;
		private PatternBTriplet activePatternBShort;
		
		private long _lastMapDebugTicks = 0;
		
		private Dictionary<string, int> lastSignalBip0BarByKey;

		private string MakeTripletKey(PatternBTriplet t)
		{
		    if (t == null) return string.Empty;
		    return (t.IsLong ? "L" : "S") + "|" + t.OgIdx + "|" + t.SupportIdx + "|" + t.TrendStartIdx;
		}
		
		// --- Pattern B live recency (BIP0) state ---
		private bool pbLivePrevLong  = false;
		private bool pbLivePrevShort = false;
		
		private int  pbLiveLongLastEdgeSeq  = -1;   // last tick-seq where long became true (false->true)
		private int  pbLiveShortLastEdgeSeq = -1;   // last tick-seq where short became true (false->true)
		private int  pbLiveTickSeq          = 0;    // increments each BIP0 call within a bar
		
		private int  pbLiveLastChosenDir    = 0;    // +1 long, -1 short, 0 none (stability tie-break)

	
        #endregion
		
        #region Properties
		

        [NinjaScriptProperty]
		[Range(0, 500000)]
		[Display(Name="Bars To Process", GroupName="ZigZag", Order=53)]
		public int BarsToProcess { get; set; } = 200; // 0 = process all
		
		// ---- Pattern B toggles ----

		[NinjaScriptProperty]
		[Display(Name="Enable Pattern B Signals", GroupName="Pattern B", Order=1)]
		public bool EnablePatternBSignal { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Enable Pattern B Levels", GroupName="Pattern B", Order=2)]
		public bool EnablePatternBLevels { get; set; } = true;
	
		[NinjaScriptProperty]
		[Display(Name = "Draw Pattern B", GroupName = "Pattern B", Order = 3)]
		public bool DrawPatternB { get; set; } = true;
		// Optional styling knobs (safe defaults)
		[NinjaScriptProperty]
		[Range(6, 30)]
		[Display(Name="Pattern B Marker Font Size", GroupName="Pattern B", Order=20)]
		public int PatternBMarkerFontSize { get; set; } = 18;
		
		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name="Pattern B Marker Offset Ticks", GroupName="Pattern B", Order=21)]
		public int PatternBMarkerOffsetTicks { get; set; } = 10;
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern B Long Color", GroupName="Pattern B", Order=22)]
		public System.Windows.Media.Brush PatternBLongColor { get; set; } = Brushes.DodgerBlue;
		
		[Browsable(false)]
		public string PatternBLongColorSerialize
		{
		    get => Serialize.BrushToString(PatternBLongColor);
		    set => PatternBLongColor = Serialize.StringToBrush(value);
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern B Short Color", GroupName="Pattern B", Order=23)]
		public System.Windows.Media.Brush PatternBShortColor { get; set; } = Brushes.Gold;
		
		[Browsable(false)]
		public string PatternBShortColorSerialize
		{
		    get => Serialize.BrushToString(PatternBShortColor);
		    set => PatternBShortColor = Serialize.StringToBrush(value);
		}
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Pattern B Level Width", GroupName="Pattern B", Order=30)]
		public int PatternBLevelWidth { get; set; } = 2;
		
		[NinjaScriptProperty]
		[Display(Name="Pattern B Level Dash", GroupName="Pattern B", Order=31)]
		public DashStyleHelper PatternBLevelDashStyle { get; set; } = DashStyleHelper.Dash;
		
		[NinjaScriptProperty]
		[Display(Name="Gate: Pre-OG Check", GroupName="Pattern B", Order=90)]
		public bool GatePreOgCheck { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name="Gate: TrendStart Check", GroupName="Pattern B", Order=91)]
		public bool GateTrendStartCheck { get; set; } = true;

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
		
		private Series<double> patternBSignal;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternBSignal
		{
		    get { return patternBSignal; }
		}
		
		private Series<double> patternBSignalMA;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternBSignalMA
		{
		    get { return patternBSignalMA; }
		}
		
		#endregion


        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AlightenLevelMultiTimeframeMATickPatternBV0002";
                Description = "Market Analyzer core: HTF ZigZag/levels on primary series with 1m/5m proximity columns.";
                Calculate   = Calculate.OnEachTick;

                // For MA, overlay doesn’t matter. For chart debugging, overlay helps.
                IsOverlay               		= true;
                DrawOnPricePanel        		= true;

                DisplayInDataBox        		= true;
                PaintPriceMarkers       		= false;
                IsSuspendedWhileInactive 		= false;
				ShowTransparentPlotsInDataBox 	= true;

				AddPlot(Brushes.Transparent, "PatternBSignal");
				AddPlot(Brushes.Transparent, "PatternBSignalMA");
                
            }
            else if (State == State.Configure)
            {
                instanceId = $"ZZ_MA_{Guid.NewGuid()}";
            }
            else if (State == State.DataLoaded)
            {
                pivotTimes       				= new List<DateTime>(Math.Min(400, BarsToProcess));
                pivotPrices      				= new List<double>(Math.Min(400, BarsToProcess));
                pivotIsHigh      				= new List<bool>(Math.Min(400, BarsToProcess));
                pivotGuidePrices 				= new List<double>(Math.Min(400, BarsToProcess));

                exportPivots     				= new List<ExportPivot>(Math.Min(400, BarsToProcess));

                zzTags 							= new HashSet<string>();
				
				patternBLongByBar  				= new Dictionary<int, double>(256);
				patternBShortByBar 				= new Dictionary<int, double>(256);
				patternBLevels     				= new List<PatternBLevel>(128);
				patternBTriplets   				= new List<PatternBTriplet>(128);
				patternBTripletsPotential   	= new List<PatternBTriplet>(128);
				patternBCandidates 				= new List<PatternBCandidate>(128);
				
				lastSignalBip0BarByKey = new Dictionary<string, int>(512, StringComparer.Ordinal);

				patternBSignal					= Values[0];
				patternBSignalMA				= Values[1];

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
		    // ---- BIP 0 (HTF / chart timeframe) ----
		    if (BarsInProgress == 0)
		    {
		        // NEW: Live intrabar check runs EVERY tick on BIP0
		        if (EnablePatternBSignal)
		        {
		            //Print($"[LIVE] t={Times[0][0]:HH:mm:ss.fff} Close0={Close[0]}");
		            UpdatePatternB_Live_BIP0();
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
		        RebuildPatternB_BIP0();
		
		        if (EnablePatternBSignal)
		        {
		            // Default clear
		            patternBSignal[0]   = 0;
		            patternBSignalMA[0] = 0;
		
		            if (CurrentBars[0] < 1)
		                return;
		
		            int sigBar = CurrentBars[0] - 1;
		
		            bool longNow  = patternBLongByBar  != null && patternBLongByBar.ContainsKey(sigBar);
		            bool shortNow = patternBShortByBar != null && patternBShortByBar.ContainsKey(sigBar);
		
		            double sig = 0;
		            if (longNow && !shortNow) sig = +1;
		            else if (shortNow && !longNow) sig = -1;
		
		            // DataBox / chart-aligned plot (belongs to bar that just closed)
		            patternBSignal[1] = sig;
		
		            // MA-friendly plot (belongs to current bar so MA reads [0] reliably)
		            patternBSignalMA[0] = sig;
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
		
		#region Pattern B
		
		// Live tags (kept distinct from your historical per-bar tags)
		private const string PB_LIVE_TAG_L = "PATTERNB_LIVE_L";
		private const string PB_LIVE_TAG_S = "PATTERNB_LIVE_S";
		private const string PB_LIVE_LVL_L = "PATTERNB_LIVE_LVL_L";
		private const string PB_LIVE_LVL_S = "PATTERNB_LIVE_LVL_S";

		private void UpdatePatternB_Live_BIP0()
		{
		    // Per-bar reset for recency tracking (BIP0)
		    if (IsFirstTickOfBar)
		    {
		        pbLivePrevLong           = false;
		        pbLivePrevShort          = false;
		        pbLiveLongLastEdgeSeq    = -1;
		        pbLiveShortLastEdgeSeq   = -1;
		        pbLiveTickSeq            = 0;
		        pbLiveLastChosenDir      = 0;
		    }
		    else
		    {
		        pbLiveTickSeq++;
		    }
		
		    // Safety
		    if (CurrentBar < 1)
		    {
		        patternBSignal[0] = 0;
		        // patternBSignalMA[0] = 0;   // by design, keep MA OnBarClose
		
		        pbLivePrevLong  = false;
		        pbLivePrevShort = false;
		
		        if (CanDrawDebug() && DrawPatternB)
		        {
		            RemoveDrawObject(PB_LIVE_TAG_L);
		            RemoveDrawObject(PB_LIVE_TAG_S);
		            RemoveDrawObject(PB_LIVE_LVL_L);
		            RemoveDrawObject(PB_LIVE_LVL_S);
		        }
		        return;
		    }
		
		    // If candidates haven't been built yet this bar, just clear live.
		    if (patternBCandidates == null || patternBCandidates.Count == 0)
		    {
		        patternBSignal[0] = 0;
		
		        pbLivePrevLong  = false;
		        pbLivePrevShort = false;
		
		        if (CanDrawDebug() && DrawPatternB)
		        {
		            RemoveDrawObject(PB_LIVE_TAG_L);
		            RemoveDrawObject(PB_LIVE_TAG_S);
		            RemoveDrawObject(PB_LIVE_LVL_L);
		            RemoveDrawObject(PB_LIVE_LVL_S);
		        }
		        return;
		    }
		
		    // Need pivots to anchor the OG start time
		    if (pivotTimes == null || pivotTimes.Count == 0)
		    {
		        patternBSignal[0] = 0;
		
		        pbLivePrevLong  = false;
		        pbLivePrevShort = false;
		
		        if (CanDrawDebug() && DrawPatternB)
		        {
		            RemoveDrawObject(PB_LIVE_TAG_L);
		            RemoveDrawObject(PB_LIVE_TAG_S);
		            RemoveDrawObject(PB_LIVE_LVL_L);
		            RemoveDrawObject(PB_LIVE_LVL_S);
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
		
		    double bestLongDistTicks  = double.MaxValue;
		    double bestShortDistTicks = double.MaxValue;
		
		    double bestLongOg  = double.NaN;
		    double bestShortOg = double.NaN;
		
		    int bestLongOgPivotIdx  = -1;
		    int bestShortOgPivotIdx = -1;
		
		    foreach (var c in patternBCandidates)
		    {
		        if (c == null || c.Aborted)
		            continue;
		
		        double og = c.OgLevel;
		
		        if (c.IsLong)
		        {
		            // Long touch: wick touches OG and body stays above OG
		            bool wickTouches = l0 <= og + eps;
		            bool bodyAbove   = bodyLow >= og - eps;
		
		            if (wickTouches && bodyAbove)
		            {
		                double distTicks = Math.Abs(c0 - og) / TickSize;
		                if (distTicks < bestLongDistTicks)
		                {
		                    bestLongDistTicks  = distTicks;
		                    bestLongOg         = og;
		                    bestLongOgPivotIdx = c.OgPivotIdx;
		                    liveLong           = true;
		                }
		            }
		        }
		        else
		        {
		            // Short touch: wick touches OG and body stays below OG
		            bool wickTouches = h0 >= og - eps;
		            bool bodyBelow   = bodyHigh <= og + eps;
		
		            if (wickTouches && bodyBelow)
		            {
		                double distTicks = Math.Abs(c0 - og) / TickSize;
		                if (distTicks < bestShortDistTicks)
		                {
		                    bestShortDistTicks  = distTicks;
		                    bestShortOg         = og;
		                    bestShortOgPivotIdx = c.OgPivotIdx;
		                    liveShort           = true;
		                }
		            }
		        }
		    }
		
		    // --- Recency tracking: capture false->true edges for each side on this bar ---
		    if (liveLong && !pbLivePrevLong)
		        pbLiveLongLastEdgeSeq = pbLiveTickSeq;
		
		    if (liveShort && !pbLivePrevShort)
		        pbLiveShortLastEdgeSeq = pbLiveTickSeq;
		
		    pbLivePrevLong  = liveLong;
		    pbLivePrevShort = liveShort;
		
		    // --- Choose liveSig for the plot (single scalar), but DRAW both sides when both are true ---
		    double liveSig = 0;
		
		    if (liveLong && !liveShort)
		    {
		        liveSig = +1;
		        pbLiveLastChosenDir = +1;
		    }
		    else if (liveShort && !liveLong)
		    {
		        liveSig = -1;
		        pbLiveLastChosenDir = -1;
		    }
		    else if (liveLong && liveShort)
		    {
		        // Recency wins
		        if (pbLiveLongLastEdgeSeq > pbLiveShortLastEdgeSeq)
		        {
		            liveSig = +1;
		            pbLiveLastChosenDir = +1;
		        }
		        else if (pbLiveShortLastEdgeSeq > pbLiveLongLastEdgeSeq)
		        {
		            liveSig = -1;
		            pbLiveLastChosenDir = -1;
		        }
		        else
		        {
		            // Tie-break by penetration beyond OG on this tick
		            double longPen  = 0;
		            double shortPen = 0;
		
		            if (!double.IsNaN(bestLongOg))
		                longPen = (bestLongOg - l0) / TickSize;     // >= 0 when touched
		
		            if (!double.IsNaN(bestShortOg))
		                shortPen = (h0 - bestShortOg) / TickSize;   // >= 0 when touched
		
		            if (longPen > shortPen + 0.0001)
		            {
		                liveSig = +1;
		                pbLiveLastChosenDir = +1;
		            }
		            else if (shortPen > longPen + 0.0001)
		            {
		                liveSig = -1;
		                pbLiveLastChosenDir = -1;
		            }
		            else
		            {
		                // Still tied: stabilize
		                if (pbLiveLastChosenDir == +1)
		                    liveSig = +1;
		                else if (pbLiveLastChosenDir == -1)
		                    liveSig = -1;
		                else
		                {
		                    // last resort: nearer-to-close
		                    if (bestLongDistTicks < bestShortDistTicks)
		                    {
		                        liveSig = +1;
		                        pbLiveLastChosenDir = +1;
		                    }
		                    else if (bestShortDistTicks < bestLongDistTicks)
		                    {
		                        liveSig = -1;
		                        pbLiveLastChosenDir = -1;
		                    }
		                    else
		                    {
		                        liveSig = 0;
		                        pbLiveLastChosenDir = 0;
		                    }
		                }
		            }
		        }
		    }
		    else
		    {
		        liveSig = 0;
		        pbLiveLastChosenDir = 0;
		    }
		
		    // LIVE plot (chart)
		    patternBSignal[0] = liveSig;
		    // patternBSignalMA[0] = liveSig;  // by design, keep MA OnBarClose only
		
		    // --- Draw live artifacts (both sides allowed) ---
		    if (CanDrawDebug() && DrawPatternB)
		    {
		        // Clear all live artifacts first
		        RemoveDrawObject(PB_LIVE_TAG_L);
		        RemoveDrawObject(PB_LIVE_TAG_S);
		        RemoveDrawObject(PB_LIVE_LVL_L);
		        RemoveDrawObject(PB_LIVE_LVL_S);
		
		        DateTime t2 = Times[0][0];
		
		        // LONG side
		        if (liveLong && !double.IsNaN(bestLongOg))
		        {
		            DateTime t1 = t2;
		
		            if (bestLongOgPivotIdx >= 0 && bestLongOgPivotIdx < pivotTimes.Count)
		            {
		                t1 = pivotTimes[bestLongOgPivotIdx];
		                int ogBarIndex = BarsArray[0].GetBar(t1);
		                if (ogBarIndex < 0)
		                    t1 = t2;
		            }
		
		            Draw.Line(this, PB_LIVE_LVL_L, false,
		                t1, bestLongOg,
		                t2, bestLongOg,
		                PatternBLongColor, PatternBLevelDashStyle, PatternBLevelWidth);
		
		            double yL = l0 - (PatternBMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PB_LIVE_TAG_L, false,
		                "▲",
		                0,
		                yL,
		                0,
		                PatternBLongColor,
		                new SimpleFont("Arial", PatternBMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);
		        }
		
		        // SHORT side
		        if (liveShort && !double.IsNaN(bestShortOg))
		        {
		            DateTime t1 = t2;
		
		            if (bestShortOgPivotIdx >= 0 && bestShortOgPivotIdx < pivotTimes.Count)
		            {
		                t1 = pivotTimes[bestShortOgPivotIdx];
		                int ogBarIndex = BarsArray[0].GetBar(t1);
		                if (ogBarIndex < 0)
		                    t1 = t2;
		            }
		
		            Draw.Line(this, PB_LIVE_LVL_S, false,
		                t1, bestShortOg,
		                t2, bestShortOg,
		                PatternBShortColor, PatternBLevelDashStyle, PatternBLevelWidth);
		
		            double yS = h0 + (PatternBMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PB_LIVE_TAG_S, false,
		                "▼",
		                0,
		                yS,
		                0,
		                PatternBShortColor,
		                new SimpleFont("Arial", PatternBMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);
		        }
		    }
		}


		private void RebuildPatternB_BIP0()
		{
		    if (!EnablePatternBSignal && !EnablePatternBLevels)
		        return;
		
		    BuildPatternB(out var newPBLong, out var newPBShort, out var newPBLevels);
		
		    ApplyPatternBResults(newPBLong, newPBShort, newPBLevels);
			
			RebuildPatternBCandidates_BIP0();
		
		    // chart-only
		    if (DrawPatternB && CanDrawDebug())
		        DrawPatternBDebug(newPBLong, newPBShort, newPBLevels);
		}
		
		private void RebuildPatternBCandidates_BIP0()
		{
		    if (patternBCandidates == null)
		        patternBCandidates = new List<PatternBCandidate>(128);
		
		    patternBCandidates.Clear();
		
		    // Ensure map exists (safe for chart + MA)
		    if (lastSignalBip0BarByKey == null)
		        lastSignalBip0BarByKey = new Dictionary<string, int>(256, StringComparer.Ordinal);
		
		    int      bip0 = CurrentBars[0];
		    DateTime t0   = Times[0][0];
		
		    int completedCount = (patternBTriplets == null) ? 0 : patternBTriplets.Count;
		    int potentialCount = (patternBTripletsPotential == null) ? 0 : patternBTripletsPotential.Count;
		
		
		    if (completedCount == 0 && potentialCount == 0)
		        return;
		
		    int added             = 0;
		    int skippedInactive   = 0;
		    int skippedDuplicate  = 0;
		    int skippedExpired    = 0;
		    int skippedOgPolarity = 0;
		
		    // Completed wins; stable de-dupe across completed + potential
		    HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		
		    void TryAddTriplet(PatternBTriplet t)
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
				
				// Added Gate for itsCam Change
		        // ---- NEW: Enforce OG pivot polarity (structural validity) ----
//				double eps = TickSize * 0.5;

//		        int ogIdx = t.OgIdx;
//		        if (ogIdx < 0 || ogIdx >= pivotIsHigh.Count)
//		        {
//		            // If indices are out of range, skip rather than crash / admit nonsense
//		            skippedOgPolarity++;		            
//		            return;
//		        }
		
//		        bool ogIsHigh = pivotIsHigh[ogIdx];
		
//		        if (t.IsLong && ogIsHigh)
//		        {
//		            skippedOgPolarity++;
//		            return;
//		        }
		
//		        if (!t.IsLong && !ogIsHigh)
//		        {
//		            skippedOgPolarity++;		           
//		            return;
//		        }
		        // ---- END NEW ----
				
				// ---- NEW: Enforce TrendStart body extreme vs OG guide (structural validity) ----
				double eps = TickSize * 0.5;

				// Gate A: Pre-OG structural requirement (new change)
				if (GatePreOgCheck && !PassesGate_PreOgSamePolarityBeyondOg(t, eps, out _))
				{
				    skippedOgPolarity++;
				    return;
				}
				
				// Gate B: TrendStart-vs-OG structural requirement (previous change)
				if (GateTrendStartCheck && !PassesGate_TrendStartBodyBeyondOg(t, eps, out _))
				{
				    skippedOgPolarity++;
				    return;
				}
				// ---- END NEW ----
				// End Added Gate for itsCam Change
		
		        var c = new PatternBCandidate
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
		
		        patternBCandidates.Add(c);
		        added++;
		
		    }
		
		    // 1) Completed first (wins)
		    if (completedCount > 0)
		        foreach (var t in patternBTriplets)
		            TryAddTriplet(t);
		
		    // 2) Potential second (only if not already present)
		    if (potentialCount > 0)
		        foreach (var t in patternBTripletsPotential)
		            TryAddTriplet(t);
		
		}
		
		private bool PassesGate_TrendStartBodyBeyondOg(PatternBTriplet t, double eps, out string failReason)
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

		private bool PassesGate_PreOgSamePolarityBeyondOg(PatternBTriplet t, double eps, out string failReason)
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


		private void BuildPatternB(
		    out Dictionary<int, double> newLongByBar,
		    out Dictionary<int, double> newShortByBar,
		    out List<PatternBLevel> newLevels)
		{
		    newLongByBar  = new Dictionary<int, double>();
		    newShortByBar = new Dictionary<int, double>();
		    newLevels     = new List<PatternBLevel>();
		
		    if ((!EnablePatternBSignal && !EnablePatternBLevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 3)
		        return;
		
		    ScanPatternB(newLongByBar, newShortByBar, newLevels);
		}

		
		private void ApplyPatternBResults(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternBLevel> newLevels)
		{
		    if (patternBLongByBar == null)
		        patternBLongByBar = new Dictionary<int, double>();
		    if (patternBShortByBar == null)
		        patternBShortByBar = new Dictionary<int, double>();
		    if (patternBLevels == null)
		        patternBLevels = new List<PatternBLevel>();
		
		    // Signals
		    if (EnablePatternBSignal)
		    {
		        patternBLongByBar.Clear();
		        foreach (var kv in newLongByBar)
		            patternBLongByBar[kv.Key] = kv.Value;
		
		        patternBShortByBar.Clear();
		        foreach (var kv in newShortByBar)
		            patternBShortByBar[kv.Key] = kv.Value;
		    }
		    else
		    {
		        patternBLongByBar.Clear();
		        patternBShortByBar.Clear();
		    }
		
		    // Levels
		    if (EnablePatternBLevels)
		    {
		        patternBLevels.Clear();
		        patternBLevels.AddRange(newLevels);
		    }
		    else
		    {
		        patternBLevels.Clear();
		    }
		}
		
		private void ScanPatternB(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternBLevel> newLevels)
		{
		    patternBTriplets.Clear();
			patternBTripletsPotential.Clear();

		    if ((!EnablePatternBSignal && !EnablePatternBLevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 3)
		        return;
		
		    int pivotCount = pivotTimes.Count;
		    double eps = TickSize * 0.5;
		
		    // Map pivot index -> primary bar index (BIP0)
		    int GetPrimaryBarIndexFromPivot(int pivotIdx)
		    {
		        if (pivotIdx < 0 || pivotIdx >= pivotTimes.Count)
		            return -1;
		
		        return BarsArray[0].GetBar(pivotTimes[pivotIdx]);
		    }
		
		    // Start from most recent pivot and go backwards (like V00029)
		    for (int i = pivotCount - 1; i >= 2; i--)
		    {
		        int idx1 = i - 2;
		        int idx2 = i - 1;
		        int idx3 = i;
		
		        // Optional lookback filter (BarsToProcess applies to BIP0 bars)
		        int ogBarIndex = GetPrimaryBarIndexFromPivot(idx1);
		        if (ogBarIndex < 0)
		            continue;
		
		        if (BarsToProcess > 0)
		        {
		            int barsAgo = CurrentBar - ogBarIndex;
		            if (barsAgo < 0 || barsAgo > BarsToProcess)
		                continue;
		        }
		
		        bool isLongTriple  = !pivotIsHigh[idx1] &&  pivotIsHigh[idx2] && !pivotIsHigh[idx3]; // L-H-L
		        bool isShortTriple =  pivotIsHigh[idx1] && !pivotIsHigh[idx2] &&  pivotIsHigh[idx3]; // H-L-H
		
		        // LONG triple: L-H-L with low3 < low1  (TrendStart Up at idx3)
		        if (isLongTriple)
		        {
		            double p1 = pivotPrices[idx1];
					double p3 = pivotPrices[idx3];
					
					if (p3 < p1 - eps)
					{
					    double ogGuide = pivotGuidePrices[idx1];
					
					    var trip = new PatternBTriplet
					    {
					        OgIdx           = idx1,
					        SupportIdx      = idx2,
					        TrendStartIdx   = idx3,
					        IsLong          = true,
					        IsActive        = true,
					        OgPrice         = ogGuide,              // OG guide
					        SupportPrice    = pivotGuidePrices[idx2],
					        TrendStartPrice = p3
					    };
					
					    // NEW: optional structural gates (shared with Rebuild)
					    if (GateTrendStartCheck && !PassesGate_TrendStartBodyBeyondOg(trip, eps, out _))
					        continue;
					
					    if (GatePreOgCheck && !PassesGate_PreOgSamePolarityBeyondOg(trip, eps, out _))
					        continue;
					
					    if (ScanPatternBFromTriple_Long_PotentialGate(trip))
					        patternBTripletsPotential.Add(trip);
					
					    if (ScanPatternBFromTriple_Long(trip, newLongByBar, newLevels))
					        patternBTriplets.Add(trip);
					}


		        }
		
		        // SHORT triple: H-L-H with high3 > high1 (TrendStart Down at idx3)
		        if (isShortTriple)
		        {
		            double p1 = pivotPrices[idx1];
					double p3 = pivotPrices[idx3];
					
					if (p3 > p1 + eps)
					{
					    double ogGuide = pivotGuidePrices[idx1];
					
					    var trip = new PatternBTriplet
					    {
					        OgIdx           = idx1,
					        SupportIdx      = idx2,
					        TrendStartIdx   = idx3,
					        IsLong          = false,
					        IsActive        = true,
					        OgPrice         = ogGuide,              // OG guide
					        SupportPrice    = pivotGuidePrices[idx2],
					        TrendStartPrice = p3
					    };
					
					    // NEW: optional structural gates (shared with Rebuild)
					    if (GateTrendStartCheck && !PassesGate_TrendStartBodyBeyondOg(trip, eps, out _))
					        continue;
					
					    if (GatePreOgCheck && !PassesGate_PreOgSamePolarityBeyondOg(trip, eps, out _))
					        continue;
					
					    if (ScanPatternBFromTriple_Short_PotentialGate(trip))
					        patternBTripletsPotential.Add(trip);
					
					    if (ScanPatternBFromTriple_Short(trip, newShortByBar, newLevels))
					        patternBTriplets.Add(trip);
					}
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


		
		// Gate for POTENTIAL Pattern B LONG:
		// Returns true once the pattern has "cleared/armed" (pivot guide cleared support guide),
		// returns false if it aborts (new extreme beyond TrendStart) or never clears.
		private bool ScanPatternBFromTriple_Long_PotentialGate(PatternBTriplet trip)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    string key = MakeTripletKey(trip);
		
		    double eps           = TickSize * 0.5;
		    double supportGuide  = trip.SupportPrice;
		    double trendStartLow = trip.TrendStartPrice;
		
		    bool hasClearedSupport = false;
		    int  armedAtJ          = -1;
		
		    // Move forward from TrendStartIdx+1 through future pivots
		    for (int j = trip.TrendStartIdx + 1; j < pivotTimes.Count; j++)
		    {
		        if (!pivotIsHigh[j])
		        {
		            // LOW pivot: abort if new low breaks TrendStart low before arming
		            double lowPrice = pivotPrices[j];
		            if (lowPrice < trendStartLow - eps)
		            {
		                return false;
		            }
		            // otherwise continue; arming happens on HIGH pivots (guide > supportGuide)
		        }
		        else
		        {
		            // HIGH pivot: “cleared support” once body/guide gets above Support guide
		            double highGuide = pivotGuidePrices[j];
		            if (highGuide > supportGuide + eps)
		            {
		                hasClearedSupport = true;
		                armedAtJ = j;
		                break;
		            }
		        }
		    }
		
		    if (!hasClearedSupport)
		    {
		        return false;
		    }
		
		    // ---- NEW FUNCTIONAL FIX: Reject potentials invalidated AFTER arming ----
		    // After arming, if any later LOW pivot breaks TrendStartLow, the potential is no longer valid.
		    for (int k = armedAtJ + 1; k < pivotTimes.Count; k++)
		    {
		        if (pivotIsHigh[k])
		            continue;
		
		        double lowPrice = pivotPrices[k];
		        if (lowPrice < trendStartLow - eps)
		        {
		            return false; // <-- the fix
		        }
		    }
		
//		    Print($"[PB POT GATE L:POSTARM OK] key={key} armedAtJ={armedAtJ} armedAtT={pivotTimes[armedAtJ]:MM-dd HH:mm}");
		    return true;
		}
		

		// Gate for POTENTIAL Pattern B SHORT:
		// Returns true once the pattern has "cleared/armed" (pivot guide cleared resistance guide),
		// returns false if it aborts (new extreme beyond TrendStart) or never clears.
		private bool ScanPatternBFromTriple_Short_PotentialGate(PatternBTriplet trip)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    string key = MakeTripletKey(trip);
		
		    double eps             = TickSize * 0.5;
		    double resistanceGuide = trip.SupportPrice;
		    double trendStartHigh  = trip.TrendStartPrice;
		
		    bool hasClearedResistance = false;
		    int  armedAtJ             = -1;
		
		    // Move forward from TrendStartIdx+1 through future pivots
		    for (int j = trip.TrendStartIdx + 1; j < pivotTimes.Count; j++)
		    {
		        if (pivotIsHigh[j])
		        {
		            // HIGH pivot: abort if new high breaks TrendStart high before arming
		            double highPrice = pivotPrices[j];
		            if (highPrice > trendStartHigh + eps)
		            {
		                return false;
		            }
		            // otherwise continue; arming happens on LOW pivots (guide < resistanceGuide)
		        }
		        else
		        {
		            // LOW pivot: “cleared resistance” once body/guide gets below resistance guide
		            double lowGuide = pivotGuidePrices[j];
		            if (lowGuide < resistanceGuide - eps)
		            {
		                hasClearedResistance = true;
		                armedAtJ = j;
		                break;
		            }
		        }
		    }
		
		    if (!hasClearedResistance)
		    {
		        return false;
		    }
		
		    // ---- NEW FUNCTIONAL FIX: Reject potentials invalidated AFTER arming ----
		    // After arming, if any later HIGH pivot breaks TrendStartHigh, the potential is no longer valid.
		    for (int k = armedAtJ + 1; k < pivotTimes.Count; k++)
		    {
		        if (!pivotIsHigh[k])
		            continue;
		
		        double highPrice = pivotPrices[k];
		        if (highPrice > trendStartHigh + eps)
		        {
		            return false; // <-- the fix
		        }
		    }
		
		    return true;
		}




		
		private bool ScanPatternBFromTriple_Long(
		    PatternBTriplet trip,
		    Dictionary<int, double> newLongByBar,
		    List<PatternBLevel> newLevels)
		{
		    double eps           = TickSize * 0.5;
		    double ogLevel       = trip.OgPrice;
		    double supportGuide  = trip.SupportPrice;
		    double trendStartLow = trip.TrendStartPrice;
		
		    bool hasClearedSupport = false;
		    bool aborted           = false;
		    int  entryPivotIdx     = -1;
		
		    // Move forward from TrendStartIdx+1 through future pivots
		    for (int j = trip.TrendStartIdx + 1; j < pivotTimes.Count; j++)
		    {
		        if (!pivotIsHigh[j])
		        {
		            // LOW pivot
		            double lowPrice = pivotPrices[j];
		
		            // Critical: if a later low breaks TrendStart (new extreme) before entry -> abort
		            // This is the "replacement/removal" behavior.
		            if (lowPrice < trendStartLow - eps)
		            {
		                aborted = true;
		                break;
		            }
		
		            if (!hasClearedSupport)
		                continue;
		
		            double bodyLow = pivotGuidePrices[j]; // body low guide
		
		            bool wickTouchesOg = lowPrice <= ogLevel + eps;
		            bool bodyAboveOg   = bodyLow  >= ogLevel - eps;
		
		            if (wickTouchesOg && bodyAboveOg)
		            {
		                entryPivotIdx = j;
		                break;
		            }
		        }
		        else
		        {
		            // HIGH pivot: “cleared support” once body/guide gets above Support guide
		            double highGuide = pivotGuidePrices[j];
		            if (highGuide > supportGuide + eps)
		                hasClearedSupport = true;
		        }
		    }
		
		    if (aborted || entryPivotIdx < 0)
		        return false;
		
		    int ogBarIndex    = BarsArray[0].GetBar(pivotTimes[trip.OgIdx]);
		    int entryBarIndex = BarsArray[0].GetBar(pivotTimes[entryPivotIdx]);
			
			// Record last signal bar (needed for candidate expiry)
			string key = MakeTripletKey(trip);
			if (!string.IsNullOrEmpty(key))
			    lastSignalBip0BarByKey[key] = entryBarIndex;

			
		    if (ogBarIndex < 0 || entryBarIndex < 0)
		        return false;
		
		    double entryPrice = pivotPrices[entryPivotIdx];
		
		    // Signal placement (keep your offset style)
		    if (EnablePatternBSignal)
			{
		        newLongByBar[entryBarIndex] = entryPrice - (PatternBMarkerOffsetTicks * TickSize);
			}
		
		    // Level ends at entry (wick) pivot (so it does NOT extend to the right forever)
		    if (EnablePatternBLevels)
		    {
		        newLevels.Add(new PatternBLevel
		        {
		            IsLong       = true,
		            OgBarIndex   = ogBarIndex,
		            OgTime       = pivotTimes[trip.OgIdx],
		            EntryBarIndex= entryBarIndex,
		            EntryTime    = pivotTimes[entryPivotIdx],
		            Price        = ogLevel
		        });
		    }
		
		    return true;
		}
		
		private bool ScanPatternBFromTriple_Short(
		    PatternBTriplet trip,
		    Dictionary<int, double> newShortByBar,
		    List<PatternBLevel> newLevels)
		{
		    double eps            = TickSize * 0.5;
		    double ogLevel        = trip.OgPrice;
		    double resistanceGuide= trip.SupportPrice;
		    double trendStartHigh = trip.TrendStartPrice;
		
		    bool hasClearedResistance = false;
		    bool aborted              = false;
		    int  entryPivotIdx        = -1;
		
		    // Move forward from TrendStartIdx+1 through future pivots
		    for (int j = trip.TrendStartIdx + 1; j < pivotTimes.Count; j++)
		    {
		        if (pivotIsHigh[j])
		        {
		            // HIGH pivot
		            double highPrice = pivotPrices[j];
		
		            // Critical: if a later high breaks TrendStart (new extreme) before entry -> abort
		            // This is the "replacement/removal" behavior.
		            if (highPrice > trendStartHigh + eps)
		            {
		                aborted = true;
		                break;
		            }
		
		            if (!hasClearedResistance)
		                continue;
		
		            double bodyHigh = pivotGuidePrices[j]; // body high guide
		
		            bool wickTouchesOg = highPrice >= ogLevel - eps;
		            bool bodyBelowOg   = bodyHigh <= ogLevel + eps;
		
		            if (wickTouchesOg && bodyBelowOg)
		            {
		                entryPivotIdx = j;
		                break;
		            }
		        }
		        else
		        {
		            // LOW pivot: “cleared resistance” once body/guide gets below resistance guide
		            double lowGuide = pivotGuidePrices[j];
		            if (lowGuide < resistanceGuide - eps)
		                hasClearedResistance = true;
		        }
		    }
		
		    if (aborted || entryPivotIdx < 0)
		        return false;
		
		    int ogBarIndex    = BarsArray[0].GetBar(pivotTimes[trip.OgIdx]);
		    int entryBarIndex = BarsArray[0].GetBar(pivotTimes[entryPivotIdx]);
			
			// Record last signal bar (needed for candidate expiry)
			string key = MakeTripletKey(trip);
			if (!string.IsNullOrEmpty(key))
			    lastSignalBip0BarByKey[key] = entryBarIndex;

		    if (ogBarIndex < 0 || entryBarIndex < 0)
		        return false;
		
		    double entryPrice = pivotPrices[entryPivotIdx];
		
		    // Signal placement (keep your offset style)
		    if (EnablePatternBSignal)
			{
		        newShortByBar[entryBarIndex] = entryPrice + (PatternBMarkerOffsetTicks * TickSize);
			}
		    // Level ends at entry (wick) pivot
		    if (EnablePatternBLevels)
		    {
		        newLevels.Add(new PatternBLevel
		        {
		            IsLong       = false,
		            OgBarIndex   = ogBarIndex,
		            OgTime       = pivotTimes[trip.OgIdx],
		            EntryBarIndex= entryBarIndex,
		            EntryTime    = pivotTimes[entryPivotIdx],
		            Price        = ogLevel
		        });
		    }
		
		    return true;
		}
	


		private void DrawPatternBDebug(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternBLevel> newLevels)
		{
		    if (!CanDrawDebug() || !DrawPatternB)
		        return;
		
		    // ---- SIGNALS (triangles) ----
		    // Clear stale markers (only if we have previous state)
		    if (patternBLongByBar != null)
		        foreach (var kv in patternBLongByBar.ToList())
		            if (!newLongByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNB_L_{kv.Key}");
		
		    if (patternBShortByBar != null)
		        foreach (var kv in patternBShortByBar.ToList())
		            if (!newShortByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNB_S_{kv.Key}");
		
		    // Draw current (or none if disabled)
		    if (EnablePatternBSignal)
		    {
		        foreach (var kv in newLongByBar)
		            DrawPatternBText(true, kv.Key, kv.Value);
		
		        foreach (var kv in newShortByBar)
		            DrawPatternBText(false, kv.Key, kv.Value);
		    }
		    else
		    {
		        // If signals disabled, remove any existing markers
		        if (patternBLongByBar != null)
		            foreach (var kv in patternBLongByBar)
		                RemoveDrawObject($"PATTERNB_L_{kv.Key}");
		
		        if (patternBShortByBar != null)
		            foreach (var kv in patternBShortByBar)
		                RemoveDrawObject($"PATTERNB_S_{kv.Key}");
		    }
		
		    // ---- LEVELS (horizontal lines) ----
		    // For simplicity, clear prior drawn levels and redraw current.
		    // (Still cheap because this is debug-only.)
		    if (patternBLevels != null)
		    {
		        foreach (var lvl in patternBLevels)
		        {
		            string tagOld = lvl.IsLong
		                ? $"PATTERNB_LVL_L_{lvl.OgBarIndex}"
		                : $"PATTERNB_LVL_S_{lvl.OgBarIndex}";
		            RemoveDrawObject(tagOld);
		        }
		    }
		
		    if (EnablePatternBLevels)
		    {
		        foreach (var lvl in newLevels)
		            DrawPatternBLevelLine(lvl);
		    }
		}
		
		private void DrawPatternBText(bool isLong, int barIndex, double price)
		{
		    if (!CanDrawDebug() || !DrawPatternB)
		        return;
		
		    if (barIndex < 0 || barIndex > CurrentBar)
		        return;
		
		    int barsAgo = CurrentBar - barIndex;
		    if (barsAgo < 0)
		        return;
		
		    string tag  = isLong ? $"PATTERNB_L_{barIndex}" : $"PATTERNB_S_{barIndex}";
		    string text = isLong ? "▲" : "▼";
		
		    Brush brush = isLong ? PatternBLongColor : PatternBShortColor;
		
		    // If you want to ensure updates replace old text, clear first
		    RemoveDrawObject(tag);
		
		    Draw.Text(this, tag, false,
		        text,
		        barsAgo,
		        price,
		        0,
		        brush,
		        new SimpleFont("Arial", PatternBMarkerFontSize),
		        TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}
		
		private void DrawPatternBLevelLine(PatternBLevel lvl)
		{
		    if (!CanDrawDebug() || !DrawPatternB)
		        return;
		
		    if (lvl == null)
		        return;
		
		    string tag = lvl.IsLong
		        ? $"PATTERNB_LVL_L_{lvl.OgBarIndex}"
		        : $"PATTERNB_LVL_S_{lvl.OgBarIndex}";
		
		    Brush brush = lvl.IsLong ? PatternBLongColor : PatternBShortColor;
		
		    RemoveDrawObject(tag);
		
		    DateTime t1 = lvl.OgTime;
		    DateTime t2 = (lvl.EntryTime != default(DateTime)) ? lvl.EntryTime : Times[0][0];
		
		    Draw.Line(this, tag, false,
		        t1, lvl.Price,
		        t2, lvl.Price,
		        brush, PatternBLevelDashStyle, PatternBLevelWidth);
		}

		
		private double GetPatternBMarkerOffsetPrice(bool isLong)
		{
		    double ticks = PatternBMarkerOffsetTicks * TickSize;
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
//		    if (!ShowZigZagDebug && !DebugDrawPatternB /*&& !DebugDrawPatternA */)
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
		private AlightenLevelMultiTimeframeMATickPatternBV0002[] cacheAlightenLevelMultiTimeframeMATickPatternBV0002;
		public AlightenLevelMultiTimeframeMATickPatternBV0002 AlightenLevelMultiTimeframeMATickPatternBV0002(int barsToProcess, bool enablePatternBSignal, bool enablePatternBLevels, bool drawPatternB, int patternBMarkerFontSize, int patternBMarkerOffsetTicks, System.Windows.Media.Brush patternBLongColor, System.Windows.Media.Brush patternBShortColor, int patternBLevelWidth, DashStyleHelper patternBLevelDashStyle, bool gatePreOgCheck, bool gateTrendStartCheck, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return AlightenLevelMultiTimeframeMATickPatternBV0002(Input, barsToProcess, enablePatternBSignal, enablePatternBLevels, drawPatternB, patternBMarkerFontSize, patternBMarkerOffsetTicks, patternBLongColor, patternBShortColor, patternBLevelWidth, patternBLevelDashStyle, gatePreOgCheck, gateTrendStartCheck, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public AlightenLevelMultiTimeframeMATickPatternBV0002 AlightenLevelMultiTimeframeMATickPatternBV0002(ISeries<double> input, int barsToProcess, bool enablePatternBSignal, bool enablePatternBLevels, bool drawPatternB, int patternBMarkerFontSize, int patternBMarkerOffsetTicks, System.Windows.Media.Brush patternBLongColor, System.Windows.Media.Brush patternBShortColor, int patternBLevelWidth, DashStyleHelper patternBLevelDashStyle, bool gatePreOgCheck, bool gateTrendStartCheck, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			if (cacheAlightenLevelMultiTimeframeMATickPatternBV0002 != null)
				for (int idx = 0; idx < cacheAlightenLevelMultiTimeframeMATickPatternBV0002.Length; idx++)
					if (cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx] != null && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].BarsToProcess == barsToProcess && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].EnablePatternBSignal == enablePatternBSignal && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].EnablePatternBLevels == enablePatternBLevels && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].DrawPatternB == drawPatternB && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].PatternBMarkerFontSize == patternBMarkerFontSize && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].PatternBMarkerOffsetTicks == patternBMarkerOffsetTicks && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].PatternBLongColor == patternBLongColor && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].PatternBShortColor == patternBShortColor && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].PatternBLevelWidth == patternBLevelWidth && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].PatternBLevelDashStyle == patternBLevelDashStyle && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].GatePreOgCheck == gatePreOgCheck && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].GateTrendStartCheck == gateTrendStartCheck && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].ShowZigZag == showZigZag && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].ZigZagColor == zigZagColor && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].ZigZagWidth == zigZagWidth && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].ZigZagStyle == zigZagStyle && cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx].EqualsInput(input))
						return cacheAlightenLevelMultiTimeframeMATickPatternBV0002[idx];
			return CacheIndicator<AlightenLevelMultiTimeframeMATickPatternBV0002>(new AlightenLevelMultiTimeframeMATickPatternBV0002(){ BarsToProcess = barsToProcess, EnablePatternBSignal = enablePatternBSignal, EnablePatternBLevels = enablePatternBLevels, DrawPatternB = drawPatternB, PatternBMarkerFontSize = patternBMarkerFontSize, PatternBMarkerOffsetTicks = patternBMarkerOffsetTicks, PatternBLongColor = patternBLongColor, PatternBShortColor = patternBShortColor, PatternBLevelWidth = patternBLevelWidth, PatternBLevelDashStyle = patternBLevelDashStyle, GatePreOgCheck = gatePreOgCheck, GateTrendStartCheck = gateTrendStartCheck, ShowZigZag = showZigZag, ZigZagColor = zigZagColor, ZigZagWidth = zigZagWidth, ZigZagStyle = zigZagStyle }, input, ref cacheAlightenLevelMultiTimeframeMATickPatternBV0002);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenLevelMultiTimeframeMATickPatternBV0002 AlightenLevelMultiTimeframeMATickPatternBV0002(int barsToProcess, bool enablePatternBSignal, bool enablePatternBLevels, bool drawPatternB, int patternBMarkerFontSize, int patternBMarkerOffsetTicks, System.Windows.Media.Brush patternBLongColor, System.Windows.Media.Brush patternBShortColor, int patternBLevelWidth, DashStyleHelper patternBLevelDashStyle, bool gatePreOgCheck, bool gateTrendStartCheck, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternBV0002(Input, barsToProcess, enablePatternBSignal, enablePatternBLevels, drawPatternB, patternBMarkerFontSize, patternBMarkerOffsetTicks, patternBLongColor, patternBShortColor, patternBLevelWidth, patternBLevelDashStyle, gatePreOgCheck, gateTrendStartCheck, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenLevelMultiTimeframeMATickPatternBV0002 AlightenLevelMultiTimeframeMATickPatternBV0002(ISeries<double> input , int barsToProcess, bool enablePatternBSignal, bool enablePatternBLevels, bool drawPatternB, int patternBMarkerFontSize, int patternBMarkerOffsetTicks, System.Windows.Media.Brush patternBLongColor, System.Windows.Media.Brush patternBShortColor, int patternBLevelWidth, DashStyleHelper patternBLevelDashStyle, bool gatePreOgCheck, bool gateTrendStartCheck, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternBV0002(input, barsToProcess, enablePatternBSignal, enablePatternBLevels, drawPatternB, patternBMarkerFontSize, patternBMarkerOffsetTicks, patternBLongColor, patternBShortColor, patternBLevelWidth, patternBLevelDashStyle, gatePreOgCheck, gateTrendStartCheck, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenLevelMultiTimeframeMATickPatternBV0002 AlightenLevelMultiTimeframeMATickPatternBV0002(int barsToProcess, bool enablePatternBSignal, bool enablePatternBLevels, bool drawPatternB, int patternBMarkerFontSize, int patternBMarkerOffsetTicks, System.Windows.Media.Brush patternBLongColor, System.Windows.Media.Brush patternBShortColor, int patternBLevelWidth, DashStyleHelper patternBLevelDashStyle, bool gatePreOgCheck, bool gateTrendStartCheck, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternBV0002(Input, barsToProcess, enablePatternBSignal, enablePatternBLevels, drawPatternB, patternBMarkerFontSize, patternBMarkerOffsetTicks, patternBLongColor, patternBShortColor, patternBLevelWidth, patternBLevelDashStyle, gatePreOgCheck, gateTrendStartCheck, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenLevelMultiTimeframeMATickPatternBV0002 AlightenLevelMultiTimeframeMATickPatternBV0002(ISeries<double> input , int barsToProcess, bool enablePatternBSignal, bool enablePatternBLevels, bool drawPatternB, int patternBMarkerFontSize, int patternBMarkerOffsetTicks, System.Windows.Media.Brush patternBLongColor, System.Windows.Media.Brush patternBShortColor, int patternBLevelWidth, DashStyleHelper patternBLevelDashStyle, bool gatePreOgCheck, bool gateTrendStartCheck, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternBV0002(input, barsToProcess, enablePatternBSignal, enablePatternBLevels, drawPatternB, patternBMarkerFontSize, patternBMarkerOffsetTicks, patternBLongColor, patternBShortColor, patternBLevelWidth, patternBLevelDashStyle, gatePreOgCheck, gateTrendStartCheck, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

#endregion

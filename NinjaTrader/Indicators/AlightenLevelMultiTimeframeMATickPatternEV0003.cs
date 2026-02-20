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
    public class AlightenLevelMultiTimeframeMATickPatternEV0003 : Indicator
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
		
		// Pattern E results (MA-friendly persistent state)
		// barIndex is primary-series (BIP 0) absolute bar index (0..CurrentBar)
		private Dictionary<int, double> patternELongByBar;
		private Dictionary<int, double> patternEShortByBar;
		
		// Pattern E derived levels (logical objects; optionally drawn in debug)
		private List<PatternELevel> patternELevels;
		
		// Pattern E structure tracking (triplets/pivots). This mirrors the V00029 concept.
		// These are used inside ScanPatternE() to detect OG/support/regional relationships.
		private List<PatternETriplet> patternETriplets;
		private List<PatternETriplet> patternETripletsPotential; // potential (structural-only)
		
		// -----------------------------
		// BASE pattern outputs (tracked but NOT plotted/drawn)
		// -----------------------------
		private Dictionary<int, double> baseLongByBar;
		private Dictionary<int, double> baseShortByBar;
		private List<PatternELevel>     baseLevels;
		
		// -----------------------------
		// Pattern E (S/R flip) state machine (drives plots/draw)
		// -----------------------------
		private bool   peFlipLongActive;         // armed from a BASE short
		private double peFlipLongOg;
		private int    peFlipLongArmBar = -1;    // bar index where base short completed
		private bool   peFlipLongSeenBreak;      // saw close above OG
		private int    peFlipLongBreakBar = -1;  // bar index of the break-close
		private bool   peFlipLongInWickStreak;   // wick streak currently active
		
		private bool   peFlipShortActive;        // armed from a BASE long
		private double peFlipShortOg;
		private int    peFlipShortArmBar = -1;   // bar index where base long completed
		private bool   peFlipShortSeenBreak;     // saw close below OG
		private int    peFlipShortBreakBar = -1; // bar index of the break-close
		private bool   peFlipShortInWickStreak;  // wick streak currently active
		
		private int      peFlipLongOgBarIndex  = -1;
		private DateTime peFlipLongOgTime      = Core.Globals.MinDate;
		
		private int      peFlipShortOgBarIndex = -1;
		private DateTime peFlipShortOgTime     = Core.Globals.MinDate;
		
		// Live line anchors (start at last wick bar, extend to now)
		private int      peLiveLongLineStartBar  = -1;
		private DateTime peLiveLongLineStartTime = Core.Globals.MinDate;
		
		private int      peLiveShortLineStartBar  = -1;
		private DateTime peLiveShortLineStartTime = Core.Globals.MinDate;
		
		// Represents a single Pattern E triple (OG, Support, TrendStart)
		private class PatternETriplet
		{
		    public int    OgIdx;
		    public int    SupportIdx;
		    public int    TrendStartIdx;
		    public bool   IsLong;
		    public bool   IsActive;
		
		    public double OgPrice;         // OG guide price
		    public double SupportPrice;    // Support guide price
		    public double TrendStartPrice; // TrendStart extreme (actual pivot price)
		
		    public PatternETriplet() { }
		}
		
		// Represents a horizontal Pattern E level to draw (at OG price), ending at the wick/entry pivot
		private class PatternELevel
		{
		    public bool     IsLong;
		    public int      OgBarIndex;
		    public DateTime OgTime;
		
		    public int      EntryBarIndex;
		    public DateTime EntryTime;
		
		    public double   Price; // OG level (guide)
		
		    public PatternELevel() { }
		}
		
		// ---- Pattern E proximity candidates (evaluated/updated on BIP 1) ----
		private class PatternECandidate
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
			
			public string Key;          // triplet key or unique id
			public int    ArmedBip0Bar; // for recency/winner selection
			
			public bool   SeenBreak;
			public int    BreakBip0BarIdx;
			
			public int    LineStartBip0BarIdx; // replaces peLiveLongLineStartBar / short equivalent
			public bool   Consumed;            // optional
			
			public bool InWickStreak;

		
		    public PatternECandidate() { }
		}
		
		private List<PatternECandidate> patternECandidates;
		
		private PatternETriplet activePatternELong;
		private PatternETriplet activePatternEShort;
		
		private long _lastMapDebugTicks = 0;
		
		private Dictionary<string, int> lastSignalBip0BarByKey;

		private string MakeTripletKey(PatternETriplet t)
		{
		    if (t == null) return string.Empty;
		    return (t.IsLong ? "L" : "S") + "|" + t.OgIdx + "|" + t.SupportIdx + "|" + t.TrendStartIdx;
		}
		
		// --- Pattern E live recency (BIP0) state ---
		private bool peLivePrevLong  = false;
		private bool peLivePrevShort = false;
		
		private int  peLiveLongLastEdgeSeq  = -1;   // last tick-seq where long became true (false->true)
		private int  peLiveShortLastEdgeSeq = -1;   // last tick-seq where short became true (false->true)
		private int  peLiveTickSeq          = 0;    // increments each BIP0 call within a bar
		
		private int  peLiveLastChosenDir    = 0;    // +1 long, -1 short, 0 none (stability tie-break)

	
        #endregion

		#region Temp Debug
		
		
		#endregion
		
        #region Properties
		

        [NinjaScriptProperty]
		[Range(0, 500000)]
		[Display(Name="Bars To Process", GroupName="ZigZag", Order=53)]
		public int BarsToProcess { get; set; } = 200; // 0 = process all
		
		// ---- Pattern E toggles ----

		[NinjaScriptProperty]
		[Display(Name="Enable Pattern E Signals", GroupName="Pattern E", Order=1)]
		public bool EnablePatternESignal { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Enable Pattern E Levels", GroupName="Pattern E", Order=2)]
		public bool EnablePatternELevels { get; set; } = true;
	
		[NinjaScriptProperty]
		[Display(Name = "Draw Pattern E", GroupName = "Pattern E", Order = 3)]
		public bool DrawPatternE { get; set; } = true;
		// Optional styling knobs (safe defaults)
		[NinjaScriptProperty]
		[Range(6, 30)]
		[Display(Name="Pattern E Marker Font Size", GroupName="Pattern E", Order=20)]
		public int PatternEMarkerFontSize { get; set; } = 18;
		
		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name="Pattern E Marker Offset Ticks", GroupName="Pattern E", Order=21)]
		public int PatternEMarkerOffsetTicks { get; set; } = 15;
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern E Long Color", GroupName="Pattern E", Order=22)]
		public System.Windows.Media.Brush PatternELongColor { get; set; } = Brushes.Yellow;
		
		[Browsable(false)]
		public string PatternELongColorSerialize
		{
		    get => Serialize.BrushToString(PatternELongColor);
		    set => PatternELongColor = Serialize.StringToBrush(value);
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern E Short Color", GroupName="Pattern E", Order=23)]
		public System.Windows.Media.Brush PatternEShortColor { get; set; } = Brushes.Orchid;
		
		[Browsable(false)]
		public string PatternEShortColorSerialize
		{
		    get => Serialize.BrushToString(PatternEShortColor);
		    set => PatternEShortColor = Serialize.StringToBrush(value);
		}
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Pattern E Level Width", GroupName="Pattern E", Order=30)]
		public int PatternELevelWidth { get; set; } = 2;
		
		[NinjaScriptProperty]
		[Display(Name="Pattern E Level Dash", GroupName="Pattern E", Order=31)]
		public DashStyleHelper PatternELevelDashStyle { get; set; } = DashStyleHelper.Dash;
		
		[NinjaScriptProperty]
		[Display(Name="Gate: Require Pivot for Pattern E Signal", GroupName="Pattern E", Order=91)]
		public bool RequirePivotForPatternESignal { get; set; } = false;

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
		
		private Series<double> patternESignal;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternESignal
		{
		    get { return patternESignal; }
		}
		
		private Series<double> patternESignalMA;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternESignalMA
		{
		    get { return patternESignalMA; }
		}
		
		#endregion


        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AlightenLevelMultiTimeframeMATickPatternEV0003";
                Description = "Market Analyzer core: HTF ZigZag/levels on primary series with 1m/5m proximity columns.";
                Calculate   = Calculate.OnEachTick;

                // For MA, overlay doesn’t matter. For chart debugging, overlay helps.
                IsOverlay               		= true;
                DrawOnPricePanel        		= true;

                DisplayInDataBox        		= true;
                PaintPriceMarkers       		= false;
                IsSuspendedWhileInactive 		= false;
				ShowTransparentPlotsInDataBox 	= true;

				AddPlot(Brushes.Transparent, "PatternESignal");
				AddPlot(Brushes.Transparent, "PatternESignalMA");
                
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
				
				patternELongByBar  				= new Dictionary<int, double>(256);
				patternEShortByBar 				= new Dictionary<int, double>(256);
				patternELevels     				= new List<PatternELevel>(128);
				patternETriplets   				= new List<PatternETriplet>(128);
				patternETripletsPotential   	= new List<PatternETriplet>(128);
				patternECandidates 				= new List<PatternECandidate>(128);
				
				baseLongByBar  = new Dictionary<int, double>(256);
				baseShortByBar = new Dictionary<int, double>(256);
				baseLevels     = new List<PatternELevel>(128);
				
				// reset flip state
				peFlipLongActive = false;
				peFlipShortActive = false;
				peFlipLongArmBar = peFlipShortArmBar = -1;
				peFlipLongBreakBar = peFlipShortBreakBar = -1;
				peFlipLongSeenBreak = peFlipShortSeenBreak = false;
				peFlipLongInWickStreak = peFlipShortInWickStreak = false;

				
				lastSignalBip0BarByKey = new Dictionary<string, int>(512, StringComparer.Ordinal);

				patternESignal					= Values[0];
				patternESignalMA				= Values[1];

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
		        if (EnablePatternESignal)
		        {
		            //Print($"[LIVE] t={Times[0][0]:HH:mm:ss.fff} Close0={Close[0]}");
		            UpdatePatternE_Live_BIP0();
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
		        RebuildPatternE_BIP0();
		
		        if (EnablePatternESignal)
		        {
		            // Default clear
		            patternESignal[0]   = 0;
		            patternESignalMA[0] = 0;
		
		            if (CurrentBars[0] < 1)
		                return;
		
		            int sigBar = CurrentBars[0] - 1;
		
		            bool longNow  = patternELongByBar  != null && patternELongByBar.ContainsKey(sigBar);
		            bool shortNow = patternEShortByBar != null && patternEShortByBar.ContainsKey(sigBar);
					
					//Print($"[PC-OC:SIGBAR] nowT={Times[0][0]:MM-dd HH:mm:ss} sigBar={sigBar} longNow={longNow} shortNow={shortNow}");
		
		            double sig = 0;
		            if (longNow && !shortNow) sig = +1;
		            else if (shortNow && !longNow) sig = -1;
		
		            // DataBox / chart-aligned plot (belongs to bar that just closed)
		            patternESignal[1] = sig;
		
		            // MA-friendly plot (belongs to current bar so MA reads [0] reliably)
		            patternESignalMA[0] = sig;
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
		
		#region Pattern E
		
		// Live tags (kept distinct from your historical per-bar tags)
		private const string PE_LIVE_TAG_L = "PATTERNE_LIVE_L";
		private const string PE_LIVE_TAG_S = "PATTERNE_LIVE_S";
		private const string PE_LIVE_LVL_L = "PATTERNE_LIVE_LVL_L";
		private const string PE_LIVE_LVL_S = "PATTERNE_LIVE_LVL_S";


		private void UpdatePatternE_Live_BIP0()
		{
		    // Per-bar reset for recency tracking (BIP0)
		    if (IsFirstTickOfBar)
		    {
		        peLivePrevLong        = false;
		        peLivePrevShort       = false;
		        peLiveLongLastEdgeSeq = -1;
		        peLiveShortLastEdgeSeq = -1;
		        peLiveTickSeq         = 0;
		        peLiveLastChosenDir   = 0;
		    }
		    else
		    {
		        peLiveTickSeq++;
		    }
		
		    if (CurrentBar < 1)
		    {
		        patternESignal[0] = 0;
		
		        if (CanDrawDebug() && DrawPatternE)
		        {
		            RemoveDrawObject(PE_LIVE_TAG_L);
		            RemoveDrawObject(PE_LIVE_TAG_S);
		            RemoveDrawObject(PE_LIVE_LVL_L);
		            RemoveDrawObject(PE_LIVE_LVL_S);
		        }
		        return;
		    }			
		
		    double eps = TickSize * 0.5;
		
		    bool liveLong  = false;
		    bool liveShort = false;
		
		    double bestLongOg  = double.NaN;
		    double bestShortOg = double.NaN;
		
		    // ---- live LONG flip (multi-candidate) ----
			PatternECandidate bestLong = null;
			
			if (patternECandidates != null && patternECandidates.Count > 0)
			{
			    for (int i = 0; i < patternECandidates.Count; i++)
			    {
			        var c = patternECandidates[i];
			        if (c == null || c.Consumed || !c.IsLong)
			            continue;
			
			        if (!c.SeenBreak || CurrentBars[0] <= c.BreakBip0BarIdx)
			            continue;
			
			        if (!WickFromAbove_CurrentBar(c.OgLevel, eps))
			            continue;
			
			        // winner: most recent armed candidate
			        if (bestLong == null || c.ArmedBip0Bar > bestLong.ArmedBip0Bar)
			            bestLong = c;
			    }
			}
			
			if (bestLong != null)
			{
			    liveLong   = true;
			    bestLongOg = bestLong.OgLevel;
			
			    // feed draw anchor from candidate (fallback will handle -1)
			    peLiveLongLineStartBar = bestLong.LineStartBip0BarIdx;
			}

		
		    // ---- live SHORT flip (multi-candidate) ----
			PatternECandidate bestShort = null;
			
			if (patternECandidates != null && patternECandidates.Count > 0)
			{
			    for (int i = 0; i < patternECandidates.Count; i++)
			    {
			        var c = patternECandidates[i];
			        if (c == null || c.Consumed || c.IsLong)
			            continue;
			
			        if (!c.SeenBreak || CurrentBars[0] <= c.BreakBip0BarIdx)
			            continue;
			
			        if (!WickFromBelow_CurrentBar(c.OgLevel, eps))
			            continue;
			
			        if (bestShort == null || c.ArmedBip0Bar > bestShort.ArmedBip0Bar)
			            bestShort = c;
			    }
			}
			
			if (bestShort != null)
			{
			    liveShort   = true;
			    bestShortOg = bestShort.OgLevel;
			    peLiveShortLineStartBar = bestShort.LineStartBip0BarIdx;
			}

		
		    // --- Recency edges ---
		    if (liveLong && !peLivePrevLong)
			{
		        peLiveLongLastEdgeSeq = peLiveTickSeq;
			}
		
		    if (liveShort && !peLivePrevShort)
			{
		        peLiveShortLastEdgeSeq = peLiveTickSeq;
			}
			
		    peLivePrevLong  = liveLong;
		    peLivePrevShort = liveShort;
		
		
		    // Single scalar liveSig for plot (but draw both if both)
		    double liveSig = 0;
		
		    if (liveLong && !liveShort) liveSig = +1;
		    else if (liveShort && !liveLong) liveSig = -1;
		    else if (liveLong && liveShort)
		    {
		        // recency wins
		        if (peLiveLongLastEdgeSeq > peLiveShortLastEdgeSeq) liveSig = +1;
		        else if (peLiveShortLastEdgeSeq > peLiveLongLastEdgeSeq) liveSig = -1;
		        else liveSig = peLiveLastChosenDir;
		    }
		
		    patternESignal[0] = liveSig;
		
		    if (CanDrawDebug() && DrawPatternE)
		    {
		        RemoveDrawObject(PE_LIVE_TAG_L);
		        RemoveDrawObject(PE_LIVE_TAG_S);
		        RemoveDrawObject(PE_LIVE_LVL_L);
		        RemoveDrawObject(PE_LIVE_LVL_S);
		
		        //DateTime t2 = Times[0][0];
		
		        // LONG
				if (liveLong && !double.IsNaN(bestLongOg))
				{
				    // peLiveLongLineStartBar is now set at CONFIRMED wick time (in DerivePatternEFlipFromBase_Append)
				    int startBar = peLiveLongLineStartBar;
				
				    // Safety fallback: if we don't have a confirmed wick yet, just draw 1-bar stub
				    if (startBar < 0)
				        startBar = CurrentBars[0] - 1;
				
				    // Clamp (avoid negative / future)
				    if (startBar > CurrentBars[0])
				        startBar = CurrentBars[0] - 1;
				
				    int barsAgoStart = CurrentBars[0] - startBar;
				    if (barsAgoStart < 1) barsAgoStart = 1;
				
				    Draw.Line(this, PE_LIVE_LVL_L, false,
				        barsAgoStart, bestLongOg,
				        0,          bestLongOg,
				        PatternELongColor, PatternELevelDashStyle, PatternELevelWidth);
				
				    double yL = Low[0] - (PatternEMarkerOffsetTicks * TickSize);
				    Draw.Text(this, PE_LIVE_TAG_L, false,
				        "▲",
				        0,
				        yL,
				        0,
				        PatternELongColor,
				        new SimpleFont("Arial", PatternEMarkerFontSize),
				        TextAlignment.Center,
				        Brushes.Transparent,
				        Brushes.Transparent,
				        0);
				}

		
		        // SHORT
				if (liveShort && !double.IsNaN(bestShortOg))
				{
				    int startBar = peLiveShortLineStartBar;
				
				    if (startBar < 0)
				        startBar = CurrentBars[0] - 1;
				
				    if (startBar > CurrentBars[0])
				        startBar = CurrentBars[0] - 1;
				
				    int barsAgoStart = CurrentBars[0] - startBar;
				    if (barsAgoStart < 1) barsAgoStart = 1;
				
				    Draw.Line(this, PE_LIVE_LVL_S, false,
				        barsAgoStart, bestShortOg,
				        0,          bestShortOg,
				        PatternEShortColor, PatternELevelDashStyle, PatternELevelWidth);
				
				    double yS = High[0] + (PatternEMarkerOffsetTicks * TickSize);
				    Draw.Text(this, PE_LIVE_TAG_S, false,
				        "▼",
				        0,
				        yS,
				        0,
				        PatternEShortColor,
				        new SimpleFont("Arial", PatternEMarkerFontSize),
				        TextAlignment.Center,
				        Brushes.Transparent,
				        Brushes.Transparent,
				        0);
				}

		    }
		}


		private void RebuildPatternE_BIP0()
		{
		    if (!EnablePatternESignal && !EnablePatternELevels)
		        return;
		
		    // 1) Build BASE pattern (existing engine) into locals
		    BuildPatternE(out var baseLong, out var baseShort, out var baseLvls);
		
		    // Persist BASE pattern for tracking (NOT for plotting/drawing)
		    if (baseLongByBar == null)  baseLongByBar  = new Dictionary<int, double>(256);
		    if (baseShortByBar == null) baseShortByBar = new Dictionary<int, double>(256);
		    if (baseLevels == null)     baseLevels     = new List<PatternELevel>(128);
		
		    baseLongByBar.Clear();
		    foreach (var kv in baseLong) baseLongByBar[kv.Key] = kv.Value;
		
		    baseShortByBar.Clear();
		    foreach (var kv in baseShort) baseShortByBar[kv.Key] = kv.Value;
		
		    baseLevels.Clear();
		    baseLevels.AddRange(baseLvls);
			
			// Build completion maps from BASE levels.
			// Key = entry bar (completion bar), Value = the BASE level object (contains OG guide + OG origin bar/time)
			Dictionary<int, PatternELevel> baseLongCompleteByBar  = new Dictionary<int, PatternELevel>(32);
			Dictionary<int, PatternELevel> baseShortCompleteByBar = new Dictionary<int, PatternELevel>(32);
			
			if (baseLevels != null)
			{
			    foreach (var lvl in baseLevels)
			    {
			        if (lvl == null) continue;
			        if (lvl.EntryBarIndex < 0) continue;
			
			        if (lvl.IsLong) baseLongCompleteByBar[lvl.EntryBarIndex] = lvl;
			        else            baseShortCompleteByBar[lvl.EntryBarIndex] = lvl;
			    }
			}

			
			DerivePatternEFlipFromBase_Append(baseLongCompleteByBar, baseShortCompleteByBar);
		

			if (CanDrawDebug() && DrawPatternE)
    			DrawPatternEDebug(patternELongByBar, patternEShortByBar, patternELevels);
		
		    // NOTE: Do NOT rebuild candidates and do NOT draw base debug.
		    // The "flip" lives off the state machine + patternELongByBar/patternEShortByBar.
		}
		
		private PatternECandidate UpsertPatternEFlipCandidate(bool isLong, PatternELevel lvl, int sigBar)
		{
		    if (patternECandidates == null)
		        patternECandidates = new List<PatternECandidate>(128);
		
		    // Stable key: OG time + price + direction.
		    // (This prevents the 4432.30 candidate from being overwritten by later OGs like 4408.00.)
		    string key =
			    (lvl.OgTime != Core.Globals.MinDate
			        ? ("OG|" + lvl.OgTime.Ticks.ToString())
			        : ("OBI|" + lvl.OgBarIndex.ToString()))
			    + "|" + lvl.Price.ToString("0.00")
			    + "|" + (isLong ? "L" : "S");

		
		    PatternECandidate c = null;
		    for (int i = 0; i < patternECandidates.Count; i++)
		    {
		        if (patternECandidates[i].Key == key)
		        {
		            c = patternECandidates[i];
		            break;
		        }
		    }
		
		    if (c == null)
		    {
		        c = new PatternECandidate
		        {
		            IsLong = isLong,
		            Key = key,
		            OgLevel = lvl.Price,
		            ArmedBip0Bar = sigBar,
		            SeenBreak = false,
		            BreakBip0BarIdx = -1,
		            LineStartBip0BarIdx = -1,
		            InWickStreak = false,
		            Consumed = false,
		            TouchTime = Core.Globals.MinDate,
		            TouchBip0BarIdx = -1
		        };
		        patternECandidates.Add(c);
		    }
		    else
		    {
		        // Keep the same candidate, but refresh its “armed” info if this base repeats.
		        c.OgLevel = lvl.Price;
		        c.ArmedBip0Bar = sigBar;
		        c.Consumed = false;
		        // Do NOT reset SeenBreak/Break unless you explicitly want “re-arm resets”.
		        // (Leaving as-is preserves progress.)
		    }
		
		    return c;
		}

		
		private void DerivePatternEFlipFromBase_Append(
		    Dictionary<int, PatternELevel> baseLongComplete,
		    Dictionary<int, PatternELevel> baseShortComplete)
		{
		    if (patternELongByBar == null)  patternELongByBar  = new Dictionary<int, double>(256);
		    if (patternEShortByBar == null) patternEShortByBar = new Dictionary<int, double>(256);
		    if (patternELevels == null)     patternELevels     = new List<PatternELevel>(256);
		
		    if (CurrentBar < 3)
		        return;
		
		    double eps = TickSize * 0.5;
		    int sigBar = CurrentBars[0] - 1;
		    if (sigBar < 1)
		        return;
		
		    // Arm LONG flip candidates from BASE SHORT completion (multi-candidate)
			if (baseShortComplete != null && baseShortComplete.TryGetValue(sigBar, out PatternELevel baseShortLvl))
			{
			    var c = UpsertPatternEFlipCandidate(true, baseShortLvl, sigBar);
				if (c.LineStartBip0BarIdx < 0)
    				c.LineStartBip0BarIdx = baseShortLvl.EntryBarIndex;			    
			}


		
		    // Arm SHORT flip candidates from BASE LONG completion (multi-candidate)
			if (baseLongComplete != null && baseLongComplete.TryGetValue(sigBar, out PatternELevel baseLongLvl))
			{
			    var c = UpsertPatternEFlipCandidate(false, baseLongLvl, sigBar);
				if (c.LineStartBip0BarIdx < 0)
    				c.LineStartBip0BarIdx = baseLongLvl.EntryBarIndex;
			}

			// 2) Update flip states per-candidate and APPEND signals when they occur
			if (patternECandidates != null && patternECandidates.Count > 0)
			{
			    double closeSig = Close[1];
			
			    for (int i = 0; i < patternECandidates.Count; i++)
			    {
			        var c = patternECandidates[i];
			        if (c == null || c.Consumed)
			            continue;
			
			        // LONG candidates (armed from BASE SHORT)
			        if (c.IsLong)
			        {
			            // Require progress beyond armed bar (matches your original gating)
			            if (sigBar <= c.ArmedBip0Bar)
			                continue;
			
			            if (!c.SeenBreak)
			            {
			                if (closeSig > c.OgLevel + eps)
			                {
			                    c.SeenBreak = true;
			                    c.BreakBip0BarIdx = sigBar;
			                    c.InWickStreak = false;
			                }
			            }
			            else if (sigBar > c.BreakBip0BarIdx)
			            {
			                bool isWick = WickFromAbove_ClosedBar(sigBar, c.OgLevel, eps);
			
			                if (!c.InWickStreak)
			                {
			                    if (isWick)
			                    {
			                        c.InWickStreak = true;
			
			                        patternELongByBar[sigBar] = c.OgLevel;
			                        patternELevels.Add(new PatternELevel
			                        {
			                            IsLong        = true,
			                            OgBarIndex    = c.ArmedBip0Bar,
			                            OgTime        = Times[0][CurrentBar - c.ArmedBip0Bar],
			                            EntryBarIndex = sigBar,
			                            EntryTime     = Times[0][1],
			                            Price         = c.OgLevel
			                        });
			
//			                        // latch line start for THIS candidate (first wick bar)
//			                        if (c.LineStartBip0BarIdx < 0)
//			                            c.LineStartBip0BarIdx = sigBar;
			                    }
			                }
			                else
			                {
			                    if (isWick)
			                    {
			                        patternELongByBar[sigBar] = c.OgLevel;
			                        patternELevels.Add(new PatternELevel
			                        {
			                            IsLong        = true,
			                            OgBarIndex    = c.ArmedBip0Bar,
			                            OgTime        = Times[0][CurrentBar - c.ArmedBip0Bar],
			                            EntryBarIndex = sigBar,
			                            EntryTime     = Times[0][1],
			                            Price         = c.OgLevel
			                        });
			                    }
			                    else
			                    {
			                        // end this candidate’s lifecycle only (do NOT kill others)
			                        c.Consumed = true;
			                        c.InWickStreak = false;
			                    }
			                }
			            }
			        }
			        // SHORT candidates (armed from BASE LONG)
			        else
			        {
			            if (sigBar <= c.ArmedBip0Bar)
			                continue;
			
			            if (!c.SeenBreak)
			            {
			                if (closeSig < c.OgLevel - eps)
			                {
			                    c.SeenBreak = true;
			                    c.BreakBip0BarIdx = sigBar;
			                    c.InWickStreak = false;
			                }
			            }
			            else if (sigBar > c.BreakBip0BarIdx)
			            {
			                bool isWick = WickFromBelow_ClosedBar(sigBar, c.OgLevel, eps);
			
			                if (!c.InWickStreak)
			                {
			                    if (isWick)
			                    {
			                        c.InWickStreak = true;
			
			                        patternEShortByBar[sigBar] = c.OgLevel;
			                        patternELevels.Add(new PatternELevel
			                        {
			                            IsLong        = false,
			                            OgBarIndex    = c.ArmedBip0Bar,
			                            OgTime        = Times[0][CurrentBar - c.ArmedBip0Bar],
			                            EntryBarIndex = sigBar,
			                            EntryTime     = Times[0][1],
			                            Price         = c.OgLevel
			                        });
			
//			                        if (c.LineStartBip0BarIdx < 0)
//			                            c.LineStartBip0BarIdx = sigBar;
			                    }
			                }
			                else
			                {
			                    if (isWick)
			                    {
			                        patternEShortByBar[sigBar] = c.OgLevel;
			                        patternELevels.Add(new PatternELevel
			                        {
			                            IsLong        = false,
			                            OgBarIndex    = c.ArmedBip0Bar,
			                            OgTime        = Times[0][CurrentBar - c.ArmedBip0Bar],
			                            EntryBarIndex = sigBar,
			                            EntryTime     = Times[0][1],
			                            Price         = c.OgLevel
			                        });
			                    }
			                    else
			                    {
			                        c.Consumed = true;
			                        c.InWickStreak = false;
			                    }
			                }
			            }
			        }
			    }
			
			    // Optional prune: remove consumed candidates to keep list bounded (minimal)
			    for (int i = patternECandidates.Count - 1; i >= 0; i--)
			        if (patternECandidates[i] != null && patternECandidates[i].Consumed)
			            patternECandidates.RemoveAt(i);
			}

		}


		
		private void RebuildPatternECandidates_BIP0()
		{
		    if (patternECandidates == null)
		        patternECandidates = new List<PatternECandidate>(128);
		
		    patternECandidates.Clear();
		
		    // Ensure map exists (safe for chart + MA)
		    if (lastSignalBip0BarByKey == null)
		        lastSignalBip0BarByKey = new Dictionary<string, int>(256, StringComparer.Ordinal);
		
		    int      bip0 = CurrentBars[0];
		    DateTime t0   = Times[0][0];
		
		    int completedCount = (patternETriplets == null) ? 0 : patternETriplets.Count;
		    int potentialCount = (patternETripletsPotential == null) ? 0 : patternETripletsPotential.Count;
		
		
		    if (completedCount == 0 && potentialCount == 0)
		        return;
		
		    int added             = 0;
		    int skippedInactive   = 0;
		    int skippedDuplicate  = 0;
		    int skippedExpired    = 0;
		    int skippedOgPolarity = 0;
		
		    // Completed wins; stable de-dupe across completed + potential
		    HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		
		    void TryAddTriplet(PatternETriplet t)
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
		
		        var c = new PatternECandidate
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
		
		        patternECandidates.Add(c);
		        added++;
		
		    }
		
		    // 1) Completed first (wins)
		    if (completedCount > 0)
		        foreach (var t in patternETriplets)
		            TryAddTriplet(t);
		
		    // 2) Potential second (only if not already present)
		    if (potentialCount > 0)
		        foreach (var t in patternETripletsPotential)
		            TryAddTriplet(t);
		
		}
		
		private bool PassesGate_TrendStartBodyBeyondOg(PatternETriplet t, double eps, out string failReason)
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

		private bool PassesGate_PreOgSamePolarityBeyondOg(PatternETriplet t, double eps, out string failReason)
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


		private void BuildPatternE(
		    out Dictionary<int, double> newLongByBar,
		    out Dictionary<int, double> newShortByBar,
		    out List<PatternELevel> newLevels)
		{
		    newLongByBar  = new Dictionary<int, double>();
		    newShortByBar = new Dictionary<int, double>();
		    newLevels     = new List<PatternELevel>();
		
		    if ((!EnablePatternESignal && !EnablePatternELevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 3)
		        return;
		
		    ScanPatternE(newLongByBar, newShortByBar, newLevels);
		}

		
		private void ScanPatternE(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternELevel> newLevels)
		{
		    patternETriplets.Clear();
		    patternETripletsPotential.Clear();
		
		    if ((!EnablePatternESignal && !EnablePatternELevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 2)
		        return;
		
		    int pivotCount = pivotTimes.Count;
		
		    // Map pivot index -> primary bar index (BIP0)
		    int GetPrimaryBarIndexFromPivot(int pivotIdx)
		    {
		        if (pivotIdx < 0 || pivotIdx >= pivotTimes.Count)
		            return -1;
		
		        return BarsArray[0].GetBar(pivotTimes[pivotIdx]);
		    }
		
		    // We now form Pattern E "triplets" from *two pivots*:
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
		
		            var trip = new PatternETriplet
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
		
		            if (ScanPatternEFromTriple_Long_PotentialGate(trip))
		                patternETripletsPotential.Add(trip);
		
		            if (ScanPatternEFromTriple_Long(trip, newLongByBar, newLevels))
		                patternETriplets.Add(trip);
		        }
		
		        if (shortPair)
		        {
		            double ogGuide = pivotGuidePrices[idx1];
		
		            var trip = new PatternETriplet
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
		
		            if (ScanPatternEFromTriple_Short_PotentialGate(trip))
		                patternETripletsPotential.Add(trip);
		
		            if (ScanPatternEFromTriple_Short(trip, newShortByBar, newLevels))
		                patternETriplets.Add(trip);
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


		
		// Gate for POTENTIAL Pattern E LONG:
		private bool ScanPatternEFromTriple_Long_PotentialGate(PatternETriplet trip)
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
		
		    // Pattern E has NO "clearance" / "trendstart extreme" gating.
		    // If OG+Support pivots exist, allow it as a potential candidate.
		    return true;
		}

		

		// Gate for POTENTIAL Pattern E SHORT:
		private bool ScanPatternEFromTriple_Short_PotentialGate(PatternETriplet trip)
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
	
		private bool ScanPatternEFromTriple_Long(
		    PatternETriplet trip,
		    Dictionary<int, double> newLongByBar,
		    List<PatternELevel> newLevels)
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
		    if (RequirePivotForPatternESignal)
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
		
		    for (int barIndex = startBar; barIndex <= CurrentBar; barIndex++)
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
		        if (!RequirePivotForPatternESignal && barIndex <= eligibleBarIx)
		            continue;
		
		        
				
				double bodyLow = Math.Min(o, c);
				
				bool wickTouchesOg = lo <= ogLevel + eps;
				
				// IMPORTANT: reject body-cross bars
				bool bodyFullyAboveOg = bodyLow >= ogLevel - eps;
				
				bool pivotOk = true;
		
				if (RequirePivotForPatternESignal)
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
		
		                if (EnablePatternESignal)
		                    newLongByBar[barIndex] = lo - (PatternEMarkerOffsetTicks * TickSize);
		            }
		        }
		        else
		        {
		            if (isWickBar)
		            {
		                lastWickBarIx = barIndex;
		
		                if (EnablePatternESignal)
		                    newLongByBar[barIndex] = lo - (PatternEMarkerOffsetTicks * TickSize);
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
		
		    if (EnablePatternELevels)
		    {
		        int entryBarsAgo = CurrentBar - lastWickBarIx;
		        if (entryBarsAgo >= 0 && entryBarsAgo <= CurrentBar)
		        {
		            newLevels.Add(new PatternELevel
		            {
		                IsLong        = true,
		                OgBarIndex    = ogBarIndex,
		                OgTime        = pivotTimes[trip.OgIdx],
		                EntryBarIndex = lastWickBarIx,
		                EntryTime     = Times[0][entryBarsAgo],
		                Price         = ogLevel
		            });
		        }
		    }
		
		    return true;
		}
		
		private bool ScanPatternEFromTriple_Short(
		    PatternETriplet trip,
		    Dictionary<int, double> newShortByBar,
		    List<PatternELevel> newLevels)
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
		    if (RequirePivotForPatternESignal)
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
		
		    for (int barIndex = startBar; barIndex <= CurrentBar; barIndex++)
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
		        if (!RequirePivotForPatternESignal && barIndex <= eligibleBarIx)
		            continue;
		
		      		
				double bodyHigh = Math.Max(o, c);
				
				bool wickTouchesOg = hi >= ogLevel - eps;
				
				// IMPORTANT: reject body-cross bars
				bool bodyFullyBelowOg = bodyHigh <= ogLevel + eps;
				
				bool pivotOk = true;
		
				if (RequirePivotForPatternESignal)
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
		
		                if (EnablePatternESignal)
		                    newShortByBar[barIndex] = hi + (PatternEMarkerOffsetTicks * TickSize);
		            }
		        }
		        else
		        {
		            if (isWickBar)
		            {
		                lastWickBarIx = barIndex;
		
		                if (EnablePatternESignal)
		                    newShortByBar[barIndex] = hi + (PatternEMarkerOffsetTicks * TickSize);
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
		
		    if (EnablePatternELevels)
		    {
		        int entryBarsAgo = CurrentBar - lastWickBarIx;
		        if (entryBarsAgo >= 0 && entryBarsAgo <= CurrentBar)
		        {
		            newLevels.Add(new PatternELevel
		            {
		                IsLong        = false,
		                OgBarIndex    = ogBarIndex,
		                OgTime        = pivotTimes[trip.OgIdx],
		                EntryBarIndex = lastWickBarIx,
		                EntryTime     = Times[0][entryBarsAgo],
		                Price         = ogLevel
		            });
		        }
		    }
		
		    return true;
		}


		private void DrawPatternEDebug(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternELevel> newLevels)
		{
		    if (!CanDrawDebug() || !DrawPatternE)
		        return;
		
		    // ---- SIGNALS (triangles) ----
		    // Clear stale markers (only if we have previous state)
		    if (patternELongByBar != null)
		        foreach (var kv in patternELongByBar.ToList())
		            if (!newLongByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNC_L_{kv.Key}");
		
		    if (patternEShortByBar != null)
		        foreach (var kv in patternEShortByBar.ToList())
		            if (!newShortByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNC_S_{kv.Key}");
		
		    // Draw current (or none if disabled)
		    if (EnablePatternESignal)
		    {
		        foreach (var kv in newLongByBar)
		            DrawPatternEText(true, kv.Key, kv.Value);
		
		        foreach (var kv in newShortByBar)
		            DrawPatternEText(false, kv.Key, kv.Value);
		    }
		    else
		    {
		        // If signals disabled, remove any existing markers
		        if (patternELongByBar != null)
		            foreach (var kv in patternELongByBar)
		                RemoveDrawObject($"PATTERNC_L_{kv.Key}");
		
		        if (patternEShortByBar != null)
		            foreach (var kv in patternEShortByBar)
		                RemoveDrawObject($"PATTERNC_S_{kv.Key}");
		    }
		
		    // ---- LEVELS (horizontal lines) ----
		    // For simplicity, clear prior drawn levels and redraw current.
		    // (Still cheap because this is debug-only.)
		    if (patternELevels != null)
		    {
		        foreach (var lvl in patternELevels)
		        {
		            string tagOld = lvl.IsLong
		                ? $"PATTERNC_LVL_L_{lvl.OgBarIndex}"
		                : $"PATTERNC_LVL_S_{lvl.OgBarIndex}";
		            RemoveDrawObject(tagOld);
		        }
		    }
		
		    if (EnablePatternELevels)
		    {
		        foreach (var lvl in newLevels)
		            DrawPatternELevelLine(lvl);
		    }
		}
		
//		private void DrawPatternEText(bool isLong, int barIndex, double price)
//		{
//		    if (!CanDrawDebug() || !DrawPatternE)
//		        return;
		
//		    if (barIndex < 0 || barIndex > CurrentBar)
//		        return;
		
//		    int barsAgo = CurrentBar - barIndex;
//		    if (barsAgo < 0)
//		        return;
		
//		    string tag  = isLong ? $"PATTERNC_L_{barIndex}" : $"PATTERNC_S_{barIndex}";
//		    string text = isLong ? "▲" : "▼";
		
//		    Brush brush = isLong ? PatternELongColor : PatternEShortColor;
		
//		    // If you want to ensure updates replace old text, clear first
//		    RemoveDrawObject(tag);
		
//		    Draw.Text(this, tag, false,
//		        text,
//		        barsAgo,
//		        price,
//		        0,
//		        brush,
//		        new SimpleFont("Arial", PatternEMarkerFontSize),
//		        TextAlignment.Center,
//		        Brushes.Transparent,
//		        Brushes.Transparent,
//		        0);
//		}
		
		void DrawPatternEText(bool isLong, int barIndex, double price)
		{
		    if (!CanDrawDebug() || !DrawPatternE)
		        return;
		
		    if (barIndex < 0 || barIndex > CurrentBar)
		        return;
		
		    int barsAgo = CurrentBar - barIndex;
		    if (barsAgo < 0)
		        return;
		
		    // ✅ Always anchor marker relative to the *actual* bar High/Low
		    double y = isLong
		        ? (Low[barsAgo]  - (PatternEMarkerOffsetTicks * TickSize))
		        : (High[barsAgo] + (PatternEMarkerOffsetTicks * TickSize));
		
		    string tag  = isLong ? $"PATTERNC_L_{barIndex}" : $"PATTERNC_S_{barIndex}";
		    string txt  = isLong ? "▲" : "▼";
		
		    Brush brush = isLong ? PatternELongColor : PatternEShortColor;
		
		    RemoveDrawObject(tag);
		
		    Draw.Text(this, tag, false,
		        txt,
		        barsAgo,
		        y,
		        0,
		        brush,
		        new SimpleFont("Arial", PatternEMarkerFontSize),
		        TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}

		
		private void DrawPatternELevelLine(PatternELevel lvl)
		{
		    if (!CanDrawDebug() || !DrawPatternE)
		        return;
		
		    if (lvl == null)
		        return;
		
		    string tag = lvl.IsLong
		        ? $"PATTERNC_LVL_L_{lvl.OgBarIndex}"
		        : $"PATTERNC_LVL_S_{lvl.OgBarIndex}";
		
		    Brush brush = lvl.IsLong ? PatternELongColor : PatternEShortColor;
		
		    RemoveDrawObject(tag);
		
		    DateTime t1 = lvl.OgTime;
		    DateTime t2 = (lvl.EntryTime != default(DateTime)) ? lvl.EntryTime : Times[0][0];
		
		    Draw.Line(this, tag, false,
		        t1, lvl.Price,
		        t2, lvl.Price,
		        brush, PatternELevelDashStyle, PatternELevelWidth);
		}

		
		private double GetPatternEMarkerOffsetPrice(bool isLong)
		{
		    double ticks = PatternEMarkerOffsetTicks * TickSize;
		    return isLong ? ticks : -ticks;
		}
		
		private bool WickFromAbove_ClosedBar(int closedBarIndex, double og, double eps)
		{
		    // closedBarIndex is absolute bar index (0..CurrentBar-1)
		    int barsAgo = CurrentBar - closedBarIndex;
		    double o = Open[barsAgo];
		    double c = Close[barsAgo];
		    double lo = Low[barsAgo];
		
		    double bodyLow = Math.Min(o, c);
		
		    bool wickTouches = lo <= og + eps;
		    bool bodyAbove   = bodyLow >= og - eps;   // body stays above (no body cross below)
		    return wickTouches && bodyAbove;
		}
		
		private bool WickFromBelow_ClosedBar(int closedBarIndex, double og, double eps)
		{
		    int barsAgo = CurrentBar - closedBarIndex;
		    double o = Open[barsAgo];
		    double c = Close[barsAgo];
		    double hi = High[barsAgo];
		
		    double bodyHigh = Math.Max(o, c);
		
		    bool wickTouches = hi >= og - eps;
		    bool bodyBelow   = bodyHigh <= og + eps;  // body stays below (no body cross above)
		    return wickTouches && bodyBelow;
		}
		
		private bool WickFromAbove_CurrentBar(double og, double eps)
		{
		    double o0 = Open[0];
		    double c0 = Close[0];
		    double l0 = Low[0];
		
		    double bodyLow0 = Math.Min(o0, c0);
		
		    bool wickTouches = l0 <= og + eps;
		    bool bodyAbove   = bodyLow0 >= og - eps;
		
		    return wickTouches && bodyAbove;
		}

		
		private bool WickFromBelow_CurrentBar(double og, double eps)
		{
		    double o0 = Open[0];
		    double c0 = Close[0];
		    double h0 = High[0];
		
		    double bodyHigh0 = Math.Max(o0, c0);
		
		    bool wickTouches = h0 >= og - eps;
		    bool bodyBelow   = bodyHigh0 <= og + eps;
		    return wickTouches && bodyBelow;
		}
		
		private void DerivePatternEFlipFromBase(
		    Dictionary<int, double> baseLong,
		    Dictionary<int, double> baseShort,
		    out Dictionary<int, double> flipLongByBar,
		    out Dictionary<int, double> flipShortByBar,
		    out List<PatternELevel> flipLevels)
		{
		    flipLongByBar  = new Dictionary<int, double>(256);
		    flipShortByBar = new Dictionary<int, double>(256);
		    flipLevels     = new List<PatternELevel>(128);
		
		    if (CurrentBar < 3)
		        return;
		
		    double eps = TickSize * 0.5;
		
		    // We process ONLY the bar that just closed on this BIP0 first-tick call:
		    // sigBar = CurrentBars[0] - 1
		    int sigBar = CurrentBars[0] - 1;
		
		    if (sigBar < 1)
		        return;
		
		    // ---------------------------------------------------------
		    // 1) If a BASE pattern completed on sigBar, arm flip tracking
		    // ---------------------------------------------------------
		    // Long flip is armed from BASE short completion
		    if (baseShort != null && baseShort.TryGetValue(sigBar, out double baseShortOg))
		    {
		        peFlipLongActive       = true;
		        peFlipLongOg           = baseShortOg;
		        peFlipLongArmBar       = sigBar;
		        peFlipLongSeenBreak    = false;
		        peFlipLongBreakBar     = -1;
		        peFlipLongInWickStreak = false;				
		    }
		
		    // Short flip is armed from BASE long completion
		    if (baseLong != null && baseLong.TryGetValue(sigBar, out double baseLongOg))
		    {
		        peFlipShortActive       = true;
		        peFlipShortOg           = baseLongOg;
		        peFlipShortArmBar       = sigBar;
		        peFlipShortSeenBreak    = false;
		        peFlipShortBreakBar     = -1;
		        peFlipShortInWickStreak = false;
		    }
		
		    // ---------------------------------------------------------
		    // 2) Update flip states using THIS closed bar (sigBar)
		    // ---------------------------------------------------------
		
		    // ---- FLIP LONG: after BASE short -> close above OG -> wick OG from above ----
		    if (peFlipLongActive && sigBar > peFlipLongArmBar)
		    {
		        double c = Close[1];
		
		        if (!peFlipLongSeenBreak)
		        {
		            if (c > peFlipLongOg + eps)
		            {
		                peFlipLongSeenBreak = true;
		                peFlipLongBreakBar  = sigBar;
		                peFlipLongInWickStreak = false; // streak begins only on wick bars AFTER break
		            }
		        }
		        else
		        {
		            if (sigBar > peFlipLongBreakBar)
		            {
		                bool isWick = WickFromAbove_ClosedBar(sigBar, peFlipLongOg, eps);
		
		                if (!peFlipLongInWickStreak)
		                {
		                    if (isWick)
		                    {
		                        // first wick => signal + start streak
		                        peFlipLongInWickStreak = true;
		                        flipLongByBar[sigBar]  = peFlipLongOg;
		
		                        flipLevels.Add(new PatternELevel
		                        {
		                            IsLong        = true,
		                            OgBarIndex    = peFlipLongArmBar,
		                            OgTime        = Times[0][CurrentBar - peFlipLongArmBar],
		                            EntryBarIndex = sigBar,
		                            EntryTime     = Times[0][1],
		                            Price         = peFlipLongOg
		                        });
		                    }
		                }
		                else
		                {
		                    if (isWick)
		                    {
		                        // sequential wick => sequential signal
		                        flipLongByBar[sigBar]  = peFlipLongOg;
		
		                        flipLevels.Add(new PatternELevel
		                        {
		                            IsLong        = true,
		                            OgBarIndex    = peFlipLongArmBar,
		                            OgTime        = Times[0][CurrentBar - peFlipLongArmBar],
		                            EntryBarIndex = sigBar,
		                            EntryTime     = Times[0][1],
		                            Price         = peFlipLongOg
		                        });
		                    }
		                    else
		                    {
		                        // streak ended => invalidate
		                        peFlipLongActive       = false;
		                        peFlipLongSeenBreak    = false;
		                        peFlipLongInWickStreak = false;
		                    }
		                }
		            }
		        }
		    }
		
		    // ---- FLIP SHORT: after BASE long -> close below OG -> wick OG from below ----
		    if (peFlipShortActive && sigBar > peFlipShortArmBar)
		    {
		        double c = Close[1];
		
		        if (!peFlipShortSeenBreak)
		        {
		            if (c < peFlipShortOg - eps)
		            {
		                peFlipShortSeenBreak = true;
		                peFlipShortBreakBar  = sigBar;
		                peFlipShortInWickStreak = false;
		            }
		        }
		        else
		        {
		            if (sigBar > peFlipShortBreakBar)
		            {
		                bool isWick = WickFromBelow_ClosedBar(sigBar, peFlipShortOg, eps);
		
		                if (!peFlipShortInWickStreak)
		                {
		                    if (isWick)
		                    {
		                        peFlipShortInWickStreak = true;
		                        flipShortByBar[sigBar]  = peFlipShortOg;
		
		                        flipLevels.Add(new PatternELevel
		                        {
		                            IsLong        = false,
		                            OgBarIndex    = peFlipShortArmBar,
		                            OgTime        = Times[0][CurrentBar - peFlipShortArmBar],
		                            EntryBarIndex = sigBar,
		                            EntryTime     = Times[0][1],
		                            Price         = peFlipShortOg
		                        });
		                    }
		                }
		                else
		                {
		                    if (isWick)
		                    {
		                        flipShortByBar[sigBar]  = peFlipShortOg;
		
		                        flipLevels.Add(new PatternELevel
		                        {
		                            IsLong        = false,
		                            OgBarIndex    = peFlipShortArmBar,
		                            OgTime        = Times[0][CurrentBar - peFlipShortArmBar],
		                            EntryBarIndex = sigBar,
		                            EntryTime     = Times[0][1],
		                            Price         = peFlipShortOg
		                        });
		                    }
		                    else
		                    {
		                        peFlipShortActive       = false;
		                        peFlipShortSeenBreak    = false;
		                        peFlipShortInWickStreak = false;
		                    }
		                }
		            }
		        }
		    }
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
//		    if (!ShowZigZagDebug && !DebugDrawPatternE /*&& !DebugDrawPatternA */)
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
		private AlightenLevelMultiTimeframeMATickPatternEV0003[] cacheAlightenLevelMultiTimeframeMATickPatternEV0003;
		public AlightenLevelMultiTimeframeMATickPatternEV0003 AlightenLevelMultiTimeframeMATickPatternEV0003(int barsToProcess, bool enablePatternESignal, bool enablePatternELevels, bool drawPatternE, int patternEMarkerFontSize, int patternEMarkerOffsetTicks, System.Windows.Media.Brush patternELongColor, System.Windows.Media.Brush patternEShortColor, int patternELevelWidth, DashStyleHelper patternELevelDashStyle, bool requirePivotForPatternESignal, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return AlightenLevelMultiTimeframeMATickPatternEV0003(Input, barsToProcess, enablePatternESignal, enablePatternELevels, drawPatternE, patternEMarkerFontSize, patternEMarkerOffsetTicks, patternELongColor, patternEShortColor, patternELevelWidth, patternELevelDashStyle, requirePivotForPatternESignal, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public AlightenLevelMultiTimeframeMATickPatternEV0003 AlightenLevelMultiTimeframeMATickPatternEV0003(ISeries<double> input, int barsToProcess, bool enablePatternESignal, bool enablePatternELevels, bool drawPatternE, int patternEMarkerFontSize, int patternEMarkerOffsetTicks, System.Windows.Media.Brush patternELongColor, System.Windows.Media.Brush patternEShortColor, int patternELevelWidth, DashStyleHelper patternELevelDashStyle, bool requirePivotForPatternESignal, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			if (cacheAlightenLevelMultiTimeframeMATickPatternEV0003 != null)
				for (int idx = 0; idx < cacheAlightenLevelMultiTimeframeMATickPatternEV0003.Length; idx++)
					if (cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx] != null && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].BarsToProcess == barsToProcess && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].EnablePatternESignal == enablePatternESignal && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].EnablePatternELevels == enablePatternELevels && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].DrawPatternE == drawPatternE && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].PatternEMarkerFontSize == patternEMarkerFontSize && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].PatternEMarkerOffsetTicks == patternEMarkerOffsetTicks && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].PatternELongColor == patternELongColor && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].PatternEShortColor == patternEShortColor && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].PatternELevelWidth == patternELevelWidth && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].PatternELevelDashStyle == patternELevelDashStyle && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].RequirePivotForPatternESignal == requirePivotForPatternESignal && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].ShowZigZag == showZigZag && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].ZigZagColor == zigZagColor && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].ZigZagWidth == zigZagWidth && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].ZigZagStyle == zigZagStyle && cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx].EqualsInput(input))
						return cacheAlightenLevelMultiTimeframeMATickPatternEV0003[idx];
			return CacheIndicator<AlightenLevelMultiTimeframeMATickPatternEV0003>(new AlightenLevelMultiTimeframeMATickPatternEV0003(){ BarsToProcess = barsToProcess, EnablePatternESignal = enablePatternESignal, EnablePatternELevels = enablePatternELevels, DrawPatternE = drawPatternE, PatternEMarkerFontSize = patternEMarkerFontSize, PatternEMarkerOffsetTicks = patternEMarkerOffsetTicks, PatternELongColor = patternELongColor, PatternEShortColor = patternEShortColor, PatternELevelWidth = patternELevelWidth, PatternELevelDashStyle = patternELevelDashStyle, RequirePivotForPatternESignal = requirePivotForPatternESignal, ShowZigZag = showZigZag, ZigZagColor = zigZagColor, ZigZagWidth = zigZagWidth, ZigZagStyle = zigZagStyle }, input, ref cacheAlightenLevelMultiTimeframeMATickPatternEV0003);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenLevelMultiTimeframeMATickPatternEV0003 AlightenLevelMultiTimeframeMATickPatternEV0003(int barsToProcess, bool enablePatternESignal, bool enablePatternELevels, bool drawPatternE, int patternEMarkerFontSize, int patternEMarkerOffsetTicks, System.Windows.Media.Brush patternELongColor, System.Windows.Media.Brush patternEShortColor, int patternELevelWidth, DashStyleHelper patternELevelDashStyle, bool requirePivotForPatternESignal, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternEV0003(Input, barsToProcess, enablePatternESignal, enablePatternELevels, drawPatternE, patternEMarkerFontSize, patternEMarkerOffsetTicks, patternELongColor, patternEShortColor, patternELevelWidth, patternELevelDashStyle, requirePivotForPatternESignal, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenLevelMultiTimeframeMATickPatternEV0003 AlightenLevelMultiTimeframeMATickPatternEV0003(ISeries<double> input , int barsToProcess, bool enablePatternESignal, bool enablePatternELevels, bool drawPatternE, int patternEMarkerFontSize, int patternEMarkerOffsetTicks, System.Windows.Media.Brush patternELongColor, System.Windows.Media.Brush patternEShortColor, int patternELevelWidth, DashStyleHelper patternELevelDashStyle, bool requirePivotForPatternESignal, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternEV0003(input, barsToProcess, enablePatternESignal, enablePatternELevels, drawPatternE, patternEMarkerFontSize, patternEMarkerOffsetTicks, patternELongColor, patternEShortColor, patternELevelWidth, patternELevelDashStyle, requirePivotForPatternESignal, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenLevelMultiTimeframeMATickPatternEV0003 AlightenLevelMultiTimeframeMATickPatternEV0003(int barsToProcess, bool enablePatternESignal, bool enablePatternELevels, bool drawPatternE, int patternEMarkerFontSize, int patternEMarkerOffsetTicks, System.Windows.Media.Brush patternELongColor, System.Windows.Media.Brush patternEShortColor, int patternELevelWidth, DashStyleHelper patternELevelDashStyle, bool requirePivotForPatternESignal, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternEV0003(Input, barsToProcess, enablePatternESignal, enablePatternELevels, drawPatternE, patternEMarkerFontSize, patternEMarkerOffsetTicks, patternELongColor, patternEShortColor, patternELevelWidth, patternELevelDashStyle, requirePivotForPatternESignal, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenLevelMultiTimeframeMATickPatternEV0003 AlightenLevelMultiTimeframeMATickPatternEV0003(ISeries<double> input , int barsToProcess, bool enablePatternESignal, bool enablePatternELevels, bool drawPatternE, int patternEMarkerFontSize, int patternEMarkerOffsetTicks, System.Windows.Media.Brush patternELongColor, System.Windows.Media.Brush patternEShortColor, int patternELevelWidth, DashStyleHelper patternELevelDashStyle, bool requirePivotForPatternESignal, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternEV0003(input, barsToProcess, enablePatternESignal, enablePatternELevels, drawPatternE, patternEMarkerFontSize, patternEMarkerOffsetTicks, patternELongColor, patternEShortColor, patternELevelWidth, patternELevelDashStyle, requirePivotForPatternESignal, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

#endregion

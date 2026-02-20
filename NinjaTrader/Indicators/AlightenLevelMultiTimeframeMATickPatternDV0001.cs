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
    public class AlightenLevelMultiTimeframeMATickPatternDV0001 : Indicator
    {
		
		#region Class Variables

        // HTF pivots (BIP 0 only)
        private List<DateTime> pivotTimes;
        private List<double>   pivotPrices;
        private List<bool>     pivotIsHigh;
        private List<double>   pivotGuidePrices;
		
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
		
		// Pattern D results (MA-friendly persistent state)
		// barIndex is primary-series (BIP 0) absolute bar index (0..CurrentBar)
		private Dictionary<int, double> patternDLongByBar;
		private Dictionary<int, double> patternDShortByBar;
		
		// Pattern D derived levels (logical objects; optionally drawn in debug)
		private List<PatternDLevel> patternDLevels;
		
		
		private List<PatternDZone> patternDZones;
		
		private class PatternDZone
		{
		    public long 	ZoneKey; // e.g., PivotTime.Ticks ^ (IsHighZone ? 0x1 : 0x2)
			public bool     IsHighZone;       // true = zone created at pivot high, false = pivot low
		    public int      PivotBarIndex;    // bar index of pivot bar used to create the zone
		    public DateTime PivotTime;
		
		    public double   ZoneLow;
		    public double   ZoneHigh;
			
			public double   PivotGuide;
		
		    // lifecycle
		    public bool     Triggered;        // first wick touch occurred
		    public bool     Invalidated;      // invalidated by close beyond zone (directional rule)
		    public int      TriggerBarIndex;  // first touch bar index (optional)
			
			public int      ConfirmBarIndex;   // bar index of the opposite pivot that confirms this zone
			public DateTime ConfirmTime;

		}

		
		// Represents a horizontal Pattern D level to draw (at OG price), ending at the wick/entry pivot
		private class PatternDLevel
		{
		    public bool     IsLong;
		    public int      OgBarIndex;
		    public DateTime OgTime;
		
		    public int      EntryBarIndex;
		    public DateTime EntryTime;
		
		    public double   Price; // OG level (guide)
		
		    public PatternDLevel() { }
		}
				
		private Dictionary<string, int> lastSignalBip0BarByKey;


		// --- Pattern D live recency (BIP0) state ---
		private bool pdLivePrevLong  = false;
		private bool pdLivePrevShort = false;
		
		private int  pdLiveLongLastEdgeSeq  = -1;   // last tick-seq where long became true (false->true)
		private int  pdLiveShortLastEdgeSeq = -1;   // last tick-seq where short became true (false->true)
		private int  pdLiveTickSeq          = 0;    // increments each BIP0 call within a bar
		
		private int  pdLiveLastChosenDir    = 0;    // +1 long, -1 short, 0 none (stability tie-break)
		
		private const string PD_LIVE_TAG_L  = "PD_LIVE_TAG_L";
		private const string PD_LIVE_TAG_S  = "PD_LIVE_TAG_S";
		private const string PD_LIVE_LVL_L  = "PD_LIVE_LVL_L";
		private const string PD_LIVE_LVL_S  = "PD_LIVE_LVL_S";
		
		private const string PD_ZONE_TAG_PREFIX = "PD_ZONE_";

		private Dictionary<long, int> pivotBarIndexByTimeTicks; // key = pivot.Time.Ticks
		
		
	
        #endregion
		
        #region Properties
		

        [NinjaScriptProperty]
		[Range(0, 500000)]
		[Display(Name="Bars To Process", GroupName="ZigZag", Order=53)]
		public int BarsToProcess { get; set; } = 200; // 0 = process all
		
		// ---- Pattern D toggles ----

		[NinjaScriptProperty]
		[Display(Name="Enable Pattern D Signals", GroupName="Pattern D", Order=1)]
		public bool EnablePatternDSignal { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Enable Pattern D Levels", GroupName="Pattern D", Order=2)]
		public bool EnablePatternDLevels { get; set; } = true;
	
		[NinjaScriptProperty]
		[Display(Name = "Draw Pattern D", GroupName = "Pattern D", Order = 3)]
		public bool DrawPatternD { get; set; } = true;
		
		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name="Pattern D Gap Size Ticks", GroupName="Pattern D", Order=4)]
		public double PatternDGapSizeTicks { get; set; } = 3.0;
		
		// Optional styling knobs (safe defaults)
		[NinjaScriptProperty]
		[Range(6, 30)]
		[Display(Name="Pattern D Marker Font Size", GroupName="Pattern D", Order=20)]
		public int PatternDMarkerFontSize { get; set; } = 18;
		
		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name="Pattern D Marker Offset Ticks", GroupName="Pattern D", Order=21)]
		public int PatternDMarkerOffsetTicks { get; set; } = 20;
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern D Long Color", GroupName="Pattern D", Order=22)]
		public System.Windows.Media.Brush PatternDLongColor { get; set; } = Brushes.BlueViolet;
		
		[Browsable(false)]
		public string PatternDLongColorSerialize
		{
		    get => Serialize.BrushToString(PatternDLongColor);
		    set => PatternDLongColor = Serialize.StringToBrush(value);
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern D Short Color", GroupName="Pattern D", Order=23)]
		public System.Windows.Media.Brush PatternDShortColor { get; set; } = Brushes.OrangeRed;
		
		[Browsable(false)]
		public string PatternDShortColorSerialize
		{
		    get => Serialize.BrushToString(PatternDShortColor);
		    set => PatternDShortColor = Serialize.StringToBrush(value);
		}
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Pattern D Level Width", GroupName="Pattern D", Order=30)]
		public int PatternDLevelWidth { get; set; } = 2;
		
		[NinjaScriptProperty]
		[Display(Name="Pattern D Level Dash", GroupName="Pattern D", Order=31)]
		public DashStyleHelper PatternDLevelDashStyle { get; set; } = DashStyleHelper.Dash;

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
		
		private Series<double> patternDSignal;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternDSignal
		{
		    get { return patternDSignal; }
		}
		
		private Series<double> patternDSignalMA;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternDSignalMA
		{
		    get { return patternDSignalMA; }
		}
		
		#endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AlightenLevelMultiTimeframeMATickPatternDV0001";
                Description = "Market Analyzer core: HTF ZigZag/levels on primary series with 1m/5m proximity columns.";
                Calculate   = Calculate.OnEachTick;

                // For MA, overlay doesn’t matter. For chart debugging, overlay helps.
                IsOverlay               		= true;
                DrawOnPricePanel        		= true;

                DisplayInDataBox        		= true;
                PaintPriceMarkers       		= false;
                IsSuspendedWhileInactive 		= false;
				ShowTransparentPlotsInDataBox 	= true;

				AddPlot(Brushes.Transparent, "PatternDSignal");
				AddPlot(Brushes.Transparent, "PatternDSignalMA");
                
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
				
				patternDLongByBar  				= new Dictionary<int, double>(256);
				patternDShortByBar 				= new Dictionary<int, double>(256);
				patternDLevels     				= new List<PatternDLevel>(128);
				patternDZones 					= new List<PatternDZone>(128);

				
				lastSignalBip0BarByKey = new Dictionary<string, int>(512, StringComparer.Ordinal);

				patternDSignal					= Values[0];
				patternDSignalMA				= Values[1];

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
		        if (EnablePatternDSignal)
		        {
		            //Print($"[LIVE] t={Times[0][0]:HH:mm:ss.fff} Close0={Close[0]}");
		            UpdatePatternD_Live_BIP0();
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
		        RebuildPatternD_Pipeline_BIP0();
		
		        if (EnablePatternDSignal)
		        {
		            // Default clear
		            patternDSignal[0]   = 0;
		            patternDSignalMA[0] = 0;
		
		            if (CurrentBars[0] < 1)
		                return;
		
		            int sigBar = CurrentBars[0] - 1;
		
		            bool longNow  = patternDLongByBar  != null && patternDLongByBar.ContainsKey(sigBar);
		            bool shortNow = patternDShortByBar != null && patternDShortByBar.ContainsKey(sigBar);
		
		            double sig = 0;
		            if (longNow && !shortNow) sig = +1;
		            else if (shortNow && !longNow) sig = -1;
		
		            // DataBox / chart-aligned plot (belongs to bar that just closed)
		            patternDSignal[1] = sig;
		
		            // MA-friendly plot (belongs to current bar so MA reads [0] reliably)
		            patternDSignalMA[0] = sig;
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
		        Level      = price,
		        Time       = time,
		        Status     = "NEW",
		        IsHigh     = isHigh,
		        GuideLevel = guidePrice
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
		
		#region Pattern D
		
		private void RebuildPatternD_Pipeline_BIP0()
		{
		    if (!EnablePatternDSignal && !EnablePatternDLevels)
		        return;
		
		    if (CurrentBars[0] < 1)
		        return;
		
		    int sigBar = CurrentBars[0] - 1;
		
		    // Compute truth (your existing compute step)
		    RebuildPatternD_BIP0();
		
		    // Publish truth (plots only)
		    ApplyPatternDResults_BIP0(sigBar);
		
		    // Chart-only visualization
		    if (DrawPatternD && CanDrawDebug())
		        DrawPatternDDebug_BIP0(sigBar);
		}
		
		private void UpdatePatternD_Live_BIP0()
		{
		    // Per-bar reset for recency tracking (BIP0)
		    if (IsFirstTickOfBar)
		    {
		        pdLivePrevLong         = false;
		        pdLivePrevShort        = false;
		        pdLiveLongLastEdgeSeq  = -1;
		        pdLiveShortLastEdgeSeq = -1;
		        pdLiveTickSeq          = 0;
		        pdLiveLastChosenDir    = 0;
		    }
		    else
		    {
		        pdLiveTickSeq++;
		    }
		
		    // Safety
		    if (CurrentBars[0] < 1)
		    {
		        patternDSignal[0] = 0;
		
		        pdLivePrevLong  = false;
		        pdLivePrevShort = false;
		
		        if (CanDrawDebug() && DrawPatternD)
		        {
		            RemoveDrawObject(PD_LIVE_TAG_L);
		            RemoveDrawObject(PD_LIVE_TAG_S);
		            RemoveDrawObject(PD_LIVE_LVL_L);
		            RemoveDrawObject(PD_LIVE_LVL_S);
		        }
		        return;
		    }
		
		    // If zones haven't been built yet this bar, just clear live.
		    if (patternDZones == null || patternDZones.Count == 0)
		    {
		        patternDSignal[0] = 0;
		
		        pdLivePrevLong  = false;
		        pdLivePrevShort = false;
		
		        if (CanDrawDebug() && DrawPatternD)
		        {
		            RemoveDrawObject(PD_LIVE_TAG_L);
		            RemoveDrawObject(PD_LIVE_TAG_S);
		            RemoveDrawObject(PD_LIVE_LVL_L);
		            RemoveDrawObject(PD_LIVE_LVL_S);
		        }
		        return;
		    }
		
		    double eps = TickSize * 0.5;
		
		    // Current forming bar (BIP0) intrabar state
		    double c0 = Close[0];
		    double h0 = High[0];
		    double l0 = Low[0];
		
		    int cb = CurrentBars[0]; // forming bar absolute index
		
		    bool liveLong  = false;
		    bool liveShort = false;
		
		    double bestLongDist  = double.MaxValue;
		    double bestShortDist = double.MaxValue;
		
		    PatternDZone bestLongZone  = null;
		    PatternDZone bestShortZone = null;
		
		    // Scalar plot chooses ONE "best" zone (distance, then recency)
		    PatternDZone best = null;
		    double bestDist   = double.MaxValue;
		    int bestRecency   = int.MinValue;
		
		    double DistanceToZone(double price, PatternDZone z)
		    {
		        if (price < z.ZoneLow)  return z.ZoneLow - price;
		        if (price > z.ZoneHigh) return price - z.ZoneHigh;
		        return 0.0;
		    }
		
		    for (int i = 0; i < patternDZones.Count; i++)
		    {
		        PatternDZone z = patternDZones[i];
		        if (z == null)
		            continue;
		
		        // IMPORTANT: LIVE must be READ-ONLY. Do not mutate z.Invalidated / z.Triggered.
		        if (z.Invalidated || z.Triggered)
		            continue;
		
		        // Must be AFTER confirmation bar (exclude confirmation bar itself)
		        if (cb <= z.ConfirmBarIndex)
		            continue;
		
		        // Directional invalidation gate (LIVE): if already invalid on this close, ignore for touching
		        // (Do NOT set Invalidated=true here; closed-bar pipeline will do it deterministically.)
		        if (z.IsHighZone)
		        {
		            if (c0 > z.ZoneHigh + eps)
		                continue;
		        }
		        else
		        {
		            if (c0 < z.ZoneLow - eps)
		                continue;
		        }
		
		        // Touch = any intersection of candle range with zone (includes body edges by definition)
		        bool touches = (h0 >= z.ZoneLow - eps) && (l0 <= z.ZoneHigh + eps);
		        if (!touches)
		            continue;
		
		        double dist = DistanceToZone(c0, z);
		        int recency = z.PivotBarIndex;
		
		        if (!z.IsHighZone)
		        {
		            liveLong = true;
		            if (dist < bestLongDist)
		            {
		                bestLongDist = dist;
		                bestLongZone = z;
		            }
		        }
		        else
		        {
		            liveShort = true;
		            if (dist < bestShortDist)
		            {
		                bestShortDist = dist;
		                bestShortZone = z;
		            }
		        }
		
		        if (best == null ||
		            dist < bestDist ||
		            (dist == bestDist && recency > bestRecency))
		        {
		            best = z;
		            bestDist = dist;
		            bestRecency = recency;
		        }
		    }
		
		    // --- Recency tracking (optional; kept to preserve your existing fields) ---
		    if (liveLong && !pdLivePrevLong)
		        pdLiveLongLastEdgeSeq = pdLiveTickSeq;
		
		    if (liveShort && !pdLivePrevShort)
		        pdLiveShortLastEdgeSeq = pdLiveTickSeq;
		
		    pdLivePrevLong  = liveLong;
		    pdLivePrevShort = liveShort;
		
		    // Scalar live plot: choose ONE best zone (matches historical "best" selection logic)
		    double liveSig = 0;
		    if (best != null)
		        liveSig = best.IsHighZone ? -1 : +1;
		
		    patternDSignal[0] = liveSig;
		
		    // --- Draw live artifacts (both sides allowed) ---
		    if (CanDrawDebug() && DrawPatternD)
		    {
		        // Clear all live artifacts first
		        RemoveDrawObject(PD_LIVE_TAG_L);
		        RemoveDrawObject(PD_LIVE_TAG_S);
		        RemoveDrawObject(PD_LIVE_LVL_L);
		        RemoveDrawObject(PD_LIVE_LVL_S);
		
		        DateTime t2 = Times[0][0];
		
		        // LONG side
		        if (bestLongZone != null)
		        {
		            DateTime t1 = t2;
		
		            DateTime pt = bestLongZone.PivotTime;
		            int pBarIx = BarsArray[0].GetBar(pt);
		            if (pBarIx >= 0)
		                t1 = pt;
		
		            double lvl = bestLongZone.PivotGuide;
		
		            Draw.Line(this, PD_LIVE_LVL_L, false,
		                t1, lvl,
		                t2, lvl,
		                PatternDLongColor, PatternDLevelDashStyle, PatternDLevelWidth);
		
		            double yL = l0 - (PatternDMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PD_LIVE_TAG_L, false,
		                "▲",
		                0,
		                yL,
		                0,
		                PatternDLongColor,
		                new SimpleFont("Arial", PatternDMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);
		        }
		
		        // SHORT side
		        if (bestShortZone != null)
		        {
		            DateTime t1 = t2;
		
		            DateTime pt = bestShortZone.PivotTime;
		            int pBarIx = BarsArray[0].GetBar(pt);
		            if (pBarIx >= 0)
		                t1 = pt;
		
		            double lvl = bestShortZone.PivotGuide;
		
		            Draw.Line(this, PD_LIVE_LVL_S, false,
		                t1, lvl,
		                t2, lvl,
		                PatternDShortColor, PatternDLevelDashStyle, PatternDLevelWidth);
		
		            double yS = h0 + (PatternDMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PD_LIVE_TAG_S, false,
		                "▼",
		                0,
		                yS,
		                0,
		                PatternDShortColor,
		                new SimpleFont("Arial", PatternDMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);
		        }
		    }
		}

		
		private void RebuildPatternD_BIP0()
		{
		    // ---- Safety / init ----
		    if (BarsInProgress != 0)
		        return;
		
		    if (CurrentBars[0] < 1)
		        return; // need [1]
		
		    if (exportPivots == null || exportPivots.Count < 2)
		        return;
		
		    if (patternDZones == null)
		        patternDZones = new List<PatternDZone>(128);
		
		    if (patternDLongByBar == null)
		        patternDLongByBar = new Dictionary<int, double>(256);
		
		    if (patternDShortByBar == null)
		        patternDShortByBar = new Dictionary<int, double>(256);
		
		    if (patternDLevels == null)
		        patternDLevels = new List<PatternDLevel>(128);
		
		    if (pivotBarIndexByTimeTicks == null)
		        pivotBarIndexByTimeTicks = new Dictionary<long, int>(512);
		
		    int cb     = CurrentBars[0];
		    int sigBar = cb - 1; // bar that just closed
		
		    // Remove any prior computed signal for this bar; recompute deterministically.
		    patternDLongByBar.Remove(sigBar);
		    patternDShortByBar.Remove(sigBar);
		
		    // ---- Optional BarsToProcess windowing (skip old pivots; no pruning of lists) ----
		    int cutoffBar = 0;
		    if (BarsToProcess > 0 && BarsArray[0] != null && BarsArray[0].Count > BarsToProcess)
		    {
		        int totalLoaded = BarsArray[0].Count;
		        cutoffBar       = totalLoaded - BarsToProcess;
		        if (cutoffBar < 0) cutoffBar = 0;
		    }
		
		    // ---- Helper: resolve pivotTime -> BIP0 barIndex (absolute) with caching ----
		    bool TryGetPivotBarIndex(DateTime pivotTime, out int barIndex)
		    {
		        long k = pivotTime.Ticks;
		
		        if (pivotBarIndexByTimeTicks.TryGetValue(k, out barIndex))
		            return barIndex >= 0;
		
		        barIndex = BarsArray[0].GetBar(pivotTime);
		
		        pivotBarIndexByTimeTicks[k] = barIndex;
		
		        return barIndex >= 0;
		    }
		
		    // ---- Build zones from confirmed pivots (confirmed = has opposite pivot after it) ----
		    // De-dup against existing zones using ZoneKey.
		    HashSet<long> existingZoneKeys = new HashSet<long>();
		    for (int z = 0; z < patternDZones.Count; z++)
		        existingZoneKeys.Add(patternDZones[z].ZoneKey);
		
		    int epCount        = exportPivots.Count;
		    int lastConfirmedI = epCount - 2; // i+1 exists
		
		    int startI = Math.Max(0, lookbackStartPivotIdx);
		
		    // Advance lookbackStartPivotIdx to skip pivots older than cutoffBar (no list pruning)
		    if (cutoffBar > 0)
		    {
		        for (int i = startI; i <= lastConfirmedI; i++)
		        {
		            ExportPivot p = exportPivots[i];
		
		            int pBar;
		            if (!TryGetPivotBarIndex(p.Time, out pBar))
		                continue;
		
		            if (pBar < cutoffBar)
		                lookbackStartPivotIdx = i + 1;
		            else
		                break;
		        }
		
		        startI = Math.Max(0, lookbackStartPivotIdx);
		    }
		
		    double epsGap = Math.Max(TickSize * 0.5, PatternDGapSizeTicks * TickSize);
		
		    for (int i = startI; i <= lastConfirmedI; i++)
		    {
		        ExportPivot p     = exportPivots[i];
		        ExportPivot pNext = exportPivots[i + 1];
		
		        // Must alternate to be a "confirmed" pivot (high confirmed by later low, low confirmed by later high)
		        if (pNext == null || pNext.IsHigh == p.IsHigh)
		            continue;
		
		        int pivotBarIndex;
		        if (!TryGetPivotBarIndex(p.Time, out pivotBarIndex))
		            continue;
		
		        // Confirmation bar = the opposite pivot that confirms this pivot
		        int confirmBarIndex;
		        if (!TryGetPivotBarIndex(pNext.Time, out confirmBarIndex))
		            continue;
		
		        if (pivotBarIndex < 1)
		            continue; // need prior bar to pivot bar
		
		        if (cutoffBar > 0 && pivotBarIndex < cutoffBar)
		            continue;
		
		        int barsAgoPivot = cb - pivotBarIndex;
		        if (barsAgoPivot < 0)
		            continue;
		
		        int barsAgoPrev = barsAgoPivot + 1;
		
		        if (barsAgoPrev > CurrentBars[0])
		            continue;
		
		        // --- Candle polarity requirements (your new rule) ---
		        // For pivot LOW zones:
		        //   prev bar must be RED, pivot bar must be GREEN, then compare body-lows.
		        //
		        // For pivot HIGH zones:
		        //   prev bar must be GREEN, pivot bar must be RED, then compare body-highs.
		        bool prevGreen  = Close[barsAgoPrev] >= Open[barsAgoPrev];
		        bool prevRed    = Close[barsAgoPrev] <  Open[barsAgoPrev];
		
		        bool pivotGreen = Close[barsAgoPivot] >= Open[barsAgoPivot];
		        bool pivotRed   = Close[barsAgoPivot] <  Open[barsAgoPivot];
		
		        double zoneLow, zoneHigh;
		
		        if (p.IsHigh)
		        {
		            // Pivot HIGH zone requires: prev green, pivot red
		            if (!prevGreen || !pivotRed)
		                continue;
		
		            double bhPrev  = Math.Max(Open[barsAgoPrev],  Close[barsAgoPrev]);
		            double bhPivot = Math.Max(Open[barsAgoPivot], Close[barsAgoPivot]);
		
		            // Only a gap if they differ by at least PatternDGapSizeTicks (or half-tick minimum)
		            if (Math.Abs(bhPrev - bhPivot) < epsGap)
		                continue;
		
		            zoneLow  = Math.Min(bhPrev, bhPivot);
		            zoneHigh = Math.Max(bhPrev, bhPivot);
		        }
		        else
		        {
		            // Pivot LOW zone requires: prev red, pivot green
		            if (!prevRed || !pivotGreen)
		                continue;
		
		            double blPrev  = Math.Min(Open[barsAgoPrev],  Close[barsAgoPrev]);
		            double blPivot = Math.Min(Open[barsAgoPivot], Close[barsAgoPivot]);
		
		            // Only a gap if they differ by at least PatternDGapSizeTicks (or half-tick minimum)
		            if (Math.Abs(blPrev - blPivot) < epsGap)
		                continue;
		
		            zoneLow  = Math.Min(blPrev, blPivot);
		            zoneHigh = Math.Max(blPrev, blPivot);
		        }
		
		        if (double.IsNaN(zoneLow) || double.IsNaN(zoneHigh))
		            continue;
		
		        // Strengthened ZoneKey to avoid collisions
		        long zoneKey = p.Time.Ticks ^ ((long)pivotBarIndex << 1) ^ (p.IsHigh ? 1L : 2L);
		
		        if (existingZoneKeys.Contains(zoneKey))
		            continue;
		
		        PatternDZone zone = new PatternDZone();
		        zone.ZoneKey         = zoneKey;
		        zone.IsHighZone      = p.IsHigh;
		        zone.PivotBarIndex   = pivotBarIndex;
		        zone.PivotTime       = p.Time;
		
		        // IMPORTANT: used to prevent confirmation-bar triggers
		        zone.ConfirmBarIndex = confirmBarIndex;
		        zone.ConfirmTime     = pNext.Time;
				
				zone.PivotGuide 	 = p.GuideLevel;
		
		        zone.ZoneLow         = zoneLow;
		        zone.ZoneHigh        = zoneHigh;
		        zone.Triggered       = false;
		        zone.Invalidated     = false;
		        zone.TriggerBarIndex = -1;
		
		        patternDZones.Add(zone);
		        existingZoneKeys.Add(zoneKey);
		    }
		
		    // ---- Evaluate zones on the bar that just closed: [1] ----
		    double hi = High[1];
		    double lo = Low[1];
		    double cl = Close[1];
		    DateTime tSig = Times[0][1];
		
		    // Collect all newly-touched zones this bar (first touch only), then select ONE for plotting.
		    List<PatternDZone> touched = null;
		
		    for (int z = 0; z < patternDZones.Count; z++)
		    {
		        PatternDZone zone = patternDZones[z];
		        if (zone == null)
		            continue;
		
		        if (zone.Invalidated || zone.Triggered)
		            continue;
		
		        // Must be AFTER confirmation bar (exclude confirmation bar itself)
		        if (sigBar <= zone.ConfirmBarIndex)
		            continue;
		
		        // Directional invalidation
		        if (zone.IsHighZone)
		        {
		            // High-zone invalidated only when price CLOSES ABOVE the zone
		            if (cl > zone.ZoneHigh)
		            {
		                zone.Invalidated = true;
		                continue;
		            }
		        }
		        else
		        {
		            // Low-zone invalidated only when price CLOSES BELOW the zone
		            if (cl < zone.ZoneLow)
		            {
		                zone.Invalidated = true;
		                continue;
		            }
		        }
		
		        // First-touch = any intersection of candle range with zone (includes body edges)
		        if (hi >= zone.ZoneLow && lo <= zone.ZoneHigh)
		        {
		            if (touched == null)
		                touched = new List<PatternDZone>(4);
		
		            touched.Add(zone);
		        }
		    }
		
		    if (touched == null || touched.Count == 0)
		        return;
		
		    // Mark ALL touched zones as Triggered (first-touch only), but choose ONE to plot
		    // based on distance of Close[1] to zone (0 if within), then most-recent pivot.
		    PatternDZone best = null;
		    double bestDist = double.MaxValue;
		    int bestRecency = int.MinValue;
		
		    double DistanceToZone(double price, PatternDZone z0)
		    {
		        if (price < z0.ZoneLow)  return z0.ZoneLow - price;
		        if (price > z0.ZoneHigh) return price - z0.ZoneHigh;
		        return 0.0;
		    }
		
		    for (int i = 0; i < touched.Count; i++)
		    {
		        PatternDZone z0 = touched[i];
		
		        z0.Triggered       = true;
		        z0.TriggerBarIndex = sigBar;
		
		        double dist = DistanceToZone(cl, z0);
		        int recency = z0.PivotBarIndex;
		
		        if (best == null ||
		            dist < bestDist ||
		            (dist == bestDist && recency > bestRecency))
		        {
		            best = z0;
		            bestDist = dist;
		            bestRecency = recency;
		        }
		    }
		
		    if (best == null)
		        return;
			
			double guide = best.PivotGuide;
		
		    // Record ONE signal for plotting
		   	if (best.IsHighZone)
			    patternDShortByBar[sigBar] = guide;
			else
			    patternDLongByBar[sigBar] = guide;
		
		    // Store a single "level" object for debug drawing
		    PatternDLevel lvl = new PatternDLevel();
		    lvl.IsLong        = !best.IsHighZone;
		    lvl.OgBarIndex    = best.PivotBarIndex;
		    lvl.OgTime        = best.PivotTime;
		    lvl.EntryBarIndex = sigBar;
		    lvl.EntryTime     = tSig;
		    lvl.Price         = guide;
		
		    patternDLevels.Add(lvl);
		}

		private void ApplyPatternDResults_BIP0(int sigBar)
		{
		    // BIP0 only
		    if (BarsInProgress != 0)
		        return;
		
		    // Ensure containers exist
		    if (patternDLongByBar == null)
		        patternDLongByBar = new Dictionary<int, double>(256);
		
		    if (patternDShortByBar == null)
		        patternDShortByBar = new Dictionary<int, double>(256);
		
		    if (patternDLevels == null)
		        patternDLevels = new List<PatternDLevel>(128);
		
		    // ---- Signals (plots + dict gating) ----
		    if (EnablePatternDSignal)
		    {
		        // Default clear on current bar (MA safety)
		        patternDSignal[0]   = 0;
		        patternDSignalMA[0] = 0;
		
		        // Need a valid closed-bar index to publish [1]
		        if (sigBar < 0)
		            return;
		
		        bool longNow  = patternDLongByBar.ContainsKey(sigBar);
		        bool shortNow = patternDShortByBar.ContainsKey(sigBar);
		
		        double sig = 0;
		        if (longNow && !shortNow) sig = +1;
		        else if (shortNow && !longNow) sig = -1;
		
		        // DataBox / chart-aligned plot (belongs to bar that just closed)
		        patternDSignal[1] = sig;
		
		        // MA-friendly plot (belongs to current bar so MA reads [0] reliably)
		        patternDSignalMA[0] = sig;
		    }
		    else
		    {
		        // Signal feature disabled: keep plots quiet and clear any computed state
		        patternDSignal[0]   = 0;
		        patternDSignalMA[0] = 0;
		
		        // Optional: clear closed-bar slot too (safe if CurrentBars[0] >= 1)
		        if (CurrentBars[0] >= 1)
		            patternDSignal[1] = 0;
		
		        patternDLongByBar.Clear();
		        patternDShortByBar.Clear();
		    }
		
		    // ---- Levels (list gating only) ----
		    if (!EnablePatternDLevels)
		    {
		        patternDLevels.Clear();
		    }
		}

		private void DrawPatternDDebug_BIP0(int sigBar)
		{
		    if (!CanDrawDebug() || !DrawPatternD)
		        return;
		
		    // ---- SIGNALS (triangles) ----
		    // Clear stale markers for bars we no longer have keys for.
		    // NOTE: Pattern D is "first touch only" so markers should normally only ever be added, not removed,
		    // unless you clear dictionaries via windowing/recompute. This keeps it safe.
		
		    // LONG markers
		    if (patternDLongByBar != null)
		    {
		        foreach (var kv in patternDLongByBar.ToList())
		        {
		            int barIndex = kv.Key;
		            // If we ever had a policy to drop old bars, this is where we'd remove stale.
		            // For now, keep it as a safety: if barIndex is out of range, remove.
		            if (barIndex < 0 || barIndex > CurrentBar)
		                RemoveDrawObject($"PATTERND_L_{barIndex}");
		        }
		    }
		
		    // SHORT markers
		    if (patternDShortByBar != null)
		    {
		        foreach (var kv in patternDShortByBar.ToList())
		        {
		            int barIndex = kv.Key;
		            if (barIndex < 0 || barIndex > CurrentBar)
		                RemoveDrawObject($"PATTERND_S_{barIndex}");
		        }
		    }
		
		    // Draw current (or none if disabled)
		    if (EnablePatternDSignal)
		    {
		        if (patternDLongByBar != null)
		        {
		            foreach (var kv in patternDLongByBar)
		                DrawPatternDText(true, kv.Key, kv.Value);
		        }
		
		        if (patternDShortByBar != null)
		        {
		            foreach (var kv in patternDShortByBar)
		                DrawPatternDText(false, kv.Key, kv.Value);
		        }
		    }
		    else
		    {
		        // If signals disabled, remove any existing markers
		        if (patternDLongByBar != null)
		            foreach (var kv in patternDLongByBar)
		                RemoveDrawObject($"PATTERND_L_{kv.Key}");
		
		        if (patternDShortByBar != null)
		            foreach (var kv in patternDShortByBar)
		                RemoveDrawObject($"PATTERND_S_{kv.Key}");
		    }
		
		    // ---- LEVELS (lines) ----
		    // Clear prior drawn levels and redraw current (debug-only).
		    if (patternDLevels != null)
		    {
		        foreach (var lvl in patternDLevels)
		        {
		            if (lvl == null)
		                continue;
		
		            string tagOld = lvl.IsLong
		                ? $"PATTERND_LVL_L_{lvl.OgBarIndex}_{lvl.EntryBarIndex}"
		                : $"PATTERND_LVL_S_{lvl.OgBarIndex}_{lvl.EntryBarIndex}";
		
		            RemoveDrawObject(tagOld);
		        }
		    }
		
		    if (EnablePatternDLevels)
		    {
		        if (patternDLevels != null)
		        {
		            foreach (var lvl in patternDLevels)
		                DrawPatternDLevelLine(lvl);
		        }
		    }
		}
		
		private void DrawPatternDText(bool isLong, int barIndex, double price)
		{
		    if (!CanDrawDebug() || !DrawPatternD)
		        return;
		
		    if (barIndex < 0 || barIndex > CurrentBar)
		        return;
		
		    int barsAgo = CurrentBar - barIndex;
		    if (barsAgo < 0)
		        return;
		
		    string tag  = isLong ? $"PATTERND_L_{barIndex}" : $"PATTERND_S_{barIndex}";
		    string text = isLong ? "▲" : "▼";
		
		    Brush brush = isLong ? PatternDLongColor : PatternDShortColor;
		
		    // Compute y based on the signal bar's extremes + offset ticks
		    double y = price; // fallback
		    double off = PatternDMarkerOffsetTicks * TickSize;
		
		    // barsAgo is in-range here by construction, but keep this safe anyway.
		    if (barsAgo >= 0 && barsAgo <= CurrentBar)
		    {
		        if (isLong)
		            y = Low[barsAgo] - off;     // below bar low
		        else
		            y = High[barsAgo] + off;    // above bar high
		    }
		
		    // Ensure updates replace old text
		    RemoveDrawObject(tag);
		
		    Draw.Text(this, tag, false,
		        text,
		        barsAgo,
		        y,
		        0,
		        brush,
		        new SimpleFont("Arial", PatternDMarkerFontSize),
		        TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}
		
		private void DrawPatternDLevelLine(PatternDLevel lvl)
		{
		    if (!CanDrawDebug() || !DrawPatternD)
		        return;
		
		    if (lvl == null)
		        return;
		
		    // Use both OG and entry to avoid collisions if multiple zones share OGBarIndex (or multiple touches exist later)
		    string tag = lvl.IsLong
		        ? $"PATTERND_LVL_L_{lvl.OgBarIndex}_{lvl.EntryBarIndex}"
		        : $"PATTERND_LVL_S_{lvl.OgBarIndex}_{lvl.EntryBarIndex}";
		
		    Brush brush = lvl.IsLong ? PatternDLongColor : PatternDShortColor;
		
		    RemoveDrawObject(tag);
		
		    DateTime t1 = lvl.OgTime;
		    DateTime t2 = (lvl.EntryTime != default(DateTime)) ? lvl.EntryTime : Times[0][0];
		
		    Draw.Line(this, tag, false,
		        t1, lvl.Price,
		        t2, lvl.Price,
		        brush, PatternDLevelDashStyle, PatternDLevelWidth);
		}

		private double GetPatternDMarkerOffsetPrice(bool isLong)
		{
		    double ticks = PatternDMarkerOffsetTicks * TickSize;
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
//		    if (!ShowZigZagDebug && !DebugDrawPatternD /*&& !DebugDrawPatternA */)
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
		private AlightenLevelMultiTimeframeMATickPatternDV0001[] cacheAlightenLevelMultiTimeframeMATickPatternDV0001;
		public AlightenLevelMultiTimeframeMATickPatternDV0001 AlightenLevelMultiTimeframeMATickPatternDV0001(int barsToProcess, bool enablePatternDSignal, bool enablePatternDLevels, bool drawPatternD, double patternDGapSizeTicks, int patternDMarkerFontSize, int patternDMarkerOffsetTicks, System.Windows.Media.Brush patternDLongColor, System.Windows.Media.Brush patternDShortColor, int patternDLevelWidth, DashStyleHelper patternDLevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return AlightenLevelMultiTimeframeMATickPatternDV0001(Input, barsToProcess, enablePatternDSignal, enablePatternDLevels, drawPatternD, patternDGapSizeTicks, patternDMarkerFontSize, patternDMarkerOffsetTicks, patternDLongColor, patternDShortColor, patternDLevelWidth, patternDLevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public AlightenLevelMultiTimeframeMATickPatternDV0001 AlightenLevelMultiTimeframeMATickPatternDV0001(ISeries<double> input, int barsToProcess, bool enablePatternDSignal, bool enablePatternDLevels, bool drawPatternD, double patternDGapSizeTicks, int patternDMarkerFontSize, int patternDMarkerOffsetTicks, System.Windows.Media.Brush patternDLongColor, System.Windows.Media.Brush patternDShortColor, int patternDLevelWidth, DashStyleHelper patternDLevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			if (cacheAlightenLevelMultiTimeframeMATickPatternDV0001 != null)
				for (int idx = 0; idx < cacheAlightenLevelMultiTimeframeMATickPatternDV0001.Length; idx++)
					if (cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx] != null && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].BarsToProcess == barsToProcess && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].EnablePatternDSignal == enablePatternDSignal && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].EnablePatternDLevels == enablePatternDLevels && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].DrawPatternD == drawPatternD && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].PatternDGapSizeTicks == patternDGapSizeTicks && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].PatternDMarkerFontSize == patternDMarkerFontSize && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].PatternDMarkerOffsetTicks == patternDMarkerOffsetTicks && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].PatternDLongColor == patternDLongColor && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].PatternDShortColor == patternDShortColor && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].PatternDLevelWidth == patternDLevelWidth && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].PatternDLevelDashStyle == patternDLevelDashStyle && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].ShowZigZag == showZigZag && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].ZigZagColor == zigZagColor && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].ZigZagWidth == zigZagWidth && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].ZigZagStyle == zigZagStyle && cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx].EqualsInput(input))
						return cacheAlightenLevelMultiTimeframeMATickPatternDV0001[idx];
			return CacheIndicator<AlightenLevelMultiTimeframeMATickPatternDV0001>(new AlightenLevelMultiTimeframeMATickPatternDV0001(){ BarsToProcess = barsToProcess, EnablePatternDSignal = enablePatternDSignal, EnablePatternDLevels = enablePatternDLevels, DrawPatternD = drawPatternD, PatternDGapSizeTicks = patternDGapSizeTicks, PatternDMarkerFontSize = patternDMarkerFontSize, PatternDMarkerOffsetTicks = patternDMarkerOffsetTicks, PatternDLongColor = patternDLongColor, PatternDShortColor = patternDShortColor, PatternDLevelWidth = patternDLevelWidth, PatternDLevelDashStyle = patternDLevelDashStyle, ShowZigZag = showZigZag, ZigZagColor = zigZagColor, ZigZagWidth = zigZagWidth, ZigZagStyle = zigZagStyle }, input, ref cacheAlightenLevelMultiTimeframeMATickPatternDV0001);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenLevelMultiTimeframeMATickPatternDV0001 AlightenLevelMultiTimeframeMATickPatternDV0001(int barsToProcess, bool enablePatternDSignal, bool enablePatternDLevels, bool drawPatternD, double patternDGapSizeTicks, int patternDMarkerFontSize, int patternDMarkerOffsetTicks, System.Windows.Media.Brush patternDLongColor, System.Windows.Media.Brush patternDShortColor, int patternDLevelWidth, DashStyleHelper patternDLevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternDV0001(Input, barsToProcess, enablePatternDSignal, enablePatternDLevels, drawPatternD, patternDGapSizeTicks, patternDMarkerFontSize, patternDMarkerOffsetTicks, patternDLongColor, patternDShortColor, patternDLevelWidth, patternDLevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenLevelMultiTimeframeMATickPatternDV0001 AlightenLevelMultiTimeframeMATickPatternDV0001(ISeries<double> input , int barsToProcess, bool enablePatternDSignal, bool enablePatternDLevels, bool drawPatternD, double patternDGapSizeTicks, int patternDMarkerFontSize, int patternDMarkerOffsetTicks, System.Windows.Media.Brush patternDLongColor, System.Windows.Media.Brush patternDShortColor, int patternDLevelWidth, DashStyleHelper patternDLevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternDV0001(input, barsToProcess, enablePatternDSignal, enablePatternDLevels, drawPatternD, patternDGapSizeTicks, patternDMarkerFontSize, patternDMarkerOffsetTicks, patternDLongColor, patternDShortColor, patternDLevelWidth, patternDLevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenLevelMultiTimeframeMATickPatternDV0001 AlightenLevelMultiTimeframeMATickPatternDV0001(int barsToProcess, bool enablePatternDSignal, bool enablePatternDLevels, bool drawPatternD, double patternDGapSizeTicks, int patternDMarkerFontSize, int patternDMarkerOffsetTicks, System.Windows.Media.Brush patternDLongColor, System.Windows.Media.Brush patternDShortColor, int patternDLevelWidth, DashStyleHelper patternDLevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternDV0001(Input, barsToProcess, enablePatternDSignal, enablePatternDLevels, drawPatternD, patternDGapSizeTicks, patternDMarkerFontSize, patternDMarkerOffsetTicks, patternDLongColor, patternDShortColor, patternDLevelWidth, patternDLevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenLevelMultiTimeframeMATickPatternDV0001 AlightenLevelMultiTimeframeMATickPatternDV0001(ISeries<double> input , int barsToProcess, bool enablePatternDSignal, bool enablePatternDLevels, bool drawPatternD, double patternDGapSizeTicks, int patternDMarkerFontSize, int patternDMarkerOffsetTicks, System.Windows.Media.Brush patternDLongColor, System.Windows.Media.Brush patternDShortColor, int patternDLevelWidth, DashStyleHelper patternDLevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternDV0001(input, barsToProcess, enablePatternDSignal, enablePatternDLevels, drawPatternD, patternDGapSizeTicks, patternDMarkerFontSize, patternDMarkerOffsetTicks, patternDLongColor, patternDShortColor, patternDLevelWidth, patternDLevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

#endregion

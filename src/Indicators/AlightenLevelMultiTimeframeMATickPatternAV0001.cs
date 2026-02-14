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
    public class AlightenLevelMultiTimeframeMATickPatternAV0001 : Indicator
    {
		
		#region Class Variables

        // HTF pivots (BIP 0 only)
        private List<DateTime> pivotTimes;
        private List<double>   pivotPrices;
        private List<bool>     pivotIsHigh;
        private List<double>   pivotGuidePrices;

        // Important levels derived from pivots (BIP 0 only)
        private List<double> importantLevels;

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
		
		// Pattern A results (MA-friendly persistent state)
		// barIndex is primary-series (BIP 0) absolute bar index (0..CurrentBar)
		private Dictionary<int, double> patternALongByBar;
		private Dictionary<int, double> patternAShortByBar;
		
		// Pattern A derived levels (logical objects; optionally drawn in debug)
		private List<PatternALevel> patternALevels;
		
		// Pattern A structure tracking (triplets/pivots). This mirrors the V00029 concept.
		// These are used inside ScanPatternA() to detect OG/support/regional relationships.
		private List<PatternATriplet> patternATriplets;
		private List<PatternATriplet> patternATripletsPotential; // potential (structural-only)

		
		// Represents a single Pattern A triple (OG, Support, TrendStart)
		// Represents a single Pattern A triple (Pivot1 level, Pivot2, Pivot3-cross)
		private class PatternATriplet
		{
		    // ---- Pivot structure ----
		    // OgIdx         = Pivot1 (start pivot) -> defines Pattern A level (guide price)
		    // SupportIdx    = Pivot2 (pre-cross pivot)
		    // TrendStartIdx = Pivot3 (crossing pivot)
		    public int    OgIdx;              // Pivot1
		    public int    SupportIdx;         // Pivot2
		    public int    TrendStartIdx;      // Pivot3
		
		    public bool   IsLong;
		    public bool   IsActive;
		
		    // ---- Prices ----
		    public double OgPrice;            // level = pivotGuidePrices[OgIdx]
		    public double SupportPrice;       // pivotGuidePrices[SupportIdx]
		    public double TrendStartPrice;    // pivotPrices[SupportIdx] (Pivot2 extreme: low for long, high for short)
		
		    // ---- Crossing state ----
		    public bool     HasCrossed;
		    public int      CrossCount;       // must be 1
		    public DateTime CrossTime;        // pivotTimes[Pivot3]
		
		    public PatternATriplet()
		    {
		        IsActive  = true;
		        CrossTime = Core.Globals.MinDate;
		    }
		}


		
		// Represents a horizontal Pattern A level to draw (at OG price), ending at the wick/entry pivot
		private class PatternALevel
		{
		    public bool     IsLong;
		    public int      OgBarIndex;
		    public DateTime OgTime;
		
		    public int      EntryBarIndex;
		    public DateTime EntryTime;
		
		    public double   Price; // OG level (guide)
		
		    public PatternALevel() { }
		}
		
		private PatternATriplet activePatternALong;
		private PatternATriplet activePatternAShort;

		private Dictionary<string, int> lastSignalBip0BarByKey;
		
		private string MakeTripletKey(PatternATriplet t)
		{
		    if (t == null) return string.Empty;
		    return (t.IsLong ? "L" : "S") + "|" + t.OgIdx + "|" + t.SupportIdx + "|" + t.TrendStartIdx;
		}
		
		// ---- LIVE (intrabar) draw tags for Pattern A ----
		private const string PA_LIVE_TAG_L = "PATTERNA_LIVE_TAG_L";
		private const string PA_LIVE_TAG_S = "PATTERNA_LIVE_TAG_S";
		private const string PA_LIVE_LVL_L = "PATTERNA_LIVE_LVL_L";
		private const string PA_LIVE_LVL_S = "PATTERNA_LIVE_LVL_S";
		
		// --- Pattern A live recency (BIP0) state ---
		private bool paLivePrevLong  = false;
		private bool paLivePrevShort = false;
		
		private int  paLiveLongLastEdgeSeq  = -1;   // last tick-seq where long became true (false->true)
		private int  paLiveShortLastEdgeSeq = -1;   // last tick-seq where short became true (false->true)
		private int  paLiveTickSeq          = 0;    // increments each BIP0 call within a bar
		
		private int  paLiveLastChosenDir    = 0;    // +1 long, -1 short, 0 none (used as a stability tie-break)


	
        #endregion
		
        #region Properties
		

        [NinjaScriptProperty]
		[Range(0, 500000)]
		[Display(Name="Bars To Process", GroupName="ZigZag", Order=53)]
		public int BarsToProcess { get; set; } = 200; // 0 = process all
		
		// ---- Pattern A toggles ----

		[NinjaScriptProperty]
		[Display(Name="Enable Pattern A Signals", GroupName="Pattern A", Order=1)]
		public bool EnablePatternASignal { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Enable Pattern A Levels", GroupName="Pattern A", Order=2)]
		public bool EnablePatternALevels { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name = "Draw Pattern A", GroupName = "Pattern A", Order = 3)]
		public bool DrawPatternA { get; set; } = true;
	
		
		// Optional styling knobs (safe defaults)
		[NinjaScriptProperty]
		[Range(6, 30)]
		[Display(Name="Pattern A Marker Font Size", GroupName="Pattern A", Order=20)]
		public int PatternAMarkerFontSize { get; set; } = 18;
		
		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name="Pattern A Marker Offset Ticks", GroupName="Pattern A", Order=21)]
		public int PatternAMarkerOffsetTicks { get; set; } = 5;
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern A Long Color", GroupName="Pattern A", Order=22)]
		public System.Windows.Media.Brush PatternALongColor { get; set; } = Brushes.Lime;
		
		[Browsable(false)]
		public string PatternALongColorSerialize
		{
		    get => Serialize.BrushToString(PatternALongColor);
		    set => PatternALongColor = Serialize.StringToBrush(value);
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern A Short Color", GroupName="Pattern A", Order=23)]
		public System.Windows.Media.Brush PatternAShortColor { get; set; } = Brushes.Magenta;
		
		[Browsable(false)]
		public string PatternAShortColorSerialize
		{
		    get => Serialize.BrushToString(PatternAShortColor);
		    set => PatternAShortColor = Serialize.StringToBrush(value);
		}
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Pattern A Level Width", GroupName="Pattern A", Order=30)]
		public int PatternALevelWidth { get; set; } = 2;
		
		[NinjaScriptProperty]
		[Display(Name="Pattern A Level Dash", GroupName="Pattern A", Order=31)]
		public DashStyleHelper PatternALevelDashStyle { get; set; } = DashStyleHelper.Dash;


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
		
		
		
		private Series<double> patternASignal;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternASignal
		{
		    get { return patternASignal; }
		}
		
		private Series<double> patternASignalMA;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternASignalMA
		{
		    get { return patternASignalMA; }
		}
		
		#endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AlightenLevelMultiTimeframeMATickPatternAV0001";
                Description = "Market Analyzer core: HTF ZigZag/levels on primary series with 1m/5m proximity columns.";
                Calculate   = Calculate.OnEachTick;

                // For MA, overlay doesn’t matter. For chart debugging, overlay helps.
                IsOverlay               		= true;
                DrawOnPricePanel        		= true;

                DisplayInDataBox        		= true;
                PaintPriceMarkers       		= false;
                IsSuspendedWhileInactive 		= false;
				ShowTransparentPlotsInDataBox 	= true;

               
				AddPlot(Brushes.Transparent, "PatternASignal");
				AddPlot(Brushes.Transparent, "PatternASignalMA");
                
            }
            else if (State == State.Configure)
            {
                // BIP 0 is the Market Analyzer interval (e.g., 60m / 240m / Daily)
//                AddDataSeries(BarsPeriodType.Minute, 1); // BIP 1

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
				
				patternALongByBar  				= new Dictionary<int, double>(256);
				patternAShortByBar 				= new Dictionary<int, double>(256);
				patternALevels     				= new List<PatternALevel>(128);
				patternATriplets   				= new List<PatternATriplet>(128);
				patternATripletsPotential   	= new List<PatternATriplet>(128);
				//patternACandidates 				= new List<PatternACandidate>(128);
				
				lastSignalBip0BarByKey = new Dictionary<string, int>(512, StringComparer.Ordinal);

				patternASignal					= Values[0];
				patternASignalMA				= Values[1];

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
		    try
		    {
		        // ---- BIP 0 only (this indicator is single-series right now) ----
		        if (BarsInProgress != 0)
		            return;
		
		        if (patternASignal == null || patternASignalMA == null)
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
		            patternASignal[0]   = 0;
		            patternASignalMA[0] = 0;
		
		            if (CanDrawDebug() && DrawPatternA)
		            {
		                RemoveDrawObject(PA_LIVE_TAG_L);
		                RemoveDrawObject(PA_LIVE_TAG_S);
		                RemoveDrawObject(PA_LIVE_LVL_L);
		                RemoveDrawObject(PA_LIVE_LVL_S);
		            }
		            return;
		        }
		
		        // -----------------------------
		        // 1) BAR-CLOSE semantics (keep existing behavior)
		        //    Only do the heavy rebuild on first tick of bar.
		        // -----------------------------
		        if (allowHeavy)
		        {
		            if (CurrentBar < 3)
		                return;
		
		            ProcessClosedBarPivot_BIP0();  // uses [1]
		            RebuildPatternA_BIP0();
		
		            if (EnablePatternASignal)
		            {
		                // Default clear (current bar)
		                patternASignal[0]   = 0;
		                patternASignalMA[0] = 0;
		
		                if (CurrentBars[0] >= 1)
		                {
		                    int sigBar = CurrentBars[0] - 1;
		
		                    bool longNow  = patternALongByBar  != null && patternALongByBar.ContainsKey(sigBar);
		                    bool shortNow = patternAShortByBar != null && patternAShortByBar.ContainsKey(sigBar);
		
		                    double sig = 0;
		                    if (longNow && !shortNow) sig = +1;
		                    else if (shortNow && !longNow) sig = -1;
		
		                    // DataBox / chart-aligned plot (belongs to bar that just closed)
		                    patternASignal[1] = sig;
		
		                    // MA-friendly plot (belongs to current bar so MA reads [0] reliably)
		                    patternASignalMA[0] = sig;
		                }
		            }
		        }
		
		        // -----------------------------
		        // 2) LIVE intrabar semantics (new)
		        //    Update on EVERY tick so it can appear/disappear while bar forms.
		        // -----------------------------
		        if (EnablePatternASignal)
		        {
		            UpdatePatternA_Live_BIP0();
		        }
		    }
		    catch (Exception ex)
		    {
		        Print(string.Format("[PA EX][OnBarUpdate] t={0:yyyy-MM-dd HH:mm:ss} BIP={1} CB0={2} CurrentBar={3} Msg={4}\nStack={5}",
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
		
		#region Pattern A
		
		private void UpdatePatternA_Live_BIP0()
		{
		    // Per-bar reset for recency tracking (BIP0)
		    if (IsFirstTickOfBar)
		    {
		        paLivePrevLong           = false;
		        paLivePrevShort          = false;
		        paLiveLongLastEdgeSeq    = -1;
		        paLiveShortLastEdgeSeq   = -1;
		        paLiveTickSeq            = 0;
		        paLiveLastChosenDir      = 0;
		    }
		    else
		    {
		        // advance intra-bar tick sequence
		        paLiveTickSeq++;
		    }
		
		    // Safety
		    if (CurrentBar < 1)
		    {
		        patternASignal[0] = 0;
		
		        // Do NOT publish MA intrabar (by design)
		        // patternASignalMA[0] = 0;
		
		        // Reset edge state to avoid carrying across early exits
		        paLivePrevLong  = false;
		        paLivePrevShort = false;
		
		        if (CanDrawDebug() && DrawPatternA)
		        {
		            RemoveDrawObject(PA_LIVE_TAG_L);
		            RemoveDrawObject(PA_LIVE_TAG_S);
		            RemoveDrawObject(PA_LIVE_LVL_L);
		            RemoveDrawObject(PA_LIVE_LVL_S);
		        }
		        return;
		    }
		
		    // If potentials haven't been built yet, just clear live.
		    if (patternATripletsPotential == null || patternATripletsPotential.Count == 0)
		    {
		        patternASignal[0] = 0;
		
		        // Reset edge state to avoid carrying across early exits
		        paLivePrevLong  = false;
		        paLivePrevShort = false;
		
		        if (CanDrawDebug() && DrawPatternA)
		        {
		            RemoveDrawObject(PA_LIVE_TAG_L);
		            RemoveDrawObject(PA_LIVE_TAG_S);
		            RemoveDrawObject(PA_LIVE_LVL_L);
		            RemoveDrawObject(PA_LIVE_LVL_S);
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
		
		    // Prev closed bar for LL/HH constraint (Pattern A logic)
		    double prevLow  = Low[1];
		    double prevHigh = High[1];
		
		    bool liveLong  = false;
		    bool liveShort = false;
		
		    double bestLongDistTicks  = double.MaxValue;
		    double bestShortDistTicks = double.MaxValue;
		
		    PatternATriplet bestLongTrip  = null;
		    PatternATriplet bestShortTrip = null;
		
		    // Choose the "best" live candidate per side by nearest OG distance to current price
		    foreach (var t in patternATripletsPotential)
		    {
		        if (t == null)
		            continue;
		
		        double level = t.OgPrice;
		
		        // Optional safety: ignore if already invalidated by a 2nd confirmed cross
		        int lastConfirmed = GetLastConfirmedPivotIdxBip0();
		        if (lastConfirmed >= 0)
		        {
		            int nextCrossPivotIdx = FindNextPivotCrossIdx(t.TrendStartIdx, level, eps);
		            if (nextCrossPivotIdx >= 0 && nextCrossPivotIdx <= lastConfirmed)
		                continue;
		        }
		
		        if (t.IsLong)
		        {
		            // LONG: wick touches OG + body strictly above OG + lower low than previous bar
		            bool wickTouches = l0 <= level + eps;
		            bool bodyAbove   = bodyLow > level + eps;
		            bool lowerLow    = l0 < prevLow - eps;
		
		            if (wickTouches && bodyAbove && lowerLow)
		            {
		                double distTicks = Math.Abs(c0 - level) / TickSize;
		                if (distTicks < bestLongDistTicks)
		                {
		                    bestLongDistTicks = distTicks;
		                    bestLongTrip      = t;
		                    liveLong          = true;
		                }
		            }
		        }
		        else
		        {
		            // SHORT: wick touches OG + body strictly below OG + higher high than previous bar
		            bool wickTouches = h0 >= level - eps;
		            bool bodyBelow   = bodyHigh < level - eps;
		            bool higherHigh  = h0 > prevHigh + eps;
		
		            if (wickTouches && bodyBelow && higherHigh)
		            {
		                double distTicks = Math.Abs(c0 - level) / TickSize;
		                if (distTicks < bestShortDistTicks)
		                {
		                    bestShortDistTicks = distTicks;
		                    bestShortTrip      = t;
		                    liveShort          = true;
		                }
		            }
		        }
		    }
		
		    // --- Recency tracking: capture false->true edges for each side on this bar ---
		    if (liveLong && !paLivePrevLong)
		        paLiveLongLastEdgeSeq = paLiveTickSeq;
		
		    if (liveShort && !paLivePrevShort)
		        paLiveShortLastEdgeSeq = paLiveTickSeq;
		
		    // Update prev flags after edge detection
		    paLivePrevLong  = liveLong;
		    paLivePrevShort = liveShort;
		
		    // --- Choose liveSig for the plot (single scalar), but DRAW both sides when both are true ---
		    double liveSig = 0;
		
		    if (liveLong && !liveShort)
		    {
		        liveSig = +1;
		        paLiveLastChosenDir = +1;
		    }
		    else if (liveShort && !liveLong)
		    {
		        liveSig = -1;
		        paLiveLastChosenDir = -1;
		    }
		    else if (liveLong && liveShort)
		    {
		        // Recency wins: later edge seq wins
		        if (paLiveLongLastEdgeSeq > paLiveShortLastEdgeSeq)
		        {
		            liveSig = +1;
		            paLiveLastChosenDir = +1;
		        }
		        else if (paLiveShortLastEdgeSeq > paLiveLongLastEdgeSeq)
		        {
		            liveSig = -1;
		            paLiveLastChosenDir = -1;
		        }
		        else
		        {
		            // Tie: both became true same tick, or both were already true.
		            // Tie-break by penetration/excursion beyond OG on THIS tick.
		            double longPen  = 0;
		            double shortPen = 0;
		
		            if (bestLongTrip != null)
		            {
		                double lvl = bestLongTrip.OgPrice;
		                longPen = (lvl - l0) / TickSize;   // how far price wicked below OG (>= 0 when touched)
		            }
		            if (bestShortTrip != null)
		            {
		                double lvl = bestShortTrip.OgPrice;
		                shortPen = (h0 - lvl) / TickSize;  // how far price wicked above OG (>= 0 when touched)
		            }
		
		            if (longPen > shortPen + 0.0001)
		            {
		                liveSig = +1;
		                paLiveLastChosenDir = +1;
		            }
		            else if (shortPen > longPen + 0.0001)
		            {
		                liveSig = -1;
		                paLiveLastChosenDir = -1;
		            }
		            else
		            {
		                // Still tied: stabilize (keep last chosen if it’s still valid), else prefer nearer-to-close
		                if (paLiveLastChosenDir == +1)
		                    liveSig = +1;
		                else if (paLiveLastChosenDir == -1)
		                    liveSig = -1;
		                else
		                {
		                    // last resort: pick whichever is closer to close
		                    if (bestLongDistTicks < bestShortDistTicks)
		                    {
		                        liveSig = +1;
		                        paLiveLastChosenDir = +1;
		                    }
		                    else if (bestShortDistTicks < bestLongDistTicks)
		                    {
		                        liveSig = -1;
		                        paLiveLastChosenDir = -1;
		                    }
		                    else
		                    {
		                        liveSig = 0;
		                        paLiveLastChosenDir = 0;
		                    }
		                }
		            }
		        }
		    }
		    else
		    {
		        liveSig = 0;
		        paLiveLastChosenDir = 0;
		    }
		
		    // LIVE plot (chart):
		    patternASignal[0] = liveSig;
		
		    // DO NOT publish MA intrabar (by design)
		    // patternASignalMA[0] = liveSig;
		
		    // --- Draw live artifacts ---
		    if (CanDrawDebug() && DrawPatternA)
		    {
		        // Clear all live artifacts first (prevents stale drawings if signal flips intrabar)
		        RemoveDrawObject(PA_LIVE_TAG_L);
		        RemoveDrawObject(PA_LIVE_TAG_S);
		        RemoveDrawObject(PA_LIVE_LVL_L);
		        RemoveDrawObject(PA_LIVE_LVL_S);
		
		        // Draw LONG live if valid (even if SHORT also valid)
		        if (liveLong && bestLongTrip != null)
		        {
		            double level = bestLongTrip.OgPrice;
		
		            double y = l0 - (PatternAMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PA_LIVE_TAG_L, false,
		                "▲",
		                0,
		                y,
		                0,
		                PatternALongColor,
		                new SimpleFont("Arial", PatternAMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);
		
		            int ogBarIndex = (pivotTimes != null && bestLongTrip.OgIdx >= 0 && bestLongTrip.OgIdx < pivotTimes.Count)
		                ? BarsArray[0].GetBar(pivotTimes[bestLongTrip.OgIdx])
		                : -1;
		
		            if (ogBarIndex >= 0)
		            {
		                int ogBarsAgo = CurrentBars[0] - ogBarIndex;
		                if (ogBarsAgo >= 0)
		                {
		                    Draw.Line(this, PA_LIVE_LVL_L, false,
		                        ogBarsAgo, level,
		                        0,        level,
		                        PatternALongColor, PatternALevelDashStyle, PatternALevelWidth);
		                }
		            }
		        }
		
		        // Draw SHORT live if valid (even if LONG also valid)
		        if (liveShort && bestShortTrip != null)
		        {
		            double level = bestShortTrip.OgPrice;
		
		            double y = h0 + (PatternAMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PA_LIVE_TAG_S, false,
		                "▼",
		                0,
		                y,
		                0,
		                PatternAShortColor,
		                new SimpleFont("Arial", PatternAMarkerFontSize),
		                TextAlignment.Center,
		                Brushes.Transparent,
		                Brushes.Transparent,
		                0);
		
		            int ogBarIndex = (pivotTimes != null && bestShortTrip.OgIdx >= 0 && bestShortTrip.OgIdx < pivotTimes.Count)
		                ? BarsArray[0].GetBar(pivotTimes[bestShortTrip.OgIdx])
		                : -1;
		
		            if (ogBarIndex >= 0)
		            {
		                int ogBarsAgo = CurrentBars[0] - ogBarIndex;
		                if (ogBarsAgo >= 0)
		                {
		                    Draw.Line(this, PA_LIVE_LVL_S, false,
		                        ogBarsAgo, level,
		                        0,        level,
		                        PatternAShortColor, PatternALevelDashStyle, PatternALevelWidth);
		                }
		            }
		        }
		    }
		}


		
		private void RebuildPatternA_BIP0()
		{
		    if (!EnablePatternASignal && !EnablePatternALevels)
		        return;
		
		    BuildPatternA(out var newPBLong, out var newPBShort, out var newPBLevels);
		
		    ApplyPatternAResults(newPBLong, newPBShort, newPBLevels);
			
			//RebuildPatternACandidates_BIP0();
		
		    // chart-only
		    if (DrawPatternA && CanDrawDebug())
		        DrawPatternADebug(newPBLong, newPBShort, newPBLevels);
		}
	
		private void BuildPatternA(
		    out Dictionary<int, double> newLongByBar,
		    out Dictionary<int, double> newShortByBar,
		    out List<PatternALevel> newLevels)
		{
		    newLongByBar  = new Dictionary<int, double>();
		    newShortByBar = new Dictionary<int, double>();
		    newLevels     = new List<PatternALevel>();
		
		    if ((!EnablePatternASignal && !EnablePatternALevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 3)
		        return;
		
		    ScanPatternA(newLongByBar, newShortByBar, newLevels);
		}

		
		private void ApplyPatternAResults(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternALevel> newLevels)
		{
		    if (patternALongByBar == null)
		        patternALongByBar = new Dictionary<int, double>();
		    if (patternAShortByBar == null)
		        patternAShortByBar = new Dictionary<int, double>();
		    if (patternALevels == null)
		        patternALevels = new List<PatternALevel>();
		
		    // Signals
		    if (EnablePatternASignal)
		    {
		        patternALongByBar.Clear();
		        foreach (var kv in newLongByBar)
		            patternALongByBar[kv.Key] = kv.Value;
		
		        patternAShortByBar.Clear();
		        foreach (var kv in newShortByBar)
		            patternAShortByBar[kv.Key] = kv.Value;
		    }
		    else
		    {
		        patternALongByBar.Clear();
		        patternAShortByBar.Clear();
		    }
		
		    // Levels
		    if (EnablePatternALevels)
		    {
		        patternALevels.Clear();
		        patternALevels.AddRange(newLevels);
		    }
		    else
		    {
		        patternALevels.Clear();
		    }
		}
		
		
		// Drop-in replacement for ScanPatternA (assumes you've added the helper methods:
		// SideOfLevel, SegmentCrossesLevel, CountPivotCrossesOverLevel, FindNextPivotCrossIdx)
		private void ScanPatternA(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternALevel> newLevels)
		{
		    patternATriplets.Clear();
		    patternATripletsPotential.Clear();
		
		    if ((!EnablePatternASignal && !EnablePatternALevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 3)
		        return;
		
		    int pivotCount = pivotTimes.Count;
		    double eps = TickSize * 0.5;
		
		    // Map pivot index -> primary (BIP0) bar index once, so we can do fast bar scans
		    int[] pivotBarIndex = new int[pivotCount];
		    for (int i = 0; i < pivotCount; i++)
		        pivotBarIndex[i] = BarsArray[0].GetBar(pivotTimes[i]);
			
		
		    bool TryAddLongSignals(PatternATriplet t, int crossBarIndex)
		    {
		        if (crossBarIndex < 0)
		            return false;
		
		        double level = t.OgPrice;
		        int ogBarIndex = pivotBarIndex[t.OgIdx];
		        if (ogBarIndex < 0)
		            return false;
		
		        bool anySignal  = false;
		        bool levelAdded = false;
		
		        int start = Math.Max(crossBarIndex, 1); // need prev bar for the lower-low check
		
		        // Stop scanning once ZigZag crosses the level again (2nd cross invalidates)
		        int end = CurrentBars[0];
		        int nextCrossPivotIdx = FindNextPivotCrossIdx(t.TrendStartIdx, level, eps);
				
				if (nextCrossPivotIdx >= 1)
				{
				    int a = nextCrossPivotIdx - 1;
				    int b = nextCrossPivotIdx;
				
				}
		        if (nextCrossPivotIdx >= 0)
		        {
		            int nextCrossBarIndex = pivotBarIndex[nextCrossPivotIdx];
		            if (nextCrossBarIndex >= 0)
						end = Math.Min(end, nextCrossBarIndex);
		                //end = Math.Min(end, nextCrossBarIndex - 1);
		        }
		
		        if (start > end)
		            return false;
				
				
		        for (int barIndex = start; barIndex <= end; barIndex++)
		        {
		            int barsAgo     = CurrentBars[0] - barIndex;
		            int prevBarsAgo = barsAgo + 1;
		            if (prevBarsAgo > CurrentBars[0])
		                continue;
		
		            double lo = Lows[0][barsAgo];
		            double o  = Opens[0][barsAgo];
		            double c  = Closes[0][barsAgo];
		
		            double bodyLow = Math.Min(o, c);
		
		            bool wickTouches = lo <= level + eps;
		            bool bodyAbove   = bodyLow > level + eps;             // strict above
		            bool lowerLow    = lo < Lows[0][prevBarsAgo] - eps;   // lower low than previous bar
		
		            if (wickTouches && bodyAbove && lowerLow)
		            {
		                anySignal = true;
						
						string key = MakeTripletKey(t);
						if (!string.IsNullOrEmpty(key))
						    lastSignalBip0BarByKey[key] = barIndex;
		
		                if (EnablePatternASignal)
		                    newLongByBar[barIndex] = lo - (PatternAMarkerOffsetTicks * TickSize);
		
		                if (EnablePatternALevels && !levelAdded)
		                {
		                    levelAdded = true;
		                    newLevels.Add(new PatternALevel
		                    {
		                        IsLong        = true,
		                        OgBarIndex    = ogBarIndex,
		                        OgTime        = pivotTimes[t.OgIdx],
		                        EntryBarIndex = barIndex,
		                        EntryTime     = Times[0][barsAgo],
		                        Price         = level
		                    });
		                }
		            }
		        }
		
		        return anySignal;
		    }
		
		    bool TryAddShortSignals(PatternATriplet t, int crossBarIndex)
		    {
		        if (crossBarIndex < 0)
		            return false;
		
		        double level = t.OgPrice;
		        int ogBarIndex = pivotBarIndex[t.OgIdx];
		        if (ogBarIndex < 0)
		            return false;
		
		        bool anySignal  = false;
		        bool levelAdded = false;
		
		        int start = Math.Max(crossBarIndex, 1); // need prev bar for higher-high check
		
		        // Stop scanning once ZigZag crosses the level again (2nd cross invalidates)
		        int end = CurrentBars[0];
		        int nextCrossPivotIdx = FindNextPivotCrossIdx(t.TrendStartIdx, level, eps);
		        if (nextCrossPivotIdx >= 0)
		        {
		            int nextCrossBarIndex = pivotBarIndex[nextCrossPivotIdx];
		            if (nextCrossBarIndex >= 0)
		                end = Math.Min(end, nextCrossBarIndex);
		        }
		
		        if (start > end)
		            return false;		
		
		        for (int barIndex = start; barIndex <= end; barIndex++)
		        {
		            int barsAgo     = CurrentBars[0] - barIndex;
		            int prevBarsAgo = barsAgo + 1;
		            if (prevBarsAgo > CurrentBars[0])
		                continue;
		
		            double hi = Highs[0][barsAgo];
		            double o  = Opens[0][barsAgo];
		            double c  = Closes[0][barsAgo];
		
		            double bodyHigh = Math.Max(o, c);
		
		            bool wickTouches = hi >= level - eps;
		            bool bodyBelow   = bodyHigh < level - eps;            // strict below
		            bool higherHigh  = hi > Highs[0][prevBarsAgo] + eps;  // higher high than previous bar
					
		            if (wickTouches && bodyBelow && higherHigh)
		            {
		                anySignal = true;
						
						string key = MakeTripletKey(t);
						if (!string.IsNullOrEmpty(key))
						    lastSignalBip0BarByKey[key] = barIndex;
		
		                if (EnablePatternASignal)
		                    newShortByBar[barIndex] = hi + (PatternAMarkerOffsetTicks * TickSize);
		
		                if (EnablePatternALevels && !levelAdded)
		                {
		                    levelAdded = true;
		                    newLevels.Add(new PatternALevel
		                    {
		                        IsLong        = false,
		                        OgBarIndex    = ogBarIndex,
		                        OgTime        = pivotTimes[t.OgIdx],
		                        EntryBarIndex = barIndex,
		                        EntryTime     = Times[0][barsAgo],
		                        Price         = level
		                    });
		                }
		            }
		        }
		
		        return anySignal;
		    }
		
		    // =========================
		    // Pattern A structural scan
		    // =========================
		    // LONG arming:
		    //   Pivot1 = HIGH (defines level = guide price at Pivot1)
		    //   Pivot2 = LOW below level
		    //   Pivot3 = HIGH above level (Pivot2->Pivot3 crosses level)
		    //
		    // Additional requirement:
		    //   Excluding Pivot1, ZigZag must cross the level EXACTLY ONCE up through Pivot3.
		    //
		    // SHORT is inverse.
		    //
		    // Pivot2->Pivot3 adjacency enforced (idx3 = idx2 + 1).
		    for (int idx1 = lookbackStartPivotIdx; idx1 < pivotCount - 2; idx1++)
		    {
		        bool isLongStart = pivotIsHigh[idx1];
				double level     = pivotGuidePrices[idx1];
				
				for (int idx2 = idx1 + 1; idx2 < pivotCount - 1; idx2++)
				{
				    // Pivot2 must be opposite polarity of Pivot1
				    if (isLongStart)
				    {
				        if (pivotIsHigh[idx2])
				            continue; // LONG: Pivot2 must be LOW
				
				        double p2 = pivotPrices[idx2];
				        if (p2 >= level - eps)
				            continue; // must be below level
				    }
				    else
				    {
				        if (!pivotIsHigh[idx2])
				            continue; // SHORT: Pivot2 must be HIGH
				
				        double p2 = pivotPrices[idx2];
				        if (p2 <= level + eps)
				            continue; // must be above level
				    }
				
				    int idx3 = idx2 + 1;
				
				    // Pivot3 must return to Pivot1 polarity
				    if (isLongStart)
				    {
				        if (!pivotIsHigh[idx3])
				            continue; // LONG: Pivot3 must be HIGH
				
				        double p3 = pivotPrices[idx3];
				        if (p3 <= level + eps)
				            continue; // must be above to ensure the segment straddles
				    }
				    else
				    {
				        if (pivotIsHigh[idx3])
				            continue; // SHORT: Pivot3 must be LOW
				
				        double p3 = pivotPrices[idx3];
				        if (p3 >= level - eps)
				            continue; // must be below to ensure the segment straddles
				    }
				
				    // Excluding Pivot1: total crosses from (idx1+1 .. idx3) must be exactly 1
				    int crossCount = CountPivotCrossesOverLevel(idx1 + 1, idx3, level, eps);
				    if (crossCount != 1)
				        continue;
				
				    // NEW (fixed + symmetric): after the arming cross at idx3, there must be NO additional crosses
				    // of the level, using confirmed pivots in realtime and strict crossing to avoid "touch" false positives.
				    int postStart = idx3 + 1;
				    int postEnd   = pivotCount - 1;
				
				    if (postStart <= postEnd)
				    {
				        int postCrossCount = CountPivotCrossesOverLevelConfirmedStrict(postStart, postEnd, level, eps);
				        if (postCrossCount != 0)
				        {           
				            continue;
				        }
				    }
				
				    var trip = new PatternATriplet
				    {
				        IsLong          = isLongStart,
				        IsActive        = true,
				        OgIdx           = idx1,
				        SupportIdx      = idx2,
				        TrendStartIdx   = idx3,
				        OgPrice         = level,
				        SupportPrice    = pivotGuidePrices[idx2],
				        TrendStartPrice = pivotPrices[idx2], // LONG: low at Pivot2; SHORT: high at Pivot2
				        HasCrossed      = true,
				        CrossCount      = crossCount,         // == 1
				        CrossTime       = pivotTimes[idx3]
				    };
				
				    patternATripletsPotential.Add(trip);
				
				    int crossBarIndex = pivotBarIndex[idx3];
				    if (isLongStart)
				    {
				        if (TryAddLongSignals(trip, crossBarIndex))
				            patternATriplets.Add(trip);
				    }
				    else
				    {
				        if (TryAddShortSignals(trip, crossBarIndex))
				            patternATriplets.Add(trip);
				    }
				}

		    }
		}
		
		
		private int SideOfLevel(double price, double level, double eps)
		{
		    if (price > level + eps) return  1;
		    if (price < level - eps) return -1;
		    return 0; // near/at level
		}
		
		private bool SegmentCrossesLevel(double a, double b, double level, double eps)
		{
		    int sa = SideOfLevel(a, level, eps);
		    int sb = SideOfLevel(b, level, eps);
		
		    // We only count a "pass over" as a true straddle (above<->below).
		    return (sa != 0 && sb != 0 && sa != sb);
		}
		
		// Counts straddle-crossings of the ZigZag polyline between consecutive pivots.
		// startPivotIdx/endPivotIdx are pivot indices; this examines segments [i-1 -> i] for i in (start+1..end).
		private int CountPivotCrossesOverLevel(int startPivotIdx, int endPivotIdx, double level, double eps)
		{
		    int count = 0;
		    startPivotIdx = Math.Max(0, startPivotIdx);
		    endPivotIdx   = Math.Min(pivotPrices.Count - 1, endPivotIdx);
		
		    for (int i = startPivotIdx + 1; i <= endPivotIdx; i++)
		    {
		        if (SegmentCrossesLevel(pivotPrices[i - 1], pivotPrices[i], level, eps))
		            count++;
		    }
		    return count;
		}
		
		// Returns the last pivot index whose pivotBarIndex is <= last closed BIP0 bar.
		// This prevents realtime from using "still forming" pivots to invalidate setups.
		private int GetLastConfirmedPivotIdxBip0()
		{
		    if (pivotTimes == null || pivotTimes.Count == 0)
		        return -1;
		
		    // Time of last closed bar on BIP0
		    DateTime lastClosedTime;
		    if (State == State.Realtime)
		    {
		        // barsAgo = 1 is the most recently closed bar
		        if (CurrentBars[0] < 1)
		            return -1;
		
		        lastClosedTime = Times[0][1];
		    }
		    else
		    {
		        // Historical: the current bar is treated as closed
		        lastClosedTime = Times[0][0];
		    }
		
		    // Find the latest pivot whose time is <= last closed bar time
		    for (int i = pivotTimes.Count - 1; i >= 0; i--)
		    {
		        if (pivotTimes[i] <= lastClosedTime)
		            return i;
		    }
		
		    return -1;
		}
		
		
		// Strict crossing: endpoints must be on opposite sides and not "touching" the level.
		private bool SegmentCrossesLevelStrict(double a, double b, double level, double eps)
		{
		    double da = a - level;
		    double db = b - level;
		
		    if (Math.Abs(da) <= eps || Math.Abs(db) <= eps)
		        return false;
		
		    return (da > 0 && db < 0) || (da < 0 && db > 0);
		}
		
		// Count strict segment crossings over the level between pivot indices [startIdx .. endIdx],
		// clamping to last confirmed pivots in realtime.
		private int CountPivotCrossesOverLevelConfirmedStrict(int startIdx, int endIdx, double level, double eps)
		{
		    if (pivotTimes == null || pivotTimes.Count < 2)
		        return 0;
		
		    int lastConfirmed = GetLastConfirmedPivotIdxBip0();
		    if (lastConfirmed < 1)
		        return 0;
		
		    // Clamp to confirmed pivot range
		    endIdx = Math.Min(endIdx, lastConfirmed);
		    startIdx = Math.Max(startIdx, 0);
		
		    // Need at least one segment (i -> i+1)
		    if (startIdx >= endIdx)
		        return 0;
		
		    int count = 0;
		
		    // IMPORTANT: use the same "line" you intend for cross semantics.
		    // Your OG levels are based on guide prices, and your earlier logic expects "trendline" behavior,
		    // so we use pivotGuidePrices for the segment endpoints.
		    for (int i = startIdx; i < endIdx; i++)
		    {
		        double a = pivotGuidePrices[i];
		        double b = pivotGuidePrices[i + 1];
		
		        if (SegmentCrossesLevelStrict(a, b, level, eps))
		            count++;
		    }
		
		    return count;
		}

		// Finds the next pivot index > fromPivotIdx where the segment [i-1 -> i] straddles the level.
		private int FindNextPivotCrossIdx(int fromPivotIdx, double level, double eps)
		{
		    int n = pivotPrices.Count;
		    for (int i = Math.Max(1, fromPivotIdx + 1); i < n; i++)
		    {
		        if (SegmentCrossesLevel(pivotPrices[i - 1], pivotPrices[i], level, eps))
		            return i;
		    }
		    return -1;
		}

		// Converts an absolute DateTime to a barsAgo on BIP0 (BarsArray[0]).
		// Returns false if time is out of range or not yet available.
		private bool TryGetBarsAgoFromTime_BIP0(DateTime t, out int barsAgo)
		{
		    barsAgo = -1;
		
		    if (BarsArray == null || BarsArray.Length < 1 || BarsArray[0] == null)
		        return false;
		
		    // Make sure we have at least one bar
		    if (CurrentBars == null || CurrentBars.Length < 1 || CurrentBars[0] < 0)
		        return false;
		
		    // GetBar returns an absolute bar index (0..CurrentBars[0]) for the given time.
		    // It returns -1 if time is before the first bar, or may return the last bar for future times depending on series.
		    int barIndex = BarsArray[0].GetBar(t);
		    if (barIndex < 0)
		        return false;
		
		    // Clamp just in case
		    if (barIndex > CurrentBars[0])
		        barIndex = CurrentBars[0];
		
		    barsAgo = CurrentBars[0] - barIndex;
		    return barsAgo >= 0;
		}
		
		private int GetBip0BarIndex(DateTime t)
		{
		    if (!TryGetBarsAgoFromTime_BIP0(t, out int barsAgo))
		        return -1;
		    return CurrentBars[0] - barsAgo;
		}


		
		// Gate for POTENTIAL Pattern A LONG:
		// Returns true once the pattern has "cleared/armed" (pivot guide cleared support guide),
		// returns false if it aborts (new extreme beyond TrendStart) or never clears.
		private bool ScanPatternAFromTriple_Long_PotentialGate(PatternATriplet trip)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    double eps           = TickSize * 0.5;
		    double supportGuide  = trip.SupportPrice;
		    double trendStartLow = trip.TrendStartPrice;
		
		    bool hasClearedSupport = false;
		
		    // Move forward from TrendStartIdx+1 through future pivots
		    for (int j = trip.TrendStartIdx + 1; j < pivotTimes.Count; j++)
		    {
		        if (!pivotIsHigh[j])
		        {
		            // LOW pivot: abort if new low breaks TrendStart low before arming
		            double lowPrice = pivotPrices[j];
		            if (lowPrice < trendStartLow - eps)
		                return false;
		
		            // otherwise continue; arming happens on HIGH pivots (guide > supportGuide)
		        }
		        else
		        {
		            // HIGH pivot: “cleared support” once body/guide gets above Support guide
		            double highGuide = pivotGuidePrices[j];
		            if (highGuide > supportGuide + eps)
		            {
		                hasClearedSupport = true;
		                break;
		            }
		        }
		    }
		
		    return hasClearedSupport;
		}
		
		// Gate for POTENTIAL Pattern A SHORT:
		// Returns true once the pattern has "cleared/armed" (pivot guide cleared resistance guide),
		// returns false if it aborts (new extreme beyond TrendStart) or never clears.
		private bool ScanPatternAFromTriple_Short_PotentialGate(PatternATriplet trip)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    double eps             = TickSize * 0.5;
		    double resistanceGuide = trip.SupportPrice;
		    double trendStartHigh  = trip.TrendStartPrice;
		
		    bool hasClearedResistance = false;
		
		    // Move forward from TrendStartIdx+1 through future pivots
		    for (int j = trip.TrendStartIdx + 1; j < pivotTimes.Count; j++)
		    {
		        if (pivotIsHigh[j])
		        {
		            // HIGH pivot: abort if new high breaks TrendStart high before arming
		            double highPrice = pivotPrices[j];
		            if (highPrice > trendStartHigh + eps)
		                return false;
		
		            // otherwise continue; arming happens on LOW pivots (guide < resistanceGuide)
		        }
		        else
		        {
		            // LOW pivot: “cleared resistance” once body/guide gets below resistance guide
		            double lowGuide = pivotGuidePrices[j];
		            if (lowGuide < resistanceGuide - eps)
		            {
		                hasClearedResistance = true;
		                break;
		            }
		        }
		    }
		
		    return hasClearedResistance;
		}

		
		private bool ScanPatternAFromTriple_Long(
		    PatternATriplet trip,
		    Dictionary<int, double> newLongByBar,
		    List<PatternALevel> newLevels)
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
		    if (ogBarIndex < 0 || entryBarIndex < 0)
		        return false;
		
		    double entryPrice = pivotPrices[entryPivotIdx];
		
		    // Signal placement (keep your offset style)
		    if (EnablePatternASignal)
		        newLongByBar[entryBarIndex] = entryPrice - (PatternAMarkerOffsetTicks * TickSize);
		
		    // Level ends at entry (wick) pivot (so it does NOT extend to the right forever)
		    if (EnablePatternALevels)
		    {
		        newLevels.Add(new PatternALevel
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
		
		private bool ScanPatternAFromTriple_Short(
		    PatternATriplet trip,
		    Dictionary<int, double> newShortByBar,
		    List<PatternALevel> newLevels)
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
		    if (ogBarIndex < 0 || entryBarIndex < 0)
		        return false;
		
		    double entryPrice = pivotPrices[entryPivotIdx];
		
		    // Signal placement (keep your offset style)
		    if (EnablePatternASignal)
		        newShortByBar[entryBarIndex] = entryPrice + (PatternAMarkerOffsetTicks * TickSize);
		
		    // Level ends at entry (wick) pivot
		    if (EnablePatternALevels)
		    {
		        newLevels.Add(new PatternALevel
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
	


		private void DrawPatternADebug(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternALevel> newLevels)
		{
		    if (!CanDrawDebug() || !DrawPatternA)
		        return;
		
		    // ---- SIGNALS (triangles) ----
		    // Clear stale markers (only if we have previous state)
		    if (patternALongByBar != null)
		        foreach (var kv in patternALongByBar.ToList())
		            if (!newLongByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNA_L_{kv.Key}");
		
		    if (patternAShortByBar != null)
		        foreach (var kv in patternAShortByBar.ToList())
		            if (!newShortByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNA_S_{kv.Key}");
		
		    // Draw current (or none if disabled)
		    if (EnablePatternASignal)
		    {
		        foreach (var kv in newLongByBar)
		            DrawPatternAText(true, kv.Key, kv.Value);
		
		        foreach (var kv in newShortByBar)
		            DrawPatternAText(false, kv.Key, kv.Value);
		    }
		    else
		    {
		        // If signals disabled, remove any existing markers
		        if (patternALongByBar != null)
		            foreach (var kv in patternALongByBar)
		                RemoveDrawObject($"PATTERNA_L_{kv.Key}");
		
		        if (patternAShortByBar != null)
		            foreach (var kv in patternAShortByBar)
		                RemoveDrawObject($"PATTERNA_S_{kv.Key}");
		    }
		
		    // ---- LEVELS (horizontal lines) ----
		    // For simplicity, clear prior drawn levels and redraw current.
		    // (Still cheap because this is debug-only.)
		    if (patternALevels != null)
		    {
		        foreach (var lvl in patternALevels)
		        {
		            string tagOld = lvl.IsLong
		                ? $"PATTERNA_LVL_L_{lvl.OgBarIndex}"
		                : $"PATTERNA_LVL_S_{lvl.OgBarIndex}";
		            RemoveDrawObject(tagOld);
		        }
		    }
		
		    if (EnablePatternALevels)
		    {
		        foreach (var lvl in newLevels)
		            DrawPatternALevelLine(lvl);
		    }
		}
		
		private void DrawPatternAText(bool isLong, int barIndex, double price)
		{
		    if (!CanDrawDebug() || !DrawPatternA)
		        return;
		
		    if (barIndex < 0 || barIndex > CurrentBar)
		        return;
		
		    int barsAgo = CurrentBar - barIndex;
		    if (barsAgo < 0)
		        return;
		
		    string tag  = isLong ? $"PATTERNA_L_{barIndex}" : $"PATTERNA_S_{barIndex}";
		    string text = isLong ? "▲" : "▼";
		
		    Brush brush = isLong ? PatternALongColor : PatternAShortColor;
		
		    // If you want to ensure updates replace old text, clear first
		    RemoveDrawObject(tag);
		
		    Draw.Text(this, tag, false,
		        text,
		        barsAgo,
		        price,
		        0,
		        brush,
		        new SimpleFont("Arial", PatternAMarkerFontSize),
		        TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}
		
		private void DrawPatternALevelLine(PatternALevel lvl)
		{
		    if (!CanDrawDebug() || !DrawPatternA)
		        return;
		
		    if (lvl == null)
		        return;
		
		    string tag = lvl.IsLong
		        ? $"PATTERNA_LVL_L_{lvl.OgBarIndex}"
		        : $"PATTERNA_LVL_S_{lvl.OgBarIndex}";
		
		    Brush brush = lvl.IsLong ? PatternALongColor : PatternAShortColor;
		
		    RemoveDrawObject(tag);
		
		    DateTime t1 = lvl.OgTime;
		    DateTime t2 = (lvl.EntryTime != default(DateTime)) ? lvl.EntryTime : Times[0][0];
		
		    Draw.Line(this, tag, false,
		        t1, lvl.Price,
		        t2, lvl.Price,
		        brush, PatternALevelDashStyle, PatternALevelWidth);
		}




		
		private double GetPatternAMarkerOffsetPrice(bool isLong)
		{
		    double ticks = PatternAMarkerOffsetTicks * TickSize;
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
//		    if (!ShowZigZagDebug && !DebugDrawPatternA /*&& !DebugDrawPatternA */)
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
		private AlightenLevelMultiTimeframeMATickPatternAV0001[] cacheAlightenLevelMultiTimeframeMATickPatternAV0001;
		public AlightenLevelMultiTimeframeMATickPatternAV0001 AlightenLevelMultiTimeframeMATickPatternAV0001(int barsToProcess, bool enablePatternASignal, bool enablePatternALevels, bool drawPatternA, int patternAMarkerFontSize, int patternAMarkerOffsetTicks, System.Windows.Media.Brush patternALongColor, System.Windows.Media.Brush patternAShortColor, int patternALevelWidth, DashStyleHelper patternALevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return AlightenLevelMultiTimeframeMATickPatternAV0001(Input, barsToProcess, enablePatternASignal, enablePatternALevels, drawPatternA, patternAMarkerFontSize, patternAMarkerOffsetTicks, patternALongColor, patternAShortColor, patternALevelWidth, patternALevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public AlightenLevelMultiTimeframeMATickPatternAV0001 AlightenLevelMultiTimeframeMATickPatternAV0001(ISeries<double> input, int barsToProcess, bool enablePatternASignal, bool enablePatternALevels, bool drawPatternA, int patternAMarkerFontSize, int patternAMarkerOffsetTicks, System.Windows.Media.Brush patternALongColor, System.Windows.Media.Brush patternAShortColor, int patternALevelWidth, DashStyleHelper patternALevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			if (cacheAlightenLevelMultiTimeframeMATickPatternAV0001 != null)
				for (int idx = 0; idx < cacheAlightenLevelMultiTimeframeMATickPatternAV0001.Length; idx++)
					if (cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx] != null && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].BarsToProcess == barsToProcess && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].EnablePatternASignal == enablePatternASignal && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].EnablePatternALevels == enablePatternALevels && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].DrawPatternA == drawPatternA && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].PatternAMarkerFontSize == patternAMarkerFontSize && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].PatternAMarkerOffsetTicks == patternAMarkerOffsetTicks && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].PatternALongColor == patternALongColor && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].PatternAShortColor == patternAShortColor && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].PatternALevelWidth == patternALevelWidth && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].PatternALevelDashStyle == patternALevelDashStyle && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].ShowZigZag == showZigZag && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].ZigZagColor == zigZagColor && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].ZigZagWidth == zigZagWidth && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].ZigZagStyle == zigZagStyle && cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx].EqualsInput(input))
						return cacheAlightenLevelMultiTimeframeMATickPatternAV0001[idx];
			return CacheIndicator<AlightenLevelMultiTimeframeMATickPatternAV0001>(new AlightenLevelMultiTimeframeMATickPatternAV0001(){ BarsToProcess = barsToProcess, EnablePatternASignal = enablePatternASignal, EnablePatternALevels = enablePatternALevels, DrawPatternA = drawPatternA, PatternAMarkerFontSize = patternAMarkerFontSize, PatternAMarkerOffsetTicks = patternAMarkerOffsetTicks, PatternALongColor = patternALongColor, PatternAShortColor = patternAShortColor, PatternALevelWidth = patternALevelWidth, PatternALevelDashStyle = patternALevelDashStyle, ShowZigZag = showZigZag, ZigZagColor = zigZagColor, ZigZagWidth = zigZagWidth, ZigZagStyle = zigZagStyle }, input, ref cacheAlightenLevelMultiTimeframeMATickPatternAV0001);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenLevelMultiTimeframeMATickPatternAV0001 AlightenLevelMultiTimeframeMATickPatternAV0001(int barsToProcess, bool enablePatternASignal, bool enablePatternALevels, bool drawPatternA, int patternAMarkerFontSize, int patternAMarkerOffsetTicks, System.Windows.Media.Brush patternALongColor, System.Windows.Media.Brush patternAShortColor, int patternALevelWidth, DashStyleHelper patternALevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternAV0001(Input, barsToProcess, enablePatternASignal, enablePatternALevels, drawPatternA, patternAMarkerFontSize, patternAMarkerOffsetTicks, patternALongColor, patternAShortColor, patternALevelWidth, patternALevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenLevelMultiTimeframeMATickPatternAV0001 AlightenLevelMultiTimeframeMATickPatternAV0001(ISeries<double> input , int barsToProcess, bool enablePatternASignal, bool enablePatternALevels, bool drawPatternA, int patternAMarkerFontSize, int patternAMarkerOffsetTicks, System.Windows.Media.Brush patternALongColor, System.Windows.Media.Brush patternAShortColor, int patternALevelWidth, DashStyleHelper patternALevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternAV0001(input, barsToProcess, enablePatternASignal, enablePatternALevels, drawPatternA, patternAMarkerFontSize, patternAMarkerOffsetTicks, patternALongColor, patternAShortColor, patternALevelWidth, patternALevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenLevelMultiTimeframeMATickPatternAV0001 AlightenLevelMultiTimeframeMATickPatternAV0001(int barsToProcess, bool enablePatternASignal, bool enablePatternALevels, bool drawPatternA, int patternAMarkerFontSize, int patternAMarkerOffsetTicks, System.Windows.Media.Brush patternALongColor, System.Windows.Media.Brush patternAShortColor, int patternALevelWidth, DashStyleHelper patternALevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternAV0001(Input, barsToProcess, enablePatternASignal, enablePatternALevels, drawPatternA, patternAMarkerFontSize, patternAMarkerOffsetTicks, patternALongColor, patternAShortColor, patternALevelWidth, patternALevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenLevelMultiTimeframeMATickPatternAV0001 AlightenLevelMultiTimeframeMATickPatternAV0001(ISeries<double> input , int barsToProcess, bool enablePatternASignal, bool enablePatternALevels, bool drawPatternA, int patternAMarkerFontSize, int patternAMarkerOffsetTicks, System.Windows.Media.Brush patternALongColor, System.Windows.Media.Brush patternAShortColor, int patternALevelWidth, DashStyleHelper patternALevelDashStyle, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenLevelMultiTimeframeMATickPatternAV0001(input, barsToProcess, enablePatternASignal, enablePatternALevels, drawPatternA, patternAMarkerFontSize, patternAMarkerOffsetTicks, patternALongColor, patternAShortColor, patternALevelWidth, patternALevelDashStyle, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

#endregion

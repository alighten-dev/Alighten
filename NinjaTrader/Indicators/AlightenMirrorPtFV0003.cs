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
    public class AlightenMirrorPtFV0003 : Indicator
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
		
		// Pattern F results (MA-friendly persistent state)
		// barIndex is primary-series (BIP 0) absolute bar index (0..CurrentBar)
		private Dictionary<int, double> patternFLongByBar;
		private Dictionary<int, double> patternFShortByBar;
		private Dictionary<int, double> patternFLongLevelByBar;
		private Dictionary<int, double> patternFShortLevelByBar;
		
		// Pattern F derived levels (logical objects; optionally drawn in debug)
		private List<PatternFLevel> patternFLevels;
		
		// Pattern F structure tracking (triplets/pivots). This mirrors the V00029 concept.
		// These are used inside ScanPatternF() to detect OG/support/regional relationships.
		private List<PatternFTriplet> patternFTriplets;
		private List<PatternFTriplet> patternFTripletsPotential; // potential (structural-only)

		// PERF: reused per-tick scratch lists (avoid allocating two Lists every tick)
		private List<PatternFTriplet> liveLongTripsScratch;
		private List<PatternFTriplet> liveShortTripsScratch;

		
		// Represents a single Pattern F triple (OG, Support, TrendStart)
		// Represents a single Pattern F triple (Pivot1 level, Pivot2, Pivot3-cross)
		private class PatternFTriplet
		{
            public int BreakBarIndex = -1;
		    // ---- Pivot structure ----
		    // OgIdx         = Pivot1 (start pivot) -> defines Pattern F level (guide price)
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
		
		    public PatternFTriplet()
		    {
		        IsActive  = true;
		        CrossTime = Core.Globals.MinDate;
		    }
		}


		
		// Represents a horizontal Pattern F level to draw (at OG price), ending at the wick/entry pivot
		private class PatternFLevel
		{
		    public bool     IsLong;
		    public int      OgBarIndex;
		    public DateTime OgTime;
		
		    public int      EntryBarIndex;
		    public DateTime EntryTime;
		
		    public double   Price; // OG level (guide)
		
		    public PatternFLevel() { }
		}
		
		private PatternFTriplet activePatternFLong;
		private PatternFTriplet activePatternFShort;

		private Dictionary<string, int> lastSignalBip0BarByKey;
		
		private string MakeTripletKey(PatternFTriplet t)
		{
		    if (t == null) return string.Empty;
		    return (t.IsLong ? "L" : "S") + "|" + t.OgIdx + "|" + t.SupportIdx + "|" + t.TrendStartIdx;
		}
		
		// ---- LIVE (intrabar) draw tags for Pattern F ----
		private const string PF_LIVE_TAG_L = "PATTERNA_LIVE_TAG_L";
		private const string PF_LIVE_TAG_S = "PATTERNA_LIVE_TAG_S";
		private const string PF_LIVE_LVL_L = "PATTERNA_LIVE_LVL_L";
		private const string PF_LIVE_LVL_S = "PATTERNA_LIVE_LVL_S";
		
		// --- Pattern F live recency (BIP0) state ---
		private bool paLivePrevLong  = false;
		private bool paLivePrevShort = false;
		
		private int  paLiveLongLastEdgeSeq  = -1;   // last tick-seq where long became true (false->true)
		private int  paLiveShortLastEdgeSeq = -1;   // last tick-seq where short became true (false->true)
		private int  paLiveTickSeq          = 0;    // increments each BIP0 call within a bar
		
		private int  paLiveLastChosenDir    = 0;    // +1 long, -1 short, 0 none (used as a stability tie-break)

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

        private HashSet<string> recordedPatternKeys;

        // Persistent storage for live timestamps across bar closes
        private Dictionary<int, double> livePreciseEndTimesL;
        private Dictionary<int, double> livePreciseEndTimesS;
	
        #endregion
		
        #region Properties
		

        [NinjaScriptProperty]
		[Range(0, 500000)]
		[Display(Name="Bars To Process", GroupName="ZigZag", Order=53)]
		public int BarsToProcess { get; set; } = 200; // 0 = process all
		
		// ---- Pattern F toggles ----


		[NinjaScriptProperty]
		[Display(Name="Enable Pattern F Signals", GroupName="Pattern F", Order=1)]
		public bool EnablePatternFSignal { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="Enable Pattern F Levels", GroupName="Pattern F", Order=2)]
		public bool EnablePatternFLevels { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name = "Draw Pattern F", GroupName = "Pattern F", Order = 3)]
		public bool DrawPatternF { get; set; } = true;
	
		
		// Optional styling knobs (safe defaults)
		[NinjaScriptProperty]
		[Range(6, 30)]
		[Display(Name="Pattern F Marker Font Size", GroupName="Pattern F", Order=20)]
		public int PatternFMarkerFontSize { get; set; } = 18;
		
		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name="Pattern F Marker Offset Ticks", GroupName="Pattern F", Order=21)]
		public int PatternFMarkerOffsetTicks { get; set; } = 5;
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern F Long Color", GroupName="Pattern F", Order=22)]
		public System.Windows.Media.Brush PatternFLongColor { get; set; } = Brushes.Lime;
		
		[Browsable(false)]
		public string PatternFLongColorSerialize
		{
		    get => Serialize.BrushToString(PatternFLongColor);
		    set => PatternFLongColor = Serialize.StringToBrush(value);
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="Pattern F Short Color", GroupName="Pattern F", Order=23)]
		public System.Windows.Media.Brush PatternFShortColor { get; set; } = Brushes.Magenta;
		
		[Browsable(false)]
		public string PatternFShortColorSerialize
		{
		    get => Serialize.BrushToString(PatternFShortColor);
		    set => PatternFShortColor = Serialize.StringToBrush(value);
		}
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="Pattern F Level Width", GroupName="Pattern F", Order=30)]
		public int PatternFLevelWidth { get; set; } = 2;
		
		[NinjaScriptProperty]
		[Display(Name="Pattern F Level Dash", GroupName="Pattern F", Order=31)]
		public DashStyleHelper PatternFLevelDashStyle { get; set; } = DashStyleHelper.Dash;


        [NinjaScriptProperty]
        [Display(Name="Show Debug Labels", GroupName="Pattern F", Order=40)]
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
		
		
		
		
        

		private Series<double> patternFSignal;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternFSignal
		{
		    get { return patternFSignal; }
		}
		
		private Series<double> patternFSignalMA;
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternFSignalMA
		{
		    get { return patternFSignalMA; }
		}

        private Series<double> patternFLongLevel;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternFLongLevel => patternFLongLevel;

        private Series<double> patternFLongStartTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternFLongStartTimestamp => patternFLongStartTimestamp;

        private Series<double> patternFLongEndTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternFLongEndTimestamp => patternFLongEndTimestamp;

        private Series<double> patternFShortLevel;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternFShortLevel => patternFShortLevel;

        private Series<double> patternFShortStartTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternFShortStartTimestamp => patternFShortStartTimestamp;

        private Series<double> patternFShortEndTimestamp;
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PatternFShortEndTimestamp => patternFShortEndTimestamp;
		
		private Series<double> patternFLongSignalBarTimestamp;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternFLongSignalBarTimestamp => patternFLongSignalBarTimestamp;
		
		private Series<double> patternFShortSignalBarTimestamp;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> PatternFShortSignalBarTimestamp => patternFShortSignalBarTimestamp;
		
		#endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AlightenMirrorPtFV0003";
                Description = "Market Analyzer core: HTF ZigZag/levels on primary series with 1m/5m proximity columns.";
                Calculate   = Calculate.OnEachTick;

                // For MA, overlay doesn’t matter. For chart debugging, overlay helps.
                IsOverlay               		= true;
                DrawOnPricePanel        		= true;

                DisplayInDataBox        		= true;
                PaintPriceMarkers       		= false;
                IsSuspendedWhileInactive 		= false;
				ShowTransparentPlotsInDataBox 	= true;

               
				AddPlot(Brushes.Transparent, "PatternFSignal");
				AddPlot(Brushes.Transparent, "PatternFSignalMA");
				AddPlot(Brushes.Transparent, "PatternFLongLevel");
				AddPlot(Brushes.Transparent, "PatternFLongStartTimestamp");
				AddPlot(Brushes.Transparent, "PatternFLongEndTimestamp");
				AddPlot(Brushes.Transparent, "PatternFShortLevel");
				AddPlot(Brushes.Transparent, "PatternFShortStartTimestamp");
				AddPlot(Brushes.Transparent, "PatternFShortEndTimestamp");
				AddPlot(Brushes.Transparent, "PatternFLongSignalBarTimestamp");
				AddPlot(Brushes.Transparent, "PatternFShortSignalBarTimestamp");
                
            }
            else if (State == State.Configure)
            {
                

                instanceId = $"PA_FILT_{Guid.NewGuid()}";
		
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
				
				patternFLongByBar  				= new Dictionary<int, double>(256);
				patternFShortByBar 				= new Dictionary<int, double>(256);
				patternFLongLevelByBar			= new Dictionary<int, double>(256);
				patternFShortLevelByBar			= new Dictionary<int, double>(256);
				patternFLevels     				= new List<PatternFLevel>(128);
				patternFTriplets   				= new List<PatternFTriplet>(128);
				patternFTripletsPotential   	= new List<PatternFTriplet>(128);
				liveLongTripsScratch			= new List<PatternFTriplet>(32);
				liveShortTripsScratch			= new List<PatternFTriplet>(32);
				//patternFCandidates 				= new List<PatternFCandidate>(128);
				
				lastSignalBip0BarByKey = new Dictionary<string, int>(512, StringComparer.Ordinal);
                lastTickTime = DateTime.MinValue;
                recordedPatternKeys = new HashSet<string>();

				patternFSignal					= Values[0];
				patternFSignalMA				= Values[1];
                patternFLongLevel               = Values[2];
                patternFLongStartTimestamp      = Values[3];
                patternFLongEndTimestamp        = Values[4];
                patternFShortLevel              = Values[5];
                patternFShortStartTimestamp     = Values[6];
                patternFShortEndTimestamp       = Values[7];
				patternFLongSignalBarTimestamp  = Values[8];
				patternFShortSignalBarTimestamp = Values[9];

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
		
		        if (patternFSignal == null || patternFSignalMA == null)
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
		            patternFSignal[0]   = 0;
		            patternFSignalMA[0] = 0;

		            patternFLongLevel[0]          = 0;
		            patternFLongStartTimestamp[0] = 0;
		            patternFLongEndTimestamp[0]   = 0;
		            
		            patternFShortLevel[0]          = 0;
		            patternFShortStartTimestamp[0] = 0;
		            patternFShortEndTimestamp[0]   = 0;
					
					patternFLongSignalBarTimestamp[0]  = 0;
					patternFShortSignalBarTimestamp[0] = 0;
		
		            if (CanDrawDebug() && DrawPatternF)
		            {
		                RemoveDrawObject(PF_LIVE_TAG_L);
		                RemoveDrawObject(PF_LIVE_TAG_S);
		                RemoveDrawObject(PF_LIVE_LVL_L);
		                RemoveDrawObject(PF_LIVE_LVL_S);
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
		            RebuildPatternF_BIP0();
		
		            if (EnablePatternFSignal)
		            {
		                // Default clear (current bar)
		                patternFSignal[0]   = 0;
		                patternFSignalMA[0] = 0;

		                patternFLongLevel[0]          = 0;
		                patternFLongStartTimestamp[0] = 0;
		                patternFLongEndTimestamp[0]   = 0;
		                
		                patternFShortLevel[0]          = 0;
		                patternFShortStartTimestamp[0] = 0;
		                patternFShortEndTimestamp[0]   = 0;
						
						patternFLongSignalBarTimestamp[0]  = 0;
						patternFShortSignalBarTimestamp[0] = 0;
						
						// Clear inherited live values from previous bar's intra-bar updates
						patternFLongLevel[1]  = 0;
						patternFShortLevel[1] = 0;
		
		                if (CurrentBars[0] >= 1)
		                {
		                    int sigBar = CurrentBars[0] - 1;
		
		                    bool longNow  = patternFLongByBar  != null && patternFLongByBar.ContainsKey(sigBar);
		                    bool shortNow = patternFShortByBar != null && patternFShortByBar.ContainsKey(sigBar);
		
		                    double sig = 0;
		                    if (longNow && !shortNow)
		                    {
		                        sig = +1;
		                        if (patternFLongLevelByBar != null && patternFLongLevelByBar.TryGetValue(sigBar, out double lvlPrice))
		                            patternFLongLevel[1] = lvlPrice;
		                    }
		                    else if (shortNow && !longNow)
		                    {
		                        sig = -1;
		                        if (patternFShortLevelByBar != null && patternFShortLevelByBar.TryGetValue(sigBar, out double lvlPrice))
		                            patternFShortLevel[1] = lvlPrice;
		                    }
		
		                    // DataBox / chart-aligned plot (belongs to bar that just closed)
		                    patternFSignal[1] = sig;
		
		                    // MA-friendly plot (belongs to current bar so MA reads [0] reliably)
		                    patternFSignalMA[0] = sig;
		                }
		            }
		        }
		
		        // -----------------------------
		        // 2) LIVE intrabar semantics (new)
		        //    Update on EVERY tick so it can appear/disappear while bar forms.
		        // -----------------------------
		        if (EnablePatternFSignal)
		        {
		            UpdatePatternF_Live_BIP0();
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
		
		#region Pattern F
		
		private void UpdatePatternF_Live_BIP0()
		{
		    double eps = TickSize * 0.5;
		    double currentPrice = Closes[0][0];
            double currentLow = Lows[0][0];
            double currentHigh = Highs[0][0];
            double bodyLow = Math.Min(Opens[0][0], Closes[0][0]);
            double bodyHigh = Math.Max(Opens[0][0], Closes[0][0]);

		    int curBarIndex = CurrentBars[0];
		
		    if (CanDrawDebug() && DrawPatternF)
		    {
		        RemoveDrawObject(PF_LIVE_TAG_L);
		        RemoveDrawObject(PF_LIVE_TAG_S);
                for(int i=0; i<20; i++)
                {
                    RemoveDrawObject(PF_LIVE_LVL_L + "_" + i);
                    RemoveDrawObject(PF_LIVE_LVL_S + "_" + i);
                }
		    }

		    if (CurrentBar < 1)
		    {
		        patternFSignal[0] = 0;
		        paLivePrevLong  = false;
		        paLivePrevShort = false;
		        return;
		    }
		
		    if (patternFTripletsPotential == null || patternFTripletsPotential.Count == 0 && patternFTriplets.Count == 0)
		    {
		        patternFSignal[0] = 0;
		        paLivePrevLong  = false;
		        paLivePrevShort = false;
		        return;
		    }

		    // Advance sequences
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
		        paLiveTickSeq++;
		    }
		
		    bool liveLong  = false;
		    bool liveShort = false;
		
            var liveLongTrips  = liveLongTripsScratch;
            var liveShortTrips = liveShortTripsScratch;
            liveLongTrips.Clear();
            liveShortTrips.Clear();

		    foreach (var t in patternFTripletsPotential.ToList())
		    {
		        if (t == null) continue;
		        if (!t.IsActive) continue;

		        int lastConfirmed = GetLastConfirmedPivotIdxBip0();
		        if (lastConfirmed >= 0)
		        {
		            int postStart = t.TrendStartIdx + 1; // This is idx3
		            if (postStart <= lastConfirmed)
		            {
                        // Pivot 3 has locked in, and we haven't broken out!
                        // This triplet failed the "immediate next pivot" constraint.
                        t.IsActive = false;
                        patternFTripletsPotential.Remove(t);
                        continue;
		            }
		        }

		        if (t.IsLong)
		        {
		            if (currentPrice > t.SupportPrice + eps)
		            {
		                t.BreakBarIndex = curBarIndex;
		                patternFTriplets.Add(t);
		                patternFTripletsPotential.Remove(t);
		            }
		        }
		        else
		        {
		            if (currentPrice < t.SupportPrice - eps)
		            {
		                t.BreakBarIndex = curBarIndex;
		                patternFTriplets.Add(t);
		                patternFTripletsPotential.Remove(t);
		            }
		        }
		    }
		
		    foreach (var t in patternFTriplets)
		    {
		        if (!t.IsActive || t.BreakBarIndex == -1 || curBarIndex <= t.BreakBarIndex)
		            continue;
		            
		        if (t.IsLong)
		        {
	                bool wickTouches = currentLow <= t.OgPrice + eps;
	                bool bodyAbove   = bodyLow >= t.OgPrice - eps;
	
	                if (wickTouches && bodyAbove)
	                {
                        liveLong = true;
                        liveLongTrips.Add(t);
                    }
                }
                else
                {
	                bool wickTouches = currentHigh >= t.OgPrice - eps;
	                bool bodyBelow   = bodyHigh <= t.OgPrice + eps;
	
	                if (wickTouches && bodyBelow)
	                {
                        liveShort = true;
                        liveShortTrips.Add(t);
                    }
                }
            }
		
		    if (liveLong && !paLivePrevLong) paLiveLongLastEdgeSeq = paLiveTickSeq;
		    if (liveShort && !paLivePrevShort) paLiveShortLastEdgeSeq = paLiveTickSeq;
		
		    paLivePrevLong  = liveLong;
		    paLivePrevShort = liveShort;
		
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
		            double maxLongPen  = 0;
		            double maxShortPen = 0;
		
                    foreach(var t in liveLongTrips)
		            {
                        double p = (t.OgPrice - currentLow) / TickSize;
                        if (p > maxLongPen) maxLongPen = p;
		            }
                    foreach(var t in liveShortTrips)
		            {
                        double p = (currentHigh - t.OgPrice) / TickSize;
                        if (p > maxShortPen) maxShortPen = p;
		            }
		
		            if (maxLongPen > maxShortPen + 0.0001)
		            {
		                liveSig = +1;
		                paLiveLastChosenDir = +1;
		            }
		            else if (maxShortPen > maxLongPen + 0.0001)
		            {
		                liveSig = -1;
		                paLiveLastChosenDir = -1;
		            }
		            else
		            {
		                if (paLiveLastChosenDir == +1) liveSig = +1;
		                else if (paLiveLastChosenDir == -1) liveSig = -1;
                        else liveSig = 0;
		            }
		        }
		    }
		    else
		    {
		        liveSig = 0;
		        paLiveLastChosenDir = 0;
		    }

            if (CurrentBars[0] > 0)
            {
                int sigBar = CurrentBars[0]; 
                DateTime now = lastTickTime != DateTime.MinValue ? lastTickTime : Times[0][0];
                
                if (liveSig == +1 && liveLongTrips.Count > 0)
                {
                    if (!livePreciseEndTimesL.ContainsKey(sigBar))
                    {
                        double preciseTime = CurrentPreciseTime;
                        livePreciseEndTimesL[sigBar] = preciseTime;
                    }
                    
                    if (paLiveLongLastEdgeSeq == paLiveTickSeq || liveLongFirstHitTime == DateTime.MinValue)
                        liveLongFirstHitTime = now;

                    patternFLongLevel[0] = liveLongTrips[0].OgPrice;
                    patternFLongStartTimestamp[0] = liveLongFirstHitTime.ToOADate();
                    patternFLongEndTimestamp[0] = now.ToOADate();
					patternFLongSignalBarTimestamp[0] = Time[0].ToOADate();
                }
                else
                {
                    patternFLongLevel[0] = 0;
                    patternFLongStartTimestamp[0] = 0;
                    patternFLongEndTimestamp[0] = 0;
					patternFLongSignalBarTimestamp[0] = 0;
                    liveLongFirstHitTime = DateTime.MinValue;
                }
                
                if (liveSig == -1 && liveShortTrips.Count > 0)
                {
                    if (!livePreciseEndTimesS.ContainsKey(sigBar))
                    {
                        double preciseTime = CurrentPreciseTime;
                        livePreciseEndTimesS[sigBar] = preciseTime;
                    }

                    if (paLiveShortLastEdgeSeq == paLiveTickSeq || liveShortFirstHitTime == DateTime.MinValue)
                        liveShortFirstHitTime = now;

                    patternFShortLevel[0] = liveShortTrips[0].OgPrice;
                    patternFShortStartTimestamp[0] = liveShortFirstHitTime.ToOADate();
                    patternFShortEndTimestamp[0] = now.ToOADate();
					patternFShortSignalBarTimestamp[0] = Time[0].ToOADate();
                }
                else
                {
                    patternFShortLevel[0] = 0;
                    patternFShortStartTimestamp[0] = 0;
                    patternFShortEndTimestamp[0] = 0;
					patternFShortSignalBarTimestamp[0] = 0;
                    liveShortFirstHitTime = DateTime.MinValue;
                }
            }
		
		    patternFSignal[0] = liveSig;
		
		    if (CanDrawDebug() && DrawPatternF)
		    {
		        if (liveLong && liveLongTrips.Count > 0)
		        {
		            double y = currentLow - (PatternFMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PF_LIVE_TAG_L, false, "L", 0, y, 0, PatternFLongColor, new SimpleFont("Arial", PatternFMarkerFontSize), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		
                    for(int i=0; i < liveLongTrips.Count && i < 20; i++)
                    {
                        var t = liveLongTrips[i];
                        double level = t.OgPrice;

		                int ogBarIndex = (pivotTimes != null && t.OgIdx >= 0 && t.OgIdx < pivotTimes.Count)
		                    ? BarsArray[0].GetBar(pivotTimes[t.OgIdx])
		                    : -1;
		
		                if (ogBarIndex >= 0)
		                {
		                    int ogBarsAgo = CurrentBars[0] - ogBarIndex;
		                    if (ogBarsAgo >= 0)
		                    {
		                        Draw.Line(this, PF_LIVE_LVL_L + "_" + i, false,
		                            ogBarsAgo, level,
		                            0,        level,
		                            PatternFLongColor, PatternFLevelDashStyle, PatternFLevelWidth);
		                    }
		                }
                    }
		        }
		
		        if (liveShort && liveShortTrips.Count > 0)
		        {
		            double y = currentHigh + (PatternFMarkerOffsetTicks * TickSize);
		            Draw.Text(this, PF_LIVE_TAG_S, false, "S", 0, y, 0, PatternFShortColor, new SimpleFont("Arial", PatternFMarkerFontSize), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		
                    for(int i=0; i < liveShortTrips.Count && i < 20; i++)
                    {
                        var t = liveShortTrips[i];
                        double level = t.OgPrice;
                        
		                int ogBarIndex = (pivotTimes != null && t.OgIdx >= 0 && t.OgIdx < pivotTimes.Count)
		                    ? BarsArray[0].GetBar(pivotTimes[t.OgIdx])
		                    : -1;
		
		                if (ogBarIndex >= 0)
		                {
		                    int ogBarsAgo = CurrentBars[0] - ogBarIndex;
		                    if (ogBarsAgo >= 0)
		                    {
		                        Draw.Line(this, PF_LIVE_LVL_S + "_" + i, false,
		                            ogBarsAgo, level,
		                            0,        level,
		                            PatternFShortColor, PatternFLevelDashStyle, PatternFLevelWidth);
		                    }
		                }
                    }
		        }
		    }
		}
		
		
		private void RebuildPatternF_BIP0()
		{
		    if (!EnablePatternFSignal && !EnablePatternFLevels)
		        return;
		
		    BuildPatternF(out var newPBLong, out var newPBShort, out var newPBLongLevel, out var newPBShortLevel, out var newPBLevels);
		
		    ApplyPatternFResults(newPBLong, newPBShort, newPBLongLevel, newPBShortLevel, newPBLevels);
			
			//RebuildPatternFCandidates_BIP0();
		
		    // chart-only
		    if (DrawPatternF && CanDrawDebug())
		        DrawPatternFDebug(newPBLong, newPBShort, newPBLevels);
		}
	
		private void BuildPatternF(
		    out Dictionary<int, double> newLongByBar,
		    out Dictionary<int, double> newShortByBar,
		    out Dictionary<int, double> newLongLevelByBar,
		    out Dictionary<int, double> newShortLevelByBar,
		    out List<PatternFLevel> newLevels)
		{
		    newLongByBar  = new Dictionary<int, double>();
		    newShortByBar = new Dictionary<int, double>();
		    newLongLevelByBar = new Dictionary<int, double>();
		    newShortLevelByBar = new Dictionary<int, double>();
		    newLevels     = new List<PatternFLevel>();
		
		    if ((!EnablePatternFSignal && !EnablePatternFLevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 3)
		        return;
		
		    ScanPatternF(newLongByBar, newShortByBar, newLongLevelByBar, newShortLevelByBar, newLevels);
		}

		
		private void ApplyPatternFResults(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    Dictionary<int, double> newLongLevelByBar,
		    Dictionary<int, double> newShortLevelByBar,
		    List<PatternFLevel> newLevels)
		{
		    if (patternFLongByBar == null)
		        patternFLongByBar = new Dictionary<int, double>();
		    if (patternFShortByBar == null)
		        patternFShortByBar = new Dictionary<int, double>();
		    if (patternFLongLevelByBar == null)
		        patternFLongLevelByBar = new Dictionary<int, double>();
		    if (patternFShortLevelByBar == null)
		        patternFShortLevelByBar = new Dictionary<int, double>();
		    if (patternFLevels == null)
		        patternFLevels = new List<PatternFLevel>();
		
		    // Signals
		    if (EnablePatternFSignal)
		    {
		        patternFLongByBar.Clear();
		        foreach (var kv in newLongByBar)
		            patternFLongByBar[kv.Key] = kv.Value;
		
		        patternFShortByBar.Clear();
		        foreach (var kv in newShortByBar)
		            patternFShortByBar[kv.Key] = kv.Value;

		        patternFLongLevelByBar.Clear();
		        foreach (var kv in newLongLevelByBar)
		            patternFLongLevelByBar[kv.Key] = kv.Value;

		        patternFShortLevelByBar.Clear();
		        foreach (var kv in newShortLevelByBar)
		            patternFShortLevelByBar[kv.Key] = kv.Value;
		    }
		    else
		    {
		        patternFLongByBar.Clear();
		        patternFShortByBar.Clear();
		        patternFLongLevelByBar.Clear();
		        patternFShortLevelByBar.Clear();
		    }
		
		    // Levels
		    if (EnablePatternFLevels)
		    {
		        patternFLevels.Clear();
		        patternFLevels.AddRange(newLevels);
		    }
		    else
		    {
		        patternFLevels.Clear();
		    }
		}
		
		
        // Helper class to collect potential signals before filtering
        private class PatternFCandidate
        {
            public int      BarIndex;
            public bool     IsLong;
            public double   LevelPrice;
            public double   SignalPrice;
            public int      OgIdx;
            public PatternFTriplet Triplet;
        }

		// Drop-in replacement for ScanPatternF (assumes you've added the helper methods:
		// SideOfLevel, SegmentCrossesLevel, CountPivotCrossesOverLevel, FindNextPivotCrossIdx)
		private void ScanPatternF(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    Dictionary<int, double> newLongLevelByBar,
		    Dictionary<int, double> newShortLevelByBar,
		    List<PatternFLevel> newLevels)
		{
		    patternFTriplets.Clear();
		    patternFTripletsPotential.Clear();
            var candidates = new List<PatternFCandidate>();

            // PERF: expired levels keyed by tick-rounded price -> O(1) lookups
            HashSet<long> expiredLevelsL = new HashSet<long>();
            HashSet<long> expiredLevelsS = new HashSet<long>();
		
		    if ((!EnablePatternFSignal && !EnablePatternFLevels)
		        || pivotTimes == null
		        || pivotTimes.Count < 3)
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
			
		    void CollectLongSignals(PatternFTriplet t, int startBarIndex)
		    {
		        if (startBarIndex < 0) return;
		
		        double level = t.OgPrice;
		        int start = Math.Max(startBarIndex + 1, 1); 
		        int end = CurrentBars[0];
		
		        if (start > end) return;
				
		        bool sequenceStarted = false;

		        // PERF: expired check once up front — the set cannot gain this level's
		        // price while this triplet is being scanned (it only adds on its own death).
		        if (expiredLevelsL.Contains(LevelKey(level)))
		        {
		            t.IsActive = false;
		            return;
		        }

		        for (int barIndex = start; barIndex <= end; barIndex++)
		        {
		            int barsAgo     = CurrentBars[0] - barIndex;
		            double lo = Lows[0][barsAgo];
		            double o  = Opens[0][barsAgo];
		            double c  = Closes[0][barsAgo];
		            double bodyLow = Math.Min(o, c);
		
		            bool wickTouches = lo <= level + eps;
		            bool bodyAbove   = bodyLow >= level - eps;
		
		            if (wickTouches && bodyAbove)
		            {
                        sequenceStarted = true;
                        candidates.Add(new PatternFCandidate {
                            BarIndex = barIndex,
                            IsLong = true,
                            LevelPrice = level,
                            SignalPrice = lo - (PatternFMarkerOffsetTicks * TickSize),
                            OgIdx = t.OgIdx,
                            Triplet = t
                        });
		            }
                    else
                    {
                        if (!sequenceStarted)
                        {
                            if (bodyLow < level - eps)
                            {
                                t.IsActive = false;
                                break;
                            }
                        }
                        else
                        {
                            if (barIndex < CurrentBars[0])
                            {
                                t.IsActive = false;
                                expiredLevelsL.Add(LevelKey(level));
                                break;
                            }
                        }
                    }
		        }
		    }
		
		    void CollectShortSignals(PatternFTriplet t, int startBarIndex)
		    {
		        if (startBarIndex < 0) return;
		
		        double level = t.OgPrice;
		        int start = Math.Max(startBarIndex + 1, 1);
		        int end = CurrentBars[0];
		
		        if (start > end) return;		
		
		        bool sequenceStarted = false;

		        // PERF: expired check once up front — the set cannot gain this level's
		        // price while this triplet is being scanned (it only adds on its own death).
		        if (expiredLevelsS.Contains(LevelKey(level)))
		        {
		            t.IsActive = false;
		            return;
		        }

		        for (int barIndex = start; barIndex <= end; barIndex++)
		        {
		            int barsAgo     = CurrentBars[0] - barIndex;
		            double hi = Highs[0][barsAgo];
		            double o  = Opens[0][barsAgo];
		            double c  = Closes[0][barsAgo];
		            double bodyHigh = Math.Max(o, c);
		
		            bool wickTouches = hi >= level - eps;
		            bool bodyBelow   = bodyHigh <= level + eps;
					
		            if (wickTouches && bodyBelow)
		            {
                        sequenceStarted = true;
                        candidates.Add(new PatternFCandidate {
                            BarIndex = barIndex,
                            IsLong = false,
                            LevelPrice = level,
                            SignalPrice = hi + (PatternFMarkerOffsetTicks * TickSize),
                            OgIdx = t.OgIdx,
                            Triplet = t
                        });
		            }
                    else
                    {
                        if (!sequenceStarted)
                        {
                            if (bodyHigh > level + eps)
                            {
                                t.IsActive = false;
                                break;
                            }
                        }
                        else
                        {
                            if (barIndex < CurrentBars[0])
                            {
                                t.IsActive = false;
                                expiredLevelsS.Add(LevelKey(level));
                                break;
                            }
                        }
                    }
		        }
		    }
		
		    for (int idx1 = lookbackStartPivotIdx; idx1 < pivotCount - 1; idx1++)
		    {
		        bool isLongStart = pivotIsHigh[idx1];
				double breakLevel = pivotPrices[idx1];
				
				for (int idx2 = idx1 + 1; idx2 < pivotCount; idx2++)
				{
				    if (isLongStart)
				    {
				        if (pivotIsHigh[idx2]) continue;
				    }
				    else
				    {
				        if (!pivotIsHigh[idx2]) continue;
				    }
				
				    double activeLevel = pivotGuidePrices[idx2];
				    
				    int startBar = pivotBarIndex[idx2];
				    if (startBar < 0) continue;
				    
				    int crossBarIndex = -1;
				    
                    int idx3 = idx2 + 1;
                    int endBar = (idx3 < pivotCount) ? pivotBarIndex[idx3] : CurrentBars[0];

                    for (int b = startBar; b <= endBar; b++)
                    {
                        int barsAgo = CurrentBars[0] - b;
                        if (barsAgo < 0) continue;
                        
                        double c = Closes[0][barsAgo];
                        
                        if (isLongStart)
                        {
                            if (c > breakLevel + eps)
                            {
                                crossBarIndex = b;
                                break;
                            }
                        }
                        else
                        {
                            if (c < breakLevel - eps)
                            {
                                crossBarIndex = b;
                                break;
                            }
                        }
                    }
                    
                    var triplet = new PatternFTriplet
                    {
                        OgIdx = idx2,
                        OgPrice = activeLevel,
                        SupportPrice = breakLevel,
                        TrendStartIdx = idx2,
                        TrendStartPrice = activeLevel,
                        IsActive = true,
                        IsLong = isLongStart,
                        BreakBarIndex = crossBarIndex
                    };
                    
                    if (crossBarIndex != -1)
                    {
                        patternFTriplets.Add(triplet);
                        if (isLongStart)
                            CollectLongSignals(triplet, crossBarIndex);
                        else
                            CollectShortSignals(triplet, crossBarIndex);
                    }
                    else
                    {
                        // ONLY add to potentials if idx3 hasn't formed yet!
                        if (idx3 >= pivotCount)
                            patternFTripletsPotential.Add(triplet);
                    }
                        
                    break;
				}
		    }

            if (EnablePatternFSignal)
            {
                foreach (var c in candidates)
                {
                    if (c.IsLong)
                    {
                        newLongByBar[c.BarIndex] = c.SignalPrice;
                        newLongLevelByBar[c.BarIndex] = c.LevelPrice;
                    }
                    else
                    {
                        newShortByBar[c.BarIndex] = c.SignalPrice;
                        newShortLevelByBar[c.BarIndex] = c.LevelPrice;
                    }
                }
            }

            if (EnablePatternFLevels)
            {
                // PERF: single pass, O(1) last-signal per triplet
                // (replaces Select/Distinct + Where/OrderByDescending per triplet).
                // Candidates are appended in ascending bar order per triplet, so
                // the last write per triplet is its latest signal in the sequence.
                var lastCandidateByTriplet = new Dictionary<PatternFTriplet, PatternFCandidate>();
                foreach (var c in candidates)
                    lastCandidateByTriplet[c.Triplet] = c;

                foreach (var kv in lastCandidateByTriplet)
                {
                    var t = kv.Key;
                    var lastSignal = kv.Value;

                    int ogBarIdx = pivotBarIndex[t.OgIdx];
                    if (ogBarIdx < 0) continue;

                    int barsAgo = CurrentBars[0] - lastSignal.BarIndex;
                    if (barsAgo >= 0)
                    {
                         newLevels.Add(new PatternFLevel
                        {
                            IsLong        = t.IsLong,
                            OgBarIndex    = ogBarIdx,
                            OgTime        = pivotTimes[t.OgIdx],
                            EntryBarIndex = lastSignal.BarIndex,
                            EntryTime     = Times[0][barsAgo],
                            Price         = t.OgPrice
                        });
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

		private int FindNextPivotCrossIdx(bool isLong, int startIdx, double level, double eps)
		{
		    int n = pivotPrices.Count;
		    for (int i = startIdx + 1; i < n; i++)
		    {
		        if (isLong)
		        {
		            if (!pivotIsHigh[i] && pivotGuidePrices[i] < level - eps)
		                return i;
		        }
		        else
		        {
		            if (pivotIsHigh[i] && pivotGuidePrices[i] > level + eps)
		                return i;
		        }
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

		// Gate for POTENTIAL Pattern F LONG:
		// Returns true once the pattern has "cleared/armed" (body high cleared support guide),
		// but aborts if it first breaks the TrendStart low.
		private bool ScanPatternFFromTriple_Long_PotentialGate(PatternFTriplet trip, double eps)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    double supportGuide = trip.SupportPrice;
		    double trendStartLow = trip.TrendStartPrice;
		
		    bool hasClearedSupport = false;
		
		    int trendStartBarIndex = BarsArray[0].GetBar(pivotTimes[trip.TrendStartIdx]);
		    if (trendStartBarIndex < 0) return false;

		    for (int b = trendStartBarIndex; b <= CurrentBars[0]; b++)
		    {
		        int barsAgo = CurrentBars[0] - b;
		        if (barsAgo < 0) continue;

		        double l = Lows[0][barsAgo];
		        double c = Closes[0][barsAgo];
		        double o = Opens[0][barsAgo];

		        double bodyHigh = Math.Max(o, c);

		        if (!hasClearedSupport)
		        {
		            if (l < trendStartLow - eps)
		                return false;

		            if (bodyHigh > supportGuide + eps)
		            {
		                hasClearedSupport = true;
		                break;
		            }
		        }
		    }
		
		    return hasClearedSupport;
		}
		
		// Gate for POTENTIAL Pattern F SHORT:
		// Returns true once the pattern has "cleared/armed" (body low cleared resistance guide),
		// but aborts if it first breaks the TrendStart high.
		private bool ScanPatternFFromTriple_Short_PotentialGate(PatternFTriplet trip, double eps)
		{
		    if (trip == null || !trip.IsActive)
		        return false;
		
		    double resistanceGuide = trip.SupportPrice;
		    double trendStartHigh  = trip.TrendStartPrice;
		
		    bool hasClearedResistance = false;
		
		    int trendStartBarIndex = BarsArray[0].GetBar(pivotTimes[trip.TrendStartIdx]);
		    if (trendStartBarIndex < 0) return false;

		    for (int b = trendStartBarIndex; b <= CurrentBars[0]; b++)
		    {
		        int barsAgo = CurrentBars[0] - b;
		        if (barsAgo < 0) continue;

		        double h = Highs[0][barsAgo];
		        double c = Closes[0][barsAgo];
		        double o = Opens[0][barsAgo];

		        double bodyLow = Math.Min(o, c);

		        if (!hasClearedResistance)
		        {
		            if (h > trendStartHigh + eps)
		                return false;

		            if (bodyLow < resistanceGuide - eps)
		            {
		                hasClearedResistance = true;
		                break;
		            }
		        }
		    }
		
		    return hasClearedResistance;
		
		}

		
		private bool ScanPatternFFromTriple_Long(
		    PatternFTriplet trip,
		    Dictionary<int, double> newLongByBar,
		    List<PatternFLevel> newLevels)
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
		    if (EnablePatternFSignal)
			{
			    if (true)
			    {
		            newLongByBar[entryBarIndex] = entryPrice - (PatternFMarkerOffsetTicks * TickSize);
                
                string plotKey = "L|" + trip.OgIdx + "|" + trip.SupportIdx + "|" + trip.TrendStartIdx + "_Historical";

                if (!recordedPatternKeys.Contains(plotKey))
                {
                    recordedPatternKeys.Add(plotKey);

                    int barsAgo = CurrentBar - entryBarIndex;
                    if (barsAgo >= 0 && barsAgo <= CurrentBar)
                    {
                        double preciseTime = 0;
                        if (livePreciseEndTimesL != null && livePreciseEndTimesL.ContainsKey(entryBarIndex))
                            preciseTime = livePreciseEndTimesL[entryBarIndex];
                        else if (PatternFLongEndTimestamp[barsAgo] > 0)
                            preciseTime = PatternFLongEndTimestamp[barsAgo];
                            
                        if (preciseTime == 0)
                            preciseTime = Times[0][barsAgo].ToOADate();
                            
                        PatternFLongLevel[barsAgo]          = ogLevel;
                        PatternFLongStartTimestamp[barsAgo] = pivotTimes[trip.OgIdx].ToOADate();
                        PatternFLongEndTimestamp[barsAgo]   = preciseTime;
						patternFLongSignalBarTimestamp[barsAgo] = Times[0][barsAgo].ToOADate();
                    }
                }
			    }
			}
		
		    // Level ends at entry (wick) pivot (so it does NOT extend to the right forever)
		    if (EnablePatternFLevels)
		    {
			    if (true)
			    {
		            newLevels.Add(new PatternFLevel
		        {
		            IsLong       = true,
		            OgBarIndex   = ogBarIndex,
		            OgTime       = pivotTimes[trip.OgIdx],
		            EntryBarIndex= entryBarIndex,
		            EntryTime    = pivotTimes[entryPivotIdx],
		            Price        = ogLevel
		        });
		        }
		    }
		
		    return true;
		}
		
		private bool ScanPatternFFromTriple_Short(
		    PatternFTriplet trip,
		    Dictionary<int, double> newShortByBar,
		    List<PatternFLevel> newLevels)
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
		    if (EnablePatternFSignal)
			{
			    if (true)
			    {
		            newShortByBar[entryBarIndex] = entryPrice + (PatternFMarkerOffsetTicks * TickSize);

                string plotKey = "S|" + trip.OgIdx + "|" + trip.SupportIdx + "|" + trip.TrendStartIdx + "_Historical";

                if (!recordedPatternKeys.Contains(plotKey))
                {
                    recordedPatternKeys.Add(plotKey);
                    
                    int barsAgo = CurrentBar - entryBarIndex;
                    if (barsAgo >= 0 && barsAgo <= CurrentBar)
                    {
                        double preciseTime = 0;
                        if (livePreciseEndTimesS != null && livePreciseEndTimesS.ContainsKey(entryBarIndex))
                            preciseTime = livePreciseEndTimesS[entryBarIndex];
                        else if (PatternFShortEndTimestamp[barsAgo] > 0)
                            preciseTime = PatternFShortEndTimestamp[barsAgo];
                            
                        if (preciseTime == 0)
                            preciseTime = Times[0][barsAgo].ToOADate();

                        PatternFShortLevel[barsAgo]          = ogLevel;
                        PatternFShortStartTimestamp[barsAgo] = pivotTimes[trip.OgIdx].ToOADate();
                        PatternFShortEndTimestamp[barsAgo]   = preciseTime;
						patternFShortSignalBarTimestamp[barsAgo] = Times[0][barsAgo].ToOADate();
                    }
                }
			    }
			}
		
		    // Level ends at entry (wick) pivot
		    if (EnablePatternFLevels)
		    {
			    if (true)
			    {
		            newLevels.Add(new PatternFLevel
		        {
		            IsLong       = false,
		            OgBarIndex   = ogBarIndex,
		            OgTime       = pivotTimes[trip.OgIdx],
		            EntryBarIndex= entryBarIndex,
		            EntryTime    = pivotTimes[entryPivotIdx],
		            Price        = ogLevel
		        });
		        }
		    }
		
		    return true;
		}
	


		private void DrawPatternFDebug(
		    Dictionary<int, double> newLongByBar,
		    Dictionary<int, double> newShortByBar,
		    List<PatternFLevel> newLevels)
		{
		    if (!CanDrawDebug() || !DrawPatternF)
		        return;
		
            // ---- New Debug Labels ----
            if (ShowDebugLabels)
            {
                 foreach (var kv in patternFLongByBar)
                 {
                    int barIndex = kv.Key;
                    int barsAgo = CurrentBar - barIndex;
                    if (barsAgo < 0 || barsAgo >= CurrentBar) continue;

                    double level = PatternFLongLevel[barsAgo];
                    if (level < 0.0001) continue; 

                    double startTs = PatternFLongStartTimestamp[barsAgo];
                    double endTs   = PatternFLongEndTimestamp[barsAgo];

                    DrawPatternFDetailLabel(true, barIndex, level, startTs, endTs);
                 }

                 foreach (var kv in patternFShortByBar)
                 {
                    int barIndex = kv.Key;
                    int barsAgo = CurrentBar - barIndex;
                    if (barsAgo < 0 || barsAgo >= CurrentBar) continue;

                    double level = PatternFShortLevel[barsAgo];
                    if (level < 0.0001) continue; 

                    double startTs = PatternFShortStartTimestamp[barsAgo];
                    double endTs   = PatternFShortEndTimestamp[barsAgo];

                    DrawPatternFDetailLabel(false, barIndex, level, startTs, endTs);
                 }
            }

		    // ---- SIGNALS (triangles) ----
		    // Clear stale markers (only if we have previous state)
		    if (patternFLongByBar != null)
		        foreach (var kv in patternFLongByBar.ToList())
		            if (!newLongByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNA_L_{kv.Key}");
		
		    if (patternFShortByBar != null)
		        foreach (var kv in patternFShortByBar.ToList())
		            if (!newShortByBar.ContainsKey(kv.Key))
		                RemoveDrawObject($"PATTERNA_S_{kv.Key}");
		
		    // Draw current (or none if disabled)
		    if (EnablePatternFSignal)
		    {
		        foreach (var kv in newLongByBar)
		            DrawPatternFText(true, kv.Key, kv.Value);
		
		        foreach (var kv in newShortByBar)
		            DrawPatternFText(false, kv.Key, kv.Value);
		    }
		    else
		    {
		        // If signals disabled, remove any existing markers
		        if (patternFLongByBar != null)
		            foreach (var kv in patternFLongByBar)
		                RemoveDrawObject($"PATTERNA_L_{kv.Key}");
		
		        if (patternFShortByBar != null)
		            foreach (var kv in patternFShortByBar)
		                RemoveDrawObject($"PATTERNA_S_{kv.Key}");
		    }
		
		    // ---- LEVELS (horizontal lines) ----
		    // For simplicity, clear prior drawn levels and redraw current.
		    // (Still cheap because this is debug-only.)
		    if (patternFLevels != null)
		    {
		        foreach (var lvl in patternFLevels)
		        {
		            string tagOld = lvl.IsLong
		                ? $"PATTERNA_LVL_L_{lvl.OgBarIndex}"
		                : $"PATTERNA_LVL_S_{lvl.OgBarIndex}";
		            RemoveDrawObject(tagOld);
		        }
		    }
		
		    if (EnablePatternFLevels)
		    {
		        foreach (var lvl in newLevels)
		            DrawPatternFLevelLine(lvl);
		    }
		}

        // ---- New Debug Labels ----
        private void DrawPatternFDetailLabel(bool isLong, int barIndex, double level, double startTsOA, double endTsOA)
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
            
             string tag = isLong ? $"PA_DET_L_{barIndex}" : $"PA_DET_S_{barIndex}";
             
             // Position: slightly above/below the level logic
             double y = isLong 
                ? Low[barsAgo] - (PatternFMarkerOffsetTicks + 15) * TickSize 
                : High[barsAgo] + (PatternFMarkerOffsetTicks + 15) * TickSize;

             Draw.Text(this, tag, false, text, barsAgo, y, 0,
                isLong ? PatternFLongColor : PatternFShortColor,
                new SimpleFont("Arial", 10),
                TextAlignment.Center,
                Brushes.Transparent,
                Brushes.Transparent,
                0);
        }
		
		private void DrawPatternFText(bool isLong, int barIndex, double price)
		{
		    if (!CanDrawDebug() || !DrawPatternF)
		        return;
		
		    if (barIndex < 0 || barIndex > CurrentBar)
		        return;
		
		    int barsAgo = CurrentBar - barIndex;
		    if (barsAgo < 0)
		        return;
		
		    string tag  = isLong ? $"PATTERNA_L_{barIndex}" : $"PATTERNA_S_{barIndex}";
		    string txt  = isLong ? "▲" : "▼";
		
		    Brush brush = isLong ? PatternFLongColor : PatternFShortColor;
		
		    // If you want to ensure updates replace old text, clear first
		    RemoveDrawObject(tag);
		
		    Draw.Text(this, tag, false,
		        txt,
		        barsAgo,
		        price,
		        0,
		        brush,
		        new SimpleFont("Arial", PatternFMarkerFontSize),
		        TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}
		
		private void DrawPatternFLevelLine(PatternFLevel lvl)
		{
		    if (!CanDrawDebug() || !DrawPatternF)
		        return;
		
		    if (lvl == null)
		        return;
		
		    string tag = lvl.IsLong
		        ? $"PATTERNA_LVL_L_{lvl.OgBarIndex}"
		        : $"PATTERNA_LVL_S_{lvl.OgBarIndex}";
		
		    Brush brush = lvl.IsLong ? PatternFLongColor : PatternFShortColor;
		
		    RemoveDrawObject(tag);
		
		    DateTime t1 = lvl.OgTime;
		    DateTime t2 = (lvl.EntryTime != default(DateTime)) ? lvl.EntryTime : Times[0][0];
		
		    Draw.Line(this, tag, false,
		        t1, lvl.Price,
		        t2, lvl.Price,
		        brush, PatternFLevelDashStyle, PatternFLevelWidth);
		}




		
		private double GetPatternFMarkerOffsetPrice(bool isLong)
		{
		    double ticks = PatternFMarkerOffsetTicks * TickSize;
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
//		    if (!ShowZigZagDebug && !DebugDrawPatternF /*&& !DebugDrawPatternF */)
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
		private AlightenMirrorPtFV0003[] cacheAlightenMirrorPtFV0003;
		public AlightenMirrorPtFV0003 AlightenMirrorPtFV0003(int barsToProcess, bool enablePatternFSignal, bool enablePatternFLevels, bool drawPatternF, int patternFMarkerFontSize, int patternFMarkerOffsetTicks, System.Windows.Media.Brush patternFLongColor, System.Windows.Media.Brush patternFShortColor, int patternFLevelWidth, DashStyleHelper patternFLevelDashStyle, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return AlightenMirrorPtFV0003(Input, barsToProcess, enablePatternFSignal, enablePatternFLevels, drawPatternF, patternFMarkerFontSize, patternFMarkerOffsetTicks, patternFLongColor, patternFShortColor, patternFLevelWidth, patternFLevelDashStyle, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public AlightenMirrorPtFV0003 AlightenMirrorPtFV0003(ISeries<double> input, int barsToProcess, bool enablePatternFSignal, bool enablePatternFLevels, bool drawPatternF, int patternFMarkerFontSize, int patternFMarkerOffsetTicks, System.Windows.Media.Brush patternFLongColor, System.Windows.Media.Brush patternFShortColor, int patternFLevelWidth, DashStyleHelper patternFLevelDashStyle, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			if (cacheAlightenMirrorPtFV0003 != null)
				for (int idx = 0; idx < cacheAlightenMirrorPtFV0003.Length; idx++)
					if (cacheAlightenMirrorPtFV0003[idx] != null && cacheAlightenMirrorPtFV0003[idx].BarsToProcess == barsToProcess && cacheAlightenMirrorPtFV0003[idx].EnablePatternFSignal == enablePatternFSignal && cacheAlightenMirrorPtFV0003[idx].EnablePatternFLevels == enablePatternFLevels && cacheAlightenMirrorPtFV0003[idx].DrawPatternF == drawPatternF && cacheAlightenMirrorPtFV0003[idx].PatternFMarkerFontSize == patternFMarkerFontSize && cacheAlightenMirrorPtFV0003[idx].PatternFMarkerOffsetTicks == patternFMarkerOffsetTicks && cacheAlightenMirrorPtFV0003[idx].PatternFLongColor == patternFLongColor && cacheAlightenMirrorPtFV0003[idx].PatternFShortColor == patternFShortColor && cacheAlightenMirrorPtFV0003[idx].PatternFLevelWidth == patternFLevelWidth && cacheAlightenMirrorPtFV0003[idx].PatternFLevelDashStyle == patternFLevelDashStyle && cacheAlightenMirrorPtFV0003[idx].ShowDebugLabels == showDebugLabels && cacheAlightenMirrorPtFV0003[idx].ShowZigZag == showZigZag && cacheAlightenMirrorPtFV0003[idx].ZigZagColor == zigZagColor && cacheAlightenMirrorPtFV0003[idx].ZigZagWidth == zigZagWidth && cacheAlightenMirrorPtFV0003[idx].ZigZagStyle == zigZagStyle && cacheAlightenMirrorPtFV0003[idx].EqualsInput(input))
						return cacheAlightenMirrorPtFV0003[idx];
			return CacheIndicator<AlightenMirrorPtFV0003>(new AlightenMirrorPtFV0003(){ BarsToProcess = barsToProcess, EnablePatternFSignal = enablePatternFSignal, EnablePatternFLevels = enablePatternFLevels, DrawPatternF = drawPatternF, PatternFMarkerFontSize = patternFMarkerFontSize, PatternFMarkerOffsetTicks = patternFMarkerOffsetTicks, PatternFLongColor = patternFLongColor, PatternFShortColor = patternFShortColor, PatternFLevelWidth = patternFLevelWidth, PatternFLevelDashStyle = patternFLevelDashStyle, ShowDebugLabels = showDebugLabels, ShowZigZag = showZigZag, ZigZagColor = zigZagColor, ZigZagWidth = zigZagWidth, ZigZagStyle = zigZagStyle }, input, ref cacheAlightenMirrorPtFV0003);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenMirrorPtFV0003 AlightenMirrorPtFV0003(int barsToProcess, bool enablePatternFSignal, bool enablePatternFLevels, bool drawPatternF, int patternFMarkerFontSize, int patternFMarkerOffsetTicks, System.Windows.Media.Brush patternFLongColor, System.Windows.Media.Brush patternFShortColor, int patternFLevelWidth, DashStyleHelper patternFLevelDashStyle, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenMirrorPtFV0003(Input, barsToProcess, enablePatternFSignal, enablePatternFLevels, drawPatternF, patternFMarkerFontSize, patternFMarkerOffsetTicks, patternFLongColor, patternFShortColor, patternFLevelWidth, patternFLevelDashStyle, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenMirrorPtFV0003 AlightenMirrorPtFV0003(ISeries<double> input , int barsToProcess, bool enablePatternFSignal, bool enablePatternFLevels, bool drawPatternF, int patternFMarkerFontSize, int patternFMarkerOffsetTicks, System.Windows.Media.Brush patternFLongColor, System.Windows.Media.Brush patternFShortColor, int patternFLevelWidth, DashStyleHelper patternFLevelDashStyle, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenMirrorPtFV0003(input, barsToProcess, enablePatternFSignal, enablePatternFLevels, drawPatternF, patternFMarkerFontSize, patternFMarkerOffsetTicks, patternFLongColor, patternFShortColor, patternFLevelWidth, patternFLevelDashStyle, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenMirrorPtFV0003 AlightenMirrorPtFV0003(int barsToProcess, bool enablePatternFSignal, bool enablePatternFLevels, bool drawPatternF, int patternFMarkerFontSize, int patternFMarkerOffsetTicks, System.Windows.Media.Brush patternFLongColor, System.Windows.Media.Brush patternFShortColor, int patternFLevelWidth, DashStyleHelper patternFLevelDashStyle, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenMirrorPtFV0003(Input, barsToProcess, enablePatternFSignal, enablePatternFLevels, drawPatternF, patternFMarkerFontSize, patternFMarkerOffsetTicks, patternFLongColor, patternFShortColor, patternFLevelWidth, patternFLevelDashStyle, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}

		public Indicators.AlightenMirrorPtFV0003 AlightenMirrorPtFV0003(ISeries<double> input , int barsToProcess, bool enablePatternFSignal, bool enablePatternFLevels, bool drawPatternF, int patternFMarkerFontSize, int patternFMarkerOffsetTicks, System.Windows.Media.Brush patternFLongColor, System.Windows.Media.Brush patternFShortColor, int patternFLevelWidth, DashStyleHelper patternFLevelDashStyle, bool showDebugLabels, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle)
		{
			return indicator.AlightenMirrorPtFV0003(input, barsToProcess, enablePatternFSignal, enablePatternFLevels, drawPatternF, patternFMarkerFontSize, patternFMarkerOffsetTicks, patternFLongColor, patternFShortColor, patternFLevelWidth, patternFLevelDashStyle, showDebugLabels, showZigZag, zigZagColor, zigZagWidth, zigZagStyle);
		}
	}
}

#endregion

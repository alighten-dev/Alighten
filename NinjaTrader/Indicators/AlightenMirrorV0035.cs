/* Version ID: 2026-07-24a AlightenMirrorV0035 — V0032 + PATTERN J: hosts the paired-pivot
   pattern source AlightenMirrorPtJV0006 (calc-only, all 47 ctor args, drawing suppressed)
   per timeframe as the SIXTH pattern (key "J", pattern index 5): levels = the nearest
   UNTESTED support/resistance (aligned with the standalone triangles; distance-capped),
   synced/drawn/researched exactly like A/B/G/H/F — per-TF windows, plots PtJD..PtJ5m
   (indices 35-41), "06.5 Pattern J Timeframes" visibility group, Pattern J Style dash
   (default DashDot), settings-modal row, research logging included automatically.
   Previous: V0031 + DAILY BIAS LEVELS: draws the last N
   Daily levels from the AlightenBiasV0003 pivot engine (ported inline on the Daily series,
   BIP 2 — AlightenBias exposes only its aggregate bias plot, so the pivot/level logic is
   replicated: two-bar color/extreme pivot rules, same-side pivots replaced by more extreme
   ones, level = the pivot bar's BODY extreme, last N confirmed pivots within the relevance
   window, developing pivot excluded). These are plain levels — no pattern/touch requirement —
   because Daily levels are respected on approach and the user wants to SEE them, not wait
   for pattern signals. Differentiated from Mirror pattern levels by their own style (group
   "11. Daily Bias Levels": solid width-3 lines, Goldenrod above price / DeepSkyBlue below,
   "DL <price>" labels); lines run from the pivot bar and re-extend to the current bar each
   primary bar. Previous: V0030 + Research Logging for confluence mining.
   Every confirmed signal is logged with (a) a snapshot of all other active levels at that moment
   and (b) forward MFE/MAE outcomes (15/30/60/120 min), time-to-target, and MAE-before-target.
   "Export Levels" additionally writes MirrorResearch_Events_*.csv and MirrorResearch_Context_*.csv,
   which feed tools\mine_mirror_signals.py to rank single signals and confluence pairs. */
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Controls;
using System.Windows.Automation;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{


    public class AlightenMirrorV0035 : Indicator
    {
		#region Class Variables
        private const int NUM_TF  = 7;
        private const int NUM_PAT = 6; // A, B, G, H, F, J

        private static readonly string[] _patternKeys = { "A", "B", "G", "H", "F", "J" };

        private class TrackedLevel
        {
            public string Tag;
            public double Price;
            public DateTime HtfStartTime;
            public DateTime HtfEndTime;
            public bool IsLong;
            public string PatternType; // "A", "B", "G", "H" or "F"
            public string Label;
            public int TfIndex; // 0..6
            public int LastUpdatedBip0Bar;
            public bool IsLive;
            public bool Removed; // set by invalidation cleanup (guards the sync fast-path cache)
            public bool ResearchLogged; // this level has produced a research event already
        }

        // ---- Research logging (confluence mining) ----
        // One event per CONFIRMED signal (closed HTF bar). Outcomes are updated on
        // every closed primary bar until the 120-minute horizon completes.
        private class ResearchEvent
        {
            public int Id;
            public DateTime Time;
            public string Pattern;
            public int TfIndex;
            public bool IsLong;
            public double LevelPrice;
            public double RefPrice;      // primary close when the signal confirmed

            public double MfeTicks;      // running favorable excursion
            public double MaeTicks;      // running adverse excursion
            public bool   TargetHit;
            public double TargetMinutes;          // minutes until MFE reached the target
            public double MaeBeforeTargetTicks;   // worst drawdown endured before target

            public double Mfe15, Mae15, Mfe30, Mae30, Mfe60, Mae60, Mfe120, Mae120;
            public bool Done;            // 120-minute horizon complete
        }
        private List<ResearchEvent> _researchOpen;
        private List<ResearchEvent> _researchDone;
        private List<string> _researchContext; // long-format: one row per active level per event
        private int _nextResearchId;

        // ---- Daily bias levels (AlightenBiasV0003 pivot engine ported inline on BIP 2) ----
        private List<int>      _dbPivotBars;    // daily-series bar index of each pivot
        private List<double>   _dbPivotPrices;  // pivot wick price (same-side replacement comparisons)
        private List<double>   _dbPivotGuides;  // BODY extreme of the pivot bar — this is the level
        private List<bool>     _dbPivotIsHigh;
        private List<DateTime> _dbPivotTimes;   // pivot daily-bar close time (line start)
        private int _dbLastProcessedBar = -1;   // newest daily bar already run through the pivot rules
        private int _dbDrawnSlots;              // level draw-object slots currently on the chart

        // Cached for RedrawDailyBiasLevels: it can run on the UI thread (ForceUISync /
        // settings modal), where series barsAgo indexing is invalid — only these cached
        // values (updated each primary bar in OnBarUpdate) may be read there.
        private double _dbLastDailyClose;       // last CLOSED daily bar's close (coloring)
        private DateTime _dbLastPrimaryTime;    // current primary bar time (line right edge)

        // PERF: per-slot sync cache — when the same (start, price) signal re-syncs
        // tick after tick, skip the tag string building + dictionary lookup entirely.
        // Slots: [pattern][tf][direction][barsAgoOnHtf(0|1)]
        private class SyncSlot
        {
            public long StartTicks;
            public double Price;
            public TrackedLevel Level;
        }
        private SyncSlot[] _syncCache;
        private const int MAX_SYNC_AGO = 3;   // deepest closed-HTF-bar scanned by SyncLevels

        // PERF: levels whose HtfEndTime is older than MirrorLookbackBars HTF bars are
        // moved here. They keep their chart drawings but stop costing per-tick scans.
        private List<TrackedLevel> _archivedLevels = new List<TrackedLevel>(256);

        private int _lastSyncMs;

        private AlightenMirrorPtAV0010[] _srcA = new AlightenMirrorPtAV0010[NUM_TF];
        private AlightenMirrorPtBV0005[] _srcB = new AlightenMirrorPtBV0005[NUM_TF];
        private AlightenMirrorPtGV0002[] _srcG = new AlightenMirrorPtGV0002[NUM_TF];
        private AlightenMirrorPtHV0002[] _srcH = new AlightenMirrorPtHV0002[NUM_TF];
		private AlightenMirrorPtFV0003[] _srcF = new AlightenMirrorPtFV0003[NUM_TF];
		private AlightenMirrorPtJV0006[] _srcJ = new AlightenMirrorPtJV0006[NUM_TF];

        // Unified tracked-level store: tracked[pattern][tf]
        private Dictionary<string, TrackedLevel>[][] tracked = new Dictionary<string, TrackedLevel>[NUM_PAT][];

        private string[] _tfLabels = { "D", "240m", "60m", "30m", "15m", "10m", "5m" };
        private int[] _tfMinutes = { 1440, 240, 60, 30, 15, 10, 5 };

		// Cleanup
		private DateTime[] _lastSeenHtfBarTime = new DateTime[NUM_TF];
		private DateTime _lastPrimaryBarTime = Core.Globals.MinDate;
		private double   _lastPrimaryClose   = double.NaN;
		private double   _lastPrimaryHigh    = double.NaN;
		private double   _lastPrimaryLow     = double.NaN;

		// Toolbar & UI
		private NinjaTrader.Gui.Chart.Chart chartWindow;
        private Button settingsButton;
        private Button exportButton;
        private Window settingsWindow;


		#endregion

		#region Properties
        // ---- Pattern A plots (0..6) ----
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtAD => Values[0];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtA240m => Values[1];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtA60m => Values[2];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtA30m => Values[3];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtA15m => Values[4];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtA10m => Values[5];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtA5m => Values[6];

        // ---- Pattern B plots (7..13) ----
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtBD => Values[7];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtB240m => Values[8];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtB60m => Values[9];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtB30m => Values[10];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtB15m => Values[11];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtB10m => Values[12];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtB5m => Values[13];

        // ---- Pattern G plots (14..20) ----
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtGD => Values[14];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtG240m => Values[15];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtG60m => Values[16];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtG30m => Values[17];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtG15m => Values[18];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtG10m => Values[19];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtG5m => Values[20];

        // ---- Pattern H plots (21..27) ----
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtHD => Values[21];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtH240m => Values[22];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtH60m => Values[23];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtH30m => Values[24];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtH15m => Values[25];
        [Browsable(false)]
        [XmlIgnore()]
        public Series<double> PtH10m => Values[26];
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PtH5m => Values[27];

        // ---- Pattern F plots (28..34) ----
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PtFD => Values[28];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PtF240m => Values[29];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PtF60m => Values[30];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PtF30m => Values[31];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PtF15m => Values[32];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PtF10m => Values[33];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PtF5m => Values[34];

        [NinjaScriptProperty]
        [Display(Name="Enable Pattern A", Order=1, GroupName="01. Patterns")]
        public bool EnablePatternA { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Pattern B", Order=2, GroupName="01. Patterns")]
        public bool EnablePatternB { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Pattern G", Order=3, GroupName="01. Patterns")]
        public bool EnablePatternG { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Pattern H", Order=4, GroupName="01. Patterns")]
        public bool EnablePatternH { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Enable Pattern F", Order=5, GroupName="01. Patterns")]
		public bool EnablePatternF { get; set; }

		[NinjaScriptProperty]
		[Display(Name="Enable Pattern J", Description="Paired-pivot pattern (AlightenMirrorPtJV0006): levels are the nearest untested support/resistance — where the Pattern J test triangles fire.", Order=5, GroupName="01. Patterns")]
		public bool EnablePatternJ { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Src Bars To Process", Order=6, GroupName="01. Patterns")]
        public int SrcBarsToProcess { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name="Mirror Lookback Bars", Order=6, GroupName="01. Patterns")]
		public int MirrorLookbackBars { get; set; }

		[NinjaScriptProperty]
        [Display(Name="Enable Invalidated Cleanup", Order=7, GroupName="01. Patterns")]
        public bool EnableInvalidatedCleanup { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Daily", Order=1, GroupName="02. Pattern A Timeframes")]
        public bool ShowPatternATF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show 240m", Order=2, GroupName="02. Pattern A Timeframes")]
        public bool ShowPatternATF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show 60m", Order=3, GroupName="02. Pattern A Timeframes")]
        public bool ShowPatternATF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show 30m", Order=4, GroupName="02. Pattern A Timeframes")]
        public bool ShowPatternATF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show 15m", Order=5, GroupName="02. Pattern A Timeframes")]
        public bool ShowPatternATF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show 10m", Order=6, GroupName="02. Pattern A Timeframes")]
        public bool ShowPatternATF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show 5m", Order=7, GroupName="02. Pattern A Timeframes")]
        public bool ShowPatternATF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Daily", Order=1, GroupName="03. Pattern B Timeframes")]
        public bool ShowPatternBTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show 240m", Order=2, GroupName="03. Pattern B Timeframes")]
        public bool ShowPatternBTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show 60m", Order=3, GroupName="03. Pattern B Timeframes")]
        public bool ShowPatternBTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show 30m", Order=4, GroupName="03. Pattern B Timeframes")]
        public bool ShowPatternBTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show 15m", Order=5, GroupName="03. Pattern B Timeframes")]
        public bool ShowPatternBTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show 10m", Order=6, GroupName="03. Pattern B Timeframes")]
        public bool ShowPatternBTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show 5m", Order=7, GroupName="03. Pattern B Timeframes")]
        public bool ShowPatternBTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Daily", Order=1, GroupName="04. Pattern G Timeframes")]
        public bool ShowPatternGTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show 240m", Order=2, GroupName="04. Pattern G Timeframes")]
        public bool ShowPatternGTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show 60m", Order=3, GroupName="04. Pattern G Timeframes")]
        public bool ShowPatternGTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show 30m", Order=4, GroupName="04. Pattern G Timeframes")]
        public bool ShowPatternGTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show 15m", Order=5, GroupName="04. Pattern G Timeframes")]
        public bool ShowPatternGTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show 10m", Order=6, GroupName="04. Pattern G Timeframes")]
        public bool ShowPatternGTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show 5m", Order=7, GroupName="04. Pattern G Timeframes")]
        public bool ShowPatternGTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Daily", Order=1, GroupName="05. Pattern H Timeframes")]
        public bool ShowPatternHTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show 240m", Order=2, GroupName="05. Pattern H Timeframes")]
        public bool ShowPatternHTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show 60m", Order=3, GroupName="05. Pattern H Timeframes")]
        public bool ShowPatternHTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show 30m", Order=4, GroupName="05. Pattern H Timeframes")]
        public bool ShowPatternHTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show 15m", Order=5, GroupName="05. Pattern H Timeframes")]
        public bool ShowPatternHTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show 10m", Order=6, GroupName="05. Pattern H Timeframes")]
        public bool ShowPatternHTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show 5m", Order=7, GroupName="05. Pattern H Timeframes")]
        public bool ShowPatternHTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Daily", Order=1, GroupName="06. Pattern F Timeframes")]
        public bool ShowPatternFTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show 240m", Order=2, GroupName="06. Pattern F Timeframes")]
        public bool ShowPatternFTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show 60m", Order=3, GroupName="06. Pattern F Timeframes")]
        public bool ShowPatternFTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show 30m", Order=4, GroupName="06. Pattern F Timeframes")]
        public bool ShowPatternFTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show 15m", Order=5, GroupName="06. Pattern F Timeframes")]
        public bool ShowPatternFTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show 10m", Order=6, GroupName="06. Pattern F Timeframes")]
        public bool ShowPatternFTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show 5m", Order=7, GroupName="06. Pattern F Timeframes")]
        public bool ShowPatternFTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Daily", Order=1, GroupName="06.5 Pattern J Timeframes")]
        public bool ShowPatternJTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show 240m", Order=2, GroupName="06.5 Pattern J Timeframes")]
        public bool ShowPatternJTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show 60m", Order=3, GroupName="06.5 Pattern J Timeframes")]
        public bool ShowPatternJTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show 30m", Order=4, GroupName="06.5 Pattern J Timeframes")]
        public bool ShowPatternJTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show 15m", Order=5, GroupName="06.5 Pattern J Timeframes")]
        public bool ShowPatternJTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show 10m", Order=6, GroupName="06.5 Pattern J Timeframes")]
        public bool ShowPatternJTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show 5m", Order=7, GroupName="06.5 Pattern J Timeframes")]
        public bool ShowPatternJTF7_5m { get; set; }



        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF1 Color", Order=1, GroupName="07. Mirror Colors")]
        public Brush ColorTF1 { get; set; }
        [Browsable(false)]
        public string ColorTF1Serialize { get { return Serialize.BrushToString(ColorTF1); } set { ColorTF1 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF2 Color", Order=2, GroupName="07. Mirror Colors")]
        public Brush ColorTF2 { get; set; }
        [Browsable(false)]
        public string ColorTF2Serialize { get { return Serialize.BrushToString(ColorTF2); } set { ColorTF2 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF3 Color", Order=3, GroupName="07. Mirror Colors")]
        public Brush ColorTF3 { get; set; }
        [Browsable(false)]
        public string ColorTF3Serialize { get { return Serialize.BrushToString(ColorTF3); } set { ColorTF3 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF4 Color", Order=4, GroupName="07. Mirror Colors")]
        public Brush ColorTF4 { get; set; }
        [Browsable(false)]
        public string ColorTF4Serialize { get { return Serialize.BrushToString(ColorTF4); } set { ColorTF4 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF5 Color", Order=5, GroupName="07. Mirror Colors")]
        public Brush ColorTF5 { get; set; }
        [Browsable(false)]
        public string ColorTF5Serialize { get { return Serialize.BrushToString(ColorTF5); } set { ColorTF5 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF6 Color", Order=6, GroupName="07. Mirror Colors")]
        public Brush ColorTF6 { get; set; }
        [Browsable(false)]
        public string ColorTF6Serialize { get { return Serialize.BrushToString(ColorTF6); } set { ColorTF6 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF7 Color", Order=7, GroupName="07. Mirror Colors")]
        public Brush ColorTF7 { get; set; }
        [Browsable(false)]
        public string ColorTF7Serialize { get { return Serialize.BrushToString(ColorTF7); } set { ColorTF7 = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Level Width", Order=1, GroupName="08. Mirror Visuals")]
        public int LevelWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern A Style", Order=2, GroupName="08. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleA { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern B Style", Order=3, GroupName="08. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleB { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern G Style", Order=4, GroupName="08. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleG { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern H Style", Order=5, GroupName="08. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleH { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern F Style", Order=6, GroupName="08. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleF { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern J Style", Order=7, GroupName="08. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleJ { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show Level Labels", Order=10, GroupName="08. Mirror Visuals")]
        public bool ShowLevelLabels { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name="Realtime Sync Throttle (ms)", Description="0 = sync levels on every tick. Higher values reduce CPU by syncing at most once per interval (signal plots still update every tick).", Order=1, GroupName="09. Performance")]
        public int SyncThrottleMs { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Research Log", Description="Log every confirmed signal with confluence context and forward MFE/MAE outcomes. Written by the Export Levels button as MirrorResearch_*.csv.", Order=1, GroupName="10. Research")]
        public bool EnableResearchLog { get; set; }

        [NinjaScriptProperty]
        [Range(10, 2000)]
        [Display(Name="Research Target (ticks)", Description="Favorable move that counts as a win (time-to-target and MAE-before-target are recorded against this).", Order=2, GroupName="10. Research")]
        public int ResearchTargetTicks { get; set; }

        // ---- Daily bias levels (AlightenBiasV0003 pivot engine, display only) ----
        [Display(Name="Enable Daily Bias Levels", Description="Draw the last N Daily levels from the AlightenBiasV0003 pivot engine (level = pivot bar's body extreme). Plain levels — no pattern/touch requirement.", Order=1, GroupName="11. Daily Bias Levels")]
        public bool EnableDailyBiasLevels { get; set; }

        [Range(1, 20)]
        [Display(Name="Number Of Levels (N)", Description="How many of the most recent confirmed Daily levels to draw (matches AlightenBiasV0003 NumberOfLevels).", Order=2, GroupName="11. Daily Bias Levels")]
        public int DailyBiasLevelCount { get; set; }

        [Range(1, 10000)]
        [Display(Name="Relevance (daily bars)", Description="Ignore pivots older than this many Daily bars (matches AlightenBiasV0003 RelevanceFactor).", Order=3, GroupName="11. Daily Bias Levels")]
        public int DailyBiasRelevanceDays { get; set; }

        [XmlIgnore]
        [Display(Name="Above-Price Color", Order=4, GroupName="11. Daily Bias Levels")]
        public Brush DailyBiasAboveColor { get; set; }
        [Browsable(false)]
        public string DailyBiasAboveColorSerialize { get { return Serialize.BrushToString(DailyBiasAboveColor); } set { DailyBiasAboveColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="Below-Price Color", Order=5, GroupName="11. Daily Bias Levels")]
        public Brush DailyBiasBelowColor { get; set; }
        [Browsable(false)]
        public string DailyBiasBelowColorSerialize { get { return Serialize.BrushToString(DailyBiasBelowColor); } set { DailyBiasBelowColor = Serialize.StringToBrush(value); } }

        [Range(1, 10)]
        [Display(Name="Line Width", Order=6, GroupName="11. Daily Bias Levels")]
        public int DailyBiasLevelWidth { get; set; }

        [Display(Name="Line Dash", Order=7, GroupName="11. Daily Bias Levels")]
        public DashStyleHelper DailyBiasLevelDash { get; set; }

        [Display(Name="Show Labels", Description="Draw a 'DL <price>' label at the right end of each level line.", Order=8, GroupName="11. Daily Bias Levels")]
        public bool ShowDailyBiasLabels { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name="Label Font Size", Order=11, GroupName="08. Mirror Visuals")]
        public int LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Label Offset Ticks", Order=12, GroupName="08. Mirror Visuals")]
        public int LabelOffsetTicks { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AlightenMirrorV0035";
                Description = "Multi-Timeframe Mirror for Patterns A, B, G, H, and F. v32: draws the last N Daily levels from the AlightenBiasV0003 pivot engine (ported inline, group 11 settings) — plain levels, no pattern requirement. Includes v31 research logging and the v30 perf work.";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = false;
                ShowTransparentPlotsInDataBox = true;

                EnablePatternA = true;
                EnablePatternB = true;
                EnablePatternG = true;
                EnablePatternH = true;
				EnablePatternF = true;
				EnablePatternJ = true;
                SrcBarsToProcess = 500;
				MirrorLookbackBars = 3;
				EnableInvalidatedCleanup = true;

                // Visibility defaults per the user's saved template (2026-07-25):
                // J shows everything except 5m; H adds 30m; A/B/G/F show Daily/240m/60m only.
                ShowPatternATF1_Daily = true;
                ShowPatternATF2_240m = true;
                ShowPatternATF3_60m = true;
                ShowPatternATF4_30m = false;
                ShowPatternATF5_15m = false;
                ShowPatternATF6_10m = false;
                ShowPatternATF7_5m = false;
                ShowPatternBTF1_Daily = true;
                ShowPatternBTF2_240m = true;
                ShowPatternBTF3_60m = true;
                ShowPatternBTF4_30m = false;
                ShowPatternBTF5_15m = false;
                ShowPatternBTF6_10m = false;
                ShowPatternBTF7_5m = false;
                ShowPatternGTF1_Daily = true;
                ShowPatternGTF2_240m = true;
                ShowPatternGTF3_60m = true;
                ShowPatternGTF4_30m = false;
                ShowPatternGTF5_15m = false;
                ShowPatternGTF6_10m = false;
                ShowPatternGTF7_5m = false;
                ShowPatternHTF1_Daily = true;
                ShowPatternHTF2_240m = true;
                ShowPatternHTF3_60m = true;
                ShowPatternHTF4_30m = true;
                ShowPatternHTF5_15m = false;
                ShowPatternHTF6_10m = false;
                ShowPatternHTF7_5m = false;
                ShowPatternFTF1_Daily = true;
                ShowPatternFTF2_240m = true;
                ShowPatternFTF3_60m = true;
                ShowPatternFTF4_30m = false;
                ShowPatternFTF5_15m = false;
                ShowPatternFTF6_10m = false;
                ShowPatternFTF7_5m = false;
                ShowPatternJTF1_Daily = true; ShowPatternJTF2_240m = true; ShowPatternJTF3_60m = true; ShowPatternJTF4_30m = true; ShowPatternJTF5_15m = true; ShowPatternJTF6_10m = true; ShowPatternJTF7_5m = false;


                ColorTF1 = Brushes.White;
                ColorTF2 = Brushes.Yellow;
                ColorTF3 = Brushes.Orange;
                ColorTF4 = Brushes.Magenta;
                ColorTF5 = Brushes.DodgerBlue;
                ColorTF6 = Brushes.Cyan;
                ColorTF7 = Brushes.Lime;

                LevelWidth = 3;
                LevelDashStyleA = DashStyleHelper.Dot;
                LevelDashStyleB = DashStyleHelper.DashDot;
                LevelDashStyleG = DashStyleHelper.Solid;
                LevelDashStyleH = DashStyleHelper.Dash;
                LevelDashStyleF = DashStyleHelper.DashDotDot;
                LevelDashStyleJ = DashStyleHelper.DashDot;

                ShowLevelLabels = true;
                LabelFontSize = 12;
                LabelOffsetTicks = 5;
                SyncThrottleMs = 0;
                EnableResearchLog = true;
                ResearchTargetTicks = 100;

                EnableDailyBiasLevels = true;
                DailyBiasLevelCount = 4;
                DailyBiasRelevanceDays = 300;
                DailyBiasAboveColor = Brushes.Goldenrod;
                DailyBiasBelowColor = Brushes.DeepSkyBlue;
                DailyBiasLevelWidth = 3;
                DailyBiasLevelDash = DashStyleHelper.Solid;
                ShowDailyBiasLabels = true;


                AddPlot(Brushes.Transparent, "PtAD");
                AddPlot(Brushes.Transparent, "PtA240m");
                AddPlot(Brushes.Transparent, "PtA60m");
                AddPlot(Brushes.Transparent, "PtA30m");
                AddPlot(Brushes.Transparent, "PtA15m");
                AddPlot(Brushes.Transparent, "PtA10m");
                AddPlot(Brushes.Transparent, "PtA5m");
                AddPlot(Brushes.Transparent, "PtBD");
                AddPlot(Brushes.Transparent, "PtB240m");
                AddPlot(Brushes.Transparent, "PtB60m");
                AddPlot(Brushes.Transparent, "PtB30m");
                AddPlot(Brushes.Transparent, "PtB15m");
                AddPlot(Brushes.Transparent, "PtB10m");
                AddPlot(Brushes.Transparent, "PtB5m");
                AddPlot(Brushes.Transparent, "PtGD");
                AddPlot(Brushes.Transparent, "PtG240m");
                AddPlot(Brushes.Transparent, "PtG60m");
                AddPlot(Brushes.Transparent, "PtG30m");
                AddPlot(Brushes.Transparent, "PtG15m");
                AddPlot(Brushes.Transparent, "PtG10m");
                AddPlot(Brushes.Transparent, "PtG5m");
                AddPlot(Brushes.Transparent, "PtHD");
                AddPlot(Brushes.Transparent, "PtH240m");
                AddPlot(Brushes.Transparent, "PtH60m");
                AddPlot(Brushes.Transparent, "PtH30m");
                AddPlot(Brushes.Transparent, "PtH15m");
                AddPlot(Brushes.Transparent, "PtH10m");
                AddPlot(Brushes.Transparent, "PtH5m");
                AddPlot(Brushes.Transparent, "PtFD");
                AddPlot(Brushes.Transparent, "PtF240m");
                AddPlot(Brushes.Transparent, "PtF60m");
                AddPlot(Brushes.Transparent, "PtF30m");
                AddPlot(Brushes.Transparent, "PtF15m");
                AddPlot(Brushes.Transparent, "PtF10m");
                AddPlot(Brushes.Transparent, "PtF5m");
                AddPlot(Brushes.Transparent, "PtJD");
                AddPlot(Brushes.Transparent, "PtJ240m");
                AddPlot(Brushes.Transparent, "PtJ60m");
                AddPlot(Brushes.Transparent, "PtJ30m");
                AddPlot(Brushes.Transparent, "PtJ15m");
                AddPlot(Brushes.Transparent, "PtJ10m");
                AddPlot(Brushes.Transparent, "PtJ5m");
            }
            else if (State == State.Configure)
			{
			    // BIP 1: Tick series
			    AddDataSeries(BarsPeriodType.Tick, 1);


			    // BIP 2: Daily
			    var dailyPeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Day,
			        Value          = 1
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: dailyPeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );

			    // BIP 3: 240-minute
			    var fourHourPeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Minute,
			        Value          = 240
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: fourHourPeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );

			    // BIP 4: 60-minute
			    var oneHourPeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Minute,
			        Value          = 60
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: oneHourPeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );

			    // BIP 5: 30-minute
			    var thirtyMinutePeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Minute,
			        Value          = 30
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: thirtyMinutePeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );

			    // BIP 6: 15-minute
			    var fifteenMinutePeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Minute,
			        Value          = 15
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: fifteenMinutePeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );

			    // BIP 7: 10-minute
			    AddDataSeries(BarsPeriodType.Minute, 10);

			    // BIP 8: 5-minute
			    AddDataSeries(BarsPeriodType.Minute, 5);
			}
            else if (State == State.DataLoaded)
            {
                for (int p = 0; p < NUM_PAT; p++)
                {
                    tracked[p] = new Dictionary<string, TrackedLevel>[NUM_TF];
                    for (int t = 0; t < NUM_TF; t++)
                        tracked[p][t] = new Dictionary<string, TrackedLevel>();
                }

                // V0035: slots cover barsAgo 0..MAX_SYNC_AGO (was 0..1) so PtJV0006's
                // retro back-stamped tests (pairs confirming 2+ HTF bars late) are
                // picked up by the deeper SyncLevels scan.
                _syncCache = new SyncSlot[NUM_PAT * NUM_TF * 2 * (MAX_SYNC_AGO + 1)];
                for (int i = 0; i < _syncCache.Length; i++)
                    _syncCache[i] = new SyncSlot();

                _researchOpen    = new List<ResearchEvent>(64);
                _researchDone    = new List<ResearchEvent>(1024);
                _researchContext = new List<string>(4096);

                _dbPivotBars   = new List<int>(512);
                _dbPivotPrices = new List<double>(512);
                _dbPivotGuides = new List<double>(512);
                _dbPivotIsHigh = new List<bool>(512);
                _dbPivotTimes  = new List<DateTime>(512);
                _dbLastProcessedBar = -1;
                _dbDrawnSlots = 0;
                _nextResearchId  = 0;

                for (int t = 0; t < NUM_TF; t++)
                {
					_lastSeenHtfBarTime[t] = Core.Globals.MinDate;

                    if (!IsTfEnabled(t)) continue;

                    int bipIdx = t + 2;

                    if (EnablePatternA)
                    {
                        _srcA[t] = AlightenMirrorPtAV0010(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternB)
                    {
                        _srcB[t] = AlightenMirrorPtBV0005(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, true, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternG)
                    {
                        _srcG[t] = AlightenMirrorPtGV0002(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternH)
                    {
                        _srcH[t] = AlightenMirrorPtHV0002(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternF)
                    {
                        _srcF[t] = AlightenMirrorPtFV0003(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternJ)
                    {
                        // Pattern J: paired-pivot engine (PtJV0005). Calc-only hosted —
                        // all its own drawing/labels/legacy display off; the Mirror draws
                        // the levels it reports (nearest untested support/resistance).
                        _srcJ[t] = AlightenMirrorPtJV0006(
                            Closes[bipIdx],
                            SrcBarsToProcess,                      // barsToProcess
                            true, false,                           // confirmedPairsOnly, useWicksForPairLevels (body levels)
                            3, 1,                                  // nearestPairLineWidth, pairWidthStep (unused: calc-only)
                            DashStyleHelper.Solid, DashStyleHelper.Dot,
                            3, 3,                                  // up/down pairs to draw (rank sizing only)
                            2000, 300,                             // pairLookbackBars, maxStoredPivots
                            1, 0,                                  // minimumTrendBars, minimumTrendTicks
                            Colors.LimeGreen, Colors.Magenta, Colors.Goldenrod, Colors.Red,
                            false,                                 // useWicksForGainLoss
                            0, 0, 0,                               // gainLoss/testTouch/testCloseHold ticks
                            false,                                 // showTestedPairLevels
                            Colors.LimeGreen, Colors.Red,          // test dot colors (unused)
                            false,                                 // showLegacyGainedLostLevels
                            3, 500, 50, 2, 2,                      // legacy sizing
                            Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent,
                            "Delete",                              // legacyCloseThroughAction
                            false,                                 // showLegacyFirstTouchDots
                            Colors.Transparent, Colors.Transparent,
                            false,                                 // showPairLevelLabels
                            2, 0, 11,                              // label layout (unused)
                            4,                                     // sg/rl pair line width (unused)
                            false, Colors.Gold, 2,                 // zigzag off
                            true,                                  // calcOnlyMode
                            0                                      // maxReportDistanceTicks: always off —
                                                                   // a reported J level is one this bar's wick
                                                                   // touched; the old 400t cap deleted exactly
                                                                   // the biggest rejections (28630 on 7/24)
                        );
                    }
                }




				if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => { CreateToolbarButton(); });
                }
            }
			else if (State == State.Terminated)
            {
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => { TryRemoveToolbarButton(); });
                }
            }
        }

        #region Pattern accessors (unified indexing: 0=A, 1=B, 2=G, 3=H, 4=F)

        private bool IsPatternEnabled(int p)
        {
            switch (p)
            {
                case 0: return EnablePatternA;
                case 1: return EnablePatternB;
                case 2: return EnablePatternG;
                case 3: return EnablePatternH;
                case 4: return EnablePatternF;
                case 5: return EnablePatternJ;
            }
            return false;
        }

        private Series<double> GetLongLevelSeries(int p, int t)
        {
            switch (p)
            {
                case 0: return _srcA[t] != null ? _srcA[t].PatternALongLevel : null;
                case 1: return _srcB[t] != null ? _srcB[t].PatternBLongLevel : null;
                case 2: return _srcG[t] != null ? _srcG[t].PatternGLongLevel : null;
                case 3: return _srcH[t] != null ? _srcH[t].PatternHLongLevel : null;
                case 4: return _srcF[t] != null ? _srcF[t].PatternFLongLevel : null;
                case 5: return _srcJ[t] != null ? _srcJ[t].PatternJLongLevel : null;
            }
            return null;
        }

        private Series<double> GetShortLevelSeries(int p, int t)
        {
            switch (p)
            {
                case 0: return _srcA[t] != null ? _srcA[t].PatternAShortLevel : null;
                case 1: return _srcB[t] != null ? _srcB[t].PatternBShortLevel : null;
                case 2: return _srcG[t] != null ? _srcG[t].PatternGShortLevel : null;
                case 3: return _srcH[t] != null ? _srcH[t].PatternHShortLevel : null;
                case 4: return _srcF[t] != null ? _srcF[t].PatternFShortLevel : null;
                case 5: return _srcJ[t] != null ? _srcJ[t].PatternJShortLevel : null;
            }
            return null;
        }

        private DashStyleHelper GetPatternDash(string pType)
        {
            switch (pType)
            {
                case "A": return LevelDashStyleA;
                case "B": return LevelDashStyleB;
                case "G": return LevelDashStyleG;
                case "H": return LevelDashStyleH;
                case "F": return LevelDashStyleF;
                case "J": return LevelDashStyleJ;
            }
            return LevelDashStyleA;
        }

        #endregion

        protected override void OnBarUpdate()
		{
		   	if (BarsInProgress < 0 || BarsInProgress >= BarsArray.Length)
		        return;

		    if (CurrentBars[0] < 1)
		        return;

		    for (int i = 1; i < BarsArray.Length; i++)
		        if (CurrentBars[i] < 1)
		            return;

		    // PERF: all host logic runs on primary-series (BIP0) events only — previously
		    // the cleanup block below also ran for every event of all 8 secondary series.
		    // Child pattern indicators update from their own input series regardless, and
		    // the HTF cleanup check reads Times[bipIdx][0] which is current from BIP0.
		    // Daily bias levels: run the pivot rules on each CLOSED Daily bar (BIP 2).
		    // The daily series preloads far MORE history than the primary chart shows, and
		    // those early daily events are discarded by the CurrentBars[0] guard above — so
		    // every call catches up ALL unprocessed bars via barsAgo indexing (the first one
		    // walks the entire preloaded daily history; after that it is one bar per day).
		    if (BarsInProgress == 2 && EnableDailyBiasLevels)
		    {
		        try
		        {
		            // Historical: the current daily bar [0] is complete. Realtime: it is forming.
		            CatchUpDailyBias(State == State.Historical ? CurrentBars[2] : CurrentBars[2] - 1);
		        }
		        catch (Exception ex) { Print("[MirrorV0034] daily-bias engine: " + ex.Message); }
		        return;
		    }

		    if (BarsInProgress != 0)
		        return;

			if (BarsInProgress == 0)
			{
                try
                {
                    for (int i = 0; i < NUM_PAT * NUM_TF; i++)
                    {
                        if (Values[i].IsValidDataPoint(0))
                            Values[i][0] = 0;
                    }

			        _lastPrimaryBarTime = Times[0][0];
			        _lastPrimaryClose   = Closes[0][0];
			        _lastPrimaryHigh    = Highs[0][0];
			        _lastPrimaryLow     = Lows[0][0];


                    // PERF: optional realtime throttle — level syncing can be limited to
                    // once per interval. Signal plot emission below still runs on every
                    // tick from the tracked dictionaries, so plots stay tick-accurate.
                    bool doSync = true;
                    if (State == State.Realtime && SyncThrottleMs > 0 && !IsFirstTickOfBar)
                    {
                        int nowMs = Environment.TickCount;
                        if (unchecked(nowMs - _lastSyncMs) < SyncThrottleMs)
                            doSync = false;
                        else
                            _lastSyncMs = nowMs;
                    }

                    // Emit Signal Plots for Strategy/Bloodhound
                    if (doSync)
                        SyncLevels();

                    // PERF: archive stale levels once per primary bar
                    if (IsFirstTickOfBar)
                        ArchiveStaleLevels();

                    // Research: update forward outcomes from the just-closed primary bar
                    if (EnableResearchLog && IsFirstTickOfBar && CurrentBars[0] >= 1)
                        UpdateResearchEvents();

                    // Daily bias levels: re-extend lines to the current bar + refresh
                    // above/below-price coloring once per primary bar. The catch-up also
                    // runs from here so short charts (few or no daily closes inside the
                    // loaded primary window) still process the preloaded daily history.
                    if (IsFirstTickOfBar && EnableDailyBiasLevels)
                    {
                        CatchUpDailyBias(CurrentBars[2] - 1);

                        // Cache the series values here (data thread) — the redraw itself
                        // must stay series-free so the UI thread can call it too.
                        _dbLastPrimaryTime = Times[0][0];
                        _dbLastDailyClose  = State == State.Historical || CurrentBars[2] < 1
                                               ? Closes[2][0] : Closes[2][1];

                        RedrawDailyBiasLevels();
                    }

                    DateTime now = Times[0][0];
                    for (int p = 0; p < NUM_PAT; p++)
                    {
                        if (!IsPatternEnabled(p)) continue;

                        for (int t = 0; t < NUM_TF; t++)
                        {
                            var tDict = tracked[p][t];
                            if (tDict == null || tDict.Count == 0) continue;

                            // Single pass instead of two LINQ Any() scans
                            bool hasLong = false, hasShort = false;
                            foreach (var x in tDict.Values)
                            {
                                if (now < x.HtfStartTime || now > x.HtfEndTime) continue;
                                if (x.IsLong) hasLong = true; else hasShort = true;
                                if (hasLong && hasShort) break;
                            }

                            if (hasLong) { Values[p * NUM_TF + t][0] = 1; }
                            else if (hasShort) { Values[p * NUM_TF + t][0] = -1; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Print("[MirrorV0034] CRASH in BIP 0: " + ex.ToString());
                }
			}

            if (EnableInvalidatedCleanup)
			{
			    for (int t = 0; t < NUM_TF; t++)
			    {
			        if (!IsTfEnabled(t)) continue;
			        int bipIdx = t + 2;
			        if (CurrentBars[bipIdx] < 1) continue;
			        DateTime currentHtfBarTime = Times[bipIdx][0];

			        if (_lastSeenHtfBarTime[t] == Core.Globals.MinDate)
			        {
			            _lastSeenHtfBarTime[t] = currentHtfBarTime;
			        }
			        else if (_lastSeenHtfBarTime[t] != currentHtfBarTime)
			        {
			            CleanupInvalidatedLevelsForTimeframe(t, bipIdx);
			            _lastSeenHtfBarTime[t] = currentHtfBarTime;
			        }
			    }
			}
		}


        private void SyncLevels()
        {
            for (int t = 0; t < NUM_TF; t++)
            {
                if (!IsTfEnabled(t)) continue;
                int bipIdx = t + 2;
                int htfBars = CurrentBars[bipIdx];
                if (htfBars < 2) continue;

                for (int p = 0; p < NUM_PAT; p++)
                {
                    if (!IsPatternEnabled(p)) continue;

                    var longSeries  = GetLongLevelSeries(p, t);
                    var shortSeries = GetShortLevelSeries(p, t);
                    if (longSeries == null || shortSeries == null) continue;

                    // V0035: scan several closed HTF bars, not just [1] — PtJV0006
                    // back-stamps a test at its true bar when the pair confirming it
                    // arrives late, so the level can first appear at barsAgo 2..3.
                    // [0] stays the live/forming HTF bar (provisional values).
                    int deepest = Math.Min(MAX_SYNC_AGO, Math.Max(1, CurrentBars[bipIdx] - 1));
                    for (int ago = deepest; ago >= 0; ago--)
                    {
                        double lv = longSeries.IsValidDataPoint(ago)  ? longSeries[ago]  : 0;
                        double sv = shortSeries.IsValidDataPoint(ago) ? shortSeries[ago] : 0;
                        SyncSingleSignal(p, t, bipIdx, ago, lv, true);
                        SyncSingleSignal(p, t, bipIdx, ago, sv, false);
                    }
                }
            }
        }



		private void SyncSingleSignal(int p, int t, int bipIdx, int barsAgoOnHtf, double priceLevel, bool isLong)
        {
            if (priceLevel == 0)
                return;

            DateTime htfBarCloseTime = Times[bipIdx][barsAgoOnHtf];
            DateTime htfBarStartTime = (barsAgoOnHtf + 1 < CurrentBars[bipIdx]) ? Times[bipIdx][barsAgoOnHtf + 1] : htfBarCloseTime.AddMinutes(-_tfMinutes[t]);
            DateTime signalStartTime = htfBarStartTime;

            string pType = _patternKeys[p];
            var tDict = tracked[p][t];
            bool isLiveSignal = (barsAgoOnHtf == 0);

            if (signalStartTime < Core.Globals.MinDate) signalStartTime = htfBarStartTime;
            if (signalStartTime >= htfBarCloseTime) signalStartTime = htfBarStartTime;

            // PERF fast path: identical (start, price) signal as the previous sync of
            // this slot -> update the tracked level directly, skipping the tag string
            // interpolation + dictionary lookup that used to run on every tick.
            int slotIdx = ((p * NUM_TF + t) * 2 + (isLong ? 0 : 1)) * (MAX_SYNC_AGO + 1) + barsAgoOnHtf;
            var slot = _syncCache[slotIdx];
            if (slot.Level != null && !slot.Level.Removed
                && slot.StartTicks == signalStartTime.Ticks && slot.Price == priceLevel)
            {
                var lvl = slot.Level;
                if (lvl.HtfEndTime < htfBarCloseTime)
                {
                    lvl.HtfEndTime = htfBarCloseTime;
                    DrawTrackedLevel(lvl, tDict);
                }
                lvl.LastUpdatedBip0Bar = CurrentBars[0];
                lvl.IsLive = isLiveSignal;

                if (EnableResearchLog && barsAgoOnHtf >= 1 && !lvl.ResearchLogged)
                    LogResearchEvent(lvl);

                return;
            }

            string tag = $"MR_{pType}_TF{t}_{(isLong ? "L" : "S")}_{signalStartTime.Ticks}";
            tag += $"_{priceLevel.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}";
            string label = $"{pType} {_tfLabels[t]} {(isLong ? "L" : "S")}";

            if (tDict.TryGetValue(tag, out var existing))
            {
                bool needsRedraw = false;
                if (existing.HtfEndTime < htfBarCloseTime) { existing.HtfEndTime = htfBarCloseTime; needsRedraw = true; }
                if (Math.Abs(existing.Price - priceLevel) > 0.0001) { existing.Price = priceLevel; needsRedraw = true; }

                existing.LastUpdatedBip0Bar = CurrentBars[0];
                existing.IsLive = isLiveSignal;

                if (needsRedraw) {
                    DrawTrackedLevel(existing, tDict);
                }

                if (EnableResearchLog && barsAgoOnHtf >= 1 && !existing.ResearchLogged)
                    LogResearchEvent(existing);

                slot.StartTicks = signalStartTime.Ticks;
                slot.Price = priceLevel;
                slot.Level = existing;
            }
            else
            {
                var newLevel = new TrackedLevel { Tag = tag, Price = priceLevel, HtfStartTime = signalStartTime, HtfEndTime = htfBarCloseTime, IsLong = isLong, PatternType = pType, Label = label, TfIndex = t, LastUpdatedBip0Bar = CurrentBars[0], IsLive = isLiveSignal };
                tDict[tag] = newLevel;
                DrawTrackedLevel(newLevel, tDict);

                slot.StartTicks = signalStartTime.Ticks;
                slot.Price = priceLevel;
                slot.Level = newLevel;

                if (EnableResearchLog && barsAgoOnHtf >= 1)
                    LogResearchEvent(newLevel);

                // Retroactively plot in Databox for historical signals
                if (State == State.Historical && CurrentBars[0] > 0)
                {
                    int plotIdx = p * NUM_TF + t;

                    int barsBack = 0;
                    while (barsBack < CurrentBars[0])
                    {
                        DateTime bTime = Times[0][barsBack];
                        if (bTime < newLevel.HtfStartTime) break;
                        if (bTime >= newLevel.HtfStartTime && bTime <= newLevel.HtfEndTime)
                        {
                                Values[plotIdx][barsBack] = newLevel.IsLong ? 1 : -1;
                        }
                        barsBack++;
                    }
                }
            }
        }

        private void DrawTrackedLevel(TrackedLevel tl, Dictionary<string, TrackedLevel> tDict)
        {
            Brush c = GetTfColor(tl.TfIndex);
            DashStyleHelper dsh = GetPatternDash(tl.PatternType);
            int lw = LevelWidth;

            bool visible = IsPatternTfShown(tl.PatternType, tl.TfIndex);


            if (visible)
            {
                Draw.Line(this, tl.Tag, false, tl.HtfStartTime, tl.Price, tl.HtfEndTime, tl.Price, c, dsh, lw);
                if (ShowLevelLabels)
                {
                    // Label Offset Ticks: shorts above the level line, longs below it
                    double lblY = tl.Price + (tl.IsLong ? -1 : 1) * LabelOffsetTicks * TickSize;
                    Draw.Text(this, tl.Tag + "_lbl", false, tl.Label, tl.HtfEndTime, lblY, 0, c, new SimpleFont("Arial", LabelFontSize), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
                }
                else
                    RemoveDrawObject(tl.Tag + "_lbl");
            }
        }


		private bool IsTfEnabled(int t)
        {
            return true;
        }




		private bool IsPatternTfShown(string pattern, int t)
        {
            if (pattern == "A") {
                switch(t) { case 0: return ShowPatternATF1_Daily; case 1: return ShowPatternATF2_240m; case 2: return ShowPatternATF3_60m; case 3: return ShowPatternATF4_30m; case 4: return ShowPatternATF5_15m; case 5: return ShowPatternATF6_10m; case 6: return ShowPatternATF7_5m; }
            } else if (pattern == "B") {
                switch(t) { case 0: return ShowPatternBTF1_Daily; case 1: return ShowPatternBTF2_240m; case 2: return ShowPatternBTF3_60m; case 3: return ShowPatternBTF4_30m; case 4: return ShowPatternBTF5_15m; case 5: return ShowPatternBTF6_10m; case 6: return ShowPatternBTF7_5m; }
            } else if (pattern == "G") {
                switch(t) { case 0: return ShowPatternGTF1_Daily; case 1: return ShowPatternGTF2_240m; case 2: return ShowPatternGTF3_60m; case 3: return ShowPatternGTF4_30m; case 4: return ShowPatternGTF5_15m; case 5: return ShowPatternGTF6_10m; case 6: return ShowPatternGTF7_5m; }
            } else if (pattern == "H") {
                switch(t) { case 0: return ShowPatternHTF1_Daily; case 1: return ShowPatternHTF2_240m; case 2: return ShowPatternHTF3_60m; case 3: return ShowPatternHTF4_30m; case 4: return ShowPatternHTF5_15m; case 5: return ShowPatternHTF6_10m; case 6: return ShowPatternHTF7_5m; }
            } else if (pattern == "F") {
                switch(t) { case 0: return ShowPatternFTF1_Daily; case 1: return ShowPatternFTF2_240m; case 2: return ShowPatternFTF3_60m; case 3: return ShowPatternFTF4_30m; case 4: return ShowPatternFTF5_15m; case 5: return ShowPatternFTF6_10m; case 6: return ShowPatternFTF7_5m; }
            } else if (pattern == "J") {
                switch(t) { case 0: return ShowPatternJTF1_Daily; case 1: return ShowPatternJTF2_240m; case 2: return ShowPatternJTF3_60m; case 3: return ShowPatternJTF4_30m; case 4: return ShowPatternJTF5_15m; case 5: return ShowPatternJTF6_10m; case 6: return ShowPatternJTF7_5m; }
            }
            return false;
        }






        private Brush GetTfColor(int t)
        {
            switch(t) {
                case 0: return ColorTF1;
                case 1: return ColorTF2;
                case 2: return ColorTF3;
                case 3: return ColorTF4;
                case 4: return ColorTF5;
                case 5: return ColorTF6;
                case 6: return ColorTF7;
            }
            return Brushes.White;
        }

		#region Research Logging

        // Log one research event for a level whose signal has CONFIRMED (closed HTF bar),
        // and snapshot every other active/recent level as confluence context.
        private void LogResearchEvent(TrackedLevel tl)
        {
            tl.ResearchLogged = true;

            var ev = new ResearchEvent
            {
                Id         = ++_nextResearchId,
                Time       = Times[0][0],
                Pattern    = tl.PatternType,
                TfIndex    = tl.TfIndex,
                IsLong     = tl.IsLong,
                LevelPrice = tl.Price,
                RefPrice   = Closes[0][0]
            };
            _researchOpen.Add(ev);

            // Confluence snapshot: all levels still active or ended within the last 30 minutes
            DateTime now = Times[0][0];
            DateTime staleCutoff = now.AddMinutes(-30);
            for (int p = 0; p < NUM_PAT; p++)
            {
                for (int t = 0; t < NUM_TF; t++)
                {
                    var tDict = tracked[p][t];
                    if (tDict == null || tDict.Count == 0) continue;
                    foreach (var ctx in tDict.Values)
                    {
                        if (ctx == tl) continue;
                        if (ctx.HtfEndTime < staleCutoff) continue;
                        _researchContext.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3},{4},{5:yyyy-MM-dd HH:mm:ss},{6:yyyy-MM-dd HH:mm:ss}",
                            ev.Id, ctx.PatternType, _tfLabels[ctx.TfIndex], ctx.IsLong ? "L" : "S",
                            ctx.Price, ctx.HtfStartTime, ctx.HtfEndTime));
                    }
                }
            }
        }

        // Update forward MFE/MAE for open events from the just-closed primary bar ([1]).
        private void UpdateResearchEvents()
        {
            if (_researchOpen == null || _researchOpen.Count == 0) return;

            double hi = Highs[0][1];
            double lo = Lows[0][1];
            DateTime barTime = Times[0][1];

            for (int i = _researchOpen.Count - 1; i >= 0; i--)
            {
                var ev = _researchOpen[i];
                double elapsedMin = (barTime - ev.Time).TotalMinutes;
                if (elapsedMin <= 0) continue;

                double fav = ev.IsLong ? (hi - ev.RefPrice) / TickSize : (ev.RefPrice - lo) / TickSize;
                double adv = ev.IsLong ? (ev.RefPrice - lo) / TickSize : (hi - ev.RefPrice) / TickSize;

                if (fav > ev.MfeTicks) ev.MfeTicks = fav;
                if (adv > ev.MaeTicks) ev.MaeTicks = adv;

                if (!ev.TargetHit && ev.MfeTicks >= ResearchTargetTicks)
                {
                    ev.TargetHit = true;
                    ev.TargetMinutes = elapsedMin;
                    // Conservative: includes this bar's adverse excursion (bar-granularity
                    // ambiguity — we cannot know intra-bar whether MAE or target came first).
                    ev.MaeBeforeTargetTicks = ev.MaeTicks;
                }

                if (elapsedMin <= 15)  { ev.Mfe15  = ev.MfeTicks; ev.Mae15  = ev.MaeTicks; }
                if (elapsedMin <= 30)  { ev.Mfe30  = ev.MfeTicks; ev.Mae30  = ev.MaeTicks; }
                if (elapsedMin <= 60)  { ev.Mfe60  = ev.MfeTicks; ev.Mae60  = ev.MaeTicks; }
                if (elapsedMin <= 120) { ev.Mfe120 = ev.MfeTicks; ev.Mae120 = ev.MaeTicks; }

                if (elapsedMin >= 120)
                {
                    ev.Done = true;
                    _researchDone.Add(ev);
                    _researchOpen.RemoveAt(i);
                }
            }
        }

        private void WriteResearchRow(System.IO.StreamWriter w, ResearchEvent ev)
        {
            w.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1:yyyy-MM-dd HH:mm:ss},{2},{3},{4},{5},{6},{7},{8},{9:F1},{10:F1},{11:F1},{12:F1},{13:F1},{14:F1},{15:F1},{16:F1},{17:F1},{18:F1},{19}",
                ev.Id, ev.Time, ev.Pattern, _tfLabels[ev.TfIndex], ev.IsLong ? "L" : "S",
                ev.LevelPrice, ev.RefPrice, ResearchTargetTicks,
                ev.TargetHit ? 1 : 0, ev.TargetMinutes, ev.MaeBeforeTargetTicks,
                ev.Mfe15, ev.Mae15, ev.Mfe30, ev.Mae30, ev.Mfe60, ev.Mae60, ev.Mfe120, ev.Mae120,
                ev.Done ? 1 : 0));
        }

        #endregion

		#region Daily Bias Levels (AlightenBiasV0003 pivot/level engine ported inline)

        // Run the pivot rules over every not-yet-processed daily bar up to and including
        // barIdx `through` (safe to call repeatedly; O(1) after the first catch-up).
        private void CatchUpDailyBias(int through)
        {
            for (int b = _dbLastProcessedBar + 1; b <= through; b++)
                ProcessDailyBiasBar(b);
        }

        // One CLOSED daily bar. Exact port of AlightenBiasV0003.OnBarUpdate's pivot rules:
        // four two-bar color/extreme combinations; a same-side pivot is replaced by a more
        // extreme one until the side alternates (DbProcessPivot).
        private void ProcessDailyBiasBar(int barIdx)
        {
            if (barIdx <= _dbLastProcessedBar) return;
            _dbLastProcessedBar = barIdx;

            int ago = CurrentBars[2] - barIdx;
            if (barIdx < 2 || ago < 0 || ago + 1 > CurrentBars[2]) return;

            double o0 = Opens[2][ago],     c0 = Closes[2][ago],     h0 = Highs[2][ago],  l0 = Lows[2][ago];
            double o1 = Opens[2][ago + 1], c1 = Closes[2][ago + 1], h1 = Highs[2][ago + 1], l1 = Lows[2][ago + 1];

            bool isHigh    = h0 > h1;
            bool isLow     = l0 < l1;
            bool prevGreen = c1 >= o1;
            bool currGreen = c0 >= o0;
            DateTime t0    = Times[2][ago];

            if (prevGreen && currGreen && isHigh)   DbProcessPivot(barIdx, h0, true,  Math.Max(o0, c0), t0);
            if (!prevGreen && !currGreen && isLow)  DbProcessPivot(barIdx, l0, false, Math.Min(o0, c0), t0);
            if (prevGreen && !currGreen && isHigh)  DbProcessPivot(barIdx, h0, true,  Math.Max(o0, c0), t0);
            if (!prevGreen && currGreen && isLow)   DbProcessPivot(barIdx, l0, false, Math.Min(o0, c0), t0);
        }

        private void DbProcessPivot(int barIdx, double price, bool isHigh, double guide, DateTime time)
        {
            int n = _dbPivotBars.Count;
            if (n > 0 && _dbPivotIsHigh[n - 1] == isHigh)
            {
                if (isHigh ? price > _dbPivotPrices[n - 1] : price < _dbPivotPrices[n - 1])
                {
                    _dbPivotBars[n - 1]   = barIdx;
                    _dbPivotPrices[n - 1] = price;
                    _dbPivotGuides[n - 1] = guide;
                    _dbPivotTimes[n - 1]  = time;
                }
                return;
            }
            _dbPivotBars.Add(barIdx);
            _dbPivotPrices.Add(price);
            _dbPivotIsHigh.Add(isHigh);
            _dbPivotGuides.Add(guide);
            _dbPivotTimes.Add(time);
        }

        // Draw the last N CONFIRMED daily levels (the newest pivot is still developing and
        // excluded, matching the Bias's own selection). Fixed slot tags, so calling this
        // every primary bar just re-anchors the line ends and refreshes the colors.
        private void RedrawDailyBiasLevels()
        {
            int used = 0;
            if (EnableDailyBiasLevels && _dbPivotBars != null && _dbPivotBars.Count > 1
                && _dbLastPrimaryTime != default(DateTime))
            {
                // Above/below coloring judged by the DAILY timeframe, not the chart series:
                // the last CLOSED daily bar's close. Stable all day — flips only when a
                // daily close crosses the level. Values are cached by OnBarUpdate because
                // this method also runs on the UI thread, where series access is invalid.
                double px       = _dbLastDailyClose;
                DateTime endT   = _dbLastPrimaryTime;
                int confirmed   = _dbPivotBars.Count - 1;   // exclude the developing pivot

                for (int i = confirmed - 1; i >= 0 && used < DailyBiasLevelCount; i--)
                {
                    if (CurrentBars[2] - _dbPivotBars[i] > DailyBiasRelevanceDays) break; // older ones are older still

                    double lvl = _dbPivotGuides[i];
                    Brush  b   = lvl >= px ? DailyBiasAboveColor : DailyBiasBelowColor;
                    string tag = "MirDBL_" + used;

                    Draw.Line(this, tag, false, _dbPivotTimes[i], lvl, endT, lvl, b, DailyBiasLevelDash, DailyBiasLevelWidth);
                    if (ShowDailyBiasLabels)
                        Draw.Text(this, tag + "_lbl", false, "DL " + lvl.ToString("0.00"), endT, lvl, 0, b,
                            new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                    else
                        RemoveDrawObject(tag + "_lbl");
                    used++;
                }
            }

            // clear slots no longer in use (fewer levels, toggled off, relevance shrank)
            for (int k = used; k < _dbDrawnSlots; k++)
            {
                RemoveDrawObject("MirDBL_" + k);
                RemoveDrawObject("MirDBL_" + k + "_lbl");
            }
            _dbDrawnSlots = used;
        }

        #endregion

		#region Cleanup Helpers

        // PERF: move levels that ended more than MirrorLookbackBars HTF bars ago out of
        // the per-tick tracked dictionaries. Their chart drawings remain; they are still
        // included in CSV export and settings-toggle redraws via _archivedLevels.
        private void ArchiveStaleLevels()
        {
            DateTime now = Times[0][0];
            for (int p = 0; p < NUM_PAT; p++)
            {
                for (int t = 0; t < NUM_TF; t++)
                {
                    var tDict = tracked[p][t];
                    if (tDict == null || tDict.Count == 0) continue;

                    DateTime cutoff = now.AddMinutes(-(double)_tfMinutes[t] * Math.Max(1, MirrorLookbackBars));

                    List<string> stale = null;
                    foreach (var kv in tDict)
                    {
                        if (kv.Value.HtfEndTime < cutoff)
                            (stale ?? (stale = new List<string>())).Add(kv.Key);
                    }

                    if (stale != null)
                    {
                        foreach (var key in stale)
                        {
                            _archivedLevels.Add(tDict[key]);
                            tDict.Remove(key);
                        }
                    }
                }
            }
        }

        private void CleanupInvalidatedLevelsForTimeframe(int t, int bipIdx)
        {
            if (CurrentBars[bipIdx] < 2) return;
            DateTime justClosedBarCloseTime = Times[bipIdx][1];
            double justClosedBarClosePrice  = Closes[bipIdx][1];
            for (int p = 0; p < NUM_PAT; p++)
                CleanupInvalidatedList(tracked[p][t], justClosedBarCloseTime, justClosedBarClosePrice);
        }

        private void CleanupInvalidatedList(Dictionary<string, TrackedLevel> tDict, DateTime justClosedBarCloseTime, double justClosedBarClosePrice)
        {
            if (tDict == null || tDict.Count == 0) return;
            var toRemove = new List<TrackedLevel>();
            foreach (var tl in tDict.Values) {
                if (tl.HtfEndTime != justClosedBarCloseTime) continue;
                if (tl.IsLong ? justClosedBarClosePrice < tl.Price : justClosedBarClosePrice > tl.Price) toRemove.Add(tl);
            }
            foreach (var tl in toRemove) RemoveTrackedLevel(tl, tDict);
        }
		#endregion

		#region Settings Modal

		private void CreateToolbarButton()
        {
            try {
                if (ChartControl == null) return;
                chartWindow = Window.GetWindow(ChartControl.Parent) as NinjaTrader.Gui.Chart.Chart;
                if (chartWindow == null || settingsButton != null) return;

                settingsButton = IndicatorVisualStyleHelper.CreateSettingsButton("Mirror Settings", "AlightenMirrorV33SettingsBtn", (s, e) => OpenSettingsWindow());
                settingsButton.Width = 110;
                settingsButton.ToolTip = "Configure Mirror V0029 visibility settings";
                chartWindow.MainMenu.Add(settingsButton);

                exportButton = IndicatorVisualStyleHelper.CreateSettingsButton("Export Levels", "AlightenMirrorV33ExportBtn", (s, e) => ExportLevelsToCsv());
                exportButton.Width = 110;
                exportButton.ToolTip = "Export tracked levels to CSV";
                chartWindow.MainMenu.Add(exportButton);
            } catch (Exception ex) { Print("[AlightenMirrorV0035] toolbar error: " + ex.Message); }
        }

        private void TryRemoveToolbarButton()
        {
            try { if (chartWindow != null && settingsButton != null) chartWindow.MainMenu.Remove(settingsButton); } catch { }
            try { if (chartWindow != null && exportButton != null) chartWindow.MainMenu.Remove(exportButton); } catch { }
            try { if (settingsWindow != null) settingsWindow.Close(); } catch { }
            settingsButton = null; exportButton = null; settingsWindow = null; chartWindow = null;
        }

        private void ExportLevelsToCsv()
        {
            try
            {
                string folder = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "Export");
                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }
                string path = System.IO.Path.Combine(folder, $"MirrorLevels_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(path, false))
                {
                    writer.WriteLine("Pattern,Timeframe,Direction,Price Level,Start Date and Time,End Date and Time");

                    for (int p = 0; p < NUM_PAT; p++)
                        for (int t = 0; t < NUM_TF; t++)
                            if (tracked[p][t] != null)
                                foreach (var tl in tracked[p][t].Values)
                                    WriteLevelToCsv(writer, tl);

                    foreach (var tl in _archivedLevels)
                        WriteLevelToCsv(writer, tl);
                }

                NinjaTrader.Code.Output.Process($"Exported levels to: {path}", PrintTo.OutputTab1);

                // ---- Research export (events + confluence context) ----
                if (EnableResearchLog && (_researchDone.Count > 0 || _researchOpen.Count > 0))
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                    string evPath = System.IO.Path.Combine(folder, $"MirrorResearch_Events_{stamp}.csv");
                    using (var w = new System.IO.StreamWriter(evPath, false))
                    {
                        w.WriteLine("EventId,Time,Pattern,TF,Dir,LevelPrice,RefPrice,TargetTicks,TargetHit,TargetMinutes,MaeBeforeTargetTicks,Mfe15,Mae15,Mfe30,Mae30,Mfe60,Mae60,Mfe120,Mae120,Complete");
                        foreach (var ev in _researchDone) WriteResearchRow(w, ev);
                        foreach (var ev in _researchOpen) WriteResearchRow(w, ev);
                    }

                    string ctxPath = System.IO.Path.Combine(folder, $"MirrorResearch_Context_{stamp}.csv");
                    using (var w = new System.IO.StreamWriter(ctxPath, false))
                    {
                        w.WriteLine("EventId,Pattern,TF,Dir,LevelPrice,StartTime,EndTime");
                        foreach (var line in _researchContext) w.WriteLine(line);
                    }

                    NinjaTrader.Code.Output.Process($"Exported research: {evPath} ({_researchDone.Count + _researchOpen.Count} events) + {ctxPath}", PrintTo.OutputTab1);
                }
            }
            catch (Exception ex)
            {
                Print("[AlightenMirrorV0035] Export error: " + ex.Message);
            }
        }

        private void WriteLevelToCsv(System.IO.StreamWriter writer, TrackedLevel tl)
        {
            string tfString = _tfLabels[tl.TfIndex];
            string dirString = tl.IsLong ? "Long" : "Short";
            string startStr = tl.HtfStartTime.ToString("yyyy-MM-dd HH:mm:ss");
            string endStr = tl.HtfEndTime.ToString("yyyy-MM-dd HH:mm:ss");
            writer.WriteLine($"{tl.PatternType},{tfString},{dirString},{tl.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)},{startStr},{endStr}");
        }

        public void ToggleSettingsWindow()
        {
            if (settingsWindow == null) { OpenSettingsWindow(); return; }
            try
            {
                if (settingsWindow.IsVisible) { settingsWindow.Close(); settingsWindow = null; }
                else { OpenSettingsWindow(); }
            }
            catch
            {
                settingsWindow = null;
                OpenSettingsWindow();
            }
        }

        public void OpenSettingsWindow()
		{
		    if (settingsWindow != null)
		    {
		        try { settingsWindow.Activate(); return; } catch { settingsWindow = null; }
		    }

		    try
		    {
		        ChartControl.Dispatcher.InvokeAsync(() =>
		        {
		            var win = BuildInlineSettingsWindow();
		            win.Owner = Window.GetWindow(ChartControl.Parent);
		            win.Closed += (s, e) => settingsWindow = null;
		            settingsWindow = win;
		            win.Show();
		        });
		    }
		    catch (Exception ex)
		    {
		        Print($"[AlightenMirrorV0035] Failed to open settings: {ex.Message}");
		    }
		}

		private Window BuildInlineSettingsWindow()
		{
		    var win = new Window
		    {
		        Title = "Mirror Settings",
		        Width = 550,
		        Height = 790,
		        WindowStartupLocation = WindowStartupLocation.CenterOwner,
		        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
		        Foreground = System.Windows.Media.Brushes.Gainsboro,
		        ResizeMode = ResizeMode.NoResize,
		        Topmost = true,
		        ShowInTaskbar = false,
		        Owner = Window.GetWindow(ChartControl?.Parent)
		    };

		    IndicatorVisualStyleHelper.ApplyDialogChrome(win);

		    var root = new StackPanel { Margin = new Thickness(12) };

		    GroupBox Group(string title, out StackPanel content)
		    {
		        var gb = new GroupBox
		        {
		            Header = title,
		            Margin = new Thickness(0, 6, 0, 6),
		            Foreground = System.Windows.Media.Brushes.Gainsboro,
		            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60))
		        };
		        content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
		        gb.Content = content;
		        return gb;
		    }

            CheckBox CheckRow(string label, bool initial, Action<bool> onChanged)
            {
                var cb = IndicatorVisualStyleHelper.CreateDialogCheckBox(label);
                cb.IsChecked = initial;
                cb.Margin = new Thickness(0, 3, 12, 3);
                cb.Checked += (s, e) => { onChanged(true); ForceUISync(); };
                cb.Unchecked += (s, e) => { onChanged(false); ForceUISync(); };
                return cb;
            }

		    // 1. Mirror Timeframes — one row per pattern, built from getter/setter tables
		    root.Children.Add(Group("Pattern Timeframes", out var mirrorPanel));
            var mirrorGrid = new StackPanel { Orientation = Orientation.Vertical };

            string[] tfShort = { "D", "4H", "1H", "30m", "15m", "10m", "5m" };

            var patternGetters = new Func<bool>[NUM_PAT][]
            {
                new Func<bool>[] { () => ShowPatternATF1_Daily, () => ShowPatternATF2_240m, () => ShowPatternATF3_60m, () => ShowPatternATF4_30m, () => ShowPatternATF5_15m, () => ShowPatternATF6_10m, () => ShowPatternATF7_5m },
                new Func<bool>[] { () => ShowPatternBTF1_Daily, () => ShowPatternBTF2_240m, () => ShowPatternBTF3_60m, () => ShowPatternBTF4_30m, () => ShowPatternBTF5_15m, () => ShowPatternBTF6_10m, () => ShowPatternBTF7_5m },
                new Func<bool>[] { () => ShowPatternGTF1_Daily, () => ShowPatternGTF2_240m, () => ShowPatternGTF3_60m, () => ShowPatternGTF4_30m, () => ShowPatternGTF5_15m, () => ShowPatternGTF6_10m, () => ShowPatternGTF7_5m },
                new Func<bool>[] { () => ShowPatternHTF1_Daily, () => ShowPatternHTF2_240m, () => ShowPatternHTF3_60m, () => ShowPatternHTF4_30m, () => ShowPatternHTF5_15m, () => ShowPatternHTF6_10m, () => ShowPatternHTF7_5m },
                new Func<bool>[] { () => ShowPatternFTF1_Daily, () => ShowPatternFTF2_240m, () => ShowPatternFTF3_60m, () => ShowPatternFTF4_30m, () => ShowPatternFTF5_15m, () => ShowPatternFTF6_10m, () => ShowPatternFTF7_5m },
                new Func<bool>[] { () => ShowPatternJTF1_Daily, () => ShowPatternJTF2_240m, () => ShowPatternJTF3_60m, () => ShowPatternJTF4_30m, () => ShowPatternJTF5_15m, () => ShowPatternJTF6_10m, () => ShowPatternJTF7_5m },
            };

            var patternSetters = new Action<bool>[NUM_PAT][]
            {
                new Action<bool>[] { v => ShowPatternATF1_Daily = v, v => ShowPatternATF2_240m = v, v => ShowPatternATF3_60m = v, v => ShowPatternATF4_30m = v, v => ShowPatternATF5_15m = v, v => ShowPatternATF6_10m = v, v => ShowPatternATF7_5m = v },
                new Action<bool>[] { v => ShowPatternBTF1_Daily = v, v => ShowPatternBTF2_240m = v, v => ShowPatternBTF3_60m = v, v => ShowPatternBTF4_30m = v, v => ShowPatternBTF5_15m = v, v => ShowPatternBTF6_10m = v, v => ShowPatternBTF7_5m = v },
                new Action<bool>[] { v => ShowPatternGTF1_Daily = v, v => ShowPatternGTF2_240m = v, v => ShowPatternGTF3_60m = v, v => ShowPatternGTF4_30m = v, v => ShowPatternGTF5_15m = v, v => ShowPatternGTF6_10m = v, v => ShowPatternGTF7_5m = v },
                new Action<bool>[] { v => ShowPatternHTF1_Daily = v, v => ShowPatternHTF2_240m = v, v => ShowPatternHTF3_60m = v, v => ShowPatternHTF4_30m = v, v => ShowPatternHTF5_15m = v, v => ShowPatternHTF6_10m = v, v => ShowPatternHTF7_5m = v },
                new Action<bool>[] { v => ShowPatternFTF1_Daily = v, v => ShowPatternFTF2_240m = v, v => ShowPatternFTF3_60m = v, v => ShowPatternFTF4_30m = v, v => ShowPatternFTF5_15m = v, v => ShowPatternFTF6_10m = v, v => ShowPatternFTF7_5m = v },
                new Action<bool>[] { v => ShowPatternJTF1_Daily = v, v => ShowPatternJTF2_240m = v, v => ShowPatternJTF3_60m = v, v => ShowPatternJTF4_30m = v, v => ShowPatternJTF5_15m = v, v => ShowPatternJTF6_10m = v, v => ShowPatternJTF7_5m = v },
            };

            for (int p = 0; p < NUM_PAT; p++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = "Pt " + _patternKeys[p], Width = 30, Foreground = System.Windows.Media.Brushes.Gainsboro, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                for (int t = 0; t < NUM_TF; t++)
                {
                    var setter = patternSetters[p][t];
                    row.Children.Add(CheckRow(tfShort[t], patternGetters[p][t](), v => setter(v)));
                }
                mirrorGrid.Children.Add(row);
            }

            mirrorPanel.Children.Add(mirrorGrid);



		    // ===== Buttons =====
		    var btnRow = new StackPanel
		    {
		        Orientation = Orientation.Horizontal,
		        HorizontalAlignment = HorizontalAlignment.Right,
		        Margin = new Thickness(0, 24, 0, 0)
		    };
		    var btnClean = IndicatorVisualStyleHelper.CreatePrimaryDialogButton("Clean Invalid", (s, e) => { PerformManualRefreshCleanup(); ForceUISync(); });
		    var btnClose = IndicatorVisualStyleHelper.CreateDangerDialogButton("Close", (s, e) => { settingsWindow?.Close(); settingsWindow = null; });
		    btnRow.Children.Add(btnClean);
		    btnRow.Children.Add(btnClose);
		    root.Children.Add(btnRow);

		    win.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
		    return win;
		}

		private void ForceUISync()
		{
		    try
		    {
                RemoveDrawObjects();

                // Re-draw Mirror Tracked with dynamic gating applied during the draw!
                for (int p = 0; p < NUM_PAT; p++)
                    for (int t = 0; t < NUM_TF; t++)
                        if (tracked[p][t] != null)
                            foreach (var tl in tracked[p][t].Values)
                                DrawTrackedLevel(tl, tracked[p][t]);

                // Archived levels keep their drawings across settings toggles
                foreach (var tl in _archivedLevels)
                    DrawTrackedLevel(tl, null);



                RedrawDailyBiasLevels(); // RemoveDrawObjects() above cleared the level lines too

                if (ChartControl != null) ChartControl.InvalidateVisual();
		    }
		    catch (Exception ex)
		    {
		        Print("[MirrorV0034] ForceUISync Error: " + ex.Message);
		    }
		}



		private void RemoveTrackedLevel(TrackedLevel tl, Dictionary<string, TrackedLevel> tDict)
		{
		    if (tl == null) return;
		    tl.Removed = true; // invalidate any sync fast-path cache entry pointing here
		    try { if (!string.IsNullOrEmpty(tl.Tag)) RemoveDrawObject(tl.Tag); } catch { }
		    try { if (!string.IsNullOrEmpty(tl.Tag)) RemoveDrawObject(tl.Tag + "_lbl"); } catch { }
		    try { if (!string.IsNullOrEmpty(tl.Tag) && tDict.ContainsKey(tl.Tag)) tDict.Remove(tl.Tag); } catch { }
		}

		private void PerformManualRefreshCleanup()
		{
		    if (_lastPrimaryBarTime == Core.Globals.MinDate || double.IsNaN(_lastPrimaryClose)) return;
		    DateTime primaryTime = _lastPrimaryBarTime;
		    double currentPrice = _lastPrimaryClose;
		    for (int p = 0; p < NUM_PAT; p++)
		        for (int t = 0; t < NUM_TF; t++)
		            if (IsTfEnabled(t))
		                CleanupCurrentBarInvalidatedList(tracked[p][t], primaryTime, currentPrice);
		}

		private void CleanupCurrentBarInvalidatedList(Dictionary<string, TrackedLevel> tDict, DateTime primaryTime, double currentPrice)
		{
		    if (tDict == null || tDict.Count == 0) return;
		    var toRemove = new List<TrackedLevel>();
		    foreach (var tl in tDict.Values) {
		        // We only clean up levels that are "currently active" (mirrored into the current bar)
		        if (tl.HtfEndTime < primaryTime) continue;
		        if (tl.IsLong ? currentPrice < tl.Price : currentPrice > tl.Price) toRemove.Add(tl);
		    }
		    foreach (var tl in toRemove) RemoveTrackedLevel(tl, tDict);
		}
		#endregion
    }
}

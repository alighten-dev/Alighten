/* Version ID: 2026-08-08a AlightenMirrorV0041 — V0036 + SIGNAL GROUP ZONES (harvested from
   AlightenMirrorEntryV0006), so one indicator replaces the V0036 + EntryV0006 pair on a chart.
   Adds: the file-driven confluence zone engine (group "12. Signal Groups" — rules file,
   ANCHORED/ORDERED flags, window-overlap detection, optional merging, FIFO draw cap), the
   zone-aware manual Clean (wrong-side level sweep + failed-zone purge with tombstones +
   active-zone repaint), and the MirrorGroupsV0041.log forensics trail. Deliberately EXCLUDED
   from EntryV0006: entry evaluation, confirmations, the BIP-1 orderflow engine and the BIP-9
   LTF trigger engine — none of which this chart uses.
   PERF: the dead BIP-1 1-tick series that V0036 loaded and never read is removed, so the HTF
   ladder is now BIP 1..7 (was 2..8) and the series count drops 9 -> 8.
   Previous: V0032 + PATTERN J: hosts the paired-pivot
   pattern source AlightenMirrorPtJV0007 (calc-only, all 47 ctor args, drawing suppressed)
   per timeframe as the SIXTH pattern (key "J", pattern index 5): levels = the nearest
   UNTESTED support/resistance (aligned with the standalone triangles; distance-capped),
   synced/drawn/researched exactly like A/B/G/H/F — per-TF windows, plots PtJD..PtJ5m
   (indices 35-41), "06.5 Pattern J Timeframes" visibility group, Pattern J Style dash
   (default DashDot), settings-modal row, research logging included automatically.
   Previous: V0031 + DAILY BIAS LEVELS: draws the last N
   Daily levels from the AlightenBiasV0003 pivot engine (ported inline on the Daily series,
   BIP 1 — AlightenBias exposes only its aggregate bias plot, so the pivot/level logic is
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


    public class AlightenMirrorV0041 : Indicator
    {
		#region Class Variables
        private const int NUM_TF  = 7;
        private const int NUM_PAT = 6; // A, B, G, H, F, J
        private const int PAT_J   = 5; // index of Pattern J in _patternKeys / tracked[]

        #region Level log (V0041 diagnostics)

        // Counterpart to PtJV0007's signal log. That one records where a signal SHOULD be
        // (one line per triangle the source draws); this one records what the Mirror
        // actually created and removed. Diffing the two shows levels the Mirror invented
        // (present here, absent there) or dropped (the reverse).
        private string _lvlLogPath;
        private int _lvlLogLines;
        private const int LVL_LOG_MAX_LINES = 60000;   // hard stop; logging must never run away

        private void LvlLog(string msg)
        {
            if (!DebugLevelLog || _lvlLogPath == null) return;
            if (_lvlLogLines >= LVL_LOG_MAX_LINES) return;
            try
            {
                _lvlLogLines++;
                if (_lvlLogLines == LVL_LOG_MAX_LINES)
                    msg += "   <<< LINE CAP REACHED - logging stops here >>>";
                System.IO.File.AppendAllText(_lvlLogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + " [" + State + "] " + msg + "\r\n");
            }
            catch { }
        }

        private void LvlLogLevel(string verb, TrackedLevel tl, int barsAgoOnHtf)
        {
            if (!DebugLevelLog || tl == null) return;
            LvlLog(verb + " " + tl.PatternType + " " + _tfLabels[tl.TfIndex]
                + " " + (tl.IsLong ? "L" : "S")
                + " level=" + tl.Price.ToString("F2")
                + " win=" + tl.HtfStartTime.ToString("MM-dd HH:mm") + "->" + tl.HtfEndTime.ToString("MM-dd HH:mm")
                + " ago=" + barsAgoOnHtf
                + " live=" + tl.IsLive);
        }

        #endregion

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

        // ---- Daily bias levels (AlightenBiasV0003 pivot engine ported inline on BIP 1) ----
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

        // Tags of levels already archived. Archiving pulls a level out of `tracked` while
        // KEEPING its drawing, so the tag lookup in SyncSingleSignal misses and the level is
        // re-created — over and over, once per primary bar, for as long as the source keeps
        // reporting it. That never bit the other patterns because a Series<double> reads 0 on
        // most bars; PtJV0007's multi-level set is a durable record and re-offers the same
        // bars on every sync, which turned the loop into ~30,000 creates of one 240m level.
        // A level is created once, archived once, and never resurrected.
        private readonly HashSet<string> _archivedTags = new HashSet<string>();

        private int _lastSyncMs;

        #region Signal group zones (harvested from AlightenMirrorEntryV0006)

        // File-driven confluence zones: each rule row lists 1+ signals (pattern+TF+dir)
        // and a maximum tick spread, e.g. "J15S, J30S, J60S; 100T". When one active
        // level per listed signal exists with all levels inside the spread, a zone
        // rectangle is drawn from the moment the group is identified until the latest
        // member window ends — a "get ready" band, deliberately not an entry arrow.
        private class GroupMember
        {
            public int P;
            public int T;
            public bool IsLong;
        }

        private class GroupRule
        {
            public List<GroupMember> Members = new List<GroupMember>();
            public int MaxTicks;
            public string Label = string.Empty;
            // ANCHORED: the highest-TF member is the anchor; every other member must
            // sit on its protected side (long anchor: at/below it; short: at/above).
            // ORDERED: the listed order is the required price order, lowest first
            // (ties allowed) — full control, written bottom-up like the chart.
            public bool Anchored;
            public bool Ordered;
            public int AnchorIdx;
            public bool IsLong;   // zone direction (the anchor member's direction)
        }

        // Overlapping same-direction zones collapse into one drawn box; per-rule
        // detections still run and log individually underneath.
        private class MergedZone
        {
            public DateTime Start;
            public DateTime End;
            public double Min;
            public double Max;
            public bool IsLong;
            public bool Worked;
            public string Tag;
            public readonly HashSet<string> Rules = new HashSet<string>();
        }

        private class ActiveGroup
        {
            public DateTime IdentifiedTime;
            public DateTime EndTime;
            public double ZoneMin;
            public double ZoneMax;
            public string Tag;
            public string Label = string.Empty;   // owning rule's label, so a redraw can relabel
            public double Spread = double.MaxValue;
            public bool IsLong;
            // Set once price ever trades through the zone's FAVORABLE side (below a
            // short's floor / above a long's ceiling). A zone that worked is never
            // purged as "failed" — snapshot price can't distinguish a failed short
            // from a victorious short revisited, but history can.
            public bool Worked;
        }

        // ---- One independent zone set -------------------------------------------------
        // V0041 runs TWO of these: the PRIMARY zones (the confluence stacks) and the INSIDE
        // zones (the pairs that sit between a primary zone and price). They are identical
        // machinery with separate rule files, colours and state, so a change to one can never
        // silently diverge from the other -- the alternative, duplicating ~500 lines of group
        // logic, drifts apart within a version or two.
        //
        // Name is also the DRAW-TAG PREFIX. Two sets sharing a tag would fight over the same
        // chart object, and Draw.* never restacks an existing tag -- it would look like zones
        // randomly disappearing.
        private class ZoneSet
        {
            public string Name;                  // "PRI" / "INS" -- tag prefix, keep short
            public string Label;                 // for log lines and error messages

            // config, snapshotted from the properties when the rules are loaded
            public bool Enabled;
            public string FileName;
            public System.Windows.Media.Brush ShortColor, LongColor;
            public int Opacity, OutlineWidth, OutlineOpacity;
            public bool Merge, ShowLabels;
            public string LogPath;

            // per-set state
            public List<MergedZone> Merged = new List<MergedZone>();
            public int MergedSeq;
            public HashSet<string> Dismissed = new HashSet<string>();
            public List<GroupRule> Rules = new List<GroupRule>();
            public Dictionary<string, ActiveGroup> Active = new Dictionary<string, ActiveGroup>();
            public Queue<string> DrawTags = new Queue<string>();
            public int MaxDrawings;              // FIFO cap on chart rectangles for this set
            public int MaxTfMinutes;             // largest TF in any rule -> level retention floor
            public bool RepaintPending;
        }

        private ZoneSet _zsPri, _zsIns;
        private ZoneSet[] _zoneSets = new ZoneSet[0];

        private const int MaxGroupDrawings = 300;

        private static readonly string[] _grpTfTokens = { "D", "240", "60", "30", "15", "10", "5" };

        #endregion

        private AlightenMirrorPtAV0010[] _srcA = new AlightenMirrorPtAV0010[NUM_TF];
        private AlightenMirrorPtBV0005[] _srcB = new AlightenMirrorPtBV0005[NUM_TF];
        private AlightenMirrorPtGV0002[] _srcG = new AlightenMirrorPtGV0002[NUM_TF];
        private AlightenMirrorPtHV0002[] _srcH = new AlightenMirrorPtHV0002[NUM_TF];
		private AlightenMirrorPtFV0003[] _srcF = new AlightenMirrorPtFV0003[NUM_TF];
		private AlightenMirrorPtJV0007[] _srcJ = new AlightenMirrorPtJV0007[NUM_TF];

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
        private Button cleanButton;
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
		[Display(Name="Enable Pattern J", Description="Paired-pivot pattern (AlightenMirrorPtJV0007): levels are the nearest untested support/resistance — where the Pattern J test triangles fire.", Order=5, GroupName="01. Patterns")]
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

        [NinjaScriptProperty]
        [Display(Name="Export Mode (no drawing)", Description="Suppress every chart drawing (levels, daily bias lines, zone rectangles) while keeping all level and signal computation. Turn on for a deep-history export run: the draw objects are what make a long load unusable, not the level math.", Order=3, GroupName="10. Research")]
        public bool ExportMode { get; set; }

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

        // ---- Signal group zones (harvested from AlightenMirrorEntryV0006) ----
        // Not [NinjaScriptProperty] on purpose: these stay out of the generated
        // constructor signature, exactly like the Daily Bias Levels group above.
        [Display(Name="Enable Signal Groups", Description="Detect file-defined confluence groups of active levels and mark their zone from identification until the latest member window ends.", Order=1, GroupName="12. Signal Groups")]
        public bool EnableSignalGroups { get; set; }

        [Display(Name="Groups File", Description="Rules file (in Documents\\NinjaTrader 8 unless an absolute path). One rule per row: signals separated by commas, then '; <ticks>T' for the max zone spread. Signal = pattern letter + timeframe + direction, e.g. J15S, J30S, J60S; 100T. Optional extra ';ANCHORED' or ';ORDERED' flag. Created with examples if missing. Edit the file, then reload the indicator.", Order=2, GroupName="12. Signal Groups")]
        public string GroupsFileName { get; set; }

        [XmlIgnore]
        [Display(Name="Short Zone Color", Order=3, GroupName="12. Signal Groups")]
        public Brush GroupShortColor { get; set; }
        [Browsable(false)]
        public string GroupShortColorSerialize { get { return Serialize.BrushToString(GroupShortColor); } set { GroupShortColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="Long Zone Color", Order=4, GroupName="12. Signal Groups")]
        public Brush GroupLongColor { get; set; }
        [Browsable(false)]
        public string GroupLongColorSerialize { get { return Serialize.BrushToString(GroupLongColor); } set { GroupLongColor = Serialize.StringToBrush(value); } }

        [Range(0, 100)]
        [Display(Name="Zone Opacity (%)", Order=5, GroupName="12. Signal Groups")]
        public int GroupZoneOpacity { get; set; }

        [Range(1, 10)]
        [Display(Name="Zone Outline Width", Order=6, GroupName="12. Signal Groups")]
        public int GroupOutlineWidth { get; set; }

        [Range(0, 100)]
        [Display(Name="Zone Outline Opacity (%)", Order=7, GroupName="12. Signal Groups")]
        public int GroupOutlineOpacity { get; set; }

        [Display(Name="Merge Overlapping Zones", Description="Collapse overlapping same-direction zones into one box (per-rule detections still run and log individually).", Order=8, GroupName="12. Signal Groups")]
        public bool MergeGroupZones { get; set; }

        [Range(50, 20000)]
        [Display(Name="Max Zone Drawings", Description="How many zone rectangles this set keeps on the chart. Older drawings are deleted once the count is exceeded, oldest first, even though the zone itself is still live. Raise it if old zones vanish; lower it if the chart feels heavy.", Order=10, GroupName="12. Signal Groups")]
        public int MaxZoneDrawings { get; set; }

        [Display(Name="Show Group Labels", Order=9, GroupName="12. Signal Groups")]
        public bool ShowGroupLabels { get; set; }

        // ---- Inside zones: a second, independent rule set ------------------------------
        // Same machinery as the primary zones, separate file and styling. Intended for the
        // narrower pairs that sit BETWEEN a primary zone and price -- the primary marks the
        // area, the inside zone marks where you would act inside it.
        [Display(Name="Enable Inside Zones", Description="Run a second, independent set of group rules from their own file. Drawn separately from the primary zones and never merged with them.", Order=1, GroupName="13. Inside Zones")]
        public bool EnableInsideZones { get; set; }

        [Display(Name="Inside Zones File", Description="Second rules file, same format as the primary Groups File. In Documents\\NinjaTrader 8 unless an absolute path. Edit the file, then reload the indicator.", Order=2, GroupName="13. Inside Zones")]
        public string InsideZonesFileName { get; set; }

        [XmlIgnore]
        [Display(Name="Inside Short Zone Color", Order=3, GroupName="13. Inside Zones")]
        public System.Windows.Media.Brush InsideShortColor { get; set; }
        [Browsable(false)]
        public string InsideShortColorSerialize { get { return Serialize.BrushToString(InsideShortColor); } set { InsideShortColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="Inside Long Zone Color", Order=4, GroupName="13. Inside Zones")]
        public System.Windows.Media.Brush InsideLongColor { get; set; }
        [Browsable(false)]
        public string InsideLongColorSerialize { get { return Serialize.BrushToString(InsideLongColor); } set { InsideLongColor = Serialize.StringToBrush(value); } }

        [Range(0, 100)]
        [Display(Name="Inside Zone Opacity (%)", Order=5, GroupName="13. Inside Zones")]
        public int InsideZoneOpacity { get; set; }

        [Range(1, 10)]
        [Display(Name="Inside Zone Outline Width", Order=6, GroupName="13. Inside Zones")]
        public int InsideOutlineWidth { get; set; }

        [Range(0, 100)]
        [Display(Name="Inside Zone Outline Opacity (%)", Order=7, GroupName="13. Inside Zones")]
        public int InsideOutlineOpacity { get; set; }

        [Display(Name="Merge Overlapping Inside Zones", Description="Collapse overlapping same-direction inside zones into one box. Inside zones never merge with primary zones.", Order=8, GroupName="13. Inside Zones")]
        public bool MergeInsideZones { get; set; }

        [Range(50, 20000)]
        [Display(Name="Max Inside Zone Drawings", Description="Same FIFO cap as the primary set, applied independently. The inside set typically produces several times more zones, so it hits the cap sooner.", Order=10, GroupName="13. Inside Zones")]
        public int MaxInsideZoneDrawings { get; set; }

        [Display(Name="Show Inside Zone Labels", Order=9, GroupName="13. Inside Zones")]
        public bool ShowInsideLabels { get; set; }

        [Display(Name = "Write Level Log", Description = "Append every level created and removed to MirrorV0041Levels.log in the NinjaTrader 8 folder, for diffing against MirrorPtJV0007Signals.log. Diagnostic — leave off in normal use. Capped at 60,000 lines.", Order = 1, GroupName = "13. Diagnostics")]
        public bool DebugLevelLog { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AlightenMirrorV0041";
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
                ShowPatternHTF4_30m = false;
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
                ShowPatternJTF1_Daily = true;
                ShowPatternJTF2_240m = true;
                ShowPatternJTF3_60m = true;
                ShowPatternJTF4_30m = false;
                ShowPatternJTF5_15m = false;
                ShowPatternJTF6_10m = false;
                ShowPatternJTF7_5m = false;


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
                ExportMode = false;

                EnableDailyBiasLevels = true;
                DailyBiasLevelCount = 4;
                DailyBiasRelevanceDays = 300;
                DailyBiasAboveColor = Brushes.Goldenrod;
                DailyBiasBelowColor = Brushes.DeepSkyBlue;
                DailyBiasLevelWidth = 3;
                DailyBiasLevelDash = DashStyleHelper.Solid;
                ShowDailyBiasLabels = true;

                EnableSignalGroups  = true;
                GroupsFileName = "MirrorGroupsV0040.txt";
                GroupShortColor = Brushes.White;
                GroupLongColor      = Brushes.DeepSkyBlue;
                GroupZoneOpacity    = 5;
                GroupOutlineWidth   = 10;
                GroupOutlineOpacity = 70;
                MergeGroupZones     = false;
                ShowGroupLabels     = false;
                MaxZoneDrawings = 500;
                EnableInsideZones = true;
                InsideZonesFileName = "MirrorInsideZonesV0040.txt";
                InsideShortColor = Brushes.Magenta;
                InsideLongColor = new SolidColorBrush(Color.FromRgb(0x25, 0xD7, 0x25));
                InsideLongColor.Freeze();
                InsideZoneOpacity = 5;
                InsideOutlineWidth = 3;
                InsideOutlineOpacity = 50;
                MergeInsideZones    = false;
                ShowInsideLabels    = false;
                MaxInsideZoneDrawings = 500;

                DebugLevelLog       = false;  // diagnostic only; tick "Write Level Log" to enable


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
			    // V0037: V0036 added a 1-tick series here that nothing ever read — it had no
			    // BarsInProgress branch and no series accessor anywhere in the file. It loaded
			    // a full tick history and fired OnBarUpdate on every tick only to fall through
			    // the guards. Removed; the HTF ladder now starts at BIP 1, so bipIdx = t + 1
			    // and the Daily series (the daily-bias engine's source) is BIP 1.

			    // BIP 1: Daily
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

			    // BIP 2: 240-minute
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

			    // BIP 3: 60-minute
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

			    // BIP 4: 30-minute
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

			    // BIP 5: 15-minute
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

			    // BIP 6: 10-minute
			    AddDataSeries(BarsPeriodType.Minute, 10);

			    // BIP 7: 5-minute
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

                if (DebugLevelLog)
                {
                    try
                    {
                        _lvlLogPath = System.IO.Path.Combine(
                            NinjaTrader.Core.Globals.UserDataDir, "MirrorV0041Levels.log");
                        _lvlLogLines = 0;
                        System.IO.File.AppendAllText(_lvlLogPath,
                            "\r\n======== LOAD " + DateTime.Now.ToString("HH:mm:ss") + "  "
                            + (Instrument != null ? Instrument.FullName : "?") + " ========\r\n");
                    }
                    catch { _lvlLogPath = null; }
                }

                BuildZoneSets();
                foreach (ZoneSet zs in _zoneSets) LoadGroupRules(zs);

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

                    int bipIdx = t + 1;

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
                        _srcJ[t] = AlightenMirrorPtJV0007(
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
		    // the cleanup block below also ran for every event of all 7 secondary series.
		    // Child pattern indicators update from their own input series regardless, and
		    // the HTF cleanup check reads Times[bipIdx][0] which is current from BIP0.
		    // Daily bias levels: run the pivot rules on each CLOSED Daily bar (BIP 1).
		    // The daily series preloads far MORE history than the primary chart shows, and
		    // those early daily events are discarded by the CurrentBars[0] guard above — so
		    // every call catches up ALL unprocessed bars via barsAgo indexing (the first one
		    // walks the entire preloaded daily history; after that it is one bar per day).
		    if (BarsInProgress == 1 && EnableDailyBiasLevels)
		    {
		        try
		        {
		            // Historical: the current daily bar [0] is complete. Realtime: it is forming.
		            CatchUpDailyBias(State == State.Historical ? CurrentBars[1] : CurrentBars[1] - 1);
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

                    // Signal group zones. Throttled to bar close (plus a one-shot after a
                    // manual Clean): the group retention floor deliberately keeps far more
                    // levels in `tracked`, which makes the combinatorial scan too expensive
                    // to run per tick. Historical runs every pass so a reload rebuilds the
                    // full zone history.
                    bool anyRepaint = false;
                    foreach (ZoneSet zsq in _zoneSets) if (zsq.RepaintPending) anyRepaint = true;
                    if (_zoneSets.Length > 0
                        && (State == State.Historical || IsFirstTickOfBar || anyRepaint))
                    {
                        _candCache.Clear();   // one scan per slot, shared by both sets
                        foreach (ZoneSet zs in _zoneSets)
                        {
                            zs.RepaintPending = false;
                            UpdateSignalGroups(zs);
                        }
                    }

                    // Research: update forward outcomes from the just-closed primary bar
                    if (EnableResearchLog && IsFirstTickOfBar && CurrentBars[0] >= 1)
                        UpdateResearchEvents();

                    // Daily bias levels: re-extend lines to the current bar + refresh
                    // above/below-price coloring once per primary bar. The catch-up also
                    // runs from here so short charts (few or no daily closes inside the
                    // loaded primary window) still process the preloaded daily history.
                    if (IsFirstTickOfBar && EnableDailyBiasLevels)
                    {
                        CatchUpDailyBias(CurrentBars[1] - 1);

                        // Cache the series values here (data thread) — the redraw itself
                        // must stay series-free so the UI thread can call it too.
                        _dbLastPrimaryTime = Times[0][0];
                        _dbLastDailyClose  = State == State.Historical || CurrentBars[1] < 1
                                               ? Closes[1][0] : Closes[1][1];

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
			        int bipIdx = t + 1;
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
                int bipIdx = t + 1;
                int htfBars = CurrentBars[bipIdx];
                if (htfBars < 2) continue;

                for (int p = 0; p < NUM_PAT; p++)
                {
                    if (!IsPatternEnabled(p)) continue;

                    var longSeries  = GetLongLevelSeries(p, t);
                    var shortSeries = GetShortLevelSeries(p, t);
                    if (longSeries == null || shortSeries == null) continue;

                    // V0035: scan several closed HTF bars, not just [1] — PtJV0007
                    // back-stamps a test at its true bar when the pair confirming it
                    // arrives late, so the level can first appear at barsAgo 2..3.
                    // [0] stays the live/forming HTF bar (provisional values).
                    //
                    // V0041: Pattern J can test SEVERAL nodes on one bar and draws a triangle
                    // for each, but one Series<double> carries only one. PtJV0007 publishes
                    // the rest as EXTRA SERIES, so they are read here exactly the way Pattern
                    // A's single series is read — same loop, same SyncSingleSignal, same tag
                    // and archive semantics. A bar with fewer levels reads 0 in the spare
                    // slots, which is identical to any bar with no signal.
                    AlightenMirrorPtJV0007 srcJ = (p == PAT_J) ? _srcJ[t] : null;

                    int deepest = Math.Min(MAX_SYNC_AGO, Math.Max(1, CurrentBars[bipIdx] - 1));
                    for (int ago = deepest; ago >= 0; ago--)
                    {
                        double lv = longSeries.IsValidDataPoint(ago)  ? longSeries[ago]  : 0;
                        double sv = shortSeries.IsValidDataPoint(ago) ? shortSeries[ago] : 0;
                        SyncSingleSignal(p, t, bipIdx, ago, lv, true);
                        SyncSingleSignal(p, t, bipIdx, ago, sv, false);

                        // Slots 2..N for Pattern J (slot 0 is longSeries/shortSeries above).
                        if (srcJ == null) continue;
                        for (int slot = 1; slot < srcJ.LevelSlotCount; slot++)
                        {
                            Series<double> ls = srcJ.LongLevelSlot(slot);
                            Series<double> ss = srcJ.ShortLevelSlot(slot);
                            SyncSingleSignal(p, t, bipIdx, ago, ls.IsValidDataPoint(ago) ? ls[ago] : 0, true);
                            SyncSingleSignal(p, t, bipIdx, ago, ss.IsValidDataPoint(ago) ? ss[ago] : 0, false);
                        }
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
                // Already archived: it exists as a drawing and in _archivedLevels, just not in
                // `tracked`. Re-creating it here is what produced the runaway.
                if (_archivedTags.Contains(tag))
                    return;

                var newLevel = new TrackedLevel { Tag = tag, Price = priceLevel, HtfStartTime = signalStartTime, HtfEndTime = htfBarCloseTime, IsLong = isLong, PatternType = pType, Label = label, TfIndex = t, LastUpdatedBip0Bar = CurrentBars[0], IsLive = isLiveSignal };
                tDict[tag] = newLevel;
                DrawTrackedLevel(newLevel, tDict);
                LvlLogLevel("CREATE", newLevel, barsAgoOnHtf);

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
            if (ExportMode) return;   // export runs draw nothing: chart objects are the cost, not the level math
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

            int ago = CurrentBars[1] - barIdx;
            if (barIdx < 2 || ago < 0 || ago + 1 > CurrentBars[1]) return;

            double o0 = Opens[1][ago],     c0 = Closes[1][ago],     h0 = Highs[1][ago],  l0 = Lows[1][ago];
            double o1 = Opens[1][ago + 1], c1 = Closes[1][ago + 1], h1 = Highs[1][ago + 1], l1 = Lows[1][ago + 1];

            bool isHigh    = h0 > h1;
            bool isLow     = l0 < l1;
            bool prevGreen = c1 >= o1;
            bool currGreen = c0 >= o0;
            DateTime t0    = Times[1][ago];

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
            if (ExportMode) return;   // export runs draw nothing: chart objects are the cost, not the level math
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
                    if (CurrentBars[1] - _dbPivotBars[i] > DailyBiasRelevanceDays) break; // older ones are older still

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

		#region Signal Group Zones (harvested from AlightenMirrorEntryV0006)

        private void GrpLog(ZoneSet zs, string msg)
        {
            if (string.IsNullOrEmpty(zs.LogPath)) return;
            try
            {
                System.IO.File.AppendAllText(zs.LogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + " [" + State + "] " + msg + "\r\n");
            }
            catch { }
        }

        // Snapshot the properties into set objects. Only enabled sets end up in _zoneSets, so
        // every loop below is over live sets and no call site needs its own enable check.
        private void BuildZoneSets()
        {
            _zsPri = new ZoneSet
            {
                Name = "PRI", Label = "primary",
                Enabled = EnableSignalGroups, FileName = GroupsFileName,
                ShortColor = GroupShortColor, LongColor = GroupLongColor,
                Opacity = GroupZoneOpacity, OutlineWidth = GroupOutlineWidth,
                OutlineOpacity = GroupOutlineOpacity,
                Merge = MergeGroupZones, ShowLabels = ShowGroupLabels,
                MaxDrawings = MaxZoneDrawings,
            };
            _zsIns = new ZoneSet
            {
                Name = "INS", Label = "inside",
                Enabled = EnableInsideZones, FileName = InsideZonesFileName,
                ShortColor = InsideShortColor, LongColor = InsideLongColor,
                Opacity = InsideZoneOpacity, OutlineWidth = InsideOutlineWidth,
                OutlineOpacity = InsideOutlineOpacity,
                Merge = MergeInsideZones, ShowLabels = ShowInsideLabels,
                MaxDrawings = MaxInsideZoneDrawings,
            };

            List<ZoneSet> live = new List<ZoneSet>();
            if (_zsPri.Enabled) live.Add(_zsPri);
            if (_zsIns.Enabled) live.Add(_zsIns);
            _zoneSets = live.ToArray();

            // Say so out loud. A disabled set writes no log at all, which is indistinguishable
            // from a broken one -- that ambiguity cost a debugging session, so both sets report
            // their state to the Output window whether they run or not.
            NinjaTrader.Code.Output.Process(string.Format(
                "[AlightenMirrorV0041] zone sets: PRIMARY {0} ({1})   INSIDE {2} ({3})",
                _zsPri.Enabled ? "ON" : "OFF", _zsPri.FileName,
                _zsIns.Enabled ? "ON" : "OFF", _zsIns.FileName), PrintTo.OutputTab1);
        }

        private void LoadGroupRules(ZoneSet zs)
        {
            zs.Rules   = new List<GroupRule>();
            zs.Active = new Dictionary<string, ActiveGroup>();
            zs.DrawTags = new Queue<string>();
            zs.MaxTfMinutes = 0;
            zs.Merged  = new List<MergedZone>();
            zs.MergedSeq = 0;
            zs.Dismissed = new HashSet<string>();

            if (!zs.Enabled)
                return;

            string path = zs.FileName != null && zs.FileName.IndexOf(':') >= 0
                ? zs.FileName
                : System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, zs.FileName ?? "MirrorGroupsV0037.txt");

            try
            {
                if (!System.IO.File.Exists(path))
                {
                    System.IO.File.WriteAllText(path,
                        "# AlightenMirrorV0041 signal groups\r\n" +
                        "# One rule per row:  <signal>, <signal>, ... ; <max zone spread in ticks>T\r\n" +
                        "# Signal = Pattern letter (A B G H F J) + timeframe (D 240 60 30 15 10 5) + direction (S or L)\r\n" +
                        "# Optional extra flag: ; ANCHORED   (others on the highest-TF member's protected side)\r\n" +
                        "#                      ; ORDERED    (listed order is the required price order, lowest first)\r\n" +
                        "# Example: the triple J short stack within 100 ticks:\r\n" +
                        "J15S, J30S, J60S; 100T\r\n" +
                        "J15L, J30L, J60L; 100T\r\n");
                    Print("[AlightenMirrorV0041] created groups file with examples: " + path);
                }

                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;

                    string[] segs = line.Split(';');
                    if (segs.Length < 2)
                    {
                        Print("[AlightenMirrorV0041] groups file: missing '; <ticks>T' in row: " + line);
                        continue;
                    }

                    string tickPart = segs[1].Trim().TrimEnd('T', 't', ' ');
                    int maxTicks;
                    if (!int.TryParse(tickPart, out maxTicks) || maxTicks <= 0)
                    {
                        Print("[AlightenMirrorV0041] groups file: bad tick spread in row: " + line);
                        continue;
                    }

                    var rule = new GroupRule { MaxTicks = maxTicks };

                    for (int s = 2; s < segs.Length; s++)
                    {
                        string flag = segs[s].Trim().ToUpperInvariant();
                        if (flag == "ANCHORED")
                            rule.Anchored = true;
                        else if (flag == "ORDERED")
                            rule.Ordered = true;
                        else if (flag.Length > 0)
                            Print("[AlightenMirrorV0041] groups file: unknown flag '" + flag + "' in row: " + line);
                    }

                    bool ok = true;
                    foreach (string tokRaw in segs[0].Split(','))
                    {
                        string tok = tokRaw.Trim().ToUpperInvariant();
                        if (tok.Length < 3) { ok = false; break; }

                        string patKey = tok.Substring(0, 1);
                        string dirKey = tok.Substring(tok.Length - 1, 1);
                        string tfKey  = tok.Substring(1, tok.Length - 2);

                        int p = Array.IndexOf(_patternKeys, patKey);
                        int t = Array.IndexOf(_grpTfTokens, tfKey);
                        bool isLong = dirKey == "L";

                        if (p < 0 || t < 0 || (dirKey != "L" && dirKey != "S"))
                        {
                            Print("[AlightenMirrorV0041] groups file: bad signal token '" + tok + "' in row: " + line);
                            ok = false;
                            break;
                        }

                        rule.Members.Add(new GroupMember { P = p, T = t, IsLong = isLong });
                        rule.Label += (rule.Label.Length > 0 ? "+" : "") + tok;
                    }

                    if (ok && rule.Members.Count > 0)
                    {
                        // Anchor = highest timeframe listed (lowest TF index; first wins ties).
                        for (int m = 1; m < rule.Members.Count; m++)
                            if (rule.Members[m].T < rule.Members[rule.AnchorIdx].T)
                                rule.AnchorIdx = m;
                        rule.IsLong = rule.Members[rule.AnchorIdx].IsLong;
                        foreach (GroupMember gm in rule.Members)
                            if (_tfMinutes[gm.T] > zs.MaxTfMinutes)
                                zs.MaxTfMinutes = _tfMinutes[gm.T];
                        zs.Rules.Add(rule);
                    }
                }

                Print("[AlightenMirrorV0041] loaded " + zs.Rules.Count + " signal-group rule(s) from " + path);

                zs.LogPath = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir,
                    "MirrorZonesV0041_" + zs.Name + ".log");
                GrpLog(zs, "======== LOAD " + (Instrument != null ? Instrument.FullName : "?")
                    + " rules=" + zs.Rules.Count + " ========");
            }
            catch (Exception ex)
            {
                Print("[AlightenMirrorV0041] groups file error: " + ex.Message);
            }
        }

        // ---- shared candidate cache + price search --------------------------------
        // Cleared once per bar before the zone sets run, so both sets and all 102 rules
        // share one scan per (pattern, timeframe, direction). Levels are only mutated by
        // SyncLevels, which completes before either set is evaluated.
        private readonly Dictionary<int, List<TrackedLevel>> _candCache =
            new Dictionary<int, List<TrackedLevel>>(64);

        private List<TrackedLevel> GetCandidates(int p, int t, bool isLong)
        {
            int key = ((p * 8 + t) << 1) | (isLong ? 1 : 0);
            List<TrackedLevel> list;
            if (_candCache.TryGetValue(key, out list))
                return list;

            list = new List<TrackedLevel>(32);
            var src = tracked[p][t];
            if (src != null)
                foreach (var lvl in src.Values)
                    if (!lvl.Removed && lvl.IsLong == isLong)
                        list.Add(lvl);

            // Sorted by price so the window search can binary-search. This also removes the
            // old `if (list.Count >= 32) break;` cap, which took an arbitrary 32 out of an
            // unordered Dictionary.Values -- so once a slot held more than 32 levels, WHICH
            // 32 you got could change between runs and a zone could appear on one reload
            // and not the next from identical inputs.
            list.Sort(delegate(TrackedLevel a, TrackedLevel b) { return a.Price.CompareTo(b.Price); });
            _candCache[key] = list;
            return list;
        }

        private static int LowerBoundByPrice(List<TrackedLevel> list, double price)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (list[mid].Price < price) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private static int UpperBoundByPrice(List<TrackedLevel> list, double price)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (list[mid].Price <= price) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private void UpdateSignalGroups(ZoneSet zs)
        {
            if (zs.Rules == null || zs.Rules.Count == 0)
                return;

            // Stamp zones that have WORKED (price through the favorable side) —
            // this history is what protects them from the failed-zone purge.
            if (!double.IsNaN(_lastPrimaryClose))
            {
                double weps = TickSize > 0 ? TickSize * 0.5 : 0.01;
                foreach (var ag in zs.Active.Values)
                    if (!ag.Worked && (ag.IsLong ? _lastPrimaryClose > ag.ZoneMax + weps
                                                 : _lastPrimaryClose < ag.ZoneMin - weps))
                        ag.Worked = true;
                if (zs.Merged != null)
                    foreach (var mz in zs.Merged)
                        if (!mz.Worked && (mz.IsLong ? _lastPrimaryClose > mz.Max + weps
                                                     : _lastPrimaryClose < mz.Min - weps))
                            mz.Worked = true;
            }

            // WINDOW-OVERLAP semantics, not "window still open at now": during
            // historical processing every HTF level syncs only at its window CLOSE
            // (the known HTF-retro gotcha), so an open-window check can never match
            // and no group would ever draw on a reload. Instead a group exists
            // wherever the member windows COEXIST IN TIME — identified at the latest
            // member window start, ending at the latest window end — computed purely
            // from the level record, so historical and live produce the same zones.
            for (int r = 0; r < zs.Rules.Count; r++)
            {
                GroupRule rule = zs.Rules[r];

                // Candidate lists are price-sorted and SHARED across every rule and both
                // zone sets. J15L alone is referenced by 24 rules; the old code rescanned
                // its dictionary once per reference, 242 scans per bar over only 34
                // distinct (pattern, timeframe, direction) slots.
                List<TrackedLevel>[] cands = new List<TrackedLevel>[rule.Members.Count];
                bool allHave = true;
                for (int m = 0; m < rule.Members.Count; m++)
                {
                    GroupMember gm = rule.Members[m];
                    cands[m] = GetCandidates(gm.P, gm.T, gm.IsLong);
                    if (cands[m].Count == 0) { allHave = false; break; }
                }

                if (!allHave)
                    continue;

                // ANCHOR-DRIVEN SEARCH. The previous code enumerated the FULL cartesian
                // product and only then tested the spread: 32^4 = 1,048,576 fully-evaluated
                // combinations per four-member rule to keep a handful, 9.1M per bar across
                // the primary file.
                //
                // Every valid combination has all its members within MaxTicks of ANY single
                // member, so pinning one slot and binary-searching the others for that price
                // window is an EXACT prefilter -- it cannot drop a combination the old loop
                // would have kept. The pinned slot is the smallest one, which bounds the
                // outer loop by the scarcest timeframe (usually 240m or 60m).
                int anchorSlot = 0;
                for (int m = 1; m < cands.Length; m++)
                    if (cands[m].Count < cands[anchorSlot].Count)
                        anchorSlot = m;

                double window = rule.MaxTicks * (TickSize > 0 ? TickSize : 0.25);
                int[] idx = new int[rule.Members.Count];
                int[] lo  = new int[rule.Members.Count];
                int[] hi  = new int[rule.Members.Count];

                for (int ai = 0; ai < cands[anchorSlot].Count; ai++)
                {
                    double ap = cands[anchorSlot][ai].Price;
                    bool anyEmpty = false;
                    for (int m = 0; m < cands.Length; m++)
                    {
                        if (m == anchorSlot) { lo[m] = ai; hi[m] = ai + 1; continue; }
                        lo[m] = LowerBoundByPrice(cands[m], ap - window);
                        hi[m] = UpperBoundByPrice(cands[m], ap + window);
                        if (hi[m] <= lo[m]) { anyEmpty = true; break; }
                    }
                    if (anyEmpty)
                        continue;

                    for (int m = 0; m < idx.Length; m++)
                        idx[m] = lo[m];

                while (true)
                {
                    double mn = double.MaxValue, mx = double.MinValue;
                    DateTime overlapStart = Core.Globals.MinDate;
                    DateTime overlapEnd   = DateTime.MaxValue;
                    DateTime lastEnd      = Core.Globals.MinDate;
                    for (int m = 0; m < idx.Length; m++)
                    {
                        TrackedLevel lvl = cands[m][idx[m]];
                        if (lvl.Price < mn) mn = lvl.Price;
                        if (lvl.Price > mx) mx = lvl.Price;
                        if (lvl.HtfStartTime > overlapStart) overlapStart = lvl.HtfStartTime;
                        if (lvl.HtfEndTime   < overlapEnd)   overlapEnd   = lvl.HtfEndTime;
                        if (lvl.HtfEndTime   > lastEnd)      lastEnd      = lvl.HtfEndTime;
                    }

                    double spreadTicks = TickSize > 0 ? (mx - mn) / TickSize : double.MaxValue;

                    // "At the same level" must always satisfy at/below (or at/above):
                    // levels from different pattern engines can differ by float noise
                    // at the same nominal price, so the comparison tolerance is half
                    // a tick — a genuine one-tick violation still fails.
                    double eqTol = TickSize > 0 ? TickSize * 0.5 : 1e-9;

                    bool orderOk = true;
                    if (rule.Anchored)
                    {
                        TrackedLevel anchor = cands[rule.AnchorIdx][idx[rule.AnchorIdx]];
                        for (int m = 0; m < idx.Length && orderOk; m++)
                        {
                            if (m == rule.AnchorIdx) continue;
                            TrackedLevel lvl = cands[m][idx[m]];
                            orderOk = anchor.IsLong
                                ? lvl.Price <= anchor.Price + eqTol   // long: others at/below (or exactly at) the anchor
                                : lvl.Price >= anchor.Price - eqTol;  // short: others at/above (or exactly at) the anchor
                        }
                    }
                    if (rule.Ordered)
                    {
                        for (int m = 1; m < idx.Length && orderOk; m++)
                            orderOk = cands[m][idx[m]].Price >= cands[m - 1][idx[m - 1]].Price - eqTol;
                    }

                    if (overlapStart < overlapEnd && spreadTicks <= rule.MaxTicks && orderOk)
                    {
                        // One zone per (rule, overlap-start): stable across re-syncs,
                        // extends in place while member windows roll forward.
                        string key = r + "_" + overlapStart.Ticks;
                        if (zs.Dismissed != null && zs.Dismissed.Contains(key))
                        {
                            // Purged by a manual Clean — stays dismissed for the session.
                            int carryD = idx.Length - 1;
                            while (carryD >= 0 && ++idx[carryD] >= hi[carryD])
                            {
                                idx[carryD] = lo[carryD];
                                carryD--;
                            }
                            if (carryD < 0)
                                break;
                            continue;
                        }
                        ActiveGroup g;
                        bool isNew = !zs.Active.TryGetValue(key, out g);
                        if (isNew)
                        {
                            g = new ActiveGroup
                            {
                                IdentifiedTime = overlapStart,
                                Tag = zs.Name + "_MRG_" + key,
                                IsLong = rule.IsLong
                            };
                            zs.Active[key] = g;
                            // Must stay >= the drawing cap. If the map is smaller, a zone can be
                            // evicted from it while its rectangle is still on the chart -- and then
                            // ForceUISync, which redraws only from this map, cannot restore it.
                            if (zs.Active.Count > Math.Max(400, zs.MaxDrawings + 100))
                                zs.Active.Clear();   // map only dedupes; drawings live on
                        }

                        // Monotonic updates only: several member combos can share one
                        // zone key (same overlap start), and letting them alternate
                        // extents caused perpetual redraw/UPD flapping — expired zones
                        // kept "living" for hours. A zone now changes only when a
                        // combo TIGHTENS it or its window genuinely extends.
                        bool extendsEnd = lastEnd > g.EndTime;
                        bool tighter    = spreadTicks < g.Spread - 1e-9;
                        if (isNew || extendsEnd || tighter)
                        {
                            g.ZoneMin = mn;
                            g.ZoneMax = mx;
                            g.EndTime = lastEnd;
                            g.Spread  = spreadTicks;
                            g.Label   = rule.Label;
                            if (zs.Merge)
                                MergeZone(zs, rule, g);
                            else
                                DrawGroupZone(zs, rule, g);

                            // Forensics: record exactly which member levels formed or
                            // reshaped this zone (MirrorGroupsV0041.log).
                            GrpLog(zs, "ZONE " + (isNew ? "NEW " : "UPD ") + g.Tag
                                + " rule=" + rule.Label
                                + " spread=" + spreadTicks.ToString("F0") + "t"
                                + " zone=" + mn.ToString("F2") + "-" + mx.ToString("F2")
                                + " ident=" + overlapStart.ToString("MM-dd HH:mm")
                                + " end=" + lastEnd.ToString("MM-dd HH:mm"));
                            for (int m = 0; m < idx.Length; m++)
                            {
                                TrackedLevel lvl = cands[m][idx[m]];
                                GrpLog(zs, "    " + _patternKeys[rule.Members[m].P]
                                    + _grpTfTokens[rule.Members[m].T]
                                    + (rule.Members[m].IsLong ? "L" : "S")
                                    + " @ " + lvl.Price.ToString("F2")
                                    + "  win " + lvl.HtfStartTime.ToString("MM-dd HH:mm")
                                    + " -> " + lvl.HtfEndTime.ToString("MM-dd HH:mm")
                                    + "  live=" + lvl.IsLive
                                    + "  tag=" + lvl.Tag);
                            }
                        }
                    }

                    int carry = idx.Length - 1;
                    while (carry >= 0 && ++idx[carry] >= hi[carry])
                    {
                        idx[carry] = lo[carry];
                        carry--;
                    }
                    if (carry < 0)
                        break;
                }
                }
            }
        }

        private void DrawGroupZone(ZoneSet zs, GroupRule rule, ActiveGroup g)
        {
            DrawZoneRect(zs, g.Tag, rule.IsLong, g.IdentifiedTime, g.EndTime, g.ZoneMin, g.ZoneMax, rule.Label);
        }

        // Re-issue the Draw calls for every zone still held in memory. Called from
        // ForceUISync after RemoveDrawObjects() has cleared the chart.
        private void RedrawGroupZones(ZoneSet zs)
        {
            if (!zs.Enabled) return;

            if (zs.Merge && zs.Merged != null)
            {
                foreach (MergedZone mz in zs.Merged)
                {
                    string label = mz.Rules.Count == 1
                        ? System.Linq.Enumerable.First(mz.Rules)
                        : (mz.IsLong ? "LONG" : "SHORT") + " x" + mz.Rules.Count;
                    DrawZoneRect(zs, mz.Tag, mz.IsLong, mz.Start, mz.End, mz.Min, mz.Max, label);
                }
                return;
            }

            if (zs.Active != null)
                foreach (ActiveGroup g in zs.Active.Values)
                    DrawZoneRect(zs, g.Tag, g.IsLong, g.IdentifiedTime, g.EndTime, g.ZoneMin, g.ZoneMax, g.Label);
        }

        private void MergeZone(ZoneSet zs, GroupRule rule, ActiveGroup g)
        {
            double pad = TickSize > 0 ? TickSize * 2 : 0.5;   // small bridge so near-touching zones fuse

            MergedZone host = null;
            for (int i = zs.Merged.Count - 1; i >= 0; i--)
            {
                MergedZone mz = zs.Merged[i];
                if (mz.IsLong != rule.IsLong)
                    continue;
                bool timeOverlap  = g.IdentifiedTime <= mz.End && g.EndTime >= mz.Start;
                bool priceOverlap = g.ZoneMin <= mz.Max + pad && g.ZoneMax >= mz.Min - pad;
                if (!timeOverlap || !priceOverlap)
                    continue;

                if (host == null)
                {
                    host = mz;
                    if (g.IdentifiedTime < mz.Start) mz.Start = g.IdentifiedTime;
                    if (g.EndTime > mz.End)          mz.End   = g.EndTime;
                    if (g.ZoneMin < mz.Min)          mz.Min   = g.ZoneMin;
                    if (g.ZoneMax > mz.Max)          mz.Max   = g.ZoneMax;
                    mz.Rules.Add(rule.Label);
                }
                else
                {
                    // The new zone bridged two merged zones — absorb the second.
                    if (mz.Start < host.Start) host.Start = mz.Start;
                    if (mz.End   > host.End)   host.End   = mz.End;
                    if (mz.Min   < host.Min)   host.Min   = mz.Min;
                    if (mz.Max   > host.Max)   host.Max   = mz.Max;
                    foreach (string rl in mz.Rules) host.Rules.Add(rl);
                    RemoveDrawObject(mz.Tag);
                    RemoveDrawObject(mz.Tag + "_lbl");
                    zs.Merged.RemoveAt(i);
                }
            }

            if (host == null)
            {
                host = new MergedZone
                {
                    Start  = g.IdentifiedTime,
                    End    = g.EndTime,
                    Min    = g.ZoneMin,
                    Max    = g.ZoneMax,
                    IsLong = rule.IsLong,
                    Tag    = "MRGM_" + (rule.IsLong ? "L" : "S") + "_" + (zs.MergedSeq++)
                };
                host.Rules.Add(rule.Label);
                zs.Merged.Add(host);
                if (zs.Merged.Count > 200)
                    zs.Merged.RemoveAt(0);   // drawing stays; zone just stops merging
            }

            string label = host.Rules.Count == 1
                ? rule.Label
                : (host.IsLong ? "LONG" : "SHORT") + " x" + host.Rules.Count;
            DrawZoneRect(zs, host.Tag, host.IsLong, host.Start, host.End, host.Min, host.Max, label);
        }

        private void DrawZoneRect(ZoneSet zs, string tag, bool isLong, DateTime start, DateTime end,
                                  double zoneMin, double zoneMax, string label)
        {
            if (ExportMode) return;   // export runs draw nothing: chart objects are the cost, not the level math
            double pad    = TickSize > 0 ? TickSize : 0.25;
            double top    = zoneMax + pad;
            double bottom = zoneMin - pad;

            Brush zone = isLong ? zs.LongColor : zs.ShortColor;
            if (zone == null) zone = isLong ? Brushes.DeepSkyBlue : Brushes.OrangeRed;

            var rect = Draw.Rectangle(this, tag, false,
                start, bottom, end, top,
                zone, zone, Math.Max(0, Math.Min(100, zs.Opacity)));
            if (rect != null)
            {
                Brush outline = zone.Clone();
                outline.Opacity = Math.Max(0, Math.Min(100, zs.OutlineOpacity)) / 100.0;
                outline.Freeze();
                rect.OutlineStroke = new Stroke(outline, DashStyleHelper.Solid, Math.Max(1, zs.OutlineWidth));
            }

            if (zs.ShowLabels)
                Draw.Text(this, tag + "_lbl", false, label,
                    start, top, 12,
                    zone, new SimpleFont("Arial", 11) { Bold = true },
                    TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);

            if (!zs.DrawTags.Contains(tag))
            {
                zs.DrawTags.Enqueue(tag);
                while (zs.DrawTags.Count > Math.Max(50, zs.MaxDrawings))
                {
                    string old = zs.DrawTags.Dequeue();
                    RemoveDrawObject(old);
                    RemoveDrawObject(old + "_lbl");
                }
            }
        }

        // A frozen LONG zone with price below its floor (or SHORT zone with price
        // above its ceiling) is a FAILED setup, not history — manual clean removes
        // it. Zones price respected stay as the permanent record.
        private int PurgeWrongSideZones(ZoneSet zs, double currentPrice)
        {
            int purged = 0;
            double eps = TickSize > 0 ? TickSize * 0.5 : 0.01;

            if (zs.Active != null)
            {
                var failedKeys = new List<string>();
                foreach (var kv in zs.Active)
                {
                    // Failed = price adverse AND the zone NEVER worked. A zone whose
                    // favorable side was traded through is a success being revisited,
                    // not a failure — it survives every Clean.
                    bool failed = !kv.Value.Worked
                        && (kv.Value.IsLong
                            ? currentPrice < kv.Value.ZoneMin - eps
                            : currentPrice > kv.Value.ZoneMax + eps);
                    if (failed)
                    {
                        RemoveDrawObject(kv.Value.Tag);
                        RemoveDrawObject(kv.Value.Tag + "_lbl");
                        failedKeys.Add(kv.Key);
                        GrpLog(zs, "CLEAN-PURGE " + kv.Value.Tag
                            + " (" + (kv.Value.IsLong ? "LONG" : "SHORT") + ", never worked)"
                            + " zone=" + kv.Value.ZoneMin.ToString("F2") + "-" + kv.Value.ZoneMax.ToString("F2")
                            + " price=" + currentPrice
                            + " ident=" + kv.Value.IdentifiedTime.ToString("MM-dd HH:mm")
                            + " end=" + kv.Value.EndTime.ToString("MM-dd HH:mm"));
                    }
                }
                foreach (string k in failedKeys)
                {
                    zs.Active.Remove(k);
                    if (zs.Dismissed != null)
                    {
                        zs.Dismissed.Add(k);   // tombstone: no resurrection
                        if (zs.Dismissed.Count > 4000)
                            zs.Dismissed.Clear();
                    }
                }
                purged += failedKeys.Count;
            }

            if (zs.Merged != null)
            {
                for (int i = zs.Merged.Count - 1; i >= 0; i--)
                {
                    MergedZone mz = zs.Merged[i];
                    bool failed = !mz.Worked
                        && (mz.IsLong
                            ? currentPrice < mz.Min - eps
                            : currentPrice > mz.Max + eps);
                    if (failed)
                    {
                        RemoveDrawObject(mz.Tag);
                        RemoveDrawObject(mz.Tag + "_lbl");
                        zs.Merged.RemoveAt(i);
                        purged++;
                    }
                }
            }

            return purged;
        }

        // Erase zones still in force so the next detection pass rebuilds them from
        // the surviving levels. Frozen historical zones are left alone.
        private int RepaintActiveGroupZones(ZoneSet zs, DateTime primaryTime)
        {
            int erased = 0;
            if (zs.Active != null)
            {
                var stale = new List<string>();
                foreach (var kv in zs.Active)
                {
                    if (kv.Value.EndTime >= primaryTime)
                    {
                        RemoveDrawObject(kv.Value.Tag);
                        RemoveDrawObject(kv.Value.Tag + "_lbl");
                        stale.Add(kv.Key);
                        GrpLog(zs, "CLEAN-ERASE " + kv.Value.Tag
                            + " zone=" + kv.Value.ZoneMin.ToString("F2") + "-" + kv.Value.ZoneMax.ToString("F2")
                            + " ident=" + kv.Value.IdentifiedTime.ToString("MM-dd HH:mm")
                            + " end=" + kv.Value.EndTime.ToString("MM-dd HH:mm") + " (awaits rebuild)");
                    }
                }
                foreach (string k in stale) zs.Active.Remove(k);
                erased += stale.Count;
            }

            if (zs.Merged != null)
            {
                for (int i = zs.Merged.Count - 1; i >= 0; i--)
                {
                    if (zs.Merged[i].End >= primaryTime)
                    {
                        RemoveDrawObject(zs.Merged[i].Tag);
                        RemoveDrawObject(zs.Merged[i].Tag + "_lbl");
                        zs.Merged.RemoveAt(i);
                        erased++;
                    }
                }
            }

            // Rebuild on the very next tick (not just the next bar's first tick).
            zs.RepaintPending = true;
            return erased;
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

                    // Signal groups: an HTF anchor (e.g. 240m) is only discovered at its
                    // window CLOSE, up to a full window after its LTF partners' windows
                    // ended. Those partners must stay in `tracked` that long or 5m/10m +
                    // 240m combos can never assemble — archiving them early is invisible
                    // to the level drawing (archived levels keep their lines) but silently
                    // starves the zone detector, which only reads `tracked`.
                    double retainMinutes = (double)_tfMinutes[t] * Math.Max(1, MirrorLookbackBars);
                    foreach (ZoneSet zs in _zoneSets)
                        if (zs.MaxTfMinutes > 0)
                            retainMinutes = Math.Max(retainMinutes, zs.MaxTfMinutes + _tfMinutes[t]);
                    DateTime cutoff = now.AddMinutes(-retainMinutes);

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
                            LvlLogLevel("ARCHIVE", tDict[key], -1);
                            _archivedLevels.Add(tDict[key]);
                            _archivedTags.Add(key);
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

                // Button IDs are VERSION-SPECIFIC. Every Mirror from V0034 to V0040 registers
                // "AlightenMirrorV33SettingsBtn" / "AlightenMirrorV33ExportBtn" -- a hardcoded ID
                // that was never bumped. NinjaTrader keys toolbar buttons by that ID, so a button
                // left behind by an older version (teardown goes through Dispatcher.InvokeAsync
                // and is skipped when a recompile discards the instance first) gets REUSED, and
                // the click runs the dead instance's handler. Symptom: "[MirrorV0034] ForceUISync
                // Error" from a version that is on no chart.
                settingsButton = IndicatorVisualStyleHelper.CreateSettingsButton("Mirror Settings", "AlightenMirrorV0041SettingsBtn", (s, e) => OpenSettingsWindow());
                settingsButton.Width = 110;
                settingsButton.ToolTip = "Configure Mirror V0041 visibility settings";
                chartWindow.MainMenu.Add(settingsButton);

                exportButton = IndicatorVisualStyleHelper.CreateSettingsButton("Export Levels", "AlightenMirrorV0041ExportBtn", (s, e) => ExportLevelsToCsv());
                exportButton.Width = 110;
                exportButton.ToolTip = "Export tracked levels to CSV (V0041)";
                chartWindow.MainMenu.Add(exportButton);

                // V0036: one-click Clean Invalid (same as the modal button). Danger
                // palette keeps it red through hover/theme repaints.
                cleanButton = IndicatorVisualStyleHelper.CreateDangerDialogButton("Clean Mirror", (s, e) => { PerformManualRefreshCleanup(); ForceUISync(); });
                cleanButton.Width = 100;
                cleanButton.Height = 28;
                cleanButton.Margin = new Thickness(6, 3, 6, 3);
                cleanButton.ToolTip = "Remove currently-active levels price has crossed, purge failed zones, and rebuild active zones (same as the modal's Clean Invalid)";
                chartWindow.MainMenu.Add(cleanButton);
            } catch (Exception ex) { Print("[AlightenMirrorV0041] toolbar error: " + ex.Message); }
        }

        private void TryRemoveToolbarButton()
        {
            try { if (chartWindow != null && settingsButton != null) chartWindow.MainMenu.Remove(settingsButton); } catch { }
            try { if (chartWindow != null && exportButton != null) chartWindow.MainMenu.Remove(exportButton); } catch { }
            try { if (chartWindow != null && cleanButton != null) chartWindow.MainMenu.Remove(cleanButton); } catch { }
            try { if (settingsWindow != null) settingsWindow.Close(); } catch { }
            settingsButton = null; exportButton = null; cleanButton = null; settingsWindow = null; chartWindow = null;
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
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = System.IO.Path.Combine(folder, $"MirrorLevels_{stamp}.csv");

                int levelRows = 0;
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(path, false))
                {
                    writer.WriteLine("Pattern,Timeframe,Direction,Price Level,Start Date and Time,End Date and Time,Tag,IsLive,Archived,Removed,LastUpdatedBar");

                    for (int p = 0; p < NUM_PAT; p++)
                        for (int t = 0; t < NUM_TF; t++)
                            if (tracked[p][t] != null)
                                foreach (var tl in tracked[p][t].Values)
                                { WriteLevelToCsv(writer, tl, false); levelRows++; }

                    foreach (var tl in _archivedLevels)
                    { WriteLevelToCsv(writer, tl, true); levelRows++; }
                }

                NinjaTrader.Code.Output.Process($"Exported {levelRows} levels to: {path}", PrintTo.OutputTab1);

                // ---- Bars export: every series exactly as the Mirror saw it ----
                // Resampling 1-minute data in Python cannot reproduce NinjaTrader's session
                // boundaries or isResetOnNewTradingDay, so any close-vs-level test needs the
                // real bars rather than a reconstruction of them.
                string barsPath = System.IO.Path.Combine(folder, $"MirrorBars_{stamp}.csv");
                int barRows = ExportBarsToCsv(barsPath);
                NinjaTrader.Code.Output.Process($"Exported {barRows} bars to: {barsPath}", PrintTo.OutputTab1);

                // ---- Provenance sidecar (matches the LevelsDecisionTree .meta.json shape) ----
                string metaPath = System.IO.Path.Combine(folder, $"MirrorExport_{stamp}.meta.json");
                WriteExportMeta(metaPath, levelRows, barRows);
                NinjaTrader.Code.Output.Process($"Exported meta to: {metaPath}", PrintTo.OutputTab1);

                // ---- Research export (events + confluence context) ----
                if (EnableResearchLog && (_researchDone.Count > 0 || _researchOpen.Count > 0))
                {

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
                Print("[AlightenMirrorV0041] Export error: " + ex.Message);
            }
        }

        // Every loaded series, by BarsInProgress: 0 is the chart's own series, 1..7 are the
        // Daily/240m/60m/30m/15m/10m/5m series added in State.Configure. Absolute bar indexing
        // is used throughout because this runs on the UI thread from the toolbar button, where
        // barsAgo-relative access is invalid.
        private int ExportBarsToCsv(string barsPath)
        {
            int rows = 0;
            using (var w = new System.IO.StreamWriter(barsPath, false))
            {
                w.WriteLine("TF,BarIndex,Time,Open,High,Low,Close,Volume");
                var ci = System.Globalization.CultureInfo.InvariantCulture;

                for (int bip = 0; bip < BarsArray.Length; bip++)
                {
                    var b = BarsArray[bip];
                    if (b == null || b.Count == 0) continue;

                    string tfLabel = bip == 0 ? "primary" : (bip - 1 < NUM_TF ? _tfLabels[bip - 1] : "bip" + bip);

                    for (int i = 0; i < b.Count; i++)
                    {
                        w.WriteLine(string.Format(ci, "{0},{1},{2:yyyy-MM-dd HH:mm:ss},{3},{4},{5},{6},{7}",
                            tfLabel, i, b.GetTime(i), b.GetOpen(i), b.GetHigh(i),
                            b.GetLow(i), b.GetClose(i), b.GetVolume(i)));
                        rows++;
                    }
                }
            }
            return rows;
        }

        private static string JsonEscape(string s)
        {
            return s == null ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // Provenance sidecar. The bar spans are recorded PER SERIES because the export depth
        // is set by the chart's Days-to-Load, not by any property here — without them there is
        // no way to tell after the fact how much history a given export actually covered.
        private void WriteExportMeta(string metaPath, int levelRows, int barRows)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendFormat(ci, "  \"instrument\": \"{0}\",\n", JsonEscape(Instrument != null ? Instrument.FullName : ""));
            sb.AppendFormat(ci, "  \"primary_period\": \"{0}\",\n", JsonEscape(BarsPeriod != null ? BarsPeriod.ToString() : ""));
            sb.AppendFormat(ci, "  \"tick_size\": {0},\n", TickSize);
            sb.AppendFormat(ci, "  \"level_rows\": {0},\n", levelRows);
            sb.AppendFormat(ci, "  \"bar_rows\": {0},\n", barRows);
            sb.AppendFormat(ci, "  \"src_bars_to_process\": {0},\n", SrcBarsToProcess);
            sb.AppendFormat(ci, "  \"mirror_lookback_bars\": {0},\n", MirrorLookbackBars);
            sb.AppendFormat(ci, "  \"export_mode\": {0},\n", ExportMode ? "true" : "false");
            sb.AppendFormat(ci, "  \"groups_file\": \"{0}\",\n", JsonEscape(GroupsFileName));
            sb.AppendFormat(ci, "  \"signal_groups_enabled\": {0},\n", EnableSignalGroups ? "true" : "false");
            sb.AppendFormat(ci, "  \"research_target_ticks\": {0},\n", ResearchTargetTicks);

            sb.AppendLine("  \"series\": {");
            for (int bip = 0; bip < BarsArray.Length; bip++)
            {
                var b = BarsArray[bip];
                string tfLabel = bip == 0 ? "primary" : (bip - 1 < NUM_TF ? _tfLabels[bip - 1] : "bip" + bip);
                int n = (b == null) ? 0 : b.Count;
                string first = n > 0 ? b.GetTime(0).ToString("yyyy-MM-dd HH:mm:ss") : "";
                string last = n > 0 ? b.GetTime(n - 1).ToString("yyyy-MM-dd HH:mm:ss") : "";
                sb.AppendFormat(ci, "    \"{0}\": {{ \"bars\": {1}, \"first\": \"{2}\", \"last\": \"{3}\" }}{4}\n",
                    tfLabel, n, first, last, bip == BarsArray.Length - 1 ? "" : ",");
            }
            sb.AppendLine("  },");

            sb.AppendLine("  \"source_indicators\": [\"AlightenMirrorPtAV0010\", \"AlightenMirrorPtBV0005\", \"AlightenMirrorPtGV0002\", \"AlightenMirrorPtHV0002\", \"AlightenMirrorPtFV0003\", \"AlightenMirrorPtJV0007\"],");
            sb.AppendLine("  \"bar_timestamp\": \"close\",");
            sb.AppendLine("  \"exported_by\": \"AlightenMirrorV0041\"");
            sb.AppendLine("}");

            System.IO.File.WriteAllText(metaPath, sb.ToString());
        }

        private void WriteLevelToCsv(System.IO.StreamWriter writer, TrackedLevel tl, bool archived)
        {
            string tfString = _tfLabels[tl.TfIndex];
            string dirString = tl.IsLong ? "Long" : "Short";
            string startStr = tl.HtfStartTime.ToString("yyyy-MM-dd HH:mm:ss");
            string endStr = tl.HtfEndTime.ToString("yyyy-MM-dd HH:mm:ss");
            writer.WriteLine($"{tl.PatternType},{tfString},{dirString},{tl.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)},{startStr},{endStr},{tl.Tag},{(tl.IsLive ? 1 : 0)},{(archived ? 1 : 0)},{(tl.Removed ? 1 : 0)},{tl.LastUpdatedBip0Bar}");
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
		        Print($"[AlightenMirrorV0041] Failed to open settings: {ex.Message}");
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

                // Zones must be redrawn here too. RemoveDrawObjects() above erased them,
                // and UpdateSignalGroups only redraws a zone that is new/extended/tighter —
                // so a frozen historical zone would vanish on any settings toggle. Active
                // zones are additionally queued for a full rebuild by the Clean path.
                foreach (ZoneSet zs in _zoneSets) RedrawGroupZones(zs);

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
		    LvlLogLevel("REMOVE", tl, -1);
		    try { if (!string.IsNullOrEmpty(tl.Tag)) RemoveDrawObject(tl.Tag); } catch { }
		    try { if (!string.IsNullOrEmpty(tl.Tag)) RemoveDrawObject(tl.Tag + "_lbl"); } catch { }
		    try { if (!string.IsNullOrEmpty(tl.Tag) && tDict.ContainsKey(tl.Tag)) tDict.Remove(tl.Tag); } catch { }
		}

		// Runs on the UI thread (toolbar / settings modal) — uses the cached primary
		// time and close, never series barsAgo indexing.
		//
		// The level sweep stays V0036's conservative one (currently-active levels only).
		// EntryV0006 deleted EVERY wrong-side level here, which it could afford because
		// it drew no level lines; doing that in the Mirror would erase historical level
		// drawings the chart is meant to keep. The zone half is EntryV0006's verbatim.
		private void PerformManualRefreshCleanup()
		{
		    if (_lastPrimaryBarTime == Core.Globals.MinDate || double.IsNaN(_lastPrimaryClose)) return;
		    DateTime primaryTime = _lastPrimaryBarTime;
		    double currentPrice = _lastPrimaryClose;
		    for (int p = 0; p < NUM_PAT; p++)
		        for (int t = 0; t < NUM_TF; t++)
		            if (IsTfEnabled(t))
		                CleanupCurrentBarInvalidatedList(tracked[p][t], primaryTime, currentPrice);

		    if (_zoneSets.Length > 0)
		    {
		        foreach (ZoneSet zs in _zoneSets)
		        {
		            int erased = RepaintActiveGroupZones(zs, primaryTime);
		            int purged = PurgeWrongSideZones(zs, currentPrice);
		            GrpLog(zs, "CLEAN price=" + currentPrice + " erased " + erased
		                + " active zone(s) for rebuild, purged " + purged + " failed frozen zone(s)");
		        }
		    }
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

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenMirrorV0041[] cacheAlightenMirrorV0041;
		public AlightenMirrorV0041 AlightenMirrorV0041(bool enablePatternA, bool enablePatternB, bool enablePatternG, bool enablePatternH, bool enablePatternF, bool enablePatternJ, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool showPatternATF1_Daily, bool showPatternATF2_240m, bool showPatternATF3_60m, bool showPatternATF4_30m, bool showPatternATF5_15m, bool showPatternATF6_10m, bool showPatternATF7_5m, bool showPatternBTF1_Daily, bool showPatternBTF2_240m, bool showPatternBTF3_60m, bool showPatternBTF4_30m, bool showPatternBTF5_15m, bool showPatternBTF6_10m, bool showPatternBTF7_5m, bool showPatternGTF1_Daily, bool showPatternGTF2_240m, bool showPatternGTF3_60m, bool showPatternGTF4_30m, bool showPatternGTF5_15m, bool showPatternGTF6_10m, bool showPatternGTF7_5m, bool showPatternHTF1_Daily, bool showPatternHTF2_240m, bool showPatternHTF3_60m, bool showPatternHTF4_30m, bool showPatternHTF5_15m, bool showPatternHTF6_10m, bool showPatternHTF7_5m, bool showPatternFTF1_Daily, bool showPatternFTF2_240m, bool showPatternFTF3_60m, bool showPatternFTF4_30m, bool showPatternFTF5_15m, bool showPatternFTF6_10m, bool showPatternFTF7_5m, bool showPatternJTF1_Daily, bool showPatternJTF2_240m, bool showPatternJTF3_60m, bool showPatternJTF4_30m, bool showPatternJTF5_15m, bool showPatternJTF6_10m, bool showPatternJTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleG, DashStyleHelper levelDashStyleH, DashStyleHelper levelDashStyleF, DashStyleHelper levelDashStyleJ, bool showLevelLabels, int syncThrottleMs, bool enableResearchLog, int researchTargetTicks, bool exportMode, int labelFontSize, int labelOffsetTicks)
		{
			return AlightenMirrorV0041(Input, enablePatternA, enablePatternB, enablePatternG, enablePatternH, enablePatternF, enablePatternJ, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, showPatternATF1_Daily, showPatternATF2_240m, showPatternATF3_60m, showPatternATF4_30m, showPatternATF5_15m, showPatternATF6_10m, showPatternATF7_5m, showPatternBTF1_Daily, showPatternBTF2_240m, showPatternBTF3_60m, showPatternBTF4_30m, showPatternBTF5_15m, showPatternBTF6_10m, showPatternBTF7_5m, showPatternGTF1_Daily, showPatternGTF2_240m, showPatternGTF3_60m, showPatternGTF4_30m, showPatternGTF5_15m, showPatternGTF6_10m, showPatternGTF7_5m, showPatternHTF1_Daily, showPatternHTF2_240m, showPatternHTF3_60m, showPatternHTF4_30m, showPatternHTF5_15m, showPatternHTF6_10m, showPatternHTF7_5m, showPatternFTF1_Daily, showPatternFTF2_240m, showPatternFTF3_60m, showPatternFTF4_30m, showPatternFTF5_15m, showPatternFTF6_10m, showPatternFTF7_5m, showPatternJTF1_Daily, showPatternJTF2_240m, showPatternJTF3_60m, showPatternJTF4_30m, showPatternJTF5_15m, showPatternJTF6_10m, showPatternJTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleG, levelDashStyleH, levelDashStyleF, levelDashStyleJ, showLevelLabels, syncThrottleMs, enableResearchLog, researchTargetTicks, exportMode, labelFontSize, labelOffsetTicks);
		}

		public AlightenMirrorV0041 AlightenMirrorV0041(ISeries<double> input, bool enablePatternA, bool enablePatternB, bool enablePatternG, bool enablePatternH, bool enablePatternF, bool enablePatternJ, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool showPatternATF1_Daily, bool showPatternATF2_240m, bool showPatternATF3_60m, bool showPatternATF4_30m, bool showPatternATF5_15m, bool showPatternATF6_10m, bool showPatternATF7_5m, bool showPatternBTF1_Daily, bool showPatternBTF2_240m, bool showPatternBTF3_60m, bool showPatternBTF4_30m, bool showPatternBTF5_15m, bool showPatternBTF6_10m, bool showPatternBTF7_5m, bool showPatternGTF1_Daily, bool showPatternGTF2_240m, bool showPatternGTF3_60m, bool showPatternGTF4_30m, bool showPatternGTF5_15m, bool showPatternGTF6_10m, bool showPatternGTF7_5m, bool showPatternHTF1_Daily, bool showPatternHTF2_240m, bool showPatternHTF3_60m, bool showPatternHTF4_30m, bool showPatternHTF5_15m, bool showPatternHTF6_10m, bool showPatternHTF7_5m, bool showPatternFTF1_Daily, bool showPatternFTF2_240m, bool showPatternFTF3_60m, bool showPatternFTF4_30m, bool showPatternFTF5_15m, bool showPatternFTF6_10m, bool showPatternFTF7_5m, bool showPatternJTF1_Daily, bool showPatternJTF2_240m, bool showPatternJTF3_60m, bool showPatternJTF4_30m, bool showPatternJTF5_15m, bool showPatternJTF6_10m, bool showPatternJTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleG, DashStyleHelper levelDashStyleH, DashStyleHelper levelDashStyleF, DashStyleHelper levelDashStyleJ, bool showLevelLabels, int syncThrottleMs, bool enableResearchLog, int researchTargetTicks, bool exportMode, int labelFontSize, int labelOffsetTicks)
		{
			if (cacheAlightenMirrorV0041 != null)
				for (int idx = 0; idx < cacheAlightenMirrorV0041.Length; idx++)
					if (cacheAlightenMirrorV0041[idx] != null && cacheAlightenMirrorV0041[idx].EnablePatternA == enablePatternA && cacheAlightenMirrorV0041[idx].EnablePatternB == enablePatternB && cacheAlightenMirrorV0041[idx].EnablePatternG == enablePatternG && cacheAlightenMirrorV0041[idx].EnablePatternH == enablePatternH && cacheAlightenMirrorV0041[idx].EnablePatternF == enablePatternF && cacheAlightenMirrorV0041[idx].EnablePatternJ == enablePatternJ && cacheAlightenMirrorV0041[idx].SrcBarsToProcess == srcBarsToProcess && cacheAlightenMirrorV0041[idx].MirrorLookbackBars == mirrorLookbackBars && cacheAlightenMirrorV0041[idx].EnableInvalidatedCleanup == enableInvalidatedCleanup && cacheAlightenMirrorV0041[idx].ShowPatternATF1_Daily == showPatternATF1_Daily && cacheAlightenMirrorV0041[idx].ShowPatternATF2_240m == showPatternATF2_240m && cacheAlightenMirrorV0041[idx].ShowPatternATF3_60m == showPatternATF3_60m && cacheAlightenMirrorV0041[idx].ShowPatternATF4_30m == showPatternATF4_30m && cacheAlightenMirrorV0041[idx].ShowPatternATF5_15m == showPatternATF5_15m && cacheAlightenMirrorV0041[idx].ShowPatternATF6_10m == showPatternATF6_10m && cacheAlightenMirrorV0041[idx].ShowPatternATF7_5m == showPatternATF7_5m && cacheAlightenMirrorV0041[idx].ShowPatternBTF1_Daily == showPatternBTF1_Daily && cacheAlightenMirrorV0041[idx].ShowPatternBTF2_240m == showPatternBTF2_240m && cacheAlightenMirrorV0041[idx].ShowPatternBTF3_60m == showPatternBTF3_60m && cacheAlightenMirrorV0041[idx].ShowPatternBTF4_30m == showPatternBTF4_30m && cacheAlightenMirrorV0041[idx].ShowPatternBTF5_15m == showPatternBTF5_15m && cacheAlightenMirrorV0041[idx].ShowPatternBTF6_10m == showPatternBTF6_10m && cacheAlightenMirrorV0041[idx].ShowPatternBTF7_5m == showPatternBTF7_5m && cacheAlightenMirrorV0041[idx].ShowPatternGTF1_Daily == showPatternGTF1_Daily && cacheAlightenMirrorV0041[idx].ShowPatternGTF2_240m == showPatternGTF2_240m && cacheAlightenMirrorV0041[idx].ShowPatternGTF3_60m == showPatternGTF3_60m && cacheAlightenMirrorV0041[idx].ShowPatternGTF4_30m == showPatternGTF4_30m && cacheAlightenMirrorV0041[idx].ShowPatternGTF5_15m == showPatternGTF5_15m && cacheAlightenMirrorV0041[idx].ShowPatternGTF6_10m == showPatternGTF6_10m && cacheAlightenMirrorV0041[idx].ShowPatternGTF7_5m == showPatternGTF7_5m && cacheAlightenMirrorV0041[idx].ShowPatternHTF1_Daily == showPatternHTF1_Daily && cacheAlightenMirrorV0041[idx].ShowPatternHTF2_240m == showPatternHTF2_240m && cacheAlightenMirrorV0041[idx].ShowPatternHTF3_60m == showPatternHTF3_60m && cacheAlightenMirrorV0041[idx].ShowPatternHTF4_30m == showPatternHTF4_30m && cacheAlightenMirrorV0041[idx].ShowPatternHTF5_15m == showPatternHTF5_15m && cacheAlightenMirrorV0041[idx].ShowPatternHTF6_10m == showPatternHTF6_10m && cacheAlightenMirrorV0041[idx].ShowPatternHTF7_5m == showPatternHTF7_5m && cacheAlightenMirrorV0041[idx].ShowPatternFTF1_Daily == showPatternFTF1_Daily && cacheAlightenMirrorV0041[idx].ShowPatternFTF2_240m == showPatternFTF2_240m && cacheAlightenMirrorV0041[idx].ShowPatternFTF3_60m == showPatternFTF3_60m && cacheAlightenMirrorV0041[idx].ShowPatternFTF4_30m == showPatternFTF4_30m && cacheAlightenMirrorV0041[idx].ShowPatternFTF5_15m == showPatternFTF5_15m && cacheAlightenMirrorV0041[idx].ShowPatternFTF6_10m == showPatternFTF6_10m && cacheAlightenMirrorV0041[idx].ShowPatternFTF7_5m == showPatternFTF7_5m && cacheAlightenMirrorV0041[idx].ShowPatternJTF1_Daily == showPatternJTF1_Daily && cacheAlightenMirrorV0041[idx].ShowPatternJTF2_240m == showPatternJTF2_240m && cacheAlightenMirrorV0041[idx].ShowPatternJTF3_60m == showPatternJTF3_60m && cacheAlightenMirrorV0041[idx].ShowPatternJTF4_30m == showPatternJTF4_30m && cacheAlightenMirrorV0041[idx].ShowPatternJTF5_15m == showPatternJTF5_15m && cacheAlightenMirrorV0041[idx].ShowPatternJTF6_10m == showPatternJTF6_10m && cacheAlightenMirrorV0041[idx].ShowPatternJTF7_5m == showPatternJTF7_5m && cacheAlightenMirrorV0041[idx].ColorTF1 == colorTF1 && cacheAlightenMirrorV0041[idx].ColorTF2 == colorTF2 && cacheAlightenMirrorV0041[idx].ColorTF3 == colorTF3 && cacheAlightenMirrorV0041[idx].ColorTF4 == colorTF4 && cacheAlightenMirrorV0041[idx].ColorTF5 == colorTF5 && cacheAlightenMirrorV0041[idx].ColorTF6 == colorTF6 && cacheAlightenMirrorV0041[idx].ColorTF7 == colorTF7 && cacheAlightenMirrorV0041[idx].LevelWidth == levelWidth && cacheAlightenMirrorV0041[idx].LevelDashStyleA == levelDashStyleA && cacheAlightenMirrorV0041[idx].LevelDashStyleB == levelDashStyleB && cacheAlightenMirrorV0041[idx].LevelDashStyleG == levelDashStyleG && cacheAlightenMirrorV0041[idx].LevelDashStyleH == levelDashStyleH && cacheAlightenMirrorV0041[idx].LevelDashStyleF == levelDashStyleF && cacheAlightenMirrorV0041[idx].LevelDashStyleJ == levelDashStyleJ && cacheAlightenMirrorV0041[idx].ShowLevelLabels == showLevelLabels && cacheAlightenMirrorV0041[idx].SyncThrottleMs == syncThrottleMs && cacheAlightenMirrorV0041[idx].EnableResearchLog == enableResearchLog && cacheAlightenMirrorV0041[idx].ResearchTargetTicks == researchTargetTicks && cacheAlightenMirrorV0041[idx].ExportMode == exportMode && cacheAlightenMirrorV0041[idx].LabelFontSize == labelFontSize && cacheAlightenMirrorV0041[idx].LabelOffsetTicks == labelOffsetTicks && cacheAlightenMirrorV0041[idx].EqualsInput(input))
						return cacheAlightenMirrorV0041[idx];
			return CacheIndicator<AlightenMirrorV0041>(new AlightenMirrorV0041(){ EnablePatternA = enablePatternA, EnablePatternB = enablePatternB, EnablePatternG = enablePatternG, EnablePatternH = enablePatternH, EnablePatternF = enablePatternF, EnablePatternJ = enablePatternJ, SrcBarsToProcess = srcBarsToProcess, MirrorLookbackBars = mirrorLookbackBars, EnableInvalidatedCleanup = enableInvalidatedCleanup, ShowPatternATF1_Daily = showPatternATF1_Daily, ShowPatternATF2_240m = showPatternATF2_240m, ShowPatternATF3_60m = showPatternATF3_60m, ShowPatternATF4_30m = showPatternATF4_30m, ShowPatternATF5_15m = showPatternATF5_15m, ShowPatternATF6_10m = showPatternATF6_10m, ShowPatternATF7_5m = showPatternATF7_5m, ShowPatternBTF1_Daily = showPatternBTF1_Daily, ShowPatternBTF2_240m = showPatternBTF2_240m, ShowPatternBTF3_60m = showPatternBTF3_60m, ShowPatternBTF4_30m = showPatternBTF4_30m, ShowPatternBTF5_15m = showPatternBTF5_15m, ShowPatternBTF6_10m = showPatternBTF6_10m, ShowPatternBTF7_5m = showPatternBTF7_5m, ShowPatternGTF1_Daily = showPatternGTF1_Daily, ShowPatternGTF2_240m = showPatternGTF2_240m, ShowPatternGTF3_60m = showPatternGTF3_60m, ShowPatternGTF4_30m = showPatternGTF4_30m, ShowPatternGTF5_15m = showPatternGTF5_15m, ShowPatternGTF6_10m = showPatternGTF6_10m, ShowPatternGTF7_5m = showPatternGTF7_5m, ShowPatternHTF1_Daily = showPatternHTF1_Daily, ShowPatternHTF2_240m = showPatternHTF2_240m, ShowPatternHTF3_60m = showPatternHTF3_60m, ShowPatternHTF4_30m = showPatternHTF4_30m, ShowPatternHTF5_15m = showPatternHTF5_15m, ShowPatternHTF6_10m = showPatternHTF6_10m, ShowPatternHTF7_5m = showPatternHTF7_5m, ShowPatternFTF1_Daily = showPatternFTF1_Daily, ShowPatternFTF2_240m = showPatternFTF2_240m, ShowPatternFTF3_60m = showPatternFTF3_60m, ShowPatternFTF4_30m = showPatternFTF4_30m, ShowPatternFTF5_15m = showPatternFTF5_15m, ShowPatternFTF6_10m = showPatternFTF6_10m, ShowPatternFTF7_5m = showPatternFTF7_5m, ShowPatternJTF1_Daily = showPatternJTF1_Daily, ShowPatternJTF2_240m = showPatternJTF2_240m, ShowPatternJTF3_60m = showPatternJTF3_60m, ShowPatternJTF4_30m = showPatternJTF4_30m, ShowPatternJTF5_15m = showPatternJTF5_15m, ShowPatternJTF6_10m = showPatternJTF6_10m, ShowPatternJTF7_5m = showPatternJTF7_5m, ColorTF1 = colorTF1, ColorTF2 = colorTF2, ColorTF3 = colorTF3, ColorTF4 = colorTF4, ColorTF5 = colorTF5, ColorTF6 = colorTF6, ColorTF7 = colorTF7, LevelWidth = levelWidth, LevelDashStyleA = levelDashStyleA, LevelDashStyleB = levelDashStyleB, LevelDashStyleG = levelDashStyleG, LevelDashStyleH = levelDashStyleH, LevelDashStyleF = levelDashStyleF, LevelDashStyleJ = levelDashStyleJ, ShowLevelLabels = showLevelLabels, SyncThrottleMs = syncThrottleMs, EnableResearchLog = enableResearchLog, ResearchTargetTicks = researchTargetTicks, ExportMode = exportMode, LabelFontSize = labelFontSize, LabelOffsetTicks = labelOffsetTicks }, input, ref cacheAlightenMirrorV0041);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenMirrorV0041 AlightenMirrorV0041(bool enablePatternA, bool enablePatternB, bool enablePatternG, bool enablePatternH, bool enablePatternF, bool enablePatternJ, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool showPatternATF1_Daily, bool showPatternATF2_240m, bool showPatternATF3_60m, bool showPatternATF4_30m, bool showPatternATF5_15m, bool showPatternATF6_10m, bool showPatternATF7_5m, bool showPatternBTF1_Daily, bool showPatternBTF2_240m, bool showPatternBTF3_60m, bool showPatternBTF4_30m, bool showPatternBTF5_15m, bool showPatternBTF6_10m, bool showPatternBTF7_5m, bool showPatternGTF1_Daily, bool showPatternGTF2_240m, bool showPatternGTF3_60m, bool showPatternGTF4_30m, bool showPatternGTF5_15m, bool showPatternGTF6_10m, bool showPatternGTF7_5m, bool showPatternHTF1_Daily, bool showPatternHTF2_240m, bool showPatternHTF3_60m, bool showPatternHTF4_30m, bool showPatternHTF5_15m, bool showPatternHTF6_10m, bool showPatternHTF7_5m, bool showPatternFTF1_Daily, bool showPatternFTF2_240m, bool showPatternFTF3_60m, bool showPatternFTF4_30m, bool showPatternFTF5_15m, bool showPatternFTF6_10m, bool showPatternFTF7_5m, bool showPatternJTF1_Daily, bool showPatternJTF2_240m, bool showPatternJTF3_60m, bool showPatternJTF4_30m, bool showPatternJTF5_15m, bool showPatternJTF6_10m, bool showPatternJTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleG, DashStyleHelper levelDashStyleH, DashStyleHelper levelDashStyleF, DashStyleHelper levelDashStyleJ, bool showLevelLabels, int syncThrottleMs, bool enableResearchLog, int researchTargetTicks, bool exportMode, int labelFontSize, int labelOffsetTicks)
		{
			return indicator.AlightenMirrorV0041(Input, enablePatternA, enablePatternB, enablePatternG, enablePatternH, enablePatternF, enablePatternJ, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, showPatternATF1_Daily, showPatternATF2_240m, showPatternATF3_60m, showPatternATF4_30m, showPatternATF5_15m, showPatternATF6_10m, showPatternATF7_5m, showPatternBTF1_Daily, showPatternBTF2_240m, showPatternBTF3_60m, showPatternBTF4_30m, showPatternBTF5_15m, showPatternBTF6_10m, showPatternBTF7_5m, showPatternGTF1_Daily, showPatternGTF2_240m, showPatternGTF3_60m, showPatternGTF4_30m, showPatternGTF5_15m, showPatternGTF6_10m, showPatternGTF7_5m, showPatternHTF1_Daily, showPatternHTF2_240m, showPatternHTF3_60m, showPatternHTF4_30m, showPatternHTF5_15m, showPatternHTF6_10m, showPatternHTF7_5m, showPatternFTF1_Daily, showPatternFTF2_240m, showPatternFTF3_60m, showPatternFTF4_30m, showPatternFTF5_15m, showPatternFTF6_10m, showPatternFTF7_5m, showPatternJTF1_Daily, showPatternJTF2_240m, showPatternJTF3_60m, showPatternJTF4_30m, showPatternJTF5_15m, showPatternJTF6_10m, showPatternJTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleG, levelDashStyleH, levelDashStyleF, levelDashStyleJ, showLevelLabels, syncThrottleMs, enableResearchLog, researchTargetTicks, exportMode, labelFontSize, labelOffsetTicks);
		}

		public Indicators.AlightenMirrorV0041 AlightenMirrorV0041(ISeries<double> input , bool enablePatternA, bool enablePatternB, bool enablePatternG, bool enablePatternH, bool enablePatternF, bool enablePatternJ, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool showPatternATF1_Daily, bool showPatternATF2_240m, bool showPatternATF3_60m, bool showPatternATF4_30m, bool showPatternATF5_15m, bool showPatternATF6_10m, bool showPatternATF7_5m, bool showPatternBTF1_Daily, bool showPatternBTF2_240m, bool showPatternBTF3_60m, bool showPatternBTF4_30m, bool showPatternBTF5_15m, bool showPatternBTF6_10m, bool showPatternBTF7_5m, bool showPatternGTF1_Daily, bool showPatternGTF2_240m, bool showPatternGTF3_60m, bool showPatternGTF4_30m, bool showPatternGTF5_15m, bool showPatternGTF6_10m, bool showPatternGTF7_5m, bool showPatternHTF1_Daily, bool showPatternHTF2_240m, bool showPatternHTF3_60m, bool showPatternHTF4_30m, bool showPatternHTF5_15m, bool showPatternHTF6_10m, bool showPatternHTF7_5m, bool showPatternFTF1_Daily, bool showPatternFTF2_240m, bool showPatternFTF3_60m, bool showPatternFTF4_30m, bool showPatternFTF5_15m, bool showPatternFTF6_10m, bool showPatternFTF7_5m, bool showPatternJTF1_Daily, bool showPatternJTF2_240m, bool showPatternJTF3_60m, bool showPatternJTF4_30m, bool showPatternJTF5_15m, bool showPatternJTF6_10m, bool showPatternJTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleG, DashStyleHelper levelDashStyleH, DashStyleHelper levelDashStyleF, DashStyleHelper levelDashStyleJ, bool showLevelLabels, int syncThrottleMs, bool enableResearchLog, int researchTargetTicks, bool exportMode, int labelFontSize, int labelOffsetTicks)
		{
			return indicator.AlightenMirrorV0041(input, enablePatternA, enablePatternB, enablePatternG, enablePatternH, enablePatternF, enablePatternJ, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, showPatternATF1_Daily, showPatternATF2_240m, showPatternATF3_60m, showPatternATF4_30m, showPatternATF5_15m, showPatternATF6_10m, showPatternATF7_5m, showPatternBTF1_Daily, showPatternBTF2_240m, showPatternBTF3_60m, showPatternBTF4_30m, showPatternBTF5_15m, showPatternBTF6_10m, showPatternBTF7_5m, showPatternGTF1_Daily, showPatternGTF2_240m, showPatternGTF3_60m, showPatternGTF4_30m, showPatternGTF5_15m, showPatternGTF6_10m, showPatternGTF7_5m, showPatternHTF1_Daily, showPatternHTF2_240m, showPatternHTF3_60m, showPatternHTF4_30m, showPatternHTF5_15m, showPatternHTF6_10m, showPatternHTF7_5m, showPatternFTF1_Daily, showPatternFTF2_240m, showPatternFTF3_60m, showPatternFTF4_30m, showPatternFTF5_15m, showPatternFTF6_10m, showPatternFTF7_5m, showPatternJTF1_Daily, showPatternJTF2_240m, showPatternJTF3_60m, showPatternJTF4_30m, showPatternJTF5_15m, showPatternJTF6_10m, showPatternJTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleG, levelDashStyleH, levelDashStyleF, levelDashStyleJ, showLevelLabels, syncThrottleMs, enableResearchLog, researchTargetTicks, exportMode, labelFontSize, labelOffsetTicks);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenMirrorV0041 AlightenMirrorV0041(bool enablePatternA, bool enablePatternB, bool enablePatternG, bool enablePatternH, bool enablePatternF, bool enablePatternJ, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool showPatternATF1_Daily, bool showPatternATF2_240m, bool showPatternATF3_60m, bool showPatternATF4_30m, bool showPatternATF5_15m, bool showPatternATF6_10m, bool showPatternATF7_5m, bool showPatternBTF1_Daily, bool showPatternBTF2_240m, bool showPatternBTF3_60m, bool showPatternBTF4_30m, bool showPatternBTF5_15m, bool showPatternBTF6_10m, bool showPatternBTF7_5m, bool showPatternGTF1_Daily, bool showPatternGTF2_240m, bool showPatternGTF3_60m, bool showPatternGTF4_30m, bool showPatternGTF5_15m, bool showPatternGTF6_10m, bool showPatternGTF7_5m, bool showPatternHTF1_Daily, bool showPatternHTF2_240m, bool showPatternHTF3_60m, bool showPatternHTF4_30m, bool showPatternHTF5_15m, bool showPatternHTF6_10m, bool showPatternHTF7_5m, bool showPatternFTF1_Daily, bool showPatternFTF2_240m, bool showPatternFTF3_60m, bool showPatternFTF4_30m, bool showPatternFTF5_15m, bool showPatternFTF6_10m, bool showPatternFTF7_5m, bool showPatternJTF1_Daily, bool showPatternJTF2_240m, bool showPatternJTF3_60m, bool showPatternJTF4_30m, bool showPatternJTF5_15m, bool showPatternJTF6_10m, bool showPatternJTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleG, DashStyleHelper levelDashStyleH, DashStyleHelper levelDashStyleF, DashStyleHelper levelDashStyleJ, bool showLevelLabels, int syncThrottleMs, bool enableResearchLog, int researchTargetTicks, bool exportMode, int labelFontSize, int labelOffsetTicks)
		{
			return indicator.AlightenMirrorV0041(Input, enablePatternA, enablePatternB, enablePatternG, enablePatternH, enablePatternF, enablePatternJ, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, showPatternATF1_Daily, showPatternATF2_240m, showPatternATF3_60m, showPatternATF4_30m, showPatternATF5_15m, showPatternATF6_10m, showPatternATF7_5m, showPatternBTF1_Daily, showPatternBTF2_240m, showPatternBTF3_60m, showPatternBTF4_30m, showPatternBTF5_15m, showPatternBTF6_10m, showPatternBTF7_5m, showPatternGTF1_Daily, showPatternGTF2_240m, showPatternGTF3_60m, showPatternGTF4_30m, showPatternGTF5_15m, showPatternGTF6_10m, showPatternGTF7_5m, showPatternHTF1_Daily, showPatternHTF2_240m, showPatternHTF3_60m, showPatternHTF4_30m, showPatternHTF5_15m, showPatternHTF6_10m, showPatternHTF7_5m, showPatternFTF1_Daily, showPatternFTF2_240m, showPatternFTF3_60m, showPatternFTF4_30m, showPatternFTF5_15m, showPatternFTF6_10m, showPatternFTF7_5m, showPatternJTF1_Daily, showPatternJTF2_240m, showPatternJTF3_60m, showPatternJTF4_30m, showPatternJTF5_15m, showPatternJTF6_10m, showPatternJTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleG, levelDashStyleH, levelDashStyleF, levelDashStyleJ, showLevelLabels, syncThrottleMs, enableResearchLog, researchTargetTicks, exportMode, labelFontSize, labelOffsetTicks);
		}

		public Indicators.AlightenMirrorV0041 AlightenMirrorV0041(ISeries<double> input , bool enablePatternA, bool enablePatternB, bool enablePatternG, bool enablePatternH, bool enablePatternF, bool enablePatternJ, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool showPatternATF1_Daily, bool showPatternATF2_240m, bool showPatternATF3_60m, bool showPatternATF4_30m, bool showPatternATF5_15m, bool showPatternATF6_10m, bool showPatternATF7_5m, bool showPatternBTF1_Daily, bool showPatternBTF2_240m, bool showPatternBTF3_60m, bool showPatternBTF4_30m, bool showPatternBTF5_15m, bool showPatternBTF6_10m, bool showPatternBTF7_5m, bool showPatternGTF1_Daily, bool showPatternGTF2_240m, bool showPatternGTF3_60m, bool showPatternGTF4_30m, bool showPatternGTF5_15m, bool showPatternGTF6_10m, bool showPatternGTF7_5m, bool showPatternHTF1_Daily, bool showPatternHTF2_240m, bool showPatternHTF3_60m, bool showPatternHTF4_30m, bool showPatternHTF5_15m, bool showPatternHTF6_10m, bool showPatternHTF7_5m, bool showPatternFTF1_Daily, bool showPatternFTF2_240m, bool showPatternFTF3_60m, bool showPatternFTF4_30m, bool showPatternFTF5_15m, bool showPatternFTF6_10m, bool showPatternFTF7_5m, bool showPatternJTF1_Daily, bool showPatternJTF2_240m, bool showPatternJTF3_60m, bool showPatternJTF4_30m, bool showPatternJTF5_15m, bool showPatternJTF6_10m, bool showPatternJTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleG, DashStyleHelper levelDashStyleH, DashStyleHelper levelDashStyleF, DashStyleHelper levelDashStyleJ, bool showLevelLabels, int syncThrottleMs, bool enableResearchLog, int researchTargetTicks, bool exportMode, int labelFontSize, int labelOffsetTicks)
		{
			return indicator.AlightenMirrorV0041(input, enablePatternA, enablePatternB, enablePatternG, enablePatternH, enablePatternF, enablePatternJ, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, showPatternATF1_Daily, showPatternATF2_240m, showPatternATF3_60m, showPatternATF4_30m, showPatternATF5_15m, showPatternATF6_10m, showPatternATF7_5m, showPatternBTF1_Daily, showPatternBTF2_240m, showPatternBTF3_60m, showPatternBTF4_30m, showPatternBTF5_15m, showPatternBTF6_10m, showPatternBTF7_5m, showPatternGTF1_Daily, showPatternGTF2_240m, showPatternGTF3_60m, showPatternGTF4_30m, showPatternGTF5_15m, showPatternGTF6_10m, showPatternGTF7_5m, showPatternHTF1_Daily, showPatternHTF2_240m, showPatternHTF3_60m, showPatternHTF4_30m, showPatternHTF5_15m, showPatternHTF6_10m, showPatternHTF7_5m, showPatternFTF1_Daily, showPatternFTF2_240m, showPatternFTF3_60m, showPatternFTF4_30m, showPatternFTF5_15m, showPatternFTF6_10m, showPatternFTF7_5m, showPatternJTF1_Daily, showPatternJTF2_240m, showPatternJTF3_60m, showPatternJTF4_30m, showPatternJTF5_15m, showPatternJTF6_10m, showPatternJTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleG, levelDashStyleH, levelDashStyleF, levelDashStyleJ, showLevelLabels, syncThrottleMs, enableResearchLog, researchTargetTicks, exportMode, labelFontSize, labelOffsetTicks);
		}
	}
}

#endregion

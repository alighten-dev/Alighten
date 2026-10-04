/* V0046 = V0045Signal + the ZONE-BREAK RETEST ENTRY SIGNAL (group "15. Retest Entry Signal").
   Purely additive. With "Enable Retest Signal" OFF this file behaves exactly like V0045 except
   for ONE deliberate visual change: the zone-cross marker is now a configurable SYMBOL (default
   "★") drawn by Draw.Text in the get-ready colour instead of Draw.ArrowUp/ArrowDown. Same tag,
   same FIFO cap, same Clean/redraw path - only the glyph changed.

   The three stages, all gated on EnableRetestSignal (ShowCrossArrows stays the master toggle
   for stage 0, exactly as in V0045):

     STAGE 0  GET READY   - CheckZoneCross' existing break test fires. The ★ is drawn and a
                            RETEST WATCH is registered holding the zone's tag, direction,
                            ZoneMin/ZoneMax, the broken EDGE (ZoneMax for a long break, ZoneMin
                            for a short), the zone's EndTime and the break bar/time.
     STAGE 1  PROVISIONAL - a later bar's WICK reaches into the zone band projected forward
                            ([min(ZoneMin,Edge) - tol, max(ZoneMax,Edge) + tol], tol = the wick
                            tolerance in ticks, default 5) AND Nebula's new BrightState plot
                            agrees with the direction (+1 long / -1 short) on that bar. A "▲"/"▼"
                            is drawn in the provisional colour.
     STAGE 2  CONFIRMED   - the CHART-SERIES pivot engine registers a confirmed pivot LOW (long)
                            or HIGH (short) within N bars of the provisional bar (default 3).
                            The same tag is redrawn in the confirmed colour (Draw.* updates in
                            place, never restacks) and the signal plots are published.

   CANCELLED by a bar that CLOSES back through the zone against the break (long watch: close <
   ZoneMin, short watch: close > ZoneMax) - the unconfirmed mark is removed - or by the zone's
   own lifetime running out (EndTime + the existing CrossGraceMins).

   THE CHART-SERIES PIVOT ENGINE is a second, independent instance of the DAILY bias engine's
   rules, run on BarsInProgress 0. Same six two-bar conditions, same same-side replacement, and
   the same definition of CONFIRMED (a pivot is confirmed once a pivot of the opposite side has
   been registered after it - which is the natural one-bar-or-more lag stage 2 relies on). The
   daily engine (_dbPivot* / ProcessDailyBiasBar / DbProcessPivot) is UNTOUCHED; the chart engine
   is a literal transcription of those rules onto a PivotEngine instance, so the two can never
   share state. See PivotProcessBar/PivotRegister - keep them in step with the daily pair.

   NEW PLOTS, appended LAST so every existing plot index is unchanged (42..45):
     42 RetestSignal    +2 confirmed long, +1 provisional long, -1 provisional short, -2 confirmed short
     43 RetestPrice     the band edge the wick touched, 0 when none
     44 RetestZoneEdge  the broken edge of the active watch, 0 when none
     45 GetReadyState   +1 long break this bar, -1 short break this bar, 0 none

   Nebula is HOSTED calc-only (all drawing, alerts and candle colouring off) purely to read its
   new transparent "BrightState" plot. Nothing else about Nebula is used. - By Alighten */
/* Version ID: 2026-08-29a AlightenMirrorV0047Signal — RESET. An exact copy of
   AlightenMirrorV0041 as of 2026-08-29, carrying nothing but the rename: class/Name,
   version-specific toolbar button IDs and captions ("Signal Settings" / "Signal Export" /
   "Clean Signal"), and its own diagnostic log names (MirrorV0046SignalLevels.log,
   MirrorZonesV0046Signal_*.log) so it can sit on the same chart as V0041 without either
   clobbering the other. Zone INPUT files are unchanged - the same production
   MirrorGroupsV0040.txt / MirrorInsideZonesV0040.txt.

   Prior experimental work (a primary-series pivot engine ported from AlightenBiasV0003,
   four structure-log CSVs, pivot/zone/sweep markers and a zone-view mode) was removed
   in full on 2026-08-29. None of it survives here.

   Inherited from V0041: V0036 + SIGNAL GROUP ZONES (harvested from
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
   pattern source AlightenMirrorPtJV0008 (calc-only, all 47 ctor args, drawing suppressed)
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
/* V0045 = V0043Signal + the daily-bias pivot fix.
   ProcessDailyBiasBar now fires all SIX of AlightenBiasV0003's two-bar pivot conditions. The
   port had only four, so an OUTSIDE day (higher high AND lower low) recorded one pivot where
   the Bias records two. Pivots must alternate high/low, so a dropped pivot also shifted every
   later same-side replacement: 284 vs 220 pivots over 775 ETH daily bars, with the visible
   N-level set differing on 73.5% of days. Verified 2026-09-16 - the 8/24/2026 level at 29405
   reappeared. Otherwise identical to V0043Signal apart from the version rename. */
/* V0045 = V0044Signal hosting AlightenMirrorPtJV0008 instead of PtJV0007. Nothing else differs.

   PtJV0008 publishes EVERY qualifying Pattern J node on the forming HTF bar (all four slots), where
   V0007 published one node per side into slot 0 only - and picked it by Dictionary enumeration
   order, so which live J level a hosting Mirror saw was effectively arbitrary. Consequence measured
   on 2026-09-17 02:50 (10m): the live J level was 29457.00, 19 ticks from A@29461.75, so inside
   rule "A10L, J10L; 10T" refused to build a zone; when the bar closed, nodes 29459.25/29459.50/
   29460.00 arrived 7-10 ticks from A and the zone appeared at 03:01. All four nodes existed during
   the forming bar.

   THIS IS AN EXPERIMENT. Provisional values repaint by design (PtJ's own comment says so), so
   publishing four live nodes instead of one means more inside zones appearing and vanishing
   intrabar. Run V0044 and V0045 side by side and decide whether earlier live zones are worth that.
   Test cases: 2026-09-17 02:50 (should now appear live) and 2026-09-15 14:50 (worked before by
   luck of the hash order - must not regress). - By Alighten */

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


    public class AlightenMirrorV0047Signal : Indicator
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
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + LogStamp() + " " + msg + "\r\n");
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

        // ---- Zone-cross signal --------------------------------------------------------
        // LONG: a bar closes up through a SHORT zone while a LONG zone sits below it.
        // SHORT: the mirror - closes down through a LONG zone with a SHORT zone above.
        // No wick and no arming window: the only history it needs is that the opposing
        // zone exists on the far side, which is known when the bar closes.
        private int _xLastBar = -1;
        // The fired signals are kept with their full draw parameters, not just their tags:
        // the manual Clean calls RemoveDrawObjects(), which wipes every object this
        // indicator owns, and only levels/zones/daily-bias were being re-issued afterwards.
        // A signal is a historical fact - it stays even if the zone behind it is later purged.
        private class CrossMark
        {
            public string Tag;
            public DateTime Time;
            public double Price;
            public bool IsLong;
        }
        private Queue<CrossMark> _xMarks;

        // ---- V0046 retest entry signal -------------------------------------------------
        // A break registers a WATCH. The watch lives until price closes back through the zone
        // against the break, or until the zone's own window (+ CrossGraceMins) expires.
        private class RetestWatch
        {
            public string   ZoneTag;      // the ActiveGroup key that was broken
            public bool     IsLong;       // direction of the BREAK (long = closed UP through a SHORT zone)
            public double   ZoneMin, ZoneMax;
            public double   Edge;         // the broken edge: ZoneMax for a long break, ZoneMin for a short
            public DateTime ZoneEndTime;  // the zone's own lifetime; + CrossGraceMins = the watch window
            public int      BreakBar;
            public DateTime BreakTime;

            // ARMED / DISARMED, never killed by price. A close back through the zone
            // against the break disarms; a close through it again in the break direction
            // re-arms. The watch itself lives until the zone's own end time + grace.
            public bool     Armed;
            public bool     Provisional;  // any candidate is live (kept for KillWatch/redraw)
            public List<RetestCand> Cands = new List<RetestCand>(8);
            public int      ProvBar;
            public DateTime ProvTime;
            public double   ProvPrice;    // the band edge the wick reached
            public string   MarkTag;      // stable tag: provisional and confirmed share it
        }
        private List<RetestWatch> _rtWatches;
        private int _rtLastBar = -1;

        // Log stamping, so log lines can be matched to market-replay time and to fills in the NT database.
        // _logLoadId = wall-clock date/time of this load: a refresh appends a NEW run, never overwrites, and
        // the id tells the runs apart.
        private string _logLoadId = "L?";

        // "L<load> [State] mkt=<market time> bar=<chart bar>". mkt = the bar being processed while history
        // loads, the replay/market clock (Globals.Now) in realtime; bar = the chart's current bar stamp.
        // All Mirror logs live in one folder (default Documents\NinjaTrader 8\Mirror Logs), created on demand.
        // Rules files (MirrorGroups*.txt, MirrorInsideZones*.txt) stay in Documents\NinjaTrader 8.
        private string LogFile(string fileName)
        {
            string folder = string.IsNullOrWhiteSpace(LogFolder) ? "Mirror Logs" : LogFolder.Trim();
            if (!System.IO.Path.IsPathRooted(folder))
                folder = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, folder);
            System.IO.Directory.CreateDirectory(folder);
            return System.IO.Path.Combine(folder, fileName);
        }

        private string LogStamp()
        {
            string mkt = "-", bar = "-";
            try
            {
                if (CurrentBars != null && CurrentBars.Length > 0 && CurrentBars[0] >= 0)
                    bar = Times[0][0].ToString("yyyy-MM-dd HH:mm:ss");
                mkt = State == State.Realtime ? NinjaTrader.Core.Globals.Now.ToString("yyyy-MM-dd HH:mm:ss") : bar;
            }
            catch { }
            return _logLoadId + " [" + State + "] mkt=" + mkt + " bar=" + bar;
        }
        private HashSet<string> _rtAlerted = new HashSet<string>();   // one alert per bar + side + stage
        private const int MAX_RETEST_CANDS = 8;   // touching bars awaiting a pivot verdict
        private const int MAX_RETEST_WATCHES = 64;

        // Drawn retest marks, kept like _xMarks so a manual Clean (RemoveDrawObjects) can
        // re-issue them. A fired signal is a historical fact and survives the Clean.
        // One touching bar awaiting its pivot verdict. Several can be live at once: a
        // retest is often two or three bars poking the same level, and only one of them
        // carries the pivot.
        private class RetestCand
        {
            public int      Bar;
            public DateTime Time;
            public double   Price;    // the wick's own extreme
            public string   Tag;
        }

        private class RetestMark
        {
            public string   Tag;
            public DateTime Time;
            public double   Price;
            public bool     IsLong;
            public bool     Confirmed;
        }
        private List<RetestMark> _rtMarks;

        // Hosted calc-only Nebula, read ONLY for its BrightState plot. Null unless the
        // retest signal is enabled AND it is configured to require Nebula agreement.
        private NebulaNT8NoCloud _neb;

        // ---- Pivot engine state (one instance per series) ------------------------------
        // The DAILY bias engine keeps its own _dbPivot* lists and its own methods, untouched.
        // This type carries the identical state for a SECOND, independent run of the same
        // rules on the chart series (BarsInProgress 0) for the retest confirmation.
        private class PivotEngine
        {
            public List<int>      Bars   = new List<int>(512);     // series bar index of each pivot
            public List<double>   Prices = new List<double>(512);  // pivot wick price (same-side comparisons)
            public List<double>   Guides = new List<double>(512);  // BODY extreme of the pivot bar
            public List<bool>     IsHigh = new List<bool>(512);
            public List<DateTime> Times  = new List<DateTime>(512);
            public int LastProcessedBar = -1;
        }
        private PivotEngine _cpEngine;   // chart-series (BIP 0) pivots

        private ZoneSet _zsPri, _zsIns;
        private ZoneSet[] _zoneSets = new ZoneSet[0];

        private const int MaxGroupDrawings = 300;

        private static readonly string[] _grpTfTokens = { "D", "240", "60", "30", "15", "10", "5" };

        #endregion

        private AlightenMirrorPtAV0011[] _srcA = new AlightenMirrorPtAV0011[NUM_TF];
        private AlightenMirrorPtBV0005[] _srcB = new AlightenMirrorPtBV0005[NUM_TF];
        private AlightenMirrorPtGV0003[] _srcG = new AlightenMirrorPtGV0003[NUM_TF];
        private AlightenMirrorPtHV0003[] _srcH = new AlightenMirrorPtHV0003[NUM_TF];
		private AlightenMirrorPtFV0004[] _srcF = new AlightenMirrorPtFV0004[NUM_TF];
		private AlightenMirrorPtJV0008[] _srcJ = new AlightenMirrorPtJV0008[NUM_TF];

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

        // ---- Pattern J plots (35..41) have no accessors (unchanged from V0045) ----

        // ---- V0046 retest signal plots (42..45), APPENDED LAST ----
        // Every index above is untouched; a consumer bound to PtA..PtJ keeps working.
        private const int PLOT_RETEST_SIGNAL = NUM_PAT * NUM_TF;       // 42
        private const int PLOT_RETEST_PRICE  = NUM_PAT * NUM_TF + 1;   // 43
        private const int PLOT_RETEST_EDGE   = NUM_PAT * NUM_TF + 2;   // 44
        private const int PLOT_GET_READY     = NUM_PAT * NUM_TF + 3;   // 45

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> RetestSignal => Values[PLOT_RETEST_SIGNAL];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> RetestPrice => Values[PLOT_RETEST_PRICE];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> RetestZoneEdge => Values[PLOT_RETEST_EDGE];

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> GetReadyState => Values[PLOT_GET_READY];

        [NinjaScriptProperty]
        [Display(Name="1.1 Enable Pattern A", Order=1, GroupName="01. Patterns")]
        public bool EnablePatternA { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1.2 Enable Pattern B", Order=2, GroupName="01. Patterns")]
        public bool EnablePatternB { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1.3 Enable Pattern G", Order=3, GroupName="01. Patterns")]
        public bool EnablePatternG { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1.4 Enable Pattern H", Order=4, GroupName="01. Patterns")]
        public bool EnablePatternH { get; set; }

		[NinjaScriptProperty]
		[Display(Name="1.5 Enable Pattern F", Order=5, GroupName="01. Patterns")]
		public bool EnablePatternF { get; set; }

		[NinjaScriptProperty]
		[Display(Name="1.6 Enable Pattern J", Description="Paired-pivot pattern (AlightenMirrorPtJV0008): levels are the nearest untested support/resistance — where the Pattern J test triangles fire.", Order=6, GroupName="01. Patterns")]
		public bool EnablePatternJ { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="2.1 Src Bars To Process", Order=1, GroupName="02. Engine & Data")]
        public int SrcBarsToProcess { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name="2.2 Mirror Lookback Bars", Order=2, GroupName="02. Engine & Data")]
		public int MirrorLookbackBars { get; set; }

		[NinjaScriptProperty]
        [Display(Name="2.3 Enable Invalidated Cleanup", Order=3, GroupName="02. Engine & Data")]
        public bool EnableInvalidatedCleanup { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3.1 Show Daily", Order=1, GroupName="03. Pattern A Timeframes")]
        public bool ShowPatternATF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3.2 Show 240m", Order=2, GroupName="03. Pattern A Timeframes")]
        public bool ShowPatternATF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3.3 Show 60m", Order=3, GroupName="03. Pattern A Timeframes")]
        public bool ShowPatternATF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3.4 Show 30m", Order=4, GroupName="03. Pattern A Timeframes")]
        public bool ShowPatternATF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3.5 Show 15m", Order=5, GroupName="03. Pattern A Timeframes")]
        public bool ShowPatternATF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3.6 Show 10m", Order=6, GroupName="03. Pattern A Timeframes")]
        public bool ShowPatternATF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3.7 Show 5m", Order=7, GroupName="03. Pattern A Timeframes")]
        public bool ShowPatternATF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4.1 Show Daily", Order=1, GroupName="04. Pattern B Timeframes")]
        public bool ShowPatternBTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4.2 Show 240m", Order=2, GroupName="04. Pattern B Timeframes")]
        public bool ShowPatternBTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4.3 Show 60m", Order=3, GroupName="04. Pattern B Timeframes")]
        public bool ShowPatternBTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4.4 Show 30m", Order=4, GroupName="04. Pattern B Timeframes")]
        public bool ShowPatternBTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4.5 Show 15m", Order=5, GroupName="04. Pattern B Timeframes")]
        public bool ShowPatternBTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4.6 Show 10m", Order=6, GroupName="04. Pattern B Timeframes")]
        public bool ShowPatternBTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4.7 Show 5m", Order=7, GroupName="04. Pattern B Timeframes")]
        public bool ShowPatternBTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5.1 Show Daily", Order=1, GroupName="05. Pattern G Timeframes")]
        public bool ShowPatternGTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5.2 Show 240m", Order=2, GroupName="05. Pattern G Timeframes")]
        public bool ShowPatternGTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5.3 Show 60m", Order=3, GroupName="05. Pattern G Timeframes")]
        public bool ShowPatternGTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5.4 Show 30m", Order=4, GroupName="05. Pattern G Timeframes")]
        public bool ShowPatternGTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5.5 Show 15m", Order=5, GroupName="05. Pattern G Timeframes")]
        public bool ShowPatternGTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5.6 Show 10m", Order=6, GroupName="05. Pattern G Timeframes")]
        public bool ShowPatternGTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5.7 Show 5m", Order=7, GroupName="05. Pattern G Timeframes")]
        public bool ShowPatternGTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6.1 Show Daily", Order=1, GroupName="06. Pattern H Timeframes")]
        public bool ShowPatternHTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6.2 Show 240m", Order=2, GroupName="06. Pattern H Timeframes")]
        public bool ShowPatternHTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6.3 Show 60m", Order=3, GroupName="06. Pattern H Timeframes")]
        public bool ShowPatternHTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6.4 Show 30m", Order=4, GroupName="06. Pattern H Timeframes")]
        public bool ShowPatternHTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6.5 Show 15m", Order=5, GroupName="06. Pattern H Timeframes")]
        public bool ShowPatternHTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6.6 Show 10m", Order=6, GroupName="06. Pattern H Timeframes")]
        public bool ShowPatternHTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6.7 Show 5m", Order=7, GroupName="06. Pattern H Timeframes")]
        public bool ShowPatternHTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7.1 Show Daily", Order=1, GroupName="07. Pattern F Timeframes")]
        public bool ShowPatternFTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7.2 Show 240m", Order=2, GroupName="07. Pattern F Timeframes")]
        public bool ShowPatternFTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7.3 Show 60m", Order=3, GroupName="07. Pattern F Timeframes")]
        public bool ShowPatternFTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7.4 Show 30m", Order=4, GroupName="07. Pattern F Timeframes")]
        public bool ShowPatternFTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7.5 Show 15m", Order=5, GroupName="07. Pattern F Timeframes")]
        public bool ShowPatternFTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7.6 Show 10m", Order=6, GroupName="07. Pattern F Timeframes")]
        public bool ShowPatternFTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7.7 Show 5m", Order=7, GroupName="07. Pattern F Timeframes")]
        public bool ShowPatternFTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="8.1 Show Daily", Order=1, GroupName="08. Pattern J Timeframes")]
        public bool ShowPatternJTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="8.2 Show 240m", Order=2, GroupName="08. Pattern J Timeframes")]
        public bool ShowPatternJTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="8.3 Show 60m", Order=3, GroupName="08. Pattern J Timeframes")]
        public bool ShowPatternJTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="8.4 Show 30m", Order=4, GroupName="08. Pattern J Timeframes")]
        public bool ShowPatternJTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="8.5 Show 15m", Order=5, GroupName="08. Pattern J Timeframes")]
        public bool ShowPatternJTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="8.6 Show 10m", Order=6, GroupName="08. Pattern J Timeframes")]
        public bool ShowPatternJTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="8.7 Show 5m", Order=7, GroupName="08. Pattern J Timeframes")]
        public bool ShowPatternJTF7_5m { get; set; }



        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="9.1 Daily Color", Order=1, GroupName="09. Level Colors")]
        public Brush ColorTF1 { get; set; }
        [Browsable(false)]
        public string ColorTF1Serialize { get { return Serialize.BrushToString(ColorTF1); } set { ColorTF1 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="9.2 240m Color", Order=2, GroupName="09. Level Colors")]
        public Brush ColorTF2 { get; set; }
        [Browsable(false)]
        public string ColorTF2Serialize { get { return Serialize.BrushToString(ColorTF2); } set { ColorTF2 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="9.3 60m Color", Order=3, GroupName="09. Level Colors")]
        public Brush ColorTF3 { get; set; }
        [Browsable(false)]
        public string ColorTF3Serialize { get { return Serialize.BrushToString(ColorTF3); } set { ColorTF3 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="9.4 30m Color", Order=4, GroupName="09. Level Colors")]
        public Brush ColorTF4 { get; set; }
        [Browsable(false)]
        public string ColorTF4Serialize { get { return Serialize.BrushToString(ColorTF4); } set { ColorTF4 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="9.5 15m Color", Order=5, GroupName="09. Level Colors")]
        public Brush ColorTF5 { get; set; }
        [Browsable(false)]
        public string ColorTF5Serialize { get { return Serialize.BrushToString(ColorTF5); } set { ColorTF5 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="9.6 10m Color", Order=6, GroupName="09. Level Colors")]
        public Brush ColorTF6 { get; set; }
        [Browsable(false)]
        public string ColorTF6Serialize { get { return Serialize.BrushToString(ColorTF6); } set { ColorTF6 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="9.7 5m Color", Order=7, GroupName="09. Level Colors")]
        public Brush ColorTF7 { get; set; }
        [Browsable(false)]
        public string ColorTF7Serialize { get { return Serialize.BrushToString(ColorTF7); } set { ColorTF7 = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="10.1 Level Width", Order=1, GroupName="10. Level Visuals")]
        public int LevelWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="10.2 Pattern A Line Style", Order=2, GroupName="10. Level Visuals")]
        public DashStyleHelper LevelDashStyleA { get; set; }

        [NinjaScriptProperty]
        [Display(Name="10.3 Pattern B Line Style", Order=3, GroupName="10. Level Visuals")]
        public DashStyleHelper LevelDashStyleB { get; set; }

        [NinjaScriptProperty]
        [Display(Name="10.4 Pattern G Line Style", Order=4, GroupName="10. Level Visuals")]
        public DashStyleHelper LevelDashStyleG { get; set; }

        [NinjaScriptProperty]
        [Display(Name="10.5 Pattern H Line Style", Order=5, GroupName="10. Level Visuals")]
        public DashStyleHelper LevelDashStyleH { get; set; }

        [NinjaScriptProperty]
        [Display(Name="10.6 Pattern F Line Style", Order=6, GroupName="10. Level Visuals")]
        public DashStyleHelper LevelDashStyleF { get; set; }

        [NinjaScriptProperty]
        [Display(Name="10.7 Pattern J Line Style", Order=7, GroupName="10. Level Visuals")]
        public DashStyleHelper LevelDashStyleJ { get; set; }

        [NinjaScriptProperty]
        [Display(Name="10.8 Show Level Labels", Order=8, GroupName="10. Level Visuals")]
        public bool ShowLevelLabels { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5000)]
        [Display(Name="2.4 Realtime Sync Throttle (ms)", Description="0 = sync levels on every tick. Higher values reduce CPU by syncing at most once per interval (signal plots still update every tick).", Order=4, GroupName="02. Engine & Data")]
        public int SyncThrottleMs { get; set; }

        [NinjaScriptProperty]
        [Display(Name="17.6 Enable Research Log", Description="Log every confirmed signal with confluence context and forward MFE/MAE outcomes. Written by the Export Levels button as MirrorResearch_*.csv.", Order=6, GroupName="17. Logging & Diagnostics")]
        public bool EnableResearchLog { get; set; }

        [NinjaScriptProperty]
        [Range(10, 2000)]
        [Display(Name="17.7 Research Target (ticks)", Description="Favorable move that counts as a win (time-to-target and MAE-before-target are recorded against this).", Order=7, GroupName="17. Logging & Diagnostics")]
        public int ResearchTargetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="17.8 Export Mode (no drawing)", Description="Suppress every chart drawing (levels, daily bias lines, zone rectangles) while keeping all level and signal computation. Turn on for a deep-history export run: the draw objects are what make a long load unusable, not the level math.", Order=8, GroupName="17. Logging & Diagnostics")]
        public bool ExportMode { get; set; }

        // ---- Daily bias levels (AlightenBiasV0003 pivot engine, display only) ----
        [Display(Name="11.1 Enable Daily Bias Levels", Description="Draw the last N Daily levels from the AlightenBiasV0003 pivot engine (level = pivot bar's body extreme). Plain levels — no pattern/touch requirement.", Order=1, GroupName="11. Daily Bias Levels")]
        public bool EnableDailyBiasLevels { get; set; }

        [Range(1, 20)]
        [Display(Name="11.2 Number Of Levels (N)", Description="How many of the most recent confirmed Daily levels to draw (matches AlightenBiasV0003 NumberOfLevels).", Order=2, GroupName="11. Daily Bias Levels")]
        public int DailyBiasLevelCount { get; set; }

        [Range(1, 10000)]
        [Display(Name="11.3 Relevance (daily bars)", Description="Ignore pivots older than this many Daily bars (matches AlightenBiasV0003 RelevanceFactor).", Order=3, GroupName="11. Daily Bias Levels")]
        public int DailyBiasRelevanceDays { get; set; }

        [XmlIgnore]
        [Display(Name="11.4 Above-Price Color", Order=4, GroupName="11. Daily Bias Levels")]
        public Brush DailyBiasAboveColor { get; set; }
        [Browsable(false)]
        public string DailyBiasAboveColorSerialize { get { return Serialize.BrushToString(DailyBiasAboveColor); } set { DailyBiasAboveColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="11.5 Below-Price Color", Order=5, GroupName="11. Daily Bias Levels")]
        public Brush DailyBiasBelowColor { get; set; }
        [Browsable(false)]
        public string DailyBiasBelowColorSerialize { get { return Serialize.BrushToString(DailyBiasBelowColor); } set { DailyBiasBelowColor = Serialize.StringToBrush(value); } }

        [Range(1, 10)]
        [Display(Name="11.6 Line Width", Order=6, GroupName="11. Daily Bias Levels")]
        public int DailyBiasLevelWidth { get; set; }

        [Display(Name="11.7 Line Dash", Order=7, GroupName="11. Daily Bias Levels")]
        public DashStyleHelper DailyBiasLevelDash { get; set; }

        [Display(Name="11.8 Show Labels", Description="Draw a 'DL <price>' label at the right end of each level line.", Order=8, GroupName="11. Daily Bias Levels")]
        public bool ShowDailyBiasLabels { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name="10.9 Label Font Size", Order=9, GroupName="10. Level Visuals")]
        public int LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name="10.10 Label Offset Ticks", Order=10, GroupName="10. Level Visuals")]
        public int LabelOffsetTicks { get; set; }

        // ---- Signal group zones (harvested from AlightenMirrorEntryV0006) ----
        // Not [NinjaScriptProperty] on purpose: these stay out of the generated
        // constructor signature, exactly like the Daily Bias Levels group above.
        [Display(Name="12.1 Enable Signal Groups", Description="Detect file-defined confluence groups of active levels and mark their zone from identification until the latest member window ends.", Order=1, GroupName="12. Signal Groups")]
        public bool EnableSignalGroups { get; set; }

        [Display(Name="12.2 Groups File", Description="Rules file (in Documents\\NinjaTrader 8 unless an absolute path). One rule per row: signals separated by commas, then '; <ticks>T' for the max zone spread. Signal = pattern letter + timeframe + direction, e.g. J15S, J30S, J60S; 100T. Optional extra ';ANCHORED' or ';ORDERED' flag. Created with examples if missing. Edit the file, then reload the indicator.", Order=2, GroupName="12. Signal Groups")]
        public string GroupsFileName { get; set; }

        [XmlIgnore]
        [Display(Name="12.3 Short Zone Color", Order=3, GroupName="12. Signal Groups")]
        public Brush GroupShortColor { get; set; }
        [Browsable(false)]
        public string GroupShortColorSerialize { get { return Serialize.BrushToString(GroupShortColor); } set { GroupShortColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="12.4 Long Zone Color", Order=4, GroupName="12. Signal Groups")]
        public Brush GroupLongColor { get; set; }
        [Browsable(false)]
        public string GroupLongColorSerialize { get { return Serialize.BrushToString(GroupLongColor); } set { GroupLongColor = Serialize.StringToBrush(value); } }

        [Range(0, 100)]
        [Display(Name="12.5 Zone Opacity (%)", Order=5, GroupName="12. Signal Groups")]
        public int GroupZoneOpacity { get; set; }

        [Range(1, 10)]
        [Display(Name="12.6 Zone Outline Width", Order=6, GroupName="12. Signal Groups")]
        public int GroupOutlineWidth { get; set; }

        [Range(0, 100)]
        [Display(Name="12.7 Zone Outline Opacity (%)", Order=7, GroupName="12. Signal Groups")]
        public int GroupOutlineOpacity { get; set; }

        [Display(Name="12.8 Merge Overlapping Zones", Description="Collapse overlapping same-direction zones into one box (per-rule detections still run and log individually).", Order=8, GroupName="12. Signal Groups")]
        public bool MergeGroupZones { get; set; }

        [Range(50, 20000)]
        [Display(Name="12.10 Max Zone Drawings", Description="How many zone rectangles this set keeps on the chart. Older drawings are deleted once the count is exceeded, oldest first, even though the zone itself is still live. Raise it if old zones vanish; lower it if the chart feels heavy.", Order=10, GroupName="12. Signal Groups")]
        public int MaxZoneDrawings { get; set; }

        [Display(Name="12.9 Show Group Labels", Order=9, GroupName="12. Signal Groups")]
        public bool ShowGroupLabels { get; set; }

        // ---- Inside zones: a second, independent rule set ------------------------------
        // Same machinery as the primary zones, separate file and styling. Intended for the
        // narrower pairs that sit BETWEEN a primary zone and price -- the primary marks the
        // area, the inside zone marks where you would act inside it.
        [Display(Name="13.1 Enable Inside Zones", Description="Run a second, independent set of group rules from their own file. Drawn separately from the primary zones and never merged with them.", Order=1, GroupName="13. Inside Zones")]
        public bool EnableInsideZones { get; set; }

        [Display(Name="13.2 Inside Zones File", Description="Second rules file, same format as the primary Groups File. In Documents\\NinjaTrader 8 unless an absolute path. Edit the file, then reload the indicator.", Order=2, GroupName="13. Inside Zones")]
        public string InsideZonesFileName { get; set; }

        [XmlIgnore]
        [Display(Name="13.3 Inside Short Zone Color", Order=3, GroupName="13. Inside Zones")]
        public System.Windows.Media.Brush InsideShortColor { get; set; }
        [Browsable(false)]
        public string InsideShortColorSerialize { get { return Serialize.BrushToString(InsideShortColor); } set { InsideShortColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="13.4 Inside Long Zone Color", Order=4, GroupName="13. Inside Zones")]
        public System.Windows.Media.Brush InsideLongColor { get; set; }
        [Browsable(false)]
        public string InsideLongColorSerialize { get { return Serialize.BrushToString(InsideLongColor); } set { InsideLongColor = Serialize.StringToBrush(value); } }

        [Range(0, 100)]
        [Display(Name="13.5 Inside Zone Opacity (%)", Order=5, GroupName="13. Inside Zones")]
        public int InsideZoneOpacity { get; set; }

        [Range(1, 10)]
        [Display(Name="13.6 Inside Zone Outline Width", Order=6, GroupName="13. Inside Zones")]
        public int InsideOutlineWidth { get; set; }

        [Range(0, 100)]
        [Display(Name="13.7 Inside Zone Outline Opacity (%)", Order=7, GroupName="13. Inside Zones")]
        public int InsideOutlineOpacity { get; set; }

        [Display(Name="13.8 Merge Overlapping Inside Zones", Description="Collapse overlapping same-direction inside zones into one box. Inside zones never merge with primary zones.", Order=8, GroupName="13. Inside Zones")]
        public bool MergeInsideZones { get; set; }

        [Range(50, 20000)]
        [Display(Name="13.10 Max Inside Zone Drawings", Description="Same FIFO cap as the primary set, applied independently. The inside set typically produces several times more zones, so it hits the cap sooner.", Order=10, GroupName="13. Inside Zones")]
        public int MaxInsideZoneDrawings { get; set; }

        [Display(Name="13.9 Show Inside Zone Labels", Order=9, GroupName="13. Inside Zones")]
        public bool ShowInsideLabels { get; set; }

        [Display(Name="14.1 Enable Zone Breaks (required for Retest)", Description="Detects a bar closing through a zone. This is also the master switch for the Retest Entry Signal: turn it off and no retest watch can ever arm. Whether the break shows a marker is the Get-Ready Symbol setting (15.2).", Order=1, GroupName="14. Zone Break Signal")]
        public bool ShowCrossArrows { get; set; }

        [Display(Name="14.2 Require Opposing Zone", Description="On = only fire when a zone of the opposite direction sits on the far side of the one being crossed (a long zone below the short zone crossed upward, or a short zone above the long zone crossed downward) - the sandwiched case. Off = fire on any fresh close through a zone, regardless of what is on the other side. Off by default: a break of support with nothing overhead is still a short, and requiring the opposing zone suppressed those.", Order=2, GroupName="14. Zone Break Signal")]
        public bool CrossRequireOpposingZone { get; set; }

        [Display(Name="14.3 Inside Zones Only", Description="Restrict the crossed zone (and the opposing zone, when required) to the INSIDE set. Off = primary zones count too.", Order=3, GroupName="14. Zone Break Signal")]
        public bool CrossInsideOnly { get; set; }

        [Display(Name="14.4 Both Directions", Description="Off = long setups only (cross up through a short zone).", Order=4, GroupName="14. Zone Break Signal")]
        public bool CrossBothDirections { get; set; }

        [Range(0, 1440)]
        [Display(Name="14.5 Zone Grace (mins)", Description="How long after a zone's window closes it still counts. Zones keep being respected after their level-overlap window ends.", Order=5, GroupName="14. Zone Break Signal")]
        public int CrossGraceMins { get; set; }

        [Range(0, 2000)]
        [Display(Name="14.6 Max Zone Separation (ticks)", Description="How far below the crossed zone the opposing zone may sit and still count. 0 = no limit. Ignored unless Require Opposing Zone is on.", Order=6, GroupName="14. Zone Break Signal")]
        public int CrossMaxSeparation { get; set; }

        [Range(10, 1000)]
        [Display(Name="14.7 Max Break Markers", Description="FIFO cap on arrow draw objects.", Order=7, GroupName="14. Zone Break Signal")]
        public int MaxCrossArrows { get; set; }

        [XmlIgnore]
        [Browsable(false)]   // unused since V0046 (break marker = get-ready symbol)
        [Display(Name="Long Arrow", Order=8, GroupName="14. Zone Cross Signal")]
        public Brush CrossLongColor { get; set; }
        [Browsable(false)]
        public string CrossLongColorSerialize { get { return Serialize.BrushToString(CrossLongColor); } set { CrossLongColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Browsable(false)]   // unused since V0046 (break marker = get-ready symbol)
        [Display(Name="Short Arrow", Order=9, GroupName="14. Zone Cross Signal")]
        public Brush CrossShortColor { get; set; }
        [Browsable(false)]
        public string CrossShortColorSerialize { get { return Serialize.BrushToString(CrossShortColor); } set { CrossShortColor = Serialize.StringToBrush(value); } }

        // ---- V0046: zone-break retest entry signal -------------------------------------
        // Not [NinjaScriptProperty] on purpose, exactly like groups 11-14 above: these stay
        // out of the generated constructor signature so the generated region is unchanged.
        [Display(Name="15.1 Enable Retest Signal", Description="Arm the three-stage retest entry on every zone break: get-ready symbol + watch, then a provisional mark when a wick reaches back into the projected zone band with Nebula bright agreeing, then a confirmed mark when the chart-series pivot engine registers the matching pivot. Off = V0045 behaviour (the break marker is still a symbol rather than an arrow).", Order=1, GroupName="15. Retest Entry Signal")]
        public bool EnableRetestSignal { get; set; }

        [Display(Name="15.2 Show Get-Ready Symbol", Description="Draw the break marker. Off = the zone break still arms its watch and the retest still fires; only the symbol is hidden.", Order=2, GroupName="15. Retest Entry Signal")]
        public bool ShowGetReadySymbol { get; set; }

        [Display(Name="15.3 Get-Ready Symbol", Description="Drawn on the zone-break bar, replacing V0045's arrow. Any text works.", Order=3, GroupName="15. Retest Entry Signal")]
        public string GetReadySymbol { get; set; }

        [Display(Name="15.7 Long Entry Symbol", Description="Drawn below the bar for a LONG provisional/confirmed entry.", Order=7, GroupName="15. Retest Entry Signal")]
        public string RetestLongSymbol { get; set; }

        [Display(Name="15.8 Short Entry Symbol", Description="Drawn above the bar for a SHORT provisional/confirmed entry.", Order=8, GroupName="15. Retest Entry Signal")]
        public string RetestShortSymbol { get; set; }

        [Range(4, 72)]
        [Display(Name="15.10 Symbol Font Size", Order=10, GroupName="15. Retest Entry Signal")]
        public int RetestSymbolFontSize { get; set; }

        [XmlIgnore]
        [Display(Name="15.4 Get-Ready Color", Order=4, GroupName="15. Retest Entry Signal")]
        public Brush GetReadyColor { get; set; }
        [Browsable(false)]
        public string GetReadyColorSerialize { get { return Serialize.BrushToString(GetReadyColor); } set { GetReadyColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="15.6 Provisional Color", Description="Colour of the entry symbol while the pivot has not confirmed yet.", Order=6, GroupName="15. Retest Entry Signal")]
        public Brush RetestProvisionalColor { get; set; }
        [Browsable(false)]
        public string RetestProvisionalColorSerialize { get { return Serialize.BrushToString(RetestProvisionalColor); } set { RetestProvisionalColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="15.9 Confirmed Color", Description="The same symbol is redrawn in this colour, on the same tag, once the pivot confirms.", Order=9, GroupName="15. Retest Entry Signal")]
        public Brush RetestConfirmedColor { get; set; }
        [Browsable(false)]
        public string RetestConfirmedColorSerialize { get { return Serialize.BrushToString(RetestConfirmedColor); } set { RetestConfirmedColor = Serialize.StringToBrush(value); } }

        [Range(0, 200)]
        [Display(Name="15.11 Wick Tolerance (ticks)", Description="How far OUTSIDE the projected zone band the wick may stop and still count as a touch.", Order=11, GroupName="15. Retest Entry Signal")]
        public int RetestWickToleranceTicks { get; set; }

        [Display(Name="15.5 Show Provisional Marks", Description="Draw the faint entry symbol while a candidate waits for its pivot. Off = only CONFIRMED triangles are drawn; the candidate machinery is unchanged.", Order=5, GroupName="15. Retest Entry Signal")]
        public bool ShowProvisionalMarks { get; set; }

        [Display(Name="17.4 Write Retest Debug Log", Description="One line per closed bar per retest watch (zone edges, band, arm state, H/L/C, touch, candidates) into MirrorRetestV0047.log in the Log Folder. Large - leave off unless diagnosing.", Order=4, GroupName="17. Logging & Diagnostics")]
        public bool DebugRetestLog { get; set; }

        [Display(Name="17.5 Show Chart Pivot Dots", Description="Draws a dot on every CONFIRMED chart-series pivot the retest engine can see: magenta = pivot high, lime = pivot low. Use it to tell 'the pivot was never registered' apart from 'the signal rules rejected it'.", Order=5, GroupName="17. Logging & Diagnostics")]
        public bool ShowChartPivotDots { get; set; }

        [Display(Name="15.12 Require Nebula Bright", Description="Stage 1 additionally requires NebulaNT8NoCloud's BrightState to be +1 (long) or -1 (short) on the wick bar. Nebula is hosted calc-only; nothing of it is drawn.", Order=12, GroupName="15. Retest Entry Signal")]
        public bool RetestRequireNebulaBright { get; set; }

        // Display-only (not NinjaScriptProperty) so the generated factory signature is unchanged.
        [Display(Name="16.1 Sound Alert on Retest", Description="Play a sound and post to the Alerts window when a CONFIRMED retest triangle appears. Realtime only - never fires while history loads. The message names the instrument and bar period, so alerts from several charts can be told apart.", Order=1, GroupName="16. Retest Alerts")]
        public bool RetestSoundAlert { get; set; }

        [Display(Name="16.2 Long Sound File", Description="A file name from the NinjaTrader 8\\sounds folder (e.g. Alert2.wav), or a full path to any .wav.", Order=2, GroupName="16. Retest Alerts")]
        public string RetestLongSound { get; set; }

        [Display(Name="16.3 Short Sound File", Description="A file name from the NinjaTrader 8\\sounds folder (e.g. Alert4.wav), or a full path to any .wav.", Order=3, GroupName="16. Retest Alerts")]
        public string RetestShortSound { get; set; }

        [Display(Name="16.4 Also Alert on Provisional", Description="Also alert at stage 1, when a wick first touches the level - earlier, but many of these never confirm. Fires whether or not provisional marks are shown.", Order=4, GroupName="16. Retest Alerts")]
        public bool RetestAlertProvisional { get; set; }

        [Display(Name="17.3 Write Level Log", Description="Every Mirror level created and removed, into MirrorV0046SignalLevels.log in the Log Folder. Diagnostic. Capped at 60,000 lines.", Order=3, GroupName="17. Logging & Diagnostics")]
        public bool DebugLevelLog { get; set; }

        [Display(Name="17.1 Log Folder", Description = "Where the zone, level and retest logs are written. A plain name is a folder inside Documents\\NinjaTrader 8 (default: Mirror Logs); a full path is used as-is. Created if missing. Logs are appended, never overwritten; each load is marked with its load id.", Order=1, GroupName="17. Logging & Diagnostics")]
        public string LogFolder { get; set; }

        [Display(Name="17.2 Write Zone Log", Description = "The zone logs (MirrorZonesV0046Signal_INS.log / _PRI.log): every zone created, updated and cleared, with members, price range and time window, stamped with load id and market time. The one to keep on for trade reviews.", Order=2, GroupName="17. Logging & Diagnostics")]
        public bool WriteZoneLog { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AlightenMirrorV0047Signal";
                Description = "SIGNAL branch of the Multi-Timeframe Mirror (Patterns A, B, G, H, F, J) - clean copy of V0041, no signal work. v32: draws the last N Daily levels from the AlightenBiasV0003 pivot engine (ported inline, group 11 settings) — plain levels, no pattern requirement. Includes v31 research logging and the v30 perf work.";
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

                // Visibility defaults per the user's saved production template (2026-09-15,
                // AlightenMirrorV0047Signal_Default.xml): A/G/H/F show Daily/240m only;
                // B and J draw no levels at all (their zones still feed the group engine).
                ShowPatternATF1_Daily = true;
                ShowPatternATF2_240m = true;
                ShowPatternATF3_60m = false;
                ShowPatternATF4_30m = false;
                ShowPatternATF5_15m = false;
                ShowPatternATF6_10m = false;
                ShowPatternATF7_5m = false;
                ShowPatternBTF1_Daily = false;
                ShowPatternBTF2_240m = false;
                ShowPatternBTF3_60m = false;
                ShowPatternBTF4_30m = false;
                ShowPatternBTF5_15m = false;
                ShowPatternBTF6_10m = false;
                ShowPatternBTF7_5m = false;
                ShowPatternGTF1_Daily = true;
                ShowPatternGTF2_240m = true;
                ShowPatternGTF3_60m = false;
                ShowPatternGTF4_30m = false;
                ShowPatternGTF5_15m = false;
                ShowPatternGTF6_10m = false;
                ShowPatternGTF7_5m = false;
                ShowPatternHTF1_Daily = true;
                ShowPatternHTF2_240m = true;
                ShowPatternHTF3_60m = false;
                ShowPatternHTF4_30m = false;
                ShowPatternHTF5_15m = false;
                ShowPatternHTF6_10m = false;
                ShowPatternHTF7_5m = false;
                ShowPatternFTF1_Daily = true;
                ShowPatternFTF2_240m = true;
                ShowPatternFTF3_60m = false;
                ShowPatternFTF4_30m = false;
                ShowPatternFTF5_15m = false;
                ShowPatternFTF6_10m = false;
                ShowPatternFTF7_5m = false;
                ShowPatternJTF1_Daily = false;
                ShowPatternJTF2_240m = false;
                ShowPatternJTF3_60m = false;
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
                EnableResearchLog = false;   // per saved template; enable per-instance for MFE/MAE research capture
                ResearchTargetTicks = 100;
                ExportMode = false;

                EnableDailyBiasLevels = true;
                DailyBiasLevelCount = 7;
                DailyBiasRelevanceDays = 300;
                DailyBiasAboveColor = Brushes.Goldenrod;
                DailyBiasBelowColor = Brushes.DeepSkyBlue;
                DailyBiasLevelWidth = 3;
                DailyBiasLevelDash = DashStyleHelper.Solid;
                ShowDailyBiasLabels = true;

                EnableSignalGroups  = false;  // per saved template; inside zones only by default
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

                ShowCrossArrows     = true;
                CrossRequireOpposingZone = false;  // a break with nothing on the far side still counts
                CrossInsideOnly     = true;
                CrossBothDirections = true;
                CrossGraceMins      = 40;   // tuned on the chart 2026-09-23
                CrossMaxSeparation  = 0;      // no limit
                MaxCrossArrows      = 200;
                CrossLongColor      = Brushes.Lime;
                CrossShortColor     = Brushes.Red;

                // V0046 retest entry signal - OFF by default. With this off the only
                // difference from V0045 is the break marker's glyph.
                EnableRetestSignal       = false;
                GetReadySymbol           = "★";   // ★
                RetestLongSymbol         = "▲";   // ▲
                RetestShortSymbol        = "▼";   // ▼
                RetestSymbolFontSize     = 14;
                GetReadyColor            = Brushes.Gold;
                RetestProvisionalColor   = Brushes.Khaki;
                RetestConfirmedColor     = Brushes.Aqua;
                RetestWickToleranceTicks = 5;
                RetestRequireNebulaBright = false;
                LogFolder                = "Mirror Logs";
                WriteZoneLog             = true;
                RetestSoundAlert         = false;
                RetestLongSound          = "Alert2.wav";
                RetestShortSound         = "Alert4.wav";
                RetestAlertProvisional   = false;
                ShowChartPivotDots       = false;
                ShowProvisionalMarks     = false;
                ShowGetReadySymbol       = true;
                DebugRetestLog           = false;

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

                // V0046 - APPENDED LAST so plot indexes 0..41 are exactly V0045's.
                AddPlot(Brushes.Transparent, "RetestSignal");     // 42
                AddPlot(Brushes.Transparent, "RetestPrice");      // 43
                AddPlot(Brushes.Transparent, "RetestZoneEdge");   // 44
                AddPlot(Brushes.Transparent, "GetReadyState");    // 45
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

                _logLoadId = "L" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                if (DebugLevelLog)
                {
                    try
                    {
                        _lvlLogPath = LogFile("MirrorV0046SignalLevels.log");
                        _lvlLogLines = 0;
                        System.IO.File.AppendAllText(_lvlLogPath,
                            "\r\n======== LOAD " + _logLoadId + "  "
                            + (Instrument != null ? Instrument.FullName : "?") + " ========\r\n");
                    }
                    catch { _lvlLogPath = null; }
                }

                _xLastBar = -1;
                _xMarks = new Queue<CrossMark>();

                _rtLastBar = -1;
                _rtAlerted.Clear();
                _rtWatches = new List<RetestWatch>(MAX_RETEST_WATCHES);
                _rtMarks   = new List<RetestMark>(64);
                _cpEngine  = new PivotEngine();

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
                        _srcA[t] = AlightenMirrorPtAV0011(
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
                        _srcG[t] = AlightenMirrorPtGV0003(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternH)
                    {
                        _srcH[t] = AlightenMirrorPtHV0003(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternF)
                    {
                        _srcF[t] = AlightenMirrorPtFV0004(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternJ)
                    {
                        // Pattern J: paired-pivot engine (PtJV0005). Calc-only hosted —
                        // all its own drawing/labels/legacy display off; the Mirror draws
                        // the levels it reports (nearest untested support/resistance).
                        _srcJ[t] = AlightenMirrorPtJV0008(
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

                // V0046: host Nebula CALC-ONLY, on the chart series, solely to read its
                // BrightState plot. Every switch that would draw, colour a candle or fire an
                // alert is passed off - a hosted child has no ChartControl and must not try.
                // Only instantiated when the feature that reads it is actually enabled, so
                // with the retest signal off this file costs exactly what V0045 costs.
                if (EnableRetestSignal && RetestRequireNebulaBright)
                {
                    try
                    {
                        _neb = NebulaNT8NoCloud(
                            Closes[0],
                            "Simple", "None", "Standard",          // cloudType, candleColoring (NONE = no BarBrushes writes), theme
                            false, false, false, false,            // strong/basic buy/sell glyphs off
                            false,                                 // showHEMA  (would Draw.Line)
                            false, false,                          // showPlus, showBigPlus
                            false,                                 // enhanceStrongSignals
                            false, false,                          // showFullProfit, showPartialProfit
                            false,                                 // show921
                            false,                                 // ignoreDoji - MUST match the bright-wave definition the user sees
                            5, 7, 1,                               // profit thresholds, doji ticks (unused with the above off)
                            false, 50, 3,                          // showVolumeImbalanceLines (would Draw.Line), line bars/width
                            false, false,                          // showReversalPattern (writes CandleOutlineBrushes), showRetests
                            false, "Top Right", 13,                // showDashboard (OnRender only), position, icon size
                            false, 80, 80, 50,                     // useQuadratic921, cloud opacities
                            14, 14,                                // adxLength, diLength
                            2, 10.0, 6,                            // fantail adx/weighting/ma
                            150, 20, 40, 20, 2.0,                  // WAE
                            0.0015, 25, 72,                        // trampoline
                            2, 21,                                 // squeeze
                            35,                                    // watch lookback
                            20, 20,                                // HEMA alpha/gamma
                            21, 8, 15,                             // kernel
                            false                                  // enableAlerts - never alert from a hosted child
                        );
                    }
                    catch (Exception ex)
                    {
                        _neb = null;
                        Print("[AlightenMirrorV0047Signal] Nebula host failed: " + ex.Message
                            + " - the retest signal will find no bright bars while 'Require Nebula Bright' is on.");
                    }
                }

				if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => { CreateToolbarButton(); });
                }
            }
			else if (State == State.Realtime)
            {
                // Everything above this line in a log was rebuilt from history at load; everything below
                // happened live (or in replay) and is what was actually on screen at that moment.
                foreach (ZoneSet zs in _zoneSets)
                    if (!string.IsNullOrEmpty(zs.LogPath))
                        GrpLog(zs, "======== REALTIME " + _logLoadId + " - lines below were seen live ========");
                if (DebugLevelLog) LvlLog("======== REALTIME - lines below were seen live ========");
            }
			else if (State == State.Terminated)
            {
                if (ChartControl != null)
                {
                    // InvokeAsync only QUEUES the teardown, and a recompile discards this
                    // instance before the queue drains - which is how buttons end up stranded
                    // on the chart window still wired to a dead instance. When we are already
                    // on the UI thread, run it synchronously so it cannot be skipped. Off the
                    // UI thread we still queue it: a blocking Invoke there can deadlock against
                    // a busy UI thread, which is a worse failure than a stale button.
                    try
                    {
                        if (ChartControl.Dispatcher.CheckAccess()) TryRemoveToolbarButton();
                        else ChartControl.Dispatcher.InvokeAsync(() => { TryRemoveToolbarButton(); });
                    }
                    catch { }
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

                    // V0046 retest plots are transient per bar, like every other Mirror plot:
                    // 0 unless something applies on THIS bar. Written unconditionally so they
                    // are never NaN for a consumer.
                    Values[PLOT_RETEST_SIGNAL][0] = 0;
                    Values[PLOT_RETEST_PRICE][0]  = 0;
                    Values[PLOT_RETEST_EDGE][0]   = 0;
                    Values[PLOT_GET_READY][0]     = 0;

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

                    if (IsFirstTickOfBar)
                    {
                        CheckZoneCross();   // after UpdateSignalGroups: reads the live zone map
                        if (EnableRetestSignal)
                            UpdateRetestWatches();   // after CheckZoneCross: a break registers its watch there
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
                    AlightenMirrorPtJV0008 srcJ = (p == PAT_J) ? _srcJ[t] : null;

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

            // AlightenBiasV0003 fires SIX conditions, not four. These two register the OPPOSITE side
            // of an OUTSIDE day (higher high AND lower low), where the Bias records two pivots and
            // this port recorded one - which dropped pivots, shifted the alternating chain, and made
            // the drawn set diverge from the Bias on ~74% of days (284 vs 220 pivots over 775 daily
            // bars). Order matches the Bias source so the same-side replacement resolves identically.
            if (!prevGreen && currGreen && isHigh)  DbProcessPivot(barIdx, h0, true,  Math.Max(o0, c0), t0);
            if (prevGreen && !currGreen && isLow)   DbProcessPivot(barIdx, l0, false, Math.Min(o0, c0), t0);
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
            if (!WriteZoneLog || string.IsNullOrEmpty(zs.LogPath)) return;
            try
            {
                System.IO.File.AppendAllText(zs.LogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + LogStamp() + " " + msg + "\r\n");
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
                "[AlightenMirrorV0047Signal] zone sets: PRIMARY {0} ({1})   INSIDE {2} ({3})",
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
                        "# AlightenMirrorV0047Signal signal groups\r\n" +
                        "# One rule per row:  <signal>, <signal>, ... ; <max zone spread in ticks>T\r\n" +
                        "# Signal = Pattern letter (A B G H F J) + timeframe (D 240 60 30 15 10 5) + direction (S or L)\r\n" +
                        "# Optional extra flag: ; ANCHORED   (others on the highest-TF member's protected side)\r\n" +
                        "#                      ; ORDERED    (listed order is the required price order, lowest first)\r\n" +
                        "# Example: the triple J short stack within 100 ticks:\r\n" +
                        "J15S, J30S, J60S; 100T\r\n" +
                        "J15L, J30L, J60L; 100T\r\n");
                    Print("[AlightenMirrorV0047Signal] created groups file with examples: " + path);
                }

                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;

                    string[] segs = line.Split(';');
                    if (segs.Length < 2)
                    {
                        Print("[AlightenMirrorV0047Signal] groups file: missing '; <ticks>T' in row: " + line);
                        continue;
                    }

                    string tickPart = segs[1].Trim().TrimEnd('T', 't', ' ');
                    int maxTicks;
                    if (!int.TryParse(tickPart, out maxTicks) || maxTicks <= 0)
                    {
                        Print("[AlightenMirrorV0047Signal] groups file: bad tick spread in row: " + line);
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
                            Print("[AlightenMirrorV0047Signal] groups file: unknown flag '" + flag + "' in row: " + line);
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
                            Print("[AlightenMirrorV0047Signal] groups file: bad signal token '" + tok + "' in row: " + line);
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

                Print("[AlightenMirrorV0047Signal] loaded " + zs.Rules.Count + " signal-group rule(s) from " + path);

                zs.LogPath = LogFile("MirrorZonesV0046Signal_" + zs.Name + ".log");
                GrpLog(zs, "======== LOAD " + _logLoadId + " " + (Instrument != null ? Instrument.FullName : "?")
                    + " chart=" + (BarsPeriod != null ? BarsPeriod.ToString() : "?")
                    + " rules=" + zs.Rules.Count + " ========");
            }
            catch (Exception ex)
            {
                Print("[AlightenMirrorV0047Signal] groups file error: " + ex.Message);
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

		#region Zone Cross Signal

        // One closed primary bar. Fires when the bar closes THROUGH one zone while an
        // opposing zone sits on the far side of it:
        //   LONG  - closes above a SHORT zone that has a LONG zone below it
        //   SHORT - closes below a LONG zone that has a SHORT zone above it
        // "Fresh" means the previous close was still on the near side, so a bar that was
        // already beyond the zone does not re-fire on every bar it stays there.
        private void CheckZoneCross()
        {
            if (!ShowCrossArrows || ExportMode || _zoneSets.Length == 0) return;

            int b = State == State.Historical ? CurrentBars[0] : CurrentBars[0] - 1;
            if (b <= _xLastBar || b < 2) return;
            _xLastBar = b;

            int ago = CurrentBars[0] - b;
            if (ago < 0 || ago + 1 > CurrentBars[0]) return;

            double h = Highs[0][ago], l = Lows[0][ago];
            double c = Closes[0][ago], pc = Closes[0][ago + 1];
            DateTime t = Times[0][ago];
            TimeSpan grace = TimeSpan.FromMinutes(CrossGraceMins);
            double sep = CrossMaxSeparation > 0 && TickSize > 0 ? CrossMaxSeparation * TickSize : double.MaxValue;

            foreach (ZoneSet zs in _zoneSets)
            {
                if (CrossInsideOnly && zs.Name != "INS") continue;
                foreach (var kv in zs.Active)
                {
                    ActiveGroup g = kv.Value;
                    if (g.IdentifiedTime > t || t > g.EndTime + grace) continue;

                    // EVERY zone broken on this bar gets its own watch. V0045 returned after
                    // the first match, which was fine when this only drew one arrow per bar -
                    // but zones stack, and with a single watch the lower of two zones at the
                    // same price could never produce a retest. Measured 2026-09-23: only the
                    // 30958.50 zone had a watch, so the wicks at 09:35/09:36, which were
                    // testing the zone beneath it, had nothing to fire against.
                    //
                    // The get-ready symbol still shares one tag per bar and direction, so the
                    // chart gains no extra clutter - only the watch list does.

                    // LONG: closed up through this SHORT zone, with a LONG zone below it
                    if (!g.IsLong && c > g.ZoneMax && pc <= g.ZoneMax
                        && (!CrossRequireOpposingZone || HasOpposingZone(false, g.ZoneMin, sep, t, grace)))
                    {
                        OnZoneBreak(b, ago, t, true, l, g, kv.Key);
                        continue;
                    }

                    // SHORT: closed down through this LONG zone, with a SHORT zone above it
                    if (g.IsLong && CrossBothDirections && c < g.ZoneMin && pc >= g.ZoneMin
                        && (!CrossRequireOpposingZone || HasOpposingZone(true, g.ZoneMax, sep, t, grace)))
                    {
                        OnZoneBreak(b, ago, t, false, h, g, kv.Key);
                        continue;
                    }
                }
            }
        }

        // Is there a zone on the far side of the one just crossed?
        //   wantAbove=false -> a LONG zone BELOW `edge`  (the long setup)
        //   wantAbove=true  -> a SHORT zone ABOVE `edge` (the short setup)
        private bool HasOpposingZone(bool wantAbove, double edge, double sep, DateTime t, TimeSpan grace)
        {
            foreach (ZoneSet zs in _zoneSets)
            {
                if (CrossInsideOnly && zs.Name != "INS") continue;
                foreach (var kv in zs.Active)
                {
                    ActiveGroup g = kv.Value;
                    if (g.IdentifiedTime > t || t > g.EndTime + grace) continue;

                    if (!wantAbove)
                    {
                        if (!g.IsLong) continue;
                        if (g.ZoneMax < edge && edge - g.ZoneMax <= sep) return true;
                    }
                    else
                    {
                        if (g.IsLong) continue;
                        if (g.ZoneMin > edge && g.ZoneMin - edge <= sep) return true;
                    }
                }
            }
            return false;
        }

        private void DrawCrossArrow(int barIdx, DateTime t, bool isLong, double px)
        {
            CrossMark m = new CrossMark
            {
                Tag = "MirXC_" + barIdx + (isLong ? "L" : "S"),
                Time = t, Price = px, IsLong = isLong
            };
            DrawOneCross(m);

            _xMarks.Enqueue(m);
            while (_xMarks.Count > MaxCrossArrows)
                RemoveDrawObject(_xMarks.Dequeue().Tag);
        }

        // V0046: the break marker is the configurable GET-READY SYMBOL, not V0045's arrow.
        // Same tag, same FIFO queue, same Clean/redraw path - only the glyph and colour changed.
        // This is the ONE behaviour difference from V0045 when the retest feature is off.
        private void DrawOneCross(CrossMark m)
        {
            if (ExportMode) return;
            // Drawing only. The break still registers its watch, so turning the symbol off
            // hides the "get ready" marker without disabling the retest signal itself.
            if (!ShowGetReadySymbol)
            {
                try { RemoveDrawObject(m.Tag); } catch { }
                return;
            }
            double off = 6 * TickSize;
            Draw.Text(this, m.Tag, false, SafeSymbol(GetReadySymbol, "★"),
                m.Time, m.IsLong ? m.Price - off : m.Price + off, 0,
                GetReadyColor ?? Brushes.Gold,
                new SimpleFont("Arial", Math.Max(4, RetestSymbolFontSize)),
                TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
        }

        private static string SafeSymbol(string s, string fallback)
        {
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        // Re-issue every signal still held. Called from ForceUISync after RemoveDrawObjects(),
        // alongside the level and zone redraws.
        private void RedrawCrossArrows()
        {
            if (!ShowCrossArrows || ExportMode || _xMarks == null) return;
            foreach (CrossMark m in _xMarks) DrawOneCross(m);
        }

        #endregion

        #region V0046 Retest Entry Signal

        // ---- STAGE 0: GET READY -------------------------------------------------------
        // Called by CheckZoneCross in place of V0045's DrawCrossArrow. The glyph is drawn
        // unconditionally (that is the one visual difference from V0045); the WATCH is only
        // registered when the retest feature is enabled.
        private void OnZoneBreak(int barIdx, int ago, DateTime t, bool isLong, double px, ActiveGroup g, string zoneKey)
        {
            DrawCrossArrow(barIdx, t, isLong, px);   // unchanged plumbing: tag, FIFO cap, Clean redraw

            if (!EnableRetestSignal || _rtWatches == null || g == null) return;

            if (ago >= 0 && ago <= CurrentBars[0] && ago < 250)
                Values[PLOT_GET_READY][ago] = isLong ? 1 : -1;

            // A re-break of the same zone in the same direction RE-ARMS the existing watch
            // rather than replacing it: the zone is one level with one history, and a
            // replacement would throw away the marks already earned against it.
            for (int i = _rtWatches.Count - 1; i >= 0; i--)
            {
                RetestWatch ex = _rtWatches[i];
                if (ex.ZoneTag != zoneKey || ex.IsLong != isLong) continue;
                ex.Armed       = true;
                ex.ZoneMin     = g.ZoneMin;      // the zone can tighten while it lives
                ex.ZoneMax     = g.ZoneMax;
                ex.Edge        = isLong ? g.ZoneMax : g.ZoneMin;
                ex.ZoneEndTime = g.EndTime;
                return;
            }

            RetestWatch w = new RetestWatch
            {
                ZoneTag     = zoneKey,
                IsLong      = isLong,
                ZoneMin     = g.ZoneMin,
                ZoneMax     = g.ZoneMax,
                Edge        = isLong ? g.ZoneMax : g.ZoneMin,
                ZoneEndTime = g.EndTime,
                BreakBar    = barIdx,
                BreakTime   = t,
                Armed       = true
            };
            _rtWatches.Add(w);

            while (_rtWatches.Count > MAX_RETEST_WATCHES)
                KillWatch(_rtWatches[0], 0);
        }

        // ---- STAGES 1 and 2, plus cancellation ----------------------------------------
        // One CLOSED primary bar, same bar selection as CheckZoneCross.
        private void UpdateRetestWatches()
        {
            if (_rtWatches == null || _cpEngine == null) return;

            int b = State == State.Historical ? CurrentBars[0] : CurrentBars[0] - 1;
            if (b <= _rtLastBar || b < 2) return;
            _rtLastBar = b;

            int ago = CurrentBars[0] - b;
            if (ago < 0 || ago + 1 > CurrentBars[0]) return;

            // The chart-series pivot engine advances exactly one bar per closed bar, so its
            // newest CONFIRMED pivot is always at least one bar old - which is the lag
            // stage 2 exists to wait for.
            PivotCatchUp(_cpEngine, 0, b);

            // Diagnostic first: it must not depend on a watch being alive, or it would go
            // blank exactly when you are trying to find out why nothing fired.
            if (ShowChartPivotDots) DrawChartPivotDots();

            if (_rtWatches.Count == 0) return;

            double hi = Highs[0][ago], lo = Lows[0][ago], cl = Closes[0][ago];
            DateTime t = Times[0][ago];
            double tol   = TickSize > 0 ? RetestWickToleranceTicks * TickSize : 0;
            TimeSpan grace = TimeSpan.FromMinutes(CrossGraceMins);

            double activeEdge = 0;

            for (int i = _rtWatches.Count - 1; i >= 0; i--)
            {
                RetestWatch w = _rtWatches[i];

                // EXPIRE: the zone's own lifetime is the only thing that ends a watch.
                if (t > w.ZoneEndTime + grace) { KillWatch(w, i); continue; }

                activeEdge = w.Edge;

                // ---- ARM STATE, evaluated for THIS bar ---------------------------------
                // Order matters, and getting it wrong cost two clear signals on 2026-09-23
                // at 13:37 and 13:41. The retest-and-reject bar - a wick through the level
                // that closes back on the break side - both RE-ARMS the watch and IS the
                // signal. Disarming first, then `continue`, meant that bar could never fire.
                //
                // So: a bar counts as armed if the watch was already armed OR this bar's
                // close re-crosses in the break direction. The signal is evaluated against
                // the bar's WICK, and the armed state is updated from the close AFTERWARDS.
                // Quality is guarded by the pivot requirement, not by the close: a confirmed
                // pivot low means price turned up there whatever that one bar did.
                bool reCross   = w.IsLong ? cl > w.ZoneMax : cl < w.ZoneMin;
                bool armedNow  = w.Armed || reCross;
                bool closedOut = w.IsLong ? cl < w.ZoneMin : cl > w.ZoneMax;

                if (DebugRetestLog)
                {
                    double dbgLo = Math.Min(w.ZoneMin, w.Edge) - tol;
                    double dbgHi = Math.Max(w.ZoneMax, w.Edge) + tol;
                    RetestLog(string.Format(
                        "{0:yyyy-MM-dd HH:mm} {1} zone={2} [{3:F2}..{4:F2}] edge={5:F2} band=[{6:F2}..{7:F2}] "
                        + "H={8:F2} L={9:F2} C={10:F2} armed={11} reCross={12} closedOut={13} "
                        + "touch={14} neb={15} cands={16} breakBar={17} bar={18}",
                        t, w.IsLong ? "LONG " : "SHORT", w.ZoneTag, w.ZoneMin, w.ZoneMax, w.Edge,
                        dbgLo, dbgHi, hi, lo, cl, armedNow, reCross, closedOut,
                        (hi >= dbgLo && lo <= dbgHi), NebulaBright(ago),
                        w.Cands.Count, w.BreakBar, b));
                }

                if (!armedNow || b <= w.BreakBar)
                {
                    // Still update the state even when this bar cannot signal.
                    if (closedOut && w.Armed) { w.Armed = false; DropProvisional(w); }
                    else if (reCross) w.Armed = true;
                    continue;
                }

                // The zone band projected forward, widened by the wick tolerance.
                double bandLo = Math.Min(w.ZoneMin, w.Edge) - tol;
                double bandHi = Math.Max(w.ZoneMax, w.Edge) + tol;

                // ---- STAGE 1: a CANDIDATE per touching bar -----------------------------
                // Every bar whose wick reaches the projected band becomes its own candidate.
                // One slot was the bug: the first bar to poke the level claimed it and the
                // pivot, which landed a bar or two later, could never match.
                // A TOUCH is the bar's range intersecting the band - both sides. The old
                // one-sided test (long: lo <= bandHi) was true for ANY bar sitting below the
                // band, so a collapsed market kept "touching" a level it had left far behind.
                // The disarm rule masked most of it; it was still wrong.
                bool touched = hi >= bandLo && lo <= bandHi;
                if (touched && (!RetestRequireNebulaBright || NebulaBright(ago) == (w.IsLong ? 1 : -1)))
                {
                    bool already = false;
                    for (int c = 0; c < w.Cands.Count; c++)
                        if (w.Cands[c].Bar == b) { already = true; break; }

                    if (!already)
                    {
                        RetestCand cd = new RetestCand
                        {
                            Bar   = b,
                            Time  = t,
                            Price = w.IsLong ? lo : hi,      // the wick's own extreme
                            Tag   = "MirRT_" + b + (w.IsLong ? "L" : "S")
                        };
                        w.Cands.Add(cd);
                        w.Provisional = true;
                        w.ProvBar = b; w.ProvTime = t; w.ProvPrice = cd.Price; w.MarkTag = cd.Tag;
                        DrawRetestCand(w, cd, false);
                        PublishRetest(ago, w.IsLong ? 1 : -1, cd.Price, w.Edge);
                        if (RetestAlertProvisional) RetestAlert(cd, w.IsLong, false);

                        while (w.Cands.Count > MAX_RETEST_CANDS)
                        {
                            DropCandMark(w.Cands[0]);
                            w.Cands.RemoveAt(0);
                        }
                    }
                }

                // ---- STAGE 2: each candidate resolves on its OWN bar -------------------
                for (int c = w.Cands.Count - 1; c >= 0; c--)
                {
                    RetestCand cd = w.Cands[c];

                    // CONFIRM: this candidate's bar is a confirmed pivot of the needed side.
                    if (DebugRetestLog)
                        RetestLog(string.Format(
                            "        cand bar={0} price={1:F2} pivotAtBar={2} pivotExists={3} sideAfter={4}",
                            cd.Bar, cd.Price, IsConfirmedPivotAt(cd.Bar, !w.IsLong),
                            PivotExistsAt(cd.Bar), HasConfirmedSidePivotAfter(cd.Bar, !w.IsLong)));

                    if (IsConfirmedPivotAt(cd.Bar, !w.IsLong))
                    {
                        w.ProvBar = cd.Bar; w.ProvTime = cd.Time; w.ProvPrice = cd.Price;
                        w.MarkTag = cd.Tag;
                        DrawRetestCand(w, cd, true);   // same tag -> updates in place
                        int cAgo = CurrentBars[0] - cd.Bar;
                        PublishRetest(cAgo, w.IsLong ? 2 : -2, cd.Price, w.Edge);
                        RetestAlert(cd, w.IsLong, true);
                        w.Cands.RemoveAt(c);           // resolved; the mark stays on the chart
                        continue;
                    }

                    // DROP: that bar holds no pivot at all AND the swing has since formed
                    // elsewhere. A pivot is unconfirmed for at least one bar by definition,
                    // so anything less patient than this throws away real signals.
                    if (!PivotExistsAt(cd.Bar) && HasConfirmedSidePivotAfter(cd.Bar, !w.IsLong))
                    {
                        DropCandMark(cd);
                        w.Cands.RemoveAt(c);
                    }
                }
                w.Provisional = w.Cands.Count > 0;

                // ---- arm state from THIS bar's close, after the signal was evaluated ----
                if (closedOut) { w.Armed = false; }
                else if (reCross || armedNow) { w.Armed = true; }
            }

            // Only if a stage-1/stage-2 publish has not already stamped this bar's edge.
            if (activeEdge != 0 && ago >= 0 && ago < 250 && ago <= CurrentBars[0]
                && Values[PLOT_RETEST_EDGE][ago] == 0)
                Values[PLOT_RETEST_EDGE][ago] = activeEdge;
        }

        // DIAGNOSTIC. Why a retest did or did not fire, in numbers rather than pixels.
        private void RetestLog(string line)
        {
            try
            {
                string p = LogFile("MirrorRetestV0047.log");
                System.IO.File.AppendAllText(p, LogStamp() + " " + line + Environment.NewLine);
            }
            catch { }
        }

        // DIAGNOSTIC. One dot per CONFIRMED chart-series pivot (entries 0..n-2; the newest
        // is still developing). This answers the only question that matters when a signal is
        // missing: did the pivot engine see a pivot on that bar at all?
        private void DrawChartPivotDots()
        {
            if (_cpEngine == null) return;
            int n = _cpEngine.Bars.Count;
            if (n < 2) return;
            int first = Math.Max(0, n - 60);          // recent history only
            for (int i = first; i <= n - 2; i++)
            {
                int bar = _cpEngine.Bars[i];
                int ago = CurrentBars[0] - bar;
                if (ago < 0 || ago > 250 || ago > CurrentBars[0]) continue;
                try
                {
                    Draw.Dot(this, "MirCP_" + bar + (_cpEngine.IsHigh[i] ? "H" : "L"),
                             false, ago, _cpEngine.Prices[i],
                             _cpEngine.IsHigh[i] ? Brushes.Magenta : Brushes.Cyan);
                }
                catch { }
            }
        }

        private void PublishRetest(int agoIdx, double signal, double price, double edge)
        {
            if (agoIdx < 0 || agoIdx >= 250 || agoIdx > CurrentBars[0]) return;
            Values[PLOT_RETEST_SIGNAL][agoIdx] = signal;
            Values[PLOT_RETEST_PRICE][agoIdx]  = price;
            Values[PLOT_RETEST_EDGE][agoIdx]   = edge;
        }

        // A pivot LOW confirms a long, a pivot HIGH confirms a short. The NEWEST pivot is
        // still developing (a more extreme same-side bar can still replace it), exactly as
        // RedrawDailyBiasLevels treats the daily set - so only entries 0..n-2 count as
        // CONFIRMED.
        private bool IsConfirmedPivotAt(int bar, bool wantHigh)
        {
            if (_cpEngine == null) return false;
            int n = _cpEngine.Bars.Count;
            if (n < 2) return false;

            // The NEWEST pivot is still developing - a more extreme same-side bar can still
            // replace it - so only entries 0..n-2 are CONFIRMED. Same rule the daily bias
            // uses (RedrawDailyBiasLevels: confirmed = Count - 1).
            for (int i = n - 2; i >= 0; i--)
            {
                if (_cpEngine.Bars[i] < bar) break;             // sorted; older from here on
                if (_cpEngine.Bars[i] != bar) continue;
                return _cpEngine.IsHigh[i] == wantHigh;
            }
            return false;
        }

        // Does the engine hold ANY pivot on this bar, confirmed or still developing? A
        // developing pivot on a candidate's bar is the normal case for a bar or two, and
        // must not be read as "this bar was never a pivot".
        private bool PivotExistsAt(int bar)
        {
            if (_cpEngine == null) return false;
            for (int i = _cpEngine.Bars.Count - 1; i >= 0; i--)
            {
                if (_cpEngine.Bars[i] < bar) return false;   // sorted
                if (_cpEngine.Bars[i] == bar) return true;
            }
            return false;
        }

        // A CONFIRMED pivot of the side this trade needs, strictly after `bar`: the swing
        // formed somewhere else, so this candidate will never be upgraded.
        private bool HasConfirmedSidePivotAfter(int bar, bool wantHigh)
        {
            if (_cpEngine == null) return false;
            for (int i = _cpEngine.Bars.Count - 2; i >= 0; i--)
            {
                if (_cpEngine.Bars[i] <= bar) return false;
                if (_cpEngine.IsHigh[i] == wantHigh) return true;
            }
            return false;
        }

        // Draw (or update) one candidate's mark. Same tag for faint and solid, so the
        // upgrade updates in place and never restacks.
        // Sound + Alerts-window entry for a retest mark. Realtime only, once per bar/side/stage:
        // stacked zones can confirm the same bar from several watches, which is one signal.
        private void RetestAlert(RetestCand cd, bool isLong, bool confirmed)
        {
            if (!RetestSoundAlert || State != State.Realtime || ExportMode) return;

            string key = cd.Bar + (isLong ? "L" : "S") + (confirmed ? "C" : "P");
            if (!_rtAlerted.Add(key)) return;
            if (_rtAlerted.Count > 500) _rtAlerted.Clear();

            string file = isLong ? RetestLongSound : RetestShortSound;
            string path = string.IsNullOrWhiteSpace(file) ? ""
                        : System.IO.Path.IsPathRooted(file) ? file
                        : System.IO.Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", file);
            if (path.Length > 0 && !System.IO.File.Exists(path))
            {
                Print(Name + ": retest alert sound not found: " + path);
                path = "";
            }

            string msg = string.Format("{0} {1}: retest {2} {3} @ {4}",
                Instrument.FullName, BarsPeriod, isLong ? "LONG" : "SHORT",
                confirmed ? "confirmed" : "provisional", Instrument.MasterInstrument.FormatPrice(cd.Price));
            try
            {
                Alert("MirRTAlert_" + key, confirmed ? Priority.High : Priority.Medium, msg, path, 0,
                      isLong ? Brushes.DarkCyan : Brushes.DarkRed, Brushes.White);
            }
            catch (Exception ex) { Print(Name + ": retest alert failed: " + ex.Message); }
        }

        private void DrawRetestCand(RetestWatch w, RetestCand cd, bool confirmed)
        {
            if (_rtMarks == null || cd == null) return;

            RetestMark m = null;
            for (int i = 0; i < _rtMarks.Count; i++)
                if (_rtMarks[i].Tag == cd.Tag) { m = _rtMarks[i]; break; }

            if (m == null)
            {
                m = new RetestMark { Tag = cd.Tag, Time = cd.Time, Price = cd.Price, IsLong = w.IsLong };
                _rtMarks.Add(m);
                int cap = Math.Max(10, MaxCrossArrows);
                while (_rtMarks.Count > cap)
                {
                    try { RemoveDrawObject(_rtMarks[0].Tag); } catch { }
                    _rtMarks.RemoveAt(0);
                }
            }

            m.Confirmed = confirmed;
            DrawOneRetest(m);
        }

        // Remove an UNCONFIRMED candidate's mark. A confirmed mark is history and stays.
        private void DropCandMark(RetestCand cd)
        {
            if (cd == null || _rtMarks == null) return;
            for (int i = 0; i < _rtMarks.Count; i++)
            {
                if (_rtMarks[i].Tag != cd.Tag) continue;
                if (!_rtMarks[i].Confirmed)
                {
                    try { RemoveDrawObject(_rtMarks[i].Tag); } catch { }
                    _rtMarks.RemoveAt(i);
                }
                break;
            }
        }

        // Remove an unconfirmed provisional mark, leaving confirmed ones alone.
        // Disarm path: every unconfirmed candidate goes. Confirmed marks are history.
        private void DropProvisional(RetestWatch w)
        {
            if (w == null) return;
            for (int c = 0; c < w.Cands.Count; c++)
                DropCandMark(w.Cands[c]);
            w.Cands.Clear();
            w.Provisional = false;
        }

        // Nebula's BrightState plot: +1 on a bright-green bar, -1 on a bright-red bar, 0
        // otherwise. 0 whenever Nebula is not hosted or has no value for the bar.
        private int NebulaBright(int ago)
        {
            if (_neb == null || ago < 0) return 0;
            try
            {
                Series<double> s = _neb.BrightState;
                if (s == null || !s.IsValidDataPoint(ago)) return 0;
                double v = s[ago];
                return v > 0.5 ? 1 : (v < -0.5 ? -1 : 0);
            }
            catch { return 0; }
        }

        private void DrawRetestMark(RetestWatch w, bool confirmed)
        {
            if (_rtMarks == null) return;

            RetestMark m = null;
            for (int i = 0; i < _rtMarks.Count; i++)
                if (_rtMarks[i].Tag == w.MarkTag) { m = _rtMarks[i]; break; }

            if (m == null)
            {
                m = new RetestMark { Tag = w.MarkTag, Time = w.ProvTime, Price = w.ProvPrice, IsLong = w.IsLong };
                _rtMarks.Add(m);
                int cap = Math.Max(10, MaxCrossArrows);
                while (_rtMarks.Count > cap)
                {
                    try { RemoveDrawObject(_rtMarks[0].Tag); } catch { }
                    _rtMarks.RemoveAt(0);
                }
            }

            m.Confirmed = confirmed;
            DrawOneRetest(m);
        }

        private void DrawOneRetest(RetestMark m)
        {
            if (ExportMode || m == null) return;
            // Provisional marks are OFF by default: every touching bar makes a candidate, so
            // on a multi-bar retest they crowd the level. The candidates still run - only
            // their drawing is suppressed - so confirmation is unaffected.
            if (!m.Confirmed && !ShowProvisionalMarks)
            {
                try { RemoveDrawObject(m.Tag); } catch { }
                return;
            }
            double off = 3 * TickSize;
            string sym = m.IsLong ? SafeSymbol(RetestLongSymbol, "▲") : SafeSymbol(RetestShortSymbol, "▼");
            Brush col = m.Confirmed ? (RetestConfirmedColor ?? Brushes.Aqua) : (RetestProvisionalColor ?? Brushes.Khaki);
            Draw.Text(this, m.Tag, false, sym, m.Time, m.IsLong ? m.Price - off : m.Price + off, 0,
                col, new SimpleFont("Arial", Math.Max(4, RetestSymbolFontSize)), TextAlignment.Center,
                Brushes.Transparent, Brushes.Transparent, 0);
        }

        // Drop a watch and, if its mark never confirmed, take the mark off the chart too.
        private void KillWatch(RetestWatch w, int index)
        {
            if (w == null) return;

            if (w.Provisional && _rtMarks != null)
            {
                for (int i = 0; i < _rtMarks.Count; i++)
                {
                    if (_rtMarks[i].Tag != w.MarkTag) continue;
                    if (!_rtMarks[i].Confirmed)
                    {
                        try { RemoveDrawObject(_rtMarks[i].Tag); } catch { }
                        _rtMarks.RemoveAt(i);
                    }
                    break;
                }
            }

            if (index >= 0 && index < _rtWatches.Count && _rtWatches[index] == w)
                _rtWatches.RemoveAt(index);
            else
                _rtWatches.Remove(w);
        }

        // Re-issue the retest marks after a manual Clean, alongside RedrawCrossArrows.
        private void RedrawRetestMarks()
        {
            if (ExportMode || _rtMarks == null) return;
            foreach (RetestMark m in _rtMarks) DrawOneRetest(m);
        }

        // ---- The chart-series pivot engine --------------------------------------------
        // A SECOND, INDEPENDENT instance of the Daily Bias pivot rules, run on BarsInProgress
        // 0. The rules below are a literal transcription of ProcessDailyBiasBar and
        // DbProcessPivot (see the "Daily Bias Levels" region) parameterised by series index
        // and engine instance; the daily engine's own code and lists are untouched, so the
        // two can never share state. If the daily rules ever change, change these to match.

        private void PivotCatchUp(PivotEngine pe, int bip, int through)
        {
            if (pe == null) return;
            for (int b = pe.LastProcessedBar + 1; b <= through; b++)
                PivotProcessBar(pe, bip, b);
        }

        // One CLOSED bar of series `bip`. Same six two-bar colour/extreme conditions as
        // ProcessDailyBiasBar, in the same order (the order decides how the same-side
        // replacement resolves on an outside bar).
        private void PivotProcessBar(PivotEngine pe, int bip, int barIdx)
        {
            if (pe == null || barIdx <= pe.LastProcessedBar) return;
            pe.LastProcessedBar = barIdx;

            if (bip < 0 || bip >= BarsArray.Length) return;

            int ago = CurrentBars[bip] - barIdx;
            if (barIdx < 2 || ago < 0 || ago + 1 > CurrentBars[bip]) return;
            if (ago + 1 >= 250) return;   // beyond MaximumBarsLookBack: unreachable, skip it

            double o0 = Opens[bip][ago],     c0 = Closes[bip][ago],     h0 = Highs[bip][ago],     l0 = Lows[bip][ago];
            double o1 = Opens[bip][ago + 1], c1 = Closes[bip][ago + 1], h1 = Highs[bip][ago + 1], l1 = Lows[bip][ago + 1];

            bool isHigh    = h0 > h1;
            bool isLow     = l0 < l1;
            bool prevGreen = c1 >= o1;
            bool currGreen = c0 >= o0;
            DateTime t0    = Times[bip][ago];

            if (prevGreen && currGreen && isHigh)   PivotRegister(pe, barIdx, h0, true,  Math.Max(o0, c0), t0);
            if (!prevGreen && !currGreen && isLow)  PivotRegister(pe, barIdx, l0, false, Math.Min(o0, c0), t0);
            if (prevGreen && !currGreen && isHigh)  PivotRegister(pe, barIdx, h0, true,  Math.Max(o0, c0), t0);
            if (!prevGreen && currGreen && isLow)   PivotRegister(pe, barIdx, l0, false, Math.Min(o0, c0), t0);

            // The OUTSIDE-bar pair: higher high AND lower low registers BOTH sides.
            if (!prevGreen && currGreen && isHigh)  PivotRegister(pe, barIdx, h0, true,  Math.Max(o0, c0), t0);
            if (prevGreen && !currGreen && isLow)   PivotRegister(pe, barIdx, l0, false, Math.Min(o0, c0), t0);
        }

        // Identical body to DbProcessPivot: a same-side pivot is REPLACED by a more extreme
        // one; the side must alternate before a new entry is appended.
        private static void PivotRegister(PivotEngine pe, int barIdx, double price, bool isHigh, double guide, DateTime time)
        {
            int n = pe.Bars.Count;
            if (n > 0 && pe.IsHigh[n - 1] == isHigh)
            {
                if (isHigh ? price > pe.Prices[n - 1] : price < pe.Prices[n - 1])
                {
                    pe.Bars[n - 1]   = barIdx;
                    pe.Prices[n - 1] = price;
                    pe.Guides[n - 1] = guide;
                    pe.Times[n - 1]  = time;
                }
                return;
            }
            pe.Bars.Add(barIdx);
            pe.Prices.Add(price);
            pe.IsHigh.Add(isHigh);
            pe.Guides.Add(guide);
            pe.Times.Add(time);
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

        // The Click lambdas capture `this`. Held as fields so teardown can unsubscribe them -
        // otherwise the Button keeps a strong reference to a discarded indicator instance and
        // the whole object graph (tracked levels, zone sets, hosted sources) leaks with it.
        private RoutedEventHandler _hSettings, _hExport, _hClean;

        private const string TB_SETTINGS_ID = "AlightenMirrorV0047SignalSettingsBtn";
        private const string TB_EXPORT_ID   = "AlightenMirrorV0047SignalExportBtn";
        private const string TB_CLEAN_ID    = "AlightenMirrorV0047SignalCleanBtn";

        // A recompile discards this instance before the Terminated teardown - which is queued
        // on the chart Dispatcher - ever runs, so the PREVIOUS build's buttons stay parented to
        // the chart window. They keep the old build's caption, and their Click lambdas still
        // capture the dead instance, so pressing one runs the old build's code. Sweep any button
        // carrying one of our automation ids before adding fresh ones.
        private void RemoveOrphanToolbarButtons()
        {
            if (chartWindow == null || chartWindow.MainMenu == null) return;
            try
            {
                string[] ids = { TB_SETTINGS_ID, TB_EXPORT_ID, TB_CLEAN_ID };
                List<System.Windows.UIElement> doomed = new List<System.Windows.UIElement>();
                foreach (System.Windows.UIElement el in chartWindow.MainMenu)
                {
                    FrameworkElement fe = el as FrameworkElement;
                    if (fe == null) continue;
                    string id = AutomationProperties.GetAutomationId(fe);
                    if (!string.IsNullOrEmpty(id) && ids.Contains(id)) doomed.Add(el);
                }
                foreach (System.Windows.UIElement el in doomed) chartWindow.MainMenu.Remove(el);
                if (doomed.Count > 0)
                    Print("[AlightenMirrorV0047Signal] cleared " + doomed.Count
                        + " orphaned toolbar button(s) left by a previous build");
            }
            catch (Exception ex) { Print("[AlightenMirrorV0047Signal] orphan sweep: " + ex.Message); }
        }

		private void CreateToolbarButton()
        {
            try {
                if (ChartControl == null) return;
                chartWindow = Window.GetWindow(ChartControl.Parent) as NinjaTrader.Gui.Chart.Chart;
                if (chartWindow == null || settingsButton != null) return;

                RemoveOrphanToolbarButtons();

                // Button IDs are VERSION-SPECIFIC. Every Mirror from V0034 to V0040 registers
                // "AlightenMirrorV33SettingsBtn" / "AlightenMirrorV33ExportBtn" -- a hardcoded ID
                // that was never bumped. NinjaTrader keys toolbar buttons by that ID, so a button
                // left behind by an older version (teardown goes through Dispatcher.InvokeAsync
                // and is skipped when a recompile discards the instance first) gets REUSED, and
                // the click runs the dead instance's handler. Symptom: "[MirrorV0034] ForceUISync
                // Error" from a version that is on no chart.
                _hSettings = (s, e) => OpenSettingsWindow();
                settingsButton = IndicatorVisualStyleHelper.CreateSettingsButton("Mirror Settings", TB_SETTINGS_ID, _hSettings);
                settingsButton.Width = 110;
                settingsButton.ToolTip = "Configure Mirror V0046Signal visibility settings";
                chartWindow.MainMenu.Add(settingsButton);

                _hExport = (s, e) => ExportLevelsToCsv();
                exportButton = IndicatorVisualStyleHelper.CreateSettingsButton("Export Levels", TB_EXPORT_ID, _hExport);
                exportButton.Width = 110;
                exportButton.ToolTip = "Export tracked levels to CSV (V0046Signal)";
                chartWindow.MainMenu.Add(exportButton);

                // V0036: one-click Clean Invalid (same as the modal button). Danger
                // palette keeps it red through hover/theme repaints.
                _hClean = (s, e) => { PerformManualRefreshCleanup(); ForceUISync(); };
                cleanButton = IndicatorVisualStyleHelper.CreateDangerDialogButton("Clean Mirror", _hClean);
                AutomationProperties.SetAutomationId(cleanButton, TB_CLEAN_ID);
                cleanButton.Width = 100;
                cleanButton.Height = 28;
                cleanButton.Margin = new Thickness(6, 3, 6, 3);
                cleanButton.ToolTip = "Remove currently-active levels price has crossed, purge failed zones, and rebuild active zones (same as the modal's Clean Invalid)";
                chartWindow.MainMenu.Add(cleanButton);
            } catch (Exception ex) { Print("[AlightenMirrorV0047Signal] toolbar error: " + ex.Message); }
        }

        private void TryRemoveToolbarButton()
        {
            // Unsubscribe BEFORE removing: a button that outlives this call (a menu already torn
            // down, an exception mid-way) must not still be able to invoke a dead instance.
            try { if (settingsButton != null && _hSettings != null) settingsButton.Click -= _hSettings; } catch { }
            try { if (exportButton   != null && _hExport   != null) exportButton.Click   -= _hExport;   } catch { }
            try { if (cleanButton    != null && _hClean    != null) cleanButton.Click    -= _hClean;    } catch { }

            try { if (chartWindow != null && settingsButton != null) chartWindow.MainMenu.Remove(settingsButton); } catch { }
            try { if (chartWindow != null && exportButton != null) chartWindow.MainMenu.Remove(exportButton); } catch { }
            try { if (chartWindow != null && cleanButton != null) chartWindow.MainMenu.Remove(cleanButton); } catch { }
            try { if (settingsWindow != null) settingsWindow.Close(); } catch { }

            _hSettings = null; _hExport = null; _hClean = null;
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
                Print("[AlightenMirrorV0047Signal] Export error: " + ex.Message);
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

            sb.AppendLine("  \"source_indicators\": [\"AlightenMirrorPtAV0011\", \"AlightenMirrorPtBV0005\", \"AlightenMirrorPtGV0003\", \"AlightenMirrorPtHV0003\", \"AlightenMirrorPtFV0004\", \"AlightenMirrorPtJV0008\"],");
            sb.AppendLine("  \"bar_timestamp\": \"close\",");
            sb.AppendLine("  \"exported_by\": \"AlightenMirrorV0047Signal\"");
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
		        Print($"[AlightenMirrorV0047Signal] Failed to open settings: {ex.Message}");
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
                RedrawCrossArrows();     // signals survive a Clean - it cleans levels and zones, not signals
                RedrawRetestMarks();     // ditto for the V0046 provisional/confirmed entry marks
                if (ShowChartPivotDots) DrawChartPivotDots();   // the diagnostic must survive a Clean too

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

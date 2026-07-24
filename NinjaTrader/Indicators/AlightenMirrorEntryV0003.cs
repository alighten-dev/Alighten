/* Version ID: 2026-07-18a AlightenMirrorEntryV0003 — Entry-bar confirmation signals at Mirror levels
   + LTF support-lost / resistance-gained trigger (ported from the MagicOpposingClusterFilter
   strategy's lower-timeframe pair trigger engine) on a user-selected added series.
   Self-contained: hosts the same pattern sources as AlightenMirrorV0031 (PtA V0010, PtB V0005,
   PtG V0002, PtH V0002, PtF V0003) to track levels internally (no level drawing — run it alongside
   the visual Mirror), plus a calc-only orderflow engine ported from AlightenFootprintOrderFlowV00021
   (tick-classified bid/ask rows, POC/VA — no rendering).

   When a closed primary bar wicks an active Mirror level in the level's direction, the enabled
   confirmations are evaluated on that bar; an entry arrow prints when >= MinConfirmations fire.
   Optional two-bar trigger: the arrow only prints when a following bar takes out the setup bar's
   extreme (within TriggerExpiryBars).

   Levels on a still-forming HTF bar signal PROVISIONALLY at the chart-bar close. When the HTF
   bar closes: a provisional signal whose level did not survive is retracted (arrow removed,
   one-signal budget refunded), and touches missed while the level was forming/repainting are
   back-filled — the live chart converges to the refreshed/historical state on its own.

   Confirmation codes drawn next to the arrow:
     S = Sweep & Reclaim (price traded >= SweepMinPenetrationTicks through the level, closed back)
     P = Volume POC in the touch wick        D = Delta POC (max |delta| row) in the touch wick
     A = Wick-delta Absorption               V = Stopping Volume at the extreme
     F = Delta Flip vs previous bar          E = Exhaustion (near-zero opposing volume at extreme)
     X = Extreme POC (POC on first/last row) C = VA+POC both at the bar extreme (combo)
     R = Relative Delta wick — exact port of AlightenRelativeDeltaV0001: own bin grid
         (RelativeDeltaAggregationInterval, default 6) and Globex-trading-day anchor

   Plots (transparent, for strategies/Bloodhound): plot 0 stamps the entry bar (the setup bar,
   or the trigger bar in two-bar mode); plots 1-11 stamp the setup bar (plot 1 also stamps the
   trigger bar in two-bar mode):
     0 Entry (+1/-1)   1 Confluence (signed count)   2 SweepReclaim   3 PocInWick   4 DeltaPocInWick
     5 Absorption   6 StoppingVolume   7 DeltaFlip   8 Exhaustion   9 ExtremePoc   10 VaPocExtreme
     11 RelativeDelta   12 LtfTrigger (+1/-1, stamps the trigger-event bar)

   New in V0002 — LTF trigger: the Magic strategy's lower-timeframe level engine runs on an added
   data series (Minute/Second/Tick/Range, user-set value). Pivot levels flip sides over time: a
   SUPPORT is LOST when a closed LTF bar crosses below it (the level becomes resistance), and a
   RESISTANCE is GAINED when a bar closes above it (it becomes support); levels can also become
   TESTED (approached, touched within tolerance, close held). Trigger eligibility modes match the
   strategy: AnyLevel, AnyTestedLevel, OuterPairBoundary. A triangle signal prints when, after a
   Mirror level has been touched (armed — EVERY touch re-arms it, so the signal can repeat):
     LONG:  the LTF engine GAINS an eligible resistance ("RL GAINED")
     SHORT: the LTF engine LOSES an eligible support ("SG LOST")
   Optional LtfLevelMaxDistanceTicks (0 = off) additionally requires the LTF trigger level to sit
   within that many ticks of the Mirror level. Signals are labeled "L <pattern><TF>", honor the
   same visibility toggles, and follow the same provisional/retract lifecycle as entries when
   their level is on a still-forming HTF bar.

   New in V0003 — RESEARCH LOG (group 08): when enabled, EVERY level touch (loose test: wick
   within TouchToleranceTicks of the level, no max-penetration / close-beyond / one-signal /
   MinConfirmations gates) is logged as one research row with the touch-bar OHLC, penetration
   geometry, all 10 confirmation flags, whether the display signal actually fired, and forward
   outcomes (MFE/MAE at 15/30/60/120m, target-hit time, MAE-before-target) measured from the
   touch bar's close — same outcome engine as AlightenMirrorV0031. LTF trigger firings are
   logged too (SigType=L). The "Export Research" toolbar button writes
   Documents\NinjaTrader 8\Export\MirrorEntryResearch_*.csv for tools/mine_entry_regime.py,
   which reconstructs the higher-timeframe touch REGIME (last 60m/240m/Daily touch direction +
   price position vs that level) offline by sorting the rows chronologically — the indicator
   itself does not need to know the regime at signal time (levels are discovered retroactively
   at HTF closes, so online regime state would be look-ahead-inconsistent). Rows from touches
   of still-forming levels are flagged Provisional (Retracted when the level dies); collect
   research data from a fresh historical reload for a clean dataset. */
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript
{
    // Version-suffixed enums make V0003 fully self-contained: V0002 declares its own
    // (un-suffixed) copies, and NT8 forbids duplicate type names in this namespace.
    // Declared in NinjaTrader.NinjaScript (not .Indicators) so NT8's auto-generated
    // wrapper code in the MarketAnalyzerColumns/Strategies namespaces can resolve them.
    public enum AlightenLtfSeriesTypeV0003
    {
        Minute,
        Second,
        Tick,
        Range
    }

    public enum AlightenLtfTriggerLevelModeV0003
    {
        AnyLevel,
        AnyTestedLevel,
        OuterPairBoundary
    }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenMirrorEntryV0003 : Indicator
    {
        #region Class Variables
        private const int NUM_TF  = 7;
        private const int NUM_PAT = 5; // A, B, G, H, F

        private static readonly string[] _patternKeys = { "A", "B", "G", "H", "F" };

        private class TrackedLevel
        {
            public string Tag;
            public double Price;
            public DateTime HtfStartTime;
            public DateTime HtfEndTime;
            public bool IsLong;
            public string PatternType;
            public int TfIndex;
            public int CreatedBip0Bar;
            public int LastUpdatedBip0Bar;
            public bool IsLive;
            public bool Removed;
            public bool RetroEvaluated; // historical: window bars already walked for touches
            public bool LtfRetroEvaluated; // LTF-trigger window walk done
            public LevelChain Chain;    // shared across the contiguous same-price segments of this level
        }

        // A persistent level price spawns a new TrackedLevel segment per HTF bar. Age and
        // one-signal accounting must span the whole price chain, not a single segment —
        // otherwise every HTF rollover makes the level look brand-new again.
        private class LevelChain
        {
            public string Key;
            public int FirstSeenBar;
            public int Signals;
            public int Segments;
            public bool LtfArmed;    // level touched since the last LTF signal — every touch re-arms
        }
        private Dictionary<string, LevelChain> _chains;

        // PERF: per-slot sync cache (same fast path as MirrorV0031)
        private class SyncSlot
        {
            public long StartTicks;
            public double Price;
            public TrackedLevel Level;
        }
        private SyncSlot[] _syncCache;

        private class PendingEntry
        {
            public bool IsLong;
            public double TriggerPrice;
            public int SetupBar;
            public int ExpiryBar;
            public int Confirmations;
            public EmittedSignal Signal;
        }
        private List<PendingEntry> _pending;

        // Every emitted signal is recorded so the settings modal can hide/reshow arrows
        // per pattern/TF without re-evaluating (visibility only — plots are unaffected).
        private class EmittedSignal
        {
            public int SetupBarIdx;
            public DateTime SetupTime;
            public bool IsLong;
            public string Pattern;
            public int TfIndex;
            public string Label;
            public double CodeY;
            public bool HasArrow;      // false while a two-bar setup awaits its trigger
            public DateTime ArrowTime;
            public double ArrowY;
            public int Count;          // confirmations fired (plot 1 magnitude)
            public int ConfMask;       // bit i -> Values[2+i] stamped (S,P,D,A,V,F,E,X,C,R)
            public TrackedLevel Level; // level segment that produced the signal
            public bool Provisional;   // emitted off a still-forming HTF bar; validated at its close
            public bool Retracted;     // provisional signal whose level did not survive
            public bool IsLtf;         // LTF trigger signal (triangle, plot 12), not a confirmation entry
            public ResearchEvent Research; // linked research row (marked Retracted with the signal)
        }
        private List<EmittedSignal> _signals;

        // ---- Research log: one row per level touch (loose test), forward outcomes per V0031 ----
        private class ResearchEvent
        {
            public int Id;
            public int SetupBarIdx;         // primary bar index of the touch bar
            public DateTime Time;           // touch bar close time
            public string SigType;          // "T" = touch/entry candidate, "L" = LTF trigger firing
            public string Pattern;
            public int TfIndex;
            public bool IsLong;
            public double LevelPrice;
            public double RefPrice;         // touch bar close — outcome measurement origin
            public double BarOpen, BarHigh, BarLow, BarClose;
            public double PenetrationTicks; // wick beyond the level (negative = stopped short of it)
            public double CloseVsLevelTicks;// signed: + = closed above the level
            public bool ClosedBeyond;       // closed on the level's entry side
            public bool BlownThrough;       // penetration exceeded MaxPenetrationTicks
            public int ConfCount;
            public int ConfMask;            // bit i -> S,P,D,A,V,F,E,X,C,R
            public string Codes;
            public bool SignalEmitted;      // the display signal path actually fired on this touch
            public bool Provisional;        // touched a still-forming HTF level (realtime only)
            public bool Retracted;          // linked signal retracted (level died at HTF close)
            public double MfeTicks, MaeTicks;
            public bool TargetHit;
            public double TargetMinutes;
            public double MaeBeforeTargetTicks;
            public double Mfe15, Mae15, Mfe30, Mae30, Mfe60, Mae60, Mfe120, Mae120;
            public bool Done;
            public int LastOutcomeBarIdx;   // newest closed primary bar already folded in
        }
        private List<ResearchEvent> _researchOpen;
        private List<ResearchEvent> _researchDone;
        private Dictionary<string, ResearchEvent> _researchSeen; // dedupe: live + HTF-close backfill re-walk the same bars
        private int _nextResearchId;

        // Rollover bookkeeping: provisional signals of TF t are judged one chart bar
        // after the HTF close (SyncLevels needs a few ticks to flip survivors to
        // IsLive=false before we can tell survival from repaint-away).
        private int[] _pendingValidateBar = new int[NUM_TF];
        private DateTime[] _pendingValidateHtfTime = new DateTime[NUM_TF];

        // Toolbar & settings modal
        private NinjaTrader.Gui.Chart.Chart chartWindow;
        private Button settingsButton;
        private Button exportButton;
        private Window settingsWindow;

        // Orderflow snapshot of one closed bar
        private class BarFlow
        {
            public bool HasRows;
            public double PocPrice, DeltaPocPrice, Vah, Val, BarDelta, TotalVolume;
            public int PocInWick, DeltaPocInWick, ExtremePoc, ExtremeVa, VaPocExtreme;
            public int Absorption, StopVol, DeltaFlip, Exhaustion, RelativeDelta;
            public string RdDebug; // populated only when EnableDebugPrints
        }

        // Debug output: NinjaScript Output window + append to MirrorEntryDebug.txt
        private void Dbg(string msg)
        {
            Print(msg);
            try { System.IO.File.AppendAllText(_dbgPath, msg + "\r\n"); } catch { }
        }

        private AlightenMirrorPtAV0010[] _srcA = new AlightenMirrorPtAV0010[NUM_TF];
        private AlightenMirrorPtBV0005[] _srcB = new AlightenMirrorPtBV0005[NUM_TF];
        private AlightenMirrorPtGV0002[] _srcG = new AlightenMirrorPtGV0002[NUM_TF];
        private AlightenMirrorPtHV0002[] _srcH = new AlightenMirrorPtHV0002[NUM_TF];
        private AlightenMirrorPtFV0003[] _srcF = new AlightenMirrorPtFV0003[NUM_TF];

        // Unified tracked-level store: tracked[pattern][tf]
        private Dictionary<string, TrackedLevel>[][] tracked = new Dictionary<string, TrackedLevel>[NUM_PAT][];

        private string[] _tfLabels = { "D", "240m", "60m", "30m", "15m", "10m", "5m" };
        private int[] _tfMinutes = { 1440, 240, 60, 30, 15, 10, 5 };

        private DateTime[] _lastSeenHtfBarTime = new DateTime[NUM_TF];

        // ---- Orderflow engine (per primary bar, filled from the BIP1 tick stream) ----
        private Dictionary<int, Dictionary<double, double>> GetAskVolumeForPrice   = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> GetBidVolumeForPrice   = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> GetDeltaForPrice       = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> GetTotalVolumeForPrice = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, double> GetDeltaForBar  = new Dictionary<int, double>();
        private Dictionary<int, double> GetVolumeForBar = new Dictionary<int, double>();

        private double sessionOpenPrice = 0;

        // Relative Delta uses its own bin grid + Globex-day anchor for parity with AlightenRelativeDeltaV0001
        private Dictionary<int, Dictionary<double, double>> GetRdDeltaForPrice = new Dictionary<int, Dictionary<double, double>>();
        private double _rdBinInterval;

        // Per-bar orderflow snapshots (lazy, shared between normal and retro evaluation)
        private Dictionary<int, BarFlow> _flowCache = new Dictionary<int, BarFlow>();
        private double _binInterval;

        private string _dbgPath;

        // First primary bar processed in realtime — reload catch-up window marker
        private int _transitionBar = -1;

        // ---- LTF trigger engine (MagicOpposingClusterFilter LTF pair engine port, BIP 9) ----
        private const int LTF_BIP = 9;

        private enum LtfLevelSide { Unknown, Support, Resistance }

        private class LtfLevelNode
        {
            public string Key;
            public int PivotBar;
            public double Level;
            public bool IsHighPivot;
            public LtfLevelSide Side;
            public bool Tested;
            public int StateChangedBar;
            public int LastProcessedBar;
        }

        private readonly List<int>    _ltfPivotBars   = new List<int>();
        private readonly List<double> _ltfPivotWicks  = new List<double>();
        private readonly List<bool>   _ltfPivotIsHigh = new List<bool>();
        private readonly List<double> _ltfGuidePrices = new List<double>();
        private readonly Dictionary<string, LtfLevelNode> _ltfNodes = new Dictionary<string, LtfLevelNode>();
        private int _ltfLastProcessedBar = -1;
        private int _ltfHistoryCutoffBar = -1;

        // Snapshot of the level states that existed BEFORE a closed LTF bar — the
        // authoritative trigger definition (prevents a developing-pivot replacement
        // from silently deleting the transition event). Same design as the strategy.
        private class LtfPriceState
        {
            public double Level;
            public bool HasSupport;
            public bool HasResistance;
            public bool HasTestedSupport;
            public bool HasTestedResistance;
        }

        private class LtfPairBoundaryState
        {
            public bool IsRlPair;
            public bool IsSgPair;
            public double LowerLevel;
            public double UpperLevel;
        }

        // Every trigger the LTF engine emits is recorded (chronological) so levels that
        // sync after their window has passed can be retro-matched against past events.
        private class LtfTriggerEvent
        {
            public DateTime Time;   // LTF bar close time
            public double Level;    // level that was gained (long) / lost (short)
            public bool IsLong;
        }
        private List<LtfTriggerEvent> _ltfEvents;
        #endregion

        #region Properties

        // Ordered as the signal pipeline reads: what counts as a touch -> what the setup
        // bar must do -> signal budget -> optional two-bar trigger -> retired/debug.
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name="Touch Tolerance (ticks)", Description="Bar counts as touching the level when its wick comes within this many ticks.", Order=1, GroupName="01. Entry Logic")]
        public int TouchToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name="Max Penetration (ticks)", Description="Ignore the touch when the wick trades more than this many ticks beyond the level (level blown through).", Order=2, GroupName="01. Entry Logic")]
        public int MaxPenetrationTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Require Close Beyond Level", Description="Setup bar must close back on the correct side of the level (above for longs, below for shorts).", Order=3, GroupName="01. Entry Logic")]
        public bool RequireCloseBeyondLevel { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Min Confirmations", Description="Number of enabled confirmations that must fire on the setup bar for an entry signal.", Order=4, GroupName="01. Entry Logic")]
        public int MinConfirmations { get; set; }

        [NinjaScriptProperty]
        [Display(Name="One Signal Per Level", Description="Each tracked level produces at most one entry signal.", Order=5, GroupName="01. Entry Logic")]
        public bool OneSignalPerLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Two-Bar Trigger", Description="Arrow only prints when a following bar takes out the setup bar's extreme (+1 tick).", Order=6, GroupName="01. Entry Logic")]
        public bool TwoBarTrigger { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name="Trigger Expiry (bars)", Description="Two-bar mode: bars after the setup bar in which the trigger may fire.", Order=7, GroupName="01. Entry Logic")]
        public int TriggerExpiryBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Min Level Age (bars)", Description="RETIRED — no longer gates signals. Forming-bar levels now signal provisionally at the chart-bar close and are retracted at the HTF close if the level does not survive. Kept for settings compatibility.", Order=8, GroupName="01. Entry Logic")]
        public int MinLevelAgeBars { get; set; }

        [Display(Name="Enable Debug Prints", Description="Print touch evaluations and skip reasons to the NinjaScript Output window.", Order=9, GroupName="01. Entry Logic")]
        public bool EnableDebugPrints { get; set; }

        // ---- Confirmations ----
        [NinjaScriptProperty]
        [Display(Name="Enable Sweep and Reclaim (S)", Order=1, GroupName="02. Confirmations")]
        public bool EnableSweepReclaim { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Sweep Min Penetration (ticks)", Description="Sweep & Reclaim: wick must trade at least this many ticks through the level. 0 = an exact touch with a close back on the correct side counts.", Order=2, GroupName="02. Confirmations")]
        public int SweepMinPenetrationTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Volume POC In Wick (P)", Order=3, GroupName="02. Confirmations")]
        public bool EnablePocInWick { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Delta POC In Wick (D)", Order=4, GroupName="02. Confirmations")]
        public bool EnableDeltaPocInWick { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="POC Near Level (ticks, 0=off)", Description="When > 0, the POC-in-wick confirmations additionally require the POC row to sit within this many ticks of the level.", Order=5, GroupName="02. Confirmations")]
        public int PocNearLevelTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Absorption (A)", Order=6, GroupName="02. Confirmations")]
        public bool EnableAbsorption { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Stopping Volume (V)", Order=7, GroupName="02. Confirmations")]
        public bool EnableStoppingVolume { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Delta Flip (F)", Order=8, GroupName="02. Confirmations")]
        public bool EnableDeltaFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Exhaustion (E)", Order=9, GroupName="02. Confirmations")]
        public bool EnableExhaustion { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Extreme POC (X)", Order=10, GroupName="02. Confirmations")]
        public bool EnableExtremePoc { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable VA+POC Extreme (C)", Order=11, GroupName="02. Confirmations")]
        public bool EnableVaPocExtreme { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Relative Delta Wick (R)", Order=12, GroupName="02. Confirmations")]
        public bool EnableRelativeDelta { get; set; }

        // ---- LTF trigger (MagicOpposingClusterFilter lower-timeframe pair engine port) ----
        [NinjaScriptProperty]
        [Display(Name="Enable LTF Trigger", Description="Run the Magic LTF level engine on the added series and signal when an armed Mirror long level sees an eligible resistance GAINED, or an armed Mirror short level sees an eligible support LOST. Every touch of the Mirror level re-arms it.", Order=1, GroupName="03. LTF Trigger")]
        public bool EnableLtfTrigger { get; set; }

        [NinjaScriptProperty]
        [Display(Name="LTF Series Bar Type", Description="Bar type of the added data series the LTF engine runs on (Minute, Second, Tick or Range).", Order=2, GroupName="03. LTF Trigger")]
        public AlightenLtfSeriesTypeV0003 LtfSeriesType { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="LTF Series Value", Description="Period value for the LTF series (minutes, seconds, ticks or range size).", Order=3, GroupName="03. LTF Trigger")]
        public int LtfSeriesValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name="LTF Trigger Level Mode", Description="AnyLevel allows any resistance gain/support loss. AnyTestedLevel requires a tested level. OuterPairBoundary requires gaining the tested upper endpoint of a down-pair for longs or losing the tested lower endpoint of an up-pair for shorts (strategy default).", Order=4, GroupName="03. LTF Trigger")]
        public AlightenLtfTriggerLevelModeV0003 LtfTriggerLevelMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name="LTF Use Wicks For Pair Levels", Description="Uses LTF wick pivot prices instead of body guide prices for the levels.", Order=5, GroupName="03. LTF Trigger")]
        public bool LtfUseWicksForPairLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name="LTF Use Wicks For Gain/Loss", Description="When enabled, an LTF wick through a level changes its side. When disabled, the completed LTF close must cross the level.", Order=6, GroupName="03. LTF Trigger")]
        public bool LtfUseWicksForGainLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name="LTF Gain/Loss Tolerance Ticks", Order=7, GroupName="03. LTF Trigger")]
        public int LtfGainLossToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name="LTF Test Touch Tolerance Ticks", Order=8, GroupName="03. LTF Trigger")]
        public int LtfTestTouchToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name="LTF Test Close Hold Ticks", Order=9, GroupName="03. LTF Trigger")]
        public int LtfTestCloseHoldTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name="LTF Level Max Distance (ticks, 0 = off)", Description="When > 0, the gained/lost LTF level must additionally sit within this many ticks of the Mirror level for the signal to fire.", Order=10, GroupName="03. LTF Trigger")]
        public int LtfLevelMaxDistanceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(20, 5000)]
        [Display(Name="LTF Max Stored Pivots", Order=11, GroupName="03. LTF Trigger")]
        public int LtfMaxStoredPivots { get; set; }

        // ---- Orderflow thresholds (defaults match AlightenFootprintOrderFlowV00021) ----
        // Engine settings first, then per-confirmation thresholds in legend order (A, V, F, E, R).
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="Aggregation Interval (ticks)", Order=1, GroupName="04. Orderflow Thresholds")]
        public int AggregationInterval { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="Value Area %", Order=2, GroupName="04. Orderflow Thresholds")]
        public double ValueAreaPer { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Align Aggregation To Session Open", Order=3, GroupName="04. Orderflow Thresholds")]
        public bool UseSessionOpenForAggregation { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Wick Delta Threshold", Description="Absorption: minimum absolute wick delta.", Order=4, GroupName="04. Orderflow Thresholds")]
        public int WickDeltaThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Wick Delta Percentage (%)", Description="Absorption: wick delta must be at least this % of the bar's absolute delta.", Order=5, GroupName="04. Orderflow Thresholds")]
        public double WickDeltaPercentage { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Stopping Volume Lookback", Order=6, GroupName="04. Orderflow Thresholds")]
        public int StoppingVolumeLookback { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Delta Flip Threshold", Order=7, GroupName="04. Orderflow Thresholds")]
        public int DeltaFlipThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Exhaustion Threshold", Order=8, GroupName="04. Orderflow Thresholds")]
        public int ExhaustionThreshold { get; set; }

        [Range(1, 100)]
        [Display(Name="Relative Delta Tick Aggregation", Description="Bin size for the Relative Delta confirmation only — match the Tick Aggregation of your AlightenRelativeDeltaV0001 instance.", Order=9, GroupName="04. Orderflow Thresholds")]
        public int RelativeDeltaAggregationInterval { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name="Relative Delta Minimum %", Order=10, GroupName="04. Orderflow Thresholds")]
        public int RelativeDeltaMinimumPercentage { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="Relative Delta Max Position", Order=11, GroupName="04. Orderflow Thresholds")]
        public int RelativeDeltaMaxPosition { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Relative Delta Min Wick Levels", Order=12, GroupName="04. Orderflow Thresholds")]
        public int RelativeDeltaMinimumWickLevels { get; set; }

        // ---- Level filters ----
        [NinjaScriptProperty]
        [Display(Name="Use Pattern A", Order=1, GroupName="05. Level Filters")]
        public bool UsePatternA { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Use Pattern B", Order=2, GroupName="05. Level Filters")]
        public bool UsePatternB { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Use Pattern G", Order=3, GroupName="05. Level Filters")]
        public bool UsePatternG { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Use Pattern H", Order=4, GroupName="05. Level Filters")]
        public bool UsePatternH { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Use Pattern F", Order=5, GroupName="05. Level Filters")]
        public bool UsePatternF { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Use Daily Levels", Order=6, GroupName="05. Level Filters")]
        public bool UseTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Use 240m Levels", Order=7, GroupName="05. Level Filters")]
        public bool UseTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Use 60m Levels", Order=8, GroupName="05. Level Filters")]
        public bool UseTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Use 30m Levels", Order=9, GroupName="05. Level Filters")]
        public bool UseTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Use 15m Levels", Order=10, GroupName="05. Level Filters")]
        public bool UseTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Use 10m Levels", Order=11, GroupName="05. Level Filters")]
        public bool UseTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Use 5m Levels", Order=12, GroupName="05. Level Filters")]
        public bool UseTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Src Bars To Process", Order=13, GroupName="05. Level Filters")]
        public int SrcBarsToProcess { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name="Mirror Lookback Bars", Order=14, GroupName="05. Level Filters")]
        public int MirrorLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Invalidated Cleanup", Order=15, GroupName="05. Level Filters")]
        public bool EnableInvalidatedCleanup { get; set; }

        // ---- Visuals ----
        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="Long Color", Order=1, GroupName="06. Visuals")]
        public Brush LongColor { get; set; }
        [Browsable(false)]
        public string LongColorSerialize { get { return Serialize.BrushToString(LongColor); } set { LongColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="Short Color", Order=2, GroupName="06. Visuals")]
        public Brush ShortColor { get; set; }
        [Browsable(false)]
        public string ShortColorSerialize { get { return Serialize.BrushToString(ShortColor); } set { ShortColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name="Arrow Offset (ticks)", Order=3, GroupName="06. Visuals")]
        public int ArrowOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show Signal Codes", Description="Draw the fired confirmation letters next to the setup bar (see indicator header for the legend).", Order=4, GroupName="06. Visuals")]
        public bool ShowSignalCodes { get; set; }

        [NinjaScriptProperty]
        [Range(6, 30)]
        [Display(Name="Code Font Size", Order=5, GroupName="06. Visuals")]
        public int CodeFontSize { get; set; }

        // ---- Signal visibility per pattern/TF (toggled live via the Entry Settings modal;
        //      visibility only — signals stay recorded and plots are unaffected) ----
        [Display(Name="A: Daily", Order=1,  GroupName="07. Signal Visibility")] public bool SignalPatternATF1_Daily { get; set; }
        [Display(Name="A: 240m",  Order=2,  GroupName="07. Signal Visibility")] public bool SignalPatternATF2_240m { get; set; }
        [Display(Name="A: 60m",   Order=3,  GroupName="07. Signal Visibility")] public bool SignalPatternATF3_60m { get; set; }
        [Display(Name="A: 30m",   Order=4,  GroupName="07. Signal Visibility")] public bool SignalPatternATF4_30m { get; set; }
        [Display(Name="A: 15m",   Order=5,  GroupName="07. Signal Visibility")] public bool SignalPatternATF5_15m { get; set; }
        [Display(Name="A: 10m",   Order=6,  GroupName="07. Signal Visibility")] public bool SignalPatternATF6_10m { get; set; }
        [Display(Name="A: 5m",    Order=7,  GroupName="07. Signal Visibility")] public bool SignalPatternATF7_5m { get; set; }
        [Display(Name="B: Daily", Order=8,  GroupName="07. Signal Visibility")] public bool SignalPatternBTF1_Daily { get; set; }
        [Display(Name="B: 240m",  Order=9,  GroupName="07. Signal Visibility")] public bool SignalPatternBTF2_240m { get; set; }
        [Display(Name="B: 60m",   Order=10, GroupName="07. Signal Visibility")] public bool SignalPatternBTF3_60m { get; set; }
        [Display(Name="B: 30m",   Order=11, GroupName="07. Signal Visibility")] public bool SignalPatternBTF4_30m { get; set; }
        [Display(Name="B: 15m",   Order=12, GroupName="07. Signal Visibility")] public bool SignalPatternBTF5_15m { get; set; }
        [Display(Name="B: 10m",   Order=13, GroupName="07. Signal Visibility")] public bool SignalPatternBTF6_10m { get; set; }
        [Display(Name="B: 5m",    Order=14, GroupName="07. Signal Visibility")] public bool SignalPatternBTF7_5m { get; set; }
        [Display(Name="G: Daily", Order=15, GroupName="07. Signal Visibility")] public bool SignalPatternGTF1_Daily { get; set; }
        [Display(Name="G: 240m",  Order=16, GroupName="07. Signal Visibility")] public bool SignalPatternGTF2_240m { get; set; }
        [Display(Name="G: 60m",   Order=17, GroupName="07. Signal Visibility")] public bool SignalPatternGTF3_60m { get; set; }
        [Display(Name="G: 30m",   Order=18, GroupName="07. Signal Visibility")] public bool SignalPatternGTF4_30m { get; set; }
        [Display(Name="G: 15m",   Order=19, GroupName="07. Signal Visibility")] public bool SignalPatternGTF5_15m { get; set; }
        [Display(Name="G: 10m",   Order=20, GroupName="07. Signal Visibility")] public bool SignalPatternGTF6_10m { get; set; }
        [Display(Name="G: 5m",    Order=21, GroupName="07. Signal Visibility")] public bool SignalPatternGTF7_5m { get; set; }
        [Display(Name="H: Daily", Order=22, GroupName="07. Signal Visibility")] public bool SignalPatternHTF1_Daily { get; set; }
        [Display(Name="H: 240m",  Order=23, GroupName="07. Signal Visibility")] public bool SignalPatternHTF2_240m { get; set; }
        [Display(Name="H: 60m",   Order=24, GroupName="07. Signal Visibility")] public bool SignalPatternHTF3_60m { get; set; }
        [Display(Name="H: 30m",   Order=25, GroupName="07. Signal Visibility")] public bool SignalPatternHTF4_30m { get; set; }
        [Display(Name="H: 15m",   Order=26, GroupName="07. Signal Visibility")] public bool SignalPatternHTF5_15m { get; set; }
        [Display(Name="H: 10m",   Order=27, GroupName="07. Signal Visibility")] public bool SignalPatternHTF6_10m { get; set; }
        [Display(Name="H: 5m",    Order=28, GroupName="07. Signal Visibility")] public bool SignalPatternHTF7_5m { get; set; }
        [Display(Name="F: Daily", Order=29, GroupName="07. Signal Visibility")] public bool SignalPatternFTF1_Daily { get; set; }
        [Display(Name="F: 240m",  Order=30, GroupName="07. Signal Visibility")] public bool SignalPatternFTF2_240m { get; set; }
        [Display(Name="F: 60m",   Order=31, GroupName="07. Signal Visibility")] public bool SignalPatternFTF3_60m { get; set; }
        [Display(Name="F: 30m",   Order=32, GroupName="07. Signal Visibility")] public bool SignalPatternFTF4_30m { get; set; }
        [Display(Name="F: 15m",   Order=33, GroupName="07. Signal Visibility")] public bool SignalPatternFTF5_15m { get; set; }
        [Display(Name="F: 10m",   Order=34, GroupName="07. Signal Visibility")] public bool SignalPatternFTF6_10m { get; set; }
        [Display(Name="F: 5m",    Order=35, GroupName="07. Signal Visibility")] public bool SignalPatternFTF7_5m { get; set; }

        // ---- Research log ----
        [Display(Name="Enable Research Log", Description="Log EVERY level touch (loose test, ignoring max-penetration/close-beyond/one-signal/MinConfirmations gates) with confirmation flags and forward MFE/MAE outcomes. Written by the Export Research button as MirrorEntryResearch_*.csv.", Order=1, GroupName="08. Research")]
        public bool EnableResearchLog { get; set; }

        [Range(1, 2000)]
        [Display(Name="Research Target (ticks)", Description="Forward move (in the touch direction, from the touch bar close) that counts as the research target.", Order=2, GroupName="08. Research")]
        public int ResearchTargetTicks { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AlightenMirrorEntryV0003";
                Description = "Entry-bar confirmation signals at Mirror levels (A/B/G/H/F, orderflow confirmations) plus the MagicOpposingClusterFilter LTF support-lost/resistance-gained trigger, with a research log of every level touch (confirmations + forward MFE/MAE) exportable to CSV for offline regime mining.";
                Calculate = Calculate.OnEachTick;
                MaximumBarsLookBack = MaximumBarsLookBack.Infinite; // retro evaluation writes plots deep into HTF windows
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = true;
                IsSuspendedWhileInactive = false;
                ShowTransparentPlotsInDataBox = true;

                // Defaults per the user's saved template (2026-07-24): P + R confirmations
                // only, multi-signal levels, 40-Range LTF trigger on tested levels.
                TouchToleranceTicks = 1;
                MaxPenetrationTicks = 40;
                RequireCloseBeyondLevel = true;
                MinConfirmations = 1;
                OneSignalPerLevel = false;
                TwoBarTrigger = false;
                TriggerExpiryBars = 3;
                MinLevelAgeBars = 1;
                EnableDebugPrints = false;

                EnableSweepReclaim = false;
                SweepMinPenetrationTicks = 0;
                EnablePocInWick = true;
                EnableDeltaPocInWick = false;
                PocNearLevelTicks = 0;
                EnableAbsorption = false;
                EnableStoppingVolume = false;
                EnableDeltaFlip = false;
                EnableExhaustion = false;
                EnableExtremePoc = false;
                EnableVaPocExtreme = false;
                EnableRelativeDelta = true;

                EnableLtfTrigger = true;
                LtfSeriesType = AlightenLtfSeriesTypeV0003.Range;
                LtfSeriesValue = 40;
                LtfTriggerLevelMode = AlightenLtfTriggerLevelModeV0003.AnyTestedLevel;
                LtfUseWicksForPairLevels = false;
                LtfUseWicksForGainLoss = false;
                LtfGainLossToleranceTicks = 0;
                LtfTestTouchToleranceTicks = 0;
                LtfTestCloseHoldTicks = 0;
                LtfLevelMaxDistanceTicks = 40;
                LtfMaxStoredPivots = 500;

                AggregationInterval = 2;
                ValueAreaPer = 70;
                UseSessionOpenForAggregation = true;
                WickDeltaThreshold = 100;
                WickDeltaPercentage = 80.0;
                StoppingVolumeLookback = 3;
                DeltaFlipThreshold = 100;
                ExhaustionThreshold = 8;
                RelativeDeltaAggregationInterval = 6;
                RelativeDeltaMinimumPercentage = 50;
                RelativeDeltaMaxPosition = 2;
                RelativeDeltaMinimumWickLevels = 1;

                UsePatternA = true;
                UsePatternB = false;
                UsePatternG = true;
                UsePatternH = true;
                UsePatternF = true;
                UseTF1_Daily = true;
                UseTF2_240m = true;
                UseTF3_60m = true;
                UseTF4_30m = true;
                UseTF5_15m = true;
                UseTF6_10m = true;
                UseTF7_5m = true;
                SrcBarsToProcess = 800;
                MirrorLookbackBars = 3;
                EnableInvalidatedCleanup = true;

                LongColor = Brushes.Lime;
                ShortColor = Brushes.Red;
                ArrowOffsetTicks = 4;
                ShowSignalCodes = true;
                CodeFontSize = 11;

                EnableResearchLog = true;
                ResearchTargetTicks = 100;

                // Visibility defaults per the user's saved template: only A/G/H signals
                // at 15m and 10m are shown; everything else stays recorded but hidden.
                SignalPatternATF1_Daily = false; SignalPatternATF2_240m = false; SignalPatternATF3_60m = false; SignalPatternATF4_30m = false; SignalPatternATF5_15m = true; SignalPatternATF6_10m = true; SignalPatternATF7_5m = false;
                SignalPatternBTF1_Daily = false; SignalPatternBTF2_240m = false; SignalPatternBTF3_60m = false; SignalPatternBTF4_30m = false; SignalPatternBTF5_15m = false; SignalPatternBTF6_10m = false; SignalPatternBTF7_5m = false;
                SignalPatternGTF1_Daily = false; SignalPatternGTF2_240m = false; SignalPatternGTF3_60m = false; SignalPatternGTF4_30m = false; SignalPatternGTF5_15m = true; SignalPatternGTF6_10m = true; SignalPatternGTF7_5m = false;
                SignalPatternHTF1_Daily = false; SignalPatternHTF2_240m = false; SignalPatternHTF3_60m = false; SignalPatternHTF4_30m = false; SignalPatternHTF5_15m = true; SignalPatternHTF6_10m = true; SignalPatternHTF7_5m = false;
                SignalPatternFTF1_Daily = false; SignalPatternFTF2_240m = false; SignalPatternFTF3_60m = false; SignalPatternFTF4_30m = false; SignalPatternFTF5_15m = false; SignalPatternFTF6_10m = false; SignalPatternFTF7_5m = false;

                AddPlot(Brushes.Transparent, "Entry");           // Values[0]
                AddPlot(Brushes.Transparent, "Confluence");      // Values[1]
                AddPlot(Brushes.Transparent, "SweepReclaim");    // Values[2]
                AddPlot(Brushes.Transparent, "PocInWick");       // Values[3]
                AddPlot(Brushes.Transparent, "DeltaPocInWick");  // Values[4]
                AddPlot(Brushes.Transparent, "Absorption");      // Values[5]
                AddPlot(Brushes.Transparent, "StoppingVolume");  // Values[6]
                AddPlot(Brushes.Transparent, "DeltaFlip");       // Values[7]
                AddPlot(Brushes.Transparent, "Exhaustion");      // Values[8]
                AddPlot(Brushes.Transparent, "ExtremePoc");      // Values[9]
                AddPlot(Brushes.Transparent, "VaPocExtreme");    // Values[10]
                AddPlot(Brushes.Transparent, "RelativeDelta");   // Values[11]
                AddPlot(Brushes.Transparent, "LtfTrigger");      // Values[12]
            }
            else if (State == State.Configure)
            {
                // BIP 1: Tick series (orderflow engine)
                AddDataSeries(BarsPeriodType.Tick, 1);

                // BIP 2: Daily
                var dailyPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Day, Value = 1 };
                AddDataSeries(
                    instrumentName: Instrument.FullName,
                    barsPeriod: dailyPeriod,
                    barsToLoad: SrcBarsToProcess,
                    tradingHoursName: "CME US Index Futures ETH",
                    isResetOnNewTradingDay: true
                );

                // BIP 3: 240-minute
                var fourHourPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 240 };
                AddDataSeries(
                    instrumentName: Instrument.FullName,
                    barsPeriod: fourHourPeriod,
                    barsToLoad: SrcBarsToProcess,
                    tradingHoursName: "CME US Index Futures ETH",
                    isResetOnNewTradingDay: true
                );

                // BIP 4: 60-minute
                var oneHourPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 60 };
                AddDataSeries(
                    instrumentName: Instrument.FullName,
                    barsPeriod: oneHourPeriod,
                    barsToLoad: SrcBarsToProcess,
                    tradingHoursName: "CME US Index Futures ETH",
                    isResetOnNewTradingDay: true
                );

                // BIP 5: 30-minute
                var thirtyMinutePeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 30 };
                AddDataSeries(
                    instrumentName: Instrument.FullName,
                    barsPeriod: thirtyMinutePeriod,
                    barsToLoad: SrcBarsToProcess,
                    tradingHoursName: "CME US Index Futures ETH",
                    isResetOnNewTradingDay: true
                );

                // BIP 6: 15-minute
                var fifteenMinutePeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 15 };
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

                // BIP 9: LTF trigger series (user-selected bar type/value)
                switch (LtfSeriesType)
                {
                    case AlightenLtfSeriesTypeV0003.Minute: AddDataSeries(BarsPeriodType.Minute, Math.Max(1, LtfSeriesValue)); break;
                    case AlightenLtfSeriesTypeV0003.Second: AddDataSeries(BarsPeriodType.Second, Math.Max(1, LtfSeriesValue)); break;
                    case AlightenLtfSeriesTypeV0003.Range:  AddDataSeries(BarsPeriodType.Range,  Math.Max(1, LtfSeriesValue)); break;
                    default:                           AddDataSeries(BarsPeriodType.Tick,   Math.Max(1, LtfSeriesValue)); break;
                }
            }
            else if (State == State.DataLoaded)
            {
                for (int p = 0; p < NUM_PAT; p++)
                {
                    tracked[p] = new Dictionary<string, TrackedLevel>[NUM_TF];
                    for (int t = 0; t < NUM_TF; t++)
                        tracked[p][t] = new Dictionary<string, TrackedLevel>();
                }

                _syncCache = new SyncSlot[NUM_PAT * NUM_TF * 4];
                for (int i = 0; i < _syncCache.Length; i++)
                    _syncCache[i] = new SyncSlot();

                _pending = new List<PendingEntry>(16);
                _signals = new List<EmittedSignal>(256);
                _chains  = new Dictionary<string, LevelChain>(256);

                _researchOpen   = new List<ResearchEvent>(128);
                _researchDone   = new List<ResearchEvent>(4096);
                _researchSeen   = new Dictionary<string, ResearchEvent>(4096);
                _nextResearchId = 0;

                _ltfPivotBars.Clear();
                _ltfPivotWicks.Clear();
                _ltfPivotIsHigh.Clear();
                _ltfGuidePrices.Clear();
                _ltfNodes.Clear();
                _ltfEvents = new List<LtfTriggerEvent>(1024);
                _ltfLastProcessedBar = -1;
                _ltfHistoryCutoffBar = -1;

                // Debug log file (fresh per load) — mirrors all EnableDebugPrints output
                _dbgPath = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "MirrorEntryDebug.txt");
                if (EnableDebugPrints)
                {
                    try { System.IO.File.WriteAllText(_dbgPath, $"[MirrorEntry] debug log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {Instrument.FullName}\r\n"); } catch { }
                }

                for (int t = 0; t < NUM_TF; t++)
                {
                    _lastSeenHtfBarTime[t] = Core.Globals.MinDate;
                    _pendingValidateBar[t] = -1;

                    if (!IsTfEnabled(t)) continue;

                    int bipIdx = t + 2;

                    if (UsePatternA)
                    {
                        _srcA[t] = AlightenMirrorPtAV0010(
                            Closes[bipIdx],
                            SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
                        );
                    }
                    if (UsePatternB)
                    {
                        _srcB[t] = AlightenMirrorPtBV0005(
                            Closes[bipIdx],
                            SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, true, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
                        );
                    }
                    if (UsePatternG)
                    {
                        _srcG[t] = AlightenMirrorPtGV0002(
                            Closes[bipIdx],
                            SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
                        );
                    }
                    if (UsePatternH)
                    {
                        _srcH[t] = AlightenMirrorPtHV0002(
                            Closes[bipIdx],
                            SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
                        );
                    }
                    if (UsePatternF)
                    {
                        _srcF[t] = AlightenMirrorPtFV0003(
                            Closes[bipIdx],
                            SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
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
                case 0: return UsePatternA;
                case 1: return UsePatternB;
                case 2: return UsePatternG;
                case 3: return UsePatternH;
                case 4: return UsePatternF;
            }
            return false;
        }

        private bool IsTfEnabled(int t)
        {
            switch (t)
            {
                case 0: return UseTF1_Daily;
                case 1: return UseTF2_240m;
                case 2: return UseTF3_60m;
                case 3: return UseTF4_30m;
                case 4: return UseTF5_15m;
                case 5: return UseTF6_10m;
                case 6: return UseTF7_5m;
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
            }
            return null;
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

            // Footprint row size = N ticks (snap to integer multiple of TickSize)
            double binInterval = AggregationInterval * TickSize;
            if (!(binInterval > 0.0) || double.IsNaN(binInterval) || double.IsInfinity(binInterval))
                return;
            int ticksPerBin = Math.Max(1, (int)Math.Round(binInterval / TickSize));
            binInterval = ticksPerBin * TickSize;
            _binInterval = binInterval;
            _rdBinInterval = Math.Max(1, RelativeDeltaAggregationInterval) * TickSize;

            // =========================================
            // 1) TICK STREAM: accumulate per-bar rows
            // =========================================
            if (BarsInProgress == 1)
            {
                AccumulateTick(binInterval);
                return;
            }

            // =========================================
            // 1b) LTF SERIES: level gain/loss engine
            // =========================================
            if (BarsInProgress == LTF_BIP)
            {
                if (EnableLtfTrigger)
                {
                    try
                    {
                        if (State == State.Historical)
                        {
                            // Historical: each callback is a completed bar
                            ProcessLtfClosedBar(0);
                        }
                        else if (IsFirstTickOfBar)
                        {
                            // Realtime: evaluate every not-yet-processed closed bar (catches the
                            // historical->realtime seam where bars can be skipped during load)
                            for (int idx = Math.Max(1, _ltfLastProcessedBar + 1); idx <= CurrentBars[LTF_BIP] - 1; idx++)
                                ProcessLtfClosedBar(CurrentBars[LTF_BIP] - idx);
                        }
                    }
                    catch (Exception ex)
                    {
                        Print("[MirrorEntryV0003] CRASH in LTF BIP: " + ex.ToString());
                    }
                }
                return;
            }

            if (BarsInProgress != 0)
                return;

            // =========================================
            // 2) PRIMARY STREAM
            // =========================================
            try
            {
                if (Bars.IsFirstBarOfSession)
                    sessionOpenPrice = Open[0];

                if (_transitionBar < 0 && State != State.Historical)
                    _transitionBar = CurrentBars[0];

                if (IsFirstTickOfBar)
                {
                    // reset live-bar plot slots
                    for (int j = 0; j < Values.Length; j++)
                        Values[j][0] = 0;
                }

                SyncLevels();

                if (EnableLtfTrigger)
                    ArmLtfTouches();

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
                        if (EnableInvalidatedCleanup)
                            CleanupInvalidatedLevelsForTimeframe(t, bipIdx);

                        // HTF bar closed: judge this TF's provisional signals one chart
                        // bar from now (gives SyncLevels time to firm up survivors).
                        _pendingValidateBar[t]     = CurrentBars[0];
                        _pendingValidateHtfTime[t] = Times[bipIdx][1];

                        _lastSeenHtfBarTime[t] = currentHtfBarTime;
                    }
                }

                if (IsFirstTickOfBar)
                {
                    if (EnableResearchLog)
                        UpdateResearchEvents();

                    PruneStaleLevels();

                    for (int t = 0; t < NUM_TF; t++)
                    {
                        if (_pendingValidateBar[t] >= 0 && CurrentBars[0] > _pendingValidateBar[t])
                        {
                            ValidateProvisionalSignals(t, _pendingValidateHtfTime[t]);
                            _pendingValidateBar[t] = -1;
                        }
                    }

                    if (State != State.Historical && CurrentBars[0] >= 2)
                        EvaluateEntryOnClosedBar();
                }

                if (TwoBarTrigger)
                    CheckPendingTriggers();
            }
            catch (Exception ex)
            {
                Print("[MirrorEntryV0003] CRASH in BIP 0: " + ex.ToString());
            }
        }

        #region Tick Accumulation (ported from AlightenFootprintOrderFlowV00021)

        private void AccumulateTick(double binInterval)
        {
            // Handle "in transition" ordering
            int indexOffset   = BarsArray[1].Count - 1 - CurrentBars[1];
            bool inTransition = (State == State.Realtime) && (indexOffset > 1);
            bool useCurrent   = State == State.Historical || inTransition || (Calculate != Calculate.OnBarClose);
            int  ti           = useCurrent ? CurrentBars[1] : Math.Min(CurrentBars[1] + 1, BarsArray[1].Count - 1);

            DateTime t   = BarsArray[1].GetTime(ti);
            int      idx = BarsArray[0].GetBar(t);          // map tick time -> primary bar index
            // NOTE: idx may exceed CurrentBars[0] during historical processing — the full
            // primary array is pre-loaded, so a bar's ticks arrive BEFORE its close event
            // is dispatched. Rejecting those ticks starves every bar of all but its final
            // tick (bug found 2026-07-13). Only reject genuinely unmappable ticks.
            if (idx < 0)
                return;

            if (!GetAskVolumeForPrice.ContainsKey(idx))   GetAskVolumeForPrice[idx]   = new Dictionary<double, double>();
            if (!GetBidVolumeForPrice.ContainsKey(idx))   GetBidVolumeForPrice[idx]   = new Dictionary<double, double>();
            if (!GetDeltaForPrice.ContainsKey(idx))       GetDeltaForPrice[idx]       = new Dictionary<double, double>();
            if (!GetTotalVolumeForPrice.ContainsKey(idx)) GetTotalVolumeForPrice[idx] = new Dictionary<double, double>();

            double price = BarsArray[1].GetClose(ti);
            double vol   = BarsArray[1].GetVolume(ti);

            // Classify strictly by L1 (ignore mid prints)
            double askPx = BarsArray[1].GetAsk(ti);
            double bidPx = BarsArray[1].GetBid(ti);
            bool   isBuy = (askPx > 0 && price >= askPx);
            bool   isSell= (bidPx > 0 && price <= bidPx);
            if (!isBuy && !isSell)
                return;

            // Anchor grid: session open OR that bar's low
            double origin = UseSessionOpenForAggregation ? sessionOpenPrice : BarsArray[0].GetLow(idx);
            origin = Math.Round(origin / TickSize) * TickSize;

            double binsFromOrigin = Math.Floor(((price - origin) / binInterval) + 1e-9);
            double binStart       = origin + binsFromOrigin * binInterval;
            binStart = Math.Round(binStart / TickSize) * TickSize;

            double ask = 0, bid = 0;
            GetAskVolumeForPrice[idx].TryGetValue(binStart, out ask);
            GetBidVolumeForPrice[idx].TryGetValue(binStart, out bid);

            if (isBuy)  ask += vol;
            if (isSell) bid += vol;

            GetAskVolumeForPrice[idx][binStart]   = ask;
            GetBidVolumeForPrice[idx][binStart]   = bid;
            GetDeltaForPrice[idx][binStart]       = ask - bid;
            GetTotalVolumeForPrice[idx][binStart] = ask + bid;

            double prevBarDelta = GetDeltaForBar.ContainsKey(idx) ? GetDeltaForBar[idx] : 0.0;
            double prevBarVol   = GetVolumeForBar.ContainsKey(idx) ? GetVolumeForBar[idx] : 0.0;
            GetDeltaForBar[idx]  = prevBarDelta + (isBuy ? vol : -vol);
            GetVolumeForBar[idx] = prevBarVol + vol;

            // Relative Delta bins: independent grid, always session-open anchored
            // (exact parity with AlightenRelativeDeltaV0001's accumulation)
            if (_rdBinInterval > 0)
            {
                if (!GetRdDeltaForPrice.ContainsKey(idx)) GetRdDeltaForPrice[idx] = new Dictionary<double, double>();

                double rdOrigin = Math.Round(sessionOpenPrice / TickSize) * TickSize;
                double rdBinsFromOrigin = Math.Floor(((price - rdOrigin) / _rdBinInterval) + 1e-9);
                double rdBinStart = rdOrigin + rdBinsFromOrigin * _rdBinInterval;
                rdBinStart = Math.Round(rdBinStart / TickSize) * TickSize;

                double rdDelta = 0;
                GetRdDeltaForPrice[idx].TryGetValue(rdBinStart, out rdDelta);
                GetRdDeltaForPrice[idx][rdBinStart] = rdDelta + (isBuy ? vol : -vol);
            }
        }

        private Dictionary<double, double> Row(Dictionary<int, Dictionary<double, double>> map, int bar)
        {
            return (map != null && map.TryGetValue(bar, out var r)) ? r : null;
        }

        private double Read(Dictionary<int, Dictionary<double, double>> map, int bar, double key)
        {
            if (map != null && map.TryGetValue(bar, out var r) && r != null && r.TryGetValue(key, out var v))
                return v;
            return 0.0;
        }

        #endregion

        #region Per-Bar Orderflow Snapshot

        // Compute the orderflow snapshot for a CLOSED primary bar (index idx, OHLC passed in).
        private BarFlow ComputeBarFlow(int idx, double o, double h, double l, double c, double binInterval)
        {
            var fp = new BarFlow();

            var rows = Row(GetTotalVolumeForPrice, idx);
            if (rows == null || rows.Count == 0)
                return fp;

            fp.HasRows = true;

            var asc = rows.OrderBy(kv => kv.Key).Select(kv => kv.Key).ToList();
            int totalBins = asc.Count;

            fp.TotalVolume = rows.Values.Sum();
            fp.BarDelta    = GetDeltaForBar.TryGetValue(idx, out var bd) ? bd : 0.0;

            // POC + Value Area (simple expansion from POC)
            var pocPair = rows.Aggregate((a, b) => a.Value > b.Value ? a : b);
            fp.PocPrice = pocPair.Key;
            fp.Vah = fp.Val = fp.PocPrice;

            var ascending = rows.OrderBy(kv => kv.Key).ToList();
            double targetVA = fp.TotalVolume * (ValueAreaPer / 100.0);
            int pocIdx = ascending.FindIndex(x => x.Key == fp.PocPrice);
            if (pocIdx < 0) pocIdx = 0;

            double cum = rows.ContainsKey(fp.PocPrice) ? rows[fp.PocPrice] : 0.0;
            int up = pocIdx + 1, down = pocIdx - 1;
            while (cum < targetVA && (up < ascending.Count || down >= 0))
            {
                double vUp = (up   < ascending.Count) ? ascending[up].Value   : 0.0;
                double vDn = (down >= 0)              ? ascending[down].Value : 0.0;

                if (up < ascending.Count && (down < 0 || vUp >= vDn)) { cum += vUp; fp.Vah = ascending[up].Key;   up++; }
                else if (down >= 0)                                    { cum += vDn; fp.Val = ascending[down].Key; down--; }
                else break;
            }

            int valIdx = asc.FindIndex(k => k.Equals(fp.Val));
            int vahIdx = asc.FindIndex(k => k.Equals(fp.Vah));
            if (valIdx < 0) valIdx = pocIdx;
            if (vahIdx < 0) vahIdx = pocIdx;

            int pocBarFromLow  = Math.Max(0, pocIdx);
            int pocBarFromHigh = totalBins - 1 - pocIdx;

            double bodyLow  = Math.Min(o, c);
            double bodyHigh = Math.Max(o, c);

            // ---------- Volume POC in wick ----------
            if (fp.PocPrice < bodyLow)       fp.PocInWick = +1;
            else if (fp.PocPrice > bodyHigh) fp.PocInWick = -1;

            // ---------- Delta POC (row with max |delta|) in wick ----------
            var deltaRows = Row(GetDeltaForPrice, idx);
            if (deltaRows != null && deltaRows.Count > 0)
            {
                var dPocPair = deltaRows.Aggregate((a, b) => Math.Abs(a.Value) > Math.Abs(b.Value) ? a : b);
                fp.DeltaPocPrice = dPocPair.Key;
                if (fp.DeltaPocPrice < bodyLow)       fp.DeltaPocInWick = +1;
                else if (fp.DeltaPocPrice > bodyHigh) fp.DeltaPocInWick = -1;
            }

            // ---------- Extreme POC / Extreme VA / VA+POC combo ----------
            bool vaAtLowExtreme   = (valIdx == 0);
            bool vaAtHighExtreme  = (vahIdx == totalBins - 1);
            bool pocAtLowExtreme  = (pocIdx == 0);
            bool pocAtHighExtreme = (pocIdx == totalBins - 1);

            if (pocAtLowExtreme)       fp.ExtremePoc = +1;
            else if (pocAtHighExtreme) fp.ExtremePoc = -1;

            if (vaAtLowExtreme)       fp.ExtremeVa = +1;
            else if (vaAtHighExtreme) fp.ExtremeVa = -1;

            if (vaAtLowExtreme && pocAtLowExtreme)        fp.VaPocExtreme = +1;
            else if (vaAtHighExtreme && pocAtHighExtreme) fp.VaPocExtreme = -1;

            // ---------- Wick Delta Absorption ----------
            {
                double upperWickDelta = 0, lowerWickDelta = 0;
                foreach (var k in asc)
                {
                    double askVol = Read(GetAskVolumeForPrice, idx, k);
                    double bidVol = Read(GetBidVolumeForPrice, idx, k);
                    double levelDelta = askVol - bidVol;

                    if (k > bodyHigh)      upperWickDelta += levelDelta;
                    else if (k < bodyLow)  lowerWickDelta += levelDelta;
                }

                double absBarDelta = Math.Abs(fp.BarDelta);
                double requiredPercentage = WickDeltaPercentage / 100.0;

                if (upperWickDelta >= WickDeltaThreshold && upperWickDelta >= absBarDelta * requiredPercentage)
                    fp.Absorption = -1;
                else if (lowerWickDelta <= -WickDeltaThreshold && Math.Abs(lowerWickDelta) >= absBarDelta * requiredPercentage)
                    fp.Absorption = 1;
            }

            // ---------- Stopping Volume ----------
            if (totalBins >= 2 && idx >= StoppingVolumeLookback)
            {
                double currLowBid  = Read(GetBidVolumeForPrice, idx, asc[0])
                                   + Read(GetBidVolumeForPrice, idx, asc[1]);
                double currHighAsk = Read(GetAskVolumeForPrice, idx, asc[totalBins - 1])
                                   + Read(GetAskVolumeForPrice, idx, asc[totalBins - 2]);

                double maxPrevLowBid = 0, maxPrevHighAsk = 0;
                for (int i = idx - StoppingVolumeLookback; i < idx; i++)
                {
                    var prevRows = Row(GetTotalVolumeForPrice, i);
                    if (prevRows == null || prevRows.Count < 2) continue;
                    var prevAsc = prevRows.OrderBy(k => k.Key).Select(k => k.Key).ToList();
                    double pLow1  = Read(GetBidVolumeForPrice, i, prevAsc[0]);
                    double pLow2  = Read(GetBidVolumeForPrice, i, prevAsc[1]);
                    double pHigh1 = Read(GetAskVolumeForPrice, i, prevAsc[prevAsc.Count - 1]);
                    double pHigh2 = Read(GetAskVolumeForPrice, i, prevAsc[prevAsc.Count - 2]);
                    maxPrevLowBid  = Math.Max(maxPrevLowBid,  pLow1  + pLow2);
                    maxPrevHighAsk = Math.Max(maxPrevHighAsk, pHigh1 + pHigh2);
                }

                bool isGreen = c >= o;
                if (isGreen && pocBarFromLow <= 1 && currLowBid > maxPrevLowBid)        fp.StopVol =  1;
                else if (!isGreen && pocBarFromHigh <= 1 && currHighAsk > maxPrevHighAsk) fp.StopVol = -1;
            }

            // ---------- Delta Flip ----------
            if (GetDeltaForBar.ContainsKey(idx - 1) && GetDeltaForBar.ContainsKey(idx))
            {
                double bar1Delta = GetDeltaForBar[idx - 1];
                double bar2Delta = GetDeltaForBar[idx];
                double thresh    = DeltaFlipThreshold;

                bool bullishFlip = (bar1Delta < -thresh) && (bar2Delta >  thresh);
                bool bearishFlip = (bar1Delta >  thresh) && (bar2Delta < -thresh);
                fp.DeltaFlip = bullishFlip ? 1 : bearishFlip ? -1 : 0;
            }

            // ---------- Exhaustion ----------
            {
                double lowestBin  = asc[0];
                double highestBin = asc[totalBins - 1];
                if (c < o) // red
                {
                    double askVolAtHigh = Read(GetAskVolumeForPrice, idx, highestBin);
                    fp.Exhaustion = (askVolAtHigh < ExhaustionThreshold) ? -1 : 0;
                }
                else // green
                {
                    double bidVolAtLow = Read(GetBidVolumeForPrice, idx, lowestBin);
                    fp.Exhaustion = (bidVolAtLow < ExhaustionThreshold) ? 1 : 0;
                }
            }

            // ---------- Relative Delta Wick ----------
            // Exact port of AlightenRelativeDeltaV0001.GetLiveSignalByBar: own bin grid
            // (RelativeDeltaAggregationInterval, session-open origin), Globex-trading-day
            // anchored delta profile, int-truncated row deltas.
            var rdRows = Row(GetRdDeltaForPrice, idx);
            if (EnableRelativeDelta && rdRows != null && rdRows.Count > 0 && _rdBinInterval > 0)
            {
                int anchorStart = GetRdAnchorStartBar(idx);

                var anchoredDeltaMap = new Dictionary<double, double>();
                for (int i = anchorStart; i <= idx; i++)
                {
                    var dMap = Row(GetRdDeltaForPrice, i);
                    if (dMap == null) continue;
                    foreach (var kvp in dMap)
                    {
                        if (!anchoredDeltaMap.ContainsKey(kvp.Key))
                            anchoredDeltaMap[kvp.Key] = kvp.Value;
                        else
                            anchoredDeltaMap[kvp.Key] += kvp.Value;
                    }
                }

                var sortedDesc = rdRows.OrderByDescending(kv => kv.Key).ToList();
                var sortedAsc  = rdRows.OrderBy(kv => kv.Key).ToList();

                bool shortSignal = false;
                bool longSignal  = false;

                // Bearish check (top rows)
                for (int i = 0; i < Math.Min(RelativeDeltaMaxPosition, sortedDesc.Count); i++)
                {
                    double price = sortedDesc[i].Key;
                    int currentBarDelta = (int)sortedDesc[i].Value;
                    double previousAnchoredDelta = 0;
                    if (anchoredDeltaMap.TryGetValue(price, out double totalAnchoredDelta))
                        previousAnchoredDelta = totalAnchoredDelta - currentBarDelta;

                    if (previousAnchoredDelta != 0)
                    {
                        double relativePct = (currentBarDelta / Math.Abs(previousAnchoredDelta)) * 100;
                        if (relativePct > 0 && relativePct >= RelativeDeltaMinimumPercentage)
                        {
                            int distLevels = (int)Math.Round((price - bodyHigh) / _rdBinInterval);
                            if (distLevels >= RelativeDeltaMinimumWickLevels)
                                shortSignal = true;
                        }
                    }
                }

                // Bullish check (bottom rows)
                for (int i = 0; i < Math.Min(RelativeDeltaMaxPosition, sortedAsc.Count); i++)
                {
                    double price = sortedAsc[i].Key;
                    int currentBarDelta = (int)sortedAsc[i].Value;
                    double previousAnchoredDelta = 0;
                    if (anchoredDeltaMap.TryGetValue(price, out double totalAnchoredDelta))
                        previousAnchoredDelta = totalAnchoredDelta - currentBarDelta;

                    if (previousAnchoredDelta != 0)
                    {
                        double relativePct = (currentBarDelta / Math.Abs(previousAnchoredDelta)) * 100;
                        if (relativePct < 0 && Math.Abs(relativePct) >= RelativeDeltaMinimumPercentage)
                        {
                            int distLevels = (int)Math.Round((bodyLow - (price + _rdBinInterval)) / _rdBinInterval);
                            if (distLevels >= RelativeDeltaMinimumWickLevels)
                                longSignal = true;
                        }
                    }
                }

                if (shortSignal && longSignal) fp.RelativeDelta = 3;
                else if (shortSignal)          fp.RelativeDelta = -1;
                else if (longSignal)           fp.RelativeDelta = 1;

                if (EnableDebugPrints)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append($" | rd: bins={rdRows.Count} binSz={_rdBinInterval} anchorBars={idx - anchorStart + 1} body=[{bodyLow},{bodyHigh}]");
                    sb.Append(" bottom:");
                    for (int i = 0; i < Math.Min(RelativeDeltaMaxPosition, sortedAsc.Count); i++)
                    {
                        double pr = sortedAsc[i].Key;
                        double anch; anchoredDeltaMap.TryGetValue(pr, out anch);
                        sb.Append($" [{pr} d={(int)sortedAsc[i].Value} prevAnch={(int)(anch - (int)sortedAsc[i].Value)} dist={(int)Math.Round((bodyLow - (pr + _rdBinInterval)) / _rdBinInterval)}]");
                    }
                    sb.Append(" top:");
                    for (int i = 0; i < Math.Min(RelativeDeltaMaxPosition, sortedDesc.Count); i++)
                    {
                        double pr = sortedDesc[i].Key;
                        double anch; anchoredDeltaMap.TryGetValue(pr, out anch);
                        sb.Append($" [{pr} d={(int)sortedDesc[i].Value} prevAnch={(int)(anch - (int)sortedDesc[i].Value)} dist={(int)Math.Round((pr - bodyHigh) / _rdBinInterval)}]");
                    }
                    fp.RdDebug = sb.ToString();
                }
            }

            if (EnableDebugPrints && fp.RdDebug == null)
            {
                if (!EnableRelativeDelta)                     fp.RdDebug = " | rd:DISABLED";
                else if (rdRows == null || rdRows.Count == 0) fp.RdDebug = " | rd:noRows";
            }

            return fp;
        }

        // Globex-trading-day anchor (parity with AlightenRelativeDeltaV0001.GetAnchorStartBar)
        private int GetRdAnchorStartBar(int barIndex)
        {
            DateTime currentTradingDay = GetGlobexTradingDate(BarsArray[0].GetTime(barIndex));
            for (int i = barIndex; i >= 0; i--)
            {
                if (GetGlobexTradingDate(BarsArray[0].GetTime(i)) != currentTradingDay)
                    return i + 1;
            }
            return 0;
        }

        private DateTime GetGlobexTradingDate(DateTime time)
        {
            DateTime d = time.Date;
            if (time.TimeOfDay < new TimeSpan(18, 0, 0)) // 18:00 session boundary
                d = d.AddDays(-1);
            return d;
        }

        #endregion

        #region Entry Evaluation

        private BarFlow GetBarFlow(int idx)
        {
            if (_flowCache.TryGetValue(idx, out var cached))
                return cached;

            int ago = CurrentBar - idx;
            var fp = ComputeBarFlow(idx, Opens[0][ago], Highs[0][ago], Lows[0][ago], Closes[0][ago], _binInterval);
            _flowCache[idx] = fp;
            return fp;
        }

        // Touch test + confirmations for one CLOSED primary bar against one level.
        // Returns true when an entry signal was emitted.
        private bool TryEntryAtBar(int ago, TrackedLevel lvl, bool retro)
        {
            bool budgetUsed = OneSignalPerLevel && lvl.Chain != null && lvl.Chain.Signals > 0;
            if (budgetUsed && !EnableResearchLog) return false;

            double o = Opens[0][ago], h = Highs[0][ago], l = Lows[0][ago], c = Closes[0][ago];

            double tol    = TouchToleranceTicks * TickSize;
            double maxPen = MaxPenetrationTicks * TickSize;
            double P      = lvl.Price;

            // ---- Touch test (wick reached the level, in the level's direction). The research
            // log records every reach; blow-through / close-beyond / one-signal / MinConfirmations
            // act as SIGNAL gates further down so the log keeps the full touch history.
            if (lvl.IsLong  && l > P + tol) return false;   // never reached the level
            if (!lvl.IsLong && h < P - tol) return false;

            bool blown        = lvl.IsLong ? l < P - maxPen : h > P + maxPen;
            bool closedBeyond = lvl.IsLong ? c > P : c < P;

            var fp = GetBarFlow(CurrentBar - ago);
            int dir = lvl.IsLong ? 1 : -1;

            // ---- Confirmations ----
            int count = 0;
            string codes = "";
            bool fSweep = false, fPoc = false, fDPoc = false, fAbs = false, fStop = false,
                 fFlip = false, fExh = false, fXPoc = false, fVaPoc = false, fRel = false;

            if (EnableSweepReclaim)
            {
                double pen = SweepMinPenetrationTicks * TickSize;
                fSweep = lvl.IsLong
                    ? (l <= P - pen && c > P)
                    : (h >= P + pen && c < P);
                if (fSweep) { count++; codes += "S"; }
            }
            if (EnablePocInWick && fp.HasRows)
            {
                fPoc = fp.PocInWick == dir
                    && (PocNearLevelTicks <= 0 || Math.Abs(fp.PocPrice - P) <= PocNearLevelTicks * TickSize);
                if (fPoc) { count++; codes += "P"; }
            }
            if (EnableDeltaPocInWick && fp.HasRows)
            {
                fDPoc = fp.DeltaPocInWick == dir
                    && (PocNearLevelTicks <= 0 || Math.Abs(fp.DeltaPocPrice - P) <= PocNearLevelTicks * TickSize);
                if (fDPoc) { count++; codes += "D"; }
            }
            if (EnableAbsorption && fp.HasRows)
            {
                fAbs = fp.Absorption == dir;
                if (fAbs) { count++; codes += "A"; }
            }
            if (EnableStoppingVolume && fp.HasRows)
            {
                fStop = fp.StopVol == dir;
                if (fStop) { count++; codes += "V"; }
            }
            if (EnableDeltaFlip && fp.HasRows)
            {
                fFlip = fp.DeltaFlip == dir;
                if (fFlip) { count++; codes += "F"; }
            }
            if (EnableExhaustion && fp.HasRows)
            {
                fExh = fp.Exhaustion == dir;
                if (fExh) { count++; codes += "E"; }
            }
            if (EnableExtremePoc && fp.HasRows)
            {
                fXPoc = fp.ExtremePoc == dir;
                if (fXPoc) { count++; codes += "X"; }
            }
            if (EnableVaPocExtreme && fp.HasRows)
            {
                fVaPoc = fp.VaPocExtreme == dir;
                if (fVaPoc) { count++; codes += "C"; }
            }
            if (EnableRelativeDelta && fp.HasRows)
            {
                fRel = fp.RelativeDelta == dir || fp.RelativeDelta == 3;
                if (fRel) { count++; codes += "R"; }
            }

            if (EnableDebugPrints)
                Dbg($"[MirrorEntry] {Times[0][ago]:MM/dd HH:mm:ss} TOUCH {lvl.PatternType}{_tfLabels[lvl.TfIndex]} {(lvl.IsLong ? "L" : "S")}@{P}: " +
                    $"S={(fSweep ? 1 : 0)} P={(fPoc ? 1 : 0)} D={(fDPoc ? 1 : 0)} A={(fAbs ? 1 : 0)} V={(fStop ? 1 : 0)} F={(fFlip ? 1 : 0)} " +
                    $"E={(fExh ? 1 : 0)} X={(fXPoc ? 1 : 0)} C={(fVaPoc ? 1 : 0)} R={(fRel ? 1 : 0)} (rdSig={fp.RelativeDelta}, hasRows={fp.HasRows}) " +
                    $"count={count}/{MinConfirmations} retro={retro}{fp.RdDebug}");

            int mask = 0;
            if (fSweep) mask |= 1 << 0;
            if (fPoc)   mask |= 1 << 1;
            if (fDPoc)  mask |= 1 << 2;
            if (fAbs)   mask |= 1 << 3;
            if (fStop)  mask |= 1 << 4;
            if (fFlip)  mask |= 1 << 5;
            if (fExh)   mask |= 1 << 6;
            if (fXPoc)  mask |= 1 << 7;
            if (fVaPoc) mask |= 1 << 8;
            if (fRel)   mask |= 1 << 9;

            int setupBarIdx = CurrentBar - ago;

            // ---- Research row: every reach is logged, before any signal gate ----
            ResearchEvent rev = null;
            if (EnableResearchLog)
                rev = LogTouchEvent(setupBarIdx, ago, lvl, "T", o, h, l, c, blown, closedBeyond, count, mask, codes);

            // ---- Signal gates ----
            if (blown) return false;                                    // level blown through
            if (RequireCloseBeyondLevel && !closedBeyond) return false; // no close back beyond level
            if (count < MinConfirmations) return false;
            if (budgetUsed) return false;                               // one-signal budget spent

            // Never emit the same pattern/TF/direction twice on one bar — the HTF-close
            // backfill re-walks bars the live path may already have signed (drawings
            // share a tag and the plot stamps would double up).
            for (int i = 0; i < _signals.Count; i++)
            {
                var s = _signals[i];
                if (!s.Retracted && !s.IsLtf && s.SetupBarIdx == setupBarIdx && s.IsLong == lvl.IsLong
                    && s.Pattern == lvl.PatternType && s.TfIndex == lvl.TfIndex)
                    return false;
            }

            // ---- Signal ----
            if (lvl.Chain != null) lvl.Chain.Signals++;
            if (rev != null) { rev.SignalEmitted = true; rev.Retracted = false; }

            Values[1][ago] = dir * count;
            for (int bit = 0; bit < 10; bit++)
                if ((mask & (1 << bit)) != 0) Values[2 + bit][ago] = dir;

            string label = codes + " " + lvl.PatternType + _tfLabels[lvl.TfIndex];
            double arrowOff = ArrowOffsetTicks * TickSize;
            double codeOff  = (2 * ArrowOffsetTicks + 3) * TickSize;

            // Stack code labels when multiple levels sign the same bar/direction
            int stack = 0;
            for (int i = 0; i < _signals.Count; i++)
                if (!_signals[i].Retracted && _signals[i].SetupBarIdx == setupBarIdx && _signals[i].IsLong == lvl.IsLong)
                    stack++;
            codeOff += stack * 3 * TickSize;

            var sig = new EmittedSignal
            {
                SetupBarIdx = setupBarIdx,
                SetupTime   = Times[0][ago],
                IsLong      = lvl.IsLong,
                Pattern     = lvl.PatternType,
                TfIndex     = lvl.TfIndex,
                Label       = label,
                CodeY       = lvl.IsLong ? l - codeOff : h + codeOff,
                Count       = count,
                ConfMask    = mask,
                Level       = lvl,
                Research    = rev,
                // Emitted off a still-forming HTF bar: provisional until that bar closes
                Provisional = State != State.Historical && lvl.IsLive
            };
            _signals.Add(sig);

            if (TwoBarTrigger)
            {
                int setupBar = CurrentBar - ago;
                double trig  = lvl.IsLong ? h + TickSize : l - TickSize;

                if (retro)
                {
                    // Historical: scan the already-closed bars after the setup for the trigger.
                    bool hit = false;
                    int lastScan = Math.Min(setupBar + TriggerExpiryBars, CurrentBar);
                    for (int k = setupBar + 1; k <= lastScan; k++)
                    {
                        int agoK = CurrentBar - k;
                        if (lvl.IsLong ? Highs[0][agoK] >= trig : Lows[0][agoK] <= trig)
                        {
                            Values[0][agoK] = dir;
                            Values[1][agoK] = dir * count;
                            sig.HasArrow  = true;
                            sig.ArrowTime = Times[0][agoK];
                            sig.ArrowY    = lvl.IsLong ? Lows[0][agoK] - arrowOff : Highs[0][agoK] + arrowOff;
                            hit = true;
                            break;
                        }
                    }
                    // Expiry window extends past the processed bars -> hand off to the live path
                    if (!hit && setupBar + TriggerExpiryBars > CurrentBar)
                        _pending.Add(new PendingEntry { IsLong = lvl.IsLong, TriggerPrice = trig, SetupBar = setupBar, ExpiryBar = setupBar + TriggerExpiryBars, Confirmations = count, Signal = sig });
                }
                else
                {
                    _pending.Add(new PendingEntry { IsLong = lvl.IsLong, TriggerPrice = trig, SetupBar = setupBar, ExpiryBar = setupBar + TriggerExpiryBars, Confirmations = count, Signal = sig });
                }
            }
            else
            {
                Values[0][ago] = dir;
                sig.HasArrow  = true;
                sig.ArrowTime = sig.SetupTime;
                sig.ArrowY    = lvl.IsLong ? l - arrowOff : h + arrowOff;
            }

            DrawSignal(sig);
            return true;
        }

        // Realtime path: evaluate the just-closed primary bar ([1]) against all currently active levels.
        private void EvaluateEntryOnClosedBar()
        {
            int idx = CurrentBar - 1;
            int ago = 1;
            DateTime barTime = Times[0][ago];

            for (int p = 0; p < NUM_PAT; p++)
            {
                if (!IsPatternEnabled(p)) continue;

                for (int t = 0; t < NUM_TF; t++)
                {
                    if (!IsTfEnabled(t)) continue;
                    var tDict = tracked[p][t];
                    if (tDict == null || tDict.Count == 0) continue;

                    foreach (var lvl in tDict.Values)
                    {
                        if (lvl.Removed) continue;
                        // NOTE: retro-evaluated levels are NOT skipped here — a level whose
                        // window is still active (e.g. a forming Daily level at the
                        // historical->realtime seam) keeps producing live signals; the
                        // window check below excludes dead historical levels, and the
                        // chain accounting prevents duplicates.

                        // Skip reasons, checked in order. Forming-bar levels signal
                        // provisionally at the chart-bar close (retracted at the HTF close
                        // if the level dies). Stale = live segment whose price repainted
                        // away THIS bar — the level isn't on the chart right now, so no
                        // signal; the HTF-close backfill re-awards it if the level survives.
                        string skip = null;
                        if (barTime < lvl.HtfStartTime || barTime > lvl.HtfEndTime) skip = "outside window";
                        else if (lvl.IsLive && lvl.LastUpdatedBip0Bar < CurrentBar) skip = "stale live level";
                        // With the research log on, spent-budget levels still get their touches
                        // logged — TryEntryAtBar applies the budget gate to the signal itself.
                        else if (!EnableResearchLog && OneSignalPerLevel && lvl.Chain != null && lvl.Chain.Signals > 0) skip = "one-signal already used";

                        if (skip != null)
                        {
                            if (EnableDebugPrints)
                            {
                                double tol = TouchToleranceTicks * TickSize;
                                bool near = lvl.IsLong ? Lows[0][ago] <= lvl.Price + tol : Highs[0][ago] >= lvl.Price - tol;
                                if (near)
                                    Dbg($"[MirrorEntry] {barTime:MM/dd HH:mm:ss} SKIP {lvl.PatternType}{_tfLabels[lvl.TfIndex]} {(lvl.IsLong ? "L" : "S")}@{lvl.Price}: {skip}");
                            }
                            continue;
                        }

                        TryEntryAtBar(ago, lvl, false);
                    }
                }
            }
        }

        // Historical path: an HTF series only ever shows CLOSED bars while processing history,
        // so a level's window is complete before any primary bar inside it can be evaluated
        // forward. When a level appears during State.Historical, walk the primary bars inside
        // its window (oldest first) and evaluate touches retroactively. Uses the level's final
        // window price — live evaluation uses the live level, so an intrabar-repainting level
        // can differ slightly between historical and realtime.
        private void RetroEvaluateLevel(TrackedLevel lvl)
        {
            RetroEvaluateLtf(lvl); // LTF arm/fire walk has its own once-per-level flag

            if (lvl.RetroEvaluated) return;
            lvl.RetroEvaluated = true;

            // Historical: bar [0] is complete (events fire at bar close). Realtime
            // catch-up: bar [0] is still forming — the live path evaluates it at its close.
            int firstB = State == State.Historical ? 0 : 1;

            List<int> agos = null;
            int maxBack = CurrentBars[0];
            for (int b = firstB; b < maxBack; b++)
            {
                DateTime bt = Times[0][b];
                if (bt < lvl.HtfStartTime) break;
                if (bt <= lvl.HtfEndTime)
                    (agos ?? (agos = new List<int>())).Add(b);
            }
            if (agos == null) return;

            for (int i = agos.Count - 1; i >= 0; i--) // oldest first
            {
                // With the research log on, keep walking after the first signal — later
                // touches emit no signal (budget gate) but must still be logged.
                if (TryEntryAtBar(agos[i], lvl, true) && OneSignalPerLevel && !EnableResearchLog)
                    break;
            }
        }

        private void CheckPendingTriggers()
        {
            if (_pending == null || _pending.Count == 0) return;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pe = _pending[i];

                if (CurrentBar > pe.ExpiryBar)
                {
                    _pending.RemoveAt(i);
                    continue;
                }
                if (CurrentBar <= pe.SetupBar)
                    continue;

                bool hit = pe.IsLong ? High[0] >= pe.TriggerPrice : Low[0] <= pe.TriggerPrice;
                if (!hit) continue;

                Values[0][0] = pe.IsLong ? 1 : -1;
                Values[1][0] = (pe.IsLong ? 1 : -1) * pe.Confirmations;
                if (pe.Signal != null)
                {
                    pe.Signal.HasArrow  = true;
                    pe.Signal.ArrowTime = Time[0];
                    pe.Signal.ArrowY    = pe.IsLong ? Low[0] - ArrowOffsetTicks * TickSize : High[0] + ArrowOffsetTicks * TickSize;
                    DrawSignal(pe.Signal);
                }
                _pending.RemoveAt(i);
            }
        }

        // Judge TF t's provisional signals after its HTF bar closed at closedHtfTime.
        // A surviving level was flipped to IsLive=false by SyncLevels ([1] sync of the
        // closed bar matches the forming segment's tag); a segment still marked live
        // means the pattern source dropped the level before/at the close -> retract.
        private void ValidateProvisionalSignals(int t, DateTime closedHtfTime)
        {
            for (int i = 0; i < _signals.Count; i++)
            {
                var sig = _signals[i];
                if (sig.Retracted || !sig.Provisional || sig.TfIndex != t) continue;

                var lvl = sig.Level;
                if (lvl == null) { sig.Provisional = false; continue; }

                // Signal emitted off the NEW forming bar during the grace period —
                // its own rollover hasn't happened yet.
                if (lvl.IsLive && lvl.HtfEndTime > closedHtfTime) continue;

                if (!lvl.IsLive)
                {
                    sig.Provisional = false;
                    if (sig.Research != null) sig.Research.Provisional = false;
                    if (EnableDebugPrints)
                        Dbg($"[MirrorEntry] CONFIRM {sig.Pattern}{_tfLabels[t]} {(sig.IsLong ? "L" : "S")}@{lvl.Price} setup {sig.SetupTime:MM/dd HH:mm:ss} — level survived HTF close");
                }
                else
                {
                    RetractSignal(sig);
                }
            }
        }

        // Remove a provisional signal whose forming-bar level did not survive the HTF
        // close: drawings off, one-signal budget refunded, pending trigger dropped,
        // plot stamps of the affected bars rebuilt from the surviving signals.
        private void RetractSignal(EmittedSignal sig)
        {
            sig.Retracted = true;

            if (sig.Research != null) sig.Research.Retracted = true;

            if (!sig.IsLtf && sig.Level != null && sig.Level.Chain != null && sig.Level.Chain.Signals > 0)
                sig.Level.Chain.Signals--;

            if (_pending != null)
                for (int i = _pending.Count - 1; i >= 0; i--)
                    if (_pending[i].Signal == sig) _pending.RemoveAt(i);

            string tag = SignalTag(sig);
            RemoveDrawObject(tag);
            RemoveDrawObject(tag + "_c");

            RestampBarPlots(sig.SetupBarIdx);
            if (sig.HasArrow)
            {
                int arrowBar = BarsArray[0].GetBar(sig.ArrowTime);
                if (arrowBar >= 0 && arrowBar != sig.SetupBarIdx)
                    RestampBarPlots(arrowBar);
            }

            if (EnableDebugPrints)
                Dbg($"[MirrorEntry] RETRACT {sig.Pattern}{_tfLabels[sig.TfIndex]} {(sig.IsLong ? "L" : "S")} setup {sig.SetupTime:MM/dd HH:mm:ss} — level did not survive HTF close");
        }

        // Rebuild every plot stamp on one chart bar from the surviving signals
        // (a retracted signal may share the bar with signals that must keep theirs).
        private void RestampBarPlots(int barIdx)
        {
            int ago = CurrentBar - barIdx;
            if (ago < 0 || ago > CurrentBar) return;

            for (int j = 0; j < Values.Length; j++)
                Values[j][ago] = 0;

            for (int i = 0; i < _signals.Count; i++)
            {
                var s = _signals[i];
                if (s.Retracted) continue;
                int dir = s.IsLong ? 1 : -1;

                if (s.IsLtf)
                {
                    if (s.SetupBarIdx == barIdx) Values[12][ago] = dir;
                    continue;
                }

                if (s.SetupBarIdx == barIdx)
                {
                    Values[1][ago] = dir * s.Count;
                    for (int bit = 0; bit < 10; bit++)
                        if ((s.ConfMask & (1 << bit)) != 0) Values[2 + bit][ago] = dir;
                }
                if (s.HasArrow && BarsArray[0].GetBar(s.ArrowTime) == barIdx)
                {
                    Values[0][ago] = dir;
                    if (s.SetupBarIdx != barIdx) Values[1][ago] = dir * s.Count; // two-bar trigger stamp
                }
            }
        }

        // Tag includes pattern+TF so same-bar signals from different levels never share
        // drawings (a hidden signal must not remove a visible one's arrow). LTF signals
        // use their own prefix so they never collide with a confirmation entry's arrow.
        private string SignalTag(EmittedSignal sig)
        {
            return (sig.IsLtf ? "MREL_" : "MRE_") + sig.SetupBarIdx + "_" + sig.Pattern + sig.TfIndex + (sig.IsLong ? "_L" : "_S");
        }

        // Draw (or remove) one recorded signal according to the pattern/TF visibility toggles.
        private void DrawSignal(EmittedSignal sig)
        {
            string tag = SignalTag(sig);

            if (!IsPatternTfSignalEnabled(sig.Pattern, sig.TfIndex))
            {
                RemoveDrawObject(tag);
                RemoveDrawObject(tag + "_c");
                return;
            }

            if (sig.HasArrow)
            {
                if (sig.IsLtf)
                {
                    if (sig.IsLong)
                        Draw.TriangleUp(this, tag, false, sig.ArrowTime, sig.ArrowY, LongColor);
                    else
                        Draw.TriangleDown(this, tag, false, sig.ArrowTime, sig.ArrowY, ShortColor);
                }
                else if (sig.IsLong)
                    Draw.ArrowUp(this, tag, false, sig.ArrowTime, sig.ArrowY, LongColor);
                else
                    Draw.ArrowDown(this, tag, false, sig.ArrowTime, sig.ArrowY, ShortColor);
            }

            if (ShowSignalCodes && !string.IsNullOrEmpty(sig.Label))
                Draw.Text(this, tag + "_c", false, sig.Label, sig.SetupTime, sig.CodeY, 0,
                    sig.IsLong ? LongColor : ShortColor,
                    new SimpleFont("Arial", CodeFontSize), TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
            else
                RemoveDrawObject(tag + "_c");
        }

        private bool IsPatternTfSignalEnabled(string pattern, int t)
        {
            if (pattern == "A") {
                switch (t) { case 0: return SignalPatternATF1_Daily; case 1: return SignalPatternATF2_240m; case 2: return SignalPatternATF3_60m; case 3: return SignalPatternATF4_30m; case 4: return SignalPatternATF5_15m; case 5: return SignalPatternATF6_10m; case 6: return SignalPatternATF7_5m; }
            } else if (pattern == "B") {
                switch (t) { case 0: return SignalPatternBTF1_Daily; case 1: return SignalPatternBTF2_240m; case 2: return SignalPatternBTF3_60m; case 3: return SignalPatternBTF4_30m; case 4: return SignalPatternBTF5_15m; case 5: return SignalPatternBTF6_10m; case 6: return SignalPatternBTF7_5m; }
            } else if (pattern == "G") {
                switch (t) { case 0: return SignalPatternGTF1_Daily; case 1: return SignalPatternGTF2_240m; case 2: return SignalPatternGTF3_60m; case 3: return SignalPatternGTF4_30m; case 4: return SignalPatternGTF5_15m; case 5: return SignalPatternGTF6_10m; case 6: return SignalPatternGTF7_5m; }
            } else if (pattern == "H") {
                switch (t) { case 0: return SignalPatternHTF1_Daily; case 1: return SignalPatternHTF2_240m; case 2: return SignalPatternHTF3_60m; case 3: return SignalPatternHTF4_30m; case 4: return SignalPatternHTF5_15m; case 5: return SignalPatternHTF6_10m; case 6: return SignalPatternHTF7_5m; }
            } else if (pattern == "F") {
                switch (t) { case 0: return SignalPatternFTF1_Daily; case 1: return SignalPatternFTF2_240m; case 2: return SignalPatternFTF3_60m; case 3: return SignalPatternFTF4_30m; case 4: return SignalPatternFTF5_15m; case 5: return SignalPatternFTF6_10m; case 6: return SignalPatternFTF7_5m; }
            }
            return false;
        }

        #endregion

        #region Research Logging (outcome engine per AlightenMirrorV0031; regime derived offline)

        // Record one research row for a level touch (SigType "T") or LTF trigger firing ("L").
        // Returns the existing row when this (bar, type, pattern, TF, dir) was already logged —
        // the live path and the HTF-close backfill re-walk the same bars. Retro-discovered
        // touches immediately catch up their forward outcomes across the already-closed bars.
        // Uses CurrentBars[0] throughout: may be called from the LTF BIP, where CurrentBar
        // belongs to the LTF series.
        private ResearchEvent LogTouchEvent(int barIdx, int ago, TrackedLevel lvl, string sigType,
            double o, double h, double l, double c, bool blown, bool closedBeyond,
            int confCount, int confMask, string codes)
        {
            string key = barIdx + "_" + sigType + "_" + lvl.PatternType + lvl.TfIndex + (lvl.IsLong ? "L" : "S");
            ResearchEvent existing;
            if (_researchSeen.TryGetValue(key, out existing)) return existing;

            double P = lvl.Price;
            var ev = new ResearchEvent
            {
                Id                = ++_nextResearchId,
                SetupBarIdx       = barIdx,
                Time              = Times[0][ago],
                SigType           = sigType,
                Pattern           = lvl.PatternType,
                TfIndex           = lvl.TfIndex,
                IsLong            = lvl.IsLong,
                LevelPrice        = P,
                RefPrice          = c,
                BarOpen           = o,
                BarHigh           = h,
                BarLow            = l,
                BarClose          = c,
                PenetrationTicks  = (lvl.IsLong ? P - l : h - P) / TickSize,
                CloseVsLevelTicks = (c - P) / TickSize,
                ClosedBeyond      = closedBeyond,
                BlownThrough      = blown,
                ConfCount         = confCount,
                ConfMask          = confMask,
                Codes             = codes,
                Provisional       = State != State.Historical && lvl.IsLive,
                LastOutcomeBarIdx = barIdx
            };
            _researchSeen[key] = ev;

            // Retro catch-up: fold every already-closed bar after the touch into the outcomes.
            int newestClosed = State == State.Historical ? CurrentBars[0] : CurrentBars[0] - 1;
            for (int b = barIdx + 1; b <= newestClosed; b++)
                FoldOutcomeBar(ev, b);

            if (ev.Done) _researchDone.Add(ev);
            else         _researchOpen.Add(ev);
            return ev;
        }

        // Fold one closed primary bar into an event's forward outcomes. Max-based, so
        // overlapping calls from the retro catch-up and the per-bar update are harmless;
        // LastOutcomeBarIdx keeps the fold strictly forward.
        private void FoldOutcomeBar(ResearchEvent ev, int barIdx)
        {
            if (barIdx <= ev.LastOutcomeBarIdx || ev.Done) return;
            int ago = CurrentBars[0] - barIdx;
            if (ago < 0 || ago > CurrentBars[0]) return;
            ev.LastOutcomeBarIdx = barIdx;

            double hi = Highs[0][ago];
            double lo = Lows[0][ago];
            double elapsedMin = (Times[0][ago] - ev.Time).TotalMinutes;
            if (elapsedMin <= 0) return;

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
                ev.Done = true;
        }

        // Per closed primary bar ([1] at the first tick of the new bar): advance all open events.
        private void UpdateResearchEvents()
        {
            if (_researchOpen == null || _researchOpen.Count == 0) return;
            int barIdx = CurrentBars[0] - 1;
            if (barIdx < 0) return;

            for (int i = _researchOpen.Count - 1; i >= 0; i--)
            {
                var ev = _researchOpen[i];
                FoldOutcomeBar(ev, barIdx);
                if (ev.Done)
                {
                    _researchDone.Add(ev);
                    _researchOpen.RemoveAt(i);
                }
            }
        }

        private void ExportResearchCsv()
        {
            try
            {
                if (_researchDone == null || (_researchDone.Count == 0 && _researchOpen.Count == 0))
                {
                    NinjaTrader.Code.Output.Process("[MirrorEntryV0003] No research events to export.", PrintTo.OutputTab1);
                    return;
                }

                string folder = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "Export");
                if (!System.IO.Directory.Exists(folder))
                    System.IO.Directory.CreateDirectory(folder);

                string path = System.IO.Path.Combine(folder, $"MirrorEntryResearch_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                var all = new List<ResearchEvent>(_researchDone.Count + _researchOpen.Count);
                all.AddRange(_researchDone);
                all.AddRange(_researchOpen);
                // Retro discovery logs events out of chronological order — the offline regime
                // reconstruction needs them time-sorted.
                all.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Id.CompareTo(b.Id));

                using (var w = new System.IO.StreamWriter(path, false))
                {
                    w.WriteLine("EventId,Time,SigType,Pattern,TF,Dir,LevelPrice,RefPrice,BarOpen,BarHigh,BarLow,BarClose,"
                              + "PenTicks,CloseVsLevelTicks,ClosedBeyond,BlownThrough,ConfCount,Codes,S,P,D,A,V,F,E,X,C,R,"
                              + "SignalEmitted,Provisional,Retracted,TargetTicks,TargetHit,TargetMinutes,MaeBeforeTargetTicks,"
                              + "Mfe15,Mae15,Mfe30,Mae30,Mfe60,Mae60,Mfe120,Mae120,Complete");
                    foreach (var ev in all)
                        WriteResearchRow(w, ev);
                }

                NinjaTrader.Code.Output.Process($"[MirrorEntryV0003] Exported {all.Count} research events to: {path}", PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                Print("[MirrorEntryV0003] Research export error: " + ex.Message);
            }
        }

        private void WriteResearchRow(System.IO.StreamWriter w, ResearchEvent ev)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string confBits = "";
            for (int bit = 0; bit < 10; bit++)
                confBits += ((ev.ConfMask & (1 << bit)) != 0 ? ",1" : ",0");

            w.WriteLine(string.Format(ci,
                "{0},{1:yyyy-MM-dd HH:mm:ss},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12:F1},{13:F1},{14},{15},{16},{17}{18},{19},{20},{21},{22},{23},{24:F1},{25:F1},{26:F1},{27:F1},{28:F1},{29:F1},{30:F1},{31:F1},{32:F1},{33:F1},{34}",
                ev.Id, ev.Time, ev.SigType, ev.Pattern, _tfLabels[ev.TfIndex], ev.IsLong ? "L" : "S",
                ev.LevelPrice, ev.RefPrice, ev.BarOpen, ev.BarHigh, ev.BarLow, ev.BarClose,
                ev.PenetrationTicks, ev.CloseVsLevelTicks, ev.ClosedBeyond ? 1 : 0, ev.BlownThrough ? 1 : 0,
                ev.ConfCount, ev.Codes, confBits,
                ev.SignalEmitted ? 1 : 0, ev.Provisional ? 1 : 0, ev.Retracted ? 1 : 0,
                ResearchTargetTicks, ev.TargetHit ? 1 : 0, ev.TargetMinutes, ev.MaeBeforeTargetTicks,
                ev.Mfe15, ev.Mae15, ev.Mfe30, ev.Mae30, ev.Mfe60, ev.Mae60, ev.Mfe120, ev.Mae120,
                ev.Done ? 1 : 0));
        }

        #endregion

        #region LTF Trigger Engine (ported from MagicOpposingClusterFilter's LTF pair engine)

        private double LtfNormalize(double price)
        {
            return TickSize > 0 ? Instrument.MasterInstrument.RoundToTickSize(price) : price;
        }

        private long LtfTickKey(double price)
        {
            return TickSize > 0 ? (long)Math.Round(price / TickSize) : (long)Math.Round(price * 1000000.0);
        }

        private string LtfMakeNodeKey(int pivotBar, double level, bool isHigh)
        {
            return pivotBar + "_" + LtfTickKey(level) + "_" + (isHigh ? "H" : "L");
        }

        private double LtfSelectedPivotLevel(int index)
        {
            if (index < 0 || index >= _ltfPivotBars.Count) return double.NaN;
            return LtfNormalize(LtfUseWicksForPairLevels ? _ltfPivotWicks[index] : _ltfGuidePrices[index]);
        }

        // Closed LTF bar: snapshot the pre-bar level states, update pivots/nodes through the
        // bar, then evaluate the strategy's trigger definitions against the snapshots.
        private void ProcessLtfClosedBar(int ago)
        {
            int idx = CurrentBars[LTF_BIP] - ago;
            if (idx < 1 || idx <= _ltfLastProcessedBar) return;
            if (CurrentBars[LTF_BIP] < ago + 1) return;

            // Authoritative pre-bar state (a developing-pivot replacement during this bar
            // must not delete the transition event) — same design as the strategy.
            Dictionary<long, LtfPriceState> beforeStates = LtfCapturePriceStates();
            List<LtfPairBoundaryState> beforePairs =
                LtfTriggerLevelMode == AlightenLtfTriggerLevelModeV0003.OuterPairBoundary
                    ? LtfCapturePairBoundaries()
                    : null;

            double o0 = Opens[LTF_BIP][ago],     c0 = Closes[LTF_BIP][ago];
            double h0 = Highs[LTF_BIP][ago],     l0 = Lows[LTF_BIP][ago];
            double o1 = Opens[LTF_BIP][ago + 1], c1 = Closes[LTF_BIP][ago + 1];
            double h1 = Highs[LTF_BIP][ago + 1], l1 = Lows[LTF_BIP][ago + 1];

            bool prevGreen = c1 >= o1, prevRed = c1 < o1;
            bool currGreen = c0 >= o0, currRed = c0 < o0;
            bool isHigh = h0 > h1, isLow = l0 < l1;
            double bodyHigh = Math.Max(o0, c0), bodyLow = Math.Min(o0, c0);
            double breakTol = Math.Max(0, LtfGainLossToleranceTicks) * TickSize;

            _ltfHistoryCutoffBar = idx;

            if (prevGreen && currGreen && isHigh) LtfProcessPivot(idx, h0, true,  bodyHigh, bodyLow);
            if (prevRed   && currRed   && isLow)  LtfProcessPivot(idx, l0, false, bodyHigh, bodyLow);
            if (prevGreen && currRed   && isHigh) LtfProcessPivot(idx, h0, true,  bodyHigh, bodyLow);
            if (prevRed   && currGreen && isLow)  LtfProcessPivot(idx, l0, false, bodyHigh, bodyLow);
            if (prevRed   && currGreen && isHigh) LtfProcessPivot(idx, h0, true,  bodyHigh, bodyLow);
            if (prevGreen && currRed   && isLow)  LtfProcessPivot(idx, l0, false, bodyHigh, bodyLow);

            foreach (LtfLevelNode node in _ltfNodes.Values.ToList())
                LtfProcessNodeAtBar(node, idx);

            _ltfLastProcessedBar = idx;
            _ltfHistoryCutoffBar = -1;

            // ---- Trigger evaluation (against the pre-bar snapshots) ----
            bool longTrig = false, shortTrig = false;
            double longLevel = double.NaN, shortLevel = double.NaN;
            double bestLongDist = double.MaxValue, bestShortDist = double.MaxValue;

            if (LtfTriggerLevelMode == AlightenLtfTriggerLevelModeV0003.OuterPairBoundary)
            {
                foreach (LtfPairBoundaryState pair in beforePairs)
                {
                    if (pair.IsRlPair)
                    {
                        bool gainedOuterRl = LtfUseWicksForGainLoss
                            ? h0 > pair.UpperLevel + breakTol
                            : c0 > pair.UpperLevel + breakTol;
                        if (gainedOuterRl)
                        {
                            longTrig = true;
                            double d = Math.Abs(c0 - pair.UpperLevel);
                            if (d < bestLongDist) { bestLongDist = d; longLevel = pair.UpperLevel; }
                        }
                    }
                    if (pair.IsSgPair)
                    {
                        bool lostOuterSg = LtfUseWicksForGainLoss
                            ? l0 < pair.LowerLevel - breakTol
                            : c0 < pair.LowerLevel - breakTol;
                        if (lostOuterSg)
                        {
                            shortTrig = true;
                            double d = Math.Abs(c0 - pair.LowerLevel);
                            if (d < bestShortDist) { bestShortDist = d; shortLevel = pair.LowerLevel; }
                        }
                    }
                }
            }
            else
            {
                foreach (LtfPriceState state in beforeStates.Values)
                {
                    bool eligibleResistance = LtfTriggerLevelMode == AlightenLtfTriggerLevelModeV0003.AnyTestedLevel
                        ? state.HasTestedResistance
                        : state.HasResistance;
                    bool eligibleSupport = LtfTriggerLevelMode == AlightenLtfTriggerLevelModeV0003.AnyTestedLevel
                        ? state.HasTestedSupport
                        : state.HasSupport;

                    bool gained = LtfUseWicksForGainLoss
                        ? h0 > state.Level + breakTol
                        : c0 > state.Level + breakTol;
                    bool lost = LtfUseWicksForGainLoss
                        ? l0 < state.Level - breakTol
                        : c0 < state.Level - breakTol;

                    if (eligibleResistance && gained)
                    {
                        longTrig = true;
                        double d = Math.Abs(c0 - state.Level);
                        if (d < bestLongDist) { bestLongDist = d; longLevel = state.Level; }
                    }
                    if (eligibleSupport && lost)
                    {
                        shortTrig = true;
                        double d = Math.Abs(c0 - state.Level);
                        if (d < bestShortDist) { bestShortDist = d; shortLevel = state.Level; }
                    }
                }
            }

            // Both directions on one bar = conflict, no signal (strategy behavior)
            if (longTrig == shortTrig) return;

            var evt = new LtfTriggerEvent
            {
                Time   = Times[LTF_BIP][ago],
                Level  = longTrig ? longLevel : shortLevel,
                IsLong = longTrig
            };
            _ltfEvents.Add(evt);

            if (EnableDebugPrints)
                Dbg($"[MirrorEntry] {evt.Time:MM/dd HH:mm:ss} LTF {(evt.IsLong ? "RL GAINED" : "SG LOST")} @{evt.Level}");

            if (State != State.Historical)
                MatchLtfEventLive(evt);
        }

        private void LtfProcessPivot(int bar, double wickPrice, bool isHigh, double bodyHigh, double bodyLow)
        {
            double guide = LtfNormalize(isHigh ? bodyHigh : bodyLow);
            wickPrice = LtfNormalize(wickPrice);
            if (_ltfPivotBars.Count == 0)
            {
                _ltfPivotBars.Add(bar);
                _ltfPivotWicks.Add(wickPrice);
                _ltfPivotIsHigh.Add(isHigh);
                _ltfGuidePrices.Add(guide);
            }
            else
            {
                int last = _ltfPivotBars.Count - 1;
                if (_ltfPivotIsHigh[last] == isHigh)
                {
                    bool moreExtreme = (isHigh && wickPrice > _ltfPivotWicks[last])
                        || (!isHigh && wickPrice < _ltfPivotWicks[last]);
                    if (moreExtreme)
                    {
                        _ltfPivotBars[last]   = bar;
                        _ltfPivotWicks[last]  = wickPrice;
                        _ltfGuidePrices[last] = guide;
                    }
                }
                else
                {
                    _ltfPivotBars.Add(bar);
                    _ltfPivotWicks.Add(wickPrice);
                    _ltfPivotIsHigh.Add(isHigh);
                    _ltfGuidePrices.Add(guide);
                }
            }

            int max = Math.Max(20, LtfMaxStoredPivots);
            while (_ltfPivotBars.Count > max)
            {
                _ltfPivotBars.RemoveAt(0);
                _ltfPivotWicks.RemoveAt(0);
                _ltfPivotIsHigh.RemoveAt(0);
                _ltfGuidePrices.RemoveAt(0);
            }

            LtfSynchronizeNodes();
        }

        private void LtfSynchronizeNodes()
        {
            HashSet<string> used = new HashSet<string>();
            for (int i = 0; i + 1 < _ltfPivotBars.Count; i++)
            {
                double startLevel = LtfSelectedPivotLevel(i);
                double endLevel = LtfSelectedPivotLevel(i + 1);
                if (double.IsNaN(startLevel) || double.IsNaN(endLevel)) continue;
                LtfLevelNode start = LtfGetOrCreateNode(_ltfPivotBars[i], startLevel, _ltfPivotIsHigh[i]);
                LtfLevelNode end = LtfGetOrCreateNode(_ltfPivotBars[i + 1], endLevel, _ltfPivotIsHigh[i + 1]);
                used.Add(start.Key);
                used.Add(end.Key);
            }
            foreach (string key in _ltfNodes.Keys.Where(k => !used.Contains(k)).ToList())
                _ltfNodes.Remove(key);
        }

        private LtfLevelNode LtfGetOrCreateNode(int pivotBar, double level, bool isHigh)
        {
            string key = LtfMakeNodeKey(pivotBar, level, isHigh);
            LtfLevelNode node;
            if (_ltfNodes.TryGetValue(key, out node)) return node;
            node = new LtfLevelNode
            {
                Key = key,
                PivotBar = pivotBar,
                Level = LtfNormalize(level),
                IsHighPivot = isHigh,
                Side = LtfLevelSide.Unknown,
                Tested = false,
                StateChangedBar = pivotBar,
                LastProcessedBar = pivotBar
            };
            _ltfNodes[key] = node;
            LtfInitializeNodeFromHistory(node);
            return node;
        }

        private void LtfInitializeNodeFromHistory(LtfLevelNode node)
        {
            if (node == null || CurrentBars[LTF_BIP] < 0) return;
            int lastClosed = _ltfHistoryCutoffBar >= 0
                ? Math.Min(CurrentBars[LTF_BIP], _ltfHistoryCutoffBar)
                : (State == State.Realtime ? Math.Max(0, CurrentBars[LTF_BIP] - 1) : CurrentBars[LTF_BIP]);
            int first = Math.Max(0, Math.Min(node.PivotBar, lastClosed));
            int ago = CurrentBars[LTF_BIP] - first;
            if (ago < 0 || ago > CurrentBars[LTF_BIP]) return;
            double closeAtPivot = Closes[LTF_BIP][ago];
            double half = TickSize * 0.5;
            if (closeAtPivot > node.Level + half) node.Side = LtfLevelSide.Support;
            else if (closeAtPivot < node.Level - half) node.Side = LtfLevelSide.Resistance;
            else node.Side = node.IsHighPivot ? LtfLevelSide.Resistance : LtfLevelSide.Support;
            node.Tested = false;
            node.StateChangedBar = first;
            node.LastProcessedBar = first;
            for (int bar = first + 1; bar <= lastClosed; bar++)
                LtfProcessNodeAtBar(node, bar);
        }

        // Side flips: a lost support becomes resistance; a gained resistance becomes support.
        // A node becomes Tested when approached from the correct side, touched within
        // tolerance, and the close holds/rejects.
        private void LtfProcessNodeAtBar(LtfLevelNode node, int barIndex)
        {
            if (node == null || barIndex <= node.LastProcessedBar || barIndex <= 0
                || CurrentBars[LTF_BIP] < barIndex)
                return;
            int barsAgo = CurrentBars[LTF_BIP] - barIndex;
            int prevAgo = barsAgo + 1;
            if (barsAgo < 0 || prevAgo > CurrentBars[LTF_BIP]) return;
            double high = Highs[LTF_BIP][barsAgo];
            double low = Lows[LTF_BIP][barsAgo];
            double close = Closes[LTF_BIP][barsAgo];
            double previousClose = Closes[LTF_BIP][prevAgo];
            double breakTol = Math.Max(0, LtfGainLossToleranceTicks) * TickSize;
            double touchTol = Math.Max(0, LtfTestTouchToleranceTicks) * TickSize;
            double holdTol = Math.Max(0, LtfTestCloseHoldTicks) * TickSize;
            double half = TickSize * 0.5;

            if (node.Side == LtfLevelSide.Support)
            {
                bool lost = LtfUseWicksForGainLoss ? low < node.Level - breakTol : close < node.Level - breakTol;
                if (lost)
                {
                    node.Side = LtfLevelSide.Resistance;
                    node.Tested = false;
                    node.StateChangedBar = barIndex;
                }
                else if (!node.Tested && barIndex > node.StateChangedBar)
                {
                    bool approached = previousClose > node.Level + half;
                    bool touched = low <= node.Level + touchTol;
                    bool held = close >= node.Level + holdTol;
                    if (approached && touched && held) node.Tested = true;
                }
            }
            else if (node.Side == LtfLevelSide.Resistance)
            {
                bool gained = LtfUseWicksForGainLoss ? high > node.Level + breakTol : close > node.Level + breakTol;
                if (gained)
                {
                    node.Side = LtfLevelSide.Support;
                    node.Tested = false;
                    node.StateChangedBar = barIndex;
                }
                else if (!node.Tested && barIndex > node.StateChangedBar)
                {
                    bool approached = previousClose < node.Level - half;
                    bool touched = high >= node.Level - touchTol;
                    bool rejected = close <= node.Level - holdTol;
                    if (approached && touched && rejected) node.Tested = true;
                }
            }
            node.LastProcessedBar = barIndex;
        }

        private Dictionary<long, LtfPriceState> LtfCapturePriceStates()
        {
            Dictionary<long, LtfPriceState> states = new Dictionary<long, LtfPriceState>();
            foreach (LtfLevelNode node in _ltfNodes.Values)
            {
                if (node == null || node.Level <= 0 || node.Side == LtfLevelSide.Unknown)
                    continue;

                long key = LtfTickKey(node.Level);
                LtfPriceState state;
                if (!states.TryGetValue(key, out state))
                {
                    state = new LtfPriceState { Level = node.Level };
                    states[key] = state;
                }

                if (node.Side == LtfLevelSide.Support)
                {
                    state.HasSupport = true;
                    if (node.Tested) state.HasTestedSupport = true;
                }
                else if (node.Side == LtfLevelSide.Resistance)
                {
                    state.HasResistance = true;
                    if (node.Tested) state.HasTestedResistance = true;
                }
            }
            return states;
        }

        // OuterPairBoundary mode: the outside endpoint of an adjacent-pivot pair remains the
        // trigger even after price crosses the inside endpoint (strategy comment preserved).
        private List<LtfPairBoundaryState> LtfCapturePairBoundaries()
        {
            List<LtfPairBoundaryState> result = new List<LtfPairBoundaryState>();
            int lastPairStart = _ltfPivotBars.Count - 2;
            if (lastPairStart < 0) return result;

            for (int i = 0; i <= lastPairStart; i++)
            {
                double startLevel = LtfSelectedPivotLevel(i);
                double endLevel = LtfSelectedPivotLevel(i + 1);
                if (double.IsNaN(startLevel) || double.IsNaN(endLevel)) continue;

                string startKey = LtfMakeNodeKey(_ltfPivotBars[i], startLevel, _ltfPivotIsHigh[i]);
                string endKey = LtfMakeNodeKey(_ltfPivotBars[i + 1], endLevel, _ltfPivotIsHigh[i + 1]);
                LtfLevelNode startNode, endNode;
                if (!_ltfNodes.TryGetValue(startKey, out startNode)
                    || !_ltfNodes.TryGetValue(endKey, out endNode)
                    || startNode == null || endNode == null)
                    continue;

                bool isUpMove = !_ltfPivotIsHigh[i] && _ltfPivotIsHigh[i + 1];
                bool isDownMove = _ltfPivotIsHigh[i] && !_ltfPivotIsHigh[i + 1];
                LtfLevelNode lowerNode = startLevel <= endLevel ? startNode : endNode;
                LtfLevelNode upperNode = startLevel >= endLevel ? startNode : endNode;

                bool isRl = isDownMove && upperNode.Side == LtfLevelSide.Resistance && upperNode.Tested;
                bool isSg = isUpMove && lowerNode.Side == LtfLevelSide.Support && lowerNode.Tested;
                if (!isRl && !isSg) continue;

                result.Add(new LtfPairBoundaryState
                {
                    IsRlPair = isRl,
                    IsSgPair = isSg,
                    LowerLevel = Math.Min(startLevel, endLevel),
                    UpperLevel = Math.Max(startLevel, endLevel)
                });
            }
            return result;
        }

        // ---- Mirror-level integration: armed by touch, re-armed on every touch ----

        // A long Mirror level is confirmed by an RL GAINED; a short level by an SG LOST.
        // Optional max-distance filter between the LTF trigger level and the Mirror level.
        private bool LtfEventMatchesLevel(TrackedLevel lvl, LtfTriggerEvent evt)
        {
            if (evt.IsLong != lvl.IsLong) return false;
            if (LtfLevelMaxDistanceTicks > 0
                && Math.Abs(evt.Level - lvl.Price) > LtfLevelMaxDistanceTicks * TickSize + TickSize * 0.5)
                return false;
            return true;
        }

        // Arm (or re-arm) every active level the forming primary bar has touched.
        private void ArmLtfTouches()
        {
            double tol = TouchToleranceTicks * TickSize;
            DateTime barTime = Times[0][0];
            for (int p = 0; p < NUM_PAT; p++)
            {
                if (!IsPatternEnabled(p)) continue;
                for (int t = 0; t < NUM_TF; t++)
                {
                    if (!IsTfEnabled(t)) continue;
                    var tDict = tracked[p][t];
                    if (tDict == null || tDict.Count == 0) continue;

                    foreach (var lvl in tDict.Values)
                    {
                        if (lvl.Removed || lvl.Chain == null || lvl.Chain.LtfArmed) continue;
                        if (barTime < lvl.HtfStartTime || barTime > lvl.HtfEndTime) continue;
                        bool touched = lvl.IsLong ? Low[0] <= lvl.Price + tol : High[0] >= lvl.Price - tol;
                        if (touched) lvl.Chain.LtfArmed = true;
                    }
                }
            }
        }

        // Realtime: match a fresh LTF trigger against every armed, currently-active Mirror level.
        private void MatchLtfEventLive(LtfTriggerEvent evt)
        {
            DateTime barTime = Times[0][0];
            for (int p = 0; p < NUM_PAT; p++)
            {
                if (!IsPatternEnabled(p)) continue;
                for (int t = 0; t < NUM_TF; t++)
                {
                    if (!IsTfEnabled(t)) continue;
                    var tDict = tracked[p][t];
                    if (tDict == null || tDict.Count == 0) continue;

                    foreach (var lvl in tDict.Values)
                    {
                        if (lvl.Removed || lvl.Chain == null || !lvl.Chain.LtfArmed) continue;
                        if (barTime < lvl.HtfStartTime || barTime > lvl.HtfEndTime) continue;
                        if (lvl.IsLive && lvl.LastUpdatedBip0Bar < CurrentBars[0] - 1) continue; // repainted away
                        if (!LtfEventMatchesLevel(lvl, evt)) continue;

                        EmitLtfSignal(lvl, evt);
                        lvl.Chain.LtfArmed = false;
                    }
                }
            }
        }

        // A level synced after its window has passed (historical processing, HTF-close backfill,
        // reload catch-up): walk its window bars oldest-first, re-arming on every touch and firing
        // on the first matching LTF trigger after each arm — the sequence the live path produces.
        private void RetroEvaluateLtf(TrackedLevel lvl)
        {
            if (!EnableLtfTrigger || lvl.LtfRetroEvaluated) return;
            lvl.LtfRetroEvaluated = true;
            if (_ltfEvents == null || _ltfEvents.Count == 0 || lvl.Chain == null) return;

            int firstB = State == State.Historical ? 0 : 1;

            List<int> agos = null;
            int maxBack = CurrentBars[0];
            for (int b = firstB; b < maxBack; b++)
            {
                DateTime bt = Times[0][b];
                if (bt < lvl.HtfStartTime) break;
                if (bt <= lvl.HtfEndTime)
                    (agos ?? (agos = new List<int>())).Add(b);
            }
            if (agos == null) return;

            bool armed = lvl.Chain.LtfArmed;
            double tol = TouchToleranceTicks * TickSize;

            int oldestAgo = agos[agos.Count - 1];
            DateTime prevBarTime = oldestAgo + 1 <= CurrentBars[0] ? Times[0][oldestAgo + 1] : Core.Globals.MinDate;
            int ei = FirstLtfEventAfter(prevBarTime);

            for (int i = agos.Count - 1; i >= 0; i--) // oldest first
            {
                int ago = agos[i];
                DateTime bt = Times[0][ago];

                // Arm first: sub-bar ordering of the touch vs the LTF close is unknowable — be permissive.
                bool touched = lvl.IsLong ? Lows[0][ago] <= lvl.Price + tol : Highs[0][ago] >= lvl.Price - tol;
                if (touched) armed = true;

                while (ei < _ltfEvents.Count && _ltfEvents[ei].Time <= bt)
                {
                    var evt = _ltfEvents[ei];
                    ei++;
                    if (!armed || !LtfEventMatchesLevel(lvl, evt)) continue;
                    EmitLtfSignal(lvl, evt);
                    armed = false;
                }
            }

            lvl.Chain.LtfArmed = armed;
        }

        // First index in the (chronological) event list with Time > time.
        private int FirstLtfEventAfter(DateTime time)
        {
            int lo = 0, hi = _ltfEvents.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_ltfEvents[mid].Time <= time) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private void EmitLtfSignal(TrackedLevel lvl, LtfTriggerEvent evt)
        {
            int barIdx = BarsArray[0].GetBar(evt.Time);
            if (barIdx < 0) return;
            // CurrentBar belongs to the BIP in progress (the LTF series when called live) —
            // offsets into the primary series must always come from CurrentBars[0].
            int ago = CurrentBars[0] - barIdx;
            if (ago < 0 || ago > CurrentBars[0]) return;

            // One LTF signal per pattern/TF/direction per bar (live + retro paths can overlap)
            for (int i = 0; i < _signals.Count; i++)
            {
                var s = _signals[i];
                if (!s.Retracted && s.IsLtf && s.SetupBarIdx == barIdx && s.IsLong == lvl.IsLong
                    && s.Pattern == lvl.PatternType && s.TfIndex == lvl.TfIndex)
                    return;
            }

            int dir = lvl.IsLong ? 1 : -1;
            Values[12][ago] = dir;

            double lo2 = Lows[0][ago], hi2 = Highs[0][ago];
            double arrowOff = ArrowOffsetTicks * TickSize;
            double codeOff  = (2 * ArrowOffsetTicks + 3) * TickSize;

            // Stack the code label under any other signals already on this bar/direction
            int stack = 0;
            for (int i = 0; i < _signals.Count; i++)
                if (!_signals[i].Retracted && _signals[i].SetupBarIdx == barIdx && _signals[i].IsLong == lvl.IsLong)
                    stack++;
            codeOff += stack * 3 * TickSize;

            var sig = new EmittedSignal
            {
                IsLtf       = true,
                SetupBarIdx = barIdx,
                SetupTime   = Times[0][ago],
                IsLong      = lvl.IsLong,
                Pattern     = lvl.PatternType,
                TfIndex     = lvl.TfIndex,
                Label       = "L " + lvl.PatternType + _tfLabels[lvl.TfIndex],
                CodeY       = lvl.IsLong ? lo2 - codeOff : hi2 + codeOff,
                Count       = 0,
                ConfMask    = 0,
                Level       = lvl,
                Provisional = State != State.Historical && lvl.IsLive,
                HasArrow    = true,
                ArrowTime   = Times[0][ago],
                ArrowY      = lvl.IsLong ? lo2 - arrowOff : hi2 + arrowOff
            };
            _signals.Add(sig);

            // Research row for the LTF trigger firing (bar may still be forming when live —
            // its OHLC/close are the values at event time; SignalEmitted marks it fired).
            if (EnableResearchLog)
            {
                double ro = Opens[0][ago], rc = Closes[0][ago];
                bool rClosedBeyond = lvl.IsLong ? rc > lvl.Price : rc < lvl.Price;
                var rev = LogTouchEvent(barIdx, ago, lvl, "L", ro, hi2, lo2, rc, false, rClosedBeyond, 0, 0, "");
                if (rev != null) { rev.SignalEmitted = true; rev.Retracted = false; }
                sig.Research = rev;
            }

            if (EnableDebugPrints)
                Dbg($"[MirrorEntry] {sig.SetupTime:MM/dd HH:mm:ss} LTF SIGNAL {lvl.PatternType}{_tfLabels[lvl.TfIndex]} {(lvl.IsLong ? "L" : "S")}@{lvl.Price} ltfLevel={evt.Level} {(evt.IsLong ? "RL GAINED" : "SG LOST")}");

            DrawSignal(sig);
        }

        #endregion

        #region Level Sync (ported from AlightenMirrorV0031, drawing/research stripped)

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

                    // Just-closed HTF bar
                    double long1  = longSeries.IsValidDataPoint(1)  ? longSeries[1]  : 0;
                    double short1 = shortSeries.IsValidDataPoint(1) ? shortSeries[1] : 0;
                    SyncSingleSignal(p, t, bipIdx, 1, long1, true);
                    SyncSingleSignal(p, t, bipIdx, 1, short1, false);

                    // Live/forming HTF bar
                    double long0  = longSeries.IsValidDataPoint(0)  ? longSeries[0]  : 0;
                    double short0 = shortSeries.IsValidDataPoint(0) ? shortSeries[0] : 0;
                    SyncSingleSignal(p, t, bipIdx, 0, long0, true);
                    SyncSingleSignal(p, t, bipIdx, 0, short0, false);
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

            if (signalStartTime >= htfBarCloseTime) signalStartTime = htfBarStartTime;

            // PERF fast path: identical (start, price) signal as the previous sync of this slot
            int slotIdx = ((p * NUM_TF + t) * 2 + (isLong ? 0 : 1)) * 2 + barsAgoOnHtf;
            var slot = _syncCache[slotIdx];
            if (slot.Level != null && !slot.Level.Removed
                && slot.StartTicks == signalStartTime.Ticks && slot.Price == priceLevel)
            {
                var lvl = slot.Level;
                if (lvl.HtfEndTime < htfBarCloseTime)
                    lvl.HtfEndTime = htfBarCloseTime;
                lvl.LastUpdatedBip0Bar = CurrentBars[0];
                lvl.IsLive = isLiveSignal;
                return;
            }

            string tag = $"MRE_{pType}_TF{t}_{(isLong ? "L" : "S")}_{signalStartTime.Ticks}";
            tag += $"_{priceLevel.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}";

            if (tDict.TryGetValue(tag, out var existing))
            {
                if (existing.HtfEndTime < htfBarCloseTime) existing.HtfEndTime = htfBarCloseTime;
                if (Math.Abs(existing.Price - priceLevel) > 0.0001) existing.Price = priceLevel;

                bool firmedUp = existing.IsLive && !isLiveSignal; // forming bar closed with the level intact
                existing.LastUpdatedBip0Bar = CurrentBars[0];
                existing.IsLive = isLiveSignal;

                slot.StartTicks = signalStartTime.Ticks;
                slot.Price = priceLevel;
                slot.Level = existing;

                // The level is now confirmed by a closed HTF bar: back-fill touches missed
                // while it was forming (stale skips, bars before it first appeared) — the
                // same walk a chart refresh would do. Chain accounting and the per-bar
                // dedupe in TryEntryAtBar prevent double signals.
                if (firmedUp && State != State.Historical)
                    RetroEvaluateLevel(existing);
            }
            else
            {
                var newLevel = new TrackedLevel
                {
                    Tag = tag,
                    Price = priceLevel,
                    HtfStartTime = signalStartTime,
                    HtfEndTime = htfBarCloseTime,
                    IsLong = isLong,
                    PatternType = pType,
                    TfIndex = t,
                    CreatedBip0Bar = CurrentBars[0],
                    LastUpdatedBip0Bar = CurrentBars[0],
                    IsLive = isLiveSignal
                };

                // Same-price segments share one chain so age / one-signal accounting
                // survives HTF window rollovers.
                string chainKey = $"{pType}_{t}_{(isLong ? "L" : "S")}_{priceLevel.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}";
                if (!_chains.TryGetValue(chainKey, out var chain))
                {
                    chain = new LevelChain { Key = chainKey, FirstSeenBar = CurrentBars[0] };
                    _chains[chainKey] = chain;
                }
                chain.Segments++;
                newLevel.Chain = chain;

                tDict[tag] = newLevel;

                slot.StartTicks = signalStartTime.Ticks;
                slot.Price = priceLevel;
                slot.Level = newLevel;

                // Historical: the window is already complete (HTF series only shows closed
                // bars while processing history) -> evaluate its primary bars retroactively.
                if (State == State.Historical)
                {
                    RetroEvaluateLevel(newLevel);
                }
                // A brand-new segment synced from a CLOSED HTF bar in realtime: the level
                // appeared (or repainted to this price) only at the HTF close, so its
                // window bars were never evaluated at this price — walk them like a
                // refresh would.
                else if (!isLiveSignal && newLevel.HtfStartTime < Times[0][0])
                {
                    RetroEvaluateLevel(newLevel);
                }
                // Reload catch-up: levels synced in the first bars after the historical->
                // realtime transition belong to HTF windows already in progress at load
                // time. Their closed bars were processed before the level existed, so
                // walk them retroactively too (closed bars only).
                else if (_transitionBar >= 0 && CurrentBars[0] - _transitionBar <= 5
                         && newLevel.HtfStartTime < Times[0][0])
                {
                    RetroEvaluateLevel(newLevel);
                }
            }
        }

        // Drop levels that ended more than MirrorLookbackBars HTF bars ago (no drawings to keep).
        private void PruneStaleLevels()
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
                            var tl = tDict[key];
                            tl.Removed = true;
                            ReleaseChain(tl);
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
            List<TrackedLevel> toRemove = null;
            foreach (var tl in tDict.Values)
            {
                if (tl.HtfEndTime != justClosedBarCloseTime) continue;
                if (tl.IsLong ? justClosedBarClosePrice < tl.Price : justClosedBarClosePrice > tl.Price)
                    (toRemove ?? (toRemove = new List<TrackedLevel>())).Add(tl);
            }
            if (toRemove != null)
            {
                foreach (var tl in toRemove)
                {
                    tl.Removed = true;
                    ReleaseChain(tl);
                    tDict.Remove(tl.Tag);
                }
            }
        }

        // Drop the chain once its last segment is gone, so a level re-forming at the
        // same price later counts as a fresh opportunity.
        private void ReleaseChain(TrackedLevel tl)
        {
            if (tl.Chain == null) return;
            tl.Chain.Segments--;
            if (tl.Chain.Segments <= 0)
                _chains.Remove(tl.Chain.Key);
        }

        #endregion

        #region Settings Modal

        private void CreateToolbarButton()
        {
            try
            {
                if (ChartControl == null) return;
                chartWindow = Window.GetWindow(ChartControl.Parent) as NinjaTrader.Gui.Chart.Chart;
                if (chartWindow == null || settingsButton != null) return;

                settingsButton = IndicatorVisualStyleHelper.CreateSettingsButton("Entry Settings", "AlightenMirrorEntryV3SettingsBtn", (s, e) => OpenSettingsWindow());
                settingsButton.Width = 110;
                settingsButton.ToolTip = "Toggle entry signal visibility per pattern and timeframe";
                chartWindow.MainMenu.Add(settingsButton);

                exportButton = IndicatorVisualStyleHelper.CreateSettingsButton("Export Research", "AlightenMirrorEntryV3ExportBtn", (s, e) => ExportResearchCsv());
                exportButton.Width = 120;
                exportButton.ToolTip = "Export the research touch log to Documents\\NinjaTrader 8\\Export\\MirrorEntryResearch_*.csv";
                chartWindow.MainMenu.Add(exportButton);
            }
            catch (Exception ex) { Print("[MirrorEntryV0003] toolbar error: " + ex.Message); }
        }

        private void TryRemoveToolbarButton()
        {
            try { if (chartWindow != null && settingsButton != null) chartWindow.MainMenu.Remove(settingsButton); } catch { }
            try { if (chartWindow != null && exportButton != null) chartWindow.MainMenu.Remove(exportButton); } catch { }
            try { if (settingsWindow != null) settingsWindow.Close(); } catch { }
            settingsButton = null; exportButton = null; settingsWindow = null; chartWindow = null;
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
                Print($"[MirrorEntryV0003] Failed to open settings: {ex.Message}");
            }
        }

        private Window BuildInlineSettingsWindow()
        {
            var win = new Window
            {
                Title = "Entry Signal Settings",
                Width = 550,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
                Foreground = Brushes.Gainsboro,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                Owner = Window.GetWindow(ChartControl?.Parent)
            };

            IndicatorVisualStyleHelper.ApplyDialogChrome(win);

            var root = new StackPanel { Margin = new Thickness(12) };

            var gb = new GroupBox
            {
                Header = "Entry Signals — Pattern Timeframes",
                Margin = new Thickness(0, 6, 0, 6),
                Foreground = Brushes.Gainsboro,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60))
            };
            var grid = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(6) };
            gb.Content = grid;
            root.Children.Add(gb);

            CheckBox CheckRow(string label, bool initial, Action<bool> onChanged)
            {
                var cb = IndicatorVisualStyleHelper.CreateDialogCheckBox(label);
                cb.IsChecked = initial;
                cb.Margin = new Thickness(0, 3, 12, 3);
                cb.Checked += (s, e) => { onChanged(true); ForceUISync(); };
                cb.Unchecked += (s, e) => { onChanged(false); ForceUISync(); };
                return cb;
            }

            string[] tfShort = { "D", "4H", "1H", "30m", "15m", "10m", "5m" };

            var patternGetters = new Func<bool>[NUM_PAT][]
            {
                new Func<bool>[] { () => SignalPatternATF1_Daily, () => SignalPatternATF2_240m, () => SignalPatternATF3_60m, () => SignalPatternATF4_30m, () => SignalPatternATF5_15m, () => SignalPatternATF6_10m, () => SignalPatternATF7_5m },
                new Func<bool>[] { () => SignalPatternBTF1_Daily, () => SignalPatternBTF2_240m, () => SignalPatternBTF3_60m, () => SignalPatternBTF4_30m, () => SignalPatternBTF5_15m, () => SignalPatternBTF6_10m, () => SignalPatternBTF7_5m },
                new Func<bool>[] { () => SignalPatternGTF1_Daily, () => SignalPatternGTF2_240m, () => SignalPatternGTF3_60m, () => SignalPatternGTF4_30m, () => SignalPatternGTF5_15m, () => SignalPatternGTF6_10m, () => SignalPatternGTF7_5m },
                new Func<bool>[] { () => SignalPatternHTF1_Daily, () => SignalPatternHTF2_240m, () => SignalPatternHTF3_60m, () => SignalPatternHTF4_30m, () => SignalPatternHTF5_15m, () => SignalPatternHTF6_10m, () => SignalPatternHTF7_5m },
                new Func<bool>[] { () => SignalPatternFTF1_Daily, () => SignalPatternFTF2_240m, () => SignalPatternFTF3_60m, () => SignalPatternFTF4_30m, () => SignalPatternFTF5_15m, () => SignalPatternFTF6_10m, () => SignalPatternFTF7_5m },
            };

            var patternSetters = new Action<bool>[NUM_PAT][]
            {
                new Action<bool>[] { v => SignalPatternATF1_Daily = v, v => SignalPatternATF2_240m = v, v => SignalPatternATF3_60m = v, v => SignalPatternATF4_30m = v, v => SignalPatternATF5_15m = v, v => SignalPatternATF6_10m = v, v => SignalPatternATF7_5m = v },
                new Action<bool>[] { v => SignalPatternBTF1_Daily = v, v => SignalPatternBTF2_240m = v, v => SignalPatternBTF3_60m = v, v => SignalPatternBTF4_30m = v, v => SignalPatternBTF5_15m = v, v => SignalPatternBTF6_10m = v, v => SignalPatternBTF7_5m = v },
                new Action<bool>[] { v => SignalPatternGTF1_Daily = v, v => SignalPatternGTF2_240m = v, v => SignalPatternGTF3_60m = v, v => SignalPatternGTF4_30m = v, v => SignalPatternGTF5_15m = v, v => SignalPatternGTF6_10m = v, v => SignalPatternGTF7_5m = v },
                new Action<bool>[] { v => SignalPatternHTF1_Daily = v, v => SignalPatternHTF2_240m = v, v => SignalPatternHTF3_60m = v, v => SignalPatternHTF4_30m = v, v => SignalPatternHTF5_15m = v, v => SignalPatternHTF6_10m = v, v => SignalPatternHTF7_5m = v },
                new Action<bool>[] { v => SignalPatternFTF1_Daily = v, v => SignalPatternFTF2_240m = v, v => SignalPatternFTF3_60m = v, v => SignalPatternFTF4_30m = v, v => SignalPatternFTF5_15m = v, v => SignalPatternFTF6_10m = v, v => SignalPatternFTF7_5m = v },
            };

            for (int p = 0; p < NUM_PAT; p++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = "Pt " + _patternKeys[p], Width = 30, Foreground = Brushes.Gainsboro, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                for (int t = 0; t < NUM_TF; t++)
                {
                    var setter = patternSetters[p][t];
                    row.Children.Add(CheckRow(tfShort[t], patternGetters[p][t](), v => setter(v)));
                }
                grid.Children.Add(row);
            }

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            var btnClose = IndicatorVisualStyleHelper.CreateDangerDialogButton("Close", (s, e) => { settingsWindow?.Close(); settingsWindow = null; });
            btnRow.Children.Add(btnClose);
            root.Children.Add(btnRow);

            win.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return win;
        }

        // Redraw every recorded signal against the current visibility toggles.
        private void ForceUISync()
        {
            try
            {
                if (_signals == null) return;
                foreach (var sig in _signals.ToArray())
                {
                    if (sig.Retracted) continue;
                    DrawSignal(sig);
                }

                if (ChartControl != null) ChartControl.InvalidateVisual();
            }
            catch (Exception ex)
            {
                Print("[MirrorEntryV0003] ForceUISync Error: " + ex.Message);
            }
        }

        #endregion
    }
}

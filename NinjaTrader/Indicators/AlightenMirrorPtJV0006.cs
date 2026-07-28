#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;

using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    // Version ID: 2026-07-23b AlightenMirrorPtJV0006 — Pattern J v2, rebuilt on the Trends
    // ("PairBounds") engine instead of the Patch event engine: persistent pair population,
    // live endpoint gain/loss state machines with Tested tracking, SG/RL classification and
    // the PairBounds panel — all unchanged from Trends — plus:
    //   1. Patch-style pair QUALIFICATION: a pivot leg must span Minimum Trend Bars and move
    //      Minimum Trend Movement (ticks) wick-to-wick to become a pair (defaults 1/0 = off).
    //   2. Levels sit at the pivot candle's BODY extreme (Use Wicks For Pair Levels = false,
    //      the Trends default) — "drawn to the matching body of the candle that made the pivot".
    //   3. TRIANGLES instead of test dots: up-triangle when a support level's first-touch test
    //      holds (long), down-triangle when a resistance test rejects (short).
    // V0001 (Patch-based, sequential one-touch signals) remains a separate indicator.
    //
    // V0006 (2026-07-24): Mirror-family hosting interface — BarsToProcess (0 = all,
    // hosts pass Src Bars To Process), PatternJLongLevel/PatternJShortLevel series
    // (the level BEING TESTED on each bar — exactly where the standalone triangles
    // fire, held through the wick sequence; 0 = no test; MaxReportDistanceTicks
    // suppresses outliers), and Calc Only Mode suppressing all drawing for hosted
    // instances. Signal behavior is identical to the validated V0003.
    //
    // V0003 (2026-07-24): SEQUENTIAL test signals, second attempt. The V0002-era attempt
    // produced signals on bars that were not true wicks (a bar opening below the level and
    // reclaiming it passed the close-hold test). V0003's continuation is strict: only the
    // bar IMMEDIATELY RIGHT of the previous signal continues the sequence, and it must be
    // a genuine WICK of the level — the low pokes into the touch tolerance while the BODY
    // (open and close) holds on the correct side (Pattern A wick semantics). The first
    // test of a level is completely unchanged from V0002. One non-wick bar ends the
    // sequence; the level then stays tested (no re-signal until it flips sides).
    public class AlightenMirrorPtJV0006 : Indicator
    {
        private enum LevelSide
        {
            Unknown,
            Support,
            Resistance
        }

        private class LevelNode
        {
            public string Key;
            public int PivotBar;
            public double Level;
            public bool IsHighPivot;

            public LevelSide Side;
            public bool Tested;
            public int StateChangedBar;
            public int LastProcessedBar;
            public int TestedBar = -1;      // Pattern J: last bar of the signal sequence
            public int LastSignalBar = -1;  // sequence continues only on the very next bar
            public List<string> SignalTags = new List<string>();

            public string DotTag;
            public string LabelTag;
        }

        private class LevelPair
        {
            public string Key;
            public int StartBar;
            public int EndBar;
            public bool IsUpMove;
            public bool IsConfirmed;

            public LevelNode Start;
            public LevelNode End;

            public int ProximityRank;
            public int DirectionRank;
            public bool DrawThisBar;

            public string StartLineTag;
            public string EndLineTag;
            public string LegTag;
        }

        // Optional legacy gained/lost event levels from the original indicator.
        // These are separate from the structural pair endpoints and can be hidden
        // by using Transparent colors while preserving their logic for later use.
        private class LegacyEventLevel
        {
            public double Level;
            public bool IsGain;
            public int CreatedBar;
            public bool Tested;

            public string TagLine;
            public string DotTag;

            public bool Frozen;
            public int FrozenBar;
            public bool Inactive;
        }

        // Strategy Builder / Data Box plots
        private const int PLOT_HOLD_ABOVE       = 0;
        private const int PLOT_HOLD_BELOW       = 1;
        private const int PLOT_HOLD_ABOVE_STATE = 2; // 1=G, 2=SG
        private const int PLOT_HOLD_BELOW_STATE = 3; // -1=L, -2=RL
        private const int PLOT_NEAREST_G         = 4;
        private const int PLOT_NEAREST_L         = 5;
        private const int PLOT_NEAREST_SG        = 6;
        private const int PLOT_NEAREST_RL        = 7;
        private const int PLOT_J_LONG            = 8; // support level tested THIS bar (0 = none)
        private const int PLOT_J_SHORT           = 9; // resistance level tested THIS bar (0 = none)
        private const int PLOT_J_SIGNAL          = 10; // +1 long test / -1 short test / 0 (Pattern A-style)

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PatternJSignal { get { return Values[PLOT_J_SIGNAL]; } }

        // Mirror-family hosting interface: the active Pattern J level per direction.
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PatternJLongLevel { get { return Values[PLOT_J_LONG]; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> PatternJShortLevel { get { return Values[PLOT_J_SHORT]; } }

        // Pivot storage. Bars are PRIMARY-series absolute bar indexes.
        private readonly List<int> pivotBars = new List<int>();
        private readonly List<double> pivotHLPrices = new List<double>();
        private readonly List<bool> pivotIsHigh = new List<bool>();
        private readonly List<double> guidePrices = new List<double>();

        private readonly Dictionary<string, LevelNode> nodesByKey = new Dictionary<string, LevelNode>();
        private readonly List<LevelPair> pairs = new List<LevelPair>();
        private readonly List<LegacyEventLevel> legacyEvents = new List<LegacyEventLevel>();
        private int legacyEventId;
        private readonly HashSet<string> pairDrawTags = new HashSet<string>();
        private readonly HashSet<string> testedLevelDrawTags = new HashSet<string>();
        private readonly HashSet<string> nodeDrawTags = new HashSet<string>();
        private readonly HashSet<string> zigZagDrawTags = new HashSet<string>();

        private Brush zigZagBrush;
        private Brush uptrendPairBrush;
        private Brush downtrendPairBrush;
        private Brush sgPairBrush;
        private Brush rlPairBrush;
        private Brush supportDotBrush;
        private Brush resistanceDotBrush;
        private Brush legacyGainBrush;
        private Brush legacyLossBrush;
        private Brush legacyGainTestBrush;
        private Brush legacyLossTestBrush;
        private Brush legacyGainDotBrush;
        private Brush legacyLossDotBrush;

        private string instanceId = string.Empty;
        private LevelPair currentSgPair;
        private LevelPair currentRlPair;
        private int lastProcessedPrimaryStructureBar = -1;
        private int lastProcessedPrimaryStateBar = -1;

        // V0006: tests committed on the same bar whose pivot churn destroyed the
        // node. Without this, a bar that both tests a level and rebuilds the swing
        // structure (fast rejections — exactly the moves worth recording) loses the
        // node in SynchronizePairsFromPivots before ProcessAllNodesAtAbsoluteBar
        // ever evaluates the test, so the signal never exists historically and a
        // level seen live vanishes on refresh.
        private readonly List<LevelNode> retiredTestedNodes = new List<LevelNode>();
        private int structureBarInProcess = -1;
        private const int MaxRetiredTestedNodes = 500;

        // V0006: a pair that confirms 2+ bars after its test bar (the next opposite
        // pivot arrives late) discovers the test in InitializeNodeFromHistory, but
        // the normal reporting path only announces TestedBar == stateBar — the test
        // reached standalone charts and never the hosting Mirror. Queue them here and
        // back-stamp the plots at the tested bar's offset on the next UpdatePlots.
        private struct RetroReport
        {
            public int Bar;
            public double Level;
            public bool IsShort;
        }
        private readonly List<RetroReport> pendingRetroReports = new List<RetroReport>();

        private double Tick => TickSize > 0 ? TickSize : 0.0;
        private double HalfTickTolerance => Tick > 0 ? Tick * 0.5 : 0.0;
        private int CurrentPrimaryBar => CurrentBars[0];
        private bool LegacyActionIsDelete =>
            string.Equals(LegacyCloseThroughAction, "Delete", StringComparison.OrdinalIgnoreCase);

        private static Brush MakeFrozenBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private double Normalize(double price)
        {
            if (Tick <= 0)
                return price;

            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private long TickKey(double price)
        {
            if (Tick <= 0)
                return (long)Math.Round(price * 1000000.0);

            return (long)Math.Round(price / Tick);
        }

        private string MakeNodeKey(int pivotBar, double level, bool isHighPivot)
        {
            return pivotBar + "_" + TickKey(level) + "_" + (isHighPivot ? "H" : "L");
        }

        private string MakePairKey(int startBar, double startLevel, int endBar, double endLevel)
        {
            return startBar + "_" + TickKey(startLevel) + "__" + endBar + "_" + TickKey(endLevel);
        }

        private void DrawLinePrimaryBars(string tag, int startPrimaryBar, double y1,
                                         int endPrimaryBar, double y2, Brush brush,
                                         DashStyleHelper dashStyle, int width)
        {
            int startBarsAgo = CurrentPrimaryBar - startPrimaryBar;
            int endBarsAgo = endPrimaryBar < 0 ? -1 : CurrentPrimaryBar - endPrimaryBar;

            if (startBarsAgo < 0)
                startBarsAgo = 0;

            if (endBarsAgo < -1)
                endBarsAgo = -1;

            Draw.Line(this, tag, false,
                startBarsAgo, y1,
                endBarsAgo, y2,
                brush, dashStyle, Math.Max(1, width));
        }

        private void RemovePairDrawings(LevelPair pair)
        {
            if (pair == null)
                return;

            RemoveDrawObject(pair.StartLineTag);
            RemoveDrawObject(pair.EndLineTag);
            RemoveDrawObject(pair.LegTag);

            pairDrawTags.Remove(pair.StartLineTag);
            pairDrawTags.Remove(pair.EndLineTag);
            pairDrawTags.Remove(pair.LegTag);
        }

        private void RemoveAllPairDrawings()
        {
            foreach (string tag in pairDrawTags)
                RemoveDrawObject(tag);

            pairDrawTags.Clear();
        }

        private void RemoveAllNodeDrawings()
        {
            foreach (string tag in nodeDrawTags)
                RemoveDrawObject(tag);

            nodeDrawTags.Clear();
        }

        private bool HasActiveLegacyEvent(double level, bool isGain)
        {
            double normalized = Normalize(level);

            for (int i = 0; i < legacyEvents.Count; i++)
            {
                LegacyEventLevel e = legacyEvents[i];
                if (e.Inactive)
                    continue;

                if (e.IsGain == isGain && TickKey(e.Level) == TickKey(normalized))
                    return true;
            }

            return false;
        }

        private void RemoveLegacyEventAt(int index)
        {
            if (index < 0 || index >= legacyEvents.Count)
                return;

            RemoveDrawObject(legacyEvents[index].TagLine);
            RemoveDrawObject(legacyEvents[index].DotTag);
            legacyEvents.RemoveAt(index);
        }

        private Brush GetLegacyEventBrush(LegacyEventLevel e)
        {
            if (!e.Tested)
                return e.IsGain ? legacyGainBrush : legacyLossBrush;

            return e.IsGain ? legacyGainTestBrush : legacyLossTestBrush;
        }

        private int GetLegacyEventWidth(LegacyEventLevel e)
        {
            return e.IsGain ? LegacyGainLineWidth : LegacyLossLineWidth;
        }

        private void UpdateLegacyEventLine(LegacyEventLevel e)
        {
            if (e == null)
                return;

            if (!ShowLegacyGainedLostLevels)
            {
                RemoveDrawObject(e.TagLine);
                return;
            }

            if (e.Frozen)
            {
                DrawLinePrimaryBars(e.TagLine, e.CreatedBar, e.Level, e.FrozenBar, e.Level,
                    GetLegacyEventBrush(e), DashStyleHelper.Solid, GetLegacyEventWidth(e));
            }
            else
            {
                DrawLinePrimaryBars(e.TagLine, e.CreatedBar, e.Level, -1, e.Level,
                    GetLegacyEventBrush(e), DashStyleHelper.Solid, GetLegacyEventWidth(e));
            }
        }

        private void AddLegacyEvent(double level, bool isGain, int createdPrimaryBar)
        {
            LegacyEventLevel e = new LegacyEventLevel
            {
                Level = Normalize(level),
                IsGain = isGain,
                CreatedBar = createdPrimaryBar,
                Tested = false,
                TagLine = "PB_LegacyEvent_" + instanceId + "_" + (++legacyEventId),
                DotTag = "PB_LegacyDot_" + instanceId + "_" + legacyEventId,
                Frozen = false,
                FrozenBar = -1,
                Inactive = false
            };

            legacyEvents.Add(e);

            while (legacyEvents.Count > LegacyMaxActiveLines)
                RemoveLegacyEventAt(0);

            UpdateLegacyEventLine(e);
        }

        private void DetectLegacyGainLossEvents(double currentClose, double previousClose, int createdPrimaryBar)
        {
            if (pivotBars.Count < 3)
                return;

            int lastConfirmed = pivotBars.Count - 1;
            int levelsToCheck = Math.Min(LegacyNumberOfLevels, lastConfirmed);

            for (int j = 0; j < levelsToCheck; j++)
            {
                // Preserve the original indicator's one-pivot-back guide selection.
                int pivotIndex = (lastConfirmed - 1) - j;
                if (pivotIndex < 0 || pivotIndex >= guidePrices.Count)
                    continue;

                double level = Normalize(guidePrices[pivotIndex]);
                bool crossedUp = currentClose > level + HalfTickTolerance
                              && previousClose <= level + HalfTickTolerance;
                bool crossedDown = currentClose < level - HalfTickTolerance
                                && previousClose >= level - HalfTickTolerance;

                if (crossedUp && !HasActiveLegacyEvent(level, true))
                    AddLegacyEvent(level, true, createdPrimaryBar);

                if (crossedDown && !HasActiveLegacyEvent(level, false))
                    AddLegacyEvent(level, false, createdPrimaryBar);
            }
        }

        private void ProcessLegacyEventsAtAbsoluteBar(int barIndex)
        {
            if (barIndex <= 0 || barIndex > CurrentPrimaryBar)
                return;

            int barsAgo = CurrentPrimaryBar - barIndex;
            int previousBarsAgo = barsAgo + 1;

            if (barsAgo < 0 || previousBarsAgo > CurrentPrimaryBar)
                return;

            double barHigh = Highs[0][barsAgo];
            double barLow = Lows[0][barsAgo];
            double barClose = Closes[0][barsAgo];
            double previousClose = Closes[0][previousBarsAgo];

            for (int i = legacyEvents.Count - 1; i >= 0; i--)
            {
                LegacyEventLevel e = legacyEvents[i];
                double level = e.Level;

                if (LegacyExtendRightBars > 0
                    && barIndex - e.CreatedBar > LegacyExtendRightBars)
                {
                    RemoveLegacyEventAt(i);
                    continue;
                }

                if (e.Inactive)
                {
                    UpdateLegacyEventLine(e);
                    continue;
                }

                if (!e.Tested && barIndex > e.CreatedBar)
                {
                    bool touched = barHigh >= level - HalfTickTolerance
                                && barLow <= level + HalfTickTolerance;
                    bool gainTest = e.IsGain && touched && previousClose > level + HalfTickTolerance;
                    bool lossTest = !e.IsGain && touched && previousClose < level - HalfTickTolerance;

                    if (gainTest || lossTest)
                    {
                        e.Tested = true;

                        if (ShowLegacyFirstTouchDots)
                        {
                            Brush dotBrush = e.IsGain ? legacyGainDotBrush : legacyLossDotBrush;
                            Draw.Dot(this, e.DotTag, false, barsAgo, level, dotBrush);
                        }
                    }
                }

                bool closeThrough = (e.IsGain && barClose < level - HalfTickTolerance)
                                 || (!e.IsGain && barClose > level + HalfTickTolerance);

                if (closeThrough)
                {
                    if (LegacyActionIsDelete)
                    {
                        RemoveLegacyEventAt(i);
                        continue;
                    }

                    e.Frozen = true;
                    e.FrozenBar = barIndex;
                    e.Inactive = true;
                }

                UpdateLegacyEventLine(e);
            }
        }

        private double GetSelectedPivotLevel(int pivotIndex)
        {
            if (pivotIndex < 0 || pivotIndex >= pivotBars.Count)
                return double.NaN;

            if (UseWicksForPairLevels)
                return Normalize(pivotHLPrices[pivotIndex]);

            return Normalize(guidePrices[pivotIndex]);
        }

        private void TrimPivots()
        {
            while (pivotBars.Count > MaxStoredPivots)
            {
                pivotBars.RemoveAt(0);
                pivotHLPrices.RemoveAt(0);
                pivotIsHigh.RemoveAt(0);
                guidePrices.RemoveAt(0);
            }
        }

        private void ProcessPivotPrimary(int primaryBarIndex, double wickPrice,
                                         bool isHigh, double bodyHigh, double bodyLow)
        {
            // V0006: remember which closed bar is driving this structure update so
            // the node-removal path can salvage a same-bar test (see retiredTestedNodes).
            structureBarInProcess = primaryBarIndex;
            double guide = Normalize(isHigh ? bodyHigh : bodyLow);
            wickPrice = Normalize(wickPrice);

            if (pivotBars.Count == 0)
            {
                pivotBars.Add(primaryBarIndex);
                pivotHLPrices.Add(wickPrice);
                pivotIsHigh.Add(isHigh);
                guidePrices.Add(guide);
            }
            else
            {
                int lastIndex = pivotBars.Count - 1;
                bool lastIsHigh = pivotIsHigh[lastIndex];
                double lastWickPrice = pivotHLPrices[lastIndex];

                if (lastIsHigh == isHigh)
                {
                    bool moreExtreme = (isHigh && wickPrice > lastWickPrice)
                                    || (!isHigh && wickPrice < lastWickPrice);

                    if (moreExtreme)
                    {
                        pivotBars[lastIndex] = primaryBarIndex;
                        pivotHLPrices[lastIndex] = wickPrice;
                        guidePrices[lastIndex] = guide;
                    }
                }
                else
                {
                    pivotBars.Add(primaryBarIndex);
                    pivotHLPrices.Add(wickPrice);
                    pivotIsHigh.Add(isHigh);
                    guidePrices.Add(guide);
                }
            }

            TrimPivots();
            SynchronizePairsFromPivots();
        }

        private LevelNode GetOrCreateNode(int pivotBar, double level, bool isHighPivot)
        {
            string key = MakeNodeKey(pivotBar, level, isHighPivot);
            LevelNode node;

            if (nodesByKey.TryGetValue(key, out node))
                return node;

            node = new LevelNode
            {
                Key = key,
                PivotBar = pivotBar,
                Level = Normalize(level),
                IsHighPivot = isHighPivot,
                Side = LevelSide.Unknown,
                Tested = false,
                StateChangedBar = pivotBar,
                LastProcessedBar = pivotBar - 1,
                DotTag = "PB_Dot_" + instanceId + "_" + key,
                LabelTag = "PB_Label_" + instanceId + "_" + key
            };

            nodesByKey[key] = node;
            InitializeNodeFromHistory(node);

            // V0006 retro-report: the init replay can discover a test on a bar the
            // state engine has already moved past (TestedBar == stateBar is handled
            // by the normal path; strictly older means the pair confirmed late).
            if (node.TestedBar >= 0 && node.TestedBar < lastProcessedPrimaryStateBar)
                pendingRetroReports.Add(new RetroReport
                {
                    Bar = node.TestedBar,
                    Level = node.Level,
                    IsShort = node.Side == LevelSide.Resistance
                });

            return node;
        }

        private void SynchronizePairsFromPivots()
        {
            Dictionary<string, LevelPair> oldPairs = pairs.ToDictionary(p => p.Key, p => p);
            List<LevelPair> rebuilt = new List<LevelPair>();
            HashSet<string> usedNodeKeys = new HashSet<string>();

            int lastPairStartIndex = pivotBars.Count - 2;

            for (int i = 0; i <= lastPairStartIndex; i++)
            {
                // The newest leg is still developing until another opposite pivot appears.
                if (UseConfirmedPairsOnly && i == lastPairStartIndex)
                    continue;

                double startLevel = GetSelectedPivotLevel(i);
                double endLevel = GetSelectedPivotLevel(i + 1);

                if (double.IsNaN(startLevel) || double.IsNaN(endLevel))
                    continue;

                int startBar = pivotBars[i];
                int endBar = pivotBars[i + 1];

                // Patch-style trend qualification: the leg must span enough bars and
                // move enough ticks (wick-to-wick) to count as a pair at all.
                if (endBar - startBar < Math.Max(1, MinimumTrendBars))
                    continue;
                if (Tick > 0
                    && Math.Abs(pivotHLPrices[i + 1] - pivotHLPrices[i]) / Tick + 1e-9 < Math.Max(0, MinimumTrendTicks))
                    continue;

                bool isUpMove = !pivotIsHigh[i] && pivotIsHigh[i + 1];
                string key = MakePairKey(startBar, startLevel, endBar, endLevel);

                LevelPair pair;
                if (!oldPairs.TryGetValue(key, out pair))
                {
                    pair = new LevelPair
                    {
                        Key = key,
                        StartLineTag = "PB_Start_" + instanceId + "_" + key,
                        EndLineTag = "PB_End_" + instanceId + "_" + key,
                        LegTag = "PB_Leg_" + instanceId + "_" + key
                    };
                }

                pair.StartBar = startBar;
                pair.EndBar = endBar;
                pair.IsUpMove = isUpMove;
                pair.IsConfirmed = i < lastPairStartIndex;
                pair.Start = GetOrCreateNode(startBar, startLevel, pivotIsHigh[i]);
                pair.End = GetOrCreateNode(endBar, endLevel, pivotIsHigh[i + 1]);

                rebuilt.Add(pair);
                usedNodeKeys.Add(pair.Start.Key);
                usedNodeKeys.Add(pair.End.Key);
                oldPairs.Remove(key);
            }

            foreach (LevelPair removed in oldPairs.Values)
                RemovePairDrawings(removed);

            pairs.Clear();
            pairs.AddRange(rebuilt.OrderBy(p => p.EndBar));

            List<string> unusedNodeKeys = nodesByKey.Keys.Where(k => !usedNodeKeys.Contains(k)).ToList();
            foreach (string key in unusedNodeKeys)
            {
                LevelNode node = nodesByKey[key];

                // V0006 test salvage: the bar rebuilding the structure may ALSO be
                // testing this node's level (a hard rejection makes a new extreme,
                // which replaces the pivot backing the node). Evaluate that bar
                // against the node BEFORE destroying it — the state engine would
                // otherwise never see the test (node removal runs ahead of
                // ProcessAllNodesAtAbsoluteBar). A node whose test commits is
                // RETIRED, not erased: triangles and tested-level stub stay on the
                // chart, and UpdatePlots keeps reporting the level for that bar so
                // a hosting Mirror records it. Runs identically in historical and
                // realtime processing, so the record survives a chart refresh.
                if (structureBarInProcess > node.LastProcessedBar
                    && structureBarInProcess <= CurrentPrimaryBar)
                {
                    ProcessNodeAtAbsoluteBar(node, structureBarInProcess, !CalcOnlyMode);
                    if (node.TestedBar == structureBarInProcess)
                    {
                        RemoveDrawObject(node.LabelTag);
                        nodeDrawTags.Remove(node.LabelTag);
                        nodesByKey.Remove(key);
                        retiredTestedNodes.Add(node);
                        if (retiredTestedNodes.Count > MaxRetiredTestedNodes)
                            retiredTestedNodes.RemoveAt(0);
                        continue;
                    }
                }

                RemoveDrawObject(node.DotTag);
                RemoveDrawObject(node.LabelTag);
                RemoveDrawObject(node.DotTag + "_lvl");
                nodeDrawTags.Remove(node.DotTag);
                nodeDrawTags.Remove(node.LabelTag);
                testedLevelDrawTags.Remove(node.DotTag + "_lvl");
                foreach (string sigTag in node.SignalTags)
                {
                    RemoveDrawObject(sigTag);
                    nodeDrawTags.Remove(sigTag);
                }
                nodesByKey.Remove(key);
            }
        }

        private void InitializeNodeFromHistory(LevelNode node)
        {
            if (node == null || CurrentBars[0] < 0)
                return;

            int lastHistoryBar = State == State.Realtime
                ? Math.Max(0, CurrentPrimaryBar - 1)
                : CurrentPrimaryBar;

            int firstBar = Math.Max(0, Math.Min(node.PivotBar, lastHistoryBar));
            int barsAgo = CurrentPrimaryBar - firstBar;

            if (barsAgo < 0 || barsAgo > CurrentPrimaryBar)
                return;

            double closeAtPivot = Closes[0][barsAgo];

            if (closeAtPivot > node.Level + HalfTickTolerance)
                node.Side = LevelSide.Support;
            else if (closeAtPivot < node.Level - HalfTickTolerance)
                node.Side = LevelSide.Resistance;
            else
                node.Side = node.IsHighPivot ? LevelSide.Resistance : LevelSide.Support;

            node.Tested = false;
            node.StateChangedBar = firstBar;
            node.LastProcessedBar = firstBar;

            // Replay DRAWS (side-specific per-bar tags make redraws idempotent).
            // Muted replay left live sessions missing triangles: pivot replacement
            // recreates nodes, the cleanup removes their drawn markers, and the
            // recreated node's replay re-detected the tests silently — signals
            // only reappeared after a chart refresh.
            for (int barIndex = firstBar + 1; barIndex <= lastHistoryBar; barIndex++)
                ProcessNodeAtAbsoluteBar(node, barIndex, !CalcOnlyMode);
        }

        private void ProcessNodeAtAbsoluteBar(LevelNode node, int barIndex, bool allowDrawing)
        {
            if (node == null || barIndex <= node.LastProcessedBar || barIndex <= 0)
                return;

            int barsAgo = CurrentPrimaryBar - barIndex;
            int previousBarsAgo = barsAgo + 1;

            if (barsAgo < 0 || previousBarsAgo < 0 || previousBarsAgo > CurrentPrimaryBar)
                return;

            double high = Highs[0][barsAgo];
            double low = Lows[0][barsAgo];
            double close = Closes[0][barsAgo];
            double open = Opens[0][barsAgo];
            double previousClose = Closes[0][previousBarsAgo];

            double breakTolerance = GainLossToleranceTicks * Tick;
            double touchTolerance = TestTouchToleranceTicks * Tick;
            double holdTolerance = TestCloseHoldTicks * Tick;

            bool flipped = false;
            bool newlyTested = false;

            if (node.Side == LevelSide.Support)
            {
                bool lost = UseWicksForGainLoss
                    ? low < node.Level - breakTolerance
                    : close < node.Level - breakTolerance;

                if (lost)
                {
                    node.Side = LevelSide.Resistance;
                    node.Tested = false;
                    node.StateChangedBar = barIndex;
                    node.LastSignalBar = -1;
                    flipped = true;
                }
                else if (!node.Tested && barIndex > node.StateChangedBar)
                {
                    bool approachedFromAbove = previousClose > node.Level + HalfTickTolerance;
                    bool touched = low <= node.Level + touchTolerance;
                    bool held = close >= node.Level + holdTolerance;

                    if (approachedFromAbove && touched && held)
                    {
                        node.Tested = true;
                        newlyTested = true;
                    }
                }
                else if (node.Tested && node.LastSignalBar >= 0 && barIndex == node.LastSignalBar + 1)
                {
                    // Sequence continuation — ONLY the bar immediately right of the
                    // previous signal, and ONLY a true wick: the low pokes into the
                    // touch tolerance while the BODY holds above the level.
                    bool wicked = low <= node.Level + touchTolerance;
                    bool bodyAbove = Math.Min(open, close) >= node.Level - HalfTickTolerance;
                    bool held = close >= node.Level + holdTolerance;

                    if (wicked && bodyAbove && held)
                        newlyTested = true;
                }
            }
            else if (node.Side == LevelSide.Resistance)
            {
                bool gained = UseWicksForGainLoss
                    ? high > node.Level + breakTolerance
                    : close > node.Level + breakTolerance;

                if (gained)
                {
                    node.Side = LevelSide.Support;
                    node.Tested = false;
                    node.StateChangedBar = barIndex;
                    node.LastSignalBar = -1;
                    flipped = true;
                }
                else if (!node.Tested && barIndex > node.StateChangedBar)
                {
                    bool approachedFromBelow = previousClose < node.Level - HalfTickTolerance;
                    bool touched = high >= node.Level - touchTolerance;
                    bool rejected = close <= node.Level - holdTolerance;

                    if (approachedFromBelow && touched && rejected)
                    {
                        node.Tested = true;
                        newlyTested = true;
                    }
                }
                else if (node.Tested && node.LastSignalBar >= 0 && barIndex == node.LastSignalBar + 1)
                {
                    // Sequence continuation (short mirror): a true wick — the high pokes
                    // into the touch tolerance while the BODY holds below the level.
                    bool wicked = high >= node.Level - touchTolerance;
                    bool bodyBelow = Math.Max(open, close) <= node.Level + HalfTickTolerance;
                    bool rejected = close <= node.Level - holdTolerance;

                    if (wicked && bodyBelow && rejected)
                        newlyTested = true;
                }
            }

            node.LastProcessedBar = barIndex;

            if (newlyTested)
            {
                node.TestedBar = barIndex;      // the tested-level stub follows the sequence
                node.LastSignalBar = barIndex;
            }

            if (allowDrawing && ShowTestDots && newlyTested)
            {
                // Pattern J: triangles instead of dots — up-triangle for a held support
                // test (long), down-triangle for a rejected resistance test (short).
                // One marker per signal bar (consecutive wick bars each signal).
                string sigTag = node.DotTag + "_S" + barIndex;
                if (node.Side == LevelSide.Support)
                    Draw.TriangleUp(this, sigTag, false, CurrentPrimaryBar - barIndex, node.Level, supportDotBrush);
                else
                    Draw.TriangleDown(this, sigTag, false, CurrentPrimaryBar - barIndex, node.Level, resistanceDotBrush);
                nodeDrawTags.Add(sigTag);
                node.SignalTags.Add(sigTag);
            }

            if (allowDrawing && flipped)
            {
                RemoveDrawObject(node.DotTag);
                nodeDrawTags.Remove(node.DotTag);

                // User-selectable: erase a flipped level's test signals (validity
                // check, default) or keep them as permanent history (Pattern A style).
                if (RemoveSignalsOnFlip)
                {
                    foreach (string oldSigTag in node.SignalTags)
                    {
                        RemoveDrawObject(oldSigTag);
                        nodeDrawTags.Remove(oldSigTag);
                    }
                    node.SignalTags.Clear();
                }
            }
        }

        private void ProcessAllNodesAtAbsoluteBar(int barIndex)
        {
            if (barIndex <= 0 || barIndex > CurrentPrimaryBar)
                return;

            foreach (LevelNode node in nodesByKey.Values)
                ProcessNodeAtAbsoluteBar(node, barIndex, !CalcOnlyMode);
        }

        private string GetNodeStateText(LevelNode node)
        {
            if (node == null)
                return "--";

            if (node.Side == LevelSide.Support)
                return node.Tested ? "SG" : "G";

            if (node.Side == LevelSide.Resistance)
                return node.Tested ? "RL" : "L";

            return "--";
        }

        private int GetNodeStateValue(LevelNode node)
        {
            if (node == null)
                return 0;

            if (node.Side == LevelSide.Support)
                return node.Tested ? 2 : 1;

            if (node.Side == LevelSide.Resistance)
                return node.Tested ? -2 : -1;

            return 0;
        }

        private double DistanceToPairRange(LevelPair pair, double price)
        {
            double low = Math.Min(pair.Start.Level, pair.End.Level);
            double high = Math.Max(pair.Start.Level, pair.End.Level);

            if (price < low)
                return low - price;

            if (price > high)
                return price - high;

            return 0.0;
        }

        private double DistanceToNearestPairEndpoint(LevelPair pair, double price)
        {
            if (pair == null || pair.Start == null || pair.End == null)
                return double.MaxValue;

            return Math.Min(Math.Abs(pair.Start.Level - price), Math.Abs(pair.End.Level - price));
        }

        private List<LevelPair> GetActivePairsByProximity()
        {
            double price = Close[0];

            // "Nearest" means the nearest endpoint of the pair to current price.
            // This avoids treating every pair that spans price as equally close.
            return pairs
                .Where(p => PairLookbackBars <= 0 || CurrentPrimaryBar - p.EndBar <= PairLookbackBars)
                .OrderBy(p => DistanceToNearestPairEndpoint(p, price))
                .ThenBy(p => Math.Abs(p.Start.Level - price))
                .ThenByDescending(p => p.EndBar)
                .ToList();
        }

        private void AssignPairRanksAndVisibility()
        {
            List<LevelPair> ordered = GetActivePairsByProximity();

            foreach (LevelPair pair in pairs)
            {
                pair.DrawThisBar = false;
                pair.ProximityRank = int.MaxValue;
                pair.DirectionRank = int.MaxValue;
            }

            // Regular context pairs are ranked independently by the endpoint nearest
            // current price. Their widths step down as they get farther away.
            List<LevelPair> selectedUp = ordered
                .Where(p => p.IsUpMove)
                .Take(UptrendPairsToDraw)
                .ToList();

            List<LevelPair> selectedDown = ordered
                .Where(p => !p.IsUpMove)
                .Take(DowntrendPairsToDraw)
                .ToList();

            // Pattern J: the regular proximity-ranked pair display is removed — ranks are
            // still computed for width/label ordering, but visibility comes only from the
            // SG/RL pairs and tested-level pairs below.
            for (int i = 0; i < selectedUp.Count; i++)
                selectedUp[i].DirectionRank = i;

            for (int i = 0; i < selectedDown.Count; i++)
                selectedDown[i].DirectionRank = i;

            HashSet<LevelPair> selected = new HashSet<LevelPair>(selectedUp.Concat(selectedDown));
            List<LevelPair> displayedByProximity = ordered.Where(selected.Contains).ToList();

            for (int i = 0; i < displayedByProximity.Count; i++)
                displayedByProximity[i].ProximityRank = i;

            // PairBounds is a separate structural selection. It scans every confirmed
            // pair, not only the regular pairs selected above. The SG source is the
            // nearest completed uptrend origin below price. The RL source is the
            // nearest completed downtrend origin above price.
            currentSgPair = SelectHoldAbovePair();
            currentRlPair = SelectHoldBelowPair();

            // Always make the active PairBounds source pairs visible, even when they
            // fall outside the normal up/down pair-count selections.
            if (currentSgPair != null)
                currentSgPair.DrawThisBar = true;

            if (currentRlPair != null)
                currentRlPair.DrawThisBar = true;

        }

        private bool IsCurrentSgPair(LevelPair pair)
        {
            return pair != null && currentSgPair != null && ReferenceEquals(pair, currentSgPair);
        }

        private bool IsCurrentRlPair(LevelPair pair)
        {
            return pair != null && currentRlPair != null && ReferenceEquals(pair, currentRlPair);
        }

        private Brush GetPairBrush(LevelPair pair)
        {
            if (pair == null)
                return Brushes.Gray;

            if (IsCurrentSgPair(pair))
                return sgPairBrush ?? Brushes.Goldenrod;

            if (IsCurrentRlPair(pair))
                return rlPairBrush ?? Brushes.Red;

            return pair.IsUpMove
                ? (uptrendPairBrush ?? Brushes.LimeGreen)
                : (downtrendPairBrush ?? Brushes.Magenta);
        }

        private int GetPairLineWidth(LevelPair pair)
        {
            if (IsCurrentSgPair(pair) || IsCurrentRlPair(pair))
                return Math.Max(1, PairBoundsPairLineWidth);

            int rank = pair == null || pair.DirectionRank == int.MaxValue
                ? 0
                : Math.Max(0, pair.DirectionRank);

            return Math.Max(1, NearestPairLineWidth - rank * PairLineWidthStep);
        }

        private DashStyleHelper GetNodeLineStyle(LevelNode node)
        {
            return node != null && node.Tested ? TestedLineStyle : UntestedLineStyle;
        }

        private void DrawPairs()
        {
            HashSet<string> tagsDrawnNow = new HashSet<string>();

            // Draw thinner/farther pairs first so the thicker/nearer pairs stay visible on top.
            foreach (LevelPair pair in pairs
                .Where(p => p.DrawThisBar)
                .OrderBy(p => GetPairLineWidth(p))
                .ThenByDescending(p => p.ProximityRank))
            {
                Brush brush = GetPairBrush(pair);
                int pairWidth = GetPairLineWidth(pair);

                DrawLinePrimaryBars(pair.StartLineTag,
                    pair.StartBar, pair.Start.Level,
                    -1, pair.Start.Level,
                    brush, GetNodeLineStyle(pair.Start), pairWidth);

                DrawLinePrimaryBars(pair.EndLineTag,
                    pair.EndBar, pair.End.Level,
                    -1, pair.End.Level,
                    brush, GetNodeLineStyle(pair.End), pairWidth);

                // Pattern J: connecting legs removed — only the endpoint level lines draw.
                RemoveDrawObject(pair.LegTag);
                pairDrawTags.Remove(pair.LegTag);

                tagsDrawnNow.Add(pair.StartLineTag);
                tagsDrawnNow.Add(pair.EndLineTag);
            }

            foreach (string oldTag in pairDrawTags.ToList())
            {
                if (!tagsDrawnNow.Contains(oldTag))
                    RemoveDrawObject(oldTag);
            }

            pairDrawTags.Clear();
            foreach (string tag in tagsDrawnNow)
                pairDrawTags.Add(tag);
        }

        // Pattern J: the level that created each test signal is drawn as a BOUNDED
        // segment from its pivot bar to the test bar — it never extends to the right
        // edge, so historical signals leave small level stubs instead of a wall of
        // full-width lines. The segment disappears when the level flips sides.
        private void DrawTestedLevels()
        {
            HashSet<string> tagsDrawnNow = new HashSet<string>();

            if (ShowTestedPairLevels)
            {
                // V0006: retired nodes (test salvaged from same-bar structure churn)
                // keep their stubs alongside the live node set.
                foreach (LevelNode node in nodesByKey.Values.Concat(retiredTestedNodes))
                {
                    if (!node.Tested || node.TestedBar < 0 || node.TestedBar < node.PivotBar)
                        continue;

                    string tag = node.DotTag + "_lvl";
                    Brush brush = node.Side == LevelSide.Support ? supportDotBrush : resistanceDotBrush;

                    DrawLinePrimaryBars(tag,
                        node.PivotBar, node.Level,
                        node.TestedBar, node.Level,
                        brush, TestedLineStyle, Math.Max(1, NearestPairLineWidth));

                    tagsDrawnNow.Add(tag);
                }
            }

            foreach (string oldTag in testedLevelDrawTags.ToList())
            {
                if (!tagsDrawnNow.Contains(oldTag))
                    RemoveDrawObject(oldTag);
            }

            testedLevelDrawTags.Clear();
            foreach (string tag in tagsDrawnNow)
                testedLevelDrawTags.Add(tag);
        }

        private Brush GetNodeDisplayBrush(LevelNode node)
        {
            LevelPair nearestReferencingPair = pairs
                .Where(p => p.DrawThisBar && (p.Start == node || p.End == node))
                .OrderBy(p => (IsCurrentSgPair(p) || IsCurrentRlPair(p)) ? 0 : 1)
                .ThenBy(p => p.ProximityRank)
                .FirstOrDefault();

            return nearestReferencingPair == null ? Brushes.Gray : GetPairBrush(nearestReferencingPair);
        }

        private void DrawLevelLabels()
        {
            if (!ShowPairLevelLabels)
            {
                foreach (LevelNode node in nodesByKey.Values)
                {
                    RemoveDrawObject(node.LabelTag);
                    nodeDrawTags.Remove(node.LabelTag);
                }
                return;
            }

            HashSet<LevelNode> visibleNodes = new HashSet<LevelNode>();
            foreach (LevelPair pair in pairs.Where(p => p.DrawThisBar))
            {
                visibleNodes.Add(pair.Start);
                visibleNodes.Add(pair.End);
            }

            foreach (LevelNode node in nodesByKey.Values)
            {
                if (!visibleNodes.Contains(node))
                {
                    RemoveDrawObject(node.LabelTag);
                    nodeDrawTags.Remove(node.LabelTag);
                    continue;
                }

                string text = Instrument.MasterInstrument.FormatPrice(node.Level) + " " + GetNodeStateText(node);
                Brush brush = GetNodeDisplayBrush(node);

                Draw.Text(this, node.LabelTag, false, text,
                    -LabelBarsRight, node.Level, LabelPixelOffset,
                    brush, new SimpleFont("Arial", LabelFontSize),
                    TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);

                nodeDrawTags.Add(node.LabelTag);
            }
        }

        private LevelPair SelectHoldAbovePair()
        {
            double price = Close[0];

            return pairs
                .Where(p => p.IsUpMove && p.IsConfirmed)
                .Where(p => p.Start != null && p.End != null)
                // A complete uptrend only becomes a PairBounds SG candidate after
                // its END level has been gained. The origin must still be acting as
                // support; otherwise it can no longer be the active hold-above level.
                .Where(p => p.End.Side == LevelSide.Support)
                .Where(p => p.Start.Side == LevelSide.Support)
                .Where(p => p.Start.Level < price + HalfTickTolerance)
                .Where(p => PairLookbackBars <= 0 || CurrentPrimaryBar - p.EndBar <= PairLookbackBars)
                // From the gained, completed uptrends, use the origin nearest below
                // current price. Time is only the final tie-breaker.
                .OrderBy(p => price - p.Start.Level)
                .ThenByDescending(p => p.EndBar)
                .FirstOrDefault();
        }

        private LevelPair SelectHoldBelowPair()
        {
            double price = Close[0];

            return pairs
                .Where(p => !p.IsUpMove && p.IsConfirmed)
                .Where(p => p.Start != null && p.End != null)
                // A complete downtrend only becomes a PairBounds RL candidate after
                // its END level has been lost. The origin must still be acting as
                // resistance; otherwise it can no longer be the active hold-below level.
                .Where(p => p.End.Side == LevelSide.Resistance)
                .Where(p => p.Start.Side == LevelSide.Resistance)
                .Where(p => p.Start.Level > price - HalfTickTolerance)
                .Where(p => PairLookbackBars <= 0 || CurrentPrimaryBar - p.EndBar <= PairLookbackBars)
                // Exact inverse of SG: choose the nearest origin above current price.
                .OrderBy(p => p.Start.Level - price)
                .ThenByDescending(p => p.EndBar)
                .FirstOrDefault();
        }

        private double FindNearestLevel(LevelSide side, bool tested)
        {
            double price = Close[0];
            IEnumerable<LevelNode> candidates = nodesByKey.Values
                .Where(n => n.Side == side && n.Tested == tested);

            if (side == LevelSide.Support)
            {
                LevelNode result = candidates
                    .Where(n => n.Level <= price + HalfTickTolerance)
                    .OrderByDescending(n => n.Level)
                    .ThenByDescending(n => n.PivotBar)
                    .FirstOrDefault();

                return result == null ? double.NaN : result.Level;
            }

            LevelNode resistance = candidates
                .Where(n => n.Level >= price - HalfTickTolerance)
                .OrderBy(n => n.Level)
                .ThenByDescending(n => n.PivotBar)
                .FirstOrDefault();

            return resistance == null ? double.NaN : resistance.Level;
        }

        // Panel removed (Pattern J) — this only feeds the Strategy/DataBox plots now.
        private void UpdatePlots()
        {
            for (int i = 0; i < Values.Length; i++)
                Values[i][0] = double.NaN;

            LevelPair holdAbovePair = currentSgPair ?? SelectHoldAbovePair();
            LevelPair holdBelowPair = currentRlPair ?? SelectHoldBelowPair();

            if (holdAbovePair != null)
            {
                Values[PLOT_HOLD_ABOVE][0] = holdAbovePair.Start.Level;
                Values[PLOT_HOLD_ABOVE_STATE][0] = 2; // structural SG origin
            }

            if (holdBelowPair != null)
            {
                Values[PLOT_HOLD_BELOW][0] = holdBelowPair.Start.Level;
                Values[PLOT_HOLD_BELOW_STATE][0] = -2; // structural RL origin
            }

            Values[PLOT_NEAREST_G][0] = FindNearestLevel(LevelSide.Support, false);
            Values[PLOT_NEAREST_L][0] = FindNearestLevel(LevelSide.Resistance, false);
            Values[PLOT_NEAREST_SG][0] = FindNearestLevel(LevelSide.Support, true);
            Values[PLOT_NEAREST_RL][0] = FindNearestLevel(LevelSide.Resistance, true);

            // Mirror hosting series: the level BEING TESTED on this bar — the exact
            // price where the standalone indicator prints its triangle, reported for
            // the duration of the test signal (and its consecutive-wick sequence).
            // 0 = no test on this bar. This makes hosted Mirror levels coincide with
            // the standalone signals; earlier semantics (nearest-untested bands and
            // sticky SG/RL origins) were rejected as "floating" — context, not signals.
            double jLong = 0.0;
            double jShort = 0.0;
            int stateBar = lastProcessedPrimaryStateBar;

            if (stateBar >= 0)
            {
                foreach (LevelNode node in nodesByKey.Values)
                {
                    if (node.TestedBar != stateBar)
                        continue;

                    if (node.Side == LevelSide.Support && jLong == 0.0)
                        jLong = node.Level;
                    else if (node.Side == LevelSide.Resistance && jShort == 0.0)
                        jShort = node.Level;

                    if (jLong != 0.0 && jShort != 0.0)
                        break;
                }

                // V0006: nodes retired by same-bar structure churn still report the
                // level they were testing when the churn destroyed them.
                foreach (LevelNode node in retiredTestedNodes)
                {
                    if (node.TestedBar != stateBar)
                        continue;

                    if (node.Side == LevelSide.Support && jLong == 0.0)
                        jLong = node.Level;
                    else if (node.Side == LevelSide.Resistance && jShort == 0.0)
                        jShort = node.Level;
                }
            }

            if (MaxReportDistanceTicks > 0 && Tick > 0)
            {
                double maxDist = MaxReportDistanceTicks * Tick;
                if (jLong != 0.0 && Math.Abs(Close[0] - jLong) > maxDist)
                    jLong = 0.0;
                if (jShort != 0.0 && Math.Abs(jShort - Close[0]) > maxDist)
                    jShort = 0.0;
            }

            // Stamp the test on the bar it BELONGS to. In realtime the state engine
            // evaluates the just-CLOSED bar while this method runs on the forming
            // bar — writing to [0] here shifted live levels one HTF window right
            // (and duplicated them across reload seams). Historical: stateAgo == 0.
            Values[PLOT_J_LONG][0] = 0.0;
            Values[PLOT_J_SHORT][0] = 0.0;
            Values[PLOT_J_SIGNAL][0] = 0.0;

            int stateAgo = CurrentBar - stateBar;
            if (stateBar >= 0 && stateAgo >= 0 && stateAgo <= CurrentBar)
            {
                Values[PLOT_J_LONG][stateAgo] = jLong;
                Values[PLOT_J_SHORT][stateAgo] = jShort;
                // Pattern A-style signal plot: +1 long test, -1 short test (each
                // sequence bar restamps, so consecutive wick bars all signal).
                Values[PLOT_J_SIGNAL][stateAgo] = jLong != 0.0 ? 1.0 : (jShort != 0.0 ? -1.0 : 0.0);
            }

            // V0006: back-stamp tests discovered by a late pair confirmation at the
            // bar they actually happened on (a hosting Mirror scans a few closed HTF
            // bars, so these still reach it). Never overwrite a value already there.
            if (pendingRetroReports.Count > 0)
            {
                foreach (RetroReport rr in pendingRetroReports)
                {
                    int ago = CurrentBar - rr.Bar;
                    if (ago < 1 || ago > CurrentBar || ago > 10)
                        continue;

                    Series<double> target = rr.IsShort ? Values[PLOT_J_SHORT] : Values[PLOT_J_LONG];
                    double existing = target.IsValidDataPoint(ago) ? target[ago] : 0.0;
                    if (existing != 0.0)
                        continue;
                    target[ago] = rr.Level;

                    double l = Values[PLOT_J_LONG].IsValidDataPoint(ago) ? Values[PLOT_J_LONG][ago] : 0.0;
                    double s = Values[PLOT_J_SHORT].IsValidDataPoint(ago) ? Values[PLOT_J_SHORT][ago] : 0.0;
                    Values[PLOT_J_SIGNAL][ago] = l != 0.0 ? 1.0 : (s != 0.0 ? -1.0 : 0.0);
                }
                pendingRetroReports.Clear();
            }

            // Realtime PROVISIONAL: evaluate the FORMING bar against armed and
            // sequence-continuing levels on every tick, so a hosting Mirror can draw
            // the J level live during the HTF bar. The value tracks the forming bar
            // tick by tick — it clears if the test condition stops holding, and the
            // closed-bar state engine delivers the confirmed value at the HTF close
            // (standard Mirror-family provisional behavior).
            if (State == State.Realtime && CurrentBar >= 1)
            {
                double liveLong = 0.0;
                double liveShort = 0.0;
                double breakTol = GainLossToleranceTicks * Tick;
                double touchTol = TestTouchToleranceTicks * Tick;
                double holdTol = TestCloseHoldTicks * Tick;

                foreach (LevelNode node in nodesByKey.Values)
                {
                    if (node.Side == LevelSide.Support)
                    {
                        bool broken = UseWicksForGainLoss
                            ? Low[0] < node.Level - breakTol
                            : Close[0] < node.Level - breakTol;
                        if (broken)
                            continue;

                        bool pass = false;
                        if (!node.Tested && CurrentBar > node.StateChangedBar)
                        {
                            pass = Close[1] > node.Level + HalfTickTolerance
                                && Low[0] <= node.Level + touchTol
                                && Close[0] >= node.Level + holdTol;
                        }
                        else if (node.Tested && node.LastSignalBar >= 0 && CurrentBar == node.LastSignalBar + 1)
                        {
                            pass = Low[0] <= node.Level + touchTol
                                && Math.Min(Open[0], Close[0]) >= node.Level - HalfTickTolerance
                                && Close[0] >= node.Level + holdTol;
                        }

                        if (pass && liveLong == 0.0)
                            liveLong = node.Level;
                    }
                    else if (node.Side == LevelSide.Resistance)
                    {
                        bool broken = UseWicksForGainLoss
                            ? High[0] > node.Level + breakTol
                            : Close[0] > node.Level + breakTol;
                        if (broken)
                            continue;

                        bool pass = false;
                        if (!node.Tested && CurrentBar > node.StateChangedBar)
                        {
                            pass = Close[1] < node.Level - HalfTickTolerance
                                && High[0] >= node.Level - touchTol
                                && Close[0] <= node.Level - holdTol;
                        }
                        else if (node.Tested && node.LastSignalBar >= 0 && CurrentBar == node.LastSignalBar + 1)
                        {
                            pass = High[0] >= node.Level - touchTol
                                && Math.Max(Open[0], Close[0]) <= node.Level + HalfTickTolerance
                                && Close[0] <= node.Level - holdTol;
                        }

                        if (pass && liveShort == 0.0)
                            liveShort = node.Level;
                    }
                }

                // V0006 developing-leg preview: with Confirmed Pairs Only, the newest
                // zigzag leg's endpoint has NO node until the next opposite pivot
                // confirms the pair — and for hard rejections that pivot arrives WITH
                // the test bar's close, so the level never showed live (7/20 09:50
                // J10@29127). The pivot and its guide price DO exist already; preview
                // it as a phantom first test of the forming bar. The preview clears
                // tick-by-tick like any provisional value, and can shift if the pivot
                // is replaced intrabar — standard provisional semantics.
                if (UseConfirmedPairsOnly && (liveLong == 0.0 || liveShort == 0.0) && pivotBars.Count >= 2)
                {
                    int lastPivot = pivotBars.Count - 1;
                    double lvl = GetSelectedPivotLevel(lastPivot);
                    if (!double.IsNaN(lvl) && Tick > 0)
                    {
                        lvl = Normalize(lvl);
                        int legStartBar = pivotBars[lastPivot - 1];
                        int legEndBar = pivotBars[lastPivot];

                        bool qualifies = legEndBar - legStartBar >= Math.Max(1, MinimumTrendBars)
                            && !(Math.Abs(pivotHLPrices[lastPivot] - pivotHLPrices[lastPivot - 1]) / Tick + 1e-9
                                 < Math.Max(0, MinimumTrendTicks));

                        int pivotAgo = CurrentBar - legEndBar;
                        int lastClosed = CurrentBar - 1;

                        if (qualifies && pivotAgo >= 1 && pivotAgo <= CurrentBar
                            && lastClosed - legEndBar <= 100)
                        {
                            // Establish the phantom's current side/tested state by
                            // walking the closed bars since the pivot with the same
                            // gain/loss/first-test rules the node engine applies.
                            double closeAtPivot = Close[pivotAgo];
                            LevelSide side = closeAtPivot > lvl + HalfTickTolerance ? LevelSide.Support
                                : (closeAtPivot < lvl - HalfTickTolerance ? LevelSide.Resistance
                                : (pivotIsHigh[lastPivot] ? LevelSide.Resistance : LevelSide.Support));
                            bool tested = false;
                            int stateChangedBar = legEndBar;

                            for (int b = legEndBar + 1; b <= lastClosed; b++)
                            {
                                int ago = CurrentBar - b;
                                double h = High[ago], lo = Low[ago], c = Close[ago], pc = Close[ago + 1];

                                if (side == LevelSide.Support)
                                {
                                    bool lost = UseWicksForGainLoss ? lo < lvl - breakTol : c < lvl - breakTol;
                                    if (lost) { side = LevelSide.Resistance; tested = false; stateChangedBar = b; continue; }
                                    if (!tested && b > stateChangedBar
                                        && pc > lvl + HalfTickTolerance && lo <= lvl + touchTol && c >= lvl + holdTol)
                                        tested = true;
                                }
                                else
                                {
                                    bool gained = UseWicksForGainLoss ? h > lvl + breakTol : c > lvl + breakTol;
                                    if (gained) { side = LevelSide.Support; tested = false; stateChangedBar = b; continue; }
                                    if (!tested && b > stateChangedBar
                                        && pc < lvl - HalfTickTolerance && h >= lvl - touchTol && c <= lvl - holdTol)
                                        tested = true;
                                }
                            }

                            // First test of the FORMING bar against the phantom level.
                            if (!tested && CurrentBar > stateChangedBar)
                            {
                                if (side == LevelSide.Support && liveLong == 0.0)
                                {
                                    bool broken = UseWicksForGainLoss ? Low[0] < lvl - breakTol : Close[0] < lvl - breakTol;
                                    if (!broken && Close[1] > lvl + HalfTickTolerance
                                        && Low[0] <= lvl + touchTol && Close[0] >= lvl + holdTol)
                                        liveLong = lvl;
                                }
                                else if (side == LevelSide.Resistance && liveShort == 0.0)
                                {
                                    bool broken = UseWicksForGainLoss ? High[0] > lvl + breakTol : Close[0] > lvl + breakTol;
                                    if (!broken && Close[1] < lvl - HalfTickTolerance
                                        && High[0] >= lvl - touchTol && Close[0] <= lvl - holdTol)
                                        liveShort = lvl;
                                }
                            }
                        }
                    }
                }

                if (MaxReportDistanceTicks > 0 && Tick > 0)
                {
                    double maxDist = MaxReportDistanceTicks * Tick;
                    if (liveLong != 0.0 && Math.Abs(Close[0] - liveLong) > maxDist)
                        liveLong = 0.0;
                    if (liveShort != 0.0 && Math.Abs(liveShort - Close[0]) > maxDist)
                        liveShort = 0.0;
                }

                Values[PLOT_J_LONG][0] = liveLong;
                Values[PLOT_J_SHORT][0] = liveShort;
                Values[PLOT_J_SIGNAL][0] = liveLong != 0.0 ? 1.0 : (liveShort != 0.0 ? -1.0 : 0.0);
            }
        }

        private void DrawOptionalZigZag()
        {
            if (!ShowZigZag)
            {
                foreach (string tag in zigZagDrawTags)
                    RemoveDrawObject(tag);

                zigZagDrawTags.Clear();
                return;
            }

            HashSet<string> tagsDrawnNow = new HashSet<string>();

            for (int i = 1; i < pivotBars.Count; i++)
            {
                string tag = "PB_ZZ_" + instanceId + "_" + i;
                DrawLinePrimaryBars(tag,
                    pivotBars[i - 1], pivotHLPrices[i - 1],
                    pivotBars[i], pivotHLPrices[i],
                    zigZagBrush, DashStyleHelper.Solid, ZigZagLineWidth);

                tagsDrawnNow.Add(tag);
            }

            foreach (string oldTag in zigZagDrawTags.ToList())
            {
                if (!tagsDrawnNow.Contains(oldTag))
                    RemoveDrawObject(oldTag);
            }

            zigZagDrawTags.Clear();
            foreach (string tag in tagsDrawnNow)
                zigZagDrawTags.Add(tag);
        }

        private void ProcessPrimaryStructureBar(int currentBarsAgo, int previousBarsAgo, int primaryBarIndex)
        {
            if (primaryBarIndex < 0 || primaryBarIndex == lastProcessedPrimaryStructureBar)
                return;

            if (currentBarsAgo < 0 || previousBarsAgo < 0 || previousBarsAgo > CurrentBar)
                return;

            bool previousGreen = Close[previousBarsAgo] >= Open[previousBarsAgo];
            bool previousRed = Close[previousBarsAgo] < Open[previousBarsAgo];
            bool currentGreen = Close[currentBarsAgo] >= Open[currentBarsAgo];
            bool currentRed = Close[currentBarsAgo] < Open[currentBarsAgo];
            bool isHigh = High[currentBarsAgo] > High[previousBarsAgo];
            bool isLow = Low[currentBarsAgo] < Low[previousBarsAgo];
            double bodyHigh = Math.Max(Open[currentBarsAgo], Close[currentBarsAgo]);
            double bodyLow = Math.Min(Open[currentBarsAgo], Close[currentBarsAgo]);

            if (previousGreen && currentGreen && isHigh)
                ProcessPivotPrimary(primaryBarIndex, High[currentBarsAgo], true, bodyHigh, bodyLow);
            if (previousRed && currentRed && isLow)
                ProcessPivotPrimary(primaryBarIndex, Low[currentBarsAgo], false, bodyHigh, bodyLow);
            if (previousGreen && currentRed && isHigh)
                ProcessPivotPrimary(primaryBarIndex, High[currentBarsAgo], true, bodyHigh, bodyLow);
            if (previousRed && currentGreen && isLow)
                ProcessPivotPrimary(primaryBarIndex, Low[currentBarsAgo], false, bodyHigh, bodyLow);
            if (previousRed && currentGreen && isHigh)
                ProcessPivotPrimary(primaryBarIndex, High[currentBarsAgo], true, bodyHigh, bodyLow);
            if (previousGreen && currentRed && isLow)
                ProcessPivotPrimary(primaryBarIndex, Low[currentBarsAgo], false, bodyHigh, bodyLow);

            DetectLegacyGainLossEvents(Close[currentBarsAgo], Close[previousBarsAgo], primaryBarIndex);
            lastProcessedPrimaryStructureBar = primaryBarIndex;
        }

        private int GetClosedPrimaryBarForStateProcessing()
        {
            if (State == State.Realtime)
            {
                if (!IsFirstTickOfBar || CurrentPrimaryBar < 1)
                    return -1;

                return CurrentPrimaryBar - 1;
            }

            return CurrentPrimaryBar;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AlightenMirrorPtJV0006";
                Description = "Pattern J v3 — v2 (qualified pairs, body levels, triangle tests, bounded signal-level stubs) plus SEQUENTIAL signals: after a test, each consecutive bar that true-WICKS the level (low pokes the tolerance, body holds the correct side) signals again; one non-wick bar ends the sequence; a flip removes the sequence markers like v2 removed its dot.";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;
                MaximumBarsLookBack = MaximumBarsLookBack.Infinite;

                BarsToProcess = 0;
                CalcOnlyMode = false;
                MaxReportDistanceTicks = 0;
                RemoveSignalsOnFlip = true;
                UseConfirmedPairsOnly = true;
                UseWicksForPairLevels = false;
                UseWicksForGainLoss = false;
                MinimumTrendBars = 1;
                MinimumTrendTicks = 0;
                ShowTestedPairLevels = true;

                NearestPairLineWidth = 3;
                PairLineWidthStep = 1;
                UntestedLineStyle = DashStyleHelper.Solid;
                TestedLineStyle = DashStyleHelper.Dot;
                UptrendPairsToDraw = 3;
                DowntrendPairsToDraw = 3;
                PairLookbackBars = 2000;
                MaxStoredPivots = 300;

                UptrendPairColor = Colors.LimeGreen;
                DowntrendPairColor = Colors.Magenta;
                SGPairColor = Colors.Goldenrod;
                RLPairColor = Colors.Red;

                GainLossToleranceTicks = 0;
                TestTouchToleranceTicks = 0;
                TestCloseHoldTicks = 0;
                ShowTestDots = true;
                SupportTestDotColor = Colors.LimeGreen;
                ResistanceTestDotColor = Colors.Red;

                ShowLegacyGainedLostLevels = true;
                LegacyNumberOfLevels = 3;
                LegacyExtendRightBars = 500;
                LegacyMaxActiveLines = 50;
                LegacyGainLineWidth = 2;
                LegacyLossLineWidth = 2;
                LegacyGainLineColor = Colors.Transparent;
                LegacyLossLineColor = Colors.Transparent;
                LegacyGainTestLineColor = Colors.Transparent;
                LegacyLossTestLineColor = Colors.Transparent;
                LegacyCloseThroughAction = "Delete";
                ShowLegacyFirstTouchDots = false;
                LegacyGainDotColor = Colors.Transparent;
                LegacyLossDotColor = Colors.Transparent;

                ShowPairLevelLabels = false;
                LabelBarsRight = 2;
                LabelPixelOffset = 0;
                LabelFontSize = 11;

                PairBoundsPairLineWidth = 4;

                ShowZigZag = false;
                ZigZagColor = Colors.Gold;
                ZigZagLineWidth = 2;

                AddPlot(Brushes.Transparent, "PairBoundsHoldAbove");
                AddPlot(Brushes.Transparent, "PairBoundsHoldBelow");
                AddPlot(Brushes.Transparent, "PairBoundsHoldAboveState");
                AddPlot(Brushes.Transparent, "PairBoundsHoldBelowState");
                AddPlot(Brushes.Transparent, "NearestG");
                AddPlot(Brushes.Transparent, "NearestL");
                AddPlot(Brushes.Transparent, "NearestSG");
                AddPlot(Brushes.Transparent, "NearestRL");
                AddPlot(Brushes.Transparent, "PatternJLongLevel");
                AddPlot(Brushes.Transparent, "PatternJShortLevel");
                AddPlot(Brushes.Transparent, "PatternJSignal");
            }
            else if (State == State.Configure)
            {
                instanceId = Guid.NewGuid().ToString("N");
            }
            else if (State == State.DataLoaded)
            {
                zigZagBrush = MakeFrozenBrush(ZigZagColor);
                uptrendPairBrush = MakeFrozenBrush(UptrendPairColor);
                downtrendPairBrush = MakeFrozenBrush(DowntrendPairColor);
                sgPairBrush = MakeFrozenBrush(SGPairColor);
                rlPairBrush = MakeFrozenBrush(RLPairColor);

                supportDotBrush = MakeFrozenBrush(SupportTestDotColor);
                resistanceDotBrush = MakeFrozenBrush(ResistanceTestDotColor);
                legacyGainBrush = MakeFrozenBrush(LegacyGainLineColor);
                legacyLossBrush = MakeFrozenBrush(LegacyLossLineColor);
                legacyGainTestBrush = MakeFrozenBrush(LegacyGainTestLineColor);
                legacyLossTestBrush = MakeFrozenBrush(LegacyLossTestLineColor);
                legacyGainDotBrush = MakeFrozenBrush(LegacyGainDotColor);
                legacyLossDotBrush = MakeFrozenBrush(LegacyLossDotColor);
            }
            else if (State == State.Terminated)
            {
                RemoveAllPairDrawings();
                RemoveAllNodeDrawings();
                for (int i = legacyEvents.Count - 1; i >= 0; i--)
                {
                    RemoveDrawObject(legacyEvents[i].TagLine);
                    RemoveDrawObject(legacyEvents[i].DotTag);
                }
                legacyEvents.Clear();
                foreach (string tag in zigZagDrawTags)
                    RemoveDrawObject(tag);
                zigZagDrawTags.Clear();

                pivotBars.Clear();
                pivotHLPrices.Clear();
                pivotIsHigh.Clear();
                guidePrices.Clear();
                nodesByKey.Clear();
                pairs.Clear();
                retiredTestedNodes.Clear();
                structureBarInProcess = -1;
                pendingRetroReports.Clear();
                currentSgPair = null;
                currentRlPair = null;
            }
        }

        protected override void OnBarUpdate()
        {
            if (Tick <= 0 || CurrentBars[0] < 2)
                return;

            if (BarsInProgress != 0)
                return;

            // Bars To Process: skip bars before the processing window (0 = all).
            if (BarsToProcess > 0 && CurrentBar < Count - BarsToProcess)
                return;

            // Pattern J: swing construction always runs on completed CHART-SERIES bars
            // (the MTF option was removed).
            if (State == State.Realtime)
            {
                if (IsFirstTickOfBar && CurrentBar >= 2)
                    ProcessPrimaryStructureBar(1, 2, CurrentBar - 1);
            }
            else
            {
                ProcessPrimaryStructureBar(0, 1, CurrentBar);
            }

            // G/L flips and successful tests stay bar-close based. Pair selection,
            // ranking, widths, labels, and plots update each tick.
            int closedPrimaryBar = GetClosedPrimaryBarForStateProcessing();
            if (closedPrimaryBar > lastProcessedPrimaryStateBar)
            {
                ProcessLegacyEventsAtAbsoluteBar(closedPrimaryBar);
                ProcessAllNodesAtAbsoluteBar(closedPrimaryBar);
                lastProcessedPrimaryStateBar = closedPrimaryBar;
            }

            AssignPairRanksAndVisibility();
            if (!CalcOnlyMode)
            {
                DrawPairs();
                DrawTestedLevels();
                DrawLevelLabels();
                DrawOptionalZigZag();
            }
            UpdatePlots();
        }

        #region Inputs
        [NinjaScriptProperty]
        [Range(0, 500000)]
        [Display(Name = "Bars To Process (0 = all)", Description = "Only process the last N bars of the series (like the other Mirror pattern sources; hosts pass Src Bars To Process).", GroupName = "02. Pair Structure", Order = 15)]
        public int BarsToProcess { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Confirmed Pairs Only", GroupName = "02. Pair Structure", Order = 0)]
        public bool UseConfirmedPairsOnly { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Wicks For Pair Levels", GroupName = "02. Pair Structure", Order = 1)]
        public bool UseWicksForPairLevels { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Nearest Pair Line Width", Description = "Line width used by the nearest uptrend pair and the nearest downtrend pair.", GroupName = "02. Pair Structure", Order = 4)]
        public int NearestPairLineWidth { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Pair Width Step", Description = "Amount subtracted from line width for each pair farther from current price. Width never falls below 1.", GroupName = "02. Pair Structure", Order = 5)]
        public int PairLineWidthStep { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Untested Line Style", GroupName = "02. Pair Structure", Order = 7)]
        public DashStyleHelper UntestedLineStyle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tested Line Style", GroupName = "02. Pair Structure", Order = 8)]
        public DashStyleHelper TestedLineStyle { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Uptrend Pairs To Draw", Description = "Number of nearest uptrend pairs ranked by their price distance from current price.", GroupName = "02. Pair Structure", Order = 9)]
        public int UptrendPairsToDraw { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Downtrend Pairs To Draw", Description = "Number of nearest downtrend pairs ranked by their price distance from current price.", GroupName = "02. Pair Structure", Order = 10)]
        public int DowntrendPairsToDraw { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Pair Lookback Bars (0=Unlimited)", GroupName = "02. Pair Structure", Order = 11)]
        public int PairLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(20, 2000)]
        [Display(Name = "Max Stored Pivots", GroupName = "02. Pair Structure", Order = 12)]
        public int MaxStoredPivots { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Minimum Trend Bars", Description = "A pivot leg must span at least this many bars to qualify as a pair (Patch-style trend qualification; 1 = every leg qualifies).", GroupName = "02. Pair Structure", Order = 13)]
        public int MinimumTrendBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Minimum Trend Movement (ticks)", Description = "A pivot leg must move at least this many ticks wick-to-wick to qualify as a pair (0 = off).", GroupName = "02. Pair Structure", Order = 14)]
        public int MinimumTrendTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Uptrend Pair Color", GroupName = "03. Pair Colors", Order = 0)]
        public Color UptrendPairColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Downtrend Pair Color", GroupName = "03. Pair Colors", Order = 1)]
        public Color DowntrendPairColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SG Pair Color", Description = "Color for the complete uptrend pair whose start is the active PairBounds hold-above level.", GroupName = "03. Pair Colors", Order = 2)]
        public Color SGPairColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "RL Pair Color", Description = "Color for the complete downtrend pair whose start is the active PairBounds hold-below level.", GroupName = "03. Pair Colors", Order = 3)]
        public Color RLPairColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Wicks For Gains/Losses", GroupName = "04. G/L and Tests", Order = 0)]
        public bool UseWicksForGainLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Gain/Loss Tolerance (ticks)", GroupName = "04. G/L and Tests", Order = 1)]
        public int GainLossToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Test Touch Tolerance (ticks)", GroupName = "04. G/L and Tests", Order = 2)]
        public int TestTouchToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Test Close Hold (ticks)", GroupName = "04. G/L and Tests", Order = 3)]
        public int TestCloseHoldTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Tested Levels", Description = "Draw the level that created each test signal as a bounded segment from its pivot bar to the test triangle (removed when the level flips sides).", GroupName = "04. G/L and Tests", Order = 7)]
        public bool ShowTestedPairLevels { get; set; }

        [Display(Name = "Show Test Dots", GroupName = "04. G/L and Tests", Order = 4)]
        public bool ShowTestDots { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Support Test Dot Color", GroupName = "04. G/L and Tests", Order = 5)]
        public Color SupportTestDotColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Resistance Test Dot Color", GroupName = "04. G/L and Tests", Order = 6)]
        public Color ResistanceTestDotColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Legacy Gained/Lost Levels", Description = "Displays the original unpaired gained/lost event levels. Leave enabled with Transparent colors to preserve them invisibly for later use.", GroupName = "05. Legacy Gained/Lost", Order = 0)]
        public bool ShowLegacyGainedLostLevels { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Number Of Recent Guide Levels", GroupName = "05. Legacy Gained/Lost", Order = 1)]
        public int LegacyNumberOfLevels { get; set; }

        [NinjaScriptProperty]
        [Range(10, 5000)]
        [Display(Name = "Extend Right (Bars)", GroupName = "05. Legacy Gained/Lost", Order = 2)]
        public int LegacyExtendRightBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Max Active Lines", GroupName = "05. Legacy Gained/Lost", Order = 3)]
        public int LegacyMaxActiveLines { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Gain Line Width", GroupName = "05. Legacy Gained/Lost", Order = 4)]
        public int LegacyGainLineWidth { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Loss Line Width", GroupName = "05. Legacy Gained/Lost", Order = 5)]
        public int LegacyLossLineWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Gain Line Color", GroupName = "05. Legacy Gained/Lost", Order = 6)]
        public Color LegacyGainLineColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Loss Line Color", GroupName = "05. Legacy Gained/Lost", Order = 7)]
        public Color LegacyLossLineColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Gain Tested Color", GroupName = "05. Legacy Gained/Lost", Order = 8)]
        public Color LegacyGainTestLineColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Loss Tested Color", GroupName = "05. Legacy Gained/Lost", Order = 9)]
        public Color LegacyLossTestLineColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close-Through Action", GroupName = "05. Legacy Gained/Lost", Order = 10)]
        [TypeConverter(typeof(LegacyCloseThroughActionConverter))]
        public string LegacyCloseThroughAction { get; set; }

        public class LegacyCloseThroughActionConverter : TypeConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                return new StandardValuesCollection(new[] { "Delete", "StopExtending" });
            }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show First-Touch Dots", GroupName = "05. Legacy Gained/Lost", Order = 11)]
        public bool ShowLegacyFirstTouchDots { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Gain First-Touch Dot Color", GroupName = "05. Legacy Gained/Lost", Order = 12)]
        public Color LegacyGainDotColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Loss First-Touch Dot Color", GroupName = "05. Legacy Gained/Lost", Order = 13)]
        public Color LegacyLossDotColor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Pair Level Labels", GroupName = "06. Labels", Order = 0)]
        public bool ShowPairLevelLabels { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Label Bars Right", GroupName = "06. Labels", Order = 1)]
        public int LabelBarsRight { get; set; }

        [NinjaScriptProperty]
        [Range(-100, 100)]
        [Display(Name = "Label Pixel Offset", GroupName = "06. Labels", Order = 2)]
        public int LabelPixelOffset { get; set; }

        [NinjaScriptProperty]
        [Range(6, 30)]
        [Display(Name = "Label Font Size", GroupName = "06. Labels", Order = 3)]
        public int LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "SG/RL Pair Line Width", Description = "Line width used for the active SG and RL source pairs. These pairs use their own colors and draw on top of regular pairs.", GroupName = "03. Pair Colors", Order = 4)]
        public int PairBoundsPairLineWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show ZigZag", GroupName = "08. ZigZag", Order = 0)]
        public bool ShowZigZag { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ZigZag Color", GroupName = "08. ZigZag", Order = 1)]
        public Color ZigZagColor { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "ZigZag Line Width", GroupName = "08. ZigZag", Order = 2)]
        public int ZigZagLineWidth { get; set; }

        // Calc-only hosting mode (LAST ctor parameter): the Mirror host reads the
        // PatternJLongLevel/PatternJShortLevel series and draws levels itself, so the
        // hosted child must not draw anything on the host chart.
        [NinjaScriptProperty]
        [Display(Name = "Calc Only Mode (hosted)", Description = "Suppress ALL drawing — for hosting by the Mirror family, which reads the PatternJ level series and draws its own levels.", GroupName = "02. Pair Structure", Order = 16)]
        public bool CalcOnlyMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Max Reported Level Distance (ticks, 0 = off)", Description = "The PatternJ level series report 0 (none) when the SG/RL origin sits farther than this many ticks from the current close — keeps hosted Mirror levels from floating far from price on old structure.", GroupName = "02. Pair Structure", Order = 17)]
        public int MaxReportDistanceTicks { get; set; }

        // Display-only (not a ctor parameter): whether a level flipping sides erases
        // its past test triangles. On = validity-check behavior (a broken level
        // invalidates its signals); off = signals persist as history, Pattern A style.
        [Display(Name = "Remove Signals When Level Flips", Description = "When a tested level flips sides, remove its test triangles (validity check). Untick to keep them as permanent signal history.", GroupName = "04. G/L and Tests", Order = 8)]
        public bool RemoveSignalsOnFlip { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenMirrorPtJV0006[] cacheAlightenMirrorPtJV0006;
		public AlightenMirrorPtJV0006 AlightenMirrorPtJV0006(int barsToProcess, bool useConfirmedPairsOnly, bool useWicksForPairLevels, int nearestPairLineWidth, int pairLineWidthStep, DashStyleHelper untestedLineStyle, DashStyleHelper testedLineStyle, int uptrendPairsToDraw, int downtrendPairsToDraw, int pairLookbackBars, int maxStoredPivots, int minimumTrendBars, int minimumTrendTicks, Color uptrendPairColor, Color downtrendPairColor, Color sGPairColor, Color rLPairColor, bool useWicksForGainLoss, int gainLossToleranceTicks, int testTouchToleranceTicks, int testCloseHoldTicks, bool showTestedPairLevels, Color supportTestDotColor, Color resistanceTestDotColor, bool showLegacyGainedLostLevels, int legacyNumberOfLevels, int legacyExtendRightBars, int legacyMaxActiveLines, int legacyGainLineWidth, int legacyLossLineWidth, Color legacyGainLineColor, Color legacyLossLineColor, Color legacyGainTestLineColor, Color legacyLossTestLineColor, string legacyCloseThroughAction, bool showLegacyFirstTouchDots, Color legacyGainDotColor, Color legacyLossDotColor, bool showPairLevelLabels, int labelBarsRight, int labelPixelOffset, int labelFontSize, int pairBoundsPairLineWidth, bool showZigZag, Color zigZagColor, int zigZagLineWidth, bool calcOnlyMode, int maxReportDistanceTicks)
		{
			return AlightenMirrorPtJV0006(Input, barsToProcess, useConfirmedPairsOnly, useWicksForPairLevels, nearestPairLineWidth, pairLineWidthStep, untestedLineStyle, testedLineStyle, uptrendPairsToDraw, downtrendPairsToDraw, pairLookbackBars, maxStoredPivots, minimumTrendBars, minimumTrendTicks, uptrendPairColor, downtrendPairColor, sGPairColor, rLPairColor, useWicksForGainLoss, gainLossToleranceTicks, testTouchToleranceTicks, testCloseHoldTicks, showTestedPairLevels, supportTestDotColor, resistanceTestDotColor, showLegacyGainedLostLevels, legacyNumberOfLevels, legacyExtendRightBars, legacyMaxActiveLines, legacyGainLineWidth, legacyLossLineWidth, legacyGainLineColor, legacyLossLineColor, legacyGainTestLineColor, legacyLossTestLineColor, legacyCloseThroughAction, showLegacyFirstTouchDots, legacyGainDotColor, legacyLossDotColor, showPairLevelLabels, labelBarsRight, labelPixelOffset, labelFontSize, pairBoundsPairLineWidth, showZigZag, zigZagColor, zigZagLineWidth, calcOnlyMode, maxReportDistanceTicks);
		}

		public AlightenMirrorPtJV0006 AlightenMirrorPtJV0006(ISeries<double> input, int barsToProcess, bool useConfirmedPairsOnly, bool useWicksForPairLevels, int nearestPairLineWidth, int pairLineWidthStep, DashStyleHelper untestedLineStyle, DashStyleHelper testedLineStyle, int uptrendPairsToDraw, int downtrendPairsToDraw, int pairLookbackBars, int maxStoredPivots, int minimumTrendBars, int minimumTrendTicks, Color uptrendPairColor, Color downtrendPairColor, Color sGPairColor, Color rLPairColor, bool useWicksForGainLoss, int gainLossToleranceTicks, int testTouchToleranceTicks, int testCloseHoldTicks, bool showTestedPairLevels, Color supportTestDotColor, Color resistanceTestDotColor, bool showLegacyGainedLostLevels, int legacyNumberOfLevels, int legacyExtendRightBars, int legacyMaxActiveLines, int legacyGainLineWidth, int legacyLossLineWidth, Color legacyGainLineColor, Color legacyLossLineColor, Color legacyGainTestLineColor, Color legacyLossTestLineColor, string legacyCloseThroughAction, bool showLegacyFirstTouchDots, Color legacyGainDotColor, Color legacyLossDotColor, bool showPairLevelLabels, int labelBarsRight, int labelPixelOffset, int labelFontSize, int pairBoundsPairLineWidth, bool showZigZag, Color zigZagColor, int zigZagLineWidth, bool calcOnlyMode, int maxReportDistanceTicks)
		{
			if (cacheAlightenMirrorPtJV0006 != null)
				for (int idx = 0; idx < cacheAlightenMirrorPtJV0006.Length; idx++)
					if (cacheAlightenMirrorPtJV0006[idx] != null && cacheAlightenMirrorPtJV0006[idx].BarsToProcess == barsToProcess && cacheAlightenMirrorPtJV0006[idx].UseConfirmedPairsOnly == useConfirmedPairsOnly && cacheAlightenMirrorPtJV0006[idx].UseWicksForPairLevels == useWicksForPairLevels && cacheAlightenMirrorPtJV0006[idx].NearestPairLineWidth == nearestPairLineWidth && cacheAlightenMirrorPtJV0006[idx].PairLineWidthStep == pairLineWidthStep && cacheAlightenMirrorPtJV0006[idx].UntestedLineStyle == untestedLineStyle && cacheAlightenMirrorPtJV0006[idx].TestedLineStyle == testedLineStyle && cacheAlightenMirrorPtJV0006[idx].UptrendPairsToDraw == uptrendPairsToDraw && cacheAlightenMirrorPtJV0006[idx].DowntrendPairsToDraw == downtrendPairsToDraw && cacheAlightenMirrorPtJV0006[idx].PairLookbackBars == pairLookbackBars && cacheAlightenMirrorPtJV0006[idx].MaxStoredPivots == maxStoredPivots && cacheAlightenMirrorPtJV0006[idx].MinimumTrendBars == minimumTrendBars && cacheAlightenMirrorPtJV0006[idx].MinimumTrendTicks == minimumTrendTicks && cacheAlightenMirrorPtJV0006[idx].UptrendPairColor == uptrendPairColor && cacheAlightenMirrorPtJV0006[idx].DowntrendPairColor == downtrendPairColor && cacheAlightenMirrorPtJV0006[idx].SGPairColor == sGPairColor && cacheAlightenMirrorPtJV0006[idx].RLPairColor == rLPairColor && cacheAlightenMirrorPtJV0006[idx].UseWicksForGainLoss == useWicksForGainLoss && cacheAlightenMirrorPtJV0006[idx].GainLossToleranceTicks == gainLossToleranceTicks && cacheAlightenMirrorPtJV0006[idx].TestTouchToleranceTicks == testTouchToleranceTicks && cacheAlightenMirrorPtJV0006[idx].TestCloseHoldTicks == testCloseHoldTicks && cacheAlightenMirrorPtJV0006[idx].ShowTestedPairLevels == showTestedPairLevels && cacheAlightenMirrorPtJV0006[idx].SupportTestDotColor == supportTestDotColor && cacheAlightenMirrorPtJV0006[idx].ResistanceTestDotColor == resistanceTestDotColor && cacheAlightenMirrorPtJV0006[idx].ShowLegacyGainedLostLevels == showLegacyGainedLostLevels && cacheAlightenMirrorPtJV0006[idx].LegacyNumberOfLevels == legacyNumberOfLevels && cacheAlightenMirrorPtJV0006[idx].LegacyExtendRightBars == legacyExtendRightBars && cacheAlightenMirrorPtJV0006[idx].LegacyMaxActiveLines == legacyMaxActiveLines && cacheAlightenMirrorPtJV0006[idx].LegacyGainLineWidth == legacyGainLineWidth && cacheAlightenMirrorPtJV0006[idx].LegacyLossLineWidth == legacyLossLineWidth && cacheAlightenMirrorPtJV0006[idx].LegacyGainLineColor == legacyGainLineColor && cacheAlightenMirrorPtJV0006[idx].LegacyLossLineColor == legacyLossLineColor && cacheAlightenMirrorPtJV0006[idx].LegacyGainTestLineColor == legacyGainTestLineColor && cacheAlightenMirrorPtJV0006[idx].LegacyLossTestLineColor == legacyLossTestLineColor && cacheAlightenMirrorPtJV0006[idx].LegacyCloseThroughAction == legacyCloseThroughAction && cacheAlightenMirrorPtJV0006[idx].ShowLegacyFirstTouchDots == showLegacyFirstTouchDots && cacheAlightenMirrorPtJV0006[idx].LegacyGainDotColor == legacyGainDotColor && cacheAlightenMirrorPtJV0006[idx].LegacyLossDotColor == legacyLossDotColor && cacheAlightenMirrorPtJV0006[idx].ShowPairLevelLabels == showPairLevelLabels && cacheAlightenMirrorPtJV0006[idx].LabelBarsRight == labelBarsRight && cacheAlightenMirrorPtJV0006[idx].LabelPixelOffset == labelPixelOffset && cacheAlightenMirrorPtJV0006[idx].LabelFontSize == labelFontSize && cacheAlightenMirrorPtJV0006[idx].PairBoundsPairLineWidth == pairBoundsPairLineWidth && cacheAlightenMirrorPtJV0006[idx].ShowZigZag == showZigZag && cacheAlightenMirrorPtJV0006[idx].ZigZagColor == zigZagColor && cacheAlightenMirrorPtJV0006[idx].ZigZagLineWidth == zigZagLineWidth && cacheAlightenMirrorPtJV0006[idx].CalcOnlyMode == calcOnlyMode && cacheAlightenMirrorPtJV0006[idx].MaxReportDistanceTicks == maxReportDistanceTicks && cacheAlightenMirrorPtJV0006[idx].EqualsInput(input))
						return cacheAlightenMirrorPtJV0006[idx];
			return CacheIndicator<AlightenMirrorPtJV0006>(new AlightenMirrorPtJV0006(){ BarsToProcess = barsToProcess, UseConfirmedPairsOnly = useConfirmedPairsOnly, UseWicksForPairLevels = useWicksForPairLevels, NearestPairLineWidth = nearestPairLineWidth, PairLineWidthStep = pairLineWidthStep, UntestedLineStyle = untestedLineStyle, TestedLineStyle = testedLineStyle, UptrendPairsToDraw = uptrendPairsToDraw, DowntrendPairsToDraw = downtrendPairsToDraw, PairLookbackBars = pairLookbackBars, MaxStoredPivots = maxStoredPivots, MinimumTrendBars = minimumTrendBars, MinimumTrendTicks = minimumTrendTicks, UptrendPairColor = uptrendPairColor, DowntrendPairColor = downtrendPairColor, SGPairColor = sGPairColor, RLPairColor = rLPairColor, UseWicksForGainLoss = useWicksForGainLoss, GainLossToleranceTicks = gainLossToleranceTicks, TestTouchToleranceTicks = testTouchToleranceTicks, TestCloseHoldTicks = testCloseHoldTicks, ShowTestedPairLevels = showTestedPairLevels, SupportTestDotColor = supportTestDotColor, ResistanceTestDotColor = resistanceTestDotColor, ShowLegacyGainedLostLevels = showLegacyGainedLostLevels, LegacyNumberOfLevels = legacyNumberOfLevels, LegacyExtendRightBars = legacyExtendRightBars, LegacyMaxActiveLines = legacyMaxActiveLines, LegacyGainLineWidth = legacyGainLineWidth, LegacyLossLineWidth = legacyLossLineWidth, LegacyGainLineColor = legacyGainLineColor, LegacyLossLineColor = legacyLossLineColor, LegacyGainTestLineColor = legacyGainTestLineColor, LegacyLossTestLineColor = legacyLossTestLineColor, LegacyCloseThroughAction = legacyCloseThroughAction, ShowLegacyFirstTouchDots = showLegacyFirstTouchDots, LegacyGainDotColor = legacyGainDotColor, LegacyLossDotColor = legacyLossDotColor, ShowPairLevelLabels = showPairLevelLabels, LabelBarsRight = labelBarsRight, LabelPixelOffset = labelPixelOffset, LabelFontSize = labelFontSize, PairBoundsPairLineWidth = pairBoundsPairLineWidth, ShowZigZag = showZigZag, ZigZagColor = zigZagColor, ZigZagLineWidth = zigZagLineWidth, CalcOnlyMode = calcOnlyMode, MaxReportDistanceTicks = maxReportDistanceTicks }, input, ref cacheAlightenMirrorPtJV0006);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenMirrorPtJV0006 AlightenMirrorPtJV0006(int barsToProcess, bool useConfirmedPairsOnly, bool useWicksForPairLevels, int nearestPairLineWidth, int pairLineWidthStep, DashStyleHelper untestedLineStyle, DashStyleHelper testedLineStyle, int uptrendPairsToDraw, int downtrendPairsToDraw, int pairLookbackBars, int maxStoredPivots, int minimumTrendBars, int minimumTrendTicks, Color uptrendPairColor, Color downtrendPairColor, Color sGPairColor, Color rLPairColor, bool useWicksForGainLoss, int gainLossToleranceTicks, int testTouchToleranceTicks, int testCloseHoldTicks, bool showTestedPairLevels, Color supportTestDotColor, Color resistanceTestDotColor, bool showLegacyGainedLostLevels, int legacyNumberOfLevels, int legacyExtendRightBars, int legacyMaxActiveLines, int legacyGainLineWidth, int legacyLossLineWidth, Color legacyGainLineColor, Color legacyLossLineColor, Color legacyGainTestLineColor, Color legacyLossTestLineColor, string legacyCloseThroughAction, bool showLegacyFirstTouchDots, Color legacyGainDotColor, Color legacyLossDotColor, bool showPairLevelLabels, int labelBarsRight, int labelPixelOffset, int labelFontSize, int pairBoundsPairLineWidth, bool showZigZag, Color zigZagColor, int zigZagLineWidth, bool calcOnlyMode, int maxReportDistanceTicks)
		{
			return indicator.AlightenMirrorPtJV0006(Input, barsToProcess, useConfirmedPairsOnly, useWicksForPairLevels, nearestPairLineWidth, pairLineWidthStep, untestedLineStyle, testedLineStyle, uptrendPairsToDraw, downtrendPairsToDraw, pairLookbackBars, maxStoredPivots, minimumTrendBars, minimumTrendTicks, uptrendPairColor, downtrendPairColor, sGPairColor, rLPairColor, useWicksForGainLoss, gainLossToleranceTicks, testTouchToleranceTicks, testCloseHoldTicks, showTestedPairLevels, supportTestDotColor, resistanceTestDotColor, showLegacyGainedLostLevels, legacyNumberOfLevels, legacyExtendRightBars, legacyMaxActiveLines, legacyGainLineWidth, legacyLossLineWidth, legacyGainLineColor, legacyLossLineColor, legacyGainTestLineColor, legacyLossTestLineColor, legacyCloseThroughAction, showLegacyFirstTouchDots, legacyGainDotColor, legacyLossDotColor, showPairLevelLabels, labelBarsRight, labelPixelOffset, labelFontSize, pairBoundsPairLineWidth, showZigZag, zigZagColor, zigZagLineWidth, calcOnlyMode, maxReportDistanceTicks);
		}

		public Indicators.AlightenMirrorPtJV0006 AlightenMirrorPtJV0006(ISeries<double> input , int barsToProcess, bool useConfirmedPairsOnly, bool useWicksForPairLevels, int nearestPairLineWidth, int pairLineWidthStep, DashStyleHelper untestedLineStyle, DashStyleHelper testedLineStyle, int uptrendPairsToDraw, int downtrendPairsToDraw, int pairLookbackBars, int maxStoredPivots, int minimumTrendBars, int minimumTrendTicks, Color uptrendPairColor, Color downtrendPairColor, Color sGPairColor, Color rLPairColor, bool useWicksForGainLoss, int gainLossToleranceTicks, int testTouchToleranceTicks, int testCloseHoldTicks, bool showTestedPairLevels, Color supportTestDotColor, Color resistanceTestDotColor, bool showLegacyGainedLostLevels, int legacyNumberOfLevels, int legacyExtendRightBars, int legacyMaxActiveLines, int legacyGainLineWidth, int legacyLossLineWidth, Color legacyGainLineColor, Color legacyLossLineColor, Color legacyGainTestLineColor, Color legacyLossTestLineColor, string legacyCloseThroughAction, bool showLegacyFirstTouchDots, Color legacyGainDotColor, Color legacyLossDotColor, bool showPairLevelLabels, int labelBarsRight, int labelPixelOffset, int labelFontSize, int pairBoundsPairLineWidth, bool showZigZag, Color zigZagColor, int zigZagLineWidth, bool calcOnlyMode, int maxReportDistanceTicks)
		{
			return indicator.AlightenMirrorPtJV0006(input, barsToProcess, useConfirmedPairsOnly, useWicksForPairLevels, nearestPairLineWidth, pairLineWidthStep, untestedLineStyle, testedLineStyle, uptrendPairsToDraw, downtrendPairsToDraw, pairLookbackBars, maxStoredPivots, minimumTrendBars, minimumTrendTicks, uptrendPairColor, downtrendPairColor, sGPairColor, rLPairColor, useWicksForGainLoss, gainLossToleranceTicks, testTouchToleranceTicks, testCloseHoldTicks, showTestedPairLevels, supportTestDotColor, resistanceTestDotColor, showLegacyGainedLostLevels, legacyNumberOfLevels, legacyExtendRightBars, legacyMaxActiveLines, legacyGainLineWidth, legacyLossLineWidth, legacyGainLineColor, legacyLossLineColor, legacyGainTestLineColor, legacyLossTestLineColor, legacyCloseThroughAction, showLegacyFirstTouchDots, legacyGainDotColor, legacyLossDotColor, showPairLevelLabels, labelBarsRight, labelPixelOffset, labelFontSize, pairBoundsPairLineWidth, showZigZag, zigZagColor, zigZagLineWidth, calcOnlyMode, maxReportDistanceTicks);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenMirrorPtJV0006 AlightenMirrorPtJV0006(int barsToProcess, bool useConfirmedPairsOnly, bool useWicksForPairLevels, int nearestPairLineWidth, int pairLineWidthStep, DashStyleHelper untestedLineStyle, DashStyleHelper testedLineStyle, int uptrendPairsToDraw, int downtrendPairsToDraw, int pairLookbackBars, int maxStoredPivots, int minimumTrendBars, int minimumTrendTicks, Color uptrendPairColor, Color downtrendPairColor, Color sGPairColor, Color rLPairColor, bool useWicksForGainLoss, int gainLossToleranceTicks, int testTouchToleranceTicks, int testCloseHoldTicks, bool showTestedPairLevels, Color supportTestDotColor, Color resistanceTestDotColor, bool showLegacyGainedLostLevels, int legacyNumberOfLevels, int legacyExtendRightBars, int legacyMaxActiveLines, int legacyGainLineWidth, int legacyLossLineWidth, Color legacyGainLineColor, Color legacyLossLineColor, Color legacyGainTestLineColor, Color legacyLossTestLineColor, string legacyCloseThroughAction, bool showLegacyFirstTouchDots, Color legacyGainDotColor, Color legacyLossDotColor, bool showPairLevelLabels, int labelBarsRight, int labelPixelOffset, int labelFontSize, int pairBoundsPairLineWidth, bool showZigZag, Color zigZagColor, int zigZagLineWidth, bool calcOnlyMode, int maxReportDistanceTicks)
		{
			return indicator.AlightenMirrorPtJV0006(Input, barsToProcess, useConfirmedPairsOnly, useWicksForPairLevels, nearestPairLineWidth, pairLineWidthStep, untestedLineStyle, testedLineStyle, uptrendPairsToDraw, downtrendPairsToDraw, pairLookbackBars, maxStoredPivots, minimumTrendBars, minimumTrendTicks, uptrendPairColor, downtrendPairColor, sGPairColor, rLPairColor, useWicksForGainLoss, gainLossToleranceTicks, testTouchToleranceTicks, testCloseHoldTicks, showTestedPairLevels, supportTestDotColor, resistanceTestDotColor, showLegacyGainedLostLevels, legacyNumberOfLevels, legacyExtendRightBars, legacyMaxActiveLines, legacyGainLineWidth, legacyLossLineWidth, legacyGainLineColor, legacyLossLineColor, legacyGainTestLineColor, legacyLossTestLineColor, legacyCloseThroughAction, showLegacyFirstTouchDots, legacyGainDotColor, legacyLossDotColor, showPairLevelLabels, labelBarsRight, labelPixelOffset, labelFontSize, pairBoundsPairLineWidth, showZigZag, zigZagColor, zigZagLineWidth, calcOnlyMode, maxReportDistanceTicks);
		}

		public Indicators.AlightenMirrorPtJV0006 AlightenMirrorPtJV0006(ISeries<double> input , int barsToProcess, bool useConfirmedPairsOnly, bool useWicksForPairLevels, int nearestPairLineWidth, int pairLineWidthStep, DashStyleHelper untestedLineStyle, DashStyleHelper testedLineStyle, int uptrendPairsToDraw, int downtrendPairsToDraw, int pairLookbackBars, int maxStoredPivots, int minimumTrendBars, int minimumTrendTicks, Color uptrendPairColor, Color downtrendPairColor, Color sGPairColor, Color rLPairColor, bool useWicksForGainLoss, int gainLossToleranceTicks, int testTouchToleranceTicks, int testCloseHoldTicks, bool showTestedPairLevels, Color supportTestDotColor, Color resistanceTestDotColor, bool showLegacyGainedLostLevels, int legacyNumberOfLevels, int legacyExtendRightBars, int legacyMaxActiveLines, int legacyGainLineWidth, int legacyLossLineWidth, Color legacyGainLineColor, Color legacyLossLineColor, Color legacyGainTestLineColor, Color legacyLossTestLineColor, string legacyCloseThroughAction, bool showLegacyFirstTouchDots, Color legacyGainDotColor, Color legacyLossDotColor, bool showPairLevelLabels, int labelBarsRight, int labelPixelOffset, int labelFontSize, int pairBoundsPairLineWidth, bool showZigZag, Color zigZagColor, int zigZagLineWidth, bool calcOnlyMode, int maxReportDistanceTicks)
		{
			return indicator.AlightenMirrorPtJV0006(input, barsToProcess, useConfirmedPairsOnly, useWicksForPairLevels, nearestPairLineWidth, pairLineWidthStep, untestedLineStyle, testedLineStyle, uptrendPairsToDraw, downtrendPairsToDraw, pairLookbackBars, maxStoredPivots, minimumTrendBars, minimumTrendTicks, uptrendPairColor, downtrendPairColor, sGPairColor, rLPairColor, useWicksForGainLoss, gainLossToleranceTicks, testTouchToleranceTicks, testCloseHoldTicks, showTestedPairLevels, supportTestDotColor, resistanceTestDotColor, showLegacyGainedLostLevels, legacyNumberOfLevels, legacyExtendRightBars, legacyMaxActiveLines, legacyGainLineWidth, legacyLossLineWidth, legacyGainLineColor, legacyLossLineColor, legacyGainTestLineColor, legacyLossTestLineColor, legacyCloseThroughAction, showLegacyFirstTouchDots, legacyGainDotColor, legacyLossDotColor, showPairLevelLabels, labelBarsRight, labelPixelOffset, labelFontSize, pairBoundsPairLineWidth, showZigZag, zigZagColor, zigZagLineWidth, calcOnlyMode, maxReportDistanceTicks);
		}
	}
}

#endregion

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
// NinjaTrader writes the bare nested enum names into its generated region; these aliases resolve them.
using AAVMTFSessionMode = NinjaTrader.NinjaScript.Indicators.AutoAVWAPMTFV0005.AAVMTFSessionMode;
using AAVMTFAnchorTimeframe = NinjaTrader.NinjaScript.Indicators.AutoAVWAPMTFV0005.AAVMTFAnchorTimeframe;
using AAVMTFTimeZone = NinjaTrader.NinjaScript.Indicators.AutoAVWAPMTFV0005.AAVMTFTimeZone;
#endregion

// Auto AVWAP MTF v1 - AutoAVWAP v4 with a selectable calculation data series.
//
// "Data series minutes" > 0 adds a Minute series of that size and runs every anchor,
// test, gap and session rule on it, exactly as v4 would on a chart of that period.
// The chart's own bars only display the result:
//   - AVWAP segments are drawn by time between calculation-bar closes,
//   - a successful test colors every chart bar inside that calculation bar,
//   - labels and price-scale boxes follow the chart's latest bar and price.
// 0 = calculate on the chart series, as v4 does.
//
// Bar-count inputs (delays, waits, lookback) count CALCULATION bars, not chart bars.
//
// v2 = v1 + the open anchor and day reset fire on the bar that CONTAINS their clock time
// (BarContains). v4/v1 fire one bar early (the bar ENDING at the time) and never across a
// session gap, so an 18:00 ET open anchor never fired and a 17:00 reset landed on the old
// session's last bar. v1 is kept unchanged for comparison.
//
// v3 = v2 + Kris's "4 Hours" session behavior (TradingView AAVWAP): session high/low anchors
// reset at the session open and at every 4-hour block after it, aligned to the trading session's
// start (18:00 ET for CME = TradingView's 240 boundaries 18/22/02/06/10/14 ET). Labels HO4H/LO4H.
// Computed from the session template, not an added 240 series, so it still works hosted.
//
// V0004 = v3 +
//   * Kris's stock/futures sessions (TradingView AAVWAP "Use stock session"). Day and 4 Hours reset at
//     the active session's start, and session high/low anchors are only made on bars inside it, so with
//     the stock session HOD/LOD and HOPD/LOPD are cash-session levels. Replaces the day reset hour/minute.
//   * No anchors until the first real session start: a load that begins mid-session shows nothing
//     instead of a partial session's high/low as if it were complete.
//   * A calculation bar that closes in the future (the one still forming at load) is not processed
//     during the historical load; it is processed when it closes, like every realtime bar.
//
// V0005 = V0004 + SIGNALS on the chart (the 30-sec chart when calculating on 5-min):
//   * WICK: a completed calc bar reaches an AVWAP (within the wick tolerance) and closes back on the side
//     it came from -> a marker on the first chart bar after that calc bar closes.
//   * RETEST: a calc bar closes THROUGH an AVWAP (arms it); the first later wick back into it that closes
//     on the break side is the stronger signal (trade #3 on 2026-09-28: break 10:30, retest 10:35).
//   * Each signal draws its stop (beyond the wick + buffer), its target (the next AVWAP beyond the entry)
//     and the reward:risk; below the minimum R:R the marker is greyed.
//   * Optional bias filter, sound alerts, a Market Analyzer plot (+-1 wick, +-2 retest), and a signal log
//     with each signal's forward outcome, appended in Documents\NinjaTrader 8\Mirror Logs.
//   * The debug log now appends (a reload no longer overwrites it), also in Mirror Logs.
//   * BREAKS: a calc-bar close THROUGH a session high/low AVWAP at least N bars old is a change of
//     direction (2026-09-28: 10:30 close below the LO AVWAP = short; 11:00 close above the new LO = long).
//     Drawn as a diamond; it sets the direction state; signals against it are greyed with an X; open
//     signals against a new break are cancelled (X) - the 10:55 short is cancelled by the 11:00 break.
//   * CLOSE MARGIN: closes within N ticks of an AVWAP are ON the line (no break; still a hold), so bodies
//     parked on a level read as tests (2026-09-29 09:45/09:50 = two holds of the LOPD AVWAP).
//   * Partial-bar fix that works in Playback: the calc bar still forming at load is detected against the
//     chart's last bar, not Globals.Now (which is the PC clock, not the replay clock).
namespace NinjaTrader.NinjaScript.Indicators
{
    public class AutoAVWAPMTFV0005 : Indicator
    {
        public enum AAVMTFSessionMode { Day, Week, Month, Quarter, Year, FourHours }
        public enum AAVMTFAnchorTimeframe { Minute1, Minute3, Minute5, Minute15, Minute30, Hour1, Hour4, Day, Week, Month }
        public enum AAVMTFTimeZone { Chicago, NewYork, LosAngeles, UTC }
        public enum AAVMTFSignalAnchors { SessionHighLow, AllAVWAPs, SessionHighLowAndTests }

        private class Anchor
        {
            public int Id, Kind, Side, StartBar, LastBar, TestCount, LastTestBar = -1;
            public double Pv, Vol, Level, Extreme;
            public bool TestUsed, LabelDrawn;
            public DateTime LastTime, StartTime;
            public int BreakDir, BreakBar = -1;   // V0005: last close-through direction (arms a retest)
            public int ClearSide;                 // V0005: side of the last close clearly away from the line (beyond the close margin)
            public string LabelTag;
            public List<string> DrawTags = new List<string>();
        }

        // A successful test bar, applied to the chart bars it spans on the next primary update.
        // V0005: a signal from a completed calc bar, drawn on the first chart bar after it closes.
        private class SignalEvent
        {
            public DateTime CalcTime;            // close stamp of the calc bar that produced it
            public int Dir, Strength, CalcBar;   // Dir +1/-1, Strength 1 = wick, 2 = retest
            public string Kind;                  // HO / LO / HOP / LOP / TEST / GAP / OPEN (+ 4H suffix etc. not needed)
            public double Level, Entry, Stop, Target, RR, High, Low;
            public bool Weak, Drawn, Conflict, Cancelled, Mixed;
            public string Tag, Text; public DateTime DrawTime; public double DrawY;   // set when drawn (for the cancel redraw)
            public double Mfe, Mae; public int Bars, Outcome;   // Outcome: 0 open, 1 target first, -1 stop first, 2 both in one bar
        }

        private struct PendingColor
        {
            public DateTime From, To;   // (From, To]
            public Brush Brush;
        }

        private readonly List<Anchor> anchors = new List<Anchor>();
        private readonly List<PendingColor> pendingColors = new List<PendingColor>();
        private int nextId = 1, lastTestBar = -1, lastProcessedBar = -1, sessionStartBar;
        private int s;   // calculation series: 0 = chart, 1 = added minute series
        private Brush bullBrush, bearBrush, bullTestBrush, bearTestBrush;
        private SimpleFont labelFont;
        private SessionIterator sessionIter;
        private string debugPath;   // diagnostic log, null when off
        private bool seenSessionStart;   // no anchors until the first real session start
        private int stockStart, stockEnd, futuresStart, futuresEnd;   // minutes of day, parsed in DataLoaded
        private long lastBlockKey = long.MinValue;   // 4 Hours mode: session start + block index of the last processed bar
        private const int PriceScaleSlots = 24;

        // Market Analyzer outputs, appended after the price-scale slots so their indices never move.
        private const int PlotCross = PriceScaleSlots, PlotBarsSinceCross = PriceScaleSlots + 1,
                          PlotTest = PriceScaleSlots + 2, PlotBarsSinceTest = PriceScaleSlots + 3,
                          PlotLiveTest = PriceScaleSlots + 4, PlotNearest = PriceScaleSlots + 5,
                          PlotDistance = PriceScaleSlots + 6, PlotBias = PriceScaleSlots + 7;
        private int lastCrossBar = -1, lastCrossDir, lastSignalBar = -1, lastSignalDir;
        private const int PlotV5Signal = PriceScaleSlots + 8;
        private readonly List<SignalEvent> pendingSignals = new List<SignalEvent>();   // waiting to be drawn
        private readonly List<SignalEvent> openSignals = new List<SignalEvent>();      // outcome still being tracked
        private readonly Queue<string> signalTags = new Queue<string>();
        private int lastV5Bar = -1, lastV5Value;
        private int dirState;   // last qualifying break: +1 long, -1 short, 0 none yet
        private class BreakMark { public DateTime CalcTime; public int Dir, CalcBar; public string Kind; public double High, Low; }
        private readonly List<BreakMark> pendingBreaks = new List<BreakMark>();
        private readonly List<SignalEvent> pendingCancels = new List<SignalEvent>();
        private SimpleFont brkFont;
        private string signalLogPath, loadId = "L?";
        private Brush sigLongBrush, sigShortBrush, sigWeakBrush;
        private SimpleFont sigFont;

        // Hosted (e.g. by a Market Analyzer column) the indicator has no chart: compute, never draw.
        private bool CanDraw { get { return ChartControl != null; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AutoAVWAPMTFV0005";
                Description = "AutoAVWAP v4 calculated on a selectable minute data series and displayed on any chart. v2: open anchor and day reset fire on the bar containing their time. v3: 4 Hours session behavior. V0004: stock/futures sessions, complete first session, no partial bar at load. V0005: wick and break-retest signals on the chart.";
                IsOverlay = true;
                Calculate = Calculate.OnEachTick;
                IsSuspendedWhileInactive = true;
                PaintPriceMarkers = true;
                BarsRequiredToPlot = 1;

                DataSeriesMinutes = 5;
                DataSeriesBarsToLoad = 0;
                CalcTimeframe = AAVMTFAnchorTimeframe.Minute5;
                SessionBehavior = AAVMTFSessionMode.Day;
                KeepPreviousSessionHL = true;
                ShowSessionLabels = true;
                LabelOffsetBars = 2;
                TimeZoneSetting = AAVMTFTimeZone.NewYork;
                OpenAnchorHour = 18;
                OpenAnchorMinute = 0;
                NewExtremeLookback = 20;
                CloseThroughDelayBars = 1;
                CloseThroughToleranceTicks = 8;
                BarsBeforeTest = 2;
                TestWaitBars = 1;
                TestToleranceTicks = 8;
                PriceScaleLabelToleranceTicks = 8;
                AllowFollowOnTests = true;
                MaxTestsPerAnchor = 10;
                IndependentCooldowns = true;
                MinimumGapTicks = 40;
                DeleteGapOnCloseThrough = false;
                ResetOnNewSession = true;
                UseStockSession = true;
                StockSession = "0930-1600";
                FuturesSession = "1800-1700";
                MaxOpenTestAVWAPs = 10;
                BullishColor = Brushes.Teal;
                BearishColor = Brushes.Red;
                ColorSuccessfulTestBars = true;
                ColorRegardlessOfCandleDirection = false;
                IntrabarColoring = true;
                BullishTestBarColor = Brushes.Teal;
                BearishTestBarColor = Brushes.Red;
                AVWAPLineWidth = 2;

                for (int i = 0; i < PriceScaleSlots; i++)
                {
                    AddPlot(Brushes.Transparent, "NearAVWAP" + (i + 1).ToString("00"));
                    Plots[i].PlotStyle = PlotStyle.PriceBox;
                }

                // Market Analyzer outputs, excluded from autoscale (OnCalculateMinMax).
                // Alpha 1/255 instead of Transparent: invisible on the chart but still listed in the
                // Data Box, while the transparent price-box slots stay out of it. Their values sit far
                // off the price scale, so no price marker appears. NearestAVWAP IS a price, so it stays
                // transparent (no marker; DistanceTicks carries the same information).
                Brush dataBoxOnly = new SolidColorBrush(Color.FromArgb(1, 128, 128, 128));
                dataBoxOnly.Freeze();
                AddPlot(dataBoxOnly, "CrossSignal");
                AddPlot(dataBoxOnly, "BarsSinceCross");
                AddPlot(dataBoxOnly, "TestSignal");
                AddPlot(dataBoxOnly, "BarsSinceTest");
                AddPlot(dataBoxOnly, "LiveTest");
                AddPlot(Brushes.Transparent, "NearestAVWAP");
                AddPlot(dataBoxOnly, "DistanceTicks");
                AddPlot(dataBoxOnly, "Bias");
                AddPlot(dataBoxOnly, "AVWAPSignal");   // V0005: +-1 wick, +-2 retest (index PriceScaleSlots + 8)
                SignalHoldBars = 1;
                EnableSignals = true;
                EnableWickSignals = true;
                EnableRetestSignals = true;
                SignalAnchors = AAVMTFSignalAnchors.SessionHighLowAndTests;
                SignalWickToleranceTicks = 12;
                SignalMinAnchorAgeBars = 1;
                SignalBiasFilter = false;
                StopBufferTicks = 4;
                MinRewardRisk = 1.0;
                ShowStopTarget = false;
                SignalLineBars = 20;
                MaxSignalDrawings = 60;
                SignalLongColor = Brushes.DeepSkyBlue;
                SignalShortColor = Brushes.OrangeRed;
                SignalWeakColor = Brushes.Gray;
                SignalFontSize = 13;
                SignalSoundAlert = true;
                SignalLongSound = "Alert2.wav";
                SignalShortSound = "Alert4.wav";
                WriteSignalLog = true;
                SignalForwardBars = 12;
                ShowBreakMarkers = true;
                UseDirectionState = true;
                CancelOnOpposingBreak = true;
                MinBreakAgeBars = 2;
                CancelWindowBars = 3;
                MixedBarNeutral = true;
                ShowRewardRiskLabel = false;
                SignalCloseMarginTicks = 4;
            }
            else if (State == State.Configure)
            {
                if (DataSeriesMinutes > 0)
                {
                    if (DataSeriesBarsToLoad > 0)
                    {
                        // Explicit history, like the Mirror: lets Week/Month/Quarter/Year sessions and large
                        // series start with enough bars even when the chart loads only a few days.
                        string tradingHours = null;   // null = the instrument's default trading hours
                        try { if (BarsArray != null && BarsArray[0] != null && BarsArray[0].TradingHours != null) tradingHours = BarsArray[0].TradingHours.Name; } catch { }
                        AddDataSeries(Instrument.FullName,
                            new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = DataSeriesMinutes },
                            DataSeriesBarsToLoad, tradingHours, null);
                    }
                    else
                        AddDataSeries(BarsPeriodType.Minute, DataSeriesMinutes);   // the chart's date range
                }
            }
            else if (State == State.Realtime && debugPath != null)
            {
                Dbg("--- REALTIME: anchors ---");
                foreach (Anchor a in anchors) Dbg("  " + AnchorText(a));
            }
            else if (State == State.DataLoaded)
            {
                s = DataSeriesMinutes > 0 ? 1 : 0;
                bullBrush = CloneBrush(BullishColor);
                bearBrush = CloneBrush(BearishColor);
                bullTestBrush = CloneBrush(BullishTestBarColor);
                bearTestBrush = CloneBrush(BearishTestBarColor);
                labelFont = new SimpleFont("Arial", 10);
                sessionIter = new SessionIterator(BarsArray[s]);
                sessionStartBar = 0;
                seenSessionStart = false;
                if (!ParseSession(StockSession, out stockStart, out stockEnd)) { stockStart = 570; stockEnd = 960; }
                if (!ParseSession(FuturesSession, out futuresStart, out futuresEnd)) { futuresStart = 1080; futuresEnd = 1020; }
                loadId = "L" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                sigLongBrush = CloneBrush(SignalLongColor);
                sigShortBrush = CloneBrush(SignalShortColor);
                sigWeakBrush = CloneBrush(SignalWeakColor);
                sigFont = new SimpleFont("Arial", SignalFontSize) { Bold = true };
                brkFont = new SimpleFont("Arial", Math.Max(6, SignalFontSize - 2)) { Bold = true };
                dirState = 0;
                signalLogPath = null;
                if (WriteSignalLog)
                {
                    try
                    {
                        BarsPeriod cp = BarsArray[0].BarsPeriod;
                        signalLogPath = LogFile("AutoAVWAPSignalsV0005_" + Instrument.MasterInstrument.Name + ".log");
                        System.IO.File.AppendAllText(signalLogPath, string.Format(
                            "# LOAD {0} {1:yyyy-MM-dd HH:mm:ss} chart={2} {3} series={4}m tol={5}t minAge={6} anchors={7} retest={8} wick={9} bias={10} stopBuf={11}t minRR={12}\n",
                            loadId, DateTime.Now, cp.Value, cp.BarsPeriodType, DataSeriesMinutes, SignalWickToleranceTicks,
                            SignalMinAnchorAgeBars, SignalAnchors, EnableRetestSignals, EnableWickSignals, SignalBiasFilter,
                            StopBufferTicks, MinRewardRisk));
                    }
                    catch { signalLogPath = null; }
                }
                debugPath = null;
                if (DebugLog)
                {
                    try
                    {
                        BarsPeriod cp = BarsArray[0].BarsPeriod;
                        debugPath = LogFile("AutoAVWAPMTFV0005_" + Instrument.MasterInstrument.Name + "_chart" + cp.Value + cp.BarsPeriodType + ".log");
                        // Appended, not overwritten: a reload must not destroy the session it recorded.
                        System.IO.File.AppendAllText(debugPath, string.Format("# " + loadId + " {0:yyyy-MM-dd HH:mm:ss} chart={1} {2} series={3}m barsToLoad={4} tradingHours={5} calcBars={6} session={7} {8}\n",
                            DateTime.Now, cp.Value, cp.BarsPeriodType, DataSeriesMinutes, DataSeriesBarsToLoad,
                            BarsArray[s].TradingHours != null ? BarsArray[s].TradingHours.Name : "?", BarsArray[s].Count,
                            UseStockSession ? "stock" : "futures", UseStockSession ? StockSession : FuturesSession));
                    }
                    catch { debugPath = null; }
                }
            }
        }

        protected override void OnBarUpdate()
        {
            // The calculation series may hold history from before the chart's first bar (bars to load):
            // process it anyway so anchors exist when the chart starts; nothing is drawn until then.
            if (CurrentBars[s] < 0 || (BarsInProgress == 0 && CurrentBars[0] < 0))
                return;

            // ---- Calculation, on the selected series --------------------------------
            if (BarsInProgress == s)
            {
                int cb = CurrentBars[s];
                if (State == State.Realtime)
                {
                    // Process a completed bar once, on the first tick of its successor.
                    if (IsFirstTickOfBar && cb > 0 && lastProcessedBar < cb - 1)
                        ProcessBar(1, cb - 1);
                }
                // A bar stamped in the future is still forming (NinjaTrader stamps bars with their close):
                // leave it for the realtime path, which processes it once it has closed.
                else if (lastProcessedBar < cb && Times[s][0] <= NinjaTrader.Core.Globals.Now && !CalcBarStillForming())
                {
                    // Historical bars are delivered at bar close when Tick Replay is off.
                    ProcessBar(0, cb);
                }
            }

            // ---- Display, on the chart series ---------------------------------------
            if (BarsInProgress != 0)
                return;

            bool live = State == State.Realtime;
            int liveTest = live ? PreviewDirection() : 0;
            if (CanDraw)
            {
                ApplyPendingColors();
                if (live && IntrabarColoring && ColorSuccessfulTestBars)
                {
                    BarBrushes[0] = null;
                    if (liveTest == 1) BarBrushes[0] = bullTestBrush;
                    else if (liveTest == -1) BarBrushes[0] = bearTestBrush;
                }
                if (!live || IsFirstTickOfBar)
                    UpdateLabels();
                DrawPendingSignals();
            }
            else
            {
                pendingColors.Clear();
                pendingSignals.Clear();
                pendingBreaks.Clear();
                pendingCancels.Clear();
            }
            UpdatePriceScaleSlots(live);
            UpdateAnalyzerPlots(live, liveTest);
        }

        // One value per Market Analyzer plot, on the chart series' current bar.
        // Signals belong to the last COMPLETED calculation bar; "bars since" counts calculation bars.
        private void UpdateAnalyzerPlots(bool live, int liveTest)
        {
            int sinceCross = lastCrossBar < 0 ? -1 : lastProcessedBar - lastCrossBar;
            int sinceTest  = lastSignalBar < 0 ? -1 : lastProcessedBar - lastSignalBar;

            Values[PlotCross][0]          = sinceCross >= 0 && sinceCross < SignalHoldBars ? lastCrossDir : 0;
            Values[PlotBarsSinceCross][0] = sinceCross < 0 ? 0 : lastCrossDir * (sinceCross + 1);
            Values[PlotTest][0]           = sinceTest >= 0 && sinceTest < SignalHoldBars ? lastSignalDir : 0;
            Values[PlotBarsSinceTest][0]  = sinceTest < 0 ? 0 : lastSignalDir * (sinceTest + 1);
            Values[PlotLiveTest][0]       = liveTest;

            double price = Closes[0][0];
            double v = Math.Max(0, Volumes[s][0]);
            double pv = ((Highs[s][0] + Lows[s][0] + Closes[s][0]) / 3.0) * v;
            double nearest = double.NaN;
            int above = 0, below = 0;
            foreach (Anchor a in anchors)
            {
                double level = live && v > 0 ? (a.Pv + pv) / (a.Vol + v) : a.Level;
                if (double.IsNaN(nearest) || Math.Abs(price - level) < Math.Abs(price - nearest)) nearest = level;
                if (level < price) below++;
                else if (level > price) above++;
            }
            Values[PlotNearest][0]  = double.IsNaN(nearest) ? 0 : nearest;
            Values[PlotDistance][0] = double.IsNaN(nearest) || TickSize <= 0 ? 0 : Math.Round((price - nearest) / TickSize);
            Values[PlotBias][0]     = anchors.Count == 0 ? 0 : (below - above) / (double)anchors.Count;
            int sinceV5 = lastV5Bar < 0 ? -1 : lastProcessedBar - lastV5Bar;
            Values[PlotV5Signal][0] = sinceV5 >= 0 && sinceV5 < SignalHoldBars ? lastV5Value : 0;
        }

        // The analyzer plots are not price levels: keep them out of the chart's autoscale.
        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;
            if (ChartBars == null) return;
            for (int i = 0; i < PriceScaleSlots; i++)
                for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
                {
                    if (!Values[i].IsValidDataPointAt(idx)) continue;
                    double y = Values[i].GetValueAt(idx);
                    if (double.IsNaN(y) || y == 0) continue;
                    if (y < MinValue) MinValue = y;
                    if (y > MaxValue) MaxValue = y;
                }
        }

        private void ProcessBar(int barsAgo, int barIndex)
        {
            if (barIndex <= lastProcessedBar || barsAgo < 0 || barsAgo > CurrentBars[s])
                return;

            DateTime barTime = Times[s][barsAgo];
            DateTime local = ToSelectedTime(barTime);
            DateTime previousLocal = barIndex > 0 ? ToSelectedTime(Times[s][barsAgo + 1]) : local.AddMinutes(-1);
            int openMinutes = OpenAnchorHour * 60 + OpenAnchorMinute;
            int sessionStart = UseStockSession ? stockStart : futuresStart;
            int sessionEnd   = UseStockSession ? stockEnd : futuresEnd;
            // Fires on the bar that CONTAINS the clock time (see BarContains). v4 fires one bar early
            // and never across a session gap; this is a deliberate MTF-only correction.
            bool openEvent = GetSeriesMinutes() < 1440 && BarContains(previousLocal, local, openMinutes);
            // Kris: Day mode starts at the active (stock or futures) session's start, on a bar INSIDE that
            // session. Without the in-session test a gap that spans the start time counts: the weekend gap
            // (Fri 17:00 -> Sun 18:05) contains Saturday 09:30, so the Sunday open fired a stock-session
            // reset, promoted Friday's cash H/L early, and Monday's real reset then deleted them.
            // Tested at the bar's last moment so a bar straddling the start still qualifies.
            bool dayReset = barIndex > 0 && BarContains(previousLocal, local, sessionStart)
                            && InSession(local.AddSeconds(-1), sessionStart, sessionEnd);
            bool calendarChanged = barIndex > 0 && SessionKey(local) != SessionKey(previousLocal);
            bool inSession = InSession(local.AddMinutes(-GetSeriesMinutes()), sessionStart, sessionEnd);
            // 4 Hours (Kris): a new 4-hour block of the trading session, or the day reset (session open).
            bool fourHourChanged = SessionBehavior == AAVMTFSessionMode.FourHours && FourHourBlockChanged(barTime, barIndex);
            bool resetEvent = SessionBehavior == AAVMTFSessionMode.Day ? dayReset
                            : SessionBehavior == AAVMTFSessionMode.FourHours ? (fourHourChanged || dayReset)
                            : calendarChanged;
            bool resettingNow = ResetOnNewSession && resetEvent;
            double o = Opens[s][barsAgo], h = Highs[s][barsAgo], l = Lows[s][barsAgo], c = Closes[s][barsAgo];
            double v = Math.Max(0, Volumes[s][barsAgo]);
            double pv = ((h + l + c) / 3.0) * v;
            bool gapEvent = barIndex > 0 && Math.Abs(o - Closes[s][barsAgo + 1]) >= MinimumGapTicks * TickSize;

            if (debugPath != null)
                Dbg(string.Format("BAR {0:MM-dd HH:mm} #{1} O={2} H={3} L={4} C={5} V={6}{7}{8}", barTime, barIndex, o, h, l, c, v,
                    inSession ? "" : "  (out of session)",
                    resetEvent ? "  RESET" + (dayReset ? " day" : "") + (fourHourChanged ? " 4h" : "") + (calendarChanged ? " cal" : "") : ""));

            // Nothing before the first real session start: a load that begins mid-session would
            // otherwise treat that fragment's high/low as a complete session's.
            if (!seenSessionStart)
            {
                if (!resetEvent)
                {
                    lastProcessedBar = barIndex;
                    return;
                }
                seenSessionStart = true;
                if (debugPath != null) Dbg("  FIRST SESSION START");
            }
            if (resetEvent)
            {
                sessionStartBar = barIndex;
                // Keep exactly the just-completed session's H/L pair as prior H/L.
                for (int i = anchors.Count - 1; i >= 0; i--)
                {
                    Anchor a = anchors[i];
                    if (IsExtreme(a.Kind) && (a.Kind == 4 || a.Kind == -4))
                        RemoveAnchor(a);
                    else if (KeepPreviousSessionHL && (a.Kind == 1 || a.Kind == -1))
                    {
                        a.Kind = a.Kind == 1 ? 4 : -4;
                        if (debugPath != null) Dbg("  PREV " + AnchorText(a));
                    }
                    else if (IsExtreme(a.Kind))
                        RemoveAnchor(a);
                }
            }

            if (resettingNow)
            {
                for (int i = anchors.Count - 1; i >= 0; i--)
                {
                    Anchor a = anchors[i];
                    if (!(KeepPreviousSessionHL && (a.Kind == 4 || a.Kind == -4)))
                        RemoveAnchor(a);
                }
            }

            double activeHigh = double.NaN, activeLow = double.NaN;
            foreach (Anchor a in anchors)
            {
                if (a.Kind == 1 && (double.IsNaN(activeHigh) || a.Extreme > activeHigh)) activeHigh = a.Extreme;
                if (a.Kind == -1 && (double.IsNaN(activeLow) || a.Extreme < activeLow)) activeLow = a.Extreme;
            }
            int lookback = GetExtremeLookback();
            double priorHigh = double.NegativeInfinity, priorLow = double.PositiveInfinity;
            int scanMax = Math.Min(lookback, Math.Max(0, barIndex - sessionStartBar));
            for (int k = 1; k <= scanMax; k++)
            {
                int ago = barsAgo + k;
                if (ago > CurrentBars[s]) break;
                priorHigh = Math.Max(priorHigh, Highs[s][ago]);
                priorLow = Math.Min(priorLow, Lows[s][ago]);
            }
            // Kris: in Day and 4 Hours, session high/low anchors only form inside the active session.
            bool allowSessionAnchor = !(SessionBehavior == AAVMTFSessionMode.Day || SessionBehavior == AAVMTFSessionMode.FourHours) || inSession;
            bool addHigh = allowSessionAnchor && (resetEvent || (h > priorHigh && (double.IsNaN(activeHigh) || h > activeHigh)));
            bool addLow = allowSessionAnchor && (resetEvent || (l < priorLow && (double.IsNaN(activeLow) || l < activeLow)));

            // A new extreme replaces only the active same-session H/L anchor.
            if (addHigh || addLow)
            {
                for (int i = anchors.Count - 1; i >= 0; i--)
                    if ((addHigh && anchors[i].Kind == 1) || (addLow && anchors[i].Kind == -1))
                        RemoveAnchor(anchors[i]);
            }

            int retestParentId = -1, retestDirection = 0;
            bool bullishCandidate = false, bearishCandidate = false;
            bool globalSpacingReady = lastTestBar < 0 || barIndex - lastTestBar > TestWaitBars;
            double testTol = TestToleranceTicks * TickSize;
            double closeTol = CloseThroughToleranceTicks * TickSize;
            double prevClose = barIndex > 0 ? Closes[s][barsAgo + 1] : c;
            int crossUp = 0, crossDown = 0;

            // V0005: forward outcome of earlier signals, from this completed bar.
            TrackSignalOutcomes(h, l);

            // V0005 candidates for this bar. Best per direction: retest beats wick, then the OLDER anchor.
            Anchor sigShortA = null, sigLongA = null;
            int sigShortStr = 0, sigLongStr = 0;
            double sigShortLvl = 0, sigLongLvl = 0;
            double sigTol = SignalWickToleranceTicks * TickSize;
            Anchor brkA = null; int brkDir = 0;   // the qualifying break on this bar (oldest AVWAP wins)

            // Update and test every surviving AVWAP before adding anchors born on this bar.
            for (int i = anchors.Count - 1; i >= 0; i--)
            {
                Anchor a = anchors[i];
                double nextPv = a.Pv + pv, nextVol = a.Vol + v;
                double level = nextVol > 0 ? nextPv / nextVol : a.Level;

                // Close-to-close CROSS of this AVWAP, counted before a close-through can delete it.
                if (barIndex > 0)
                {
                    if (prevClose <= a.Level && c > level) crossUp++;
                    else if (prevClose >= a.Level && c < level) crossDown++;
                }

                // V0005 signals, against the line this bar traded into (a.Level = the AVWAP before this bar).
                if (EnableSignals && barIndex > 0 && SignalAnchorAllowed(a.Kind))
                {
                    double lb = a.Level;
                    // Close margin: a close within it is ON the line (2026-09-29 09:45 closed 0.22 below the LOPD AVWAP
                    // and 09:50 opened on it: two tests that held, not a break and a reversal). Breaks need a close
                    // clearly on the other side of the LAST CLEAR close; a wick's direction comes from the side the bar
                    // opened on, or, if it opened on the line, from that last clear side. 0 = the original rules.
                    double margin = SignalCloseMarginTicks * TickSize;
                    int brk, from = 0;
                    if (margin <= 0)
                        brk = (prevClose >= lb && c < level) ? -1 : (prevClose <= lb && c > level) ? 1 : 0;
                    else
                    {
                        if (a.ClearSide == 0) a.ClearSide = prevClose > lb + margin ? 1 : prevClose < lb - margin ? -1 : 0;
                        int cs = c > level + margin ? 1 : c < level - margin ? -1 : 0;
                        brk = (cs != 0 && a.ClearSide != 0 && cs != a.ClearSide) ? cs : 0;
                        from = Math.Abs(o - lb) > margin ? (o > lb ? 1 : -1) : (a.ClearSide != 0 ? a.ClearSide : (prevClose >= lb ? 1 : -1));
                        if (cs != 0) a.ClearSide = cs;
                    }
                    if (brk != 0)
                    {
                        a.BreakDir = brk; a.BreakBar = barIndex;   // arms a retest
                        // A change of direction only counts on an AVWAP that has existed a while: 5-min bars cross
                        // brand-new AVWAPs constantly (09:50 crossed a 1-bar-old LO up, right before #1's short).
                        // Direction only ever comes from the session high/low AVWAPs: with TEST AVWAPs allowed, a close
                        // through a 2-bar-old TEST AVWAP at 09:30 on 9/28 set "long" and crossed out trade #1's short.
                        if (IsExtreme(a.Kind) && barIndex - a.StartBar >= MinBreakAgeBars && (brkA == null || a.StartBar < brkA.StartBar))
                        { brkA = a; brkDir = brk; }
                    }
                    else if (barIndex - a.StartBar >= SignalMinAnchorAgeBars)
                    {
                        // Wick: came from one side, reached the line (within tolerance), closed back on that side.
                        bool shortWick, longWick;
                        if (margin <= 0)
                        {
                            shortWick = o <= lb + sigTol && h >= lb - sigTol && c < lb;
                            longWick  = o >= lb - sigTol && l <= lb + sigTol && c > lb;
                        }
                        else
                        {   // came from one side, reached the line, and did not close clearly through it
                            longWick  = from == 1  && l <= lb + sigTol && c >= lb - margin;
                            shortWick = from == -1 && h >= lb - sigTol && c <= lb + margin;
                        }
                        if (shortWick)
                        {
                            int str = (a.BreakDir == -1 && barIndex > a.BreakBar) ? 2 : 1;
                            if ((str == 2 ? EnableRetestSignals : EnableWickSignals) && BetterSignal(str, a, sigShortStr, sigShortA))
                            { sigShortA = a; sigShortStr = str; sigShortLvl = lb; }
                        }
                        if (longWick)
                        {
                            int str = (a.BreakDir == 1 && barIndex > a.BreakBar) ? 2 : 1;
                            if ((str == 2 ? EnableRetestSignals : EnableWickSignals) && BetterSignal(str, a, sigLongStr, sigLongA))
                            { sigLongA = a; sigLongStr = str; sigLongLvl = lb; }
                        }
                    }
                }
                int side = a.Side;
                bool extreme = IsExtreme(a.Kind);
                int age = barIndex - a.StartBar;
                bool flippedExtreme = extreme && ((side == -1 && c > level) || (side == 1 && c < level));
                if (flippedExtreme)
                {
                    side = -side;
                    a.Side = side;
                    a.TestUsed = false;
                    a.TestCount = 0;
                }
                bool closeCheck = !extreme && (a.Kind != 3 || DeleteGapOnCloseThrough) && age >= CloseThroughDelayBars;
                bool closedThrough = closeCheck && ((side == 1 && c < level - closeTol) || (side == -1 && c > level + closeTol));
                if (closedThrough)
                {
                    RemoveAnchor(a);
                    continue;
                }

                if (a.LastBar < barIndex)
                    DrawAnchorSegment(a, barIndex, barTime, a.Level, level, c >= level ? bullBrush : bearBrush);
                a.Pv = nextPv; a.Vol = nextVol; a.Level = level; a.LastBar = barIndex; a.LastTime = barTime;

                bool heldSide = extreme ? (side == 1 ? c > level : c < level) : c != level;
                bool heldTest = age >= BarsBeforeTest && !flippedExtreme && h >= level - testTol && l <= level + testTol && heldSide;
                bool available = !a.TestUsed || (AllowFollowOnTests && a.TestCount > 0 && a.TestCount < MaxTestsPerAnchor);
                bool parentSpacing = a.LastTestBar < 0 || barIndex - a.LastTestBar > TestWaitBars;
                bool spacingReady = IndependentCooldowns ? parentSpacing : globalSpacingReady;
                if (heldTest && available && spacingReady)
                {
                    int dir = c > level ? 1 : c < level ? -1 : 0;
                    if (dir == 1) bullishCandidate = true;
                    if (dir == -1) bearishCandidate = true;
                    if (retestParentId < 0)
                    {
                        retestParentId = a.Id;
                        retestDirection = dir;
                    }
                }
            }

            // V0005: one signal per bar. Both directions -> keep the stronger, then the older anchor.
            if (sigShortA != null && sigLongA != null)
            {
                bool keepShort = ShortWins(sigShortStr, sigShortA, sigLongStr, sigLongA);
                if (keepShort) sigLongA = null; else sigShortA = null;
            }
            // A break on this bar sets the direction before this bar's signal is judged.
            int emitDir = sigShortA != null ? -1 : sigLongA != null ? 1 : 0;
            Anchor emitA = sigShortA ?? sigLongA;
            int emitStr = sigShortA != null ? sigShortStr : sigLongStr;
            double emitLvl = sigShortA != null ? sigShortLvl : sigLongLvl;
            // MIXED bar: it broke one way and signalled the other (2026-09-28 12:30: closed above the HOPD AVWAP,
            // retest-rejected the LOPD AVWAP above it = trade #12). Neither side wins: the direction goes neutral
            // and the signal is drawn with a mixed mark instead of an X.
            bool mixedBar = brkA != null && emitA != null && emitDir != brkDir;
            if (brkA != null) ApplyBreak(brkDir, brkA, barIndex, barTime, h, l, c);
            if (mixedBar && MixedBarNeutral)
            {
                dirState = 0;
                if (signalLogPath != null)
                    SigLog(string.Format("MIXED  {0:yyyy-MM-dd HH:mm} break {1} and a {2} signal on one bar -> direction NEUTRAL",
                        barTime, brkDir == -1 ? "DOWN" : "UP", emitDir == -1 ? "SHORT" : "LONG"));
            }

            // One direction per bar; a bar that crossed levels both ways is not a signal.
            if (crossUp > 0 && crossDown == 0) { lastCrossBar = barIndex; lastCrossDir = 1; }
            else if (crossDown > 0 && crossUp == 0) { lastCrossBar = barIndex; lastCrossDir = -1; }

            int openTestCount = 0;
            foreach (Anchor a in anchors) if (a.Kind == 0 || a.Kind == 2) openTestCount++;
            bool addOpen = openEvent;
            bool addRetest = !openEvent && retestParentId >= 0 && openTestCount < MaxOpenTestAVWAPs;
            bool addGap = gapEvent;
            if (v > 0 && (!resettingNow || addHigh || addLow || addRetest || addGap))
            {
                if (addOpen && !resettingNow) AddAnchor(0, h, l, c, pv, v, barIndex, barTime);
                if (addHigh) AddAnchor(1, h, l, c, pv, v, barIndex, barTime);
                if (addLow) AddAnchor(-1, h, l, c, pv, v, barIndex, barTime);
                if (addRetest) AddAnchor(2, h, l, c, pv, v, barIndex, barTime);
                if (addGap) AddAnchor(3, h, l, c, pv, v, barIndex, barTime);

                if (addRetest)
                {
                    lastTestBar = barIndex;
                    Anchor parent = FindAnchor(retestParentId);
                    if (parent != null)
                    {
                        parent.TestUsed = true; parent.TestCount++; parent.LastTestBar = barIndex;
                    }
                    bool oneSided = !(bullishCandidate && bearishCandidate);
                    bool aligned = ColorRegardlessOfCandleDirection || (retestDirection == 1 ? c > o : c < o);
                    // The analyzer's TestSignal is the coloring rule, whether or not bars are colored.
                    if (oneSided && aligned && retestDirection != 0)
                    {
                        lastSignalBar = barIndex;
                        lastSignalDir = retestDirection;
                    }
                    if (ColorSuccessfulTestBars && oneSided && aligned && retestDirection != 0)
                        pendingColors.Add(new PendingColor
                        {
                            From  = barIndex > 0 ? Times[s][barsAgo + 1] : DateTime.MinValue,
                            To    = barTime,
                            Brush = retestDirection == 1 ? bullTestBrush : bearTestBrush
                        });
                }
            }

            // Emitted after this bar's new anchors exist, so a new HOD/LOD can be the target (09:55 made a new low:
            // the old LO AVWAP was replaced before the loop and the signal used to report "target none").
            if (emitA != null) EmitSignal(emitDir, emitStr, emitA, emitLvl, barIndex, barTime, h, l, c, mixedBar);

            lastProcessedBar = barIndex;
        }

        private void AddAnchor(int kind, double h, double l, double c, double pv, double v, int barIndex, DateTime barTime)
        {
            double level = pv / v;
            Anchor a = new Anchor { Id = nextId++, Kind = kind, Side = c >= level ? 1 : -1, StartBar = barIndex, LastBar = barIndex,
                LastTime = barTime, Pv = pv, Vol = v, Level = level, Extreme = kind == 1 ? h : kind == -1 ? l : double.NaN,
                LabelTag = "AutoAVWAPMTFV0005_Label_" + nextId.ToString() };
            a.StartTime = barTime;
            anchors.Add(a);
            if (debugPath != null) Dbg("  ADD  " + AnchorText(a));
        }

        // Drawn by time so it lands on the right chart bars whatever the chart period is.
        private void DrawAnchorSegment(Anchor a, int barIndex, DateTime barTime, double oldLevel, double newLevel, Brush brush)
        {
            if (!CanDraw || CurrentBars[0] < 0) return;   // no chart bars yet: segment is off-chart anyway
            string tag = "AutoAVWAPMTFV0005_Line_" + a.Id + "_" + barIndex;
            Draw.Line(this, tag, false, a.LastTime, oldLevel, barTime, newLevel, brush, DashStyleHelper.Solid, AVWAPLineWidth);
            a.DrawTags.Add(tag);
        }

        // Chart series only: color every chart bar inside each successful calculation test bar.
        private void ApplyPendingColors()
        {
            if (pendingColors.Count == 0) return;
            foreach (PendingColor p in pendingColors)
            {
                for (int k = 0; k <= CurrentBars[0]; k++)
                {
                    DateTime t = Times[0][k];
                    if (t <= p.From) break;
                    if (t <= p.To) BarBrushes[k] = p.Brush;
                }
            }
            pendingColors.Clear();
        }

        // Chart series only: labels sit LabelOffsetBars chart bars to the right of the latest bar.
        private void UpdateLabels()
        {
            double close = Closes[0][0];
            foreach (Anchor a in anchors)
            {
                if (!ShowSessionLabels || !IsExtreme(a.Kind))
                {
                    if (a.LabelDrawn) { RemoveDrawObject(a.LabelTag); a.LabelDrawn = false; }
                    continue;
                }
                Draw.Text(this, a.LabelTag, false, SessionLabel(a.Kind), -LabelOffsetBars, a.Level, 0,
                    close >= a.Level ? bullBrush : bearBrush, labelFont, TextAlignment.Left,
                    Brushes.Transparent, Brushes.Transparent, 0);
                a.LabelDrawn = true;
            }
        }

        private string SessionLabel(int kind)
        {
            string p = SessionBehavior == AAVMTFSessionMode.FourHours ? "4H" : SessionBehavior == AAVMTFSessionMode.Year ? "Y" : SessionBehavior == AAVMTFSessionMode.Quarter ? "Q" : SessionBehavior == AAVMTFSessionMode.Month ? "M" : SessionBehavior == AAVMTFSessionMode.Week ? "W" : "D";
            return kind == 1 ? "HO" + p : kind == -1 ? "LO" + p : kind == 4 ? "HOP" + p : "LOP" + p;
        }

        private void RemoveAnchor(Anchor a)
        {
            if (CanDraw)
            {
                for (int i = 0; i < a.DrawTags.Count; i++) RemoveDrawObject(a.DrawTags[i]);
                if (a.LabelTag != null) RemoveDrawObject(a.LabelTag);
            }
            anchors.Remove(a);
            if (debugPath != null) Dbg("  DEL  " + AnchorText(a));
        }

        // ======================= V0005 signals =================================================

        private bool SignalAnchorAllowed(int kind)
        {
            if (SignalAnchors == AAVMTFSignalAnchors.AllAVWAPs) return true;
            if (SignalAnchors == AAVMTFSignalAnchors.SessionHighLowAndTests) return IsExtreme(kind) || kind == 2;
            return IsExtreme(kind);
        }

        // Is candidate (str, a) better than the current best (bestStr, best)? Retest beats wick; then the
        // older anchor (more established, and the tie-break that keeps 09:55's HOD short over a 1-bar-old LOD).
        private static bool BetterSignal(int str, Anchor a, int bestStr, Anchor best)
        {
            if (best == null) return true;
            if (str != bestStr) return str > bestStr;
            int r = KindRank(a.Kind), rb = KindRank(best.Kind);
            if (r != rb) return r > rb;
            return a.StartBar < best.StartBar;
        }

        // How much an AVWAP matters: previous session high/low > session high/low > TEST > open/gap.
        private static int KindRank(int kind)
        {
            return kind == 4 || kind == -4 ? 3 : kind == 1 || kind == -1 ? 2 : kind == 2 ? 1 : 0;
        }

        // A bar with both a short and a long candidate keeps ONE: retest beats wick, then the more important
        // AVWAP, then the side that agrees with the current direction, then the older AVWAP.
        // 2026-10-02: 10:50 short at the HOD AVWAP beat a long at an older TEST AVWAP; 11:40 (two TESTs) went
        // long with the direction set at 11:30; 11:45 long at the LOD AVWAP beat a short at a TEST AVWAP.
        private bool ShortWins(int shortStr, Anchor shortA, int longStr, Anchor longA)
        {
            if (shortStr != longStr) return shortStr > longStr;
            int rs = KindRank(shortA.Kind), rl = KindRank(longA.Kind);
            if (rs != rl) return rs > rl;
            if (dirState != 0) return dirState == -1;
            return shortA.StartBar < longA.StartBar;
        }

        private static string KindText(int kind)
        {
            return kind == 1 ? "HO" : kind == -1 ? "LO" : kind == 4 ? "HOP" : kind == -4 ? "LOP" : kind == 0 ? "OPEN" : kind == 2 ? "TEST" : "GAP";
        }

        private void EmitSignal(int dir, int strength, Anchor a, double level, int barIndex, DateTime barTime, double h, double l, double c, bool mixed)
        {
            // Bias filter: shorts only with most AVWAPs above price, longs only with most below.
            if (SignalBiasFilter && anchors.Count > 0)
            {
                int above = 0, below = 0;
                foreach (Anchor x in anchors) { if (x.Level > c) above++; else if (x.Level < c) below++; }
                if (dir == -1 && above < below) return;
                if (dir == 1 && below < above) return;
            }

            double buf = StopBufferTicks * TickSize;
            double stop = dir == -1 ? h + buf : l - buf;
            // Target: the nearest OTHER AVWAP beyond the entry in the trade's direction.
            double target = double.NaN;
            foreach (Anchor x in anchors)
            {
                if (x == a || !IsExtreme(x.Kind)) continue;   // targets: the major levels only (HOD/LOD/HOPD/LOPD)
                if (dir == -1 && x.Level < c - TickSize && (double.IsNaN(target) || x.Level > target)) target = x.Level;
                if (dir == 1 && x.Level > c + TickSize && (double.IsNaN(target) || x.Level < target)) target = x.Level;
            }
            double risk = Math.Abs(c - stop);
            double rr = double.IsNaN(target) || risk <= 0 ? double.NaN : Math.Abs(target - c) / risk;

            SignalEvent e = new SignalEvent
            {
                CalcTime = barTime, CalcBar = barIndex, Dir = dir, Strength = strength, Kind = KindText(a.Kind),
                Level = level, Entry = c, Stop = stop, Target = target, RR = rr, High = h, Low = l,
                Weak = !double.IsNaN(rr) && rr < MinRewardRisk,
                Conflict = UseDirectionState && dirState != 0 && dir != dirState,
                Mixed = mixed
            };
            pendingSignals.Add(e);
            openSignals.Add(e);
            if (!e.Conflict)
            {
                lastV5Bar = barIndex;
                lastV5Value = dir * strength;
            }

            if (signalLogPath != null)
                SigLog(string.Format("SIGNAL {0:yyyy-MM-dd HH:mm} {1} {2} {3} level={4:F2} H={5} L={6} C={7} entry={8:F2} stop={9:F2} target={10} RR={11}{12}{13}",
                    barTime, dir == -1 ? "SHORT" : "LONG ", strength == 2 ? "RETEST" : "WICK  ", e.Kind, level, h, l, c, c, stop,
                    double.IsNaN(target) ? "none" : target.ToString("F2"), double.IsNaN(rr) ? "-" : rr.ToString("F2"), e.Weak ? " (weak)" : "",
                    e.Conflict ? " (CONFLICT: against the last break)" : e.Mixed ? " (MIXED bar: broke the other way)" : ""));

            if (SignalSoundAlert && !e.Conflict && State == State.Realtime && CanDraw)
            {
                try
                {
                    string file = dir == -1 ? SignalShortSound : SignalLongSound;
                    string path = string.IsNullOrWhiteSpace(file) ? "" : System.IO.Path.IsPathRooted(file) ? file
                                : System.IO.Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", file);
                    if (path.Length > 0 && !System.IO.File.Exists(path)) path = "";
                    Alert("AAVWAPV5_" + barIndex + "_" + dir, Priority.High,
                          string.Format("{0} {1}: AVWAP {2} {3} {4} @ {5}", Instrument.FullName, BarsPeriod,
                              strength == 2 ? "RETEST" : "wick", dir == -1 ? "SHORT" : "LONG", e.Kind, Instrument.MasterInstrument.FormatPrice(c)),
                          path, 0, dir == -1 ? Brushes.DarkRed : Brushes.DarkCyan, Brushes.White);
                }
                catch { }
            }
        }

        // Chart series only. A signal is drawn on the FIRST chart bar after its calc bar closed (the bar you
        // could act on), not on the calc bar's own (already finished) chart bars.
        private void DrawPendingSignals()
        {
            // No early return on an empty signal queue: breaks and cancels are drawn below even when no
            // signal is waiting (the 11:00 break was logged but never drawn because of that return).
            if (CurrentBars[0] < 0) return;
            DateTime now = Times[0][0];
            for (int i = 0; i < pendingSignals.Count; i++)
            {
                SignalEvent e = pendingSignals[i];
                if (e.Drawn || now <= e.CalcTime) continue;
                e.Drawn = true;
                bool muted = e.Weak || e.Conflict || e.Cancelled;
                Brush br = muted ? sigWeakBrush : e.Dir == -1 ? sigShortBrush : sigLongBrush;
                string arrow = e.Dir == -1 ? (e.Strength == 2 ? "▼▼" : "▼") : (e.Strength == 2 ? "▲▲" : "▲");
                string text = arrow + " " + (e.Strength == 2 ? "R " : "") + e.Kind + (double.IsNaN(e.RR) || !ShowRewardRiskLabel ? "" : " " + e.RR.ToString("0.0") + "R");
                e.Text = text;
                if (e.Conflict || e.Cancelled) text = "✕ " + text;
                else if (e.Mixed) text = "⇅ " + text;
                double off = 6 * TickSize;
                double y = e.Dir == -1 ? Math.Max(Highs[0][0], e.High) + off : Math.Min(Lows[0][0], e.Low) - off;
                string tag = "AAVWAPV5_sig_" + e.CalcBar + "_" + e.Dir;
                e.Tag = tag; e.DrawTime = Times[0][0]; e.DrawY = y;
                Draw.Text(this, tag, false, text, 0, y, 0, br, sigFont,
                          TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                RememberSignalTag(tag);
                if (ShowStopTarget && !e.Conflict && !e.Cancelled)
                {
                    string ts = tag + "_stop";
                    Draw.Line(this, ts, false, 0, e.Stop, -SignalLineBars, e.Stop, br, DashStyleHelper.Dash, 1);
                    RememberSignalTag(ts);
                    if (!double.IsNaN(e.Target))
                    {
                        string tt = tag + "_target";
                        Draw.Line(this, tt, false, 0, e.Target, -SignalLineBars, e.Target, br, DashStyleHelper.Dot, 2);
                        RememberSignalTag(tt);
                    }
                }
            }
            pendingSignals.RemoveAll(x => x.Drawn);
            DrawPendingBreaks(now);
            DrawPendingCancels();
        }

        // A qualifying close-through: direction state, break marker, cancellation of open opposing signals.
        private void ApplyBreak(int dir, Anchor a, int barIndex, DateTime barTime, double h, double l, double c)
        {
            dirState = dir;
            if (signalLogPath != null)
                SigLog(string.Format("BREAK  {0:yyyy-MM-dd HH:mm} {1} {2} (AVWAP from {3:MM-dd HH:mm}, {4} bars old) close={5} -> direction {6}",
                    barTime, dir == -1 ? "DOWN" : "UP  ", KindText(a.Kind), a.StartTime, barIndex - a.StartBar, c, dir == -1 ? "SHORT" : "LONG"));
            if (ShowBreakMarkers)
                pendingBreaks.Add(new BreakMark { CalcTime = barTime, Dir = dir, CalcBar = barIndex, Kind = KindText(a.Kind), High = h, Low = l });
            if (!CancelOnOpposingBreak) return;
            foreach (SignalEvent e in openSignals)
            {
                // Only RECENT pending signals: a signal that has already worked is not undone by a later turn.
                if (e.Dir == dir || e.Cancelled || e.Conflict || e.Outcome != 0 || e.CalcBar >= barIndex
                    || barIndex - e.CalcBar > CancelWindowBars) continue;
                e.Cancelled = true;
                pendingCancels.Add(e);
                if (signalLogPath != null)
                    SigLog(string.Format("CANCEL {0:yyyy-MM-dd HH:mm} {1} {2} signal of {3:HH:mm} - opposed by the break", barTime,
                        e.Dir == -1 ? "SHORT" : "LONG ", e.Kind, e.CalcTime));
            }
        }

        private void DrawPendingBreaks(DateTime now)
        {
            for (int i = pendingBreaks.Count - 1; i >= 0; i--)
            {
                BreakMark m = pendingBreaks[i];
                if (now <= m.CalcTime) continue;
                string brkText = (m.Dir == -1 ? "◆↓ " : "◆↑ ") + m.Kind;
                // A signal on the same bar and the same side (short signals and down-breaks both sit above the
                // bar) would print on top of the break: merge them into one label, e.g. "▼ HO + ◆↓ LO".
                SignalEvent same = openSignals.Find(x => x.Drawn && x.CalcBar == m.CalcBar && x.Dir == m.Dir && x.Tag != null);
                if (same != null)
                {
                    same.Text = same.Text + " + " + brkText;
                    bool muted = same.Weak || same.Conflict || same.Cancelled;
                    Draw.Text(this, same.Tag, false, (same.Conflict || same.Cancelled ? "✕ " : "") + same.Text, same.DrawTime, same.DrawY, 0,
                              muted ? sigWeakBrush : m.Dir == -1 ? sigShortBrush : sigLongBrush, sigFont,
                              TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                    pendingBreaks.RemoveAt(i);
                    continue;
                }
                double off = 4 * TickSize;
                double y = m.Dir == -1 ? Math.Max(Highs[0][0], m.High) + off : Math.Min(Lows[0][0], m.Low) - off;
                string tag = "AAVWAPV5_brk_" + m.CalcBar;
                Draw.Text(this, tag, false, brkText, 0, y, 0,
                          m.Dir == -1 ? sigShortBrush : sigLongBrush, brkFont, TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                RememberSignalTag(tag);
                pendingBreaks.RemoveAt(i);
            }
        }

        // Re-draw a cancelled signal in place: same tag, greyed, with an X; its stop/target lines removed.
        private void DrawPendingCancels()
        {
            for (int i = pendingCancels.Count - 1; i >= 0; i--)
            {
                SignalEvent e = pendingCancels[i];
                if (!e.Drawn) continue;   // not drawn yet: it will be drawn already greyed
                Draw.Text(this, e.Tag, false, "✕ " + e.Text, e.DrawTime, e.DrawY, 0, sigWeakBrush, sigFont,
                          TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
                RemoveDrawObject(e.Tag + "_stop");
                RemoveDrawObject(e.Tag + "_target");
                pendingCancels.RemoveAt(i);
            }
        }

        private void RememberSignalTag(string tag)
        {
            signalTags.Enqueue(tag);
            while (signalTags.Count > Math.Max(3, MaxSignalDrawings * 3))
                RemoveDrawObject(signalTags.Dequeue());
        }

        // Forward outcome per signal, on completed calc bars (coarse: inside one bar the order of high and low
        // is unknown, so a bar touching both stop and target is "both").
        private void TrackSignalOutcomes(double h, double l)
        {
            for (int i = openSignals.Count - 1; i >= 0; i--)
            {
                SignalEvent e = openSignals[i];
                e.Bars++;
                double fav = e.Dir == -1 ? e.Entry - l : h - e.Entry;
                double adv = e.Dir == -1 ? h - e.Entry : e.Entry - l;
                e.Mfe = Math.Max(e.Mfe, fav);
                e.Mae = Math.Max(e.Mae, adv);
                if (e.Outcome == 0)
                {
                    bool stopHit = e.Dir == -1 ? h >= e.Stop : l <= e.Stop;
                    bool tgtHit = !double.IsNaN(e.Target) && (e.Dir == -1 ? l <= e.Target : h >= e.Target);
                    if (stopHit && tgtHit) e.Outcome = 2; else if (tgtHit) e.Outcome = 1; else if (stopHit) e.Outcome = -1;
                }
                if (e.Bars >= SignalForwardBars || (e.Outcome != 0 && e.Bars >= 1 && e.Outcome == -1))
                {
                    if (signalLogPath != null)
                        SigLog(string.Format("OUTCOME {0:yyyy-MM-dd HH:mm} {1} {2} {3} entry={4:F2} -> {5} after {6} bars  MFE={7:F2} MAE={8:F2}{9}",
                            e.CalcTime, e.Dir == -1 ? "SHORT" : "LONG ", e.Strength == 2 ? "RETEST" : "WICK  ", e.Kind, e.Entry,
                            e.Outcome == 1 ? "TARGET first" : e.Outcome == -1 ? "STOP first" : e.Outcome == 2 ? "both in one bar" : "neither",
                            e.Bars, e.Mfe, e.Mae, e.Conflict ? " (conflict)" : e.Cancelled ? " (cancelled)" : ""));
                    openSignals.RemoveAt(i);
                }
            }
        }

        // Playback-safe partial-bar test: the calc bar still forming at load closes AFTER the chart's last bar.
        private bool CalcBarStillForming()
        {
            if (s == 0 || State != State.Historical) return false;
            try
            {
                if (CurrentBars[s] != BarsArray[s].Count - 1) return false;
                return Times[s][0] > BarsArray[0].GetTime(BarsArray[0].Count - 1);
            }
            catch { return false; }
        }

        private string LogFile(string name)
        {
            string folder = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "Mirror Logs");
            System.IO.Directory.CreateDirectory(folder);
            return System.IO.Path.Combine(folder, name);
        }

        private void SigLog(string line)
        {
            try { System.IO.File.AppendAllText(signalLogPath, loadId + " [" + State + "] " + line + "\n"); } catch { }
        }

        private Anchor FindAnchor(int id)
        {
            for (int i = 0; i < anchors.Count; i++) if (anchors[i].Id == id) return anchors[i];
            return null;
        }

        private string AnchorText(Anchor a)
        {
            string k = a.Kind == 1 ? "HO" : a.Kind == -1 ? "LO" : a.Kind == 4 ? "HOP" : a.Kind == -4 ? "LOP" : a.Kind == 0 ? "OPEN" : a.Kind == 2 ? "TEST" : "GAP";
            return string.Format("id={0} {1} start={2:MM-dd HH:mm} extreme={3} level={4:F2} side={5}", a.Id, k, a.StartTime, a.Extreme, a.Level, a.Side);
        }

        private void Dbg(string line)
        {
            try { System.IO.File.AppendAllText(debugPath, line + "\n"); } catch { }
        }

        private bool IsExtreme(int kind) { return kind == 1 || kind == -1 || kind == 4 || kind == -4; }

        // Tests the FORMING calculation bar: +1 / -1 when it would color as a test right now, else 0.
        // Feeds both intrabar coloring and the LiveTest plot.
        private int PreviewDirection()
        {
            int cb = CurrentBars[s];
            if (anchors.Count == 0 || cb < 1)
                return 0;
            double fo = Opens[s][0], fh = Highs[s][0], fl = Lows[s][0], fc = Closes[s][0];
            double v = Math.Max(0, Volumes[s][0]);
            if (v <= 0) return 0;
            double pv = ((fh + fl + fc) / 3.0) * v;
            bool bull = false, bear = false;
            int direction = 0;
            int openTestCount = 0;
            foreach (Anchor a in anchors) if (a.Kind == 0 || a.Kind == 2) openTestCount++;
            if (openTestCount >= MaxOpenTestAVWAPs) return 0;
            bool globalReady = lastTestBar < 0 || cb - lastTestBar > TestWaitBars;
            for (int i = anchors.Count - 1; i >= 0; i--)
            {
                Anchor a = anchors[i];
                double vol = a.Vol + v;
                double level = vol > 0 ? (a.Pv + pv) / vol : a.Level;
                int side = a.Side;
                bool extreme = IsExtreme(a.Kind);
                bool flipped = extreme && ((side == -1 && fc > level) || (side == 1 && fc < level));
                if (flipped) side = -side;
                int age = cb - a.StartBar;
                bool closeCheck = !extreme && (a.Kind != 3 || DeleteGapOnCloseThrough) && age >= CloseThroughDelayBars;
                bool closedThrough = closeCheck && ((side == 1 && fc < level - CloseThroughToleranceTicks * TickSize) || (side == -1 && fc > level + CloseThroughToleranceTicks * TickSize));
                bool held = extreme ? (side == 1 ? fc > level : fc < level) : fc != level;
                bool touch = age >= BarsBeforeTest && !flipped && !closedThrough && held && fh >= level - TestToleranceTicks * TickSize && fl <= level + TestToleranceTicks * TickSize;
                bool available = !a.TestUsed || (AllowFollowOnTests && a.TestCount > 0 && a.TestCount < MaxTestsPerAnchor);
                bool perReady = a.LastTestBar < 0 || cb - a.LastTestBar > TestWaitBars;
                if (touch && available && (IndependentCooldowns ? perReady : globalReady))
                {
                    int dir = fc > level ? 1 : fc < level ? -1 : 0;
                    if (dir == 1) bull = true;
                    if (dir == -1) bear = true;
                    if (direction == 0) direction = dir;
                }
            }
            if (bull && bear) return 0;
            if (direction == 1 && (ColorRegardlessOfCandleDirection || fc > fo)) return 1;
            if (direction == -1 && (ColorRegardlessOfCandleDirection || fc < fo)) return -1;
            return 0;
        }

        // Chart series only. Live levels include the forming calculation bar.
        private void UpdatePriceScaleSlots(bool live)
        {
            for (int i = 0; i < PriceScaleSlots; i++)
            {
                Values[i].Reset(0);   // no value: no price box, no Data Box row
                PlotBrushes[i][0] = Brushes.Transparent;
            }
            if (anchors.Count == 0) return;
            double allowed = PriceScaleLabelToleranceTicks * TickSize;
            double current = Closes[0][0];
            double v = Math.Max(0, Volumes[s][0]);
            double pv = ((Highs[s][0] + Lows[s][0] + Closes[s][0]) / 3.0) * v;
            int slot = 0;
            for (int i = anchors.Count - 1; i >= 0 && slot < PriceScaleSlots; i--)
            {
                Anchor a = anchors[i];
                double level = live && v > 0 ? (a.Pv + pv) / (a.Vol + v) : a.Level;
                if (Math.Abs(current - level) <= allowed)
                {
                    Values[slot][0] = level;
                    PlotBrushes[slot][0] = current >= level ? bullBrush : bearBrush;
                    slot++;
                }
            }
        }

        private DateTime ToSelectedTime(DateTime t)
        {
            string id = TimeZoneSetting == AAVMTFTimeZone.NewYork ? "Eastern Standard Time" :
                        TimeZoneSetting == AAVMTFTimeZone.LosAngeles ? "Pacific Standard Time" :
                        TimeZoneSetting == AAVMTFTimeZone.UTC ? "UTC" : "Central Standard Time";
            try
            {
                DateTime unspecified = DateTime.SpecifyKind(t, DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTime(unspecified, Core.Globals.GeneralOptions.TimeZoneInfo, TimeZoneInfo.FindSystemTimeZoneById(id));
            }
            catch { return t; }
        }

        // NinjaTrader stamps a bar with its CLOSE time, so the bar holding clock time T is the one
        // with previousClose <= T < close: at 9:30 on 5-minute bars that is the 9:35 bar (9:30-9:35),
        // not the 9:30 bar (9:25-9:30). Across a session gap the first bar after T qualifies, so an
        // 18:00 ET anchor lands on the 18:05 bar instead of never firing. Yesterday is checked too,
        // for bars that span midnight or a weekend.
        private static bool BarContains(DateTime previousClose, DateTime close, int minutesOfDay)
        {
            for (int back = 0; back <= 1; back++)
            {
                DateTime t = close.Date.AddDays(-back).AddMinutes(minutesOfDay);
                if (previousClose <= t && t < close) return true;
            }
            return false;
        }

        // TradingView's 240 bars for futures are aligned to the session open; so are these blocks:
        // session start + k * 240 minutes. A bar belongs to the block containing its OPEN time
        // (NinjaTrader stamps the close), so the reset lands on each block's first bar.
        private bool FourHourBlockChanged(DateTime barClose, int barIndex)
        {
            DateTime barOpen = barClose.AddMinutes(-GetSeriesMinutes());
            sessionIter.GetNextSession(barClose, true);
            DateTime sessionBegin = sessionIter.ActualSessionBegin;
            long block = (long)Math.Floor(Math.Max(0, (barOpen - sessionBegin).TotalMinutes) / 240.0);
            long key = sessionBegin.Ticks / TimeSpan.TicksPerMinute * 100 + block;
            bool changed = barIndex > 0 && lastBlockKey != long.MinValue && key != lastBlockKey;
            lastBlockKey = key;
            return changed;
        }

        // "HHmm-HHmm" in the selected time zone, e.g. 0930-1600 or 1800-1700 (wraps midnight).
        private static bool ParseSession(string text, out int start, out int end)
        {
            start = end = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().Split('-');
            if (parts.Length != 2) return false;
            int a, b;
            if (!int.TryParse(parts[0].Trim(), out a) || !int.TryParse(parts[1].Trim(), out b)) return false;
            if (a / 100 > 23 || a % 100 > 59 || b / 100 > 24 || b % 100 > 59) return false;
            start = a / 100 * 60 + a % 100;
            end   = b / 100 * 60 + b % 100;
            return true;
        }

        // Is a bar that OPENS at this local time inside [start, end)? Handles sessions across midnight.
        private static bool InSession(DateTime barOpenLocal, int start, int end)
        {
            int m = barOpenLocal.Hour * 60 + barOpenLocal.Minute;
            return start <= end ? (m >= start && m < end) : (m >= start || m < end);
        }

        private string SessionKey(DateTime t)
        {
            if (SessionBehavior == AAVMTFSessionMode.Year) return t.Year.ToString();
            if (SessionBehavior == AAVMTFSessionMode.Quarter) return t.Year + "-Q" + ((t.Month - 1) / 3).ToString();
            if (SessionBehavior == AAVMTFSessionMode.Month) return t.Year + "-" + t.Month;
            if (SessionBehavior == AAVMTFSessionMode.Week)
            {
                int delta = ((int)t.DayOfWeek + 6) % 7;
                return t.Date.AddDays(-delta).ToString("yyyyMMdd");
            }
            return t.Date.ToString("yyyyMMdd");
        }

        // Period of the CALCULATION series (v4's GetChartMinutes).
        private double GetSeriesMinutes()
        {
            BarsPeriod bp = BarsArray[s] != null ? BarsArray[s].BarsPeriod : null;
            if (bp == null) return 1;
            switch (bp.BarsPeriodType)
            {
                case BarsPeriodType.Second: return bp.Value / 60.0;
                case BarsPeriodType.Minute: return bp.Value;
                case BarsPeriodType.Day: return 1440.0 * bp.Value;
                case BarsPeriodType.Week: return 10080.0 * bp.Value;
                case BarsPeriodType.Month: return 43200.0 * bp.Value;
                case BarsPeriodType.Year: return 525600.0 * bp.Value;
                default: return 1;
            }
        }

        private double GetSelectedMinutes()
        {
            switch (CalcTimeframe)
            {
                case AAVMTFAnchorTimeframe.Minute1: return 1;
                case AAVMTFAnchorTimeframe.Minute3: return 3;
                case AAVMTFAnchorTimeframe.Minute5: return 5;
                case AAVMTFAnchorTimeframe.Minute15: return 15;
                case AAVMTFAnchorTimeframe.Minute30: return 30;
                case AAVMTFAnchorTimeframe.Hour1: return 60;
                case AAVMTFAnchorTimeframe.Hour4: return 240;
                case AAVMTFAnchorTimeframe.Day: return 1440;
                case AAVMTFAnchorTimeframe.Week: return 10080;
                default: return 43200;
            }
        }

        private int GetExtremeLookback()
        {
            double ratio = Math.Max(1.0, GetSelectedMinutes() / Math.Max(1.0, GetSeriesMinutes()));
            return Math.Min(5000, Math.Max(1, (int)Math.Round(NewExtremeLookback * ratio)));
        }

        private Brush CloneBrush(Brush brush)
        {
            if (brush == null) return Brushes.Transparent;
            Brush b = brush.Clone(); b.Freeze(); return b;
        }

        #region Inputs
        [NinjaScriptProperty, Range(0, 1440)]
        [Display(Name="Data series minutes (0 = chart)", Description="Minute bar size the AVWAPs are calculated on, e.g. 5, 10, 15, 60. The result is drawn on whatever chart this is applied to. 0 calculates on the chart's own bars, like AutoAVWAP v4. Bar-count inputs below count bars of THIS series.", GroupName="Data Series", Order=0)]
        public int DataSeriesMinutes { get; set; }

        // Display-only (not NinjaScriptProperty) so the generated factory signature is unchanged.
        [Range(0, 100000)]
        [Display(Name="Data series bars to load (0 = chart's range)", Description="How many bars of the data series to load. 0 loads the same date range as the chart. Set it when the chart loads only a few days but the session (Week, Month, Quarter, Year) or a large data series needs more history - e.g. 2000 bars of 5 minutes is about 7 trading days. Ignored when Data series minutes is 0.", GroupName="Data Series", Order=1)]
        public int DataSeriesBarsToLoad { get; set; }

        [Display(Name="Write debug log (diagnostic)", Description="Writes every calculation bar (OHLCV), session reset and anchor add/remove/promote to Documents\\NinjaTrader 8\\AutoAVWAPMTFV0005_<instrument>_chart<period>.log, one file per chart period, for comparing two charts. Leave off in normal use.", GroupName="Data Series", Order=2)]
        public bool DebugLog { get; set; }

        [NinjaScriptProperty]
        [Display(Name="AVWAP timeframe", GroupName="Timeframe", Order=0)]
        public AAVMTFAnchorTimeframe CalcTimeframe { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Session behavior", Description="Which session the high/low AVWAPs reset on. FourHours (Kris's 4 Hours): at the session open and every 4 hours after it, aligned to the trading session start (18:00 ET for CME: 18, 22, 02, 06, 10, 14). Labels HO4H / LO4H.", GroupName="Session", Order=0)]
        public AAVMTFSessionMode SessionBehavior { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Keep previous session High and Low", GroupName="Session", Order=1)]
        public bool KeepPreviousSessionHL { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show session high/low labels", GroupName="Display", Order=0)]
        public bool ShowSessionLabels { get; set; }

        [NinjaScriptProperty, Range(0, 50)]
        [Display(Name="Label right offset (chart bars)", GroupName="Display", Order=1)]
        public int LabelOffsetBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Time zone", GroupName="Anchors", Order=0)]
        public AAVMTFTimeZone TimeZoneSetting { get; set; }

        [NinjaScriptProperty, Range(0, 23)]
        [Display(Name="Open anchor hour", GroupName="Anchors", Order=1)]
        public int OpenAnchorHour { get; set; }

        [NinjaScriptProperty, Range(0, 59)]
        [Display(Name="Open anchor minute", GroupName="Anchors", Order=2)]
        public int OpenAnchorMinute { get; set; }

        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name="New high/low lookback (selected-timeframe bars)", GroupName="Anchors", Order=3)]
        public int NewExtremeLookback { get; set; }

        [NinjaScriptProperty, Range(1, 15)]
        [Display(Name="Close-through check delay (data series bars)", GroupName="Anchors", Order=4)]
        public int CloseThroughDelayBars { get; set; }

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Close-through tolerance (ticks)", GroupName="Anchors", Order=5)]
        public int CloseThroughToleranceTicks { get; set; }

        [NinjaScriptProperty, Range(2, 100)]
        [Display(Name="Bars after creation before AVWAP can be tested", GroupName="Anchors", Order=6)]
        public int BarsBeforeTest { get; set; }

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Bars to wait between test AVWAPs", GroupName="Test AVWAP", Order=0)]
        public int TestWaitBars { get; set; }

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="Test AVWAP tolerance (ticks)", GroupName="Test AVWAP", Order=1)]
        public int TestToleranceTicks { get; set; }

        [NinjaScriptProperty, Range(0, 1000)]
        [Display(Name="Price scale label tolerance (ticks)", GroupName="Test AVWAP", Order=2)]
        public int PriceScaleLabelToleranceTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Allow follow-on tests", GroupName="Test AVWAP", Order=3)]
        public bool AllowFollowOnTests { get; set; }

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name="Maximum tests per source AVWAP", GroupName="Test AVWAP", Order=4)]
        public int MaxTestsPerAnchor { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Independent cooldown per AVWAP", GroupName="Test AVWAP", Order=5)]
        public bool IndependentCooldowns { get; set; }

        [NinjaScriptProperty, Range(1, 10000)]
        [Display(Name="Minimum gap size (ticks)", GroupName="Gap AVWAP", Order=0)]
        public int MinimumGapTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Delete gap AVWAP on close-through", GroupName="Gap AVWAP", Order=1)]
        public bool DeleteGapOnCloseThrough { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Reset all AVWAPs on new session", GroupName="Anchors", Order=7)]
        public bool ResetOnNewSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Use stock session", Description="Kris's option. On: Day and 4 Hours reset at the stock session start and session high/low anchors only form inside the stock session, so HOD/LOD and HOPD/LOPD are cash-session levels. Off: the futures session.", GroupName="Session", Order=2)]
        public bool UseStockSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Stock session start-end", Description="HHmm-HHmm in the selected time zone. 0930-1600 = the US cash session in New York time.", GroupName="Session", Order=3)]
        public string StockSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Futures session start-end", Description="HHmm-HHmm in the selected time zone; may cross midnight. 1800-1700 = CME Globex in New York time. Replaces v3's day reset hour/minute: the day resets on the bar containing the start.", GroupName="Session", Order=4)]
        public string FuturesSession { get; set; }

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name="Maximum open/test AVWAPs", GroupName="Display", Order=2)]
        public int MaxOpenTestAVWAPs { get; set; }

        [XmlIgnore]
        [Display(Name="Bullish color", GroupName="Display", Order=3)]
        public Brush BullishColor { get; set; }
        [Browsable(false)] public string BullishColorSerialize { get { return Serialize.BrushToString(BullishColor); } set { BullishColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="Bearish color", GroupName="Display", Order=4)]
        public Brush BearishColor { get; set; }
        [Browsable(false)] public string BearishColorSerialize { get { return Serialize.BrushToString(BearishColor); } set { BearishColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Display(Name="Color successful test bars", GroupName="Bar Colors", Order=0)]
        public bool ColorSuccessfulTestBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Color regardless of candle direction", GroupName="Bar Colors", Order=1)]
        public bool ColorRegardlessOfCandleDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Intrabar coloring", GroupName="Bar Colors", Order=2)]
        public bool IntrabarColoring { get; set; }

        [XmlIgnore]
        [Display(Name="Bullish test bar", GroupName="Bar Colors", Order=3)]
        public Brush BullishTestBarColor { get; set; }
        [Browsable(false)] public string BullishTestBarColorSerialize { get { return Serialize.BrushToString(BullishTestBarColor); } set { BullishTestBarColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="Bearish test bar", GroupName="Bar Colors", Order=4)]
        public Brush BearishTestBarColor { get; set; }
        [Browsable(false)] public string BearishTestBarColorSerialize { get { return Serialize.BrushToString(BearishTestBarColor); } set { BearishTestBarColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty, Range(1, 5)]
        [Display(Name="Line width", GroupName="Display", Order=5)]
        public int AVWAPLineWidth { get; set; }

        // Display-only (not NinjaScriptProperty) so the generated factory signature is unchanged.
        [Range(1, 100)]
        [Display(Name="Signal hold (data series bars)", Description="How many completed data-series bars CrossSignal and TestSignal stay non-zero after the event. 1 = until the next bar closes. BarsSinceCross / BarsSinceTest keep counting regardless.", GroupName="Market Analyzer", Order=0)]
        public int SignalHoldBars { get; set; }

        // ---- V0005 signals (display-only, not NinjaScriptProperty) ----------------------------------
        [Display(Name="S.1 Enable Signals", Description="Markers on the chart when a completed data-series bar wicks an AVWAP (and closes back) or retests one it closed through.", GroupName="Signals", Order=1)]
        public bool EnableSignals { get; set; }

        [Display(Name="S.2 Wick Signals", Description="Data-series bar reaches the AVWAP (within the tolerance) and closes back on the side it came from.", GroupName="Signals", Order=2)]
        public bool EnableWickSignals { get; set; }

        [Display(Name="S.3 Retest Signals", Description="A data-series bar closes THROUGH an AVWAP (arms it); the first later wick back into it that closes on the break side. Drawn stronger (double arrow, R).", GroupName="Signals", Order=3)]
        public bool EnableRetestSignals { get; set; }

        [Display(Name="S.4 Signal AVWAPs", Description="Which AVWAPs can produce wick/retest signals. SessionHighLow = HOD/LOD and HOPD/LOPD. SessionHighLowAndTests adds Kris's TEST (progression) AVWAPs. AllAVWAPs also adds open and gap AVWAPs. Direction (breaks, X, cancels) always comes from HOD/LOD/HOPD/LOPD only.", GroupName="Signals", Order=4)]
        public AAVMTFSignalAnchors SignalAnchors { get; set; }

        [Range(0, 200)]
        [Display(Name="S.5 Wick tolerance (ticks)", Description="A wick that stops this close to the AVWAP still counts as touching it.", GroupName="Signals", Order=5)]
        public int SignalWickToleranceTicks { get; set; }

        [Range(0, 100)]
        [Display(Name="S.6 Min AVWAP age (data-series bars)", Description="1 = the first touch of a brand-new AVWAP counts if it is not on the anchor's own bar. When both sides fire on one bar, the retest wins, then the OLDER AVWAP.", GroupName="Signals", Order=6)]
        public int SignalMinAnchorAgeBars { get; set; }

        [Display(Name="S.7 Bias filter", Description="Shorts only when more AVWAPs are above price than below; longs only when more are below.", GroupName="Signals", Order=7)]
        public bool SignalBiasFilter { get; set; }

        [Range(0, 200)]
        [Display(Name="S.8 Stop buffer (ticks)", Description="Stop = beyond the signal bar's wick by this much.", GroupName="Signals", Order=8)]
        public int StopBufferTicks { get; set; }

        [Range(0, 20)]
        [Display(Name="S.9 Min reward:risk", Description="Target = the nearest other AVWAP beyond the entry. Below this R:R the marker is drawn in the weak colour.", GroupName="Signals", Order=9)]
        public double MinRewardRisk { get; set; }

        [Display(Name="S.10 Show stop / target", GroupName="Signals", Order=10)]
        public bool ShowStopTarget { get; set; }

        [Range(1, 500)]
        [Display(Name="S.11 Stop/target line length (chart bars)", GroupName="Signals", Order=11)]
        public int SignalLineBars { get; set; }

        [Range(1, 1000)]
        [Display(Name="S.12 Max signals drawn", Description="Older signal drawings are removed beyond this.", GroupName="Signals", Order=12)]
        public int MaxSignalDrawings { get; set; }

        [XmlIgnore]
        [Display(Name="S.13 Long signal color", GroupName="Signals", Order=13)]
        public Brush SignalLongColor { get; set; }
        [Browsable(false)] public string SignalLongColorSerialize { get { return Serialize.BrushToString(SignalLongColor); } set { SignalLongColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="S.14 Short signal color", GroupName="Signals", Order=14)]
        public Brush SignalShortColor { get; set; }
        [Browsable(false)] public string SignalShortColorSerialize { get { return Serialize.BrushToString(SignalShortColor); } set { SignalShortColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name="S.15 Weak signal color (below min R:R)", GroupName="Signals", Order=15)]
        public Brush SignalWeakColor { get; set; }
        [Browsable(false)] public string SignalWeakColorSerialize { get { return Serialize.BrushToString(SignalWeakColor); } set { SignalWeakColor = Serialize.StringToBrush(value); } }

        [Range(6, 40)]
        [Display(Name="S.16 Signal font size", GroupName="Signals", Order=16)]
        public int SignalFontSize { get; set; }

        [Display(Name="S.17 Sound alert", Description="Realtime only. Also posts to the Alerts window with instrument, period and AVWAP.", GroupName="Signals", Order=17)]
        public bool SignalSoundAlert { get; set; }

        [Display(Name="S.18 Long sound file", Description="A file in the NinjaTrader 8\\sounds folder, or a full path.", GroupName="Signals", Order=18)]
        public string SignalLongSound { get; set; }

        [Display(Name="S.19 Short sound file", GroupName="Signals", Order=19)]
        public string SignalShortSound { get; set; }

        [Display(Name="S.20 Write signal log", Description="Every signal and its forward outcome (target/stop first, MFE, MAE) appended to Documents\\NinjaTrader 8\\Mirror Logs\\AutoAVWAPSignalsV0005_<instrument>.log.", GroupName="Signals", Order=20)]
        public bool WriteSignalLog { get; set; }

        [Range(1, 500)]
        [Display(Name="S.21 Outcome window (data-series bars)", GroupName="Signals", Order=21)]
        public int SignalForwardBars { get; set; }

        [Display(Name="S.22 Show break markers", Description="A diamond where a data-series bar CLOSES THROUGH a session high/low AVWAP (at least the min break age old): the change of direction. ◆↓ below = short, ◆↑ above = long.", GroupName="Signals", Order=22)]
        public bool ShowBreakMarkers { get; set; }

        [Display(Name="S.23 Direction filter", Description="The last break sets the direction. Signals against it are drawn greyed with an X (and get no sound, plot value or stop/target).", GroupName="Signals", Order=23)]
        public bool UseDirectionState { get; set; }

        [Display(Name="S.24 Cancel on opposing break", Description="A new break cancels still-open signals in the other direction: they are re-drawn greyed with an X and their stop/target lines removed.", GroupName="Signals", Order=24)]
        public bool CancelOnOpposingBreak { get; set; }

        [Range(0, 100)]
        [Display(Name="S.25 Min break age (data-series bars)", Description="A close-through only changes direction if the AVWAP is at least this old. 2 ignores breaks of 1-bar-old AVWAPs, which 5-min bars cross constantly.", GroupName="Signals", Order=25)]
        public int MinBreakAgeBars { get; set; }

        [Range(1, 100)]
        [Display(Name="S.26 Cancel window (data-series bars)", Description="An opposing break only cancels signals from this many bars back. Older signals have already played out and are left alone.", GroupName="Signals", Order=26)]
        public int CancelWindowBars { get; set; }

        [Display(Name="S.27 Mixed bar = neutral", Description="When one data-series bar breaks one way and signals the other (e.g. closes above the HOPD AVWAP while rejecting the LOPD AVWAP), the direction resets to neutral and the signal is drawn with ⇅ instead of an X. The next clean break sets the direction again.", GroupName="Signals", Order=27)]
        public bool MixedBarNeutral { get; set; }

        [Display(Name="S.28 Show reward:risk on label", Description="Adds the R:R to the target (e.g. 1.5R) to each signal label. Off: the label shows only the arrow, R for retest, and the AVWAP. Weak signals (below S.9) are still drawn in the weak colour; set S.9 to 0 to stop that too.", GroupName="Signals", Order=28)]
        public bool ShowRewardRiskLabel { get; set; }

        [Range(0, 200)]
        [Display(Name="S.29 Close margin (ticks)", Description="A close within this many ticks of an AVWAP counts as ON the line: it is not a break, and a bar that wicked through but closed on the line still counts as holding it. Breaks need a close clearly on the other side of the last clear close. 0 = original exact-close rules.", GroupName="Signals", Order=29)]
        public int SignalCloseMarginTicks { get; set; }
        #endregion
    }
}

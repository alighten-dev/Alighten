#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// IMPORTANT: enums in ROOT namespace so NT generated wrappers always resolve them
namespace NinjaTrader.NinjaScript
{
    public enum AlightenAggRoundingMode
    {
        Nearest,
        Up,
        Down
    }

    public enum AlightenAtrSessionMode
    {
        AllDay,
        CurrentSession,
        Asia,
        London,
        NewYork
    }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenAggregationAdvisor : Indicator
    {
        private static readonly int[] AllowedAgg = new int[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };

        // Per-bar maps
        private Dictionary<int, Dictionary<double, double>> siAskByBar;
        private Dictionary<int, Dictionary<double, double>> siBidByBar;
        private Dictionary<int, Dictionary<double, double>> siTotByBar;

        private double siSessionOpenPrice = 0.0;

        // aggressor inference
        private bool   prevWasAsk;
        private double prevAsk;
        private double prevBid;

        // Tape speed (contracts/sec) on BIP1
        private struct TapePrint { public DateTime Time; public double Vol; }
        private Queue<TapePrint> tapeQ;
        private double tapeVolSum;
        private double tapeSpeedCps;
        private double tapeSpeedEma;

        // Session ATR (ticks) - EMA of TR (ticks)
        private double atrAllTicks;
        private double atrAsiaTicks;
        private double atrLondonTicks;
        private double atrNyTicks;

        // Current recommendation (hysteresis)
        private int currentAgg = 1;
        private int pendingAgg = -1;
        private int pendingAggCount = 0;

        // Active agg for the CURRENT primary bar while ticks arrive
        private int aggActive = 1;
        private int lastTickPrimaryIdx = -1;

        // Derived suggestions
        private double recMinRowVol = 0.0;
        private double recImbFact   = 4.0;
        private int    recStackLvls = 2;

        // Rolling per-bar stats
        private Queue<double> qBarRowP25;
        private Queue<double> qBarRatioP80;

        // Plots (Data Box)
        private Series<double> pRecAgg;
        private Series<double> pRecMinRowVol;
        private Series<double> pRecImbFact;
        private Series<double> pRecStackLvls;
        private Series<double> pAtrTicks;
        private Series<double> pTapeSpeed;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "AlightenAggregationAdvisor";
                Description              = "Advises SI aggregation settings using session-based ATR + tape speed + observed row/ratio stats.";
                Calculate                = Calculate.OnEachTick;
                IsOverlay                = true;
                DisplayInDataBox         = true;
                PaintPriceMarkers        = false;
                IsSuspendedWhileInactive = true;
                ShowTransparentPlotsInDataBox = true;

                // ATR
                AtrLength      = 14;
                AtrSessionMode = NinjaTrader.NinjaScript.AlightenAtrSessionMode.AllDay;

                // Session windows (assumes chart is Exchange/Chicago; adjust if needed)
                AsiaStartHHmm    = 1700; AsiaEndHHmm    = 0200; // overnight ok
                LondonStartHHmm  = 0200; LondonEndHHmm  = 0830;
                NewYorkStartHHmm = 0830; NewYorkEndHHmm = 1600;

                // Volatility targeting
                AutoTargetRowsByInstrument = true;
                TargetRows_ES              = 16.0;
                TargetRows_GC              = 18.0;
                TargetRows_Default         = 16.0;

                // Aggregation bounds
                MinAggTicks  = 1;
                MaxAggTicks  = 32;
                RoundingMode = NinjaTrader.NinjaScript.AlightenAggRoundingMode.Nearest;

                // Liquidity / tape
                UseTapeSpeedLiquidity = true;
                SpeedWindowMs         = 1000;
                BaselineEmaLength     = 200;
                LiquidityPower        = 0.50;

                // SI grid
                UseSessionOpenForAggregation = true;

                // Stats / stability
                StatsLookbackBars = 50;
                ConfirmBars       = 3;

                MinRowVolumeFloor = 0.0;
                MinRowVolumeCap   = 200000.0;
                ImbFactFloor      = 2.0;
                ImbFactCap        = 10.0;

                // Display
                ShowFixedReadout = true;
                ReadoutPosition  = TextPosition.TopRight;
                ReadoutFontSize  = 14;

                AddPlot(Brushes.Transparent, "RecAggTicks");
                AddPlot(Brushes.Transparent, "RecMinRowVol");
                AddPlot(Brushes.Transparent, "RecImbFact");
                AddPlot(Brushes.Transparent, "RecStackLvls");
                AddPlot(Brushes.Transparent, "ATRTicks");
                AddPlot(Brushes.Transparent, "TapeSpeedCps");
                return;
            }

            if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
                return;
            }

            if (State == State.DataLoaded)
            {
                siAskByBar = new Dictionary<int, Dictionary<double, double>>(2048);
                siBidByBar = new Dictionary<int, Dictionary<double, double>>(2048);
                siTotByBar = new Dictionary<int, Dictionary<double, double>>(2048);

                tapeQ        = new Queue<TapePrint>(8192);
                tapeVolSum   = 0.0;
                tapeSpeedCps = 0.0;
                tapeSpeedEma = 0.0;

                qBarRowP25   = new Queue<double>(StatsLookbackBars + 10);
                qBarRatioP80 = new Queue<double>(StatsLookbackBars + 10);

                currentAgg = ClampAgg(1);
                aggActive  = currentAgg;

                pRecAgg       = Values[0];
                pRecMinRowVol = Values[1];
                pRecImbFact   = Values[2];
                pRecStackLvls = Values[3];
                pAtrTicks     = Values[4];
                pTapeSpeed    = Values[5];
                return;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                OnTick_BIP1();
                return;
            }

            if (CurrentBar < 2)
            {
                PublishPlots(0, currentAgg, recMinRowVol, recImbFact, recStackLvls, 0.0, tapeSpeedCps);
                return;
            }

            if (IsFirstTickOfBar)
            {
                if (BarsArray[0].IsFirstBarOfSession)
                    siSessionOpenPrice = Open[0];

                int barJustClosed = CurrentBar - 1;

                UpdateSessionAtrFromClosedBar(barJustClosed);
                FinalizeBarStats(barJustClosed);

                if (UseTapeSpeedLiquidity)
                    tapeSpeedEma = EmaStep(tapeSpeedEma, tapeSpeedCps, BaselineEmaLength);

                double atrTicks = SelectAtrTicks();
                int proposedAgg = RecommendAggTicks(atrTicks);
                currentAgg = ApplyAggHysteresis(proposedAgg);

                UpdateDerivedRecommendations(currentAgg);

                if (ShowFixedReadout)
                    DrawReadout(currentAgg, atrTicks);

                if (barJustClosed % 25 == 0)
                    PruneBarMaps(Math.Max(0, barJustClosed - (StatsLookbackBars + 10)));
            }

            double atrNow = SelectAtrTicks();
            PublishPlots(0, currentAgg, recMinRowVol, recImbFact, recStackLvls, atrNow, tapeSpeedCps);
        }

        // -------------------- BIP1 ticks --------------------
        private void OnTick_BIP1()
        {
            if (CurrentBars[1] < 0)
                return;

            DateTime t = Times[1][0];

            if (SpeedWindowMs > 0)
                UpdateTapeSpeed_BIP1(t);

            int idx0 = BarsArray[0].GetBar(t);
            if (idx0 < 0)
                return;

            if (idx0 != lastTickPrimaryIdx)
            {
                lastTickPrimaryIdx = idx0;
                aggActive = currentAgg;
            }

            double close = BarsArray[1].GetClose(CurrentBars[1]);
            double ask   = BarsArray[1].GetAsk(CurrentBars[1]);
            double bid   = BarsArray[1].GetBid(CurrentBars[1]);
            double vol   = BarsArray[1].GetVolume(CurrentBars[1]);

            bool isAskAggressor = close >= ask || (close > bid && prevWasAsk);

            SI_AggregateTick(idx0, close, vol, isAskAggressor, aggActive);

            prevAsk = ask;
            prevBid = bid;
            prevWasAsk = isAskAggressor;
        }

        private void UpdateTapeSpeed_BIP1(DateTime t)
        {
            double vol = BarsArray[1].GetVolume(CurrentBars[1]);

            tapeQ.Enqueue(new TapePrint { Time = t, Vol = vol });
            tapeVolSum += vol;

            DateTime cutoff = t.AddMilliseconds(-SpeedWindowMs);
            while (tapeQ.Count > 0 && tapeQ.Peek().Time < cutoff)
            {
                TapePrint p = tapeQ.Dequeue();
                tapeVolSum -= p.Vol;
            }

            double windowSec = Math.Max(0.001, SpeedWindowMs / 1000.0);
            tapeSpeedCps = tapeVolSum / windowSec;
        }

        // -------------------- Session ATR --------------------
        private void UpdateSessionAtrFromClosedBar(int barIdx)
        {
            if (barIdx <= 0)
                return;

            double hi = BarsArray[0].GetHigh(barIdx);
            double lo = BarsArray[0].GetLow(barIdx);
            double pc = BarsArray[0].GetClose(barIdx - 1);

            double tr = Math.Max(hi - lo, Math.Max(Math.Abs(hi - pc), Math.Abs(lo - pc)));
            double trTicks = (TickSize > 0 ? tr / TickSize : 0.0);

            atrAllTicks = EmaStep(atrAllTicks, trTicks, AtrLength);

            DateTime bt = BarsArray[0].GetTime(barIdx);
            int hhmm = (ToTime(bt) / 100);

            if (IsInWindow(hhmm, AsiaStartHHmm, AsiaEndHHmm))
                atrAsiaTicks = EmaStep(atrAsiaTicks, trTicks, AtrLength);

            if (IsInWindow(hhmm, LondonStartHHmm, LondonEndHHmm))
                atrLondonTicks = EmaStep(atrLondonTicks, trTicks, AtrLength);

            if (IsInWindow(hhmm, NewYorkStartHHmm, NewYorkEndHHmm))
                atrNyTicks = EmaStep(atrNyTicks, trTicks, AtrLength);
        }

        private double SelectAtrTicks()
        {
            double builtin = (TickSize > 0 ? ATR(AtrLength)[0] / TickSize : 0.0);

            var mode = AtrSessionMode;

            if (mode == NinjaTrader.NinjaScript.AlightenAtrSessionMode.AllDay)
                return (atrAllTicks > 0 ? atrAllTicks : builtin);

            if (mode == NinjaTrader.NinjaScript.AlightenAtrSessionMode.Asia)
                return (atrAsiaTicks > 0 ? atrAsiaTicks : builtin);

            if (mode == NinjaTrader.NinjaScript.AlightenAtrSessionMode.London)
                return (atrLondonTicks > 0 ? atrLondonTicks : builtin);

            if (mode == NinjaTrader.NinjaScript.AlightenAtrSessionMode.NewYork)
                return (atrNyTicks > 0 ? atrNyTicks : builtin);

            // CurrentSession
            int hhmm = (ToTime(Time[0]) / 100);
            if (IsInWindow(hhmm, AsiaStartHHmm, AsiaEndHHmm) && atrAsiaTicks > 0) return atrAsiaTicks;
            if (IsInWindow(hhmm, LondonStartHHmm, LondonEndHHmm) && atrLondonTicks > 0) return atrLondonTicks;
            if (IsInWindow(hhmm, NewYorkStartHHmm, NewYorkEndHHmm) && atrNyTicks > 0) return atrNyTicks;

            return (atrAllTicks > 0 ? atrAllTicks : builtin);
        }

        private bool IsInWindow(int hhmm, int start, int end)
        {
            if (start == end) return true;
            if (start < end) return (hhmm >= start && hhmm < end);
            return (hhmm >= start || hhmm < end); // overnight
        }

        // -------------------- Aggregation recommendation --------------------
        private int RecommendAggTicks(double atrTicks)
        {
            double targetRows = ResolveTargetRows();
            double baseAggRaw = (targetRows <= 0.0) ? 1.0 : (atrTicks / targetRows);
            if (baseAggRaw < 1.0) baseAggRaw = 1.0;

            double liqAdj = 1.0;
            if (UseTapeSpeedLiquidity && LiquidityPower > 0.0)
            {
                double eps = 1e-9;
                double baseV = Math.Max(eps, tapeSpeedEma);
                double curV  = Math.Max(eps, tapeSpeedCps);
                double ratio = curV / baseV;

                liqAdj = Math.Pow(1.0 / ratio, LiquidityPower);
                if (liqAdj < 0.50) liqAdj = 0.50;
                if (liqAdj > 2.00) liqAdj = 2.00;
            }

            double raw = baseAggRaw * liqAdj;
            return RoundToAllowed(raw, MinAggTicks, MaxAggTicks, RoundingMode);
        }

        private int ApplyAggHysteresis(int proposed)
        {
            if (proposed == currentAgg)
            {
                pendingAgg = -1;
                pendingAggCount = 0;
                return currentAgg;
            }

            if (pendingAgg != proposed)
            {
                pendingAgg = proposed;
                pendingAggCount = 1;
                return currentAgg;
            }

            pendingAggCount++;
            if (pendingAggCount >= Math.Max(1, ConfirmBars))
            {
                currentAgg = proposed;
                pendingAgg = -1;
                pendingAggCount = 0;
            }

            return currentAgg;
        }

        private int RoundToAllowed(double raw, int minAgg, int maxAgg, NinjaTrader.NinjaScript.AlightenAggRoundingMode mode)
        {
            int minC = ClampAgg(minAgg);
            int maxC = ClampAgg(maxAgg);

            raw = Math.Max(minC, Math.Min(maxC, raw));

            List<int> ladder = new List<int>(AllowedAgg.Length);
            for (int i = 0; i < AllowedAgg.Length; i++)
            {
                int v = AllowedAgg[i];
                if (v >= minC && v <= maxC)
                    ladder.Add(v);
            }
            if (ladder.Count == 0)
                return ClampAgg((int)Math.Round(raw));

            if (mode == NinjaTrader.NinjaScript.AlightenAggRoundingMode.Nearest)
            {
                int best = ladder[0];
                double bestD = Math.Abs(raw - best);
                for (int i = 1; i < ladder.Count; i++)
                {
                    double d = Math.Abs(raw - ladder[i]);
                    if (d < bestD) { bestD = d; best = ladder[i]; }
                }
                return best;
            }

            if (mode == NinjaTrader.NinjaScript.AlightenAggRoundingMode.Up)
            {
                for (int i = 0; i < ladder.Count; i++)
                    if (ladder[i] >= raw)
                        return ladder[i];
                return ladder[ladder.Count - 1];
            }

            for (int i = ladder.Count - 1; i >= 0; i--)
                if (ladder[i] <= raw)
                    return ladder[i];
            return ladder[0];
        }

        private int ClampAgg(int v)
        {
            if (v < 1) v = 1;
            if (v > 256) v = 256;
            return v;
        }

        private double ResolveTargetRows()
        {
            if (!AutoTargetRowsByInstrument || Instrument == null || Instrument.MasterInstrument == null)
                return TargetRows_Default;

            string name = (Instrument.MasterInstrument.Name ?? "").ToUpperInvariant();

            if (name.StartsWith("ES") || name.StartsWith("MES"))
                return TargetRows_ES;

            if (name.StartsWith("GC") || name.StartsWith("MGC"))
                return TargetRows_GC;

            return TargetRows_Default;
        }

        // -------------------- SI aggregation + stats --------------------
        private void SI_AggregateTick(int primaryBarIdx, double price, double vol, bool isAskAggressor, int aggTicks)
        {
            if (primaryBarIdx < 0)
                return;

            int ticksPerBin = Math.Max(1, aggTicks);
            double binInterval = ticksPerBin * TickSize;

            if (!siAskByBar.ContainsKey(primaryBarIdx)) siAskByBar[primaryBarIdx] = new Dictionary<double, double>(256);
            if (!siBidByBar.ContainsKey(primaryBarIdx)) siBidByBar[primaryBarIdx] = new Dictionary<double, double>(256);
            if (!siTotByBar.ContainsKey(primaryBarIdx)) siTotByBar[primaryBarIdx] = new Dictionary<double, double>(256);

            double origin;
            if (UseSessionOpenForAggregation && siSessionOpenPrice != 0.0)
                origin = siSessionOpenPrice;
            else
                origin = BarsArray[0].GetLow(primaryBarIdx);

            origin = Math.Round(origin / TickSize) * TickSize;

            double binsFromOrigin = Math.Floor(((price - origin) / binInterval) + 1e-9);
            double binStart = origin + binsFromOrigin * binInterval;
            binStart = Math.Round(binStart / TickSize) * TickSize;

            var askMap = siAskByBar[primaryBarIdx];
            var bidMap = siBidByBar[primaryBarIdx];
            var totMap = siTotByBar[primaryBarIdx];

            double ask = 0, bid = 0;
            askMap.TryGetValue(binStart, out ask);
            bidMap.TryGetValue(binStart, out bid);

            if (isAskAggressor) ask += vol;
            else                bid += vol;

            double tot = ask + bid;

            askMap[binStart] = ask;
            bidMap[binStart] = bid;
            totMap[binStart] = tot;
        }

        private void FinalizeBarStats(int barIdx)
        {
            Dictionary<double, double> totRows;
            if (siTotByBar == null || !siTotByBar.TryGetValue(barIdx, out totRows) || totRows == null || totRows.Count < 2)
                return;

            Dictionary<double, double> askRows = null, bidRows = null;
            siAskByBar.TryGetValue(barIdx, out askRows);
            siBidByBar.TryGetValue(barIdx, out bidRows);

            List<double> rowTotals = totRows.Values.Where(v => v > 0.0).ToList();
            if (rowTotals.Count < 2)
                return;

            double barP25 = Percentile(rowTotals, 25.0);
            EnqueueBounded(qBarRowP25, barP25, StatsLookbackBars);

            double rowFilter = Math.Max(1.0, barP25);

            var keys = totRows.Keys.OrderBy(k => k).ToList();
            List<double> ratios = new List<double>(keys.Count * 2);

            double Read(Dictionary<double, double> map, double key)
            {
                if (map == null) return 0.0;
                double v;
                return map.TryGetValue(key, out v) ? v : 0.0;
            }

            for (int i = 1; i < keys.Count; i++)
            {
                double kPrev = keys[i - 1];
                double kCurr = keys[i];

                double totPrev = totRows.ContainsKey(kPrev) ? totRows[kPrev] : 0.0;
                double totCurr = totRows.ContainsKey(kCurr) ? totRows[kCurr] : 0.0;
                if (totPrev < rowFilter || totCurr < rowFilter) continue;

                double askCurr = Read(askRows, kCurr);
                double bidPrev = Read(bidRows, kPrev);

                if (askCurr >= rowFilter && bidPrev >= rowFilter)
                    ratios.Add(askCurr / Math.Max(1.0, bidPrev));
            }

            for (int i = 0; i < keys.Count - 1; i++)
            {
                double kCurr = keys[i];
                double kNext = keys[i + 1];

                double totCurr = totRows.ContainsKey(kCurr) ? totRows[kCurr] : 0.0;
                double totNext = totRows.ContainsKey(kNext) ? totRows[kNext] : 0.0;
                if (totCurr < rowFilter || totNext < rowFilter) continue;

                double bidCurr = Read(bidRows, kCurr);
                double askNext = Read(askRows, kNext);

                if (bidCurr >= rowFilter && askNext >= rowFilter)
                    ratios.Add(bidCurr / Math.Max(1.0, askNext));
            }

            if (ratios.Count >= 5)
            {
                double barR80 = Percentile(ratios, 80.0);
                EnqueueBounded(qBarRatioP80, barR80, StatsLookbackBars);
            }
        }

        private void UpdateDerivedRecommendations(int aggTicks)
        {
            if (qBarRowP25.Count >= 5)
            {
                double rollMedP25 = Percentile(qBarRowP25.ToList(), 50.0);
                recMinRowVol = Clamp(rollMedP25, MinRowVolumeFloor, MinRowVolumeCap);
            }
            else recMinRowVol = 0.0;

            if (qBarRatioP80.Count >= 5)
            {
                double rollMedR80 = Percentile(qBarRatioP80.ToList(), 50.0);
                double target = rollMedR80 * 0.90;
                recImbFact = Clamp(target, ImbFactFloor, ImbFactCap);
            }
            else
            {
                recImbFact = Clamp(aggTicks <= 2 ? 6.0 : aggTicks <= 4 ? 5.0 : aggTicks <= 8 ? 4.0 : 3.5, ImbFactFloor, ImbFactCap);
            }

            recStackLvls = (aggTicks <= 4) ? 3 : 2;
        }

        private void DrawReadout(int aggTicks, double atrTicks)
        {
            string txt =
                "Alighten Aggregation Advisor\n" +
                "ATR Mode      : " + AtrSessionMode + "\n" +
                "Rec Agg (ticks): " + aggTicks + "\n" +
                "Rec MinRowVol : " + recMinRowVol.ToString("0") + "\n" +
                "Rec ImbFact   : " + recImbFact.ToString("0.0") + "\n" +
                "Rec StackLvls : " + recStackLvls + "\n" +
                "ATR           : " + atrTicks.ToString("0.0") + " ticks\n" +
                (UseTapeSpeedLiquidity ? ("Tape Speed    : " + tapeSpeedCps.ToString("0") + " /s (base " + tapeSpeedEma.ToString("0") + " /s)\n") : "") +
                "\nApply to OFT:\n" +
                "SIAggregationIntervalTicks = " + aggTicks + "\n" +
                "SIMinRowVolume            = " + recMinRowVol.ToString("0") + "\n" +
                "SIImbFact                 = " + recImbFact.ToString("0.0") + "\n" +
                "SIStackedImbalanceLookback= " + recStackLvls;

            Draw.TextFixed(this, "AOF_AGG_ADVISOR_READOUT", txt, ReadoutPosition,
                Brushes.White, new SimpleFont("Arial", ReadoutFontSize),
                Brushes.Transparent, Brushes.Transparent, 0);
        }

        private void PublishPlots(int barsAgo, int agg, double minRow, double imb, int stack, double atrTicks, double tape)
        {
            pRecAgg[barsAgo]       = agg;
            pRecMinRowVol[barsAgo] = minRow;
            pRecImbFact[barsAgo]   = imb;
            pRecStackLvls[barsAgo] = stack;
            pAtrTicks[barsAgo]     = atrTicks;
            pTapeSpeed[barsAgo]    = tape;
        }

        private void EnqueueBounded(Queue<double> q, double v, int max)
        {
            q.Enqueue(v);
            while (q.Count > Math.Max(1, max))
                q.Dequeue();
        }

        private double Percentile(List<double> values, double p)
        {
            if (values == null || values.Count == 0) return 0.0;
            values.Sort();
            if (values.Count == 1) return values[0];

            double rank = (p / 100.0) * (values.Count - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            if (hi <= lo) return values[lo];

            double w = rank - lo;
            return values[lo] * (1.0 - w) + values[hi] * w;
        }

        private double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private double EmaStep(double prev, double value, int len)
        {
            if (len <= 1) return value;
            double a = 2.0 / (len + 1.0);
            return prev + a * (value - prev);
        }

        private void PruneBarMaps(int oldestKeepBar)
        {
            PruneMap(siAskByBar, oldestKeepBar);
            PruneMap(siBidByBar, oldestKeepBar);
            PruneMap(siTotByBar, oldestKeepBar);
        }

        private void PruneMap<T>(Dictionary<int, T> map, int oldestKeepBar)
        {
            if (map == null || map.Count == 0) return;
            var keys = map.Keys.Where(k => k < oldestKeepBar).ToList();
            for (int i = 0; i < keys.Count; i++)
                map.Remove(keys[i]);
        }

        // -------------------- Inputs --------------------
        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ATR Length", GroupName = "ATR", Order = 0)]
        public int AtrLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ATR Session Mode", GroupName = "ATR", Order = 10)]
        public NinjaTrader.NinjaScript.AlightenAtrSessionMode AtrSessionMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Asia Start (HHmm)", GroupName = "ATR Sessions", Order = 0)]
        public int AsiaStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Asia End (HHmm)", GroupName = "ATR Sessions", Order = 10)]
        public int AsiaEndHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "London Start (HHmm)", GroupName = "ATR Sessions", Order = 20)]
        public int LondonStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "London End (HHmm)", GroupName = "ATR Sessions", Order = 30)]
        public int LondonEndHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "New York Start (HHmm)", GroupName = "ATR Sessions", Order = 40)]
        public int NewYorkStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "New York End (HHmm)", GroupName = "ATR Sessions", Order = 50)]
        public int NewYorkEndHHmm { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Auto TargetRows By Instrument", GroupName = "Volatility", Order = 10)]
        public bool AutoTargetRowsByInstrument { get; set; }

        [NinjaScriptProperty]
        [Range(6.0, 40.0)]
        [Display(Name = "TargetRows ES/MES", GroupName = "Volatility", Order = 20)]
        public double TargetRows_ES { get; set; }

        [NinjaScriptProperty]
        [Range(6.0, 40.0)]
        [Display(Name = "TargetRows GC/MGC", GroupName = "Volatility", Order = 30)]
        public double TargetRows_GC { get; set; }

        [NinjaScriptProperty]
        [Range(6.0, 40.0)]
        [Display(Name = "TargetRows Default", GroupName = "Volatility", Order = 40)]
        public double TargetRows_Default { get; set; }

        [NinjaScriptProperty]
        [Range(1, 64)]
        [Display(Name = "Min Agg Ticks", GroupName = "Aggregation", Order = 0)]
        public int MinAggTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 64)]
        [Display(Name = "Max Agg Ticks", GroupName = "Aggregation", Order = 10)]
        public int MaxAggTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Rounding Mode", GroupName = "Aggregation", Order = 20)]
        public NinjaTrader.NinjaScript.AlightenAggRoundingMode RoundingMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Tape Speed Liquidity", GroupName = "Liquidity", Order = 0)]
        public bool UseTapeSpeedLiquidity { get; set; }

        [NinjaScriptProperty]
        [Range(100, 10000)]
        [Display(Name = "Speed Window (ms)", GroupName = "Liquidity", Order = 10)]
        public int SpeedWindowMs { get; set; }

        [NinjaScriptProperty]
        [Range(10, 2000)]
        [Display(Name = "Baseline EMA Length", GroupName = "Liquidity", Order = 20)]
        public int BaselineEmaLength { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 2.0)]
        [Display(Name = "Liquidity Power", GroupName = "Liquidity", Order = 30)]
        public double LiquidityPower { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Session Open For Aggregation", GroupName = "SI Grid", Order = 0)]
        public bool UseSessionOpenForAggregation { get; set; }

        [NinjaScriptProperty]
        [Range(10, 300)]
        [Display(Name = "Stats Lookback Bars", GroupName = "Stability", Order = 0)]
        public int StatsLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Confirm Bars (Agg)", GroupName = "Stability", Order = 10)]
        public int ConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 2000000.0)]
        [Display(Name = "MinRowVol Floor", GroupName = "Output Limits", Order = 0)]
        public double MinRowVolumeFloor { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 2000000.0)]
        [Display(Name = "MinRowVol Cap", GroupName = "Output Limits", Order = 10)]
        public double MinRowVolumeCap { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 20.0)]
        [Display(Name = "ImbFact Floor", GroupName = "Output Limits", Order = 20)]
        public double ImbFactFloor { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 50.0)]
        [Display(Name = "ImbFact Cap", GroupName = "Output Limits", Order = 30)]
        public double ImbFactCap { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Fixed Readout", GroupName = "Display", Order = 0)]
        public bool ShowFixedReadout { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Readout Position", GroupName = "Display", Order = 10)]
        public TextPosition ReadoutPosition { get; set; }

        [NinjaScriptProperty]
        [Range(8, 36)]
        [Display(Name = "Readout Font Size", GroupName = "Display", Order = 20)]
        public int ReadoutFontSize { get; set; }

        // Outputs (Data Box)
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> RecAggTicks { get { return pRecAgg; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> RecMinRowVol { get { return pRecMinRowVol; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> RecImbFact { get { return pRecImbFact; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> RecStackLvls { get { return pRecStackLvls; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ATRTicks { get { return pAtrTicks; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> TapeSpeedCps { get { return pTapeSpeed; } }
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenAggregationAdvisor[] cacheAlightenAggregationAdvisor;
		public AlightenAggregationAdvisor AlightenAggregationAdvisor(int atrLength, NinjaTrader.NinjaScript.AlightenAtrSessionMode atrSessionMode, int asiaStartHHmm, int asiaEndHHmm, int londonStartHHmm, int londonEndHHmm, int newYorkStartHHmm, int newYorkEndHHmm, bool autoTargetRowsByInstrument, double targetRows_ES, double targetRows_GC, double targetRows_Default, int minAggTicks, int maxAggTicks, NinjaTrader.NinjaScript.AlightenAggRoundingMode roundingMode, bool useTapeSpeedLiquidity, int speedWindowMs, int baselineEmaLength, double liquidityPower, bool useSessionOpenForAggregation, int statsLookbackBars, int confirmBars, double minRowVolumeFloor, double minRowVolumeCap, double imbFactFloor, double imbFactCap, bool showFixedReadout, TextPosition readoutPosition, int readoutFontSize)
		{
			return AlightenAggregationAdvisor(Input, atrLength, atrSessionMode, asiaStartHHmm, asiaEndHHmm, londonStartHHmm, londonEndHHmm, newYorkStartHHmm, newYorkEndHHmm, autoTargetRowsByInstrument, targetRows_ES, targetRows_GC, targetRows_Default, minAggTicks, maxAggTicks, roundingMode, useTapeSpeedLiquidity, speedWindowMs, baselineEmaLength, liquidityPower, useSessionOpenForAggregation, statsLookbackBars, confirmBars, minRowVolumeFloor, minRowVolumeCap, imbFactFloor, imbFactCap, showFixedReadout, readoutPosition, readoutFontSize);
		}

		public AlightenAggregationAdvisor AlightenAggregationAdvisor(ISeries<double> input, int atrLength, NinjaTrader.NinjaScript.AlightenAtrSessionMode atrSessionMode, int asiaStartHHmm, int asiaEndHHmm, int londonStartHHmm, int londonEndHHmm, int newYorkStartHHmm, int newYorkEndHHmm, bool autoTargetRowsByInstrument, double targetRows_ES, double targetRows_GC, double targetRows_Default, int minAggTicks, int maxAggTicks, NinjaTrader.NinjaScript.AlightenAggRoundingMode roundingMode, bool useTapeSpeedLiquidity, int speedWindowMs, int baselineEmaLength, double liquidityPower, bool useSessionOpenForAggregation, int statsLookbackBars, int confirmBars, double minRowVolumeFloor, double minRowVolumeCap, double imbFactFloor, double imbFactCap, bool showFixedReadout, TextPosition readoutPosition, int readoutFontSize)
		{
			if (cacheAlightenAggregationAdvisor != null)
				for (int idx = 0; idx < cacheAlightenAggregationAdvisor.Length; idx++)
					if (cacheAlightenAggregationAdvisor[idx] != null && cacheAlightenAggregationAdvisor[idx].AtrLength == atrLength && cacheAlightenAggregationAdvisor[idx].AtrSessionMode == atrSessionMode && cacheAlightenAggregationAdvisor[idx].AsiaStartHHmm == asiaStartHHmm && cacheAlightenAggregationAdvisor[idx].AsiaEndHHmm == asiaEndHHmm && cacheAlightenAggregationAdvisor[idx].LondonStartHHmm == londonStartHHmm && cacheAlightenAggregationAdvisor[idx].LondonEndHHmm == londonEndHHmm && cacheAlightenAggregationAdvisor[idx].NewYorkStartHHmm == newYorkStartHHmm && cacheAlightenAggregationAdvisor[idx].NewYorkEndHHmm == newYorkEndHHmm && cacheAlightenAggregationAdvisor[idx].AutoTargetRowsByInstrument == autoTargetRowsByInstrument && cacheAlightenAggregationAdvisor[idx].TargetRows_ES == targetRows_ES && cacheAlightenAggregationAdvisor[idx].TargetRows_GC == targetRows_GC && cacheAlightenAggregationAdvisor[idx].TargetRows_Default == targetRows_Default && cacheAlightenAggregationAdvisor[idx].MinAggTicks == minAggTicks && cacheAlightenAggregationAdvisor[idx].MaxAggTicks == maxAggTicks && cacheAlightenAggregationAdvisor[idx].RoundingMode == roundingMode && cacheAlightenAggregationAdvisor[idx].UseTapeSpeedLiquidity == useTapeSpeedLiquidity && cacheAlightenAggregationAdvisor[idx].SpeedWindowMs == speedWindowMs && cacheAlightenAggregationAdvisor[idx].BaselineEmaLength == baselineEmaLength && cacheAlightenAggregationAdvisor[idx].LiquidityPower == liquidityPower && cacheAlightenAggregationAdvisor[idx].UseSessionOpenForAggregation == useSessionOpenForAggregation && cacheAlightenAggregationAdvisor[idx].StatsLookbackBars == statsLookbackBars && cacheAlightenAggregationAdvisor[idx].ConfirmBars == confirmBars && cacheAlightenAggregationAdvisor[idx].MinRowVolumeFloor == minRowVolumeFloor && cacheAlightenAggregationAdvisor[idx].MinRowVolumeCap == minRowVolumeCap && cacheAlightenAggregationAdvisor[idx].ImbFactFloor == imbFactFloor && cacheAlightenAggregationAdvisor[idx].ImbFactCap == imbFactCap && cacheAlightenAggregationAdvisor[idx].ShowFixedReadout == showFixedReadout && cacheAlightenAggregationAdvisor[idx].ReadoutPosition == readoutPosition && cacheAlightenAggregationAdvisor[idx].ReadoutFontSize == readoutFontSize && cacheAlightenAggregationAdvisor[idx].EqualsInput(input))
						return cacheAlightenAggregationAdvisor[idx];
			return CacheIndicator<AlightenAggregationAdvisor>(new AlightenAggregationAdvisor(){ AtrLength = atrLength, AtrSessionMode = atrSessionMode, AsiaStartHHmm = asiaStartHHmm, AsiaEndHHmm = asiaEndHHmm, LondonStartHHmm = londonStartHHmm, LondonEndHHmm = londonEndHHmm, NewYorkStartHHmm = newYorkStartHHmm, NewYorkEndHHmm = newYorkEndHHmm, AutoTargetRowsByInstrument = autoTargetRowsByInstrument, TargetRows_ES = targetRows_ES, TargetRows_GC = targetRows_GC, TargetRows_Default = targetRows_Default, MinAggTicks = minAggTicks, MaxAggTicks = maxAggTicks, RoundingMode = roundingMode, UseTapeSpeedLiquidity = useTapeSpeedLiquidity, SpeedWindowMs = speedWindowMs, BaselineEmaLength = baselineEmaLength, LiquidityPower = liquidityPower, UseSessionOpenForAggregation = useSessionOpenForAggregation, StatsLookbackBars = statsLookbackBars, ConfirmBars = confirmBars, MinRowVolumeFloor = minRowVolumeFloor, MinRowVolumeCap = minRowVolumeCap, ImbFactFloor = imbFactFloor, ImbFactCap = imbFactCap, ShowFixedReadout = showFixedReadout, ReadoutPosition = readoutPosition, ReadoutFontSize = readoutFontSize }, input, ref cacheAlightenAggregationAdvisor);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenAggregationAdvisor AlightenAggregationAdvisor(int atrLength, NinjaTrader.NinjaScript.AlightenAtrSessionMode atrSessionMode, int asiaStartHHmm, int asiaEndHHmm, int londonStartHHmm, int londonEndHHmm, int newYorkStartHHmm, int newYorkEndHHmm, bool autoTargetRowsByInstrument, double targetRows_ES, double targetRows_GC, double targetRows_Default, int minAggTicks, int maxAggTicks, NinjaTrader.NinjaScript.AlightenAggRoundingMode roundingMode, bool useTapeSpeedLiquidity, int speedWindowMs, int baselineEmaLength, double liquidityPower, bool useSessionOpenForAggregation, int statsLookbackBars, int confirmBars, double minRowVolumeFloor, double minRowVolumeCap, double imbFactFloor, double imbFactCap, bool showFixedReadout, TextPosition readoutPosition, int readoutFontSize)
		{
			return indicator.AlightenAggregationAdvisor(Input, atrLength, atrSessionMode, asiaStartHHmm, asiaEndHHmm, londonStartHHmm, londonEndHHmm, newYorkStartHHmm, newYorkEndHHmm, autoTargetRowsByInstrument, targetRows_ES, targetRows_GC, targetRows_Default, minAggTicks, maxAggTicks, roundingMode, useTapeSpeedLiquidity, speedWindowMs, baselineEmaLength, liquidityPower, useSessionOpenForAggregation, statsLookbackBars, confirmBars, minRowVolumeFloor, minRowVolumeCap, imbFactFloor, imbFactCap, showFixedReadout, readoutPosition, readoutFontSize);
		}

		public Indicators.AlightenAggregationAdvisor AlightenAggregationAdvisor(ISeries<double> input , int atrLength, NinjaTrader.NinjaScript.AlightenAtrSessionMode atrSessionMode, int asiaStartHHmm, int asiaEndHHmm, int londonStartHHmm, int londonEndHHmm, int newYorkStartHHmm, int newYorkEndHHmm, bool autoTargetRowsByInstrument, double targetRows_ES, double targetRows_GC, double targetRows_Default, int minAggTicks, int maxAggTicks, NinjaTrader.NinjaScript.AlightenAggRoundingMode roundingMode, bool useTapeSpeedLiquidity, int speedWindowMs, int baselineEmaLength, double liquidityPower, bool useSessionOpenForAggregation, int statsLookbackBars, int confirmBars, double minRowVolumeFloor, double minRowVolumeCap, double imbFactFloor, double imbFactCap, bool showFixedReadout, TextPosition readoutPosition, int readoutFontSize)
		{
			return indicator.AlightenAggregationAdvisor(input, atrLength, atrSessionMode, asiaStartHHmm, asiaEndHHmm, londonStartHHmm, londonEndHHmm, newYorkStartHHmm, newYorkEndHHmm, autoTargetRowsByInstrument, targetRows_ES, targetRows_GC, targetRows_Default, minAggTicks, maxAggTicks, roundingMode, useTapeSpeedLiquidity, speedWindowMs, baselineEmaLength, liquidityPower, useSessionOpenForAggregation, statsLookbackBars, confirmBars, minRowVolumeFloor, minRowVolumeCap, imbFactFloor, imbFactCap, showFixedReadout, readoutPosition, readoutFontSize);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenAggregationAdvisor AlightenAggregationAdvisor(int atrLength, NinjaTrader.NinjaScript.AlightenAtrSessionMode atrSessionMode, int asiaStartHHmm, int asiaEndHHmm, int londonStartHHmm, int londonEndHHmm, int newYorkStartHHmm, int newYorkEndHHmm, bool autoTargetRowsByInstrument, double targetRows_ES, double targetRows_GC, double targetRows_Default, int minAggTicks, int maxAggTicks, NinjaTrader.NinjaScript.AlightenAggRoundingMode roundingMode, bool useTapeSpeedLiquidity, int speedWindowMs, int baselineEmaLength, double liquidityPower, bool useSessionOpenForAggregation, int statsLookbackBars, int confirmBars, double minRowVolumeFloor, double minRowVolumeCap, double imbFactFloor, double imbFactCap, bool showFixedReadout, TextPosition readoutPosition, int readoutFontSize)
		{
			return indicator.AlightenAggregationAdvisor(Input, atrLength, atrSessionMode, asiaStartHHmm, asiaEndHHmm, londonStartHHmm, londonEndHHmm, newYorkStartHHmm, newYorkEndHHmm, autoTargetRowsByInstrument, targetRows_ES, targetRows_GC, targetRows_Default, minAggTicks, maxAggTicks, roundingMode, useTapeSpeedLiquidity, speedWindowMs, baselineEmaLength, liquidityPower, useSessionOpenForAggregation, statsLookbackBars, confirmBars, minRowVolumeFloor, minRowVolumeCap, imbFactFloor, imbFactCap, showFixedReadout, readoutPosition, readoutFontSize);
		}

		public Indicators.AlightenAggregationAdvisor AlightenAggregationAdvisor(ISeries<double> input , int atrLength, NinjaTrader.NinjaScript.AlightenAtrSessionMode atrSessionMode, int asiaStartHHmm, int asiaEndHHmm, int londonStartHHmm, int londonEndHHmm, int newYorkStartHHmm, int newYorkEndHHmm, bool autoTargetRowsByInstrument, double targetRows_ES, double targetRows_GC, double targetRows_Default, int minAggTicks, int maxAggTicks, NinjaTrader.NinjaScript.AlightenAggRoundingMode roundingMode, bool useTapeSpeedLiquidity, int speedWindowMs, int baselineEmaLength, double liquidityPower, bool useSessionOpenForAggregation, int statsLookbackBars, int confirmBars, double minRowVolumeFloor, double minRowVolumeCap, double imbFactFloor, double imbFactCap, bool showFixedReadout, TextPosition readoutPosition, int readoutFontSize)
		{
			return indicator.AlightenAggregationAdvisor(input, atrLength, atrSessionMode, asiaStartHHmm, asiaEndHHmm, londonStartHHmm, londonEndHHmm, newYorkStartHHmm, newYorkEndHHmm, autoTargetRowsByInstrument, targetRows_ES, targetRows_GC, targetRows_Default, minAggTicks, maxAggTicks, roundingMode, useTapeSpeedLiquidity, speedWindowMs, baselineEmaLength, liquidityPower, useSessionOpenForAggregation, statsLookbackBars, confirmBars, minRowVolumeFloor, minRowVolumeCap, imbFactFloor, imbFactCap, showFixedReadout, readoutPosition, readoutFontSize);
		}
	}
}

#endregion

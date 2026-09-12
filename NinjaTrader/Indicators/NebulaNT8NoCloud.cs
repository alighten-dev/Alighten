#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// Converted from the TradingView Pine Script "Nebula v2.2" supplied by the user.
// Original source is MPL-2.0 and credits TraderOracle plus the component authors named there.
// TradingView-only library calls are reproduced locally where practical.

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NebulaNT8NoCloud : Indicator
    {
        private const int UpTrend = 1;
        private const int DownTrend = 2;
        private const int VSqueeze = 4, VTramp = 4, VBands = 2, VLuxRev = 3, VEarlyRev = 2, VDeadRev = 2, VShark = 2;

        private EMA ema9, ema21, ema12, ema26;
        private RSI rsi14, rsiMain;
        private Series<double> macdSeries, macdSignalSeries, varMaSeries, fanSeries, mgSeries, hemaSeries, bHSeries;
        private Series<double> waeFastSeries, waeSlowSeries, squeezeValSeries, lazySeries;
        private Series<double> adxSeries, pdiSmoothSeries, mdiSmoothSeries, trSmoothSeries;
        private Series<bool> brightGreenSeries, brightRedSeries, plotBuySeries, plotSellSeries, upVodkaSeries, downVodkaSeries;
        private Series<int> waveSeries;

        private Queue<int> buyWatch = new Queue<int>();
        private Queue<int> sellWatch = new Queue<int>();
        private int squeezeGreenCount, squeezeRedCount;
        private bool squeezePosPrev, squeezeNegPrev;
        private double priorPl = double.NaN, priorPh = double.NaN;
        private double supportTop = double.NaN, supportBottom = double.NaN, resistanceTop = double.NaN, resistanceBottom = double.NaN;
        private int supportBreakBar = -1, resistanceBreakBar = -1;
        private int lastAlertBar = -1;

        private Brush bigGreen = Brushes.Lime;
        private Brush bigRed = Brushes.Red;

        // User-configurable colors (see Colors group). bull/bear drive candles + primary signals.
        private Brush bullBrush = Brushes.Lime;
        private Brush bearBrush = Brushes.Red;
        private Brush partialProfitBrush = Brushes.Magenta;   // partial-profit "✓" + retest "!"
        private Brush skullBrush = Brushes.Yellow;            // 9/21 skull triangles + reversal outline
        private Brush vectorMediumUpBrush = Brushes.DodgerBlue;
        private Brush vectorMediumDownBrush = Brushes.Violet;

        // Sparse per-bar signal store (only bars that have a mark). Rendered in OnRender = cost scales with
        // visible bars, not with total history, and creates zero persistent drawing objects.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, BarMark> markers = new System.Collections.Concurrent.ConcurrentDictionary<int, BarMark>();
        private class BarMark { public int signal, plus, bigPlus, profit; public bool skullUp, skullDown, retestUp, retestDown; }
        private BarMark MarkFor(int bar) { return markers.GetOrAdd(bar, b => new BarMark()); }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "NinjaTrader 8 conversion of Nebula v2.2: cloud, candle coloring, reversal signals, Tidal Wave, HEMA and alerts.";
                Name = "NebulaNT8NoCloud";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;

                CloudType = "Simple";
                CandleColoring = "Waddah";
                Theme = "Standard";
                ShowHEMA = false;
                ShowPlus = true;
                ShowBigPlus = true;
                EnhanceStrongSignals = true;
                ShowProfit = true;
                Show921 = false;
                IgnoreDoji = false;
                ProfitThreshold = 5;
                MaxProfitThreshold = 7;
                MaxDojiTicks = 1;
                ShowVolumeImbalanceLines = false;
                ImbalanceLineBars = 50;
                ImbalanceLineWidth = 3;
                ShowReversalPattern = false;
                ShowRetests = false;
                ShowDashboard = false;
                DashboardPosition = "Top Right";
                UseQuadratic921 = false;
                SimpleCloudOpacity = 80;
                LowCloudOpacity = 80;
                HighCloudOpacity = 50;
                AdxLength = 14;
                DiLength = 14;
                FantailAdxLength = 2;
                FantailWeighting = 10.0;
                FantailMaLength = 6;
                WaeSensitivity = 150;
                WaeFastLength = 20;
                WaeSlowLength = 40;
                WaeChannelLength = 20;
                WaeMultiplier = 2.0;
                TrampolineBbThreshold = 0.0015;
                TrampolineRsiLower = 25;
                TrampolineRsiUpper = 72;
                SqueezeTolerance = 2;
                SqueezeAdxThreshold = 21;
                WatchSignalLookback = 35;
                AlphaLength = 20;
                GammaLength = 20;
                KernelLookback = 21;
                KernelRelativeWeight = 8;
                KernelStartBar = 15;
                EnableAlerts = true;

                AddPlot(Brushes.Transparent, "Signal");       // +1 basic buy, -1 basic sell, +2/-2 strong
                AddPlot(Brushes.Transparent, "AddSignal");    // +1/-1 small add, +2/-2 strong add
                AddPlot(Brushes.Transparent, "ProfitSignal"); // +1/-1 partial, +2/-2 full
            }
            else if (State == State.DataLoaded)
            {
                ema9 = EMA(9); ema21 = EMA(21); ema12 = EMA(12); ema26 = EMA(26);
                rsi14 = RSI(14, 1);
                rsiMain = RSI(32, 1);

                foreach (Brush br in new[] { bullBrush, bearBrush, partialProfitBrush, skullBrush, vectorMediumUpBrush, vectorMediumDownBrush })
                    if (br != null && !br.IsFrozen && br.CanFreeze) br.Freeze();
                markers.Clear();

                macdSeries = new Series<double>(this);
                macdSignalSeries = new Series<double>(this);
                varMaSeries = new Series<double>(this);
                fanSeries = new Series<double>(this);
                mgSeries = new Series<double>(this);
                hemaSeries = new Series<double>(this);
                bHSeries = new Series<double>(this);
                waeFastSeries = new Series<double>(this);
                waeSlowSeries = new Series<double>(this);
                squeezeValSeries = new Series<double>(this);
                lazySeries = new Series<double>(this);
                adxSeries = new Series<double>(this);
                pdiSmoothSeries = new Series<double>(this);
                mdiSmoothSeries = new Series<double>(this);
                trSmoothSeries = new Series<double>(this);
                brightGreenSeries = new Series<bool>(this);
                brightRedSeries = new Series<bool>(this);
                plotBuySeries = new Series<bool>(this);
                plotSellSeries = new Series<bool>(this);
                upVodkaSeries = new Series<bool>(this);
                downVodkaSeries = new Series<bool>(this);
                waveSeries = new Series<int>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            Values[0][0] = 0;
            Values[1][0] = 0;
            Values[2][0] = 0;
            if (CurrentBar < 210) return;

            SetTheme();
            double tick = TickSize <= 0 ? 1 : TickSize;
            double bodySize = Math.Abs(Close[0] - Open[0]);
            bool green = Close[0] > Open[0];
            bool red = Close[0] < Open[0];
            bool doji = IgnoreDoji && bodySize <= MaxDojiTicks * tick;

            // ADX / DI (Wilder smoothing)
            double upMove = High[0] - High[1];
            double downMove = Low[1] - Low[0];
            double plusDm = upMove > downMove && upMove > 0 ? upMove : 0;
            double minusDm = downMove > upMove && downMove > 0 ? downMove : 0;
            double tr = Math.Max(High[0] - Low[0], Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));
            double k = 1.0 / Math.Max(1, DiLength);
            pdiSmoothSeries[0] = CurrentBar == 210 ? plusDm : pdiSmoothSeries[1] + k * (plusDm - pdiSmoothSeries[1]);
            mdiSmoothSeries[0] = CurrentBar == 210 ? minusDm : mdiSmoothSeries[1] + k * (minusDm - mdiSmoothSeries[1]);
            trSmoothSeries[0] = CurrentBar == 210 ? tr : trSmoothSeries[1] + k * (tr - trSmoothSeries[1]);
            double pdi = trSmoothSeries[0] == 0 ? 0 : 100 * pdiSmoothSeries[0] / trSmoothSeries[0];
            double mdi = trSmoothSeries[0] == 0 ? 0 : 100 * mdiSmoothSeries[0] / trSmoothSeries[0];
            double dx = (pdi + mdi) == 0 ? 0 : 100 * Math.Abs(pdi - mdi) / (pdi + mdi);
            double ak = 1.0 / Math.Max(1, AdxLength);
            adxSeries[0] = CurrentBar == 210 ? dx : adxSeries[1] + ak * (dx - adxSeries[1]);

            // MACD
            macdSeries[0] = ema12[0] - ema26[0];
            macdSignalSeries[0] = CurrentBar == 210 ? macdSeries[0] : macdSignalSeries[1] + 2.0 / 10.0 * (macdSeries[0] - macdSignalSeries[1]);
            double hist = macdSeries[0] - macdSignalSeries[0];

            // Fantail VMA + McGinley cloud
            double fw = Math.Max(1.0, FantailWeighting);
            double bulls1 = 0.5 * (Math.Abs(High[0] - High[1]) + (High[0] - High[1]));
            double bears1 = 0.5 * (Math.Abs(Low[1] - Low[0]) + (Low[1] - Low[0]));
            double bulls = bulls1 < bears1 ? 0 : (Math.Abs(bulls1 - bears1) < 1e-12 ? 0 : bulls1);
            double bears = bulls1 > bears1 ? 0 : (Math.Abs(bulls1 - bears1) < 1e-12 ? 0 : bears1);
            double oldP = CurrentBar == 210 ? 0 : pdiSmoothSeries[1];
            double oldM = CurrentBar == 210 ? 0 : mdiSmoothSeries[1];
            double oldTr = CurrentBar == 210 ? High[0] - Low[0] : trSmoothSeries[1];
            double fp = (fw * oldP + bulls) / (fw + 1);
            double fm = (fw * oldM + bears) / (fw + 1);
            double ftr = (fw * oldTr + tr) / (fw + 1);
            double fPdi = ftr > 0 ? fp / ftr : 0;
            double fMdi = ftr > 0 ? fm / ftr : 0;
            double fdx = fPdi + fMdi > 0 ? Math.Abs(fPdi - fMdi) / (fPdi + fMdi) : 0;
            double fAdx = CurrentBar == 210 ? fdx : (fw * adxSeries[1] + fdx) / (fw + 1);
            double fMin = fAdx, fMax = fAdx;
            for (int i = 1; i < Math.Min(FantailAdxLength, CurrentBar); i++) { fMin = Math.Min(fMin, adxSeries[i]); fMax = Math.Max(fMax, adxSeries[i]); }
            double c = fMax > fMin ? (fAdx - fMin) / (fMax - fMin) : 0;
            varMaSeries[0] = CurrentBar == 210 ? Close[0] : ((2 - c) * varMaSeries[1] + c * Close[0]) / 2.0;
            fanSeries[0] = SimpleAverage(varMaSeries, FantailMaLength);
            mgSeries[0] = CurrentBar == 210 || mgSeries[1] == 0 ? EMAValue(Close, 14, 0) : mgSeries[1] + (Close[0] - mgSeries[1]) / (14.0 * Math.Pow(Math.Max(0.000001, Close[0] / mgSeries[1]), 4));

            // HEMA
            double alpha = 2.0 / (AlphaLength + 1.0), gamma = 2.0 / (GammaLength + 1.0);
            hemaSeries[0] = CurrentBar == 210 ? Close[0] : (1 - alpha) * (hemaSeries[1] + bHSeries[1]) + alpha * Close[0];
            bHSeries[0] = CurrentBar == 210 ? 0 : (1 - gamma) * bHSeries[1] + gamma * (hemaSeries[0] - hemaSeries[1]);

            // Waddah Attar Explosion (recursive EMAs; carried in series instead of recomputed each bar)
            double afFast = 2.0 / (WaeFastLength + 1.0), afSlow = 2.0 / (WaeSlowLength + 1.0);
            waeFastSeries[0] = CurrentBar == 210 ? Close[0] : waeFastSeries[1] + afFast * (Close[0] - waeFastSeries[1]);
            waeSlowSeries[0] = CurrentBar == 210 ? Close[0] : waeSlowSeries[1] + afSlow * (Close[0] - waeSlowSeries[1]);
            double waeFast = waeFastSeries[0], waeSlow = waeSlowSeries[0];
            double prevMacdWae = waeFastSeries[1] - waeSlowSeries[1];
            double t1 = ((waeFast - waeSlow) - prevMacdWae) * WaeSensitivity;
            double basisWae = SimpleAverage(Close, WaeChannelLength);
            double devWae = WaeMultiplier * Std(Close, WaeChannelLength);
            double explosion = 2 * devWae;
            double trendUpWae = t1 >= 0 ? t1 : 0;
            double trendDownWae = t1 < 0 ? -t1 : 0;

            // Squeeze momentum approximation of Pine linreg(close - avg2, 20, 0)
            int sqLen = 20;
            double bbBasis = SimpleAverage(Close, sqLen);
            double bbDev = 1.5 * Std(Close, sqLen);
            double upperBB = bbBasis + bbDev, lowerBB = bbBasis - bbDev;
            double rangeMa = AverageRange(sqLen);
            double upperKC = bbBasis + rangeMa * 1.5, lowerKC = bbBasis - rangeMa * 1.5;
            bool sqzOn = lowerBB > lowerKC && upperBB < upperKC;
            double hh = Highest(High, sqLen), ll = Lowest(Low, sqLen);
            double avg2 = ((hh + ll) / 2.0 + bbBasis) / 2.0;
            squeezeValSeries[0] = LinRegValue(Close, avg2, sqLen);
            double sqVal = squeezeValSeries[0];
            if (sqVal < squeezeValSeries[1] && sqVal < 5 && !sqzOn) squeezeRedCount++;
            if (sqVal > squeezeValSeries[1] && sqVal > 5 && !sqzOn) squeezeGreenCount++;
            bool squeezePos = sqVal > squeezeValSeries[1] && squeezeRedCount > SqueezeTolerance && sqVal < 5 && !squeezePosPrev && adxSeries[0] > SqueezeAdxThreshold;
            bool squeezeNeg = sqVal < squeezeValSeries[1] && squeezeGreenCount > SqueezeTolerance && sqVal > 5 && !squeezeNegPrev && adxSeries[0] > SqueezeAdxThreshold;
            if (squeezePos) squeezeRedCount = 0;
            if (squeezeNeg) squeezeGreenCount = 0;
            squeezePosPrev = squeezePos; squeezeNegPrev = squeezeNeg;

            // Approximate CVD from candle anatomy, matching supplied Pine formula
            double spread = Math.Max(tick, High[0] - Low[0]);
            double upperWick = Close[0] > Open[0] ? High[0] - Close[0] : High[0] - Open[0];
            double lowerWick = Close[0] > Open[0] ? Open[0] - Low[0] : Close[0] - Low[0];
            double body = Math.Max(0, spread - upperWick - lowerWick);
            double wickPart = ((upperWick + lowerWick) / spread) / 2.0;
            double bodyPart = body / spread;
            double buyingVol = Close[0] > Open[0] ? (bodyPart + wickPart) * Volume[0] : wickPart * Volume[0];
            double sellingVol = Close[0] < Open[0] ? (bodyPart + wickPart) * Volume[0] : wickPart * Volume[0];
            double buyEma = EMARecursive(buyingVol, "buy", 14);
            double sellEma = EMARecursive(sellingVol, "sell", 14);
            double cvd = buyEma - sellEma;

            // Reversal families and take-profit score
            int tpCount = 0;
            bool buyDsr = Close[1] < Open[1] && green && Close[0] > Open[1] && Lowest(Low, 3) < LowestAgo(Low, 50, 1);
            bool sellDsr = Close[1] > Open[1] && red && Close[0] < Open[1] && Highest(High, 3) > HighestAgo(High, 50, 1);
            if (buyDsr || sellDsr || (Close[2] != 0 && (DsrAgo(true,1) || DsrAgo(false,1)))) tpCount += VDeadRev;

            bool luxUp = NineCountReversal(true);
            bool luxDown = NineCountReversal(false);
            if (luxUp || luxDown) tpCount += VLuxRev;

            bool trampUp = Trampoline(true);
            bool trampDown = Trampoline(false);
            if (trampUp || trampDown) tpCount += VTramp;
            if (squeezePos || squeezeNeg) tpCount += VSqueeze;

            double rsi = rsi14[0];
            double rsiBasis30 = SimpleAverageRSI(30);
            double rsiStd30 = StdRSI(30);
            bool sharkUp = rsi < rsiBasis30 - 2 * rsiStd30 && rsi < 26;
            bool sharkDown = rsi > rsiBasis30 + 2 * rsiStd30 && rsi > 74;
            if (sharkUp || sharkDown) tpCount += VShark;

            double wickBasis = SimpleAverage(Close, 20), wickDev = 2.5 * Std(Close, 20);
            bool wickUp = Low[0] <= wickBasis - wickDev && Close[0] >= wickBasis - wickDev && red;
            bool wickDown = High[0] >= wickBasis + wickDev && Close[0] < wickBasis + wickDev && green;
            if (wickUp || wickDown) tpCount += VBands;

            // Ultimate Buy/Sell watch + trigger logic
            BuildUltimateBuySell(out bool plotBuy, out bool plotSell);
            plotBuySeries[0] = plotBuy;
            plotSellSeries[0] = plotSell;

            // Internal replacement for TradersReality vector candle classification
            double avgVol10 = SimpleAverage(Volume, 10);
            double spreadVol = spread * Volume[0];
            double highestSpreadVol = 0;
            for (int i = 1; i <= 10; i++) highestSpreadVol = Math.Max(highestSpreadVol, (High[i] - Low[i]) * Volume[i]);
            bool vectorStrong = Volume[0] >= 2.0 * avgVol10 || spreadVol >= highestSpreadVol;
            bool vectorMedium = Volume[0] >= 1.5 * avgVol10;
            bool vectorGreen = green && vectorStrong;
            bool vectorRed = red && vectorStrong;
            if ((vectorGreen || vectorRed) && (plotBuy || plotSell)) tpCount += VEarlyRev;

            // Vodka Shot approximation
            lazySeries[0] = LazyLine(21);
            double volAvgS = Math.Max(1, SimpleAverage(Volume, 14));
            double volAvgL = Math.Max(1, SimpleAverage(Volume, 70));
            double volDev = (volAvgL + 1.618034 * Std(Volume, 70)) / volAvgL * 11.0 / 100.0;
            double volRel = Volume[0] / volAvgL;
            bool lazyUp = lazySeries[0] > lazySeries[1];
            bool energyUp = macdSeries[0] >= macdSignalSeries[0];
            bool energyDown = macdSeries[0] < macdSignalSeries[0];
            bool vodkaUp = lazyUp && energyUp && green && volRel * .145898 > volDev;
            bool vodkaDown = !lazyUp && energyDown && red && volRel * .145898 > volDev;
            bool pBuyVodka = vodkaUp && !upVodkaSeries[1] && !upVodkaSeries[2] && !upVodkaSeries[3] && !upVodkaSeries[4];
            bool pSellVodka = vodkaDown && !downVodkaSeries[1] && !downVodkaSeries[2] && !downVodkaSeries[3] && !downVodkaSeries[4];
            upVodkaSeries[0] = vodkaUp; downVodkaSeries[0] = vodkaDown;

            // Tidal Wave state / gaps / reversals
            int prevWave = waveSeries[1] == 0 ? DownTrend : waveSeries[1];
            int wave = prevWave;
            bool noOverlapGreen = false, noOverlapRed = false, brightGreen = false, brightRed = false, gapGreen = false, gapRed = false;
            if (green && !doji)
            {
                for (int i = 1; i <= 200; i++)
                {
                    if (brightRedSeries[i]) break;
                    if (wave == UpTrend && Close[i] < Open[i]) break;
                    if (wave == DownTrend && Open[0] >= Close[i] && Close[i] > Open[i]) { noOverlapGreen = true; brightGreen = true; wave = UpTrend; break; }
                }
                if (Open[0] >= Close[1] && Close[1] > Open[1]) { wave = UpTrend; brightGreen = true; gapGreen = true; DrawImbalance(true); }
            }
            if (red && !doji)
            {
                for (int i = 1; i <= 200; i++)
                {
                    if (brightGreenSeries[i]) break;
                    if (wave == DownTrend && Close[i] > Open[i]) break;
                    if (wave == UpTrend && Open[0] <= Close[i] && Close[i] < Open[i]) { noOverlapRed = true; brightRed = true; wave = DownTrend; break; }
                }
                if (Close[1] < Open[1] && Open[0] < Close[1]) { wave = DownTrend; brightRed = true; gapRed = true; DrawImbalance(false); }
            }
            brightGreenSeries[0] = brightGreen; brightRedSeries[0] = brightRed; waveSeries[0] = wave;

            bool bigBuy = plotBuy || plotBuySeries[1] || plotBuySeries[2] || plotBuySeries[3];
            bool bigSell = plotSell || plotSellSeries[1] || plotSellSeries[2] || plotSellSeries[3];
            bool buyChar = noOverlapGreen && prevWave == DownTrend;
            bool sellChar = noOverlapRed && prevWave == UpTrend;
            bool strongBuy = bigBuy && buyChar;
            bool strongSell = bigSell && sellChar;
            bool basicBuy = !bigBuy && buyChar;
            bool basicSell = !bigSell && sellChar;

            if (basicBuy || strongBuy) { Values[0][0] = strongBuy ? 2 : 1; MarkFor(CurrentBar).signal = strongBuy ? 2 : 1; FireAlert(strongBuy ? "Buy Signal Super" : "Buy Signal Basic", true); }
            if (basicSell || strongSell) { Values[0][0] = strongSell ? -2 : -1; MarkFor(CurrentBar).signal = strongSell ? -2 : -1; FireAlert(strongSell ? "Sell Signal Super" : "Sell Signal Basic", false); }

            if (ShowPlus && gapGreen && prevWave == UpTrend) { Values[1][0] = 1; MarkFor(CurrentBar).plus = 1; }
            if (ShowPlus && gapRed && prevWave == DownTrend) { Values[1][0] = -1; MarkFor(CurrentBar).plus = -1; }
            if (ShowBigPlus && pBuyVodka && prevWave == UpTrend) { Values[1][0] = 2; MarkFor(CurrentBar).bigPlus = 1; }
            if (ShowBigPlus && pSellVodka && prevWave == DownTrend) { Values[1][0] = -2; MarkFor(CurrentBar).bigPlus = -1; }

            if (ShowProfit && tpCount >= MaxProfitThreshold) { Values[2][0] = wave == UpTrend ? 1 : -1; MarkFor(CurrentBar).profit = wave == UpTrend ? 1 : -1; }
            else if (ShowProfit && tpCount >= ProfitThreshold) { Values[2][0] = wave == UpTrend ? 2 : -2; MarkFor(CurrentBar).profit = wave == UpTrend ? 2 : -2; }

            // EMA / rational quadratic cross
            double kernel = (Show921 && UseQuadratic921) ? RationalQuadratic() : ema21[0];
            bool skullUp = Show921 && CrossAbove(ema9, UseQuadratic921 ? kernel : ema21[0], 1);
            bool skullDown = Show921 && CrossBelow(ema9, UseQuadratic921 ? kernel : ema21[0], 1);
            if (skullUp) MarkFor(CurrentBar).skullUp = true;
            if (skullDown) MarkFor(CurrentBar).skullDown = true;

            // Cloud drawing removed by request.
            // Underlying FanVMA / McGinley calculations remain available for signal and retest logic.
            if (ShowHEMA)
                Draw.Line(this, "HEMA"+CurrentBar, 1, hemaSeries[1], 0, hemaSeries[0], hemaSeries[0] >= hemaSeries[1] ? bigGreen : bigRed);

            // Retests / reversal candle outline approximation
            HandlePivotsAndRetests();
            bool threeOutUp = Close[2] < Open[2] && Close[1] > Open[1] && green && Open[1] < Close[2] && Open[2] < Close[1] && Math.Abs(Close[1]-Open[1]) > Math.Abs(Close[2]-Open[2]);
            bool threeOutDown = Close[2] > Open[2] && Close[1] < Open[1] && red && Open[1] > Close[2] && Open[2] > Close[1] && Math.Abs(Close[1]-Open[1]) > Math.Abs(Close[2]-Open[2]);
            if (ShowReversalPattern && (threeOutUp || threeOutDown)) CandleOutlineBrushes[0] = skullBrush;

            ApplyCandleColor(CandleColoring, trendUpWae, trendDownWae, explosion, sqVal, cvd, vectorStrong, vectorMedium, green);
        }

        private double buyEmaState, sellEmaState;
        private double EMARecursive(double value, string side, int len)
        {
            double a = 2.0/(len+1.0);
            if (side == "buy") { buyEmaState = buyEmaState == 0 ? value : buyEmaState + a*(value-buyEmaState); return buyEmaState; }
            sellEmaState = sellEmaState == 0 ? value : sellEmaState + a*(value-sellEmaState); return sellEmaState;
        }

        private void BuildUltimateBuySell(out bool plotBuy, out bool plotSell)
        {
            plotBuy = false; plotSell = false;
            double r = rsiMain[0];
            double rb = AverageIndicator(rsiMain, 32);
            double rs = StdIndicator(rsiMain, 32);
            double rUpper = rb + 2*rs, rLower = rb - 2*rs;
            double rma = WmaIndicator(rsiMain, 24);
            double pb = SimpleAverage(Close, 20), ps = Std(Close,20);
            double pUpper = pb + 2*ps, pLower = pb - 2*ps;
            double atr = ATR(30)[0];
            double amid = WMA(Close,10)[0];
            double aUpper = amid + atr*1.5, aLower = amid - atr*1.5;

            bool priceOverInner = CrossAboveValue(Close[0], Close[1], pLower, SimpleAverageAgo(Close,20,1)-2*StdAgo(Close,20,1));
            bool priceUnderInner = CrossBelowValue(Close[0], Close[1], pUpper, SimpleAverageAgo(Close,20,1)+2*StdAgo(Close,20,1));
            bool rsiOverLower = r > rLower && rsiMain[1] <= (AverageIndicatorAgo(rsiMain,32,1)-2*StdIndicatorAgo(rsiMain,32,1));
            bool rsiUnderUpper = r < rUpper && rsiMain[1] >= (AverageIndicatorAgo(rsiMain,32,1)+2*StdIndicatorAgo(rsiMain,32,1));
            bool rsiOver25 = r > 25 && rsiMain[1] <= 25;
            bool rsiUnder75 = r < 75 && rsiMain[1] >= 75;
            bool atrWatchBuy = High[0] < aLower && High[1] >= (WMA(Close,10)[1]-ATR(30)[1]*1.5);
            bool atrWatchSell = Low[0] > aUpper && Low[1] <= (WMA(Close,10)[1]+ATR(30)[1]*1.5);
            bool buyWatchedNow = priceOverInner || rsiOverLower || rsiOver25 || atrWatchBuy;
            bool sellWatchedNow = priceUnderInner || rsiUnderUpper || rsiUnder75 || atrWatchSell;
            buyWatch.Enqueue(buyWatchedNow ? 1 : 0); sellWatch.Enqueue(sellWatchedNow ? 1 : 0);
            while (buyWatch.Count > WatchSignalLookback) buyWatch.Dequeue();
            while (sellWatch.Count > WatchSignalLookback) sellWatch.Dequeue();
            bool buyWatchMet = buyWatch.Sum() >= 1, sellWatchMet = sellWatch.Sum() >= 1;
            bool buySignal = (r > rb && rsiMain[1] <= AverageIndicatorAgo(rsiMain,32,1)) || rsiOver25 || (r > rma && rsiMain[1] <= WmaIndicatorAgo(rsiMain,24,1));
            bool sellSignal = (r < rb && rsiMain[1] >= AverageIndicatorAgo(rsiMain,32,1)) || rsiUnder75 || (r < rma && rsiMain[1] >= WmaIndicatorAgo(rsiMain,24,1));
            if (buyWatchMet && buySignal && !buyWatchedNow) { plotBuy = true; buyWatch.Clear(); sellWatch.Clear(); }
            else if (sellWatchMet && sellSignal && !sellWatchedNow) { plotSell = true; buyWatch.Clear(); sellWatch.Clear(); }
        }

        private bool NineCountReversal(bool bullish)
        {
            bool all = true;
            for (int i=0;i<9;i++)
            {
                if (bullish && !(Close[i] < Close[i+4])) all=false;
                if (!bullish && !(Close[i] >= Close[i+4])) all=false;
            }
            return all;
        }

        private bool Trampoline(bool bullish)
        {
            double basis = SimpleAverage(Close,20), dev = 2*Std(Close,20);
            double upper = basis+dev, lower=basis-dev;
            double bbw = basis == 0 ? 0 : (upper-lower)/basis;
            for(int i=1;i<=5;i++)
            {
                double b = SimpleAverageAgo(Close,20,i), d=2*StdAgo(Close,20,i);
                double ri = rsi14[i];
                if (bullish && Close[i]<Open[i] && ri<=TrampolineRsiLower && Close[i] < b-d && bbw>TrampolineBbThreshold && Close[0]>Open[0] && High[0]>High[1]) return true;
                if (!bullish && Close[i]>Open[i] && ri>=TrampolineRsiUpper && Close[i] > b+d && bbw>TrampolineBbThreshold && Close[0]<Open[0] && Low[0]<Low[1]) return true;
            }
            return false;
        }

        private bool DsrAgo(bool bullish, int ago)
        {
            if (bullish) return Close[ago+1] < Open[ago+1] && Close[ago] > Open[ago] && Close[ago] > Open[ago+1];
            return Close[ago+1] > Open[ago+1] && Close[ago] < Open[ago] && Close[ago] < Open[ago+1];
        }

        private void ApplyCandleColor(string mode, double up, double down, double explosion, double sq, double cvd, bool vectorStrong, bool vectorMedium, bool green)
        {
            if (mode == "None") return;
            Brush b = null;
            if (mode == "Waddah")
            {
                if (up > 0) b = up > explosion ? bigGreen : MakeTransparent(bigGreen, 70);
                else if (down > 0) b = down > explosion ? bigRed : MakeTransparent(bigRed, 70);
            }
            else if (mode == "Squeeze") b = sq >= 0 ? (sq >= squeezeValSeries[1] ? bigGreen : MakeTransparent(bigGreen,65)) : (sq <= squeezeValSeries[1] ? bigRed : MakeTransparent(bigRed,65));
            else if (mode == "Volume Delta") b = cvd >= 0 ? bigGreen : bigRed;
            else if (mode == "Vector")
            {
                if (vectorStrong) b = green ? bigGreen : bigRed;
                else if (vectorMedium) b = green ? vectorMediumUpBrush : vectorMediumDownBrush;
                else b = green ? MakeTransparent(bigGreen,75) : MakeTransparent(bigRed,75);
            }
            if (b != null) { BarBrushes[0] = b; CandleOutlineBrushes[0] = b; }
        }

        private void HandlePivotsAndRetests()
        {
            const int p=20;
            bool pivotLow=true,pivotHigh=true;
            for(int i=1;i<=p;i++) { if(Low[p] >= Low[p-i] || Low[p] > Low[p+i]) pivotLow=false; if(High[p] <= High[p-i] || High[p] < High[p+i]) pivotHigh=false; }
            if(pivotLow && (double.IsNaN(priorPl) || Math.Abs(Low[p]-priorPl)>TickSize/2)) { priorPl=Low[p]; supportBottom=Low[p]; supportTop=Math.Max(Low[p-1],Low[p+1]); }
            if(pivotHigh && (double.IsNaN(priorPh) || Math.Abs(High[p]-priorPh)>TickSize/2)) { priorPh=High[p]; resistanceTop=High[p]; resistanceBottom=Math.Min(High[p-1],High[p+1]); }
            if(!double.IsNaN(supportBottom) && Close[0] < supportBottom && supportBreakBar<0) supportBreakBar=CurrentBar;
            if(!double.IsNaN(resistanceTop) && Close[0] > resistanceTop && resistanceBreakBar<0) resistanceBreakBar=CurrentBar;
            bool inCloud = fanSeries[0]>mgSeries[0] ? (Low[0]<=fanSeries[0] && High[0]>=mgSeries[0]) : (Low[0]<=mgSeries[0] && High[0]>=fanSeries[0]);
            if(ShowRetests && inCloud)
            {
                if(supportBreakBar>0 && CurrentBar-supportBreakBar<=2 && High[0]>=supportBottom && Close[0]<=supportTop) MarkFor(CurrentBar).retestUp = true;
                if(resistanceBreakBar>0 && CurrentBar-resistanceBreakBar<=2 && Low[0]<=resistanceTop && Close[0]>=resistanceBottom) MarkFor(CurrentBar).retestDown = true;
            }
            if(supportBreakBar>0 && CurrentBar-supportBreakBar>2) supportBreakBar=-1;
            if(resistanceBreakBar>0 && CurrentBar-resistanceBreakBar>2) resistanceBreakBar=-1;
        }

        private void DrawImbalance(bool bullish)
        {
            if(!ShowVolumeImbalanceLines) return;
            Brush b = bullish ? MakeTransparent(bigGreen,50) : MakeTransparent(bigRed,50);
            Draw.Line(this, "IMB"+CurrentBar, 0, Open[0], -ImbalanceLineBars, Open[0], b);
        }

        private void FireAlert(string name, bool bullish)
        {
            if(!EnableAlerts || lastAlertBar==CurrentBar) return;
            lastAlertBar=CurrentBar;
            Alert("Nebula"+name+CurrentBar, Priority.Medium, name, NinjaTrader.Core.Globals.InstallDir+@"\sounds\Alert2.wav", 0, bullish?bigGreen:bigRed, Brushes.Black);
        }

        private void SetTheme()
        {
            if(Theme=="Pinky and the Brain") { bigGreen=Brushes.Cyan; bigRed=Brushes.Magenta; }
            else if(Theme=="Color Blind") { bigGreen=Brushes.Cyan; bigRed=Brushes.Orange; }
            else if(Theme=="Mellow Yellow") { bigGreen=Brushes.DeepSkyBlue; bigRed=Brushes.Yellow; }
            else { bigGreen=bullBrush; bigRed=bearBrush; }   // Standard / Custom: use the Colors group
        }

        private Brush MakeTransparent(Brush brush, byte percent)
        {
            SolidColorBrush s=brush as SolidColorBrush; if(s==null) return brush;
            Color c=s.Color; return new SolidColorBrush(Color.FromArgb((byte)(255*(100-percent)/100),c.R,c.G,c.B));
        }

        private double RationalQuadratic()
        {
            int count=Math.Min(CurrentBar, KernelStartBar+60); double cw=0, ww=0;
            for(int i=0;i<=count;i++) { double w=Math.Pow(1+(i*i)/(KernelLookback*KernelLookback*2*Math.Max(.25,KernelRelativeWeight)),-KernelRelativeWeight); cw+=Close[i]*w; ww+=w; }
            return ww==0?Close[0]:cw/ww;
        }

        private double LazyLine(int len)
        {
            int w2=Math.Max(1,(int)Math.Round(len/3.0)); int w1=Math.Max(1,(int)Math.Round((len-w2)/2.0)); int w3=Math.Max(1,(len-w2)/2);
            // Three-stage WMA approximated directly from Close history.
            double[] stage1=new double[w2+w3+2];
            for(int j=0;j<stage1.Length;j++) stage1[j]=WmaAgo(Close,w1,j);
            double[] stage2=new double[w3+1];
            for(int j=0;j<stage2.Length;j++) stage2[j]=WmaArray(stage1,j,w2);
            return WmaArray(stage2,0,w3);
        }

        private double EMAValue(ISeries<double> s,int len,int ago)
        {
            double a=2.0/(len+1.0), v=s[Math.Min(CurrentBar,ago+len*4)];
            for(int i=Math.Min(CurrentBar,ago+len*4)-1;i>=ago;i--) v=v+a*(s[i]-v);
            return v;
        }

        // ---- Signal legend dashboard (overlay, drawn in OnRender = one GPU pass, no drawing-object buildup) ----
        private SharpDX.Direct2D1.SolidColorBrush DxSolid(Brush wpf)
        {
            SolidColorBrush scb = wpf as SolidColorBrush;
            Color c = scb != null ? scb.Color : Colors.White;
            return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(c.R/255f, c.G/255f, c.B/255f, c.A/255f));
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (RenderTarget == null) return;
            RenderSignals(chartControl, chartScale);
            if (ShowDashboard) RenderDashboard(chartControl, chartScale);
        }

        // Paint signal glyphs for on-screen bars only. No persistent drawing objects; cost scales with
        // the number of visible bars, not with total chart history.
        private void RenderSignals(ChartControl chartControl, ChartScale chartScale)
        {
            if (markers.IsEmpty || Bars == null || ChartBars == null) return;
            int from = ChartBars.FromIndex, to = ChartBars.ToIndex;
            if (from < 0 || to < 0) return;
            int lastBar = Bars.Count - 1;
            if (to > lastBar) to = lastBar;
            if (from > to) return;
            double tick = TickSize;

            SharpDX.Direct2D1.SolidColorBrush bull  = DxSolid(bullBrush);
            SharpDX.Direct2D1.SolidColorBrush bear  = DxSolid(bearBrush);
            SharpDX.Direct2D1.SolidColorBrush part  = DxSolid(partialProfitBrush);
            SharpDX.Direct2D1.SolidColorBrush skull = DxSolid(skullBrush);
            SharpDX.DirectWrite.TextFormat gf = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", 13f);
            gf.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
            gf.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
            gf.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

            for (int idx = from; idx <= to; idx++)
            {
                BarMark m;
                if (!markers.TryGetValue(idx, out m)) continue;
                float x = chartControl.GetXByBarIndex(ChartBars, idx);
                double hi = Bars.GetHigh(idx), lo = Bars.GetLow(idx);

                if (m.signal != 0)
                {
                    bool up = m.signal > 0;
                    string g = Math.Abs(m.signal) == 2 ? "\u278A" : "\u2460";
                    DrawGlyph(g, x, chartScale.GetYByValue(up ? lo - 3 * tick : hi + 3 * tick), up ? bull : bear, gf);
                }
                if (m.plus != 0)
                {
                    bool up = m.plus > 0;
                    DrawGlyph("+", x, chartScale.GetYByValue(up ? lo - 2 * tick : hi + 2 * tick), up ? bull : bear, gf);
                }
                if (m.bigPlus != 0)
                {
                    bool up = m.bigPlus > 0;
                    DrawGlyph("\u271A", x, chartScale.GetYByValue(up ? lo - 4 * tick : hi + 4 * tick), up ? bull : bear, gf);
                }
                if (m.profit != 0)
                {
                    bool up = m.profit > 0, full = Math.Abs(m.profit) == 1;
                    DrawGlyph(full ? "\u2714" : "\u2713", x, chartScale.GetYByValue(up ? hi + 5 * tick : lo - 5 * tick), full ? (up ? bull : bear) : part, gf);
                }
                if (m.skullUp)   DrawGlyph("\u25B2", x, chartScale.GetYByValue(lo - 2 * tick), skull, gf);
                if (m.skullDown) DrawGlyph("\u25BC", x, chartScale.GetYByValue(hi + 2 * tick), skull, gf);
                if (m.retestUp)   DrawGlyph("!", x, chartScale.GetYByValue(hi + 2 * tick), part, gf);
                if (m.retestDown) DrawGlyph("!", x, chartScale.GetYByValue(lo - 2 * tick), part, gf);
            }

            bull.Dispose(); bear.Dispose(); part.Dispose(); skull.Dispose(); gf.Dispose();
        }

        private void DrawGlyph(string g, float cx, float cy, SharpDX.Direct2D1.Brush b, SharpDX.DirectWrite.TextFormat f)
        {
            RenderTarget.DrawText(g, f, new SharpDX.RectangleF(cx - 12f, cy - 10f, 24f, 20f), b);
        }

        private void RenderDashboard(ChartControl chartControl, ChartScale chartScale)
        {
            if (ChartPanel == null) return;

            string[] glyphs = { "\u278A", "\u2460", "\u278A", "\u2460", "+", "\u271A", "\u2714", "\u2713", "\u25B2\u25BC", "\u25A2", "!", "\u25A0" };
            string[] descs  =
            {
                "Strong Buy \u2013 trend flip confirmed by Ultimate Buy",
                "Basic Buy \u2013 trend flip, not yet confirmed",
                "Strong Sell \u2013 trend flip confirmed by Ultimate Sell",
                "Basic Sell \u2013 trend flip, not yet confirmed",
                "Plus \u2013 gap continuation in trend  (Show +)",
                "Big Plus \u2013 'Vodka' volume/energy surge  (Show \u271A)",
                "Full Profit \u2013 strong exit-target confluence  (Show Profit)",
                "Partial Profit \u2013 early exit-target confluence",
                "9/21 Cross \u2013 EMA 9/21 skull reversal  (Show 9/21)",
                "Reversal Bar \u2013 3-bar reversal outline  (Show Reversal)",
                "Retest \u2013 break & retest of broken S/R  (Show Retests)",
                "Candle Color \u2013 momentum via Candle Coloring mode"
            };

            SharpDX.Direct2D1.SolidColorBrush bg     = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.07f, 0.07f, 0.09f, 0.88f));
            SharpDX.Direct2D1.SolidColorBrush border = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.40f, 0.40f, 0.46f, 1f));
            SharpDX.Direct2D1.SolidColorBrush white  = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.90f, 0.90f, 0.93f, 1f));
            SharpDX.Direct2D1.SolidColorBrush gray   = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.68f, 0.70f, 0.74f, 1f));
            SharpDX.Direct2D1.SolidColorBrush bull   = DxSolid(bullBrush);
            SharpDX.Direct2D1.SolidColorBrush bear   = DxSolid(bearBrush);
            SharpDX.Direct2D1.SolidColorBrush part   = DxSolid(partialProfitBrush);
            SharpDX.Direct2D1.SolidColorBrush skull  = DxSolid(skullBrush);
            SharpDX.Direct2D1.SolidColorBrush vmed   = DxSolid(vectorMediumUpBrush);
            SharpDX.Direct2D1.Brush[] rowBrush = { bull, bull, bear, bear, bull, bull, bull, part, skull, skull, part, vmed };

            SharpDX.DirectWrite.TextFormat titleFmt = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 14f);
            SharpDX.DirectWrite.TextFormat glyphFmt = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", 14f);
            SharpDX.DirectWrite.TextFormat bodyFmt  = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", 12f);
            titleFmt.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
            glyphFmt.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
            bodyFmt.WordWrapping  = SharpDX.DirectWrite.WordWrapping.NoWrap;

            float pad = 10f, lineH = 21f, glyphW = 34f, headerH = 46f, panelW = 440f;
            float panelH = headerH + glyphs.Length * lineH + pad;
            float x = DashboardPosition == "Top Left" ? ChartPanel.X + pad : ChartPanel.X + ChartPanel.W - panelW - pad;
            float y = ChartPanel.Y + pad;

            SharpDX.RectangleF panel = new SharpDX.RectangleF(x, y, panelW, panelH);
            RenderTarget.FillRectangle(panel, bg);
            RenderTarget.DrawRectangle(panel, border, 1f);

            RenderTarget.DrawText("Nebula \u2014 Signal Legend", titleFmt, new SharpDX.RectangleF(x + pad, y + 6, panelW - 2 * pad, 20f), white);
            RenderTarget.DrawText("Buy prints below the bar \u00B7 Sell mirrors above", bodyFmt, new SharpDX.RectangleF(x + pad, y + 24, panelW - 2 * pad, 16f), gray);

            for (int i = 0; i < glyphs.Length; i++)
            {
                float ry = y + headerH + i * lineH;
                RenderTarget.DrawText(glyphs[i], glyphFmt, new SharpDX.RectangleF(x + pad, ry, glyphW, lineH), rowBrush[i]);
                RenderTarget.DrawText(descs[i], bodyFmt, new SharpDX.RectangleF(x + pad + glyphW, ry, panelW - glyphW - 2 * pad, lineH), white);
            }

            bg.Dispose(); border.Dispose(); white.Dispose(); gray.Dispose();
            bull.Dispose(); bear.Dispose(); part.Dispose(); skull.Dispose(); vmed.Dispose();
            titleFmt.Dispose(); glyphFmt.Dispose(); bodyFmt.Dispose();
        }

        private double SimpleAverage(ISeries<double> s,int len){return SimpleAverageAgo(s,len,0);}        
        private double SimpleAverageAgo(ISeries<double> s,int len,int ago){double z=0;int n=Math.Min(len,CurrentBar-ago+1);for(int i=ago;i<ago+n;i++)z+=s[i];return n==0?0:z/n;}
        private double Std(ISeries<double> s,int len){return StdAgo(s,len,0);}        
        private double StdAgo(ISeries<double> s,int len,int ago){double m=SimpleAverageAgo(s,len,ago),q=0;int n=Math.Min(len,CurrentBar-ago+1);for(int i=ago;i<ago+n;i++){double d=s[i]-m;q+=d*d;}return n<=1?0:Math.Sqrt(q/n);}
        private double Highest(ISeries<double>s,int len){double x=double.MinValue;for(int i=0;i<len;i++)x=Math.Max(x,s[i]);return x;}
        private double Lowest(ISeries<double>s,int len){double x=double.MaxValue;for(int i=0;i<len;i++)x=Math.Min(x,s[i]);return x;}
        private double HighestAgo(ISeries<double>s,int len,int ago){double x=double.MinValue;for(int i=ago;i<ago+len;i++)x=Math.Max(x,s[i]);return x;}
        private double LowestAgo(ISeries<double>s,int len,int ago){double x=double.MaxValue;for(int i=ago;i<ago+len;i++)x=Math.Min(x,s[i]);return x;}
        private double AverageRange(int len){double z=0;for(int i=0;i<len;i++)z+=High[i]-Low[i];return z/len;}
        private double LinRegValue(ISeries<double>s,double offset,int len){double sx=0,sy=0,sxy=0,sxx=0;for(int i=0;i<len;i++){double x=i,y=s[len-1-i]-offset;sx+=x;sy+=y;sxy+=x*y;sxx+=x*x;}double den=len*sxx-sx*sx;if(den==0)return 0;double slope=(len*sxy-sx*sy)/den,inter=(sy-slope*sx)/len;return inter+slope*(len-1);}
        private double SimpleAverageRSI(int len){return AverageIndicator(rsi14,len);} private double StdRSI(int len){return StdIndicator(rsi14,len);}
        private double AverageIndicator(ISeries<double>s,int len){return SimpleAverageAgo(s,len,0);} private double AverageIndicatorAgo(ISeries<double>s,int len,int ago){return SimpleAverageAgo(s,len,ago);}
        private double StdIndicator(ISeries<double>s,int len){return StdAgo(s,len,0);} private double StdIndicatorAgo(ISeries<double>s,int len,int ago){return StdAgo(s,len,ago);}
        private double WmaIndicator(ISeries<double>s,int len){return WmaAgo(s,len,0);} private double WmaIndicatorAgo(ISeries<double>s,int len,int ago){return WmaAgo(s,len,ago);}
        private double WmaAgo(ISeries<double>s,int len,int ago){double z=0,w=0;for(int i=0;i<len;i++){double wt=len-i;z+=s[ago+i]*wt;w+=wt;}return w==0?0:z/w;}
        private double WmaArray(double[]a,int ago,int len){double z=0,w=0;for(int i=0;i<len && ago+i<a.Length;i++){double wt=len-i;z+=a[ago+i]*wt;w+=wt;}return w==0?0:z/w;}
        private bool CrossAboveValue(double c,double p,double level,double prevLevel){return c>level && p<=prevLevel;}
        private bool CrossBelowValue(double c,double p,double level,double prevLevel){return c<level && p>=prevLevel;}
        private bool CrossAbove(ISeries<double>s,double level,int lookback){return s[0]>level && s[1]<=level;}
        private bool CrossBelow(ISeries<double>s,double level,int lookback){return s[0]<level && s[1]>=level;}

        #region Properties
        [NinjaScriptProperty][Display(Name="Cloud Type",Order=1,GroupName="Visible Settings")]
        public string CloudType { get; set; }
        [NinjaScriptProperty][TypeConverter(typeof(NebulaCandleColoringConverter))][Display(Name="Candle Coloring",Order=2,GroupName="Visible Settings")]
        public string CandleColoring { get; set; }
        [NinjaScriptProperty][TypeConverter(typeof(NebulaThemeConverter))][Display(Name="Theme",Order=3,GroupName="Visible Settings")]
        public string Theme { get; set; }
        [NinjaScriptProperty][Display(Name="Show HEMA",Order=4,GroupName="Visible Settings")]
        public bool ShowHEMA { get; set; }
        [NinjaScriptProperty][Display(Name="Show +",Order=5,GroupName="Visible Settings")]
        public bool ShowPlus { get; set; }
        [NinjaScriptProperty][Display(Name="Show Big +",Order=6,GroupName="Visible Settings")]
        public bool ShowBigPlus { get; set; }
        [NinjaScriptProperty][Display(Name="Enhance Strong Signals",Order=7,GroupName="Visible Settings")]
        public bool EnhanceStrongSignals { get; set; }
        [NinjaScriptProperty][Display(Name="Show Profit",Order=8,GroupName="Visible Settings")]
        public bool ShowProfit { get; set; }
        [NinjaScriptProperty][Display(Name="Show 9/21",Order=9,GroupName="Visible Settings")]
        public bool Show921 { get; set; }

        [NinjaScriptProperty][Display(Name="Ignore Doji",Order=1,GroupName="Basic Settings")]
        public bool IgnoreDoji { get; set; }
        [NinjaScriptProperty][Range(2,50)][Display(Name="Partial Profit Score",Order=3,GroupName="Basic Settings")]
        public int ProfitThreshold { get; set; }
        [NinjaScriptProperty][Range(3,50)][Display(Name="Full Profit Score",Order=4,GroupName="Basic Settings")]
        public int MaxProfitThreshold { get; set; }
        [NinjaScriptProperty][Range(1,4)][Display(Name="Doji Max Ticks",Order=5,GroupName="Basic Settings")]
        public int MaxDojiTicks { get; set; }

        [NinjaScriptProperty][Display(Name="Show Imbalance Lines",Order=2,GroupName="Tidal Wave")]
        public bool ShowVolumeImbalanceLines { get; set; }
        [NinjaScriptProperty][Range(10,500)][Display(Name="Line Bars",Order=3,GroupName="Tidal Wave")]
        public int ImbalanceLineBars { get; set; }
        [NinjaScriptProperty][Range(1,10)][Display(Name="Line Width",Order=4,GroupName="Tidal Wave")]
        public int ImbalanceLineWidth { get; set; }

        [NinjaScriptProperty][Display(Name="Show Reversal Pattern",Order=1,GroupName="Advanced")]
        public bool ShowReversalPattern { get; set; }
        [NinjaScriptProperty][Display(Name="Show Retests",Order=2,GroupName="Advanced")]
        public bool ShowRetests { get; set; }
        [NinjaScriptProperty][Display(Name="Show Dashboard (signal legend)",Order=20,GroupName="Advanced")]
        public bool ShowDashboard { get; set; }
        [NinjaScriptProperty][TypeConverter(typeof(NebulaDashPositionConverter))][Display(Name="Dashboard Position",Order=21,GroupName="Advanced")]
        public string DashboardPosition { get; set; }
        [NinjaScriptProperty][Display(Name="Use Quadratic 9/21",Order=3,GroupName="Advanced")]
        public bool UseQuadratic921 { get; set; }
        [NinjaScriptProperty][Range(0,100)][Display(Name="Cloud Opacity",Order=4,GroupName="Advanced")]
        public int SimpleCloudOpacity { get; set; }
        [NinjaScriptProperty][Range(0,100)][Display(Name="Low Cloud Opacity",Order=5,GroupName="Advanced")]
        public int LowCloudOpacity { get; set; }
        [NinjaScriptProperty][Range(0,100)][Display(Name="High Cloud Opacity",Order=6,GroupName="Advanced")]
        public int HighCloudOpacity { get; set; }

        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Length",Order=1,GroupName="ADX")]
        public int AdxLength { get; set; }
        [NinjaScriptProperty][Range(1,100)][Display(Name="DI Length",Order=2,GroupName="ADX")]
        public int DiLength { get; set; }

        [NinjaScriptProperty][Range(1,50)][Display(Name="ADX Length",Order=1,GroupName="Fantail VMA")]
        public int FantailAdxLength { get; set; }
        [NinjaScriptProperty][Range(1,100)][Display(Name="Weighting",Order=2,GroupName="Fantail VMA")]
        public double FantailWeighting { get; set; }
        [NinjaScriptProperty][Range(1,100)][Display(Name="MA Length",Order=3,GroupName="Fantail VMA")]
        public int FantailMaLength { get; set; }

        [NinjaScriptProperty][Range(1,500)][Display(Name="Sensitivity",Order=1,GroupName="WAE")]
        public int WaeSensitivity { get; set; }
        [NinjaScriptProperty][Range(1,100)][Display(Name="Fast Length",Order=2,GroupName="WAE")]
        public int WaeFastLength { get; set; }
        [NinjaScriptProperty][Range(2,200)][Display(Name="Slow Length",Order=3,GroupName="WAE")]
        public int WaeSlowLength { get; set; }
        [NinjaScriptProperty][Range(2,100)][Display(Name="Channel Length",Order=4,GroupName="WAE")]
        public int WaeChannelLength { get; set; }
        [NinjaScriptProperty][Range(.1,10)][Display(Name="BB Multiplier",Order=5,GroupName="WAE")]
        public double WaeMultiplier { get; set; }

        [NinjaScriptProperty][Range(0,1)][Display(Name="BB Width Threshold",Order=1,GroupName="Trampoline")]
        public double TrampolineBbThreshold { get; set; }
        [NinjaScriptProperty][Range(1,99)][Display(Name="RSI Lower",Order=2,GroupName="Trampoline")]
        public int TrampolineRsiLower { get; set; }
        [NinjaScriptProperty][Range(1,99)][Display(Name="RSI Upper",Order=3,GroupName="Trampoline")]
        public int TrampolineRsiUpper { get; set; }

        [NinjaScriptProperty][Range(1,20)][Display(Name="Tolerance",Order=1,GroupName="Squeeze")]
        public int SqueezeTolerance { get; set; }
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Threshold",Order=2,GroupName="Squeeze")]
        public int SqueezeAdxThreshold { get; set; }

        [NinjaScriptProperty][Range(1,200)][Display(Name="Watch Lookback",Order=1,GroupName="Ultimate Buy Sell")]
        public int WatchSignalLookback { get; set; }

        [NinjaScriptProperty][Range(1,100)][Display(Name="Alpha Length",Order=1,GroupName="HEMA")]
        public int AlphaLength { get; set; }
        [NinjaScriptProperty][Range(1,100)][Display(Name="Gamma Length",Order=2,GroupName="HEMA")]
        public int GammaLength { get; set; }

        [NinjaScriptProperty][Range(3,100)][Display(Name="Lookback",Order=1,GroupName="Kernel")]
        public double KernelLookback { get; set; }
        [NinjaScriptProperty][Range(.25,50)][Display(Name="Relative Weight",Order=2,GroupName="Kernel")]
        public double KernelRelativeWeight { get; set; }
        [NinjaScriptProperty][Range(1,100)][Display(Name="Start Bar",Order=3,GroupName="Kernel")]
        public int KernelStartBar { get; set; }

        [NinjaScriptProperty][Display(Name="Enable Alerts",Order=1,GroupName="Alerts")]
        public bool EnableAlerts { get; set; }

        [XmlIgnore][Display(Name="Bull Color (up candles / buy signals)",Order=1,GroupName="Colors")]
        public Brush BullBrush { get { return bullBrush; } set { bullBrush = value; } }
        [Browsable(false)]
        public string BullBrushSerialize { get { return NinjaTrader.Gui.Serialize.BrushToString(bullBrush); } set { bullBrush = NinjaTrader.Gui.Serialize.StringToBrush(value); } }

        [XmlIgnore][Display(Name="Bear Color (down candles / sell signals)",Order=2,GroupName="Colors")]
        public Brush BearBrush { get { return bearBrush; } set { bearBrush = value; } }
        [Browsable(false)]
        public string BearBrushSerialize { get { return NinjaTrader.Gui.Serialize.BrushToString(bearBrush); } set { bearBrush = NinjaTrader.Gui.Serialize.StringToBrush(value); } }

        [XmlIgnore][Display(Name="Partial Profit / Retest Color",Order=3,GroupName="Colors")]
        public Brush PartialProfitBrush { get { return partialProfitBrush; } set { partialProfitBrush = value; } }
        [Browsable(false)]
        public string PartialProfitBrushSerialize { get { return NinjaTrader.Gui.Serialize.BrushToString(partialProfitBrush); } set { partialProfitBrush = NinjaTrader.Gui.Serialize.StringToBrush(value); } }

        [XmlIgnore][Display(Name="Skull / Reversal Color",Order=4,GroupName="Colors")]
        public Brush SkullBrush { get { return skullBrush; } set { skullBrush = value; } }
        [Browsable(false)]
        public string SkullBrushSerialize { get { return NinjaTrader.Gui.Serialize.BrushToString(skullBrush); } set { skullBrush = NinjaTrader.Gui.Serialize.StringToBrush(value); } }

        [XmlIgnore][Display(Name="Vector Medium Up Color",Order=5,GroupName="Colors")]
        public Brush VectorMediumUpBrush { get { return vectorMediumUpBrush; } set { vectorMediumUpBrush = value; } }
        [Browsable(false)]
        public string VectorMediumUpBrushSerialize { get { return NinjaTrader.Gui.Serialize.BrushToString(vectorMediumUpBrush); } set { vectorMediumUpBrush = NinjaTrader.Gui.Serialize.StringToBrush(value); } }

        [XmlIgnore][Display(Name="Vector Medium Down Color",Order=6,GroupName="Colors")]
        public Brush VectorMediumDownBrush { get { return vectorMediumDownBrush; } set { vectorMediumDownBrush = value; } }
        [Browsable(false)]
        public string VectorMediumDownBrushSerialize { get { return NinjaTrader.Gui.Serialize.BrushToString(vectorMediumDownBrush); } set { vectorMediumDownBrush = NinjaTrader.Gui.Serialize.StringToBrush(value); } }

        [Browsable(false)][XmlIgnore] public Series<double> Signal { get { return Values[0]; } }
        [Browsable(false)][XmlIgnore] public Series<double> AddSignal { get { return Values[1]; } }
        [Browsable(false)][XmlIgnore] public Series<double> ProfitSignal { get { return Values[2]; } }
        #endregion
    }

    public class NebulaThemeConverter : TypeConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(new string[] { "Standard", "Pinky and the Brain", "Color Blind", "Mellow Yellow" });
        }
    }

    public class NebulaCandleColoringConverter : TypeConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(new string[] { "None", "Waddah", "Squeeze", "Volume Delta", "Vector" });
        }
    }

    public class NebulaDashPositionConverter : TypeConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(new string[] { "Top Right", "Top Left" });
        }
    }
}

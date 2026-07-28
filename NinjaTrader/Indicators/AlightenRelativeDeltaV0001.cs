#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenRelativeDeltaV0001 : Indicator
    {
        private double tickSize;
        private double sessionOpenPrice;
        private Dictionary<int, Dictionary<double, double>> GetAskVolumeForPrice = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> GetBidVolumeForPrice = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> GetTotalVolumeForPrice = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> GetDeltaForPrice = new Dictionary<int, Dictionary<double, double>>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Relative Delta Only Footprint Indicator - By Alighten";
                Name = "AlightenRelativeDeltaV0001";
                Calculate = Calculate.OnEachTick;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = false;
                ShowTransparentPlotsInDataBox = true;
                
                AddPlot(System.Windows.Media.Brushes.Transparent, "RelativeDeltaWick");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                tickSize = TickSize;
            }
        }

        protected override void OnBarUpdate()
        {
            if (Bars == null || BarsArray == null || BarsArray.Length < 2) return;
            if (CurrentBars[0] < 0 || CurrentBars[1] < 0) return;

            if (Bars.IsFirstBarOfSession)
                sessionOpenPrice = Open[0];

            double binInterval = AggregationInterval * TickSize;
            if (!(binInterval > 0.0) || double.IsNaN(binInterval) || double.IsInfinity(binInterval)) return;
            int ticksPerBin = Math.Max(1, (int)Math.Round(binInterval / TickSize));
            binInterval = ticksPerBin * TickSize;

            if (BarsInProgress == 1)
            {
                int indexOffset = BarsArray[1].Count - 1 - CurrentBars[1];
                bool inTransition = (State == State.Realtime) && (indexOffset > 1);
                bool useCurrent = State == State.Historical || inTransition || (Calculate != Calculate.OnBarClose);
                int ti = useCurrent ? CurrentBars[1] : Math.Min(CurrentBars[1] + 1, BarsArray[1].Count - 1);

                DateTime t = BarsArray[1].GetTime(ti);
                int idx = BarsArray[0].GetBar(t);
                if (idx < 0 || idx > CurrentBar) return;

                if (!GetAskVolumeForPrice.ContainsKey(idx)) GetAskVolumeForPrice[idx] = new Dictionary<double, double>();
                if (!GetBidVolumeForPrice.ContainsKey(idx)) GetBidVolumeForPrice[idx] = new Dictionary<double, double>();
                if (!GetDeltaForPrice.ContainsKey(idx)) GetDeltaForPrice[idx] = new Dictionary<double, double>();
                if (!GetTotalVolumeForPrice.ContainsKey(idx)) GetTotalVolumeForPrice[idx] = new Dictionary<double, double>();

                double price = BarsArray[1].GetClose(ti);
                double vol = BarsArray[1].GetVolume(ti);

                double askPx = BarsArray[1].GetAsk(ti);
                double bidPx = BarsArray[1].GetBid(ti);
                bool isBuy = (askPx > 0 && price >= askPx);
                bool isSell = (bidPx > 0 && price <= bidPx);
                if (!isBuy && !isSell) return;

                double origin = sessionOpenPrice;
                origin = Math.Round(origin / TickSize) * TickSize;

                double binsFromOrigin = Math.Floor(((price - origin) / binInterval) + 1e-9);
                double binStart = origin + binsFromOrigin * binInterval;
                binStart = Math.Round(binStart / TickSize) * TickSize;

                double ask = 0, bid = 0;
                GetAskVolumeForPrice[idx].TryGetValue(binStart, out ask);
                GetBidVolumeForPrice[idx].TryGetValue(binStart, out bid);

                if (isBuy) ask += vol;
                if (isSell) bid += vol;

                GetAskVolumeForPrice[idx][binStart] = ask;
                GetBidVolumeForPrice[idx][binStart] = bid;
                GetTotalVolumeForPrice[idx][binStart] = ask + bid;
                GetDeltaForPrice[idx][binStart] = ask - bid;
            }

            if (BarsInProgress == 0)
            {
                Values[0][0] = 0; // Initialize plot to prevent "barsAgo" crash in hosted indicators

                if (BarsToProcess > 0)
                {
                    int cutoffBar = Count - BarsToProcess;
                    if (CurrentBar < cutoffBar)
                        return;
                }

                relativeDeltaSignal = 0;
                if (EnableRelativeDeltaSignal)
                {
                    relativeDeltaSignal = GetLiveSignal();
                }

                Values[0][0] = relativeDeltaSignal;

                if (EnableRelativeDeltaSignal)
                {
                    int longVal = (relativeDeltaSignal == 1 || relativeDeltaSignal == 3) ? 1 : 0;
                    int shortVal = (relativeDeltaSignal == -1 || relativeDeltaSignal == 3) ? -1 : 0;
                    DrawSignalDiamond(CurrentBar, "RelDeltaLong", longVal, RelativeDeltaColor, RelativeDeltaDiamondOffset);
                    DrawSignalDiamond(CurrentBar, "RelDeltaShort", shortVal, RelativeDeltaColor, RelativeDeltaDiamondOffset);
                }
                if (EnableDebugPrints)
                {
                    Print($"[RelativeDelta] Series: {BarsPeriod.Value}{BarsPeriod.BarsPeriodType} | Time: {Time[0]:MM/dd/yyyy HH:mm:ss} | Bar: {CurrentBar} | Signal: {relativeDeltaSignal}");
                }
            }
        }

        public int GetLiveSignal()
        {
            return GetLiveSignalByBar(CurrentBar);
        }

        public int GetLiveSignal(DateTime time)
        {
            int targetBar = BarsArray[0].GetBar(time);
            if (targetBar < 0 || targetBar >= BarsArray[0].Count) return 0;
            return GetLiveSignalByBar(targetBar);
        }

        private int GetLiveSignalByBar(int targetBar)
        {
            if (targetBar < 0) return 0;
            
            Dictionary<double, double> dMap = null;
            bool haveBins = GetAskVolumeForPrice.TryGetValue(targetBar, out var aMap) &&
                            GetBidVolumeForPrice.TryGetValue(targetBar, out var bMap) &&
                            GetTotalVolumeForPrice.TryGetValue(targetBar, out var totMap) &&
                            GetDeltaForPrice.TryGetValue(targetBar, out dMap);

            if (!haveBins) return 0;

            int anchorStart = GetAnchorStartBar(targetBar);
            Dictionary<double, double> anchoredDeltaMap = new Dictionary<double, double>();

            for (int i = anchorStart; i <= targetBar; i++)
            {
                if (GetDeltaForPrice.TryGetValue(i, out var dM))
                {
                    foreach (var kvp in dM)
                    {
                        if (!anchoredDeltaMap.ContainsKey(kvp.Key))
                            anchoredDeltaMap[kvp.Key] = kvp.Value;
                        else
                            anchoredDeltaMap[kvp.Key] += kvp.Value;
                    }
                }
            }

            var sortedDesc = new List<KeyValuePair<double, double>>(dMap);
            sortedDesc.Sort((x, y) => y.Key.CompareTo(x.Key));

            var sortedAsc = new List<KeyValuePair<double, double>>(dMap);
            sortedAsc.Sort((x, y) => x.Key.CompareTo(y.Key));

            double topBody = Math.Max(Open.GetValueAt(targetBar), Close.GetValueAt(targetBar));
            double bottomBody = Math.Min(Open.GetValueAt(targetBar), Close.GetValueAt(targetBar));

            bool shortSignal = false;
            bool longSignal = false;
            
            double binInterval = AggregationInterval * TickSize;
            if (binInterval <= 0) return 0;
            int ticksPerBin = Math.Max(1, (int)Math.Round(binInterval / TickSize));
            binInterval = ticksPerBin * TickSize;

            for (int i = 0; i < Math.Min(RelativeDeltaMaxPosition, sortedDesc.Count); i++)
            {
                double p = sortedDesc[i].Key;
                int currentBarDelta = (int)sortedDesc[i].Value;
                double previousAnchoredDelta = 0;
                if (anchoredDeltaMap.TryGetValue(p, out double totalAnchoredDelta))
                    previousAnchoredDelta = totalAnchoredDelta - currentBarDelta;

                if (previousAnchoredDelta != 0)
                {
                    double relativePct = (currentBarDelta / Math.Abs(previousAnchoredDelta)) * 100;
                    if (relativePct > 0 && relativePct >= RelativeDeltaMinimumPercentage)
                    {
                        int distLevels = (int)Math.Round((p - topBody) / binInterval);
                        if (distLevels >= RelativeDeltaMinimumWickLevels)
                            shortSignal = true;
                    }
                }
            }

            for (int i = 0; i < Math.Min(RelativeDeltaMaxPosition, sortedAsc.Count); i++)
            {
                double p = sortedAsc[i].Key;
                int currentBarDelta = (int)sortedAsc[i].Value;
                double previousAnchoredDelta = 0;
                if (anchoredDeltaMap.TryGetValue(p, out double totalAnchoredDelta))
                    previousAnchoredDelta = totalAnchoredDelta - currentBarDelta;

                if (previousAnchoredDelta != 0)
                {
                    double relativePct = (currentBarDelta / Math.Abs(previousAnchoredDelta)) * 100;
                    if (relativePct < 0 && Math.Abs(relativePct) >= RelativeDeltaMinimumPercentage)
                    {
                        int distLevels = (int)Math.Round((bottomBody - (p + binInterval)) / binInterval);
                        if (distLevels >= RelativeDeltaMinimumWickLevels)
                            longSignal = true;
                    }
                }
            }

            if (shortSignal && longSignal) return 3;
            else if (shortSignal) return -1;
            else if (longSignal) return 1;
            return 0;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
            if (RenderTarget == null || ChartBars == null) return;
            if (!EnableFootprintText) return;

            int firstBar = ChartBars.FromIndex;
            if (BarsToProcess > 0)
            {
                int cutoffBar = ChartBars.Count - BarsToProcess;
                if (firstBar < cutoffBar) firstBar = cutoffBar;
            }
            int lastBar = ChartBars.ToIndex;

            SharpDX.DirectWrite.TextFormat textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", FootprintTextFontSize)
            {
                TextAlignment = SharpDX.DirectWrite.TextAlignment.Center,
                ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center,
                WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap
            };

            SharpDX.Direct2D1.SolidColorBrush posDeltaColor = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(PositiveRelativeDeltaTextColor));
            SharpDX.Direct2D1.SolidColorBrush negDeltaColor = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(NegativeRelativeDeltaTextColor));

            float totalBarWidth = (float)chartControl.Properties.BarDistance * 0.8f;
            for (int barIndex = firstBar; barIndex <= lastBar; barIndex++)
            {
                float xBar = chartControl.GetXByBarIndex(ChartBars, barIndex);
                float xStart = xBar - totalBarWidth / 2f;
                float xEnd = xBar + totalBarWidth / 2f;

                int anchorStart = GetAnchorStartBar(barIndex);
                Dictionary<double, double> anchoredDeltaMap = new Dictionary<double, double>();

                for (int i = anchorStart; i <= barIndex; i++)
                {
                    if (GetDeltaForPrice.TryGetValue(i, out var dMap))
                    {
                        foreach (var kvp in dMap)
                        {
                            if (!anchoredDeltaMap.ContainsKey(kvp.Key))
                                anchoredDeltaMap[kvp.Key] = kvp.Value;
                            else
                                anchoredDeltaMap[kvp.Key] += kvp.Value;
                        }
                    }
                }

                if (GetDeltaForPrice.TryGetValue(barIndex, out var currentDeltaMap))
                {
                    foreach (var kvp in currentDeltaMap)
                    {
                        double price = kvp.Key;
                        int currentBarDelta = (int)kvp.Value;

                        double previousAnchoredDelta = 0;
                        if (anchoredDeltaMap.TryGetValue(price, out double totalAnchoredDelta))
                            previousAnchoredDelta = totalAnchoredDelta - currentBarDelta;

                        double relativeDeltaPct = 0;
                        if (previousAnchoredDelta != 0)
                            relativeDeltaPct = (currentBarDelta / Math.Abs(previousAnchoredDelta)) * 100;

                        string textVal = $"{Math.Round(relativeDeltaPct)}%";
                        var drawBrush = (currentBarDelta > 0) ? posDeltaColor : negDeltaColor;

                        float yTop = chartScale.GetYByValue(price + AggregationInterval * TickSize);
                        float yBot = chartScale.GetYByValue(price);

                        var rect = new SharpDX.RectangleF(xStart, yTop, xEnd - xStart, yBot - yTop);
                        RenderTarget.DrawText(textVal, textFormat, rect, drawBrush);
                    }
                }
            }

            textFormat.Dispose();
            posDeltaColor.Dispose();
            negDeltaColor.Dispose();
        }

        private int GetAnchorStartBar(int barIndex)
        {
            if (AnchorPeriod == 0) return barIndex;
            if (Bars == null) return 0;

            DateTime currentTime = Bars.GetTime(barIndex);
            DateTime currentTradingDay = GetGlobexTradingDate(currentTime);

            for (int i = barIndex; i >= 0; i--)
            {
                if (GetGlobexTradingDate(Bars.GetTime(i)) != currentTradingDay)
                    return i + 1;
            }
            return 0;
        }

        private DateTime GetGlobexTradingDate(DateTime time)
        {
            DateTime d = time.Date;
            TimeSpan t = time.TimeOfDay;
            TimeSpan globex = new TimeSpan(18, 0, 0); // 18:00 EST

            if (t < globex)
                d = d.AddDays(-1);

            return d;
        }

        private SharpDX.Color4 ConvertMediaBrushToColor4(System.Windows.Media.Brush mediaBrush)
        {
            var scb = mediaBrush as System.Windows.Media.SolidColorBrush;
            if (scb == null)
                return new SharpDX.Color4(0.2f, 0.2f, 0.2f, 1f);

            float a = (float)scb.Color.A / 255f;
            float r = (float)scb.Color.R / 255f;
            float g = (float)scb.Color.G / 255f;
            float b = (float)scb.Color.B / 255f;
            return new SharpDX.Color4(r, g, b, a);
        }

        private void DrawSignalDiamond(int bar, string signalName, int signalValue, System.Windows.Media.Brush signalBrush, int offsetValue)
        {
            string longTag = signalName + "_long_" + bar;
            string shortTag = signalName + "_short_" + bar;

            RemoveDrawObject(longTag);
            RemoveDrawObject(shortTag);

            if (signalValue == 0) return;

            double tickOffset = offsetValue * TickSize;

            if (signalValue == 1 || signalValue == 3)
            {
                double price = Low[0] - tickOffset;
                Draw.Diamond(this, longTag, false, 0, price, signalBrush);
            }
            if (signalValue == -1 || signalValue == 3)
            {
                double price = High[0] + tickOffset;
                Draw.Diamond(this, shortTag, false, 0, price, signalBrush);
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Tick Aggregation", Order = 1, GroupName = "Relative Delta")]
        public int AggregationInterval { get; set; } = 6;

        [NinjaScriptProperty]
        [Display(Name = "Value Area %", Order = 2, GroupName = "Relative Delta")]
        public double ValueAreaPer { get; set; } = 70;

        [NinjaScriptProperty]
        [Display(Name = "Anchored Profile Period", Order = 3, GroupName = "Relative Delta")]
        public int AnchorPeriod { get; set; } = 1;

        [XmlIgnore]
        [Display(Name = "Positive Relative Delta Text Color", Order = 4, GroupName = "Relative Delta")]
        public System.Windows.Media.Brush PositiveRelativeDeltaTextColor { get; set; } = System.Windows.Media.Brushes.DeepSkyBlue;
        [Browsable(false)]
        public string PositiveRelativeDeltaTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(PositiveRelativeDeltaTextColor); }
            set { PositiveRelativeDeltaTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Negative Relative Delta Text Color", Order = 5, GroupName = "Relative Delta")]
        public System.Windows.Media.Brush NegativeRelativeDeltaTextColor { get; set; } = System.Windows.Media.Brushes.Fuchsia;
        [Browsable(false)]
        public string NegativeRelativeDeltaTextColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(NegativeRelativeDeltaTextColor); }
            set { NegativeRelativeDeltaTextColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Relative Delta Text Font Size", Order = 6, GroupName = "Relative Delta")]
        public int FootprintTextFontSize { get; set; } = 16;

        [NinjaScriptProperty]
        [Display(Name = "Enable Relative Delta Wick Diamond Signal", Order = 7, GroupName = "Relative Delta")]
        public bool EnableRelativeDeltaSignal { get; set; } = true;

        [XmlIgnore]
        [Display(Name = "Relative Delta Wick Signal Color", Order = 8, GroupName = "Relative Delta")]
        public System.Windows.Media.Brush RelativeDeltaColor { get; set; } = System.Windows.Media.Brushes.Fuchsia;
        [Browsable(false)]
        public string RelativeDeltaColorSerialize
        {
            get { return NinjaTrader.Gui.Serialize.BrushToString(RelativeDeltaColor); }
            set { RelativeDeltaColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Relative Delta Wick Signal Diamond Offset", Order = 9, GroupName = "Relative Delta")]
        public int RelativeDeltaDiamondOffset { get; set; } = 15;

        [NinjaScriptProperty]
        [Display(Name = "Relative Delta Minimum %", Order = 10, GroupName = "Relative Delta")]
        public int RelativeDeltaMinimumPercentage { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Relative Delta Max Position", Order = 11, GroupName = "Relative Delta")]
        public int RelativeDeltaMaxPosition { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Relative Delta Min Wick Levels", Order = 12, GroupName = "Relative Delta")]
        public int RelativeDeltaMinimumWickLevels { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(0, 500000)]
        [Display(Name="Bars To Process (0 = All)", GroupName="Parameters", Order=13)]
        public int BarsToProcess { get; set; } = 200;

        [NinjaScriptProperty]
        [Display(Name = "Enable Debug Prints", Order = 14, GroupName = "Relative Delta")]
        public bool EnableDebugPrints { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Enable Footprint Text", Order = 15, GroupName = "Relative Delta")]
        public bool EnableFootprintText { get; set; } = true;

        private int relativeDeltaSignal = 0;
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenRelativeDeltaV0001[] cacheAlightenRelativeDeltaV0001;
		public AlightenRelativeDeltaV0001 AlightenRelativeDeltaV0001(int aggregationInterval, double valueAreaPer, int anchorPeriod, int footprintTextFontSize, bool enableRelativeDeltaSignal, int relativeDeltaDiamondOffset, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableFootprintText)
		{
			return AlightenRelativeDeltaV0001(Input, aggregationInterval, valueAreaPer, anchorPeriod, footprintTextFontSize, enableRelativeDeltaSignal, relativeDeltaDiamondOffset, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableFootprintText);
		}

		public AlightenRelativeDeltaV0001 AlightenRelativeDeltaV0001(ISeries<double> input, int aggregationInterval, double valueAreaPer, int anchorPeriod, int footprintTextFontSize, bool enableRelativeDeltaSignal, int relativeDeltaDiamondOffset, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableFootprintText)
		{
			if (cacheAlightenRelativeDeltaV0001 != null)
				for (int idx = 0; idx < cacheAlightenRelativeDeltaV0001.Length; idx++)
					if (cacheAlightenRelativeDeltaV0001[idx] != null && cacheAlightenRelativeDeltaV0001[idx].AggregationInterval == aggregationInterval && cacheAlightenRelativeDeltaV0001[idx].ValueAreaPer == valueAreaPer && cacheAlightenRelativeDeltaV0001[idx].AnchorPeriod == anchorPeriod && cacheAlightenRelativeDeltaV0001[idx].FootprintTextFontSize == footprintTextFontSize && cacheAlightenRelativeDeltaV0001[idx].EnableRelativeDeltaSignal == enableRelativeDeltaSignal && cacheAlightenRelativeDeltaV0001[idx].RelativeDeltaDiamondOffset == relativeDeltaDiamondOffset && cacheAlightenRelativeDeltaV0001[idx].RelativeDeltaMinimumPercentage == relativeDeltaMinimumPercentage && cacheAlightenRelativeDeltaV0001[idx].RelativeDeltaMaxPosition == relativeDeltaMaxPosition && cacheAlightenRelativeDeltaV0001[idx].RelativeDeltaMinimumWickLevels == relativeDeltaMinimumWickLevels && cacheAlightenRelativeDeltaV0001[idx].BarsToProcess == barsToProcess && cacheAlightenRelativeDeltaV0001[idx].EnableDebugPrints == enableDebugPrints && cacheAlightenRelativeDeltaV0001[idx].EnableFootprintText == enableFootprintText && cacheAlightenRelativeDeltaV0001[idx].EqualsInput(input))
						return cacheAlightenRelativeDeltaV0001[idx];
			return CacheIndicator<AlightenRelativeDeltaV0001>(new AlightenRelativeDeltaV0001(){ AggregationInterval = aggregationInterval, ValueAreaPer = valueAreaPer, AnchorPeriod = anchorPeriod, FootprintTextFontSize = footprintTextFontSize, EnableRelativeDeltaSignal = enableRelativeDeltaSignal, RelativeDeltaDiamondOffset = relativeDeltaDiamondOffset, RelativeDeltaMinimumPercentage = relativeDeltaMinimumPercentage, RelativeDeltaMaxPosition = relativeDeltaMaxPosition, RelativeDeltaMinimumWickLevels = relativeDeltaMinimumWickLevels, BarsToProcess = barsToProcess, EnableDebugPrints = enableDebugPrints, EnableFootprintText = enableFootprintText }, input, ref cacheAlightenRelativeDeltaV0001);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenRelativeDeltaV0001 AlightenRelativeDeltaV0001(int aggregationInterval, double valueAreaPer, int anchorPeriod, int footprintTextFontSize, bool enableRelativeDeltaSignal, int relativeDeltaDiamondOffset, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableFootprintText)
		{
			return indicator.AlightenRelativeDeltaV0001(Input, aggregationInterval, valueAreaPer, anchorPeriod, footprintTextFontSize, enableRelativeDeltaSignal, relativeDeltaDiamondOffset, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableFootprintText);
		}

		public Indicators.AlightenRelativeDeltaV0001 AlightenRelativeDeltaV0001(ISeries<double> input , int aggregationInterval, double valueAreaPer, int anchorPeriod, int footprintTextFontSize, bool enableRelativeDeltaSignal, int relativeDeltaDiamondOffset, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableFootprintText)
		{
			return indicator.AlightenRelativeDeltaV0001(input, aggregationInterval, valueAreaPer, anchorPeriod, footprintTextFontSize, enableRelativeDeltaSignal, relativeDeltaDiamondOffset, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableFootprintText);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenRelativeDeltaV0001 AlightenRelativeDeltaV0001(int aggregationInterval, double valueAreaPer, int anchorPeriod, int footprintTextFontSize, bool enableRelativeDeltaSignal, int relativeDeltaDiamondOffset, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableFootprintText)
		{
			return indicator.AlightenRelativeDeltaV0001(Input, aggregationInterval, valueAreaPer, anchorPeriod, footprintTextFontSize, enableRelativeDeltaSignal, relativeDeltaDiamondOffset, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableFootprintText);
		}

		public Indicators.AlightenRelativeDeltaV0001 AlightenRelativeDeltaV0001(ISeries<double> input , int aggregationInterval, double valueAreaPer, int anchorPeriod, int footprintTextFontSize, bool enableRelativeDeltaSignal, int relativeDeltaDiamondOffset, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableFootprintText)
		{
			return indicator.AlightenRelativeDeltaV0001(input, aggregationInterval, valueAreaPer, anchorPeriod, footprintTextFontSize, enableRelativeDeltaSignal, relativeDeltaDiamondOffset, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableFootprintText);
		}
	}
}

#endregion

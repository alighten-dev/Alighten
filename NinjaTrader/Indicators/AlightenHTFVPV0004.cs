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
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenHTFVPV0004 : Indicator
    {
        private double currentPOC = 0;
        private double currentVAH = 0;
        private double currentVAL = 0;
        private double currentBias = 0;

        // Volume tracking for the HTF
        private double htfHighestPrice = double.MinValue;
        private double htfLowestPrice = double.MaxValue;
        private Dictionary<double, double> tickVolumeAccumulator = new Dictionary<double, double>();

        private bool isNewHTFBar = true;
        private int currentHTFStartBarIndex = -1;

        public class HTFProfile
        {
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public double VAH { get; set; }
            public double VAL { get; set; }
            public double POC { get; set; }
            public double Bias { get; set; }
        }

        public List<HTFProfile> Profiles { get; private set; } = new List<HTFProfile>();
        private DateTime _lastSeenHtfTime = Core.Globals.MinDate;
        private DateTime _cutoffTime = Core.Globals.MinDate;

        public HTFProfile GetProfileAt(DateTime time)
        {
            if (Profiles == null || Profiles.Count == 0)
                return null;
            
            for (int i = Profiles.Count - 1; i >= 0; i--)
            {
                if (time >= Profiles[i].StartTime && time < Profiles[i].EndTime)
                    return Profiles[i];
            }
            return null;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Calculates Volume Profile of a Higher Timeframe (HTF) bar and plots it on the current timeframe chart for the duration of the subsequent HTF bar.";
                Name                                        = "AlightenHTFVPV0004";
                Calculate                                   = Calculate.OnEachTick;
                IsOverlay                                   = true;
                DisplayInDataBox                            = true;
                DrawOnPricePanel                            = true;
                DrawHorizontalGridLines                     = true;
                DrawVerticalGridLines                       = true;
                PaintPriceMarkers                           = true;
                ScaleJustification                          = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive                    = true;
                ShowTransparentPlotsInDataBox               = true;

                HTFBarType                                  = BarsPeriodType.Minute;
                HTFBarValue                                 = 60;
                BarsToProcess                               = 48; // Default to 48 HTF windows (e.g. 48 hours)
                ValueAreaPercentage                         = 70;
                
                VAHWidth = 3;
                VALWidth = 3;
                POCWidth = 3;
                VAHDashStyle = DashStyleHelper.Solid;
                VALDashStyle = DashStyleHelper.Solid;
                POCDashStyle = DashStyleHelper.Solid;

                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "VA High");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "VA Low");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "POC");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "Bias");
            }
            else if (State == State.Configure)
            {
                // Reset states
                tickVolumeAccumulator.Clear();
                htfHighestPrice = double.MinValue;
                htfLowestPrice = double.MaxValue;
                isNewHTFBar = true;
                currentHTFStartBarIndex = -1;
                Profiles.Clear();
                _lastSeenHtfTime = Core.Globals.MinDate;
                _cutoffTime = Core.Globals.MinDate;
            }
        }

        private DateTime GetHtfAnchorTime(DateTime time)
        {
            if (HTFBarType == BarsPeriodType.Day)
            {
                return time.Date;
            }
            if (HTFBarType == BarsPeriodType.Minute)
            {
                int totalMinutes = time.Hour * 60 + time.Minute;
                int anchorMinutes = (totalMinutes / HTFBarValue) * HTFBarValue;
                return time.Date.AddMinutes(anchorMinutes);
            }
            return time;
        }

        private DateTime GetHtfEndTime(DateTime anchorTime)
        {
            if (HTFBarType == BarsPeriodType.Day)
            {
                return anchorTime.AddDays(HTFBarValue);
            }
            return anchorTime.AddMinutes(HTFBarValue);
        }

        private readonly object _vpLock = new object();

        protected override void OnBarUpdate()
        {
            lock (_vpLock)
            {
                try
                {
                    // Ensure Primary series has started
                    if (CurrentBars[0] < 1) return;

                    // Performance Optimization: Skip historical processing if outside the user-defined window
                    if (BarsToProcess > 0)
                    {
                        if (_cutoffTime == Core.Globals.MinDate)
                        {
                            DateTime lastTime = BarsArray[0].GetTime(BarsArray[0].Count - 1);
                            double daysToSub = 2; // base buffer
                            
                            if (HTFBarType == BarsPeriodType.Day)
                                daysToSub += (BarsToProcess * HTFBarValue * 1.5);
                            else if (HTFBarType == BarsPeriodType.Minute)
                                daysToSub += ((BarsToProcess * HTFBarValue) / 1440.0 * 1.5);

                            _cutoffTime = lastTime.AddDays(-daysToSub);
                        }

                        if (Times[0][0] < _cutoffTime)
                            return;
                    }
        
                    if (BarsInProgress == 0)
                    {
                        if (isNewHTFBar)
                        {
                            currentHTFStartBarIndex = CurrentBars[0];
                            isNewHTFBar = false;
                        }
    
                        // 1. Capture the finalized volume of the closed 1-minute bar
                        if (IsFirstTickOfBar && CurrentBars[0] > 0)
                        {
                            double vol = Volume[1];
                            double barHigh = High[1];
                            double barLow = Low[1];
                            double tickSize = TickSize;
            
                            htfHighestPrice = Math.Max(htfHighestPrice, barHigh);
                            htfLowestPrice = Math.Min(htfLowestPrice, barLow);
            
                            int minIdx = (int)Math.Round(barLow / tickSize);
                            int maxIdx = (int)Math.Round(barHigh / tickSize);
                            int levels = maxIdx - minIdx + 1;
            
                            if (levels > 0)
                            {
                                double volPerLevel = vol / levels;
                                for (int j = minIdx; j <= maxIdx; j++)
                                {
                                    double p = j * tickSize;
                                    if (tickVolumeAccumulator.ContainsKey(p))
                                        tickVolumeAccumulator[p] += volPerLevel;
                                    else
                                        tickVolumeAccumulator[p] = volPerLevel;
                                }
                            }
                        }
    
                        // 2. Check if the HTF Bar has rolled over
                        DateTime currentHtfTime = GetHtfAnchorTime(Times[0][0]);
                        if (_lastSeenHtfTime != Core.Globals.MinDate && currentHtfTime != _lastSeenHtfTime)
                        {
                            if (tickVolumeAccumulator.Count > 0)
                            {
                                CalculateProfile(_lastSeenHtfTime);
                                
                                tickVolumeAccumulator.Clear();
                                htfHighestPrice = double.MinValue;
                                htfLowestPrice = double.MaxValue;
                                isNewHTFBar = true;
                            }
                        }
    
                        _lastSeenHtfTime = currentHtfTime;

                        // 3. Push stored calculated values
                        if (currentPOC > 0)
                        {
                            Values[0][0] = currentVAH;
                            Values[1][0] = currentVAL;
                            Values[2][0] = currentPOC;
                            Values[3][0] = currentBias;
                            
                            if (currentHTFStartBarIndex >= 0)
                            {
                                int barsAgoStart = CurrentBars[0] - currentHTFStartBarIndex;
                                Draw.Line(this, "VAH_" + currentHTFStartBarIndex, false, barsAgoStart, currentVAH, 0, currentVAH, VAHColor, VAHDashStyle, VAHWidth);
                                Draw.Line(this, "VAL_" + currentHTFStartBarIndex, false, barsAgoStart, currentVAL, 0, currentVAL, VALColor, VALDashStyle, VALWidth);
                                Draw.Line(this, "POC_" + currentHTFStartBarIndex, false, barsAgoStart, currentPOC, 0, currentPOC, POCColor, POCDashStyle, POCWidth);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Print("[HTFVPV0004] CRASH: " + ex.ToString());
                }
            }
        }

        private void CalculateProfile(DateTime closedHtfBarOpenTime)
        {
            var profile = InternalCalculateProfile(closedHtfBarOpenTime, false);
            if (profile == null) return;

            currentPOC = profile.POC;
            currentVAH = profile.VAH;
            currentVAL = profile.VAL;
            currentBias = profile.Bias;

            if (Profiles.Count > 0 && Profiles.Last().StartTime == profile.StartTime)
            {
                return;
            }

            // Bridge gaps: Cap the previous profile's end time to this new profile's start time
            if (Profiles.Count > 0)
            {
                Profiles.Last().EndTime = profile.StartTime;
            }

            Profiles.Add(profile);
        }

        public HTFProfile GetDevelopingProfile()
        {
            lock (_vpLock)
            {
                return InternalCalculateProfile(_lastSeenHtfTime, true);
            }
        }

        private HTFProfile InternalCalculateProfile(DateTime contextTime, bool isDeveloping)
        {
            if (tickVolumeAccumulator.Count == 0) return null;

            List<double> prices = new List<double>(tickVolumeAccumulator.Keys);
            prices.Sort();

            List<double> vols = new List<double>(prices.Count);
            double totalVol = 0.0;
            double maxVol = double.MinValue;
            int pocIdx = -1;

            for (int i = 0; i < prices.Count; i++)
            {
                double v = tickVolumeAccumulator[prices[i]];
                vols.Add(v);
                totalVol += v;
                if (v > maxVol)
                {
                    maxVol = v;
                    pocIdx = i;
                }
            }

            if (prices.Count == 0 || pocIdx < 0 || totalVol <= 0) return null;

            double targetVol = totalVol * (ValueAreaPercentage / 100.0);
            double cumVol = vols[pocIdx];

            int lowerIndex = pocIdx;
            int upperIndex = pocIdx;
            int nextDown = pocIdx - 1;
            int nextUp = pocIdx + 1;

            while (cumVol < targetVol && (nextDown >= 0 || nextUp < vols.Count))
            {
                double downVol = nextDown >= 0 ? vols[nextDown] : double.MinValue;
                double upVol = nextUp < vols.Count ? vols[nextUp] : double.MinValue;

                if (upVol > downVol)
                {
                    cumVol += upVol;
                    upperIndex = nextUp;
                    nextUp++;
                }
                else if (downVol > upVol)
                {
                    cumVol += downVol;
                    lowerIndex = nextDown;
                    nextDown--;
                }
                else
                {
                    if (nextUp < vols.Count) { cumVol += vols[nextUp]; upperIndex = nextUp; nextUp++; }
                    if (cumVol < targetVol && nextDown >= 0) { cumVol += vols[nextDown]; lowerIndex = nextDown; nextDown--; }
                }
            }

            double valPrice = Instrument.MasterInstrument.RoundToTickSize(prices[lowerIndex]);
            double pocPrice = Instrument.MasterInstrument.RoundToTickSize(prices[pocIdx]);
            double vahPrice = Instrument.MasterInstrument.RoundToTickSize(prices[upperIndex]);

            double htfClose = isDeveloping ? Closes[0][0] : Closes[0][1];
            double bias = 0;

            if (pocPrice > vahPrice) bias = -3;
            else if (pocPrice < valPrice) bias = 3;
            else if (htfClose > vahPrice) bias = 2;
            else if (htfClose < valPrice) bias = -2;
            else if (htfClose > pocPrice && htfClose <= vahPrice) bias = 1;
            else if (htfClose < pocPrice && htfClose >= valPrice) bias = -1;

            DateTime anchorTime = GetHtfAnchorTime(contextTime);
            DateTime htfBarCloseTime = GetHtfEndTime(anchorTime);

            return new HTFProfile
            {
                StartTime = isDeveloping ? anchorTime : htfBarCloseTime,
                EndTime = isDeveloping ? htfBarCloseTime : DateTime.MaxValue,
                VAH = vahPrice,
                VAL = valPrice,
                POC = pocPrice,
                Bias = bias
            };
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Bars To Process", Order=0, GroupName="1. Parameters")]
        public int BarsToProcess { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF Bar Type", Order=1, GroupName="1. Parameters")]
        public BarsPeriodType HTFBarType { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="HTF Bar Value", Order=2, GroupName="1. Parameters")]
        public int HTFBarValue { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="Value Area Percentage", Order=3, GroupName="1. Parameters")]
        public double ValueAreaPercentage { get; set; }

        [XmlIgnore]
        [Display(Name = "VAH Line Color", GroupName = "2. Visuals", Order = 4)]
        public Brush VAHColor { get; set; } = Brushes.Blue;

        [Browsable(false)]
        public string VAHColorSerializable
        {
            get { return Serialize.BrushToString(VAHColor); }
            set { VAHColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "VAH Line Width", GroupName = "2. Visuals", Order = 5)]
        public int VAHWidth { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "VAH Line Style", GroupName = "2. Visuals", Order = 6)]
        public DashStyleHelper VAHDashStyle { get; set; }

        [XmlIgnore]
        [Display(Name = "VAL Line Color", GroupName = "2. Visuals", Order = 7)]
        public Brush VALColor { get; set; } = Brushes.Red;

        [Browsable(false)]
        public string VALColorSerializable
        {
            get { return Serialize.BrushToString(VALColor); }
            set { VALColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "VAL Line Width", GroupName = "2. Visuals", Order = 8)]
        public int VALWidth { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "VAL Line Style", GroupName = "2. Visuals", Order = 9)]
        public DashStyleHelper VALDashStyle { get; set; }

        [XmlIgnore]
        [Display(Name = "POC Line Color", GroupName = "2. Visuals", Order = 10)]
        public Brush POCColor { get; set; } = Brushes.Yellow;

        [Browsable(false)]
        public string POCColorSerializable
        {
            get { return Serialize.BrushToString(POCColor); }
            set { POCColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "POC Line Width", GroupName = "2. Visuals", Order = 11)]
        public int POCWidth { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "POC Line Style", GroupName = "2. Visuals", Order = 12)]
        public DashStyleHelper POCDashStyle { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> VAHPlot
        {
            get { return Values[0]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> VALPlot
        {
            get { return Values[1]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> POCPlot
        {
            get { return Values[2]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BiasPlot
        {
            get { return Values[3]; }
        }

        [Browsable(false)]
        [XmlIgnore]
        public double CurrentVAH => currentVAH;

        [Browsable(false)]
        [XmlIgnore]
        public double CurrentVAL => currentVAL;

        [Browsable(false)]
        [XmlIgnore]
        public double CurrentPOC => currentPOC;

        [Browsable(false)]
        [XmlIgnore]
        public double CurrentBias => currentBias;
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenHTFVPV0004[] cacheAlightenHTFVPV0004;
		public AlightenHTFVPV0004 AlightenHTFVPV0004(int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return AlightenHTFVPV0004(Input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}

		public AlightenHTFVPV0004 AlightenHTFVPV0004(ISeries<double> input, int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			if (cacheAlightenHTFVPV0004 != null)
				for (int idx = 0; idx < cacheAlightenHTFVPV0004.Length; idx++)
					if (cacheAlightenHTFVPV0004[idx] != null && cacheAlightenHTFVPV0004[idx].BarsToProcess == barsToProcess && cacheAlightenHTFVPV0004[idx].HTFBarType == hTFBarType && cacheAlightenHTFVPV0004[idx].HTFBarValue == hTFBarValue && cacheAlightenHTFVPV0004[idx].ValueAreaPercentage == valueAreaPercentage && cacheAlightenHTFVPV0004[idx].VAHWidth == vAHWidth && cacheAlightenHTFVPV0004[idx].VAHDashStyle == vAHDashStyle && cacheAlightenHTFVPV0004[idx].VALWidth == vALWidth && cacheAlightenHTFVPV0004[idx].VALDashStyle == vALDashStyle && cacheAlightenHTFVPV0004[idx].POCWidth == pOCWidth && cacheAlightenHTFVPV0004[idx].POCDashStyle == pOCDashStyle && cacheAlightenHTFVPV0004[idx].EqualsInput(input))
						return cacheAlightenHTFVPV0004[idx];
			return CacheIndicator<AlightenHTFVPV0004>(new AlightenHTFVPV0004(){ BarsToProcess = barsToProcess, HTFBarType = hTFBarType, HTFBarValue = hTFBarValue, ValueAreaPercentage = valueAreaPercentage, VAHWidth = vAHWidth, VAHDashStyle = vAHDashStyle, VALWidth = vALWidth, VALDashStyle = vALDashStyle, POCWidth = pOCWidth, POCDashStyle = pOCDashStyle }, input, ref cacheAlightenHTFVPV0004);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenHTFVPV0004 AlightenHTFVPV0004(int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return indicator.AlightenHTFVPV0004(Input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}

		public Indicators.AlightenHTFVPV0004 AlightenHTFVPV0004(ISeries<double> input , int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return indicator.AlightenHTFVPV0004(input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenHTFVPV0004 AlightenHTFVPV0004(int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return indicator.AlightenHTFVPV0004(Input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}

		public Indicators.AlightenHTFVPV0004 AlightenHTFVPV0004(ISeries<double> input , int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return indicator.AlightenHTFVPV0004(input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}
	}
}

#endregion

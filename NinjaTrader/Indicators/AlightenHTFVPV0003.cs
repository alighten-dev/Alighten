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
    public class AlightenHTFVPV0003 : Indicator
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
                Name                                        = "AlightenHTFVPV0003";
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
                        int totalLoaded = BarsArray[0].Count;
                        int multiplier = (HTFBarType == BarsPeriodType.Minute) ? HTFBarValue : 1;
                        int cutoffBar = totalLoaded - (BarsToProcess * multiplier);
                        
                        if (CurrentBars[0] < cutoffBar)
                            return;
                    }
        
                    if (BarsInProgress == 0)
                    {
                        if (isNewHTFBar)
                        {
                            currentHTFStartBarIndex = CurrentBars[0];
                            isNewHTFBar = false;
                        }
    
                        // 1. Unconditionally capture the finalized volume of the fully closed 1-minute bar ONE TIME.
                        if (IsFirstTickOfBar && CurrentBars[0] > 0)
                        {
                            double vol = Volume[1];
                            double barHigh = High[1];
                            double barLow = Low[1];
                            double tickSize = TickSize;
            
                            htfHighestPrice = Math.Max(htfHighestPrice, barHigh);
                            htfLowestPrice = Math.Min(htfLowestPrice, barLow);
            
                            // Distribute finalized volume equally across the bar's price range
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
    
                        // 2. Check if the HTF Bar has safely rolled over to a new hour
                        DateTime currentHtfTime = GetHtfAnchorTime(Times[0][0]);
                        if (_lastSeenHtfTime != Core.Globals.MinDate && currentHtfTime != _lastSeenHtfTime)
                        {
                            // HTF Bar Rollover detected! The PREVIOUS bar's 9:59 1-minute vol was just accumulated.
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
                        // 3. Push stored calculated values (from previous HTF bar) onto the Primary timeframe plots
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
                    } // End if (BarsInProgress == 0)
                }
                catch (Exception ex)
                {
                    Print("[HTFVPV0003] CRASH: " + ex.ToString());
                }
            }
        }

        private void CalculateProfile(DateTime closedHtfBarOpenTime)
        {
            if (tickVolumeAccumulator.Count == 0) 
            {
                return;
            }

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

            if (prices.Count == 0 || pocIdx < 0 || totalVol <= 0) return;

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
                    if (nextUp < vols.Count)
                    {
                        cumVol += vols[nextUp];
                        upperIndex = nextUp;
                        nextUp++;
                    }

                    if (cumVol < targetVol && nextDown >= 0)
                    {
                        cumVol += vols[nextDown];
                        lowerIndex = nextDown;
                        nextDown--;
                    }
                }
            }

            double valPrice = Instrument.MasterInstrument.RoundToTickSize(prices[lowerIndex]);
            double pocPrice = Instrument.MasterInstrument.RoundToTickSize(prices[pocIdx]);
            double vahPrice = Instrument.MasterInstrument.RoundToTickSize(prices[upperIndex]);

            // Cache the calculated values for the next HTF period display
            currentPOC = pocPrice;
            currentVAH = vahPrice;
            currentVAL = valPrice;

            // 3. Determine Bias for the newly starting HTF bar based on the just-closed HTF bar
            double htfClose = Closes[0][1];

            if (currentPOC > currentVAH)
            {
                currentBias = -3; // ReversalShort
            }
            else if (currentPOC < currentVAL)
            {
                currentBias = 3;  // ReversalLong
            }
            else if (htfClose > currentVAH)
            {
                currentBias = 2;  // StrongLong
            }
            else if (htfClose < currentVAL)
            {
                currentBias = -2; // StrongShort
            }
            else if (htfClose > currentPOC && htfClose <= currentVAH)
            {
                currentBias = 1;  // Long
            }
            else if (htfClose < currentPOC && htfClose >= currentVAL)
            {
                currentBias = -1; // Short
            }
            else
            {
                currentBias = 0;  // Neutral / Exact POC Match
            }

            // The 'closedHtfBarOpenTime' is the timestamp of the 60min bar that just finished (e.g. 9:00:00)
            // It projects forward from its CloseTime (10:00:00) until the *next* CloseTime (11:00:00)
            DateTime htfBarCloseTime = GetHtfEndTime(closedHtfBarOpenTime); // e.g., 10:00:00
            
            // Duplicate Profile Prevention Guard
            if (Profiles.Count > 0 && Profiles.Last().StartTime == htfBarCloseTime)
            {
                return; // NT8 evaluated this prematurely, ignore the duplicate!
            }
            
            DateTime projectionEnd = GetHtfEndTime(htfBarCloseTime);        // e.g., 11:00:00

            var profile = new HTFProfile
            {
                StartTime = htfBarCloseTime,
                EndTime = projectionEnd,
                VAH = currentVAH,
                VAL = currentVAL,
                POC = currentPOC,
                Bias = currentBias
            };

            Profiles.Add(profile);
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
		private AlightenHTFVPV0003[] cacheAlightenHTFVPV0003;
		public AlightenHTFVPV0003 AlightenHTFVPV0003(int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return AlightenHTFVPV0003(Input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}

		public AlightenHTFVPV0003 AlightenHTFVPV0003(ISeries<double> input, int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			if (cacheAlightenHTFVPV0003 != null)
				for (int idx = 0; idx < cacheAlightenHTFVPV0003.Length; idx++)
					if (cacheAlightenHTFVPV0003[idx] != null && cacheAlightenHTFVPV0003[idx].BarsToProcess == barsToProcess && cacheAlightenHTFVPV0003[idx].HTFBarType == hTFBarType && cacheAlightenHTFVPV0003[idx].HTFBarValue == hTFBarValue && cacheAlightenHTFVPV0003[idx].ValueAreaPercentage == valueAreaPercentage && cacheAlightenHTFVPV0003[idx].VAHWidth == vAHWidth && cacheAlightenHTFVPV0003[idx].VAHDashStyle == vAHDashStyle && cacheAlightenHTFVPV0003[idx].VALWidth == vALWidth && cacheAlightenHTFVPV0003[idx].VALDashStyle == vALDashStyle && cacheAlightenHTFVPV0003[idx].POCWidth == pOCWidth && cacheAlightenHTFVPV0003[idx].POCDashStyle == pOCDashStyle && cacheAlightenHTFVPV0003[idx].EqualsInput(input))
						return cacheAlightenHTFVPV0003[idx];
			return CacheIndicator<AlightenHTFVPV0003>(new AlightenHTFVPV0003(){ BarsToProcess = barsToProcess, HTFBarType = hTFBarType, HTFBarValue = hTFBarValue, ValueAreaPercentage = valueAreaPercentage, VAHWidth = vAHWidth, VAHDashStyle = vAHDashStyle, VALWidth = vALWidth, VALDashStyle = vALDashStyle, POCWidth = pOCWidth, POCDashStyle = pOCDashStyle }, input, ref cacheAlightenHTFVPV0003);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenHTFVPV0003 AlightenHTFVPV0003(int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return indicator.AlightenHTFVPV0003(Input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}

		public Indicators.AlightenHTFVPV0003 AlightenHTFVPV0003(ISeries<double> input , int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return indicator.AlightenHTFVPV0003(input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenHTFVPV0003 AlightenHTFVPV0003(int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return indicator.AlightenHTFVPV0003(Input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}

		public Indicators.AlightenHTFVPV0003 AlightenHTFVPV0003(ISeries<double> input , int barsToProcess, BarsPeriodType hTFBarType, int hTFBarValue, double valueAreaPercentage, int vAHWidth, DashStyleHelper vAHDashStyle, int vALWidth, DashStyleHelper vALDashStyle, int pOCWidth, DashStyleHelper pOCDashStyle)
		{
			return indicator.AlightenHTFVPV0003(input, barsToProcess, hTFBarType, hTFBarValue, valueAreaPercentage, vAHWidth, vAHDashStyle, vALWidth, vALDashStyle, pOCWidth, pOCDashStyle);
		}
	}
}

#endregion

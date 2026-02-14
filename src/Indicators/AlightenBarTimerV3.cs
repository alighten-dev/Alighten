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
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using NinjaTrader.Gui.Tools;    // <-- for SimpleFont
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenBarTimerV3 : Indicator
    {
        private DateTime                               now               = Core.Globals.Now;
        private bool                                   connected, hasRealtimeData;
        private SessionIterator                        sessionIterator;
        private SimpleFont                             fontSetting;

        #region Properties
        [NinjaScriptProperty]
        [Range(6, 72)]
        [Display(Name="Font Size", GroupName="Parameters", Order=2)]
        public int FontSize { get; set; } = 14;
		
		[NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="Timer Offset Above Bar", GroupName="Parameters", Order=3)]
        public int TimerOffset { get; set; } = 20;
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Timer Color", Order = 4, GroupName = "Parameters")]
		public System.Windows.Media.Brush TimerColor { get; set; } = System.Windows.Media.Brushes.White;
		[Browsable(false)]
		public string TimerColorSerialize
		{
		    get { return Serialize.BrushToString(TimerColor); }
		    set { TimerColor = Serialize.StringToBrush(value); }
		}
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description      = "Bar Timer: shows countdown above each bar.";
                Name             = "AlightenBarTimerV3";
                Calculate        = Calculate.OnEachTick;
                DrawOnPricePanel = true;
                IsChartOnly      = true;
                IsOverlay        = true;
                DisplayInDataBox = false;

            }
            else if (State == State.DataLoaded)
            {
                if (fontSetting == null && ChartControl != null)
                {
                    fontSetting = ChartControl.Properties.LabelFont.Clone() as SimpleFont;
                    fontSetting.Size = FontSize;
                }
            }
            else if (State == State.Realtime)
            {
                // one-time font creation
                
            }
            else if (State == State.Terminated)
            {                
            }
        }

        protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs args)
        {
            if (args.PriceStatus == ConnectionStatus.Connected
                && args.Connection.InstrumentTypes.Contains(Instrument.MasterInstrument.InstrumentType)
                && Bars.BarsType.IsTimeBased
                && Bars.BarsType.IsIntraday)
                connected = true;
            else if (args.PriceStatus == ConnectionStatus.Disconnected)
                connected = false;
        }

        protected override void OnBarUpdate()
        {
            // only run after real‐time begins
            if (State == State.Realtime)
                hasRealtimeData = true;

            if (CurrentBar < 1 || fontSetting == null)
                return;

            // compute text
            string textToShow;
//			bool initialDraw = fontSetting != null && State != State.Realtime;
//            if (initialDraw || (!connected || !SessionIterator.IsInSession(Now, false, true) || !hasRealtimeData))
//			    textToShow = "PAUSED";
//            else
//            {
                var span = Bars.GetTime(Bars.Count - 1).Subtract(Now);
				
               	if (span.Ticks < 0)
    				span = TimeSpan.Zero;
				
				double barLengthMin;
				if (BarsPeriod.BarsPeriodType == BarsPeriodType.Second)
				    barLengthMin = BarsPeriod.Value / 60.0;     // sub-1min bars
				else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
				    barLengthMin = BarsPeriod.Value;            // true minute bars
				else
				    barLengthMin = 61;                          // everything else → treat as >60min
				

				if (barLengthMin > 60)
				    textToShow = $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
				else if (barLengthMin >= 1)
				    textToShow = $"{span.Minutes:00}:{span.Seconds:00}";
				else
				    textToShow = $"{span.Seconds:00}";

//            }

            // clear old label
            RemoveDrawObject("BarTimerText");

            // draw above the current bar
            Draw.Text(
                this,
                "BarTimerText",
                true,                    
                textToShow,
                0,                       
                High[0],      
                TimerOffset,                      
                TimerColor,
                fontSetting,
                System.Windows.TextAlignment.Center,
                Brushes.Transparent,
                Brushes.Transparent,
                0);
        }

        private SessionIterator SessionIterator
        {
            get
            {
                if (sessionIterator == null)
                    sessionIterator = new SessionIterator(Bars);
                return sessionIterator;
            }
        }

        private DateTime Now
        {
            get
            {
                var t = Connection.PlaybackConnection != null
                        ? Connection.PlaybackConnection.Now
                        : Core.Globals.Now;
                return t.Millisecond == 0
                    ? t
                    : Core.Globals.MinDate.AddSeconds(Math.Floor((t - Core.Globals.MinDate).TotalSeconds));
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenBarTimerV3[] cacheAlightenBarTimerV3;
		public AlightenBarTimerV3 AlightenBarTimerV3(int fontSize, int timerOffset, System.Windows.Media.Brush timerColor)
		{
			return AlightenBarTimerV3(Input, fontSize, timerOffset, timerColor);
		}

		public AlightenBarTimerV3 AlightenBarTimerV3(ISeries<double> input, int fontSize, int timerOffset, System.Windows.Media.Brush timerColor)
		{
			if (cacheAlightenBarTimerV3 != null)
				for (int idx = 0; idx < cacheAlightenBarTimerV3.Length; idx++)
					if (cacheAlightenBarTimerV3[idx] != null && cacheAlightenBarTimerV3[idx].FontSize == fontSize && cacheAlightenBarTimerV3[idx].TimerOffset == timerOffset && cacheAlightenBarTimerV3[idx].TimerColor == timerColor && cacheAlightenBarTimerV3[idx].EqualsInput(input))
						return cacheAlightenBarTimerV3[idx];
			return CacheIndicator<AlightenBarTimerV3>(new AlightenBarTimerV3(){ FontSize = fontSize, TimerOffset = timerOffset, TimerColor = timerColor }, input, ref cacheAlightenBarTimerV3);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenBarTimerV3 AlightenBarTimerV3(int fontSize, int timerOffset, System.Windows.Media.Brush timerColor)
		{
			return indicator.AlightenBarTimerV3(Input, fontSize, timerOffset, timerColor);
		}

		public Indicators.AlightenBarTimerV3 AlightenBarTimerV3(ISeries<double> input , int fontSize, int timerOffset, System.Windows.Media.Brush timerColor)
		{
			return indicator.AlightenBarTimerV3(input, fontSize, timerOffset, timerColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenBarTimerV3 AlightenBarTimerV3(int fontSize, int timerOffset, System.Windows.Media.Brush timerColor)
		{
			return indicator.AlightenBarTimerV3(Input, fontSize, timerOffset, timerColor);
		}

		public Indicators.AlightenBarTimerV3 AlightenBarTimerV3(ISeries<double> input , int fontSize, int timerOffset, System.Windows.Media.Brush timerColor)
		{
			return indicator.AlightenBarTimerV3(input, fontSize, timerOffset, timerColor);
		}
	}
}

#endregion

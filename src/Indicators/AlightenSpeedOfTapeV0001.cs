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

public enum AlightenSpeedOfTapeDisplayMode
{
	Speed,
	NetSpeed
}

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
	public class AlightenSpeedOfTapeV0001 : Indicator
	{
		private struct TapePrint
		{
			public DateTime Time;
			public double Vol;
			public bool IsAsk; // true=ask aggressor (buy), false=bid aggressor (sell)
		}

		private Queue<TapePrint> tapeWindow;
		private List<double> thresholdBuffer;
		private double tapeAskSum;
		private double tapeBidSum;

		private double latestTapeSpeedTotal;
		private double latestTapeNetSpeed;
		
		private double prevAsk;
		private double prevBid;
		private bool   prevWasAsk;

        #region Properties

		[NinjaScriptProperty]
		[Display(Name = "Display Mode", GroupName = "Parameters", Order = 0)]
		public AlightenSpeedOfTapeDisplayMode DisplayMode { get; set; } = AlightenSpeedOfTapeDisplayMode.Speed;

		[NinjaScriptProperty]
		[Range(50, 10000)]
		[Display(Name = "Speed Window (ms)", GroupName = "Parameters", Order = 1)]
		public int SpeedWindowMs { get; set; } = 1000;

		[NinjaScriptProperty]
		[Display(Name = "Enable Threshold", GroupName = "Parameters", Order = 1)]
		public bool EnableThreshold { get; set; } = true;

		[NinjaScriptProperty]
		[Range(10, 1000)]
		[Display(Name = "Threshold Lookback", GroupName = "Parameters", Order = 2)]
		public int ThresholdLookback { get; set; } = 60;

		[NinjaScriptProperty]
		[Display(Name = "Outlier Trim StdDev", GroupName = "Parameters", Order = 3)]
		public double OutlierTrimStdDev { get; set; } = 1.0;

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Bar Thickness", GroupName = "Visuals", Order = 5)]
		public int BarThickness { get; set; } = 3;

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Threshold Line Thickness", GroupName = "Visuals", Order = 6)]
		public int ThresholdThickness { get; set; } = 2;

		[XmlIgnore]
		[Display(Name = "Speed Color", GroupName = "Colors", Order = 10)]
		public Brush SpeedColor { get; set; } = Brushes.White;

		[Browsable(false)]
		public string SpeedColorSerializable
		{
			get { return Serialize.BrushToString(SpeedColor); }
			set { SpeedColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Positive Net Color", GroupName = "Colors", Order = 11)]
		public Brush PositiveNetColor { get; set; } = Brushes.Green;

		[Browsable(false)]
		public string PositiveNetColorSerializable
		{
			get { return Serialize.BrushToString(PositiveNetColor); }
			set { PositiveNetColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Negative Net Color", GroupName = "Colors", Order = 12)]
		public Brush NegativeNetColor { get; set; } = Brushes.Red;

		[Browsable(false)]
		public string NegativeNetColorSerializable
		{
			get { return Serialize.BrushToString(NegativeNetColor); }
			set { NegativeNetColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Threshold Color", GroupName = "Colors", Order = 13)]
		public Brush ThresholdColor { get; set; } = Brushes.Cyan;

		[Browsable(false)]
		public string ThresholdColorSerializable
		{
			get { return Serialize.BrushToString(ThresholdColor); }
			set { ThresholdColor = Serialize.StringToBrush(value); }
		}

        #endregion

		#region Plots
		private Series<double> speedOfTape;
		private Series<double> netSpeedOfTape;
		private Series<double> threshold;

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SpeedOfTape
		{
			get { return speedOfTape; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NetSpeedOfTape
		{
			get { return netSpeedOfTape; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Threshold
		{
			get { return threshold; }
		}
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Calculates and displays the Speed of Tape as a histogram.";
				Name										= "AlightenSpeedOfTapeV0001";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= false;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= false;

				AddPlot(new Stroke(Brushes.White, 3), PlotStyle.Bar, "SpeedOfTape");
				AddPlot(new Stroke(Brushes.Green, 3), PlotStyle.Bar, "NetSpeedOfTape");
				AddPlot(new Stroke(Brushes.Cyan, 2), PlotStyle.Line, "Threshold");
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
				tapeWindow = new Queue<TapePrint>(2048);
				thresholdBuffer = new List<double>(ThresholdLookback);
				tapeAskSum = 0;
				tapeBidSum = 0;

				speedOfTape    = Values[0];
				netSpeedOfTape = Values[1];
				threshold      = Values[2];

				// Apply initial styles from parameters
				Plots[0].Width = BarThickness;
				Plots[1].Width = BarThickness;
				Plots[2].Width = ThresholdThickness;
				
				Plots[0].Brush = SpeedColor;
				Plots[2].Brush = ThresholdColor;
			}
		}

		protected override void OnBarUpdate()
		{
			// Gather data from BIP 1 (Tick Series)
			if (BarsInProgress == 1)
			{
				double ask   = BarsArray[1].GetAsk(CurrentBars[1]);
				double bid   = BarsArray[1].GetBid(CurrentBars[1]);
				double close = BarsArray[1].GetClose(CurrentBars[1]);
				double vol   = BarsArray[1].GetVolume(CurrentBars[1]);
				DateTime t   = BarsArray[1].GetTime(CurrentBars[1]);

				// Aggressor logic matching V0005
				bool isAskAggressor = close >= ask || (close > bid && prevWasAsk);
				
				UpdateSpeedOfTape(vol, isAskAggressor, t);

				prevAsk = ask;
				prevBid = bid;
				prevWasAsk = isAskAggressor;
				
				return;
			}

			// Process UI / Plots on BIP 0 (Primary Series)
			if (BarsInProgress == 0)
			{
				if (CurrentBar < 1)
					return;

				// Update threshold buffer ONCE PER BAR for a stable 60-bar lookback
				if (IsFirstTickOfBar)
				{
					if (thresholdBuffer == null) thresholdBuffer = new List<double>(ThresholdLookback);
					if (thresholdBuffer.Count >= ThresholdLookback)
						thresholdBuffer.RemoveAt(0);
					thresholdBuffer.Add(latestTapeSpeedTotal);
				}

				// Threshold calculation
				double threshVal = EnableThreshold ? CalculateThreshold() : 0;

				// Populate all series
				speedOfTape[0]    = latestTapeSpeedTotal;
				netSpeedOfTape[0] = latestTapeNetSpeed;
				
				if (EnableThreshold)
					threshold[0] = threshVal;
				else
					threshold[0] = double.NaN; // Hide the line

				// Handle Visibility and Coloring based on DisplayMode
				if (DisplayMode == AlightenSpeedOfTapeDisplayMode.NetSpeed)
				{
					speedOfTape[0] = 0; 
					PlotBrushes[1][0] = (latestTapeNetSpeed >= 0) ? PositiveNetColor : NegativeNetColor;
				}
				else
				{
					netSpeedOfTape[0] = 0;
					PlotBrushes[0][0] = SpeedColor;
				}
			}
		}

		private void UpdateSpeedOfTape(double vol, bool isAskAggressor, DateTime t)
		{
			if (SpeedWindowMs <= 0)
				return;

			if (tapeWindow == null) tapeWindow = new Queue<TapePrint>(2048);

			tapeWindow.Enqueue(new TapePrint { Time = t, Vol = vol, IsAsk = isAskAggressor });

			if (isAskAggressor) tapeAskSum += vol;
			else                tapeBidSum += vol;

			DateTime cutoff = t.AddMilliseconds(-SpeedWindowMs);

			while (tapeWindow.Count > 0 && tapeWindow.Peek().Time < cutoff)
			{
				var p = tapeWindow.Dequeue();
				if (p.IsAsk) tapeAskSum -= p.Vol;
				else         tapeBidSum -= p.Vol;
			}

			double windowSeconds = Math.Max(0.001, SpeedWindowMs / 1000.0);
			latestTapeSpeedTotal = (tapeAskSum + tapeBidSum) / windowSeconds;
			latestTapeNetSpeed = (tapeAskSum - tapeBidSum) / windowSeconds;
		}

		private double CalculateThreshold()
		{
			if (thresholdBuffer == null || thresholdBuffer.Count < 5)
				return 0;

			// Pass 1: Mean and StdDev of current lookback
			double m1 = thresholdBuffer.Average();
			double sumSq1 = thresholdBuffer.Select(v => Math.Pow(v - m1, 2)).Sum();
			double s1 = Math.Sqrt(sumSq1 / thresholdBuffer.Count);

			// Inverted Logic: 
			// Higher OutlierTrimStdDev = MORE trimming (smaller ceiling) = flatter line.
			// Lower OutlierTrimStdDev  = LESS trimming (larger ceiling)  = noisier line.
			// Range for multiplier is 1.0 (noise) to 6.0 (flat).
			double internalMultiplier = Math.Max(0.1, 6.0 - OutlierTrimStdDev);
			double upperLimit = m1 + (internalMultiplier * s1);

			// Pass 2: Final Average on non-extreme values
			var filtered = thresholdBuffer.Where(x => x <= upperLimit).ToList();

			if (filtered.Count == 0) return m1;

			return filtered.Average();
		}
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenSpeedOfTapeV0001[] cacheAlightenSpeedOfTapeV0001;
		public AlightenSpeedOfTapeV0001 AlightenSpeedOfTapeV0001(AlightenSpeedOfTapeDisplayMode displayMode, int speedWindowMs, bool enableThreshold, int thresholdLookback, double outlierTrimStdDev, int barThickness, int thresholdThickness)
		{
			return AlightenSpeedOfTapeV0001(Input, displayMode, speedWindowMs, enableThreshold, thresholdLookback, outlierTrimStdDev, barThickness, thresholdThickness);
		}

		public AlightenSpeedOfTapeV0001 AlightenSpeedOfTapeV0001(ISeries<double> input, AlightenSpeedOfTapeDisplayMode displayMode, int speedWindowMs, bool enableThreshold, int thresholdLookback, double outlierTrimStdDev, int barThickness, int thresholdThickness)
		{
			if (cacheAlightenSpeedOfTapeV0001 != null)
				for (int idx = 0; idx < cacheAlightenSpeedOfTapeV0001.Length; idx++)
					if (cacheAlightenSpeedOfTapeV0001[idx] != null && cacheAlightenSpeedOfTapeV0001[idx].DisplayMode == displayMode && cacheAlightenSpeedOfTapeV0001[idx].SpeedWindowMs == speedWindowMs && cacheAlightenSpeedOfTapeV0001[idx].EnableThreshold == enableThreshold && cacheAlightenSpeedOfTapeV0001[idx].ThresholdLookback == thresholdLookback && cacheAlightenSpeedOfTapeV0001[idx].OutlierTrimStdDev == outlierTrimStdDev && cacheAlightenSpeedOfTapeV0001[idx].BarThickness == barThickness && cacheAlightenSpeedOfTapeV0001[idx].ThresholdThickness == thresholdThickness && cacheAlightenSpeedOfTapeV0001[idx].EqualsInput(input))
						return cacheAlightenSpeedOfTapeV0001[idx];
			return CacheIndicator<AlightenSpeedOfTapeV0001>(new AlightenSpeedOfTapeV0001(){ DisplayMode = displayMode, SpeedWindowMs = speedWindowMs, EnableThreshold = enableThreshold, ThresholdLookback = thresholdLookback, OutlierTrimStdDev = outlierTrimStdDev, BarThickness = barThickness, ThresholdThickness = thresholdThickness }, input, ref cacheAlightenSpeedOfTapeV0001);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenSpeedOfTapeV0001 AlightenSpeedOfTapeV0001(AlightenSpeedOfTapeDisplayMode displayMode, int speedWindowMs, bool enableThreshold, int thresholdLookback, double outlierTrimStdDev, int barThickness, int thresholdThickness)
		{
			return indicator.AlightenSpeedOfTapeV0001(Input, displayMode, speedWindowMs, enableThreshold, thresholdLookback, outlierTrimStdDev, barThickness, thresholdThickness);
		}

		public Indicators.AlightenSpeedOfTapeV0001 AlightenSpeedOfTapeV0001(ISeries<double> input , AlightenSpeedOfTapeDisplayMode displayMode, int speedWindowMs, bool enableThreshold, int thresholdLookback, double outlierTrimStdDev, int barThickness, int thresholdThickness)
		{
			return indicator.AlightenSpeedOfTapeV0001(input, displayMode, speedWindowMs, enableThreshold, thresholdLookback, outlierTrimStdDev, barThickness, thresholdThickness);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenSpeedOfTapeV0001 AlightenSpeedOfTapeV0001(AlightenSpeedOfTapeDisplayMode displayMode, int speedWindowMs, bool enableThreshold, int thresholdLookback, double outlierTrimStdDev, int barThickness, int thresholdThickness)
		{
			return indicator.AlightenSpeedOfTapeV0001(Input, displayMode, speedWindowMs, enableThreshold, thresholdLookback, outlierTrimStdDev, barThickness, thresholdThickness);
		}

		public Indicators.AlightenSpeedOfTapeV0001 AlightenSpeedOfTapeV0001(ISeries<double> input , AlightenSpeedOfTapeDisplayMode displayMode, int speedWindowMs, bool enableThreshold, int thresholdLookback, double outlierTrimStdDev, int barThickness, int thresholdThickness)
		{
			return indicator.AlightenSpeedOfTapeV0001(input, displayMode, speedWindowMs, enableThreshold, thresholdLookback, outlierTrimStdDev, barThickness, thresholdThickness);
		}
	}
}

#endregion

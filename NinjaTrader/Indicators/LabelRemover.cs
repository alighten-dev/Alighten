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

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
	public class LabelRemover : Indicator
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Removes labels from all indicators on chart.";
				Name										= "LabelRemover";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
			}
			else if (State == State.DataLoaded)
			{
				if (ChartControl == null) return;
				
				foreach (NinjaTrader.Gui.NinjaScript.IndicatorRenderBase indicator in ChartControl.Indicators)
				{
					indicator.Name = "";
				}
			}
		}

		protected override void OnBarUpdate()
		{
			//Add your custom indicator logic here.
		}
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private LabelRemover[] cacheLabelRemover;
		public LabelRemover LabelRemover()
		{
			return LabelRemover(Input);
		}

		public LabelRemover LabelRemover(ISeries<double> input)
		{
			if (cacheLabelRemover != null)
				for (int idx = 0; idx < cacheLabelRemover.Length; idx++)
					if (cacheLabelRemover[idx] != null &&  cacheLabelRemover[idx].EqualsInput(input))
						return cacheLabelRemover[idx];
			return CacheIndicator<LabelRemover>(new LabelRemover(), input, ref cacheLabelRemover);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.LabelRemover LabelRemover()
		{
			return indicator.LabelRemover(Input);
		}

		public Indicators.LabelRemover LabelRemover(ISeries<double> input )
		{
			return indicator.LabelRemover(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.LabelRemover LabelRemover()
		{
			return indicator.LabelRemover(Input);
		}

		public Indicators.LabelRemover LabelRemover(ISeries<double> input )
		{
			return indicator.LabelRemover(input);
		}
	}
}

#endregion

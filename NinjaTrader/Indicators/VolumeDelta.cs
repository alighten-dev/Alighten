// #############################################################
// #														   #
// #                     Volume Delta by Gill                  #
// #						11.05.2019						   #
// #                 										   #
// #														   #
// #        Thanks and comments are highly appreciated         #
// #        		gsbi619znw@pomail.net				       #
// #														   #
// #############################################################

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
using System.Windows.Controls;
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
using System.Windows.Controls;
using NinjaTrader.Gui.Chart;

#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{

	public class VolumeDelta : Indicator
	{
		private double		buys 	= 1;
		private double 		sells 	= 1;
		private double		cdHigh 	= 1;
		private double 		cdLow 	= 1;
		private double		cdOpen 	= 1;
		private double 		cdClose	= 1;
		private int										barPaintWidth;
		private Dictionary<string, DXMediaMap>			dxmBrushes;
		private SharpDX.RectangleF						reuseRect;
		private SharpDX.Vector2							reuseVector1, reuseVector2;
		private double									tmpMax, tmpMin, tmpPlotVal;
		private int										x, y1, y2, y3, y4;
		private Series<Double> delta_open;
		private Series<Double> delta_close;
		private Series<Double> delta_high;
		private Series<Double> delta_low;		
		

		
		private bool	isReset;

		private int 	lastBar;
		private bool 	lastInTransition;
		
		private Brush	divergeCandleup   = Brushes.Purple;  // Color body for Divergence Candle
		private Brush	divergeCandledown   = Brushes.Pink;  // Color body for Divergence Candle
		
		
		
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Volume Delta by Gill";
				Name										= "Volume Delta";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= false;
	
				
				MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
				
				dxmBrushes	= new Dictionary<string, DXMediaMap>();
				foreach (string brushName in new string[] { "barColorDown", "barColorUp", "shadowColor" })
					dxmBrushes.Add(brushName, new DXMediaMap());
				BarColorDown								= Brushes.Red;
				BarColorUp									= Brushes.LimeGreen;
				ShadowColor									= Brushes.Black;
				ShadowWidth									= 1;
				int MinSize 								= 0;
				CumulativeDelta								= false;
				ShowDivs 									= false;
				
				AddPlot(new Stroke(Brushes.Transparent),PlotStyle.PriceBox,"DeltaOpen");
				AddPlot(new Stroke(Brushes.Transparent),PlotStyle.PriceBox,"DeltaHigh");
				AddPlot(new Stroke(Brushes.Transparent),PlotStyle.PriceBox,"DeltaLow");
				AddPlot(new Stroke(Brushes.Orange),PlotStyle.PriceBox,"DeltaClose");
				
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			
			else if (State == State.DataLoaded)
			{
				delta_open = new Series<double>(this);
				delta_close = new Series<double>(this);
				delta_high = new Series<double>(this);
				delta_low = new Series<double>(this);
			}		
		}
		
		protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < 5 || CurrentBars[1] < 5)
				return;
			if (BarsInProgress == 0)
			{
				
				int indexOffset = BarsArray[1].Count - 1 - CurrentBars[1];
				
				
				if (IsFirstTickOfBar && Calculate != Calculate.OnBarClose && (State == State.Realtime || BarsArray[0].IsTickReplay))
				{
					
					if (CurrentBars[0] > 0)
						SetValues(1);					
					
					if (BarsArray[0].IsTickReplay || State == State.Realtime && indexOffset == 0)
						ResetValues(false,cdClose);
				}
				
				
				SetValues(0);
				
			
				if (Calculate == Calculate.OnBarClose || (lastBar != CurrentBars[0] && (State == State.Historical || State == State.Realtime && indexOffset > 0)))
					ResetValues(false,cdClose);
				
				lastBar = CurrentBars[0];
				if (CumulativeDelta)
				{
					if (delta_close[0] > delta_close[1]) PlotBrushes[3][0] = (Brush) Brushes.LimeGreen;
					else if (delta_close[0] < delta_close[1]) PlotBrushes[3][0] = (Brush) Brushes.Red;
					else PlotBrushes[3][0] = (Brush) Brushes.Orange;
				}
				else
				{
					if (delta_close[0] > 0) PlotBrushes[3][0] = (Brush) Brushes.LimeGreen;
					else if (delta_close[0] < 0) PlotBrushes[3][0] = (Brush) Brushes.Red;
					else PlotBrushes[3][0] = (Brush) Brushes.Orange;
				}
				
				if (IsFirstTickOfBar && ShowDivs)
				{
				if(CumulativeDelta && delta_low[1] >= delta_low[2] && Low[1] <= Low[2] && Low[1] <= Low[3] && Stochastics(3, 14, 3).K[1] <= 20)	
				{
				
					Draw.TriangleUp(this,CurrentBar.ToString(), true, 1, Low[1] - 2*TickSize, divergeCandleup);
				}		
	
				if(CumulativeDelta && delta_high[1] <= delta_high[2] && High[1] >= High[2] && High[1] >= High[3] && Stochastics(3, 14, 3).K[1] >= 80)
	
				{
				
					Draw.TriangleDown(this,CurrentBar.ToString(), true, 1, High[1] + 2*TickSize, divergeCandledown);
				}
				}
				
			}
			else if (BarsInProgress == 1)
			{
			
				if (BarsArray[1].IsFirstBarOfSession)
					ResetValues(true,cdClose);
			
				CalculateValues(false);
			}
		}
		
				
		private void CalculateValues(bool forceCurrentBar)
		{
			
			int 	indexOffset 	= BarsArray[1].Count - 1 - CurrentBars[1];
			bool 	inTransition 	= State == State.Realtime && indexOffset > 1;
			if (!inTransition && lastInTransition && !forceCurrentBar && Calculate == Calculate.OnBarClose)
				CalculateValues(true);
			
			bool 	useCurrentBar 	= State == State.Historical || inTransition || Calculate != Calculate.OnBarClose || forceCurrentBar;
			int 	whatBar 		= useCurrentBar ? CurrentBars[1] : Math.Min(CurrentBars[1] + 1, BarsArray[1].Count - 1);
		
			double 	volume 			= BarsArray[1].GetVolume(whatBar);
			double	price			= BarsArray[1].GetClose(whatBar);
			
			if (price >= BarsArray[1].GetAsk(whatBar) && volume>=MinSize)
				buys += volume;	
			else if (price <= BarsArray[1].GetBid(whatBar) && volume>=MinSize)
				sells += volume;
			
			cdClose = buys - sells;
	
			if (cdClose > cdHigh)
					cdHigh = cdClose;
	
			if (cdClose < cdLow)
					cdLow = cdClose;
	
			
			lastInTransition 	= inTransition;
		}
		
		private void SetValues(int barsAgo)
		{
		
		
			
			Values[0][barsAgo] = delta_open[barsAgo] = cdOpen;
			Values[1][barsAgo] = delta_high[barsAgo] = cdHigh;
			Values[2][barsAgo] = delta_low[barsAgo] = cdLow;
			Values[3][barsAgo] = delta_close[barsAgo] = cdClose;
			
	
		}
		
		private void ResetValues(bool isNewSession, double openlevel)
		{
		
		
			if (CumulativeDelta)
				{
				cdOpen = cdClose = cdHigh = cdLow = openlevel;
				}
			if (!CumulativeDelta)
				{
				cdOpen = cdClose = cdHigh = cdLow = buys = sells = 0;
				}
			if (isNewSession)
			{
				cdOpen = cdClose = cdHigh = cdLow = buys = sells = 0;
			}
			isReset = true;
		}
		
		public override string DisplayName
		{
		  get { return "Volume Delta by Gill"; }
		}
		
		#region Miscellaneous
	
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);

			barPaintWidth = Math.Max(3, 1 + 2 * ((int)ChartBars.Properties.ChartStyle.BarWidth - 1) + 2 * ShadowWidth);
	

            for (int idx = ChartBars.FromIndex; idx <= ChartBars.ToIndex; idx++)
            {
                if (idx - Displacement < 0 || idx - Displacement >= BarsArray[0].Count || (idx - Displacement < BarsRequiredToPlot))
                    continue;

                x					= ChartControl.GetXByBarIndex(ChartBars, idx);
                y1					= chartScale.GetYByValue(delta_open.GetValueAt(idx));
                y2					= chartScale.GetYByValue(delta_high.GetValueAt(idx));
                y3					= chartScale.GetYByValue(delta_low.GetValueAt(idx));
                y4					= chartScale.GetYByValue(delta_close.GetValueAt(idx));

				reuseVector1.X		= x;
				reuseVector1.Y		= y2;
				reuseVector2.X		= x;
				reuseVector2.Y		= y3;

				RenderTarget.DrawLine(reuseVector1, reuseVector2, dxmBrushes["shadowColor"].DxBrush);

				if (y4 == y1)
				{
					reuseVector1.X	= (x - barPaintWidth / 2);
					reuseVector1.Y	= y1;
					reuseVector2.X	= (x + barPaintWidth / 2);
					reuseVector2.Y	= y1;

					RenderTarget.DrawLine(reuseVector1, reuseVector2, dxmBrushes["shadowColor"].DxBrush);
				}
				else
				{
					if (y4 > y1)
					{
						UpdateRect(ref reuseRect, (x - barPaintWidth / 2), y1, barPaintWidth, (y4 - y1));
						RenderTarget.FillRectangle(reuseRect, dxmBrushes["barColorDown"].DxBrush);
					}
					else
					{
						UpdateRect(ref reuseRect, (x - barPaintWidth / 2), y4, barPaintWidth, (y1 - y4));
						RenderTarget.FillRectangle(reuseRect, dxmBrushes["barColorUp"].DxBrush);
					}

					UpdateRect(ref reuseRect, ((x - barPaintWidth / 2) + (ShadowWidth / 2)), Math.Min(y4, y1), (barPaintWidth - ShadowWidth + 2), Math.Abs(y4 - y1));
					RenderTarget.DrawRectangle(reuseRect, dxmBrushes["shadowColor"].DxBrush);
				}
            }
		}
		public override void OnRenderTargetChanged()
		{		
			try
			{
				foreach (KeyValuePair<string, DXMediaMap> item in dxmBrushes)
				{
					if (item.Value.DxBrush != null)
						item.Value.DxBrush.Dispose();

					if (RenderTarget != null)
						item.Value.DxBrush = item.Value.MediaBrush.ToDxBrush(RenderTarget);					
				}
			}
			catch (Exception exception)
			{
			}
		}

		private void UpdateRect(ref SharpDX.RectangleF updateRectangle, float x, float y, float width, float height)
		{
			updateRectangle.X		= x;
			updateRectangle.Y		= y;
			updateRectangle.Width	= width;
			updateRectangle.Height	= height;
		}

		private void UpdateRect(ref SharpDX.RectangleF rectangle, int x, int y, int width, int height)
		{
			UpdateRect(ref rectangle, (float)x, (float)y, (float)width, (float)height);
		}
		#endregion
		
		#region Properties

		[Browsable(false)]
		public class DXMediaMap
		{
			public SharpDX.Direct2D1.Brush		DxBrush;
			public System.Windows.Media.Brush	MediaBrush;
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BarColorDown", Order=4, GroupName= "Optics")]
		public Brush BarColorDown
		{
			get { return dxmBrushes["barColorDown"].MediaBrush; }
			set { dxmBrushes["barColorDown"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string BarColorDownSerializable
		{
			get { return Serialize.BrushToString(BarColorDown); }
			set { BarColorDown = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="BarColorUp", Order=5, GroupName= "Optics")]
		public Brush BarColorUp
		{
			get { return dxmBrushes["barColorUp"].MediaBrush; }
			set { dxmBrushes["barColorUp"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string BarColorUpSerializable
		{
			get { return Serialize.BrushToString(BarColorUp); }
			set { BarColorUp = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name="ShadowColor", Order=6, GroupName="Optics")]
		public Brush ShadowColor
		{
			get { return dxmBrushes["shadowColor"].MediaBrush; }
			set { dxmBrushes["shadowColor"].MediaBrush = value; }
		}

		[Browsable(false)]
		public string ShadowColorSerializable
		{
			get { return Serialize.BrushToString(ShadowColor); }
			set { ShadowColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="ShadowWidth", Order=7, GroupName= "Optics")]
		public int ShadowWidth
		{ get; set; }
		

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaOpen
		{
			get { return Values[0]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaHigh
		{
			get { return Values[1]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaLow
		{
			get { return Values[2]; }
		}
		
				
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> DeltaClose
		{
			get { return Values[3]; }
		}
		
		[NinjaScriptProperty]
		[Display(Name="Cumulative Delta", Description="Enable cumulative delta", Order=1, GroupName="Parameters")]
		public bool CumulativeDelta
		{ get; set; }
		
		[Range(0, int.MaxValue)]
		[NinjaScriptProperty]
		[Display(Name="Size Filter", Description="Size filtering", Order=2, GroupName="Parameters")]
		public int MinSize
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show Delta Divergences", Description="Enable to show volume cumulative delta divergences [Cumulative Delta must be enabled]", Order=3, GroupName="Parameters")]
		public bool ShowDivs
		{ get; set; }
		
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasOpen
        {
            get { return delta_open; }
        }	
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasHigh
        {
            get { return delta_high; }
        }	
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasClose
        {
            get { return delta_close; }
        }	
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<double> DeltasLow
        {
            get { return delta_low; }
        }	
		
		#endregion
		
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private VolumeDelta[] cacheVolumeDelta;
		public VolumeDelta VolumeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, bool cumulativeDelta, int minSize, bool showDivs)
		{
			return VolumeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, cumulativeDelta, minSize, showDivs);
		}

		public VolumeDelta VolumeDelta(ISeries<double> input, Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, bool cumulativeDelta, int minSize, bool showDivs)
		{
			if (cacheVolumeDelta != null)
				for (int idx = 0; idx < cacheVolumeDelta.Length; idx++)
					if (cacheVolumeDelta[idx] != null && cacheVolumeDelta[idx].BarColorDown == barColorDown && cacheVolumeDelta[idx].BarColorUp == barColorUp && cacheVolumeDelta[idx].ShadowColor == shadowColor && cacheVolumeDelta[idx].ShadowWidth == shadowWidth && cacheVolumeDelta[idx].CumulativeDelta == cumulativeDelta && cacheVolumeDelta[idx].MinSize == minSize && cacheVolumeDelta[idx].ShowDivs == showDivs && cacheVolumeDelta[idx].EqualsInput(input))
						return cacheVolumeDelta[idx];
			return CacheIndicator<VolumeDelta>(new VolumeDelta(){ BarColorDown = barColorDown, BarColorUp = barColorUp, ShadowColor = shadowColor, ShadowWidth = shadowWidth, CumulativeDelta = cumulativeDelta, MinSize = minSize, ShowDivs = showDivs }, input, ref cacheVolumeDelta);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.VolumeDelta VolumeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, bool cumulativeDelta, int minSize, bool showDivs)
		{
			return indicator.VolumeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, cumulativeDelta, minSize, showDivs);
		}

		public Indicators.VolumeDelta VolumeDelta(ISeries<double> input , Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, bool cumulativeDelta, int minSize, bool showDivs)
		{
			return indicator.VolumeDelta(input, barColorDown, barColorUp, shadowColor, shadowWidth, cumulativeDelta, minSize, showDivs);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.VolumeDelta VolumeDelta(Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, bool cumulativeDelta, int minSize, bool showDivs)
		{
			return indicator.VolumeDelta(Input, barColorDown, barColorUp, shadowColor, shadowWidth, cumulativeDelta, minSize, showDivs);
		}

		public Indicators.VolumeDelta VolumeDelta(ISeries<double> input , Brush barColorDown, Brush barColorUp, Brush shadowColor, int shadowWidth, bool cumulativeDelta, int minSize, bool showDivs)
		{
			return indicator.VolumeDelta(input, barColorDown, barColorUp, shadowColor, shadowWidth, cumulativeDelta, minSize, showDivs);
		}
	}
}

#endregion

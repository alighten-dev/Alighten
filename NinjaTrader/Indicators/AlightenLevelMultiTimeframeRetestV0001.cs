#region Using declarations
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Provider;

//using SharpDX;
//using SharpDX.Direct2D1;
//using SharpDX.DirectWrite;
#endregion


namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenLevelMultiTimeframeRetestV0001 : Indicator
    {
		
		#region Class Variables

        // HTF pivots (BIP 0 only)
        private List<DateTime> pivotTimes;
        private List<double>   pivotPrices;
        private List<bool>     pivotIsHigh;
        private List<double>   pivotGuidePrices;

        // Important levels derived from pivots (BIP 0 only)
        private List<double> importantLevels;

        private double lastPivotPrice = double.NaN;
        private int    lastPivotType  = 0; // +1 high, -1 low

        private DateTime tsu1_time = DateTime.MinValue;
        private double   tsu1_price = double.NaN;
        private DateTime tsu2_time = DateTime.MinValue;
        private double   tsu2_price = double.NaN;

        private DateTime tsd1_time = DateTime.MinValue;
        private double   tsd1_price = double.NaN;
        private DateTime tsd2_time = DateTime.MinValue;
        private double   tsd2_price = double.NaN;

        // ---- V00029-style “export pivot” DTO pattern (serialization-safe) ----
        private class ExportPivot
        {
            public double   Level;
            public DateTime Time;
            public string   Status;
            public bool     IsHigh;

            // Keep this if you want to mirror your guide/bodies later:
            public double   GuideLevel;

            // Explicit parameterless ctor for NinjaTrader serialization
            public ExportPivot()
            {
            }
        }
        private List<ExportPivot> exportPivots = new List<ExportPivot>();

        // ---- Lookback window index (no list pruning / no RemoveAt churn) ----
        private int lookbackStartPivotIdx = 0;

        // ---- Debug draw internals ----
        private string instanceId;
        private HashSet<string> zzTags;
		
		private DateTime lastTickTime;
		
		
        #endregion
		
        #region Properties
		

        [NinjaScriptProperty]
		[Range(0, 500000)]
		[Display(Name="Bars To Process", GroupName="ZigZag", Order=53)]
		public int BarsToProcess { get; set; } = 200; // 0 = process all
		
		

        // ---- Debug (chart-only) ZigZag drawing ----

        [NinjaScriptProperty]
        [Display(Name="Show ZigZag (Chart Only)", GroupName="ZigZag", Order=1)]
        public bool ShowZigZag { get; set; } = true;

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="ZigZag Color", GroupName="ZigZag", Order=2)]
        public System.Windows.Media.Brush ZigZagColor { get; set; } = Brushes.Yellow;

        [Browsable(false)]
        public string ZigZagColorSerialize
        {
            get => Serialize.BrushToString(ZigZagColor);
            set => ZigZagColor = Serialize.StringToBrush(value);
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="ZigZag Width", GroupName="ZigZag", Order=3)]
        public int ZigZagWidth { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name="ZigZag Style", GroupName="ZigZag", Order=4)]
        public DashStyleHelper ZigZagStyle { get; set; } = DashStyleHelper.Solid;

		[NinjaScriptProperty]
		[Display(Name="Show Trend Start Lines", GroupName="Trend Start", Order=5)]
		public bool ShowTrendStartLines { get; set; } = true;

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="TSD Line Color (1 & 2)", GroupName="Trend Start", Order=6)]
		public System.Windows.Media.Brush TSDLineColor { get; set; } = Brushes.Magenta;

		[Browsable(false)]
		public string TSDLineColorSerialize
		{
			get => Serialize.BrushToString(TSDLineColor);
			set => TSDLineColor = Serialize.StringToBrush(value);
		}

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="TSD Line Width", GroupName="Trend Start", Order=7)]
		public int TSDLineWidth { get; set; } = 3;

		[NinjaScriptProperty]
		[Display(Name="TSD1 Line Style", GroupName="Trend Start", Order=8)]
		public DashStyleHelper TSD1LineStyle { get; set; } = DashStyleHelper.Solid;

		[NinjaScriptProperty]
		[Display(Name="TSD2 Line Style", GroupName="Trend Start", Order=9)]
		public DashStyleHelper TSD2LineStyle { get; set; } = DashStyleHelper.Dash;

		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name="TSU Line Color (1 & 2)", GroupName="Trend Start", Order=10)]
		public System.Windows.Media.Brush TSULineColor { get; set; } = Brushes.Cyan;

		[Browsable(false)]
		public string TSULineColorSerialize
		{
			get => Serialize.BrushToString(TSULineColor);
			set => TSULineColor = Serialize.StringToBrush(value);
		}

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name="TSU Line Width", GroupName="Trend Start", Order=11)]
		public int TSULineWidth { get; set; } = 3;

		[NinjaScriptProperty]
		[Display(Name="TSU1 Line Style", GroupName="Trend Start", Order=12)]
		public DashStyleHelper TSU1LineStyle { get; set; } = DashStyleHelper.Solid;

		[NinjaScriptProperty]
		[Display(Name="TSU2 Line Style", GroupName="Trend Start", Order=13)]
		public DashStyleHelper TSU2LineStyle { get; set; } = DashStyleHelper.Dash;

		[NinjaScriptProperty]
		[Display(Name="Show Labels", GroupName="Trend Start Labels", Order=14)]
		public bool ShowLabels { get; set; } = false;

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Label Font Size", GroupName="Trend Start Labels", Order=15)]
		public int LabelFontSize { get; set; } = 11;

		[NinjaScriptProperty]
		[Display(Name="Label Offset (Ticks)", GroupName="Trend Start Labels", Order=16)]
		public int LabelOffsetTicks { get; set; } = 5;

		
		#endregion
		
		#region Plots

		private Series<double> tsu1PriceSeries;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> TSU1PriceSeries => tsu1PriceSeries;

		private Series<double> tsu1TimeSeries;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> TSU1TimeSeries => tsu1TimeSeries;

		private Series<double> tsu2PriceSeries;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> TSU2PriceSeries => tsu2PriceSeries;

		private Series<double> tsu2TimeSeries;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> TSU2TimeSeries => tsu2TimeSeries;

		private Series<double> tsd1PriceSeries;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> TSD1PriceSeries => tsd1PriceSeries;

		private Series<double> tsd1TimeSeries;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> TSD1TimeSeries => tsd1TimeSeries;

		private Series<double> tsd2PriceSeries;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> TSD2PriceSeries => tsd2PriceSeries;

		private Series<double> tsd2TimeSeries;
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> TSD2TimeSeries => tsd2TimeSeries;

		#endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "AlightenLevelMultiTimeframeRetestV0001";
                Description = "Market Analyzer core: HTF ZigZag/levels";
                Calculate   = Calculate.OnEachTick;

                // For MA, overlay doesn’t matter. For chart debugging, overlay helps.
                IsOverlay               		= true;
                DrawOnPricePanel        		= true;

                DisplayInDataBox        		= true;
                PaintPriceMarkers       		= false;
                IsSuspendedWhileInactive 		= false;
				ShowTransparentPlotsInDataBox 	= true;
            
                
            }
            else if (State == State.Configure)
            {
				//AddDataSeries(BarsPeriodType.Tick, 1);
                instanceId = $"ZZ_MA_{Guid.NewGuid()}";

				AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TSU1_Price");
				AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TSU1_Time");
				AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TSU2_Price");
				AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TSU2_Time");

				AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TSD1_Price");
				AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TSD1_Time");
				AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TSD2_Price");
				AddPlot(new Stroke(Brushes.Transparent), PlotStyle.Line, "TSD2_Time");
            }
            else if (State == State.DataLoaded)
            {
				tsu1PriceSeries = Values[0];
				tsu1TimeSeries  = Values[1];
				tsu2PriceSeries = Values[2];
				tsu2TimeSeries  = Values[3];

				tsd1PriceSeries = Values[4];
				tsd1TimeSeries  = Values[5];
				tsd2PriceSeries = Values[6];
				tsd2TimeSeries  = Values[7];
                pivotTimes       				= new List<DateTime>(Math.Min(400, BarsToProcess));
                pivotPrices      				= new List<double>(Math.Min(400, BarsToProcess));
                pivotIsHigh      				= new List<bool>(Math.Min(400, BarsToProcess));
                pivotGuidePrices 				= new List<double>(Math.Min(400, BarsToProcess));

                exportPivots     				= new List<ExportPivot>(Math.Min(400, BarsToProcess));

                zzTags 							= new HashSet<string>();
				
				lastTickTime 					= DateTime.MinValue;
				


            }
            else if (State == State.Terminated)
            {
                if (zzTags != null)
                {
                    foreach (var t in zzTags)
                        RemoveDrawObject(t);
                    zzTags.Clear();
                }
            }
        }

        protected override void OnBarUpdate()
		{
            if (BarsInProgress == 1)
            {
                lastTickTime = Times[1][0];
                return;
            }

		    try
		    {
		        // ---- BIP 0 only (this indicator is single-series right now) ----
		        if (BarsInProgress != 0)
		            return;		   
		
		        // Ultra-fast early-exit windowing — keep this for heavy work only
		        bool allowHeavy = IsFirstTickOfBar;
		        if (allowHeavy && BarsToProcess > 0)
		        {
		            int totalLoaded = BarsArray[0].Count;
		            int cutoffBar   = totalLoaded - BarsToProcess;
		            if (CurrentBars[0] < cutoffBar)
		                return;
		        }
		
		        if (CurrentBar < 1)
		        {
		            
		        }
		
		        // -----------------------------
		        // 1) BAR-CLOSE semantics (keep existing behavior)
		        //    Only do the heavy rebuild on first tick of bar.
		        // -----------------------------
		        if (allowHeavy)
		        {
		            if (CurrentBar < 3)
		                return;
		
		            ProcessClosedBarPivot_BIP0();  // uses [1]

		        }
		
                // Update plots and lines for the current bar
                Values[0][0] = tsu1_price;
                Values[1][0] = tsu1_time != DateTime.MinValue ? tsu1_time.Ticks : double.NaN;

                bool showTsu2 = (tsu1_price < tsu2_price);
                Values[2][0] = showTsu2 ? tsu2_price : double.NaN;
                Values[3][0] = (showTsu2 && tsu2_time != DateTime.MinValue) ? tsu2_time.Ticks : double.NaN;

                Values[4][0] = tsd1_price;
                Values[5][0] = tsd1_time != DateTime.MinValue ? tsd1_time.Ticks : double.NaN;

                bool showTsd2 = (tsd1_price > tsd2_price);
                Values[6][0] = showTsd2 ? tsd2_price : double.NaN;
                Values[7][0] = (showTsd2 && tsd2_time != DateTime.MinValue) ? tsd2_time.Ticks : double.NaN;

                DrawOrUpdateTrendStartLines();

		    }
		    catch (Exception ex)
		    {
		        Print(string.Format("[PA EX][OnBarUpdate] t={0:yyyy-MM-dd HH:mm:ss} BIP={1} CB0={2} CurrentBar={3} Msg={4}\nStack={5}",
		            Times[0][0], BarsInProgress, (CurrentBars != null && CurrentBars.Length > 0) ? CurrentBars[0] : -1,
		            CurrentBar, ex.Message, ex.StackTrace));
		    }
		}



        #region ZigZag (HTF on BIP 0)
		
		private void ProcessClosedBarPivot_BIP0()
		{
		    // Called only when IsFirstTickOfBar == true on BIP 0,
		    // so bar [1] is the bar that just closed.
		
		    if (CurrentBar < 3)
		        return;
		
		    // Just-closed bar = [1], prior closed bar = [2]
		    double h0 = High[1];
		    double h1 = High[2];
		    double l0 = Low[1];
		    double l1 = Low[2];
		    double o0 = Open[1];
		    double o1 = Open[2];
		    double c0 = Close[1];
		    double c1 = Close[2];
		
		    bool isHigh        = h0 > h1;
		    bool isLow         = l0 < l1;
		    bool previousGreen = c1 >= o1;
		    bool previousRed   = c1 <  o1;
		    bool currentGreen  = c0 >= o0;
		    bool currentRed    = c0 <  o0;
		
		    // Use the closed bar's time
		    DateTime time = Times[0][1];
		
		    double bodyHigh = Math.Max(o0, c0);
		    double bodyLow  = Math.Min(o0, c0);
			
			 bool createdPivot = false;
		
		    if (previousGreen && currentGreen && isHigh) { ProcessPivot(time, h0, true,  bodyHigh); createdPivot = true; }
		    if (previousRed   && currentRed   && isLow ) { ProcessPivot(time, l0, false, bodyLow ); createdPivot = true; }
		    if (previousGreen && currentRed   && isHigh) { ProcessPivot(time, h0, true,  bodyHigh); createdPivot = true; }
		    if (previousRed   && currentGreen && isLow ) { ProcessPivot(time, l0, false, bodyLow ); createdPivot = true; }
		    if (previousRed   && currentGreen && isHigh) { ProcessPivot(time, h0, true,  bodyHigh); createdPivot = true; }
		    if (previousGreen && currentRed   && isLow ) { ProcessPivot(time, l0, false, bodyLow ); createdPivot = true; }
		
		    // No pivot created => do nothing
		}


        private void ProcessPivot(DateTime time, double price, bool isHigh, double guidePrice)
        {
            // Basic alternating-pivot ZigZag:
            // - First pivot: add
            // - Same-side pivot: replace if more extreme
            // - Opposite-side pivot: append
            int n = pivotTimes.Count;

            if (n == 0)
            {
                AddPivot(time, price, isHigh, guidePrice);
                return;
            }

            bool lastIsHigh  = pivotIsHigh[n - 1];
            double lastPrice = pivotPrices[n - 1];

            if (lastIsHigh == isHigh)
            {
                if (isHigh && price > lastPrice)
                    ReplaceLastPivot(time, price, isHigh, guidePrice);
                else if (!isHigh && price < lastPrice)
                    ReplaceLastPivot(time, price, isHigh, guidePrice);

                return; // ignore weaker same-side pivot
            }

            AddPivot(time, price, isHigh, guidePrice);
        }

        private void AddPivot(DateTime time, double price, bool isHigh, double guidePrice)
        {
            if (pivotTimes.Count > 0)
            {
                // Lock in the PREVIOUS pivot as a Confirmed Trend Start
                int lastIdx = pivotTimes.Count - 1;
                bool prevIsHigh    = pivotIsHigh[lastIdx];
                double prevPrice   = pivotGuidePrices[lastIdx]; // Use the GuidePrice (OG / body), not the wick 'price'
                DateTime prevTime  = pivotTimes[lastIdx];

                if (prevIsHigh) // TSD
                {
                    tsd2_time = tsd1_time;
                    tsd2_price = tsd1_price;
                    tsd1_time = prevTime;
                    tsd1_price = prevPrice;
                }
                else // TSU
                {
                    tsu2_time = tsu1_time;
                    tsu2_price = tsu1_price;
                    tsu1_time = prevTime;
                    tsu1_price = prevPrice;
                }
            }

            pivotTimes.Add(time);
            pivotPrices.Add(price);
            pivotIsHigh.Add(isHigh);
            pivotGuidePrices.Add(guidePrice);

            // Export DTO (kept aligned)
            exportPivots.Add(new ExportPivot
            {
                Level     = price,
                Time      = time,
                Status    = "NEW",
                IsHigh    = isHigh,
                GuideLevel= guidePrice
            });

            lastPivotPrice = price;
            lastPivotType  = isHigh ? 1 : -1;

            DrawOrUpdateLastSegment();
        }

        private void ReplaceLastPivot(DateTime time, double price, bool isHigh, double guidePrice)
        {
            int idx = pivotTimes.Count - 1;

            pivotTimes[idx]       = time;
            pivotPrices[idx]      = price;
            pivotIsHigh[idx]      = isHigh;
            pivotGuidePrices[idx] = guidePrice;

            // Keep export list aligned with pivot arrays
            if (exportPivots != null && exportPivots.Count == pivotTimes.Count && idx >= 0)
            {
                ExportPivot ep = exportPivots[idx];
                if (ep == null)
                {
                    ep = new ExportPivot();
                    exportPivots[idx] = ep;
                }

                ep.Level      = price;
                ep.Time       = time;
                ep.IsHigh     = isHigh;
                ep.Status     = "REPLACED";
                ep.GuideLevel = guidePrice;
            }

            lastPivotPrice = price;
            lastPivotType  = isHigh ? 1 : -1;


            DrawOrUpdateReplacedLastSegment();
        }

        #endregion
		
		
		#region Draw ZigZag

		private void DrawOrUpdateTrendStartLines()
        {
            if (!CanDrawDebug() || !ShowTrendStartLines)
                return;

            DateTime currTime = Times[0][0];
            double tickOffset = TickSize * LabelOffsetTicks;

            var font = new SimpleFont("Arial", LabelFontSize);

            // TSU 1
            if (tsu1_time != DateTime.MinValue && !double.IsNaN(tsu1_price))
            {
                Draw.Line(this, instanceId + "_TSU1", false, tsu1_time, tsu1_price, currTime, tsu1_price, TSULineColor, TSU1LineStyle, TSULineWidth);
                if (ShowLabels) Draw.Text(this, instanceId + "_LBL_TSU1", false, tsu1_time.ToString("MM/dd HH:mm"), currTime, tsu1_price - tickOffset, 0, TSULineColor, font, TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
            }
            else 
            {
                RemoveDrawObject(instanceId + "_TSU1");
                RemoveDrawObject(instanceId + "_LBL_TSU1");
            }

            // TSU 2
            bool showTsu2 = (tsu1_price < tsu2_price);
            if (showTsu2 && tsu2_time != DateTime.MinValue && !double.IsNaN(tsu2_price))
            {
                Draw.Line(this, instanceId + "_TSU2", false, tsu2_time, tsu2_price, currTime, tsu2_price, TSULineColor, TSU2LineStyle, TSULineWidth);
                if (ShowLabels) Draw.Text(this, instanceId + "_LBL_TSU2", false, tsu2_time.ToString("MM/dd HH:mm"), currTime, tsu2_price - tickOffset, 0, TSULineColor, font, TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
            }
            else 
            {
                RemoveDrawObject(instanceId + "_TSU2");
                RemoveDrawObject(instanceId + "_LBL_TSU2");
            }

            // TSD 1
            if (tsd1_time != DateTime.MinValue && !double.IsNaN(tsd1_price))
            {
                Draw.Line(this, instanceId + "_TSD1", false, tsd1_time, tsd1_price, currTime, tsd1_price, TSDLineColor, TSD1LineStyle, TSDLineWidth);
                if (ShowLabels) Draw.Text(this, instanceId + "_LBL_TSD1", false, tsd1_time.ToString("MM/dd HH:mm"), currTime, tsd1_price + tickOffset, 0, TSDLineColor, font, TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
            }
            else 
            {
                RemoveDrawObject(instanceId + "_TSD1");
                RemoveDrawObject(instanceId + "_LBL_TSD1");
            }

            // TSD 2
            bool showTsd2 = (tsd1_price > tsd2_price);
            if (showTsd2 && tsd2_time != DateTime.MinValue && !double.IsNaN(tsd2_price))
            {
                Draw.Line(this, instanceId + "_TSD2", false, tsd2_time, tsd2_price, currTime, tsd2_price, TSDLineColor, TSD2LineStyle, TSDLineWidth);
                if (ShowLabels) Draw.Text(this, instanceId + "_LBL_TSD2", false, tsd2_time.ToString("MM/dd HH:mm"), currTime, tsd2_price + tickOffset, 0, TSDLineColor, font, TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
            }
            else 
            {
                RemoveDrawObject(instanceId + "_TSD2");
                RemoveDrawObject(instanceId + "_LBL_TSD2");
            }
        }
		
		private void DrawOrUpdateLastSegment()
        {
            if (!CanDrawDebug() || !ShowZigZag)
                return;

            int n = pivotTimes.Count;
            if (n < 2)
                return;

            int segIdx = n - 1;
            string tag = $"{instanceId}_SEG_{segIdx}";

            RemoveDrawObject(tag);

            Draw.Line(this, tag, false,
                pivotTimes[segIdx - 1], pivotPrices[segIdx - 1],
                pivotTimes[segIdx],     pivotPrices[segIdx],
                ZigZagColor, ZigZagStyle, ZigZagWidth);

            if (zzTags != null)
                zzTags.Add(tag);
        }

        private void DrawOrUpdateReplacedLastSegment()
        {
           if (!CanDrawDebug() || !ShowZigZag)
                return;

            int n = pivotTimes.Count;
            if (n < 2)
                return;

            int segIdx = n - 1;
            string tag = $"{instanceId}_SEG_{segIdx}";

            RemoveDrawObject(tag);

            Draw.Line(this, tag, false,
                pivotTimes[segIdx - 1], pivotPrices[segIdx - 1],
                pivotTimes[segIdx],     pivotPrices[segIdx],
                ZigZagColor, ZigZagStyle, ZigZagWidth);

            if (zzTags != null)
                zzTags.Add(tag);
        }
		#endregion

        #region Helpers

		
		private void DPrint(string message)
		{
		
		    // Append time ONLY when enabled (avoid string work otherwise)
		    Print(string.Format(
		        "{0:yyyy-MM-dd HH:mm:ss.fff} | {1}",
		        Times[BarsInProgress][0],
		        message
		    ));
		}

		
		private string F(double v)
		{
		    if (double.IsNaN(v) || double.IsInfinity(v))
		        return "NaN";
		    return v.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
		}


		private bool CanDrawDebug()
		{
		    // Chart-only debug guard:
		    // - Market Analyzer has no chart surface (ChartControl == null)
		    // - Debug drawing must be explicitly enabled
		    if (ChartControl == null)
		        return false;
	
		
		    return true;
		}

        

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenLevelMultiTimeframeRetestV0001[] cacheAlightenLevelMultiTimeframeRetestV0001;
		public AlightenLevelMultiTimeframeRetestV0001 AlightenLevelMultiTimeframeRetestV0001(int barsToProcess, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle, bool showTrendStartLines, System.Windows.Media.Brush tSDLineColor, int tSDLineWidth, DashStyleHelper tSD1LineStyle, DashStyleHelper tSD2LineStyle, System.Windows.Media.Brush tSULineColor, int tSULineWidth, DashStyleHelper tSU1LineStyle, DashStyleHelper tSU2LineStyle, bool showLabels, int labelFontSize, int labelOffsetTicks)
		{
			return AlightenLevelMultiTimeframeRetestV0001(Input, barsToProcess, showZigZag, zigZagColor, zigZagWidth, zigZagStyle, showTrendStartLines, tSDLineColor, tSDLineWidth, tSD1LineStyle, tSD2LineStyle, tSULineColor, tSULineWidth, tSU1LineStyle, tSU2LineStyle, showLabels, labelFontSize, labelOffsetTicks);
		}

		public AlightenLevelMultiTimeframeRetestV0001 AlightenLevelMultiTimeframeRetestV0001(ISeries<double> input, int barsToProcess, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle, bool showTrendStartLines, System.Windows.Media.Brush tSDLineColor, int tSDLineWidth, DashStyleHelper tSD1LineStyle, DashStyleHelper tSD2LineStyle, System.Windows.Media.Brush tSULineColor, int tSULineWidth, DashStyleHelper tSU1LineStyle, DashStyleHelper tSU2LineStyle, bool showLabels, int labelFontSize, int labelOffsetTicks)
		{
			if (cacheAlightenLevelMultiTimeframeRetestV0001 != null)
				for (int idx = 0; idx < cacheAlightenLevelMultiTimeframeRetestV0001.Length; idx++)
					if (cacheAlightenLevelMultiTimeframeRetestV0001[idx] != null && cacheAlightenLevelMultiTimeframeRetestV0001[idx].BarsToProcess == barsToProcess && cacheAlightenLevelMultiTimeframeRetestV0001[idx].ShowZigZag == showZigZag && cacheAlightenLevelMultiTimeframeRetestV0001[idx].ZigZagColor == zigZagColor && cacheAlightenLevelMultiTimeframeRetestV0001[idx].ZigZagWidth == zigZagWidth && cacheAlightenLevelMultiTimeframeRetestV0001[idx].ZigZagStyle == zigZagStyle && cacheAlightenLevelMultiTimeframeRetestV0001[idx].ShowTrendStartLines == showTrendStartLines && cacheAlightenLevelMultiTimeframeRetestV0001[idx].TSDLineColor == tSDLineColor && cacheAlightenLevelMultiTimeframeRetestV0001[idx].TSDLineWidth == tSDLineWidth && cacheAlightenLevelMultiTimeframeRetestV0001[idx].TSD1LineStyle == tSD1LineStyle && cacheAlightenLevelMultiTimeframeRetestV0001[idx].TSD2LineStyle == tSD2LineStyle && cacheAlightenLevelMultiTimeframeRetestV0001[idx].TSULineColor == tSULineColor && cacheAlightenLevelMultiTimeframeRetestV0001[idx].TSULineWidth == tSULineWidth && cacheAlightenLevelMultiTimeframeRetestV0001[idx].TSU1LineStyle == tSU1LineStyle && cacheAlightenLevelMultiTimeframeRetestV0001[idx].TSU2LineStyle == tSU2LineStyle && cacheAlightenLevelMultiTimeframeRetestV0001[idx].ShowLabels == showLabels && cacheAlightenLevelMultiTimeframeRetestV0001[idx].LabelFontSize == labelFontSize && cacheAlightenLevelMultiTimeframeRetestV0001[idx].LabelOffsetTicks == labelOffsetTicks && cacheAlightenLevelMultiTimeframeRetestV0001[idx].EqualsInput(input))
						return cacheAlightenLevelMultiTimeframeRetestV0001[idx];
			return CacheIndicator<AlightenLevelMultiTimeframeRetestV0001>(new AlightenLevelMultiTimeframeRetestV0001(){ BarsToProcess = barsToProcess, ShowZigZag = showZigZag, ZigZagColor = zigZagColor, ZigZagWidth = zigZagWidth, ZigZagStyle = zigZagStyle, ShowTrendStartLines = showTrendStartLines, TSDLineColor = tSDLineColor, TSDLineWidth = tSDLineWidth, TSD1LineStyle = tSD1LineStyle, TSD2LineStyle = tSD2LineStyle, TSULineColor = tSULineColor, TSULineWidth = tSULineWidth, TSU1LineStyle = tSU1LineStyle, TSU2LineStyle = tSU2LineStyle, ShowLabels = showLabels, LabelFontSize = labelFontSize, LabelOffsetTicks = labelOffsetTicks }, input, ref cacheAlightenLevelMultiTimeframeRetestV0001);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenLevelMultiTimeframeRetestV0001 AlightenLevelMultiTimeframeRetestV0001(int barsToProcess, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle, bool showTrendStartLines, System.Windows.Media.Brush tSDLineColor, int tSDLineWidth, DashStyleHelper tSD1LineStyle, DashStyleHelper tSD2LineStyle, System.Windows.Media.Brush tSULineColor, int tSULineWidth, DashStyleHelper tSU1LineStyle, DashStyleHelper tSU2LineStyle, bool showLabels, int labelFontSize, int labelOffsetTicks)
		{
			return indicator.AlightenLevelMultiTimeframeRetestV0001(Input, barsToProcess, showZigZag, zigZagColor, zigZagWidth, zigZagStyle, showTrendStartLines, tSDLineColor, tSDLineWidth, tSD1LineStyle, tSD2LineStyle, tSULineColor, tSULineWidth, tSU1LineStyle, tSU2LineStyle, showLabels, labelFontSize, labelOffsetTicks);
		}

		public Indicators.AlightenLevelMultiTimeframeRetestV0001 AlightenLevelMultiTimeframeRetestV0001(ISeries<double> input , int barsToProcess, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle, bool showTrendStartLines, System.Windows.Media.Brush tSDLineColor, int tSDLineWidth, DashStyleHelper tSD1LineStyle, DashStyleHelper tSD2LineStyle, System.Windows.Media.Brush tSULineColor, int tSULineWidth, DashStyleHelper tSU1LineStyle, DashStyleHelper tSU2LineStyle, bool showLabels, int labelFontSize, int labelOffsetTicks)
		{
			return indicator.AlightenLevelMultiTimeframeRetestV0001(input, barsToProcess, showZigZag, zigZagColor, zigZagWidth, zigZagStyle, showTrendStartLines, tSDLineColor, tSDLineWidth, tSD1LineStyle, tSD2LineStyle, tSULineColor, tSULineWidth, tSU1LineStyle, tSU2LineStyle, showLabels, labelFontSize, labelOffsetTicks);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenLevelMultiTimeframeRetestV0001 AlightenLevelMultiTimeframeRetestV0001(int barsToProcess, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle, bool showTrendStartLines, System.Windows.Media.Brush tSDLineColor, int tSDLineWidth, DashStyleHelper tSD1LineStyle, DashStyleHelper tSD2LineStyle, System.Windows.Media.Brush tSULineColor, int tSULineWidth, DashStyleHelper tSU1LineStyle, DashStyleHelper tSU2LineStyle, bool showLabels, int labelFontSize, int labelOffsetTicks)
		{
			return indicator.AlightenLevelMultiTimeframeRetestV0001(Input, barsToProcess, showZigZag, zigZagColor, zigZagWidth, zigZagStyle, showTrendStartLines, tSDLineColor, tSDLineWidth, tSD1LineStyle, tSD2LineStyle, tSULineColor, tSULineWidth, tSU1LineStyle, tSU2LineStyle, showLabels, labelFontSize, labelOffsetTicks);
		}

		public Indicators.AlightenLevelMultiTimeframeRetestV0001 AlightenLevelMultiTimeframeRetestV0001(ISeries<double> input , int barsToProcess, bool showZigZag, System.Windows.Media.Brush zigZagColor, int zigZagWidth, DashStyleHelper zigZagStyle, bool showTrendStartLines, System.Windows.Media.Brush tSDLineColor, int tSDLineWidth, DashStyleHelper tSD1LineStyle, DashStyleHelper tSD2LineStyle, System.Windows.Media.Brush tSULineColor, int tSULineWidth, DashStyleHelper tSU1LineStyle, DashStyleHelper tSU2LineStyle, bool showLabels, int labelFontSize, int labelOffsetTicks)
		{
			return indicator.AlightenLevelMultiTimeframeRetestV0001(input, barsToProcess, showZigZag, zigZagColor, zigZagWidth, zigZagStyle, showTrendStartLines, tSDLineColor, tSDLineWidth, tSD1LineStyle, tSD2LineStyle, tSULineColor, tSULineWidth, tSU1LineStyle, tSU2LineStyle, showLabels, labelFontSize, labelOffsetTicks);
		}
	}
}

#endregion

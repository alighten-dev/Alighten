#region Using declarations
using System;
using System.Collections;
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
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenFootprintOrderFlowV00015 : Indicator
    {
        #region Properties

		// ***** Aggregation Settings *****
		[NinjaScriptProperty]
		[Display(Name = "Aggregation Interval (ticks)", Order = 1, GroupName = "Aggregation Settings")]
		public int AggregationInterval { get; set; } = 15;  // Use a higher value (15) to match iotecOFPlus4
		
		[NinjaScriptProperty]
		[Display(Name = "Value Area %", Order = 2, GroupName = "Aggregation Settings")]
		public double ValueAreaPer { get; set; } = 70;
		
		[NinjaScriptProperty]
		[Display(Name = "Align Aggregation To Session Open", Order = 3, GroupName = "Aggregation Settings")]
		public bool UseSessionOpenForAggregation { get; set; } = true;
		
		// ***** Signal Lookback Settings *****
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Volume Seq Lookback", Order = 3, GroupName = "Signal Lookback Settings")]
		public int VolumeSeqLookback { get; set; } = 4;
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Stacked Imbalance Lookback", Order = 4, GroupName = "Signal Lookback Settings")]
		public int StackedImbalanceLookback { get; set; } = 2;
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Delta Sequence Lookback", Order = 5, GroupName = "Signal Lookback Settings")]
		public int DeltaSequenceLookback { get; set; } = 2;
		
		[NinjaScriptProperty]
		[Display(Name = "Sweep Lookback", Order = 6, GroupName = "Signal Lookback Settings")]
		public int SweepLookback { get; set; } = 4;
		
		[NinjaScriptProperty]
		[Display(Name = "Divergence Lookback", Order = 7, GroupName = "Signal Lookback Settings")]
		public int DivergenceLookback { get; set; } = 5;
		
		[NinjaScriptProperty]
		[Display(
		    Name = "Use Session High/Low Divergence",
		    Order = 8,                      // place near Divergence entries
		    GroupName = "Signal Lookback Settings")]
		public bool UseSessionHiLoDivergence { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Stopping Volume Lookback", Order = 9, GroupName = "Signal Lookback Settings")]
		public int StoppingVolumeLookback { get; set; } = 3;		
		
		[NinjaScriptProperty]
		[Display(Name = "Delta Flip Lookback", Order = 10, GroupName = "Signal Lookback Settings")]
		public int DeltaFlipLookback { get; set; } = 3;	
		
		[NinjaScriptProperty]
		[Display(Name = "Fading Momentum Lookback", Order = 11, GroupName = "Signal Lookback Settings")]
		public int FadingMomentumLookback { get; set; } = 3;		
		
		// ***** Signal Thresholds *****
		[NinjaScriptProperty]
		[Display(Name = "Imbalance Ratio", Order = 7, GroupName = "Signal Thresholds")]
		public double ImbFact { get; set; } = 3.0;
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Large Ratio Threshold", Order = 8, GroupName = "Signal Thresholds")]
		public double LargeRatioThreshold { get; set; } = 30;  
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Small Ratio Threshold", Order = 9, GroupName = "Signal Thresholds")]
		public double SmallRatioThreshold { get; set; } = 0.69;  
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Delta Threshold 1", Order = 10, GroupName = "Signal Thresholds")]
		public int DeltaThreshold1 { get; set; } = 100;  
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Delta Threshold 2", Order = 11, GroupName = "Signal Thresholds")]
		public int DeltaThreshold2 { get; set; } = 300; 
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Volume Threshold", Order = 12, GroupName = "Signal Thresholds")]
		public int VolumeThreshold { get; set; } = 2000; 
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Near Zero Threshold (Flip/Sweep/Table Display)", Order = 12, GroupName = "Signal Thresholds")]
		public int NearZeroaThreshold { get; set; } = 8; 
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Exhaustion Threshold", Order = 13, GroupName = "Signal Thresholds")]
		public int ExhaustionThreshold { get; set; } = 8; 	
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Set the Font Size", Order = 1, GroupName = "Display")]
		public int StandardFontSize { get; set; } = 16; 
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Footprint on Bars", Order = 2, GroupName = "Display")]
		public bool EnableFootprint { get; set; } = true; 
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Footprint Text", Order = 3, GroupName = "Display")]
		public bool EnableFootprintText { get; set; } = true; 
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Summary Grid on Chart", Order = 4, GroupName = "Display")]
		public bool EnableSummaryGrid { get; set; } = true; // Adds the summary grid at the bottom of the chart to display Delta/MinDelta/MaxDelta/Volume
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Signal Grid on Bar", Order = 5, GroupName = "Display")]
		public bool EnableSignalGrid { get; set; } = true; // Adds the summary grid at the bottom of the chart to display Delta/MinDelta/MaxDelta/Volume
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Signal Grid Offset", Order = 6, GroupName = "Display")]
		public int SignalGridOffset { get; set; } = 40; // Signal display offset from bar
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Signal Legend", Order = 7, GroupName = "Display")]
		public bool EnableSignalGridLegend { get; set; } = false; // Adds the summary grid at the bottom of the chart to display Delta/MinDelta/MaxDelta/Volume
		
		// ***** Summary Grid Row Visibility *****
		[NinjaScriptProperty]
		[Display(Name = "Show Summary Delta Signals Row", Order = 8, GroupName = "Display")]
		public bool ShowSummaryDeltaSignalsRow { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name = "Show Summary Delta Rows (Δ / Max / Min)", Order = 9, GroupName = "Display")]
		public bool ShowSummaryDeltaRows { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name = "Show Summary COT Rows (High / Low)", Order = 10, GroupName = "Display")]
		public bool ShowSummaryCOTRows { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Show Summary Volume Row", Order = 11, GroupName = "Display")]
		public bool ShowSummaryVolumeRow { get; set; } = true;

		
		[NinjaScriptProperty]
		[Display(Name = "Enable Delta on Bar", Order = 12, GroupName = "Display")]
		public bool EnableDeltaOnBar { get; set; } = true;
		
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Delta on Bar Font Size", Order = 13, GroupName = "Display")]
		public int DeltaOnBarFontSize { get; set; } = 16;
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Delta on Bar Positive Color", Order = 14, GroupName = "Display")]
		public System.Windows.Media.Brush DeltaOnBarPositiveColor { get; set; } = System.Windows.Media.Brushes.LimeGreen;
		[Browsable(false)]
		public string DeltaOnBarPositiveColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaOnBarPositiveColor); }
		    set { DeltaOnBarPositiveColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Delta on Bar Negative Color", Order = 15, GroupName = "Display")]
		public System.Windows.Media.Brush DeltaOnBarNegativeColor { get; set; } = System.Windows.Media.Brushes.Red;
		[Browsable(false)]
		public string DeltaOnBarNegativeColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaOnBarNegativeColor); }
		    set { DeltaOnBarNegativeColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Delta on Bar Offset (px)", Order = 16, GroupName = "Display")]
		public int DeltaOnBarOffset { get; set; } = 20;
		
		// ***** Color Settings *****
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Default Bid/Ask Text Color", Order = 4, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush DefaultFootprintTextColor { get; set; } = System.Windows.Media.Brushes.White;
		[Browsable(false)]
		public string DefaultFootprintTextColorSerialize
		{
		    get { return Serialize.BrushToString(DefaultFootprintTextColor); }
		    set { DefaultFootprintTextColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Positive Imbalance Text Color", Order = 5, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush PositiveImbalanceTextColor { get; set; } = System.Windows.Media.Brushes.Blue;
		[Browsable(false)]
		public string PositiveImbalanceTextColorSerialize
		{
		    get { return Serialize.BrushToString(PositiveImbalanceTextColor); }
		    set { PositiveImbalanceTextColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Negative Imbalance Text Color", Order = 6, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush NegativeImbalanceTextColor { get; set; } = System.Windows.Media.Brushes.Red;
		[Browsable(false)]
		public string NegativeImbalanceTextColorSerialize
		{
		    get { return Serialize.BrushToString(NegativeImbalanceTextColor); }
		    set { NegativeImbalanceTextColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Bar Up Color", Order = 7, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush BarUpColor { get; set; } = System.Windows.Media.Brushes.LimeGreen;
		[Browsable(false)]
		public string BarUpColorSerialize
		{
		    get { return Serialize.BrushToString(BarUpColor); }
		    set { BarUpColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Bar Down Color", Order = 8, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush BarDownColor { get; set; } = System.Windows.Media.Brushes.Red;
		[Browsable(false)]
		public string BarDownColorSerialize
		{
		    get { return Serialize.BrushToString(BarDownColor); }
		    set { BarDownColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Bar Up Outline Color", Order = 9, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush BarUpOutlineColor { get; set; } = System.Windows.Media.Brushes.White;
		[Browsable(false)]
		public string BarUpOutlineColorSerialize
		{
		    get { return Serialize.BrushToString(BarUpOutlineColor); }
		    set { BarUpOutlineColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Bar Down Outline Color", Order = 10, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush BarDownOutlineColor { get; set; } = System.Windows.Media.Brushes.White;
		[Browsable(false)]
		public string BarDownOutlineColorSerialize
		{
		    get { return Serialize.BrushToString(BarDownOutlineColor); }
		    set { BarDownOutlineColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Bar Up Value Area Color", Order = 11, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush BarUpVAColor { get; set; } = System.Windows.Media.Brushes.DarkGreen;
		[Browsable(false)]
		public string BarUpVAColorSerialize
		{
		    get { return Serialize.BrushToString(BarUpVAColor); }
		    set { BarUpVAColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Bar Down Value Area Color", Order = 12, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush BarDownVAColor { get; set; } = System.Windows.Media.Brushes.Red;
		[Browsable(false)]
		public string BarDownVAColorSerialize
		{
		    get { return Serialize.BrushToString(BarDownVAColor); }
		    set { BarDownVAColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Value Area Opacity (0 - 100)", Order = 13, GroupName = "Footprint Bar Color Settings")]
		public int VAOpacity { get; set; } = 20; 
		
		[XmlIgnore]
		[NinjaScriptProperty]	
		[Display(Name = "POC Color", Order = 20, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush POCColor { get; set; } = System.Windows.Media.Brushes.Gray;
		[Browsable(false)]
		public string POCColorSerialize
		{
		    get { return Serialize.BrushToString(POCColor); }
		    set { POCColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]	
		[Display(Name = "POC Outline Color", Order = 21, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush POCOutlineColor { get; set; } = System.Windows.Media.Brushes.Yellow;
		[Browsable(false)]
		public string POCOutlineColorSerialize
		{
		    get { return Serialize.BrushToString(POCOutlineColor); }
		    set { POCOutlineColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]	
		[Display(Name = "POC Text Color", Order = 22, GroupName = "Footprint Bar Color Settings")]
		public System.Windows.Media.Brush POCTextColor { get; set; } = System.Windows.Media.Brushes.White;
		[Browsable(false)]
		public string POCTextColorSerialize
		{
		    get { return Serialize.BrushToString(POCTextColor); }
		    set { POCTextColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Delta Low Positive Color", Order = 12, GroupName = "Summary Table Color Settings")]
		public System.Windows.Media.Brush DeltaLowPositiveColor { get; set; } = System.Windows.Media.Brushes.DarkGreen;
		[Browsable(false)]
		public string DeltaLowPositiveColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaLowPositiveColor); }
		    set { DeltaLowPositiveColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Delta Medium Positive Color", Order = 13, GroupName = "Summary Table Color Settings")]
		public System.Windows.Media.Brush DeltaMediumPositiveColor { get; set; } = System.Windows.Media.Brushes.Green;
		[Browsable(false)]
		public string DeltaMediumPositiveColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaMediumPositiveColor); }
		    set { DeltaMediumPositiveColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Delta High Positive Color", Order = 14, GroupName = "Summary Table Color Settings")]
		public System.Windows.Media.Brush DeltaHighPositiveColor { get; set; } = System.Windows.Media.Brushes.Lime;
		[Browsable(false)]
		public string DeltaHighPositiveColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaHighPositiveColor); }
		    set { DeltaHighPositiveColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Delta Low Negative Color", Order = 15, GroupName = "Summary Table Color Settings")]
		public System.Windows.Media.Brush DeltaLowNegativeColor { get; set; } = System.Windows.Media.Brushes.DarkRed;
		[Browsable(false)]
		public string DeltaLowNegativeColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaLowNegativeColor); }
		    set { DeltaLowNegativeColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Delta Medium Negative Color", Order = 16, GroupName = "Summary Table Color Settings")]
		public System.Windows.Media.Brush DeltaMediumNegativeColor { get; set; } = System.Windows.Media.Brushes.Red;
		[Browsable(false)]
		public string DeltaMediumNegativeColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaMediumNegativeColor); }
		    set { DeltaMediumNegativeColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Delta High Negative Color", Order = 17, GroupName = "Summary Table Color Settings")]
		public System.Windows.Media.Brush DeltaHighNegativeColor { get; set; } = System.Windows.Media.Brushes.Crimson;
		[Browsable(false)]
		public string DeltaHighNegativeColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaHighNegativeColor); }
		    set { DeltaHighNegativeColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "High Volume Color", Order = 18, GroupName = "Summary Table Color Settings")]
		public System.Windows.Media.Brush HighVolumeColor { get; set; } = System.Windows.Media.Brushes.Cyan;
		[Browsable(false)]
		public string HighVolumeColorSerialize
		{
		    get { return Serialize.BrushToString(HighVolumeColor); }
		    set { HighVolumeColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]		
		[Display(Name = "Zero Delta Color", Order = 19, GroupName = "Summary Table Color Settings")]
		public System.Windows.Media.Brush ZeroDeltaColor { get; set; } = System.Windows.Media.Brushes.Gray;
		[Browsable(false)]
		public string ZeroDeltaColorSerialize
		{
		    get { return Serialize.BrushToString(ZeroDeltaColor); }
		    set { ZeroDeltaColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Summary Grid Line Color", Order = 20, GroupName = "Summary Table Color Settings")]
		public System.Windows.Media.Brush SummaryGridLineColor { get; set; } = System.Windows.Media.Brushes.White;
		[Browsable(false)]
		public string SummaryGridLineColorSerialize
		{
		    get { return Serialize.BrushToString(SummaryGridLineColor); }
		    set { SummaryGridLineColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Summary Grid Line Thickness", Order = 21, GroupName = "Summary Table Color Settings")]
		public int SummaryGridLineThickness { get; set; } = 1;

		// ***** Signal Colors *****
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "VolSeq Color", Order = 20, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush VolSeqColor { get; set; } = System.Windows.Media.Brushes.Blue;
		[Browsable(false)]
		public string VolSeqColorSerialize
		{
		    get { return Serialize.BrushToString(VolSeqColor); }
		    set { VolSeqColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "StackedImb Color", Order = 21, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush StackedImbColor { get; set; } = System.Windows.Media.Brushes.Orange;
		[Browsable(false)]
		public string StackedImbColorSerialize
		{
		    get { return Serialize.BrushToString(StackedImbColor); }
		    set { StackedImbColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "RevPOC Color", Order = 22, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush RevPOCColor { get; set; } = System.Windows.Media.Brushes.Purple;
		[Browsable(false)]
		public string RevPOCColorSerialize
		{
		    get { return Serialize.BrushToString(RevPOCColor); }
		    set { RevPOCColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Sweep Color", Order = 23, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush SweepColor { get; set; } = System.Windows.Media.Brushes.Yellow;
		[Browsable(false)]
		public string SweepColorSerialize
		{
		    get { return Serialize.BrushToString(SweepColor); }
		    set { SweepColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "DeltaSeq Color", Order = 24, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush DeltaSeqColor { get; set; } = System.Windows.Media.Brushes.Magenta;
		[Browsable(false)]
		public string DeltaSeqColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaSeqColor); }
		    set { DeltaSeqColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Divergence Color", Order = 25, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush DivergenceColor { get; set; } = System.Windows.Media.Brushes.Cyan;
		[Browsable(false)]
		public string DivergenceColorSerialize
		{
		    get { return Serialize.BrushToString(DivergenceColor); }
		    set { DivergenceColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Absorption Color", Order = 26, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush AbsorptionColor { get; set; } = System.Windows.Media.Brushes.LightGreen;
		[Browsable(false)]
		public string AbsorptionColorSerialize
		{
		    get { return Serialize.BrushToString(AbsorptionColor); }
		    set { AbsorptionColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Exhaustion Color", Order = 27, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush ExhaustionColor { get; set; } = System.Windows.Media.Brushes.Red;
		[Browsable(false)]
		public string ExhaustionColorSerialize
		{
		    get { return Serialize.BrushToString(ExhaustionColor); }
		    set { ExhaustionColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "VAGap Color", Order = 28, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush VAGapColor { get; set; } = System.Windows.Media.Brushes.Teal;
		[Browsable(false)]
		public string VAGapColorSerialize
		{
		    get { return Serialize.BrushToString(VAGapColor); }
		    set { VAGapColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Extreme Ratio Color", Order = 29, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush ExtremeRatioColor { get; set; } = System.Windows.Media.Brushes.Pink;
		[Browsable(false)]
		public string ExtremeRatioColorSerialize
		{
		    get { return Serialize.BrushToString(ExtremeRatioColor); }
		    set { ExtremeRatioColor = Serialize.StringToBrush(value); }
		}	
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Delta Flip Color", Order = 30, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush DeltaFlipColor { get; set; } = System.Windows.Media.Brushes.DarkGreen;
		[Browsable(false)]
		public string DeltaFlipColorSerialize
		{
		    get { return Serialize.BrushToString(DeltaFlipColor); }
		    set { DeltaFlipColor = Serialize.StringToBrush(value); }
		}	
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Stopping Volume Color", Order = 31, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush StoppingVolumeColor { get; set; } = System.Windows.Media.Brushes.Brown;
		[Browsable(false)]
		public string StoppingVolumeColorSerialize
		{
		    get { return Serialize.BrushToString(StoppingVolumeColor); }
		    set { StoppingVolumeColor = Serialize.StringToBrush(value); }
		}	
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Fading Momentum Color", Order = 32, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush FadingMomentumColor { get; set; } = System.Windows.Media.Brushes.Crimson;
		[Browsable(false)]
		public string FadingMomentumColorSerialize
		{
		    get { return Serialize.BrushToString(FadingMomentumColor); }
		    set { FadingMomentumColor = Serialize.StringToBrush(value); }
		}	
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Extreme VA Color", Order = 33, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush ExtremeVAColor { get; set; } = System.Windows.Media.Brushes.Gold;
		[Browsable(false)]
		public string ExtremeVAColorSerialize
		{
		    get { return Serialize.BrushToString(ExtremeVAColor); }
		    set { ExtremeVAColor = Serialize.StringToBrush(value); }
		}	
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "POC In Wick Color", Order = 34, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush POCInWickColor { get; set; } = System.Windows.Media.Brushes.MediumSlateBlue;
		[Browsable(false)]
		public string POCInWickColorSerialize
		{
		    get { return Serialize.BrushToString(POCInWickColor); }
		    set { POCInWickColor = Serialize.StringToBrush(value); }
		}	
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "Extreme POC Color", Order = 35, GroupName = "Signal Colors")]
		public System.Windows.Media.Brush ExtremePOCColor { get; set; } = System.Windows.Media.Brushes.DarkOrange;
		[Browsable(false)]
		public string ExtremePOCColorSerialize
		{
		    get { return Serialize.BrushToString(ExtremePOCColor); }
		    set { ExtremePOCColor = Serialize.StringToBrush(value); }
		}	
		
		// ***** Show/Hide Individual Signals for Predator *****
		[NinjaScriptProperty]
		[Display(Name = "Enable VolSeq Diamond Signal (11)", Order = 100, GroupName = "Individual Signal Settings")]
		public bool EnableVolSeqSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "VolSeq Diamond Offset", Order = 101, GroupName = "Individual Signal Settings")]
		public int VolSeqDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Stacked Imb Diamond Signal (12)", Order = 102, GroupName = "Individual Signal Settings")]
		public bool EnableStackedImbSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Stacked Imb Diamond Offset", Order = 103, GroupName = "Individual Signal Settings")]
		public int StackedImbDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Reversal POC Diamond Signal (13)", Order = 104, GroupName = "Individual Signal Settings")]
		public bool EnableReversalPOCSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Reversal POC Diamond Offset", Order = 105, GroupName = "Individual Signal Settings")]
		public int ReversalPOCDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Sweep Diamond Signal (14)", Order = 106, GroupName = "Individual Signal Settings")]
		public bool EnableSweepSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Sweep Diamond Offset", Order = 107, GroupName = "Individual Signal Settings")]
		public int SweepDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable DeltaSeq Diamond Signal (15)", Order = 108, GroupName = "Individual Signal Settings")]
		public bool EnableDeltaSeqSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "DeltaSeq Diamond Offset", Order = 109, GroupName = "Individual Signal Settings")]
		public int DeltaSeqDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Divergence Diamond Signal (16)", Order = 110, GroupName = "Individual Signal Settings")]
		public bool EnableDivergenceSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Divergence Diamond Offset", Order = 111, GroupName = "Individual Signal Settings")]
		public int DivergenceDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Delta Flip Diamond Signal (17)", Order = 112, GroupName = "Individual Signal Settings")]
		public bool EnableDeltaFlipSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Delta Flip Diamond Offset", Order = 113, GroupName = "Individual Signal Settings")]
		public int DeltaFlipDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Stopping Volume Diamond Signal (18)", Order = 114, GroupName = "Individual Signal Settings")]
		public bool EnableStoppingVolumeSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Stopping Volume Diamond Offset", Order = 115, GroupName = "Individual Signal Settings")]
		public int StoppingVolumeDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Absorption Diamond Signal (19)", Order = 116, GroupName = "Individual Signal Settings")]
		public bool EnableAbsorptionSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Absorption Diamond Offset", Order = 117, GroupName = "Individual Signal Settings")]
		public int AbsorptionDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Exhaustion Diamond Signal (20)", Order = 118, GroupName = "Individual Signal Settings")]
		public bool EnableExhaustionSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Exhaustion Diamond Offset", Order = 119, GroupName = "Individual Signal Settings")]
		public int ExhaustionDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable VAGap Diamond Signal (21)", Order = 120, GroupName = "Individual Signal Settings")]
		public bool EnableVAGapSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "VAGap Diamond Offset", Order = 121, GroupName = "Individual Signal Settings")]
		public int VAGapDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Extreme Ratio Diamond Signal (22)", Order = 122, GroupName = "Individual Signal Settings")]
		public bool EnableExtremeRatioSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Extreme Ratio Diamond Offset", Order = 123, GroupName = "Individual Signal Settings")]
		public int ExtremeRatioDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Fading Momentum Diamond Signal (23)", Order = 124, GroupName = "Individual Signal Settings")]
		public bool EnableFadingMomentumSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Fading Momentum Diamond Offset", Order = 125, GroupName = "Individual Signal Settings")]
		public int FadingMomentumDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Extreme VA Diamond Signal (33)", Order = 126, GroupName = "Individual Signal Settings")]
		public bool EnableExtremeVASignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Extreme VA Diamond Offset", Order = 127, GroupName = "Individual Signal Settings")]
		public int ExtremeVADiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable POC In Wick Diamond Signal (34)", Order = 128, GroupName = "Individual Signal Settings")]
		public bool EnablePOCInWickSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "POC In Wick Diamond Offset", Order = 129, GroupName = "Individual Signal Settings")]
		public int POCInWickDiamondOffset { get; set; } = 3;
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Extreme POC Diamond Signal (35)", Order = 130, GroupName = "Individual Signal Settings")]
		public bool EnableExtremePOCSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Extreme POC Diamond Offset", Order = 131, GroupName = "Individual Signal Settings")]
		public int ExtremePOCDiamondOffset { get; set; } = 3;
		
		// ***** Combination Plots *****
		[NinjaScriptProperty]
		[Display(Name = "CustomPlot1 Sources (comma-separated, 11–23)", Order = 1, GroupName = "Combination Plots")]
		public string CustomPlot1Sources { get; set; } = "";
		
		[NinjaScriptProperty]
		[Display(Name = "CustomPlot2 Sources (comma-separated, 11–23)", Order = 2, GroupName = "Combination Plots")]
		public string CustomPlot2Sources { get; set; } = "";
		
		[NinjaScriptProperty]
		[Display(Name = "CustomPlot3 Sources (comma-separated, 11–23)", Order = 3, GroupName = "Combination Plots")]
		public string CustomPlot3Sources { get; set; } = "";
		
		// ── Combination Plots (visuals) ──────────────────────────────────────────────
		[NinjaScriptProperty]
		[Display(Name = "Enable CustomPlot1 Signal", GroupName = "Combination Plots", Order = 30)]
		public bool EnableCustomPlot1Signal { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name = "CustomPlot1 Diamond Offset (ticks)", GroupName = "Combination Plots", Order = 31)]
		public int CustomPlot1DiamondOffset { get; set; } = 6;
		
		[XmlIgnore]
		[Display(Name = "CustomPlot1 Color", GroupName = "Combination Plots", Order = 32)]
		public System.Windows.Media.Brush CustomPlot1Color { get; set; } = Brushes.DodgerBlue;
		
		[Browsable(false)]
		public string CustomPlot1ColorSerializable
		{
		    get => Serialize.BrushToString(CustomPlot1Color);
		    set => CustomPlot1Color = Serialize.StringToBrush(value);
		}
		
		[NinjaScriptProperty]
		[Display(Name = "Enable CustomPlot2 Signal", GroupName = "Combination Plots", Order = 40)]
		public bool EnableCustomPlot2Signal { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name = "CustomPlot2 Diamond Offset (ticks)", GroupName = "Combination Plots", Order = 41)]
		public int CustomPlot2DiamondOffset { get; set; } = 6;
		
		[XmlIgnore]
		[Display(Name = "CustomPlot2 Color", GroupName = "Combination Plots", Order = 42)]
		public System.Windows.Media.Brush CustomPlot2Color { get; set; } = Brushes.DarkViolet;
		
		[Browsable(false)]
		public string CustomPlot2ColorSerializable
		{
		    get => Serialize.BrushToString(CustomPlot2Color);
		    set => CustomPlot2Color = Serialize.StringToBrush(value);
		}
		
		[NinjaScriptProperty]
		[Display(Name = "Enable CustomPlot3 Signal", GroupName = "Combination Plots", Order = 50)]
		public bool EnableCustomPlot3Signal { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name = "CustomPlot3 Diamond Offset (ticks)", GroupName = "Combination Plots", Order = 51)]
		public int CustomPlot3DiamondOffset { get; set; } = 6;
		
		[XmlIgnore]
		[Display(Name = "CustomPlot3 Color", GroupName = "Combination Plots", Order = 52)]
		public System.Windows.Media.Brush CustomPlot3Color { get; set; } = Brushes.White;
		
		[Browsable(false)]
		public string CustomPlot3ColorSerializable
		{
		    get => Serialize.BrushToString(CustomPlot3Color);
		    set => CustomPlot3Color = Serialize.StringToBrush(value);
		}
		
		// ===== VA-at-Extreme & VA+POC Combo (User Controls) =====
		
		[NinjaScriptProperty]
		[Display(Name = "Enable VA+POC Extreme Triangles (34)", GroupName = "VA and POC Signal Settings", Order = 201)]
		public bool EnableVAPOCExtremeComboSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Range(8, 72)]
		[Display(Name = "Triangle Font Size", GroupName = "VA and POC Signal Settings", Order = 202)]
		public int TriangleFontSize { get; set; } = 18;
		
		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Triangle Offset (ticks)", GroupName = "VA and POC Signal Settings", Order = 203)]
		public int TriangleOffsetTicks { get; set; } = 6;
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "VA+POC Bull Color", Order = 262, GroupName = "VA and POC Signal Settings")]
		public System.Windows.Media.Brush VAPOCBullColor { get; set; } = System.Windows.Media.Brushes.Lime;
		[Browsable(false)]
		public string VAPOCBullColorSerialize
		{
		    get { return Serialize.BrushToString(VAPOCBullColor); }
		    set { VAPOCBullColor = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[NinjaScriptProperty]
		[Display(Name = "VA+POC Bear Color", Order = 263, GroupName = "VA and POC Signal Settings")]
		public System.Windows.Media.Brush VAPOCBearColor { get; set; } = System.Windows.Media.Brushes.Fuchsia;
		[Browsable(false)]
		public string VAPOCBearColorSerialize
		{
		    get { return Serialize.BrushToString(VAPOCBearColor); }
		    set { VAPOCBearColor = Serialize.StringToBrush(value); }
		}




		#endregion
		
		#region Class Variables
		

        // ***** Internal variables *****
        private NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType barsType;
        private double tickSize;
		private int lastPrintedBar = -1;
		private double sessionOpenPrice;

        // Aggregated dictionaries persist across bars (keyed by bar index)
        private Dictionary<int, Dictionary<double, double>> GetAskVolumeForPrice = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> GetBidVolumeForPrice = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> GetDeltaForPrice = new Dictionary<int, Dictionary<double, double>>();
        private Dictionary<int, Dictionary<double, double>> GetTotalVolumeForPrice = new Dictionary<int, Dictionary<double, double>>();
		
		// Dictionaries for calculated values
		private Dictionary<int, double> GetDeltaForBar = new Dictionary<int, double>();		
		private Dictionary<int, double> GetMinDeltaForBar = new Dictionary<int, double>();
		private Dictionary<int, double> GetMaxDeltaForBar = new Dictionary<int, double>();
		private Dictionary<int, double> GetCumDeltaForBar = new Dictionary<int, double>();
		private Dictionary<int, double> GetVolumeForBar = new Dictionary<int, double>();
		private Dictionary<int, double> GetPOCForBar = new Dictionary<int, double>();
		private Dictionary<int, double> GetVAHForBar = new Dictionary<int, double>();
		private Dictionary<int, double> GetVALForBar = new Dictionary<int, double>();
		private Dictionary<int, double> GetRatioForBar = new Dictionary<int, double>();
		private Dictionary<int, int> GetPOCPosForBar = new Dictionary<int, int>(); 
		private Dictionary<int, double> GetDeltaPerVolumeForBar = new Dictionary<int, double>(); 
		private Dictionary<int, string> GetDeltaSignalsForBar = new Dictionary<int, string>();		
		
		// Dictionary for calculated signals
		private Dictionary<int, int> GetVolSeqForBar = new Dictionary<int, int>(); 
		private Dictionary<int, int> GetStackedImbForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetReversalPOCForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetSweepForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetDeltaSeqForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetDivergenceForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetDeltaFlipForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetStoppingVolumeForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetAbsorptionForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetExhaustionForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetVAGapForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetExtremeRatioForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetFadingMomentumForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetExtremeVAForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetPOCInWickForBar = new Dictionary<int, int>();
		private Dictionary<int, int> GetExtremePOCForBar = new Dictionary<int, int>();
		
		// Dictionaries for calculated POC position
		private Dictionary<int, int>   GetPocVAFromLow    = new Dictionary<int,int>();
		private Dictionary<int, int>   GetPocVAFromHigh   = new Dictionary<int,int>();
		private Dictionary<int, int>   GetPocBarFromLow   = new Dictionary<int,int>();
		private Dictionary<int, int>   GetPocBarFromHigh  = new Dictionary<int,int>();
		private Dictionary<int, double> GetPocToBodyHigh  = new Dictionary<int,double>();
		private Dictionary<int, double> GetPocToBodyLow   = new Dictionary<int,double>();
				
        // Calculated values for the current bar 
        private double barDelta = 0;
		private double minDelta = 0;
		private double maxDelta = 0;   
		private double minBarDelta = 0;
		private double maxBarDelta = 0;
		private int   deltaBarIndex = -1;
        private double cumulativeDelta = 0;
        private double totalVolume = 0;
        private double pocPrice = 0;
        private double vah = 0;  // Value Area High
        private double val = 0;  // Value Area Low
        private double ratio = 0;
        private double pocPos = 0; // POC Position: -1, 0, 1
		private string deltaSignal = "";
		private double deltaPerVolume = 0;
		
        // Calculated signals for the current bar 
        private int volSeqSignal = 0;
        private int stackedImbSignal = 0;
        private int reversalPOCSignal = 0;
        private int sweepSignal = 0;
        private int deltaSeqSignal = 0;
		private int divergenceSignal = 0;
		private int deltaFlipSignal = 0;
		private int stopVolSignal = 0;
		private int absorptionSignal = 0;
		private int exhaustionSignal = 0;
		private int vaGapSignal = 0;
		private int extremeRatioSignal = 0;
		private int fadingMomentumSignal = 0;
		
		// Calculated POC details for the current bar
		private int pocVA_FromLow = 0;
		private int pocVA_FromHigh = 0;
		private int pocBar_FromLow = 0;
		private int pocBar_FromHigh = 0;
		
		// VA and POC Signals
		private int extremeVASignal = 0;     
		private int pocInWickSignal = 0;
		private int extremePOCSignal = 0;  
		private int vapocExtremeSignal = 0;
		
		// --- Live-bar cumulative delta trackers (optional) ---
		private double tBuys    = 0.0;  // sum of buy volume this bar
		private double tSells   = 0.0;  // sum of sell volume this bar
		private double tCdClose = 0.0;  // current bar's running CD (tBuys - tSells)
		private double tCdHigh  = 0.0;  // intrabar max of tCdClose
		private double tCdLow   = 0.0;  // intrabar min of tCdClose
		
		// COT (Cumulative-Orderflow-Through-the-bar) per bar
		private readonly Dictionary<int, double> GetCOTOpenForBar  = new Dictionary<int, double>();
		private readonly Dictionary<int, double> GetCOTCloseForBar = new Dictionary<int, double>();
		private readonly Dictionary<int, double> GetCOTHighForBar  = new Dictionary<int, double>();
		private readonly Dictionary<int, double> GetCOTLowForBar   = new Dictionary<int, double>();
		
		private readonly Dictionary<int, double> cotRunning = new Dictionary<int, double>();
		
		private const int PLOT_COT_HIGH = 28;
		private const int PLOT_COT_LOW  = 29;
		
		private bool tHasFirstPrint = false;

		// Session H/L trackers for session-based divergence
		private double sessionHighPrice = double.MinValue;
		private double sessionLowPrice  = double.MaxValue;
		
		// Parsed lists for combo plots
		private int[] custom1Ids = Array.Empty<int>();
		private int[] custom2Ids = Array.Empty<int>();
		private int[] custom3Ids = Array.Empty<int>();
	

        #endregion

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Custom Footprint Indicator that aggregates Bid, Ask, Delta, Volume, POC and Value Area, plus signals. Plots designed for integration with strategies. - By Alighten";
                Name = "AlightenFootprintOrderFlowV00015";
                Calculate = Calculate.OnEachTick;
				MaximumBarsLookBack                         = MaximumBarsLookBack.TwoHundredFiftySix;
                IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				IsSuspendedWhileInactive					= false;
				ShowTransparentPlotsInDataBox 				= true;

                // --- Add Plots for Values ---
				AddPlot(Brushes.Transparent, "AggDelta");
                AddPlot(Brushes.Transparent, "MinDelta");
                AddPlot(Brushes.Transparent, "MaxDelta");
				AddPlot(Brushes.Transparent, "CumDelta");
                AddPlot(Brushes.Transparent, "TotalVol");
                AddPlot(Brushes.Transparent, "POC");
                AddPlot(Brushes.Transparent, "VAHigh");
                AddPlot(Brushes.Transparent, "VALow");
				AddPlot(Brushes.Transparent, "Ratio");
				AddPlot(Brushes.Transparent, "POCPos");	
				AddPlot(Brushes.Transparent, "DeltaPerVolume");
				
				// --- Add Plots for Signals ---
                AddPlot(Brushes.Transparent, "VolSeq");
                AddPlot(Brushes.Transparent, "StackImb");                
                AddPlot(Brushes.Transparent, "RevPOC");
                AddPlot(Brushes.Transparent, "Sweep");
                AddPlot(Brushes.Transparent, "DeltaSeq");            
				AddPlot(Brushes.Transparent, "Divergence");
				AddPlot(Brushes.Transparent, "DeltaFlip");
				AddPlot(Brushes.Transparent, "StoppingVolume");
				AddPlot(Brushes.Transparent, "Absorption");
				AddPlot(Brushes.Transparent, "Exhaustion");
				AddPlot(Brushes.Transparent, "VAGap");
				AddPlot(Brushes.Transparent, "ExtremeRatio");	
				AddPlot(Brushes.Transparent, "FadingMomentum");	
				
				// --- Add Plots for detailed POC tracking ---
				AddPlot(Brushes.Transparent, "POC_VA_FromLow");     
				AddPlot(Brushes.Transparent, "POC_VA_FromHigh");    
				AddPlot(Brushes.Transparent, "POC_Bar_FromLow");    
				AddPlot(Brushes.Transparent, "POC_Bar_FromHigh");   
				
				AddPlot(Brushes.Transparent, "COTHigh");
			    AddPlot(Brushes.Transparent, "COTLow");
				
				AddPlot(Brushes.Transparent, "CustomPlot1"); // Values[30]
				AddPlot(Brushes.Transparent, "CustomPlot2"); // Values[31]
				AddPlot(Brushes.Transparent, "CustomPlot3"); // Values[32]
				
				AddPlot(Brushes.Transparent, "ExtremeVA");      // Values[33]
				AddPlot(Brushes.Transparent, "POCInWick");      // Values[34]
				AddPlot(Brushes.Transparent, "ExtremePOC");      // Values[35]
				AddPlot(Brushes.Transparent, "VAPOCExtreme");   // Values[36]

            }
            else if (State == State.Configure)
            {	
                // Add volumetric data series (using VolumetricDeltaType.BidAsk)
                //AddVolumetric(Instrument.FullName, BarsPeriod.BarsPeriodType, BarsPeriod.Value, VolumetricDeltaType.BidAsk, 1);
				
				AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                barsType = BarsArray[1].BarsType as NinjaTrader.NinjaScript.BarsTypes.VolumetricBarsType;
                tickSize = TickSize;
				
				custom1Ids = ParsePlotList(CustomPlot1Sources);
    			custom2Ids = ParsePlotList(CustomPlot2Sources);
				custom3Ids = ParsePlotList(CustomPlot3Sources);
            }
        }
        #endregion

        #region OnBarUpdate

		protected override void OnBarUpdate()
		{
		    try
		    {
		        // Require both series to be running
		        if (Bars == null || BarsArray == null || BarsArray.Length < 2)
		            return;
		        if (CurrentBars[0] < 0 || CurrentBars[1] < 0)
		            return;
		
		        // Keep session open for consistent origin if requested
		        if (Bars.IsFirstBarOfSession)
		            sessionOpenPrice = Open[0];
		
		        // Size of each footprint row = N ticks (snap to integer multiple of TickSize)
		        double binInterval = AggregationInterval * TickSize;
		        if (!(binInterval > 0.0) || double.IsNaN(binInterval) || double.IsInfinity(binInterval))
		            return;
		        int ticksPerBin = Math.Max(1, (int)Math.Round(binInterval / TickSize));
		        binInterval = ticksPerBin * TickSize;   // exact grid, avoids floating drift
		
		        // =========================================
		        // 1) TICK STREAM: accumulate per-bar rows
		        // =========================================
		        if (BarsInProgress == 1)
		        {
		            // Handle "in transition" ordering (like the sample)
		            int indexOffset   = BarsArray[1].Count - 1 - CurrentBars[1];
		            bool inTransition = (State == State.Realtime) && (indexOffset > 1);
		            bool useCurrent   = State == State.Historical || inTransition || (Calculate != Calculate.OnBarClose);
		            int  ti           = useCurrent ? CurrentBars[1] : Math.Min(CurrentBars[1] + 1, BarsArray[1].Count - 1);
		
		            DateTime t   = BarsArray[1].GetTime(ti);
		            int      idx = BarsArray[0].GetBar(t);          // map tick time -> primary bar index
		            if (idx < 0 || idx > CurrentBar)
		                return;
		
		            // Ensure per-bar maps exist (do NOT clear here)
		            if (!GetAskVolumeForPrice.ContainsKey(idx))   GetAskVolumeForPrice[idx]   = new Dictionary<double, double>();
		            if (!GetBidVolumeForPrice.ContainsKey(idx))   GetBidVolumeForPrice[idx]   = new Dictionary<double, double>();
		            if (!GetDeltaForPrice.ContainsKey(idx))       GetDeltaForPrice[idx]       = new Dictionary<double, double>();
		            if (!GetTotalVolumeForPrice.ContainsKey(idx)) GetTotalVolumeForPrice[idx] = new Dictionary<double, double>();
		
		            // --- Ensure COT containers exist for this bar (bar-wise COT starts at 0) ---
		            if (!cotRunning.ContainsKey(idx))
		            {
		                cotRunning[idx]        = 0.0;
		                GetCOTOpenForBar[idx]  = 0.0;
		                GetCOTCloseForBar[idx] = 0.0;
		                GetCOTHighForBar[idx]  = 0.0;
		                GetCOTLowForBar[idx]   = 0.0;
		            }
		
		            double price = BarsArray[1].GetClose(ti);
		            double vol   = BarsArray[1].GetVolume(ti);
		
		            // Classify strictly by L1 (ignore mid prints)
		            double askPx = BarsArray[1].GetAsk(ti);
		            double bidPx = BarsArray[1].GetBid(ti);
		            bool   isBuy = (askPx > 0 && price >= askPx);
		            bool   isSell= (bidPx > 0 && price <= bidPx);
		            if (!isBuy && !isSell)
		                return;
		
		            // Anchor grid: session open OR that bar's low (read via barsAgo)
		            int barsAgo = CurrentBar - idx;
		            if (barsAgo < 0 || barsAgo >= Count) return;
		
		            double origin = UseSessionOpenForAggregation ? sessionOpenPrice : Low[barsAgo];
		            origin = Math.Round(origin / TickSize) * TickSize;  // snap to tick grid
		
		            // Compute bin start for this tick, snap to grid
		            double binsFromOrigin = Math.Floor(((price - origin) / binInterval) + 1e-9);
		            double binStart       = origin + binsFromOrigin * binInterval;
		            binStart = Math.Round(binStart / TickSize) * TickSize;
		
		            // Accumulate row volumes
		            double ask = 0, bid = 0;
		            GetAskVolumeForPrice[idx].TryGetValue(binStart, out ask);
		            GetBidVolumeForPrice[idx].TryGetValue(binStart, out bid);
		
		            if (isBuy)  ask += vol;
		            if (isSell) bid += vol;
		
		            GetAskVolumeForPrice[idx][binStart]   = ask;
		            GetBidVolumeForPrice[idx][binStart]   = bid;
		            GetDeltaForPrice[idx][binStart]       = ask - bid;
		            GetTotalVolumeForPrice[idx][binStart] = ask + bid;
		
		            // Bar-level totals incrementally
		            double prevBarDelta = GetDeltaForBar.ContainsKey(idx) ? GetDeltaForBar[idx] : 0.0;
		            double prevBarVol   = GetVolumeForBar.ContainsKey(idx) ? GetVolumeForBar[idx] : 0.0;
		            GetDeltaForBar[idx]  = prevBarDelta + (isBuy ? vol : (isSell ? -vol : 0.0));
		            GetVolumeForBar[idx] = prevBarVol + vol;
		
		            // --------- COT running update (bar-wise cumulative orderflow) ----------
		            double step = isBuy ? vol : (isSell ? -vol : 0.0);
		            if (step != 0.0)
		            {
		                double run = cotRunning[idx] + step;
		                cotRunning[idx] = run;
		
		                // Update intrabar extremes and close
		                if (!GetCOTHighForBar.TryGetValue(idx, out var hi) || run > hi) GetCOTHighForBar[idx] = run;
		                if (!GetCOTLowForBar .TryGetValue(idx, out var lo) || run <  lo) GetCOTLowForBar[idx]  = run;
		                GetCOTCloseForBar[idx] = run;
		
		                // Optional convenience for "current" bar UI (won't break if you already use these)
		                if (idx == CurrentBar)
		                {
		                    if (isBuy) tBuys += vol; 
							else if (isSell) tSells += vol;
							
							tCdClose = tBuys - tSells;
					        if (tCdClose > tCdHigh) tCdHigh = tCdClose;
					        if (tCdClose < tCdLow)  tCdLow  = tCdClose;
							
		                    // first qualifying print of the bar seeds the extrema
//						    if (!tHasFirstPrint)
//						    {
//						        tHasFirstPrint = true;
//						        tCdClose = (isBuy ? vol : -vol); // first step from zero
//						        tCdHigh  = tCdClose;
//						        tCdLow   = tCdClose;
//						    }
//						    else
//						    {
//						        tCdClose = tBuys - tSells;
//						        if (tCdClose > tCdHigh) tCdHigh = tCdClose;
//						        if (tCdClose < tCdLow)  tCdLow  = tCdClose;
//						    }
		                }
		            }
		
		            return; // tick path done
		        }
		
		        // =========================================
		        // 2) PRIMARY STREAM: compute per-bar outputs
		        // =========================================
		        if (BarsInProgress != 0)
		            return;
				
				if (Bars.IsFirstBarOfSession && IsFirstTickOfBar)
				{
				    sessionHighPrice = High[0];
				    sessionLowPrice  = Low[0];
				}
		
		        // Seed COT for a new forming bar so OnRender never sees missing keys
		        if (IsFirstTickOfBar)
		        {
		            // reset "current bar" convenience trackers
		            tBuys = tSells = 0.0;
		            tCdClose = 0.0; tCdHigh = 0.0; tCdLow = 0.0;
					
    				tHasFirstPrint = false;
		
		            if (!cotRunning.ContainsKey(CurrentBar))
		            {
		                cotRunning[CurrentBar]        = 0.0;
		                GetCOTOpenForBar[CurrentBar]  = 0.0;
		                GetCOTCloseForBar[CurrentBar] = 0.0;
		                GetCOTHighForBar[CurrentBar]  = 0.0;
		                GetCOTLowForBar[CurrentBar]   = 0.0;
		            }
		        }
		
		        // Ensure per-bar maps exist (do NOT clear—ticks are still filling them)
		        if (!GetAskVolumeForPrice.ContainsKey(CurrentBar))   GetAskVolumeForPrice[CurrentBar]   = new Dictionary<double, double>();
		        if (!GetBidVolumeForPrice.ContainsKey(CurrentBar))   GetBidVolumeForPrice[CurrentBar]   = new Dictionary<double, double>();
		        if (!GetDeltaForPrice.ContainsKey(CurrentBar))       GetDeltaForPrice[CurrentBar]       = new Dictionary<double, double>();
		        if (!GetTotalVolumeForPrice.ContainsKey(CurrentBar)) GetTotalVolumeForPrice[CurrentBar] = new Dictionary<double, double>();
		
		        var rows = GetTotalVolumeForPrice[CurrentBar];
		
		        // bar metrics from rows
		        barDelta     = GetDeltaForPrice[CurrentBar].Count > 0 ? GetDeltaForPrice[CurrentBar].Values.Sum() : 0.0;
		        totalVolume  = rows.Count > 0 ? rows.Values.Sum() : 0.0;
		        double prevC = GetCumDeltaForBar.ContainsKey(CurrentBar - 1) ? GetCumDeltaForBar[CurrentBar - 1] : 0.0;
		        cumulativeDelta = prevC + barDelta;
		
		        if (GetDeltaForPrice[CurrentBar].Count > 0)
		        {
		            double dMin = GetDeltaForPrice[CurrentBar].Values.Min();
		            double dMax = GetDeltaForPrice[CurrentBar].Values.Max();
		            minDelta = Math.Min(dMin, barDelta);
		            maxDelta = Math.Max(dMax, barDelta);
		        }
		        else
		        {
		            minDelta = barDelta;
		            maxDelta = barDelta;
		        }
		
		        deltaPerVolume = totalVolume > 0 ? barDelta / totalVolume : 0.0;
		
		        // POC + Value Area (simple expansion from POC)
		        if (rows.Count > 0)
		        {
		            var pocPair = rows.Aggregate((a, b) => a.Value > b.Value ? a : b);
		            pocPrice = pocPair.Key;
		
		            vah = val = pocPrice;
		            var ascending = rows.OrderBy(kv => kv.Key).ToList();
		            double targetVA = totalVolume * (ValueAreaPer / 100.0);
		            int pocIdx = ascending.FindIndex(x => x.Key == pocPrice);
		            if (pocIdx < 0) pocIdx = 0;
		
		            double cum = rows.ContainsKey(pocPrice) ? rows[pocPrice] : 0.0;
		            int up = pocIdx + 1, down = pocIdx - 1;
		            while (cum < targetVA && (up < ascending.Count || down >= 0))
		            {
		                double vUp = (up   < ascending.Count) ? ascending[up].Value   : 0.0;
		                double vDn = (down >= 0)              ? ascending[down].Value : 0.0;
		
		                if (up < ascending.Count && (down < 0 || vUp >= vDn)) { cum += vUp; vah = ascending[up].Key;   up++; }
		                else if (down >= 0)                                    { cum += vDn; val = ascending[down].Key; down--; }
		                else break;
		            }
		
		            // compute all derived signals & persist/plot
		            RemainderOnBarUpdate(binInterval);
		        }
		        else
		        {
		            // nothing to compute this bar; still persist zeros/signals consistently
		            RemainderOnBarUpdate(binInterval);
		        }
		    }
		    catch (Exception ex)
		    {
		        Print(ex.ToString());
		    }
		}


        #endregion
		
		#region RemainderOnBarUpdate
		
		private void RemainderOnBarUpdate(double binInterval)
		{
		    // convenience
		    var rows = Row(GetTotalVolumeForPrice, CurrentBar);
		    var asc  = rows?.OrderBy(kv => kv.Key).Select(kv => kv.Key).ToList()
		               ?? new List<double>();
		
		    // --- Delta/Volume: Simple ratio
		    deltaPerVolume = totalVolume > 0 ? barDelta / totalVolume : 0.0;
		
		    // --- POC position & offsets (use existing val/vah/pocPrice set in OnBarUpdate)
		    int totalBins = asc.Count;
		    int pocIdx    = totalBins > 0 ? asc.FindIndex(k => k.Equals(pocPrice)) : -1;
		    if (pocIdx < 0 && totalBins > 0) pocIdx = 0;
		
		    if (totalBins > 1 && pocIdx >= 0)
		    {
		        double frac = (double)pocIdx / (totalBins - 1);
		        pocPos = (frac >= 2.0/3.0) ? 1 : (frac <= 1.0/3.0) ? -1 : 0;
		    }
		    else
		        pocPos = 0;
		
		    int valIdx = totalBins > 0 ? asc.FindIndex(k => k.Equals(val)) : -1;
		    int vahIdx = totalBins > 0 ? asc.FindIndex(k => k.Equals(vah)) : -1;
		    if (valIdx < 0) valIdx = pocIdx;
		    if (vahIdx < 0) vahIdx = pocIdx;
		
		    pocVA_FromLow   = (pocIdx >= 0 && valIdx >= 0) ? (pocIdx - valIdx) : 0;
		    pocVA_FromHigh  = (pocIdx >= 0 && vahIdx >= 0) ? (vahIdx - pocIdx) : 0;
		    pocBar_FromLow  = Math.Max(0, pocIdx);
		    pocBar_FromHigh = (totalBins > 0 && pocIdx >= 0) ? (totalBins - 1 - pocIdx) : 0;
		
		    // ===================== Signals =====================
		
		    // Volume Sequencing
		    if (asc.Count >= VolumeSeqLookback)
		    {
		        bool askSeq = true;
		        for (int i = 1; i < VolumeSeqLookback; i++)
		        {
		            double askPrev = Read(GetAskVolumeForPrice, CurrentBar, asc[i - 1]);
		            double askCurr = Read(GetAskVolumeForPrice, CurrentBar, asc[i]);
		            if (!(askCurr > askPrev)) { askSeq = false; break; }
		        }
		
		        bool bidSeq = true;
		        for (int i = asc.Count - VolumeSeqLookback + 1; i < asc.Count; i++)
		        {
		            double bidBelow = Read(GetBidVolumeForPrice, CurrentBar, asc[i - 1]);
		            double bidNow   = Read(GetBidVolumeForPrice, CurrentBar, asc[i]);
		            if (!(bidBelow > bidNow)) { bidSeq = false; break; }
		        }
		
		        volSeqSignal = askSeq ? 1 : bidSeq ? -1 : 0;
		    }
		    else volSeqSignal = 0;
		
		    // Stacked Imbalance
		    int maxConsecutiveAsk = 0, currentConsecutiveAsk = 0;
		    for (int i = 1; i < asc.Count; i++)
		    {
		        double askCurr = Read(GetAskVolumeForPrice, CurrentBar, asc[i]);
		        double bidPrev = Read(GetBidVolumeForPrice, CurrentBar, asc[i - 1]);
		        currentConsecutiveAsk = (askCurr >= ImbFact * bidPrev) ? currentConsecutiveAsk + 1 : 0;
		        if (currentConsecutiveAsk > maxConsecutiveAsk) maxConsecutiveAsk = currentConsecutiveAsk;
		    }
		
		    int maxConsecutiveBid = 0, currentConsecutiveBid = 0;
		    for (int i = 0; i < asc.Count - 1; i++)
		    {
		        double bidCurr = Read(GetBidVolumeForPrice, CurrentBar, asc[i]);
		        double askNext = Read(GetAskVolumeForPrice, CurrentBar, asc[i + 1]);
		        currentConsecutiveBid = (bidCurr >= ImbFact * askNext) ? currentConsecutiveBid + 1 : 0;
		        if (currentConsecutiveBid > maxConsecutiveBid) maxConsecutiveBid = currentConsecutiveBid;
		    }
		
		    if      (maxConsecutiveAsk >= StackedImbalanceLookback) stackedImbSignal =  1;
		    else if (maxConsecutiveBid >= StackedImbalanceLookback) stackedImbSignal = -1;
		    else                                                    stackedImbSignal =  0;
		
		    // Reversal POC
		    if (CurrentBar >= 1)
		    {
		        bool prevBarGreen = Closes[0][1] >= Opens[0][1];
		        bool prevBarRed   = Closes[0][1] <  Opens[0][1];
		        bool currBarGreen = Closes[0][0] >= Opens[0][0];
		        bool currBarRed   = Closes[0][0] <  Opens[0][0];
		
		        if (prevBarRed && currBarGreen && Low[0]  < Low[1] && pocPos == -1) reversalPOCSignal =  1;
		        else if (prevBarGreen && currBarRed && High[0] > High[1] && pocPos == 1) reversalPOCSignal = -1;
		        else reversalPOCSignal = 0;
		    }
		    else reversalPOCSignal = 0;
		
		    // Sweep
		    int consAskLow = 0, consBidLow = 0;
		    foreach (var k in asc)
		    {
		        double askVol = Read(GetAskVolumeForPrice, CurrentBar, k);
		        double bidVol = Read(GetBidVolumeForPrice, CurrentBar, k);
		        consAskLow = (askVol <= NearZeroaThreshold) ? consAskLow + 1 : 0;
		        consBidLow = (bidVol <= NearZeroaThreshold) ? consBidLow + 1 : 0;
		    }
		    if      (consAskLow >= SweepLookback) sweepSignal = -1;
		    else if (consBidLow >= SweepLookback) sweepSignal =  1;
		    else                                   sweepSignal =  0;
		
		    // Delta Sequence (use GetDeltaForBar history)
		    if (CurrentBar >= DeltaSequenceLookback)
		    {
		        bool isIncreasing = true, isDecreasing = true;
		        for (int i = CurrentBar - DeltaSequenceLookback + 1; i <= CurrentBar; i++)
		        {
		            double prevDelta = GetDeltaForBar.TryGetValue(i - 1, out var pd) ? pd : 0;
		            double currDelta = GetDeltaForBar.TryGetValue(i,     out var cd) ? cd : 0;
		            if (currDelta <= prevDelta) isIncreasing = false;
		            if (currDelta >= prevDelta) isDecreasing = false;
		        }
		        if (isIncreasing) { deltaSeqSignal =  1; deltaSignal = "I"; }
		        else if (isDecreasing) { deltaSeqSignal = -1; deltaSignal = "D"; }
		        else { deltaSeqSignal = 0; deltaSignal = ""; }
		    }
		    else { deltaSeqSignal = 0; deltaSignal = ""; }
		
		    // Bar Ratio (use adjacent keys, not arithmetic offsets)
			ratio = double.NaN;           
			extremeRatioSignal = 0;
		    if (Close[0] > Open[0])
		    {
		        if (asc.Count >= 2)
		        {
		            double baseVol = Read(GetBidVolumeForPrice, CurrentBar, asc[0]);
		            double nextVol = Read(GetBidVolumeForPrice, CurrentBar, asc[1]);
		            if (baseVol > 0) ratio = nextVol / baseVol;
		        }
		    }
		    else if (Close[0] < Open[0])
		    {
		        if (asc.Count >= 2)
		        {
		            double baseVol = Read(GetAskVolumeForPrice, CurrentBar, asc[asc.Count - 1]);     // top
		            double prevVol = Read(GetAskVolumeForPrice, CurrentBar, asc[asc.Count - 2]);     // one below
		            if (baseVol > 0) ratio = prevVol / baseVol;
		        }
		    }
		    if (!double.IsNaN(ratio) &&
		        (ratio <= SmallRatioThreshold || ratio >= LargeRatioThreshold))
		        extremeRatioSignal = Close[0] > Open[0] ? +1 : -1;
		
		    // Divergence (mode-aware)
			if (UseSessionHiLoDivergence)
			{
			    // Detect if THIS bar makes a fresh session extreme *before* updating the rolling extremes.
			    bool makesNewSessionHigh = High[0] > sessionHighPrice;
			    bool makesNewSessionLow  = Low[0]  < sessionLowPrice;
			
			    int sig = 0;
			
			    // Bearish divergence at a fresh session high (price made new HOD, but delta is non-supportive)
			    // Choose one of the two lines below to taste:
			    // 1) strict sign flip:
			    // if (makesNewSessionHigh && barDelta <= -NearZeroaThreshold) sig = -1;
			    // 2) stronger threshold:
			    if (makesNewSessionHigh && barDelta <= -DeltaThreshold1) sig = -1;
			
			    // Bullish divergence at a fresh session low (price made new LOD, but delta is non-supportive)
			    // 1) strict sign flip:
			    // if (makesNewSessionLow && barDelta >=  NearZeroaThreshold) sig = +1;
			    // 2) stronger threshold:
			    if (makesNewSessionLow && barDelta >=  DeltaThreshold1) sig = +1;
			
			    divergenceSignal = sig;
			
			    // Now advance the rolling session extremes to include this bar
			    sessionHighPrice = Math.Max(sessionHighPrice, High[0]);
			    sessionLowPrice  = Math.Min(sessionLowPrice,  Low[0]);
			}
			else
			{
			    // Original bar-based divergence (unchanged)
			    if (CurrentBar >= DivergenceLookback)
			    {
			        int lowestIndex  = LowestBar(Low,  DivergenceLookback);
			        int highestIndex = HighestBar(High, DivergenceLookback);
			        if (lowestIndex == 0 && Close[0] >= Open[0] && barDelta < 0)      divergenceSignal =  1;
			        else if (highestIndex == 0 && Close[0] < Open[0] && barDelta > 0) divergenceSignal = -1;
			        else                                                              divergenceSignal =  0;
			    }
			    else divergenceSignal = 0;
			
			    // Keep the rolling extremes fresh for whenever the user flips the mode on
			    sessionHighPrice = (Bars.IsFirstBarOfSession && IsFirstTickOfBar) ? High[0] : Math.Max(sessionHighPrice, High[0]);
			    sessionLowPrice  = (Bars.IsFirstBarOfSession && IsFirstTickOfBar) ? Low[0]  : Math.Min(sessionLowPrice,  Low[0]);
			}

		
		    // Delta Flip
		    if (CurrentBar >= DeltaFlipLookback &&
		        GetDeltaForBar.ContainsKey(CurrentBar - 1) &&
		        GetDeltaForBar.ContainsKey(CurrentBar))
		    {
		        double bar1Delta = GetDeltaForBar[CurrentBar - 1];
		        double bar2Delta = GetDeltaForBar[CurrentBar];
		        double thresh    = DeltaThreshold1;
		
		        bool bullishFlip = (bar1Delta < -thresh) && (bar2Delta >  thresh);
		        bool bearishFlip = (bar1Delta >  thresh) && (bar2Delta < -thresh);
		        deltaFlipSignal = bullishFlip ?  1 : bearishFlip ? -1 : 0;
		    }
		    else deltaFlipSignal = 0;
		
		    // Stopping Volume
		    if (asc.Count >= 2 && CurrentBar >= StoppingVolumeLookback)
		    {
		        double lowKey1  = asc[0];
		        double lowKey2  = asc[1];
		        double highKey1 = asc[asc.Count - 1];
		        double highKey2 = asc[asc.Count - 2];
		
		        double currLowBid  = Read(GetBidVolumeForPrice, CurrentBar, lowKey1)
		                           + Read(GetBidVolumeForPrice, CurrentBar, lowKey2);
		        double currHighAsk = Read(GetAskVolumeForPrice, CurrentBar, highKey1)
		                           + Read(GetAskVolumeForPrice, CurrentBar, highKey2);
		
		        double maxPrevLowBid = 0, maxPrevHighAsk = 0;
		        for (int i = CurrentBar - StoppingVolumeLookback; i < CurrentBar; i++)
		        {
		            var prevRows = Row(GetTotalVolumeForPrice, i);
		            if (prevRows == null || prevRows.Count < 2) continue;
		            var prevAsc = prevRows.OrderBy(k => k.Key).Select(k => k.Key).ToList();
		            double pLow1  = Read(GetBidVolumeForPrice, i, prevAsc[0]);
		            double pLow2  = Read(GetBidVolumeForPrice, i, prevAsc[1]);
		            double pHigh1 = Read(GetAskVolumeForPrice, i, prevAsc[prevAsc.Count - 1]);
		            double pHigh2 = Read(GetAskVolumeForPrice, i, prevAsc[prevAsc.Count - 2]);
		            maxPrevLowBid  = Math.Max(maxPrevLowBid,  pLow1  + pLow2);
		            maxPrevHighAsk = Math.Max(maxPrevHighAsk, pHigh1 + pHigh2);
		        }
		
		        bool isGreen = Closes[0][0] >= Opens[0][0];
		        bool isRed   = !isGreen;
		
		        stopVolSignal = 0;
		        if (isGreen && pocBar_FromLow  <= 1 && currLowBid  > maxPrevLowBid)  stopVolSignal =  1;
		        else if (isRed && pocBar_FromHigh <= 1 && currHighAsk > maxPrevHighAsk) stopVolSignal = -1;
		    }
		    else stopVolSignal = 0;
		
		    // Absorption
		    if (minDelta < -DeltaThreshold1 && barDelta > -NearZeroaThreshold)      absorptionSignal =  1;
		    else if (maxDelta > DeltaThreshold1 && barDelta <  NearZeroaThreshold)  absorptionSignal = -1;
		    else                                                                     absorptionSignal =  0;
		
		    // Exhaustion
		    if (asc.Count > 0)
		    {
		        double lowestBin  = asc[0];
		        double highestBin = asc[asc.Count - 1];
		        if (Close[0] < Open[0]) // red
		        {
		            double askVolAtHigh = Read(GetAskVolumeForPrice, CurrentBar, highestBin);
		            exhaustionSignal = (askVolAtHigh < ExhaustionThreshold) ? -1 : 0;
		        }
		        else // green
		        {
		            double bidVolAtLow = Read(GetBidVolumeForPrice, CurrentBar, lowestBin);
		            exhaustionSignal = (bidVolAtLow < ExhaustionThreshold) ?  1 : 0;
		        }
		    }
		    else exhaustionSignal = 0;
		
		    // VA gap vs previous bar (guard missing keys)
		    if (CurrentBar >= 1)
		    {
		        double prevVAH = GetVAHForBar.TryGetValue(CurrentBar - 1, out var pVah) ? pVah : double.NaN;
		        double prevVAL = GetVALForBar.TryGetValue(CurrentBar - 1, out var pVal) ? pVal : double.NaN;
		        if (!double.IsNaN(prevVAH) && !double.IsNaN(prevVAL))
		        {
		            if      (val > prevVAH + binInterval) vaGapSignal =  1;
		            else if (vah < prevVAL - binInterval) vaGapSignal = -1;
		            else                                   vaGapSignal =  0;
		        }
		        else vaGapSignal = 0;
		    }
		    else vaGapSignal = 0;
		
		    // Fading Momentum (use GetDeltaForBar)
		    if (CurrentBar >= FadingMomentumLookback)
		    {
		        int look = FadingMomentumLookback;
		
		        bool bearishFade = true; // positive deltas strictly decreasing
		        for (int i = CurrentBar - look + 1; i <= CurrentBar; i++)
		            if ((GetDeltaForBar.TryGetValue(i, out var d) ? d : 0) <= 0) { bearishFade = false; break; }
		        if (bearishFade)
		            for (int i = CurrentBar - look + 2; i <= CurrentBar; i++)
		                if ((GetDeltaForBar.TryGetValue(i, out var d2) ? d2 : 0)
		                    >= (GetDeltaForBar.TryGetValue(i - 1, out var d1) ? d1 : 0)) { bearishFade = false; break; }
		
		        bool bullishFade = true; // negative deltas strictly increasing
		        for (int i = CurrentBar - look + 1; i <= CurrentBar; i++)
		            if ((GetDeltaForBar.TryGetValue(i, out var d) ? d : 0) >= 0) { bullishFade = false; break; }
		        if (bullishFade)
		            for (int i = CurrentBar - look + 2; i <= CurrentBar; i++)
		                if ((GetDeltaForBar.TryGetValue(i, out var d2) ? d2 : 0)
		                    <= (GetDeltaForBar.TryGetValue(i - 1, out var d1) ? d1 : 0)) { bullishFade = false; break; }
		
		        fadingMomentumSignal = bearishFade ? -1 : bullishFade ? 1 : 0;
		    }
		    else fadingMomentumSignal = 0;
			
			// --- COT (running intrabar delta extremes) -----------------
			// Prefer live intrabar trackers for the live bar; otherwise use any
			// previously stored values, and if none, fall back to row extrema.
			double cotHigh, cotLow;
			
			if (CurrentBar == Bars.Count - 1 && State != State.Historical)
			{
			    cotHigh = tCdHigh;
			    cotLow  = tCdLow;
			}
			else
			{
			    cotHigh = GetCOTHighForBar.TryGetValue(CurrentBar, out var ch) ? ch : maxDelta;
			    cotLow  = GetCOTLowForBar .TryGetValue(CurrentBar, out var cl) ? cl : minDelta;
			}	
			
			// --- VA-Extreme, POC-in-Wick, Extreme-POC & VA+POC-Extreme (signals + drawing) ---
			extremeVASignal     = 0;
			pocInWickSignal     = 0;
			extremePOCSignal    = 0;
			vapocExtremeSignal  = 0;
			
			// Assumptions: asc (sorted price bins), val, vah, pocPrice, totalBins, valIdx, vahIdx, pocIdx are already computed.
			bool haveBins = totalBins > 0 && valIdx >= 0 && vahIdx >= 0 && pocIdx >= 0;
			
			// "Extreme" = first (low) or last (high) footprint row of the bar
			bool vaAtLowExtreme   = haveBins && (valIdx == 0);
			bool vaAtHighExtreme  = haveBins && (vahIdx == totalBins - 1);
			bool pocAtLowExtreme  = haveBins && (pocIdx == 0);
			bool pocAtHighExtreme = haveBins && (pocIdx == totalBins - 1);
			
			// ---------- Standard signal: Extreme VA ----------
			if (haveBins)
			{
			    if (vaAtLowExtreme)      extremeVASignal  = +1;
			    else if (vaAtHighExtreme)extremeVASignal  = -1;
			}
			
			// ---------- Standard signal: POC in Wick ----------
			if (haveBins)
			{
			    // Body bounds (include doji as zero-height body)
			    double bodyLow  = Math.Min(Open[0], Close[0]);
			    double bodyHigh = Math.Max(Open[0], Close[0]);
			
			    if (pocPrice < bodyLow)       pocInWickSignal  = +1; // in lower wick
			    else if (pocPrice > bodyHigh) pocInWickSignal  = -1; // in upper wick
			    else                          pocInWickSignal  =  0; // inside body
			}
			
			// ---------- Standard signal: Extreme POC ----------
			if (haveBins)
			{
			    if (pocAtLowExtreme)       extremePOCSignal = +1;
			    else if (pocAtHighExtreme) extremePOCSignal = -1;
			}
			
			// Draw positions for triangle helpers (which apply tick-based Y offsets internally)
			double bullPriceY = Low[0];
			double bearPriceY = High[0];
			
			// Per-bar unique tags (include CurrentBar here, once)
			string baseTagCombo = $"VAPOC_{CurrentBar}";
			string bullTag      = baseTagCombo + "_B";
			string bearTag      = baseTagCombo + "_S";
			
			// Only mutate drawings for the currently forming bar
			bool isLiveBar = (CurrentBar == Bars.Count - 1);
			
			vapocExtremeSignal = 0;
			
			if (!EnableVAPOCExtremeComboSignal)
			{
			    // If disabled, optionally clear any live-drawn triangles for this bar
			    if (isLiveBar)
			    {
			        RemoveDrawObject(bullTag);
			        RemoveDrawObject(bearTag);
			    }
			}
			else if (!haveBins)
			{
			    // No bins to evaluate; clear on the live bar
			    if (isLiveBar)
			    {
			        RemoveDrawObject(bullTag);
			        RemoveDrawObject(bearTag);
			    }
			}
			else
			{
			    bool bullCombo = vaAtLowExtreme  && pocAtLowExtreme;
			    bool bearCombo = vaAtHighExtreme && pocAtHighExtreme;
			
			    if (bullCombo)
			    {
			        DrawTriangleUp(bullTag, 0, bullPriceY, VAPOCBullColor);
			        vapocExtremeSignal = +1;
			
			        // If the opposite was drawn earlier this bar, remove it
			        if (isLiveBar) RemoveDrawObject(bearTag);
			    }
			    else if (bearCombo)
			    {
			        DrawTriangleDown(bearTag, 0, bearPriceY, VAPOCBearColor);
			        vapocExtremeSignal = -1;
			
			        if (isLiveBar) RemoveDrawObject(bullTag);
			    }
			    else
			    {
			        // Condition no longer true on the forming bar → remove both
			        if (isLiveBar)
			        {
			            RemoveDrawObject(bullTag);
			            RemoveDrawObject(bearTag);
			        }
			        // signal stays 0
			    }
			}


		
		    // ===================== Persist & Plot =====================
		
		    // Dictionaries for render/table
		    GetDeltaForBar[CurrentBar]          = barDelta;
		    GetMaxDeltaForBar[CurrentBar]       = maxDelta;
		    GetMinDeltaForBar[CurrentBar]       = minDelta;
		    GetCumDeltaForBar[CurrentBar]       = cumulativeDelta;
		    GetVolumeForBar[CurrentBar]         = totalVolume;
		    GetPOCForBar[CurrentBar]            = pocPrice;
		    GetVAHForBar[CurrentBar]            = vah;
		    GetVALForBar[CurrentBar]            = val;
		    GetRatioForBar[CurrentBar]          = ratio;
		    GetDeltaPerVolumeForBar[CurrentBar] = deltaPerVolume;
		
		    GetDeltaSignalsForBar[CurrentBar]   = deltaSignal;
		
		    GetVolSeqForBar[CurrentBar]         = volSeqSignal;
		    GetStackedImbForBar[CurrentBar]     = stackedImbSignal;
		    GetReversalPOCForBar[CurrentBar]    = reversalPOCSignal;
		    GetSweepForBar[CurrentBar]          = sweepSignal;
		    GetDeltaSeqForBar[CurrentBar]       = deltaSeqSignal;
		    GetDivergenceForBar[CurrentBar]     = divergenceSignal;
		    GetDeltaFlipForBar[CurrentBar]      = deltaFlipSignal;
		    GetStoppingVolumeForBar[CurrentBar] = stopVolSignal;
		    GetAbsorptionForBar[CurrentBar]     = absorptionSignal;
		    GetExhaustionForBar[CurrentBar]     = exhaustionSignal;
		    GetVAGapForBar[CurrentBar]          = vaGapSignal;
		    GetExtremeRatioForBar[CurrentBar]   = extremeRatioSignal;
		    GetFadingMomentumForBar[CurrentBar] = fadingMomentumSignal;
		
		    GetPocVAFromLow  [CurrentBar]       = pocVA_FromLow;
		    GetPocVAFromHigh [CurrentBar]       = pocVA_FromHigh;
		    GetPocBarFromLow [CurrentBar]       = pocBar_FromLow;
		    GetPocBarFromHigh[CurrentBar]       = pocBar_FromHigh;
			
			GetCOTHighForBar[CurrentBar] 		= cotHigh;
			GetCOTLowForBar [CurrentBar] 		= cotLow;
		
		    // Plots
		    Values[0][0]  = barDelta;
		    Values[1][0]  = minDelta;
		    Values[2][0]  = maxDelta;
		    Values[3][0]  = cumulativeDelta;
		    Values[4][0]  = totalVolume;
		    Values[5][0]  = pocPrice;
		    Values[6][0]  = vah;
		    Values[7][0]  = val;
		    Values[8][0]  = ratio;
		    Values[9][0]  = pocPos;
		    Values[10][0] = deltaPerVolume;
		
		    Values[11][0] = volSeqSignal;
		    Values[12][0] = stackedImbSignal;
		    Values[13][0] = reversalPOCSignal;
		    Values[14][0] = sweepSignal;
		    Values[15][0] = deltaSeqSignal;
		    Values[16][0] = divergenceSignal;
		    Values[17][0] = deltaFlipSignal;
		    Values[18][0] = stopVolSignal;
		    Values[19][0] = absorptionSignal;
		    Values[20][0] = exhaustionSignal;
		    Values[21][0] = vaGapSignal;
		    Values[22][0] = extremeRatioSignal;
		    Values[23][0] = fadingMomentumSignal;
		
		    Values[24][0] = pocVA_FromLow;
		    Values[25][0] = pocVA_FromHigh;
		    Values[26][0] = pocBar_FromLow;
		    Values[27][0] = pocBar_FromHigh;
			
			Values[28][0] = cotHigh;   
			Values[29 ][0] = cotLow;    
			
			Values[33][0] = extremeVASignal;     // "VAExtreme"
			Values[34][0] = pocInWickSignal;     // "VAExtreme"
			Values[35][0] = extremePOCSignal;     // "VAExtreme"
			Values[36][0] = vapocExtremeSignal;  // "VAPOCExtreme
			
			int custom1 = CombineSignals(custom1Ids);
			int custom2 = CombineSignals(custom2Ids);
			int custom3 = CombineSignals(custom3Ids);
			
			Values[30][0] = custom1;
			Values[31][0] = custom2;
			Values[32][0] = custom3;
			
			
		
		    // Optional diamonds
		    if (EnableVolSeqSignal)         DrawSignalDiamond(CurrentBar, "VolSeq",        volSeqSignal,        VolSeqColor,        VolSeqDiamondOffset);
		    if (EnableStackedImbSignal)     DrawSignalDiamond(CurrentBar, "StackedImb",    stackedImbSignal,    StackedImbColor,    StackedImbDiamondOffset);
		    if (EnableReversalPOCSignal)    DrawSignalDiamond(CurrentBar, "ReversalPOC",   reversalPOCSignal,   RevPOCColor,        ReversalPOCDiamondOffset);
		    if (EnableSweepSignal)          DrawSignalDiamond(CurrentBar, "Sweep",         sweepSignal,         SweepColor,         SweepDiamondOffset);
		    if (EnableDeltaSeqSignal)       DrawSignalDiamond(CurrentBar, "DeltaSeq",      deltaSeqSignal,      DeltaSeqColor,      DeltaSeqDiamondOffset);
		    if (EnableDivergenceSignal)     DrawSignalDiamond(CurrentBar, "Divergence",    divergenceSignal,    DivergenceColor,    DivergenceDiamondOffset);
		    if (EnableDeltaFlipSignal)      DrawSignalDiamond(CurrentBar, "DeltaFlip",     deltaFlipSignal,     DeltaFlipColor,     DeltaFlipDiamondOffset);
		    if (EnableStoppingVolumeSignal) DrawSignalDiamond(CurrentBar, "StopVolTrap",   stopVolSignal,       StoppingVolumeColor,StoppingVolumeDiamondOffset);
		    if (EnableAbsorptionSignal)     DrawSignalDiamond(CurrentBar, "Absorption",    absorptionSignal,    AbsorptionColor,    AbsorptionDiamondOffset);
		    if (EnableExhaustionSignal)     DrawSignalDiamond(CurrentBar, "Exhaustion",    exhaustionSignal,    ExhaustionColor,    ExhaustionDiamondOffset);
		    if (EnableVAGapSignal)          DrawSignalDiamond(CurrentBar, "VAGap",         vaGapSignal,         VAGapColor,         VAGapDiamondOffset);
		    if (EnableExtremeRatioSignal)   DrawSignalDiamond(CurrentBar, "ExtremeRatio",  extremeRatioSignal,  ExtremeRatioColor,  ExtremeRatioDiamondOffset);
		    if (EnableFadingMomentumSignal) DrawSignalDiamond(CurrentBar, "FadingMomentum",fadingMomentumSignal,FadingMomentumColor,FadingMomentumDiamondOffset);
			
			if (EnableExtremeVASignal) 		DrawSignalDiamond(CurrentBar, "ExtremeVA", 		extremeVASignal, ExtremeVAColor, ExtremeVADiamondOffset);
			if (EnablePOCInWickSignal) 		DrawSignalDiamond(CurrentBar, "POCInWick",		pocInWickSignal, POCInWickColor, POCInWickDiamondOffset);
			if (EnableExtremePOCSignal) 	DrawSignalDiamond(CurrentBar, "ExtremePOC",		extremePOCSignal,ExtremePOCColor,ExtremePOCDiamondOffset);
			
			if (EnableCustomPlot1Signal)	DrawSignalDiamond(CurrentBar, "Custom1", custom1, CustomPlot1Color, CustomPlot1DiamondOffset);
			if (EnableCustomPlot2Signal)	DrawSignalDiamond(CurrentBar, "Custom2", custom2, CustomPlot2Color, CustomPlot2DiamondOffset);
			if (EnableCustomPlot3Signal)	DrawSignalDiamond(CurrentBar, "Custom3", custom3, CustomPlot3Color, CustomPlot3DiamondOffset);
			
			
		}

		#endregion
		
		#region OnRender and DrawScrollingGrid and DrawLegend
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
		    base.OnRender(chartControl, chartScale);
		    if (RenderTarget == null || ChartBars == null)
		        return;
		
		    // Visible bars (absolute indices)
		    int firstBar = ChartBars.FromIndex;
		    int lastBar  = ChartBars.ToIndex;
		
		    bool renderAnyPerBar = EnableFootprint || EnableSignalGrid || EnableDeltaOnBar;
		    if (!renderAnyPerBar)
		    {
		        // Still allow non-per-bar overlays
		        if (EnableSignalGridLegend)
		            DrawLegend(chartControl, chartScale);
		
		        if (EnableSummaryGrid)
		            DrawScrollingGrid(chartControl, chartScale);
		
		        return;
		    }
		
		    // ----------------------------
		    // Delta-on-Bar resources (per-render)
		    // ----------------------------
		    SharpDX.DirectWrite.TextFormat      deltaTextFormat = null;
		    SharpDX.Direct2D1.SolidColorBrush   deltaPosBrush   = null;
		    SharpDX.Direct2D1.SolidColorBrush   deltaNegBrush   = null;
		
		    try
		    {
		        if (EnableDeltaOnBar)
		        {
		            deltaTextFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", DeltaOnBarFontSize)
		            {
		                TextAlignment      = SharpDX.DirectWrite.TextAlignment.Center,
		                ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center,
		                WordWrapping       = SharpDX.DirectWrite.WordWrapping.NoWrap
		            };
		
		            deltaPosBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(DeltaOnBarPositiveColor));
		            deltaNegBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(DeltaOnBarNegativeColor));
		        }
		
		        // If footprint is enabled, keep its existing render path and piggyback SignalGrid / DeltaOnBar per bar
		        if (EnableFootprint)
		        {
		            float  alpha          = VAOpacity / 100f;
		            var    baseVAUpColor   = ConvertMediaBrushToColor4(BarUpVAColor);
		            var    baseVADownColor = ConvertMediaBrushToColor4(BarDownVAColor);
		            var    vaUpFinal       = new Color4(baseVAUpColor.Red, baseVAUpColor.Green, baseVAUpColor.Blue, alpha);
		            var    vaDownFinal     = new Color4(baseVADownColor.Red, baseVADownColor.Green, baseVADownColor.Blue, alpha);
		
		            // prepare our brushes and text format
		            using (var textFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", StandardFontSize)
		            {
		                TextAlignment      = SharpDX.DirectWrite.TextAlignment.Center,
		                ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center,
		                WordWrapping       = SharpDX.DirectWrite.WordWrapping.NoWrap
		            })
		            using (var vaBrushUp       = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, vaUpFinal))
		            using (var vaBrushDown     = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, vaDownFinal))
		            using (var pocFillBrush    = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(POCColor)))
		            using (var pocOutlineBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(POCOutlineColor)))
		            {
		                // occupy 80% of bar distance
		                float barWidth      = (float)chartControl.BarWidth;
		                float totalBarWidth = (float)chartControl.Properties.BarDistance * 0.8f;
		                float halfWidth     = totalBarWidth / 2f;
		                float textHeight    = 24f;  // matches your TextLayout height
		
		                for (int barIndex = firstBar; barIndex <= lastBar; barIndex++)
		                {
		                    // center X of this bar (compute once)
		                    float xBar = chartControl.GetXByBarIndex(ChartBars, barIndex);
		
		                    // --- Footprint draw (only if aggregated data exists for this bar)
		                    if (GetTotalVolumeForPrice != null && GetTotalVolumeForPrice.ContainsKey(barIndex))
		                    {
		                        var    aggTotal = GetTotalVolumeForPrice[barIndex];
		                        var    aggBid   = GetBidVolumeForPrice[barIndex];
		                        var    aggAsk   = GetAskVolumeForPrice[barIndex];
		                        double poc      = GetPOCForBar[barIndex];
		                        double VAH      = GetVAHForBar[barIndex];
		                        double VAL      = GetVALForBar[barIndex];
		
		                        // sort bins by price
		                        var ascendingBins = aggTotal.OrderBy(kv => kv.Key).ToList();
		                        if (ascendingBins.Count > 0)
		                        {
		                            // bar direction (use the chart's bars for the panel)
		                            if (ChartBars?.Bars != null && barIndex >= 0 && barIndex < ChartBars.Bars.Count)
		                            {
		                                double open  = ChartBars.Bars.GetOpen(barIndex);
		                                double close = ChartBars.Bars.GetClose(barIndex);
		                                bool   barIsUp = close >= open;
		
		                                // 1) draw Value-Area & POC slabs
		                                foreach (var kv in ascendingBins)
		                                {
		                                    double price = kv.Key;
		                                    float  yTop  = chartScale.GetYByValue(price + AggregationInterval * TickSize);
		                                    float  yBot  = chartScale.GetYByValue(price);
		                                    var    rect  = new RectangleF(
		                                        xBar - totalBarWidth / 2f,
		                                        yTop,
		                                        totalBarWidth,
		                                        yBot - yTop);
		
		                                    // VA shading
		                                    if (price >= VAL && price <= VAH)
		                                        RenderTarget.FillRectangle(rect, barIsUp ? vaBrushUp : vaBrushDown);
		
		                                    // POC slab + outline
		                                    if (Math.Abs(price - poc) < TickSize * 0.5)
		                                    {
		                                        RenderTarget.FillRectangle(rect, pocFillBrush);
		                                        RenderTarget.DrawRectangle(rect, pocOutlineBrush, 2f);
		                                    }
		                                }
		
		                                // 2) draw bid/ask text + imbalance tint
		                                if (EnableFootprintText)
		                                {
		                                    var defaultColor4  = ConvertMediaBrushToColor4(DefaultFootprintTextColor);
		                                    var positiveColor4 = ConvertMediaBrushToColor4(PositiveImbalanceTextColor);
		                                    var negativeColor4 = ConvertMediaBrushToColor4(NegativeImbalanceTextColor);
		
		                                    for (int i = 0; i < ascendingBins.Count; i++)
		                                    {
		                                        double price  = ascendingBins[i].Key;
		                                        int    bidVol = aggBid.ContainsKey(price) ? (int)aggBid[price] : 0;
		                                        int    askVol = aggAsk.ContainsKey(price) ? (int)aggAsk[price] : 0;
		
		                                        // detect diagonal imbalances
		                                        bool isAskImb = false, isBidImb = false;
		                                        if (i > 0)
		                                        {
		                                            double pBelow = ascendingBins[i - 1].Key;
		                                            int    bBelow = aggBid.ContainsKey(pBelow) ? (int)aggBid[pBelow] : 0;
		                                            if (askVol >= ImbFact * bBelow) isAskImb = true;
		                                        }
		                                        if (i < ascendingBins.Count - 1)
		                                        {
		                                            double pAbove = ascendingBins[i + 1].Key;
		                                            int    aAbove = aggAsk.ContainsKey(pAbove) ? (int)aggAsk[pAbove] : 0;
		                                            if (bidVol >= ImbFact * aAbove) isBidImb = true;
		                                        }
		
		                                        // vertical center of this bin
		                                        float yTop = chartScale.GetYByValue(price + AggregationInterval * TickSize);
		                                        float yBot = chartScale.GetYByValue(price);
		                                        float yMid = (yTop + yBot) * 0.5f - textHeight * 0.5f;
		
		                                        // choose tinted brushes
		                                        using (var bidBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, isBidImb ? negativeColor4 : defaultColor4))
		                                        using (var askBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, isAskImb ? positiveColor4 : defaultColor4))
		                                        {
		                                            // draw bid in left half
		                                            using (var bidLayout = new TextLayout(Core.Globals.DirectWriteFactory, bidVol.ToString(), textFormat, halfWidth, textHeight))
		                                                RenderTarget.DrawTextLayout(new Vector2(xBar - halfWidth, yMid), bidLayout, bidBrush);
		
		                                            // draw ask in right half
		                                            using (var askLayout = new TextLayout(Core.Globals.DirectWriteFactory, askVol.ToString(), textFormat, halfWidth, textHeight))
		                                                RenderTarget.DrawTextLayout(new Vector2(xBar, yMid), askLayout, askBrush);
		                                        }
		                                    }
		                                }
		
		                                DrawCandlestickOverlay(barIndex, chartControl, chartScale, barWidth);
		                            }
		                        }
		                    }
		
		                    // --- Per-bar overlays that should work independently of footprint availability
		                    if (EnableSignalGrid)
		                        DrawSignalGridForBar(chartControl, chartScale, barIndex, xBar);
		
		                    if (EnableDeltaOnBar)
		                        DrawDeltaOnBar(chartControl, chartScale, barIndex, xBar, deltaTextFormat, deltaPosBrush, deltaNegBrush);
		                }
		            }
		        }
		        else
		        {
		            // No footprint: single loop for SignalGrid and/or DeltaOnBar
		            for (int barIndex = firstBar; barIndex <= lastBar; barIndex++)
		            {
		                float xBar = chartControl.GetXByBarIndex(ChartBars, barIndex);
		
		                if (EnableSignalGrid)
		                    DrawSignalGridForBar(chartControl, chartScale, barIndex, xBar);
		
		                if (EnableDeltaOnBar)
		                    DrawDeltaOnBar(chartControl, chartScale, barIndex, xBar, deltaTextFormat, deltaPosBrush, deltaNegBrush);
		            }
		        }
		    }
		    finally
		    {
		        if (deltaNegBrush != null) { deltaNegBrush.Dispose(); deltaNegBrush = null; }
		        if (deltaPosBrush != null) { deltaPosBrush.Dispose(); deltaPosBrush = null; }
		        if (deltaTextFormat != null) { deltaTextFormat.Dispose(); deltaTextFormat = null; }
		    }
		
		    // End-of-render overlays (outside bar loop)
		    if (EnableSignalGridLegend)
		        DrawLegend(chartControl, chartScale);
		
		    if (EnableSummaryGrid)
		        DrawScrollingGrid(chartControl, chartScale);
		}
		
		private void DrawDeltaOnBar(
		    ChartControl chartControl,
		    ChartScale chartScale,
		    int barIndex,
		    float xBar,
		    SharpDX.DirectWrite.TextFormat tfDelta,
		    SharpDX.Direct2D1.Brush posBrush,
		    SharpDX.Direct2D1.Brush negBrush)
		{
		    if (!EnableDeltaOnBar)
		        return;
		
		    if (tfDelta == null || posBrush == null || negBrush == null)
		        return;
		
		    if (GetDeltaForBar == null || !GetDeltaForBar.TryGetValue(barIndex, out double deltaVal))
		        return;
		
		    if (ChartBars?.Bars == null || barIndex < 0 || barIndex >= ChartBars.Bars.Count)
		        return;
		
		    double high = ChartBars.Bars.GetHigh(barIndex);
		
		    float cellWidth  = (float)chartControl.Properties.BarDistance;
		    float cellHeight = Math.Max(18f, (float)DeltaOnBarFontSize + 8f);
		
		    // Anchor just above the bar's high, then apply user offset upward.
		    float yTop = chartScale.GetYByValue(high) - (float)DeltaOnBarOffset - cellHeight;
		
		    string text = (deltaVal >= 0)
		        ? $"Δ {deltaVal:N0}"
		        : $"Δ {deltaVal:N0}";
		
		    var rect = new RectangleF(xBar - cellWidth / 2f, yTop, cellWidth, cellHeight);
		    RenderTarget.DrawText(text, tfDelta, rect, (deltaVal >= 0) ? posBrush : negBrush);
		}
		
		private void DrawSignalGridForBar(ChartControl chartControl, ChartScale chartScale, int barIndex, float xBar)
		{
		    if (!EnableSignalGrid)
		        return;
		
		    if (ChartBars == null)
		        return;
		
		    double lowP, highP;
		
		    // Prefer the chart's own series (exactly what the panel is painting)
		    if (ChartBars.Bars != null)
		    {
		        if (barIndex < 0 || barIndex >= ChartBars.Bars.Count)
		            return;
		
		        lowP  = ChartBars.Bars.GetLow(barIndex);
		        highP = ChartBars.Bars.GetHigh(barIndex);
		    }
		    else
		    {
		        // Fallback to indicator input seriesT series if needed
		        if (barIndex < 0 || barIndex >= Bars.Count)
		            return;
		
		        lowP  = Bars.GetLow(barIndex);
		        highP = Bars.GetHigh(barIndex);
		    }
		
		    // Map to pixels on THIS panel’s scale
		    float barLowY  = chartScale.GetYByValue(lowP);
		    float barHighY = chartScale.GetYByValue(highP);
		
		    // Require signal data
		    if (GetVolSeqForBar == null
		        || GetStackedImbForBar == null
		        || GetReversalPOCForBar == null
		        || GetSweepForBar == null
		        || GetDeltaSeqForBar == null
		        || GetDivergenceForBar == null
		        || GetDeltaFlipForBar == null
		        || GetStoppingVolumeForBar == null
		        || GetAbsorptionForBar == null
		        || GetExhaustionForBar == null
		        || GetVAGapForBar == null
		        || GetExtremeRatioForBar == null)
		        return;
		
		    if (!GetVolSeqForBar.ContainsKey(barIndex)
		        || !GetStackedImbForBar.ContainsKey(barIndex)
		        || !GetReversalPOCForBar.ContainsKey(barIndex)
		        || !GetSweepForBar.ContainsKey(barIndex)
		        || !GetDeltaSeqForBar.ContainsKey(barIndex)
		        || !GetDivergenceForBar.ContainsKey(barIndex)
		        || !GetDeltaFlipForBar.ContainsKey(barIndex)
		        || !GetStoppingVolumeForBar.ContainsKey(barIndex)
		        || !GetAbsorptionForBar.ContainsKey(barIndex)
		        || !GetExhaustionForBar.ContainsKey(barIndex)
		        || !GetVAGapForBar.ContainsKey(barIndex)
		        || !GetExtremeRatioForBar.ContainsKey(barIndex))
		        return;
		
		    int[] signals = new int[12];
		    signals[0]  = GetVolSeqForBar[barIndex];
		    signals[1]  = GetStackedImbForBar[barIndex];
		    signals[2]  = GetReversalPOCForBar[barIndex];
		    signals[3]  = GetSweepForBar[barIndex];
		    signals[4]  = GetDeltaSeqForBar[barIndex];
		    signals[5]  = GetDivergenceForBar[barIndex];
		    signals[6]  = GetDeltaFlipForBar[barIndex];
		    signals[7]  = GetStoppingVolumeForBar[barIndex];
		    signals[8]  = GetAbsorptionForBar[barIndex];
		    signals[9]  = GetExhaustionForBar[barIndex];
		    signals[10] = GetVAGapForBar[barIndex];
		    signals[11] = GetExtremeRatioForBar[barIndex];
		
		    SharpDX.Color4[] signalColors = new SharpDX.Color4[12];
		    signalColors[0]  = ConvertMediaBrushToColor4(VolSeqColor);
		    signalColors[1]  = ConvertMediaBrushToColor4(StackedImbColor);
		    signalColors[2]  = ConvertMediaBrushToColor4(RevPOCColor);
		    signalColors[3]  = ConvertMediaBrushToColor4(SweepColor);
		    signalColors[4]  = ConvertMediaBrushToColor4(DeltaSeqColor);
		    signalColors[5]  = ConvertMediaBrushToColor4(DivergenceColor);
		    signalColors[6]  = ConvertMediaBrushToColor4(DeltaFlipColor);
		    signalColors[7]  = ConvertMediaBrushToColor4(StoppingVolumeColor);
		    signalColors[8]  = ConvertMediaBrushToColor4(AbsorptionColor);
		    signalColors[9]  = ConvertMediaBrushToColor4(ExhaustionColor);
		    signalColors[10] = ConvertMediaBrushToColor4(VAGapColor);
		    signalColors[11] = ConvertMediaBrushToColor4(ExtremeRatioColor);
		
		    bool hasLong  = false;
		    bool hasShort = false;
		    for (int s = 0; s < signals.Length; s++)
		    {
		        if (signals[s] > 0) hasLong = true;
		        else if (signals[s] < 0) hasShort = true;
		    }
		
		    // Long grid below bar low
		    if (hasLong)
		    {
		        int GridCellSize = 10;
		        int GridOffset   = SignalGridOffset;
		        int gridCols     = 2;
		        int gridRows     = 6;
		        int gridWidth    = gridCols * GridCellSize;
		        int gridHeight   = gridRows * GridCellSize;
		
		        float gridX      = xBar - gridWidth / 2f;
		        float longGridY  = barLowY + GridOffset;
		
		        for (int row = 0; row < gridRows; row++)
		        {
		            for (int col = 0; col < gridCols; col++)
		            {
		                int cellIndex = row * gridCols + col;
		                float cellX = gridX + col * GridCellSize;
		                float cellY = longGridY + row * GridCellSize;
		                RectangleF cellRect = new RectangleF(cellX, cellY, GridCellSize, GridCellSize);
		
		                if (signals[cellIndex] > 0)
		                {
		                    using (var cellBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, signalColors[cellIndex]))
		                        RenderTarget.FillRectangle(cellRect, cellBrush);
		                }
		
		                using (var borderBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(1f, 1f, 1f, 1f)))
		                    RenderTarget.DrawRectangle(cellRect, borderBrush);
		            }
		        }
		    }
		
		    // Short grid above bar high
		    if (hasShort)
		    {
		        int GridCellSize = 10;
		        int GridOffset   = SignalGridOffset;
		        int gridCols     = 2;
		        int gridRows     = 6;
		        int gridWidth    = gridCols * GridCellSize;
		        int gridHeight   = gridRows * GridCellSize;
		
		        float gridX      = xBar - gridWidth / 2f;
		        float shortGridY = barHighY - GridOffset - gridHeight;
		
		        for (int row = 0; row < gridRows; row++)
		        {
		            for (int col = 0; col < gridCols; col++)
		            {
		                int cellIndex = row * gridCols + col;
		                float cellX = gridX + col * GridCellSize;
		                float cellY = shortGridY + row * GridCellSize;
		                RectangleF cellRect = new RectangleF(cellX, cellY, GridCellSize, GridCellSize);
		
		                if (signals[cellIndex] < 0)
		                {
		                    using (var cellBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, signalColors[cellIndex]))
		                        RenderTarget.FillRectangle(cellRect, cellBrush);
		                }
		
		                using (var borderBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(1f, 1f, 1f, 1f)))
		                    RenderTarget.DrawRectangle(cellRect, borderBrush);
		            }
		        }
		    }
		}
		
		private void DrawLegend(ChartControl chartControl, ChartScale chartScale)
		{
		    // Get the panel for coordinate reference
		    ChartPanel panel = chartControl.ChartPanels[chartScale.PanelIndex];
		    
		    // Legend parameters
		    int cellSize     = 25;   // size of the color square
		    int rowHeight    = 35;   // total vertical space per row (square + label)
		    int columnWidth  = 150;  // total horizontal space per column (square + label)
		    int margin       = 60;   // distance from the chart edge
		    
		    // We have 6 rows x 2 columns = 12 signals
		    int legendRows   = 6;
		    int legendCols   = 2;
		    
		    // Position the legend near the top-right
		    //float legendX = panel.X + panel.W - margin - (legendCols * columnWidth);
		    float legendX = panel.X + margin;
			float legendY = panel.Y + margin;
		
		    // The labels for your 10 signals, row-major order:
		    string[] signalLabels = new string[]
			{
			    "Volume Sequence",   // 0
			    "Stacked Imbalance",  // 1
			    "Reverse POC",        // 2
			    "Sweep",              // 3
			    "Delta Sequence",     // 4
			    "Divergence",         // 5
			    "Delta Flip",         // 6 
			    "Stopping Volume",    // 7 
			    "Absorption",         // 8
			    "Exhaustion",         // 9
			    "Value Area Gap",     // 10
			    "Extreme Ratio"         // 11
			};
		
		    // Corresponding colors for each label
		    SharpDX.Color4[] legendColors = new SharpDX.Color4[12];
		    legendColors[0] 	= ConvertMediaBrushToColor4(VolSeqColor);
		    legendColors[1] 	= ConvertMediaBrushToColor4(StackedImbColor);
		    legendColors[2] 	= ConvertMediaBrushToColor4(RevPOCColor);
		    legendColors[3] 	= ConvertMediaBrushToColor4(SweepColor);
		    legendColors[4] 	= ConvertMediaBrushToColor4(DeltaSeqColor);
		    legendColors[5] 	= ConvertMediaBrushToColor4(DivergenceColor);
			legendColors[6] 	= ConvertMediaBrushToColor4(DeltaFlipColor);
			legendColors[7] 	= ConvertMediaBrushToColor4(StoppingVolumeColor);
		    legendColors[8] 	= ConvertMediaBrushToColor4(AbsorptionColor);
		    legendColors[9] 	= ConvertMediaBrushToColor4(ExhaustionColor);
		    legendColors[10] 	= ConvertMediaBrushToColor4(VAGapColor);
		    legendColors[11] 	= ConvertMediaBrushToColor4(ExtremeRatioColor);
		
		    // Create a text format for the legend labels
		    using (TextFormat legendTextFormat = new TextFormat(Core.Globals.DirectWriteFactory, "Arial", 14))
		    {
		        legendTextFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
		        legendTextFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
		        
		        // Loop over each cell in the 5x2 layout
		        for (int row = 0; row < legendRows; row++)
		        {
		            for (int col = 0; col < legendCols; col++)
		            {
		                int index = row * legendCols + col;
		                if (index >= legendColors.Length)
		                    break;
		                
		                // Compute the top-left of this cell
		                float cellX = legendX + col * columnWidth;
		                float cellY = legendY + row * rowHeight;
		
		                // Draw the color square
		                RectangleF colorRect = new RectangleF(cellX, cellY, cellSize, cellSize);
		                using (var fillBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, legendColors[index]))
		                {
		                    RenderTarget.FillRectangle(colorRect, fillBrush);
		                }
		                // Draw a white border around the square
		                using (var borderBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White))
		                {
		                    RenderTarget.DrawRectangle(colorRect, borderBrush);
		                }
		
		                // Draw the label to the right of the color square
		                float labelX = cellX + cellSize + 5;   // small gap after the square
		                float labelY = cellY;                  // align with the square top
		                string labelText = signalLabels[index];
		                
		                using (TextLayout layout = new TextLayout(Core.Globals.DirectWriteFactory, labelText, legendTextFormat, columnWidth - cellSize, rowHeight))
		                {
		                    using (var textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White))
		                    {
		                        RenderTarget.DrawTextLayout(new Vector2(labelX, labelY), layout, textBrush);
		                    }
		                }
		            }
		        }
		    }
		}
		
		private void DrawScrollingGrid(ChartControl chartControl, ChartScale chartScale)
		{
		    // Panel
		    ChartPanel panel = chartControl.ChartPanels[chartScale.PanelIndex];
		
		    // Logical rows (fixed index space)
		    // 0 = Delta Signals
		    // 1 = Delta
		    // 2 = COTHigh
		    // 3 = COTLow
		    // 4 = MaxDelta
		    // 5 = MinDelta
		    // 6 = Volume
		    const int totalRowCount = 7;
		
		    // Build list of *visible* rows in the order they should appear
		    var activeRows = new List<int>(totalRowCount);
		
		    if (ShowSummaryDeltaSignalsRow)
		        activeRows.Add(0);
		
		    if (ShowSummaryDeltaRows)
		        activeRows.Add(1);   // Delta
		
		    if (ShowSummaryDeltaRows)
		    {
		        activeRows.Add(2);   // MaxDelta
		        activeRows.Add(3);   // MinDelta
		    }
			
			if (ShowSummaryCOTRows)
		    {
		        activeRows.Add(4);   // COTHigh
		        activeRows.Add(5);   // COTLow
		    }
		
		    if (ShowSummaryVolumeRow)
		        activeRows.Add(6);   // Volume
		
		    int rowCount = activeRows.Count;
		    if (rowCount == 0)
		        return; // nothing to draw
		
		    float rowHeight     = 25f;
		    float tableHeight   = rowHeight * rowCount;
		    float labelColWidth = 150f; // pinned label column
		    float colWidth      = (float)chartControl.Properties.BarDistance;
		
		    // Label column position
		    float labelX = panel.X;
		    float baseY  = panel.Y + panel.H - tableHeight - 10;
		
		    // Visible bars (absolute indices)
		    int firstBar = ChartBars.FromIndex;
		    int lastBar  = ChartBars.ToIndex;
		
		    // Data columns start (to the right of label column)
		    float dataStartX = labelX + labelColWidth;
		    float dataEndX   = dataStartX;
		
		    // Determine rightmost edge for the data band (same as original)
		    for (int barIndex = firstBar; barIndex <= lastBar; barIndex++)
		    {
		        if (!GetVolumeForBar.ContainsKey(barIndex))
		            continue;
		
		        float barX     = chartControl.GetXByBarIndex(ChartBars, barIndex);
		        float colRight = barX + (colWidth / 2f);
		        if (colRight > dataEndX)
		            dataEndX = colRight;
		    }
		
		    // 1) Background row strips (same style as original, just using visible rows)
		    using (var rowStripBrushEven = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.10f, 0.10f, 0.10f, 1f)))
		    using (var rowStripBrushOdd  = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.20f, 0.20f, 0.20f, 1f)))
		    {
		        for (int visibleRow = 0; visibleRow < rowCount; visibleRow++)
		        {
		            float rowY = baseY + visibleRow * rowHeight;
		            var rowRect = new RectangleF(
		                dataStartX,
		                rowY,
		                Math.Max(0, dataEndX - dataStartX),
		                rowHeight);
		
		            RenderTarget.FillRectangle(rowRect, (visibleRow % 2 == 0) ? rowStripBrushEven : rowStripBrushOdd);
		        }
		    }
		
		    // 2) Bar columns and per-cell content (preserve original centering; optional grid lines)
		    using (var tfCols = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", StandardFontSize))
		    using (var gridLineBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(SummaryGridLineColor)))
		    {
		        tfCols.TextAlignment      = SharpDX.DirectWrite.TextAlignment.Center;
		        tfCols.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
		        tfCols.WordWrapping       = SharpDX.DirectWrite.WordWrapping.NoWrap;
		        float gridStrokeWidth     = Math.Max(1f, (float)SummaryGridLineThickness);
		
		        for (int barIndex = firstBar; barIndex <= lastBar; barIndex++)
		        {
		            if (!GetVolumeForBar.ContainsKey(barIndex))
		                continue;
		
		            float barX = chartControl.GetXByBarIndex(ChartBars, barIndex);
		            float colX = barX - colWidth / 2f;
		
		            // Fetch values for this bar
		            string deltaSignals = (GetDeltaSignalsForBar != null && GetDeltaSignalsForBar.ContainsKey(barIndex))
		                                  ? GetDeltaSignalsForBar[barIndex]
		                                  : "";
		
		            double deltaVal    = GetDeltaForBar.ContainsKey(barIndex)    ? GetDeltaForBar[barIndex]    : 0.0;
		            double maxDeltaVal = GetMaxDeltaForBar.ContainsKey(barIndex) ? GetMaxDeltaForBar[barIndex] : 0.0;
		            double minDeltaVal = GetMinDeltaForBar.ContainsKey(barIndex) ? GetMinDeltaForBar[barIndex] : 0.0;
		            double volumeVal   = GetVolumeForBar.ContainsKey(barIndex)   ? GetVolumeForBar[barIndex]   : 0.0;
		
		            // COT High/Low (fallback to Max/MinDelta if not populated yet)
		            double cotHighVal = GetCOTHighForBar != null && GetCOTHighForBar.TryGetValue(barIndex, out var _cotH)
		                                ? _cotH
		                                : maxDeltaVal;
		            double cotLowVal  = GetCOTLowForBar != null && GetCOTLowForBar.TryGetValue(barIndex, out var _cotL)
		                                ? _cotL
		                                : minDeltaVal;
		
		            // Logical row order (0–6)
		            string[] rowValues =
		            {
		                deltaSignals,
		                deltaVal.ToString("N0"),		               
		                maxDeltaVal.ToString("N0"),
		                minDeltaVal.ToString("N0"),
						cotHighVal.ToString("N0"),
		                cotLowVal.ToString("N0"),
		                volumeVal.ToString("N0")
		            };
		
		            for (int visibleRow = 0; visibleRow < rowCount; visibleRow++)
		            {
		                int r = activeRows[visibleRow];
		
		                float rowY = baseY + visibleRow * rowHeight;
		                var   cellRect = new RectangleF(colX, rowY, colWidth, rowHeight);
		
		                // Default background color
		                SharpDX.Color4 cellBgColor = new SharpDX.Color4(0.20f, 0.20f, 0.20f, 1f);
		
		                // Parse numeric when applicable
		                bool isNumber = double.TryParse(rowValues[r], out double value);
		
		                // Row index constants in logical space
		                const int idxDelta    = 1;
		                const int idxMaxDelta = 2;
		                const int idxMinDelta = 3;
						const int idxCOTHigh  = 4;
		                const int idxCOTLow   = 5;
		                const int idxVolume   = 6;
		
		                // Color rules for delta-like rows
		                if (isNumber && (r == idxDelta || r == idxCOTHigh || r == idxCOTLow || r == idxMaxDelta || r == idxMinDelta))
		                {
		                    if (Math.Abs(value) < NearZeroaThreshold)
		                    {
		                        cellBgColor = ConvertMediaBrushToColor4(ZeroDeltaColor);
		                    }
		                    else if (value > 0)
		                    {
		                        if (value <= DeltaThreshold1)      cellBgColor = ConvertMediaBrushToColor4(DeltaLowPositiveColor);
		                        else if (value <= DeltaThreshold2) cellBgColor = ConvertMediaBrushToColor4(DeltaMediumPositiveColor);
		                        else                                cellBgColor = ConvertMediaBrushToColor4(DeltaHighPositiveColor);
		                    }
		                    else // value < 0
		                    {
		                        double absVal = Math.Abs(value);
		                        if (absVal <= DeltaThreshold1)      cellBgColor = ConvertMediaBrushToColor4(DeltaLowNegativeColor);
		                        else if (absVal <= DeltaThreshold2) cellBgColor = ConvertMediaBrushToColor4(DeltaMediumNegativeColor);
		                        else                                 cellBgColor = ConvertMediaBrushToColor4(DeltaHighNegativeColor);
		                    }
		                }
		                else if (r == idxVolume && isNumber && value > VolumeThreshold)
		                {
		                    // Highlight high volume (match original logic)
		                    cellBgColor = ConvertMediaBrushToColor4(HighVolumeColor);
		                }
		
		                // Paint cell
		                using (var cellBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, cellBgColor))
		                    RenderTarget.FillRectangle(cellRect, cellBrush);
		
		                // Grid line (cell border)
		                RenderTarget.DrawRectangle(cellRect, gridLineBrush, gridStrokeWidth);
		
		                // Text (match original placement: colX + 5, rowY)
		                using (var layout = new SharpDX.DirectWrite.TextLayout(
		                    Core.Globals.DirectWriteFactory,
		                    rowValues[r],
		                    tfCols,
		                    colWidth,
		                    rowHeight))
		                using (var textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White))
		                {
		                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(colX + 5, rowY), layout, textBrush);
		                }
		            }
		        }
		    }
		
		    // 3) Pinned label column (NO header, NO transparency; identical to original style)
		    var labelBgRect = new RectangleF(labelX, baseY, labelColWidth, tableHeight);
		    using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.20f, 0.20f, 0.20f, 1f)))
		        RenderTarget.FillRectangle(labelBgRect, bgBrush);
		
		    string[] rowLabels =
		    {
		        "Delta Signals",
		        "Delta",		       
		        "MaxDelta",
		        "MinDelta",
				"COTHigh",
		        "COTLow",
		        "Volume"
		    };
		
		    using (var tfLabels = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", StandardFontSize))
		    using (var gridLineBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(SummaryGridLineColor)))
		    {
		        tfLabels.TextAlignment      = SharpDX.DirectWrite.TextAlignment.Leading;
		        tfLabels.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
		        tfLabels.WordWrapping       = SharpDX.DirectWrite.WordWrapping.NoWrap;
		        float gridStrokeWidth       = Math.Max(1f, (float)SummaryGridLineThickness);
		
		        // Outline the entire label column band
		        RenderTarget.DrawRectangle(labelBgRect, gridLineBrush, gridStrokeWidth);
		
		        for (int visibleRow = 0; visibleRow < rowCount; visibleRow++)
		        {
		            int r = activeRows[visibleRow];
		
		            float rowY  = baseY + visibleRow * rowHeight;
		            var   rowRect = new RectangleF(labelX, rowY, labelColWidth, rowHeight);
		
		            // Match original: shade only the "even" rows with the darker 0.10 strip
		            if (visibleRow % 2 == 0)
		            {
		                using (var rowBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color4(0.10f, 0.10f, 0.10f, 1f)))
		                    RenderTarget.FillRectangle(rowRect, rowBrush);
		            }
		
		            // Grid line for label cell
		            RenderTarget.DrawRectangle(rowRect, gridLineBrush, gridStrokeWidth);
		
		            using (var labelLayout = new SharpDX.DirectWrite.TextLayout(
		                Core.Globals.DirectWriteFactory,
		                rowLabels[r],
		                tfLabels,
		                labelColWidth,
		                rowHeight))
		            using (var textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White))
		            {
		                RenderTarget.DrawTextLayout(new SharpDX.Vector2(labelX + 5, rowY), labelLayout, textBrush);
		            }
		        }
		    }
		}
	
		#endregion

        #region Helper Methods
		
		private static int[] ParsePlotList(string s)
		{
		    if (string.IsNullOrWhiteSpace(s)) return Array.Empty<int>();
		    return s.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
		            .Select(x => { int n; return int.TryParse(x.Trim(), out n) ? n : int.MinValue; })
		            .Where(n => (n >= 11 && n <= 23) || (n >= 33 && n <= 35))
		            .Distinct()
		            .OrderBy(n => n)
		            .ToArray();
		}
		
		private int GetSignalByPlotNumber(int plotNo)
		{
		    switch (plotNo)
		    {
		        case 11: return volSeqSignal;
		        case 12: return stackedImbSignal;
		        case 13: return reversalPOCSignal;
		        case 14: return sweepSignal;
		        case 15: return deltaSeqSignal;
		        case 16: return divergenceSignal;
		        case 17: return deltaFlipSignal;
		        case 18: return stopVolSignal;
		        case 19: return absorptionSignal;
		        case 20: return exhaustionSignal;
		        case 21: return vaGapSignal;
		        case 22: return extremeRatioSignal;
		        case 23: return fadingMomentumSignal;
				case 33: return extremeVASignal;
				case 34: return pocInWickSignal;
				case 35: return extremePOCSignal;
		        default: return 0;
		    }
		}
		
		private int CombineSignals(int[] ids)
		{
		    if (ids == null || ids.Length == 0)
		        return 0;
		
		    int dir = 0; // 0 = unset, +1 or -1 once seen
		
		    foreach (var id in ids)
		    {
		        int s = GetSignalByPlotNumber(id); // returns -1, 0, or +1
		
		        // Strict AND requires every member be non-zero and equal
		        if (s == 0)
		            return 0;
		
		        if (dir == 0)
		            dir = s;             // set the direction on first non-zero
		        else if (s != dir)
		            return 0;            // mismatch => mixed directions => 0
		    }
		
		    return dir; // all matched and non-zero
		}




		// Safe row fetch: returns null if bar or row missing
		private Dictionary<double,double> Row(
		    Dictionary<int, Dictionary<double,double>> map, int bar)
		{
		    return (map != null && map.TryGetValue(bar, out var r)) ? r : null;
		}
		
		// Safe value read: returns 0 if bar or key missing
		private double Read(
		    Dictionary<int, Dictionary<double,double>> map, int bar, double key)
		{
		    if (map != null && map.TryGetValue(bar, out var r) && r != null
		        && r.TryGetValue(key, out var v))
		        return v;
		    return 0.0;
		}
		
		private void DrawCandlestickOverlay(int barIndex, ChartControl chartControl, ChartScale chartScale, float barWidth)
		{
		    // Basic safety checks
		    if (ChartBars == null || ChartBars.Bars == null)
		    {
		        //Print($"[CANDLE SKIP] barIndex={barIndex} reason=ChartBars.Bars null");
		        return;
		    }
		
		    if (barIndex < 0 || barIndex >= ChartBars.Bars.Count)
		    {
		        //Print($"[CANDLE SKIP] barIndex={barIndex} out of range, Bars.Count={ChartBars.Bars.Count}");
		        return;
		    }
		
		    double open  = ChartBars.Bars.GetOpen(barIndex);
		    double high  = ChartBars.Bars.GetHigh(barIndex);
		    double low   = ChartBars.Bars.GetLow(barIndex);
		    double close = ChartBars.Bars.GetClose(barIndex);
		
		    // Log a couple of sample bars so we know this is sane.
		    
		
		    // Decide up/down based on price
		    var isUp = close >= open;
		
		    var mediaBarBrush = isUp ? BarUpColor : BarDownColor;
		    var mediaOutlineBrush = isUp ? BarUpOutlineColor : BarDownOutlineColor;
		
		    using (var outlineBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(mediaOutlineBrush)))
		    using (var barBrush     = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ConvertMediaBrushToColor4(mediaBarBrush)))
		    {
		        float yHigh  = chartScale.GetYByValue(high);
		        float yLow   = chartScale.GetYByValue(low);
		        float yOpen  = chartScale.GetYByValue(open);
		        float yClose = chartScale.GetYByValue(close);
		
		        float top    = Math.Min(yOpen, yClose);
		        float bottom = Math.Max(yOpen, yClose);
		        float xCenter = chartControl.GetXByBarIndex(ChartBars, barIndex);
		
		        // Wick
		        RenderTarget.DrawLine(new Vector2(xCenter, yHigh),  new Vector2(xCenter, top),    outlineBrush, 1f);
		        RenderTarget.DrawLine(new Vector2(xCenter, bottom), new Vector2(xCenter, yLow),   outlineBrush, 1f);
		
		        // Body
		        var bodyRect = new RectangleF(
		            xCenter - barWidth - 0.5f,
		            top,
		            barWidth * 2f,
		            bottom - top);
		
		        RenderTarget.FillRectangle(bodyRect, barBrush);
		        RenderTarget.DrawRectangle(bodyRect, outlineBrush, 1f);
		    }
		}
		
		private void DrawSignalDiamond(int bar, string signalName, int signalValue, System.Windows.Media.Brush signalBrush, int offsetValue)
		{
		    // build both possible tags
		    string longTag  = signalName + "_long_"  + bar;
		    string shortTag = signalName + "_short_" + bar;
		    
		    // 1) always clear any existing diamond for this bar/signal  
		    RemoveDrawObject(longTag);
		    RemoveDrawObject(shortTag);
		
		    // 2) if the new value is zero, we’re done (nothing to draw)
		    if (signalValue == 0)
		        return;
		
		    // 3) otherwise draw the new one
		    double offset     = TickSize * offsetValue;
		    double yCoordinate = (signalValue > 0) 
		        ? Low[0]  - offset 
		        : High[0] + offset;
		
		    // pick the correct tag
		    string tagSuffix = (signalValue > 0) ? "_long_" : "_short_";
		    string tag       = signalName + tagSuffix + bar;
		
		    Draw.Diamond(this, tag, true, 0, yCoordinate, signalBrush);
		}
		
		private void DrawTriangleUp(string tag, int barsAgo, double yPrice, System.Windows.Media.Brush brush)
		{
		    double y = yPrice - (TickSize * TriangleOffsetTicks);
		    Draw.Text(
		        this,
		        tag, // use tag as-is
		        false,
		        "▲",
		        barsAgo,
		        y,
		        0,
		        brush,
		        new SimpleFont("Arial", TriangleFontSize) { Bold = true },
		        System.Windows.TextAlignment.Center,
		        Brushes.Transparent,
		        null,
		        1
		    );
		}
		
		private void DrawTriangleDown(string tag, int barsAgo, double yPrice, System.Windows.Media.Brush brush)
		{
		    double y = yPrice + (TickSize * TriangleOffsetTicks);
		    Draw.Text(
		        this,
		        tag, // use tag as-is
		        false,
		        "▼",
		        barsAgo,
		        y,
		        0,
		        brush,
		        new SimpleFont("Arial", TriangleFontSize) { Bold = true },
		        System.Windows.TextAlignment.Center,
		        Brushes.Transparent,
		        null,
		        1
		    );
		}



		private SharpDX.Color4 ConvertMediaBrushToColor4(System.Windows.Media.Brush mediaBrush)
		{
		    var scb = mediaBrush as System.Windows.Media.SolidColorBrush;
		    if (scb == null)
		        return new SharpDX.Color4(0.2f, 0.2f, 0.2f, 1f);
		
		    // Convert from sRGB to float values
		    float a = (float)scb.Color.A / 255f;
		    float r = (float)scb.Color.R / 255f;
		    float g = (float)scb.Color.G / 255f;
		    float b = (float)scb.Color.B / 255f;
		    return new SharpDX.Color4(r, g, b, a);
		}
        private void CleanupExtraneousValues(Dictionary<double, double> dict, double lowPrice, double interval)
        {
            var keysToRemove = dict.Keys.Where(price => (price - lowPrice) % interval != 0).ToList();
            foreach (var key in keysToRemove)
            {
                dict.Remove(key);
            }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenFootprintOrderFlowV00015[] cacheAlightenFootprintOrderFlowV00015;
		public AlightenFootprintOrderFlowV00015 AlightenFootprintOrderFlowV00015(int aggregationInterval, double valueAreaPer, bool useSessionOpenForAggregation, int volumeSeqLookback, int stackedImbalanceLookback, int deltaSequenceLookback, int sweepLookback, int divergenceLookback, bool useSessionHiLoDivergence, int stoppingVolumeLookback, int deltaFlipLookback, int fadingMomentumLookback, double imbFact, double largeRatioThreshold, double smallRatioThreshold, int deltaThreshold1, int deltaThreshold2, int volumeThreshold, int nearZeroaThreshold, int exhaustionThreshold, int standardFontSize, bool enableFootprint, bool enableFootprintText, bool enableSummaryGrid, bool enableSignalGrid, int signalGridOffset, bool enableSignalGridLegend, bool showSummaryDeltaSignalsRow, bool showSummaryDeltaRows, bool showSummaryCOTRows, bool showSummaryVolumeRow, bool enableDeltaOnBar, int deltaOnBarFontSize, System.Windows.Media.Brush deltaOnBarPositiveColor, System.Windows.Media.Brush deltaOnBarNegativeColor, int deltaOnBarOffset, System.Windows.Media.Brush defaultFootprintTextColor, System.Windows.Media.Brush positiveImbalanceTextColor, System.Windows.Media.Brush negativeImbalanceTextColor, System.Windows.Media.Brush barUpColor, System.Windows.Media.Brush barDownColor, System.Windows.Media.Brush barUpOutlineColor, System.Windows.Media.Brush barDownOutlineColor, System.Windows.Media.Brush barUpVAColor, System.Windows.Media.Brush barDownVAColor, int vAOpacity, System.Windows.Media.Brush pOCColor, System.Windows.Media.Brush pOCOutlineColor, System.Windows.Media.Brush pOCTextColor, System.Windows.Media.Brush deltaLowPositiveColor, System.Windows.Media.Brush deltaMediumPositiveColor, System.Windows.Media.Brush deltaHighPositiveColor, System.Windows.Media.Brush deltaLowNegativeColor, System.Windows.Media.Brush deltaMediumNegativeColor, System.Windows.Media.Brush deltaHighNegativeColor, System.Windows.Media.Brush highVolumeColor, System.Windows.Media.Brush zeroDeltaColor, System.Windows.Media.Brush summaryGridLineColor, int summaryGridLineThickness, System.Windows.Media.Brush volSeqColor, System.Windows.Media.Brush stackedImbColor, System.Windows.Media.Brush revPOCColor, System.Windows.Media.Brush sweepColor, System.Windows.Media.Brush deltaSeqColor, System.Windows.Media.Brush divergenceColor, System.Windows.Media.Brush absorptionColor, System.Windows.Media.Brush exhaustionColor, System.Windows.Media.Brush vAGapColor, System.Windows.Media.Brush extremeRatioColor, System.Windows.Media.Brush deltaFlipColor, System.Windows.Media.Brush stoppingVolumeColor, System.Windows.Media.Brush fadingMomentumColor, System.Windows.Media.Brush extremeVAColor, System.Windows.Media.Brush pOCInWickColor, System.Windows.Media.Brush extremePOCColor, bool enableVolSeqSignal, int volSeqDiamondOffset, bool enableStackedImbSignal, int stackedImbDiamondOffset, bool enableReversalPOCSignal, int reversalPOCDiamondOffset, bool enableSweepSignal, int sweepDiamondOffset, bool enableDeltaSeqSignal, int deltaSeqDiamondOffset, bool enableDivergenceSignal, int divergenceDiamondOffset, bool enableDeltaFlipSignal, int deltaFlipDiamondOffset, bool enableStoppingVolumeSignal, int stoppingVolumeDiamondOffset, bool enableAbsorptionSignal, int absorptionDiamondOffset, bool enableExhaustionSignal, int exhaustionDiamondOffset, bool enableVAGapSignal, int vAGapDiamondOffset, bool enableExtremeRatioSignal, int extremeRatioDiamondOffset, bool enableFadingMomentumSignal, int fadingMomentumDiamondOffset, bool enableExtremeVASignal, int extremeVADiamondOffset, bool enablePOCInWickSignal, int pOCInWickDiamondOffset, bool enableExtremePOCSignal, int extremePOCDiamondOffset, string customPlot1Sources, string customPlot2Sources, string customPlot3Sources, bool enableCustomPlot1Signal, int customPlot1DiamondOffset, bool enableCustomPlot2Signal, int customPlot2DiamondOffset, bool enableCustomPlot3Signal, int customPlot3DiamondOffset, bool enableVAPOCExtremeComboSignal, int triangleFontSize, int triangleOffsetTicks, System.Windows.Media.Brush vAPOCBullColor, System.Windows.Media.Brush vAPOCBearColor)
		{
			return AlightenFootprintOrderFlowV00015(Input, aggregationInterval, valueAreaPer, useSessionOpenForAggregation, volumeSeqLookback, stackedImbalanceLookback, deltaSequenceLookback, sweepLookback, divergenceLookback, useSessionHiLoDivergence, stoppingVolumeLookback, deltaFlipLookback, fadingMomentumLookback, imbFact, largeRatioThreshold, smallRatioThreshold, deltaThreshold1, deltaThreshold2, volumeThreshold, nearZeroaThreshold, exhaustionThreshold, standardFontSize, enableFootprint, enableFootprintText, enableSummaryGrid, enableSignalGrid, signalGridOffset, enableSignalGridLegend, showSummaryDeltaSignalsRow, showSummaryDeltaRows, showSummaryCOTRows, showSummaryVolumeRow, enableDeltaOnBar, deltaOnBarFontSize, deltaOnBarPositiveColor, deltaOnBarNegativeColor, deltaOnBarOffset, defaultFootprintTextColor, positiveImbalanceTextColor, negativeImbalanceTextColor, barUpColor, barDownColor, barUpOutlineColor, barDownOutlineColor, barUpVAColor, barDownVAColor, vAOpacity, pOCColor, pOCOutlineColor, pOCTextColor, deltaLowPositiveColor, deltaMediumPositiveColor, deltaHighPositiveColor, deltaLowNegativeColor, deltaMediumNegativeColor, deltaHighNegativeColor, highVolumeColor, zeroDeltaColor, summaryGridLineColor, summaryGridLineThickness, volSeqColor, stackedImbColor, revPOCColor, sweepColor, deltaSeqColor, divergenceColor, absorptionColor, exhaustionColor, vAGapColor, extremeRatioColor, deltaFlipColor, stoppingVolumeColor, fadingMomentumColor, extremeVAColor, pOCInWickColor, extremePOCColor, enableVolSeqSignal, volSeqDiamondOffset, enableStackedImbSignal, stackedImbDiamondOffset, enableReversalPOCSignal, reversalPOCDiamondOffset, enableSweepSignal, sweepDiamondOffset, enableDeltaSeqSignal, deltaSeqDiamondOffset, enableDivergenceSignal, divergenceDiamondOffset, enableDeltaFlipSignal, deltaFlipDiamondOffset, enableStoppingVolumeSignal, stoppingVolumeDiamondOffset, enableAbsorptionSignal, absorptionDiamondOffset, enableExhaustionSignal, exhaustionDiamondOffset, enableVAGapSignal, vAGapDiamondOffset, enableExtremeRatioSignal, extremeRatioDiamondOffset, enableFadingMomentumSignal, fadingMomentumDiamondOffset, enableExtremeVASignal, extremeVADiamondOffset, enablePOCInWickSignal, pOCInWickDiamondOffset, enableExtremePOCSignal, extremePOCDiamondOffset, customPlot1Sources, customPlot2Sources, customPlot3Sources, enableCustomPlot1Signal, customPlot1DiamondOffset, enableCustomPlot2Signal, customPlot2DiamondOffset, enableCustomPlot3Signal, customPlot3DiamondOffset, enableVAPOCExtremeComboSignal, triangleFontSize, triangleOffsetTicks, vAPOCBullColor, vAPOCBearColor);
		}

		public AlightenFootprintOrderFlowV00015 AlightenFootprintOrderFlowV00015(ISeries<double> input, int aggregationInterval, double valueAreaPer, bool useSessionOpenForAggregation, int volumeSeqLookback, int stackedImbalanceLookback, int deltaSequenceLookback, int sweepLookback, int divergenceLookback, bool useSessionHiLoDivergence, int stoppingVolumeLookback, int deltaFlipLookback, int fadingMomentumLookback, double imbFact, double largeRatioThreshold, double smallRatioThreshold, int deltaThreshold1, int deltaThreshold2, int volumeThreshold, int nearZeroaThreshold, int exhaustionThreshold, int standardFontSize, bool enableFootprint, bool enableFootprintText, bool enableSummaryGrid, bool enableSignalGrid, int signalGridOffset, bool enableSignalGridLegend, bool showSummaryDeltaSignalsRow, bool showSummaryDeltaRows, bool showSummaryCOTRows, bool showSummaryVolumeRow, bool enableDeltaOnBar, int deltaOnBarFontSize, System.Windows.Media.Brush deltaOnBarPositiveColor, System.Windows.Media.Brush deltaOnBarNegativeColor, int deltaOnBarOffset, System.Windows.Media.Brush defaultFootprintTextColor, System.Windows.Media.Brush positiveImbalanceTextColor, System.Windows.Media.Brush negativeImbalanceTextColor, System.Windows.Media.Brush barUpColor, System.Windows.Media.Brush barDownColor, System.Windows.Media.Brush barUpOutlineColor, System.Windows.Media.Brush barDownOutlineColor, System.Windows.Media.Brush barUpVAColor, System.Windows.Media.Brush barDownVAColor, int vAOpacity, System.Windows.Media.Brush pOCColor, System.Windows.Media.Brush pOCOutlineColor, System.Windows.Media.Brush pOCTextColor, System.Windows.Media.Brush deltaLowPositiveColor, System.Windows.Media.Brush deltaMediumPositiveColor, System.Windows.Media.Brush deltaHighPositiveColor, System.Windows.Media.Brush deltaLowNegativeColor, System.Windows.Media.Brush deltaMediumNegativeColor, System.Windows.Media.Brush deltaHighNegativeColor, System.Windows.Media.Brush highVolumeColor, System.Windows.Media.Brush zeroDeltaColor, System.Windows.Media.Brush summaryGridLineColor, int summaryGridLineThickness, System.Windows.Media.Brush volSeqColor, System.Windows.Media.Brush stackedImbColor, System.Windows.Media.Brush revPOCColor, System.Windows.Media.Brush sweepColor, System.Windows.Media.Brush deltaSeqColor, System.Windows.Media.Brush divergenceColor, System.Windows.Media.Brush absorptionColor, System.Windows.Media.Brush exhaustionColor, System.Windows.Media.Brush vAGapColor, System.Windows.Media.Brush extremeRatioColor, System.Windows.Media.Brush deltaFlipColor, System.Windows.Media.Brush stoppingVolumeColor, System.Windows.Media.Brush fadingMomentumColor, System.Windows.Media.Brush extremeVAColor, System.Windows.Media.Brush pOCInWickColor, System.Windows.Media.Brush extremePOCColor, bool enableVolSeqSignal, int volSeqDiamondOffset, bool enableStackedImbSignal, int stackedImbDiamondOffset, bool enableReversalPOCSignal, int reversalPOCDiamondOffset, bool enableSweepSignal, int sweepDiamondOffset, bool enableDeltaSeqSignal, int deltaSeqDiamondOffset, bool enableDivergenceSignal, int divergenceDiamondOffset, bool enableDeltaFlipSignal, int deltaFlipDiamondOffset, bool enableStoppingVolumeSignal, int stoppingVolumeDiamondOffset, bool enableAbsorptionSignal, int absorptionDiamondOffset, bool enableExhaustionSignal, int exhaustionDiamondOffset, bool enableVAGapSignal, int vAGapDiamondOffset, bool enableExtremeRatioSignal, int extremeRatioDiamondOffset, bool enableFadingMomentumSignal, int fadingMomentumDiamondOffset, bool enableExtremeVASignal, int extremeVADiamondOffset, bool enablePOCInWickSignal, int pOCInWickDiamondOffset, bool enableExtremePOCSignal, int extremePOCDiamondOffset, string customPlot1Sources, string customPlot2Sources, string customPlot3Sources, bool enableCustomPlot1Signal, int customPlot1DiamondOffset, bool enableCustomPlot2Signal, int customPlot2DiamondOffset, bool enableCustomPlot3Signal, int customPlot3DiamondOffset, bool enableVAPOCExtremeComboSignal, int triangleFontSize, int triangleOffsetTicks, System.Windows.Media.Brush vAPOCBullColor, System.Windows.Media.Brush vAPOCBearColor)
		{
			if (cacheAlightenFootprintOrderFlowV00015 != null)
				for (int idx = 0; idx < cacheAlightenFootprintOrderFlowV00015.Length; idx++)
					if (cacheAlightenFootprintOrderFlowV00015[idx] != null && cacheAlightenFootprintOrderFlowV00015[idx].AggregationInterval == aggregationInterval && cacheAlightenFootprintOrderFlowV00015[idx].ValueAreaPer == valueAreaPer && cacheAlightenFootprintOrderFlowV00015[idx].UseSessionOpenForAggregation == useSessionOpenForAggregation && cacheAlightenFootprintOrderFlowV00015[idx].VolumeSeqLookback == volumeSeqLookback && cacheAlightenFootprintOrderFlowV00015[idx].StackedImbalanceLookback == stackedImbalanceLookback && cacheAlightenFootprintOrderFlowV00015[idx].DeltaSequenceLookback == deltaSequenceLookback && cacheAlightenFootprintOrderFlowV00015[idx].SweepLookback == sweepLookback && cacheAlightenFootprintOrderFlowV00015[idx].DivergenceLookback == divergenceLookback && cacheAlightenFootprintOrderFlowV00015[idx].UseSessionHiLoDivergence == useSessionHiLoDivergence && cacheAlightenFootprintOrderFlowV00015[idx].StoppingVolumeLookback == stoppingVolumeLookback && cacheAlightenFootprintOrderFlowV00015[idx].DeltaFlipLookback == deltaFlipLookback && cacheAlightenFootprintOrderFlowV00015[idx].FadingMomentumLookback == fadingMomentumLookback && cacheAlightenFootprintOrderFlowV00015[idx].ImbFact == imbFact && cacheAlightenFootprintOrderFlowV00015[idx].LargeRatioThreshold == largeRatioThreshold && cacheAlightenFootprintOrderFlowV00015[idx].SmallRatioThreshold == smallRatioThreshold && cacheAlightenFootprintOrderFlowV00015[idx].DeltaThreshold1 == deltaThreshold1 && cacheAlightenFootprintOrderFlowV00015[idx].DeltaThreshold2 == deltaThreshold2 && cacheAlightenFootprintOrderFlowV00015[idx].VolumeThreshold == volumeThreshold && cacheAlightenFootprintOrderFlowV00015[idx].NearZeroaThreshold == nearZeroaThreshold && cacheAlightenFootprintOrderFlowV00015[idx].ExhaustionThreshold == exhaustionThreshold && cacheAlightenFootprintOrderFlowV00015[idx].StandardFontSize == standardFontSize && cacheAlightenFootprintOrderFlowV00015[idx].EnableFootprint == enableFootprint && cacheAlightenFootprintOrderFlowV00015[idx].EnableFootprintText == enableFootprintText && cacheAlightenFootprintOrderFlowV00015[idx].EnableSummaryGrid == enableSummaryGrid && cacheAlightenFootprintOrderFlowV00015[idx].EnableSignalGrid == enableSignalGrid && cacheAlightenFootprintOrderFlowV00015[idx].SignalGridOffset == signalGridOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableSignalGridLegend == enableSignalGridLegend && cacheAlightenFootprintOrderFlowV00015[idx].ShowSummaryDeltaSignalsRow == showSummaryDeltaSignalsRow && cacheAlightenFootprintOrderFlowV00015[idx].ShowSummaryDeltaRows == showSummaryDeltaRows && cacheAlightenFootprintOrderFlowV00015[idx].ShowSummaryCOTRows == showSummaryCOTRows && cacheAlightenFootprintOrderFlowV00015[idx].ShowSummaryVolumeRow == showSummaryVolumeRow && cacheAlightenFootprintOrderFlowV00015[idx].EnableDeltaOnBar == enableDeltaOnBar && cacheAlightenFootprintOrderFlowV00015[idx].DeltaOnBarFontSize == deltaOnBarFontSize && cacheAlightenFootprintOrderFlowV00015[idx].DeltaOnBarPositiveColor == deltaOnBarPositiveColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaOnBarNegativeColor == deltaOnBarNegativeColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaOnBarOffset == deltaOnBarOffset && cacheAlightenFootprintOrderFlowV00015[idx].DefaultFootprintTextColor == defaultFootprintTextColor && cacheAlightenFootprintOrderFlowV00015[idx].PositiveImbalanceTextColor == positiveImbalanceTextColor && cacheAlightenFootprintOrderFlowV00015[idx].NegativeImbalanceTextColor == negativeImbalanceTextColor && cacheAlightenFootprintOrderFlowV00015[idx].BarUpColor == barUpColor && cacheAlightenFootprintOrderFlowV00015[idx].BarDownColor == barDownColor && cacheAlightenFootprintOrderFlowV00015[idx].BarUpOutlineColor == barUpOutlineColor && cacheAlightenFootprintOrderFlowV00015[idx].BarDownOutlineColor == barDownOutlineColor && cacheAlightenFootprintOrderFlowV00015[idx].BarUpVAColor == barUpVAColor && cacheAlightenFootprintOrderFlowV00015[idx].BarDownVAColor == barDownVAColor && cacheAlightenFootprintOrderFlowV00015[idx].VAOpacity == vAOpacity && cacheAlightenFootprintOrderFlowV00015[idx].POCColor == pOCColor && cacheAlightenFootprintOrderFlowV00015[idx].POCOutlineColor == pOCOutlineColor && cacheAlightenFootprintOrderFlowV00015[idx].POCTextColor == pOCTextColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaLowPositiveColor == deltaLowPositiveColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaMediumPositiveColor == deltaMediumPositiveColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaHighPositiveColor == deltaHighPositiveColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaLowNegativeColor == deltaLowNegativeColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaMediumNegativeColor == deltaMediumNegativeColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaHighNegativeColor == deltaHighNegativeColor && cacheAlightenFootprintOrderFlowV00015[idx].HighVolumeColor == highVolumeColor && cacheAlightenFootprintOrderFlowV00015[idx].ZeroDeltaColor == zeroDeltaColor && cacheAlightenFootprintOrderFlowV00015[idx].SummaryGridLineColor == summaryGridLineColor && cacheAlightenFootprintOrderFlowV00015[idx].SummaryGridLineThickness == summaryGridLineThickness && cacheAlightenFootprintOrderFlowV00015[idx].VolSeqColor == volSeqColor && cacheAlightenFootprintOrderFlowV00015[idx].StackedImbColor == stackedImbColor && cacheAlightenFootprintOrderFlowV00015[idx].RevPOCColor == revPOCColor && cacheAlightenFootprintOrderFlowV00015[idx].SweepColor == sweepColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaSeqColor == deltaSeqColor && cacheAlightenFootprintOrderFlowV00015[idx].DivergenceColor == divergenceColor && cacheAlightenFootprintOrderFlowV00015[idx].AbsorptionColor == absorptionColor && cacheAlightenFootprintOrderFlowV00015[idx].ExhaustionColor == exhaustionColor && cacheAlightenFootprintOrderFlowV00015[idx].VAGapColor == vAGapColor && cacheAlightenFootprintOrderFlowV00015[idx].ExtremeRatioColor == extremeRatioColor && cacheAlightenFootprintOrderFlowV00015[idx].DeltaFlipColor == deltaFlipColor && cacheAlightenFootprintOrderFlowV00015[idx].StoppingVolumeColor == stoppingVolumeColor && cacheAlightenFootprintOrderFlowV00015[idx].FadingMomentumColor == fadingMomentumColor && cacheAlightenFootprintOrderFlowV00015[idx].ExtremeVAColor == extremeVAColor && cacheAlightenFootprintOrderFlowV00015[idx].POCInWickColor == pOCInWickColor && cacheAlightenFootprintOrderFlowV00015[idx].ExtremePOCColor == extremePOCColor && cacheAlightenFootprintOrderFlowV00015[idx].EnableVolSeqSignal == enableVolSeqSignal && cacheAlightenFootprintOrderFlowV00015[idx].VolSeqDiamondOffset == volSeqDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableStackedImbSignal == enableStackedImbSignal && cacheAlightenFootprintOrderFlowV00015[idx].StackedImbDiamondOffset == stackedImbDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableReversalPOCSignal == enableReversalPOCSignal && cacheAlightenFootprintOrderFlowV00015[idx].ReversalPOCDiamondOffset == reversalPOCDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableSweepSignal == enableSweepSignal && cacheAlightenFootprintOrderFlowV00015[idx].SweepDiamondOffset == sweepDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableDeltaSeqSignal == enableDeltaSeqSignal && cacheAlightenFootprintOrderFlowV00015[idx].DeltaSeqDiamondOffset == deltaSeqDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableDivergenceSignal == enableDivergenceSignal && cacheAlightenFootprintOrderFlowV00015[idx].DivergenceDiamondOffset == divergenceDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableDeltaFlipSignal == enableDeltaFlipSignal && cacheAlightenFootprintOrderFlowV00015[idx].DeltaFlipDiamondOffset == deltaFlipDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableStoppingVolumeSignal == enableStoppingVolumeSignal && cacheAlightenFootprintOrderFlowV00015[idx].StoppingVolumeDiamondOffset == stoppingVolumeDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableAbsorptionSignal == enableAbsorptionSignal && cacheAlightenFootprintOrderFlowV00015[idx].AbsorptionDiamondOffset == absorptionDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableExhaustionSignal == enableExhaustionSignal && cacheAlightenFootprintOrderFlowV00015[idx].ExhaustionDiamondOffset == exhaustionDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableVAGapSignal == enableVAGapSignal && cacheAlightenFootprintOrderFlowV00015[idx].VAGapDiamondOffset == vAGapDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableExtremeRatioSignal == enableExtremeRatioSignal && cacheAlightenFootprintOrderFlowV00015[idx].ExtremeRatioDiamondOffset == extremeRatioDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableFadingMomentumSignal == enableFadingMomentumSignal && cacheAlightenFootprintOrderFlowV00015[idx].FadingMomentumDiamondOffset == fadingMomentumDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableExtremeVASignal == enableExtremeVASignal && cacheAlightenFootprintOrderFlowV00015[idx].ExtremeVADiamondOffset == extremeVADiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnablePOCInWickSignal == enablePOCInWickSignal && cacheAlightenFootprintOrderFlowV00015[idx].POCInWickDiamondOffset == pOCInWickDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableExtremePOCSignal == enableExtremePOCSignal && cacheAlightenFootprintOrderFlowV00015[idx].ExtremePOCDiamondOffset == extremePOCDiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].CustomPlot1Sources == customPlot1Sources && cacheAlightenFootprintOrderFlowV00015[idx].CustomPlot2Sources == customPlot2Sources && cacheAlightenFootprintOrderFlowV00015[idx].CustomPlot3Sources == customPlot3Sources && cacheAlightenFootprintOrderFlowV00015[idx].EnableCustomPlot1Signal == enableCustomPlot1Signal && cacheAlightenFootprintOrderFlowV00015[idx].CustomPlot1DiamondOffset == customPlot1DiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableCustomPlot2Signal == enableCustomPlot2Signal && cacheAlightenFootprintOrderFlowV00015[idx].CustomPlot2DiamondOffset == customPlot2DiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableCustomPlot3Signal == enableCustomPlot3Signal && cacheAlightenFootprintOrderFlowV00015[idx].CustomPlot3DiamondOffset == customPlot3DiamondOffset && cacheAlightenFootprintOrderFlowV00015[idx].EnableVAPOCExtremeComboSignal == enableVAPOCExtremeComboSignal && cacheAlightenFootprintOrderFlowV00015[idx].TriangleFontSize == triangleFontSize && cacheAlightenFootprintOrderFlowV00015[idx].TriangleOffsetTicks == triangleOffsetTicks && cacheAlightenFootprintOrderFlowV00015[idx].VAPOCBullColor == vAPOCBullColor && cacheAlightenFootprintOrderFlowV00015[idx].VAPOCBearColor == vAPOCBearColor && cacheAlightenFootprintOrderFlowV00015[idx].EqualsInput(input))
						return cacheAlightenFootprintOrderFlowV00015[idx];
			return CacheIndicator<AlightenFootprintOrderFlowV00015>(new AlightenFootprintOrderFlowV00015(){ AggregationInterval = aggregationInterval, ValueAreaPer = valueAreaPer, UseSessionOpenForAggregation = useSessionOpenForAggregation, VolumeSeqLookback = volumeSeqLookback, StackedImbalanceLookback = stackedImbalanceLookback, DeltaSequenceLookback = deltaSequenceLookback, SweepLookback = sweepLookback, DivergenceLookback = divergenceLookback, UseSessionHiLoDivergence = useSessionHiLoDivergence, StoppingVolumeLookback = stoppingVolumeLookback, DeltaFlipLookback = deltaFlipLookback, FadingMomentumLookback = fadingMomentumLookback, ImbFact = imbFact, LargeRatioThreshold = largeRatioThreshold, SmallRatioThreshold = smallRatioThreshold, DeltaThreshold1 = deltaThreshold1, DeltaThreshold2 = deltaThreshold2, VolumeThreshold = volumeThreshold, NearZeroaThreshold = nearZeroaThreshold, ExhaustionThreshold = exhaustionThreshold, StandardFontSize = standardFontSize, EnableFootprint = enableFootprint, EnableFootprintText = enableFootprintText, EnableSummaryGrid = enableSummaryGrid, EnableSignalGrid = enableSignalGrid, SignalGridOffset = signalGridOffset, EnableSignalGridLegend = enableSignalGridLegend, ShowSummaryDeltaSignalsRow = showSummaryDeltaSignalsRow, ShowSummaryDeltaRows = showSummaryDeltaRows, ShowSummaryCOTRows = showSummaryCOTRows, ShowSummaryVolumeRow = showSummaryVolumeRow, EnableDeltaOnBar = enableDeltaOnBar, DeltaOnBarFontSize = deltaOnBarFontSize, DeltaOnBarPositiveColor = deltaOnBarPositiveColor, DeltaOnBarNegativeColor = deltaOnBarNegativeColor, DeltaOnBarOffset = deltaOnBarOffset, DefaultFootprintTextColor = defaultFootprintTextColor, PositiveImbalanceTextColor = positiveImbalanceTextColor, NegativeImbalanceTextColor = negativeImbalanceTextColor, BarUpColor = barUpColor, BarDownColor = barDownColor, BarUpOutlineColor = barUpOutlineColor, BarDownOutlineColor = barDownOutlineColor, BarUpVAColor = barUpVAColor, BarDownVAColor = barDownVAColor, VAOpacity = vAOpacity, POCColor = pOCColor, POCOutlineColor = pOCOutlineColor, POCTextColor = pOCTextColor, DeltaLowPositiveColor = deltaLowPositiveColor, DeltaMediumPositiveColor = deltaMediumPositiveColor, DeltaHighPositiveColor = deltaHighPositiveColor, DeltaLowNegativeColor = deltaLowNegativeColor, DeltaMediumNegativeColor = deltaMediumNegativeColor, DeltaHighNegativeColor = deltaHighNegativeColor, HighVolumeColor = highVolumeColor, ZeroDeltaColor = zeroDeltaColor, SummaryGridLineColor = summaryGridLineColor, SummaryGridLineThickness = summaryGridLineThickness, VolSeqColor = volSeqColor, StackedImbColor = stackedImbColor, RevPOCColor = revPOCColor, SweepColor = sweepColor, DeltaSeqColor = deltaSeqColor, DivergenceColor = divergenceColor, AbsorptionColor = absorptionColor, ExhaustionColor = exhaustionColor, VAGapColor = vAGapColor, ExtremeRatioColor = extremeRatioColor, DeltaFlipColor = deltaFlipColor, StoppingVolumeColor = stoppingVolumeColor, FadingMomentumColor = fadingMomentumColor, ExtremeVAColor = extremeVAColor, POCInWickColor = pOCInWickColor, ExtremePOCColor = extremePOCColor, EnableVolSeqSignal = enableVolSeqSignal, VolSeqDiamondOffset = volSeqDiamondOffset, EnableStackedImbSignal = enableStackedImbSignal, StackedImbDiamondOffset = stackedImbDiamondOffset, EnableReversalPOCSignal = enableReversalPOCSignal, ReversalPOCDiamondOffset = reversalPOCDiamondOffset, EnableSweepSignal = enableSweepSignal, SweepDiamondOffset = sweepDiamondOffset, EnableDeltaSeqSignal = enableDeltaSeqSignal, DeltaSeqDiamondOffset = deltaSeqDiamondOffset, EnableDivergenceSignal = enableDivergenceSignal, DivergenceDiamondOffset = divergenceDiamondOffset, EnableDeltaFlipSignal = enableDeltaFlipSignal, DeltaFlipDiamondOffset = deltaFlipDiamondOffset, EnableStoppingVolumeSignal = enableStoppingVolumeSignal, StoppingVolumeDiamondOffset = stoppingVolumeDiamondOffset, EnableAbsorptionSignal = enableAbsorptionSignal, AbsorptionDiamondOffset = absorptionDiamondOffset, EnableExhaustionSignal = enableExhaustionSignal, ExhaustionDiamondOffset = exhaustionDiamondOffset, EnableVAGapSignal = enableVAGapSignal, VAGapDiamondOffset = vAGapDiamondOffset, EnableExtremeRatioSignal = enableExtremeRatioSignal, ExtremeRatioDiamondOffset = extremeRatioDiamondOffset, EnableFadingMomentumSignal = enableFadingMomentumSignal, FadingMomentumDiamondOffset = fadingMomentumDiamondOffset, EnableExtremeVASignal = enableExtremeVASignal, ExtremeVADiamondOffset = extremeVADiamondOffset, EnablePOCInWickSignal = enablePOCInWickSignal, POCInWickDiamondOffset = pOCInWickDiamondOffset, EnableExtremePOCSignal = enableExtremePOCSignal, ExtremePOCDiamondOffset = extremePOCDiamondOffset, CustomPlot1Sources = customPlot1Sources, CustomPlot2Sources = customPlot2Sources, CustomPlot3Sources = customPlot3Sources, EnableCustomPlot1Signal = enableCustomPlot1Signal, CustomPlot1DiamondOffset = customPlot1DiamondOffset, EnableCustomPlot2Signal = enableCustomPlot2Signal, CustomPlot2DiamondOffset = customPlot2DiamondOffset, EnableCustomPlot3Signal = enableCustomPlot3Signal, CustomPlot3DiamondOffset = customPlot3DiamondOffset, EnableVAPOCExtremeComboSignal = enableVAPOCExtremeComboSignal, TriangleFontSize = triangleFontSize, TriangleOffsetTicks = triangleOffsetTicks, VAPOCBullColor = vAPOCBullColor, VAPOCBearColor = vAPOCBearColor }, input, ref cacheAlightenFootprintOrderFlowV00015);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenFootprintOrderFlowV00015 AlightenFootprintOrderFlowV00015(int aggregationInterval, double valueAreaPer, bool useSessionOpenForAggregation, int volumeSeqLookback, int stackedImbalanceLookback, int deltaSequenceLookback, int sweepLookback, int divergenceLookback, bool useSessionHiLoDivergence, int stoppingVolumeLookback, int deltaFlipLookback, int fadingMomentumLookback, double imbFact, double largeRatioThreshold, double smallRatioThreshold, int deltaThreshold1, int deltaThreshold2, int volumeThreshold, int nearZeroaThreshold, int exhaustionThreshold, int standardFontSize, bool enableFootprint, bool enableFootprintText, bool enableSummaryGrid, bool enableSignalGrid, int signalGridOffset, bool enableSignalGridLegend, bool showSummaryDeltaSignalsRow, bool showSummaryDeltaRows, bool showSummaryCOTRows, bool showSummaryVolumeRow, bool enableDeltaOnBar, int deltaOnBarFontSize, System.Windows.Media.Brush deltaOnBarPositiveColor, System.Windows.Media.Brush deltaOnBarNegativeColor, int deltaOnBarOffset, System.Windows.Media.Brush defaultFootprintTextColor, System.Windows.Media.Brush positiveImbalanceTextColor, System.Windows.Media.Brush negativeImbalanceTextColor, System.Windows.Media.Brush barUpColor, System.Windows.Media.Brush barDownColor, System.Windows.Media.Brush barUpOutlineColor, System.Windows.Media.Brush barDownOutlineColor, System.Windows.Media.Brush barUpVAColor, System.Windows.Media.Brush barDownVAColor, int vAOpacity, System.Windows.Media.Brush pOCColor, System.Windows.Media.Brush pOCOutlineColor, System.Windows.Media.Brush pOCTextColor, System.Windows.Media.Brush deltaLowPositiveColor, System.Windows.Media.Brush deltaMediumPositiveColor, System.Windows.Media.Brush deltaHighPositiveColor, System.Windows.Media.Brush deltaLowNegativeColor, System.Windows.Media.Brush deltaMediumNegativeColor, System.Windows.Media.Brush deltaHighNegativeColor, System.Windows.Media.Brush highVolumeColor, System.Windows.Media.Brush zeroDeltaColor, System.Windows.Media.Brush summaryGridLineColor, int summaryGridLineThickness, System.Windows.Media.Brush volSeqColor, System.Windows.Media.Brush stackedImbColor, System.Windows.Media.Brush revPOCColor, System.Windows.Media.Brush sweepColor, System.Windows.Media.Brush deltaSeqColor, System.Windows.Media.Brush divergenceColor, System.Windows.Media.Brush absorptionColor, System.Windows.Media.Brush exhaustionColor, System.Windows.Media.Brush vAGapColor, System.Windows.Media.Brush extremeRatioColor, System.Windows.Media.Brush deltaFlipColor, System.Windows.Media.Brush stoppingVolumeColor, System.Windows.Media.Brush fadingMomentumColor, System.Windows.Media.Brush extremeVAColor, System.Windows.Media.Brush pOCInWickColor, System.Windows.Media.Brush extremePOCColor, bool enableVolSeqSignal, int volSeqDiamondOffset, bool enableStackedImbSignal, int stackedImbDiamondOffset, bool enableReversalPOCSignal, int reversalPOCDiamondOffset, bool enableSweepSignal, int sweepDiamondOffset, bool enableDeltaSeqSignal, int deltaSeqDiamondOffset, bool enableDivergenceSignal, int divergenceDiamondOffset, bool enableDeltaFlipSignal, int deltaFlipDiamondOffset, bool enableStoppingVolumeSignal, int stoppingVolumeDiamondOffset, bool enableAbsorptionSignal, int absorptionDiamondOffset, bool enableExhaustionSignal, int exhaustionDiamondOffset, bool enableVAGapSignal, int vAGapDiamondOffset, bool enableExtremeRatioSignal, int extremeRatioDiamondOffset, bool enableFadingMomentumSignal, int fadingMomentumDiamondOffset, bool enableExtremeVASignal, int extremeVADiamondOffset, bool enablePOCInWickSignal, int pOCInWickDiamondOffset, bool enableExtremePOCSignal, int extremePOCDiamondOffset, string customPlot1Sources, string customPlot2Sources, string customPlot3Sources, bool enableCustomPlot1Signal, int customPlot1DiamondOffset, bool enableCustomPlot2Signal, int customPlot2DiamondOffset, bool enableCustomPlot3Signal, int customPlot3DiamondOffset, bool enableVAPOCExtremeComboSignal, int triangleFontSize, int triangleOffsetTicks, System.Windows.Media.Brush vAPOCBullColor, System.Windows.Media.Brush vAPOCBearColor)
		{
			return indicator.AlightenFootprintOrderFlowV00015(Input, aggregationInterval, valueAreaPer, useSessionOpenForAggregation, volumeSeqLookback, stackedImbalanceLookback, deltaSequenceLookback, sweepLookback, divergenceLookback, useSessionHiLoDivergence, stoppingVolumeLookback, deltaFlipLookback, fadingMomentumLookback, imbFact, largeRatioThreshold, smallRatioThreshold, deltaThreshold1, deltaThreshold2, volumeThreshold, nearZeroaThreshold, exhaustionThreshold, standardFontSize, enableFootprint, enableFootprintText, enableSummaryGrid, enableSignalGrid, signalGridOffset, enableSignalGridLegend, showSummaryDeltaSignalsRow, showSummaryDeltaRows, showSummaryCOTRows, showSummaryVolumeRow, enableDeltaOnBar, deltaOnBarFontSize, deltaOnBarPositiveColor, deltaOnBarNegativeColor, deltaOnBarOffset, defaultFootprintTextColor, positiveImbalanceTextColor, negativeImbalanceTextColor, barUpColor, barDownColor, barUpOutlineColor, barDownOutlineColor, barUpVAColor, barDownVAColor, vAOpacity, pOCColor, pOCOutlineColor, pOCTextColor, deltaLowPositiveColor, deltaMediumPositiveColor, deltaHighPositiveColor, deltaLowNegativeColor, deltaMediumNegativeColor, deltaHighNegativeColor, highVolumeColor, zeroDeltaColor, summaryGridLineColor, summaryGridLineThickness, volSeqColor, stackedImbColor, revPOCColor, sweepColor, deltaSeqColor, divergenceColor, absorptionColor, exhaustionColor, vAGapColor, extremeRatioColor, deltaFlipColor, stoppingVolumeColor, fadingMomentumColor, extremeVAColor, pOCInWickColor, extremePOCColor, enableVolSeqSignal, volSeqDiamondOffset, enableStackedImbSignal, stackedImbDiamondOffset, enableReversalPOCSignal, reversalPOCDiamondOffset, enableSweepSignal, sweepDiamondOffset, enableDeltaSeqSignal, deltaSeqDiamondOffset, enableDivergenceSignal, divergenceDiamondOffset, enableDeltaFlipSignal, deltaFlipDiamondOffset, enableStoppingVolumeSignal, stoppingVolumeDiamondOffset, enableAbsorptionSignal, absorptionDiamondOffset, enableExhaustionSignal, exhaustionDiamondOffset, enableVAGapSignal, vAGapDiamondOffset, enableExtremeRatioSignal, extremeRatioDiamondOffset, enableFadingMomentumSignal, fadingMomentumDiamondOffset, enableExtremeVASignal, extremeVADiamondOffset, enablePOCInWickSignal, pOCInWickDiamondOffset, enableExtremePOCSignal, extremePOCDiamondOffset, customPlot1Sources, customPlot2Sources, customPlot3Sources, enableCustomPlot1Signal, customPlot1DiamondOffset, enableCustomPlot2Signal, customPlot2DiamondOffset, enableCustomPlot3Signal, customPlot3DiamondOffset, enableVAPOCExtremeComboSignal, triangleFontSize, triangleOffsetTicks, vAPOCBullColor, vAPOCBearColor);
		}

		public Indicators.AlightenFootprintOrderFlowV00015 AlightenFootprintOrderFlowV00015(ISeries<double> input , int aggregationInterval, double valueAreaPer, bool useSessionOpenForAggregation, int volumeSeqLookback, int stackedImbalanceLookback, int deltaSequenceLookback, int sweepLookback, int divergenceLookback, bool useSessionHiLoDivergence, int stoppingVolumeLookback, int deltaFlipLookback, int fadingMomentumLookback, double imbFact, double largeRatioThreshold, double smallRatioThreshold, int deltaThreshold1, int deltaThreshold2, int volumeThreshold, int nearZeroaThreshold, int exhaustionThreshold, int standardFontSize, bool enableFootprint, bool enableFootprintText, bool enableSummaryGrid, bool enableSignalGrid, int signalGridOffset, bool enableSignalGridLegend, bool showSummaryDeltaSignalsRow, bool showSummaryDeltaRows, bool showSummaryCOTRows, bool showSummaryVolumeRow, bool enableDeltaOnBar, int deltaOnBarFontSize, System.Windows.Media.Brush deltaOnBarPositiveColor, System.Windows.Media.Brush deltaOnBarNegativeColor, int deltaOnBarOffset, System.Windows.Media.Brush defaultFootprintTextColor, System.Windows.Media.Brush positiveImbalanceTextColor, System.Windows.Media.Brush negativeImbalanceTextColor, System.Windows.Media.Brush barUpColor, System.Windows.Media.Brush barDownColor, System.Windows.Media.Brush barUpOutlineColor, System.Windows.Media.Brush barDownOutlineColor, System.Windows.Media.Brush barUpVAColor, System.Windows.Media.Brush barDownVAColor, int vAOpacity, System.Windows.Media.Brush pOCColor, System.Windows.Media.Brush pOCOutlineColor, System.Windows.Media.Brush pOCTextColor, System.Windows.Media.Brush deltaLowPositiveColor, System.Windows.Media.Brush deltaMediumPositiveColor, System.Windows.Media.Brush deltaHighPositiveColor, System.Windows.Media.Brush deltaLowNegativeColor, System.Windows.Media.Brush deltaMediumNegativeColor, System.Windows.Media.Brush deltaHighNegativeColor, System.Windows.Media.Brush highVolumeColor, System.Windows.Media.Brush zeroDeltaColor, System.Windows.Media.Brush summaryGridLineColor, int summaryGridLineThickness, System.Windows.Media.Brush volSeqColor, System.Windows.Media.Brush stackedImbColor, System.Windows.Media.Brush revPOCColor, System.Windows.Media.Brush sweepColor, System.Windows.Media.Brush deltaSeqColor, System.Windows.Media.Brush divergenceColor, System.Windows.Media.Brush absorptionColor, System.Windows.Media.Brush exhaustionColor, System.Windows.Media.Brush vAGapColor, System.Windows.Media.Brush extremeRatioColor, System.Windows.Media.Brush deltaFlipColor, System.Windows.Media.Brush stoppingVolumeColor, System.Windows.Media.Brush fadingMomentumColor, System.Windows.Media.Brush extremeVAColor, System.Windows.Media.Brush pOCInWickColor, System.Windows.Media.Brush extremePOCColor, bool enableVolSeqSignal, int volSeqDiamondOffset, bool enableStackedImbSignal, int stackedImbDiamondOffset, bool enableReversalPOCSignal, int reversalPOCDiamondOffset, bool enableSweepSignal, int sweepDiamondOffset, bool enableDeltaSeqSignal, int deltaSeqDiamondOffset, bool enableDivergenceSignal, int divergenceDiamondOffset, bool enableDeltaFlipSignal, int deltaFlipDiamondOffset, bool enableStoppingVolumeSignal, int stoppingVolumeDiamondOffset, bool enableAbsorptionSignal, int absorptionDiamondOffset, bool enableExhaustionSignal, int exhaustionDiamondOffset, bool enableVAGapSignal, int vAGapDiamondOffset, bool enableExtremeRatioSignal, int extremeRatioDiamondOffset, bool enableFadingMomentumSignal, int fadingMomentumDiamondOffset, bool enableExtremeVASignal, int extremeVADiamondOffset, bool enablePOCInWickSignal, int pOCInWickDiamondOffset, bool enableExtremePOCSignal, int extremePOCDiamondOffset, string customPlot1Sources, string customPlot2Sources, string customPlot3Sources, bool enableCustomPlot1Signal, int customPlot1DiamondOffset, bool enableCustomPlot2Signal, int customPlot2DiamondOffset, bool enableCustomPlot3Signal, int customPlot3DiamondOffset, bool enableVAPOCExtremeComboSignal, int triangleFontSize, int triangleOffsetTicks, System.Windows.Media.Brush vAPOCBullColor, System.Windows.Media.Brush vAPOCBearColor)
		{
			return indicator.AlightenFootprintOrderFlowV00015(input, aggregationInterval, valueAreaPer, useSessionOpenForAggregation, volumeSeqLookback, stackedImbalanceLookback, deltaSequenceLookback, sweepLookback, divergenceLookback, useSessionHiLoDivergence, stoppingVolumeLookback, deltaFlipLookback, fadingMomentumLookback, imbFact, largeRatioThreshold, smallRatioThreshold, deltaThreshold1, deltaThreshold2, volumeThreshold, nearZeroaThreshold, exhaustionThreshold, standardFontSize, enableFootprint, enableFootprintText, enableSummaryGrid, enableSignalGrid, signalGridOffset, enableSignalGridLegend, showSummaryDeltaSignalsRow, showSummaryDeltaRows, showSummaryCOTRows, showSummaryVolumeRow, enableDeltaOnBar, deltaOnBarFontSize, deltaOnBarPositiveColor, deltaOnBarNegativeColor, deltaOnBarOffset, defaultFootprintTextColor, positiveImbalanceTextColor, negativeImbalanceTextColor, barUpColor, barDownColor, barUpOutlineColor, barDownOutlineColor, barUpVAColor, barDownVAColor, vAOpacity, pOCColor, pOCOutlineColor, pOCTextColor, deltaLowPositiveColor, deltaMediumPositiveColor, deltaHighPositiveColor, deltaLowNegativeColor, deltaMediumNegativeColor, deltaHighNegativeColor, highVolumeColor, zeroDeltaColor, summaryGridLineColor, summaryGridLineThickness, volSeqColor, stackedImbColor, revPOCColor, sweepColor, deltaSeqColor, divergenceColor, absorptionColor, exhaustionColor, vAGapColor, extremeRatioColor, deltaFlipColor, stoppingVolumeColor, fadingMomentumColor, extremeVAColor, pOCInWickColor, extremePOCColor, enableVolSeqSignal, volSeqDiamondOffset, enableStackedImbSignal, stackedImbDiamondOffset, enableReversalPOCSignal, reversalPOCDiamondOffset, enableSweepSignal, sweepDiamondOffset, enableDeltaSeqSignal, deltaSeqDiamondOffset, enableDivergenceSignal, divergenceDiamondOffset, enableDeltaFlipSignal, deltaFlipDiamondOffset, enableStoppingVolumeSignal, stoppingVolumeDiamondOffset, enableAbsorptionSignal, absorptionDiamondOffset, enableExhaustionSignal, exhaustionDiamondOffset, enableVAGapSignal, vAGapDiamondOffset, enableExtremeRatioSignal, extremeRatioDiamondOffset, enableFadingMomentumSignal, fadingMomentumDiamondOffset, enableExtremeVASignal, extremeVADiamondOffset, enablePOCInWickSignal, pOCInWickDiamondOffset, enableExtremePOCSignal, extremePOCDiamondOffset, customPlot1Sources, customPlot2Sources, customPlot3Sources, enableCustomPlot1Signal, customPlot1DiamondOffset, enableCustomPlot2Signal, customPlot2DiamondOffset, enableCustomPlot3Signal, customPlot3DiamondOffset, enableVAPOCExtremeComboSignal, triangleFontSize, triangleOffsetTicks, vAPOCBullColor, vAPOCBearColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenFootprintOrderFlowV00015 AlightenFootprintOrderFlowV00015(int aggregationInterval, double valueAreaPer, bool useSessionOpenForAggregation, int volumeSeqLookback, int stackedImbalanceLookback, int deltaSequenceLookback, int sweepLookback, int divergenceLookback, bool useSessionHiLoDivergence, int stoppingVolumeLookback, int deltaFlipLookback, int fadingMomentumLookback, double imbFact, double largeRatioThreshold, double smallRatioThreshold, int deltaThreshold1, int deltaThreshold2, int volumeThreshold, int nearZeroaThreshold, int exhaustionThreshold, int standardFontSize, bool enableFootprint, bool enableFootprintText, bool enableSummaryGrid, bool enableSignalGrid, int signalGridOffset, bool enableSignalGridLegend, bool showSummaryDeltaSignalsRow, bool showSummaryDeltaRows, bool showSummaryCOTRows, bool showSummaryVolumeRow, bool enableDeltaOnBar, int deltaOnBarFontSize, System.Windows.Media.Brush deltaOnBarPositiveColor, System.Windows.Media.Brush deltaOnBarNegativeColor, int deltaOnBarOffset, System.Windows.Media.Brush defaultFootprintTextColor, System.Windows.Media.Brush positiveImbalanceTextColor, System.Windows.Media.Brush negativeImbalanceTextColor, System.Windows.Media.Brush barUpColor, System.Windows.Media.Brush barDownColor, System.Windows.Media.Brush barUpOutlineColor, System.Windows.Media.Brush barDownOutlineColor, System.Windows.Media.Brush barUpVAColor, System.Windows.Media.Brush barDownVAColor, int vAOpacity, System.Windows.Media.Brush pOCColor, System.Windows.Media.Brush pOCOutlineColor, System.Windows.Media.Brush pOCTextColor, System.Windows.Media.Brush deltaLowPositiveColor, System.Windows.Media.Brush deltaMediumPositiveColor, System.Windows.Media.Brush deltaHighPositiveColor, System.Windows.Media.Brush deltaLowNegativeColor, System.Windows.Media.Brush deltaMediumNegativeColor, System.Windows.Media.Brush deltaHighNegativeColor, System.Windows.Media.Brush highVolumeColor, System.Windows.Media.Brush zeroDeltaColor, System.Windows.Media.Brush summaryGridLineColor, int summaryGridLineThickness, System.Windows.Media.Brush volSeqColor, System.Windows.Media.Brush stackedImbColor, System.Windows.Media.Brush revPOCColor, System.Windows.Media.Brush sweepColor, System.Windows.Media.Brush deltaSeqColor, System.Windows.Media.Brush divergenceColor, System.Windows.Media.Brush absorptionColor, System.Windows.Media.Brush exhaustionColor, System.Windows.Media.Brush vAGapColor, System.Windows.Media.Brush extremeRatioColor, System.Windows.Media.Brush deltaFlipColor, System.Windows.Media.Brush stoppingVolumeColor, System.Windows.Media.Brush fadingMomentumColor, System.Windows.Media.Brush extremeVAColor, System.Windows.Media.Brush pOCInWickColor, System.Windows.Media.Brush extremePOCColor, bool enableVolSeqSignal, int volSeqDiamondOffset, bool enableStackedImbSignal, int stackedImbDiamondOffset, bool enableReversalPOCSignal, int reversalPOCDiamondOffset, bool enableSweepSignal, int sweepDiamondOffset, bool enableDeltaSeqSignal, int deltaSeqDiamondOffset, bool enableDivergenceSignal, int divergenceDiamondOffset, bool enableDeltaFlipSignal, int deltaFlipDiamondOffset, bool enableStoppingVolumeSignal, int stoppingVolumeDiamondOffset, bool enableAbsorptionSignal, int absorptionDiamondOffset, bool enableExhaustionSignal, int exhaustionDiamondOffset, bool enableVAGapSignal, int vAGapDiamondOffset, bool enableExtremeRatioSignal, int extremeRatioDiamondOffset, bool enableFadingMomentumSignal, int fadingMomentumDiamondOffset, bool enableExtremeVASignal, int extremeVADiamondOffset, bool enablePOCInWickSignal, int pOCInWickDiamondOffset, bool enableExtremePOCSignal, int extremePOCDiamondOffset, string customPlot1Sources, string customPlot2Sources, string customPlot3Sources, bool enableCustomPlot1Signal, int customPlot1DiamondOffset, bool enableCustomPlot2Signal, int customPlot2DiamondOffset, bool enableCustomPlot3Signal, int customPlot3DiamondOffset, bool enableVAPOCExtremeComboSignal, int triangleFontSize, int triangleOffsetTicks, System.Windows.Media.Brush vAPOCBullColor, System.Windows.Media.Brush vAPOCBearColor)
		{
			return indicator.AlightenFootprintOrderFlowV00015(Input, aggregationInterval, valueAreaPer, useSessionOpenForAggregation, volumeSeqLookback, stackedImbalanceLookback, deltaSequenceLookback, sweepLookback, divergenceLookback, useSessionHiLoDivergence, stoppingVolumeLookback, deltaFlipLookback, fadingMomentumLookback, imbFact, largeRatioThreshold, smallRatioThreshold, deltaThreshold1, deltaThreshold2, volumeThreshold, nearZeroaThreshold, exhaustionThreshold, standardFontSize, enableFootprint, enableFootprintText, enableSummaryGrid, enableSignalGrid, signalGridOffset, enableSignalGridLegend, showSummaryDeltaSignalsRow, showSummaryDeltaRows, showSummaryCOTRows, showSummaryVolumeRow, enableDeltaOnBar, deltaOnBarFontSize, deltaOnBarPositiveColor, deltaOnBarNegativeColor, deltaOnBarOffset, defaultFootprintTextColor, positiveImbalanceTextColor, negativeImbalanceTextColor, barUpColor, barDownColor, barUpOutlineColor, barDownOutlineColor, barUpVAColor, barDownVAColor, vAOpacity, pOCColor, pOCOutlineColor, pOCTextColor, deltaLowPositiveColor, deltaMediumPositiveColor, deltaHighPositiveColor, deltaLowNegativeColor, deltaMediumNegativeColor, deltaHighNegativeColor, highVolumeColor, zeroDeltaColor, summaryGridLineColor, summaryGridLineThickness, volSeqColor, stackedImbColor, revPOCColor, sweepColor, deltaSeqColor, divergenceColor, absorptionColor, exhaustionColor, vAGapColor, extremeRatioColor, deltaFlipColor, stoppingVolumeColor, fadingMomentumColor, extremeVAColor, pOCInWickColor, extremePOCColor, enableVolSeqSignal, volSeqDiamondOffset, enableStackedImbSignal, stackedImbDiamondOffset, enableReversalPOCSignal, reversalPOCDiamondOffset, enableSweepSignal, sweepDiamondOffset, enableDeltaSeqSignal, deltaSeqDiamondOffset, enableDivergenceSignal, divergenceDiamondOffset, enableDeltaFlipSignal, deltaFlipDiamondOffset, enableStoppingVolumeSignal, stoppingVolumeDiamondOffset, enableAbsorptionSignal, absorptionDiamondOffset, enableExhaustionSignal, exhaustionDiamondOffset, enableVAGapSignal, vAGapDiamondOffset, enableExtremeRatioSignal, extremeRatioDiamondOffset, enableFadingMomentumSignal, fadingMomentumDiamondOffset, enableExtremeVASignal, extremeVADiamondOffset, enablePOCInWickSignal, pOCInWickDiamondOffset, enableExtremePOCSignal, extremePOCDiamondOffset, customPlot1Sources, customPlot2Sources, customPlot3Sources, enableCustomPlot1Signal, customPlot1DiamondOffset, enableCustomPlot2Signal, customPlot2DiamondOffset, enableCustomPlot3Signal, customPlot3DiamondOffset, enableVAPOCExtremeComboSignal, triangleFontSize, triangleOffsetTicks, vAPOCBullColor, vAPOCBearColor);
		}

		public Indicators.AlightenFootprintOrderFlowV00015 AlightenFootprintOrderFlowV00015(ISeries<double> input , int aggregationInterval, double valueAreaPer, bool useSessionOpenForAggregation, int volumeSeqLookback, int stackedImbalanceLookback, int deltaSequenceLookback, int sweepLookback, int divergenceLookback, bool useSessionHiLoDivergence, int stoppingVolumeLookback, int deltaFlipLookback, int fadingMomentumLookback, double imbFact, double largeRatioThreshold, double smallRatioThreshold, int deltaThreshold1, int deltaThreshold2, int volumeThreshold, int nearZeroaThreshold, int exhaustionThreshold, int standardFontSize, bool enableFootprint, bool enableFootprintText, bool enableSummaryGrid, bool enableSignalGrid, int signalGridOffset, bool enableSignalGridLegend, bool showSummaryDeltaSignalsRow, bool showSummaryDeltaRows, bool showSummaryCOTRows, bool showSummaryVolumeRow, bool enableDeltaOnBar, int deltaOnBarFontSize, System.Windows.Media.Brush deltaOnBarPositiveColor, System.Windows.Media.Brush deltaOnBarNegativeColor, int deltaOnBarOffset, System.Windows.Media.Brush defaultFootprintTextColor, System.Windows.Media.Brush positiveImbalanceTextColor, System.Windows.Media.Brush negativeImbalanceTextColor, System.Windows.Media.Brush barUpColor, System.Windows.Media.Brush barDownColor, System.Windows.Media.Brush barUpOutlineColor, System.Windows.Media.Brush barDownOutlineColor, System.Windows.Media.Brush barUpVAColor, System.Windows.Media.Brush barDownVAColor, int vAOpacity, System.Windows.Media.Brush pOCColor, System.Windows.Media.Brush pOCOutlineColor, System.Windows.Media.Brush pOCTextColor, System.Windows.Media.Brush deltaLowPositiveColor, System.Windows.Media.Brush deltaMediumPositiveColor, System.Windows.Media.Brush deltaHighPositiveColor, System.Windows.Media.Brush deltaLowNegativeColor, System.Windows.Media.Brush deltaMediumNegativeColor, System.Windows.Media.Brush deltaHighNegativeColor, System.Windows.Media.Brush highVolumeColor, System.Windows.Media.Brush zeroDeltaColor, System.Windows.Media.Brush summaryGridLineColor, int summaryGridLineThickness, System.Windows.Media.Brush volSeqColor, System.Windows.Media.Brush stackedImbColor, System.Windows.Media.Brush revPOCColor, System.Windows.Media.Brush sweepColor, System.Windows.Media.Brush deltaSeqColor, System.Windows.Media.Brush divergenceColor, System.Windows.Media.Brush absorptionColor, System.Windows.Media.Brush exhaustionColor, System.Windows.Media.Brush vAGapColor, System.Windows.Media.Brush extremeRatioColor, System.Windows.Media.Brush deltaFlipColor, System.Windows.Media.Brush stoppingVolumeColor, System.Windows.Media.Brush fadingMomentumColor, System.Windows.Media.Brush extremeVAColor, System.Windows.Media.Brush pOCInWickColor, System.Windows.Media.Brush extremePOCColor, bool enableVolSeqSignal, int volSeqDiamondOffset, bool enableStackedImbSignal, int stackedImbDiamondOffset, bool enableReversalPOCSignal, int reversalPOCDiamondOffset, bool enableSweepSignal, int sweepDiamondOffset, bool enableDeltaSeqSignal, int deltaSeqDiamondOffset, bool enableDivergenceSignal, int divergenceDiamondOffset, bool enableDeltaFlipSignal, int deltaFlipDiamondOffset, bool enableStoppingVolumeSignal, int stoppingVolumeDiamondOffset, bool enableAbsorptionSignal, int absorptionDiamondOffset, bool enableExhaustionSignal, int exhaustionDiamondOffset, bool enableVAGapSignal, int vAGapDiamondOffset, bool enableExtremeRatioSignal, int extremeRatioDiamondOffset, bool enableFadingMomentumSignal, int fadingMomentumDiamondOffset, bool enableExtremeVASignal, int extremeVADiamondOffset, bool enablePOCInWickSignal, int pOCInWickDiamondOffset, bool enableExtremePOCSignal, int extremePOCDiamondOffset, string customPlot1Sources, string customPlot2Sources, string customPlot3Sources, bool enableCustomPlot1Signal, int customPlot1DiamondOffset, bool enableCustomPlot2Signal, int customPlot2DiamondOffset, bool enableCustomPlot3Signal, int customPlot3DiamondOffset, bool enableVAPOCExtremeComboSignal, int triangleFontSize, int triangleOffsetTicks, System.Windows.Media.Brush vAPOCBullColor, System.Windows.Media.Brush vAPOCBearColor)
		{
			return indicator.AlightenFootprintOrderFlowV00015(input, aggregationInterval, valueAreaPer, useSessionOpenForAggregation, volumeSeqLookback, stackedImbalanceLookback, deltaSequenceLookback, sweepLookback, divergenceLookback, useSessionHiLoDivergence, stoppingVolumeLookback, deltaFlipLookback, fadingMomentumLookback, imbFact, largeRatioThreshold, smallRatioThreshold, deltaThreshold1, deltaThreshold2, volumeThreshold, nearZeroaThreshold, exhaustionThreshold, standardFontSize, enableFootprint, enableFootprintText, enableSummaryGrid, enableSignalGrid, signalGridOffset, enableSignalGridLegend, showSummaryDeltaSignalsRow, showSummaryDeltaRows, showSummaryCOTRows, showSummaryVolumeRow, enableDeltaOnBar, deltaOnBarFontSize, deltaOnBarPositiveColor, deltaOnBarNegativeColor, deltaOnBarOffset, defaultFootprintTextColor, positiveImbalanceTextColor, negativeImbalanceTextColor, barUpColor, barDownColor, barUpOutlineColor, barDownOutlineColor, barUpVAColor, barDownVAColor, vAOpacity, pOCColor, pOCOutlineColor, pOCTextColor, deltaLowPositiveColor, deltaMediumPositiveColor, deltaHighPositiveColor, deltaLowNegativeColor, deltaMediumNegativeColor, deltaHighNegativeColor, highVolumeColor, zeroDeltaColor, summaryGridLineColor, summaryGridLineThickness, volSeqColor, stackedImbColor, revPOCColor, sweepColor, deltaSeqColor, divergenceColor, absorptionColor, exhaustionColor, vAGapColor, extremeRatioColor, deltaFlipColor, stoppingVolumeColor, fadingMomentumColor, extremeVAColor, pOCInWickColor, extremePOCColor, enableVolSeqSignal, volSeqDiamondOffset, enableStackedImbSignal, stackedImbDiamondOffset, enableReversalPOCSignal, reversalPOCDiamondOffset, enableSweepSignal, sweepDiamondOffset, enableDeltaSeqSignal, deltaSeqDiamondOffset, enableDivergenceSignal, divergenceDiamondOffset, enableDeltaFlipSignal, deltaFlipDiamondOffset, enableStoppingVolumeSignal, stoppingVolumeDiamondOffset, enableAbsorptionSignal, absorptionDiamondOffset, enableExhaustionSignal, exhaustionDiamondOffset, enableVAGapSignal, vAGapDiamondOffset, enableExtremeRatioSignal, extremeRatioDiamondOffset, enableFadingMomentumSignal, fadingMomentumDiamondOffset, enableExtremeVASignal, extremeVADiamondOffset, enablePOCInWickSignal, pOCInWickDiamondOffset, enableExtremePOCSignal, extremePOCDiamondOffset, customPlot1Sources, customPlot2Sources, customPlot3Sources, enableCustomPlot1Signal, customPlot1DiamondOffset, enableCustomPlot2Signal, customPlot2DiamondOffset, enableCustomPlot3Signal, customPlot3DiamondOffset, enableVAPOCExtremeComboSignal, triangleFontSize, triangleOffsetTicks, vAPOCBullColor, vAPOCBearColor);
		}
	}
}

#endregion

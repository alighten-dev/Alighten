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
#endregion

//This namespace holds MarketAnalyzerColumns in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public class AlightenMAPatternCSignalColumnV0001 : MarketAnalyzerColumn
    {
        private NinjaTrader.NinjaScript.Indicators.AlightenLevelMultiTimeframeMATickPatternCV0002 core;

        // Cached last signal (+1 / 0 / -1)
        private double lastSig = 0;

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Long back color", GroupName="Signal", Order=0)]
        public Brush LongBackColor { get; set; }

        [Browsable(false)]
        public string LongBackColorSerialize
        {
            get { return Serialize.BrushToString(LongBackColor); }
            set { LongBackColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Short back color", GroupName="Signal", Order=1)]
        public Brush ShortBackColor { get; set; }

        [Browsable(false)]
        public string ShortBackColorSerialize
        {
            get { return Serialize.BrushToString(ShortBackColor); }
            set { ShortBackColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Neutral back color", GroupName="Signal", Order=2)]
        public Brush NeutralBackColor { get; set; }

        [Browsable(false)]
        public string NeutralBackColorSerialize
        {
            get { return Serialize.BrushToString(NeutralBackColor); }
            set { NeutralBackColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Active font color", GroupName="Signal", Order=3)]
        public Brush ActiveFontColor { get; set; }

        [Browsable(false)]
        public string ActiveFontColorSerialize
        {
            get { return Serialize.BrushToString(ActiveFontColor); }
            set { ActiveFontColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Inactive font color", GroupName="Signal", Order=4)]
        public Brush InactiveFontColor { get; set; }

        [Browsable(false)]
        public string InactiveFontColorSerialize
        {
            get { return Serialize.BrushToString(InactiveFontColor); }
            set { InactiveFontColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 500000)]
        [Display(Name="Bars To Process", GroupName="Signal", Order=5)]
        public int BarsToProcess { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                 = "AlightenMAPatternCSignalColumnV0001";
                Calculate            = Calculate.OnEachTick;
                IsDataSeriesRequired = true;

                DataType       = typeof(double);
                FormatDecimals = 0;
                IsEditable     = false;

                // colors
                LongBackColor     = Brushes.MediumSeaGreen;
                ShortBackColor    = Brushes.Magenta;
                NeutralBackColor  = Brushes.Transparent;
                ActiveFontColor   = Brushes.White;
                InactiveFontColor = Brushes.Gray;
                BarsToProcess     = 400;
            }
            else if (State == State.Configure)
            {
                // Host preload required in MA
                AddDataSeries(BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                core = AlightenLevelMultiTimeframeMATickPatternCV0002(
                    Input,
                    barsToProcess:             BarsToProcess,
                    enablePatternCSignal:      true,
                    enablePatternCLevels:      false,
					drawPatternC:              false, 
                    patternCMarkerFontSize:    14,
                    patternCMarkerOffsetTicks: 6,
                    patternCLongColor:         Brushes.Lime,
                    patternCShortColor:        Brushes.Magenta,
                    patternCLevelWidth:        2,
                    patternCLevelDashStyle:    DashStyleHelper.Dash,                    
                    requirePivotForPatternCSignal: false,
                    showZigZag:           false,
                    zigZagColor:          Brushes.Yellow,
                    zigZagWidth:          2,
                    zigZagStyle:          DashStyleHelper.Solid                                       
                );
            }
        }

        protected override void OnBarUpdate()
		{
		    if (core == null || CurrentBar < 0)
		        return;
		
		    if (BarsInProgress != 0)
		        return;
		
		    // Use LIVE PatternCSignal only (intrabar)
		    double live = 0;
		
		    try
		    {
		        // Live may be NaN if not set yet
		        live = core.PatternCSignal != null ? core.PatternCSignal[0] : 0;
		        if (double.IsNaN(live)) live = 0;
		    }
		    catch
		    {
		        // If a series isn't ready (rare, but can happen on load), treat as neutral
		        live = 0;
		    }
		
		    lastSig = live;
		
		    CurrentValue = lastSig;
		}

        public override string Format(double value)
        {
            if (CellConditions.Count == 0)
            {
                if (value > 0.5) // LONG
                {
                    BackColor = LongBackColor;
                    ForeColor = ActiveFontColor;
                    return " 🡅";
                }
                else if (value < -0.5) // SHORT
                {
                    BackColor = ShortBackColor;
                    ForeColor = ActiveFontColor;
                    return " 🡇";
                }
                else
                {
                    BackColor = NeutralBackColor;
                    ForeColor = InactiveFontColor;
                    return string.Empty;
                }
            }

            if (value > 0.5)  return " 🡅";
            if (value < -0.5) return " 🡇";
            return string.Empty;
        }
    }
}

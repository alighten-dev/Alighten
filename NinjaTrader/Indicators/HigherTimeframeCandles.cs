#region Using declarations
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.DrawingTools;
using System;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
    public class HigherTimeframeCandles : Indicator
	{
        private int priorBar = 0;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
                Description = @"Shows the levels of a higher-timeframe candle on a lower-timeframe chart.";
                Name = "Higher timeframe candles";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                //Disable this property if your indicator requires custom values that cumulate with each new market data event. 
                //See Help Guide for additional information.
                IsSuspendedWhileInactive = true;
                PeriodType = BarsPeriodType.Minute;
                Period = 60;
                WickColor = Brushes.DarkGray;
                BullishColor = Brushes.LimeGreen;
                BearishColor = Brushes.Red;
                Opacity = 20;
                OutlineOpacity = 100;
                BorderWidth = 1;
            }
            else if (State == State.Configure)
			{
                //Print($"Configuring {this.Name} for {this.PeriodType} {this.Period}");
                AddDataSeries(this.PeriodType, this.Period);
            }
        }

		protected override void OnBarUpdate()
		{
            if (CurrentBars[1] < 2)
            {
                return;
            }

            var currentBar = CurrentBars[1];

            var bar = new PriceBar(Opens[1][0], Closes[1][0], Highs[1][0], Lows[1][0], CurrentBars[1], Times[1][0]);
            var previousBar = new PriceBar(Opens[1][1], Closes[1][1], Highs[1][1], Lows[1][1], CurrentBars[1] - 1, Times[1][1]);

            var bodyColor = bar.IsBullish ? BullishColor : BearishColor;
            var wickBorderColor = this.BorderWidth == 0 ? Brushes.Transparent : this.WickColor;
            var bodyBorderColor = this.BorderWidth == 0 ? Brushes.Transparent : bodyColor;

            var rectangle = Draw.Rectangle(this, $"hightf-wick{currentBar}", this.IsAutoScale, previousBar.Time, bar.BodyHigh, bar.Time, bar.High, wickBorderColor, WickColor, this.Opacity);
            rectangle.OutlineStroke.Width = this.BorderWidth;
            rectangle.OutlineStroke.Opacity = this.OutlineOpacity;

            rectangle = Draw.Rectangle(this, $"hightf-tail{currentBar}", this.IsAutoScale, previousBar.Time, bar.BodyLow, bar.Time, bar.Low, wickBorderColor, WickColor, this.Opacity);
            rectangle.OutlineStroke.Width = this.BorderWidth;
            rectangle.OutlineStroke.Opacity = this.OutlineOpacity;

            rectangle = Draw.Rectangle(this, $"hightf-body{currentBar}", this.IsAutoScale, previousBar.Time, bar.BodyLow, bar.Time, bar.BodyHigh, bodyBorderColor, bodyColor, this.Opacity);
            rectangle.OutlineStroke.Width = this.BorderWidth;
            rectangle.OutlineStroke.Opacity = this.OutlineOpacity;
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Period type", Description = "Period type for higher timeframe", Order = 50, GroupName = "Parameters")]
        public BarsPeriodType PeriodType { get; set; }

        [NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Period", Description = "Period length", Order = 100, GroupName = "Parameters")]
		public int Period { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bullish color", Description = "Body color when bullish", Order = 200, GroupName = "Parameters")]
        public Brush BullishColor { get; set; }

        [Browsable(false)]
        public string BullishColorSerializable
        {
            get { return Serialize.BrushToString(BullishColor); }
            set { BullishColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bearish color", Description = "Body color when bearish", Order = 300, GroupName = "Parameters")]
        public Brush BearishColor { get; set; }

        [Browsable(false)]
        public string BearishColorSerializable
        {
            get { return Serialize.BrushToString(BearishColor); }
            set { BearishColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Wick color", Description = "Color for the wick area", Order = 400, GroupName = "Parameters")]
		public Brush WickColor { get; set; }

		[Browsable(false)]
		public string WickColorSerializable
		{
			get { return Serialize.BrushToString(WickColor); }
			set { WickColor = Serialize.StringToBrush(value); }
		}			
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Body/wick opacity", Description = "Candle/wick opacity (0 - 100%)", Order = 450, GroupName = "Parameters")]
        public int Opacity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Outline opacity", Description = "Outline opacity (0 - 100%)", Order = 460, GroupName = "Parameters")]
        public int OutlineOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5)]
        [Display(Name = "Border width", Description = "0 = transparent.", Order = 475, GroupName = "Parameters")]
        public int BorderWidth { get; set; }
        #endregion

        private class PriceBar
        {
            private double _open;
            private double _close;
            private double _high;
            private double _low;
            private int _barNumber;
            private DateTime _time;

            public PriceBar(double open, double close, double high, double low, int barNumber, DateTime time)
            {
                _open = open;
                _close = close;
                _high = high;
                _low = low;
                _barNumber = barNumber;
                _time = time;
            }

            public double Open => _open;

            public double Close => _close;

            public double High => _high;

            public double Low => _low;

            public DateTime Time => _time;

            public bool IsBullish => _open < _close;

            public bool IsBearish => _open > _close;

            public double BodyHigh => IsBullish ? _close : _open;

            public double BodyLow => IsBearish ? _close : _open;

            public double BodySize => IsBullish ? _close - _open : _open - _close;

            public double WickSize => IsBullish ? _high - _close : _high - _open;

            public double TailSize => IsBullish ? _open - _low : _close - _low;

            public double TotalSize => _high - _low;

            public int BarNumber => _barNumber;
        }
    }
}

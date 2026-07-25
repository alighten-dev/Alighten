#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenBarTimerV0004 : Indicator
    {
        private DateTime now = Core.Globals.Now;
        private bool connected = false;
        private bool hasRealtimeData = false;
        private SessionIterator sessionIterator;
        private SimpleFont fontSetting;
        private System.Windows.Threading.DispatcherTimer timer;

        private string currentText = string.Empty;
        private readonly object textLock = new object();
        
        private TextFormat dxTextFormat;
        private SharpDX.Direct2D1.Brush dxTextBrush;

        #region Properties
        [NinjaScriptProperty]
        [Range(6, 72)]
        [Display(Name="Font Size", GroupName="Parameters", Order=2)]
        public int FontSize { get; set; } = 20;
		
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
                Description      = "Bar Timer using OnRender to eliminate flashing.";
                Name             = "AlightenBarTimerV0004";
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
                if (timer == null && IsVisible)
                {
                    if (Bars.BarsType.IsTimeBased && Bars.BarsType.IsIntraday)
                    {
                        lock (textLock) { currentText = GetStatusText(); }
                    }
                    else
                    {
                        lock (textLock) { currentText = "NOT TIME BASED"; }
                    }
                }
            }
            else if (State == State.Terminated)
            {
                if (timer != null)
                {
                    timer.IsEnabled = false;
                    timer = null;
                }
            }
        }

        public override void OnRenderTargetChanged()
        {
            base.OnRenderTargetChanged();

            if (dxTextFormat != null)
            {
                dxTextFormat.Dispose();
                dxTextFormat = null;
            }

            if (dxTextBrush != null)
            {
                dxTextBrush.Dispose();
                dxTextBrush = null;
            }

            if (RenderTarget != null)
            {
                try 
                {
                    SimpleFont f = fontSetting ?? new SimpleFont("Arial", FontSize);
                    dxTextFormat = new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory,
                        f.Family.ToString(),
                        SharpDX.DirectWrite.FontWeight.Bold,
                        SharpDX.DirectWrite.FontStyle.Normal,
                        (float)f.Size);
                    
                    dxTextFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
                    dxTextFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Bottom;

                    dxTextBrush = TimerColor.ToDxBrush(RenderTarget);
                } 
                catch (Exception ex)
                {
                    Print("[BarTimer OnRenderTargetChanged] Error: " + ex.Message);
                }
            }
        }

        protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs args)
        {
            if (args.PriceStatus == ConnectionStatus.Connected
                && args.Connection.InstrumentTypes.Contains(Instrument.MasterInstrument.InstrumentType)
                && Bars.BarsType.IsTimeBased
                && Bars.BarsType.IsIntraday)
            {
                connected = true;

                if (DisplayTime() && timer == null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        timer = new System.Windows.Threading.DispatcherTimer { Interval = new TimeSpan(0, 0, 1), IsEnabled = true };
                        timer.Tick += OnTimerTick;
                    });
                }
            }
            else if (args.PriceStatus == ConnectionStatus.Disconnected)
            {
                connected = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (State == State.Realtime)
            {
                hasRealtimeData = true;
                connected = true;
            }
            if (hasRealtimeData) 
            {
                lock (textLock) { currentText = GetStatusText(); }
            }
        }

        private bool DisplayTime() => ChartControl != null && Bars?.Instrument.MarketData != null && IsVisible;

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (DisplayTime())
            {
                if (timer != null && !timer.IsEnabled)
                    timer.IsEnabled = true;

                lock (textLock) { currentText = GetStatusText(); }
                ForceRefresh();
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || chartControl == null || CurrentBar < 0) return;
            if (dxTextFormat == null || dxTextBrush == null || RenderTarget == null) return;
            
            string textToDraw;
            lock (textLock) { textToDraw = currentText; }
            if (string.IsNullOrEmpty(textToDraw)) return;

            int lastBarIdx = ChartBars.Count - 1;
            if (lastBarIdx < 0) return;

            int cb = CurrentBar;
            if (cb < 0 || cb >= High.Count) return;

            try 
            {
                // Get X coordinate using standard NinjaTrader method
                float x = chartControl.GetXByBarIndex(ChartBars, lastBarIdx);
                
                // Get High of the current active bar
                double highPrice = High.GetValueAt(cb); 
                float y = chartScale.GetYByValue(highPrice);

                // Apply Offset Upward
                y -= TimerOffset;

                // Define bounds. Paragraph alignment is Bottom, drawing exactly ON the Y offset line.
                float width = 300f;
                float height = 50f;
                RectangleF rect = new RectangleF(x - (width / 2f), y - height, width, height);
                
                RenderTarget.DrawText(textToDraw, dxTextFormat, rect, dxTextBrush);
            }
            catch (Exception ex)
            {
                Print("[BarTimer OnRender] Error: " + ex.Message);
            }
        }

        private string GetStatusText()
        {
            if (!connected) return "DISCONNECTED";
            if (!SessionIterator.IsInSession(Now, false, true)) return "PAUSED";
            if (!hasRealtimeData) return "WAITING";

            var span = Bars.GetTime(Bars.Count - 1).Subtract(Now);
            if (span.Ticks < 0) span = TimeSpan.Zero;
            
            double barLengthMin;
            if (BarsPeriod.BarsPeriodType == BarsPeriodType.Second)
                barLengthMin = BarsPeriod.Value / 60.0;
            else if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
                barLengthMin = BarsPeriod.Value;
            else
                barLengthMin = 61; // treat as >60min

            if (barLengthMin > 60)
                return $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
            else if (barLengthMin >= 1)
                return $"{span.Minutes:00}:{span.Seconds:00}";
            else
                return $"{span.Seconds:00}";
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

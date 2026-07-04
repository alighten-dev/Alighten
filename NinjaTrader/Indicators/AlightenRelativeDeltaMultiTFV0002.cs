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

using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using DashStyleHelper = NinjaTrader.Gui.DashStyleHelper;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenRelativeDeltaMultiTFV0002 : Indicator
    {
        private AlightenRelativeDeltaV0001 rdw60;
        private AlightenRelativeDeltaV0001 rdw30;
        private AlightenRelativeDeltaV0001 rdw15;
        private AlightenRelativeDeltaV0001 rdw10;
        private AlightenRelativeDeltaV0001 rdw5;

        private int idx60 = -1;
        private int idx30 = -1;
        private int idx15 = -1;
        private int idx10 = -1;
        private int idx5  = -1;

        private DateTime lastEvaluatedTime = DateTime.MinValue;

        private int lastAlertState = 0; // 1 = all positive, -1 = all negative, 0 = mixed/neutral

        private class RDWRecord
        {
            public DateTime Time;
            public double RDW60;
            public double RDW30;
            public double RDW15;
            public double RDW10;
            public double RDW5;
        }

        private List<RDWRecord> rdwRecords;
        private readonly object listLock = new object();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                 = @"Multi-Timeframe Relative Delta Wick Heatmap with all-timeframe alignment audio alerts";
                Name                                        = "AlightenRelativeDeltaMultiTFV0002";
                
                Calculate                                   = Calculate.OnEachTick; // REALTIME
                
                IsOverlay                                   = false;
                MaximumBarsLookBack                         = MaximumBarsLookBack.Infinite;
                DisplayInDataBox                            = false;
                DrawOnPricePanel                            = false;
                DrawHorizontalGridLines                     = false;
                DrawVerticalGridLines                       = false;
                PaintPriceMarkers                           = false;
                ScaleJustification                          = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive                    = false;

                // Relative Delta properties
                AggregationInterval                         = 6;
                AnchorPeriod                                = 1;
                RelativeDeltaMinimumPercentage              = 50;
                RelativeDeltaMaxPosition                    = 2;
                RelativeDeltaMinimumWickLevels              = 1;
                BarsToProcess                               = 200;

                PositiveColor                               = Brushes.DeepSkyBlue;
                NegativeColor                               = Brushes.Fuchsia;
                DualSignalColor                             = Brushes.Gold;
                NeutralColor                                = Brushes.DimGray;

                EnableAllPositiveAlert                      = true;
                EnableAllNegativeAlert                      = true;
                TreatDualAsBothForAlerts                    = false;
                AllPositiveAlertSound                       = "Alert1.wav";
                AllNegativeAlertSound                       = "Alert2.wav";
                AlertRearmSeconds                           = 60;
            }
            else if (State == State.Configure)
            {
                if (!(BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 60))
                    AddDataSeries(BarsPeriodType.Minute, 60);
                if (!(BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 30))
                    AddDataSeries(BarsPeriodType.Minute, 30);
                if (!(BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 15))
                    AddDataSeries(BarsPeriodType.Minute, 15);
                if (!(BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 10))
                    AddDataSeries(BarsPeriodType.Minute, 10);
                if (!(BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 5))
                    AddDataSeries(BarsPeriodType.Minute, 5);
                
                // Pre-load the 1 Tick series so the child indicators don't throw an exception when they request it
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                rdwRecords = new List<RDWRecord>();

                for (int i = 0; i < BarsArray.Length; i++)
                {
                    if (BarsArray[i].BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
                    {
                        if (BarsArray[i].BarsPeriod.Value == 60) idx60 = i;
                        else if (BarsArray[i].BarsPeriod.Value == 30) idx30 = i;
                        else if (BarsArray[i].BarsPeriod.Value == 15 && i > 0) idx15 = i;
                        else if (BarsArray[i].BarsPeriod.Value == 10) idx10 = i;
                        else if (BarsArray[i].BarsPeriod.Value == 5) idx5 = i;
                    }
                }
                
                if (idx15 == -1 && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 15)
                    idx15 = 0;

                // Instantiate child indicators. We pass true to enable the signal calculation which populates Values[0][0]
                if (idx60 != -1) rdw60 = AlightenRelativeDeltaV0001(BarsArray[idx60], AggregationInterval, 70, AnchorPeriod, 16, true, 15, RelativeDeltaMinimumPercentage, RelativeDeltaMaxPosition, RelativeDeltaMinimumWickLevels, BarsToProcess, false, false);
                if (idx30 != -1) rdw30 = AlightenRelativeDeltaV0001(BarsArray[idx30], AggregationInterval, 70, AnchorPeriod, 16, true, 15, RelativeDeltaMinimumPercentage, RelativeDeltaMaxPosition, RelativeDeltaMinimumWickLevels, BarsToProcess, false, false);
                if (idx15 != -1) rdw15 = AlightenRelativeDeltaV0001(BarsArray[idx15], AggregationInterval, 70, AnchorPeriod, 16, true, 15, RelativeDeltaMinimumPercentage, RelativeDeltaMaxPosition, RelativeDeltaMinimumWickLevels, BarsToProcess, false, false);
                if (idx10 != -1) rdw10 = AlightenRelativeDeltaV0001(BarsArray[idx10], AggregationInterval, 70, AnchorPeriod, 16, true, 15, RelativeDeltaMinimumPercentage, RelativeDeltaMaxPosition, RelativeDeltaMinimumWickLevels, BarsToProcess, false, false);
                if (idx5  != -1) rdw5  = AlightenRelativeDeltaV0001(BarsArray[idx5],  AggregationInterval, 70, AnchorPeriod, 16, true, 15, RelativeDeltaMinimumPercentage, RelativeDeltaMaxPosition, RelativeDeltaMinimumWickLevels, BarsToProcess, false, false);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 0) return;

            try 
            {
                // Force evaluation of child indicators to prevent NT8 from skipping them.
                if (BarsInProgress == idx60 && rdw60 != null) { rdw60.Update(); }
                if (BarsInProgress == idx30 && rdw30 != null) { rdw30.Update(); }
                if (BarsInProgress == idx15 && rdw15 != null) { rdw15.Update(); }
                if (BarsInProgress == idx10 && rdw10 != null) { rdw10.Update(); }
                if (BarsInProgress == idx5  && rdw5  != null) { rdw5.Update(); }

                int tickIdx = BarsArray.Length - 1;

                // We record the tape continuously on the Tick series update (BIP = tickIdx).
                // Because the Tick series is added last, this guarantees that ALL minute series 
                // have fully processed the current tick and advanced their CurrentBar states, 
                // perfectly eliminating any cross-timeframe synchronization lag.
                if (BarsInProgress == tickIdx && CurrentBars[idx5] >= 0)
                {
                    if (BarsToProcess > 0)
                    {
                        // Explicitly check against the primary series (0), NOT the Tick series.
                        int cutoffBar = BarsArray[0].Count - BarsToProcess;
                        if (CurrentBars[0] < cutoffBar) return;
                    }

                    DateTime time = BarsArray[idx5].GetTime(CurrentBars[idx5]);

                    bool isNew5mBar = false;
                    lock (listLock)
                    {
                        if (lastEvaluatedTime != time)
                        {
                            isNew5mBar = true;
                            lastEvaluatedTime = time;
                        }
                    }

                    if (State == State.Historical)
                    {
                        if (!isNew5mBar) return; // Skip all intermediate ticks to eliminate lag!

                        // Evaluate the footprint of the PREVIOUS fully closed 5-minute bar
                        DateTime prevTime = time.AddMinutes(-5);
                        
                        lock (listLock)
                        {
                            // Backfill any missing gaps (data holes or skips)
                            if (rdwRecords.Count > 0)
                            {
                                DateTime expectedTime = rdwRecords[rdwRecords.Count - 1].Time.AddMinutes(5);
                                while (expectedTime < prevTime)
                                {
                                    rdwRecords.Add(new RDWRecord() 
                                    { 
                                        Time = expectedTime, 
                                        RDW60 = rdw60 != null ? rdw60.GetLiveSignal(expectedTime) : 0,
                                        RDW30 = rdw30 != null ? rdw30.GetLiveSignal(expectedTime) : 0,
                                        RDW15 = rdw15 != null ? rdw15.GetLiveSignal(expectedTime) : 0,
                                        RDW10 = rdw10 != null ? rdw10.GetLiveSignal(expectedTime) : 0,
                                        RDW5  = rdw5  != null ? rdw5.GetLiveSignal(expectedTime)  : 0
                                    });
                                    expectedTime = expectedTime.AddMinutes(5);
                                }
                            }

                            rdwRecords.Add(new RDWRecord() 
                            { 
                                Time = prevTime, // Store the closing time of the bar we just evaluated
                                RDW60 = rdw60 != null ? rdw60.GetLiveSignal(prevTime) : 0, 
                                RDW30 = rdw30 != null ? rdw30.GetLiveSignal(prevTime) : 0, 
                                RDW15 = rdw15 != null ? rdw15.GetLiveSignal(prevTime) : 0, 
                                RDW10 = rdw10 != null ? rdw10.GetLiveSignal(prevTime) : 0, 
                                RDW5  = rdw5  != null ? rdw5.GetLiveSignal(prevTime)  : 0 
                            });
                        }
                    }
                    else
                    {
                        // Real-time mode: calculate and update the live footprint on EVERY tick
                        // Force synchronization on every tick to bypass NT8's buggy lower-timeframe event loop
                        if (rdw60 != null) rdw60.Update();
                        if (rdw30 != null) rdw30.Update();
                        if (rdw15 != null) rdw15.Update();
                        if (rdw10 != null) rdw10.Update();
                        if (rdw5  != null) rdw5.Update();

                        double v60 = rdw60 != null ? rdw60.GetLiveSignal() : 0;
                        double v30 = rdw30 != null ? rdw30.GetLiveSignal() : 0;
                        double v15 = rdw15 != null ? rdw15.GetLiveSignal() : 0;
                        double v10 = rdw10 != null ? rdw10.GetLiveSignal() : 0;
                        double v5  = rdw5  != null ? rdw5.GetLiveSignal()  : 0;

                        lock (listLock)
                        {
                            if (!isNew5mBar && rdwRecords.Count > 0)
                            {
                                var last = rdwRecords[rdwRecords.Count - 1];
                                last.RDW60 = v60;
                                last.RDW30 = v30;
                                last.RDW15 = v15;
                                last.RDW10 = v10;
                                last.RDW5  = v5;
                            }
                            else
                            {
                                // Backfill any missing gaps (like the transition from Historical to Realtime!)
                                if (rdwRecords.Count > 0)
                                {
                                    DateTime expectedTime = rdwRecords[rdwRecords.Count - 1].Time.AddMinutes(5);
                                    while (expectedTime < time)
                                    {
                                        rdwRecords.Add(new RDWRecord() 
                                        { 
                                            Time = expectedTime, 
                                            RDW60 = rdw60 != null ? rdw60.GetLiveSignal(expectedTime) : 0,
                                            RDW30 = rdw30 != null ? rdw30.GetLiveSignal(expectedTime) : 0,
                                            RDW15 = rdw15 != null ? rdw15.GetLiveSignal(expectedTime) : 0,
                                            RDW10 = rdw10 != null ? rdw10.GetLiveSignal(expectedTime) : 0,
                                            RDW5  = rdw5  != null ? rdw5.GetLiveSignal(expectedTime)  : 0
                                        });
                                        expectedTime = expectedTime.AddMinutes(5);
                                    }
                                }

                                rdwRecords.Add(new RDWRecord() 
                                { 
                                    Time = time, 
                                    RDW60 = v60, RDW30 = v30, RDW15 = v15, RDW10 = v10, RDW5 = v5 
                                });
                            }
                        }

                        CheckAlignmentAlerts(v60, v30, v15, v10, v5);
                    }
                }
            } 
            catch (Exception ex)
            {
                Print("RelativeDeltaMultiTF ONBARUPDATE CRASH: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        private bool IsPositiveForAlert(double value)
        {
            return value == 1 || (TreatDualAsBothForAlerts && value == 3);
        }

        private bool IsNegativeForAlert(double value)
        {
            return value == -1 || (TreatDualAsBothForAlerts && value == 3);
        }

        private string ResolveSoundFile(string soundFile)
        {
            if (string.IsNullOrWhiteSpace(soundFile))
                return string.Empty;

            if (System.IO.Path.IsPathRooted(soundFile))
                return soundFile;

            return System.IO.Path.Combine(Core.Globals.InstallDir, "sounds", soundFile);
        }

        private void CheckAlignmentAlerts(double v60, double v30, double v15, double v10, double v5)
        {
            if (State != State.Realtime)
                return;

            bool allPositive = IsPositiveForAlert(v60) && IsPositiveForAlert(v30) && IsPositiveForAlert(v15) && IsPositiveForAlert(v10) && IsPositiveForAlert(v5);
            bool allNegative = IsNegativeForAlert(v60) && IsNegativeForAlert(v30) && IsNegativeForAlert(v15) && IsNegativeForAlert(v10) && IsNegativeForAlert(v5);

            int currentAlertState = allPositive ? 1 : allNegative ? -1 : 0;

            // Fire only when alignment changes into all-positive or all-negative.
            if (currentAlertState == lastAlertState)
                return;

            if (currentAlertState == 1 && EnableAllPositiveAlert)
            {
                Alert("All5TimeframesPositive", Priority.High, "All 5 relative-delta timeframes are positive", ResolveSoundFile(AllPositiveAlertSound), AlertRearmSeconds, PositiveColor ?? Brushes.DeepSkyBlue, Brushes.White);
            }
            else if (currentAlertState == -1 && EnableAllNegativeAlert)
            {
                Alert("All5TimeframesNegative", Priority.High, "All 5 relative-delta timeframes are negative", ResolveSoundFile(AllNegativeAlertSound), AlertRearmSeconds, NegativeColor ?? Brushes.Fuchsia, Brushes.White);
            }

            lastAlertState = currentAlertState;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartControl == null || rdw60 == null)
                return;

            try 
            {
                using (SharpDX.Direct2D1.Brush dxPositive = PositiveColor.ToDxBrush(RenderTarget))
                using (SharpDX.Direct2D1.Brush dxNegative = NegativeColor.ToDxBrush(RenderTarget))
                using (SharpDX.Direct2D1.Brush dxDual = DualSignalColor.ToDxBrush(RenderTarget))
                using (SharpDX.Direct2D1.Brush dxNeutral = NeutralColor.ToDxBrush(RenderTarget))
                using (SharpDX.Direct2D1.Brush dxText = ChartControl.Properties.ChartText.ToDxBrush(RenderTarget))
                using (SharpDX.Direct2D1.Brush dxLabelBackground = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(20, 20, 20, 200)))
                using (SharpDX.DirectWrite.TextFormat textFormat = ChartControl.Properties.LabelFont.ToDirectWriteTextFormat())
                {

                    int padding = 2;
                    float panelY = ChartPanel.Y;
                    float panelH = ChartPanel.H;
                    
                    float usableHeight = panelH - (padding * 6);
                    float rowH = usableHeight / 5f;

                    float space = (float)(chartControl.BarSpacingType == BarSpacingType.TimeBased ? chartControl.BarWidth : chartControl.Properties.BarDistance);
                    
                    float leftVisibleX = ChartPanel.X;
                    float rightVisibleX = ChartPanel.X + ChartPanel.W;
                    
                    DateTime viewStartTime;
                    DateTime viewEndTime;
                    try 
                    {
                        viewStartTime = chartControl.GetTimeByX((int)leftVisibleX).AddMinutes(-120); 
                        viewEndTime = chartControl.GetTimeByX((int)rightVisibleX).AddMinutes(120);
                    } 
                    catch 
                    {
                        return;
                    }

                    List<RDWRecord> renderRecords;
                    lock (listLock)
                    {
                        renderRecords = rdwRecords.Where(r => r.Time >= viewStartTime && r.Time <= viewEndTime).ToList();
                    }

                    if (renderRecords.Count == 0) return;

                    // Shift perfectly aligns the sub-slices to the center of the primary chart's physical candles
                    float shift = space / 2f;

                    for (int i = 0; i < renderRecords.Count; i++)
                    {
                        var record = renderRecords[i];
                        
                        // Align the rectangle exactly over the physical duration of the bar that generated it.
                        // The record Time represents the CLOSE of the 5-minute bar.
                        DateTime t1 = record.Time.AddMinutes(-5);
                        float x1 = chartControl.GetXByTime(t1) + shift;
                        
                        float x2 = chartControl.GetXByTime(record.Time) + shift;
                        
                        if (i == renderRecords.Count - 1 && State == State.Realtime)
                        {
                            // The live state extends all the way to the right edge of the screen
                            x2 = rightVisibleX;
                        }
                        
                        if (x2 < leftVisibleX) continue;
                        if (x1 > rightVisibleX) break;
                        
                        float sliceWidth = x2 - x1;
                        if (sliceWidth <= 0) sliceWidth = 1;

                        double[] biases = new double[] { record.RDW60, record.RDW30, record.RDW15, record.RDW10, record.RDW5 };

                        for (int r = 0; r < 5; r++)
                        {
                            SharpDX.Direct2D1.Brush fillBrush = dxNeutral;
                            if (biases[r] == 1) fillBrush = dxPositive;
                            else if (biases[r] == -1) fillBrush = dxNegative;
                            else if (biases[r] == 3) fillBrush = dxDual;

                            float y1 = panelY + padding + r * (rowH + padding);
                            
                            SharpDX.RectangleF rect = new SharpDX.RectangleF(x1, y1, sliceWidth, rowH);
                            RenderTarget.FillRectangle(rect, fillBrush);
                        }
                    }

                    var timeframes = new[] { "60-min", "30-min", "15-min", "10-min", "5-min" };

                    // Draw Labels on the right side AFTER the data loop so they are not covered
                    float rightEdge = ChartPanel.X + ChartPanel.W;
                    for (int r = 0; r < 5; r++)
                    {
                        float y1 = panelY + padding + r * (rowH + padding);
                        using (SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, timeframes[r], textFormat, 100, rowH))
                        {
                            float textWidth = layout.Metrics.Width;
                            float textHeight = layout.Metrics.Height;
                            SharpDX.RectangleF bgRect = new SharpDX.RectangleF(rightEdge - textWidth - 8, y1 + (rowH - textHeight) / 2f - 2, textWidth + 6, textHeight + 4);
                            RenderTarget.FillRectangle(bgRect, dxLabelBackground);
                            RenderTarget.DrawTextLayout(new SharpDX.Vector2(rightEdge - textWidth - 5, y1 + (rowH - textHeight) / 2f), layout, dxText);
                        }
                    }
                }
            } 
            catch (Exception ex)
            {
                Print("RENDER EXCEPTION: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Tick Aggregation", Order = 1, GroupName = "Relative Delta Parameters")]
        public int AggregationInterval { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Anchored Profile Period", Order = 3, GroupName = "Relative Delta Parameters")]
        public int AnchorPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Relative Delta Minimum %", Order = 10, GroupName = "Relative Delta Parameters")]
        public int RelativeDeltaMinimumPercentage { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Relative Delta Max Position", Order = 11, GroupName = "Relative Delta Parameters")]
        public int RelativeDeltaMaxPosition { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Relative Delta Min Wick Levels", Order = 12, GroupName = "Relative Delta Parameters")]
        public int RelativeDeltaMinimumWickLevels { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500000)]
        [Display(Name="Bars To Process (0 = All)", GroupName="Relative Delta Parameters", Order=13)]
        public int BarsToProcess { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Debug Prints", GroupName="Relative Delta Parameters", Order=14)]
        public bool EnableDebugPrints { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name="Enable All Positive Alert", GroupName="Audio Alerts", Order=30)]
        public bool EnableAllPositiveAlert { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable All Negative Alert", GroupName="Audio Alerts", Order=31)]
        public bool EnableAllNegativeAlert { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Treat Dual Signal As Both", GroupName="Audio Alerts", Order=32)]
        public bool TreatDualAsBothForAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name="All Positive Alert Sound", Description="Sound file name in NinjaTrader's sounds folder, or a full path.", GroupName="Audio Alerts", Order=33)]
        public string AllPositiveAlertSound { get; set; }

        [NinjaScriptProperty]
        [Display(Name="All Negative Alert Sound", Description="Sound file name in NinjaTrader's sounds folder, or a full path.", GroupName="Audio Alerts", Order=34)]
        public string AllNegativeAlertSound { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3600)]
        [Display(Name="Alert Rearm Seconds", GroupName="Audio Alerts", Order=35)]
        public int AlertRearmSeconds { get; set; }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="Positive Color (Long Exhaustion)", GroupName="Colors", Order=20)]
        public Brush PositiveColor { get; set; }

        [Browsable(false)]
        public string PositiveColorSerialize
        {
            get { return Serialize.BrushToString(PositiveColor); }
            set { PositiveColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="Negative Color (Short Exhaustion)", GroupName="Colors", Order=21)]
        public Brush NegativeColor { get; set; }

        [Browsable(false)]
        public string NegativeColorSerialize
        {
            get { return Serialize.BrushToString(NegativeColor); }
            set { NegativeColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="Dual Signal Color (Both)", GroupName="Colors", Order=22)]
        public Brush DualSignalColor { get; set; }

        [Browsable(false)]
        public string DualSignalColorSerialize
        {
            get { return Serialize.BrushToString(DualSignalColor); }
            set { DualSignalColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="Neutral Color", GroupName="Colors", Order=23)]
        public Brush NeutralColor { get; set; }

        [Browsable(false)]
        public string NeutralColorSerialize
        {
            get { return Serialize.BrushToString(NeutralColor); }
            set { NeutralColor = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenRelativeDeltaMultiTFV0002[] cacheAlightenRelativeDeltaMultiTFV0002;
		public AlightenRelativeDeltaMultiTFV0002 AlightenRelativeDeltaMultiTFV0002(int aggregationInterval, int anchorPeriod, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableAllPositiveAlert, bool enableAllNegativeAlert, bool treatDualAsBothForAlerts, string allPositiveAlertSound, string allNegativeAlertSound, int alertRearmSeconds, Brush positiveColor, Brush negativeColor, Brush dualSignalColor, Brush neutralColor)
		{
			return AlightenRelativeDeltaMultiTFV0002(Input, aggregationInterval, anchorPeriod, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableAllPositiveAlert, enableAllNegativeAlert, treatDualAsBothForAlerts, allPositiveAlertSound, allNegativeAlertSound, alertRearmSeconds, positiveColor, negativeColor, dualSignalColor, neutralColor);
		}

		public AlightenRelativeDeltaMultiTFV0002 AlightenRelativeDeltaMultiTFV0002(ISeries<double> input, int aggregationInterval, int anchorPeriod, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableAllPositiveAlert, bool enableAllNegativeAlert, bool treatDualAsBothForAlerts, string allPositiveAlertSound, string allNegativeAlertSound, int alertRearmSeconds, Brush positiveColor, Brush negativeColor, Brush dualSignalColor, Brush neutralColor)
		{
			if (cacheAlightenRelativeDeltaMultiTFV0002 != null)
				for (int idx = 0; idx < cacheAlightenRelativeDeltaMultiTFV0002.Length; idx++)
					if (cacheAlightenRelativeDeltaMultiTFV0002[idx] != null && cacheAlightenRelativeDeltaMultiTFV0002[idx].AggregationInterval == aggregationInterval && cacheAlightenRelativeDeltaMultiTFV0002[idx].AnchorPeriod == anchorPeriod && cacheAlightenRelativeDeltaMultiTFV0002[idx].RelativeDeltaMinimumPercentage == relativeDeltaMinimumPercentage && cacheAlightenRelativeDeltaMultiTFV0002[idx].RelativeDeltaMaxPosition == relativeDeltaMaxPosition && cacheAlightenRelativeDeltaMultiTFV0002[idx].RelativeDeltaMinimumWickLevels == relativeDeltaMinimumWickLevels && cacheAlightenRelativeDeltaMultiTFV0002[idx].BarsToProcess == barsToProcess && cacheAlightenRelativeDeltaMultiTFV0002[idx].EnableDebugPrints == enableDebugPrints && cacheAlightenRelativeDeltaMultiTFV0002[idx].EnableAllPositiveAlert == enableAllPositiveAlert && cacheAlightenRelativeDeltaMultiTFV0002[idx].EnableAllNegativeAlert == enableAllNegativeAlert && cacheAlightenRelativeDeltaMultiTFV0002[idx].TreatDualAsBothForAlerts == treatDualAsBothForAlerts && cacheAlightenRelativeDeltaMultiTFV0002[idx].AllPositiveAlertSound == allPositiveAlertSound && cacheAlightenRelativeDeltaMultiTFV0002[idx].AllNegativeAlertSound == allNegativeAlertSound && cacheAlightenRelativeDeltaMultiTFV0002[idx].AlertRearmSeconds == alertRearmSeconds && cacheAlightenRelativeDeltaMultiTFV0002[idx].PositiveColor == positiveColor && cacheAlightenRelativeDeltaMultiTFV0002[idx].NegativeColor == negativeColor && cacheAlightenRelativeDeltaMultiTFV0002[idx].DualSignalColor == dualSignalColor && cacheAlightenRelativeDeltaMultiTFV0002[idx].NeutralColor == neutralColor && cacheAlightenRelativeDeltaMultiTFV0002[idx].EqualsInput(input))
						return cacheAlightenRelativeDeltaMultiTFV0002[idx];
			return CacheIndicator<AlightenRelativeDeltaMultiTFV0002>(new AlightenRelativeDeltaMultiTFV0002(){ AggregationInterval = aggregationInterval, AnchorPeriod = anchorPeriod, RelativeDeltaMinimumPercentage = relativeDeltaMinimumPercentage, RelativeDeltaMaxPosition = relativeDeltaMaxPosition, RelativeDeltaMinimumWickLevels = relativeDeltaMinimumWickLevels, BarsToProcess = barsToProcess, EnableDebugPrints = enableDebugPrints, EnableAllPositiveAlert = enableAllPositiveAlert, EnableAllNegativeAlert = enableAllNegativeAlert, TreatDualAsBothForAlerts = treatDualAsBothForAlerts, AllPositiveAlertSound = allPositiveAlertSound, AllNegativeAlertSound = allNegativeAlertSound, AlertRearmSeconds = alertRearmSeconds, PositiveColor = positiveColor, NegativeColor = negativeColor, DualSignalColor = dualSignalColor, NeutralColor = neutralColor }, input, ref cacheAlightenRelativeDeltaMultiTFV0002);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenRelativeDeltaMultiTFV0002 AlightenRelativeDeltaMultiTFV0002(int aggregationInterval, int anchorPeriod, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableAllPositiveAlert, bool enableAllNegativeAlert, bool treatDualAsBothForAlerts, string allPositiveAlertSound, string allNegativeAlertSound, int alertRearmSeconds, Brush positiveColor, Brush negativeColor, Brush dualSignalColor, Brush neutralColor)
		{
			return indicator.AlightenRelativeDeltaMultiTFV0002(Input, aggregationInterval, anchorPeriod, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableAllPositiveAlert, enableAllNegativeAlert, treatDualAsBothForAlerts, allPositiveAlertSound, allNegativeAlertSound, alertRearmSeconds, positiveColor, negativeColor, dualSignalColor, neutralColor);
		}

		public Indicators.AlightenRelativeDeltaMultiTFV0002 AlightenRelativeDeltaMultiTFV0002(ISeries<double> input , int aggregationInterval, int anchorPeriod, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableAllPositiveAlert, bool enableAllNegativeAlert, bool treatDualAsBothForAlerts, string allPositiveAlertSound, string allNegativeAlertSound, int alertRearmSeconds, Brush positiveColor, Brush negativeColor, Brush dualSignalColor, Brush neutralColor)
		{
			return indicator.AlightenRelativeDeltaMultiTFV0002(input, aggregationInterval, anchorPeriod, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableAllPositiveAlert, enableAllNegativeAlert, treatDualAsBothForAlerts, allPositiveAlertSound, allNegativeAlertSound, alertRearmSeconds, positiveColor, negativeColor, dualSignalColor, neutralColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenRelativeDeltaMultiTFV0002 AlightenRelativeDeltaMultiTFV0002(int aggregationInterval, int anchorPeriod, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableAllPositiveAlert, bool enableAllNegativeAlert, bool treatDualAsBothForAlerts, string allPositiveAlertSound, string allNegativeAlertSound, int alertRearmSeconds, Brush positiveColor, Brush negativeColor, Brush dualSignalColor, Brush neutralColor)
		{
			return indicator.AlightenRelativeDeltaMultiTFV0002(Input, aggregationInterval, anchorPeriod, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableAllPositiveAlert, enableAllNegativeAlert, treatDualAsBothForAlerts, allPositiveAlertSound, allNegativeAlertSound, alertRearmSeconds, positiveColor, negativeColor, dualSignalColor, neutralColor);
		}

		public Indicators.AlightenRelativeDeltaMultiTFV0002 AlightenRelativeDeltaMultiTFV0002(ISeries<double> input , int aggregationInterval, int anchorPeriod, int relativeDeltaMinimumPercentage, int relativeDeltaMaxPosition, int relativeDeltaMinimumWickLevels, int barsToProcess, bool enableDebugPrints, bool enableAllPositiveAlert, bool enableAllNegativeAlert, bool treatDualAsBothForAlerts, string allPositiveAlertSound, string allNegativeAlertSound, int alertRearmSeconds, Brush positiveColor, Brush negativeColor, Brush dualSignalColor, Brush neutralColor)
		{
			return indicator.AlightenRelativeDeltaMultiTFV0002(input, aggregationInterval, anchorPeriod, relativeDeltaMinimumPercentage, relativeDeltaMaxPosition, relativeDeltaMinimumWickLevels, barsToProcess, enableDebugPrints, enableAllPositiveAlert, enableAllNegativeAlert, treatDualAsBothForAlerts, allPositiveAlertSound, allNegativeAlertSound, alertRearmSeconds, positiveColor, negativeColor, dualSignalColor, neutralColor);
		}
	}
}

#endregion

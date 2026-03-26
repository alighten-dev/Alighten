#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Controls;
using System.Windows.Automation;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenMirrorV0012 : Indicator
    {
		#region Class Variables
        private const int NUM_TF = 7;
        
        private class TrackedLevel
        {
            public string Tag;
            public double Price;
            public DateTime HtfStartTime;
            public DateTime HtfEndTime;
            public bool IsLong;
            public string PatternType; // "A", "B", "C" or "E"
            public string Label;
            public int TfIndex; // 0..6
            public int LastUpdatedBip0Bar;
        }

        private AlightenMirrorPtAV0003[] _srcA = new AlightenMirrorPtAV0003[NUM_TF];
        private AlightenMirrorPtBV0003[] _srcB = new AlightenMirrorPtBV0003[NUM_TF];
        private AlightenMirrorPtCV0004[] _srcC = new AlightenMirrorPtCV0004[NUM_TF];
        private AlightenMirrorPtEV0005[] _srcE = new AlightenMirrorPtEV0005[NUM_TF];
        private AlightenLevelMultiTimeframeRetestV0001[] _srcGL = new AlightenLevelMultiTimeframeRetestV0001[NUM_TF];

        private List<TrackedLevel>[] trackedA = new List<TrackedLevel>[NUM_TF];
        private List<TrackedLevel>[] trackedB = new List<TrackedLevel>[NUM_TF];
        private List<TrackedLevel>[] trackedC = new List<TrackedLevel>[NUM_TF];
        private List<TrackedLevel>[] trackedE = new List<TrackedLevel>[NUM_TF];
        private List<TrackedLevel>[] trackedGL = new List<TrackedLevel>[NUM_TF];

        private string[] _tfLabels = { "D", "240m", "60m", "30m", "15m", "10m", "5m" };
        private int[] _tfMinutes = { 1440, 240, 60, 30, 15, 10, 5 };
		
		// Cleanup
		private DateTime[] _lastSeenHtfBarTime = new DateTime[NUM_TF];
		private DateTime _lastPrimaryBarTime = Core.Globals.MinDate;
		private double   _lastPrimaryClose   = double.NaN;
		private double   _lastPrimaryHigh    = double.NaN;
		private double   _lastPrimaryLow     = double.NaN;
		
		// Toolbar
		private NinjaTrader.Gui.Chart.Chart chartWindow;
        private Button cleanupButton;
		
		private class ActiveGLState
		{
		    public TrackedLevel Level;
		    public double LastPrice;
		    public DateTime LastAnchorTime;
		}
		
		private Dictionary<string, ActiveGLState>[] _activeGL = new Dictionary<string, ActiveGLState>[NUM_TF];
		
        // HTF VP passthrough
        private AlightenHTFVPV0003 _srcHTFVP;
        private int _htfVpBipIndex = -1;
        private int _lastProcessedProfileCount = 0;
		
		#endregion
		
		#region Properties
        [NinjaScriptProperty]
        [Display(Name="Enable Pattern A", Order=1, GroupName="1. Patterns")]
        public bool EnablePatternA { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Pattern B", Order=2, GroupName="1. Patterns")]
        public bool EnablePatternB { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Pattern C", Order=3, GroupName="1. Patterns")]
        public bool EnablePatternC { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable Pattern E", Order=4, GroupName="1. Patterns")]
        public bool EnablePatternE { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Src Bars To Process", Order=5, GroupName="1. Patterns")]
        public int SrcBarsToProcess { get; set; }
		
		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name="Mirror Lookback Bars", Order=6, GroupName="1. Patterns")]
		public int MirrorLookbackBars { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name="Enable Invalidated Cleanup", Order=7, GroupName="1. Patterns")]
        public bool EnableInvalidatedCleanup { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Daily", Order=1, GroupName="2. Timeframes")]
        public bool EnableTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. 240 Min", Order=2, GroupName="2. Timeframes")]
        public bool EnableTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. 60 Min", Order=3, GroupName="2. Timeframes")]
        public bool EnableTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. 30 Min", Order=4, GroupName="2. Timeframes")]
        public bool EnableTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. 15 Min", Order=5, GroupName="2. Timeframes")]
        public bool EnableTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. 10 Min", Order=6, GroupName="2. Timeframes")]
        public bool EnableTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. 5 Min", Order=7, GroupName="2. Timeframes")]
        public bool EnableTF7_5m { get; set; }

		[NinjaScriptProperty]
		[Display(Name="1. Daily (GainLoss)", Order=8, GroupName="2. Timeframes")]
		public bool EnableGLTF1_Daily { get; set; }

		[NinjaScriptProperty]
		[Display(Name="2. 240 Min (GainLoss)", Order=9, GroupName="2. Timeframes")]
		public bool EnableGLTF2_240m { get; set; }

		[NinjaScriptProperty]
		[Display(Name="3. 60 Min (GainLoss)", Order=10, GroupName="2. Timeframes")]
		public bool EnableGLTF3_60m { get; set; }

		[NinjaScriptProperty]
		[Display(Name="4. 30 Min (GainLoss)", Order=11, GroupName="2. Timeframes")]
		public bool EnableGLTF4_30m { get; set; }

		[NinjaScriptProperty]
		[Display(Name="5. 15 Min (GainLoss)", Order=12, GroupName="2. Timeframes")]
		public bool EnableGLTF5_15m { get; set; }

		[NinjaScriptProperty]
		[Display(Name="6. 10 Min (GainLoss)", Order=13, GroupName="2. Timeframes")]
		public bool EnableGLTF6_10m { get; set; }

		[NinjaScriptProperty]
		[Display(Name="7. 5 Min (GainLoss)", Order=14, GroupName="2. Timeframes")]
		public bool EnableGLTF7_5m { get; set; }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF1 Color", Order=1, GroupName="3. Colors")]
        public Brush ColorTF1 { get; set; }
        [Browsable(false)]
        public string ColorTF1Serialize { get { return Serialize.BrushToString(ColorTF1); } set { ColorTF1 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF2 Color", Order=2, GroupName="3. Colors")]
        public Brush ColorTF2 { get; set; }
        [Browsable(false)]
        public string ColorTF2Serialize { get { return Serialize.BrushToString(ColorTF2); } set { ColorTF2 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF3 Color", Order=3, GroupName="3. Colors")]
        public Brush ColorTF3 { get; set; }
        [Browsable(false)]
        public string ColorTF3Serialize { get { return Serialize.BrushToString(ColorTF3); } set { ColorTF3 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF4 Color", Order=4, GroupName="3. Colors")]
        public Brush ColorTF4 { get; set; }
        [Browsable(false)]
        public string ColorTF4Serialize { get { return Serialize.BrushToString(ColorTF4); } set { ColorTF4 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF5 Color", Order=5, GroupName="3. Colors")]
        public Brush ColorTF5 { get; set; }
        [Browsable(false)]
        public string ColorTF5Serialize { get { return Serialize.BrushToString(ColorTF5); } set { ColorTF5 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF6 Color", Order=6, GroupName="3. Colors")]
        public Brush ColorTF6 { get; set; }
        [Browsable(false)]
        public string ColorTF6Serialize { get { return Serialize.BrushToString(ColorTF6); } set { ColorTF6 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF7 Color", Order=7, GroupName="3. Colors")]
        public Brush ColorTF7 { get; set; }
        [Browsable(false)]
        public string ColorTF7Serialize { get { return Serialize.BrushToString(ColorTF7); } set { ColorTF7 = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Level Width", Order=1, GroupName="4. Visuals")]
        public int LevelWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern A Style", Order=2, GroupName="4. Visuals")]
        public DashStyleHelper LevelDashStyleA { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern B Style", Order=3, GroupName="4. Visuals")]
        public DashStyleHelper LevelDashStyleB { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern C Style", Order=4, GroupName="4. Visuals")]
        public DashStyleHelper LevelDashStyleC { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern E Style", Order=5, GroupName="4. Visuals")]
        public DashStyleHelper LevelDashStyleE { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="GainLoss Level Width", Order=6, GroupName="4. Visuals")]
        public int LevelWidthGL { get; set; }

        [NinjaScriptProperty]
        [Display(Name="GainLoss TSU1 Style", Order=7, GroupName="4. Visuals")]
        public DashStyleHelper LevelDashStyleTSU1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="GainLoss TSU2 Style", Order=8, GroupName="4. Visuals")]
        public DashStyleHelper LevelDashStyleTSU2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="GainLoss TSD1 Style", Order=9, GroupName="4. Visuals")]
        public DashStyleHelper LevelDashStyleTSD1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="GainLoss TSD2 Style", Order=10, GroupName="4. Visuals")]
        public DashStyleHelper LevelDashStyleTSD2 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name="Label Font Size", Order=11, GroupName="4. Visuals")]
        public int LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Label Offset Ticks", Order=12, GroupName="4. Visuals")]
        public int LabelOffsetTicks { get; set; }
		
        [NinjaScriptProperty]
        [Display(Name="Enable HTF VP Filter", Order=1, GroupName="5. HTF VP")]
        public bool EnableHTFVPFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Show HTF VP Levels", Order=2, GroupName="5. HTF VP")]
        public bool ShowHTFVPLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP Bar Type", Order=3, GroupName="5. HTF VP")]
        public BarsPeriodType HTFVPBarType { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="HTF VP Bar Value", Order=4, GroupName="5. HTF VP")]
        public int HTFVPBarValue { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="HTF VP Value Area %", Order=5, GroupName="5. HTF VP")]
        public double HTFVPValueAreaPercentage { get; set; }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="HTF VP VAH Color", Order=6, GroupName="5. HTF VP")]
        public Brush HTFVPVAHColor { get; set; }
        [Browsable(false)]
        public string HTFVPVAHColorSerialize { get { return Serialize.BrushToString(HTFVPVAHColor); } set { HTFVPVAHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="HTF VP VAL Color", Order=7, GroupName="5. HTF VP")]
        public Brush HTFVPVALColor { get; set; }
        [Browsable(false)]
        public string HTFVPVALColorSerialize { get { return Serialize.BrushToString(HTFVPVALColor); } set { HTFVPVALColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="HTF VP Bars To Process", Order=8, GroupName="5. HTF VP")]
        public int HTFVPBarsToProcess { get; set; }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="HTF VP POC Color", Order=8, GroupName="5. HTF VP")]
        public Brush HTFVPPOCColor { get; set; }
        [Browsable(false)]
        public string HTFVPPOCColorSerialize { get { return Serialize.BrushToString(HTFVPPOCColor); } set { HTFVPPOCColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="HTF VP VAH Width", Order=9, GroupName="5. HTF VP")]
        public int HTFVPVAHWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP VAH Style", Order=10, GroupName="5. HTF VP")]
        public DashStyleHelper HTFVPVAHStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="HTF VP VAL Width", Order=11, GroupName="5. HTF VP")]
        public int HTFVPVALWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP VAL Style", Order=12, GroupName="5. HTF VP")]
        public DashStyleHelper HTFVPVALStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="HTF VP POC Width", Order=13, GroupName="5. HTF VP")]
        public int HTFVPPOCWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP POC Style", Order=14, GroupName="5. HTF VP")]
        public DashStyleHelper HTFVPPOCStyle { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AlightenMirrorV0012";
                Description = "Multi-Timeframe Mirror for Patterns A, B, C, and E.";
                Calculate = Calculate.OnEachTick; 
                IsOverlay = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = false;
                
                EnablePatternA = true;
                EnablePatternB = true;
                EnablePatternC = true;
                EnablePatternE = true;
                SrcBarsToProcess = 400;
				MirrorLookbackBars = 3;
				EnableInvalidatedCleanup = true;
                
                EnableTF1_Daily = true;
                EnableTF2_240m = true;
                EnableTF3_60m = true;
                EnableTF4_30m = true;
                EnableTF5_15m = true;
                EnableTF6_10m = true;
                EnableTF7_5m = true;

				EnableGLTF1_Daily = false;
				EnableGLTF2_240m  = false;
				EnableGLTF3_60m   = true; // Default to 60m per user request
				EnableGLTF4_30m   = false;
				EnableGLTF5_15m   = false;
				EnableGLTF6_10m   = false;
				EnableGLTF7_5m    = false;

                ColorTF1 = Brushes.White;
                ColorTF2 = Brushes.Yellow;
                ColorTF3 = Brushes.Orange;
                ColorTF4 = Brushes.Magenta;
                ColorTF5 = Brushes.DodgerBlue;
                ColorTF6 = Brushes.Cyan;
                ColorTF7 = Brushes.Lime;

                LevelWidth = 3;
                LevelDashStyleA = DashStyleHelper.Dot;
                LevelDashStyleB = DashStyleHelper.DashDot;
                LevelDashStyleC = DashStyleHelper.Solid;
                LevelDashStyleE = DashStyleHelper.Dash; 

                LevelWidthGL = 3;
                LevelDashStyleTSU1 = DashStyleHelper.Solid;
                LevelDashStyleTSU2 = DashStyleHelper.Dash;
                LevelDashStyleTSD1 = DashStyleHelper.Solid;
                LevelDashStyleTSD2 = DashStyleHelper.Dash;
                
                LabelFontSize = 12;
                LabelOffsetTicks = 5;
				
				EnableHTFVPFilter = true;
                ShowHTFVPLevels = true;
                HTFVPBarType = BarsPeriodType.Minute;
                HTFVPBarValue = 60;
                HTFVPValueAreaPercentage = 70;

                HTFVPVAHColor = Brushes.DeepSkyBlue;
                HTFVPVALColor = Brushes.HotPink;
                HTFVPPOCColor = Brushes.Gold;
                HTFVPBarsToProcess = 24;

                HTFVPVAHWidth = 3;
                HTFVPVALWidth = 3;
                HTFVPPOCWidth = 3;

                HTFVPVAHStyle = DashStyleHelper.Solid;
                HTFVPVALStyle = DashStyleHelper.Solid;
                HTFVPPOCStyle = DashStyleHelper.Solid;
            }
            else if (State == State.Configure)
			{
			    // BIP 1: Tick series
			    AddDataSeries(BarsPeriodType.Tick, 1);

			
			    // BIP 2: Daily
			    var dailyPeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Day,
			        Value          = 1
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: dailyPeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );
			
			    // BIP 3: 240-minute
			    var fourHourPeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Minute,
			        Value          = 240
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: fourHourPeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );
			
			    // BIP 4: 60-minute
			    var oneHourPeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Minute,
			        Value          = 60
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: oneHourPeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );
			
			    // BIP 5: 30-minute
			    var thirtyMinutePeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Minute,
			        Value          = 30
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: thirtyMinutePeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );
			
			    // BIP 6: 15-minute
			    var fifteenMinutePeriod = new BarsPeriod
			    {
			        BarsPeriodType = BarsPeriodType.Minute,
			        Value          = 15
			    };
			    AddDataSeries(
			        instrumentName: Instrument.FullName,
			        barsPeriod: fifteenMinutePeriod,
			        barsToLoad: SrcBarsToProcess,
			        tradingHoursName: "CME US Index Futures ETH",
			        isResetOnNewTradingDay: true
			    );
			
			    // BIP 7: 10-minute
			    AddDataSeries(BarsPeriodType.Minute, 10);
			
			    // BIP 8: 5-minute
			    AddDataSeries(BarsPeriodType.Minute, 5);
				
				// BIP 9: Dedicated HTF VP passthrough series
			    AddDataSeries(HTFVPBarType, HTFVPBarValue);
			}
            else if (State == State.DataLoaded)
            {
                for (int t = 0; t < NUM_TF; t++)
                {
					_lastSeenHtfBarTime[t] = Core.Globals.MinDate;
					
                    trackedA[t] = new List<TrackedLevel>();
                    trackedB[t] = new List<TrackedLevel>();
                    trackedC[t] = new List<TrackedLevel>();
                    trackedE[t] = new List<TrackedLevel>();
                    
                    if (!IsTfEnabled(t)) continue;
                    
                    int bipIdx = t + 2; // DataSeries index (2 for Daily, 3 for 240, etc.)
                    
                    if (EnablePatternA)
                    {
                        _srcA[t] = AlightenMirrorPtAV0003(
						    Closes[bipIdx],
						    SrcBarsToProcess,            // barsToProcess
						    true,                        // enablePatternASignal
						    true,                        // enablePatternALevels
						    false,                       // drawPatternA
						    18,                          // patternAMarkerFontSize
						    5,                           // patternAMarkerOffsetTicks
						    Brushes.Lime,                // patternALongColor
						    Brushes.Red,                 // patternAShortColor
						    2,                           // patternALevelWidth
						    DashStyleHelper.Solid,       // patternALevelDashStyle
                            false,                       // showDebugLabels
						    false,                       // showZigZag
						    Brushes.DimGray,             // zigZagColor
						    1,                           // zigZagWidth
						    DashStyleHelper.Solid        // zigZagStyle
						);
                    }
                    if (EnablePatternB)
                    {
                        _srcB[t] = AlightenMirrorPtBV0003(
						    Closes[bipIdx],
						    SrcBarsToProcess,            // barsToProcess
						    true,                        // enablePatternBSignal
						    true,                        // enablePatternBLevels
						    false,                       // drawPatternB
						    18,                          // patternBMarkerFontSize
						    5,                           // patternBMarkerOffsetTicks
						    Brushes.Lime,                // patternBLongColor
						    Brushes.Red,                 // patternBShortColor
						    2,                           // patternBLevelWidth
						    DashStyleHelper.Solid,       // patternBLevelDashStyle
                            true,                        // gateTrendStartCheck
                            false,                       // showDebugLabels
						    false,                       // showZigZag
						    Brushes.DimGray,             // zigZagColor
						    1,                           // zigZagWidth
						    DashStyleHelper.Solid        // zigZagStyle
						);
                    }
                    if (EnablePatternC)
                    {
                        _srcC[t] = AlightenMirrorPtCV0004(
						    Closes[bipIdx],
						    SrcBarsToProcess,            // barsToProcess
						    true,                        // enablePatternCSignal
						    true,                        // enablePatternCLevels
						    false,                       // drawPatternC
						    18,                          // patternCMarkerFontSize
						    5,                           // patternCMarkerOffsetTicks
						    Brushes.Lime,                // patternCLongColor
						    Brushes.Red,                 // patternCShortColor
						    2,                           // patternCLevelWidth
						    DashStyleHelper.Solid,       // patternCLevelDashStyle
						    false,                       // requirePivotForPatternCSignal
						    false,                       // showDebugLabels
						    false,                       // showZigZag
						    Brushes.DimGray,             // zigZagColor
						    1,                           // zigZagWidth
						    DashStyleHelper.Solid        // zigZagStyle
						);
                    }
                    if (EnablePatternE)
                    {
                        _srcE[t] = AlightenMirrorPtEV0005(
						    Closes[bipIdx],
						    SrcBarsToProcess,            // barsToProcess
						    true,                        // enablePatternESignal
						    true,                        // enablePatternELevels
						    false,                       // drawPatternE
						    18,                          // patternEMarkerFontSize
						    5,                           // patternEMarkerOffsetTicks
						    Brushes.Lime,                // patternELongColor
						    Brushes.Red,                 // patternEShortColor
						    2,                           // patternELevelWidth
						    DashStyleHelper.Solid,       // patternELevelDashStyle
						    false,                       // requirePivotForPatternESignal
						    false,                       // showDebugLabels
						    false,                       // showZigZag
						    Brushes.DimGray,             // zigZagColor
						    1,                           // zigZagWidth
						    DashStyleHelper.Solid        // zigZagStyle
						);
                    }
                }
                
                for (int t = 0; t < NUM_TF; t++)
                {
                    trackedGL[t] = new List<TrackedLevel>();
					_activeGL[t] = new Dictionary<string, ActiveGLState>();

                    
                    if (!IsGlTfEnabled(t)) continue;
                    
                    int bipIdx = t + 2; 

                    _srcGL[t] = AlightenLevelMultiTimeframeRetestV0001(
                        Closes[bipIdx],
                        SrcBarsToProcess,            // barsToProcess
                        false,                       // showZigZag
                        Brushes.Yellow,              // zigZagColor
                        2,                           // zigZagWidth
                        DashStyleHelper.Solid,       // zigZagStyle
                        false,                       // showTrendStartLines (Mirror renders these)
                        Brushes.Magenta,             // TSDLineColor
                        3,                           // TSDLineWidth
                        DashStyleHelper.Solid,       // TSD1LineStyle
                        DashStyleHelper.Dash,        // TSD2LineStyle
                        Brushes.Cyan,                // TSULineColor
                        3,                           // TSULineWidth
                        DashStyleHelper.Solid,       // TSU1LineStyle
                        DashStyleHelper.Dash,        // TSU2LineStyle
                        false,                       // showLabels
                        11,                          // labelFontSize
                        5                            // labelOffsetTicks
                    );
                }
				
				if (EnableHTFVPFilter)
                {
                    _srcHTFVP = AlightenHTFVPV0003(
                        Input,
						HTFVPBarsToProcess,
                        HTFVPBarType,
                        HTFVPBarValue,
                        HTFVPValueAreaPercentage,
                        HTFVPVAHWidth,
                        HTFVPVAHStyle,
                        HTFVPVALWidth,
                        HTFVPVALStyle,
                        HTFVPPOCWidth,
                        HTFVPPOCStyle
                    );

                    _htfVpBipIndex = BarsArray.Length - 1;					
                }

				if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        CreateToolbarButton();
                    });
                }
            }
			else if (State == State.Terminated)
            {
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        TryRemoveToolbarButton();
                    });
                }
            }
        }
        
       

        protected override void OnBarUpdate()
		{
		    // Allow sync from any series update so newly finalized HTF bars are seen immediately
		   	if (BarsInProgress < 0 || BarsInProgress >= BarsArray.Length)
		        return;

		    if (CurrentBars[0] < 1)
		        return;

		    for (int i = 1; i < BarsArray.Length; i++)
		        if (CurrentBars[i] < 1)
		            return;
		
			if (BarsInProgress == 0)
			{
                try 
                {
			        _lastPrimaryBarTime = Times[0][0];
			        _lastPrimaryClose   = Closes[0][0];
			        _lastPrimaryHigh    = Highs[0][0];
			        _lastPrimaryLow     = Lows[0][0];
			
			        if (EnableHTFVPFilter)
			        {	
                        if (_srcHTFVP != null)
                        {
                            _srcHTFVP.Update();
                        }
                        
                        if (IsFirstTickOfBar)
                        {
                        }
		        	
			            DrawHTFVPPassthrough();
					    //DrawHTFVPBiasDebugForActiveSegment();
                        
                        if (_srcHTFVP != null && _srcHTFVP.Profiles != null && _srcHTFVP.Profiles.Count > _lastProcessedProfileCount)
                        {
                            var lp = _srcHTFVP.Profiles.LastOrDefault();
                            if (lp != null) 
                            {
                            }
                            
                            ApplyHTFVPBiasClipToTrackedSignals();
                            _lastProcessedProfileCount = _srcHTFVP.Profiles.Count;
                        }
			        }
                }
                catch (Exception ex)
                {
                    Print("[MirrorV0012] CRASH in BIP 0: " + ex.ToString());
                }
			}

            // Detect first tick of a new HTF bar for each enabled timeframe.
            // When that happens, evaluate the just-closed HTF bar and remove any
            // levels invalidated by that bar's final close.
            if (EnableInvalidatedCleanup)
			{
			    for (int t = 0; t < NUM_TF; t++)
			    {
			        if (!IsTfEnabled(t))
			            continue;
			
			        int bipIdx = t + 2;
			
			        if (CurrentBars[bipIdx] < 1)
			            continue;
			
			        DateTime currentHtfBarTime = Times[bipIdx][0];
			
			        if (_lastSeenHtfBarTime[t] == Core.Globals.MinDate)
			        {
			            _lastSeenHtfBarTime[t] = currentHtfBarTime;
			        }
			        else if (_lastSeenHtfBarTime[t] != currentHtfBarTime)
			        {
			            // First tick of a new HTF bar → previous bar just finalized
			            CleanupInvalidatedLevelsForTimeframe(t, bipIdx);
			
			            _lastSeenHtfBarTime[t] = currentHtfBarTime;
			        }
			    }
			}
		
		    SyncLevels();
		}
		
		private void DrawHTFVPPassthrough()
		{
		    if (!EnableHTFVPFilter || !ShowHTFVPLevels || _srcHTFVP == null || _srcHTFVP.Profiles == null || _srcHTFVP.Profiles.Count == 0)
			    return;
			
			if (BarsInProgress != 0)
			    return;
			
            var activeProfile = _srcHTFVP.GetProfileAt(Times[0][0]);
            if (activeProfile == null)
                return;
		        
            DateTime endTime = Times[0][0];
            
            if (activeProfile.EndTime != Core.Globals.MinDate && endTime > activeProfile.EndTime)
                endTime = activeProfile.EndTime;
						
            if (double.IsNaN(activeProfile.VAH) || double.IsNaN(activeProfile.VAL) || double.IsNaN(activeProfile.POC) || activeProfile.POC <= 0)
			{			    
			    return;
			}
			
			if (activeProfile.StartTime >= endTime)
			{
			    return;
			}
		
		    Draw.Line(this, "MR_HTFVP_VAH_" + activeProfile.StartTime.Ticks, false,
		        activeProfile.StartTime, activeProfile.VAH,
		        endTime,                 activeProfile.VAH,
		        HTFVPVAHColor, HTFVPVAHStyle, HTFVPVAHWidth);
		
		    Draw.Line(this, "MR_HTFVP_VAL_" + activeProfile.StartTime.Ticks, false,
		        activeProfile.StartTime, activeProfile.VAL,
		        endTime,                 activeProfile.VAL,
		        HTFVPVALColor, HTFVPVALStyle, HTFVPVALWidth);
		
		    Draw.Line(this, "MR_HTFVP_POC_" + activeProfile.StartTime.Ticks, false,
		        activeProfile.StartTime, activeProfile.POC,
		        endTime,                 activeProfile.POC,
		        HTFVPPOCColor, HTFVPPOCStyle, HTFVPPOCWidth);
		}
		
		private void DrawHTFVPBiasDebugForActiveSegment()
		{
		    if (!EnableHTFVPFilter || _srcHTFVP == null || _srcHTFVP.Profiles == null || _srcHTFVP.Profiles.Count == 0)
		        return;
		
		    if (BarsInProgress != 0)
		        return;
		
            var activeProfile = _srcHTFVP.GetProfileAt(Times[0][0]);
            if (activeProfile == null)
                return;
		
		    string biasText = $"{activeProfile.Bias}";
		    Brush biasBrush = activeProfile.Bias > 0 ? Brushes.Lime : (activeProfile.Bias < 0 ? Brushes.Red : Brushes.Gray);
		
		    // Repaint all primary bars inside the active projected HTF segment
		    for (int barsAgo = 0; barsAgo < CurrentBars[0]; barsAgo++)
		    {
		        DateTime t = Times[0][barsAgo];
		
		        if (t >= activeProfile.EndTime)
		            continue;
		            
		        if (t < activeProfile.StartTime)
		            break;
		
		        string tag = "MR_HTFVP_BIASDBG_" + t.Ticks;
		
		        RemoveDrawObject(tag);
		
		        Draw.Text(
		            this,
		            tag,
		            false,
		            biasText,
		            t,
		            Highs[0][barsAgo] + (4 * TickSize),
		            0,
		            biasBrush,
		            new SimpleFont("Arial", 9),
		            TextAlignment.Center,
		            Brushes.Transparent,
		            Brushes.Transparent,
		            0
		        );
		    }
		}
		
		/// <summary>
		/// Bias Gating and Filtering
		/// </summary>
		private bool TryGetProjectedHTFVPBiasAt(DateTime candidateTime, out double bias)
		{
		    bias = 0.0;
		    if (!EnableHTFVPFilter || _srcHTFVP == null)
		        return false;
		
            var profile = _srcHTFVP.GetProfileAt(candidateTime);
            if (profile == null)
                return false;
		
		    bias = profile.Bias;
		    return true;
		}
		
		private bool PassesHTFVPBiasGate(DateTime candidateTime, bool isLong)
		{
		    if (!EnableHTFVPFilter || _srcHTFVP == null)
		        return true;
		
            var profile = _srcHTFVP.GetProfileAt(candidateTime);
            if (profile == null)
		        return true;   // no projected bias segment covering this time → do not block
		
		    if (profile.Bias > 0)
		        return isLong;
		
		    if (profile.Bias < 0)
		        return !isLong;
		
		    return true;       // neutral / zero bias
		}
		
		private bool OverlapsBiasWindow(DateTime levelStart, DateTime levelEnd, DateTime biasStart, DateTime biasEnd)
		{
		    return levelStart < biasEnd && levelEnd > biasStart;
		}
		
		private bool ViolatesHTFVPBias(TrackedLevel tl)
		{
		    if (!EnableHTFVPFilter || _srcHTFVP == null)
		        return false;
		
            var profile = _srcHTFVP.GetProfileAt(tl.HtfStartTime);
            if (profile == null)
		        return false;
		        
		    if (profile.Bias > 0 && !tl.IsLong)
		        return true;
		
		    if (profile.Bias < 0 && tl.IsLong)
		        return true;
		
		    return false;
		}		
		
		private void ApplyHTFVPBiasClipToTrackedSignals()
		{
		    if (!EnableHTFVPFilter || _srcHTFVP == null || _srcHTFVP.Profiles == null || _srcHTFVP.Profiles.Count == 0)
		        return;
		        
            var profile = _srcHTFVP.Profiles.LastOrDefault();
            if (profile == null || Math.Abs(profile.Bias) < 0.0001)
		        return;
		
		    for (int t = 0; t < NUM_TF; t++)
		    {
		        ClipTrackedListToCurrentBias(trackedA[t]);
		        ClipTrackedListToCurrentBias(trackedB[t]);
		        ClipTrackedListToCurrentBias(trackedC[t]);
		        ClipTrackedListToCurrentBias(trackedE[t]);
		    }
		}
		
		private void ClipTrackedListToCurrentBias(List<TrackedLevel> tList)
		{
		    if (tList == null || tList.Count == 0)
		        return;
		
		    var toRemove = new List<TrackedLevel>();
		
		    foreach (var tl in tList)
		    {
		        if (ViolatesHTFVPBias(tl))
		            toRemove.Add(tl);
		    }
		
		    foreach (var tl in toRemove)
		        RemoveTrackedLevel(tl, tList);
		}
		/// <summary>
		/// End Bias Gating and Filtering
		/// </summary>
		
        private void SyncLevels()
        {
            for (int t = 0; t < NUM_TF; t++)
            {
                if (!IsTfEnabled(t)) continue;
                
                int bipIdx = t + 2; 
                int htfBars = CurrentBars[bipIdx];
                if (htfBars < 2) continue;

				// Sync Pattern A
				if (EnablePatternA && _srcA[t] != null)
				{
				    int lookback = Math.Min(MirrorLookbackBars, htfBars);
				    for (int i = lookback; i >= 0; i--)
				    {
				        if (_srcA[t].PatternALongLevel.Count > i)
				        {
				            SyncSingleSignal(t, bipIdx, i, _srcA[t].PatternALongLevel[i], true, "A", _srcA[t].PatternALongStartTimestamp.Count > i ? _srcA[t].PatternALongStartTimestamp[i] : 0);
				        }
				        if (_srcA[t].PatternAShortLevel.Count > i)
				        {
				            SyncSingleSignal(t, bipIdx, i, _srcA[t].PatternAShortLevel[i], false, "A", _srcA[t].PatternAShortStartTimestamp.Count > i ? _srcA[t].PatternAShortStartTimestamp[i] : 0);
				        }
				    }
				}
				
				// Sync Pattern B
				if (EnablePatternB && _srcB[t] != null)
				{
				    int lookback = Math.Min(MirrorLookbackBars, htfBars);
				    for (int i = lookback; i >= 0; i--)
				    {
				        if (_srcB[t].PatternBLongLevel.Count > i)
				        {
				            SyncSingleSignal(t, bipIdx, i, _srcB[t].PatternBLongLevel[i], true, "B", _srcB[t].PatternBLongStartTimestamp.Count > i ? _srcB[t].PatternBLongStartTimestamp[i] : 0);
				        }
				        if (_srcB[t].PatternBShortLevel.Count > i)
				        {
				            SyncSingleSignal(t, bipIdx, i, _srcB[t].PatternBShortLevel[i], false, "B", _srcB[t].PatternBShortStartTimestamp.Count > i ? _srcB[t].PatternBShortStartTimestamp[i] : 0);
				        }
				    }
				}

                // Sync Pattern C
				if (EnablePatternC && _srcC[t] != null)
				{
				    int lookback = Math.Min(MirrorLookbackBars, htfBars);
				    for (int i = lookback; i >= 0; i--)
				    {
				        if (_srcC[t].PatternCLongLevel.Count > i)
				        {
				            SyncSingleSignal(t, bipIdx, i, _srcC[t].PatternCLongLevel[i], true, "C", _srcC[t].PatternCLongStartTimestamp.Count > i ? _srcC[t].PatternCLongStartTimestamp[i] : 0);
				        }
				
				        if (_srcC[t].PatternCShortLevel.Count > i)
				        {
				            SyncSingleSignal(t, bipIdx, i, _srcC[t].PatternCShortLevel[i], false, "C", _srcC[t].PatternCShortStartTimestamp.Count > i ? _srcC[t].PatternCShortStartTimestamp[i] : 0);
				        }
				    }
				}
				
				// Sync Pattern E
				if (EnablePatternE && _srcE[t] != null)
				{
				    int lookback = Math.Min(MirrorLookbackBars, htfBars);
				    for (int i = lookback; i >= 0; i--)
				    {
				        if (_srcE[t].PatternELongLevel.Count > i)
				        {
				            SyncSingleSignal(t, bipIdx, i, _srcE[t].PatternELongLevel[i], true, "E", _srcE[t].PatternELongStartTimestamp.Count > i ? _srcE[t].PatternELongStartTimestamp[i] : 0);
				        }
				
				        if (_srcE[t].PatternEShortLevel.Count > i)
				        {
				            SyncSingleSignal(t, bipIdx, i, _srcE[t].PatternEShortLevel[i], false, "E", _srcE[t].PatternEShortStartTimestamp.Count > i ? _srcE[t].PatternEShortStartTimestamp[i] : 0);
				        }
				    }
				}
            } // end Timeframe loop

            SyncGainLossLevels();
        }

        private void SyncGainLossLevels()
        {
            for (int t = 0; t < NUM_TF; t++)
            {
                if (!IsGlTfEnabled(t) || _srcGL[t] == null) continue;
                
                int bipIdx = t + 2; 
                if (CurrentBars[bipIdx] < 2) continue;

                double tsu1Price = _srcGL[t].TSU1PriceSeries[0];
                double tsu1Time  = _srcGL[t].TSU1TimeSeries[0];
                double tsu2Price = _srcGL[t].TSU2PriceSeries[0];
                double tsu2Time  = _srcGL[t].TSU2TimeSeries[0];
                double tsd1Price = _srcGL[t].TSD1PriceSeries[0];
                double tsd1Time  = _srcGL[t].TSD1TimeSeries[0];
                double tsd2Price = _srcGL[t].TSD2PriceSeries[0];
                double tsd2Time  = _srcGL[t].TSD2TimeSeries[0];

                SyncSingleGLSignal(t, bipIdx, tsu1Price, tsu1Time, "TSU1");
                SyncSingleGLSignal(t, bipIdx, tsu2Price, tsu2Time, "TSU2");
                SyncSingleGLSignal(t, bipIdx, tsd1Price, tsd1Time, "TSD1");
                SyncSingleGLSignal(t, bipIdx, tsd2Price, tsd2Time, "TSD2");
            }
        }

		private void SyncSingleGLSignal(int t, int bipIdx, double priceLevel, double startTicks, string typeTag)
		{
		    if (_activeGL[t] == null)
		        _activeGL[t] = new Dictionary<string, ActiveGLState>();
		
		    var activeMap = _activeGL[t];
		    DateTime nowTime = Times[bipIdx][0];
		
		    bool hasValidValue = !(double.IsNaN(priceLevel) || double.IsNaN(startTicks) || startTicks <= 0);
		    DateTime anchorTime = hasValidValue ? new DateTime((long)startTicks) : Core.Globals.MinDate;
		
		    ActiveGLState active;
		    activeMap.TryGetValue(typeTag, out active);
		
		    if (active != null && active.Level != null && !trackedGL[t].Contains(active.Level))
		    {
		        activeMap.Remove(typeTag);
		        active = null;
		    }
		
		    if (!hasValidValue)
		    {
		        if (active != null && active.Level != null)
		        {
		            if (active.Level.HtfEndTime < nowTime)
		            {
		                active.Level.HtfEndTime = nowTime;
		                DrawTrackedLevel(active.Level, trackedGL[t]);
		            }
		
		            activeMap.Remove(typeTag);
		        }
		
		        return;
		    }
		
		    if (active == null || active.Level == null)
		    {
		        var newLevel = new TrackedLevel
		        {
		            Tag = $"MR_{typeTag}_TF{t}_{anchorTime.Ticks}_{Math.Round(priceLevel, 4)}",
		            Price = priceLevel,
		            HtfStartTime = anchorTime,
		            HtfEndTime = nowTime,
		            IsLong = typeTag.StartsWith("TSU"),
		            PatternType = typeTag,
		            Label = $"GL {_tfLabels[t]} {typeTag}",
		            TfIndex = t,
		            LastUpdatedBip0Bar = CurrentBars[0]
		        };
		
		        trackedGL[t].Add(newLevel);
		        DrawTrackedLevel(newLevel, trackedGL[t]);
		
		        activeMap[typeTag] = new ActiveGLState
		        {
		            Level = newLevel,
		            LastPrice = priceLevel,
		            LastAnchorTime = anchorTime
		        };
		
		        return;
		    }
		
		    bool sameSegment =
		        Math.Abs(active.LastPrice - priceLevel) < 0.0001 &&
		        active.LastAnchorTime == anchorTime;
		
		    if (sameSegment)
		    {
		        if (active.Level.HtfEndTime < nowTime)
		        {
		            active.Level.HtfEndTime = nowTime;
		            active.Level.LastUpdatedBip0Bar = CurrentBars[0];
		            DrawTrackedLevel(active.Level, trackedGL[t]);
		        }
		
		        return;
		    }
		
		    // State changed: transition at the source-provided anchor time
		    DateTime transitionTime = anchorTime;
		
		    if (transitionTime == Core.Globals.MinDate || transitionTime < active.Level.HtfStartTime)
		        transitionTime = nowTime;
		
		    active.Level.HtfEndTime = transitionTime;
		    DrawTrackedLevel(active.Level, trackedGL[t]);
		
		    var replacement = new TrackedLevel
		    {
		        Tag = $"MR_{typeTag}_TF{t}_{transitionTime.Ticks}_{Math.Round(priceLevel, 4)}",
		        Price = priceLevel,
		        HtfStartTime = transitionTime,
		        HtfEndTime = nowTime,
		        IsLong = typeTag.StartsWith("TSU"),
		        PatternType = typeTag,
		        Label = $"GL {_tfLabels[t]} {typeTag}",
		        TfIndex = t,
		        LastUpdatedBip0Bar = CurrentBars[0]
		    };
		
		    trackedGL[t].Add(replacement);
		    DrawTrackedLevel(replacement, trackedGL[t]);
		
		    activeMap[typeTag] = new ActiveGLState
		    {
		        Level = replacement,
		        LastPrice = priceLevel,
		        LastAnchorTime = anchorTime
		    };
		}
        
//		private void SyncSingleSignal(int t, int bipIdx, int barsAgoOnHtf, double priceLevel, bool isLong, string pType, double startTimestampExtracted)
//		{
//		    if (priceLevel == 0 || startTimestampExtracted == 0)
//		        return;
		
//		    DateTime htfBarCloseTime = Times[bipIdx][barsAgoOnHtf];
		
//		    DateTime htfBarStartTime;
//		    if (barsAgoOnHtf + 1 < CurrentBars[bipIdx])
//		        htfBarStartTime = Times[bipIdx][barsAgoOnHtf + 1];
//		    else
//		        htfBarStartTime = htfBarCloseTime.AddMinutes(-_tfMinutes[t]);
		
//		    // HTF VP bias gate:
//		    // If the projected VP bias window covering this candidate start time disagrees
//		    // with the signal direction, do not create/update the mirrored signal.
//		    if (!PassesHTFVPBiasGate(htfBarStartTime, isLong))
//		        return;
		
//		    var tList = pType == "A" ? trackedA[t]
//		             : pType == "B" ? trackedB[t]
//		             : pType == "C" ? trackedC[t]
//		             : trackedE[t];
		
//		    string tag = $"MR_{pType}_TF{t}_{(isLong ? "L" : "S")}_{Math.Round(priceLevel, 4)}";
		
//		    var existing = tList.FirstOrDefault(x =>
//		        x.PatternType == pType &&
//		        x.IsLong == isLong &&
//		        Math.Abs(x.Price - priceLevel) < 0.0001 &&
//		        x.HtfStartTime == htfBarStartTime
//		    );
		
//		    if (existing != null)
//		    {
//		        if (existing.HtfEndTime < htfBarCloseTime)
//		        {
//		            existing.HtfEndTime = htfBarCloseTime;
//		            existing.LastUpdatedBip0Bar = CurrentBars[0];
//		            DrawTrackedLevel(existing, tList);
//		        }
//		    }
//		    else
//		    {
//		        var newLevel = new TrackedLevel
//		        {
//		            Tag = tag + "_" + htfBarStartTime.Ticks,
//		            Price = priceLevel,
//		            HtfStartTime = htfBarStartTime,
//		            HtfEndTime = htfBarCloseTime,
//		            IsLong = isLong,
//		            PatternType = pType,
//		            Label = $"{pType} {_tfLabels[t]}",
//		            TfIndex = t,
//		            LastUpdatedBip0Bar = CurrentBars[0]
//		        };
		
//		        tList.Add(newLevel);
//		        DrawTrackedLevel(newLevel, tList);
//		    }
//		}
		
		private void SyncSingleSignal(int t, int bipIdx, int barsAgoOnHtf, double priceLevel, bool isLong, string pType, double startTimestampExtracted)
{
    if (priceLevel == 0 || double.IsNaN(startTimestampExtracted) || double.IsInfinity(startTimestampExtracted))
        return;

    DateTime htfBarCloseTime = Times[bipIdx][barsAgoOnHtf];

    DateTime htfBarStartTime;
    if (barsAgoOnHtf + 1 < CurrentBars[bipIdx])
        htfBarStartTime = Times[bipIdx][barsAgoOnHtf + 1];
    else
        htfBarStartTime = htfBarCloseTime.AddMinutes(-_tfMinutes[t]);

    // Use extracted timestamp only if it is a valid drawable NinjaTrader DateTime
    DateTime signalStartTime = htfBarStartTime;
    long extractedTicks = 0;

    try
    {
        extractedTicks = Convert.ToInt64(Math.Round(startTimestampExtracted));
    }
    catch
    {
        extractedTicks = 0;
    }

    if (extractedTicks >= Core.Globals.MinDate.Ticks && extractedTicks <= DateTime.MaxValue.Ticks)
        signalStartTime = new DateTime(extractedTicks);

    // Extra guard: never allow a non-drawable start time through
    if (signalStartTime < Core.Globals.MinDate)
        signalStartTime = htfBarStartTime;

    // Use the source-provided signal timestamp for bias gating when valid,
    // otherwise fall back to the HTF bar start time
    if (!PassesHTFVPBiasGate(signalStartTime, isLong))
        return;

    var tList = pType == "A" ? trackedA[t]
             : pType == "B" ? trackedB[t]
             : pType == "C" ? trackedC[t]
             : trackedE[t];

    string tag = $"MR_{pType}_TF{t}_{(isLong ? "L" : "S")}_{Math.Round(priceLevel, 4)}";

    var existing = tList.FirstOrDefault(x =>
        x.PatternType == pType &&
        x.IsLong == isLong &&
        Math.Abs(x.Price - priceLevel) < 0.0001 &&
        x.HtfStartTime == signalStartTime
    );

    if (existing != null)
    {
        if (existing.HtfEndTime < htfBarCloseTime)
        {
            existing.HtfEndTime = htfBarCloseTime;
            existing.LastUpdatedBip0Bar = CurrentBars[0];
            DrawTrackedLevel(existing, tList);
        }
    }
    else
    {
        var newLevel = new TrackedLevel
        {
            Tag = tag + "_" + signalStartTime.Ticks,
            Price = priceLevel,
            HtfStartTime = signalStartTime,
            HtfEndTime = htfBarCloseTime,
            IsLong = isLong,
            PatternType = pType,
            Label = $"{pType} {_tfLabels[t]}",
            TfIndex = t,
            LastUpdatedBip0Bar = CurrentBars[0]
        };

        tList.Add(newLevel);
        DrawTrackedLevel(newLevel, tList);
    }
}
				
        
        private void DrawTrackedLevel(TrackedLevel tl, List<TrackedLevel> tList)
        {
            Brush c = GetTfColor(tl.TfIndex);
            
            DashStyleHelper dsh = LevelDashStyleA;
            int lw = LevelWidth;

            if (tl.PatternType == "A") dsh = LevelDashStyleA;
            else if (tl.PatternType == "B") dsh = LevelDashStyleB;
            else if (tl.PatternType == "C") dsh = LevelDashStyleC;
            else if (tl.PatternType == "E") dsh = LevelDashStyleE;
            else if (tl.PatternType == "TSU1") { dsh = LevelDashStyleTSU1; lw = LevelWidthGL; }
            else if (tl.PatternType == "TSU2") { dsh = LevelDashStyleTSU2; lw = LevelWidthGL; }
            else if (tl.PatternType == "TSD1") { dsh = LevelDashStyleTSD1; lw = LevelWidthGL; }
            else if (tl.PatternType == "TSD2") { dsh = LevelDashStyleTSD2; lw = LevelWidthGL; }

            Draw.Line(this, tl.Tag, false,
                tl.HtfStartTime, tl.Price,
                tl.HtfEndTime, tl.Price,
                c, dsh, lw);

            if (tl.PatternType.StartsWith("TS"))
            {
                // GL Retest label right aligned.
                Draw.Text(this, tl.Tag + "_lbl", false,
                    tl.Label + (tl.IsLong ? " ▲" : " ▼"),
                    tl.HtfEndTime, tl.Price + (tl.IsLong ? -(LabelOffsetTicks * TickSize) : (LabelOffsetTicks * TickSize)), 0,
                    c, new SimpleFont("Arial", LabelFontSize), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
            }
            else
            {
                // General text anchored on the left.
                Draw.Text(this, tl.Tag + "_lbl", false,
                    tl.Label + (tl.IsLong ? " ▲" : " ▼"),
                    tl.HtfStartTime, tl.Price + (LabelOffsetTicks * TickSize), 0,
                    c, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }
        }
		
		private bool IsTfEnabled(int t)
        {
            switch(t) {
                case 0: return EnableTF1_Daily;
                case 1: return EnableTF2_240m;
                case 2: return EnableTF3_60m;
                case 3: return EnableTF4_30m;
                case 4: return EnableTF5_15m;
                case 5: return EnableTF6_10m;
                case 6: return EnableTF7_5m;
            }
            return false;
        }

        private bool IsGlTfEnabled(int t)
        {
            switch(t) {
                case 0: return EnableGLTF1_Daily;
                case 1: return EnableGLTF2_240m;
                case 2: return EnableGLTF3_60m;
                case 3: return EnableGLTF4_30m;
                case 4: return EnableGLTF5_15m;
                case 5: return EnableGLTF6_10m;
                case 6: return EnableGLTF7_5m;
            }
            return false;
        }

        private Brush GetTfColor(int t)
        {
            switch(t) {
                case 0: return ColorTF1;
                case 1: return ColorTF2;
                case 2: return ColorTF3;
                case 3: return ColorTF4;
                case 4: return ColorTF5;
                case 5: return ColorTF6;
                case 6: return ColorTF7;
            }
            return Brushes.White;
        }
		
		#region Cleanup Helpers


        private void CleanupInvalidatedLevelsForTimeframe(int t, int bipIdx)
        {
            if (CurrentBars[bipIdx] < 2)
                return;

            // We only clean up levels based on the just-closed HTF bar.
            // On the first tick of a new HTF bar:
            //   Times[bipIdx][0] = new HTF bar
            //   Times[bipIdx][1] = just-closed HTF bar
            DateTime justClosedBarCloseTime = Times[bipIdx][1];
            double justClosedBarClosePrice  = Closes[bipIdx][1];

            CleanupInvalidatedList(trackedA[t], justClosedBarCloseTime, justClosedBarClosePrice);
            CleanupInvalidatedList(trackedB[t], justClosedBarCloseTime, justClosedBarClosePrice);
            CleanupInvalidatedList(trackedC[t], justClosedBarCloseTime, justClosedBarClosePrice);
            CleanupInvalidatedList(trackedE[t], justClosedBarCloseTime, justClosedBarClosePrice);
            
            // Note: GL lines don't get "invalidated" strictly by the HTF bar breaking them because 
            // the ReTest indicator explicitly drops them and sets values to double.NaN immediately inside itself. 
            // So we just passively clean up GL lines via CleanupCurrentBarInvalidatedList if they aren't refreshed.
        }

        private void CleanupInvalidatedList(List<TrackedLevel> tList, DateTime justClosedBarCloseTime, double justClosedBarClosePrice)
        {
            if (tList == null || tList.Count == 0)
                return;

            var toRemove = new List<TrackedLevel>();

            foreach (var tl in tList)
            {
                // Only evaluate levels whose mirrored endpoint is the HTF bar that just closed.
                // Those are the levels that were "alive" during that HTF bar's development
                // and now need confirmation/removal based on the final HTF close.
                if (tl.HtfEndTime != justClosedBarCloseTime)
                    continue;

                bool invalidated =
                    tl.IsLong
                        ? justClosedBarClosePrice < tl.Price
                        : justClosedBarClosePrice > tl.Price;

                if (invalidated)
                    toRemove.Add(tl);
            }

            foreach (var tl in toRemove)
                RemoveTrackedLevel(tl, tList);
        }
		#endregion
		
		#region Toolbar Cleanup
		private void CreateToolbarButton()
        {
            try
            {
                if (ChartControl == null)
                    return;

                chartWindow = Window.GetWindow(ChartControl.Parent) as NinjaTrader.Gui.Chart.Chart;
                if (chartWindow == null)
                    return;

                if (cleanupButton != null)
                    return;

                cleanupButton = IndicatorVisualStyleHelper.CreateSettingsButton(
                    "Clean Invalid",
                    "AlightenLevelMirrorCleanupBtn",
                    OnCleanupButtonClick
                );

                cleanupButton.Width = 110;
                cleanupButton.ToolTip = "Remove all currently invalidated mirrored levels";

                chartWindow.MainMenu.Add(cleanupButton);
            }
            catch (Exception ex)
            {
                Print("[AlightenMirrorV0012] toolbar error: " + ex.Message);
            }
        }

        private void TryRemoveToolbarButton()
        {
            try
            {
                if (chartWindow != null && cleanupButton != null)
                    chartWindow.MainMenu.Remove(cleanupButton);
            }
            catch { }

            cleanupButton = null;
            chartWindow = null;
        }

		private void OnCleanupButtonClick(object sender, RoutedEventArgs e)
		{
		    try
		    {
		        PerformManualRefreshCleanup();
		
		        if (ChartControl != null)
		            ChartControl.InvalidateVisual();
		    }
		    catch (Exception ex)
		    {
		        Print("[AlightenMirrorV0012] cleanup button error: " + ex.Message);
		    }
		}
		
		private void RemoveTrackedLevel(TrackedLevel tl, List<TrackedLevel> tList)
		{
		    if (tl == null)
		        return;
		
		    try
		    {
		        if (!string.IsNullOrEmpty(tl.Tag))
		            RemoveDrawObject(tl.Tag);
		    }
		    catch (Exception ex)
		    {
		        Print($"[MirrorCleanup] RemoveDrawObject failed for line tag '{tl.Tag}': {ex.Message}");
		    }
		
		    try
		    {
		        if (!string.IsNullOrEmpty(tl.Tag))
		            RemoveDrawObject(tl.Tag + "_lbl");
		    }
		    catch (Exception ex)
		    {
		        Print($"[MirrorCleanup] RemoveDrawObject failed for label tag '{tl.Tag}_lbl': {ex.Message}");
		    }
		
		    try
		    {
		        tList.Remove(tl);
		    }
		    catch (Exception ex)
		    {
		        Print($"[MirrorCleanup] tList.Remove failed for tag '{tl.Tag}': {ex.Message}");
		    }
		
		    // NEW: if this is a GL level, clear any matching active entry
		    if (tl.PatternType == "TSU1" || tl.PatternType == "TSU2" || tl.PatternType == "TSD1" || tl.PatternType == "TSD2")
		    {
		        int tf = tl.TfIndex;
		
		        if (tf >= 0 && tf < NUM_TF && _activeGL[tf] != null)
		        {
		            ActiveGLState active;
		            if (_activeGL[tf].TryGetValue(tl.PatternType, out active))
		            {
		                if (active != null && object.ReferenceEquals(active.Level, tl))
		                    _activeGL[tf].Remove(tl.PatternType);
		            }
		        }
		    }
		}
		
		private void PerformManualRefreshCleanup()
		{
		    if (_lastPrimaryBarTime == Core.Globals.MinDate || double.IsNaN(_lastPrimaryClose))
		    {
		        return;
		    }
		
		    DateTime primaryTime  = _lastPrimaryBarTime;
		    double   currentPrice = _lastPrimaryClose;
		
		    for (int t = 0; t < NUM_TF; t++)
		    {
		        if (IsTfEnabled(t))
		        {
		            CleanupCurrentBarInvalidatedList(trackedA[t], primaryTime, currentPrice, $"A TF{t}");
		            CleanupCurrentBarInvalidatedList(trackedB[t], primaryTime, currentPrice, $"B TF{t}");
		            CleanupCurrentBarInvalidatedList(trackedC[t], primaryTime, currentPrice, $"C TF{t}");
		            CleanupCurrentBarInvalidatedList(trackedE[t], primaryTime, currentPrice, $"E TF{t}");
		        }
                
                if (IsGlTfEnabled(t))
                {
                    CleanupCurrentBarInvalidatedList(trackedGL[t], primaryTime, currentPrice, $"GL TF{t}");
                }
		    }
		}
		
		private void CleanupCurrentBarInvalidatedList(List<TrackedLevel> tList, DateTime primaryTime, double currentPrice, string debugLabel)
		{
		    if (tList == null)
		    {
		        return;
		    }
		
		    if (tList.Count == 0)
		    {
		        return;
		    }
		
		    var toRemove = new List<TrackedLevel>();
		
		    foreach (var tl in tList)
		    {
		        bool isActiveNow = primaryTime >= tl.HtfStartTime && primaryTime < tl.HtfEndTime;
		
		        if (!isActiveNow)
		            continue;
		
		        bool invalidated = tl.IsLong
		            ? currentPrice < tl.Price
		            : currentPrice > tl.Price;
		
		        if (invalidated)
		        {
		            toRemove.Add(tl);
		        }
		    }
		
		    if (toRemove.Count == 0)
		    {
		        return;
		    }
		
		    foreach (var tl in toRemove)
		    {
		        RemoveTrackedLevel(tl, tList);
		    }
		}
		#endregion
		
		#region Debug
		
		private bool ShouldPrintHTFVPDebugPrimary()
		{
		    if (BarsInProgress != 0)
		        return false;
		
		    if (CurrentBars[0] < 1)
		        return false;
		
		    DateTime t = Times[0][0];
		
		    // Print all first-tick primary bars, plus anything near the hour boundary
		    if (IsFirstTickOfBar)
		        return true;
		
		    if (t.Minute >= 58 || t.Minute <= 2)
		        return true;
		
		    return false;
		}
		
		private string FmtDt(DateTime dt)
		{
		    return dt == Core.Globals.MinDate ? "MinDate" : dt.ToString("yyyy-MM-dd HH:mm:ss");
		}
		
		private string FmtD(double v)
		{
		    return double.IsNaN(v) ? "NaN" : v.ToString("0.00");
		}

		#endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenMirrorV0012[] cacheAlightenMirrorV0012;
		public AlightenMirrorV0012 AlightenMirrorV0012(bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool enableTF2_240m, bool enableTF3_60m, bool enableTF4_30m, bool enableTF5_15m, bool enableTF6_10m, bool enableTF7_5m, bool enableGLTF1_Daily, bool enableGLTF2_240m, bool enableGLTF3_60m, bool enableGLTF4_30m, bool enableGLTF5_15m, bool enableGLTF6_10m, bool enableGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, bool enableHTFVPFilter, bool showHTFVPLevels, BarsPeriodType hTFVPBarType, int hTFVPBarValue, double hTFVPValueAreaPercentage, Brush hTFVPVAHColor, Brush hTFVPVALColor, int hTFVPBarsToProcess, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle)
		{
			return AlightenMirrorV0012(Input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, enableTF2_240m, enableTF3_60m, enableTF4_30m, enableTF5_15m, enableTF6_10m, enableTF7_5m, enableGLTF1_Daily, enableGLTF2_240m, enableGLTF3_60m, enableGLTF4_30m, enableGLTF5_15m, enableGLTF6_10m, enableGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, enableHTFVPFilter, showHTFVPLevels, hTFVPBarType, hTFVPBarValue, hTFVPValueAreaPercentage, hTFVPVAHColor, hTFVPVALColor, hTFVPBarsToProcess, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle);
		}

		public AlightenMirrorV0012 AlightenMirrorV0012(ISeries<double> input, bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool enableTF2_240m, bool enableTF3_60m, bool enableTF4_30m, bool enableTF5_15m, bool enableTF6_10m, bool enableTF7_5m, bool enableGLTF1_Daily, bool enableGLTF2_240m, bool enableGLTF3_60m, bool enableGLTF4_30m, bool enableGLTF5_15m, bool enableGLTF6_10m, bool enableGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, bool enableHTFVPFilter, bool showHTFVPLevels, BarsPeriodType hTFVPBarType, int hTFVPBarValue, double hTFVPValueAreaPercentage, Brush hTFVPVAHColor, Brush hTFVPVALColor, int hTFVPBarsToProcess, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle)
		{
			if (cacheAlightenMirrorV0012 != null)
				for (int idx = 0; idx < cacheAlightenMirrorV0012.Length; idx++)
					if (cacheAlightenMirrorV0012[idx] != null && cacheAlightenMirrorV0012[idx].EnablePatternA == enablePatternA && cacheAlightenMirrorV0012[idx].EnablePatternB == enablePatternB && cacheAlightenMirrorV0012[idx].EnablePatternC == enablePatternC && cacheAlightenMirrorV0012[idx].EnablePatternE == enablePatternE && cacheAlightenMirrorV0012[idx].SrcBarsToProcess == srcBarsToProcess && cacheAlightenMirrorV0012[idx].MirrorLookbackBars == mirrorLookbackBars && cacheAlightenMirrorV0012[idx].EnableInvalidatedCleanup == enableInvalidatedCleanup && cacheAlightenMirrorV0012[idx].EnableTF1_Daily == enableTF1_Daily && cacheAlightenMirrorV0012[idx].EnableTF2_240m == enableTF2_240m && cacheAlightenMirrorV0012[idx].EnableTF3_60m == enableTF3_60m && cacheAlightenMirrorV0012[idx].EnableTF4_30m == enableTF4_30m && cacheAlightenMirrorV0012[idx].EnableTF5_15m == enableTF5_15m && cacheAlightenMirrorV0012[idx].EnableTF6_10m == enableTF6_10m && cacheAlightenMirrorV0012[idx].EnableTF7_5m == enableTF7_5m && cacheAlightenMirrorV0012[idx].EnableGLTF1_Daily == enableGLTF1_Daily && cacheAlightenMirrorV0012[idx].EnableGLTF2_240m == enableGLTF2_240m && cacheAlightenMirrorV0012[idx].EnableGLTF3_60m == enableGLTF3_60m && cacheAlightenMirrorV0012[idx].EnableGLTF4_30m == enableGLTF4_30m && cacheAlightenMirrorV0012[idx].EnableGLTF5_15m == enableGLTF5_15m && cacheAlightenMirrorV0012[idx].EnableGLTF6_10m == enableGLTF6_10m && cacheAlightenMirrorV0012[idx].EnableGLTF7_5m == enableGLTF7_5m && cacheAlightenMirrorV0012[idx].ColorTF1 == colorTF1 && cacheAlightenMirrorV0012[idx].ColorTF2 == colorTF2 && cacheAlightenMirrorV0012[idx].ColorTF3 == colorTF3 && cacheAlightenMirrorV0012[idx].ColorTF4 == colorTF4 && cacheAlightenMirrorV0012[idx].ColorTF5 == colorTF5 && cacheAlightenMirrorV0012[idx].ColorTF6 == colorTF6 && cacheAlightenMirrorV0012[idx].ColorTF7 == colorTF7 && cacheAlightenMirrorV0012[idx].LevelWidth == levelWidth && cacheAlightenMirrorV0012[idx].LevelDashStyleA == levelDashStyleA && cacheAlightenMirrorV0012[idx].LevelDashStyleB == levelDashStyleB && cacheAlightenMirrorV0012[idx].LevelDashStyleC == levelDashStyleC && cacheAlightenMirrorV0012[idx].LevelDashStyleE == levelDashStyleE && cacheAlightenMirrorV0012[idx].LevelWidthGL == levelWidthGL && cacheAlightenMirrorV0012[idx].LevelDashStyleTSU1 == levelDashStyleTSU1 && cacheAlightenMirrorV0012[idx].LevelDashStyleTSU2 == levelDashStyleTSU2 && cacheAlightenMirrorV0012[idx].LevelDashStyleTSD1 == levelDashStyleTSD1 && cacheAlightenMirrorV0012[idx].LevelDashStyleTSD2 == levelDashStyleTSD2 && cacheAlightenMirrorV0012[idx].LabelFontSize == labelFontSize && cacheAlightenMirrorV0012[idx].LabelOffsetTicks == labelOffsetTicks && cacheAlightenMirrorV0012[idx].EnableHTFVPFilter == enableHTFVPFilter && cacheAlightenMirrorV0012[idx].ShowHTFVPLevels == showHTFVPLevels && cacheAlightenMirrorV0012[idx].HTFVPBarType == hTFVPBarType && cacheAlightenMirrorV0012[idx].HTFVPBarValue == hTFVPBarValue && cacheAlightenMirrorV0012[idx].HTFVPValueAreaPercentage == hTFVPValueAreaPercentage && cacheAlightenMirrorV0012[idx].HTFVPVAHColor == hTFVPVAHColor && cacheAlightenMirrorV0012[idx].HTFVPVALColor == hTFVPVALColor && cacheAlightenMirrorV0012[idx].HTFVPBarsToProcess == hTFVPBarsToProcess && cacheAlightenMirrorV0012[idx].HTFVPPOCColor == hTFVPPOCColor && cacheAlightenMirrorV0012[idx].HTFVPVAHWidth == hTFVPVAHWidth && cacheAlightenMirrorV0012[idx].HTFVPVAHStyle == hTFVPVAHStyle && cacheAlightenMirrorV0012[idx].HTFVPVALWidth == hTFVPVALWidth && cacheAlightenMirrorV0012[idx].HTFVPVALStyle == hTFVPVALStyle && cacheAlightenMirrorV0012[idx].HTFVPPOCWidth == hTFVPPOCWidth && cacheAlightenMirrorV0012[idx].HTFVPPOCStyle == hTFVPPOCStyle && cacheAlightenMirrorV0012[idx].EqualsInput(input))
						return cacheAlightenMirrorV0012[idx];
			return CacheIndicator<AlightenMirrorV0012>(new AlightenMirrorV0012(){ EnablePatternA = enablePatternA, EnablePatternB = enablePatternB, EnablePatternC = enablePatternC, EnablePatternE = enablePatternE, SrcBarsToProcess = srcBarsToProcess, MirrorLookbackBars = mirrorLookbackBars, EnableInvalidatedCleanup = enableInvalidatedCleanup, EnableTF1_Daily = enableTF1_Daily, EnableTF2_240m = enableTF2_240m, EnableTF3_60m = enableTF3_60m, EnableTF4_30m = enableTF4_30m, EnableTF5_15m = enableTF5_15m, EnableTF6_10m = enableTF6_10m, EnableTF7_5m = enableTF7_5m, EnableGLTF1_Daily = enableGLTF1_Daily, EnableGLTF2_240m = enableGLTF2_240m, EnableGLTF3_60m = enableGLTF3_60m, EnableGLTF4_30m = enableGLTF4_30m, EnableGLTF5_15m = enableGLTF5_15m, EnableGLTF6_10m = enableGLTF6_10m, EnableGLTF7_5m = enableGLTF7_5m, ColorTF1 = colorTF1, ColorTF2 = colorTF2, ColorTF3 = colorTF3, ColorTF4 = colorTF4, ColorTF5 = colorTF5, ColorTF6 = colorTF6, ColorTF7 = colorTF7, LevelWidth = levelWidth, LevelDashStyleA = levelDashStyleA, LevelDashStyleB = levelDashStyleB, LevelDashStyleC = levelDashStyleC, LevelDashStyleE = levelDashStyleE, LevelWidthGL = levelWidthGL, LevelDashStyleTSU1 = levelDashStyleTSU1, LevelDashStyleTSU2 = levelDashStyleTSU2, LevelDashStyleTSD1 = levelDashStyleTSD1, LevelDashStyleTSD2 = levelDashStyleTSD2, LabelFontSize = labelFontSize, LabelOffsetTicks = labelOffsetTicks, EnableHTFVPFilter = enableHTFVPFilter, ShowHTFVPLevels = showHTFVPLevels, HTFVPBarType = hTFVPBarType, HTFVPBarValue = hTFVPBarValue, HTFVPValueAreaPercentage = hTFVPValueAreaPercentage, HTFVPVAHColor = hTFVPVAHColor, HTFVPVALColor = hTFVPVALColor, HTFVPBarsToProcess = hTFVPBarsToProcess, HTFVPPOCColor = hTFVPPOCColor, HTFVPVAHWidth = hTFVPVAHWidth, HTFVPVAHStyle = hTFVPVAHStyle, HTFVPVALWidth = hTFVPVALWidth, HTFVPVALStyle = hTFVPVALStyle, HTFVPPOCWidth = hTFVPPOCWidth, HTFVPPOCStyle = hTFVPPOCStyle }, input, ref cacheAlightenMirrorV0012);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenMirrorV0012 AlightenMirrorV0012(bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool enableTF2_240m, bool enableTF3_60m, bool enableTF4_30m, bool enableTF5_15m, bool enableTF6_10m, bool enableTF7_5m, bool enableGLTF1_Daily, bool enableGLTF2_240m, bool enableGLTF3_60m, bool enableGLTF4_30m, bool enableGLTF5_15m, bool enableGLTF6_10m, bool enableGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, bool enableHTFVPFilter, bool showHTFVPLevels, BarsPeriodType hTFVPBarType, int hTFVPBarValue, double hTFVPValueAreaPercentage, Brush hTFVPVAHColor, Brush hTFVPVALColor, int hTFVPBarsToProcess, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle)
		{
			return indicator.AlightenMirrorV0012(Input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, enableTF2_240m, enableTF3_60m, enableTF4_30m, enableTF5_15m, enableTF6_10m, enableTF7_5m, enableGLTF1_Daily, enableGLTF2_240m, enableGLTF3_60m, enableGLTF4_30m, enableGLTF5_15m, enableGLTF6_10m, enableGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, enableHTFVPFilter, showHTFVPLevels, hTFVPBarType, hTFVPBarValue, hTFVPValueAreaPercentage, hTFVPVAHColor, hTFVPVALColor, hTFVPBarsToProcess, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle);
		}

		public Indicators.AlightenMirrorV0012 AlightenMirrorV0012(ISeries<double> input , bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool enableTF2_240m, bool enableTF3_60m, bool enableTF4_30m, bool enableTF5_15m, bool enableTF6_10m, bool enableTF7_5m, bool enableGLTF1_Daily, bool enableGLTF2_240m, bool enableGLTF3_60m, bool enableGLTF4_30m, bool enableGLTF5_15m, bool enableGLTF6_10m, bool enableGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, bool enableHTFVPFilter, bool showHTFVPLevels, BarsPeriodType hTFVPBarType, int hTFVPBarValue, double hTFVPValueAreaPercentage, Brush hTFVPVAHColor, Brush hTFVPVALColor, int hTFVPBarsToProcess, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle)
		{
			return indicator.AlightenMirrorV0012(input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, enableTF2_240m, enableTF3_60m, enableTF4_30m, enableTF5_15m, enableTF6_10m, enableTF7_5m, enableGLTF1_Daily, enableGLTF2_240m, enableGLTF3_60m, enableGLTF4_30m, enableGLTF5_15m, enableGLTF6_10m, enableGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, enableHTFVPFilter, showHTFVPLevels, hTFVPBarType, hTFVPBarValue, hTFVPValueAreaPercentage, hTFVPVAHColor, hTFVPVALColor, hTFVPBarsToProcess, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenMirrorV0012 AlightenMirrorV0012(bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool enableTF2_240m, bool enableTF3_60m, bool enableTF4_30m, bool enableTF5_15m, bool enableTF6_10m, bool enableTF7_5m, bool enableGLTF1_Daily, bool enableGLTF2_240m, bool enableGLTF3_60m, bool enableGLTF4_30m, bool enableGLTF5_15m, bool enableGLTF6_10m, bool enableGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, bool enableHTFVPFilter, bool showHTFVPLevels, BarsPeriodType hTFVPBarType, int hTFVPBarValue, double hTFVPValueAreaPercentage, Brush hTFVPVAHColor, Brush hTFVPVALColor, int hTFVPBarsToProcess, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle)
		{
			return indicator.AlightenMirrorV0012(Input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, enableTF2_240m, enableTF3_60m, enableTF4_30m, enableTF5_15m, enableTF6_10m, enableTF7_5m, enableGLTF1_Daily, enableGLTF2_240m, enableGLTF3_60m, enableGLTF4_30m, enableGLTF5_15m, enableGLTF6_10m, enableGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, enableHTFVPFilter, showHTFVPLevels, hTFVPBarType, hTFVPBarValue, hTFVPValueAreaPercentage, hTFVPVAHColor, hTFVPVALColor, hTFVPBarsToProcess, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle);
		}

		public Indicators.AlightenMirrorV0012 AlightenMirrorV0012(ISeries<double> input , bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool enableTF2_240m, bool enableTF3_60m, bool enableTF4_30m, bool enableTF5_15m, bool enableTF6_10m, bool enableTF7_5m, bool enableGLTF1_Daily, bool enableGLTF2_240m, bool enableGLTF3_60m, bool enableGLTF4_30m, bool enableGLTF5_15m, bool enableGLTF6_10m, bool enableGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, bool enableHTFVPFilter, bool showHTFVPLevels, BarsPeriodType hTFVPBarType, int hTFVPBarValue, double hTFVPValueAreaPercentage, Brush hTFVPVAHColor, Brush hTFVPVALColor, int hTFVPBarsToProcess, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle)
		{
			return indicator.AlightenMirrorV0012(input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, enableTF2_240m, enableTF3_60m, enableTF4_30m, enableTF5_15m, enableTF6_10m, enableTF7_5m, enableGLTF1_Daily, enableGLTF2_240m, enableGLTF3_60m, enableGLTF4_30m, enableGLTF5_15m, enableGLTF6_10m, enableGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, enableHTFVPFilter, showHTFVPLevels, hTFVPBarType, hTFVPBarValue, hTFVPValueAreaPercentage, hTFVPVAHColor, hTFVPVALColor, hTFVPBarsToProcess, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle);
		}
	}
}

#endregion

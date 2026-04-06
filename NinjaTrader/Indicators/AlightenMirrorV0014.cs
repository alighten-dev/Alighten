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

public enum HTFVPBiasTimeframe
{
    None,
    TF1_Daily,
    TF2_240m,
    TF3_60m,
    TF4_30m,
    TF5_15m,
    TF6_10m,
    TF7_5m
}

namespace NinjaTrader.NinjaScript.Indicators
{
    

    public class AlightenMirrorV0014 : Indicator
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
		
		// Toolbar & UI
		private NinjaTrader.Gui.Chart.Chart chartWindow;
        private Button settingsButton;
        private Window settingsWindow;
		
		private class ActiveGLState
		{
		    public TrackedLevel Level;
		    public double LastPrice;
		    public DateTime LastAnchorTime;
		}
		
		private Dictionary<string, ActiveGLState>[] _activeGL = new Dictionary<string, ActiveGLState>[NUM_TF];
		
        // HTF VP passthrough
        private AlightenHTFVPV0004[] _srcHTFVP = new AlightenHTFVPV0004[NUM_TF];
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
        [Display(Name="1. Enable Daily", Order=1, GroupName="2. Mirror Timeframes")]
        public bool EnableTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Daily", Order=2, GroupName="2. Mirror Timeframes")]
        public bool ShowTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Enable 240 Min", Order=3, GroupName="2. Mirror Timeframes")]
        public bool EnableTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show 240 Min", Order=4, GroupName="2. Mirror Timeframes")]
        public bool ShowTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Enable 60 Min", Order=5, GroupName="2. Mirror Timeframes")]
        public bool EnableTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show 60 Min", Order=6, GroupName="2. Mirror Timeframes")]
        public bool ShowTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Enable 30 Min", Order=7, GroupName="2. Mirror Timeframes")]
        public bool EnableTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show 30 Min", Order=8, GroupName="2. Mirror Timeframes")]
        public bool ShowTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Enable 15 Min", Order=9, GroupName="2. Mirror Timeframes")]
        public bool EnableTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show 15 Min", Order=10, GroupName="2. Mirror Timeframes")]
        public bool ShowTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Enable 10 Min", Order=11, GroupName="2. Mirror Timeframes")]
        public bool EnableTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show 10 Min", Order=12, GroupName="2. Mirror Timeframes")]
        public bool ShowTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Enable 5 Min", Order=13, GroupName="2. Mirror Timeframes")]
        public bool EnableTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show 5 Min", Order=14, GroupName="2. Mirror Timeframes")]
        public bool ShowTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Enable Daily (GL)", Order=1, GroupName="3. GainLoss Timeframes")]
        public bool EnableGLTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Daily (GL)", Order=2, GroupName="3. GainLoss Timeframes")]
        public bool ShowGLTF1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Enable 240 Min (GL)", Order=3, GroupName="3. GainLoss Timeframes")]
        public bool EnableGLTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show 240 Min (GL)", Order=4, GroupName="3. GainLoss Timeframes")]
        public bool ShowGLTF2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Enable 60 Min (GL)", Order=5, GroupName="3. GainLoss Timeframes")]
        public bool EnableGLTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show 60 Min (GL)", Order=6, GroupName="3. GainLoss Timeframes")]
        public bool ShowGLTF3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Enable 30 Min (GL)", Order=7, GroupName="3. GainLoss Timeframes")]
        public bool EnableGLTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show 30 Min (GL)", Order=8, GroupName="3. GainLoss Timeframes")]
        public bool ShowGLTF4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Enable 15 Min (GL)", Order=9, GroupName="3. GainLoss Timeframes")]
        public bool EnableGLTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show 15 Min (GL)", Order=10, GroupName="3. GainLoss Timeframes")]
        public bool ShowGLTF5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Enable 10 Min (GL)", Order=11, GroupName="3. GainLoss Timeframes")]
        public bool EnableGLTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show 10 Min (GL)", Order=12, GroupName="3. GainLoss Timeframes")]
        public bool ShowGLTF6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Enable 5 Min (GL)", Order=13, GroupName="3. GainLoss Timeframes")]
        public bool EnableGLTF7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show 5 Min (GL)", Order=14, GroupName="3. GainLoss Timeframes")]
        public bool ShowGLTF7_5m { get; set; }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF1 Color", Order=1, GroupName="5. Mirror Colors")]
        public Brush ColorTF1 { get; set; }
        [Browsable(false)]
        public string ColorTF1Serialize { get { return Serialize.BrushToString(ColorTF1); } set { ColorTF1 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF2 Color", Order=2, GroupName="5. Mirror Colors")]
        public Brush ColorTF2 { get; set; }
        [Browsable(false)]
        public string ColorTF2Serialize { get { return Serialize.BrushToString(ColorTF2); } set { ColorTF2 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF3 Color", Order=3, GroupName="5. Mirror Colors")]
        public Brush ColorTF3 { get; set; }
        [Browsable(false)]
        public string ColorTF3Serialize { get { return Serialize.BrushToString(ColorTF3); } set { ColorTF3 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF4 Color", Order=4, GroupName="5. Mirror Colors")]
        public Brush ColorTF4 { get; set; }
        [Browsable(false)]
        public string ColorTF4Serialize { get { return Serialize.BrushToString(ColorTF4); } set { ColorTF4 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF5 Color", Order=5, GroupName="5. Mirror Colors")]
        public Brush ColorTF5 { get; set; }
        [Browsable(false)]
        public string ColorTF5Serialize { get { return Serialize.BrushToString(ColorTF5); } set { ColorTF5 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF6 Color", Order=6, GroupName="5. Mirror Colors")]
        public Brush ColorTF6 { get; set; }
        [Browsable(false)]
        public string ColorTF6Serialize { get { return Serialize.BrushToString(ColorTF6); } set { ColorTF6 = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="TF7 Color", Order=7, GroupName="5. Mirror Colors")]
        public Brush ColorTF7 { get; set; }
        [Browsable(false)]
        public string ColorTF7Serialize { get { return Serialize.BrushToString(ColorTF7); } set { ColorTF7 = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="Level Width", Order=1, GroupName="6. Mirror Visuals")]
        public int LevelWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern A Style", Order=2, GroupName="6. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleA { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern B Style", Order=3, GroupName="6. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleB { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern C Style", Order=4, GroupName="6. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleC { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Pattern E Style", Order=5, GroupName="6. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleE { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="GainLoss Level Width", Order=6, GroupName="6. Mirror Visuals")]
        public int LevelWidthGL { get; set; }

        [NinjaScriptProperty]
        [Display(Name="GainLoss TSU1 Style", Order=7, GroupName="6. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleTSU1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="GainLoss TSU2 Style", Order=8, GroupName="6. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleTSU2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="GainLoss TSD1 Style", Order=9, GroupName="6. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleTSD1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name="GainLoss TSD2 Style", Order=10, GroupName="6. Mirror Visuals")]
        public DashStyleHelper LevelDashStyleTSD2 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name="Label Font Size", Order=11, GroupName="6. Mirror Visuals")]
        public int LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Label Offset Ticks", Order=12, GroupName="6. Mirror Visuals")]
        public int LabelOffsetTicks { get; set; }
		
        [NinjaScriptProperty]
        [Display(Name="Bias Filter Timeframe", Order=0, GroupName="4. HTF VP Timeframes")]
        public HTFVPBiasTimeframe BiasTimeframe { get; set; }

                [NinjaScriptProperty]
        [Display(Name="1. Enable Daily VP", Order=1, GroupName="4. HTF VP Timeframes")]
        public bool EnableHTFVP1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Daily VP", Order=2, GroupName="4. HTF VP Timeframes")]
        public bool ShowHTFVP1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="1. Show Dev Daily VP", Order=3, GroupName="4. HTF VP Timeframes")]
        public bool ShowDevVP1_Daily { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Enable 240 Min VP", Order=4, GroupName="4. HTF VP Timeframes")]
        public bool EnableHTFVP2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show 240 Min VP", Order=5, GroupName="4. HTF VP Timeframes")]
        public bool ShowHTFVP2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="2. Show Dev 240 Min VP", Order=6, GroupName="4. HTF VP Timeframes")]
        public bool ShowDevVP2_240m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Enable 60 Min VP", Order=7, GroupName="4. HTF VP Timeframes")]
        public bool EnableHTFVP3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show 60 Min VP", Order=8, GroupName="4. HTF VP Timeframes")]
        public bool ShowHTFVP3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="3. Show Dev 60 Min VP", Order=9, GroupName="4. HTF VP Timeframes")]
        public bool ShowDevVP3_60m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Enable 30 Min VP", Order=10, GroupName="4. HTF VP Timeframes")]
        public bool EnableHTFVP4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show 30 Min VP", Order=11, GroupName="4. HTF VP Timeframes")]
        public bool ShowHTFVP4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="4. Show Dev 30 Min VP", Order=12, GroupName="4. HTF VP Timeframes")]
        public bool ShowDevVP4_30m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Enable 15 Min VP", Order=13, GroupName="4. HTF VP Timeframes")]
        public bool EnableHTFVP5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show 15 Min VP", Order=14, GroupName="4. HTF VP Timeframes")]
        public bool ShowHTFVP5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="5. Show Dev 15 Min VP", Order=15, GroupName="4. HTF VP Timeframes")]
        public bool ShowDevVP5_15m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Enable 10 Min VP", Order=16, GroupName="4. HTF VP Timeframes")]
        public bool EnableHTFVP6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show 10 Min VP", Order=17, GroupName="4. HTF VP Timeframes")]
        public bool ShowHTFVP6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="6. Show Dev 10 Min VP", Order=18, GroupName="4. HTF VP Timeframes")]
        public bool ShowDevVP6_10m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Enable 5 Min VP", Order=19, GroupName="4. HTF VP Timeframes")]
        public bool EnableHTFVP7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show 5 Min VP", Order=20, GroupName="4. HTF VP Timeframes")]
        public bool ShowHTFVP7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="7. Show Dev 5 Min VP", Order=21, GroupName="4. HTF VP Timeframes")]
        public bool ShowDevVP7_5m { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Enable HTF VP Filter (Bias Gating)", Order=22, GroupName="4. HTF VP Timeframes")]
        public bool EnableHTFVPFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="HTF VP Value Area %", Order=1, GroupName="7. HTF VP Settings")]
        public double HTFVPValueAreaPercentage { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name="HTF VP Bars To Process", Order=2, GroupName="7. HTF VP Settings")]
        public int HTFVPBarsToProcess { get; set; }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="HTF VP VAH Color", Order=3, GroupName="7. HTF VP Settings")]
        public Brush HTFVPVAHColor { get; set; }
        [Browsable(false)]
        public string HTFVPVAHColorSerialize { get { return Serialize.BrushToString(HTFVPVAHColor); } set { HTFVPVAHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="HTF VP VAL Color", Order=4, GroupName="7. HTF VP Settings")]
        public Brush HTFVPVALColor { get; set; }
        [Browsable(false)]
        public string HTFVPVALColorSerialize { get { return Serialize.BrushToString(HTFVPVALColor); } set { HTFVPVALColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name="HTF VP POC Color", Order=5, GroupName="7. HTF VP Settings")]
        public Brush HTFVPPOCColor { get; set; }
        [Browsable(false)]
        public string HTFVPPOCColorSerialize { get { return Serialize.BrushToString(HTFVPPOCColor); } set { HTFVPPOCColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="HTF VP VAH Width", Order=11, GroupName="7. HTF VP Settings")]
        public int HTFVPVAHWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP VAH Style", Order=12, GroupName="7. HTF VP Settings")]
        public DashStyleHelper HTFVPVAHStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="HTF VP VAL Width", Order=13, GroupName="7. HTF VP Settings")]
        public int HTFVPVALWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP VAL Style", Order=14, GroupName="7. HTF VP Settings")]
        public DashStyleHelper HTFVPVALStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="HTF VP POC Width", Order=15, GroupName="7. HTF VP Settings")]
        public int HTFVPPOCWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP POC Style", Order=16, GroupName="7. HTF VP Settings")]
        public DashStyleHelper HTFVPPOCStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="HTF VP Dev VAH Width", Order=17, GroupName="7. HTF VP Settings")]
        public int HTFVPDevVAHWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP Dev VAH Style", Order=18, GroupName="7. HTF VP Settings")]
        public DashStyleHelper HTFVPDevVAHStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="HTF VP Dev VAL Width", Order=19, GroupName="7. HTF VP Settings")]
        public int HTFVPDevVALWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP Dev VAL Style", Order=20, GroupName="7. HTF VP Settings")]
        public DashStyleHelper HTFVPDevVALStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name="HTF VP Dev POC Width", Order=21, GroupName="7. HTF VP Settings")]
        public int HTFVPDevPOCWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name="HTF VP Dev POC Style", Order=22, GroupName="7. HTF VP Settings")]
        public DashStyleHelper HTFVPDevPOCStyle { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AlightenMirrorV0014";
                Description = "Multi-Timeframe Mirror for Patterns A, B, C, and E. v14: Multi-Timeframe HTF VP controls and labeled profiles.";
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
                ShowTF1_Daily = true;
                EnableTF2_240m = true;
                ShowTF2_240m = true;
                EnableTF3_60m = true;
                ShowTF3_60m = true;
                EnableTF4_30m = true;
                ShowTF4_30m = true;
                EnableTF5_15m = true;
                ShowTF5_15m = true;
                EnableTF6_10m = true;
                ShowTF6_10m = true;
                EnableTF7_5m = true;
                ShowTF7_5m = true;

				EnableGLTF1_Daily = false;
				ShowGLTF1_Daily = false;
				EnableGLTF2_240m  = false;
				ShowGLTF2_240m = false;
				EnableGLTF3_60m   = true; 
				ShowGLTF3_60m = true;
				EnableGLTF4_30m   = false;
				ShowGLTF4_30m = false;
				EnableGLTF5_15m   = false;
				ShowGLTF5_15m = false;
				EnableGLTF6_10m   = false;
				ShowGLTF6_10m = false;
				EnableGLTF7_5m    = false;
				ShowGLTF7_5m = false;

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
				
				BiasTimeframe = HTFVPBiasTimeframe.TF3_60m;
				EnableHTFVP1_Daily = true;
				ShowHTFVP1_Daily = true;
				ShowDevVP1_Daily = false;
				EnableHTFVP2_240m = true;
				ShowHTFVP2_240m = true;
				ShowDevVP2_240m = false;
				EnableHTFVP3_60m = true;
				ShowHTFVP3_60m = true;
				ShowDevVP3_60m = false;
				EnableHTFVP4_30m = false;
				ShowHTFVP4_30m = false;
				ShowDevVP4_30m = false;
				EnableHTFVP5_15m = false;
				ShowHTFVP5_15m = false;
				ShowDevVP5_15m = false;
				EnableHTFVP6_10m = false;
				ShowHTFVP6_10m = false;
				ShowDevVP6_10m = false;
				EnableHTFVP7_5m = false;
				ShowHTFVP7_5m = false;
				ShowDevVP7_5m = false;

				EnableHTFVPFilter = true;
                HTFVPValueAreaPercentage = 70;

                HTFVPVAHColor = Brushes.MediumSeaGreen;
                HTFVPVALColor = Brushes.HotPink;
                HTFVPPOCColor = Brushes.DeepSkyBlue;
                HTFVPBarsToProcess = 24;

                HTFVPVAHWidth = 2;
                HTFVPVALWidth = 2;
                HTFVPPOCWidth = 2;

                HTFVPVAHStyle = DashStyleHelper.Dash;
                HTFVPVALStyle = DashStyleHelper.Dash;
                HTFVPPOCStyle = DashStyleHelper.Dash;

                HTFVPDevVAHWidth = 1;
                HTFVPDevVALWidth = 1;
                HTFVPDevPOCWidth = 1;
                HTFVPDevVAHStyle = DashStyleHelper.Dot;
                HTFVPDevVALStyle = DashStyleHelper.Dot;
                HTFVPDevPOCStyle = DashStyleHelper.Dot;
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
                    
                    int bipIdx = t + 2; 
                    
                    if (EnablePatternA)
                    {
                        _srcA[t] = AlightenMirrorPtAV0003(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternB)
                    {
                        _srcB[t] = AlightenMirrorPtBV0003(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, true, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternC)
                    {
                        _srcC[t] = AlightenMirrorPtCV0004(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
						);
                    }
                    if (EnablePatternE)
                    {
                        _srcE[t] = AlightenMirrorPtEV0005(
						    Closes[bipIdx],
						    SrcBarsToProcess, true, true, false, 18, 5, Brushes.Lime, Brushes.Red, 2, DashStyleHelper.Solid, false, false, false, Brushes.DimGray, 1, DashStyleHelper.Solid
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
                        Closes[bipIdx], SrcBarsToProcess, false, Brushes.Yellow, 2, DashStyleHelper.Solid, false, Brushes.Magenta, 3, DashStyleHelper.Solid, DashStyleHelper.Dash, Brushes.Cyan, 3, DashStyleHelper.Solid, DashStyleHelper.Dash, false, 11, 5
                    );
                }
				
				// Initialize HTF VP passthroughs
				for (int t = 0; t < NUM_TF; t++)
				{
					if (!IsHtfVpEnabled(t)) continue;
					BarsPeriodType bpType = BarsPeriodType.Minute;
					int bpVal = _tfMinutes[t];
					if (t == 0) { bpType = BarsPeriodType.Day; bpVal = 1; }
					
					_srcHTFVP[t] = AlightenHTFVPV0004(
						Input, HTFVPBarsToProcess, bpType, bpVal, HTFVPValueAreaPercentage, HTFVPVAHWidth, HTFVPVAHStyle, HTFVPVALWidth, HTFVPVALStyle, HTFVPPOCWidth, HTFVPPOCStyle
					);
				}

				if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => { CreateToolbarButton(); });
                }
            }
			else if (State == State.Terminated)
            {
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => { TryRemoveToolbarButton(); });
                }
            }
        }
        
        protected override void OnBarUpdate()
		{
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
			
                    for (int t = 0; t < NUM_TF; t++)
                    {
                        if (_srcHTFVP[t] != null)
                        {
                            _srcHTFVP[t].Update();
                        }
                    }
                    
                    // Display Completed Levels
                    DrawHTFVPPassthrough();
                    
                    // Display Developing Levels
                    DrawDevelopingHTFVPPassthrough();

                    // Apply filter if enabled (using bias timeframe)
                    if (EnableHTFVPFilter && BiasTimeframe != HTFVPBiasTimeframe.None)
                    {
                        int tfIdx = ((int)BiasTimeframe) - 1;
                        if (tfIdx >= 0 && tfIdx < NUM_TF && _srcHTFVP[tfIdx] != null && _srcHTFVP[tfIdx].Profiles != null && _srcHTFVP[tfIdx].Profiles.Count > _lastProcessedProfileCount)
                        {
                            ApplyHTFVPBiasClipToTrackedSignals();
                            _lastProcessedProfileCount = _srcHTFVP[tfIdx].Profiles.Count;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Print("[MirrorV0013] CRASH in BIP 0: " + ex.ToString());
                }
			}

            if (EnableInvalidatedCleanup)
			{
			    for (int t = 0; t < NUM_TF; t++)
			    {
			        if (!IsTfEnabled(t)) continue;
			        int bipIdx = t + 2;
			        if (CurrentBars[bipIdx] < 1) continue;
			        DateTime currentHtfBarTime = Times[bipIdx][0];

			        if (_lastSeenHtfBarTime[t] == Core.Globals.MinDate)
			        {
			            _lastSeenHtfBarTime[t] = currentHtfBarTime;
			        }
			        else if (_lastSeenHtfBarTime[t] != currentHtfBarTime)
			        {
			            CleanupInvalidatedLevelsForTimeframe(t, bipIdx);
			            _lastSeenHtfBarTime[t] = currentHtfBarTime;
			        }
			    }
			}
		
		    SyncLevels();
		}
		
		private void DrawHTFVPPassthrough(bool force = false)
		{
		    
		    if (!force && BarsInProgress != 0) return;
		    
		    for (int t = 0; t < NUM_TF; t++)
		    {
		        if (!IsHtfVpShown(t) || _srcHTFVP[t] == null || _srcHTFVP[t].Profiles == null || _srcHTFVP[t].Profiles.Count == 0) continue;
		            
		        var activeProfile = _srcHTFVP[t].GetProfileAt(Times[0][0]);
		        if (activeProfile == null) continue;
		            
		        DateTime endTime = Times[0][0];
		        if (activeProfile.EndTime != Core.Globals.MinDate && endTime > activeProfile.EndTime)
		            endTime = activeProfile.EndTime;
		                    
		        if (double.IsNaN(activeProfile.VAH) || double.IsNaN(activeProfile.VAL) || double.IsNaN(activeProfile.POC) || activeProfile.POC <= 0)
		            continue;
		        
		        if (activeProfile.StartTime >= endTime) continue;
		    
		        string tagVAH = "MR_HTFVP_VAH_TF" + t + "_" + activeProfile.StartTime.Ticks;
		        string tagVAL = "MR_HTFVP_VAL_TF" + t + "_" + activeProfile.StartTime.Ticks;
		        string tagPOC = "MR_HTFVP_POC_TF" + t + "_" + activeProfile.StartTime.Ticks;
		        
		        Draw.Line(this, tagVAH, false, activeProfile.StartTime, activeProfile.VAH, endTime, activeProfile.VAH, HTFVPVAHColor, HTFVPVAHStyle, HTFVPVAHWidth);
		        Draw.Line(this, tagVAL, false, activeProfile.StartTime, activeProfile.VAL, endTime, activeProfile.VAL, HTFVPVALColor, HTFVPVALStyle, HTFVPVALWidth);
		        Draw.Line(this, tagPOC, false, activeProfile.StartTime, activeProfile.POC, endTime, activeProfile.POC, HTFVPPOCColor, HTFVPPOCStyle, HTFVPPOCWidth);
		        
		        Draw.Text(this, tagVAH + "_lbl", false, _tfLabels[t] + " VAH", endTime, activeProfile.VAH + (LabelOffsetTicks * TickSize), 0, HTFVPVAHColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		        Draw.Text(this, tagVAL + "_lbl", false, _tfLabels[t] + " VAL", endTime, activeProfile.VAL + (LabelOffsetTicks * TickSize), 0, HTFVPVALColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		        Draw.Text(this, tagPOC + "_lbl", false, _tfLabels[t] + " POC", endTime, activeProfile.POC + (LabelOffsetTicks * TickSize), 0, HTFVPPOCColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		    }
		}

        private void DrawDevelopingHTFVPPassthrough(bool force = false)
        {
            
            if (!force && BarsInProgress != 0) return;

            for (int t = 0; t < NUM_TF; t++)
            {
                if (!IsDevVpShown(t) || _srcHTFVP[t] == null) continue;

                var devProfile = _srcHTFVP[t].GetDevelopingProfile();
                if (devProfile == null) continue;

                if (double.IsNaN(devProfile.VAH) || double.IsNaN(devProfile.VAL) || double.IsNaN(devProfile.POC) || devProfile.POC <= 0) continue;

                DateTime startTime = devProfile.StartTime;
                DateTime endTime = Times[0][0];
                if (startTime >= endTime) continue;

                string tagVAH = "MR_DEV_HTFVP_VAH_TF" + t;
                string tagVAL = "MR_DEV_HTFVP_VAL_TF" + t;
                string tagPOC = "MR_DEV_HTFVP_POC_TF" + t;

                Draw.Line(this, tagVAH, false, startTime, devProfile.VAH, endTime, devProfile.VAH, HTFVPVAHColor, HTFVPDevVAHStyle, HTFVPDevVAHWidth);
                Draw.Line(this, tagVAL, false, startTime, devProfile.VAL, endTime, devProfile.VAL, HTFVPVALColor, HTFVPDevVALStyle, HTFVPDevVALWidth);
                Draw.Line(this, tagPOC, false, startTime, devProfile.POC, endTime, devProfile.POC, HTFVPPOCColor, HTFVPDevPOCStyle, HTFVPDevPOCWidth);
                
                Draw.Text(this, tagVAH + "_lbl", false, _tfLabels[t] + " Dev VAH", endTime, devProfile.VAH + (LabelOffsetTicks * TickSize), 0, HTFVPVAHColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                Draw.Text(this, tagVAL + "_lbl", false, _tfLabels[t] + " Dev VAL", endTime, devProfile.VAL + (LabelOffsetTicks * TickSize), 0, HTFVPVALColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                Draw.Text(this, tagPOC + "_lbl", false, _tfLabels[t] + " Dev POC", endTime, devProfile.POC + (LabelOffsetTicks * TickSize), 0, HTFVPPOCColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }
        }
		
		private bool PassesHTFVPBiasGate(DateTime candidateTime, bool isLong)
		{
		    if (!EnableHTFVPFilter || BiasTimeframe == HTFVPBiasTimeframe.None)
		        return true;
		        
            int tfIdx = ((int)BiasTimeframe) - 1;
            if (tfIdx < 0 || tfIdx >= NUM_TF || _srcHTFVP[tfIdx] == null)
                return true;
		
            var profile = _srcHTFVP[tfIdx].GetProfileAt(candidateTime);
            if (profile == null)
		        return true;   
		
		    if (profile.Bias > 0) return isLong;
		    if (profile.Bias < 0) return !isLong;
		    return true;       
		}
		
		private bool ViolatesHTFVPBias(TrackedLevel tl)
		{
		    if (!EnableHTFVPFilter || BiasTimeframe == HTFVPBiasTimeframe.None)
		        return false;
		        
            int tfIdx = ((int)BiasTimeframe) - 1;
            if (tfIdx < 0 || tfIdx >= NUM_TF || _srcHTFVP[tfIdx] == null)
                return false;
		
            var profile = _srcHTFVP[tfIdx].GetProfileAt(tl.HtfStartTime);
            if (profile == null)
		        return false;
		        
		    if (profile.Bias > 0 && !tl.IsLong) return true;
		    if (profile.Bias < 0 && tl.IsLong) return true;
		    return false;
		}		
		
		private void ApplyHTFVPBiasClipToTrackedSignals()
		{
		    if (!EnableHTFVPFilter || BiasTimeframe == HTFVPBiasTimeframe.None)
		        return;
		        
            int tfIdx = ((int)BiasTimeframe) - 1;
            if (tfIdx < 0 || tfIdx >= NUM_TF || _srcHTFVP[tfIdx] == null || _srcHTFVP[tfIdx].Profiles == null || _srcHTFVP[tfIdx].Profiles.Count == 0)
                return;
		        
            var profile = _srcHTFVP[tfIdx].Profiles.LastOrDefault();
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
		
        private void SyncLevels()
        {
            for (int t = 0; t < NUM_TF; t++)
            {
                if (!IsTfEnabled(t)) continue;
                int bipIdx = t + 2; 
                int htfBars = CurrentBars[bipIdx];
                if (htfBars < 2) continue;

				if (EnablePatternA && _srcA[t] != null)
				{
				    int lookback = Math.Min(MirrorLookbackBars, htfBars);
				    for (int i = lookback; i >= 0; i--)
				    {
				        if (_srcA[t].PatternALongLevel.Count > i) SyncSingleSignal(t, bipIdx, i, _srcA[t].PatternALongLevel[i], true, "A", _srcA[t].PatternALongStartTimestamp.Count > i ? _srcA[t].PatternALongStartTimestamp[i] : 0);
				        if (_srcA[t].PatternAShortLevel.Count > i) SyncSingleSignal(t, bipIdx, i, _srcA[t].PatternAShortLevel[i], false, "A", _srcA[t].PatternAShortStartTimestamp.Count > i ? _srcA[t].PatternAShortStartTimestamp[i] : 0);
				    }
				}
				if (EnablePatternB && _srcB[t] != null)
				{
				    int lookback = Math.Min(MirrorLookbackBars, htfBars);
				    for (int i = lookback; i >= 0; i--)
				    {
				        if (_srcB[t].PatternBLongLevel.Count > i) SyncSingleSignal(t, bipIdx, i, _srcB[t].PatternBLongLevel[i], true, "B", _srcB[t].PatternBLongStartTimestamp.Count > i ? _srcB[t].PatternBLongStartTimestamp[i] : 0);
				        if (_srcB[t].PatternBShortLevel.Count > i) SyncSingleSignal(t, bipIdx, i, _srcB[t].PatternBShortLevel[i], false, "B", _srcB[t].PatternBShortStartTimestamp.Count > i ? _srcB[t].PatternBShortStartTimestamp[i] : 0);
				    }
				}
				if (EnablePatternC && _srcC[t] != null)
				{
				    int lookback = Math.Min(MirrorLookbackBars, htfBars);
				    for (int i = lookback; i >= 0; i--)
				    {
				        if (_srcC[t].PatternCLongLevel.Count > i) SyncSingleSignal(t, bipIdx, i, _srcC[t].PatternCLongLevel[i], true, "C", _srcC[t].PatternCLongStartTimestamp.Count > i ? _srcC[t].PatternCLongStartTimestamp[i] : 0);
				        if (_srcC[t].PatternCShortLevel.Count > i) SyncSingleSignal(t, bipIdx, i, _srcC[t].PatternCShortLevel[i], false, "C", _srcC[t].PatternCShortStartTimestamp.Count > i ? _srcC[t].PatternCShortStartTimestamp[i] : 0);
				    }
				}
				if (EnablePatternE && _srcE[t] != null)
				{
				    int lookback = Math.Min(MirrorLookbackBars, htfBars);
				    for (int i = lookback; i >= 0; i--)
				    {
				        if (_srcE[t].PatternELongLevel.Count > i) SyncSingleSignal(t, bipIdx, i, _srcE[t].PatternELongLevel[i], true, "E", _srcE[t].PatternELongStartTimestamp.Count > i ? _srcE[t].PatternELongStartTimestamp[i] : 0);
				        if (_srcE[t].PatternEShortLevel.Count > i) SyncSingleSignal(t, bipIdx, i, _srcE[t].PatternEShortLevel[i], false, "E", _srcE[t].PatternEShortStartTimestamp.Count > i ? _srcE[t].PatternEShortStartTimestamp[i] : 0);
				    }
				}
            }
            SyncGainLossLevels();
        }

        private void SyncGainLossLevels()
        {
            for (int t = 0; t < NUM_TF; t++)
            {
                if (!IsGlTfEnabled(t) || _srcGL[t] == null) continue;
                int bipIdx = t + 2; 
                if (CurrentBars[bipIdx] < 2) continue;
                SyncSingleGLSignal(t, bipIdx, _srcGL[t].TSU1PriceSeries[0], _srcGL[t].TSU1TimeSeries[0], "TSU1");
                SyncSingleGLSignal(t, bipIdx, _srcGL[t].TSU2PriceSeries[0], _srcGL[t].TSU2TimeSeries[0], "TSU2");
                SyncSingleGLSignal(t, bipIdx, _srcGL[t].TSD1PriceSeries[0], _srcGL[t].TSD1TimeSeries[0], "TSD1");
                SyncSingleGLSignal(t, bipIdx, _srcGL[t].TSD2PriceSeries[0], _srcGL[t].TSD2TimeSeries[0], "TSD2");
            }
        }

		private void SyncSingleGLSignal(int t, int bipIdx, double priceLevel, double startTicks, string typeTag)
		{
		    if (_activeGL[t] == null) _activeGL[t] = new Dictionary<string, ActiveGLState>();
		    var activeMap = _activeGL[t];
		    DateTime nowTime = Times[bipIdx][0];
		    bool hasValidValue = !(double.IsNaN(priceLevel) || double.IsNaN(startTicks) || startTicks <= 0);
		    DateTime anchorTime = hasValidValue ? new DateTime((long)startTicks) : Core.Globals.MinDate;
		    ActiveGLState active;
		    activeMap.TryGetValue(typeTag, out active);
		    if (active != null && active.Level != null && !trackedGL[t].Contains(active.Level)) { activeMap.Remove(typeTag); active = null; }
		    if (!hasValidValue) {
		        if (active != null && active.Level != null) {
		            if (active.Level.HtfEndTime < nowTime) { active.Level.HtfEndTime = nowTime; DrawTrackedLevel(active.Level, trackedGL[t]); }
		            activeMap.Remove(typeTag);
		        }
		        return;
		    }
		    if (active == null || active.Level == null) {
		        var newLevel = new TrackedLevel { Tag = $"MR_{typeTag}_TF{t}_{anchorTime.Ticks}_{Math.Round(priceLevel, 4)}", Price = priceLevel, HtfStartTime = anchorTime, HtfEndTime = nowTime, IsLong = typeTag.StartsWith("TSU"), PatternType = typeTag, Label = $"GL {_tfLabels[t]} {typeTag}", TfIndex = t, LastUpdatedBip0Bar = CurrentBars[0] };
		        trackedGL[t].Add(newLevel); DrawTrackedLevel(newLevel, trackedGL[t]);
		        activeMap[typeTag] = new ActiveGLState { Level = newLevel, LastPrice = priceLevel, LastAnchorTime = anchorTime };
		        return;
		    }
		    bool sameSegment = Math.Abs(active.LastPrice - priceLevel) < 0.0001 && active.LastAnchorTime == anchorTime;
		    if (sameSegment) { if (active.Level.HtfEndTime < nowTime) { active.Level.HtfEndTime = nowTime; active.Level.LastUpdatedBip0Bar = CurrentBars[0]; DrawTrackedLevel(active.Level, trackedGL[t]); } return; }
		    DateTime transitionTime = anchorTime;
		    if (transitionTime == Core.Globals.MinDate || transitionTime < active.Level.HtfStartTime) transitionTime = nowTime;
		    active.Level.HtfEndTime = transitionTime; DrawTrackedLevel(active.Level, trackedGL[t]);
		    var replacement = new TrackedLevel { Tag = $"MR_{typeTag}_TF{t}_{transitionTime.Ticks}_{Math.Round(priceLevel, 4)}", Price = priceLevel, HtfStartTime = transitionTime, HtfEndTime = nowTime, IsLong = typeTag.StartsWith("TSU"), PatternType = typeTag, Label = $"GL {_tfLabels[t]} {typeTag}", TfIndex = t, LastUpdatedBip0Bar = CurrentBars[0] };
		    trackedGL[t].Add(replacement); DrawTrackedLevel(replacement, trackedGL[t]);
		    activeMap[typeTag] = new ActiveGLState { Level = replacement, LastPrice = priceLevel, LastAnchorTime = anchorTime };
		}
		
		private void SyncSingleSignal(int t, int bipIdx, int barsAgoOnHtf, double priceLevel, bool isLong, string pType, double startTimestampExtracted)
        {
            if (priceLevel == 0 || double.IsNaN(startTimestampExtracted)) return;
            DateTime htfBarCloseTime = Times[bipIdx][barsAgoOnHtf];
            DateTime htfBarStartTime = (barsAgoOnHtf + 1 < CurrentBars[bipIdx]) ? Times[bipIdx][barsAgoOnHtf + 1] : htfBarCloseTime.AddMinutes(-_tfMinutes[t]);
            DateTime signalStartTime = htfBarStartTime;
            long extractedTicks = 0;
            try { extractedTicks = Convert.ToInt64(Math.Round(startTimestampExtracted)); } catch { extractedTicks = 0; }
            if (extractedTicks >= Core.Globals.MinDate.Ticks && extractedTicks <= DateTime.MaxValue.Ticks) signalStartTime = new DateTime(extractedTicks);
            if (signalStartTime < Core.Globals.MinDate) signalStartTime = htfBarStartTime;

            var tList = pType == "A" ? trackedA[t] : pType == "B" ? trackedB[t] : pType == "C" ? trackedC[t] : trackedE[t];
            string tag = $"MR_{pType}_TF{t}_{(isLong ? "L" : "S")}_{Math.Round(priceLevel, 4)}";
            var existing = tList.FirstOrDefault(x => x.PatternType == pType && x.IsLong == isLong && Math.Abs(x.Price - priceLevel) < 0.0001 && x.HtfStartTime == signalStartTime);
            if (existing != null) {
                if (existing.HtfEndTime < htfBarCloseTime) { existing.HtfEndTime = htfBarCloseTime; existing.LastUpdatedBip0Bar = CurrentBars[0]; DrawTrackedLevel(existing, tList); }
            } else {
                var newLevel = new TrackedLevel { Tag = tag + "_" + signalStartTime.Ticks, Price = priceLevel, HtfStartTime = signalStartTime, HtfEndTime = htfBarCloseTime, IsLong = isLong, PatternType = pType, Label = $"{pType} {_tfLabels[t]}", TfIndex = t, LastUpdatedBip0Bar = CurrentBars[0] };
                tList.Add(newLevel); DrawTrackedLevel(newLevel, tList);
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

            bool visible = tl.PatternType.StartsWith("TS") ? IsGlTfShown(tl.TfIndex) : IsTfShown(tl.TfIndex);
            
            // Re-apply bias filter dynamically to hide invalidated entries
            if (visible && !tl.PatternType.StartsWith("TS") && ViolatesHTFVPBias(tl))
                visible = false;
                
            if (visible)
            {
                Draw.Line(this, tl.Tag, false, tl.HtfStartTime, tl.Price, tl.HtfEndTime, tl.Price, c, dsh, lw);
                if (tl.PatternType.StartsWith("TS")) {
                    Draw.Text(this, tl.Tag + "_lbl", false, tl.Label + (tl.IsLong ? " ▲" : " ▼"), tl.HtfEndTime, tl.Price + (tl.IsLong ? -(LabelOffsetTicks * TickSize) : (LabelOffsetTicks * TickSize)), 0, c, new SimpleFont("Arial", LabelFontSize), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
                } else {
                    Draw.Text(this, tl.Tag + "_lbl", false, tl.Label + (tl.IsLong ? " ▲" : " ▼"), tl.HtfStartTime, tl.Price + (LabelOffsetTicks * TickSize), 0, c, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                }
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

		private bool IsHtfVpEnabled(int t)
        {
            switch(t) {
                case 0: return EnableHTFVP1_Daily;
                case 1: return EnableHTFVP2_240m;
                case 2: return EnableHTFVP3_60m;
                case 3: return EnableHTFVP4_30m;
                case 4: return EnableHTFVP5_15m;
                case 5: return EnableHTFVP6_10m;
                case 6: return EnableHTFVP7_5m;
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
		private bool IsTfShown(int t)
        {
            switch(t) {
                case 0: return ShowTF1_Daily;
                case 1: return ShowTF2_240m;
                case 2: return ShowTF3_60m;
                case 3: return ShowTF4_30m;
                case 4: return ShowTF5_15m;
                case 5: return ShowTF6_10m;
                case 6: return ShowTF7_5m;
            }
            return false;
        }

		private bool IsGlTfShown(int t)
        {
            switch(t) {
                case 0: return ShowGLTF1_Daily;
                case 1: return ShowGLTF2_240m;
                case 2: return ShowGLTF3_60m;
                case 3: return ShowGLTF4_30m;
                case 4: return ShowGLTF5_15m;
                case 5: return ShowGLTF6_10m;
                case 6: return ShowGLTF7_5m;
            }
            return false;
        }

		private bool IsHtfVpShown(int t)
        {
            switch(t) {
                case 0: return ShowHTFVP1_Daily;
                case 1: return ShowHTFVP2_240m;
                case 2: return ShowHTFVP3_60m;
                case 3: return ShowHTFVP4_30m;
                case 4: return ShowHTFVP5_15m;
                case 5: return ShowHTFVP6_10m;
                case 6: return ShowHTFVP7_5m;
            }
            return false;
        }

		private bool IsDevVpShown(int t)
        {
            switch(t) {
                case 0: return ShowDevVP1_Daily;
                case 1: return ShowDevVP2_240m;
                case 2: return ShowDevVP3_60m;
                case 3: return ShowDevVP4_30m;
                case 4: return ShowDevVP5_15m;
                case 5: return ShowDevVP6_10m;
                case 6: return ShowDevVP7_5m;
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
            if (CurrentBars[bipIdx] < 2) return;
            DateTime justClosedBarCloseTime = Times[bipIdx][1];
            double justClosedBarClosePrice  = Closes[bipIdx][1];
            CleanupInvalidatedList(trackedA[t], justClosedBarCloseTime, justClosedBarClosePrice);
            CleanupInvalidatedList(trackedB[t], justClosedBarCloseTime, justClosedBarClosePrice);
            CleanupInvalidatedList(trackedC[t], justClosedBarCloseTime, justClosedBarClosePrice);
            CleanupInvalidatedList(trackedE[t], justClosedBarCloseTime, justClosedBarClosePrice);
        }

        private void CleanupInvalidatedList(List<TrackedLevel> tList, DateTime justClosedBarCloseTime, double justClosedBarClosePrice)
        {
            if (tList == null || tList.Count == 0) return;
            var toRemove = new List<TrackedLevel>();
            foreach (var tl in tList) {
                if (tl.HtfEndTime != justClosedBarCloseTime) continue;
                if (tl.IsLong ? justClosedBarClosePrice < tl.Price : justClosedBarClosePrice > tl.Price) toRemove.Add(tl);
            }
            foreach (var tl in toRemove) RemoveTrackedLevel(tl, tList);
        }
		#endregion
		
		#region Settings Modal
		
		private void CreateToolbarButton()
        {
            try {
                if (ChartControl == null) return;
                chartWindow = Window.GetWindow(ChartControl.Parent) as NinjaTrader.Gui.Chart.Chart;
                if (chartWindow == null || settingsButton != null) return;
                
                settingsButton = IndicatorVisualStyleHelper.CreateSettingsButton("Mirror Settings", "AlightenMirrorSettingsBtn", (s, e) => OpenSettingsWindow());
                settingsButton.Width = 110;
                settingsButton.ToolTip = "Configure Mirror V0014 visibility settings";
                chartWindow.MainMenu.Add(settingsButton);
            } catch (Exception ex) { Print("[AlightenMirrorV0014] toolbar error: " + ex.Message); }
        }

        private void TryRemoveToolbarButton()
        {
            try { if (chartWindow != null && settingsButton != null) chartWindow.MainMenu.Remove(settingsButton); } catch { }
            try { if (settingsWindow != null) settingsWindow.Close(); } catch { }
            settingsButton = null; settingsWindow = null; chartWindow = null;
        }

        public void ToggleSettingsWindow()
        {
            if (settingsWindow == null) { OpenSettingsWindow(); return; }
            try
            {
                if (settingsWindow.IsVisible) { settingsWindow.Close(); settingsWindow = null; }
                else { OpenSettingsWindow(); }
            }
            catch
            {
                settingsWindow = null;
                OpenSettingsWindow();
            }
        }

        public void OpenSettingsWindow()
		{
		    if (settingsWindow != null)
		    {
		        try { settingsWindow.Activate(); return; } catch { settingsWindow = null; }
		    }
		
		    try
		    {
		        ChartControl.Dispatcher.InvokeAsync(() =>
		        {
		            var win = BuildInlineSettingsWindow();
		            win.Owner = Window.GetWindow(ChartControl.Parent);
		            win.Closed += (s, e) => settingsWindow = null;
		            settingsWindow = win;
		            win.Show();
		        });
		    }
		    catch (Exception ex)
		    {
		        Print($"[AlightenMirrorV0014] Failed to open settings: {ex.Message}");
		    }
		}

		private Window BuildInlineSettingsWindow()
		{
		    var win = new Window
		    {
		        Title = "Mirror Settings",
		        Width = 445,
		        Height = 790,
		        WindowStartupLocation = WindowStartupLocation.CenterOwner,
		        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
		        Foreground = System.Windows.Media.Brushes.Gainsboro,
		        ResizeMode = ResizeMode.NoResize,
		        Topmost = true,
		        ShowInTaskbar = false,
		        Owner = Window.GetWindow(ChartControl?.Parent)
		    };
		
		    IndicatorVisualStyleHelper.ApplyDialogChrome(win);
		
		    var root = new StackPanel { Margin = new Thickness(12) };
		
		    GroupBox Group(string title, out StackPanel content)
		    {
		        var gb = new GroupBox
		        {
		            Header = title,
		            Margin = new Thickness(0, 6, 0, 6),
		            Foreground = System.Windows.Media.Brushes.Gainsboro,
		            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60))
		        };
		        content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
		        gb.Content = content;
		        return gb;
		    }

            CheckBox CheckRow(string label, bool initial, Action<bool> onChanged)
            {
                var cb = IndicatorVisualStyleHelper.CreateDialogCheckBox(label);
                cb.IsChecked = initial;
                cb.Margin = new Thickness(0, 3, 12, 3);
                cb.Checked += (s, e) => { onChanged(true); ForceUISync(); };
                cb.Unchecked += (s, e) => { onChanged(false); ForceUISync(); };
                return cb;
            }

		    // 1. Mirror Timeframes
		    root.Children.Add(Group("Mirror Timeframes", out var mirrorPanel));
            var mirrorGrid = new StackPanel { Orientation = Orientation.Vertical };
            var mRow1 = new StackPanel { Orientation = Orientation.Horizontal };
            var mRow2 = new StackPanel { Orientation = Orientation.Horizontal };
            mRow1.Children.Add(CheckRow("Daily", ShowTF1_Daily, v => ShowTF1_Daily = v));
            mRow1.Children.Add(CheckRow("240m", ShowTF2_240m, v => ShowTF2_240m = v));
            mRow1.Children.Add(CheckRow("60m", ShowTF3_60m, v => ShowTF3_60m = v));
            mRow1.Children.Add(CheckRow("30m", ShowTF4_30m, v => ShowTF4_30m = v));
            mRow2.Children.Add(CheckRow("15m", ShowTF5_15m, v => ShowTF5_15m = v));
            mRow2.Children.Add(CheckRow("10m", ShowTF6_10m, v => ShowTF6_10m = v));
            mRow2.Children.Add(CheckRow("5m", ShowTF7_5m, v => ShowTF7_5m = v));
            mirrorGrid.Children.Add(mRow1); mirrorGrid.Children.Add(mRow2);
            mirrorPanel.Children.Add(mirrorGrid);

		    // 2. Gain Loss Timeframes
		    root.Children.Add(Group("Gain/Loss Timeframes", out var glPanel));
            var glGrid = new StackPanel { Orientation = Orientation.Vertical };
            var glRow1 = new StackPanel { Orientation = Orientation.Horizontal };
            var glRow2 = new StackPanel { Orientation = Orientation.Horizontal };
            glRow1.Children.Add(CheckRow("Daily", ShowGLTF1_Daily, v => ShowGLTF1_Daily = v));
            glRow1.Children.Add(CheckRow("240m", ShowGLTF2_240m, v => ShowGLTF2_240m = v));
            glRow1.Children.Add(CheckRow("60m", ShowGLTF3_60m, v => ShowGLTF3_60m = v));
            glRow1.Children.Add(CheckRow("30m", ShowGLTF4_30m, v => ShowGLTF4_30m = v));
            glRow2.Children.Add(CheckRow("15m", ShowGLTF5_15m, v => ShowGLTF5_15m = v));
            glRow2.Children.Add(CheckRow("10m", ShowGLTF6_10m, v => ShowGLTF6_10m = v));
            glRow2.Children.Add(CheckRow("5m", ShowGLTF7_5m, v => ShowGLTF7_5m = v));
            glGrid.Children.Add(glRow1); glGrid.Children.Add(glRow2);
            glPanel.Children.Add(glGrid);

		    // 3. HTFVP Timeframes
		    root.Children.Add(Group("HTF VP Levels", out var htfvpPanel));
            var hpGrid = new StackPanel { Orientation = Orientation.Vertical };
            var hpRow1 = new StackPanel { Orientation = Orientation.Horizontal };
            var hpRow2 = new StackPanel { Orientation = Orientation.Horizontal };
            hpRow1.Children.Add(CheckRow("Daily", ShowHTFVP1_Daily, v => ShowHTFVP1_Daily = v));
            hpRow1.Children.Add(CheckRow("240m", ShowHTFVP2_240m, v => ShowHTFVP2_240m = v));
            hpRow1.Children.Add(CheckRow("60m", ShowHTFVP3_60m, v => ShowHTFVP3_60m = v));
            hpRow1.Children.Add(CheckRow("30m", ShowHTFVP4_30m, v => ShowHTFVP4_30m = v));
            hpRow2.Children.Add(CheckRow("15m", ShowHTFVP5_15m, v => ShowHTFVP5_15m = v));
            hpRow2.Children.Add(CheckRow("10m", ShowHTFVP6_10m, v => ShowHTFVP6_10m = v));
            hpRow2.Children.Add(CheckRow("5m", ShowHTFVP7_5m, v => ShowHTFVP7_5m = v));
            hpGrid.Children.Add(hpRow1); hpGrid.Children.Add(hpRow2);
            htfvpPanel.Children.Add(hpGrid);

		    // 4. DEV VP Timeframes
		    root.Children.Add(Group("Developing VP Levels", out var devPanel));
            var dpGrid = new StackPanel { Orientation = Orientation.Vertical };
            var dpRow1 = new StackPanel { Orientation = Orientation.Horizontal };
            var dpRow2 = new StackPanel { Orientation = Orientation.Horizontal };
            dpRow1.Children.Add(CheckRow("Daily", ShowDevVP1_Daily, v => ShowDevVP1_Daily = v));
            dpRow1.Children.Add(CheckRow("240m", ShowDevVP2_240m, v => ShowDevVP2_240m = v));
            dpRow1.Children.Add(CheckRow("60m", ShowDevVP3_60m, v => ShowDevVP3_60m = v));
            dpRow1.Children.Add(CheckRow("30m", ShowDevVP4_30m, v => ShowDevVP4_30m = v));
            dpRow2.Children.Add(CheckRow("15m", ShowDevVP5_15m, v => ShowDevVP5_15m = v));
            dpRow2.Children.Add(CheckRow("10m", ShowDevVP6_10m, v => ShowDevVP6_10m = v));
            dpRow2.Children.Add(CheckRow("5m", ShowDevVP7_5m, v => ShowDevVP7_5m = v));
            dpGrid.Children.Add(dpRow1); dpGrid.Children.Add(dpRow2);
            devPanel.Children.Add(dpGrid);

		    // 5. Bias Filter
		    root.Children.Add(Group("Bias Context Filter", out var biasPanel));
            var bGrid = new StackPanel { Orientation = Orientation.Vertical };
            var bcb = CheckRow("Enable HTF VP Filter (Bias Gating)", EnableHTFVPFilter, v => EnableHTFVPFilter = v);
            
            var dropRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            dropRow.Children.Add(new TextBlock { Text = "Bias Timeframe: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) });
            var combo = new ComboBox { Width = 150 };
            foreach (var item in Enum.GetValues(typeof(HTFVPBiasTimeframe))) combo.Items.Add(item);
            combo.SelectedItem = BiasTimeframe;
            combo.SelectionChanged += (s, e) => { BiasTimeframe = (HTFVPBiasTimeframe)combo.SelectedItem; ForceUISync(); };
            dropRow.Children.Add(combo);

            bGrid.Children.Add(bcb);
            bGrid.Children.Add(dropRow);
            biasPanel.Children.Add(bGrid);

		    // ===== Buttons =====
		    var btnRow = new StackPanel
		    {
		        Orientation = Orientation.Horizontal,
		        HorizontalAlignment = HorizontalAlignment.Right,
		        Margin = new Thickness(0, 24, 0, 0)
		    };
		    var btnClean = IndicatorVisualStyleHelper.CreatePrimaryDialogButton("Clean Invalid", (s, e) => { PerformManualRefreshCleanup(); ForceUISync(); });
		    var btnClose = IndicatorVisualStyleHelper.CreateDangerDialogButton("Close", (s, e) => { settingsWindow?.Close(); settingsWindow = null; });
		    btnRow.Children.Add(btnClean);
		    btnRow.Children.Add(btnClose);
		    root.Children.Add(btnRow);
		
		    win.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
		    return win;
		}

		private void ForceUISync()
		{
		    try
		    {
                RemoveDrawObjects();
                
                // Re-draw Mirror Tracked with dynamic gating applied during the draw!
                for (int t = 0; t < NUM_TF; t++) {
                    if (trackedA[t] != null) foreach (var tl in trackedA[t]) DrawTrackedLevel(tl, trackedA[t]);
                    if (trackedB[t] != null) foreach (var tl in trackedB[t]) DrawTrackedLevel(tl, trackedB[t]);
                    if (trackedC[t] != null) foreach (var tl in trackedC[t]) DrawTrackedLevel(tl, trackedC[t]);
                    if (trackedE[t] != null) foreach (var tl in trackedE[t]) DrawTrackedLevel(tl, trackedE[t]);
                    if (trackedGL[t] != null) foreach (var tl in trackedGL[t]) DrawTrackedLevel(tl, trackedGL[t]);
                }
                
                RedrawAllHTFVPPassthroughs();
                RedrawAllDevelopingHTFVPPassthroughs();
                
                if (ChartControl != null) ChartControl.InvalidateVisual();
		    }
		    catch (Exception ex)
		    {
		        Print("[MirrorV0014] ForceUISync Error: " + ex.Message);
		    }
		}

		private void RedrawAllHTFVPPassthroughs()
		{
		    DateTime endTime = _lastPrimaryBarTime != Core.Globals.MinDate ? _lastPrimaryBarTime : Core.Globals.MaxDate;
		    
		    for (int t = 0; t < NUM_TF; t++)
		    {
		        if (!IsHtfVpShown(t) || _srcHTFVP[t] == null || _srcHTFVP[t].Profiles == null) continue;
		        foreach (var activeProfile in _srcHTFVP[t].Profiles)
		        {
		            if (double.IsNaN(activeProfile.VAH) || double.IsNaN(activeProfile.VAL) || double.IsNaN(activeProfile.POC) || activeProfile.POC <= 0)
		                continue;

		            DateTime tEnd = endTime;
		            if (activeProfile.EndTime != Core.Globals.MinDate && tEnd > activeProfile.EndTime)
		                tEnd = activeProfile.EndTime;
		                        
		            if (activeProfile.StartTime >= tEnd) continue;

		            string tagVAH = "MR_HTFVP_VAH_TF" + t + "_" + activeProfile.StartTime.Ticks;
		            string tagVAL = "MR_HTFVP_VAL_TF" + t + "_" + activeProfile.StartTime.Ticks;
		            string tagPOC = "MR_HTFVP_POC_TF" + t + "_" + activeProfile.StartTime.Ticks;
		            
		            Draw.Line(this, tagVAH, false, activeProfile.StartTime, activeProfile.VAH, tEnd, activeProfile.VAH, HTFVPVAHColor, HTFVPVAHStyle, HTFVPVAHWidth);
		            Draw.Line(this, tagVAL, false, activeProfile.StartTime, activeProfile.VAL, tEnd, activeProfile.VAL, HTFVPVALColor, HTFVPVALStyle, HTFVPVALWidth);
		            Draw.Line(this, tagPOC, false, activeProfile.StartTime, activeProfile.POC, tEnd, activeProfile.POC, HTFVPPOCColor, HTFVPPOCStyle, HTFVPPOCWidth);
		            
		            Draw.Text(this, tagVAH + "_lbl", false, _tfLabels[t] + " VAH", tEnd, activeProfile.VAH + (LabelOffsetTicks * TickSize), 0, HTFVPVAHColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		            Draw.Text(this, tagVAL + "_lbl", false, _tfLabels[t] + " VAL", tEnd, activeProfile.VAL + (LabelOffsetTicks * TickSize), 0, HTFVPVALColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		            Draw.Text(this, tagPOC + "_lbl", false, _tfLabels[t] + " POC", tEnd, activeProfile.POC + (LabelOffsetTicks * TickSize), 0, HTFVPPOCColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
		        }
		    }
		}

		private void RedrawAllDevelopingHTFVPPassthroughs()
		{
		    DateTime endTime = _lastPrimaryBarTime != Core.Globals.MinDate ? _lastPrimaryBarTime : Core.Globals.MaxDate;
		    
            for (int t = 0; t < NUM_TF; t++)
            {
                if (!IsDevVpShown(t) || _srcHTFVP[t] == null || _srcHTFVP[t].Profiles == null) continue;

                // Redraw strictly the single active developing profile
                var devProfile = _srcHTFVP[t].GetDevelopingProfile();
                if (devProfile == null) continue;

                if (double.IsNaN(devProfile.VAH) || double.IsNaN(devProfile.VAL) || double.IsNaN(devProfile.POC) || devProfile.POC <= 0) continue;

                DateTime startTime = devProfile.StartTime;
                if (startTime >= endTime) continue;

                string tagVAH = "MR_DEV_HTFVP_VAH_TF" + t;
                string tagVAL = "MR_DEV_HTFVP_VAL_TF" + t;
                string tagPOC = "MR_DEV_HTFVP_POC_TF" + t;

                Draw.Line(this, tagVAH, false, startTime, devProfile.VAH, endTime, devProfile.VAH, HTFVPVAHColor, HTFVPDevVAHStyle, HTFVPDevVAHWidth);
                Draw.Line(this, tagVAL, false, startTime, devProfile.VAL, endTime, devProfile.VAL, HTFVPVALColor, HTFVPDevVALStyle, HTFVPDevVALWidth);
                Draw.Line(this, tagPOC, false, startTime, devProfile.POC, endTime, devProfile.POC, HTFVPPOCColor, HTFVPDevPOCStyle, HTFVPDevPOCWidth);
                
                Draw.Text(this, tagVAH + "_lbl", false, _tfLabels[t] + " Dev VAH", endTime, devProfile.VAH + (LabelOffsetTicks * TickSize), 0, HTFVPVAHColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                Draw.Text(this, tagVAL + "_lbl", false, _tfLabels[t] + " Dev VAL", endTime, devProfile.VAL + (LabelOffsetTicks * TickSize), 0, HTFVPVALColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                Draw.Text(this, tagPOC + "_lbl", false, _tfLabels[t] + " Dev POC", endTime, devProfile.POC + (LabelOffsetTicks * TickSize), 0, HTFVPPOCColor, new SimpleFont("Arial", LabelFontSize), TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
            }
		}

		private void RemoveTrackedLevel(TrackedLevel tl, List<TrackedLevel> tList)
		{
		    if (tl == null) return;
		    try { if (!string.IsNullOrEmpty(tl.Tag)) RemoveDrawObject(tl.Tag); } catch { }
		    try { if (!string.IsNullOrEmpty(tl.Tag)) RemoveDrawObject(tl.Tag + "_lbl"); } catch { }
		    try { tList.Remove(tl); } catch { }
		    if (tl.PatternType.StartsWith("TS")) {
		        int tf = tl.TfIndex;
		        if (tf >= 0 && tf < NUM_TF && _activeGL[tf] != null) {
		            ActiveGLState active;
		            if (_activeGL[tf].TryGetValue(tl.PatternType, out active)) {
		                if (active != null && object.ReferenceEquals(active.Level, tl)) _activeGL[tf].Remove(tl.PatternType);
		            }
		        }
		    }
		}
		
		private void PerformManualRefreshCleanup()
		{
		    if (_lastPrimaryBarTime == Core.Globals.MinDate || double.IsNaN(_lastPrimaryClose)) return;
		    DateTime primaryTime = _lastPrimaryBarTime;
		    double currentPrice = _lastPrimaryClose;
		    for (int t = 0; t < NUM_TF; t++) {
		        if (IsTfEnabled(t)) {
		            CleanupCurrentBarInvalidatedList(trackedA[t], primaryTime, currentPrice);
		            CleanupCurrentBarInvalidatedList(trackedB[t], primaryTime, currentPrice);
		            CleanupCurrentBarInvalidatedList(trackedC[t], primaryTime, currentPrice);
		            CleanupCurrentBarInvalidatedList(trackedE[t], primaryTime, currentPrice);
		        }
                if (IsGlTfEnabled(t)) CleanupCurrentBarInvalidatedList(trackedGL[t], primaryTime, currentPrice);
		    }
		}
		
		private void CleanupCurrentBarInvalidatedList(List<TrackedLevel> tList, DateTime primaryTime, double currentPrice)
		{
		    if (tList == null || tList.Count == 0) return;
		    var toRemove = new List<TrackedLevel>();
		    foreach (var tl in tList) {
		        // We only clean up levels that are "currently active" (mirrored into the current bar)
		        if (tl.HtfEndTime < primaryTime) continue;
		        if (tl.IsLong ? currentPrice < tl.Price : currentPrice > tl.Price) toRemove.Add(tl);
		    }
		    foreach (var tl in toRemove) RemoveTrackedLevel(tl, tList);
		}
		#endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenMirrorV0014[] cacheAlightenMirrorV0014;
		public AlightenMirrorV0014 AlightenMirrorV0014(bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool showTF1_Daily, bool enableTF2_240m, bool showTF2_240m, bool enableTF3_60m, bool showTF3_60m, bool enableTF4_30m, bool showTF4_30m, bool enableTF5_15m, bool showTF5_15m, bool enableTF6_10m, bool showTF6_10m, bool enableTF7_5m, bool showTF7_5m, bool enableGLTF1_Daily, bool showGLTF1_Daily, bool enableGLTF2_240m, bool showGLTF2_240m, bool enableGLTF3_60m, bool showGLTF3_60m, bool enableGLTF4_30m, bool showGLTF4_30m, bool enableGLTF5_15m, bool showGLTF5_15m, bool enableGLTF6_10m, bool showGLTF6_10m, bool enableGLTF7_5m, bool showGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, HTFVPBiasTimeframe biasTimeframe, bool enableHTFVP1_Daily, bool showHTFVP1_Daily, bool showDevVP1_Daily, bool enableHTFVP2_240m, bool showHTFVP2_240m, bool showDevVP2_240m, bool enableHTFVP3_60m, bool showHTFVP3_60m, bool showDevVP3_60m, bool enableHTFVP4_30m, bool showHTFVP4_30m, bool showDevVP4_30m, bool enableHTFVP5_15m, bool showHTFVP5_15m, bool showDevVP5_15m, bool enableHTFVP6_10m, bool showHTFVP6_10m, bool showDevVP6_10m, bool enableHTFVP7_5m, bool showHTFVP7_5m, bool showDevVP7_5m, bool enableHTFVPFilter, double hTFVPValueAreaPercentage, int hTFVPBarsToProcess, Brush hTFVPVAHColor, Brush hTFVPVALColor, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle, int hTFVPDevVAHWidth, DashStyleHelper hTFVPDevVAHStyle, int hTFVPDevVALWidth, DashStyleHelper hTFVPDevVALStyle, int hTFVPDevPOCWidth, DashStyleHelper hTFVPDevPOCStyle)
		{
			return AlightenMirrorV0014(Input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, showTF1_Daily, enableTF2_240m, showTF2_240m, enableTF3_60m, showTF3_60m, enableTF4_30m, showTF4_30m, enableTF5_15m, showTF5_15m, enableTF6_10m, showTF6_10m, enableTF7_5m, showTF7_5m, enableGLTF1_Daily, showGLTF1_Daily, enableGLTF2_240m, showGLTF2_240m, enableGLTF3_60m, showGLTF3_60m, enableGLTF4_30m, showGLTF4_30m, enableGLTF5_15m, showGLTF5_15m, enableGLTF6_10m, showGLTF6_10m, enableGLTF7_5m, showGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, biasTimeframe, enableHTFVP1_Daily, showHTFVP1_Daily, showDevVP1_Daily, enableHTFVP2_240m, showHTFVP2_240m, showDevVP2_240m, enableHTFVP3_60m, showHTFVP3_60m, showDevVP3_60m, enableHTFVP4_30m, showHTFVP4_30m, showDevVP4_30m, enableHTFVP5_15m, showHTFVP5_15m, showDevVP5_15m, enableHTFVP6_10m, showHTFVP6_10m, showDevVP6_10m, enableHTFVP7_5m, showHTFVP7_5m, showDevVP7_5m, enableHTFVPFilter, hTFVPValueAreaPercentage, hTFVPBarsToProcess, hTFVPVAHColor, hTFVPVALColor, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle, hTFVPDevVAHWidth, hTFVPDevVAHStyle, hTFVPDevVALWidth, hTFVPDevVALStyle, hTFVPDevPOCWidth, hTFVPDevPOCStyle);
		}

		public AlightenMirrorV0014 AlightenMirrorV0014(ISeries<double> input, bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool showTF1_Daily, bool enableTF2_240m, bool showTF2_240m, bool enableTF3_60m, bool showTF3_60m, bool enableTF4_30m, bool showTF4_30m, bool enableTF5_15m, bool showTF5_15m, bool enableTF6_10m, bool showTF6_10m, bool enableTF7_5m, bool showTF7_5m, bool enableGLTF1_Daily, bool showGLTF1_Daily, bool enableGLTF2_240m, bool showGLTF2_240m, bool enableGLTF3_60m, bool showGLTF3_60m, bool enableGLTF4_30m, bool showGLTF4_30m, bool enableGLTF5_15m, bool showGLTF5_15m, bool enableGLTF6_10m, bool showGLTF6_10m, bool enableGLTF7_5m, bool showGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, HTFVPBiasTimeframe biasTimeframe, bool enableHTFVP1_Daily, bool showHTFVP1_Daily, bool showDevVP1_Daily, bool enableHTFVP2_240m, bool showHTFVP2_240m, bool showDevVP2_240m, bool enableHTFVP3_60m, bool showHTFVP3_60m, bool showDevVP3_60m, bool enableHTFVP4_30m, bool showHTFVP4_30m, bool showDevVP4_30m, bool enableHTFVP5_15m, bool showHTFVP5_15m, bool showDevVP5_15m, bool enableHTFVP6_10m, bool showHTFVP6_10m, bool showDevVP6_10m, bool enableHTFVP7_5m, bool showHTFVP7_5m, bool showDevVP7_5m, bool enableHTFVPFilter, double hTFVPValueAreaPercentage, int hTFVPBarsToProcess, Brush hTFVPVAHColor, Brush hTFVPVALColor, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle, int hTFVPDevVAHWidth, DashStyleHelper hTFVPDevVAHStyle, int hTFVPDevVALWidth, DashStyleHelper hTFVPDevVALStyle, int hTFVPDevPOCWidth, DashStyleHelper hTFVPDevPOCStyle)
		{
			if (cacheAlightenMirrorV0014 != null)
				for (int idx = 0; idx < cacheAlightenMirrorV0014.Length; idx++)
					if (cacheAlightenMirrorV0014[idx] != null && cacheAlightenMirrorV0014[idx].EnablePatternA == enablePatternA && cacheAlightenMirrorV0014[idx].EnablePatternB == enablePatternB && cacheAlightenMirrorV0014[idx].EnablePatternC == enablePatternC && cacheAlightenMirrorV0014[idx].EnablePatternE == enablePatternE && cacheAlightenMirrorV0014[idx].SrcBarsToProcess == srcBarsToProcess && cacheAlightenMirrorV0014[idx].MirrorLookbackBars == mirrorLookbackBars && cacheAlightenMirrorV0014[idx].EnableInvalidatedCleanup == enableInvalidatedCleanup && cacheAlightenMirrorV0014[idx].EnableTF1_Daily == enableTF1_Daily && cacheAlightenMirrorV0014[idx].ShowTF1_Daily == showTF1_Daily && cacheAlightenMirrorV0014[idx].EnableTF2_240m == enableTF2_240m && cacheAlightenMirrorV0014[idx].ShowTF2_240m == showTF2_240m && cacheAlightenMirrorV0014[idx].EnableTF3_60m == enableTF3_60m && cacheAlightenMirrorV0014[idx].ShowTF3_60m == showTF3_60m && cacheAlightenMirrorV0014[idx].EnableTF4_30m == enableTF4_30m && cacheAlightenMirrorV0014[idx].ShowTF4_30m == showTF4_30m && cacheAlightenMirrorV0014[idx].EnableTF5_15m == enableTF5_15m && cacheAlightenMirrorV0014[idx].ShowTF5_15m == showTF5_15m && cacheAlightenMirrorV0014[idx].EnableTF6_10m == enableTF6_10m && cacheAlightenMirrorV0014[idx].ShowTF6_10m == showTF6_10m && cacheAlightenMirrorV0014[idx].EnableTF7_5m == enableTF7_5m && cacheAlightenMirrorV0014[idx].ShowTF7_5m == showTF7_5m && cacheAlightenMirrorV0014[idx].EnableGLTF1_Daily == enableGLTF1_Daily && cacheAlightenMirrorV0014[idx].ShowGLTF1_Daily == showGLTF1_Daily && cacheAlightenMirrorV0014[idx].EnableGLTF2_240m == enableGLTF2_240m && cacheAlightenMirrorV0014[idx].ShowGLTF2_240m == showGLTF2_240m && cacheAlightenMirrorV0014[idx].EnableGLTF3_60m == enableGLTF3_60m && cacheAlightenMirrorV0014[idx].ShowGLTF3_60m == showGLTF3_60m && cacheAlightenMirrorV0014[idx].EnableGLTF4_30m == enableGLTF4_30m && cacheAlightenMirrorV0014[idx].ShowGLTF4_30m == showGLTF4_30m && cacheAlightenMirrorV0014[idx].EnableGLTF5_15m == enableGLTF5_15m && cacheAlightenMirrorV0014[idx].ShowGLTF5_15m == showGLTF5_15m && cacheAlightenMirrorV0014[idx].EnableGLTF6_10m == enableGLTF6_10m && cacheAlightenMirrorV0014[idx].ShowGLTF6_10m == showGLTF6_10m && cacheAlightenMirrorV0014[idx].EnableGLTF7_5m == enableGLTF7_5m && cacheAlightenMirrorV0014[idx].ShowGLTF7_5m == showGLTF7_5m && cacheAlightenMirrorV0014[idx].ColorTF1 == colorTF1 && cacheAlightenMirrorV0014[idx].ColorTF2 == colorTF2 && cacheAlightenMirrorV0014[idx].ColorTF3 == colorTF3 && cacheAlightenMirrorV0014[idx].ColorTF4 == colorTF4 && cacheAlightenMirrorV0014[idx].ColorTF5 == colorTF5 && cacheAlightenMirrorV0014[idx].ColorTF6 == colorTF6 && cacheAlightenMirrorV0014[idx].ColorTF7 == colorTF7 && cacheAlightenMirrorV0014[idx].LevelWidth == levelWidth && cacheAlightenMirrorV0014[idx].LevelDashStyleA == levelDashStyleA && cacheAlightenMirrorV0014[idx].LevelDashStyleB == levelDashStyleB && cacheAlightenMirrorV0014[idx].LevelDashStyleC == levelDashStyleC && cacheAlightenMirrorV0014[idx].LevelDashStyleE == levelDashStyleE && cacheAlightenMirrorV0014[idx].LevelWidthGL == levelWidthGL && cacheAlightenMirrorV0014[idx].LevelDashStyleTSU1 == levelDashStyleTSU1 && cacheAlightenMirrorV0014[idx].LevelDashStyleTSU2 == levelDashStyleTSU2 && cacheAlightenMirrorV0014[idx].LevelDashStyleTSD1 == levelDashStyleTSD1 && cacheAlightenMirrorV0014[idx].LevelDashStyleTSD2 == levelDashStyleTSD2 && cacheAlightenMirrorV0014[idx].LabelFontSize == labelFontSize && cacheAlightenMirrorV0014[idx].LabelOffsetTicks == labelOffsetTicks && cacheAlightenMirrorV0014[idx].BiasTimeframe == biasTimeframe && cacheAlightenMirrorV0014[idx].EnableHTFVP1_Daily == enableHTFVP1_Daily && cacheAlightenMirrorV0014[idx].ShowHTFVP1_Daily == showHTFVP1_Daily && cacheAlightenMirrorV0014[idx].ShowDevVP1_Daily == showDevVP1_Daily && cacheAlightenMirrorV0014[idx].EnableHTFVP2_240m == enableHTFVP2_240m && cacheAlightenMirrorV0014[idx].ShowHTFVP2_240m == showHTFVP2_240m && cacheAlightenMirrorV0014[idx].ShowDevVP2_240m == showDevVP2_240m && cacheAlightenMirrorV0014[idx].EnableHTFVP3_60m == enableHTFVP3_60m && cacheAlightenMirrorV0014[idx].ShowHTFVP3_60m == showHTFVP3_60m && cacheAlightenMirrorV0014[idx].ShowDevVP3_60m == showDevVP3_60m && cacheAlightenMirrorV0014[idx].EnableHTFVP4_30m == enableHTFVP4_30m && cacheAlightenMirrorV0014[idx].ShowHTFVP4_30m == showHTFVP4_30m && cacheAlightenMirrorV0014[idx].ShowDevVP4_30m == showDevVP4_30m && cacheAlightenMirrorV0014[idx].EnableHTFVP5_15m == enableHTFVP5_15m && cacheAlightenMirrorV0014[idx].ShowHTFVP5_15m == showHTFVP5_15m && cacheAlightenMirrorV0014[idx].ShowDevVP5_15m == showDevVP5_15m && cacheAlightenMirrorV0014[idx].EnableHTFVP6_10m == enableHTFVP6_10m && cacheAlightenMirrorV0014[idx].ShowHTFVP6_10m == showHTFVP6_10m && cacheAlightenMirrorV0014[idx].ShowDevVP6_10m == showDevVP6_10m && cacheAlightenMirrorV0014[idx].EnableHTFVP7_5m == enableHTFVP7_5m && cacheAlightenMirrorV0014[idx].ShowHTFVP7_5m == showHTFVP7_5m && cacheAlightenMirrorV0014[idx].ShowDevVP7_5m == showDevVP7_5m && cacheAlightenMirrorV0014[idx].EnableHTFVPFilter == enableHTFVPFilter && cacheAlightenMirrorV0014[idx].HTFVPValueAreaPercentage == hTFVPValueAreaPercentage && cacheAlightenMirrorV0014[idx].HTFVPBarsToProcess == hTFVPBarsToProcess && cacheAlightenMirrorV0014[idx].HTFVPVAHColor == hTFVPVAHColor && cacheAlightenMirrorV0014[idx].HTFVPVALColor == hTFVPVALColor && cacheAlightenMirrorV0014[idx].HTFVPPOCColor == hTFVPPOCColor && cacheAlightenMirrorV0014[idx].HTFVPVAHWidth == hTFVPVAHWidth && cacheAlightenMirrorV0014[idx].HTFVPVAHStyle == hTFVPVAHStyle && cacheAlightenMirrorV0014[idx].HTFVPVALWidth == hTFVPVALWidth && cacheAlightenMirrorV0014[idx].HTFVPVALStyle == hTFVPVALStyle && cacheAlightenMirrorV0014[idx].HTFVPPOCWidth == hTFVPPOCWidth && cacheAlightenMirrorV0014[idx].HTFVPPOCStyle == hTFVPPOCStyle && cacheAlightenMirrorV0014[idx].HTFVPDevVAHWidth == hTFVPDevVAHWidth && cacheAlightenMirrorV0014[idx].HTFVPDevVAHStyle == hTFVPDevVAHStyle && cacheAlightenMirrorV0014[idx].HTFVPDevVALWidth == hTFVPDevVALWidth && cacheAlightenMirrorV0014[idx].HTFVPDevVALStyle == hTFVPDevVALStyle && cacheAlightenMirrorV0014[idx].HTFVPDevPOCWidth == hTFVPDevPOCWidth && cacheAlightenMirrorV0014[idx].HTFVPDevPOCStyle == hTFVPDevPOCStyle && cacheAlightenMirrorV0014[idx].EqualsInput(input))
						return cacheAlightenMirrorV0014[idx];
			return CacheIndicator<AlightenMirrorV0014>(new AlightenMirrorV0014(){ EnablePatternA = enablePatternA, EnablePatternB = enablePatternB, EnablePatternC = enablePatternC, EnablePatternE = enablePatternE, SrcBarsToProcess = srcBarsToProcess, MirrorLookbackBars = mirrorLookbackBars, EnableInvalidatedCleanup = enableInvalidatedCleanup, EnableTF1_Daily = enableTF1_Daily, ShowTF1_Daily = showTF1_Daily, EnableTF2_240m = enableTF2_240m, ShowTF2_240m = showTF2_240m, EnableTF3_60m = enableTF3_60m, ShowTF3_60m = showTF3_60m, EnableTF4_30m = enableTF4_30m, ShowTF4_30m = showTF4_30m, EnableTF5_15m = enableTF5_15m, ShowTF5_15m = showTF5_15m, EnableTF6_10m = enableTF6_10m, ShowTF6_10m = showTF6_10m, EnableTF7_5m = enableTF7_5m, ShowTF7_5m = showTF7_5m, EnableGLTF1_Daily = enableGLTF1_Daily, ShowGLTF1_Daily = showGLTF1_Daily, EnableGLTF2_240m = enableGLTF2_240m, ShowGLTF2_240m = showGLTF2_240m, EnableGLTF3_60m = enableGLTF3_60m, ShowGLTF3_60m = showGLTF3_60m, EnableGLTF4_30m = enableGLTF4_30m, ShowGLTF4_30m = showGLTF4_30m, EnableGLTF5_15m = enableGLTF5_15m, ShowGLTF5_15m = showGLTF5_15m, EnableGLTF6_10m = enableGLTF6_10m, ShowGLTF6_10m = showGLTF6_10m, EnableGLTF7_5m = enableGLTF7_5m, ShowGLTF7_5m = showGLTF7_5m, ColorTF1 = colorTF1, ColorTF2 = colorTF2, ColorTF3 = colorTF3, ColorTF4 = colorTF4, ColorTF5 = colorTF5, ColorTF6 = colorTF6, ColorTF7 = colorTF7, LevelWidth = levelWidth, LevelDashStyleA = levelDashStyleA, LevelDashStyleB = levelDashStyleB, LevelDashStyleC = levelDashStyleC, LevelDashStyleE = levelDashStyleE, LevelWidthGL = levelWidthGL, LevelDashStyleTSU1 = levelDashStyleTSU1, LevelDashStyleTSU2 = levelDashStyleTSU2, LevelDashStyleTSD1 = levelDashStyleTSD1, LevelDashStyleTSD2 = levelDashStyleTSD2, LabelFontSize = labelFontSize, LabelOffsetTicks = labelOffsetTicks, BiasTimeframe = biasTimeframe, EnableHTFVP1_Daily = enableHTFVP1_Daily, ShowHTFVP1_Daily = showHTFVP1_Daily, ShowDevVP1_Daily = showDevVP1_Daily, EnableHTFVP2_240m = enableHTFVP2_240m, ShowHTFVP2_240m = showHTFVP2_240m, ShowDevVP2_240m = showDevVP2_240m, EnableHTFVP3_60m = enableHTFVP3_60m, ShowHTFVP3_60m = showHTFVP3_60m, ShowDevVP3_60m = showDevVP3_60m, EnableHTFVP4_30m = enableHTFVP4_30m, ShowHTFVP4_30m = showHTFVP4_30m, ShowDevVP4_30m = showDevVP4_30m, EnableHTFVP5_15m = enableHTFVP5_15m, ShowHTFVP5_15m = showHTFVP5_15m, ShowDevVP5_15m = showDevVP5_15m, EnableHTFVP6_10m = enableHTFVP6_10m, ShowHTFVP6_10m = showHTFVP6_10m, ShowDevVP6_10m = showDevVP6_10m, EnableHTFVP7_5m = enableHTFVP7_5m, ShowHTFVP7_5m = showHTFVP7_5m, ShowDevVP7_5m = showDevVP7_5m, EnableHTFVPFilter = enableHTFVPFilter, HTFVPValueAreaPercentage = hTFVPValueAreaPercentage, HTFVPBarsToProcess = hTFVPBarsToProcess, HTFVPVAHColor = hTFVPVAHColor, HTFVPVALColor = hTFVPVALColor, HTFVPPOCColor = hTFVPPOCColor, HTFVPVAHWidth = hTFVPVAHWidth, HTFVPVAHStyle = hTFVPVAHStyle, HTFVPVALWidth = hTFVPVALWidth, HTFVPVALStyle = hTFVPVALStyle, HTFVPPOCWidth = hTFVPPOCWidth, HTFVPPOCStyle = hTFVPPOCStyle, HTFVPDevVAHWidth = hTFVPDevVAHWidth, HTFVPDevVAHStyle = hTFVPDevVAHStyle, HTFVPDevVALWidth = hTFVPDevVALWidth, HTFVPDevVALStyle = hTFVPDevVALStyle, HTFVPDevPOCWidth = hTFVPDevPOCWidth, HTFVPDevPOCStyle = hTFVPDevPOCStyle }, input, ref cacheAlightenMirrorV0014);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenMirrorV0014 AlightenMirrorV0014(bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool showTF1_Daily, bool enableTF2_240m, bool showTF2_240m, bool enableTF3_60m, bool showTF3_60m, bool enableTF4_30m, bool showTF4_30m, bool enableTF5_15m, bool showTF5_15m, bool enableTF6_10m, bool showTF6_10m, bool enableTF7_5m, bool showTF7_5m, bool enableGLTF1_Daily, bool showGLTF1_Daily, bool enableGLTF2_240m, bool showGLTF2_240m, bool enableGLTF3_60m, bool showGLTF3_60m, bool enableGLTF4_30m, bool showGLTF4_30m, bool enableGLTF5_15m, bool showGLTF5_15m, bool enableGLTF6_10m, bool showGLTF6_10m, bool enableGLTF7_5m, bool showGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, HTFVPBiasTimeframe biasTimeframe, bool enableHTFVP1_Daily, bool showHTFVP1_Daily, bool showDevVP1_Daily, bool enableHTFVP2_240m, bool showHTFVP2_240m, bool showDevVP2_240m, bool enableHTFVP3_60m, bool showHTFVP3_60m, bool showDevVP3_60m, bool enableHTFVP4_30m, bool showHTFVP4_30m, bool showDevVP4_30m, bool enableHTFVP5_15m, bool showHTFVP5_15m, bool showDevVP5_15m, bool enableHTFVP6_10m, bool showHTFVP6_10m, bool showDevVP6_10m, bool enableHTFVP7_5m, bool showHTFVP7_5m, bool showDevVP7_5m, bool enableHTFVPFilter, double hTFVPValueAreaPercentage, int hTFVPBarsToProcess, Brush hTFVPVAHColor, Brush hTFVPVALColor, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle, int hTFVPDevVAHWidth, DashStyleHelper hTFVPDevVAHStyle, int hTFVPDevVALWidth, DashStyleHelper hTFVPDevVALStyle, int hTFVPDevPOCWidth, DashStyleHelper hTFVPDevPOCStyle)
		{
			return indicator.AlightenMirrorV0014(Input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, showTF1_Daily, enableTF2_240m, showTF2_240m, enableTF3_60m, showTF3_60m, enableTF4_30m, showTF4_30m, enableTF5_15m, showTF5_15m, enableTF6_10m, showTF6_10m, enableTF7_5m, showTF7_5m, enableGLTF1_Daily, showGLTF1_Daily, enableGLTF2_240m, showGLTF2_240m, enableGLTF3_60m, showGLTF3_60m, enableGLTF4_30m, showGLTF4_30m, enableGLTF5_15m, showGLTF5_15m, enableGLTF6_10m, showGLTF6_10m, enableGLTF7_5m, showGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, biasTimeframe, enableHTFVP1_Daily, showHTFVP1_Daily, showDevVP1_Daily, enableHTFVP2_240m, showHTFVP2_240m, showDevVP2_240m, enableHTFVP3_60m, showHTFVP3_60m, showDevVP3_60m, enableHTFVP4_30m, showHTFVP4_30m, showDevVP4_30m, enableHTFVP5_15m, showHTFVP5_15m, showDevVP5_15m, enableHTFVP6_10m, showHTFVP6_10m, showDevVP6_10m, enableHTFVP7_5m, showHTFVP7_5m, showDevVP7_5m, enableHTFVPFilter, hTFVPValueAreaPercentage, hTFVPBarsToProcess, hTFVPVAHColor, hTFVPVALColor, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle, hTFVPDevVAHWidth, hTFVPDevVAHStyle, hTFVPDevVALWidth, hTFVPDevVALStyle, hTFVPDevPOCWidth, hTFVPDevPOCStyle);
		}

		public Indicators.AlightenMirrorV0014 AlightenMirrorV0014(ISeries<double> input , bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool showTF1_Daily, bool enableTF2_240m, bool showTF2_240m, bool enableTF3_60m, bool showTF3_60m, bool enableTF4_30m, bool showTF4_30m, bool enableTF5_15m, bool showTF5_15m, bool enableTF6_10m, bool showTF6_10m, bool enableTF7_5m, bool showTF7_5m, bool enableGLTF1_Daily, bool showGLTF1_Daily, bool enableGLTF2_240m, bool showGLTF2_240m, bool enableGLTF3_60m, bool showGLTF3_60m, bool enableGLTF4_30m, bool showGLTF4_30m, bool enableGLTF5_15m, bool showGLTF5_15m, bool enableGLTF6_10m, bool showGLTF6_10m, bool enableGLTF7_5m, bool showGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, HTFVPBiasTimeframe biasTimeframe, bool enableHTFVP1_Daily, bool showHTFVP1_Daily, bool showDevVP1_Daily, bool enableHTFVP2_240m, bool showHTFVP2_240m, bool showDevVP2_240m, bool enableHTFVP3_60m, bool showHTFVP3_60m, bool showDevVP3_60m, bool enableHTFVP4_30m, bool showHTFVP4_30m, bool showDevVP4_30m, bool enableHTFVP5_15m, bool showHTFVP5_15m, bool showDevVP5_15m, bool enableHTFVP6_10m, bool showHTFVP6_10m, bool showDevVP6_10m, bool enableHTFVP7_5m, bool showHTFVP7_5m, bool showDevVP7_5m, bool enableHTFVPFilter, double hTFVPValueAreaPercentage, int hTFVPBarsToProcess, Brush hTFVPVAHColor, Brush hTFVPVALColor, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle, int hTFVPDevVAHWidth, DashStyleHelper hTFVPDevVAHStyle, int hTFVPDevVALWidth, DashStyleHelper hTFVPDevVALStyle, int hTFVPDevPOCWidth, DashStyleHelper hTFVPDevPOCStyle)
		{
			return indicator.AlightenMirrorV0014(input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, showTF1_Daily, enableTF2_240m, showTF2_240m, enableTF3_60m, showTF3_60m, enableTF4_30m, showTF4_30m, enableTF5_15m, showTF5_15m, enableTF6_10m, showTF6_10m, enableTF7_5m, showTF7_5m, enableGLTF1_Daily, showGLTF1_Daily, enableGLTF2_240m, showGLTF2_240m, enableGLTF3_60m, showGLTF3_60m, enableGLTF4_30m, showGLTF4_30m, enableGLTF5_15m, showGLTF5_15m, enableGLTF6_10m, showGLTF6_10m, enableGLTF7_5m, showGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, biasTimeframe, enableHTFVP1_Daily, showHTFVP1_Daily, showDevVP1_Daily, enableHTFVP2_240m, showHTFVP2_240m, showDevVP2_240m, enableHTFVP3_60m, showHTFVP3_60m, showDevVP3_60m, enableHTFVP4_30m, showHTFVP4_30m, showDevVP4_30m, enableHTFVP5_15m, showHTFVP5_15m, showDevVP5_15m, enableHTFVP6_10m, showHTFVP6_10m, showDevVP6_10m, enableHTFVP7_5m, showHTFVP7_5m, showDevVP7_5m, enableHTFVPFilter, hTFVPValueAreaPercentage, hTFVPBarsToProcess, hTFVPVAHColor, hTFVPVALColor, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle, hTFVPDevVAHWidth, hTFVPDevVAHStyle, hTFVPDevVALWidth, hTFVPDevVALStyle, hTFVPDevPOCWidth, hTFVPDevPOCStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenMirrorV0014 AlightenMirrorV0014(bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool showTF1_Daily, bool enableTF2_240m, bool showTF2_240m, bool enableTF3_60m, bool showTF3_60m, bool enableTF4_30m, bool showTF4_30m, bool enableTF5_15m, bool showTF5_15m, bool enableTF6_10m, bool showTF6_10m, bool enableTF7_5m, bool showTF7_5m, bool enableGLTF1_Daily, bool showGLTF1_Daily, bool enableGLTF2_240m, bool showGLTF2_240m, bool enableGLTF3_60m, bool showGLTF3_60m, bool enableGLTF4_30m, bool showGLTF4_30m, bool enableGLTF5_15m, bool showGLTF5_15m, bool enableGLTF6_10m, bool showGLTF6_10m, bool enableGLTF7_5m, bool showGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, HTFVPBiasTimeframe biasTimeframe, bool enableHTFVP1_Daily, bool showHTFVP1_Daily, bool showDevVP1_Daily, bool enableHTFVP2_240m, bool showHTFVP2_240m, bool showDevVP2_240m, bool enableHTFVP3_60m, bool showHTFVP3_60m, bool showDevVP3_60m, bool enableHTFVP4_30m, bool showHTFVP4_30m, bool showDevVP4_30m, bool enableHTFVP5_15m, bool showHTFVP5_15m, bool showDevVP5_15m, bool enableHTFVP6_10m, bool showHTFVP6_10m, bool showDevVP6_10m, bool enableHTFVP7_5m, bool showHTFVP7_5m, bool showDevVP7_5m, bool enableHTFVPFilter, double hTFVPValueAreaPercentage, int hTFVPBarsToProcess, Brush hTFVPVAHColor, Brush hTFVPVALColor, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle, int hTFVPDevVAHWidth, DashStyleHelper hTFVPDevVAHStyle, int hTFVPDevVALWidth, DashStyleHelper hTFVPDevVALStyle, int hTFVPDevPOCWidth, DashStyleHelper hTFVPDevPOCStyle)
		{
			return indicator.AlightenMirrorV0014(Input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, showTF1_Daily, enableTF2_240m, showTF2_240m, enableTF3_60m, showTF3_60m, enableTF4_30m, showTF4_30m, enableTF5_15m, showTF5_15m, enableTF6_10m, showTF6_10m, enableTF7_5m, showTF7_5m, enableGLTF1_Daily, showGLTF1_Daily, enableGLTF2_240m, showGLTF2_240m, enableGLTF3_60m, showGLTF3_60m, enableGLTF4_30m, showGLTF4_30m, enableGLTF5_15m, showGLTF5_15m, enableGLTF6_10m, showGLTF6_10m, enableGLTF7_5m, showGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, biasTimeframe, enableHTFVP1_Daily, showHTFVP1_Daily, showDevVP1_Daily, enableHTFVP2_240m, showHTFVP2_240m, showDevVP2_240m, enableHTFVP3_60m, showHTFVP3_60m, showDevVP3_60m, enableHTFVP4_30m, showHTFVP4_30m, showDevVP4_30m, enableHTFVP5_15m, showHTFVP5_15m, showDevVP5_15m, enableHTFVP6_10m, showHTFVP6_10m, showDevVP6_10m, enableHTFVP7_5m, showHTFVP7_5m, showDevVP7_5m, enableHTFVPFilter, hTFVPValueAreaPercentage, hTFVPBarsToProcess, hTFVPVAHColor, hTFVPVALColor, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle, hTFVPDevVAHWidth, hTFVPDevVAHStyle, hTFVPDevVALWidth, hTFVPDevVALStyle, hTFVPDevPOCWidth, hTFVPDevPOCStyle);
		}

		public Indicators.AlightenMirrorV0014 AlightenMirrorV0014(ISeries<double> input , bool enablePatternA, bool enablePatternB, bool enablePatternC, bool enablePatternE, int srcBarsToProcess, int mirrorLookbackBars, bool enableInvalidatedCleanup, bool enableTF1_Daily, bool showTF1_Daily, bool enableTF2_240m, bool showTF2_240m, bool enableTF3_60m, bool showTF3_60m, bool enableTF4_30m, bool showTF4_30m, bool enableTF5_15m, bool showTF5_15m, bool enableTF6_10m, bool showTF6_10m, bool enableTF7_5m, bool showTF7_5m, bool enableGLTF1_Daily, bool showGLTF1_Daily, bool enableGLTF2_240m, bool showGLTF2_240m, bool enableGLTF3_60m, bool showGLTF3_60m, bool enableGLTF4_30m, bool showGLTF4_30m, bool enableGLTF5_15m, bool showGLTF5_15m, bool enableGLTF6_10m, bool showGLTF6_10m, bool enableGLTF7_5m, bool showGLTF7_5m, Brush colorTF1, Brush colorTF2, Brush colorTF3, Brush colorTF4, Brush colorTF5, Brush colorTF6, Brush colorTF7, int levelWidth, DashStyleHelper levelDashStyleA, DashStyleHelper levelDashStyleB, DashStyleHelper levelDashStyleC, DashStyleHelper levelDashStyleE, int levelWidthGL, DashStyleHelper levelDashStyleTSU1, DashStyleHelper levelDashStyleTSU2, DashStyleHelper levelDashStyleTSD1, DashStyleHelper levelDashStyleTSD2, int labelFontSize, int labelOffsetTicks, HTFVPBiasTimeframe biasTimeframe, bool enableHTFVP1_Daily, bool showHTFVP1_Daily, bool showDevVP1_Daily, bool enableHTFVP2_240m, bool showHTFVP2_240m, bool showDevVP2_240m, bool enableHTFVP3_60m, bool showHTFVP3_60m, bool showDevVP3_60m, bool enableHTFVP4_30m, bool showHTFVP4_30m, bool showDevVP4_30m, bool enableHTFVP5_15m, bool showHTFVP5_15m, bool showDevVP5_15m, bool enableHTFVP6_10m, bool showHTFVP6_10m, bool showDevVP6_10m, bool enableHTFVP7_5m, bool showHTFVP7_5m, bool showDevVP7_5m, bool enableHTFVPFilter, double hTFVPValueAreaPercentage, int hTFVPBarsToProcess, Brush hTFVPVAHColor, Brush hTFVPVALColor, Brush hTFVPPOCColor, int hTFVPVAHWidth, DashStyleHelper hTFVPVAHStyle, int hTFVPVALWidth, DashStyleHelper hTFVPVALStyle, int hTFVPPOCWidth, DashStyleHelper hTFVPPOCStyle, int hTFVPDevVAHWidth, DashStyleHelper hTFVPDevVAHStyle, int hTFVPDevVALWidth, DashStyleHelper hTFVPDevVALStyle, int hTFVPDevPOCWidth, DashStyleHelper hTFVPDevPOCStyle)
		{
			return indicator.AlightenMirrorV0014(input, enablePatternA, enablePatternB, enablePatternC, enablePatternE, srcBarsToProcess, mirrorLookbackBars, enableInvalidatedCleanup, enableTF1_Daily, showTF1_Daily, enableTF2_240m, showTF2_240m, enableTF3_60m, showTF3_60m, enableTF4_30m, showTF4_30m, enableTF5_15m, showTF5_15m, enableTF6_10m, showTF6_10m, enableTF7_5m, showTF7_5m, enableGLTF1_Daily, showGLTF1_Daily, enableGLTF2_240m, showGLTF2_240m, enableGLTF3_60m, showGLTF3_60m, enableGLTF4_30m, showGLTF4_30m, enableGLTF5_15m, showGLTF5_15m, enableGLTF6_10m, showGLTF6_10m, enableGLTF7_5m, showGLTF7_5m, colorTF1, colorTF2, colorTF3, colorTF4, colorTF5, colorTF6, colorTF7, levelWidth, levelDashStyleA, levelDashStyleB, levelDashStyleC, levelDashStyleE, levelWidthGL, levelDashStyleTSU1, levelDashStyleTSU2, levelDashStyleTSD1, levelDashStyleTSD2, labelFontSize, labelOffsetTicks, biasTimeframe, enableHTFVP1_Daily, showHTFVP1_Daily, showDevVP1_Daily, enableHTFVP2_240m, showHTFVP2_240m, showDevVP2_240m, enableHTFVP3_60m, showHTFVP3_60m, showDevVP3_60m, enableHTFVP4_30m, showHTFVP4_30m, showDevVP4_30m, enableHTFVP5_15m, showHTFVP5_15m, showDevVP5_15m, enableHTFVP6_10m, showHTFVP6_10m, showDevVP6_10m, enableHTFVP7_5m, showHTFVP7_5m, showDevVP7_5m, enableHTFVPFilter, hTFVPValueAreaPercentage, hTFVPBarsToProcess, hTFVPVAHColor, hTFVPVALColor, hTFVPPOCColor, hTFVPVAHWidth, hTFVPVAHStyle, hTFVPVALWidth, hTFVPVALStyle, hTFVPPOCWidth, hTFVPPOCStyle, hTFVPDevVAHWidth, hTFVPDevVAHStyle, hTFVPDevVALWidth, hTFVPDevVALStyle, hTFVPDevPOCWidth, hTFVPDevPOCStyle);
		}
	}
}

#endregion

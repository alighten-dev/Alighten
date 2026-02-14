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

//This code is subject to the terms of the Mozilla Public License 2.0 at https://mozilla.org/MPL/2.0/
//Created by RedTail Indicators - @_hawkeye_13
//Version: v2.3.5 - Added Onvernight High and Low and custom sessions for Dual Mode
public enum VolumeTypeEnum
{
    Standard,
    Bullish,
    Bearish,
    Both
}

public enum ProfileModeEnum
{
    Session,
    VisibleRange,
    Weeks,
    Months,
    Composite
}

public enum CompositeDateRangeType
{
    DaysBack,
    WeeksBack,
    MonthsBack,
    CustomDateRange
}

public enum ProfileAlignment
{
    Right,
    Left,
    Anchored
}

public enum LVNModeEnum
{
    Disabled,
    Enabled
}

public enum SessionProfileStyleEnum
{
    Filled,
    Outline
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class RedTailVolumeProfile_V2 : Indicator
    {
        #region Variables
		
		// Public properties for other indicators to access current session levels
public bool IsProfileCalculated
{
    get { return volumes != null && volumes.Count > 0 && maxIndexForRender >= 0; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> CurrentPOCPlot
{
    get { return Values[0]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> CurrentVAHPlot
{
    get { return Values[1]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> CurrentVALPlot
{
    get { return Values[2]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> PrevDayPOCPlot
{
    get { return Values[3]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> PrevDayVAHPlot
{
    get { return Values[4]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> PrevDayVALPlot
{
    get { return Values[5]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> PrevDayHighPlot
{
    get { return Values[6]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> PrevDayLowPlot
{
    get { return Values[7]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> OvernightPOCPlot
{
    get { return Values[8]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> OvernightVAHPlot
{
    get { return Values[9]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> OvernightVALPlot
{
    get { return Values[10]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> OvernightHighPlot
{
    get { return Values[11]; }
}

[Browsable(false)]
[XmlIgnore]
public Series<double> OvernightLowPlot
{
    get { return Values[12]; }
}

// Weekly Naked Levels
public List<double> GetWeeklyNakedPOCLevels()
{
    if (!DisplayWeeklyNakedLevels || historicalWeeklyLevels == null)
        return new List<double>();
        
    return historicalWeeklyLevels.Values
        .Where(l => l.POCNaked)
        .OrderByDescending(l => l.WeekStartDate)
        .Take(MaxWeeklyNakedLevelsToDisplay)
        .Select(l => l.POC)
        .ToList();
}

public List<double> GetWeeklyNakedVAHLevels()
{
    if (!DisplayWeeklyNakedLevels || historicalWeeklyLevels == null)
        return new List<double>();
        
    return historicalWeeklyLevels.Values
        .Where(l => l.VAHNaked)
        .OrderByDescending(l => l.WeekStartDate)
        .Take(MaxWeeklyNakedLevelsToDisplay)
        .Select(l => l.VAH)
        .ToList();
}

public List<double> GetWeeklyNakedVALLevels()
{
    if (!DisplayWeeklyNakedLevels || historicalWeeklyLevels == null)
        return new List<double>();
        
    return historicalWeeklyLevels.Values
        .Where(l => l.VALNaked)
        .OrderByDescending(l => l.WeekStartDate)
        .Take(MaxWeeklyNakedLevelsToDisplay)
        .Select(l => l.VAL)
        .ToList();
}
        private List<double> volumes;
        private List<bool> volumePolarities; // Track if each price level is bullish/bearish dominant
        private double highestPrice;
        private double lowestPrice;
        private double priceInterval;
        
        // Caching variables for performance
        private int lastCalculatedBar = -1;
        private DateTime lastSessionStart = DateTime.MinValue;
        private bool needsRecalculation = true;
        
        // Visible range tracking
        private int lastVisibleFromIndex = -1;
        private int lastVisibleToIndex = -1;
        
        // Historical level tracking for previous day and naked levels
        private class DayLevels
    {
        public DateTime Date { get; set; }
        public double VAH { get; set; }
        public double VAL { get; set; }
        public double POC { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public bool VAHNaked { get; set; }
        public bool VALNaked { get; set; }
        public bool POCNaked { get; set; }
        public bool POCFilled { get; set; }
        public bool VAHFilled { get; set; }
        public bool VALFilled { get; set; }
    
        // Touch tracking
        public int POCTouchCount { get; set; }
        public int VAHTouchCount { get; set; }
        public int VALTouchCount { get; set; }
        public DateTime POCLastTouchSession { get; set; }
        public DateTime VAHLastTouchSession { get; set; }
        public DateTime VALLastTouchSession { get; set; }
     }   
        
        private Dictionary<DateTime, DayLevels> historicalLevels = new Dictionary<DateTime, DayLevels>();
        private DayLevels previousDayLevels = null;
        private DateTime currentProcessingDate = DateTime.MinValue;

        // Weekly level tracking for naked weekly levels
        private class WeekLevels
     {
        public DateTime WeekStartDate { get; set; }
        public double VAH { get; set; }
        public double VAL { get; set; }
        public double POC { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public bool VAHNaked { get; set; }
        public bool VALNaked { get; set; }
        public bool POCNaked { get; set; }
        public bool POCFilled { get; set; }
        public bool VAHFilled { get; set; }
        public bool VALFilled { get; set; }
    
        // Touch tracking
        public int POCTouchCount { get; set; }
        public int VAHTouchCount { get; set; }
        public int VALTouchCount { get; set; }
        public DateTime POCLastTouchSession { get; set; }
        public DateTime VAHLastTouchSession { get; set; }
        public DateTime VALLastTouchSession { get; set; }
     }  

        private Dictionary<DateTime, WeekLevels> historicalWeeklyLevels = new Dictionary<DateTime, WeekLevels>();
        private WeekLevels previousWeekLevels = null;
        private DateTime currentProcessingWeek = DateTime.MinValue;

        // Session tracking
        private SessionIterator sessionIterator;
        private DateTime currentSessionStart;
        private DateTime currentSessionEnd;
        
        // Custom session time tracking
        private DateTime customSessionStart;
        private DateTime customSessionEnd;
        
        // Rendering state (cached for OnRender)
        private double maxVolumeForRender = 0;
        private int maxIndexForRender = -1;
        private int vaUpForRender = -1;
        private int vaDownForRender = -1;
        private bool isCalculating = false;  // Prevent recursion
        
        // Session date tracking
        private DateTime lastKnownSessionDate = DateTime.MinValue;
        
        // Previous Day High/Low tracking - NEW APPROACH
        private double prevDayHigh = 0;
        private double prevDayLow = 0;
        private DateTime prevDayDate = DateTime.MinValue;
	 
	    // Overnight Session Levels (6 PM - 8:30 AM)
        private double overnightPOC = 0;
        private double overnightVAH = 0;
        private double overnightVAL = 0;
        private double overnightHigh = 0;
        private double overnightLow = 0;
        private DateTime overnightSessionDate = DateTime.MinValue;
        private bool overnightLevelsCalculated = false;
        
        // Current session tracking for PDH/PDL
        private double currentSessionHigh = double.MinValue;
        private double currentSessionLow = double.MaxValue;
        private DateTime currentSessionDateForHL = DateTime.MinValue;
        
        // Session profile for anchored mode
        private class SessionProfile
        {
            public DateTime SessionDate { get; set; }
            public DateTime SessionStart { get; set; }
            public DateTime SessionEnd { get; set; }
            public int StartBarIndex { get; set; }
            public int EndBarIndex { get; set; }
            public List<double> Volumes { get; set; }
            public double HighestPrice { get; set; }
            public double LowestPrice { get; set; }
            public double PriceInterval { get; set; }
            public int POCIndex { get; set; }
            public int VAUpIndex { get; set; }
            public int VADownIndex { get; set; }
            public double MaxVolume { get; set; }
        }
        
        // Anchored profiles storage
        private List<SessionProfile> anchoredProfiles = new List<SessionProfile>();
        private DateTime lastAnchoredCalculation = DateTime.MinValue;
		// NEW: Naked levels tracking
        private const double PRICE_TOUCH_TOLERANCE = 0.25; // Ticks tolerance for "touching" a level
        
        // LVN Detection variables
        private ProfileData lvnProfile;
        private int lastLVNCalculationBar = -1;
        private bool lvnNeedsRecalculation = true;
        
        // Dual Profile Mode variables
        private List<double> weeklyVolumes;
        private DateTime customDailySessionStart;
        private DateTime customDailySessionEnd;
        private double weeklyHighestPrice;
        private double weeklyLowestPrice;
        private double weeklyPriceInterval;
        private int weeklyMaxIndex = -1;
        private int weeklyVAUp = -1;
        private int weeklyVADown = -1;
        private double weeklyMaxVolume = 0;
        
        private List<double> sessionVolumes;
        private double sessionHighestPrice;
        private double sessionLowestPrice;
        private double sessionPriceInterval;
        private int sessionMaxIndex = -1;
        private int sessionVAUp = -1;
        private int sessionVADown = -1;
        private double sessionMaxVolume = 0;
        
        private DateTime currentWeekStart;
        private DateTime currentWeekEnd;
        
        // Composite mode variables
        private DateTime compositeStartDate;
        private DateTime compositeEndDate;
        #endregion
        
        #region LVN ProfileData Class
        public class ProfileData
        {
            public List<double> BarHighs { get; set; }
            public List<double> BarLows { get; set; }
            public List<double> BarVolumes { get; set; }
            public List<bool> BarPolarities { get; set; }
            public double[] TotalVolume { get; set; }
            public List<VolumeNode> VolumeNodes { get; set; }
            public double ProfileHigh { get; set; }
            public double ProfileLow { get; set; }
            public double PriceStep { get; set; }
            
            public ProfileData(int numRows)
            {
                BarHighs = new List<double>(5000);
                BarLows = new List<double>(5000);
                BarVolumes = new List<double>(5000);
                BarPolarities = new List<bool>(5000);
                TotalVolume = new double[numRows];
                VolumeNodes = new List<VolumeNode>(numRows);
            }
        }
        #endregion
        
        #region LVN Volume Node Class
        public class VolumeNode
        {
            public double PriceLevel { get; set; }
            public double TotalVolume { get; set; }
            public bool IsTrough { get; set; }
            public int RowIndex { get; set; }
        }
        #endregion
		
		#region DOM Visualization Variables
        
        // DOM Order tracking
        private class OrderInfo
        {
            public double Price { get; set; }
            public long Volume { get; set; }
            public DateTime LastUpdate { get; set; }
        }
        
        private readonly object orderLock = new object();
        private Dictionary<double, OrderInfo> renderBidOrders = new Dictionary<double, OrderInfo>();
        private Dictionary<double, OrderInfo> renderAskOrders = new Dictionary<double, OrderInfo>();
        private long maxDOMVolume = 0;
        private long outlierThreshold = 100;
        private double currentBidPrice = double.MinValue;
        private double currentAskPrice = double.MaxValue;
        private Queue<long> recentVolumes = new Queue<long>();
        private const int VOLUME_HISTORY_SIZE = 100;
        private const double VOLUME_STDDEV_MULTIPLIER = 1.0;
        
        // DOM Caching structures
        private Dictionary<float, SharpDX.DirectWrite.TextFormat> textFormatCache = new Dictionary<float, SharpDX.DirectWrite.TextFormat>();
        private Dictionary<double, OrderInfo> cachedBidOrders = new Dictionary<double, OrderInfo>();
        private Dictionary<double, OrderInfo> cachedAskOrders = new Dictionary<double, OrderInfo>();
        private float cachedDOMBarHeight = 0;
        private double cachedTickSize = 0;
        private DateTime lastMaxVolumeUpdate = DateTime.MinValue;
        private const int MAX_VOLUME_UPDATE_INTERVAL_MS = 500;
        private SharpDX.Direct2D1.SolidColorBrush cachedDxBrushBid = null;
        private SharpDX.Direct2D1.SolidColorBrush cachedDxBrushAsk = null;
        private SharpDX.Direct2D1.SolidColorBrush cachedDxBrushText = null;
        private SharpDX.Direct2D1.SolidColorBrush cachedDxBrushOutlier = null;
        private DateTime lastBrushUpdate = DateTime.MinValue;
        private const int BRUSH_UPDATE_INTERVAL_MS = 1000;
        
        // DOM Refresh throttling
        private bool needsDOMRefresh = false;
        private DateTime lastDOMRefresh = DateTime.MinValue;
        private const int MIN_REFRESH_INTERVAL_MS = 16; // ~60 FPS max
        
        // DOM Cache for dynamic threshold calculation
        private double cachedDynamicThreshold = 0;
        private DateTime lastThresholdUpdate = DateTime.MinValue;
        private const int THRESHOLD_UPDATE_INTERVAL_MS = 200;
        
        // DOM Cache for visible range calculations
        private double cachedDOMVisibleHigh = 0;
        private double cachedDOMVisibleLow = 0;
        private bool cachedDOMDataDirty = true;
        
        #endregion
        
        // Helper method to get the Sunday start date for a given bar's week
        private DateTime GetWeekStartForBar(DateTime barTime)
    {
        DateTime sessionDate;
        
        if (barTime.DayOfWeek == DayOfWeek.Sunday && barTime.Hour >= 18)
        {
            sessionDate = barTime.Date;
        }
        else if (barTime.DayOfWeek == DayOfWeek.Sunday)
        {
            sessionDate = barTime.Date.AddDays(-7);
        }
        else if (barTime.DayOfWeek >= DayOfWeek.Monday && barTime.DayOfWeek <= DayOfWeek.Friday)
        {
            sessionDate = barTime.Date;
            while (sessionDate.DayOfWeek != DayOfWeek.Sunday)
            {
                sessionDate = sessionDate.AddDays(-1);
            }
        }
        else
        {
            sessionDate = barTime.Date.AddDays(1);
        }
        
        return sessionDate;
    }
	
	   // Helper method to format dates based on user preference
       private string FormatDateLabel(DateTime date)
    {
        if (BritishDateFormat)
    {
        // DD/MM format for our friends across the pond
        return date.ToString("dd/MM");
        }
        else
    {
        // MM/DD format for freedom-loving Americans
        return date.ToString("MM/dd");
    }
}
    
        // Helper method to determine which session a bar belongs to
        private DateTime GetSessionDateForBar(DateTime barTime)
        {
            // Bars from 6:00 PM to 11:59 PM belong to NEXT day's session
            // Bars from 12:00 AM to 5:59 PM belong to TODAY's session
            if (barTime.Hour >= 18) // 6 PM or later
            {
                return barTime.Date.AddDays(1);
            }
            else
            {
                return barTime.Date;
            }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"RedTail Volume Profile v2.3.5 - Added Onvernight High and Low and custom sessions for Dual Mode";
                Name = "RedTail Volume Profile V2";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = false;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                IsAutoScale = false;

                // Add plots for all levels (Transparent - data only, no visual display)
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "CurrentPOC");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "CurrentVAH");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "CurrentVAL");
                
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "PrevDayPOC");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "PrevDayVAH");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "PrevDayVAL");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "PrevDayHigh");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "PrevDayLow");
                
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "OvernightPOC");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "OvernightVAH");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "OvernightVAL");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "OvernightHigh");
                AddPlot(new Stroke(Brushes.Transparent, DashStyleHelper.Solid, 1), PlotStyle.Line, "OvernightLow");

                // Profile Mode
                ProfileMode = ProfileModeEnum.Session;
                Alignment = ProfileAlignment.Right;
                
                // Lookback Period
                SessionsLookback = 5;
                WeeksLookback = 1;
                MonthsLookback = 1;
                
                // Composite Mode defaults
                CompositeRangeType = CompositeDateRangeType.DaysBack;
                CompositeDaysBack = 30;
                CompositeWeeksBack = 4;
                CompositeMonthsBack = 3;
                CompositeCustomStartDate = DateTime.Today.AddDays(-30);
                CompositeCustomEndDate = DateTime.Today;
				
				// Custom Session Time defaults (6:00 PM - 5:00 PM next day)
                // Use 24-hour format: 1800 = 6:00 PM, 0930 = 9:30 AM
                UseCustomSessionTimes = false;
                SessionStartTime = 1759;  // 5:59 PM (to capture 6:00 PM bar)
                SessionEndTime = 1700;     // 5:00 PM

                // Volume Bars
                NumberOfVolumeBars = 250;
                BarThickness = 2;
                ProfileWidth = 200;
                VolumeType = VolumeTypeEnum.Standard;
                BarColor = Brushes.Gray;
                BarOpacity = 50;
                BullishBarColor = Brushes.Green;
                BearishBarColor = Brushes.Red;

                // Point of Control
                DisplayPoC = true;
                PoCLineThickness = 2;
                PoCLineColor = Brushes.Red;
                PoCLineStyle = DashStyleHelper.Solid;
                PoCLineOpacity = 100;
                ExtendPoCLine = false;

                // Value Area
                DisplayValueArea = true;
                ValueAreaPercentage = 68;
                ValueAreaBarColor = Brushes.Blue;
                DisplayValueAreaLines = true;
                ValueAreaLinesColor = Brushes.Yellow;
                ValueAreaLinesThickness = 2;
                ValueAreaLineStyle = DashStyleHelper.Solid;
                ValueAreaLinesOpacity = 100;
                ExtendValueAreaLines = false;
                
                // Performance
                UpdateFrequency = 100;
                
                // Previous Day Levels
                DisplayPreviousDayPOC = false;
                PdPOCColor = Brushes.Orange;
                PdPOCLineStyle = DashStyleHelper.Solid;
                PdPOCThickness = 2;
                PdPOCOpacity = 80;
                
                DisplayPreviousDayVAH = false;
                PdVAHColor = Brushes.Cyan;
                PdVAHLineStyle = DashStyleHelper.Solid;
                PdVAHThickness = 2;
                PdVAHOpacity = 80;
                
                DisplayPreviousDayVAL = false;
                PdVALColor = Brushes.Magenta;
                PdVALLineStyle = DashStyleHelper.Solid;
                PdVALThickness = 2;
                PdVALOpacity = 80;
                
                // Previous Day High/Low (NEW)
                DisplayPreviousDayHigh = false;
                PdHighColor = Brushes.Lime;
                PdHighLineStyle = DashStyleHelper.Dash;
                PdHighThickness = 2;
                PdHighOpacity = 85;
                
                DisplayPreviousDayLow = false;
                PdLowColor = Brushes.Red;
                PdLowLineStyle = DashStyleHelper.Dash;
                PdLowThickness = 2;
                PdLowOpacity = 85;
				
				// Overnight Levels (NEW)
                DisplayOvernightPOC = false;
                OvernightPOCColor = Brushes.Yellow;
                OvernightPOCLineStyle = DashStyleHelper.Solid;
                OvernightPOCThickness = 2;
                OvernightPOCOpacity = 85;

                DisplayOvernightVAH = false;
                OvernightVAHColor = Brushes.LightBlue;
                OvernightVAHLineStyle = DashStyleHelper.Solid;
                OvernightVAHThickness = 2;
                OvernightVAHOpacity = 85;

                DisplayOvernightVAL = false;
                OvernightVALColor = Brushes.Pink;
                OvernightVALLineStyle = DashStyleHelper.Solid;
                OvernightVALThickness = 2;
                OvernightVALOpacity = 85;

                DisplayOvernightHigh = false;
                OvernightHighColor = Brushes.LightGreen;
                OvernightHighLineStyle = DashStyleHelper.Dash;
                OvernightHighThickness = 2;
                OvernightHighOpacity = 85;

                DisplayOvernightLow = false;
                OvernightLowColor = Brushes.LightCoral;
                OvernightLowLineStyle = DashStyleHelper.Dash;
                OvernightLowThickness = 2;
                OvernightLowOpacity = 85;

                // Overnight session times
                OvernightStartTime = 1800;  // 6:00 PM
                OvernightEndTime = 830;     // 8:30 AM
                
                // Session Naked Levels defaults (Group 07)
                NakedPOCColor = Brushes.Orange;
                NakedPOCLineStyle = DashStyleHelper.Solid;
                NakedPOCThickness = 2;
                NakedPOCOpacity = 80;
                
                NakedVAHColor = Brushes.Cyan;
                NakedVAHLineStyle = DashStyleHelper.Solid;
                NakedVAHThickness = 2;
                NakedVAHOpacity = 80;
                
                NakedVALColor = Brushes.Magenta;
                NakedVALLineStyle = DashStyleHelper.Solid;
                NakedVALThickness = 2;
                NakedVALOpacity = 80;
                
                // Weekly Naked Levels defaults (Group 08)
                WeeklyNakedPOCColor = Brushes.DarkOrange;
                WeeklyNakedPOCLineStyle = DashStyleHelper.Solid;
                WeeklyNakedPOCThickness = 2;
                WeeklyNakedPOCOpacity = 80;
                
                WeeklyNakedVAHColor = Brushes.DarkCyan;
                WeeklyNakedVAHLineStyle = DashStyleHelper.Solid;
                WeeklyNakedVAHThickness = 2;
                WeeklyNakedVAHOpacity = 80;
                
                WeeklyNakedVALColor = Brushes.DarkMagenta;
                WeeklyNakedVALLineStyle = DashStyleHelper.Solid;
                WeeklyNakedVALThickness = 2;
                WeeklyNakedVALOpacity = 80;
				
				// Session Naked Levels (no defaults here - just colors in group 07)
    
                // Weekly Naked Levels (no defaults here - just colors in group 08)
    
                // NEW: Naked Levels Settings (Group 09)
                DisplayNakedLevels = true;
                MaxNakedLevelsToDisplay = 5;
                DisplayWeeklyNakedLevels = false;
                MaxWeeklyNakedLevelsToDisplay = 3;
                KeepFilledLevelsAfterSession = false;
                RemoveAfterTouchCount = 0;
                KeepFilledWeeklyLevelsAfterWeek = false;
                RemoveWeeklyAfterTouchCount = 0;
                ShowTouchCountInLabels = false;
				BritishDateFormat = false;

                // Display Settings
                PreviousDayLineWidth = 0;
				ShowPriceValuesInLabels = true;

                // Dual Profile Mode
                EnableDualProfileMode = false;
                WeeklyProfileWidth = 200;
                SessionProfileWidth = 150;
                ProfileGap = 10;
                UseCustomDailySessionTimes = false;
                DailySessionStartTime = 1759;  // 5:59 PM
                DailySessionEndTime = 1700;     // 5:00 PM
                SessionProfileStyle = SessionProfileStyleEnum.Outline;
                SessionOutlineSmoothness = 50;
				
				// Gradient Fill
                EnableGradientFill = false;
                GradientIntensity = 70;
                
                // Weekly Profile Colors
                WeeklyBarColor = Brushes.DarkRed;
                WeeklyPoCColor = Brushes.Red;
                WeeklyVAColor = Brushes.DarkBlue;
                WeeklyVALinesColor = Brushes.Yellow;
				
				// Weekly Profile Settings
                WeeklyNumberOfVolumeBars = 250;
                WeeklyBarThickness = 2;
                WeeklyVolumeType = VolumeTypeEnum.Both;
                WeeklyBarOpacity = 50;
                WeeklyDisplayPoC = true;
                WeeklyPoCLineThickness = 2;
                WeeklyPoCLineOpacity = 100;
                WeeklyDisplayValueArea = true;
                WeeklyValueAreaPercentage = 68;
                WeeklyDisplayValueAreaLines = true;
                WeeklyValueAreaLinesThickness = 2;
                WeeklyValueAreaLinesOpacity = 100;
                
                // Session Profile Colors
                SessionOutlineColor = Brushes.Cyan;
                SessionPoCColor = Brushes.Orange;
                SessionVALinesColor = Brushes.LightGreen;
                SessionVAColor = Brushes.DarkCyan;
				
				// Session Profile Settings
                SessionNumberOfVolumeBars = 250;
                SessionBarThickness = 2;
                SessionVolumeType = VolumeTypeEnum.Both;
                SessionBarOpacity = 50;
                SessionDisplayPoC = true;
                SessionPoCLineThickness = 2;
                SessionPoCLineOpacity = 100;
                SessionDisplayValueArea = true;
                SessionValueAreaPercentage = 68;
                SessionDisplayValueAreaLines = true;
                SessionValueAreaLinesThickness = 2;
                SessionValueAreaLinesOpacity = 100;
                
                // LVN Settings
                DisplayLVN = false;
                LVNNumberOfRows = 100;
                LVNDetectionPercent = 5;
                ShowAdjacentLVNNodes = true;
                LVNFillColor = Brushes.Gray;
                LVNFillOpacity = 40;
                LVNBorderColor = Brushes.DarkGray;
                LVNBorderOpacity = 100;
				
				// DOM Visualization
                EnableDomdicator = false;
                DomdicatorWidth = 100;
                DomdicatorGap = 10;
                DomMaxRightExtension = 50;
                ShowDOMVolumeText = true;
                DomMaxTextSize = 14;
                DomMinTextSize = 10;
                DomHistoricalOpacity = 60;
                ShowHistoricalOrders = true;
                LiveOrderTickThreshold = 30;
                DomLiveOpacity = 80;
                MinimumOrdersToStart = 5;
                DomBidBrush = Brushes.Green;
                DomAskBrush = Brushes.Red;
                DomTextBrush = Brushes.White;
                DomOutlierBrush = Brushes.Yellow;
                currentBidPrice = double.MinValue;
                currentAskPrice = double.MaxValue;
            }
            else if (State == State.Configure)
            {
				// Initialize LVN profile
                lvnProfile = new ProfileData(LVNNumberOfRows);
				
                volumes = new List<double>();
                volumePolarities = new List<bool>();
                weeklyVolumes = new List<double>();
                sessionVolumes = new List<double>();
                anchoredProfiles = new List<SessionProfile>();
                needsRecalculation = true;
                lastCalculatedBar = -1;
            }
            else if (State == State.DataLoaded)
            {
                if (ProfileMode == ProfileModeEnum.Session)
                {
                    sessionIterator = new SessionIterator(Bars);
                }
            }
			
			else if (State == State.Realtime)
            {
                // Initialize DOM from market depth snapshot
                if (EnableDomdicator)
                {
                    System.Threading.Tasks.Task.Delay(1000).ContinueWith(t => InitializeFromMarketDepthSnapshot());
                }
            }
			else if (State == State.Terminated)
            {
                // Clean up DOM resources
                if (cachedDxBrushBid != null) { cachedDxBrushBid.Dispose(); cachedDxBrushBid = null; }
                if (cachedDxBrushAsk != null) { cachedDxBrushAsk.Dispose(); cachedDxBrushAsk = null; }
                if (cachedDxBrushText != null) { cachedDxBrushText.Dispose(); cachedDxBrushText = null; }
                if (cachedDxBrushOutlier != null) { cachedDxBrushOutlier.Dispose(); cachedDxBrushOutlier = null; }

                foreach (var format in textFormatCache.Values)
                {
                    if (format != null) format.Dispose();
                }
                textFormatCache.Clear();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;
            
            // LVN data collection based on profile mode (only in realtime)
            if (DisplayLVN && State == State.Realtime)
            {
                CollectLVNData();
            }

            // Check if Anchored mode is enabled
            bool isAnchoredMode = Alignment == ProfileAlignment.Anchored;
            
            // Validate that anchored mode is only used with compatible profile modes
            if (isAnchoredMode)
            {
                if (ProfileMode == ProfileModeEnum.VisibleRange)
                {
                    if (CurrentBar == 1)
                    {
                        Print("WARNING: Anchored alignment is not compatible with Visible Range mode.");
                        Print("Anchored mode works with Session, Weeks, or Months modes only.");
                    }
                    return;
                }
            }

            // ANCHORED MODE: Handle differently
            if (isAnchoredMode)
            {
                // Only update anchored profiles periodically to save CPU
                if (State == State.Realtime || CurrentBar % UpdateFrequency == 0 || CurrentBar == Count - 1)
                {
                    needsRecalculation = true;
                }
                return; // Skip the regular profile calculation
            }

            // REGULAR MODE (NON-ANCHORED): Existing logic
            if (ProfileMode == ProfileModeEnum.Session)
            {
                if (UseCustomSessionTimes)
                {
                    UpdateCustomSessionInfo();
                }
                else
                {
                    UpdateSessionInfo();
                }
            }
            
            // Check for Previous Day High/Low updates
            if (!isAnchoredMode) // Don't show previous day levels in anchored mode
            {
                CheckForPrevDayLevels();
            }
			
			// Check for Overnight level updates
if (!isAnchoredMode && (DisplayOvernightPOC || DisplayOvernightVAH || DisplayOvernightVAL || DisplayOvernightHigh || DisplayOvernightLow))
{
    CheckForOvernightLevels();
}
            
            // CRITICAL: Detect session boundary crossing (e.g., 6:00 PM)
            // Force recalculation when we enter a new session
            DateTime currentSessionDate = GetSessionDateForBar(Time[0]);
            if (currentSessionDate != lastKnownSessionDate && lastKnownSessionDate != DateTime.MinValue)
            {
                // We've crossed into a new session!
                
                // Force recalculation
                needsRecalculation = true;
                
                // Update daily levels immediately
                if (!isAnchoredMode && (DisplayPreviousDayPOC || DisplayPreviousDayVAH || DisplayPreviousDayVAL))
                {
                    UpdateDailyLevels();
                }
                
                // Always update historical levels if naked levels are enabled
                if (!isAnchoredMode && DisplayNakedLevels)
                {
                    UpdateDailyLevels();
                }
            }
            lastKnownSessionDate = currentSessionDate;

            // Check if we need to recalculate
            bool shouldCalculate = DetermineIfShouldCalculate();
            
            if (shouldCalculate)
            {
                
                // Calculate dual profiles if enabled
                if (EnableDualProfileMode)
                {
                    CalculateDualProfiles();
                }
                else
                {
                    CalculateVolumeProfile();
                    DrawVolumeProfile();
                }
                
                // Track previous day levels if enabled (but skip if we just did it above)
                if (!isAnchoredMode && !needsRecalculation)
                {
                    if (DisplayPreviousDayPOC || DisplayPreviousDayVAH || DisplayPreviousDayVAL)
                    {
                        UpdateDailyLevels();
                    }
                    
                    // Also update if naked levels are enabled
                    if (DisplayNakedLevels)
                    {
                        UpdateDailyLevels();
                    }
                }
                
                lastCalculatedBar = CurrentBar;
                needsRecalculation = false;
            }
            
			// NEW: Check for naked level touches on EVERY bar
            if (!isAnchoredMode && DisplayNakedLevels)
            {
                CheckNakedLevelTouches();
            }
			
			// Check for weekly naked level touches on EVERY bar
            if (!isAnchoredMode && DisplayWeeklyNakedLevels)
            {
                CheckWeeklyNakedLevelTouches();
                UpdateWeeklyLevels();
            }
			
			// Update all plot values
            UpdatePlotValues();
			
            // Draw previous day levels EVERY bar (only in non-anchored mode)
            if (!isAnchoredMode && (DisplayPreviousDayPOC || DisplayPreviousDayVAH || DisplayPreviousDayVAL) && previousDayLevels != null)
            {
                // Only draw if we're in realtime OR within last 500 bars of historical data
                bool shouldDraw = (State == State.Realtime) || (CurrentBar >= Count - 500);
                
                if (shouldDraw)
                {
                    // Use session-aware date logic
                    DateTime barSessionDate = GetSessionDateForBar(Time[0]);
                    DateTime previousSessionDate = previousDayLevels.Date;
                    
                    // Draw if we're in a session AFTER the previousDayLevels session
                    if (barSessionDate > previousSessionDate)
                    {
                        DrawPreviousDayLevels();
                    }
                }
            }
			
			// NEW: Draw naked levels EVERY bar
            if (!isAnchoredMode && DisplayNakedLevels)
            {
                bool shouldDraw = (State == State.Realtime) || (CurrentBar >= Count - 500);
                if (shouldDraw)
                {
                    DrawNakedLevels();
                }
            }
			
			// Draw weekly naked levels EVERY bar
            if (!isAnchoredMode && DisplayWeeklyNakedLevels)
            {
                bool shouldDraw = (State == State.Realtime) || (CurrentBar >= Count - 500);
                if (shouldDraw)
                {
                    DrawWeeklyNakedLevels();
                }
            }
            
            // Draw Previous Day High/Low EVERY bar (only in non-anchored mode)
            if (!isAnchoredMode && (DisplayPreviousDayHigh || DisplayPreviousDayLow) && prevDayHigh > 0)
            {
                bool shouldDraw = (State == State.Realtime) || (CurrentBar >= Count - 500);
                if (shouldDraw)
                {
                    DrawPrevDayHighLow();
                }
            }
			
			// Draw Overnight Levels EVERY bar (only in non-anchored mode)
if (!isAnchoredMode && (DisplayOvernightPOC || DisplayOvernightVAH || DisplayOvernightVAL || DisplayOvernightHigh || DisplayOvernightLow) && overnightLevelsCalculated)
{
    bool shouldDraw = (State == State.Realtime) || (CurrentBar >= Count - 500);
    if (shouldDraw)
    {
        DrawOvernightLevels();
    }
}
            
            // Calculate and draw LVN if enabled - only on the last bar for performance
            if (DisplayLVN)
            {
                // Only calculate LVN on the very last bar (most recent)
                // This gives instant loading but still shows LVN
                // Calculate once when we're within 50 bars of the end
                bool isNearEnd = (CurrentBar >= Count - 50);
                
                if (isNearEnd && lastLVNCalculationBar == -1)
                {
                    // Collect the data first, then calculate
                    CollectLVNData();
                    CalculateLVNProfile();
                    DrawLVNRectangles();
                    lastLVNCalculationBar = CurrentBar;
                }
            }
        }

        private bool DetermineIfShouldCalculate()
        {
            // Always calculate in real-time
            if (State == State.Realtime)
            {
                if (needsRecalculation)
                    return true;
                
                if (ProfileMode == ProfileModeEnum.VisibleRange && ChartControl != null && ChartBars != null)
                {
                    int currentFromIndex = ChartBars.FromIndex;
                    int currentToIndex = ChartBars.ToIndex;
                    
                    if (currentFromIndex != lastVisibleFromIndex || currentToIndex != lastVisibleToIndex)
                    {
                        lastVisibleFromIndex = currentFromIndex;
                        lastVisibleToIndex = currentToIndex;
                        needsRecalculation = true;
                        return true;
                    }
                }
                
                if (ProfileMode == ProfileModeEnum.Session)
                {
                    DateTime sessionToCheck = UseCustomSessionTimes ? customSessionStart : currentSessionStart;
                    if (sessionToCheck != lastSessionStart)
                    {
                        lastSessionStart = sessionToCheck;
                        needsRecalculation = true;
                        return true;
                    }
                }
                
                return true;
            }

            // Historical mode
            if (State == State.Historical)
            {
                int barsSinceLastCalc = CurrentBar - lastCalculatedBar;
                bool hitUpdateFrequency = barsSinceLastCalc >= UpdateFrequency;
                bool isLastBar = CurrentBar == Count - 1;
                
                if (hitUpdateFrequency || isLastBar)
                {
                    if (ProfileMode == ProfileModeEnum.VisibleRange && ChartControl != null && ChartBars != null)
                    {
                        int currentFromIndex = ChartBars.FromIndex;
                        int currentToIndex = ChartBars.ToIndex;
                        
                        if (currentFromIndex != lastVisibleFromIndex || currentToIndex != lastVisibleToIndex)
                        {
                            lastVisibleFromIndex = currentFromIndex;
                            lastVisibleToIndex = currentToIndex;
                            needsRecalculation = true;
                        }
                    }
                    
                    if (ProfileMode == ProfileModeEnum.Session)
                    {
                        DateTime sessionToCheck = UseCustomSessionTimes ? customSessionStart : currentSessionStart;
                        if (sessionToCheck != lastSessionStart)
                        {
                            lastSessionStart = sessionToCheck;
                            needsRecalculation = true;
                        }
                    }
                    
                    return true;
                }
                
                return false;
            }

            return false;
        }

        private void UpdateSessionInfo()
        {
            if (sessionIterator == null)
                return;

            try
            {
                // Get the session for the current bar
                sessionIterator.GetNextSession(Time[0], true);
                currentSessionStart = sessionIterator.ActualSessionBegin;
                currentSessionEnd = sessionIterator.ActualSessionEnd;
                
                // Determine which session we're in based on bar time
                DateTime currentSessionDate = GetSessionDateForBar(Time[0]);
                
                // Store this for comparison
                if (lastKnownSessionDate == DateTime.MinValue)
                {
                    lastKnownSessionDate = currentSessionDate;
                }
            }
            catch
            {
                // Silently handle session info errors
            }
        }
        
        private bool BarTouchesSession(int barsAgo, DateTime sessionStart, DateTime sessionEnd)
        {
            DateTime barTime = Time[barsAgo];
            
            // Bar starts within session
            if (barTime >= sessionStart && barTime < sessionEnd)
                return true;
            
            // For higher timeframes: check if bar spans the session
            // Calculate the bar's end time based on the timeframe
            DateTime barEndTime;
            if (barsAgo > 0)
            {
                // Bar end time is the start of the next bar
                barEndTime = Time[barsAgo - 1];
            }
            else
            {
                // For current bar, estimate end time based on bar period
                TimeSpan barPeriod = Time[0] - Time[1];
                barEndTime = Time[0].Add(barPeriod);
            }
            
            // Check if bar overlaps with session in any way
            // Bar overlaps if: bar starts before session ends AND bar ends after session starts
            if (barTime < sessionEnd && barEndTime > sessionStart)
                return true;
            
            return false;
        }

        private bool BarTouchesCustomSession(int barsAgo, DateTime sessionStart, DateTime sessionEnd)
        {
            DateTime barTime = Time[barsAgo];
            
            // Bar starts within session
            if (barTime >= sessionStart && barTime < sessionEnd)
                return true;
            
            // For higher timeframes: check if bar spans the session
            // Calculate the bar's end time based on the timeframe
            DateTime barEndTime;
            if (barsAgo > 0)
            {
                // Bar end time is the start of the next bar
                barEndTime = Time[barsAgo - 1];
            }
            else
            {
                // For current bar, estimate end time based on bar period
                TimeSpan barPeriod = Time[0] - Time[1];
                barEndTime = Time[0].Add(barPeriod);
            }
            
            // Check if bar overlaps with session in any way
            // Bar overlaps if: bar starts before session ends AND bar ends after session starts
            if (barTime < sessionEnd && barEndTime > sessionStart)
                return true;
            
            return false;
        }
		
		private void UpdateCustomDailySessionInfo()
{
    DateTime barDate = Time[0].Date;
    
    // Parse the 4-digit time format (HHMM)
    int startHour = DailySessionStartTime / 100;
    int startMinute = DailySessionStartTime % 100;
    int endHour = DailySessionEndTime / 100;
    int endMinute = DailySessionEndTime % 100;
    
    // If start time is in evening (e.g., 1759 = 5:59 PM), session starts previous day
    if (startHour >= 12 && endHour < 12)
    {
        // Cross-midnight session (e.g., 6 PM to 5 PM next day)
        customDailySessionStart = new DateTime(barDate.Year, barDate.Month, barDate.Day, startHour, startMinute, 0).AddDays(-1);
        customDailySessionEnd = new DateTime(barDate.Year, barDate.Month, barDate.Day, endHour, endMinute, 0);
    }
    else if (startHour > endHour || (startHour == endHour && startMinute > endMinute))
    {
        // Start is later in day than end (crosses midnight)
        customDailySessionStart = new DateTime(barDate.Year, barDate.Month, barDate.Day, startHour, startMinute, 0).AddDays(-1);
        customDailySessionEnd = new DateTime(barDate.Year, barDate.Month, barDate.Day, endHour, endMinute, 0);
    }
    else
    {
        // Same-day session (e.g., 9:30 AM to 4:00 PM)
        customDailySessionStart = new DateTime(barDate.Year, barDate.Month, barDate.Day, startHour, startMinute, 0);
        customDailySessionEnd = new DateTime(barDate.Year, barDate.Month, barDate.Day, endHour, endMinute, 0);
    }
}

        private void UpdateCustomSessionInfo()
        {
            DateTime barDate = Time[0].Date;
            
            // Parse the 4-digit time format (HHMM)
            int startHour = SessionStartTime / 100;
            int startMinute = SessionStartTime % 100;
            int endHour = SessionEndTime / 100;
            int endMinute = SessionEndTime % 100;
            
            // If start time is in evening (e.g., 1759 = 5:59 PM), session starts previous day
            if (startHour >= 12 && endHour < 12)
            {
                // Cross-midnight session (e.g., 6 PM to 5 PM next day)
                customSessionStart = new DateTime(barDate.Year, barDate.Month, barDate.Day, startHour, startMinute, 0).AddDays(-1);
                customSessionEnd = new DateTime(barDate.Year, barDate.Month, barDate.Day, endHour, endMinute, 0);
            }
            else if (startHour > endHour || (startHour == endHour && startMinute > endMinute))
            {
                // Start is later in day than end (crosses midnight)
                customSessionStart = new DateTime(barDate.Year, barDate.Month, barDate.Day, startHour, startMinute, 0).AddDays(-1);
                customSessionEnd = new DateTime(barDate.Year, barDate.Month, barDate.Day, endHour, endMinute, 0);
            }
            else
            {
                // Same-day session (e.g., 9:30 AM to 4:00 PM)
                customSessionStart = new DateTime(barDate.Year, barDate.Month, barDate.Day, startHour, startMinute, 0);
                customSessionEnd = new DateTime(barDate.Year, barDate.Month, barDate.Day, endHour, endMinute, 0);
            }
        }
 
        private int GetLookbackBarsForMode()
        {
            switch (ProfileMode)
            {
                case ProfileModeEnum.Session:
                    if (!UseCustomSessionTimes)
                    {
                        if (sessionIterator == null)
                        {
                            Print("WARNING: Session mode but no session iterator. Using fixed range.");
                            return Math.Min(200, CurrentBar);
                        }
                    }
                    
                    int sessionBars = 0;
                    for (int i = 0; i <= CurrentBar; i++)
                    {
                        if (UseCustomSessionTimes)
                        {
                            if (BarTouchesCustomSession(i, customSessionStart, customSessionEnd))
                                sessionBars++;
                        }
                        else
                        {
                            if (BarTouchesSession(i, currentSessionStart, currentSessionEnd))
                                sessionBars++;
                        }
                    }
                   
                    return Math.Max(1, sessionBars);
                
                
                case ProfileModeEnum.Weeks:
                    DateTime weeksAgo = Time[0].AddDays(-7 * WeeksLookback);
                    int weeksBars = 0;
                    for (int i = 0; i <= CurrentBar; i++)
                    {
                        if (Time[i] >= weeksAgo)
                            weeksBars++;
                    }
                    
                    return Math.Max(1, weeksBars);
                
                case ProfileModeEnum.Months:
                    DateTime monthsAgo = Time[0].AddMonths(-MonthsLookback);
                    int monthsBars = 0;
                    for (int i = 0; i <= CurrentBar; i++)
                    {
                        if (Time[i] >= monthsAgo)
                            monthsBars++;
                    }
                    
                    return Math.Max(1, monthsBars);
                
                case ProfileModeEnum.VisibleRange:
                    if (ChartControl != null && ChartBars != null)
                    {
                        int fromIndex = ChartBars.FromIndex;
                        int toIndex = ChartBars.ToIndex;
                        
                        // CRITICAL: If we're scrolled beyond CurrentBar (future area with no data), return 0
                        if (fromIndex > CurrentBar || toIndex < 0)
                            return 0;
                        
                        fromIndex = Math.Max(0, Math.Min(fromIndex, CurrentBar));
                        toIndex = Math.Max(0, Math.Min(toIndex, CurrentBar));
                        
                        int barsBack = CurrentBar - fromIndex;
                        int visibleBars = Math.Max(1, Math.Min(barsBack + 1, CurrentBar + 1));
                        
                        return visibleBars;
                    }
                    return Math.Min(200, CurrentBar);
                
                case ProfileModeEnum.Composite:
                    DateTime compositeStart;
                    DateTime compositeEnd = Time[0];
                    
                    switch (CompositeRangeType)
                    {
                        case CompositeDateRangeType.DaysBack:
                            compositeStart = Time[0].AddDays(-CompositeDaysBack);
                            break;
                            
                        case CompositeDateRangeType.WeeksBack:
                            compositeStart = Time[0].AddDays(-7 * CompositeWeeksBack);
                            break;
                            
                        case CompositeDateRangeType.MonthsBack:
                            compositeStart = Time[0].AddMonths(-CompositeMonthsBack);
                            break;
                            
                        case CompositeDateRangeType.CustomDateRange:
                            compositeStart = CompositeCustomStartDate;
                            compositeEnd = CompositeCustomEndDate;
                            break;
                            
                        default:
                            compositeStart = Time[0].AddDays(-30);
                            break;
                    }
                    
                    int compositeBars = 0;
                    for (int i = 0; i <= CurrentBar; i++)
                    {
                        if (Time[i] >= compositeStart && Time[i] <= compositeEnd)
                            compositeBars++;
                    }
                    
                    return Math.Max(1, compositeBars);
                
                default:
                    return Math.Min(200, CurrentBar);
            }
        }

        private void CalculateVolumeProfile()
        {
            volumes.Clear();
            volumePolarities.Clear();
            
            for (int i = 0; i < NumberOfVolumeBars; i++)
            {
                volumes.Add(0);
                volumePolarities.Add(true); // Default to bullish
            }

            int lookbackBars = GetLookbackBarsForMode();
            lookbackBars = Math.Min(lookbackBars, CurrentBar + 1);
            
            if (lookbackBars < 1)
                return;

            highestPrice = double.MinValue;
            lowestPrice = double.MaxValue;

            int barsProcessed = 0;
            for (int i = 0; i < lookbackBars && i <= CurrentBar; i++)
            {
                if (i > CurrentBar)
                    break;
                    
                int barIndex = CurrentBar - i;
                
                if (ProfileMode == ProfileModeEnum.Session)
                {
                    if (UseCustomSessionTimes)
                    {
                        if (!BarTouchesCustomSession(i, customSessionStart, customSessionEnd))
                            continue;
                    }
                    else
                    {
                        if (!BarTouchesSession(i, currentSessionStart, currentSessionEnd))
                            continue;
                    }
                }
                
                if (ProfileMode == ProfileModeEnum.Composite)
                {
                    DateTime barTime = Time[i];
                    DateTime startDate, endDate;
                    
                    switch (CompositeRangeType)
                    {
                        case CompositeDateRangeType.DaysBack:
                            startDate = Time[0].AddDays(-CompositeDaysBack);
                            endDate = Time[0];
                            break;
                            
                        case CompositeDateRangeType.WeeksBack:
                            startDate = Time[0].AddDays(-7 * CompositeWeeksBack);
                            endDate = Time[0];
                            break;
                            
                        case CompositeDateRangeType.MonthsBack:
                            startDate = Time[0].AddMonths(-CompositeMonthsBack);
                            endDate = Time[0];
                            break;
                            
                        case CompositeDateRangeType.CustomDateRange:
                            startDate = CompositeCustomStartDate;
                            endDate = CompositeCustomEndDate;
                            break;
                            
                        default:
                            startDate = Time[0].AddDays(-30);
                            endDate = Time[0];
                            break;
                    }
                    
                    if (barTime < startDate || barTime > endDate)
                        continue;
                }

                highestPrice = Math.Max(highestPrice, High[i]);
                lowestPrice = Math.Min(lowestPrice, Low[i]);
                barsProcessed++;
            }

            priceInterval = (highestPrice - lowestPrice) / (NumberOfVolumeBars - 1);

            if (priceInterval <= 0)
                return;

            // Track bullish/bearish volume per level
            double[] bullishVolume = new double[NumberOfVolumeBars];
            double[] bearishVolume = new double[NumberOfVolumeBars];

            barsProcessed = 0;
            for (int i = 0; i < lookbackBars && i <= CurrentBar; i++)
            {
                if (i > CurrentBar)
                    break;
                    
                int barIndex = CurrentBar - i;
                
                if (ProfileMode == ProfileModeEnum.Session)
                {
                    if (UseCustomSessionTimes)
                    {
                        if (!BarTouchesCustomSession(i, customSessionStart, customSessionEnd))
                            continue;
                    }
                    else
                    {
                        if (!BarTouchesSession(i, currentSessionStart, currentSessionEnd))
                            continue;
                    }
                }
                
                if (ProfileMode == ProfileModeEnum.Composite)
                {
                    DateTime barTime = Time[i];
                    DateTime startDate, endDate;
                    
                    switch (CompositeRangeType)
                    {
                        case CompositeDateRangeType.DaysBack:
                            startDate = Time[0].AddDays(-CompositeDaysBack);
                            endDate = Time[0];
                            break;
                            
                        case CompositeDateRangeType.WeeksBack:
                            startDate = Time[0].AddDays(-7 * CompositeWeeksBack);
                            endDate = Time[0];
                            break;
                            
                        case CompositeDateRangeType.MonthsBack:
                            startDate = Time[0].AddMonths(-CompositeMonthsBack);
                            endDate = Time[0];
                            break;
                            
                        case CompositeDateRangeType.CustomDateRange:
                            startDate = CompositeCustomStartDate;
                            endDate = CompositeCustomEndDate;
                            break;
                            
                        default:
                            startDate = Time[0].AddDays(-30);
                            endDate = Time[0];
                            break;
                    }
                    
                    if (barTime < startDate || barTime > endDate)
                        continue;
                }

                double barLow = Low[i];
                double barHigh = High[i];
                double barVolume = Volume[i];
                bool isBullish = Close[i] >= Open[i];
                
                bool includeVol = VolumeType == VolumeTypeEnum.Standard ||
                                 VolumeType == VolumeTypeEnum.Both ||
                                 (VolumeType == VolumeTypeEnum.Bullish && isBullish) ||
                                 (VolumeType == VolumeTypeEnum.Bearish && !isBullish);

                if (!includeVol)
                    continue;

                int minPriceIndex = (int)Math.Floor((barLow - lowestPrice) / priceInterval);
                int maxPriceIndex = (int)Math.Ceiling((barHigh - lowestPrice) / priceInterval);
                
                minPriceIndex = Math.Max(0, Math.Min(minPriceIndex, NumberOfVolumeBars - 1));
                maxPriceIndex = Math.Max(0, Math.Min(maxPriceIndex, NumberOfVolumeBars - 1));
                
                int touchedLevels = maxPriceIndex - minPriceIndex + 1;
                if (touchedLevels > 0)
                {
                    double volumePerLevel = barVolume / touchedLevels;
                    for (int j = minPriceIndex; j <= maxPriceIndex; j++)
                    {
                        volumes[j] += volumePerLevel;
                        
                        // Track polarity
                        if (isBullish)
                            bullishVolume[j] += volumePerLevel;
                        else
                            bearishVolume[j] += volumePerLevel;
                    }
                }
                
                barsProcessed++;
            }
            
            // Determine dominant polarity for each level
            for (int i = 0; i < NumberOfVolumeBars; i++)
            {
                volumePolarities[i] = bullishVolume[i] >= bearishVolume[i];
            }
        }

        private void DrawVolumeProfile()
        {
            if (volumes.Count == 0)
                return;

            double maxVolume = 0;
            int maxIndex = 0;
            for (int i = 0; i < volumes.Count; i++)
            {
                if (volumes[i] > maxVolume)
                {
                    maxVolume = volumes[i];
                    maxIndex = i;
                }
            }

            if (maxVolume == 0)
                return;

            int vaDown = maxIndex;
            int vaUp = maxIndex;

            if (DisplayValueArea)
            {
                CalculateValueArea(maxIndex, maxVolume, out vaDown, out vaUp);
            }

            maxVolumeForRender = maxVolume;
            maxIndexForRender = maxIndex;
            vaUpForRender = vaUp;
            vaDownForRender = vaDown;
        }

        private void RecalculateForVisibleRange()
        {
            if (isCalculating)
                return;
            
            isCalculating = true;
            
            try
            {
                int tempCurrentBar = CurrentBar;
                if (tempCurrentBar < 0 || Bars == null || tempCurrentBar >= Bars.Count)
                    return;
                
                volumes.Clear();
                volumePolarities.Clear();
                for (int i = 0; i < NumberOfVolumeBars; i++)
                {
                    volumes.Add(0);
                    volumePolarities.Add(true);
                }

                int lookbackBars = GetLookbackBarsForMode();
                lookbackBars = Math.Min(lookbackBars, tempCurrentBar + 1);
                
                if (lookbackBars < 1)
                    return;

                double highPrice = double.MinValue;
                double lowPrice = double.MaxValue;

                for (int i = 0; i < lookbackBars; i++)
                {
                    int barIndex = tempCurrentBar - i;
                    if (barIndex < 0 || barIndex >= Bars.Count)
                        continue;
                    
                    highPrice = Math.Max(highPrice, High.GetValueAt(barIndex));
                    lowPrice = Math.Min(lowPrice, Low.GetValueAt(barIndex));
                }

                double interval = (highPrice - lowPrice) / (NumberOfVolumeBars - 1);
                if (interval <= 0)
                    return;

                highestPrice = highPrice;
                lowestPrice = lowPrice;
                priceInterval = interval;

                // Track bullish/bearish volume per level
                double[] bullishVolume = new double[NumberOfVolumeBars];
                double[] bearishVolume = new double[NumberOfVolumeBars];

                for (int i = 0; i < lookbackBars; i++)
                {
                    int barIndex = tempCurrentBar - i;
                    if (barIndex < 0 || barIndex >= Bars.Count)
                        continue;

                    double barLow = Low.GetValueAt(barIndex);
                    double barHigh = High.GetValueAt(barIndex);
                    double barVolume = Volume.GetValueAt(barIndex);
                    double barClose = Close.GetValueAt(barIndex);
                    double barOpen = Open.GetValueAt(barIndex);
                    bool isBullish = barClose >= barOpen;
                    
                    bool includeVol = VolumeType == VolumeTypeEnum.Standard ||
                                     VolumeType == VolumeTypeEnum.Both ||
                                     (VolumeType == VolumeTypeEnum.Bullish && isBullish) ||
                                     (VolumeType == VolumeTypeEnum.Bearish && !isBullish);

                    if (!includeVol)
                        continue;

                    int minPriceIndex = (int)Math.Floor((barLow - lowestPrice) / priceInterval);
                    int maxPriceIndex = (int)Math.Ceiling((barHigh - lowestPrice) / priceInterval);
                    
                    minPriceIndex = Math.Max(0, Math.Min(minPriceIndex, NumberOfVolumeBars - 1));
                    maxPriceIndex = Math.Max(0, Math.Min(maxPriceIndex, NumberOfVolumeBars - 1));
                    
                    int touchedLevels = maxPriceIndex - minPriceIndex + 1;
                    if (touchedLevels > 0)
                    {
                        double volumePerLevel = barVolume / touchedLevels;
                        for (int j = minPriceIndex; j <= maxPriceIndex; j++)
                        {
                            volumes[j] += volumePerLevel;
                            
                            // Track polarity
                            if (isBullish)
                                bullishVolume[j] += volumePerLevel;
                            else
                                bearishVolume[j] += volumePerLevel;
                        }
                    }
                }
                
                // Determine dominant polarity for each level
                for (int i = 0; i < NumberOfVolumeBars; i++)
                {
                    volumePolarities[i] = bullishVolume[i] >= bearishVolume[i];
                }

                if (volumes.Count > 0)
                {
                    double maxVolume = 0;
                    int maxIndex = 0;
                    for (int i = 0; i < volumes.Count; i++)
                    {
                        if (volumes[i] > maxVolume)
                        {
                            maxVolume = volumes[i];
                            maxIndex = i;
                        }
                    }

                    if (maxVolume > 0)
                    {
                        int vaDown = maxIndex;
                        int vaUp = maxIndex;

                        if (DisplayValueArea)
                        {
                            CalculateValueArea(maxIndex, maxVolume, out vaDown, out vaUp);
                        }

                        maxVolumeForRender = maxVolume;
                        maxIndexForRender = maxIndex;
                        vaUpForRender = vaUp;
                        vaDownForRender = vaDown;
                    }
                }
            }
            catch
            {
                // Silently handle recalculation errors
            }
            finally
            {
                isCalculating = false;
            }
        }

        private void CalculateValueArea(int maxIndex, double maxVolume, out int vaDown, out int vaUp)
        {
            double sumVolume = 0;
            for (int i = 0; i < volumes.Count; i++)
                sumVolume += volumes[i];
            
            double vaVolume = sumVolume * ValueAreaPercentage / 100.0;

            vaUp = maxIndex;
            vaDown = maxIndex;
            double vaSum = maxVolume;

            while (vaSum < vaVolume)
            {
                double vUp = (vaUp < NumberOfVolumeBars - 1) ? volumes[vaUp + 1] : 0.0;
                double vDown = (vaDown > 0) ? volumes[vaDown - 1] : 0.0;

                if (vUp == 0 && vDown == 0)
                    break;

                if (vUp >= vDown)
                {
                    vaSum += vUp;
                    vaUp++;
                }
                else
                {
                    vaSum += vDown;
                    vaDown--;
                }
            }
        }

        // NEW METHOD: Get session date for a bar (works for Session/Weeks/Months modes)
        private DateTime GetSessionForBar(DateTime barTime)
        {
            switch (ProfileMode)
            {
                case ProfileModeEnum.Session:
                    // Daily sessions: 6 PM to 5 PM next day
                    return GetSessionDateForBar(barTime);
                
                case ProfileModeEnum.Weeks:
                    // Weekly sessions: Sunday 6 PM through Friday 5 PM
                    // Determine which week this bar belongs to based on futures session logic
                    
                    DateTime sessionDate;
                    
                    // If bar is on Sunday at/after 6 PM, it belongs to THIS Sunday's week
                    if (barTime.DayOfWeek == DayOfWeek.Sunday && barTime.Hour >= 18)
                    {
                        sessionDate = barTime.Date;
                    }
                    // If bar is on Sunday before 6 PM, it belongs to LAST Sunday's week
                    else if (barTime.DayOfWeek == DayOfWeek.Sunday)
                    {
                        sessionDate = barTime.Date.AddDays(-7);
                    }
                    // If bar is Monday through Friday (before or after 6 PM doesn't matter for these days)
                    else if (barTime.DayOfWeek >= DayOfWeek.Monday && barTime.DayOfWeek <= DayOfWeek.Friday)
                    {
                        // Find the most recent Sunday at or before this date
                        sessionDate = barTime.Date;
                        while (sessionDate.DayOfWeek != DayOfWeek.Sunday)
                        {
                            sessionDate = sessionDate.AddDays(-1);
                        }
                    }
                    // If bar is on Saturday, it belongs to NEXT Sunday's week
                    else // Saturday
                    {
                        sessionDate = barTime.Date.AddDays(1); // Move to Sunday
                    }
                    
                    return sessionDate;
                
                case ProfileModeEnum.Months:
    // Monthly sessions work like daily sessions:
    // Session starts at 6 PM on last day of PREVIOUS month
    // Session ends at 5 PM on last day of THIS month
    
    DateTime barDate = barTime.Date;
    int barHour = barTime.Hour;
    
    // Determine which month's session this bar belongs to
    DateTime sessionMonth;
    
    // If we're on the 1st of a month before 6 PM, we belong to PREVIOUS month's session
    if (barDate.Day == 1 && barHour < 18)
    {
        sessionMonth = barDate.AddMonths(-1);
    }
    // If we're on the last day of a month at/after 6 PM, we belong to NEXT month's session
    else
    {
        int daysInCurrentMonth = DateTime.DaysInMonth(barDate.Year, barDate.Month);
        bool isLastDayOfMonth = (barDate.Day == daysInCurrentMonth);
        
        if (isLastDayOfMonth && barHour >= 18)
        {
            // This bar belongs to next month's session
            sessionMonth = barDate.AddMonths(1);
        }
        else
        {
            // This bar belongs to current month's session
            sessionMonth = barDate;
        }
    }
    
    // Return first day of the month as the session identifier
    return new DateTime(sessionMonth.Year, sessionMonth.Month, 1);
                
                default:
                    return barTime.Date;
            }
        }

        // NEW METHOD: Calculate all anchored profiles based on lookback settings
        private void CalculateAnchoredProfiles()
        {
            if (ChartControl == null || ChartBars == null || Bars == null)
                return;

            anchoredProfiles.Clear();
            
            // Calculate cutoff date based on profile mode
            DateTime cutoffDate;
            
            switch (ProfileMode)
            {
                case ProfileModeEnum.Session:
                    // Count back N trading days (skip weekends)
                    DateTime tempDate = GetSessionDateForBar(Time.GetValueAt(CurrentBar));
                    for (int i = 0; i < SessionsLookback; i++)
                    {
                        tempDate = GetPreviousTradingDay(tempDate);
                    }
                    cutoffDate = tempDate;
                    break;
                    
                case ProfileModeEnum.Weeks:
                    // Get current week's Sunday session date
                    DateTime currentWeekSession = GetSessionForBar(Time.GetValueAt(CurrentBar));
                    // Go back N weeks from there
                    cutoffDate = currentWeekSession.AddDays(-7 * WeeksLookback);
                    break;
                    
                case ProfileModeEnum.Months:
                    // Get current month's session date (1st of month)
                    DateTime currentMonthSession = GetSessionForBar(Time.GetValueAt(CurrentBar));
                    // Go back N months from there
                    cutoffDate = currentMonthSession.AddMonths(-MonthsLookback);
                    break;
                
                case ProfileModeEnum.Composite:
                    switch (CompositeRangeType)
                    {
                        case CompositeDateRangeType.DaysBack:
                            cutoffDate = Time.GetValueAt(CurrentBar).AddDays(-CompositeDaysBack);
                            break;
                            
                        case CompositeDateRangeType.WeeksBack:
                            cutoffDate = Time.GetValueAt(CurrentBar).AddDays(-7 * CompositeWeeksBack);
                            break;
                            
                        case CompositeDateRangeType.MonthsBack:
                            cutoffDate = Time.GetValueAt(CurrentBar).AddMonths(-CompositeMonthsBack);
                            break;
                            
                        case CompositeDateRangeType.CustomDateRange:
                            cutoffDate = CompositeCustomStartDate;
                            break;
                            
                        default:
                            cutoffDate = Time.GetValueAt(CurrentBar).AddDays(-30);
                            break;
                    }
                    break;
                    
                default:
                    cutoffDate = Time[0].AddDays(-30); // Fallback
                    break;
            }
            
            // Collect all unique sessions from CurrentBar back to cutoff date
            Dictionary<DateTime, List<int>> sessionBars = new Dictionary<DateTime, List<int>>();
            
            // Scan backwards from current bar to cutoff date
            for (int barIdx = CurrentBar; barIdx >= 0; barIdx--)
            {
                if (barIdx >= Bars.Count)
                    continue;

                DateTime barTime = Time.GetValueAt(barIdx);
                
                if (barTime < cutoffDate)
                    break;
                
                DateTime sessionDate = GetSessionForBar(barTime);
                
                if (sessionDate < cutoffDate)
                    continue;
                
                // Note: We do NOT skip weekend session dates for Weekly/Monthly modes
                // because Sunday is a valid anchor for weekly profiles!
                // Only skip if it's actually Saturday (which should never happen with our logic)
                if (sessionDate.DayOfWeek == DayOfWeek.Saturday)
                    continue;

                if (!sessionBars.ContainsKey(sessionDate))
                {
                    sessionBars[sessionDate] = new List<int>();
                }
                
                sessionBars[sessionDate].Add(barIdx);
            }

            // Calculate profile for each session
            foreach (var kvp in sessionBars.OrderBy(x => x.Key))
            {
                DateTime sessionDate = kvp.Key;
                List<int> barIndices = kvp.Value;
                
                if (barIndices.Count == 0)
                    continue;

                SessionProfile profile = CalculateSingleSessionProfile(sessionDate, barIndices);
                
                if (profile != null)
                    anchoredProfiles.Add(profile);
            }
        }

        // NEW METHOD: Calculate profile for a single session
        private SessionProfile CalculateSingleSessionProfile(DateTime sessionDate, List<int> barIndices)
        {
            if (barIndices.Count == 0)
                return null;

            SessionProfile profile = new SessionProfile
            {
                SessionDate = sessionDate,
                StartBarIndex = barIndices.Min(),
                EndBarIndex = barIndices.Max(),
                Volumes = new List<double>()
            };

            // Initialize volume bins
            for (int i = 0; i < NumberOfVolumeBars; i++)
                profile.Volumes.Add(0);

            // Find price range
            double high = double.MinValue;
            double low = double.MaxValue;

            foreach (int barIdx in barIndices)
            {
                if (barIdx >= Bars.Count)
                    continue;
                    
                high = Math.Max(high, High.GetValueAt(barIdx));
                low = Math.Min(low, Low.GetValueAt(barIdx));
            }

            if (high <= low)
                return null;

            profile.HighestPrice = high;
            profile.LowestPrice = low;
            profile.PriceInterval = (high - low) / (NumberOfVolumeBars - 1);

            if (profile.PriceInterval <= 0)
                return null;

            // Accumulate volume
            foreach (int barIdx in barIndices)
            {
                if (barIdx >= Bars.Count)
                    continue;

                double barLow = Low.GetValueAt(barIdx);
                double barHigh = High.GetValueAt(barIdx);
                double barVolume = Volume.GetValueAt(barIdx);
                double barClose = Close.GetValueAt(barIdx);
                double barOpen = Open.GetValueAt(barIdx);
                bool isBullish = barClose >= barOpen;
                
                bool includeVol = VolumeType == VolumeTypeEnum.Standard ||
                                 VolumeType == VolumeTypeEnum.Both ||
                                 (VolumeType == VolumeTypeEnum.Bullish && isBullish) ||
                                 (VolumeType == VolumeTypeEnum.Bearish && !isBullish);

                if (!includeVol)
                    continue;
				
                int minIdx = (int)Math.Floor((barLow - profile.LowestPrice) / profile.PriceInterval);
                int maxIdx = (int)Math.Ceiling((barHigh - profile.LowestPrice) / profile.PriceInterval);
                
                minIdx = Math.Max(0, Math.Min(minIdx, NumberOfVolumeBars - 1));
                maxIdx = Math.Max(0, Math.Min(maxIdx, NumberOfVolumeBars - 1));
                
                int levels = maxIdx - minIdx + 1;
                if (levels > 0)
                {
                    double volPerLevel = barVolume / levels;
                    for (int j = minIdx; j <= maxIdx; j++)
                    {
                        profile.Volumes[j] += volPerLevel;
                    }
                }
            }

            // Find POC
            double maxVol = 0;
            int pocIdx = 0;
            for (int i = 0; i < profile.Volumes.Count; i++)
            {
                if (profile.Volumes[i] > maxVol)
                {
                    maxVol = profile.Volumes[i];
                    pocIdx = i;
                }
            }

            profile.MaxVolume = maxVol;
            profile.POCIndex = pocIdx;

            if (maxVol == 0)
                return null;

            // Calculate Value Area
            if (DisplayValueArea)
            {
                double sumVol = profile.Volumes.Sum();
                double vaVol = sumVol * ValueAreaPercentage / 100.0;

                int vaUp = pocIdx;
                int vaDown = pocIdx;
                double vaSum = maxVol;

                while (vaSum < vaVol)
                {
                    double vUp = (vaUp < NumberOfVolumeBars - 1) ? profile.Volumes[vaUp + 1] : 0.0;
                    double vDown = (vaDown > 0) ? profile.Volumes[vaDown - 1] : 0.0;

                    if (vUp == 0 && vDown == 0)
                        break;

                    if (vUp >= vDown)
                    {
                        vaSum += vUp;
                        vaUp++;
                    }
                    else
                    {
                        vaSum += vDown;
                        vaDown--;
                    }
                }

                profile.VAUpIndex = vaUp;
                profile.VADownIndex = vaDown;
            }

            return profile;
        }
		
		protected override void OnMarketDepth(MarketDepthEventArgs marketDepthUpdate)
        {
            if (!EnableDomdicator || State != State.Realtime)
                return;

            if (Instrument == null || marketDepthUpdate == null)
                return;

            if (marketDepthUpdate.Instrument != null && marketDepthUpdate.Instrument != Instrument)
                return;

            if (marketDepthUpdate.IsReset)
            {
                lock (orderLock)
                {
                    renderBidOrders.Clear();
                    renderAskOrders.Clear();
                    cachedDOMDataDirty = true;
                }
                InitializeFromMarketDepthSnapshot();
                needsDOMRefresh = true;
                return;
            }

            if ((renderBidOrders.Count + renderAskOrders.Count) > 0 && 
                (renderBidOrders.Count + renderAskOrders.Count) % 50 == 0)
            {
                InitializeFromMarketDepthSnapshot();
            }

            double price = Math.Round(marketDepthUpdate.Price, 2);
            long volume = marketDepthUpdate.Volume;
            Operation operation = marketDepthUpdate.Operation;
            int position = marketDepthUpdate.Position;

            // Ask side (position 0)
            if (position == 0)
            {
                lock (orderLock)
                {
                    if (operation == Operation.Add || operation == Operation.Update)
                    {
                        if (volume > 0)
                        {
                            if (!renderAskOrders.ContainsKey(price))
                                renderAskOrders[price] = new OrderInfo();
                            renderAskOrders[price].Price = price;
                            renderAskOrders[price].Volume = volume;
                            renderAskOrders[price].LastUpdate = DateTime.Now;

                            if (recentVolumes.Count >= VOLUME_HISTORY_SIZE)
                                recentVolumes.Dequeue();
                            recentVolumes.Enqueue(volume);

                            if (volume > maxDOMVolume && (DateTime.Now - lastMaxVolumeUpdate).TotalMilliseconds > MAX_VOLUME_UPDATE_INTERVAL_MS)
                            {
                                maxDOMVolume = volume;
                                outlierThreshold = volume;
                                lastMaxVolumeUpdate = DateTime.Now;
                            }
                        }
                    }
                    else if (operation == Operation.Remove)
                    {
                        renderAskOrders.Remove(price);
                    }

                    if (marketDepthUpdate.MarketMaker == "Inside Ask")
                    {
                        currentAskPrice = price;
                    }
                    
                    cachedDOMDataDirty = true;
                }
            }
            // Bid side (position 1)
            else if (position == 1)
            {
                lock (orderLock)
                {
                    if (operation == Operation.Add || operation == Operation.Update)
                    {
                        if (volume > 0)
                        {
                            if (!renderBidOrders.ContainsKey(price))
                                renderBidOrders[price] = new OrderInfo();
                            renderBidOrders[price].Price = price;
                            renderBidOrders[price].Volume = volume;
                            renderBidOrders[price].LastUpdate = DateTime.Now;

                            if (recentVolumes.Count >= VOLUME_HISTORY_SIZE)
                                recentVolumes.Dequeue();
                            recentVolumes.Enqueue(volume);

                            if (volume > maxDOMVolume && (DateTime.Now - lastMaxVolumeUpdate).TotalMilliseconds > MAX_VOLUME_UPDATE_INTERVAL_MS)
                            {
                                maxDOMVolume = volume;
                                outlierThreshold = volume;
                                lastMaxVolumeUpdate = DateTime.Now;
                            }
                        }
                    }
                    else if (operation == Operation.Remove)
                    {
                        renderBidOrders.Remove(price);
                    }

                    if (marketDepthUpdate.MarketMaker == "Inside Bid")
                    {
                        currentBidPrice = price;
                    }
                    
                    cachedDOMDataDirty = true;
                }
            }

            needsDOMRefresh = true;
            
            if ((DateTime.Now - lastDOMRefresh).TotalMilliseconds >= MIN_REFRESH_INTERVAL_MS)
            {
                ChartControl?.Dispatcher.InvokeAsync(() => ChartControl?.InvalidateVisual(), System.Windows.Threading.DispatcherPriority.Render);
                lastDOMRefresh = DateTime.Now;
            }
        }
		
		public override void OnRenderTargetChanged()
        {
            try
            {
                if (cachedDxBrushBid != null) { cachedDxBrushBid.Dispose(); cachedDxBrushBid = null; }
                if (cachedDxBrushAsk != null) { cachedDxBrushAsk.Dispose(); cachedDxBrushAsk = null; }
                if (cachedDxBrushText != null) { cachedDxBrushText.Dispose(); cachedDxBrushText = null; }
                if (cachedDxBrushOutlier != null) { cachedDxBrushOutlier.Dispose(); cachedDxBrushOutlier = null; }
                
                lastBrushUpdate = DateTime.MinValue;
            }
            catch
            {
                // Silently handle errors
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);
			
			// Render DOM if enabled
            if (EnableDomdicator && State == State.Realtime)
            {
                RenderDOMVisualization(chartControl, chartScale);
            }
            
            // DUAL PROFILE MODE RENDERING
            if (EnableDualProfileMode)
            {
                RenderDualProfiles(chartControl, chartScale);
                return;
            }

            // ANCHORED MODE RENDERING
            if (Alignment == ProfileAlignment.Anchored)
            {
                // Recalculate if needed
                if (needsRecalculation || anchoredProfiles.Count == 0)
                {
                    CalculateAnchoredProfiles();
                    needsRecalculation = false;
                }

                // Render each anchored profile
                foreach (SessionProfile profile in anchoredProfiles)
                {
                    RenderAnchoredProfile(chartControl, chartScale, profile);
                }
                
                return;
            }

            // REGULAR MODE RENDERING
            if (ProfileMode == ProfileModeEnum.VisibleRange && ChartBars != null && !isCalculating)
            {
                int currentFromIndex = ChartBars.FromIndex;
                int currentToIndex = ChartBars.ToIndex;
                
                if (currentFromIndex != lastVisibleFromIndex || currentToIndex != lastVisibleToIndex)
                {
                    lastVisibleFromIndex = currentFromIndex;
                    lastVisibleToIndex = currentToIndex;
                    
                    RecalculateForVisibleRange();
                }
            }

            if (volumes.Count == 0 || maxVolumeForRender == 0)
            {
                return;
            }

            float profileWidthPixels = ProfileWidth;
            float leftEdge, rightEdge;
            
            if (Alignment == ProfileAlignment.Right)
            {
                rightEdge = (float)chartControl.CanvasRight;
                leftEdge = rightEdge - profileWidthPixels;
            }
            else
            {
                leftEdge = (float)chartControl.CanvasLeft;
                rightEdge = leftEdge + profileWidthPixels;
            }

            SharpDX.Direct2D1.Brush pocBrush = null;
            SharpDX.Direct2D1.Brush vaBrush = null;
            SharpDX.Direct2D1.Brush regularBrush = null;

            try
            {
                pocBrush = PoCLineColor.ToDxBrush(RenderTarget);
                vaBrush = ValueAreaBarColor.Clone().ToDxBrush(RenderTarget);
                vaBrush.Opacity = BarOpacity / 100.0f;
                regularBrush = BarColor.Clone().ToDxBrush(RenderTarget);
                regularBrush.Opacity = BarOpacity / 100.0f;

                for (int i = 0; i < volumes.Count; i++)
                {
                    double vol = volumes[i];
                    if (vol <= 0)
                        continue;

                    double priceLevel = lowestPrice + priceInterval * i;
                    float y = chartScale.GetYByValue(priceLevel);

                    double volumeRatio = vol / maxVolumeForRender;
                    float barWidth = (float)(volumeRatio * profileWidthPixels);
                    
                    float barLeft, barRight;
                    if (Alignment == ProfileAlignment.Right)
                    {
                        barRight = rightEdge;
                        barLeft = rightEdge - barWidth;
                    }
                    else
                    {
                        barLeft = leftEdge;
                        barRight = leftEdge + barWidth;
                    }

                    SharpDX.Direct2D1.Brush barBrush;
                    bool isPOC = (i == maxIndexForRender && DisplayPoC);
                    bool isVA = (DisplayValueArea && i >= vaDownForRender && i <= vaUpForRender);
                    
                    // Determine which color to use based on volume type and polarity
                    Brush sourceColor;
                    if (isPOC)
                    {
                        sourceColor = PoCLineColor;
                    }
                    else if (isVA && VolumeType == VolumeTypeEnum.Standard)
                    {
                        // Standard mode: use traditional VA color
                        sourceColor = ValueAreaBarColor;
                    }
                    else if (VolumeType == VolumeTypeEnum.Standard)
                    {
                        // Standard mode: use traditional bar color
                        sourceColor = BarColor;
                    }
                    else
                    {
                        // Polarity-based colors for Bullish/Bearish/Both modes
                        if (VolumeType == VolumeTypeEnum.Bullish)
                            sourceColor = BullishBarColor;
                        else if (VolumeType == VolumeTypeEnum.Bearish)
                            sourceColor = BearishBarColor;
                        else // VolumeType.Both - show dominant polarity
                            sourceColor = volumePolarities[i] ? BullishBarColor : BearishBarColor;
                    }
                    
                    // Apply gradient to all volume bars (regular and VA)
                    SharpDX.Direct2D1.LinearGradientBrush gradientBrush = null;
                    
                    if (EnableGradientFill)
                    {
                        gradientBrush = CreateGradientBrush(sourceColor, barLeft, barRight, y);
                        barBrush = gradientBrush ?? sourceColor.ToDxBrush(RenderTarget);
                        if (barBrush != null && !(barBrush is SharpDX.Direct2D1.LinearGradientBrush))
                            barBrush.Opacity = BarOpacity / 100.0f;
                    }
                    else
                    {
                        // No gradient - use solid brushes with polarity colors
                        barBrush = sourceColor.Clone().ToDxBrush(RenderTarget);
                        barBrush.Opacity = BarOpacity / 100.0f;
                    }

                    float gapSize = 1.0f;
                    float adjustedY = y + (gapSize / 2.0f);
                    float adjustedThickness = Math.Max(1, BarThickness - gapSize);

                    SharpDX.Vector2 startPoint = new SharpDX.Vector2(barLeft, adjustedY);
                    SharpDX.Vector2 endPoint = new SharpDX.Vector2(barRight, adjustedY);
                    RenderTarget.DrawLine(startPoint, endPoint, barBrush, adjustedThickness);
                    
                    // Dispose gradient brush if created
                    gradientBrush?.Dispose();
                }

                // Draw POC line
                if (DisplayPoC && maxIndexForRender >= 0 && maxIndexForRender < volumes.Count)
                {
                    SharpDX.Direct2D1.Brush pocLineBrush = null;
                    try
                    {
                        pocLineBrush = PoCLineColor.Clone().ToDxBrush(RenderTarget);
                        pocLineBrush.Opacity = PoCLineOpacity / 100.0f;

                        SharpDX.Direct2D1.StrokeStyle pocStrokeStyle = null;
                        if (PoCLineStyle != DashStyleHelper.Solid)
                        {
                            var factory = RenderTarget.Factory;
                            var strokeStyleProperties = new SharpDX.Direct2D1.StrokeStyleProperties();
                            
                            if (PoCLineStyle == DashStyleHelper.Dash)
                                strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.Dash;
                            else if (PoCLineStyle == DashStyleHelper.Dot)
                                strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.Dot;
                            else if (PoCLineStyle == DashStyleHelper.DashDot)
                                strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.DashDot;
                            else if (PoCLineStyle == DashStyleHelper.DashDotDot)
                                strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.DashDotDot;
                            
                            pocStrokeStyle = new SharpDX.Direct2D1.StrokeStyle(factory, strokeStyleProperties);
                        }

                        double pocPrice = lowestPrice + priceInterval * maxIndexForRender;
                        float pocY = chartScale.GetYByValue(pocPrice);
                        
                        float pocStartX, pocEndX;
                        if (ExtendPoCLine)
                        {
                            if (Alignment == ProfileAlignment.Right)
                            {
                                pocStartX = (float)chartControl.CanvasLeft;
                                pocEndX = rightEdge;
                            }
                            else
                            {
                                pocStartX = leftEdge;
                                pocEndX = (float)chartControl.CanvasRight;
                            }
                        }
                        else
                        {
                            pocStartX = leftEdge;
                            pocEndX = rightEdge;
                        }
                        
                        SharpDX.Vector2 pocStart = new SharpDX.Vector2(pocStartX, pocY);
                        SharpDX.Vector2 pocEnd = new SharpDX.Vector2(pocEndX, pocY);
                        
                        if (pocStrokeStyle != null)
                            RenderTarget.DrawLine(pocStart, pocEnd, pocLineBrush, PoCLineThickness, pocStrokeStyle);
                        else
                            RenderTarget.DrawLine(pocStart, pocEnd, pocLineBrush, PoCLineThickness);
                        
                        pocStrokeStyle?.Dispose();
                    }
                    finally
                    {
                        pocLineBrush?.Dispose();
                    }
                }
				
				// Draw POC price label
if (DisplayPoC && ShowPriceValuesInLabels && maxIndexForRender >= 0 && maxIndexForRender < volumes.Count)
{
    double pocPrice = lowestPrice + priceInterval * maxIndexForRender;
    string pocLabel = "POC " + pocPrice.ToString("F2");
    
    float pocY = chartScale.GetYByValue(pocPrice);
    float labelX = Alignment == ProfileAlignment.Right ? (float)chartControl.CanvasRight - 60 : (float)chartControl.CanvasLeft;
    
    SharpDX.Direct2D1.SolidColorBrush textBrush = PoCLineColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        var textRect = new SharpDX.RectangleF(labelX, pocY - 15, 60, 15);
        RenderTarget.DrawText(pocLabel, textFormat, textRect, textBrush);
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}

                // Draw Value Area boundary lines
                if (DisplayValueArea && DisplayValueAreaLines)
                {
                    SharpDX.Direct2D1.Brush vaLinesBrush = null;
                    try
                    {
                        vaLinesBrush = ValueAreaLinesColor.Clone().ToDxBrush(RenderTarget);
                        vaLinesBrush.Opacity = ValueAreaLinesOpacity / 100.0f;

                        SharpDX.Direct2D1.StrokeStyle strokeStyle = null;
                        if (ValueAreaLineStyle != DashStyleHelper.Solid)
                        {
                            var factory = RenderTarget.Factory;
                            var strokeStyleProperties = new SharpDX.Direct2D1.StrokeStyleProperties();
                            
                            if (ValueAreaLineStyle == DashStyleHelper.Dash)
                                strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.Dash;
                            else if (ValueAreaLineStyle == DashStyleHelper.Dot)
                                strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.Dot;
                            else if (ValueAreaLineStyle == DashStyleHelper.DashDot)
                                strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.DashDot;
                            else if (ValueAreaLineStyle == DashStyleHelper.DashDotDot)
                                strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.DashDotDot;
                            
                            strokeStyle = new SharpDX.Direct2D1.StrokeStyle(factory, strokeStyleProperties);
                        }

                        // VAH line
                        if (vaUpForRender >= 0 && vaUpForRender < volumes.Count)
                        {
                            double vahPrice = lowestPrice + priceInterval * vaUpForRender;
                            float vahY = chartScale.GetYByValue(vahPrice);
                            
                            float vahStartX, vahEndX;
                            if (ExtendValueAreaLines)
                            {
                                if (Alignment == ProfileAlignment.Right)
                                {
                                    vahStartX = (float)chartControl.CanvasLeft;
                                    vahEndX = rightEdge;
                                }
                                else
                                {
                                    vahStartX = leftEdge;
                                    vahEndX = (float)chartControl.CanvasRight;
                                }
                            }
                            else
                            {
                                double volumeRatio = volumes[vaUpForRender] / maxVolumeForRender;
                                float barWidth = (float)(volumeRatio * profileWidthPixels);
                                
                                if (Alignment == ProfileAlignment.Right)
                                {
                                    vahStartX = rightEdge - barWidth;
                                    vahEndX = rightEdge;
                                }
                                else
                                {
                                    vahStartX = leftEdge;
                                    vahEndX = leftEdge + barWidth;
                                }
                            }
                            
                            SharpDX.Vector2 vahStart = new SharpDX.Vector2(vahStartX, vahY);
                            SharpDX.Vector2 vahEnd = new SharpDX.Vector2(vahEndX, vahY);
                            
                            if (strokeStyle != null)
                                RenderTarget.DrawLine(vahStart, vahEnd, vaLinesBrush, ValueAreaLinesThickness, strokeStyle);
                            else
                                RenderTarget.DrawLine(vahStart, vahEnd, vaLinesBrush, ValueAreaLinesThickness);
                        }

                        // VAL line
                        if (vaDownForRender >= 0 && vaDownForRender < volumes.Count)
                        {
                            double valPrice = lowestPrice + priceInterval * vaDownForRender;
                            float valY = chartScale.GetYByValue(valPrice);
                            
                            float valStartX, valEndX;
                            if (ExtendValueAreaLines)
                            {
                                if (Alignment == ProfileAlignment.Right)
                                {
                                    valStartX = (float)chartControl.CanvasLeft;
                                    valEndX = rightEdge;
                                }
                                else
                                {
                                    valStartX = leftEdge;
                                    valEndX = (float)chartControl.CanvasRight;
                                }
                            }
                            else
                            {
                                double volumeRatio = volumes[vaDownForRender] / maxVolumeForRender;
                                float barWidth = (float)(volumeRatio * profileWidthPixels);
                                
                                if (Alignment == ProfileAlignment.Right)
                                {
                                    valStartX = rightEdge - barWidth;
                                    valEndX = rightEdge;
                                }
                                else
                                {
                                    valStartX = leftEdge;
                                    valEndX = leftEdge + barWidth;
                                }
                            }
                            
                            SharpDX.Vector2 valStart = new SharpDX.Vector2(valStartX, valY);
                            SharpDX.Vector2 valEnd = new SharpDX.Vector2(valEndX, valY);
                            
                            if (strokeStyle != null)
                                RenderTarget.DrawLine(valStart, valEnd, vaLinesBrush, ValueAreaLinesThickness, strokeStyle);
                            else
                                RenderTarget.DrawLine(valStart, valEnd, vaLinesBrush, ValueAreaLinesThickness);
                        }
                        
                        strokeStyle?.Dispose();
                    }
                    finally
                    {
                        vaLinesBrush?.Dispose();
                    }
                }
				
				// Draw VAH and VAL price labels
if (DisplayValueArea && DisplayValueAreaLines && ShowPriceValuesInLabels)
{
    SharpDX.Direct2D1.SolidColorBrush textBrush = ValueAreaLinesColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        float labelX = Alignment == ProfileAlignment.Right ? (float)chartControl.CanvasRight - 60 : (float)chartControl.CanvasLeft;
        
        // VAH label
        if (vaUpForRender >= 0 && vaUpForRender < volumes.Count)
        {
            double vahPrice = lowestPrice + priceInterval * vaUpForRender;
            string vahLabel = "VAH " + vahPrice.ToString("F2");
            float vahY = chartScale.GetYByValue(vahPrice);
            var vahRect = new SharpDX.RectangleF(labelX, vahY - 15, 60, 15);
            RenderTarget.DrawText(vahLabel, textFormat, vahRect, textBrush);
        }
        
        // VAL label
        if (vaDownForRender >= 0 && vaDownForRender < volumes.Count)
        {
            double valPrice = lowestPrice + priceInterval * vaDownForRender;
            string valLabel = "VAL " + valPrice.ToString("F2");
            float valY = chartScale.GetYByValue(valPrice);
            var valRect = new SharpDX.RectangleF(labelX, valY - 15, 60, 15);
            RenderTarget.DrawText(valLabel, textFormat, valRect, textBrush);
        }
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}
            }
            catch
            {
                // Silently handle rendering errors
            }
            finally
            {
                pocBrush?.Dispose();
                vaBrush?.Dispose();
                regularBrush?.Dispose();
            }
        }
		
		
		
		#region Gradient Rendering Helper
        
        private SharpDX.Direct2D1.LinearGradientBrush CreateGradientBrush(Brush baseColor, float startX, float endX, float y)
        {
            if (!EnableGradientFill || GradientIntensity <= 0)
                return null;
            
            try
            {
                // Extract color from brush safely
                System.Windows.Media.Color mediaColor;
                if (baseColor is System.Windows.Media.SolidColorBrush solidBrush)
                {
                    mediaColor = solidBrush.Color;
                }
                else
                {
                    // Fallback to gray if we can't get the color
                    mediaColor = System.Windows.Media.Colors.Gray;
                }
                
                // Calculate opacity at start (left) and end (right)
                float baseOpacity = BarOpacity / 100.0f;
                float intensityFactor = GradientIntensity / 100.0f;
                float startOpacity = baseOpacity * (1.0f - intensityFactor); // More transparent at left
                float endOpacity = baseOpacity; // Solid at right
                
                // Create gradient stops
                var gradientStops = new SharpDX.Direct2D1.GradientStop[2];
                
                // Start stop (left - more transparent)
                gradientStops[0] = new SharpDX.Direct2D1.GradientStop
                {
                    Position = 0.0f,
                    Color = new SharpDX.Color4(
                        mediaColor.R / 255f,
                        mediaColor.G / 255f,
                        mediaColor.B / 255f,
                        startOpacity
                    )
                };
                
                // End stop (right - solid)
                gradientStops[1] = new SharpDX.Direct2D1.GradientStop
                {
                    Position = 1.0f,
                    Color = new SharpDX.Color4(
                        mediaColor.R / 255f,
                        mediaColor.G / 255f,
                        mediaColor.B / 255f,
                        endOpacity
                    )
                };
                
                // Create gradient stop collection
                var gradientStopCollection = new SharpDX.Direct2D1.GradientStopCollection(
                    RenderTarget,
                    gradientStops
                );
                
                // Create linear gradient brush
                var gradientBrush = new SharpDX.Direct2D1.LinearGradientBrush(
                    RenderTarget,
                    new SharpDX.Direct2D1.LinearGradientBrushProperties
                    {
                        StartPoint = new SharpDX.Vector2(startX, y),
                        EndPoint = new SharpDX.Vector2(endX, y)
                    },
                    gradientStopCollection
                );
                
                gradientStopCollection.Dispose();
                
                return gradientBrush;
            }
            catch
            {
                return null;
            }
        }
        
        #endregion
		
		#region Dual Profile Rendering
        
        private void RenderDualProfiles(ChartControl chartControl, ChartScale chartScale)
        {
            float rightEdge = (float)chartControl.CanvasRight;
            
            // Calculate positions
            float weeklyLeft = rightEdge - WeeklyProfileWidth;
            float weeklyRight = rightEdge;
            
            float sessionLeft = weeklyLeft - ProfileGap - SessionProfileWidth;
            float sessionRight = weeklyLeft - ProfileGap;
            
            // Render Weekly Profile (filled)
            RenderWeeklyProfile(chartControl, chartScale, weeklyLeft, weeklyRight);
            
            // Render Session Profile (outline)
            RenderSessionProfile(chartControl, chartScale, sessionLeft, sessionRight);
        }
        
        private void RenderWeeklyProfile(ChartControl chartControl, ChartScale chartScale, float leftEdge, float rightEdge)
        {
            if (weeklyVolumes.Count == 0 || weeklyMaxVolume == 0)
                return;
            
            float profileWidth = rightEdge - leftEdge;
            
            SharpDX.Direct2D1.Brush pocBrush = null;
            SharpDX.Direct2D1.Brush vaBrush = null;
            SharpDX.Direct2D1.Brush regularBrush = null;
            
            try
            {
                pocBrush = WeeklyPoCColor.ToDxBrush(RenderTarget);
                vaBrush = WeeklyVAColor.Clone().ToDxBrush(RenderTarget);
                vaBrush.Opacity = WeeklyBarOpacity / 100.0f;
                regularBrush = WeeklyBarColor.Clone().ToDxBrush(RenderTarget);
                regularBrush.Opacity = WeeklyBarOpacity / 100.0f;
                
                // Draw volume bars
                for (int i = 0; i < weeklyVolumes.Count; i++)
                {
                    double vol = weeklyVolumes[i];
                    if (vol <= 0)
                        continue;
                    
                    double priceLevel = weeklyLowestPrice + weeklyPriceInterval * i;
                    float y = chartScale.GetYByValue(priceLevel);
                    
                    double volumeRatio = vol / weeklyMaxVolume;
                    float barWidth = (float)(volumeRatio * profileWidth);
                    
                    float barRight = rightEdge;
                    float barLeft = rightEdge - barWidth;
                    
                    bool isPOC = (i == weeklyMaxIndex && WeeklyDisplayPoC);
                    bool isVA = (WeeklyDisplayValueArea && i >= weeklyVADown && i <= weeklyVAUp);
                    
                    SharpDX.Direct2D1.Brush barBrush;
                    SharpDX.Direct2D1.LinearGradientBrush gradientBrush = null;
                    
                    if (EnableGradientFill)
                    {
                        // Determine which color to use for gradient
                        Brush sourceColor;
                        if (isPOC)
                            sourceColor = WeeklyPoCColor;
                        else if (isVA)
                            sourceColor = WeeklyVAColor;
                        else
                            sourceColor = WeeklyBarColor;
                        
                        gradientBrush = CreateGradientBrush(sourceColor, barLeft, barRight, y);
                        barBrush = gradientBrush ?? (isPOC ? pocBrush : (isVA ? vaBrush : regularBrush));
                    }
                    else
                    {
                        // No gradient - use solid brushes
                        if (isPOC)
                            barBrush = pocBrush;
                        else if (isVA)
                            barBrush = vaBrush;
                        else
                            barBrush = regularBrush;
                    }
                    
                    float gapSize = 1.0f;
                    float adjustedY = y + (gapSize / 2.0f);
                    float adjustedThickness = Math.Max(1, WeeklyBarThickness - gapSize);
                    
                    SharpDX.Vector2 startPoint = new SharpDX.Vector2(barLeft, adjustedY);
                    SharpDX.Vector2 endPoint = new SharpDX.Vector2(barRight, adjustedY);
                    RenderTarget.DrawLine(startPoint, endPoint, barBrush, adjustedThickness);
                    
                    gradientBrush?.Dispose();
                }
                
                // Draw POC line
if (WeeklyDisplayPoC && weeklyMaxIndex >= 0)
{
    SharpDX.Direct2D1.Brush pocLineBrush = WeeklyPoCColor.Clone().ToDxBrush(RenderTarget);
    pocLineBrush.Opacity = WeeklyPoCLineOpacity / 100.0f;
    
    double pocPrice = weeklyLowestPrice + weeklyPriceInterval * weeklyMaxIndex;
    float pocY = chartScale.GetYByValue(pocPrice);
    
    float pocStartX, pocEndX;
    if (WeeklyExtendPoCLine)
    {
        pocStartX = (float)chartControl.CanvasLeft;
        pocEndX = rightEdge;
    }
    else
    {
        double pocVolumeRatio = weeklyVolumes[weeklyMaxIndex] / weeklyMaxVolume;
        float pocBarWidth = (float)(pocVolumeRatio * profileWidth);
        pocStartX = rightEdge - pocBarWidth;
        pocEndX = rightEdge;
    }
    
    SharpDX.Vector2 pocStart = new SharpDX.Vector2(pocStartX, pocY);
    SharpDX.Vector2 pocEnd = new SharpDX.Vector2(pocEndX, pocY);
    RenderTarget.DrawLine(pocStart, pocEnd, pocLineBrush, WeeklyPoCLineThickness);
    
    pocLineBrush.Dispose();
}

// Draw weekly POC price label
if (WeeklyDisplayPoC && ShowPriceValuesInLabels && weeklyMaxIndex >= 0)
{
    double pocPrice = weeklyLowestPrice + weeklyPriceInterval * weeklyMaxIndex;
    string pocLabel = "POC " + pocPrice.ToString("F2");
    
    float pocY = chartScale.GetYByValue(pocPrice);
    float labelX = (float)chartControl.CanvasRight - 60;
    
    SharpDX.Direct2D1.SolidColorBrush textBrush = WeeklyPoCColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        var textRect = new SharpDX.RectangleF(labelX, pocY - 15, 60, 15);
        RenderTarget.DrawText(pocLabel, textFormat, textRect, textBrush);
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}
                
                /// Draw Value Area lines
if (WeeklyDisplayValueArea && WeeklyDisplayValueAreaLines)
{
    SharpDX.Direct2D1.Brush vaLinesBrush = WeeklyVALinesColor.Clone().ToDxBrush(RenderTarget);
    vaLinesBrush.Opacity = WeeklyValueAreaLinesOpacity / 100.0f;
    
    // VAH
    if (weeklyVAUp >= 0 && weeklyVAUp < weeklyVolumes.Count)
    {
        double vahPrice = weeklyLowestPrice + weeklyPriceInterval * weeklyVAUp;
        float vahY = chartScale.GetYByValue(vahPrice);
        
        float vahStartX, vahEndX;
        if (WeeklyExtendValueAreaLines)
        {
            vahStartX = (float)chartControl.CanvasLeft;
            vahEndX = rightEdge;
        }
        else
        {
            double vahVolumeRatio = weeklyVolumes[weeklyVAUp] / weeklyMaxVolume;
            float vahBarWidth = (float)(vahVolumeRatio * profileWidth);
            vahStartX = rightEdge - vahBarWidth;
            vahEndX = rightEdge;
        }
        
        SharpDX.Vector2 vahStart = new SharpDX.Vector2(vahStartX, vahY);
        SharpDX.Vector2 vahEnd = new SharpDX.Vector2(vahEndX, vahY);
        RenderTarget.DrawLine(vahStart, vahEnd, vaLinesBrush, WeeklyValueAreaLinesThickness);
    }
    
    // VAL
    if (weeklyVADown >= 0 && weeklyVADown < weeklyVolumes.Count)
    {
        double valPrice = weeklyLowestPrice + weeklyPriceInterval * weeklyVADown;
        float valY = chartScale.GetYByValue(valPrice);
        
        float valStartX, valEndX;
        if (WeeklyExtendValueAreaLines)
        {
            valStartX = (float)chartControl.CanvasLeft;
            valEndX = rightEdge;
        }
        else
        {
            double valVolumeRatio = weeklyVolumes[weeklyVADown] / weeklyMaxVolume;
            float valBarWidth = (float)(valVolumeRatio * profileWidth);
            valStartX = rightEdge - valBarWidth;
            valEndX = rightEdge;
        }
        
        SharpDX.Vector2 valStart = new SharpDX.Vector2(valStartX, valY);
        SharpDX.Vector2 valEnd = new SharpDX.Vector2(valEndX, valY);
        RenderTarget.DrawLine(valStart, valEnd, vaLinesBrush, WeeklyValueAreaLinesThickness);
    }
    
    vaLinesBrush.Dispose();
}

// Draw weekly VAH and VAL price labels
if (WeeklyDisplayValueArea && WeeklyDisplayValueAreaLines && ShowPriceValuesInLabels)
{
    SharpDX.Direct2D1.SolidColorBrush textBrush = WeeklyVALinesColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        float labelX = (float)chartControl.CanvasRight - 60;
        
        // VAH label
        if (weeklyVAUp >= 0 && weeklyVAUp < weeklyVolumes.Count)
        {
            double vahPrice = weeklyLowestPrice + weeklyPriceInterval * weeklyVAUp;
            string vahLabel = "VAH " + vahPrice.ToString("F2");
            float vahY = chartScale.GetYByValue(vahPrice);
            var vahRect = new SharpDX.RectangleF(labelX, vahY - 15, 60, 15);
            RenderTarget.DrawText(vahLabel, textFormat, vahRect, textBrush);
        }
        
        // VAL label
        if (weeklyVADown >= 0 && weeklyVADown < weeklyVolumes.Count)
        {
            double valPrice = weeklyLowestPrice + weeklyPriceInterval * weeklyVADown;
            string valLabel = "VAL " + valPrice.ToString("F2");
            float valY = chartScale.GetYByValue(valPrice);
            var valRect = new SharpDX.RectangleF(labelX, valY - 15, 60, 15);
            RenderTarget.DrawText(valLabel, textFormat, valRect, textBrush);
        }
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}
            }
            finally
            {
                pocBrush?.Dispose();
                vaBrush?.Dispose();
                regularBrush?.Dispose();
            }
        }
        
        private void RenderSessionProfile(ChartControl chartControl, ChartScale chartScale, float leftEdge, float rightEdge)
        {
            if (sessionVolumes.Count == 0 || sessionMaxVolume == 0)
                return;
            
            float profileWidth = rightEdge - leftEdge;
            
            if (SessionProfileStyle == SessionProfileStyleEnum.Outline)
            {
                RenderSessionProfileSmooth(chartControl, chartScale, leftEdge, rightEdge, profileWidth);
            }
            else
            {
                // Filled style (similar to weekly)
                RenderSessionProfileFilled(chartControl, chartScale, leftEdge, rightEdge, profileWidth);
            }
        }
        
        private void RenderSessionProfileSmooth(ChartControl chartControl, ChartScale chartScale, float leftEdge, float rightEdge, float profileWidth)
        {
            SharpDX.Direct2D1.Brush outlineBrush = null;
            SharpDX.Direct2D1.PathGeometry pathGeometry = null;
            SharpDX.Direct2D1.GeometrySink sink = null;
            
            try
            {
                outlineBrush = SessionOutlineColor.Clone().ToDxBrush(RenderTarget);
                outlineBrush.Opacity = SessionBarOpacity / 100.0f;
                
                // Build list of points for the profile edge
                List<SharpDX.Vector2> rightEdgePoints = new List<SharpDX.Vector2>();
                
                for (int i = 0; i < sessionVolumes.Count; i++)
                {
                    double vol = sessionVolumes[i];
                    if (vol <= 0)
                        continue;
                    
                    double priceLevel = sessionLowestPrice + sessionPriceInterval * i;
                    float y = chartScale.GetYByValue(priceLevel);
                    
                    double volumeRatio = vol / sessionMaxVolume;
                    float barWidth = (float)(volumeRatio * profileWidth);
                    float x = rightEdge - barWidth;
                    
                    rightEdgePoints.Add(new SharpDX.Vector2(x, y));
                }
                
                if (rightEdgePoints.Count < 2)
                    return;
                
                // Create smooth path
                pathGeometry = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory);
                sink = pathGeometry.Open();
                
                sink.BeginFigure(rightEdgePoints[0], SharpDX.Direct2D1.FigureBegin.Hollow);
                
                // Apply smoothing based on SessionOutlineSmoothness (0-100)
                float smoothFactor = SessionOutlineSmoothness / 100.0f;
                
                for (int i = 1; i < rightEdgePoints.Count; i++)
                {
                    if (smoothFactor > 0.1f && i > 0 && i < rightEdgePoints.Count - 1)
                    {
                        // Calculate control points for Bezier curve
                        SharpDX.Vector2 p0 = rightEdgePoints[i - 1];
                        SharpDX.Vector2 p1 = rightEdgePoints[i];
                        SharpDX.Vector2 p2 = (i + 1 < rightEdgePoints.Count) ? rightEdgePoints[i + 1] : p1;
                        
                        // Control point 1: somewhere between p0 and p1
                        SharpDX.Vector2 cp1 = new SharpDX.Vector2(
                            p0.X + (p1.X - p0.X) * smoothFactor,
                            p0.Y + (p1.Y - p0.Y) * smoothFactor
                        );
                        
                        // Control point 2: somewhere between p1 and p2
                        SharpDX.Vector2 cp2 = new SharpDX.Vector2(
                            p1.X + (p2.X - p1.X) * (1 - smoothFactor),
                            p1.Y + (p2.Y - p1.Y) * (1 - smoothFactor)
                        );
                        
                        // Draw quadratic bezier
                        sink.AddQuadraticBezier(new SharpDX.Direct2D1.QuadraticBezierSegment
                        {
                            Point1 = cp1,
                            Point2 = p1
                        });
                    }
                    else
                    {
                        sink.AddLine(rightEdgePoints[i]);
                    }
                }
                
                sink.EndFigure(SharpDX.Direct2D1.FigureEnd.Open);
                sink.Close();
                
                // Draw the smooth path
                RenderTarget.DrawGeometry(pathGeometry, outlineBrush, 2.0f);
                
                // Draw POC line
if (SessionDisplayPoC && sessionMaxIndex >= 0)
{
    SharpDX.Direct2D1.Brush pocBrush = SessionPoCColor.Clone().ToDxBrush(RenderTarget);
    pocBrush.Opacity = SessionPoCLineOpacity / 100.0f;
    
    double pocPrice = sessionLowestPrice + sessionPriceInterval * sessionMaxIndex;
    float pocY = chartScale.GetYByValue(pocPrice);
    
    float pocStartX, pocEndX;
    if (SessionExtendPoCLine)
    {
        pocStartX = (float)chartControl.CanvasLeft;
        pocEndX = rightEdge;
    }
    else
    {
        double pocVolumeRatio = sessionVolumes[sessionMaxIndex] / sessionMaxVolume;
        float pocBarWidth = (float)(pocVolumeRatio * profileWidth);
        pocStartX = rightEdge - pocBarWidth;
        pocEndX = rightEdge;
    }
    
    SharpDX.Vector2 pocStart = new SharpDX.Vector2(pocStartX, pocY);
    SharpDX.Vector2 pocEnd = new SharpDX.Vector2(pocEndX, pocY);
    RenderTarget.DrawLine(pocStart, pocEnd, pocBrush, SessionPoCLineThickness);
    
    pocBrush.Dispose();
}

// Draw session POC price label
if (SessionDisplayPoC && ShowPriceValuesInLabels && sessionMaxIndex >= 0)
{
    double pocPrice = sessionLowestPrice + sessionPriceInterval * sessionMaxIndex;
    string pocLabel = "POC " + pocPrice.ToString("F2");
    
    float pocY = chartScale.GetYByValue(pocPrice);
    float weeklyRight = (float)chartControl.CanvasRight;
    float sessionRight = weeklyRight - WeeklyProfileWidth - ProfileGap;
    float labelX = sessionRight - 60;
    
    SharpDX.Direct2D1.SolidColorBrush textBrush = SessionPoCColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        var textRect = new SharpDX.RectangleF(labelX, pocY - 15, 60, 15);
        RenderTarget.DrawText(pocLabel, textFormat, textRect, textBrush);
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}
                
                // Draw VA lines
if (SessionDisplayValueArea && SessionDisplayValueAreaLines)
{
    SharpDX.Direct2D1.Brush vaBrush = SessionVALinesColor.Clone().ToDxBrush(RenderTarget);
    vaBrush.Opacity = SessionValueAreaLinesOpacity / 100.0f;
    
    // VAH
    if (sessionVAUp >= 0 && sessionVAUp < sessionVolumes.Count)
    {
        double vahPrice = sessionLowestPrice + sessionPriceInterval * sessionVAUp;
        float vahY = chartScale.GetYByValue(vahPrice);
        
        float vahStartX, vahEndX;
        if (SessionExtendValueAreaLines)
        {
            vahStartX = (float)chartControl.CanvasLeft;
            vahEndX = rightEdge;
        }
        else
        {
            double vahVolumeRatio = sessionVolumes[sessionVAUp] / sessionMaxVolume;
            float vahBarWidth = (float)(vahVolumeRatio * profileWidth);
            vahStartX = rightEdge - vahBarWidth;
            vahEndX = rightEdge;
        }
        
        SharpDX.Vector2 vahStart = new SharpDX.Vector2(vahStartX, vahY);
        SharpDX.Vector2 vahEnd = new SharpDX.Vector2(vahEndX, vahY);
        RenderTarget.DrawLine(vahStart, vahEnd, vaBrush, SessionValueAreaLinesThickness);
    }
    
    // VAL
    if (sessionVADown >= 0 && sessionVADown < sessionVolumes.Count)
    {
        double valPrice = sessionLowestPrice + sessionPriceInterval * sessionVADown;
        float valY = chartScale.GetYByValue(valPrice);
        
        float valStartX, valEndX;
        if (SessionExtendValueAreaLines)
        {
            valStartX = (float)chartControl.CanvasLeft;
            valEndX = rightEdge;
        }
        else
        {
            double valVolumeRatio = sessionVolumes[sessionVADown] / sessionMaxVolume;
            float valBarWidth = (float)(valVolumeRatio * profileWidth);
            valStartX = rightEdge - valBarWidth;
            valEndX = rightEdge;
        }
        
        SharpDX.Vector2 valStart = new SharpDX.Vector2(valStartX, valY);
        SharpDX.Vector2 valEnd = new SharpDX.Vector2(valEndX, valY);
        RenderTarget.DrawLine(valStart, valEnd, vaBrush, SessionValueAreaLinesThickness);
    }
    
    vaBrush.Dispose();
}

// Draw session VAH and VAL price labels
if (SessionDisplayValueArea && SessionDisplayValueAreaLines && ShowPriceValuesInLabels)
{
    SharpDX.Direct2D1.SolidColorBrush textBrush = SessionVALinesColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        float weeklyRight = (float)chartControl.CanvasRight;
        float sessionRight = weeklyRight - WeeklyProfileWidth - ProfileGap;
        float labelX = sessionRight - 60;
        
        // VAH label
        if (sessionVAUp >= 0 && sessionVAUp < sessionVolumes.Count)
        {
            double vahPrice = sessionLowestPrice + sessionPriceInterval * sessionVAUp;
            string vahLabel = "VAH " + vahPrice.ToString("F2");
            float vahY = chartScale.GetYByValue(vahPrice);
            var vahRect = new SharpDX.RectangleF(labelX, vahY - 10, 100, 20);
            RenderTarget.DrawText(vahLabel, textFormat, vahRect, textBrush);
        }
        
        // VAL label
        if (sessionVADown >= 0 && sessionVADown < sessionVolumes.Count)
        {
            double valPrice = sessionLowestPrice + sessionPriceInterval * sessionVADown;
            string valLabel = "VAL " + valPrice.ToString("F2");
            float valY = chartScale.GetYByValue(valPrice);
            var valRect = new SharpDX.RectangleF(labelX, valY - 10, 100, 20);
            RenderTarget.DrawText(valLabel, textFormat, valRect, textBrush);
        }
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}
            }
            finally
            {
                sink?.Dispose();
                pathGeometry?.Dispose();
                outlineBrush?.Dispose();
            }
        }
        
        private void RenderSessionProfileFilled(ChartControl chartControl, ChartScale chartScale, float leftEdge, float rightEdge, float profileWidth)
        {
            // Same as weekly rendering but with session colors
            SharpDX.Direct2D1.Brush pocBrush = null;
            SharpDX.Direct2D1.Brush vaBrush = null;
            SharpDX.Direct2D1.Brush regularBrush = null;
            
            try
            {
                pocBrush = SessionPoCColor.ToDxBrush(RenderTarget);
                vaBrush = SessionVAColor.Clone().ToDxBrush(RenderTarget);
                vaBrush.Opacity = SessionBarOpacity / 100.0f;
                regularBrush = SessionOutlineColor.Clone().ToDxBrush(RenderTarget);
                regularBrush.Opacity = SessionBarOpacity / 100.0f;
                
                for (int i = 0; i < sessionVolumes.Count; i++)
                {
                    double vol = sessionVolumes[i];
                    if (vol <= 0)
                        continue;
                    
                    double priceLevel = sessionLowestPrice + sessionPriceInterval * i;
                    float y = chartScale.GetYByValue(priceLevel);
                    
                    double volumeRatio = vol / sessionMaxVolume;
                    float barWidth = (float)(volumeRatio * profileWidth);
                    
                    float barRight = rightEdge;
                    float barLeft = rightEdge - barWidth;
                    
                    bool isPOC = (i == sessionMaxIndex && SessionDisplayPoC);
                    bool isVA = (SessionDisplayValueArea && i >= sessionVADown && i <= sessionVAUp);
                    
                    SharpDX.Direct2D1.Brush barBrush;
                    SharpDX.Direct2D1.LinearGradientBrush gradientBrush = null;
                    
                    if (EnableGradientFill)
                    {
                        // Determine which color to use for gradient
                        Brush sourceColor;
                        if (isPOC)
                            sourceColor = SessionPoCColor;
                        else if (isVA)
                            sourceColor = SessionVAColor;
                        else
                            sourceColor = SessionOutlineColor;
                        
                        gradientBrush = CreateGradientBrush(sourceColor, barLeft, barRight, y);
                        barBrush = gradientBrush ?? (isPOC ? pocBrush : (isVA ? vaBrush : regularBrush));
                    }
                    else
                    {
                        // No gradient - use solid brushes
                        if (isPOC)
                            barBrush = pocBrush;
                        else if (isVA)
                            barBrush = vaBrush;
                        else
                            barBrush = regularBrush;
                    }
                    
                    float gapSize = 1.0f;
                    float adjustedY = y + (gapSize / 2.0f);
                    float adjustedThickness = Math.Max(1, SessionBarThickness - gapSize);
                    
                    SharpDX.Vector2 startPoint = new SharpDX.Vector2(barLeft, adjustedY);
                    SharpDX.Vector2 endPoint = new SharpDX.Vector2(barRight, adjustedY);
                    RenderTarget.DrawLine(startPoint, endPoint, barBrush, adjustedThickness);
                    
                    gradientBrush?.Dispose();
                }
                
                // Draw POC line (for filled mode)
if (SessionDisplayPoC && sessionMaxIndex >= 0)
{
    SharpDX.Direct2D1.Brush pocLineBrush = SessionPoCColor.Clone().ToDxBrush(RenderTarget);
    pocLineBrush.Opacity = SessionPoCLineOpacity / 100.0f;
    
    double pocPrice = sessionLowestPrice + sessionPriceInterval * sessionMaxIndex;
    float pocY = chartScale.GetYByValue(pocPrice);
    
    float pocStartX, pocEndX;
    if (SessionExtendPoCLine)
    {
        pocStartX = (float)chartControl.CanvasLeft;
        pocEndX = rightEdge;
    }
    else
    {
        double pocVolumeRatio = sessionVolumes[sessionMaxIndex] / sessionMaxVolume;
        float pocBarWidth = (float)(pocVolumeRatio * profileWidth);
        pocStartX = rightEdge - pocBarWidth;
        pocEndX = rightEdge;
    }
    
    SharpDX.Vector2 pocStart = new SharpDX.Vector2(pocStartX, pocY);
    SharpDX.Vector2 pocEnd = new SharpDX.Vector2(pocEndX, pocY);
    RenderTarget.DrawLine(pocStart, pocEnd, pocLineBrush, SessionPoCLineThickness);
    
    pocLineBrush.Dispose();
}

// Draw session POC price label (filled mode)
if (SessionDisplayPoC && ShowPriceValuesInLabels && sessionMaxIndex >= 0)
{
    double pocPrice = sessionLowestPrice + sessionPriceInterval * sessionMaxIndex;
    string pocLabel = "POC " + pocPrice.ToString("F2");
    
    float pocY = chartScale.GetYByValue(pocPrice);
    float weeklyRight = (float)chartControl.CanvasRight;
    float sessionRight = weeklyRight - WeeklyProfileWidth - ProfileGap;
    float labelX = sessionRight - 60;
    
    SharpDX.Direct2D1.SolidColorBrush textBrush = SessionPoCColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        var textRect = new SharpDX.RectangleF(labelX, pocY - 15, 60, 15);
        RenderTarget.DrawText(pocLabel, textFormat, textRect, textBrush);
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}
                
                // Draw VA lines (for filled mode)
if (SessionDisplayValueArea && SessionDisplayValueAreaLines)
{
    SharpDX.Direct2D1.Brush vaLinesBrush = SessionVALinesColor.Clone().ToDxBrush(RenderTarget);
    vaLinesBrush.Opacity = SessionValueAreaLinesOpacity / 100.0f;
    
    // VAH
    if (sessionVAUp >= 0 && sessionVAUp < sessionVolumes.Count)
    {
        double vahPrice = sessionLowestPrice + sessionPriceInterval * sessionVAUp;
        float vahY = chartScale.GetYByValue(vahPrice);
        
        float vahStartX, vahEndX;
        if (SessionExtendValueAreaLines)
        {
            vahStartX = (float)chartControl.CanvasLeft;
            vahEndX = rightEdge;
        }
        else
        {
            double vahVolumeRatio = sessionVolumes[sessionVAUp] / sessionMaxVolume;
            float vahBarWidth = (float)(vahVolumeRatio * profileWidth);
            vahStartX = rightEdge - vahBarWidth;
            vahEndX = rightEdge;
        }
        
        SharpDX.Vector2 vahStart = new SharpDX.Vector2(vahStartX, vahY);
        SharpDX.Vector2 vahEnd = new SharpDX.Vector2(vahEndX, vahY);
        RenderTarget.DrawLine(vahStart, vahEnd, vaLinesBrush, SessionValueAreaLinesThickness);
    }
    
    // VAL
    if (sessionVADown >= 0 && sessionVADown < sessionVolumes.Count)
    {
        double valPrice = sessionLowestPrice + sessionPriceInterval * sessionVADown;
        float valY = chartScale.GetYByValue(valPrice);
        
        float valStartX, valEndX;
        if (SessionExtendValueAreaLines)
        {
            valStartX = (float)chartControl.CanvasLeft;
            valEndX = rightEdge;
        }
        else
        {
            double valVolumeRatio = sessionVolumes[sessionVADown] / sessionMaxVolume;
            float valBarWidth = (float)(valVolumeRatio * profileWidth);
            valStartX = rightEdge - valBarWidth;
            valEndX = rightEdge;
        }
        
        SharpDX.Vector2 valStart = new SharpDX.Vector2(valStartX, valY);
        SharpDX.Vector2 valEnd = new SharpDX.Vector2(valEndX, valY);
        RenderTarget.DrawLine(valStart, valEnd, vaLinesBrush, SessionValueAreaLinesThickness);
    }
    
    vaLinesBrush.Dispose();
}

// Draw session VAH and VAL price labels (filled mode)
if (SessionDisplayValueArea && SessionDisplayValueAreaLines && ShowPriceValuesInLabels)
{
    SharpDX.Direct2D1.SolidColorBrush textBrush = SessionVALinesColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        float weeklyRight = (float)chartControl.CanvasRight;
        float sessionRight = weeklyRight - WeeklyProfileWidth - ProfileGap;
        float labelX = sessionRight - 60;
        
        // VAH label
        if (sessionVAUp >= 0 && sessionVAUp < sessionVolumes.Count)
        {
            double vahPrice = sessionLowestPrice + sessionPriceInterval * sessionVAUp;
            string vahLabel = "VAH " + vahPrice.ToString("F2");
            float vahY = chartScale.GetYByValue(vahPrice);
            var vahRect = new SharpDX.RectangleF(labelX, vahY - 15, 60, 15);
            RenderTarget.DrawText(vahLabel, textFormat, vahRect, textBrush);
        }
        
        // VAL label
        if (sessionVADown >= 0 && sessionVADown < sessionVolumes.Count)
        {
            double valPrice = sessionLowestPrice + sessionPriceInterval * sessionVADown;
            string valLabel = "VAL " + valPrice.ToString("F2");
            float valY = chartScale.GetYByValue(valPrice);
            var valRect = new SharpDX.RectangleF(labelX, valY - 15, 60, 15);
            RenderTarget.DrawText(valLabel, textFormat, valRect, textBrush);
        }
        
        // VAL label
        if (sessionVADown >= 0 && sessionVADown < sessionVolumes.Count)
        {
            double valPrice = sessionLowestPrice + sessionPriceInterval * sessionVADown;
            string valLabel = "VAL " + valPrice.ToString("F2");
            float valY = chartScale.GetYByValue(valPrice);
            var valRect = new SharpDX.RectangleF(labelX, valY - 15, 60, 15);
            RenderTarget.DrawText(valLabel, textFormat, valRect, textBrush);
        }
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}
            }
            finally
            {
                pocBrush?.Dispose();
                vaBrush?.Dispose();
                regularBrush?.Dispose();
            }
        }
        
        #endregion
        
        // NEW METHOD: Render a single anchored profile
        private void RenderAnchoredProfile(ChartControl chartControl, ChartScale chartScale, SessionProfile profile)
        {
            if (profile == null || profile.Volumes.Count == 0 || profile.MaxVolume == 0)
                return;

            try
            {
                // Get X coordinates for session start and end bars
                float sessionStartX = chartControl.GetXByBarIndex(ChartBars, profile.StartBarIndex);
                float sessionEndX = chartControl.GetXByBarIndex(ChartBars, profile.EndBarIndex);
                
                // Profile spans from session start to session end
                float profileWidthPixels = sessionEndX - sessionStartX;
                
                if (profileWidthPixels <= 0)
                    return;

                float leftEdge = sessionStartX;
                float rightEdge = sessionEndX;

                SharpDX.Direct2D1.Brush pocBrush = null;
                SharpDX.Direct2D1.Brush vaBrush = null;
                SharpDX.Direct2D1.Brush regularBrush = null;

                try
                {
                    pocBrush = PoCLineColor.ToDxBrush(RenderTarget);
                    vaBrush = ValueAreaBarColor.Clone().ToDxBrush(RenderTarget);
                    vaBrush.Opacity = BarOpacity / 100.0f;
                    regularBrush = BarColor.Clone().ToDxBrush(RenderTarget);
                    regularBrush.Opacity = BarOpacity / 100.0f;

                    // Draw volume bars
                    for (int i = 0; i < profile.Volumes.Count; i++)
                    {
                        double vol = profile.Volumes[i];
                        if (vol <= 0)
                            continue;

                        double priceLevel = profile.LowestPrice + profile.PriceInterval * i;
                        float y = chartScale.GetYByValue(priceLevel);

                        double volumeRatio = vol / profile.MaxVolume;
                        float barWidth = (float)(volumeRatio * profileWidthPixels);
                        
                        float barLeft = leftEdge;
                        float barRight = leftEdge + barWidth;

                        SharpDX.Direct2D1.Brush barBrush;
                        if (i == profile.POCIndex && DisplayPoC)
                        {
                            barBrush = pocBrush;
                        }
                        else if (DisplayValueArea && i >= profile.VADownIndex && i <= profile.VAUpIndex)
                        {
                            barBrush = vaBrush;
                        }
                        else
                        {
                            barBrush = regularBrush;
                        }

                        float gapSize = 1.0f;
                        float adjustedY = y + (gapSize / 2.0f);
                        float adjustedThickness = Math.Max(1, BarThickness - gapSize);

                        SharpDX.Vector2 startPoint = new SharpDX.Vector2(barLeft, adjustedY);
                        SharpDX.Vector2 endPoint = new SharpDX.Vector2(barRight, adjustedY);
                        RenderTarget.DrawLine(startPoint, endPoint, barBrush, adjustedThickness);
                    }

                    // Draw POC line (only extends through this session)
                    if (DisplayPoC && profile.POCIndex >= 0 && profile.POCIndex < profile.Volumes.Count)
                    {
                        SharpDX.Direct2D1.Brush pocLineBrush = null;
                        SharpDX.Direct2D1.StrokeStyle pocStrokeStyle = null;
                        
                        try
                        {
                            pocLineBrush = PoCLineColor.Clone().ToDxBrush(RenderTarget);
                            pocLineBrush.Opacity = PoCLineOpacity / 100.0f;

                            if (PoCLineStyle != DashStyleHelper.Solid)
                            {
                                var factory = RenderTarget.Factory;
                                var strokeStyleProperties = new SharpDX.Direct2D1.StrokeStyleProperties();
                                
                                if (PoCLineStyle == DashStyleHelper.Dash)
                                    strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.Dash;
                                else if (PoCLineStyle == DashStyleHelper.Dot)
                                    strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.Dot;
                                else if (PoCLineStyle == DashStyleHelper.DashDot)
                                    strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.DashDot;
                                else if (PoCLineStyle == DashStyleHelper.DashDotDot)
                                    strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.DashDotDot;
                                
                                pocStrokeStyle = new SharpDX.Direct2D1.StrokeStyle(factory, strokeStyleProperties);
                            }

                            double pocPrice = profile.LowestPrice + profile.PriceInterval * profile.POCIndex;
                            float pocY = chartScale.GetYByValue(pocPrice);
                            
                            float pocStartX = leftEdge;
                            float pocEndX = rightEdge;
                            
                            SharpDX.Vector2 pocStart = new SharpDX.Vector2(pocStartX, pocY);
                            SharpDX.Vector2 pocEnd = new SharpDX.Vector2(pocEndX, pocY);
                            
                            if (pocStrokeStyle != null)
                                RenderTarget.DrawLine(pocStart, pocEnd, pocLineBrush, PoCLineThickness, pocStrokeStyle);
                            else
                                RenderTarget.DrawLine(pocStart, pocEnd, pocLineBrush, PoCLineThickness);
                        }
                        finally
                        {
                            pocLineBrush?.Dispose();
                            pocStrokeStyle?.Dispose();
                        }
                    }
					
					// Draw POC price label for anchored profile
if (DisplayPoC && ShowPriceValuesInLabels && profile.POCIndex >= 0 && profile.POCIndex < profile.Volumes.Count)
{
    double pocPrice = profile.LowestPrice + profile.PriceInterval * profile.POCIndex;
    string pocLabel = "POC " + pocPrice.ToString("F2");
    
    float pocY = chartScale.GetYByValue(pocPrice);
    float labelX = (float)chartControl.CanvasLeft;
    
    SharpDX.Direct2D1.SolidColorBrush textBrush = PoCLineColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        var textRect = new SharpDX.RectangleF(labelX, pocY - 15, 60, 15);
        RenderTarget.DrawText(pocLabel, textFormat, textRect, textBrush);
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}

                    // Draw Value Area lines (only extend through this session)
                    if (DisplayValueArea && DisplayValueAreaLines)
                    {
                        SharpDX.Direct2D1.Brush vaLinesBrush = null;
                        SharpDX.Direct2D1.StrokeStyle strokeStyle = null;
                        
                        try
                        {
                            vaLinesBrush = ValueAreaLinesColor.Clone().ToDxBrush(RenderTarget);
                            vaLinesBrush.Opacity = ValueAreaLinesOpacity / 100.0f;

                            if (ValueAreaLineStyle != DashStyleHelper.Solid)
                            {
                                var factory = RenderTarget.Factory;
                                var strokeStyleProperties = new SharpDX.Direct2D1.StrokeStyleProperties();
                                
                                if (ValueAreaLineStyle == DashStyleHelper.Dash)
                                    strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.Dash;
                                else if (ValueAreaLineStyle == DashStyleHelper.Dot)
                                    strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.Dot;
                                else if (ValueAreaLineStyle == DashStyleHelper.DashDot)
                                    strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.DashDot;
                                else if (ValueAreaLineStyle == DashStyleHelper.DashDotDot)
                                    strokeStyleProperties.DashStyle = SharpDX.Direct2D1.DashStyle.DashDotDot;
                                
                                strokeStyle = new SharpDX.Direct2D1.StrokeStyle(factory, strokeStyleProperties);
                            }

                            // VAH line
                            if (profile.VAUpIndex >= 0 && profile.VAUpIndex < profile.Volumes.Count)
                            {
                                double vahPrice = profile.LowestPrice + profile.PriceInterval * profile.VAUpIndex;
                                float vahY = chartScale.GetYByValue(vahPrice);
                                
                                float vahStartX = leftEdge;
                                float vahEndX = rightEdge;
                                
                                SharpDX.Vector2 vahStart = new SharpDX.Vector2(vahStartX, vahY);
                                SharpDX.Vector2 vahEnd = new SharpDX.Vector2(vahEndX, vahY);
                                
                                if (strokeStyle != null)
                                    RenderTarget.DrawLine(vahStart, vahEnd, vaLinesBrush, ValueAreaLinesThickness, strokeStyle);
                                else
                                    RenderTarget.DrawLine(vahStart, vahEnd, vaLinesBrush, ValueAreaLinesThickness);
                            }

                            // VAL line
                            if (profile.VADownIndex >= 0 && profile.VADownIndex < profile.Volumes.Count)
                            {
                                double valPrice = profile.LowestPrice + profile.PriceInterval * profile.VADownIndex;
                                float valY = chartScale.GetYByValue(valPrice);
                                
                                float valStartX = leftEdge;
                                float valEndX = rightEdge;
                                
                                SharpDX.Vector2 valStart = new SharpDX.Vector2(valStartX, valY);
                                SharpDX.Vector2 valEnd = new SharpDX.Vector2(valEndX, valY);
                                
                                if (strokeStyle != null)
                                    RenderTarget.DrawLine(valStart, valEnd, vaLinesBrush, ValueAreaLinesThickness, strokeStyle);
                                else
                                    RenderTarget.DrawLine(valStart, valEnd, vaLinesBrush, ValueAreaLinesThickness);
                            }
                        }
                        finally
                        {
                            vaLinesBrush?.Dispose();
                            strokeStyle?.Dispose();
                        }
                    }
					
					// Draw VAH and VAL price labels for anchored profile
if (DisplayValueArea && DisplayValueAreaLines && ShowPriceValuesInLabels)
{
    SharpDX.Direct2D1.SolidColorBrush textBrush = ValueAreaLinesColor.Clone().ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
    if (textBrush != null)
    {
        var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 9);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
        
        float labelX = (float)chartControl.CanvasLeft;
        
        // VAH label
        if (profile.VAUpIndex >= 0 && profile.VAUpIndex < profile.Volumes.Count)
        {
            double vahPrice = profile.LowestPrice + profile.PriceInterval * profile.VAUpIndex;
            string vahLabel = "VAH " + vahPrice.ToString("F2");
            float vahY = chartScale.GetYByValue(vahPrice);
            var vahRect = new SharpDX.RectangleF(labelX, vahY - 15, 60, 15);
            RenderTarget.DrawText(vahLabel, textFormat, vahRect, textBrush);
        }
        
        // VAL label
        if (profile.VADownIndex >= 0 && profile.VADownIndex < profile.Volumes.Count)
        {
            double valPrice = profile.LowestPrice + profile.PriceInterval * profile.VADownIndex;
            string valLabel = "VAL " + valPrice.ToString("F2");
            float valY = chartScale.GetYByValue(valPrice);
            var valRect = new SharpDX.RectangleF(labelX, valY - 15, 60, 15);
            RenderTarget.DrawText(valLabel, textFormat, valRect, textBrush);
        }
        
        textFormat.Dispose();
        textBrush.Dispose();
    }
}
                }
                finally
                {
                    pocBrush?.Dispose();
                    vaBrush?.Dispose();
                    regularBrush?.Dispose();
                }
            }
            catch
            {
                // Silently handle rendering errors
            }
        }
		
		// Check if we need to calculate overnight levels
private void CheckForOvernightLevels()
{
    if ((!DisplayOvernightPOC && !DisplayOvernightVAH && !DisplayOvernightVAL && !DisplayOvernightHigh && !DisplayOvernightLow) || CurrentBar < 1)
        return;
    
    try
    {
        DateTime barTime = Time[0];
        
        // Parse overnight session times
        int startHour = OvernightStartTime / 100;
        int startMinute = OvernightStartTime % 100;
        int endHour = OvernightEndTime / 100;
        int endMinute = OvernightEndTime % 100;
        
        // Determine which day's overnight session this bar belongs to
        DateTime overnightDate;
        
        // If we're at/after the start time, we belong to TOMORROW's overnight session
        if (barTime.Hour > startHour || (barTime.Hour == startHour && barTime.Minute >= startMinute))
        {
            overnightDate = barTime.Date.AddDays(1);
        }
        // If we're before the end time, we belong to TODAY's overnight session
        else if (barTime.Hour < endHour || (barTime.Hour == endHour && barTime.Minute < endMinute))
        {
            overnightDate = barTime.Date;
        }
        // If we're at/after the end time, the overnight session has ended
        else
        {
            overnightDate = barTime.Date;
            
            // Calculate overnight levels if we haven't yet for this date
            if (overnightSessionDate != overnightDate)
            {
                // Skip weekends
                if (overnightDate.DayOfWeek != DayOfWeek.Saturday && 
                    overnightDate.DayOfWeek != DayOfWeek.Sunday)
                {
                    CalculateOvernightProfile(overnightDate);
                }
            }
        }
    }
    catch
    {
        // Silently handle errors
    }
}
        
        // NEW HELPER METHOD: Get previous trading day (skip weekends)
        private DateTime GetPreviousTradingDay(DateTime currentDate)
        {
            DateTime previousDay = currentDate.AddDays(-1);
            
            // Skip Saturday and Sunday
            while (previousDay.DayOfWeek == DayOfWeek.Saturday || previousDay.DayOfWeek == DayOfWeek.Sunday)
            {
                previousDay = previousDay.AddDays(-1);
            }
            
            return previousDay;
        }
        
        // NEW HELPER METHOD: Get next trading day (skip weekends)
        private DateTime GetNextTradingDay(DateTime currentDate)
        {
            DateTime nextDay = currentDate.AddDays(1);
            
            // Skip Saturday and Sunday
            while (nextDay.DayOfWeek == DayOfWeek.Saturday || nextDay.DayOfWeek == DayOfWeek.Sunday)
            {
                nextDay = nextDay.AddDays(1);
            }
            
            return nextDay;
        }
		
		// NEW METHOD: Check if current bar touches any naked levels
private void CheckNakedLevelTouches()
{
    if (historicalLevels.Count == 0)
        return;
    
    double currentHigh = High[0];
    double currentLow = Low[0];
    double tickSize = TickSize;
    double tolerance = PRICE_TOUCH_TOLERANCE * tickSize;
    
    // Get current session for touch tracking
    DateTime currentSession = GetSessionDateForBar(Time[0]);
    
    // Check all historical levels for touches
    foreach (var kvp in historicalLevels.ToList())
    {
        DateTime date = kvp.Key;
        DayLevels levels = kvp.Value;
        
        // Skip if this is the current processing date (not yet complete)
        if (date == currentProcessingDate)
            continue;
        
        // Check POC touch - track touches across sessions
if (levels.POCNaked)  // Remove the !levels.POCFilled check
{
    if ((currentLow - tolerance) <= levels.POC && (currentHigh + tolerance) >= levels.POC)
    {
        // Mark as filled on first touch
        if (!levels.POCFilled)
        {
            levels.POCFilled = true;
        }
        
        // Track touches per session
        if (levels.POCLastTouchSession != currentSession)
                        {
                            levels.POCTouchCount++;
                            levels.POCLastTouchSession = currentSession;
                        }
    }
}
        
        // Check VAH touch - track touches across sessions
if (levels.VAHNaked)  // Remove the !levels.VAHFilled check
{
    if ((currentLow - tolerance) <= levels.VAH && (currentHigh + tolerance) >= levels.VAH)
    {
        // Mark as filled on first touch
        if (!levels.VAHFilled)
        {
            levels.VAHFilled = true;
        }
        
        // Track touches per session
        if (levels.VAHLastTouchSession != currentSession)
                        {
                            levels.VAHTouchCount++;
                            levels.VAHLastTouchSession = currentSession;
                        }
    }
}
        
        // Check VAL touch - track touches across sessions
if (levels.VALNaked)
{
    if ((currentLow - tolerance) <= levels.VAL && (currentHigh + tolerance) >= levels.VAL)
    {
        // Mark as filled on first touch
        if (!levels.VALFilled)
        {
            levels.VALFilled = true;
        }
        
        // Track touches per session
        if (levels.VALLastTouchSession != currentSession)
                        {
                            levels.VALTouchCount++;
                            levels.VALLastTouchSession = currentSession;
                        }
    }
}
    }
}
        
        private void UpdateDailyLevels()
        {
            DateTime barTime = Time[0];
            
            // Determine which SESSION this bar belongs to
            // Bars from 6:00 PM to 11:59 PM belong to NEXT day's session
            // Bars from 12:00 AM to 5:00 PM belong to TODAY's session
            DateTime sessionDate;
            if (barTime.Hour >= 18) // 6 PM or later
            {
                // This bar is part of tomorrow's session
                sessionDate = barTime.Date.AddDays(1);
            }
            else if (barTime.Hour < 18) // Before 6 PM
            {
                // This bar is part of today's session
                sessionDate = barTime.Date;
            }
            else
            {
                sessionDate = barTime.Date;
            }
            
            // CRITICAL: Skip weekends entirely - don't process Saturday or Sunday sessions
            if (sessionDate.DayOfWeek == DayOfWeek.Saturday || sessionDate.DayOfWeek == DayOfWeek.Sunday)
            {
                // Don't update currentProcessingDate for weekends
                return;
            }
            
            if (sessionDate != currentProcessingDate)
            {
                if (currentProcessingDate != DateTime.MinValue)
                {
                    // Only calculate if the previous date wasn't a weekend
                    if (currentProcessingDate.DayOfWeek != DayOfWeek.Saturday && 
                        currentProcessingDate.DayOfWeek != DayOfWeek.Sunday)
                    {
                        // CRITICAL: Only calculate if we haven't already calculated this date
                        if (!historicalLevels.ContainsKey(currentProcessingDate))
                        {
                            
                            CalculateFullDayProfile(currentProcessingDate);
                        }
                        // If already calculated, CalculateFullDayProfile will skip it
                        
                        if (historicalLevels.ContainsKey(currentProcessingDate))
                        {
                            // Remove old drawing objects before updating to new day
                            if (previousDayLevels != null)
                            {
                                string oldDateTag = previousDayLevels.Date.ToString("yyyyMMdd");
                                RemoveDrawObject("pdPOC_" + oldDateTag);
                                RemoveDrawObject("pdPOC_Label_" + oldDateTag);
                                RemoveDrawObject("pdVAH_" + oldDateTag);
                                RemoveDrawObject("pdVAH_Label_" + oldDateTag);
                                RemoveDrawObject("pdVAL_" + oldDateTag);
                                RemoveDrawObject("pdVAL_Label_" + oldDateTag);
                            }
                            
                            previousDayLevels = historicalLevels[currentProcessingDate];
                        }
                    }
                }
                
                currentProcessingDate = sessionDate;
				// Clean up filled levels from previous sessions at session end
                CleanupFilledLevels();
            }
        }
		
		// NEW METHOD: Draw naked levels from previous sessions
private void DrawNakedLevels()
{
    if (historicalLevels.Count == 0)
        return;
    
    // Get current session date
    DateTime currentSessionDate = GetSessionDateForBar(Time[0]);
    
    // Collect naked levels from historical sessions
    List<KeyValuePair<DateTime, DayLevels>> nakedLevelsList = new List<KeyValuePair<DateTime, DayLevels>>();
    
    foreach (var kvp in historicalLevels)
    {
        DateTime date = kvp.Key;
        DayLevels levels = kvp.Value;
        
        // Skip current processing date
        if (date == currentProcessingDate)
            continue;
        
        // Skip future dates
        if (date >= currentSessionDate)
            continue;
        
        // NEW: Skip the previous day levels (they're drawn separately if enabled)
        if (previousDayLevels != null && date == previousDayLevels.Date)
            continue;
        
        // Include if at least one level is still being tracked (naked OR filled)
        if (levels.POCNaked || levels.VAHNaked || levels.VALNaked)
        {
            nakedLevelsList.Add(kvp);
        }
    }
    
    // Sort by date descending (most recent first) and take max count
    var recentNakedLevels = nakedLevelsList
        .OrderByDescending(x => x.Key)
        .Take(MaxNakedLevelsToDisplay)
        .ToList();
    
    // Calculate line extent
    int rightExtent = 0;
    int leftExtent = PreviousDayLineWidth == 0 ? CurrentBar : Math.Min(CurrentBar, PreviousDayLineWidth);
    
    // Draw each naked level
    foreach (var kvp in recentNakedLevels)
    {
        DateTime date = kvp.Key;
        DayLevels levels = kvp.Value;
        string dateTag = date.ToString("yyyyMMdd");
        
// Draw POC (naked or filled - keep drawing until session end)
if (levels.POCNaked)  // This stays true until CleanupFilledLevels() at session end
{
    Brush nakedPOCBrush = NakedPOCColor.Clone();
    nakedPOCBrush.Opacity = NakedPOCOpacity / 100.0;
    
    Draw.Line(this, "nakedPOC_" + dateTag, false, 
        leftExtent, levels.POC, rightExtent, levels.POC,
        nakedPOCBrush, NakedPOCLineStyle, NakedPOCThickness);
    
    // Build label with optional touch count and price value
string labelText = ShowPriceValuesInLabels ? 
    "nPOC " + levels.POC.ToString("F2") + " " + FormatDateLabel(date) : 
    "nPOC " + FormatDateLabel(date);
    if (levels.POCFilled)
        labelText += " (filled)";
    if (ShowTouchCountInLabels && levels.POCTouchCount > 0)
        labelText += " " + levels.POCTouchCount + "x";
    
    Draw.Text(this, "nakedPOC_Label_" + dateTag, false, 
        labelText, 
        rightExtent - 5, levels.POC, 0, NakedPOCColor,
        new SimpleFont("Arial", 9), TextAlignment.Left,
        Brushes.Transparent, Brushes.Transparent, 0);
}
        
// Draw VAH (naked or filled - keep drawing until session end)
if (levels.VAHNaked)  // This stays true until CleanupFilledLevels() at session end
{
    Brush nakedVAHBrush = NakedVAHColor.Clone();
    nakedVAHBrush.Opacity = NakedVAHOpacity / 100.0;
    
    Draw.Line(this, "nakedVAH_" + dateTag, false,
        leftExtent, levels.VAH, rightExtent, levels.VAH,
        nakedVAHBrush, NakedVAHLineStyle, NakedVAHThickness);
    
    // Build label with optional touch count and price value
    string labelText = ShowPriceValuesInLabels ? 
        "nVAH " + levels.VAH.ToString("F2") + " " + FormatDateLabel(date) : 
        "nVAH " + FormatDateLabel(date);
    if (levels.VAHFilled)
        labelText += " (filled)";
    if (ShowTouchCountInLabels && levels.VAHTouchCount > 0)
        labelText += " " + levels.VAHTouchCount + "x";
    
    Draw.Text(this, "nakedVAH_Label_" + dateTag, false,
        labelText,
        rightExtent - 5, levels.VAH, 0, NakedVAHColor,
        new SimpleFont("Arial", 9), TextAlignment.Left,
        Brushes.Transparent, Brushes.Transparent, 0);
}
        
 // Draw VAL (naked or filled - keep drawing until session end)
if (levels.VALNaked)  // This stays true until CleanupFilledLevels() at session end
{
    Brush nakedVALBrush = NakedVALColor.Clone();
    nakedVALBrush.Opacity = NakedVALOpacity / 100.0;
    
    Draw.Line(this, "nakedVAL_" + dateTag, false,
        leftExtent, levels.VAL, rightExtent, levels.VAL,
        nakedVALBrush, NakedVALLineStyle, NakedVALThickness);
    
    /// Build label with optional touch count and price value
    string labelText = ShowPriceValuesInLabels ? 
        "nVAL " + levels.VAL.ToString("F2") + " " + FormatDateLabel(date) : 
        "nVAL " + FormatDateLabel(date);
    if (levels.VALFilled)
        labelText += " (filled)";
    if (ShowTouchCountInLabels && levels.VALTouchCount > 0)
        labelText += " " + levels.VALTouchCount + "x";
    
    Draw.Text(this, "nakedVAL_Label_" + dateTag, false,
        labelText,
        rightExtent - 5, levels.VAL, 0, NakedVALColor,
        new SimpleFont("Arial", 9), TextAlignment.Left,
        Brushes.Transparent, Brushes.Transparent, 0);
}
    }
}
		
// Check if current bar touches any weekly naked levels
private void CheckWeeklyNakedLevelTouches()
{
    if (historicalWeeklyLevels.Count == 0)
        return;
    
    double currentHigh = High[0];
    double currentLow = Low[0];
    double tickSize = TickSize;
    double tolerance = PRICE_TOUCH_TOLERANCE * tickSize;
    
    DateTime currentWeek = GetWeekStartForBar(Time[0]);
    
    foreach (var kvp in historicalWeeklyLevels.ToList())
    {
        DateTime weekDate = kvp.Key;
        WeekLevels levels = kvp.Value;
        
        if (weekDate == currentProcessingWeek)
            continue;
        
        if (weekDate >= currentWeek)
            continue;
        
        // Check POC touch - track touches across weeks
if (levels.POCNaked)
{
    if ((currentLow - tolerance) <= levels.POC && (currentHigh + tolerance) >= levels.POC)
    {
        // Mark as filled on first touch
        if (!levels.POCFilled)
        {
            levels.POCFilled = true;
        }
        
        // Track touches per week
        if (levels.POCLastTouchSession != currentWeek)
                        {
                            levels.POCTouchCount++;
                            levels.POCLastTouchSession = currentWeek;
                        }
    }
}
        
        // Check VAH touch - track touches across weeks
if (levels.VAHNaked)
{
    if ((currentLow - tolerance) <= levels.VAH && (currentHigh + tolerance) >= levels.VAH)
    {
        // Mark as filled on first touch
        if (!levels.VAHFilled)
        {
            levels.VAHFilled = true;
        }
        
        // Track touches per week
        if (levels.VAHLastTouchSession != currentWeek)
                        {
                            levels.VAHTouchCount++;
                            levels.VAHLastTouchSession = currentWeek;
                        }
    }
}
        
        // Check VAL touch - track touches across weeks
if (levels.VALNaked)
{
    if ((currentLow - tolerance) <= levels.VAL && (currentHigh + tolerance) >= levels.VAL)
    {
        // Mark as filled on first touch
        if (!levels.VALFilled)
        {
            levels.VALFilled = true;
        }
        
        // Track touches per week
        if (levels.VALLastTouchSession != currentWeek)
                        {
                            levels.VALTouchCount++;
                            levels.VALLastTouchSession = currentWeek;
                        }
    }
}
    }
}
		
		private void UpdateWeeklyLevels()
        {
            DateTime barTime = Time[0];
            DateTime weekStart = GetWeekStartForBar(barTime);
            
            if (weekStart.DayOfWeek == DayOfWeek.Saturday)
                return;
            
            if (weekStart != currentProcessingWeek)
            {
                if (currentProcessingWeek != DateTime.MinValue)
                {
                    if (currentProcessingWeek.DayOfWeek != DayOfWeek.Saturday)
                    {
                        if (!historicalWeeklyLevels.ContainsKey(currentProcessingWeek))
                            CalculateFullWeekProfile(currentProcessingWeek);
                        
                        if (historicalWeeklyLevels.ContainsKey(currentProcessingWeek))
                        {
                            if (previousWeekLevels != null)
                            {
                                string oldWeekTag = previousWeekLevels.WeekStartDate.ToString("yyyyMMdd");
                                RemoveDrawObject("weeklyNakedPOC_" + oldWeekTag);
                                RemoveDrawObject("weeklyNakedPOC_Label_" + oldWeekTag);
                                RemoveDrawObject("weeklyNakedVAH_" + oldWeekTag);
                                RemoveDrawObject("weeklyNakedVAH_Label_" + oldWeekTag);
                                RemoveDrawObject("weeklyNakedVAL_" + oldWeekTag);
                                RemoveDrawObject("weeklyNakedVAL_Label_" + oldWeekTag);
                            }
                            
                            previousWeekLevels = historicalWeeklyLevels[currentProcessingWeek];
                        }
                    }
                }
                
                currentProcessingWeek = weekStart;
                CleanupFilledWeeklyLevels();
            }
        }
        
 // NEW METHOD: Remove filled levels that are from completed sessions
private void CleanupFilledLevels()
{
    if (historicalLevels.Count == 0)
        return;
    
    DateTime currentSessionDate = GetSessionDateForBar(Time[0]);
    
    // Remove filled levels from sessions before the current one
    // This happens at the END of the session when we transition to a new one
    foreach (var kvp in historicalLevels.ToList())
    {
        DateTime date = kvp.Key;
        DayLevels levels = kvp.Value;
        
        // Skip current session
        if (date >= currentSessionDate)
            continue;
        
        string dateTag = date.ToString("yyyyMMdd");
        
        // Check POC removal criteria
        bool removePOC = false;
        if (levels.POCFilled && levels.POCNaked)
        {
            // Remove if: NOT keeping filled levels after session
            if (!KeepFilledLevelsAfterSession)
                removePOC = true;
            
            // OR if: Touch count exceeded (if enabled)
            if (RemoveAfterTouchCount > 0 && levels.POCTouchCount >= RemoveAfterTouchCount)
                removePOC = true;
        }
        
        if (removePOC)
        {
            levels.POCNaked = false;
            RemoveDrawObject("nakedPOC_" + dateTag);
            RemoveDrawObject("nakedPOC_Label_" + dateTag);
        }
        // Check VAH removal criteria
        bool removeVAH = false;
        if (levels.VAHFilled && levels.VAHNaked)
        {
            // Remove if: NOT keeping filled levels after session
            if (!KeepFilledLevelsAfterSession)
                removeVAH = true;
            
            // OR if: Touch count exceeded (if enabled)
            if (RemoveAfterTouchCount > 0 && levels.VAHTouchCount >= RemoveAfterTouchCount)
                removeVAH = true;
        }
        
        if (removeVAH)
        {
            levels.VAHNaked = false;
            RemoveDrawObject("nakedVAH_" + dateTag);
            RemoveDrawObject("nakedVAH_Label_" + dateTag);
        }
        // Check VAL removal criteria
        bool removeVAL = false;
        if (levels.VALFilled && levels.VALNaked)
        {
            // Remove if: NOT keeping filled levels after session
            if (!KeepFilledLevelsAfterSession)
                removeVAL = true;
            
            // OR if: Touch count exceeded (if enabled)
            if (RemoveAfterTouchCount > 0 && levels.VALTouchCount >= RemoveAfterTouchCount)
                removeVAL = true;
        }
        
        if (removeVAL)
        {
            levels.VALNaked = false;
            RemoveDrawObject("nakedVAL_" + dateTag);
            RemoveDrawObject("nakedVAL_Label_" + dateTag);
        }
    }
}

// Calculate overnight session profile (customizable times)
private void CalculateOvernightProfile(DateTime targetDate)
{
    // Parse overnight session times from HHMM format
    int startHour = OvernightStartTime / 100;
    int startMinute = OvernightStartTime % 100;
    int endHour = OvernightEndTime / 100;
    int endMinute = OvernightEndTime % 100;
    
    // Overnight session: previous day at start time to target day at end time
    DateTime previousDay = targetDate.AddDays(-1);
    DateTime sessionStart = new DateTime(previousDay.Year, previousDay.Month, previousDay.Day, startHour, startMinute, 0);
    DateTime sessionEnd = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, endHour, endMinute, 0);
    
    // Reset overnight high/low
double sessionHighest = double.MinValue;
double sessionLowest = double.MaxValue;
    
    List<int> overnightBarIndices = new List<int>();
    
    // Find all bars in overnight session
    for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
    {
        if (barsAgo > CurrentBar)
            break;
            
        DateTime barTime = Time[barsAgo];
        
        // Stop searching if we're way before the session
        if (barTime < sessionStart.AddHours(-24))
            break;
        
        // Bar within session range
        if (barTime >= sessionStart && barTime <= sessionEnd)
        {
            int absoluteIndex = CurrentBar - barsAgo;
            overnightBarIndices.Add(absoluteIndex);
        }
        // Check for higher timeframe bars that span into session
        else if (barTime < sessionEnd && barsAgo > 0)
        {
            DateTime nextBarTime = Time[barsAgo - 1];
            if (nextBarTime > sessionStart)
            {
                int absoluteIndex = CurrentBar - barsAgo;
                if (!overnightBarIndices.Contains(absoluteIndex))
                    overnightBarIndices.Add(absoluteIndex);
            }
        }
    }
    
    if (overnightBarIndices.Count == 0)
{
    return;
}

overnightBarIndices.Sort();

// Calculate overnight high and low from the bars we found
foreach (int idx in overnightBarIndices)
{
    sessionHighest = Math.Max(sessionHighest, High.GetValueAt(idx));
    sessionLowest = Math.Min(sessionLowest, Low.GetValueAt(idx));
}

// Calculate profile
List<double> overnightVolumes = new List<double>();
    for (int i = 0; i < NumberOfVolumeBars; i++)
        overnightVolumes.Add(0);
    
    foreach (int idx in overnightBarIndices)
    {
        overnightHigh = Math.Max(overnightHigh, High.GetValueAt(idx));
        overnightLow = Math.Min(overnightLow, Low.GetValueAt(idx));
    }
    
    double interval = (overnightHigh - overnightLow) / (NumberOfVolumeBars - 1);
    if (interval <= 0)
        return;
    
    // Accumulate volume
    foreach (int idx in overnightBarIndices)
    {
        double barLow = Low.GetValueAt(idx);
        double barHigh = High.GetValueAt(idx);
        double barVol = Volume.GetValueAt(idx);
        
        int minIdx = (int)Math.Floor((barLow - overnightLow) / interval);
        int maxIdx = (int)Math.Ceiling((barHigh - overnightLow) / interval);
        
        minIdx = Math.Max(0, Math.Min(minIdx, NumberOfVolumeBars - 1));
        maxIdx = Math.Max(0, Math.Min(maxIdx, NumberOfVolumeBars - 1));
        
        int levels = maxIdx - minIdx + 1;
        if (levels > 0)
        {
            double volPerLevel = barVol / levels;
            for (int j = minIdx; j <= maxIdx; j++)
                overnightVolumes[j] += volPerLevel;
        }
    }
    
    // Find POC
    double maxVol = 0;
    int pocIdx = 0;
    for (int i = 0; i < overnightVolumes.Count; i++)
    {
        if (overnightVolumes[i] > maxVol)
        {
            maxVol = overnightVolumes[i];
            pocIdx = i;
        }
    }
    
    if (maxVol == 0)
        return;
    
    // Calculate Value Area
    double sumVolume = 0;
    for (int i = 0; i < overnightVolumes.Count; i++)
        sumVolume += overnightVolumes[i];
    
    double vaVolume = sumVolume * ValueAreaPercentage / 100.0;
    
    int vaUp = pocIdx;
    int vaDown = pocIdx;
    double vaSum = maxVol;
    
    while (vaSum < vaVolume)
    {
        double vUp = (vaUp < NumberOfVolumeBars - 1) ? overnightVolumes[vaUp + 1] : 0.0;
        double vDown = (vaDown > 0) ? overnightVolumes[vaDown - 1] : 0.0;
        
        if (vUp == 0 && vDown == 0)
            break;
        
        if (vUp >= vDown)
        {
            vaSum += vUp;
            vaUp++;
        }
        else
        {
            vaSum += vDown;
            vaDown--;
        }
    }
    
    // Store overnight levels
overnightPOC = overnightLow + interval * pocIdx;
overnightVAH = overnightLow + interval * vaUp;
overnightVAL = overnightLow + interval * vaDown;
overnightHigh = sessionHighest;
overnightLow = sessionLowest;
overnightSessionDate = targetDate;
overnightLevelsCalculated = true;
	Print(string.Format("Overnight levels calculated for {0}: High={1:F2}, Low={2:F2}, POC={3:F2}", 
    targetDate.ToShortDateString(), overnightHigh, overnightLow, overnightPOC));
}
		
		private void CalculateFullWeekProfile(DateTime weekStartDate)
        {
            if (weekStartDate.DayOfWeek != DayOfWeek.Sunday)
                return;
            if (historicalWeeklyLevels.ContainsKey(weekStartDate))
            {
                return;
            }
            
            List<int> weekBarIndices = new List<int>();
            
            DateTime sessionStart = new DateTime(weekStartDate.Year, weekStartDate.Month, weekStartDate.Day, 18, 0, 0);
            DateTime sessionEnd = sessionStart.AddDays(5).AddHours(23).AddMinutes(59);
            
            int barsFound = 0;
            int maxBarsToSearch = CurrentBar + 1;
            
            for (int barsAgo = 0; barsAgo < maxBarsToSearch; barsAgo++)
            {
                if (barsAgo > CurrentBar)
                    break;
                    
                DateTime barTime = Time[barsAgo];
                
                if (barTime < sessionStart.AddDays(-7))
                    break;
                
                if (barTime >= sessionStart && barTime <= sessionEnd)
                {
                    int absoluteIndex = CurrentBar - barsAgo;
                    weekBarIndices.Add(absoluteIndex);
                    barsFound++;
                    continue;
                }
                
                if (barTime < sessionEnd && barsAgo > 0)
                {
                    DateTime nextBarTime = Time[barsAgo - 1];
                    
                    if (nextBarTime > sessionStart)
                    {
                        int absoluteIndex = CurrentBar - barsAgo;
                        if (!weekBarIndices.Contains(absoluteIndex))
                        {
                            weekBarIndices.Add(absoluteIndex);
                            barsFound++;
                        }
                    }
                }
            }
            
            if (weekBarIndices.Count == 0)
            {
                return;
            }
            
            weekBarIndices.Sort();
            
            List<double> weekVolumes = new List<double>();
            for (int i = 0; i < WeeklyNumberOfVolumeBars; i++)
                weekVolumes.Add(0);
            
            double weekHigh = double.MinValue;
            double weekLow = double.MaxValue;
            
            foreach (int idx in weekBarIndices)
            {
                weekHigh = Math.Max(weekHigh, High.GetValueAt(idx));
                weekLow = Math.Min(weekLow, Low.GetValueAt(idx));
            }
            
            double interval = (weekHigh - weekLow) / (WeeklyNumberOfVolumeBars - 1);
            if (interval <= 0)
                return;
            
            foreach (int idx in weekBarIndices)
            {
                double barLow = Low.GetValueAt(idx);
                double barHigh = High.GetValueAt(idx);
                double barVol = Volume.GetValueAt(idx);
                
                int minIdx = (int)Math.Floor((barLow - weekLow) / interval);
                int maxIdx = (int)Math.Ceiling((barHigh - weekLow) / interval);
                
                minIdx = Math.Max(0, Math.Min(minIdx, WeeklyNumberOfVolumeBars - 1));
                maxIdx = Math.Max(0, Math.Min(maxIdx, WeeklyNumberOfVolumeBars - 1));
                
                int levels = maxIdx - minIdx + 1;
                if (levels > 0)
                {
                    double volPerLevel = barVol / levels;
                    for (int j = minIdx; j <= maxIdx; j++)
                        weekVolumes[j] += volPerLevel;
                }
            }
            
            double maxVol = 0;
            int pocIdx = 0;
            for (int i = 0; i < weekVolumes.Count; i++)
            {
                if (weekVolumes[i] > maxVol)
                {
                    maxVol = weekVolumes[i];
                    pocIdx = i;
                }
            }
            
            if (maxVol == 0)
                return;
            
            double sumVolume = 0;
            for (int i = 0; i < weekVolumes.Count; i++)
                sumVolume += weekVolumes[i];
            
            double vaVolume = sumVolume * WeeklyValueAreaPercentage / 100.0;
            
            int vaUp = pocIdx;
            int vaDown = pocIdx;
            double vaSum = maxVol;
            
            while (vaSum < vaVolume)
            {
                double vUp = (vaUp < WeeklyNumberOfVolumeBars - 1) ? weekVolumes[vaUp + 1] : 0.0;
                double vDown = (vaDown > 0) ? weekVolumes[vaDown - 1] : 0.0;
                
                if (vUp == 0 && vDown == 0)
                    break;
                
                if (vUp >= vDown)
                {
                    vaSum += vUp;
                    vaUp++;
                }
                else
                {
                    vaSum += vDown;
                    vaDown--;
                }
            }
            
            double pocPrice = weekLow + interval * pocIdx;
            double vahPrice = weekLow + interval * vaUp;
            double valPrice = weekLow + interval * vaDown;
            
historicalWeeklyLevels[weekStartDate] = new WeekLevels
{
    WeekStartDate = weekStartDate,
    POC = pocPrice,
    VAH = vahPrice,
    VAL = valPrice,
    High = weekHigh,
    Low = weekLow,
    POCNaked = true,
    VAHNaked = true,
    VALNaked = true,
    POCTouchCount = 0,
    VAHTouchCount = 0,
    VALTouchCount = 0,
    POCLastTouchSession = DateTime.MinValue,
    VAHLastTouchSession = DateTime.MinValue,
    VALLastTouchSession = DateTime.MinValue
};
        }
		
private void CleanupFilledWeeklyLevels()
{
    if (historicalWeeklyLevels.Count == 0)
        return;
    
    DateTime currentWeek = GetWeekStartForBar(Time[0]);
    
    // Remove filled levels from weeks before the current one
    // This happens at the END of the week when we transition to a new one
    foreach (var kvp in historicalWeeklyLevels.ToList())
    {
        DateTime weekDate = kvp.Key;
        WeekLevels levels = kvp.Value;
        
        if (weekDate >= currentWeek)
            continue;
        
        string weekTag = weekDate.ToString("yyyyMMdd");
        
        // Check POC removal criteria
        bool removePOC = false;
        if (levels.POCFilled && levels.POCNaked)
        {
            // Remove if: NOT keeping filled levels after week
            if (!KeepFilledWeeklyLevelsAfterWeek)
                removePOC = true;
            
            // OR if: Touch count exceeded (if enabled)
            if (RemoveWeeklyAfterTouchCount > 0 && levels.POCTouchCount >= RemoveWeeklyAfterTouchCount)
                removePOC = true;
        }
        
        if (removePOC)
        {
            levels.POCNaked = false;
            RemoveDrawObject("weeklyNakedPOC_" + weekTag);
            RemoveDrawObject("weeklyNakedPOC_Label_" + weekTag);
        }
        
        // Check VAH removal criteria
        bool removeVAH = false;
        if (levels.VAHFilled && levels.VAHNaked)
        {
            // Remove if: NOT keeping filled levels after week
            if (!KeepFilledWeeklyLevelsAfterWeek)
                removeVAH = true;
            
            // OR if: Touch count exceeded (if enabled)
            if (RemoveWeeklyAfterTouchCount > 0 && levels.VAHTouchCount >= RemoveWeeklyAfterTouchCount)
                removeVAH = true;
        }
        
        if (removeVAH)
        {
            levels.VAHNaked = false;
            RemoveDrawObject("weeklyNakedVAH_" + weekTag);
            RemoveDrawObject("weeklyNakedVAH_Label_" + weekTag);
        }
        
        // Check VAL removal criteria
        bool removeVAL = false;
        if (levels.VALFilled && levels.VALNaked)
        {
            // Remove if: NOT keeping filled levels after week
            if (!KeepFilledWeeklyLevelsAfterWeek)
                removeVAL = true;
            
            // OR if: Touch count exceeded (if enabled)
            if (RemoveWeeklyAfterTouchCount > 0 && levels.VALTouchCount >= RemoveWeeklyAfterTouchCount)
                removeVAL = true;
        }
        
        if (removeVAL)
        {
            levels.VALNaked = false;
            RemoveDrawObject("weeklyNakedVAL_" + weekTag);
            RemoveDrawObject("weeklyNakedVAL_Label_" + weekTag);
        }
    }
}
		
		private void DrawWeeklyNakedLevels()
        {
            if (historicalWeeklyLevels.Count == 0)
                return;
            
            DateTime currentWeek = GetWeekStartForBar(Time[0]);
            
            List<KeyValuePair<DateTime, WeekLevels>> nakedWeeksList = new List<KeyValuePair<DateTime, WeekLevels>>();
            
            foreach (var kvp in historicalWeeklyLevels)
            {
                DateTime weekDate = kvp.Key;
                WeekLevels levels = kvp.Value;
                
                if (weekDate == currentProcessingWeek)
                    continue;
                
                if (weekDate >= currentWeek)
                    continue;
                
                if (previousWeekLevels != null && weekDate == previousWeekLevels.WeekStartDate)
                    continue;
                
                if (levels.POCNaked || levels.VAHNaked || levels.VALNaked || 
                    levels.POCFilled || levels.VAHFilled || levels.VALFilled)
                {
                    nakedWeeksList.Add(kvp);
                }
            }
            
            var recentNakedWeeks = nakedWeeksList
                .OrderByDescending(x => x.Key)
                .Take(MaxWeeklyNakedLevelsToDisplay)
                .ToList();
            
            int rightExtent = 0;
            int leftExtent = PreviousDayLineWidth == 0 ? CurrentBar : Math.Min(CurrentBar, PreviousDayLineWidth);
            
            foreach (var kvp in recentNakedWeeks)
            {
                DateTime weekDate = kvp.Key;
                WeekLevels levels = kvp.Value;
                string weekTag = weekDate.ToString("yyyyMMdd");
                
                if (levels.POCNaked)
{
    Brush weeklyNakedPOCBrush = WeeklyNakedPOCColor.Clone();
    weeklyNakedPOCBrush.Opacity = WeeklyNakedPOCOpacity / 100.0;
    
    Draw.Line(this, "weeklyNakedPOC_" + weekTag, false, 
        leftExtent, levels.POC, rightExtent, levels.POC,
        weeklyNakedPOCBrush, WeeklyNakedPOCLineStyle, WeeklyNakedPOCThickness);
    
    // Build label with optional touch count and price value
    string labelText = ShowPriceValuesInLabels ? 
        "wPOC " + levels.POC.ToString("F2") + " " + FormatDateLabel(weekDate) : 
        "wPOC " + FormatDateLabel(weekDate);
    if (levels.POCFilled)
        labelText += " (filled)";
    if (ShowTouchCountInLabels && levels.POCTouchCount > 0)
        labelText += " " + levels.POCTouchCount + "x";
    
    Draw.Text(this, "weeklyNakedPOC_Label_" + weekTag, false, 
        labelText, 
        rightExtent - 5, levels.POC, 0, WeeklyNakedPOCColor,
        new SimpleFont("Arial", 9), TextAlignment.Left,
        Brushes.Transparent, Brushes.Transparent, 0);
}
                
                if (levels.VAHNaked)
{
    Brush weeklyNakedVAHBrush = WeeklyNakedVAHColor.Clone();
    weeklyNakedVAHBrush.Opacity = WeeklyNakedVAHOpacity / 100.0;
    
    Draw.Line(this, "weeklyNakedVAH_" + weekTag, false,
        leftExtent, levels.VAH, rightExtent, levels.VAH,
        weeklyNakedVAHBrush, WeeklyNakedVAHLineStyle, WeeklyNakedVAHThickness);
    
    // Build label with optional touch count and price value
    string labelText = ShowPriceValuesInLabels ? 
        "wVAH " + levels.VAH.ToString("F2") + " " + FormatDateLabel(weekDate) : 
        "wVAH " + FormatDateLabel(weekDate);
    if (levels.VAHFilled)
        labelText += " (filled)";
    if (ShowTouchCountInLabels && levels.VAHTouchCount > 0)
        labelText += " " + levels.VAHTouchCount + "x";
    
    Draw.Text(this, "weeklyNakedVAH_Label_" + weekTag, false,
        labelText,
        rightExtent - 5, levels.VAH, 0, WeeklyNakedVAHColor,
        new SimpleFont("Arial", 9), TextAlignment.Left,
        Brushes.Transparent, Brushes.Transparent, 0);
}
                
                if (levels.VALNaked)
{
    Brush weeklyNakedVALBrush = WeeklyNakedVALColor.Clone();
    weeklyNakedVALBrush.Opacity = WeeklyNakedVALOpacity / 100.0;
    
    Draw.Line(this, "weeklyNakedVAL_" + weekTag, false,
        leftExtent, levels.VAL, rightExtent, levels.VAL,
        weeklyNakedVALBrush, WeeklyNakedVALLineStyle, WeeklyNakedVALThickness);
    
    // Build label with optional touch count and price value
    string labelText = ShowPriceValuesInLabels ? 
        "wVAL " + levels.VAL.ToString("F2") + " " + FormatDateLabel(weekDate) : 
        "wVAL " + FormatDateLabel(weekDate);
    if (levels.VALFilled)
        labelText += " (filled)";
    if (ShowTouchCountInLabels && levels.VALTouchCount > 0)
        labelText += " " + levels.VALTouchCount + "x";
    
    Draw.Text(this, "weeklyNakedVAL_Label_" + weekTag, false,
        labelText,
        rightExtent - 5, levels.VAL, 0, WeeklyNakedVALColor,
        new SimpleFont("Arial", 9), TextAlignment.Left,
        Brushes.Transparent, Brushes.Transparent, 0);
}
            }
        }
		
        private void DrawPreviousDayLevels()
        {
            if (previousDayLevels == null || CurrentBar < 1)
                return;
            
            string dateTag = previousDayLevels.Date.ToString("yyyyMMdd");
            
            // Calculate line extent based on PreviousDayLineWidth
            // If width is 0, extend back to current bar, otherwise limit by width
            int rightExtent = 0;
            int leftExtent = PreviousDayLineWidth == 0 ? CurrentBar : Math.Min(CurrentBar, PreviousDayLineWidth);
            
            if (DisplayPreviousDayPOC)
            {
                Brush pdPOCBrush = PdPOCColor.Clone();
                pdPOCBrush.Opacity = PdPOCOpacity / 100.0;
                Draw.Line(this, "pdPOC_" + dateTag, false, leftExtent, previousDayLevels.POC, rightExtent, previousDayLevels.POC, 
                         pdPOCBrush, PdPOCLineStyle, PdPOCThickness);
                
                string labelText = ShowPriceValuesInLabels ? "pdPOC " + previousDayLevels.POC.ToString("F2") : "pdPOC";
                Draw.Text(this, "pdPOC_Label_" + dateTag, false, labelText, rightExtent - 5, previousDayLevels.POC, 0, PdPOCColor,
                         new SimpleFont("Arial", 10), TextAlignment.Left, 
                         Brushes.Transparent, Brushes.Transparent, 0);
            }
            
            if (DisplayPreviousDayVAH)
            {
                Brush pdVAHBrush = PdVAHColor.Clone();
                pdVAHBrush.Opacity = PdVAHOpacity / 100.0;
                Draw.Line(this, "pdVAH_" + dateTag, false, leftExtent, previousDayLevels.VAH, rightExtent, previousDayLevels.VAH, 
                         pdVAHBrush, PdVAHLineStyle, PdVAHThickness);
                
                string labelText = ShowPriceValuesInLabels ? "pdVAH " + previousDayLevels.VAH.ToString("F2") : "pdVAH";
Draw.Text(this, "pdVAH_Label_" + dateTag, false, labelText, rightExtent - 5, previousDayLevels.VAH, 0, PdVAHColor,
                         new SimpleFont("Arial", 10), TextAlignment.Left, 
                         Brushes.Transparent, Brushes.Transparent, 0);
            }
            
            if (DisplayPreviousDayVAL)
            {
                Brush pdVALBrush = PdVALColor.Clone();
                pdVALBrush.Opacity = PdVALOpacity / 100.0;
                Draw.Line(this, "pdVAL_" + dateTag, false, leftExtent, previousDayLevels.VAL, rightExtent, previousDayLevels.VAL, 
                         pdVALBrush, PdVALLineStyle, PdVALThickness);
                
                string labelText = ShowPriceValuesInLabels ? "pdVAL " + previousDayLevels.VAL.ToString("F2") : "pdVAL";
Draw.Text(this, "pdVAL_Label_" + dateTag, false, labelText, rightExtent - 5, previousDayLevels.VAL, 0, PdVALColor,
                         new SimpleFont("Arial", 10), TextAlignment.Left, 
                         Brushes.Transparent, Brushes.Transparent, 0);
            }
        }
        
        private void CalculateFullDayProfile(DateTime targetDate)
        {
            // CRITICAL: Don't calculate profiles for weekends
            if (targetDate.DayOfWeek == DayOfWeek.Saturday || targetDate.DayOfWeek == DayOfWeek.Sunday)
            {
                Print(string.Format("SKIPPING weekend date: {0} ({1})", targetDate.ToShortDateString(), targetDate.DayOfWeek));
                return;
            }
            
            // CRITICAL: If we've already calculated this date, don't do it again
            if (historicalLevels.ContainsKey(targetDate))
            {
                Print(string.Format("*** SKIP: Profile for {0} already exists (POC={1:F2}), not recalculating ***", 
                    targetDate.ToShortDateString(), historicalLevels[targetDate].POC));
                return;
            }
            
            Print(string.Format("*** CALCULATING: Profile for {0} does NOT exist yet, calculating now ***", targetDate.ToShortDateString()));
            
            List<int> dayBarIndices = new List<int>();
            
            // CRITICAL FIX: Get the actual previous TRADING day (skip weekends)
            DateTime previousTradingDate = GetPreviousTradingDay(targetDate);
            
            // Futures session: 6pm previous TRADING day to 5pm TARGET day (inclusive)
            // Session ends at 5:00 PM, but we need to include the 5:00 PM bar
            // So we use 5:59:59 PM as the end time to capture all bars through 5:00 PM
            DateTime sessionStart = new DateTime(previousTradingDate.Year, previousTradingDate.Month, previousTradingDate.Day, 18, 0, 0);
            DateTime sessionEnd = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 17, 59, 59);
            
            // CRITICAL FIX: For higher timeframes, we need to find ALL bars that touch the session
            // A bar "touches" the session if any part of its time period overlaps with the session
            int barsFound = 0;
            
            // Search limit depends on what we're calculating
            int maxBarsToSearch;
            
            if (ProfileMode == ProfileModeEnum.VisibleRange)
            {
                // For visible range, only search the visible bars plus a small buffer
                // This is very efficient even on large datasets
                if (ChartControl != null && ChartBars != null)
                {
                    int visibleRange = Math.Abs(ChartBars.ToIndex - ChartBars.FromIndex);
                    maxBarsToSearch = Math.Min(visibleRange + 100, CurrentBar + 1); // Visible bars + 100 bar buffer
                }
                else
                {
                    maxBarsToSearch = Math.Min(500, CurrentBar + 1); // Fallback
                }
            }
            else
            {
                // For session/week/month calculations, we need to search all bars to find the right date range
                maxBarsToSearch = CurrentBar + 1;
            }
            
            for (int barsAgo = 0; barsAgo < maxBarsToSearch; barsAgo++)
            {
                if (barsAgo > CurrentBar)
                    break;
                    
                DateTime barTime = Time[barsAgo];
                
                // Stop searching if we're way before the session start
                if (barTime < sessionStart.AddHours(-24))
                    break;
                
                // For bars that START within the session range
                if (barTime >= sessionStart && barTime <= sessionEnd)
                {
                    int absoluteIndex = CurrentBar - barsAgo;
                    dayBarIndices.Add(absoluteIndex);
                    barsFound++;
                    continue;
                }
                
                // ADDITIONAL CHECK for higher timeframes:
                // If this bar starts BEFORE the session end, but the NEXT bar (if it exists) 
                // is AFTER the session start, then this bar likely spans part of the session
                if (barTime < sessionEnd && barsAgo > 0)
                {
                    // Look at the next chronological bar (previous barsAgo index)
                    DateTime nextBarTime = Time[barsAgo - 1];
                    
                    // If next bar is after session start, this bar must overlap with session
                    if (nextBarTime > sessionStart)
                    {
                        int absoluteIndex = CurrentBar - barsAgo;
                        if (!dayBarIndices.Contains(absoluteIndex))  // Avoid duplicates
                        {
                            dayBarIndices.Add(absoluteIndex);
                            barsFound++;
                        }
                    }
                }
            }
            
            if (dayBarIndices.Count == 0)
                return;
            
            // Sort indices in ascending order for proper processing
            dayBarIndices.Sort();
            
            List<double> dayVolumes = new List<double>();
            for (int i = 0; i < NumberOfVolumeBars; i++)
                dayVolumes.Add(0);
            
            double dayHigh = double.MinValue;
            double dayLow = double.MaxValue;
            
            foreach (int idx in dayBarIndices)
            {
                dayHigh = Math.Max(dayHigh, High.GetValueAt(idx));
                dayLow = Math.Min(dayLow, Low.GetValueAt(idx));
            }
            
            double interval = (dayHigh - dayLow) / (NumberOfVolumeBars - 1);
            if (interval <= 0)
                return;
            
            foreach (int idx in dayBarIndices)
            {
                double barLow = Low.GetValueAt(idx);
                double barHigh = High.GetValueAt(idx);
                double barVol = Volume.GetValueAt(idx);
                
                int minIdx = (int)Math.Floor((barLow - dayLow) / interval);
                int maxIdx = (int)Math.Ceiling((barHigh - dayLow) / interval);
                
                minIdx = Math.Max(0, Math.Min(minIdx, NumberOfVolumeBars - 1));
                maxIdx = Math.Max(0, Math.Min(maxIdx, NumberOfVolumeBars - 1));
                
                int levels = maxIdx - minIdx + 1;
                if (levels > 0)
                {
                    double volPerLevel = barVol / levels;
                    for (int j = minIdx; j <= maxIdx; j++)
                        dayVolumes[j] += volPerLevel;
                }
            }
            
            // Find POC
            double maxVol = 0;
            int pocIdx = 0;
            for (int i = 0; i < dayVolumes.Count; i++)
            {
                if (dayVolumes[i] > maxVol)
                {
                    maxVol = dayVolumes[i];
                    pocIdx = i;
                }
            }
            
            if (maxVol == 0)
                return;
            
            // Calculate Value Area
            double sumVolume = 0;
            for (int i = 0; i < dayVolumes.Count; i++)
                sumVolume += dayVolumes[i];
            
            double vaVolume = sumVolume * ValueAreaPercentage / 100.0;
            
            int vaUp = pocIdx;
            int vaDown = pocIdx;
            double vaSum = maxVol;
            
            while (vaSum < vaVolume)
            {
                double vUp = (vaUp < NumberOfVolumeBars - 1) ? dayVolumes[vaUp + 1] : 0.0;
                double vDown = (vaDown > 0) ? dayVolumes[vaDown - 1] : 0.0;
                
                if (vUp == 0 && vDown == 0)
                    break;
                
                if (vUp >= vDown)
                {
                    vaSum += vUp;
                    vaUp++;
                }
                else
                {
                    vaSum += vDown;
                    vaDown--;
                }
            }
            
            // Store levels with calculated VAH and VAL
            double pocPrice = dayLow + interval * pocIdx;
            double vahPrice = dayLow + interval * vaUp;
            double valPrice = dayLow + interval * vaDown;
            
            historicalLevels[targetDate] = new DayLevels
{
    Date = targetDate,
    POC = pocPrice,
    VAH = vahPrice,
    VAL = valPrice,
    High = dayHigh,
    Low = dayLow,
    POCNaked = true,
    VAHNaked = true,
    VALNaked = true,
    POCTouchCount = 0,
    VAHTouchCount = 0,
    VALTouchCount = 0,
    POCLastTouchSession = DateTime.MinValue,
    VAHLastTouchSession = DateTime.MinValue,
    VALLastTouchSession = DateTime.MinValue
};
            
        }
		
		private void UpdatePlotValues()
        {
            // Current Session Levels (Plots 0-2)
            if (IsProfileCalculated && DisplayPoC && maxIndexForRender >= 0 && maxIndexForRender < volumes.Count)
            {
                Values[0][0] = lowestPrice + priceInterval * maxIndexForRender;
            }
            
            if (IsProfileCalculated && DisplayValueArea)
            {
                if (vaUpForRender >= 0 && vaUpForRender < volumes.Count)
                    Values[1][0] = lowestPrice + priceInterval * vaUpForRender;
                    
                if (vaDownForRender >= 0 && vaDownForRender < volumes.Count)
                    Values[2][0] = lowestPrice + priceInterval * vaDownForRender;
            }
            
            // Previous Day Levels (Plots 3-7)
            if (previousDayLevels != null)
            {
                if (DisplayPreviousDayPOC)
                    Values[3][0] = previousDayLevels.POC;
                    
                if (DisplayPreviousDayVAH)
                    Values[4][0] = previousDayLevels.VAH;
                    
                if (DisplayPreviousDayVAL)
                    Values[5][0] = previousDayLevels.VAL;
            }
            
            if (DisplayPreviousDayHigh && prevDayHigh > 0)
                Values[6][0] = prevDayHigh;
                
            if (DisplayPreviousDayLow && prevDayLow > 0)
                Values[7][0] = prevDayLow;
            
            // Overnight Levels (Plots 8-12)
if (overnightLevelsCalculated)
{
    if (DisplayOvernightPOC)
        Values[8][0] = overnightPOC;
        
    if (DisplayOvernightVAH)
        Values[9][0] = overnightVAH;
        
    if (DisplayOvernightVAL)
        Values[10][0] = overnightVAL;
        
    if (DisplayOvernightHigh)
        Values[11][0] = overnightHigh;
        
    if (DisplayOvernightLow)
        Values[12][0] = overnightLow;
}
        }
        
        // NEW METHOD: Check for Previous Day High/Low
        private void CheckForPrevDayLevels()
        {
            if ((!DisplayPreviousDayHigh && !DisplayPreviousDayLow) || CurrentBar < 1) 
                return;
            
            try
            {
                // Get the session date for this bar using our existing method
                DateTime barSessionDate = GetSessionDateForBar(Time[0]);
                
                // Initialize on first bar
                if (currentSessionDateForHL == DateTime.MinValue)
                {
                    currentSessionDateForHL = barSessionDate;
                    currentSessionHigh = High[0];
                    currentSessionLow = Low[0];
                    return;
                }
                
                // Check if we've moved to a new session
                if (barSessionDate != currentSessionDateForHL)
                {
                    // Skip if the previous session was a weekend
                    if (currentSessionDateForHL.DayOfWeek != DayOfWeek.Saturday && 
                        currentSessionDateForHL.DayOfWeek != DayOfWeek.Sunday)
                    {
                        // Store the completed session's high/low as "previous day"
                        prevDayHigh = currentSessionHigh;
                        prevDayLow = currentSessionLow;
                        prevDayDate = currentSessionDateForHL;
                    }
                    
                    // Reset for new session
                    currentSessionHigh = High[0];
                    currentSessionLow = Low[0];
                    currentSessionDateForHL = barSessionDate;
                }
                else
                {
                    // Update current session's high/low
                    currentSessionHigh = Math.Max(currentSessionHigh, High[0]);
                    currentSessionLow = Math.Min(currentSessionLow, Low[0]);
                }
            }
            catch
            {
                // Silently handle errors
            }
        }
        
        // NEW METHOD: Draw Previous Day High/Low
        private void DrawPrevDayHighLow()
        {
            try
            {
                int rightExtent = 0;
                int leftExtent = PreviousDayLineWidth == 0 ? GetBarsToSessionStart() : Math.Min(GetBarsToSessionStart(), PreviousDayLineWidth);
                
                if (DisplayPreviousDayHigh && prevDayHigh > 0)
                {
                    Brush highBrush = PdHighColor.Clone();
                    highBrush.Opacity = PdHighOpacity / 100.0;
                    
                    Draw.Line(this, "PdHigh_Line", false, leftExtent, prevDayHigh, rightExtent, prevDayHigh,
                             highBrush, PdHighLineStyle, PdHighThickness);
                    
                    string labelText = ShowPriceValuesInLabels ? "PDH " + prevDayHigh.ToString("F2") : "PDH";
                    Draw.Text(this, "PdHigh_Text", false, labelText, -5, prevDayHigh, 0, highBrush,
                             new SimpleFont("Arial", 9), TextAlignment.Left,
                             Brushes.Transparent, Brushes.Transparent, 0);
                }
                
                if (DisplayPreviousDayLow && prevDayLow > 0 && prevDayLow != double.MaxValue)
                {
                    Brush lowBrush = PdLowColor.Clone();
                    lowBrush.Opacity = PdLowOpacity / 100.0;
                    
                    Draw.Line(this, "PdLow_Line", false, leftExtent, prevDayLow, rightExtent, prevDayLow,
                             lowBrush, PdLowLineStyle, PdLowThickness);
                    
                    string labelText = ShowPriceValuesInLabels ? "PDL " + prevDayLow.ToString("F2") : "PDL";
                    Draw.Text(this, "PdLow_Text", false, labelText, -5, prevDayLow, 0, lowBrush,
                             new SimpleFont("Arial", 9), TextAlignment.Left,
                             Brushes.Transparent, Brushes.Transparent, 0);
                }
            }
            catch
            {
                // Silently handle errors
            }
        }
		
		// Draw overnight levels
private void DrawOvernightLevels()
{
    try
    {
        int rightExtent = 0;
        int leftExtent = PreviousDayLineWidth == 0 ? CurrentBar : Math.Min(CurrentBar, PreviousDayLineWidth);
        
        if (DisplayOvernightPOC && overnightPOC > 0)
        {
            Brush oPOCBrush = OvernightPOCColor.Clone();
            oPOCBrush.Opacity = OvernightPOCOpacity / 100.0;
            
            Draw.Line(this, "oPOC_Line", false, leftExtent, overnightPOC, rightExtent, overnightPOC,
                     oPOCBrush, OvernightPOCLineStyle, OvernightPOCThickness);
            
            string labelText = ShowPriceValuesInLabels ? "oPOC " + overnightPOC.ToString("F2") : "oPOC";
            Draw.Text(this, "oPOC_Text", false, labelText, -5, overnightPOC, 0, oPOCBrush,
                     new SimpleFont("Arial", 9), TextAlignment.Left,
                     Brushes.Transparent, Brushes.Transparent, 0);
        }
        
        if (DisplayOvernightVAH && overnightVAH > 0)
        {
            Brush oVAHBrush = OvernightVAHColor.Clone();
            oVAHBrush.Opacity = OvernightVAHOpacity / 100.0;
            
            Draw.Line(this, "oVAH_Line", false, leftExtent, overnightVAH, rightExtent, overnightVAH,
                     oVAHBrush, OvernightVAHLineStyle, OvernightVAHThickness);
            
            string labelText = ShowPriceValuesInLabels ? "oVAH " + overnightVAH.ToString("F2") : "oVAH";
            Draw.Text(this, "oVAH_Text", false, labelText, -5, overnightVAH, 0, oVAHBrush,
                     new SimpleFont("Arial", 9), TextAlignment.Left,
                     Brushes.Transparent, Brushes.Transparent, 0);
        }
        
        if (DisplayOvernightVAL && overnightVAL > 0)
        {
            Brush oVALBrush = OvernightVALColor.Clone();
            oVALBrush.Opacity = OvernightVALOpacity / 100.0;
            
            Draw.Line(this, "oVAL_Line", false, leftExtent, overnightVAL, rightExtent, overnightVAL,
                     oVALBrush, OvernightVALLineStyle, OvernightVALThickness);
            
            string labelText = ShowPriceValuesInLabels ? "oVAL " + overnightVAL.ToString("F2") : "oVAL";
            Draw.Text(this, "oVAL_Text", false, labelText, -5, overnightVAL, 0, oVALBrush,
                     new SimpleFont("Arial", 9), TextAlignment.Left,
                     Brushes.Transparent, Brushes.Transparent, 0);
        }
		Print(string.Format("Drawing ONH: overnightHigh={0:F2}, Display={1}", overnightHigh, DisplayOvernightHigh));
		if (DisplayOvernightHigh && overnightHigh > 0)
        {
            Brush oHighBrush = OvernightHighColor.Clone();
            oHighBrush.Opacity = OvernightHighOpacity / 100.0;
            
            Draw.Line(this, "oHigh_Line", false, leftExtent, overnightHigh, rightExtent, overnightHigh,
                     oHighBrush, OvernightHighLineStyle, OvernightHighThickness);
            
            string labelText = ShowPriceValuesInLabels ? "ONH " + overnightHigh.ToString("F2") : "ONH";
            Draw.Text(this, "oHigh_Text", false, labelText, -5, overnightHigh, 0, oHighBrush,
                     new SimpleFont("Arial", 9), TextAlignment.Left,
                     Brushes.Transparent, Brushes.Transparent, 0);
        }
        Print(string.Format("Drawing ONL: overnightLow={0:F2}, Display={1}", overnightLow, DisplayOvernightLow));
        if (DisplayOvernightLow && overnightLow > 0 && overnightLow != double.MaxValue)
        {
            Brush oLowBrush = OvernightLowColor.Clone();
            oLowBrush.Opacity = OvernightLowOpacity / 100.0;
            
            Draw.Line(this, "oLow_Line", false, leftExtent, overnightLow, rightExtent, overnightLow,
                     oLowBrush, OvernightLowLineStyle, OvernightLowThickness);
            
            string labelText = ShowPriceValuesInLabels ? "ONL " + overnightLow.ToString("F2") : "ONL";
            Draw.Text(this, "oLow_Text", false, labelText, -5, overnightLow, 0, oLowBrush,
                     new SimpleFont("Arial", 9), TextAlignment.Left,
                     Brushes.Transparent, Brushes.Transparent, 0);
        }
    }
    catch
    {
        // Silently handle errors
    }
}
        
        // NEW METHOD: Helper to get bars to session start
        private int GetBarsToSessionStart()
        {
            try
            {
                // Get current session date
                DateTime barSessionDate = GetSessionDateForBar(Time[0]);
                
                // Previous trading day
                DateTime prevTradingDay = GetPreviousTradingDay(barSessionDate);
                
                // Session starts at 6pm on previous trading day
                DateTime sessionStart = new DateTime(prevTradingDay.Year, prevTradingDay.Month, prevTradingDay.Day, 18, 0, 0);
                
                // Count bars back to session start
                for (int i = 0; i <= CurrentBar; i++)
                {
                    if (Time[i] <= sessionStart)
                    {
                        return i;
                    }
                }
                
                return Math.Min(CurrentBar, 500);
            }
            catch
            {
                return Math.Min(CurrentBar, 500);
            }
        }
		
		#region Dual Profile Calculation
        
        private void CalculateDualProfiles()
        {
            if (!EnableDualProfileMode)
                return;
            
            // Calculate Weekly Profile
            CalculateWeeklyProfile();
            
            // Calculate Session Profile
            CalculateSessionProfile();
        }
        
        private void CalculateWeeklyProfile()
{
    weeklyVolumes.Clear();
    for (int i = 0; i < WeeklyNumberOfVolumeBars; i++)
        weeklyVolumes.Add(0);
    
    // Get current bar's week using the SAME logic as anchored mode
    DateTime currentWeekSession = GetWeekStartForBar(Time[0]);
    
    // Define session boundaries: Sunday 6 PM through Friday 5 PM (5:59:59 PM to capture 5 PM bar)
    DateTime previousTradingDay = currentWeekSession; // This IS the Sunday
    currentWeekStart = new DateTime(previousTradingDay.Year, previousTradingDay.Month, previousTradingDay.Day, 18, 0, 0);
    currentWeekEnd = currentWeekStart.AddDays(5).AddHours(23).AddMinutes(59).AddSeconds(59); // Friday 5:59:59 PM
    
    weeklyHighestPrice = double.MinValue;
    weeklyLowestPrice = double.MaxValue;
    
    // Find price range for this week using the SAME bar inclusion logic as anchored mode
    for (int i = 0; i <= CurrentBar; i++)
    {
        DateTime barTime = Time[i];
        
        // Bar starts within session range
        if (barTime >= currentWeekStart && barTime <= currentWeekEnd)
        {
            weeklyHighestPrice = Math.Max(weeklyHighestPrice, High[i]);
            weeklyLowestPrice = Math.Min(weeklyLowestPrice, Low[i]);
        }
        // Check for bars that SPAN into the session (for higher timeframes)
        else if (barTime < currentWeekEnd && i > 0)
        {
            DateTime nextBarTime = Time[i - 1];
            if (nextBarTime > currentWeekStart)
            {
                weeklyHighestPrice = Math.Max(weeklyHighestPrice, High[i]);
                weeklyLowestPrice = Math.Min(weeklyLowestPrice, Low[i]);
            }
        }
    }
    
    if (weeklyHighestPrice <= weeklyLowestPrice)
        return;
    
    weeklyPriceInterval = (weeklyHighestPrice - weeklyLowestPrice) / (WeeklyNumberOfVolumeBars - 1);
    
    if (weeklyPriceInterval <= 0)
        return;
    
    // Accumulate volume using the SAME bar inclusion logic
    for (int i = 0; i <= CurrentBar; i++)
    {
        DateTime barTime = Time[i];
        bool includeBar = false;
        
        // Bar starts within session range
        if (barTime >= currentWeekStart && barTime <= currentWeekEnd)
        {
            includeBar = true;
        }
        // Check for bars that SPAN into the session (for higher timeframes)
        else if (barTime < currentWeekEnd && i > 0)
        {
            DateTime nextBarTime = Time[i - 1];
            if (nextBarTime > currentWeekStart)
            {
                includeBar = true;
            }
        }
        
        if (includeBar)
        {
            double barLow = Low[i];
            double barHigh = High[i];
            double barVolume = Volume[i];
            bool isBullish = Close[i] >= Open[i];
            
            bool includeVol = WeeklyVolumeType == VolumeTypeEnum.Standard ||
                             WeeklyVolumeType == VolumeTypeEnum.Both ||
                             (WeeklyVolumeType == VolumeTypeEnum.Bullish && isBullish) ||
                             (WeeklyVolumeType == VolumeTypeEnum.Bearish && !isBullish);
            
            if (!includeVol)
                continue;
            
            int minIdx = (int)Math.Floor((barLow - weeklyLowestPrice) / weeklyPriceInterval);
            int maxIdx = (int)Math.Ceiling((barHigh - weeklyLowestPrice) / weeklyPriceInterval);
            
            minIdx = Math.Max(0, Math.Min(minIdx, WeeklyNumberOfVolumeBars - 1));
            maxIdx = Math.Max(0, Math.Min(maxIdx, WeeklyNumberOfVolumeBars - 1));
            
            int levels = maxIdx - minIdx + 1;
            if (levels > 0)
            {
                double volPerLevel = barVolume / levels;
                for (int j = minIdx; j <= maxIdx; j++)
                    weeklyVolumes[j] += volPerLevel;
            }
        }
    }
    
    // Find POC (rest of the code remains the same)
    weeklyMaxVolume = 0;
    weeklyMaxIndex = 0;
    for (int i = 0; i < weeklyVolumes.Count; i++)
    {
        if (weeklyVolumes[i] > weeklyMaxVolume)
        {
            weeklyMaxVolume = weeklyVolumes[i];
            weeklyMaxIndex = i;
        }
    }
    
    // Calculate Value Area
    if (weeklyMaxVolume > 0 && WeeklyDisplayValueArea)
    {
        double sumVol = weeklyVolumes.Sum();
        double vaVol = sumVol * WeeklyValueAreaPercentage / 100.0;
        
        weeklyVAUp = weeklyMaxIndex;
        weeklyVADown = weeklyMaxIndex;
        double vaSum = weeklyMaxVolume;
        
        while (vaSum < vaVol)
        {
            double vUp = (weeklyVAUp < WeeklyNumberOfVolumeBars - 1) ? weeklyVolumes[weeklyVAUp + 1] : 0.0;
            double vDown = (weeklyVADown > 0) ? weeklyVolumes[weeklyVADown - 1] : 0.0;
            
            if (vUp == 0 && vDown == 0)
                break;
            
            if (vUp >= vDown)
            {
                vaSum += vUp;
                weeklyVAUp++;
            }
            else
            {
                vaSum += vDown;
                weeklyVADown--;
            }
        }
    }
}
        
        private void CalculateSessionProfile()
{
    sessionVolumes.Clear();
    for (int i = 0; i < SessionNumberOfVolumeBars; i++)
        sessionVolumes.Add(0);
    
    DateTime sessionStart, sessionEnd;
    
    if (UseCustomDailySessionTimes)
    {
        // Use custom session times
        UpdateCustomDailySessionInfo();
        sessionStart = customDailySessionStart;
        sessionEnd = customDailySessionEnd;
    }
    else
    {
        // Get current session (6 PM yesterday to 5 PM today)
        DateTime now = Time[0];
        DateTime sessionDate = GetSessionDateForBar(now);
        DateTime prevTradingDay = GetPreviousTradingDay(sessionDate);
        
        sessionStart = new DateTime(prevTradingDay.Year, prevTradingDay.Month, prevTradingDay.Day, 18, 0, 0);
        sessionEnd = new DateTime(sessionDate.Year, sessionDate.Month, sessionDate.Day, 17, 59, 59);
    }
            
            sessionHighestPrice = double.MinValue;
            sessionLowestPrice = double.MaxValue;
            
            // Find price range for this session
            for (int i = 0; i <= CurrentBar; i++)
            {
                if (Time[i] >= sessionStart && Time[i] <= sessionEnd)
                {
                    sessionHighestPrice = Math.Max(sessionHighestPrice, High[i]);
                    sessionLowestPrice = Math.Min(sessionLowestPrice, Low[i]);
                }
            }
            
            if (sessionHighestPrice <= sessionLowestPrice)
                return;
            
            sessionPriceInterval = (sessionHighestPrice - sessionLowestPrice) / (SessionNumberOfVolumeBars - 1);
            
            if (sessionPriceInterval <= 0)
                return;
            
            // Accumulate volume
            for (int i = 0; i <= CurrentBar; i++)
            {
                if (Time[i] >= sessionStart && Time[i] <= sessionEnd)
            {
                double barLow = Low[i];
                double barHigh = High[i];
                double barVolume = Volume[i];
                bool isBullish = Close[i] >= Open[i];
                
                bool includeVol = SessionVolumeType == VolumeTypeEnum.Standard ||
                                 SessionVolumeType == VolumeTypeEnum.Both ||
                                 (SessionVolumeType == VolumeTypeEnum.Bullish && isBullish) ||
                                 (SessionVolumeType == VolumeTypeEnum.Bearish && !isBullish);
                
                if (!includeVol)
                    continue;
                    
                    int minIdx = (int)Math.Floor((barLow - sessionLowestPrice) / sessionPriceInterval);
                    int maxIdx = (int)Math.Ceiling((barHigh - sessionLowestPrice) / sessionPriceInterval);
                    
                    minIdx = Math.Max(0, Math.Min(minIdx, SessionNumberOfVolumeBars - 1));
                maxIdx = Math.Max(0, Math.Min(maxIdx, SessionNumberOfVolumeBars - 1));
                    
                    int levels = maxIdx - minIdx + 1;
                    if (levels > 0)
                    {
                        double volPerLevel = barVolume / levels;
                        for (int j = minIdx; j <= maxIdx; j++)
                            sessionVolumes[j] += volPerLevel;
                    }
                }
            }
            
            // Find POC
            sessionMaxVolume = 0;
            sessionMaxIndex = 0;
            for (int i = 0; i < sessionVolumes.Count; i++)
            {
                if (sessionVolumes[i] > sessionMaxVolume)
                {
                    sessionMaxVolume = sessionVolumes[i];
                    sessionMaxIndex = i;
                }
            }
            
            // Calculate Value Area
            if (sessionMaxVolume > 0 && SessionDisplayValueArea)
            {
                double sumVol = sessionVolumes.Sum();
                double vaVol = sumVol * SessionValueAreaPercentage / 100.0;
                
                sessionVAUp = sessionMaxIndex;
                sessionVADown = sessionMaxIndex;
                double vaSum = sessionMaxVolume;
                
                while (vaSum < vaVol)
                {
                    double vUp = (sessionVAUp < SessionNumberOfVolumeBars - 1) ? sessionVolumes[sessionVAUp + 1] : 0.0;
                    double vDown = (sessionVADown > 0) ? sessionVolumes[sessionVADown - 1] : 0.0;
                    
                    if (vUp == 0 && vDown == 0)
                        break;
                    
                    if (vUp >= vDown)
                    {
                        vaSum += vUp;
                        sessionVAUp++;
                    }
                    else
                    {
                        vaSum += vDown;
                        sessionVADown--;
                    }
                }
            }
        }
        
        #endregion
        
        #region LVN Calculation Methods
        
        private void CalculateLVNProfile()
        {
            if (!DisplayLVN)
                return;
                
            // Fast clear without reallocation
            Array.Clear(lvnProfile.TotalVolume, 0, lvnProfile.TotalVolume.Length);
            lvnProfile.VolumeNodes.Clear();
            
            lvnProfile.PriceStep = (lvnProfile.ProfileHigh - lvnProfile.ProfileLow) / LVNNumberOfRows;
            
            if (lvnProfile.PriceStep <= 0 || double.IsNaN(lvnProfile.PriceStep) || double.IsInfinity(lvnProfile.PriceStep))
                return;
            
            // Pre-calculate inverse for division optimization
            double invPriceStep = 1.0 / lvnProfile.PriceStep;
            
            // Distribute volume across price levels
            for (int i = 0; i < lvnProfile.BarHighs.Count; i++)
            {
                double barHigh = lvnProfile.BarHighs[i];
                double barLow = lvnProfile.BarLows[i];
                double barVolume = lvnProfile.BarVolumes[i];
                
                double barRange = barHigh - barLow;
                if (barRange <= 0) continue;
                
                double invBarRange = 1.0 / barRange;
                
                int startRow = Math.Max(0, (int)((barLow - lvnProfile.ProfileLow) * invPriceStep));
                int endRow = Math.Min(LVNNumberOfRows - 1, (int)((barHigh - lvnProfile.ProfileLow) * invPriceStep));
                
                for (int row = startRow; row <= endRow; row++)
                {
                    double levelPrice = lvnProfile.ProfileLow + (row * lvnProfile.PriceStep);
                    double nextLevelPrice = levelPrice + lvnProfile.PriceStep;
                    
                    double overlapHigh = Math.Min(barHigh, nextLevelPrice);
                    double overlapLow = Math.Max(barLow, levelPrice);
                    double overlapRange = overlapHigh - overlapLow;
                    
                    if (overlapRange > 0)
                    {
                        double volumeForLevel = barVolume * (overlapRange * invBarRange);
                        lvnProfile.TotalVolume[row] += volumeForLevel;
                    }
                }
            }
            
            // Create volume nodes
            for (int i = 0; i < LVNNumberOfRows; i++)
            {
                lvnProfile.VolumeNodes.Add(new VolumeNode
                {
                    PriceLevel = lvnProfile.ProfileLow + (i * lvnProfile.PriceStep) + (lvnProfile.PriceStep * 0.5),
                    TotalVolume = lvnProfile.TotalVolume[i],
                    IsTrough = false,
                    RowIndex = i
                });
            }
            
            // Detect LVN troughs
            DetectLVNTroughs();
        }
        
        private void DetectLVNTroughs()
        {
            int lvnNodes = (int)(LVNNumberOfRows * (LVNDetectionPercent / 100.0));
            
            for (int i = lvnNodes; i < LVNNumberOfRows - lvnNodes; i++)
            {
                // Skip completely empty rows
                if (lvnProfile.TotalVolume[i] <= 0)
                    continue;
                
                bool isLVN = true;
                
                // Check left side - all values should be GREATER than current (current is a local minimum)
                for (int j = i - lvnNodes; j < i; j++)
                {
                    if (lvnProfile.TotalVolume[j] <= lvnProfile.TotalVolume[i])
                    {
                        isLVN = false;
                        break;
                    }
                }
                
                // Check right side - all values should be GREATER than current (current is a local minimum)
                if (isLVN)
                {
                    for (int j = i + 1; j <= i + lvnNodes && j < LVNNumberOfRows; j++)
                    {
                        if (lvnProfile.TotalVolume[j] <= lvnProfile.TotalVolume[i])
                        {
                            isLVN = false;
                            break;
                        }
                    }
                }
                
                if (isLVN)
                {
                    lvnProfile.VolumeNodes[i].IsTrough = true;
                }
            }
        }
        
        #endregion
        
        #region LVN Data Collection
        
        private void CollectLVNData()
        {
            switch (ProfileMode)
            {
                case ProfileModeEnum.Session:
                    CollectLVNDataSession();
                    break;
                    
                case ProfileModeEnum.VisibleRange:
                    CollectLVNDataVisibleRange();
                    break;
                    
                case ProfileModeEnum.Weeks:
                    CollectLVNDataWeeks();
                    break;
                    
                case ProfileModeEnum.Months:
                    CollectLVNDataMonths();
                    break;
            }
        }
        
        private void CollectLVNDataSession()
        {
            lvnProfile.BarHighs.Clear();
            lvnProfile.BarLows.Clear();
            lvnProfile.BarVolumes.Clear();
            lvnProfile.BarPolarities.Clear();
            
            // Determine session boundaries
            DateTime sessionStart, sessionEnd;
            if (UseCustomSessionTimes)
            {
                sessionStart = customSessionStart;
                sessionEnd = customSessionEnd;
            }
            else
            {
                sessionStart = currentSessionStart;
                sessionEnd = currentSessionEnd;
            }
            
            // Collect all bars in the current session
            for (int i = 0; i <= CurrentBar; i++)
            {
                bool inSession;
                if (UseCustomSessionTimes)
                {
                    inSession = BarTouchesCustomSession(i, sessionStart, sessionEnd);
                }
                else
                {
                    inSession = BarTouchesSession(i, sessionStart, sessionEnd);
                }
                
                if (inSession)
                {
                    lvnProfile.BarHighs.Add(High[i]);
                    lvnProfile.BarLows.Add(Low[i]);
                    lvnProfile.BarVolumes.Add(Volume[i]);
                    lvnProfile.BarPolarities.Add(Close[i] >= Open[i]);
                }
            }
            
            if (lvnProfile.BarHighs.Count > 0)
            {
                lvnProfile.ProfileHigh = lvnProfile.BarHighs.Max();
                lvnProfile.ProfileLow = lvnProfile.BarLows.Min();

            }
        }
        
        private void CollectLVNDataVisibleRange()
        {
            lvnProfile.BarHighs.Clear();
            lvnProfile.BarLows.Clear();
            lvnProfile.BarVolumes.Clear();
            lvnProfile.BarPolarities.Clear();
            
            int barsToAnalyze = Math.Min(CurrentBar + 1, Bars.Count);
            
            for (int i = barsToAnalyze - 1; i >= 0; i--)
            {
                lvnProfile.BarHighs.Add(High[i]);
                lvnProfile.BarLows.Add(Low[i]);
                lvnProfile.BarVolumes.Add(Volume[i]);
                lvnProfile.BarPolarities.Add(Close[i] >= Open[i]);
            }
            
            if (lvnProfile.BarHighs.Count > 0)
            {
                lvnProfile.ProfileHigh = lvnProfile.BarHighs.Max();
                lvnProfile.ProfileLow = lvnProfile.BarLows.Min();
            }
        }
        
        private void CollectLVNDataWeeks()
        {
            lvnProfile.BarHighs.Clear();
            lvnProfile.BarLows.Clear();
            lvnProfile.BarVolumes.Clear();
            lvnProfile.BarPolarities.Clear();
            
            DateTime targetDate = Time[0].AddDays(-7 * WeeksLookback);
            
            for (int i = 0; i <= CurrentBar; i++)
            {
                if (Time[i] >= targetDate)
                {
                    lvnProfile.BarHighs.Add(High[i]);
                    lvnProfile.BarLows.Add(Low[i]);
                    lvnProfile.BarVolumes.Add(Volume[i]);
                    lvnProfile.BarPolarities.Add(Close[i] >= Open[i]);
                }
            }
            
            if (lvnProfile.BarHighs.Count > 0)
            {
                lvnProfile.ProfileHigh = lvnProfile.BarHighs.Max();
                lvnProfile.ProfileLow = lvnProfile.BarLows.Min();
            }
        }
        
        private void CollectLVNDataMonths()
        {
            lvnProfile.BarHighs.Clear();
            lvnProfile.BarLows.Clear();
            lvnProfile.BarVolumes.Clear();
            lvnProfile.BarPolarities.Clear();
            
            DateTime targetDate = Time[0].AddMonths(-MonthsLookback);
            
            for (int i = 0; i <= CurrentBar; i++)
            {
                if (Time[i] >= targetDate)
                {
                    lvnProfile.BarHighs.Add(High[i]);
                    lvnProfile.BarLows.Add(Low[i]);
                    lvnProfile.BarVolumes.Add(Volume[i]);
                    lvnProfile.BarPolarities.Add(Close[i] >= Open[i]);
                }
            }
            
            if (lvnProfile.BarHighs.Count > 0)
            {
                lvnProfile.ProfileHigh = lvnProfile.BarHighs.Max();
                lvnProfile.ProfileLow = lvnProfile.BarLows.Min();
            }
        }
        
        #endregion
        
        #region LVN Drawing Methods
        
        private void DrawLVNRectangles()
        {
            if (!DisplayLVN)
                return;
                
            // Remove old LVN drawings
            List<string> lvnTags = new List<string>();
            foreach (DrawingTool drawObj in DrawObjects)
            {
                if (drawObj.Tag != null && drawObj.Tag.StartsWith("LVN"))
                    lvnTags.Add(drawObj.Tag);
            }
            foreach (string tag in lvnTags)
            {
                RemoveDrawObject(tag);
            }
            
            // Draw LVN rectangles using horizontal lines (since Draw.Rectangle has issues)
            int leftExtent = Math.Min(CurrentBar, 500);
            int rightExtent = -500;
            
            // Draw LVN rectangles
            for (int i = 0; i < lvnProfile.VolumeNodes.Count; i++)
            {
                VolumeNode node = lvnProfile.VolumeNodes[i];
                if (node.IsTrough)
                {
                    // Calculate the combined rectangle bounds if adjacent nodes are shown
                    double levelTop, levelBottom;
                    
                    if (ShowAdjacentLVNNodes)
                    {
                        // Find the highest and lowest adjacent nodes
                        int topIndex = Math.Min(i + 1, lvnProfile.VolumeNodes.Count - 1);
                        int bottomIndex = Math.Max(i - 1, 0);
                        
                        VolumeNode topNode = lvnProfile.VolumeNodes[topIndex];
                        VolumeNode bottomNode = lvnProfile.VolumeNodes[bottomIndex];
                        
                        // Calculate combined bounds
                        levelTop = topNode.PriceLevel + lvnProfile.PriceStep * 0.4;
                        levelBottom = bottomNode.PriceLevel - lvnProfile.PriceStep * 0.4;
                    }
                    else
                    {
                        // Just the main trough
                        levelTop = node.PriceLevel + lvnProfile.PriceStep * 0.4;
                        levelBottom = node.PriceLevel - lvnProfile.PriceStep * 0.4;
                    }
                    
                    // Draw filled rectangle with opacity-adjusted border
                    int startBarIndex = Math.Min(CurrentBar, Bars.Count - 1);
                    DateTime startDateTime = Time[startBarIndex];
                    DateTime endDateTime = Time[0].AddDays(7);
                    
                    // Apply border opacity to a cloned brush
                    Brush borderBrush = LVNBorderColor.Clone();
                    borderBrush.Opacity = LVNBorderOpacity / 100.0;
                    
                    Draw.Rectangle(this, "LVN_" + CurrentBar.ToString() + "_" + i.ToString(), false,
                        startDateTime, levelBottom, endDateTime, levelTop, 
                        borderBrush, LVNFillColor, LVNFillOpacity);
                }
            }
        }
        
        #endregion
		
		#region DOM Helper Methods

        private double CalculateDynamicThreshold()
        {
            if (recentVolumes.Count < 10)
                return 0;

            var volumes = recentVolumes.ToArray();
            double mean = volumes.Average();
            double sumOfSquares = volumes.Sum(x => Math.Pow(x - mean, 2));
            double stdDev = Math.Sqrt(sumOfSquares / volumes.Length);

            return mean + (stdDev * VOLUME_STDDEV_MULTIPLIER);
        }

        private SharpDX.DirectWrite.TextFormat GetTextFormat(float size)
        {
            if (!textFormatCache.ContainsKey(size))
            {
                textFormatCache[size] = new SharpDX.DirectWrite.TextFormat(
                    Core.Globals.DirectWriteFactory,
                    "Segoe UI", 
                    size
                );
            }
            return textFormatCache[size];
        }

        private float CalculateDOMOpacity(bool isHistorical)
        {
            float liveOpacity = DomLiveOpacity / 100.0f;
            return isHistorical ? liveOpacity * (DomHistoricalOpacity / 100f) : liveOpacity;
        }

        private void InitializeFromMarketDepthSnapshot()
        {
            if (Instrument == null || State != State.Realtime)
                return;

            try
            {
                lock (Instrument.SyncMarketDepth)
                {
                    if (Instrument.MarketDepth == null || 
                        (Instrument.MarketDepth.Asks.Count == 0 && Instrument.MarketDepth.Bids.Count == 0))
                    {
                        return;
                    }

                    lock (orderLock)
                    {
                        renderBidOrders.Clear();
                        renderAskOrders.Clear();

                        foreach (var bid in Instrument.MarketDepth.Bids)
                        {
                            if (bid != null && bid.Price > 0 && bid.Volume > 0)
                            {
                                double price = Math.Round(bid.Price, 2);
                                if (!renderBidOrders.ContainsKey(price))
                                {
                                    renderBidOrders[price] = new OrderInfo { Price = price, Volume = bid.Volume, LastUpdate = DateTime.Now };
                                }
                                else
                                {
                                    renderBidOrders[price].Volume = bid.Volume;
                                    renderBidOrders[price].LastUpdate = DateTime.Now;
                                }
                            }
                        }

                        foreach (var ask in Instrument.MarketDepth.Asks)
                        {
                            if (ask != null && ask.Price > 0 && ask.Volume > 0)
                            {
                                double price = Math.Round(ask.Price, 2);
                                if (!renderAskOrders.ContainsKey(price))
                                {
                                    renderAskOrders[price] = new OrderInfo { Price = price, Volume = ask.Volume, LastUpdate = DateTime.Now };
                                }
                                else
                                {
                                    renderAskOrders[price].Volume = ask.Volume;
                                    renderAskOrders[price].LastUpdate = DateTime.Now;
                                }
                            }
                        }
                        
                        cachedDOMDataDirty = true;
                    }
                }
            }
            catch
            {
                // Silently handle errors
            }
        }

        #endregion
	
#region Properties

        [NinjaScriptProperty]
        [Display(Name = "Profile Mode", Description = "Volume Profile calculation mode", Order = 1, GroupName = "01. Profile Mode")]
        public ProfileModeEnum ProfileMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profile Alignment", Description = "Dock profile to left/right side of chart, or anchor to session open", Order = 2, GroupName = "01. Profile Mode")]
        public ProfileAlignment Alignment { get; set; }

        [NinjaScriptProperty]
        [Range(1, 52)]
        [Display(Name = "Weeks Lookback", Description = "Number of weeks to include (Weeks mode)", Order = 4, GroupName = "01. Profile Mode")]
        public int WeeksLookback { get; set; }
		
		[NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Sessions Lookback", Description = "Number of sessions to include (Session/Anchored mode)", Order = 3, GroupName = "01. Profile Mode")]
        public int SessionsLookback { get; set; }

        [NinjaScriptProperty]
        [Range(1, 24)]
        [Display(Name = "Months Lookback", Description = "Number of months to include (Months mode)", Order = 5, GroupName = "01. Profile Mode")]
        public int MonthsLookback { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Composite Range Type", Description = "Type of date range for Composite mode", Order = 6, GroupName = "01. Profile Mode")]
        public CompositeDateRangeType CompositeRangeType { get; set; }

        [NinjaScriptProperty]
        [Range(1, 365)]
        [Display(Name = "Composite Days Back", Description = "Number of days to include (Composite mode - Days Back)", Order = 7, GroupName = "01. Profile Mode")]
        public int CompositeDaysBack { get; set; }

        [NinjaScriptProperty]
        [Range(1, 52)]
        [Display(Name = "Composite Weeks Back", Description = "Number of weeks to include (Composite mode - Weeks Back)", Order = 8, GroupName = "01. Profile Mode")]
        public int CompositeWeeksBack { get; set; }

        [NinjaScriptProperty]
        [Range(1, 24)]
        [Display(Name = "Composite Months Back", Description = "Number of months to include (Composite mode - Months Back)", Order = 9, GroupName = "01. Profile Mode")]
        public int CompositeMonthsBack { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Composite Start Date", Description = "Start date for Custom Date Range (Composite mode)", Order = 10, GroupName = "01. Profile Mode")]
        public DateTime CompositeCustomStartDate { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Composite End Date", Description = "End date for Custom Date Range (Composite mode)", Order = 11, GroupName = "01. Profile Mode")]
        public DateTime CompositeCustomEndDate { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Use Custom Session Times", Description = "Override default session with custom times (Session mode only, ignored in Anchored mode)", Order = 12, GroupName = "01. Profile Mode")]
        public bool UseCustomSessionTimes { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Session Start Time", Description = "Start time in 24-hour format (HHMM). Example: 1759 = 5:59 PM, 0930 = 9:30 AM", Order = 13, GroupName = "01. Profile Mode")]
        public int SessionStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Session End Time", Description = "End time in 24-hour format (HHMM). Example: 1700 = 5:00 PM, 1600 = 4:00 PM", Order = 14, GroupName = "01. Profile Mode")]
        public int SessionEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(10, 1000)]
        [Display(Name = "Number of Volume Bars", Description = "Price levels in profile", Order = 1, GroupName = "02. Volume Bars")]
        public int NumberOfVolumeBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Bar Thickness", Description = "Thickness of volume bars", Order = 2, GroupName = "02. Volume Bars")]
        public int BarThickness { get; set; }

        [NinjaScriptProperty]
        [Range(50, 1000)]
        [Display(Name = "Profile Width", Description = "Max width of profile in pixels (ignored in Anchored mode)", Order = 3, GroupName = "02. Volume Bars")]
        public int ProfileWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Volume Type", Description = "Type of volume to include", Order = 4, GroupName = "02. Volume Bars")]
        public VolumeTypeEnum VolumeType { get; set; }

        [XmlIgnore]
        [Display(Name = "Bar Color", Description = "Color of volume bars", Order = 6, GroupName = "02. Volume Bars")]
        public Brush BarColor { get; set; }

        [Browsable(false)]
        public string BarColorSerialize
        {
            get { return Serialize.BrushToString(BarColor); }
            set { BarColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bullish Bar Color", Description = "Color of bullish volume bars", Order = 8, GroupName = "02. Volume Bars")]
        public Brush BullishBarColor { get; set; }

        [Browsable(false)]
        public string BullishBarColorSerialize
        {
            get { return Serialize.BrushToString(BullishBarColor); }
            set { BullishBarColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bearish Bar Color", Description = "Color of bearish volume bars", Order = 9, GroupName = "02. Volume Bars")]
        public Brush BearishBarColor { get; set; }

        [Browsable(false)]
        public string BearishBarColorSerialize
        {
            get { return Serialize.BrushToString(BearishBarColor); }
            set { BearishBarColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Bar Opacity", Description = "Opacity 0-100", Order = 10, GroupName = "02. Volume Bars")]
        public int BarOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Display Point of Control", Description = "Show PoC line", Order = 1, GroupName = "03. Point of Control")]
        public bool DisplayPoC { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "PoC Line Thickness", Description = "PoC line width", Order = 2, GroupName = "03. Point of Control")]
        public int PoCLineThickness { get; set; }

        [XmlIgnore]
        [Display(Name = "PoC Line Color", Description = "PoC color", Order = 3, GroupName = "03. Point of Control")]
        public Brush PoCLineColor { get; set; }

        [Browsable(false)]
        public string PoCLineColorSerialize
        {
            get { return Serialize.BrushToString(PoCLineColor); }
            set { PoCLineColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "PoC Line Style", Description = "Line style for PoC", Order = 4, GroupName = "03. Point of Control")]
        public DashStyleHelper PoCLineStyle { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "PoC Line Opacity", Description = "Opacity of PoC line (0-100)", Order = 5, GroupName = "03. Point of Control")]
        public int PoCLineOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Extend PoC Line", Description = "Extend PoC line to left edge of chart (ignored in Anchored mode)", Order = 6, GroupName = "03. Point of Control")]
        public bool ExtendPoCLine { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Display Value Area", Description = "Show Value Area", Order = 1, GroupName = "04. Value Area")]
        public bool DisplayValueArea { get; set; }

        [NinjaScriptProperty]
        [Range(5, 95)]
        [Display(Name = "Value Area Percentage", Description = "VA percentage", Order = 2, GroupName = "04. Value Area")]
        public int ValueAreaPercentage { get; set; }

        [XmlIgnore]
        [Display(Name = "Value Area Bar Color", Description = "VA bar color", Order = 3, GroupName = "04. Value Area")]
        public Brush ValueAreaBarColor { get; set; }

        [Browsable(false)]
        public string ValueAreaBarColorSerialize
        {
            get { return Serialize.BrushToString(ValueAreaBarColor); }
            set { ValueAreaBarColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Display Value Area Lines", Description = "Show VA boundary lines", Order = 4, GroupName = "04. Value Area")]
        public bool DisplayValueAreaLines { get; set; }

        [XmlIgnore]
        [Display(Name = "VA Lines Color", Description = "Color of VAH/VAL lines", Order = 5, GroupName = "04. Value Area")]
        public Brush ValueAreaLinesColor { get; set; }

        [Browsable(false)]
        public string ValueAreaLinesColorSerialize
        {
            get { return Serialize.BrushToString(ValueAreaLinesColor); }
            set { ValueAreaLinesColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "VA Lines Thickness", Description = "Thickness of VAH/VAL lines", Order = 6, GroupName = "04. Value Area")]
        public int ValueAreaLinesThickness { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "VA Lines Style", Description = "Line style for VAH/VAL", Order = 7, GroupName = "04. Value Area")]
        public DashStyleHelper ValueAreaLineStyle { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "VA Lines Opacity", Description = "Opacity of VAH/VAL lines (0-100)", Order = 8, GroupName = "04. Value Area")]
        public int ValueAreaLinesOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Extend VA Lines", Description = "Extend VAH/VAL lines to left edge of chart", Order = 9, GroupName = "04. Value Area")]
        public bool ExtendValueAreaLines { get; set; }

        [Range(1, 100)]
        [Display(Name = "Update Frequency", Description = "Update every N bars (historical)", Order = 1, GroupName = "05. Performance")]
        public int UpdateFrequency { get; set; }

        // Previous Day POC
        [NinjaScriptProperty]
        [Display(Name = "Display Previous Day POC", Description = "Show previous day's Point of Control", Order = 1, GroupName = "06. Previous Day")]
        public bool DisplayPreviousDayPOC { get; set; }

        [XmlIgnore]
        [Display(Name = "pdPOC Color", Description = "Color for pdPOC line", Order = 2, GroupName = "06. Previous Day")]
        public Brush PdPOCColor { get; set; }

        [Browsable(false)]
        public string PdPOCColorSerialize
        {
            get { return Serialize.BrushToString(PdPOCColor); }
            set { PdPOCColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "pdPOC Line Style", Description = "Line style for pdPOC", Order = 3, GroupName = "06. Previous Day")]
        public DashStyleHelper PdPOCLineStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "pdPOC Thickness", Description = "Line thickness for pdPOC", Order = 4, GroupName = "06. Previous Day")]
        public int PdPOCThickness { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "pdPOC Opacity", Description = "Opacity for pdPOC line (0-100)", Order = 5, GroupName = "06. Previous Day")]
        public int PdPOCOpacity { get; set; }

        // Previous Day VAH
        [NinjaScriptProperty]
        [Display(Name = "Display Previous Day VAH", Description = "Show previous day's Value Area High", Order = 6, GroupName = "06. Previous Day")]
        public bool DisplayPreviousDayVAH { get; set; }

        [XmlIgnore]
        [Display(Name = "pdVAH Color", Description = "Color for pdVAH line", Order = 7, GroupName = "06. Previous Day")]
        public Brush PdVAHColor { get; set; }

        [Browsable(false)]
        public string PdVAHColorSerialize
        {
            get { return Serialize.BrushToString(PdVAHColor); }
            set { PdVAHColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "pdVAH Line Style", Description = "Line style for pdVAH", Order = 8, GroupName = "06. Previous Day")]
        public DashStyleHelper PdVAHLineStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "pdVAH Thickness", Description = "Line thickness for pdVAH", Order = 9, GroupName = "06. Previous Day")]
        public int PdVAHThickness { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "pdVAH Opacity", Description = "Opacity for pdVAH line (0-100)", Order = 10, GroupName = "06. Previous Day")]
        public int PdVAHOpacity { get; set; }

        // Previous Day VAL
        [NinjaScriptProperty]
        [Display(Name = "Display Previous Day VAL", Description = "Show previous day's Value Area Low", Order = 11, GroupName = "06. Previous Day")]
        public bool DisplayPreviousDayVAL { get; set; }

        [XmlIgnore]
        [Display(Name = "pdVAL Color", Description = "Color for pdVAL line", Order = 12, GroupName = "06. Previous Day")]
        public Brush PdVALColor { get; set; }

        [Browsable(false)]
        public string PdVALColorSerialize
        {
            get { return Serialize.BrushToString(PdVALColor); }
            set { PdVALColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "pdVAL Line Style", Description = "Line style for pdVAL", Order = 13, GroupName = "06. Previous Day")]
        public DashStyleHelper PdVALLineStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "pdVAL Thickness", Description = "Line thickness for pdVAL", Order = 14, GroupName = "06. Previous Day")]
        public int PdVALThickness { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "pdVAL Opacity", Description = "Opacity for pdVAL line (0-100)", Order = 15, GroupName = "06. Previous Day")]
        public int PdVALOpacity { get; set; }
        
        // Previous Day High
        [NinjaScriptProperty]
        [Display(Name = "Display Previous Day High", Order = 16, GroupName = "06. Previous Day")]
        public bool DisplayPreviousDayHigh { get; set; }
        
        [XmlIgnore]
        [Display(Name = "PDH Color", Order = 17, GroupName = "06. Previous Day")]
        public Brush PdHighColor { get; set; }
        
        [Browsable(false)]
        public string PdHighColorSerialize
        {
            get { return Serialize.BrushToString(PdHighColor); }
            set { PdHighColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "PDH Line Style", Order = 18, GroupName = "06. Previous Day")]
        public DashStyleHelper PdHighLineStyle { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "PDH Thickness", Order = 19, GroupName = "06. Previous Day")]
        public int PdHighThickness { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "PDH Opacity", Order = 20, GroupName = "06. Previous Day")]
        public int PdHighOpacity { get; set; }
        
        // Previous Day Low
        [NinjaScriptProperty]
        [Display(Name = "Display Previous Day Low", Order = 21, GroupName = "06. Previous Day")]
        public bool DisplayPreviousDayLow { get; set; }
        
        [XmlIgnore]
        [Display(Name = "PDL Color", Order = 22, GroupName = "06. Previous Day")]
        public Brush PdLowColor { get; set; }
        
        [Browsable(false)]
        public string PdLowColorSerialize
        {
            get { return Serialize.BrushToString(PdLowColor); }
            set { PdLowColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "PDL Line Style", Order = 23, GroupName = "06. Previous Day")]
        public DashStyleHelper PdLowLineStyle { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "PDL Thickness", Order = 24, GroupName = "06. Previous Day")]
        public int PdLowThickness { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "PDL Opacity", Order = 25, GroupName = "06. Previous Day")]
        public int PdLowOpacity { get; set; }
		
		// ============================================
// GROUP 07: OVERNIGHT LEVELS (NEW)
// ============================================

// Overnight POC
[NinjaScriptProperty]
[Display(Name = "Display Overnight POC", Description = "Show overnight session's Point of Control (6 PM - 8:30 AM)", Order = 1, GroupName = "07. Overnight Levels")]
public bool DisplayOvernightPOC { get; set; }

[XmlIgnore]
[Display(Name = "oPOC Color", Description = "Color for oPOC line", Order = 2, GroupName = "07. Overnight Levels")]
public Brush OvernightPOCColor { get; set; }

[Browsable(false)]
public string OvernightPOCColorSerialize
{
    get { return Serialize.BrushToString(OvernightPOCColor); }
    set { OvernightPOCColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "oPOC Line Style", Description = "Line style for oPOC", Order = 3, GroupName = "07. Overnight Levels")]
public DashStyleHelper OvernightPOCLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "oPOC Thickness", Description = "Line thickness for oPOC", Order = 4, GroupName = "07. Overnight Levels")]
public int OvernightPOCThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "oPOC Opacity", Description = "Opacity for oPOC line (0-100)", Order = 5, GroupName = "07. Overnight Levels")]
public int OvernightPOCOpacity { get; set; }

// Overnight VAH
[NinjaScriptProperty]
[Display(Name = "Display Overnight VAH", Description = "Show overnight session's Value Area High (6 PM - 8:30 AM)", Order = 6, GroupName = "07. Overnight Levels")]
public bool DisplayOvernightVAH { get; set; }

[XmlIgnore]
[Display(Name = "oVAH Color", Description = "Color for oVAH line", Order = 7, GroupName = "07. Overnight Levels")]
public Brush OvernightVAHColor { get; set; }

[Browsable(false)]
public string OvernightVAHColorSerialize
{
    get { return Serialize.BrushToString(OvernightVAHColor); }
    set { OvernightVAHColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "oVAH Line Style", Description = "Line style for oVAH", Order = 8, GroupName = "07. Overnight Levels")]
public DashStyleHelper OvernightVAHLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "oVAH Thickness", Description = "Line thickness for oVAH", Order = 9, GroupName = "07. Overnight Levels")]
public int OvernightVAHThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "oVAH Opacity", Description = "Opacity for oVAH line (0-100)", Order = 10, GroupName = "07. Overnight Levels")]
public int OvernightVAHOpacity { get; set; }

// Overnight VAL
[NinjaScriptProperty]
[Display(Name = "Display Overnight VAL", Description = "Show overnight session's Value Area Low (6 PM - 8:30 AM)", Order = 11, GroupName = "07. Overnight Levels")]
public bool DisplayOvernightVAL { get; set; }

[XmlIgnore]
[Display(Name = "oVAL Color", Description = "Color for oVAL line", Order = 12, GroupName = "07. Overnight Levels")]
public Brush OvernightVALColor { get; set; }

[Browsable(false)]
public string OvernightVALColorSerialize
{
    get { return Serialize.BrushToString(OvernightVALColor); }
    set { OvernightVALColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "oVAL Line Style", Description = "Line style for oVAL", Order = 13, GroupName = "07. Overnight Levels")]
public DashStyleHelper OvernightVALLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "oVAL Thickness", Description = "Line thickness for oVAL", Order = 14, GroupName = "07. Overnight Levels")]
public int OvernightVALThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "oVAL Opacity", Description = "Opacity for oVAL line (0-100)", Order = 15, GroupName = "07. Overnight Levels")]
public int OvernightVALOpacity { get; set; }

// Overnight High
[NinjaScriptProperty]
[Display(Name = "Display Overnight High", Description = "Show overnight session's High", Order = 16, GroupName = "07. Overnight Levels")]
public bool DisplayOvernightHigh { get; set; }

[XmlIgnore]
[Display(Name = "ONH Color", Description = "Color for ONH line", Order = 17, GroupName = "07. Overnight Levels")]
public Brush OvernightHighColor { get; set; }

[Browsable(false)]
public string OvernightHighColorSerialize
{
    get { return Serialize.BrushToString(OvernightHighColor); }
    set { OvernightHighColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "ONH Line Style", Description = "Line style for ONH", Order = 18, GroupName = "07. Overnight Levels")]
public DashStyleHelper OvernightHighLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "ONH Thickness", Description = "Line thickness for ONH", Order = 19, GroupName = "07. Overnight Levels")]
public int OvernightHighThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "ONH Opacity", Description = "Opacity for ONH line (0-100)", Order = 20, GroupName = "07. Overnight Levels")]
public int OvernightHighOpacity { get; set; }

// Overnight Low
[NinjaScriptProperty]
[Display(Name = "Display Overnight Low", Description = "Show overnight session's Low", Order = 21, GroupName = "07. Overnight Levels")]
public bool DisplayOvernightLow { get; set; }

[XmlIgnore]
[Display(Name = "ONL Color", Description = "Color for ONL line", Order = 22, GroupName = "07. Overnight Levels")]
public Brush OvernightLowColor { get; set; }

[Browsable(false)]
public string OvernightLowColorSerialize
{
    get { return Serialize.BrushToString(OvernightLowColor); }
    set { OvernightLowColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "ONL Line Style", Description = "Line style for ONL", Order = 23, GroupName = "07. Overnight Levels")]
public DashStyleHelper OvernightLowLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "ONL Thickness", Description = "Line thickness for ONL", Order = 24, GroupName = "07. Overnight Levels")]
public int OvernightLowThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "ONL Opacity", Description = "Opacity for ONL line (0-100)", Order = 25, GroupName = "07. Overnight Levels")]
public int OvernightLowOpacity { get; set; }

// Overnight Session Time Settings
[NinjaScriptProperty]
[Range(0, 2359)]
[Display(Name = "Overnight Start Time", Description = "Overnight session start time in 24-hour format (HHMM). Default: 1800 = 6:00 PM", Order = 26, GroupName = "07. Overnight Levels")]
public int OvernightStartTime { get; set; }

[NinjaScriptProperty]
[Range(0, 2359)]
[Display(Name = "Overnight End Time", Description = "Overnight session end time in 24-hour format (HHMM). Default: 0830 = 8:30 AM", Order = 27, GroupName = "07. Overnight Levels")]
public int OvernightEndTime { get; set; }

// ============================================
// GROUP 08: SESSION NAKED LEVELS (formerly "Naked Levels", was GROUP 07)
// ============================================

[XmlIgnore]
[Display(Name = "Naked POC Color", Order = 1, GroupName = "08. Session Naked Levels")]
public Brush NakedPOCColor { get; set; }

[Browsable(false)]
public string NakedPOCColorSerialize
{
    get { return Serialize.BrushToString(NakedPOCColor); }
    set { NakedPOCColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "Naked POC Line Style", Order = 2, GroupName = "08. Session Naked Levels")]
public DashStyleHelper NakedPOCLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "Naked POC Thickness", Order = 3, GroupName = "08. Session Naked Levels")]
public int NakedPOCThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Naked POC Opacity", Order = 4, GroupName = "08. Session Naked Levels")]
public int NakedPOCOpacity { get; set; }

[XmlIgnore]
[Display(Name = "Naked VAH Color", Order = 5, GroupName = "08. Session Naked Levels")]
public Brush NakedVAHColor { get; set; }

[Browsable(false)]
public string NakedVAHColorSerialize
{
    get { return Serialize.BrushToString(NakedVAHColor); }
    set { NakedVAHColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "Naked VAH Line Style", Order = 6, GroupName = "08. Session Naked Levels")]
public DashStyleHelper NakedVAHLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "Naked VAH Thickness", Order = 7, GroupName = "08. Session Naked Levels")]
public int NakedVAHThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Naked VAH Opacity", Order = 8, GroupName = "08. Session Naked Levels")]
public int NakedVAHOpacity { get; set; }

[XmlIgnore]
[Display(Name = "Naked VAL Color", Order = 9, GroupName = "08. Session Naked Levels")]
public Brush NakedVALColor { get; set; }

[Browsable(false)]
public string NakedVALColorSerialize
{
    get { return Serialize.BrushToString(NakedVALColor); }
    set { NakedVALColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "Naked VAL Line Style", Order = 10, GroupName = "08. Session Naked Levels")]
public DashStyleHelper NakedVALLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "Naked VAL Thickness", Order = 11, GroupName = "08. Session Naked Levels")]
public int NakedVALThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Naked VAL Opacity", Order = 12, GroupName = "08. Session Naked Levels")]
public int NakedVALOpacity { get; set; }

// ============================================
// GROUP 09: WEEKLY SESSION NAKED LEVELS (was GROUP 08)
// ============================================

[XmlIgnore]
[Display(Name = "Weekly Naked POC Color", Order = 1, GroupName = "09. Weekly Session Naked Levels")]
public Brush WeeklyNakedPOCColor { get; set; }

[Browsable(false)]
public string WeeklyNakedPOCColorSerialize
{
    get { return Serialize.BrushToString(WeeklyNakedPOCColor); }
    set { WeeklyNakedPOCColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "Weekly Naked POC Line Style", Order = 2, GroupName = "09. Weekly Session Naked Levels")]
public DashStyleHelper WeeklyNakedPOCLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "Weekly Naked POC Thickness", Order = 3, GroupName = "09. Weekly Session Naked Levels")]
public int WeeklyNakedPOCThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Weekly Naked POC Opacity", Order = 4, GroupName = "09. Weekly Session Naked Levels")]
public int WeeklyNakedPOCOpacity { get; set; }

[XmlIgnore]
[Display(Name = "Weekly Naked VAH Color", Order = 5, GroupName = "09. Weekly Session Naked Levels")]
public Brush WeeklyNakedVAHColor { get; set; }

[Browsable(false)]
public string WeeklyNakedVAHColorSerialize
{
    get { return Serialize.BrushToString(WeeklyNakedVAHColor); }
    set { WeeklyNakedVAHColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "Weekly Naked VAH Line Style", Order = 6, GroupName = "09. Weekly Session Naked Levels")]
public DashStyleHelper WeeklyNakedVAHLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "Weekly Naked VAH Thickness", Order = 7, GroupName = "09. Weekly Session Naked Levels")]
public int WeeklyNakedVAHThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Weekly Naked VAH Opacity", Order = 8, GroupName = "09. Weekly Session Naked Levels")]
public int WeeklyNakedVAHOpacity { get; set; }

[XmlIgnore]
[Display(Name = "Weekly Naked VAL Color", Order = 9, GroupName = "09. Weekly Session Naked Levels")]
public Brush WeeklyNakedVALColor { get; set; }

[Browsable(false)]
public string WeeklyNakedVALColorSerialize
{
    get { return Serialize.BrushToString(WeeklyNakedVALColor); }
    set { WeeklyNakedVALColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Display(Name = "Weekly Naked VAL Line Style", Order = 10, GroupName = "09. Weekly Session Naked Levels")]
public DashStyleHelper WeeklyNakedVALLineStyle { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "Weekly Naked VAL Thickness", Order = 11, GroupName = "09. Weekly Session Naked Levels")]
public int WeeklyNakedVALThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Weekly Naked VAL Opacity", Order = 12, GroupName = "09. Weekly Session Naked Levels")]
public int WeeklyNakedVALOpacity { get; set; }

// ============================================
// GROUP 10: NAKED LEVELS SETTINGS (was GROUP 09)
// ============================================

[NinjaScriptProperty]
[Display(Name = "Display Naked Levels", 
         Description = "Master toggle for all naked levels (session and weekly)",
         Order = 1, GroupName = "10. Naked Levels Settings")]
public bool DisplayNakedLevels { get; set; }

[NinjaScriptProperty]
[Range(1, 20)]
[Display(Name = "Max Session Levels to Display", 
         Description = "Maximum number of past sessions to show naked levels from",
         Order = 2, GroupName = "10. Naked Levels Settings")]
public int MaxNakedLevelsToDisplay { get; set; }

[NinjaScriptProperty]
[Display(Name = "Display Weekly Naked Levels", 
         Description = "Enable naked levels from weekly sessions",
         Order = 3, GroupName = "10. Naked Levels Settings")]
public bool DisplayWeeklyNakedLevels { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "Max Weekly Levels to Display", 
         Description = "Maximum number of past weeks to show naked levels from",
         Order = 4, GroupName = "10. Naked Levels Settings")]
public int MaxWeeklyNakedLevelsToDisplay { get; set; }

// Session Level Persistence Settings
[NinjaScriptProperty]
[Display(Name = "Keep Filled Session Levels After Session", 
         Description = "When enabled, filled session levels persist across sessions until other removal criteria are met",
         Order = 5, GroupName = "10. Naked Levels Settings")]
public bool KeepFilledLevelsAfterSession { get; set; }

[NinjaScriptProperty]
[Range(0, 20)]
[Display(Name = "Remove Session Levels After Touch Count", 
         Description = "Remove session levels after X session touches (0 = never remove based on touches)",
         Order = 6, GroupName = "10. Naked Levels Settings")]
public int RemoveAfterTouchCount { get; set; }

// Weekly Level Persistence Settings
[NinjaScriptProperty]
[Display(Name = "Keep Filled Weekly Levels After Week", 
         Description = "When enabled, filled weekly levels persist across weeks until other removal criteria are met",
         Order = 7, GroupName = "10. Naked Levels Settings")]
public bool KeepFilledWeeklyLevelsAfterWeek { get; set; }

[NinjaScriptProperty]
[Range(0, 20)]
[Display(Name = "Remove Weekly Levels After Touch Count", 
         Description = "Remove weekly levels after X weekly session touches (0 = never remove based on touches)",
         Order = 8, GroupName = "10. Naked Levels Settings")]
public int RemoveWeeklyAfterTouchCount { get; set; }

// Display Options
[NinjaScriptProperty]
[Display(Name = "Show Touch Count in Labels", 
         Description = "Display touch count in level labels (e.g., 'nPOC 01/06 (2x)')",
         Order = 9, GroupName = "10. Naked Levels Settings")]
public bool ShowTouchCountInLabels { get; set; }

[NinjaScriptProperty]
[Display(Name = "Weird Date Formatting for the Brits", 
 Description = "Enable DD/MM format instead of MM/DD (because apparently they drive on the wrong side of the road AND write dates backwards)",
 Order = 10, GroupName = "10. Naked Levels Settings")]
public bool BritishDateFormat { get; set; }

// ============================================
// GROUP 11: DISPLAY SETTINGS (was GROUP 10)
// ============================================

[NinjaScriptProperty]
[Range(0, 500)]
[Display(Name = "Historical Line Width", Description = "Width in bars for Previous Day and Naked Level lines. 0 = extend to current bar indefinitely", Order = 1, GroupName = "11. Display Settings")]
public int PreviousDayLineWidth { get; set; }

[NinjaScriptProperty]
[Display(Name = "Show Price Values in Labels", Description = "Display the numerical price value in level labels (e.g., 'pdVAL 4655' instead of just 'pdVAL')", Order = 2, GroupName = "11. Display Settings")]
public bool ShowPriceValuesInLabels { get; set; }

// ============================================
// GROUP 12: DUAL PROFILE MODE (was GROUP 11)
// ============================================

[NinjaScriptProperty]
[Display(Name = "Enable Dual Profile Mode", Description = "Display both Weekly and Session profiles side-by-side on the right panel", Order = 1, GroupName = "12. Dual Profile Mode")]
public bool EnableDualProfileMode { get; set; }

[NinjaScriptProperty]
[Range(50, 500)]
[Display(Name = "Weekly Profile Width", Description = "Width in pixels for the weekly profile", Order = 2, GroupName = "12. Dual Profile Mode")]
public int WeeklyProfileWidth { get; set; }

[NinjaScriptProperty]
[Range(50, 500)]
[Display(Name = "Session Profile Width", Description = "Width in pixels for the session profile", Order = 3, GroupName = "12. Dual Profile Mode")]
public int SessionProfileWidth { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Gap Between Profiles", Description = "Gap in pixels between session and weekly profiles", Order = 4, GroupName = "12. Dual Profile Mode")]
public int ProfileGap { get; set; }

[NinjaScriptProperty]
[Display(Name = "Use Custom Daily Session Times", Description = "Override default session with custom times for daily profile (Dual Mode only)", Order = 5, GroupName = "12. Dual Profile Mode")]
public bool UseCustomDailySessionTimes { get; set; }

[NinjaScriptProperty]
[Range(0, 2359)]
[Display(Name = "Daily Session Start Time", Description = "Start time in 24-hour format (HHMM). Example: 1759 = 5:59 PM, 0930 = 9:30 AM", Order = 6, GroupName = "12. Dual Profile Mode")]
public int DailySessionStartTime { get; set; }

[NinjaScriptProperty]
[Range(0, 2359)]
[Display(Name = "Daily Session End Time", Description = "End time in 24-hour format (HHMM). Example: 1700 = 5:00 PM, 1600 = 4:00 PM", Order = 7, GroupName = "12. Dual Profile Mode")]
public int DailySessionEndTime { get; set; }

[NinjaScriptProperty]
[Display(Name = "Session Profile Style", Description = "Render session profile as filled bars or smooth outline", Order = 8, GroupName = "12. Dual Profile Mode")]
public SessionProfileStyleEnum SessionProfileStyle { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Session Outline Smoothness", Description = "0=sharp corners, 100=very smooth curves", Order = 9, GroupName = "12. Dual Profile Mode")]
public int SessionOutlineSmoothness { get; set; }

// ============================================
// GROUP 13: WEEKLY PROFILE SETTINGS (was GROUP 12)
// ============================================

[NinjaScriptProperty]
[Range(10, 1000)]
[Display(Name = "Number of Volume Bars", Description = "Price levels in weekly profile", Order = 1, GroupName = "13. Weekly Profile Settings")]
public int WeeklyNumberOfVolumeBars { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "Bar Thickness", Description = "Thickness of weekly volume bars", Order = 2, GroupName = "13. Weekly Profile Settings")]
public int WeeklyBarThickness { get; set; }

[NinjaScriptProperty]
[Display(Name = "Volume Type", Description = "Type of volume to include in weekly profile", Order = 3, GroupName = "13. Weekly Profile Settings")]
public VolumeTypeEnum WeeklyVolumeType { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Bar Opacity", Description = "Opacity 0-100 for weekly bars", Order = 4, GroupName = "13. Weekly Profile Settings")]
public int WeeklyBarOpacity { get; set; }

[NinjaScriptProperty]
[Display(Name = "Display PoC", Description = "Show PoC in weekly profile", Order = 5, GroupName = "13. Weekly Profile Settings")]
public bool WeeklyDisplayPoC { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "PoC Line Thickness", Description = "Weekly PoC line width", Order = 6, GroupName = "13. Weekly Profile Settings")]
public int WeeklyPoCLineThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "PoC Line Opacity", Description = "Opacity of weekly PoC line (0-100)", Order = 7, GroupName = "13. Weekly Profile Settings")]
public int WeeklyPoCLineOpacity { get; set; }

[NinjaScriptProperty]
[Display(Name = "Display Value Area", Description = "Show Value Area in weekly profile", Order = 8, GroupName = "13. Weekly Profile Settings")]
public bool WeeklyDisplayValueArea { get; set; }

[NinjaScriptProperty]
[Range(5, 95)]
[Display(Name = "Value Area Percentage", Description = "VA percentage for weekly profile", Order = 9, GroupName = "13. Weekly Profile Settings")]
public int WeeklyValueAreaPercentage { get; set; }

[NinjaScriptProperty]
[Display(Name = "Display VA Lines", Description = "Show VA boundary lines in weekly profile", Order = 10, GroupName = "13. Weekly Profile Settings")]
public bool WeeklyDisplayValueAreaLines { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "VA Lines Thickness", Description = "Thickness of weekly VAH/VAL lines", Order = 11, GroupName = "13. Weekly Profile Settings")]
public int WeeklyValueAreaLinesThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "VA Lines Opacity", Description = "Opacity of weekly VAH/VAL lines (0-100)", Order = 12, GroupName = "13. Weekly Profile Settings")]
public int WeeklyValueAreaLinesOpacity { get; set; }

[NinjaScriptProperty]
[Display(Name = "Extend PoC Line", Description = "Extend weekly PoC line to left edge of chart", Order = 13, GroupName = "13. Weekly Profile Settings")]
public bool WeeklyExtendPoCLine { get; set; }

[NinjaScriptProperty]
[Display(Name = "Extend VA Lines", Description = "Extend weekly VAH/VAL lines to left edge of chart", Order = 14, GroupName = "13. Weekly Profile Settings")]
public bool WeeklyExtendValueAreaLines { get; set; }

// ============================================
// GROUP 14: WEEKLY PROFILE COLORS (was GROUP 13)
// ============================================

[XmlIgnore]
[Display(Name = "Weekly Bar Color", Order = 1, GroupName = "14. Weekly Profile Colors")]
public Brush WeeklyBarColor { get; set; }

[Browsable(false)]
public string WeeklyBarColorSerialize
{
    get { return Serialize.BrushToString(WeeklyBarColor); }
    set { WeeklyBarColor = Serialize.StringToBrush(value); }
}

[XmlIgnore]
[Display(Name = "Weekly PoC Color", Order = 2, GroupName = "14. Weekly Profile Colors")]
public Brush WeeklyPoCColor { get; set; }

[Browsable(false)]
public string WeeklyPoCColorSerialize
{
    get { return Serialize.BrushToString(WeeklyPoCColor); }
    set { WeeklyPoCColor = Serialize.StringToBrush(value); }
}

[XmlIgnore]
[Display(Name = "Weekly VA Color", Order = 3, GroupName = "14. Weekly Profile Colors")]
public Brush WeeklyVAColor { get; set; }

[Browsable(false)]
public string WeeklyVAColorSerialize
{
    get { return Serialize.BrushToString(WeeklyVAColor); }
    set { WeeklyVAColor = Serialize.StringToBrush(value); }
}

[XmlIgnore]
[Display(Name = "Weekly VA Lines Color", Order = 4, GroupName = "14. Weekly Profile Colors")]
public Brush WeeklyVALinesColor { get; set; }

[Browsable(false)]
public string WeeklyVALinesColorSerialize
{
    get { return Serialize.BrushToString(WeeklyVALinesColor); }
    set { WeeklyVALinesColor = Serialize.StringToBrush(value); }
}

// ============================================
// GROUP 15: SESSION PROFILE SETTINGS (was GROUP 14)
// ============================================

[NinjaScriptProperty]
[Range(10, 1000)]
[Display(Name = "Number of Volume Bars", Description = "Price levels in session profile", Order = 1, GroupName = "15. Session Profile Settings")]
public int SessionNumberOfVolumeBars { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "Bar Thickness", Description = "Thickness of session volume bars", Order = 2, GroupName = "15. Session Profile Settings")]
public int SessionBarThickness { get; set; }

[NinjaScriptProperty]
[Display(Name = "Volume Type", Description = "Type of volume to include in session profile", Order = 3, GroupName = "15. Session Profile Settings")]
public VolumeTypeEnum SessionVolumeType { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Bar Opacity", Description = "Opacity 0-100 for session bars", Order = 4, GroupName = "15. Session Profile Settings")]
public int SessionBarOpacity { get; set; }

[NinjaScriptProperty]
[Display(Name = "Display PoC", Description = "Show PoC in session profile", Order = 5, GroupName = "15. Session Profile Settings")]
public bool SessionDisplayPoC { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "PoC Line Thickness", Description = "Session PoC line width", Order = 6, GroupName = "15. Session Profile Settings")]
public int SessionPoCLineThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "PoC Line Opacity", Description = "Opacity of session PoC line (0-100)", Order = 7, GroupName = "15. Session Profile Settings")]
public int SessionPoCLineOpacity { get; set; }

[NinjaScriptProperty]
[Display(Name = "Display Value Area", Description = "Show Value Area in session profile", Order = 8, GroupName = "15. Session Profile Settings")]
public bool SessionDisplayValueArea { get; set; }

[NinjaScriptProperty]
[Range(5, 95)]
[Display(Name = "Value Area Percentage", Description = "VA percentage for session profile", Order = 9, GroupName = "15. Session Profile Settings")]
public int SessionValueAreaPercentage { get; set; }

[NinjaScriptProperty]
[Display(Name = "Display VA Lines", Description = "Show VA boundary lines in session profile", Order = 10, GroupName = "15. Session Profile Settings")]
public bool SessionDisplayValueAreaLines { get; set; }

[NinjaScriptProperty]
[Range(1, 10)]
[Display(Name = "VA Lines Thickness", Description = "Thickness of session VAH/VAL lines", Order = 11, GroupName = "15. Session Profile Settings")]
public int SessionValueAreaLinesThickness { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "VA Lines Opacity", Description = "Opacity of session VAH/VAL lines (0-100)", Order = 12, GroupName = "15. Session Profile Settings")]
public int SessionValueAreaLinesOpacity { get; set; }

[NinjaScriptProperty]
[Display(Name = "Extend PoC Line", Description = "Extend session PoC line to left edge of chart", Order = 13, GroupName = "15. Session Profile Settings")]
public bool SessionExtendPoCLine { get; set; }

[NinjaScriptProperty]
[Display(Name = "Extend VA Lines", Description = "Extend session VAH/VAL lines to left edge of chart", Order = 14, GroupName = "15. Session Profile Settings")]
public bool SessionExtendValueAreaLines { get; set; }

// ============================================
// GROUP 16: SESSION PROFILE COLORS (was GROUP 15)
// ============================================

[XmlIgnore]
[Display(Name = "Session Outline Color", Order = 1, GroupName = "16. Session Profile Colors")]
public Brush SessionOutlineColor { get; set; }

[Browsable(false)]
public string SessionOutlineColorSerialize
{
    get { return Serialize.BrushToString(SessionOutlineColor); }
    set { SessionOutlineColor = Serialize.StringToBrush(value); }
}

[XmlIgnore]
[Display(Name = "Session PoC Color", Order = 2, GroupName = "16. Session Profile Colors")]
public Brush SessionPoCColor { get; set; }

[Browsable(false)]
public string SessionPoCColorSerialize
{
    get { return Serialize.BrushToString(SessionPoCColor); }
    set { SessionPoCColor = Serialize.StringToBrush(value); }
}

[XmlIgnore]
[Display(Name = "Session VA Lines Color", Order = 3, GroupName = "16. Session Profile Colors")]
public Brush SessionVALinesColor { get; set; }

[XmlIgnore]
[Display(Name = "Session VA Bar Color", Description = "Color for Value Area bars in filled mode", Order = 4, GroupName = "16. Session Profile Colors")]
public Brush SessionVAColor { get; set; }

[Browsable(false)]
public string SessionVAColorSerialize
{
    get { return Serialize.BrushToString(SessionVAColor); }
    set { SessionVAColor = Serialize.StringToBrush(value); }
}

[Browsable(false)]
public string SessionVALinesColorSerialize
{
    get { return Serialize.BrushToString(SessionVALinesColor); }
    set { SessionVALinesColor = Serialize.StringToBrush(value); }
}

// ============================================
// GROUP 17: GRADIENT FILL (was GROUP 16)
// ============================================

[NinjaScriptProperty]
[Display(Name = "Enable Gradient Fill", Description = "Apply gradient effect to volume bars (fade in from left to right)", Order = 1, GroupName = "17. Gradient Fill")]
public bool EnableGradientFill { get; set; }

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "Gradient Intensity", Description = "0=no fade (solid), 100=maximum fade effect", Order = 2, GroupName = "17. Gradient Fill")]
public int GradientIntensity { get; set; }

// ============================================
// GROUP 18: LVN DETECTION (was GROUP 17)
// ============================================

[NinjaScriptProperty]
[Display(Name = "Display LVN", Description = "Enable/disable Low Volume Node detection and display", Order = 1, GroupName = "18. LVN Detection")]
public bool DisplayLVN { get; set; }

[NinjaScriptProperty]
[Range(20, 500)]
[Display(Name = "LVN Number of Rows", Description = "Granularity of LVN analysis. More rows = finer/noisier LVNs. Fewer rows = broader/smoother zones.", Order = 2, GroupName = "18. LVN Detection")]
public int LVNNumberOfRows { get; set; }

[NinjaScriptProperty]
[Range(1, 50)]
[Display(Name = "LVN Detection %", Description = "Percentage of rows used for LVN detection (lower = more sensitive)", Order = 3, GroupName = "18. LVN Detection")]
public int LVNDetectionPercent { get; set; }

[NinjaScriptProperty]
[Display(Name = "Show Adjacent LVN Nodes", Description = "Include nodes above and below each LVN (creates wider zones)", Order = 4, GroupName = "18. LVN Detection")]
public bool ShowAdjacentLVNNodes { get; set; }

// ============================================
// GROUP 19: LVN DISPLAY (was GROUP 18)
// ============================================

[XmlIgnore]
[Display(Name = "LVN Fill Color", Description = "Fill color for LVN rectangles", Order = 1, GroupName = "19. LVN Display")]
public Brush LVNFillColor { get; set; }

[Browsable(false)]
public string LVNFillColorSerializable
{
    get { return Serialize.BrushToString(LVNFillColor); }
    set { LVNFillColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "LVN Fill Opacity %", Description = "Opacity of LVN fill color (0=transparent, 100=solid)", Order = 2, GroupName = "19. LVN Display")]
public int LVNFillOpacity { get; set; }

[XmlIgnore]
[Display(Name = "LVN Border Color", Description = "Border color for LVN rectangles", Order = 3, GroupName = "19. LVN Display")]
public Brush LVNBorderColor { get; set; }

[Browsable(false)]
public string LVNBorderColorSerializable
{
    get { return Serialize.BrushToString(LVNBorderColor); }
    set { LVNBorderColor = Serialize.StringToBrush(value); }
}

[NinjaScriptProperty]
[Range(0, 100)]
[Display(Name = "LVN Border Opacity %", Description = "Opacity of LVN border (0=transparent, 100=solid)", Order = 4, GroupName = "19. LVN Display")]
public int LVNBorderOpacity { get; set; }

        // ============================================
        // DOM SETTINGS (Hidden from GUI)
        // ============================================

        [Browsable(false)]
        [NinjaScriptProperty]
        public bool EnableDomdicator { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public int DomdicatorWidth { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public int DomdicatorGap { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public int DomMaxRightExtension { get; set; }
		
		[Browsable(false)]
        [NinjaScriptProperty]
        public bool ShowDOMVolumeText { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public float DomMaxTextSize { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public float DomMinTextSize { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public float DomHistoricalOpacity { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public bool ShowHistoricalOrders { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public int LiveOrderTickThreshold { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public float DomLiveOpacity { get; set; }

        [Browsable(false)]
        [NinjaScriptProperty]
        public int MinimumOrdersToStart { get; set; }
		
		[Browsable(false)]
        [XmlIgnore]
        public Brush DomBidBrush { get; set; }

        [Browsable(false)]
        public string DomBidBrushSerialize
        {
            get { return Serialize.BrushToString(DomBidBrush); }
            set { DomBidBrush = Serialize.StringToBrush(value); }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Brush DomAskBrush { get; set; }

        [Browsable(false)]
        public string DomAskBrushSerialize
        {
            get { return Serialize.BrushToString(DomAskBrush); }
            set { DomAskBrush = Serialize.StringToBrush(value); }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Brush DomTextBrush { get; set; }

        [Browsable(false)]
        public string DomTextBrushSerialize
        {
            get { return Serialize.BrushToString(DomTextBrush); }
            set { DomTextBrush = Serialize.StringToBrush(value); }
        }

        [Browsable(false)]
        [XmlIgnore]
        public Brush DomOutlierBrush { get; set; }

        [Browsable(false)]
        public string DomOutlierBrushSerialize
        {
            get { return Serialize.BrushToString(DomOutlierBrush); }
            set { DomOutlierBrush = Serialize.StringToBrush(value); }
        }
		
		#endregion
		
		#region DOM Rendering

        private void RenderDOMVisualization(ChartControl chartControl, ChartScale chartScale)
        {
            if (!EnableDomdicator || State != State.Realtime)
                return;

            // Calculate DOM position based on profile mode
            float profileLeftEdge;
            float domRightEdge;
            
            if (EnableDualProfileMode)
            {
                // In dual mode: position left of session profile
                float weeklyRight = (float)chartControl.CanvasRight;
                float sessionRight = weeklyRight - WeeklyProfileWidth - ProfileGap;
                profileLeftEdge = sessionRight - SessionProfileWidth;
                domRightEdge = profileLeftEdge - DomdicatorGap;
            }
            else if (Alignment == ProfileAlignment.Right)
            {
                // In single mode with right alignment: position left of profile
                float profileRight = (float)chartControl.CanvasRight;
                profileLeftEdge = profileRight - ProfileWidth;
                domRightEdge = profileLeftEdge - DomdicatorGap;
            }
            else
            {
                // If profile is left-aligned, position DOM at right edge of chart
                domRightEdge = (float)chartControl.CanvasRight;
            }
            
            float domLeftEdge = domRightEdge - DomdicatorWidth;
            
            // Thread-safe copy of orders
            Dictionary<double, OrderInfo> threadSafeBidOrders;
            Dictionary<double, OrderInfo> threadSafeAskOrders;
            long currentMaxVolume;
            
            lock (orderLock)
            {
                if (cachedDOMDataDirty)
                {
                    cachedBidOrders.Clear();
                    cachedAskOrders.Clear();
                    foreach (var kvp in renderBidOrders) cachedBidOrders[kvp.Key] = kvp.Value;
                    foreach (var kvp in renderAskOrders) cachedAskOrders[kvp.Key] = kvp.Value;
                    cachedDOMDataDirty = false;
                }
                
                threadSafeBidOrders = cachedBidOrders;
                threadSafeAskOrders = cachedAskOrders;
                currentMaxVolume = maxDOMVolume;
            }

            int totalOrders = threadSafeBidOrders.Count + threadSafeAskOrders.Count;
            if (currentMaxVolume == 0 || totalOrders < MinimumOrdersToStart)
                return;

            double tickSize = Instrument.MasterInstrument.TickSize;
            double visibleHigh = chartScale.MaxValue;
            double visibleLow = chartScale.MinValue;
            
            if (cachedTickSize != tickSize || cachedDOMVisibleHigh != visibleHigh || cachedDOMVisibleLow != visibleLow)
            {
                cachedTickSize = tickSize;
                cachedDOMVisibleHigh = visibleHigh;
                cachedDOMVisibleLow = visibleLow;
                cachedDOMBarHeight = 0;
            }
            
            double visibleRange = visibleHigh - visibleLow;
            if (visibleRange <= 0 || Double.IsInfinity(visibleRange))
                return;

            float barHeight;
            if (cachedDOMBarHeight == 0)
            {
                float pixelsPerPrice = (float)ChartPanel.H / (float)visibleRange;
                float pixelsPerTick = pixelsPerPrice * (float)tickSize;
                float minBarHeight = 1.0f;
                float minGap = 3.0f;
                float absoluteMinBarHeight = 0.2f;
                float absoluteMinGap = 0.2f;
                float gap;
                float available = pixelsPerTick;
                float scale = available / (minBarHeight + minGap);
                if (scale < 1.0f) {
                    barHeight = Math.Max(absoluteMinBarHeight, minBarHeight * scale);
                    gap = Math.Max(absoluteMinGap, minGap * scale);
                } else {
                    barHeight = available - minGap;
                    gap = minGap;
                }
                barHeight = Math.Max(1.0f, barHeight);
                cachedDOMBarHeight = barHeight;
            }
            else
            {
                barHeight = cachedDOMBarHeight;
            }

            // Create brushes
            SharpDX.Direct2D1.SolidColorBrush dxBrushBid = null;
            SharpDX.Direct2D1.SolidColorBrush dxBrushAsk = null;
            SharpDX.Direct2D1.SolidColorBrush dxBrushText = null;
            SharpDX.Direct2D1.SolidColorBrush dxBrushOutlier = null;

            if (cachedDxBrushBid == null || cachedDxBrushAsk == null || cachedDxBrushText == null || cachedDxBrushOutlier == null || 
                (DateTime.Now - lastBrushUpdate).TotalMilliseconds > BRUSH_UPDATE_INTERVAL_MS)
            {
                try
                {
                    if (cachedDxBrushBid != null) { cachedDxBrushBid.Dispose(); cachedDxBrushBid = null; }
                    if (cachedDxBrushAsk != null) { cachedDxBrushAsk.Dispose(); cachedDxBrushAsk = null; }
                    if (cachedDxBrushText != null) { cachedDxBrushText.Dispose(); cachedDxBrushText = null; }
                    if (cachedDxBrushOutlier != null) { cachedDxBrushOutlier.Dispose(); cachedDxBrushOutlier = null; }
                    
                    if (RenderTarget != null)
                    {
                        cachedDxBrushBid = ((SolidColorBrush)DomBidBrush).ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                        cachedDxBrushAsk = ((SolidColorBrush)DomAskBrush).ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                        cachedDxBrushText = ((SolidColorBrush)DomTextBrush).ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                        cachedDxBrushOutlier = ((SolidColorBrush)DomOutlierBrush).ToDxBrush(RenderTarget) as SharpDX.Direct2D1.SolidColorBrush;
                        
                        lastBrushUpdate = DateTime.Now;
                    }
                }
                catch
                {
                    if (cachedDxBrushBid != null) { cachedDxBrushBid.Dispose(); cachedDxBrushBid = null; }
                    if (cachedDxBrushAsk != null) { cachedDxBrushAsk.Dispose(); cachedDxBrushAsk = null; }
                    if (cachedDxBrushText != null) { cachedDxBrushText.Dispose(); cachedDxBrushText = null; }
                    if (cachedDxBrushOutlier != null) { cachedDxBrushOutlier.Dispose(); cachedDxBrushOutlier = null; }
                    return;
                }
            }
            
            dxBrushBid = cachedDxBrushBid;
            dxBrushAsk = cachedDxBrushAsk;
            dxBrushText = cachedDxBrushText;
            dxBrushOutlier = cachedDxBrushOutlier;

            if (dxBrushBid == null || dxBrushAsk == null || dxBrushText == null || dxBrushOutlier == null)
                return;

            var renderedPrices = new HashSet<double>();
            var renderedNumbers = new HashSet<string>();

            try
            {
                double dynamicThreshold = cachedDynamicThreshold;
                if ((DateTime.Now - lastThresholdUpdate).TotalMilliseconds > THRESHOLD_UPDATE_INTERVAL_MS)
                {
                    dynamicThreshold = CalculateDynamicThreshold();
                    cachedDynamicThreshold = dynamicThreshold;
                    lastThresholdUpdate = DateTime.Now;
                }

                // Render asks
                var sortedAsks = threadSafeAskOrders.Values
                    .Where(ask => ask.Volume > 0 && ask.Price >= visibleLow && ask.Price <= visibleHigh)
                    .Where(ask => currentAskPrice == double.MaxValue || ask.Price >= currentAskPrice)
                    .OrderBy(ask => ask.Price)
                    .ToList();
                    
                foreach (var ask in sortedAsks)
                {
                    double priceDistance = currentAskPrice != double.MaxValue ? Math.Abs(ask.Price - currentAskPrice) : 0;
                    int ticksAway = (int)(priceDistance / tickSize);
                    if (!ShowHistoricalOrders && ticksAway > LiveOrderTickThreshold)
                        continue;
                    bool isHistorical = ticksAway > LiveOrderTickThreshold;
                    if (renderedPrices.Contains(ask.Price))
                        continue;
                    renderedPrices.Add(ask.Price);
                    bool isOutlier = ask.Volume > outlierThreshold;
                    var brush = isOutlier ? dxBrushOutlier : dxBrushAsk;
                    RenderDOMBar(ask.Price, ask.Volume, false, currentMaxVolume, barHeight, chartScale, brush, dxBrushText, 
                                tickSize, isOutlier, dynamicThreshold, renderedNumbers, isHistorical, domLeftEdge, domRightEdge);
                }

                // Render bids
                var sortedBids = threadSafeBidOrders.Values
                    .Where(bid => bid.Volume > 0 && bid.Price >= visibleLow && bid.Price <= visibleHigh)
                    .Where(bid => currentBidPrice == double.MinValue || bid.Price <= currentBidPrice)
                    .OrderByDescending(bid => bid.Price)
                    .ToList();
                    
                foreach (var bid in sortedBids)
                {
                    double priceDistance = currentBidPrice != double.MinValue ? Math.Abs(bid.Price - currentBidPrice) : 0;
                    int ticksAway = (int)(priceDistance / tickSize);
                    if (!ShowHistoricalOrders && ticksAway > LiveOrderTickThreshold)
                        continue;
                    bool isHistorical = ticksAway > LiveOrderTickThreshold;
                    if (renderedPrices.Contains(bid.Price))
                        continue;
                    renderedPrices.Add(bid.Price);
                    bool isOutlier = bid.Volume > outlierThreshold;
                    var brush = isOutlier ? dxBrushOutlier : dxBrushBid;
                    RenderDOMBar(bid.Price, bid.Volume, true, currentMaxVolume, barHeight, chartScale, brush, dxBrushText, 
                                tickSize, isOutlier, dynamicThreshold, renderedNumbers, isHistorical, domLeftEdge, domRightEdge);
                }
            }
            catch (Exception ex)
            {
                // Silently handle errors
            }
        }
		
		private void RenderDOMBar(double price, long volume, bool isBid, long maxVolume, float barHeight, 
            ChartScale chartScale, SharpDX.Direct2D1.SolidColorBrush brush, SharpDX.Direct2D1.SolidColorBrush textBrush,
            double tickSize, bool isOutlier, double dynamicThreshold, HashSet<string> renderedNumbers, bool isHistorical,
            float domLeftEdge, float domRightEdge)
        {
            float y = chartScale.GetYByValue(price);
            if (y < 0 || y > ChartPanel.H)
                return;

            float distanceOpacity = CalculateDOMOpacity(isHistorical);
            float volumeRatio;
            if (isOutlier || maxVolume <= 0)
            {
                volumeRatio = 1.0f;
            }
            else
            {
                volumeRatio = (float)volume / (float)maxVolume;
            }
            float maxBarWidth = domRightEdge - domLeftEdge; // Use actual DOM width, not DomMaxRightExtension
            float barWidth = Math.Min(maxBarWidth, volumeRatio * maxBarWidth);
            float x = domLeftEdge;

            var rect = new SharpDX.RectangleF(
                x,
                y - barHeight / 2,
                barWidth,
                barHeight
            );
            brush.Opacity = distanceOpacity;
            RenderTarget.FillRectangle(rect, brush);

            bool showText = ShowDOMVolumeText && (isOutlier || volume >= dynamicThreshold);
            string num = volume.ToString("N0");
            if (showText && !renderedNumbers.Contains(num))
            {
                renderedNumbers.Add(num);
                float textSizeRange = DomMaxTextSize - DomMinTextSize;
                float textSize = Math.Min(DomMaxTextSize, Math.Max(DomMinTextSize, DomMinTextSize + (textSizeRange * volumeRatio)));
                
                var textFormat = GetTextFormat(textSize);
                textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
                textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                float textX = rect.X + barWidth + 5;
                var textRect = new SharpDX.RectangleF(
                    textX,
                    rect.Y,
                    50,
                    rect.Height
                );
                textBrush.Opacity = distanceOpacity;
                RenderTarget.DrawText(
                    num,
                    textFormat,
                    textRect,
                    textBrush
                );
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
		private RedTailVolumeProfile_V2[] cacheRedTailVolumeProfile_V2;
		public RedTailVolumeProfile_V2 RedTailVolumeProfile_V2(ProfileModeEnum profileMode, ProfileAlignment alignment, int weeksLookback, int sessionsLookback, int monthsLookback, CompositeDateRangeType compositeRangeType, int compositeDaysBack, int compositeWeeksBack, int compositeMonthsBack, DateTime compositeCustomStartDate, DateTime compositeCustomEndDate, bool useCustomSessionTimes, int sessionStartTime, int sessionEndTime, int numberOfVolumeBars, int barThickness, int profileWidth, VolumeTypeEnum volumeType, int barOpacity, bool displayPoC, int poCLineThickness, DashStyleHelper poCLineStyle, int poCLineOpacity, bool extendPoCLine, bool displayValueArea, int valueAreaPercentage, bool displayValueAreaLines, int valueAreaLinesThickness, DashStyleHelper valueAreaLineStyle, int valueAreaLinesOpacity, bool extendValueAreaLines, bool displayPreviousDayPOC, DashStyleHelper pdPOCLineStyle, int pdPOCThickness, int pdPOCOpacity, bool displayPreviousDayVAH, DashStyleHelper pdVAHLineStyle, int pdVAHThickness, int pdVAHOpacity, bool displayPreviousDayVAL, DashStyleHelper pdVALLineStyle, int pdVALThickness, int pdVALOpacity, bool displayPreviousDayHigh, DashStyleHelper pdHighLineStyle, int pdHighThickness, int pdHighOpacity, bool displayPreviousDayLow, DashStyleHelper pdLowLineStyle, int pdLowThickness, int pdLowOpacity, bool displayOvernightPOC, DashStyleHelper overnightPOCLineStyle, int overnightPOCThickness, int overnightPOCOpacity, bool displayOvernightVAH, DashStyleHelper overnightVAHLineStyle, int overnightVAHThickness, int overnightVAHOpacity, bool displayOvernightVAL, DashStyleHelper overnightVALLineStyle, int overnightVALThickness, int overnightVALOpacity, bool displayOvernightHigh, DashStyleHelper overnightHighLineStyle, int overnightHighThickness, int overnightHighOpacity, bool displayOvernightLow, DashStyleHelper overnightLowLineStyle, int overnightLowThickness, int overnightLowOpacity, int overnightStartTime, int overnightEndTime, DashStyleHelper nakedPOCLineStyle, int nakedPOCThickness, int nakedPOCOpacity, DashStyleHelper nakedVAHLineStyle, int nakedVAHThickness, int nakedVAHOpacity, DashStyleHelper nakedVALLineStyle, int nakedVALThickness, int nakedVALOpacity, DashStyleHelper weeklyNakedPOCLineStyle, int weeklyNakedPOCThickness, int weeklyNakedPOCOpacity, DashStyleHelper weeklyNakedVAHLineStyle, int weeklyNakedVAHThickness, int weeklyNakedVAHOpacity, DashStyleHelper weeklyNakedVALLineStyle, int weeklyNakedVALThickness, int weeklyNakedVALOpacity, bool displayNakedLevels, int maxNakedLevelsToDisplay, bool displayWeeklyNakedLevels, int maxWeeklyNakedLevelsToDisplay, bool keepFilledLevelsAfterSession, int removeAfterTouchCount, bool keepFilledWeeklyLevelsAfterWeek, int removeWeeklyAfterTouchCount, bool showTouchCountInLabels, bool britishDateFormat, int previousDayLineWidth, bool showPriceValuesInLabels, bool enableDualProfileMode, int weeklyProfileWidth, int sessionProfileWidth, int profileGap, bool useCustomDailySessionTimes, int dailySessionStartTime, int dailySessionEndTime, SessionProfileStyleEnum sessionProfileStyle, int sessionOutlineSmoothness, int weeklyNumberOfVolumeBars, int weeklyBarThickness, VolumeTypeEnum weeklyVolumeType, int weeklyBarOpacity, bool weeklyDisplayPoC, int weeklyPoCLineThickness, int weeklyPoCLineOpacity, bool weeklyDisplayValueArea, int weeklyValueAreaPercentage, bool weeklyDisplayValueAreaLines, int weeklyValueAreaLinesThickness, int weeklyValueAreaLinesOpacity, bool weeklyExtendPoCLine, bool weeklyExtendValueAreaLines, int sessionNumberOfVolumeBars, int sessionBarThickness, VolumeTypeEnum sessionVolumeType, int sessionBarOpacity, bool sessionDisplayPoC, int sessionPoCLineThickness, int sessionPoCLineOpacity, bool sessionDisplayValueArea, int sessionValueAreaPercentage, bool sessionDisplayValueAreaLines, int sessionValueAreaLinesThickness, int sessionValueAreaLinesOpacity, bool sessionExtendPoCLine, bool sessionExtendValueAreaLines, bool enableGradientFill, int gradientIntensity, bool displayLVN, int lVNNumberOfRows, int lVNDetectionPercent, bool showAdjacentLVNNodes, int lVNFillOpacity, int lVNBorderOpacity, bool enableDomdicator, int domdicatorWidth, int domdicatorGap, int domMaxRightExtension, bool showDOMVolumeText, float domMaxTextSize, float domMinTextSize, float domHistoricalOpacity, bool showHistoricalOrders, int liveOrderTickThreshold, float domLiveOpacity, int minimumOrdersToStart)
		{
			return RedTailVolumeProfile_V2(Input, profileMode, alignment, weeksLookback, sessionsLookback, monthsLookback, compositeRangeType, compositeDaysBack, compositeWeeksBack, compositeMonthsBack, compositeCustomStartDate, compositeCustomEndDate, useCustomSessionTimes, sessionStartTime, sessionEndTime, numberOfVolumeBars, barThickness, profileWidth, volumeType, barOpacity, displayPoC, poCLineThickness, poCLineStyle, poCLineOpacity, extendPoCLine, displayValueArea, valueAreaPercentage, displayValueAreaLines, valueAreaLinesThickness, valueAreaLineStyle, valueAreaLinesOpacity, extendValueAreaLines, displayPreviousDayPOC, pdPOCLineStyle, pdPOCThickness, pdPOCOpacity, displayPreviousDayVAH, pdVAHLineStyle, pdVAHThickness, pdVAHOpacity, displayPreviousDayVAL, pdVALLineStyle, pdVALThickness, pdVALOpacity, displayPreviousDayHigh, pdHighLineStyle, pdHighThickness, pdHighOpacity, displayPreviousDayLow, pdLowLineStyle, pdLowThickness, pdLowOpacity, displayOvernightPOC, overnightPOCLineStyle, overnightPOCThickness, overnightPOCOpacity, displayOvernightVAH, overnightVAHLineStyle, overnightVAHThickness, overnightVAHOpacity, displayOvernightVAL, overnightVALLineStyle, overnightVALThickness, overnightVALOpacity, displayOvernightHigh, overnightHighLineStyle, overnightHighThickness, overnightHighOpacity, displayOvernightLow, overnightLowLineStyle, overnightLowThickness, overnightLowOpacity, overnightStartTime, overnightEndTime, nakedPOCLineStyle, nakedPOCThickness, nakedPOCOpacity, nakedVAHLineStyle, nakedVAHThickness, nakedVAHOpacity, nakedVALLineStyle, nakedVALThickness, nakedVALOpacity, weeklyNakedPOCLineStyle, weeklyNakedPOCThickness, weeklyNakedPOCOpacity, weeklyNakedVAHLineStyle, weeklyNakedVAHThickness, weeklyNakedVAHOpacity, weeklyNakedVALLineStyle, weeklyNakedVALThickness, weeklyNakedVALOpacity, displayNakedLevels, maxNakedLevelsToDisplay, displayWeeklyNakedLevels, maxWeeklyNakedLevelsToDisplay, keepFilledLevelsAfterSession, removeAfterTouchCount, keepFilledWeeklyLevelsAfterWeek, removeWeeklyAfterTouchCount, showTouchCountInLabels, britishDateFormat, previousDayLineWidth, showPriceValuesInLabels, enableDualProfileMode, weeklyProfileWidth, sessionProfileWidth, profileGap, useCustomDailySessionTimes, dailySessionStartTime, dailySessionEndTime, sessionProfileStyle, sessionOutlineSmoothness, weeklyNumberOfVolumeBars, weeklyBarThickness, weeklyVolumeType, weeklyBarOpacity, weeklyDisplayPoC, weeklyPoCLineThickness, weeklyPoCLineOpacity, weeklyDisplayValueArea, weeklyValueAreaPercentage, weeklyDisplayValueAreaLines, weeklyValueAreaLinesThickness, weeklyValueAreaLinesOpacity, weeklyExtendPoCLine, weeklyExtendValueAreaLines, sessionNumberOfVolumeBars, sessionBarThickness, sessionVolumeType, sessionBarOpacity, sessionDisplayPoC, sessionPoCLineThickness, sessionPoCLineOpacity, sessionDisplayValueArea, sessionValueAreaPercentage, sessionDisplayValueAreaLines, sessionValueAreaLinesThickness, sessionValueAreaLinesOpacity, sessionExtendPoCLine, sessionExtendValueAreaLines, enableGradientFill, gradientIntensity, displayLVN, lVNNumberOfRows, lVNDetectionPercent, showAdjacentLVNNodes, lVNFillOpacity, lVNBorderOpacity, enableDomdicator, domdicatorWidth, domdicatorGap, domMaxRightExtension, showDOMVolumeText, domMaxTextSize, domMinTextSize, domHistoricalOpacity, showHistoricalOrders, liveOrderTickThreshold, domLiveOpacity, minimumOrdersToStart);
		}

		public RedTailVolumeProfile_V2 RedTailVolumeProfile_V2(ISeries<double> input, ProfileModeEnum profileMode, ProfileAlignment alignment, int weeksLookback, int sessionsLookback, int monthsLookback, CompositeDateRangeType compositeRangeType, int compositeDaysBack, int compositeWeeksBack, int compositeMonthsBack, DateTime compositeCustomStartDate, DateTime compositeCustomEndDate, bool useCustomSessionTimes, int sessionStartTime, int sessionEndTime, int numberOfVolumeBars, int barThickness, int profileWidth, VolumeTypeEnum volumeType, int barOpacity, bool displayPoC, int poCLineThickness, DashStyleHelper poCLineStyle, int poCLineOpacity, bool extendPoCLine, bool displayValueArea, int valueAreaPercentage, bool displayValueAreaLines, int valueAreaLinesThickness, DashStyleHelper valueAreaLineStyle, int valueAreaLinesOpacity, bool extendValueAreaLines, bool displayPreviousDayPOC, DashStyleHelper pdPOCLineStyle, int pdPOCThickness, int pdPOCOpacity, bool displayPreviousDayVAH, DashStyleHelper pdVAHLineStyle, int pdVAHThickness, int pdVAHOpacity, bool displayPreviousDayVAL, DashStyleHelper pdVALLineStyle, int pdVALThickness, int pdVALOpacity, bool displayPreviousDayHigh, DashStyleHelper pdHighLineStyle, int pdHighThickness, int pdHighOpacity, bool displayPreviousDayLow, DashStyleHelper pdLowLineStyle, int pdLowThickness, int pdLowOpacity, bool displayOvernightPOC, DashStyleHelper overnightPOCLineStyle, int overnightPOCThickness, int overnightPOCOpacity, bool displayOvernightVAH, DashStyleHelper overnightVAHLineStyle, int overnightVAHThickness, int overnightVAHOpacity, bool displayOvernightVAL, DashStyleHelper overnightVALLineStyle, int overnightVALThickness, int overnightVALOpacity, bool displayOvernightHigh, DashStyleHelper overnightHighLineStyle, int overnightHighThickness, int overnightHighOpacity, bool displayOvernightLow, DashStyleHelper overnightLowLineStyle, int overnightLowThickness, int overnightLowOpacity, int overnightStartTime, int overnightEndTime, DashStyleHelper nakedPOCLineStyle, int nakedPOCThickness, int nakedPOCOpacity, DashStyleHelper nakedVAHLineStyle, int nakedVAHThickness, int nakedVAHOpacity, DashStyleHelper nakedVALLineStyle, int nakedVALThickness, int nakedVALOpacity, DashStyleHelper weeklyNakedPOCLineStyle, int weeklyNakedPOCThickness, int weeklyNakedPOCOpacity, DashStyleHelper weeklyNakedVAHLineStyle, int weeklyNakedVAHThickness, int weeklyNakedVAHOpacity, DashStyleHelper weeklyNakedVALLineStyle, int weeklyNakedVALThickness, int weeklyNakedVALOpacity, bool displayNakedLevels, int maxNakedLevelsToDisplay, bool displayWeeklyNakedLevels, int maxWeeklyNakedLevelsToDisplay, bool keepFilledLevelsAfterSession, int removeAfterTouchCount, bool keepFilledWeeklyLevelsAfterWeek, int removeWeeklyAfterTouchCount, bool showTouchCountInLabels, bool britishDateFormat, int previousDayLineWidth, bool showPriceValuesInLabels, bool enableDualProfileMode, int weeklyProfileWidth, int sessionProfileWidth, int profileGap, bool useCustomDailySessionTimes, int dailySessionStartTime, int dailySessionEndTime, SessionProfileStyleEnum sessionProfileStyle, int sessionOutlineSmoothness, int weeklyNumberOfVolumeBars, int weeklyBarThickness, VolumeTypeEnum weeklyVolumeType, int weeklyBarOpacity, bool weeklyDisplayPoC, int weeklyPoCLineThickness, int weeklyPoCLineOpacity, bool weeklyDisplayValueArea, int weeklyValueAreaPercentage, bool weeklyDisplayValueAreaLines, int weeklyValueAreaLinesThickness, int weeklyValueAreaLinesOpacity, bool weeklyExtendPoCLine, bool weeklyExtendValueAreaLines, int sessionNumberOfVolumeBars, int sessionBarThickness, VolumeTypeEnum sessionVolumeType, int sessionBarOpacity, bool sessionDisplayPoC, int sessionPoCLineThickness, int sessionPoCLineOpacity, bool sessionDisplayValueArea, int sessionValueAreaPercentage, bool sessionDisplayValueAreaLines, int sessionValueAreaLinesThickness, int sessionValueAreaLinesOpacity, bool sessionExtendPoCLine, bool sessionExtendValueAreaLines, bool enableGradientFill, int gradientIntensity, bool displayLVN, int lVNNumberOfRows, int lVNDetectionPercent, bool showAdjacentLVNNodes, int lVNFillOpacity, int lVNBorderOpacity, bool enableDomdicator, int domdicatorWidth, int domdicatorGap, int domMaxRightExtension, bool showDOMVolumeText, float domMaxTextSize, float domMinTextSize, float domHistoricalOpacity, bool showHistoricalOrders, int liveOrderTickThreshold, float domLiveOpacity, int minimumOrdersToStart)
		{
			if (cacheRedTailVolumeProfile_V2 != null)
				for (int idx = 0; idx < cacheRedTailVolumeProfile_V2.Length; idx++)
					if (cacheRedTailVolumeProfile_V2[idx] != null && cacheRedTailVolumeProfile_V2[idx].ProfileMode == profileMode && cacheRedTailVolumeProfile_V2[idx].Alignment == alignment && cacheRedTailVolumeProfile_V2[idx].WeeksLookback == weeksLookback && cacheRedTailVolumeProfile_V2[idx].SessionsLookback == sessionsLookback && cacheRedTailVolumeProfile_V2[idx].MonthsLookback == monthsLookback && cacheRedTailVolumeProfile_V2[idx].CompositeRangeType == compositeRangeType && cacheRedTailVolumeProfile_V2[idx].CompositeDaysBack == compositeDaysBack && cacheRedTailVolumeProfile_V2[idx].CompositeWeeksBack == compositeWeeksBack && cacheRedTailVolumeProfile_V2[idx].CompositeMonthsBack == compositeMonthsBack && cacheRedTailVolumeProfile_V2[idx].CompositeCustomStartDate == compositeCustomStartDate && cacheRedTailVolumeProfile_V2[idx].CompositeCustomEndDate == compositeCustomEndDate && cacheRedTailVolumeProfile_V2[idx].UseCustomSessionTimes == useCustomSessionTimes && cacheRedTailVolumeProfile_V2[idx].SessionStartTime == sessionStartTime && cacheRedTailVolumeProfile_V2[idx].SessionEndTime == sessionEndTime && cacheRedTailVolumeProfile_V2[idx].NumberOfVolumeBars == numberOfVolumeBars && cacheRedTailVolumeProfile_V2[idx].BarThickness == barThickness && cacheRedTailVolumeProfile_V2[idx].ProfileWidth == profileWidth && cacheRedTailVolumeProfile_V2[idx].VolumeType == volumeType && cacheRedTailVolumeProfile_V2[idx].BarOpacity == barOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayPoC == displayPoC && cacheRedTailVolumeProfile_V2[idx].PoCLineThickness == poCLineThickness && cacheRedTailVolumeProfile_V2[idx].PoCLineStyle == poCLineStyle && cacheRedTailVolumeProfile_V2[idx].PoCLineOpacity == poCLineOpacity && cacheRedTailVolumeProfile_V2[idx].ExtendPoCLine == extendPoCLine && cacheRedTailVolumeProfile_V2[idx].DisplayValueArea == displayValueArea && cacheRedTailVolumeProfile_V2[idx].ValueAreaPercentage == valueAreaPercentage && cacheRedTailVolumeProfile_V2[idx].DisplayValueAreaLines == displayValueAreaLines && cacheRedTailVolumeProfile_V2[idx].ValueAreaLinesThickness == valueAreaLinesThickness && cacheRedTailVolumeProfile_V2[idx].ValueAreaLineStyle == valueAreaLineStyle && cacheRedTailVolumeProfile_V2[idx].ValueAreaLinesOpacity == valueAreaLinesOpacity && cacheRedTailVolumeProfile_V2[idx].ExtendValueAreaLines == extendValueAreaLines && cacheRedTailVolumeProfile_V2[idx].DisplayPreviousDayPOC == displayPreviousDayPOC && cacheRedTailVolumeProfile_V2[idx].PdPOCLineStyle == pdPOCLineStyle && cacheRedTailVolumeProfile_V2[idx].PdPOCThickness == pdPOCThickness && cacheRedTailVolumeProfile_V2[idx].PdPOCOpacity == pdPOCOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayPreviousDayVAH == displayPreviousDayVAH && cacheRedTailVolumeProfile_V2[idx].PdVAHLineStyle == pdVAHLineStyle && cacheRedTailVolumeProfile_V2[idx].PdVAHThickness == pdVAHThickness && cacheRedTailVolumeProfile_V2[idx].PdVAHOpacity == pdVAHOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayPreviousDayVAL == displayPreviousDayVAL && cacheRedTailVolumeProfile_V2[idx].PdVALLineStyle == pdVALLineStyle && cacheRedTailVolumeProfile_V2[idx].PdVALThickness == pdVALThickness && cacheRedTailVolumeProfile_V2[idx].PdVALOpacity == pdVALOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayPreviousDayHigh == displayPreviousDayHigh && cacheRedTailVolumeProfile_V2[idx].PdHighLineStyle == pdHighLineStyle && cacheRedTailVolumeProfile_V2[idx].PdHighThickness == pdHighThickness && cacheRedTailVolumeProfile_V2[idx].PdHighOpacity == pdHighOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayPreviousDayLow == displayPreviousDayLow && cacheRedTailVolumeProfile_V2[idx].PdLowLineStyle == pdLowLineStyle && cacheRedTailVolumeProfile_V2[idx].PdLowThickness == pdLowThickness && cacheRedTailVolumeProfile_V2[idx].PdLowOpacity == pdLowOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayOvernightPOC == displayOvernightPOC && cacheRedTailVolumeProfile_V2[idx].OvernightPOCLineStyle == overnightPOCLineStyle && cacheRedTailVolumeProfile_V2[idx].OvernightPOCThickness == overnightPOCThickness && cacheRedTailVolumeProfile_V2[idx].OvernightPOCOpacity == overnightPOCOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayOvernightVAH == displayOvernightVAH && cacheRedTailVolumeProfile_V2[idx].OvernightVAHLineStyle == overnightVAHLineStyle && cacheRedTailVolumeProfile_V2[idx].OvernightVAHThickness == overnightVAHThickness && cacheRedTailVolumeProfile_V2[idx].OvernightVAHOpacity == overnightVAHOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayOvernightVAL == displayOvernightVAL && cacheRedTailVolumeProfile_V2[idx].OvernightVALLineStyle == overnightVALLineStyle && cacheRedTailVolumeProfile_V2[idx].OvernightVALThickness == overnightVALThickness && cacheRedTailVolumeProfile_V2[idx].OvernightVALOpacity == overnightVALOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayOvernightHigh == displayOvernightHigh && cacheRedTailVolumeProfile_V2[idx].OvernightHighLineStyle == overnightHighLineStyle && cacheRedTailVolumeProfile_V2[idx].OvernightHighThickness == overnightHighThickness && cacheRedTailVolumeProfile_V2[idx].OvernightHighOpacity == overnightHighOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayOvernightLow == displayOvernightLow && cacheRedTailVolumeProfile_V2[idx].OvernightLowLineStyle == overnightLowLineStyle && cacheRedTailVolumeProfile_V2[idx].OvernightLowThickness == overnightLowThickness && cacheRedTailVolumeProfile_V2[idx].OvernightLowOpacity == overnightLowOpacity && cacheRedTailVolumeProfile_V2[idx].OvernightStartTime == overnightStartTime && cacheRedTailVolumeProfile_V2[idx].OvernightEndTime == overnightEndTime && cacheRedTailVolumeProfile_V2[idx].NakedPOCLineStyle == nakedPOCLineStyle && cacheRedTailVolumeProfile_V2[idx].NakedPOCThickness == nakedPOCThickness && cacheRedTailVolumeProfile_V2[idx].NakedPOCOpacity == nakedPOCOpacity && cacheRedTailVolumeProfile_V2[idx].NakedVAHLineStyle == nakedVAHLineStyle && cacheRedTailVolumeProfile_V2[idx].NakedVAHThickness == nakedVAHThickness && cacheRedTailVolumeProfile_V2[idx].NakedVAHOpacity == nakedVAHOpacity && cacheRedTailVolumeProfile_V2[idx].NakedVALLineStyle == nakedVALLineStyle && cacheRedTailVolumeProfile_V2[idx].NakedVALThickness == nakedVALThickness && cacheRedTailVolumeProfile_V2[idx].NakedVALOpacity == nakedVALOpacity && cacheRedTailVolumeProfile_V2[idx].WeeklyNakedPOCLineStyle == weeklyNakedPOCLineStyle && cacheRedTailVolumeProfile_V2[idx].WeeklyNakedPOCThickness == weeklyNakedPOCThickness && cacheRedTailVolumeProfile_V2[idx].WeeklyNakedPOCOpacity == weeklyNakedPOCOpacity && cacheRedTailVolumeProfile_V2[idx].WeeklyNakedVAHLineStyle == weeklyNakedVAHLineStyle && cacheRedTailVolumeProfile_V2[idx].WeeklyNakedVAHThickness == weeklyNakedVAHThickness && cacheRedTailVolumeProfile_V2[idx].WeeklyNakedVAHOpacity == weeklyNakedVAHOpacity && cacheRedTailVolumeProfile_V2[idx].WeeklyNakedVALLineStyle == weeklyNakedVALLineStyle && cacheRedTailVolumeProfile_V2[idx].WeeklyNakedVALThickness == weeklyNakedVALThickness && cacheRedTailVolumeProfile_V2[idx].WeeklyNakedVALOpacity == weeklyNakedVALOpacity && cacheRedTailVolumeProfile_V2[idx].DisplayNakedLevels == displayNakedLevels && cacheRedTailVolumeProfile_V2[idx].MaxNakedLevelsToDisplay == maxNakedLevelsToDisplay && cacheRedTailVolumeProfile_V2[idx].DisplayWeeklyNakedLevels == displayWeeklyNakedLevels && cacheRedTailVolumeProfile_V2[idx].MaxWeeklyNakedLevelsToDisplay == maxWeeklyNakedLevelsToDisplay && cacheRedTailVolumeProfile_V2[idx].KeepFilledLevelsAfterSession == keepFilledLevelsAfterSession && cacheRedTailVolumeProfile_V2[idx].RemoveAfterTouchCount == removeAfterTouchCount && cacheRedTailVolumeProfile_V2[idx].KeepFilledWeeklyLevelsAfterWeek == keepFilledWeeklyLevelsAfterWeek && cacheRedTailVolumeProfile_V2[idx].RemoveWeeklyAfterTouchCount == removeWeeklyAfterTouchCount && cacheRedTailVolumeProfile_V2[idx].ShowTouchCountInLabels == showTouchCountInLabels && cacheRedTailVolumeProfile_V2[idx].BritishDateFormat == britishDateFormat && cacheRedTailVolumeProfile_V2[idx].PreviousDayLineWidth == previousDayLineWidth && cacheRedTailVolumeProfile_V2[idx].ShowPriceValuesInLabels == showPriceValuesInLabels && cacheRedTailVolumeProfile_V2[idx].EnableDualProfileMode == enableDualProfileMode && cacheRedTailVolumeProfile_V2[idx].WeeklyProfileWidth == weeklyProfileWidth && cacheRedTailVolumeProfile_V2[idx].SessionProfileWidth == sessionProfileWidth && cacheRedTailVolumeProfile_V2[idx].ProfileGap == profileGap && cacheRedTailVolumeProfile_V2[idx].UseCustomDailySessionTimes == useCustomDailySessionTimes && cacheRedTailVolumeProfile_V2[idx].DailySessionStartTime == dailySessionStartTime && cacheRedTailVolumeProfile_V2[idx].DailySessionEndTime == dailySessionEndTime && cacheRedTailVolumeProfile_V2[idx].SessionProfileStyle == sessionProfileStyle && cacheRedTailVolumeProfile_V2[idx].SessionOutlineSmoothness == sessionOutlineSmoothness && cacheRedTailVolumeProfile_V2[idx].WeeklyNumberOfVolumeBars == weeklyNumberOfVolumeBars && cacheRedTailVolumeProfile_V2[idx].WeeklyBarThickness == weeklyBarThickness && cacheRedTailVolumeProfile_V2[idx].WeeklyVolumeType == weeklyVolumeType && cacheRedTailVolumeProfile_V2[idx].WeeklyBarOpacity == weeklyBarOpacity && cacheRedTailVolumeProfile_V2[idx].WeeklyDisplayPoC == weeklyDisplayPoC && cacheRedTailVolumeProfile_V2[idx].WeeklyPoCLineThickness == weeklyPoCLineThickness && cacheRedTailVolumeProfile_V2[idx].WeeklyPoCLineOpacity == weeklyPoCLineOpacity && cacheRedTailVolumeProfile_V2[idx].WeeklyDisplayValueArea == weeklyDisplayValueArea && cacheRedTailVolumeProfile_V2[idx].WeeklyValueAreaPercentage == weeklyValueAreaPercentage && cacheRedTailVolumeProfile_V2[idx].WeeklyDisplayValueAreaLines == weeklyDisplayValueAreaLines && cacheRedTailVolumeProfile_V2[idx].WeeklyValueAreaLinesThickness == weeklyValueAreaLinesThickness && cacheRedTailVolumeProfile_V2[idx].WeeklyValueAreaLinesOpacity == weeklyValueAreaLinesOpacity && cacheRedTailVolumeProfile_V2[idx].WeeklyExtendPoCLine == weeklyExtendPoCLine && cacheRedTailVolumeProfile_V2[idx].WeeklyExtendValueAreaLines == weeklyExtendValueAreaLines && cacheRedTailVolumeProfile_V2[idx].SessionNumberOfVolumeBars == sessionNumberOfVolumeBars && cacheRedTailVolumeProfile_V2[idx].SessionBarThickness == sessionBarThickness && cacheRedTailVolumeProfile_V2[idx].SessionVolumeType == sessionVolumeType && cacheRedTailVolumeProfile_V2[idx].SessionBarOpacity == sessionBarOpacity && cacheRedTailVolumeProfile_V2[idx].SessionDisplayPoC == sessionDisplayPoC && cacheRedTailVolumeProfile_V2[idx].SessionPoCLineThickness == sessionPoCLineThickness && cacheRedTailVolumeProfile_V2[idx].SessionPoCLineOpacity == sessionPoCLineOpacity && cacheRedTailVolumeProfile_V2[idx].SessionDisplayValueArea == sessionDisplayValueArea && cacheRedTailVolumeProfile_V2[idx].SessionValueAreaPercentage == sessionValueAreaPercentage && cacheRedTailVolumeProfile_V2[idx].SessionDisplayValueAreaLines == sessionDisplayValueAreaLines && cacheRedTailVolumeProfile_V2[idx].SessionValueAreaLinesThickness == sessionValueAreaLinesThickness && cacheRedTailVolumeProfile_V2[idx].SessionValueAreaLinesOpacity == sessionValueAreaLinesOpacity && cacheRedTailVolumeProfile_V2[idx].SessionExtendPoCLine == sessionExtendPoCLine && cacheRedTailVolumeProfile_V2[idx].SessionExtendValueAreaLines == sessionExtendValueAreaLines && cacheRedTailVolumeProfile_V2[idx].EnableGradientFill == enableGradientFill && cacheRedTailVolumeProfile_V2[idx].GradientIntensity == gradientIntensity && cacheRedTailVolumeProfile_V2[idx].DisplayLVN == displayLVN && cacheRedTailVolumeProfile_V2[idx].LVNNumberOfRows == lVNNumberOfRows && cacheRedTailVolumeProfile_V2[idx].LVNDetectionPercent == lVNDetectionPercent && cacheRedTailVolumeProfile_V2[idx].ShowAdjacentLVNNodes == showAdjacentLVNNodes && cacheRedTailVolumeProfile_V2[idx].LVNFillOpacity == lVNFillOpacity && cacheRedTailVolumeProfile_V2[idx].LVNBorderOpacity == lVNBorderOpacity && cacheRedTailVolumeProfile_V2[idx].EnableDomdicator == enableDomdicator && cacheRedTailVolumeProfile_V2[idx].DomdicatorWidth == domdicatorWidth && cacheRedTailVolumeProfile_V2[idx].DomdicatorGap == domdicatorGap && cacheRedTailVolumeProfile_V2[idx].DomMaxRightExtension == domMaxRightExtension && cacheRedTailVolumeProfile_V2[idx].ShowDOMVolumeText == showDOMVolumeText && cacheRedTailVolumeProfile_V2[idx].DomMaxTextSize == domMaxTextSize && cacheRedTailVolumeProfile_V2[idx].DomMinTextSize == domMinTextSize && cacheRedTailVolumeProfile_V2[idx].DomHistoricalOpacity == domHistoricalOpacity && cacheRedTailVolumeProfile_V2[idx].ShowHistoricalOrders == showHistoricalOrders && cacheRedTailVolumeProfile_V2[idx].LiveOrderTickThreshold == liveOrderTickThreshold && cacheRedTailVolumeProfile_V2[idx].DomLiveOpacity == domLiveOpacity && cacheRedTailVolumeProfile_V2[idx].MinimumOrdersToStart == minimumOrdersToStart && cacheRedTailVolumeProfile_V2[idx].EqualsInput(input))
						return cacheRedTailVolumeProfile_V2[idx];
			return CacheIndicator<RedTailVolumeProfile_V2>(new RedTailVolumeProfile_V2(){ ProfileMode = profileMode, Alignment = alignment, WeeksLookback = weeksLookback, SessionsLookback = sessionsLookback, MonthsLookback = monthsLookback, CompositeRangeType = compositeRangeType, CompositeDaysBack = compositeDaysBack, CompositeWeeksBack = compositeWeeksBack, CompositeMonthsBack = compositeMonthsBack, CompositeCustomStartDate = compositeCustomStartDate, CompositeCustomEndDate = compositeCustomEndDate, UseCustomSessionTimes = useCustomSessionTimes, SessionStartTime = sessionStartTime, SessionEndTime = sessionEndTime, NumberOfVolumeBars = numberOfVolumeBars, BarThickness = barThickness, ProfileWidth = profileWidth, VolumeType = volumeType, BarOpacity = barOpacity, DisplayPoC = displayPoC, PoCLineThickness = poCLineThickness, PoCLineStyle = poCLineStyle, PoCLineOpacity = poCLineOpacity, ExtendPoCLine = extendPoCLine, DisplayValueArea = displayValueArea, ValueAreaPercentage = valueAreaPercentage, DisplayValueAreaLines = displayValueAreaLines, ValueAreaLinesThickness = valueAreaLinesThickness, ValueAreaLineStyle = valueAreaLineStyle, ValueAreaLinesOpacity = valueAreaLinesOpacity, ExtendValueAreaLines = extendValueAreaLines, DisplayPreviousDayPOC = displayPreviousDayPOC, PdPOCLineStyle = pdPOCLineStyle, PdPOCThickness = pdPOCThickness, PdPOCOpacity = pdPOCOpacity, DisplayPreviousDayVAH = displayPreviousDayVAH, PdVAHLineStyle = pdVAHLineStyle, PdVAHThickness = pdVAHThickness, PdVAHOpacity = pdVAHOpacity, DisplayPreviousDayVAL = displayPreviousDayVAL, PdVALLineStyle = pdVALLineStyle, PdVALThickness = pdVALThickness, PdVALOpacity = pdVALOpacity, DisplayPreviousDayHigh = displayPreviousDayHigh, PdHighLineStyle = pdHighLineStyle, PdHighThickness = pdHighThickness, PdHighOpacity = pdHighOpacity, DisplayPreviousDayLow = displayPreviousDayLow, PdLowLineStyle = pdLowLineStyle, PdLowThickness = pdLowThickness, PdLowOpacity = pdLowOpacity, DisplayOvernightPOC = displayOvernightPOC, OvernightPOCLineStyle = overnightPOCLineStyle, OvernightPOCThickness = overnightPOCThickness, OvernightPOCOpacity = overnightPOCOpacity, DisplayOvernightVAH = displayOvernightVAH, OvernightVAHLineStyle = overnightVAHLineStyle, OvernightVAHThickness = overnightVAHThickness, OvernightVAHOpacity = overnightVAHOpacity, DisplayOvernightVAL = displayOvernightVAL, OvernightVALLineStyle = overnightVALLineStyle, OvernightVALThickness = overnightVALThickness, OvernightVALOpacity = overnightVALOpacity, DisplayOvernightHigh = displayOvernightHigh, OvernightHighLineStyle = overnightHighLineStyle, OvernightHighThickness = overnightHighThickness, OvernightHighOpacity = overnightHighOpacity, DisplayOvernightLow = displayOvernightLow, OvernightLowLineStyle = overnightLowLineStyle, OvernightLowThickness = overnightLowThickness, OvernightLowOpacity = overnightLowOpacity, OvernightStartTime = overnightStartTime, OvernightEndTime = overnightEndTime, NakedPOCLineStyle = nakedPOCLineStyle, NakedPOCThickness = nakedPOCThickness, NakedPOCOpacity = nakedPOCOpacity, NakedVAHLineStyle = nakedVAHLineStyle, NakedVAHThickness = nakedVAHThickness, NakedVAHOpacity = nakedVAHOpacity, NakedVALLineStyle = nakedVALLineStyle, NakedVALThickness = nakedVALThickness, NakedVALOpacity = nakedVALOpacity, WeeklyNakedPOCLineStyle = weeklyNakedPOCLineStyle, WeeklyNakedPOCThickness = weeklyNakedPOCThickness, WeeklyNakedPOCOpacity = weeklyNakedPOCOpacity, WeeklyNakedVAHLineStyle = weeklyNakedVAHLineStyle, WeeklyNakedVAHThickness = weeklyNakedVAHThickness, WeeklyNakedVAHOpacity = weeklyNakedVAHOpacity, WeeklyNakedVALLineStyle = weeklyNakedVALLineStyle, WeeklyNakedVALThickness = weeklyNakedVALThickness, WeeklyNakedVALOpacity = weeklyNakedVALOpacity, DisplayNakedLevels = displayNakedLevels, MaxNakedLevelsToDisplay = maxNakedLevelsToDisplay, DisplayWeeklyNakedLevels = displayWeeklyNakedLevels, MaxWeeklyNakedLevelsToDisplay = maxWeeklyNakedLevelsToDisplay, KeepFilledLevelsAfterSession = keepFilledLevelsAfterSession, RemoveAfterTouchCount = removeAfterTouchCount, KeepFilledWeeklyLevelsAfterWeek = keepFilledWeeklyLevelsAfterWeek, RemoveWeeklyAfterTouchCount = removeWeeklyAfterTouchCount, ShowTouchCountInLabels = showTouchCountInLabels, BritishDateFormat = britishDateFormat, PreviousDayLineWidth = previousDayLineWidth, ShowPriceValuesInLabels = showPriceValuesInLabels, EnableDualProfileMode = enableDualProfileMode, WeeklyProfileWidth = weeklyProfileWidth, SessionProfileWidth = sessionProfileWidth, ProfileGap = profileGap, UseCustomDailySessionTimes = useCustomDailySessionTimes, DailySessionStartTime = dailySessionStartTime, DailySessionEndTime = dailySessionEndTime, SessionProfileStyle = sessionProfileStyle, SessionOutlineSmoothness = sessionOutlineSmoothness, WeeklyNumberOfVolumeBars = weeklyNumberOfVolumeBars, WeeklyBarThickness = weeklyBarThickness, WeeklyVolumeType = weeklyVolumeType, WeeklyBarOpacity = weeklyBarOpacity, WeeklyDisplayPoC = weeklyDisplayPoC, WeeklyPoCLineThickness = weeklyPoCLineThickness, WeeklyPoCLineOpacity = weeklyPoCLineOpacity, WeeklyDisplayValueArea = weeklyDisplayValueArea, WeeklyValueAreaPercentage = weeklyValueAreaPercentage, WeeklyDisplayValueAreaLines = weeklyDisplayValueAreaLines, WeeklyValueAreaLinesThickness = weeklyValueAreaLinesThickness, WeeklyValueAreaLinesOpacity = weeklyValueAreaLinesOpacity, WeeklyExtendPoCLine = weeklyExtendPoCLine, WeeklyExtendValueAreaLines = weeklyExtendValueAreaLines, SessionNumberOfVolumeBars = sessionNumberOfVolumeBars, SessionBarThickness = sessionBarThickness, SessionVolumeType = sessionVolumeType, SessionBarOpacity = sessionBarOpacity, SessionDisplayPoC = sessionDisplayPoC, SessionPoCLineThickness = sessionPoCLineThickness, SessionPoCLineOpacity = sessionPoCLineOpacity, SessionDisplayValueArea = sessionDisplayValueArea, SessionValueAreaPercentage = sessionValueAreaPercentage, SessionDisplayValueAreaLines = sessionDisplayValueAreaLines, SessionValueAreaLinesThickness = sessionValueAreaLinesThickness, SessionValueAreaLinesOpacity = sessionValueAreaLinesOpacity, SessionExtendPoCLine = sessionExtendPoCLine, SessionExtendValueAreaLines = sessionExtendValueAreaLines, EnableGradientFill = enableGradientFill, GradientIntensity = gradientIntensity, DisplayLVN = displayLVN, LVNNumberOfRows = lVNNumberOfRows, LVNDetectionPercent = lVNDetectionPercent, ShowAdjacentLVNNodes = showAdjacentLVNNodes, LVNFillOpacity = lVNFillOpacity, LVNBorderOpacity = lVNBorderOpacity, EnableDomdicator = enableDomdicator, DomdicatorWidth = domdicatorWidth, DomdicatorGap = domdicatorGap, DomMaxRightExtension = domMaxRightExtension, ShowDOMVolumeText = showDOMVolumeText, DomMaxTextSize = domMaxTextSize, DomMinTextSize = domMinTextSize, DomHistoricalOpacity = domHistoricalOpacity, ShowHistoricalOrders = showHistoricalOrders, LiveOrderTickThreshold = liveOrderTickThreshold, DomLiveOpacity = domLiveOpacity, MinimumOrdersToStart = minimumOrdersToStart }, input, ref cacheRedTailVolumeProfile_V2);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RedTailVolumeProfile_V2 RedTailVolumeProfile_V2(ProfileModeEnum profileMode, ProfileAlignment alignment, int weeksLookback, int sessionsLookback, int monthsLookback, CompositeDateRangeType compositeRangeType, int compositeDaysBack, int compositeWeeksBack, int compositeMonthsBack, DateTime compositeCustomStartDate, DateTime compositeCustomEndDate, bool useCustomSessionTimes, int sessionStartTime, int sessionEndTime, int numberOfVolumeBars, int barThickness, int profileWidth, VolumeTypeEnum volumeType, int barOpacity, bool displayPoC, int poCLineThickness, DashStyleHelper poCLineStyle, int poCLineOpacity, bool extendPoCLine, bool displayValueArea, int valueAreaPercentage, bool displayValueAreaLines, int valueAreaLinesThickness, DashStyleHelper valueAreaLineStyle, int valueAreaLinesOpacity, bool extendValueAreaLines, bool displayPreviousDayPOC, DashStyleHelper pdPOCLineStyle, int pdPOCThickness, int pdPOCOpacity, bool displayPreviousDayVAH, DashStyleHelper pdVAHLineStyle, int pdVAHThickness, int pdVAHOpacity, bool displayPreviousDayVAL, DashStyleHelper pdVALLineStyle, int pdVALThickness, int pdVALOpacity, bool displayPreviousDayHigh, DashStyleHelper pdHighLineStyle, int pdHighThickness, int pdHighOpacity, bool displayPreviousDayLow, DashStyleHelper pdLowLineStyle, int pdLowThickness, int pdLowOpacity, bool displayOvernightPOC, DashStyleHelper overnightPOCLineStyle, int overnightPOCThickness, int overnightPOCOpacity, bool displayOvernightVAH, DashStyleHelper overnightVAHLineStyle, int overnightVAHThickness, int overnightVAHOpacity, bool displayOvernightVAL, DashStyleHelper overnightVALLineStyle, int overnightVALThickness, int overnightVALOpacity, bool displayOvernightHigh, DashStyleHelper overnightHighLineStyle, int overnightHighThickness, int overnightHighOpacity, bool displayOvernightLow, DashStyleHelper overnightLowLineStyle, int overnightLowThickness, int overnightLowOpacity, int overnightStartTime, int overnightEndTime, DashStyleHelper nakedPOCLineStyle, int nakedPOCThickness, int nakedPOCOpacity, DashStyleHelper nakedVAHLineStyle, int nakedVAHThickness, int nakedVAHOpacity, DashStyleHelper nakedVALLineStyle, int nakedVALThickness, int nakedVALOpacity, DashStyleHelper weeklyNakedPOCLineStyle, int weeklyNakedPOCThickness, int weeklyNakedPOCOpacity, DashStyleHelper weeklyNakedVAHLineStyle, int weeklyNakedVAHThickness, int weeklyNakedVAHOpacity, DashStyleHelper weeklyNakedVALLineStyle, int weeklyNakedVALThickness, int weeklyNakedVALOpacity, bool displayNakedLevels, int maxNakedLevelsToDisplay, bool displayWeeklyNakedLevels, int maxWeeklyNakedLevelsToDisplay, bool keepFilledLevelsAfterSession, int removeAfterTouchCount, bool keepFilledWeeklyLevelsAfterWeek, int removeWeeklyAfterTouchCount, bool showTouchCountInLabels, bool britishDateFormat, int previousDayLineWidth, bool showPriceValuesInLabels, bool enableDualProfileMode, int weeklyProfileWidth, int sessionProfileWidth, int profileGap, bool useCustomDailySessionTimes, int dailySessionStartTime, int dailySessionEndTime, SessionProfileStyleEnum sessionProfileStyle, int sessionOutlineSmoothness, int weeklyNumberOfVolumeBars, int weeklyBarThickness, VolumeTypeEnum weeklyVolumeType, int weeklyBarOpacity, bool weeklyDisplayPoC, int weeklyPoCLineThickness, int weeklyPoCLineOpacity, bool weeklyDisplayValueArea, int weeklyValueAreaPercentage, bool weeklyDisplayValueAreaLines, int weeklyValueAreaLinesThickness, int weeklyValueAreaLinesOpacity, bool weeklyExtendPoCLine, bool weeklyExtendValueAreaLines, int sessionNumberOfVolumeBars, int sessionBarThickness, VolumeTypeEnum sessionVolumeType, int sessionBarOpacity, bool sessionDisplayPoC, int sessionPoCLineThickness, int sessionPoCLineOpacity, bool sessionDisplayValueArea, int sessionValueAreaPercentage, bool sessionDisplayValueAreaLines, int sessionValueAreaLinesThickness, int sessionValueAreaLinesOpacity, bool sessionExtendPoCLine, bool sessionExtendValueAreaLines, bool enableGradientFill, int gradientIntensity, bool displayLVN, int lVNNumberOfRows, int lVNDetectionPercent, bool showAdjacentLVNNodes, int lVNFillOpacity, int lVNBorderOpacity, bool enableDomdicator, int domdicatorWidth, int domdicatorGap, int domMaxRightExtension, bool showDOMVolumeText, float domMaxTextSize, float domMinTextSize, float domHistoricalOpacity, bool showHistoricalOrders, int liveOrderTickThreshold, float domLiveOpacity, int minimumOrdersToStart)
		{
			return indicator.RedTailVolumeProfile_V2(Input, profileMode, alignment, weeksLookback, sessionsLookback, monthsLookback, compositeRangeType, compositeDaysBack, compositeWeeksBack, compositeMonthsBack, compositeCustomStartDate, compositeCustomEndDate, useCustomSessionTimes, sessionStartTime, sessionEndTime, numberOfVolumeBars, barThickness, profileWidth, volumeType, barOpacity, displayPoC, poCLineThickness, poCLineStyle, poCLineOpacity, extendPoCLine, displayValueArea, valueAreaPercentage, displayValueAreaLines, valueAreaLinesThickness, valueAreaLineStyle, valueAreaLinesOpacity, extendValueAreaLines, displayPreviousDayPOC, pdPOCLineStyle, pdPOCThickness, pdPOCOpacity, displayPreviousDayVAH, pdVAHLineStyle, pdVAHThickness, pdVAHOpacity, displayPreviousDayVAL, pdVALLineStyle, pdVALThickness, pdVALOpacity, displayPreviousDayHigh, pdHighLineStyle, pdHighThickness, pdHighOpacity, displayPreviousDayLow, pdLowLineStyle, pdLowThickness, pdLowOpacity, displayOvernightPOC, overnightPOCLineStyle, overnightPOCThickness, overnightPOCOpacity, displayOvernightVAH, overnightVAHLineStyle, overnightVAHThickness, overnightVAHOpacity, displayOvernightVAL, overnightVALLineStyle, overnightVALThickness, overnightVALOpacity, displayOvernightHigh, overnightHighLineStyle, overnightHighThickness, overnightHighOpacity, displayOvernightLow, overnightLowLineStyle, overnightLowThickness, overnightLowOpacity, overnightStartTime, overnightEndTime, nakedPOCLineStyle, nakedPOCThickness, nakedPOCOpacity, nakedVAHLineStyle, nakedVAHThickness, nakedVAHOpacity, nakedVALLineStyle, nakedVALThickness, nakedVALOpacity, weeklyNakedPOCLineStyle, weeklyNakedPOCThickness, weeklyNakedPOCOpacity, weeklyNakedVAHLineStyle, weeklyNakedVAHThickness, weeklyNakedVAHOpacity, weeklyNakedVALLineStyle, weeklyNakedVALThickness, weeklyNakedVALOpacity, displayNakedLevels, maxNakedLevelsToDisplay, displayWeeklyNakedLevels, maxWeeklyNakedLevelsToDisplay, keepFilledLevelsAfterSession, removeAfterTouchCount, keepFilledWeeklyLevelsAfterWeek, removeWeeklyAfterTouchCount, showTouchCountInLabels, britishDateFormat, previousDayLineWidth, showPriceValuesInLabels, enableDualProfileMode, weeklyProfileWidth, sessionProfileWidth, profileGap, useCustomDailySessionTimes, dailySessionStartTime, dailySessionEndTime, sessionProfileStyle, sessionOutlineSmoothness, weeklyNumberOfVolumeBars, weeklyBarThickness, weeklyVolumeType, weeklyBarOpacity, weeklyDisplayPoC, weeklyPoCLineThickness, weeklyPoCLineOpacity, weeklyDisplayValueArea, weeklyValueAreaPercentage, weeklyDisplayValueAreaLines, weeklyValueAreaLinesThickness, weeklyValueAreaLinesOpacity, weeklyExtendPoCLine, weeklyExtendValueAreaLines, sessionNumberOfVolumeBars, sessionBarThickness, sessionVolumeType, sessionBarOpacity, sessionDisplayPoC, sessionPoCLineThickness, sessionPoCLineOpacity, sessionDisplayValueArea, sessionValueAreaPercentage, sessionDisplayValueAreaLines, sessionValueAreaLinesThickness, sessionValueAreaLinesOpacity, sessionExtendPoCLine, sessionExtendValueAreaLines, enableGradientFill, gradientIntensity, displayLVN, lVNNumberOfRows, lVNDetectionPercent, showAdjacentLVNNodes, lVNFillOpacity, lVNBorderOpacity, enableDomdicator, domdicatorWidth, domdicatorGap, domMaxRightExtension, showDOMVolumeText, domMaxTextSize, domMinTextSize, domHistoricalOpacity, showHistoricalOrders, liveOrderTickThreshold, domLiveOpacity, minimumOrdersToStart);
		}

		public Indicators.RedTailVolumeProfile_V2 RedTailVolumeProfile_V2(ISeries<double> input , ProfileModeEnum profileMode, ProfileAlignment alignment, int weeksLookback, int sessionsLookback, int monthsLookback, CompositeDateRangeType compositeRangeType, int compositeDaysBack, int compositeWeeksBack, int compositeMonthsBack, DateTime compositeCustomStartDate, DateTime compositeCustomEndDate, bool useCustomSessionTimes, int sessionStartTime, int sessionEndTime, int numberOfVolumeBars, int barThickness, int profileWidth, VolumeTypeEnum volumeType, int barOpacity, bool displayPoC, int poCLineThickness, DashStyleHelper poCLineStyle, int poCLineOpacity, bool extendPoCLine, bool displayValueArea, int valueAreaPercentage, bool displayValueAreaLines, int valueAreaLinesThickness, DashStyleHelper valueAreaLineStyle, int valueAreaLinesOpacity, bool extendValueAreaLines, bool displayPreviousDayPOC, DashStyleHelper pdPOCLineStyle, int pdPOCThickness, int pdPOCOpacity, bool displayPreviousDayVAH, DashStyleHelper pdVAHLineStyle, int pdVAHThickness, int pdVAHOpacity, bool displayPreviousDayVAL, DashStyleHelper pdVALLineStyle, int pdVALThickness, int pdVALOpacity, bool displayPreviousDayHigh, DashStyleHelper pdHighLineStyle, int pdHighThickness, int pdHighOpacity, bool displayPreviousDayLow, DashStyleHelper pdLowLineStyle, int pdLowThickness, int pdLowOpacity, bool displayOvernightPOC, DashStyleHelper overnightPOCLineStyle, int overnightPOCThickness, int overnightPOCOpacity, bool displayOvernightVAH, DashStyleHelper overnightVAHLineStyle, int overnightVAHThickness, int overnightVAHOpacity, bool displayOvernightVAL, DashStyleHelper overnightVALLineStyle, int overnightVALThickness, int overnightVALOpacity, bool displayOvernightHigh, DashStyleHelper overnightHighLineStyle, int overnightHighThickness, int overnightHighOpacity, bool displayOvernightLow, DashStyleHelper overnightLowLineStyle, int overnightLowThickness, int overnightLowOpacity, int overnightStartTime, int overnightEndTime, DashStyleHelper nakedPOCLineStyle, int nakedPOCThickness, int nakedPOCOpacity, DashStyleHelper nakedVAHLineStyle, int nakedVAHThickness, int nakedVAHOpacity, DashStyleHelper nakedVALLineStyle, int nakedVALThickness, int nakedVALOpacity, DashStyleHelper weeklyNakedPOCLineStyle, int weeklyNakedPOCThickness, int weeklyNakedPOCOpacity, DashStyleHelper weeklyNakedVAHLineStyle, int weeklyNakedVAHThickness, int weeklyNakedVAHOpacity, DashStyleHelper weeklyNakedVALLineStyle, int weeklyNakedVALThickness, int weeklyNakedVALOpacity, bool displayNakedLevels, int maxNakedLevelsToDisplay, bool displayWeeklyNakedLevels, int maxWeeklyNakedLevelsToDisplay, bool keepFilledLevelsAfterSession, int removeAfterTouchCount, bool keepFilledWeeklyLevelsAfterWeek, int removeWeeklyAfterTouchCount, bool showTouchCountInLabels, bool britishDateFormat, int previousDayLineWidth, bool showPriceValuesInLabels, bool enableDualProfileMode, int weeklyProfileWidth, int sessionProfileWidth, int profileGap, bool useCustomDailySessionTimes, int dailySessionStartTime, int dailySessionEndTime, SessionProfileStyleEnum sessionProfileStyle, int sessionOutlineSmoothness, int weeklyNumberOfVolumeBars, int weeklyBarThickness, VolumeTypeEnum weeklyVolumeType, int weeklyBarOpacity, bool weeklyDisplayPoC, int weeklyPoCLineThickness, int weeklyPoCLineOpacity, bool weeklyDisplayValueArea, int weeklyValueAreaPercentage, bool weeklyDisplayValueAreaLines, int weeklyValueAreaLinesThickness, int weeklyValueAreaLinesOpacity, bool weeklyExtendPoCLine, bool weeklyExtendValueAreaLines, int sessionNumberOfVolumeBars, int sessionBarThickness, VolumeTypeEnum sessionVolumeType, int sessionBarOpacity, bool sessionDisplayPoC, int sessionPoCLineThickness, int sessionPoCLineOpacity, bool sessionDisplayValueArea, int sessionValueAreaPercentage, bool sessionDisplayValueAreaLines, int sessionValueAreaLinesThickness, int sessionValueAreaLinesOpacity, bool sessionExtendPoCLine, bool sessionExtendValueAreaLines, bool enableGradientFill, int gradientIntensity, bool displayLVN, int lVNNumberOfRows, int lVNDetectionPercent, bool showAdjacentLVNNodes, int lVNFillOpacity, int lVNBorderOpacity, bool enableDomdicator, int domdicatorWidth, int domdicatorGap, int domMaxRightExtension, bool showDOMVolumeText, float domMaxTextSize, float domMinTextSize, float domHistoricalOpacity, bool showHistoricalOrders, int liveOrderTickThreshold, float domLiveOpacity, int minimumOrdersToStart)
		{
			return indicator.RedTailVolumeProfile_V2(input, profileMode, alignment, weeksLookback, sessionsLookback, monthsLookback, compositeRangeType, compositeDaysBack, compositeWeeksBack, compositeMonthsBack, compositeCustomStartDate, compositeCustomEndDate, useCustomSessionTimes, sessionStartTime, sessionEndTime, numberOfVolumeBars, barThickness, profileWidth, volumeType, barOpacity, displayPoC, poCLineThickness, poCLineStyle, poCLineOpacity, extendPoCLine, displayValueArea, valueAreaPercentage, displayValueAreaLines, valueAreaLinesThickness, valueAreaLineStyle, valueAreaLinesOpacity, extendValueAreaLines, displayPreviousDayPOC, pdPOCLineStyle, pdPOCThickness, pdPOCOpacity, displayPreviousDayVAH, pdVAHLineStyle, pdVAHThickness, pdVAHOpacity, displayPreviousDayVAL, pdVALLineStyle, pdVALThickness, pdVALOpacity, displayPreviousDayHigh, pdHighLineStyle, pdHighThickness, pdHighOpacity, displayPreviousDayLow, pdLowLineStyle, pdLowThickness, pdLowOpacity, displayOvernightPOC, overnightPOCLineStyle, overnightPOCThickness, overnightPOCOpacity, displayOvernightVAH, overnightVAHLineStyle, overnightVAHThickness, overnightVAHOpacity, displayOvernightVAL, overnightVALLineStyle, overnightVALThickness, overnightVALOpacity, displayOvernightHigh, overnightHighLineStyle, overnightHighThickness, overnightHighOpacity, displayOvernightLow, overnightLowLineStyle, overnightLowThickness, overnightLowOpacity, overnightStartTime, overnightEndTime, nakedPOCLineStyle, nakedPOCThickness, nakedPOCOpacity, nakedVAHLineStyle, nakedVAHThickness, nakedVAHOpacity, nakedVALLineStyle, nakedVALThickness, nakedVALOpacity, weeklyNakedPOCLineStyle, weeklyNakedPOCThickness, weeklyNakedPOCOpacity, weeklyNakedVAHLineStyle, weeklyNakedVAHThickness, weeklyNakedVAHOpacity, weeklyNakedVALLineStyle, weeklyNakedVALThickness, weeklyNakedVALOpacity, displayNakedLevels, maxNakedLevelsToDisplay, displayWeeklyNakedLevels, maxWeeklyNakedLevelsToDisplay, keepFilledLevelsAfterSession, removeAfterTouchCount, keepFilledWeeklyLevelsAfterWeek, removeWeeklyAfterTouchCount, showTouchCountInLabels, britishDateFormat, previousDayLineWidth, showPriceValuesInLabels, enableDualProfileMode, weeklyProfileWidth, sessionProfileWidth, profileGap, useCustomDailySessionTimes, dailySessionStartTime, dailySessionEndTime, sessionProfileStyle, sessionOutlineSmoothness, weeklyNumberOfVolumeBars, weeklyBarThickness, weeklyVolumeType, weeklyBarOpacity, weeklyDisplayPoC, weeklyPoCLineThickness, weeklyPoCLineOpacity, weeklyDisplayValueArea, weeklyValueAreaPercentage, weeklyDisplayValueAreaLines, weeklyValueAreaLinesThickness, weeklyValueAreaLinesOpacity, weeklyExtendPoCLine, weeklyExtendValueAreaLines, sessionNumberOfVolumeBars, sessionBarThickness, sessionVolumeType, sessionBarOpacity, sessionDisplayPoC, sessionPoCLineThickness, sessionPoCLineOpacity, sessionDisplayValueArea, sessionValueAreaPercentage, sessionDisplayValueAreaLines, sessionValueAreaLinesThickness, sessionValueAreaLinesOpacity, sessionExtendPoCLine, sessionExtendValueAreaLines, enableGradientFill, gradientIntensity, displayLVN, lVNNumberOfRows, lVNDetectionPercent, showAdjacentLVNNodes, lVNFillOpacity, lVNBorderOpacity, enableDomdicator, domdicatorWidth, domdicatorGap, domMaxRightExtension, showDOMVolumeText, domMaxTextSize, domMinTextSize, domHistoricalOpacity, showHistoricalOrders, liveOrderTickThreshold, domLiveOpacity, minimumOrdersToStart);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RedTailVolumeProfile_V2 RedTailVolumeProfile_V2(ProfileModeEnum profileMode, ProfileAlignment alignment, int weeksLookback, int sessionsLookback, int monthsLookback, CompositeDateRangeType compositeRangeType, int compositeDaysBack, int compositeWeeksBack, int compositeMonthsBack, DateTime compositeCustomStartDate, DateTime compositeCustomEndDate, bool useCustomSessionTimes, int sessionStartTime, int sessionEndTime, int numberOfVolumeBars, int barThickness, int profileWidth, VolumeTypeEnum volumeType, int barOpacity, bool displayPoC, int poCLineThickness, DashStyleHelper poCLineStyle, int poCLineOpacity, bool extendPoCLine, bool displayValueArea, int valueAreaPercentage, bool displayValueAreaLines, int valueAreaLinesThickness, DashStyleHelper valueAreaLineStyle, int valueAreaLinesOpacity, bool extendValueAreaLines, bool displayPreviousDayPOC, DashStyleHelper pdPOCLineStyle, int pdPOCThickness, int pdPOCOpacity, bool displayPreviousDayVAH, DashStyleHelper pdVAHLineStyle, int pdVAHThickness, int pdVAHOpacity, bool displayPreviousDayVAL, DashStyleHelper pdVALLineStyle, int pdVALThickness, int pdVALOpacity, bool displayPreviousDayHigh, DashStyleHelper pdHighLineStyle, int pdHighThickness, int pdHighOpacity, bool displayPreviousDayLow, DashStyleHelper pdLowLineStyle, int pdLowThickness, int pdLowOpacity, bool displayOvernightPOC, DashStyleHelper overnightPOCLineStyle, int overnightPOCThickness, int overnightPOCOpacity, bool displayOvernightVAH, DashStyleHelper overnightVAHLineStyle, int overnightVAHThickness, int overnightVAHOpacity, bool displayOvernightVAL, DashStyleHelper overnightVALLineStyle, int overnightVALThickness, int overnightVALOpacity, bool displayOvernightHigh, DashStyleHelper overnightHighLineStyle, int overnightHighThickness, int overnightHighOpacity, bool displayOvernightLow, DashStyleHelper overnightLowLineStyle, int overnightLowThickness, int overnightLowOpacity, int overnightStartTime, int overnightEndTime, DashStyleHelper nakedPOCLineStyle, int nakedPOCThickness, int nakedPOCOpacity, DashStyleHelper nakedVAHLineStyle, int nakedVAHThickness, int nakedVAHOpacity, DashStyleHelper nakedVALLineStyle, int nakedVALThickness, int nakedVALOpacity, DashStyleHelper weeklyNakedPOCLineStyle, int weeklyNakedPOCThickness, int weeklyNakedPOCOpacity, DashStyleHelper weeklyNakedVAHLineStyle, int weeklyNakedVAHThickness, int weeklyNakedVAHOpacity, DashStyleHelper weeklyNakedVALLineStyle, int weeklyNakedVALThickness, int weeklyNakedVALOpacity, bool displayNakedLevels, int maxNakedLevelsToDisplay, bool displayWeeklyNakedLevels, int maxWeeklyNakedLevelsToDisplay, bool keepFilledLevelsAfterSession, int removeAfterTouchCount, bool keepFilledWeeklyLevelsAfterWeek, int removeWeeklyAfterTouchCount, bool showTouchCountInLabels, bool britishDateFormat, int previousDayLineWidth, bool showPriceValuesInLabels, bool enableDualProfileMode, int weeklyProfileWidth, int sessionProfileWidth, int profileGap, bool useCustomDailySessionTimes, int dailySessionStartTime, int dailySessionEndTime, SessionProfileStyleEnum sessionProfileStyle, int sessionOutlineSmoothness, int weeklyNumberOfVolumeBars, int weeklyBarThickness, VolumeTypeEnum weeklyVolumeType, int weeklyBarOpacity, bool weeklyDisplayPoC, int weeklyPoCLineThickness, int weeklyPoCLineOpacity, bool weeklyDisplayValueArea, int weeklyValueAreaPercentage, bool weeklyDisplayValueAreaLines, int weeklyValueAreaLinesThickness, int weeklyValueAreaLinesOpacity, bool weeklyExtendPoCLine, bool weeklyExtendValueAreaLines, int sessionNumberOfVolumeBars, int sessionBarThickness, VolumeTypeEnum sessionVolumeType, int sessionBarOpacity, bool sessionDisplayPoC, int sessionPoCLineThickness, int sessionPoCLineOpacity, bool sessionDisplayValueArea, int sessionValueAreaPercentage, bool sessionDisplayValueAreaLines, int sessionValueAreaLinesThickness, int sessionValueAreaLinesOpacity, bool sessionExtendPoCLine, bool sessionExtendValueAreaLines, bool enableGradientFill, int gradientIntensity, bool displayLVN, int lVNNumberOfRows, int lVNDetectionPercent, bool showAdjacentLVNNodes, int lVNFillOpacity, int lVNBorderOpacity, bool enableDomdicator, int domdicatorWidth, int domdicatorGap, int domMaxRightExtension, bool showDOMVolumeText, float domMaxTextSize, float domMinTextSize, float domHistoricalOpacity, bool showHistoricalOrders, int liveOrderTickThreshold, float domLiveOpacity, int minimumOrdersToStart)
		{
			return indicator.RedTailVolumeProfile_V2(Input, profileMode, alignment, weeksLookback, sessionsLookback, monthsLookback, compositeRangeType, compositeDaysBack, compositeWeeksBack, compositeMonthsBack, compositeCustomStartDate, compositeCustomEndDate, useCustomSessionTimes, sessionStartTime, sessionEndTime, numberOfVolumeBars, barThickness, profileWidth, volumeType, barOpacity, displayPoC, poCLineThickness, poCLineStyle, poCLineOpacity, extendPoCLine, displayValueArea, valueAreaPercentage, displayValueAreaLines, valueAreaLinesThickness, valueAreaLineStyle, valueAreaLinesOpacity, extendValueAreaLines, displayPreviousDayPOC, pdPOCLineStyle, pdPOCThickness, pdPOCOpacity, displayPreviousDayVAH, pdVAHLineStyle, pdVAHThickness, pdVAHOpacity, displayPreviousDayVAL, pdVALLineStyle, pdVALThickness, pdVALOpacity, displayPreviousDayHigh, pdHighLineStyle, pdHighThickness, pdHighOpacity, displayPreviousDayLow, pdLowLineStyle, pdLowThickness, pdLowOpacity, displayOvernightPOC, overnightPOCLineStyle, overnightPOCThickness, overnightPOCOpacity, displayOvernightVAH, overnightVAHLineStyle, overnightVAHThickness, overnightVAHOpacity, displayOvernightVAL, overnightVALLineStyle, overnightVALThickness, overnightVALOpacity, displayOvernightHigh, overnightHighLineStyle, overnightHighThickness, overnightHighOpacity, displayOvernightLow, overnightLowLineStyle, overnightLowThickness, overnightLowOpacity, overnightStartTime, overnightEndTime, nakedPOCLineStyle, nakedPOCThickness, nakedPOCOpacity, nakedVAHLineStyle, nakedVAHThickness, nakedVAHOpacity, nakedVALLineStyle, nakedVALThickness, nakedVALOpacity, weeklyNakedPOCLineStyle, weeklyNakedPOCThickness, weeklyNakedPOCOpacity, weeklyNakedVAHLineStyle, weeklyNakedVAHThickness, weeklyNakedVAHOpacity, weeklyNakedVALLineStyle, weeklyNakedVALThickness, weeklyNakedVALOpacity, displayNakedLevels, maxNakedLevelsToDisplay, displayWeeklyNakedLevels, maxWeeklyNakedLevelsToDisplay, keepFilledLevelsAfterSession, removeAfterTouchCount, keepFilledWeeklyLevelsAfterWeek, removeWeeklyAfterTouchCount, showTouchCountInLabels, britishDateFormat, previousDayLineWidth, showPriceValuesInLabels, enableDualProfileMode, weeklyProfileWidth, sessionProfileWidth, profileGap, useCustomDailySessionTimes, dailySessionStartTime, dailySessionEndTime, sessionProfileStyle, sessionOutlineSmoothness, weeklyNumberOfVolumeBars, weeklyBarThickness, weeklyVolumeType, weeklyBarOpacity, weeklyDisplayPoC, weeklyPoCLineThickness, weeklyPoCLineOpacity, weeklyDisplayValueArea, weeklyValueAreaPercentage, weeklyDisplayValueAreaLines, weeklyValueAreaLinesThickness, weeklyValueAreaLinesOpacity, weeklyExtendPoCLine, weeklyExtendValueAreaLines, sessionNumberOfVolumeBars, sessionBarThickness, sessionVolumeType, sessionBarOpacity, sessionDisplayPoC, sessionPoCLineThickness, sessionPoCLineOpacity, sessionDisplayValueArea, sessionValueAreaPercentage, sessionDisplayValueAreaLines, sessionValueAreaLinesThickness, sessionValueAreaLinesOpacity, sessionExtendPoCLine, sessionExtendValueAreaLines, enableGradientFill, gradientIntensity, displayLVN, lVNNumberOfRows, lVNDetectionPercent, showAdjacentLVNNodes, lVNFillOpacity, lVNBorderOpacity, enableDomdicator, domdicatorWidth, domdicatorGap, domMaxRightExtension, showDOMVolumeText, domMaxTextSize, domMinTextSize, domHistoricalOpacity, showHistoricalOrders, liveOrderTickThreshold, domLiveOpacity, minimumOrdersToStart);
		}

		public Indicators.RedTailVolumeProfile_V2 RedTailVolumeProfile_V2(ISeries<double> input , ProfileModeEnum profileMode, ProfileAlignment alignment, int weeksLookback, int sessionsLookback, int monthsLookback, CompositeDateRangeType compositeRangeType, int compositeDaysBack, int compositeWeeksBack, int compositeMonthsBack, DateTime compositeCustomStartDate, DateTime compositeCustomEndDate, bool useCustomSessionTimes, int sessionStartTime, int sessionEndTime, int numberOfVolumeBars, int barThickness, int profileWidth, VolumeTypeEnum volumeType, int barOpacity, bool displayPoC, int poCLineThickness, DashStyleHelper poCLineStyle, int poCLineOpacity, bool extendPoCLine, bool displayValueArea, int valueAreaPercentage, bool displayValueAreaLines, int valueAreaLinesThickness, DashStyleHelper valueAreaLineStyle, int valueAreaLinesOpacity, bool extendValueAreaLines, bool displayPreviousDayPOC, DashStyleHelper pdPOCLineStyle, int pdPOCThickness, int pdPOCOpacity, bool displayPreviousDayVAH, DashStyleHelper pdVAHLineStyle, int pdVAHThickness, int pdVAHOpacity, bool displayPreviousDayVAL, DashStyleHelper pdVALLineStyle, int pdVALThickness, int pdVALOpacity, bool displayPreviousDayHigh, DashStyleHelper pdHighLineStyle, int pdHighThickness, int pdHighOpacity, bool displayPreviousDayLow, DashStyleHelper pdLowLineStyle, int pdLowThickness, int pdLowOpacity, bool displayOvernightPOC, DashStyleHelper overnightPOCLineStyle, int overnightPOCThickness, int overnightPOCOpacity, bool displayOvernightVAH, DashStyleHelper overnightVAHLineStyle, int overnightVAHThickness, int overnightVAHOpacity, bool displayOvernightVAL, DashStyleHelper overnightVALLineStyle, int overnightVALThickness, int overnightVALOpacity, bool displayOvernightHigh, DashStyleHelper overnightHighLineStyle, int overnightHighThickness, int overnightHighOpacity, bool displayOvernightLow, DashStyleHelper overnightLowLineStyle, int overnightLowThickness, int overnightLowOpacity, int overnightStartTime, int overnightEndTime, DashStyleHelper nakedPOCLineStyle, int nakedPOCThickness, int nakedPOCOpacity, DashStyleHelper nakedVAHLineStyle, int nakedVAHThickness, int nakedVAHOpacity, DashStyleHelper nakedVALLineStyle, int nakedVALThickness, int nakedVALOpacity, DashStyleHelper weeklyNakedPOCLineStyle, int weeklyNakedPOCThickness, int weeklyNakedPOCOpacity, DashStyleHelper weeklyNakedVAHLineStyle, int weeklyNakedVAHThickness, int weeklyNakedVAHOpacity, DashStyleHelper weeklyNakedVALLineStyle, int weeklyNakedVALThickness, int weeklyNakedVALOpacity, bool displayNakedLevels, int maxNakedLevelsToDisplay, bool displayWeeklyNakedLevels, int maxWeeklyNakedLevelsToDisplay, bool keepFilledLevelsAfterSession, int removeAfterTouchCount, bool keepFilledWeeklyLevelsAfterWeek, int removeWeeklyAfterTouchCount, bool showTouchCountInLabels, bool britishDateFormat, int previousDayLineWidth, bool showPriceValuesInLabels, bool enableDualProfileMode, int weeklyProfileWidth, int sessionProfileWidth, int profileGap, bool useCustomDailySessionTimes, int dailySessionStartTime, int dailySessionEndTime, SessionProfileStyleEnum sessionProfileStyle, int sessionOutlineSmoothness, int weeklyNumberOfVolumeBars, int weeklyBarThickness, VolumeTypeEnum weeklyVolumeType, int weeklyBarOpacity, bool weeklyDisplayPoC, int weeklyPoCLineThickness, int weeklyPoCLineOpacity, bool weeklyDisplayValueArea, int weeklyValueAreaPercentage, bool weeklyDisplayValueAreaLines, int weeklyValueAreaLinesThickness, int weeklyValueAreaLinesOpacity, bool weeklyExtendPoCLine, bool weeklyExtendValueAreaLines, int sessionNumberOfVolumeBars, int sessionBarThickness, VolumeTypeEnum sessionVolumeType, int sessionBarOpacity, bool sessionDisplayPoC, int sessionPoCLineThickness, int sessionPoCLineOpacity, bool sessionDisplayValueArea, int sessionValueAreaPercentage, bool sessionDisplayValueAreaLines, int sessionValueAreaLinesThickness, int sessionValueAreaLinesOpacity, bool sessionExtendPoCLine, bool sessionExtendValueAreaLines, bool enableGradientFill, int gradientIntensity, bool displayLVN, int lVNNumberOfRows, int lVNDetectionPercent, bool showAdjacentLVNNodes, int lVNFillOpacity, int lVNBorderOpacity, bool enableDomdicator, int domdicatorWidth, int domdicatorGap, int domMaxRightExtension, bool showDOMVolumeText, float domMaxTextSize, float domMinTextSize, float domHistoricalOpacity, bool showHistoricalOrders, int liveOrderTickThreshold, float domLiveOpacity, int minimumOrdersToStart)
		{
			return indicator.RedTailVolumeProfile_V2(input, profileMode, alignment, weeksLookback, sessionsLookback, monthsLookback, compositeRangeType, compositeDaysBack, compositeWeeksBack, compositeMonthsBack, compositeCustomStartDate, compositeCustomEndDate, useCustomSessionTimes, sessionStartTime, sessionEndTime, numberOfVolumeBars, barThickness, profileWidth, volumeType, barOpacity, displayPoC, poCLineThickness, poCLineStyle, poCLineOpacity, extendPoCLine, displayValueArea, valueAreaPercentage, displayValueAreaLines, valueAreaLinesThickness, valueAreaLineStyle, valueAreaLinesOpacity, extendValueAreaLines, displayPreviousDayPOC, pdPOCLineStyle, pdPOCThickness, pdPOCOpacity, displayPreviousDayVAH, pdVAHLineStyle, pdVAHThickness, pdVAHOpacity, displayPreviousDayVAL, pdVALLineStyle, pdVALThickness, pdVALOpacity, displayPreviousDayHigh, pdHighLineStyle, pdHighThickness, pdHighOpacity, displayPreviousDayLow, pdLowLineStyle, pdLowThickness, pdLowOpacity, displayOvernightPOC, overnightPOCLineStyle, overnightPOCThickness, overnightPOCOpacity, displayOvernightVAH, overnightVAHLineStyle, overnightVAHThickness, overnightVAHOpacity, displayOvernightVAL, overnightVALLineStyle, overnightVALThickness, overnightVALOpacity, displayOvernightHigh, overnightHighLineStyle, overnightHighThickness, overnightHighOpacity, displayOvernightLow, overnightLowLineStyle, overnightLowThickness, overnightLowOpacity, overnightStartTime, overnightEndTime, nakedPOCLineStyle, nakedPOCThickness, nakedPOCOpacity, nakedVAHLineStyle, nakedVAHThickness, nakedVAHOpacity, nakedVALLineStyle, nakedVALThickness, nakedVALOpacity, weeklyNakedPOCLineStyle, weeklyNakedPOCThickness, weeklyNakedPOCOpacity, weeklyNakedVAHLineStyle, weeklyNakedVAHThickness, weeklyNakedVAHOpacity, weeklyNakedVALLineStyle, weeklyNakedVALThickness, weeklyNakedVALOpacity, displayNakedLevels, maxNakedLevelsToDisplay, displayWeeklyNakedLevels, maxWeeklyNakedLevelsToDisplay, keepFilledLevelsAfterSession, removeAfterTouchCount, keepFilledWeeklyLevelsAfterWeek, removeWeeklyAfterTouchCount, showTouchCountInLabels, britishDateFormat, previousDayLineWidth, showPriceValuesInLabels, enableDualProfileMode, weeklyProfileWidth, sessionProfileWidth, profileGap, useCustomDailySessionTimes, dailySessionStartTime, dailySessionEndTime, sessionProfileStyle, sessionOutlineSmoothness, weeklyNumberOfVolumeBars, weeklyBarThickness, weeklyVolumeType, weeklyBarOpacity, weeklyDisplayPoC, weeklyPoCLineThickness, weeklyPoCLineOpacity, weeklyDisplayValueArea, weeklyValueAreaPercentage, weeklyDisplayValueAreaLines, weeklyValueAreaLinesThickness, weeklyValueAreaLinesOpacity, weeklyExtendPoCLine, weeklyExtendValueAreaLines, sessionNumberOfVolumeBars, sessionBarThickness, sessionVolumeType, sessionBarOpacity, sessionDisplayPoC, sessionPoCLineThickness, sessionPoCLineOpacity, sessionDisplayValueArea, sessionValueAreaPercentage, sessionDisplayValueAreaLines, sessionValueAreaLinesThickness, sessionValueAreaLinesOpacity, sessionExtendPoCLine, sessionExtendValueAreaLines, enableGradientFill, gradientIntensity, displayLVN, lVNNumberOfRows, lVNDetectionPercent, showAdjacentLVNNodes, lVNFillOpacity, lVNBorderOpacity, enableDomdicator, domdicatorWidth, domdicatorGap, domMaxRightExtension, showDOMVolumeText, domMaxTextSize, domMinTextSize, domHistoricalOpacity, showHistoricalOrders, liveOrderTickThreshold, domLiveOpacity, minimumOrdersToStart);
		}
	}
}

#endregion

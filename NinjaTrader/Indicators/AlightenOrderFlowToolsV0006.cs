using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Vendor;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;


    

namespace NinjaTrader.NinjaScript.Indicators
{
	
	public enum OrderFlowToolsLargeTradeFindByV0006
    {
        BidAsk,
        Price,
        Block
    }
    public enum OrderFlowToolsLargeTradeSizeByV0006
    {
        VisibleArea,
        Session
    }
    public class AlightenOrderFlowToolsV0006 : Indicator
    {
		[NinjaScriptProperty]
		[Range(0, 200000)]
		[Display(Name = "BarsToProcess", GroupName = "Performance", Order = 0)]
		public int BarsToProcess { get; set; } = 800;
		

		#region Large Trades
        // -------------------- Large Trades (same surface shape) --------------------
        [Display(Name = "BaseLargeVolumeOn", GroupName = "Large Trades", Order = 0)]
        [NinjaScriptProperty]
        public OrderFlowToolsLargeTradeFindByV0006 LargeTradeFindBy { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Enable Large Trade Bubbles", GroupName = "Large Trades", Order = 1)]
		public bool EnableLargeTradeBubbles { get; set; } = true;

        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "MinimumVolumeForMarker", GroupName = "Large Trades", Order = 2)]
        public int MinimumVolumeForMarker { get; set; }

        [Range(0, int.MaxValue)]
        [NinjaScriptProperty]
        [Display(Name = "MaximumMarkerSize", GroupName = "Large Trades", Order = 3)]
        public int MaximumMarkerSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BaseMarkerSizeOn", GroupName = "Large Trades", Order = 4)]
        public OrderFlowToolsLargeTradeSizeByV0006 LargeTradeSizeBy { get; set; }

        [Display(Name = "HoverValues", GroupName = "Large Trades", Order = 5)]
        [NinjaScriptProperty]
        public bool HoverValues { get; set; }

        [Display(Name = "AskBrush", GroupName = "Large Trades", Order = 6)]
        [XmlIgnore]
        public System.Windows.Media.Brush AskBrush { get; set; }

        [Browsable(false)]
        public string AskBrushSeralizer
        {
            get { return Serialize.BrushToString(AskBrush); }
            set { AskBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "BidBrush", GroupName = "Large Trades", Order = 7)]
        public System.Windows.Media.Brush BidBrush { get; set; }

        [Browsable(false)]
        public string BidBrushSeralizer
        {
            get { return Serialize.BrushToString(BidBrush); }
            set { BidBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "OutlineBrush", GroupName = "Large Trades", Order = 8)]
        public System.Windows.Media.Brush OutlineBrush { get; set; }

        [Browsable(false)]
        public string OutlineBrushSeralizer
        {
            get { return Serialize.BrushToString(OutlineBrush); }
            set { OutlineBrush = Serialize.StringToBrush(value); }
        }

        [Display(Name = "FillOpacity", GroupName = "Large Trades", Order = 9)]
        [Range(0, 100)]
        public int FillOpacity { get; set; }

		// -------------------- Big Trade Zone --------------------
		[NinjaScriptProperty]
		[Display(Name = "Enable Big Trade Zones", GroupName = "Large Trades", Order = 10)]
		public bool EnableBigTradeZones { get; set; } = true;

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Big Trade Zone Threshold (total vol)", GroupName = "Large Trades", Order = 11)]
		public int BigTradeZoneThreshold { get; set; } = 50;

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Big Trade Zone Opacity", GroupName = "Large Trades", Order = 12)]
		public int BigTradeZoneOpacity { get; set; } = 20;

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Big Trade Zone Height (ticks)", GroupName = "Large Trades", Order = 13)]
		public int BigTradeZoneHeightTicks { get; set; } = 20;

		#endregion
		
		// -------------------- Shared layout --------------------
		[NinjaScriptProperty]
		[Range(6, 96)]
		[Display(Name = "Signal Font Size", GroupName = "Signals - Layout", Order = 0)]
		public int SignalFontSize { get; set; } = 18;
		
		[NinjaScriptProperty]
		[Range(0, 200)]
		[Display(Name = "Signal Offset (ticks)", GroupName = "Signals - Layout", Order = 10)]
		public int SignalOffsetTicks { get; set; } = 10;
		
		[NinjaScriptProperty]
		[Range(0, 200)]
		[Display(Name = "Signal Stack Padding (ticks)", GroupName = "Signals - Layout", Order = 20)]
		public int SignalStackPaddingTicks { get; set; } = 4;
		
		#region Trapped Traders Properties

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Trap Lookahead Bars", GroupName = "Trapped Traders", Order = 10)]
		public int TrapLookaheadBars { get; set; } = 1;
		
		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "Trap Move Ticks", GroupName = "Trapped Traders", Order = 20)]
		public int TrapMoveTicks { get; set; } = 15;
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trap Min Total Volume", GroupName = "Trapped Traders", Order = 30)]
		public int TrapMinTotalVolume { get; set; } = 10;
		
		[NinjaScriptProperty]
		[Range(50, 100)]
		[Display(Name = "Trap Min Dominance %", GroupName = "Trapped Traders", Order = 40)]
		public int TrapMinDominancePct { get; set; } = 70;
		
		#endregion
		
		#region Speed of Tape Properties

		[NinjaScriptProperty]
		[Range(50, 10000)]
		[Display(Name = "Speed Window (ms)", GroupName = "Speed of Tape", Order = 10)]
		public int SpeedWindowMs { get; set; } = 1000;
		
		#endregion
		
		#region Signals
		// -------------------- Trapped Traders --------------------
		[NinjaScriptProperty]
		[Display(Name = "Enable Trapped Traders Signal", GroupName = "Signals - Trapped", Order = 0)]
		public bool EnableTrappedTradersSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Trapped Traders Marker", GroupName = "Signals - Trapped", Order = 10)]
		public string TrappedTradersMarker { get; set; } = "⛒";
		
		[XmlIgnore]
		[Display(Name = "Trapped Long Brush", GroupName = "Signals - Trapped", Order = 20)]
		public System.Windows.Media.Brush TrappedLongBrush { get; set; } = Brushes.MediumSpringGreen;
		
		[Browsable(false)]
		public string TrappedLongBrushSerializer
		{
		    get { return Serialize.BrushToString(TrappedLongBrush); }
		    set { TrappedLongBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(Name = "Trapped Short Brush", GroupName = "Signals - Trapped", Order = 30)]
		public System.Windows.Media.Brush TrappedShortBrush { get; set; } = Brushes.DeepPink;
		
		[Browsable(false)]
		public string TrappedShortBrushSerializer
		{
		    get { return Serialize.BrushToString(TrappedShortBrush); }
		    set { TrappedShortBrush = Serialize.StringToBrush(value); }
		}
		
		// -------------------- Stacked Imbalance Trap --------------------
		[NinjaScriptProperty]
		[Display(Name = "Enable SI Trap Signal", GroupName = "Signals - SI Trap", Order = 0)]
		public bool EnableSITrapSignal { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "SI Trap Marker", GroupName = "Signals - SI Trap", Order = 10)]
		public string SITrapMarker { get; set; } = "★";
		
		[XmlIgnore]
		[Display(Name = "SI Trap Long Brush", GroupName = "Signals - SI Trap", Order = 20)]
		public System.Windows.Media.Brush SITrapLongBrush { get; set; } = Brushes.DodgerBlue;
		
		[Browsable(false)]
		public string SITrapLongBrushSerializer
		{
		    get { return Serialize.BrushToString(SITrapLongBrush); }
		    set { SITrapLongBrush = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(Name = "SI Trap Short Brush", GroupName = "Signals - SI Trap", Order = 30)]
		public System.Windows.Media.Brush SITrapShortBrush { get; set; } = Brushes.OrangeRed;
		
		[Browsable(false)]
		public string SITrapShortBrushSerializer
		{
		    get { return Serialize.BrushToString(SITrapShortBrush); }
		    set { SITrapShortBrush = Serialize.StringToBrush(value); }
		}

		
		#endregion

		#region Debug
		
		[NinjaScriptProperty]
		[Display(Name="DebugPrints", GroupName="Debug", Order=0)]
		public bool DebugPrints { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name="DebugOnlyCurrentBar", GroupName="Debug", Order=1)]
		public bool DebugOnlyCurrentBar { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name="DebugMinTotalVol", GroupName="Debug", Order=2)]
		public int DebugMinTotalVol { get; set; } = 1;
		
		// --------------------- Debug ----------------------------
		private int debugLastPrimaryBar = int.MinValue;
		private int debugLinesThisBar = 0;
		private const int DEBUG_MAX_LINES_PER_BAR = 40;
		
		#endregion
		
		#region Stacked Imbalance Trapped Traders
		// -------------------- Stacked Imbalance Trap (footprint-lite) --------------------

		[NinjaScriptProperty]
		[Display(Name = "Enable SI Trap", GroupName = "Trapped Traders - Stacked Imbalance", Order = 0)]
		public bool EnableStackedImbalanceTrap { get; set; } = true;
		
		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "SI Trap Lookahead Bars", GroupName = "Trapped Traders - Stacked Imbalance", Order = 10)]
		public int SITrapLookaheadBars { get; set; } = 1;
		
		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "SI Trap Move Ticks", GroupName = "Trapped Traders - Stacked Imbalance", Order = 20)]
		public int SITrapMoveTicks { get; set; } = 30;
		
		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "SI Aggregation Interval (ticks)", GroupName = "Trapped Traders - Stacked Imbalance", Order = 30)]
		public int SIAggregationIntervalTicks { get; set; } = 4;
		
		[NinjaScriptProperty]
		[Range(1.01, 50)]
		[Display(Name = "SI Imbalance Factor (ImbFact)", GroupName = "Trapped Traders - Stacked Imbalance", Order = 40)]
		public double SIImbFact { get; set; } = 4.0;
		
		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "SI Stacked Levels", GroupName = "Trapped Traders - Stacked Imbalance", Order = 50)]
		public int SIStackedImbalanceLookback { get; set; } = 2;
		
		[NinjaScriptProperty]
		[Range(0, 1000000)]
		[Display(Name = "SI Min Row Volume", GroupName = "Trapped Traders - Stacked Imbalance", Order = 60)]
		public double SIMinRowVolume { get; set; } = 0;
		
		[NinjaScriptProperty]
		[Display(Name = "SI Use Session Open For Aggregation", GroupName = "Trapped Traders - Stacked Imbalance", Order = 70)]
		public bool SIUseSessionOpenForAggregation { get; set; } = true;
		
		#endregion
		
		// -------------------- Flow debug / per-bar summaries --------------------
		private int flowCurBarIdx = -1;
		
		private FlowBarStats flowCur;
		private FlowBarStats flowPendingResolve;
		
		private class FlowBarStats
		{
		    public int      BarIdx;
		    public DateTime BarTime;
		    public double O, H, L, C;
		    public double TotAskAgg;
		    public double TotBidAgg;
		    public double MinTradePx = double.MaxValue;
		    public double MaxTradePx = double.MinValue;
		    public bool SellerExhaustion;
		    public bool BuyerExhaustion;
		    public bool SellerAbsorption;
		    public bool BuyerAbsorption;
			public bool BuyerInitiative;
			public bool SellerInitiative;
		}

        // -------------------- Internal state --------------------
        private SharpDX.Direct2D1.Brush dxAskBrush;
        private SharpDX.Direct2D1.Brush dxBidBrush;
        private SharpDX.Direct2D1.Brush dxOutlineBrush;
        // Zone fill brushes (separate opacity from bubble brushes)
        private SharpDX.Direct2D1.Brush dxAskZoneBrush;
        private SharpDX.Direct2D1.Brush dxBidZoneBrush;

        private double rollingAskVol;
        private double rollingBidVol;

        private bool   prevWasAsk;
        private double prevAsk;
        private double prevBid;

        private bool inTransitionPrev;
        private int  lastPrimaryBarIdx;
        private int  sessionId;

        private double maxVolVisible;
        private bool   isCrypto;

        private Tuple<int, double, int> lastKey;

        private List<Tuple<int, double, int>> pendingRemovals;
        private Dictionary<int, double> maxVolBySession;

        private Dictionary<Tuple<int, double, int>, Tuple<double, double, DateTime, int>> committed;
        private Dictionary<Tuple<int, double, int>, Tuple<double, double, DateTime, int>> staged;

        private Dictionary<double, Tuple<double, double>> volAtPriceThisBar;

        // Hover
        private int panelIndex = int.MinValue;
        private bool forceRefreshArmed;
        private System.Windows.Point cursorPt;
        private SimpleFont hoverFont;
        private float hoverPad;

        private System.Windows.Media.Brush hoverTextBrushWpf;
        private System.Windows.Media.Brush hoverBgBrushWpf;
        private System.Windows.Media.Brush hoverOutlineBrushWpf;

        private SharpDX.Direct2D1.Brush dxHoverText;
        private SharpDX.Direct2D1.Brush dxHoverBg;
        private SharpDX.Direct2D1.Brush dxHoverOutline;

        private Tuple<string, DateTime> hoverText;
        private DispatcherTimer hoverTimer;

		// ==================== Big Trade Zone State ====================
		/// <summary>
		/// Represents a shaded zone created from a qualifying large-trade bubble.
		/// The zone extends from StartBarIdx onward until a bar's high touches TopPrice
		/// or a bar's low touches BottomPrice, at which point EndBarIdx is set.
		/// </summary>
		private class BigTradeZone
		{
		    public int    StartBarIdx;   // bar index where bubble was first committed
		    // Per price-level end bar, exactly like FVG Gap.Ticks:
		    //   value == -1  → tick is still open (extend to ChartBars.ToIndex)
		    //   value >= 0   → bar index where this tick was closed
		    public Dictionary<double, int> Ticks; // key = price level, value = endBarIdx
		    public bool   IsAskDom;      // true = ask dominant → use AskBrush color
		    public double TotalVol;      // for databox display
		    public double TopPrice;      // highest tick price  (for databox / hover)
		    public double BottomPrice;   // lowest  tick price  (for databox / hover)
		}

		private List<BigTradeZone>   bigTradeZones;          // all zones (open + closed)
		// bigTradeZoneTagsDrawn removed — FVG-style render redraws every frame, no tag caching needed
		// Track which bubble keys have already spawned a zone (avoid duplicates)
		private HashSet<string> bigTradeZoneKeys;  // keyed on "barIdx_price" to survive key tiebreaker churn

		// DataBox series for zone top/bottom prices
		private Series<double> zoneTopPrice;
		private Series<double> zoneBotPrice;
		// ==================== End Big Trade Zone State ====================

		
		// -------------------- Trapped Traders (large trade -> adverse move) --------------------
		private int pendingTrappedBarIdx = -1;
		private int pendingTrappedSig    = 0;
		
		// -------------------- SI Trap plot publish --------------------
		private int pendingSITrapBarIdx = -1;
		private int pendingSITrapSig    = 0;


		
		private class LargeTradeEvent
		{
		    public int      BarIdx;
		    public double   Price;
		    public bool     IsBuy;
		    public double   TotalVol;
		    public DateTime Time;
		    public bool     Resolved;
		}
		
		// -------------------- Stacked Imbalance Trap --------------------
		private class StackedImbalanceEvent
		{
		    public int    BarIdx;
		    public double Price;
		    public int    Dir;
		    public bool   Resolved;
		}
		
		private Dictionary<int, Dictionary<double, double>> siAskByBar;
		private Dictionary<int, Dictionary<double, double>> siBidByBar;
		private Dictionary<int, Dictionary<double, double>> siTotByBar;
		
		private HashSet<int> siEventBars;
		private List<StackedImbalanceEvent> siEvents;
		
		private double siSessionOpenPrice;

		
		// -------------------- Speed of Tape --------------------
		private struct TapePrint
		{
		    public DateTime Time;
		    public double   Vol;
		    public bool     IsAsk;
		}
		
		private Queue<TapePrint> tapeWindow;
		private double tapeAskSum;
		private double tapeBidSum;
		private double latestTapeSpeedTotal;
		private int    tapeSpeedPrimaryBarIdx = -1;
		private double latestTapeSpeedMaxThisBar = 0;
		private double latestTapeNetSpeed;
		private int tapeNetPrimaryBarIdx = -1;
		private double latestTapeNetSpeedMaxThisBar = 0;
		
		private HashSet<Tuple<int, double, int>> largeTradeEventKeys;
		private List<LargeTradeEvent>            largeTradeEvents;
		

		// -------------------- signal marker rendering --------------------
		private SimpleFont signalMarkerFont;
		private int        signalMarkerFontSizeCached = -1;

		
		#region Plots
		
		private Series<double> trappedTraders;
		private Series<double> stackedImbTrap;
		private Series<double> speedOfTape;
		private Series<double> maxSpeedOfTape;
		private Series<double> netSpeedOfTape;
		private Series<double> maxNetSpeedOfTape;
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> TrappedTraders
		{
		    get { return trappedTraders; }
		}
		
		[Browsable(false)]
		[XmlIgnore()]
		public Series<double> StackedImbTrap
		{
		    get { return stackedImbTrap; }
		}

		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SpeedOfTape
		{
		    get { return speedOfTape; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> MaxSpeedOfTape
		{
		    get { return maxSpeedOfTape; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> NetSpeedOfTape
		{
		    get { return netSpeedOfTape; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> MaxNetSpeedOfTape
		{
		    get { return maxNetSpeedOfTape; }
		}

		/// <summary>Top price of any active big-trade zone at the current bar (for DataBox).</summary>
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ZoneTopPrice  { get { return zoneTopPrice; } }

		/// <summary>Bottom price of any active big-trade zone at the current bar (for DataBox).</summary>
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ZoneBotPrice  { get { return zoneBotPrice; } }
		
		#endregion

        // -------------------- Lifecycle --------------------
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Order-flow trade detector (clean-room implementation).";
                Name                     = "AlightenOrderFlowToolsV0006";
                Calculate                = Calculate.OnEachTick;
                IsOverlay                = true;
                IsChartOnly              = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = true;
                DrawHorizontalGridLines  = true;
                DrawVerticalGridLines    = true;
                PaintPriceMarkers        = false;
                ScaleJustification       = ScaleJustification.Right;
                IsSuspendedWhileInactive = false;
				ShowTransparentPlotsInDataBox 	= true;

                LargeTradeFindBy       = OrderFlowToolsLargeTradeFindByV0006.BidAsk;
                LargeTradeSizeBy       = OrderFlowToolsLargeTradeSizeByV0006.VisibleArea;
                MaximumMarkerSize      = 100;
                MinimumVolumeForMarker = 100;

                AskBrush     = Brushes.DarkCyan;
                BidBrush     = Brushes.Crimson;
                OutlineBrush = Brushes.DarkGray;
                FillOpacity  = 50;

                HoverValues = true;

				// Zone defaults
				EnableBigTradeZones    = true;
				BigTradeZoneThreshold  = 50;
				BigTradeZoneOpacity    = 20;
				
				AddPlot(Brushes.Transparent, "TrappedTraders");
				AddPlot(Brushes.Transparent, "StackedImbTrap");
				AddPlot(Brushes.Transparent, "SpeedOfTape");
				AddPlot(Brushes.Transparent, "MaxSpeedOfTape");
				AddPlot(Brushes.Transparent, "NetSpeedOfTape");
				AddPlot(Brushes.Transparent, "MaxNetSpeedOfTape");
				// Zone DataBox plots
				AddPlot(Brushes.Transparent, "ZoneTopPrice");
				AddPlot(Brushes.Transparent, "ZoneBotPrice");

                return;
            }

            if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);

                if (HoverValues)
                {
                    hoverPad            = 3f;
                    hoverTextBrushWpf   = Brushes.White;
                    hoverBgBrushWpf     = Brushes.Black;
                    hoverOutlineBrushWpf= Brushes.DimGray;

                    (hoverTextBrushWpf as Freezable)?.Freeze();
                    (hoverBgBrushWpf as Freezable)?.Freeze();
                    (hoverOutlineBrushWpf as Freezable)?.Freeze();
                }
                return;
            }

            if (State == State.DataLoaded)
            {
                committed       = new Dictionary<Tuple<int, double, int>, Tuple<double, double, DateTime, int>>();
                staged          = new Dictionary<Tuple<int, double, int>, Tuple<double, double, DateTime, int>>();
                pendingRemovals = new List<Tuple<int, double, int>>(64);

                isCrypto = Instrument.MasterInstrument.InstrumentType == InstrumentType.CryptoCurrency;
				
				largeTradeEventKeys = new HashSet<Tuple<int, double, int>>();
				largeTradeEvents    = new List<LargeTradeEvent>(256);
				
				tapeWindow = new Queue<TapePrint>(2048);
				tapeAskSum = 0;
				tapeBidSum = 0;
				latestTapeSpeedTotal = 0;
				
				siAskByBar   = new Dictionary<int, Dictionary<double, double>>(2048);
				siBidByBar   = new Dictionary<int, Dictionary<double, double>>(2048);
				siTotByBar   = new Dictionary<int, Dictionary<double, double>>(2048);
				
				siEventBars  = new HashSet<int>();
				siEvents     = new List<StackedImbalanceEvent>(512);
				
				siSessionOpenPrice = 0.0;

				// Zone init
				bigTradeZones        = new List<BigTradeZone>(256);
				// bigTradeZoneTagsDrawn not used
				bigTradeZoneKeys     = new HashSet<string>();
				
				trappedTraders          = Values[0];
				stackedImbTrap          = Values[1];
				speedOfTape             = Values[2];
				maxSpeedOfTape          = Values[3];
				netSpeedOfTape          = Values[4];
				maxNetSpeedOfTape       = Values[5];
				zoneTopPrice            = Values[6];
				zoneBotPrice            = Values[7];

                return;
            }

            if (State == State.Historical)
            {
                if (HoverValues && ChartControl != null)
                {
                    hoverFont  = ChartControl.Properties.LabelFont;
                    hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, HoverTimerTick, ChartControl.Dispatcher);
                }
                return;
            }

            if (State == State.Terminated)
            {
                if (HoverValues)
                {
                    if (panelIndex != int.MinValue && ChartControl != null && ChartControl.ChartPanels.Count > panelIndex)
                    {
                        var p = ChartControl.ChartPanels[panelIndex];
                        if (p != null)
                            p.MouseMove -= OnMouseMove;
                    }

                    if (hoverTimer != null)
                    {
                        hoverTimer.Stop();
                        hoverTimer.Tick -= HoverTimerTick;
                        hoverTimer = null;
                    }
                }
            }
        }

        protected override void OnBarUpdate()
		{
		    CalculateInternal(false);
		
		    if (BarsInProgress != 0)
		        return;
		
		    if (CurrentBar < 1)
		    {
		        trappedTraders[0] 		= 0;
				stackedImbTrap[0]       = 0;
				speedOfTape[0] 			= 0;
				maxSpeedOfTape[0]		= 0;
				netSpeedOfTape[0] 		= 0;
				maxNetSpeedOfTape[0]	= 0;
				zoneTopPrice[0]         = 0;
				zoneBotPrice[0]         = 0;
		        return;
		    }
		
		    speedOfTape[0]        = latestTapeSpeedTotal;
			maxSpeedOfTape[0]     = latestTapeSpeedMaxThisBar;
			netSpeedOfTape[0]     = latestTapeNetSpeed;
			maxNetSpeedOfTape[0]  = latestTapeNetSpeedMaxThisBar;

			// Update zone DataBox plots every tick for current bar
			UpdateZoneDataBoxPlots();
			
			trappedTraders[0]       = 0;
			stackedImbTrap[0]       = 0;
			
			if (!IsFirstTickOfBar)
			    return;
			
			if (IsFirstTickOfBar && BarsArray[0].IsFirstBarOfSession)
			    siSessionOpenPrice = Open[0];

			// Evaluate trapped traders for the bar that just closed
			EvaluateTrappedTraders_BIP0(CurrentBars[0] - 1);
			
			if (pendingTrappedBarIdx == CurrentBars[0] - 1)
			    trappedTraders[1] = pendingTrappedSig;

			if (pendingSITrapBarIdx == CurrentBars[0] - 1)
			    stackedImbTrap[1] = pendingSITrapSig;

			// Check open zones for invalidation at the bar that just closed
			if (EnableBigTradeZones && bigTradeZones != null)
			    CheckZoneInvalidation(CurrentBars[0] - 1);
			
			DrawFlowSignalMarkers_BIP0(1);
		}

		// ==================== Big Trade Zone Methods ====================

		/// <summary>
		/// Called from MaybeRecordLargeTradeEvent when a qualifying bubble is committed.
		/// Creates a new zone anchored to the bar's high/low.
		/// </summary>
		private void MaybAddBigTradeZone(Tuple<int, double, int> key, double askVol, double bidVol)
		{
		    if (!EnableBigTradeZones)
		        return;

		    double tot = askVol + bidVol;
		    if (tot < BigTradeZoneThreshold)
		        return;

		    // One zone per (bar, price) — string key ignores tiebreaker churn
		    string zoneDedupeKey = key.Item1 + "_" + key.Item2.ToString("F5");
		    if (bigTradeZoneKeys.Contains(zoneDedupeKey))
		        return;

		    int barIdx = key.Item1;
		    if (barIdx < 0 || barIdx >= BarsArray[0].Count)
		        return;

		    bigTradeZoneKeys.Add(zoneDedupeKey);

		    // Build per-tick dictionary exactly like FVG does:
		    // populate every tick level from bot to top, each starting as open (-1)
		    double halfHeight = (BigTradeZoneHeightTicks / 2.0) * TickSize;
		    double bubblePx   = key.Item2;
		    double top        = Math.Round((bubblePx + halfHeight) / TickSize) * TickSize;
		    double bot        = Math.Round((bubblePx - halfHeight) / TickSize) * TickSize;

		    var ticks = new Dictionary<double, int>();
		    for (double p = bot; p <= top + TickSize * 0.001; p += TickSize)
		    {
		        p = Math.Round(p / TickSize) * TickSize;
		        ticks[p] = -1;  // -1 = open, extend to ChartBars.ToIndex in render
		    }

		    bool isAsk = askVol >= bidVol;

		    var zone = new BigTradeZone
		    {
		        StartBarIdx  = barIdx,
		        Ticks        = ticks,
		        IsAskDom     = isAsk,
		        TotalVol     = tot,
		        TopPrice     = top,
		        BottomPrice  = bot,
		    };

		    bigTradeZones.Add(zone);

		    if (DebugPrints)
		        Print($"[BTZ ADD] bar={barIdx} top={top:F2} bot={bot:F2} ticks={ticks.Count} vol={tot:0} ask={isAsk}");
		}

		/// <summary>
		/// On each new bar close, check all open zones to see if this bar invalidated them.
		/// A zone is invalidated when the bar's high >= zone.TopPrice or bar's low <= zone.BottomPrice.
		/// </summary>
		private void CheckZoneInvalidation(int barJustClosed)
		{
		    if (bigTradeZones == null || barJustClosed < 0)
		        return;

		    double hi = BarsArray[0].GetHigh(barJustClosed);
		    double lo = BarsArray[0].GetLow(barJustClosed);

		    foreach (var z in bigTradeZones)
		    {
		        if (z.Ticks == null || z.Ticks.Count == 0)
		            continue;

		        // Mirror FVG: skip the zone's own bar and the bar immediately after
		        if (barJustClosed < z.StartBarIdx + 2)
		            continue;

		        // For each price tick in the zone, if the bar's range covers it
		        // and it's still open (-1), close it at this bar — exactly like FVG.Check()
		        foreach (double p in z.Ticks.Keys.ToList())
		        {
		            if (z.Ticks[p] != -1)
		                continue;  // already closed

		            if (p >= lo && p <= hi)
		            {
		                z.Ticks[p] = barJustClosed;

		                if (DebugPrints)
		                    Print($"[BTZ TICK CLOSE] bar={barJustClosed} price={p:F2} hi={hi:F2} lo={lo:F2}");
		            }
		        }
		    }
		}

		/// <summary>
		/// Updates the ZoneTopPrice / ZoneBotPrice DataBox plots for the current bar.
		/// Publishes the top/bottom of the nearest active zone that covers this bar.
		/// </summary>
		private void UpdateZoneDataBoxPlots()
		{
		    if (!EnableBigTradeZones || bigTradeZones == null || bigTradeZones.Count == 0)
		    {
		        zoneTopPrice[0] = 0;
		        zoneBotPrice[0] = 0;
		        return;
		    }

		    int    curBar  = CurrentBars[0];
		    double bestTop = 0, bestBot = 0, bestVol = 0;

		    foreach (var z in bigTradeZones)
		    {
		        if (z.Ticks == null || z.StartBarIdx > curBar)
		            continue;

		        // Zone is active for this bar if any tick is still open (-1)
		        // or was closed at or after curBar
		        bool active = z.Ticks.Values.Any(v => v == -1 || v >= curBar);
		        if (!active)
		            continue;

		        if (z.TotalVol > bestVol)
		        {
		            bestVol = z.TotalVol;
		            bestTop = z.TopPrice;
		            bestBot = z.BottomPrice;
		        }
		    }

		    zoneTopPrice[0] = bestTop;
		    zoneBotPrice[0] = bestBot;
		}

		// ==================== End Big Trade Zone Methods ====================

        // -------------------- Core calc (tick series) --------------------
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CalculateInternal(bool forceCurrentTickBar)
        {
            if (BarsInProgress != 1)
                return;

            if (CurrentBars[0] <= 0 || BarsArray == null || BarsArray.Length < 2)
                return;

            int tickCount = BarsArray[1].Count;
            if (tickCount <= 0 || CurrentBars[1] < 0)
                return;

            int lag = tickCount - 1 - CurrentBars[1];
            bool inTransition = State == State.Realtime && lag > 1;

            if (!inTransition && inTransitionPrev && !forceCurrentTickBar && Calculate == Calculate.OnBarClose)
                CalculateInternal(true);

            int tickBarIdx =
                (State == State.Historical || inTransition || Calculate > Calculate.OnBarClose || forceCurrentTickBar)
                    ? CurrentBars[1]
                    : Math.Min(CurrentBars[1] + 1, tickCount - 1);

            inTransitionPrev = inTransition;

            if (BarsArray[1].IsFirstBarOfSession)
                sessionId++;

            double ask   = BarsArray[1].GetAsk(tickBarIdx);
            double bid   = BarsArray[1].GetBid(tickBarIdx);
            double close = BarsArray[1].GetClose(tickBarIdx);

            double vol = isCrypto
                ? Globals.ToCryptocurrencyVolume(BarsArray[1].GetVolume(tickBarIdx))
                : (double)BarsArray[1].GetVolume(tickBarIdx);

            DateTime t = BarsArray[1].GetTime(tickBarIdx);

            bool isAskAggressor = close >= ask || (close > bid && prevWasAsk);

            bool mustStage =
                ChartPanel != null &&
                CurrentBars[0] <= BarsArray[0].Count - ((Calculate == Calculate.OnBarClose) ? 2 : 1);

            int timeBasedRealtimeAdjust =
                (BarsArray[0].BarsType.IsTimeBased && State == State.Realtime && Calculate != Calculate.OnBarClose) ? 1 : 0;
			
            int primaryBarIdx = CurrentBars[0] + ((t == Times[0][0]) ? 0 : 1) - timeBasedRealtimeAdjust;
			
			if (EnableStackedImbalanceTrap)
    			SI_AggregateTick_BIP1(primaryBarIdx, close, vol, isAskAggressor);


            if (LargeTradeFindBy == OrderFlowToolsLargeTradeFindByV0006.BidAsk)
            {
                if (ask.ApproxCompare(prevAsk) == 0 && bid.ApproxCompare(prevBid) == 0)
                {
                    rollingAskVol += isAskAggressor ? vol : 0.0;
                    rollingBidVol += isAskAggressor ? 0.0 : vol;
                }
                else
                {
                    rollingAskVol = isAskAggressor ? vol : 0.0;
                    rollingBidVol = isAskAggressor ? 0.0 : vol;
                    lastKey = null;
                }
				
				UpdateSpeedOfTape(vol, isAskAggressor, t);

                if (rollingAskVol + rollingBidVol >= MinimumVolumeForMarker)
				{
				    var key = MakeUniqueKey(primaryBarIdx, close, 0);
				    UpsertOrb(key, rollingAskVol, rollingBidVol, t, sessionId, mustStage, replaceLastKey: true);
					MaybeRecordLargeTradeEvent(key, rollingAskVol, rollingBidVol, t);
				    TrackSessionMax(rollingAskVol + rollingBidVol);
				}
            }
            else if (LargeTradeFindBy == OrderFlowToolsLargeTradeFindByV0006.Price)
            {
                rollingAskVol = isAskAggressor ? vol : 0.0;
                rollingBidVol = isAskAggressor ? 0.0 : vol;

                if (lastPrimaryBarIdx != (CurrentBars[0] - timeBasedRealtimeAdjust) || volAtPriceThisBar == null)
                    volAtPriceThisBar = new Dictionary<double, Tuple<double, double>>();

                if (volAtPriceThisBar.TryGetValue(close, out var ab))
                    volAtPriceThisBar[close] = new Tuple<double, double>(ab.Item1 + rollingAskVol, ab.Item2 + rollingBidVol);
                else
                    volAtPriceThisBar[close] = new Tuple<double, double>(rollingAskVol, rollingBidVol);

                var key = new Tuple<int, double, int>(primaryBarIdx, close, 0);

                double totalHere = 0.0;

                lock (staged)
                {
                    if (!mustStage && committed.TryGetValue(key, out var cur))
                    {
                        committed[key] = new Tuple<double, double, DateTime, int>(cur.Item1 + rollingAskVol, cur.Item2 + rollingBidVol, t, sessionId);
                        totalHere = committed[key].Item1 + committed[key].Item2;
                    }
                    else if (staged.TryGetValue(key, out var tmp))
                    {
                        staged[key] = new Tuple<double, double, DateTime, int>(tmp.Item1 + rollingAskVol, tmp.Item2 + rollingBidVol, t, sessionId);
                        totalHere = staged[key].Item1 + staged[key].Item2;
                    }
                    else
                    {
                        var atPrice = volAtPriceThisBar[close];
                        if (atPrice.Item1 + atPrice.Item2 >= MinimumVolumeForMarker)
                        {
                            if (mustStage)
                                staged[key] = new Tuple<double, double, DateTime, int>(atPrice.Item1, atPrice.Item2, t, sessionId);
                            else
                                committed[key] = new Tuple<double, double, DateTime, int>(atPrice.Item1, atPrice.Item2, t, sessionId);

                            totalHere = atPrice.Item1 + atPrice.Item2;
                        }
                    }
                }

                TrackSessionMax(totalHere);
            }
            else // Block
            {
                rollingAskVol = isAskAggressor ? vol : 0.0;
                rollingBidVol = isAskAggressor ? 0.0 : vol;

                if (rollingAskVol + rollingBidVol >= MinimumVolumeForMarker)
                {
                    var key = MakeUniqueKey(primaryBarIdx, close, 0);
                    lock (staged)
                    {
                        if (mustStage)
                            staged[key] = new Tuple<double, double, DateTime, int>(rollingAskVol, rollingBidVol, t, sessionId);
                        else
                            committed[key] = new Tuple<double, double, DateTime, int>(rollingAskVol, rollingBidVol, t, sessionId);
                    }
					MaybeRecordLargeTradeEvent(key, rollingAskVol, rollingBidVol, t);
                    TrackSessionMax(rollingAskVol + rollingBidVol);
                }
            }

            prevAsk = ask;
            prevBid = bid;
            prevWasAsk = isAskAggressor;
            lastPrimaryBarIdx = CurrentBars[0] - timeBasedRealtimeAdjust;
        }

        private Tuple<int, double, int> MakeUniqueKey(int barIndex, double price, int tieBreaker)
        {
            var key = new Tuple<int, double, int>(barIndex, price, tieBreaker);
            lock (staged)
            {
                while (committed.ContainsKey(key) || staged.ContainsKey(key))
                    key = new Tuple<int, double, int>(barIndex, price, ++tieBreaker);
            }
            return key;
        }

        private void UpsertOrb(
		    Tuple<int, double, int> key,
		    double askVol,
		    double bidVol,
		    DateTime time,
		    int sess,
		    bool mustStage,
		    bool replaceLastKey)
		{
		    lock (staged)
		    {
		        if (replaceLastKey && lastKey != null)
		        {
		            if (committed.ContainsKey(lastKey))
		            {
		                if (mustStage)
		                    pendingRemovals.Add(lastKey);
		                else
		                    committed.Remove(lastKey);
		            }
		
		            if (staged.ContainsKey(lastKey))
		                staged.Remove(lastKey);
		        }
		
		        if (pendingRemovals.Contains(key))
		            pendingRemovals.Remove(key);
		
		        if (mustStage)
		            staged[key] = new Tuple<double, double, DateTime, int>(askVol, bidVol, time, sess);
		        else
		            committed[key] = new Tuple<double, double, DateTime, int>(askVol, bidVol, time, sess);
		
		        lastKey = key;
		    }
		}


        private void TrackSessionMax(double vol)
        {
            if (LargeTradeSizeBy != OrderFlowToolsLargeTradeSizeByV0006.Session)
                return;

            if (maxVolBySession == null)
                maxVolBySession = new Dictionary<int, double>();

            if (maxVolBySession.TryGetValue(sessionId, out var cur))
            {
                if (vol > cur)
                    maxVolBySession[sessionId] = vol;
            }
            else
            {
                maxVolBySession[sessionId] = vol;
            }
        }

        // -------------------- Min/Max & selection --------------------
        public override void OnCalculateMinMax()
        {
            if (ChartBars == null)
                return;

            double hi = double.MinValue;
            double lo = double.MaxValue;

            foreach (var k in committed.Keys.Where(key =>
                     key.Item1 >= ChartBars.FromIndex - Displacement &&
                     key.Item1 <= Math.Max(ChartBars.ToIndex, ChartBars.GetBarIdxByTime(ChartControl, ChartControl.LastTimePainted)) - Displacement))
            {
                if (k.Item2 > hi) hi = k.Item2;
                if (k.Item2 < lo) lo = k.Item2;
            }

            MaxValue = hi;
            MinValue = lo;
        }

        protected override System.Windows.Point[] OnGetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
        {
            if (!IsSelected || Count == 0 || ChartBars == null)
                return Array.Empty<System.Windows.Point>();

            var pts = new List<System.Windows.Point>();

            foreach (var key in committed.Keys.Where(k =>
                     k.Item1 >= ChartBars.FromIndex - Displacement &&
                     k.Item1 <= Math.Max(ChartBars.ToIndex, ChartBars.GetBarIdxByTime(chartControl, chartControl.LastTimePainted)) - Displacement &&
                     k.Item3 == 0))
            {
                pts.Add(new System.Windows.Point(chartControl.GetXByBarIndex(ChartBars, key.Item1 + Displacement),
                                  chartScale.GetYByValue(key.Item2)));
            }

            return pts.ToArray();
        }

        // -------------------- Render target management --------------------
        public override void OnRenderTargetChanged()
        {
            dxAskBrush?.Dispose();
            dxBidBrush?.Dispose();
            dxOutlineBrush?.Dispose();
            dxAskZoneBrush?.Dispose();
            dxBidZoneBrush?.Dispose();

            if (HoverValues)
            {
                dxHoverText?.Dispose();
                dxHoverBg?.Dispose();
                dxHoverOutline?.Dispose();
            }

            if (RenderTarget == null)
                return;

            dxAskBrush     = AskBrush.ToDxBrush(RenderTarget);
            dxBidBrush     = BidBrush.ToDxBrush(RenderTarget);
            dxOutlineBrush = OutlineBrush.ToDxBrush(RenderTarget);

            float o = FillOpacity / 100f;
            dxAskBrush.Opacity     = o;
            dxBidBrush.Opacity     = o;
            dxOutlineBrush.Opacity = o;

            // Zone brushes use same color as bubbles but with their own opacity
            dxAskZoneBrush         = AskBrush.ToDxBrush(RenderTarget);
            dxBidZoneBrush         = BidBrush.ToDxBrush(RenderTarget);
            dxAskZoneBrush.Opacity = BigTradeZoneOpacity / 100f;
            dxBidZoneBrush.Opacity = BigTradeZoneOpacity / 100f;

            if (HoverValues)
            {
                dxHoverText    = hoverTextBrushWpf.ToDxBrush(RenderTarget);
                dxHoverBg      = hoverBgBrushWpf.ToDxBrush(RenderTarget);
                dxHoverOutline = hoverOutlineBrushWpf.ToDxBrush(RenderTarget);
            }
        }

        // -------------------- Render --------------------
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (BarsArray == null || BarsArray.Length == 0 || BarsArray[0] == null || ChartBars == null || ChartBars.Count <= 0)
                return;
			
			if (!EnableLargeTradeBubbles && !EnableBigTradeZones)
			{
			    if (HoverValues && panelIndex != int.MinValue && chartControl.ChartPanels.Count > panelIndex && chartControl.ChartPanels[panelIndex] != null)
			        chartControl.ChartPanels[panelIndex].MouseMove -= OnMouseMove;
			    panelIndex = int.MinValue;
			    hoverText  = null;
			    return;
			}

            if (HoverValues && chartControl.ChartPanels.IndexOf(ChartPanel) != panelIndex)
            {
                if (panelIndex != int.MinValue && chartControl.ChartPanels.Count > panelIndex && chartControl.ChartPanels[panelIndex] != null)
                    chartControl.ChartPanels[panelIndex].MouseMove -= OnMouseMove;

                ChartPanel.MouseMove += OnMouseMove;
                panelIndex = chartControl.ChartPanels.IndexOf(ChartPanel);
                hoverText = null;
            }

            CommitStaged();

            var prevAA = RenderTarget.AntialiasMode;
            RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;

            int firstBar = ChartBars.FromIndex - Displacement;
            int lastBar  = Math.Max(ChartBars.ToIndex, ChartBars.GetBarIdxByTime(chartControl, chartControl.LastTimePainted)) - Displacement;

			// ==================== Draw Big Trade Zones (FVG-style per-tick render) ====================
			if (EnableBigTradeZones && bigTradeZones != null && dxAskZoneBrush != null && dxBidZoneBrush != null)
			{
			    float zoneOpacity = BigTradeZoneOpacity / 100f;

			    foreach (var z in bigTradeZones)
			    {
			        if (z.Ticks == null || z.Ticks.Count == 0)
			            continue;

			        // Skip zone if it started beyond the visible right edge
			        if (z.StartBarIdx > lastBar)
			            continue;

			        // x origin — exactly as FVG: GetXByBarIndex with Displacement
			        double xOrigin = chartControl.GetXByBarIndex(ChartBars, z.StartBarIdx + Displacement);

			        var zoneBrush = z.IsAskDom ? dxAskZoneBrush : dxBidZoneBrush;
			        zoneBrush.Opacity = zoneOpacity;

			        foreach (var kvp in z.Ticks)
			        {
			            double price   = kvp.Key;
			            int    endBar  = kvp.Value;  // -1 = still open

			            // y: centred on tick, half tick above and below — exactly as FVG
			            double yTop = chartScale.GetYByValue(price + TickSize * 0.5);
			            double yBot = chartScale.GetYByValue(price - TickSize * 0.5);
			            double h    = yBot - yTop;
			            if (h <= 0) h = 1;

			            // xRight: open → ChartBars.ToIndex (rightmost bar in chart data)
			            //         closed → the bar that closed this tick
			            int endIdx = endBar > 0 ? endBar + Displacement : ChartBars.ToIndex;
			            double xEnd = chartControl.GetXByBarIndex(ChartBars, endIdx);
			            double w    = xEnd - xOrigin;
			            if (w <= 0) continue;  // zone is entirely to the left of visible area

			            RenderTarget.FillRectangle(
			                new SharpDX.RectangleF((float)xOrigin, (float)yTop, (float)w, (float)h),
			                zoneBrush);
			        }

			        // Draw top and bottom boundary lines across the full zone width
			        // Use the widest open tick for the line length
			        int    lineEndBar = z.Ticks.Values.Any(v => v == -1)
			                           ? ChartBars.ToIndex
			                           : z.Ticks.Values.Max() + Displacement;
			        double xLineEnd   = chartControl.GetXByBarIndex(ChartBars, lineEndBar);
			        double lineW      = xLineEnd - xOrigin;
			        if (lineW > 0)
			        {
			            var prevOp = dxOutlineBrush.Opacity;
			            dxOutlineBrush.Opacity = Math.Min(1f, zoneOpacity * 2f);
			            double yLineTop = chartScale.GetYByValue(z.TopPrice  + TickSize * 0.5);
			            double yLineBot = chartScale.GetYByValue(z.BottomPrice - TickSize * 0.5);
			            RenderTarget.DrawLine(new SharpDX.Vector2((float)xOrigin, (float)yLineTop), new SharpDX.Vector2((float)xLineEnd, (float)yLineTop), dxOutlineBrush, 1f);
			            RenderTarget.DrawLine(new SharpDX.Vector2((float)xOrigin, (float)yLineBot), new SharpDX.Vector2((float)xLineEnd, (float)yLineBot), dxOutlineBrush, 1f);
			            dxOutlineBrush.Opacity = prevOp;
			        }
			    }
			}
			// ==================== End Big Trade Zones ====================

			// -------------------- Large Trade Bubble draw gate --------------------
			if (!EnableLargeTradeBubbles)
			{
			    RenderTarget.AntialiasMode = prevAA;
			    return;
			}

            if (LargeTradeSizeBy == OrderFlowToolsLargeTradeSizeByV0006.VisibleArea)
            {
                maxVolVisible = committed
                    .Where(kvp => kvp.Key.Item1 >= firstBar && kvp.Key.Item1 <= lastBar)
                    .Select(kvp => (kvp.Value?.Item1 ?? 0.0) + (kvp.Value?.Item2 ?? 0.0))
                    .DefaultIfEmpty(0.0)
                    .Max();

                if (maxVolVisible <= 0.0)
                {
                    RenderTarget.AntialiasMode = prevAA;
                    return;
                }
            }

            foreach (var kvp in committed
                     .Where(k => k.Key.Item1 >= firstBar && k.Key.Item1 <= lastBar)
                     .OrderBy(k => k.Value.Item3))
            {
                var key   = kvp.Key;
                var value = kvp.Value;

                if (LargeTradeSizeBy == OrderFlowToolsLargeTradeSizeByV0006.Session && maxVolBySession != null && maxVolBySession.TryGetValue(value.Item4, out var sv))
                    maxVolVisible = sv;

                int barIdx = (chartControl.BarSpacingType == BarSpacingType.TimeBased)
                    ? ChartBars.GetBarIdxByTime(chartControl, value.Item3)
                    : key.Item1;

                var center = new System.Windows.Point(
                    chartControl.GetXByBarIndex(ChartBars, barIdx + Displacement),
                    chartScale.GetYByValue(key.Item2));

                double askV = value.Item1;
                double bidV = value.Item2;
                double tot  = askV + bidV;
                if (tot <= 0) continue;

                double bidFrac = bidV / tot;
                double askFrac = 1.0 - bidFrac;

                double dominant = Math.Max(bidFrac, askFrac);
                double minor    = Math.Min(bidFrac, askFrac);

                int px = (int)(
                    Math.Max(1.0, tot - MinimumVolumeForMarker) /
                    Math.Max(1.0, maxVolVisible - MinimumVolumeForMarker) *
                    (MaximumMarkerSize - 10.0)) + 10;

                double r = px / 2.0;

                if (bidFrac.ApproxCompare(0.0) == 0 || bidFrac.ApproxCompare(1.0) == 0)
                {
                    RenderTarget.FillEllipse(
                        new SharpDX.Direct2D1.Ellipse(new Vector2((float)center.X, (float)center.Y), (float)r, (float)r),
                        (bidFrac.CompareTo(1.0) == 0) ? dxBidBrush : dxAskBrush);
                }
                else
                {
                    double chordX = Math.Sqrt(r * r - (r - px * minor) * (r - px * minor));
                    float y1 = (bidFrac > askFrac) ? (float)(center.Y + r - px * minor) : (float)(center.Y - r + px * minor);

                    var arc1 = new SharpDX.Direct2D1.ArcSegment
                    {
                        ArcSize = ArcSize.Large,
                        Point = new Vector2((float)(center.X + chordX), y1),
                        SweepDirection = (dominant.ApproxCompare(bidFrac) == 0) ? SharpDX.Direct2D1.SweepDirection.Clockwise : SharpDX.Direct2D1.SweepDirection.CounterClockwise,
                        Size = new Size2F((float)r, (float)r)
                    };

                    using (var geo = new SharpDX.Direct2D1.PathGeometry(Globals.D2DFactory))
                    {
                        var sink = geo.Open();
                        sink.BeginFigure(new Vector2((float)(center.X - chordX), y1), FigureBegin.Filled);
                        sink.AddArc(arc1);
                        sink.EndFigure(FigureEnd.Closed);
                        sink.Close();
                        RenderTarget.FillGeometry(geo, (bidFrac >= askFrac) ? dxBidBrush : dxAskBrush);
                    }

                    float y2 = (bidFrac < askFrac) ? (float)(center.Y + r - px * dominant) : (float)(center.Y - r + px * dominant);

                    var arc2 = new SharpDX.Direct2D1.ArcSegment
                    {
                        ArcSize = ArcSize.Small,
                        Point = new Vector2((float)(center.X + chordX), y2),
                        SweepDirection = (dominant.ApproxCompare(bidFrac) == 0) ? SharpDX.Direct2D1.SweepDirection.CounterClockwise : SharpDX.Direct2D1.SweepDirection.Clockwise,
                        Size = new Size2F((float)r, (float)r)
                    };

                    using (var geo2 = new SharpDX.Direct2D1.PathGeometry(Globals.D2DFactory))
                    {
                        var sink2 = geo2.Open();
                        sink2.BeginFigure(new Vector2((float)(center.X - chordX), y2), FigureBegin.Filled);
                        sink2.AddArc(arc2);
                        sink2.EndFigure(FigureEnd.Closed);
                        sink2.Close();
                        RenderTarget.FillGeometry(geo2, (bidFrac < askFrac) ? dxBidBrush : dxAskBrush);
                    }
                }

                RenderTarget.DrawEllipse(
                    new SharpDX.Direct2D1.Ellipse(new Vector2((float)center.X, (float)center.Y), (float)r, (float)r),
                    dxOutlineBrush);
            }

            if (HoverValues && hoverText != null)
            {
                var tf = hoverFont.ToDirectWriteTextFormat();
                using (var layout = new TextLayout(Globals.DirectWriteFactory, hoverText.Item1, tf, (float)ChartPanel.W, tf.FontSize))
                {
                    layout.MaxWidth  = layout.Metrics.Width;
                    layout.MaxHeight = layout.Metrics.Height;

                    var rect = new RectangleF((float)cursorPt.X + 10f, (float)cursorPt.Y + 10f,
                                              layout.MaxWidth + hoverPad * 2f,
                                              layout.MaxHeight + hoverPad * 2f);

                    RenderTarget.FillRectangle(rect, dxHoverBg);
                    RenderTarget.DrawRectangle(rect, dxHoverOutline);
                    RenderTarget.DrawTextLayout(new Vector2((float)cursorPt.X + 10f + hoverPad,
                                                           (float)cursorPt.Y + 10f + hoverPad),
                                                layout, dxHoverText);
                }
            }

            forceRefreshArmed = false;
            RenderTarget.AntialiasMode = prevAA;
        }

        private void CommitStaged()
		{
		    if (ChartPanel == null)
		        return;
		
		    if (!Monitor.TryEnter(staged))
		        return;
		
		    try
		    {		       
		        foreach (var k in pendingRemovals)
		            committed.Remove(k);
		        pendingRemovals.Clear();
		
		        foreach (var kvp in staged)
		            committed[kvp.Key] = kvp.Value;
		
		        staged.Clear();
		    }
		    finally
		    {
		        Monitor.Exit(staged);
		    }
		}


        // -------------------- Hover --------------------
        private void HoverTimerTick(object sender, EventArgs e)
        {
            if (ChartBars == null || ChartBars.Count <= 0 || ChartPanel == null)
                return;
			
			if (!EnableLargeTradeBubbles && !EnableBigTradeZones)
			{
			    hoverText = null;
			    hoverTimer.IsEnabled = false;
			    return;
			}

            cursorPt.X = Mouse.GetPosition(ChartPanel.ChartControl).X.ConvertToHorizontalPixels(ChartPanel.ChartControl.PresentationSource);
            cursorPt.Y = Mouse.GetPosition(ChartPanel.ChartControl).Y.ConvertToVerticalPixels(ChartPanel.ChartControl.PresentationSource);

            int windowBars = MaximumMarkerSize / Convert.ToInt32(ChartControl.GetBarPaintWidth(ChartBars));
            int barAtX      = ChartBars.GetBarIdxByX(ChartControl, (int)cursorPt.X);
            int start       = barAtX - windowBars;
            int end         = barAtX + windowBars;

            Tuple<string, DateTime> best = null;

			// --- Check bubble hover ---
			if (EnableLargeTradeBubbles)
			{
			    foreach (var kvp in committed.Where(k => k.Key.Item1 >= start && k.Key.Item1 <= end))
			    {
			        double y = ChartPanel.Scales[ScaleJustification].GetYByValue(kvp.Key.Item2);

			        if (best != null && best.Item2 >= kvp.Value.Item3)
			            continue;

			        if (cursorPt.Y < y - MaximumMarkerSize || cursorPt.Y > y + MaximumMarkerSize)
			            continue;

			        double dx = ChartControl.GetXByBarIndex(ChartBars, kvp.Key.Item1 + Displacement) - cursorPt.X;
			        double dy = y - cursorPt.Y;

			        double tot = kvp.Value.Item1 + kvp.Value.Item2;
			        double r = ((int)(Math.Max(1.0, tot - MinimumVolumeForMarker) /
			                          Math.Max(1.0, maxVolVisible - MinimumVolumeForMarker) *
			                          (MaximumMarkerSize - 10.0)) + 10) / 2.0;

			        if (dx * dx + dy * dy <= r * r)
			        {
			            string fmtBid = isCrypto ? Globals.FormatCryptocurrencyQuantity(kvp.Value.Item2, true) : Globals.FormatQuantity((long)kvp.Value.Item2, false);
			            string fmtAsk = isCrypto ? Globals.FormatCryptocurrencyQuantity(kvp.Value.Item1, true) : Globals.FormatQuantity((long)kvp.Value.Item1, false);
			            string fmtTot = isCrypto ? Globals.FormatCryptocurrencyQuantity(kvp.Value.Item1 + kvp.Value.Item2, true) : Globals.FormatQuantity((long)(kvp.Value.Item1 + kvp.Value.Item2), false);
			            string fmtPx  = Instrument.MasterInstrument.FormatPrice(kvp.Key.Item2, true);

			            best = new Tuple<string, DateTime>($"Bid: {fmtBid}\nAsk: {fmtAsk}\nTotal: {fmtTot}\nPrice: {fmtPx}", kvp.Value.Item3);
			        }
			    }
			}

			// --- Check zone hover (only if no bubble matched) ---
			if (best == null && EnableBigTradeZones && bigTradeZones != null)
			{
			    double cursorPrice = ChartPanel.Scales[ScaleJustification].GetValueByY((int)cursorPt.Y);
			    int    cursorBar   = barAtX;

			    foreach (var z in bigTradeZones)
			    {
			        if (z.Ticks == null || z.StartBarIdx > cursorBar)
			            continue;

			        // Zone is visible at cursorBar if any tick covering cursorPrice
			        // is still open or was closed at/after cursorBar
			        double nearestTick = Math.Round(cursorPrice / TickSize) * TickSize;
			        bool tickActive = z.Ticks.TryGetValue(nearestTick, out int tickEnd)
			                         && (tickEnd == -1 || tickEnd >= cursorBar);
			        if (!tickActive)
			            continue;

			        string fmtTop = Instrument.MasterInstrument.FormatPrice(z.TopPrice, true);
			        string fmtBot = Instrument.MasterInstrument.FormatPrice(z.BottomPrice, true);
			        string fmtVol = isCrypto
			            ? Globals.FormatCryptocurrencyQuantity(z.TotalVol, true)
			            : Globals.FormatQuantity((long)z.TotalVol, false);
			        bool   allClosed = z.Ticks.Values.All(v => v != -1);
			        string status    = allClosed ? "Closed" : "Active";
			        string side      = z.IsAskDom ? "Ask dominant" : "Bid dominant";

			        best = new Tuple<string, DateTime>(
			            $"Zone Top:    {fmtTop}\nZone Bottom: {fmtBot}\nTotal Vol:   {fmtVol}\n{side} | {status}",
			            DateTime.UtcNow);
			        break;
			    }
			}

            if (best != null)
            {
                if (!best.Equals(hoverText) && !forceRefreshArmed)
                {
                    hoverText = best;
                    forceRefreshArmed = true;
                    ForceRefresh();
                }
            }
            else if (hoverText != null)
            {
                hoverText = null;
                if (!forceRefreshArmed)
                {
                    forceRefreshArmed = true;
                    ForceRefresh();
                }
            }

            hoverTimer.IsEnabled = false;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (IsInHitTest)
                return;

            if (hoverTimer != null && !hoverTimer.IsEnabled)
                hoverTimer.IsEnabled = true;
        }
		
		#region StackedImbalanceTrap 

		private void SI_AggregateTick_BIP1(int primaryBarIdx, double price, double vol, bool isAskAggressor)
		{
		    if (primaryBarIdx < 0)
		        return;

		    int ticksPerBin   = Math.Max(1, SIAggregationIntervalTicks);
		    double binInterval = ticksPerBin * TickSize;
		
		    if (!siAskByBar.ContainsKey(primaryBarIdx)) siAskByBar[primaryBarIdx] = new Dictionary<double, double>(256);
		    if (!siBidByBar.ContainsKey(primaryBarIdx)) siBidByBar[primaryBarIdx] = new Dictionary<double, double>(256);
		    if (!siTotByBar.ContainsKey(primaryBarIdx)) siTotByBar[primaryBarIdx] = new Dictionary<double, double>(256);
		
		    double origin;
		    if (SIUseSessionOpenForAggregation && siSessionOpenPrice != 0.0)
		        origin = siSessionOpenPrice;
		    else
		        origin = BarsArray[0].GetLow(primaryBarIdx);
		
		    origin = Math.Round(origin / TickSize) * TickSize;
		
		    double binsFromOrigin = Math.Floor(((price - origin) / binInterval) + 1e-9);
		    double binStart       = origin + binsFromOrigin * binInterval;
		    binStart              = Math.Round(binStart / TickSize) * TickSize;
		
		    var askMap = siAskByBar[primaryBarIdx];
		    var bidMap = siBidByBar[primaryBarIdx];
		    var totMap = siTotByBar[primaryBarIdx];
		
		    double ask = 0, bid = 0, tot = 0;
		    askMap.TryGetValue(binStart, out ask);
		    bidMap.TryGetValue(binStart, out bid);
		
		    if (isAskAggressor) ask += vol;
		    else                bid += vol;
		
		    tot = ask + bid;
		
		    askMap[binStart] = ask;
		    bidMap[binStart] = bid;
		    totMap[binStart] = tot;
		}
		
		private void SI_MaybeAddEvent_BIP0(int barJustClosedIdx)
		{
		    if (barJustClosedIdx < 0)
		        return;
		
		    if (siEventBars.Contains(barJustClosedIdx))
		        return;
		
		    bool hasAsk, hasBid;
		    double askHigh, bidLow;
			SI_ComputeStackedImbalanceForBar(barJustClosedIdx, out hasAsk, out hasBid, out askHigh, out bidLow);

		    if (!hasAsk && !hasBid)
		        return;
		
		    siEventBars.Add(barJustClosedIdx);

		    double pxClose = BarsArray[0].GetClose(barJustClosedIdx);

			double pxAsk = (!double.IsNaN(askHigh) ? askHigh : pxClose);
			double pxBid = (!double.IsNaN(bidLow)  ? bidLow  : pxClose);

		    if (hasAsk)
		    {
		        siEvents.Add(new StackedImbalanceEvent
		        {
		            BarIdx   = barJustClosedIdx,
		            Price    = pxAsk,
		            Dir      = +1,
		            Resolved = false
		        });
		    }
		
		    if (hasBid)
		    {
		        siEvents.Add(new StackedImbalanceEvent
		        {
		            BarIdx   = barJustClosedIdx,
		            Price    = pxBid,
		            Dir      = -1,
		            Resolved = false
		        });
		    }
		
		    if (DebugPrints)
		        Print($"[OFT SI] bar={barJustClosedIdx} ask={hasAsk} bid={hasBid} close={pxClose:F2} askHigh={pxAsk:F2} bidLow={pxBid:F2}");
		}


		
		private void SI_ComputeStackedImbalanceForBar(
		    int barIdx,
		    out bool hasAskStack,
		    out bool hasBidStack,
		    out double askStackHigh,
		    out double bidStackLow)
		{
		    hasAskStack = false;
			hasBidStack = false;
			askStackHigh = double.NaN;
			bidStackLow  = double.NaN;

		    Dictionary<double, double> totRows;
		    if (siTotByBar == null || !siTotByBar.TryGetValue(barIdx, out totRows) || totRows == null || totRows.Count < 2)
		        return;
		
		    Dictionary<double, double> askRows = null, bidRows = null;
		    siAskByBar.TryGetValue(barIdx, out askRows);
		    siBidByBar.TryGetValue(barIdx, out bidRows);
		
		    double Read(Dictionary<double, double> map, double key)
		    {
		        if (map == null) return 0.0;
		        double v;
		        return map.TryGetValue(key, out v) ? v : 0.0;
		    }
		
		    double ReadTot(double key)
		    {
		        double v;
		        return totRows.TryGetValue(key, out v) ? v : 0.0;
		    }
		
		    var asc = totRows.Keys.OrderBy(k => k).ToList();
		    if (asc.Count < 2)
		        return;
		
		    int maxConsecutiveAsk = 0, currentAsk = 0;
		    for (int i = 1; i < asc.Count; i++)
		    {
		        double keyCurr = asc[i];
		        double keyPrev = asc[i - 1];
		
		        if (SIMinRowVolume > 0 && (ReadTot(keyCurr) < SIMinRowVolume || ReadTot(keyPrev) < SIMinRowVolume))
		        {
		            currentAsk = 0;
		            continue;
		        }
		
		        double askCurr = Read(askRows, keyCurr);
		        double bidPrev = Read(bidRows, keyPrev);
		
		        currentAsk = (askCurr >= SIImbFact * bidPrev) ? (currentAsk + 1) : 0;

			    if (currentAsk >= SIStackedImbalanceLookback)
			    {
			        hasAskStack = true;
			        if (double.IsNaN(askStackHigh) || keyCurr > askStackHigh)
			            askStackHigh = keyCurr;
			    }
			    if (currentAsk > maxConsecutiveAsk) maxConsecutiveAsk = currentAsk;
		    }
		
		    int maxConsecutiveBid = 0, currentBid = 0;
		    for (int i = 0; i < asc.Count - 1; i++)
		    {
		        double keyCurr = asc[i];
		        double keyNext = asc[i + 1];
		
		        if (SIMinRowVolume > 0 && (ReadTot(keyCurr) < SIMinRowVolume || ReadTot(keyNext) < SIMinRowVolume))
		        {
		            currentBid = 0;
		            continue;
		        }
		
		        double bidCurr = Read(bidRows, keyCurr);
		        double askNext = Read(askRows, keyNext);
		
		        currentBid = (bidCurr >= SIImbFact * askNext) ? (currentBid + 1) : 0;

			    if (currentBid >= SIStackedImbalanceLookback)
			    {
			        hasBidStack = true;
			        if (double.IsNaN(bidStackLow) || keyCurr < bidStackLow)
			            bidStackLow = keyCurr;
			    }
			    if (currentBid > maxConsecutiveBid) maxConsecutiveBid = currentBid;
		    }
		}

		
		private void SI_EvaluateTrap_BIP0(int barJustClosedIdx, double closeOfJustClosed)
		{
		    if (siEvents == null || siEvents.Count == 0)
		        return;
		
		    double move = SITrapMoveTicks * TickSize;
		
			int bestIdxShort = -1;
			int bestAgeShort = int.MaxValue;
			int bestIdxLong  = -1;
			int bestAgeLong  = int.MaxValue;

		    for (int i = siEvents.Count - 1; i >= 0; i--)
		    {
		        var e = siEvents[i];
		        if (e.Resolved)
		            continue;
		
		        int age = barJustClosedIdx - e.BarIdx;
		        if (age < 0)
		            continue;
		
		        if (age > SITrapLookaheadBars)
		            continue;
		
		        bool trappedAsk = (e.Dir > 0) && (closeOfJustClosed <= e.Price - move);
		        bool trappedBid = (e.Dir < 0) && (closeOfJustClosed >= e.Price + move);
		
		        if (trappedAsk && age < bestAgeShort)
			    {
			        bestAgeShort = age;
			        bestIdxShort = i;
			    }
			    if (trappedBid && age < bestAgeLong)
			    {
			        bestAgeLong = age;
			        bestIdxLong = i;
			    }
		    }
		
			if (bestIdxShort >= 0 || bestIdxLong >= 0)
			{
			    pendingSITrapBarIdx = barJustClosedIdx;
			
			    bool fireShort = (bestIdxShort >= 0);
			    bool fireLong  = (bestIdxLong  >= 0);
			
			    pendingSITrapSig = (fireShort && fireLong) ? 2
			                     : (fireLong)            ? +1
			                     :                        -1;
			
			    if (bestIdxShort >= 0)
			    {
			        var eS = siEvents[bestIdxShort];
			        eS.Resolved = true;
			        siEvents[bestIdxShort] = eS;
			
			        if (DebugPrints)
			            Print($"[OFT SI TRAP] SHORT baseBar={eS.BarIdx} basePx={eS.Price:F2} dir={eS.Dir} trappedOnBar={barJustClosedIdx} close={closeOfJustClosed:F2} sig=-1");
			    }
			
			    if (bestIdxLong >= 0 && bestIdxLong != bestIdxShort)
			    {
			        var eL = siEvents[bestIdxLong];
			        eL.Resolved = true;
			        siEvents[bestIdxLong] = eL;
			
			        if (DebugPrints)
			            Print($"[OFT SI TRAP] LONG  baseBar={eL.BarIdx} basePx={eL.Price:F2} dir={eL.Dir} trappedOnBar={barJustClosedIdx} close={closeOfJustClosed:F2} sig=+1");
			    }
			}

		    int oldestKeep = Math.Max(0, barJustClosedIdx - (SITrapLookaheadBars + 50));
		    if (siEvents.Count > 1024)
		    {
		        int removeCount = 0;
		        for (int i = 0; i < siEvents.Count; i++)
		        {
		            if (siEvents[i].BarIdx < oldestKeep)
		                removeCount++;
		            else
		                break;
		        }
		        if (removeCount > 0)
		            siEvents.RemoveRange(0, removeCount);
		    }
		
		    if (barJustClosedIdx % 50 == 0)
		        SI_PruneBarMaps(oldestKeep);
		}
		
		private void SI_PruneBarMaps(int oldestKeepBar)
		{
		    if (siAskByBar == null) return;
		
		    void prune<T>(Dictionary<int, T> map)
		    {
		        if (map == null || map.Count == 0) return;
		        var keys = map.Keys.Where(k => k < oldestKeepBar).ToList();
		        for (int i = 0; i < keys.Count; i++)
		            map.Remove(keys[i]);
		    }
		
		    prune(siAskByBar);
		    prune(siBidByBar);
		    prune(siTotByBar);
		
		    if (siEventBars != null && siEventBars.Count > 0)
		    {
		        var rm = siEventBars.Where(b => b < oldestKeepBar).ToList();
		        for (int i = 0; i < rm.Count; i++)
		            siEventBars.Remove(rm[i]);
		    }
		}
		
		#endregion
		
		#region Drawing Helpers
		
		private bool CanDrawSignalMarkers()
		{
		    return ChartControl != null && ChartPanel != null;
		}
		
		private SimpleFont GetSignalMarkerFont()
		{
		    if (signalMarkerFont == null || signalMarkerFontSizeCached != SignalFontSize)
		    {
		        signalMarkerFont = new SimpleFont("Arial", SignalFontSize);
		        signalMarkerFontSizeCached = SignalFontSize;
		    }
		    return signalMarkerFont;
		}
		
		private string MakeSigTag(string sigKey, string dirKey, int barsAgo)
		{
		    long ticks = Times[0][barsAgo].Ticks;
		    return "AOF_SIG_" + sigKey + "_" + dirKey + "_" + ticks.ToString();
		}
		
		private void RemoveSigTagsForBar(int barsAgo)
		{
		    RemoveDrawObject(MakeSigTag("TRP", "L", barsAgo));
		    RemoveDrawObject(MakeSigTag("TRP", "S", barsAgo));
			RemoveDrawObject(MakeSigTag("SITRP", "L", barsAgo));
			RemoveDrawObject(MakeSigTag("SITRP", "S", barsAgo));
		}
		
		private void DrawFlowSignalMarkers_BIP0(int barsAgo)
		{
		    if (!CanDrawSignalMarkers())
		        return;
		
		    if (CurrentBar < barsAgo)
		        return;
		
		    RemoveSigTagsForBar(barsAgo);
		
		    double tick = TickSize;
		    double baseOff = SignalOffsetTicks * tick;
		    double padOff  = SignalStackPaddingTicks * tick;
		    
		    int trp = (int)trappedTraders[barsAgo];
			int si  = (int)stackedImbTrap[barsAgo];
			
		    int shortIdx = 0;
		   
		    if (EnableTrappedTradersSignal && trp < 0 && !string.IsNullOrEmpty(TrappedTradersMarker))
		        DrawOneSigShort("TRP", TrappedTradersMarker, TrappedShortBrush, barsAgo, baseOff, padOff, shortIdx++);
			
			if (EnableSITrapSignal && (si == -1 || si == 2) && !string.IsNullOrEmpty(SITrapMarker))
			    DrawOneSigShort("SITRP", SITrapMarker, SITrapShortBrush, barsAgo, baseOff, padOff, shortIdx++);

		    int longIdx = 0;
		    
		    if (EnableTrappedTradersSignal && trp > 0 && !string.IsNullOrEmpty(TrappedTradersMarker))
		        DrawOneSigLong("TRP", TrappedTradersMarker, TrappedLongBrush, barsAgo, baseOff, padOff, longIdx++);
			
			if (EnableSITrapSignal && (si == 1 || si == 2) && !string.IsNullOrEmpty(SITrapMarker))
    			DrawOneSigLong("SITRP", SITrapMarker, SITrapLongBrush, barsAgo, baseOff, padOff, longIdx++);
		}
		
		private void DrawOneSigShort(string sigKey, string marker, System.Windows.Media.Brush brush, int barsAgo, double baseOff, double padOff, int stackIdx)
		{
		    if (brush == null)
		        brush = Brushes.White;
		
		    double y = High[barsAgo] + baseOff + (stackIdx * padOff);
		
		    Draw.Text(
		        this,
		        MakeSigTag(sigKey, "S", barsAgo),
		        false,
		        marker,
		        barsAgo,
		        y,
		        0,
		        brush,
		        GetSignalMarkerFont(),
		        System.Windows.TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}
		
		private void DrawOneSigLong(string sigKey, string marker, System.Windows.Media.Brush brush, int barsAgo, double baseOff, double padOff, int stackIdx)
		{
		    if (brush == null)
		        brush = Brushes.White;
		
		    double y = Low[barsAgo] - baseOff - (stackIdx * padOff);
		
		    Draw.Text(
		        this,
		        MakeSigTag(sigKey, "L", barsAgo),
		        false,
		        marker,
		        barsAgo,
		        y,
		        0,
		        brush,
		        GetSignalMarkerFont(),
		        System.Windows.TextAlignment.Center,
		        Brushes.Transparent,
		        Brushes.Transparent,
		        0);
		}
		
		#endregion
		
		#region Helpers
	
		
		private void MaybeRecordLargeTradeEvent(Tuple<int, double, int> key, double askVol, double bidVol, DateTime t)
		{
		    double tot = askVol + bidVol;

		    // ---- Zone: triggered purely by BigTradeZoneThreshold, independent of trap filters ----
		    MaybAddBigTradeZone(key, askVol, bidVol);

		    // ---- Trap: additional dominance / min-volume filters ----
		    if (tot < TrapMinTotalVolume)
		        return;
		
		    double dom = Math.Max(askVol, bidVol) / Math.Max(1.0, tot);
		    if (dom < (TrapMinDominancePct / 100.0))
		        return;
		
		    if (largeTradeEventKeys == null)
		        largeTradeEventKeys = new HashSet<Tuple<int, double, int>>();
		
		    if (largeTradeEvents == null)
		        largeTradeEvents = new List<LargeTradeEvent>(256);
		
		    if (largeTradeEventKeys.Contains(key))
		        return;
		
		    largeTradeEventKeys.Add(key);
		
		    bool isBuy = askVol >= bidVol;
		
		    largeTradeEvents.Add(new LargeTradeEvent
		    {
		        BarIdx   = key.Item1,
		        Price    = key.Item2,
		        IsBuy    = isBuy,
		        TotalVol = tot,
		        Time     = t,
		        Resolved = false
		    });
		}
		
		private void EvaluateTrappedTraders_BIP0(int barJustClosedIdx)
		{
		    if (barJustClosedIdx < 0 || largeTradeEvents == null || largeTradeEvents.Count == 0)
		        return;
		
		    double c = BarsArray[0].GetClose(barJustClosedIdx);
			
			if (EnableStackedImbalanceTrap)
			    SI_MaybeAddEvent_BIP0(barJustClosedIdx);

		    double move = TrapMoveTicks * TickSize;
		
		    int    bestIdx = -1;
		    double bestVol = double.MinValue;
		
		    for (int i = largeTradeEvents.Count - 1; i >= 0; i--)
		    {
		        var e = largeTradeEvents[i];
		        if (e.Resolved)
		            continue;
		
		        int age = barJustClosedIdx - e.BarIdx;
		        if (age < 0)
		            continue;
		
		        if (age > TrapLookaheadBars)
		            continue;
		
		        bool trappedBuy  = e.IsBuy  && (c <= e.Price - move);
		        bool trappedSell = !e.IsBuy && (c >= e.Price + move);
		
		        if (!(trappedBuy || trappedSell))
		            continue;
		
		        if (e.TotalVol > bestVol)
		        {
		            bestVol = e.TotalVol;
		            bestIdx = i;
		        }
		    }
		
		    if (bestIdx >= 0)
		    {
		        var e = largeTradeEvents[bestIdx];
		
		        pendingTrappedBarIdx = barJustClosedIdx;
		        pendingTrappedSig = e.IsBuy ? -1 : 1;
		
		        e.Resolved = true;
		        largeTradeEvents[bestIdx] = e;
		
		        if (DebugPrints)
		        {
		            Print(
		                $"[OFT TRAP] baseBar={e.BarIdx} basePx={e.Price:F2} " +
		                $"dir={(e.IsBuy ? "BUY" : "SELL")} vol={e.TotalVol:0} " +
		                $"trappedOnBar={barJustClosedIdx} close={c:F2} " +
		                $"sig={pendingTrappedSig}"
		            );
		        }
		    }
			
			if (pendingTrappedBarIdx != barJustClosedIdx && EnableStackedImbalanceTrap)
			    SI_EvaluateTrap_BIP0(barJustClosedIdx, c);

		    int oldestKeep = Math.Max(0, barJustClosedIdx - (TrapLookaheadBars + 20));
		
		    if (largeTradeEvents.Count > 512)
		    {
		        int removeCount = 0;
		        for (int i = 0; i < largeTradeEvents.Count; i++)
		        {
		            if (largeTradeEvents[i].BarIdx < oldestKeep)
		                removeCount++;
		            else
		                break;
		        }
		
		        if (removeCount > 0)
		            largeTradeEvents.RemoveRange(0, removeCount);
		    }
		}
		
		

		
		private void UpdateSpeedOfTape(double vol, bool isAskAggressor, DateTime t)
		{
			if (SpeedWindowMs <= 0)
    			return;
		
		    if (tapeWindow == null)
		        tapeWindow = new Queue<TapePrint>(2048);
		
		    tapeWindow.Enqueue(new TapePrint { Time = t, Vol = vol, IsAsk = isAskAggressor });
		
		    if (isAskAggressor) tapeAskSum += vol;
		    else                tapeBidSum += vol;
		
		    DateTime cutoff = t.AddMilliseconds(-SpeedWindowMs);
		
		    while (tapeWindow.Count > 0 && tapeWindow.Peek().Time < cutoff)
		    {
		        var p = tapeWindow.Dequeue();
		        if (p.IsAsk) tapeAskSum -= p.Vol;
		        else         tapeBidSum -= p.Vol;
		    }
		
		    double windowSeconds = Math.Max(0.001, SpeedWindowMs / 1000.0);
		    latestTapeSpeedTotal = (tapeAskSum + tapeBidSum) / windowSeconds;
			latestTapeNetSpeed = (tapeAskSum - tapeBidSum) / windowSeconds;
		
		    int idx0 = BarsArray[0].GetBar(t);
		    if (idx0 < 0)
		        return;
		
		    if (tapeSpeedPrimaryBarIdx != idx0)
		    {
		        tapeSpeedPrimaryBarIdx    = idx0;
		        latestTapeSpeedMaxThisBar = 0;
		    }
		
		    if (latestTapeSpeedTotal > latestTapeSpeedMaxThisBar)
		        latestTapeSpeedMaxThisBar = latestTapeSpeedTotal;
			
			if (tapeNetPrimaryBarIdx != idx0)
			{
			    tapeNetPrimaryBarIdx          = idx0;
			    latestTapeNetSpeedMaxThisBar  = 0;
			}
			
			if (Math.Abs(latestTapeNetSpeed) > Math.Abs(latestTapeNetSpeedMaxThisBar))
			    latestTapeNetSpeedMaxThisBar = latestTapeNetSpeed;
		}


		#endregion

    }    
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenOrderFlowToolsV0006[] cacheAlightenOrderFlowToolsV0006;
		public AlightenOrderFlowToolsV0006 AlightenOrderFlowToolsV0006(int barsToProcess, OrderFlowToolsLargeTradeFindByV0006 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0006 largeTradeSizeBy, bool hoverValues, bool enableBigTradeZones, int bigTradeZoneThreshold, int bigTradeZoneOpacity, int bigTradeZoneHeightTicks, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return AlightenOrderFlowToolsV0006(Input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, enableBigTradeZones, bigTradeZoneThreshold, bigTradeZoneOpacity, bigTradeZoneHeightTicks, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}

		public AlightenOrderFlowToolsV0006 AlightenOrderFlowToolsV0006(ISeries<double> input, int barsToProcess, OrderFlowToolsLargeTradeFindByV0006 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0006 largeTradeSizeBy, bool hoverValues, bool enableBigTradeZones, int bigTradeZoneThreshold, int bigTradeZoneOpacity, int bigTradeZoneHeightTicks, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			if (cacheAlightenOrderFlowToolsV0006 != null)
				for (int idx = 0; idx < cacheAlightenOrderFlowToolsV0006.Length; idx++)
					if (cacheAlightenOrderFlowToolsV0006[idx] != null && cacheAlightenOrderFlowToolsV0006[idx].BarsToProcess == barsToProcess && cacheAlightenOrderFlowToolsV0006[idx].LargeTradeFindBy == largeTradeFindBy && cacheAlightenOrderFlowToolsV0006[idx].EnableLargeTradeBubbles == enableLargeTradeBubbles && cacheAlightenOrderFlowToolsV0006[idx].MinimumVolumeForMarker == minimumVolumeForMarker && cacheAlightenOrderFlowToolsV0006[idx].MaximumMarkerSize == maximumMarkerSize && cacheAlightenOrderFlowToolsV0006[idx].LargeTradeSizeBy == largeTradeSizeBy && cacheAlightenOrderFlowToolsV0006[idx].HoverValues == hoverValues && cacheAlightenOrderFlowToolsV0006[idx].EnableBigTradeZones == enableBigTradeZones && cacheAlightenOrderFlowToolsV0006[idx].BigTradeZoneThreshold == bigTradeZoneThreshold && cacheAlightenOrderFlowToolsV0006[idx].BigTradeZoneOpacity == bigTradeZoneOpacity && cacheAlightenOrderFlowToolsV0006[idx].BigTradeZoneHeightTicks == bigTradeZoneHeightTicks && cacheAlightenOrderFlowToolsV0006[idx].SignalFontSize == signalFontSize && cacheAlightenOrderFlowToolsV0006[idx].SignalOffsetTicks == signalOffsetTicks && cacheAlightenOrderFlowToolsV0006[idx].SignalStackPaddingTicks == signalStackPaddingTicks && cacheAlightenOrderFlowToolsV0006[idx].TrapLookaheadBars == trapLookaheadBars && cacheAlightenOrderFlowToolsV0006[idx].TrapMoveTicks == trapMoveTicks && cacheAlightenOrderFlowToolsV0006[idx].TrapMinTotalVolume == trapMinTotalVolume && cacheAlightenOrderFlowToolsV0006[idx].TrapMinDominancePct == trapMinDominancePct && cacheAlightenOrderFlowToolsV0006[idx].SpeedWindowMs == speedWindowMs && cacheAlightenOrderFlowToolsV0006[idx].EnableTrappedTradersSignal == enableTrappedTradersSignal && cacheAlightenOrderFlowToolsV0006[idx].TrappedTradersMarker == trappedTradersMarker && cacheAlightenOrderFlowToolsV0006[idx].EnableSITrapSignal == enableSITrapSignal && cacheAlightenOrderFlowToolsV0006[idx].SITrapMarker == sITrapMarker && cacheAlightenOrderFlowToolsV0006[idx].DebugPrints == debugPrints && cacheAlightenOrderFlowToolsV0006[idx].DebugOnlyCurrentBar == debugOnlyCurrentBar && cacheAlightenOrderFlowToolsV0006[idx].DebugMinTotalVol == debugMinTotalVol && cacheAlightenOrderFlowToolsV0006[idx].EnableStackedImbalanceTrap == enableStackedImbalanceTrap && cacheAlightenOrderFlowToolsV0006[idx].SITrapLookaheadBars == sITrapLookaheadBars && cacheAlightenOrderFlowToolsV0006[idx].SITrapMoveTicks == sITrapMoveTicks && cacheAlightenOrderFlowToolsV0006[idx].SIAggregationIntervalTicks == sIAggregationIntervalTicks && cacheAlightenOrderFlowToolsV0006[idx].SIImbFact == sIImbFact && cacheAlightenOrderFlowToolsV0006[idx].SIStackedImbalanceLookback == sIStackedImbalanceLookback && cacheAlightenOrderFlowToolsV0006[idx].SIMinRowVolume == sIMinRowVolume && cacheAlightenOrderFlowToolsV0006[idx].SIUseSessionOpenForAggregation == sIUseSessionOpenForAggregation && cacheAlightenOrderFlowToolsV0006[idx].EqualsInput(input))
						return cacheAlightenOrderFlowToolsV0006[idx];
			return CacheIndicator<AlightenOrderFlowToolsV0006>(new AlightenOrderFlowToolsV0006(){ BarsToProcess = barsToProcess, LargeTradeFindBy = largeTradeFindBy, EnableLargeTradeBubbles = enableLargeTradeBubbles, MinimumVolumeForMarker = minimumVolumeForMarker, MaximumMarkerSize = maximumMarkerSize, LargeTradeSizeBy = largeTradeSizeBy, HoverValues = hoverValues, EnableBigTradeZones = enableBigTradeZones, BigTradeZoneThreshold = bigTradeZoneThreshold, BigTradeZoneOpacity = bigTradeZoneOpacity, BigTradeZoneHeightTicks = bigTradeZoneHeightTicks, SignalFontSize = signalFontSize, SignalOffsetTicks = signalOffsetTicks, SignalStackPaddingTicks = signalStackPaddingTicks, TrapLookaheadBars = trapLookaheadBars, TrapMoveTicks = trapMoveTicks, TrapMinTotalVolume = trapMinTotalVolume, TrapMinDominancePct = trapMinDominancePct, SpeedWindowMs = speedWindowMs, EnableTrappedTradersSignal = enableTrappedTradersSignal, TrappedTradersMarker = trappedTradersMarker, EnableSITrapSignal = enableSITrapSignal, SITrapMarker = sITrapMarker, DebugPrints = debugPrints, DebugOnlyCurrentBar = debugOnlyCurrentBar, DebugMinTotalVol = debugMinTotalVol, EnableStackedImbalanceTrap = enableStackedImbalanceTrap, SITrapLookaheadBars = sITrapLookaheadBars, SITrapMoveTicks = sITrapMoveTicks, SIAggregationIntervalTicks = sIAggregationIntervalTicks, SIImbFact = sIImbFact, SIStackedImbalanceLookback = sIStackedImbalanceLookback, SIMinRowVolume = sIMinRowVolume, SIUseSessionOpenForAggregation = sIUseSessionOpenForAggregation }, input, ref cacheAlightenOrderFlowToolsV0006);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenOrderFlowToolsV0006 AlightenOrderFlowToolsV0006(int barsToProcess, OrderFlowToolsLargeTradeFindByV0006 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0006 largeTradeSizeBy, bool hoverValues, bool enableBigTradeZones, int bigTradeZoneThreshold, int bigTradeZoneOpacity, int bigTradeZoneHeightTicks, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return indicator.AlightenOrderFlowToolsV0006(Input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, enableBigTradeZones, bigTradeZoneThreshold, bigTradeZoneOpacity, bigTradeZoneHeightTicks, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}

		public Indicators.AlightenOrderFlowToolsV0006 AlightenOrderFlowToolsV0006(ISeries<double> input , int barsToProcess, OrderFlowToolsLargeTradeFindByV0006 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0006 largeTradeSizeBy, bool hoverValues, bool enableBigTradeZones, int bigTradeZoneThreshold, int bigTradeZoneOpacity, int bigTradeZoneHeightTicks, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return indicator.AlightenOrderFlowToolsV0006(input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, enableBigTradeZones, bigTradeZoneThreshold, bigTradeZoneOpacity, bigTradeZoneHeightTicks, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenOrderFlowToolsV0006 AlightenOrderFlowToolsV0006(int barsToProcess, OrderFlowToolsLargeTradeFindByV0006 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0006 largeTradeSizeBy, bool hoverValues, bool enableBigTradeZones, int bigTradeZoneThreshold, int bigTradeZoneOpacity, int bigTradeZoneHeightTicks, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return indicator.AlightenOrderFlowToolsV0006(Input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, enableBigTradeZones, bigTradeZoneThreshold, bigTradeZoneOpacity, bigTradeZoneHeightTicks, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}

		public Indicators.AlightenOrderFlowToolsV0006 AlightenOrderFlowToolsV0006(ISeries<double> input , int barsToProcess, OrderFlowToolsLargeTradeFindByV0006 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0006 largeTradeSizeBy, bool hoverValues, bool enableBigTradeZones, int bigTradeZoneThreshold, int bigTradeZoneOpacity, int bigTradeZoneHeightTicks, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return indicator.AlightenOrderFlowToolsV0006(input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, enableBigTradeZones, bigTradeZoneThreshold, bigTradeZoneOpacity, bigTradeZoneHeightTicks, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}
	}
}

#endregion

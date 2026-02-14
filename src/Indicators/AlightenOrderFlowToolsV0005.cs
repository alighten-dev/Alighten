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
	
	public enum OrderFlowToolsLargeTradeFindByV0005
    {
        BidAsk,
        Price,
        Block
    }
    public enum OrderFlowToolsLargeTradeSizeByV0005
    {
        VisibleArea,
        Session
    }
    public class AlightenOrderFlowToolsV0005 : Indicator
    {
		[NinjaScriptProperty]
		[Range(0, 200000)]
		[Display(Name = "BarsToProcess", GroupName = "Performance", Order = 0)]
		public int BarsToProcess { get; set; } = 500;
		

		#region Large Trades
        // -------------------- Large Trades (same surface shape) --------------------
        [Display(Name = "BaseLargeVolumeOn", GroupName = "Large Trades", Order = 0)]
        [NinjaScriptProperty]
        public OrderFlowToolsLargeTradeFindByV0005 LargeTradeFindBy { get; set; }
		
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
        public OrderFlowToolsLargeTradeSizeByV0005 LargeTradeSizeBy { get; set; }

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

//		[NinjaScriptProperty]
//		[Display(Name = "Enable Trapped Traders", GroupName = "Trapped Traders", Order = 0)]
//		public bool EnableTrappedTraders { get; set; } = true;
		
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

//		[NinjaScriptProperty]
//		[Display(Name = "Enable Speed of Tape", GroupName = "Speed of Tape", Order = 0)]
//		public bool EnableSpeedOfTape { get; set; } = true;
		
		[NinjaScriptProperty]
		[Range(50, 10000)]
		[Display(Name = "Speed Window (ms)", GroupName = "Speed of Tape", Order = 10)]
		public int SpeedWindowMs { get; set; } = 1000;
		
		#endregion
		
		#region Signals
		// -------------------- Trapped Traders --------------------
		[NinjaScriptProperty]
		[Display(Name = "Enable Trapped Traders Signal", GroupName = "Signals - Trapped", Order = 0)]
		public bool EnableTrappedTradersSignal { get; set; } = true;
		
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
		public bool EnableSITrapSignal { get; set; } = true;
		
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
		public double SIImbFact { get; set; } = 3.0;
		
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
		
		// --------------------- Stacked Imbalance Trapped Traders --------------------------
		//public int TrapMinDominancePct { get; set; } = 70;

		
		// -------------------- Flow debug / per-bar summaries --------------------
		private int flowCurBarIdx = -1;
		
		private FlowBarStats flowCur;
		private FlowBarStats flowPendingResolve; // last completed bar awaiting next bar close
		
		private class FlowBarStats
		{
		    public int      BarIdx;
		    public DateTime BarTime;
		
		    // OHLC (captured at finalize)
		    public double O, H, L, C;
		
		    // Micro tape totals (sum of aggressed volume)
		    public double TotAskAgg;
		    public double TotBidAgg;
		
		    // Extremes seen from tradePx (optional but useful)
		    public double MinTradePx = double.MaxValue;
		    public double MaxTradePx = double.MinValue;
		
		    // Candidates
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


		
		// -------------------- Trapped Traders (large trade -> adverse move) --------------------
		private int pendingTrappedBarIdx = -1;
		private int pendingTrappedSig    = 0; // +1 trapped sellers (bullish), -1 trapped buyers (bearish)
		
		// -------------------- SI Trap plot publish (separate from TrappedTraders) --------------------
		private int pendingSITrapBarIdx = -1;
		
		// Encoding for SI trap plot:
		//  0 = none
		// +1 = bullish SI trap (bid-stack trapped / sellers trapped)
		// -1 = bearish SI trap (ask-stack trapped / buyers trapped)
		//  2 = both directions trapped on the same bar
		private int pendingSITrapSig    = 0;


		
		private class LargeTradeEvent
		{
		    public int      BarIdx;
		    public double   Price;
		    public bool     IsBuy;     // true = ask-dominant (buy aggressor), false = bid-dominant (sell aggressor)
		    public double   TotalVol;
		    public DateTime Time;
		    public bool     Resolved;  // set once we emit a trapped signal
		}
		
		// -------------------- Stacked Imbalance Trap (stacked imbalance -> adverse move) --------------------
		private class StackedImbalanceEvent
		{
		    public int    BarIdx;
		    public double Price;     // anchor (default: bar close)
		    public int    Dir;       // +1 ask-stack, -1 bid-stack
		    public bool   Resolved;
		}
		
		// Per primary bar: binStart -> volume
		private Dictionary<int, Dictionary<double, double>> siAskByBar;
		private Dictionary<int, Dictionary<double, double>> siBidByBar;
		private Dictionary<int, Dictionary<double, double>> siTotByBar;
		
		// Track which bars already created an SI event
		private HashSet<int> siEventBars;
		private List<StackedImbalanceEvent> siEvents;
		
		// Session anchor
		private double siSessionOpenPrice;

		
		// -------------------- Speed of Tape (rolling time window over BIP1 ticks) --------------------
		private struct TapePrint
		{
		    public DateTime Time;
		    public double   Vol;
		    public bool     IsAsk; // true=ask aggressor (buy), false=bid aggressor (sell)
		}
		
		private Queue<TapePrint> tapeWindow;
		private double tapeAskSum;
		private double tapeBidSum;
		private double latestTapeSpeedTotal; // contracts/sec
		private int    tapeSpeedPrimaryBarIdx = -1;
		private double latestTapeSpeedMaxThisBar = 0; // max contracts/sec seen so far for the CURRENT primary bar
		private double latestTapeNetSpeed;
		private int tapeNetPrimaryBarIdx = -1;
		private double latestTapeNetSpeedMaxThisBar = 0;
		
		// Events keyed by bubble key so we don't double-insert while the rolling cluster updates
		private HashSet<Tuple<int, double, int>> largeTradeEventKeys;
		private List<LargeTradeEvent>            largeTradeEvents;
		

		// -------------------- signal marker rendering --------------------
		private SimpleFont signalMarkerFont;
		private int        signalMarkerFontSizeCached = -1;
		
		// -------------------- performance --------------------------------
//		private int barsToProcessCutoffBar = 0;   // bars < this index are skipped
//		private int barsToProcessLastTotal = -1;


		
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
		
		#endregion

        // -------------------- Lifecycle --------------------
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Order-flow trade detector (clean-room implementation).";
                Name                     = "AlightenOrderFlowToolsV0005";
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

                LargeTradeFindBy       = OrderFlowToolsLargeTradeFindByV0005.BidAsk;
                LargeTradeSizeBy       = OrderFlowToolsLargeTradeSizeByV0005.VisibleArea;
                MaximumMarkerSize      = 50;
                MinimumVolumeForMarker = 10;

                AskBrush     = Brushes.DarkCyan;
                BidBrush     = Brushes.Crimson;
                OutlineBrush = Brushes.DarkGray;
                FillOpacity  = 50;

                HoverValues = true;
				
				
				AddPlot(Brushes.Transparent, "TrappedTraders");
				AddPlot(Brushes.Transparent, "StackedImbTrap");
				AddPlot(Brushes.Transparent, "SpeedOfTape");
				AddPlot(Brushes.Transparent, "MaxSpeedOfTape");
				AddPlot(Brushes.Transparent, "NetSpeedOfTape");
				AddPlot(Brushes.Transparent, "MaxNetSpeedOfTape");


                return;
            }

            if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);

                if (HoverValues)
                {
                    // Decomp used private WPF resources; we use sane defaults.
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
				
				// -------------------- SI init --------------------
				siAskByBar   = new Dictionary<int, Dictionary<double, double>>(2048);
				siBidByBar   = new Dictionary<int, Dictionary<double, double>>(2048);
				siTotByBar   = new Dictionary<int, Dictionary<double, double>>(2048);
				
				siEventBars  = new HashSet<int>();
				siEvents     = new List<StackedImbalanceEvent>(512);
				
				siSessionOpenPrice = 0.0;

				
				
				trappedTraders          = Values[0];
				stackedImbTrap          = Values[1];
				speedOfTape             = Values[2];
				maxSpeedOfTape          = Values[3];
				netSpeedOfTape          = Values[4];
				maxNetSpeedOfTape       = Values[5];


				
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
				


		        return;
		    }
		
		    // Always keep the tape plots current (these update from BIP1)
			speedOfTape[0]        = latestTapeSpeedTotal;
			maxSpeedOfTape[0]     = latestTapeSpeedMaxThisBar;
			netSpeedOfTape[0]     = latestTapeNetSpeed;
			maxNetSpeedOfTape[0]  = latestTapeNetSpeedMaxThisBar;
			
			// Seed signal plots for THIS bar (MA-safe)
			
			trappedTraders[0]       = 0;
			stackedImbTrap[0]       = 0;
			
			// Only do publish/evaluate/draw once per bar (first tick)
			if (!IsFirstTickOfBar)
			    return;
			
			// Update cutoff once per bar (cheap, cached)
//			if (IsFirstTickOfBar)
//			    UpdateBarsToProcessCutoff_BIP0();
			
			// Preserve SI session-open anchor even if BarsToProcess skips early bars
			if (IsFirstTickOfBar && BarsArray[0].IsFirstBarOfSession)
			    siSessionOpenPrice = Open[0];

			
			
			// -------------------------------------------------
			// Evaluate trapped traders for the bar that just closed
			// -------------------------------------------------
			EvaluateTrappedTraders_BIP0(CurrentBars[0] - 1);
			
			if (pendingTrappedBarIdx == CurrentBars[0] - 1)
			{
			    trappedTraders[1] = pendingTrappedSig;
			}
			if (pendingSITrapBarIdx == CurrentBars[0] - 1)
			{
			    stackedImbTrap[1] = pendingSITrapSig;
			}
			
			// Draw markers for the prior bar
			DrawFlowSignalMarkers_BIP0(1);


		
		}


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


            if (LargeTradeFindBy == OrderFlowToolsLargeTradeFindByV0005.BidAsk)
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
				
				
				// Call Speed of Tape
				UpdateSpeedOfTape(vol, isAskAggressor, t);
				// End call Speed of Tape
				
				// Debug section 
				if (DebugPrints && DebugMinTotalVol > 0)
				{
				    double dbgTot = rollingAskVol + rollingBidVol;
				
				    if (dbgTot >= DebugMinTotalVol && dbgTot < MinimumVolumeForMarker)
				    {
				        var dbgKey = MakeUniqueKey(primaryBarIdx, close, 0);
				        string lk  = lastKey == null ? "null" : $"{lastKey.Item1}/{lastKey.Item2:F2}/{lastKey.Item3}";
				
//				        Print(
//						    $"[OFT DBG][MICRO] tradeT={t:HH:mm:ss.fff} barIdx={primaryBarIdx} " +
//						    $"px={close:F2} askV={rollingAskVol:0} bidV={rollingBidVol:0} tot={dbgTot:0} " +
//						    $"isAskAgg={isAskAggressor} mustStage={mustStage} lastKey={lk}"
//						);
				    }
				}
				// End debug section


                if (rollingAskVol + rollingBidVol >= MinimumVolumeForMarker)
				{
				    var key = MakeUniqueKey(primaryBarIdx, close, 0);
				    UpsertOrb(key, rollingAskVol, rollingBidVol, t, sessionId, mustStage, replaceLastKey: true);
					MaybeRecordLargeTradeEvent(key, rollingAskVol, rollingBidVol, t);
				    TrackSessionMax(rollingAskVol + rollingBidVol);
				}
            }
            else if (LargeTradeFindBy == OrderFlowToolsLargeTradeFindByV0005.Price)
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
		        // Debug section
				if (DebugPrints)
				{
				    if (replaceLastKey && lastKey != null)
				    {
				        bool wasCommitted = committed.ContainsKey(lastKey);
				        bool wasStaged    = staged.ContainsKey(lastKey);
				
//				        Print(
//				            $"[OFT DBG][UPSERT REPLACE] time={time:HH:mm:ss.fff} " +
//				            $"oldKey=(bar={lastKey.Item1}, px={lastKey.Item2:F2}) " +
//				            $"newKey=(bar={key.Item1}, px={key.Item2:F2}) " +
//				            $"oldState={(wasCommitted ? "COMMITTED" : (wasStaged ? "STAGED" : "MISSING"))} " +
//				            $"newState={(mustStage ? "STAGED" : "COMMITTED")} " +
//				            $"askVol={askVol:0} bidVol={bidVol:0}"
//				        );
				    }
				    else
				    {
//				        Print(
//				            $"[OFT DBG][UPSERT ADD] time={time:HH:mm:ss.fff} " +
//				            $"key=(bar={key.Item1}, px={key.Item2:F2}) " +
//				            $"newState={(mustStage ? "STAGED" : "COMMITTED")} " +
//				            $"askVol={askVol:0} bidVol={bidVol:0}"
//				        );
				    }
				}
				// End debug section

		
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
            if (LargeTradeSizeBy != OrderFlowToolsLargeTradeSizeByV0005.Session)
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
			
			// -------------------- Large Trade Bubble draw gate --------------------
			if (!EnableLargeTradeBubbles)
			{
			    // If hover was previously enabled, detach when bubbles are disabled
			    if (HoverValues && panelIndex != int.MinValue && chartControl.ChartPanels.Count > panelIndex && chartControl.ChartPanels[panelIndex] != null)
			        chartControl.ChartPanels[panelIndex].MouseMove -= OnMouseMove;
			
			    panelIndex = int.MinValue;
			    hoverText  = null;
			    return; // skip CommitStaged + bubble render loop
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

            if (LargeTradeSizeBy == OrderFlowToolsLargeTradeSizeByV0005.VisibleArea)
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

                if (LargeTradeSizeBy == OrderFlowToolsLargeTradeSizeByV0005.Session && maxVolBySession != null && maxVolBySession.TryGetValue(value.Item4, out var sv))
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
			
			// If bubbles are disabled, hover is meaningless and can be expensive.
			if (!EnableLargeTradeBubbles)
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
		
//		    // Optional BarsToProcess cutoff (match your other logic patterns)
//		    if (BarsToProcess > 0)
//		    {
//		        int totalLoaded = BarsArray[0].Count;
//		        int cutoffBar   = totalLoaded - BarsToProcess;
//		        if (cutoffBar > 0 && primaryBarIdx < cutoffBar)
//		            return;
//		    }
		
		    int ticksPerBin   = Math.Max(1, SIAggregationIntervalTicks);
		    double binInterval = ticksPerBin * TickSize;
		
		    // Ensure dictionaries exist for this bar
		    if (!siAskByBar.ContainsKey(primaryBarIdx)) siAskByBar[primaryBarIdx] = new Dictionary<double, double>(256);
		    if (!siBidByBar.ContainsKey(primaryBarIdx)) siBidByBar[primaryBarIdx] = new Dictionary<double, double>(256);
		    if (!siTotByBar.ContainsKey(primaryBarIdx)) siTotByBar[primaryBarIdx] = new Dictionary<double, double>(256);
		
		    // Anchor grid: session open OR bar low
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
		
		    // NOTE: you can still keep siEventBars to avoid double-processing the bar.
		    // But since a bar can have BOTH directions, we only use it to guard
		    // the diamond drawing, not to prevent adding 2 events.
		    if (siEventBars.Contains(barJustClosedIdx))
		        return;
		
		    bool hasAsk, hasBid;
		    double askHigh, bidLow;
			SI_ComputeStackedImbalanceForBar(barJustClosedIdx, out hasAsk, out hasBid, out askHigh, out bidLow);

		    if (!hasAsk && !hasBid)
		        return;
		
		    siEventBars.Add(barJustClosedIdx);

		
		    double pxClose = BarsArray[0].GetClose(barJustClosedIdx);

			// Fallbacks (shouldn't happen often, but keeps you safe)
			double pxAsk = (!double.IsNaN(askHigh) ? askHigh : pxClose);
			double pxBid = (!double.IsNaN(bidLow)  ? bidLow  : pxClose);

		
		    // Add one or two SI events (one per direction)
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

		
		// Fallback trap evaluation when large-trade did not fire.
		// Uses CLOSE of barJustClosed vs SI event anchor price.
		private void SI_EvaluateTrap_BIP0(int barJustClosedIdx, double closeOfJustClosed)
		{
		    if (siEvents == null || siEvents.Count == 0)
		        return;
		
		    double move = SITrapMoveTicks * TickSize;
		
		    // Track qualifying traps independently so a bar can fire BOTH directions.
			int bestIdxShort = -1; // ask-stack trapped => bearish (-1)
			int bestAgeShort = int.MaxValue;
			int bestIdxLong  = -1; // bid-stack trapped => bullish (+1)
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
		
		        // Dir +1 ask-stack (buyers aggressive) => trap is move DOWN => signal -1
		        // Dir -1 bid-stack (sellers aggressive) => trap is move UP => signal +1
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
		
		    // Resolve + publish (can be one or both)
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
			
			    // If both directions selected the same event index (shouldn't happen because dir differs)
			    if (bestIdxLong >= 0 && bestIdxLong != bestIdxShort)
			    {
			        var eL = siEvents[bestIdxLong];
			        eL.Resolved = true;
			        siEvents[bestIdxLong] = eL;
			
			        if (DebugPrints)
			            Print($"[OFT SI TRAP] LONG  baseBar={eL.BarIdx} basePx={eL.Price:F2} dir={eL.Dir} trappedOnBar={barJustClosedIdx} close={closeOfJustClosed:F2} sig=+1");
			    }
			}

		
		    // Cleanup (prevent unbounded growth)
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
		
		    // Also prune per-bar dictionaries occasionally
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
		
		// -------------------- Flow signal marker drawing (BIP0) --------------------
		private bool CanDrawSignalMarkers()
		{
		    // Market Analyzer / non-chart contexts will have ChartControl == null
		    return ChartControl != null && ChartPanel != null;
		}
		
		private SimpleFont GetSignalMarkerFont()
		{
		    if (signalMarkerFont == null || signalMarkerFontSizeCached != SignalFontSize)
		    {
		        // One shared font size across all signals
		        signalMarkerFont = new SimpleFont("Arial", SignalFontSize);
		        signalMarkerFontSizeCached = SignalFontSize;
		    }
		    return signalMarkerFont;
		}
		
		private string MakeSigTag(string sigKey, string dirKey, int barsAgo)
		{
		    // Deterministic tag per bar so we can remove/re-draw if user changes settings
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
		
		    // Always clear existing markers for this bar so toggles/strings/colors update cleanly.
		    RemoveSigTagsForBar(barsAgo);
		
		    double tick = TickSize;
		    double baseOff = SignalOffsetTicks * tick;
		    double padOff  = SignalStackPaddingTicks * tick;
		
		    // Convention: +1 => long (below bar), -1 => short (above bar)
		    
		    int trp = (int)trappedTraders[barsAgo];
			
			int si  = (int)stackedImbTrap[barsAgo];
			
		    // ---- SHORT stack (above high) ----
		    int shortIdx = 0;
		   
		    if (EnableTrappedTradersSignal && trp < 0 && !string.IsNullOrEmpty(TrappedTradersMarker))
		        DrawOneSigShort("TRP", TrappedTradersMarker, TrappedShortBrush, barsAgo, baseOff, padOff, shortIdx++);
			
			// SI trap can be -1 (short), +1 (long), or 2 (both)
			if (EnableSITrapSignal && (si == -1 || si == 2) && !string.IsNullOrEmpty(SITrapMarker))
			    DrawOneSigShort("SITRP", SITrapMarker, SITrapShortBrush, barsAgo, baseOff, padOff, shortIdx++);

		
		    // ---- LONG stack (below low) ----
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
		    if (tot < TrapMinTotalVolume)
		        return;
		
		    double dom = Math.Max(askVol, bidVol) / Math.Max(1.0, tot); // 0..1
		    if (dom < (TrapMinDominancePct / 100.0))
		        return;
		
		    if (largeTradeEventKeys == null)
		        largeTradeEventKeys = new HashSet<Tuple<int, double, int>>();
		
		    if (largeTradeEvents == null)
		        largeTradeEvents = new List<LargeTradeEvent>(256);
		
		    // Only record once per bubble key
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
		
		    // Current close of the bar that just closed
		    double c = BarsArray[0].GetClose(barJustClosedIdx);
			
//			// Update session open anchor for SI
//			if (BarsArray[0].IsFirstBarOfSession)
//			    siSessionOpenPrice = BarsArray[0].GetOpen(barJustClosedIdx);
			
			// Create SI event for this just-closed bar (if stacked imbalance fired)
			if (EnableStackedImbalanceTrap)
			    SI_MaybeAddEvent_BIP0(barJustClosedIdx);

		
		    double move = TrapMoveTicks * TickSize;
		
		    // Find the most recent (or highest volume) unresolved event inside lookahead window
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
		
		        // Prefer larger trades if multiple qualify
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
		
		        // +1 trapped sellers (bullish), -1 trapped buyers (bearish)
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
			
			// If large-trade did not produce a signal, try SI trap
			if (pendingTrappedBarIdx != barJustClosedIdx && EnableStackedImbalanceTrap)
			{
			    SI_EvaluateTrap_BIP0(barJustClosedIdx, c);
			}

		
		    // Light cleanup: drop old resolved events to prevent unbounded growth
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
		
		    // ------------------------------
		    // Rolling speed calculation
		    // ------------------------------
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
			
		
		    // ------------------------------
		    // MAX speed tracking (per primary bar, intrabar)
		    // ------------------------------
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
			
			// ------------------------------
			// MAX NET speed tracking (per primary bar, intrabar)
			// ------------------------------
			if (tapeNetPrimaryBarIdx != idx0)
			{
			    tapeNetPrimaryBarIdx          = idx0;
			    latestTapeNetSpeedMaxThisBar  = 0;
			}
			
			// Keep the signed net value that had the largest absolute magnitude so far this bar
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
		private AlightenOrderFlowToolsV0005[] cacheAlightenOrderFlowToolsV0005;
		public AlightenOrderFlowToolsV0005 AlightenOrderFlowToolsV0005(int barsToProcess, OrderFlowToolsLargeTradeFindByV0005 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0005 largeTradeSizeBy, bool hoverValues, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return AlightenOrderFlowToolsV0005(Input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}

		public AlightenOrderFlowToolsV0005 AlightenOrderFlowToolsV0005(ISeries<double> input, int barsToProcess, OrderFlowToolsLargeTradeFindByV0005 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0005 largeTradeSizeBy, bool hoverValues, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			if (cacheAlightenOrderFlowToolsV0005 != null)
				for (int idx = 0; idx < cacheAlightenOrderFlowToolsV0005.Length; idx++)
					if (cacheAlightenOrderFlowToolsV0005[idx] != null && cacheAlightenOrderFlowToolsV0005[idx].BarsToProcess == barsToProcess && cacheAlightenOrderFlowToolsV0005[idx].LargeTradeFindBy == largeTradeFindBy && cacheAlightenOrderFlowToolsV0005[idx].EnableLargeTradeBubbles == enableLargeTradeBubbles && cacheAlightenOrderFlowToolsV0005[idx].MinimumVolumeForMarker == minimumVolumeForMarker && cacheAlightenOrderFlowToolsV0005[idx].MaximumMarkerSize == maximumMarkerSize && cacheAlightenOrderFlowToolsV0005[idx].LargeTradeSizeBy == largeTradeSizeBy && cacheAlightenOrderFlowToolsV0005[idx].HoverValues == hoverValues && cacheAlightenOrderFlowToolsV0005[idx].SignalFontSize == signalFontSize && cacheAlightenOrderFlowToolsV0005[idx].SignalOffsetTicks == signalOffsetTicks && cacheAlightenOrderFlowToolsV0005[idx].SignalStackPaddingTicks == signalStackPaddingTicks && cacheAlightenOrderFlowToolsV0005[idx].TrapLookaheadBars == trapLookaheadBars && cacheAlightenOrderFlowToolsV0005[idx].TrapMoveTicks == trapMoveTicks && cacheAlightenOrderFlowToolsV0005[idx].TrapMinTotalVolume == trapMinTotalVolume && cacheAlightenOrderFlowToolsV0005[idx].TrapMinDominancePct == trapMinDominancePct && cacheAlightenOrderFlowToolsV0005[idx].SpeedWindowMs == speedWindowMs && cacheAlightenOrderFlowToolsV0005[idx].EnableTrappedTradersSignal == enableTrappedTradersSignal && cacheAlightenOrderFlowToolsV0005[idx].TrappedTradersMarker == trappedTradersMarker && cacheAlightenOrderFlowToolsV0005[idx].EnableSITrapSignal == enableSITrapSignal && cacheAlightenOrderFlowToolsV0005[idx].SITrapMarker == sITrapMarker && cacheAlightenOrderFlowToolsV0005[idx].DebugPrints == debugPrints && cacheAlightenOrderFlowToolsV0005[idx].DebugOnlyCurrentBar == debugOnlyCurrentBar && cacheAlightenOrderFlowToolsV0005[idx].DebugMinTotalVol == debugMinTotalVol && cacheAlightenOrderFlowToolsV0005[idx].EnableStackedImbalanceTrap == enableStackedImbalanceTrap && cacheAlightenOrderFlowToolsV0005[idx].SITrapLookaheadBars == sITrapLookaheadBars && cacheAlightenOrderFlowToolsV0005[idx].SITrapMoveTicks == sITrapMoveTicks && cacheAlightenOrderFlowToolsV0005[idx].SIAggregationIntervalTicks == sIAggregationIntervalTicks && cacheAlightenOrderFlowToolsV0005[idx].SIImbFact == sIImbFact && cacheAlightenOrderFlowToolsV0005[idx].SIStackedImbalanceLookback == sIStackedImbalanceLookback && cacheAlightenOrderFlowToolsV0005[idx].SIMinRowVolume == sIMinRowVolume && cacheAlightenOrderFlowToolsV0005[idx].SIUseSessionOpenForAggregation == sIUseSessionOpenForAggregation && cacheAlightenOrderFlowToolsV0005[idx].EqualsInput(input))
						return cacheAlightenOrderFlowToolsV0005[idx];
			return CacheIndicator<AlightenOrderFlowToolsV0005>(new AlightenOrderFlowToolsV0005(){ BarsToProcess = barsToProcess, LargeTradeFindBy = largeTradeFindBy, EnableLargeTradeBubbles = enableLargeTradeBubbles, MinimumVolumeForMarker = minimumVolumeForMarker, MaximumMarkerSize = maximumMarkerSize, LargeTradeSizeBy = largeTradeSizeBy, HoverValues = hoverValues, SignalFontSize = signalFontSize, SignalOffsetTicks = signalOffsetTicks, SignalStackPaddingTicks = signalStackPaddingTicks, TrapLookaheadBars = trapLookaheadBars, TrapMoveTicks = trapMoveTicks, TrapMinTotalVolume = trapMinTotalVolume, TrapMinDominancePct = trapMinDominancePct, SpeedWindowMs = speedWindowMs, EnableTrappedTradersSignal = enableTrappedTradersSignal, TrappedTradersMarker = trappedTradersMarker, EnableSITrapSignal = enableSITrapSignal, SITrapMarker = sITrapMarker, DebugPrints = debugPrints, DebugOnlyCurrentBar = debugOnlyCurrentBar, DebugMinTotalVol = debugMinTotalVol, EnableStackedImbalanceTrap = enableStackedImbalanceTrap, SITrapLookaheadBars = sITrapLookaheadBars, SITrapMoveTicks = sITrapMoveTicks, SIAggregationIntervalTicks = sIAggregationIntervalTicks, SIImbFact = sIImbFact, SIStackedImbalanceLookback = sIStackedImbalanceLookback, SIMinRowVolume = sIMinRowVolume, SIUseSessionOpenForAggregation = sIUseSessionOpenForAggregation }, input, ref cacheAlightenOrderFlowToolsV0005);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenOrderFlowToolsV0005 AlightenOrderFlowToolsV0005(int barsToProcess, OrderFlowToolsLargeTradeFindByV0005 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0005 largeTradeSizeBy, bool hoverValues, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return indicator.AlightenOrderFlowToolsV0005(Input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}

		public Indicators.AlightenOrderFlowToolsV0005 AlightenOrderFlowToolsV0005(ISeries<double> input , int barsToProcess, OrderFlowToolsLargeTradeFindByV0005 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0005 largeTradeSizeBy, bool hoverValues, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return indicator.AlightenOrderFlowToolsV0005(input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenOrderFlowToolsV0005 AlightenOrderFlowToolsV0005(int barsToProcess, OrderFlowToolsLargeTradeFindByV0005 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0005 largeTradeSizeBy, bool hoverValues, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return indicator.AlightenOrderFlowToolsV0005(Input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}

		public Indicators.AlightenOrderFlowToolsV0005 AlightenOrderFlowToolsV0005(ISeries<double> input , int barsToProcess, OrderFlowToolsLargeTradeFindByV0005 largeTradeFindBy, bool enableLargeTradeBubbles, int minimumVolumeForMarker, int maximumMarkerSize, OrderFlowToolsLargeTradeSizeByV0005 largeTradeSizeBy, bool hoverValues, int signalFontSize, int signalOffsetTicks, int signalStackPaddingTicks, int trapLookaheadBars, int trapMoveTicks, int trapMinTotalVolume, int trapMinDominancePct, int speedWindowMs, bool enableTrappedTradersSignal, string trappedTradersMarker, bool enableSITrapSignal, string sITrapMarker, bool debugPrints, bool debugOnlyCurrentBar, int debugMinTotalVol, bool enableStackedImbalanceTrap, int sITrapLookaheadBars, int sITrapMoveTicks, int sIAggregationIntervalTicks, double sIImbFact, int sIStackedImbalanceLookback, double sIMinRowVolume, bool sIUseSessionOpenForAggregation)
		{
			return indicator.AlightenOrderFlowToolsV0005(input, barsToProcess, largeTradeFindBy, enableLargeTradeBubbles, minimumVolumeForMarker, maximumMarkerSize, largeTradeSizeBy, hoverValues, signalFontSize, signalOffsetTicks, signalStackPaddingTicks, trapLookaheadBars, trapMoveTicks, trapMinTotalVolume, trapMinDominancePct, speedWindowMs, enableTrappedTradersSignal, trappedTradersMarker, enableSITrapSignal, sITrapMarker, debugPrints, debugOnlyCurrentBar, debugMinTotalVol, enableStackedImbalanceTrap, sITrapLookaheadBars, sITrapMoveTicks, sIAggregationIntervalTicks, sIImbFact, sIStackedImbalanceLookback, sIMinRowVolume, sIUseSessionOpenForAggregation);
		}
	}
}

#endregion

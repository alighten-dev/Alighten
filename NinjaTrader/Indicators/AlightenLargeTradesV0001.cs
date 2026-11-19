#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
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
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
// NT writes nested enum names bare into its generated region: these aliases make them resolve there.
using LargeTradesFindByV0001 = NinjaTrader.NinjaScript.Indicators.AlightenLargeTradesV0001.LargeTradesFindByV0001;
using LargeTradesSizeByV0001 = NinjaTrader.NinjaScript.Indicators.AlightenLargeTradesV0001.LargeTradesSizeByV0001;
#endregion

// AlightenLargeTradesV0001 (2026-11-09)
// The Large Trades bubbles and Big Trade Zones of AlightenOrderFlowToolsV0006 (itself modelled on NinjaTrader's Order Flow
// Large Trades), on their own, with everything else of OrderFlowTools removed (Trapped Traders, the stacked-imbalance trap,
// speed of tape, signal markers). The BidAsk / Price / Block detection is V0006's, unchanged. New:
//   - Sweep mode (1.01): consecutive same-side prints in the same bar, each within the Sweep Window of the last, price moving
//     only in the order's direction = one aggressive order. The bubble sits at the price that traded the most inside the
//     sweep. Side = V0006's aggressor rule (at/above the ask = buy).
//
// CREDIT: Sweep mode is Whisky's idea. It follows the Sweep trade aggregation of whiskysBigTrades by Whisky (v3.1,
// 2026-10-20; Whisky credits its engine to "LargeTrades V3"): the 4 ms window, the price-progress rule and drawing the
// bubble at the sweep's busiest price all come from there. Written fresh for this indicator, no code copied.
//   - Zones in every mode (V0006 made them only in BidAsk and Block), and zone TOUCH / FILLED log lines.
//   - Logging (group 05): one CSV per load in Documents\NinjaTrader 8\Mirror Logs, one line per FINAL large trade
//     (BidAsk: when the quote changes; Sweep: when the order ends; Block: each print; Price: at the bar's end).
//   - Alerts (group 04): sound + Alerts window and phone notifications through a NinjaTrader share service
//     (the Mirror V0058 mechanism), realtime only.

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenLargeTradesV0001 : Indicator
    {
        public enum LargeTradesFindByV0001 { BidAsk, Price, Block, Sweep }
        public enum LargeTradesSizeByV0001 { VisibleArea, Session }

        private const string BUILD_ID = "V0001-2026-11-09b";

        #region Properties

        // -------------------- 01. Large Trades --------------------
        [NinjaScriptProperty]
        [Display(Name = "1.01 Find Large Trades By", Order = 1, GroupName = "01. Large Trades",
            Description = "BidAsk: volume traded while the bid AND the ask stay unchanged (OrderFlowTools / NinjaTrader Large Trades).\n" +
                          "Price: volume at one price in the bar.\nBlock: one print.\n" +
                          "Sweep: one aggressive order - same-side prints within the Sweep Window, price moving one way (group 02). Credit: Whisky's whiskysBigTrades.")]
        public LargeTradesFindByV0001 LargeTradeFindBy { get; set; }

        [Display(Name = "1.02 Show Bubbles", Order = 2, GroupName = "01. Large Trades")]
        public bool EnableLargeTradeBubbles { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "1.03 Minimum Volume", Order = 3, GroupName = "01. Large Trades", Description = "Smallest large trade drawn, logged and alerted.")]
        public int MinimumVolumeForMarker { get; set; }

        [Range(10, int.MaxValue)]
        [Display(Name = "1.04 Maximum Marker Size", Order = 4, GroupName = "01. Large Trades")]
        public int MaximumMarkerSize { get; set; }

        [Display(Name = "1.05 Size Bubbles By", Order = 5, GroupName = "01. Large Trades")]
        public LargeTradesSizeByV0001 LargeTradeSizeBy { get; set; }

        [Display(Name = "1.06 Hover Values", Order = 6, GroupName = "01. Large Trades")]
        public bool HoverValues { get; set; }

        [XmlIgnore]
        [Display(Name = "1.07 Ask (Buy) Brush", Order = 7, GroupName = "01. Large Trades")]
        public System.Windows.Media.Brush AskBrush { get; set; }

        [Browsable(false)]
        public string AskBrushSerializer
        {
            get { return Serialize.BrushToString(AskBrush); }
            set { AskBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "1.08 Bid (Sell) Brush", Order = 8, GroupName = "01. Large Trades")]
        public System.Windows.Media.Brush BidBrush { get; set; }

        [Browsable(false)]
        public string BidBrushSerializer
        {
            get { return Serialize.BrushToString(BidBrush); }
            set { BidBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "1.09 Outline Brush", Order = 9, GroupName = "01. Large Trades")]
        public System.Windows.Media.Brush OutlineBrush { get; set; }

        [Browsable(false)]
        public string OutlineBrushSerializer
        {
            get { return Serialize.BrushToString(OutlineBrush); }
            set { OutlineBrush = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "1.10 Fill Opacity", Order = 10, GroupName = "01. Large Trades")]
        public int FillOpacity { get; set; }

        // -------------------- 02. Sweeps --------------------
        [Range(0, 1000)]
        [Display(Name = "2.01 Sweep Window (ms)", Order = 1, GroupName = "02. Sweeps (credit: Whisky)",
            Description = "A print joins the sweep when it comes within this many milliseconds of the previous print. 0 = identical timestamp only. 4 = the default in Whisky's whiskysBigTrades, where Sweep mode comes from.")]
        public int SweepWindowMs { get; set; }

        [Display(Name = "2.02 Require Price Progress", Order = 2, GroupName = "02. Sweeps (credit: Whisky)",
            Description = "A buy sweep may only stay or move up, a sell sweep only stay or move down.")]
        public bool SweepRequirePriceProgress { get; set; }

        // -------------------- 03. Large Trade Zones --------------------
        [Display(Name = "3.01 Show Large Trade Zones", Order = 1, GroupName = "03. Large Trade Zones")]
        public bool EnableBigTradeZones { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "3.02 Zone Threshold (total vol)", Order = 2, GroupName = "03. Large Trade Zones", Description = "A large trade at least this big draws a zone.")]
        public int BigTradeZoneThreshold { get; set; }

        [Range(0, 100)]
        [Display(Name = "3.03 Zone Opacity", Order = 3, GroupName = "03. Large Trade Zones")]
        public int BigTradeZoneOpacity { get; set; }

        [Range(1, 500)]
        [Display(Name = "3.04 Zone Height (ticks)", Order = 4, GroupName = "03. Large Trade Zones")]
        public int BigTradeZoneHeightTicks { get; set; }

        // -------------------- 04. Alerts --------------------
        [Display(Name = "4.01 Sound Alerts", Order = 1, GroupName = "04. Alerts", Description = "Sound + Alerts window entry for each large trade (realtime only).")]
        public bool SoundAlerts { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "4.02 Alert Minimum Volume", Order = 2, GroupName = "04. Alerts", Description = "Only trades at least this big alert (sound and phone). 0 = 1.03 Minimum Volume.")]
        public int AlertMinVolume { get; set; }

        [Display(Name = "4.03 Buy Sound", Order = 3, GroupName = "04. Alerts", Description = "File in NinjaTrader's sounds folder, or a full path. Empty = silent.")]
        public string BuySound { get; set; }

        [Display(Name = "4.04 Sell Sound", Order = 4, GroupName = "04. Alerts")]
        public string SellSound { get; set; }

        [Display(Name = "4.05 Alert On New Zones", Order = 5, GroupName = "04. Alerts", Description = "Also alert (sound and phone) when a large trade zone is drawn.")]
        public bool AlertOnZones { get; set; }

        [Display(Name = "4.06 Phone Notifications", Order = 6, GroupName = "04. Alerts", Description = "Send each alert through a NinjaTrader share service (Tools > Options > General > Share services).")]
        public bool ShareNotifications { get; set; }

        [TypeConverter(typeof(ShareServiceNameConverterLTV0001))]
        [Display(Name = "4.07 Share Service", Order = 7, GroupName = "04. Alerts", Description = "(default) = the service ticked Default in Share services.")]
        public string ShareServiceName { get; set; }

        [Display(Name = "4.08 Send To", Order = 8, GroupName = "04. Alerts", Description = "Email services only. Empty = the service's own Email address (e.g. your ntfy address).")]
        public string ShareTo { get; set; }

        [Display(Name = "4.09 Subject", Order = 9, GroupName = "04. Alerts",
            Description = "Placeholders: {text} {event} {side} {volume} {ask} {bid} {price} {mode} {instrument} {period} {time}. The subject is ntfy's title.")]
        public string ShareSubject { get; set; }

        [Display(Name = "4.10 Message", Order = 10, GroupName = "04. Alerts", Description = "Same placeholders as 4.09.")]
        public string ShareMessage { get; set; }

        [Range(0, 3600)]
        [Display(Name = "4.11 Min Seconds Between", Order = 11, GroupName = "04. Alerts", Description = "Phone notifications closer together than this are skipped (0 = send all).")]
        public int ShareMinSeconds { get; set; }

        [Display(Name = "4.12 Send in Market Replay", Order = 12, GroupName = "04. Alerts")]
        public bool ShareInPlayback { get; set; }

        // -------------------- 05. Logging --------------------
        [Display(Name = "5.01 Write Log", Order = 1, GroupName = "05. Logging",
            Description = "One CSV per load: LargeTradesV0001_<instrument>_<mode>_L<load time>.csv, one line per final large trade and per zone event.")]
        public bool WriteLog { get; set; }

        [Display(Name = "5.02 Log History", Order = 2, GroupName = "05. Logging", Description = "Also log the trades found while loading history (the 6.01 Bars To Process window), not only realtime.")]
        public bool LogHistory { get; set; }

        [Display(Name = "5.03 Log Folder", Order = 3, GroupName = "05. Logging", Description = "Empty = Documents\\NinjaTrader 8\\Mirror Logs.")]
        public string LogFolder { get; set; }

        // -------------------- 06. Performance --------------------
        [Range(0, 200000)]
        [Display(Name = "6.01 Bars To Process", Order = 1, GroupName = "06. Performance", Description = "History is processed only for the last N primary bars (0 = all).")]
        public int BarsToProcess { get; set; }

        [Display(Name = "6.02 Debug Prints", Order = 2, GroupName = "06. Performance")]
        public bool DebugPrints { get; set; }

        #endregion

        #region State

        private SharpDX.Direct2D1.Brush dxAskBrush;
        private SharpDX.Direct2D1.Brush dxBidBrush;
        private SharpDX.Direct2D1.Brush dxOutlineBrush;
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

        // BidAsk epoch (the run of prints at one unchanged quote) - for the log and alerts
        private bool     epActive;
        private Tuple<int, double, int> epKey;      // the bubble's key once the epoch reached 1.03
        private DateTime epFirstTime, epLastTime;
        private int      epPrints;
        private double   epLo, epHi;

        // Sweep (1.01 = Sweep)
        private bool     swActive;
        private int      swBar;
        private bool     swIsAsk;
        private double   swVol;
        private int      swFills;
        private double   swLastPrice, swLo, swHi;
        private DateTime swFirstTime, swLastTime;
        private Tuple<int, double, int> swKey;
        private readonly Dictionary<double, double> swVolByPrice = new Dictionary<double, double>();
        private readonly ConcurrentDictionary<Tuple<int, double, int>, string> bubbleInfo = new ConcurrentDictionary<Tuple<int, double, int>, string>();

        // Price mode: per-bar log + alert bookkeeping
        private int      priceBarIdx = -1;
        private DateTime priceBarLastTime;
        private readonly HashSet<double> priceAlerted = new HashSet<double>();

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

        // Zones: shaded band around a large trade; each tick row closes (FVG style) when a later bar trades through it
        private class BigTradeZone
        {
            public int    StartBarIdx;
            public Dictionary<double, int> Ticks;   // price -> -1 open, else the bar that closed the row
            public bool   IsAskDom;
            public double TotalVol;
            public double TopPrice;
            public double BottomPrice;
            public double Price;
            public bool   Touched;
            public bool   Filled;
        }

        private List<BigTradeZone> bigTradeZones;
        private HashSet<string>    bigTradeZoneKeys;

        private Series<double> zoneTopPrice;
        private Series<double> zoneBotPrice;

        // Log
        private StreamWriter logWriter;
        private string       logPath;
        private bool         logFailed;
        private int          logLinesSinceFlush;

        private int alertSeq;

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ZoneTopPrice { get { return zoneTopPrice; } }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> ZoneBotPrice { get { return zoneBotPrice; } }

        #endregion

        // -------------------- Lifecycle --------------------
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "Large Trades bubbles and Large Trade Zones from AlightenOrderFlowToolsV0006, plus Sweep mode, logging and alerts. Sweep mode credit: Whisky (whiskysBigTrades).";
                Name                     = "AlightenLargeTradesV0001";
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
                ShowTransparentPlotsInDataBox = true;

                // defaults = the user's OrderFlowTools V0006 settings in the AlightenMirror workspace (2026-11-09)
                LargeTradeFindBy        = LargeTradesFindByV0001.BidAsk;
                EnableLargeTradeBubbles = true;
                MinimumVolumeForMarker  = 50;
                MaximumMarkerSize       = 100;
                LargeTradeSizeBy        = LargeTradesSizeByV0001.VisibleArea;
                HoverValues             = true;
                AskBrush                = Brushes.DarkCyan;
                BidBrush                = Brushes.Crimson;
                OutlineBrush            = Brushes.DarkGray;
                FillOpacity             = 50;

                SweepWindowMs             = 4;
                SweepRequirePriceProgress = true;

                EnableBigTradeZones     = true;
                BigTradeZoneThreshold   = 50;
                BigTradeZoneOpacity     = 20;
                BigTradeZoneHeightTicks = 20;

                SoundAlerts        = false;
                AlertMinVolume     = 0;
                BuySound           = "Alert1.wav";
                SellSound          = "Alert2.wav";
                AlertOnZones       = false;
                ShareNotifications = false;
                ShareServiceName   = "(default)";
                ShareTo            = "";
                ShareSubject       = "{instrument} LARGE {side} {volume}";
                ShareMessage       = "{text}";
                ShareMinSeconds    = 5;
                ShareInPlayback    = true;

                WriteLog   = false;
                LogHistory = true;
                LogFolder  = "";

                BarsToProcess = 800;
                DebugPrints   = false;

                AddPlot(Brushes.Transparent, "ZoneTopPrice");
                AddPlot(Brushes.Transparent, "ZoneBotPrice");
                return;
            }

            if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);

                if (HoverValues)
                {
                    hoverPad             = 3f;
                    hoverTextBrushWpf    = Brushes.White;
                    hoverBgBrushWpf      = Brushes.Black;
                    hoverOutlineBrushWpf = Brushes.DimGray;

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

                bigTradeZones    = new List<BigTradeZone>(256);
                bigTradeZoneKeys = new HashSet<string>();

                zoneTopPrice = Values[0];
                zoneBotPrice = Values[1];

                epActive = false; swActive = false; priceBarIdx = -1;
                bubbleInfo.Clear();
                priceAlerted.Clear();

                if (WriteLog) OpenLog();
                Print(Name + " " + BUILD_ID + ": mode " + LargeTradeFindBy + ", min " + MinimumVolumeForMarker
                      + (WriteLog ? ", log " + (logPath ?? "(failed)") : ", log off"));
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

            if (State == State.Realtime)
            {
                LogLine("REALTIME", "", "", DateTime.MinValue, DateTime.MinValue, -1, 0, 0, 0, 0, 0, 0);
                FlushLog();
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
                CloseLog();
            }
        }

        protected override void OnBarUpdate()
        {
            CalculateInternal(false);

            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 1)
            {
                zoneTopPrice[0] = 0;
                zoneBotPrice[0] = 0;
                return;
            }

            UpdateZoneDataBoxPlots();

            if (!IsFirstTickOfBar)
                return;

            if (EnableBigTradeZones && bigTradeZones != null)
                CheckZoneInvalidation(CurrentBars[0] - 1);
        }

        // ==================== Core calc (tick series) ====================
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

            // History: only the last BarsToProcess primary bars (+50 bars of warm-up). Realtime is never gated.
            if (State == State.Historical && BarsToProcess > 0)
            {
                if (CurrentBars[0] < BarsArray[0].Count - (BarsToProcess + 50))
                    return;
            }

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

            if (LargeTradeFindBy == LargeTradesFindByV0001.BidAsk)
            {
                if (ask.ApproxCompare(prevAsk) == 0 && bid.ApproxCompare(prevBid) == 0)
                {
                    rollingAskVol += isAskAggressor ? vol : 0.0;
                    rollingBidVol += isAskAggressor ? 0.0 : vol;
                }
                else
                {
                    FinalizeEpoch();   // V0001: the previous quote's run is final - log / alert it
                    rollingAskVol = isAskAggressor ? vol : 0.0;
                    rollingBidVol = isAskAggressor ? 0.0 : vol;
                    lastKey = null;
                    epActive = true; epKey = null; epFirstTime = t; epPrints = 0; epLo = close; epHi = close;
                }

                epPrints++; epLastTime = t;
                if (close < epLo) epLo = close;
                if (close > epHi) epHi = close;

                if (rollingAskVol + rollingBidVol >= MinimumVolumeForMarker)
                {
                    var key = MakeUniqueKey(primaryBarIdx, close, 0);
                    UpsertOrb(key, rollingAskVol, rollingBidVol, t, sessionId, mustStage, replaceLastKey: true);
                    MaybAddBigTradeZone(key, rollingAskVol, rollingBidVol, t);
                    TrackSessionMax(rollingAskVol + rollingBidVol);
                    if (epKey != null && !epKey.Equals(key)) bubbleInfo.TryRemove(epKey, out _);
                    epKey = key;
                }
            }
            else if (LargeTradeFindBy == LargeTradesFindByV0001.Price)
            {
                rollingAskVol = isAskAggressor ? vol : 0.0;
                rollingBidVol = isAskAggressor ? 0.0 : vol;

                if (lastPrimaryBarIdx != (CurrentBars[0] - timeBasedRealtimeAdjust) || volAtPriceThisBar == null)
                {
                    LogPriceBar();     // V0001: the finished bar's price rows are final
                    volAtPriceThisBar = new Dictionary<double, Tuple<double, double>>();
                    priceAlerted.Clear();
                }

                if (volAtPriceThisBar.TryGetValue(close, out var ab))
                    volAtPriceThisBar[close] = new Tuple<double, double>(ab.Item1 + rollingAskVol, ab.Item2 + rollingBidVol);
                else
                    volAtPriceThisBar[close] = new Tuple<double, double>(rollingAskVol, rollingBidVol);

                priceBarIdx = primaryBarIdx;
                priceBarLastTime = t;

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

                if (totalHere > 0)
                {
                    var atP = volAtPriceThisBar[close];
                    MaybAddBigTradeZone(key, atP.Item1, atP.Item2, t);   // V0001: zones in Price mode too
                    // Price rows grow all bar: alert once, when the row first reaches the alert size
                    if (totalHere >= AlertThreshold() && !priceAlerted.Contains(close))
                    {
                        priceAlerted.Add(close);
                        TradeAlert("LARGE TRADE", atP.Item1 >= atP.Item2, close, atP.Item1, atP.Item2, "Price", 0, close, close);
                    }
                }
            }
            else if (LargeTradeFindBy == LargeTradesFindByV0001.Sweep)
            {
                bool cont = swActive && primaryBarIdx == swBar && isAskAggressor == swIsAsk
                            && (SweepWindowMs <= 0 ? t == swLastTime : (t - swLastTime).TotalMilliseconds <= SweepWindowMs);
                if (cont && SweepRequirePriceProgress)
                    cont = swIsAsk ? close >= swLastPrice : close <= swLastPrice;

                if (!cont)
                {
                    FinalizeSweep();
                    swActive = true; swBar = primaryBarIdx; swIsAsk = isAskAggressor;
                    swVol = 0; swFills = 0; swVolByPrice.Clear();
                    swFirstTime = t; swLo = close; swHi = close; swKey = null;
                }

                swVol += vol; swFills++;
                swLastPrice = close; swLastTime = t;
                if (close < swLo) swLo = close;
                if (close > swHi) swHi = close;
                double pv;
                swVolByPrice.TryGetValue(close, out pv);
                swVolByPrice[close] = pv + vol;

                if (swVol >= MinimumVolumeForMarker)
                {
                    double drawPx = SweepDrawPrice();
                    // the bubble grows in place while the sweep runs (UpsertOrb replaces this sweep's previous key)
                    lastKey = swKey;
                    var key = (swKey != null && swKey.Item2.ApproxCompare(drawPx) == 0) ? swKey : MakeUniqueKey(swBar, drawPx, 0);
                    UpsertOrb(key, swIsAsk ? swVol : 0.0, swIsAsk ? 0.0 : swVol, t, sessionId, mustStage, replaceLastKey: true);
                    lastKey = null;
                    if (swKey != null && !swKey.Equals(key)) bubbleInfo.TryRemove(swKey, out _);
                    swKey = key;
                    bubbleInfo[key] = SweepInfoText();
                    TrackSessionMax(swVol);
                }
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
                    MaybAddBigTradeZone(key, rollingAskVol, rollingBidVol, t);
                    TrackSessionMax(rollingAskVol + rollingBidVol);
                    LogTrade("Block", key.Item1, close, rollingAskVol, rollingBidVol, 1, close, close, t, t);
                    TradeAlert("LARGE TRADE", isAskAggressor, close, rollingAskVol, rollingBidVol, "Block", 1, close, close);
                }
            }

            prevAsk = ask;
            prevBid = bid;
            prevWasAsk = isAskAggressor;
            lastPrimaryBarIdx = CurrentBars[0] - timeBasedRealtimeAdjust;
        }

        // BidAsk: the run at the previous quote ended; if it made a bubble, that bubble is final now
        private void FinalizeEpoch()
        {
            if (!epActive || epKey == null) { epActive = false; return; }
            double a = rollingAskVol, b = rollingBidVol;
            LogTrade("BidAsk", epKey.Item1, epKey.Item2, a, b, epPrints, epLo, epHi, epFirstTime, epLastTime);
            TradeAlert("LARGE TRADE", a >= b, epKey.Item2, a, b, "BidAsk", epPrints, epLo, epHi);
            epActive = false; epKey = null;
        }

        // Sweep: the order ended (next print broke the chain); if it reached 1.03 it is final now
        private void FinalizeSweep()
        {
            if (!swActive) return;
            swActive = false;
            if (swKey == null) return;
            double a = swIsAsk ? swVol : 0.0, b = swIsAsk ? 0.0 : swVol;
            MaybAddBigTradeZone(swKey, a, b, swLastTime);
            LogTrade("Sweep", swKey.Item1, swKey.Item2, a, b, swFills, swLo, swHi, swFirstTime, swLastTime);
            TradeAlert("LARGE TRADE", swIsAsk, swKey.Item2, a, b, "Sweep", swFills, swLo, swHi);
            swKey = null;
        }

        private double SweepDrawPrice()
        {
            double best = -1, px = swLastPrice;
            foreach (var kv in swVolByPrice)
                if (kv.Value > best) { best = kv.Value; px = kv.Key; }
            return px;
        }

        private string SweepInfoText()
        {
            return "Fills: " + swFills + "   Levels: " + swVolByPrice.Count + "\nRange: " + Instrument.MasterInstrument.FormatPrice(swLo, true)
                   + " - " + Instrument.MasterInstrument.FormatPrice(swHi, true)
                   + "\nTook: " + (swLastTime - swFirstTime).TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
        }

        // Price mode: log every row of the finished bar that reached 1.03
        private void LogPriceBar()
        {
            if (volAtPriceThisBar == null || priceBarIdx < 0) return;
            foreach (var kv in volAtPriceThisBar)
            {
                double tot = kv.Value.Item1 + kv.Value.Item2;
                if (tot >= MinimumVolumeForMarker)
                    LogTrade("Price", priceBarIdx, kv.Key, kv.Value.Item1, kv.Value.Item2, 0, kv.Key, kv.Key, priceBarLastTime, priceBarLastTime);
            }
        }

        private double AlertThreshold()
        {
            return AlertMinVolume > 0 ? AlertMinVolume : MinimumVolumeForMarker;
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
            if (LargeTradeSizeBy != LargeTradesSizeByV0001.Session)
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

        // ==================== Large Trade Zones ====================

        private void MaybAddBigTradeZone(Tuple<int, double, int> key, double askVol, double bidVol, DateTime t)
        {
            if (!EnableBigTradeZones)
                return;

            double tot = askVol + bidVol;
            if (tot < BigTradeZoneThreshold)
                return;

            // One zone per (bar, price) - string key ignores tiebreaker churn
            string zoneDedupeKey = key.Item1 + "_" + key.Item2.ToString("F5");
            if (bigTradeZoneKeys.Contains(zoneDedupeKey))
                return;

            int barIdx = key.Item1;
            if (barIdx < 0 || barIdx >= BarsArray[0].Count)
                return;

            bigTradeZoneKeys.Add(zoneDedupeKey);

            double halfHeight = (BigTradeZoneHeightTicks / 2.0) * TickSize;
            double bubblePx   = key.Item2;
            double top        = Math.Round((bubblePx + halfHeight) / TickSize) * TickSize;
            double bot        = Math.Round((bubblePx - halfHeight) / TickSize) * TickSize;

            var ticks = new Dictionary<double, int>();
            for (double p = bot; p <= top + TickSize * 0.001; p += TickSize)
            {
                p = Math.Round(p / TickSize) * TickSize;
                ticks[p] = -1;
            }

            bool isAsk = askVol >= bidVol;

            var zone = new BigTradeZone
            {
                StartBarIdx = barIdx,
                Ticks       = ticks,
                IsAskDom    = isAsk,
                TotalVol    = tot,
                TopPrice    = top,
                BottomPrice = bot,
                Price       = bubblePx,
            };

            lock (bigTradeZones)
                bigTradeZones.Add(zone);

            LogLine("ZONE", LargeTradeFindBy.ToString(), isAsk ? "BUY" : "SELL", t, t, barIdx, bubblePx,
                    askVol, bidVol, 0, bot, top);

            if (AlertOnZones)
                TradeAlert("ZONE", isAsk, bubblePx, askVol, bidVol, LargeTradeFindBy.ToString(), 0, bot, top);

            if (DebugPrints)
                Print($"[LT ZONE ADD] bar={barIdx} top={top:F2} bot={bot:F2} ticks={ticks.Count} vol={tot:0} ask={isAsk}");
        }

        // Each closed bar closes every open zone row it traded through (not on the zone's own bar or the next one)
        private void CheckZoneInvalidation(int barJustClosed)
        {
            if (bigTradeZones == null || barJustClosed < 0)
                return;

            double hi = BarsArray[0].GetHigh(barJustClosed);
            double lo = BarsArray[0].GetLow(barJustClosed);

            lock (bigTradeZones)
            {
                foreach (var z in bigTradeZones)
                {
                    if (z.Ticks == null || z.Ticks.Count == 0 || z.Filled)
                        continue;

                    if (barJustClosed < z.StartBarIdx + 2)
                        continue;

                    bool closedAny = false;
                    foreach (double p in z.Ticks.Keys.ToList())
                    {
                        if (z.Ticks[p] != -1)
                            continue;

                        if (p >= lo && p <= hi)
                        {
                            z.Ticks[p] = barJustClosed;
                            closedAny = true;
                        }
                    }

                    if (!closedAny) continue;

                    DateTime bt = BarsArray[0].GetTime(barJustClosed);
                    if (!z.Touched)
                    {
                        z.Touched = true;
                        LogLine("ZONE_TOUCH", LargeTradeFindBy.ToString(), z.IsAskDom ? "BUY" : "SELL", bt, bt, z.StartBarIdx, z.Price,
                                z.IsAskDom ? z.TotalVol : 0, z.IsAskDom ? 0 : z.TotalVol, 0, z.BottomPrice, z.TopPrice);
                    }
                    if (z.Ticks.Values.All(v => v != -1))
                    {
                        z.Filled = true;
                        LogLine("ZONE_FILLED", LargeTradeFindBy.ToString(), z.IsAskDom ? "BUY" : "SELL", bt, bt, z.StartBarIdx, z.Price,
                                z.IsAskDom ? z.TotalVol : 0, z.IsAskDom ? 0 : z.TotalVol, 0, z.BottomPrice, z.TopPrice);
                    }
                }
            }
        }

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

            lock (bigTradeZones)
            {
                foreach (var z in bigTradeZones)
                {
                    if (z.Ticks == null || z.StartBarIdx > curBar)
                        continue;
                    if (z.Filled && z.Ticks.Values.Max() < curBar)
                        continue;

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
            }

            zoneTopPrice[0] = bestTop;
            zoneBotPrice[0] = bestBot;
        }

        // ==================== Log ====================

        private void OpenLog()
        {
            if (logWriter != null || logFailed) return;
            try
            {
                string dir = string.IsNullOrWhiteSpace(LogFolder) ? Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "Mirror Logs") : LogFolder.Trim();
                Directory.CreateDirectory(dir);
                string inst = Instrument != null ? Instrument.FullName : "inst";
                foreach (char c in Path.GetInvalidFileNameChars()) inst = inst.Replace(c, '_');
                inst = inst.Replace(' ', '_');
                string stem = "LargeTradesV0001_" + inst + "_" + LargeTradeFindBy + "_L" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string path = Path.Combine(dir, stem + ".csv");
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, stem + "_" + n + ".csv");
                var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                logWriter = new StreamWriter(fs, new System.Text.UTF8Encoding(false));
                logPath = path;
                logWriter.WriteLine("# " + Name + " " + BUILD_ID + " instrument=" + inst + " period=" + BarsPeriod + " mode=" + LargeTradeFindBy
                                    + " min=" + MinimumVolumeForMarker + " zone=" + BigTradeZoneThreshold + "/" + BigTradeZoneHeightTicks + "t"
                                    + " sweep_ms=" + SweepWindowMs + " progress=" + SweepRequirePriceProgress + " bars_to_process=" + BarsToProcess);
                logWriter.WriteLine("event,mode,side,time,end_time,bar_time,price,ask,bid,total,fills,low,high,ms,state");
                logWriter.Flush();
            }
            catch (Exception ex)
            {
                logFailed = true;
                logWriter = null;
                Print(Name + ": could not open the log: " + ex.Message);
            }
        }

        private void LogTrade(string mode, int barIdx, double price, double askVol, double bidVol, int fills, double lo, double hi, DateTime t0, DateTime t1)
        {
            LogLine("TRADE", mode, askVol >= bidVol ? "BUY" : "SELL", t0, t1, barIdx, price, askVol, bidVol, fills, lo, hi);
        }

        private void LogLine(string evt, string mode, string side, DateTime t0, DateTime t1, int barIdx, double price,
                             double askVol, double bidVol, int fills, double lo, double hi)
        {
            if (!WriteLog || logWriter == null) return;
            if (State == State.Historical && !LogHistory) return;
            try
            {
                var ci = CultureInfo.InvariantCulture;
                string barTime = (barIdx >= 0 && BarsArray != null && barIdx < BarsArray[0].Count) ? BarsArray[0].GetTime(barIdx).ToString("yyyy-MM-dd HH:mm:ss", ci) : "";
                string ts0 = t0 == DateTime.MinValue ? "" : t0.ToString("yyyy-MM-dd HH:mm:ss.fff", ci);
                string ts1 = t1 == DateTime.MinValue ? "" : t1.ToString("yyyy-MM-dd HH:mm:ss.fff", ci);
                string ms  = (t0 == DateTime.MinValue || t1 == DateTime.MinValue) ? "" : (t1 - t0).TotalMilliseconds.ToString("0", ci);
                string line = string.Join(",", evt, mode, side, ts0, ts1, barTime,
                    price.ToString("0.########", ci), askVol.ToString("0.########", ci), bidVol.ToString("0.########", ci),
                    (askVol + bidVol).ToString("0.########", ci), fills.ToString(ci), lo.ToString("0.########", ci), hi.ToString("0.########", ci),
                    ms, State == State.Realtime ? "R" : "H");
                logWriter.WriteLine(line);
                if (State == State.Realtime || ++logLinesSinceFlush >= 500) FlushLog();
            }
            catch (Exception ex)
            {
                Print(Name + ": log write failed, logging stopped: " + ex.Message);
                CloseLog();
                logFailed = true;
            }
        }

        private void FlushLog()
        {
            logLinesSinceFlush = 0;
            try { logWriter?.Flush(); } catch { }
        }

        private void CloseLog()
        {
            try { logWriter?.Flush(); logWriter?.Dispose(); } catch { }
            logWriter = null;
        }

        // ==================== Alerts ====================

        private void TradeAlert(string evt, bool isBuy, double price, double askVol, double bidVol, string mode, int fills, double lo, double hi)
        {
            if (State != State.Realtime) return;
            double tot = askVol + bidVol;
            if (evt != "ZONE" && tot < AlertThreshold()) return;
            if (!SoundAlerts && !ShareNotifications) return;

            string fmtPx = Instrument.MasterInstrument.FormatPrice(price);
            string text = string.Format(CultureInfo.InvariantCulture, "{0} {1}: {2} {3} {4:0} @ {5} ({6}{7})",
                Instrument.FullName, BarsPeriod, evt == "ZONE" ? "zone" : "large", isBuy ? "BUY" : "SELL", tot, fmtPx, mode,
                mode == "Sweep" ? ", " + fills + " fills " + Instrument.MasterInstrument.FormatPrice(lo) + "-" + Instrument.MasterInstrument.FormatPrice(hi) : "");

            if (SoundAlerts)
            {
                string path = AlertSoundPath(isBuy ? BuySound : SellSound);
                try
                {
                    Alert("LTV0001_" + (++alertSeq), Priority.Medium, text, path, 0,
                          isBuy ? Brushes.DarkCyan : Brushes.DarkRed, Brushes.White);
                }
                catch (Exception ex) { Print(Name + ": alert failed: " + ex.Message); }
            }
            if (ShareNotifications)
                ShareSend(evt == "ZONE" ? "ZONE" : "LARGE TRADE", isBuy, price, askVol, bidVol, mode, text);
        }

        private string AlertSoundPath(string file)
        {
            string path = string.IsNullOrWhiteSpace(file) ? ""
                        : Path.IsPathRooted(file) ? file
                        : Path.Combine(NinjaTrader.Core.Globals.InstallDir, "sounds", file);
            if (path.Length > 0 && !File.Exists(path))
            {
                Print(Name + ": alert sound not found: " + path);
                path = "";
            }
            return path;
        }

        // Phone notifications through a NinjaTrader share service (as in AlightenMirrorV0058Signal). The services live in
        // Documents\NinjaTrader 8\Config.xml under <ShareServices>. NinjaTrader's Mail service sends only to the address passed
        // in args[0], so an Email service is always called with { to, subject }; other types get subject + body as one text.
        public class ShareServiceInfoLTV0001 { public string Name; public string Type; public bool IsDefault; public string EmailAddress; }

        public static List<ShareServiceInfoLTV0001> ReadShareServicesLTV0001()
        {
            var list = new List<ShareServiceInfoLTV0001>();
            try
            {
                string cfg = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "Config.xml");
                if (!File.Exists(cfg)) return list;
                var doc = new System.Xml.XmlDocument();
                using (var fs = new FileStream(cfg, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    doc.Load(fs);
                System.Xml.XmlNode root = doc.SelectSingleNode("//*[local-name()='ShareServices']");
                if (root == null) return list;
                foreach (System.Xml.XmlNode n in root.ChildNodes)
                {
                    if (n.NodeType != System.Xml.XmlNodeType.Element) continue;
                    System.Xml.XmlNode nm = n.SelectSingleNode("*[local-name()='Name']");
                    if (nm == null || string.IsNullOrWhiteSpace(nm.InnerText)) continue;
                    System.Xml.XmlNode conf = n.SelectSingleNode("*[local-name()='IsConfigured']");
                    if (conf != null && conf.InnerText.Trim().ToLowerInvariant() == "false") continue;
                    System.Xml.XmlNode def = n.SelectSingleNode("*[local-name()='IsDefault']");
                    System.Xml.XmlNode from = n.SelectSingleNode("*[local-name()='FromMailAddress']");
                    list.Add(new ShareServiceInfoLTV0001
                    {
                        Name = nm.InnerText.Trim(),
                        Type = n.LocalName,
                        IsDefault = def != null && def.InnerText.Trim().ToLowerInvariant() == "true",
                        EmailAddress = from != null ? from.InnerText.Trim() : ""
                    });
                }
            }
            catch { }
            return list;
        }

        private DateTime shareLastUtc = DateTime.MinValue;
        private bool shareWarned;

        private void ShareSend(string evt, bool isBuy, double price, double askVol, double bidVol, string mode, string text)
        {
            if (!ShareNotifications || State != State.Realtime) return;
            if (!ShareInPlayback && Connection.PlaybackConnection != null) return;

            DateTime nowUtc = DateTime.UtcNow;
            if (ShareMinSeconds > 0 && (nowUtc - shareLastUtc).TotalSeconds < ShareMinSeconds)
            {
                Print(Name + ": notification skipped (4.11 Min Seconds Between): " + text);
                return;
            }

            ShareServiceInfoLTV0001 svc = null;
            string want = (ShareServiceName ?? "").Trim();
            List<ShareServiceInfoLTV0001> all = ReadShareServicesLTV0001();
            if (want.Length == 0 || want == "(default)")
                svc = all.FirstOrDefault(s => s.IsDefault) ?? (all.Count == 1 ? all[0] : null);
            else
                svc = all.FirstOrDefault(s => string.Equals(s.Name, want, StringComparison.OrdinalIgnoreCase))
                      ?? new ShareServiceInfoLTV0001 { Name = want, Type = "", EmailAddress = "" };
            if (svc == null)
            {
                if (!shareWarned) Print(Name + ": no share service to send through - set 4.07 Share Service (Tools > Options > General > Share services).");
                shareWarned = true;
                return;
            }

            var ci = CultureInfo.InvariantCulture;
            string side = isBuy ? "BUY" : "SELL";
            string px = Instrument.MasterInstrument.FormatPrice(price);
            string tm = Times[0][0].ToString("HH:mm:ss", ci);
            Func<string, string> fill = s => (s ?? "")
                .Replace("{text}", text).Replace("{event}", evt).Replace("{side}", side)
                .Replace("{volume}", (askVol + bidVol).ToString("0", ci)).Replace("{ask}", askVol.ToString("0", ci)).Replace("{bid}", bidVol.ToString("0", ci))
                .Replace("{price}", px).Replace("{mode}", mode).Replace("{instrument}", Instrument.FullName)
                .Replace("{period}", BarsPeriod.ToString()).Replace("{time}", tm);
            string subject = fill(ShareSubject).Trim();
            string body = fill(ShareMessage).Trim();
            if (body.Length == 0) body = text;

            try
            {
                if (svc.Type == "Mail")
                {
                    string to = string.IsNullOrWhiteSpace(ShareTo) ? svc.EmailAddress : ShareTo.Trim();
                    if (string.IsNullOrWhiteSpace(to))
                    {
                        if (!shareWarned) Print(Name + ": share service '" + svc.Name + "' has no Email address - fill 4.08 Send To.");
                        shareWarned = true;
                        return;
                    }
                    Share(svc.Name, body, new object[] { to, subject });
                }
                else
                    Share(svc.Name, subject.Length > 0 ? subject + " - " + body : body);
                shareLastUtc = nowUtc;
            }
            catch (Exception ex) { Print(Name + ": notification failed (" + svc.Name + "): " + ex.Message); }
        }

        // 4.07: the configured share services as a drop-down (typing a name is allowed too)
        public class ShareServiceNameConverterLTV0001 : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return false; }
            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                var names = new List<string> { "(default)" };
                foreach (var s in ReadShareServicesLTV0001())
                    if (!names.Contains(s.Name)) names.Add(s.Name);
                return new StandardValuesCollection(names);
            }
        }

        // ==================== Min/Max & selection ====================
        public override void OnCalculateMinMax()
        {
            if (ChartBars == null || committed == null)
                return;

            double hi = double.MinValue;
            double lo = double.MaxValue;

            int from = ChartBars.FromIndex - Displacement;
            int to   = Math.Max(ChartBars.ToIndex, ChartBars.GetBarIdxByTime(ChartControl, ChartControl.LastTimePainted)) - Displacement;

            lock (staged)
            {
                foreach (var k in committed.Keys)
                {
                    if (k.Item1 < from || k.Item1 > to) continue;
                    if (k.Item2 > hi) hi = k.Item2;
                    if (k.Item2 < lo) lo = k.Item2;
                }
            }

            MaxValue = hi;
            MinValue = lo;
        }

        protected override System.Windows.Point[] OnGetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
        {
            if (!IsSelected || Count == 0 || ChartBars == null || committed == null)
                return Array.Empty<System.Windows.Point>();

            var pts = new List<System.Windows.Point>();
            int from = ChartBars.FromIndex - Displacement;
            int to   = Math.Max(ChartBars.ToIndex, ChartBars.GetBarIdxByTime(chartControl, chartControl.LastTimePainted)) - Displacement;

            lock (staged)
            {
                foreach (var key in committed.Keys)
                {
                    if (key.Item1 < from || key.Item1 > to || key.Item3 != 0) continue;
                    pts.Add(new System.Windows.Point(chartControl.GetXByBarIndex(ChartBars, key.Item1 + Displacement),
                                                     chartScale.GetYByValue(key.Item2)));
                }
            }

            return pts.ToArray();
        }

        // ==================== Render ====================
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

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (BarsArray == null || BarsArray.Length == 0 || BarsArray[0] == null || ChartBars == null || ChartBars.Count <= 0 || committed == null)
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

            if (dxAskBrush == null || dxBidBrush == null || dxOutlineBrush == null)
                return;

            var prevAA = RenderTarget.AntialiasMode;
            RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;

            int firstBar = ChartBars.FromIndex - Displacement;
            int lastBar  = Math.Max(ChartBars.ToIndex, ChartBars.GetBarIdxByTime(chartControl, chartControl.LastTimePainted)) - Displacement;

            // -------- zones (FVG-style per-tick rows) --------
            if (EnableBigTradeZones && bigTradeZones != null && dxAskZoneBrush != null && dxBidZoneBrush != null)
            {
                float zoneOpacity = BigTradeZoneOpacity / 100f;

                lock (bigTradeZones)
                {
                    foreach (var z in bigTradeZones)
                    {
                        if (z.Ticks == null || z.Ticks.Count == 0)
                            continue;

                        if (z.StartBarIdx > lastBar)
                            continue;

                        double xOrigin = chartControl.GetXByBarIndex(ChartBars, z.StartBarIdx + Displacement);

                        var zoneBrush = z.IsAskDom ? dxAskZoneBrush : dxBidZoneBrush;
                        zoneBrush.Opacity = zoneOpacity;

                        foreach (var kvp in z.Ticks)
                        {
                            double price  = kvp.Key;
                            int    endBar = kvp.Value;

                            double yTop = chartScale.GetYByValue(price + TickSize * 0.5);
                            double yBot = chartScale.GetYByValue(price - TickSize * 0.5);
                            double h    = yBot - yTop;
                            if (h <= 0) h = 1;

                            int endIdx = endBar > 0 ? endBar + Displacement : ChartBars.ToIndex;
                            double xEnd = chartControl.GetXByBarIndex(ChartBars, endIdx);
                            double w    = xEnd - xOrigin;
                            if (w <= 0) continue;

                            RenderTarget.FillRectangle(
                                new SharpDX.RectangleF((float)xOrigin, (float)yTop, (float)w, (float)h),
                                zoneBrush);
                        }

                        int    lineEndBar = z.Ticks.Values.Any(v => v == -1)
                                           ? ChartBars.ToIndex
                                           : z.Ticks.Values.Max() + Displacement;
                        double xLineEnd   = chartControl.GetXByBarIndex(ChartBars, lineEndBar);
                        double lineW      = xLineEnd - xOrigin;
                        if (lineW > 0)
                        {
                            var prevOp = dxOutlineBrush.Opacity;
                            dxOutlineBrush.Opacity = Math.Min(1f, zoneOpacity * 2f);
                            double yLineTop = chartScale.GetYByValue(z.TopPrice + TickSize * 0.5);
                            double yLineBot = chartScale.GetYByValue(z.BottomPrice - TickSize * 0.5);
                            RenderTarget.DrawLine(new SharpDX.Vector2((float)xOrigin, (float)yLineTop), new SharpDX.Vector2((float)xLineEnd, (float)yLineTop), dxOutlineBrush, 1f);
                            RenderTarget.DrawLine(new SharpDX.Vector2((float)xOrigin, (float)yLineBot), new SharpDX.Vector2((float)xLineEnd, (float)yLineBot), dxOutlineBrush, 1f);
                            dxOutlineBrush.Opacity = prevOp;
                        }
                    }
                }
            }

            // -------- bubbles --------
            if (!EnableLargeTradeBubbles)
            {
                RenderTarget.AntialiasMode = prevAA;
                return;
            }

            List<KeyValuePair<Tuple<int, double, int>, Tuple<double, double, DateTime, int>>> visible;
            lock (staged)
            {
                visible = committed.Where(k => k.Key.Item1 >= firstBar && k.Key.Item1 <= lastBar)
                                   .OrderBy(k => k.Value.Item3)
                                   .ToList();
            }

            if (LargeTradeSizeBy == LargeTradesSizeByV0001.VisibleArea)
            {
                maxVolVisible = visible
                    .Select(kvp => (kvp.Value?.Item1 ?? 0.0) + (kvp.Value?.Item2 ?? 0.0))
                    .DefaultIfEmpty(0.0)
                    .Max();

                if (maxVolVisible <= 0.0)
                {
                    RenderTarget.AntialiasMode = prevAA;
                    return;
                }
            }

            foreach (var kvp in visible)
            {
                var key   = kvp.Key;
                var value = kvp.Value;

                if (LargeTradeSizeBy == LargeTradesSizeByV0001.Session && maxVolBySession != null && maxVolBySession.TryGetValue(value.Item4, out var sv))
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

            if (HoverValues && hoverText != null && hoverFont != null && dxHoverBg != null)
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
                tf.Dispose();
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

        // ==================== Hover ====================
        private void HoverTimerTick(object sender, EventArgs e)
        {
            if (ChartBars == null || ChartBars.Count <= 0 || ChartPanel == null || committed == null)
                return;

            if (!EnableLargeTradeBubbles && !EnableBigTradeZones)
            {
                hoverText = null;
                hoverTimer.IsEnabled = false;
                return;
            }

            cursorPt.X = Mouse.GetPosition(ChartPanel.ChartControl).X.ConvertToHorizontalPixels(ChartPanel.ChartControl.PresentationSource);
            cursorPt.Y = Mouse.GetPosition(ChartPanel.ChartControl).Y.ConvertToVerticalPixels(ChartPanel.ChartControl.PresentationSource);

            int windowBars = MaximumMarkerSize / Math.Max(1, Convert.ToInt32(ChartControl.GetBarPaintWidth(ChartBars)));
            int barAtX      = ChartBars.GetBarIdxByX(ChartControl, (int)cursorPt.X);
            int start       = barAtX - windowBars;
            int end         = barAtX + windowBars;

            Tuple<string, DateTime> best = null;

            if (EnableLargeTradeBubbles)
            {
                List<KeyValuePair<Tuple<int, double, int>, Tuple<double, double, DateTime, int>>> near;
                lock (staged)
                    near = committed.Where(k => k.Key.Item1 >= start && k.Key.Item1 <= end).ToList();

                foreach (var kvp in near)
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
                        string extra;
                        bubbleInfo.TryGetValue(kvp.Key, out extra);

                        best = new Tuple<string, DateTime>($"Bid: {fmtBid}\nAsk: {fmtAsk}\nTotal: {fmtTot}\nPrice: {fmtPx}"
                                                           + (string.IsNullOrEmpty(extra) ? "" : "\n" + extra), kvp.Value.Item3);
                    }
                }
            }

            if (best == null && EnableBigTradeZones && bigTradeZones != null)
            {
                double cursorPrice = ChartPanel.Scales[ScaleJustification].GetValueByY((int)cursorPt.Y);
                int    cursorBar   = barAtX;

                lock (bigTradeZones)
                {
                    foreach (var z in bigTradeZones)
                    {
                        if (z.Ticks == null || z.StartBarIdx > cursorBar)
                            continue;

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
                        string status = z.Filled ? "Filled" : z.Touched ? "Touched" : "Untouched";
                        string side   = z.IsAskDom ? "Ask dominant" : "Bid dominant";

                        best = new Tuple<string, DateTime>(
                            $"Zone Top:    {fmtTop}\nZone Bottom: {fmtBot}\nTotal Vol:   {fmtVol}\n{side} | {status}",
                            DateTime.UtcNow);
                        break;
                    }
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
    }
}


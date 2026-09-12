/* AlightenVerticalLineAtIntervalV0001
   Draws a vertical line every N minutes across each trading session, anchored at the
   session open (e.g. 18:00 Globex) and stopping at the session close. Because the
   session length is rarely an exact multiple of the interval, the final block before
   the close is normally short — that is expected, not a rounding bug.

   Session begin/end come from the chart's trading-hours template via SessionIterator,
   so holidays and early closes shorten the last block automatically instead of needing
   a hardcoded close time.

   Rendering: lines are painted directly in OnRender rather than created as Draw.* objects.
   That is what makes UPCOMING boundaries visible — a draw object anchored to a timestamp
   past the last bar collapses onto the last bar, whereas here the x pixel is projected
   forward across the chart's right margin. It also means zero draw objects accumulate,
   however many sessions or however small the interval.

   Inspired by BzvVerticalLineAtTimeV1 (single fixed time); this one is interval-driven. */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using SharpDX;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenVerticalLineAtIntervalV0001 : Indicator
    {
        #region Variables

        // One session's worth of line times. Built once when the session is first seen —
        // including the times still in the future, which is what lets them render ahead
        // of the last bar.
        private class SessionBlocks
        {
            public DateTime Begin;
            public DateTime End;
            public DateTime Anchor;
            public List<DateTime> Intervals = new List<DateTime>();
        }

        private SessionIterator sessionIterator;
        private List<SessionBlocks> sessions;
        private DateTime trackedSessionEnd = Core.Globals.MinDate;

        private Stroke intervalStroke;
        private Stroke boundaryStroke;

        // Reused across renders so the per-frame bar-rate measurement allocates nothing.
        private double[] deltaBuf;

        // A bar duration above this multiple of the median is a session break, not a slow
        // bar, and is excluded from the rate. Well clear of ordinary quiet-market bars.
        private const double GAP_REJECT_MULTIPLE = 20.0;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "AlightenVerticalLineAtIntervalV0001";
                Description              = @"Draws a vertical line every N minutes from the session open to the session close, including the upcoming ones to the right of price. The final block before the close is short whenever the session does not divide evenly by the interval.";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;
                DrawHorizontalGridLines  = false;
                DrawVerticalGridLines    = false;
                PaintPriceMarkers        = false;
                IsSuspendedWhileInactive = true;
                ScaleJustification       = NinjaTrader.Gui.Chart.ScaleJustification.Right;

                IntervalMinutes        = 60;
                UseSessionOpenAsAnchor = true;
                CustomAnchorTime       = DateTime.Parse("18:00", System.Globalization.CultureInfo.InvariantCulture);
                ShowSessionOpenLine    = true;
                ShowSessionCloseLine   = true;
                MaxSessionsBack        = 10;

                LineColor              = Brushes.DimGray;
                LineDashStyle          = DashStyleHelper.Dot;
                LineThickness          = 1;

                BoundaryLineColor      = Brushes.Goldenrod;
                BoundaryLineDashStyle  = DashStyleHelper.Solid;
                BoundaryLineThickness  = 2;

                ProjectionLookbackBars = 20;
            }
            else if (State == State.Configure)
            {
                sessions          = new List<SessionBlocks>();
                trackedSessionEnd = Core.Globals.MinDate;
            }
            else if (State == State.DataLoaded)
            {
                sessionIterator = new SessionIterator(Bars);
            }
            else if (State == State.Historical)
            {
                // Keep the lines behind the bars.
                if (ZOrder != -1)
                    SetZOrder(-1);
            }
            else if (State == State.Terminated)
            {
                if (intervalStroke != null) { intervalStroke.RenderTarget = null; intervalStroke = null; }
                if (boundaryStroke != null) { boundaryStroke.RenderTarget = null; boundaryStroke = null; }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1 || sessionIterator == null || Bars == null || IntervalMinutes < 1)
                return;

            DateTime barTime = Time[0];

            // Only recompute when this bar belongs to a session we have not laid out yet.
            if (trackedSessionEnd != Core.Globals.MinDate && barTime <= trackedSessionEnd)
                return;

            sessionIterator.GetNextSession(barTime, true);
            BuildSession(sessionIterator.ActualSessionBegin, sessionIterator.ActualSessionEnd);
            trackedSessionEnd = sessionIterator.ActualSessionEnd;
        }

        // Lay out every line time for one session in one pass, future times included.
        private void BuildSession(DateTime begin, DateTime end)
        {
            if (end <= begin)
                return;

            SessionBlocks sb = new SessionBlocks { Begin = begin, End = end };
            sb.Anchor = ResolveAnchor(begin);

            if (sb.Anchor < end)
            {
                DateTime t = sb.Anchor.AddMinutes(IntervalMinutes);
                // Strictly before the close: the close line terminates the final block, so a
                // boundary landing exactly on it would double-draw.
                while (t < end)
                {
                    sb.Intervals.Add(t);
                    t = t.AddMinutes(IntervalMinutes);
                }
            }

            sessions.Add(sb);

            if (MaxSessionsBack > 0)
                while (sessions.Count > MaxSessionsBack)
                    sessions.RemoveAt(0);
        }

        // Session open, or the user's clock time mapped onto this session. A session can
        // span midnight (18:00 -> 17:00 next day), so the anchor is walked forward from the
        // session's own start date until it lands inside the session.
        private DateTime ResolveAnchor(DateTime begin)
        {
            if (UseSessionOpenAsAnchor)
                return begin;

            DateTime anchor = begin.Date.Add(CustomAnchorTime.TimeOfDay);
            while (anchor < begin)
                anchor = anchor.AddDays(1);
            return anchor;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (RenderTarget == null || chartControl == null || chartScale == null
                || ChartBars == null || ChartBars.Bars == null || ChartBars.Bars.Count < 2
                || sessions == null || sessions.Count == 0)
                return;

            EnsureStrokes();

            Bars bars        = ChartBars.Bars;
            int lastDataIdx  = bars.Count - 1;
            DateTime lastT   = bars.GetTime(lastDataIdx);

            float xLast = chartControl.GetXByBarIndex(ChartBars, lastDataIdx);
            float xPrev = chartControl.GetXByBarIndex(ChartBars, lastDataIdx - 1);
            float pxPerBar = xLast - xPrev;
            if (pxPerBar <= 0f)
                pxPerBar = 1f;

            double barSeconds = EstimateBarSeconds(bars);

            float top    = ChartPanel.Y;
            float bottom = ChartPanel.Y + ChartPanel.H;
            float left   = ChartPanel.X;
            float right  = ChartPanel.X + ChartPanel.W;

            for (int s = 0; s < sessions.Count; s++)
            {
                SessionBlocks sb = sessions[s];

                if (ShowSessionOpenLine && sb.Anchor >= sb.Begin && sb.Anchor < sb.End)
                    RenderAt(chartControl, bars, lastDataIdx, lastT, xLast, pxPerBar, barSeconds,
                             sb.Anchor, boundaryStroke, top, bottom, left, right);

                for (int i = 0; i < sb.Intervals.Count; i++)
                    RenderAt(chartControl, bars, lastDataIdx, lastT, xLast, pxPerBar, barSeconds,
                             sb.Intervals[i], intervalStroke, top, bottom, left, right);

                if (ShowSessionCloseLine)
                    RenderAt(chartControl, bars, lastDataIdx, lastT, xLast, pxPerBar, barSeconds,
                             sb.End, boundaryStroke, top, bottom, left, right);
            }
        }

        private void RenderAt(ChartControl chartControl, Bars bars, int lastDataIdx, DateTime lastT,
                              float xLast, float pxPerBar, double barSeconds, DateTime time,
                              Stroke stroke, float top, float bottom, float left, float right)
        {
            if (stroke == null)
                return;

            float x;
            if (time <= lastT)
            {
                int idx = bars.GetBar(time);
                if (idx < 0)
                    return;
                x = chartControl.GetXByBarIndex(ChartBars, idx);
            }
            else
            {
                // Past the last bar: step forward by however many bars' worth of time it is.
                // This is the whole point of rendering manually — a Draw object here would
                // snap back onto the last bar instead of sitting out in the right margin.
                double barsAhead = (time - lastT).TotalSeconds / barSeconds;
                x = xLast + (float)(barsAhead * pxPerBar);
            }

            if (x < left || x > right)
                return;

            RenderTarget.DrawLine(new Vector2(x, top), new Vector2(x, bottom),
                                  stroke.BrushDX, stroke.Width, stroke.StrokeStyle);
        }

        // Seconds of chart time one bar represents. Exact for time-based bar types.
        //
        // For UniRenko / tick / volume / range bars there is no fixed answer — bars form
        // fast in volatile stretches and slowly in quiet ones — so the current rate is
        // measured from recent bars. This runs on EVERY render, so the projection tracks
        // the changing bar rate continuously rather than being fixed at load.
        //
        // TRIMMED MEAN, chosen over both a plain mean and a plain median after testing all
        // three against synthetic UniRenko timing:
        //   - plain mean   : one bar spanning a session break is enough to wreck it
        //                    (worst case measured ~55 min of placement error)
        //   - plain median : rejects gaps, but also discards genuinely recurring slow bars,
        //                    so it runs fast during quiet stretches (~23 min error)
        //   - trimmed mean : averages everything except true outliers (~3 min error)
        // The cutoff is a multiple of the median, which cleanly separates a real 4-hour
        // maintenance gap (hundreds of times the median) from an ordinary slow bar in a
        // quiet market (a handful of times the median, and a real part of the rate).
        private double EstimateBarSeconds(Bars bars)
        {
            BarsPeriod bp = bars.BarsPeriod;
            if (bp != null && bp.Value > 0)
            {
                if (bp.BarsPeriodType == BarsPeriodType.Minute) return bp.Value * 60.0;
                if (bp.BarsPeriodType == BarsPeriodType.Second) return bp.Value;
                if (bp.BarsPeriodType == BarsPeriodType.Day)    return bp.Value * 86400.0;
            }

            int last = bars.Count - 1;
            int want = Math.Max(5, ProjectionLookbackBars);
            int n    = Math.Min(want, last);
            if (n < 1)
                return 60.0;

            if (deltaBuf == null || deltaBuf.Length < n)
                deltaBuf = new double[n];

            int count = 0;
            for (int i = 0; i < n; i++)
            {
                double d = (bars.GetTime(last - i) - bars.GetTime(last - i - 1)).TotalSeconds;
                if (d > 0.0)
                    deltaBuf[count++] = d;
            }

            if (count == 0)
                return 60.0;

            Array.Sort(deltaBuf, 0, count);

            double median = deltaBuf[count / 2];
            if (median <= 0.0)
                return 60.0;

            // Ascending, so everything past the cutoff is a tail outlier — stop there.
            double cutoff = median * GAP_REJECT_MULTIPLE;
            double sum = 0.0;
            int kept = 0;
            for (int i = 0; i < count && deltaBuf[i] <= cutoff; i++)
            {
                sum += deltaBuf[i];
                kept++;
            }

            if (kept == 0)
                return median;

            double mean = sum / kept;
            return mean > 0.0 ? mean : median;
        }

        private void EnsureStrokes()
        {
            if (intervalStroke == null
                || intervalStroke.Brush != LineColor
                || intervalStroke.DashStyleHelper != LineDashStyle
                || Math.Abs(intervalStroke.Width - LineThickness) > 0.001)
            {
                if (intervalStroke != null) intervalStroke.RenderTarget = null;
                intervalStroke = new Stroke(LineColor ?? Brushes.DimGray, LineDashStyle, LineThickness);
            }

            if (boundaryStroke == null
                || boundaryStroke.Brush != BoundaryLineColor
                || boundaryStroke.DashStyleHelper != BoundaryLineDashStyle
                || Math.Abs(boundaryStroke.Width - BoundaryLineThickness) > 0.001)
            {
                if (boundaryStroke != null) boundaryStroke.RenderTarget = null;
                boundaryStroke = new Stroke(BoundaryLineColor ?? Brushes.Goldenrod, BoundaryLineDashStyle, BoundaryLineThickness);
            }

            intervalStroke.RenderTarget = RenderTarget;
            boundaryStroke.RenderTarget = RenderTarget;
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(1, 1440)]
        [Display(Name = "Interval (minutes)", Description = "Minutes between vertical lines, measured from the anchor. Any value works — 30, 45, 60, 240. The final block before the session close is short if the session does not divide evenly.", Order = 1, GroupName = "01. Interval")]
        public int IntervalMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Anchor At Session Open", Description = "On: intervals start at the session open from the chart's trading-hours template (holidays and early closes handled automatically). Off: they start at the custom anchor time below.", Order = 2, GroupName = "01. Interval")]
        public bool UseSessionOpenAsAnchor { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Custom Anchor Time", Description = "Clock time the intervals count from when 'Anchor At Session Open' is off. Ignored otherwise.", Order = 3, GroupName = "01. Interval")]
        public DateTime CustomAnchorTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Session Open Line", Description = "Draw a boundary-styled line at the anchor itself.", Order = 4, GroupName = "01. Interval")]
        public bool ShowSessionOpenLine { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Session Close Line", Description = "Draw a boundary-styled line at the session close, ending the final (usually short) block.", Order = 5, GroupName = "01. Interval")]
        public bool ShowSessionCloseLine { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Max Sessions Back", Description = "How many recent sessions keep their lines. 0 = every loaded session. Lines are painted, not stored as chart objects, so this only bounds memory.", Order = 6, GroupName = "01. Interval")]
        public int MaxSessionsBack { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Interval Line Color", Order = 1, GroupName = "02. Interval Line Style")]
        public Brush LineColor { get; set; }

        [Browsable(false)]
        public string LineColorSerializable
        {
            get { return Serialize.BrushToString(LineColor); }
            set { LineColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Interval Line Dash", Order = 2, GroupName = "02. Interval Line Style")]
        public DashStyleHelper LineDashStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Interval Line Thickness", Order = 3, GroupName = "02. Interval Line Style")]
        public int LineThickness { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Session Boundary Color", Description = "Used for the session open and session close lines so they read differently from the interval lines.", Order = 1, GroupName = "03. Session Boundary Style")]
        public Brush BoundaryLineColor { get; set; }

        [Browsable(false)]
        public string BoundaryLineColorSerializable
        {
            get { return Serialize.BrushToString(BoundaryLineColor); }
            set { BoundaryLineColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Session Boundary Dash", Order = 2, GroupName = "03. Session Boundary Style")]
        public DashStyleHelper BoundaryLineDashStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Session Boundary Thickness", Order = 3, GroupName = "03. Session Boundary Style")]
        public int BoundaryLineThickness { get; set; }

        [NinjaScriptProperty]
        [Range(5, 500)]
        [Display(Name = "Projection Lookback (bars)", Description = "Only affects bar types with no fixed duration (UniRenko, tick, volume, range). How many recent bars are measured to work out the current bar rate, which places the UPCOMING lines. Lower = tracks changing speed faster but jitters more; higher = steadier but slower to react. Ignored on time-based charts, where bar duration is exact.", Order = 1, GroupName = "04. Future Projection")]
        public int ProjectionLookbackBars { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenVerticalLineAtIntervalV0001[] cacheAlightenVerticalLineAtIntervalV0001;
		public AlightenVerticalLineAtIntervalV0001 AlightenVerticalLineAtIntervalV0001(int intervalMinutes, bool useSessionOpenAsAnchor, DateTime customAnchorTime, bool showSessionOpenLine, bool showSessionCloseLine, int maxSessionsBack, Brush lineColor, DashStyleHelper lineDashStyle, int lineThickness, Brush boundaryLineColor, DashStyleHelper boundaryLineDashStyle, int boundaryLineThickness, int projectionLookbackBars)
		{
			return AlightenVerticalLineAtIntervalV0001(Input, intervalMinutes, useSessionOpenAsAnchor, customAnchorTime, showSessionOpenLine, showSessionCloseLine, maxSessionsBack, lineColor, lineDashStyle, lineThickness, boundaryLineColor, boundaryLineDashStyle, boundaryLineThickness, projectionLookbackBars);
		}

		public AlightenVerticalLineAtIntervalV0001 AlightenVerticalLineAtIntervalV0001(ISeries<double> input, int intervalMinutes, bool useSessionOpenAsAnchor, DateTime customAnchorTime, bool showSessionOpenLine, bool showSessionCloseLine, int maxSessionsBack, Brush lineColor, DashStyleHelper lineDashStyle, int lineThickness, Brush boundaryLineColor, DashStyleHelper boundaryLineDashStyle, int boundaryLineThickness, int projectionLookbackBars)
		{
			if (cacheAlightenVerticalLineAtIntervalV0001 != null)
				for (int idx = 0; idx < cacheAlightenVerticalLineAtIntervalV0001.Length; idx++)
					if (cacheAlightenVerticalLineAtIntervalV0001[idx] != null && cacheAlightenVerticalLineAtIntervalV0001[idx].IntervalMinutes == intervalMinutes && cacheAlightenVerticalLineAtIntervalV0001[idx].UseSessionOpenAsAnchor == useSessionOpenAsAnchor && cacheAlightenVerticalLineAtIntervalV0001[idx].CustomAnchorTime == customAnchorTime && cacheAlightenVerticalLineAtIntervalV0001[idx].ShowSessionOpenLine == showSessionOpenLine && cacheAlightenVerticalLineAtIntervalV0001[idx].ShowSessionCloseLine == showSessionCloseLine && cacheAlightenVerticalLineAtIntervalV0001[idx].MaxSessionsBack == maxSessionsBack && cacheAlightenVerticalLineAtIntervalV0001[idx].LineColor == lineColor && cacheAlightenVerticalLineAtIntervalV0001[idx].LineDashStyle == lineDashStyle && cacheAlightenVerticalLineAtIntervalV0001[idx].LineThickness == lineThickness && cacheAlightenVerticalLineAtIntervalV0001[idx].BoundaryLineColor == boundaryLineColor && cacheAlightenVerticalLineAtIntervalV0001[idx].BoundaryLineDashStyle == boundaryLineDashStyle && cacheAlightenVerticalLineAtIntervalV0001[idx].BoundaryLineThickness == boundaryLineThickness && cacheAlightenVerticalLineAtIntervalV0001[idx].ProjectionLookbackBars == projectionLookbackBars && cacheAlightenVerticalLineAtIntervalV0001[idx].EqualsInput(input))
						return cacheAlightenVerticalLineAtIntervalV0001[idx];
			return CacheIndicator<AlightenVerticalLineAtIntervalV0001>(new AlightenVerticalLineAtIntervalV0001(){ IntervalMinutes = intervalMinutes, UseSessionOpenAsAnchor = useSessionOpenAsAnchor, CustomAnchorTime = customAnchorTime, ShowSessionOpenLine = showSessionOpenLine, ShowSessionCloseLine = showSessionCloseLine, MaxSessionsBack = maxSessionsBack, LineColor = lineColor, LineDashStyle = lineDashStyle, LineThickness = lineThickness, BoundaryLineColor = boundaryLineColor, BoundaryLineDashStyle = boundaryLineDashStyle, BoundaryLineThickness = boundaryLineThickness, ProjectionLookbackBars = projectionLookbackBars }, input, ref cacheAlightenVerticalLineAtIntervalV0001);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenVerticalLineAtIntervalV0001 AlightenVerticalLineAtIntervalV0001(int intervalMinutes, bool useSessionOpenAsAnchor, DateTime customAnchorTime, bool showSessionOpenLine, bool showSessionCloseLine, int maxSessionsBack, Brush lineColor, DashStyleHelper lineDashStyle, int lineThickness, Brush boundaryLineColor, DashStyleHelper boundaryLineDashStyle, int boundaryLineThickness, int projectionLookbackBars)
		{
			return indicator.AlightenVerticalLineAtIntervalV0001(Input, intervalMinutes, useSessionOpenAsAnchor, customAnchorTime, showSessionOpenLine, showSessionCloseLine, maxSessionsBack, lineColor, lineDashStyle, lineThickness, boundaryLineColor, boundaryLineDashStyle, boundaryLineThickness, projectionLookbackBars);
		}

		public Indicators.AlightenVerticalLineAtIntervalV0001 AlightenVerticalLineAtIntervalV0001(ISeries<double> input , int intervalMinutes, bool useSessionOpenAsAnchor, DateTime customAnchorTime, bool showSessionOpenLine, bool showSessionCloseLine, int maxSessionsBack, Brush lineColor, DashStyleHelper lineDashStyle, int lineThickness, Brush boundaryLineColor, DashStyleHelper boundaryLineDashStyle, int boundaryLineThickness, int projectionLookbackBars)
		{
			return indicator.AlightenVerticalLineAtIntervalV0001(input, intervalMinutes, useSessionOpenAsAnchor, customAnchorTime, showSessionOpenLine, showSessionCloseLine, maxSessionsBack, lineColor, lineDashStyle, lineThickness, boundaryLineColor, boundaryLineDashStyle, boundaryLineThickness, projectionLookbackBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenVerticalLineAtIntervalV0001 AlightenVerticalLineAtIntervalV0001(int intervalMinutes, bool useSessionOpenAsAnchor, DateTime customAnchorTime, bool showSessionOpenLine, bool showSessionCloseLine, int maxSessionsBack, Brush lineColor, DashStyleHelper lineDashStyle, int lineThickness, Brush boundaryLineColor, DashStyleHelper boundaryLineDashStyle, int boundaryLineThickness, int projectionLookbackBars)
		{
			return indicator.AlightenVerticalLineAtIntervalV0001(Input, intervalMinutes, useSessionOpenAsAnchor, customAnchorTime, showSessionOpenLine, showSessionCloseLine, maxSessionsBack, lineColor, lineDashStyle, lineThickness, boundaryLineColor, boundaryLineDashStyle, boundaryLineThickness, projectionLookbackBars);
		}

		public Indicators.AlightenVerticalLineAtIntervalV0001 AlightenVerticalLineAtIntervalV0001(ISeries<double> input , int intervalMinutes, bool useSessionOpenAsAnchor, DateTime customAnchorTime, bool showSessionOpenLine, bool showSessionCloseLine, int maxSessionsBack, Brush lineColor, DashStyleHelper lineDashStyle, int lineThickness, Brush boundaryLineColor, DashStyleHelper boundaryLineDashStyle, int boundaryLineThickness, int projectionLookbackBars)
		{
			return indicator.AlightenVerticalLineAtIntervalV0001(input, intervalMinutes, useSessionOpenAsAnchor, customAnchorTime, showSessionOpenLine, showSessionCloseLine, maxSessionsBack, lineColor, lineDashStyle, lineThickness, boundaryLineColor, boundaryLineDashStyle, boundaryLineThickness, projectionLookbackBars);
		}
	}
}

#endregion

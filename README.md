# Alighten NinjaTrader 8 Indicators

This repository contains a proprietary suite of custom NinjaTrader 8 indicators developed for advanced order flow analysis, multi-timeframe pattern recognition, and chart visualization. The suite is centered around the **Alighten Mirror Dashboard** ecosystem and high-performance order flow engines.

## Production Versions

The highest version number is **not** always the production version — abandoned experiments sometimes carry higher numbers (e.g., `RelativeDeltaMultiTFV0003` was an experiment; `V0002` remained production). This table is the source of truth; update it whenever a version is promoted or retired.

| Indicator family | Production version |
|---|---|
| Mirror Dashboard | `AlightenMirrorV0041` |
| Pattern A source | `AlightenMirrorPtAV0010` |
| Pattern B source | `AlightenMirrorPtBV0005` |
| Pattern G source | `AlightenMirrorPtGV0002` |
| Pattern H source | `AlightenMirrorPtHV0002` |
| Pattern F source | `AlightenMirrorPtFV0003` |
| Pattern J source | `AlightenMirrorPtJV0007` |
| Footprint OrderFlow | `AlightenFootprintOrderFlowV00021` |
| HTF Volume Profile | `AlightenHTFVPV0004` |
| Relative Delta | `AlightenRelativeDeltaV0001` |
| Relative Delta MultiTF | `AlightenRelativeDeltaMultiTFV0002` (V0003 = abandoned experiment) |
| Bias | `AlightenBiasV0003` |
| Bar Timer | `AlightenBarTimerV0004` |
| Button Panel | `AlightenButtonPanelV0005` |

## Repository Structure

The core scripts are located in the `NinjaTrader/Indicators/` directory. Zone rule files — read at
runtime, not compiled — live in `NinjaTrader/ZoneFiles/`.

### 📊 Alighten Mirror Dashboard Ecosystem

The Mirror separates **pattern detection** from **presentation**. Six pattern engines (A, B, G, H,
F, J) each run as a calc-only child indicator, hosted once per timeframe — Daily, 240m, 60m, 30m,
15m, 10m, 5m — so a single primary chart drives 42 detector instances. The Mirror consolidates what
they report into levels, groups those levels into zones, draws everything, and logs it.

#### Levels

A **level** is one pattern engine's output on one timeframe: a price, a direction (long or short),
and a window running from the higher-timeframe bar that produced it to that bar's close. Levels are
keyed `MR_<pattern>_TF<n>_<L|S>_<startTicks>` and held in `tracked[pattern][timeframe]`.

Two behaviours surprise people:

* **Levels sync at their window CLOSE during historical processing**, not while the bar forms. Logic
  that asks "is this level's window open right now" therefore never matches on a reload — which is
  why zones are built from window *overlap* instead.
* **Stale levels are archived, not deleted.** Past the retention floor they move out of `tracked`
  into `_archivedLevels`, keeping their chart drawings and still appearing in exports, but no longer
  costing per-tick scans. Zone rebuilding reads only `tracked`.

#### Zones

A **zone** is a confluence of levels that a rule file asked for. A rule names two or more signals
and a maximum price spread; a zone exists wherever one live level per named signal sits inside that
spread, with all member windows **coexisting in time**.

```
J240L, J60L, J15L; 40T          three long levels — 4H, 1H, 15m — within 40 ticks
A15S, J15S; 20T; ANCHORED       two shorts within 20 ticks, others on the anchor's protected side
```

* **Signal** — pattern letter (A B G H F J) + timeframe (D 240 60 30 15 10 5) + direction (L or S)
* **Spread** — `<n>T`, the maximum distance from the lowest member to the highest
* **ANCHORED** — the highest-timeframe member is the anchor; every other member must sit on its
  protected side (at or below a long anchor, at or above a short)
* **ORDERED** — the listed order is the required price order, lowest first

A zone is identified at the **latest** member window start and ends at the **latest** member end,
computed purely from level records so historical and realtime produce identical zones. Its key is
the rule's index plus that start time, so several member combinations collapse into one zone. When
they disagree the **tightest** combination wins — which means a drawn band is often narrower than
the levels that formed it.

#### Two independent zone sets

Both sets run the same machinery over separate rule files, with their own colours, opacity, merge
behaviour and drawing caps:

| set | property group | rule file | intent |
|---|---|---|---|
| **Primary** | `12. Signal Groups` | `ZoneFiles/MirrorGroupsV0040.txt` | confluence stacks spanning 3+ timeframes |
| **Inside** | `13. Inside Zones` | `ZoneFiles/MirrorInsideZonesV0040.txt` | narrower pairs sitting between a primary zone and price |

Draw tags are prefixed per set (`PRI_` / `INS_`) so the two can never fight over one chart object,
merging never crosses sets, and each writes its own forensics trail — `MirrorZonesV0041_PRI.log`
and `_INS.log` — recording every zone created, extended, erased and purged.

The engine does **not** enforce the inside/outside geometry. It draws two independent sets; whether
an inside zone actually sits between a primary zone and price is a property of how the rules are
written, not something the code checks.

#### Operational notes

Four behaviours that look like bugs and are not:

* **`Max Zone Drawings` / `Max Inside Zone Drawings`** bound how many rectangles each set keeps on
  the chart. Past the cap the oldest drawing is deleted — while the zone stays live in memory. Old
  zones vanishing, most visibly after **Clean Mirror** (which tears everything down and redraws from
  memory), is this cap, not the zone engine. Raise it before suspecting anything else.
* **Clean Mirror does two separate things.** It erases still-live zones so they rebuild on the next
  bar, *and* it permanently dismisses zones sitting on the adverse side of price that never
  "worked" — price never traded through their favourable side. Long-lived 4H-anchored zones rarely
  get stamped as worked, so Clean deletes them; short-lived low-timeframe zones usually do, and
  survive.
* **A missing rule file is not an error.** The loader silently creates a stub containing two example
  rules. A `rules=2` load banner in the zone log means the filename in the settings did not resolve.
* **Rule order matters.** The zone key is built from the rule's *index*, so inserting a rule
  mid-file renumbers every rule after it. Append instead.

#### Export

`Export Mode` suppresses every `Draw.*` call while keeping all level computation, which is what
makes a deep-history run survivable — chart objects are the cost, not the level math. "Export
Levels" then writes a matched set sharing one timestamp:

| file | contents |
|---|---|
| `MirrorLevels_*.csv` | every level with price, window, tag and lifecycle flags |
| `MirrorBars_*.csv` | OHLCV for the primary series and all seven added series |
| `MirrorExport_*.meta.json` | per-series bar counts and first/last timestamps |
| `MirrorResearch_Events/Context_*.csv` | confirmed signals with forward MFE/MAE and confluence context |

`MirrorBars` exists so offline analysis never has to reconstruct NinjaTrader's session boundaries by
resampling — a reconstruction that quietly disagrees with the indicator's own view.

Export depth is governed by the **chart's Days-to-Load**, not by `SrcBarsToProcess`. Note also that
only the Daily/240m/60m/30m/15m series honour `SrcBarsToProcess`; 10m and 5m follow the chart. The
`series` block in the meta sidecar records what each series actually covered.

#### Zone search performance

Candidate levels are price-sorted and cached once per bar per `(pattern, timeframe, direction)`,
shared across every rule and both sets — one scan where the naive version did one per rule
reference. The combination search then pins the scarcest member slot and binary-searches the others
for the `±MaxTicks` window around it. Because every valid combination has all its members within
`MaxTicks` of *any* one member, that window is an **exact** prefilter, verified equivalent to the
full cartesian product on real level data. It is what makes a 100-rule file affordable; the naive
version evaluated over nine million combinations per bar on the same file.

#### Files

* **`AlightenMirrorV0041.cs`** — **current Mirror of record.** V0035 → V0038 → V0039 (exporter) →
  V0040 (two zone sets) → V0041 (zone-search optimisation, per-set drawing caps).
  * Per-pattern and per-timeframe visibility via a live settings modal, per-TF colours, per-pattern
    dash styles, transparent Databox plots for strategies and Bloodhound (`PtAD`…`PtJ5m`).
  * **Daily Bias Levels** — the last N Daily levels from the `AlightenBiasV0003` pivot engine,
    ported inline, coloured by which side the last daily close sits on.
  * **Research Log** — every confirmed signal with confluence context and forward MFE/MAE.
* **`AlightenMirrorPtAV0010.cs`** — Pattern A: ZigZag/level tracking with sequential wick-touch
  signals (consecutive touch bars all signal until the sequence breaks).
* **`AlightenMirrorPtBV0005.cs`** — Pattern B signal engine.
* **`AlightenMirrorPtGV0002.cs`** — Pattern G: level gained/lost arming with wick-retest signals.
* **`AlightenMirrorPtHV0002.cs`** — Pattern H, the "flipped Pattern G": a completed G pattern whose
  level is then lost or gained arms the opposite-direction retest.
* **`AlightenMirrorPtFV0003.cs`** — Pattern F: precise breakouts and immediate retests of structure.
* **`AlightenMirrorPtJV0007.cs`** — Pattern J (paired pivots), the densest source by a wide margin.
  Qualified zigzag pairs with minimum trend bars/ticks, levels at the pivot candle's body, endpoint
  gain/loss state machines with first-touch tests, triangle test markers, and optional
  flip-invalidation of past signals. Serves both standalone chart use and Mirror hosting via
  `PatternJLongLevel` / `PatternJShortLevel` / `PatternJSignal`, including provisional evaluation of
  the forming bar and a Calc-Only mode.

### 🔬 Order Flow & Volume Analytics
High-performance indicators for analyzing Bid/Ask delta, volume nodes, and institutional footprints.

* **`AlightenFootprintOrderFlowV00021.cs`**
  * **Description:** A comprehensive Footprint Indicator that aggregates Bid, Ask, Delta, Volume, Point of Control (POC), and Value Area metrics natively within NinjaTrader. Emits clean data arrays for integration with Bloodhound/strategies.
* **`AlightenHTFVPV0004.cs`**
  * **Description:** Higher Timeframe Volume Profile (HTFVP). Calculates the volume profile of an HTF bar and natively projects its POC and Value Area onto lower timeframe charts for the duration of the subsequent HTF bar. Used heavily for trend bias filtering.
* **`AlightenRelativeDeltaMultiTFV0002.cs`**
  * **Description:** Multi-Timeframe Relative Delta Wick Heatmap. Provides audio alerts when specific delta conditions align across multiple tracked timeframes.
* **`AlightenRelativeDeltaV0001.cs`**
  * **Description:** Relative Delta Footprint visualization — the current production version of the Relative Delta indicator.
* **`VolumeDelta.cs`**
  * **Description:** Core foundational Volume Delta calculation engine.

### 📈 Market Structure & Trend Analysis
* **`AlightenBiasV0003.cs`**
  * **Description:** Evaluates multi-level FTG (Failed To Go) and FTL (Failed To Lower) structures to dynamically determine the current market trend/bias. Its pivot/level engine also powers the Mirror dashboard's Daily Bias Levels (ported inline in AlightenMirrorV0041).
* **`HigherTimeframeCandles.cs`**
  * **Description:** Projects higher-timeframe candlestick boundaries (Open, High, Low, Close) dynamically onto lower-timeframe charts.

### 🛠 UI / UX Utilities
Tools to enhance the NinjaTrader charting experience and streamline live execution.

* **`AlightenBarTimerV0004.cs`**
  * **Description:** An optimized Bar Timer that utilizes the Direct2D `OnRender` pipeline to completely eliminate the flashing/flickering common in standard UI-based bar timers.
* **`AlightenButtonPanelV0005.cs`**
  * **Description:** An interactive on-chart button panel providing quick access to Order Flow parameter controls and execution logic (e.g., "Breakeven + X Ticks").
* **`IndicatorVisualStyleHelper.cs`**
  * **Description:** Centralized helper class for managing uniform visual styling (brushes, strokes, fonts) across the Alighten indicator suite.
* **`LabelRemover.cs`**
  * **Description:** A clean-up utility that automatically removes cluttering text labels from all indicators loaded on a chart.
* **`OrderLineDecorator.cs`**
  * **Description:** Enhances the visual presentation of active order lines on the chart.

---
*Note: Legacy versions and WIP indicators designated with an `@` prefix have been excluded from this index.*
